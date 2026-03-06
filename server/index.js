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

const simMl = require("./sim-multilane");
const { stringToSeed } = require("./sim-core");
const simSurvival = require("./sim-survival");
let aiRuntime;
try {
  // Monorepo layout: server/index.js + ../ai/*
  aiRuntime = require("../ai/sim_runner");
} catch (_err) {
  try {
    // Server-only deploy layout if ai/ is bundled alongside index.js
    aiRuntime = require("./ai/sim_runner");
  } catch (_err2) {
    // Fallback adapter to legacy AI engine so server can still boot.
    const legacyAi = require("./ai");
    aiRuntime = {
      __engine: "legacy_ai_adapter_v1",
      createBotController(config) {
        const cfg = config && typeof config === "object" ? config : {};
        const game = cfg.game;
        const botConfigs = Array.isArray(cfg.botConfigs) ? cfg.botConfigs : [];
        const handles = botConfigs.map((b) => legacyAi.startAI(game, b.laneIndex, b.difficulty));
        return {
          runtimeTracker: null,
          getBotCount() { return handles.length; },
          onBeforeSimTick() {},
          onAfterSimTick() {},
          drainActionLog() { return []; },
          stop() { handles.forEach((h) => legacyAi.stopAI(h)); },
        };
      },
      captureStateLite(game) {
        return {
          tick: Number(game && game.tick) || 0,
          phase: game && game.phase,
          winner: game && game.winner,
          lanes: (game && game.lanes ? game.lanes : []).map((lane) => ({
            laneIndex: lane.laneIndex,
            team: lane.team,
            eliminated: !!lane.eliminated,
            gold: Number(lane.gold) || 0,
            income: Number(lane.income) || 0,
            lives: Number(lane.lives) || 0,
          })),
        };
      },
    };
    // log deferred â€" logger loaded after this block
    process.stderr.write(JSON.stringify({ level: 'warn', ts: new Date().toISOString(), msg: '[ai] new ai runtime module not found; using legacy adapter fallback' }) + '\n');
  }
}
const authService = require("./auth");
const matchmaker = require("./services/matchmaker");
const log = require("./logger");
const unitTypes = require("./unitTypes");
const barracksLevels = require("./barracksLevels");
const db = process.env.DATABASE_URL ? require("./db") : null;
const ratingService = process.env.DATABASE_URL ? require("./services/rating") : null;
const seasonService = process.env.DATABASE_URL ? require("./services/season") : null;
const gameConfig    = require("./gameConfig");

// â"€â"€ Loadout helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

function loadoutEntry(ut) {
  return {
    id:           ut.id,
    key:          ut.key,
    name:         ut.name,
    send_cost:    Number(ut.send_cost)    || 0,
    hp:           Number(ut.hp)           || 0,
    attack_damage:Number(ut.attack_damage)|| 0,
    attack_speed: Number(ut.attack_speed) || 1,
    range:        Number(ut.range)        || 0,
    path_speed:   Number(ut.path_speed)   || 0,
    damage_type:  ut.damage_type          || 'NORMAL',
    armor_type:   ut.armor_type           || 'UNARMORED',
    income:       Number(ut.income)       || 0,
    icon_url:     ut.icon_url             || null,
    sprite_url:   ut.sprite_url           || null,
    animation_url: ut.animation_url       || null,
    sprite_url_front: ut.sprite_url_front || null,
    sprite_url_back: ut.sprite_url_back   || null,
    animation_url_front: ut.animation_url_front || null,
    animation_url_back: ut.animation_url_back   || null,
    idle_sprite_url: ut.idle_sprite_url || null,
    idle_sprite_url_front: ut.idle_sprite_url_front || null,
    idle_sprite_url_back: ut.idle_sprite_url_back || null,
  };
}

// Resolve a player's loadout to an array of exactly 5 unit type objects.
// Only unit types with behavior_mode='both' are allowed.
// Falls back to the 5 cheapest enabled 'both' unit types by default.
async function resolveLoadout(playerId, loadoutSlot, inlineUnitTypeIds) {
  const all    = unitTypes.getAllUnitTypes();
  const byId   = {};
  for (const ut of all) byId[ut.id] = ut;

  let ids = null;

  // Authenticated player with a saved slot
  if (Number.isInteger(loadoutSlot) && loadoutSlot >= 0 && loadoutSlot <= 3 && db && playerId) {
    try {
      const row = await db.query(
        'SELECT unit_type_ids FROM loadouts WHERE player_id = $1 AND slot = $2',
        [playerId, loadoutSlot]
      ).then(r => r.rows[0]);
      if (row) ids = row.unit_type_ids;
    } catch { /* fall through to default */ }
  }

  // Guest inline IDs
  if (!ids && Array.isArray(inlineUnitTypeIds) && inlineUnitTypeIds.length === 5) {
    ids = inlineUnitTypeIds;
  }

  // Resolve IDs â†' enabled moving unit types
  if (ids) {
    const resolved = ids
      .map(id => byId[id])
      .filter(ut => ut && ut.enabled && ut.behavior_mode === 'both');
    if (resolved.length === 5) return resolved.map(loadoutEntry);
  }

  // Default: cheapest 5 enabled moving units
  const moving = all
    .filter(ut => ut.enabled && ut.behavior_mode === 'both')
    .sort((a, b) => (Number(a.send_cost) || 0) - (Number(b.send_cost) || 0));
  return moving.slice(0, 5).map(loadoutEntry);
}

function hasValidInlineLoadoutIds(unitTypeIds) {
  if (!Array.isArray(unitTypeIds) || unitTypeIds.length !== 5) return false;
  const allowedIds = new Set(
    unitTypes.getAllUnitTypes()
      .filter(ut => ut.enabled && ut.behavior_mode === "both")
      .map(ut => ut.id)
  );
  return unitTypeIds.every(id => {
    const parsed = Number(id);
    return Number.isInteger(parsed) && allowedIds.has(parsed);
  });
}

