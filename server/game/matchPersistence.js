"use strict";

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function createMatchPersistence({ db, log, ratingService, seasonService }) {
  async function logMatchStart(roomId, mode) {
    if (!db) return null;
    try {
      const result = await db.query(
        "INSERT INTO matches (room_id, mode) VALUES ($1, $2) RETURNING id",
        [roomId, mode]
      );
      return result.rows[0].id;
    } catch (err) {
      log.error("[match] insert failed:", { err: err.message });
      return null;
    }
  }

  async function logMatchEnd(matchIdPromise, winnerLane, playerSnapshots, options = {}) {
    if (!db) {
      return { matchId: null, ratingUpdates: [], finalizationState: "disabled" };
    }
    const matchId = await matchIdPromise;
    if (!matchId) {
      return { matchId: null, ratingUpdates: [], finalizationState: "missing_match_id" };
    }

    const winnerLaneValue = Number.isInteger(winnerLane) && winnerLane >= 0 ? winnerLane : null;
    const combatLogPayload = Array.isArray(options.combatLog) ? JSON.stringify(options.combatLog) : null;
    const waveStatsPayload = Array.isArray(options.waveStats) ? JSON.stringify(options.waveStats) : null;
    const validSnapshots = Array.isArray(playerSnapshots)
      ? playerSnapshots.filter((snapshot) => UUID_RE.test(String(snapshot?.playerId || "")))
      : [];
    const skippedSnapshots = Array.isArray(playerSnapshots)
      ? playerSnapshots.filter((snapshot) => !UUID_RE.test(String(snapshot?.playerId || "")))
      : [];
    if (skippedSnapshots.length > 0) {
      log.warn("[match] skipping invalid player snapshots during close", {
        matchId,
        skipped: skippedSnapshots.map((snapshot) => ({
          playerId: snapshot?.playerId ?? null,
          laneIndex: snapshot?.laneIndex ?? null,
          result: snapshot?.result ?? null,
        })),
      });
    }
    const client = await db.getClient();
    try {
      await client.query("BEGIN");
      const lockResult = await client.query(
        "SELECT finalization_state FROM matches WHERE id = $1 FOR UPDATE",
        [matchId]
      );
      if (!lockResult.rows[0]) {
        await client.query("ROLLBACK");
        return { matchId, ratingUpdates: [], finalizationState: "missing_match" };
      }

      const currentFinalizationState = lockResult.rows[0].finalization_state;
      if (currentFinalizationState === "completed" || currentFinalizationState === "abandoned") {
        await client.query("ROLLBACK");
        return { matchId, ratingUpdates: [], finalizationState: currentFinalizationState };
      }

      await client.query(
        "UPDATE matches SET finalization_state = 'processing' WHERE id = $1",
        [matchId]
      );
      await client.query(
        `UPDATE matches
            SET status = 'completed',
                ended_at = NOW(),
                winner_lane = $1,
                combat_log = COALESCE($2::jsonb, combat_log),
                wave_stats = COALESCE($3::jsonb, wave_stats)
          WHERE id = $4`,
        [winnerLaneValue, combatLogPayload, waveStatsPayload, matchId]
      );
      if (validSnapshots.length > 0) {
        const placeholders = validSnapshots
          .map((_, i) => `($1, ${i * 3 + 2}, ${i * 3 + 3}, ${i * 3 + 4})`)
          .join(", ");
        const params = [matchId];
        for (const { playerId, laneIndex, result } of validSnapshots) {
          params.push(playerId, laneIndex, result);
        }
        await client.query(
          `INSERT INTO match_players (match_id, player_id, lane_index, result)
           VALUES ${placeholders}
           ON CONFLICT (match_id, player_id) DO UPDATE SET
             lane_index = EXCLUDED.lane_index,
             result = EXCLUDED.result`,
          params
        );
      }

      let ratingUpdates = [];
      if (ratingService && options.mode && options.mode.endsWith("_ranked")) {
        ratingUpdates = await ratingService.updateRatings(
          db,
          matchId,
          options.mode,
          validSnapshots,
          options.partyASize || 1,
          { client }
        );

        if (seasonService && ratingUpdates.length > 0) {
          const season = await seasonService.getActiveSeason(db, { client });
          if (season) {
            for (const update of ratingUpdates) {
              await seasonService.updatePeakRating(
                db,
                season.id,
                update.playerId,
                options.mode,
                update.newRating,
                { client }
              );
            }
          }
        }
      }

      await client.query(
        "UPDATE matches SET finalization_state = 'completed', finalized_at = NOW() WHERE id = $1",
        [matchId]
      );
      await client.query("COMMIT");
      return { matchId, ratingUpdates, finalizationState: "completed" };
    } catch (err) {
      await client.query("ROLLBACK").catch(() => {});
      log.error("[match] close failed:", { matchId, err: err.message });
      return { matchId, ratingUpdates: [], finalizationState: "failed" };
    } finally {
      client.release();
    }
  }

  return { logMatchEnd, logMatchStart };
}

module.exports = { createMatchPersistence };
