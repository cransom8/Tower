'use strict';

// ── Glicko-2 Rating Service ───────────────────────────────────────────────────
//
// For a 2v2 match, team assignment follows laneIndex:
//   Team A: lanes 0 .. partyASize-1
//   Team B: lanes partyASize .. N-1
//
// Each player is updated against a composite opponent representing the other
// team (average mu, RMS phi). This degenerates to standard Glicko-2 for 1v1.
//
// DB columns: mu (display), sigma (display), rating = mu - 3*sigma
// Glicko-2 internal scale: mu_internal = (mu - 1500)/173.7178
//                          phi_internal = sigma / 173.7178

const SCALE = 173.7178;
const TAU   = 0.5;       // system constant (limits volatility change per period)
const EPSILON = 1e-6;    // convergence tolerance for Illinois algorithm
const DEFAULT_VOLATILITY = 0.06;   // fixed per-match volatility (not stored in DB)

// ── Glicko-2 math ─────────────────────────────────────────────────────────────

function _g(phi) {
  return 1 / Math.sqrt(1 + (3 * phi * phi) / (Math.PI * Math.PI));
}

function _E(mu, mu_j, phi_j) {
  return 1 / (1 + Math.exp(-_g(phi_j) * (mu - mu_j)));
}

// Illinois algorithm: find new volatility sigma_prime given current state.
function _newVolatility(phi, sigma, delta, v) {
  const a = Math.log(sigma * sigma);
  const deltaSq = delta * delta;
  const phiSq = phi * phi;

  function f(x) {
    const ex = Math.exp(x);
    const tmp = phiSq + v + ex;
    return (ex * (deltaSq - phiSq - v - ex)) / (2 * tmp * tmp) - (x - a) / (TAU * TAU);
  }

  let A = a;
  let B;
  if (deltaSq > phiSq + v) {
    B = Math.log(deltaSq - phiSq - v);
  } else {
    let k = 1;
    while (f(a - k * TAU) < 0) k++;
    B = a - k * TAU;
  }

  let fA = f(A);
  let fB = f(B);
  for (let iter = 0; iter < 100; iter++) {
    if (Math.abs(B - A) < EPSILON) break;
    const C = A + (A - B) * fA / (fB - fA);
    const fC = f(C);
    if (fC * fB < 0) {
      A = B; fA = fB;
    } else {
      fA /= 2;
    }
    B = C; fB = fC;
  }
  return Math.exp(A / 2);
}

// Update one player given opponents: [{ mu_j, phi_j, score }] (internal scale).
// Returns { new_mu, new_phi, new_sigma } in internal scale.
function _updatePlayer(mu, phi, sigma, opponents) {
  if (opponents.length === 0) return { new_mu: mu, new_phi: phi, new_sigma: sigma };

  const v_inv = opponents.reduce((sum, opp) => {
    const gj = _g(opp.phi_j);
    const ej = _E(mu, opp.mu_j, opp.phi_j);
    return sum + gj * gj * ej * (1 - ej);
  }, 0);
  if (v_inv === 0) return { new_mu: mu, new_phi: phi, new_sigma: sigma };
  const v = 1 / v_inv;

  const outImprovement = opponents.reduce((sum, opp) => {
    return sum + _g(opp.phi_j) * (opp.score - _E(mu, opp.mu_j, opp.phi_j));
  }, 0);
  const delta = v * outImprovement;

  const new_sigma = _newVolatility(phi, sigma, delta, v);
  const phi_star  = Math.sqrt(phi * phi + new_sigma * new_sigma);
  const new_phi   = 1 / Math.sqrt(1 / (phi_star * phi_star) + 1 / v);
  const new_mu    = mu + new_phi * new_phi * outImprovement;

  return { new_mu, new_phi, new_sigma };
}

