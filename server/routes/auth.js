'use strict';

const express     = require('express');
const router      = express.Router();
const authService = require('../auth');
const db          = process.env.DATABASE_URL ? require('../db') : null;
const log         = require('../logger');

// Simple in-memory IP rate limiter: max 10 auth attempts per IP per minute.
const _authAttempts = new Map();
setInterval(() => {
  const cutoff = Date.now() - 60_000;
  for (const [ip, r] of _authAttempts) {
    if (r.windowStart < cutoff) _authAttempts.delete(ip);
  }
}, 60_000).unref();

function checkAuthRateLimit(ip) {
  const now = Date.now();
  let r = _authAttempts.get(ip);
  if (!r || now - r.windowStart > 60_000) {
    r = { count: 0, windowStart: now };
    _authAttempts.set(ip, r);
  }
  r.count++;
  return r.count <= 10;
}

// ── Helper: validate email format ────────────────────────────────────────────
function isValidEmail(e) {
  return typeof e === 'string' && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(e);
}

// ── Cookie helpers ───────────────────────────────────────────────────────────
const IS_PROD = process.env.NODE_ENV === 'production';
const ACCESS_COOKIE_TTL  = 15 * 60;              // 15 min in seconds
const REFRESH_COOKIE_TTL = 30 * 24 * 60 * 60;   // 30 days in seconds

function setAuthCookies(res, accessToken, refreshToken) {
  const base = { httpOnly: true, secure: IS_PROD, sameSite: 'strict', path: '/' };
  res.cookie('cd_access',  accessToken,  { ...base, maxAge: ACCESS_COOKIE_TTL  * 1000 });
  res.cookie('cd_refresh', refreshToken, { ...base, maxAge: REFRESH_COOKIE_TTL * 1000 });
}

function clearAuthCookies(res) {
  const base = { httpOnly: true, secure: IS_PROD, sameSite: 'strict', path: '/' };
  res.clearCookie('cd_access',  base);
  res.clearCookie('cd_refresh', base);
}

// ── Helper: issue tokens, set HttpOnly cookies, and return response body ──────
async function issueTokens(player, res) {
  if (db) {
    await db.query(
      'UPDATE refresh_tokens SET revoked_at = NOW() WHERE player_id = $1 AND revoked_at IS NULL',
      [player.id]
    );
  }
  const accessToken  = authService.signAccessToken(player);
  const refreshToken = await authService.createRefreshToken(player.id);
  if (res) setAuthCookies(res, accessToken, refreshToken);
  return {
    accessToken,
    refreshToken,
    player: {
      id:          player.id,
      displayName: player.display_name,
      region:      player.region,
    },
  };
}

// POST /auth/google
// Body: { idToken: string }
// Returns: { accessToken, refreshToken, player }
router.post('/google', async (req, res) => {
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many authentication attempts. Try again later.' });
  }
  try {
    const { idToken } = req.body;
    if (!idToken) return res.status(400).json({ error: 'idToken required' });

    const googleUser = await authService.verifyGoogleToken(idToken);
    const player     = await authService.findOrCreatePlayer(googleUser.googleId, googleUser.name);

    if (player.status === 'suspended') {
      return res.status(403).json({ error: 'Account suspended' });
    }

    res.json(await issueTokens(player, res));
  } catch (err) {
    const msg = String(err?.message || '');
    let userMessage = 'Google authentication failed';
    if (/wrong recipient|audience/i.test(msg)) {
      userMessage = 'Google client configuration mismatch (audience/client ID).';
    } else if (/expired|too late|used too early|not yet valid/i.test(msg)) {
      userMessage = 'Google token timing validation failed. Try signing in again.';
    }
    log.error('[auth] google error', { err: msg });
    if (process.env.NODE_ENV !== 'production') {
      let decoded = null;
      try {
        const parts = String(req.body?.idToken || '').split('.');
        if (parts.length >= 2) {
          decoded = JSON.parse(Buffer.from(parts[1], 'base64url').toString('utf8'));
        }
      } catch { /* best effort */ }
      return res.status(401).json({
        error: userMessage,
        debug: {
          verifyError: msg,
          tokenAud: decoded?.aud || null,
          tokenIss: decoded?.iss || null,
        },
      });
    }
    res.status(401).json({ error: userMessage });
  }
});

