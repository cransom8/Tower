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
  fireProjectile,
  resolveProjectile,
  resolveAbilityHook,
  resolveStatuses,
  mulberry32,
} = require("./sim-core");
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
const { getBarracksUpgradeDef } = require("./barracksLevels");
const {
  createMLSnapshot: buildMLSnapshot,
  createMLPublicConfig: buildMLPublicConfig,
} = require("./sim-multilane-serialization");
const {
  FORTRESS_BUILDING_DEFS,
  FORTRESS_PAD_DEFS,
} = fortressSystem;
const {
  BARRACKS_LEVEL_ONE_SPEED_MULT,
  SPEED_UPGRADE_STEP,
  BARRACKS_COST_BASE,
  BARRACKS_REQ_INCOME_BASE,
  BARRACKS_ROSTER_REFUND_PCT,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  HERO_ROSTER_DEFS,
  MARKET_ROSTER_DEFS,
} = barracksSystem;
const {
  ROUTE_TYPES,
  ROUTE_SEGMENT_POLYLINES,
} = require("./game/multilane/routeGraph");
const {
  TICK_HZ,
  TICK_MS,
  INCOME_INTERVAL_TICKS,
  TOWER_MAX_LEVEL,
  MAX_UNITS_PER_LANE,
  ENABLE_WAVE_UNIT_TRACE,
  WAVE_UNIT_STATES,
  GRID_W,
  GRID_H,
  SPAWN_X,
  SPAWN_YG,
  CASTLE_X,
  CASTLE_YG,
  SPLASH_RADIUS_TILES,
  MIN_UNIT_SPACING,
  BASE_COMBAT_PATH_SPEED,
  ENGAGEMENT_RANGE_PADDING,
  SEP_DAMP,
  SEP_MAX_PUSH,
  CONTACT_SLOT_TOLERANCE,
  USE_PER_UNIT_ANCHOR_SLOTS,
  TEAM_HP_START,
  BARRACKS_SEND_TIMER_TICKS,
  WAVE_TIMER_TICKS,
  BUILD_PHASE_TICKS,
  TRANSITION_PHASE_TICKS,
  ESCALATION_PER_EXTRA_ROUND,
  getMlRuntimeSettings,
  SHARED_SUFFIX_LENGTH,
  FIXED_SLOT_LAYOUT,
  BATTLEFIELD_TOPOLOGY,
  SPAWN_SOURCE_TYPES,
  ALLEGIANCE_KEYS,
  PATH_CONTRACT_TYPES,
  UNIT_STANCES,
  LANE_COMMAND_STATES,
  UNIT_MOVEMENT_MODES,
  LANE_STANCE_ANCHOR_KINDS,
  UNIT_COMBAT_ROLES,
  OPPOSING_LANE_INDEX,
  ROUTE_TRACE_MIN_DISTANCE_TO_CORE,
  ROUTE_BLOCKING_CORRIDOR_RADIUS,
  DEFENDER_ENGAGEMENT_RANGE,
  ROUTE_BLOCKING_FORWARD_DOT,
  DEFENDER_HOLD_LEASH,
  ROUTE_SLOT_ROW_SPACING,
  ROUTE_SLOT_COLUMN_SPACING,
  ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY,
  LANE_COMMAND_DEFENSE_RADIUS,
  LANE_COMMAND_COMBAT_LEASH,
  LANE_COMMAND_HOME_CONTAINER_PROGRESS,
  LANE_ANCHOR_APPROACH_PROGRESS_TOLERANCE,
  LANE_ANCHOR_JOIN_DISTANCE,
  LANE_ANCHOR_ARRIVAL_DEAD_ZONE,
  LANE_ANCHOR_MAX_COLUMNS,
  LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE,
  LANE_ANCHOR_SETTLED_SEPARATION_SCALE,
  LANE_ANCHOR_MIXED_SEPARATION_SCALE,
  LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET,
  LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK,
  LANE_COMBAT_POCKET_RADIUS_PADDING,
  LANE_COMBAT_POCKET_RADIUS_SCALE,
  LANE_COMBAT_TARGET_LOCK_TICKS,
  LANE_COMBAT_REGROUP_TICKS,
  LANE_COMBAT_SWITCH_DISTANCE_MARGIN,
  FORTRESS_INTERIOR_ASSAULT_RADIUS,
  STRUCTURE_TARGET_VICINITY_PADDING,
  INSIDE_GATE_ANCHOR_OFFSET,
  OUTSIDE_GATE_ANCHOR_OFFSET,
  ANCHOR_SUPPORT_TARGET_RADIUS,
  WAVE_ROUTE_COMBAT_RECOVERY_TICKS,
  LEGACY_ACTION_REJECTION_REASONS,
  normalizeAllegianceKey,
  normalizeUnitStance,
  areAllegiancesHostile,
  resolveLaneAllegianceKey,
} = require("./game/multilane/runtimeConfig");

