'use strict';

const { Pool } = require('pg');
const log = require('./logger');

// SSL: In production, cert validation is enforced by default (rejectUnauthorized: true).
// Set DATABASE_SSL_REJECT_UNAUTHORIZED=false only in staging/dev environments that use
// self-signed certs. Never disable in production.
const isLocal = (process.env.DATABASE_URL || '').includes('localhost') ||
                (process.env.DATABASE_URL || '').includes('127.0.0.1');

const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
  ssl: process.env.DATABASE_URL && !isLocal
    ? { rejectUnauthorized: process.env.DATABASE_SSL_REJECT_UNAUTHORIZED !== 'false' }
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
