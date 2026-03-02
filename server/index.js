// server/index.js
"use strict";

const express = require("express");
const http = require("http");
const cors = require("cors");
const path = require("path");
const { Server } = require("socket.io");

const sim = require("./sim");
const simMl = require("./sim-multilane");

const app = express();
app.use(cors());

// Serve client at /client
app.use("/client", express.static(path.join(__dirname, "client")));

const server = http.createServer(app);
const io = new Server(server, {
  maxHttpBufferSize: 1e4, // fix #8: cap incoming message size at 10 KB
  cors: { origin: process.env.ALLOWED_ORIGIN || "*", methods: ["GET", "POST"] }, // fix #2: use env var in production
});

const ALLOWED_ACTION_TYPES = new Set(["spawn_unit", "build_tower", "upgrade_tower", "sell_tower"]);

// code -> { roomId, players: [socketId], sidesBySocketId: Map<socketId, side>, createdAt }
const roomsByCode = new Map();
// code -> { roomId, players:[socketId], laneBySocketId:Map, readySet:Set, playerNames:Map, createdAt, mode:"multilane", hostSocketId }
const mlRoomsByCode = new Map();
// socketId -> { code, roomId, side } | { code, roomId, laneIndex, mode:"multilane" }
const sessionBySocketId = new Map();

// roomId -> { game, tickHandle, snapshotEveryNTicks }
const gamesByRoomId = new Map();

// fix #4: per-socket rate-limit counters
// socketId -> { lobbyCount, lobbyWindowStart, actionCount, actionWindowStart }
const rateLimitBySocketId = new Map();

function checkLobbyRateLimit(socketId) {
  const now = Date.now();
  let r = rateLimitBySocketId.get(socketId);
  if (!r) {
    r = { lobbyCount: 0, lobbyWindowStart: now, actionCount: 0, actionWindowStart: now };
    rateLimitBySocketId.set(socketId, r);
  }
  if (now - r.lobbyWindowStart > 60_000) {
    r.lobbyCount = 0;
    r.lobbyWindowStart = now;
  }
  r.lobbyCount++;
  return r.lobbyCount <= 10; // max 10 create_room / join_room per minute
}

function checkActionRateLimit(socketId) {
  const now = Date.now();
  let r = rateLimitBySocketId.get(socketId);
  if (!r) {
    r = { lobbyCount: 0, lobbyWindowStart: now, actionCount: 0, actionWindowStart: now };
    rateLimitBySocketId.set(socketId, r);
  }
  if (now - r.actionWindowStart > 1_000) {
    r.actionCount = 0;
    r.actionWindowStart = now;
  }
  r.actionCount++;
  return r.actionCount <= 30; // max 30 player_actions per second
}

function generateCode(len = 6) {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let out = "";
  for (let i = 0; i < len; i++) out += chars[Math.floor(Math.random() * chars.length)];
  return out;
}

function createRoom() {
  let code;
  do code = generateCode(6);
  while (roomsByCode.has(code));
  const roomId = `room_${code}`;
  roomsByCode.set(code, { roomId, players: [], sidesBySocketId: new Map(), createdAt: Date.now() });
  return { code, roomId };
}

function startGame(roomId) {
  if (gamesByRoomId.has(roomId)) return;

  const game = sim.createGame();
  const snapshotEveryNTicks = 2; // 20hz tick, 10hz snapshots

  let localTick = 0;
  const tickHandle = setInterval(() => {
    const entry = gamesByRoomId.get(roomId);
    if (!entry) return;

    sim.tick(entry.game);
    localTick++;

    if (localTick % snapshotEveryNTicks === 0) {
      io.to(roomId).emit("state_snapshot", sim.createSnapshot(entry.game));
    }

    if (entry.game.phase === "ended") {
      io.to(roomId).emit("game_over", { winner: entry.game.winner });
      stopGame(roomId);
    }
  }, sim.TICK_MS);

  gamesByRoomId.set(roomId, { game, tickHandle, snapshotEveryNTicks });
  console.log(`[game] started ${roomId}`);
}

