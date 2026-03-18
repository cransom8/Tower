'use strict';

const express = require('express');
const router = express.Router();
const { requireAuth } = require('../middleware/auth');

// GET /api/loadouts - deprecated saved preset slots endpoint.
router.get('/', requireAuth, async (_req, res) => {
  res.json({ loadouts: [], deprecated: true });
});

// PUT /api/loadouts/:slot - deprecated. Match loadouts are now chosen inline
// during the dedicated loadout phase rather than persisted as account presets.
router.put('/:slot', requireAuth, async (_req, res) => {
  res.status(410).json({ error: 'Saved preset loadouts are deprecated.' });
});

module.exports = router;
