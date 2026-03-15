'use strict';

const express  = require('express');
const router   = express.Router();
const jwt      = require('jsonwebtoken');
const bcrypt   = require('bcryptjs');
const crypto   = require('crypto');
const fs       = require('fs');
const fsp      = fs.promises;
const path     = require('path');
const log      = require('../logger');
const branding = require('../branding');

const UUID_RE      = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const FLAG_NAME_RE = /^[a-z_]{1,64}$/;
const EMAIL_RE     = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const BCRYPT_ROUNDS = 13;
const ADMIN_JWT_TTL = '1h';
const ADMIN_RESET_TOKEN_TTL_MS = 30 * 60 * 1000; // 30 minutes
const VALID_ROLES   = ['viewer', 'support', 'moderator', 'editor', 'engineer', 'owner'];
const MAX_ASSET_UPLOAD_BYTES = 2 * 1024 * 1024; // 2MB

const IMAGE_MIME_EXT = {
  'image/png': '.png',
  'image/jpeg': '.jpg',
  'image/webp': '.webp',
  'image/gif': '.gif',
  'image/svg+xml': '.svg',
};

// ── RBAC permission map ───────────────────────────────────────────────────────
// Permissions accumulate up the role hierarchy
const ROLE_PERMS = {
  viewer:    new Set([]),
  support:   new Set(['player.ban', 'player.revoke']),
  moderator: new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write']),
  editor:    new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write',
                      'config.write', 'season.write', 'rating.adjust']),
  engineer:  new Set(['player.ban', 'player.revoke', 'match.terminate', 'flag.write',
                      'config.write', 'season.write', 'rating.adjust', 'admin.write']),
  // owner → all (checked explicitly)
};

function hasPermission(role, perm) {
  if (role === 'owner') return true;
  return ROLE_PERMS[role]?.has(perm) ?? false;
}

// ── Auth helpers ──────────────────────────────────────────────────────────────

function getAdminJwtSecret() {
  const secret = process.env.JWT_SECRET;
  if (!secret) throw new Error('[admin] JWT_SECRET not configured');
  return secret;
}

function issueAdminToken(user) {
  return jwt.sign(
    { type: 'admin', id: user.id, email: user.email,
      role: user.role, displayName: user.display_name },
    getAdminJwtSecret(),
    { algorithm: 'HS256', expiresIn: ADMIN_JWT_TTL }
  );
}

const ADMIN_IS_PROD = process.env.NODE_ENV === 'production';

function setAdminCookie(res, token) {
  res.cookie('cd_admin', token, {
    httpOnly: true,
    secure:   ADMIN_IS_PROD,
    sameSite: 'strict',
    path:     '/admin',
    maxAge:   60 * 60 * 1000, // 1 hour in ms
  });
}

function clearAdminCookie(res) {
  res.clearCookie('cd_admin', { httpOnly: true, secure: ADMIN_IS_PROD, sameSite: 'strict', path: '/admin' });
}

function verifyAdminToken(token) {
  const payload = jwt.verify(token, getAdminJwtSecret(), { algorithms: ['HS256'] });
  if (payload.type !== 'admin') throw new Error('Invalid admin token');
  return payload;
}

// requireAdmin: accepts Bearer admin JWT OR cd_admin cookie
function requireAdmin(req, res, next) {
  const auth = req.headers['authorization'];
  const bearerToken = auth?.startsWith('Bearer ') ? auth.slice(7) : null;
  const cookieToken = req.cookies?.cd_admin || null;
  const token = bearerToken || cookieToken;

  if (token) {
    try {
      const payload = verifyAdminToken(token);
      req.adminRole   = payload.role;
      req.adminEmail  = payload.email;
      req.adminUserId = payload.id;
      return next();
    } catch { /* fall through */ }
  }

  return res.status(403).json({ error: 'Forbidden' });
}

// requirePermission: gate a route behind a specific RBAC permission
function requirePermission(perm) {
  return (req, res, next) => {
    if (hasPermission(req.adminRole, perm)) return next();
    return res.status(403).json({
      error: `Your role (${req.adminRole}) does not have permission: ${perm}`
    });
  };
}

// Audit log — non-blocking best-effort
async function audit(db, action, targetType, targetId, payload, adminEmail, adminIp) {
  if (!db) return;
  const payloadBytes = JSON.stringify(
    payload ? { ...payload, _by: adminEmail } : { _by: adminEmail }
  ).slice(0, 4096); // cap at 4KB to prevent DB bloat
  try {
    await db.query(
      `INSERT INTO admin_audit_log (action, target_type, target_id, payload, admin_ip)
       VALUES ($1, $2, $3, $4, $5)`,
      [action, targetType || null, targetId || null, payloadBytes, adminIp || null]
    );
  } catch { /* never block on audit failure */ }
}

// ── Admin login rate limiter (5 attempts per IP per minute) ──────────────────
const _adminLoginAttempts = new Map();
setInterval(() => {
  const cutoff = Date.now() - 60_000;
  for (const [ip, r] of _adminLoginAttempts) {
    if (r.windowStart < cutoff) _adminLoginAttempts.delete(ip);
  }
}, 60_000).unref();

function checkAdminLoginRateLimit(ip) {
  const now = Date.now();
  let r = _adminLoginAttempts.get(ip);
  if (!r || now - r.windowStart > 60_000) {
    r = { count: 0, windowStart: now };
    _adminLoginAttempts.set(ip, r);
  }
  r.count++;
  return r.count <= 5;
}

async function createAdminPasswordResetToken(email) {
  const db = require('../db');
  const result = await db.query(
    `SELECT id, email
     FROM admin_users
     WHERE lower(email) = lower($1) AND active = true AND password_hash IS NOT NULL
     LIMIT 1`,
    [email]
  );
  if (!result.rows.length) return null;

  const adminUser = result.rows[0];
  const raw  = crypto.randomBytes(32).toString('hex');
  const hash = crypto.createHmac('sha256', getAdminJwtSecret()).update(raw).digest('hex');
  const exp  = new Date(Date.now() + ADMIN_RESET_TOKEN_TTL_MS);

  await db.query(
    `UPDATE admin_password_reset_tokens
     SET used_at = NOW()
     WHERE admin_user_id = $1 AND used_at IS NULL`,
    [adminUser.id]
  );
  await db.query(
    `INSERT INTO admin_password_reset_tokens (admin_user_id, token_hash, expires_at)
     VALUES ($1, $2, $3)`,
    [adminUser.id, hash, exp]
  );

  return { raw, email: String(adminUser.email).toLowerCase() };
}

async function consumeAdminPasswordResetToken(rawToken, newPassword) {
  const db = require('../db');
  const hash = crypto.createHmac('sha256', getAdminJwtSecret()).update(rawToken).digest('hex');
  const result = await db.query(
    `SELECT aprt.admin_user_id, aprt.expires_at, aprt.used_at, au.active
     FROM admin_password_reset_tokens aprt
     JOIN admin_users au ON au.id = aprt.admin_user_id
     WHERE aprt.token_hash = $1`,
    [hash]
  );
  if (!result.rows.length) throw new Error('Invalid or expired reset link');

  const row = result.rows[0];
  if (row.used_at) throw new Error('Reset link already used');
  if (new Date(row.expires_at) < new Date()) throw new Error('Invalid or expired reset link');
  if (!row.active) throw new Error('Account is inactive');

  const newHash = await bcrypt.hash(newPassword, BCRYPT_ROUNDS);
  await db.query(`UPDATE admin_password_reset_tokens SET used_at = NOW() WHERE token_hash = $1`, [hash]);
  await db.query(`UPDATE admin_users SET password_hash = $1 WHERE id = $2`, [newHash, row.admin_user_id]);
  return { adminUserId: row.admin_user_id };
}

// ── Admin login (email + password) ───────────────────────────────────────────

// POST /admin/login
router.post('/login', async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAdminLoginRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many login attempts. Try again later.' });
  }
  const db = require('../db');
  const { email, password } = req.body;
  if (!email || !password) return res.status(400).json({ error: 'email and password required' });

  try {
    const r = await db.query(
      `SELECT id, email, display_name, role, active, password_hash, totp_enabled
       FROM admin_users WHERE lower(email) = lower($1)`,
      [email]
    );
    const user = r.rows[0];
    if (!user || !user.active) return res.status(401).json({ error: 'Invalid credentials' });
    if (!user.password_hash) return res.status(401).json({ error: 'Password login not available — use Google SSO' });
    const valid = await bcrypt.compare(password, user.password_hash);
    if (!valid) return res.status(401).json({ error: 'Invalid credentials' });

    // If 2FA is enabled, issue a short-lived pending ticket instead of a full JWT
    if (user.totp_enabled) {
      const ticket = jwt.sign(
        { type: 'admin_2fa_pending', id: user.id },
        getAdminJwtSecret(),
        { algorithm: 'HS256', expiresIn: '5m' }
      );
      return res.json({ twoFaRequired: true, ticket });
    }

    await db.query(`UPDATE admin_users SET last_login = NOW() WHERE id = $1`, [user.id]);
    const token = issueAdminToken(user);
    setAdminCookie(res, token);
    res.json({ token, role: user.role, displayName: user.display_name, email: user.email });
  } catch (err) {
    log.error('[admin] POST /login failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/logout
router.post('/logout', (req, res) => {
  clearAdminCookie(res);
  res.json({ ok: true });
});

// POST /admin/login/2fa — complete 2FA step after password login
router.post('/login/2fa', async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const { ticket, code } = req.body;
  if (!ticket || !code) return res.status(400).json({ error: 'ticket and code required' });
  try {
    const payload = jwt.verify(ticket, getAdminJwtSecret(), { algorithms: ['HS256'] });
    if (payload.type !== 'admin_2fa_pending') return res.status(401).json({ error: 'Invalid ticket' });

    const db = require('../db');
    const r = await db.query(
      `SELECT id, email, display_name, role, active, totp_secret, totp_enabled
       FROM admin_users WHERE id = $1`,
      [payload.id]
    );
    const user = r.rows[0];
    if (!user || !user.active || !user.totp_enabled) return res.status(401).json({ error: 'Invalid credentials' });

    const { authenticator } = require('otplib');
    authenticator.options = { window: 1 };
    const valid = authenticator.verify({ token: String(code).replace(/\s/g, ''), secret: user.totp_secret });
    if (!valid) return res.status(401).json({ error: 'Invalid 2FA code' });

    await db.query(`UPDATE admin_users SET last_login = NOW() WHERE id = $1`, [user.id]);
    const token = issueAdminToken(user);
    setAdminCookie(res, token);
    res.json({ token, role: user.role, displayName: user.display_name, email: user.email });
  } catch (err) {
    log.error('[admin] POST /login/2fa failed', { err: err.message });
    res.status(401).json({ error: 'Invalid or expired ticket' });
  }
});

// POST /admin/forgot-password
// Body: { email }
// Always returns 200 to avoid account enumeration.
router.post('/forgot-password', async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAdminLoginRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many attempts. Try again later.' });
  }
  const { email } = req.body || {};
  if (!email) return res.status(400).json({ error: 'Email required' });

  try {
    if (EMAIL_RE.test(String(email).trim())) {
      const tokenResult = await createAdminPasswordResetToken(String(email).trim());
      if (tokenResult) {
        const mailer  = require('../services/mailer');
        const baseUrl = process.env.APP_URL || `${req.protocol}://${req.get('host')}`;
        const resetUrl = `${baseUrl}/admin?admin_reset=${encodeURIComponent(tokenResult.raw)}`;
        await mailer.sendAdminPasswordReset(tokenResult.email, resetUrl);
      }
    }
  } catch (err) {
    log.error('[admin] POST /forgot-password failed', { err: err.message });
  }

  res.json({ ok: true });
});

// POST /admin/reset-password
// Body: { token, password }
router.post('/reset-password', async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAdminLoginRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many attempts. Try again later.' });
  }
  try {
    const { token, password } = req.body || {};
    if (!token) return res.status(400).json({ error: 'Token required' });
    if (!password || String(password).length < 8) {
      return res.status(400).json({ error: 'Password must be at least 8 chars' });
    }
    const result = await consumeAdminPasswordResetToken(String(token), String(password));
    clearAdminCookie(res);
    await audit(require('../db'), 'reset_password', 'admin_user', result.adminUserId, {}, null, req.ip);
    res.json({ ok: true });
  } catch (err) {
    const known = ['Invalid or expired reset link', 'Reset link already used', 'Account is inactive'];
    const msg = known.includes(err.message) ? err.message : 'Password reset failed';
    res.status(400).json({ error: msg });
  }
});

