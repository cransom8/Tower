'use strict';

const { OAuth2Client } = require('google-auth-library');
const jwt    = require('jsonwebtoken');
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const db     = require('./db');

const BCRYPT_ROUNDS = 12;

const KNOWN_AUTH_ERRORS = new Set([
  'Invalid refresh token',
  'Refresh token already revoked',
  'Refresh token expired',
  'Account suspended',
]);

const GOOGLE_CLIENT_ID    = process.env.GOOGLE_CLIENT_ID;
const JWT_SECRET          = process.env.JWT_SECRET;

if (!JWT_SECRET) {
  throw new Error('[auth] FATAL: JWT_SECRET env var is not set — refusing to start');
}
if (JWT_SECRET.length < 32) {
  throw new Error('[auth] FATAL: JWT_SECRET is too short (< 32 chars) — use a stronger secret');
}
const ACCESS_TOKEN_TTL    = '15m';
const REFRESH_TOKEN_TTL_MS = 30 * 24 * 60 * 60 * 1000; // 30 days

const googleClient = new OAuth2Client(GOOGLE_CLIENT_ID);

// ── Google ────────────────────────────────────────────────────────────────────

async function verifyGoogleToken(idToken) {
  const ticket = await googleClient.verifyIdToken({
    idToken,
    audience: GOOGLE_CLIENT_ID,
  });
  const p = ticket.getPayload();
  return { googleId: p.sub, email: p.email, name: p.name };
}

// ── Player ────────────────────────────────────────────────────────────────────

function sanitizeName(raw) {
  return String(raw || '')
    .replace(/[^a-zA-Z0-9_ ]/g, '')
    .trim()
    .slice(0, 20) || 'Player';
}

async function findOrCreatePlayer(googleId, suggestedName) {
  const existing = await db.query(
    'SELECT id, display_name, region, status FROM players WHERE google_id = $1',
    [googleId]
  );
  if (existing.rows.length > 0) return existing.rows[0];

  // Generate a unique display name. Cap iterations at 20 to prevent runaway
  // DB queries when a popular base name has many registered variants.
  let base = sanitizeName(suggestedName);
  let name = base;
  let suffix = 1;
  const MAX_SUFFIX_TRIES = 20;
  while (suffix <= MAX_SUFFIX_TRIES) {
    const conflict = await db.query(
      'SELECT id FROM players WHERE lower(display_name) = lower($1)',
      [name]
    );
    if (conflict.rows.length === 0) break;
    const tag = String(suffix++);
    name = base.slice(0, 20 - tag.length) + tag;
  }
  // If still conflicted after MAX_SUFFIX_TRIES, use a random hex suffix
  if (suffix > MAX_SUFFIX_TRIES) {
    const rnd = crypto.randomBytes(3).toString('hex');
    name = base.slice(0, 14) + rnd;
  }

  const res = await db.query(
    `INSERT INTO players (google_id, display_name, email_verified)
     VALUES ($1, $2, true)
     RETURNING id, display_name, region, status`,
    [googleId, name]
  );
  const player = res.rows[0];

  // Seed default ratings for all modes
  await db.query(
    `INSERT INTO ratings (player_id, mode, mu, sigma, rating)
     VALUES ($1, '2v2_ranked', 1500, 350, 450),
            ($1, '2v2_casual', 1500, 350, 450)
     ON CONFLICT DO NOTHING`,
    [player.id]
  );

  return player;
}

// ── Password auth ────────────────────────────────────────────────────────────

async function registerWithPassword(email, displayName, password) {
  const conflict = await db.query(
    'SELECT id FROM players WHERE lower(email) = lower($1)',
    [email]
  );
  if (conflict.rows.length) throw new Error('Email already registered');

  const hash = await bcrypt.hash(password, BCRYPT_ROUNDS);

  let base = sanitizeName(displayName);
  let name = base;
  let suffix = 1;
  const MAX_SUFFIX_TRIES = 20;
  while (suffix <= MAX_SUFFIX_TRIES) {
    const c = await db.query(
      'SELECT id FROM players WHERE lower(display_name) = lower($1)',
      [name]
    );
    if (c.rows.length === 0) break;
    const tag = String(suffix++);
    name = base.slice(0, 20 - tag.length) + tag;
  }
  if (suffix > MAX_SUFFIX_TRIES) {
    const rnd = crypto.randomBytes(3).toString('hex');
    name = base.slice(0, 14) + rnd;
  }

  const res = await db.query(
    `INSERT INTO players (email, display_name, password_hash)
     VALUES ($1, $2, $3)
     RETURNING id, display_name, region, status`,
    [email.toLowerCase(), name, hash]
  );
  const player = res.rows[0];

  await db.query(
    `INSERT INTO ratings (player_id, mode, mu, sigma, rating)
     VALUES ($1, '2v2_ranked', 1500, 350, 450),
            ($1, '2v2_casual', 1500, 350, 450)
     ON CONFLICT DO NOTHING`,
    [player.id]
  );

  return player;
}

