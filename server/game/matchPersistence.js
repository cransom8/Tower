"use strict";

const { performance } = require("node:perf_hooks");

const { attachMatchId } = require("./multilane/balanceTelemetry");

const UUID_RE = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const SLOW_QUERY_THRESHOLD_MS = 300;
const MAX_QUERY_SUMMARY_CHARS = 180;

function summarizeQueryText(text) {
  return String(text || "")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, MAX_QUERY_SUMMARY_CHARS);
}

function roundDurationMs(value) {
  return Number.isFinite(value) ? Number(value.toFixed(1)) : null;
}

function createMatchPersistence({ db, log, ratingService, seasonService }) {
  async function timedClientQuery(client, text, params, label) {
    const start = performance.now();
    const result = params === undefined
      ? await client.query(text)
      : await client.query(text, params);
    const ms = performance.now() - start;
    if (ms > SLOW_QUERY_THRESHOLD_MS) {
      log.info("[db] slow query", {
        ms: roundDurationMs(ms),
        label: label || null,
        query: summarizeQueryText(text),
        rowCount: typeof result.rowCount === "number" ? result.rowCount : null,
      });
    }
    return result;
  }

  async function logMatchStart(roomId, mode) {
    if (!db) return null;
    try {
      const result = await db.query(
        "INSERT INTO matches (room_id, mode) VALUES ($1, $2) RETURNING id",
        [roomId, mode],
        { label: "matches:insert_start" }
      );
      return result.rows[0].id;
    } catch (err) {
      log.error("[match] insert failed:", { err: err.message });
      return null;
    }
  }

  async function persistMatchFinalization(matchIdPromise, winnerLane, playerSnapshots, options = {}) {
    if (!db) {
      return { matchId: null, ratingUpdates: [], finalizationState: "disabled" };
    }
    const matchId = await matchIdPromise;
    if (!matchId) {
      return { matchId: null, ratingUpdates: [], finalizationState: "missing_match_id" };
    }

    const status = options.status === "abandoned" ? "abandoned" : "completed";
    const targetFinalizationState = status === "abandoned" ? "abandoned" : "completed";
    const winnerLaneValue = Number.isInteger(winnerLane) && winnerLane >= 0 ? winnerLane : null;
    const waveStatsWithMatchId = Array.isArray(options.waveStats)
      ? attachMatchId(options.waveStats, matchId)
      : null;
    const balanceSummaryWithMatchId = options.balanceSummary && typeof options.balanceSummary === "object"
      ? attachMatchId(options.balanceSummary, matchId)
      : null;
    const combatLogPayload = Array.isArray(options.combatLog) ? JSON.stringify(options.combatLog) : null;
    const waveStatsPayload = waveStatsWithMatchId ? JSON.stringify(waveStatsWithMatchId) : null;
    const balanceSummaryPayload = balanceSummaryWithMatchId ? JSON.stringify(balanceSummaryWithMatchId) : null;
    const balanceFlagsPayload = Array.isArray(options.balanceFlags) ? JSON.stringify(options.balanceFlags) : null;
    const actionTimelinePayload = options.actionTimeline && typeof options.actionTimeline === "object"
      ? JSON.stringify({ ...options.actionTimeline, matchId })
      : null;
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
      await timedClientQuery(client, "BEGIN", undefined, "matches:close_begin");
      const lockResult = await timedClientQuery(
        client,
        "SELECT finalization_state FROM matches WHERE id = $1 FOR UPDATE",
        [matchId],
        "matches:close_lock"
      );
      if (!lockResult.rows[0]) {
        await timedClientQuery(client, "ROLLBACK", undefined, "matches:close_rollback_missing");
        return { matchId, ratingUpdates: [], finalizationState: "missing_match" };
      }

      const currentFinalizationState = lockResult.rows[0].finalization_state;
      if (currentFinalizationState === "completed" || currentFinalizationState === "abandoned") {
        await timedClientQuery(client, "ROLLBACK", undefined, "matches:close_rollback_already_finalized");
        return { matchId, ratingUpdates: [], finalizationState: currentFinalizationState };
      }

      await timedClientQuery(
        client,
        "UPDATE matches SET finalization_state = 'processing' WHERE id = $1",
        [matchId],
        "matches:close_mark_processing"
      );
      const updateMatchRow = async (includeActionTimeline) => {
        if (includeActionTimeline) {
          return timedClientQuery(
            client,
            `UPDATE matches
                SET status = '${status}',
                    ended_at = NOW(),
                    winner_lane = $1,
                    combat_log = COALESCE($2::jsonb, combat_log),
                    wave_stats = COALESCE($3::jsonb, wave_stats),
                    balance_summary = COALESCE($4::jsonb, balance_summary),
                    balance_flags = COALESCE($5::jsonb, balance_flags),
                    action_timeline = COALESCE($6::jsonb, action_timeline)
              WHERE id = $7`,
            [winnerLaneValue, combatLogPayload, waveStatsPayload, balanceSummaryPayload, balanceFlagsPayload, actionTimelinePayload, matchId],
            "matches:close_update_match"
          );
        }
        return timedClientQuery(
          client,
          `UPDATE matches
              SET status = '${status}',
                  ended_at = NOW(),
                  winner_lane = $1,
                  combat_log = COALESCE($2::jsonb, combat_log),
                  wave_stats = COALESCE($3::jsonb, wave_stats),
                  balance_summary = COALESCE($4::jsonb, balance_summary),
                  balance_flags = COALESCE($5::jsonb, balance_flags)
            WHERE id = $6`,
          [winnerLaneValue, combatLogPayload, waveStatsPayload, balanceSummaryPayload, balanceFlagsPayload, matchId],
          "matches:close_update_match_legacy"
        );
      };

      try {
        await updateMatchRow(!!actionTimelinePayload);
      } catch (err) {
        if (actionTimelinePayload && /action_timeline/i.test(String(err && err.message || ""))) {
          log.warn("[match] action timeline column missing during finalization; continuing without action timeline", {
            matchId,
            err: err.message,
          });
          await updateMatchRow(false);
        } else {
          throw err;
        }
      }
      if (validSnapshots.length > 0) {
        const placeholders = validSnapshots
          .map((_, i) => `($1, $${i * 3 + 2}, $${i * 3 + 3}, $${i * 3 + 4})`)
          .join(", ");
        const params = [matchId];
        for (const { playerId, laneIndex, result } of validSnapshots) {
          params.push(playerId, laneIndex, result);
        }
          await timedClientQuery(
            client,
            `INSERT INTO match_players (match_id, player_id, lane_index, result)
           VALUES ${placeholders}
           ON CONFLICT (match_id, player_id) DO UPDATE SET
             lane_index = EXCLUDED.lane_index,
             result = EXCLUDED.result`,
          params,
          "matches:close_upsert_players"
        );
      }

      let ratingUpdates = [];
      if (status === "completed" && ratingService && options.mode && options.mode.endsWith("_ranked")) {
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

      await timedClientQuery(
        client,
        `UPDATE matches SET finalization_state = '${targetFinalizationState}', finalized_at = NOW() WHERE id = $1`,
        [matchId],
        `matches:close_mark_${targetFinalizationState}`
      );
      await timedClientQuery(client, "COMMIT", undefined, "matches:close_commit");
      return { matchId, ratingUpdates, finalizationState: targetFinalizationState };
    } catch (err) {
      await timedClientQuery(client, "ROLLBACK", undefined, "matches:close_rollback_error").catch(() => {});
      log.error("[match] close failed:", { matchId, err: err.message });
      return { matchId, ratingUpdates: [], finalizationState: "failed" };
    } finally {
      client.release();
    }
  }

  async function logMatchEnd(matchIdPromise, winnerLane, playerSnapshots, options = {}) {
    return persistMatchFinalization(matchIdPromise, winnerLane, playerSnapshots, {
      ...options,
      status: "completed",
    });
  }

  async function logMatchAbandon(matchIdPromise, playerSnapshots, options = {}) {
    return persistMatchFinalization(matchIdPromise, null, playerSnapshots, {
      ...options,
      status: "abandoned",
    });
  }

  return { logMatchAbandon, logMatchEnd, logMatchStart };
}

module.exports = { createMatchPersistence };
