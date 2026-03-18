'use strict';

// ── In-memory matchmaker ──────────────────────────────────────────────────────
// Queue entry shape:
//   { partyId, mode, rating, queueEnteredAt, region, isPlacement, filters }
//
// `filters` is an opaque object forwarded from queue:enter for future use
// (e.g. botConfigs, privateCode, pvpMode). The matchmaker itself only uses
// rating + region for ranked/casual; all other modes are resolved directly
// in the queue:enter handler in index.js before reaching addToQueue.
//
// Active queued modes: 'ranked_2v2' | 'casual_2v2'
// Handled inline (no matchmaker loop): 'solo_td' | 'solo_t2t' | 'private_td' | 'private_t2t'

const queues = new Map(); // mode -> [ entry, ... ]

function _getQueue(mode) {
  if (!queues.has(mode)) queues.set(mode, []);
  return queues.get(mode);
}

function addToQueue(partyId, entry) {
  // Remove any stale entry for this party first (re-queue is idempotent)
  removeFromQueue(partyId);
  _getQueue(entry.mode).push({ ...entry, partyId });
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
    const entry = q.find(e => e.partyId === partyId);
    if (entry) return entry;
  }
  return null;
}

// ── Rating window ─────────────────────────────────────────────────────────────
// Normal: starts at ±150, expands +50 per 30 s in queue.
// Placement players (first 10 ranked matches): always ±500 to guarantee a match.

function _ratingOf(entry) {
  return typeof entry.rating === 'number' ? entry.rating : 1200;
}

function _windowFor(entry) {
  if (entry.isPlacement) return 500;
  const elapsedMs = Date.now() - entry.queueEnteredAt;
  const expansions = Math.floor(elapsedMs / 30_000);
  return 150 + expansions * 50;
}

// ── Match finding ─────────────────────────────────────────────────────────────

function _tryMatch(q) {
  for (let i = 0; i < q.length; i++) {
    for (let j = i + 1; j < q.length; j++) {
      const a = q[i];
      const b = q[j];
      // Region filter: only apply when queue is large enough
      if (q.length >= 10 && a.region !== b.region) continue;
      // Use the smaller of the two windows
      const window = Math.min(_windowFor(a), _windowFor(b));
      if (Math.abs(_ratingOf(a) - _ratingOf(b)) <= window) {
        // Remove highest index first to preserve lower index
        q.splice(j, 1);
        q.splice(i, 1);
        return [a, b];
      }
    }
  }
  return null;
}

// ── Loop ──────────────────────────────────────────────────────────────────────

let _loopHandle = null;

/** Stop the matchmaking loop (called during graceful shutdown). */
function stopMatchmakingLoop() {
  if (_loopHandle) {
    clearInterval(_loopHandle);
    _loopHandle = null;
  }
}

/**
 * Start the 5-second matchmaking poll.
 * @param {import('socket.io').Server} io
 * @param {Map} partiesById          partyId -> PartyObject
 * @param {Map} socketByPlayerId     playerId -> socketId
 * @param {Function} onMatchFound    (partyA, partyB, mode) -> void
 */
function startMatchmakingLoop(io, partiesById, socketByPlayerId, onMatchFound) {
  if (_loopHandle) return;

  _loopHandle = setInterval(() => {
    // Try to form matches in each mode queue
    for (const [mode, q] of queues) {
      let match;
      let safetyCount = 0;
      while ((match = _tryMatch(q)) !== null && ++safetyCount < 100) {
        const [entryA, entryB] = match;
        const partyA = partiesById.get(entryA.partyId);
        const partyB = partiesById.get(entryB.partyId);
        if (!partyA || !partyB) continue; // stale — parties already gone
        onMatchFound(partyA, partyB, mode);
      }
    }

    // Push queue_status heartbeat to all still-queued parties
    for (const [mode, q] of queues) {
      // Total players in this mode's queue
      const queueSize = q.reduce((n, e) => {
        const p = partiesById.get(e.partyId);
        return n + (p ? p.members.length : 1);
      }, 0);
      for (const entry of q) {
        const party = partiesById.get(entry.partyId);
        if (!party) continue;
        const elapsed = Math.floor((Date.now() - entry.queueEnteredAt) / 1000);
        _emitToParty(io, party, socketByPlayerId, 'queue_status', {
          status: 'queued',
          mode,
          elapsed,
          queueSize,
        });
      }
    }
  }, 5_000);
}

// ── Utility ───────────────────────────────────────────────────────────────────

function _emitToParty(io, party, socketByPlayerId, event, payload) {
  for (const member of party.members) {
    const sid = socketByPlayerId.get(member.playerId);
    if (sid) io.to(sid).emit(event, payload);
  }
}

/** Exported helper so index.js can emit party events without duplicating logic. */
function emitToParty(io, party, socketByPlayerId, event, payload) {
  _emitToParty(io, party, socketByPlayerId, event, payload);
}

function getQueuePlayerCount(mode, partiesById) {
  const q = _getQueue(mode);
  return q.reduce((n, e) => {
    const p = partiesById ? partiesById.get(e.partyId) : null;
    return n + (p ? p.members.length : 1);
  }, 0);
}

/**
 * Returns a snapshot of all queues for admin visibility.
 * @param {Map} partiesById
 * @returns {{ ranked_2v2: QueueStat, casual_2v2: QueueStat }}
 *   QueueStat = { entries: number, players: number, oldest: number|null (ms since entered) }
 */
function getQueueStats(partiesById) {
  const result = {};
  for (const [mode, q] of queues) {
    const now = Date.now();
    let players = 0;
    let oldest = null;
    for (const entry of q) {
      const p = partiesById ? partiesById.get(entry.partyId) : null;
      players += p ? p.members.length : 1;
      const elapsed = now - entry.queueEnteredAt;
      if (oldest === null || elapsed > oldest) oldest = elapsed;
    }
    result[mode] = { entries: q.length, players, oldestMs: oldest };
  }
  return result;
}

module.exports = {
  addToQueue,
  removeFromQueue,
  getQueueEntry,
  getQueuePlayerCount,
  getQueueStats,
  startMatchmakingLoop,
  stopMatchmakingLoop,
  emitToParty,
};