function bindSystemMethod(system, methodName) {
  return (...args) => system[methodName](...args);
}

function bindSystemMethodWithDeps(system, methodName, getDeps) {
  return (...args) => system[methodName](...args, getDeps());
}

// Shared route runtime now lives in server/game/multilane/routeRuntimeSystem.js.
const normalize2D = bindSystemMethod(routeRuntimeSystem, "normalize2D");
const perpendicular2D = bindSystemMethod(routeRuntimeSystem, "perpendicular2D");
const getLaneNodeId = bindSystemMethod(routeRuntimeSystem, "getLaneNodeId");
const getWaveSpawnNodeId = bindSystemMethod(routeRuntimeSystem, "getWaveSpawnNodeId");
const getLaneCombatAxes = bindSystemMethod(routeRuntimeSystem, "getLaneCombatAxes");
const getBarracksRouteStartNodeId = bindSystemMethod(
  routeRuntimeSystem,
  "getBarracksRouteStartNodeId"
);
const getLaneCoreNodeIdForRouteNode = bindSystemMethod(
  routeRuntimeSystem,
  "getLaneCoreNodeIdForRouteNode"
);
const isBarracksRouteStartNode = bindSystemMethod(
  routeRuntimeSystem,
  "isBarracksRouteStartNode"
);
const getWaveSpawnWorldPosition = bindSystemMethod(
  routeRuntimeSystem,
  "getWaveSpawnWorldPosition"
);
const getPadWorldPosition = bindSystemMethod(routeRuntimeSystem, "getPadWorldPosition");
const getBarracksSiteWorldPosition = bindSystemMethod(
  routeRuntimeSystem,
  "getBarracksSiteWorldPosition"
);

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

const normalizeLaneCommandState = bindSystemMethodWithDeps(
  laneCommandSystem,
  "normalizeLaneCommandState",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const isLaneCombatEnabledCommandState = bindSystemMethodWithDeps(
  laneCommandSystem,
  "isLaneCombatEnabledCommandState",
  () => LANE_COMMAND_SYSTEM_DEPS
);

function getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex) {
  return laneCommandSystem.getDefaultLaneObjectiveLaneIndex
    ? laneCommandSystem.getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex, LANE_COMMAND_SYSTEM_DEPS)
    : sourceLaneIndex;
}

