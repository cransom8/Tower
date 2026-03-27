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
