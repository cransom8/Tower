"use strict";

function createMatchPersistence({ db, log }) {
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

  async function logMatchEnd(matchIdPromise, winnerLane, playerSnapshots) {
    if (!db) return;
    const matchId = await matchIdPromise;
    if (!matchId) return;
    const client = await db.getClient();
    try {
      await client.query("BEGIN");
      await client.query(
        "UPDATE matches SET status='completed', ended_at=NOW(), winner_lane=$1 WHERE id=$2",
        [winnerLane ?? null, matchId]
      );
      if (playerSnapshots.length > 0) {
        const placeholders = playerSnapshots
          .map((_, i) => `($1, ${i * 3 + 2}, ${i * 3 + 3}, ${i * 3 + 4})`)
          .join(", ");
        const params = [matchId];
        for (const { playerId, laneIndex, result } of playerSnapshots) {
          params.push(playerId, laneIndex, result);
        }
        await client.query(
          `INSERT INTO match_players (match_id, player_id, lane_index, result) VALUES ${placeholders}`,
          params
        );
      }
      await client.query("COMMIT");
    } catch (err) {
      await client.query("ROLLBACK").catch(() => {});
      log.error("[match] close failed:", { err: err.message });
    } finally {
      client.release();
    }
  }

  return { logMatchEnd, logMatchStart };
}

module.exports = { createMatchPersistence };
