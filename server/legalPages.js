'use strict';

const fs = require('fs');
const path = require('path');

const PRIVACY_POLICY_PATH = path.join(__dirname, '..', 'PRIVACY_POLICY.md');

const LEGAL_PAGE_STYLES = `
:root {
  --bg: #0a0d12;
  --panel: rgba(16, 20, 29, 0.94);
  --panel-border: rgba(201, 158, 65, 0.18);
  --text: #e5dcc6;
  --muted: #b1a48a;
  --accent: #d8b05a;
  --accent-soft: rgba(216, 176, 90, 0.16);
  --link: #8dc7ff;
  --shadow: rgba(0, 0, 0, 0.45);
}

* {
  box-sizing: border-box;
}

html {
  color-scheme: dark;
}

body {
  margin: 0;
  min-height: 100vh;
  color: var(--text);
  background:
    radial-gradient(circle at top, rgba(128, 52, 22, 0.28), transparent 32%),
    radial-gradient(circle at 18% 12%, rgba(216, 176, 90, 0.1), transparent 28%),
    linear-gradient(180deg, #1a1210 0%, #0a0d12 38%, #06080c 100%);
  font-family: "Trebuchet MS", "Gill Sans MT", sans-serif;
}

a {
  color: var(--link);
}

.legal-shell {
  width: min(100%, 1000px);
  margin: 0 auto;
  padding: 32px 16px 56px;
}

.legal-card {
  position: relative;
  overflow: hidden;
  background: linear-gradient(180deg, rgba(22, 27, 38, 0.97), rgba(11, 14, 22, 0.97));
  border: 1px solid var(--panel-border);
  border-radius: 20px;
  box-shadow: 0 24px 80px var(--shadow);
}

.legal-card::before {
  content: "";
  position: absolute;
  inset: 0 0 auto 0;
  height: 5px;
  background: linear-gradient(90deg, rgba(216, 176, 90, 0), rgba(216, 176, 90, 0.95), rgba(216, 176, 90, 0));
}

.legal-header,
.legal-body {
  position: relative;
  z-index: 1;
}

.legal-header {
  padding: 28px 28px 22px;
  border-bottom: 1px solid rgba(216, 176, 90, 0.12);
  background:
    radial-gradient(circle at top right, rgba(216, 176, 90, 0.12), transparent 28%),
    linear-gradient(180deg, rgba(216, 176, 90, 0.08), rgba(216, 176, 90, 0));
}

.legal-eyebrow {
  display: inline-block;
  margin: 0 0 12px;
  padding: 6px 10px;
  border-radius: 999px;
  background: var(--accent-soft);
  color: var(--accent);
  font-size: 0.74rem;
  font-weight: 700;
  letter-spacing: 0.16em;
  text-transform: uppercase;
}

.legal-back-link {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 18px;
  color: var(--accent);
  text-decoration: none;
  font-size: 0.95rem;
}

.legal-back-link:hover,
.legal-back-link:focus-visible {
  text-decoration: underline;
}

.legal-title {
  margin: 0;
  color: #f7ebcf;
  font-family: Georgia, "Palatino Linotype", serif;
  font-size: clamp(2rem, 4vw, 3.2rem);
  line-height: 1.05;
  letter-spacing: 0.02em;
}

.legal-body {
  padding: 28px;
}

.legal-meta {
  margin: 0 0 22px;
  color: var(--muted);
  font-size: 0.95rem;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.legal-body h2,
.legal-body h3 {
  color: var(--accent);
  font-family: Georgia, "Palatino Linotype", serif;
  line-height: 1.15;
}

.legal-body h2 {
  margin: 30px 0 10px;
  font-size: 1.55rem;
}

.legal-body h3 {
  margin: 22px 0 8px;
  font-size: 1.14rem;
}

.legal-body p,
.legal-body li {
  font-size: 1.03rem;
  line-height: 1.65;
}

.legal-body p {
  margin: 0 0 16px;
}

.legal-body ul {
  margin: 0 0 18px;
  padding-left: 22px;
}

.legal-contact {
  display: inline-block;
  margin-top: 4px;
  padding: 14px 16px;
  border-radius: 14px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(216, 176, 90, 0.14);
  white-space: nowrap;
}

@media (max-width: 640px) {
  .legal-shell {
    padding: 18px 12px 32px;
  }

  .legal-header,
  .legal-body {
    padding: 20px 18px;
  }

  .legal-contact {
    white-space: normal;
  }
}
`;

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}

