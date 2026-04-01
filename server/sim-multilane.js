// server/sim-multilane.js
"use strict";

/**
 * Fortress-survival PvP simulation.
 * Center-spawned waves route into each lane's Town Core while barracks units
 * sabotage opponents over shared battlefield routes.
 * Authored fortress pads, walls, gates, and turrets remain part of the active
 * game, but retired classic tile-grid actions stay blocked here on purpose so
 * older clients, AI code, and room flows cannot quietly reactivate them.
 *
 * Wired to sim-core (computeDamage, fireProjectile, resolveProjectile, mulberry32 RNG)
 * and the unitTypes DB cache. UNIT_DEFS / TOWER_DEFS remain exported only for
 * backward compatibility; live unit and tower data comes from unitTypes.
 * Match-start economy and wave settings are expected to come from the active
 * multilane config and should fail loudly when missing or invalid.
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
const log = require("./logger");
const {
  DEFAULT_FORT_PRESENTATION_KEY,
  resolveFortCatalogUnitKey,
  resolveFortDisplayName,
  resolveFortPortraitKey,
  resolveFortSkinKey,
  isFortArchetypeKey,
} = require("./game/fortUnitCatalog");
const fortressSystem = require("./game/multilane/fortressSystem");
const barracksSystem = require("./game/multilane/barracksSystem");
const routeGraph = require("./game/multilane/routeGraph");
const {
  getCurrentBarracksMult,
  getBarracksUpgradeDef,
} = require("./barracksLevels");
const {
  createMLSnapshot: buildMLSnapshot,
  createMLPublicConfig: buildMLPublicConfig,
} = require("./sim-multilane-serialization");
const {
  FRONT_GATE_COMBAT_OFFSET,
  FORTRESS_BUILD_STATES,
  FORTRESS_BUILDING_DEFS,
  FORTRESS_PAD_DEFS,
} = fortressSystem;
const {
  BARRACKS_LEVEL_ONE_SPEED_MULT,
  SPEED_UPGRADE_STEP,
  BARRACKS_COST_BASE,
  BARRACKS_REQ_INCOME_BASE,
  BARRACKS_ROSTER_REFUND_PCT,
  STARTING_COMBAT_TEST_BARRACKS_ID,
  STARTING_COMBAT_TEST_MILITIA_ROSTER_KEY,
  BARRACKS_SPAWN_ROLE_ORDER,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  HERO_ROSTER_DEFS,
  MARKET_ROSTER_DEFS,
} = barracksSystem;
const {
  ROUTE_TYPES,
  LANE_NODE_IDS,
  RouteMineNode,
  ROUTE_GRAPH_NODE_POSITIONS,
  ROUTE_SEGMENT_POLYLINES,
} = routeGraph;

const TICK_HZ = 20;
const TICK_MS = Math.floor(1000 / TICK_HZ);
const INCOME_INTERVAL_TICKS = 240; // 12 s
const TOWER_MAX_LEVEL = 10;
const MAX_UNITS_PER_LANE = 200;
const GATE_KILL_BOUNTY = 10;
const ENABLE_WAVE_UNIT_TRACE = process.env.ENABLE_WAVE_UNIT_TRACE === "1";
const WAVE_UNIT_STATES = Object.freeze({
  IDLE: "IDLE",
  MOVING: "MOVING",
  COMBAT: "COMBAT",
  DEAD: "DEAD",
});

// Grid constants
const GRID_W = 11;
const GRID_H = 28;
const SPAWN_X = 5;
const SPAWN_YG = 0;
const CASTLE_X = 5;
const CASTLE_YG = 27;
const MAX_PATH_LEN = GRID_W * GRID_H; // kept for reference; path is always GRID_H steps
const SPLASH_RADIUS_TILES = 1.5;
const MIN_UNIT_SPACING = 0.8;      // minimum pathIdx gap enforced by applySeparation

// Combat movement tuning
// All combat movers share a fixed server-side base path speed so the battlefield
// reads consistently. Barracks progression then scales that baseline up in
// 0.25x steps, starting at half-speed until the player invests in upgrades.
const BASE_COMBAT_PATH_SPEED = 0.25;
// Mobile defender constants
const DEFENDER_BASE_SPEED   = BASE_COMBAT_PATH_SPEED * BARRACKS_LEVEL_ONE_SPEED_MULT;
const ENGAGEMENT_RANGE_PADDING = 2.0; // extra leash beyond attack range before a unit opens fire
const SEP_DAMP              = 0.35;
const SEP_MAX_PUSH          = 0.16;
const CONTACT_SLOT_TOLERANCE = 0.10;
// Lane-controlled units use per-unit anchor slots for travel and defend bounds,
// while combat targeting and surround decisions stay strictly per-unit.
const USE_PER_UNIT_ANCHOR_SLOTS = true;

// Wave defense constants (Forge Wars Wave Rework Phase 1)
const TEAM_HP_START = 20;
const BARRACKS_SEND_TIMER_TICKS = 30 * TICK_HZ;
const WAVE_TIMER_TICKS = 120 * TICK_HZ;
const BUILD_PHASE_TICKS = 600;       // 30 s at 20 Hz
const TRANSITION_PHASE_TICKS = 200;  // 10 s at 20 Hz
const ESCALATION_PER_EXTRA_ROUND = 0.10; // +10% HP/DMG per round beyond last wave

function getMlRuntimeSettings() {
  const cfg = gameConfig.getRequiredActiveConfig("multilane");
  const gp = cfg.globalParams;
  return {
    startGold: gp.startGold,
    startIncome: gp.startIncome,
    livesStart: gp.livesStart,
    teamHpStart: gp.teamHpStart,
    buildPhaseTicks: gp.buildPhaseTicks,
    transitionPhaseTicks: gp.transitionPhaseTicks,
  };
}

// Shared path suffix appended after each branch's BFS path.
// Represents the outer shared bridge leading to the enemy base:
//   Left side  (lanes 0,1): Bridge_A → Dwarf Base
//   Right side (lanes 2,3): Bridge_F → Goblin Base
// Both lanes on the same side share this bridge. Towers cannot target units here
// (virtual coordinates are outside the private build grid).
const SHARED_SUFFIX_LENGTH = 28; // matches private branch grid length (GRID_H)

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
  { laneIndex: 1, slotKey: "left_b", side: "left",  slotColor: "yellow", branchId: "left_branch_b",  branchLabel: "Yellow Branch", castleSide: "right" },
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

// Keep SCHEDULED_WAVE as a compatibility alias while older callers are archived.
const SPAWN_SOURCE_TYPES = Object.freeze({
  DUNGEON_WAVE: "dungeon_wave",
  SCHEDULED_WAVE: "dungeon_wave",
  BARRACKS_ROSTER: "barracks_roster",
  BARRACKS_HERO: "barracks_hero",
});

const ALLEGIANCE_KEYS = Object.freeze({
  RED: "red",
  YELLOW: "yellow",
  BLUE: "blue",
  GREEN: "green",
  DUNGEON: "dungeon",
});

const PLAYER_ALLEGIANCE_KEYS = new Set([
  ALLEGIANCE_KEYS.RED,
  ALLEGIANCE_KEYS.YELLOW,
  ALLEGIANCE_KEYS.BLUE,
  ALLEGIANCE_KEYS.GREEN,
]);

const PATH_CONTRACT_TYPES = Object.freeze({
  WAVE_LANE: "wave_lane",
  BARRACKS_CROSS: "barracks_cross",
  BARRACKS_LOOP: "barracks_loop",
  GUARD_ANCHOR: "guard_anchor",
  INTERCEPT: "intercept",
  RETREAT_ANCHOR: "retreat_anchor",
});

const UNIT_STANCES = Object.freeze({
  DEFEND: "DEFEND",
  ATTACK: "ATTACK",
  HOLD: "HOLD",
  RETREAT: "RETREAT",
  ADVANCE: "ATTACK",
});

const LANE_COMMAND_STATES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  HOLD: "DEFEND",
  RETREAT: "RETREAT",
  CALLBACK: "RETREAT",
  FORWARD: "ATTACK",
});

const UNIT_MOVEMENT_MODES = Object.freeze({
  LANE_TRAVEL: "LaneTravel",
  ANCHOR_JOIN: "AnchorJoin",
  COMBAT_ENGAGE: "CombatEngage",
  RETURN_TO_ANCHOR: "ReturnToAnchor",
});

const LANE_STANCE_ANCHOR_KINDS = Object.freeze({
  INSIDE_GATE: "insideGateAnchor",
  OUTSIDE_GATE: "outsideGateAnchor",
  ENEMY_CORE: "enemyCoreAnchor",
});

const UNIT_COMBAT_ROLES = Object.freeze({
  SHIELD: "shield",
  SWORD: "sword",
  SPEAR: "spear",
  ARCHER: "archer",
  MAGE: "mage",
  PRIEST: "priest",
});

const UNIT_PREFERRED_BANDS = Object.freeze({
  SHIELD_FRONT: "shield_front",
  SWORD_LINE: "sword_line",
  SPEAR_LINE: "spear_line",
  RANGED_REAR: "ranged_rear",
  PRIEST_REAR: "priest_rear",
});

const OPPOSING_LANE_INDEX = Object.freeze([1, 0, 3, 2]);
const ROUTE_TRACE_MIN_DISTANCE_TO_CORE = 6.5;
const ROUTE_BLOCKING_CORRIDOR_RADIUS = 4.5;
const DEFENDER_ENGAGEMENT_RANGE = ROUTE_BLOCKING_CORRIDOR_RADIUS;
const ROUTE_BLOCKING_FORWARD_DOT = 0.1;
const DEFENDER_HOLD_LEASH = 7.5;
const ROUTE_SLOT_ROW_SPACING = 1.15;
const ROUTE_SLOT_COLUMN_SPACING = 1.15;
const ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY = ROUTE_SLOT_COLUMN_SPACING * 0.7;
const LANE_COMMAND_DEFENSE_RADIUS = ROUTE_BLOCKING_CORRIDOR_RADIUS * 3;
const LANE_COMMAND_COMBAT_LEASH = 8.0;
const LANE_COMMAND_HOME_CONTAINER_PROGRESS = 0.18;
const LANE_ANCHOR_APPROACH_PROGRESS_TOLERANCE = 0.025;
const LANE_ANCHOR_JOIN_DISTANCE = 1.6;
const LANE_ANCHOR_ARRIVAL_DEAD_ZONE = 0.2;
const LANE_ANCHOR_MAX_COLUMNS = 5;
const LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE = 0.9;
const LANE_ANCHOR_SETTLED_SEPARATION_SCALE = 0.25;
const LANE_ANCHOR_MIXED_SEPARATION_SCALE = 0.6;
const LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET = 0.15;
const LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK = 0.12;
const LANE_COMBAT_POCKET_RADIUS_PADDING = ROUTE_SLOT_ROW_SPACING * 2.5;
const LANE_COMBAT_POCKET_RADIUS_SCALE = 0.6;
const LANE_COMBAT_TARGET_LOCK_TICKS = Math.max(1, Math.round(TICK_HZ * 0.5));
const LANE_COMBAT_REGROUP_TICKS = Math.max(1, Math.round(TICK_HZ * 0.35));
const LANE_COMBAT_SWITCH_DISTANCE_MARGIN = 0.75;
// Keep home-side command anchors aligned with the fortress front-gate combat
// offset so DEFEND units actually stage at the gate instead of collapsing
// around the Town Core.
const FORTRESS_INTERIOR_ASSAULT_RADIUS = FRONT_GATE_COMBAT_OFFSET + ROUTE_BLOCKING_CORRIDOR_RADIUS;
const STRUCTURE_TARGET_VICINITY_PADDING = ROUTE_SLOT_ROW_SPACING * 1.5;
const INSIDE_GATE_ANCHOR_OFFSET = FRONT_GATE_COMBAT_OFFSET - 2.0;
const OUTSIDE_GATE_ANCHOR_OFFSET = FRONT_GATE_COMBAT_OFFSET + 4.0;
const ANCHOR_SUPPORT_TARGET_RADIUS = 7.5;
const WAVE_ROUTE_COMBAT_RECOVERY_TICKS = Math.max(1, Math.round(TICK_HZ * 1.0));
const LEGACY_ACTION_REJECTION_REASONS = Object.freeze({
  spawn_unit: "Manual CMD sends were removed. Units must come from Barracks.",
  place_unit: "Tile-grid defender placement is disabled in fortress mode",
  upgrade_tower: "Tile-grid defender upgrades are disabled in fortress mode",
  bulk_upgrade_towers: "Tile-grid defender upgrades are disabled in fortress mode",
  set_tower_target: "Tile-grid defender targeting is disabled in fortress mode",
  upgrade_barracks: "Select Barracks Left, Center, or Right to upgrade that building.",
  sell_tower: "Tile-grid defender selling is disabled in fortress mode",
  set_autosend: "CMD autosend was removed. Units must come from Barracks.",
});

// Route topology and reusable route math now live in server/game/multilane/routeGraph.js.
function normalize2D(vec) {
  return routeGraph.normalize2D(vec);
}

function perpendicular2D(vec) {
  return routeGraph.perpendicular2D(vec);
}

function getLaneNodeId(laneIndex) {
  return routeGraph.getLaneNodeId(laneIndex);
}

function getWaveSpawnNodeId(laneIndex) {
  return routeGraph.getWaveSpawnNodeId(laneIndex);
}

function getLaneCombatAxes(laneIndex) {
  return routeGraph.getLaneCombatAxes(laneIndex);
}

function getBarracksRouteStartNodeId(laneIndex, barracksId) {
  return routeGraph.getBarracksRouteStartNodeId(laneIndex, barracksId);
}

function getLaneCoreNodeIdForRouteNode(nodeId) {
  return routeGraph.getLaneCoreNodeIdForRouteNode(nodeId);
}

function isBarracksRouteStartNode(nodeId) {
  return routeGraph.isBarracksRouteStartNode(nodeId);
}

function getWaveSpawnWorldPosition(laneIndex) {
  return routeGraph.getWaveSpawnWorldPosition(laneIndex);
}

function getPadWorldPosition(laneIndex, gridX, gridY) {
  return routeGraph.getPadWorldPosition(laneIndex, gridX, gridY);
}

function getBarracksSiteWorldPosition(laneIndex, barracksId) {
  return routeGraph.getBarracksSiteWorldPosition(laneIndex, barracksId);
}

function resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex) {
  if (!game || !Array.isArray(game.lanes))
    return null;

  for (let step = 1; step <= game.lanes.length; step++) {
    const idx = (sourceLaneIndex + step) % game.lanes.length;
    if (isOpponentLane(game, sourceLaneIndex, idx))
      return idx;
  }
  return null;
}

function resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex) {
  const opposingIdx = Number.isInteger(sourceLaneIndex) && sourceLaneIndex >= 0 && sourceLaneIndex < OPPOSING_LANE_INDEX.length
    ? OPPOSING_LANE_INDEX[sourceLaneIndex]
    : null;
  if (Number.isInteger(opposingIdx) && isOpponentLane(game, sourceLaneIndex, opposingIdx))
    return opposingIdx;
  return resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex);
}

function normalizeLaneCommandState(value) {
  const normalized = String(value || "").trim().toUpperCase();
  switch (normalized) {
    case LANE_COMMAND_STATES.ATTACK:
    case "ADVANCE":
      return LANE_COMMAND_STATES.ATTACK;
    case LANE_COMMAND_STATES.DEFEND:
    case "HOLD":
      return LANE_COMMAND_STATES.DEFEND;
    case LANE_COMMAND_STATES.RETREAT:
    case "CALLBACK":
      return LANE_COMMAND_STATES.RETREAT;
    default:
      return null;
  }
}

function isLaneCombatEnabledCommandState(commandState) {
  return commandState === LANE_COMMAND_STATES.ATTACK
    || commandState === LANE_COMMAND_STATES.DEFEND;
}

function getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex) {
  if (!Number.isInteger(sourceLaneIndex) || sourceLaneIndex < 0)
    return null;

  const opposingLaneIndex = OPPOSING_LANE_INDEX[sourceLaneIndex];
  if (game && Array.isArray(game.lanes) && Number.isInteger(opposingLaneIndex)
      && opposingLaneIndex >= 0 && opposingLaneIndex < game.lanes.length) {
    return opposingLaneIndex;
  }

  if (game && Array.isArray(game.lanes) && game.lanes.length > 1) {
    const resolved = resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex);
    if (Number.isInteger(resolved))
      return resolved;
  }

  return sourceLaneIndex;
}

function getLaneCommandState(lane) {
  return normalizeLaneCommandState(lane && lane.commandState) || LANE_COMMAND_STATES.DEFEND;
}

function getLaneCommandObjectiveLaneIndex(game, laneOrLaneIndex) {
  const lane = typeof laneOrLaneIndex === "object"
    ? laneOrLaneIndex
    : getSourceLane(game, laneOrLaneIndex);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const explicit = Number.isInteger(lane.commandTargetLaneIndex)
    ? lane.commandTargetLaneIndex
    : null;
  if (explicit !== null && game && Array.isArray(game.lanes)
      && explicit >= 0 && explicit < game.lanes.length) {
    return explicit;
  }

  return getDefaultLaneObjectiveLaneIndex(game, lane.laneIndex);
}

function getLaneCommandRouteObjectiveLaneIndex(game, laneOrLaneIndex) {
  const lane = typeof laneOrLaneIndex === "object"
    ? laneOrLaneIndex
    : getSourceLane(game, laneOrLaneIndex);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane);
  if (commandState === LANE_COMMAND_STATES.RETREAT)
    return lane.laneIndex;

  return getLaneCommandObjectiveLaneIndex(game, lane);
}

function getLaneCommandEngagementRadius(lane) {
  const commandState = getLaneCommandState(lane);
  if (!isLaneCombatEnabledCommandState(commandState))
    return 0;
  const explicitRadius = Number(lane && lane.engagementRadius);
  if (Number.isFinite(explicitRadius) && explicitRadius > 0)
    return commandState === LANE_COMMAND_STATES.DEFEND
      ? Math.max(explicitRadius, LANE_COMMAND_DEFENSE_RADIUS)
      : Math.max(explicitRadius, LANE_COMMAND_COMBAT_LEASH);
  return commandState === LANE_COMMAND_STATES.DEFEND
    ? LANE_COMMAND_DEFENSE_RADIUS
    : LANE_COMMAND_COMBAT_LEASH;
}

function getLaneCommandAnchorProgress(lane) {
  const commandState = getLaneCommandState(lane);
  if (commandState === LANE_COMMAND_STATES.ATTACK)
    return 1;
  if (commandState === LANE_COMMAND_STATES.DEFEND || commandState === LANE_COMMAND_STATES.RETREAT)
    return 0;
  const raw = Number(lane && lane.commandAnchorProgress);
  if (!Number.isFinite(raw))
    return 0;
  return Math.max(0, Math.min(1, raw));
}

function resolveLaneCommandContainerLaneIndex(game, lane) {
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane);
  if (commandState === LANE_COMMAND_STATES.RETREAT)
    return lane.laneIndex;

  const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  if (!Number.isInteger(objectiveLaneIndex))
    return lane.laneIndex;

  return getLaneCommandAnchorProgress(lane) <= LANE_COMMAND_HOME_CONTAINER_PROGRESS
    ? lane.laneIndex
    : objectiveLaneIndex;
}

function resolveRouteNodeLaneIndex(nodeId) {
  const coreNodeId = getLaneCoreNodeIdForRouteNode(nodeId);
  return coreNodeId ? getNodeIndex(coreNodeId) : -1;
}

function resolveLaneControlledUnitCurrentSegmentLaneIndex(unit) {
  const currentSegment = String(unit && unit.currentSegment || "").trim().toUpperCase();
  if (currentSegment === "")
    return null;

  const parts = currentSegment.split("_");
  if (parts.length !== 2)
    return null;

  const fromLaneIndex = resolveRouteNodeLaneIndex(parts[0]);
  const toLaneIndex = resolveRouteNodeLaneIndex(parts[1]);
  if (fromLaneIndex >= 0 && toLaneIndex >= 0)
    return fromLaneIndex === toLaneIndex
      ? fromLaneIndex
      : ((Math.max(0, Math.min(1, Number(unit && unit.segmentProgress) || 0)) < 0.5)
        ? fromLaneIndex
        : toLaneIndex);
  if (fromLaneIndex >= 0)
    return fromLaneIndex;
  if (toLaneIndex >= 0)
    return toLaneIndex;
  return null;
}

function resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit) {
  const fallbackLaneIndex = resolveLaneCommandContainerLaneIndex(game, ownerLane);
  if (!ownerLane || !isLaneControlledUnit(unit))
    return fallbackLaneIndex;
  if (USE_PER_UNIT_ANCHOR_SLOTS)
    return fallbackLaneIndex;

  const currentSegmentLaneIndex = resolveLaneControlledUnitCurrentSegmentLaneIndex(unit);
  if (Number.isInteger(currentSegmentLaneIndex) && currentSegmentLaneIndex >= 0)
    return currentSegmentLaneIndex;

  const routeStartLaneIndex = resolveRouteNodeLaneIndex(unit && unit.routeStartNode);
  const routeTargetLaneIndex = resolveRouteNodeLaneIndex(unit && unit.routeTargetNode);
  if (routeStartLaneIndex < 0 || routeTargetLaneIndex < 0)
    return fallbackLaneIndex;
  if (routeStartLaneIndex === routeTargetLaneIndex)
    return routeStartLaneIndex;

  const routeProgress = computeUnitRoutePathIndex(unit);
  if (!Number.isFinite(routeProgress))
    return fallbackLaneIndex;

  return routeProgress < 0.5
    ? routeStartLaneIndex
    : routeTargetLaneIndex;
}

function buildLaneCommandCoreRouteSegments(sourceLaneIndex, objectiveLaneIndex) {
  const sourceNodeId = getLaneNodeId(sourceLaneIndex);
  const objectiveNodeId = getLaneNodeId(objectiveLaneIndex);
  if (!sourceNodeId || !objectiveNodeId)
    return null;
  if (sourceNodeId === objectiveNodeId)
    return [];
  return buildRouteSegments(ROUTE_TYPES.CENTER_CROSS, sourceNodeId, objectiveNodeId);
}

function buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksId, objectiveLaneIndex) {
  const sourceNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksId);
  const sourceCoreNodeId = getLaneNodeId(sourceLaneIndex);
  const coreRouteSegments = buildLaneCommandCoreRouteSegments(sourceLaneIndex, objectiveLaneIndex);
  if (!sourceNodeId || !sourceCoreNodeId || !Array.isArray(coreRouteSegments))
    return null;
  return [`${sourceNodeId}_${sourceCoreNodeId}`, ...coreRouteSegments];
}

function buildLaneCommandAnchorSet(game, lane) {
  if (!game || !lane || !Number.isInteger(lane.laneIndex))
    return null;

  const axes = getLaneCombatAxes(lane.laneIndex);
  if (!axes)
    return null;

  const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  const objectiveLane = getLaneByIndex(game, objectiveLaneIndex);
  const enemyCoreTarget = objectiveLane ? getLaneTownCoreCombatTarget(objectiveLane) : null;
  const attackFacing = enemyCoreTarget
    ? normalize2D({
        x: Number(enemyCoreTarget.posX) - Number(axes.core.x),
        y: Number(enemyCoreTarget.posY) - Number(axes.core.y),
      })
    : axes.forward;

  return {
    insideGateAnchor: {
      x: Number(axes.core.x) - (Number(axes.forward.x) * INSIDE_GATE_ANCHOR_OFFSET),
      y: Number(axes.core.y) - (Number(axes.forward.y) * INSIDE_GATE_ANCHOR_OFFSET),
    },
    outsideGateAnchor: {
      x: Number(axes.core.x) + (Number(axes.forward.x) * OUTSIDE_GATE_ANCHOR_OFFSET),
      y: Number(axes.core.y) + (Number(axes.forward.y) * OUTSIDE_GATE_ANCHOR_OFFSET),
    },
    enemyCoreAnchor: enemyCoreTarget
      ? {
          x: Number(enemyCoreTarget.posX) || 0,
          y: Number(enemyCoreTarget.posY) || 0,
        }
      : {
          x: Number(axes.core.x) || 0,
          y: Number(axes.core.y) || 0,
        },
    objectiveLaneIndex,
    forward: axes.forward,
    lateral: axes.lateral,
    attackFacing,
  };
}

function sampleLaneCommandAnchor(game, lane) {
  if (!game || !lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane);
  const anchorSet = buildLaneCommandAnchorSet(game, lane);
  if (!anchorSet)
    return null;
  const objectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, lane);
  const baseRouteSegments = buildLaneCommandCoreRouteSegments(lane.laneIndex, objectiveLaneIndex);
  if (!Array.isArray(baseRouteSegments))
    return null;
  const anchorProgress = getLaneCommandAnchorProgress(lane);
  const anchorKind = commandState === LANE_COMMAND_STATES.RETREAT
    ? LANE_STANCE_ANCHOR_KINDS.INSIDE_GATE
    : (commandState === LANE_COMMAND_STATES.DEFEND
      ? LANE_STANCE_ANCHOR_KINDS.OUTSIDE_GATE
      : LANE_STANCE_ANCHOR_KINDS.ENEMY_CORE);
  const anchorPoint = anchorKind === LANE_STANCE_ANCHOR_KINDS.INSIDE_GATE
    ? anchorSet.insideGateAnchor
    : (anchorKind === LANE_STANCE_ANCHOR_KINDS.OUTSIDE_GATE
      ? anchorSet.outsideGateAnchor
      : anchorSet.enemyCoreAnchor);
  const facing = normalize2D(
    commandState === LANE_COMMAND_STATES.ATTACK
      ? anchorSet.attackFacing
      : anchorSet.forward
  );
  return {
    commandState,
    combatEnabled: isLaneCombatEnabledCommandState(commandState),
    engagementRadius: getLaneCommandEngagementRadius(lane),
    objectiveLaneIndex,
    containerLaneIndex: resolveLaneCommandContainerLaneIndex(game, lane),
    anchorProgress,
    anchorKind,
    anchorX: Number(anchorPoint && anchorPoint.x) || 0,
    anchorY: Number(anchorPoint && anchorPoint.y) || 0,
    facing,
    lateral: perpendicular2D(facing),
    insideGateAnchor: anchorSet.insideGateAnchor,
    outsideGateAnchor: anchorSet.outsideGateAnchor,
    enemyCoreAnchor: anchorSet.enemyCoreAnchor,
    baseRouteSegments,
  };
}

function normalizeLegacyDefenderUnit(game, fallbackLane, unit) {
  if (!unit || !unit.isDefender)
    return false;

  const ownerLane = getSourceLane(game, Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1) || fallbackLane;
  if (!ownerLane || !Number.isInteger(ownerLane.laneIndex))
    return false;

  const normalizedBarracksId = resolveUnitSourceBarracksId(unit) || "center";
  const allegianceKey = resolveLaneAllegianceKey(ownerLane) || resolveUnitAllegianceKey(game, ownerLane, unit);

  unit.isDefender = false;
  unit.spawnSourceType = unit.isHero ? SPAWN_SOURCE_TYPES.BARRACKS_HERO : SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;
  unit.sourceLaneIndex = ownerLane.laneIndex;
  unit.ownerLaneIndex = ownerLane.laneIndex;
  unit.ownerLane = ownerLane.laneIndex;
  unit.targetLaneIndex = Number.isInteger(unit.targetLaneIndex) ? unit.targetLaneIndex : ownerLane.laneIndex;
  unit.laneId = unit.targetLaneIndex;
  unit.sourceBarracksId = normalizedBarracksId;
  unit.sourceBarracksKey = normalizedBarracksId;
  unit.barracksId = normalizedBarracksId;
  unit.allegianceKey = allegianceKey;
  unit.sourceTeam = resolveLegacySourceTeamFromAllegianceKey(allegianceKey) || ownerLane.team || null;
  unit.stance = null;
  unit.pathContractType = null;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.defState = null;
  unit._legacyDefenderNormalized = true;
  return true;
}

function normalizeLegacyDefenderUnits(game) {
  if (!game || !Array.isArray(game.lanes))
    return;

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (const unit of lane.units) {
      if (normalizeLegacyDefenderUnit(game, lane, unit))
        applyCanonicalUnitMirrors(game, lane, unit);
    }
  }
}

function isLaneControlledUnit(unit) {
  if (!unit || !Number.isInteger(unit.sourceLaneIndex) || unit.sourceLaneIndex < 0)
    return false;
  if (unit.isDefender)
    return false;
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  return spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER
    || spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_HERO;
}

function getLaneCommandOwnerLane(game, unit) {
  if (!isLaneControlledUnit(unit))
    return null;
  return getSourceLane(game, unit.sourceLaneIndex);
}

function getLaneCommandStateForUnit(game, unit) {
  return getLaneCommandState(getLaneCommandOwnerLane(game, unit));
}

function isLaneCommandCombatEnabledForUnit(game, unit) {
  if (!isLaneControlledUnit(unit))
    return true;
  return isLaneCombatEnabledCommandState(getLaneCommandStateForUnit(game, unit));
}

function resolveTargetLaneForBarracksSend(game, sourceLaneIndex, barracksId) {
  const sourceLane = getSourceLane(game, sourceLaneIndex);
  if (!sourceLane)
    return null;
  return resolveLaneCommandContainerLaneIndex(game, sourceLane);
}

function buildRouteSegments(routeType, sourceNodeId, targetNodeId) {
  return routeGraph.buildRouteSegments(routeType, sourceNodeId, targetNodeId);
}

function parseRouteSegmentId(segmentId) {
  return routeGraph.parseRouteSegmentId(segmentId);
}

function getRouteLength(routeSegments) {
  return routeGraph.getRouteLength(routeSegments);
}

function sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset = 0) {
  return routeGraph.sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset);
}

function advanceRouteState(unit, deltaDistance) {
  return routeGraph.advanceRouteState(unit, deltaDistance);
}

function moveScalarToward(value, target, maxDelta) {
  const numericValue = Number(value);
  const numericTarget = Number(target);
  const safeDelta = Math.max(0, Number(maxDelta) || 0);
  if (!Number.isFinite(numericValue))
    return Number.isFinite(numericTarget) ? numericTarget : 0;
  if (!Number.isFinite(numericTarget))
    return numericValue;
  if (safeDelta <= 0)
    return numericValue;

  const delta = numericTarget - numericValue;
  if (Math.abs(delta) <= safeDelta)
    return numericTarget;
  return numericValue + (Math.sign(delta) * safeDelta);
}

function relaxUnitRouteOffsets(unit, speed) {
  if (!unit)
    return;

  const baseRelaxStep = Math.max(0.12, Math.abs(Number(speed) || 0) * 0.25);
  const relaxStep = USE_PER_UNIT_ANCHOR_SLOTS && isLaneControlledUnit(unit)
    ? Math.max(0.35, Math.abs(Number(speed) || 0) * 0.35)
    : baseRelaxStep;
  unit.routeLateralOffset = moveScalarToward(unit.routeLateralOffset, 0, relaxStep);
  unit.routeLongitudinalOffset = moveScalarToward(
    unit.routeLongitudinalOffset,
    0,
    USE_PER_UNIT_ANCHOR_SLOTS && isLaneControlledUnit(unit)
      ? relaxStep
      : (relaxStep * 0.6)
  );
}

function setUnitRouteSnapshotState(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const routeProgress = computeUnitRoutePathIndex(unit);
  const longitudinalOffset = Number(unit.routeLongitudinalOffset) || 0;
  const lateralOffset = Number(unit.routeLateralOffset) || 0;
  const sample = sampleContinuousRoutePosition(
    unit.routeSegments,
    routeProgress,
    longitudinalOffset,
    lateralOffset
  );
  if (!sample)
    return false;

  unit.currentSegment = sample.segmentId;
  unit.posX = sample.point.x;
  unit.posY = sample.point.y;
  unit.pathIdx = routeProgress;
  unit.routeWorldX = unit.posX;
  unit.routeWorldY = unit.posY;
  return true;
}

function computeUnitRoutePathIndex(unit) {
  return routeGraph.computeUnitRoutePathIndex(unit);
}

function sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset = 0, lateralOffset = 0) {
  return routeGraph.sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset, lateralOffset);
}

function resolveSpawnOriginForUnit(unit, targetLane) {
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.SCHEDULED_WAVE)
    return getWaveSpawnWorldPosition(targetLane && targetLane.laneIndex);

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const sourceBarracksKey = normalizeBarracksSiteId(unit && (unit.sourceBarracksKey || unit.sourceBarracksId));
  if (sourceLaneIndex < 0 || !sourceBarracksKey)
    return null;

  return getBarracksSiteWorldPosition(sourceLaneIndex, sourceBarracksKey);
}

function resolveRouteContractForUnit(game, targetLane, unit) {
  if (!game || !targetLane || !unit)
    return { ok: false, reason: "Missing game, target lane, or unit" };

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  const targetLaneIndex = targetLane.laneIndex;
  const targetNodeId = getLaneNodeId(targetLaneIndex);
  if (!targetNodeId)
    return { ok: false, reason: `Target lane ${targetLaneIndex} is missing a route node` };

  if (spawnSourceType === SPAWN_SOURCE_TYPES.SCHEDULED_WAVE) {
    const sourceNodeId = getWaveSpawnNodeId(targetLaneIndex);
    const routeSegments = buildRouteSegments(ROUTE_TYPES.WAVE_LANE, sourceNodeId, targetNodeId);
    if (!sourceNodeId || !routeSegments)
      return { ok: false, reason: `Wave route is missing for lane ${targetLaneIndex}` };

    const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane);
    if (!spawnOrigin)
      return { ok: false, reason: `Wave spawn origin is missing for lane ${targetLaneIndex}` };

    return {
      ok: true,
      spawnSourceType,
      routeType: ROUTE_TYPES.WAVE_LANE,
      sourceNodeId,
      targetNodeId,
      routeSegments,
      spawnOrigin,
      pathId: buildRoutePathId(routeSegments),
    };
  }

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const sourceBarracksKey = normalizeBarracksSiteId(unit.sourceBarracksKey || unit.sourceBarracksId);
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, targetLane, unit);
  const objectiveNodeId = getLaneNodeId(objectiveLaneIndex);
  if (sourceLaneIndex < 0)
    return { ok: false, reason: "Barracks unit is missing sourceLaneIndex" };
  if (!sourceBarracksKey)
    return { ok: false, reason: "Barracks unit is missing a valid sourceBarracksId" };
  if (!objectiveNodeId)
    return { ok: false, reason: `Barracks unit is missing a valid objective lane (${objectiveLaneIndex})` };

  const sourceNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksKey);
  if (!sourceNodeId)
    return {
      ok: false,
      reason: `Barracks route start node is missing for lane=${sourceLaneIndex} barracks='${sourceBarracksKey}'`,
    };

  const routeSegments = buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksKey, objectiveLaneIndex);
  if (!routeSegments)
    return { ok: false, reason: `Lane command route could not be built for barracks '${sourceBarracksKey}'` };

  const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane);
  if (!spawnOrigin)
    return { ok: false, reason: `Barracks spawn origin is missing for lane=${sourceLaneIndex} barracks='${sourceBarracksKey}'` };

  return {
    ok: true,
      spawnSourceType,
      routeType: ROUTE_TYPES.CENTER_CROSS,
      sourceNodeId,
      targetNodeId: objectiveNodeId,
      objectiveLaneIndex,
      routeSegments,
    spawnOrigin,
    pathId: buildRoutePathId(routeSegments),
    barracksKey: sourceBarracksKey,
    routeLabel: "lane_command_cross",
  };
}

function resolveRedirectRouteContractForExistingLaneControlledUnit(game, currentLane, targetLane, unit) {
  if (!game || !currentLane || !targetLane || !unit)
    return { ok: false, reason: "Missing game, current lane, target lane, or unit" };
  if (!isLaneControlledUnit(unit))
    return resolveRouteContractForUnit(game, targetLane, unit);

  const currentLaneIndex = Number.isInteger(currentLane.laneIndex)
    ? currentLane.laneIndex
    : resolveUnitTargetLaneIndex(game, currentLane, unit);
  const routeTargetLaneIndex = resolveUnitObjectiveLaneIndex(game, targetLane, unit);
  let sourceNodeId = getLaneNodeId(currentLaneIndex);
  let targetNodeId = getLaneNodeId(routeTargetLaneIndex);
  let routeSegments = buildLaneCommandCoreRouteSegments(currentLaneIndex, routeTargetLaneIndex);
  let routeType = ROUTE_TYPES.CENTER_CROSS;
  let routeLabel = "lane_command_redirect";

  if ((!sourceNodeId || !targetNodeId || !Array.isArray(routeSegments) || routeSegments.length <= 0)
      && Number.isInteger(currentLaneIndex)
      && currentLaneIndex === routeTargetLaneIndex) {
    const currentCoreNodeId = getLaneNodeId(currentLaneIndex);
    const currentSegment = parseRouteSegmentId(unit && unit.currentSegment);
    const sourceBarracksId = resolveUnitSourceBarracksId(unit);
    const barracksNodeId = getBarracksRouteStartNodeId(
      Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : currentLaneIndex,
      sourceBarracksId
    );

    if (currentCoreNodeId && currentSegment) {
      const fromCoreNodeId = getLaneCoreNodeIdForRouteNode(currentSegment.fromNode);
      const toCoreNodeId = getLaneCoreNodeIdForRouteNode(currentSegment.toNode);
      if (currentSegment.fromNode === RouteMineNode && toCoreNodeId === currentCoreNodeId) {
        sourceNodeId = RouteMineNode;
        targetNodeId = currentCoreNodeId;
        routeSegments = [`${RouteMineNode}_${currentCoreNodeId}`];
      } else if (currentSegment.toNode === RouteMineNode && fromCoreNodeId === currentCoreNodeId) {
        sourceNodeId = RouteMineNode;
        targetNodeId = currentCoreNodeId;
        routeSegments = [`${RouteMineNode}_${currentCoreNodeId}`];
      } else if (barracksNodeId
          && ((currentSegment.fromNode === barracksNodeId && currentSegment.toNode === currentCoreNodeId)
            || (currentSegment.toNode === barracksNodeId && currentSegment.fromNode === currentCoreNodeId))) {
        sourceNodeId = barracksNodeId;
        targetNodeId = currentCoreNodeId;
        routeSegments = [`${barracksNodeId}_${currentCoreNodeId}`];
      }
      routeLabel = "lane_command_redirect_home";
    }
  }

  if (!sourceNodeId || !targetNodeId || !Array.isArray(routeSegments) || routeSegments.length <= 0)
    return resolveRouteContractForUnit(game, targetLane, unit);

  return {
    ok: true,
    spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
    routeType,
    sourceNodeId,
    targetNodeId,
    objectiveLaneIndex: routeTargetLaneIndex,
    routeSegments,
    pathId: buildRoutePathId(routeSegments),
    routeLabel,
  };
}

function isSameLaneHoldRouteContract(routeContract, sourceLaneIndex, targetLaneIndex) {
  if (!routeContract)
    return false;
  if (!Number.isInteger(sourceLaneIndex) || !Number.isInteger(targetLaneIndex))
    return false;
  if (sourceLaneIndex !== targetLaneIndex)
    return false;

  const sourceCoreNodeId = getLaneCoreNodeIdForRouteNode(routeContract.sourceNodeId);
  return !!sourceCoreNodeId && routeContract.targetNodeId === sourceCoreNodeId;
}

function initializeMovingUnitRouteState(game, targetLane, unit, spawnLogicalPos) {
  if (!game || !targetLane || !unit)
    return { ok: false, reason: "Missing game, target lane, or unit" };

  const routeContract = resolveRouteContractForUnit(game, targetLane, unit);
  if (!routeContract.ok) {
    log.error("[SpawnAudit][ServerRoute] rejected", {
      unitId: unit && unit.id,
      unitType: unit && unit.type,
      targetLaneIndex: targetLane && targetLane.laneIndex,
      sourceLaneIndex: unit && unit.sourceLaneIndex,
      sourceBarracksKey: unit && (unit.sourceBarracksKey || unit.sourceBarracksId) || null,
      spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
      reason: routeContract.reason,
    });
    return routeContract;
  }

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = targetLane.laneIndex;
  const logicalX = spawnLogicalPos && Number.isFinite(spawnLogicalPos.x) ? Number(spawnLogicalPos.x) : SPAWN_X;
  const logicalY = spawnLogicalPos && Number.isFinite(spawnLogicalPos.y) ? Number(spawnLogicalPos.y) : SPAWN_YG;

  unit.spawnSourceType = routeContract.spawnSourceType;
  unit.routeType = routeContract.routeType;
  unit.routeStartNode = routeContract.sourceNodeId;
  unit.routeTargetNode = routeContract.targetNodeId;
  unit.objectiveLaneIndex = routeContract.objectiveLaneIndex ?? unit.objectiveLaneIndex ?? targetLane.laneIndex;
  unit.routeSegments = routeContract.routeSegments;
  unit.routeSegmentIndex = 0;
  unit.segmentProgress = 0;
  const startSample = sampleRoutePosition(unit.routeSegments, 0, 0, 0);
  if (!startSample) {
    const failure = {
      ok: false,
      reason: `Route sample is missing for path '${routeContract.pathId}'`,
    };
    log.error("[SpawnAudit][ServerRoute] rejected", {
      unitId: unit.id,
      unitType: unit.type,
      targetLaneIndex,
      sourceLaneIndex,
      sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
      spawnSourceType: unit.spawnSourceType,
      reason: failure.reason,
    });
    return failure;
  }
  const routeTangent = normalize2D(startSample.tangent);
  const routeLateral = perpendicular2D(routeTangent);
  const originDelta = {
    x: Number(routeContract.spawnOrigin.x) - Number(startSample.point.x),
    y: Number(routeContract.spawnOrigin.y) - Number(startSample.point.y),
  };
  const spawnLateralOffset = (logicalX - SPAWN_X) * ROUTE_SLOT_COLUMN_SPACING;
  const spawnLongitudinalOffset = -logicalY * ROUTE_SLOT_ROW_SPACING;
  unit.routeLateralOffset = dot2D(originDelta, routeLateral) + spawnLateralOffset;
  unit.routeLongitudinalOffset = dot2D(originDelta, routeTangent) + spawnLongitudinalOffset;
  unit.stance = routeContract.spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
    ? UNIT_STANCES.ATTACK
    : null;
  unit.routeState = WAVE_UNIT_STATES.MOVING;
  unit.combatState = WAVE_UNIT_STATES.MOVING;
  unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  unit.blockedByStructure = false;
  unit.blockedByStructureId = null;
  unit.blockedByStructureType = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.targetLaneIndex = targetLaneIndex;
  unit.laneId = targetLaneIndex;
  if (!setUnitRouteSnapshotState(unit)) {
    const failure = {
      ok: false,
      reason: `Failed to materialize path snapshot state for path '${routeContract.pathId}'`,
    };
    log.error("[SpawnAudit][ServerRoute] rejected", {
      unitId: unit.id,
      unitType: unit.type,
      targetLaneIndex,
      sourceLaneIndex,
      sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
      spawnSourceType: unit.spawnSourceType,
      reason: failure.reason,
    });
    return failure;
  }
  applyCanonicalUnitMirrors(game, targetLane, unit);

  log.info("[SpawnAudit][ServerRoute] assigned", {
    unitId: unit.id,
    unitType: unit.type,
    spawnSourceType: unit.spawnSourceType,
    sourceLaneIndex,
    targetLaneIndex,
    sourceBarracksKey: routeContract.barracksKey || null,
    spawnOriginX: Number.isFinite(routeContract.spawnOrigin.x) ? Number(routeContract.spawnOrigin.x.toFixed(3)) : null,
    spawnOriginY: Number.isFinite(routeContract.spawnOrigin.y) ? Number(routeContract.spawnOrigin.y.toFixed(3)) : null,
    routeType: routeContract.routeType,
    routeLabel: routeContract.routeLabel || routeContract.pathId,
    routeStartNode: routeContract.sourceNodeId,
    routeTargetNode: routeContract.targetNodeId,
    pathId: routeContract.pathId,
    currentWaypointIndex: unit.routeSegmentIndex,
    nextWaypoint: resolveUnitNextWaypoint(unit),
    movementState: unit.routeState,
    currentSegment: unit.currentSegment || null,
    segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
    routeWorldX: Number.isFinite(unit.routeWorldX) ? Number(unit.routeWorldX.toFixed(3)) : null,
    routeWorldY: Number.isFinite(unit.routeWorldY) ? Number(unit.routeWorldY.toFixed(3)) : null,
    routeLateralOffset: Number.isFinite(unit.routeLateralOffset) ? Number(unit.routeLateralOffset.toFixed(3)) : null,
    routeLongitudinalOffset: Number.isFinite(unit.routeLongitudinalOffset) ? Number(unit.routeLongitudinalOffset.toFixed(3)) : null,
  });

  return {
    ok: true,
    pathId: routeContract.pathId,
  };
}

function applyRouteContractToExistingUnit(unit, routeContract, currentPosition = null) {
  if (!unit || !routeContract || routeContract.ok === false)
    return { ok: false, reason: "Missing unit or route contract" };
  if (!Array.isArray(routeContract.routeSegments) || routeContract.routeSegments.length <= 0)
    return { ok: false, reason: "Route contract is missing route segments" };

  const startSample = sampleRoutePosition(routeContract.routeSegments, 0, 0, 0);
  if (!startSample) {
    return {
      ok: false,
      reason: `Route sample is missing for path '${routeContract.pathId || "<unknown>"}'`,
    };
  }

  const safeCurrentPosition = currentPosition
    && Number.isFinite(Number(currentPosition.x))
    && Number.isFinite(Number(currentPosition.y))
    ? { x: Number(currentPosition.x), y: Number(currentPosition.y) }
    : { x: Number(unit.posX) || Number(startSample.point.x), y: Number(unit.posY) || Number(startSample.point.y) };
  const projection = projectPointOntoRouteSegments(routeContract.routeSegments, safeCurrentPosition);
  const routeSample = projection || {
    segmentIndex: 0,
    segmentProgress: 0,
    segmentId: routeContract.routeSegments[0],
    point: startSample.point,
    tangent: startSample.tangent,
  };
  const routeTangent = normalize2D(routeSample.tangent);
  const routeLateral = perpendicular2D(routeTangent);
  const routeDelta = {
    x: safeCurrentPosition.x - Number(routeSample.point.x),
    y: safeCurrentPosition.y - Number(routeSample.point.y),
  };

  unit.routeType = routeContract.routeType;
  unit.routeStartNode = routeContract.sourceNodeId;
  unit.routeTargetNode = routeContract.targetNodeId;
  unit.objectiveLaneIndex = routeContract.objectiveLaneIndex ?? unit.objectiveLaneIndex ?? unit.targetLaneIndex;
  unit.routeSegments = routeContract.routeSegments;
  unit.routeSegmentIndex = Math.max(
    0,
    Math.min(routeContract.routeSegments.length - 1, Math.floor(Number(routeSample.segmentIndex) || 0))
  );
  unit.segmentProgress = Math.max(0, Math.min(1, Number(routeSample.segmentProgress) || 0));
  unit.currentSegment = routeSample.segmentId || routeContract.routeSegments[unit.routeSegmentIndex];
  unit.routeLateralOffset = dot2D(routeDelta, routeLateral);
  unit.routeLongitudinalOffset = dot2D(routeDelta, routeTangent);
  unit.stance = null;
  unit.routeState = WAVE_UNIT_STATES.MOVING;
  unit.combatState = WAVE_UNIT_STATES.MOVING;
  unit.movementMode = UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.blockedByStructure = false;
  unit.blockedByStructureId = null;
  unit.blockedByStructureType = null;
  unit._missingRouteLogged = false;

  if (!setUnitRouteSnapshotState(unit)) {
    return {
      ok: false,
      reason: `Failed to materialize path snapshot state for path '${routeContract.pathId || "<unknown>"}'`,
    };
  }
  applyCanonicalUnitMirrors(null, null, unit);

  return {
    ok: true,
    pathId: routeContract.pathId || buildRoutePathId(routeContract.routeSegments),
  };
}

function getUnitForwardDirection(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return { x: 0, y: 1 };
  const sample = sampleContinuousRoutePosition(
    unit.routeSegments,
    computeUnitRoutePathIndex(unit),
    0,
    0
  );
  return sample ? sample.tangent : { x: 0, y: 1 };
}

function buildSampledPathFromSegments(routeSegments, sampleCount = 28) {
  return routeGraph.buildSampledPathFromSegments(routeSegments, sampleCount);
}

function sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset = 0) {
  return routeGraph.sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset);
}

function projectPointOntoPolyline(points, targetPoint) {
  return routeGraph.projectPointOntoPolyline(points, targetPoint);
}

function projectPointOntoRouteSegments(routeSegments, targetPoint) {
  return routeGraph.projectPointOntoRouteSegments(routeSegments, targetPoint);
}

function syncUnitRouteStateToWorldPosition(unit, worldPosition = null) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const targetPoint = worldPosition
    && Number.isFinite(Number(worldPosition.x))
    && Number.isFinite(Number(worldPosition.y))
    ? { x: Number(worldPosition.x), y: Number(worldPosition.y) }
    : (Number.isFinite(Number(unit.posX)) && Number.isFinite(Number(unit.posY))
      ? { x: Number(unit.posX), y: Number(unit.posY) }
      : null);
  if (!targetPoint)
    return false;

  const best = projectPointOntoRouteSegments(unit.routeSegments, targetPoint);
  if (!best)
    return false;

  const tangent = normalize2D(best.tangent);
  const lateral = perpendicular2D(tangent);
  const delta = {
    x: targetPoint.x - best.point.x,
    y: targetPoint.y - best.point.y,
  };

  unit.routeSegmentIndex = best.segmentIndex;
  unit.segmentProgress = Math.max(0, Math.min(1, Number(best.segmentProgress) || 0));
  unit.currentSegment = best.segmentId;
  unit.routeLongitudinalOffset = dot2D(delta, tangent);
  unit.routeLateralOffset = dot2D(delta, lateral);
  unit.posX = targetPoint.x;
  unit.posY = targetPoint.y;
  unit.pathIdx = computeUnitRoutePathIndex(unit);
  unit.routeWorldX = targetPoint.x;
  unit.routeWorldY = targetPoint.y;
  return true;
}

function syncMovedUnitPathState(unit) {
  if (!unit)
    return false;

  if (syncUnitRouteStateToWorldPosition(unit))
    return true;

  unit.pathIdx = unit.posY;
  return false;
}

function resolveLaneAnchorColumns(unitCount) {
  const safeCount = Math.max(1, Math.floor(Number(unitCount) || 1));
  return Math.max(1, Math.min(LANE_ANCHOR_MAX_COLUMNS, safeCount >= 4 ? 5 : safeCount));
}

function resolveCenteredSlotOffset(columnIndex, columns) {
  const safeColumns = Math.max(1, Math.floor(Number(columns) || 1));
  const safeColumnIndex = Math.max(0, Math.min(safeColumns - 1, Math.floor(Number(columnIndex) || 0)));
  const center = Math.floor(safeColumns / 2);
  if (safeColumns % 2 === 1)
    return safeColumnIndex - center;

  const raw = safeColumnIndex < center
    ? safeColumnIndex - center
    : safeColumnIndex - center + 1;
  return raw >= 0 ? raw - 0.5 : raw + 0.5;
}

function resolveLaneControlledUnitSortKey(unit) {
  const assignedSlotIndex = Number(unit && unit.assignedSlotIndex);
  if (Number.isFinite(assignedSlotIndex))
    return `slot:${String(Math.floor(assignedSlotIndex)).padStart(6, "0")}:${unit.id}`;
  const spawnIndex = Number(unit && unit.spawnIndex);
  if (Number.isFinite(spawnIndex))
    return `spawn:${String(Math.floor(spawnIndex)).padStart(6, "0")}:${unit.id}`;
  return `id:${String(unit && unit.id || "")}`;
}

function normalizeCombatRole(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case UNIT_COMBAT_ROLES.SHIELD:
      return UNIT_COMBAT_ROLES.SHIELD;
    case UNIT_COMBAT_ROLES.SPEAR:
    case "polearm":
      return UNIT_COMBAT_ROLES.SPEAR;
    case UNIT_COMBAT_ROLES.ARCHER:
    case "ranged":
      return UNIT_COMBAT_ROLES.ARCHER;
    case UNIT_COMBAT_ROLES.MAGE:
    case "arcane":
      return UNIT_COMBAT_ROLES.MAGE;
    case UNIT_COMBAT_ROLES.PRIEST:
    case "support":
    case "healer":
      return UNIT_COMBAT_ROLES.PRIEST;
    case UNIT_COMBAT_ROLES.SWORD:
    case "melee":
    case "hero":
      return UNIT_COMBAT_ROLES.SWORD;
    default:
      return null;
  }
}

function resolveUnitCombatRole(unit) {
  const explicitRole = normalizeCombatRole(unit && unit.combatRole);
  if (explicitRole)
    return explicitRole;

  const sourceTokens = [
    unit && unit.heroKey,
    unit && unit.rosterKey,
    unit && unit.role,
    unit && unit.archetypeKey,
    unit && unit.unitTypeKey,
    unit && unit.type,
  ]
    .filter(Boolean)
    .map((value) => String(value).trim().toLowerCase());
  const joined = sourceTokens.join("|");
  if (joined.includes("shield") || joined.includes("guardian"))
    return UNIT_COMBAT_ROLES.SHIELD;
  if (joined.includes("spear") || joined.includes("polearm") || joined.includes("halber") || joined.includes("lancer"))
    return UNIT_COMBAT_ROLES.SPEAR;
  if (joined.includes("priest") || joined.includes("cleric") || joined.includes("bishop") || joined.includes("support"))
    return UNIT_COMBAT_ROLES.PRIEST;
  if (joined.includes("mage") || joined.includes("wizard") || joined.includes("arcane") || joined.includes("thaum"))
    return UNIT_COMBAT_ROLES.MAGE;
  if (joined.includes("archer") || joined.includes("crossbow") || joined.includes("ranger") || joined.includes("ranged"))
    return UNIT_COMBAT_ROLES.ARCHER;
  return UNIT_COMBAT_ROLES.SWORD;
}

function resolveAnchorHoldDepthBias(combatRole) {
  switch (normalizeCombatRole(combatRole)) {
    case UNIT_COMBAT_ROLES.SHIELD:
      return -0.6;
    case UNIT_COMBAT_ROLES.SWORD:
      return -0.25;
    case UNIT_COMBAT_ROLES.SPEAR:
      return 0.1;
    case UNIT_COMBAT_ROLES.ARCHER:
    case UNIT_COMBAT_ROLES.MAGE:
      return 0.45;
    case UNIT_COMBAT_ROLES.PRIEST:
      return 0.8;
    default:
      return 0;
  }
}

function buildLaneAnchorSlot(anchorState, unit, slotIndex, unitCount) {
  const safeSlotIndex = Math.max(0, Math.floor(Number(slotIndex) || 0));
  const columns = Math.max(3, Math.min(9, Math.max(resolveLaneAnchorColumns(unitCount), 3)));
  const row = Math.floor(safeSlotIndex / columns);
  const column = safeSlotIndex % columns;
  const lateralIndex = resolveCenteredSlotOffset(column, columns);
  const lateralDistance = lateralIndex * ROUTE_SLOT_COLUMN_SPACING * 1.15;
  const depthDistance = (row * ROUTE_SLOT_ROW_SPACING * 0.78) + resolveAnchorHoldDepthBias(resolveUnitCombatRole(unit));
  const anchorX = Number(anchorState && anchorState.anchorX) || 0;
  const anchorY = Number(anchorState && anchorState.anchorY) || 0;
  const facing = anchorState && anchorState.facing ? anchorState.facing : { x: 0, y: -1 };
  const lateral = anchorState && anchorState.lateral ? anchorState.lateral : perpendicular2D(facing);
  return {
    slotIndex: safeSlotIndex,
    row,
    column,
    x: anchorX + (lateral.x * lateralDistance) - (facing.x * depthDistance),
    y: anchorY + (lateral.y * lateralDistance) - (facing.y * depthDistance),
  };
}

function computeLaneAnchorHoldRadius(anchorState, anchorSlots) {
  const slots = Array.isArray(anchorSlots) ? anchorSlots : [];
  const centerX = Number(anchorState && anchorState.anchorX) || 0;
  const centerY = Number(anchorState && anchorState.anchorY) || 0;
  let maxDistance = ROUTE_SLOT_COLUMN_SPACING;
  for (const slot of slots) {
    const dx = (Number(slot && slot.x) || 0) - centerX;
    const dy = (Number(slot && slot.y) || 0) - centerY;
    maxDistance = Math.max(maxDistance, Math.sqrt((dx * dx) + (dy * dy)));
  }
  return maxDistance + ROUTE_SLOT_ROW_SPACING;
}

function resolveUnitAnchorLeashRadius(unit, anchorHoldRadius) {
  const combatRole = resolveUnitCombatRole(unit);
  const baseRadius = Math.max(anchorHoldRadius, ROUTE_SLOT_ROW_SPACING);
  switch (combatRole) {
    case UNIT_COMBAT_ROLES.PRIEST:
      return Math.max(baseRadius + 0.75, 3.25);
    case UNIT_COMBAT_ROLES.ARCHER:
    case UNIT_COMBAT_ROLES.MAGE:
      return Math.max(baseRadius + 1.25, 4.0);
    default:
      return Math.max(baseRadius + 2.0, 5.0);
  }
}

function buildAnchorWaypointTarget(anchorState) {
  return anchorState
    ? {
        kind: anchorState.anchorKind || null,
        laneIndex: Number.isInteger(anchorState.containerLaneIndex) ? anchorState.containerLaneIndex : null,
        x: Number(anchorState.anchorX) || 0,
        y: Number(anchorState.anchorY) || 0,
      }
    : null;
}

function shouldKeepUnitAfterLaneDefeat(lane, unit) {
  if (!unit)
    return false;
  if (unit.isDefender)
    return false;
  if (isScheduledWaveUnit(unit))
    return false;
  return Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0 && unit.sourceLaneIndex !== lane.laneIndex;
}

function laneHasOccupyingForces(lane) {
  if (!lane)
    return false;
  const activeUnits = Array.isArray(lane.units)
    && lane.units.some((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit) && unit.hp > 0);
  if (activeUnits)
    return true;
  return Array.isArray(lane.spawnQueue)
    && lane.spawnQueue.some((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit));
}

function requeueLaneControlledUnit(targetLane, unit) {
  if (!targetLane || !unit)
    return false;
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  unit.targetLaneIndex = targetLane.laneIndex;
  unit.laneId = targetLane.laneIndex;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.assignedSlotIndex = null;
  unit.stance = null;
  unit.pathContractType = null;
  unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  unit.routeType = null;
  unit.routeStartNode = null;
  unit.routeTargetNode = null;
  unit.routeSegments = null;
  unit.routeSegmentIndex = 0;
  unit.segmentProgress = 0;
  unit.currentSegment = null;
  unit.routeWorldX = null;
  unit.routeWorldY = null;
  unit.routeLateralOffset = 0;
  unit.routeLongitudinalOffset = 0;
  unit.spawnIndex = Math.max(0, targetLane.spawnQueue.length);
  unit.spawnLogicalPos = resolveSpawnLogicalPosition(spawnSourceType, unit.spawnIndex);
  targetLane.spawnQueue.push(unit);
  return true;
}

function syncLaneCommandAssignments(game) {
  if (!game || !Array.isArray(game.lanes))
    return;

  normalizeLegacyDefenderUnits(game);

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    lane.commandSlots = [];
    lane.assignedUnits = [];
    lane.assignedUnitOrder = Array.isArray(lane.assignedUnitOrder)
      ? lane.assignedUnitOrder
      : [];
    lane.insideGateAnchor = null;
    lane.outsideGateAnchor = null;
    lane.enemyCoreAnchor = null;
  }

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (let unitIndex = lane.units.length - 1; unitIndex >= 0; unitIndex -= 1) {
      const unit = lane.units[unitIndex];
      if (!isLaneControlledUnit(unit))
        continue;

      const ownerLane = getLaneCommandOwnerLane(game, unit);
      if (!ownerLane)
        continue;

      const targetLaneIndex = resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit);
      const targetLane = getLaneByIndex(game, targetLaneIndex) || lane;
      const routeObjectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, ownerLane);
      unit.targetLaneIndex = targetLane.laneIndex;
      unit.laneId = targetLane.laneIndex;
      unit.objectiveLaneIndex = routeObjectiveLaneIndex;

      const currentPathId = buildRoutePathId(unit.routeSegments);
      const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
      const sourceBarracksId = resolveUnitSourceBarracksId(unit);
      const sourceRouteNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksId);
      const sourceCoreNodeId = getLaneNodeId(sourceLaneIndex);
      const expectedSpawnEntrySegment = sourceRouteNodeId && sourceCoreNodeId
        ? `${sourceRouteNodeId}_${sourceCoreNodeId}`
        : null;
      const currentRouteUsesBattlefieldCore = !!(
        Array.isArray(unit.routeSegments)
        && unit.routeSegments.length > 0
        && expectedSpawnEntrySegment
        && unit.routeSegments[0] !== expectedSpawnEntrySegment
      );
      const desiredTargetNodeId = getLaneNodeId(routeObjectiveLaneIndex);

      let routeContract = null;
      if (lane === targetLane
          && currentRouteUsesBattlefieldCore
          && desiredTargetNodeId
          && unit.routeTargetNode === desiredTargetNodeId
          && currentPathId) {
        routeContract = {
          ok: true,
          pathId: currentPathId,
          objectiveLaneIndex: routeObjectiveLaneIndex,
          routeSegments: unit.routeSegments,
        };
      } else {
        routeContract = resolveRouteContractForUnit(game, targetLane, unit);
      }
      const desiredPathId = routeContract && routeContract.ok ? routeContract.pathId : null;
      if (routeContract && routeContract.ok
          && Array.isArray(unit.routeSegments) && unit.routeSegments.length > 0
          && (lane !== targetLane || desiredPathId !== currentPathId || unit.objectiveLaneIndex !== routeObjectiveLaneIndex)) {
        routeContract = resolveRedirectRouteContractForExistingLaneControlledUnit(game, lane, targetLane, unit);
      }
      const resolvedPathId = routeContract && routeContract.ok ? routeContract.pathId : null;
      if (routeContract && routeContract.ok
          && (lane !== targetLane || resolvedPathId !== currentPathId || unit.objectiveLaneIndex !== routeObjectiveLaneIndex)) {
        applyRouteContractToExistingUnit(unit, routeContract, {
          x: Number(unit.posX),
          y: Number(unit.posY),
        });
      }

      if (lane !== targetLane) {
        lane.units.splice(unitIndex, 1);
        targetLane.units.push(unit);
      }
      applyCanonicalUnitMirrors(game, targetLane, unit);
    }

    if (!Array.isArray(lane.spawnQueue))
      continue;

    for (let queueIndex = lane.spawnQueue.length - 1; queueIndex >= 0; queueIndex -= 1) {
      const unit = lane.spawnQueue[queueIndex];
      if (!isLaneControlledUnit(unit))
        continue;

      const ownerLane = getLaneCommandOwnerLane(game, unit);
      if (!ownerLane)
        continue;

      const targetLaneIndex = resolveLaneCommandContainerLaneIndex(game, ownerLane);
      const targetLane = getLaneByIndex(game, targetLaneIndex) || lane;
      unit.targetLaneIndex = targetLane.laneIndex;
      unit.laneId = targetLane.laneIndex;
      unit.objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, ownerLane);

      if (lane !== targetLane) {
        lane.spawnQueue.splice(queueIndex, 1);
        requeueLaneControlledUnit(targetLane, unit);
      } else {
        unit.spawnIndex = queueIndex;
        unit.spawnLogicalPos = resolveSpawnLogicalPosition(resolveSpawnSourceTypeFromUnit(unit), queueIndex);
      }
      applyCanonicalUnitMirrors(game, targetLane, unit);
    }
  }

  for (const ownerLane of game.lanes) {
    if (!ownerLane)
      continue;

    const anchorState = sampleLaneCommandAnchor(game, ownerLane);
    ownerLane.combatEnabled = !!(anchorState && anchorState.combatEnabled);
    ownerLane.engagementRadius = anchorState ? anchorState.engagementRadius : 0;
    ownerLane.commandAnchorProgress = anchorState ? anchorState.anchorProgress : 0;
    ownerLane.commandAnchor = anchorState
      ? { x: anchorState.anchorX, y: anchorState.anchorY, laneIndex: anchorState.containerLaneIndex }
      : null;
    ownerLane.commandFacing = anchorState
      ? { x: anchorState.facing.x, y: anchorState.facing.y }
      : null;
    ownerLane.insideGateAnchor = anchorState && anchorState.insideGateAnchor
      ? { x: anchorState.insideGateAnchor.x, y: anchorState.insideGateAnchor.y }
      : null;
    ownerLane.outsideGateAnchor = anchorState && anchorState.outsideGateAnchor
      ? { x: anchorState.outsideGateAnchor.x, y: anchorState.outsideGateAnchor.y }
      : null;
    ownerLane.enemyCoreAnchor = anchorState && anchorState.enemyCoreAnchor
      ? { x: anchorState.enemyCoreAnchor.x, y: anchorState.enemyCoreAnchor.y }
      : null;

    const orderedEntries = [];
    for (const lane of game.lanes) {
      for (const unit of lane.units || []) {
        if (!isLaneControlledUnit(unit) || unit.sourceLaneIndex !== ownerLane.laneIndex || unit.hp <= 0)
          continue;

        unit.combatRole = resolveUnitCombatRole(unit);
        orderedEntries.push({ lane, unit });
      }
    }

    orderedEntries.sort((left, right) => resolveLaneControlledUnitSortKey(left.unit).localeCompare(resolveLaneControlledUnitSortKey(right.unit)));

    const currentWaypointTarget = buildAnchorWaypointTarget(anchorState);
    const commandSlots = orderedEntries.map((entry, unitIndex) => {
      const slot = buildLaneAnchorSlot(anchorState, entry.unit, unitIndex, orderedEntries.length);
      return {
        slotIndex: unitIndex,
        x: Number(slot.x.toFixed(3)),
        y: Number(slot.y.toFixed(3)),
        unitId: entry.unit.id,
      };
    });
    const anchorHoldRadius = computeLaneAnchorHoldRadius(anchorState, commandSlots);
    const anchorCenterX = anchorState ? Number(anchorState.anchorX) : 0;
    const anchorCenterY = anchorState ? Number(anchorState.anchorY) : 0;
    const anchorFacing = anchorState ? anchorState.facing : { x: 0, y: -1 };
    const anchorLateral = anchorState ? anchorState.lateral : { x: 1, y: 0 };

    for (let unitIndex = 0; unitIndex < orderedEntries.length; unitIndex += 1) {
      const entry = orderedEntries[unitIndex];
      const slot = commandSlots[unitIndex];
      const anchorLeashRadius = resolveUnitAnchorLeashRadius(entry.unit, anchorHoldRadius);
      entry.unit.assignedSlotIndex = unitIndex;
      entry.unit.anchorTargetX = slot.x;
      entry.unit.anchorTargetY = slot.y;
      entry.unit.anchorTargetProgress = anchorState ? anchorState.anchorProgress : 0;
      entry.unit.anchorFacingX = anchorFacing.x;
      entry.unit.anchorFacingY = anchorFacing.y;
      entry.unit.anchorLateralX = anchorLateral.x;
      entry.unit.anchorLateralY = anchorLateral.y;
      entry.unit.commandState = anchorState ? anchorState.commandState : getLaneCommandState(ownerLane);
      entry.unit.anchorCenterX = anchorCenterX;
      entry.unit.anchorCenterY = anchorCenterY;
      entry.unit.anchorHoldRadius = anchorHoldRadius;
      entry.unit.anchorLeashRadius = anchorLeashRadius;
      entry.unit.currentWaypointTargetX = currentWaypointTarget ? currentWaypointTarget.x : null;
      entry.unit.currentWaypointTargetY = currentWaypointTarget ? currentWaypointTarget.y : null;
      entry.unit.currentWaypointTargetKind = currentWaypointTarget ? currentWaypointTarget.kind : null;
      entry.unit.canEngage = !!(anchorState && anchorState.combatEnabled);
      entry.unit.combatLeashRadius = anchorState
        ? Math.max(anchorState.engagementRadius, anchorLeashRadius)
        : anchorLeashRadius;
      ownerLane.assignedUnits.push(entry.unit.id);
      ownerLane.commandSlots.push({
        slotIndex: ownerLane.commandSlots.length,
        x: Number(slot.x.toFixed(3)),
        y: Number(slot.y.toFixed(3)),
        unitId: entry.unit.id,
      });
      applyCanonicalUnitMirrors(game, entry.lane, entry.unit);
    }

    ownerLane.assignedUnitOrder = ownerLane.assignedUnits.slice();
  }
}

// Unit and tower definitions are DB-driven via unitTypes.js.
// These empty objects are kept for backward-compat exports only.
const UNIT_DEFS  = {};
const TOWER_DEFS = {};

// Fortress, barracks, and market catalogs now live in server/game/multilane/*System.js.

// ── Phase G: Ability system helpers ───────────────────────────────────────────

function resolveFortPresentationConfig(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY, fallbackDisplayName = null) {
  const resolvedPresentationKey = presentationKey || DEFAULT_FORT_PRESENTATION_KEY;
  const catalogUnitKey = resolveFortCatalogUnitKey(archetypeKey, resolvedPresentationKey);
  const skinKey = resolveFortSkinKey(archetypeKey, resolvedPresentationKey);
  const portraitKey = resolveFortPortraitKey(archetypeKey, resolvedPresentationKey);
  const displayName = resolveFortDisplayName(archetypeKey, resolvedPresentationKey, fallbackDisplayName);
  return {
    archetypeKey,
    presentationKey: resolvedPresentationKey,
    catalogUnitKey,
    skinKey,
    portraitKey,
    displayName,
  };
}

function resolveGameplayCatalogUnitKey(unitKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  if (!isFortArchetypeKey(unitKey))
    return unitKey;

  return resolveFortCatalogUnitKey(unitKey, presentationKey) || unitKey;
}

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
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(unitTypeKey);
  const ut = getUnitType(resolvedUnitTypeKey);
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
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(key);
  const ut = getUnitType(resolvedUnitTypeKey);
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

function resolveUnitSupportProfile(unit) {
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(
    unit && (unit.type || unit.unitTypeKey || unit.key)
  );
  const ut = getUnitType(resolvedUnitTypeKey);
  const sp = (ut && ut.special_props && typeof ut.special_props === "object")
    ? ut.special_props
    : {};
  const supportRoleRaw = sp.supportRole != null ? sp.supportRole : sp.support_role;
  const supportRole = typeof supportRoleRaw === "string"
    ? supportRoleRaw.trim().toLowerCase()
    : null;
  const isHealer = supportRole === "healer";
  const healAmountRaw = sp.healAmount != null ? sp.healAmount : sp.heal_amount;
  return {
    role: supportRole,
    isHealer,
    healAmount: isHealer ? Math.max(1, Number(healAmountRaw) || 1) : 0,
  };
}

/**
 * Resolve a tower definition from the DB (authoritative).
 * DB range is stored normalised to [0,1] × GRID_W.
 */
