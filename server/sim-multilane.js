// server/sim-multilane.js
"use strict";

/**
 * Multi-lane PvP simulation — 10×28 tile grid per player lane.
 * Units follow a straight path from spawn tile (5,0) to castle tile (5,27).
 * Forge Wars wave defense: defenders placed on tile grid fight incoming wave enemies.
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
const gameConfig = require("./gameConfig");
const { getUnitType, getAllUnitTypes } = require("./unitTypes");
const combatLog = require("./combatLog");
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
const MAX_PATH_LEN = GRID_W * GRID_H; // kept for reference; path is always GRID_H steps
const SPLASH_RADIUS_TILES = 1.5;
const SEND_INTERVAL_TICKS = 5;     // ticks between send-queue drains (0.25s at 20Hz)
const QUEUE_CAP = 200;             // max units in send queue per lane
const MIN_UNIT_SPACING = 0.8;      // minimum pathIdx gap enforced by applySeparation
const TOWER_TARGET_MODES = new Set(["first", "last", "weakest", "strongest"]);

// Mobile defender constants
const DEFENDER_BASE_SPEED   = 0.15;
const ENGAGEMENT_RANGE_PADDING = 2.0; // extra leash beyond attack range before a unit opens fire
const MERGE_STAGING_COLS    = [2, 4, 5, 6, 8];
const SEP_DAMP              = 0.35;
const SEP_MAX_PUSH          = 0.10;

// Wave defense constants (Forge Wars Wave Rework Phase 1)
const TEAM_HP_START = 20;
const BUILD_PHASE_TICKS = 600;       // 30 s at 20 Hz
const TRANSITION_PHASE_TICKS = 200;  // 10 s at 20 Hz
const ESCALATION_PER_EXTRA_ROUND = 0.10; // +10% HP/DMG per round beyond last wave

function getMlRuntimeSettings() {
  const cfg = gameConfig.getActiveConfig("multilane") || {};
  const gp = cfg.globalParams || {};
  return {
    startGold: Math.floor(clampNum(gp.startGold, 0, 10000, START_GOLD)),
    startIncome: clampNum(gp.startIncome, 0, 1000, START_INCOME),
    livesStart: Math.floor(clampNum(gp.livesStart, 1, 1000, LIVES_START)),
    teamHpStart: Math.floor(clampNum(gp.teamHpStart, 1, 1000, TEAM_HP_START)),
    buildPhaseTicks: Math.floor(clampNum(gp.buildPhaseTicks, 20, 7200, BUILD_PHASE_TICKS)),
    transitionPhaseTicks: Math.floor(clampNum(gp.transitionPhaseTicks, 20, 7200, TRANSITION_PHASE_TICKS)),
  };
}

// Shared path suffix appended after each branch's BFS path.
// Represents the outer shared bridge leading to the enemy base:
//   Left side  (lanes 0,1): Bridge_A → Dwarf Base
//   Right side (lanes 2,3): Bridge_F → Goblin Base
// Both lanes on the same side share this bridge. Towers cannot target units here
// (virtual coordinates are outside the private build grid).
const SHARED_SUFFIX_LENGTH = 28; // matches private branch grid length (GRID_H)

// Suffix pathIdx that maps to pt3 (Island_Split exit / merge bridge entry) in the Unity
// lane waypoint polyline.  Computed from Unity TileGrid._lanePathWaypoints arc lengths:
//   d(pt2→pt3) = √(28²+12.5²) ≈ 30.664;  d(pt3→pt4)=20;  d(pt4→pt5)=27  → total ≈ 77.664
//   normProgress_pt3 = 30.664/77.664 ≈ 0.3948
//   MERGE_BRIDGE_ENTRY_IDX = GRID_H + normProgress_pt3 × (SHARED_SUFFIX_LENGTH−1) ≈ 38.66
const _D23 = Math.sqrt(28 * 28 + 12.5 * 12.5); // pt2→pt3 arc length (same for all 4 lanes)
const MERGE_BRIDGE_ENTRY_IDX = GRID_H + (_D23 / (_D23 + 20 + 27)) * (SHARED_SUFFIX_LENGTH - 1);

function buildFullPath(branchPath) {
  if (!branchPath || branchPath.length === 0) return [];
  const last = branchPath[branchPath.length - 1];
  const suffix = [];
  for (let i = 1; i <= SHARED_SUFFIX_LENGTH; i++) {
    suffix.push({ x: last.x, y: last.y + i });
  }
  return branchPath.concat(suffix);
}

const FIXED_SLOT_LAYOUT = [
  { laneIndex: 0, slotKey: "left_a", side: "left",  slotColor: "red",   branchId: "left_branch_a",  branchLabel: "Red Branch",   castleSide: "right" },
  { laneIndex: 1, slotKey: "left_b", side: "left",  slotColor: "gold",  branchId: "left_branch_b",  branchLabel: "Gold Branch",  castleSide: "right" },
  { laneIndex: 2, slotKey: "right_a", side: "right", slotColor: "blue",  branchId: "right_branch_a", branchLabel: "Blue Branch",  castleSide: "left" },
  { laneIndex: 3, slotKey: "right_b", side: "right", slotColor: "green", branchId: "right_branch_b", branchLabel: "Green Branch", castleSide: "left" },
];

const BATTLEFIELD_TOPOLOGY = Object.freeze({
  mapType: "lava_lake_funnel",
  centerIslandId: "center_spawn_island",
  sideOrder: ["left", "right"],
  castles: [
    { side: "left", castleId: "left_castle", bridgeId: "left_castle_bridge" },
    { side: "right", castleId: "right_castle", bridgeId: "right_castle_bridge" },
  ],
  mergeZones: [
    { side: "left", landmassId: "left_merge_landmass", bridgeId: "left_castle_bridge" },
    { side: "right", landmassId: "right_merge_landmass", bridgeId: "right_castle_bridge" },
  ],
  buildZones: [
    { branchId: "left_branch_a", ownerLaneIndex: 0, buildable: true },
    { branchId: "left_branch_b", ownerLaneIndex: 1, buildable: true },
    { branchId: "right_branch_a", ownerLaneIndex: 2, buildable: true },
    { branchId: "right_branch_b", ownerLaneIndex: 3, buildable: true },
  ],
  sharedZonesBuildable: false,
});

// Warlock debuff constants (3-second window at 20 hz)
const WARLOCK_DEBUFF_CD    = 60;  // ticks between debuff attempts
const WARLOCK_DEBUFF_TICKS = 60;  // debuff duration in ticks
const WARLOCK_DEBUFF_MULT  = 0.75; // -25% tower damage
const WARLOCK_DEBUFF_RANGE = 3.5;  // tile radius

// Unit and tower definitions are DB-driven via unitTypes.js.
// These empty objects are kept for backward-compat exports only.
const UNIT_DEFS  = {};
const TOWER_DEFS = {};

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
 * Resolve a unit definition from the DB (authoritative).
 * Returns null if the unit type is unknown or fixed-only.
 */
