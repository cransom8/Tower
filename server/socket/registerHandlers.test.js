const test = require("node:test");
const assert = require("node:assert/strict");

const { registerSocketHandlers } = require("./registerHandlers");

function createSocket(id) {
  return {
    id,
    handshake: { address: "127.0.0.1" },
    handlers: new Map(),
    joinedRooms: [],
    emitted: [],
    join(roomId) {
      this.joinedRooms.push(roomId);
    },
    leave() {},
    emit(event, payload) {
      this.emitted.push({ event, payload });
    },
    on(event, handler) {
      this.handlers.set(event, handler);
    },
    trigger(event, payload) {
      const handler = this.handlers.get(event);
      if (typeof handler !== "function")
        throw new Error(`Missing handler for ${event}`);
      return handler(payload);
    },
  };
}

function createIo() {
  const events = [];
  const sockets = new Map();
  return {
    events,
    sockets: { sockets },
    on(event, handler) {
      if (event === "connection")
        this.connectionHandler = handler;
    },
    to(target) {
      return {
        emit(event, payload) {
          events.push({ target, event, payload });
        },
      };
    },
  };
}

function createDeps() {
  const io = createIo();
  const gamesByRoomId = new Map();
  const mlRoomsByCode = new Map();
  const partiesById = new Map();
  const partiesByCode = new Map();
  const partyByPlayerId = new Map();
  const sessionBySocketId = new Map();
  const socketByPlayerId = new Map();
  const startMLGameCalls = [];

  function createMLRoom(hostSocketId, displayName) {
    const code = "ROOM01";
    const roomId = `mlroom_${code}`;
    mlRoomsByCode.set(code, {
      roomId,
      players: [hostSocketId],
      laneBySocketId: new Map([[hostSocketId, 0]]),
      playerTeamsBySocketId: new Map(),
      readySet: new Set(),
      playerNames: new Map([[hostSocketId, displayName]]),
      aiPlayers: [],
      settings: {},
      hostSocketId,
    });
    return { code, roomId };
  }

  const deps = {
    attachTakeoverBot() {},
    buildAvailableUnits() { return []; },
    buildRematchStatus() { return { count: 0, needed: 0, allAccepted: false }; },
    checkActionRateLimit() { return true; },
    checkLobbyRateLimit() { return true; },
    createMLRoom,
    detachTakeoverBot() {},
    db: null,
    disconnectGrace: new Map(),
    ffaTeamForLane(laneIndex) {
      return ["red", "yellow", "blue", "green"][laneIndex] || `p${laneIndex}`;
    },
    gamesByRoomId,
    generateCode() { return "ABCDEF"; },
    handlePostWinDecision() { return { ok: false, reason: "unused" }; },
    hasValidInlineLoadoutIds() { return false; },
    io,
    log: {
      info() {},
      warn() {},
      error() {},
    },
    matchmaker: {
      addToQueue() {},
      getLobbyForSocket() { return null; },
      getQueuePlayerCount() { return 0; },
      makeBucketKey(gameType, matchFormat, ranked) {
        return `${gameType}:${matchFormat}:${ranked ? "1" : "0"}`;
      },
    },
    mlLobbyUpdatePayload(code) {
      return { code };
    },
    mlRoomsByCode,
    normalizeMatchSettings(settings) { return settings || {}; },
    pickBalancedMlTeam() { return "red"; },
    partiesByCode,
    partiesById,
    partyByPlayerId,
    requireAuthSocket(_socket, next) { next(); },
    sanitizeDisplayName(name) { return name; },
    sessionBySocketId,
    simMl: {},
    socketByPlayerId,
    startMLGame(roomId, code) {
      startMLGameCalls.push({ roomId, code });
    },
    stopMLGame() {},
    submitWaveReadyVote() {},
    validateLoadoutSelection() { return true; },
    validateMlTeamSetup() { return { ok: true }; },
    verifyReconnectToken() { return null; },
    applyMultilaneAction() { return { ok: true }; },
    authenticateSocketToken() { return Promise.resolve({ ok: false }); },
    RECONNECT_GRACE_MS: 120_000,
    isEnabled() { return true; },
  };

  return {
    deps,
    io,
    mlRoomsByCode,
    partiesById,
    sessionBySocketId,
    socketByPlayerId,
    startMLGameCalls,
  };
}