const getLaneCommandState = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandState",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getLaneCommandObjectiveLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandObjectiveLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getLaneCommandRouteObjectiveLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandRouteObjectiveLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getLaneCommandEngagementRadius = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandEngagementRadius",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getLaneCommandAnchorProgress = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandAnchorProgress",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveLaneCommandContainerLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveLaneCommandContainerLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveRouteNodeLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveRouteNodeLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveLaneControlledUnitCurrentSegmentLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveLaneControlledUnitCurrentSegmentLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveLaneControlledUnitContainerLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveLaneControlledUnitContainerLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const buildLaneCommandCoreRouteSegments = bindSystemMethodWithDeps(
  laneCommandSystem,
  "buildLaneCommandCoreRouteSegments",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const buildLaneCommandRouteSegments = bindSystemMethodWithDeps(
  laneCommandSystem,
  "buildLaneCommandRouteSegments",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const buildLaneCommandAnchorSet = bindSystemMethodWithDeps(
  laneCommandSystem,
  "buildLaneCommandAnchorSet",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const sampleLaneCommandAnchor = bindSystemMethodWithDeps(
  laneCommandSystem,
  "sampleLaneCommandAnchor",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const normalizeLegacyDefenderUnit = bindSystemMethodWithDeps(
  laneCommandSystem,
  "normalizeLegacyDefenderUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const normalizeLegacyDefenderUnits = bindSystemMethodWithDeps(
  laneCommandSystem,
  "normalizeLegacyDefenderUnits",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const isLaneControlledUnit = bindSystemMethodWithDeps(
  laneCommandSystem,
  "isLaneControlledUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getLaneCommandOwnerLane = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandOwnerLane",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getLaneCommandStateForUnit = bindSystemMethodWithDeps(
  laneCommandSystem,
  "getLaneCommandStateForUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const isLaneCommandCombatEnabledForUnit = bindSystemMethodWithDeps(
  laneCommandSystem,
  "isLaneCommandCombatEnabledForUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveTargetLaneForBarracksSend = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveTargetLaneForBarracksSend",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const buildRouteSegments = bindSystemMethod(routeRuntimeSystem, "buildRouteSegments");
const parseRouteSegmentId = bindSystemMethod(routeRuntimeSystem, "parseRouteSegmentId");
const getRouteLength = bindSystemMethod(routeRuntimeSystem, "getRouteLength");
const sampleRoutePosition = bindSystemMethod(routeRuntimeSystem, "sampleRoutePosition");
const advanceRouteState = bindSystemMethod(routeRuntimeSystem, "advanceRouteState");
const relaxUnitRouteOffsets = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "relaxUnitRouteOffsets",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const setUnitRouteSnapshotState = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "setUnitRouteSnapshotState",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const computeUnitRoutePathIndex = bindSystemMethod(
  routeRuntimeSystem,
  "computeUnitRoutePathIndex"
);
const sampleContinuousRoutePosition = bindSystemMethod(
  routeRuntimeSystem,
  "sampleContinuousRoutePosition"
);
const resolveSpawnOriginForUnit = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "resolveSpawnOriginForUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveRouteContractForUnit = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "resolveRouteContractForUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveRedirectRouteContractForExistingLaneControlledUnit = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "resolveRedirectRouteContractForExistingLaneControlledUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const initializeMovingUnitRouteState = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "initializeMovingUnitRouteState",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const applyRouteContractToExistingUnit = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "applyRouteContractToExistingUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getUnitForwardDirection = bindSystemMethod(
  routeRuntimeSystem,
  "getUnitForwardDirection"
);
const buildSampledPathFromSegments = bindSystemMethod(
  routeRuntimeSystem,
  "buildSampledPathFromSegments"
);
const sampleRouteByDistanceNorm = bindSystemMethod(
  routeRuntimeSystem,
  "sampleRouteByDistanceNorm"
);
const projectPointOntoPolyline = bindSystemMethod(
  routeRuntimeSystem,
  "projectPointOntoPolyline"
);
const projectPointOntoRouteSegments = bindSystemMethod(
  routeRuntimeSystem,
  "projectPointOntoRouteSegments"
);
const syncUnitRouteStateToWorldPosition = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "syncUnitRouteStateToWorldPosition",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const syncMovedUnitPathState = bindSystemMethodWithDeps(
  routeRuntimeSystem,
  "syncMovedUnitPathState",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveLaneAnchorColumns = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveLaneAnchorColumns",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveCenteredSlotOffset = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveCenteredSlotOffset",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveLaneControlledUnitSortKey = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveLaneControlledUnitSortKey",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const normalizeCombatRole = bindSystemMethodWithDeps(
  laneCommandSystem,
  "normalizeCombatRole",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitCombatRole = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitCombatRole",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveAnchorHoldDepthBias = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveAnchorHoldDepthBias",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const buildLaneAnchorSlot = bindSystemMethodWithDeps(
  laneCommandSystem,
  "buildLaneAnchorSlot",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const computeLaneAnchorHoldRadius = bindSystemMethodWithDeps(
  laneCommandSystem,
  "computeLaneAnchorHoldRadius",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitAnchorLeashRadius = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitAnchorLeashRadius",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const buildAnchorWaypointTarget = bindSystemMethodWithDeps(
  laneCommandSystem,
  "buildAnchorWaypointTarget",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const shouldKeepUnitAfterLaneDefeat = bindSystemMethodWithDeps(
  laneCommandSystem,
  "shouldKeepUnitAfterLaneDefeat",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const laneHasOccupyingForces = bindSystemMethodWithDeps(
  laneCommandSystem,
  "laneHasOccupyingForces",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const requeueLaneControlledUnit = bindSystemMethodWithDeps(
  laneCommandSystem,
  "requeueLaneControlledUnit",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const syncLaneCommandAssignments = bindSystemMethodWithDeps(
  laneCommandSystem,
  "syncLaneCommandAssignments",
  () => LANE_COMMAND_SYSTEM_DEPS
);

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
const translateAbilityParams = bindSystemMethodWithDeps(
  catalogSystem,
  "translateAbilityParams",
  () => CATALOG_SYSTEM_DEPS
);

/**
 * Build the abilities array for a unit/tower type from the DB-loaded unitType.
 * Returns [] if the type has no abilities or isn't in the DB.
 * @param {string} unitTypeKey
 * @returns {object[]} abilities in sim-core format
 */
const buildAbilitiesForUnitType = bindSystemMethodWithDeps(
  catalogSystem,
  "buildAbilitiesForUnitType",
  () => CATALOG_SYSTEM_DEPS
);

// ── DB-first unit/tower resolution ────────────────────────────────────────────

/**
 * Resolve a unit definition from the DB (authoritative).
 * Returns null if the unit type is unknown or fixed-only.
 */
const resolveUnitDef = bindSystemMethodWithDeps(
  catalogSystem,
  "resolveUnitDef",
  () => CATALOG_SYSTEM_DEPS
);
const resolveUnitSupportProfile = bindSystemMethodWithDeps(
  catalogSystem,
  "resolveUnitSupportProfile",
  () => CATALOG_SYSTEM_DEPS
);

/**
 * Resolve a tower definition from the DB (authoritative).
 * DB range is stored normalised to [0,1] × GRID_W.
 */
const resolveTowerDef = bindSystemMethodWithDeps(
  catalogSystem,
  "resolveTowerDef",
  () => CATALOG_SYSTEM_DEPS
);

// ── Barracks helpers ───────────────────────────────────────────────────────────

const getBarracksLevelDef = bindSystemMethod(barracksSystem, "getBarracksLevelDef");
const getBarracksSpeedMultForLevel = bindSystemMethod(
  barracksSystem,
  "getBarracksSpeedMultForLevel"
);
const getBarracksSpeedMult = bindSystemMethod(barracksSystem, "getBarracksSpeedMult");

function getBaseCombatPathSpeed(_unitTypeKey) {
  return BASE_COMBAT_PATH_SPEED;
}

function getSourceLane(game, sourceLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || !Number.isInteger(sourceLaneIndex))
    return null;
  if (sourceLaneIndex < 0 || sourceLaneIndex >= game.lanes.length)
    return null;
  return game.lanes[sourceLaneIndex] || null;
}

const resolveSpawnSourceTypeFromWaveDef = bindSystemMethodWithDeps(
  spawnSystem,
  "resolveSpawnSourceTypeFromWaveDef",
  () => SPAWN_SYSTEM_DEPS
);
const resolveSpawnSourceTypeFromUnit = bindSystemMethodWithDeps(
  spawnSystem,
  "resolveSpawnSourceTypeFromUnit",
  () => SPAWN_SYSTEM_DEPS
);
const isScheduledWaveUnit = bindSystemMethodWithDeps(
  spawnSystem,
  "isScheduledWaveUnit",
  () => SPAWN_SYSTEM_DEPS
);
const resolveUnitSourceBarracksId = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitSourceBarracksId",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitTargetLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitTargetLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitObjectiveLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitObjectiveLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitOwnerLaneIndex = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitOwnerLaneIndex",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitStance = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitStance",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const mapRouteTypeToPathContractType = bindSystemMethodWithDeps(
  laneCommandSystem,
  "mapRouteTypeToPathContractType",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitPathContractType = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitPathContractType",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveUnitAllegianceKey = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveUnitAllegianceKey",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const resolveLegacySourceTeamFromAllegianceKey = bindSystemMethodWithDeps(
  laneCommandSystem,
  "resolveLegacySourceTeamFromAllegianceKey",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const applyCanonicalUnitMirrors = bindSystemMethodWithDeps(
  laneCommandSystem,
  "applyCanonicalUnitMirrors",
  () => LANE_COMMAND_SYSTEM_DEPS
);
const getMarketRouteNodeId = bindSystemMethod(routeRuntimeSystem, "getMarketRouteNodeId");
const getRearGateRouteNodeId = bindSystemMethod(routeRuntimeSystem, "getRearGateRouteNodeId");
const getTradeOutpostRouteNodeId = bindSystemMethod(routeRuntimeSystem, "getTradeOutpostRouteNodeId");
const getMarketPadWorldPosition = bindSystemMethod(routeRuntimeSystem, "getMarketPadWorldPosition");
const buildMarketLoopRouteSegments = bindSystemMethod(routeRuntimeSystem, "buildMarketLoopRouteSegments");
const buildRoutePathId = bindSystemMethod(routeRuntimeSystem, "buildRoutePathId");
const resolveUnitNextWaypoint = bindSystemMethod(
  routeRuntimeSystem,
  "resolveUnitNextWaypoint"
);
const dot2D = bindSystemMethod(routeRuntimeSystem, "dot2D");
const resolveSpawnLogicalPosition = bindSystemMethodWithDeps(
  spawnSystem,
  "resolveSpawnLogicalPosition",
  () => SPAWN_SYSTEM_DEPS
);
function validateSpawnDefinition(game, targetLane, waveDef, options = {}) {
  return spawnSystem.validateSpawnDefinition(
    game,
    targetLane,
    waveDef,
    options,
    SPAWN_SYSTEM_DEPS
  );
}
const getEffectiveWaveEntrySpeedMult = bindSystemMethodWithDeps(
  spawnSystem,
  "getEffectiveWaveEntrySpeedMult",
  () => SPAWN_SYSTEM_DEPS
);

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

const getFortressMaxTier = bindSystemMethod(fortressSystem, "getFortressMaxTier");
const createFortressPadStates = bindSystemMethod(
  fortressSystem,
  "createFortressPadStates"
);
const createBarracksRosterCounts = bindSystemMethod(
  barracksSystem,
  "createBarracksRosterCounts"
);
const createBarracksSiteRosterCounts = bindSystemMethod(
  barracksSystem,
  "createBarracksSiteRosterCounts"
);
const createMarketRosterCounts = bindSystemMethod(
  barracksSystem,
  "createMarketRosterCounts"
);
const getBarracksSiteDef = bindSystemMethod(barracksSystem, "getBarracksSiteDef");
const getMarketRosterDefinition = bindSystemMethod(barracksSystem, "getMarketRosterDefinition");
const normalizeBarracksSiteId = bindSystemMethod(
  barracksSystem,
  "normalizeBarracksSiteId"
);
const summarizeBarracksSiteCounts = bindSystemMethod(
  barracksSystem,
  "summarizeBarracksSiteCounts"
);
const summarizeBarracksSiteRosterEntries = bindSystemMethod(
  barracksSystem,
  "summarizeBarracksSiteRosterEntries"
);
const logBarracksRosterState = bindSystemMethod(
  barracksSystem,
  "logBarracksRosterState"
);
const getBarracksSiteCounts = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteCounts"
);
const getMarketRosterCounts = bindSystemMethod(
  barracksSystem,
  "getMarketRosterCounts"
);
const hasOwnedBarracksUnits = bindSystemMethod(
  barracksSystem,
  "hasOwnedBarracksUnits"
);
const getBarracksSiteTierRequirement = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteTierRequirement"
);
const getBarracksSiteBuildCost = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteBuildCost"
);
const getBarracksSiteMaxLevel = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteMaxLevel"
);
const normalizeBarracksSiteLevel = bindSystemMethod(
  barracksSystem,
  "normalizeBarracksSiteLevel"
);
const getBarracksSiteBaseMaxHp = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteBaseMaxHp"
);
const getBarracksSiteSendIntervalTicks = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteSendIntervalTicks"
);
const createBarracksSiteState = bindSystemMethod(
  barracksSystem,
  "createBarracksSiteState"
);
const createBarracksSiteStates = bindSystemMethod(
  barracksSystem,
  "createBarracksSiteStates"
);
const syncLegacyBarracksAggregate = bindSystemMethod(
  barracksSystem,
  "syncLegacyBarracksAggregate"
);
const ensureBarracksSiteStates = bindSystemMethod(
  barracksSystem,
  "ensureBarracksSiteStates"
);
const getBarracksSiteState = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteState"
);
const describeBarracksSite = bindSystemMethod(barracksSystem, "describeBarracksSite");
const getBarracksSiteLockedReason = bindSystemMethod(
  barracksSystem,
  "getBarracksSiteLockedReason"
);
const isBarracksSiteAvailable = bindSystemMethod(
  barracksSystem,
  "isBarracksSiteAvailable"
);
const getBarracksRosterLockedReason = bindSystemMethod(
  barracksSystem,
  "getBarracksRosterLockedReason"
);
const getBuiltBarracksSiteTier = bindSystemMethod(
  barracksSystem,
  "getBuiltBarracksSiteTier"
);
const getHighestBuiltBarracksSiteTier = bindSystemMethod(
  barracksSystem,
  "getHighestBuiltBarracksSiteTier"
);
const getCurrentBarracksRosterDefinitionForBranch = bindSystemMethod(
  barracksSystem,
  "getCurrentBarracksRosterDefinitionForBranch"
);
const getCurrentMarketRosterDefinitionForLane = bindSystemMethod(
  barracksSystem,
  "getCurrentMarketRosterDefinitionForLane"
);
const resolveBarracksRosterUnlockContext = bindSystemMethod(
  barracksSystem,
  "resolveBarracksRosterUnlockContext"
);
const getHeroRosterDefinition = bindSystemMethod(
  barracksSystem,
  "getHeroRosterDefinition"
);
const getHeroRosterLockedReason = bindSystemMethod(
  barracksSystem,
  "getHeroRosterLockedReason"
);
const completeMarketWorkerLap = bindSystemMethod(
  barracksSystem,
  "completeMarketWorkerLap"
);
const getFortressPadState = bindSystemMethod(fortressSystem, "getFortressPadState");
const getFortressPadByBuildingType = bindSystemMethod(
  fortressSystem,
  "getFortressPadByBuildingType"
);
const getHighestBuiltFortressPadTier = bindSystemMethod(
  fortressSystem,
  "getHighestBuiltFortressPadTier"
);
const getTownCoreTier = bindSystemMethod(fortressSystem, "getTownCoreTier");
const getLaneTownCorePad = bindSystemMethod(fortressSystem, "getLaneTownCorePad");
const getLaneTownCoreHp = bindSystemMethod(fortressSystem, "getLaneTownCoreHp");
const getLaneTownCoreMaxHp = bindSystemMethod(
  fortressSystem,
  "getLaneTownCoreMaxHp"
);
function advanceFortressConstruction(game) {
  return fortressSystem.advanceFortressConstruction(game, FORTRESS_SYSTEM_DEPS);
}

function advanceBarracksSiteConstruction(game) {
  return barracksSystem.advanceBarracksSiteConstruction(game);
}

const FORTRESS_SYSTEM_DEPS = Object.freeze({
  getLaneCombatAxes,
  getPadWorldPosition,
  isScheduledWaveUnit,
  shouldKeepUnitAfterLaneDefeat,
  resolveLaneAllegianceKey,
  getCurrentBarracksRosterDefinitionForBranch,
  getCurrentMarketRosterDefinitionForLane,
  upgradeOwnedBarracksBranchUnits(game, lane, branchKey, targetRosterDef) {
    return barracksSystem.upgradeOwnedBarracksBranchUnits(
      game,
      lane,
      branchKey,
      targetRosterDef,
      BARRACKS_SYSTEM_DEPS
    );
  },
  upgradeOwnedMarketUnits(game, lane, targetUnitDef) {
    return barracksSystem.upgradeOwnedMarketUnits(
      game,
      lane,
      targetUnitDef,
      BARRACKS_SYSTEM_DEPS
    );
  },
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
  getBaseCombatPathSpeed,
  buildAbilitiesForUnitType,
  applyCanonicalUnitMirrors,
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
  getMarketPadWorldPosition,
  getMarketRouteNodeId,
  buildMarketLoopRouteSegments,
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
  advanceFortressConstruction,
  advanceBarracksSiteConstruction,
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
  runScheduledBuildingConstruction,
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
  computeUnitRoutePathIndex,
  setUnitRouteSnapshotState,
  onMarketWorkerLapComplete(game, lane, unit) {
    return completeMarketWorkerLap(game, lane, unit);
  },
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
  createMarketRosterCounts,
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
  buyMarketUnit(game, laneIndex, lane, unitKey, count) {
    return barracksSystem.buyMarketUnit(
      game,
      laneIndex,
      lane,
      unitKey,
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

function createMarketRosterSnapshot(game, lane) {
  return barracksSystem.createMarketRosterSnapshot(game, lane, BARRACKS_SYSTEM_DEPS);
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
  if (!lane || !unit || unit.hp <= 0 || unit.isDefender || unit.isMarketWorker)
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
  if (unit && unit.isMarketWorker)
    return commandState !== LANE_COMMAND_STATES.RETREAT;
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

function runScheduledBuildingConstruction(game) {
  return waveSystem.runScheduledBuildingConstruction(game, WAVE_SYSTEM_DEPS);
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

const getMovingUnitDefMap = bindSystemMethodWithDeps(
  catalogSystem,
  "getMovingUnitDefMap",
  () => CATALOG_SYSTEM_DEPS
);
const getFixedUnitDefMap = bindSystemMethodWithDeps(
  catalogSystem,
  "getFixedUnitDefMap",
  () => CATALOG_SYSTEM_DEPS
);

const SNAPSHOT_SERIALIZATION_DEPS = Object.freeze({
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
  createMarketRosterSnapshot,
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

const PUBLIC_CONFIG_SERIALIZATION_DEPS = Object.freeze({
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
  getLaneCombatAxes,
  getBarracksRouteStartNodeId,
  getMarketRouteNodeId,
  getRearGateRouteNodeId,
  getTradeOutpostRouteNodeId,
  getDefaultSlotDefinitions,
  defaultEnvironmentPlayerCount: FIXED_SLOT_LAYOUT.length,
  normalizeAllegianceKey,
});

const createMLSnapshot = (game) => buildMLSnapshot(game, SNAPSHOT_SERIALIZATION_DEPS);
const createMLPublicConfig = (options) => buildMLPublicConfig(options, PUBLIC_CONFIG_SERIALIZATION_DEPS);

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





