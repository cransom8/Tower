'use strict';

const express = require('express');
const router  = express.Router();
const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Admin auth: requires ADMIN_SECRET env var + X-Admin-Secret header match.
function requireAdmin(req, res, next) {
  const secret = process.env.ADMIN_SECRET;
  if (!secret) return res.status(403).json({ error: 'Admin not configured' });
  const provided = req.headers['x-admin-secret'] || '';
  if (provided !== secret) return res.status(403).json({ error: 'Forbidden' });
  next();
}

// POST /admin/seasons
// Body: { name: string }
// Creates a new active season. Fails if one already exists.
router.post('/seasons', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');

  const name = String(req.body.name || '').trim().slice(0, 80);
  if (!name) return res.status(400).json({ error: 'Season name required' });

  try {
    const existing = await db.query(
      `SELECT id FROM seasons WHERE is_active = true LIMIT 1`
    );
    if (existing.rows.length > 0) {
      return res.status(409).json({ error: 'An active season already exists. Close it first.' });
    }

    const result = await db.query(
      `INSERT INTO seasons (name, start_date, is_active)
       VALUES ($1, NOW(), true)
       RETURNING id, name, start_date, is_active`,
      [name]
    );
    console.log(`[admin] created season "${name}" (${result.rows[0].id})`);
    res.status(201).json({ season: result.rows[0] });
  } catch (err) {
    console.error('[admin] POST /seasons:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

// POST /admin/seasons/:id/close
// Closes a season: snapshots final ratings, soft-resets all ranked ratings.
router.post('/seasons/:id/close', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db      = require('../db');
  const season  = require('../services/season');

  if (!UUID_RE.test(req.params.id)) {
    return res.status(400).json({ error: 'Invalid season ID' });
  }

  try {
    const check = await db.query(
      `SELECT id, name, is_active FROM seasons WHERE id = $1`,
      [req.params.id]
    );
    if (!check.rows[0]) return res.status(404).json({ error: 'Season not found' });
    if (!check.rows[0].is_active) return res.status(409).json({ error: 'Season already closed' });

    const { snapshotCount, resetCount } = await season.closeSeason(db, req.params.id);
    res.json({ closed: true, seasonId: req.params.id, snapshotCount, resetCount });
  } catch (err) {
    console.error('[admin] POST /seasons/:id/close:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

// GET /admin/seasons — list all seasons (for admin inspection)
router.get('/seasons', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.json({ seasons: [] });
  const db = require('../db');
  try {
    const result = await db.query(
      `SELECT id, name, start_date, end_date, is_active FROM seasons ORDER BY start_date DESC`
    );
    res.json({ seasons: result.rows });
  } catch (err) {
    console.error('[admin] GET /seasons:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Match inspection ──────────────────────────────────────────────────────────

// GET /admin/matches/:id — full match record with per-player details
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
         FROM   match_players mp
         JOIN   players p ON p.id = mp.player_id
         WHERE  mp.match_id = $1
         ORDER  BY mp.lane_index`,
        [req.params.id]
      ),
    ]);
    if (!matchRes.rows[0]) return res.status(404).json({ error: 'Match not found' });
    res.json({ match: matchRes.rows[0], players: playersRes.rows });
  } catch (err) {
    console.error('[admin] GET /matches/:id:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Rating adjustment ─────────────────────────────────────────────────────────

// POST /admin/players/:id/adjust-rating
// Body: { mode: '2v2_ranked'|'2v2_casual', mu: number, sigma: number }
router.post('/players/:id/adjust-rating', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });

  const { mode, mu, sigma } = req.body;
  if (!['2v2_ranked', '2v2_casual'].includes(mode)) {
    return res.status(400).json({ error: 'mode must be 2v2_ranked or 2v2_casual' });
  }
  const mu_n    = Number(mu);
  const sigma_n = Number(sigma);
  if (!Number.isFinite(mu_n) || !Number.isFinite(sigma_n)) {
    return res.status(400).json({ error: 'mu and sigma must be finite numbers' });
  }
  if (sigma_n <= 0) return res.status(400).json({ error: 'sigma must be positive' });

  const new_rating = mu_n - 3 * sigma_n;
  try {
    const result = await db.query(
      `INSERT INTO ratings (player_id, mode, mu, sigma, rating, updated_at)
       VALUES ($1, $2, $3, $4, $5, NOW())
       ON CONFLICT (player_id, mode) DO UPDATE SET
         mu         = EXCLUDED.mu,
         sigma      = EXCLUDED.sigma,
         rating     = EXCLUDED.rating,
         updated_at = NOW()
       RETURNING player_id, mode, mu, sigma, rating, wins, losses`,
      [req.params.id, mode, mu_n, sigma_n, new_rating]
    );
    console.log(`[admin] adjusted rating for player ${req.params.id} mode=${mode} mu=${mu_n} sigma=${sigma_n}`);
    res.json({ rating: result.rows[0] });
  } catch (err) {
    console.error('[admin] POST /players/:id/adjust-rating:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

// ── Player bans ───────────────────────────────────────────────────────────────

// POST /admin/players/:id/ban
// Body: { reason?: string }
router.post('/players/:id/ban', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });

  const reason = String(req.body?.reason || '').trim().slice(0, 200) || 'Banned by admin';
  try {
    const result = await db.query(
      `UPDATE players
       SET    status = 'suspended', ban_reason = $1, banned_at = NOW(), updated_at = NOW()
       WHERE  id = $2
       RETURNING id, display_name, status, ban_reason, banned_at`,
      [reason, req.params.id]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });
    console.log(`[admin] banned player ${req.params.id}: ${reason}`);
    res.json({ player: result.rows[0] });
  } catch (err) {
    console.error('[admin] POST /players/:id/ban:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

// DELETE /admin/players/:id/ban — lift a ban
router.delete('/players/:id/ban', requireAdmin, async (req, res) => {
  if (!process.env.DATABASE_URL) return res.status(503).json({ error: 'No database' });
  const db = require('../db');
  if (!UUID_RE.test(req.params.id)) return res.status(400).json({ error: 'Invalid player ID' });

  try {
    const result = await db.query(
      `UPDATE players
       SET    status = 'active', ban_reason = NULL, banned_at = NULL, updated_at = NOW()
       WHERE  id = $1
       RETURNING id, display_name, status`,
      [req.params.id]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });
    console.log(`[admin] unbanned player ${req.params.id}`);
    res.json({ player: result.rows[0] });
  } catch (err) {
    console.error('[admin] DELETE /players/:id/ban:', err.message);
    res.status(500).json({ error: 'Server error' });
  }
});

module.exports = router;
