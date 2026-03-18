"use strict";

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

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
      await client.query(
        "UPDATE matches SET status='completed', ended_at=NOW(), winner_lane=$1 WHERE id=$2",
        [winnerLane ?? null, matchId]
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