function validateLoadoutSelection(socket, loadoutSlot, unitTypeIds) {
  const validSlot = Number.isInteger(loadoutSlot) && loadoutSlot >= 0 && loadoutSlot <= 3;
  if (!validSlot) {
    socket.emit("error_message", { message: "Select a loadout slot before starting a match." });
    return false;
  }
  if (socket.playerId) return true;
  if (hasValidInlineLoadoutIds(unitTypeIds)) return true;
  socket.emit("error_message", { message: "Save a temporary loadout before starting as a guest." });
  return false;
}

if (db) {
  gameConfig.loadActiveConfigs(db).catch(err =>
    log.warn('game config load failed, using hardcoded defaults', {
      err: err && (err.stack || err.message || String(err)),
      code: err?.code || null,
      name: err?.name || null,
    })
  );
}

const app = express();
// M5: validate ALLOWED_ORIGIN is never a wildcard when credentials=true
const _corsOrigin = process.env.ALLOWED_ORIGIN || 'http://localhost:3000';
if (_corsOrigin === '*' || _corsOrigin.includes('*')) {
  throw new Error('[startup] ALLOWED_ORIGIN cannot be a wildcard when credentials=true');
}
// H-4: restrict CORS to known origin; wildcard is unsafe for auth endpoints
app.use(cors({
  origin: _corsOrigin,
  methods: ['GET', 'POST', 'PATCH'],
  credentials: true,
}));
app.use(express.json({ limit: '8mb' }));
// Minimal cookie parser â€" populates req.cookies without adding a dependency
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

// â"€â"€ Security headers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
app.use((_req, res, next) => {
  const isAdminDocument = _req.path === '/admin.html' || _req.path === '/admin';
  const scriptSrc = isAdminDocument
    ? "script-src 'self' 'unsafe-inline' https://accounts.google.com https://cdn.socket.io; "
    : "script-src 'self' https://accounts.google.com https://cdn.socket.io; ";

  // Google Identity popup flow can fail silently under strict COOP.
  // Allow popups on the admin document so GIS callback can complete.
  if (isAdminDocument) {
    res.setHeader('Cross-Origin-Opener-Policy', 'same-origin-allow-popups');
    res.setHeader('Cross-Origin-Embedder-Policy', 'unsafe-none');
  }

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
    scriptSrc +
    "connect-src 'self' wss: ws: https://accounts.google.com https://cdn.socket.io; " +
    "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://accounts.google.com; " +
    "font-src 'self' https://fonts.gstatic.com; " +
    "img-src 'self' data: https:; " +
    "frame-src https://accounts.google.com; " +
    "frame-ancestors 'self';"
  );
  next();
});

// â"€â"€ Auth + player routes â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
app.use("/auth",    require("./routes/auth"));
app.use("/players", require("./routes/players"));
app.use("/parties", require("./routes/parties"));
app.use("/queue",   require("./routes/queue"));
app.use("/matches",     require("./routes/matches"));
app.use("/leaderboard", require("./routes/leaderboard"));
app.use("/admin",       require("./routes/admin"));
app.use("/api/loadouts", require("./routes/loadouts"));

// Public config for client (Google Client ID etc.)
app.get("/config", (_req, res) => {
  res.json({
    googleClientId:      process.env.GOOGLE_CLIENT_ID || "",
    passwordAuthEnabled: !!db,  // true whenever DB is connected
  });
});

// Public tower catalog â€" returns enabled towers with abilities
app.get("/api/towers", async (_req, res) => {
  if (!db) return res.json({ towers: [] });
  try {
    const rows = await db.query(`
      SELECT t.*,
        COALESCE(json_agg(
          json_build_object('ability_key', a.ability_key, 'params', a.params)
          ORDER BY a.id
        ) FILTER (WHERE a.id IS NOT NULL), '[]') AS abilities
      FROM towers t
      LEFT JOIN tower_ability_assignments a ON a.tower_id = t.id
      WHERE t.enabled = true
      GROUP BY t.id
      ORDER BY t.id`
    );
    res.json({ towers: rows.rows });
  } catch (err) {
    log.error('[api] GET /api/towers error', { err: err.message });
    res.json({ towers: [] });
  }
});

// Public unit type catalog — returns enabled, player-visible unit types + display field config
app.get("/api/unit-types", async (_req, res) => {
  const all = unitTypes.getAllUnitTypes().filter(
    ut => ut.enabled && ut.display_to_players !== false
  );
  let displayFields = null;
  if (db) {
    try {
      const r = await db.query('SELECT * FROM unit_display_fields ORDER BY sort_order');
      displayFields = r.rows;
    } catch { /* table may not exist until migration 026 runs */ }
  }
  res.json({ unitTypes: all, displayFields });
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

function sendClientFile(res, filename) {
  const candidate = [clientDir, ...clientDirCandidates]
    .map((dir) => path.join(dir, filename))
    .find((filePath) => fs.existsSync(filePath));
  if (candidate) return res.sendFile(candidate);
  return res.status(404).json({ error: `${filename} not found` });
}

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
  if (entry.botController && typeof entry.botController.stop === "function") {
    try { entry.botController.stop(); } catch { /* ignore */ }
  }
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

// â"€â"€ Cookie parsing helper â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
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

// â"€â"€ Socket.IO auth middleware â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
// Auth is optional â€" guests without a token continue as before.
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
      // Check ban status with cache (M6: avoid DB hit on every connect)
      if (db) {
        try {
          const cached = playerBanCache.get(payload.sub);
          if (cached && Date.now() - cached.cachedAt < PLAYER_BAN_CACHE_TTL_MS) {
            if (cached.suspended) socket.playerBanned = true;
          } else {
            const r = await db.query(`SELECT status FROM players WHERE id = $1`, [payload.sub]);
            const suspended = r.rows[0]?.status === 'suspended';
            playerBanCache.set(payload.sub, { suspended, cachedAt: Date.now() });
            if (suspended) socket.playerBanned = true;
          }
        } catch { /* ignore */ }
      }
    } catch { /* invalid/expired â€" treat as guest */ }
  }
  next();
});

