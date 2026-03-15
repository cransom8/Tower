'use strict';

require('dotenv').config();

const fs = require('fs');
const path = require('path');
const db = require('../db');
const unitTypes = require('../unitTypes');

function getArg(name) {
  const prefix = `--${name}=`;
  const raw = process.argv.find((arg) => arg.startsWith(prefix));
  return raw ? raw.slice(prefix.length).trim() : '';
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

async function upsertUnit(entry) {
  const key = String(entry.key || '').trim();
  if (!key) return false;

  const exists = await db.query('SELECT id FROM unit_types WHERE key = $1', [key]);
  if (!exists.rows[0]) {
    console.warn(`[import-remote-content] skipping unknown unit key '${key}'`);
    return false;
  }

  const remote = normalizeRemote(entry, true);
  await db.query(
    `INSERT INTO unit_content_metadata (
       unit_type_id, content_key, addressables_label, prefab_address,
       dependency_keys, version_tag, is_critical, enabled, updated_at
     ) VALUES (
       $1, $2, $3, $4,
       COALESCE($5, '[]'::jsonb), COALESCE($6, '1'), COALESCE($7, TRUE), TRUE, NOW()
     )
     ON CONFLICT (unit_type_id) DO UPDATE SET
       content_key = EXCLUDED.content_key,
       addressables_label = EXCLUDED.addressables_label,
       prefab_address = EXCLUDED.prefab_address,
       dependency_keys = EXCLUDED.dependency_keys,
       version_tag = EXCLUDED.version_tag,
       is_critical = EXCLUDED.is_critical,
       enabled = EXCLUDED.enabled,
       updated_at = NOW()`,
    [
      exists.rows[0].id,
      remote.content_key,
      remote.addressables_label,
      remote.prefab_address,
      JSON.stringify(remote.dependency_keys),
      remote.version_tag,
      remote.is_critical,
    ]
  );
  return true;
}

async function upsertSkin(entry) {
  const key = String(entry.key || '').trim();
  if (!key) return false;

  const exists = await db.query('SELECT id FROM skin_catalog WHERE skin_key = $1', [key]);
  if (!exists.rows[0]) {
    console.warn(`[import-remote-content] skipping unknown skin key '${key}'`);
    return false;
  }

  const remote = normalizeRemote(entry, false);
  await db.query(
    `INSERT INTO skin_content_metadata (
       skin_catalog_id, content_key, addressables_label, prefab_address,
       dependency_keys, version_tag, is_critical, enabled, updated_at
     ) VALUES (
       $1, $2, $3, $4,
       COALESCE($5, '[]'::jsonb), COALESCE($6, '1'), COALESCE($7, FALSE), TRUE, NOW()
     )
     ON CONFLICT (skin_catalog_id) DO UPDATE SET
       content_key = EXCLUDED.content_key,
       addressables_label = EXCLUDED.addressables_label,
       prefab_address = EXCLUDED.prefab_address,
       dependency_keys = EXCLUDED.dependency_keys,
       version_tag = EXCLUDED.version_tag,
       is_critical = EXCLUDED.is_critical,
       enabled = EXCLUDED.enabled,
       updated_at = NOW()`,
    [
      exists.rows[0].id,
      remote.content_key,
      remote.addressables_label,
      remote.prefab_address,
      JSON.stringify(remote.dependency_keys),
      remote.version_tag,
      remote.is_critical,
    ]
  );
  return true;
}

function normalizeRemote(entry, isCriticalDefault) {
  return {
    content_key: entry.recommendedContentKey || entry.key,
    addressables_label: entry.recommendedAddressablesLabel || entry.key,
    prefab_address: entry.recommendedPrefabAddress || entry.assetPath || null,
    dependency_keys: Array.isArray(entry.dependencyKeys) ? entry.dependencyKeys : [],
    version_tag: entry.versionTag || '1',
    is_critical: entry.isCritical !== undefined ? !!entry.isCritical : !!isCriticalDefault,
  };
}

async function main() {
  if (!process.env.DATABASE_URL) fail('DATABASE_URL is not configured.');

  const reportArg = getArg('report') || 'docs/../unity-client/Assets/Reports/remote_content_audit.json';
  const reportPath = path.resolve(process.cwd(), reportArg);
  if (!fs.existsSync(reportPath)) fail(`Report file not found: ${reportPath}`);

  const parsed = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
  const units = Array.isArray(parsed.units) ? parsed.units : [];
  const skins = Array.isArray(parsed.skins) ? parsed.skins : [];

  let importedUnits = 0;
  let importedSkins = 0;

  for (const entry of units) {
    if (await upsertUnit(entry)) importedUnits++;
  }

  for (const entry of skins) {
    if (await upsertSkin(entry)) importedSkins++;
  }

  await unitTypes.loadUnitTypes(db).catch(() => {});
  console.log(JSON.stringify({
    importedUnits,
    importedSkins,
    reportPath,
  }, null, 2));
}

main()
  .catch((err) => {
    console.error(err.message || String(err));
    process.exit(1);
  })
  .finally(async () => {
    try { await db.pool.end(); } catch {}
  });
