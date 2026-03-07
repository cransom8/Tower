// server/sim-multilane.js
"use strict";

/**
 * Multi-lane PvP simulation — 10×28 tile grid per player lane.
 * Units follow a BFS path from spawn tile to castle tile.
 * Wall placement requires BFS validation + combat lock.
 * Barracks provides global unit-tech upgrades.
 *
 * Wired to sim-core (computeDamage, fireProjectile, resolveProjectile, mulberry32 RNG)
 * and the unitTypes DB cache. UNIT_DEFS / TOWER_DEFS serve as last-resort fallbacks
 * for units not present in the DB (e.g. during local dev without migrations).
 */

const {
  computeDamage: coreComputeDamage,
  fireProjectile,
  resolveProjectile,
  resolveAbilityHook,
  applyAuras,
  resolveStatuses,
  applySeparation,
  mulberry32,
} = require("./sim-core");
const { getUnitType, getAllUnitTypes } = require("./unitTypes");
const {
  getCurrentBarracksMult,
  getBarracksUpgradeDef,
  getMaxBarracksLevel,
} = require("./barracksLevels");

const TICK_HZ = 20;
const TICK_MS = Math.floor(1000 / TICK_HZ);
const INCOME_INTERVAL_TICKS = 240; // 12 s
const START_GOLD = 70;
const START_INCOME = 10;
const LIVES_START = 20;
const TOWER_MAX_LEVEL = 10;
const MAX_UNITS_PER_LANE = 200;
const GATE_KILL_BOUNTY = 10;

// Grid constants
const GRID_W = 11;
const GRID_H = 28;
const SPAWN_X = 5;
const SPAWN_YG = 0;
const CASTLE_X = 5;
const CASTLE_YG = 27;
const MAX_PATH_LEN = GRID_W * GRID_H; // max possible BFS path in a 11×28 grid
const MAX_WALLS = null; // unlimited
const WALL_COST = 2;
const SPLASH_RADIUS_TILES = 1.5;
const SEND_INTERVAL_TICKS = 5;     // ticks between send-queue drains (0.25s at 20Hz)
const QUEUE_CAP = 200;             // max units in send queue per lane
const MIN_UNIT_SPACING = 0.15;     // minimum pathIdx gap enforced by applySeparation
const TOWER_TARGET_MODES = new Set(["first", "last", "weakest", "strongest"]);

// Warlock debuff constants (3-second window at 20 hz)
const WARLOCK_DEBUFF_CD    = 60;  // ticks between debuff attempts
const WARLOCK_DEBUFF_TICKS = 60;  // debuff duration in ticks
const WARLOCK_DEBUFF_MULT  = 0.75; // -25% tower damage
const WARLOCK_DEBUFF_RANGE = 3.5;  // tile radius

// ── Hardcoded fallback definitions ────────────────────────────────────────────
// Used when DB unit types are not loaded. Fields now include armorType,
// damageType, and damageReductionPct explicitly for use with sim-core.
const UNIT_DEFS = {
  runner:   { cost: 8,  income: 0.5, hp: 60,  dmg: 7,  atkCdTicks: 7,  pathSpeed: 0.060375, bounty: 2, range: 0, ranged: false, armorType: "UNARMORED", damageType: "NORMAL", damageReductionPct: 0 },
  footman:  { cost: 10, income: 1,   hp: 90,  dmg: 8,  atkCdTicks: 8,  pathSpeed: 0.036225, bounty: 3, range: 0, ranged: false, armorType: "MEDIUM",    damageType: "NORMAL", damageReductionPct: 0 },
  ironclad: { cost: 16, income: 2,   hp: 160, dmg: 9,  atkCdTicks: 10, pathSpeed: 0.036225, bounty: 4, range: 0, ranged: false, armorType: "HEAVY",     damageType: "NORMAL", damageReductionPct: 20, warlockDebuff: false },
  warlock:  { cost: 18, income: 2,   hp: 80,  dmg: 12, atkCdTicks: 11, pathSpeed: 0.036225, bounty: 5, range: 0, ranged: false, armorType: "MAGIC",     damageType: "MAGIC",  damageReductionPct: 0,  warlockDebuff: true },
  golem:    { cost: 25, income: 3,   hp: 240, dmg: 14, atkCdTicks: 13, pathSpeed: 0.024150, bounty: 6, range: 0, ranged: false, armorType: "HEAVY",     damageType: "NORMAL", damageReductionPct: 0,  structBonus: 0.25 },
};
// Default auto-send priority: loadout slot order is used when set; cost-descending as fallback
const AUTOSEND_PRIORITY = Object.keys(UNIT_DEFS).sort((a, b) => UNIT_DEFS[b].cost - UNIT_DEFS[a].cost);

const TOWER_DEFS = {
  archer:   { cost: 10, range: 4.2,  dmg: 8.5,  atkCdTicks: 11, projectileTicks: 7, damageType: "PIERCE", isSplash: false },
  fighter:  { cost: 12, range: 2.5,  dmg: 10.0, atkCdTicks: 10, projectileTicks: 6, damageType: "NORMAL", isSplash: false },
  mage:     { cost: 24, range: 4.0,  dmg: 15.0, atkCdTicks: 13, projectileTicks: 7, damageType: "MAGIC",  isSplash: false },
  ballista: { cost: 20, range: 5.0,  dmg: 12.1, atkCdTicks: 14, projectileTicks: 8, damageType: "SIEGE",  isSplash: false },
  cannon:   { cost: 30, range: 3.84, dmg: 8,    atkCdTicks: 16, projectileTicks: 9, damageType: "SPLASH", isSplash: true  },
};

const BARRACKS_COST_BASE = 100;
const BARRACKS_REQ_INCOME_BASE = 8;

// ── Phase G: Ability system helpers ───────────────────────────────────────────

// Maps ability_key → hook category
const ABILITY_HOOKS = {
  splash_damage:   "onAttack",
  pierce_targets:  "onAttack",
  chain_lightning: "onAttack",
  slow:            "onHit",
  freeze:          "onHit",
  poison:          "onHit",
  burn:            "onHit",
  armor_reduction: "onHit",
  reveal_stealth:  "onTick",
  knockback:       "onHit",
  teleport_back:   "onHit",
  aura_damage:     "onSpawn",
  aura_atk_speed:  "onSpawn",
  aura_range:      "onSpawn",
  aura_cooldown:   "onSpawn",
};

// Maps aura ability_key → auraType used in lane.activeAuras
const ABILITY_AURA_TYPES = {
  aura_damage:    "dmg_bonus",
  aura_atk_speed: "atk_speed_bonus",
  aura_range:     "range_bonus",
  aura_cooldown:  "cooldown_reduction",
};

/**
 * Translate raw DB ability params into the format expected by sim-core
 * _executeAbility. Converts named params (e.g. slow_pct, dps) to
 * internal names (speedMult, dmgPerTick).
 */
