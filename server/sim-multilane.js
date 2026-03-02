// server/sim-multilane.js
"use strict";

/**
 * Multi-lane PvP simulation — 10×28 tile grid per player lane.
 * Units follow a BFS path from spawn tile to castle tile.
 * Wall placement requires BFS validation + combat lock.
 * Barracks provides global unit-tech upgrades.
 */

const TICK_HZ = 20;
const TICK_MS = Math.floor(1000 / TICK_HZ);
const INCOME_INTERVAL_TICKS = 240; // 12 s
const START_GOLD = 50;
const GATE_HP_START = 100;
const TOWER_MAX_LEVEL = 10;
const MAX_UNITS_PER_LANE = 80;
const GATE_KILL_BOUNTY = 10;

// Grid constants
const GRID_W = 10;
const GRID_H = 28;
const SPAWN_X = 4;
const SPAWN_YG = 0;
const CASTLE_X = 4;
const CASTLE_YG = 27;
const MAX_PATH_LEN = 48;
const MAX_WALLS = 100;
const WALL_COST = 5;
const ENGAGEMENT_SLOTS = 6;
const SPLASH_RADIUS_TILES = 1.5;

// pathSpeed = oldSpeedPerTick × GRID_H
// Standard: 0.00129375 × 28 ≈ 0.036225
// Runner:   0.00215625 × 28 ≈ 0.060375
const UNIT_DEFS = {
  footman:  { cost: 10, income: 1, hp: 90,  dmg: 7,  atkCdTicks: 8,  pathSpeed: 0.036225, bounty: 3, range: 1.0, ranged: false, armorType: "MEDIUM",    damageType: "NORMAL" },
  bowman:   { cost: 12, income: 1, hp: 50,  dmg: 8,  atkCdTicks: 9,  pathSpeed: 0.036225, bounty: 3, range: 4.0, ranged: true,  projectileTicks: 7, armorType: "LIGHT",     damageType: "PIERCE" },
  ironclad: { cost: 15, income: 2, hp: 140, dmg: 9,  atkCdTicks: 10, pathSpeed: 0.036225, bounty: 4, range: 1.0, ranged: false, armorType: "HEAVY",     damageType: "NORMAL" },
  runner:   { cost: 8,  income: 0, hp: 55,  dmg: 6,  atkCdTicks: 7,  pathSpeed: 0.060375, bounty: 2, range: 1.0, ranged: false, armorType: "UNARMORED", damageType: "NORMAL" },
  warlock:  { cost: 15, income: 2, hp: 80,  dmg: 11, atkCdTicks: 11, pathSpeed: 0.036225, bounty: 4, range: 3.0, ranged: true,  projectileTicks: 8, armorType: "MAGIC",     damageType: "MAGIC"  },
};

const TOWER_DEFS = {
  archer:   { cost: 10, range: 3.5, dmg: 8.5,  atkCdTicks: 11, projectileTicks: 7, damageType: "PIERCE", isSplash: false },
  fighter:  { cost: 12, range: 2.5, dmg: 10.0, atkCdTicks: 10, projectileTicks: 6, damageType: "NORMAL", isSplash: false },
  mage:     { cost: 24, range: 4.0, dmg: 15.0, atkCdTicks: 13, projectileTicks: 7, damageType: "MAGIC",  isSplash: false },
  ballista: { cost: 20, range: 5.0, dmg: 12.1, atkCdTicks: 14, projectileTicks: 8, damageType: "SIEGE",  isSplash: false },
  cannon:   { cost: 30, range: 6.0, dmg: 19.8, atkCdTicks: 16, projectileTicks: 9, damageType: "SPLASH", isSplash: true  },
};

// Index 0 unused; index 1 = current level 1 stats, index 2 = level 2 upgrade target, etc.
const BARRACKS_LEVELS = [
  null,
  { hpMult: 1.00, dmgMult: 1.00, speedMult: 1.00, structMult: 1.00, cost: 0,   reqIncome: 0  },
  { hpMult: 1.15, dmgMult: 1.10, speedMult: 1.00, structMult: 1.00, cost: 60,  reqIncome: 5  },
  { hpMult: 1.30, dmgMult: 1.20, speedMult: 1.05, structMult: 1.00, cost: 120, reqIncome: 12 },
  { hpMult: 1.45, dmgMult: 1.30, speedMult: 1.08, structMult: 1.10, cost: 200, reqIncome: 20 },
];

