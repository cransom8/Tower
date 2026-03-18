'use strict';

const { Pool } = require('pg');

function makePool(url) {
  const isLocal = (url || '').includes('localhost') || (url || '').includes('127.0.0.1');
  return new Pool({
    connectionString: url,
    ssl: isLocal ? false : { rejectUnauthorized: process.env.DATABASE_SSL_REJECT_UNAUTHORIZED !== 'false' },
  });
}

async function scalar(pool, sql, params = []) {
  const { rows } = await pool.query(sql, params);
  if (!rows.length) return null;
  const row = rows[0];
  const key = Object.keys(row)[0];
  return row[key];
}

async function rows(pool, sql, params = []) {
  const result = await pool.query(sql, params);
  return result.rows;
}

async function getSchemaFingerprint(pool) {
  const tables = await rows(pool, `
    select table_name
    from information_schema.tables
    where table_schema = 'public'
    order by table_name
  `);

  const columns = await rows(pool, `
    select table_name, column_name, data_type
    from information_schema.columns
    where table_schema = 'public'
    order by table_name, ordinal_position
  `);

  const tableCounts = {};
  for (const table of [
    'players',
    'unit_types',
    'barracks_levels',
    'game_configs',
    'feature_flags',
    'skins',
    'loadouts',
    'ml_wave_configs',
  ]) {
    try {
      tableCounts[table] = await scalar(pool, `select count(*)::int from ${table}`);
    } catch {
      tableCounts[table] = null;
    }
  }

  const unitTypes = await rows(pool, `
    select key, name, build_cost, income
    from unit_types
    order by key
  `).catch(() => []);

  const barracks = await rows(pool, `
    select level, upgrade_cost, multiplier
    from barracks_levels
    order by level
  `).catch(() => []);

  const flags = await rows(pool, `
    select name, enabled
    from feature_flags
    order by name
  `).catch(() => []);

  const waveConfigs = await rows(pool, `
    select config_id, count(*)::int as wave_count
    from ml_wave_configs
    group by config_id
    order by config_id
  `).catch(() => []);

  return {
    tables: tables.map((r) => r.table_name),
    columns: columns.map((r) => `${r.table_name}.${r.column_name}:${r.data_type}`),
    tableCounts,
    unitTypes,
    barracks,
    flags,
    waveConfigs,
  };
}

function diffList(label, left, right) {
  const leftOnly = left.filter((x) => !right.includes(x));
  const rightOnly = right.filter((x) => !left.includes(x));
  return { label, leftOnly, rightOnly, matches: leftOnly.length === 0 && rightOnly.length === 0 };
}

function diffJsonByKey(label, left, right, keyFn) {
  const leftMap = new Map(left.map((item) => [keyFn(item), JSON.stringify(item)]));
  const rightMap = new Map(right.map((item) => [keyFn(item), JSON.stringify(item)]));
  const allKeys = [...new Set([...leftMap.keys(), ...rightMap.keys()])].sort();
  const mismatches = [];

  for (const key of allKeys) {
    const l = leftMap.get(key);
    const r = rightMap.get(key);
    if (l !== r) {
      mismatches.push({
        key,
        left: l ? JSON.parse(l) : null,
        right: r ? JSON.parse(r) : null,
      });
    }
  }

  return { label, mismatches, matches: mismatches.length === 0 };
}

async function main() {
  const leftUrl = process.env.LEFT_DATABASE_URL || process.env.DATABASE_URL;
  const rightUrl = process.env.RIGHT_DATABASE_URL || process.env.PROD_DATABASE_URL;

  if (!leftUrl || !rightUrl) {
    console.error('[compare-db] Set LEFT_DATABASE_URL and RIGHT_DATABASE_URL (or DATABASE_URL and PROD_DATABASE_URL).');
    process.exit(1);
  }

  const leftPool = makePool(leftUrl);
  const rightPool = makePool(rightUrl);

  try {
    const [left, right] = await Promise.all([
      getSchemaFingerprint(leftPool),
      getSchemaFingerprint(rightPool),
    ]);

    const report = {
      tables: diffList('tables', left.tables, right.tables),
      columns: diffList('columns', left.columns, right.columns),
      tableCounts: {
        matches: JSON.stringify(left.tableCounts) === JSON.stringify(right.tableCounts),
        left: left.tableCounts,
        right: right.tableCounts,
      },
      unitTypes: diffJsonByKey('unit_types', left.unitTypes, right.unitTypes, (x) => x.key),
      barracks: diffJsonByKey('barracks_levels', left.barracks, right.barracks, (x) => String(x.level)),
      flags: diffJsonByKey('feature_flags', left.flags, right.flags, (x) => x.name),
      waveConfigs: diffJsonByKey('ml_wave_configs', left.waveConfigs, right.waveConfigs, (x) => String(x.config_id)),
    };

    console.log(JSON.stringify(report, null, 2));
  } finally {
    await leftPool.end();
    await rightPool.end();
  }
}

main().catch((err) => {
  console.error('[compare-db] fatal:', err && (err.stack || err.message || String(err)));
  process.exit(1);
});
