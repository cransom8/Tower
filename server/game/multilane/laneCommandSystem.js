"use strict";

const routeGraph = require("./routeGraph");

const {
  ROUTE_TYPES,
  RouteMineNode,
  normalize2D,
  perpendicular2D,
  dot2D,
  getLaneNodeId,
  getWaveSpawnNodeId,
  getNodeIndex,
  getLaneCombatAxes,
  getBarracksRouteStartNodeId,
  getLaneCoreNodeIdForRouteNode,
  buildRouteSegments,
  parseRouteSegmentId,
  sampleRoutePosition,
  computeUnitRoutePathIndex,
  sampleContinuousRoutePosition,
  projectPointOntoRouteSegments,
  buildRoutePathId,
  resolveUnitNextWaypoint,
} = routeGraph;

const DEFAULT_SPAWN_SOURCE_TYPES = Object.freeze({
  DUNGEON_WAVE: "dungeon_wave",
  SCHEDULED_WAVE: "dungeon_wave",
  BARRACKS_ROSTER: "barracks_roster",
  BARRACKS_HERO: "barracks_hero",
  MARKET_ROSTER: "market_roster",
});

const DEFAULT_LANE_COMMAND_STATES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  RETREAT: "RETREAT",
});

const DEFAULT_UNIT_STANCES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  HOLD: "HOLD",
  RETREAT: "RETREAT",
});

const DEFAULT_PATH_CONTRACT_TYPES = Object.freeze({
  WAVE_LANE: "wave_lane",
  BARRACKS_CROSS: "barracks_cross",
  BARRACKS_LOOP: "barracks_loop",
  GUARD_ANCHOR: "guard_anchor",
  INTERCEPT: "intercept",
  RETREAT_ANCHOR: "retreat_anchor",
});

const DEFAULT_UNIT_MOVEMENT_MODES = Object.freeze({
  LANE_TRAVEL: "LaneTravel",
  RETURN_TO_ANCHOR: "ReturnToAnchor",
});

const DEFAULT_WAVE_UNIT_STATES = Object.freeze({
  MOVING: "MOVING",
});

const DEFAULT_UNIT_COMBAT_ROLES = Object.freeze({
  SHIELD: "shield",
  SWORD: "sword",
  SPEAR: "spear",
  ARCHER: "archer",
  MAGE: "mage",
  PRIEST: "priest",
});

const DEFAULT_LANE_STANCE_ANCHOR_KINDS = Object.freeze({
  INSIDE_GATE: "insideGateAnchor",
  OUTSIDE_GATE: "outsideGateAnchor",
  ENEMY_CORE: "enemyCoreAnchor",
});

const DEFAULT_ALLEGIANCE_KEYS = Object.freeze({
  DUNGEON: "dungeon",
});

function getSpawnSourceTypes(deps) {
  return deps && deps.SPAWN_SOURCE_TYPES ? deps.SPAWN_SOURCE_TYPES : DEFAULT_SPAWN_SOURCE_TYPES;
}

function getLaneCommandStates(deps) {
  return deps && deps.LANE_COMMAND_STATES ? deps.LANE_COMMAND_STATES : DEFAULT_LANE_COMMAND_STATES;
}

function getUnitStances(deps) {
  return deps && deps.UNIT_STANCES ? deps.UNIT_STANCES : DEFAULT_UNIT_STANCES;
}

function getPathContractTypes(deps) {
  return deps && deps.PATH_CONTRACT_TYPES ? deps.PATH_CONTRACT_TYPES : DEFAULT_PATH_CONTRACT_TYPES;
}

function getUnitMovementModes(deps) {
  return deps && deps.UNIT_MOVEMENT_MODES ? deps.UNIT_MOVEMENT_MODES : DEFAULT_UNIT_MOVEMENT_MODES;
}

function getWaveUnitStates(deps) {
  return deps && deps.WAVE_UNIT_STATES ? deps.WAVE_UNIT_STATES : DEFAULT_WAVE_UNIT_STATES;
}

function getUnitCombatRoles(deps) {
  return deps && deps.UNIT_COMBAT_ROLES ? deps.UNIT_COMBAT_ROLES : DEFAULT_UNIT_COMBAT_ROLES;
}

function getLaneStanceAnchorKinds(deps) {
  return deps && deps.LANE_STANCE_ANCHOR_KINDS ? deps.LANE_STANCE_ANCHOR_KINDS : DEFAULT_LANE_STANCE_ANCHOR_KINDS;
}

function getAllegianceKeys(deps) {
  return deps && deps.ALLEGIANCE_KEYS ? deps.ALLEGIANCE_KEYS : DEFAULT_ALLEGIANCE_KEYS;
}

function resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex, deps = {}) {
  if (!game || !Array.isArray(game.lanes))
    return null;

  for (let step = 1; step <= game.lanes.length; step += 1) {
    const idx = (sourceLaneIndex + step) % game.lanes.length;
    if (typeof deps.isOpponentLane === "function" && deps.isOpponentLane(game, sourceLaneIndex, idx))
      return idx;
  }
  return null;
}

function resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex, deps = {}) {
  const opposingLaneIndex = Array.isArray(deps.OPPOSING_LANE_INDEX)
    ? deps.OPPOSING_LANE_INDEX[sourceLaneIndex]
    : null;
  if (Number.isInteger(opposingLaneIndex)
      && typeof deps.isOpponentLane === "function"
      && deps.isOpponentLane(game, sourceLaneIndex, opposingLaneIndex)) {
    return opposingLaneIndex;
  }
  return resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex, deps);
}

function normalizeLaneCommandState(value, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  const normalized = String(value || "").trim().toUpperCase();
  switch (normalized) {
    case laneCommandStates.ATTACK:
    case "ADVANCE":
      return laneCommandStates.ATTACK;
    case laneCommandStates.DEFEND:
    case "HOLD":
      return laneCommandStates.DEFEND;
    case laneCommandStates.RETREAT:
    case "CALLBACK":
      return laneCommandStates.RETREAT;
    default:
      return null;
  }
}

function isLaneCombatEnabledCommandState(commandState, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  return commandState === laneCommandStates.ATTACK
    || commandState === laneCommandStates.DEFEND;
}

function getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex, deps = {}) {
  if (!Number.isInteger(sourceLaneIndex) || sourceLaneIndex < 0)
    return null;

  const opposingLaneIndex = Array.isArray(deps.OPPOSING_LANE_INDEX)
    ? deps.OPPOSING_LANE_INDEX[sourceLaneIndex]
    : null;
  if (game && Array.isArray(game.lanes) && Number.isInteger(opposingLaneIndex)
      && opposingLaneIndex >= 0 && opposingLaneIndex < game.lanes.length) {
    return opposingLaneIndex;
  }

  if (game && Array.isArray(game.lanes) && game.lanes.length > 1) {
    const resolved = resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex, deps);
    if (Number.isInteger(resolved))
      return resolved;
  }

  return sourceLaneIndex;
}

function getLaneCommandState(lane, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  return normalizeLaneCommandState(lane && lane.commandState, deps) || laneCommandStates.DEFEND;
}

function getLaneCommandObjectiveLaneIndex(game, laneOrLaneIndex, deps = {}) {
  const lane = typeof laneOrLaneIndex === "object"
    ? laneOrLaneIndex
    : (typeof deps.getSourceLane === "function" ? deps.getSourceLane(game, laneOrLaneIndex) : null);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const explicit = Number.isInteger(lane.commandTargetLaneIndex)
    ? lane.commandTargetLaneIndex
    : null;
  if (explicit !== null && game && Array.isArray(game.lanes)
      && explicit >= 0 && explicit < game.lanes.length) {
    return explicit;
  }

  return getDefaultLaneObjectiveLaneIndex(game, lane.laneIndex, deps);
}

function getLaneCommandRouteObjectiveLaneIndex(game, laneOrLaneIndex, deps = {}) {
  const lane = typeof laneOrLaneIndex === "object"
    ? laneOrLaneIndex
    : (typeof deps.getSourceLane === "function" ? deps.getSourceLane(game, laneOrLaneIndex) : null);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const laneCommandStates = getLaneCommandStates(deps);
  const commandState = getLaneCommandState(lane, deps);
  if (commandState === laneCommandStates.RETREAT)
    return lane.laneIndex;

  return getLaneCommandObjectiveLaneIndex(game, lane, deps);
}