// POST /auth/register
// Body: { email, displayName, password }
// Returns: { accessToken, refreshToken, player }
router.post('/register', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many authentication attempts. Try again later.' });
  }
  try {
    const { email, displayName, password } = req.body;
    if (!isValidEmail(email))             return res.status(400).json({ error: 'Invalid email address' });
    if (!displayName || !displayName.trim()) return res.status(400).json({ error: 'Display name required' });
    if (!password || password.length < 8)   return res.status(400).json({ error: 'Password must be at least 8 characters' });

    const player = await authService.registerWithPassword(email.trim(), displayName.trim(), password);

    // Send verification email (non-fatal if mailer fails)
    try {
      const { raw } = await authService.createEmailVerificationToken(player.id);
      const mailer  = require('../services/mailer');
      const baseUrl = process.env.APP_URL || `${req.protocol}://${req.get('host')}`;
      await mailer.sendEmailVerification(email.trim().toLowerCase(), `${baseUrl}/?verify=${raw}`);
    } catch (mailErr) {
      log.error('[auth] verification email error', { err: mailErr.message });
    }

    res.status(201).json({ requiresVerification: true, email: email.trim().toLowerCase() });
  } catch (err) {
    const msg = err.message === 'Email already registered' ? err.message : 'Registration failed';
    res.status(err.message === 'Email already registered' ? 409 : 400).json({ error: msg });
  }
});

// POST /auth/login
// Body: { email, password }
// Returns: { accessToken, refreshToken, player }  OR  { requiresMfa: true, mfaToken }
router.post('/login', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many authentication attempts. Try again later.' });
  }
  try {
    const { email, password } = req.body;
    if (!isValidEmail(email)) return res.status(400).json({ error: 'Invalid email address' });
    if (!password)            return res.status(400).json({ error: 'Password required' });

    const player = await authService.loginWithPassword(email.trim(), password);

    // Check email verification and 2FA together
    const row = await db.query(
      'SELECT totp_enabled, email_verified FROM players WHERE id = $1',
      [player.id]
    );
    if (row.rows[0] && !row.rows[0].email_verified) {
      return res.status(403).json({ error: 'email_not_verified', email: email.trim().toLowerCase() });
    }
    if (row.rows[0]?.totp_enabled) {
      const mfaToken = authService.signMfaToken(player.id);
      return res.json({ requiresMfa: true, mfaToken });
    }

    res.json(await issueTokens(player, res));
  } catch (err) {
    const known = ['Invalid email or password', 'Account suspended'];
    const msg   = known.includes(err.message) ? err.message : 'Login failed';
    res.status(401).json({ error: msg });
  }
});

// POST /auth/login/mfa
// Body: { mfaToken, code }
// Returns: { accessToken, refreshToken, player }
router.post('/login/mfa', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many authentication attempts. Try again later.' });
  }
  try {
    const { mfaToken, code } = req.body;
    if (!mfaToken || !code) return res.status(400).json({ error: 'mfaToken and code required' });

    const payload = authService.verifyMfaToken(mfaToken);
    const row = await db.query(
      'SELECT id, display_name, region, status, totp_secret, totp_enabled FROM players WHERE id = $1',
      [payload.sub]
    );
    if (!row.rows.length) return res.status(401).json({ error: 'Invalid MFA token' });
    const player = row.rows[0];
    if (player.status === 'suspended') return res.status(403).json({ error: 'Account suspended' });
    if (!player.totp_enabled || !player.totp_secret) return res.status(400).json({ error: 'MFA not configured' });

    const { authenticator } = require('otplib');
    authenticator.options = { window: 1 };
    const valid = authenticator.verify({ token: code, secret: player.totp_secret });
    if (!valid) return res.status(401).json({ error: 'Invalid verification code' });

    res.json(await issueTokens(player, res));
  } catch (err) {
    res.status(401).json({ error: 'MFA verification failed' });
  }
});

