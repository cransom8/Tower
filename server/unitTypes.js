'use strict';

// In-memory cache of unit_types loaded from the database.
// Provides fast synchronous lookups during sim ticks.
const log = require('./logger');

let _db = null;
let _unitTypes = [];   // ordered array
let _byKey    = {};    // key → unitType object

async function _reload() {
  if (!_db) return;
  const res = await _db.query(`
    SELECT u.*,
      pd.behavior       AS proj_behavior,
      pd.behavior_params AS proj_behavior_params,
      COALESCE(json_agg(
        json_build_object('ability_key', a.ability_key, 'params', a.params)
        ORDER BY a.id
      ) FILTER (WHERE a.id IS NOT NULL), '[]') AS abilities
    FROM unit_types u
    LEFT JOIN projectile_definitions pd ON pd.id = u.projectile_def_id
    LEFT JOIN unit_type_ability_assignments a ON a.unit_type_id = u.id
    GROUP BY u.id, pd.behavior, pd.behavior_params
    ORDER BY u.id
  `);
  _unitTypes = res.rows;
  _byKey = Object.fromEntries(_unitTypes.map(ut => [ut.key, ut]));
  log.info('[unitTypes] loaded', { count: _unitTypes.length });
}

async function loadUnitTypes(db) {
  _db = db;
  await _reload();
}

function reloadUnitTypes() {
  return _reload();
}

function getUnitType(key) {
  return _byKey[key] || null;
}

function getAllUnitTypes() {
  return _unitTypes;
}

function setUnitTypesForTests(unitTypes) {
  _unitTypes = Array.isArray(unitTypes) ? unitTypes.slice() : [];
  _byKey = Object.fromEntries(_unitTypes.map((ut) => [ut.key, ut]));
}

module.exports = { loadUnitTypes, reloadUnitTypes, getUnitType, getAllUnitTypes, setUnitTypesForTests };
