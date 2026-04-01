const test = require("node:test");
const assert = require("node:assert/strict");

const { createFeatureFlagCache } = require("./featureFlagCache");

test("feature flags fail closed until the initial load completes", () => {
  const cache = createFeatureFlagCache({
    query: async () => ({ rows: [] }),
  });

  assert.equal(cache.isEnabled("ranked_queue_enabled"), false);
  assert.equal(cache.isEnabled("private_match_enabled"), false);
  assert.equal(cache.isEnabled("new_player_registration"), false);
});

test("feature flag cache loads known flags and leaves unknown flags closed", async () => {
  const cache = createFeatureFlagCache({
    query: async () => ({
      rows: [
        { name: "ranked_queue_enabled", enabled: true },
        { name: "public_ffa_enabled", enabled: true },
      ],
    }),
  });

  await cache.start();
  try {
    assert.equal(cache.isEnabled("ranked_queue_enabled"), true);
    assert.equal(cache.isEnabled("public_ffa_enabled"), true);
    assert.equal(cache.isEnabled("private_match_enabled"), false);
    assert.equal(cache.isEnabled("totally_unknown_flag"), false);
  } finally {
    cache.stop();
  }
});

test("feature flag cache aborts startup when the first load fails", async () => {
  const cache = createFeatureFlagCache({
    query: async () => {
      throw new Error("db unavailable");
    },
  });

  await assert.rejects(() => cache.start(), /db unavailable/);
  assert.equal(cache.isEnabled("ranked_queue_enabled"), false);
});
