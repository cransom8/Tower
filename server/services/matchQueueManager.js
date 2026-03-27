'use strict';

const crypto = require('crypto');

// ── Constants ─────────────────────────────────────────────────────────────────

const GAME_TYPES    = new Set(['line_wars', 'survival']);
const MATCH_FORMATS = new Set(['ffa']);
const VALID_DIFFS   = new Set(['easy', 'medium', 'hard', 'insane']);
const GAME_TYPE_ALIASES = new Map([
  ['linewars', 'line_wars'],
  ['line_wars', 'line_wars'],
  ['forgewars', 'line_wars'],
  ['forge_wars', 'line_wars'],
  ['multilane', 'line_wars'],
  ['pvp', 'line_wars'],
  ['public', 'line_wars'],
  ['survival', 'survival'],
  ['wave_defense', 'survival'],
]);
const MATCH_FORMAT_ALIASES = new Map([
  ['1v1', 'ffa'],
  ['duel', 'ffa'],
  ['solo', 'ffa'],
  ['2v2', 'ffa'],
  ['teams', 'ffa'],
  ['team', 'ffa'],
  ['ffa', 'ffa'],
  ['freeforall', 'ffa'],
  ['free_for_all', 'ffa'],
]);

// Total player-slots needed to launch a match per format
const FORMAT_SLOTS = { 'ffa': 4 };


function makeBucketKey(gameType, matchFormat, ranked) {
  return `${gameType}:${matchFormat}:${ranked ? '1' : '0'}`;
}

function normalizeGameType(gameType, fallback = 'line_wars') {
  const raw = String(gameType || '').trim();
  if (!raw) return fallback;
  const canonical = GAME_TYPE_ALIASES.get(raw.toLowerCase());
  return canonical && GAME_TYPES.has(canonical) ? canonical : null;
}

function normalizeMatchFormat(matchFormat, fallback = 'ffa') {
  const raw = String(matchFormat || '').trim();
  if (!raw) return fallback;
  const compact = raw.toLowerCase().replace(/[\s_-]+/g, '');
  const canonical = MATCH_FORMAT_ALIASES.get(compact) || MATCH_FORMAT_ALIASES.get(raw.toLowerCase());
  return canonical && MATCH_FORMATS.has(canonical) ? canonical : null;
}

function normalizeQueueRequest({ gameType, matchFormat, ranked } = {}) {
  let normalizedGameType = normalizeGameType(gameType);
  let normalizedMatchFormat = normalizeMatchFormat(matchFormat);
  let normalizedRanked = typeof ranked === 'boolean' ? ranked : !!ranked;
  const legacyTokens = [gameType, matchFormat]
    .map((value) => String(value || '').trim().toLowerCase())
    .filter(Boolean);

  for (const legacyToken of legacyTokens) {
    if (normalizedGameType && normalizedMatchFormat) break;
    const legacyMatch = legacyToken.match(/^(?:(ranked|casual)[_:])?(1v1|2v2|ffa)(?:[_:](ranked|casual))?$/);
    if (!legacyMatch) continue;
    normalizedGameType = normalizedGameType || 'line_wars';
    normalizedMatchFormat = normalizedMatchFormat || 'ffa';
    const legacyRankToken = legacyMatch[1] || legacyMatch[3];
    if (legacyRankToken) normalizedRanked = legacyRankToken === 'ranked';
  }

  return {
    gameType: normalizedGameType,
    matchFormat: normalizedMatchFormat,
    ranked: normalizedRanked,
  };
}

// ── Public queue validation ───────────────────────────────────────────────────

function validatePublicPartySize(matchFormat, partySize) {
  if (matchFormat === 'ffa' && partySize !== 1)
    return 'Public survival queue only supports solo players.';
  return null;
}

// ── Queue buckets ─────────────────────────────────────────────────────────────

const queues = new Map(); // bucketKey → [QueueTicket, ...]

function _getQ(key) {
  if (!queues.has(key)) queues.set(key, []);
  return queues.get(key);
}

/**
 * Add a party to the public queue.
 * QueueTicket shape:
 *   { partyId, gameType, matchFormat, ranked, partySize, rating, region,
 *     isPlacement, queueEnteredAt }
 */
function addToQueue(partyId, entry) {
  removeFromQueue(partyId);
  const gameType    = normalizeGameType(entry.gameType) || 'line_wars';
  const matchFormat = normalizeMatchFormat(entry.matchFormat) || 'ffa';
  const ranked      = !!entry.ranked;
  const key         = makeBucketKey(gameType, matchFormat, ranked);
  _getQ(key).push({ ...entry, partyId, gameType, matchFormat, ranked });
}