const DAMAGE_MULTIPLIERS = {
  PIERCE: { UNARMORED: 1.35, LIGHT: 1.20, MEDIUM: 1.00, HEAVY: 0.75, MAGIC: 0.85 },
  NORMAL: { UNARMORED: 1.10, LIGHT: 1.00, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 0.90 },
  SPLASH: { UNARMORED: 1.10, LIGHT: 1.25, MEDIUM: 1.20, HEAVY: 0.80, MAGIC: 0.85 },
  SIEGE:  { UNARMORED: 0.90, LIGHT: 0.90, MEDIUM: 1.00, HEAVY: 1.35, MAGIC: 0.80 },
  MAGIC:  { UNARMORED: 1.00, LIGHT: 1.05, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 1.40 },
};

function getDamageMultiplier(damageType, armorType) {
  const byDamage = DAMAGE_MULTIPLIERS[damageType] || DAMAGE_MULTIPLIERS.NORMAL;
  const m = byDamage[armorType];
  return Number.isFinite(m) ? m : 1;
}

function computeDamage(baseDmg, damageType, armorType) {
  return baseDmg * getDamageMultiplier(damageType, armorType);
}

function getTowerUpgradeCost(type, nextLevel) {
  const base = TOWER_DEFS[type];
  if (!base) return Infinity;
  return Math.ceil(base.cost * (0.75 + 0.25 * nextLevel));
}

function getTowerStats(type, level) {
  const base = TOWER_DEFS[type];
  if (!base) return null;
  const lvl = Math.max(1, Math.min(TOWER_MAX_LEVEL, level));
  const s = lvl - 1;
  return {
    dmg: base.dmg * (1 + 0.12 * s),
    range: base.range * (1 + 0.015 * s),
    atkCdTicks: Math.max(5, Math.round(base.atkCdTicks * (1 - 0.015 * s))),
    projectileTicks: base.projectileTicks,
    damageType: base.damageType,
    isSplash: base.isSplash || false,
  };
}

// ── Grid helpers ──────────────────────────────────────────────────────────────

function makeGrid() {
  const grid = [];
  for (let x = 0; x < GRID_W; x++) {
    grid[x] = [];
    for (let y = 0; y < GRID_H; y++) {
      grid[x][y] = { type: "empty", towerType: null, towerLevel: 0, atkCd: 0 };
    }
  }
  grid[SPAWN_X][SPAWN_YG].type = "spawn";
  grid[CASTLE_X][CASTLE_YG].type = "castle";
  return grid;
}

// 4-directional BFS from spawn to castle; walls and towers are obstacles.
// Returns [{x,y}] path (length ≤ MAX_PATH_LEN) or null if blocked/too long.
function bfsPath(grid) {
  const visited = [];
  const parent = [];
  for (let x = 0; x < GRID_W; x++) {
    visited[x] = new Array(GRID_H).fill(false);
    parent[x] = new Array(GRID_H).fill(null);
  }

  const sx = SPAWN_X, sy = SPAWN_YG;
  const ex = CASTLE_X, ey = CASTLE_YG;
  const queue = [[sx, sy]];
  visited[sx][sy] = true;

  const dirs = [[0, 1], [0, -1], [1, 0], [-1, 0]];
  let found = false;

  while (queue.length > 0) {
    const [cx, cy] = queue.shift();
    if (cx === ex && cy === ey) { found = true; break; }
    for (const [dx, dy] of dirs) {
      const nx = cx + dx, ny = cy + dy;
      if (nx < 0 || nx >= GRID_W || ny < 0 || ny >= GRID_H) continue;
      if (visited[nx][ny]) continue;
      const tile = grid[nx][ny];
      if (tile.type === "wall" || tile.type === "tower") continue;
      visited[nx][ny] = true;
      parent[nx][ny] = [cx, cy];
      queue.push([nx, ny]);
    }
  }

  if (!found) return null;

  // Reconstruct path
  const path = [];
  let cx = ex, cy = ey;
  while (!(cx === sx && cy === sy)) {
    path.unshift({ x: cx, y: cy });
    const p = parent[cx][cy];
    cx = p[0]; cy = p[1];
  }
  path.unshift({ x: sx, y: sy });

  if (path.length > MAX_PATH_LEN) return null;
  return path;
}