function getLaneCommandEngagementRadius(lane, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  const commandState = getLaneCommandState(lane, deps);
  if (!isLaneCombatEnabledCommandState(commandState, deps))
    return 0;

  const explicitRadius = Number(lane && lane.engagementRadius);
  if (Number.isFinite(explicitRadius) && explicitRadius > 0) {
    return commandState === laneCommandStates.DEFEND
      ? Math.max(explicitRadius, Number(deps.LANE_COMMAND_DEFENSE_RADIUS) || 0)
      : Math.max(explicitRadius, Number(deps.LANE_COMMAND_COMBAT_LEASH) || 0);
  }

  return commandState === laneCommandStates.DEFEND
    ? Number(deps.LANE_COMMAND_DEFENSE_RADIUS) || 0
    : Number(deps.LANE_COMMAND_COMBAT_LEASH) || 0;
}

function getLaneCommandAnchorProgress(lane, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  const commandState = getLaneCommandState(lane, deps);
  if (commandState === laneCommandStates.ATTACK)
    return 1;
  if (commandState === laneCommandStates.DEFEND || commandState === laneCommandStates.RETREAT)
    return 0;

  const raw = Number(lane && lane.commandAnchorProgress);
  if (!Number.isFinite(raw))
    return 0;
  return Math.max(0, Math.min(1, raw));
}

function resolveLaneCommandContainerLaneIndex(game, lane, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane, deps);
  if (commandState === laneCommandStates.RETREAT)
    return lane.laneIndex;

  const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane, deps);
  if (!Number.isInteger(objectiveLaneIndex))
    return lane.laneIndex;

  return getLaneCommandAnchorProgress(lane, deps) <= (Number(deps.LANE_COMMAND_HOME_CONTAINER_PROGRESS) || 0)
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
  if (fromLaneIndex >= 0 && toLaneIndex >= 0) {
    return fromLaneIndex === toLaneIndex
      ? fromLaneIndex
      : ((Math.max(0, Math.min(1, Number(unit && unit.segmentProgress) || 0)) < 0.5)
        ? fromLaneIndex
        : toLaneIndex);
  }
  if (fromLaneIndex >= 0)
    return fromLaneIndex;
  if (toLaneIndex >= 0)
    return toLaneIndex;
  return null;
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

function buildLaneCommandAnchorSet(game, lane, deps = {}) {
  if (!game || !lane || !Number.isInteger(lane.laneIndex))
    return null;

  const axes = getLaneCombatAxes(lane.laneIndex);
  if (!axes)
    return null;

  const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane, deps);
  const objectiveLane = typeof deps.getLaneByIndex === "function"
    ? deps.getLaneByIndex(game, objectiveLaneIndex)
    : null;
  const enemyCoreTarget = objectiveLane && typeof deps.getLaneTownCoreCombatTarget === "function"
    ? deps.getLaneTownCoreCombatTarget(objectiveLane)
    : null;
  const attackFacing = enemyCoreTarget
    ? normalize2D({
        x: Number(enemyCoreTarget.posX) - Number(axes.core.x),
        y: Number(enemyCoreTarget.posY) - Number(axes.core.y),
      })
    : axes.forward;

  return {
    insideGateAnchor: {
      x: Number(axes.core.x) - (Number(axes.forward.x) * (Number(deps.INSIDE_GATE_ANCHOR_OFFSET) || 0)),
      y: Number(axes.core.y) - (Number(axes.forward.y) * (Number(deps.INSIDE_GATE_ANCHOR_OFFSET) || 0)),
    },
    outsideGateAnchor: {
      x: Number(axes.core.x) + (Number(axes.forward.x) * (Number(deps.OUTSIDE_GATE_ANCHOR_OFFSET) || 0)),
      y: Number(axes.core.y) + (Number(axes.forward.y) * (Number(deps.OUTSIDE_GATE_ANCHOR_OFFSET) || 0)),
    },
    enemyCoreAnchor: enemyCoreTarget
      ? { x: Number(enemyCoreTarget.posX) || 0, y: Number(enemyCoreTarget.posY) || 0 }
      : { x: Number(axes.core.x) || 0, y: Number(axes.core.y) || 0 },
    objectiveLaneIndex,
    forward: axes.forward,
    lateral: axes.lateral,
    attackFacing,
  };
}

function sampleLaneCommandAnchor(game, lane, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  const anchorKinds = getLaneStanceAnchorKinds(deps);
  if (!game || !lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane, deps);
  const anchorSet = buildLaneCommandAnchorSet(game, lane, deps);
  if (!anchorSet)
    return null;

  const objectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, lane, deps);
  const baseRouteSegments = buildLaneCommandCoreRouteSegments(lane.laneIndex, objectiveLaneIndex);
  if (!Array.isArray(baseRouteSegments))
    return null;

  const anchorProgress = getLaneCommandAnchorProgress(lane, deps);
  const anchorKind = commandState === laneCommandStates.RETREAT
    ? anchorKinds.INSIDE_GATE
    : (commandState === laneCommandStates.DEFEND ? anchorKinds.OUTSIDE_GATE : anchorKinds.ENEMY_CORE);
  const anchorPoint = anchorKind === anchorKinds.INSIDE_GATE
    ? anchorSet.insideGateAnchor
    : (anchorKind === anchorKinds.OUTSIDE_GATE ? anchorSet.outsideGateAnchor : anchorSet.enemyCoreAnchor);
  const facing = normalize2D(commandState === laneCommandStates.ATTACK ? anchorSet.attackFacing : anchorSet.forward);

  return {
    commandState,
    combatEnabled: isLaneCombatEnabledCommandState(commandState, deps),
    engagementRadius: getLaneCommandEngagementRadius(lane, deps),
    objectiveLaneIndex,
    containerLaneIndex: resolveLaneCommandContainerLaneIndex(game, lane, deps),
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

function normalizeLegacyDefenderUnit(game, fallbackLane, unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (!unit || !unit.isDefender)
    return false;

  const ownerLane = (typeof deps.getSourceLane === "function"
    ? deps.getSourceLane(game, Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1)
    : null) || fallbackLane;
  if (!ownerLane || !Number.isInteger(ownerLane.laneIndex))
    return false;

  const normalizedBarracksId = resolveUnitSourceBarracksId(unit, deps) || "center";
  const allegianceKey = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(ownerLane)
    : null;

  unit.isDefender = false;
  unit.spawnSourceType = unit.isHero ? spawnSourceTypes.BARRACKS_HERO : spawnSourceTypes.BARRACKS_ROSTER;
  unit.sourceLaneIndex = ownerLane.laneIndex;
  unit.ownerLaneIndex = ownerLane.laneIndex;
  unit.ownerLane = ownerLane.laneIndex;
  unit.targetLaneIndex = Number.isInteger(unit.targetLaneIndex) ? unit.targetLaneIndex : ownerLane.laneIndex;
  unit.laneId = unit.targetLaneIndex;
  unit.sourceBarracksId = normalizedBarracksId;
  unit.sourceBarracksKey = normalizedBarracksId;
  unit.barracksId = normalizedBarracksId;
  unit.allegianceKey = allegianceKey || resolveUnitAllegianceKey(game, ownerLane, unit, deps);
  unit.sourceTeam = resolveLegacySourceTeamFromAllegianceKey(unit.allegianceKey, deps) || ownerLane.team || null;
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

function normalizeLegacyDefenderUnits(game, deps = {}) {
  if (!game || !Array.isArray(game.lanes))
    return;

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (const unit of lane.units) {
      if (normalizeLegacyDefenderUnit(game, lane, unit, deps))
        applyCanonicalUnitMirrors(game, lane, unit, deps);
    }
  }
}

function isMarketWorkerLaneControlledUnit(unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (!unit || !Number.isInteger(unit.sourceLaneIndex) || unit.sourceLaneIndex < 0)
    return false;
  if (unit.isDefender)
    return false;

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  return spawnSourceType === spawnSourceTypes.MARKET_ROSTER;
}

function isLaneControlledUnit(unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (!unit || !Number.isInteger(unit.sourceLaneIndex) || unit.sourceLaneIndex < 0)
    return false;
  if (unit.isDefender)
    return false;

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  return spawnSourceType === spawnSourceTypes.BARRACKS_ROSTER
    || spawnSourceType === spawnSourceTypes.BARRACKS_HERO
    || spawnSourceType === spawnSourceTypes.MARKET_ROSTER;
}

function getLaneCommandOwnerLane(game, unit, deps = {}) {
  if (!isLaneControlledUnit(unit, deps) || typeof deps.getSourceLane !== "function")
    return null;
  return deps.getSourceLane(game, unit.sourceLaneIndex);
}

function getLaneCommandStateForUnit(game, unit, deps = {}) {
  return getLaneCommandState(getLaneCommandOwnerLane(game, unit, deps), deps);
}

function isLaneCommandCombatEnabledForUnit(game, unit, deps = {}) {
  if (!isLaneControlledUnit(unit, deps))
    return true;
  const commandState = getLaneCommandStateForUnit(game, unit, deps);
  if (isMarketWorkerLaneControlledUnit(unit, deps))
    return commandState === getLaneCommandStates(deps).RETREAT;
  return isLaneCombatEnabledCommandState(commandState, deps);
}

function resolveTargetLaneForBarracksSend(game, sourceLaneIndex, _barracksId, deps = {}) {
  if (typeof deps.getSourceLane !== "function")
    return null;
  const sourceLane = deps.getSourceLane(game, sourceLaneIndex);
  if (!sourceLane)
    return null;
  return resolveLaneCommandContainerLaneIndex(game, sourceLane, deps);
}

function resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit, deps = {}) {
  const fallbackLaneIndex = resolveLaneCommandContainerLaneIndex(game, ownerLane, deps);
  if (!ownerLane || !isLaneControlledUnit(unit, deps))
    return fallbackLaneIndex;
  if (isMarketWorkerLaneControlledUnit(unit, deps))
    return ownerLane.laneIndex;
  if (deps.USE_PER_UNIT_ANCHOR_SLOTS)
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

  return routeProgress < 0.5 ? routeStartLaneIndex : routeTargetLaneIndex;
}

