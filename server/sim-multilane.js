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
const laneCommandSystem = require("./game/multilane/laneCommandSystem");
const spawnSystem = require("./game/multilane/spawnSystem");
const waveSystem = require("./game/multilane/waveSystem");
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
  return laneCommandSystem.resolveOuterLoopTargetLaneIndex
    ? laneCommandSystem.resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex, LANE_COMMAND_SYSTEM_DEPS)
    : null;
}

function resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex) {
  return laneCommandSystem.resolveCenterCrossTargetLaneIndex
    ? laneCommandSystem.resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex, LANE_COMMAND_SYSTEM_DEPS)
    : resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex);
}

function normalizeLaneCommandState(value) {
  return laneCommandSystem.normalizeLaneCommandState(value, LANE_COMMAND_SYSTEM_DEPS);
}

function isLaneCombatEnabledCommandState(commandState) {
  return laneCommandSystem.isLaneCombatEnabledCommandState(commandState, LANE_COMMAND_SYSTEM_DEPS);
}

function getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex) {
  return laneCommandSystem.getDefaultLaneObjectiveLaneIndex
    ? laneCommandSystem.getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex, LANE_COMMAND_SYSTEM_DEPS)
    : sourceLaneIndex;
}

function getLaneCommandState(lane) {
  return laneCommandSystem.getLaneCommandState(lane, LANE_COMMAND_SYSTEM_DEPS);
}

function getLaneCommandObjectiveLaneIndex(game, laneOrLaneIndex) {
  return laneCommandSystem.getLaneCommandObjectiveLaneIndex(game, laneOrLaneIndex, LANE_COMMAND_SYSTEM_DEPS);
}

function getLaneCommandRouteObjectiveLaneIndex(game, laneOrLaneIndex) {
  return laneCommandSystem.getLaneCommandRouteObjectiveLaneIndex(game, laneOrLaneIndex, LANE_COMMAND_SYSTEM_DEPS);
}

function getLaneCommandEngagementRadius(lane) {
  return laneCommandSystem.getLaneCommandEngagementRadius(lane, LANE_COMMAND_SYSTEM_DEPS);
}

