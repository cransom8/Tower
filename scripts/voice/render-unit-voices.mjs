import fs from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, "..", "..");
const manifestPath = path.join(__dirname, "unit-voice-manifest.json");

const args = parseArgs(process.argv.slice(2));
const envFromFile = await loadRepoEnv();
const apiKey = process.env.OPENAI_API_KEY || envFromFile.OPENAI_API_KEY || null;

if (!apiKey && !args.dryRun) {
  console.error("OPENAI_API_KEY is missing. Set it in your shell before running the voice generator.");
  process.exit(1);
}

const manifest = JSON.parse(await fs.readFile(manifestPath, "utf8"));
const selectedProfiles = new Set(args.profileFilter);
const selectedCues = new Set(args.cueFilter);

let generatedCount = 0;
let skippedCount = 0;

for (const profile of manifest.profiles) {
  if (selectedProfiles.size > 0 && !selectedProfiles.has(profile.key)) {
    continue;
  }

  for (const [cue, clips] of Object.entries(profile.clips || {})) {
    if (selectedCues.size > 0 && !selectedCues.has(cue)) {
      continue;
    }

    for (const clip of clips) {
      const outputRelativePath = clip.output || `${profile.key}/${cue}/${clip.id}.wav`;
      const outputPath = path.join(repoRoot, manifest.outputRoot, outputRelativePath);
      const instructions = [manifest.defaultInstructions, profile.instructions, clip.instructions]
        .filter(Boolean)
        .join(" ");

      if (!args.force && await fileExists(outputPath)) {
        skippedCount++;
        console.log(`skip ${outputRelativePath}`);
        continue;
      }

      if (args.dryRun) {
        generatedCount++;
        console.log(`dry-run ${outputRelativePath}`);
        continue;
      }

      await fs.mkdir(path.dirname(outputPath), { recursive: true });

      const response = await fetch(manifest.endpoint, {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`,
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          model: clip.model || profile.model || manifest.model,
          voice: clip.voice || profile.voice || manifest.voice,
          input: clip.text,
          instructions: instructions || undefined,
          response_format: clip.responseFormat || profile.responseFormat || manifest.responseFormat || "wav",
          speed: clip.speed || profile.speed || manifest.speed || 1
        })
      });

      if (!response.ok) {
        const errorText = await response.text();
        throw new Error(`Failed to generate ${outputRelativePath}: ${response.status} ${response.statusText}\n${errorText}`);
      }

      const arrayBuffer = await response.arrayBuffer();
      await fs.writeFile(outputPath, Buffer.from(arrayBuffer));
      generatedCount++;
      console.log(`wrote ${outputRelativePath}`);
    }
  }
}

console.log(`voice generation complete: generated=${generatedCount} skipped=${skippedCount}`);

function parseArgs(argv) {
  const result = {
    force: false,
    dryRun: false,
    profileFilter: [],
    cueFilter: []
  };

  for (const arg of argv) {
    if (arg === "--force") {
      result.force = true;
      continue;
    }

    if (arg === "--dry-run") {
      result.dryRun = true;
      continue;
    }

    if (arg.startsWith("--profile=")) {
      result.profileFilter.push(...splitCsv(arg.slice("--profile=".length)));
      continue;
    }

    if (arg.startsWith("--cue=")) {
      result.cueFilter.push(...splitCsv(arg.slice("--cue=".length)));
      continue;
    }
  }

  return result;
}

function splitCsv(value) {
  return String(value || "")
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean);
}

async function fileExists(targetPath) {
  try {
    await fs.access(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function loadRepoEnv() {
  const result = {};
  for (const fileName of [".env.local", ".env"]) {
    const filePath = path.join(repoRoot, fileName);
    if (!await fileExists(filePath)) {
      continue;
    }

    const text = await fs.readFile(filePath, "utf8");
    for (const rawLine of text.split(/\r?\n/)) {
      const line = String(rawLine || "").trim();
      if (!line || line.startsWith("#")) {
        continue;
      }

      const separatorIndex = line.indexOf("=");
      if (separatorIndex <= 0) {
        continue;
      }

      const key = line.slice(0, separatorIndex).trim();
      if (!key || key in result) {
        continue;
      }

      let value = line.slice(separatorIndex + 1).trim();
      if ((value.startsWith("\"") && value.endsWith("\"")) || (value.startsWith("'") && value.endsWith("'"))) {
        value = value.slice(1, -1);
      }

      result[key] = value;
    }
  }

  return result;
}