function resolveUnitDef(key) {
  const ut = getUnitType(key);
  if (!ut) return null;
  if (Number(ut.send_cost) <= 0) return null;
  const sp = (ut.special_props && typeof ut.special_props === "object") ? ut.special_props : {};
  const attackSpeed = Math.max(0.01, Number(ut.attack_speed) || 0.01);
  return {
    cost:               Number(ut.send_cost),
    income:             Number(ut.income),
    hp:                 Number(ut.hp),
    dmg:                Number(ut.attack_damage),
    atkCdTicks:         Math.max(1, Math.round(TICK_HZ / attackSpeed)),
    pathSpeed:          Number(ut.path_speed),
    bounty:             Number(ut.bounty) || 1,
    range:              Number(ut.range),
    ranged:             Number(ut.range) > 0,
    armorType:          ut.armor_type   || "MEDIUM",
    damageType:         ut.damage_type  || "NORMAL",
    damageReductionPct: Number(ut.damage_reduction_pct) || 0,
    warlockDebuff:      sp.warlockDebuff != null ? !!sp.warlockDebuff : false,
    structBonus:        sp.structBonus   != null ?  +sp.structBonus   : 0,
    barracks_scales_hp:  ut.barracks_scales_hp  === true,
    barracks_scales_dmg: ut.barracks_scales_dmg === true,
  };
}

/**
 * Resolve a tower definition from the DB (authoritative).
 * DB range is stored normalised to [0,1] × GRID_W.
 */