async function loginWithPassword(email, password) {
  const res = await db.query(
    'SELECT id, display_name, region, status, password_hash FROM players WHERE lower(email) = lower($1)',
    [email]
  );
  if (!res.rows.length) throw new Error('Invalid email or password');
  const player = res.rows[0];
  if (player.status === 'suspended') throw new Error('Account suspended');
  if (!player.password_hash) throw new Error('Invalid email or password');
  const valid = await bcrypt.compare(password, player.password_hash);
  if (!valid) throw new Error('Invalid email or password');
  return player;
}

// ── Password reset ────────────────────────────────────────────────────────────

const RESET_TOKEN_TTL_MS = 30 * 60 * 1000; // 30 minutes

async function createPasswordResetToken(email) {
  const res = await db.query(
    'SELECT id FROM players WHERE lower(email) = lower($1) AND password_hash IS NOT NULL',
    [email]
  );
  if (!res.rows.length) return null; // silently no-op (don't reveal account existence)

  const playerId = res.rows[0].id;
  const raw  = crypto.randomBytes(32).toString('hex');
  const hash = crypto.createHmac('sha256', JWT_SECRET).update(raw).digest('hex');
  const exp  = new Date(Date.now() + RESET_TOKEN_TTL_MS);

  // Invalidate any existing unused tokens for this player
  await db.query(
    `UPDATE password_reset_tokens SET used_at = NOW()
     WHERE player_id = $1 AND used_at IS NULL`,
    [playerId]
  );

  await db.query(
    `INSERT INTO password_reset_tokens (player_id, token_hash, expires_at)
     VALUES ($1, $2, $3)`,
    [playerId, hash, exp]
  );

  return { raw, email: email.toLowerCase() };
}

async function consumePasswordResetToken(rawToken, newPassword) {
  const hash = crypto.createHmac('sha256', JWT_SECRET).update(rawToken).digest('hex');
  const res  = await db.query(
    `SELECT prt.player_id, prt.expires_at, prt.used_at, p.status
     FROM password_reset_tokens prt
     JOIN players p ON p.id = prt.player_id
     WHERE prt.token_hash = $1`,
    [hash]
  );
  if (!res.rows.length)          throw new Error('Invalid or expired reset link');
  const row = res.rows[0];
  if (row.used_at)               throw new Error('Reset link already used');
  if (new Date(row.expires_at) < new Date()) throw new Error('Invalid or expired reset link');
  if (row.status === 'suspended') throw new Error('Account suspended');

  const newHash = await bcrypt.hash(newPassword, BCRYPT_ROUNDS);

  await db.query(
    `UPDATE password_reset_tokens SET used_at = NOW() WHERE token_hash = $1`,
    [hash]
  );
  await db.query(
    `UPDATE players SET password_hash = $1 WHERE id = $2`,
    [newHash, row.player_id]
  );

  // Revoke all refresh tokens so old sessions are invalidated
  await db.query(
    `UPDATE refresh_tokens SET revoked_at = NOW()
     WHERE player_id = $1 AND revoked_at IS NULL`,
    [row.player_id]
  );

  return { playerId: row.player_id };
}

// ── Email verification ────────────────────────────────────────────────────────

const VERIFY_TOKEN_TTL_MS = 2 * 60 * 60 * 1000; // 2 hours

async function createEmailVerificationToken(playerId) {
  const raw  = crypto.randomBytes(32).toString('hex');
  const hash = crypto.createHmac('sha256', JWT_SECRET).update(raw).digest('hex');
  const exp  = new Date(Date.now() + VERIFY_TOKEN_TTL_MS);

  // Invalidate any existing unused tokens for this player
  await db.query(
    `UPDATE email_verification_tokens SET used_at = NOW()
     WHERE player_id = $1 AND used_at IS NULL`,
    [playerId]
  );

  await db.query(
    `INSERT INTO email_verification_tokens (player_id, token_hash, expires_at)
     VALUES ($1, $2, $3)`,
    [playerId, hash, exp]
  );

  return { raw, playerId };
}

