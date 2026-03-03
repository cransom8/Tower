"use strict";

const express = require("express");
const router = express.Router();

// GET /matches/history?playerId=<uuid>&page=<int>
// Returns last 20 matches for a player (paginated, public).
router.get("/history", async (req, res) => {
  if (!process.env.DATABASE_URL) {
    return res.json({ matches: [], page: 1 });
  }
  const db = require("../db");

  const rawPlayerId = String(req.query.playerId || "").trim();
  const pageNum = Math.min(Math.max(1, parseInt(req.query.page) || 1), 100);
  const limit = 20;
  const offset = (pageNum - 1) * limit;

  if (!rawPlayerId) {
    return res.status(400).json({ error: "playerId required" });
  }

  // Basic UUID format guard to avoid unnecessary DB hits
  const uuidRe = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRe.test(rawPlayerId)) {
    return res.status(400).json({ error: "Invalid playerId" });
  }

  try {
    const result = await db.query(
      `SELECT
         m.id, m.room_id, m.mode, m.winner_lane, m.started_at, m.ended_at, m.status,
         mp.lane_index, mp.result
       FROM matches m
       JOIN match_players mp ON mp.match_id = m.id
       WHERE mp.player_id = $1
       ORDER BY m.started_at DESC
       LIMIT $2 OFFSET $3`,
      [rawPlayerId, limit, offset]
    );
    res.json({ matches: result.rows, page: pageNum });
  } catch (err) {
    console.error("[matches] history error:", err.message);
    res.status(500).json({ error: "Internal server error" });
  }
});

module.exports = router;
