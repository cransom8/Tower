'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');

const {
  parseLegalMarkdown,
  renderLegalMarkdownPage,
  renderPrivacyPolicyPage,
} = require('../legalPages');

test('parseLegalMarkdown keeps headings, bullet lists, and multiline paragraphs distinct', () => {
  const blocks = parseLegalMarkdown([
    '# Sample Policy',
    '',
    'Effective Date: April 4, 2026',
    '',
    '## 1. Scope',
    '',
    'We collect:',
    '- Email',
    '- Display name',
    '',
    'Contact:',
    '',
    'RansomForge, LLC  ',
    'support@ransomforge.com',
  ].join('\n'));

  assert.deepEqual(blocks, [
    { type: 'h1', text: 'Sample Policy' },
    { type: 'p', lines: ['Effective Date: April 4, 2026'] },
    { type: 'h2', text: '1. Scope' },
    { type: 'p', lines: ['We collect:'] },
    { type: 'ul', items: ['Email', 'Display name'] },
    { type: 'p', lines: ['Contact:'] },
    { type: 'p', lines: ['RansomForge, LLC', 'support@ransomforge.com'] },
  ]);
});

test('renderLegalMarkdownPage linkifies raw URLs and email addresses', () => {
  const html = renderLegalMarkdownPage({
    markdown: [
      '# Sample Policy',
      '',
      'Effective Date: April 4, 2026',
      '',
      'Visit https://app.ransomforge.com.',
      '',
      'Contact:',
      '',
      'support@ransomforge.com',
    ].join('\n'),
    canonicalPath: '/privacy',
    titlePrefix: 'Sample Policy',
  });

  assert.match(html, /<title>Sample Policy<\/title>/);
  assert.match(html, /href="https:\/\/app\.ransomforge\.com" rel="noreferrer">https:\/\/app\.ransomforge\.com<\/a>\./);
  assert.match(html, /href="mailto:support@ransomforge\.com">support@ransomforge\.com<\/a>/);
  assert.match(html, /<link rel="canonical" href="https:\/\/app\.ransomforge\.com\/privacy">/);
});

test('renderPrivacyPolicyPage includes the current privacy policy title and contact block', () => {
  const html = renderPrivacyPolicyPage();

  assert.match(html, /RansomForge Privacy Policy/);
  assert.match(html, /Effective Date: March 3, 2026/);
  assert.match(html, /<h2>11\. Contact Us<\/h2>/);
  assert.match(html, /support@ransomforge\.com/);
});