function linkifyEscapedText(text) {
  return text
    .replace(/https?:\/\/[^\s<]+/g, (match) => {
      const trailing = match.match(/[),.;:!?]+$/);
      const suffix = trailing ? trailing[0] : '';
      const href = suffix ? match.slice(0, -suffix.length) : match;
      return `<a href="${href}" rel="noreferrer">${href}</a>${suffix}`;
    })
    .replace(/\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b/gi, (match) => {
      return `<a href="mailto:${match}">${match}</a>`;
    });
}

function renderInlineMarkdown(text) {
  return linkifyEscapedText(escapeHtml(text.trim()));
}

function isHeadingLine(line) {
  return line.startsWith('# ') || line.startsWith('## ') || line.startsWith('### ');
}

function parseLegalMarkdown(markdown) {
  const lines = String(markdown).replace(/\r\n/g, '\n').split('\n');
  const blocks = [];

  for (let index = 0; index < lines.length;) {
    const line = lines[index];
    const trimmed = line.trim();

    if (!trimmed) {
      index += 1;
      continue;
    }

    if (line.startsWith('### ')) {
      blocks.push({ type: 'h3', text: line.slice(4).trim() });
      index += 1;
      continue;
    }

    if (line.startsWith('## ')) {
      blocks.push({ type: 'h2', text: line.slice(3).trim() });
      index += 1;
      continue;
    }

    if (line.startsWith('# ')) {
      blocks.push({ type: 'h1', text: line.slice(2).trim() });
      index += 1;
      continue;
    }

    if (line.startsWith('- ')) {
      const items = [];
      while (index < lines.length && lines[index].startsWith('- ')) {
        items.push(lines[index].slice(2).trim());
        index += 1;
      }
      blocks.push({ type: 'ul', items });
      continue;
    }

    const paragraphLines = [];
    while (
      index < lines.length &&
      lines[index].trim() &&
      !lines[index].startsWith('- ') &&
      !isHeadingLine(lines[index])
    ) {
      paragraphLines.push(lines[index].replace(/\s+$/, ''));
      index += 1;
    }

    blocks.push({ type: 'p', lines: paragraphLines });
  }

  return blocks;
}

function renderParagraph(block, isMeta) {
  const paragraphHtml = block.lines.map((line) => renderInlineMarkdown(line)).join('<br>\n');
  if (isMeta) {
    return `<p class="legal-meta">${paragraphHtml}</p>`;
  }
  if (block.lines.length > 1) {
    return `<p class="legal-contact">${paragraphHtml}</p>`;
  }
  return `<p>${paragraphHtml}</p>`;
}

function renderLegalMarkdownPage({
  markdown,
  canonicalPath,
  titlePrefix,
  eyebrow = 'Legal',
}) {
  const blocks = parseLegalMarkdown(markdown);
  const titleBlock = blocks.find((block) => block.type === 'h1');
  const title = titleBlock ? titleBlock.text : titlePrefix;
  const contentBlocks = blocks.filter((block) => block !== titleBlock);

  const bodyHtml = contentBlocks.map((block, index) => {
    if (block.type === 'h2') return `<h2>${renderInlineMarkdown(block.text)}</h2>`;
    if (block.type === 'h3') return `<h3>${renderInlineMarkdown(block.text)}</h3>`;
    if (block.type === 'ul') {
      const items = block.items.map((item) => `<li>${renderInlineMarkdown(item)}</li>`).join('');
      return `<ul>${items}</ul>`;
    }
    if (block.type === 'p') {
      const firstParagraph = index === 0 && block.lines.length === 1 && /^Effective Date:/i.test(block.lines[0]);
      return renderParagraph(block, firstParagraph);
    }
    return '';
  }).join('\n');

  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(title)}</title>
  <meta name="description" content="${escapeHtml(titlePrefix)}">
  <link rel="canonical" href="https://app.ransomforge.com${canonicalPath}">
  <style>${LEGAL_PAGE_STYLES}</style>
</head>
<body>
  <main class="legal-shell">
    <section class="legal-card">
      <header class="legal-header">
        <a class="legal-back-link" href="/">&#8592; Back to RansomForge</a>
        <div class="legal-eyebrow">${escapeHtml(eyebrow)}</div>
        <h1 class="legal-title">${escapeHtml(title)}</h1>
      </header>
      <article class="legal-body">
${bodyHtml}
      </article>
    </section>
  </main>
</body>
</html>`;
}

function renderPrivacyPolicyPage() {
  const markdown = fs.readFileSync(PRIVACY_POLICY_PATH, 'utf8');
  return renderLegalMarkdownPage({
    markdown,
    canonicalPath: '/privacy',
    titlePrefix: 'RansomForge Privacy Policy',
    eyebrow: 'Privacy Policy',
  });
}

module.exports = {
  PRIVACY_POLICY_PATH,
  parseLegalMarkdown,
  renderLegalMarkdownPage,
  renderPrivacyPolicyPage,
};
