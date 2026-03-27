#!/usr/bin/env node
'use strict';

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const repoRoot = path.resolve(__dirname, '..');
const unityRoot = path.join(repoRoot, 'unity-client');
const assetsRoot = path.join(unityRoot, 'Assets');
const reportDir = path.join(repoRoot, 'projects');
const reportJsonPath = path.join(reportDir, 'cleanup_audit_report.json');
const reportMdPath = path.join(reportDir, 'cleanup_audit_report.md');

const folderCandidates = [
  'server/client_backup_20260306_233552',
  'server/client_backup_20260306_235052',
  'server/client_backup_20260307_002446',
  'server/client_backup_20260313_154057',
  'server/client_backup_20260313_184750',
  'server/client_backup_20260313_203649',
  'server/client_backup_20260313_204502',
  'server/client_backup_20260313_214135',
  'server/client_backup_20260313_214851',
  'server/client_backup_20260313_220455',
  'unity-client/Assets/_Recovery',
  'unity-client/Assets/Backup enviroments',
  'builds',
  'admin-client',
  'server/admin-client',
  'server/client',
  'unity-client/ServerData',
  'server/migrations',
  'unity-client/Assets/AddressableContent',
  'unity-client/Assets/Resources',
  'archive_pending_deletion',
];

main();