const DEFAULT_MATCH_SETTINGS = Object.freeze({
  startIncome: 10,
});

// code -> { roomId, players:[socketId], laneBySocketId:Map, readySet:Set, playerNames:Map, createdAt, mode:"multilane", hostSocketId }
const mlRoomsByCode = new Map();
// code -> { roomId, players:[socketId], laneBySocketId:Map, readySet:Set, playerNames:Map, hostSocketId, coopMode:bool, createdAt }
const survivalRoomsByCode = new Map();
// socketId -> { code, roomId, side } | { code, roomId, laneIndex, mode:"multilane" } | { code, roomId, laneIndex, mode:"survival" }
const sessionBySocketId = new Map();

// roomId -> { game, tickHandle, snapshotEveryNTicks }
const gamesByRoomId = new Map();

// â"€â"€ Competitive in-memory state â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
// partyId -> PartyObject
const partiesById = new Map();
// playerId -> partyId
const partyByPlayerId = new Map();
// playerId -> socketId  (only authenticated players)
const socketByPlayerId = new Map();
// L-4: O(1) party code lookup â€" kept in sync with partiesById
const partiesByCode = new Map();

// â"€â"€ Reconnect state â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
const RECONNECT_GRACE_MS = 120_000; // 2 minutes grace before forfeit
// `${roomId}:${seatKey}` -> { graceHandle, expiresAt, playerId, guestId, playerName, code, mode, seatKey, prevSocketId }
const disconnectGrace = new Map();

// Expose via app.locals so REST routes can read them
app.locals.partiesById     = partiesById;
app.locals.partyByPlayerId = partyByPlayerId;
// Admin dashboard live-state access
app.locals.gamesByRoomId      = gamesByRoomId;
app.locals.mlRoomsByCode      = mlRoomsByCode;
app.locals.survivalRoomsByCode = survivalRoomsByCode;
app.locals.socketByPlayerId   = socketByPlayerId;
// playerBanCache assigned to app.locals below after declaration

// Per-socket rate-limit counters (resets on reconnect â€" see IP limit below for bypass prevention)
// socketId -> { lobbyCount, lobbyWindowStart, actionCount, actionWindowStart }
const rateLimitBySocketId = new Map();

// H5: IP-based lobby rate limit â€" persists across reconnects
// ip -> { count, windowStart }
const lobbyRateLimitByIp = new Map();
setInterval(() => {
  const cutoff = Date.now() - 60_000;
  for (const [ip, r] of lobbyRateLimitByIp) {
    if (r.windowStart < cutoff) lobbyRateLimitByIp.delete(ip);
  }
}, 60_000).unref();

// M6: per-player ban status cache (5min TTL) to avoid a DB hit on every socket connect
// playerId -> { suspended: bool, cachedAt: number }
const playerBanCache = new Map();
app.locals.playerBanCache     = playerBanCache; // admin ban/unban should call .delete(playerId)
const PLAYER_BAN_CACHE_TTL_MS = 5 * 60 * 1000;
setInterval(() => {
  const cutoff = Date.now() - PLAYER_BAN_CACHE_TTL_MS;
  for (const [id, v] of playerBanCache) {
    if (v.cachedAt < cutoff) playerBanCache.delete(id);
  }
}, 60_000).unref();

function checkLobbyRateLimit(socketId, ip) {
  const now = Date.now();
  // Socket-level limit
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
  if (r.lobbyCount > 10) return false; // max 10 lobby ops per minute per socket

  // H5: IP-level limit â€" prevents bypass via reconnect; allows 20/min per IP
  if (ip) {
    let ipR = lobbyRateLimitByIp.get(ip);
    if (!ipR || now - ipR.windowStart > 60_000) {
      ipR = { count: 0, windowStart: now };
      lobbyRateLimitByIp.set(ip, ipR);
    }
    ipR.count++;
    if (ipR.count > 20) return false;
  }
  return true;
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

// â"€â"€ Reconnect token helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
const jwt = require("jsonwebtoken");
const RECONNECT_JWT_SECRET = process.env.JWT_SECRET; // required â€" server/auth.js throws at startup if unset

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

// â"€â"€ Match logging helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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
  const client = await db.getClient();
  try {
    await client.query('BEGIN');
    await client.query(
      `UPDATE matches SET status='completed', ended_at=NOW(), winner_lane=$1 WHERE id=$2`,
      [winnerLane ?? null, matchId]
    );
    if (playerSnapshots.length > 0) {
      const placeholders = playerSnapshots.map((_, i) =>
        `($1, ${i * 3 + 2}, ${i * 3 + 3}, ${i * 3 + 4})`
      ).join(', ');
      const params = [matchId];
      for (const { playerId, laneIndex, result } of playerSnapshots) {
        params.push(playerId, laneIndex, result);
      }
      await client.query(
        `INSERT INTO match_players (match_id, player_id, lane_index, result) VALUES ${placeholders}`,
        params
      );
    }
    await client.query('COMMIT');
  } catch (err) {
    await client.query('ROLLBACK').catch(() => {});
    log.error('[match] close failed:', { err: err.message });
  } finally {
    client.release();
  }
}


// â"€â"€ Multi-Lane helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

