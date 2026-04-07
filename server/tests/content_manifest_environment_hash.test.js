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

test('buildContentManifest prefers skin remote metadata for skin-backed unit variants', () => {
  const manifest = buildContentManifest({
    unitTypes: [
      {
        key: 'tt_peasant',
        name: 'Peasant',
        enabled: true,
        usage_scope: 'both',
        canonical_unit_type: 'goblin',
        remote_content: {
          content_key: 'tt_peasant',
          enabled: true,
          tier: 't1',
        },
      },
    ],
    skins: [
      {
        skin_key: 'tt_peasant',
        unit_type: 'goblin',
        name: 'Peasant',
        enabled: true,
        remote_content: {
          content_key: 'tt_peasant',
          addressables_label: 'tt_peasant',
          prefab_address: 'skins/tt_peasant',
          enabled: true,
          tier: 't1',
        },
      },
    ],
  });

  const matchingEntries = manifest.loadout_critical_content.filter(
    (entry) => entry && entry.content_key === 'tt_peasant');
  assert.equal(matchingEntries.length, 1);
  assert.equal(matchingEntries[0].kind, 'skin');
  assert.equal(matchingEntries[0].address, 'skins/tt_peasant');

  const unitVariantEntry = manifest.loadout_critical_content.find(
    (entry) => entry && entry.kind === 'unit' && entry.key === 'tt_peasant');
  assert.equal(unitVariantEntry, undefined);
});
