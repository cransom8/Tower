'use strict';

const express = require('express');
const router  = express.Router();
const { requireAuth } = require('../middleware/auth');

const db  = process.env.DATABASE_URL ? require('../db') : null;
const log = require('../logger');

// GET /api/loadouts — player's 4 saved slots
router.get('/', requireAuth, async (req, res) => {
  if (!db) return res.json({ loadouts: [] });
  try {
    const result = await db.query(
      'SELECT slot, name, unit_type_ids FROM loadouts WHERE player_id = $1 ORDER BY slot',
      [req.player.sub]
    );
    res.json({ loadouts: result.rows });
  } catch (err) {
    log.error('[loadouts] GET error', { err: err.message });
    res.status(500).json({ error: 'DB error' });
  }
});

// PUT /api/loadouts/:slot — upsert slot 0–3 with { name, unitTypeIds: [id×5] }
router.put('/:slot', requireAuth, async (req, res) => {
  const slot = parseInt(req.params.slot, 10);
  if (!Number.isInteger(slot) || slot < 0 || slot > 3) {
    return res.status(400).json({ error: 'slot must be 0–3' });
  }
  if (!db) return res.status(503).json({ error: 'DB unavailable' });

  const { name = '', unitTypeIds } = req.body || {};
  if (!Array.isArray(unitTypeIds) || unitTypeIds.length !== 5) {
    return res.status(400).json({ error: 'unitTypeIds must be an array of 5 IDs' });
  }
  if (!unitTypeIds.every(id => Number.isInteger(id) && id > 0)) {
    return res.status(400).json({ error: 'unitTypeIds must be positive integers' });
  }

  try {
    // Loadouts allow enabled attacker unit types (moving or both).
    const validRows = await db.query(
      `SELECT id
         FROM unit_types
        WHERE enabled = true
          AND behavior_mode IN ('moving', 'both')
          AND id = ANY($1::int[])`,
      [unitTypeIds]
    );
    const validIds = new Set(validRows.rows.map(r => Number(r.id)));
    const allValid = unitTypeIds.every(id => validIds.has(Number(id)));
    if (!allValid) {
      return res.status(400).json({ error: 'All unitTypeIds must be enabled attacker unit types' });
    }

    const result = await db.query(
      `INSERT INTO loadouts (player_id, slot, name, unit_type_ids, updated_at)
       VALUES ($1, $2, $3, $4, NOW())
       ON CONFLICT (player_id, slot)
       DO UPDATE SET name = $3, unit_type_ids = $4, updated_at = NOW()
       RETURNING slot, name, unit_type_ids`,
      [req.player.sub, slot, String(name).slice(0, 40), unitTypeIds]
    );
    res.json({ loadout: result.rows[0] });
  } catch (err) {
    log.error('[loadouts] PUT error', { err: err.message });
    res.status(500).json({ error: 'DB error' });
  }
});

module.exports = router;