function resolveTowerDef(key) {
  const ut = getUnitType(key);
  if (!ut) return null;
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
    projectileTicks: Number(ut.projectile_travel_ticks) || 7,
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
  let total = base.cost; // direct placement cost (no wall step)
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

// Straight-line path from spawn (SPAWN_X, 0) to castle (CASTLE_X, CASTLE_YG).
// Walls no longer exist; path is always the fixed vertical centre column.
function straightLinePath() {
  const path = [];
  for (let y = SPAWN_YG; y <= CASTLE_YG; y++) {
    path.push({ x: SPAWN_X, y });
  }
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

function cloneSlotDef(slot, laneTeam) {
  return {
    laneIndex: slot.laneIndex,
    slotKey: slot.slotKey,
    side: slot.side,
    slotColor: slot.slotColor,
    branchId: slot.branchId,
    branchLabel: slot.branchLabel,
    castleSide: slot.castleSide,
    team: laneTeam || (slot.side === "left" ? "red" : "blue"),
  };
}

// For 2-player, pick one branch from each side so both castles are contested:
//   lane 0 → Red Branch (left side, attacks right castle)
//   lane 1 → Blue Branch (right side, attacks left castle) — remapped from FIXED_SLOT_LAYOUT[2]
const TWO_PLAYER_SLOT_BASES = [
  FIXED_SLOT_LAYOUT[0],
  Object.assign({}, FIXED_SLOT_LAYOUT[2], { laneIndex: 1 }),
];

function getDefaultSlotDefinitions(playerCount, laneTeams) {
  const safeCount = Math.max(0, Math.floor(Number(playerCount) || 0));
  const defs = [];
  for (let i = 0; i < safeCount; i++) {
    const base = safeCount === 2
      ? (TWO_PLAYER_SLOT_BASES[i] || FIXED_SLOT_LAYOUT[i])
      : (FIXED_SLOT_LAYOUT[i] || {
          laneIndex: i,
          slotKey: `slot_${i}`,
          side: i % 2 === 0 ? "left" : "right",
          slotColor: `slot-${i}`,
          branchId: `branch_${i}`,
          branchLabel: `Branch ${i + 1}`,
          castleSide: i % 2 === 0 ? "right" : "left",
        });
    defs.push(cloneSlotDef(base, laneTeams && laneTeams[i]));
  }
  return defs;
}

function getBattlefieldTopology(playerCount, laneTeams) {
  return {
    mapType: BATTLEFIELD_TOPOLOGY.mapType,
    centerIslandId: BATTLEFIELD_TOPOLOGY.centerIslandId,
    sideOrder: BATTLEFIELD_TOPOLOGY.sideOrder.slice(),
    castles: BATTLEFIELD_TOPOLOGY.castles.map((castle) => Object.assign({}, castle)),
    mergeZones: BATTLEFIELD_TOPOLOGY.mergeZones.map((zone) => Object.assign({}, zone)),
    buildZones: BATTLEFIELD_TOPOLOGY.buildZones
      .filter((zone) => zone.ownerLaneIndex < playerCount)
      .map((zone) => Object.assign({}, zone)),
    sharedZonesBuildable: !!BATTLEFIELD_TOPOLOGY.sharedZonesBuildable,
    slotDefinitions: getDefaultSlotDefinitions(playerCount, laneTeams),
  };
}

function normalizeGameOptions(options) {
  const src = options && typeof options === "object" ? options : {};
  const runtime = getMlRuntimeSettings();
  const laneTeamsRaw = Array.isArray(src.laneTeams) ? src.laneTeams : [];
  const laneTeams = laneTeamsRaw.map((team, idx) => {
    const normalized = String(team || "").trim();
    if (normalized.length > 0) return normalized.slice(0, 24);
    return idx % 2 === 0 ? "red" : "blue";
  });
  return {
    startGold: Math.floor(clampNum(src.startGold, 0, 10000, runtime.startGold)),
    startIncome: clampNum(src.startIncome, 0, 1000, runtime.startIncome),
    livesStart: Math.floor(clampNum(src.livesStart, 1, 1000, runtime.livesStart)),
    teamHpStart: Math.floor(clampNum(src.teamHpStart, 1, 1000, runtime.teamHpStart)),
    buildPhaseTicks: Math.floor(clampNum(src.buildPhaseTicks, 20, 7200, runtime.buildPhaseTicks)),
    transitionPhaseTicks: Math.floor(clampNum(src.transitionPhaseTicks, 20, 7200, runtime.transitionPhaseTicks)),
    laneTeams,
    matchSeed: typeof src.matchSeed === "number" ? (src.matchSeed >>> 0) : undefined,
    battlefieldTopology: getBattlefieldTopology(Number(src.playerCount) || laneTeams.length || 4, laneTeams),
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
  const battlefieldTopology = getBattlefieldTopology(playerCount, opt.laneTeams);
  const slotDefinitions = battlefieldTopology.slotDefinitions;
  const lanes = [];
  for (let i = 0; i < playerCount; i++) {
    const grid = makeGrid();
    const path = straightLinePath(); // fixed centre column (5,0)→(5,27)
    const slot = slotDefinitions[i] || cloneSlotDef({
      laneIndex: i,
      slotKey: `slot_${i}`,
      side: i % 2 === 0 ? "left" : "right",
      slotColor: `slot-${i}`,
      branchId: `branch_${i}`,
      branchLabel: `Branch ${i + 1}`,
      castleSide: i % 2 === 0 ? "right" : "left",
    }, opt.laneTeams[i]);
    lanes.push({
      laneIndex: i,
      team: slot.team,
      side: slot.side,
      slotKey: slot.slotKey,
      slotColor: slot.slotColor,
      branchId: slot.branchId,
      branchLabel: slot.branchLabel,
      castleSide: slot.castleSide,
      eliminated: false,
      gold: opt.startGold + opt.startIncome,
      income: opt.startIncome,
      incomeRemainder: 0,
      lives: opt.livesStart,
      totalSendSpend: 0,
      totalSendCount: 0,
      totalBuildSpend: 0,
      totalLeaksTaken: 0,
      biggestLeakTaken: 0,
      wavesHeld: 0,
      wavesLeaked: 0,
      currentHoldStreak: 0,
      longestHoldStreak: 0,
      leakCountThisRound: 0,
      lifeLossThisRound: 0,
      sendCountThisRound: 0,
      sendSpendThisRound: 0,
      buildSpendThisRound: 0,
      grid,
      path,
      fullPath: buildFullPath(path),
      barracks: Object.assign({ level: 1 }, getBarracksLevelDef(1)),
      units: [],
      mobileDefenders: new Map(),
      spawnQueue: [],
      projectiles: [],
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
    playerCount,
    lanes,
    battlefieldTopology,
    // Wave defense state
    teamHp: { left: opt.teamHpStart, right: opt.teamHpStart },
    teamHpMax: opt.teamHpStart,
    buildPhaseTicks: opt.buildPhaseTicks,
    transitionPhaseTicks: opt.transitionPhaseTicks,
    roundState: "build",
    roundNumber: 1,
    roundStateTicks: 0,
    pendingSends: {},    // targetLaneIdx → [{unitType, count}]
    waveConfig: [],      // loaded at match start by multilaneRuntime
    roundSnapshots: [],        // one entry per completed wave + one terminal entry
    startedAt: null,           // set on first live tick (not at object creation)
    finalSnapshotCaptured: false,
    _pendingEvents: [],  // drained by runtime each tick
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

function getLaneBuildValue(lane) {
  let total = 0;
  for (let tx = 0; tx < GRID_W; tx++) {
    for (let ty = 0; ty < GRID_H; ty++) {
      const tile = lane.grid[tx][ty];
      if ((tile.type === "tower" || tile.type === "dead_tower") && tile.costHistory) {
        for (const entry of tile.costHistory) {
          total += entry.cost;
        }
      }
    }
  }
  return total;
}

function getLaneWaveResult(lane) {
  if (!lane) return "Unknown";
  if (lane.eliminated && lane.lifeLossThisRound > 0) return "Defeated";
  if (lane.leakCountThisRound >= 5 || lane.lifeLossThisRound >= 5) return "Crushed";
  if (lane.leakCountThisRound > 0 || lane.lifeLossThisRound > 0) return "Leaked";
  return "Held";
}

function createRoundSnapshotLane(game, lane) {
  return {
    laneIndex: lane.laneIndex,
    income: lane.income,
    buildValue: getLaneBuildValue(lane),
    gold: Math.floor(lane.gold),
    leaksTaken: lane.leakCountThisRound,
    leakDamage: lane.lifeLossThisRound,
    sendSpend: lane.sendSpendThisRound,
    sendCount: lane.sendCountThisRound,
    buildSpend: lane.buildSpendThisRound,
    lives: lane.lives,
    teamHp: game.teamHp[lane.side],
    eliminated: lane.eliminated,
    holdResult: getLaneWaveResult(lane),
  };
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
    if (game.roundState !== "build" && game.roundState !== "combat")
      return { ok: false, reason: "Can only send units during build or combat phase" };
    const unitType = String((data && data.unitType) || "").toLowerCase();
    const def = resolveUnitDef(unitType);
    if (!def) return { ok: false, reason: "Unknown unitType" };
    const _loadoutKeys = lane.autosend && lane.autosend.loadoutKeys;
    if (_loadoutKeys && _loadoutKeys.length > 0 && !_loadoutKeys.includes(unitType)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    if (lane.gold < def.cost) return { ok: false, reason: "Not enough gold" };

    // Resolve target lane (opponent)
    let targetLaneIdx = Number.isInteger(data && data.targetLaneIndex) ? data.targetLaneIndex : null;
    if (targetLaneIdx === null || !isOpponentLane(game, laneIndex, targetLaneIdx)) {
      targetLaneIdx = findNextActiveOpponentLaneIndex(game, laneIndex);
    }
    if (targetLaneIdx === null) return { ok: false, reason: "No valid target lane" };

    lane.gold -= def.cost;
    lane.income += def.income;
    lane.totalSendSpend += def.cost;
    lane.totalSendCount += 1;
    lane.sendSpendThisRound += def.cost;
    lane.sendCountThisRound += 1;
    // Sends always feed the target lane's next combat wave.
    // Buying during combat is allowed, but it should never spawn mid-wave.
    if (!game.pendingSends[targetLaneIdx]) game.pendingSends[targetLaneIdx] = [];
    game.pendingSends[targetLaneIdx].push({ unitType, count: 1 });
    return { ok: true };
  }

  if (type === "place_unit") {
    if (game.roundState !== "build") return { ok: false, reason: "Can only place units during build phase" };
    const gx = Number((data && data.gridX !== undefined) ? data.gridX : -1);
    const gy = Number((data && data.gridY !== undefined) ? data.gridY : -1);
    const unitTypeKey = String((data && data.unitTypeKey) || "").toLowerCase();
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position" };
    }
    if (gx === SPAWN_X && gy === SPAWN_YG) return { ok: false, reason: "Cannot place on spawn tile" };
    if (gx === CASTLE_X && gy === CASTLE_YG) return { ok: false, reason: "Cannot place on castle tile" };
    const tile = lane.grid[gx][gy];
    if (tile.type !== "empty") return { ok: false, reason: "Tile not empty" };
    const ut = getUnitType(unitTypeKey);
    if (!ut || ut.enabled === false) return { ok: false, reason: "Unknown unit type" };
    if (Number(ut.range) <= 0 || Number(ut.build_cost) <= 0) {
      return { ok: false, reason: "Unit cannot be placed as a defender" };
    }
    const loadoutKeys = lane.autosend && lane.autosend.loadoutKeys;
    if (loadoutKeys && loadoutKeys.length > 0 && !loadoutKeys.includes(unitTypeKey)) {
      return { ok: false, reason: "Unit not in loadout" };
    }
    const towerDef = resolveTowerDef(unitTypeKey);
    if (!towerDef) return { ok: false, reason: "Cannot resolve unit as defender" };
    if (lane.gold < towerDef.cost) return { ok: false, reason: "Not enough gold" };

    const hp = Number(ut.hp) || 100;
    tile.type = "tower";
    tile.towerType = unitTypeKey;
    tile.towerLevel = 1;
    tile.atkCd = 0;
    tile.targetMode = "first";
    tile.debuffEndTick = 0;
    tile.debuffMult = 1.0;
    tile.abilities = buildAbilitiesForUnitType(unitTypeKey);
    tile.hp = hp;
    tile.maxHp = hp;
    tile.costHistory = [{ cost: towerDef.cost, refundPct: 100 }];
    lane.gold -= towerDef.cost;
    lane.totalBuildSpend += towerDef.cost;
    lane.buildSpendThisRound += towerDef.cost;
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
    lane.totalBuildSpend += cost;
    lane.buildSpendThisRound += cost;
    tile.towerLevel = nextLevel;
    if (!tile.costHistory) tile.costHistory = [];
    tile.costHistory.push({ cost, refundPct: 100 });
    const stats = getTowerStats(tile.towerType, nextLevel);
    if (stats && tile.atkCd > stats.atkCdTicks) tile.atkCd = stats.atkCdTicks;
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
      lane.totalBuildSpend += cost;
      lane.buildSpendThisRound += cost;
      tile.towerLevel = nextLevel;
      if (!tile.costHistory) tile.costHistory = [];
      tile.costHistory.push({ cost, refundPct: 100 });
      const stats = getTowerStats(tile.towerType, nextLevel);
      if (stats && tile.atkCd > stats.atkCdTicks) tile.atkCd = stats.atkCdTicks;
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
      lane.totalBuildSpend += fallback.cost;
      lane.buildSpendThisRound += fallback.cost;
      lane.barracks = { level: nextLevel, multiplier: fallback.hpMult };
      return { ok: true };
    }
    if (lane.gold < nextDef.upgradeCost) return { ok: false, reason: "Not enough gold" };

    // Phase F: barracks stores level + DB multiplier only
    lane.gold -= nextDef.upgradeCost;
    lane.totalBuildSpend += nextDef.upgradeCost;
    lane.buildSpendThisRound += nextDef.upgradeCost;
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
      for (const ut of getAllUnitTypes().filter(u => u.enabled).map(u => u.key)) {
        as.enabledUnits[ut] = !!enabledUnits[ut];
      }
    }
    const VALID_RATES = new Set(["slow", "normal", "fast"]);
    if (VALID_RATES.has(rate)) as.rate = rate;
    if (Array.isArray(loadoutKeys)) as.loadoutKeys = loadoutKeys.slice(0, 5);
    as.tickCounter = 0;
    return { ok: true };
  }

  return { ok: false, reason: "Unknown action type" };
}

// ── Mobile defender helpers ───────────────────────────────────────────────────

function dist2D(a, b) {
  const dx = a.posX - b.posX, dy = a.posY - b.posY;
  return Math.sqrt(dx * dx + dy * dy);
}

function moveToward2D(unit, tx, ty, speed, minX, maxX, minY, maxY) {
  const dx = tx - unit.posX, dy = ty - unit.posY;
  const d  = Math.sqrt(dx * dx + dy * dy);
  if (d < 0.01) return;
  const step = Math.min(speed, d);
  unit.posX  = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY  = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  unit.pathIdx = unit.posY;
}

function applySeparation2D(units, minSpacing, minX, maxX, minY, maxY) {
  for (let i = 0; i < units.length; i++) {
    for (let j = i + 1; j < units.length; j++) {
      const a = units[i], b = units[j];
      const dx = b.posX - a.posX, dy = b.posY - a.posY;
      const d  = Math.sqrt(dx * dx + dy * dy);
      if (d >= minSpacing || d < 0.001) continue;
      const push = Math.min((minSpacing - d) * SEP_DAMP, SEP_MAX_PUSH);
      a.posX = Math.max(minX, Math.min(maxX, a.posX - (dx / d) * push));
      a.posY = Math.max(minY, Math.min(maxY, a.posY - (dy / d) * push));
      a.pathIdx = a.posY;
      b.posX = Math.max(minX, Math.min(maxX, b.posX + (dx / d) * push));
      b.posY = Math.max(minY, Math.min(maxY, b.posY + (dy / d) * push));
      b.pathIdx = b.posY;
    }
  }
}

function getUnitAttackRange(typeKey) {
  const stats = getTowerStats(typeKey, 1);
  if (stats && stats.range) return stats.range;
  const uDef = resolveUnitDef(typeKey);
  if (uDef && uDef.combatRange) return uDef.combatRange;
  return 1.5;  // melee default
}

function getUnitEngagementRange(typeKey) {
  // Engagement and attack are intentionally separate:
  // units should commit to nearby defenders well before they are in attack range,
  // and the leash is large enough to cover the full active combat zone.
  return Math.max(getUnitAttackRange(typeKey) + ENGAGEMENT_RANGE_PADDING, GRID_H);
}

function isSplitZoneUnit(unit) {
  return Number(unit.posY) < GRID_H;
}

function isMergeZoneUnit(unit) {
  return !isSplitZoneUnit(unit);
}

function canEngageDefenderInZone(attacker, defender) {
  if (!defender || !defender.isDefender || defender.hp <= 0) return false;
  if (isSplitZoneUnit(attacker)) return defender.defState === "split_guard";
  return defender.defState === "merge_guard";
}

function _doAttack(game, lane, attacker, target) {
  const stats    = getTowerStats(attacker.type, 1);
  const uDef     = resolveUnitDef(attacker.type);
  const dmg      = attacker.baseDmg || (stats ? stats.dmg : (uDef ? uDef.dmg : 5));
  const cdTk     = attacker.atkCdTicks || (stats ? stats.atkCdTicks : 30);
  const atkRange = getUnitAttackRange(attacker.type);

  if (atkRange > 2.0) {
    // Ranged — fire projectile
    const behavior = stats ? (stats.projBehavior || 'single') : 'single';
    fireProjectile(game, lane,
      { id: attacker.id, kind: 'unit', x: attacker.posX, y: attacker.posY },
      target.id,
      {
        dmg,
        damageType:     stats ? stats.damageType : 'NORMAL',
        behavior,
        behaviorParams: stats ? (stats.projBehaviorParams || {}) : {},
        travelTicks:    stats ? (stats.projectileTicks || 8) : 8,
        isSplash:       behavior === 'splash',
        projectileType: attacker.type,
        abilities:      [],
      }
    );
  } else {
    // Melee — instant damage
    target.hp = Math.max(0, target.hp - dmg);
  }
  attacker.atkCd = cdTk;
}

function assignMergeSlot(lane, defUnit) {
  const usedX = new Set(
    lane.units
      .filter(u => u.isDefender && u.defState === 'merge_guard' && u !== defUnit && u.hp > 0)
      .map(u => Math.round(u.posX))
  );
  let best = MERGE_STAGING_COLS[0], bestDist = Infinity;
  for (const col of MERGE_STAGING_COLS) {
    if (usedX.has(col)) continue;
    const d = Math.abs(col - defUnit.homeTx);
    if (d < bestDist) { bestDist = d; best = col; }
  }
  return best;
}

// ── Wave defense helpers ──────────────────────────────────────────────────────

function _pickTowerTarget(tile, unitsInRange) {
  const mode = TOWER_TARGET_MODES.has(tile.targetMode) ? tile.targetMode : "first";
  let target = unitsInRange[0];
  if (mode === "strongest") {
    for (const u of unitsInRange) { if (u.hp > target.hp) target = u; }
    return target;
  }
  if (mode === "weakest") {
    for (const u of unitsInRange) { if (u.hp < target.hp) target = u; }
    return target;
  }
  if (mode === "last") {
    for (const u of unitsInRange) { if (u.pathIdx < target.pathIdx) target = u; }
    return target;
  }
  for (const u of unitsInRange) { if (u.pathIdx > target.pathIdx) target = u; }
  return target;
}

function _resolveWave(game) {
  const cfg = Array.isArray(game.waveConfig) ? game.waveConfig : [];
  const round = game.roundNumber;
  if (cfg.length === 0) {
    // No config — use a minimal fallback so the game still runs
    return { unit_type: "goblin", spawn_qty: 8, hp_mult: 1, dmg_mult: 1, speed_mult: 1 };
  }
  // Find exact wave row; if past the last, use last row with escalation
  const exact = cfg.find(w => Number(w.wave_number) === round);
  if (exact) return exact;
  const last = cfg.reduce((a, b) => Number(a.wave_number) >= Number(b.wave_number) ? a : b);
  const extra = round - Number(last.wave_number);
  const esc = 1 + extra * ESCALATION_PER_EXTRA_ROUND;
  return {
    unit_type: last.unit_type,
    spawn_qty: last.spawn_qty,
    hp_mult:    Number(last.hp_mult)    * esc,
    dmg_mult:   Number(last.dmg_mult)   * esc,
    speed_mult: Number(last.speed_mult),
  };
}

function _spawnWaveUnit(game, lane, waveDef) {
  const unitType = waveDef.unit_type;
  const def = resolveUnitDef(unitType);
  if (!def) return;
  if (lane.units.length + lane.spawnQueue.length >= MAX_UNITS_PER_LANE) return;
  const hp  = Math.ceil(def.hp    * Number(waveDef.hp_mult    || 1));
  const dmg =           def.dmg   * Number(waveDef.dmg_mult   || 1);
  const spd =           def.pathSpeed * Number(waveDef.speed_mult || 1);
  lane.spawnQueue.push({
    id: `wu${game.nextUnitId++}`,
    ownerLane: -1,  // wave unit — no player owner
    type: unitType,
    skinKey: null,
    pathIdx: 0,
    hp,
    maxHp: hp,
    baseDmg: dmg,
    baseSpeed: spd,
    atkCd: 0,
    atkCdTicks: def.atkCdTicks,
    armorType: def.armorType || "MEDIUM",
    damageReductionPct: def.damageReductionPct || 0,
    abilities: buildAbilitiesForUnitType(unitType),
    bounty: def.bounty || 1,
    isWaveUnit: true,
    combatTarget: null,
    spawnIndex: lane.spawnQueue.length,  // position in wave formation rectangle
  });
}

function _startCombat(game) {
  const waveDef = _resolveWave(game);
  const waveSizes = {};
  for (const lane of game.lanes) {
    if (lane.eliminated) continue;
    waveSizes[lane.laneIndex] = waveDef.spawn_qty;
    // Base wave
    for (let i = 0; i < waveDef.spawn_qty; i++) _spawnWaveUnit(game, lane, waveDef);
    // Flush pendingSends (units sent by opponent during build phase)
    for (const entry of (game.pendingSends[lane.laneIndex] || [])) {
      const sendDef = resolveUnitDef(entry.unitType);
      if (sendDef) {
        for (let i = 0; i < entry.count; i++) {
          _spawnWaveUnit(game, lane, {
            unit_type: entry.unitType, spawn_qty: 1,
            hp_mult: 1, dmg_mult: 1, speed_mult: 1,
          });
        }
      }
    }
    game.pendingSends[lane.laneIndex] = [];

    // Mobilize defenders from their home tiles
    lane.mobileDefenders.clear();
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type !== 'tower' || !tile.towerType) continue;
        const stats = getTowerStats(tile.towerType, tile.towerLevel || 1);
        const uDef  = resolveUnitDef(tile.towerType);
        const mob   = {
          id:           `def_${lane.laneIndex}_${tx}_${ty}_${game.roundNumber}`,
          ownerLane:    lane.laneIndex,
          type:         tile.towerType,
          skinKey:      null,
          posX:         tx,  posY: ty,
          pathIdx:      ty,
          homeTx: tx,   homeTy: ty,
          hp:           tile.hp || tile.maxHp || 100,
          maxHp:        tile.maxHp || 100,
          baseDmg:      stats ? stats.dmg : (uDef ? uDef.dmg : 5),
          moveSpeed:    DEFENDER_BASE_SPEED,
          atkCd:        0,
          atkCdTicks:   stats ? stats.atkCdTicks : 30,
          armorType:    'HEAVY',
          damageReductionPct: 10,
          isDefender:   true,
          isWaveUnit:   false,
          defState:     'split_guard',
          combatTarget: null,
          abilities:    [],
        };
        tile.mobilized = true;
        lane.mobileDefenders.set(`${tx}_${ty}`, mob);
        lane.units.push(mob);
        // Lock refund to 70% now that combat is starting
        if (tile.costHistory) {
          for (const entry of tile.costHistory) entry.refundPct = 70;
        }
      }
    }
  }
  game.roundState = "combat";
  game.roundStateTicks = 0;
  game._pendingEvents.push({ type: "ml_wave_start", roundNumber: game.roundNumber, waveSizes });
}

function _startTransition(game) {
  for (const lane of game.lanes) {
    if (lane.leakCountThisRound > 0 || lane.lifeLossThisRound > 0) {
      lane.wavesLeaked += 1;
      lane.currentHoldStreak = 0;
      lane.biggestLeakTaken = Math.max(lane.biggestLeakTaken || 0, lane.lifeLossThisRound || lane.leakCountThisRound || 0);
    } else {
      lane.wavesHeld += 1;
      lane.currentHoldStreak = (lane.currentHoldStreak || 0) + 1;
      lane.longestHoldStreak = Math.max(lane.longestHoldStreak || 0, lane.currentHoldStreak);
    }
  }
  game.roundSnapshots.push({
    round: game.roundNumber,
    elapsedSeconds: Math.floor(game.tick / TICK_HZ),
    lanes: game.lanes.map(l => createRoundSnapshotLane(game, l)),
  });
  game._pendingEvents.push({
    type: "ml_round_end",
    roundNumber: game.roundNumber,
    teamHp: Object.assign({}, game.teamHp),
  });
  game.roundState = "transition";
  game.roundStateTicks = 0;
}

function _startBuild(game) {
  game.roundNumber += 1;
  game.roundState = "build";
  game.roundStateTicks = 0;
  // Respawn dead defenders; restore HP on surviving defenders; grant build-phase income
  for (const lane of game.lanes) {
    if (lane.eliminated) continue;
    lane.gold += lane.income;
    lane.leakCountThisRound = 0;
    lane.lifeLossThisRound = 0;
    lane.sendCountThisRound = 0;
    lane.sendSpendThisRound = 0;
    lane.buildSpendThisRound = 0;
    lane.units = [];
    lane.mobileDefenders.clear();
    lane.spawnQueue = [];
    for (let tx = 0; tx < GRID_W; tx++)
      for (let ty = 0; ty < GRID_H; ty++)
        lane.grid[tx][ty].mobilized = false;
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type === "dead_tower" && tile.towerType) {
          tile.type = "tower";
          tile.hp = tile.maxHp || 100;
          tile.atkCd = 0;
        } else if (tile.type === "tower" && tile.towerType) {
          tile.hp = tile.maxHp || tile.hp || 100;
          tile.atkCd = 0;
        }
      }
    }
  }
  game._pendingEvents.push({
    type: "ml_round_start",
    roundNumber: game.roundNumber,
    teamHp: Object.assign({}, game.teamHp),
  });
}

