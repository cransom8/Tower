"use strict";

const ACTION_TIMELINE_SCHEMA_VERSION = 1;

function cloneJsonSafe(value) {
  if (value === undefined)
    return null;
  if (value === null || typeof value !== "object")
    return value;
  try {
    return JSON.parse(JSON.stringify(value));
  } catch {
    return null;
  }
}

function getLaneSocketId(room, laneIndex) {
  return Array.isArray(room && room.players)
    ? room.players.find((socketId) => room.laneBySocketId && room.laneBySocketId.get(socketId) === laneIndex) || null
    : null;
}

function getLaneAiEntry(room, laneIndex) {
  return (room && Array.isArray(room.aiPlayers) ? room.aiPlayers : [])
    .find((entry) => Number(entry && entry.laneIndex) === Number(laneIndex)) || null;
}

function getLanePlayerSnapshot(entry, laneIndex) {
  return (entry && Array.isArray(entry.playerSnapshot) ? entry.playerSnapshot : [])
    .find((snapshot) => Number(snapshot && snapshot.laneIndex) === Number(laneIndex)) || null;
}

function getLaneOwnerMeta(room, entry, laneIndex) {
  const aiEntry = getLaneAiEntry(room, laneIndex);
  if (aiEntry) {
    return {
      isAI: true,
      difficulty: aiEntry.difficulty || null,
      takeover: !!aiEntry.takeover,
      displayName: aiEntry.displayName || `CPU (${aiEntry.difficulty || "unknown"})`,
      playerId: null,
    };
  }

  const socketId = getLaneSocketId(room, laneIndex);
  const snapshot = getLanePlayerSnapshot(entry, laneIndex);
  return {
    isAI: false,
    difficulty: null,
    takeover: false,
    displayName: socketId && room && room.playerNames ? room.playerNames.get(socketId) || `Lane ${Number(laneIndex) + 1}` : `Lane ${Number(laneIndex) + 1}`,
    playerId: snapshot && snapshot.playerId ? snapshot.playerId : null,
  };
}

function getLaneLoadoutKeys(room, game, laneIndex) {
  const lane = game && Array.isArray(game.lanes)
    ? game.lanes.find((entry) => Number(entry && entry.laneIndex) === Number(laneIndex)) || null
    : null;
  if (lane && Array.isArray(lane.loadoutKeys) && lane.loadoutKeys.length > 0)
    return lane.loadoutKeys.map((key) => String(key)).filter(Boolean);

  const laneLoadout = room && Array.isArray(room.loadoutByLane) ? room.loadoutByLane[laneIndex] : null;
  return Array.isArray(laneLoadout)
    ? laneLoadout.map((unit) => String(unit && unit.key || "")).filter(Boolean)
    : [];
}

function recordTimelineAction(entry, room, laneIndex, action, options = null) {
  if (!entry || !room || !action || typeof action.type !== "string")
    return null;

  if (!Array.isArray(entry.actionTimeline))
    entry.actionTimeline = [];
  if (!entry.actionTimelineCounts || typeof entry.actionTimelineCounts !== "object")
    entry.actionTimelineCounts = {};

  const opt = options && typeof options === "object" ? options : {};
  const game = entry.game || null;
  const lane = game && Array.isArray(game.lanes)
    ? game.lanes.find((candidate) => Number(candidate && candidate.laneIndex) === Number(laneIndex)) || null
    : null;
  const ownerMeta = getLaneOwnerMeta(room, entry, laneIndex);
  const laneActionIndex = Math.max(0, Number(entry.actionTimelineCounts[laneIndex]) || 0) + 1;
  entry.actionTimelineCounts[laneIndex] = laneActionIndex;

  const record = {
    tick: Number.isFinite(Number(action.tickApply)) ? Number(action.tickApply) : Math.max(0, Number(game && game.tick) || 0),
    round: Math.max(0, Number(game && game.roundNumber) || 0),
    actionSeq: Number.isFinite(Number(action.actionSeq)) ? Number(action.actionSeq) : null,
    laneIndex: Number(laneIndex),
    laneActionIndex,
    actorType: ownerMeta.isAI ? "ai" : "human",
    origin: opt.origin || (ownerMeta.isAI ? "ai_runtime" : "player_socket"),
    difficulty: ownerMeta.difficulty,
    takeover: ownerMeta.takeover,
    displayName: ownerMeta.displayName,
    playerId: ownerMeta.playerId,
    team: lane ? lane.team || null : null,
    side: lane ? lane.side || null : null,
    type: String(action.type),
    data: cloneJsonSafe(action.data || {}),
  };

  entry.actionTimeline.push(record);
  return record;
}

function buildActionTimelinePayload(entry, room, normalizeMatchSettings) {
  const game = entry && entry.game;
  if (!game)
    return null;

  const settings = typeof normalizeMatchSettings === "function"
    ? normalizeMatchSettings(room && room.settings ? room.settings : {})
    : (room && room.settings ? room.settings : {});

  return {
    schemaVersion: ACTION_TIMELINE_SCHEMA_VERSION,
    mode: entry.dbMode || "multilane",
    roomId: entry.roomId || null,
    roomCode: entry.roomCode || null,
    matchSeedText: entry.matchSeedText || null,
    matchSeed: Number.isFinite(Number(game.matchSeed)) ? Number(game.matchSeed) : null,
    configVersionId: game.configVersionId != null ? game.configVersionId : null,
    battlefieldLayoutId: entry.layoutId || null,
    battlefieldLayoutHash: entry.layoutHash || null,
    playerCount: Math.max(0, Number(game.playerCount) || (Array.isArray(game.lanes) ? game.lanes.length : 0)),
    settings: cloneJsonSafe(settings),
    laneContext: (Array.isArray(game.lanes) ? game.lanes : []).map((lane) => {
      const ownerMeta = getLaneOwnerMeta(room, entry, lane.laneIndex);
      return {
        laneIndex: lane.laneIndex,
        isAI: ownerMeta.isAI,
        difficulty: ownerMeta.difficulty,
        takeover: ownerMeta.takeover,
        displayName: ownerMeta.displayName,
        playerId: ownerMeta.playerId,
        team: lane.team || null,
        side: lane.side || null,
        raceId: room && Array.isArray(room.raceByLane) ? room.raceByLane[lane.laneIndex] || null : null,
        loadoutKeys: getLaneLoadoutKeys(room, game, lane.laneIndex),
      };
    }),
    actions: (Array.isArray(entry.actionTimeline) ? entry.actionTimeline : []).map((actionEntry) => cloneJsonSafe(actionEntry)),
  };
}

module.exports = {
  ACTION_TIMELINE_SCHEMA_VERSION,
  buildActionTimelinePayload,
  getLaneOwnerMeta,
  recordTimelineAction,
};