function resolveTowerDef(key) {
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(key);
  const ut = getUnitType(resolvedUnitTypeKey);
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
  return barracksSystem.getBarracksLevelDef(level);
}

function getBarracksSpeedMultForLevel(level) {
  return barracksSystem.getBarracksSpeedMultForLevel(level);
}

function getBarracksSpeedMult(_br) {
  return barracksSystem.getBarracksSpeedMult(_br);
}

function getLaneWaveSpeedMult(lane) {
  if (!lane || !Number.isFinite(Number(lane.waveSpeedMult)))
    return 1;
  return Math.max(0.01, Number(lane.waveSpeedMult));
}

function getBaseCombatPathSpeed(_unitTypeKey) {
  return BASE_COMBAT_PATH_SPEED;
}

function normalizeAllegianceKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case "red":
      return ALLEGIANCE_KEYS.RED;
    case "gold":
    case "yellow":
      return ALLEGIANCE_KEYS.YELLOW;
    case "blue":
      return ALLEGIANCE_KEYS.BLUE;
    case "green":
      return ALLEGIANCE_KEYS.GREEN;
    case "dungeon":
      return ALLEGIANCE_KEYS.DUNGEON;
    default:
      return null;
  }
}

function normalizeSpawnSourceType(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case "scheduled_wave":
    case "dungeon_wave":
      return SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
    case SPAWN_SOURCE_TYPES.BARRACKS_ROSTER:
      return SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;
    case SPAWN_SOURCE_TYPES.BARRACKS_HERO:
      return SPAWN_SOURCE_TYPES.BARRACKS_HERO;
    default:
      return null;
  }
}

