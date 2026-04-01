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
const catalogSystem = require("./game/multilane/catalogSystem");
const laneCommandSystem = require("./game/multilane/laneCommandSystem");
const gameRuntimeSystem = require("./game/multilane/gameRuntimeSystem");
const routeRuntimeSystem = require("./game/multilane/routeRuntimeSystem");
const spawnSystem = require("./game/multilane/spawnSystem");
const waveSystem = require("./game/multilane/waveSystem");
const combatSystem = require("./game/multilane/combatSystem");
const tickSystem = require("./game/multilane/tickSystem");
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

// Shared route runtime now lives in server/game/multilane/routeRuntimeSystem.js.
function normalize2D(vec) {
  return routeRuntimeSystem.normalize2D(vec);
}

function perpendicular2D(vec) {
  return routeRuntimeSystem.perpendicular2D(vec);
}

function getLaneNodeId(laneIndex) {
  return routeRuntimeSystem.getLaneNodeId(laneIndex);
}

function getWaveSpawnNodeId(laneIndex) {
  return routeRuntimeSystem.getWaveSpawnNodeId(laneIndex);
}

function getLaneCombatAxes(laneIndex) {
  return routeRuntimeSystem.getLaneCombatAxes(laneIndex);
}

function getBarracksRouteStartNodeId(laneIndex, barracksId) {
  return routeRuntimeSystem.getBarracksRouteStartNodeId(laneIndex, barracksId);
}

function getLaneCoreNodeIdForRouteNode(nodeId) {
  return routeRuntimeSystem.getLaneCoreNodeIdForRouteNode(nodeId);
}

function isBarracksRouteStartNode(nodeId) {
  return routeRuntimeSystem.isBarracksRouteStartNode(nodeId);
}

function getWaveSpawnWorldPosition(laneIndex) {
  return routeRuntimeSystem.getWaveSpawnWorldPosition(laneIndex);
}

function getPadWorldPosition(laneIndex, gridX, gridY) {
  return routeRuntimeSystem.getPadWorldPosition(laneIndex, gridX, gridY);
}

function getBarracksSiteWorldPosition(laneIndex, barracksId) {
  return routeRuntimeSystem.getBarracksSiteWorldPosition(laneIndex, barracksId);
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
  return routeRuntimeSystem.buildRouteSegments(routeType, sourceNodeId, targetNodeId);
}

function parseRouteSegmentId(segmentId) {
  return routeRuntimeSystem.parseRouteSegmentId(segmentId);
}

function getRouteLength(routeSegments) {
  return routeRuntimeSystem.getRouteLength(routeSegments);
}

function sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset = 0) {
  return routeRuntimeSystem.sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset);
}

function advanceRouteState(unit, deltaDistance) {
  return routeRuntimeSystem.advanceRouteState(unit, deltaDistance);
}

function relaxUnitRouteOffsets(unit, speed) {
  return routeRuntimeSystem.relaxUnitRouteOffsets(unit, speed, LANE_COMMAND_SYSTEM_DEPS);
}

function setUnitRouteSnapshotState(unit) {
  return routeRuntimeSystem.setUnitRouteSnapshotState(unit, LANE_COMMAND_SYSTEM_DEPS);
}

function computeUnitRoutePathIndex(unit) {
  return routeRuntimeSystem.computeUnitRoutePathIndex(unit);
}

function sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset = 0, lateralOffset = 0) {
  return routeRuntimeSystem.sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset, lateralOffset);
}

function resolveSpawnOriginForUnit(unit, targetLane) {
  return routeRuntimeSystem.resolveSpawnOriginForUnit(unit, targetLane, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveRouteContractForUnit(game, targetLane, unit) {
  return routeRuntimeSystem.resolveRouteContractForUnit(game, targetLane, unit, LANE_COMMAND_SYSTEM_DEPS);
}

function resolveRedirectRouteContractForExistingLaneControlledUnit(game, currentLane, targetLane, unit) {
  return routeRuntimeSystem.resolveRedirectRouteContractForExistingLaneControlledUnit(
    game,
    currentLane,
    targetLane,
    unit,
    LANE_COMMAND_SYSTEM_DEPS
  );
}

function initializeMovingUnitRouteState(game, targetLane, unit, spawnLogicalPos) {
  return routeRuntimeSystem.initializeMovingUnitRouteState(
    game,
    targetLane,
    unit,
    spawnLogicalPos,
    LANE_COMMAND_SYSTEM_DEPS
  );
}

function applyRouteContractToExistingUnit(unit, routeContract, currentPosition = null) {
  return routeRuntimeSystem.applyRouteContractToExistingUnit(
    unit,
    routeContract,
    currentPosition,
    LANE_COMMAND_SYSTEM_DEPS
  );
}

function getUnitForwardDirection(unit) {
  return routeRuntimeSystem.getUnitForwardDirection(unit);
}

function buildSampledPathFromSegments(routeSegments, sampleCount = 28) {
  return routeRuntimeSystem.buildSampledPathFromSegments(routeSegments, sampleCount);
}

function sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset = 0) {
  return routeRuntimeSystem.sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset);
}