function relaxUnitRouteOffsets(unit, speed, deps = {}) {
  if (!unit)
    return;

  const usePerUnitAnchorSlots = !!deps.USE_PER_UNIT_ANCHOR_SLOTS;
  const baseRelaxStep = Math.max(0.12, Math.abs(Number(speed) || 0) * 0.25);
  const relaxStep = usePerUnitAnchorSlots && isLaneControlledUnit(unit, deps)
    ? Math.max(0.35, Math.abs(Number(speed) || 0) * 0.35)
    : baseRelaxStep;
  unit.routeLateralOffset = moveScalarToward(unit.routeLateralOffset, 0, relaxStep);
  unit.routeLongitudinalOffset = moveScalarToward(
    unit.routeLongitudinalOffset,
    0,
    usePerUnitAnchorSlots && isLaneControlledUnit(unit, deps)
      ? relaxStep
      : (relaxStep * 0.6)
  );
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

function setUnitRouteSnapshotState(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const routeProgress = computeUnitRoutePathIndex(unit);
  const longitudinalOffset = Number(unit.routeLongitudinalOffset) || 0;
  const lateralOffset = Number(unit.routeLateralOffset) || 0;
  const sample = sampleContinuousRoutePosition(unit.routeSegments, routeProgress, longitudinalOffset, lateralOffset);
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

function resolveSpawnOriginForUnit(unit, targetLane, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  if (spawnSourceType === spawnSourceTypes.SCHEDULED_WAVE) {
    return typeof deps.getWaveSpawnWorldPosition === "function"
      ? deps.getWaveSpawnWorldPosition(targetLane && targetLane.laneIndex)
      : null;
  }

  if (spawnSourceType === spawnSourceTypes.MARKET_ROSTER) {
    const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
    return typeof deps.getMarketPadWorldPosition === "function"
      ? deps.getMarketPadWorldPosition(sourceLaneIndex)
      : null;
  }

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const sourceBarracksKey = typeof deps.normalizeBarracksSiteId === "function"
    ? deps.normalizeBarracksSiteId(unit && (unit.sourceBarracksKey || unit.sourceBarracksId))
    : null;
  if (sourceLaneIndex < 0 || !sourceBarracksKey)
    return null;

  return typeof deps.getBarracksSiteWorldPosition === "function"
    ? deps.getBarracksSiteWorldPosition(sourceLaneIndex, sourceBarracksKey)
    : null;
}

function resolveRouteContractForUnit(game, targetLane, unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (!game || !targetLane || !unit)
    return { ok: false, reason: "Missing game, target lane, or unit" };

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  const targetLaneIndex = targetLane.laneIndex;
  const targetNodeId = getLaneNodeId(targetLaneIndex);
  if (!targetNodeId)
    return { ok: false, reason: `Target lane ${targetLaneIndex} is missing a route node` };

  if (spawnSourceType === spawnSourceTypes.SCHEDULED_WAVE) {
    const sourceNodeId = getWaveSpawnNodeId(targetLaneIndex);
    const routeSegments = buildRouteSegments(ROUTE_TYPES.WAVE_LANE, sourceNodeId, targetNodeId);
    if (!sourceNodeId || !routeSegments)
      return { ok: false, reason: `Wave route is missing for lane ${targetLaneIndex}` };

    const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane, deps);
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

  if (spawnSourceType === spawnSourceTypes.MARKET_ROSTER) {
    const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
    const sourceNodeId = typeof deps.getMarketRouteNodeId === "function"
      ? deps.getMarketRouteNodeId(sourceLaneIndex)
      : null;
    const routeSegments = typeof deps.buildMarketLoopRouteSegments === "function"
      ? deps.buildMarketLoopRouteSegments(sourceLaneIndex)
      : null;
    const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane, deps);
    if (sourceLaneIndex < 0)
      return { ok: false, reason: "Market worker is missing sourceLaneIndex" };
    if (!sourceNodeId || !Array.isArray(routeSegments) || routeSegments.length <= 0)
      return { ok: false, reason: `Market loop route is missing for lane ${sourceLaneIndex}` };
    if (!spawnOrigin)
      return { ok: false, reason: `Market spawn origin is missing for lane ${sourceLaneIndex}` };

    return {
      ok: true,
      spawnSourceType,
      routeType: ROUTE_TYPES.OUTER_LOOP,
      sourceNodeId,
      targetNodeId: sourceNodeId,
      objectiveLaneIndex: sourceLaneIndex,
      routeSegments,
      spawnOrigin,
      pathId: buildRoutePathId(routeSegments),
      routeLabel: "market_loop",
    };
  }

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const sourceBarracksKey = resolveUnitSourceBarracksId(unit, deps);
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, targetLane, unit, deps);
  const objectiveNodeId = getLaneNodeId(objectiveLaneIndex);
  if (sourceLaneIndex < 0)
    return { ok: false, reason: "Barracks unit is missing sourceLaneIndex" };
  if (!sourceBarracksKey)
    return { ok: false, reason: "Barracks unit is missing a valid sourceBarracksId" };
  if (!objectiveNodeId)
    return { ok: false, reason: `Barracks unit is missing a valid objective lane (${objectiveLaneIndex})` };

  const sourceNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksKey);
  if (!sourceNodeId) {
    return {
      ok: false,
      reason: `Barracks route start node is missing for lane=${sourceLaneIndex} barracks='${sourceBarracksKey}'`,
    };
  }

  const routeSegments = buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksKey, objectiveLaneIndex);
  if (!routeSegments)
    return { ok: false, reason: `Lane command route could not be built for barracks '${sourceBarracksKey}'` };

  const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane, deps);
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