function normalizeUnitStance(value) {
  const normalized = String(value || "").trim().toUpperCase();
  switch (normalized) {
    case UNIT_STANCES.DEFEND:
      return UNIT_STANCES.DEFEND;
    case UNIT_STANCES.ATTACK:
    case "ADVANCE":
      return UNIT_STANCES.ATTACK;
    case UNIT_STANCES.HOLD:
      return UNIT_STANCES.HOLD;
    case "RETREAT":
    case UNIT_STANCES.RETREAT:
      return UNIT_STANCES.RETREAT;
    default:
      return null;
  }
}

function isPlayerAllegianceKey(allegianceKey) {
  return PLAYER_ALLEGIANCE_KEYS.has(normalizeAllegianceKey(allegianceKey));
}

function areAllegiancesHostile(a, b) {
  const left = normalizeAllegianceKey(a);
  const right = normalizeAllegianceKey(b);
  if (!left || !right)
    return false;
  if (left === right)
    return false;
  if (left === ALLEGIANCE_KEYS.DUNGEON || right === ALLEGIANCE_KEYS.DUNGEON)
    return true;
  return isPlayerAllegianceKey(left) && isPlayerAllegianceKey(right);
}

function resolveLaneAllegianceKey(lane) {
  if (!lane)
    return null;
  return normalizeAllegianceKey(lane.allegianceKey || lane.team || lane.slotColor);
}

function getSourceLane(game, sourceLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || !Number.isInteger(sourceLaneIndex))
    return null;
  if (sourceLaneIndex < 0 || sourceLaneIndex >= game.lanes.length)
    return null;
  return game.lanes[sourceLaneIndex] || null;
}

function resolveSpawnSourceTypeFromWaveDef(waveDef) {
  const explicitSourceType = normalizeSpawnSourceType(waveDef && waveDef.spawnSourceType);
  if (explicitSourceType)
    return explicitSourceType;

  if (waveDef && waveDef.isHero)
    return SPAWN_SOURCE_TYPES.BARRACKS_HERO;

  const sourceBarracksId = resolveUnitSourceBarracksId(waveDef);
  if (waveDef && (sourceBarracksId
      || Number.isInteger(waveDef.sourceLaneIndex) && waveDef.sourceLaneIndex >= 0))
    return SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;

  if (waveDef && waveDef.isWaveUnit)
    return SPAWN_SOURCE_TYPES.DUNGEON_WAVE;

  return SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
}

function resolveSpawnSourceTypeFromUnit(unit) {
  const explicitSourceType = normalizeSpawnSourceType(unit && unit.spawnSourceType);
  if (explicitSourceType)
    return explicitSourceType;

  if (unit && unit.isDefender)
    return unit.isHero ? SPAWN_SOURCE_TYPES.BARRACKS_HERO : SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;

  return resolveSpawnSourceTypeFromWaveDef(unit);
}

function isScheduledWaveUnit(unit) {
  return resolveSpawnSourceTypeFromUnit(unit) === SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
}

function resolveUnitSourceBarracksId(unit) {
  return normalizeBarracksSiteId(unit && (unit.sourceBarracksId || unit.sourceBarracksKey || unit.barracksId));
}

function resolveUnitTargetLaneIndex(game, fallbackLane, unit) {
  if (Number.isInteger(unit && unit.targetLaneIndex))
    return unit.targetLaneIndex;
  if (Number.isInteger(unit && unit.laneId))
    return unit.laneId;
  if (fallbackLane && Number.isInteger(fallbackLane.laneIndex))
    return fallbackLane.laneIndex;
  if (Number.isInteger(unit && unit.ownerLaneIndex))
    return unit.ownerLaneIndex;
  return -1;
}

function resolveUnitObjectiveLaneIndex(game, fallbackLane, unit) {
  if (Number.isInteger(unit && unit.objectiveLaneIndex))
    return unit.objectiveLaneIndex;

  if (isLaneControlledUnit(unit)) {
    const sourceLane = getSourceLane(game, Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1);
    const objectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, sourceLane);
    if (Number.isInteger(objectiveLaneIndex))
      return objectiveLaneIndex;
  }

  return resolveUnitTargetLaneIndex(game, fallbackLane, unit);
}

function resolveUnitOwnerLaneIndex(game, fallbackLane, unit) {
  if (Number.isInteger(unit && unit.ownerLaneIndex))
    return unit.ownerLaneIndex;
  if (Number.isInteger(unit && unit.ownerLane))
    return unit.ownerLane;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return -1;

  if (Number.isInteger(unit && unit.sourceLaneIndex))
    return unit.sourceLaneIndex;

  if (unit && unit.isDefender && fallbackLane && Number.isInteger(fallbackLane.laneIndex))
    return fallbackLane.laneIndex;

  return -1;
}

function resolveUnitStance(game, fallbackLane, unit) {
  if (isLaneControlledUnit(unit)) {
    const commandState = getLaneCommandStateForUnit(game, unit);
    if (commandState === LANE_COMMAND_STATES.RETREAT)
      return UNIT_STANCES.RETREAT;
    if (commandState === LANE_COMMAND_STATES.DEFEND)
      return UNIT_STANCES.DEFEND;
    return UNIT_STANCES.ATTACK;
  }

  const explicitStance = normalizeUnitStance(unit && unit.stance);
  if (explicitStance)
    return explicitStance;

  const explicitPathContractType = String(unit && unit.pathContractType || "").trim().toLowerCase();
  if (explicitPathContractType === PATH_CONTRACT_TYPES.GUARD_ANCHOR
      || explicitPathContractType === PATH_CONTRACT_TYPES.INTERCEPT)
    return UNIT_STANCES.DEFEND;

  if (unit && unit.isDefender)
    return UNIT_STANCES.DEFEND;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return UNIT_STANCES.ATTACK;

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = resolveUnitTargetLaneIndex(game, fallbackLane, unit);
  if (sourceLaneIndex >= 0 && sourceLaneIndex === targetLaneIndex)
    return UNIT_STANCES.HOLD;

  return UNIT_STANCES.ATTACK;
}

function mapRouteTypeToPathContractType(routeType, barracksId) {
  if (routeType === ROUTE_TYPES.WAVE_LANE)
    return PATH_CONTRACT_TYPES.WAVE_LANE;
  if (routeType === ROUTE_TYPES.CENTER_CROSS)
    return PATH_CONTRACT_TYPES.BARRACKS_CROSS;
  if (routeType === ROUTE_TYPES.OUTER_LOOP)
    return PATH_CONTRACT_TYPES.BARRACKS_LOOP;

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (normalizedBarracksId === "center")
    return PATH_CONTRACT_TYPES.BARRACKS_CROSS;
  if (normalizedBarracksId === "left" || normalizedBarracksId === "right")
    return PATH_CONTRACT_TYPES.BARRACKS_LOOP;
  return null;
}

function resolveUnitPathContractType(game, fallbackLane, unit) {
  if (isLaneControlledUnit(unit)) {
    const commandState = getLaneCommandStateForUnit(game, unit);
    const combatTargetId = unit && (unit.combatTargetId || unit.combatTarget && unit.combatTarget.unitId);
    if (combatTargetId && isLaneCommandCombatEnabledForUnit(game, unit))
      return PATH_CONTRACT_TYPES.INTERCEPT;
    if (commandState === LANE_COMMAND_STATES.RETREAT)
      return PATH_CONTRACT_TYPES.RETREAT_ANCHOR;
    if (commandState === LANE_COMMAND_STATES.DEFEND)
      return PATH_CONTRACT_TYPES.GUARD_ANCHOR;
    return PATH_CONTRACT_TYPES.BARRACKS_CROSS;
  }

  const stance = resolveUnitStance(game, fallbackLane, unit);
  if (stance === UNIT_STANCES.DEFEND)
    return unit && (unit.combatTargetId || unit.combatTarget && unit.combatTarget.unitId)
      ? PATH_CONTRACT_TYPES.INTERCEPT
      : PATH_CONTRACT_TYPES.GUARD_ANCHOR;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return PATH_CONTRACT_TYPES.WAVE_LANE;

  return mapRouteTypeToPathContractType(unit && unit.routeType, resolveUnitSourceBarracksId(unit));
}

function resolveUnitAllegianceKey(game, fallbackLane, unit) {
  const explicitAllegiance = normalizeAllegianceKey(unit && unit.allegianceKey);
  if (explicitAllegiance)
    return explicitAllegiance;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return ALLEGIANCE_KEYS.DUNGEON;

  const sourceTeam = normalizeAllegianceKey(unit && unit.sourceTeam);
  if (sourceTeam)
    return sourceTeam;

  const sourceLane = getSourceLane(game, Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1);
  const sourceLaneAllegiance = resolveLaneAllegianceKey(sourceLane);
  if (sourceLaneAllegiance)
    return sourceLaneAllegiance;

  const ownerLane = getSourceLane(game, resolveUnitOwnerLaneIndex(game, fallbackLane, unit));
  const ownerLaneAllegiance = resolveLaneAllegianceKey(ownerLane);
  if (ownerLaneAllegiance)
    return ownerLaneAllegiance;

  return resolveLaneAllegianceKey(fallbackLane);
}

function resolveLegacySourceTeamFromAllegianceKey(allegianceKey) {
  const canonical = normalizeAllegianceKey(allegianceKey);
  return canonical === ALLEGIANCE_KEYS.DUNGEON
    ? null
    : canonical;
}

