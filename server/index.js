// server/index.js
"use strict";

require('dotenv').config(); // loads .env in development (no-op if file absent)

const express = require("express");
const http = require("http");
const cors = require("cors");
const fs = require("fs");
const path = require("path");
const { Server } = require("socket.io");

const crypto = require("crypto");

const sim = require("./sim");
const simMl = require("./sim-multilane");
const aiRuntime = require("../ai/sim_runner");
const authService = require("./auth");
const matchmaker = require("./services/matchmaker");
const log = require("./logger");
const db = process.env.DATABASE_URL ? require("./db") : null;
const ratingService = process.env.DATABASE_URL ? require("./services/rating") : null;
const seasonService = process.env.DATABASE_URL ? require("./services/season") : null;
const gameConfig    = require("./gameConfig");
if (db) {
  gameConfig.loadActiveConfigs(db).catch(err =>
    log.warn('game config load failed, using hardcoded defaults', { err: err.message })
  );
}

const app = express();
// H-4: restrict CORS to known origin; wildcard is unsafe for auth endpoints
app.use(cors({
  origin: process.env.ALLOWED_ORIGIN || 'http://localhost:3000',
  methods: ['GET', 'POST', 'PATCH'],
  credentials: true,
}));
app.use(express.json());
// Minimal cookie parser — populates req.cookies without adding a dependency
app.use((req, _res, next) => {
  const cookies = {};
  for (const pair of String(req.headers.cookie || '').split(';')) {
    const eq = pair.indexOf('=');
    if (eq === -1) continue;
    const k = pair.slice(0, eq).trim();
    const v = pair.slice(eq + 1).trim();
    try { cookies[k] = decodeURIComponent(v); } catch { cookies[k] = v; }
  }
  req.cookies = cookies;
  next();
});

// ── Security headers ──────────────────────────────────────────────────────────
app.use((_req, res, next) => {
  res.setHeader('X-Frame-Options', 'SAMEORIGIN');
  res.setHeader('X-Content-Type-Options', 'nosniff');
  res.setHeader('X-XSS-Protection', '1; mode=block');
  res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
  // HSTS: always set; shorter max-age in non-production
  const hstsTtl = process.env.NODE_ENV === 'production' ? 31536000 : 3600;
  res.setHeader('Strict-Transport-Security', `max-age=${hstsTtl}; includeSubDomains; preload`);
  res.setHeader(
    'Content-Security-Policy',
    "default-src 'self'; " +
    "script-src 'self' https://accounts.google.com https://cdn.socket.io; " +
    "connect-src 'self' wss: ws: https://accounts.google.com https://cdn.socket.io; " +
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://accounts.google.com; " +
    "font-src 'self' https://fonts.gstatic.com; " +
    "img-src 'self' data: https:; " +
    "frame-src https://accounts.google.com; " +
    "frame-ancestors 'self';"
  );
  next();
});

// ── Auth + player routes ──────────────────────────────────────────────────────
app.use("/auth",    require("./routes/auth"));
app.use("/players", require("./routes/players"));
app.use("/parties", require("./routes/parties"));
app.use("/queue",   require("./routes/queue"));
app.use("/matches",     require("./routes/matches"));
app.use("/leaderboard", require("./routes/leaderboard"));
app.use("/admin",       require("./routes/admin"));

// Public config for client (Google Client ID etc.)
app.get("/config", (_req, res) => {
  res.json({
    googleClientId:      process.env.GOOGLE_CLIENT_ID || "",
    passwordAuthEnabled: !!db,  // true whenever DB is connected
  });
});

// Serve web app client at both / and /client for production compatibility.
// Support both monorepo root deploys and server-subdir deploys.
const clientDirCandidates = [
  path.join(__dirname, "..", "client"),
  path.join(__dirname, "client"),
];
const clientDir = clientDirCandidates.find((p) => fs.existsSync(path.join(p, "index.html"))) || clientDirCandidates[0];
log.info('static dir resolved', { clientDir });
app.use(express.static(clientDir));
app.use("/client", express.static(clientDir));

const server = http.createServer(app);
const io = new Server(server, {
  maxHttpBufferSize: 1e4, // fix #8: cap incoming message size at 10 KB
  cors: {
    origin: process.env.ALLOWED_ORIGIN || 'http://localhost:3000',
    methods: ["GET", "POST"],
    credentials: true,
  },
});

// Admin: expose io for live-stats route
app.locals.io = io;

// Admin: force-terminate an active match (called by POST /admin/matches/:id/terminate)
app.locals.terminateMatch = async function terminateMatch(roomId) {
  const entry = gamesByRoomId.get(roomId);
  if (!entry) return false;
  clearInterval(entry.tickHandle);
  gamesByRoomId.delete(roomId);
  // Notify all sockets in the room
  io.to(roomId).emit('game_over',    { winner: null, reason: 'admin_terminated' });
  io.to(roomId).emit('ml_game_over', { winnerLaneIndex: null, winnerName: null, reason: 'admin_terminated' });
  // Mark abandoned in DB
  if (db && entry.matchIdPromise) {
    const matchId = await Promise.resolve(entry.matchIdPromise).catch(() => null);
    if (matchId) {
      await db.query(
        `UPDATE matches SET status='abandoned', ended_at=NOW() WHERE id=$1`, [matchId]
      ).catch(() => {});
    }
  }
  log.info('match terminated by admin', { roomId });
  return true;
};

// ── Cookie parsing helper ─────────────────────────────────────────────────────
function parseCookies(cookieStr) {
  const out = {};
  for (const pair of String(cookieStr || '').split(';')) {
    const eq = pair.indexOf('=');
    if (eq === -1) continue;
    const k = pair.slice(0, eq).trim();
    const v = pair.slice(eq + 1).trim();
    try { out[k] = decodeURIComponent(v); } catch { out[k] = v; }
  }
  return out;
}

// ── Socket.IO auth middleware ─────────────────────────────────────────────────
// Auth is optional — guests without a token continue as before.
// Accepts token from handshake.auth.token (legacy) or cd_access cookie.
// Authenticated players get socket.playerId / socket.playerDisplayName attached.
// Banned players get socket.playerBanned = true; requireAuthSocket checks this.
io.use(async (socket, next) => {
  const cookies = parseCookies(socket.handshake.headers?.cookie);
  const token = socket.handshake.auth?.token || cookies['cd_access'] || null;
  if (token) {
    try {
      const payload = authService.verifyAccessToken(token);
      socket.playerId          = payload.sub;
      socket.playerDisplayName = payload.displayName;
      // Check ban status (non-blocking; treat DB errors as not-banned)
      if (db) {
        try {
          const r = await db.query(`SELECT status FROM players WHERE id = $1`, [payload.sub]);
          if (r.rows[0]?.status === 'suspended') socket.playerBanned = true;
        } catch { /* ignore */ }
      }
    } catch { /* invalid/expired — treat as guest */ }
  }
  next();
});

const ALLOWED_ACTION_TYPES = new Set([
  "spawn_unit",
  "build_tower",
  "upgrade_tower",
  "sell_tower",
  "upgrade_barracks",
  "set_autosend",
  "set_tower_target",
]);

const DEFAULT_MATCH_SETTINGS = Object.freeze({
  startIncome: 10,
});

// code -> { roomId, players: [socketId], sidesBySocketId: Map<socketId, side>, createdAt }
const roomsByCode = new Map();
// code -> { roomId, players:[socketId], laneBySocketId:Map, readySet:Set, playerNames:Map, createdAt, mode:"multilane", hostSocketId }
const mlRoomsByCode = new Map();
// socketId -> { code, roomId, side } | { code, roomId, laneIndex, mode:"multilane" }
const sessionBySocketId = new Map();

// roomId -> { game, tickHandle, snapshotEveryNTicks }
const gamesByRoomId = new Map();

// ── Competitive in-memory state ───────────────────────────────────────────────
// partyId -> PartyObject
const partiesById = new Map();
// playerId -> partyId
const partyByPlayerId = new Map();
// playerId -> socketId  (only authenticated players)
const socketByPlayerId = new Map();
// L-4: O(1) party code lookup — kept in sync with partiesById
const partiesByCode = new Map();

// ── Reconnect state ───────────────────────────────────────────────────────────
const RECONNECT_GRACE_MS = 120_000; // 2 minutes grace before forfeit
// `${roomId}:${seatKey}` -> { graceHandle, expiresAt, playerId, guestId, playerName, code, mode, seatKey, prevSocketId }
const disconnectGrace = new Map();

// Expose via app.locals so REST routes can read them
app.locals.partiesById     = partiesById;
app.locals.partyByPlayerId = partyByPlayerId;
// Admin dashboard live-state access
app.locals.gamesByRoomId   = gamesByRoomId;
app.locals.roomsByCode     = roomsByCode;
app.locals.mlRoomsByCode   = mlRoomsByCode;
app.locals.socketByPlayerId = socketByPlayerId;

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
  return r.actionCount <= 10; // max 10 player_actions per second
}

function generateCode(len = 6) {
  // H-5: use CSPRNG instead of Math.random() to prevent code enumeration
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  const bytes = crypto.randomBytes(len);
  return Array.from(bytes, b => chars[b % chars.length]).join('');
}

// ── Reconnect token helpers ───────────────────────────────────────────────────
const jwt = require("jsonwebtoken");
const RECONNECT_JWT_SECRET = process.env.JWT_SECRET; // required — server/auth.js throws at startup if unset