function translateAbilityParams(abilityKey, rawParams) {
  switch (abilityKey) {
    case "slow":
      return {
        speedMult:     1 - (rawParams.slow_pct || 25) / 100,
        durationTicks: Math.round((rawParams.duration || 2) * TICK_HZ),
      };
    case "freeze":
      return {
        durationTicks: Math.round((rawParams.duration || 1) * TICK_HZ),
        procChance:    rawParams.proc_chance || 20,
      };
    case "poison":
      return {
        dmgPerTick:    (rawParams.dps || 5) / TICK_HZ,
        durationTicks: Math.round((rawParams.duration || 4) * TICK_HZ),
      };
    case "burn":
      return {
        dmgPerTick:    (rawParams.dps || 8) / TICK_HZ,
        durationTicks: Math.round((rawParams.duration || 3) * TICK_HZ),
      };
    case "armor_reduction":
      return {
        reductionPct:  rawParams.reduction_pct || 20,
        durationTicks: Math.round((rawParams.duration || 5) * TICK_HZ),
      };
    case "knockback":
      return {
        tiles:      Math.max(1, Math.round((rawParams.distance || 0.05) * GRID_H)),
        procChance: rawParams.proc_chance || 15,
      };
    case "teleport_back":
      return {
        procChance: rawParams.proc_chance || 10,
      };
    case "chain_lightning":
      return {
        maxJumps:   rawParams.chains     || 3,
        jumpRange:  2.0,
        dmgFalloff: 1 - (rawParams.decay_pct || 25) / 100,
      };
    case "pierce_targets":
      return {
        maxTargets:   rawParams.max_targets || 3,
        pierceRadius: 1.0,
      };
    case "splash_damage":
      return {
        radius: (rawParams.radius || 0.05) * GRID_W,
      };
    default:
      return rawParams;
  }
}

/**
 * Build the abilities array for a unit/tower type from the DB-loaded unitType.
 * Returns [] if the type has no abilities or isn't in the DB.
 * @param {string} unitTypeKey
 * @returns {object[]} abilities in sim-core format
 */
function buildAbilitiesForUnitType(unitTypeKey) {
  const ut = getUnitType(unitTypeKey);
  if (!ut || !Array.isArray(ut.abilities) || ut.abilities.length === 0) return [];
  return ut.abilities.map((a, idx) => {
    const abilityKey = a.ability_key;
    const rawParams  = (a.params && typeof a.params === "object") ? a.params : {};
    const hook       = ABILITY_HOOKS[abilityKey] || "onTick";
    const isAura     = hook === "onSpawn";
    const params     = isAura
      ? { auraType: ABILITY_AURA_TYPES[abilityKey] || "dmg_bonus",
          value:    rawParams.boost_pct || rawParams.value || 0,
          ...rawParams }
      : translateAbilityParams(abilityKey, rawParams);
    return {
      type:      isAura ? "aura" : abilityKey,
      hook,
      params,
      priority:  idx,
      abilityId: idx,
    };
  });
}

// ── DB-first unit/tower resolution ────────────────────────────────────────────

/**
 * Resolve a unit definition — DB-first, falls back to UNIT_DEFS.
 * DB is authoritative; UNIT_DEFS used only as last-resort fallback.
 */
function resolveUnitDef(key) {
  const ut = getUnitType(key);
  if (!ut) return UNIT_DEFS[key] || null;
  // Fixed-only units (e.g. archer, wall_placeholder) cannot be sent as attackers
  if (ut.behavior_mode === "fixed") return null;
  const base = UNIT_DEFS[key] || {};
  const sp   = (ut.special_props && typeof ut.special_props === "object") ? ut.special_props : {};
  const attackSpeed = Math.max(0.01, Number(ut.attack_speed) || 0.01);
  return {
    cost:               Number(ut.send_cost),
    income:             Number(ut.income),
    hp:                 Number(ut.hp),
    dmg:                Number(ut.attack_damage),
    atkCdTicks:         Math.max(1, Math.round(TICK_HZ / attackSpeed)),
    pathSpeed:          Number(ut.path_speed),
    bounty:             Number(ut.bounty) || base.bounty || 1,
    range:              Number(ut.range),
    ranged:             Number(ut.range) > 0,
    armorType:          ut.armor_type   || "MEDIUM",
    damageType:         ut.damage_type  || "NORMAL",
    damageReductionPct: Number(ut.damage_reduction_pct) || 0,
    warlockDebuff:      sp.warlockDebuff  != null ? !!sp.warlockDebuff  : !!base.warlockDebuff,
    structBonus:        sp.structBonus    != null ?  +sp.structBonus    : (base.structBonus || 0),
    barracks_scales_hp:  ut.barracks_scales_hp  === true,
    barracks_scales_dmg: ut.barracks_scales_dmg === true,
  };
}

/**
 * Resolve a tower definition — DB-first, falls back to TOWER_DEFS.
 * DB range is stored normalised to [0,1] × GRID_W.
 */
function resolveTowerDef(key) {
  const ut = getUnitType(key);
  if (!ut) return TOWER_DEFS[key] || null;
  const base = TOWER_DEFS[key] || {};
  const attackSpeed = Math.max(0.01, Number(ut.attack_speed) || 0.01);
  const dbBehavior = ut.proj_behavior || null;
  const dbBehaviorParams = (ut.proj_behavior_params && typeof ut.proj_behavior_params === "object")
    ? ut.proj_behavior_params : null;
  // Fall back: splash damage type → splash behavior; otherwise single
  const fallbackBehavior = ut.damage_type === "SPLASH" ? "splash" : "single";
  const fallbackParams   = ut.damage_type === "SPLASH" ? { radius: SPLASH_RADIUS_TILES } : {};
  return {
    cost:            Number(ut.build_cost),
    range:           Number(ut.range) * GRID_W,   // DB range normalised to [0,1] × GRID_W
    dmg:             Number(ut.attack_damage),
    atkCdTicks:      Math.max(1, Math.round(TICK_HZ / attackSpeed)),
    projectileTicks: Number(ut.projectile_travel_ticks) || base.projectileTicks || 7,
    damageType:      ut.damage_type || "NORMAL",
    projBehavior:       dbBehavior       || fallbackBehavior,
    projBehaviorParams: dbBehaviorParams || fallbackParams,
    isSplash:        (dbBehavior || fallbackBehavior) === "splash",
  };
}

// ── Barracks helpers ───────────────────────────────────────────────────────────

function getBarracksLevelDef(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  if (lvl === 1) {
    return {
      hpMult: 1,
      dmgMult: 1,
      speedMult: 1,
      structMult: 1,
      unitCostMult: 1,
      unitIncomeMult: 1,
      incomeBonus: 0,
      cost: 0,
      reqIncome: 0,
    };
  }
  const statMult = Math.pow(2, lvl - 1);       // x2 per barracks upgrade
  const gateMult = Math.pow(2, lvl - 2);
  return {
    hpMult: statMult,
    dmgMult: statMult,
    speedMult: 1,
    structMult: statMult,
    unitCostMult: statMult,
    unitIncomeMult: statMult,
    incomeBonus: 0,
    cost: Math.ceil(BARRACKS_COST_BASE * gateMult),
    reqIncome: Math.ceil(BARRACKS_REQ_INCOME_BASE * gateMult),
  };
}

