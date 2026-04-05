"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const {
  ACTION_TIMELINE_SCHEMA_VERSION,
  buildActionTimelinePayload,
  recordTimelineAction,
} = require("./actionTimeline");

test("action timeline payload records human and AI commands with lane metadata", () => {
  const room = {
    roomId: "mlroom_ROOM01",
    players: ["sock-human"],
    laneBySocketId: new Map([["sock-human", 0]]),
    playerNames: new Map([["sock-human", "Alice"]]),
    aiPlayers: [{ laneIndex: 1, difficulty: "hard", displayName: "CPU (Hard)" }],
    raceByLane: ["human", "orc"],
    loadoutByLane: [
      [{ key: "militia" }],
      [{ key: "archer" }],
    ],
    settings: { selectionMode: "manual" },
  };

  const entry = {
    roomId: "mlroom_ROOM01",
    roomCode: "ROOM01",
    dbMode: "multilane",
    matchSeedText: "room-seed-1",
    layoutId: "fort_alpha",
    layoutHash: "layout-hash-1",
    playerSnapshot: [{ playerId: "player-human-1", laneIndex: 0 }],
    actionTimeline: [],
    actionTimelineCounts: {},
    game: {
      matchSeed: 12345,
      configVersionId: "cfg-v1",
      playerCount: 2,
      roundNumber: 3,
      lanes: [
        { laneIndex: 0, team: "red", side: "west", loadoutKeys: ["militia"] },
        { laneIndex: 1, team: "blue", side: "east", loadoutKeys: ["archer"] },
      ],
    },
  };

  recordTimelineAction(entry, room, 0, {
    type: "build_on_pad",
    data: { padId: "market_pad" },
    tickApply: 120,
    actionSeq: 4,
  }, { origin: "player_socket" });
  recordTimelineAction(entry, room, 1, {
    type: "buy_market_unit",
    data: { unitKey: "peasant", count: 2 },
    tickApply: 125,
    actionSeq: 5,
  }, { origin: "ai_runtime" });

  const payload = buildActionTimelinePayload(entry, room, (settings) => settings);
  assert.equal(payload.schemaVersion, ACTION_TIMELINE_SCHEMA_VERSION);
  assert.equal(payload.matchSeedText, "room-seed-1");
  assert.equal(payload.matchSeed, 12345);
  assert.equal(payload.actions.length, 2);
  assert.deepEqual(payload.actions[0], {
    tick: 120,
    round: 3,
    actionSeq: 4,
    laneIndex: 0,
    laneActionIndex: 1,
    actorType: "human",
    origin: "player_socket",
    difficulty: null,
    takeover: false,
    displayName: "Alice",
    playerId: "player-human-1",
    team: "red",
    side: "west",
    type: "build_on_pad",
    data: { padId: "market_pad" },
  });
  assert.deepEqual(payload.actions[1], {
    tick: 125,
    round: 3,
    actionSeq: 5,
    laneIndex: 1,
    laneActionIndex: 1,
    actorType: "ai",
    origin: "ai_runtime",
    difficulty: "hard",
    takeover: false,
    displayName: "CPU (Hard)",
    playerId: null,
    team: "blue",
    side: "east",
    type: "buy_market_unit",
    data: { unitKey: "peasant", count: 2 },
  });
  assert.deepEqual(payload.laneContext, [
    {
      laneIndex: 0,
      isAI: false,
      difficulty: null,
      takeover: false,
      displayName: "Alice",
      playerId: "player-human-1",
      team: "red",
      side: "west",
      raceId: "human",
      loadoutKeys: ["militia"],
    },
    {
      laneIndex: 1,
      isAI: true,
      difficulty: "hard",
      takeover: false,
      displayName: "CPU (Hard)",
      playerId: null,
      team: "blue",
      side: "east",
      raceId: "orc",
      loadoutKeys: ["archer"],
    },
  ]);
});
