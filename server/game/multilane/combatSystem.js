"use strict";

const { logSpawnAuditInfo: logVerboseAuditInfo } = require("./spawnAuditLogging");

const DEFAULT_LANE_COMMAND_STATES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
});

const DEFAULT_UNIT_COMBAT_ROLES = Object.freeze({
  SHIELD: "shield",
  SWORD: "sword",
  SPEAR: "spear",
});

const DEFAULT_UNIT_MOVEMENT_MODES = Object.freeze({
  COMBAT_ENGAGE: "CombatEngage",
});

const DEFAULT_WAVE_UNIT_STATES = Object.freeze({
  IDLE: "IDLE",
  COMBAT: "COMBAT",
});

const DEFAULT_FORTRESS_BUILDING_DEFS = Object.freeze({});
const DEFAULT_BARRACKS_SITE_DEFS = Object.freeze([]);
const LIVE_LANE_UNIT_INDEX_CACHE = Symbol("liveLaneUnitIndexCache");

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`combatSystem requires deps.${name}`);
  return fn;
}

function getLaneCommandStates(deps = {}) {
  return deps.LANE_COMMAND_STATES || DEFAULT_LANE_COMMAND_STATES;
}

function getUnitCombatRoles(deps = {}) {
  return deps.UNIT_COMBAT_ROLES || DEFAULT_UNIT_COMBAT_ROLES;
}

function getUnitMovementModes(deps = {}) {
  return deps.UNIT_MOVEMENT_MODES || DEFAULT_UNIT_MOVEMENT_MODES;
}

function getWaveUnitStates(deps = {}) {
  return deps.WAVE_UNIT_STATES || DEFAULT_WAVE_UNIT_STATES;
}

function getFortressBuildingDefs(deps = {}) {
  return deps.FORTRESS_BUILDING_DEFS || DEFAULT_FORTRESS_BUILDING_DEFS;
}

function getBarracksSiteDefs(deps = {}) {
  return Array.isArray(deps.BARRACKS_SITE_DEFS) ? deps.BARRACKS_SITE_DEFS : DEFAULT_BARRACKS_SITE_DEFS;
}

function getContactSlotTolerance(deps = {}) {
  const value = Number(deps.CONTACT_SLOT_TOLERANCE);
  return Number.isFinite(value) && value >= 0 ? value : 0.1;
}

function getUsePerUnitAnchorSlots(deps = {}) {
  return deps.USE_PER_UNIT_ANCHOR_SLOTS !== false;
}

function getLaneCommandCombatLeash(deps = {}) {
  const value = Number(deps.LANE_COMMAND_COMBAT_LEASH);
  return Number.isFinite(value) && value > 0 ? value : 8.0;
}

function getLaneCommandDefenseRadius(deps = {}) {
  const value = Number(deps.LANE_COMMAND_DEFENSE_RADIUS);
  return Number.isFinite(value) && value > 0 ? value : 6.0;
}

function getAnchorSupportTargetRadius(deps = {}) {
  const value = Number(deps.ANCHOR_SUPPORT_TARGET_RADIUS);
  return Number.isFinite(value) && value > 0 ? value : 7.5;
}

function getRouteSlotRowSpacing(deps = {}) {
  const value = Number(deps.ROUTE_SLOT_ROW_SPACING);
  return Number.isFinite(value) && value > 0 ? value : 1.0;
}

function getLaneCombatPocketRadiusScale(deps = {}) {
  const value = Number(deps.LANE_COMBAT_POCKET_RADIUS_SCALE);
  return Number.isFinite(value) && value > 0 ? value : 0.6;
}

function getLaneCombatPocketRadiusPadding(deps = {}) {
  const value = Number(deps.LANE_COMBAT_POCKET_RADIUS_PADDING);
  return Number.isFinite(value) && value > 0 ? value : 2.5;
}

function getLaneCombatRegroupTicks(deps = {}) {
  const value = Math.floor(Number(deps.LANE_COMBAT_REGROUP_TICKS));
  return Number.isInteger(value) && value > 0 ? value : 7;
}

function getLaneCombatSwitchDistanceMargin(deps = {}) {
  const value = Number(deps.LANE_COMBAT_SWITCH_DISTANCE_MARGIN);
  return Number.isFinite(value) && value > 0 ? value : 0.75;
}

function getEngagementRangePadding(deps = {}) {
  const value = Number(deps.ENGAGEMENT_RANGE_PADDING);
  return Number.isFinite(value) && value >= 0 ? value : 2.0;
}

function getDefenderEngagementRange(deps = {}) {
  const value = Number(deps.DEFENDER_ENGAGEMENT_RANGE);
  return Number.isFinite(value) && value > 0 ? value : 3.5;
}

function getFortressInteriorAssaultRadius(deps = {}) {
  const value = Number(deps.FORTRESS_INTERIOR_ASSAULT_RADIUS);
  return Number.isFinite(value) && value > 0 ? value : 6.0;
}

function getStructureTargetVicinityPadding(deps = {}) {
  const value = Number(deps.STRUCTURE_TARGET_VICINITY_PADDING);
  return Number.isFinite(value) && value >= 0 ? value : 1.5;
}

function getGridHeight(deps = {}) {
  const value = Math.floor(Number(deps.GRID_H));
  return Number.isInteger(value) && value > 0 ? value : 28;
}

function getSepDamp(deps = {}) {
  const value = Number(deps.SEP_DAMP);
  return Number.isFinite(value) && value > 0 ? value : 0.35;
}

function getSepMaxPush(deps = {}) {
  const value = Number(deps.SEP_MAX_PUSH);
  return Number.isFinite(value) && value > 0 ? value : 0.16;
}

function getLiveLaneUnitById(lane, unitId, tick = null) {
  if (!lane || !Array.isArray(lane.units) || !unitId)
    return null;

  const cacheTick = Number.isInteger(tick) ? tick : null;
  let cache = lane[LIVE_LANE_UNIT_INDEX_CACHE];
  if (!cache
      || cache.tick !== cacheTick
      || cache.units !== lane.units
      || cache.unitCount !== lane.units.length) {
    const byId = new Map();
    for (let i = 0; i < lane.units.length; i += 1) {
      const candidate = lane.units[i];
      if (!candidate || !candidate.id || candidate.hp <= 0)
        continue;
      byId.set(candidate.id, candidate);
    }

    cache = {
      tick: cacheTick,
      units: lane.units,
      unitCount: lane.units.length,
      byId,
    };
    lane[LIVE_LANE_UNIT_INDEX_CACHE] = cache;
  }

  const unit = cache.byId.get(unitId) || null;
  if (unit && unit.hp > 0)
    return unit;
  if (unit)
    cache.byId.delete(unitId);
  return null;
}

function getLaneAnchorSettledSeparationDistance(deps = {}) {
  const value = Number(deps.LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE);
  return Number.isFinite(value) && value > 0 ? value : 0.9;
}

function getLaneAnchorSettledSeparationScale(deps = {}) {
  const value = Number(deps.LANE_ANCHOR_SETTLED_SEPARATION_SCALE);
  return Number.isFinite(value) && value > 0 ? value : 0.25;
}

function getLaneAnchorMixedSeparationScale(deps = {}) {
  const value = Number(deps.LANE_ANCHOR_MIXED_SEPARATION_SCALE);
  return Number.isFinite(value) && value > 0 ? value : 0.6;
}

function getLaneAnchorSettledMaxLateralOffset(deps = {}) {
  const value = Number(deps.LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET);
  return Number.isFinite(value) && value > 0 ? value : 0.15;
}

function getLaneAnchorSettledSlotSpacingSlack(deps = {}) {
  const value = Number(deps.LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK);
  return Number.isFinite(value) && value >= 0 ? value : 0.12;
}

function getEnableWaveUnitTrace(deps = {}) {
  return deps.ENABLE_WAVE_UNIT_TRACE === true;
}

function getUnitTypeKey(subject) {
  if (!subject)
    return null;
  if (typeof subject === "string")
    return subject;
  if (typeof subject !== "object")
    return null;
  return subject.type || subject.unitTypeKey || subject.archetypeKey || subject.rosterKey || null;
}

function moveTowardContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY, deps = {}) {
  const syncMovedUnitPathState = requireDepFunction(deps, "syncMovedUnitPathState");
  if (!target)
    return;

  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (!Number.isFinite(distance) || distance <= stopDistance || distance < 0.01)
    return;

  const step = Math.min(speed, Math.max(0, distance - stopDistance));
  if (step <= 0)
    return;

  unit.posX = Math.max(minX, Math.min(maxX, Number(unit.posX) + ((dx / distance) * step)));
  unit.posY = Math.max(minY, Math.min(maxY, Number(unit.posY) + ((dy / distance) * step)));
  syncMovedUnitPathState(unit);
}

function updateSimpleContactApproachMemory(unit, target, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const getUnitForwardDirection = requireDepFunction(deps, "getUnitForwardDirection");
  if (!unit || !target || !target.id)
    return null;

  const transientLaneControlledApproach = getUsePerUnitAnchorSlots(deps) && isLaneControlledUnit(unit);
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

function moveTowardSimpleContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY, deps = {}) {
  const moveTowardPoint2D = requireDepFunction(deps, "moveTowardPoint2D");
  if (!target)
    return;

  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (!Number.isFinite(distance) || distance <= stopDistance || distance < 0.01)
    return;

  const approachMemory = updateSimpleContactApproachMemory(unit, target, deps);
  const captureDistance = stopDistance + Math.max(0.55, getUnitContactRadius(unit, deps) * 0.65);
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

  moveTowardContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY, deps);
}

function getUnitAttackRange(subject, deps = {}) {
  const getTowerStats = requireDepFunction(deps, "getTowerStats");
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  if (subject && typeof subject === "object" && Number.isFinite(Number(subject.attackRangeOverride))) {
    const attackRangeOverride = Number(subject.attackRangeOverride);
    if (attackRangeOverride > 0)
      return attackRangeOverride;
  }
  const typeKey = getUnitTypeKey(subject);
  const unitDef = resolveUnitDef(typeKey);
  if (unitDef && unitDef.combatRange)
    return unitDef.combatRange;

  const stats = getTowerStats(typeKey, 1);
  if (stats && stats.range)
    return stats.range;

  return 1.5;
}