function getBarracksSpeedMult(_br) {
  return 1;
}

function getBarracksUnitCostMult(br) {
  if (!br || typeof br !== "object") return 1;
  if (Number.isFinite(br.unitCostMult)) return Math.max(0, Number(br.unitCostMult));
  if (Number.isFinite(br.hpMult)) return Math.max(0, Number(br.hpMult));
  return 1;
}

function getBarracksUnitIncomeMult(br) {
  if (!br || typeof br !== "object") return 1;
  if (Number.isFinite(br.unitIncomeMult)) return Math.max(0, Number(br.unitIncomeMult));
  if (Number.isFinite(br.hpMult)) return Math.max(0, Number(br.hpMult));
  return 1;
}

// ── Tower stat helpers ─────────────────────────────────────────────────────────

function getTowerUpgradeCost(type, nextLevel) {
  const base = resolveTowerDef(type);
  if (!base) return Infinity;
  return Math.ceil(base.cost * (0.75 + 0.25 * nextLevel));
}

function getTowerTotalCost(type, level) {
  const base = resolveTowerDef(type);
  if (!base) return 0;
  let total = WALL_COST + base.cost; // wall placement + level-1 conversion
  for (let lvl = 2; lvl <= level; lvl++) {
    total += getTowerUpgradeCost(type, lvl);
  }
  return total;
}

function getTowerSellValue(type, level) {
  return Math.floor(getTowerTotalCost(type, level) * 0.7);
}

function getTowerStats(type, level) {
  const base = resolveTowerDef(type);
  if (!base) return null;
  const lvl = Math.max(1, Math.min(TOWER_MAX_LEVEL, level));
  const s = lvl - 1;
  return {
    dmg: base.dmg * (1 + 0.12 * s),
    range: base.range * (1 + 0.015 * s),
    atkCdTicks: Math.max(5, Math.round(base.atkCdTicks * (1 - 0.015 * s))),
    projectileTicks:    base.projectileTicks,
    damageType:         base.damageType,
    projBehavior:       base.projBehavior       || (base.isSplash ? "splash" : "single"),
    projBehaviorParams: base.projBehaviorParams  || (base.isSplash ? { radius: SPLASH_RADIUS_TILES } : {}),
    isSplash: base.isSplash || false,
  };
}

// ── Grid helpers ──────────────────────────────────────────────────────────────