function createMLRoom(hostSocketId, displayName, settings) {
  let code;
  do code = generateCode(6);
  while (mlRoomsByCode.has(code));
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

const FFA_TEAMS = ['red', 'blue', 'green', 'purple'];

function ffaTeamForLane(laneIndex) {
  return FFA_TEAMS[laneIndex] || `p${laneIndex}`;
}

function validateMlTeamSetup(room) {
  if (room.pvpMode === 'ffa') return { ok: true };
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
    team: room.playerTeamsBySocketId?.get(sid) || "red",
  }));
  const aiPlayers = (room.aiPlayers || []).map(ai => ({
    laneIndex: ai.laneIndex,
    displayName: "CPU (" + capFirst(ai.difficulty) + ")",
    ready: true,
    isAI: true,
    difficulty: ai.difficulty,
    team: ai.team || "red",
  }));
  const players = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);
  return { code, players, hostLaneIndex: 0, settings: normalizeMatchSettings(room.settings), pvpMode: room.pvpMode || '2v2' };
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
      team: room.playerTeamsBySocketId?.get(sid) || "red",
    })),
    ...aiList.map(ai => ({
      laneIndex: ai.laneIndex,
      displayName: "CPU (" + capFirst(ai.difficulty) + ")",
      isAI: true,
      team: ai.team || "red",
    })),
  ];
  const laneTeams = new Array(playerCount).fill("red");
  for (const a of laneAssignments) {
    if (Number.isInteger(a.laneIndex) && a.laneIndex >= 0 && a.laneIndex < playerCount) {
      laneTeams[a.laneIndex] = a.team || "red";
    }
  }
  const matchSeedStr = buildMlMatchSeed(roomId, code, laneAssignments);
  const matchSeedNum = stringToSeed(matchSeedStr);
  const game = simMl.createMLGame(playerCount, { ...room.settings, laneTeams, matchSeed: matchSeedNum });

  io.to(roomId).emit("ml_match_ready", { code, playerCount, laneAssignments, aiEngine: "new_bot_controller_v1" });
  const _mlBaseConfig = simMl.createMLPublicConfig(room.settings);
  io.to(roomId).emit("ml_match_config", _mlBaseConfig);

  // Per-player loadout resolution (non-blocking â€" arrives shortly after match_ready)
  if (!room.loadoutByLane) room.loadoutByLane = [];
  Promise.all(room.players.map(async sid => {
    const sock = io.sockets.sockets.get(sid);
    if (!sock) return;
    const laneIdx = room.laneBySocketId.get(sid);
    const loadout = await resolveLoadout(
      sock.playerId || null,
      sock.pendingLoadoutSlot,
      sock.pendingUnitTypeIds
    );
    sock.pendingLoadoutSlot = null;
    sock.pendingUnitTypeIds = null;
    room.loadoutByLane[laneIdx] = loadout;
    // Phase D: wire loadout key order into lane.autosend so auto-send respects slot order
    if (game.lanes[laneIdx]) {
      game.lanes[laneIdx].autosend.loadoutKeys = loadout.map(ut => ut.key);
    }
    sock.emit("ml_match_config", { loadout });
  })).catch(err => log.error('[loadout] resolve error', { err: err.message }));
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
  if (aiList.length > 0) {
    log.info("ml ai engine active", {
      roomId,
      code,
      aiEngine: aiRuntime.__engine || "new_bot_controller_v1",
      botCount: botController ? botController.getBotCount() : 0,
    });
  }

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
  const snapshotEveryNTicks = 10; // Phase B: 10 ticks = 0.5s at 20Hz

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

    // Phase D: emit per-player queue_update every 5 ticks (250ms at 20Hz)
    if (localTick % 5 === 0) for (const sid of room.players) {
      const laneIdx = room.laneBySocketId.get(sid);
      if (laneIdx === undefined) continue;
      const qLane = entry.game.lanes[laneIdx];
      if (!qLane) continue;
      const qSock = io.sockets.sockets.get(sid);
      if (!qSock) continue;
      const queues = {};
      for (const k of qLane.sendQueue) queues[k] = (queues[k] || 0) + 1;
      qSock.emit("queue_update", {
        queues,
        drainProgress: simMl.SEND_INTERVAL_TICKS > 0
          ? qLane.sendDrainCounter / simMl.SEND_INTERVAL_TICKS : 0,
        totalQueued: qLane.sendQueue.length,
        queueCap: simMl.QUEUE_CAP,
      });
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
  if (entry.botController && typeof entry.botController.stop === "function") {
    try { entry.botController.stop(); } catch { /* ignore */ }
  }
  gamesByRoomId.delete(roomId);
  log.info(`[ml-game] stopped ${roomId}`);
  const handle = setTimeout(() => {
    mlRoomsByCode.delete(code);
    log.info(`[ml-room] cleaned up ${code}`);
  }, 60_000);
  const room = mlRoomsByCode.get(code);
  if (room) room._cleanupHandle = handle;
}

// â"€â"€ Survival helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

async function loadActiveWaveSet() {
  if (!db) return null;
  try {
    const setRes = await db.query(
      `SELECT * FROM survival_wave_sets WHERE is_active = true LIMIT 1`
    );
    if (!setRes.rows[0]) return null;
    const waveSet = setRes.rows[0];

    const wavesRes = await db.query(
      `SELECT w.*, COALESCE(
         json_agg(sg ORDER BY sg.start_delay_ms, sg.id) FILTER (WHERE sg.id IS NOT NULL), '[]'
       ) AS spawn_groups
       FROM survival_waves w
       LEFT JOIN survival_spawn_groups sg ON sg.wave_id = w.id
       WHERE w.wave_set_id = $1
       GROUP BY w.id
       ORDER BY w.wave_number`,
      [waveSet.id]
    );
    waveSet.waves = wavesRes.rows;
    return waveSet;
  } catch (err) {
    log.error('[survival] loadActiveWaveSet error', { err: err.message });
    return null;
  }
}

function createSurvivalRoom(hostSocketId, displayName, coopMode) {
  let code;
  do code = generateCode(6);
  while (mlRoomsByCode.has(code) || survivalRoomsByCode.has(code));
  const roomId = `svroom_${code}`;
  const room = {
    roomId,
    players: [hostSocketId],
    laneBySocketId: new Map([[hostSocketId, 0]]),
    playerNames: new Map([[hostSocketId, sanitizeDisplayName(displayName)]]),
    readySet: new Set(),
    hostSocketId,
    coopMode: !!coopMode,
    createdAt: Date.now(),
    mode: "survival",
  };
  survivalRoomsByCode.set(code, room);
  return { code, roomId };
}