function issueReconnectToken(roomId, code, mode, seatKey, socket) {
  return jwt.sign(
    { type: "reconnect", roomId, code, mode, seatKey,
      playerId: socket.playerId || null,
      guestId:  socket.guestId  || null },
    RECONNECT_JWT_SECRET,
    { algorithm: 'HS256', expiresIn: "24h" }
  );
}

function verifyReconnectToken(token) {
  // C-1: pin algorithm to prevent confusion attacks
  return jwt.verify(token, RECONNECT_JWT_SECRET, { algorithms: ['HS256'] });
}

function sanitizeDisplayName(raw) {
  return (String(raw || '').replace(/[^a-zA-Z0-9_ ]/g, '').trim().slice(0, 20)) || 'Player';
}

function normalizeMatchSettings(settings) {
  const src = settings && typeof settings === "object" ? settings : {};
  const rawIncome = Number(src.startIncome);
  const startIncome = Number.isFinite(rawIncome) ? Math.max(0, Math.min(1000, rawIncome)) : DEFAULT_MATCH_SETTINGS.startIncome;
  return { startIncome };
}

// ── Match logging helpers ────────────────────────────────────────────────────

async function logMatchStart(roomId, mode) {
  if (!db) return null;
  try {
    const r = await db.query(
      `INSERT INTO matches (room_id, mode) VALUES ($1, $2) RETURNING id`,
      [roomId, mode]
    );
    return r.rows[0].id;
  } catch (err) {
    log.error('[match] insert failed:', { err: err.message });
    return null;
  }
}

// playerSnapshots: [{ playerId, laneIndex, result }]
async function logMatchEnd(matchIdPromise, winnerLane, playerSnapshots) {
  if (!db) return;
  const matchId = await matchIdPromise;
  if (!matchId) return;
  try {
    await db.query(
      `UPDATE matches SET status='completed', ended_at=NOW(), winner_lane=$1 WHERE id=$2`,
      [winnerLane ?? null, matchId]
    );
    for (const { playerId, laneIndex, result } of playerSnapshots) {
      await db.query(
        `INSERT INTO match_players (match_id, player_id, lane_index, result) VALUES ($1, $2, $3, $4)`,
        [matchId, playerId, laneIndex, result]
      );
    }
  } catch (err) {
    log.error('[match] close failed:', { err: err.message });
  }
}

function createRoom(settings) {
  let code;
  do code = generateCode(6);
  while (roomsByCode.has(code));
  const roomId = `room_${code}`;
  roomsByCode.set(code, {
    roomId,
    players: [],
    sidesBySocketId: new Map(),
    createdAt: Date.now(),
    settings: normalizeMatchSettings(settings),
  });
  return { code, roomId };
}

function startGame(roomId, settings) {
  if (gamesByRoomId.has(roomId)) return;

  const game = sim.createGame(settings);
  const snapshotEveryNTicks = 2; // 20hz tick, 10hz snapshots

  // Snapshot authenticated players for DB logging (captured at start, before room cleanup)
  const playerSnapshot = [];
  for (const [, room] of roomsByCode.entries()) {
    if (room.roomId !== roomId) continue;
    for (const [sid, side] of room.sidesBySocketId.entries()) {
      const sock = io.sockets.sockets.get(sid);
      if (sock?.playerId) {
        playerSnapshot.push({ playerId: sock.playerId, laneIndex: side === "bottom" ? 0 : 1 });
      }
    }
    break;
  }
  const matchIdPromise = logMatchStart(roomId, "classic");

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
      const winner = entry.game.winner;
      io.to(roomId).emit("game_over", { winner });
      const winnerLane = winner === "bottom" ? 0 : winner === "top" ? 1 : null;
      const snapshots = entry.playerSnapshot.map(p => ({
        ...p,
        result: winnerLane === null ? "draw" : p.laneIndex === winnerLane ? "win" : "loss",
      }));
      logMatchEnd(entry.matchIdPromise, winnerLane, snapshots);
      stopGame(roomId);
    }
  }, sim.TICK_MS);

  gamesByRoomId.set(roomId, { game, tickHandle, snapshotEveryNTicks, matchIdPromise, playerSnapshot });

  // Issue reconnect tokens to human players
  for (const [rCode, room] of roomsByCode.entries()) {
    if (room.roomId !== roomId) continue;
    for (const [sid, side] of room.sidesBySocketId.entries()) {
      const sock = io.sockets.sockets.get(sid);
      if (!sock) continue;
      const rToken = issueReconnectToken(roomId, rCode, "classic", side, sock);
      sock.emit("reconnect_token", { token: rToken, gracePeriodMs: RECONNECT_GRACE_MS });
    }
    break;
  }

  log.info('game started', { roomId });
}

function stopGame(roomId) {
  const entry = gamesByRoomId.get(roomId);
  if (!entry) return;
  clearInterval(entry.tickHandle);
  gamesByRoomId.delete(roomId);
  log.info('game stopped', { roomId });

  // fix #5: schedule room cleanup 60 s after game ends to prevent indefinite memory leak
  const handle = setTimeout(() => {
    for (const [code, room] of roomsByCode.entries()) {
      if (room.roomId === roomId) {
        roomsByCode.delete(code);
        log.info('room cleaned up', { code, reason: 'post-game timeout' });
        break;
      }
    }
  }, 60_000);
  for (const [, room] of roomsByCode.entries()) {
    if (room.roomId === roomId) { room._cleanupHandle = handle; break; }
  }
}

// ── Multi-Lane helpers ───────────────────────────────────────────────────────

function createMLRoom(hostSocketId, displayName, settings) {
  let code;
  do code = generateCode(6);
  while (roomsByCode.has(code) || mlRoomsByCode.has(code));
  const roomId = `mlroom_${code}`;
  const room = {
    roomId,
    players: [hostSocketId],
    laneBySocketId: new Map([[hostSocketId, 0]]),
    playerTeamsBySocketId: new Map([[hostSocketId, "red"]]),
    readySet: new Set(),
    playerNames: new Map([[hostSocketId, sanitizeDisplayName(displayName)]]),
    createdAt: Date.now(),
    mode: "multilane",
    hostSocketId,
    aiPlayers: [], // [{ laneIndex, difficulty, team }]
    settings: normalizeMatchSettings(settings),
  };
  mlRoomsByCode.set(code, room);
  return { code, roomId };
}

function countMlTeams(room) {
  const counts = { red: 0, blue: 0 };
  for (const sid of room.players || []) {
    const t = room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red";
    counts[t] += 1;
  }
  for (const ai of room.aiPlayers || []) {
    const t = ai.team === "blue" ? "blue" : "red";
    counts[t] += 1;
  }
  return counts;
}

function pickBalancedMlTeam(room) {
  const counts = countMlTeams(room);
  return counts.red <= counts.blue ? "red" : "blue";
}

function validateMlTeamSetup(room) {
  const total = (room.players?.length || 0) + (room.aiPlayers?.length || 0);
  if (total !== 4) return { ok: true };
  const counts = countMlTeams(room);
  if (counts.red !== 2 || counts.blue !== 2) {
    return { ok: false, reason: "For 4-player matches, assign exactly 2 Red and 2 Blue before starting." };
  }
  return { ok: true };
}

function mlLobbyUpdatePayload(code, room) {
  const humanPlayers = room.players.map(sid => ({
    laneIndex: room.laneBySocketId.get(sid),
    displayName: room.playerNames.get(sid) || "Player",
    ready: room.readySet.has(sid),
    isAI: false,
    team: room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red",
  }));
  const aiPlayers = (room.aiPlayers || []).map(ai => ({
    laneIndex: ai.laneIndex,
    displayName: "CPU (" + capFirst(ai.difficulty) + ")",
    ready: true,
    isAI: true,
    difficulty: ai.difficulty,
    team: ai.team === "blue" ? "blue" : "red",
  }));
  const players = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);
  return { code, players, hostLaneIndex: 0, settings: normalizeMatchSettings(room.settings) };
}

function capFirst(s) {
  return String(s || "").charAt(0).toUpperCase() + String(s || "").slice(1);
}

function buildMlMatchSeed(roomId, code, laneAssignments) {
  const parts = laneAssignments
    .slice()
    .sort((a, b) => a.laneIndex - b.laneIndex)
    .map((a) => `${a.laneIndex}:${a.team}:${a.isAI ? "ai" : "human"}`);
  return `${roomId}:${code}:${parts.join("|")}`;
}

function resolveDefaultMultilaneTarget(game, sourceLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || game.lanes.length <= 1) return null;
  const sourceLane = game.lanes[sourceLaneIndex];
  if (!sourceLane) return null;
  const total = Math.min(Number(game.playerCount) || game.lanes.length, game.lanes.length);
  for (let step = 1; step < total; step++) {
    const idx = (sourceLaneIndex + step) % total;
    const lane = game.lanes[idx];
    if (!lane || lane.eliminated) continue;
    if (lane.team === sourceLane.team) continue;
    return idx;
  }
  return null;
}

function applyMultilaneAction(entry, laneIndex, action) {
  const res = simMl.applyMLAction(entry.game, laneIndex, action);
  const tracker = entry.botController && entry.botController.runtimeTracker;
  if (tracker) {
    if (!res.ok) {
      tracker.recordInvalidAction(laneIndex);
    } else if (action && action.type === "spawn_unit") {
      const requestedTarget = Number(action.data && action.data.targetLaneIndex);
      const targetLaneIndex = Number.isInteger(requestedTarget)
        ? requestedTarget
        : resolveDefaultMultilaneTarget(entry.game, laneIndex);
      tracker.recordSendEvent(
        laneIndex,
        targetLaneIndex,
        action.data && action.data.unitType,
        1,
        1,
        entry.game.tick
      );
    }
  }
  return res;
}

