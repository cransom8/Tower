'use strict';

// Combat event logger.
// Enabled by setting env var:  COMBAT_LOG=true
//
// Events are buffered on game._combatLog and flushed to the matches.combat_log
// column when the match ends. Viewable in the admin site under Match Details.
//
// Only kill-level events are recorded (not every hit) to keep the log concise.

const enabled = process.env.COMBAT_LOG === 'true';

/**
 * Push a combat event onto game._combatLog.
 * @param {object} game   - the sim game object
 * @param {string} type   - event type: 'defender_killed' | 'wave_unit_killed' | 'round_summary' | 'leak'
 * @param {object} data   - event payload
 */
function logEvent(game, type, data) {
  if (!enabled) return;
  if (!game._combatLog) game._combatLog = [];
  game._combatLog.push({ type, tick: game.tick, round: game.roundNumber, ...data });
}

module.exports = { enabled, logEvent };
