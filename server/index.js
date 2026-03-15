// server/index.js
"use strict";

require("dotenv").config();

const express = require("express");
const http = require("http");
const cors = require("cors");
const fs = require("fs");
const path = require("path");
const { Server } = require("socket.io");

const authService = require("./auth");
const barracksLevels = require("./barracksLevels");
const branding = require("./branding");
const gameConfig = require("./gameConfig");
const log = require("./logger");
const matchmaker = require("./services/matchQueueManager");
const simMl = require("./sim-multilane");
const { buildContentManifest, formatPublicUnitType } = require("./remoteContent");
const unitTypes = require("./unitTypes");
const { stringToSeed } = require("./sim-core");
const { createLoadoutHelpers } = require("./game/loadoutHelpers");
const { createMatchPersistence } = require("./game/matchPersistence");
const { createMultilaneRuntime } = require("./game/multilaneRuntime");
const {
  createCodeGenerator,
  createRateLimiters,
  createReconnectTokenHelpers,
  installSocketAuth,
  normalizeMatchSettings,
  requireAuthSocket,
  sanitizeDisplayName,
} = require("./socket/helpers");
const { registerSocketHandlers } = require("./socket/registerHandlers");
const { createRuntimeState } = require("./state/runtimeState");

let aiRuntime;
try {
  aiRuntime = require("./ai/sim_runner");
} catch (_err) {
  try {
    aiRuntime = require("../ai/sim_runner");
  } catch (_err2) {
    const legacyAi = require("./ai");
    aiRuntime = {
      __engine: "legacy_ai_adapter_v1",
      createBotController(config) {
        const cfg = config && typeof config === "object" ? config : {};
        const game = cfg.game;
        const botConfigs = Array.isArray(cfg.botConfigs) ? cfg.botConfigs : [];
        const handles = botConfigs.map((bot) => legacyAi.startAI(game, bot.laneIndex, bot.difficulty));
        return {
          runtimeTracker: null,
          getBotCount() { return handles.length; },
          onBeforeSimTick() {},
          onAfterSimTick() {},
          drainActionLog() { return []; },
          stop() { handles.forEach((handle) => legacyAi.stopAI(handle)); },
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
    process.stderr.write(
      JSON.stringify({
        level: "warn",
        ts: new Date().toISOString(),
        msg: "[ai] new ai runtime module not found; using legacy adapter fallback",
      }) + "\n"
    );
  }
}

const db = process.env.DATABASE_URL ? require("./db") : null;
const ratingService = process.env.DATABASE_URL ? require("./services/rating") : null;
const seasonService = process.env.DATABASE_URL ? require("./services/season") : null;
const { createFeatureFlagCache } = require("./services/featureFlagCache");
const featureFlagCache = createFeatureFlagCache(db);

if (db) {
  gameConfig.loadActiveConfigs(db).catch((err) =>
    log.warn("game config load failed, using hardcoded defaults", {
      err: err && (err.stack || err.message || String(err)),
      code: err?.code || null,
      name: err?.name || null,
    })
  );
}

const app = express();
app.locals.db = db;

const corsOrigin = process.env.ALLOWED_ORIGIN || "http://localhost:3000";
if (corsOrigin === "*" || corsOrigin.includes("*")) {
  throw new Error("[startup] ALLOWED_ORIGIN cannot be a wildcard when credentials=true");
}

app.use(cors({
  origin: corsOrigin,
  methods: ["GET", "POST", "PATCH"],
  credentials: true,
}));
app.use(express.json({ limit: "8mb" }));
app.use((req, _res, next) => {
  const cookies = {};
  for (const pair of String(req.headers.cookie || "").split(";")) {
    const eq = pair.indexOf("=");
    if (eq === -1) continue;
    const key = pair.slice(0, eq).trim();
    const value = pair.slice(eq + 1).trim();
    try {
      cookies[key] = decodeURIComponent(value);
    } catch {
      cookies[key] = value;
    }
  }
  req.cookies = cookies;
  next();
});

app.use((req, res, next) => {
  const isAdminDocument = req.path === "/admin.html" || req.path === "/admin";
  const isUnityDocument = req.path === "/" || req.path === "/index.html";
  const isUnityAsset    = req.path.startsWith("/client") || req.path.startsWith("/Build/") || req.path.startsWith("/TemplateData/");
  const isUnityClient   = isUnityDocument || isUnityAsset;
  const scriptSrc = isAdminDocument
    ? "script-src 'self' 'unsafe-inline' https://accounts.google.com https://cdn.socket.io; "
    : isUnityClient
    ? "script-src 'self' 'unsafe-inline' 'unsafe-eval' blob: https://accounts.google.com https://cdn.socket.io; "
    : "script-src 'self' https://accounts.google.com https://cdn.socket.io; ";

  if (isAdminDocument) {
    res.setHeader("Cross-Origin-Opener-Policy", "same-origin-allow-popups");
    res.setHeader("Cross-Origin-Embedder-Policy", "unsafe-none");
  }

  res.setHeader("X-Frame-Options", "SAMEORIGIN");
  res.setHeader("X-Content-Type-Options", "nosniff");
  res.setHeader("X-XSS-Protection", "1; mode=block");
  res.setHeader("Referrer-Policy", "strict-origin-when-cross-origin");
  const hstsTtl = process.env.NODE_ENV === "production" ? 31536000 : 3600;
  res.setHeader("Strict-Transport-Security", `max-age=${hstsTtl}; includeSubDomains; preload`);
  const connectSrc = isUnityClient
    ? "connect-src 'self' wss: ws: blob: https://accounts.google.com https://cdn.socket.io; "
    : "connect-src 'self' wss: ws: https://accounts.google.com https://cdn.socket.io; ";
  const workerSrc = isUnityClient ? "worker-src blob:; " : "";
  res.setHeader(
    "Content-Security-Policy",
    "default-src 'self'; " +
      scriptSrc +
      connectSrc +
      workerSrc +
      "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://accounts.google.com; " +
      "font-src 'self' https://fonts.gstatic.com; " +
      "img-src 'self' data: https:; " +
      "frame-src https://accounts.google.com; " +
      "frame-ancestors 'self';"
  );
  next();
});

app.use("/auth", require("./routes/auth"));
app.use("/players", require("./routes/players"));
app.use("/parties", require("./routes/parties"));
app.use("/queue", require("./routes/queue"));
app.use("/matches", require("./routes/matches"));
app.use("/leaderboard", require("./routes/leaderboard"));
app.use("/admin", require("./routes/admin"));
app.use("/api/loadouts", require("./routes/loadouts"));
app.use("/api/skins",   require("./routes/skins"));

app.get("/config", async (_req, res) => {
  const uiBranding = await branding.getBranding(db);
  res.json({
    googleClientId: process.env.GOOGLE_CLIENT_ID || "",
    passwordAuthEnabled: !!db,
    branding: uiBranding,
  });
});

// Legacy compatibility endpoint. The current Unity client no longer fetches
// tower catalog data at startup, but we are keeping this route for future
// base/camp work and any older tools that still expect it.
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
      ORDER BY t.id
    `);
    res.json({ towers: rows.rows });
  } catch (err) {
    log.error("[api] GET /api/towers error", { err: err.message });
    res.json({ towers: [] });
  }
});

app.get("/api/unit-types", async (_req, res) => {
  const all = unitTypes
    .getAllUnitTypes()
    .filter((ut) => ut.enabled && ut.display_to_players !== false)
    .map((ut) => formatPublicUnitType(ut));
  let displayFields = null;
  if (db) {
    try {
      const result = await db.query("SELECT * FROM unit_display_fields ORDER BY sort_order");
      displayFields = result.rows;
    } catch {
      // Table may not exist until migration 026 runs.
    }
  }
  res.json({ unitTypes: all, displayFields });
});

app.get("/api/content-manifest", async (_req, res) => {
  const allUnitTypes = unitTypes
    .getAllUnitTypes()
    .filter((ut) => ut.enabled && ut.display_to_players !== false);
  let skins = [];

  if (db) {
    try {
      const result = await db.query(
        `SELECT s.skin_key, s.unit_type, s.name, s.enabled,
                CASE
                  WHEN scm.id IS NULL THEN NULL
                  ELSE json_build_object(
                    'id', scm.id,
                    'content_key', scm.content_key,
                    'addressables_label', scm.addressables_label,
                    'prefab_address', scm.prefab_address,
                    'placeholder_key', scm.placeholder_key,
                    'catalog_url', scm.catalog_url,
                    'content_url', scm.content_url,
                    'version_tag', scm.version_tag,
                    'content_hash', scm.content_hash,
                    'dependency_keys', scm.dependency_keys,
                    'metadata', scm.metadata,
                    'is_critical', scm.is_critical,
                    'enabled', scm.enabled
                  )
                END AS remote_content
           FROM skin_catalog s
           LEFT JOIN skin_content_metadata scm ON scm.skin_catalog_id = s.id
          WHERE s.enabled = true
          ORDER BY s.unit_type, s.name`
      );
      skins = result.rows;
    } catch (err) {
      log.error("[api] GET /api/content-manifest skins query error", { err: err.message });
    }
  }

  res.json(buildContentManifest({ unitTypes: allUnitTypes, skins }));
});

app.get("/api/barracks-levels", async (_req, res) => {
  if (!db) return res.json([]);
  try {
    const rows = await db.query(
      `SELECT level, multiplier, upgrade_cost, notes
         FROM barracks_levels
        ORDER BY level`
    );
    res.json(rows.rows);
  } catch (err) {
    log.error("[api] GET /api/barracks-levels error", { err: err.message });
    res.json([]);
  }
});

const unityClientDirCandidates = [
  path.join(__dirname, "client"),
  path.join(__dirname, "unity-client"),
  path.join(__dirname, "client_backup_20260307_002446"),
  path.join(__dirname, "client_backup_20260306_235052"),
];

const adminClientDirCandidates = [
  path.join(__dirname, "..", "admin-client"),
  path.join(__dirname, "admin-client"),
  path.join(__dirname, "client_backup_20260306_233552"),
];

const adminAssetDirCandidates = [
  path.join(__dirname, "..", "admin-client", "assets"),
  path.join(__dirname, "admin-client", "assets"),
  path.join(__dirname, "client_backup_20260306_233552", "assets"),
];

const unityAddressablesDirCandidates = [
  path.join(__dirname, "..", "unity-client", "ServerData"),
  path.join(__dirname, "unity-client", "ServerData"),
];

// Unity WebGL build is served from the dedicated Unity client path.
const unityClientDir =
  unityClientDirCandidates.find((dir) => fs.existsSync(path.join(dir, "index.html"))) ||
  unityClientDirCandidates[0];
log.info("unity client dir", { unityClientDir });

const adminClientDir =
  adminClientDirCandidates.find((dir) => fs.existsSync(path.join(dir, "admin.html"))) ||
  adminClientDirCandidates[0];
log.info("admin client dir", { adminClientDir });

const adminAssetDir =
  adminAssetDirCandidates.find((dir) => fs.existsSync(dir)) ||
  adminAssetDirCandidates[0];
log.info("admin asset dir", { adminAssetDir });

const unityAddressablesDir =
  unityAddressablesDirCandidates.find((dir) => fs.existsSync(dir)) ||
  unityAddressablesDirCandidates[0];
log.info("unity addressables dir", { unityAddressablesDir });

// Unity WebGL Brotli middleware — must run BEFORE express.static.
// Unity builds output .js.br / .wasm.br / .data.br files that need
// Content-Encoding: br + the right Content-Type for the browser to handle them.
const unityBrExtMap = {
  // Legacy .br / .gz Unity builds
  '.js.br':              { encoding: 'br',   type: 'application/javascript' },
  '.wasm.br':            { encoding: 'br',   type: 'application/wasm' },
  '.data.br':            { encoding: 'br',   type: 'application/octet-stream' },
  '.js.gz':              { encoding: 'gzip', type: 'application/javascript' },
  '.wasm.gz':            { encoding: 'gzip', type: 'application/wasm' },
  '.data.gz':            { encoding: 'gzip', type: 'application/octet-stream' },
  // Unity 2022+ .unityweb (Brotli by default)
  '.framework.js.unityweb': { encoding: 'br', type: 'application/javascript' },
  '.wasm.unityweb':         { encoding: 'br', type: 'application/wasm' },
  '.data.unityweb':         { encoding: 'br', type: 'application/octet-stream' },
  '.loader.js':             { encoding: null, type: 'application/javascript' },
};

function unityWebGLMiddleware(baseDir) {
  return (req, res, next) => {
    const url = req.path;
    for (const [ext, meta] of Object.entries(unityBrExtMap)) {
      if (url.endsWith(ext)) {
        if (meta.encoding) res.setHeader('Content-Encoding', meta.encoding);
        res.setHeader('Content-Type', meta.type);
        break;
      }
    }
    next();
  };
}

app.use(unityWebGLMiddleware(unityClientDir), express.static(unityClientDir, { index: false }));
app.use("/client", unityWebGLMiddleware(unityClientDir), express.static(unityClientDir, { index: false }));
app.use("/addressables", express.static(unityAddressablesDir, { index: false }));
app.use("/assets", express.static(adminAssetDir, { index: false }));

function sendUnityClientFile(res, filename) {
  const candidate = [unityClientDir, ...unityClientDirCandidates]
    .map((dir) => path.join(dir, filename))
    .find((filePath) => fs.existsSync(filePath));
  if (candidate) return res.sendFile(candidate);
  return res.status(404).json({ error: `${filename} not found` });
}

function sendAdminClientFile(res, filename) {
  const candidate = [adminClientDir, ...adminClientDirCandidates]
    .map((dir) => path.join(dir, filename))
    .find((filePath) => fs.existsSync(filePath));
  if (candidate) return res.sendFile(candidate);
  return res.status(404).json({ error: `${filename} not found` });
}

const server = http.createServer(app);
const io = new Server(server, {
  maxHttpBufferSize: 1e4,
  cors: {
    origin: corsOrigin,
    methods: ["GET", "POST"],
    credentials: true,
  },
});

app.locals.io = io;

const runtimeState = createRuntimeState(app);
const generateCode = createCodeGenerator();
const { checkLobbyRateLimit, checkActionRateLimit } = createRateLimiters(runtimeState);
const { issueReconnectToken, verifyReconnectToken } = createReconnectTokenHelpers(process.env.JWT_SECRET);
const { resolveLoadout, validateLoadoutSelection, hasValidInlineLoadoutIds } = createLoadoutHelpers({ db, unitTypes });
const { logMatchStart, logMatchEnd } = createMatchPersistence({ db, log });

installSocketAuth(io, {
  authService,
  db,
  playerBanCache: runtimeState.playerBanCache,
  PLAYER_BAN_CACHE_TTL_MS: runtimeState.PLAYER_BAN_CACHE_TTL_MS,
});

const multilaneRuntime = createMultilaneRuntime({
  aiRuntime,
  db,
  getAllUnitTypes: unitTypes.getAllUnitTypes,
  disconnectGrace: runtimeState.disconnectGrace,
  generateCode,
  gamesByRoomId: runtimeState.gamesByRoomId,
  io,
  issueReconnectToken,
  log,
  logMatchEnd,
  logMatchStart,
  mlRoomsByCode: runtimeState.mlRoomsByCode,
  normalizeMatchSettings,
  ratingService,
  resolveLoadout,
  sanitizeDisplayName,
  seasonService,
  sessionBySocketId: runtimeState.sessionBySocketId,
  simMl,
  socketByPlayerId: runtimeState.socketByPlayerId,
  stringToSeed,
  RECONNECT_GRACE_MS: runtimeState.RECONNECT_GRACE_MS,
});

const { onMatchFound } = registerSocketHandlers({
  authService,
  checkActionRateLimit,
  checkLobbyRateLimit,
  createMLRoom: multilaneRuntime.createMLRoom,
  db,
  disconnectGrace: runtimeState.disconnectGrace,
  ffaTeamForLane: multilaneRuntime.ffaTeamForLane,
  gamesByRoomId: runtimeState.gamesByRoomId,
  generateCode,
  io,
  log,
  matchmaker,
  mlLobbyUpdatePayload: multilaneRuntime.mlLobbyUpdatePayload,
  mlRoomsByCode: runtimeState.mlRoomsByCode,
  normalizeMatchSettings,
  pickBalancedMlTeam: multilaneRuntime.pickBalancedMlTeam,
  partiesByCode: runtimeState.partiesByCode,
  partiesById: runtimeState.partiesById,
  partyByPlayerId: runtimeState.partyByPlayerId,
  requireAuthSocket,
  sanitizeDisplayName,
  sessionBySocketId: runtimeState.sessionBySocketId,
  simMl,
  socketByPlayerId: runtimeState.socketByPlayerId,
  buildAvailableUnits: multilaneRuntime.buildAvailableUnits,
  buildRematchStatus: multilaneRuntime.buildRematchStatus,
  hasValidInlineLoadoutIds,
  startMLGame: multilaneRuntime.startMLGame,
  stopMLGame: multilaneRuntime.stopMLGame,
  validateLoadoutSelection,
  validateMlTeamSetup: multilaneRuntime.validateMlTeamSetup,
  verifyReconnectToken,
  applyMultilaneAction: multilaneRuntime.applyMultilaneAction,
  RECONNECT_GRACE_MS: runtimeState.RECONNECT_GRACE_MS,
  isEnabled: featureFlagCache.isEnabled,
});

app.locals.terminateMatch = async function terminateMatch(roomId) {
  const entry = runtimeState.gamesByRoomId.get(roomId);
  if (!entry) return false;
  clearInterval(entry.tickHandle);
  if (entry.botController && typeof entry.botController.stop === "function") {
    try {
      entry.botController.stop();
    } catch {
      // Ignore bot shutdown failures for admin termination.
    }
  }
  runtimeState.gamesByRoomId.delete(roomId);
  io.to(roomId).emit("game_over", { winner: null, reason: "admin_terminated" });
  io.to(roomId).emit("ml_game_over", { winnerLaneIndex: null, winnerName: null, reason: "admin_terminated" });
  if (db && entry.matchIdPromise) {
    const matchId = await Promise.resolve(entry.matchIdPromise).catch(() => null);
    if (matchId) {
      await db.query("UPDATE matches SET status='abandoned', ended_at=NOW() WHERE id=$1", [matchId]).catch(() => {});
    }
  }
  log.info("match terminated by admin", { roomId });
  return true;
};

app.get("/", (_req, res) => sendUnityClientFile(res, "index.html"));
app.get("/admin", (_req, res) => sendAdminClientFile(res, "admin.html"));
app.get("/admin.html", (_req, res) => sendAdminClientFile(res, "admin.html"));
app.get("/admin.css", (_req, res) => sendAdminClientFile(res, "admin.css"));
app.get("/admin.js", (_req, res) => sendAdminClientFile(res, "admin.js"));
app.get("/render/assets.js", (_req, res) => sendAdminClientFile(res, path.join("render", "assets.js")));
app.get("/terms", (_req, res) => res.status(410).type("text/plain").send("Legacy web client removed. Use the Unity client instead."));
app.get("/privacy", (_req, res) => res.status(410).type("text/plain").send("Legacy web client removed. Use the Unity client instead."));
app.get("/terms-of-service", (_req, res) => res.status(410).type("text/plain").send("Legacy web client removed. Use the Unity client instead."));
app.get("/privacy-policy", (_req, res) => res.status(410).type("text/plain").send("Legacy web client removed. Use the Unity client instead."));

app.get("/health", async (_req, res) => {
  if (process.env.DATABASE_URL) {
    try {
      const liveDb = require("./db");
      await liveDb.query("SELECT 1");
      return res.json({ ok: true, db: "connected" });
    } catch {
      return res.status(503).json({ ok: false, db: "error" });
    }
  }
  return res.json({ ok: true, db: "none" });
});

const PORT = process.env.PORT || 3000;
const HOST = process.env.HOST || "0.0.0.0";

async function startServer() {
  if (!process.env.JWT_SECRET) {
    log.warn("JWT_SECRET is not set - authentication will not work securely");
  }
  if (process.env.NODE_ENV === "production" && !process.env.ALLOWED_ORIGIN) {
    log.warn("ALLOWED_ORIGIN not set in production - CORS allows all origins");
  }

  if (process.env.DATABASE_URL) {
    try {
      const { execSync } = require("child_process");
      execSync(`node "${path.join(__dirname, "migrate.js")}"`, {
        stdio: "inherit",
        env: process.env,
      });
    } catch (err) {
      const stderr = Buffer.isBuffer(err?.stderr) ? err.stderr.toString("utf8") : String(err?.stderr || "");
      const stdout = Buffer.isBuffer(err?.stdout) ? err.stdout.toString("utf8") : String(err?.stdout || "");
      log.error("migration failed", {
        message: err?.message || String(err),
        code: err?.code || null,
        status: err?.status ?? null,
        signal: err?.signal || null,
        stderr: stderr || null,
        stdout: stdout || null,
        stack: err?.stack || null,
      });
    }
  }

  if (db) {
    unitTypes.loadUnitTypes(db).catch((err) => log.warn("unit types load failed", { err: err && (err.stack || err.message || String(err)) }));
    barracksLevels.loadBarracksLevels(db).catch((err) => log.warn("barracks levels load failed", { err: err && (err.stack || err.message || String(err)) }));
    featureFlagCache.start();
  }

  matchmaker.startMatchmakingLoop(io, runtimeState.partiesById, runtimeState.socketByPlayerId, onMatchFound);
  server.listen(PORT, HOST, () => {
    log.info("server started", { port: PORT, host: HOST, env: process.env.NODE_ENV || "development" });
  });
}

function gracefulShutdown(signal) {
  log.info("graceful shutdown", { signal });
  featureFlagCache.stop();
  matchmaker.stopMatchmakingLoop();

  for (const [, entry] of runtimeState.gamesByRoomId.entries()) {
    clearInterval(entry.tickHandle);
    if (entry.botController && typeof entry.botController.stop === "function") {
      try {
        entry.botController.stop();
      } catch {
        // Ignore AI shutdown failures during process exit.
      }
    }
  }
  runtimeState.gamesByRoomId.clear();

  for (const [, grace] of runtimeState.disconnectGrace.entries()) {
    clearTimeout(grace.graceHandle);
  }
  runtimeState.disconnectGrace.clear();

  io.emit("server_shutdown", { message: "Server is restarting. Please refresh in a moment." });

  const forceExit = setTimeout(() => {
    log.warn("forced exit after shutdown timeout");
    process.exit(1);
  }, 10_000);
  forceExit.unref();

  io.close(() => {
    server.close(async () => {
      if (db) {
        try {
          await db.pool.end();
        } catch {
          // Ignore pool shutdown failures during exit.
        }
      }
      log.info("server stopped cleanly");
      process.exit(0);
    });
  });
}

process.on("SIGTERM", () => gracefulShutdown("SIGTERM"));
process.on("SIGINT", () => gracefulShutdown("SIGINT"));

startServer();