function tileDist(ax, ay, bx, by) {
  const dx = ax - bx, dy = ay - by;
  return Math.sqrt(dx * dx + dy * dy);
}

// Get current tile position of a unit from its pathIdx
function getUnitTilePos(unit, path) {
  if (!path || path.length === 0) return null;
  const idx = Math.min(Math.floor(unit.pathIdx), path.length - 1);
  return path[idx];
}

// ── Public API ────────────────────────────────────────────────────────────────

function createMLGame(playerCount) {
  const lanes = [];
  for (let i = 0; i < playerCount; i++) {
    const grid = makeGrid();
    const path = bfsPath(grid); // straight line (4,0)→(4,27)
    lanes.push({
      laneIndex: i,
      eliminated: false,
      gold: START_GOLD,
      income: 0,
      gateHp: GATE_HP_START,
      grid,
      path,
      wallCount: 0,
      barracks: { level: 1, hpMult: 1, dmgMult: 1, speedMult: 1, structMult: 1 },
      units: [],
      spawnQueue: [],
      projectiles: [],
    });
  }
  return {
    tick: 0,
    phase: "playing",
    winner: null,
    incomeTickCounter: 0,
    playerCount,
    lanes,
    nextUnitId: 1,
    nextProjectileId: 1,
  };
}

function applyMLAction(game, laneIndex, action) {
  if (!game || game.phase !== "playing") return { ok: false, reason: "Game not active" };
  if (laneIndex < 0 || laneIndex >= game.lanes.length) return { ok: false, reason: "Bad laneIndex" };
  const lane = game.lanes[laneIndex];
  if (lane.eliminated) return { ok: false, reason: "You have been eliminated" };
  if (!action || typeof action.type !== "string") return { ok: false, reason: "Bad action" };

  const { type, data } = action;

  if (type === "spawn_unit") {
    const unitType = String((data && data.unitType) || "").toLowerCase();
    const def = UNIT_DEFS[unitType];
    if (!def) return { ok: false, reason: "Unknown unitType" };
    if (lane.gold < def.cost) return { ok: false, reason: "Not enough gold" };
    if (lane.spawnQueue.length >= MAX_UNITS_PER_LANE) return { ok: false, reason: "Spawn queue full" };

    lane.gold -= def.cost;
    lane.income += def.income;

    // Round-robin: send units to lane on the right
    const targetLaneIndex = (laneIndex + 1) % game.playerCount;
    const targetLane = game.lanes[targetLaneIndex];
    const br = lane.barracks;
    targetLane.spawnQueue.push({
      id: `u${game.nextUnitId++}`,
      ownerLane: laneIndex,
      type: unitType,
      pathIdx: 0,
      hp: Math.ceil(def.hp * br.hpMult),
      maxHp: Math.ceil(def.hp * br.hpMult),
      baseDmg: def.dmg * br.dmgMult,
      baseSpeed: def.pathSpeed * br.speedMult,
      atkCd: 0,
    });
    return { ok: true };
  }

  if (type === "place_wall") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    if (gx === SPAWN_X && gy === SPAWN_YG) return { ok: false, reason: "Cannot wall spawn tile" };
    if (gx === CASTLE_X && gy === CASTLE_YG) return { ok: false, reason: "Cannot wall castle tile" };
    const tile = lane.grid[gx][gy];
    if (tile.type !== "empty") return { ok: false, reason: "Tile not empty" };
    if (lane.wallCount >= MAX_WALLS) return { ok: false, reason: "Wall limit reached" };
    if (lane.gold < WALL_COST) return { ok: false, reason: "Not enough gold" };

    tile.type = "wall";
    const newPath = bfsPath(lane.grid);
    if (!newPath) {
      tile.type = "empty";
      return { ok: false, reason: "Wall would block all paths" };
    }

    lane.gold -= WALL_COST;
    lane.wallCount += 1;
    lane.path = newPath;
    return { ok: true };
  }

  if (type === "convert_wall") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    const towerType = String((data && data.towerType) || "").toLowerCase();
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    const tile = lane.grid[gx][gy];
    if (tile.type !== "wall") return { ok: false, reason: "Tile is not a wall" };
    if (!TOWER_DEFS[towerType]) return { ok: false, reason: "Unknown towerType" };
    const cost = TOWER_DEFS[towerType].cost;
    if (lane.gold < cost) return { ok: false, reason: "Not enough gold" };

    tile.type = "tower";
    tile.towerType = towerType;
    tile.towerLevel = 1;
    tile.atkCd = 0;
    lane.wallCount -= 1; // wall→tower: no longer counts toward wall limit

    // Tower blocks same as wall so path is unchanged — re-verify to be safe
    const newPath = bfsPath(lane.grid);
    if (!newPath) {
      tile.type = "wall";
      tile.towerType = null;
      tile.towerLevel = 0;
      lane.wallCount += 1;
      return { ok: false, reason: "Would block path" };
    }

    lane.gold -= cost;
    lane.path = newPath;
    return { ok: true };
  }

  if (type === "upgrade_tower") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    const tile = lane.grid[gx][gy];
    if (tile.type !== "tower") return { ok: false, reason: "No tower at that position" };
    if (tile.towerLevel >= TOWER_MAX_LEVEL) return { ok: false, reason: "Tower already maxed" };
    const nextLevel = tile.towerLevel + 1;
    const cost = getTowerUpgradeCost(tile.towerType, nextLevel);
    if (lane.gold < cost) return { ok: false, reason: "Not enough gold" };

    lane.gold -= cost;
    tile.towerLevel = nextLevel;
    const stats = getTowerStats(tile.towerType, nextLevel);
    if (tile.atkCd > stats.atkCdTicks) tile.atkCd = stats.atkCdTicks;
    return { ok: true };
  }

  if (type === "upgrade_barracks") {
    const currentLevel = lane.barracks.level;
    const nextLevel = currentLevel + 1;
    if (nextLevel >= BARRACKS_LEVELS.length) return { ok: false, reason: "Barracks already maxed" };
    const nextDef = BARRACKS_LEVELS[nextLevel];
    if (lane.income < nextDef.reqIncome) return { ok: false, reason: `Need ${nextDef.reqIncome}g income` };
    if (lane.gold < nextDef.cost) return { ok: false, reason: "Not enough gold" };

    lane.gold -= nextDef.cost;
    lane.barracks = {
      level: nextLevel,
      hpMult: nextDef.hpMult,
      dmgMult: nextDef.dmgMult,
      speedMult: nextDef.speedMult,
      structMult: nextDef.structMult,
    };
    return { ok: true };
  }

  return { ok: false, reason: "Unknown action type" };
}