async function startSurvivalGame(roomId, code, io) {
  if (gamesByRoomId.has(roomId)) return;
  const room = survivalRoomsByCode.get(code);
  if (!room) return;

  // Load wave set from DB (or use minimal fallback)
  let waveSet = await loadActiveWaveSet();
  if (!waveSet) {
    waveSet = {
      id: null, name: "Default", description: "", auto_scale: true,
      starting_gold: 150, starting_lives: 20, waves: [],
    };
  }

  const playerCount = room.players.length;
  const survivalSeedNum = stringToSeed(`${roomId}:${code}:survival`);
  const game = simSurvival.createSurvivalGame(playerCount, waveSet, survivalSeedNum);

  const playerSnapshot = room.players
    .map(sid => {
      const sock = io.sockets.sockets.get(sid);
      return sock?.playerId
        ? { playerId: sock.playerId, laneIndex: room.laneBySocketId.get(sid) }
        : null;
    })
    .filter(Boolean);

  const matchIdPromise = logMatchStart(roomId, "survival");

  // Announce match ready
  const laneAssignments = room.players.map(sid => ({
    laneIndex: room.laneBySocketId.get(sid),
    displayName: room.playerNames.get(sid) || "Player",
  }));
  io.to(roomId).emit("survival_match_ready", { code, playerCount, laneAssignments, waveSetName: waveSet.name });
  // Send tower/unit definitions so the client's towerMeta is populated (same event
  // the ML client already handles via applyMatchConfig).
  io.to(roomId).emit("ml_match_config", simMl.createMLPublicConfig({}));

  let localTick = 0;
  const snapshotEveryNTicks = 10; // Phase B: 10 ticks = 0.5s at 20Hz

  const tickHandle = setInterval(() => {
    const entry = gamesByRoomId.get(roomId);
    if (!entry) return;

    simSurvival.tickSurvival(entry.game);
    localTick++;

    // Emit wave start banner when phase transitions to SPAWNING
    if (entry.game.wavePhase === "SPAWNING" && entry._lastWavePhase !== "SPAWNING") {
      const waveConfig = (entry.game.config?.waves || []).find(w => w.wave_number === entry.game.waveNumber);
      io.to(roomId).emit("survival_wave_start", {
        waveNumber: entry.game.waveNumber,
        isBoss:  waveConfig?.is_boss  || false,
        isRush:  waveConfig?.is_rush  || false,
        isElite: waveConfig?.is_elite || false,
      });
    }
    entry._lastWavePhase = entry.game.wavePhase;

    if (localTick % snapshotEveryNTicks === 0) {
      io.to(roomId).emit("survival_state_snapshot", simSurvival.createSurvivalSnapshot(entry.game));
    }

    if (entry.game.phase === "ended") {
      const snap = simSurvival.createSurvivalSnapshot(entry.game);
      io.to(roomId).emit("survival_ended", {
        wavesCleared: snap.totalWavesCleared,
        killCount: snap.killCount,
        timeSurvived: snap.timeSurvived,
        goldEarned: snap.goldEarned,
        wavePhase: snap.wavePhase,
      });
      logMatchEnd(entry.matchIdPromise, null, entry.playerSnapshot);
      stopSurvivalGame(roomId, code);
    }
  }, simSurvival.TICK_MS);

  gamesByRoomId.set(roomId, {
    game,
    tickHandle,
    mode: "survival",
    snapshotEveryNTicks,
    matchIdPromise,
    playerSnapshot,
    _lastWavePhase: "PREP",
  });

  log.info('[survival] game started', { roomId, code, playerCount });
}

function stopSurvivalGame(roomId, code) {
  const entry = gamesByRoomId.get(roomId);
  if (!entry) return;
  clearInterval(entry.tickHandle);
  gamesByRoomId.delete(roomId);
  log.info('[survival] game stopped', { roomId });
  setTimeout(() => {
    survivalRoomsByCode.delete(code);
    log.info('[survival] room cleaned up', { code });
  }, 60_000);
}

// â"€â"€ Friends helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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

// â"€â"€ Party / queue business logic â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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
    // Disband â€" remove from both indexes (L-4)
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
      // No connected member â€" disband
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

// â"€â"€ Party helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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

