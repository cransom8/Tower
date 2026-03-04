'use strict';

const express  = require('express');
const router   = express.Router();
const jwt      = require('jsonwebtoken');
const bcrypt   = require('bcryptjs');
const log      = require('../logger');

const UUID_RE      = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const FLAG_NAME_RE = /^[a-z_]{1,64}$/;
const EMAIL_RE     = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
const BCRYPT_ROUNDS = 13;
const ADMIN_JWT_TTL = '1h';
const VALID_ROLES   = ['viewer', 'support', 'moderator', 'editor', 'engineer', 'owner'];

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
    const email = p.email;

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
      return res.status(401).json({ error: 'No admin account found for this Google account. Contact an owner to create one.' });
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
    log.error('[admin] POST /auth/google failed', { err: err.message });
    res.status(401).json({ error: 'Google authentication failed' });
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
  if (!secret || req.headers['x-admin-secret'] !== secret) {
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
  const roomsByCode   = req.app.locals.roomsByCode   || new Map();
  const mlRoomsByCode = req.app.locals.mlRoomsByCode || new Map();
  const io            = req.app.locals.io;

  let classicGames = 0, mlGames = 0;
  for (const [roomId] of gamesByRoomId) {
    if (roomId.startsWith('mlroom_')) mlGames++; else classicGames++;
  }
  const lobbyRooms =
    [...roomsByCode.values()].filter(r => !gamesByRoomId.has(r.roomId)).length +
    [...mlRoomsByCode.values()].filter(r => !gamesByRoomId.has(r.roomId)).length;

  res.json({
    connectedSockets: io ? io.sockets.sockets.size : 0,
    activeGames: gamesByRoomId.size,
    classicGames,
    mlGames,
    lobbyRooms,
    adminRole: req.adminRole,
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
  const roomsByCode   = req.app.locals.roomsByCode   || new Map();
  const mlRoomsByCode = req.app.locals.mlRoomsByCode || new Map();
  const io            = req.app.locals.io;

  const live = [];
  for (const [roomId, entry] of gamesByRoomId) {
    const isML    = roomId.startsWith('mlroom_');
    const codeMap = isML ? mlRoomsByCode : roomsByCode;
    let code = null, playerNames = [], playerCount = 0;
    for (const [c, room] of codeMap) {
      if (room.roomId !== roomId) continue;
      code = c;
      playerCount = room.players ? room.players.length : 0;
      if (isML) {
        playerNames = (room.players || []).map(sid => room.playerNames?.get(sid) || 'Player');
        playerCount += (room.aiPlayers || []).length;
        playerNames.push(...(room.aiPlayers || []).map(a => `CPU(${a.difficulty})`));
      } else {
        playerNames = (room.players || []).map(sid => {
          const sock = io?.sockets.sockets.get(sid);
          return sock?.playerDisplayName || 'Guest';
        });
      }
      break;
    }
    live.push({ roomId, code, mode: isML ? 'multilane' : 'classic', playerCount, playerNames, phase: entry.game?.phase || 'unknown' });
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
  if (!['2v2_ranked', '2v2_casual'].includes(mode)) return res.status(400).json({ error: 'Invalid mode' });
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
    res.status(201).json({ config: result.rows[0], note: 'Changes take effect on next server restart.' });
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
    const check = await db.query(`SELECT id, mode, version, label FROM game_configs WHERE id=$1`, [req.params.id]);
    if (!check.rows[0]) return res.status(404).json({ error: 'Config version not found' });
    await db.query(`UPDATE game_configs SET is_active=false WHERE mode=$1`, [check.rows[0].mode]);
    await db.query(`UPDATE game_configs SET is_active=true  WHERE id=$1`,  [req.params.id]);
    await audit(db, 'activate_config', 'game_config', req.params.id,
      { mode: check.rows[0].mode, version: check.rows[0].version }, req.adminEmail, req.ip);
    res.json({ activated: true, config: check.rows[0], note: 'Changes take effect on next server restart.' });
  } catch (err) {
    log.error('[admin] route error', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Tower Management ──────────────────────────────────────────────────────────

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

module.exports = router;
