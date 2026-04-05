"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const { createMultilaneRuntime } = require("../game/multilaneRuntime");

function createRuntimeHarness({ remainingWaveMobs = 0, startNextWaveNow = () => true } = {}) {
  const gamesByRoomId = new Map();
  const mlRoomsByCode = new Map();
  const emitted = [];

  const runtime = createMultilaneRuntime({
    aiRuntime: null,
    db: null,
    getAllUnitTypes: async () => [],
    disconnectGrace: null,
    generateCode: () => "SOLO01",
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
    ratingService: null,
    resolveLoadout: async () => [],
    sanitizeDisplayName: (value) => value,
    seasonService: null,
    sessionBySocketId: new Map(),
    simMl: {
      TICK_MS: 50,
      TICK_HZ: 20,
      countRemainingWaveMobs: () => remainingWaveMobs,
      startNextWaveNow,
    },
    socketByPlayerId: new Map(),
    stringToSeed: () => 1,
    RECONNECT_GRACE_MS: 1000,
  });

  return { runtime, gamesByRoomId, mlRoomsByCode, emitted };
}

function seedSoloWinState(gamesByRoomId, mlRoomsByCode) {
  const code = "SOLO01";
  const roomId = `mlroom_${code}`;
  const room = {
    roomId,
    players: ["host"],
    laneBySocketId: new Map([["host", 0]]),
    playerTeamsBySocketId: new Map([["host", "red"]]),
    readySet: new Set(),
    playerNames: new Map([["host", "Solo"]]),
    aiPlayers: [{ laneIndex: 1, difficulty: "medium", team: "blue" }],
  };
  const game = {
    phase: "playing",
    matchState: "pvp_resolved",
    awaitingPostWinDecision: true,
    continuedIntoSurvival: false,
    survivalStartedAtTick: null,
    survivalStartRound: null,
    tick: 480,
    roundNumber: 6,
    hasSpawnedWave: true,
    officialWinnerLane: 0,
    officialWinningTeam: "red",
    officialWinningSide: "left",
    losingTeam: "blue",
    losingSide: "right",
    lanes: [
      { laneIndex: 0, eliminated: false, team: "red" },
      { laneIndex: 1, eliminated: true, team: "blue" },
    ],
  };
  const entry = {
    game,
    waveReadyVotes: new Set(),
    waveReadyWaveNumber: 7,
  };

  mlRoomsByCode.set(code, room);
  gamesByRoomId.set(roomId, entry);

  return { code, roomId, entry, game };
}

test("continue clears a stale resolved state and auto-starts the next wave when the board is clear", () => {
  let startCalls = 0;
  const harness = createRuntimeHarness({
    remainingWaveMobs: 0,
    startNextWaveNow: () => {
      startCalls += 1;
      return true;
    },
  });
  const { code, roomId, entry, game } = seedSoloWinState(harness.gamesByRoomId, harness.mlRoomsByCode);

  const result = harness.runtime.handlePostWinDecision(roomId, code, "continue", "host");

  assert.equal(result.ok, true);
  assert.equal(result.startedImmediately, true);
  assert.equal(game.matchState, "active_survival");
  assert.equal(game.awaitingPostWinDecision, false);
  assert.equal(game.continuedIntoSurvival, true);
  assert.equal(startCalls, 1, "continue should immediately start the next wave when nothing remains alive");
  assert.ok(
    harness.emitted.some((evt) => evt.event === "ml_survival_continuation_started"),
    "clients should be told the match is active again"
  );
});

test("continue clears a stale resolved state even when the current wave is still clearing", () => {
  let startCalls = 0;
  const harness = createRuntimeHarness({
    remainingWaveMobs: 3,
    startNextWaveNow: () => {
      startCalls += 1;
      return true;
    },
  });
  const { code, roomId, entry, game } = seedSoloWinState(harness.gamesByRoomId, harness.mlRoomsByCode);

  const result = harness.runtime.handlePostWinDecision(roomId, code, "continue", "host");

  assert.equal(result.ok, true);
  assert.equal(result.startedImmediately, false);
  assert.equal(game.matchState, "active_survival");
  assert.equal(startCalls, 0, "the next wave should wait until the remaining mobs finish clearing");
  assert.ok(
    harness.emitted.some((evt) => evt.event === "ml_survival_continuation_started"),
    "the client should receive the active-survival continuation signal"
  );
});