function startMLGame(roomId, code, io) {
  if (gamesByRoomId.has(roomId)) return;
  const room = mlRoomsByCode.get(code);
  if (!room) return;

  const humanCount = room.players.length;
  const aiList = room.aiPlayers || [];
  const playerCount = humanCount + aiList.length;
  const laneAssignments = [
    ...room.players.map(sid => ({
      laneIndex: room.laneBySocketId.get(sid),
      displayName: room.playerNames.get(sid) || "Player",
      isAI: false,
      team: room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red",
    })),
    ...aiList.map(ai => ({
      laneIndex: ai.laneIndex,
      displayName: "CPU (" + capFirst(ai.difficulty) + ")",
      isAI: true,
      team: ai.team === "blue" ? "blue" : "red",
    })),
  ];
  const laneTeams = new Array(playerCount).fill("red");
  for (const a of laneAssignments) {
    if (Number.isInteger(a.laneIndex) && a.laneIndex >= 0 && a.laneIndex < playerCount) {
      laneTeams[a.laneIndex] = a.team === "blue" ? "blue" : "red";
    }
  }
  const game = simMl.createMLGame(playerCount, { ...room.settings, laneTeams });

  io.to(roomId).emit("ml_match_ready", { code, playerCount, laneAssignments });
  io.to(roomId).emit("ml_match_config", simMl.createMLPublicConfig(room.settings));
  const botController = aiList.length > 0
    ? aiRuntime.createBotController({
      game,
      seed: buildMlMatchSeed(roomId, code, laneAssignments),
      tickMs: simMl.TICK_MS,
      botTickMs: 500,
      runtimeOptions: { waveTickInterval: simMl.INCOME_INTERVAL_TICKS || 240 },
      botConfigs: aiList.map((ai) => ({
        laneIndex: ai.laneIndex,
        difficulty: ai.difficulty,
      })),
    })
    : null;

  // Snapshot authenticated players for DB logging
  const playerSnapshot = room.players
    .map(sid => {
      const sock = io.sockets.sockets.get(sid);
      return sock?.playerId
        ? { playerId: sock.playerId, laneIndex: room.laneBySocketId.get(sid) }
        : null;
    })
    .filter(Boolean);
  const dbMode = room.queueMode === 'ranked_2v2' ? '2v2_ranked' : 'multilane';
  const matchIdPromise = logMatchStart(roomId, dbMode);

  const eliminatedNotified = new Set();
  let localTick = 0;
  const snapshotEveryNTicks = 2;

  const tickHandle = setInterval(() => {
    const entry = gamesByRoomId.get(roomId);
    if (!entry) return;

    const prevLite = entry.botController ? aiRuntime.captureStateLite(entry.game) : null;
    if (entry.botController) entry.botController.onBeforeSimTick(entry.game);
    simMl.mlTick(entry.game);
    if (entry.botController && prevLite) {
      const nextLite = aiRuntime.captureStateLite(entry.game);
      entry.botController.onAfterSimTick(prevLite, nextLite);
    }
    localTick++;

    // Check for newly eliminated lanes
    for (const lane of entry.game.lanes) {
      if (lane.eliminated && !entry.eliminatedNotified.has(lane.laneIndex)) {
        entry.eliminatedNotified.add(lane.laneIndex);
        // Find which socket or AI owns this lane
        const eliminatedSid = room.players.find(sid => room.laneBySocketId.get(sid) === lane.laneIndex);
        const aiEntry = (room.aiPlayers || []).find(ai => ai.laneIndex === lane.laneIndex);
        const displayName = eliminatedSid
          ? (room.playerNames.get(eliminatedSid) || "Player")
          : (aiEntry ? "CPU (" + capFirst(aiEntry.difficulty) + ")" : "Player");
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
        if (winnerSid) {
          winnerName = room.playerNames.get(winnerSid) || "Player";
        } else {
          const winnerAi = (room.aiPlayers || []).find(ai => ai.laneIndex === winnerLane);
          if (winnerAi) winnerName = "CPU (" + capFirst(winnerAi.difficulty) + ")";
        }
      }
      io.to(roomId).emit("ml_game_over", { winnerLaneIndex: winnerLane, winnerName });
      const snapshots = entry.playerSnapshot.map(p => ({
        ...p,
        result: winnerLane === null
          ? "draw"
          : entry.game.lanes[p.laneIndex]?.team === entry.game.lanes[winnerLane]?.team
            ? "win"
            : "loss",
      }));
      const endP = logMatchEnd(entry.matchIdPromise, winnerLane, snapshots);
      if (ratingService && db && entry.dbMode === '2v2_ranked') {
        Promise.all([endP, entry.matchIdPromise])
          .then(([, matchId]) => ratingService.updateRatings(db, matchId, entry.dbMode, snapshots, entry.partyASize))
          .then(updates => {
            for (const u of updates) {
              const sid = socketByPlayerId.get(u.playerId);
              if (sid) {
                const totalMatches = (u.wins || 0) + (u.losses || 0);
                const isPlacement  = totalMatches < 10;
                io.to(sid).emit('rating_update', {
                  mode: entry.dbMode,
                  newRating: Math.round(u.newRating),
                  delta: Math.round(u.delta),
                  isPlacement,
                  placementProgress: isPlacement ? `${totalMatches}/10` : null,
                });
              }
            }
            // Update season peak ratings (fire-and-forget)
            if (seasonService && updates.length > 0) {
              seasonService.getActiveSeason(db).then(season => {
                if (!season) return;
                for (const u of updates) {
                  seasonService.updatePeakRating(db, season.id, u.playerId, entry.dbMode, u.newRating);
                }
              }).catch(() => {});
            }
          })
          .catch(err => log.error('[rating] error:', { err: err.message }));
      }
      stopMLGame(roomId, code);
    }
  }, simMl.TICK_MS);

  gamesByRoomId.set(roomId, {
    game,
    tickHandle,
    mode: "multilane",
    snapshotEveryNTicks,
    eliminatedNotified,
    botController,
    matchIdPromise,
    playerSnapshot,
    dbMode,
    partyASize: room.partyASize || 1,
  });

  // Issue reconnect tokens to human players
  for (const sid of room.players) {
    const sock = io.sockets.sockets.get(sid);
    if (!sock) continue;
    const laneIdx = room.laneBySocketId.get(sid);
    const rToken = issueReconnectToken(roomId, code, "multilane", String(laneIdx), sock);
    sock.emit("reconnect_token", { token: rToken, gracePeriodMs: RECONNECT_GRACE_MS });
  }

  log.info(`[ml-game] started ${roomId}`);
}

function stopMLGame(roomId, code) {
  const entry = gamesByRoomId.get(roomId);
  if (!entry) return;
  clearInterval(entry.tickHandle);
  gamesByRoomId.delete(roomId);
  log.info(`[ml-game] stopped ${roomId}`);
  const handle = setTimeout(() => {
    mlRoomsByCode.delete(code);
    log.info(`[ml-room] cleaned up ${code}`);
  }, 60_000);
  const room = mlRoomsByCode.get(code);
  if (room) room._cleanupHandle = handle;
}

// ── Friends helpers ────────────────────────────────────────────────────────────

/**
 * Returns the friends list for a player as:
 * [{ playerId, displayName, status: 'accepted'|'pending_sent'|'pending_received', online: bool }]
 */
async function getFriendsList(playerId) {
  if (!db) return [];
  const { rows } = await db.query(
    `SELECT
       CASE WHEN f.requester_id = $1 THEN f.addressee_id ELSE f.requester_id END AS friend_id,
       CASE WHEN f.requester_id = $1 THEN pa.display_name ELSE pr.display_name END AS display_name,
       CASE
         WHEN f.status = 'accepted' THEN 'accepted'
         WHEN f.requester_id = $1   THEN 'pending_sent'
         ELSE 'pending_received'
       END AS status
     FROM friends f
     JOIN players pr ON pr.id = f.requester_id
     JOIN players pa ON pa.id = f.addressee_id
     WHERE f.requester_id = $1 OR f.addressee_id = $1`,
    [playerId]
  );
  return rows.map(r => ({
    playerId: r.friend_id,
    displayName: r.display_name,
    status: r.status,
    online: socketByPlayerId.has(r.friend_id),
  }));
}

/**
 * Push an updated friends_list to a player if they are online.
 */
async function pushFriendsList(playerId) {
  const sid = socketByPlayerId.get(playerId);
  if (!sid) return;
  const sock = io.sockets.sockets.get(sid);
  if (!sock) return;
  const friends = await getFriendsList(playerId);
  sock.emit('friends_list', friends);
}

// ── Party / queue business logic ──────────────────────────────────────────────

/**
 * Remove a player from their party. Disbands the party if it becomes empty.
 * Must be called with a valid partyId. Cleans up queue entry if needed.
 */
