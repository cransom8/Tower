"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const { createMatchPersistence } = require("./matchPersistence");

test("match finalization persists the action timeline payload on the match row", async () => {
  const queries = [];
  let released = false;
  const client = {
    async query(text, params) {
      queries.push({ text, params });
      if (/SELECT finalization_state FROM matches/i.test(text))
        return { rows: [{ finalization_state: "in_progress" }], rowCount: 1 };
      return { rows: [], rowCount: 1 };
    },
    release() {
      released = true;
    },
  };

  const persistence = createMatchPersistence({
    db: {
      async getClient() {
        return client;
      },
    },
    log: {
      info() {},
      warn() {},
      error() {},
    },
    ratingService: null,
    seasonService: null,
  });

  const matchId = "11111111-1111-4111-8111-111111111111";
  const playerId = "22222222-2222-4222-8222-222222222222";
  const timeline = {
    schemaVersion: 1,
    actions: [
      { tick: 40, laneIndex: 0, type: "build_on_pad", data: { padId: "market_pad" } },
    ],
  };

  const result = await persistence.logMatchEnd(
    Promise.resolve(matchId),
    0,
    [{ playerId, laneIndex: 0, result: "win" }],
    {
      mode: "multilane",
      actionTimeline: timeline,
    }
  );

  assert.equal(result.finalizationState, "completed");
  assert.equal(released, true);

  const updateQuery = queries.find((entry) => /action_timeline/i.test(entry.text));
  assert.ok(updateQuery, "expected matches close query to write action_timeline");
  const payload = JSON.parse(updateQuery.params[5]);
  assert.equal(payload.matchId, matchId);
  assert.deepEqual(payload.actions, timeline.actions);
});

test("match abandonment persists telemetry and marks the row abandoned", async () => {
  const queries = [];
  let released = false;
  const client = {
    async query(text, params) {
      queries.push({ text, params });
      if (/SELECT finalization_state FROM matches/i.test(text))
        return { rows: [{ finalization_state: "pending" }], rowCount: 1 };
      return { rows: [], rowCount: 1 };
    },
    release() {
      released = true;
    },
  };

  const persistence = createMatchPersistence({
    db: {
      async getClient() {
        return client;
      },
    },
    log: {
      info() {},
      warn() {},
      error() {},
    },
    ratingService: null,
    seasonService: null,
  });

  const matchId = "33333333-3333-4333-8333-333333333333";
  const playerId = "44444444-4444-4444-8444-444444444444";

  const result = await persistence.logMatchAbandon(
    Promise.resolve(matchId),
    [{ playerId, laneIndex: 0, result: "draw" }],
    {
      waveStats: [{ waveNumber: 1 }],
      balanceSummary: { readable: { diagnosis: [], perWaveLog: [] } },
      balanceFlags: [{ key: "match_abandoned", severity: "medium" }],
      actionTimeline: { schemaVersion: 1, actions: [] },
    }
  );

  assert.equal(result.finalizationState, "abandoned");
  assert.equal(released, true);
  assert.ok(
    queries.some((entry) => /SET status = 'abandoned'/i.test(entry.text)),
    "expected abandoned match update query"
  );
  assert.ok(
    queries.some((entry) => /SET finalization_state = 'abandoned'/i.test(entry.text)),
    "expected abandoned finalization state update query"
  );
});

test("match finalization upserts match_players rows with parameterized UUID values", async () => {
  const queries = [];
  const client = {
    async query(text, params) {
      queries.push({ text, params });
      if (/SELECT finalization_state FROM matches/i.test(text))
        return { rows: [{ finalization_state: "pending" }], rowCount: 1 };
      return { rows: [], rowCount: 1 };
    },
    release() {},
  };

  const persistence = createMatchPersistence({
    db: {
      async getClient() {
        return client;
      },
    },
    log: {
      info() {},
      warn() {},
      error() {},
    },
    ratingService: null,
    seasonService: null,
  });

  const matchId = "55555555-5555-4555-8555-555555555555";
  const playerA = "66666666-6666-4666-8666-666666666666";
  const playerB = "77777777-7777-4777-8777-777777777777";

  const result = await persistence.logMatchEnd(
    Promise.resolve(matchId),
    0,
    [
      { playerId: playerA, laneIndex: 0, result: "win" },
      { playerId: playerB, laneIndex: 1, result: "loss" },
    ],
    { mode: "multilane" }
  );

  assert.equal(result.finalizationState, "completed");

  const upsertQuery = queries.find((entry) => /INSERT INTO match_players/i.test(entry.text));
  assert.ok(upsertQuery, "expected match_players upsert query");
  assert.match(upsertQuery.text, /VALUES \(\$1, \$2, \$3, \$4\), \(\$1, \$5, \$6, \$7\)/i);
  assert.deepEqual(
    upsertQuery.params,
    [matchId, playerA, 0, "win", playerB, 1, "loss"]
  );
});
