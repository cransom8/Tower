"use strict";

function createRuntimeState(app) {
  const state = {
    mlRoomsByCode: new Map(),
    survivalRoomsByCode: new Map(),
    sessionBySocketId: new Map(),
    gamesByRoomId: new Map(),
    partiesById: new Map(),
    partyByPlayerId: new Map(),
    socketByPlayerId: new Map(),
    partiesByCode: new Map(),
    disconnectGrace: new Map(),
    rateLimitBySocketId: new Map(),
    lobbyRateLimitByIp: new Map(),
    playerBanCache: new Map(),
    RECONNECT_GRACE_MS: 120_000,
    PLAYER_BAN_CACHE_TTL_MS: 5 * 60 * 1000,
  };

  app.locals.partiesById = state.partiesById;
  app.locals.partyByPlayerId = state.partyByPlayerId;
  app.locals.gamesByRoomId = state.gamesByRoomId;
  app.locals.mlRoomsByCode = state.mlRoomsByCode;
  app.locals.survivalRoomsByCode = state.survivalRoomsByCode;
  app.locals.socketByPlayerId = state.socketByPlayerId;
  app.locals.playerBanCache = state.playerBanCache;

  setInterval(() => {
    const cutoff = Date.now() - 60_000;
    for (const [ip, record] of state.lobbyRateLimitByIp) {
      if (record.windowStart < cutoff) state.lobbyRateLimitByIp.delete(ip);
    }
  }, 60_000).unref();

  setInterval(() => {
    const cutoff = Date.now() - state.PLAYER_BAN_CACHE_TTL_MS;
    for (const [playerId, record] of state.playerBanCache) {
      if (record.cachedAt < cutoff) state.playerBanCache.delete(playerId);
    }
  }, 60_000).unref();

  return state;
}

module.exports = { createRuntimeState };