function getUnitContactRadius(subject, deps = {}) {
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const isFortArchetypeKey = requireDepFunction(deps, "isFortArchetypeKey");
  const typeKey = getUnitTypeKey(subject);
  if (typeKey && getFortressBuildingDefs(deps)[typeKey]) {
    if (typeKey === "town_core")
      return 1.15;
    return 0.95;
  }

  const unitDef = resolveUnitDef(typeKey);
  const hp = Number(unitDef && unitDef.hp) || 80;
  const attackRange = getUnitAttackRange(typeKey, deps);

  let radius;
  if (hp >= 140) radius = 0.85;
  else if (hp >= 100) radius = 0.75;
  else if (hp >= 70) radius = 0.65;
  else if (hp >= 50) radius = 0.55;
  else radius = 0.45;

  if (attackRange > 2.5)
    radius = Math.max(0.45, radius - 0.10);

  if (typeof typeKey === "string" && (typeKey.startsWith("tt_") || isFortArchetypeKey(typeKey)))
    radius += 0.20;

  return radius;
}

function getUnitStopDistance(attackerSubject, targetSubject, deps = {}) {
  const resolveUnitCombatRole = requireDepFunction(deps, "resolveUnitCombatRole");
  const attackerType = getUnitTypeKey(attackerSubject);
  const targetType = getUnitTypeKey(targetSubject);
  const attackRange = getUnitAttackRange(attackerSubject, deps);
  const base = attackRange + (attackRange > 2.0 ? 0.15 : 0.05);
  if (attackRange > 2.0)
    return base;

  const attackerRadius = getUnitContactRadius(attackerSubject, deps);
  const targetRadius = getUnitContactRadius(targetSubject, deps);
  const bodyPadding = Math.max(attackerRadius, targetRadius, (attackerRadius + targetRadius) * 0.5);
  const attackerRole = resolveUnitCombatRole({
    type: attackerType,
    unitTypeKey: attackerType,
    archetypeKey: attackerType,
    rosterKey: attackerType,
  });
  const unitCombatRoles = getUnitCombatRoles(deps);

  let meleeContactBias = 0;
  switch (attackerRole) {
    case unitCombatRoles.SHIELD:
      meleeContactBias = -0.35;
      break;
    case unitCombatRoles.SWORD:
      meleeContactBias = -0.65;
      break;
    case unitCombatRoles.SPEAR:
      meleeContactBias = -0.20;
      break;
    default:
      meleeContactBias = 0;
      break;
  }

  return base + Math.max(0.35, bodyPadding + meleeContactBias);
}

function clearUnitCombatTarget(unit, gameTick = 0, options = {}, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
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
    : gameTick + getLaneCombatRegroupTicks(deps);
  unit.regroupUntilTick = Math.max(Number(unit.regroupUntilTick) || 0, nextRegroupTick);
}

