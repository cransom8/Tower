// server/ai.js
"use strict";

/**
 * Server-side AI opponents for ML (multi-lane) mode.
 * Uses the same applyMLAction() as human players - no stat cheating.
 */

const simMl = require("./sim-multilane");

const TICK_INTERVALS = { easy: 800, medium: 450, hard: 250 };

// Probability of skipping an AI decision tick (suboptimal play simulation)
const SKIP_CHANCE = { easy: 0.25, medium: 0.10, hard: 0.02 };
const MAX_TOWER_LEVEL = 10;
const TARGET_MODES = new Set(["first", "last", "weakest", "strongest"]);
const SPAWN_X = 5;
const SPAWN_Y = 0;
const CASTLE_X = 5;
const CASTLE_Y = 27;
const WALL_COST = 2;

// Ordered tower build plans per difficulty.
// Each entry: place a wall at (x,y) then convert it to towerType.
const TOWER_PLANS = {
  easy: [
    { x: 3, y: 8, type: "archer" },
    { x: 5, y: 18, type: "fighter" },
  ],
  medium: [
    { x: 3, y: 5, type: "archer" },
    { x: 5, y: 11, type: "fighter" },
    { x: 3, y: 17, type: "archer" },
    { x: 6, y: 8, type: "mage" },
  ],
  hard: [
    { x: 3, y: 4, type: "archer" },
    { x: 5, y: 9, type: "fighter" },
    { x: 3, y: 14, type: "mage" },
    { x: 6, y: 19, type: "cannon" },
    { x: 2, y: 23, type: "archer" },
  ],
};

// Unit send priorities per difficulty.
const UNIT_PRIORITIES = {
  easy: ["footman", "runner"],
  medium: ["footman", "ironclad", "runner", "warlock"],
  hard: ["ironclad", "warlock", "golem", "footman", "runner"],
};

const DIFFICULTY_PROFILE = {
  easy: {
    reserveGold: 16,
    baseSpawnChance: 0.50,
    dangerSpawnChance: 0.30,
    spawnBurst: 1,
    saveForBarracks: 70,
    maxSkipsWhenRich: 0,
    mazeEnabled: false,
    mazeMinGain: 99,
    mazeIntervalTicks: 40,
  },
  medium: {
    reserveGold: 10,
    baseSpawnChance: 0.75,
    dangerSpawnChance: 0.45,
    spawnBurst: 2,
    saveForBarracks: 55,
    maxSkipsWhenRich: 1,
    mazeEnabled: true,
    mazeMinGain: 2,
    mazeIntervalTicks: 18,
  },
  hard: {
    reserveGold: 6,
    baseSpawnChance: 1.0,
    dangerSpawnChance: 0.75,
    spawnBurst: 3,
    saveForBarracks: 45,
    maxSkipsWhenRich: 2,
    mazeEnabled: true,
    mazeMinGain: 1,
    mazeIntervalTicks: 10,
  },
};

const DAMAGE_MULTIPLIERS = {
  PIERCE: { UNARMORED: 1.35, LIGHT: 1.20, MEDIUM: 1.00, HEAVY: 0.75, MAGIC: 0.85 },
  NORMAL: { UNARMORED: 1.10, LIGHT: 1.00, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 0.90 },
  SPLASH: { UNARMORED: 1.10, LIGHT: 1.25, MEDIUM: 1.20, HEAVY: 0.80, MAGIC: 0.85 },
  SIEGE: { UNARMORED: 0.90, LIGHT: 0.90, MEDIUM: 1.00, HEAVY: 1.35, MAGIC: 0.80 },
  MAGIC: { UNARMORED: 1.00, LIGHT: 1.05, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 1.40 },
};

function getDamageMultiplier(damageType, armorType) {
  const byDamage = DAMAGE_MULTIPLIERS[damageType] || DAMAGE_MULTIPLIERS.NORMAL;
  const m = byDamage[armorType];
  return Number.isFinite(m) ? m : 1;
}