// POST /admin/auth/google — Google SSO login for admin (must already have account)
router.post('/auth/google', async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const { credential } = req.body;
  if (!credential) return res.status(400).json({ error: 'credential required' });

  const googleClientId = process.env.GOOGLE_CLIENT_ID;
  if (!googleClientId) return res.status(503).json({ error: 'Google SSO not configured' });

  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAdminLoginRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many login attempts. Try again later.' });
  }

  try {
    const { OAuth2Client } = require('google-auth-library');
    const gClient = new OAuth2Client(googleClientId);
    const ticket = await gClient.verifyIdToken({ idToken: credential, audience: googleClientId });
    const p = ticket.getPayload();
    const googleId = p.sub;
    const email = String(p.email || '').trim().toLowerCase();

    const db = require('../db');
    // Match by google_id first, then fall back to email (to auto-link on first SSO use)
    const r = await db.query(
      `SELECT id, email, display_name, role, active, totp_enabled, totp_secret, google_id
       FROM admin_users
       WHERE google_id = $1 OR lower(email) = lower($2)
       ORDER BY (google_id = $1) DESC
       LIMIT 1`,
      [googleId, email]
    );
    const user = r.rows[0];
    if (!user || !user.active) {
      const suffix = email ? ` (${email})` : '';
      return res.status(401).json({
        error: `No active admin account found for this Google account${suffix}. Contact an owner to create or enable it.`,
      });
    }

    // Auto-link google_id on first SSO login via email match
    if (!user.google_id) {
      await db.query(`UPDATE admin_users SET google_id = $1 WHERE id = $2`, [googleId, user.id]);
    }

    // If 2FA is enabled, issue a pending ticket
    if (user.totp_enabled) {
      const pendingTicket = jwt.sign(
        { type: 'admin_2fa_pending', id: user.id },
        getAdminJwtSecret(),
        { algorithm: 'HS256', expiresIn: '5m' }
      );
      return res.json({ twoFaRequired: true, ticket: pendingTicket });
    }

    await db.query(`UPDATE admin_users SET last_login = NOW() WHERE id = $1`, [user.id]);
    const token = issueAdminToken(user);
    setAdminCookie(res, token);
    res.json({ token, role: user.role, displayName: user.display_name, email: user.email });
  } catch (err) {
    const msg = String(err?.message || '');
    let userMessage = 'Google authentication failed';
    if (/wrong recipient|audience/i.test(msg)) {
      userMessage = 'Google client configuration mismatch (audience/client ID).';
    } else if (/expired|too late|used too early|not yet valid/i.test(msg)) {
      userMessage = 'Google token timing validation failed. Try signing in again.';
    }
    log.error('[admin] POST /auth/google failed', { err: msg });
    if (process.env.NODE_ENV !== 'production') {
      let decoded = null;
      try {
        const parts = String(credential || '').split('.');
        if (parts.length >= 2) {
          decoded = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf8'));
        }
      } catch { /* best effort */ }
      return res.status(401).json({
        error: userMessage,
        debug: {
          verifyError: msg,
          expectedAudience: googleClientId,
          tokenAud: decoded?.aud || null,
          tokenAzp: decoded?.azp || null,
          tokenIss: decoded?.iss || null,
        },
      });
    }
    res.status(401).json({ error: userMessage });
  }
});

// ── Self-management (authenticated admin acting on their own account) ─────────