function removeFromQueue(partyId) {
  for (const [, q] of queues) {
    const idx = q.findIndex(e => e.partyId === partyId);
    if (idx !== -1) { q.splice(idx, 1); return true; }
  }
  return false;
}

function getQueueEntry(partyId) {
  for (const [, q] of queues) {
    const e = q.find(t => t.partyId === partyId);
    if (e) return e;
  }
  return null;
}

function getQueuePlayerCount(bucketKey, partiesById) {
  const q = _getQ(bucketKey);
  return q.reduce((n, e) => {
    const p = partiesById ? partiesById.get(e.partyId) : null;
    return n + (p ? p.members.length : (e.partySize || 1));
  }, 0);
}

function getQueueStats(partiesById) {
  const result = {};
  for (const [key, q] of queues) {
    const now = Date.now();
    let players = 0, oldest = null;
    for (const e of q) {
      const p = partiesById ? partiesById.get(e.partyId) : null;
      players += p ? p.members.length : (e.partySize || 1);
      const elapsed = now - e.queueEnteredAt;
      if (oldest === null || elapsed > oldest) oldest = elapsed;
    }
    result[key] = { entries: q.length, players, oldestMs: oldest };
  }
  return result;
}

// ── Rating window ─────────────────────────────────────────────────────────────

function _rating(e) { return typeof e.rating === 'number' ? e.rating : 1200; }
function _window(e) {
  if (e.isPlacement) return 500;
  return 150 + Math.floor((Date.now() - e.queueEnteredAt) / 30_000) * 50;
}
function _ratingOk(a, b) {
  return Math.abs(_rating(a) - _rating(b)) <= Math.min(_window(a), _window(b));
}

// ── Team assembly ─────────────────────────────────────────────────────────────
// Returns [[teamA-tickets...], [teamB-tickets...], ...] or null

function _tryMatchFFA(q) {
  const solos = q.filter(e => (e.partySize || 1) === 1);
  if (solos.length < 2) return null;
  const take = Math.min(solos.length, 4);
  const chosen = solos.slice(0, take);
  const minR = Math.min(...chosen.map(_rating));
  const maxR = Math.max(...chosen.map(_rating));
  if (maxR - minR > Math.min(...chosen.map(_window))) return null;
  chosen.forEach(t => { const k = q.indexOf(t); if (k !== -1) q.splice(k, 1); });
  return chosen.map(t => [t]);
}

function _tryMatchBucket(q, matchFormat) {
  if (matchFormat === 'ffa') return _tryMatchFFA(q);
  return null;
}

// ── Matchmaking loop ──────────────────────────────────────────────────────────

let _handle = null;

function stopMatchmakingLoop() {
  if (_handle) { clearInterval(_handle); _handle = null; }
}

/**
 * onMatchFound(partyA, partyB, mode, teams, bucketKey)
 *   - For backward compat (2v2), partyA/partyB are Party objects and mode is 'ranked_2v2'|'casual_2v2'.
 *   - For new formats, partyA/partyB are null and bucketKey is the full key.
 *   - teams = [[ticket,...], [ticket,...], ...] always present.
 */
function startMatchmakingLoop(io, partiesById, socketByPlayerId, onMatchFound) {
  if (_handle) return;
  _handle = setInterval(() => {
    for (const [key, q] of queues) {
      if (!q.length) continue;
      const [, matchFormat] = key.split(':');
      let safety = 0;
      while (safety++ < 100) {
        const teams = _tryMatchBucket(q, matchFormat);
        if (!teams) break;
        onMatchFound(null, null, key, teams, key);
      }
    }

    // Queue heartbeat to all waiting parties
    for (const [key, q] of queues) {
      const totalPlayers = q.reduce((n, e) => {
        const p = partiesById.get(e.partyId);
        return n + (p ? p.members.length : (e.partySize || 1));
      }, 0);
      for (const entry of q) {
        const party = partiesById.get(entry.partyId);
        if (!party) continue;
        const elapsed = Math.floor((Date.now() - entry.queueEnteredAt) / 1000);
        _emitToParty(io, party, socketByPlayerId, 'queue_status', {
          status:    'queued',
          mode:      entry.mode || key, // legacy compat
          elapsed,
          queueSize: totalPlayers,
        });
      }
    }
  }, 5_000);
}

// ── Private lobby ─────────────────────────────────────────────────────────────

