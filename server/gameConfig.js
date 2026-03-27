'use strict';
const log = require('./logger');

// Loads the active game config from the DB at server startup.
// Multilane runtime consumers are expected to fail loudly when the active
// config is missing or invalid instead of silently falling back.

const { CLASSIC, MULTILANE } = require('./gameDefaults');

// mode → config_json object (set at startup)
const _active = Object.create(null);
const MULTILANE_GLOBAL_PARAM_RULES = Object.freeze({
  startGold: { min: 0, max: 10000, integer: true },
  startIncome: { min: 0, max: 1000, integer: false },
  livesStart: { min: 1, max: 1000, integer: true },
  teamHpStart: { min: 1, max: 1000, integer: true },
  buildPhaseTicks: { min: 20, max: 7200, integer: true },
  transitionPhaseTicks: { min: 20, max: 7200, integer: true },
});

function validateMultilaneNumericParam(globalParams, fieldName, rule, context) {
  if (!globalParams || typeof globalParams !== 'object' || Array.isArray(globalParams)) {
    throw new Error(`[multilane-config] ${context} is missing globalParams.`);
  }
  if (!Object.prototype.hasOwnProperty.call(globalParams, fieldName)) {
    throw new Error(`[multilane-config] ${context} is missing globalParams.${fieldName}.`);
  }

  const rawValue = globalParams[fieldName];
  const value = Number(rawValue);
  if (!Number.isFinite(value)) {
    throw new Error(`[multilane-config] ${context} has invalid globalParams.${fieldName}; expected a finite number.`);
  }
  if (value < rule.min || value > rule.max) {
    throw new Error(
      `[multilane-config] ${context} has out-of-range globalParams.${fieldName}; expected ${rule.min}-${rule.max}.`
    );
  }
  if (rule.integer && !Number.isInteger(value)) {
    throw new Error(`[multilane-config] ${context} has non-integer globalParams.${fieldName}; expected a whole number.`);
  }

  return value;
}

function validateMultilaneConfig(config, context = 'multilane config') {
  if (!config || typeof config !== 'object' || Array.isArray(config)) {
    throw new Error(`[multilane-config] ${context} must be an object.`);
  }

  const globalParams = config.globalParams;
  const normalizedGlobalParams = { ...(globalParams || {}) };
  for (const [fieldName, rule] of Object.entries(MULTILANE_GLOBAL_PARAM_RULES)) {
    normalizedGlobalParams[fieldName] = validateMultilaneNumericParam(globalParams, fieldName, rule, context);
  }

  return {
    ...config,
    globalParams: normalizedGlobalParams,
  };
}

function validateConfig(mode, config, context = `${mode} config`) {
  if (mode === 'multilane') {
    return validateMultilaneConfig(config, context);
  }
  return config;
}

async function loadActiveConfigs(db) {
  try {
    const r = await db.query(
      `SELECT mode, config_json FROM game_configs WHERE is_active = true`
    );
    for (const row of r.rows) {
      _active[row.mode] = validateConfig(row.mode, row.config_json, `active ${row.mode} config`);
    }
    if (Object.keys(_active).length > 0) {
      log.info('[gameConfig] loaded active configs from DB', { modes: Object.keys(_active).join(', ') });
    }
  } catch (err) {
    log.error('[gameConfig] load failed; strict multilane config validation is blocking fallback usage', { err: err.message });
    throw err;
  }
}

// Returns the active config for a mode, or null when none is loaded.
function getActiveConfig(mode) {
  return _active[mode] || null;
}

function getRequiredActiveConfig(mode) {
  const config = _active[mode];
  if (!config) {
    throw new Error(`[gameConfig] Missing active ${mode} config. Publish and activate one in Admin before starting a match.`);
  }
  return validateConfig(mode, config, `active ${mode} config`);
}

// Returns the current defaults (for display in admin when no DB config exists)
function getDefaults(mode) {
  return mode === 'classic' ? CLASSIC : MULTILANE;
}

function setActiveConfig(mode, config) {
  if (!mode) return;
  if (config == null) {
    delete _active[mode];
    return;
  }
  _active[mode] = validateConfig(mode, config, `active ${mode} config`);
}

module.exports = {
  getActiveConfig,
  getDefaults,
  getRequiredActiveConfig,
  loadActiveConfigs,
  setActiveConfig,
  validateConfig,
};