// GET /admin/me — return own account info
router.get('/me', requireAdmin, async (req, res) => {
  if (!req.adminUserId) return res.status(400).json({ error: 'Only JWT-authenticated admins can use /me' });
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  try {
    const r = await db.query(
      `SELECT id, email, display_name, role, active, totp_enabled,
              (google_id IS NOT NULL) AS has_google, created_at, last_login
       FROM admin_users WHERE id = $1`,
      [req.adminUserId]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Not found' });
    res.json({ user: r.rows[0] });
  } catch (err) {
    log.error('[admin] GET /me failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/me/password — change own password
router.post('/me/password', requireAdmin, async (req, res) => {
  if (!req.adminUserId) return res.status(400).json({ error: 'Only JWT-authenticated admins can use /me' });
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { currentPassword, newPassword } = req.body;
  if (!newPassword || newPassword.length < 8) {
    return res.status(400).json({ error: 'New password must be at least 8 chars' });
  }
  try {
    const r = await db.query(
      `SELECT password_hash FROM admin_users WHERE id = $1`, [req.adminUserId]
    );
    const user = r.rows[0];
    if (!user) return res.status(404).json({ error: 'Not found' });
    // If account has a password, require the current password for confirmation
    if (user.password_hash) {
      if (!currentPassword) return res.status(400).json({ error: 'currentPassword required' });
      const valid = await bcrypt.compare(currentPassword, user.password_hash);
      if (!valid) return res.status(401).json({ error: 'Current password is incorrect' });
    }
    const hash = await bcrypt.hash(newPassword, BCRYPT_ROUNDS);
    await db.query(`UPDATE admin_users SET password_hash = $1 WHERE id = $2`, [hash, req.adminUserId]);
    await audit(db, 'change_own_password', 'admin_user', req.adminUserId, {}, req.adminEmail, req.ip);
    res.json({ ok: true });
  } catch (err) {
    log.error('[admin] POST /me/password failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/me/2fa/setup — generate TOTP secret + QR code (pending, not yet enabled)
router.post('/me/2fa/setup', requireAdmin, async (req, res) => {
  if (!req.adminUserId) return res.status(400).json({ error: 'Only JWT-authenticated admins can use /me' });
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  try {
    const { authenticator } = require('otplib');
    const QRCode = require('qrcode');
    const r = await db.query(`SELECT display_name FROM admin_users WHERE id = $1`, [req.adminUserId]);
    if (!r.rows[0]) return res.status(404).json({ error: 'Not found' });
    const secret = authenticator.generateSecret();
    await db.query(
      `UPDATE admin_users SET totp_secret = $1, totp_enabled = false WHERE id = $2`,
      [secret, req.adminUserId]
    );
    const otpauth = authenticator.keyuri(r.rows[0].display_name, 'CastleDefender Admin', secret);
    const qrCodeDataUrl = await QRCode.toDataURL(otpauth);
    res.json({ secret, qrCodeDataUrl });
  } catch (err) {
    log.error('[admin] POST /me/2fa/setup failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/me/2fa/enable — confirm TOTP code and activate 2FA
router.post('/me/2fa/enable', requireAdmin, async (req, res) => {
  if (!req.adminUserId) return res.status(400).json({ error: 'Only JWT-authenticated admins can use /me' });
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { code } = req.body;
  if (!code) return res.status(400).json({ error: 'code required' });
  try {
    const r = await db.query(
      `SELECT totp_secret FROM admin_users WHERE id = $1`, [req.adminUserId]
    );
    if (!r.rows[0]?.totp_secret) return res.status(400).json({ error: 'Run /me/2fa/setup first' });
    const { authenticator } = require('otplib');
    authenticator.options = { window: 1 };
    const valid = authenticator.verify({ token: String(code).replace(/\s/g, ''), secret: r.rows[0].totp_secret });
    if (!valid) return res.status(401).json({ error: 'Invalid code' });
    await db.query(`UPDATE admin_users SET totp_enabled = true WHERE id = $1`, [req.adminUserId]);
    await audit(db, 'enable_2fa', 'admin_user', req.adminUserId, {}, req.adminEmail, req.ip);
    res.json({ ok: true });
  } catch (err) {
    log.error('[admin] POST /me/2fa/enable failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/me/2fa/disable — verify current TOTP then disable
router.post('/me/2fa/disable', requireAdmin, async (req, res) => {
  if (!req.adminUserId) return res.status(400).json({ error: 'Only JWT-authenticated admins can use /me' });
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { code } = req.body;
  if (!code) return res.status(400).json({ error: 'code required' });
  try {
    const r = await db.query(
      `SELECT totp_secret, totp_enabled FROM admin_users WHERE id = $1`, [req.adminUserId]
    );
    if (!r.rows[0]?.totp_enabled) return res.status(400).json({ error: '2FA is not enabled' });
    const { authenticator } = require('otplib');
    authenticator.options = { window: 1 };
    const valid = authenticator.verify({ token: String(code).replace(/\s/g, ''), secret: r.rows[0].totp_secret });
    if (!valid) return res.status(401).json({ error: 'Invalid code' });
    await db.query(
      `UPDATE admin_users SET totp_enabled = false, totp_secret = NULL WHERE id = $1`,
      [req.adminUserId]
    );
    await audit(db, 'disable_2fa', 'admin_user', req.adminUserId, {}, req.adminEmail, req.ip);
    res.json({ ok: true });
  } catch (err) {
    log.error('[admin] POST /me/2fa/disable failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/setup — create first admin user (only when table is empty)
// Requires ADMIN_SECRET header to bootstrap; not available after first user exists.
router.post('/setup', async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const secret = process.env.ADMIN_SECRET;
  const provided = req.get('x-admin-secret') || '';
  // Use timingSafeEqual to prevent timing-based brute-force of the bootstrap secret.
  // Buffers must be equal length for timingSafeEqual, so pad to secret length.
  const secretBuf   = Buffer.from(secret || '');
  const providedBuf = Buffer.from(provided);
  const lengthMatch = secret && providedBuf.length === secretBuf.length;
  const valueMatch  = lengthMatch && crypto.timingSafeEqual(providedBuf, secretBuf);
  if (!valueMatch) {
    return res.status(403).json({ error: 'Forbidden' });
  }
  const db = require('../db');
  const { email, password, displayName } = req.body;
  if (!EMAIL_RE.test(email)) return res.status(400).json({ error: 'Invalid email' });
  if (!password || password.length < 8) return res.status(400).json({ error: 'Password must be at least 8 chars' });
  if (!displayName) return res.status(400).json({ error: 'displayName required' });

  try {
    const count = await db.query(`SELECT COUNT(*) AS cnt FROM admin_users`);
    if (parseInt(count.rows[0].cnt, 10) > 0) {
      return res.status(409).json({ error: 'Admin users already exist. Use POST /admin/users to add more.' });
    }
    const hash = await bcrypt.hash(password, BCRYPT_ROUNDS);
    const result = await db.query(
      `INSERT INTO admin_users (email, display_name, password_hash, role)
       VALUES ($1, $2, $3, 'owner') RETURNING id, email, display_name, role`,
      [email.toLowerCase(), displayName.slice(0, 40), hash]
    );
    res.status(201).json({ user: result.rows[0] });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'Email already in use' });
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Admin user management ─────────────────────────────────────────────────────

// GET /admin/users
router.get('/users', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ users: [] });
  const db = require('../db');
  try {
    const r = await db.query(
      `SELECT id, email, display_name, role, active, created_at, last_login
       FROM admin_users ORDER BY created_at`
    );
    res.json({ users: r.rows });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/users — create a new admin user (engineer+)
router.post('/users', requireAdmin, requirePermission('admin.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { email, password, displayName, role } = req.body;
  if (!EMAIL_RE.test(email)) return res.status(400).json({ error: 'Invalid email' });
  if (!password || password.length < 8) return res.status(400).json({ error: 'Password must be at least 8 chars' });
  if (!VALID_ROLES.includes(role)) return res.status(400).json({ error: 'Invalid role' });
  // Non-owners cannot create owners or engineers
  if (req.adminRole !== 'owner' && ['owner', 'engineer'].includes(role)) {
    return res.status(403).json({ error: 'Only owners can create owner/engineer accounts' });
  }
  try {
    const hash = await bcrypt.hash(password, BCRYPT_ROUNDS);
    const result = await db.query(
      `INSERT INTO admin_users (email, display_name, password_hash, role)
       VALUES ($1, $2, $3, $4) RETURNING id, email, display_name, role, active`,
      [email.toLowerCase(), (displayName || '').slice(0, 40) || email, hash, role]
    );
    await audit(db, 'create_admin_user', 'admin_user', result.rows[0].id, { email, role }, req.adminEmail, req.ip);
    res.status(201).json({ user: result.rows[0] });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'Email already in use' });
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PATCH /admin/users/:id — update role, active status, displayName, or password (owner only)
router.patch('/users/:id', requireAdmin, async (req, res) => {
  if (req.adminRole !== 'owner') return res.status(403).json({ error: 'Only owners can modify admin users' });
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid user ID' });
  const db = require('../db');
  const { role, active, password, displayName } = req.body;
  const sets = []; const params = [];
  if (role !== undefined) {
    if (!VALID_ROLES.includes(role)) return res.status(400).json({ error: 'Invalid role' });
    params.push(role); sets.push(`role = $${params.length}`);
  }
  if (active !== undefined) {
    params.push(!!active); sets.push(`active = $${params.length}`);
  }
  if (displayName !== undefined) {
    params.push(String(displayName).trim().slice(0, 40));
    sets.push(`display_name = $${params.length}`);
  }
  if (password !== undefined && password !== '') {
    if (password.length < 8) return res.status(400).json({ error: 'Password must be at least 8 chars' });
    const hash = await bcrypt.hash(password, BCRYPT_ROUNDS);
    params.push(hash); sets.push(`password_hash = $${params.length}`);
  }
  if (!sets.length) return res.status(400).json({ error: 'Nothing to update' });
  params.push(req.params.id);
  try {
    const result = await db.query(
      `UPDATE admin_users SET ${sets.join(', ')} WHERE id = $${params.length}
       RETURNING id, email, display_name, role, active`,
      params
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Admin user not found' });
    await audit(db, 'update_admin_user', 'admin_user', req.params.id,
      { role, active, displayName, passwordChanged: !!password }, req.adminEmail, req.ip);
    res.json({ user: result.rows[0] });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Live stats ────────────────────────────────────────────────────────────────

router.get('/stats/live', requireAdmin, (req, res) => {
  const gamesByRoomId = req.app.locals.gamesByRoomId || new Map();
  const mlRoomsByCode = req.app.locals.mlRoomsByCode || new Map();
  const partiesById   = req.app.locals.partiesById   || new Map();
  const io            = req.app.locals.io;
  const matchmaker    = require('../services/matchmaker');

  let mlGames = 0, soloGames = 0;
  for (const [, entry] of gamesByRoomId) {
    mlGames++;
    if (entry && entry.botController) soloGames++;
  }
  const lobbyRooms =
    [...mlRoomsByCode.values()].filter(r => !gamesByRoomId.has(r.roomId)).length;

  const privateLobbies = [...mlRoomsByCode.values()].filter(r =>
    !gamesByRoomId.has(r.roomId) && r.players && r.players.length > 0
  ).length;

  const queueStats = matchmaker.getQueueStats(partiesById);

  res.json({
    connectedSockets: io ? io.sockets.sockets.size : 0,
    activeGames: gamesByRoomId.size,
    mlGames,
    soloGames,
    lobbyRooms,
    privateLobbies,
    queues: queueStats,
    adminRole: req.adminRole,
  });
});

// GET /admin/queue — detailed queue snapshot (entries per mode + private lobbies)
router.get('/queue', requireAdmin, (req, res) => {
  const gamesByRoomId = req.app.locals.gamesByRoomId || new Map();
  const mlRoomsByCode = req.app.locals.mlRoomsByCode || new Map();
  const partiesById   = req.app.locals.partiesById   || new Map();
  const matchmaker    = require('../services/matchmaker');

  const queueStats = matchmaker.getQueueStats(partiesById);

  // Private lobbies: ML rooms not yet in a game, with at least one human player
  const privateLobbies = [...mlRoomsByCode.entries()]
    .filter(([, r]) => !gamesByRoomId.has(r.roomId) && r.players && r.players.length > 0)
    .map(([code, r]) => ({
      code,
      playerCount:  r.players.length,
      aiCount:      (r.aiPlayers || []).length,
      pvpMode:      r.pvpMode || '2v2',
      createdAt:    r.createdAt || null,
      waitMs:       r.createdAt ? Date.now() - r.createdAt : null,
    }));

  // Active solo games: ML rooms in a game that have bot controllers
  const soloGames = [...gamesByRoomId.entries()]
    .filter(([roomId, entry]) => roomId.startsWith('mlroom_') && entry && entry.botController)
    .map(([roomId, entry]) => ({
      roomId,
      botCount: entry.botController ? entry.botController.getBotCount() : 0,
      tick: entry.game ? entry.game.tick : 0,
    }));

  res.json({
    queues: queueStats,
    privateLobbies,
    soloGames,
    timestamp: Date.now(),
  });
});

router.get('/stats/daily', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) {
    return res.json({ matchesToday: 0, newPlayersToday: 0, totalPlayers: 0, avgMatchLengthSeconds: 0 });
  }
  const db = require('../db');
  try {
    const [matchRes, playerRes, totalRes, avgRes] = await Promise.all([
      db.query(`SELECT COUNT(*) AS cnt FROM matches WHERE started_at >= NOW() - INTERVAL '24 hours'`),
      db.query(`SELECT COUNT(*) AS cnt FROM players WHERE created_at >= NOW() - INTERVAL '24 hours'`),
      db.query(`SELECT COUNT(*) AS cnt FROM players`),
      db.query(`SELECT AVG(EXTRACT(EPOCH FROM (ended_at - started_at))) AS avg_secs
                FROM matches WHERE status='completed' AND ended_at IS NOT NULL
                AND started_at >= NOW() - INTERVAL '7 days'`),
    ]);
    res.json({
      matchesToday:         parseInt(matchRes.rows[0].cnt, 10),
      newPlayersToday:      parseInt(playerRes.rows[0].cnt, 10),
      totalPlayers:         parseInt(totalRes.rows[0].cnt, 10),
      avgMatchLengthSeconds: Math.round(parseFloat(avgRes.rows[0].avg_secs) || 0),
    });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Player management ─────────────────────────────────────────────────────────

router.get('/players', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ players: [], total: 0 });
  const db     = require('../db');
  const q      = String(req.query.q || '').trim().slice(0, 100);
  const limit  = Math.min(100, Math.max(1, parseInt(req.query.limit, 10)  || 20));
  const offset = Math.max(0,               parseInt(req.query.offset, 10) || 0);

  const params = [];
  let where = '';
  if (q) {
    // Escape wildcard characters to prevent unintended ILIKE expansion
    const safeQ = q.replace(/[%_\\]/g, '\\$&');
    params.push(`%${safeQ}%`);
    where = `WHERE p.display_name ILIKE $1 ESCAPE '\\' OR CAST(p.id AS TEXT) ILIKE $1 ESCAPE '\\'`;
  }
  try {
    const [rows, totalRes] = await Promise.all([
      db.query(
        `SELECT p.id, p.display_name, p.email, p.email_verified, p.region, p.status, p.created_at,
                CASE
                  WHEN p.google_id IS NOT NULL AND p.password_hash IS NOT NULL THEN 'google+password'
                  WHEN p.google_id IS NOT NULL THEN 'google'
                  WHEN p.password_hash IS NOT NULL THEN 'password'
                  ELSE 'unknown'
                END AS auth_provider,
                r.rating, r.wins, r.losses
         FROM players p
         LEFT JOIN ratings r ON r.player_id = p.id AND r.mode = '2v2_ranked'
         ${where} ORDER BY p.created_at DESC
         LIMIT $${params.length+1} OFFSET $${params.length+2}`,
        [...params, limit, offset]
      ),
      db.query(`SELECT COUNT(*) AS cnt FROM players p ${where}`, params),
    ]);
    res.json({ players: rows.rows, total: parseInt(totalRes.rows[0].cnt, 10) });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.get('/players/:id', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });
  try {
    const [playerRes, ratingsRes, matchesRes, ratingHistRes] = await Promise.all([
      db.query(
        `SELECT id, display_name, email, email_verified, region, status, ban_reason, banned_at, created_at, updated_at,
                (google_id IS NOT NULL)   AS has_google_auth,
                (password_hash IS NOT NULL) AS has_password_auth
         FROM players WHERE id = $1`, [req.params.id]
      ),
      db.query(`SELECT mode, mu, sigma, rating, wins, losses FROM ratings WHERE player_id = $1`, [req.params.id]),
      db.query(
        `SELECT m.id, m.mode, m.status, m.started_at, m.ended_at, mp.lane_index, mp.result
         FROM match_players mp JOIN matches m ON m.id = mp.match_id
         WHERE mp.player_id = $1 ORDER BY m.started_at DESC LIMIT 20`, [req.params.id]
      ),
      db.query(
        `SELECT mode, old_mu, new_mu, delta, created_at
         FROM rating_history WHERE player_id = $1 ORDER BY created_at DESC LIMIT 10`, [req.params.id]
      ),
    ]);
    if (!playerRes.rows[0]) return res.status(404).json({ error: 'Player not found' });
    res.json({
      player: playerRes.rows[0],
      ratings: ratingsRes.rows,
      recentMatches: matchesRes.rows,
      ratingHistory: ratingHistRes.rows,
    });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.post('/players/:id/revoke-tokens', requireAdmin, requirePermission('player.revoke'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });
  try {
    const result = await db.query(`DELETE FROM refresh_tokens WHERE player_id = $1 RETURNING id`, [req.params.id]);
    await audit(db, 'revoke_tokens', 'player', req.params.id, { count: result.rowCount }, req.adminEmail, req.ip);
    res.json({ revoked: result.rowCount });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Match management ──────────────────────────────────────────────────────────

// GET /admin/matches — must come before /matches/:id
router.get('/matches', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ matches: [], total: 0 });
  const db     = require('../db');
  const limit  = Math.min(100, Math.max(1, parseInt(req.query.limit, 10)  || 20));
  const offset = Math.max(0,               parseInt(req.query.offset, 10) || 0);
  const conditions = []; const params = [];
  if (req.query.status && ['in_progress','completed','abandoned'].includes(req.query.status)) {
    params.push(req.query.status); conditions.push(`m.status = $${params.length}`);
  }
  if (req.query.mode && ['classic','multilane','2v2_ranked'].includes(req.query.mode)) {
    params.push(req.query.mode); conditions.push(`m.mode = $${params.length}`);
  }
  const where = conditions.length ? 'WHERE ' + conditions.join(' AND ') : '';
  try {
    const [matchesRes, totalRes] = await Promise.all([
      db.query(
        `SELECT m.id, m.room_id, m.mode, m.status, m.started_at, m.ended_at, m.winner_lane,
                EXTRACT(EPOCH FROM (COALESCE(m.ended_at,NOW()) - m.started_at))::int AS duration_secs,
                json_agg(json_build_object('player_id',mp.player_id,'lane',mp.lane_index,'result',mp.result)
                         ORDER BY mp.lane_index) FILTER (WHERE mp.id IS NOT NULL) AS players
         FROM matches m LEFT JOIN match_players mp ON mp.match_id = m.id ${where}
         GROUP BY m.id ORDER BY m.started_at DESC LIMIT $${params.length+1} OFFSET $${params.length+2}`,
        [...params, limit, offset]
      ),
      db.query(`SELECT COUNT(*) AS cnt FROM matches m ${where}`, params),
    ]);
    res.json({ matches: matchesRes.rows, total: parseInt(totalRes.rows[0].cnt, 10) });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/matches/live — must come before /matches/:id
router.get('/matches/live', requireAdmin, (req, res) => {
  const gamesByRoomId = req.app.locals.gamesByRoomId || new Map();
  const mlRoomsByCode = req.app.locals.mlRoomsByCode || new Map();

  const live = [];
  for (const [roomId, entry] of gamesByRoomId) {
    let code = null, playerNames = [], playerCount = 0;
    for (const [c, room] of mlRoomsByCode) {
      if (room.roomId !== roomId) continue;
      code = c;
      playerNames = (room.players || []).map(sid => room.playerNames?.get(sid) || 'Player');
      playerCount = (room.players || []).length + (room.aiPlayers || []).length;
      playerNames.push(...(room.aiPlayers || []).map(a => `CPU(${a.difficulty})`));
      break;
    }
    live.push({ roomId, code, mode: 'multilane', playerCount, playerNames, phase: entry.game?.phase || 'unknown' });
  }
  res.json({ live });
});

// GET /admin/matches/:id — full match detail
router.get('/matches/:id', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid match ID' });
  try {
    const [matchRes, playersRes] = await Promise.all([
      db.query(`SELECT * FROM matches WHERE id = $1`, [req.params.id]),
      db.query(
        `SELECT mp.id, mp.lane_index, mp.result,
                p.id AS player_id, p.display_name, p.status AS player_status
         FROM match_players mp JOIN players p ON p.id = mp.player_id
         WHERE mp.match_id = $1 ORDER BY mp.lane_index`, [req.params.id]
      ),
    ]);
    if (!matchRes.rows[0]) return res.status(404).json({ error: 'Match not found' });
    res.json({ match: matchRes.rows[0], players: playersRes.rows });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/matches/:id/combat-log
router.get('/matches/:id/combat-log', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid match ID' });
  try {
    const r = await db.query(`SELECT combat_log FROM matches WHERE id = $1`, [req.params.id]);
    if (!r.rows[0]) return res.status(404).json({ error: 'Match not found' });
    res.json({ events: r.rows[0].combat_log || [] });
  } catch (err) {
    log.error('[admin] combat-log route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/matches/:id/terminate
router.post('/matches/:id/terminate', requireAdmin, requirePermission('match.terminate'), async (req, res) => {
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid match ID' });
  const terminate     = req.app.locals.terminateMatch;
  const gamesByRoomId = req.app.locals.gamesByRoomId || new Map();
  if (!terminate) return res.status(503).json({ error: 'terminateMatch not available' });

  let targetRoomId = null;
  if (process.env.DATABASE_URL) {
    const db = require('../db');
    const r = await db.query(`SELECT room_id FROM matches WHERE id = $1`, [req.params.id]).catch(() => null);
    if (r?.rows[0]) targetRoomId = r.rows[0].room_id;
  }
  if (!targetRoomId || !gamesByRoomId.has(targetRoomId)) {
    return res.status(404).json({ error: 'Match not found or not currently active' });
  }
  const ok = await terminate(targetRoomId, 'admin');
  if (!ok) return res.status(404).json({ error: 'Match already ended' });
  if (process.env.DATABASE_URL) {
    const db = require('../db');
    await audit(db, 'terminate_match', 'match', req.params.id, { roomId: targetRoomId }, req.adminEmail, req.ip);
  }
  res.json({ terminated: true, roomId: targetRoomId });
});

// ── Analytics ─────────────────────────────────────────────────────────────────

router.get('/analytics', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) {
    return res.json({ matchStats: {}, modeBreakdown: [], ratingBuckets: [], topPlayers: [], last7d: [], last30d: [] });
  }
  const db = require('../db');
  try {
    const [matchStatsRes, modeRes, ratingRes, topRes, last7dRes] = await Promise.all([
      db.query(`
        SELECT
          COUNT(*)                                                             AS total,
          COUNT(*) FILTER (WHERE status='completed')                          AS completed,
          COUNT(*) FILTER (WHERE status='abandoned')                          AS abandoned,
          COUNT(*) FILTER (WHERE status='in_progress')                        AS in_progress,
          ROUND(AVG(EXTRACT(EPOCH FROM (ended_at - started_at)))
                FILTER (WHERE status='completed' AND ended_at IS NOT NULL))   AS avg_secs,
          COUNT(*) FILTER (WHERE started_at >= NOW() - INTERVAL '24 hours')   AS today,
          COUNT(*) FILTER (WHERE started_at >= NOW() - INTERVAL '7 days')     AS last_7d,
          COUNT(*) FILTER (WHERE started_at >= NOW() - INTERVAL '30 days')    AS last_30d
        FROM matches`),
      db.query(`SELECT mode, COUNT(*) AS cnt FROM matches GROUP BY mode ORDER BY cnt DESC`),
      db.query(`SELECT FLOOR(rating/100)*100 AS bucket, COUNT(*) AS cnt
                FROM ratings WHERE mode='2v2_ranked' GROUP BY bucket ORDER BY bucket`),
      db.query(`
        SELECT p.display_name, r.rating, r.wins, r.losses,
               ROUND(r.wins::numeric/NULLIF(r.wins+r.losses,0)*100) AS win_pct
        FROM ratings r JOIN players p ON p.id = r.player_id
        WHERE r.mode='2v2_ranked' AND p.status='active'
        ORDER BY r.rating DESC LIMIT 10`),
      db.query(`
        SELECT DATE_TRUNC('day', started_at) AS day, COUNT(*) AS cnt
        FROM matches WHERE started_at >= NOW() - INTERVAL '7 days'
        GROUP BY day ORDER BY day`),
    ]);
    res.json({
      matchStats: matchStatsRes.rows[0],
      modeBreakdown: modeRes.rows,
      ratingBuckets: ratingRes.rows,
      topPlayers: topRes.rows,
      last7d: last7dRes.rows,
    });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Anti-cheat ────────────────────────────────────────────────────────────────

router.get('/anticheat', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ flags: [] });
  const db = require('../db');
  try {
    const [newHighWr, extremeWr, newBurst] = await Promise.all([
      // New accounts (< 30d) with high win rate (≥75%) and at least 5 games
      db.query(`
        SELECT p.id, p.display_name, p.created_at, p.status,
               r.wins, r.losses, r.rating,
               ROUND(r.wins::numeric/NULLIF(r.wins+r.losses,0)*100) AS win_pct,
               'new_high_wr' AS signal
        FROM players p JOIN ratings r ON r.player_id = p.id AND r.mode = '2v2_ranked'
        WHERE p.created_at >= NOW() - INTERVAL '30 days'
          AND r.wins + r.losses >= 5
          AND r.wins::numeric / NULLIF(r.wins+r.losses,0) >= 0.75
        ORDER BY win_pct DESC LIMIT 50`),
      // Extreme performers (≥90% win rate, ≥10 games, any age)
      db.query(`
        SELECT p.id, p.display_name, p.created_at, p.status,
               r.wins, r.losses, r.rating,
               ROUND(r.wins::numeric/NULLIF(r.wins+r.losses,0)*100) AS win_pct,
               'extreme_wr' AS signal
        FROM players p JOIN ratings r ON r.player_id = p.id AND r.mode = '2v2_ranked'
        WHERE r.wins + r.losses >= 10
          AND r.wins::numeric / NULLIF(r.wins+r.losses,0) >= 0.90
          AND p.status = 'active'
        ORDER BY win_pct DESC LIMIT 50`),
      // Very new accounts with many games (burst grinder, possible bot)
      db.query(`
        SELECT p.id, p.display_name, p.created_at, p.status,
               r.wins + r.losses AS total_games,
               r.wins, r.losses, r.rating,
               NULL::numeric AS win_pct,
               'burst_new' AS signal
        FROM players p JOIN ratings r ON r.player_id = p.id AND r.mode = '2v2_ranked'
        WHERE p.created_at >= NOW() - INTERVAL '7 days'
          AND r.wins + r.losses >= 20
        ORDER BY total_games DESC LIMIT 30`),
    ]);

    const seen = new Set();
    const flags = [];
    for (const row of [...newHighWr.rows, ...extremeWr.rows, ...newBurst.rows]) {
      const key = `${row.id}:${row.signal}`;
      if (!seen.has(key)) { seen.add(key); flags.push(row); }
    }
    res.json({ flags });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Rating adjustment ─────────────────────────────────────────────────────────

router.post('/players/:id/adjust-rating', requireAdmin, requirePermission('rating.adjust'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });
  const { mode, mu, sigma } = req.body;
  if (!['2v2_ranked', '2v2_casual', '1v1_ranked', '1v1_casual', 'ffa_ranked', 'ffa_casual'].includes(mode)) return res.status(400).json({ error: 'Invalid mode' });
  const mu_n = Number(mu), sigma_n = Number(sigma);
  if (!Number.isFinite(mu_n) || !Number.isFinite(sigma_n) || sigma_n <= 0) {
    return res.status(400).json({ error: 'mu/sigma must be finite numbers; sigma > 0' });
  }
  try {
    const result = await db.query(
      `INSERT INTO ratings (player_id, mode, mu, sigma, rating, updated_at)
       VALUES ($1,$2,$3,$4,$5,NOW())
       ON CONFLICT (player_id,mode) DO UPDATE SET
         mu=EXCLUDED.mu, sigma=EXCLUDED.sigma, rating=EXCLUDED.rating, updated_at=NOW()
       RETURNING player_id, mode, mu, sigma, rating, wins, losses`,
      [req.params.id, mode, mu_n, sigma_n, mu_n - 3 * sigma_n]
    );
    await audit(db, 'adjust_rating', 'player', req.params.id, { mode, mu: mu_n, sigma: sigma_n }, req.adminEmail, req.ip);
    res.json({ rating: result.rows[0] });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Player bans ───────────────────────────────────────────────────────────────

router.post('/players/:id/ban', requireAdmin, requirePermission('player.ban'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });
  const reason = String(req.body?.reason || '').trim().slice(0, 200) || 'Banned by admin';
  try {
    const result = await db.query(
      `UPDATE players SET status='suspended', ban_reason=$1, banned_at=NOW(), updated_at=NOW()
       WHERE id=$2 RETURNING id, display_name, status, ban_reason, banned_at`,
      [reason, req.params.id]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });
    // Invalidate ban cache so next socket connect sees the new status immediately
    req.app.locals.playerBanCache?.delete(req.params.id);
    await audit(db, 'ban_player', 'player', req.params.id, { reason }, req.adminEmail, req.ip);
    res.json({ player: result.rows[0] });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.delete('/players/:id/ban', requireAdmin, requirePermission('player.ban'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });
  try {
    const result = await db.query(
      `UPDATE players SET status='active', ban_reason=NULL, banned_at=NULL, updated_at=NOW()
       WHERE id=$1 RETURNING id, display_name, status`,
      [req.params.id]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });
    req.app.locals.playerBanCache?.delete(req.params.id);
    await audit(db, 'unban_player', 'player', req.params.id, null, req.adminEmail, req.ip);
    res.json({ player: result.rows[0] });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Season management ─────────────────────────────────────────────────────────

router.get('/seasons', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ seasons: [] });
  const db = require('../db');
  try {
    const result = await db.query(`SELECT id, name, start_date, end_date, is_active FROM seasons ORDER BY start_date DESC`);
    res.json({ seasons: result.rows });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.post('/seasons', requireAdmin, requirePermission('season.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const name = String(req.body.name || '').trim().slice(0, 80);
  if (!name) return res.status(400).json({ error: 'Season name required' });
  try {
    const existing = await db.query(`SELECT id FROM seasons WHERE is_active=true LIMIT 1`);
    if (existing.rows.length) return res.status(409).json({ error: 'An active season already exists' });
    const result = await db.query(
      `INSERT INTO seasons (name, start_date, is_active) VALUES ($1,NOW(),true)
       RETURNING id, name, start_date, is_active`, [name]
    );
    await audit(db, 'create_season', 'season', result.rows[0].id, { name }, req.adminEmail, req.ip);
    res.status(201).json({ season: result.rows[0] });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.post('/seasons/:id/close', requireAdmin, requirePermission('season.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const season = require('../services/season');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid season ID' });
  try {
    const check = await db.query(`SELECT id, name, is_active FROM seasons WHERE id=$1`, [req.params.id]);
    if (!check.rows[0])          return res.status(404).json({ error: 'Season not found' });
    if (!check.rows[0].is_active) return res.status(409).json({ error: 'Season already closed' });
    const { snapshotCount, resetCount } = await season.closeSeason(db, req.params.id);
    await audit(db, 'close_season', 'season', req.params.id, { snapshotCount, resetCount }, req.adminEmail, req.ip);
    res.json({ closed: true, seasonId: req.params.id, snapshotCount, resetCount });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Feature flags ─────────────────────────────────────────────────────────────

const DEFAULT_FLAGS = [
  { name: 'maintenance_mode',        enabled: false, notes: 'Block new connections' },
  { name: 'ranked_queue_enabled',    enabled: true,  notes: 'Enable ranked queue' },
  { name: 'new_player_registration', enabled: true,  notes: 'Allow new registrations' },
  { name: 'casual_queue_enabled',    enabled: true,  notes: 'Enable casual queue' },
];

router.get('/flags', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ flags: DEFAULT_FLAGS });
  const db = require('../db');
  try {
    const result = await db.query(`SELECT name, enabled, notes, updated_at FROM feature_flags ORDER BY name`);
    res.json({ flags: result.rows.length ? result.rows : DEFAULT_FLAGS });
  } catch (err) {
    res.json({ flags: DEFAULT_FLAGS });
  }
});

router.patch('/flags/:name', requireAdmin, requirePermission('flag.write'), async (req, res) => {
  if (!FLAG_NAME_RE.test(req.params.name)) return res.status(400).json({ error: 'Invalid flag name' });
  const { enabled, notes } = req.body;
  if (typeof enabled !== 'boolean') return res.status(400).json({ error: 'enabled must be boolean' });
  if (!process.env.DATABASE_URL) return res.json({ flag: { name: req.params.name, enabled } });
  const db = require('../db');
  try {
    const result = await db.query(
      `INSERT INTO feature_flags (name, enabled, notes, updated_at) VALUES ($1,$2,$3,NOW())
       ON CONFLICT (name) DO UPDATE SET
         enabled=EXCLUDED.enabled, notes=COALESCE($3,feature_flags.notes), updated_at=NOW()
       RETURNING name, enabled, notes, updated_at`,
      [req.params.name, enabled, notes !== undefined ? String(notes).slice(0, 200) : null]
    );
    await audit(db, 'set_flag', 'flag', req.params.name, { enabled }, req.adminEmail, req.ip);
    res.json({ flag: result.rows[0] });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Game config ───────────────────────────────────────────────────────────────

// GET /admin/game-config — active config for a mode (defaults if nothing published)
router.get('/game-config', requireAdmin, async (req, res) => {
  const { CLASSIC, MULTILANE } = require('../gameDefaults');
  const defaults = { classic: CLASSIC, multilane: MULTILANE };

  if (!process.env.DATABASE_URL) return res.json({ classic: defaults.classic, multilane: defaults.multilane, dbSource: false });
  const db  = require('../db');
  const cfg = {};
  try {
    const r = await db.query(
      `SELECT mode, config_json FROM game_configs WHERE is_active=true`
    );
    for (const row of r.rows) cfg[row.mode] = row.config_json;
  } catch { /* use defaults */ }

  res.json({
    classic:   cfg.classic   || defaults.classic,
    multilane: cfg.multilane || defaults.multilane,
    dbSource: Object.keys(cfg).length > 0,
  });
});

// GET /admin/game-config/history — all versions for a mode
router.get('/game-config/history', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ versions: [] });
  const db   = require('../db');
  const mode = req.query.mode || 'classic';
  if (!['classic','multilane'].includes(mode)) return res.status(400).json({ error: 'Invalid mode' });
  try {
    const r = await db.query(
      `SELECT id, mode, version, label, notes, published_by, published_at, is_active
       FROM game_configs WHERE mode=$1 ORDER BY published_at DESC LIMIT 50`, [mode]
    );
    res.json({ versions: r.rows });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/branding — current branding config
router.get('/branding', requireAdmin, async (_req, res) => {
  if (!process.env.DATABASE_URL) {
    return res.json({ branding: { ...branding.DEFAULT_BRANDING, updatedAt: null, updatedBy: null } });
  }
  try {
    const db = require('../db');
    const current = await branding.getBranding(db);
    res.json({ branding: current });
  } catch (err) {
    log.error('[admin] GET /branding failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PUT /admin/branding — update branding config
router.put('/branding', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  try {
    const db = require('../db');
    const saved = await branding.saveBranding(db, req.body?.branding || req.body || {}, req.adminEmail);
    await audit(db, 'update_branding', 'branding_settings', 'singleton', saved, req.adminEmail, req.ip);
    res.json({ branding: saved });
  } catch (err) {
    log.error('[admin] PUT /branding failed', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/game-config — publish a new config version
router.post('/game-config', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { mode, config, label, notes, activate = true } = req.body;
  if (!['classic','multilane'].includes(mode)) return res.status(400).json({ error: 'mode must be classic or multilane' });
  if (!config || typeof config !== 'object') return res.status(400).json({ error: 'config object required' });

  // Basic validation
  if (config.unitDefs) {
    for (const [name, u] of Object.entries(config.unitDefs)) {
      if (!Number.isFinite(u.cost) || u.cost <= 0) return res.status(400).json({ error: `Unit ${name}: cost must be > 0` });
      if (!Number.isFinite(u.hp)   || u.hp   <= 0) return res.status(400).json({ error: `Unit ${name}: hp must be > 0` });
      if (!Number.isFinite(u.dmg)  || u.dmg  <= 0) return res.status(400).json({ error: `Unit ${name}: dmg must be > 0` });
    }
  }
  if (config.towerDefs) {
    for (const [name, t] of Object.entries(config.towerDefs)) {
      if (!Number.isFinite(t.cost) || t.cost <= 0) return res.status(400).json({ error: `Tower ${name}: cost must be > 0` });
      if (!Number.isFinite(t.dmg)  || t.dmg  <= 0) return res.status(400).json({ error: `Tower ${name}: dmg must be > 0` });
    }
  }

  try {
    const gameConfig = require('../gameConfig');
    const versionRes = await db.query(
      `SELECT COALESCE(MAX(version),0)+1 AS next FROM game_configs WHERE mode=$1`, [mode]
    );
    const version = versionRes.rows[0].next;

    if (activate) {
      // Deactivate existing active config for this mode
      await db.query(`UPDATE game_configs SET is_active=false WHERE mode=$1 AND is_active=true`, [mode]);
    }
    const result = await db.query(
      `INSERT INTO game_configs (mode, version, label, config_json, notes, published_by, is_active)
       VALUES ($1,$2,$3,$4,$5,$6,$7) RETURNING id, mode, version, label, is_active, published_at`,
      [mode, version, (label || '').slice(0, 80) || null,
       JSON.stringify(config), (notes || '').slice(0, 500) || null,
       req.adminEmail || 'ADMIN_SECRET', !!activate]
    );
    await audit(db, 'publish_config', 'game_config', result.rows[0].id,
      { mode, version, label, activate }, req.adminEmail, req.ip);
    if (activate) gameConfig.setActiveConfig(mode, config);
    res.status(201).json({ config: result.rows[0], note: activate ? 'Changes are active for new matches now.' : 'Saved without activating.' });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/game-config/:id/activate — rollback to a previous version
router.post('/game-config/:id/activate', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid config ID' });
  try {
    const gameConfig = require('../gameConfig');
    const check = await db.query(`SELECT id, mode, version, label FROM game_configs WHERE id=$1`, [req.params.id]);
    if (!check.rows[0]) return res.status(404).json({ error: 'Config version not found' });
    await db.query(`UPDATE game_configs SET is_active=false WHERE mode=$1`, [check.rows[0].mode]);
    await db.query(`UPDATE game_configs SET is_active=true  WHERE id=$1`,  [req.params.id]);
    const active = await db.query(`SELECT config_json FROM game_configs WHERE id=$1`, [req.params.id]);
    if (active.rows[0]?.config_json) gameConfig.setActiveConfig(check.rows[0].mode, active.rows[0].config_json);
    await audit(db, 'activate_config', 'game_config', req.params.id,
      { mode: check.rows[0].mode, version: check.rows[0].version }, req.adminEmail, req.ip);
    res.json({ activated: true, config: check.rows[0], note: 'Changes are active for new matches now.' });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Tower Management ──────────────────────────────────────────────────────────

// POST /admin/assets/upload-image
// Body: { fileName, mimeType, dataBase64 } where dataBase64 can be raw base64 or a data URL.
router.post('/assets/upload-image', requireAdmin, requirePermission('config.write'), async (req, res) => {
  const { fileName, mimeType, dataBase64 } = req.body || {};
  const ext = IMAGE_MIME_EXT[String(mimeType || '').toLowerCase()];
  if (!ext) {
    return res.status(400).json({ error: 'Unsupported image type. Use PNG, JPG, WebP, GIF, or SVG.' });
  }
  if (!dataBase64 || typeof dataBase64 !== 'string') {
    return res.status(400).json({ error: 'Image payload required' });
  }

  try {
    const match = String(dataBase64).match(/^data:[^;]+;base64,(.+)$/);
    const rawB64 = match ? match[1] : dataBase64;
    const bytes = Buffer.from(rawB64, 'base64');
    if (!bytes.length) return res.status(400).json({ error: 'Invalid image payload' });
    if (bytes.length > MAX_ASSET_UPLOAD_BYTES) {
      return res.status(400).json({ error: 'Image too large. Max size is 2MB.' });
    }

    const adminAssetRoot = path.join(__dirname, '..', 'admin-client');
    if (!fs.existsSync(path.join(adminAssetRoot, 'assets'))) {
      throw new Error(`Admin asset root missing at ${adminAssetRoot}`);
    }
    const uploadDir = path.join(adminAssetRoot, 'assets', 'uploads', 'towers');
    await fsp.mkdir(uploadDir, { recursive: true });

    const safeBase = path
      .basename(String(fileName || 'tower_asset'))
      .replace(/\.[^.]+$/, '')
      .replace(/[^a-zA-Z0-9_-]/g, '_')
      .slice(0, 48) || 'tower_asset';
    const finalName = `${Date.now()}-${crypto.randomBytes(6).toString('hex')}-${safeBase}${ext}`;
    const filePath = path.join(uploadDir, finalName);

    await fsp.writeFile(filePath, bytes);
    return res.status(201).json({ url: `/assets/uploads/towers/${finalName}`, size: bytes.length });
  } catch (err) {
    log.error('[admin] POST /assets/upload-image error', { err: err.message });
    return res.status(500).json({ error: 'Image upload failed' });
  }
});

// GET /admin/towers — list all towers (with ability count)
router.get('/towers', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ towers: [] });
  const db = require('../db');
  try {
    const rows = await db.query(`
      SELECT t.*,
        COALESCE(json_agg(
          json_build_object('ability_key', a.ability_key, 'params', a.params)
          ORDER BY a.id
        ) FILTER (WHERE a.id IS NOT NULL), '[]') AS abilities
      FROM towers t
      LEFT JOIN tower_ability_assignments a ON a.tower_id = t.id
      GROUP BY t.id
      ORDER BY t.id`
    );
    res.json({ towers: rows.rows });
  } catch (err) {
    log.error('[admin] GET /towers error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/towers/:id — single tower with abilities
router.get('/towers/:id', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid tower ID' });
  try {
    const [tRow, aRows] = await Promise.all([
      db.query('SELECT * FROM towers WHERE id=$1', [id]),
      db.query('SELECT * FROM tower_ability_assignments WHERE tower_id=$1 ORDER BY id', [id]),
    ]);
    if (!tRow.rows[0]) return res.status(404).json({ error: 'Tower not found' });
    res.json({ tower: { ...tRow.rows[0], abilities: aRows.rows } });
  } catch (err) {
    log.error('[admin] GET /towers/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/towers — create tower
router.post('/towers', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const {
    name, description = '', category = 'damage', enabled = true,
    attack_damage, attack_speed = 1.0, range, projectile_speed, splash_radius, damage_type = 'NORMAL',
    targeting_options = 'first', base_cost, upgrade_cost_mult = 1.0,
    damage_scaling = 0.12, range_scaling = 0.015, attack_speed_scaling = 0.015,
    icon_url, sprite_url, projectile_url, animation_url,
  } = req.body;

  if (!name || !name.trim()) return res.status(400).json({ error: 'name required' });
  if (!Number.isFinite(+attack_damage) || +attack_damage <= 0)
    return res.status(400).json({ error: 'attack_damage must be > 0' });
  if (!Number.isFinite(+range) || +range <= 0)
    return res.status(400).json({ error: 'range must be > 0' });
  if (!Number.isFinite(+base_cost) || +base_cost <= 0)
    return res.status(400).json({ error: 'base_cost must be > 0' });

  const VALID_CATS  = ['damage','slow_control','aura_support','economy','special'];
  const VALID_DTYPE = ['NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE'];
  if (!VALID_CATS.includes(category))  return res.status(400).json({ error: 'Invalid category' });
  if (!VALID_DTYPE.includes(damage_type)) return res.status(400).json({ error: 'Invalid damage_type' });

  try {
    const r = await db.query(`
      INSERT INTO towers (name, description, category, enabled,
        attack_damage, attack_speed, range, projectile_speed, splash_radius, damage_type,
        targeting_options, base_cost, upgrade_cost_mult,
        damage_scaling, range_scaling, attack_speed_scaling,
        icon_url, sprite_url, projectile_url, animation_url)
      VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20)
      RETURNING *`,
      [name.trim(), description, category, !!enabled,
       +attack_damage, +attack_speed, +range,
       projectile_speed != null ? +projectile_speed : null,
       splash_radius    != null ? +splash_radius    : null,
       damage_type, targeting_options, +base_cost, +upgrade_cost_mult,
       +damage_scaling, +range_scaling, +attack_speed_scaling,
       icon_url || null, sprite_url || null, projectile_url || null, animation_url || null]
    );
    await audit(db, 'create_tower', 'tower', r.rows[0].id,
      { name: r.rows[0].name }, req.adminEmail, req.ip);
    res.status(201).json({ tower: r.rows[0] });
  } catch (err) {
    log.error('[admin] POST /towers error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PATCH /admin/towers/:id — update tower
router.patch('/towers/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid tower ID' });

  const allowed = [
    'name','description','category','enabled',
    'attack_damage','attack_speed','range','projectile_speed','splash_radius','damage_type',
    'targeting_options','base_cost','upgrade_cost_mult',
    'damage_scaling','range_scaling','attack_speed_scaling',
    'icon_url','sprite_url','projectile_url','animation_url',
  ];
  const VALID_CATS  = ['damage','slow_control','aura_support','economy','special'];
  const VALID_DTYPE = ['NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE'];

  const sets = []; const vals = [];
  for (const key of allowed) {
    if (!(key in req.body)) continue;
    let v = req.body[key];
    if (key === 'category'    && !VALID_CATS.includes(v))   return res.status(400).json({ error: 'Invalid category' });
    if (key === 'damage_type' && !VALID_DTYPE.includes(v))  return res.status(400).json({ error: 'Invalid damage_type' });
    if (['attack_damage','attack_speed','range','projectile_speed','splash_radius',
         'base_cost','upgrade_cost_mult','damage_scaling','range_scaling','attack_speed_scaling'].includes(key)) {
      if (v !== null && v !== '') v = +v;
    }
    if (key === 'enabled') v = !!v;
    sets.push(`${key}=$${vals.length + 1}`);
    vals.push(v === '' ? null : v);
  }
  if (!sets.length) return res.status(400).json({ error: 'Nothing to update' });
  sets.push(`updated_at=NOW()`);
  vals.push(id);

  try {
    const r = await db.query(
      `UPDATE towers SET ${sets.join(',')} WHERE id=$${vals.length} RETURNING *`, vals
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Tower not found' });
    await audit(db, 'update_tower', 'tower', id,
      { fields: Object.keys(req.body) }, req.adminEmail, req.ip);
    res.json({ tower: r.rows[0] });
  } catch (err) {
    log.error('[admin] PATCH /towers/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/towers/:id — delete tower
router.delete('/towers/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid tower ID' });
  try {
    const r = await db.query('DELETE FROM towers WHERE id=$1 RETURNING id, name', [id]);
    if (!r.rows[0]) return res.status(404).json({ error: 'Tower not found' });
    await audit(db, 'delete_tower', 'tower', id,
      { name: r.rows[0].name }, req.adminEmail, req.ip);
    res.json({ deleted: true });
  } catch (err) {
    log.error('[admin] DELETE /towers/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/abilities — list all predefined ability templates
router.get('/abilities', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ abilities: [] });
  const db = require('../db');
  try {
    const r = await db.query('SELECT * FROM tower_abilities ORDER BY category, name');
    res.json({ abilities: r.rows });
  } catch (err) {
    log.error('[admin] GET /abilities error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/towers/:id/abilities — attach ability to tower
router.post('/towers/:id/abilities', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const tower_id = parseInt(req.params.id, 10);
  if (!Number.isFinite(tower_id)) return res.status(400).json({ error: 'Invalid tower ID' });
  const { ability_key, params = {} } = req.body;
  if (!ability_key || typeof ability_key !== 'string')
    return res.status(400).json({ error: 'ability_key required' });

  try {
    const check = await db.query('SELECT key FROM tower_abilities WHERE key=$1', [ability_key]);
    if (!check.rows[0]) return res.status(404).json({ error: 'Ability not found' });
    const r = await db.query(`
      INSERT INTO tower_ability_assignments (tower_id, ability_key, params)
      VALUES ($1,$2,$3)
      ON CONFLICT (tower_id, ability_key) DO UPDATE SET params=$3
      RETURNING *`,
      [tower_id, ability_key, JSON.stringify(params)]
    );
    await audit(db, 'assign_ability', 'tower', tower_id,
      { ability_key, params }, req.adminEmail, req.ip);
    res.status(201).json({ assignment: r.rows[0] });
  } catch (err) {
    log.error('[admin] POST /towers/:id/abilities error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PATCH /admin/towers/:id/abilities/:abilityKey — update ability params
router.patch('/towers/:id/abilities/:abilityKey', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const tower_id   = parseInt(req.params.id, 10);
  const ability_key = req.params.abilityKey;
  if (!Number.isFinite(tower_id)) return res.status(400).json({ error: 'Invalid tower ID' });
  const { params = {} } = req.body;
  try {
    const r = await db.query(
      `UPDATE tower_ability_assignments SET params=$1
       WHERE tower_id=$2 AND ability_key=$3 RETURNING *`,
      [JSON.stringify(params), tower_id, ability_key]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Assignment not found' });
    await audit(db, 'update_ability_params', 'tower', tower_id,
      { ability_key, params }, req.adminEmail, req.ip);
    res.json({ assignment: r.rows[0] });
  } catch (err) {
    log.error('[admin] PATCH /towers/:id/abilities/:key error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/towers/:id/abilities/:abilityKey — remove ability from tower
router.delete('/towers/:id/abilities/:abilityKey', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const tower_id   = parseInt(req.params.id, 10);
  const ability_key = req.params.abilityKey;
  if (!Number.isFinite(tower_id)) return res.status(400).json({ error: 'Invalid tower ID' });
  try {
    const r = await db.query(
      `DELETE FROM tower_ability_assignments WHERE tower_id=$1 AND ability_key=$2 RETURNING id`,
      [tower_id, ability_key]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Assignment not found' });
    await audit(db, 'remove_ability', 'tower', tower_id,
      { ability_key }, req.adminEmail, req.ip);
    res.json({ deleted: true });
  } catch (err) {
    log.error('[admin] DELETE /towers/:id/abilities/:key error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── ML Wave Config ────────────────────────────────────────────────────────────
// Replaces the old survival wave builder. Survival mode is retired.
// Each ml_wave_config has a sequence of ml_waves (one unit type per wave,
// with qty and stat multipliers). After the last wave, the last wave repeats
// with +10% HP/DMG per extra round (escalation).

// GET /admin/ml-waves/configs — list all wave configs
router.get('/ml-waves/configs', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ configs: [] });
  const db = require('../db');
  try {
    const r = await db.query(`
      SELECT c.*, COUNT(w.id)::int AS wave_count
      FROM ml_wave_configs c
      LEFT JOIN ml_waves w ON w.config_id = c.id
      GROUP BY c.id
      ORDER BY c.id DESC`
    );
    res.json({ configs: r.rows });
  } catch (err) {
    log.error('[admin] GET /ml-waves/configs error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/ml-waves/configs/:id — get one config with its waves
router.get('/ml-waves/configs/:id', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  try {
    const cr = await db.query(`SELECT * FROM ml_wave_configs WHERE id=$1`, [id]);
    if (!cr.rows[0]) return res.status(404).json({ error: 'Config not found' });
    const wr = await db.query(
      `SELECT * FROM ml_waves WHERE config_id=$1 ORDER BY wave_number`, [id]
    );
    res.json({ config: cr.rows[0], waves: wr.rows });
  } catch (err) {
    log.error('[admin] GET /ml-waves/configs/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/ml-waves/configs — create a new wave config
router.post('/ml-waves/configs', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { name, description } = req.body || {};
  if (!name || typeof name !== 'string' || !name.trim()) {
    return res.status(400).json({ error: 'name is required' });
  }
  try {
    const r = await db.query(
      `INSERT INTO ml_wave_configs (name, description) VALUES ($1, $2) RETURNING *`,
      [name.trim(), description || null]
    );
    res.json({ config: r.rows[0] });
  } catch (err) {
    log.error('[admin] POST /ml-waves/configs error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PATCH /admin/ml-waves/configs/:id — update name/description
router.patch('/ml-waves/configs/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  const { name, description } = req.body || {};
  if (name !== undefined && (typeof name !== 'string' || !name.trim())) {
    return res.status(400).json({ error: 'name must be a non-empty string' });
  }
  try {
    const fields = [], vals = [];
    if (name !== undefined) { fields.push(`name=$${vals.push(name.trim())}`); }
    if (description !== undefined) { fields.push(`description=$${vals.push(description || null)}`); }
    if (fields.length === 0) return res.status(400).json({ error: 'No fields to update' });
    vals.push(id);
    const r = await db.query(
      `UPDATE ml_wave_configs SET ${fields.join(',')} WHERE id=$${vals.length} RETURNING *`, vals
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Config not found' });
    res.json({ config: r.rows[0] });
  } catch (err) {
    log.error('[admin] PATCH /ml-waves/configs/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/ml-waves/configs/:id — delete a config (cascades to waves)
router.delete('/ml-waves/configs/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  try {
    await db.query(`DELETE FROM ml_wave_configs WHERE id=$1`, [id]);
    res.json({ ok: true });
  } catch (err) {
    log.error('[admin] DELETE /ml-waves/configs/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/ml-waves/configs/:id/set-default — mark one config as the default
router.post('/ml-waves/configs/:id/set-default', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  try {
    await db.query(`UPDATE ml_wave_configs SET is_default=FALSE`);
    const r = await db.query(
      `UPDATE ml_wave_configs SET is_default=TRUE WHERE id=$1 RETURNING *`, [id]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Config not found' });
    res.json({ config: r.rows[0] });
  } catch (err) {
    log.error('[admin] POST /ml-waves/configs/:id/set-default error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PUT /admin/ml-waves/configs/:id/waves — bulk upsert full wave list for a config
// Body: { waves: [{ wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult }] }
router.put('/ml-waves/configs/:id/waves', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  const { waves } = req.body || {};
  if (!Array.isArray(waves)) return res.status(400).json({ error: 'waves must be an array' });

  for (const w of waves) {
    if (!Number.isInteger(w.wave_number) || w.wave_number < 1) return res.status(400).json({ error: 'wave_number must be a positive integer' });
    if (!w.unit_type || typeof w.unit_type !== 'string') return res.status(400).json({ error: 'unit_type required' });
    if (!Number.isInteger(w.spawn_qty) || w.spawn_qty < 1) return res.status(400).json({ error: 'spawn_qty must be >= 1' });
    for (const f of ['hp_mult', 'dmg_mult', 'speed_mult']) {
      const v = Number(w[f]);
      if (!Number.isFinite(v) || v <= 0) return res.status(400).json({ error: `${f} must be > 0` });
    }
  }

  try {
    const cfgCheck = await db.query(`SELECT id FROM ml_wave_configs WHERE id=$1`, [id]);
    if (!cfgCheck.rows[0]) return res.status(404).json({ error: 'Config not found' });

    await db.query(`DELETE FROM ml_waves WHERE config_id=$1`, [id]);
    for (const w of waves) {
      await db.query(
        `INSERT INTO ml_waves (config_id, wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
         VALUES ($1,$2,$3,$4,$5,$6,$7)`,
        [id, w.wave_number, w.unit_type.toLowerCase().trim(),
         w.spawn_qty, w.hp_mult || 1, w.dmg_mult || 1, w.speed_mult || 1]
      );
    }
    const wr = await db.query(`SELECT * FROM ml_waves WHERE config_id=$1 ORDER BY wave_number`, [id]);
    res.json({ waves: wr.rows });
  } catch (err) {
    log.error('[admin] PUT /ml-waves/configs/:id/waves error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/ml-waves/default — get the default wave config with waves (used by sim at match start)
router.get('/ml-waves/default', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ config: null, waves: [] });
  const db = require('../db');
  try {
    const cr = await db.query(`SELECT * FROM ml_wave_configs WHERE is_default=TRUE LIMIT 1`);
    if (!cr.rows[0]) return res.json({ config: null, waves: [] });
    const wr = await db.query(
      `SELECT * FROM ml_waves WHERE config_id=$1 ORDER BY wave_number`, [cr.rows[0].id]
    );
    res.json({ config: cr.rows[0], waves: wr.rows });
  } catch (err) {
    log.error('[admin] GET /ml-waves/default error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Unit Types CRUD ────────────────────────────────────────────────────────────

const UNIT_TYPE_FIELDS = [
  'key','name','description','enabled','display_to_players',
  'hp','attack_damage','attack_speed','range','path_speed',
  'damage_type','armor_type','damage_reduction_pct',
  'send_cost','build_cost','income','refund_pct',
  'bounty','projectile_travel_ticks','special_props',
  'barracks_scales_hp','barracks_scales_dmg',
  'icon_url','sprite_url','animation_url',
  'sprite_url_front','sprite_url_back',
  'animation_url_front','animation_url_back',
  'idle_sprite_url','idle_sprite_url_front','idle_sprite_url_back',
  'sound_spawn','sound_attack','sound_hit','sound_death',
];
const UNIT_KEY_RE       = /^[a-z_][a-z0-9_]{0,63}$/;
const VALID_DAMAGE_TYPES = ['NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE'];
const VALID_ARMOR_TYPES  = ['UNARMORED','LIGHT','MEDIUM','HEAVY','MAGIC'];
const UNIT_BULK_NUMERIC_FIELDS = new Set([
  'hp',
  'attack_damage',
  'attack_speed',
  'range',
  'path_speed',
  'send_cost',
  'build_cost',
  'income',
  'refund_pct',
  'bounty',
  'projectile_travel_ticks',
  'damage_reduction_pct',
]);
const UNIT_BULK_INTEGER_FIELDS = new Set([
  'send_cost',
  'build_cost',
  'income',
  'refund_pct',
  'bounty',
  'projectile_travel_ticks',
  'damage_reduction_pct',
]);
const REMOTE_CONTENT_TEXT_FIELDS = new Set([
  'content_key',
  'addressables_label',
  'prefab_address',
  'placeholder_key',
  'catalog_url',
  'content_url',
  'version_tag',
  'content_hash',
]);

function isPlainObject(value) {
  return !!value && typeof value === 'object' && !Array.isArray(value);
}

function normalizeRemoteContentBody(body = {}) {
  const normalized = {};
  for (const field of REMOTE_CONTENT_TEXT_FIELDS) {
    if (body[field] !== undefined) {
      const raw = body[field];
      normalized[field] = raw == null ? null : String(raw).trim() || null;
    }
  }
  if (body.dependency_keys !== undefined) {
    normalized.dependency_keys = Array.isArray(body.dependency_keys)
      ? body.dependency_keys.map((v) => String(v || '').trim()).filter(Boolean)
      : body.dependency_keys;
  }
  if (body.metadata !== undefined) normalized.metadata = body.metadata;
  if (body.is_critical !== undefined) normalized.is_critical = !!body.is_critical;
  if (body.enabled !== undefined) normalized.enabled = !!body.enabled;
  return normalized;
}

function validateRemoteContentBody(body) {
  const errs = [];
  if (body.content_key !== undefined && body.content_key !== null && !UNIT_KEY_RE.test(body.content_key)) {
    errs.push('content_key must be lowercase letters/numbers/underscores, 1–64 chars');
  }
  if (body.dependency_keys !== undefined) {
    if (!Array.isArray(body.dependency_keys)) {
      errs.push('dependency_keys must be an array of strings');
    } else if (body.dependency_keys.some((v) => !UNIT_KEY_RE.test(String(v)))) {
      errs.push('dependency_keys must contain lowercase letters/numbers/underscores only');
    }
  }
  if (body.metadata !== undefined && !isPlainObject(body.metadata)) {
    errs.push('metadata must be a plain object');
  }
  return errs;
}

function validateUnitTypeBody(body) {
  const errs = [];
  if (body.key !== undefined && !UNIT_KEY_RE.test(body.key))
    errs.push('key must be lowercase letters/numbers/underscores, 1–64 chars');
  if (body.damage_type !== undefined && !VALID_DAMAGE_TYPES.includes(body.damage_type))
    errs.push('invalid damage_type');
  if (body.armor_type !== undefined && !VALID_ARMOR_TYPES.includes(body.armor_type))
    errs.push('invalid armor_type');
  if (body.damage_reduction_pct !== undefined) {
    const v = Number(body.damage_reduction_pct);
    if (!Number.isFinite(v) || v < 0 || v > 80) errs.push('damage_reduction_pct must be 0–80');
  }
  if (body.refund_pct !== undefined) {
    const v = Number(body.refund_pct);
    if (!Number.isFinite(v) || v < 0 || v > 100) errs.push('refund_pct must be 0–100');
  }
  if (body.bounty !== undefined) {
    const v = Number(body.bounty);
    if (!Number.isFinite(v) || v < 0) errs.push('bounty must be a non-negative number');
  }
  if (body.projectile_travel_ticks !== undefined) {
    const v = Number(body.projectile_travel_ticks);
    if (!Number.isFinite(v) || v < 1) errs.push('projectile_travel_ticks must be >= 1');
  }
  if (body.special_props !== undefined) {
    if (typeof body.special_props !== 'object' || Array.isArray(body.special_props) || body.special_props === null)
      errs.push('special_props must be a plain object');
  }
  return errs;
}

function unitTypeQuery(db, whereClause, vals) {
  return db.query(`
    SELECT u.*,
      CASE
        WHEN ucm.id IS NULL THEN NULL
        ELSE json_build_object(
          'id', ucm.id,
          'content_key', ucm.content_key,
          'addressables_label', ucm.addressables_label,
          'prefab_address', ucm.prefab_address,
          'placeholder_key', ucm.placeholder_key,
          'catalog_url', ucm.catalog_url,
          'content_url', ucm.content_url,
          'version_tag', ucm.version_tag,
          'content_hash', ucm.content_hash,
          'dependency_keys', ucm.dependency_keys,
          'metadata', ucm.metadata,
          'is_critical', ucm.is_critical,
          'enabled', ucm.enabled
        )
      END AS remote_content,
      COALESCE(json_agg(
        json_build_object('ability_key', a.ability_key, 'params', a.params)
        ORDER BY a.id
      ) FILTER (WHERE a.id IS NOT NULL), '[]') AS abilities
    FROM unit_types u
    LEFT JOIN unit_content_metadata ucm ON ucm.unit_type_id = u.id
    LEFT JOIN unit_type_ability_assignments a ON a.unit_type_id = u.id
    ${whereClause || ''}
    GROUP BY u.id, ucm.id
    ORDER BY u.id
  `, vals);
}

function skinQuery(db, whereClause = '', vals = []) {
  return db.query(`
    SELECT s.*,
      CASE
        WHEN scm.id IS NULL THEN NULL
        ELSE json_build_object(
          'id', scm.id,
          'content_key', scm.content_key,
          'addressables_label', scm.addressables_label,
          'prefab_address', scm.prefab_address,
          'placeholder_key', scm.placeholder_key,
          'catalog_url', scm.catalog_url,
          'content_url', scm.content_url,
          'version_tag', scm.version_tag,
          'content_hash', scm.content_hash,
          'dependency_keys', scm.dependency_keys,
          'metadata', scm.metadata,
          'is_critical', scm.is_critical,
          'enabled', scm.enabled
        )
      END AS remote_content
    FROM skin_catalog s
    LEFT JOIN skin_content_metadata scm ON scm.skin_catalog_id = s.id
    ${whereClause}
    ORDER BY s.unit_type, s.name, s.id
  `, vals);
}

// GET /admin/unit-types
router.get('/unit-types', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ unitTypes: [] });
  const db = require('../db');
  try {
    const r = await unitTypeQuery(db, '', []);
    res.json({ unitTypes: r.rows });
  } catch (err) {
    log.error('[admin] GET /unit-types error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/skins
router.get('/skins', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ skins: [] });
  const db = require('../db');
  try {
    const r = await skinQuery(db);
    res.json({ skins: r.rows });
  } catch (err) {
    log.error('[admin] GET /skins error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/unit-types/bulk-update
router.post('/unit-types/bulk-update', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'Database not configured' });
  const db = require('../db');
  const field = String(req.body?.field || '').trim();
  const operation = String(req.body?.operation || '').trim();
  const scope = String(req.body?.scope || 'all').trim();
  const value = Number(req.body?.value);

  if (!UNIT_BULK_NUMERIC_FIELDS.has(field)) {
    return res.status(400).json({ error: 'Unsupported bulk-edit field' });
  }
  if (!['set', 'add', 'multiply'].includes(operation)) {
    return res.status(400).json({ error: 'Unsupported bulk-edit operation' });
  }
  if (!Number.isFinite(value)) {
    return res.status(400).json({ error: 'Bulk-edit value must be numeric' });
  }

  let whereClause = 'TRUE';
  if (scope === 'enabled') {
    whereClause = 'enabled = TRUE';
  } else if (scope === 'moving') {
    whereClause = 'COALESCE(send_cost, 0) > 0 AND NOT (COALESCE(build_cost, 0) > 0 AND COALESCE(range, 0) > 0)';
  } else if (scope === 'fixed') {
    whereClause = 'COALESCE(build_cost, 0) > 0 AND COALESCE(range, 0) > 0 AND COALESCE(send_cost, 0) <= 0';
  } else if (scope === 'both') {
    whereClause = 'COALESCE(send_cost, 0) > 0 AND COALESCE(build_cost, 0) > 0 AND COALESCE(range, 0) > 0';
  } else if (scope !== 'all') {
    return res.status(400).json({ error: 'Unsupported bulk-edit scope' });
  }

  let expr;
  if (operation === 'set') expr = '$1';
  else if (operation === 'add') expr = `COALESCE(${field}, 0) + $1`;
  else expr = `COALESCE(${field}, 0) * $1`;

  if (UNIT_BULK_INTEGER_FIELDS.has(field)) {
    expr = `ROUND((${expr})::numeric)::int`;
  }

  if (['send_cost', 'build_cost', 'income', 'refund_pct', 'bounty', 'projectile_travel_ticks', 'damage_reduction_pct', 'hp', 'attack_damage', 'attack_speed', 'range', 'path_speed'].includes(field)) {
    expr = `GREATEST(0, ${expr})`;
  }
  if (field === 'projectile_travel_ticks') {
    expr = `GREATEST(1, ${expr})`;
  }

  try {
    const r = await db.query(
      `UPDATE unit_types
       SET ${field} = ${expr}, updated_at = NOW()
       WHERE ${whereClause}
       RETURNING id`,
      [value]
    );
    const unitTypesModule = require('../unitTypes');
    await unitTypesModule.reloadUnitTypes();
    res.json({ ok: true, updated: r.rowCount, field, operation, value, scope });
  } catch (err) {
    log.error('[admin] POST /unit-types/bulk-update error', { err: err.message, field, operation, scope });
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/unit-types/:id
router.get('/unit-types/:id', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(404).json({ error: 'Not found' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  try {
    const r = await unitTypeQuery(db, 'WHERE u.id=$1', [id]);
    if (!r.rows[0]) return res.status(404).json({ error: 'Not found' });
    res.json({ unitType: r.rows[0] });
  } catch (err) {
    log.error('[admin] GET /unit-types/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/unit-types/reload  (must be before :id route to avoid ambiguity)
router.post('/unit-types/reload', requireAdmin, requirePermission('config.write'), async (req, res) => {
  try {
    const unitTypesModule = require('../unitTypes');
    await unitTypesModule.reloadUnitTypes();
    res.json({ ok: true, count: unitTypesModule.getAllUnitTypes().length });
  } catch (err) {
    log.error('[admin] POST /unit-types/reload error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/unit-types
router.post('/unit-types', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!req.body.key || !req.body.name) return res.status(400).json({ error: 'key and name required' });
  const errs = validateUnitTypeBody(req.body);
  if (errs.length) return res.status(400).json({ error: errs.join('; ') });
  try {
    const cols = UNIT_TYPE_FIELDS.filter(f => req.body[f] !== undefined);
    const vals = cols.map(f => req.body[f]);
    const placeholders = cols.map((_, i) => `$${i + 1}`).join(', ');
    const r = await db.query(
      `INSERT INTO unit_types (${cols.join(', ')}) VALUES (${placeholders}) RETURNING *`, vals
    );
    const ut = { ...r.rows[0], abilities: [] };
    await audit(db, 'create_unit_type', 'unit_type', ut.id, { key: ut.key }, req.adminEmail, req.ip);
    require('../unitTypes').reloadUnitTypes().catch(() => {});
    res.status(201).json({ unitType: ut });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'A unit type with that key already exists' });
    log.error('[admin] POST /unit-types error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PATCH /admin/unit-types/:id
router.patch('/unit-types/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  const errs = validateUnitTypeBody(req.body);
  if (errs.length) return res.status(400).json({ error: errs.join('; ') });
  const updates = {};
  for (const f of UNIT_TYPE_FIELDS) if (req.body[f] !== undefined) updates[f] = req.body[f];
  if (Object.keys(updates).length === 0) return res.status(400).json({ error: 'Nothing to update' });
  try {
    const setClauses = Object.keys(updates).map((k, i) => `${k}=$${i + 2}`).join(', ');
    const r = await db.query(
      `UPDATE unit_types SET ${setClauses}, updated_at=NOW() WHERE id=$1 RETURNING *`,
      [id, ...Object.values(updates)]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Not found' });
    await audit(db, 'update_unit_type', 'unit_type', id, updates, req.adminEmail, req.ip);
    require('../unitTypes').reloadUnitTypes().catch(() => {});
    res.json({ unitType: r.rows[0] });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'A unit type with that key already exists' });
    log.error('[admin] PATCH /unit-types/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PUT /admin/unit-types/:id/content
router.put('/unit-types/:id/content', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });

  const normalized = normalizeRemoteContentBody(req.body || {});
  const errs = validateRemoteContentBody(normalized);
  if (errs.length) return res.status(400).json({ error: errs.join('; ') });

  const shouldDelete =
    !normalized.content_key &&
    !normalized.addressables_label &&
    !normalized.prefab_address &&
    !normalized.placeholder_key &&
    !normalized.catalog_url &&
    !normalized.content_url &&
    !normalized.version_tag &&
    !normalized.content_hash &&
    normalized.dependency_keys === undefined &&
    normalized.metadata === undefined &&
    normalized.is_critical === undefined &&
    normalized.enabled === undefined;

  try {
    const exists = await db.query('SELECT id, key FROM unit_types WHERE id = $1', [id]);
    if (!exists.rows[0]) return res.status(404).json({ error: 'Unit type not found' });

    if (shouldDelete) {
      await db.query('DELETE FROM unit_content_metadata WHERE unit_type_id = $1', [id]);
    } else {
      const row = await db.query('SELECT content_key FROM unit_content_metadata WHERE unit_type_id = $1', [id]);
      const contentKey = normalized.content_key || row.rows[0]?.content_key || exists.rows[0].key;
      await db.query(
        `INSERT INTO unit_content_metadata (
           unit_type_id, content_key, addressables_label, prefab_address, placeholder_key,
           catalog_url, content_url, version_tag, content_hash, dependency_keys, metadata,
           is_critical, enabled, updated_at
         ) VALUES (
           $1, $2, $3, $4, $5,
           $6, $7, COALESCE($8, '1'), $9, COALESCE($10, '[]'::jsonb), COALESCE($11, '{}'::jsonb),
           COALESCE($12, TRUE), COALESCE($13, TRUE), NOW()
         )
         ON CONFLICT (unit_type_id) DO UPDATE SET
           content_key = EXCLUDED.content_key,
           addressables_label = EXCLUDED.addressables_label,
           prefab_address = EXCLUDED.prefab_address,
           placeholder_key = EXCLUDED.placeholder_key,
           catalog_url = EXCLUDED.catalog_url,
           content_url = EXCLUDED.content_url,
           version_tag = EXCLUDED.version_tag,
           content_hash = EXCLUDED.content_hash,
           dependency_keys = EXCLUDED.dependency_keys,
           metadata = EXCLUDED.metadata,
           is_critical = EXCLUDED.is_critical,
           enabled = EXCLUDED.enabled,
           updated_at = NOW()`,
        [
          id,
          contentKey,
          normalized.addressables_label ?? null,
          normalized.prefab_address ?? null,
          normalized.placeholder_key ?? null,
          normalized.catalog_url ?? null,
          normalized.content_url ?? null,
          normalized.version_tag ?? null,
          normalized.content_hash ?? null,
          normalized.dependency_keys !== undefined ? JSON.stringify(normalized.dependency_keys) : null,
          normalized.metadata !== undefined ? JSON.stringify(normalized.metadata) : null,
          normalized.is_critical ?? null,
          normalized.enabled ?? null,
        ]
      );
    }

    await audit(db, 'upsert_unit_content_metadata', 'unit_type', id, normalized, req.adminEmail, req.ip);
    require('../unitTypes').reloadUnitTypes().catch(() => {});
    const refreshed = await unitTypeQuery(db, 'WHERE u.id=$1', [id]);
    res.json({ remoteContent: refreshed.rows[0]?.remote_content || null, unitType: refreshed.rows[0] || null });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'content_key already exists' });
    log.error('[admin] PUT /unit-types/:id/content error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/unit-types/:id
router.delete('/unit-types/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  try {
    const r = await db.query(`DELETE FROM unit_types WHERE id=$1 RETURNING id, key`, [id]);
    if (!r.rows[0]) return res.status(404).json({ error: 'Not found' });
    await audit(db, 'delete_unit_type', 'unit_type', id, { key: r.rows[0].key }, req.adminEmail, req.ip);
    require('../unitTypes').reloadUnitTypes().catch(() => {});
    res.json({ deleted: true });
  } catch (err) {
    log.error('[admin] DELETE /unit-types/:id error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PUT /admin/skins/:skinKey/content
router.put('/skins/:skinKey/content', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const skinKey = String(req.params.skinKey || '').trim();
  if (!skinKey) return res.status(400).json({ error: 'Invalid skin key' });

  const normalized = normalizeRemoteContentBody(req.body || {});
  const errs = validateRemoteContentBody(normalized);
  if (errs.length) return res.status(400).json({ error: errs.join('; ') });

  const shouldDelete =
    !normalized.content_key &&
    !normalized.addressables_label &&
    !normalized.prefab_address &&
    !normalized.placeholder_key &&
    !normalized.catalog_url &&
    !normalized.content_url &&
    !normalized.version_tag &&
    !normalized.content_hash &&
    normalized.dependency_keys === undefined &&
    normalized.metadata === undefined &&
    normalized.is_critical === undefined &&
    normalized.enabled === undefined;

  try {
    const exists = await db.query('SELECT id FROM skin_catalog WHERE skin_key = $1', [skinKey]);
    if (!exists.rows[0]) return res.status(404).json({ error: 'Skin not found' });

    if (shouldDelete) {
      await db.query('DELETE FROM skin_content_metadata WHERE skin_catalog_id = $1', [exists.rows[0].id]);
    } else {
      const row = await db.query('SELECT content_key FROM skin_content_metadata WHERE skin_catalog_id = $1', [exists.rows[0].id]);
      const contentKey = normalized.content_key || row.rows[0]?.content_key || skinKey;
      await db.query(
        `INSERT INTO skin_content_metadata (
           skin_catalog_id, content_key, addressables_label, prefab_address, placeholder_key,
           catalog_url, content_url, version_tag, content_hash, dependency_keys, metadata,
           is_critical, enabled, updated_at
         ) VALUES (
           $1, $2, $3, $4, $5,
           $6, $7, COALESCE($8, '1'), $9, COALESCE($10, '[]'::jsonb), COALESCE($11, '{}'::jsonb),
           COALESCE($12, FALSE), COALESCE($13, TRUE), NOW()
         )
         ON CONFLICT (skin_catalog_id) DO UPDATE SET
           content_key = EXCLUDED.content_key,
           addressables_label = EXCLUDED.addressables_label,
           prefab_address = EXCLUDED.prefab_address,
           placeholder_key = EXCLUDED.placeholder_key,
           catalog_url = EXCLUDED.catalog_url,
           content_url = EXCLUDED.content_url,
           version_tag = EXCLUDED.version_tag,
           content_hash = EXCLUDED.content_hash,
           dependency_keys = EXCLUDED.dependency_keys,
           metadata = EXCLUDED.metadata,
           is_critical = EXCLUDED.is_critical,
           enabled = EXCLUDED.enabled,
           updated_at = NOW()`,
        [
          exists.rows[0].id,
          contentKey,
          normalized.addressables_label ?? null,
          normalized.prefab_address ?? null,
          normalized.placeholder_key ?? null,
          normalized.catalog_url ?? null,
          normalized.content_url ?? null,
          normalized.version_tag ?? null,
          normalized.content_hash ?? null,
          normalized.dependency_keys !== undefined ? JSON.stringify(normalized.dependency_keys) : null,
          normalized.metadata !== undefined ? JSON.stringify(normalized.metadata) : null,
          normalized.is_critical ?? null,
          normalized.enabled ?? null,
        ]
      );
    }

    await audit(db, 'upsert_skin_content_metadata', 'skin', skinKey, normalized, req.adminEmail, req.ip);
    const refreshed = await skinQuery(db, 'WHERE s.skin_key = $1', [skinKey]);
    res.json({ remoteContent: refreshed.rows[0]?.remote_content || null, skin: refreshed.rows[0] || null });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'content_key already exists' });
    log.error('[admin] PUT /skins/:skinKey/content error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/unit-types/:id/abilities
router.post('/unit-types/:id/abilities', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  const { ability_key, params = {} } = req.body;
  if (!ability_key) return res.status(400).json({ error: 'ability_key required' });
  try {
    const r = await db.query(
      `INSERT INTO unit_type_ability_assignments (unit_type_id, ability_key, params)
       VALUES ($1, $2, $3) RETURNING *`,
      [id, ability_key, JSON.stringify(params)]
    );
    await audit(db, 'attach_unit_ability', 'unit_type', id, { ability_key }, req.adminEmail, req.ip);
    require('../unitTypes').reloadUnitTypes().catch(() => {});
    res.status(201).json({ assignment: r.rows[0] });
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'Ability already attached' });
    if (err.code === '23503') return res.status(404).json({ error: 'Unit type or ability not found' });
    log.error('[admin] POST /unit-types/:id/abilities error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/unit-types/:id/abilities/:key
router.delete('/unit-types/:id/abilities/:key', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const id = parseInt(req.params.id, 10);
  const abilityKey = req.params.key;
  if (!Number.isFinite(id)) return res.status(400).json({ error: 'Invalid id' });
  try {
    const r = await db.query(
      `DELETE FROM unit_type_ability_assignments WHERE unit_type_id=$1 AND ability_key=$2 RETURNING id`,
      [id, abilityKey]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Assignment not found' });
    await audit(db, 'detach_unit_ability', 'unit_type', id, { ability_key: abilityKey }, req.adminEmail, req.ip);
    require('../unitTypes').reloadUnitTypes().catch(() => {});
    res.json({ deleted: true });
  } catch (err) {
    log.error('[admin] DELETE /unit-types/:id/abilities/:key error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Unit Display Fields ────────────────────────────────────────────────────────

// GET /admin/unit-display-fields
router.get('/unit-display-fields', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ fields: [] });
  const db = require('../db');
  try {
    const r = await db.query('SELECT * FROM unit_display_fields ORDER BY sort_order');
    res.json({ fields: r.rows });
  } catch (err) {
    log.error('[admin] GET /unit-display-fields error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PUT /admin/unit-display-fields/:fieldKey
router.put('/unit-display-fields/:fieldKey', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const { fieldKey } = req.params;
  const { visible_to_players } = req.body;
  if (typeof visible_to_players !== 'boolean') {
    return res.status(400).json({ error: 'visible_to_players must be boolean' });
  }
  try {
    const r = await db.query(
      `UPDATE unit_display_fields SET visible_to_players=$1 WHERE field_key=$2 RETURNING *`,
      [visible_to_players, fieldKey]
    );
    if (!r.rows[0]) return res.status(404).json({ error: 'Field not found' });
    await audit(db, 'update_unit_display_field', 'unit_display_field', fieldKey,
      { visible_to_players }, req.adminEmail, req.ip);
    res.json({ field: r.rows[0] });
  } catch (err) {
    log.error('[admin] PUT /unit-display-fields/:fieldKey error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Barracks Levels CRUD ───────────────────────────────────────────────────────

// GET /admin/barracks-levels
router.get('/barracks-levels', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ levels: [] });
  const db = require('../db');
  try {
    const r = await db.query(`SELECT * FROM barracks_levels ORDER BY level`);
    res.json({ levels: r.rows });
  } catch (err) {
    log.error('[admin] GET /barracks-levels error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PUT /admin/barracks-levels/:level
router.put('/barracks-levels/:level', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const level = parseInt(req.params.level, 10);
  if (!Number.isFinite(level) || level < 1 || level > 99)
    return res.status(400).json({ error: 'level must be 1–99' });
  const { multiplier, upgrade_cost = 0, notes = '' } = req.body;
  if (multiplier === undefined) return res.status(400).json({ error: 'multiplier required' });
  const mult = Number(multiplier);
  if (!Number.isFinite(mult) || mult < 0.1 || mult > 100)
    return res.status(400).json({ error: 'multiplier must be 0.1–100' });
  try {
    const r = await db.query(
      `INSERT INTO barracks_levels (level, multiplier, upgrade_cost, notes)
       VALUES ($1, $2, $3, $4)
       ON CONFLICT (level) DO UPDATE SET
         multiplier   = EXCLUDED.multiplier,
         upgrade_cost = EXCLUDED.upgrade_cost,
         notes        = EXCLUDED.notes,
         updated_at   = NOW()
       RETURNING *`,
      [level, mult, Number(upgrade_cost), String(notes)]
    );
    await audit(db, 'upsert_barracks_level', 'barracks_level', level, { multiplier: mult }, req.adminEmail, req.ip);
    require('../barracksLevels').reloadBarracksLevels().catch(() => {});
    res.json({ level: r.rows[0] });
  } catch (err) {
    log.error('[admin] PUT /barracks-levels/:level error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/barracks-levels/:level
router.delete('/barracks-levels/:level', requireAdmin, requirePermission('config.write'), async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  const level = parseInt(req.params.level, 10);
  if (!Number.isFinite(level)) return res.status(400).json({ error: 'Invalid level' });
  try {
    const r = await db.query(`DELETE FROM barracks_levels WHERE level=$1 RETURNING level`, [level]);
    if (!r.rows[0]) return res.status(404).json({ error: 'Level not found' });
    require('../barracksLevels').reloadBarracksLevels().catch(() => {});
    res.json({ deleted: true });
  } catch (err) {
    log.error('[admin] DELETE /barracks-levels/:level error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Audit log ─────────────────────────────────────────────────────────────────

router.get('/audit-log', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ entries: [], total: 0 });
  const db     = require('../db');
  const limit  = Math.min(200, Math.max(1, parseInt(req.query.limit, 10)  || 50));
  const offset = Math.max(0,               parseInt(req.query.offset, 10) || 0);
  try {
    const [rows, total] = await Promise.all([
      db.query(
        `SELECT id, action, target_type, target_id, payload, created_at
         FROM admin_audit_log ORDER BY created_at DESC LIMIT $1 OFFSET $2`,
        [limit, offset]
      ),
      db.query(`SELECT COUNT(*) AS cnt FROM admin_audit_log`),
    ]);
    res.json({ entries: rows.rows, total: parseInt(total.rows[0].cnt, 10) });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Phase H: Asset Packs CRUD ─────────────────────────────────────────────────

router.get('/asset-packs', requireAdmin, async (req, res) => {
  const db = req.app.locals.db;
  try {
    const packs = await db.query(
      `SELECT ap.*,
              COALESCE(json_agg(api ORDER BY api.id) FILTER (WHERE api.id IS NOT NULL), '[]') AS items
         FROM asset_packs ap
         LEFT JOIN asset_pack_items api ON api.pack_id = ap.id
        GROUP BY ap.id
        ORDER BY ap.id`
    );
    res.json(packs.rows);
  } catch (err) {
    log.error('[admin] asset-packs list error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.post('/asset-packs', requireAdmin, requirePermission('config.write'), async (req, res) => {
  const db = req.app.locals.db;
  const { key, name, description = '', enabled = true } = req.body;
  if (!key || !name) return res.status(400).json({ error: 'key and name required' });
  try {
    const r = await db.query(
      `INSERT INTO asset_packs (key, name, description, enabled)
       VALUES ($1, $2, $3, $4) RETURNING *`,
      [key.trim(), name.trim(), description.trim(), !!enabled]
    );
    res.json(r.rows[0]);
  } catch (err) {
    if (err.code === '23505') return res.status(409).json({ error: 'key already exists' });
    log.error('[admin] asset-packs create error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.patch('/asset-packs/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  const db = req.app.locals.db;
  const { name, description, enabled } = req.body;
  try {
    const r = await db.query(
      `UPDATE asset_packs
          SET name        = COALESCE($1, name),
              description = COALESCE($2, description),
              enabled     = COALESCE($3, enabled)
        WHERE id = $4 RETURNING *`,
      [name || null, description != null ? description : null, enabled != null ? !!enabled : null, req.params.id]
    );
    if (!r.rows.length) return res.status(404).json({ error: 'Not found' });
    res.json(r.rows[0]);
  } catch (err) {
    log.error('[admin] asset-packs patch error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.delete('/asset-packs/:id', requireAdmin, requirePermission('config.write'), async (req, res) => {
  const db = req.app.locals.db;
  try {
    await db.query(`DELETE FROM asset_packs WHERE id = $1`, [req.params.id]);
    res.json({ ok: true });
  } catch (err) {
    log.error('[admin] asset-packs delete error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// Asset Pack Items
router.post('/asset-packs/:id/items', requireAdmin, requirePermission('config.write'), async (req, res) => {
  const db = req.app.locals.db;
  const { unit_type_key, asset_slot, url } = req.body;
  if (!unit_type_key || !asset_slot || !url) return res.status(400).json({ error: 'unit_type_key, asset_slot, url required' });
  if (!['icon', 'sprite', 'animation'].includes(asset_slot)) return res.status(400).json({ error: 'invalid asset_slot' });
  try {
    const r = await db.query(
      `INSERT INTO asset_pack_items (pack_id, unit_type_key, asset_slot, url)
       VALUES ($1, $2, $3, $4)
       ON CONFLICT (pack_id, unit_type_key, asset_slot) DO UPDATE SET url = EXCLUDED.url
       RETURNING *`,
      [req.params.id, unit_type_key.trim(), asset_slot, url.trim()]
    );
    res.json(r.rows[0]);
  } catch (err) {
    log.error('[admin] asset-pack-items upsert error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.delete('/asset-packs/:id/items/:itemId', requireAdmin, requirePermission('config.write'), async (req, res) => {
  const db = req.app.locals.db;
  try {
    await db.query(`DELETE FROM asset_pack_items WHERE id = $1 AND pack_id = $2`, [req.params.itemId, req.params.id]);
    res.json({ ok: true });
  } catch (err) {
    log.error('[admin] asset-pack-items delete error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// Public endpoint: resolved asset pack items for a given pack key
router.get('/asset-packs/:key/resolve', async (req, res) => {
  const db = req.app.locals.db;
  try {
    const r = await db.query(
      `SELECT api.unit_type_key, api.asset_slot, api.url
         FROM asset_pack_items api
         JOIN asset_packs ap ON ap.id = api.pack_id
        WHERE ap.key = $1 AND ap.enabled = TRUE`,
      [req.params.key]
    );
    res.json(r.rows);
  } catch (err) {
    log.error('[admin] asset-pack resolve error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

module.exports = router;