function main() {
  ensureDir(reportDir);

  const buildSettings = parseBuildSettings();
  const addressables = parseAddressableGroups();
  const resources = listFiles('unity-client/Assets/Resources').filter((file) => !file.endsWith('.meta'));
  const rootFolderSizes = folderCandidates.map(buildFolderSummary).filter(Boolean);
  const staleSceneFiles = listFiles('unity-client/Assets')
    .filter((file) =>
      (/InitTestScene.*\.unity$/i.test(file) || /^unity-client\/Assets\/_Recovery\//i.test(file))
      && !file.endsWith('.meta'));
  const missingAssetPaths = findMissingHardcodedAssetPaths();
  const duplicateAdminClient = compareDirectories('admin-client', 'server/admin-client');
  const serverBackupRefs = findTextRefs('server/index.js', ['client_backup_', 'server/admin-client', 'ServerData']);
  const resourcesLoadRefs = findPatternRefs(['unity-client/Assets', 'server'], /Resources\.Load|Resources\.FindObjectsOfTypeAll/g, ['.cs', '.js']);
  const remoteAutomationScripts = listFiles('unity-client/Assets/Scripts/Editor')
    .filter((file) =>
      /(Addressables|RemoteContent|Portrait|Environment|BuildStripper|CommitSceneEnvironmentInstances|PromoteGameEnvironmentEditPreview)/i.test(file));
  const migrationFiles = listFiles('server/migrations').filter((file) => file.endsWith('.sql'));
  const duplicateMigrationPrefixes = findDuplicateMigrationPrefixes(migrationFiles);

  const report = {
    generatedAt: new Date().toISOString(),
    buildScenes: buildSettings,
    addressables,
    resources: {
      count: resources.length,
      files: resources,
    },
    staleSceneFiles,
    missingHardcodedAssetPaths: missingAssetPaths,
    duplicateAdminClient,
    serverBackupRefs,
    resourcesLoadRefs,
    remoteAutomationScripts,
    duplicateMigrationPrefixes,
    folderSizes: rootFolderSizes,
  };

  fs.writeFileSync(reportJsonPath, JSON.stringify(report, null, 2));
  fs.writeFileSync(reportMdPath, renderMarkdown(report));

  process.stdout.write(
    `cleanup audit written:\n- ${relative(reportJsonPath)}\n- ${relative(reportMdPath)}\n`
  );
}

function parseBuildSettings() {
  const file = path.join(unityRoot, 'ProjectSettings', 'EditorBuildSettings.asset');
  const text = safeRead(file);
  const scenes = [];
  const regex = /- enabled: (\d+)\s+path: ([^\r\n]+)\s+guid: ([a-f0-9]+)/g;
  let match;
  while ((match = regex.exec(text)) !== null) {
    scenes.push({
      enabled: match[1] === '1',
      path: match[2].trim(),
      guid: match[3],
      exists: fs.existsSync(path.join(unityRoot, match[2].replace(/^Assets\//, 'Assets/'))),
    });
  }
  return scenes;
}

function parseAddressableGroups() {
  const groupDir = path.join(assetsRoot, 'AddressableAssetsData', 'AssetGroups');
  if (!fs.existsSync(groupDir)) return [];

  return fs.readdirSync(groupDir)
    .filter((name) => name.endsWith('.asset') && !name.includes('_Bundled') && !name.includes('_Content'))
    .map((name) => {
      const full = path.join(groupDir, name);
      const text = safeRead(full);
      const groupNameMatch = text.match(/m_GroupName:\s*(.+)/);
      const entryRegex = /m_GUID:\s*([a-f0-9]+)\s+m_Address:\s*([^\r\n]+)/g;
      const entries = [];
      let entryMatch;
      while ((entryMatch = entryRegex.exec(text)) !== null) {
        entries.push({
          guid: entryMatch[1],
          address: entryMatch[2].trim(),
        });
      }
      return {
        file: relative(full),
        groupName: groupNameMatch ? groupNameMatch[1].trim() : name,
        entryCount: entries.length,
        entries,
      };
    })
    .sort((a, b) => a.groupName.localeCompare(b.groupName));
}

function buildFolderSummary(relPath) {
  const full = path.join(repoRoot, relPath);
  if (!fs.existsSync(full)) return null;

  const files = listFiles(relPath);
  let bytes = 0;
  for (const file of files) {
    const stat = fs.statSync(path.join(repoRoot, file));
    bytes += stat.size;
  }

  return {
    path: relPath.replace(/\\/g, '/'),
    fileCount: files.length,
    sizeMb: round(bytes / (1024 * 1024)),
  };
}

function findMissingHardcodedAssetPaths() {
  const candidates = listFiles('unity-client/Assets/Scripts')
    .filter((file) => file.endsWith('.cs'));
  const findings = [];
  const literalAssetPathRegex = /["'`](Assets\/[^"'`\r\n]+)["'`]/g;

  for (const relFile of candidates) {
    const text = safeRead(path.join(repoRoot, relFile));
    const seen = new Set();
    let match;
    while ((match = literalAssetPathRegex.exec(text)) !== null) {
      const assetPath = match[1].replace(/\\/g, '/').trim();
      if (!seen.add(assetPath)) continue;

      const full = path.join(unityRoot, assetPath.replace(/^Assets\//, 'Assets/'));
      if (!fs.existsSync(full)) {
        findings.push({
          file: relFile.replace(/\\/g, '/'),
          missingAssetPath: assetPath,
        });
      }
    }
    literalAssetPathRegex.lastIndex = 0;
  }

  return findings.sort((a, b) => `${a.missingAssetPath}:${a.file}`.localeCompare(`${b.missingAssetPath}:${b.file}`));
}

function compareDirectories(leftRel, rightRel) {
  const leftFiles = fileMap(leftRel);
  const rightFiles = fileMap(rightRel);
  const allKeys = Array.from(new Set([...leftFiles.keys(), ...rightFiles.keys()])).sort();
  const onlyLeft = [];
  const onlyRight = [];
  const differing = [];

  for (const key of allKeys) {
    const left = leftFiles.get(key);
    const right = rightFiles.get(key);
    if (!left) {
      onlyRight.push(key);
      continue;
    }
    if (!right) {
      onlyLeft.push(key);
      continue;
    }
    if (left.size !== right.size || left.hash !== right.hash) {
      differing.push(key);
    }
  }

  return {
    left: leftRel.replace(/\\/g, '/'),
    right: rightRel.replace(/\\/g, '/'),
    identical: onlyLeft.length === 0 && onlyRight.length === 0 && differing.length === 0,
    onlyLeft,
    onlyRight,
    differing,
  };
}

function fileMap(relDir) {
  const files = listFiles(relDir);
  const map = new Map();
  for (const file of files) {
    const rel = path.relative(path.join(repoRoot, relDir), path.join(repoRoot, file)).replace(/\\/g, '/');
    const full = path.join(repoRoot, file);
    const buf = fs.readFileSync(full);
    map.set(rel, {
      size: buf.length,
      hash: crypto.createHash('sha1').update(buf).digest('hex'),
    });
  }
  return map;
}

function findTextRefs(relFile, needles) {
  const text = safeRead(path.join(repoRoot, relFile));
  const results = [];
  for (const needle of needles) {
    const index = text.indexOf(needle);
    if (index >= 0) {
      results.push(needle);
    }
  }
  return results;
}

function findPatternRefs(searchRoots, regex, exts) {
  const results = [];
  for (const root of searchRoots) {
    for (const relFile of listFiles(root)) {
      if (!exts.includes(path.extname(relFile).toLowerCase())) continue;
      const text = safeRead(path.join(repoRoot, relFile));
      if (regex.test(text)) {
        results.push(relFile.replace(/\\/g, '/'));
      }
      regex.lastIndex = 0;
    }
  }
  return results.sort();
}

function findDuplicateMigrationPrefixes(files) {
  const counts = new Map();
  for (const file of files) {
    const base = path.basename(file);
    const prefix = base.split('_')[0];
    counts.set(prefix, (counts.get(prefix) || 0) + 1);
  }
  return Array.from(counts.entries())
    .filter(([, count]) => count > 1)
    .map(([prefix, count]) => ({ prefix, count }))
    .sort((a, b) => a.prefix.localeCompare(b.prefix));
}

function renderMarkdown(report) {
  const lines = [];
  lines.push('# Cleanup Audit Report');
  lines.push('');
  lines.push(`Generated: \`${report.generatedAt}\``);
  lines.push('');
  lines.push('## Build Scenes');
  lines.push('');
  for (const scene of report.buildScenes) {
    lines.push(`- ${scene.enabled ? '[enabled]' : '[disabled]'} \`${scene.path}\`${scene.exists ? '' : ' (missing file)'}`);
  }
  lines.push('');
  lines.push('## Addressables Groups');
  lines.push('');
  for (const group of report.addressables) {
    lines.push(`- \`${group.groupName}\`: ${group.entryCount} entr${group.entryCount === 1 ? 'y' : 'ies'}`);
  }
  lines.push('');
  lines.push('## Resources');
  lines.push('');
  lines.push(`- Files under \`Assets/Resources\`: ${report.resources.count}`);
  for (const file of report.resources.files) {
    lines.push(`- \`${file}\``);
  }
  lines.push('');
  lines.push('## Missing Hardcoded Asset Paths');
  lines.push('');
  if (report.missingHardcodedAssetPaths.length === 0) {
    lines.push('- None found.');
  } else {
    for (const item of report.missingHardcodedAssetPaths) {
      lines.push(`- \`${item.missingAssetPath}\` referenced by \`${item.file}\``);
    }
  }
  lines.push('');
  lines.push('## Stale Scene-Like Artifacts');
  lines.push('');
  if (report.staleSceneFiles.length === 0) {
    lines.push('- None found.');
  } else {
    for (const file of report.staleSceneFiles) {
      lines.push(`- \`${file}\``);
    }
  }
  lines.push('');
  lines.push('## Duplicate Admin Client Check');
  lines.push('');
  lines.push(`- \`${report.duplicateAdminClient.left}\` vs \`${report.duplicateAdminClient.right}\`: ${report.duplicateAdminClient.identical ? 'identical' : 'different'}`);
  for (const file of report.duplicateAdminClient.onlyLeft) {
    lines.push(`- only in left: \`${file}\``);
  }
  for (const file of report.duplicateAdminClient.onlyRight) {
    lines.push(`- only in right: \`${file}\``);
  }
  for (const file of report.duplicateAdminClient.differing) {
    lines.push(`- differs: \`${file}\``);
  }
  lines.push('');
  lines.push('## Remote Automation Scripts');
  lines.push('');
  for (const file of report.remoteAutomationScripts) {
    lines.push(`- \`${file}\``);
  }
  lines.push('');
  lines.push('## Folder Sizes');
  lines.push('');
  for (const item of report.folderSizes.sort((a, b) => b.sizeMb - a.sizeMb)) {
    lines.push(`- \`${item.path}\`: ${item.sizeMb} MB across ${item.fileCount} files`);
  }
  lines.push('');
  lines.push('## Migration Prefix Collisions');
  lines.push('');
  if (report.duplicateMigrationPrefixes.length === 0) {
    lines.push('- None found.');
  } else {
    for (const item of report.duplicateMigrationPrefixes) {
      lines.push(`- prefix \`${item.prefix}\` appears ${item.count} times`);
    }
  }
  lines.push('');
  lines.push('## Runtime/Editor Reference Buckets');
  lines.push('');
  lines.push(`- Server bootstrap text references: ${report.serverBackupRefs.length ? report.serverBackupRefs.map((item) => `\`${item}\``).join(', ') : 'none'}`);
  lines.push(`- Files using \`Resources.*\`: ${report.resourcesLoadRefs.length ? report.resourcesLoadRefs.map((item) => `\`${item}\``).join(', ') : 'none'}`);
  lines.push('');
  return lines.join('\n');
}

function listFiles(relDir) {
  const full = path.join(repoRoot, relDir);
  if (!fs.existsSync(full)) return [];

  const out = [];
  walk(full, (file) => out.push(relative(file)));
  return out.sort();
}

function walk(dir, onFile) {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, onFile);
      continue;
    }
    onFile(full);
  }
}

function safeRead(file) {
  try {
    return fs.readFileSync(file, 'utf8');
  } catch {
    return '';
  }
}

function ensureDir(dir) {
  fs.mkdirSync(dir, { recursive: true });
}

function relative(full) {
  return path.relative(repoRoot, full).replace(/\\/g, '/');
}

function round(value) {
  return Math.round(value * 100) / 100;
}