function _runAutosendBuild(game, lane) {
  const as = lane.autosend;
  if (!as || !as.enabled) return;
  const keys = Array.isArray(as.loadoutKeys) && as.loadoutKeys.length > 0 ? as.loadoutKeys : [];
  const priority = keys.filter(k => !!as.enabledUnits[k]);
  if (priority.length === 0) return;
  let iterations = 0;
  while (iterations < 200) {
    iterations++;
    let bought = false;
    for (const ut of priority) {
      const def = resolveUnitDef(ut);
      if (!def || lane.gold < def.cost) continue;
      const targetIdx = findNextActiveOpponentLaneIndex(game, lane.laneIndex);
      if (targetIdx === null) break;
      lane.gold -= def.cost;
      lane.income += def.income;
      lane.totalSendSpend += def.cost;
      lane.sendSpendThisRound += def.cost;
      lane.sendCountThisRound += 1;
      if (!game.pendingSends[targetIdx]) game.pendingSends[targetIdx] = [];
      game.pendingSends[targetIdx].push({ unitType: ut, count: 1 });
      bought = true;
      break;
    }
    if (!bought) break;
  }
}

function mlTick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;
  if (!game.startedAt) game.startedAt = Date.now();
  game.roundStateTicks = (game.roundStateTicks || 0) + 1;

  // ── BUILD PHASE ──────────────────────────────────────────────────────────────
  if (game.roundState === "build") {
    if (game.roundStateTicks >= (game.buildPhaseTicks || BUILD_PHASE_TICKS)) _startCombat(game);
    return;
  }

  // ── TRANSITION PHASE ─────────────────────────────────────────────────────────
  if (game.roundState === "transition") {
    if (game.roundStateTicks >= (game.transitionPhaseTicks || TRANSITION_PHASE_TICKS)) _startBuild(game);
    return;
  }

  // ── COMBAT PHASE ─────────────────────────────────────────────────────────────
  for (const lane of game.lanes) {
    if (lane.eliminated) continue;

    // Drain spawn queue — place each unit at a unique position in a rectangle
    // so the whole wave arrives at once but spread across the grid width.
    // col = spawnIndex % GRID_W, row = floor(spawnIndex / GRID_W)
    while (lane.spawnQueue.length > 0) {
      const unit = lane.spawnQueue.shift();
      const idx  = unit.spawnIndex ?? 0;
      unit.posX    = idx % GRID_W;
      unit.posY    = Math.floor(idx / GRID_W);
      unit.pathIdx = unit.posY;
      lane.units.push(unit);
      if (unit.abilities && unit.abilities.length > 0) {
        resolveAbilityHook(game, lane, unit, "onSpawn", {});
      }
    }

    // Decrement cooldowns
    for (const u of lane.units) {
      if (u.atkCd > 0) u.atkCd -= 1;
      if (u.warlockCd !== undefined && u.warlockCd > 0) u.warlockCd -= 1;
    }
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type === "tower" && !tile.mobilized && tile.atkCd > 0) tile.atkCd -= 1;
      }
    }

    // Aura refresh
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

    // Tower attacks on wave units
    for (let tx = 0; tx < GRID_W; tx++) {
      for (let ty = 0; ty < GRID_H; ty++) {
        const tile = lane.grid[tx][ty];
        if (tile.type !== "tower" || !tile.towerType || tile.atkCd > 0 || tile.mobilized) continue;
        const stats = getTowerStats(tile.towerType, tile.towerLevel || 1);
        if (!stats) continue;
        const auraRange = activeAuras.range_bonus || 0;
        const auraSpd = (activeAuras.atk_speed_bonus || 0) + (activeAuras.cooldown_reduction || 0);
        // Use Y-only (row) distance so defenders at any column engage units on the path.
        // Min 3.5 rows so towers can always reach units that stopped within the 3-row
        // engagement search radius (Math.floor rounding can add ~1 extra row of gap).
        const effRows = Math.max(stats.range, 3.5) * (1 + auraRange / 100);
        const effAtkCd = auraSpd > 0
          ? Math.max(1, Math.round(stats.atkCdTicks * (1 - auraSpd / 100)))
          : stats.atkCdTicks;
        const unitsInRange = [];
        for (const u of lane.units) {
          if (u.hp <= 0 || u.isDefender) continue;
          // Use raw float pathIdx for row distance — avoids Math.floor rounding units out of range.
          if (Math.abs(ty - u.pathIdx) <= effRows) unitsInRange.push(u);
        }
        if (unitsInRange.length === 0) continue;
        const target = _pickTowerTarget(tile, unitsInRange);
        const debuffMult = (tile.debuffEndTick !== undefined && tile.debuffEndTick > game.tick)
          ? (tile.debuffMult || 1.0) : 1.0;
        const auraDmgMult = 1 + (activeAuras.dmg_bonus || 0) / 100;
        const effDmg = stats.dmg * debuffMult * auraDmgMult;
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

    // ── Mobile defender AI ────────────────────────────────────────────────────
    const splitAttackers = lane.units.filter(u => u.isWaveUnit && u.hp > 0 && u.posY < GRID_H);

    for (const d of lane.units) {
      if (!d.isDefender || d.hp <= 0) continue;

      const atkRange = getUnitAttackRange(d.type);
      const spd      = d.moveSpeed || DEFENDER_BASE_SPEED;

      if (d.defState === 'split_guard') {
        // Split clearance → teleport to merge bridge (pt3: Island_Split exit)
        if (splitAttackers.length === 0) {
          d.defState    = 'merge_guard';
          d.posX        = assignMergeSlot(lane, d);
          d.posY        = MERGE_BRIDGE_ENTRY_IDX;
          d.pathIdx     = MERGE_BRIDGE_ENTRY_IDX;
          d.combatTarget = null;
          continue;
        }

        // Validate stale target
        if (d.combatTarget) {
          if (!lane.units.find(u => u.id === d.combatTarget.unitId && u.hp > 0))
            d.combatTarget = null;
        }

        // Acquire nearest attacker in split zone
        if (!d.combatTarget) {
          let best = null, bestDist = Infinity;
          for (const wu of splitAttackers) {
            const dd = dist2D(d, wu);
            if (dd < bestDist) { bestDist = dd; best = wu; }
          }
          if (best) d.combatTarget = { unitId: best.id };
        }

        if (d.combatTarget) {
          const t = lane.units.find(u => u.id === d.combatTarget.unitId && u.hp > 0);
          if (t) {
            const dist = dist2D(d, t);
            if (dist <= atkRange + 0.15) {
              if (d.atkCd <= 0) _doAttack(game, lane, d, t);
            } else {
              moveToward2D(d, t.posX, t.posY, spd, 0, GRID_W - 1, 0, GRID_H - 1);
            }
          } else {
            d.combatTarget = null;
          }
        } else {
          // No target — advance toward attacker spawn side
          moveToward2D(d, d.posX, 0, spd, 0, GRID_W - 1, 0, GRID_H - 1);
        }

      } else if (d.defState === 'merge_guard') {
        const maxY          = GRID_H + SHARED_SUFFIX_LENGTH - 1;
        const mergeAttackers = lane.units.filter(u => u.isWaveUnit && u.hp > 0 && u.posY >= GRID_H);

        if (d.combatTarget) {
          if (!lane.units.find(u => u.id === d.combatTarget.unitId && u.hp > 0))
            d.combatTarget = null;
        }
        if (!d.combatTarget && mergeAttackers.length > 0) {
          let best = null, bestDist = Infinity;
          for (const wu of mergeAttackers) {
            const dd = dist2D(d, wu);
            if (dd < bestDist) { bestDist = dd; best = wu; }
          }
          if (best) d.combatTarget = { unitId: best.id };
        }
        if (d.combatTarget) {
          const t = lane.units.find(u => u.id === d.combatTarget.unitId && u.hp > 0);
          if (t) {
            const dist = dist2D(d, t);
            if (dist <= atkRange + 0.15) {
              if (d.atkCd <= 0) _doAttack(game, lane, d, t);
            } else {
              moveToward2D(d, t.posX, t.posY, spd, 0, GRID_W - 1, GRID_H, maxY);
            }
          }
        }
      }
    }

    // Wave unit movement, defender attacks, and leaks
    const fullPath = lane.fullPath || lane.path || [];
    const pathLen = fullPath.length || 1;
    const dotDeadIds = new Set();

    for (const u of lane.units) {
      if (u.hp <= 0) continue;
      if (u.isDefender) continue;  // defenders handled above
      resolveStatuses(u, game.tick);
      if (u.hp <= 0) { dotDeadIds.add(u.id); continue; }

      // Reached end → leak; damages this lane's side's teamHp
      if (u.pathIdx >= pathLen - 1) {
        u.hp = 0;
        combatLog.logEvent(game, 'leak', { unitId: u.id, unitType: u.type, lane: lane.laneIndex });
        const side = lane.side;
        if (side && game.teamHp && game.teamHp[side] !== undefined) {
          game.teamHp[side] = Math.max(0, game.teamHp[side] - 1);
          lane.totalLeaksTaken += 1;
          lane.leakCountThisRound += 1;
          lane.lifeLossThisRound += 1;
          for (const l of game.lanes) {
            if (l.side === side) l.lives = game.teamHp[side];
          }
          if (game.teamHp[side] <= 0) {
            for (const l of game.lanes) {
              if (l.side === side && !l.eliminated) l.eliminated = true;
            }
          }
        }
        continue;
      }

      // Warlock debuff on nearby tower
      const def = resolveUnitDef(u.type);
      if (def && def.warlockDebuff && (u.warlockCd === undefined || u.warlockCd <= 0)) {
        u.warlockCd = WARLOCK_DEBUFF_CD;
        const pos = getUnitTilePos(u, lane.path);
        if (pos) {
          let bestTile = null, bestDist = Infinity;
          for (let dtx = 0; dtx < GRID_W; dtx++) {
            for (let dty = 0; dty < GRID_H; dty++) {
              const dtile = lane.grid[dtx][dty];
              if (dtile.type !== "tower") continue;
              const d = tileDist(pos.x, pos.y, dtx, dty);
              if (d <= WARLOCK_DEBUFF_RANGE && d < bestDist) { bestDist = d; bestTile = dtile; }
            }
          }
          if (bestTile) {
            bestTile.debuffEndTick = game.tick + WARLOCK_DEBUFF_TICKS;
            bestTile.debuffMult = WARLOCK_DEBUFF_MULT;
          }
        }
      }

      // ── Wave unit combat target tracking (mobile defenders) ─────────────────
      // Validate stale target
      if (u.combatTarget && u.combatTarget.unitId) {
        const t = lane.units.find(x => x.id === u.combatTarget.unitId && x.hp > 0);
        if (!t || !canEngageDefenderInZone(u, t)) u.combatTarget = null;
      }

      // Acquire target: nearest mobile defender in the same combat zone within engagement range.
      // Do not cap attackers per defender: if a defender is alive, wave units should commit
      // instead of pathing past it toward the castle.
      if (!u.combatTarget) {
        const engageRange = getUnitEngagementRange(u.type);
        let best = null, bestDist = Infinity;
        for (const du of lane.units) {
          if (!canEngageDefenderInZone(u, du)) continue;
          const dd = dist2D(u, du);
          if (dd > engageRange) continue;
          if (dd < bestDist) { bestDist = dd; best = du; }
        }
        if (best) u.combatTarget = { unitId: best.id };
      }

      let attackedDefender = false;
      if (u.combatTarget && u.combatTarget.unitId) {
        const t        = lane.units.find(x => x.id === u.combatTarget.unitId && x.hp > 0);
        const atkRange = getUnitAttackRange(u.type);
        if (t) {
          const dist = dist2D(u, t);
          if (dist <= atkRange + 0.15) {
            if (u.atkCd <= 0) {
              // Wave units deal melee instant damage to mobile defenders
              const dmg = def ? def.dmg : (u.baseDmg || 1);
              t.hp = Math.max(0, t.hp - dmg);
              if (t.hp <= 0) {
                const htile = lane.grid[t.homeTx] && lane.grid[t.homeTx][t.homeTy];
                if (htile) { htile.type = 'dead_tower'; htile.mobilized = false; }
                lane.mobileDefenders.delete(`${t.homeTx}_${t.homeTy}`);
                combatLog.logEvent(game, 'defender_killed', { x: t.homeTx, y: t.homeTy, defenderType: t.type, killedBy: u.id, killedByType: u.type, lane: lane.laneIndex });
                u.combatTarget = null;
              }
              u.atkCd = def ? (def.atkCdTicks || 20) : 20;
              attackedDefender = true;
            } else {
              attackedDefender = true; // in range but on cooldown — hold position
            }
          } else {
            // Move toward defender
            moveToward2D(u, t.posX, t.posY, u.baseSpeed || 0.18, 0, GRID_W - 1, 0, GRID_H + SHARED_SUFFIX_LENGTH - 1);
          }
        } else {
          u.combatTarget = null;
        }
      }

      // Advance toward castle when not engaged
      if (!u.combatTarget && !attackedDefender) {
        const maxY = GRID_H + SHARED_SUFFIX_LENGTH - 1;
        moveToward2D(u, SPAWN_X, maxY, u.baseSpeed || 0.18, 0, GRID_W - 1, 0, maxY);
      }
    }

    const splitUnits = lane.units.filter(u => u.hp > 0 && (u.posY !== undefined ? u.posY < GRID_H : u.pathIdx < GRID_H));
    const mergeUnits = lane.units.filter(u => u.hp > 0 && (u.posY !== undefined ? u.posY >= GRID_H : u.pathIdx >= GRID_H));
    applySeparation2D(splitUnits, MIN_UNIT_SPACING, 0, GRID_W - 1, 0, GRID_H);
    applySeparation2D(mergeUnits, MIN_UNIT_SPACING, 0, GRID_W - 1, GRID_H, GRID_H + SHARED_SUFFIX_LENGTH - 1);

    // DEBUG: log max pathIdx once per second (every 20 ticks) so we can see if units enter suffix
    if (game.tick % 20 === 0 && lane.units.length > 0) {
      const waveUnits = lane.units.filter(u => u.isWaveUnit && u.hp > 0);
      if (waveUnits.length > 0) {
        const maxIdx = Math.max(...waveUnits.map(u => u.pathIdx));
        const minIdx = Math.min(...waveUnits.map(u => u.pathIdx));
        console.log(`[DEBUG lane${lane.laneIndex} tick${game.tick}] wave units=${waveUnits.length} pathIdx min=${minIdx.toFixed(2)} max=${maxIdx.toFixed(2)} GRID_H=${GRID_H}`);
      }
    }

    // Resolve projectiles
    const killedById = new Set();
    const stillFlying = [];
    for (const p of lane.projectiles) {
      p.ticksRemaining -= 1;
      if (p.ticksRemaining > 0) { stillFlying.push(p); continue; }
      const { dead, hit } = resolveProjectile(game, lane, p);
      for (const id of dead) killedById.add(id);
      if (p.abilities && p.abilities.length > 0 && hit.length > 0) {
        const attacker = { abilities: p.abilities };
        for (const hitId of hit) {
          if (killedById.has(hitId)) continue;
          const hitUnit = lane.units.find(u => u.id === hitId && u.hp > 0);
          if (hitUnit) resolveAbilityHook(game, lane, attacker, "onHit", { target: hitUnit });
        }
      }
    }
    lane.projectiles = stillFlying;

    for (const id of dotDeadIds) killedById.add(id);

    // onDeath hooks + combat log for killed wave units
    for (const u of lane.units) {
      if (u.hp > 0) continue;
      if (u.abilities && u.abilities.length > 0) {
        resolveAbilityHook(game, lane, u, "onDeath", {});
      }
      if (u.isWaveUnit) {
        lane.gold += u.bounty || 1;
        combatLog.logEvent(game, 'wave_unit_killed', { unitId: u.id, unitType: u.type, bounty: u.bounty || 1, lane: lane.laneIndex });
      }
    }
    lane.units = lane.units.filter(u => u.hp > 0);
  }

  // Win condition: teamHp elimination
  if (game.phase === "playing") {
    const activeLanes = game.lanes.filter(l => !l.eliminated);
    if (activeLanes.length === 0) {
      if (!game.finalSnapshotCaptured) {
        game.roundSnapshots.push({
          round: game.roundNumber,
          terminal: true,
          elapsedSeconds: Math.floor(game.tick / TICK_HZ),
          lanes: game.lanes.map(l => createRoundSnapshotLane(game, l)),
        });
        game.finalSnapshotCaptured = true;
      }
      game.phase = "ended";
      game.winner = null;
    } else {
      const aliveTeams = new Set(activeLanes.map(l => l.team));
      if (aliveTeams.size === 1) {
        if (!game.finalSnapshotCaptured) {
          game.roundSnapshots.push({
            round: game.roundNumber,
            terminal: true,
            elapsedSeconds: Math.floor(game.tick / TICK_HZ),
            lanes: game.lanes.map(l => createRoundSnapshotLane(game, l)),
          });
          game.finalSnapshotCaptured = true;
        }
        game.phase = "ended";
        game.winner = activeLanes[0].laneIndex;
      }
    }
  }

  // Combat end: if all lanes clear, move to transition
  if (game.phase === "playing" && game.roundState === "combat") {
    const activeLanes = game.lanes.filter(l => !l.eliminated);
    const allClear = activeLanes.every(
      l => l.units.filter(u => u.isWaveUnit).length === 0 && l.spawnQueue.length === 0
    );
    if (allClear) _startTransition(game);
  }
}