const lobbiesById   = new Map(); // lobbyId   → lobby
const lobbiesByCode = new Map(); // code       → lobbyId
const lobbyBySockId = new Map(); // socketId   → lobbyId

function _lobbyCode() {
  const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
  const b = crypto.randomBytes(6);
  return Array.from(b, x => chars[x % chars.length]).join('');
}

/**
 * Create a new private lobby.
 * @returns {object} lobby
 */
function createLobby({ hostSocketId, hostDisplayName, gameType, matchFormat, pvpMode, settings } = {}) {
  let code;
  do { code = _lobbyCode(); } while (lobbiesByCode.has(code));
  const lobbyId = crypto.randomUUID();
  const lobby = {
    lobbyId,
    code,
    hostSocketId,
    gameType:    normalizeGameType(gameType) || 'line_wars',
    matchFormat: 'ffa',
    pvpMode:     'ffa',
    botSlots:    [],                           // [{ difficulty }]
    settings:    { ...(settings || {}) },
    members:         new Map([[hostSocketId, { name: hostDisplayName || 'Player', isReady: false }]]),
    status:          'open',
    createdAt:   Date.now(),
  };
  lobbiesById.set(lobbyId, lobby);
  lobbiesByCode.set(code, lobbyId);
  lobbyBySockId.set(hostSocketId, lobbyId);
  return lobby;
}

function getLobby(lobbyId)      { return lobbiesById.get(lobbyId)  || null; }
function getLobbyByCode(code)   {
  const id = lobbiesByCode.get(code);
  return id ? lobbiesById.get(id) : null;
}
function getLobbyForSocket(sid) {
  const id = lobbyBySockId.get(sid);
  return id ? lobbiesById.get(id) : null;
}

/**
 * Join an existing lobby by code.
 * @returns {string|null} error message or null on success
 */
function joinLobby(code, socketId, displayName) {
  const id = lobbiesByCode.get(code);
  if (!id) return 'Lobby not found.';
  const lobby = lobbiesById.get(id);
  if (!lobby) return 'Lobby not found.';
  if (lobby.status !== 'open') return 'This lobby has already started.';
  if (lobby.members.has(socketId)) return null; // already in — no-op
  const max = FORMAT_SLOTS[lobby.matchFormat] || 4;
  if (lobby.members.size + lobby.botSlots.length >= max) return 'Lobby is full.';
  lobby.members.set(socketId, { name: displayName || 'Player', isReady: false });
  lobbyBySockId.set(socketId, id);
  return null;
}

/**
 * Remove a socket from its lobby. Returns { lobby, disbanded }.
 */
function leaveLobby(socketId) {
  const id = lobbyBySockId.get(socketId);
  if (!id) return { lobby: null, disbanded: false };
  const lobby = lobbiesById.get(id);
  if (!lobby) return { lobby: null, disbanded: false };
  lobby.members.delete(socketId);
  lobbyBySockId.delete(socketId);
  if (lobby.hostSocketId === socketId) {
    const remaining = [...lobby.members.keys()];
    if (remaining.length === 0) {
      lobbiesById.delete(id);
      lobbiesByCode.delete(lobby.code);
      return { lobby, disbanded: true };
    }
    lobby.hostSocketId = remaining[0];
  }
  return { lobby, disbanded: false };
}

/**
 * Disbands a lobby entirely (called on server-initiated cleanup).
 */
function disbandLobby(lobbyId) {
  const lobby = lobbiesById.get(lobbyId);
  if (!lobby) return;
  for (const sid of lobby.members.keys()) lobbyBySockId.delete(sid);
  lobbiesById.delete(lobbyId);
  lobbiesByCode.delete(lobby.code);
}

/**
 * Update lobby settings (host only).
 * @returns {string|null} error message or null
 */
function updateLobby(socketId, changes) {
  const lobby = getLobbyForSocket(socketId);
  if (!lobby)                              return 'Not in a lobby.';
  if (lobby.hostSocketId !== socketId)     return 'Only the host can update lobby settings.';
  if (lobby.status !== 'open')             return 'Lobby has already started.';
  const normalizedGameType = changes.gameType ? normalizeGameType(changes.gameType, null) : null;
  if (normalizedGameType) lobby.gameType = normalizedGameType;
  lobby.matchFormat = 'ffa';
  lobby.pvpMode = 'ffa';
  const max     = FORMAT_SLOTS[lobby.matchFormat] || 4;
  const maxBots = Math.max(0, max - lobby.members.size);
  if (lobby.botSlots.length > maxBots) lobby.botSlots.length = maxBots;
  if (changes.settings)  lobby.settings  = { ...lobby.settings, ...changes.settings };
  return null;
}

