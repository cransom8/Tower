'use strict';

const { verifyAccessToken } = require('../auth');

function extractToken(req) {
  const header = req.headers.authorization || '';
  if (header.startsWith('Bearer ')) return header.slice(7);
  // Fall back to HttpOnly cookie
  return req.cookies?.cd_access || null;
}

function requireAuth(req, res, next) {
  const token = extractToken(req);
  if (!token) return res.status(401).json({ error: 'Unauthorized' });
  try {
    req.player = verifyAccessToken(token);
    next();
  } catch {
    return res.status(401).json({ error: 'Invalid or expired token' });
  }
}

function optionalAuth(req, res, next) {
  const token = extractToken(req);
  if (token) {
    try { req.player = verifyAccessToken(token); } catch { /* ignore */ }
  }
  next();
}

module.exports = { requireAuth, optionalAuth };