function computeThreatScore(lane) {
  if (!lane || lane.eliminated) return 0;
  let score = 0;
  if (lane.lives <= 8) score += 2;
  else if (lane.lives <= 12) score += 1;

  const pathLen = lane.path ? lane.path.length : 1;
  for (const u of lane.units) {
    if (u.ownerLane === lane.laneIndex) continue;
    const progress = pathLen > 1 ? (u.pathIdx / (pathLen - 1)) : 0;
    const unitDef = simMl.UNIT_DEFS[u.type];
    const hpNorm = Math.max(0.35, Math.min(1.8, (u.hp || 0) / ((unitDef && unitDef.hp) || 100)));
    if (progress >= 0.80) score += 2.4 * hpNorm;
    else if (progress >= 0.65) score += 1.5 * hpNorm;
    else if (progress >= 0.50) score += 0.6 * hpNorm;
  }
  return score;
}

function isDangerous(lane) {
  return computeThreatScore(lane) >= 2.5;
}

function getOutgoingTargetLane(game, laneIndex) {
  if (!game || !game.lanes || game.lanes.length === 0) return null;
  const idx = (laneIndex + 1) % game.lanes.length;
  return game.lanes[idx] || null;
}

function estimateTowerDamageProfile(lane) {
  const profile = { PIERCE: 0, NORMAL: 0, SPLASH: 0, SIEGE: 0, MAGIC: 0, towers: 0 };
  if (!lane) return profile;
  const GRID_W = simMl.GRID_W;
  const GRID_H = simMl.GRID_H;
  for (let x = 0; x < GRID_W; x++) {
    for (let y = 0; y < GRID_H; y++) {
      const tile = lane.grid[x][y];
      if (tile.type !== "tower" || !tile.towerType) continue;
      const base = simMl.TOWER_DEFS[tile.towerType];
      if (!base) continue;
      const lvl = Math.max(1, Math.min(MAX_TOWER_LEVEL, Number(tile.towerLevel) || 1));
      const scaledDmg = base.dmg * (1 + 0.12 * (lvl - 1));
      profile[base.damageType] += scaledDmg;
      profile.towers += 1;
    }
  }
  return profile;
}

function chooseAdaptiveTowerType(targetLane, fallbackType, difficulty) {
  if (difficulty === "easy" || !targetLane) return fallbackType;
  const enemyUnits = targetLane.units || [];
  const counts = { runner: 0, footman: 0, ironclad: 0, warlock: 0, golem: 0 };
  for (const u of enemyUnits) {
    if (u.ownerLane === targetLane.laneIndex) continue;
    if (counts[u.type] !== undefined) counts[u.type] += 1;
  }
  const swarm = (counts.runner + counts.footman) >= 7;
  const heavy = (counts.ironclad + counts.golem) >= 4;
  const caster = counts.warlock >= 3;

  if (swarm) return "cannon";
  if (heavy) return "ballista";
  if (caster) return "mage";
  return fallbackType;
}

function chooseTowerTargetMode(lane) {
  const enemies = (lane.units || []).filter((u) => u.ownerLane !== lane.laneIndex);
  if (enemies.length === 0) return "first";
  let heavy = 0;
  let lowHp = 0;
  for (const u of enemies) {
    if (u.type === "ironclad" || u.type === "golem") heavy += 1;
    if ((u.hp || 0) < 40) lowHp += 1;
  }
  if (heavy >= Math.ceil(enemies.length * 0.35)) return "strongest";
  if (lowHp >= Math.ceil(enemies.length * 0.4)) return "weakest";
  return "first";
}

function retargetTowers(game, laneIndex, desiredMode) {
  if (!TARGET_MODES.has(desiredMode)) return false;
  const lane = game.lanes[laneIndex];
  if (!lane) return false;
  const GRID_W = simMl.GRID_W;
  const GRID_H = simMl.GRID_H;
  for (let x = 0; x < GRID_W; x++) {
    for (let y = 0; y < GRID_H; y++) {
      const tile = lane.grid[x][y];
      if (tile.type !== "tower") continue;
      if ((tile.targetMode || "first") === desiredMode) continue;
      const res = simMl.applyMLAction(game, laneIndex, {
        type: "set_tower_target",
        data: { gridX: x, gridY: y, targetMode: desiredMode },
      });
      if (res.ok) return true;
    }
  }
  return false;
}

