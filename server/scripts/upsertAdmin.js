'use strict';

require('dotenv').config();

const bcrypt = require('bcryptjs');
const db = require('../db');

const VALID_ROLES = new Set(['viewer', 'support', 'moderator', 'editor', 'engineer', 'owner']);
const BCRYPT_ROUNDS = 13;

function getArg(name) {
  const prefix = `--${name}=`;
  const raw = process.argv.find((arg) => arg.startsWith(prefix));
  return raw ? raw.slice(prefix.length).trim() : '';
}

function hasFlag(name) {
  return process.argv.includes(`--${name}`);
}

function fail(message) {
  console.error(message);
  process.exit(1);
}

async function main() {
  if (!process.env.DATABASE_URL) fail('DATABASE_URL is not configured.');

  const email = getArg('email').toLowerCase();
  const displayName = getArg('display-name');
  const password = getArg('password');
  const role = (getArg('role') || 'owner').toLowerCase();
  const active = !hasFlag('inactive');

  if (!email || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
    fail('Provide a valid email with --email=you@example.com');
  }
  if (!displayName) fail('Provide a display name with --display-name="Your Name"');
  if (!VALID_ROLES.has(role)) fail(`Invalid role. Use one of: ${Array.from(VALID_ROLES).join(', ')}`);
  if (!password || password.length < 8) {
    fail('Provide a password with --password=... (minimum 8 characters)');
  }

  const passwordHash = await bcrypt.hash(password, BCRYPT_ROUNDS);
  const existing = await db.query(
    `SELECT id, email, display_name, role, active
       FROM admin_users
      WHERE lower(email) = lower($1)
      LIMIT 1`,
    [email]
  );

  if (existing.rows[0]) {
    const updated = await db.query(
      `UPDATE admin_users
          SET display_name = $2,
              password_hash = $3,
              role = $4,
              active = $5
        WHERE id = $1
        RETURNING id, email, display_name, role, active`,
      [existing.rows[0].id, displayName.slice(0, 40), passwordHash, role, active]
    );
    console.log(JSON.stringify({ action: 'updated', user: updated.rows[0] }, null, 2));
    return;
  }

  const inserted = await db.query(
    `INSERT INTO admin_users (email, display_name, password_hash, role, active)
     VALUES ($1, $2, $3, $4, $5)
     RETURNING id, email, display_name, role, active`,
    [email, displayName.slice(0, 40), passwordHash, role, active]
  );

  console.log(JSON.stringify({ action: 'created', user: inserted.rows[0] }, null, 2));
}

main()
  .catch((err) => {
    console.error(err.message || String(err));
    process.exit(1);
  })
  .finally(async () => {
    try { await db.pool.end(); } catch {}
  });
