"use strict";

const DEFAULT_LANE_COMMAND_STATES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
});

const DEFAULT_UNIT_COMBAT_ROLES = Object.freeze({
  SHIELD: "shield",
  SWORD: "sword",
  SPEAR: "spear",
});

const DEFAULT_FORTRESS_BUILDING_DEFS = Object.freeze({});
const DEFAULT_BARRACKS_SITE_DEFS = Object.freeze([]);

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
  const captureDistance = stopDistance + Math.max(0.55, getUnitContactRadius(unit && unit.type, deps) * 0.65);
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

function getUnitAttackRange(typeKey, deps = {}) {
  const getTowerStats = requireDepFunction(deps, "getTowerStats");
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const stats = getTowerStats(typeKey, 1);
  if (stats && stats.range)
    return stats.range;

  const unitDef = resolveUnitDef(typeKey);
  if (unitDef && unitDef.combatRange)
    return unitDef.combatRange;

  return 1.5;
}

function getUnitContactRadius(typeKey, deps = {}) {
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const isFortArchetypeKey = requireDepFunction(deps, "isFortArchetypeKey");
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

function getUnitStopDistance(attackerType, targetType, deps = {}) {
  const resolveUnitCombatRole = requireDepFunction(deps, "resolveUnitCombatRole");
  const attackRange = getUnitAttackRange(attackerType, deps);
  const base = attackRange + (attackRange > 2.0 ? 0.15 : 0.05);
  if (attackRange > 2.0)
    return base;

  const attackerRadius = getUnitContactRadius(attackerType, deps);
  const targetRadius = getUnitContactRadius(targetType, deps);
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

function getUnitEngagementRange(typeKey, deps = {}) {
  return Math.max(
    getUnitAttackRange(typeKey, deps) + getEngagementRangePadding(deps),
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
    ? Math.max(getLaneControlledCombatLeashRadius(unit, deps), getUnitAttackRange(unit.type, deps) + 0.5)
    : Math.max(getUnitEngagementRange(unit.type, deps), getUnitAttackRange(unit.type, deps) + 0.5);
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

function getRouteUnitTargetPressureCount(game, seeker, targetEntity, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const resolveRouteUnitFactionKey = requireDepFunction(deps, "resolveRouteUnitFactionKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
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

function shouldUseLaneControlledSurroundSlots(attacker, target, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  if (!isLaneControlledUnit(attacker))
    return false;
  if (!target || (target.kind && target.kind !== "unit"))
    return false;

  return getUnitAttackRange(attacker.type, deps) <= 2.0;
}

function getRouteUnitTargetPreferenceScore(game, lane, unit, target, requireAttackRange = false, deps = {}) {
  const liveTarget = target && target.entity
    ? target.entity
    : target;
  const centerDistance = getResolvedCombatTargetDistance(unit, liveTarget);
  if (!Number.isFinite(centerDistance))
    return Infinity;

  let approachDistance = centerDistance;
  if (lane
      && requireDepFunction(deps, "isLaneControlledUnit")(unit)
      && liveTarget
      && shouldUseLaneControlledSurroundSlots(unit, liveTarget, deps)) {
    const stopDistance = getUnitStopDistance(unit.type, liveTarget.type, deps);
    const slotPoint = getLaneControlledCombatPocketPoint(
      lane,
      unit,
      liveTarget,
      stopDistance,
      { appendAttackerIfMissing: true },
      deps
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

  const pressure = getRouteUnitTargetPressureCount(game, unit, liveTarget, deps);
  const penalty = Number(deps.ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY);
  return approachDistance + (pressure * (Number.isFinite(penalty) ? penalty : 0.4));
}

function shouldSwitchCombatTarget(game, lane, unit, currentTarget, nextTarget, deps = {}) {
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
  const currentTargetInContact = !!(lane && currentTargetEntity && isUnitInCombatContact(lane, unit, currentTargetEntity, deps));
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
    : getRouteUnitTargetPreferenceScore(game, lane, unit, currentTarget, false, deps);
  const nextScore = Number.isFinite(Number(nextTarget && nextTarget.preferenceScore))
    ? Number(nextTarget.preferenceScore)
    : getRouteUnitTargetPreferenceScore(game, lane, unit, nextTarget, false, deps);
  const laneCommandStates = getLaneCommandStates(deps);
  const switchMargin = normalizeLaneCommandState(unit.commandState) === laneCommandStates.DEFEND
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

  const unit = lane.units.find((candidate) => candidate && candidate.id === combatTarget.unitId && candidate.hp > 0);
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

function getContactSlotPoint(lane, attacker, target, stopDistance, options = null, deps = {}) {
  const resolveLaneControlledUnitSortKey = requireDepFunction(deps, "resolveLaneControlledUnitSortKey");
  const appendAttackerIfMissing = !!(options && options.appendAttackerIfMissing);
  const attackers = lane.units
    .filter((unit) => unit
      && unit.hp > 0
      && !unit.isDefender
      && unit.combatTarget
      && unit.combatTarget.unitId === target.id)
    .sort((left, right) => resolveLaneControlledUnitSortKey(left).localeCompare(resolveLaneControlledUnitSortKey(right)));

  const attackerIndex = attackers.findIndex((unit) => unit.id === attacker.id);
  const slotIndex = Math.max(0, attackerIndex >= 0 ? attackerIndex : (appendAttackerIfMissing ? attackers.length : 0));
  const effectiveAttackerCount = appendAttackerIfMissing && attackerIndex < 0
    ? attackers.length + 1
    : attackers.length;
  const radius = Math.max(0.75, getUnitContactRadius(attacker.type, deps) + getUnitContactRadius(target.type, deps));
  const centroid = attackers.reduce((sum, unit) => ({
    x: sum.x + (Number(unit.posX) || 0),
    y: sum.y + (Number(unit.posY) || 0),
  }), { x: 0, y: 0 });
  const centroidX = attackers.length > 0 ? centroid.x / attackers.length : Number(attacker.posX) || Number(target.posX) || 0;
  const centroidY = attackers.length > 0 ? centroid.y / attackers.length : Number(attacker.posY) || Number(target.posY) || 0;
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

function getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance, options = null, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const shouldLaneControlledUnitFreeRoamInCombat = requireDepFunction(deps, "shouldLaneControlledUnitFreeRoamInCombat");
  const getLaneControlledSharedCombatAnchor = requireDepFunction(deps, "getLaneControlledSharedCombatAnchor");
  const desiredPoint = getContactSlotPoint(lane, attacker, target, stopDistance, options, deps);
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
  const attackerRadius = getUnitContactRadius(attacker && attacker.type, deps);
  const targetRadius = getUnitContactRadius(target && target.type, deps);
  const combinedRadius = attackerRadius + targetRadius;
  return Math.max(0.35, Math.min(0.75, combinedRadius * 0.35));
}

function isUnitInCombatContact(lane, attacker, target, deps = {}) {
  if (!attacker || !target)
    return false;

  const distance = getWaveUnitTargetDistance(attacker, target);
  const stopDistance = getUnitStopDistance(attacker.type, target.type, deps);
  if (!Number.isFinite(distance) || !Number.isFinite(stopDistance) || distance > stopDistance + getContactSlotTolerance(deps))
    return false;

  if (!lane || !shouldUseLaneControlledSurroundSlots(attacker, target, deps))
    return true;

  const slotPoint = getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance, null, deps);
  const slotDistance = Math.hypot(
    Number(attacker.posX) - Number(slotPoint.x),
    Number(attacker.posY) - Number(slotPoint.y)
  );
  return Number.isFinite(slotDistance) && slotDistance <= getCombatSlotArrivalTolerance(attacker, target, deps);
}

function getStructureTargetAcquisitionRange(unit, target, deps = {}) {
  if (!unit || !target)
    return 0;
  return getUnitStopDistance(unit.type, target.type, deps) + getStructureTargetVicinityPadding(deps);
}

function findHostileRouteUnitTarget(game, lane, unit, requireAttackRange = false, options = null, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const getLaneCommandStateForUnit = requireDepFunction(deps, "getLaneCommandStateForUnit");
  const getLaneCommandAnchorStateForUnit = requireDepFunction(deps, "getLaneCommandAnchorStateForUnit");
  const canEngageRouteUnitTarget = requireDepFunction(deps, "canEngageRouteUnitTarget");
  const isTargetInsideHomeFortressEmergencyZone = requireDepFunction(deps, "isTargetInsideHomeFortressEmergencyZone");
  if (!game || !lane || !unit)
    return null;

  const directEngagementOnly = !!(options && options.directEngagementOnly);
  const laneCommandStates = getLaneCommandStates(deps);
  const defendSeek = isLaneControlledUnit(unit)
    && getLaneCommandStateForUnit(game, unit) === laneCommandStates.DEFEND;
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
      const baseEngagementRange = getUnitEngagementRange(unit.type, deps);
      const rangeLimit = requireAttackRange
        ? getUnitStopDistance(unit.type, candidate.type, deps) + getContactSlotTolerance(deps)
        : (directEngagementOnly
          ? baseEngagementRange
          : (emergencyInteriorTarget
            ? Math.max(baseEngagementRange, getFortressInteriorAssaultRadius(deps))
            : (defendSeek
              ? Math.max(
                baseEngagementRange,
                (Number(defendAnchorState && defendAnchorState.engagementRadius) || getLaneCommandDefenseRadius(deps)) + getRouteSlotRowSpacing(deps)
              )
              : baseEngagementRange))) + getContactSlotTolerance(deps);
      if (dist > rangeLimit)
        continue;

      const preferenceScore = getRouteUnitTargetPreferenceScore(game, candidateLane, unit, candidate, requireAttackRange, deps);
      if (requireAttackRange
          && isLaneControlledUnit(unit)
          && shouldUseLaneControlledSurroundSlots(unit, candidate, deps)
          && preferenceScore > getCombatSlotArrivalTolerance(unit, candidate, deps) + getContactSlotTolerance(deps)) {
        continue;
      }

      if (dist < bestDist || (Math.abs(dist - bestDist) <= 0.0001 && preferenceScore < bestPreferenceScore)) {
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

function getWaveUnitPreferredTarget(game, lane, unit, deps = {}) {
  const routeUnitInAttackRange = findHostileRouteUnitTarget(game, lane, unit, true, null, deps);
  if (routeUnitInAttackRange) {
    return {
      kind: "unit",
      entity: routeUnitInAttackRange.entity,
      laneIndex: routeUnitInAttackRange.laneIndex,
      reason: "route_unit_attack_range",
      preferenceScore: routeUnitInAttackRange.preferenceScore,
    };
  }

  const routeUnitInEngageRange = findHostileRouteUnitTarget(game, lane, unit, false, null, deps);
  if (routeUnitInEngageRange) {
    return {
      kind: "unit",
      entity: routeUnitInEngageRange.entity,
      laneIndex: routeUnitInEngageRange.laneIndex,
      reason: "route_unit_engage_range",
      preferenceScore: routeUnitInEngageRange.preferenceScore,
    };
  }

  return null;
}

function hasImmediateFollowThroughCombatTarget(game, lane, unit, deps = {}) {
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  if (!game || !lane || !unit || !isLaneControlledUnit(unit))
    return false;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return false;

  const routeUnitInAttackRange = findHostileRouteUnitTarget(game, lane, unit, true, null, deps);
  if (routeUnitInAttackRange)
    return true;

  return !!findHostileRouteUnitTarget(game, lane, unit, false, { directEngagementOnly: true }, deps);
}

function getLaneBlockingStructureTargets(lane, unit = null, deps = {}) {
  const getFortressPadCombatTarget = requireDepFunction(deps, "getFortressPadCombatTarget");
  const getBarracksSiteCombatTarget = requireDepFunction(deps, "getBarracksSiteCombatTarget");
  const targets = [];
  if (!lane)
    return targets;

  if (Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
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

module.exports = {
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
  hasImmediateFollowThroughCombatTarget,
  getLaneBlockingStructureTargets,
  findBlockingStructureTarget,
};
