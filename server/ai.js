// server/ai.js
"use strict";

/**
 * Server-side AI opponents for ML (multi-lane) mode.
 * Uses the same applyMLAction() as human players — no stat cheating.
 */

const simMl = require("./sim-multilane");

const TICK_INTERVALS = { easy: 800, medium: 450, hard: 250 };

// Probability of skipping an AI decision tick (suboptimal play simulation)
const SKIP_CHANCE = { easy: 0.25, medium: 0.10, hard: 0.02 };

// Ordered tower build plans per difficulty.
// Each entry: place a wall at (x,y) then convert it to towerType.
// Positions are chosen near the default center column (x=4) so towers
// cover units walking the straight path.
const TOWER_PLANS = {
  easy: [
    { x: 3, y: 8,  type: "archer"  },
    { x: 5, y: 18, type: "fighter" },
  ],
  medium: [
    { x: 3, y: 5,  type: "archer"  },
    { x: 5, y: 11, type: "fighter" },
    { x: 3, y: 17, type: "archer"  },
    { x: 6, y: 8,  type: "mage"    },
  ],
  hard: [
    { x: 3, y: 4,  type: "archer"  },
    { x: 5, y: 9,  type: "fighter" },
    { x: 3, y: 14, type: "mage"    },
    { x: 6, y: 19, type: "cannon"  },
    { x: 2, y: 23, type: "archer"  },
  ],
};

// Unit send priorities per difficulty.
// Hard AI sends heavier/ranged units; Easy AI keeps it simple.
const UNIT_PRIORITIES = {
  easy:   ["footman", "runner"],
  medium: ["footman", "ironclad", "runner"],
  hard:   ["ironclad", "warlock", "golem", "footman"],
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function isDangerous(lane) {
  if (lane.gateHp < 40) return true;
  const pathLen = lane.path ? lane.path.length : 1;
  const threshold = pathLen * 0.70; // enemies in bottom 30% of path
  return lane.units.some(u => u.ownerLane !== lane.laneIndex && u.pathIdx >= threshold);
}

// Try each tower in the plan: place wall → convert wall → upgrade when under threat.
// Returns true if an action was taken (so caller can return early).
function tryBuildFromPlan(game, laneIndex, plan) {
  const lane = game.lanes[laneIndex];
  for (const target of plan) {
    const tile = lane.grid[target.x] && lane.grid[target.x][target.y];
    if (!tile) continue;
    if (tile.type === "tower") continue; // already done

    if (tile.type === "empty") {
      const res = simMl.applyMLAction(game, laneIndex, {
        type: "place_wall",
        data: { gridX: target.x, gridY: target.y },
      });
      if (res.ok) return true;
    }

    if (tile.type === "wall") {
      const res = simMl.applyMLAction(game, laneIndex, {
        type: "convert_wall",
        data: { gridX: target.x, gridY: target.y, towerType: target.type },
      });
      if (res.ok) return true;
    }
  }
  return false;
}

// Try to upgrade any existing tower (cheapest next level first).
function tryUpgradeAnyTower(game, laneIndex) {
  const lane = game.lanes[laneIndex];
  const GRID_W = simMl.GRID_W;
  const GRID_H = simMl.GRID_H;
  for (let x = 0; x < GRID_W; x++) {
    for (let y = 0; y < GRID_H; y++) {
      const tile = lane.grid[x][y];
      if (tile.type !== "tower" || tile.towerLevel >= 10) continue;
      const res = simMl.applyMLAction(game, laneIndex, {
        type: "upgrade_tower",
        data: { gridX: x, gridY: y },
      });
      if (res.ok) return true;
    }
  }
  return false;
}

// Try to send a unit from the priority list.
function trySpawnUnit(game, laneIndex, difficulty) {
  const lane = game.lanes[laneIndex];
  const list = UNIT_PRIORITIES[difficulty] || UNIT_PRIORITIES.easy;
  const affordable = list.filter(ut => {
    const def = simMl.UNIT_DEFS[ut];
    return def && lane.gold >= def.cost;
  });
  if (affordable.length === 0) return false;

  // Hard: always pick the best affordable; others: random affordable
  let unitType;
  if (difficulty === "hard") {
    unitType = affordable[0];
  } else {
    unitType = affordable[Math.floor(Math.random() * affordable.length)];
  }
  const res = simMl.applyMLAction(game, laneIndex, { type: "spawn_unit", data: { unitType } });
  return res.ok;
}

// ── Main AI decision function ─────────────────────────────────────────────────

function aiDecide(game, laneIndex, difficulty) {
  const lane = game.lanes[laneIndex];
  if (!lane || lane.eliminated || game.phase !== "playing") return;
  if (Math.random() < (SKIP_CHANCE[difficulty] || 0)) return;

  const plan = TOWER_PLANS[difficulty] || TOWER_PLANS.easy;
  const danger = isDangerous(lane);

  // ── 1. Defense first ──
  if (danger) {
    if (tryUpgradeAnyTower(game, laneIndex)) return;
    // Try to convert any planned wall to a tower immediately
    for (const target of plan) {
      const tile = lane.grid[target.x] && lane.grid[target.x][target.y];
      if (tile && tile.type === "wall") {
        const res = simMl.applyMLAction(game, laneIndex, {
          type: "convert_wall",
          data: { gridX: target.x, gridY: target.y, towerType: target.type },
        });
        if (res.ok) return;
      }
    }
  }

  // ── 2. Income — always send units ──
  // Hard: aggressively send every tick; Easy/Medium: throttle with 60% chance
  const sendChance = danger ? 0.4 : (difficulty === "hard" ? 1.0 : 0.6);
  if (Math.random() < sendChance) {
    if (trySpawnUnit(game, laneIndex, difficulty)) return;
  }

  // ── 3. Build tower plan ──
  if (tryBuildFromPlan(game, laneIndex, plan)) return;

  // ── 4. Upgrade barracks when requirements met ──
  // Hard: upgrade eagerly; Easy: only when not under threat and gold is ample
  const barracksPriority = difficulty === "hard" || (!danger && lane.gold > 80);
  if (barracksPriority) {
    simMl.applyMLAction(game, laneIndex, { type: "upgrade_barracks", data: {} });
    // Not returning — barracks upgrade is low priority; fall through to tower upgrades
  }

  // ── 5. Upgrade existing towers (non-emergency) ──
  tryUpgradeAnyTower(game, laneIndex);
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Start an AI loop for one lane. Returns a handle (interval ID).
 * @param {object} game  - live game object from simMl.createMLGame()
 * @param {number} laneIndex
 * @param {string} difficulty - 'easy' | 'medium' | 'hard'
 * @returns {NodeJS.Timeout}
 */
function startAI(game, laneIndex, difficulty) {
  const interval = TICK_INTERVALS[difficulty] || 800;
  return setInterval(() => {
    if (!game || game.phase !== "playing") return;
    const lane = game.lanes[laneIndex];
    if (!lane || lane.eliminated) return;
    aiDecide(game, laneIndex, difficulty);
  }, interval);
}

/** Stop a previously started AI loop. */
function stopAI(handle) {
  if (handle != null) clearInterval(handle);
}

module.exports = { startAI, stopAI };