function findPathLengthWithExtraWall(grid, wallX, wallY) {
  const GRID_W = simMl.GRID_W;
  const GRID_H = simMl.GRID_H;
  const visited = Array.from({ length: GRID_W }, () => new Array(GRID_H).fill(false));
  const dist = Array.from({ length: GRID_W }, () => new Array(GRID_H).fill(-1));
  const q = [];
  let head = 0;
  q.push([SPAWN_X, SPAWN_Y]);
  visited[SPAWN_X][SPAWN_Y] = true;
  dist[SPAWN_X][SPAWN_Y] = 0;
  const dirs = [[1, 0], [-1, 0], [0, 1], [0, -1]];

  while (head < q.length) {
    const [cx, cy] = q[head++];
    if (cx === CASTLE_X && cy === CASTLE_Y) return dist[cx][cy] + 1;
    for (const [dx, dy] of dirs) {
      const nx = cx + dx;
      const ny = cy + dy;
      if (nx < 0 || nx >= GRID_W || ny < 0 || ny >= GRID_H) continue;
      if (visited[nx][ny]) continue;
      if (nx === wallX && ny === wallY) continue;
      const tile = grid[nx][ny];
      if (!tile) continue;
      if (tile.type === "wall" || tile.type === "tower") continue;
      visited[nx][ny] = true;
      dist[nx][ny] = dist[cx][cy] + 1;
      q.push([nx, ny]);
    }
  }
  return null;
}

function getAdjacentObstacleCount(grid, x, y) {
  const dirs = [[1, 0], [-1, 0], [0, 1], [0, -1]];
  let count = 0;
  for (const [dx, dy] of dirs) {
    const nx = x + dx;
    const ny = y + dy;
    if (nx < 0 || nx >= simMl.GRID_W || ny < 0 || ny >= simMl.GRID_H) continue;
    const t = grid[nx][ny];
    if (t && (t.type === "wall" || t.type === "tower")) count += 1;
  }
  return count;
}

function tryBuildMazeWall(game, laneIndex, profile, danger) {
  const lane = game.lanes[laneIndex];
  if (!lane || !profile.mazeEnabled) return false;
  if (danger && lane.gold < 40) return false;
  if (lane.gold < WALL_COST + (profile.reserveGold || 0)) return false;
  if (lane.wallCount >= 95) return false;
  if (!lane.path || lane.path.length < 2) return false;

  const nowTick = Number(game.tick) || 0;
  if ((lane.__aiNextMazeTick || 0) > nowTick) return false;

  const path = lane.path;
  const baseLen = path.length;
  const pathCells = new Set(path.map((p) => `${p.x},${p.y}`));
  let best = null;

  for (let x = 0; x < simMl.GRID_W; x++) {
    for (let y = 1; y < simMl.GRID_H - 1; y++) {
      if ((x === SPAWN_X && y === SPAWN_Y) || (x === CASTLE_X && y === CASTLE_Y)) continue;
      const tile = lane.grid[x] && lane.grid[x][y];
      if (!tile || tile.type !== "empty") continue;

      const newLen = findPathLengthWithExtraWall(lane.grid, x, y);
      if (newLen == null) continue;

      const gain = newLen - baseLen;
      if (gain < profile.mazeMinGain) continue;

      let score = gain;
      if (pathCells.has(`${x},${y}`)) score += 0.8;
      score += getAdjacentObstacleCount(lane.grid, x, y) * 0.2;
      if (y > 4 && y < simMl.GRID_H - 4) score += 0.2;

      if (!best || score > best.score) best = { x, y, score };
    }
  }

  lane.__aiNextMazeTick = nowTick + Math.max(6, profile.mazeIntervalTicks || 12);
  if (!best) return false;

  const res = simMl.applyMLAction(game, laneIndex, {
    type: "place_wall",
    data: { gridX: best.x, gridY: best.y },
  });
  return res.ok;
}