function _leaveParty(socket, partyId) {
  const party = partiesById.get(partyId);
  if (!party) return;

  party.members = party.members.filter(m => m.playerId !== socket.playerId);
  partyByPlayerId.delete(socket.playerId);
  socket.leave("party:" + partyId);

  if (party.members.length === 0) {
    // Disband — remove from both indexes (L-4)
    if (party.status === "queued") matchmaker.removeFromQueue(partyId);
    partiesById.delete(partyId);
    partiesByCode.delete(party.code);
    log.info(`[party] disbanded ${partyId}`);
    return;
  }

  // Promote a new leader if the leaver was leader (must be still connected)
  if (party.leaderId === socket.playerId) {
    const newLeader = party.members.find(m => socketByPlayerId.has(m.playerId));
    if (newLeader) {
      party.leaderId = newLeader.playerId;
    } else {
      // No connected member — disband
      if (party.status === "queued") matchmaker.removeFromQueue(partyId);
      partiesById.delete(partyId);
      partiesByCode.delete(party.code);
      log.info(`[party] disbanded ${partyId} (no connected leader candidate)`);
      return;
    }
  }

  // If queued and party is now empty enough, cancel queue
  if (party.status === "queued" && party.members.length === 0) {
    matchmaker.removeFromQueue(partyId);
    party.status = "idle";
    party.queueMode = null;
    party.queueEnteredAt = null;
  }

  io.to("party:" + partyId).emit("party_update", { party });
  if (party.status === "queued") {
    const elapsed = Math.floor((Date.now() - party.queueEnteredAt) / 1000);
    io.to("party:" + partyId).emit("queue_status", { status: "queued", mode: party.queueMode, elapsed });
  }
  log.info(`[party] ${socket.playerId} left ${partyId}`);
}

/**
 * Called by the matchmaker loop when two parties are matched.
 * Creates an ML room, auto-joins all members, and emits match_found.
 */
function onMatchFound(partyA, partyB, mode) {
  log.info(`[match] found: party ${partyA.id} vs party ${partyB.id} mode=${mode}`);

  // Create a 4-player ML room (2 per party)
  const allMembers = [...partyA.members, ...partyB.members];
  if (allMembers.length === 0) return;

  const hostSocketId = socketByPlayerId.get(allMembers[0].playerId) || allMembers[0].socketId;
  const hostDisplayName = sanitizeDisplayName(allMembers[0].displayName);

  const { code, roomId } = createMLRoom(hostSocketId, hostDisplayName, {});
  const room = mlRoomsByCode.get(code);
  room.queueMode  = mode;                       // 'ranked_2v2' | 'casual_2v2'
  room.partyASize = partyA.members.length;      // lane split point for rating team assignment

  // Set host session (createMLRoom does not call sessionBySocketId.set)
  sessionBySocketId.set(hostSocketId, { code, roomId, laneIndex: 0, mode: "multilane" });
  room.playerTeamsBySocketId.set(hostSocketId, "red");
  const hostSocket = io.sockets.sockets.get(hostSocketId);
  if (hostSocket) hostSocket.join(roomId);

  // Auto-join remaining members
  let laneIndex = 1;
  for (let i = 1; i < allMembers.length; i++) {
    const member = allMembers[i];
    const sid = socketByPlayerId.get(member.playerId) || member.socketId;
    if (!sid) { laneIndex++; continue; }

    room.players.push(sid);
    room.laneBySocketId.set(sid, laneIndex);
    room.playerNames.set(sid, sanitizeDisplayName(member.displayName));
    room.playerTeamsBySocketId.set(sid, i < partyA.members.length ? "red" : "blue");
    sessionBySocketId.set(sid, { code, roomId, laneIndex, mode: "multilane" });
    const memberSocket = io.sockets.sockets.get(sid);
    if (memberSocket) memberSocket.join(roomId);
    laneIndex++;
  }

  // Update hostSocketId session (createMLRoom sets laneIndex 0 already)
  // (already set by createMLRoom for hostSocketId)

  io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));

  // Emit match_found to each member
  const partyANames = partyA.members.map(m => m.displayName);
  const partyBNames = partyB.members.map(m => m.displayName);

  partyA.members.forEach((member, idx) => {
    const sid = socketByPlayerId.get(member.playerId) || member.socketId;
    if (!sid) return;
    io.to(sid).emit("match_found", {
      roomCode: code,
      laneIndex: idx,
      teammates: partyANames,
      opponents: partyBNames,
    });
  });

  partyB.members.forEach((member, idx) => {
    const sid = socketByPlayerId.get(member.playerId) || member.socketId;
    if (!sid) return;
    io.to(sid).emit("match_found", {
      roomCode: code,
      laneIndex: partyA.members.length + idx,
      teammates: partyBNames,
      opponents: partyANames,
    });
  });

  // Mark both parties as in_match
  partyA.status = "in_match";
  partyA.queueMode = null;
  partyA.queueEnteredAt = null;
  partyB.status = "in_match";
  partyB.queueMode = null;
  partyB.queueEnteredAt = null;

  io.to("party:" + partyA.id).emit("party_update", { party: partyA });
  io.to("party:" + partyB.id).emit("party_update", { party: partyB });
}

// ── Party helpers ─────────────────────────────────────────────────────────────

function generatePartyCode() {
  // H-5: reuses CSPRNG-based generateCode; L-4: uniqueness checked via partiesByCode
  return generateCode(6);
}

function requireAuthSocket(socket, cb) {
  if (!socket.playerId) {
    socket.emit("error_message", { message: "Sign in required." });
    return false;
  }
  if (socket.playerBanned) {
    socket.emit("error_message", { message: "Your account has been suspended." });
    return false;
  }
  return cb();
}

// ── Socket.IO connection ─────────────────────────────────────────────────────