function makeGrid() {
  const grid = [];
  for (let x = 0; x < GRID_W; x++) {
    grid[x] = [];
    for (let y = 0; y < GRID_H; y++) {
      grid[x][y] = { type: "empty", towerType: null, towerLevel: 0, atkCd: 0, targetMode: "first" };
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

function clampNum(v, min, max, fallback) {
  const n = Number(v);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(min, Math.min(max, n));
}

function normalizeGameOptions(options) {
  const src = options && typeof options === "object" ? options : {};
  const laneTeamsRaw = Array.isArray(src.laneTeams) ? src.laneTeams : [];
  const laneTeams = laneTeamsRaw.map((team, idx) => {
    const normalized = String(team || "").trim();
    if (normalized.length > 0) return normalized.slice(0, 24);
    return idx % 2 === 0 ? "red" : "blue";
  });
  return {
    startGold: Math.floor(clampNum(src.startGold, 0, 10000, START_GOLD)),
    startIncome: clampNum(src.startIncome, 0, 1000, START_INCOME),
    laneTeams,
    matchSeed: typeof src.matchSeed === "number" ? (src.matchSeed >>> 0) : undefined,
  };
}

// Get current tile position of a unit from its pathIdx
function getUnitTilePos(unit, path) {
  if (!path || path.length === 0) return null;
  const idx = Math.min(Math.floor(unit.pathIdx), path.length - 1);
  return path[idx];
}

// ── Public API ────────────────────────────────────────────────────────────────

function createMLGame(playerCount, options) {
  const opt = normalizeGameOptions(options);
  const lanes = [];
  for (let i = 0; i < playerCount; i++) {
    const grid = makeGrid();
    const path = bfsPath(grid); // straight line (5,0)→(5,27)
    lanes.push({
      laneIndex: i,
      team: opt.laneTeams[i] || (i % 2 === 0 ? "red" : "blue"),
      eliminated: false,
      gold: opt.startGold,
      income: opt.startIncome,
      incomeRemainder: 0,
      lives: LIVES_START,
      grid,
      path,
      wallCount: 0,
      barracks: Object.assign({ level: 1 }, getBarracksLevelDef(1)),
      units: [],
      spawnQueue: [],
      projectiles: [],
      sendQueue: [],        // flat array of unit-type keys pending drain
      sendDrainCounter: 0,  // ticks until next drain
      autosend: {
        enabled: false,
        enabledUnits: {},  // populated from loadout keys when match starts
        rate: "normal",
        tickCounter: 0,
        loadoutKeys: null,  // ordered unit-type keys for auto-send; set from index.js
      },
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
    // Phase B: seeded RNG + versioning + action sequencing
    rng: mulberry32(opt.matchSeed !== undefined ? opt.matchSeed : (Date.now() >>> 0)),
    matchSeed: opt.matchSeed !== undefined ? opt.matchSeed : null,
    configVersionId: null,
    actionSeq: 0,
  };
}

function parseBulkTiles(rawTiles) {
  if (!Array.isArray(rawTiles)) return { ok: false, reason: "Tiles must be an array" };
  if (rawTiles.length === 0) return { ok: false, reason: "No tiles selected" };
  if (rawTiles.length > 150) return { ok: false, reason: "Too many tiles selected" };

  const dedup = new Map();
  for (const raw of rawTiles) {
    const gx = Number((raw && raw.gridX !== undefined) ? raw.gridX : raw && raw.x);
    const gy = Number((raw && raw.gridY !== undefined) ? raw.gridY : raw && raw.y);
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position in tiles list" };
    }
    dedup.set(gx + "," + gy, { gx, gy });
  }
  return { ok: true, tiles: Array.from(dedup.values()) };
}

function isOpponentLane(game, sourceLaneIndex, targetLaneIndex) {
  const sourceLane = game && game.lanes && game.lanes[sourceLaneIndex];
  const targetLane = game && game.lanes && game.lanes[targetLaneIndex];
  if (!sourceLane || !targetLane) return false;
  if (targetLane.eliminated) return false;
  if (sourceLaneIndex === targetLaneIndex) return false;
  return sourceLane.team !== targetLane.team;
}

function findNextActiveOpponentLaneIndex(game, fromLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || game.lanes.length <= 1) return null;
  const total = Math.min(Number(game.playerCount) || game.lanes.length, game.lanes.length);
  for (let step = 1; step < total; step++) {
    const idx = (fromLaneIndex + step) % total;
    if (isOpponentLane(game, fromLaneIndex, idx)) return idx;
  }
  return null;
}

function applyMLAction(game, laneIndex, action) {
  if (!game || game.phase !== "playing") return { ok: false, reason: "Game not active" };
  if (laneIndex < 0 || laneIndex >= game.lanes.length) return { ok: false, reason: "Bad laneIndex" };
  const lane = game.lanes[laneIndex];
  if (lane.eliminated) return { ok: false, reason: "You have been eliminated" };
  if (!action || typeof action.type !== "string") return { ok: false, reason: "Bad action" };

  // Phase B: action sequencing — stamp canonical replay fields
  game.actionSeq = (game.actionSeq || 0) + 1;
  action.tickApply = game.tick;
  action.laneId    = laneIndex;
  action.actionSeq = game.actionSeq;

  const { type, data } = action;

  if (type === "spawn_unit") {
    const unitType = String((data && data.unitType) || "").toLowerCase();
    const def = resolveUnitDef(unitType);
    if (!def) return { ok: false, reason: "Unknown unitType" };
    const _loadoutKeys = lane.autosend && lane.autosend.loadoutKeys;
    if (_loadoutKeys && _loadoutKeys.length > 0 && !_loadoutKeys.includes(unitType)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    if (lane.gold < def.cost) return { ok: false, reason: "Not enough gold" };
    if (lane.sendQueue.length >= QUEUE_CAP) return { ok: false, reason: "Send queue full" };

    // Phase D: deduct gold/income immediately; unit drained every SEND_INTERVAL_TICKS
    // Phase F: send_cost and income are NOT affected by barracks level
    lane.gold -= def.cost;
    lane.income += def.income;
    lane.sendQueue.push(unitType);
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
    if (lane.gold < WALL_COST) return { ok: false, reason: "Not enough gold" };

    tile.type = "wall";
    const newPath = bfsPath(lane.grid);
    if (!newPath) {
      tile.type = "empty";
      return { ok: false, reason: "Wall would block all paths" };
    }

    lane.gold -= WALL_COST;
    lane.wallCount += 1;
    tile.costHistory = [{ cost: WALL_COST, refundPct: 100 }];
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
    if (!resolveTowerDef(towerType)) return { ok: false, reason: "Unknown towerType" };
    const _lkConvert = lane.autosend && lane.autosend.loadoutKeys;
    if (_lkConvert && _lkConvert.length > 0 && !_lkConvert.includes(towerType)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    const cost = resolveTowerDef(towerType).cost;
    if (lane.gold < cost) return { ok: false, reason: "Not enough gold" };

    tile.type = "tower";
    tile.towerType = towerType;
    tile.towerLevel = 1;
    tile.atkCd = 0;
    tile.targetMode = "first";
    tile.debuffEndTick = 0;
    tile.debuffMult = 1.0;
    tile.abilities = buildAbilitiesForUnitType(towerType);
    lane.wallCount -= 1; // wall→tower: no longer counts toward wall limit

    // Tower blocks same as wall so path is unchanged — re-verify to be safe
    const newPath = bfsPath(lane.grid);
    if (!newPath) {
      tile.type = "wall";
      tile.towerType = null;
      tile.towerLevel = 0;
      tile.abilities = null;
      lane.wallCount += 1;
      return { ok: false, reason: "Would block path" };
    }

    lane.gold -= cost;
    lane.path = newPath;
    if (!tile.costHistory) tile.costHistory = [{ cost: WALL_COST, refundPct: 100 }];
    tile.costHistory.push({ cost, refundPct: 70 });
    return { ok: true };
  }

  if (type === "upgrade_wall") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    const unitTypeKey = String((data && (data.unitTypeKey || data.towerType)) || "").toLowerCase();
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    const tile = lane.grid[gx][gy];
    if (tile.type !== "wall") return { ok: false, reason: "Tile is not a wall" };
    const ut = getUnitType(unitTypeKey);
    if (ut && ut.behavior_mode !== "fixed" && ut.behavior_mode !== "both") {
      return { ok: false, reason: "Unit type is not a fixed defender" };
    }
    const _lkUpgrade = lane.autosend && lane.autosend.loadoutKeys;
    if (_lkUpgrade && _lkUpgrade.length > 0 && !_lkUpgrade.includes(unitTypeKey)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    const def = resolveTowerDef(unitTypeKey);
    if (!def) return { ok: false, reason: "Unknown unit type" };
    const cost = def.cost;
    if (lane.gold < cost) return { ok: false, reason: "Not enough gold" };

    tile.type = "tower";
    tile.towerType = unitTypeKey;
    tile.towerLevel = 1;
    tile.atkCd = 0;
    tile.targetMode = "first";
    tile.debuffEndTick = 0;
    tile.debuffMult = 1.0;
    tile.abilities = buildAbilitiesForUnitType(unitTypeKey);
    lane.wallCount -= 1;

    const newPath = bfsPath(lane.grid);
    if (!newPath) {
      tile.type = "wall";
      tile.towerType = null;
      tile.towerLevel = 0;
      tile.atkCd = 0;
      tile.targetMode = "first";
      tile.debuffEndTick = 0;
      tile.debuffMult = 1.0;
      tile.abilities = null;
      lane.wallCount += 1;
      return { ok: false, reason: "Would block path" };
    }

    lane.gold -= cost;
    lane.path = newPath;
    if (!tile.costHistory) tile.costHistory = [{ cost: WALL_COST, refundPct: 100 }];
    tile.costHistory.push({ cost, refundPct: 70 });
    return { ok: true };
  }

  if (type === "bulk_convert_walls") {
    const towerType = String((data && data.towerType) || "").toLowerCase();
    if (!resolveTowerDef(towerType)) return { ok: false, reason: "Unknown towerType" };
    const _lkBulkConvert = lane.autosend && lane.autosend.loadoutKeys;
    if (_lkBulkConvert && _lkBulkConvert.length > 0 && !_lkBulkConvert.includes(towerType)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    const parsed = parseBulkTiles(data && data.tiles);
    if (!parsed.ok) return { ok: false, reason: parsed.reason };

    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      if (tile.type !== "wall") return { ok: false, reason: "One or more selected tiles are not walls" };
    }

    const totalCost = parsed.tiles.length * resolveTowerDef(towerType).cost;
    if (lane.gold < totalCost) return { ok: false, reason: "Not enough gold" };

    const unitCost = resolveTowerDef(towerType).cost;
    const bulkConvertAbilities = buildAbilitiesForUnitType(towerType);
    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      tile.type = "tower";
      tile.towerType = towerType;
      tile.towerLevel = 1;
      tile.atkCd = 0;
      tile.targetMode = "first";
      tile.debuffEndTick = 0;
      tile.debuffMult = 1.0;
      tile.abilities = bulkConvertAbilities;
      lane.wallCount -= 1;
    }

    const newPath = bfsPath(lane.grid);
    if (!newPath) {
      for (const pos of parsed.tiles) {
        const tile = lane.grid[pos.gx][pos.gy];
        tile.type = "wall";
        tile.towerType = null;
        tile.towerLevel = 0;
        tile.atkCd = 0;
        tile.targetMode = "first";
        tile.debuffEndTick = 0;
        tile.debuffMult = 1.0;
        tile.abilities = null;
        lane.wallCount += 1;
      }
      return { ok: false, reason: "Would block path" };
    }

    lane.gold -= totalCost;
    lane.path = newPath;
    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      if (!tile.costHistory) tile.costHistory = [{ cost: WALL_COST, refundPct: 100 }];
      tile.costHistory.push({ cost: unitCost, refundPct: 70 });
    }
    return { ok: true };
  }

  if (type === "bulk_upgrade_walls") {
    const unitTypeKey = String((data && (data.unitTypeKey || data.towerType)) || "").toLowerCase();
    if (!resolveTowerDef(unitTypeKey)) return { ok: false, reason: "Unknown unit type" };
    const ut = getUnitType(unitTypeKey);
    if (ut && ut.behavior_mode !== "fixed" && ut.behavior_mode !== "both") {
      return { ok: false, reason: "Unit type is not a fixed defender" };
    }
    const _lkBulk = lane.autosend && lane.autosend.loadoutKeys;
    if (_lkBulk && _lkBulk.length > 0 && !_lkBulk.includes(unitTypeKey)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    const parsed = parseBulkTiles(data && data.tiles);
    if (!parsed.ok) return { ok: false, reason: parsed.reason };

    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      if (tile.type !== "wall") return { ok: false, reason: "One or more selected tiles are not walls" };
    }

    const unitCostBulk = resolveTowerDef(unitTypeKey).cost;
    const totalCostBulk = parsed.tiles.length * unitCostBulk;
    if (lane.gold < totalCostBulk) return { ok: false, reason: "Not enough gold" };

    const bulkUpgradeAbilities = buildAbilitiesForUnitType(unitTypeKey);
    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      tile.type = "tower";
      tile.towerType = unitTypeKey;
      tile.towerLevel = 1;
      tile.atkCd = 0;
      tile.targetMode = "first";
      tile.debuffEndTick = 0;
      tile.debuffMult = 1.0;
      tile.abilities = bulkUpgradeAbilities;
      lane.wallCount -= 1;
    }

    const newPathBulk = bfsPath(lane.grid);
    if (!newPathBulk) {
      for (const pos of parsed.tiles) {
        const tile = lane.grid[pos.gx][pos.gy];
        tile.type = "wall";
        tile.towerType = null;
        tile.towerLevel = 0;
        tile.atkCd = 0;
        tile.targetMode = "first";
        tile.debuffEndTick = 0;
        tile.debuffMult = 1.0;
        tile.abilities = null;
        lane.wallCount += 1;
      }
      return { ok: false, reason: "Would block path" };
    }

    lane.gold -= totalCostBulk;
    lane.path = newPathBulk;
    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      if (!tile.costHistory) tile.costHistory = [{ cost: WALL_COST, refundPct: 100 }];
      tile.costHistory.push({ cost: unitCostBulk, refundPct: 70 });
    }
    return { ok: true };
  }

  if (type === "remove_wall") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    const tile = lane.grid[gx][gy];
    if (tile.type !== "wall") return { ok: false, reason: "Tile is not a wall" };
    const refund = (tile.costHistory && tile.costHistory.length > 0)
      ? Math.floor(tile.costHistory[0].cost * tile.costHistory[0].refundPct / 100)
      : WALL_COST;
    tile.type = "empty";
    tile.costHistory = null;
    lane.gold += refund;
    lane.wallCount -= 1;
    lane.path = bfsPath(lane.grid) || lane.path;
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
    if (!tile.costHistory) tile.costHistory = [];
    tile.costHistory.push({ cost, refundPct: 70 });
    const stats = getTowerStats(tile.towerType, nextLevel);
    if (tile.atkCd > stats.atkCdTicks) tile.atkCd = stats.atkCdTicks;
    return { ok: true };
  }

  if (type === "bulk_upgrade_towers") {
    const parsed = parseBulkTiles(data && data.tiles);
    if (!parsed.ok) return { ok: false, reason: parsed.reason };

    let selectedType = null;
    const upgradable = [];
    for (const pos of parsed.tiles) {
      const tile = lane.grid[pos.gx][pos.gy];
      if (tile.type !== "tower" || !tile.towerType) {
        return { ok: false, reason: "One or more selected tiles are not towers" };
      }
      if (!selectedType) selectedType = tile.towerType;
      if (tile.towerType !== selectedType) return { ok: false, reason: "Selected towers must be the same type" };
      upgradable.push(tile);
    }
    if (upgradable.length === 0) return { ok: false, reason: "No towers selected" };

    let upgradedCount = 0;
    for (const tile of upgradable) {
      if (tile.towerLevel >= TOWER_MAX_LEVEL) continue;
      const nextLevel = tile.towerLevel + 1;
      const cost = getTowerUpgradeCost(tile.towerType, nextLevel);
      if (lane.gold < cost) continue;
      lane.gold -= cost;
      tile.towerLevel = nextLevel;
      const stats = getTowerStats(tile.towerType, nextLevel);
      if (tile.atkCd > stats.atkCdTicks) tile.atkCd = stats.atkCdTicks;
      upgradedCount += 1;
    }
    if (upgradedCount === 0) return { ok: false, reason: "Not enough gold" };
    return { ok: true };
  }

  if (type === "set_tower_target") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    const targetMode = String((data && data.targetMode) || "").toLowerCase();
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    if (!TOWER_TARGET_MODES.has(targetMode)) return { ok: false, reason: "Bad target mode" };
    const tile = lane.grid[gx][gy];
    if (tile.type !== "tower") return { ok: false, reason: "No tower at that position" };
    tile.targetMode = targetMode;
    return { ok: true };
  }

  if (type === "upgrade_barracks") {
    const currentLevel = lane.barracks ? lane.barracks.level : 1;
    const nextLevel = currentLevel + 1;
    const maxLevel = getMaxBarracksLevel();
    if (nextLevel > maxLevel) return { ok: false, reason: "Barracks already at max level" };
    const nextDef = getBarracksUpgradeDef(nextLevel);
    if (!nextDef) {
      // DB not loaded; fall back to hardcoded formula
      const fallback = getBarracksLevelDef(nextLevel);
      if (lane.gold < fallback.cost) return { ok: false, reason: "Not enough gold" };
      lane.gold -= fallback.cost;
      lane.barracks = { level: nextLevel, multiplier: fallback.hpMult };
      return { ok: true };
    }
    if (lane.gold < nextDef.upgradeCost) return { ok: false, reason: "Not enough gold" };

    // Phase F: barracks stores level + DB multiplier only
    lane.gold -= nextDef.upgradeCost;
    lane.barracks = { level: nextLevel, multiplier: nextDef.multiplier };
    return { ok: true };
  }

  if (type === "sell_tower") {
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    const tile = lane.grid[gx][gy];
    if (tile.type !== "tower") return { ok: false, reason: "No tower at that position" };

    const sellValue = (tile.costHistory && tile.costHistory.length > 0)
      ? Math.floor(tile.costHistory.reduce((sum, item) => sum + item.cost * item.refundPct / 100, 0))
      : getTowerSellValue(tile.towerType, tile.towerLevel);
    tile.type = "empty";
    tile.towerType = null;
    tile.towerLevel = 0;
    tile.atkCd = 0;
    tile.costHistory = null;
    tile.abilities = null;

    const newPath = bfsPath(lane.grid);
    lane.path = newPath; // removing an obstacle can only open paths, never block
    lane.gold += sellValue;
    return { ok: true };
  }

  if (type === "set_autosend") {
    const as = lane.autosend;
    const { enabled, enabledUnits, rate, loadoutKeys } = data || {};
    if (typeof enabled === "boolean") {
      as.enabled = enabled;
    }
    if (enabledUnits && typeof enabledUnits === "object") {
      for (const ut of Object.keys(UNIT_DEFS)) {
        as.enabledUnits[ut] = !!enabledUnits[ut];
      }
    }
    const VALID_RATES = new Set(["slow", "normal", "fast"]);
    if (VALID_RATES.has(rate)) as.rate = rate;
    if (Array.isArray(loadoutKeys)) as.loadoutKeys = loadoutKeys.slice(0, 5);
    as.tickCounter = 0;
    if (as.enabled) runLaneAutosendFill(game, lane);
    return { ok: true };
  }

  return { ok: false, reason: "Unknown action type" };
}