// â"€â"€ Socket.IO connection â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
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

  // â"€â"€ Reconnect â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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
        team: room.playerTeamsBySocketId?.get(sid) || "red",
      }));
      const aiPlayers = (room.aiPlayers || []).map(ai => ({
        laneIndex: ai.laneIndex,
        displayName: "CPU (" + capFirst(ai.difficulty) + ")",
        ready: true, isAI: true,
        team: ai.team || "red",
      }));
      const laneAssignments = [...humanPlayers, ...aiPlayers].sort((a, b) => a.laneIndex - b.laneIndex);

      // Send ack first so client sets myLaneIndex before ml_match_ready fires
      socket.emit("rejoin_ack", { success: true, mode: "multilane", laneIndex, code });
      socket.emit("ml_match_ready", { code, playerCount: laneAssignments.length, laneAssignments });
      const _reconnectConfig = simMl.createMLPublicConfig(room.settings);
      if (room.loadoutByLane && room.loadoutByLane[laneIndex]) {
        _reconnectConfig.loadout = room.loadoutByLane[laneIndex];
      }
      socket.emit("ml_match_config", _reconnectConfig);
      socket.emit("ml_state_snapshot", simMl.createMLSnapshot(entry.game));
      io.to(roomId).emit("player_reconnected", { laneIndex, displayName: playerName, mode: "multilane" });
      log.info(`[reconnect] ml lane ${laneIndex} in ${roomId}`);

    } else {
      socket.emit("rejoin_fail", { reason: "mode_unsupported" });
    }
  });

  // â"€â"€ Party events â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

  socket.on("party:create", () => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
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

  // â"€â"€ Friends events â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

  socket.on("friend:list", () => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
    if (!db) return;
    requireAuthSocket(socket, () => {
      getFriendsList(socket.playerId).then(friends => {
        socket.emit('friends_list', friends);
      }).catch(() => {});
    });
  });

  socket.on("friend:add", ({ displayName } = {}) => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) return;
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
      if (party.status !== 'idle') {
        return socket.emit('error_message', { message: 'Party must be idle to send invites.' });
      }
      if (party.members.length >= 4) {
        return socket.emit('error_message', { message: 'Party is full.' });
      }
      if (party.members.some(m => m.playerId === targetPlayerId)) {
        return socket.emit('error_message', { message: 'That player is already in your party.' });
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
        if (!tSock) return socket.emit('error_message', { message: 'That player is not online.' });
        tSock.emit('party_invite', {
          partyId,
          partyCode: party.code,
          fromPlayerId: socket.playerId,
          fromDisplayName: socket.playerDisplayName,
        });
        socket.emit('party_invite_sent', {
          playerId: targetPlayerId,
          displayName: tSock.playerDisplayName || 'Player',
        });
      }).catch(() => {});
    });
  });

  // â"€â"€ Queue events â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

  socket.on("queue:enter", ({ mode, loadoutSlot, unitTypeIds, filters = {}, displayName, settings, pvpMode } = {}) => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }

    // ── Solo modes (no auth required — guests can play solo too) ─────────────
    if (mode === 'solo_td' || mode === 'solo_t2t') {
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || 'Player');
      const normalizedSettings = normalizeMatchSettings(settings);
      socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
      socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
      const { code, roomId } = createMLRoom(socket.id, playerDisplayName, normalizedSettings);
      const room = mlRoomsByCode.get(code);
      sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: 'multilane' });
      socket.join(roomId);
      // Add bots from filters.botConfigs (default: one medium bot)
      const botCfgs = Array.isArray(filters.botConfigs) && filters.botConfigs.length > 0
        ? filters.botConfigs : [{ difficulty: 'medium' }];
      const validDiffs = ['easy', 'medium', 'hard'];
      for (const b of botCfgs) {
        const diff = validDiffs.includes(b.difficulty) ? b.difficulty : 'medium';
        const totalPlayers = room.players.length + (room.aiPlayers || []).length;
        if (totalPlayers >= 4) break;
        if (!room.aiPlayers) room.aiPlayers = [];
        room.aiPlayers.push({ laneIndex: totalPlayers, difficulty: diff, team: pickBalancedMlTeam(room) });
      }
      // autoStart=true tells the client to skip the lobby waiting panel
      socket.emit('match_found', {
        roomCode: code, laneIndex: 0,
        teammates: [playerDisplayName],
        opponents: (room.aiPlayers || []).map(ai => `CPU (${ai.difficulty})`),
        autoStart: true,
      });
      startMLGame(roomId, code, io);
      log.info('solo match started via queue:enter', { code, bots: (room.aiPlayers || []).length });
      return;
    }

    // ── Private modes (no auth required) ─────────────────────────────────────
    if (mode === 'private_td' || mode === 'private_t2t') {
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const privateCode = typeof filters.privateCode === 'string'
        ? filters.privateCode.trim().toUpperCase() : null;
      if (!privateCode) {
        // HOST: create a new private room
        const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || 'Player');
        const normalizedSettings = normalizeMatchSettings(settings);
        const { code, roomId } = createMLRoom(socket.id, playerDisplayName, normalizedSettings);
        const room = mlRoomsByCode.get(code);
        room.pvpMode = pvpMode === 'ffa' ? 'ffa' : '2v2';
        socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
        socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
        sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: 'multilane' });
        socket.join(roomId);
        socket.emit('ml_room_created', { code, laneIndex: 0, displayName: playerDisplayName, settings: room.settings });
        io.to(roomId).emit('ml_lobby_update', mlLobbyUpdatePayload(code, room));
        log.info('private room created via queue:enter', { code });
      } else {
        // GUEST: join an existing private room by code
        const room = mlRoomsByCode.get(privateCode);
        if (!room) return socket.emit('error_message', { message: 'Room not found.' });
        const totalInRoom = room.players.length + (room.aiPlayers || []).length;
        if (totalInRoom >= 4) return socket.emit('error_message', { message: 'Room is full (max 4).' });
        if (gamesByRoomId.has(room.roomId)) return socket.emit('error_message', { message: 'Game already started.' });
        const laneIndex = room.players.length;
        const assignedTeam = room.pvpMode === 'ffa' ? ffaTeamForLane(laneIndex) : pickBalancedMlTeam(room);
        room.players.push(socket.id);
        room.laneBySocketId.set(socket.id, laneIndex);
        room.playerTeamsBySocketId.set(socket.id, assignedTeam);
        const playerDisplayName = sanitizeDisplayName(displayName || socket.playerDisplayName || 'Player');
        room.playerNames.set(socket.id, playerDisplayName);
        if (room.aiPlayers) room.aiPlayers.forEach((ai, i) => { ai.laneIndex = room.players.length + i; });
        socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
        socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
        sessionBySocketId.set(socket.id, { code: privateCode, roomId: room.roomId, laneIndex, mode: 'multilane' });
        socket.join(room.roomId);
        socket.emit('ml_room_joined', { code: privateCode, laneIndex, displayName: playerDisplayName });
        io.to(room.roomId).emit('ml_lobby_update', mlLobbyUpdatePayload(privateCode, room));
        log.info('joined private room via queue:enter', { code: privateCode });
      }
      return;
    }

    // ── Ranked / Casual (auth required) ──────────────────────────────────────
    requireAuthSocket(socket, () => {
      if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
      const validModes = ["ranked_2v2", "casual_2v2"];
      const queueMode = validModes.includes(mode) ? mode : "casual_2v2";
      const dbMode = queueMode === "ranked_2v2" ? "2v2_ranked" : "2v2_casual";

      // Auto-create a solo party if the player isn't already in one
      let partyId = partyByPlayerId.get(socket.playerId);
      if (!partyId) {
        let code;
        do { code = generatePartyCode(); } while (partiesByCode.has(code));
        partyId = crypto.randomUUID();
        const soloParty = {
          id: partyId,
          code,
          leaderId: socket.playerId,
          members: [{ playerId: socket.playerId, displayName: socket.playerDisplayName || "Player", socketId: socket.id }],
          status: "idle",
          queueMode: null,
          queueEnteredAt: null,
          region: "global",
        };
        partiesById.set(partyId, soloParty);
        partiesByCode.set(code, partyId);
        partyByPlayerId.set(socket.playerId, partyId);
        socket.join("party:" + partyId);
      }
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

        // Store loadout preference on socket for use at match start
        socket.pendingLoadoutSlot  = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
        socket.pendingUnitTypeIds  = Array.isArray(unitTypeIds) ? unitTypeIds : null;

        const isPlacement = queueMode === "ranked_2v2" && rankedMatches < 10;
        matchmaker.addToQueue(partyId, {
          mode: queueMode,
          rating,
          queueEnteredAt: party.queueEnteredAt,
          region: party.region,
          isPlacement,
        });

        const queueSize = matchmaker.getQueuePlayerCount(queueMode, partiesById);
        io.to("party:" + partyId).emit("queue_status", { status: "queued", mode: queueMode, elapsed: 0, queueSize });
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


  // â"€â"€ Multi-Lane lobby events â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

  socket.on("create_ml_room", ({ displayName, settings, pvpMode, loadoutSlot, unitTypeIds } = {}) => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
    const normalizedSettings = normalizeMatchSettings(settings);
    const { code, roomId } = createMLRoom(socket.id, displayName, normalizedSettings);
    socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
    socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
    sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "multilane" });
    socket.join(roomId);
    const room = mlRoomsByCode.get(code);
    room.pvpMode = pvpMode === 'ffa' ? 'ffa' : '2v2';
    // For FFA, host (lane 0) gets 'red'; createMLRoom already set that so no change needed.
    socket.emit("ml_room_created", {
      code,
      laneIndex: 0,
      displayName: String(displayName || "Player").slice(0, 20),
      settings: room.settings,
    });
    io.to(roomId).emit("ml_lobby_update", mlLobbyUpdatePayload(code, room));
  });

  socket.on("join_ml_room", ({ code, displayName, loadoutSlot, unitTypeIds } = {}) => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
    const normalized = String(code || "").trim().toUpperCase();
    if (normalized.length !== 6) return socket.emit("error_message", { message: "Invalid room code." });
    const room = mlRoomsByCode.get(normalized);
    if (!room) return socket.emit("error_message", { message: "Room not found." });
    const totalInRoom = room.players.length + (room.aiPlayers || []).length;
    if (totalInRoom >= 4) return socket.emit("error_message", { message: "Room is full (max 4)." });
    if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });

    // Humans always fill the lowest indices; AI slots are bumped above humans.
    const laneIndex = room.players.length;
    const assignedTeam = room.pvpMode === 'ffa' ? ffaTeamForLane(laneIndex) : pickBalancedMlTeam(room);
    room.players.push(socket.id);
    room.laneBySocketId.set(socket.id, laneIndex);
    room.playerTeamsBySocketId.set(socket.id, assignedTeam);
    room.playerNames.set(socket.id, sanitizeDisplayName(displayName));
    // Shift any existing AI players above the new human count
    if (room.aiPlayers) {
      room.aiPlayers.forEach((ai, i) => { ai.laneIndex = room.players.length + i; });
    }

    socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
    socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
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
    // FFA rooms skip auto-start â€" host uses force-start to control when the game begins
    const totalPlayers = room.players.length + (room.aiPlayers || []).length;
    const teamCheck = validateMlTeamSetup(room);
    if (!teamCheck.ok) {
      return socket.emit("error_message", { message: teamCheck.reason });
    }
    if (room.pvpMode !== 'ffa' && totalPlayers >= 2 && room.readySet.size === room.players.length) {
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
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
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
    const aiTeam = room.pvpMode === 'ffa' ? ffaTeamForLane(totalPlayers) : pickBalancedMlTeam(room);
    room.aiPlayers.push({ laneIndex: totalPlayers, difficulty: diff, team: aiTeam });
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
    if (room.pvpMode === 'ffa') return; // teams are fixed per-lane in FFA

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

  // â"€â"€ Player actions (classic path below; ML path branches here) â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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

    // â"€â"€ Survival branch â"€â"€
    if (session.mode === "survival") {
      const entry = gamesByRoomId.get(session.roomId);
      if (!entry) return socket.emit("error_message", { message: "Game not started" });

      const res = simSurvival.applySurvivalAction(entry.game, session.laneIndex, { type, data });
      if (!res.ok) return socket.emit("error_message", { message: res.reason || "Action rejected" });

      const lane = entry.game.lanes[session.laneIndex];
      socket.emit("action_applied", {
        type,
        laneIndex: session.laneIndex,
        tick: entry.game.tick,
        gold: lane ? lane.gold : 0,
        income: lane ? lane.income : 0,
      });
      return;
    }

    // â"€â"€ Multi-lane branch â"€â"€
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
  });

  // â"€â"€ Survival lobby events â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

  socket.on("create_survival_room", ({ displayName, coopMode, loadoutSlot, unitTypeIds } = {}) => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
    const { code, roomId } = createSurvivalRoom(socket.id, displayName, coopMode);
    socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
    socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
    sessionBySocketId.set(socket.id, { code, roomId, laneIndex: 0, mode: "survival" });
    socket.join(roomId);
    socket.emit("survival_room_created", {
      code,
      laneIndex: 0,
      coopMode: !!coopMode,
      displayName: sanitizeDisplayName(displayName),
    });
  });

  socket.on("join_survival_room", ({ code, displayName, loadoutSlot, unitTypeIds } = {}) => {
    if (!checkLobbyRateLimit(socket.id, socket.handshake.address)) {
      return socket.emit("error_message", { message: "Too many requests. Please wait." });
    }
    if (!validateLoadoutSelection(socket, loadoutSlot, unitTypeIds)) return;
    const normalized = String(code || "").trim().toUpperCase();
    if (normalized.length !== 6) return socket.emit("error_message", { message: "Invalid room code." });
    const room = survivalRoomsByCode.get(normalized);
    if (!room) return socket.emit("error_message", { message: "Survival room not found." });
    if (!room.coopMode) return socket.emit("error_message", { message: "Room is solo-only." });
    if (room.players.length >= 2) return socket.emit("error_message", { message: "Co-op room is full." });
    if (gamesByRoomId.has(room.roomId)) return socket.emit("error_message", { message: "Game already started." });

    const laneIndex = room.players.length;
    room.players.push(socket.id);
    room.laneBySocketId.set(socket.id, laneIndex);
    room.playerNames.set(socket.id, sanitizeDisplayName(displayName));
    socket.pendingLoadoutSlot = Number.isInteger(loadoutSlot) ? loadoutSlot : null;
    socket.pendingUnitTypeIds = Array.isArray(unitTypeIds) ? unitTypeIds : null;
    sessionBySocketId.set(socket.id, { code: normalized, roomId: room.roomId, laneIndex, mode: "survival" });
    socket.join(room.roomId);

    socket.emit("survival_room_joined", { code: normalized, laneIndex, displayName: room.playerNames.get(socket.id) });
    io.to(room.roomId).emit("survival_lobby_update", {
      code: normalized,
      players: room.players.map(sid => ({
        laneIndex: room.laneBySocketId.get(sid),
        displayName: room.playerNames.get(sid) || "Player",
        ready: room.readySet.has(sid),
      })),
      coopMode: room.coopMode,
    });
  });

  socket.on("survival_player_ready", ({ code } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "survival") return;
    const normalized = String(code || session.code || "").trim().toUpperCase();
    const room = survivalRoomsByCode.get(normalized);
    if (!room || !room.players.includes(socket.id)) return;

    room.readySet.add(socket.id);
    io.to(room.roomId).emit("survival_lobby_update", {
      code: normalized,
      players: room.players.map(sid => ({
        laneIndex: room.laneBySocketId.get(sid),
        displayName: room.playerNames.get(sid) || "Player",
        ready: room.readySet.has(sid),
      })),
      coopMode: room.coopMode,
    });

    // Auto-start when all players are ready
    if (room.readySet.size >= room.players.length && room.players.length >= 1) {
      startSurvivalGame(room.roomId, normalized, io);
    }
  });

  socket.on("survival_force_start", ({ code } = {}) => {
    const session = sessionBySocketId.get(socket.id);
    if (!session || session.mode !== "survival") return;
    const normalized = String(code || session.code || "").trim().toUpperCase();
    const room = survivalRoomsByCode.get(normalized);
    if (!room) return;
    if (room.hostSocketId !== socket.id) {
      return socket.emit("error_message", { message: "Only the host can force start." });
    }
    if (!gamesByRoomId.has(room.roomId)) {
      startSurvivalGame(room.roomId, normalized, io);
    }
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
    // Non-ML sessions have no room to clean up
    sessionBySocketId.delete(socket.id);
    socket.emit("left_game_ack");
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

    // Survival room cleanup
    for (const [code, room] of survivalRoomsByCode.entries()) {
      const idx = room.players.indexOf(socket.id);
      if (idx !== -1) {
        room.players.splice(idx, 1);
        room.laneBySocketId.delete(socket.id);
        room.playerNames.delete(socket.id);
        room.readySet.delete(socket.id);
        if (room.players.length === 0) {
          stopSurvivalGame(room.roomId, code);
          survivalRoomsByCode.delete(code);
        }
        break;
      }
    }

    // ML room cleanup â€" grace period if mid-game, immediate otherwise
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
  sendClientFile(res, "index.html");
});