function resolveRedirectRouteContractForExistingLaneControlledUnit(game, currentLane, targetLane, unit, deps = {}) {
  if (!game || !currentLane || !targetLane || !unit)
    return { ok: false, reason: "Missing game, current lane, target lane, or unit" };
  if (!isLaneControlledUnit(unit, deps))
    return resolveRouteContractForUnit(game, targetLane, unit, deps);

  const currentLaneIndex = Number.isInteger(currentLane.laneIndex)
    ? currentLane.laneIndex
    : resolveUnitTargetLaneIndex(game, currentLane, unit);
  const routeTargetLaneIndex = resolveUnitObjectiveLaneIndex(game, targetLane, unit, deps);
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
    const sourceBarracksId = resolveUnitSourceBarracksId(unit, deps);
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
    return resolveRouteContractForUnit(game, targetLane, unit, deps);

  return {
    ok: true,
    spawnSourceType: typeof deps.resolveSpawnSourceTypeFromUnit === "function"
      ? deps.resolveSpawnSourceTypeFromUnit(unit)
      : null,
    routeType,
    sourceNodeId,
    targetNodeId,
    objectiveLaneIndex: routeTargetLaneIndex,
    routeSegments,
    pathId: buildRoutePathId(routeSegments),
    routeLabel,
  };
}

