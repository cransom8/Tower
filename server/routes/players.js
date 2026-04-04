'use strict';
const log = require('../logger');

const express      = require('express');
const router       = express.Router();
const db           = require('../db');
const { requireAuth } = require('../middleware/auth');
const {
  normalizePlayerPreferences,
} = require('../lib/playerPreferences');

const VALID_REGIONS = new Set(['global', 'na', 'eu', 'asia']);
const RANKED_QUEUE_CASUAL_REQUIREMENT = 5;

// GET /players/me — authenticated player's own profile + ranked rating
router.get('/me', requireAuth, async (req, res) => {
  try {
    const result = await db.query(
      `SELECT p.id, p.display_name, p.region, p.status, p.created_at,
              p.preferences_json,
              r.mu, r.sigma, r.rating, r.wins, r.losses, r.updated_at AS rating_updated_at,
              COALESCE(casual.wins + casual.losses, 0) AS ranked_unlock_casual_matches_completed
       FROM players p
       LEFT JOIN ratings r ON r.player_id = p.id AND r.mode = '2v2_ranked'
       LEFT JOIN ratings casual ON casual.player_id = p.id AND casual.mode = 'ffa_casual'
       WHERE p.id = $1`,
      [req.player.sub]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });
    const {
      preferences_json,
      ranked_unlock_casual_matches_completed,
      ...player
    } = result.rows[0];
    const completedCasualMatches = Number(ranked_unlock_casual_matches_completed) || 0;
    res.json({
      ...player,
      preferences: normalizePlayerPreferences(preferences_json),
      queue_progression: {
        ranked_casual_matches_completed: completedCasualMatches,
        ranked_casual_matches_required: RANKED_QUEUE_CASUAL_REQUIREMENT,
        ranked_queue_unlocked: completedCasualMatches >= RANKED_QUEUE_CASUAL_REQUIREMENT,
      },
    });
  } catch (err) {
    log.error('[players] GET /me:', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.get('/me/preferences', requireAuth, async (req, res) => {
  try {
    const result = await db.query(
      'SELECT preferences_json FROM players WHERE id = $1',
      [req.player.sub]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });

    res.json({
      preferences: normalizePlayerPreferences(result.rows[0].preferences_json),
    });
  } catch (err) {
    log.error('[players] GET /me/preferences:', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// GET /players/:id — public profile
router.get('/:id', async (req, res) => {
  if (!UUID_RE.test(req.params.id)) {
    return res.status(400).json({ error: 'Invalid player ID' });
  }
  try {
    const result = await db.query(
      `SELECT p.id, p.display_name, p.region, p.created_at,
              r.rating, r.wins, r.losses
       FROM players p
       LEFT JOIN ratings r ON r.player_id = p.id AND r.mode = '2v2_ranked'
       WHERE p.id = $1 AND p.status = 'active'`,
      [req.params.id]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });
    res.json(result.rows[0]);
  } catch (err) {
    log.error('[players] GET /:id:', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

// PATCH /players/me — update display_name or region
router.patch('/me', requireAuth, async (req, res) => {
  try {
    const { displayName, region } = req.body;
    const sets   = [];
    const params = [];

    if (displayName !== undefined) {
      const cleaned = String(displayName).replace(/[^a-zA-Z0-9_ ]/g, '').trim().slice(0, 20);
      if (!cleaned) return res.status(400).json({ error: 'Invalid display name' });

      const taken = await db.query(
        'SELECT id FROM players WHERE lower(display_name) = lower($1) AND id != $2',
        [cleaned, req.player.sub]
      );
      if (taken.rows.length > 0) return res.status(409).json({ error: 'Display name already taken' });

      sets.push(`display_name = $${params.length + 1}`);
      params.push(cleaned);
    }

    if (region !== undefined) {
      if (!VALID_REGIONS.has(region)) return res.status(400).json({ error: 'Invalid region' });
      sets.push(`region = $${params.length + 1}`);
      params.push(region);
    }

    if (sets.length === 0) return res.status(400).json({ error: 'Nothing to update' });

    sets.push('updated_at = NOW()');
    params.push(req.player.sub);

    const result = await db.query(
      `UPDATE players SET ${sets.join(', ')} WHERE id = $${params.length}
       RETURNING id, display_name, region`,
      params
    );
    res.json(result.rows[0]);
  } catch (err) {
    log.error('[players] PATCH /me:', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

router.put('/me/preferences', requireAuth, async (req, res) => {
  try {
    const incoming = req.body?.preferences ?? req.body;
    const normalizedPreferences = normalizePlayerPreferences(incoming);

    const result = await db.query(
      `UPDATE players
          SET preferences_json = $1::jsonb,
              updated_at = NOW()
        WHERE id = $2
      RETURNING preferences_json`,
      [JSON.stringify(normalizedPreferences), req.player.sub]
    );
    if (!result.rows[0]) return res.status(404).json({ error: 'Player not found' });

    res.json({
      preferences: normalizePlayerPreferences(result.rows[0].preferences_json),
    });
  } catch (err) {
    log.error('[players] PUT /me/preferences:', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

module.exports = router;