/**
 * Phase D: fill lane.sendQueue from loadout slot order using available gold.
 * Runs each tick for auto-send-enabled lanes. Drain at SEND_INTERVAL_TICKS
 * is handled in mlTick per-lane loop.
 */
function runLaneAutosendFill(game, lane) {
  if (!game || !lane || lane.eliminated) return;
  const as = lane.autosend;
  if (!as || !as.enabled) return;
  const enabledUnits = as.enabledUnits || {};
  // Use loadout-ordered keys if set; otherwise fall back to cost-descending priority
  const allKeys = (Array.isArray(as.loadoutKeys) && as.loadoutKeys.length > 0)
    ? as.loadoutKeys : AUTOSEND_PRIORITY;
  const priority = allKeys.filter(ut => !!enabledUnits[ut]);
  if (priority.length === 0) return;

  // Phase F: send_cost and income NOT affected by barracks
  let iterations = 0;
  while (lane.sendQueue.length < QUEUE_CAP && iterations < QUEUE_CAP) {
    iterations++;
    let bought = false;
    for (const ut of priority) {
      const def = resolveUnitDef(ut);
      if (!def) continue;
      if (lane.gold < def.cost) continue;
      lane.gold -= def.cost;
      lane.income += def.income;
      lane.sendQueue.push(ut);
      bought = true;
      break; // restart from slot 0 each time
    }
    if (!bought) break;
  }
}