// POST /auth/2fa/setup  (authenticated)
// Returns: { secret, qrCodeDataUrl }
router.post('/2fa/setup', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  try {
    const authHeader = req.headers.authorization || '';
    const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : (req.cookies?.cd_access || '');
    if (!token) return res.status(401).json({ error: 'Unauthorized' });
    const payload = authService.verifyAccessToken(token);

    const { authenticator } = require('otplib');
    const QRCode = require('qrcode');

    const secret = authenticator.generateSecret();
    const playerRow = await db.query('SELECT display_name FROM players WHERE id = $1', [payload.sub]);
    if (!playerRow.rows.length) return res.status(404).json({ error: 'Player not found' });
    const displayName = playerRow.rows[0].display_name;

    // Store secret (not yet enabled — user must confirm with a valid code)
    await db.query('UPDATE players SET totp_secret = $1, totp_enabled = false WHERE id = $2', [secret, payload.sub]);

    const otpauth = authenticator.keyuri(displayName, 'CastleDefender', secret);
    const qrCodeDataUrl = await QRCode.toDataURL(otpauth);

    res.json({ secret, qrCodeDataUrl });
  } catch (err) {
    res.status(401).json({ error: 'Unauthorized' });
  }
});

// POST /auth/2fa/enable  (authenticated)
// Body: { code }
router.post('/2fa/enable', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  try {
    const authHeader = req.headers.authorization || '';
    const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : (req.cookies?.cd_access || '');
    if (!token) return res.status(401).json({ error: 'Unauthorized' });
    const payload = authService.verifyAccessToken(token);

    const { code } = req.body;
    if (!code) return res.status(400).json({ error: 'code required' });

    const row = await db.query(
      'SELECT totp_secret FROM players WHERE id = $1',
      [payload.sub]
    );
    if (!row.rows.length || !row.rows[0].totp_secret) {
      return res.status(400).json({ error: 'Run /auth/2fa/setup first' });
    }

    const { authenticator } = require('otplib');
    authenticator.options = { window: 1 };
    const valid = authenticator.verify({ token: code, secret: row.rows[0].totp_secret });
    if (!valid) return res.status(401).json({ error: 'Invalid verification code' });

    await db.query('UPDATE players SET totp_enabled = true WHERE id = $1', [payload.sub]);
    res.json({ ok: true });
  } catch (err) {
    res.status(401).json({ error: 'Unauthorized' });
  }
});

// POST /auth/2fa/disable  (authenticated)
// Body: { code }
router.post('/2fa/disable', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  try {
    const authHeader = req.headers.authorization || '';
    const token = authHeader.startsWith('Bearer ') ? authHeader.slice(7) : (req.cookies?.cd_access || '');
    if (!token) return res.status(401).json({ error: 'Unauthorized' });
    const payload = authService.verifyAccessToken(token);

    const { code } = req.body;
    if (!code) return res.status(400).json({ error: 'code required' });

    const row = await db.query(
      'SELECT totp_secret, totp_enabled FROM players WHERE id = $1',
      [payload.sub]
    );
    if (!row.rows.length || !row.rows[0].totp_enabled) {
      return res.status(400).json({ error: '2FA is not enabled' });
    }

    const { authenticator } = require('otplib');
    authenticator.options = { window: 1 };
    const valid = authenticator.verify({ token: code, secret: row.rows[0].totp_secret });
    if (!valid) return res.status(401).json({ error: 'Invalid verification code' });

    await db.query(
      'UPDATE players SET totp_enabled = false, totp_secret = NULL WHERE id = $1',
      [payload.sub]
    );
    res.json({ ok: true });
  } catch (err) {
    res.status(401).json({ error: 'Unauthorized' });
  }
});

// POST /auth/forgot-password
// Body: { email }
// Always returns 200 (don't reveal whether email exists)
router.post('/forgot-password', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many attempts. Try again later.' });
  }
  const { email } = req.body;
  if (!email) return res.status(400).json({ error: 'Email required' });
  try {
    const result = await authService.createPasswordResetToken(email.trim());
    if (result) {
      const mailer  = require('../services/mailer');
      const baseUrl = process.env.APP_URL || `${req.protocol}://${req.get('host')}`;
      const resetUrl = `${baseUrl}/?reset=${result.raw}`;
      await mailer.sendPasswordReset(result.email, resetUrl);
    }
  } catch (err) {
    log.error('[auth] forgot-password error', { err: err.message });
    // Still return 200 — don't reveal internal errors
  }
  res.json({ ok: true });
});

