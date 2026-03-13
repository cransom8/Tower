"use strict";

const crypto = require("crypto");
const jwt = require("jsonwebtoken");

function parseCookies(cookieStr) {
  const out = {};
  for (const pair of String(cookieStr || "").split(";")) {
    const eq = pair.indexOf("=");
    if (eq === -1) continue;
    const key = pair.slice(0, eq).trim();
    const value = pair.slice(eq + 1).trim();
    try {
      out[key] = decodeURIComponent(value);
    } catch {
      out[key] = value;
    }
  }
  return out;
}

function installSocketAuth(io, { authService, db, playerBanCache, PLAYER_BAN_CACHE_TTL_MS }) {
  io.use(async (socket, next) => {
    const cookies = parseCookies(socket.handshake.headers?.cookie);
    const token = socket.handshake.auth?.token || cookies.cd_access || null;
    if (token) {
      try {
        const payload = authService.verifyAccessToken(token);
        socket.playerId = payload.sub;
        socket.playerDisplayName = payload.displayName;
        if (db) {
          try {
            const cached = playerBanCache.get(payload.sub);
            if (cached && Date.now() - cached.cachedAt < PLAYER_BAN_CACHE_TTL_MS) {
              if (cached.suspended) socket.playerBanned = true;
            } else {
              const result = await db.query("SELECT status FROM players WHERE id = $1", [payload.sub]);
              const suspended = result.rows[0]?.status === "suspended";
              playerBanCache.set(payload.sub, { suspended, cachedAt: Date.now() });
              if (suspended) socket.playerBanned = true;
            }
          } catch {
            // Ignore ban-cache lookup failures and continue as authenticated.
          }
        }
      } catch {
        // Invalid/expired token falls through to guest behavior.
      }
    }
    next();
  });
}

function createRateLimiters({ rateLimitBySocketId, lobbyRateLimitByIp }) {
  function ensureSocketRecord(socketId, now) {
    let record = rateLimitBySocketId.get(socketId);
    if (!record) {
      record = { lobbyCount: 0, lobbyWindowStart: now, actionCount: 0, actionWindowStart: now };
      rateLimitBySocketId.set(socketId, record);
    }
    return record;
  }

  function checkLobbyRateLimit(socketId, ip) {
    const now = Date.now();
    const record = ensureSocketRecord(socketId, now);

    if (now - record.lobbyWindowStart > 60_000) {
      record.lobbyCount = 0;
      record.lobbyWindowStart = now;
    }
    record.lobbyCount += 1;
    if (record.lobbyCount > 10) return false;

    if (ip) {
      let ipRecord = lobbyRateLimitByIp.get(ip);
      if (!ipRecord || now - ipRecord.windowStart > 60_000) {
        ipRecord = { count: 0, windowStart: now };
        lobbyRateLimitByIp.set(ip, ipRecord);
      }
      ipRecord.count += 1;
      if (ipRecord.count > 20) return false;
    }

    return true;
  }

  function checkActionRateLimit(socketId) {
    const now = Date.now();
    const record = ensureSocketRecord(socketId, now);
    if (now - record.actionWindowStart > 1_000) {
      record.actionCount = 0;
      record.actionWindowStart = now;
    }
    record.actionCount += 1;
    return record.actionCount <= 10;
  }

  return { checkLobbyRateLimit, checkActionRateLimit };
}

function createCodeGenerator() {
  return function generateCode(len = 6) {
    const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const bytes = crypto.randomBytes(len);
    return Array.from(bytes, (b) => chars[b % chars.length]).join("");
  };
}

function createReconnectTokenHelpers(jwtSecret) {
  function issueReconnectToken(roomId, code, mode, seatKey, socket) {
    return jwt.sign(
      {
        type: "reconnect",
        roomId,
        code,
        mode,
        seatKey,
        playerId: socket.playerId || null,
        guestId: socket.guestId || null,
      },
      jwtSecret,
      { algorithm: "HS256", expiresIn: "24h" }
    );
  }

  function verifyReconnectToken(token) {
    return jwt.verify(token, jwtSecret, { algorithms: ["HS256"] });
  }

  return { issueReconnectToken, verifyReconnectToken };
}

function sanitizeDisplayName(raw) {
  return String(raw || "")
    .replace(/[^a-zA-Z0-9_ ]/g, "")
    .trim()
    .slice(0, 20) || "Player";
}

const gameConfig = require("../gameConfig");

function getDefaultMatchSettings() {
  const active = gameConfig.getActiveConfig("multilane");
  const gp = active && active.globalParams ? active.globalParams : {};
  return {
    startGold: Number(gp.startGold) || 70,
    startIncome: Number(gp.startIncome) || 10,
    livesStart: Number(gp.livesStart) || 20,
    teamHpStart: Number(gp.teamHpStart) || 20,
    buildPhaseTicks: Number(gp.buildPhaseTicks) || 600,
    transitionPhaseTicks: Number(gp.transitionPhaseTicks) || 200,
  };
}

function normalizeMatchSettings(settings) {
  const src = settings && typeof settings === "object" ? settings : {};
  const defaults = getDefaultMatchSettings();
  const rawGold = Number(src.startGold);
  const rawIncome = Number(src.startIncome);
  const rawLives = Number(src.livesStart);
  const rawTeamHp = Number(src.teamHpStart);
  const rawBuildPhaseTicks = Number(src.buildPhaseTicks);
  const rawTransitionPhaseTicks = Number(src.transitionPhaseTicks);
  const startGold = Number.isFinite(rawGold)
    ? Math.max(0, Math.min(10000, Math.floor(rawGold)))
    : defaults.startGold;
  const startIncome = Number.isFinite(rawIncome)
    ? Math.max(0, Math.min(1000, rawIncome))
    : defaults.startIncome;
  const livesStart = Number.isFinite(rawLives)
    ? Math.max(1, Math.min(1000, Math.floor(rawLives)))
    : defaults.livesStart;
  const teamHpStart = Number.isFinite(rawTeamHp)
    ? Math.max(1, Math.min(1000, Math.floor(rawTeamHp)))
    : defaults.teamHpStart;
  const buildPhaseTicks = Number.isFinite(rawBuildPhaseTicks)
    ? Math.max(20, Math.min(7200, Math.floor(rawBuildPhaseTicks)))
    : defaults.buildPhaseTicks;
  const transitionPhaseTicks = Number.isFinite(rawTransitionPhaseTicks)
    ? Math.max(20, Math.min(7200, Math.floor(rawTransitionPhaseTicks)))
    : defaults.transitionPhaseTicks;
  const selectionMode = src.selectionMode === "random" ? "random" : "manual";
  return {
    startGold,
    startIncome,
    livesStart,
    teamHpStart,
    buildPhaseTicks,
    transitionPhaseTicks,
    selectionMode,
  };
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

module.exports = {
  createCodeGenerator,
  createRateLimiters,
  createReconnectTokenHelpers,
  getDefaultMatchSettings,
  installSocketAuth,
  normalizeMatchSettings,
  parseCookies,
  requireAuthSocket,
  sanitizeDisplayName,
};