// Try each tower in the plan: place wall -> convert wall.
function tryBuildFromPlan(game, laneIndex, plan, difficulty) {
  const lane = game.lanes[laneIndex];
  const targetLane = getOutgoingTargetLane(game, laneIndex);

  for (const target of plan) {
    const tile = lane.grid[target.x] && lane.grid[target.x][target.y];
    if (!tile) continue;
    if (tile.type === "tower") continue;

    if (tile.type === "empty") {
      const res = simMl.applyMLAction(game, laneIndex, {
        type: "place_wall",
        data: { gridX: target.x, gridY: target.y },
      });
      if (res.ok) return true;
    }

    if (tile.type === "wall") {
      const adaptiveType = chooseAdaptiveTowerType(targetLane, target.type, difficulty);
      const res = simMl.applyMLAction(game, laneIndex, {
        type: "convert_wall",
        data: { gridX: target.x, gridY: target.y, towerType: adaptiveType },
      });
      if (res.ok) return true;
    }
  }
  return false;
}

function tryUpgradeBestTower(game, laneIndex, danger) {
  const lane = game.lanes[laneIndex];
  const GRID_W = simMl.GRID_W;
  const GRID_H = simMl.GRID_H;
  const pathLen = lane.path ? lane.path.length : 1;
  let best = null;

  for (let x = 0; x < GRID_W; x++) {
    for (let y = 0; y < GRID_H; y++) {
      const tile = lane.grid[x][y];
      if (tile.type !== "tower" || tile.towerLevel >= MAX_TOWER_LEVEL) continue;

      const base = simMl.TOWER_DEFS[tile.towerType];
      if (!base) continue;
      const nextLevel = (tile.towerLevel || 1) + 1;
      const upgradeCost = Math.ceil(base.cost * (0.75 + 0.25 * nextLevel));
      if (upgradeCost <= 0 || lane.gold < upgradeCost) continue;

      let score = 1 / upgradeCost;
      if (danger) {
        const castleDist = Math.max(1, (pathLen - 1) - y);
        score += 1.4 / castleDist;
      } else {
        score += tile.towerLevel < 4 ? 0.25 : 0.1;
      }

      if (!best || score > best.score) best = { x, y, score };
    }
  }

  if (!best) return false;
  const res = simMl.applyMLAction(game, laneIndex, {
    type: "upgrade_tower",
    data: { gridX: best.x, gridY: best.y },
  });
  return res.ok;
}

function scoreUnitChoice(unitType, targetLane, profile, difficulty) {
  const def = simMl.UNIT_DEFS[unitType];
  if (!def) return -Infinity;

  const incomeValue = def.income * (difficulty === "hard" ? 1.0 : 1.2);
  const pressureValue = (def.dmg / Math.max(1, def.atkCdTicks)) * 6;
  const goldEfficiency = (def.hp / Math.max(1, def.cost)) * 0.2;
  const armor = def.armorType || "MEDIUM";

  const towerThreat = (
    profile.PIERCE * getDamageMultiplier("PIERCE", armor) +
    profile.NORMAL * getDamageMultiplier("NORMAL", armor) +
    profile.SPLASH * getDamageMultiplier("SPLASH", armor) +
    profile.SIEGE * getDamageMultiplier("SIEGE", armor) +
    profile.MAGIC * getDamageMultiplier("MAGIC", armor)
  );
  const survivability = (def.hp / Math.max(1, towerThreat + 10)) * 20;

  let specialty = 0;
  if (unitType === "golem" && profile.towers >= 4) specialty += 4;
  if (unitType === "warlock" && profile.towers >= 3) specialty += 3;
  if (unitType === "runner" && (!targetLane || (targetLane.units || []).length <= 3)) specialty += 1.5;

  return incomeValue + pressureValue + goldEfficiency + survivability + specialty;
}