function mlTick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;

  // Income
  game.incomeTickCounter += 1;
  if (game.incomeTickCounter >= INCOME_INTERVAL_TICKS) {
    game.incomeTickCounter = 0;
    for (const lane of game.lanes) {
      if (!lane.eliminated) lane.gold += lane.income;
    }
  }

  for (const lane of game.lanes) {
    if (lane.eliminated) continue;

    // Drain spawn queue
    while (lane.spawnQueue.length > 0) {
      const unit = lane.spawnQueue.shift();
      unit.pathIdx = 0;
      lane.units.push(unit);
    }

    // Decrement unit cooldowns
    for (const u of lane.units) {
      if (u.atkCd > 0) u.atkCd -= 1;
    }

    // Decrement tower cooldowns
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type === "tower" && tile.atkCd > 0) tile.atkCd -= 1;
      }
    }

    // Tower attacks — target unit with highest pathIdx within range
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type !== "tower" || !tile.towerType || tile.atkCd > 0) continue;
        const stats = getTowerStats(tile.towerType, tile.towerLevel || 1);
        if (!stats) continue;

        let target = null;
        for (const u of lane.units) {
          if (u.hp <= 0) continue;
          const pos = getUnitTilePos(u, lane.path);
          if (!pos) continue;
          if (tileDist(tx, ty, pos.x, pos.y) <= stats.range) {
            if (!target || u.pathIdx > target.pathIdx) target = u;
          }
        }
        if (!target) continue;

        const targetPos = getUnitTilePos(target, lane.path);
        lane.projectiles.push({
          id: `p${game.nextProjectileId++}`,
          ownerLane: lane.laneIndex,
          sourceKind: "tower",
          projectileType: tile.towerType,
          damageType: stats.damageType,
          isSplash: stats.isSplash,
          targetKind: "unit",
          targetId: target.id,
          dmg: stats.dmg,
          fromX: tx, fromY: ty,
          toX: targetPos ? targetPos.x : CASTLE_X,
          toY: targetPos ? targetPos.y : CASTLE_YG,
          ticksRemaining: stats.projectileTicks,
          ticksTotal: stats.projectileTicks,
        });
        tile.atkCd = stats.atkCdTicks;
      }
    }

    // Unit movement
    const pathLen = lane.path ? lane.path.length : 1;
    let engagedCount = 0;
    for (const u of lane.units) {
      if (u.hp > 0 && u.pathIdx >= pathLen - 1) engagedCount++;
    }

    for (const u of lane.units) {
      if (u.hp <= 0) continue;
      const def = UNIT_DEFS[u.type];

      // At gate
      if (u.pathIdx >= pathLen - 1) {
        if (engagedCount <= ENGAGEMENT_SLOTS && u.atkCd <= 0) {
          lane.gateHp -= u.baseDmg;
          u.atkCd = def.atkCdTicks;
          if (lane.gateHp <= 0) {
            lane.gateHp = 0;
            lane.eliminated = true;
            if (u.ownerLane !== lane.laneIndex) {
              const ownerLane = game.lanes[u.ownerLane];
              if (ownerLane) ownerLane.gold += GATE_KILL_BOUNTY;
            }
          }
        }
        continue;
      }

      // Ranged: fire at castle when in range
      if (def.ranged) {
        const pos = getUnitTilePos(u, lane.path);
        if (pos && tileDist(pos.x, pos.y, CASTLE_X, CASTLE_YG) <= def.range) {
          if (u.atkCd <= 0) {
            lane.projectiles.push({
              id: `p${game.nextProjectileId++}`,
              ownerLane: u.ownerLane,
              sourceKind: "unit",
              projectileType: u.type,
              damageType: def.damageType,
              isSplash: false,
              targetKind: "gate",
              targetLaneIndex: lane.laneIndex,
              dmg: u.baseDmg,
              fromX: pos.x, fromY: pos.y,
              toX: CASTLE_X, toY: CASTLE_YG,
              ticksRemaining: def.projectileTicks || 7,
              ticksTotal: def.projectileTicks || 7,
            });
            u.atkCd = def.atkCdTicks;
          }
          continue;
        }
      }

      // Advance along path
      u.pathIdx = Math.min(u.pathIdx + u.baseSpeed, pathLen - 1);
    }

    // Resolve projectiles
    const stillFlying = [];
    const goldGain = {};
    for (const p of lane.projectiles) {
      p.ticksRemaining -= 1;
      if (p.ticksRemaining > 0) { stillFlying.push(p); continue; }

      if (p.targetKind === "unit") {
        if (p.isSplash) {
          // Cannon splash: damage all units within SPLASH_RADIUS_TILES of landing tile
          const landX = p.toX, landY = p.toY;
          for (const u of lane.units) {
            if (u.hp <= 0) continue;
            const pos = getUnitTilePos(u, lane.path);
            if (!pos) continue;
            if (tileDist(landX, landY, pos.x, pos.y) <= SPLASH_RADIUS_TILES) {
              const armorType = (UNIT_DEFS[u.type] && UNIT_DEFS[u.type].armorType) || "MEDIUM";
              u.hp -= computeDamage(p.dmg, p.damageType || "SPLASH", armorType);
              if (u.hp <= 0) {
                const oi = lane.laneIndex;
                goldGain[oi] = (goldGain[oi] || 0) + (UNIT_DEFS[u.type] ? UNIT_DEFS[u.type].bounty : 1);
              }
            }
          }
        } else {
          const target = lane.units.find(u => u.id === p.targetId && u.hp > 0);
          if (target) {
            const armorType = (UNIT_DEFS[target.type] && UNIT_DEFS[target.type].armorType) || "MEDIUM";
            target.hp -= computeDamage(p.dmg, p.damageType || "NORMAL", armorType);
            if (target.hp <= 0) {
              const oi = lane.laneIndex;
              goldGain[oi] = (goldGain[oi] || 0) + (UNIT_DEFS[target.type] ? UNIT_DEFS[target.type].bounty : 1);
            }
          }
        }
        continue;
      }

      if (p.targetKind === "gate") {
        lane.gateHp -= p.dmg;
        if (lane.gateHp <= 0) {
          lane.gateHp = 0;
          lane.eliminated = true;
          const ownerLane = game.lanes[p.ownerLane];
          if (ownerLane) ownerLane.gold += GATE_KILL_BOUNTY;
        }
      }
    }
    lane.projectiles = stillFlying;

    // Apply kill bounties to defending lane
    for (const [idx, g] of Object.entries(goldGain)) {
      const gl = game.lanes[Number(idx)];
      if (gl && !gl.eliminated) gl.gold += g;
    }

    // Remove dead units
    lane.units = lane.units.filter(u => u.hp > 0);
  }

  // Win condition
  const activeLanes = game.lanes.filter(l => !l.eliminated);
  if (activeLanes.length === 1) {
    game.phase = "ended";
    game.winner = activeLanes[0].laneIndex;
  } else if (activeLanes.length === 0) {
    game.phase = "ended";
    game.winner = null;
  }
}

