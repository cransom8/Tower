'use strict';

const express      = require('express');
const router       = express.Router();
const { requireAuth } = require('../middleware/auth');
const { formatPublicSkin } = require('../remoteContent');

const db  = process.env.DATABASE_URL ? require('../db') : null;
const log = require('../logger');

// ── GET /api/skins ───────────────────────────────────────────────────────────
// Public catalog of all enabled skins.
// If authenticated, also returns which skins the player owns + has equipped.
router.get('/', async (req, res) => {
  if (!db) return res.json({ catalog: [], owned: [], equipped: {} });
  try {
    const catalogQ = await db.query(
      `SELECT s.skin_key, s.unit_type, s.name, s.description, s.price, s.currency, s.preview_url,
              CASE
                WHEN scm.id IS NULL THEN NULL
                ELSE json_build_object(
                  'id', scm.id,
                  'content_key', scm.content_key,
                  'addressables_label', scm.addressables_label,
                  'prefab_address', scm.prefab_address,
                  'placeholder_key', scm.placeholder_key,
                  'catalog_url', scm.catalog_url,
                  'content_url', scm.content_url,
                  'version_tag', scm.version_tag,
                  'content_hash', scm.content_hash,
                  'dependency_keys', scm.dependency_keys,
                  'metadata', scm.metadata,
                  'is_critical', scm.is_critical,
                  'enabled', scm.enabled
                )
              END AS remote_content
         FROM skin_catalog s
         LEFT JOIN skin_content_metadata scm ON scm.skin_catalog_id = s.id
        WHERE s.enabled = true
        ORDER BY s.unit_type, s.name`
    );
    const catalog = catalogQ.rows.map((row) => formatPublicSkin(row));

    let owned   = [];
    let equipped = {};

    if (req.player?.sub) {
      const ownedQ = await db.query(
        'SELECT skin_key FROM player_skins WHERE player_id = $1',
        [req.player.sub]
      );
      owned = ownedQ.rows.map(r => r.skin_key);

      const equipQ = await db.query(
        'SELECT unit_type, skin_key FROM player_skin_equip WHERE player_id = $1',
        [req.player.sub]
      );
      for (const row of equipQ.rows) equipped[row.unit_type] = row.skin_key;
    }

    res.json({ catalog, owned, equipped });
  } catch (err) {
    log.error('[skins] GET / error', { err: err.message });
    res.status(500).json({ error: 'internal' });
  }
});

// ── POST /api/skins/equip ────────────────────────────────────────────────────
// Equip an owned skin for a unit type, or unequip (skinKey = null).
router.post('/equip', requireAuth, async (req, res) => {
  if (!db) return res.status(503).json({ error: 'no_db' });
  const { unitType, skinKey } = req.body;
  const playerId = req.player.sub;

  if (!unitType) return res.status(400).json({ error: 'unitType required' });

  try {
    if (!skinKey) {
      // Unequip: remove row
      await db.query(
        'DELETE FROM player_skin_equip WHERE player_id = $1 AND unit_type = $2',
        [playerId, unitType]
      );
      return res.json({ equipped: null });
    }

    // Verify player owns the skin
    const owned = await db.query(
      'SELECT 1 FROM player_skins WHERE player_id = $1 AND skin_key = $2',
      [playerId, skinKey]
    );
    if (owned.rows.length === 0)
      return res.status(403).json({ error: 'skin_not_owned' });

    // Verify skin applies to this unit type
    const catalog = await db.query(
      'SELECT unit_type FROM skin_catalog WHERE skin_key = $1 AND enabled = true',
      [skinKey]
    );
    if (catalog.rows.length === 0)
      return res.status(404).json({ error: 'skin_not_found' });
    if (catalog.rows[0].unit_type !== unitType)
      return res.status(400).json({ error: 'skin_wrong_unit_type' });

    // Upsert
    await db.query(
      `INSERT INTO player_skin_equip (player_id, unit_type, skin_key)
       VALUES ($1, $2, $3)
       ON CONFLICT (player_id, unit_type) DO UPDATE SET skin_key = EXCLUDED.skin_key`,
      [playerId, unitType, skinKey]
    );
    res.json({ equipped: skinKey });
  } catch (err) {
    log.error('[skins] POST /equip error', { err: err.message });
    res.status(500).json({ error: 'internal' });
  }
});

// ── POST /api/skins/buy ──────────────────────────────────────────────────────
// Purchase a skin from the catalog. Deducts gems (stored on players table).
// Requires a `gems` column on players — add migration if not present.
router.post('/buy', requireAuth, async (req, res) => {
  if (!db) return res.status(503).json({ error: 'no_db' });
  const { skinKey } = req.body;
  const playerId    = req.player.sub;

  if (!skinKey) return res.status(400).json({ error: 'skinKey required' });

  try {
    const skinQ = await db.query(
      'SELECT skin_key, price, currency FROM skin_catalog WHERE skin_key = $1 AND enabled = true',
      [skinKey]
    );
    if (skinQ.rows.length === 0)
      return res.status(404).json({ error: 'skin_not_found' });

    const skin = skinQ.rows[0];

    // Check not already owned
    const alreadyQ = await db.query(
      'SELECT 1 FROM player_skins WHERE player_id = $1 AND skin_key = $2',
      [playerId, skinKey]
    );
    if (alreadyQ.rows.length > 0)
      return res.status(409).json({ error: 'already_owned' });

    if (skin.price > 0 && skin.currency === 'gems') {
      // Deduct gems atomically; fail if insufficient
      const deduct = await db.query(
        `UPDATE players SET gems = gems - $1
         WHERE id = $2 AND gems >= $1
         RETURNING gems`,
        [skin.price, playerId]
      );
      if (deduct.rows.length === 0)
        return res.status(402).json({ error: 'insufficient_gems' });
    }

    // Grant skin
    await db.query(
      'INSERT INTO player_skins (player_id, skin_key) VALUES ($1, $2) ON CONFLICT DO NOTHING',
      [playerId, skinKey]
    );

    res.json({ owned: skinKey });
  } catch (err) {
    log.error('[skins] POST /buy error', { err: err.message });
    res.status(500).json({ error: 'internal' });
  }
});

module.exports = router;