function projectPointOntoPolyline(points, targetPoint) {
  return routeRuntimeSystem.projectPointOntoPolyline(points, targetPoint);
}

function projectPointOntoRouteSegments(routeSegments, targetPoint) {
  return routeRuntimeSystem.projectPointOntoRouteSegments(routeSegments, targetPoint);
}

function syncUnitRouteStateToWorldPosition(unit, worldPosition = null) {
  return routeRuntimeSystem.syncUnitRouteStateToWorldPosition(unit, worldPosition, LANE_COMMAND_SYSTEM_DEPS);
}

function syncMovedUnitPathState(unit) {
  return routeRuntimeSystem.syncMovedUnitPathState(unit, LANE_COMMAND_SYSTEM_DEPS);
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
  return catalogSystem.resolveFortPresentationConfig(
    archetypeKey,
    presentationKey,
    fallbackDisplayName,
    CATALOG_SYSTEM_DEPS
  );
}

function resolveGameplayCatalogUnitKey(unitKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  return catalogSystem.resolveGameplayCatalogUnitKey(unitKey, presentationKey, CATALOG_SYSTEM_DEPS);
}

/**
 * Translate raw DB ability params into the format expected by sim-core
 * _executeAbility. Converts named params (e.g. slow_pct, dps) to
 * internal names (speedMult, dmgPerTick).
 */
function translateAbilityParams(abilityKey, rawParams) {
  return catalogSystem.translateAbilityParams(abilityKey, rawParams, CATALOG_SYSTEM_DEPS);
}

/**
 * Build the abilities array for a unit/tower type from the DB-loaded unitType.
 * Returns [] if the type has no abilities or isn't in the DB.
 * @param {string} unitTypeKey
 * @returns {object[]} abilities in sim-core format
 */
function buildAbilitiesForUnitType(unitTypeKey) {
  return catalogSystem.buildAbilitiesForUnitType(unitTypeKey, CATALOG_SYSTEM_DEPS);
}

// ── DB-first unit/tower resolution ────────────────────────────────────────────

/**
 * Resolve a unit definition from the DB (authoritative).
 * Returns null if the unit type is unknown or fixed-only.
 */
function resolveUnitDef(key) {
  return catalogSystem.resolveUnitDef(key, CATALOG_SYSTEM_DEPS);
}

function resolveUnitSupportProfile(unit) {
  return catalogSystem.resolveUnitSupportProfile(unit, CATALOG_SYSTEM_DEPS);
}

/**
 * Resolve a tower definition from the DB (authoritative).
 * DB range is stored normalised to [0,1] × GRID_W.
 */
function resolveTowerDef(key) {
  return catalogSystem.resolveTowerDef(key, CATALOG_SYSTEM_DEPS);
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
  return routeRuntimeSystem.buildRoutePathId(routeSegments);
}

function resolveUnitNextWaypoint(unit) {
  return routeRuntimeSystem.resolveUnitNextWaypoint(unit);
}