function createMLSnapshot(game) {
  return {
    tick: game.tick,
    phase: game.phase,
    winner: game.winner,
    incomeTicksRemaining: INCOME_INTERVAL_TICKS - game.incomeTickCounter,
    lanes: game.lanes.map(lane => {
      const walls = [];
      const towerCells = [];
      for (let x = 0; x < GRID_W; x++) {
        for (let y = 0; y < GRID_H; y++) {
          const tile = lane.grid[x][y];
          if (tile.type === "wall") walls.push({ x, y });
          else if (tile.type === "tower") towerCells.push({ x, y, type: tile.towerType, level: tile.towerLevel });
        }
      }

      const projectiles = lane.projectiles.map(p => ({
        id: p.id,
        ownerLane: p.ownerLane,
        sourceKind: p.sourceKind,
        projectileType: p.projectileType,
        damageType: p.damageType,
        isSplash: p.isSplash,
        fromX: p.fromX, fromY: p.fromY,
        toX: p.toX, toY: p.toY,
        progress: 1 - p.ticksRemaining / p.ticksTotal,
      }));

      return {
        laneIndex: lane.laneIndex,
        eliminated: lane.eliminated,
        gold: lane.gold,
        income: lane.income,
        gateHp: lane.gateHp,
        barracksLevel: lane.barracks.level,
        wallCount: lane.wallCount,
        walls,
        towerCells,
        path: lane.path || [],
        units: lane.units.map(u => {
          const pos = getUnitTilePos(u, lane.path);
          return {
            id: u.id,
            ownerLane: u.ownerLane,
            type: u.type,
            pathIdx: u.pathIdx,
            gridX: pos ? pos.x : SPAWN_X,
            gridY: pos ? pos.y : SPAWN_YG,
            normProgress: (lane.path && lane.path.length > 1) ? u.pathIdx / (lane.path.length - 1) : 0,
            hp: u.hp,
            maxHp: u.maxHp,
          };
        }),
        spawnQueueLength: lane.spawnQueue.length,
        projectiles,
      };
    }),
  };
}

function createMLPublicConfig() {
  return {
    tickHz: TICK_HZ,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    startGold: START_GOLD,
    gateHpStart: GATE_HP_START,
    gridW: GRID_W,
    gridH: GRID_H,
    wallCost: WALL_COST,
    maxWalls: MAX_WALLS,
    maxPathLen: MAX_PATH_LEN,
    unitDefs: UNIT_DEFS,
    towerDefs: TOWER_DEFS,
    towerMaxLevel: TOWER_MAX_LEVEL,
    // barracksLevels[0]=level1, [1]=level2, [2]=level3, [3]=level4
    barracksLevels: BARRACKS_LEVELS.slice(1),
  };
}

module.exports = {
  TICK_HZ,
  TICK_MS,
  UNIT_DEFS,
  TOWER_DEFS,
  GRID_W,
  GRID_H,
  createMLGame,
  applyMLAction,
  mlTick,
  createMLSnapshot,
  createMLPublicConfig,
};