/**
 * Phase D: drain one unit from lane.sendQueue into the target opponent's spawnQueue.
 * Called every SEND_INTERVAL_TICKS from mlTick.
 * Unit stats are computed at drain time so barracks upgrades apply to queued units.
 */
function drainOneSendQueueUnit(game, lane) {
  if (!lane.sendQueue || lane.sendQueue.length === 0) return;
  const unitType = lane.sendQueue.shift();
  const def = resolveUnitDef(unitType);
  if (!def) return; // unknown type; discard silently
  const targetLaneIndex = findNextActiveOpponentLaneIndex(game, lane.laneIndex);
  if (targetLaneIndex === null) return; // no active opponents; unit lost
  const targetLane = game.lanes[targetLaneIndex];
  if (!targetLane || targetLane.spawnQueue.length >= MAX_UNITS_PER_LANE) return;
  // Phase F: DB-backed barracks multiplier, applied per unit type scaling flags
  const barracksMult = getCurrentBarracksMult(lane.barracks ? lane.barracks.level : 1);
  const scaledHp  = Math.ceil(def.hp  * (def.barracks_scales_hp  ? barracksMult : 1));
  const scaledDmg = def.dmg * (def.barracks_scales_dmg ? barracksMult : 1);
  const newUnit = {
    id: `u${game.nextUnitId++}`,
    ownerLane: lane.laneIndex,
    type: unitType,
    pathIdx: 0,
    hp: scaledHp,
    maxHp: scaledHp,
    baseDmg: scaledDmg,
    baseSpeed: def.pathSpeed,
    atkCd: 0,
    armorType: def.armorType || "MEDIUM",
    damageReductionPct: def.damageReductionPct || 0,
    abilities: buildAbilitiesForUnitType(unitType),
  };
  if (def.warlockDebuff) newUnit.warlockCd = WARLOCK_DEBUFF_CD;
  targetLane.spawnQueue.push(newUnit);
}