function pickBestAffordableUnit(game, laneIndex, difficulty) {
  const lane = game.lanes[laneIndex];
  const targetLane = getOutgoingTargetLane(game, laneIndex);
  const towerProfile = estimateTowerDamageProfile(targetLane);
  const list = UNIT_PRIORITIES[difficulty] || UNIT_PRIORITIES.easy;

  let bestType = null;
  let bestScore = -Infinity;
  for (const unitType of list) {
    const def = simMl.UNIT_DEFS[unitType];
    if (!def || lane.gold < def.cost) continue;
    const score = scoreUnitChoice(unitType, targetLane, towerProfile, difficulty);
    if (score > bestScore) {
      bestScore = score;
      bestType = unitType;
    }
  }

  return bestType;
}

// Try to send one or more units based on profile settings.
function trySpawnBurst(game, laneIndex, difficulty, profile) {
  const lane = game.lanes[laneIndex];
  let spawned = 0;
  const reserve = profile.reserveGold || 0;
  const attempts = Math.max(1, profile.spawnBurst || 1);

  for (let i = 0; i < attempts; i++) {
    const nextType = pickBestAffordableUnit(game, laneIndex, difficulty);
    if (!nextType) break;
    const nextDef = simMl.UNIT_DEFS[nextType];
    if (!nextDef) break;
    if (lane.gold - nextDef.cost < reserve && i >= (profile.maxSkipsWhenRich || 0)) break;

    const res = simMl.applyMLAction(game, laneIndex, { type: "spawn_unit", data: { unitType: nextType } });
    if (!res.ok) break;
    spawned += 1;
  }

  return spawned > 0;
}

function aiDecide(game, laneIndex, difficulty) {
  const lane = game.lanes[laneIndex];
  if (!lane || lane.eliminated || game.phase !== "playing") return;
  if (Math.random() < (SKIP_CHANCE[difficulty] || 0)) return;

  const profile = DIFFICULTY_PROFILE[difficulty] || DIFFICULTY_PROFILE.easy;
  const plan = TOWER_PLANS[difficulty] || TOWER_PLANS.easy;
  const dangerScore = computeThreatScore(lane);
  const danger = isDangerous(lane);

  const desiredTargetMode = chooseTowerTargetMode(lane);
  if (retargetTowers(game, laneIndex, desiredTargetMode)) return;

  // 1. Defense first
  if (danger) {
    if (tryUpgradeBestTower(game, laneIndex, true)) return;

    const targetLane = getOutgoingTargetLane(game, laneIndex);
    for (const target of plan) {
      const tile = lane.grid[target.x] && lane.grid[target.x][target.y];
      if (tile && tile.type === "wall") {
        const adaptiveType = chooseAdaptiveTowerType(targetLane, target.type, difficulty);
        const res = simMl.applyMLAction(game, laneIndex, {
          type: "convert_wall",
          data: { gridX: target.x, gridY: target.y, towerType: adaptiveType },
        });
        if (res.ok) return;
      }
    }
  }

  // 2. Maze building (medium/hard): place walls that maximize path detours.
  if (tryBuildMazeWall(game, laneIndex, profile, danger)) return;

  // 3. Economy / pressure
  const sendChance = danger ? profile.dangerSpawnChance : profile.baseSpawnChance;
  if (Math.random() < sendChance) {
    if (trySpawnBurst(game, laneIndex, difficulty, profile)) return;
  }

  // 4. Build tower plan
  if (tryBuildFromPlan(game, laneIndex, plan, difficulty)) return;

  // 5. Upgrade barracks when requirements met
  const barracksPriority = lane.gold >= profile.saveForBarracks && (difficulty === "hard" || !danger);
  if (barracksPriority) {
    const upgraded = simMl.applyMLAction(game, laneIndex, { type: "upgrade_barracks", data: {} });
    if (upgraded.ok) return;
  }

  // 6. Upgrade existing towers (non-emergency)
  if (tryUpgradeBestTower(game, laneIndex, danger)) return;

  // 7. Fallback gold dump when very threatened
  if (dangerScore >= 4.5) {
    trySpawnBurst(game, laneIndex, difficulty, Object.assign({}, profile, { reserveGold: 0, spawnBurst: 1 }));
  }
}

/**
 * Start an AI loop for one lane. Returns a handle (interval ID).
 * @param {object} game - live game object from simMl.createMLGame()
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
