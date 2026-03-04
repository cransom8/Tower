'use strict';
const log = require('./logger');

// Loads the active game config from the DB at server startup.
// The sims still use their own hardcoded defaults for now; changes take
// effect on the next server restart once a new config is published here.
// Phase 3 will wire the sims to read from getActiveConfig() at game creation.

const { CLASSIC, MULTILANE } = require('./gameDefaults');

// mode → config_json object (set at startup)
const _active = Object.create(null);

async function loadActiveConfigs(db) {
  try {
    const r = await db.query(
      `SELECT mode, config_json FROM game_configs WHERE is_active = true`
    );
    for (const row of r.rows) {
      _active[row.mode] = row.config_json;
    }
    if (Object.keys(_active).length > 0) {
      log.info('[gameConfig] loaded active configs from DB', { modes: Object.keys(_active).join(', ') });
    }
  } catch (err) {
    log.warn('[gameConfig] load failed, using hardcoded defaults:', { err: err.message });
  }
}

// Returns the active config for a mode, or the hardcoded default.
function getActiveConfig(mode) {
  return _active[mode] || (mode === 'classic' ? CLASSIC : MULTILANE);
}

// Returns the current defaults (for display in admin when no DB config exists)
function getDefaults(mode) {
  return mode === 'classic' ? CLASSIC : MULTILANE;
}

module.exports = { loadActiveConfigs, getActiveConfig, getDefaults };