function clearUnitSupportTarget(unit, gameTick = 0, options = {}, deps = {}) {
  return clearUnitCombatTarget(unit, gameTick, {
    ...(options && typeof options === "object" ? options : {}),
    suppressRegroup: true,
  }, deps);
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

function isLaneControlledUnitInRegroupWindow(unit, gameTick, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  return !!(isLaneControlledUnit(unit) && Number(unit.regroupUntilTick) > gameTick);
}

function isUnitCombatTargetStillValid(game, lane, attacker, target, deps = {}) {
  const canRouteUnitEngageTarget = requireDepFunction(deps, "canRouteUnitEngageTarget");
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const isLaneControlledUnitNearSharedCombat = requireDepFunction(deps, "isLaneControlledUnitNearSharedCombat");
  const areRouteUnitsHostile = requireDepFunction(deps, "areRouteUnitsHostile");
  if (!attacker || !target)
    return false;

  if (target.kind === "unit") {
    if (!target.entity)
      return false;
    if (isLaneControlledUnit(attacker)) {
      if (!canLaneControlledUnitSeekCombat(game, attacker))
        return false;
      if (!areRouteUnitsHostile(game, attacker, target.entity))
        return false;
      return isLaneControlledUnitNearSharedCombat(attacker, target);
    }
    if (!canRouteUnitEngageTarget(game, lane, attacker, target.entity))
      return false;
    return true;
  }

  return canLaneControlledUnitSeekCombat(game, attacker, target);
}

function getWaveUnitTargetDistance(unit, target) {
  if (!unit || !target)
    return null;

  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  return Math.sqrt((dx * dx) + (dy * dy));
}

function getResolvedCombatTargetDistance(unit, target) {
  const liveTarget = target && target.entity
    ? target.entity
    : target;
  const distance = getWaveUnitTargetDistance(unit, liveTarget);
  return Number.isFinite(distance) ? distance : Infinity;
}

function getUnitEngagementRange(subject, deps = {}) {
  return Math.max(
    getUnitAttackRange(subject, deps) + getEngagementRangePadding(deps),
    getDefenderEngagementRange(deps)
  );
}

function findFriendlyHealTarget(game, lane, unit, supportProfile, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const resolveUnitAllegianceKey = requireDepFunction(deps, "resolveUnitAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  if (!game || !lane || !unit || !supportProfile || !supportProfile.isHealer || !Array.isArray(lane.units))
    return null;

  const healerAllegiance = resolveUnitAllegianceKey(game, lane, unit);
  const maxSeekDistance = isLaneControlledUnit(unit)
    ? Math.max(getLaneControlledCombatLeashRadius(unit, deps), getUnitAttackRange(unit, deps) + 0.5)
    : Math.max(getUnitEngagementRange(unit, deps), getUnitAttackRange(unit, deps) + 0.5);
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

    const dist = getWaveUnitTargetDistance(unit, candidate);
    if (!Number.isFinite(dist) || dist > maxSeekDistance + getContactSlotTolerance(deps))
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

function getCurrentCombatTargetUnitId(unit) {
  if (!unit)
    return null;
  if (unit.combatTarget && unit.combatTarget.unitId)
    return unit.combatTarget.unitId;
  return unit.combatTargetId || null;
}

function buildRouteUnitTargetPressureIndex(game, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const resolveRouteUnitFactionKey = requireDepFunction(deps, "resolveRouteUnitFactionKey");
  const byFactionAndTarget = new Map();

  for (const allyLane of game && game.lanes || []) {
    if (!allyLane || !Array.isArray(allyLane.units))
      continue;

    for (const ally of allyLane.units) {
      if (!ally || ally.hp <= 0 || !isLaneControlledUnit(ally))
        continue;

      const allyFaction = resolveRouteUnitFactionKey(game, ally);
      const allyTargetId = getCurrentCombatTargetUnitId(ally);
      if (!allyFaction || !allyTargetId)
        continue;

      let targetCounts = byFactionAndTarget.get(allyFaction);
      if (!targetCounts) {
        targetCounts = new Map();
        byFactionAndTarget.set(allyFaction, targetCounts);
      }
      targetCounts.set(allyTargetId, (targetCounts.get(allyTargetId) || 0) + 1);
    }
  }

  return {
    byFactionAndTarget,
    factionKeys: Array.from(byFactionAndTarget.keys()),
    friendlyFactionKeysByFaction: new Map(),
  };
}

function getCombatTargetSpatialCellSize(deps = {}) {
  return Math.max(
    2.5,
    getDefenderEngagementRange(deps) + getContactSlotTolerance(deps)
  );
}

function buildCombatTargetSpatialIndex(game, deps = {}) {
  const cellSize = getCombatTargetSpatialCellSize(deps);
  const buckets = new Map();
  let maxContactRadius = 0;
  let maxBaseSpeed = 0;
  if (!game || !Array.isArray(game.lanes)) {
    return {
      buckets,
      cellSize,
      maxContactRadius,
      maxBaseSpeed,
    };
  }

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (const unit of lane.units) {
      if (!unit || unit.hp <= 0 || unit.isDefender)
        continue;

      maxContactRadius = Math.max(maxContactRadius, getUnitContactRadius(unit, deps));
      maxBaseSpeed = Math.max(maxBaseSpeed, Math.max(0, Number(unit.baseSpeed) || 0));

      const cellX = getSpatialBucketCellCoordinate(unit.posX, cellSize);
      const cellY = getSpatialBucketCellCoordinate(unit.posY, cellSize);
      const key = getSpatialBucketKey(cellX, cellY);
      let bucket = buckets.get(key);
      if (!bucket) {
        bucket = [];
        buckets.set(key, bucket);
      }
      bucket.push({ lane, unit });
    }
  }

  return {
    buckets,
    cellSize,
    maxContactRadius,
    maxBaseSpeed,
  };
}

function collectCombatTargetSpatialCandidates(spatialIndex, unit, maxDistance) {
  if (!spatialIndex || !unit)
    return [];

  const cellSize = Math.max(0.5, Number(spatialIndex.cellSize) || 0.5);
  const searchRadius = Math.max(
    0,
    (Number(maxDistance) || 0)
      + (Number(spatialIndex.maxContactRadius) || 0)
      + (Number(spatialIndex.maxBaseSpeed) || 0)
      + cellSize
  );
  const centerX = Number(unit.posX) || 0;
  const centerY = Number(unit.posY) || 0;
  const minCellX = getSpatialBucketCellCoordinate(centerX - searchRadius, cellSize);
  const maxCellX = getSpatialBucketCellCoordinate(centerX + searchRadius, cellSize);
  const minCellY = getSpatialBucketCellCoordinate(centerY - searchRadius, cellSize);
  const maxCellY = getSpatialBucketCellCoordinate(centerY + searchRadius, cellSize);
  const candidates = [];

  for (let cellX = minCellX; cellX <= maxCellX; cellX += 1) {
    for (let cellY = minCellY; cellY <= maxCellY; cellY += 1) {
      const bucket = spatialIndex.buckets.get(getSpatialBucketKey(cellX, cellY));
      if (!bucket)
        continue;
      for (const entry of bucket)
        candidates.push(entry);
    }
  }

  return candidates;
}

function createCombatTargetingContext(game, deps = {}) {
  return {
    routeTargetPressureIndex: buildRouteUnitTargetPressureIndex(game, deps),
    combatUnitSpatialIndex: buildCombatTargetSpatialIndex(game, deps),
    contactSlotOccupancyByLane: new WeakMap(),
  };
}

function getFriendlyPressureFactionKeys(pressureIndex, seekerFaction, areAllegiancesHostile) {
  if (!pressureIndex || !seekerFaction)
    return [];

  const cached = pressureIndex.friendlyFactionKeysByFaction.get(seekerFaction);
  if (cached)
    return cached;

  const friendlyFactionKeys = [];
  for (const factionKey of pressureIndex.factionKeys) {
    if (!factionKey || areAllegiancesHostile(seekerFaction, factionKey))
      continue;
    friendlyFactionKeys.push(factionKey);
  }

  pressureIndex.friendlyFactionKeysByFaction.set(seekerFaction, friendlyFactionKeys);
  return friendlyFactionKeys;
}

function getRouteUnitTargetPressureCount(game, seeker, targetEntity, deps = {}, context = null) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const resolveRouteUnitFactionKey = requireDepFunction(deps, "resolveRouteUnitFactionKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  if (!game || !seeker || !targetEntity || !isLaneControlledUnit(seeker))
    return 0;

  const seekerFaction = resolveRouteUnitFactionKey(game, seeker);
  const pressureIndex = context && context.routeTargetPressureIndex
    ? context.routeTargetPressureIndex
    : null;
  const targetId = targetEntity && targetEntity.id
    ? targetEntity.id
    : null;
  if (pressureIndex && targetId) {
    let pressure = 0;
    const friendlyFactionKeys = getFriendlyPressureFactionKeys(
      pressureIndex,
      seekerFaction,
      areAllegiancesHostile
    );
    for (const factionKey of friendlyFactionKeys) {
      const targetCounts = pressureIndex.byFactionAndTarget.get(factionKey);
      if (!targetCounts)
        continue;
      pressure += targetCounts.get(targetId) || 0;
    }

    if (getCurrentCombatTargetUnitId(seeker) === targetId)
      pressure -= 1;
    return Math.max(0, pressure);
  }

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

function shouldUseLaneControlledSurroundSlots(attacker, target, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  if (!attacker || attacker.isDefender)
    return false;
  if (!target || (target.kind && target.kind !== "unit"))
    return false;

  if (getUnitAttackRange(attacker, deps) > 2.0)
    return false;

  if (isLaneControlledUnit(attacker))
    return true;

  return !!(
    attacker.isWaveUnit
    || String(attacker.spawnSourceType || "").trim().toLowerCase() === "dungeon_wave"
    || Number(attacker.ownerLaneIndex) < 0
  );
}

function getRouteUnitTargetPreferenceScore(game, lane, unit, target, requireAttackRange = false, deps = {}, context = null) {
  const liveTarget = target && target.entity
    ? target.entity
    : target;
  const centerDistance = getResolvedCombatTargetDistance(unit, liveTarget);
  if (!Number.isFinite(centerDistance))
    return Infinity;

  let approachDistance = centerDistance;
  if (lane
      && liveTarget
      && shouldUseLaneControlledSurroundSlots(unit, liveTarget, deps)) {
    const stopDistance = getUnitStopDistance(unit, liveTarget, deps);
    const slotPoint = getLaneControlledCombatPocketPoint(
      lane,
      unit,
      liveTarget,
      stopDistance,
      { appendAttackerIfMissing: true },
      deps,
      context
    );
    const slotDistance = Math.hypot(
      Number(unit.posX) - Number(slotPoint.x),
      Number(unit.posY) - Number(slotPoint.y)
    );
    if (Number.isFinite(slotDistance)) {
      approachDistance = Math.max(
        slotDistance,
        Math.max(0, centerDistance - stopDistance)
      );
    }
  }

  if (requireAttackRange || !requireDepFunction(deps, "isLaneControlledUnit")(unit) || !liveTarget)
    return approachDistance;

  const pressure = getRouteUnitTargetPressureCount(game, unit, liveTarget, deps, context);
  const penalty = Number(deps.ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY);
  return approachDistance + (pressure * (Number.isFinite(penalty) ? penalty : 0.4));
}

function shouldSwitchCombatTarget(game, lane, unit, currentTarget, nextTarget, deps = {}, context = null) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const normalizeLaneCommandState = requireDepFunction(deps, "normalizeLaneCommandState");
  if (!unit || !currentTarget || !nextTarget)
    return false;
  if (currentTarget.kind !== "unit" || nextTarget.kind !== "unit")
    return true;
  if (currentTarget.entity && nextTarget.entity && currentTarget.entity.id === nextTarget.entity.id)
    return false;

  const currentTargetEntity = currentTarget && currentTarget.entity
    ? currentTarget.entity
    : currentTarget;
  const currentDistance = getResolvedCombatTargetDistance(unit, currentTarget);
  const currentStopDistance = getUnitStopDistance(unit, currentTargetEntity, deps);
  const currentTargetInDirectRange = Number.isFinite(currentDistance)
    && Number.isFinite(currentStopDistance)
    && currentDistance <= currentStopDistance + getContactSlotTolerance(deps);
  if (currentTargetInDirectRange)
    return false;
  const currentTargetInContact = !!(lane && currentTargetEntity && isUnitInCombatContact(lane, unit, currentTargetEntity, deps, context));
  if (currentTargetInContact)
    return false;

  const nextDistance = getResolvedCombatTargetDistance(unit, nextTarget);
  if (!Number.isFinite(currentDistance))
    return true;
  if (!Number.isFinite(nextDistance))
    return false;

  const currentScore = Number.isFinite(Number(currentTarget && currentTarget.preferenceScore))
    ? Number(currentTarget.preferenceScore)
    : getRouteUnitTargetPreferenceScore(game, lane, unit, currentTarget, false, deps, context);
  const nextScore = Number.isFinite(Number(nextTarget && nextTarget.preferenceScore))
    ? Number(nextTarget.preferenceScore)
    : getRouteUnitTargetPreferenceScore(game, lane, unit, nextTarget, false, deps, context);
  const laneCommandStates = getLaneCommandStates(deps);
  const normalizedCommandState = normalizeLaneCommandState(unit.commandState);
  if (isLaneControlledUnit(unit)
      && normalizedCommandState === laneCommandStates.ATTACK
      && currentDistance <= getLaneControlledCombatLeashRadius(unit, deps) + getAnchorSupportTargetRadius(deps) + getContactSlotTolerance(deps)) {
    return false;
  }

  const switchMargin = normalizedCommandState === laneCommandStates.DEFEND
    ? (currentTargetInContact
      ? Math.min(getLaneCombatSwitchDistanceMargin(deps), 0.35)
      : 0.05)
    : getLaneCombatSwitchDistanceMargin(deps);

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

function getLaneControlledCombatLeashRadius(unit, deps = {}) {
  const anchorLeash = Number(unit && unit.anchorLeashRadius);
  const commandLeash = Number(unit && unit.combatLeashRadius);
  const leashRadius = Math.max(
    Number.isFinite(anchorLeash) && anchorLeash > 0 ? anchorLeash : 0,
    Number.isFinite(commandLeash) && commandLeash > 0 ? commandLeash : 0
  );
  if (leashRadius > 0)
    return leashRadius;
  return getLaneCommandCombatLeash(deps);
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
  if (distance <= maxRadius || distance < 0.001)
    return { x: safePointX, y: safePointY };

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

function shouldAnchorClampLaneControlledCombat(unit, target, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const shouldLaneControlledUnitFreeRoamInCombat = requireDepFunction(deps, "shouldLaneControlledUnitFreeRoamInCombat");
  const getLaneControlledSharedCombatAnchor = requireDepFunction(deps, "getLaneControlledSharedCombatAnchor");
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

  const sharedCombatRadius = getLaneControlledCombatLeashRadius(unit, deps) + getRouteSlotRowSpacing(deps);
  if (isPointWithinRadius(targetX, targetY, anchor.x, anchor.y, sharedCombatRadius))
    return true;

  return !isPointWithinRadius(targetX, targetY, Number(unit.posX), Number(unit.posY), sharedCombatRadius);
}

function resolveWaveCombatTarget(game, lane, combatTarget, deps = {}) {
  const getLaneByIndex = requireDepFunction(deps, "getLaneByIndex");
  const getBarracksSiteCombatTarget = requireDepFunction(deps, "getBarracksSiteCombatTarget");
  const getFortressPadState = requireDepFunction(deps, "getFortressPadState");
  const getLaneTownCorePad = requireDepFunction(deps, "getLaneTownCorePad");
  const getLaneTownCoreCombatTarget = requireDepFunction(deps, "getLaneTownCoreCombatTarget");
  const getFortressPadCombatTarget = requireDepFunction(deps, "getFortressPadCombatTarget");
  const findRouteUnitById = requireDepFunction(deps, "findRouteUnitById");
  if (!lane || !combatTarget || !combatTarget.unitId)
    return null;

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

  const unit = getLiveLaneUnitById(lane, combatTarget.unitId, game && game.tick);
  if (!unit) {
    const resolved = findRouteUnitById(
      game,
      combatTarget.unitId,
      Number.isInteger(combatTarget.laneIndex) ? combatTarget.laneIndex : lane.laneIndex
    );
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

function resolveContactFrame(attacker, target, deps = {}) {
  const getUnitForwardDirection = requireDepFunction(deps, "getUnitForwardDirection");
  const dx = Number(attacker && attacker.posX) - Number(target && target.posX);
  const dy = Number(attacker && attacker.posY) - Number(target && target.posY);
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (distance > 0.0001) {
    const forward = { x: dx / distance, y: dy / distance };
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
  const fallbackMagnitude = Math.sqrt((fallbackForward.x * fallbackForward.x) + (fallbackForward.y * fallbackForward.y));
  if (fallbackMagnitude > 0.0001) {
    const forward = {
      x: fallbackForward.x / fallbackMagnitude,
      y: fallbackForward.y / fallbackMagnitude,
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

function buildLaneContactSlotOccupancyIndex(lane, deps = {}) {
  const resolveLaneControlledUnitSortKey = requireDepFunction(deps, "resolveLaneControlledUnitSortKey");
  const occupancyByTarget = new Map();
  if (!lane || !Array.isArray(lane.units))
    return occupancyByTarget;

  for (const unit of lane.units) {
    if (!unit || unit.hp <= 0 || unit.isDefender || !unit.combatTarget || !unit.combatTarget.unitId)
      continue;

    const targetId = unit.combatTarget.unitId;
    let occupancy = occupancyByTarget.get(targetId);
    if (!occupancy) {
      occupancy = {
        attackers: [],
        indexById: null,
      };
      occupancyByTarget.set(targetId, occupancy);
    }

    occupancy.attackers.push(unit);
  }

  for (const occupancy of occupancyByTarget.values()) {
    occupancy.attackers.sort((left, right) => resolveLaneControlledUnitSortKey(left).localeCompare(resolveLaneControlledUnitSortKey(right)));
    const indexById = new Map();
    for (let i = 0; i < occupancy.attackers.length; i += 1)
      indexById.set(occupancy.attackers[i].id, i);
    occupancy.indexById = indexById;
  }

  return occupancyByTarget;
}

function getContactSlotOccupancySnapshot(lane, target, deps = {}, context = null) {
  const targetId = target && target.id
    ? target.id
    : null;
  if (!targetId) {
    return {
      attackers: [],
      indexById: new Map(),
    };
  }

  const laneCache = context && context.contactSlotOccupancyByLane instanceof WeakMap
    ? context.contactSlotOccupancyByLane
    : null;
  if (!laneCache) {
    const occupancyByTarget = buildLaneContactSlotOccupancyIndex(lane, deps);
    return occupancyByTarget.get(targetId) || {
      attackers: [],
      indexById: new Map(),
    };
  }

  let occupancyByTarget = laneCache.get(lane);
  if (!occupancyByTarget) {
    occupancyByTarget = buildLaneContactSlotOccupancyIndex(lane, deps);
    laneCache.set(lane, occupancyByTarget);
  }

  return occupancyByTarget.get(targetId) || {
    attackers: [],
    indexById: new Map(),
  };
}

function getLiveContactSlotOccupancySnapshot(occupancy, targetId) {
  if (!occupancy || !Array.isArray(occupancy.attackers) || !targetId) {
    return {
      attackers: [],
      indexById: new Map(),
    };
  }

  const attackers = [];
  const indexById = new Map();
  for (const attacker of occupancy.attackers) {
    if (!attacker || attacker.hp <= 0 || getCurrentCombatTargetUnitId(attacker) !== targetId)
      continue;
    indexById.set(attacker.id, attackers.length);
    attackers.push(attacker);
  }

  return {
    attackers,
    indexById,
  };
}

function getContactSlotPoint(lane, attacker, target, stopDistance, options = null, deps = {}, context = null) {
  const appendAttackerIfMissing = !!(options && options.appendAttackerIfMissing);
  const targetId = target && target.id
    ? target.id
    : null;
  const occupancy = getContactSlotOccupancySnapshot(lane, target, deps, context);
  const liveOccupancy = getLiveContactSlotOccupancySnapshot(occupancy, targetId);
  const attackers = liveOccupancy.attackers;

  const attackerIndex = liveOccupancy.indexById.has(attacker.id)
    ? liveOccupancy.indexById.get(attacker.id)
    : -1;
  const slotIndex = Math.max(0, attackerIndex >= 0 ? attackerIndex : (appendAttackerIfMissing ? attackers.length : 0));
  const effectiveAttackerCount = appendAttackerIfMissing && attackerIndex < 0
    ? attackers.length + 1
    : attackers.length;
  const radius = Math.max(0.75, getUnitContactRadius(attacker, deps) + getUnitContactRadius(target, deps));
  let centroidX = Number(attacker.posX) || Number(target.posX) || 0;
  let centroidY = Number(attacker.posY) || Number(target.posY) || 0;
  if (attackers.length > 0) {
    let sumX = 0;
    let sumY = 0;
    for (const occupiedAttacker of attackers) {
      sumX += Number(occupiedAttacker.posX) || 0;
      sumY += Number(occupiedAttacker.posY) || 0;
    }
    centroidX = sumX / attackers.length;
    centroidY = sumY / attackers.length;
  }
  let baseAngle = Math.atan2(centroidY - Number(target.posY), centroidX - Number(target.posX));
  if (!Number.isFinite(baseAngle)) {
    const frame = resolveContactFrame(attacker, target, deps);
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
    x: Number(target.posX) + (Math.cos(angle) * ringRadius),
    y: Number(target.posY) + (Math.sin(angle) * ringRadius),
  };
}

function getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance, options = null, deps = {}, context = null) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const shouldLaneControlledUnitFreeRoamInCombat = requireDepFunction(deps, "shouldLaneControlledUnitFreeRoamInCombat");
  const getLaneControlledSharedCombatAnchor = requireDepFunction(deps, "getLaneControlledSharedCombatAnchor");
  const desiredPoint = getContactSlotPoint(lane, attacker, target, stopDistance, options, deps, context);
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

  const leashRadius = getLaneControlledCombatLeashRadius(attacker, deps);
  const shouldAnchorClamp = shouldAnchorClampLaneControlledCombat(attacker, target, deps);
  const pocketRadius = Math.max(
    stopDistance + getRouteSlotRowSpacing(deps),
    Math.min(
      leashRadius * getLaneCombatPocketRadiusScale(deps),
      stopDistance + getLaneCombatPocketRadiusPadding(deps)
    )
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

function getCombatSlotArrivalTolerance(attacker, target, deps = {}) {
  const attackerRadius = getUnitContactRadius(attacker, deps);
  const targetRadius = getUnitContactRadius(target, deps);
  const combinedRadius = attackerRadius + targetRadius;
  return Math.max(0.35, Math.min(0.75, combinedRadius * 0.35));
}

function isUnitInCombatContact(lane, attacker, target, deps = {}, context = null) {
  if (!attacker || !target)
    return false;

  const distance = getWaveUnitTargetDistance(attacker, target);
  const stopDistance = getUnitStopDistance(attacker, target, deps);
  if (!Number.isFinite(distance) || !Number.isFinite(stopDistance) || distance > stopDistance + getContactSlotTolerance(deps))
    return false;

  if (!lane || !shouldUseLaneControlledSurroundSlots(attacker, target, deps))
    return true;

  const slotPoint = getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance, null, deps, context);
  const slotDistance = Math.hypot(
    Number(attacker.posX) - Number(slotPoint.x),
    Number(attacker.posY) - Number(slotPoint.y)
  );
  return Number.isFinite(slotDistance) && slotDistance <= getCombatSlotArrivalTolerance(attacker, target, deps);
}

function getStructureTargetAcquisitionRange(unit, target, deps = {}) {
  if (!unit || !target)
    return 0;
  return getUnitStopDistance(unit, target, deps) + getStructureTargetVicinityPadding(deps);
}

function isRouteUnitInsideFortressInterior(lane, target, deps = {}) {
  const getLaneTownCoreCombatTarget = requireDepFunction(deps, "getLaneTownCoreCombatTarget");
  if (!lane || !target)
    return false;

  const townCoreTarget = getLaneTownCoreCombatTarget(lane);
  if (!townCoreTarget)
    return false;

  const distanceToCore = getWaveUnitTargetDistance(target, townCoreTarget);
  return Number.isFinite(distanceToCore)
    && distanceToCore <= getFortressInteriorAssaultRadius(deps);
}

function updateBestHostileRouteUnitTarget(currentBest, candidate, laneIndex, distance, preferenceScore) {
  if (!candidate || !Number.isFinite(distance) || !Number.isFinite(preferenceScore))
    return currentBest;
  if (!currentBest
      || distance < currentBest.distance - 0.0001
      || (Math.abs(distance - currentBest.distance) <= 0.0001
        && preferenceScore < currentBest.preferenceScore)) {
    return {
      entity: candidate,
      laneIndex,
      distance,
      preferenceScore,
    };
  }
  return currentBest;
}

function scanHostileRouteUnitTargets(game, lane, unit, deps = {}, context = null) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const getLaneCommandStateForUnit = requireDepFunction(deps, "getLaneCommandStateForUnit");
  const getLaneCommandAnchorStateForUnit = requireDepFunction(deps, "getLaneCommandAnchorStateForUnit");
  const canEngageRouteUnitTarget = requireDepFunction(deps, "canEngageRouteUnitTarget");
  const isTargetInsideHomeFortressEmergencyZone = requireDepFunction(deps, "isTargetInsideHomeFortressEmergencyZone");
  const summary = {
    attackRange: null,
    engageRange: null,
    fortressInterior: null,
    directEngagement: null,
  };
  if (!game || !lane || !unit)
    return summary;

  const unitIsLaneControlled = isLaneControlledUnit(unit);
  const laneCommandStates = getLaneCommandStates(deps);
  const laneCommandState = unitIsLaneControlled
    ? getLaneCommandStateForUnit(game, unit)
    : null;
  const defendSeek = unitIsLaneControlled
    && laneCommandState === laneCommandStates.DEFEND;
  const attackSeek = unitIsLaneControlled
    && laneCommandState === laneCommandStates.ATTACK;
  const defendAnchorState = defendSeek
    ? getLaneCommandAnchorStateForUnit(game, unit)
    : null;
  const contactSlotTolerance = getContactSlotTolerance(deps);
  const baseEngagementRange = getUnitEngagementRange(unit, deps);
  const directEngagementRange = baseEngagementRange + contactSlotTolerance;
  const fortressInteriorRange = Math.max(baseEngagementRange, getFortressInteriorAssaultRadius(deps)) + contactSlotTolerance;
  const defendSeekRange = Math.max(
    baseEngagementRange,
    (Number(defendAnchorState && defendAnchorState.engagementRadius) || getLaneCommandDefenseRadius(deps))
      + getRouteSlotRowSpacing(deps)
  ) + contactSlotTolerance;
  const attackSeekRange = directEngagementRange;
  const combatUnitSpatialIndex = context && context.combatUnitSpatialIndex
    ? context.combatUnitSpatialIndex
    : buildCombatTargetSpatialIndex(game, deps);
  const scoreContext = {
    routeTargetPressureIndex: unitIsLaneControlled
      ? (context && context.routeTargetPressureIndex
        ? context.routeTargetPressureIndex
        : buildRouteUnitTargetPressureIndex(game, deps))
      : null,
    contactSlotOccupancyByLane: context && context.contactSlotOccupancyByLane instanceof WeakMap
      ? context.contactSlotOccupancyByLane
      : new WeakMap(),
  };
  const maxCandidateSeekDistance = Math.max(
    directEngagementRange,
    fortressInteriorRange,
    defendSeekRange,
    attackSeekRange
  );

  const candidateEntries = collectCombatTargetSpatialCandidates(
    combatUnitSpatialIndex,
    unit,
    maxCandidateSeekDistance
  );
  for (const entry of candidateEntries) {
    const candidateLane = entry && entry.lane;
    const candidate = entry && entry.unit;
    if (!candidateLane || !candidate || !canEngageRouteUnitTarget(game, unit, candidate))
      continue;

    const dx = Number(candidate.posX) - Number(unit.posX);
    const dy = Number(candidate.posY) - Number(unit.posY);
    const distance = Math.sqrt((dx * dx) + (dy * dy));
    if (!Number.isFinite(distance))
      continue;

    const emergencyInteriorTarget = defendSeek
      && isTargetInsideHomeFortressEmergencyZone(game, unit, candidate);
    const fortressInteriorTarget = isRouteUnitInsideFortressInterior(candidateLane, candidate, deps);
    const attackRangeLimit = getUnitStopDistance(unit, candidate, deps) + contactSlotTolerance;
    const engageRangeLimit = emergencyInteriorTarget
      ? fortressInteriorRange
      : (defendSeek ? defendSeekRange : attackSeekRange);
    const inAttackRange = distance <= attackRangeLimit;
    const inDirectEngagementRange = distance <= directEngagementRange;
    const inEngageRange = distance <= engageRangeLimit;
    const inFortressInteriorRange = fortressInteriorTarget && distance <= fortressInteriorRange;
    if (!inAttackRange && !inDirectEngagementRange && !inEngageRange && !inFortressInteriorRange)
      continue;

    let attackPreferenceScore = null;
    let generalPreferenceScore = null;

    if (inAttackRange) {
      attackPreferenceScore = getRouteUnitTargetPreferenceScore(
        game,
        candidateLane,
        unit,
        candidate,
        true,
        deps,
        scoreContext
      );
      if (unitIsLaneControlled
          && shouldUseLaneControlledSurroundSlots(unit, candidate, deps)
          && attackPreferenceScore > getCombatSlotArrivalTolerance(unit, candidate, deps) + contactSlotTolerance) {
        attackPreferenceScore = null;
      }
    }

    if (inDirectEngagementRange || inEngageRange || inFortressInteriorRange) {
      generalPreferenceScore = getRouteUnitTargetPreferenceScore(
        game,
        candidateLane,
        unit,
        candidate,
        false,
        deps,
        scoreContext
      );
    }

    if (Number.isFinite(attackPreferenceScore)) {
      summary.attackRange = updateBestHostileRouteUnitTarget(
        summary.attackRange,
        candidate,
        candidateLane.laneIndex,
        distance,
        attackPreferenceScore
      );
    }
    if (inDirectEngagementRange && Number.isFinite(generalPreferenceScore)) {
      summary.directEngagement = updateBestHostileRouteUnitTarget(
        summary.directEngagement,
        candidate,
        candidateLane.laneIndex,
        distance,
        generalPreferenceScore
      );
    }
    if (inEngageRange && Number.isFinite(generalPreferenceScore)) {
      summary.engageRange = updateBestHostileRouteUnitTarget(
        summary.engageRange,
        candidate,
        candidateLane.laneIndex,
        distance,
        generalPreferenceScore
      );
    }
    if (inFortressInteriorRange && Number.isFinite(generalPreferenceScore)) {
      summary.fortressInterior = updateBestHostileRouteUnitTarget(
        summary.fortressInterior,
        candidate,
        candidateLane.laneIndex,
        distance,
        generalPreferenceScore
      );
    }
  }

  return summary;
}

function findHostileRouteUnitTarget(game, lane, unit, requireAttackRange = false, options = null, deps = {}, context = null) {
  if (!game || !lane || !unit)
    return null;

  const directEngagementOnly = !!(options && options.directEngagementOnly);
  const fortressInteriorOnly = !!(options && options.fortressInteriorOnly);
  const summary = scanHostileRouteUnitTargets(game, lane, unit, deps, context);
  if (requireAttackRange)
    return summary.attackRange;
  if (directEngagementOnly)
    return summary.directEngagement;
  if (fortressInteriorOnly)
    return summary.fortressInterior;
  return summary.engageRange;
}

function getWaveUnitPreferredTarget(game, lane, unit, deps = {}, context = null) {
  const targetSummary = scanHostileRouteUnitTargets(game, lane, unit, deps, context);
  const routeUnitInAttackRange = targetSummary.attackRange;
  if (routeUnitInAttackRange) {
    return {
      kind: "unit",
      entity: routeUnitInAttackRange.entity,
      laneIndex: routeUnitInAttackRange.laneIndex,
      reason: "route_unit_attack_range",
      preferenceScore: routeUnitInAttackRange.preferenceScore,
    };
  }

  const routeUnitInEngageRange = targetSummary.engageRange;
  if (routeUnitInEngageRange) {
    return {
      kind: "unit",
      entity: routeUnitInEngageRange.entity,
      laneIndex: routeUnitInEngageRange.laneIndex,
      reason: "route_unit_engage_range",
      preferenceScore: routeUnitInEngageRange.preferenceScore,
    };
  }

  const fortressInteriorTarget = targetSummary.fortressInterior;
  if (fortressInteriorTarget) {
    return {
      kind: "unit",
      entity: fortressInteriorTarget.entity,
      laneIndex: fortressInteriorTarget.laneIndex,
      reason: "fortress_interior_clearance",
      preferenceScore: fortressInteriorTarget.preferenceScore,
    };
  }

  return null;
}

function isRouteUnitTargetBlockedByStructure(game, lane, unit, targetDescriptor, blockingTarget, deps = {}) {
  const getLaneByIndex = requireDepFunction(deps, "getLaneByIndex");
  const getLaneTownCoreCombatTarget = requireDepFunction(deps, "getLaneTownCoreCombatTarget");
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const defensiveStructureTypes = new Set(["town_core", "wall", "gate", "turret"]);
  if (!game || !lane || !unit || !targetDescriptor || targetDescriptor.kind !== "unit" || !targetDescriptor.entity)
    return false;

  const targetLaneIndex = Number.isInteger(targetDescriptor.laneIndex)
    ? targetDescriptor.laneIndex
    : lane.laneIndex;
  const blockingLaneIndex = blockingTarget && Number.isInteger(blockingTarget.laneIndex)
    ? blockingTarget.laneIndex
    : targetLaneIndex;
  if (Number.isInteger(targetLaneIndex)
      && Number.isInteger(blockingLaneIndex)
      && targetLaneIndex !== blockingLaneIndex) {
    return false;
  }

  const targetLane = Number.isInteger(targetLaneIndex)
    ? (getLaneByIndex(game, targetLaneIndex) || lane)
    : lane;
  const townCoreTarget = getLaneTownCoreCombatTarget(targetLane);
  if (!townCoreTarget)
    return false;

  const targetEntity = targetDescriptor.entity;
  const targetCoreDistance = Math.hypot(
    Number(targetEntity.posX) - Number(townCoreTarget.posX),
    Number(targetEntity.posY) - Number(townCoreTarget.posY)
  );
  if (!Number.isFinite(targetCoreDistance))
    return false;
  const unitCoreDistance = Math.hypot(
    Number(unit.posX) - Number(townCoreTarget.posX),
    Number(unit.posY) - Number(townCoreTarget.posY)
  );
  const homeLaneIndex = Number.isInteger(unit && unit.ownerLaneIndex)
    ? unit.ownerLaneIndex
    : Number.isInteger(unit && unit.ownerLane)
      ? unit.ownerLane
      : Number.isInteger(unit && unit.sourceLaneIndex)
        ? unit.sourceLaneIndex
        : null;
  const rangedHomeLaneUnit = !!(
    isLaneControlledUnit(unit)
    && Number.isInteger(homeLaneIndex)
    && Number.isInteger(targetLaneIndex)
    && homeLaneIndex === targetLaneIndex
    && getUnitAttackRange(unit, deps) > 2.0
  );
  const meleeHomeLaneUnit = !!(
    isLaneControlledUnit(unit)
    && Number.isInteger(homeLaneIndex)
    && Number.isInteger(targetLaneIndex)
    && homeLaneIndex === targetLaneIndex
    && getUnitAttackRange(unit, deps) <= 2.0
  );

  const candidateStructures = [];
  if (blockingTarget)
    candidateStructures.push(blockingTarget);
  for (const candidate of getLaneBlockingStructureTargets(targetLane, unit, deps)) {
    const buildingType = String(candidate && (candidate.buildingType || candidate.type) || "").trim().toLowerCase();
    if (!candidate || !defensiveStructureTypes.has(buildingType))
      continue;
    candidateStructures.push(candidate);
  }

  for (const candidate of candidateStructures) {
    const blockerCoreDistance = Math.hypot(
      Number(candidate.posX) - Number(townCoreTarget.posX),
      Number(candidate.posY) - Number(townCoreTarget.posY)
    );
    if (!Number.isFinite(blockerCoreDistance))
      continue;
    const unitRelativeToBlocker = Number.isFinite(unitCoreDistance)
      ? unitCoreDistance - blockerCoreDistance
      : null;
    const targetRelativeToBlocker = targetCoreDistance - blockerCoreDistance;
    if (Number.isFinite(unitRelativeToBlocker)
        && (unitRelativeToBlocker * targetRelativeToBlocker) >= -0.01) {
      continue;
    }
    if (rangedHomeLaneUnit)
      continue;
    if (meleeHomeLaneUnit)
      return true;
    if (targetCoreDistance + 0.5 >= blockerCoreDistance)
      continue;
    return true;
  }

  return false;
}

function hasImmediateFollowThroughCombatTarget(game, lane, unit, deps = {}, context = null) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  if (!game || !lane || !unit || !isLaneControlledUnit(unit))
    return false;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return false;

  const targetSummary = scanHostileRouteUnitTargets(game, lane, unit, deps, context);
  return !!(targetSummary.attackRange || targetSummary.directEngagement || targetSummary.fortressInterior);
}

function getLaneBlockingStructureTargets(lane, unit = null, deps = {}) {
  const getFortressPadCombatTarget = requireDepFunction(deps, "getFortressPadCombatTarget");
  const getBarracksSiteCombatTarget = requireDepFunction(deps, "getBarracksSiteCombatTarget");
  const isWaveStructureSeeker = !!(
    unit
    && (unit.isWaveUnit
      || String(unit.spawnSourceType || "").trim().toLowerCase() === "dungeon_wave"
      || Number(unit.ownerLaneIndex) < 0)
  );
  const targets = [];
  if (!lane)
    return targets;

  if (Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      const buildingType = String(pad && pad.buildingType || "").trim().toLowerCase();
      if (isWaveStructureSeeker
          && buildingType !== "town_core"
          && buildingType !== "wall"
          && buildingType !== "gate"
          && buildingType !== "turret") {
        continue;
      }
      const target = getFortressPadCombatTarget(lane, pad);
      if (target)
        targets.push(target);
    }
  }

  for (const siteDef of getBarracksSiteDefs(deps)) {
    const target = getBarracksSiteCombatTarget(lane, siteDef.barracksId);
    if (target)
      targets.push(target);
  }

  return targets;
}

function findBlockingStructureTarget(game, lane, unit, deps = {}) {
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  const isRouteUnitHostileToLane = requireDepFunction(deps, "isRouteUnitHostileToLane");
  if (!game || !unit)
    return null;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return null;

  let best = null;
  let bestDistance = Infinity;
  for (const candidateLane of game.lanes || []) {
    if (!candidateLane || candidateLane.eliminated || !isRouteUnitHostileToLane(game, candidateLane, unit))
      continue;

    const candidates = getLaneBlockingStructureTargets(candidateLane, unit, deps);
    for (const candidate of candidates) {
      const dx = Number(candidate.posX) - Number(unit.posX);
      const dy = Number(candidate.posY) - Number(unit.posY);
      const straightDistance = Math.sqrt((dx * dx) + (dy * dy));
      const distanceLimit = getStructureTargetAcquisitionRange(unit, candidate, deps);
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

function markTownCoreBreach(game, lane, unit, townCoreTarget, deps = {}) {
  const combatLog = deps && deps.combatLog;
  const recordBalanceLeak = typeof deps.recordBalanceLeak === "function"
    ? deps.recordBalanceLeak
    : null;
  const getLaneTownCoreHp = requireDepFunction(deps, "getLaneTownCoreHp");
  const getLaneTownCoreMaxHp = requireDepFunction(deps, "getLaneTownCoreMaxHp");
  if (!game || !lane || !unit || unit.hasBreachedTownCore)
    return;

  unit.hasBreachedTownCore = true;
  lane.totalLeaksTaken += 1;
  lane.leakCountThisRound += 1;
  logVerboseAuditInfo(deps, "[TownCoreTrace] core target acquired", {
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
  if (combatLog && typeof combatLog.logEvent === "function") {
    combatLog.logEvent(game, "leak", {
      unitId: unit.id,
      unitType: unit.type,
      lane: lane.laneIndex,
      targetPadId: townCoreTarget ? townCoreTarget.padId || townCoreTarget.id : null,
      breachOnly: true,
    });
  }
  if (recordBalanceLeak)
    recordBalanceLeak(game, lane, unit, townCoreTarget);
}

function attackFortressPad(game, lane, attacker, target, deps = {}) {
  const getLaneByIndex = requireDepFunction(deps, "getLaneByIndex");
  const getTowerStats = requireDepFunction(deps, "getTowerStats");
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const getBarracksSiteState = requireDepFunction(deps, "getBarracksSiteState");
  const applyBarracksSiteDamage = requireDepFunction(deps, "applyBarracksSiteDamage");
  const getFortressPadState = requireDepFunction(deps, "getFortressPadState");
  const applyFortressPadDamage = requireDepFunction(deps, "applyFortressPadDamage");
  if (!game || !lane || !attacker || !target || target.kind !== "fortress_pad") {
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };
  }

  const targetLane = Number.isInteger(target.laneIndex)
    ? (getLaneByIndex(game, target.laneIndex) || lane)
    : lane;
  const fallbackTowerStats = getTowerStats(attacker.type, 1);
  const unitDef = resolveUnitDef(attacker.type);
  const rawDamage = Math.max(
    1,
    Math.floor(Number(attacker.baseDmg) || Number(unitDef && unitDef.dmg) || Number(fallbackTowerStats && fallbackTowerStats.dmg) || 1)
  );
  const cooldownTicks = Math.max(
    1,
    Math.floor(
      Number(attacker.atkCdTicks)
      || Number(unitDef && unitDef.atkCdTicks)
      || Number(fallbackTowerStats && fallbackTowerStats.atkCdTicks)
      || 20
    )
  );
  const targetId = String(target.padId || target.unitId || target.id || "");
  const barracksSiteMatch = targetId.match(/^barracks_site:(.+)$/);
  const targetPad = barracksSiteMatch ? null : getFortressPadState(targetLane, targetId);
  const targetSite = barracksSiteMatch ? getBarracksSiteState(targetLane, barracksSiteMatch[1], game) : null;
  const prevHp = targetPad
    ? Math.max(0, Math.floor(Number(targetPad.hp) || 0))
    : targetSite
      ? Math.max(0, Math.floor(Number(targetSite.hp) || 0))
      : 0;
  const result = barracksSiteMatch
    ? applyBarracksSiteDamage(game, targetLane, barracksSiteMatch[1], rawDamage)
    : applyFortressPadDamage(game, targetLane, targetId, rawDamage);

  attacker.attackPulse = (attacker.attackPulse || 0) + 1;
  attacker.atkCd = cooldownTicks;
  if (targetPad && targetPad.buildingType === "town_core") {
    logVerboseAuditInfo(deps, "[TownCoreTrace] core attacked", {
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

function clampUnitToAnchor(unit, anchorX, anchorY, maxRadius, minX, maxX, minY, maxY, deps = {}) {
  const syncMovedUnitPathState = requireDepFunction(deps, "syncMovedUnitPathState");
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY) || maxRadius <= 0)
    return;

  const dx = Number(unit.posX) - anchorX;
  const dy = Number(unit.posY) - anchorY;
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (distance <= maxRadius || distance < 0.001)
    return;

  const scale = maxRadius / distance;
  unit.posX = Math.max(minX, Math.min(maxX, anchorX + (dx * scale)));
  unit.posY = Math.max(minY, Math.min(maxY, anchorY + (dy * scale)));
  syncMovedUnitPathState(unit);
}

function clampLaneControlledUnitToCombatLeash(unit, minX, maxX, minY, maxY, target = null, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const getLaneControlledSharedCombatAnchor = requireDepFunction(deps, "getLaneControlledSharedCombatAnchor");
  if (!isLaneControlledUnit(unit))
    return false;
  if (target && !shouldAnchorClampLaneControlledCombat(unit, target, deps))
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
    getLaneControlledCombatLeashRadius(unit, deps),
    minX,
    maxX,
    minY,
    maxY,
    deps
  );
  return previousX !== Number(unit.posX) || previousY !== Number(unit.posY);
}

function isLaneControlledUnitSettledAtAnchor(unit, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const unitMovementModes = getUnitMovementModes(deps);
  if (!isLaneControlledUnit(unit))
    return false;
  if (unit.combatTarget || unit.movementMode === unitMovementModes.COMBAT_ENGAGE)
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

  return Math.sqrt((dx * dx) + (dy * dy)) <= getLaneAnchorSettledSeparationDistance(deps);
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

function getPairSpacing(left, right, fallback, deps = {}, context = null) {
  const leftContactRadius = Number.isFinite(Number(context && context.leftContactRadius))
    ? Number(context.leftContactRadius)
    : getUnitContactRadius(left, deps);
  const rightContactRadius = Number.isFinite(Number(context && context.rightContactRadius))
    ? Number(context.rightContactRadius)
    : getUnitContactRadius(right, deps);
  let spacing = Math.max(
    fallback,
    leftContactRadius + rightContactRadius
  );

  const leftTargetId = context && context.leftTargetId !== undefined
    ? context.leftTargetId
    : getCurrentCombatTargetUnitId(left);
  const rightTargetId = context && context.rightTargetId !== undefined
    ? context.rightTargetId
    : getCurrentCombatTargetUnitId(right);
  const leftAttackRange = Number.isFinite(Number(context && context.leftAttackRange))
    ? Number(context.leftAttackRange)
    : getUnitAttackRange(left, deps);
  const rightAttackRange = Number.isFinite(Number(context && context.rightAttackRange))
    ? Number(context.rightAttackRange)
    : getUnitAttackRange(right, deps);
  const sharedTargetSpacing = !!(
    getUsePerUnitAnchorSlots(deps)
    && leftTargetId
    && rightTargetId
    && leftTargetId === rightTargetId
    && leftAttackRange <= 2.0
    && rightAttackRange <= 2.0
  );
  if (sharedTargetSpacing) {
    spacing = Math.max(
      spacing,
      leftContactRadius + rightContactRadius + 0.20
    );
  }

  return spacing;
}

function tryResolveSettledAnchorPairSpacing(left, right, minSpacing, fallbackSpacing, deps = {}) {
  if (!isLaneControlledUnitSettledAtAnchor(left, deps) || !isLaneControlledUnitSettledAtAnchor(right, deps))
    return null;
  if (!Number.isFinite(Number(left && left.anchorTargetX)) || !Number.isFinite(Number(left && left.anchorTargetY)))
    return null;
  if (!Number.isFinite(Number(right && right.anchorTargetX)) || !Number.isFinite(Number(right && right.anchorTargetY)))
    return null;

  const anchorDx = Number(right.anchorTargetX) - Number(left.anchorTargetX);
  const anchorDy = Number(right.anchorTargetY) - Number(left.anchorTargetY);
  const anchorDistance = Math.sqrt((anchorDx * anchorDx) + (anchorDy * anchorDy));
  if (!Number.isFinite(anchorDistance) || anchorDistance <= 0)
    return null;

  const settledSlotSpacing = Math.max(
    minSpacing,
    anchorDistance - getLaneAnchorSettledSlotSpacingSlack(deps)
  );
  return Math.min(fallbackSpacing, settledSlotSpacing);
}

function clampLaneControlledUnitToAnchorDrift(unit, minX, maxX, minY, maxY, deps = {}) {
  const syncMovedUnitPathState = requireDepFunction(deps, "syncMovedUnitPathState");
  if (!isLaneControlledUnitSettledAtAnchor(unit, deps))
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
    -getLaneAnchorSettledMaxLateralOffset(deps),
    Math.min(getLaneAnchorSettledMaxLateralOffset(deps), lateralOffset)
  );
  if (Math.abs(clampedOffset - lateralOffset) <= 0.0001)
    return false;

  unit.posX = Math.max(minX, Math.min(maxX, Number(unit.posX) + (lateralAxis.x * (clampedOffset - lateralOffset))));
  unit.posY = Math.max(minY, Math.min(maxY, Number(unit.posY) + (lateralAxis.y * (clampedOffset - lateralOffset))));
  syncMovedUnitPathState(unit);
  return true;
}

function getPairLateralAxis(left, right) {
  const axisLeft = getUnitAnchorLateralAxis(left);
  const axisRight = getUnitAnchorLateralAxis(right);
  if (axisLeft && axisRight) {
    const summedX = axisLeft.x + axisRight.x;
    const summedY = axisLeft.y + axisRight.y;
    const magnitude = Math.sqrt((summedX * summedX) + (summedY * summedY));
    if (magnitude >= 0.001) {
      return {
        x: summedX / magnitude,
        y: summedY / magnitude,
      };
    }
  }

  return axisLeft || axisRight || { x: 1, y: 0 };
}

function shouldPreferLateralSpacing(left, right, dx, dy, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  if (getUsePerUnitAnchorSlots(deps) && (isLaneControlledUnit(left) || isLaneControlledUnit(right)))
    return false;
  return !!(left && right && !left.isDefender && !right.isDefender && Math.abs(dy) >= Math.abs(dx));
}

function resolveExactOverlapAxis(left, right) {
  const seedSource = `${String(left && left.id || "")}|${String(right && right.id || "")}`;
  let hash = 0;
  for (let i = 0; i < seedSource.length; i += 1)
    hash = ((hash * 33) + seedSource.charCodeAt(i)) >>> 0;

  const angle = (hash % 360) * (Math.PI / 180);
  return {
    x: Math.cos(angle),
    y: Math.sin(angle),
  };
}

function getSharedTargetTangentialAxis(game, lane, left, right, deps = {}) {
  const leftTargetId = left && left.combatTarget && left.combatTarget.unitId
    ? left.combatTarget.unitId
    : (left ? left.combatTargetId : null);
  const rightTargetId = right && right.combatTarget && right.combatTarget.unitId
    ? right.combatTarget.unitId
    : (right ? right.combatTargetId : null);
  if (!leftTargetId || !rightTargetId || leftTargetId !== rightTargetId)
    return null;

  const liveTarget = resolveWaveCombatTarget(
    game,
    lane,
    left && left.combatTarget && left.combatTarget.unitId ? left.combatTarget : (right ? right.combatTarget : null),
    deps
  );
  const targetX = Number(liveTarget && liveTarget.posX);
  const targetY = Number(liveTarget && liveTarget.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return null;

  const leftDistance = getWaveUnitTargetDistance(left, liveTarget);
  const rightDistance = getWaveUnitTargetDistance(right, liveTarget);
  const leftStopDistance = getUnitStopDistance(left, liveTarget, deps);
  const rightStopDistance = getUnitStopDistance(right, liveTarget, deps);
  const tangentialSpreadDistance = Math.max(2.0, Math.max(leftStopDistance, rightStopDistance) + 1.25);
  if (!Number.isFinite(leftDistance) || !Number.isFinite(rightDistance)
      || leftDistance > tangentialSpreadDistance
      || rightDistance > tangentialSpreadDistance) {
    return null;
  }

  const midX = (Number(left.posX) + Number(right.posX)) * 0.5;
  const midY = (Number(left.posY) + Number(right.posY)) * 0.5;
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

function shouldApplySimpleLaneHostileCollision(game, left, right, distance, deps = {}, context = null) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const areRouteUnitsHostile = requireDepFunction(deps, "areRouteUnitsHostile");
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  const usePerUnitAnchorSlots = context && typeof context.usePerUnitAnchorSlots === "boolean"
    ? context.usePerUnitAnchorSlots
    : getUsePerUnitAnchorSlots(deps);
  if (!usePerUnitAnchorSlots)
    return false;
  if (!left || !right || !Number.isFinite(distance))
    return false;
  if ((left.combatTargetId && left.combatTargetId === right.id) || (right.combatTargetId && right.combatTargetId === left.id))
    return false;

  const leftLaneControlled = context && typeof context.leftLaneControlled === "boolean"
    ? context.leftLaneControlled
    : isLaneControlledUnit(left);
  const rightLaneControlled = context && typeof context.rightLaneControlled === "boolean"
    ? context.rightLaneControlled
    : isLaneControlledUnit(right);
  const laneControlled = leftLaneControlled
    ? left
    : (rightLaneControlled ? right : null);
  const other = laneControlled === left ? right : left;
  if (!laneControlled || !other)
    return false;
  if (!areRouteUnitsHostile(game, laneControlled, other))
    return false;
  if (!canLaneControlledUnitSeekCombat(game, laneControlled, other))
    return false;

  const stopDistance = getUnitStopDistance(laneControlled, other, deps);
  const collisionWakeDistance = Math.max(2.4, stopDistance + 0.9);
  return distance <= collisionWakeDistance;
}

function getSpatialBucketCellCoordinate(value, cellSize) {
  return Math.floor((Number(value) || 0) / cellSize);
}

function getSpatialBucketKey(cellX, cellY) {
  return `${cellX},${cellY}`;
}

function buildSeparationSpatialIndex(units, cellSize) {
  const buckets = new Map();
  const unitCells = new Array(units.length);
  for (let index = 0; index < units.length; index += 1) {
    const unit = units[index];
    const cellX = getSpatialBucketCellCoordinate(unit && unit.posX, cellSize);
    const cellY = getSpatialBucketCellCoordinate(unit && unit.posY, cellSize);
    const key = getSpatialBucketKey(cellX, cellY);
    let bucket = buckets.get(key);
    if (!bucket) {
      bucket = new Set();
      buckets.set(key, bucket);
    }
    bucket.add(index);
    unitCells[index] = { cellX, cellY, key };
  }
  return { buckets, unitCells };
}

function updateSeparationSpatialIndexUnit(index, units, cellSize, spatialIndex) {
  const unit = units[index];
  const nextCellX = getSpatialBucketCellCoordinate(unit && unit.posX, cellSize);
  const nextCellY = getSpatialBucketCellCoordinate(unit && unit.posY, cellSize);
  const nextKey = getSpatialBucketKey(nextCellX, nextCellY);
  const previousCell = spatialIndex.unitCells[index];
  if (previousCell
      && previousCell.cellX === nextCellX
      && previousCell.cellY === nextCellY
      && previousCell.key === nextKey) {
    return;
  }

  if (previousCell) {
    const previousBucket = spatialIndex.buckets.get(previousCell.key);
    if (previousBucket) {
      previousBucket.delete(index);
      if (previousBucket.size === 0)
        spatialIndex.buckets.delete(previousCell.key);
    }
  }

  let nextBucket = spatialIndex.buckets.get(nextKey);
  if (!nextBucket) {
    nextBucket = new Set();
    spatialIndex.buckets.set(nextKey, nextBucket);
  }
  nextBucket.add(index);
  spatialIndex.unitCells[index] = { cellX: nextCellX, cellY: nextCellY, key: nextKey };
}

function collectSeparationNeighborIndices(index, units, cellSize, spatialIndex, checkedIndices) {
  const cell = spatialIndex.unitCells[index];
  if (!cell)
    return [];

  const neighbors = [];
  for (let dx = -1; dx <= 1; dx += 1) {
    for (let dy = -1; dy <= 1; dy += 1) {
      const bucket = spatialIndex.buckets.get(getSpatialBucketKey(cell.cellX + dx, cell.cellY + dy));
      if (!bucket)
        continue;
      for (const otherIndex of bucket) {
        if (otherIndex <= index || checkedIndices.has(otherIndex))
          continue;
        neighbors.push(otherIndex);
      }
    }
  }

  neighbors.sort((left, right) => left - right);
  return neighbors;
}

function applySeparation2D(game, lane, units, minSpacing, minX, maxX, minY, maxY, deps = {}) {
  const syncMovedUnitPathState = requireDepFunction(deps, "syncMovedUnitPathState");
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const shouldLaneControlledUnitFreeRoamInCombat = requireDepFunction(deps, "shouldLaneControlledUnitFreeRoamInCombat");
  const unitMovementModes = getUnitMovementModes(deps);
  const usePerUnitAnchorSlots = getUsePerUnitAnchorSlots(deps);
  const sepDamp = getSepDamp(deps);
  const sepMaxPush = getSepMaxPush(deps);
  const engageMovementMode = unitMovementModes.COMBAT_ENGAGE;
  const unitState = new Array(units.length);
  let maxContactRadius = 0;
  for (let i = 0; i < units.length; i += 1) {
    const unit = units[i];
    const laneControlled = isLaneControlledUnit(unit);
    const contactRadius = getUnitContactRadius(unit, deps);
    const attackRange = getUnitAttackRange(unit, deps);
    unitState[i] = {
      laneControlled,
      freeRoamInCombat: laneControlled && shouldLaneControlledUnitFreeRoamInCombat(unit),
      contactRadius,
      attackRange,
      targetId: getCurrentCombatTargetUnitId(unit),
    };
    if (contactRadius > maxContactRadius)
      maxContactRadius = contactRadius;
  }
  const maxInteractionDistance = Math.max(minSpacing, (maxContactRadius * 2) + 0.20);
  const cellSize = Math.max(0.5, maxInteractionDistance);

  for (let pass = 0; pass < 2; pass += 1) {
    const spatialIndex = buildSeparationSpatialIndex(units, cellSize);
    for (let i = 0; i < units.length; i += 1) {
      const checkedIndices = new Set();
      while (true) {
        const neighborIndices = collectSeparationNeighborIndices(i, units, cellSize, spatialIndex, checkedIndices);
        if (neighborIndices.length === 0)
          break;

        for (const j of neighborIndices) {
          checkedIndices.add(j);
        const left = units[i];
        const right = units[j];
        const leftState = unitState[i];
        const rightState = unitState[j];
        const simpleLaneSpacing = usePerUnitAnchorSlots
          && (leftState.laneControlled || rightState.laneControlled);
        let dx = Number(right.posX) - Number(left.posX);
        let dy = Number(right.posY) - Number(left.posY);
        let distance = Math.sqrt((dx * dx) + (dy * dy));
        const activeCombatSpacing = !simpleLaneSpacing
          || leftState.freeRoamInCombat
          || rightState.freeRoamInCombat
          || shouldApplySimpleLaneHostileCollision(game, left, right, distance, deps, {
            usePerUnitAnchorSlots,
            leftLaneControlled: leftState.laneControlled,
            rightLaneControlled: rightState.laneControlled,
          });
        if (!activeCombatSpacing)
          continue;

        if (distance < 0.001) {
          const overlapAxis = resolveExactOverlapAxis(left, right);
          dx = overlapAxis.x * 0.001;
          dy = overlapAxis.y * 0.001;
          distance = 0.001;
        }

        let pairSpacing = getPairSpacing(left, right, minSpacing, deps, {
          leftContactRadius: leftState.contactRadius,
          rightContactRadius: rightState.contactRadius,
          leftAttackRange: leftState.attackRange,
          rightAttackRange: rightState.attackRange,
          leftTargetId: leftState.targetId,
          rightTargetId: rightState.targetId,
        });
        if (distance >= pairSpacing)
          continue;

        const settledLeft = isLaneControlledUnitSettledAtAnchor(left, deps);
        const settledRight = isLaneControlledUnitSettledAtAnchor(right, deps);
        if (settledLeft && settledRight) {
          const settledPairSpacing = tryResolveSettledAnchorPairSpacing(left, right, minSpacing, pairSpacing, deps);
          if (Number.isFinite(settledPairSpacing))
            pairSpacing = settledPairSpacing;
        }
        if (distance >= pairSpacing)
          continue;

        const separationScale = settledLeft && settledRight
          ? getLaneAnchorSettledSeparationScale(deps)
          : (settledLeft || settledRight ? getLaneAnchorMixedSeparationScale(deps) : 1);
        const push = Math.min((pairSpacing - distance) * sepDamp, sepMaxPush) * separationScale;
        if (push <= 0.0001)
          continue;

        const sharedTargetTangentialAxis = simpleLaneSpacing && activeCombatSpacing
          ? getSharedTargetTangentialAxis(game, lane, left, right, deps)
          : null;
        if (sharedTargetTangentialAxis) {
          const projectionLeft = (Number(left.posX) * sharedTargetTangentialAxis.x) + (Number(left.posY) * sharedTargetTangentialAxis.y);
          const projectionRight = (Number(right.posX) * sharedTargetTangentialAxis.x) + (Number(right.posY) * sharedTargetTangentialAxis.y);
          const chooseNegativeLeft = projectionLeft < projectionRight
            || (Math.abs(projectionLeft - projectionRight) < 0.001 && String(left.id) <= String(right.id));
          const negative = chooseNegativeLeft ? left : right;
          const positive = chooseNegativeLeft ? right : left;
          negative.posX = Math.max(minX, Math.min(maxX, negative.posX - (sharedTargetTangentialAxis.x * push)));
          negative.posY = Math.max(minY, Math.min(maxY, negative.posY - (sharedTargetTangentialAxis.y * push)));
          positive.posX = Math.max(minX, Math.min(maxX, positive.posX + (sharedTargetTangentialAxis.x * push)));
          positive.posY = Math.max(minY, Math.min(maxY, positive.posY + (sharedTargetTangentialAxis.y * push)));
          syncMovedUnitPathState(negative);
          syncMovedUnitPathState(positive);
          updateSeparationSpatialIndexUnit(chooseNegativeLeft ? i : j, units, cellSize, spatialIndex);
          updateSeparationSpatialIndexUnit(chooseNegativeLeft ? j : i, units, cellSize, spatialIndex);
          continue;
        }

        if (!simpleLaneSpacing && (shouldPreferLateralSpacing(left, right, dx, dy, deps) || (settledLeft && settledRight))) {
          const lateralAxis = getPairLateralAxis(left, right);
          const projectionLeft = (Number(left.posX) * lateralAxis.x) + (Number(left.posY) * lateralAxis.y);
          const projectionRight = (Number(right.posX) * lateralAxis.x) + (Number(right.posY) * lateralAxis.y);
          const chooseNegativeLeft = projectionLeft < projectionRight
            || (Math.abs(projectionLeft - projectionRight) < 0.001 && String(left.id) <= String(right.id));
          const negative = chooseNegativeLeft ? left : right;
          const positive = chooseNegativeLeft ? right : left;
          negative.posX = Math.max(minX, Math.min(maxX, negative.posX - (lateralAxis.x * push)));
          negative.posY = Math.max(minY, Math.min(maxY, negative.posY - (lateralAxis.y * push)));
          positive.posX = Math.max(minX, Math.min(maxX, positive.posX + (lateralAxis.x * push)));
          positive.posY = Math.max(minY, Math.min(maxY, positive.posY + (lateralAxis.y * push)));
          syncMovedUnitPathState(negative);
          syncMovedUnitPathState(positive);
          if (negative.movementMode !== engageMovementMode)
            clampLaneControlledUnitToCombatLeash(negative, minX, maxX, minY, maxY, null, deps);
          if (positive.movementMode !== engageMovementMode)
            clampLaneControlledUnitToCombatLeash(positive, minX, maxX, minY, maxY, null, deps);
          clampLaneControlledUnitToAnchorDrift(negative, minX, maxX, minY, maxY, deps);
          clampLaneControlledUnitToAnchorDrift(positive, minX, maxX, minY, maxY, deps);
          updateSeparationSpatialIndexUnit(chooseNegativeLeft ? i : j, units, cellSize, spatialIndex);
          updateSeparationSpatialIndexUnit(chooseNegativeLeft ? j : i, units, cellSize, spatialIndex);
          continue;
        }

        left.posX = Math.max(minX, Math.min(maxX, left.posX - ((dx / distance) * push)));
        left.posY = Math.max(minY, Math.min(maxY, left.posY - ((dy / distance) * push)));
        syncMovedUnitPathState(left);
        if (left.movementMode !== engageMovementMode)
          clampLaneControlledUnitToCombatLeash(left, minX, maxX, minY, maxY, null, deps);
        if (!simpleLaneSpacing)
          clampLaneControlledUnitToAnchorDrift(left, minX, maxX, minY, maxY, deps);

        right.posX = Math.max(minX, Math.min(maxX, right.posX + ((dx / distance) * push)));
        right.posY = Math.max(minY, Math.min(maxY, right.posY + ((dy / distance) * push)));
        syncMovedUnitPathState(right);
        if (right.movementMode !== engageMovementMode)
          clampLaneControlledUnitToCombatLeash(right, minX, maxX, minY, maxY, null, deps);
        if (!simpleLaneSpacing)
          clampLaneControlledUnitToAnchorDrift(right, minX, maxX, minY, maxY, deps);
        updateSeparationSpatialIndexUnit(i, units, cellSize, spatialIndex);
        updateSeparationSpatialIndexUnit(j, units, cellSize, spatialIndex);
        }
      }
    }
  }
}

function traceWaveUnitTick(game, lane, unit, target, details = {}, deps = {}) {
  const log = deps && deps.log;
  if (!getEnableWaveUnitTrace(deps))
    return;
  if (!game || !lane || !unit)
    return;

  const targetId = target ? (target.unitId || target.id || null) : null;
  const targetType = target
    ? (target.kind === "unit"
      ? "defender"
      : target.kind === "fortress_pad"
        ? (target.buildingType || target.type || "fortress_pad")
        : target.kind || null)
    : null;
  const distanceToTarget = target ? getWaveUnitTargetDistance(unit, target) : null;
  const waveUnitStates = getWaveUnitStates(deps);
  const shouldLog = !!(
    details.coreDamageApplied
    || details.movementAdvanced
    || targetId
    || unit.combatState === waveUnitStates.COMBAT
    || Number(unit.posY) >= getGridHeight(deps) - 2
  );
  if (!shouldLog || !log || typeof log.info !== "function")
    return;

  log.info("[WaveUnitTrace] tick", {
    tick: game.tick,
    laneIndex: lane.laneIndex,
    unitId: unit.id,
    unitType: unit.type,
    routeType: unit.routeType || null,
    currentSegment: unit.currentSegment || null,
    segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
    state: unit.combatState || waveUnitStates.IDLE,
    movementMode: unit.movementMode || null,
    inCombat: unit.combatState === waveUnitStates.COMBAT,
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

function doAttack(game, lane, attacker, target, deps = {}) {
  const fireProjectile = requireDepFunction(deps, "fireProjectile");
  const getTowerStats = requireDepFunction(deps, "getTowerStats");
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const resolveUnitAllegianceKey = requireDepFunction(deps, "resolveUnitAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  const stats = getTowerStats(attacker.type, 1);
  const unitDef = resolveUnitDef(attacker.type);
  const damage = Number(attacker.baseDmg) || Number(unitDef && unitDef.dmg) || (stats ? stats.dmg : 5);
  const cooldownTicks = Number(attacker.atkCdTicks) || Number(unitDef && unitDef.atkCdTicks) || (stats ? stats.atkCdTicks : 30);
  const attackRange = getUnitAttackRange(attacker, deps);
  const rewardLaneIndex = Number.isInteger(attacker && attacker.ownerLaneIndex)
    ? attacker.ownerLaneIndex
    : Number.isInteger(attacker && attacker.ownerLane)
      ? attacker.ownerLane
      : (Number.isInteger(attacker && attacker.sourceLaneIndex) ? attacker.sourceLaneIndex : -1);
  const attackerAllegiance = resolveUnitAllegianceKey(game, lane, attacker);
  const targetAllegiance = resolveUnitAllegianceKey(game, lane, target);
  const canCreditKill = Number.isInteger(rewardLaneIndex)
    && rewardLaneIndex >= 0
    && areAllegiancesHostile(attackerAllegiance, targetAllegiance);

  if (attackRange > 2.0) {
    const behavior = unitDef
      ? (unitDef.projBehavior || (unitDef.isSplash ? "splash" : "single"))
      : stats
        ? (stats.projBehavior || "single")
        : "single";
    fireProjectile(
      game,
      lane,
      {
        id: attacker.id,
        kind: "unit",
        x: attacker.posX,
        y: attacker.posY,
        ownerLaneIndex: rewardLaneIndex,
        sourceLaneIndex: attacker.sourceLaneIndex,
        allegianceKey: attackerAllegiance,
      },
      target.id,
      {
        dmg: damage,
        damageType: unitDef ? (unitDef.damageType || "NORMAL") : (stats ? stats.damageType : "NORMAL"),
        behavior,
        behaviorParams: unitDef
          ? (unitDef.projBehaviorParams || {})
          : (stats ? (stats.projBehaviorParams || {}) : {}),
        travelTicks: unitDef
          ? (unitDef.projectileTicks || 8)
          : (stats ? (stats.projectileTicks || 8) : 8),
        isSplash: unitDef ? !!unitDef.isSplash : behavior === "splash",
        projectileType: attacker.type,
        abilities: [],
      }
    );
  } else {
    target.hp = Math.max(0, target.hp - damage);
    if (canCreditKill) {
      target.lastHostileKillCredit = {
        rewardLaneIndex,
        sourceKind: "unit",
        sourceId: attacker.id,
        sourceAllegianceKey: attackerAllegiance || null,
        tick: Number.isInteger(game && game.tick) ? game.tick : 0,
      };
    }
  }

  attacker.attackPulse = (attacker.attackPulse || 0) + 1;
  attacker.atkCd = cooldownTicks;
}

module.exports = {
  createCombatTargetingContext,
  moveTowardContact2D,
  updateSimpleContactApproachMemory,
  moveTowardSimpleContact2D,
  getUnitAttackRange,
  getUnitContactRadius,
  getUnitStopDistance,
  clearUnitCombatTarget,
  clearUnitSupportTarget,
  assignUnitCombatTarget,
  assignUnitSupportTarget,
  isLaneControlledUnitInRegroupWindow,
  isUnitCombatTargetStillValid,
  findFriendlyHealTarget,
  getRouteUnitTargetPressureCount,
  getRouteUnitTargetPreferenceScore,
  shouldSwitchCombatTarget,
  getLaneControlledCombatLeashRadius,
  clampPointToRadius,
  isPointWithinRadius,
  shouldAnchorClampLaneControlledCombat,
  resolveWaveCombatTarget,
  resolveContactFrame,
  shouldUseLaneControlledSurroundSlots,
  getContactSlotPoint,
  getLaneControlledCombatPocketPoint,
  getCombatSlotArrivalTolerance,
  isUnitInCombatContact,
  getUnitEngagementRange,
  getWaveUnitTargetDistance,
  getStructureTargetAcquisitionRange,
  findHostileRouteUnitTarget,
  getWaveUnitPreferredTarget,
  isRouteUnitTargetBlockedByStructure,
  hasImmediateFollowThroughCombatTarget,
  getLaneBlockingStructureTargets,
  findBlockingStructureTarget,
  markTownCoreBreach,
  attackFortressPad,
  clampUnitToAnchor,
  clampLaneControlledUnitToCombatLeash,
  getPairSpacing,
  tryResolveSettledAnchorPairSpacing,
  isLaneControlledUnitSettledAtAnchor,
  getUnitAnchorLateralAxis,
  clampLaneControlledUnitToAnchorDrift,
  getPairLateralAxis,
  shouldPreferLateralSpacing,
  resolveExactOverlapAxis,
  getSharedTargetTangentialAxis,
  shouldApplySimpleLaneHostileCollision,
  applySeparation2D,
  traceWaveUnitTick,
  doAttack,
};