test("queue-created public matches start the ML runtime after match_found", () => {
  const context = createDeps();
  const { deps, io, partiesById, sessionBySocketId, socketByPlayerId, startMLGameCalls } = context;

  const alphaSocket = createSocket("sid-alpha");
  const bravoSocket = createSocket("sid-bravo");
  io.sockets.sockets.set(alphaSocket.id, alphaSocket);
  io.sockets.sockets.set(bravoSocket.id, bravoSocket);
  socketByPlayerId.set("player-alpha", alphaSocket.id);
  socketByPlayerId.set("player-bravo", bravoSocket.id);

  partiesById.set("party-alpha", {
    id: "party-alpha",
    members: [{ playerId: "player-alpha", displayName: "Alpha", socketId: alphaSocket.id }],
    status: "queued",
    queueMode: "line_wars:ffa:0",
    queueEnteredAt: Date.now(),
  });
  partiesById.set("party-bravo", {
    id: "party-bravo",
    members: [{ playerId: "player-bravo", displayName: "Bravo", socketId: bravoSocket.id }],
    status: "queued",
    queueMode: "line_wars:ffa:0",
    queueEnteredAt: Date.now(),
  });

  const { onMatchFound } = registerSocketHandlers(deps);
  onMatchFound(
    null,
    null,
    "line_wars:ffa:0",
    [
      [{ partyId: "party-alpha", rating: 1200, partySize: 1, queueEnteredAt: Date.now() }],
      [{ partyId: "party-bravo", rating: 1200, partySize: 1, queueEnteredAt: Date.now() }],
    ],
    "line_wars:ffa:0"
  );

  assert.deepEqual(startMLGameCalls, [{ roomId: "mlroom_ROOM01", code: "ROOM01" }]);
  assert.equal(sessionBySocketId.get(alphaSocket.id)?.code, "ROOM01");
  assert.equal(sessionBySocketId.get(bravoSocket.id)?.code, "ROOM01");
  assert.deepEqual(alphaSocket.joinedRooms, ["mlroom_ROOM01"]);
  assert.deepEqual(bravoSocket.joinedRooms, ["mlroom_ROOM01"]);
  assert.equal(partiesById.get("party-alpha").status, "in_match");
  assert.equal(partiesById.get("party-bravo").status, "in_match");

  const matchFoundEvents = io.events.filter((entry) => entry.event === "match_found");
  assert.equal(matchFoundEvents.length, 2);
});

test("legacy queue matches also start the ML runtime after match_found", () => {
  const context = createDeps();
  const { deps, io, partiesById, socketByPlayerId, startMLGameCalls } = context;

  const alphaSocket = createSocket("sid-alpha");
  const bravoSocket = createSocket("sid-bravo");
  io.sockets.sockets.set(alphaSocket.id, alphaSocket);
  io.sockets.sockets.set(bravoSocket.id, bravoSocket);
  socketByPlayerId.set("player-alpha", alphaSocket.id);
  socketByPlayerId.set("player-bravo", bravoSocket.id);

  const partyAlpha = {
    id: "party-alpha",
    members: [{ playerId: "player-alpha", displayName: "Alpha", socketId: alphaSocket.id }],
    status: "queued",
    queueMode: "ranked_2v2",
    queueEnteredAt: Date.now(),
  };
  const partyBravo = {
    id: "party-bravo",
    members: [{ playerId: "player-bravo", displayName: "Bravo", socketId: bravoSocket.id }],
    status: "queued",
    queueMode: "ranked_2v2",
    queueEnteredAt: Date.now(),
  };
  partiesById.set(partyAlpha.id, partyAlpha);
  partiesById.set(partyBravo.id, partyBravo);

  const { onMatchFound } = registerSocketHandlers(deps);
  onMatchFound(partyAlpha, partyBravo, "ranked_2v2");

  assert.deepEqual(startMLGameCalls, [{ roomId: "mlroom_ROOM01", code: "ROOM01" }]);
  assert.equal(partiesById.get("party-alpha").status, "in_match");
  assert.equal(partiesById.get("party-bravo").status, "in_match");
});

test("manual AI add defaults missing difficulty to medium", () => {
  const context = createDeps();
  const { deps, io, mlRoomsByCode, sessionBySocketId } = context;
  const hostSocket = createSocket("sid-host");

  registerSocketHandlers(deps);
  assert.equal(typeof io.connectionHandler, "function");
  io.connectionHandler(hostSocket);

  const { code } = deps.createMLRoom(hostSocket.id, "Host");
  sessionBySocketId.set(hostSocket.id, { mode: "multilane", code });

  hostSocket.trigger("add_ai_to_ml_room", {});

  const room = mlRoomsByCode.get(code);
  assert.equal(Array.isArray(room && room.aiPlayers), true);
  assert.equal(room.aiPlayers.length, 1);
  assert.equal(room.aiPlayers[0].difficulty, "medium");
});
