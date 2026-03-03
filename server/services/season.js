'use strict';

// ── Season Service ─────────────────────────────────────────────────────────────
//
// Manages season lifecycle and per-player peak rating tracking.
// All public functions are safe to call without a db (return null/void).

/**
 * Get the currently active season, or null.
 */
async function getActiveSeason(db) {
  if (!db) return null;
  try {
    const res = await db.query(
      `SELECT id, name, start_date FROM seasons WHERE is_active = true LIMIT 1`
    );
    return res.rows[0] || null;
  } catch (err) {
    console.error('[season] getActiveSeason:', err.message);
    return null;
  }
}

/**
 * Upsert season_ratings: bump peak_rating if newRating exceeds current stored peak.
 * Fire-and-forget — never throws.
 */
async function updatePeakRating(db, seasonId, playerId, mode, newRating) {
  if (!db || !seasonId || !playerId) return;
  try {
    await db.query(
      `INSERT INTO season_ratings (season_id, player_id, mode, peak_rating)
       VALUES ($1, $2, $3, $4)
       ON CONFLICT (season_id, player_id, mode) DO UPDATE
         SET peak_rating = GREATEST(season_ratings.peak_rating, EXCLUDED.peak_rating)`,
      [seasonId, playerId, mode, newRating]
    );
  } catch (err) {
    console.error('[season] updatePeakRating:', err.message);
  }
}

/**
 * Close a season:
 *   1. Snapshot each player's current rating as final_rating into season_ratings.
 *   2. Soft-reset: inflate sigma +50 (cap 350) for all players in '2v2_ranked',
 *      recompute rating = mu - 3 * new_sigma.
 *   3. Mark season closed (is_active=false, end_date=NOW()).
 *
 * Returns { snapshotCount, resetCount } or throws on DB error.
 */
async function closeSeason(db, seasonId) {
  const client = await db.getClient();
  try {
    await client.query('BEGIN');

    // 1. Snapshot final ratings
    const snap = await client.query(
      `INSERT INTO season_ratings (season_id, player_id, mode, peak_rating, final_rating)
       SELECT $1, r.player_id, r.mode, r.rating, r.rating
       FROM ratings r
       WHERE r.mode = '2v2_ranked'
       ON CONFLICT (season_id, player_id, mode) DO UPDATE SET
         final_rating = EXCLUDED.final_rating,
         peak_rating  = GREATEST(season_ratings.peak_rating, EXCLUDED.peak_rating)`,
      [seasonId]
    );

    // 2. Soft-reset: inflate sigma +50 (capped at 350), recompute conservative rating
    const reset = await client.query(
      `UPDATE ratings SET
         sigma      = LEAST(sigma + 50, 350),
         rating     = mu - 3 * LEAST(sigma + 50, 350),
         updated_at = NOW()
       WHERE mode = '2v2_ranked'`
    );

    // 3. Close the season
    await client.query(
      `UPDATE seasons SET is_active = false, end_date = NOW() WHERE id = $1`,
      [seasonId]
    );

    await client.query('COMMIT');
    console.log(`[season] closed season ${seasonId}: ${snap.rowCount} snapshots, ${reset.rowCount} resets`);
    return { snapshotCount: snap.rowCount, resetCount: reset.rowCount };
  } catch (err) {
    await client.query('ROLLBACK');
    throw err;
  } finally {
    client.release();
  }
}

module.exports = { getActiveSeason, updatePeakRating, closeSeason };