function applyCanonicalUnitMirrors(game, fallbackLane, unit) {
  if (!unit)
    return unit;

  const unitId = unit.id || unit.unitId || null;
  const unitTypeKey = unit.type || unit.unitTypeKey || null;
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  const ownerLaneIndex = resolveUnitOwnerLaneIndex(game, fallbackLane, unit);
  const targetLaneIndex = resolveUnitTargetLaneIndex(game, fallbackLane, unit);
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, fallbackLane, unit);
  const allegianceKey = resolveUnitAllegianceKey(game, fallbackLane, unit);
  const pathContractType = resolveUnitPathContractType(game, fallbackLane, unit);
  const sourceBarracksId = resolveUnitSourceBarracksId(unit);
  const stance = resolveUnitStance(game, fallbackLane, unit);
  const combatTargetId = unit.combatTargetId || (unit.combatTarget && unit.combatTarget.unitId) || null;
  const combatRole = resolveUnitCombatRole(unit);
  const isBarracksControlledUnit = Number.isInteger(unit.sourceLaneIndex)
    && unit.sourceLaneIndex >= 0
    && (spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER
      || spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_HERO);
  const isDefenderUnit = !!unit.isDefender && !isBarracksControlledUnit;
  const canEngage = isLaneCommandCombatEnabledForUnit(game, unit);

  unit.unitId = unitId;
  unit.id = unitId;
  unit.unitTypeKey = unitTypeKey;
  unit.type = unitTypeKey;
  unit.spawnSourceType = spawnSourceType;
  unit.allegianceKey = allegianceKey;
  unit.ownerLaneIndex = ownerLaneIndex;
  unit.ownerLane = ownerLaneIndex;
  unit.targetLaneIndex = targetLaneIndex;
  unit.laneId = targetLaneIndex;
  unit.objectiveLaneIndex = objectiveLaneIndex;
  unit.sourceBarracksId = sourceBarracksId;
  unit.sourceBarracksKey = sourceBarracksId;
  unit.barracksId = sourceBarracksId;
  unit.heroKey = unit.heroKey || null;
  unit.stance = stance;
  unit.pathContractType = pathContractType;
  unit.combatRole = combatRole;
  unit.combatTargetId = combatTargetId;
  unit.currentTargetId = combatTargetId;
  unit.sourceTeam = resolveLegacySourceTeamFromAllegianceKey(allegianceKey);
  unit.isWaveUnit = spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
  unit.isDefender = isDefenderUnit;
  unit.canEngage = canEngage;
  unit.state = unit.combatState || unit.routeState || unit.state || WAVE_UNIT_STATES.IDLE;
  if (!unit.movementMode)
    unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  return unit;
}

function buildRoutePathId(routeSegments) {
  return routeGraph.buildRoutePathId(routeSegments);
}

function resolveUnitNextWaypoint(unit) {
  return routeGraph.resolveUnitNextWaypoint(unit);
}

function dot2D(a, b) {
  return routeGraph.dot2D(a, b);
}

function resolveCenteredSpawnColumn(slotIndex) {
  const centerColumn = Math.max(0, Math.min(GRID_W - 1, Math.floor(Number(SPAWN_X) || 0)));
  const safeSlotIndex = Math.max(0, Math.floor(Number(slotIndex) || 0));
  if (safeSlotIndex <= 0)
    return centerColumn;

  const step = Math.ceil(safeSlotIndex / 2);
  const offset = safeSlotIndex % 2 === 1 ? -step : step;
  return Math.max(0, Math.min(GRID_W - 1, centerColumn + offset));
}

function resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex) {
  const safeSpawnIndex = Math.max(0, Math.floor(Number(resolvedSpawnIndex) || 0));
  const row = Math.floor(safeSpawnIndex / GRID_W);
  const slotInRow = safeSpawnIndex % GRID_W;
  if (spawnType === SPAWN_SOURCE_TYPES.SCHEDULED_WAVE) {
    return {
      x: resolveCenteredSpawnColumn(slotInRow),
      y: row,
    };
  }

  return {
    x: slotInRow,
    y: row,
  };
}

function validateSpawnDefinition(game, targetLane, waveDef, options = {}) {
  const spawnType = resolveSpawnSourceTypeFromWaveDef(waveDef);
  const allowUnbuiltBarracks = !!(
    options.allowUnbuiltBarracks
    || (waveDef && waveDef.allowUnbuiltBarracks)
  );
  const requestedSpawnIndex = Number.isInteger(waveDef && waveDef.spawnIndex)
    ? waveDef.spawnIndex
    : Math.max(0, targetLane && Array.isArray(targetLane.spawnQueue) ? targetLane.spawnQueue.length : 0);
  const resolvedSpawnIndex = Math.max(0, requestedSpawnIndex);
  const logicalPos = resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex);
  const sourceLaneIndex = Number.isInteger(waveDef && waveDef.sourceLaneIndex) ? waveDef.sourceLaneIndex : -1;
  const sourceLane = getSourceLane(game, sourceLaneIndex);
  const sourceBarracksKey = normalizeBarracksSiteId(
    waveDef && (waveDef.sourceBarracksKey || waveDef.sourceBarracksId)
  );
  const sourceTeam = normalizeAllegianceKey(waveDef && waveDef.sourceTeam);

  if (!targetLane)
    return { ok: false, reason: "Missing target lane", spawnType };

  if (logicalPos.x < 0 || logicalPos.x >= GRID_W || logicalPos.y < 0)
    return { ok: false, reason: "Resolved spawn index is out of legal queue bounds", spawnType };

  if ((spawnType === "barracks_roster" || spawnType === "barracks_hero") && !sourceLane)
    return { ok: false, reason: "Spawn source lane is missing", spawnType };

  if ((spawnType === "barracks_roster" || spawnType === "barracks_hero") && !sourceBarracksKey)
    return { ok: false, reason: "Spawn source barracks id is missing", spawnType };

  if ((spawnType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER || spawnType === SPAWN_SOURCE_TYPES.BARRACKS_HERO)
      && sourceLane && sourceTeam && resolveLaneAllegianceKey(sourceLane) !== sourceTeam)
    return { ok: false, reason: "Spawn source team does not match source lane ownership", spawnType };

  if ((spawnType === "barracks_roster" || spawnType === "barracks_hero") && sourceLane && !allowUnbuiltBarracks) {
    const descriptor = describeBarracksSite(game, sourceLane, sourceBarracksKey);
    if (!descriptor || !descriptor.isBuilt)
      return { ok: false, reason: "Spawn source barracks does not exist or is not built on the source lane", spawnType };
  }

  if (spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE && !getWaveSpawnWorldPosition(targetLane.laneIndex))
    return { ok: false, reason: "Wave spawn origin is missing for the target lane", spawnType };

  return {
    ok: true,
    spawnType,
    sourceLaneIndex,
    sourceTeam,
    sourceBarracksKey,
    requestedSpawnIndex,
    resolvedSpawnIndex,
    logicalPos,
  };
}