function mlTick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;

  // Income
  game.incomeTickCounter += 1;
  if (game.incomeTickCounter >= INCOME_INTERVAL_TICKS) {
    game.incomeTickCounter = 0;
    for (const lane of game.lanes) {
      if (lane.eliminated) continue;
      lane.gold += lane.income;
    }
  }

  // Phase D: auto-send fill — fill sendQueue from loadout slot order
  for (const lane of game.lanes) {
    if (lane.eliminated) continue;
    const as = lane.autosend;
    if (!as || !as.enabled) continue;
    runLaneAutosendFill(game, lane);
  }

  for (const lane of game.lanes) {
    if (lane.eliminated) continue;

    // Phase D: drain one unit from sendQueue every SEND_INTERVAL_TICKS
    lane.sendDrainCounter = (lane.sendDrainCounter || 0) + 1;
    if (lane.sendDrainCounter >= SEND_INTERVAL_TICKS) {
      lane.sendDrainCounter = 0;
      drainOneSendQueueUnit(game, lane);
    }

    // Drain spawn queue (units ready to enter the field)
    while (lane.spawnQueue.length > 0) {
      const unit = lane.spawnQueue.shift();
      unit.pathIdx = 0;
      lane.units.push(unit);
      // Phase G: trigger onSpawn (aura registration, etc.)
      if (unit.abilities && unit.abilities.length > 0) {
        resolveAbilityHook(game, lane, unit, "onSpawn", {});
      }
    }

    // Decrement unit cooldowns
    for (const u of lane.units) {
      if (u.atkCd > 0) u.atkCd -= 1;
      if (u.warlockCd !== undefined && u.warlockCd > 0) u.warlockCd -= 1;
    }

    // Decrement tower cooldowns
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type === "tower" && tile.atkCd > 0) tile.atkCd -= 1;
      }
    }

    function pickTowerTarget(tile, unitsInRange) {
      const mode = TOWER_TARGET_MODES.has(tile.targetMode) ? tile.targetMode : "first";
      let target = unitsInRange[0];
      if (mode === "strongest") {
        for (const u of unitsInRange) {
          if (u.hp > target.hp) target = u;
        }
        return target;
      }
      if (mode === "weakest") {
        for (const u of unitsInRange) {
          if (u.hp < target.hp) target = u;
        }
        return target;
      }
      if (mode === "last") {
        for (const u of unitsInRange) {
          if (u.pathIdx < target.pathIdx) target = u;
        }
        return target;
      }
      for (const u of unitsInRange) {
        if (u.pathIdx > target.pathIdx) target = u;
      }
      return target;
    }

    // Phase G: refresh aura state — collect from units + tower tiles
    const towerAuraSources = [];
    for (let ax = 0; ax < GRID_W; ax++) {
      for (let ay = 0; ay < GRID_H; ay++) {
        const atile = lane.grid[ax][ay];
        if (atile.type === "tower" && atile.abilities && atile.abilities.length > 0) {
          towerAuraSources.push({ abilities: atile.abilities });
        }
      }
    }
    applyAuras(game, lane, towerAuraSources);
    const activeAuras = lane.activeAuras || {};

    // Tower attacks — Phase B/G: use fireProjectile + ability onAttack context
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type !== "tower" || !tile.towerType || tile.atkCd > 0) continue;
        const stats = getTowerStats(tile.towerType, tile.towerLevel || 1);
        if (!stats) continue;

        // Phase G: apply aura bonuses to effective range and attack cooldown
        const auraRange = activeAuras.range_bonus    || 0;
        const auraSpd   = (activeAuras.atk_speed_bonus || 0) + (activeAuras.cooldown_reduction || 0);
        const effRange  = stats.range * (1 + auraRange / 100);
        const effAtkCd  = auraSpd > 0 ? Math.max(1, Math.round(stats.atkCdTicks * (1 - auraSpd / 100))) : stats.atkCdTicks;

        const unitsInRange = [];
        for (const u of lane.units) {
          if (u.hp <= 0) continue;
          const pos = getUnitTilePos(u, lane.path);
          if (!pos) continue;
          if (tileDist(tx, ty, pos.x, pos.y) <= effRange) {
            unitsInRange.push(u);
          }
        }
        if (unitsInRange.length === 0) continue;
        const target = pickTowerTarget(tile, unitsInRange);

        const debuffMult = (tile.debuffEndTick !== undefined && tile.debuffEndTick > game.tick)
          ? (tile.debuffMult || 1.0) : 1.0;
        const auraDmgMult = 1 + (activeAuras.dmg_bonus || 0) / 100;
        const effDmg = stats.dmg * debuffMult * auraDmgMult;

        // onAttack abilities may override behavior (e.g. splash_damage ability)
        const attackCtx = {
          target,
          behavior:       stats.projBehavior,
          behaviorParams: Object.assign({}, stats.projBehaviorParams),
        };
        if (tile.abilities && tile.abilities.length > 0) {
          resolveAbilityHook(game, lane, { abilities: tile.abilities }, "onAttack", attackCtx);
        }

        fireProjectile(game, lane,
          { id: `tower_${tx}_${ty}`, kind: "tower", x: tx, y: ty },
          target.id,
          {
            dmg:            effDmg,
            damageType:     stats.damageType,
            behavior:       attackCtx.behavior,
            behaviorParams: attackCtx.behaviorParams,
            travelTicks:    stats.projectileTicks,
            isSplash:       attackCtx.behavior === "splash",
            projectileType: tile.towerType,
            abilities:      tile.abilities || [],
          }
        );
        tile.atkCd = effAtkCd;
      }
    }

    // Unit movement
    const pathLen = lane.path ? lane.path.length : 1;
    const dotDeadIds = new Set(); // Phase G: units killed by DoT this tick

    for (const u of lane.units) {
      if (u.hp <= 0) continue;

      // Phase G: apply status effects (DoT, expiry)
      resolveStatuses(u, game.tick);
      if (u.hp <= 0) { dotDeadIds.add(u.id); continue; }

      const def = resolveUnitDef(u.type);

      // Reached castle — remove 1 life and despawn
      if (u.pathIdx >= pathLen - 1) {
        lane.lives -= 1;
        u.hp = 0; // despawn immediately
        if (lane.lives <= 0) {
          lane.lives = 0;
          lane.eliminated = true;
          if (isOpponentLane(game, lane.laneIndex, u.ownerLane)) {
            const ownerLane = game.lanes[u.ownerLane];
            if (ownerLane) ownerLane.gold += GATE_KILL_BOUNTY;
          }
        }
        continue;
      }

      // Warlock: attempt to debuff nearby tower every WARLOCK_DEBUFF_CD ticks
      if (def && def.warlockDebuff && (u.warlockCd === undefined || u.warlockCd <= 0)) {
        u.warlockCd = WARLOCK_DEBUFF_CD;
        const pos = getUnitTilePos(u, lane.path);
        if (pos) {
          let bestTile = null;
          let bestDist = Infinity;
          for (let dtx = 0; dtx < GRID_W; dtx++) {
            for (let dty = 0; dty < GRID_H; dty++) {
              const dtile = lane.grid[dtx][dty];
              if (dtile.type !== "tower") continue;
              const d = tileDist(pos.x, pos.y, dtx, dty);
              if (d <= WARLOCK_DEBUFF_RANGE && d < bestDist) {
                bestDist = d;
                bestTile = dtile;
              }
            }
          }
          if (bestTile) {
            // Non-stacking: refresh timer only
            bestTile.debuffEndTick = game.tick + WARLOCK_DEBUFF_TICKS;
            bestTile.debuffMult = WARLOCK_DEBUFF_MULT;
          }
        }
      }

      // Advance along path
      u.pathIdx = Math.min(u.pathIdx + u.baseSpeed, pathLen - 1);
    }

    // Phase H: push overlapping units apart so they don't stack on the same tile
    applySeparation(lane.units.filter(u => u.hp > 0), MIN_UNIT_SPACING);

    // Resolve projectiles — Phase B/G: use resolveProjectile; apply onHit abilities
    const killedById = new Set();
    const stillFlying = [];
    for (const p of lane.projectiles) {
      p.ticksRemaining -= 1;
      if (p.ticksRemaining > 0) { stillFlying.push(p); continue; }
      const { dead, hit } = resolveProjectile(game, lane, p);
      for (const id of dead) killedById.add(id);
      // Phase G: onHit abilities for each unit that received damage and survived
      if (p.abilities && p.abilities.length > 0 && hit.length > 0) {
        const attacker = { abilities: p.abilities };
        for (const hitId of hit) {
          if (killedById.has(hitId)) continue; // skip dead units
          const hitUnit = lane.units.find(u => u.id === hitId && u.hp > 0);
          if (hitUnit) resolveAbilityHook(game, lane, attacker, "onHit", { target: hitUnit });
        }
      }
    }
    lane.projectiles = stillFlying;

    // Phase G: merge DoT kills into combined dead set
    const allDeadIds = killedById;
    for (const id of dotDeadIds) allDeadIds.add(id);

    // Apply kill bounties to defending lane
    if (allDeadIds.size > 0 && !lane.eliminated) {
      for (const u of lane.units) {
        if (!allDeadIds.has(u.id)) continue;
        const def = resolveUnitDef(u.type);
        lane.gold += (def && def.bounty) ? def.bounty : 1;
      }
    }

    // Phase G: onDeath hook before removing dead units
    for (const u of lane.units) {
      if (u.hp > 0) continue;
      if (u.abilities && u.abilities.length > 0) {
        resolveAbilityHook(game, lane, u, "onDeath", {});
      }
    }

    // Remove dead units
    lane.units = lane.units.filter(u => u.hp > 0);
  }

  // Win condition
  const activeLanes = game.lanes.filter(l => !l.eliminated);
  if (activeLanes.length === 0) {
    game.phase = "ended";
    game.winner = null;
  } else {
    const aliveTeams = new Set(activeLanes.map(l => l.team));
    if (aliveTeams.size === 1) {
      game.phase = "ended";
      game.winner = activeLanes[0].laneIndex;
    }
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
      // Phase D: sendQueue histogram
      const sendQueueHist = {};
      for (const k of lane.sendQueue) sendQueueHist[k] = (sendQueueHist[k] || 0) + 1;
      for (let x = 0; x < GRID_W; x++) {
        for (let y = 0; y < GRID_H; y++) {
          const tile = lane.grid[x][y];
          if (tile.type === "wall") walls.push({ x, y });
          else if (tile.type === "tower") towerCells.push({
            x, y, type: tile.towerType, level: tile.towerLevel,
            targetMode: tile.targetMode || "first",
            debuffed: tile.debuffEndTick !== undefined && tile.debuffEndTick > game.tick,
          });
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
        team: lane.team || "red",
        eliminated: lane.eliminated,
        gold: lane.gold,
        income: lane.income,
        lives: lane.lives,
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
        sendQueue: sendQueueHist,
        sendQueueTotal: lane.sendQueue.length,
        sendDrainProgress: SEND_INTERVAL_TICKS > 0 ? lane.sendDrainCounter / SEND_INTERVAL_TICKS : 0,
        autosend: lane.autosend ? {
          enabled: !!lane.autosend.enabled,
          enabledUnits: Object.assign({}, lane.autosend.enabledUnits || {}),
          rate: lane.autosend.rate || "normal",
          tickCounter: Number(lane.autosend.tickCounter) || 0,
        } : {
          enabled: false,
          enabledUnits: {},
          rate: "normal",
          tickCounter: 0,
        },
      };
    }),
  };
}

