"use strict";

/**
 * featureFlagCache.js
 * Loads feature_flags from the DB on startup and refreshes every 60s.
 * Unknown or not-yet-loaded flags fail closed so operational controls stay safe.
 */

const FLAG_DEFAULTS = Object.freeze({
  maintenance_mode: true,
  ranked_queue_enabled: false,
  casual_queue_enabled: false,
  new_player_registration: false,
  survival_public_enabled: false,
  public_ffa_enabled: false,
  private_match_enabled: false,
  "1v1_ranked_enabled": false,
});

function createFeatureFlagCache(db) {
  const flags = new Map();
  let hasLoaded = false;
  let intervalHandle = null;

  function getDefaultFlagValue(name) {
    return Object.prototype.hasOwnProperty.call(FLAG_DEFAULTS, name)
      ? FLAG_DEFAULTS[name]
      : false;
  }

  async function loadFlags({ throwOnError = false } = {}) {
    if (!db) {
      hasLoaded = true;
      flags.clear();
      return;
    }

    try {
      const result = await db.query(
        "SELECT name, enabled FROM feature_flags",
        { label: "feature_flags:refresh" }
      );
      flags.clear();
      for (const row of result.rows)
        flags.set(row.name, !!row.enabled);
      hasLoaded = true;
    } catch (err) {
      if (!hasLoaded)
        flags.clear();
      if (throwOnError)
        throw err;
    }
  }

  function isEnabled(name) {
    if (!hasLoaded)
      return getDefaultFlagValue(name);

    const val = flags.get(name);
    return val === undefined ? getDefaultFlagValue(name) : val;
  }

  async function start() {
    await loadFlags({ throwOnError: true });
    intervalHandle = setInterval(() => {
      loadFlags().catch(() => {});
    }, 60_000);
    if (intervalHandle.unref) intervalHandle.unref();
  }

  function stop() {
    if (intervalHandle) {
      clearInterval(intervalHandle);
      intervalHandle = null;
    }
  }

  return { isEnabled, start, stop };
}

module.exports = { FLAG_DEFAULTS, createFeatureFlagCache };
