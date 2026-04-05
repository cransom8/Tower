"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const { createMultilaneRuntime } = require("../game/multilaneRuntime");

function createRuntime() {
  return createMultilaneRuntime({
    aiRuntime: null,
    db: null,
    getAllUnitTypes: async () => [],
    disconnectGrace: null,
    generateCode: () => "LANE01",
    gamesByRoomId: new Map(),
    io: {
      to() {
        return { emit() {} };
      },
      sockets: { sockets: new Map() },
    },
    issueReconnectToken: () => "token",
    log: { info() {}, warn() {}, error() {} },
    logMatchAbandon: async () => null,
    logMatchEnd: async () => null,
    logMatchStart: async () => null,
    mlRoomsByCode: new Map(),
    normalizeMatchSettings: (settings = {}) => settings,
    ratingService: null,
    resolveLoadout: async () => [],
    sanitizeDisplayName: (value) => value,
    seasonService: null,
    sessionBySocketId: new Map(),
    simMl: {
      TICK_MS: 50,
      TICK_HZ: 20,
      createMLPublicConfig: () => ({ slotDefinitions: [] }),
      createMLGame: () => ({ lanes: [] }),
      createRoundSnapshotLane: (_game, lane) => ({ laneIndex: lane.laneIndex }),
    },
    socketByPlayerId: new Map(),
    stringToSeed: () => 1,
    RECONNECT_GRACE_MS: 1000,
  });
}

test("runtime seat colors follow red, yellow, blue, green", () => {
  const runtime = createRuntime();

  assert.equal(runtime.ffaTeamForLane(0), "red");
  assert.equal(runtime.ffaTeamForLane(1), "yellow");
  assert.equal(runtime.ffaTeamForLane(2), "blue");
  assert.equal(runtime.ffaTeamForLane(3), "green");
  assert.equal(
    runtime.pickBalancedMlTeam({
      players: ["host"],
      aiPlayers: [],
    }),
    "yellow"
  );
});

test("stopMLGame abandons active matches before deleting runtime state", async () => {
  const gamesByRoomId = new Map();
  const mlRoomsByCode = new Map();
  const abandonCalls = [];
  const roomId = "mlroom_LANE01";
  const code = "LANE01";
  const matchId = "11111111-1111-4111-8111-111111111111";
  const playerId = "22222222-2222-4222-8222-222222222222";
  const tickHandle = setInterval(() => {}, 1000);

  const runtime = createMultilaneRuntime({
    aiRuntime: null,
    db: null,
    getAllUnitTypes: async () => [],
    disconnectGrace: null,
    generateCode: () => code,
    gamesByRoomId,
    io: {
      to() {
        return { emit() {} };
      },
      sockets: { sockets: new Map() },
    },
    issueReconnectToken: () => "token",
    log: { info() {}, warn() {}, error() {} },
    logMatchAbandon: async (...args) => {
      abandonCalls.push(args);
      return { finalizationState: "abandoned" };
    },
    logMatchEnd: async () => null,
    logMatchStart: async () => null,
    mlRoomsByCode,
    normalizeMatchSettings: (settings = {}) => settings,
    ratingService: null,
    resolveLoadout: async () => [],
    sanitizeDisplayName: (value) => value,
    seasonService: null,
    sessionBySocketId: new Map(),
    simMl: {
      TICK_MS: 50,
      TICK_HZ: 20,
      createMLPublicConfig: () => ({ slotDefinitions: [] }),
      createMLGame: () => ({ lanes: [] }),
      finalizeMatchBalance: () => ({
        waveReports: [{ waveNumber: 1 }],
        summary: { readable: { diagnosis: [], perWaveLog: [] } },
        flags: [],
      }),
    },
    socketByPlayerId: new Map(),
    stringToSeed: () => 1,
    RECONNECT_GRACE_MS: 1000,
  });

  mlRoomsByCode.set(code, {
    roomId,
    players: ["host"],
    laneBySocketId: new Map([["host", 0]]),
    playerNames: new Map([["host", "Host"]]),
    aiPlayers: [],
    settings: {},
    raceByLane: ["human"],
    loadoutByLane: [[{ key: "spearman" }]],
  });
  gamesByRoomId.set(roomId, {
    game: {
      phase: "playing",
      tick: 25,
      playerCount: 1,
      lanes: [{ laneIndex: 0, team: "red", side: "left", loadoutKeys: ["spearman"] }],
    },
    tickHandle,
    mode: "multilane",
    roomId,
    roomCode: code,
    matchIdPromise: Promise.resolve(matchId),
    playerSnapshot: [{ playerId, laneIndex: 0 }],
    dbMode: "multilane",
    partyASize: 1,
    actionTimeline: [],
    actionTimelineCounts: {},
  });

  await runtime.stopMLGame(roomId, code, { reason: "last_player_left" });

  assert.equal(abandonCalls.length, 1);
  assert.equal(gamesByRoomId.has(roomId), false);
  const [matchIdPromiseArg, snapshotsArg, optionsArg] = abandonCalls[0];
  assert.equal(await matchIdPromiseArg, matchId);
  assert.deepEqual(snapshotsArg, [{ playerId, laneIndex: 0, result: "draw" }]);
  assert.equal(optionsArg.mode, "multilane");
  assert.equal(optionsArg.balanceFlags[0].key, "match_abandoned");
  assert.equal(optionsArg.balanceFlags[0].reason, "last_player_left");
  assert.equal(optionsArg.waveStats[0].waveNumber, 1);
  clearInterval(tickHandle);
});