function createMLPublicConfig(options) {
  const opt = normalizeGameOptions(options);
  const allUnitTypes = typeof getAllUnitTypes === "function" ? getAllUnitTypes() : [];

  function pickSoundFields(ut) {
    return {
      sound_spawn:  ut.sound_spawn  || null,
      sound_attack: ut.sound_attack || null,
      sound_hit:    ut.sound_hit    || null,
      sound_death:  ut.sound_death  || null,
    };
  }

  const fixedUnitTypes = allUnitTypes
    .filter(ut => (ut.behavior_mode === "fixed" || ut.behavior_mode === "both") && ut.enabled !== false)
    .map(ut => ({
      key: ut.key,
      name: ut.name,
      build_cost: Number(ut.build_cost) || 0,
      range: Number(ut.range) * GRID_W,
      attack_damage: Number(ut.attack_damage) || 0,
      attack_speed: Number(ut.attack_speed) || 1,
      damage_type: ut.damage_type || "NORMAL",
      icon_url: ut.icon_url || null,
      ...pickSoundFields(ut),
    }));

  const movingUnitTypes = allUnitTypes
    .filter(ut => (ut.behavior_mode === "moving" || ut.behavior_mode === "both") && ut.enabled !== false)
    .map(ut => ({
      key: ut.key,
      name: ut.name,
      send_cost: Number(ut.send_cost) || 1,
      ...pickSoundFields(ut),
    }));

  return {
    tickHz: TICK_HZ,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    startGold: opt.startGold,
    startIncome: opt.startIncome,
    livesStart: LIVES_START,
    gridW: GRID_W,
    gridH: GRID_H,
    wallCost: WALL_COST,
    maxWalls: null,
    maxPathLen: MAX_PATH_LEN,
    unitDefs: UNIT_DEFS,
    towerDefs: TOWER_DEFS,
    towerMaxLevel: TOWER_MAX_LEVEL,
    barracksInfinite: true,
    barracksCostBase: BARRACKS_COST_BASE,
    barracksReqIncomeBase: BARRACKS_REQ_INCOME_BASE,
    // retained for older clients; formula above is authoritative
    barracksLevels: [],
    // Phase D
    sendIntervalTicks: SEND_INTERVAL_TICKS,
    queueCap: QUEUE_CAP,
    // Phase E
    fixedUnitTypes,
    // Phase H
    movingUnitTypes,
  };
}

/**
 * Returns a key→unitDef map for all sendable (moving/both-mode) units from the
 * DB cache. Falls back to the hardcoded UNIT_DEFS when the DB has no rows yet
 * (e.g. local dev without migrations).
 */
function getMovingUnitDefMap() {
  const all = getAllUnitTypes();
  if (all.length === 0) return Object.assign({}, UNIT_DEFS);
  const map = {};
  for (const ut of all) {
    if (!ut.enabled) continue;
    if (ut.behavior_mode !== "both" && ut.behavior_mode !== "moving") continue;
    const def = resolveUnitDef(ut.key);
    if (def) map[ut.key] = def;
  }
  return Object.keys(map).length > 0 ? map : Object.assign({}, UNIT_DEFS);
}

module.exports = {
  TICK_HZ,
  TICK_MS,
  INCOME_INTERVAL_TICKS,
  SEND_INTERVAL_TICKS,
  QUEUE_CAP,
  UNIT_DEFS,
  TOWER_DEFS,
  getBarracksLevelDef,
  getMovingUnitDefMap,
  GRID_W,
  GRID_H,
  createMLGame,
  applyMLAction,
  mlTick,
  createMLSnapshot,
  createMLPublicConfig,
  resolveTowerDef,
};