// POST /auth/reset-password
// Body: { token, password }
router.post('/reset-password', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many attempts. Try again later.' });
  }
  try {
    const { token, password } = req.body;
    if (!token)                    return res.status(400).json({ error: 'Token required' });
    if (!password || password.length < 8) return res.status(400).json({ error: 'Password must be at least 8 characters' });
    await authService.consumePasswordResetToken(token, password);
    res.json({ ok: true });
  } catch (err) {
    const known = ['Invalid or expired reset link', 'Reset link already used', 'Account suspended'];
    const msg   = known.includes(err.message) ? err.message : 'Password reset failed';
    res.status(400).json({ error: msg });
  }
});

// POST /auth/verify-email
// Body: { token }
// Returns: { accessToken, refreshToken, player }
router.post('/verify-email', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  try {
    const { token } = req.body;
    if (!token) return res.status(400).json({ error: 'token required' });
    const player = await authService.consumeEmailVerificationToken(token);
    if (player.status === 'suspended') return res.status(403).json({ error: 'Account suspended' });
    res.json(await issueTokens(player, res));
  } catch (err) {
    const msg = err.message === 'Invalid or expired verification link' ? err.message : 'Verification failed';
    res.status(400).json({ error: msg });
  }
});

// POST /auth/resend-verification
// Body: { email }
// Always returns 200 (don't reveal account existence)
router.post('/resend-verification', async (req, res) => {
  if (!db) return res.status(503).json({ error: 'Database not configured' });
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many attempts. Try again later.' });
  }
  const { email } = req.body;
  if (!email) return res.status(400).json({ error: 'Email required' });
  try {
    const playerRow = await db.query(
      'SELECT id, email, email_verified FROM players WHERE lower(email) = lower($1)',
      [email.trim()]
    );
    if (playerRow.rows.length) {
      const player = playerRow.rows[0];
      if (player.email_verified) {
        return res.json({ ok: true, alreadyVerified: true });
      }
      const { raw } = await authService.createEmailVerificationToken(player.id);
      const mailer  = require('../services/mailer');
      const baseUrl = process.env.APP_URL || `${req.protocol}://${req.get('host')}`;
      await mailer.sendEmailVerification(player.email, `${baseUrl}/?verify=${raw}`);
    }
  } catch (err) {
    log.error('[auth] resend-verification error', { err: err.message });
  }
  res.json({ ok: true });
});

// POST /auth/refresh
// Reads refreshToken from HttpOnly cookie (preferred) or request body (legacy).
// Returns: { accessToken, refreshToken }
router.post('/refresh', async (req, res) => {
  // H-1: rate-limit refresh attempts by IP (shared counter with /google)
  const ip = req.ip || req.socket?.remoteAddress || 'unknown';
  if (!checkAuthRateLimit(ip)) {
    return res.status(429).json({ error: 'Too many authentication attempts. Try again later.' });
  }
  try {
    // Accept token from cookie first, fall back to body for backward compat
    const refreshToken = req.cookies?.cd_refresh || req.body?.refreshToken;
    if (!refreshToken) { clearAuthCookies(res); return res.status(400).json({ error: 'refreshToken required' }); }

    const result = await authService.rotateRefreshToken(refreshToken);
    setAuthCookies(res, result.accessToken, result.refreshToken);
    res.json({ accessToken: result.accessToken, refreshToken: result.refreshToken });
  } catch (err) {
    clearAuthCookies(res);
    // M-4: only forward known error messages; mask internal errors
    const msg = authService.KNOWN_AUTH_ERRORS.has(err.message) ? err.message : 'Token refresh failed';
    res.status(401).json({ error: msg });
  }
});

// POST /auth/logout
// Revokes the refresh token (from cookie or body) and clears auth cookies.
router.post('/logout', async (req, res) => {
  try {
    const refreshToken = req.cookies?.cd_refresh || req.body?.refreshToken;
    if (refreshToken) await authService.revokeRefreshToken(refreshToken);
  } catch { /* best-effort */ }
  clearAuthCookies(res);
  res.json({ ok: true });
});

module.exports = router;