app.get("/admin", (_req, res) => {
  sendClientFile(res, "admin.html");
});

app.get("/terms", (_req, res) => {
  sendClientFile(res, "terms.html");
});

app.get("/privacy", (_req, res) => {
  sendClientFile(res, "privacy.html");
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
  // â"€â"€ Startup security checks â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
  if (!process.env.JWT_SECRET) {
    log.warn('JWT_SECRET is not set â€" authentication will not work securely');
  }
  if (process.env.NODE_ENV === 'production' && !process.env.ALLOWED_ORIGIN) {
    log.warn('ALLOWED_ORIGIN not set in production â€" CORS allows all origins');
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
      const stderr = Buffer.isBuffer(err?.stderr) ? err.stderr.toString('utf8') : String(err?.stderr || '');
      const stdout = Buffer.isBuffer(err?.stdout) ? err.stdout.toString('utf8') : String(err?.stdout || '');
      log.error('migration failed', {
        message: err?.message || String(err),
        code: err?.code || null,
        status: err?.status ?? null,
        signal: err?.signal || null,
        stderr: stderr || null,
        stdout: stdout || null,
        stack: err?.stack || null,
      });
      // Don't abort â€" server can still serve the game without DB
    }
  }

  // Load unit types cache after migrations
  if (db) {
    unitTypes.loadUnitTypes(db).catch(err =>
      log.warn('unit types load failed', {
        err: err && (err.stack || err.message || String(err)),
      })
    );
    barracksLevels.loadBarracksLevels(db).catch(err =>
      log.warn('barracks levels load failed', {
        err: err && (err.stack || err.message || String(err)),
      })
    );
  }

  // Start in-memory matchmaking loop
  matchmaker.startMatchmakingLoop(io, partiesById, socketByPlayerId, onMatchFound);

  server.listen(PORT, HOST, () => {
    log.info('server started', { port: PORT, host: HOST, env: process.env.NODE_ENV || 'development' });
  });
}

// â"€â"€ Graceful shutdown â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
// On SIGTERM / SIGINT: stop game loops, disconnect sockets, close DB pool, exit.

function gracefulShutdown(signal) {
  log.info('graceful shutdown', { signal });

  // Stop matchmaking loop
  matchmaker.stopMatchmakingLoop();

  // Stop all active game tick loops.
  for (const [, entry] of gamesByRoomId.entries()) {
    clearInterval(entry.tickHandle);
    if (entry.botController && typeof entry.botController.stop === "function") {
      try { entry.botController.stop(); } catch { /* ignore */ }
    }
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

  // Close socket.io â†' HTTP server â†' DB pool â†' exit
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