function stopGame(roomId) {
  const entry = gamesByRoomId.get(roomId);
  if (!entry) return;
  clearInterval(entry.tickHandle);
  gamesByRoomId.delete(roomId);
  console.log(`[game] stopped ${roomId}`);

  // fix #5: schedule room cleanup 60 s after game ends to prevent indefinite memory leak
  setTimeout(() => {
    for (const [code, room] of roomsByCode.entries()) {
      if (room.roomId === roomId) {
        roomsByCode.delete(code);
        console.log(`[room] cleaned up ${code} (post-game timeout)`);
        break;
      }
    }
  }, 60_000);
}

// ── Multi-Lane helpers ───────────────────────────────────────────────────────

function createMLRoom(hostSocketId, displayName) {
  let code;
  do code = generateCode(6);
  while (roomsByCode.has(code) || mlRoomsByCode.has(code));
  const roomId = `mlroom_${code}`;
  const room = {
    roomId,
    players: [hostSocketId],
    laneBySocketId: new Map([[hostSocketId, 0]]),
    readySet: new Set(),
    playerNames: new Map([[hostSocketId, String(displayName || "Player").slice(0, 20)]]),
    createdAt: Date.now(),
    mode: "multilane",
    hostSocketId,
  };
  mlRoomsByCode.set(code, room);
  return { code, roomId };
}

function mlLobbyUpdatePayload(code, room) {
  const players = room.players.map(sid => ({
    laneIndex: room.laneBySocketId.get(sid),
    displayName: room.playerNames.get(sid) || "Player",
    ready: room.readySet.has(sid),
  }));
  return { code, players, hostLaneIndex: 0 };
}

function startMLGame(roomId, code, io) {
  if (gamesByRoomId.has(roomId)) return;
  const room = mlRoomsByCode.get(code);
  if (!room) return;

  const playerCount = room.players.length;
  const game = simMl.createMLGame(playerCount);

  const laneAssignments = room.players.map(sid => ({
    laneIndex: room.laneBySocketId.get(sid),
    displayName: room.playerNames.get(sid) || "Player",
  }));

  io.to(roomId).emit("ml_match_ready", { code, playerCount, laneAssignments });
  io.to(roomId).emit("ml_match_config", simMl.createMLPublicConfig());

  const eliminatedNotified = new Set();
  let localTick = 0;
  const snapshotEveryNTicks = 2;

  const tickHandle = setInterval(() => {
    const entry = gamesByRoomId.get(roomId);
    if (!entry) return;

    simMl.mlTick(entry.game);
    localTick++;

    // Check for newly eliminated lanes
    for (const lane of entry.game.lanes) {
      if (lane.eliminated && !entry.eliminatedNotified.has(lane.laneIndex)) {
        entry.eliminatedNotified.add(lane.laneIndex);
        // Find which socket owns this lane
        const eliminatedSid = room.players.find(sid => room.laneBySocketId.get(sid) === lane.laneIndex);
        const displayName = room.playerNames.get(eliminatedSid) || "Player";
        io.to(roomId).emit("ml_player_eliminated", { laneIndex: lane.laneIndex, displayName });
        if (eliminatedSid) {
          io.to(eliminatedSid).emit("ml_spectator_join", { laneIndex: lane.laneIndex });
        }
      }
    }

    if (localTick % snapshotEveryNTicks === 0) {
      io.to(roomId).emit("ml_state_snapshot", simMl.createMLSnapshot(entry.game));
    }

    if (entry.game.phase === "ended") {
      const winnerLane = entry.game.winner;
      let winnerName = "Unknown";
      if (winnerLane !== null && winnerLane !== undefined) {
        const winnerSid = room.players.find(sid => room.laneBySocketId.get(sid) === winnerLane);
        winnerName = room.playerNames.get(winnerSid) || "Player";
      }
      io.to(roomId).emit("ml_game_over", { winnerLaneIndex: winnerLane, winnerName });
      stopMLGame(roomId, code);
    }
  }, simMl.TICK_MS);

  gamesByRoomId.set(roomId, { game, tickHandle, mode: "multilane", snapshotEveryNTicks, eliminatedNotified });
  console.log(`[ml-game] started ${roomId}`);
}