function dot2D(a, b) {
  return routeRuntimeSystem.dot2D(a, b);
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

const CATALOG_SYSTEM_DEPS = Object.freeze({
  DEFAULT_FORT_PRESENTATION_KEY,
  resolveFortCatalogUnitKey,
  resolveFortDisplayName,
  resolveFortPortraitKey,
  resolveFortSkinKey,
  isFortArchetypeKey,
  getUnitType,
  getAllUnitTypes,
  TICK_HZ,
  GRID_W,
  GRID_H,
  SPLASH_RADIUS_TILES,
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

const COMBAT_SYSTEM_DEPS = Object.freeze({
  log,
  combatLog,
  syncMovedUnitPathState,
  isLaneControlledUnit,
  getUnitForwardDirection,
  moveTowardPoint2D,
  fireProjectile,
  getTowerStats,
  resolveUnitDef,
  resolveUnitCombatRole,
  isFortArchetypeKey,
  getLaneByIndex,
  getBarracksSiteCombatTarget,
  getFortressPadState,
  getLaneTownCorePad,
  getLaneTownCoreHp,
  getLaneTownCoreMaxHp,
  getLaneTownCoreCombatTarget,
  getFortressPadCombatTarget,
  applyFortressPadDamage,
  findRouteUnitById,
  canRouteUnitEngageTarget,
  canLaneControlledUnitSeekCombat,
  areRouteUnitsHostile,
  isLaneControlledUnitNearSharedCombat,
  resolveUnitAllegianceKey,
  areAllegiancesHostile,
  resolveRouteUnitFactionKey,
  normalizeLaneCommandState,
  getLaneControlledSharedCombatAnchor,
  shouldLaneControlledUnitFreeRoamInCombat,
  resolveLaneControlledUnitSortKey,
  getLaneCommandStateForUnit,
  getLaneCommandAnchorStateForUnit,
  canEngageRouteUnitTarget,
  isTargetInsideHomeFortressEmergencyZone,
  isRouteUnitHostileToLane,
  FORTRESS_BUILDING_DEFS,
  BARRACKS_SITE_DEFS,
  LANE_COMMAND_STATES,
  UNIT_COMBAT_ROLES,
  UNIT_MOVEMENT_MODES,
  WAVE_UNIT_STATES,
  USE_PER_UNIT_ANCHOR_SLOTS,
  ENABLE_WAVE_UNIT_TRACE,
  CONTACT_SLOT_TOLERANCE,
  LANE_COMMAND_COMBAT_LEASH,
  LANE_COMMAND_DEFENSE_RADIUS,
  ANCHOR_SUPPORT_TARGET_RADIUS,
  ROUTE_SLOT_ROW_SPACING,
  LANE_COMBAT_POCKET_RADIUS_SCALE,
  LANE_COMBAT_POCKET_RADIUS_PADDING,
  LANE_COMBAT_REGROUP_TICKS,
  LANE_COMBAT_SWITCH_DISTANCE_MARGIN,
  ENGAGEMENT_RANGE_PADDING,
  DEFENDER_ENGAGEMENT_RANGE,
  FORTRESS_INTERIOR_ASSAULT_RADIUS,
  STRUCTURE_TARGET_VICINITY_PADDING,
  ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY,
  GRID_H,
  SEP_DAMP,
  SEP_MAX_PUSH,
  LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE,
  LANE_ANCHOR_SETTLED_SEPARATION_SCALE,
  LANE_ANCHOR_MIXED_SEPARATION_SCALE,
  LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET,
  LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK,
});

const TICK_SYSTEM_DEPS = Object.freeze({
  log,
  combatLog,
  grantScheduledIncome,
  runScheduledWaves,
  runScheduledBarracksSends,
  syncLaneCommandAssignments,
  laneHasOccupyingForces,
  initializeMovingUnitRouteState,
  resolveSpawnSourceTypeFromUnit,
  applyCanonicalUnitMirrors,
  resolveAbilityHook,
  resolveStatuses,
  resolveUnitSupportProfile,
  traceWaveUnitTick,
  resolveWaveCombatTarget,
  isUnitCombatTargetStillValid,
  clearUnitCombatTarget,
  hasImmediateFollowThroughCombatTarget,
  isLaneControlledUnitInRegroupWindow,
  canLaneControlledUnitSeekCombat,
  getWaveUnitPreferredTarget,
  shouldSwitchCombatTarget,
  assignUnitCombatTarget,
  findBlockingStructureTarget,
  markTownCoreBreach,
  findFriendlyHealTarget,
  getUnitAttackRange,
  dist2D,
  assignUnitSupportTarget,
  shouldUseSimpleContactApproach,
  moveTowardContact2D,
  moveTowardSimpleContact2D,
  getLaneControlledCombatPocketPoint,
  moveTowardPoint2D,
  shouldLaneControlledUnitFreeRoamInCombat,
  clampLaneControlledUnitToCombatLeash,
  clearUnitSupportTarget,
  getLaneByIndex,
  resolveRouteUnitLane,
  getUnitStopDistance,
  getWaveUnitTargetDistance,
  isLaneControlledUnit,
  isUnitInCombatContact,
  resolveUnitDef,
  attackFortressPad,
  shouldLaneControlledUnitRouteMarch,
  syncUnitRouteStateToWorldPosition,
  advanceLaneControlledUnitAlongRoute,
  moveLaneControlledUnitToAnchor,
  relaxUnitRouteOffsets,
  advanceRouteState,
  setUnitRouteSnapshotState,
  buildRoutePathId,
  resolveUnitNextWaypoint,
  applySeparation2D,
  resolveProjectile,
  isScheduledWaveUnit,
  createRoundSnapshotLane,
  buildTownCoreStateSummary,
  isCurrentWaveComplete,
  startNextWaveNow,
  WAVE_UNIT_STATES,
  UNIT_MOVEMENT_MODES,
  CONTACT_SLOT_TOLERANCE,
  GRID_H,
  GRID_W,
  TICK_HZ,
  MIN_UNIT_SPACING,
  WAVE_ROUTE_COMBAT_RECOVERY_TICKS,
  ENABLE_WAVE_UNIT_TRACE,
});

const GAME_RUNTIME_SYSTEM_DEPS = Object.freeze({
  log,
  mulberry32,
  getMlRuntimeSettings,
  normalizeAllegianceKey,
  buildRouteSegments,
  getWaveSpawnNodeId,
  getLaneNodeId,
  buildSampledPathFromSegments,
  getBarracksLevelDef,
  createFortressPadStates,
  createBarracksSiteStates,
  createBarracksSiteRosterCounts,
  seedStartingCombatTestMilitia,
  recomputeTeamHpState,
  getBarracksSiteCounts,
  getBarracksRosterBuyCost,
  resolveLaneAllegianceKey,
  areAllegiancesHostile,
  normalizeLaneCommandState,
  getLaneCommandAnchorProgress,
  getLaneCommandObjectiveLaneIndex,
  isLaneCombatEnabledCommandState,
  getLaneCommandEngagementRadius,
  syncLaneCommandAssignments,
  applyFortressBuildOnPad,
  getFortressPadByBuildingType,
  applyFortressUpgrade,
  normalizeBarracksSiteId,
  applyBarracksSiteBuildAction,
  applyBarracksSiteUpgradeAction,
  deployBarracksHero,
  buyBarracksUnit(game, laneIndex, lane, rosterKey, requestedBarracksId, count) {
    return barracksSystem.buyBarracksUnit(
      game,
      laneIndex,
      lane,
      rosterKey,
      requestedBarracksId,
      count,
      BARRACKS_SYSTEM_DEPS
    );
  },
  sellBarracksUnit(laneIndex, lane, rosterKey, requestedBarracksId) {
    return barracksSystem.sellBarracksUnit(
      laneIndex,
      lane,
      rosterKey,
      requestedBarracksId,
      BARRACKS_SYSTEM_DEPS
    );
  },
  ROUTE_TYPES,
  TEAM_HP_START,
  INCOME_INTERVAL_TICKS,
  BARRACKS_SEND_TIMER_TICKS,
  WAVE_TIMER_TICKS,
  BUILD_PHASE_TICKS,
  TRANSITION_PHASE_TICKS,
  GRID_W,
  GRID_H,
  SPAWN_X,
  SPAWN_YG,
  CASTLE_X,
  CASTLE_YG,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  FIXED_SLOT_LAYOUT,
  BATTLEFIELD_TOPOLOGY,
  ALLEGIANCE_KEYS,
  LANE_COMMAND_STATES,
  OPPOSING_LANE_INDEX,
  LEGACY_ACTION_REJECTION_REASONS,
  LANE_COMMAND_COMBAT_LEASH,
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

function getDefaultSlotDefinitions(playerCount, laneTeams) {
  return gameRuntimeSystem.getDefaultSlotDefinitions(playerCount, laneTeams, GAME_RUNTIME_SYSTEM_DEPS);
}

function normalizeGameOptions(options) {
  return gameRuntimeSystem.normalizeGameOptions(options, GAME_RUNTIME_SYSTEM_DEPS);
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
  return gameRuntimeSystem.createMLGame(playerCount, options, GAME_RUNTIME_SYSTEM_DEPS);
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
  return gameRuntimeSystem.getLaneBuildValue(lane, GAME_RUNTIME_SYSTEM_DEPS);
}

function createRoundSnapshotLane(game, lane) {
  return gameRuntimeSystem.createRoundSnapshotLane(game, lane, GAME_RUNTIME_SYSTEM_DEPS);
}

function isOpponentLane(game, sourceLaneIndex, targetLaneIndex) {
  return gameRuntimeSystem.isOpponentLane(game, sourceLaneIndex, targetLaneIndex, GAME_RUNTIME_SYSTEM_DEPS);
}

function findNextActiveOpponentLaneIndex(game, fromLaneIndex) {
  return gameRuntimeSystem.findNextActiveOpponentLaneIndex(game, fromLaneIndex, GAME_RUNTIME_SYSTEM_DEPS);
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

function applyMLAction(game, laneIndex, action) {
  return gameRuntimeSystem.applyMLAction(game, laneIndex, action, GAME_RUNTIME_SYSTEM_DEPS);
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
  return combatSystem.moveTowardContact2D(
    unit,
    target,
    speed,
    stopDistance,
    minX,
    maxX,
    minY,
    maxY,
    COMBAT_SYSTEM_DEPS
  );
}

function updateSimpleContactApproachMemory(unit, target) {
  return combatSystem.updateSimpleContactApproachMemory(unit, target, COMBAT_SYSTEM_DEPS);
}

function moveTowardSimpleContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY) {
  return combatSystem.moveTowardSimpleContact2D(
    unit,
    target,
    speed,
    stopDistance,
    minX,
    maxX,
    minY,
    maxY,
    COMBAT_SYSTEM_DEPS
  );
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
  return combatSystem.getUnitContactRadius(typeKey, COMBAT_SYSTEM_DEPS);
}

function getUnitStopDistance(attackerType, targetType) {
  return combatSystem.getUnitStopDistance(attackerType, targetType, COMBAT_SYSTEM_DEPS);
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
  return combatSystem.clearUnitCombatTarget(unit, gameTick, options, COMBAT_SYSTEM_DEPS);
}

function clearUnitSupportTarget(unit, gameTick = 0, options = {}) {
  return combatSystem.clearUnitSupportTarget(unit, gameTick, options, COMBAT_SYSTEM_DEPS);
}

function assignUnitCombatTarget(unit, targetDescriptor, gameTick = 0) {
  return combatSystem.assignUnitCombatTarget(unit, targetDescriptor, gameTick, COMBAT_SYSTEM_DEPS);
}

function assignUnitSupportTarget(unit, targetDescriptor, gameTick = 0) {
  return combatSystem.assignUnitSupportTarget(unit, targetDescriptor, gameTick, COMBAT_SYSTEM_DEPS);
}

function isLaneControlledUnitInRegroupWindow(unit, gameTick) {
  return combatSystem.isLaneControlledUnitInRegroupWindow(unit, gameTick, COMBAT_SYSTEM_DEPS);
}

function isUnitCombatTargetStillValid(game, lane, attacker, target) {
  return combatSystem.isUnitCombatTargetStillValid(game, lane, attacker, target, COMBAT_SYSTEM_DEPS);
}

function getResolvedCombatTargetDistance(unit, target) {
  const liveTarget = target && target.entity
    ? target.entity
    : target;
  const distance = getWaveUnitTargetDistance(unit, liveTarget);
  return Number.isFinite(distance) ? distance : Infinity;
}

function findFriendlyHealTarget(game, lane, unit, supportProfile) {
  return combatSystem.findFriendlyHealTarget(game, lane, unit, supportProfile, COMBAT_SYSTEM_DEPS);
}

function getRouteUnitTargetPressureCount(game, seeker, targetEntity) {
  return combatSystem.getRouteUnitTargetPressureCount(game, seeker, targetEntity, COMBAT_SYSTEM_DEPS);
}

function getRouteUnitTargetPreferenceScore(game, lane, unit, target, requireAttackRange = false) {
  return combatSystem.getRouteUnitTargetPreferenceScore(
    game,
    lane,
    unit,
    target,
    requireAttackRange,
    COMBAT_SYSTEM_DEPS
  );
}

function shouldSwitchCombatTarget(game, lane, unit, currentTarget, nextTarget) {
  return combatSystem.shouldSwitchCombatTarget(
    game,
    lane,
    unit,
    currentTarget,
    nextTarget,
    COMBAT_SYSTEM_DEPS
  );
}

function getLaneControlledCombatLeashRadius(unit) {
  return combatSystem.getLaneControlledCombatLeashRadius(unit, COMBAT_SYSTEM_DEPS);
}

function clampPointToRadius(pointX, pointY, centerX, centerY, maxRadius) {
  return combatSystem.clampPointToRadius(pointX, pointY, centerX, centerY, maxRadius, COMBAT_SYSTEM_DEPS);
}

function isPointWithinRadius(pointX, pointY, centerX, centerY, maxRadius) {
  return combatSystem.isPointWithinRadius(pointX, pointY, centerX, centerY, maxRadius, COMBAT_SYSTEM_DEPS);
}

function shouldAnchorClampLaneControlledCombat(unit, target) {
  return combatSystem.shouldAnchorClampLaneControlledCombat(unit, target, COMBAT_SYSTEM_DEPS);
}

function resolveWaveCombatTarget(game, lane, combatTarget) {
  return combatSystem.resolveWaveCombatTarget(game, lane, combatTarget, COMBAT_SYSTEM_DEPS);
}

function markTownCoreBreach(game, lane, unit, townCoreTarget) {
  return combatSystem.markTownCoreBreach(game, lane, unit, townCoreTarget, COMBAT_SYSTEM_DEPS);
}

function attackFortressPad(game, lane, attacker, target) {
  return combatSystem.attackFortressPad(game, lane, attacker, target, COMBAT_SYSTEM_DEPS);
}

function clampUnitToAnchor(unit, anchorX, anchorY, maxRadius, minX, maxX, minY, maxY) {
  return combatSystem.clampUnitToAnchor(
    unit,
    anchorX,
    anchorY,
    maxRadius,
    minX,
    maxX,
    minY,
    maxY,
    COMBAT_SYSTEM_DEPS
  );
}

function clampLaneControlledUnitToCombatLeash(unit, minX, maxX, minY, maxY, target = null) {
  return combatSystem.clampLaneControlledUnitToCombatLeash(
    unit,
    minX,
    maxX,
    minY,
    maxY,
    target,
    COMBAT_SYSTEM_DEPS
  );
}

function resolveContactFrame(attacker, target) {
  return combatSystem.resolveContactFrame(attacker, target, COMBAT_SYSTEM_DEPS);
}

function shouldUseLaneControlledSurroundSlots(attacker, target) {
  return combatSystem.shouldUseLaneControlledSurroundSlots(attacker, target, COMBAT_SYSTEM_DEPS);
}

function getContactSlotPoint(lane, attacker, target, stopDistance, options = null) {
  return combatSystem.getContactSlotPoint(lane, attacker, target, stopDistance, options, COMBAT_SYSTEM_DEPS);
}

function getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance, options = null) {
  return combatSystem.getLaneControlledCombatPocketPoint(
    lane,
    attacker,
    target,
    stopDistance,
    options,
    COMBAT_SYSTEM_DEPS
  );
}

function getCombatSlotArrivalTolerance(attacker, target) {
  return combatSystem.getCombatSlotArrivalTolerance(attacker, target, COMBAT_SYSTEM_DEPS);
}

function isUnitInCombatContact(lane, attacker, target) {
  return combatSystem.isUnitInCombatContact(lane, attacker, target, COMBAT_SYSTEM_DEPS);
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
  return combatSystem.getPairSpacing(a, b, fallback, COMBAT_SYSTEM_DEPS);
}

function tryResolveSettledAnchorPairSpacing(a, b, minSpacing, fallbackSpacing) {
  return combatSystem.tryResolveSettledAnchorPairSpacing(a, b, minSpacing, fallbackSpacing, COMBAT_SYSTEM_DEPS);
}

function isLaneControlledUnitSettledAtAnchor(unit) {
  return combatSystem.isLaneControlledUnitSettledAtAnchor(unit, COMBAT_SYSTEM_DEPS);
}

function getUnitAnchorLateralAxis(unit) {
  return combatSystem.getUnitAnchorLateralAxis(unit, COMBAT_SYSTEM_DEPS);
}

function clampLaneControlledUnitToAnchorDrift(unit, minX, maxX, minY, maxY) {
  return combatSystem.clampLaneControlledUnitToAnchorDrift(unit, minX, maxX, minY, maxY, COMBAT_SYSTEM_DEPS);
}

function getPairLateralAxis(a, b) {
  return combatSystem.getPairLateralAxis(a, b, COMBAT_SYSTEM_DEPS);
}

function shouldPreferLateralSpacing(a, b, dx, dy) {
  return combatSystem.shouldPreferLateralSpacing(a, b, dx, dy, COMBAT_SYSTEM_DEPS);
}

function resolveExactOverlapAxis(a, b) {
  return combatSystem.resolveExactOverlapAxis(a, b, COMBAT_SYSTEM_DEPS);
}

function getSharedTargetTangentialAxis(game, lane, a, b) {
  return combatSystem.getSharedTargetTangentialAxis(game, lane, a, b, COMBAT_SYSTEM_DEPS);
}

function shouldApplySimpleLaneHostileCollision(game, a, b, distance) {
  return combatSystem.shouldApplySimpleLaneHostileCollision(game, a, b, distance, COMBAT_SYSTEM_DEPS);
}

function applySeparation2D(game, lane, units, minSpacing, minX, maxX, minY, maxY) {
  return combatSystem.applySeparation2D(
    game,
    lane,
    units,
    minSpacing,
    minX,
    maxX,
    minY,
    maxY,
    COMBAT_SYSTEM_DEPS
  );
}

function getUnitAttackRange(typeKey) {
  return combatSystem.getUnitAttackRange(typeKey, COMBAT_SYSTEM_DEPS);
}

function getUnitEngagementRange(typeKey) {
  return combatSystem.getUnitEngagementRange(typeKey, COMBAT_SYSTEM_DEPS);
}

function isSplitZoneUnit(unit) {
  return Number(unit.posY) < GRID_H;
}

function isMergeZoneUnit(unit) {
  return !isSplitZoneUnit(unit);
}

function getWaveUnitTargetDistance(unit, target) {
  return combatSystem.getWaveUnitTargetDistance(unit, target, COMBAT_SYSTEM_DEPS);
}

function getStructureTargetAcquisitionRange(unit, target) {
  return combatSystem.getStructureTargetAcquisitionRange(unit, target, COMBAT_SYSTEM_DEPS);
}

function findHostileRouteUnitTarget(game, lane, unit, requireAttackRange = false, options = null) {
  return combatSystem.findHostileRouteUnitTarget(
    game,
    lane,
    unit,
    requireAttackRange,
    options,
    COMBAT_SYSTEM_DEPS
  );
}

function getWaveUnitPreferredTarget(game, lane, unit) {
  return combatSystem.getWaveUnitPreferredTarget(game, lane, unit, COMBAT_SYSTEM_DEPS);
}

function hasImmediateFollowThroughCombatTarget(game, lane, unit) {
  return combatSystem.hasImmediateFollowThroughCombatTarget(game, lane, unit, COMBAT_SYSTEM_DEPS);
}

function getLaneBlockingStructureTargets(lane, unit = null) {
  return combatSystem.getLaneBlockingStructureTargets(lane, unit, COMBAT_SYSTEM_DEPS);
}

function findBlockingStructureTarget(game, lane, unit) {
  return combatSystem.findBlockingStructureTarget(game, lane, unit, COMBAT_SYSTEM_DEPS);
}

function traceWaveUnitTick(game, lane, unit, target, details = {}) {
  return combatSystem.traceWaveUnitTick(game, lane, unit, target, details, COMBAT_SYSTEM_DEPS);
}

function _doAttack(game, lane, attacker, target) {
  return combatSystem.doAttack(game, lane, attacker, target, COMBAT_SYSTEM_DEPS);
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
  return tickSystem.mlTick(game, TICK_SYSTEM_DEPS);
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
    getLaneNodeId,
    getWaveSpawnNodeId,
    getBarracksRouteStartNodeId,
    getDefaultSlotDefinitions,
    defaultEnvironmentPlayerCount: FIXED_SLOT_LAYOUT.length,
    normalizeAllegianceKey,
  });
}

/**
 * Returns a key→unitDef map for all sendable (moving/both-mode) units from the DB.
 */
function getMovingUnitDefMap() {
  return catalogSystem.getMovingUnitDefMap(CATALOG_SYSTEM_DEPS);
}

/**
 * Returns a key→towerDef map for all placeable (fixed/both-mode) units from the DB.
 */
function getFixedUnitDefMap() {
  return catalogSystem.getFixedUnitDefMap(CATALOG_SYSTEM_DEPS);
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