function getEffectiveWaveEntrySpeedMult(game, lane, waveDef) {
  const safeWaveDef = waveDef && typeof waveDef === "object" ? waveDef : {};
  const authoredSpeedMult = Math.max(0.01, Number(safeWaveDef.speed_mult || 1));
  const sourceLane = getSourceLane(game, safeWaveDef.sourceLaneIndex);
  if (sourceLane)
    return authoredSpeedMult * getBarracksSpeedMult(sourceLane.barracks);
  return authoredSpeedMult * getLaneWaveSpeedMult(lane);
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

function getFortressMaxTier(buildingType) {
  return fortressSystem.getFortressMaxTier(buildingType);
}

function createFortressPadStates(teamHpStart) {
  return fortressSystem.createFortressPadStates(teamHpStart);
}

function createBarracksRosterCounts() {
  return barracksSystem.createBarracksRosterCounts();
}

function createBarracksSiteRosterCounts() {
  return barracksSystem.createBarracksSiteRosterCounts();
}

function getBarracksSiteDef(barracksId) {
  return barracksSystem.getBarracksSiteDef(barracksId);
}

function normalizeBarracksSiteId(value) {
  return barracksSystem.normalizeBarracksSiteId(value);
}

function summarizeBarracksSiteCounts(siteCounts) {
  return barracksSystem.summarizeBarracksSiteCounts(siteCounts);
}

function summarizeBarracksSiteRosterEntries(rosterEntries) {
  return barracksSystem.summarizeBarracksSiteRosterEntries(rosterEntries);
}

function logBarracksRosterState(lane, reason) {
  return barracksSystem.logBarracksRosterState(lane, reason);
}

function getBarracksSiteCounts(lane, barracksId) {
  return barracksSystem.getBarracksSiteCounts(lane, barracksId);
}

function hasOwnedBarracksUnits(lane, barracksId) {
  return barracksSystem.hasOwnedBarracksUnits(lane, barracksId);
}

function getBarracksSiteTierRequirement(siteDef) {
  return barracksSystem.getBarracksSiteTierRequirement(siteDef);
}

function getBarracksSiteBuildCost(siteDef) {
  return barracksSystem.getBarracksSiteBuildCost(siteDef);
}

function getBarracksSiteMaxLevel() {
  return barracksSystem.getBarracksSiteMaxLevel();
}

function normalizeBarracksSiteLevel(level) {
  return barracksSystem.normalizeBarracksSiteLevel(level);
}

function getBarracksSiteBaseMaxHp() {
  return barracksSystem.getBarracksSiteBaseMaxHp();
}

function getBarracksSiteSendIntervalTicks(level) {
  return barracksSystem.getBarracksSiteSendIntervalTicks(level);
}

function createBarracksSiteState(siteDef, options = {}) {
  return barracksSystem.createBarracksSiteState(siteDef, options);
}

function createBarracksSiteStates(_teamHpStart, legacyBarracksLevel = 1) {
  return barracksSystem.createBarracksSiteStates(_teamHpStart, legacyBarracksLevel);
}

function syncLegacyBarracksAggregate(lane) {
  return barracksSystem.syncLegacyBarracksAggregate(lane);
}

function ensureBarracksSiteStates(lane, game) {
  return barracksSystem.ensureBarracksSiteStates(lane, game);
}

function getBarracksSiteState(lane, barracksId, game) {
  return barracksSystem.getBarracksSiteState(lane, barracksId, game);
}

function describeBarracksSite(game, lane, barracksId) {
  return barracksSystem.describeBarracksSite(game, lane, barracksId);
}

function getBarracksSiteLockedReason(siteDef) {
  return barracksSystem.getBarracksSiteLockedReason(siteDef);
}

function isBarracksSiteAvailable(lane, barracksId) {
  return barracksSystem.isBarracksSiteAvailable(lane, barracksId);
}

function getBarracksRosterLockedReason(rosterDef) {
  return barracksSystem.getBarracksRosterLockedReason(rosterDef);
}

function getBuiltBarracksSiteTier(lane, barracksId) {
  return barracksSystem.getBuiltBarracksSiteTier(lane, barracksId);
}

function getHighestBuiltBarracksSiteTier(lane) {
  return barracksSystem.getHighestBuiltBarracksSiteTier(lane);
}

function resolveBarracksRosterUnlockContext(lane, rosterDef, barracksId = null) {
  return barracksSystem.resolveBarracksRosterUnlockContext(lane, rosterDef, barracksId);
}

function getHeroRosterDefinition(heroKey) {
  return barracksSystem.getHeroRosterDefinition(heroKey);
}

function getHeroRosterLockedReason(heroDef) {
  return barracksSystem.getHeroRosterLockedReason(heroDef);
}

function getFortressPadState(lane, padId) {
  return fortressSystem.getFortressPadState(lane, padId);
}

function getFortressPadByBuildingType(lane, buildingType) {
  return fortressSystem.getFortressPadByBuildingType(lane, buildingType);
}

function getHighestBuiltFortressPadTier(lane, buildingType) {
  return fortressSystem.getHighestBuiltFortressPadTier(lane, buildingType);
}

function getTownCoreTier(lane) {
  return fortressSystem.getTownCoreTier(lane);
}

function getLaneTownCorePad(lane) {
  return fortressSystem.getLaneTownCorePad(lane);
}

function getLaneTownCoreHp(lane) {
  return fortressSystem.getLaneTownCoreHp(lane);
}

function getLaneTownCoreMaxHp(lane) {
  return fortressSystem.getLaneTownCoreMaxHp(lane);
}

const FORTRESS_SYSTEM_DEPS = Object.freeze({
  getLaneCombatAxes,
  getPadWorldPosition,
  isScheduledWaveUnit,
  shouldKeepUnitAfterLaneDefeat,
  resolveLaneAllegianceKey,
  log,
  getBarracksUpgradeCost(nextTier) {
    const nextDef = getBarracksUpgradeDef(nextTier);
    if (nextDef && Number.isFinite(nextDef.upgradeCost))
      return Math.max(0, Math.floor(Number(nextDef.upgradeCost)));

    const fallback = getBarracksLevelDef(nextTier);
    return Math.max(0, Math.floor(Number(fallback.cost) || 0));
  },
});

const BARRACKS_SYSTEM_DEPS = Object.freeze({
  log,
  resolveFortPresentationConfig,
  getUnitType,
  resolveTowerDef,
  resolveUnitDef,
  resolveLaneAllegianceKey,
  getBarracksSiteWorldPosition,
  resolveTargetLaneForBarracksSend,
  normalizeCombatRole,
  resolveUnitCombatRole,
  spawnWaveUnit: _spawnWaveUnit,
  gridWidth: GRID_W,
  defaultHeroCombatRole: UNIT_COMBAT_ROLES.SWORD,
  spawnSourceTypes: SPAWN_SOURCE_TYPES,
});

function buildTownCoreStateSummary(game) {
  return fortressSystem.buildTownCoreStateSummary(game, FORTRESS_SYSTEM_DEPS);
}

function getFortressPadCombatTarget(lane, padState, positionOverride = null) {
  return fortressSystem.getFortressPadCombatTarget(lane, padState, positionOverride, FORTRESS_SYSTEM_DEPS);
}

function getBarracksSiteCombatTarget(lane, barracksId) {
  return barracksSystem.getBarracksSiteCombatTarget(lane, barracksId, BARRACKS_SYSTEM_DEPS);
}

function getLaneTownCoreCombatTarget(lane) {
  return fortressSystem.getLaneTownCoreCombatTarget(lane, FORTRESS_SYSTEM_DEPS);
}

function getBuildingTierDisplayName(buildingType, tier) {
  return fortressSystem.getBuildingTierDisplayName(buildingType, tier);
}

function getBuildingBranchLabel(buildingType) {
  return fortressSystem.getBuildingBranchLabel(buildingType);
}

function getBuildingDisplayName(buildingType) {
  return fortressSystem.getBuildingDisplayName(buildingType);
}

function isHeroUnlockedForLane(lane, heroDef) {
  return barracksSystem.isHeroUnlockedForLane(lane, heroDef);
}

function getHeroDisabledReason(game, lane, heroDef) {
  return barracksSystem.getHeroDisabledReason(game, lane, heroDef, BARRACKS_SYSTEM_DEPS);
}

function createHeroRosterSnapshot(game, lane) {
  return barracksSystem.createHeroRosterSnapshot(game, lane, BARRACKS_SYSTEM_DEPS);
}

function getHeroCooldownReadyTick(lane, heroKey) {
  return barracksSystem.getHeroCooldownReadyTick(lane, heroKey);
}

function countActiveHeroDeployments(game, sourceLaneIndex, heroKey) {
  return barracksSystem.countActiveHeroDeployments(game, sourceLaneIndex, heroKey);
}

function countBuiltBarracksSites(lane) {
  return barracksSystem.countBuiltBarracksSites(lane);
}

function markLaneDefeated(game, lane, defeatContext = null) {
  return fortressSystem.markLaneDefeated(game, lane, defeatContext, FORTRESS_SYSTEM_DEPS);
}

function recomputeTeamHpState(game) {
  return fortressSystem.recomputeTeamHpState(game, FORTRESS_SYSTEM_DEPS);
}

function applyFortressPadDamage(game, lane, padId, damage) {
  return fortressSystem.applyFortressPadDamage(game, lane, padId, damage, FORTRESS_SYSTEM_DEPS);
}

function resolveFortressBuildingMaxHp(buildingType, tier, teamHpStart) {
  return fortressSystem.resolveFortressBuildingMaxHp(buildingType, tier, teamHpStart);
}

function getFortressRequiredTownCoreTier(buildingType, targetTier) {
  return fortressSystem.getFortressRequiredTownCoreTier(buildingType, targetTier);
}

function getFortressDependencyRequirements(buildingType, targetTier) {
  return fortressSystem.getFortressDependencyRequirements(buildingType, targetTier);
}

function getFortressDependencyLockedReason(lane, buildingType, targetTier) {
  return fortressSystem.getFortressDependencyLockedReason(lane, buildingType, targetTier);
}

function getFortressBuildCost(buildingType) {
  return fortressSystem.getFortressBuildCost(buildingType);
}

function getFortressUpgradeCost(buildingType, nextTier) {
  return fortressSystem.getFortressUpgradeCost(buildingType, nextTier, FORTRESS_SYSTEM_DEPS);
}

function describeFortressPad(game, lane, padState) {
  return fortressSystem.describeFortressPad(game, lane, padState, FORTRESS_SYSTEM_DEPS);
}

function getBarracksRosterDefinition(rosterKey) {
  return barracksSystem.getBarracksRosterDefinition(rosterKey);
}

function getBarracksRosterBuyCost(rosterDef) {
  return barracksSystem.getBarracksRosterBuyCost(rosterDef, BARRACKS_SYSTEM_DEPS);
}

function getBarracksRosterSellRefund(rosterDef) {
  return barracksSystem.getBarracksRosterSellRefund(rosterDef, BARRACKS_SYSTEM_DEPS);
}

function getBarracksRoleSortIndex(role) {
  return barracksSystem.getBarracksRoleSortIndex(role);
}

function getBarracksSpawnQueueRoleSortIndex(role) {
  return barracksSystem.getBarracksSpawnQueueRoleSortIndex(role);
}

function createBarracksSiteRosterSnapshot(game, lane, barracksId) {
  return barracksSystem.createBarracksSiteRosterSnapshot(game, lane, barracksId, BARRACKS_SYSTEM_DEPS);
}

function createBarracksSiteSnapshot(game, lane, barracksId) {
  return barracksSystem.createBarracksSiteSnapshot(game, lane, barracksId, BARRACKS_SYSTEM_DEPS);
}

function createBarracksRosterSnapshot(game, lane) {
  return barracksSystem.createBarracksRosterSnapshot(game, lane, BARRACKS_SYSTEM_DEPS);
}

function createFortressPadSnapshot(game, lane, padState) {
  return fortressSystem.createFortressPadSnapshot(game, lane, padState, FORTRESS_SYSTEM_DEPS);
}

function buildBarracksRosterSpawnEntries(game, lane, barracksId = null) {
  return barracksSystem.buildBarracksRosterSpawnEntries(game, lane, barracksId, BARRACKS_SYSTEM_DEPS);
}

function spawnScheduledBarracksRoster(game, lane, barracksId = null) {
  return barracksSystem.spawnScheduledBarracksRoster(game, lane, barracksId, BARRACKS_SYSTEM_DEPS);
}

function getBarracksSpawnColumns(role, unitCount) {
  return barracksSystem.getBarracksSpawnColumns(role, unitCount);
}

function spawnBarracksRosterLayout(game, lane, pendingEntries, options = null) {
  return barracksSystem.spawnBarracksRosterLayout(game, lane, pendingEntries, options, BARRACKS_SYSTEM_DEPS);
}

// ── Tower stat helpers ─────────────────────────────────────────────────────────

function seedStartingCombatTestMilitia(game, lane, count) {
  return barracksSystem.seedStartingCombatTestMilitia(game, lane, count, BARRACKS_SYSTEM_DEPS);
}

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

function hasExplicitMlOption(src, key) {
  return Object.prototype.hasOwnProperty.call(src, key)
    && src[key] !== undefined
    && src[key] !== null
    && src[key] !== "";
}

function normalizeMlOptionNumber(src, key, defaultValue, min, max, integer = false) {
  if (!hasExplicitMlOption(src, key))
    return defaultValue;

  const n = Number(src[key]);
  if (!Number.isFinite(n)) {
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected a finite number.`);
  }
  if (n < min || n > max) {
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected ${min}-${max}.`);
  }
  if (integer && !Number.isInteger(n)) {
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected a whole number.`);
  }
  return Math.max(min, Math.min(max, n));
}

function cloneSlotDef(slot, laneTeam) {
  const canonicalLaneTeam = normalizeAllegianceKey(laneTeam || slot.team || slot.slotColor || (slot.side === "left" ? "red" : "blue"));
  const canonicalSlotColor = normalizeAllegianceKey(slot.slotColor) || canonicalLaneTeam;
  return {
    laneIndex: slot.laneIndex,
    slotKey: slot.slotKey,
    side: slot.side,
    slotColor: canonicalSlotColor || slot.slotColor,
    branchId: slot.branchId,
    branchLabel: slot.branchLabel,
    castleSide: slot.castleSide,
    team: canonicalLaneTeam || ALLEGIANCE_KEYS.RED,
    allegianceKey: canonicalLaneTeam || ALLEGIANCE_KEYS.RED,
  };
}

// For 2-player, seat players on the authored Red vs Yellow branches.
// Blue and Green stay unoccupied and function as the outer-loop travel space between them.
const TWO_PLAYER_SLOT_BASES = [
  FIXED_SLOT_LAYOUT[0],
  Object.assign({}, FIXED_SLOT_LAYOUT[1], {
    laneIndex: 1,
    side: "right",
    castleSide: "left",
  }),
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
    const normalized = normalizeAllegianceKey(team);
    if (normalized) return normalized;
    return [ALLEGIANCE_KEYS.RED, ALLEGIANCE_KEYS.YELLOW, ALLEGIANCE_KEYS.BLUE, ALLEGIANCE_KEYS.GREEN][idx] || `p${idx}`;
  });
  return {
    startGold: normalizeMlOptionNumber(src, "startGold", runtime.startGold, 0, 10000, true),
    startIncome: normalizeMlOptionNumber(src, "startIncome", runtime.startIncome, 0, 1000),
    livesStart: normalizeMlOptionNumber(src, "livesStart", runtime.livesStart, 1, 1000, true),
    teamHpStart: normalizeMlOptionNumber(src, "teamHpStart", runtime.teamHpStart, 1, 1000, true),
    buildPhaseTicks: normalizeMlOptionNumber(src, "buildPhaseTicks", runtime.buildPhaseTicks, 20, 7200, true),
    transitionPhaseTicks: normalizeMlOptionNumber(src, "transitionPhaseTicks", runtime.transitionPhaseTicks, 20, 7200, true),
    laneTeams,
    matchSeed: typeof src.matchSeed === "number" ? (src.matchSeed >>> 0) : undefined,
    startingCombatMilitiaCount: Math.max(0, Math.floor(Number(src.startingCombatMilitiaCount) || 0)),
    battlefieldTopology: getBattlefieldTopology(Number(src.playerCount) || laneTeams.length || 4, laneTeams),
  };
}

// Get current tile position of a unit from its pathIdx
function getUnitTilePos(unit, path) {
  if (unit && Number.isFinite(Number(unit.posX)) && Number.isFinite(Number(unit.posY))) {
    return {
      x: Number(unit.posX),
      y: Number(unit.posY),
    };
  }
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
    const routeToCore = buildRouteSegments(ROUTE_TYPES.WAVE_LANE, getWaveSpawnNodeId(i), getLaneNodeId(i));
    // Legacy compatibility path samples for systems that still read lane.path/pathIdx.
    // Live movement authority remains routeSegments/currentSegment/segmentProgress.
    const path = buildSampledPathFromSegments(routeToCore, 28);
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
      allegianceKey: normalizeAllegianceKey(slot.team),
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
      fullPath: buildSampledPathFromSegments(routeToCore, 56),
      barracks: Object.assign({ level: 1 }, getBarracksLevelDef(1)),
      waveSpeedMult: 1,
      fortressPads: createFortressPadStates(opt.teamHpStart),
      barracksSiteStates: createBarracksSiteStates(opt.teamHpStart, 1),
      barracksSiteRosterCounts: createBarracksSiteRosterCounts(),
      heroCooldownReadyTicks: {},
      units: [],
      spawnQueue: [],
      projectiles: [],
      loadoutKeys: null,
      commandState: LANE_COMMAND_STATES.DEFEND,
      commandTargetLaneIndex: Number.isInteger(OPPOSING_LANE_INDEX[i]) && OPPOSING_LANE_INDEX[i] < playerCount
        ? OPPOSING_LANE_INDEX[i]
        : (playerCount > 1 ? ((i + 1) % playerCount) : i),
      commandAnchorProgress: 0,
      commandAnchor: null,
      commandFacing: null,
      commandSlots: [],
      assignedUnits: [],
      assignedUnitOrder: [],
      insideGateAnchor: null,
      outsideGateAnchor: null,
      enemyCoreAnchor: null,
      engagementRadius: LANE_COMMAND_COMBAT_LEASH,
      combatEnabled: true,
    });
  }
  const game = {
    tick: 0,
    phase: "playing",
    winner: null,
    matchState: "active_survival",
    officialWinnerLane: null,
    officialWinningTeam: null,
    officialWinningSide: null,
    losingTeam: null,
    losingSide: null,
    awaitingPostWinDecision: false,
    continuedIntoSurvival: true,
    pvpResolvedAtTick: null,
    survivalStartedAtTick: 0,
    survivalStartRound: 1,
    finalGameOverReason: null,
    finalGameOverDebug: null,
    playerCount,
    lanes,
    battlefieldTopology,
    // Wave defense state
    teamHp: { left: opt.teamHpStart, right: opt.teamHpStart },
    teamHpMax: opt.teamHpStart,
    buildPhaseTicks: opt.buildPhaseTicks,
    transitionPhaseTicks: opt.transitionPhaseTicks,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    barracksSendIntervalTicks: BARRACKS_SEND_TIMER_TICKS,
    waveIntervalTicks: WAVE_TIMER_TICKS,
    roundState: "combat",
    roundNumber: 1,
    roundStateTicks: 0,
    nextIncomeTick: INCOME_INTERVAL_TICKS,
    nextBarracksSendTick: BARRACKS_SEND_TIMER_TICKS,
    nextWaveTick: WAVE_TIMER_TICKS,
    lastWaveSpawnTick: null,
    hasSpawnedWave: false,
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
  if (opt.startingCombatMilitiaCount > 0) {
    for (const lane of lanes)
      seedStartingCombatTestMilitia(game, lane, opt.startingCombatMilitiaCount);
  }
  recomputeTeamHpState(game);
  return game;
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
  if (Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      if (!pad || !Array.isArray(pad.costHistory)) continue;
      for (const entry of pad.costHistory)
        total += Number(entry && entry.cost) || 0;
    }
  }
  if (lane.barracksSiteStates && typeof lane.barracksSiteStates === "object") {
    for (const siteState of Object.values(lane.barracksSiteStates)) {
      if (!siteState || !Array.isArray(siteState.costHistory)) continue;
      for (const entry of siteState.costHistory)
        total += Number(entry && entry.cost) || 0;
    }
  }
  if (lane.barracksSiteRosterCounts) {
    for (const siteDef of BARRACKS_SITE_DEFS) {
      const siteCounts = getBarracksSiteCounts(lane, siteDef.barracksId) || {};
      for (const rosterDef of BARRACKS_ROSTER_DEFS) {
        const ownedCount = Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
        total += ownedCount * getBarracksRosterBuyCost(rosterDef);
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
    teamHp: lane.lives,
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
  return areAllegiancesHostile(resolveLaneAllegianceKey(sourceLane), resolveLaneAllegianceKey(targetLane));
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

function applyFortressBuildOnPad(game, lane, padId) {
  return fortressSystem.applyFortressBuildOnPad(game, lane, padId, FORTRESS_SYSTEM_DEPS);
}

function applyBarracksSiteBuildAction(game, lane, barracksId) {
  return barracksSystem.applyBarracksSiteBuildAction(game, lane, barracksId);
}

function applyBarracksSiteUpgradeAction(game, lane, barracksId) {
  return barracksSystem.applyBarracksSiteUpgradeAction(game, lane, barracksId);
}

function applyFortressUpgrade(game, lane, padId) {
  return fortressSystem.applyFortressUpgrade(game, lane, padId, FORTRESS_SYSTEM_DEPS);
}

function deployBarracksHero(game, laneIndex, lane, heroKey, barracksId) {
  return barracksSystem.deployBarracksHero(game, laneIndex, lane, heroKey, barracksId, BARRACKS_SYSTEM_DEPS);
}

function resolveRequestedLaneAnchorProgress(game, lane, data, fallbackProgress) {
  const rawProgress = Number(
    data && (
      data.routeProgress
      ?? data.anchorProgress
      ?? data.progress
      ?? data.normProgress
    )
  );
  if (Number.isFinite(rawProgress))
    return Math.max(0, Math.min(1, rawProgress));

  const worldX = Number(data && (data.worldX ?? data.x));
  const worldY = Number(data && (data.worldY ?? data.y));
  if (Number.isFinite(worldX) && Number.isFinite(worldY) && lane && Number.isInteger(lane.laneIndex)) {
    const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
    const routeSegments = buildLaneCommandCoreRouteSegments(lane.laneIndex, objectiveLaneIndex);
    const projection = projectPointOntoRouteSegments(routeSegments, { x: worldX, y: worldY });
    if (projection)
      return Math.max(0, Math.min(1, Number(projection.routeProgress) || 0));
  }

  return Math.max(0, Math.min(1, Number(fallbackProgress) || 0));
}

function applyLaneCommandAction(game, lane, commandState, data = null) {
  const normalizedCommandState = normalizeLaneCommandState(commandState);
  if (!lane || !normalizedCommandState)
    return { ok: false, reason: "Invalid lane command" };

  const requestedTargetLaneIndex = Number.isInteger(data && data.targetLaneIndex)
    ? data.targetLaneIndex
    : null;
  if (requestedTargetLaneIndex !== null && isOpponentLane(game, lane.laneIndex, requestedTargetLaneIndex))
    lane.commandTargetLaneIndex = requestedTargetLaneIndex;

  let anchorProgress = getLaneCommandAnchorProgress(lane);
  if (normalizedCommandState === LANE_COMMAND_STATES.ATTACK) {
    anchorProgress = 1;
  } else {
    anchorProgress = 0;
  }

  lane.commandState = normalizedCommandState;
  lane.commandAnchorProgress = anchorProgress;
  lane.commandTargetLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  lane.combatEnabled = isLaneCombatEnabledCommandState(normalizedCommandState);
  lane.engagementRadius = getLaneCommandEngagementRadius(lane);
  syncLaneCommandAssignments(game);
  return { ok: true };
}

function warnLegacyActionOnce(game, laneIndex, type) {
  if (!game || !type) return;
  if (!(game.__legacyActionWarnings instanceof Set))
    game.__legacyActionWarnings = new Set();
  const warningKey = `${laneIndex}:${type}`;
  if (game.__legacyActionWarnings.has(warningKey)) return;
  game.__legacyActionWarnings.add(warningKey);
  log.warn("[ActionBoundary] rejected legacy fortress action", {
    actionType: type,
    laneIndex,
    tick: Number(game.tick) || 0,
  });
}

function rejectLegacyFortressAction(game, laneIndex, type) {
  const reason = LEGACY_ACTION_REJECTION_REASONS[type];
  if (!reason) return null;
  warnLegacyActionOnce(game, laneIndex, type);
  return { ok: false, reason };
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

  if (type === "build_on_pad") {
    const padId = String((data && data.padId) || "").trim();
    if (!padId) return { ok: false, reason: "Missing padId" };
    return applyFortressBuildOnPad(game, lane, padId);
  }

  if (type === "upgrade_building") {
    const rawPadId = String((data && data.padId) || "").trim();
    const rawBuildingType = String((data && data.buildingType) || "").trim();
    const padId = rawPadId || (getFortressPadByBuildingType(lane, rawBuildingType) || {}).padId;
    if (!padId) return { ok: false, reason: "Missing padId" };
    return applyFortressUpgrade(game, lane, padId);
  }

  if (type === "build_barracks_site") {
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    if (!barracksId) return { ok: false, reason: "Missing or invalid barracksId" };
    return applyBarracksSiteBuildAction(game, lane, barracksId);
  }

  if (type === "upgrade_barracks_site") {
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    if (!barracksId) return { ok: false, reason: "Missing or invalid barracksId" };
    return applyBarracksSiteUpgradeAction(game, lane, barracksId);
  }

  if (type === "buy_barracks_unit") {
    const rosterKey = String((data && data.rosterKey) || "").trim();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const requestedCount = Math.max(1, Math.floor(Number((data && data.count) || 1) || 1));
    const count = Math.min(25, requestedCount);
    return barracksSystem.buyBarracksUnit(
      game,
      laneIndex,
      lane,
      rosterKey,
      requestedBarracksId,
      count,
      BARRACKS_SYSTEM_DEPS
    );
  }

  if (type === "sell_barracks_unit") {
    const rosterKey = String((data && data.rosterKey) || "").trim();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    return barracksSystem.sellBarracksUnit(
      laneIndex,
      lane,
      rosterKey,
      requestedBarracksId,
      BARRACKS_SYSTEM_DEPS
    );
  }

  if (type === "deploy_barracks_hero") {
    const heroKey = String((data && data.heroKey) || "").trim().toLowerCase();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    return deployBarracksHero(game, laneIndex, lane, heroKey, requestedBarracksId);
  }

  if (type === "set_lane_attack")
    return applyLaneCommandAction(game, lane, LANE_COMMAND_STATES.ATTACK, data);

  if (type === "set_lane_defend" || type === "set_lane_hold" || type === "set_lane_defend_point")
    return applyLaneCommandAction(game, lane, LANE_COMMAND_STATES.DEFEND, data);

  if (type === "set_lane_retreat" || type === "set_lane_callback")
    return applyLaneCommandAction(game, lane, LANE_COMMAND_STATES.RETREAT, data);

  if (type === "set_lane_command") {
    const requestedCommandState = data && data.commandState;
    return applyLaneCommandAction(game, lane, requestedCommandState, data);
  }

  // Keep retired classic actions fenced here so old clients and AI paths fail
  // loudly without re-enabling removed game modes inside the active runtime.
  const legacyRejection = rejectLegacyFortressAction(game, laneIndex, type);
  if (legacyRejection) return legacyRejection;

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
  if (d < 0.01) {
    unit.posX = Math.max(minX, Math.min(maxX, tx));
    unit.posY = Math.max(minY, Math.min(maxY, ty));
    syncMovedUnitPathState(unit);
    return;
  }
  const step = Math.min(speed, d);
  unit.posX  = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY  = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  syncMovedUnitPathState(unit);
}

function moveTowardContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY) {
  if (!target) return;
  const dx = target.posX - unit.posX;
  const dy = target.posY - unit.posY;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d <= stopDistance || d < 0.01) return;
  const step = Math.min(speed, Math.max(0, d - stopDistance));
  if (step <= 0) return;
  unit.posX = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  syncMovedUnitPathState(unit);
}

function updateSimpleContactApproachMemory(unit, target) {
  if (!unit || !target || !target.id)
    return null;

  const transientLaneControlledApproach = USE_PER_UNIT_ANCHOR_SLOTS && isLaneControlledUnit(unit);
  const currentTargetId = String(target.id);
  const cachedTargetId = String(unit.simpleContactApproachTargetId || "");
  let memoryX = Number(unit.simpleContactApproachX);
  let memoryY = Number(unit.simpleContactApproachY);
  let memoryMagnitude = Math.sqrt((memoryX * memoryX) + (memoryY * memoryY));
  if (cachedTargetId !== currentTargetId || !Number.isFinite(memoryMagnitude) || memoryMagnitude < 0.001) {
    let dx = Number(unit.posX) - Number(target.posX);
    let dy = Number(unit.posY) - Number(target.posY);
    let distance = Math.sqrt((dx * dx) + (dy * dy));
    if (!Number.isFinite(distance) || distance < 0.001) {
      const fallbackForward = getUnitForwardDirection(unit);
      dx = -(Number(fallbackForward && fallbackForward.x) || 0);
      dy = -(Number(fallbackForward && fallbackForward.y) || 1);
      distance = Math.sqrt((dx * dx) + (dy * dy));
    }

    if (!Number.isFinite(distance) || distance < 0.001)
      return null;

    memoryX = dx / distance;
    memoryY = dy / distance;
    memoryMagnitude = Math.sqrt((memoryX * memoryX) + (memoryY * memoryY));
    if (!transientLaneControlledApproach) {
      unit.simpleContactApproachTargetId = currentTargetId;
      unit.simpleContactApproachX = memoryX;
      unit.simpleContactApproachY = memoryY;
    }
  }

  return {
    x: memoryX / memoryMagnitude,
    y: memoryY / memoryMagnitude,
  };
}

function moveTowardSimpleContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY) {
  if (!target)
    return;

  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (!Number.isFinite(distance) || distance <= stopDistance || distance < 0.01)
    return;

  const approachMemory = updateSimpleContactApproachMemory(unit, target);
  const captureDistance = stopDistance + Math.max(0.55, getUnitContactRadius(unit && unit.type) * 0.65);
  if (approachMemory && distance <= captureDistance) {
    moveTowardPoint2D(
      unit,
      Number(target.posX) + (approachMemory.x * stopDistance),
      Number(target.posY) + (approachMemory.y * stopDistance),
      speed,
      minX,
      maxX,
      minY,
      maxY
    );
    return;
  }

  moveTowardContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY);
}

function moveTowardPoint2D(unit, tx, ty, speed, minX, maxX, minY, maxY) {
  const dx = tx - unit.posX;
  const dy = ty - unit.posY;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d < 0.01) {
    unit.posX = Math.max(minX, Math.min(maxX, tx));
    unit.posY = Math.max(minY, Math.min(maxY, ty));
    syncMovedUnitPathState(unit);
    return;
  }
  const step = Math.min(speed, d);
  unit.posX = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  syncMovedUnitPathState(unit);
}

function getUnitContactRadius(typeKey) {
  if (typeKey && FORTRESS_BUILDING_DEFS[typeKey]) {
    if (typeKey === "town_core") return 1.15;
    return 0.95;
  }

  const def = resolveUnitDef(typeKey);
  const hp = Number(def && def.hp) || 80;
  const atkRange = getUnitAttackRange(typeKey);

  let radius;
  if (hp >= 140) radius = 0.85;
  else if (hp >= 100) radius = 0.75;
  else if (hp >= 70) radius = 0.65;
  else if (hp >= 50) radius = 0.55;
  else radius = 0.45;

  if (atkRange > 2.5)
    radius = Math.max(0.45, radius - 0.10);

  if (typeof typeKey === "string" && (typeKey.startsWith("tt_") || isFortArchetypeKey(typeKey)))
    radius += 0.20;

  return radius;
}

function getUnitStopDistance(attackerType, targetType) {
  const attackRange = getUnitAttackRange(attackerType);
  const base = attackRange + (attackRange > 2.0 ? 0.15 : 0.05);
  if (attackRange > 2.0) return base;
  const attackerRadius = getUnitContactRadius(attackerType);
  const targetRadius = getUnitContactRadius(targetType);
  const bodyPadding = Math.max(attackerRadius, targetRadius, (attackerRadius + targetRadius) * 0.5);
  const attackerRole = resolveUnitCombatRole({
    type: attackerType,
    unitTypeKey: attackerType,
    archetypeKey: attackerType,
    rosterKey: attackerType,
  });

  let meleeContactBias = 0;
  switch (attackerRole) {
    case UNIT_COMBAT_ROLES.SHIELD:
      meleeContactBias = -0.35;
      break;
    case UNIT_COMBAT_ROLES.SWORD:
      meleeContactBias = -0.65;
      break;
    case UNIT_COMBAT_ROLES.SPEAR:
      meleeContactBias = -0.20;
      break;
    default:
      meleeContactBias = 0;
      break;
  }

  return base + Math.max(0.35, bodyPadding + meleeContactBias);
}

function getLaneByIndex(game, laneIndex) {
  if (!game || !Array.isArray(game.lanes) || !Number.isInteger(laneIndex))
    return null;
  return game.lanes.find((candidate) => candidate && candidate.laneIndex === laneIndex) || null;
}

function resolveRouteUnitLane(game, unit, fallbackLane = null) {
  if (!game || !unit)
    return fallbackLane;

  if (Number.isInteger(unit.targetLaneIndex)) {
    const explicitTargetLane = getLaneByIndex(game, unit.targetLaneIndex);
    if (explicitTargetLane)
      return explicitTargetLane;
  }

  if (Number.isInteger(unit.laneId)) {
    const explicitLane = getLaneByIndex(game, unit.laneId);
    if (explicitLane)
      return explicitLane;
  }

  return fallbackLane;
}

function findRouteUnitById(game, unitId, preferredLaneIndex = -1) {
  if (!game || !Array.isArray(game.lanes) || !unitId)
    return null;

  if (Number.isInteger(preferredLaneIndex)) {
    const preferredLane = getLaneByIndex(game, preferredLaneIndex);
    if (preferredLane) {
      const match = preferredLane.units.find((candidate) => candidate && candidate.id === unitId && candidate.hp > 0);
      if (match)
        return { lane: preferredLane, unit: match };
    }
  }

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    const match = lane.units.find((candidate) => candidate && candidate.id === unitId && candidate.hp > 0);
    if (match)
      return { lane, unit: match };
  }

  return null;
}

function resolveRouteUnitFactionKey(game, unit) {
  if (!unit)
    return null;
  return resolveUnitAllegianceKey(game, resolveRouteUnitLane(game, unit, null), unit);
}

function getLaneCommandAnchorStateForUnit(game, unit) {
  const ownerLane = getLaneCommandOwnerLane(game, unit);
  return ownerLane ? sampleLaneCommandAnchor(game, ownerLane) : null;
}

function isTargetInsideHomeFortressEmergencyZone(game, attacker, target) {
  if (!game || !attacker || !target || !isLaneControlledUnit(attacker))
    return false;

  const ownerLane = getLaneCommandOwnerLane(game, attacker);
  const townCoreTarget = getLaneTownCoreCombatTarget(ownerLane);
  const distanceToCore = getWaveUnitTargetDistance(target, townCoreTarget);
  return Number.isFinite(distanceToCore)
    && distanceToCore <= FORTRESS_INTERIOR_ASSAULT_RADIUS;
}

function isTargetInsideLaneDefenseZone(game, attacker, target) {
  if (!attacker || !target || !isLaneControlledUnit(attacker))
    return true;

  const commandState = getLaneCommandStateForUnit(game, attacker);
  if (commandState !== LANE_COMMAND_STATES.DEFEND)
    return true;
  if (isTargetInsideHomeFortressEmergencyZone(game, attacker, target))
    return true;

  const anchorState = getLaneCommandAnchorStateForUnit(game, attacker);
  if (!anchorState || !Number.isFinite(anchorState.engagementRadius) || anchorState.engagementRadius <= 0)
    return false;

  const tx = Number(target.posX);
  const ty = Number(target.posY);
  if (!Number.isFinite(tx) || !Number.isFinite(ty))
    return false;

  const dx = tx - anchorState.anchorX;
  const dy = ty - anchorState.anchorY;
  return Math.sqrt((dx * dx) + (dy * dy)) <= anchorState.engagementRadius;
}

function canLaneControlledUnitSeekCombat(game, attacker, target = null) {
  if (!isLaneControlledUnit(attacker))
    return true;
  if (!isLaneCommandCombatEnabledForUnit(game, attacker))
    return false;
  if (!target)
    return true;
  if (isTargetInsideLaneDefenseZone(game, attacker, target))
    return true;

  const liveDistance = getWaveUnitTargetDistance(attacker, target);
  return Number.isFinite(liveDistance)
    && liveDistance <= getUnitEngagementRange(attacker.type) + CONTACT_SLOT_TOLERANCE;
}

function areRouteUnitsHostile(game, attacker, target) {
  if (!attacker || !target || attacker === target)
    return false;
  if (attacker.hp <= 0 || target.hp <= 0)
    return false;
  if (attacker.isDefender || target.isDefender)
    return false;

  const attackerFaction = resolveRouteUnitFactionKey(game, attacker);
  const targetFaction = resolveRouteUnitFactionKey(game, target);
  if (attackerFaction && targetFaction)
    return areAllegiancesHostile(attackerFaction, targetFaction);

  return attacker.id !== target.id;
}

function canEngageRouteUnitTarget(game, attacker, target) {
  return canLaneControlledUnitSeekCombat(game, attacker, target)
    && areRouteUnitsHostile(game, attacker, target);
}

function isRouteUnitHostileToLane(game, lane, unit) {
  if (!lane || !unit || unit.hp <= 0 || unit.isDefender)
    return false;

  const laneTeam = resolveLaneAllegianceKey(lane);
  const unitFaction = resolveRouteUnitFactionKey(game, unit);
  if (laneTeam && unitFaction)
    return areAllegiancesHostile(laneTeam, unitFaction);

  if (Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0)
    return unit.sourceLaneIndex !== lane.laneIndex;

  return true;
}

function canRouteUnitEngageTarget(game, lane, attacker, target) {
  if (!attacker || !target)
    return false;
  if (!canLaneControlledUnitSeekCombat(game, attacker, target))
    return false;
  if (target.isDefender)
    return false;
  return canEngageRouteUnitTarget(game, attacker, target);
}

function getLaneControlledSharedCombatAnchor(unit) {
  const commandState = normalizeLaneCommandState(unit && unit.commandState);
  if (commandState !== LANE_COMMAND_STATES.DEFEND && commandState !== LANE_COMMAND_STATES.RETREAT)
    return null;

  const anchorX = Number.isFinite(Number(unit && unit.anchorTargetX))
    ? Number(unit.anchorTargetX)
    : Number(unit && unit.posX);
  const anchorY = Number.isFinite(Number(unit && unit.anchorTargetY))
    ? Number(unit.anchorTargetY)
    : Number(unit && unit.posY);
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY))
    return null;
  return { x: anchorX, y: anchorY };
}

function shouldLaneControlledUnitFreeRoamInCombat(unit) {
  return false;
}

function getLaneControlledSharedCombatJoinRadius(unit) {
  const baseRadius = getLaneControlledCombatLeashRadius(unit) + ROUTE_SLOT_ROW_SPACING;
  if (!USE_PER_UNIT_ANCHOR_SLOTS || !isLaneControlledUnit(unit))
    return baseRadius;

  const commandState = normalizeLaneCommandState(unit.commandState);
  if (commandState === LANE_COMMAND_STATES.DEFEND)
    return Math.max(baseRadius, LANE_COMMAND_DEFENSE_RADIUS + ANCHOR_SUPPORT_TARGET_RADIUS);
  if (commandState === LANE_COMMAND_STATES.ATTACK)
    return Math.max(baseRadius, LANE_COMMAND_COMBAT_LEASH + ANCHOR_SUPPORT_TARGET_RADIUS);
  return Math.max(baseRadius, LANE_COMMAND_COMBAT_LEASH + ROUTE_SLOT_ROW_SPACING);
}

function isLaneControlledUnitNearSharedCombat(unit, target) {
  if (!unit || !target)
    return false;

  const unitX = Number(unit.posX);
  const unitY = Number(unit.posY);
  const targetX = Number(target.posX);
  const targetY = Number(target.posY);
  if (!Number.isFinite(unitX) || !Number.isFinite(unitY) || !Number.isFinite(targetX) || !Number.isFinite(targetY))
    return false;

  const anchor = getLaneControlledSharedCombatAnchor(unit);
  const maxDistance = getLaneControlledSharedCombatJoinRadius(unit);
  const anchorDistance = anchor
    ? Math.sqrt(Math.pow(targetX - anchor.x, 2) + Math.pow(targetY - anchor.y, 2))
    : Infinity;
  const liveDistance = Math.sqrt(Math.pow(targetX - unitX, 2) + Math.pow(targetY - unitY, 2));

  return Math.min(anchorDistance, liveDistance) <= maxDistance;
}

function shouldLaneControlledUnitRouteMarch(unit) {
  if (!isLaneControlledUnit(unit))
    return false;

  const commandState = normalizeLaneCommandState(unit.commandState);
  if (commandState === LANE_COMMAND_STATES.ATTACK)
    return true;

  return String(unit.currentWaypointTargetKind || "").trim().toLowerCase()
    === String(LANE_STANCE_ANCHOR_KINDS.ENEMY_CORE).toLowerCase();
}

function clearUnitCombatTarget(unit, gameTick = 0, options = {}) {
  if (!unit)
    return;

  const hadTarget = !!(unit.combatTarget && unit.combatTarget.unitId);
  const suppressRegroup = !!(options && options.suppressRegroup);
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.currentTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.combatTargetWorldX = null;
  unit.combatTargetWorldY = null;
  unit.simpleContactApproachTargetId = null;
  unit.simpleContactApproachX = 0;
  unit.simpleContactApproachY = 0;

  if (!hadTarget)
    return;

  if (suppressRegroup) {
    unit.regroupUntilTick = 0;
    return;
  }

  if (!isLaneControlledUnit(unit))
    return;

  const nextRegroupTick = Number.isFinite(Number(options.regroupUntilTick))
    ? Number(options.regroupUntilTick)
    : gameTick + LANE_COMBAT_REGROUP_TICKS;
  unit.regroupUntilTick = Math.max(Number(unit.regroupUntilTick) || 0, nextRegroupTick);
}

function assignUnitCombatTarget(unit, targetDescriptor, gameTick = 0) {
  if (!unit || !targetDescriptor || !targetDescriptor.unitId)
    return false;

  const previousTargetId = unit.combatTarget && unit.combatTarget.unitId
    ? unit.combatTarget.unitId
    : null;
  unit.combatTarget = {
    unitId: targetDescriptor.unitId,
    kind: targetDescriptor.kind || "unit",
    laneIndex: Number.isInteger(targetDescriptor.laneIndex) ? targetDescriptor.laneIndex : null,
    padId: targetDescriptor.padId || null,
  };
  unit.combatTargetId = targetDescriptor.unitId;
  unit.currentTargetId = targetDescriptor.unitId;
  unit.combatTargetLockedUntilTick = 0;
  unit.routeRecoveringFromCombatTicks = 0;
  if (!previousTargetId || previousTargetId !== targetDescriptor.unitId)
    unit.regroupUntilTick = 0;
  return true;
}

function assignUnitSupportTarget(unit, targetDescriptor, gameTick = 0) {
  return assignUnitCombatTarget(unit, targetDescriptor, gameTick);
}

function isLaneControlledUnitInRegroupWindow(unit, gameTick) {
  return !!(isLaneControlledUnit(unit) && Number(unit.regroupUntilTick) > gameTick);
}

function isUnitCombatTargetStillValid(game, lane, attacker, target) {
  if (!attacker || !target)
    return false;

  if (target.kind === "unit") {
    if (!canRouteUnitEngageTarget(game, lane, attacker, target.entity))
      return false;
    if (isLaneControlledUnit(attacker))
      return isLaneControlledUnitNearSharedCombat(attacker, target);
    return true;
  }

  return canLaneControlledUnitSeekCombat(game, attacker, target);
}

function getResolvedCombatTargetDistance(unit, target) {
  const liveTarget = target && target.entity
    ? target.entity
    : target;
  const distance = getWaveUnitTargetDistance(unit, liveTarget);
  return Number.isFinite(distance) ? distance : Infinity;
}

function findFriendlyHealTarget(game, lane, unit, supportProfile) {
  if (!game || !lane || !unit || !supportProfile || !supportProfile.isHealer || !Array.isArray(lane.units))
    return null;

  const healerAllegiance = resolveUnitAllegianceKey(game, lane, unit);
  const maxSeekDistance = isLaneControlledUnit(unit)
    ? Math.max(getLaneControlledCombatLeashRadius(lane, unit), getUnitAttackRange(unit.type) + 0.5)
    : Math.max(getUnitEngagementRange(unit.type), getUnitAttackRange(unit.type) + 0.5);
  let best = null;
  let bestHealthRatio = Infinity;
  let bestDist = Infinity;

  for (const candidate of lane.units) {
    if (!candidate || candidate === unit || Number(candidate.hp) <= 0)
      continue;

    const candidateAllegiance = resolveUnitAllegianceKey(game, lane, candidate);
    if (areAllegiancesHostile(healerAllegiance, candidateAllegiance))
      continue;

    const maxHp = Math.max(1, Number(candidate.maxHp) || Number(candidate.hp) || 1);
    const hp = Math.max(0, Number(candidate.hp) || 0);
    if (hp >= maxHp - 0.001)
      continue;

    const dist = dist2D(unit, candidate);
    if (!Number.isFinite(dist) || dist > maxSeekDistance + CONTACT_SLOT_TOLERANCE)
      continue;

    const healthRatio = hp / maxHp;
    if (healthRatio < bestHealthRatio - 0.0001
        || (Math.abs(healthRatio - bestHealthRatio) <= 0.0001 && dist < bestDist - 0.0001)
        || (Math.abs(healthRatio - bestHealthRatio) <= 0.0001
            && Math.abs(dist - bestDist) <= 0.0001
            && String(candidate.id || "").localeCompare(String(best && best.id || "")) < 0)) {
      best = candidate;
      bestHealthRatio = healthRatio;
      bestDist = dist;
    }
  }

  return best;
}

function getRouteUnitTargetPressureCount(game, seeker, targetEntity) {
  if (!game || !seeker || !targetEntity || !isLaneControlledUnit(seeker))
    return 0;

  const seekerFaction = resolveRouteUnitFactionKey(game, seeker);
  let pressure = 0;
  for (const allyLane of game.lanes || []) {
    if (!allyLane || !Array.isArray(allyLane.units))
      continue;

    for (const ally of allyLane.units) {
      if (!ally || ally === seeker || ally.hp <= 0 || !isLaneControlledUnit(ally))
        continue;
      const allyFaction = resolveRouteUnitFactionKey(game, ally);
      if (!seekerFaction || !allyFaction || areAllegiancesHostile(seekerFaction, allyFaction))
        continue;

      const allyTargetId = ally && ally.combatTarget && ally.combatTarget.unitId
        ? ally.combatTarget.unitId
        : ally && ally.combatTargetId;
      if (allyTargetId === targetEntity.id)
        pressure += 1;
    }
  }

  return pressure;
}

function getRouteUnitTargetPreferenceScore(game, lane, unit, target, requireAttackRange = false) {
  const liveTarget = target && target.entity
    ? target.entity
    : target;
  const centerDistance = getResolvedCombatTargetDistance(unit, liveTarget);
  if (!Number.isFinite(centerDistance))
    return Infinity;

  let approachDistance = centerDistance;
  if (lane
      && isLaneControlledUnit(unit)
      && liveTarget
      && shouldUseLaneControlledSurroundSlots(unit, liveTarget)) {
    const stopDistance = getUnitStopDistance(unit.type, liveTarget.type);
    const slotPoint = getLaneControlledCombatPocketPoint(
      lane,
      unit,
      liveTarget,
      stopDistance,
      { appendAttackerIfMissing: true }
    );
    const slotDistance = Math.hypot(
      Number(unit.posX) - Number(slotPoint.x),
      Number(unit.posY) - Number(slotPoint.y)
    );
    if (Number.isFinite(slotDistance))
      approachDistance = Math.max(
        slotDistance,
        Math.max(0, centerDistance - stopDistance)
      );
  }

  if (requireAttackRange || !isLaneControlledUnit(unit) || !liveTarget)
    return approachDistance;

  const pressure = getRouteUnitTargetPressureCount(game, unit, liveTarget);
  return approachDistance + (pressure * ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY);
}

function shouldSwitchCombatTarget(game, lane, unit, currentTarget, nextTarget) {
  if (!unit || !currentTarget || !nextTarget)
    return false;
  if (currentTarget.kind !== "unit" || nextTarget.kind !== "unit")
    return true;
  if (currentTarget.entity && nextTarget.entity && currentTarget.entity.id === nextTarget.entity.id)
    return false;

  const currentTargetEntity = currentTarget && currentTarget.entity
    ? currentTarget.entity
    : currentTarget;
  const currentTargetInContact = !!(lane && currentTargetEntity && isUnitInCombatContact(lane, unit, currentTargetEntity));
  if (currentTargetInContact)
    return false;

  const currentDistance = getResolvedCombatTargetDistance(unit, currentTarget);
  const nextDistance = getResolvedCombatTargetDistance(unit, nextTarget);
  if (!Number.isFinite(currentDistance))
    return true;
  if (!Number.isFinite(nextDistance))
    return false;

  const currentScore = Number.isFinite(Number(currentTarget && currentTarget.preferenceScore))
    ? Number(currentTarget.preferenceScore)
    : getRouteUnitTargetPreferenceScore(game, lane, unit, currentTarget, false);
  const nextScore = Number.isFinite(Number(nextTarget && nextTarget.preferenceScore))
    ? Number(nextTarget.preferenceScore)
    : getRouteUnitTargetPreferenceScore(game, lane, unit, nextTarget, false);
  const switchMargin = normalizeLaneCommandState(unit.commandState) === LANE_COMMAND_STATES.DEFEND
    ? (currentTargetInContact
      ? Math.min(LANE_COMBAT_SWITCH_DISTANCE_MARGIN, 0.35)
      : 0.05)
    : LANE_COMBAT_SWITCH_DISTANCE_MARGIN;

  if (Number.isFinite(currentDistance) && Number.isFinite(nextDistance)) {
    if (nextDistance + switchMargin < currentDistance)
      return true;
    if (currentDistance + switchMargin < nextDistance)
      return false;
  }

  if (Number.isFinite(currentScore) && Number.isFinite(nextScore))
    return nextScore + 0.05 < currentScore;

  return nextDistance + switchMargin < currentDistance;
}

function getLaneControlledCombatLeashRadius(unit) {
  const anchorLeash = Number(unit && unit.anchorLeashRadius);
  const commandLeash = Number(unit && unit.combatLeashRadius);
  const leashRadius = Math.max(
    Number.isFinite(anchorLeash) && anchorLeash > 0 ? anchorLeash : 0,
    Number.isFinite(commandLeash) && commandLeash > 0 ? commandLeash : 0
  );
  if (leashRadius > 0)
    return leashRadius;
  return LANE_COMMAND_COMBAT_LEASH;
}

function clampPointToRadius(pointX, pointY, centerX, centerY, maxRadius) {
  const safePointX = Number(pointX);
  const safePointY = Number(pointY);
  if (!Number.isFinite(safePointX) || !Number.isFinite(safePointY))
    return null;
  if (!Number.isFinite(centerX) || !Number.isFinite(centerY) || !Number.isFinite(maxRadius))
    return { x: safePointX, y: safePointY };
  if (maxRadius <= 0)
    return { x: centerX, y: centerY };

  const dx = safePointX - centerX;
  const dy = safePointY - centerY;
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (distance <= maxRadius || distance < 0.001) {
    return { x: safePointX, y: safePointY };
  }

  const scale = maxRadius / distance;
  return {
    x: centerX + (dx * scale),
    y: centerY + (dy * scale),
  };
}

function isPointWithinRadius(pointX, pointY, centerX, centerY, maxRadius) {
  const safePointX = Number(pointX);
  const safePointY = Number(pointY);
  const safeCenterX = Number(centerX);
  const safeCenterY = Number(centerY);
  const safeRadius = Number(maxRadius);
  if (!Number.isFinite(safePointX) || !Number.isFinite(safePointY)
      || !Number.isFinite(safeCenterX) || !Number.isFinite(safeCenterY)
      || !Number.isFinite(safeRadius) || safeRadius < 0) {
    return false;
  }

  const dx = safePointX - safeCenterX;
  const dy = safePointY - safeCenterY;
  return ((dx * dx) + (dy * dy)) <= (safeRadius * safeRadius);
}

function shouldAnchorClampLaneControlledCombat(unit, target) {
  if (!isLaneControlledUnit(unit) || !target)
    return false;
  if (shouldLaneControlledUnitFreeRoamInCombat(unit))
    return false;

  const anchor = getLaneControlledSharedCombatAnchor(unit);
  if (!anchor)
    return false;

  const targetX = Number(target.posX);
  const targetY = Number(target.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return false;

  const leashRadius = getLaneControlledCombatLeashRadius(unit);
  const sharedCombatRadius = leashRadius + ROUTE_SLOT_ROW_SPACING;
  if (isPointWithinRadius(targetX, targetY, anchor.x, anchor.y, sharedCombatRadius))
    return true;

  return !isPointWithinRadius(targetX, targetY, Number(unit.posX), Number(unit.posY), sharedCombatRadius);
}

function resolveWaveCombatTarget(game, lane, combatTarget) {
  if (!lane || !combatTarget || !combatTarget.unitId) return null;

  if (combatTarget.kind === "fortress_pad") {
    const targetLane = Number.isInteger(combatTarget.laneIndex)
      ? (getLaneByIndex(game, combatTarget.laneIndex) || lane)
      : lane;
    const barracksSiteMatch = String(combatTarget.padId || combatTarget.unitId || "").match(/^barracks_site:(.+)$/);
    if (barracksSiteMatch)
      return getBarracksSiteCombatTarget(targetLane, barracksSiteMatch[1]);

    const padState = combatTarget.padId
      ? getFortressPadState(targetLane, combatTarget.padId)
      : getLaneTownCorePad(targetLane);
    if (padState && padState.buildingType === "town_core")
      return getLaneTownCoreCombatTarget(targetLane);
    return getFortressPadCombatTarget(targetLane, padState);
  }

  const unit = lane.units.find((candidate) => candidate && candidate.id === combatTarget.unitId && candidate.hp > 0);
  if (!unit) {
    const resolved = findRouteUnitById(game, combatTarget.unitId, Number.isInteger(combatTarget.laneIndex) ? combatTarget.laneIndex : lane.laneIndex);
    if (!resolved || !resolved.unit)
      return null;
    return {
      id: resolved.unit.id,
      unitId: resolved.unit.id,
      kind: "unit",
      type: resolved.unit.type,
      posX: resolved.unit.posX,
      posY: resolved.unit.posY,
      laneIndex: resolved.lane ? resolved.lane.laneIndex : null,
      entity: resolved.unit,
    };
  }

  return {
    id: unit.id,
    unitId: unit.id,
    kind: "unit",
    type: unit.type,
    posX: unit.posX,
    posY: unit.posY,
    laneIndex: lane.laneIndex,
    entity: unit,
  };
}

function markTownCoreBreach(game, lane, unit, townCoreTarget) {
  if (!game || !lane || !unit || unit.hasBreachedTownCore) return;
  unit.hasBreachedTownCore = true;
  lane.totalLeaksTaken += 1;
  lane.leakCountThisRound += 1;
  log.info("[TownCoreTrace] core target acquired", {
    tick: game.tick,
    laneIndex: lane.laneIndex,
    attackerId: unit.id,
    attackerType: unit.type,
    attackerHp: Math.max(0, Math.floor(Number(unit.hp) || 0)),
    pathIdx: Number.isFinite(unit.pathIdx) ? Number(unit.pathIdx.toFixed(2)) : null,
    corePadId: townCoreTarget ? townCoreTarget.padId || townCoreTarget.id : null,
    coreHp: getLaneTownCoreHp(lane),
    coreMaxHp: getLaneTownCoreMaxHp(lane),
  });
  combatLog.logEvent(game, "leak", {
    unitId: unit.id,
    unitType: unit.type,
    lane: lane.laneIndex,
    targetPadId: townCoreTarget ? townCoreTarget.padId || townCoreTarget.id : null,
    breachOnly: true,
  });
}

function attackFortressPad(game, lane, attacker, target) {
  if (!game || !lane || !attacker || !target || target.kind !== "fortress_pad") {
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };
  }

  const targetLane = Number.isInteger(target.laneIndex)
    ? (getLaneByIndex(game, target.laneIndex) || lane)
    : lane;
  const def = resolveUnitDef(attacker.type);
  const rawDamage = Math.max(1, Math.floor(Number(attacker.baseDmg) || Number(def && def.dmg) || 1));
  const cooldownTicks = Math.max(1, Math.floor(Number(attacker.atkCdTicks) || Number(def && def.atkCdTicks) || 20));
  const targetPad = getFortressPadState(targetLane, target.padId || target.id);
  const prevHp = targetPad ? Math.max(0, Math.floor(Number(targetPad.hp) || 0)) : 0;
  const result = applyFortressPadDamage(game, targetLane, target.padId || target.id, rawDamage);

  attacker.attackPulse = (attacker.attackPulse || 0) + 1;
  attacker.atkCd = cooldownTicks;
  if (targetPad && targetPad.buildingType === "town_core") {
    log.info("[TownCoreTrace] core attacked", {
      tick: game.tick,
      laneIndex: targetLane.laneIndex,
      attackerId: attacker.id,
      attackerType: attacker.type,
      corePadId: targetPad.padId,
      attemptedDamage: rawDamage,
      appliedDamage: result.damageApplied,
      previousHp: prevHp,
      remainingHp: result.remainingHp,
      destroyed: !!result.destroyed,
    });
  }
  return result;
}

function clampUnitToAnchor(unit, anchorX, anchorY, maxRadius, minX, maxX, minY, maxY) {
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY) || maxRadius <= 0) return;
  const dx = unit.posX - anchorX;
  const dy = unit.posY - anchorY;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d <= maxRadius || d < 0.001) return;
  const scale = maxRadius / d;
  unit.posX = Math.max(minX, Math.min(maxX, anchorX + dx * scale));
  unit.posY = Math.max(minY, Math.min(maxY, anchorY + dy * scale));
  syncMovedUnitPathState(unit);
}

function clampLaneControlledUnitToCombatLeash(unit, minX, maxX, minY, maxY, target = null) {
  if (!isLaneControlledUnit(unit))
    return false;
  if (target && !shouldAnchorClampLaneControlledCombat(unit, target))
    return false;

  const anchor = getLaneControlledSharedCombatAnchor(unit);
  if (!anchor)
    return false;

  const previousX = Number(unit.posX);
  const previousY = Number(unit.posY);
  clampUnitToAnchor(
    unit,
    anchor.x,
    anchor.y,
    getLaneControlledCombatLeashRadius(unit),
    minX,
    maxX,
    minY,
    maxY
  );
  return previousX !== Number(unit.posX) || previousY !== Number(unit.posY);
}

function resolveContactFrame(attacker, target) {
  const dx = Number(attacker && attacker.posX) - Number(target && target.posX);
  const dy = Number(attacker && attacker.posY) - Number(target && target.posY);
  const dist = Math.sqrt((dx * dx) + (dy * dy));
  if (dist > 0.0001) {
    const forward = { x: dx / dist, y: dy / dist };
    return {
      forward,
      right: { x: -forward.y, y: forward.x },
    };
  }

  const attackerForward = getUnitForwardDirection(attacker);
  const fallbackForward = {
    x: -(Number(attackerForward && attackerForward.x) || 0),
    y: -(Number(attackerForward && attackerForward.y) || 1),
  };
  const fallbackMag = Math.sqrt((fallbackForward.x * fallbackForward.x) + (fallbackForward.y * fallbackForward.y));
  if (fallbackMag > 0.0001) {
    const forward = {
      x: fallbackForward.x / fallbackMag,
      y: fallbackForward.y / fallbackMag,
    };
    return {
      forward,
      right: { x: -forward.y, y: forward.x },
    };
  }

  return {
    forward: { x: 0, y: -1 },
    right: { x: 1, y: 0 },
  };
}

function shouldUseLaneControlledSurroundSlots(attacker, target) {
  if (!isLaneControlledUnit(attacker))
    return false;
  if (!target || (target.kind && target.kind !== "unit"))
    return false;

  return getUnitAttackRange(attacker.type) <= 2.0;
}

function getContactSlotPoint(lane, attacker, target, stopDistance, options = null) {
  const appendAttackerIfMissing = !!(options && options.appendAttackerIfMissing);
  const attackers = lane.units
    .filter(u =>
      u &&
      u.hp > 0 &&
      !u.isDefender &&
      u.combatTarget &&
      u.combatTarget.unitId === target.id)
    .sort((a, b) => {
      const leftKey = resolveLaneControlledUnitSortKey(a);
      const rightKey = resolveLaneControlledUnitSortKey(b);
      return leftKey.localeCompare(rightKey);
    });

  const attackerIndex = attackers.findIndex(u => u.id === attacker.id);
  const slotIndex = Math.max(0, attackerIndex >= 0 ? attackerIndex : (appendAttackerIfMissing ? attackers.length : 0));
  const effectiveAttackerCount = appendAttackerIfMissing && attackerIndex < 0
    ? attackers.length + 1
    : attackers.length;
  const radius = Math.max(0.75, getUnitContactRadius(attacker.type) + getUnitContactRadius(target.type));
  const centroid = attackers.reduce((sum, unit) => ({
    x: sum.x + (Number(unit.posX) || 0),
    y: sum.y + (Number(unit.posY) || 0),
  }), { x: 0, y: 0 });
  const centroidX = attackers.length > 0 ? centroid.x / attackers.length : Number(attacker.posX) || Number(target.posX) || 0;
  const centroidY = attackers.length > 0 ? centroid.y / attackers.length : Number(attacker.posY) || Number(target.posY) || 0;
  let baseAngle = Math.atan2(centroidY - Number(target.posY), centroidX - Number(target.posX));
  if (!Number.isFinite(baseAngle)) {
    const frame = resolveContactFrame(attacker, target);
    baseAngle = Math.atan2(frame.forward.y, frame.forward.x);
  }

  let ring = 0;
  let slotOnRing = slotIndex;
  let slotsThisRing = 6;
  let attackersBeforeRing = 0;
  while (slotOnRing >= slotsThisRing) {
    slotOnRing -= slotsThisRing;
    attackersBeforeRing += slotsThisRing;
    ring += 1;
    slotsThisRing = 6 + (ring * 2);
  }

  const ringSpacing = Math.max(0.70, radius * 0.90);
  const ringRadius = Math.max(stopDistance, radius * 1.1) + (ring * ringSpacing);
  const occupiedSlotsOnRing = Math.max(1, Math.min(slotsThisRing, effectiveAttackerCount - attackersBeforeRing));
  const angle = baseAngle + ((slotOnRing / occupiedSlotsOnRing) * Math.PI * 2);

  return {
    x: target.posX + (Math.cos(angle) * ringRadius),
    y: target.posY + (Math.sin(angle) * ringRadius),
  };
}

function getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance, options = null) {
  const desiredPoint = getContactSlotPoint(lane, attacker, target, stopDistance, options);
  if (!isLaneControlledUnit(attacker))
    return desiredPoint;
  if (shouldLaneControlledUnitFreeRoamInCombat(attacker))
    return desiredPoint;

  const anchor = getLaneControlledSharedCombatAnchor(attacker);
  if (!anchor)
    return desiredPoint;

  const targetX = Number(target && target.posX);
  const targetY = Number(target && target.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return desiredPoint;

  const leashRadius = getLaneControlledCombatLeashRadius(attacker);
  const shouldAnchorClamp = shouldAnchorClampLaneControlledCombat(attacker, target);
  const pocketRadius = Math.max(
    stopDistance + ROUTE_SLOT_ROW_SPACING,
    Math.min(leashRadius * LANE_COMBAT_POCKET_RADIUS_SCALE, stopDistance + LANE_COMBAT_POCKET_RADIUS_PADDING)
  );
  const boundedCenter = shouldAnchorClamp
    ? (clampPointToRadius(
      targetX,
      targetY,
      anchor.x,
      anchor.y,
      Math.max(0, leashRadius - pocketRadius)
    ) || { x: targetX, y: targetY })
    : { x: targetX, y: targetY };
  const offsetX = Number(desiredPoint.x) - targetX;
  const offsetY = Number(desiredPoint.y) - targetY;
  const offsetDistance = Math.sqrt((offsetX * offsetX) + (offsetY * offsetY));
  const boundedOffsetScale = offsetDistance > pocketRadius && offsetDistance > 0.001
    ? pocketRadius / offsetDistance
    : 1;

  const boundedPoint = shouldAnchorClamp
    ? clampPointToRadius(
      boundedCenter.x + (offsetX * boundedOffsetScale),
      boundedCenter.y + (offsetY * boundedOffsetScale),
      anchor.x,
      anchor.y,
      leashRadius
    )
    : {
      x: boundedCenter.x + (offsetX * boundedOffsetScale),
      y: boundedCenter.y + (offsetY * boundedOffsetScale),
    };
  return boundedPoint || desiredPoint;
}

function getCombatSlotArrivalTolerance(attacker, target) {
  const attackerRadius = getUnitContactRadius(attacker && attacker.type);
  const targetRadius = getUnitContactRadius(target && target.type);
  const combinedRadius = attackerRadius + targetRadius;
  return Math.max(0.35, Math.min(0.75, combinedRadius * 0.35));
}

function isUnitInCombatContact(lane, attacker, target) {
  if (!attacker || !target)
    return false;

  const distance = getWaveUnitTargetDistance(attacker, target);
  const stopDistance = getUnitStopDistance(attacker.type, target.type);
  if (!Number.isFinite(distance) || !Number.isFinite(stopDistance) || distance > stopDistance + CONTACT_SLOT_TOLERANCE)
    return false;

  if (!lane || !shouldUseLaneControlledSurroundSlots(attacker, target))
    return true;

  const slotPoint = getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance);
  const slotDistance = Math.hypot(
    Number(attacker.posX) - Number(slotPoint.x),
    Number(attacker.posY) - Number(slotPoint.y)
  );
  return Number.isFinite(slotDistance) && slotDistance <= getCombatSlotArrivalTolerance(attacker, target);
}

function shouldUseSimpleContactApproach(unit, target) {
  return false;
}

function isLaneControlledUnitOutsideDefenseZone(game, unit, target) {
  if (!isLaneControlledUnit(unit))
    return false;
  if (getLaneCommandStateForUnit(game, unit) !== LANE_COMMAND_STATES.DEFEND)
    return false;
  return !isTargetInsideLaneDefenseZone(game, unit, target);
}

function advanceLaneControlledUnitAlongRoute(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const speed = Number(unit.baseSpeed) || 0.18;
  const advanced = advanceRouteState(unit, speed);
  relaxUnitRouteOffsets(unit, speed);
  setUnitRouteSnapshotState(unit);
  unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  return advanced;
}

function moveLaneControlledUnitToAnchor(unit) {
  if (!unit)
    return false;

  const waypointTargetX = Number(unit.currentWaypointTargetX);
  const waypointTargetY = Number(unit.currentWaypointTargetY);
  const anchorTargetX = Number(unit.anchorTargetX);
  const anchorTargetY = Number(unit.anchorTargetY);
  const speed = Number(unit.baseSpeed) || 0.18;
  const useLooseHoldTarget = USE_PER_UNIT_ANCHOR_SLOTS
    && Number.isFinite(anchorTargetX)
    && Number.isFinite(anchorTargetY);
  const targetPoint = {
    x: useLooseHoldTarget
      ? anchorTargetX
      : (USE_PER_UNIT_ANCHOR_SLOTS && Number.isFinite(waypointTargetX)
        ? waypointTargetX
        : Number(unit.anchorTargetX)),
    y: useLooseHoldTarget
      ? anchorTargetY
      : (USE_PER_UNIT_ANCHOR_SLOTS && Number.isFinite(waypointTargetY)
        ? waypointTargetY
        : Number(unit.anchorTargetY)),
  };
  if (!Number.isFinite(targetPoint.x) || !Number.isFinite(targetPoint.y))
    return false;

  const projection = projectPointOntoRouteSegments(unit.routeSegments, targetPoint);
  const currentProgress = computeUnitRoutePathIndex(unit);
  if (projection && Math.abs((projection.routeProgress || 0) - currentProgress) > LANE_ANCHOR_APPROACH_PROGRESS_TOLERANCE) {
    const direction = projection.routeProgress > currentProgress ? 1 : -1;
    advanceRouteState(unit, direction * speed);
    relaxUnitRouteOffsets(unit, speed);
    setUnitRouteSnapshotState(unit);
    unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
    return true;
  }

  const dx = Number(unit.posX) - targetPoint.x;
  const dy = Number(unit.posY) - targetPoint.y;
  const distanceToAnchor = Math.sqrt((dx * dx) + (dy * dy));
  if (USE_PER_UNIT_ANCHOR_SLOTS) {
    const holdRadius = useLooseHoldTarget
      ? 0.28
      : Math.max(
        1.1,
        Math.min(2.25, (Number(unit.anchorHoldRadius) || ROUTE_SLOT_ROW_SPACING) * 0.9)
      );
    if (distanceToAnchor <= holdRadius) {
      unit.movementMode = UNIT_MOVEMENT_MODES.ANCHOR_JOIN;
      return false;
    }
  } else if (distanceToAnchor <= LANE_ANCHOR_ARRIVAL_DEAD_ZONE) {
    unit.posX = targetPoint.x;
    unit.posY = targetPoint.y;
    syncUnitRouteStateToWorldPosition(unit);
    unit.movementMode = UNIT_MOVEMENT_MODES.ANCHOR_JOIN;
    return false;
  }

  moveTowardPoint2D(unit, targetPoint.x, targetPoint.y, speed, -64, 64, -64, 64);
  syncUnitRouteStateToWorldPosition(unit);
  const nextDx = Number(unit.posX) - targetPoint.x;
  const nextDy = Number(unit.posY) - targetPoint.y;
  unit.movementMode = Math.sqrt((nextDx * nextDx) + (nextDy * nextDy)) <= LANE_ANCHOR_JOIN_DISTANCE
    ? UNIT_MOVEMENT_MODES.ANCHOR_JOIN
    : UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR;
  return true;
}

function getPairSpacing(a, b, fallback) {
  let spacing = Math.max(
    fallback,
    getUnitContactRadius(a && a.type) + getUnitContactRadius(b && b.type)
  );

  const leftTargetId = a && a.combatTarget && a.combatTarget.unitId
    ? a.combatTarget.unitId
    : (a ? a.combatTargetId : null);
  const rightTargetId = b && b.combatTarget && b.combatTarget.unitId
    ? b.combatTarget.unitId
    : (b ? b.combatTargetId : null);
  const sharedTargetSpacing = !!(
    USE_PER_UNIT_ANCHOR_SLOTS
    && leftTargetId
    && rightTargetId
    && leftTargetId === rightTargetId
    && getUnitAttackRange(a && a.type) <= 2.0
    && getUnitAttackRange(b && b.type) <= 2.0
  );
  if (sharedTargetSpacing)
    spacing = Math.max(spacing, getUnitContactRadius(a && a.type) + getUnitContactRadius(b && b.type) + 0.20);

  return spacing;
}

function tryResolveSettledAnchorPairSpacing(a, b, minSpacing, fallbackSpacing) {
  if (!isLaneControlledUnitSettledAtAnchor(a) || !isLaneControlledUnitSettledAtAnchor(b))
    return null;
  if (!Number.isFinite(Number(a && a.anchorTargetX)) || !Number.isFinite(Number(a && a.anchorTargetY)))
    return null;
  if (!Number.isFinite(Number(b && b.anchorTargetX)) || !Number.isFinite(Number(b && b.anchorTargetY)))
    return null;

  const anchorDx = Number(b.anchorTargetX) - Number(a.anchorTargetX);
  const anchorDy = Number(b.anchorTargetY) - Number(a.anchorTargetY);
  const anchorDistance = Math.sqrt((anchorDx * anchorDx) + (anchorDy * anchorDy));
  if (!Number.isFinite(anchorDistance) || anchorDistance <= 0)
    return null;

  const settledSlotSpacing = Math.max(
    minSpacing,
    anchorDistance - LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK
  );
  return Math.min(fallbackSpacing, settledSlotSpacing);
}

function isLaneControlledUnitSettledAtAnchor(unit) {
  if (!isLaneControlledUnit(unit))
    return false;
  if (unit.combatTarget || unit.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
    return false;

  const anchorX = Number.isFinite(Number(unit && unit.anchorTargetX))
    ? Number(unit.anchorTargetX)
    : Number(unit && unit.anchorCenterX);
  const anchorY = Number.isFinite(Number(unit && unit.anchorTargetY))
    ? Number(unit.anchorTargetY)
    : Number(unit && unit.anchorCenterY);
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY))
    return false;

  const dx = Number(unit.posX) - anchorX;
  const dy = Number(unit.posY) - anchorY;
  if (!Number.isFinite(dx) || !Number.isFinite(dy))
    return false;
  return Math.sqrt((dx * dx) + (dy * dy)) <= LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE;
}

function getUnitAnchorLateralAxis(unit) {
  const axisX = Number(unit && unit.anchorLateralX);
  const axisY = Number(unit && unit.anchorLateralY);
  const magnitude = Math.sqrt((axisX * axisX) + (axisY * axisY));
  if (!Number.isFinite(axisX) || !Number.isFinite(axisY) || magnitude < 0.001)
    return null;
  return {
    x: axisX / magnitude,
    y: axisY / magnitude,
  };
}

function clampLaneControlledUnitToAnchorDrift(unit, minX, maxX, minY, maxY) {
  if (!isLaneControlledUnitSettledAtAnchor(unit))
    return false;
  if (!Number.isFinite(Number(unit && unit.anchorTargetX)) || !Number.isFinite(Number(unit && unit.anchorTargetY)))
    return false;

  const lateralAxis = getUnitAnchorLateralAxis(unit);
  if (!lateralAxis)
    return false;

  const anchorX = Number(unit.anchorTargetX);
  const anchorY = Number(unit.anchorTargetY);
  const lateralOffset = ((Number(unit.posX) - anchorX) * lateralAxis.x)
    + ((Number(unit.posY) - anchorY) * lateralAxis.y);
  const clampedOffset = Math.max(
    -LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET,
    Math.min(LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET, lateralOffset)
  );
  if (Math.abs(clampedOffset - lateralOffset) <= 0.0001)
    return false;

  unit.posX = Math.max(minX, Math.min(maxX, Number(unit.posX) + (lateralAxis.x * (clampedOffset - lateralOffset))));
  unit.posY = Math.max(minY, Math.min(maxY, Number(unit.posY) + (lateralAxis.y * (clampedOffset - lateralOffset))));
  syncMovedUnitPathState(unit);
  return true;
}

function getPairLateralAxis(a, b) {
  const axisA = getUnitAnchorLateralAxis(a);
  const axisB = getUnitAnchorLateralAxis(b);
  if (axisA && axisB) {
    const summedX = axisA.x + axisB.x;
    const summedY = axisA.y + axisB.y;
    const magnitude = Math.sqrt((summedX * summedX) + (summedY * summedY));
    if (magnitude >= 0.001) {
      return {
        x: summedX / magnitude,
        y: summedY / magnitude,
      };
    }
  }
  return axisA || axisB || { x: 1, y: 0 };
}

function shouldPreferLateralSpacing(a, b, dx, dy) {
  if (USE_PER_UNIT_ANCHOR_SLOTS && (isLaneControlledUnit(a) || isLaneControlledUnit(b)))
    return false;
  return !!(a && b && !a.isDefender && !b.isDefender && Math.abs(dy) >= Math.abs(dx));
}

function resolveExactOverlapAxis(a, b) {
  const seedSource = `${String(a && a.id || "")}|${String(b && b.id || "")}`;
  let hash = 0;
  for (let i = 0; i < seedSource.length; i += 1)
    hash = ((hash * 33) + seedSource.charCodeAt(i)) >>> 0;

  const angle = (hash % 360) * (Math.PI / 180);
  return {
    x: Math.cos(angle),
    y: Math.sin(angle),
  };
}

function getSharedTargetTangentialAxis(game, lane, a, b) {
  const leftTargetId = a && a.combatTarget && a.combatTarget.unitId
    ? a.combatTarget.unitId
    : (a ? a.combatTargetId : null);
  const rightTargetId = b && b.combatTarget && b.combatTarget.unitId
    ? b.combatTarget.unitId
    : (b ? b.combatTargetId : null);
  if (!leftTargetId || !rightTargetId || leftTargetId !== rightTargetId)
    return null;

  const liveTarget = resolveWaveCombatTarget(
    game,
    lane,
    a && a.combatTarget && a.combatTarget.unitId ? a.combatTarget : (b ? b.combatTarget : null)
  );
  const targetX = Number(liveTarget && liveTarget.posX);
  const targetY = Number(liveTarget && liveTarget.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return null;

  const leftDistance = getWaveUnitTargetDistance(a, liveTarget);
  const rightDistance = getWaveUnitTargetDistance(b, liveTarget);
  const leftStopDistance = getUnitStopDistance(a && a.type, liveTarget && liveTarget.type);
  const rightStopDistance = getUnitStopDistance(b && b.type, liveTarget && liveTarget.type);
  const tangentialSpreadDistance = Math.max(
    2.0,
    Math.max(leftStopDistance, rightStopDistance) + 1.25
  );
  if (!Number.isFinite(leftDistance) || !Number.isFinite(rightDistance)
      || leftDistance > tangentialSpreadDistance
      || rightDistance > tangentialSpreadDistance) {
    return null;
  }

  const midX = (Number(a.posX) + Number(b.posX)) * 0.5;
  const midY = (Number(a.posY) + Number(b.posY)) * 0.5;
  const radialX = midX - targetX;
  const radialY = midY - targetY;
  const radialMagnitude = Math.sqrt((radialX * radialX) + (radialY * radialY));
  if (!Number.isFinite(radialMagnitude) || radialMagnitude < 0.001)
    return null;

  return {
    x: -radialY / radialMagnitude,
    y: radialX / radialMagnitude,
  };
}

function shouldApplySimpleLaneHostileCollision(game, a, b, distance) {
  if (!USE_PER_UNIT_ANCHOR_SLOTS)
    return false;
  if (!a || !b || !Number.isFinite(distance))
    return false;
  if ((a.combatTargetId && a.combatTargetId === b.id) || (b.combatTargetId && b.combatTargetId === a.id))
    return false;

  const laneControlled = isLaneControlledUnit(a)
    ? a
    : (isLaneControlledUnit(b) ? b : null);
  const other = laneControlled === a ? b : a;
  if (!laneControlled || !other)
    return false;
  if (!areRouteUnitsHostile(game, laneControlled, other))
    return false;
  if (!canLaneControlledUnitSeekCombat(game, laneControlled, other))
    return false;

  const stopDistance = getUnitStopDistance(laneControlled.type, other.type);
  const collisionWakeDistance = Math.max(2.4, stopDistance + 0.9);
  return distance <= collisionWakeDistance;
}

function applySeparation2D(game, lane, units, minSpacing, minX, maxX, minY, maxY) {
  for (let pass = 0; pass < 2; pass++) {
    for (let i = 0; i < units.length; i++) {
      for (let j = i + 1; j < units.length; j++) {
        const a = units[i], b = units[j];
        const simpleLaneSpacing = USE_PER_UNIT_ANCHOR_SLOTS
          && (isLaneControlledUnit(a) || isLaneControlledUnit(b));
        let dx = b.posX - a.posX;
        let dy = b.posY - a.posY;
        let d  = Math.sqrt(dx * dx + dy * dy);
        const activeCombatSpacing = !simpleLaneSpacing
          || shouldLaneControlledUnitFreeRoamInCombat(a)
          || shouldLaneControlledUnitFreeRoamInCombat(b)
          || shouldApplySimpleLaneHostileCollision(game, a, b, d);
        if (!activeCombatSpacing)
          continue;
        if (d < 0.001) {
          const overlapAxis = resolveExactOverlapAxis(a, b);
          dx = overlapAxis.x * 0.001;
          dy = overlapAxis.y * 0.001;
          d = 0.001;
        }
        let pairSpacing = getPairSpacing(a, b, minSpacing);
        if (d >= pairSpacing) continue;
        const settledA = isLaneControlledUnitSettledAtAnchor(a);
        const settledB = isLaneControlledUnitSettledAtAnchor(b);
        if (settledA && settledB) {
          const settledPairSpacing = tryResolveSettledAnchorPairSpacing(a, b, minSpacing, pairSpacing);
          if (Number.isFinite(settledPairSpacing))
            pairSpacing = settledPairSpacing;
        }
        if (d >= pairSpacing)
          continue;
        const separationScale = settledA && settledB
          ? LANE_ANCHOR_SETTLED_SEPARATION_SCALE
          : (settledA || settledB ? LANE_ANCHOR_MIXED_SEPARATION_SCALE : 1);
        const push = Math.min((pairSpacing - d) * SEP_DAMP, SEP_MAX_PUSH) * separationScale;
        if (push <= 0.0001)
          continue;
        const sharedTargetTangentialAxis = simpleLaneSpacing && activeCombatSpacing
          ? getSharedTargetTangentialAxis(game, lane, a, b)
          : null;
        if (sharedTargetTangentialAxis) {
          const projectionA = (Number(a.posX) * sharedTargetTangentialAxis.x) + (Number(a.posY) * sharedTargetTangentialAxis.y);
          const projectionB = (Number(b.posX) * sharedTargetTangentialAxis.x) + (Number(b.posY) * sharedTargetTangentialAxis.y);
          const chooseNegativeA = projectionA < projectionB || (Math.abs(projectionA - projectionB) < 0.001 && String(a.id) <= String(b.id));
          const negative = chooseNegativeA ? a : b;
          const positive = chooseNegativeA ? b : a;
          negative.posX = Math.max(minX, Math.min(maxX, negative.posX - (sharedTargetTangentialAxis.x * push)));
          negative.posY = Math.max(minY, Math.min(maxY, negative.posY - (sharedTargetTangentialAxis.y * push)));
          positive.posX = Math.max(minX, Math.min(maxX, positive.posX + (sharedTargetTangentialAxis.x * push)));
          positive.posY = Math.max(minY, Math.min(maxY, positive.posY + (sharedTargetTangentialAxis.y * push)));
          syncMovedUnitPathState(negative);
          syncMovedUnitPathState(positive);
          continue;
        }
        if (!simpleLaneSpacing && (shouldPreferLateralSpacing(a, b, dx, dy) || (settledA && settledB))) {
          const lateralAxis = getPairLateralAxis(a, b);
          const projectionA = (Number(a.posX) * lateralAxis.x) + (Number(a.posY) * lateralAxis.y);
          const projectionB = (Number(b.posX) * lateralAxis.x) + (Number(b.posY) * lateralAxis.y);
          const chooseNegativeA = projectionA < projectionB || (Math.abs(projectionA - projectionB) < 0.001 && String(a.id) <= String(b.id));
          const negative = chooseNegativeA ? a : b;
          const positive = chooseNegativeA ? b : a;
          negative.posX = Math.max(minX, Math.min(maxX, negative.posX - (lateralAxis.x * push)));
          negative.posY = Math.max(minY, Math.min(maxY, negative.posY - (lateralAxis.y * push)));
          positive.posX = Math.max(minX, Math.min(maxX, positive.posX + (lateralAxis.x * push)));
          positive.posY = Math.max(minY, Math.min(maxY, positive.posY + (lateralAxis.y * push)));
          syncMovedUnitPathState(negative);
          syncMovedUnitPathState(positive);
          if (negative.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
            clampLaneControlledUnitToCombatLeash(negative, minX, maxX, minY, maxY);
          if (positive.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
            clampLaneControlledUnitToCombatLeash(positive, minX, maxX, minY, maxY);
          if (!simpleLaneSpacing) {
            clampLaneControlledUnitToAnchorDrift(negative, minX, maxX, minY, maxY);
            clampLaneControlledUnitToAnchorDrift(positive, minX, maxX, minY, maxY);
          }
          continue;
        }
        a.posX = Math.max(minX, Math.min(maxX, a.posX - (dx / d) * push));
        a.posY = Math.max(minY, Math.min(maxY, a.posY - (dy / d) * push));
        syncMovedUnitPathState(a);
        if (a.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
          clampLaneControlledUnitToCombatLeash(a, minX, maxX, minY, maxY);
        if (!simpleLaneSpacing)
          clampLaneControlledUnitToAnchorDrift(a, minX, maxX, minY, maxY);
        b.posX = Math.max(minX, Math.min(maxX, b.posX + (dx / d) * push));
        b.posY = Math.max(minY, Math.min(maxY, b.posY + (dy / d) * push));
        syncMovedUnitPathState(b);
        if (b.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
          clampLaneControlledUnitToCombatLeash(b, minX, maxX, minY, maxY);
        if (!simpleLaneSpacing)
          clampLaneControlledUnitToAnchorDrift(b, minX, maxX, minY, maxY);
      }
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
  // route units should commit to nearby defenders before they are in attack range,
  // but the leash must stay inside the local fortress combat pocket. Using the
  // full lane height here caused mid-route units to aggro early, which in turn
  // made the client yank them into combat-space near the Town Core.
  return Math.max(getUnitAttackRange(typeKey) + ENGAGEMENT_RANGE_PADDING, DEFENDER_ENGAGEMENT_RANGE);
}

function isSplitZoneUnit(unit) {
  return Number(unit.posY) < GRID_H;
}

function isMergeZoneUnit(unit) {
  return !isSplitZoneUnit(unit);
}

function getWaveUnitTargetDistance(unit, target) {
  if (!unit || !target) return null;
  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  return Math.sqrt(dx * dx + dy * dy);
}

function getStructureTargetAcquisitionRange(unit, target) {
  if (!unit || !target)
    return 0;
  return getUnitStopDistance(unit.type, target.type) + STRUCTURE_TARGET_VICINITY_PADDING;
}

function findHostileRouteUnitTarget(game, lane, unit, requireAttackRange = false, options = null) {
  if (!game || !lane || !unit)
    return null;

  const directEngagementOnly = !!(options && options.directEngagementOnly);
  const defendSeek = isLaneControlledUnit(unit)
    && getLaneCommandStateForUnit(game, unit) === LANE_COMMAND_STATES.DEFEND;
  const defendAnchorState = defendSeek
    ? getLaneCommandAnchorStateForUnit(game, unit)
    : null;
  let best = null;
  let bestLaneIndex = -1;
  let bestDist = Infinity;
  let bestPreferenceScore = Infinity;

  for (const candidateLane of game.lanes || []) {
    if (!candidateLane || !Array.isArray(candidateLane.units))
      continue;

    for (const candidate of candidateLane.units) {
      if (!candidate || !canEngageRouteUnitTarget(game, unit, candidate))
        continue;

      const dx = Number(candidate.posX) - Number(unit.posX);
      const dy = Number(candidate.posY) - Number(unit.posY);
      const dist = Math.sqrt((dx * dx) + (dy * dy));
      const emergencyInteriorTarget = defendSeek
        && isTargetInsideHomeFortressEmergencyZone(game, unit, candidate);
      const baseEngagementRange = getUnitEngagementRange(unit.type);
      const rangeLimit = requireAttackRange
        ? getUnitStopDistance(unit.type, candidate.type) + CONTACT_SLOT_TOLERANCE
        : (directEngagementOnly
          ? baseEngagementRange
          : (emergencyInteriorTarget
            ? Math.max(baseEngagementRange, FORTRESS_INTERIOR_ASSAULT_RADIUS)
            : (defendSeek
              ? Math.max(
                baseEngagementRange,
                (Number(defendAnchorState && defendAnchorState.engagementRadius) || LANE_COMMAND_DEFENSE_RADIUS) + ROUTE_SLOT_ROW_SPACING
              )
              : baseEngagementRange))) + CONTACT_SLOT_TOLERANCE;
      if (dist > rangeLimit)
        continue;

      const preferenceScore = getRouteUnitTargetPreferenceScore(game, candidateLane, unit, candidate, requireAttackRange);
      if (requireAttackRange
          && isLaneControlledUnit(unit)
          && shouldUseLaneControlledSurroundSlots(unit, candidate)
          && preferenceScore > getCombatSlotArrivalTolerance(unit, candidate) + CONTACT_SLOT_TOLERANCE) {
        continue;
      }
      if (
        dist < bestDist
        || (Math.abs(dist - bestDist) <= 0.0001 && preferenceScore < bestPreferenceScore)
      ) {
        best = candidate;
        bestLaneIndex = candidateLane.laneIndex;
        bestDist = dist;
        bestPreferenceScore = preferenceScore;
      }
    }
  }

  return best
    ? {
      entity: best,
      laneIndex: bestLaneIndex,
      distance: bestDist,
      preferenceScore: bestPreferenceScore,
    }
    : null;
}

function getWaveUnitPreferredTarget(game, lane, unit) {
  const routeUnitInAttackRange = findHostileRouteUnitTarget(game, lane, unit, true);
  if (routeUnitInAttackRange) return { kind: "unit", entity: routeUnitInAttackRange.entity, laneIndex: routeUnitInAttackRange.laneIndex, reason: "route_unit_attack_range", preferenceScore: routeUnitInAttackRange.preferenceScore };

  const routeUnitInEngageRange = findHostileRouteUnitTarget(game, lane, unit, false);
  if (routeUnitInEngageRange) return { kind: "unit", entity: routeUnitInEngageRange.entity, laneIndex: routeUnitInEngageRange.laneIndex, reason: "route_unit_engage_range", preferenceScore: routeUnitInEngageRange.preferenceScore };

  return null;
}

function hasImmediateFollowThroughCombatTarget(game, lane, unit) {
  if (!game || !lane || !unit || !isLaneControlledUnit(unit))
    return false;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return false;

  const routeUnitInAttackRange = findHostileRouteUnitTarget(game, lane, unit, true);
  if (routeUnitInAttackRange)
    return true;

  return !!findHostileRouteUnitTarget(game, lane, unit, false, { directEngagementOnly: true });
}

function getLaneBlockingStructureTargets(lane, unit = null) {
  const targets = [];
  if (!lane)
    return targets;

  if (Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      const target = getFortressPadCombatTarget(lane, pad);
      if (!target)
        continue;
      targets.push(target);
    }
  }

  for (const siteDef of BARRACKS_SITE_DEFS) {
    const target = getBarracksSiteCombatTarget(lane, siteDef.barracksId);
    if (target)
      targets.push(target);
  }

  return targets;
}

function findBlockingStructureTarget(game, lane, unit) {
  if (!game || !unit)
    return null;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return null;

  let best = null;
  let bestDistance = Infinity;
  for (const candidateLane of game.lanes || []) {
    if (!candidateLane || candidateLane.eliminated || !isRouteUnitHostileToLane(game, candidateLane, unit))
      continue;
    const candidates = getLaneBlockingStructureTargets(candidateLane, unit);
    for (const candidate of candidates) {
      const dx = Number(candidate.posX) - Number(unit.posX);
      const dy = Number(candidate.posY) - Number(unit.posY);
      const straightDistance = Math.sqrt((dx * dx) + (dy * dy));
      const distanceLimit = getStructureTargetAcquisitionRange(unit, candidate);
      if (straightDistance > distanceLimit)
        continue;
      if (straightDistance < bestDistance) {
        best = candidate;
        bestDistance = straightDistance;
      }
    }
  }

  return best;
}

function traceWaveUnitTick(game, lane, unit, target, details = {}) {
  if (!ENABLE_WAVE_UNIT_TRACE) return;
  if (!game || !lane || !unit) return;
  const targetId = target ? (target.unitId || target.id || null) : null;
  const targetType = target
    ? (target.kind === "unit"
      ? "defender"
      : target.kind === "fortress_pad"
        ? (target.buildingType || target.type || "fortress_pad")
        : target.kind || null)
    : null;
  const distanceToTarget = target ? getWaveUnitTargetDistance(unit, target) : null;
  const shouldLog = !!(
    details.coreDamageApplied ||
    details.movementAdvanced ||
    targetId ||
    unit.combatState === WAVE_UNIT_STATES.COMBAT ||
    unit.posY >= GRID_H - 2
  );
  if (!shouldLog) return;

  log.info("[WaveUnitTrace] tick", {
    tick: game.tick,
    laneIndex: lane.laneIndex,
    unitId: unit.id,
    unitType: unit.type,
    routeType: unit.routeType || null,
    currentSegment: unit.currentSegment || null,
    segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
    state: unit.combatState || WAVE_UNIT_STATES.IDLE,
    movementMode: unit.movementMode || null,
    inCombat: unit.combatState === WAVE_UNIT_STATES.COMBAT,
    targetId,
    targetType,
    blockedByStructure: !!unit.blockedByStructure,
    blockedByStructureId: unit.blockedByStructureId || null,
    distanceToTarget: Number.isFinite(distanceToTarget) ? Number(distanceToTarget.toFixed(3)) : null,
    movementAdvanced: !!details.movementAdvanced,
    coreDamageApplied: !!details.coreDamageApplied,
    pathIdx: Number.isFinite(unit.pathIdx) ? Number(unit.pathIdx.toFixed(2)) : null,
    preferredTargetReason: details.preferredTargetReason || null,
  });
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
  attacker.attackPulse = (attacker.attackPulse || 0) + 1;
  attacker.atkCd = cdTk;
}

// ── Wave defense helpers ──────────────────────────────────────────────────────

function resolveWaveForRound(game, roundNumber) {
  const cfg = Array.isArray(game.waveConfig) ? game.waveConfig : [];
  const round = Math.max(1, Math.floor(Number(roundNumber) || 1));
  if (cfg.length === 0)
    return null;

  // Find exact wave row; if past the last, use last row with escalation.
  const exact = cfg.find(w => Number(w.wave_number) === round);
  if (exact) return exact;

  const last = cfg.reduce((a, b) => Number(a.wave_number) >= Number(b.wave_number) ? a : b);
  if (!last)
    return null;

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

function getUpcomingWaveNumber(game) {
  if (!game)
    return 1;

  const currentRound = Math.max(1, Math.floor(Number(game.roundNumber) || 1));
  return game.hasSpawnedWave
    ? currentRound + 1
    : currentRound;
}

function countRemainingWaveMobs(game) {
  if (!game)
    return 0;

  let remaining = 0;
  for (const lane of game.lanes || []) {
    if (!lane || lane.eliminated)
      continue;

    for (const unit of lane.spawnQueue || []) {
      if (unit && isScheduledWaveUnit(unit) && Number(unit.hp) > 0)
        remaining += 1;
    }

    for (const unit of lane.units || []) {
      if (unit && isScheduledWaveUnit(unit) && Number(unit.hp) > 0)
        remaining += 1;
    }
  }

  return remaining;
}

function isCurrentWaveComplete(game) {
  return countRemainingWaveMobs(game) <= 0;
}

function createLaneUpcomingWavePreview(game, lane, waveNumber = null) {
  const upcomingWaveNumber = Number.isInteger(waveNumber) && waveNumber > 0
    ? waveNumber
    : getUpcomingWaveNumber(game);
  const entriesByKey = new Map();

  const addEntry = ({
    source,
    unitType,
    archetypeKey = null,
    presentationKey = null,
    skinKey = null,
    count = 1,
    hpMult = 1,
    dmgMult = 1,
    speedMult = 1,
    sourceLaneIndex = -1,
    sourceBarracksId = null,
    isHero = false,
    heroKey = null,
    heroVisualStyleKey = null,
  }) => {
    if (!unitType)
      return;

    const normalizedCount = Math.max(0, Math.floor(Number(count) || 0));
    if (normalizedCount <= 0)
      return;

    const key = [
      source || "scheduled",
      String(unitType).trim().toLowerCase(),
      String(archetypeKey || "").trim().toLowerCase(),
      String(presentationKey || "").trim().toLowerCase(),
      String(skinKey || "").trim().toLowerCase(),
      Number(hpMult || 1).toFixed(3),
      Number(dmgMult || 1).toFixed(3),
      Number(speedMult || 1).toFixed(3),
      Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1,
      String(sourceBarracksId || "").trim().toLowerCase(),
      isHero ? "hero" : "unit",
      String(heroKey || "").trim().toLowerCase(),
    ].join("|");

    if (entriesByKey.has(key)) {
      entriesByKey.get(key).count += normalizedCount;
      return;
    }

    entriesByKey.set(key, {
      source: source || "scheduled",
      unitType: String(unitType).trim().toLowerCase(),
      archetypeKey: archetypeKey || null,
      presentationKey: presentationKey || null,
      skinKey: skinKey || null,
      count: normalizedCount,
      hpMult: Number(hpMult || 1),
      dmgMult: Number(dmgMult || 1),
      speedMult: Number(speedMult || 1),
      sourceLaneIndex: Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1,
      sourceBarracksId: sourceBarracksId || null,
      isHero: !!isHero,
      heroKey: heroKey || null,
      heroVisualStyleKey: heroVisualStyleKey || null,
    });
  };

  const scheduledWave = resolveWaveForRound(game, upcomingWaveNumber);
  if (scheduledWave) {
    addEntry({
      source: "scheduled",
      unitType: scheduledWave.unit_type,
      archetypeKey: null,
      presentationKey: null,
      count: scheduledWave.spawn_qty,
      hpMult: scheduledWave.hp_mult,
      dmgMult: scheduledWave.dmg_mult,
      speedMult: getEffectiveWaveEntrySpeedMult(game, lane, scheduledWave),
    });
  }

  const entries = Array.from(entriesByKey.values()).sort((a, b) => {
    if (a.source !== b.source)
      return a.source === "scheduled" ? -1 : 1;
    if (a.count !== b.count)
      return b.count - a.count;
    return String(a.unitType).localeCompare(String(b.unitType));
  });

  return {
    waveNumber: upcomingWaveNumber,
    totalUnits: entries.reduce((sum, entry) => sum + (entry.count || 0), 0),
    entries,
  };
}

function createLaneUpcomingWaveQueue(game, lane, maxCount = 4) {
  const safeCount = Math.max(1, Math.floor(Number(maxCount) || 1));
  const firstWaveNumber = getUpcomingWaveNumber(game);
  const queue = [];

  for (let i = 0; i < safeCount; i++) {
    const waveNumber = firstWaveNumber + i;
    queue.push(createLaneUpcomingWavePreview(game, lane, waveNumber));
  }

  return queue;
}

function _spawnWaveUnit(game, lane, waveDef, options = {}) {
  const unitType = waveDef.unit_type;
  const def = resolveUnitDef(unitType);
  if (!def) return;
  if (lane.units.length + lane.spawnQueue.length >= MAX_UNITS_PER_LANE) return;
  const spawnValidation = validateSpawnDefinition(game, lane, waveDef, options);
  if (!spawnValidation.ok) {
    log.error("[SpawnAudit][ServerQueue] rejected", {
      spawnType: spawnValidation.spawnType,
      reason: spawnValidation.reason,
      unitType,
      laneIndex: lane ? lane.laneIndex : null,
      sourceLaneIndex: waveDef && Number.isInteger(waveDef.sourceLaneIndex) ? waveDef.sourceLaneIndex : -1,
      sourceBarracksKey: normalizeBarracksSiteId(
        waveDef && (waveDef.sourceBarracksKey || waveDef.sourceBarracksId)
      ),
      sourceTeam: waveDef && waveDef.sourceTeam ? waveDef.sourceTeam : null,
      requestedSpawnIndex: waveDef && waveDef.spawnIndex,
    });
    return;
  }
  const effectiveSpeedMult = getEffectiveWaveEntrySpeedMult(game, lane, waveDef);
  const hp  = Math.ceil(def.hp    * Number(waveDef.hp_mult    || 1));
  const dmg =           def.dmg   * Number(waveDef.dmg_mult   || 1);
  const spd =           getBaseCombatPathSpeed(unitType) * effectiveSpeedMult;
  log.info("[SpawnAudit][ServerQueue] queued", {
    spawnType: spawnValidation.spawnType,
    unitType,
    laneIndex: lane.laneIndex,
    team: lane.team || null,
    sourceLaneIndex: spawnValidation.sourceLaneIndex,
    sourceTeam: spawnValidation.sourceTeam,
    sourceBarracksKey: spawnValidation.sourceBarracksKey,
    requestedSpawnKey: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
      ? `lane:${lane.laneIndex}:wave_origin`
      : `lane:${spawnValidation.sourceLaneIndex}:barracks:${spawnValidation.sourceBarracksKey}`,
    resolvedMarkerName: `server_queue_${spawnValidation.spawnType}`,
    resolvedLogicalPosition: spawnValidation.logicalPos,
    requestedSpawnIndex: spawnValidation.requestedSpawnIndex,
    resolvedSpawnIndex: spawnValidation.resolvedSpawnIndex,
    fallbackUsed: false,
    authoring: "server",
  });
  const ownerLaneIndex = spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
    ? -1
    : spawnValidation.sourceLaneIndex;
  const sourceLane = getSourceLane(game, spawnValidation.sourceLaneIndex);
  const objectiveLaneIndex = spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
    ? lane.laneIndex
    : getLaneCommandRouteObjectiveLaneIndex(game, sourceLane);
  const queuedUnit = {
    id: `wu${game.nextUnitId++}`,
    unitId: null,
    targetLaneIndex: lane.laneIndex,
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    objectiveLaneIndex,
    sourceLaneIndex: spawnValidation.sourceLaneIndex,
    sourceTeam: spawnValidation.sourceTeam,
    sourceBarracksKey: spawnValidation.sourceBarracksKey,
    sourceBarracksId: spawnValidation.sourceBarracksKey,
    barracksId: spawnValidation.sourceBarracksKey,
    spawnSourceType: spawnValidation.spawnType,
    allegianceKey: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
      ? ALLEGIANCE_KEYS.DUNGEON
      : resolveLaneAllegianceKey(getSourceLane(game, spawnValidation.sourceLaneIndex)),
    type: unitType,
    unitTypeKey: unitType,
    archetypeKey: waveDef.archetypeKey || (isFortArchetypeKey(unitType) ? unitType : null),
    presentationKey: waveDef.presentationKey || null,
    catalogUnitKey: resolveGameplayCatalogUnitKey(unitType, waveDef.presentationKey || DEFAULT_FORT_PRESENTATION_KEY),
    skinKey: waveDef.skinKey || null,
    isHero: !!waveDef.isHero,
    heroKey: waveDef.heroKey || null,
    heroVisualStyleKey: waveDef.heroVisualStyleKey || null,
    rosterKey: waveDef.rosterKey || null,
    role: waveDef.role || null,
    combatRole: waveDef.combatRole || null,
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
    stance: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE ? UNIT_STANCES.ATTACK : null,
    pathContractType: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
      ? PATH_CONTRACT_TYPES.WAVE_LANE
      : null,
    isWaveUnit: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE,
    isDefender: false,
    combatTarget: null,
    combatTargetId: null,
    combatTargetLockedUntilTick: 0,
    currentTargetId: null,
    regroupUntilTick: 0,
    combatState: WAVE_UNIT_STATES.IDLE,
    state: WAVE_UNIT_STATES.IDLE,
    movementMode: UNIT_MOVEMENT_MODES.LANE_TRAVEL,
    hasBreachedTownCore: false,
    spawnIndex: spawnValidation.resolvedSpawnIndex,  // position in the authored wave spawn rectangle
    spawnLogicalPos: spawnValidation.logicalPos,
  };
  applyCanonicalUnitMirrors(game, lane, queuedUnit);
  lane.spawnQueue.push(queuedUnit);
}

function finalizeCompletedWave(game) {
  if (!game || !game.hasSpawnedWave) return;

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
}

function resetWaveIntervalState(game) {
  if (!game) return;
  for (const lane of game.lanes) {
    lane.leakCountThisRound = 0;
    lane.lifeLossThisRound = 0;
    lane.sendCountThisRound = 0;
    lane.sendSpendThisRound = 0;
    lane.buildSpendThisRound = 0;
  }
}

function spawnScheduledWave(game) {
  if (!game) return;

  const nextRoundNumber = getUpcomingWaveNumber(game);
  const waveDef = resolveWaveForRound(game, nextRoundNumber);
  if (!waveDef)
    return false;

  if (game.hasSpawnedWave)
    finalizeCompletedWave(game);

  game.roundNumber = nextRoundNumber;

  resetWaveIntervalState(game);
  const waveSizes = {};
  for (const lane of game.lanes) {
    if (lane.eliminated) continue;
    waveSizes[lane.laneIndex] = waveDef.spawn_qty;
    for (let i = 0; i < waveDef.spawn_qty; i++) _spawnWaveUnit(game, lane, waveDef);
  }

  game.hasSpawnedWave = true;
  game.lastWaveSpawnTick = game.tick;
  game._pendingEvents.push({
    type: "ml_wave_start",
    roundNumber: game.roundNumber,
    waveSizes,
  });
  return true;
}

function grantScheduledIncome(game) {
  if (!game) return;
  const interval = Math.max(1, Math.floor(Number(game.incomeIntervalTicks) || INCOME_INTERVAL_TICKS));
  if (!Number.isInteger(game.nextIncomeTick) || game.nextIncomeTick <= 0)
    game.nextIncomeTick = game.tick + interval;

  while (game.tick >= game.nextIncomeTick) {
    for (const lane of game.lanes) {
      if (lane && !lane.eliminated)
        lane.gold += lane.income;
    }
    game.nextIncomeTick += interval;
  }
}

function runScheduledBarracksSends(game) {
  if (!game) return;
  for (const lane of game.lanes) {
    if (!lane || lane.eliminated)
      continue;

    ensureBarracksSiteStates(lane, game);
    for (const siteDef of BARRACKS_SITE_DEFS) {
      const descriptor = describeBarracksSite(game, lane, siteDef.barracksId);
      if (!descriptor || !descriptor.isBuilt)
        continue;

      const interval = Math.max(1, descriptor.sendIntervalTicks);
      let siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
      if (!siteState)
        continue;

      if (!Number.isInteger(siteState.nextSendTick) || siteState.nextSendTick <= 0)
        siteState.nextSendTick = game.tick + interval;

      while (siteState != null && game.tick >= siteState.nextSendTick) {
        const siteSnapshot = createBarracksSiteSnapshot(game, lane, siteDef.barracksId);
        console.log(
          `[BarracksTrace][ServerTimer] tick=${game.tick} sourceLane=${lane.laneIndex} ` +
          `barracksId='${siteDef.barracksId}' built=${siteSnapshot ? siteSnapshot.isBuilt : false} ` +
          `roster=${summarizeBarracksSiteRosterEntries(siteSnapshot && siteSnapshot.roster)}`);
        const spawnedCount = spawnScheduledBarracksRoster(game, lane, siteDef.barracksId);
        console.log(
          `[BarracksTrace][ServerTimer] tick=${game.tick} sourceLane=${lane.laneIndex} ` +
          `barracksId='${siteDef.barracksId}' spawnedCount=${spawnedCount}`);
        siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
        if (siteState != null)
            siteState.nextSendTick += interval;
      }
    }
  }
}

function runScheduledWaves(game) {
  if (!game) return;
  const interval = Math.max(1, Math.floor(Number(game.waveIntervalTicks) || WAVE_TIMER_TICKS));
  if (!Number.isInteger(game.nextWaveTick) || game.nextWaveTick <= 0)
    game.nextWaveTick = game.tick + interval;

  while (game.tick >= game.nextWaveTick) {
    spawnScheduledWave(game);
    game.nextWaveTick += interval;
  }
}

function startNextWaveNow(game) {
  if (!game || game.phase !== "playing")
    return false;

  if (!isCurrentWaveComplete(game))
    return false;

  const interval = Math.max(1, Math.floor(Number(game.waveIntervalTicks) || WAVE_TIMER_TICKS));
  const spawned = spawnScheduledWave(game);
  if (spawned)
    game.nextWaveTick = Math.floor(Number(game.tick) || 0) + interval;
  return spawned;
}

function mlTick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;
  if (!game.startedAt) game.startedAt = Date.now();
  grantScheduledIncome(game);
  runScheduledWaves(game);
  runScheduledBarracksSends(game);
  syncLaneCommandAssignments(game);
  game.roundState = "combat";
  game.roundStateTicks = Number.isInteger(game.lastWaveSpawnTick)
    ? Math.max(0, game.tick - game.lastWaveSpawnTick)
    : game.tick;

  for (const lane of game.lanes) {
    const laneHasActiveOccupiers = laneHasOccupyingForces(lane);
    if (lane.eliminated && !laneHasActiveOccupiers) continue;
    const laneActive = !lane.eliminated;

    // Drain spawn queue — place each unit at a unique position in a rectangle
    // so the whole wave arrives at once but spread across the grid width.
    // col = spawnIndex % GRID_W, row = floor(spawnIndex / GRID_W)
    while (lane.spawnQueue.length > 0) {
      const unit = lane.spawnQueue.shift();
      const idx  = unit.spawnIndex ?? 0;
      const spawnLogicalPos = unit.spawnLogicalPos || {
        x: idx % GRID_W,
        y: Math.floor(idx / GRID_W),
      };
      const routeInit = initializeMovingUnitRouteState(game, lane, unit, spawnLogicalPos);
      if (!routeInit || !routeInit.ok) {
        log.error("[SpawnAudit][ServerLive] rejected", {
          reason: routeInit && routeInit.reason ? routeInit.reason : "Route initialization failed",
          unitId: unit && unit.id || null,
          unitType: unit && unit.type || null,
          targetLaneIndex: lane.laneIndex,
          sourceLaneIndex: Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1,
          sourceBarracksKey: unit && (unit.sourceBarracksKey || unit.sourceBarracksId) || null,
          spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
          requestedLogicalPosition: spawnLogicalPos,
        });
        continue;
      }
      unit.ownerLane = Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0
        ? unit.sourceLaneIndex
        : -1;
      unit.ownerLaneIndex = unit.ownerLane;
      applyCanonicalUnitMirrors(game, lane, unit);
      log.info("[SpawnAudit][ServerLive] materialized", {
        spawnType: resolveSpawnSourceTypeFromUnit(unit),
        unitId: unit.id,
        unitType: unit.type,
        targetLaneIndex: lane.laneIndex,
        targetTeam: lane.team || null,
        sourceLaneIndex: Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1,
        sourceTeam: unit.sourceTeam || null,
        sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
        routeType: unit.routeType,
        currentSegment: unit.currentSegment,
        segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
        resolvedMarkerName: "server_route_graph",
        resolvedLogicalPosition: spawnLogicalPos,
        fallbackUsed: false,
        authoring: "server",
      });
      if (unit.sourceBarracksKey || unit.sourceBarracksId) {
        console.log(
          `[BarracksTrace][ServerSpawn] tick=${game.tick} targetLane=${lane.laneIndex} ` +
          `unit='${unit.id}' type='${unit.type}' barracksId='${unit.sourceBarracksKey || unit.sourceBarracksId}' ` +
          `sourceLane=${unit.sourceLaneIndex} sourceTeam='${unit.sourceTeam || ""}'`);
      }
      lane.units.push(unit);
      if (unit.abilities && unit.abilities.length > 0) {
        resolveAbilityHook(game, lane, unit, "onSpawn", {});
      }
    }
    for (const unit of lane.units) {
      applyCanonicalUnitMirrors(game, lane, unit);
    }

    // Decrement cooldowns
    for (const u of lane.units) {
      if (u.atkCd > 0) u.atkCd -= 1;
    }
          // Use raw float pathIdx for row distance — avoids Math.floor rounding units out of range.
    // ── Mobile defender AI ────────────────────────────────────────────────────
    // Wave unit movement, combat targeting, and Town Core assaults
    const dotDeadIds = new Set();

    for (const u of lane.units) {
      if (u.hp <= 0) continue;
      if (u.isDefender) continue;  // defenders handled above
      const startedTickWithCombatTarget = !!(u.combatTarget && u.combatTarget.unitId);
      const startedTickInCombat = u.combatState === WAVE_UNIT_STATES.COMBAT || u.routeState === WAVE_UNIT_STATES.COMBAT;
      const supportProfile = resolveUnitSupportProfile(u);
      const healerMode = supportProfile.isHealer;
      u.combatState = WAVE_UNIT_STATES.IDLE;
      u.routeState = WAVE_UNIT_STATES.IDLE;
      u.blockedByStructure = false;
      u.blockedByStructureId = null;
      u.blockedByStructureType = null;
      resolveStatuses(u, game.tick);
      if (u.hp <= 0) {
        u.combatState = WAVE_UNIT_STATES.DEAD;
        u.routeState = WAVE_UNIT_STATES.DEAD;
        dotDeadIds.add(u.id);
        traceWaveUnitTick(game, lane, u, null, {
          movementAdvanced: false,
          coreDamageApplied: false,
        });
        continue;
      }

      // ── Wave unit combat target tracking (mobile defenders) ─────────────────
      let preferredTarget = null;
      let resolvedCombatTarget = null;
      if (!healerMode) {
        resolvedCombatTarget = u.combatTarget && u.combatTarget.unitId
          ? resolveWaveCombatTarget(game, lane, u.combatTarget)
          : null;
        if (u.combatTarget && u.combatTarget.unitId) {
          const targetStillValid = !!resolvedCombatTarget && isUnitCombatTargetStillValid(game, lane, u, resolvedCombatTarget);
          if (!targetStillValid) {
            clearUnitCombatTarget(u, game.tick, {
              suppressRegroup: !resolvedCombatTarget && hasImmediateFollowThroughCombatTarget(game, lane, u),
            });
            resolvedCombatTarget = null;
          }
        }

        const canReacquireUnitTarget = !isLaneControlledUnitInRegroupWindow(u, game.tick);
        let directPreferredTarget = null;
        if (canReacquireUnitTarget && canLaneControlledUnitSeekCombat(game, u))
          directPreferredTarget = getWaveUnitPreferredTarget(game, lane, u);

        if (directPreferredTarget)
          preferredTarget = directPreferredTarget;

        if (preferredTarget && preferredTarget.kind === "unit") {
          const shouldAssignPreferredTarget = !resolvedCombatTarget
            || shouldSwitchCombatTarget(game, lane, u, resolvedCombatTarget, preferredTarget);
          if (shouldAssignPreferredTarget) {
            assignUnitCombatTarget(u, {
              unitId: preferredTarget.entity.id,
              kind: "unit",
              laneIndex: preferredTarget.laneIndex,
            }, game.tick);
            resolvedCombatTarget = resolveWaveCombatTarget(game, lane, u.combatTarget);
          }
        }

        if (!u.combatTarget) {
          const blockingTarget = findBlockingStructureTarget(game, lane, u);
          if (blockingTarget) {
            if (blockingTarget.buildingType === "town_core")
              markTownCoreBreach(game, lane, u, blockingTarget);
            u.blockedByStructure = true;
            u.blockedByStructureId = blockingTarget.unitId;
            u.blockedByStructureType = blockingTarget.buildingType || blockingTarget.type || null;
            assignUnitCombatTarget(u, {
              unitId: blockingTarget.unitId,
              kind: "fortress_pad",
              padId: blockingTarget.padId,
              laneIndex: Number.isInteger(blockingTarget.laneIndex) ? blockingTarget.laneIndex : lane.laneIndex,
            }, game.tick);
            resolvedCombatTarget = resolveWaveCombatTarget(game, lane, u.combatTarget);
          }
        }
      }

      let attackedTarget = false;
      let movementAdvanced = false;
      let coreDamageApplied = false;
      if (healerMode) {
        const healTarget = findFriendlyHealTarget(game, lane, u, supportProfile);
        if (healTarget) {
          const healRange = getUnitAttackRange(u.type);
          const stopDist = healRange + 0.15;
          const dist = dist2D(u, healTarget);
          assignUnitSupportTarget(u, {
            unitId: healTarget.id,
            kind: "unit",
            laneIndex: lane.laneIndex,
          });
          u.combatState = WAVE_UNIT_STATES.COMBAT;
          u.routeState = WAVE_UNIT_STATES.COMBAT;
          u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
          if (dist <= stopDist + CONTACT_SLOT_TOLERANCE) {
            if (u.atkCd <= 0) {
              const healAmount = Math.max(1, Math.round(supportProfile.healAmount || 1));
              const maxHp = Math.max(1, Number(healTarget.maxHp) || Number(healTarget.hp) || 1);
              healTarget.hp = Math.min(maxHp, (Number(healTarget.hp) || 0) + healAmount);
              u.attackPulse = (u.attackPulse || 0) + 1;
              u.atkCd = Math.max(1, Math.floor(Number(u.atkCdTicks) || 20));
              attackedTarget = true;
            } else {
              attackedTarget = true;
            }
          } else {
            if (shouldUseSimpleContactApproach(u, { kind: "unit" })) {
              moveTowardContact2D(u, healTarget, u.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
            } else {
              const slotPoint = getLaneControlledCombatPocketPoint(lane, u, healTarget, stopDist);
              moveTowardPoint2D(u, slotPoint.x, slotPoint.y, u.baseSpeed || 0.18, -64, 64, -64, 64);
            }
            if (!shouldLaneControlledUnitFreeRoamInCombat(u))
              clampLaneControlledUnitToCombatLeash(u, -64, 64, -64, 64, healTarget);
            u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
            movementAdvanced = true;
          }
        } else {
          clearUnitSupportTarget(u);
        }
      } else if (u.combatTarget && u.combatTarget.unitId) {
        let target = resolveWaveCombatTarget(game, lane, u.combatTarget);
        if (!target) {
          clearUnitCombatTarget(u, game.tick, {
            suppressRegroup: hasImmediateFollowThroughCombatTarget(game, lane, u),
          });
        } else if (target.kind === "unit") {
          u.combatState = WAVE_UNIT_STATES.COMBAT;
          u.routeState = WAVE_UNIT_STATES.COMBAT;
          u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
          const t = target.entity;
          u.combatTargetWorldX = Number(t && t.posX);
          u.combatTargetWorldY = Number(t && t.posY);
          const targetLane = Number.isInteger(target.laneIndex)
            ? getLaneByIndex(game, target.laneIndex)
            : resolveRouteUnitLane(game, t, lane);
          const stopDist = getUnitStopDistance(u.type, t.type);
          const directContactDistance = getWaveUnitTargetDistance(u, t);
          const allowImmediateLaneContact = !!(
            isLaneControlledUnit(u)
            && !startedTickWithCombatTarget
            && Number.isFinite(directContactDistance)
            && directContactDistance <= stopDist + CONTACT_SLOT_TOLERANCE
          );
          const inCombatContact = allowImmediateLaneContact || isUnitInCombatContact(lane, u, t);
          if (inCombatContact) {
            if (u.atkCd <= 0) {
              const dmg = Math.max(1, Math.floor(Number(u.baseDmg) || Number(def && def.dmg) || 1));
              const cooldownTicks = Math.max(1, Math.floor(Number(u.atkCdTicks) || Number(def && def.atkCdTicks) || 20));
              t.hp = Math.max(0, t.hp - dmg);
              u.attackPulse = (u.attackPulse || 0) + 1;
              if (t.hp <= 0) {
                combatLog.logEvent(game, "route_unit_killed", {
                  unitId: t.id,
                  unitType: t.type,
                  lane: targetLane ? targetLane.laneIndex : null,
                  killedBy: u.id,
                  killedByType: u.type,
                  killedByLane: lane.laneIndex,
                });
                clearUnitCombatTarget(u, game.tick, {
                  suppressRegroup: hasImmediateFollowThroughCombatTarget(game, lane, u),
                });
              }
              u.atkCd = cooldownTicks;
              attackedTarget = true;
            } else {
              attackedTarget = true;
            }
          } else {
            if (shouldUseSimpleContactApproach(u, target)) {
              moveTowardSimpleContact2D(u, t, u.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
            } else {
              const slotPoint = getLaneControlledCombatPocketPoint(lane, u, t, stopDist);
              moveTowardPoint2D(u, slotPoint.x, slotPoint.y, u.baseSpeed || 0.18, -64, 64, -64, 64);
            }
            if (!shouldLaneControlledUnitFreeRoamInCombat(u))
              clampLaneControlledUnitToCombatLeash(u, -64, 64, -64, 64, t);
            u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
            movementAdvanced = true;
          }
        } else if (target.kind === "fortress_pad") {
          u.combatState = WAVE_UNIT_STATES.COMBAT;
          u.routeState = WAVE_UNIT_STATES.COMBAT;
          u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
          const dist = Math.sqrt(
            Math.pow(target.posX - u.posX, 2) +
            Math.pow(target.posY - u.posY, 2)
          );
          const stopDist = getUnitStopDistance(u.type, target.type);
          if (dist <= stopDist + CONTACT_SLOT_TOLERANCE) {
            if (u.atkCd <= 0) {
              const result = attackFortressPad(game, lane, u, target);
              if (result.destroyed)
                clearUnitCombatTarget(u, game.tick, { regroupUntilTick: game.tick });
              coreDamageApplied = result.damageApplied > 0;
              attackedTarget = true;
            } else {
              attackedTarget = true;
            }
          } else {
            const slotPoint = getLaneControlledCombatPocketPoint(lane, u, target, stopDist);
            moveTowardPoint2D(u, slotPoint.x, slotPoint.y, u.baseSpeed || 0.18, -64, 64, -64, 64);
            if (!shouldLaneControlledUnitFreeRoamInCombat(u))
              clampLaneControlledUnitToCombatLeash(u, -64, 64, -64, 64, target);
            u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
            movementAdvanced = true;
          }
        }
      }

      if (!u.combatTarget && !attackedTarget) {
        u.combatState = WAVE_UNIT_STATES.MOVING;
        u.routeState = WAVE_UNIT_STATES.MOVING;
        if (isLaneControlledUnit(u)) {
          if (startedTickWithCombatTarget || startedTickInCombat || u.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
            syncUnitRouteStateToWorldPosition(u);
          if (shouldLaneControlledUnitRouteMarch(u))
            movementAdvanced = advanceLaneControlledUnitAlongRoute(u);
          else
            movementAdvanced = moveLaneControlledUnitToAnchor(u);
          if (!movementAdvanced)
            u.movementMode = shouldLaneControlledUnitRouteMarch(u)
              ? UNIT_MOVEMENT_MODES.LANE_TRAVEL
              : UNIT_MOVEMENT_MODES.ANCHOR_JOIN;
        } else {
          u.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
          if (!Array.isArray(u.routeSegments) || u.routeSegments.length === 0) {
            if (!u._missingRouteLogged) {
              u._missingRouteLogged = true;
              log.error("[WaveUnitTrace] missing_route_contract", {
                tick: game.tick,
                laneIndex: lane.laneIndex,
                unitId: u.id,
                unitType: u.type,
                spawnSourceType: resolveSpawnSourceTypeFromUnit(u),
                sourceLaneIndex: Number.isInteger(u.sourceLaneIndex) ? u.sourceLaneIndex : -1,
                sourceBarracksKey: u.sourceBarracksKey || u.sourceBarracksId || null,
              });
            }
          } else {
            const routeSpeed = u.baseSpeed || 0.18;
            if (startedTickWithCombatTarget || startedTickInCombat)
              syncUnitRouteStateToWorldPosition(u);
            if (startedTickWithCombatTarget || startedTickInCombat) {
              u.routeRecoveringFromCombatTicks = Math.max(
                Number(u.routeRecoveringFromCombatTicks) || 0,
                WAVE_ROUTE_COMBAT_RECOVERY_TICKS
              );
            }
            if ((Number(u.routeRecoveringFromCombatTicks) || 0) > 0) {
              relaxUnitRouteOffsets(u, routeSpeed);
              u.routeRecoveringFromCombatTicks = Math.max(0, (Number(u.routeRecoveringFromCombatTicks) || 0) - 1);
            }
            advanceRouteState(u, routeSpeed);
            setUnitRouteSnapshotState(u);
            u._missingRouteLogged = false;
            if (ENABLE_WAVE_UNIT_TRACE) {
              log.info("[WaveUnitTrace] movement_progress", {
                tick: game.tick,
                laneIndex: lane.laneIndex,
                unitId: u.id,
                unitType: u.type,
                spawnSourceType: resolveSpawnSourceTypeFromUnit(u),
                pathId: buildRoutePathId(u.routeSegments),
                currentWaypointIndex: Math.max(0, Math.floor(Number(u.routeSegmentIndex) || 0)),
                nextWaypoint: resolveUnitNextWaypoint(u),
                movementState: u.routeState,
                segmentProgress: Number.isFinite(u.segmentProgress) ? Number(u.segmentProgress.toFixed(3)) : null,
                pathIdx: Number.isFinite(u.pathIdx) ? Number(u.pathIdx.toFixed(3)) : null,
                routeWorldX: Number.isFinite(u.routeWorldX) ? Number(u.routeWorldX.toFixed(3)) : null,
                routeWorldY: Number.isFinite(u.routeWorldY) ? Number(u.routeWorldY.toFixed(3)) : null,
              });
            }
          }
          movementAdvanced = true;
        }
      }

      const traceTarget = u.combatTarget ? resolveWaveCombatTarget(game, lane, u.combatTarget) : null;
      u.state = u.combatState || u.routeState || u.state || WAVE_UNIT_STATES.IDLE;
      u.currentTargetId = u.combatTargetId || (u.combatTarget && u.combatTarget.unitId) || null;
      traceWaveUnitTick(game, lane, u, traceTarget, {
        movementAdvanced,
        coreDamageApplied,
        preferredTargetReason: preferredTarget ? preferredTarget.reason : null,
      });
      continue;
    }

    const laneCollisionUnits = lane.units.filter((unit) =>
      unit
      && unit.hp > 0
      && !unit.isDefender
      && (
        isLaneControlledUnit(unit)
        || unit.combatTarget
        || unit.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE
        || unit.combatState === WAVE_UNIT_STATES.COMBAT
        || unit.routeState === WAVE_UNIT_STATES.COMBAT
      ));
    if (laneCollisionUnits.length > 1)
      applySeparation2D(game, lane, laneCollisionUnits, MIN_UNIT_SPACING, -64, 64, -64, 64);

    for (const unit of lane.units) {
      applyCanonicalUnitMirrors(game, lane, unit);
    }

    // Keep hot-path movement debug opt-in so local terminals do not stall under log spam.
    if (laneActive && ENABLE_WAVE_UNIT_TRACE && game.tick % 20 === 0 && lane.units.length > 0) {
      const waveUnits = lane.units.filter(u => !u.isDefender && u.hp > 0);
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
      if (isScheduledWaveUnit(u)) {
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
      game.finalGameOverReason = "all_town_cores_destroyed";
      game.finalGameOverDebug = {
        tick: game.tick,
        coreStates: buildTownCoreStateSummary(game),
      };
      log.warn("[TownCoreTrace] final game over triggered", {
        tick: game.tick,
        reason: game.finalGameOverReason,
        coreStates: game.finalGameOverDebug.coreStates,
      });
      game.phase = "ended";
      game.matchState = "final_game_over";
      game.winner = Number.isInteger(game.officialWinnerLane) ? game.officialWinnerLane : null;
    } else if (game.hasSpawnedWave && isCurrentWaveComplete(game)) {
      startNextWaveNow(game);
    }
  }
}

function createMLSnapshot(game) {
  return buildMLSnapshot(game, {
    WAVE_TIMER_TICKS,
    TEAM_HP_START,
    GRID_W,
    GRID_H,
    SPAWN_X,
    SPAWN_YG,
    SHARED_SUFFIX_LENGTH,
    BARRACKS_SITE_DEFS,
    ensureBarracksSiteStates,
    getBarracksSiteSendIntervalTicks,
    normalizeBarracksSiteId,
    describeBarracksSite,
    isScheduledWaveUnit,
    resolveSpawnSourceTypeFromUnit,
    buildRoutePathId,
    resolveUnitNextWaypoint,
    syncLegacyBarracksAggregate,
    getUnitTilePos,
    createFortressPadSnapshot,
    createBarracksSiteSnapshot,
    createLaneUpcomingWavePreview,
    createLaneUpcomingWaveQueue,
    createBarracksRosterSnapshot,
    createHeroRosterSnapshot,
    resolveLaneAllegianceKey,
    resolveUnitAllegianceKey,
    resolveUnitOwnerLaneIndex,
    resolveUnitTargetLaneIndex,
    resolveUnitPathContractType,
    resolveUnitSourceBarracksId,
    resolveUnitStance,
    LANE_COMMAND_STATES,
    UNIT_MOVEMENT_MODES,
    LANE_ANCHOR_ARRIVAL_DEAD_ZONE,
    CONTACT_SLOT_TOLERANCE,
    resolveWaveCombatTarget,
    getWaveUnitTargetDistance,
    getUnitStopDistance,
    isUnitInCombatContact,
  });
}

function createMLPublicConfig(options) {
  return buildMLPublicConfig(options, {
    TICK_HZ,
    INCOME_INTERVAL_TICKS,
    GRID_W,
    GRID_H,
    TOWER_MAX_LEVEL,
    TEAM_HP_START,
    BARRACKS_COST_BASE,
    BARRACKS_REQ_INCOME_BASE,
    BARRACKS_ROSTER_REFUND_PCT,
    BARRACKS_SEND_TIMER_TICKS,
    WAVE_TIMER_TICKS,
    ESCALATION_PER_EXTRA_ROUND,
    BASE_COMBAT_PATH_SPEED,
    BARRACKS_LEVEL_ONE_SPEED_MULT,
    SPEED_UPGRADE_STEP,
    FORTRESS_BUILDING_DEFS,
    FORTRESS_PAD_DEFS,
    BARRACKS_SITE_DEFS,
    BARRACKS_ROSTER_DEFS,
    HERO_ROSTER_DEFS,
    MARKET_ROSTER_DEFS,
    normalizeGameOptions,
    getMlRuntimeSettings,
    getAllUnitTypes,
    getMovingUnitDefMap,
    getFixedUnitDefMap,
    getBuildingBranchLabel,
    getFortressMaxTier,
    getFortressBuildCost,
    getBarracksRosterBuyCost,
    getBarracksRosterSellRefund,
    getBuildingDisplayName,
    getBuildingTierDisplayName,
    getBarracksRosterLockedReason,
    getHeroRosterLockedReason,
    getBarracksSiteTierRequirement,
    getBarracksSiteBuildCost,
    getBarracksSiteMaxLevel,
    resolveFortPresentationConfig,
    ROUTE_SEGMENT_POLYLINES,
    ROUTE_GRAPH_NODE_POSITIONS,
    getPadWorldPosition,
    getBarracksSiteWorldPosition,
    getWaveSpawnWorldPosition,
    getLaneNodeId,
    getWaveSpawnNodeId,
    getBarracksRouteStartNodeId,
    getLaneCombatAxes,
    getDefaultSlotDefinitions,
    defaultEnvironmentPlayerCount: FIXED_SLOT_LAYOUT.length,
    normalizeAllegianceKey,
    FRONT_GATE_COMBAT_OFFSET,
  });
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
  BARRACKS_SEND_TIMER_TICKS,
  WAVE_TIMER_TICKS,
  BUILD_PHASE_TICKS,
  TRANSITION_PHASE_TICKS,
  getBarracksLevelDef,
  resolveUnitDef,
  getMovingUnitDefMap,
  getFixedUnitDefMap,
  GRID_W,
  GRID_H,
  validateSpawnDefinition,
  createMLGame,
  applyMLAction,
  mlTick,
  startNextWaveNow,
  createMLSnapshot,
  createMLPublicConfig,
  resolveTowerDef,
  getLaneBuildValue,
  getUpcomingWaveNumber,
  countRemainingWaveMobs,
  isCurrentWaveComplete,
  resolveWaveForRound,
  ROUTE_TYPES,
  resolveTargetLaneForBarracksSend,
  buildRouteSegments,
  initializeMovingUnitRouteState,
  getLaneTownCoreCombatTarget,
  getUnitStopDistance,
  LANE_COMMAND_STATES,
  getLaneCommandState,
  createFortressPadSnapshot,
  createBarracksSiteSnapshot,
  createBarracksRosterSnapshot,
  createHeroRosterSnapshot,
  FORTRESS_BUILDING_DEFS,
  FORTRESS_PAD_DEFS,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  HERO_ROSTER_DEFS,
};