function getLaneCommandAnchorProgress(lane) {
  return laneCommandSystem.getLaneCommandAnchorProgress(lane, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveLaneCommandContainerLaneIndex(game, lane) {
  return laneCommandSystem.resolveLaneCommandContainerLaneIndex(game, lane, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveRouteNodeLaneIndex(nodeId) {
  return laneCommandSystem.resolveRouteNodeLaneIndex(nodeId, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveLaneControlledUnitCurrentSegmentLaneIndex(unit) {
  return laneCommandSystem.resolveLaneControlledUnitCurrentSegmentLaneIndex(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit) {
  return laneCommandSystem.resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function buildLaneCommandCoreRouteSegments(sourceLaneIndex, objectiveLaneIndex) {
  return laneCommandSystem.buildLaneCommandCoreRouteSegments(sourceLaneIndex, objectiveLaneIndex, LANE_COMMAND_SYSTEM_DEPS);
}

function buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksId, objectiveLaneIndex) {
  return laneCommandSystem.buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksId, objectiveLaneIndex, LANE_COMMAND_SYSTEM_DEPS);
}

function buildLaneCommandAnchorSet(game, lane) {
  return laneCommandSystem.buildLaneCommandAnchorSet(game, lane, LANE_COMMAND_SYSTEM_DEPS);
}

function sampleLaneCommandAnchor(game, lane) {
  return laneCommandSystem.sampleLaneCommandAnchor(game, lane, LANE_COMMAND_SYSTEM_DEPS);
}

function normalizeLegacyDefenderUnit(game, fallbackLane, unit) {
  return laneCommandSystem.normalizeLegacyDefenderUnit(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function normalizeLegacyDefenderUnits(game) {
  return laneCommandSystem.normalizeLegacyDefenderUnits(game, LANE_COMMAND_SYSTEM_DEPS);
}

function isLaneControlledUnit(unit) {
  return laneCommandSystem.isLaneControlledUnit(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function getLaneCommandOwnerLane(game, unit) {
  return laneCommandSystem.getLaneCommandOwnerLane(game, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function getLaneCommandStateForUnit(game, unit) {
  return laneCommandSystem.getLaneCommandStateForUnit(game, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function isLaneCommandCombatEnabledForUnit(game, unit) {
  return laneCommandSystem.isLaneCommandCombatEnabledForUnit(game, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveTargetLaneForBarracksSend(game, sourceLaneIndex, barracksId) {
  return laneCommandSystem.resolveTargetLaneForBarracksSend(game, sourceLaneIndex, barracksId, LANE_COMMAND_SYSTEM_DEPS);
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
  return laneCommandSystem.relaxUnitRouteOffsets(unit, speed, LANE_COMMAND_SYSTEM_DEPS);
}

function setUnitRouteSnapshotState(unit) {
  return laneCommandSystem.setUnitRouteSnapshotState(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function computeUnitRoutePathIndex(unit) {
  return routeGraph.computeUnitRoutePathIndex(unit);
}

function sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset = 0, lateralOffset = 0) {
  return routeGraph.sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset, lateralOffset);
}

function resolveSpawnOriginForUnit(unit, targetLane) {
  return laneCommandSystem.resolveSpawnOriginForUnit
    ? laneCommandSystem.resolveSpawnOriginForUnit(unit, targetLane, LANE_COMMAND_SYSTEM_DEPS)
    : null;
}

function resolveRouteContractForUnit(game, targetLane, unit) {
  return laneCommandSystem.resolveRouteContractForUnit(game, targetLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveRedirectRouteContractForExistingLaneControlledUnit(game, currentLane, targetLane, unit) {
  return laneCommandSystem.resolveRedirectRouteContractForExistingLaneControlledUnit(
    game,
    currentLane,
    targetLane,
    unit,
    LANE_COMMAND_SYSTEM_DEPS
  );
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
  return laneCommandSystem.initializeMovingUnitRouteState(
    game,
    targetLane,
    unit,
    spawnLogicalPos,
    LANE_COMMAND_SYSTEM_DEPS
  );
}

function applyRouteContractToExistingUnit(unit, routeContract, currentPosition = null) {
  return laneCommandSystem.applyRouteContractToExistingUnit(
    unit,
    routeContract,
    currentPosition,
    LANE_COMMAND_SYSTEM_DEPS
  );
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
  return laneCommandSystem.syncUnitRouteStateToWorldPosition(unit, worldPosition, LANE_COMMAND_SYSTEM_DEPS);
}

function syncMovedUnitPathState(unit) {
  return laneCommandSystem.syncMovedUnitPathState(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveLaneAnchorColumns(unitCount) {
  return laneCommandSystem.resolveLaneAnchorColumns(unitCount, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveCenteredSlotOffset(columnIndex, columns) {
  return laneCommandSystem.resolveCenteredSlotOffset(columnIndex, columns, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveLaneControlledUnitSortKey(unit) {
  return laneCommandSystem.resolveLaneControlledUnitSortKey(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function normalizeCombatRole(value) {
  return laneCommandSystem.normalizeCombatRole(value, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitCombatRole(unit) {
  return laneCommandSystem.resolveUnitCombatRole(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveAnchorHoldDepthBias(combatRole) {
  return laneCommandSystem.resolveAnchorHoldDepthBias(combatRole, LANE_COMMAND_SYSTEM_DEPS);
}

function buildLaneAnchorSlot(anchorState, unit, slotIndex, unitCount) {
  return laneCommandSystem.buildLaneAnchorSlot(anchorState, unit, slotIndex, unitCount, LANE_COMMAND_SYSTEM_DEPS);
}

function computeLaneAnchorHoldRadius(anchorState, anchorSlots) {
  return laneCommandSystem.computeLaneAnchorHoldRadius(anchorState, anchorSlots, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitAnchorLeashRadius(unit, anchorHoldRadius) {
  return laneCommandSystem.resolveUnitAnchorLeashRadius(unit, anchorHoldRadius, LANE_COMMAND_SYSTEM_DEPS);
}

function buildAnchorWaypointTarget(anchorState) {
  return laneCommandSystem.buildAnchorWaypointTarget(anchorState, LANE_COMMAND_SYSTEM_DEPS);
}

function shouldKeepUnitAfterLaneDefeat(lane, unit) {
  return laneCommandSystem.shouldKeepUnitAfterLaneDefeat(lane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function laneHasOccupyingForces(lane) {
  return laneCommandSystem.laneHasOccupyingForces(lane, LANE_COMMAND_SYSTEM_DEPS);
}

function requeueLaneControlledUnit(targetLane, unit) {
  return laneCommandSystem.requeueLaneControlledUnit(targetLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function syncLaneCommandAssignments(game) {
  return laneCommandSystem.syncLaneCommandAssignments(game, LANE_COMMAND_SYSTEM_DEPS);
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
  return spawnSystem.resolveSpawnSourceTypeFromWaveDef(waveDef, SPAWN_SYSTEM_DEPS);
}

function resolveSpawnSourceTypeFromUnit(unit) {
  return spawnSystem.resolveSpawnSourceTypeFromUnit(unit, SPAWN_SYSTEM_DEPS);
}

function isScheduledWaveUnit(unit) {
  return spawnSystem.isScheduledWaveUnit(unit, SPAWN_SYSTEM_DEPS);
}

function resolveUnitSourceBarracksId(unit) {
  return laneCommandSystem.resolveUnitSourceBarracksId(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitTargetLaneIndex(game, fallbackLane, unit) {
  return laneCommandSystem.resolveUnitTargetLaneIndex(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitObjectiveLaneIndex(game, fallbackLane, unit) {
  return laneCommandSystem.resolveUnitObjectiveLaneIndex(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitOwnerLaneIndex(game, fallbackLane, unit) {
  return laneCommandSystem.resolveUnitOwnerLaneIndex(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitStance(game, fallbackLane, unit) {
  return laneCommandSystem.resolveUnitStance(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function mapRouteTypeToPathContractType(routeType, barracksId) {
  return laneCommandSystem.mapRouteTypeToPathContractType(routeType, barracksId, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitPathContractType(game, fallbackLane, unit) {
  return laneCommandSystem.resolveUnitPathContractType(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveUnitAllegianceKey(game, fallbackLane, unit) {
  return laneCommandSystem.resolveUnitAllegianceKey(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveLegacySourceTeamFromAllegianceKey(allegianceKey) {
  return laneCommandSystem.resolveLegacySourceTeamFromAllegianceKey(allegianceKey, LANE_COMMAND_SYSTEM_DEPS);
}

function applyCanonicalUnitMirrors(game, fallbackLane, unit) {
  return laneCommandSystem.applyCanonicalUnitMirrors(game, fallbackLane, unit, LANE_COMMAND_SYSTEM_DEPS);
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

function resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex) {
  return spawnSystem.resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex, SPAWN_SYSTEM_DEPS);
}

function validateSpawnDefinition(game, targetLane, waveDef, options = {}) {
  return spawnSystem.validateSpawnDefinition(game, targetLane, waveDef, options, SPAWN_SYSTEM_DEPS);
}

function getEffectiveWaveEntrySpeedMult(game, lane, waveDef) {
  return spawnSystem.getEffectiveWaveEntrySpeedMult(game, lane, waveDef, SPAWN_SYSTEM_DEPS);
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

const LANE_COMMAND_SYSTEM_DEPS = Object.freeze({
  log,
  getLaneByIndex,
  getSourceLane,
  isOpponentLane,
  getLaneTownCoreCombatTarget,
  getWaveSpawnWorldPosition,
  getBarracksSiteWorldPosition,
  resolveSpawnSourceTypeFromUnit,
  resolveSpawnLogicalPosition,
  normalizeBarracksSiteId,
  normalizeAllegianceKey,
  resolveLaneAllegianceKey,
  normalizeUnitStance,
  SPAWN_SOURCE_TYPES,
  LANE_COMMAND_STATES,
  UNIT_STANCES,
  PATH_CONTRACT_TYPES,
  UNIT_MOVEMENT_MODES,
  WAVE_UNIT_STATES,
  UNIT_COMBAT_ROLES,
  LANE_STANCE_ANCHOR_KINDS,
  ALLEGIANCE_KEYS,
  OPPOSING_LANE_INDEX,
  USE_PER_UNIT_ANCHOR_SLOTS,
  LANE_COMMAND_DEFENSE_RADIUS,
  LANE_COMMAND_COMBAT_LEASH,
  LANE_COMMAND_HOME_CONTAINER_PROGRESS,
  INSIDE_GATE_ANCHOR_OFFSET,
  OUTSIDE_GATE_ANCHOR_OFFSET,
  LANE_ANCHOR_MAX_COLUMNS,
  ROUTE_SLOT_COLUMN_SPACING,
  ROUTE_SLOT_ROW_SPACING,
  SPAWN_X,
  SPAWN_YG,
});

const SPAWN_SYSTEM_DEPS = Object.freeze({
  log,
  getSourceLane,
  getBarracksSpeedMult,
  normalizeAllegianceKey,
  normalizeBarracksSiteId,
  resolveLaneAllegianceKey,
  describeBarracksSite,
  getWaveSpawnWorldPosition,
  resolveUnitDef,
  getBaseCombatPathSpeed,
  getLaneCommandRouteObjectiveLaneIndex,
  resolveGameplayCatalogUnitKey,
  buildAbilitiesForUnitType,
  applyCanonicalUnitMirrors,
  isFortArchetypeKey,
  GRID_W,
  SPAWN_X,
  MAX_UNITS_PER_LANE,
  DEFAULT_FORT_PRESENTATION_KEY,
  SPAWN_SOURCE_TYPES,
  ALLEGIANCE_KEYS,
  UNIT_STANCES,
  PATH_CONTRACT_TYPES,
  UNIT_MOVEMENT_MODES,
  WAVE_UNIT_STATES,
});

const WAVE_SYSTEM_DEPS = Object.freeze({
  ESCALATION_PER_EXTRA_ROUND,
  INCOME_INTERVAL_TICKS,
  WAVE_TIMER_TICKS,
  TICK_HZ,
  BARRACKS_SITE_DEFS,
  isScheduledWaveUnit,
  getEffectiveWaveEntrySpeedMult,
  createRoundSnapshotLane,
  ensureBarracksSiteStates,
  describeBarracksSite,
  getBarracksSiteState,
  createBarracksSiteSnapshot,
  summarizeBarracksSiteRosterEntries,
  spawnScheduledBarracksRoster,
  spawnWaveUnit: _spawnWaveUnit,
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
  return waveSystem.resolveWaveForRound(game, roundNumber, WAVE_SYSTEM_DEPS);
}

function getUpcomingWaveNumber(game) {
  return waveSystem.getUpcomingWaveNumber(game, WAVE_SYSTEM_DEPS);
}

function countRemainingWaveMobs(game) {
  return waveSystem.countRemainingWaveMobs(game, WAVE_SYSTEM_DEPS);
}

function isCurrentWaveComplete(game) {
  return waveSystem.isCurrentWaveComplete(game, WAVE_SYSTEM_DEPS);
}

function createLaneUpcomingWavePreview(game, lane, waveNumber = null) {
  return waveSystem.createLaneUpcomingWavePreview(game, lane, waveNumber, WAVE_SYSTEM_DEPS);
}

function createLaneUpcomingWaveQueue(game, lane, maxCount = 4) {
  return waveSystem.createLaneUpcomingWaveQueue(game, lane, maxCount, WAVE_SYSTEM_DEPS);
}

function _spawnWaveUnit(game, lane, waveDef, options = {}) {
  return spawnSystem.spawnWaveUnit(game, lane, waveDef, options, SPAWN_SYSTEM_DEPS);
}

function finalizeCompletedWave(game) {
  return waveSystem.finalizeCompletedWave(game, WAVE_SYSTEM_DEPS);
}

function resetWaveIntervalState(game) {
  return waveSystem.resetWaveIntervalState(game, WAVE_SYSTEM_DEPS);
}

function spawnScheduledWave(game) {
  return waveSystem.spawnScheduledWave(game, WAVE_SYSTEM_DEPS);
}

function grantScheduledIncome(game) {
  return waveSystem.grantScheduledIncome(game, WAVE_SYSTEM_DEPS);
}

function runScheduledBarracksSends(game) {
  return waveSystem.runScheduledBarracksSends(game, WAVE_SYSTEM_DEPS);
}

function runScheduledWaves(game) {
  return waveSystem.runScheduledWaves(game, WAVE_SYSTEM_DEPS);
}

function startNextWaveNow(game) {
  return waveSystem.startNextWaveNow(game, WAVE_SYSTEM_DEPS);
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