io.on("connection", (socket) => {
  log.info('socket connected');

  // Assign a stable guest ID for reconnect token issuance (even auth players get one as fallback)
  socket.guestId = crypto.randomUUID();

  // Track authenticated sockets immediately on connect (token via handshake)
  if (socket.playerId) {
    socketByPlayerId.set(socket.playerId, socket.id);
    // Push friends list + notify accepted friends that this player is online
    if (db) {
      getFriendsList(socket.playerId).then(friends => {
        socket.emit('friends_list', friends);
        for (const f of friends) {
          if (f.status === 'accepted') {
            const fSid = socketByPlayerId.get(f.playerId);
            if (fSid) {
              const fSock = io.sockets.sockets.get(fSid);
              if (fSock) fSock.emit('friend_online', { playerId: socket.playerId, displayName: socket.playerDisplayName });
            }
          }
        }
      }).catch(() => {});
    }
  }

  // Allows clients to authenticate after the socket is already connected
  // (e.g. user signs in while lobby is open without reloading the page).
  socket.on("authenticate", ({ token } = {}) => {
    // H-2: rate-limit JWT verify calls to prevent event-loop DoS
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!token) return;
    try {
      const payload = authService.verifyAccessToken(token);
      socket.playerId          = payload.sub;
      socket.playerDisplayName = payload.displayName;
      socketByPlayerId.set(socket.playerId, socket.id);
      socket.emit("authenticated", { playerId: socket.playerId, displayName: socket.playerDisplayName });
      // Push friends list + notify accepted friends that this player is online
      if (db) {
        getFriendsList(socket.playerId).then(friends => {
          socket.emit('friends_list', friends);
          for (const f of friends) {
            if (f.status === 'accepted') {
              const fSid = socketByPlayerId.get(f.playerId);
              if (fSid) {
                const fSock = io.sockets.sockets.get(fSid);
                if (fSock) fSock.emit('friend_online', { playerId: socket.playerId, displayName: socket.playerDisplayName });
              }
            }
          }
        }).catch(() => {});
      }
    } catch {
      socket.emit("auth_error", { message: "Invalid or expired token" });
    }
  });

  // ── Reconnect ──────────────────────────────────────────────────────────────────

  socket.on("rejoin_game", ({ token } = {}) => {
    if (!token || typeof token !== "string") {
      return socket.emit("rejoin_fail", { reason: "no_token" });
    }

    let payload;
    try {
      payload = verifyReconnectToken(token);
    } catch {
      return socket.emit("rejoin_fail", { reason: "invalid_token" });
    }

    if (payload.type !== "reconnect") {
      return socket.emit("rejoin_fail", { reason: "wrong_type" });
    }

    const { roomId, code, mode, seatKey, playerId, guestId } = payload;

    // Auth players must match
    if (playerId && socket.playerId && socket.playerId !== playerId) {
      return socket.emit("rejoin_fail", { reason: "identity_mismatch" });
    }

    // Game must still be running
    const entry = gamesByRoomId.get(roomId);
    if (!entry) {
      return socket.emit("rejoin_fail", { reason: "game_over" });
    }

    const graceKey = `${roomId}:${seatKey}`;

    if (mode === "multilane") {
      const room = mlRoomsByCode.get(code);
      if (!room) return socket.emit("rejoin_fail", { reason: "room_gone" });

      const laneIndex = parseInt(seatKey, 10);

      // Remove stale socket holding this lane (if any)
      for (const [sid, li] of room.laneBySocketId.entries()) {
        if (li === laneIndex && sid !== socket.id) {
          if (io.sockets.sockets.has(sid)) {
            return socket.emit("rejoin_fail", { reason: "seat_taken" });
          }
          const staleTeam = room.playerTeamsBySocketId?.get(sid);
          room.players = room.players.filter(s => s !== sid);
          room.laneBySocketId.delete(sid);
          if (room.playerTeamsBySocketId) room.playerTeamsBySocketId.delete(sid);
          room.playerNames.delete(sid);
          room.readySet.delete(sid);
          sessionBySocketId.delete(sid);
          if (staleTeam && room.playerTeamsBySocketId) room.playerTeamsBySocketId.set(socket.id, staleTeam);
          break;
        }
      }

      const grace = disconnectGrace.get(graceKey);
      if (grace) { clearTimeout(grace.graceHandle); disconnectGrace.delete(graceKey); }

      const playerName = grace?.playerName || socket.playerDisplayName || "Player";

      if (!room.players.includes(socket.id)) room.players.push(socket.id);
      room.laneBySocketId.set(socket.id, laneIndex);
      if (room.playerTeamsBySocketId && !room.playerTeamsBySocketId.has(socket.id)) {
        room.playerTeamsBySocketId.set(socket.id, pickBalancedMlTeam(room));
      }
      room.playerNames.set(socket.id, playerName);
      socket.join(roomId);
      sessionBySocketId.set(socket.id, { code, roomId, laneIndex, mode: "multilane" });

      const humanPlayers = room.players.map(sid => ({
        laneIndex: room.laneBySocketId.get(sid),
        displayName: room.playerNames.get(sid) || "Player",
        ready: true, isAI: false,
        team: room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red",
      }));
      const aiPlayers = (room.aiPlayers || []).map(ai => ({
        laneIndex: ai.laneIndex,
        displayName: "CPU (" + capFirst(ai.difficulty) + ")",
        ready: true, isAI: true,
        team: ai.team === "blue" ? "blue" : "red",
      }));
      const laneAssignments = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);

      // Send ack first so client sets myLaneIndex before ml_match_ready fires
      socket.emit("rejoin_ack", { success: true, mode: "multilane", laneIndex, code });
      socket.emit("ml_match_ready", { code, playerCount: laneAssignments.length, laneAssignments });
      socket.emit("ml_match_config", simMl.createMLPublicConfig(room.settings));
      socket.emit("ml_state_snapshot", simMl.createMLSnapshot(entry.game));
      io.to(roomId).emit("player_reconnected", { laneIndex, displayName: playerName, mode: "multilane" });
      log.info(`[reconnect] ml lane ${laneIndex} in ${roomId}`);

    } else {
      const room = roomsByCode.get(code);
      if (!room) return socket.emit("rejoin_fail", { reason: "room_gone" });

      const side = seatKey;

      // Remove stale socket holding this side (if any)
      for (const [sid, s] of room.sidesBySocketId.entries()) {
        if (s === side && sid !== socket.id) {
          if (io.sockets.sockets.has(sid)) {
            return socket.emit("rejoin_fail", { reason: "seat_taken" });
          }
          room.players = room.players.filter(p => p !== sid);
          room.sidesBySocketId.delete(sid);
          sessionBySocketId.delete(sid);
          break;
        }
      }

      const grace = disconnectGrace.get(graceKey);
      if (grace) { clearTimeout(grace.graceHandle); disconnectGrace.delete(graceKey); }

      const playerName = grace?.playerName || socket.playerDisplayName || "Player";

      if (!room.players.includes(socket.id)) room.players.push(socket.id);
      room.sidesBySocketId.set(socket.id, side);
      socket.join(roomId);
      sessionBySocketId.set(socket.id, { code, roomId, side });

      // Send ack first so client sets myCode/mySide before state_snapshot fires
      socket.emit("rejoin_ack", { success: true, mode: "classic", side, code });
      socket.emit("state_snapshot", sim.createSnapshot(entry.game));
      socket.emit("match_config", sim.createPublicConfig(room.settings));
      io.to(roomId).emit("player_reconnected", { side, displayName: playerName, mode: "classic" });
      log.info(`[reconnect] classic side=${side} in ${roomId}`);
    }
  });

  // ── Party events ─────────────────────────────────────────────────────────────

  socket.on("party:create", () => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    requireAuthSocket(socket, () => {
      // If already in a party, leave it first
      const existingPartyId = partyByPlayerId.get(socket.playerId);
      if (existingPartyId) {
        _leaveParty(socket, existingPartyId);
      }

      // L-4: O(1) uniqueness check via partiesByCode index
      let code;
      do { code = generatePartyCode(); }
      while (partiesByCode.has(code));

      const partyId = crypto.randomUUID();
      const party = {
        id: partyId,
        code,
        leaderId: socket.playerId,
        members: [{ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id }],
        status: "idle",
        queueMode: null,
        queueEnteredAt: null,
        region: "global",
      };
      partiesById.set(partyId, party);
      partiesByCode.set(code, partyId);
      partyByPlayerId.set(socket.playerId, partyId);
      socket.join("party:" + partyId);
      io.to("party:" + partyId).emit("party_update", { party });
      log.info(`[party] created ${partyId} code=${code} by ${socket.playerId}`);
    });
  });

  socket.on("party:join", ({ code } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    requireAuthSocket(socket, () => {
      const normalized = String(code || "").trim().toUpperCase();
      if (normalized.length !== 6) {
        return socket.emit("error_message", { message: "Invalid party code." });
      }
      // L-4: O(1) party lookup by code
      const party = partiesById.get(partiesByCode.get(normalized));
      if (!party) return socket.emit("error_message", { message: "Party not found." });
      if (party.members.length >= 4) return socket.emit("error_message", { message: "Party is full." });
      if (party.status !== "idle") return socket.emit("error_message", { message: "Party is already in queue or match." });
      if (party.members.some(m => m.playerId === socket.playerId)) {
        return socket.emit("error_message", { message: "Already in this party." });
      }

      // Leave current party if any
      const existingPartyId = partyByPlayerId.get(socket.playerId);
      if (existingPartyId) _leaveParty(socket, existingPartyId);

      party.members.push({ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id });
      partyByPlayerId.set(socket.playerId, party.id);
      socket.join("party:" + party.id);
      io.to("party:" + party.id).emit("party_update", { party });
      log.info(`[party] ${socket.playerId} joined ${party.id}`);
    });
  });

  socket.on("party:leave", () => {
    requireAuthSocket(socket, () => {
      const partyId = partyByPlayerId.get(socket.playerId);
      if (!partyId) return;
      _leaveParty(socket, partyId);
    });
  });

  // ── Friends events ─────────────────────────────────────────────────────────────

  socket.on("friend:list", () => {
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!db) return;
    requireAuthSocket(socket, () => {
      getFriendsList(socket.playerId).then(friends => {
        socket.emit('friends_list', friends);
      }).catch(() => {});
    });
  });

  socket.on("friend:add", ({ displayName } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!db) return socket.emit('friend_error', { message: 'Friends require an account.' });
    if (!displayName || typeof displayName !== 'string') return;
    const name = displayName.trim().slice(0, 20);
    if (!name) return;
    requireAuthSocket(socket, () => {
      db.query(
        `SELECT id, display_name FROM players WHERE display_name = $1 AND id <> $2 LIMIT 1`,
        [name, socket.playerId]
      ).then(async ({ rows }) => {
        if (!rows.length) {
          return socket.emit('friend_error', { message: `No player named "${name}".` });
        }
        const target = rows[0];
        await db.query(
          `INSERT INTO friends (requester_id, addressee_id)
           VALUES ($1, $2)
           ON CONFLICT DO NOTHING`,
          [socket.playerId, target.id]
        );
        // Notify target if online
        const tSid = socketByPlayerId.get(target.id);
        if (tSid) {
          const tSock = io.sockets.sockets.get(tSid);
          if (tSock) tSock.emit('friend_request', { playerId: socket.playerId, displayName: socket.playerDisplayName });
          // Refresh their list too
          pushFriendsList(target.id).catch(() => {});
        }
        // Refresh caller's list
        pushFriendsList(socket.playerId).catch(() => {});
      }).catch(() => socket.emit('friend_error', { message: 'Could not send request.' }));
    });
  });

  socket.on("friend:accept", ({ playerId: requesterId } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!db) return;
    if (!requesterId || typeof requesterId !== 'string') return;
    requireAuthSocket(socket, () => {
      db.query(
        `UPDATE friends SET status = 'accepted', updated_at = NOW()
         WHERE requester_id = $1 AND addressee_id = $2 AND status = 'pending'`,
        [requesterId, socket.playerId]
      ).then(async () => {
        // Notify requester if online
        const rSid = socketByPlayerId.get(requesterId);
        if (rSid) {
          const rSock = io.sockets.sockets.get(rSid);
          if (rSock) rSock.emit('friend_accepted', { playerId: socket.playerId, displayName: socket.playerDisplayName });
          pushFriendsList(requesterId).catch(() => {});
        }
        pushFriendsList(socket.playerId).catch(() => {});
      }).catch(() => {});
    });
  });

  socket.on("friend:decline", ({ playerId: otherId } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!db) return;
    if (!otherId || typeof otherId !== 'string') return;
    requireAuthSocket(socket, () => {
      db.query(
        `DELETE FROM friends
         WHERE status = 'pending'
           AND ((requester_id = $1 AND addressee_id = $2)
             OR (requester_id = $2 AND addressee_id = $1))`,
        [socket.playerId, otherId]
      ).then(() => {
        pushFriendsList(socket.playerId).catch(() => {});
        pushFriendsList(otherId).catch(() => {});
      }).catch(() => {});
    });
  });

  socket.on("friend:remove", ({ playerId: otherId } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!db) return;
    if (!otherId || typeof otherId !== 'string') return;
    requireAuthSocket(socket, () => {
      db.query(
        `DELETE FROM friends
         WHERE (requester_id = $1 AND addressee_id = $2)
            OR (requester_id = $2 AND addressee_id = $1)`,
        [socket.playerId, otherId]
      ).then(() => {
        // Notify the removed player
        const oSid = socketByPlayerId.get(otherId);
        if (oSid) {
          const oSock = io.sockets.sockets.get(oSid);
          if (oSock) oSock.emit('friend_removed', { playerId: socket.playerId });
          pushFriendsList(otherId).catch(() => {});
        }
        pushFriendsList(socket.playerId).catch(() => {});
      }).catch(() => {});
    });
  });

  socket.on("party:invite", ({ targetPlayerId } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) return;
    if (!db) return;
    if (!targetPlayerId || typeof targetPlayerId !== 'string') return;
    requireAuthSocket(socket, () => {
      // Must be party leader
      const partyId = partyByPlayerId.get(socket.playerId);
      if (!partyId) return socket.emit('error_message', { message: 'You must be in a party to invite.' });
      const party = partiesById.get(partyId);
      if (!party || party.leaderId !== socket.playerId) {
        return socket.emit('error_message', { message: 'Only the party leader can invite.' });
      }
      if (party.members.length >= 4) {
        return socket.emit('error_message', { message: 'Party is full.' });
      }
      // Target must be online
      const tSid = socketByPlayerId.get(targetPlayerId);
      if (!tSid) return socket.emit('error_message', { message: 'That player is not online.' });

      // Verify they are an accepted friend
      db.query(
        `SELECT 1 FROM friends
         WHERE status = 'accepted'
           AND ((requester_id = $1 AND addressee_id = $2)
             OR (requester_id = $2 AND addressee_id = $1))
         LIMIT 1`,
        [socket.playerId, targetPlayerId]
      ).then(({ rows }) => {
        if (!rows.length) return socket.emit('error_message', { message: 'That player is not your friend.' });
        const tSock = io.sockets.sockets.get(tSid);
        if (tSock) {
          tSock.emit('party_invite', {
            partyId,
            partyCode: party.code,
            fromPlayerId: socket.playerId,
            fromDisplayName: socket.playerDisplayName,
          });
        }
      }).catch(() => {});
    });
  });

  // ── Queue events ──────────────────────────────────────────────────────────────

  socket.on("queue:enter", ({ mode } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    requireAuthSocket(socket, () => {
      const validModes = ["ranked_2v2", "casual_2v2"];
      const queueMode = validModes.includes(mode) ? mode : "casual_2v2";
      const dbMode = queueMode === "ranked_2v2" ? "2v2_ranked" : "2v2_casual";

      const partyId = partyByPlayerId.get(socket.playerId);
      if (!partyId) return socket.emit("error_message", { message: "You must be in a party to queue." });
      const party = partiesById.get(partyId);
      if (!party) return socket.emit("error_message", { message: "Party not found." });
      if (party.leaderId !== socket.playerId) {
        return socket.emit("error_message", { message: "Only the party leader can enter queue." });
      }
      if (party.status !== "idle") {
        return socket.emit("error_message", { message: "Party is already queued or in a match." });
      }

      // Build a promise that resolves to { rating, casualMatches, rankedMatches }.
      // For ranked: also fetches casual match count (smurf gate) + ranked match count (placement).
      let dataPromise;
      if (db && queueMode === "ranked_2v2") {
        dataPromise = Promise.all([
          db.query(
            `SELECT COALESCE(wins + losses, 0) AS matches FROM ratings WHERE player_id = $1 AND mode = '2v2_casual'`,
            [socket.playerId]
          ).then(r => Number(r.rows[0]?.matches) || 0).catch(() => 0),
          db.query(
            `SELECT rating, wins + losses AS matches FROM ratings WHERE player_id = $1 AND mode = '2v2_ranked'`,
            [socket.playerId]
          ).then(r => r.rows[0]
            ? { rating: Number(r.rows[0].rating), rankedMatches: Number(r.rows[0].matches) }
            : { rating: 1200, rankedMatches: 0 }
          ).catch(() => ({ rating: 1200, rankedMatches: 0 })),
        ]).then(([casualMatches, ranked]) => ({ casualMatches, ...ranked }));
      } else {
        dataPromise = (db
          ? db.query(`SELECT rating FROM ratings WHERE player_id = $1 AND mode = $2`, [socket.playerId, dbMode])
              .then(r => Number(r.rows[0]?.rating) || 1200)
              .catch(() => 1200)
          : Promise.resolve(1200)
        ).then(rating => ({ rating, casualMatches: 99, rankedMatches: 99 }));
      }

      dataPromise.then(({ rating, casualMatches, rankedMatches }) => {
        // Smurf gate: ranked requires completing 5+ casual matches first
        if (queueMode === "ranked_2v2" && casualMatches < 5) {
          return socket.emit("error_message", {
            message: `Complete ${5 - casualMatches} more casual match(es) to unlock ranked queue.`,
          });
        }

        // Commit queue state (done here so a failed gate never leaves party stuck in "queued")
        party.status = "queued";
        party.queueMode = queueMode;
        party.queueEnteredAt = Date.now();

        const isPlacement = queueMode === "ranked_2v2" && rankedMatches < 10;
        matchmaker.addToQueue(partyId, {
          mode: queueMode,
          rating,
          queueEnteredAt: party.queueEnteredAt,
          region: party.region,
          isPlacement,
        });

        io.to("party:" + partyId).emit("queue_status", { status: "queued", mode: queueMode, elapsed: 0 });
        log.info("queue entered", { partyId, mode: queueMode, isPlacement });
      });
    });
  });

  socket.on("queue:leave", () => {
    requireAuthSocket(socket, () => {
      const partyId = partyByPlayerId.get(socket.playerId);
      if (!partyId) return;
      const party = partiesById.get(partyId);
      if (!party) return;
      if (party.leaderId !== socket.playerId) {
        return socket.emit("error_message", { message: "Only the party leader can leave queue." });
      }
      if (party.status !== "queued") return;

      matchmaker.removeFromQueue(partyId);
      party.status = "idle";
      party.queueMode = null;
      party.queueEnteredAt = null;

      io.to("party:" + partyId).emit("queue_status", { status: "idle" });
      io.to("party:" + partyId).emit("party_update", { party });
      log.info('queue left', { partyId });
    });
  });

  socket.on("create_room", ({ settings } = {}) => {
    // fix #4: rate-limit lobby events
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const normalizedSettings = normalizeMatchSettings(settings);
    const { code, roomId } = createRoom(normalizedSettings);
    const room = roomsByCode.get(code);

    room.players.push(socket.id);
    room.sidesBySocketId.set(socket.id, "bottom");
    room.hostSocketId = socket.id;
    sessionBySocketId.set(socket.id, { code, roomId, side: "bottom" });
    socket.join(roomId);

    socket.emit("room_created", { code, side: "bottom", settings: room.settings });
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
    startGame(room.roomId, room.settings);
    io.to(room.roomId).emit("match_config", sim.createPublicConfig(room.settings));
  });

  // ── Multi-Lane lobby events ────────────────────────────────────────────────

  socket.on("create_ml_room", ({ displayName, settings } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const normalizedSettings = normalizeMatchSettings(settings);
    const { code, roomId } = createMLRoom(socket.id, displayName, normalizedSettings);
    sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
    socket.join(roomId);
    const room = mlRoomsByCode.get(code);
    socket.emit("ml_room_created", {
      code,
      laneIndex: 0,
      displayName: String(displayName || "Player").slice(0, 20),
      settings: room.settings,
    });
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
    const totalInRoom = room.players.length + (room.aiPlayers || []).length;
    if (totalInRoom >= 4) return socket.emit("error_message", { message: "Room is full (max 4)." });
    if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });

    // Humans always fill the lowest indices; AI slots are bumped above humans.
    const laneIndex = room.players.length;
    const assignedTeam = pickBalancedMlTeam(room);
    room.players.push(socket.id);
    room.laneBySocketId.set(socket.id, laneIndex);
    room.playerTeamsBySocketId.set(socket.id, assignedTeam);
    room.playerNames.set(socket.id, sanitizeDisplayName(displayName));
    // Shift any existing AI players above the new human count
    if (room.aiPlayers) {
      room.aiPlayers.forEach((ai, i) => { ai.laneIndex = room.players.length + i; });
    }

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

    // Auto-start when all human players are ready and total (human + AI) >= 2
    const totalPlayers = room.players.length + (room.aiPlayers || []).length;
    const teamCheck = validateMlTeamSetup(room);
    if (!teamCheck.ok) {
      return socket.emit("error_message", { message: teamCheck.reason });
    }
    if (totalPlayers >= 2 && room.readySet.size === room.players.length) {
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
    const totalPlayers = room.players.length + (room.aiPlayers || []).length;
    if (totalPlayers < 2) {
      return socket.emit("error_message", { message: "Need at least 2 players (add AI or invite another)." });
    }
    const teamCheck = validateMlTeamSetup(room);
    if (!teamCheck.ok) {
      return socket.emit("error_message", { message: teamCheck.reason });
    }
    if (!gamesByRoomId.has(room.roomId)) {
      startMLGame(room.roomId, normalized, io);
    }
  });

  socket.on("update_room_settings", ({ settings } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode === "multilane") return;
    const room = roomsByCode.get(session.code);
    if (!room) return;
    if (gamesByRoomId.has(room.roomId)) return;
    if (room.hostSocketId !== socket.id) return;
    room.settings = normalizeMatchSettings(settings);
    io.to(room.roomId).emit("room_settings_update", { settings: room.settings });
  });

  socket.on("update_ml_room_settings", ({ settings } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const room = mlRoomsByCode.get(session.code);
    if (!room) return;
    if (room.hostSocketId !== socket.id) return;
    if (gamesByRoomId.has(room.roomId)) return;
    room.settings = normalizeMatchSettings(settings);
    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
  });

  socket.on("add_ai_to_ml_room", ({ difficulty } = {}) => {
    if (!checkLobbyRateLimit(socket.id)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const room = mlRoomsByCode.get(session.code);
    if (!room) return;
    if (room.hostSocketId !== socket.id) {
      return socket.emit("error_message", { message: "Only the host can add AI players." });
    }
    if (gamesByRoomId.has(room.roomId)) {
      return socket.emit("error_message", { message: "Game already started." });
    }
    const totalPlayers = room.players.length + (room.aiPlayers || []).length;
    if (totalPlayers >= 4) {
      return socket.emit("error_message", { message: "Room is full (max 4 players)." });
    }
    const validDifficulties = ["easy", "medium", "hard"];
    const diff = validDifficulties.includes(String(difficulty)) ? String(difficulty) : "easy";
    if (!room.aiPlayers) room.aiPlayers = [];
    room.aiPlayers.push({ laneIndex: totalPlayers, difficulty: diff, team: pickBalancedMlTeam(room) });
    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
  });

  socket.on("remove_ai_from_ml_room", ({ laneIndex } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const room = mlRoomsByCode.get(session.code);
    if (!room || room.hostSocketId !== socket.id) return;
    if (gamesByRoomId.has(room.roomId)) return;
    if (!room.aiPlayers) return;
    const idx = room.aiPlayers.findIndex(ai => ai.laneIndex === Number(laneIndex));
    if (idx === -1) return;
    room.aiPlayers.splice(idx, 1);
    // Reassign lane indices so they remain contiguous after humans
    room.aiPlayers.forEach((ai, i) => { ai.laneIndex = room.players.length + i; });
    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
  });

  socket.on("swap_ml_lanes", ({ laneA, laneB } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const room = mlRoomsByCode.get(session.code);
    if (!room || room.hostSocketId !== socket.id) return;
    if (gamesByRoomId.has(room.roomId)) return;

    const a = Number(laneA);
    const b = Number(laneB);
    const total = room.players.length + (room.aiPlayers || []).length;
    if (!Number.isInteger(a) || !Number.isInteger(b) || a === b) return;
    if (a < 0 || a >= total || b < 0 || b >= total) return;

    const getOwner = (laneIdx) => {
      const humanSid = room.players.find(sid => room.laneBySocketId.get(sid) === laneIdx);
      if (humanSid) return { type: "human", sid: humanSid };
      const aiEntry = (room.aiPlayers || []).find(ai => ai.laneIndex === laneIdx);
      if (aiEntry) return { type: "ai", entry: aiEntry };
      return null;
    };

    const ownerA = getOwner(a);
    const ownerB = getOwner(b);
    if (!ownerA || !ownerB) return;

    if (ownerA.type === "human") {
      room.laneBySocketId.set(ownerA.sid, b);
      const sess = sessionBySocketId.get(ownerA.sid);
      if (sess) sess.laneIndex = b;
      io.to(ownerA.sid).emit("ml_lane_reassigned", { laneIndex: b });
    } else {
      ownerA.entry.laneIndex = b;
    }
    if (ownerB.type === "human") {
      room.laneBySocketId.set(ownerB.sid, a);
      const sess = sessionBySocketId.get(ownerB.sid);
      if (sess) sess.laneIndex = a;
      io.to(ownerB.sid).emit("ml_lane_reassigned", { laneIndex: a });
    } else {
      ownerB.entry.laneIndex = a;
    }

    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
  });

  socket.on("set_ml_team", ({ laneIndex, team } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "multilane") return;
    const room = mlRoomsByCode.get(session.code);
    if (!room || room.hostSocketId !== socket.id) return;
    if (gamesByRoomId.has(room.roomId)) return;

    const normalizedTeam = team === "blue" ? "blue" : "red";
    const targetLane = Number(laneIndex);
    const total = room.players.length + (room.aiPlayers || []).length;
    if (!Number.isInteger(targetLane) || targetLane < 0 || targetLane >= total) return;

    let currentTeam = null;
    let targetSid = null;
    let targetAi = null;
    for (const sid of room.players) {
      if (room.laneBySocketId.get(sid) === targetLane) {
        targetSid = sid;
        currentTeam = room.playerTeamsBySocketId?.get(sid) === "blue" ? "blue" : "red";
        break;
      }
    }
    if (!targetSid) {
      targetAi = (room.aiPlayers || []).find(ai => ai.laneIndex === targetLane) || null;
      if (targetAi) currentTeam = targetAi.team === "blue" ? "blue" : "red";
    }
    if (!currentTeam) return;
    if (currentTeam === normalizedTeam) return;

    const counts = countMlTeams(room);
    counts[currentTeam] -= 1;
    counts[normalizedTeam] += 1;
    const maxTeamSize = Math.ceil(total / 2);
    if (counts.red > maxTeamSize || counts.blue > maxTeamSize) {
      return socket.emit("error_message", { message: `Too many players on one team (max ${maxTeamSize}).` });
    }

    if (targetSid) {
      room.playerTeamsBySocketId.set(targetSid, normalizedTeam);
    } else if (targetAi) {
      targetAi.team = normalizedTeam;
    }

    io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(session.code, room));
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

      const res = applyMultilaneAction(entry, session.laneIndex, { type, data });
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

  socket.on("request_rematch", () => {
    const session = sessionBySocketId.get(socket.id);
    if (!session) return;

    if (session.mode === "multilane") {
      const room = mlRoomsByCode.get(session.code);
      if (!room) return;
      if (gamesByRoomId.has(room.roomId)) return; // game still running
      if (!room.rematchVotes) room.rematchVotes = new Set();
      if (room.rematchVotes.has(socket.id)) return; // already voted
      room.rematchVotes.add(socket.id);
      const needed = room.players.length;
      io.to(room.roomId).emit("rematch_vote", { count: room.rematchVotes.size, needed });
      if (room.rematchVotes.size >= needed) {
        room.rematchVotes = null;
        room.readySet = new Set();
        if (room._cleanupHandle) { clearTimeout(room._cleanupHandle); room._cleanupHandle = null; }
        startMLGame(room.roomId, session.code, io);
      }
    } else {
      const room = roomsByCode.get(session.code);
      if (!room) return;
      if (room.players.length < 2) return;
      if (gamesByRoomId.has(room.roomId)) return;
      if (!room.rematchVotes) room.rematchVotes = new Set();
      if (room.rematchVotes.has(socket.id)) return;
      room.rematchVotes.add(socket.id);
      const needed = room.players.length;
      io.to(room.roomId).emit("rematch_vote", { count: room.rematchVotes.size, needed });
      if (room.rematchVotes.size >= needed) {
        room.rematchVotes = null;
        if (room._cleanupHandle) { clearTimeout(room._cleanupHandle); room._cleanupHandle = null; }
        startGame(room.roomId, room.settings);
        io.to(room.roomId).emit("match_ready", { code: session.code });
        io.to(room.roomId).emit("match_config", sim.createPublicConfig(room.settings));
      }
    }
  });

  socket.on("leave_game", () => {
    const session = sessionBySocketId.get(socket.id);
    if (!session) return;

    if (session.mode === "multilane") {
      const code = session.code;
      const room = mlRoomsByCode.get(code);
      if (!room) return;

      const laneIndex = room.laneBySocketId.get(socket.id);
      const entry = gamesByRoomId.get(room.roomId);
      const playerName = room.playerNames.get(socket.id) || socket.playerDisplayName || "Player";
      if (entry && laneIndex !== undefined && !entry.eliminatedNotified.has(laneIndex)) {
        entry.eliminatedNotified.add(laneIndex);
        io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "quit" });
      }

      const idx = room.players.indexOf(socket.id);
      if (idx !== -1) room.players.splice(idx, 1);
      room.laneBySocketId.delete(socket.id);
      if (room.playerTeamsBySocketId) room.playerTeamsBySocketId.delete(socket.id);
      room.playerNames.delete(socket.id);
      room.readySet.delete(socket.id);
      if (room.rematchVotes) room.rematchVotes.delete(socket.id);
      sessionBySocketId.delete(socket.id);
      socket.leave(room.roomId);
      socket.emit("left_game_ack");

      if (room.players.length === 0) {
        stopMLGame(room.roomId, code);
        mlRoomsByCode.delete(code);
      } else if (!entry) {
        io.to(room.roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));
      }
      return;
    }

    const code = session.code;
    const room = roomsByCode.get(code);
    if (!room) return;

    const side = session.side || room.sidesBySocketId.get(socket.id);
    const entry = gamesByRoomId.get(room.roomId);
    if (entry && side) {
      const winner = side === "bottom" ? "top" : "bottom";
      const winnerLane = winner === "bottom" ? 0 : 1;
      const snapshots = entry.playerSnapshot.map(p => ({
        ...p,
        result: p.laneIndex === winnerLane ? "win" : "loss",
      }));
      io.to(room.roomId).emit("game_over", { winner, reason: "quit" });
      logMatchEnd(entry.matchIdPromise, winnerLane, snapshots);
      stopGame(room.roomId);
    }

    const idx = room.players.indexOf(socket.id);
    if (idx !== -1) room.players.splice(idx, 1);
    room.sidesBySocketId.delete(socket.id);
    if (room.rematchVotes) room.rematchVotes.delete(socket.id);
    sessionBySocketId.delete(socket.id);
    socket.leave(room.roomId);
    socket.emit("left_game_ack");

    if (room.players.length === 0) {
      roomsByCode.delete(code);
    } else if (!entry) {
      io.to(room.roomId).emit("player_left", { code });
    }
  });

  socket.on("disconnect", () => {
    rateLimitBySocketId.delete(socket.id);
    sessionBySocketId.delete(socket.id);

    // Authenticated player cleanup (party/queue always cleaned up on disconnect)
    if (socket.playerId) {
      // Notify accepted friends that this player went offline (before deleting from map)
      if (db) {
        const offlineId = socket.playerId;
        const offlineName = socket.playerDisplayName;
        getFriendsList(offlineId).then(friends => {
          for (const f of friends) {
            if (f.status === 'accepted') {
              const fSid = socketByPlayerId.get(f.playerId);
              if (fSid) {
                const fSock = io.sockets.sockets.get(fSid);
                if (fSock) fSock.emit('friend_offline', { playerId: offlineId, displayName: offlineName });
              }
            }
          }
        }).catch(() => {});
      }

      socketByPlayerId.delete(socket.playerId);

      // Party cleanup: remove from party, disband if empty, cancel queue if needed
      const partyId = partyByPlayerId.get(socket.playerId);
      if (partyId) {
        _leaveParty(socket, partyId);
      }
    }

    // Classic room cleanup — grace period if mid-game, immediate otherwise
    for (const [code, room] of roomsByCode.entries()) {
      const idx = room.players.indexOf(socket.id);
      if (idx !== -1) {
        const side = room.sidesBySocketId.get(socket.id);
        const isActive = gamesByRoomId.has(room.roomId);

        if (isActive && side) {
          // Start grace period — don't remove from room or stop game yet
          const graceKey = `${room.roomId}:${side}`;
          const playerName = socket.playerDisplayName || "Player";
          io.to(room.roomId).emit("opponent_disconnected", { side, gracePeriodMs: RECONNECT_GRACE_MS });

          const graceHandle = setTimeout(() => {
            disconnectGrace.delete(graceKey);
            const gEntry = gamesByRoomId.get(room.roomId);
            if (gEntry) {
              // Forfeit: award win to remaining side
              const winner = side === "bottom" ? "top" : "bottom";
              const winnerLane = winner === "bottom" ? 0 : 1;
              const snapshots = gEntry.playerSnapshot.map(p => ({
                ...p,
                result: p.laneIndex === winnerLane ? "win" : "loss",
              }));
              io.to(room.roomId).emit("game_over", { winner, reason: "forfeit" });
              logMatchEnd(gEntry.matchIdPromise, winnerLane, snapshots);
              stopGame(room.roomId);
            }
            // Clean up the now-stale slot
            const r = roomsByCode.get(code);
            if (r) {
              const i = r.players.indexOf(socket.id);
              if (i !== -1) r.players.splice(i, 1);
              r.sidesBySocketId.delete(socket.id);
              if (r.players.length === 0) roomsByCode.delete(code);
            }
          }, RECONNECT_GRACE_MS);

          disconnectGrace.set(graceKey, {
            graceHandle, expiresAt: Date.now() + RECONNECT_GRACE_MS,
            playerId: socket.playerId || null, guestId: socket.guestId,
            playerName, code, mode: "classic", seatKey: side, prevSocketId: socket.id,
          });
        } else {
          room.players.splice(idx, 1);
          room.sidesBySocketId.delete(socket.id);
          io.to(room.roomId).emit("player_left", { code });
          if (room.players.length === 0) {
            stopGame(room.roomId);
            roomsByCode.delete(code);
          }
        }
        break;
      }
    }

    // ML room cleanup — grace period if mid-game, immediate otherwise
    for (const [code, room] of mlRoomsByCode.entries()) {
      const idx = room.players.indexOf(socket.id);
      if (idx !== -1) {
        const laneIndex = room.laneBySocketId.get(socket.id);
        const isActive = gamesByRoomId.has(room.roomId);

        if (isActive && laneIndex !== undefined) {
          const graceKey = `${room.roomId}:${laneIndex}`;
          const playerName = room.playerNames.get(socket.id) || socket.playerDisplayName || "Player";
          io.to(room.roomId).emit("player_disconnected", { laneIndex, displayName: playerName, gracePeriodMs: RECONNECT_GRACE_MS });

          const graceHandle = setTimeout(() => {
            disconnectGrace.delete(graceKey);
            // ML forfeit: emit elimination; game continues for remaining players
            const gEntry = gamesByRoomId.get(room.roomId);
            if (gEntry && !gEntry.eliminatedNotified.has(laneIndex)) {
              gEntry.eliminatedNotified.add(laneIndex);
              io.to(room.roomId).emit("ml_player_eliminated", { laneIndex, displayName: playerName, reason: "forfeit" });
              io.to(socket.id).emit("ml_spectator_join", { laneIndex });
            }
            // Remove the stale slot
            const r = mlRoomsByCode.get(code);
            if (r) {
              const i = r.players.indexOf(socket.id);
              if (i !== -1) r.players.splice(i, 1);
              r.laneBySocketId.delete(socket.id);
              if (r.playerTeamsBySocketId) r.playerTeamsBySocketId.delete(socket.id);
              r.playerNames.delete(socket.id);
              r.readySet.delete(socket.id);
              if (r.players.length === 0) {
                stopMLGame(room.roomId, code);
                mlRoomsByCode.delete(code);
              }
            }
          }, RECONNECT_GRACE_MS);

          disconnectGrace.set(graceKey, {
            graceHandle, expiresAt: Date.now() + RECONNECT_GRACE_MS,
            playerId: socket.playerId || null, guestId: socket.guestId,
            playerName, code, mode: "multilane", seatKey: String(laneIndex), prevSocketId: socket.id,
          });
        } else {
          room.players.splice(idx, 1);
          room.laneBySocketId.delete(socket.id);
          if (room.playerTeamsBySocketId) room.playerTeamsBySocketId.delete(socket.id);
          room.playerNames.delete(socket.id);
          room.readySet.delete(socket.id);
          io.to(room.roomId).emit("player_left", { code });
          if (room.players.length === 0) {
            stopMLGame(room.roomId, code);
            mlRoomsByCode.delete(code);
          }
        }
        break;
      }
    }
  });
});

