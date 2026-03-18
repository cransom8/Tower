'use strict';

// In-memory cache of barracks_levels loaded from the database.
// Phase F: getCurrentBarracksMult() is the only sim-facing function.
const log = require('./logger');

let _db = null;
let _levels = {}; // level (integer) → { multiplier: number, upgradeCost: number }

async function _reload() {
  if (!_db) return;
  const res = await _db.query(
    'SELECT level, multiplier, upgrade_cost FROM barracks_levels ORDER BY level'
  );
  _levels = {};
  for (const row of res.rows) {
    _levels[Number(row.level)] = {
      multiplier:  Number(row.multiplier),
      upgradeCost: Number(row.upgrade_cost),
    };
  }
  log.info('[barracksLevels] loaded', { count: res.rows.length });
}

async function loadBarracksLevels(db) {
  _db = db;
  await _reload();
}

function reloadBarracksLevels() {
  return _reload();
}

/**
 * Return the stat multiplier for the given barracks level.
 * Falls back to 1.0 if the level is not found in the cache.
 */
function getCurrentBarracksMult(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  const row = _levels[lvl];
  return row ? row.multiplier : 1.0;
}

/**
 * Return { multiplier, upgradeCost } for the given level, or null if not found.
 */
function getBarracksUpgradeDef(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  return _levels[lvl] || null;
}

/**
 * Return the highest level present in the cache.
 */
function getMaxBarracksLevel() {
  const keys = Object.keys(_levels).map(Number);
  return keys.length > 0 ? Math.max(...keys) : 5;
}

module.exports = {
  loadBarracksLevels,
  reloadBarracksLevels,
  getCurrentBarracksMult,
  getBarracksUpgradeDef,
  getMaxBarracksLevel,
};