async function consumeEmailVerificationToken(rawToken) {
  const hash = crypto.createHmac('sha256', JWT_SECRET).update(rawToken).digest('hex');
  const res  = await db.query(
    `SELECT evt.player_id, evt.expires_at, evt.used_at,
            p.id, p.display_name, p.region, p.status
     FROM email_verification_tokens evt
     JOIN players p ON p.id = evt.player_id
     WHERE evt.token_hash = $1`,
    [hash]
  );
  if (!res.rows.length)          throw new Error('Invalid or expired verification link');
  const row = res.rows[0];
  if (row.used_at)               throw new Error('Invalid or expired verification link');
  if (new Date(row.expires_at) < new Date()) throw new Error('Invalid or expired verification link');

  await db.query(
    `UPDATE email_verification_tokens SET used_at = NOW() WHERE token_hash = $1`,
    [hash]
  );
  await db.query(
    `UPDATE players SET email_verified = true WHERE id = $1`,
    [row.player_id]
  );

  return { id: row.id, display_name: row.display_name, region: row.region, status: row.status };
}

// ── TOTP 2FA ─────────────────────────────────────────────────────────────────

const MFA_TOKEN_TTL = '5m';

function signMfaToken(playerId) {
  return jwt.sign(
    { sub: playerId, scope: 'mfa' },
    JWT_SECRET,
    { algorithm: 'HS256', expiresIn: MFA_TOKEN_TTL }
  );
}

function verifyMfaToken(token) {
  const payload = jwt.verify(token, JWT_SECRET, { algorithms: ['HS256'] });
  if (payload.scope !== 'mfa') throw new Error('Invalid MFA token');
  return payload;
}

// ── JWT ───────────────────────────────────────────────────────────────────────

function signAccessToken(player) {
  return jwt.sign(
    { sub: player.id, displayName: player.display_name },
    JWT_SECRET,
    { algorithm: 'HS256', expiresIn: ACCESS_TOKEN_TTL }
  );
}

function verifyAccessToken(token) {
  // Pin algorithm to HS256 to prevent algorithm-confusion attacks (C-1)
  return jwt.verify(token, JWT_SECRET, { algorithms: ['HS256'] });
}

// ── Refresh tokens ────────────────────────────────────────────────────────────

async function createRefreshToken(playerId) {
  const raw  = crypto.randomBytes(48).toString('hex');
  const hash = crypto.createHash('sha256').update(raw).digest('hex');
  const exp  = new Date(Date.now() + REFRESH_TOKEN_TTL_MS);

  await db.query(
    `INSERT INTO refresh_tokens (player_id, token_hash, expires_at)
     VALUES ($1, $2, $3)`,
    [playerId, hash, exp]
  );
  return raw; // only the raw token is sent to the client
}

async function rotateRefreshToken(rawToken) {
  const hash = crypto.createHash('sha256').update(rawToken).digest('hex');
  const res  = await db.query(
    `SELECT rt.player_id, rt.expires_at, rt.revoked_at,
            p.display_name, p.status
     FROM refresh_tokens rt
     JOIN players p ON p.id = rt.player_id
     WHERE rt.token_hash = $1`,
    [hash]
  );

  if (!res.rows.length)          throw new Error('Invalid refresh token');
  const row = res.rows[0];
  if (row.revoked_at)            throw new Error('Refresh token already revoked');
  if (new Date(row.expires_at) < new Date()) throw new Error('Refresh token expired');
  if (row.status === 'suspended') throw new Error('Account suspended');

  // Revoke used token (rotation)
  await db.query(
    `UPDATE refresh_tokens SET revoked_at = NOW() WHERE token_hash = $1`,
    [hash]
  );

  const player       = { id: row.player_id, display_name: row.display_name };
  const accessToken  = signAccessToken(player);
  const refreshToken = await createRefreshToken(row.player_id);
  return { accessToken, refreshToken, player };
}

async function revokeRefreshToken(rawToken) {
  const hash = crypto.createHash('sha256').update(rawToken).digest('hex');
  await db.query(
    `UPDATE refresh_tokens SET revoked_at = NOW()
     WHERE token_hash = $1 AND revoked_at IS NULL`,
    [hash]
  );
}

module.exports = {
  verifyGoogleToken,
  findOrCreatePlayer,
  registerWithPassword,
  loginWithPassword,
  createPasswordResetToken,
  consumePasswordResetToken,
  createEmailVerificationToken,
  consumeEmailVerificationToken,
  signMfaToken,
  verifyMfaToken,
  signAccessToken,
  verifyAccessToken,
  createRefreshToken,
  rotateRefreshToken,
  revokeRefreshToken,
  KNOWN_AUTH_ERRORS,
};
