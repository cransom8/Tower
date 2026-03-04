'use strict';

const { Pool } = require('pg');

// SSL: Railway requires rejectUnauthorized:false by default.
// Set DATABASE_SSL_REJECT_UNAUTHORIZED=true to enforce cert validation in environments
// that provide a trusted CA (e.g. self-managed Postgres with a real cert).
const isLocal = (process.env.DATABASE_URL || '').includes('localhost') ||
                (process.env.DATABASE_URL || '').includes('127.0.0.1');

const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
  ssl: process.env.DATABASE_URL && !isLocal
    ? { rejectUnauthorized: process.env.DATABASE_SSL_REJECT_UNAUTHORIZED === 'true' }
    : false,
});

pool.on('error', (err) => {
  log.error('[db] unexpected error on idle client', { err: err.message });
});

async function query(text, params) {
  const start = Date.now();
  const res = await pool.query(text, params);
  const ms = Date.now() - start;
  if (ms > 300) log.info(`[db] slow query (${ms}ms)`);
  return res;
}

async function getClient() {
  return pool.connect();
}

module.exports = { query, getClient, pool };