app.get("/", (_req, res) => {
  res.sendFile(path.join(clientDir, "index.html"));
});

app.get("/terms", (_req, res) => {
  res.sendFile(path.join(clientDir, "terms.html"));
});

app.get("/privacy", (_req, res) => {
  res.sendFile(path.join(clientDir, "privacy.html"));
});

// Alias routes for common legal URL patterns
app.get("/terms-of-service", (_req, res) => {
  res.redirect(302, "/terms");
});
app.get("/privacy-policy", (_req, res) => {
  res.redirect(302, "/privacy");
});

// Lightweight health endpoint for uptime checks.
app.get("/health", async (_req, res) => {
  if (process.env.DATABASE_URL) {
    try {
      const db = require("./db");
      await db.query("SELECT 1");
      return res.json({ ok: true, db: "connected" });
    } catch {
      return res.status(503).json({ ok: false, db: "error" });
    }
  }
  res.json({ ok: true, db: "none" });
});

const PORT = process.env.PORT || 3000;
const HOST = process.env.HOST || '0.0.0.0';

async function startServer() {
  // ── Startup security checks ──────────────────────────────────────────────
  if (!process.env.JWT_SECRET) {
    log.warn('JWT_SECRET is not set — authentication will not work securely');
  }
  if (process.env.NODE_ENV === 'production' && !process.env.ALLOWED_ORIGIN) {
    log.warn('ALLOWED_ORIGIN not set in production — CORS allows all origins');
  }

  // Auto-run DB migrations when DATABASE_URL is present
  if (process.env.DATABASE_URL) {
    try {
      const { execSync } = require("child_process");
      execSync(`node "${path.join(__dirname, "migrate.js")}"`, {
        stdio: "inherit",
        env: process.env,
      });
    } catch (err) {
      log.error('migration failed', { err: err.message });
      // Don't abort — server can still serve the game without DB
    }
  }

  // Start in-memory matchmaking loop
  matchmaker.startMatchmakingLoop(io, partiesById, socketByPlayerId, onMatchFound);

  server.listen(PORT, HOST, () => {
    log.info('server started', { port: PORT, host: HOST, env: process.env.NODE_ENV || 'development' });
  });
}

