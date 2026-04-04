'use strict';

const { Pool } = require('pg');
const { performance } = require('node:perf_hooks');
const log = require('./logger');

const SLOW_QUERY_THRESHOLD_MS = 300;
const MAX_QUERY_SUMMARY_CHARS = 180;

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

function normalizeQueryArgs(params, options) {
  if (!Array.isArray(params) && params && typeof params === 'object' && options == null) {
    if (typeof params.label === 'string' || typeof params.queryName === 'string') {
      return { params: undefined, options: params };
    }
  }

  return { params, options: options || {} };
}

function summarizeQueryText(text) {
  return String(text || '')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, MAX_QUERY_SUMMARY_CHARS);
}

function roundDurationMs(value) {
  return Number.isFinite(value) ? Number(value.toFixed(1)) : null;
}

async function query(text, params, options) {
  const normalized = normalizeQueryArgs(params, options);
  const start = performance.now();
  const res = normalized.params === undefined
    ? await pool.query(text)
    : await pool.query(text, normalized.params);
  const ms = performance.now() - start;
  if (ms > SLOW_QUERY_THRESHOLD_MS) {
    log.info('[db] slow query', {
      ms: roundDurationMs(ms),
      label: normalized.options.label || normalized.options.queryName || null,
      query: summarizeQueryText(text),
      rowCount: typeof res.rowCount === 'number' ? res.rowCount : null,
    });
  }
  return res;
}

async function getClient() {
  return pool.connect();
}

module.exports = { query, getClient, pool };