function addBotToLobby(socketId, difficulty) {
  const lobby = getLobbyForSocket(socketId);
  if (!lobby)                              return 'Not in a lobby.';
  if (lobby.hostSocketId !== socketId)     return 'Only the host can add bots.';
  const max = FORMAT_SLOTS[lobby.matchFormat] || 4;
  if (lobby.members.size + lobby.botSlots.length >= max) return 'Lobby is full.';
  lobby.botSlots.push({ difficulty: VALID_DIFFS.has(difficulty) ? difficulty : 'medium' });
  return null;
}

function removeBotFromLobby(socketId, index) {
  const lobby = getLobbyForSocket(socketId);
  if (!lobby)                              return 'Not in a lobby.';
  if (lobby.hostSocketId !== socketId)     return 'Only the host can remove bots.';
  if (index < 0 || index >= lobby.botSlots.length) return 'Invalid bot slot index.';
  lobby.botSlots.splice(index, 1);
  return null;
}

/**
 * Set the ready state of a lobby member.
 * @returns {{ lobby: object|null, error: string|null }}
 */
function setMemberReady(socketId, ready) {
  const lobby = getLobbyForSocket(socketId);
  if (!lobby) return { lobby: null, error: 'Not in a lobby.' };
  if (lobby.status !== 'open') return { lobby, error: 'Lobby has already started.' };
  const member = lobby.members.get(socketId);
  if (!member) return { lobby, error: 'Not a member of this lobby.' };
  member.isReady = !!ready;
  return { lobby, error: null };
}

function assignTeam(hostSocketId) {
  const lobby = getLobbyForSocket(hostSocketId);
  if (!lobby) return 'Not in a lobby.';
  return 'Team assignment is no longer supported. Matches now use free-for-all survival lanes.';
}

/**
 * Validate that a lobby is ready to launch.
 * @returns {string|null} error or null
 */
function validateLaunch(lobby) {
  const total = lobby.members.size + lobby.botSlots.length;
  const min   = 1;
  if (total < min) return `Need at least ${min} player${min > 1 ? 's or bots' : ''} to launch.`;
  return null;
}

/**
 * Serialize a lobby for client consumption.
 */
function lobbySnapshot(lobby) {
  return {
    lobbyId:     lobby.lobbyId,
    code:        lobby.code,
    hostSocketId: lobby.hostSocketId,
    gameType:    lobby.gameType,
    matchFormat: 'ffa',
    pvpMode:     'ffa',
    botSlots:    lobby.botSlots.map((b, i) => ({ ...b, index: i })),
    settings:    { ...lobby.settings },
    members:     [...lobby.members.entries()].map(([sid, m]) => ({
      socketId: sid,
      name:     m.name,
      isHost:   sid === lobby.hostSocketId,
      isReady:  m.isReady,
      team:     null,
    })),
    status:      lobby.status,
  };
}

// ── Utility ───────────────────────────────────────────────────────────────────

function _emitToParty(io, party, socketByPlayerId, event, payload) {
  for (const m of party.members) {
    const sid = socketByPlayerId.get(m.playerId);
    if (sid) io.to(sid).emit(event, payload);
  }
}

function emitToParty(io, party, socketByPlayerId, event, payload) {
  _emitToParty(io, party, socketByPlayerId, event, payload);
}

/** Remove socket from all lobby index maps (call on disconnect). */
function cleanupSocket(socketId) {
  lobbyBySockId.delete(socketId);
}

// ── Exports ───────────────────────────────────────────────────────────────────

module.exports = {
  // Constants
  GAME_TYPES, MATCH_FORMATS, VALID_DIFFS, FORMAT_SLOTS,
  // Queue helpers
  makeBucketKey, normalizeGameType, normalizeMatchFormat, normalizeQueueRequest, validatePublicPartySize,
  addToQueue, removeFromQueue, getQueueEntry,
  getQueuePlayerCount, getQueueStats,
  startMatchmakingLoop, stopMatchmakingLoop,
  // Private lobby
  createLobby, getLobby, getLobbyByCode, getLobbyForSocket,
  joinLobby, leaveLobby, disbandLobby, updateLobby,
  addBotToLobby, removeBotFromLobby, setMemberReady, assignTeam,
  validateLaunch, lobbySnapshot,
  // Utility
  emitToParty, cleanupSocket,
};