// Build composite "opponent" for a team (all values in internal Glicko-2 scale).
// composite mu = average mu; composite phi = RMS of phis.
function _teamComposite(internalPlayers) {
  const n = internalPlayers.length;
  if (n === 0) return { mu_j: 0, phi_j: 350 / SCALE };
  const mu_j  = internalPlayers.reduce((s, p) => s + p.mu, 0) / n;
  const phi_j = Math.sqrt(internalPlayers.reduce((s, p) => s + p.phi * p.phi, 0) / n);
  return { mu_j, phi_j };
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Update Glicko-2 ratings for all authenticated players in a ranked match.
 *
 * @param {object} db            - server/db module (query, getClient)
 * @param {string|null} matchId  - UUID from matches table (may be null)
 * @param {string} mode          - '2v2_ranked' (only mode that triggers update)
 * @param {Array<{playerId:string, laneIndex:number, result:string}>} snapshots
 * @param {number} partyASize    - number of lanes belonging to team A (default 1)
 * @returns {Promise<Array<{playerId:string, newRating:number, delta:number}>>}
 */
async function updateRatings(db, matchId, mode, snapshots, partyASize = 1) {
  if (mode !== '2v2_ranked') return [];
  if (!db) return [];

  // Only process authenticated players with a decisive result
  const players = snapshots.filter(s => s.playerId && (s.result === 'win' || s.result === 'loss' || s.result === 'draw'));
  if (players.length < 2) return [];

  // Split into teams by laneIndex
  const teamA = players.filter(p => p.laneIndex < partyASize);
  const teamB = players.filter(p => p.laneIndex >= partyASize);
  if (teamA.length === 0 || teamB.length === 0) return [];

  // Fetch current ratings for all players
  const allIds = players.map(p => p.playerId);
  let rows;
  try {
    const res = await db.query(
      `SELECT player_id, mu, sigma, wins, losses FROM ratings WHERE player_id = ANY($1) AND mode = $2`,
      [allIds, mode]
    );
    rows = res.rows;
  } catch (err) {
    log.error('[rating] fetch failed:', { err: err.message });
    return [];
  }
  const ratingMap = new Map(rows.map(r => [r.player_id, r]));

  function toInternal(playerId) {
    const row = ratingMap.get(playerId);
    return {
      mu:    ((row ? Number(row.mu)    : 1500) - 1500) / SCALE,
      phi:    (row ? Number(row.sigma) : 350)          / SCALE,
      sigma: DEFAULT_VOLATILITY,
    };
  }

  // Build composite opponents for each team (in internal scale)
  const compositeA = _teamComposite(teamA.map(p => toInternal(p.playerId)));
  const compositeB = _teamComposite(teamB.map(p => toInternal(p.playerId)));

  // Compute updates
  const updates = players.map(player => {
    const current = toInternal(player.playerId);
    const score   = player.result === 'win' ? 1.0 : player.result === 'draw' ? 0.5 : 0.0;
    const isTeamA = player.laneIndex < partyASize;
    const composite = isTeamA ? compositeB : compositeA;

    const { new_mu, new_phi } = _updatePlayer(
      current.mu, current.phi, current.sigma,
      [{ mu_j: composite.mu_j, phi_j: composite.phi_j, score }]
    );

    const new_mu_d    = SCALE * new_mu + 1500;
    const new_sigma_d = SCALE * new_phi;
    const new_rating  = new_mu_d - 3 * new_sigma_d;

    const old = ratingMap.get(player.playerId);
    const old_mu_d    = old ? Number(old.mu)    : 1500;
    const old_sigma_d = old ? Number(old.sigma) : 350;
    const old_rating  = old_mu_d - 3 * old_sigma_d;
    const newWins    = (old ? Number(old.wins)   : 0) + (player.result === 'win'  ? 1 : 0);
    const newLosses  = (old ? Number(old.losses) : 0) + (player.result === 'loss' ? 1 : 0);

    return {
      playerId:  player.playerId,
      result:    player.result,
      old_mu:    old_mu_d,
      old_sigma: old_sigma_d,
      new_mu:    new_mu_d,
      new_sigma: new_sigma_d,
      new_rating,
      delta:     new_rating - old_rating,
      wins:      newWins,
      losses:    newLosses,
    };
  });

  // Write to DB in a single transaction
  const client = await db.getClient();
  try {
    await client.query('BEGIN');
    for (const u of updates) {
      const winInc  = u.result === 'win'  ? 1 : 0;
      const lossInc = u.result === 'loss' ? 1 : 0;
      await client.query(
        `INSERT INTO ratings (player_id, mode, mu, sigma, rating, wins, losses, updated_at)
         VALUES ($1, $2, $3, $4, $5, $6, $7, NOW())
         ON CONFLICT (player_id, mode) DO UPDATE SET
           mu         = EXCLUDED.mu,
           sigma      = EXCLUDED.sigma,
           rating     = EXCLUDED.rating,
           wins       = ratings.wins   + EXCLUDED.wins,
           losses     = ratings.losses + EXCLUDED.losses,
           updated_at = NOW()`,
        [u.playerId, mode, u.new_mu, u.new_sigma, u.new_rating, winInc, lossInc]
      );
      await client.query(
        `INSERT INTO rating_history
           (player_id, match_id, mode, old_mu, old_sigma, new_mu, new_sigma, delta)
         VALUES ($1, $2, $3, $4, $5, $6, $7, $8)`,
        [u.playerId, matchId, mode, u.old_mu, u.old_sigma, u.new_mu, u.new_sigma, u.delta]
      );
    }
    await client.query('COMMIT');
  } catch (err) {
    await client.query('ROLLBACK');
    log.error('[rating] transaction failed:', { err: err.message });
    return [];
  } finally {
    client.release();
  }

  log.info(`[rating] updated ${updates.length} player(s) for match ${matchId} mode=${mode}`);
  return updates.map(u => ({
    playerId:  u.playerId,
    newRating: u.new_rating,
    delta:     u.delta,
    wins:      u.wins,
    losses:    u.losses,
  }));
}

module.exports = { updateRatings };