function createMLSnapshot(game) {
  return {
    tick: game.tick,
    phase: game.phase,
    winner: game.winner,
    incomeTicksRemaining: 0,
    // Round state
    roundState: game.roundState || "build",
    roundNumber: game.roundNumber || 1,
    roundStateTicks: game.roundStateTicks || 0,
    buildPhaseTotal: game.buildPhaseTicks || BUILD_PHASE_TICKS,
    transitionPhaseTotal: game.transitionPhaseTicks || TRANSITION_PHASE_TICKS,
    teamHp: game.teamHp || { left: game.teamHpMax || TEAM_HP_START, right: game.teamHpMax || TEAM_HP_START },
    teamHpMax: game.teamHpMax || TEAM_HP_START,
    lanes: game.lanes.map(lane => {
      const towerCells = [];
      const mobilizedCells = [];
      const deadCells = [];
      for (let x = 0; x < GRID_W; x++) {
        for (let y = 0; y < GRID_H; y++) {
          const tile = lane.grid[x][y];
          if (tile.type === "tower" && !tile.mobilized) {
            towerCells.push({
              x, y, type: tile.towerType, level: tile.towerLevel,
              targetMode: tile.targetMode || "first",
              debuffed: tile.debuffEndTick !== undefined && tile.debuffEndTick > game.tick,
              hp: tile.hp != null ? tile.hp : tile.maxHp,
              maxHp: tile.maxHp || null,
            });
          } else if (tile.type === "tower" && tile.mobilized) {
            // Mobilized: unit has moved out of tile — render as floor but
            // keep metadata so the client can show upgrade/sell on tap.
            mobilizedCells.push({
              x, y, type: tile.towerType, level: tile.towerLevel,
            });
          } else if (tile.type === "dead_tower") {
            deadCells.push({ x, y, type: tile.towerType });
          }
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

      const snapFullPath = lane.fullPath || lane.path || [];
      return {
        laneIndex: lane.laneIndex,
        team: lane.team || "red",
        side: lane.side || null,
        slotKey: lane.slotKey || null,
        slotColor: lane.slotColor || null,
        branchId: lane.branchId || null,
        branchLabel: lane.branchLabel || null,
        castleSide: lane.castleSide || null,
        eliminated: lane.eliminated,
        gold: lane.gold,
        income: lane.income,
        lives: lane.lives,
        barracksLevel: lane.barracks.level,
        towerCells,
        mobilizedCells,
        deadCells,
        path: lane.path || [],
        fullPathLength: snapFullPath.length,
        units: lane.units.map(u => {
          const pos = getUnitTilePos(u, lane.path);
          const gx = (u.posX !== undefined) ? u.posX : (pos ? pos.x : SPAWN_X);
          const gy = (u.posY !== undefined) ? u.posY : (pos ? pos.y : SPAWN_YG);
          return {
            id: u.id,
            ownerLane: u.ownerLane,
            type: u.type,
            skinKey: u.skinKey || null,
            pathIdx: u.pathIdx,
            gridX: gx,
            gridY: gy,
            // normProgress is suffix-relative: 0 = grid end (pt2), 1 = castle (pt5).
            // On-branch units (pathIdx < GRID_H) report 0; Unity uses TileToWorld for those.
            // This makes the on-branch → suffix transition seamless (TileToWorld at row GRID_H === pt2).
            normProgress: u.pathIdx <= GRID_H ? 0
                : Math.min(1, (u.pathIdx - GRID_H) / (SHARED_SUFFIX_LENGTH - 1)),
            hp: u.hp,
            maxHp: u.maxHp,
            isWaveUnit:   u.isWaveUnit  || false,
            isDefender:   u.isDefender  || false,
            isAttacking:  !!(u.combatTarget && u.combatTarget.unitId),
            level:        u.isWaveUnit ? 1 : (lane.barracksLevel || 1),
          };
        }),
        spawnQueueLength: lane.spawnQueue.length,
        projectiles,
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
    .filter(ut => ut.enabled !== false && Number(ut.build_cost) > 0)
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
    .filter(ut => ut.enabled !== false && Number(ut.send_cost) > 0)
    .map(ut => ({
      key: ut.key,
      name: ut.name,
      send_cost: Number(ut.send_cost) || 1,
      income: Number(ut.income) || 0,
      ...pickSoundFields(ut),
    }));

  return {
    tickHz: TICK_HZ,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    startGold: opt.startGold,
    startIncome: opt.startIncome,
    livesStart: opt.livesStart,
    gridW: GRID_W,
    gridH: GRID_H,
    unitDefs: getMovingUnitDefMap(),
    towerDefs: getFixedUnitDefMap(),
    towerMaxLevel: TOWER_MAX_LEVEL,
    barracksInfinite: true,
    barracksCostBase: BARRACKS_COST_BASE,
    barracksReqIncomeBase: BARRACKS_REQ_INCOME_BASE,
    barracksLevels: [],
    // Wave defense
    teamHpStart: opt.teamHpStart,
    buildPhaseTicks: opt.buildPhaseTicks,
    transitionPhaseTicks: opt.transitionPhaseTicks,
    escalationPerExtraRound: ESCALATION_PER_EXTRA_ROUND,
    battlefieldTopology: opt.battlefieldTopology,
    slotDefinitions: opt.battlefieldTopology.slotDefinitions,
    fixedUnitTypes,
    movingUnitTypes,
  };
}

/**
 * Returns a key→unitDef map for all sendable (moving/both-mode) units from the DB.
 */
function getMovingUnitDefMap() {
  const map = {};
  for (const ut of getAllUnitTypes()) {
    if (!ut.enabled) continue;
    if (Number(ut.send_cost) <= 0) continue;
    const def = resolveUnitDef(ut.key);
    if (def) map[ut.key] = def;
  }
  return map;
}

/**
 * Returns a key→towerDef map for all placeable (fixed/both-mode) units from the DB.
 */
function getFixedUnitDefMap() {
  const map = {};
  for (const ut of getAllUnitTypes()) {
    if (!ut.enabled) continue;
    if (Number(ut.build_cost) <= 0) continue;
    const def = resolveTowerDef(ut.key);
    if (def) map[ut.key] = def;
  }
  return map;
}

module.exports = {
  TICK_HZ,
  TICK_MS,
  INCOME_INTERVAL_TICKS,
  UNIT_DEFS,
  TOWER_DEFS,
  TEAM_HP_START,
  BUILD_PHASE_TICKS,
  TRANSITION_PHASE_TICKS,
  getBarracksLevelDef,
  resolveUnitDef,
  getMovingUnitDefMap,
  getFixedUnitDefMap,
  GRID_W,
  GRID_H,
  createMLGame,
  applyMLAction,
  mlTick,
  createMLSnapshot,
  createMLPublicConfig,
  resolveTowerDef,
  getLaneBuildValue,
};