test("forfeitMultilaneMatch ends the match and captures a terminal report snapshot", () => {
  const gamesByRoomId = new Map();
  const mlRoomsByCode = new Map();
  const emitted = [];
  const roomId = "mlroom_LANE01";
  const code = "LANE01";

  const runtime = createMultilaneRuntime({
    aiRuntime: null,
    db: null,
    getAllUnitTypes: async () => [],
    disconnectGrace: null,
    generateCode: () => code,
    gamesByRoomId,
    io: {
      to(target) {
        return {
          emit(event, payload) {
            emitted.push({ target, event, payload });
          },
        };
      },
      sockets: { sockets: new Map() },
    },
    issueReconnectToken: () => "token",
    log: { info() {}, warn() {}, error() {} },
    logMatchAbandon: async () => null,
    logMatchEnd: async () => null,
    logMatchStart: async () => null,
    mlRoomsByCode,
    normalizeMatchSettings: (settings = {}) => settings,
    resolveLoadout: async () => [],
    sanitizeDisplayName: (value) => value,
    sessionBySocketId: new Map(),
    simMl: {
      TICK_MS: 50,
      TICK_HZ: 20,
      createMLPublicConfig: () => ({ slotDefinitions: [] }),
      createMLGame: () => ({ lanes: [] }),
      createRoundSnapshotLane: (_game, lane) => ({
        laneIndex: lane.laneIndex,
        team: lane.team,
        eliminated: !!lane.eliminated,
      }),
    },
    socketByPlayerId: new Map(),
    stringToSeed: () => 1,
    RECONNECT_GRACE_MS: 1000,
  });

  mlRoomsByCode.set(code, {
    roomId,
    players: ["host"],
    laneBySocketId: new Map([["host", 0]]),
    playerNames: new Map([["host", "Host"]]),
    aiPlayers: [
      { laneIndex: 1, difficulty: "medium", team: "blue", displayName: "CPU (Medium)" },
    ],
  });
  gamesByRoomId.set(roomId, {
    game: {
      phase: "playing",
      tick: 180,
      roundNumber: 5,
      lanes: [
        { laneIndex: 0, team: "red", side: "left", lives: 12, eliminated: false },
        { laneIndex: 1, team: "blue", side: "right", lives: 18, eliminated: false },
      ],
      roundSnapshots: [],
    },
    eliminatedNotified: new Set(),
  });

  const result = runtime.forfeitMultilaneMatch(roomId, code, "host");
  const entry = gamesByRoomId.get(roomId);

  assert.equal(result.ok, true);
  assert.equal(entry.game.phase, "ended");
  assert.equal(entry.game.matchState, "final_game_over");
  assert.equal(entry.game.finalGameOverReason, "player_forfeit");
  assert.equal(entry.game.forfeitLaneIndex, 0);
  assert.equal(entry.game.officialWinnerLane, 1);
  assert.equal(entry.game.winner, 1);
  assert.equal(entry.game.lanes[0].eliminated, true);
  assert.equal(entry.game.lanes[0].lives, 0);
  assert.equal(entry.game.finalSnapshotCaptured, true);
  assert.equal(entry.game.roundSnapshots.length, 1);
  assert.equal(entry.game.roundSnapshots[0].terminal, true);
  assert.deepEqual(entry.game.roundSnapshots[0].lanes.map((lane) => lane.laneIndex), [0, 1]);
  assert.ok(
    emitted.some((event) =>
      event.target === roomId
      && event.event === "ml_player_eliminated"
      && event.payload?.laneIndex === 0
      && event.payload?.reason === "forfeit")
  );
  assert.ok(
    emitted.some((event) =>
      event.target === "host"
      && event.event === "ml_spectator_join"
      && event.payload?.laneIndex === 0)
  );
});