// ── Graceful shutdown ─────────────────────────────────────────────────────────
// On SIGTERM / SIGINT: stop game loops, disconnect sockets, close DB pool, exit.

function gracefulShutdown(signal) {
  log.info('graceful shutdown', { signal });

  // Stop matchmaking loop
  matchmaker.stopMatchmakingLoop();

  // Stop all active game tick loops.
  for (const [, entry] of gamesByRoomId.entries()) {
    clearInterval(entry.tickHandle);
  }
  gamesByRoomId.clear();

  // Cancel all disconnect grace timers
  for (const [, grace] of disconnectGrace.entries()) {
    clearTimeout(grace.graceHandle);
  }
  disconnectGrace.clear();

  // Notify all connected clients
  io.emit('server_shutdown', { message: 'Server is restarting. Please refresh in a moment.' });

  // Force exit fallback in case shutdown stalls
  const forceExit = setTimeout(() => {
    log.warn('forced exit after shutdown timeout');
    process.exit(1);
  }, 10_000);
  forceExit.unref();

  // Close socket.io → HTTP server → DB pool → exit
  io.close(() => {
    server.close(async () => {
      if (db) {
        try { await db.pool.end(); } catch { /* ignore */ }
      }
      log.info('server stopped cleanly');
      process.exit(0);
    });
  });
}

process.on('SIGTERM', () => gracefulShutdown('SIGTERM'));
process.on('SIGINT',  () => gracefulShutdown('SIGINT'));

startServer();