function stopMLGame(roomId, code) {
  const entry = gamesByRoomId.get(roomId);
  if (!entry) return;
  clearInterval(entry.tickHandle);
  gamesByRoomId.delete(roomId);
  console.log(`[ml-game] stopped ${roomId}`);
  setTimeout(() => {
    mlRoomsByCode.delete(code);
    console.log(`[ml-room] cleaned up ${code}`);
  }, 60_000);
}

// ── Socket.IO connection ─────────────────────────────────────────────────────

io.on("connection", (socket) => {
  console.log("connected:", socket.id);

  socket.on("create_room", () => {
    // fix #4: rate-limit lobby events
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const { code, roomId } = createRoom();
    const room = roomsByCode.get(code);

    room.players.push(socket.id);
    room.sidesBySocketId.set(socket.id, "bottom");
    sessionBySocketId.set(socket.id, { code, roomId, side: "bottom" });
    socket.join(roomId);

    socket.emit("room_created", { code, side: "bottom" });
  });

  socket.on("join_room", ({ code }) => {
    // fix #4: rate-limit lobby events
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const normalized = String(code || "").trim().toUpperCase();
    // fix #6: exact length check before map lookup
    if (normalized.length !== 6) return socket.emit("error_message", { message: "Invalid room code." });
    const room = roomsByCode.get(normalized);

    if (!room) return socket.emit("error_message", { message: "Room not found." });
    if (room.players.length >= 2) return socket.emit("error_message", { message: "Room is full." });

    room.players.push(socket.id);
    room.sidesBySocketId.set(socket.id, "top");
    sessionBySocketId.set(socket.id, { code: normalized, roomId: room.roomId, side: "top" });
    socket.join(room.roomId);

    socket.emit("room_joined", { code: normalized, side: "top" });
    io.to(room.roomId).emit("match_ready", { code: normalized });

    // Start match sim once both players are in
    startGame(room.roomId);
    io.to(room.roomId).emit("match_config", sim.createPublicConfig());
  });

  // ── Multi-Lane lobby events ────────────────────────────────────────────────

  socket.on("create_ml_room", ({ displayName } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const { code, roomId } = createMLRoom(socket.id, displayName);
    sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
    socket.join(roomId);
    socket.emit("ml_room_created", { code, laneIndex: 0, displayName: String(displayName || "Player").slice(0, 20) });
    const room = mlRoomsByCode.get(code);
    io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));
  });

  socket.on("join_ml_room", ({ code, displayName } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const normalized = String(code || "").trim().toUpperCase();
    if (normalized.length !== 6) return socket.emit("error_message", { message: "Invalid room code." });
    const room = mlRoomsByCode.get(normalized);
    if (!room) return socket.emit("error_message", { message: "Room not found." });
    if (room.players.length >= 4) return socket.emit("error_message", { message: "Room is full (max 4)." });
    if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });

    const laneIndex = room.players.length;
    room.players.push(socket.id);
    room.laneBySocketId.set(socket.id, laneIndex);
    room.playerNames.set(socket.id, String(displayName || "Player").slice(0, 20));

    sessionBySocketId.set(socket.id, { code: normalized, roomId: room.roomId, laneIndex, mode: "multilane" });
    socket.join(room.roomId);

    socket.emit("ml_room_joined", { code: normalized, laneIndex, displayName: room.playerNames.get(socket.id) });
    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(normalized, room));
  });

  socket.on("ml_player_ready", ({ code } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const normalized = String(code || session.code || "").trim().toUpperCase();
    const room = mlRoomsByCode.get(normalized);
    if (!room || !room.players.includes(socket.id)) return;

    room.readySet.add(socket.id);
    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(normalized, room));

    // Auto-start when all players are ready (minimum 2)
    if (room.players.length >= 2 && room.readySet.size === room.players.length) {
      startMLGame(room.roomId, normalized, io);
    }
  });

  socket.on("ml_force_start", ({ code } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const normalized = String(code || session.code || "").trim().toUpperCase();
    const room = mlRoomsByCode.get(normalized);
    if (!room) return;
    if (room.hostSocketId !== socket.id) {
      return socket.emit("error_message", { message: "Only the host can force start." });
    }
    if (room.players.length < 2) {
      return socket.emit("error_message", { message: "Need at least 2 players." });
    }
    if (!gamesByRoomId.has(room.roomId)) {
      startMLGame(room.roomId, normalized, io);
    }
  });

  // ── Player actions (classic path below; ML path branches here) ─────────────

  socket.on("player_action", ({ code, side, type, data }) => {
    // fix #4: rate-limit player actions
    if (!checkActionRateLimit(socket.id)) {
      socket.emit("error_message", { message: "Action rate limit exceeded." });
      return;
    }
    const session = sessionBySocketId.get(socket.id);
    if (!session) {
      socket.emit("error_message", { message: "No session for socket" });
      return;
    }

    // ── Multi-lane branch ──
    if (session.mode === "multilane") {
      const entry = gamesByRoomId.get(session.roomId);
      if (!entry) return socket.emit("error_message", { message: "Game not started" });

      const lane = entry.game.lanes[session.laneIndex];
      if (!lane) return socket.emit("error_message", { message: "Lane not found" });
      if (lane.eliminated) return socket.emit("error_message", { message: "You have been eliminated." });

      const res = simMl.applyMLAction(entry.game, session.laneIndex, { type, data });
      if (!res.ok) return socket.emit("error_message", { message: res.reason || "Action rejected" });

      socket.emit("action_applied", {
        type,
        laneIndex: session.laneIndex,
        tick: entry.game.tick,
        gold: lane.gold,
        income: lane.income,
      });
      return;
    }

    const room = roomsByCode.get(session.code);
    if (!room) {
      socket.emit("error_message", { message: "Room not found for action" });
      return;
    }
    if (!room.players.includes(socket.id)) {
      socket.emit("error_message", { message: "You are not in this room" });
      return;
    }

    const entry = gamesByRoomId.get(session.roomId);
    if (!entry) {
      socket.emit("error_message", { message: "Game not started" });
      return;
    }

    const expectedSide = session.side || room.sidesBySocketId.get(socket.id);
    if (!expectedSide) {
      socket.emit("error_message", { message: "Side assignment missing" });
      return;
    }

    // Server-authoritative side: ignore client-provided side except for diagnostics.
    if (side && side !== expectedSide) {
      socket.emit("error_message", { message: "Side mismatch corrected by server" });
    }

    if (!ALLOWED_ACTION_TYPES.has(type)) {
      socket.emit("error_message", { message: "Action not allowed" });
      return;
    }

    const res = sim.applyAction(entry.game, expectedSide, { type, data });
    if (!res.ok) {
      socket.emit("error_message", { message: res.reason || "Action rejected" });
      return;
    }

    socket.emit("action_applied", {
      type,
      side: expectedSide,
      tick: entry.game.tick,
      units: entry.game.units.length,
      gold: entry.game.players[expectedSide].gold,
      income: entry.game.players[expectedSide].income,
    });
  });

  socket.on("disconnect", () => {
    rateLimitBySocketId.delete(socket.id);
    sessionBySocketId.delete(socket.id);

    // Classic room cleanup
    for (const [code, room] of roomsByCode.entries()) {
      const idx = room.players.indexOf(socket.id);
      if (idx !== -1) {
        room.players.splice(idx, 1);
        room.sidesBySocketId.delete(socket.id);
        io.to(room.roomId).emit("player_left", { code });
        if (room.players.length === 0) {
          stopGame(room.roomId);
          roomsByCode.delete(code);
        }
        break;
      }
    }

    // ML room cleanup
    for (const [code, room] of mlRoomsByCode.entries()) {
      const idx = room.players.indexOf(socket.id);
      if (idx !== -1) {
        room.players.splice(idx, 1);
        room.laneBySocketId.delete(socket.id);
        room.playerNames.delete(socket.id);
        room.readySet.delete(socket.id);
        io.to(room.roomId).emit("player_left", { code });
        if (room.players.length === 0) {
          stopMLGame(room.roomId, code);
          mlRoomsByCode.delete(code);
        }
        break;
      }
    }
  });
});

app.get("/", (_req, res) => res.send("Castle Defender PvP server running"));

const PORT = process.env.PORT || 3000;
const HOST = process.env.HOST || '0.0.0.0'; // fix #3: bind to all interfaces; override via HOST env var
server.listen(PORT, HOST, () => {
  console.log(`Server listening on http://${HOST}:${PORT}`);
});
