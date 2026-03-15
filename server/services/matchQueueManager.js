'use strict';

const crypto = require('crypto');

// ── Constants ─────────────────────────────────────────────────────────────────

const GAME_TYPES    = new Set(['line_wars', 'survival']);
const MATCH_FORMATS = new Set(['1v1', '2v2', 'ffa']);
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
  ['1v1', '1v1'],
  ['duel', '1v1'],
  ['solo', '1v1'],
  ['2v2', '2v2'],
  ['teams', '2v2'],
  ['team', '2v2'],
  ['ffa', 'ffa'],
  ['freeforall', 'ffa'],
  ['free_for_all', 'ffa'],
]);

// Total player-slots needed to launch a match per format
const FORMAT_SLOTS = { '1v1': 2, '2v2': 4, 'ffa': 4 };


function makeBucketKey(gameType, matchFormat, ranked) {
  return `${gameType}:${matchFormat}:${ranked ? '1' : '0'}`;
}

function normalizeGameType(gameType, fallback = 'line_wars') {
  const raw = String(gameType || '').trim();
  if (!raw) return fallback;
  const canonical = GAME_TYPE_ALIASES.get(raw.toLowerCase());
  return canonical && GAME_TYPES.has(canonical) ? canonical : null;
}

function normalizeMatchFormat(matchFormat, fallback = '2v2') {
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
    normalizedMatchFormat = normalizedMatchFormat || legacyMatch[2];
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
  if (matchFormat === '1v1' && partySize !== 1)
    return 'Public 1v1 only supports solo players.';
  if (matchFormat === '2v2' && partySize > 2)
    return "Party of 4 can't join public 2v2. Use a private match.";
  if (matchFormat === 'ffa' && partySize !== 1)
    return 'Public FFA only supports solo players.';
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
  const matchFormat = normalizeMatchFormat(entry.matchFormat) || '2v2';
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

function _tryMatch1v1(q) {
  for (let i = 0; i < q.length; i++) {
    for (let j = i + 1; j < q.length; j++) {
      const a = q[i], b = q[j];
      if (q.length >= 10 && a.region !== b.region) continue;
      if (!_ratingOk(a, b)) continue;
      q.splice(j, 1); q.splice(i, 1);
      return [[a], [b]];
    }
  }
  return null;
}

function _tryMatch2v2(q) {
  // [party-of-2] vs [party-of-2]
  const p2 = q.filter(e => (e.partySize || 1) === 2);
  for (let i = 0; i < p2.length; i++) {
    for (let j = i + 1; j < p2.length; j++) {
      const a = p2[i], b = p2[j];
      if (!_ratingOk(a, b)) continue;
      [a, b].forEach(t => { const k = q.indexOf(t); if (k !== -1) q.splice(k, 1); });
      return [[a], [b]];
    }
  }
  // [party-of-2] + 2 solos
  const solos = q.filter(e => (e.partySize || 1) === 1);
  const p2a   = p2[0];
  if (p2a && solos.length >= 2) {
    const compat = solos.filter(s => _ratingOk(p2a, s));
    if (compat.length >= 2) {
      const [s1, s2] = compat;
      [p2a, s1, s2].forEach(t => { const k = q.indexOf(t); if (k !== -1) q.splice(k, 1); });
      return [[p2a], [s1, s2]];
    }
  }
  // 4 solos
  if (solos.length >= 4) {
    for (let i = 0; i < solos.length; i++) {
      const anchor = solos[i];
      const rest   = solos.filter((s, idx) => idx !== i && _ratingOk(anchor, s));
      if (rest.length >= 3) {
        const group = [anchor, ...rest.slice(0, 3)];
        group.forEach(t => { const k = q.indexOf(t); if (k !== -1) q.splice(k, 1); });
        return [[group[0], group[2]], [group[1], group[3]]];
      }
    }
  }
  return null;
}

function _tryMatchFFA(q) {
  const solos = q.filter(e => (e.partySize || 1) === 1);
  if (solos.length < 3) return null;
  const take = Math.min(solos.length, 4);
  const chosen = solos.slice(0, take);
  const minR = Math.min(...chosen.map(_rating));
  const maxR = Math.max(...chosen.map(_rating));
  if (maxR - minR > Math.min(...chosen.map(_window))) return null;
  chosen.forEach(t => { const k = q.indexOf(t); if (k !== -1) q.splice(k, 1); });
  return chosen.map(t => [t]);
}

function _tryMatchBucket(q, matchFormat) {
  if (matchFormat === '1v1') return _tryMatch1v1(q);
  if (matchFormat === '2v2') return _tryMatch2v2(q);
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
      const [, matchFormat, rankedFlag] = key.split(':');
      let safety = 0;
      while (safety++ < 100) {
        const teams = _tryMatchBucket(q, matchFormat);
        if (!teams) break;

        // Resolve party objects for each team
        const resolvedTeams = teams.map(team =>
          team.map(t => partiesById.get(t.partyId)).filter(Boolean)
        );

        // For legacy 2v2 backward compat
        const isLegacy2v2 = matchFormat === '2v2' && resolvedTeams.length === 2
          && resolvedTeams[0].length === 1 && resolvedTeams[1].length === 1;

        if (isLegacy2v2) {
          const legacyMode = rankedFlag === '1' ? 'ranked_2v2' : 'casual_2v2';
          onMatchFound(resolvedTeams[0][0], resolvedTeams[1][0], legacyMode, teams, key);
        } else {
          onMatchFound(null, null, key, teams, key);
        }
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
    matchFormat: normalizeMatchFormat(matchFormat) || '2v2',
    pvpMode:     pvpMode || matchFormat || '2v2',
    botSlots:    [],                           // [{ difficulty }]
    settings:    { startIncome: 10, ...(settings || {}) },
    members:         new Map([[hostSocketId, { name: hostDisplayName || 'Player', isReady: false }]]),
    teamAssignments: new Map(), // socketId → 'red'|'blue'|'green'|'orange'
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
  lobby.teamAssignments.delete(socketId);
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
  if (changes.gameType    && GAME_TYPES.has(changes.gameType))       lobby.gameType    = changes.gameType;
  if (changes.matchFormat && MATCH_FORMATS.has(changes.matchFormat)) {
    lobby.matchFormat = changes.matchFormat;
    lobby.pvpMode     = changes.pvpMode || changes.matchFormat;
    // Trim bots that no longer fit
    const max     = FORMAT_SLOTS[lobby.matchFormat] || 4;
    const maxBots = Math.max(0, max - lobby.members.size);
    if (lobby.botSlots.length > maxBots) lobby.botSlots.length = maxBots;
  }
  if (changes.pvpMode)   lobby.pvpMode   = changes.pvpMode;
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

const VALID_TEAMS = new Set(['red', 'blue', 'green', 'orange']);

/**
 * Assign (or clear) a team color for a lobby member. Host only.
 * Pass team=null or team='' to clear the assignment (revert to auto).
 * @returns {string|null} error message or null
 */
function assignTeam(hostSocketId, targetSocketId, team) {
  const lobby = getLobbyForSocket(hostSocketId);
  if (!lobby)                              return 'Not in a lobby.';
  if (lobby.hostSocketId !== hostSocketId) return 'Only the host can assign teams.';
  if (lobby.status !== 'open')             return 'Lobby has already started.';
  if (!lobby.members.has(targetSocketId)) return 'Player not in this lobby.';
  if (!team) {
    lobby.teamAssignments.delete(targetSocketId);
    return null;
  }
  if (!VALID_TEAMS.has(team)) return 'Invalid team color.';
  lobby.teamAssignments.set(targetSocketId, team);
  return null;
}

/**
 * Validate that a lobby is ready to launch.
 * @returns {string|null} error or null
 */
function validateLaunch(lobby) {
  const total = lobby.members.size + lobby.botSlots.length;
  const min   = lobby.gameType === 'survival' ? 1 : 2;
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
    matchFormat: lobby.matchFormat,
    pvpMode:     lobby.pvpMode,
    botSlots:    lobby.botSlots.map((b, i) => ({ ...b, index: i })),
    settings:    { ...lobby.settings },
    members:     [...lobby.members.entries()].map(([sid, m]) => ({
      socketId: sid,
      name:     m.name,
      isHost:   sid === lobby.hostSocketId,
      isReady:  m.isReady,
      team:     lobby.teamAssignments.get(sid) || null,
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
