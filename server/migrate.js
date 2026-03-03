'use strict';

// Run all pending migrations in order.
// Usage: node server/migrate.js
// Safe to run multiple times — all DDL uses IF NOT EXISTS.

const fs   = require('fs');
const path = require('path');
const { Pool } = require('pg');

async function main() {
  if (!process.env.DATABASE_URL) {
    console.error('[migrate] DATABASE_URL not set — skipping migrations');
    return;
  }

  const isLocal = (process.env.DATABASE_URL || '').includes('localhost') ||
                  (process.env.DATABASE_URL || '').includes('127.0.0.1');
  const pool = new Pool({
    connectionString: process.env.DATABASE_URL,
    ssl: isLocal ? false : { rejectUnauthorized: process.env.DATABASE_SSL_REJECT_UNAUTHORIZED === 'true' },
  });

  const migrationsDir = path.join(__dirname, 'migrations');
  const files = fs
    .readdirSync(migrationsDir)
    .filter(f => f.endsWith('.sql'))
    .sort();

  console.log(`[migrate] running ${files.length} migration(s)…`);

  for (const file of files) {
    const sql = fs.readFileSync(path.join(migrationsDir, file), 'utf8');
    console.log(`[migrate] → ${file}`);
    await pool.query(sql);
  }

  console.log('[migrate] done');
  await pool.end();
}

main().catch(err => {
  console.error('[migrate] fatal:', err.message);
  process.exit(1);
});
