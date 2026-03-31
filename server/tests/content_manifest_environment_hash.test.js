const test = require('node:test');
const assert = require('node:assert/strict');

const { buildContentManifest } = require('../remoteContent');

test('buildContentManifest carries environment content hash into wave-critical manifest entries', () => {
  const manifest = buildContentManifest({
    environmentEntries: [
      {
        key: 'game_ml',
        content_key: 'environment_game_ml',
        content_hash: 'layout-hash-123',
        tier: 't1',
        address: 'environment/game_ml',
        reason: 'Required for authoritative gameplay.',
      },
    ],
  });

  const envWaveEntry = manifest.wave_critical_content.find(
    (entry) => entry && entry.kind === 'environment' && entry.address === 'environment/game_ml');
  const envT1Entry = manifest.t1_content.find(
    (entry) => entry && entry.kind === 'environment' && entry.address === 'environment/game_ml');

  assert.ok(envWaveEntry, 'wave_critical_content should include the environment entry');
  assert.ok(envT1Entry, 't1_content should include the environment entry');
  assert.equal(envWaveEntry.content_hash, 'layout-hash-123');
  assert.equal(envT1Entry.content_hash, 'layout-hash-123');
});
