const test = require("node:test");
const assert = require("node:assert/strict");

const { authenticateSocketToken } = require("./helpers");

function makeAuthService() {
  return {
    verifyAccessToken() {
      return {
        sub: "player-1",
        displayName: "Player One",
      };
    },
  };
}

test("authenticateSocketToken authenticates active players", async () => {
  const socket = {};
  const result = await authenticateSocketToken(socket, "token", {
    authService: makeAuthService(),
    db: {
      query: async () => ({ rows: [{ status: "active" }] }),
    },
    playerBanCache: new Map(),
    PLAYER_BAN_CACHE_TTL_MS: 60_000,
  });

  assert.equal(result.ok, true);
  assert.equal(socket.playerId, "player-1");
  assert.equal(socket.playerDisplayName, "Player One");
  assert.equal(socket.playerBanned, false);
});

test("authenticateSocketToken rejects suspended players without attaching identity", async () => {
  const socket = {};
  const result = await authenticateSocketToken(socket, "token", {
    authService: makeAuthService(),
    db: {
      query: async () => ({ rows: [{ status: "suspended" }] }),
    },
    playerBanCache: new Map(),
    PLAYER_BAN_CACHE_TTL_MS: 60_000,
  });

  assert.equal(result.ok, false);
  assert.equal(result.code, "suspended");
  assert.equal(socket.playerId, undefined);
  assert.equal(socket.playerBanned, true);
});

test("authenticateSocketToken fails closed when player status lookup fails", async () => {
  const socket = {};
  const result = await authenticateSocketToken(socket, "token", {
    authService: makeAuthService(),
    db: {
      query: async () => {
        throw new Error("status lookup failed");
      },
    },
    playerBanCache: new Map(),
    PLAYER_BAN_CACHE_TTL_MS: 60_000,
  });

  assert.equal(result.ok, false);
  assert.equal(result.code, "auth_unavailable");
  assert.equal(socket.playerId, undefined);
});
