"use strict";

/**
 * featureFlagCache.js
 * Loads feature_flags from the DB on startup and refreshes every 60s.
 * isEnabled(name) returns true if the flag is enabled, unknown, or DB is unavailable.
 */

function createFeatureFlagCache(db) {
  const flags = new Map();
  let intervalHandle = null;

  async function loadFlags() {
    if (!db) return;
    try {
      const result = await db.query("SELECT name, enabled FROM feature_flags");
      for (const row of result.rows) {
        flags.set(row.name, !!row.enabled);
      }
    } catch {
      // Non-fatal — retain previously loaded values (or defaults on first load).
    }
  }

  function isEnabled(name) {
    if (!db) return true;
    const val = flags.get(name);
    return val === undefined ? true : val;
  }

  function start() {
    loadFlags();
    intervalHandle = setInterval(loadFlags, 60_000);
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

module.exports = { createFeatureFlagCache };
