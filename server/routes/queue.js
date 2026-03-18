'use strict';

const express    = require('express');
const router     = express.Router();
const { optionalAuth } = require('../middleware/auth');
const matchmaker = require('../services/matchmaker');

// GET /queue/status — returns queue entry for the authenticated player's party.
router.get('/status', optionalAuth, (req, res) => {
  if (!req.player) return res.status(204).end();
  const { partyByPlayerId } = req.app.locals;
  const partyId = partyByPlayerId.get(req.player.sub);
  if (!partyId) return res.status(204).end();
  const entry = matchmaker.getQueueEntry(partyId);
  if (!entry) return res.status(204).end();
  const elapsed = Math.floor((Date.now() - entry.queueEnteredAt) / 1000);
  res.json({ status: 'queued', mode: entry.mode, elapsed });
});

module.exports = router;