function initializeMovingUnitRouteState(game, targetLane, unit, spawnLogicalPos, deps = {}) {
  const waveUnitStates = getWaveUnitStates(deps);
  const unitMovementModes = getUnitMovementModes(deps);
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (!game || !targetLane || !unit)
    return { ok: false, reason: "Missing game, target lane, or unit" };

  const routeContract = resolveRouteContractForUnit(game, targetLane, unit, deps);
  if (!routeContract.ok) {
    if (deps.log && typeof deps.log.error === "function") {
      deps.log.error("[SpawnAudit][ServerRoute] rejected", {
        unitId: unit && unit.id,
        unitType: unit && unit.type,
        targetLaneIndex: targetLane && targetLane.laneIndex,
        sourceLaneIndex: unit && unit.sourceLaneIndex,
        sourceBarracksKey: unit && (unit.sourceBarracksKey || unit.sourceBarracksId) || null,
        spawnSourceType: typeof deps.resolveSpawnSourceTypeFromUnit === "function"
          ? deps.resolveSpawnSourceTypeFromUnit(unit)
          : null,
        reason: routeContract.reason,
      });
    }
    return routeContract;
  }

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = targetLane.laneIndex;
  const logicalX = spawnLogicalPos && Number.isFinite(spawnLogicalPos.x)
    ? Number(spawnLogicalPos.x)
    : Number(deps.SPAWN_X) || 0;
  const logicalY = spawnLogicalPos && Number.isFinite(spawnLogicalPos.y)
    ? Number(spawnLogicalPos.y)
    : Number(deps.SPAWN_YG) || 0;

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
    const failure = { ok: false, reason: `Route sample is missing for path '${routeContract.pathId}'` };
    if (deps.log && typeof deps.log.error === "function") {
      deps.log.error("[SpawnAudit][ServerRoute] rejected", {
        unitId: unit.id,
        unitType: unit.type,
        targetLaneIndex,
        sourceLaneIndex,
        sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
        spawnSourceType: unit.spawnSourceType,
        reason: failure.reason,
      });
    }
    return failure;
  }

  const routeTangent = normalize2D(startSample.tangent);
  const routeLateral = perpendicular2D(routeTangent);
  const originDelta = {
    x: Number(routeContract.spawnOrigin.x) - Number(startSample.point.x),
    y: Number(routeContract.spawnOrigin.y) - Number(startSample.point.y),
  };
  const spawnLateralOffset = (logicalX - (Number(deps.SPAWN_X) || 0)) * (Number(deps.ROUTE_SLOT_COLUMN_SPACING) || 0);
  const spawnLongitudinalOffset = -logicalY * (Number(deps.ROUTE_SLOT_ROW_SPACING) || 0);
  unit.routeLateralOffset = dot2D(originDelta, routeLateral) + spawnLateralOffset;
  unit.routeLongitudinalOffset = dot2D(originDelta, routeTangent) + spawnLongitudinalOffset;
  unit.stance = routeContract.spawnSourceType === spawnSourceTypes.DUNGEON_WAVE
    ? getUnitStances(deps).ATTACK
    : null;
  unit.routeState = waveUnitStates.MOVING;
  unit.combatState = waveUnitStates.MOVING;
  unit.movementMode = unitMovementModes.LANE_TRAVEL;
  unit.blockedByStructure = false;
  unit.blockedByStructureId = null;
  unit.blockedByStructureType = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.targetLaneIndex = targetLaneIndex;
  unit.laneId = targetLaneIndex;
  if (!setUnitRouteSnapshotState(unit)) {
    const failure = { ok: false, reason: `Failed to materialize path snapshot state for path '${routeContract.pathId}'` };
    if (deps.log && typeof deps.log.error === "function") {
      deps.log.error("[SpawnAudit][ServerRoute] rejected", {
        unitId: unit.id,
        unitType: unit.type,
        targetLaneIndex,
        sourceLaneIndex,
        sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
        spawnSourceType: unit.spawnSourceType,
        reason: failure.reason,
      });
    }
    return failure;
  }
  applyCanonicalUnitMirrors(game, targetLane, unit, deps);

  if (deps.log && typeof deps.log.info === "function") {
    deps.log.info("[SpawnAudit][ServerRoute] assigned", {
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
  }

  return { ok: true, pathId: routeContract.pathId };
}

function applyRouteContractToExistingUnit(unit, routeContract, currentPosition = null, deps = {}) {
  const unitMovementModes = getUnitMovementModes(deps);
  const waveUnitStates = getWaveUnitStates(deps);
  if (!unit || !routeContract || routeContract.ok === false)
    return { ok: false, reason: "Missing unit or route contract" };
  if (!Array.isArray(routeContract.routeSegments) || routeContract.routeSegments.length <= 0)
    return { ok: false, reason: "Route contract is missing route segments" };

  const startSample = sampleRoutePosition(routeContract.routeSegments, 0, 0, 0);
  if (!startSample)
    return { ok: false, reason: `Route sample is missing for path '${routeContract.pathId || "<unknown>"}'` };

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
  unit.routeSegmentIndex = Math.max(0, Math.min(routeContract.routeSegments.length - 1, Math.floor(Number(routeSample.segmentIndex) || 0)));
  unit.segmentProgress = Math.max(0, Math.min(1, Number(routeSample.segmentProgress) || 0));
  unit.currentSegment = routeSample.segmentId || routeContract.routeSegments[unit.routeSegmentIndex];
  unit.routeLateralOffset = dot2D(routeDelta, routeLateral);
  unit.routeLongitudinalOffset = dot2D(routeDelta, routeTangent);
  unit.stance = null;
  unit.routeState = waveUnitStates.MOVING;
  unit.combatState = waveUnitStates.MOVING;
  unit.movementMode = unitMovementModes.RETURN_TO_ANCHOR;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.blockedByStructure = false;
  unit.blockedByStructureId = null;
  unit.blockedByStructureType = null;
  unit._missingRouteLogged = false;

  if (!setUnitRouteSnapshotState(unit))
    return { ok: false, reason: `Failed to materialize path snapshot state for path '${routeContract.pathId || "<unknown>"}'` };
  applyCanonicalUnitMirrors(null, null, unit, deps);

  return { ok: true, pathId: routeContract.pathId || buildRoutePathId(routeContract.routeSegments) };
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

function resolveLaneAnchorColumns(unitCount, deps = {}) {
  const safeCount = Math.max(1, Math.floor(Number(unitCount) || 1));
  return Math.max(1, Math.min(Number(deps.LANE_ANCHOR_MAX_COLUMNS) || 1, safeCount >= 4 ? 5 : safeCount));
}

function resolveCenteredSlotOffset(columnIndex, columns) {
  const safeColumns = Math.max(1, Math.floor(Number(columns) || 1));
  const safeColumnIndex = Math.max(0, Math.min(safeColumns - 1, Math.floor(Number(columnIndex) || 0)));
  const center = Math.floor(safeColumns / 2);
  if (safeColumns % 2 === 1)
    return safeColumnIndex - center;
  const raw = safeColumnIndex < center ? safeColumnIndex - center : safeColumnIndex - center + 1;
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

function normalizeCombatRole(value, deps = {}) {
  const unitCombatRoles = getUnitCombatRoles(deps);
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case unitCombatRoles.SHIELD:
      return unitCombatRoles.SHIELD;
    case unitCombatRoles.SPEAR:
    case "polearm":
      return unitCombatRoles.SPEAR;
    case unitCombatRoles.ARCHER:
    case "ranged":
      return unitCombatRoles.ARCHER;
    case unitCombatRoles.MAGE:
    case "arcane":
      return unitCombatRoles.MAGE;
    case unitCombatRoles.PRIEST:
    case "support":
    case "healer":
      return unitCombatRoles.PRIEST;
    case unitCombatRoles.SWORD:
    case "melee":
    case "hero":
      return unitCombatRoles.SWORD;
    default:
      return null;
  }
}

function resolveUnitCombatRole(unit, deps = {}) {
  const unitCombatRoles = getUnitCombatRoles(deps);
  const explicitRole = normalizeCombatRole(unit && unit.combatRole, deps);
  if (explicitRole)
    return explicitRole;

  const sourceTokens = [unit && unit.heroKey, unit && unit.rosterKey, unit && unit.role, unit && unit.archetypeKey, unit && unit.unitTypeKey, unit && unit.type]
    .filter(Boolean)
    .map((value) => String(value).trim().toLowerCase());
  const joined = sourceTokens.join("|");
  if (joined.includes("shield") || joined.includes("guardian"))
    return unitCombatRoles.SHIELD;
  if (joined.includes("spear") || joined.includes("polearm") || joined.includes("halber") || joined.includes("lancer"))
    return unitCombatRoles.SPEAR;
  if (joined.includes("priest") || joined.includes("cleric") || joined.includes("bishop") || joined.includes("support"))
    return unitCombatRoles.PRIEST;
  if (joined.includes("mage") || joined.includes("wizard") || joined.includes("arcane") || joined.includes("thaum"))
    return unitCombatRoles.MAGE;
  if (joined.includes("archer") || joined.includes("crossbow") || joined.includes("ranger") || joined.includes("ranged"))
    return unitCombatRoles.ARCHER;
  return unitCombatRoles.SWORD;
}

function resolveAnchorHoldDepthBias(combatRole, deps = {}) {
  const unitCombatRoles = getUnitCombatRoles(deps);
  switch (normalizeCombatRole(combatRole, deps)) {
    case unitCombatRoles.SHIELD:
      return -0.6;
    case unitCombatRoles.SWORD:
      return -0.25;
    case unitCombatRoles.SPEAR:
      return 0.1;
    case unitCombatRoles.ARCHER:
    case unitCombatRoles.MAGE:
      return 0.45;
    case unitCombatRoles.PRIEST:
      return 0.8;
    default:
      return 0;
  }
}

function buildLaneAnchorSlot(anchorState, unit, slotIndex, unitCount, deps = {}) {
  const safeSlotIndex = Math.max(0, Math.floor(Number(slotIndex) || 0));
  const columns = Math.max(3, Math.min(9, Math.max(resolveLaneAnchorColumns(unitCount, deps), 3)));
  const row = Math.floor(safeSlotIndex / columns);
  const column = safeSlotIndex % columns;
  const lateralIndex = resolveCenteredSlotOffset(column, columns);
  const lateralDistance = lateralIndex * (Number(deps.ROUTE_SLOT_COLUMN_SPACING) || 0) * 1.15;
  const depthDistance = (row * (Number(deps.ROUTE_SLOT_ROW_SPACING) || 0) * 0.78)
    + resolveAnchorHoldDepthBias(resolveUnitCombatRole(unit, deps), deps);
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

function computeLaneAnchorHoldRadius(anchorState, anchorSlots, deps = {}) {
  const slots = Array.isArray(anchorSlots) ? anchorSlots : [];
  const centerX = Number(anchorState && anchorState.anchorX) || 0;
  const centerY = Number(anchorState && anchorState.anchorY) || 0;
  let maxDistance = Number(deps.ROUTE_SLOT_COLUMN_SPACING) || 0;
  for (const slot of slots) {
    const dx = (Number(slot && slot.x) || 0) - centerX;
    const dy = (Number(slot && slot.y) || 0) - centerY;
    maxDistance = Math.max(maxDistance, Math.sqrt((dx * dx) + (dy * dy)));
  }
  return maxDistance + (Number(deps.ROUTE_SLOT_ROW_SPACING) || 0);
}

function resolveUnitAnchorLeashRadius(unit, anchorHoldRadius, deps = {}) {
  const unitCombatRoles = getUnitCombatRoles(deps);
  const combatRole = resolveUnitCombatRole(unit, deps);
  const baseRadius = Math.max(anchorHoldRadius, Number(deps.ROUTE_SLOT_ROW_SPACING) || 0);
  switch (combatRole) {
    case unitCombatRoles.PRIEST:
      return Math.max(baseRadius + 0.75, 3.25);
    case unitCombatRoles.ARCHER:
    case unitCombatRoles.MAGE:
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

function buildMarketWorkerRetreatAnchorState(game, lane, anchorState, deps = {}) {
  if (!game || !lane)
    return anchorState;
  const townCoreTarget = typeof deps.getLaneTownCoreCombatTarget === "function"
    ? deps.getLaneTownCoreCombatTarget(lane)
    : null;
  if (!townCoreTarget)
    return anchorState;

  const facing = anchorState && anchorState.facing
    ? anchorState.facing
    : { x: 0, y: -1 };
  const lateral = anchorState && anchorState.lateral
    ? anchorState.lateral
    : perpendicular2D(facing);
  return {
    commandState: anchorState ? anchorState.commandState : getLaneCommandState(lane, deps),
    combatEnabled: true,
    engagementRadius: anchorState ? anchorState.engagementRadius : 0,
    objectiveLaneIndex: lane.laneIndex,
    containerLaneIndex: lane.laneIndex,
    anchorProgress: 0,
    anchorKind: "town_core",
    anchorX: Number(townCoreTarget.posX) || 0,
    anchorY: Number(townCoreTarget.posY) || 0,
    facing,
    lateral,
    insideGateAnchor: anchorState ? anchorState.insideGateAnchor : null,
    outsideGateAnchor: anchorState ? anchorState.outsideGateAnchor : null,
    enemyCoreAnchor: anchorState ? anchorState.enemyCoreAnchor : null,
    baseRouteSegments: anchorState ? anchorState.baseRouteSegments : null,
  };
}

function shouldKeepUnitAfterLaneDefeat(lane, unit, deps = {}) {
  if (!unit)
    return false;
  if (unit.isDefender)
    return false;
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  if (spawnSourceType === spawnSourceTypes.DUNGEON_WAVE)
    return false;
  return Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0 && unit.sourceLaneIndex !== lane.laneIndex;
}

function laneHasOccupyingForces(lane, deps = {}) {
  if (!lane)
    return false;
  const activeUnits = Array.isArray(lane.units)
    && lane.units.some((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit, deps) && unit.hp > 0);
  if (activeUnits)
    return true;
  return Array.isArray(lane.spawnQueue)
    && lane.spawnQueue.some((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit, deps));
}

function resolveUnitSourceBarracksId(unit, deps = {}) {
  return typeof deps.normalizeBarracksSiteId === "function"
    ? deps.normalizeBarracksSiteId(unit && (unit.sourceBarracksId || unit.sourceBarracksKey || unit.barracksId))
    : null;
}

function resolveUnitTargetLaneIndex(_game, fallbackLane, unit) {
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

function resolveUnitObjectiveLaneIndex(game, fallbackLane, unit, deps = {}) {
  if (isMarketWorkerLaneControlledUnit(unit, deps))
    return Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : resolveUnitTargetLaneIndex(game, fallbackLane, unit);

  if (Number.isInteger(unit && unit.objectiveLaneIndex))
    return unit.objectiveLaneIndex;

  if (isLaneControlledUnit(unit, deps)) {
    const sourceLane = typeof deps.getSourceLane === "function"
      ? deps.getSourceLane(game, Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1)
      : null;
    const objectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, sourceLane, deps);
    if (Number.isInteger(objectiveLaneIndex))
      return objectiveLaneIndex;
  }

  return resolveUnitTargetLaneIndex(game, fallbackLane, unit);
}

function resolveUnitOwnerLaneIndex(game, fallbackLane, unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (Number.isInteger(unit && unit.ownerLaneIndex))
    return unit.ownerLaneIndex;
  if (Number.isInteger(unit && unit.ownerLane))
    return unit.ownerLane;

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  if (spawnSourceType === spawnSourceTypes.DUNGEON_WAVE)
    return -1;
  if (Number.isInteger(unit && unit.sourceLaneIndex))
    return unit.sourceLaneIndex;
  if (unit && unit.isDefender && fallbackLane && Number.isInteger(fallbackLane.laneIndex))
    return fallbackLane.laneIndex;
  return -1;
}

function resolveUnitStance(game, fallbackLane, unit, deps = {}) {
  const laneCommandStates = getLaneCommandStates(deps);
  const unitStances = getUnitStances(deps);
  const pathContractTypes = getPathContractTypes(deps);
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (isLaneControlledUnit(unit, deps)) {
    const commandState = getLaneCommandStateForUnit(game, unit, deps);
    if (commandState === laneCommandStates.RETREAT)
      return unitStances.RETREAT;
    if (commandState === laneCommandStates.DEFEND)
      return unitStances.DEFEND;
    return unitStances.ATTACK;
  }

  const explicitStance = typeof deps.normalizeUnitStance === "function"
    ? deps.normalizeUnitStance(unit && unit.stance)
    : null;
  if (explicitStance)
    return explicitStance;

  const explicitPathContractType = String(unit && unit.pathContractType || "").trim().toLowerCase();
  if (explicitPathContractType === pathContractTypes.GUARD_ANCHOR
      || explicitPathContractType === pathContractTypes.INTERCEPT) {
    return unitStances.DEFEND;
  }
  if (unit && unit.isDefender)
    return unitStances.DEFEND;

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  if (spawnSourceType === spawnSourceTypes.DUNGEON_WAVE)
    return unitStances.ATTACK;

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = resolveUnitTargetLaneIndex(game, fallbackLane, unit);
  if (sourceLaneIndex >= 0 && sourceLaneIndex === targetLaneIndex)
    return unitStances.HOLD;

  return unitStances.ATTACK;
}

function mapRouteTypeToPathContractType(routeType, barracksId, deps = {}) {
  const pathContractTypes = getPathContractTypes(deps);
  if (routeType === ROUTE_TYPES.WAVE_LANE)
    return pathContractTypes.WAVE_LANE;
  if (routeType === ROUTE_TYPES.CENTER_CROSS)
    return pathContractTypes.BARRACKS_CROSS;
  if (routeType === ROUTE_TYPES.OUTER_LOOP)
    return pathContractTypes.BARRACKS_LOOP;

  const normalizedBarracksId = typeof deps.normalizeBarracksSiteId === "function"
    ? deps.normalizeBarracksSiteId(barracksId)
    : null;
  if (normalizedBarracksId === "center")
    return pathContractTypes.BARRACKS_CROSS;
  if (normalizedBarracksId === "left" || normalizedBarracksId === "right")
    return pathContractTypes.BARRACKS_LOOP;
  return null;
}

function resolveUnitPathContractType(game, fallbackLane, unit, deps = {}) {
  const pathContractTypes = getPathContractTypes(deps);
  const laneCommandStates = getLaneCommandStates(deps);
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  if (isLaneControlledUnit(unit, deps)) {
    const commandState = getLaneCommandStateForUnit(game, unit, deps);
    const combatTargetId = unit && (unit.combatTargetId || unit.combatTarget && unit.combatTarget.unitId);
    if (isMarketWorkerLaneControlledUnit(unit, deps)) {
      if (combatTargetId && isLaneCommandCombatEnabledForUnit(game, unit, deps))
        return pathContractTypes.INTERCEPT;
      if (commandState === laneCommandStates.RETREAT)
        return pathContractTypes.RETREAT_ANCHOR;
      return pathContractTypes.BARRACKS_LOOP;
    }
    if (combatTargetId && isLaneCommandCombatEnabledForUnit(game, unit, deps))
      return pathContractTypes.INTERCEPT;
    if (commandState === laneCommandStates.RETREAT)
      return pathContractTypes.RETREAT_ANCHOR;
    if (commandState === laneCommandStates.DEFEND)
      return pathContractTypes.GUARD_ANCHOR;
    return pathContractTypes.BARRACKS_CROSS;
  }

  const stance = resolveUnitStance(game, fallbackLane, unit, deps);
  if (stance === getUnitStances(deps).DEFEND) {
    return unit && (unit.combatTargetId || unit.combatTarget && unit.combatTarget.unitId)
      ? pathContractTypes.INTERCEPT
      : pathContractTypes.GUARD_ANCHOR;
  }

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  if (spawnSourceType === spawnSourceTypes.DUNGEON_WAVE)
    return pathContractTypes.WAVE_LANE;

  return mapRouteTypeToPathContractType(unit && unit.routeType, resolveUnitSourceBarracksId(unit, deps), deps);
}

function resolveUnitAllegianceKey(game, fallbackLane, unit, deps = {}) {
  const allegianceKeys = getAllegianceKeys(deps);
  const explicitAllegiance = typeof deps.normalizeAllegianceKey === "function"
    ? deps.normalizeAllegianceKey(unit && unit.allegianceKey)
    : null;
  if (explicitAllegiance)
    return explicitAllegiance;

  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  if (spawnSourceType === getSpawnSourceTypes(deps).DUNGEON_WAVE)
    return allegianceKeys.DUNGEON;

  const sourceTeam = typeof deps.normalizeAllegianceKey === "function"
    ? deps.normalizeAllegianceKey(unit && unit.sourceTeam)
    : null;
  if (sourceTeam)
    return sourceTeam;

  const sourceLane = typeof deps.getSourceLane === "function"
    ? deps.getSourceLane(game, Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1)
    : null;
  const sourceLaneAllegiance = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(sourceLane)
    : null;
  if (sourceLaneAllegiance)
    return sourceLaneAllegiance;

  const ownerLane = typeof deps.getSourceLane === "function"
    ? deps.getSourceLane(game, resolveUnitOwnerLaneIndex(game, fallbackLane, unit, deps))
    : null;
  const ownerLaneAllegiance = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(ownerLane)
    : null;
  if (ownerLaneAllegiance)
    return ownerLaneAllegiance;

  return typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(fallbackLane)
    : null;
}

function resolveLegacySourceTeamFromAllegianceKey(allegianceKey, deps = {}) {
  const canonical = typeof deps.normalizeAllegianceKey === "function"
    ? deps.normalizeAllegianceKey(allegianceKey)
    : allegianceKey;
  return canonical === getAllegianceKeys(deps).DUNGEON ? null : canonical;
}

function applyCanonicalUnitMirrors(game, fallbackLane, unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const waveUnitStates = getWaveUnitStates(deps);
  if (!unit)
    return unit;

  const unitId = unit.id || unit.unitId || null;
  const unitTypeKey = unit.type || unit.unitTypeKey || null;
  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  const ownerLaneIndex = resolveUnitOwnerLaneIndex(game, fallbackLane, unit, deps);
  const targetLaneIndex = resolveUnitTargetLaneIndex(game, fallbackLane, unit);
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, fallbackLane, unit, deps);
  const allegianceKey = resolveUnitAllegianceKey(game, fallbackLane, unit, deps);
  const pathContractType = resolveUnitPathContractType(game, fallbackLane, unit, deps);
  const sourceBarracksId = resolveUnitSourceBarracksId(unit, deps);
  const commandState = isLaneControlledUnit(unit, deps)
    ? getLaneCommandStateForUnit(game, unit, deps)
    : null;
  const stance = resolveUnitStance(game, fallbackLane, unit, deps);
  const combatTargetId = unit.combatTargetId || (unit.combatTarget && unit.combatTarget.unitId) || null;
  const combatRole = resolveUnitCombatRole(unit, deps);
  const isBarracksControlledUnit = Number.isInteger(unit.sourceLaneIndex)
    && unit.sourceLaneIndex >= 0
    && (spawnSourceType === spawnSourceTypes.BARRACKS_ROSTER
      || spawnSourceType === spawnSourceTypes.BARRACKS_HERO);
  const isDefenderUnit = !!unit.isDefender && !isBarracksControlledUnit;
  const canEngage = isLaneCommandCombatEnabledForUnit(game, unit, deps);

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
  unit.commandState = commandState;
  unit.stance = stance;
  unit.pathContractType = pathContractType;
  unit.combatRole = combatRole;
  unit.combatTargetId = combatTargetId;
  unit.currentTargetId = combatTargetId;
  unit.sourceTeam = resolveLegacySourceTeamFromAllegianceKey(allegianceKey, deps);
  unit.isWaveUnit = spawnSourceType === spawnSourceTypes.DUNGEON_WAVE;
  unit.isDefender = isDefenderUnit;
  unit.canEngage = canEngage;
  unit.state = unit.combatState || unit.routeState || unit.state || waveUnitStates.MOVING;
  if (!unit.movementMode)
    unit.movementMode = getUnitMovementModes(deps).LANE_TRAVEL;
  return unit;
}

function requeueLaneControlledUnit(targetLane, unit, deps = {}) {
  if (!targetLane || !unit)
    return false;
  const spawnSourceType = typeof deps.resolveSpawnSourceTypeFromUnit === "function"
    ? deps.resolveSpawnSourceTypeFromUnit(unit)
    : null;
  unit.targetLaneIndex = targetLane.laneIndex;
  unit.laneId = targetLane.laneIndex;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.assignedSlotIndex = null;
  unit.stance = null;
  unit.pathContractType = null;
  unit.movementMode = getUnitMovementModes(deps).LANE_TRAVEL;
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
  unit.spawnLogicalPos = typeof deps.resolveSpawnLogicalPosition === "function"
    ? deps.resolveSpawnLogicalPosition(spawnSourceType, unit.spawnIndex)
    : null;
  targetLane.spawnQueue.push(unit);
  return true;
}

function syncLaneCommandAssignments(game, deps = {}) {
  if (!game || !Array.isArray(game.lanes))
    return;

  normalizeLegacyDefenderUnits(game, deps);

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    lane.commandSlots = [];
    lane.assignedUnits = [];
    lane.assignedUnitOrder = Array.isArray(lane.assignedUnitOrder) ? lane.assignedUnitOrder : [];
    lane.insideGateAnchor = null;
    lane.outsideGateAnchor = null;
    lane.enemyCoreAnchor = null;
  }

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (let unitIndex = lane.units.length - 1; unitIndex >= 0; unitIndex -= 1) {
      const unit = lane.units[unitIndex];
      if (!isLaneControlledUnit(unit, deps))
        continue;

      const ownerLane = getLaneCommandOwnerLane(game, unit, deps);
      if (!ownerLane)
        continue;

      const targetLaneIndex = resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit, deps);
      const targetLane = typeof deps.getLaneByIndex === "function" ? deps.getLaneByIndex(game, targetLaneIndex) || lane : lane;
      const routeObjectiveLaneIndex = unit.isMarketWorker
        ? ownerLane.laneIndex
        : getLaneCommandRouteObjectiveLaneIndex(game, ownerLane, deps);
      unit.targetLaneIndex = targetLane.laneIndex;
      unit.laneId = targetLane.laneIndex;
      unit.objectiveLaneIndex = routeObjectiveLaneIndex;

      const currentPathId = buildRoutePathId(unit.routeSegments);
      const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
      const sourceBarracksId = resolveUnitSourceBarracksId(unit, deps);
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
        routeContract = resolveRouteContractForUnit(game, targetLane, unit, deps);
      }
      const desiredPathId = routeContract && routeContract.ok ? routeContract.pathId : null;
      if (routeContract && routeContract.ok
          && Array.isArray(unit.routeSegments) && unit.routeSegments.length > 0
          && (lane !== targetLane || desiredPathId !== currentPathId || unit.objectiveLaneIndex !== routeObjectiveLaneIndex)) {
        routeContract = resolveRedirectRouteContractForExistingLaneControlledUnit(game, lane, targetLane, unit, deps);
      }
      const resolvedPathId = routeContract && routeContract.ok ? routeContract.pathId : null;
      if (routeContract && routeContract.ok
          && (lane !== targetLane || resolvedPathId !== currentPathId || unit.objectiveLaneIndex !== routeObjectiveLaneIndex)) {
        applyRouteContractToExistingUnit(unit, routeContract, {
          x: Number(unit.posX),
          y: Number(unit.posY),
        }, deps);
      }

      if (lane !== targetLane) {
        lane.units.splice(unitIndex, 1);
        targetLane.units.push(unit);
      }
      applyCanonicalUnitMirrors(game, targetLane, unit, deps);
    }

    if (!Array.isArray(lane.spawnQueue))
      continue;

    for (let queueIndex = lane.spawnQueue.length - 1; queueIndex >= 0; queueIndex -= 1) {
      const unit = lane.spawnQueue[queueIndex];
      if (!isLaneControlledUnit(unit, deps))
        continue;

      const ownerLane = getLaneCommandOwnerLane(game, unit, deps);
      if (!ownerLane)
        continue;

      const targetLaneIndex = resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit, deps);
      const targetLane = typeof deps.getLaneByIndex === "function" ? deps.getLaneByIndex(game, targetLaneIndex) || lane : lane;
      unit.targetLaneIndex = targetLane.laneIndex;
      unit.laneId = targetLane.laneIndex;
      unit.objectiveLaneIndex = unit.isMarketWorker
        ? ownerLane.laneIndex
        : getLaneCommandObjectiveLaneIndex(game, ownerLane, deps);

      if (lane !== targetLane) {
        lane.spawnQueue.splice(queueIndex, 1);
        requeueLaneControlledUnit(targetLane, unit, deps);
      } else {
        unit.spawnIndex = queueIndex;
        unit.spawnLogicalPos = typeof deps.resolveSpawnLogicalPosition === "function"
          ? deps.resolveSpawnLogicalPosition(
              typeof deps.resolveSpawnSourceTypeFromUnit === "function" ? deps.resolveSpawnSourceTypeFromUnit(unit) : null,
              queueIndex
            )
          : null;
      }
      applyCanonicalUnitMirrors(game, targetLane, unit, deps);
    }
  }

  for (const ownerLane of game.lanes) {
    if (!ownerLane)
      continue;

    const anchorState = sampleLaneCommandAnchor(game, ownerLane, deps);
    const ownerCommandState = anchorState ? anchorState.commandState : getLaneCommandState(ownerLane, deps);
    ownerLane.combatEnabled = !!(anchorState && anchorState.combatEnabled);
    ownerLane.engagementRadius = anchorState ? anchorState.engagementRadius : 0;
    ownerLane.commandAnchorProgress = anchorState ? anchorState.anchorProgress : 0;
    ownerLane.commandAnchor = anchorState
      ? { x: anchorState.anchorX, y: anchorState.anchorY, laneIndex: anchorState.containerLaneIndex }
      : null;
    ownerLane.commandFacing = anchorState ? { x: anchorState.facing.x, y: anchorState.facing.y } : null;
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
    const retreatingMarketEntries = [];
    for (const lane of game.lanes) {
      for (const unit of lane.units || []) {
        if (!isLaneControlledUnit(unit, deps) || unit.sourceLaneIndex !== ownerLane.laneIndex || unit.hp <= 0)
          continue;
        unit.combatRole = resolveUnitCombatRole(unit, deps);
        if (unit.isMarketWorker) {
          if (ownerCommandState === getLaneCommandStates(deps).RETREAT) {
            retreatingMarketEntries.push({ lane, unit });
          } else {
            unit.assignedSlotIndex = null;
            unit.anchorTargetX = null;
            unit.anchorTargetY = null;
            unit.anchorTargetProgress = null;
            unit.anchorFacingX = null;
            unit.anchorFacingY = null;
            unit.anchorLateralX = null;
            unit.anchorLateralY = null;
            unit.anchorCenterX = null;
            unit.anchorCenterY = null;
            unit.anchorHoldRadius = 0;
            unit.anchorLeashRadius = 0;
            unit.currentWaypointTargetX = null;
            unit.currentWaypointTargetY = null;
            unit.currentWaypointTargetKind = null;
            continue;
          }
        }
        orderedEntries.push({ lane, unit });
      }
    }

    orderedEntries.sort((left, right) => resolveLaneControlledUnitSortKey(left.unit).localeCompare(resolveLaneControlledUnitSortKey(right.unit)));
    retreatingMarketEntries.sort((left, right) => resolveLaneControlledUnitSortKey(left.unit).localeCompare(resolveLaneControlledUnitSortKey(right.unit)));

    const currentWaypointTarget = buildAnchorWaypointTarget(anchorState);
    const commandSlots = orderedEntries.map((entry, unitIndex) => {
      const slot = buildLaneAnchorSlot(anchorState, entry.unit, unitIndex, orderedEntries.length, deps);
      return {
        slotIndex: unitIndex,
        x: Number(slot.x.toFixed(3)),
        y: Number(slot.y.toFixed(3)),
        unitId: entry.unit.id,
      };
    });
    const anchorHoldRadius = computeLaneAnchorHoldRadius(anchorState, commandSlots, deps);
    const anchorCenterX = anchorState ? Number(anchorState.anchorX) : 0;
    const anchorCenterY = anchorState ? Number(anchorState.anchorY) : 0;
    const anchorFacing = anchorState ? anchorState.facing : { x: 0, y: -1 };
    const anchorLateral = anchorState ? anchorState.lateral : { x: 1, y: 0 };
    const marketRetreatAnchorState = retreatingMarketEntries.length > 0
      ? buildMarketWorkerRetreatAnchorState(game, ownerLane, anchorState, deps)
      : null;
    const marketWaypointTarget = buildAnchorWaypointTarget(marketRetreatAnchorState);
    const marketCommandSlots = marketRetreatAnchorState
      ? retreatingMarketEntries.map((entry, unitIndex) => {
        const slot = buildLaneAnchorSlot(marketRetreatAnchorState, entry.unit, unitIndex, retreatingMarketEntries.length, deps);
        return {
          slotIndex: unitIndex,
          x: Number(slot.x.toFixed(3)),
          y: Number(slot.y.toFixed(3)),
          unitId: entry.unit.id,
        };
      })
      : [];
    const marketAnchorHoldRadius = marketRetreatAnchorState
      ? computeLaneAnchorHoldRadius(marketRetreatAnchorState, marketCommandSlots, deps)
      : 0;
    const marketAnchorCenterX = marketRetreatAnchorState ? Number(marketRetreatAnchorState.anchorX) : 0;
    const marketAnchorCenterY = marketRetreatAnchorState ? Number(marketRetreatAnchorState.anchorY) : 0;
    const marketAnchorFacing = marketRetreatAnchorState ? marketRetreatAnchorState.facing : { x: 0, y: -1 };
    const marketAnchorLateral = marketRetreatAnchorState ? marketRetreatAnchorState.lateral : { x: 1, y: 0 };

    function assignAnchorEntries(entries, slots, activeAnchorState, activeWaypointTarget, activeHoldRadius, activeAnchorCenterX, activeAnchorCenterY, activeAnchorFacing, activeAnchorLateral) {
      for (let unitIndex = 0; unitIndex < entries.length; unitIndex += 1) {
        const entry = entries[unitIndex];
        const slot = slots[unitIndex];
        const anchorLeashRadius = resolveUnitAnchorLeashRadius(entry.unit, activeHoldRadius, deps);
        entry.unit.assignedSlotIndex = unitIndex;
        entry.unit.anchorTargetX = slot.x;
        entry.unit.anchorTargetY = slot.y;
        entry.unit.anchorTargetProgress = activeAnchorState ? activeAnchorState.anchorProgress : 0;
        entry.unit.anchorFacingX = activeAnchorFacing.x;
        entry.unit.anchorFacingY = activeAnchorFacing.y;
        entry.unit.anchorLateralX = activeAnchorLateral.x;
        entry.unit.anchorLateralY = activeAnchorLateral.y;
        entry.unit.commandState = activeAnchorState ? activeAnchorState.commandState : getLaneCommandState(ownerLane, deps);
        entry.unit.anchorCenterX = activeAnchorCenterX;
        entry.unit.anchorCenterY = activeAnchorCenterY;
        entry.unit.anchorHoldRadius = activeHoldRadius;
        entry.unit.anchorLeashRadius = anchorLeashRadius;
        entry.unit.currentWaypointTargetX = activeWaypointTarget ? activeWaypointTarget.x : null;
        entry.unit.currentWaypointTargetY = activeWaypointTarget ? activeWaypointTarget.y : null;
        entry.unit.currentWaypointTargetKind = activeWaypointTarget ? activeWaypointTarget.kind : null;
        entry.unit.canEngage = isLaneCommandCombatEnabledForUnit(game, entry.unit, deps);
        entry.unit.combatLeashRadius = activeAnchorState
          ? Math.max(activeAnchorState.engagementRadius, anchorLeashRadius)
          : anchorLeashRadius;
        ownerLane.assignedUnits.push(entry.unit.id);
        ownerLane.commandSlots.push({
          slotIndex: ownerLane.commandSlots.length,
          x: Number(slot.x.toFixed(3)),
          y: Number(slot.y.toFixed(3)),
          unitId: entry.unit.id,
        });
        applyCanonicalUnitMirrors(game, entry.lane, entry.unit, deps);
      }
    }

    assignAnchorEntries(
      orderedEntries,
      commandSlots,
      anchorState,
      currentWaypointTarget,
      anchorHoldRadius,
      anchorCenterX,
      anchorCenterY,
      anchorFacing,
      anchorLateral
    );
    assignAnchorEntries(
      retreatingMarketEntries,
      marketCommandSlots,
      marketRetreatAnchorState,
      marketWaypointTarget,
      marketAnchorHoldRadius,
      marketAnchorCenterX,
      marketAnchorCenterY,
      marketAnchorFacing,
      marketAnchorLateral
    );

    ownerLane.assignedUnitOrder = ownerLane.assignedUnits.slice();
  }
}

