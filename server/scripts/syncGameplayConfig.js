'use strict';

const { Pool } = require('pg');

function makePool(url) {
  const isLocal = (url || '').includes('localhost') || (url || '').includes('127.0.0.1');
  return new Pool({
    connectionString: url,
    ssl: isLocal ? false : { rejectUnauthorized: process.env.DATABASE_SSL_REJECT_UNAUTHORIZED !== 'false' },
  });
}

async function getColumns(pool, table) {
  const { rows } = await pool.query(`
    select column_name
    from information_schema.columns
    where table_schema = 'public' and table_name = $1
    order by ordinal_position
  `, [table]);
  return rows.map((r) => r.column_name);
}

function quoteIdent(name) {
  return `"${String(name).replace(/"/g, '""')}"`;
}

async function syncUnitTypes(sourcePool, targetPool) {
  const columns = await getColumns(sourcePool, 'unit_types');
  const { rows } = await sourcePool.query(`
    select ${columns.map(quoteIdent).join(', ')}
    from unit_types
    order by key
  `);

  const insertCols = columns;
  const updateCols = columns.filter((c) => c !== 'id' && c !== 'key');
  const placeholders = insertCols.map((_, i) => `$${i + 1}`).join(', ');
  const updateClause = updateCols.map((c) => `${quoteIdent(c)} = EXCLUDED.${quoteIdent(c)}`).join(', ');

  for (const row of rows) {
    const values = insertCols.map((c) => row[c]);
    await targetPool.query(`
      insert into unit_types (${insertCols.map(quoteIdent).join(', ')})
      values (${placeholders})
      on conflict (key) do update
      set ${updateClause}
    `, values);
  }

  return rows.length;
}

async function syncGameConfigs(sourcePool, targetPool) {
  const columns = await getColumns(sourcePool, 'game_configs');
  const { rows } = await sourcePool.query(`
    select ${columns.map(quoteIdent).join(', ')}
    from game_configs
    order by mode, version
  `);

  await targetPool.query('delete from game_configs');

  const placeholders = columns.map((_, i) => `$${i + 1}`).join(', ');
  for (const row of rows) {
    const values = columns.map((c) => row[c]);
    await targetPool.query(`
      insert into game_configs (${columns.map(quoteIdent).join(', ')})
      values (${placeholders})
    `, values);
  }

  return rows.length;
}

async function main() {
  const leftUrl = process.env.LEFT_DATABASE_URL || process.env.DATABASE_URL;
  const rightUrl = process.env.RIGHT_DATABASE_URL || process.env.PROD_DATABASE_URL;

  if (!leftUrl || !rightUrl) {
    console.error('[sync-config] Set LEFT_DATABASE_URL and RIGHT_DATABASE_URL (or DATABASE_URL and PROD_DATABASE_URL).');
    process.exit(1);
  }

  const sourcePool = makePool(leftUrl);
  const targetPool = makePool(rightUrl);

  try {
    await targetPool.query('begin');
    const unitTypeCount = await syncUnitTypes(sourcePool, targetPool);
    const gameConfigCount = await syncGameConfigs(sourcePool, targetPool);
    await targetPool.query('commit');
    console.log(JSON.stringify({
      ok: true,
      synced: {
        unit_types: unitTypeCount,
        game_configs: gameConfigCount,
      },
    }, null, 2));
  } catch (err) {
    try { await targetPool.query('rollback'); } catch {}
    throw err;
  } finally {
    await sourcePool.end();
    await targetPool.end();
  }
}

main().catch((err) => {
  console.error('[sync-config] fatal:', err && (err.stack || err.message || String(err)));
  process.exit(1);
});
