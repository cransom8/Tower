'use strict';
const log = require('../logger');

const express = require('express');
const router  = express.Router();

const VALID_MODES   = new Set(['2v2_ranked', '2v2_casual', '1v1_ranked', '1v1_casual', 'ffa_ranked', 'ffa_casual']);
const VALID_REGIONS = new Set(['global', 'na', 'eu', 'asia']);
const DEFAULT_PAGE_SIZE = 50;
const MAX_PAGE_SIZE = 50;

function parsePositiveInt(value, fallback) {
  const parsed = Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : fallback;
}

// GET /leaderboard?mode=2v2_ranked&region=global&page=1&limit=20
// Returns paginated top players sorted by rating (conservative Glicko-2 estimate).
router.get('/', async (req, res) => {
  const db = req.app?.locals?.db || (process.env.DATABASE_URL ? require('../db') : null);
  if (!db)
    return res.json({ season: null, entries: [], page: 1, total: 0, pageSize: DEFAULT_PAGE_SIZE });

  const mode   = VALID_MODES.has(req.query.mode)   ? req.query.mode   : '2v2_ranked';
  const region = VALID_REGIONS.has(req.query.region) ? req.query.region : 'global';
  const pageSize = Math.min(parsePositiveInt(req.query.limit, DEFAULT_PAGE_SIZE), MAX_PAGE_SIZE);
  const page   = Math.min(Math.max(1, parsePositiveInt(req.query.page, 1)), 100);
  const offset = (page - 1) * pageSize;
  const leaderboardOrder = 'r.rating DESC, r.wins DESC, p.display_name ASC, p.id ASC';

  try {
    // Active season
    const seasonRes = await db.query(
      `SELECT id, name, start_date
       FROM seasons
       WHERE is_active = true
       ORDER BY start_date DESC
       LIMIT 1`
    );
    const season = seasonRes.rows[0] || null;

    // Build optional region WHERE clause
    const params = [mode, pageSize, offset];
    let regionClause = '';
    if (region && region !== 'global') {
      params.push(region);
      regionClause = `AND p.region = $${params.length}`;
    }

    const result = await db.query(
      `SELECT
         p.id, p.display_name, p.region,
         r.rating, r.wins, r.losses,
         (ROW_NUMBER() OVER (ORDER BY ${leaderboardOrder}))::int AS rank
       FROM ratings r
       JOIN players p ON p.id = r.player_id
       WHERE r.mode = $1
         AND p.status = 'active'
         ${regionClause}
       ORDER BY ${leaderboardOrder}
       LIMIT $2 OFFSET $3`,
      params
    );

    // Total count
    const countParams = [mode];
    let countRegion = '';
    if (region && region !== 'global') {
      countParams.push(region);
      countRegion = `AND p.region = $${countParams.length}`;
    }
    const countRes = await db.query(
      `SELECT COUNT(*)::int AS total
       FROM ratings r
       JOIN players p ON p.id = r.player_id
       WHERE r.mode = $1 AND p.status = 'active' ${countRegion}`,
      countParams
    );

    res.json({
      season,
      entries: result.rows,
      page,
      total: Number(countRes.rows[0]?.total) || 0,
      pageSize,
    });
  } catch (err) {
    log.error('[leaderboard] error:', { err: err.message });
    res.status(500).json({ error: 'Server error' });
  }
});

module.exports = router;