module.exports = {
  resolveOuterLoopTargetLaneIndex,
  resolveCenterCrossTargetLaneIndex,
  normalizeLaneCommandState,
  isLaneCombatEnabledCommandState,
  getDefaultLaneObjectiveLaneIndex,
  getLaneCommandState,
  getLaneCommandObjectiveLaneIndex,
  getLaneCommandRouteObjectiveLaneIndex,
  getLaneCommandEngagementRadius,
  getLaneCommandAnchorProgress,
  resolveLaneCommandContainerLaneIndex,
  resolveRouteNodeLaneIndex,
  resolveLaneControlledUnitCurrentSegmentLaneIndex,
  resolveLaneControlledUnitContainerLaneIndex,
  buildLaneCommandCoreRouteSegments,
  buildLaneCommandRouteSegments,
  buildLaneCommandAnchorSet,
  sampleLaneCommandAnchor,
  normalizeLegacyDefenderUnit,
  normalizeLegacyDefenderUnits,
  isLaneControlledUnit,
  getLaneCommandOwnerLane,
  getLaneCommandStateForUnit,
  isLaneCommandCombatEnabledForUnit,
  resolveTargetLaneForBarracksSend,
  relaxUnitRouteOffsets,
  setUnitRouteSnapshotState,
  resolveSpawnOriginForUnit,
  resolveRouteContractForUnit,
  resolveRedirectRouteContractForExistingLaneControlledUnit,
  initializeMovingUnitRouteState,
  applyRouteContractToExistingUnit,
  syncUnitRouteStateToWorldPosition,
  syncMovedUnitPathState,
  resolveLaneAnchorColumns,
  resolveCenteredSlotOffset,
  resolveLaneControlledUnitSortKey,
  normalizeCombatRole,
  resolveUnitCombatRole,
  resolveAnchorHoldDepthBias,
  buildLaneAnchorSlot,
  computeLaneAnchorHoldRadius,
  resolveUnitAnchorLeashRadius,
  buildAnchorWaypointTarget,
  shouldKeepUnitAfterLaneDefeat,
  laneHasOccupyingForces,
  resolveUnitSourceBarracksId,
  resolveUnitTargetLaneIndex,
  resolveUnitObjectiveLaneIndex,
  resolveUnitOwnerLaneIndex,
  resolveUnitStance,
  mapRouteTypeToPathContractType,
  resolveUnitPathContractType,
  resolveUnitAllegianceKey,
  resolveLegacySourceTeamFromAllegianceKey,
  applyCanonicalUnitMirrors,
  requeueLaneControlledUnit,
  syncLaneCommandAssignments,
};
