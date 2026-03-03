'use strict';

const express = require('express');
const router  = express.Router();
const { optionalAuth } = require('../middleware/auth');

// GET /parties/me — returns current party for the authenticated player.
// Uses in-memory state via app.locals (partiesById, partyByPlayerId).
router.get('/me', optionalAuth, (req, res) => {
  if (!req.player) return res.status(204).end();
  const { partiesById, partyByPlayerId } = req.app.locals;
  const partyId = partyByPlayerId.get(req.player.sub);
  if (!partyId) return res.status(204).end();
  const party = partiesById.get(partyId);
  if (!party) return res.status(204).end();
  res.json({ party });
});

module.exports = router;
