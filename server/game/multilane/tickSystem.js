"use strict";

const DEFAULT_WAVE_UNIT_STATES = Object.freeze({
  IDLE: "IDLE",
  MOVING: "MOVING",
  COMBAT: "COMBAT",
  DEAD: "DEAD",
});

const DEFAULT_UNIT_MOVEMENT_MODES = Object.freeze({
  LANE_TRAVEL: "LaneTravel",
  ANCHOR_JOIN: "AnchorJoin",
  COMBAT_ENGAGE: "CombatEngage",
});

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`tickSystem requires deps.${name}`);
  return fn;
}

function getWaveUnitStates(deps = {}) {
  return deps.WAVE_UNIT_STATES || DEFAULT_WAVE_UNIT_STATES;
}

function getUnitMovementModes(deps = {}) {
  return deps.UNIT_MOVEMENT_MODES || DEFAULT_UNIT_MOVEMENT_MODES;
}

function getGridWidth(deps = {}) {
  const value = Math.floor(Number(deps.GRID_W));
  return Number.isInteger(value) && value > 0 ? value : 11;
}

function getGridHeight(deps = {}) {
  const value = Math.floor(Number(deps.GRID_H));
  return Number.isInteger(value) && value > 0 ? value : 28;
}

function getTickHz(deps = {}) {
  const value = Math.floor(Number(deps.TICK_HZ));
  return Number.isInteger(value) && value > 0 ? value : 20;
}

function getMinUnitSpacing(deps = {}) {
  const value = Number(deps.MIN_UNIT_SPACING);
  return Number.isFinite(value) && value > 0 ? value : 0.8;
}

function getContactSlotTolerance(deps = {}) {
  const value = Number(deps.CONTACT_SLOT_TOLERANCE);
  return Number.isFinite(value) && value >= 0 ? value : 0.1;
}

function getWaveRouteCombatRecoveryTicks(deps = {}) {
  const value = Math.floor(Number(deps.WAVE_ROUTE_COMBAT_RECOVERY_TICKS));
  return Number.isInteger(value) && value > 0 ? value : 2;
}

function getEnableWaveUnitTrace(deps = {}) {
  return deps.ENABLE_WAVE_UNIT_TRACE === true;
}

function materializeLaneSpawnQueue(game, lane, deps = {}) {
  const log = deps && deps.log;
  const initializeMovingUnitRouteState = requireDepFunction(deps, "initializeMovingUnitRouteState");
  const resolveSpawnSourceTypeFromUnit = requireDepFunction(deps, "resolveSpawnSourceTypeFromUnit");
  const applyCanonicalUnitMirrors = requireDepFunction(deps, "applyCanonicalUnitMirrors");
  const resolveAbilityHook = requireDepFunction(deps, "resolveAbilityHook");
  const gridWidth = getGridWidth(deps);

  while (Array.isArray(lane.spawnQueue) && lane.spawnQueue.length > 0) {
    const unit = lane.spawnQueue.shift();
    const idx = unit.spawnIndex ?? 0;
    const spawnLogicalPos = unit.spawnLogicalPos || {
      x: idx % gridWidth,
      y: Math.floor(idx / gridWidth),
    };
    const routeInit = initializeMovingUnitRouteState(game, lane, unit, spawnLogicalPos);
    if (!routeInit || !routeInit.ok) {
      if (log && typeof log.error === "function") {
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
      }
      continue;
    }

    unit.ownerLane = Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0
      ? unit.sourceLaneIndex
      : -1;
    unit.ownerLaneIndex = unit.ownerLane;
    applyCanonicalUnitMirrors(game, lane, unit);
    if (log && typeof log.info === "function") {
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
    }
    if (unit.sourceBarracksKey || unit.sourceBarracksId) {
      console.log(
        `[BarracksTrace][ServerSpawn] tick=${game.tick} targetLane=${lane.laneIndex} `
        + `unit='${unit.id}' type='${unit.type}' barracksId='${unit.sourceBarracksKey || unit.sourceBarracksId}' `
        + `sourceLane=${unit.sourceLaneIndex} sourceTeam='${unit.sourceTeam || ""}'`
      );
    }
    lane.units.push(unit);
    if (unit.abilities && unit.abilities.length > 0)
      resolveAbilityHook(game, lane, unit, "onSpawn", {});
  }

  for (const unit of lane.units || [])
    applyCanonicalUnitMirrors(game, lane, unit);
}

function decrementLaneCooldowns(lane) {
  for (const unit of lane.units || []) {
    if (unit && unit.atkCd > 0)
      unit.atkCd -= 1;
  }
}

function logMissingRouteContract(game, lane, unit, deps = {}) {
  const log = deps && deps.log;
  const resolveSpawnSourceTypeFromUnit = requireDepFunction(deps, "resolveSpawnSourceTypeFromUnit");
  if (unit._missingRouteLogged)
    return;
  unit._missingRouteLogged = true;
  if (log && typeof log.error === "function") {
    log.error("[WaveUnitTrace] missing_route_contract", {
      tick: game.tick,
      laneIndex: lane.laneIndex,
      unitId: unit.id,
      unitType: unit.type,
      spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
      sourceLaneIndex: Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1,
      sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
    });
  }
}

function logWaveRouteProgress(game, lane, unit, deps = {}) {
  const log = deps && deps.log;
  const resolveSpawnSourceTypeFromUnit = requireDepFunction(deps, "resolveSpawnSourceTypeFromUnit");
  const buildRoutePathId = requireDepFunction(deps, "buildRoutePathId");
  const resolveUnitNextWaypoint = requireDepFunction(deps, "resolveUnitNextWaypoint");
  if (!getEnableWaveUnitTrace(deps) || !log || typeof log.info !== "function")
    return;
  log.info("[WaveUnitTrace] movement_progress", {
    tick: game.tick,
    laneIndex: lane.laneIndex,
    unitId: unit.id,
    unitType: unit.type,
    spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
    pathId: buildRoutePathId(unit.routeSegments),
    currentWaypointIndex: Math.max(0, Math.floor(Number(unit.routeSegmentIndex) || 0)),
    nextWaypoint: resolveUnitNextWaypoint(unit),
    movementState: unit.routeState,
    segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
    pathIdx: Number.isFinite(unit.pathIdx) ? Number(unit.pathIdx.toFixed(3)) : null,
    routeWorldX: Number.isFinite(unit.routeWorldX) ? Number(unit.routeWorldX.toFixed(3)) : null,
    routeWorldY: Number.isFinite(unit.routeWorldY) ? Number(unit.routeWorldY.toFixed(3)) : null,
  });
}

function resolveUnitAttackStats(unit, deps = {}) {
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const unitDef = unit && unit.type
    ? resolveUnitDef(unit.type)
    : null;
  return {
    damage: Math.max(1, Math.floor(Number(unit && unit.baseDmg) || Number(unitDef && unitDef.dmg) || 1)),
    cooldownTicks: Math.max(1, Math.floor(Number(unit && unit.atkCdTicks) || Number(unitDef && unitDef.atkCdTicks) || 20)),
  };
}

function processLane(game, lane, deps = {}) {
  const log = deps && deps.log;
  const combatLog = deps && deps.combatLog;
  const waveUnitStates = getWaveUnitStates(deps);
  const unitMovementModes = getUnitMovementModes(deps);
  const minUnitSpacing = getMinUnitSpacing(deps);
  const contactSlotTolerance = getContactSlotTolerance(deps);
  const waveRouteCombatRecoveryTicks = getWaveRouteCombatRecoveryTicks(deps);
  const enableWaveUnitTrace = getEnableWaveUnitTrace(deps);
  const laneHasOccupyingForces = requireDepFunction(deps, "laneHasOccupyingForces");
  const resolveUnitSupportProfile = requireDepFunction(deps, "resolveUnitSupportProfile");
  const resolveStatuses = requireDepFunction(deps, "resolveStatuses");
  const traceWaveUnitTick = requireDepFunction(deps, "traceWaveUnitTick");
  const resolveWaveCombatTarget = requireDepFunction(deps, "resolveWaveCombatTarget");
  const isUnitCombatTargetStillValid = requireDepFunction(deps, "isUnitCombatTargetStillValid");
  const clearUnitCombatTarget = requireDepFunction(deps, "clearUnitCombatTarget");
  const hasImmediateFollowThroughCombatTarget = requireDepFunction(deps, "hasImmediateFollowThroughCombatTarget");
  const isLaneControlledUnitInRegroupWindow = requireDepFunction(deps, "isLaneControlledUnitInRegroupWindow");
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  const getWaveUnitPreferredTarget = requireDepFunction(deps, "getWaveUnitPreferredTarget");
  const shouldSwitchCombatTarget = requireDepFunction(deps, "shouldSwitchCombatTarget");
  const assignUnitCombatTarget = requireDepFunction(deps, "assignUnitCombatTarget");
  const findBlockingStructureTarget = requireDepFunction(deps, "findBlockingStructureTarget");
  const markTownCoreBreach = requireDepFunction(deps, "markTownCoreBreach");
  const findFriendlyHealTarget = requireDepFunction(deps, "findFriendlyHealTarget");
  const getUnitAttackRange = requireDepFunction(deps, "getUnitAttackRange");
  const dist2D = requireDepFunction(deps, "dist2D");
  const assignUnitSupportTarget = requireDepFunction(deps, "assignUnitSupportTarget");
  const shouldUseSimpleContactApproach = requireDepFunction(deps, "shouldUseSimpleContactApproach");
  const moveTowardContact2D = requireDepFunction(deps, "moveTowardContact2D");
  const moveTowardSimpleContact2D = requireDepFunction(deps, "moveTowardSimpleContact2D");
  const getLaneControlledCombatPocketPoint = requireDepFunction(deps, "getLaneControlledCombatPocketPoint");
  const moveTowardPoint2D = requireDepFunction(deps, "moveTowardPoint2D");
  const shouldLaneControlledUnitFreeRoamInCombat = requireDepFunction(deps, "shouldLaneControlledUnitFreeRoamInCombat");
  const clampLaneControlledUnitToCombatLeash = requireDepFunction(deps, "clampLaneControlledUnitToCombatLeash");
  const clearUnitSupportTarget = requireDepFunction(deps, "clearUnitSupportTarget");
  const getLaneByIndex = requireDepFunction(deps, "getLaneByIndex");
  const resolveRouteUnitLane = requireDepFunction(deps, "resolveRouteUnitLane");
  const getUnitStopDistance = requireDepFunction(deps, "getUnitStopDistance");
  const getWaveUnitTargetDistance = requireDepFunction(deps, "getWaveUnitTargetDistance");
  const isLaneControlledUnit = requireDepFunction(deps, "isLaneControlledUnit");
  const isUnitInCombatContact = requireDepFunction(deps, "isUnitInCombatContact");
  const attackFortressPad = requireDepFunction(deps, "attackFortressPad");
  const shouldLaneControlledUnitRouteMarch = requireDepFunction(deps, "shouldLaneControlledUnitRouteMarch");
  const syncUnitRouteStateToWorldPosition = requireDepFunction(deps, "syncUnitRouteStateToWorldPosition");
  const advanceLaneControlledUnitAlongRoute = requireDepFunction(deps, "advanceLaneControlledUnitAlongRoute");
  const moveLaneControlledUnitToAnchor = requireDepFunction(deps, "moveLaneControlledUnitToAnchor");
  const relaxUnitRouteOffsets = requireDepFunction(deps, "relaxUnitRouteOffsets");
  const advanceRouteState = requireDepFunction(deps, "advanceRouteState");
  const computeUnitRoutePathIndex = requireDepFunction(deps, "computeUnitRoutePathIndex");
  const setUnitRouteSnapshotState = requireDepFunction(deps, "setUnitRouteSnapshotState");
  const onMarketWorkerLapComplete = requireDepFunction(deps, "onMarketWorkerLapComplete");
  const applySeparation2D = requireDepFunction(deps, "applySeparation2D");
  const resolveProjectile = requireDepFunction(deps, "resolveProjectile");
  const resolveAbilityHook = requireDepFunction(deps, "resolveAbilityHook");
  const isScheduledWaveUnit = requireDepFunction(deps, "isScheduledWaveUnit");
  const applyCanonicalUnitMirrors = requireDepFunction(deps, "applyCanonicalUnitMirrors");

  const laneHasActiveOccupiers = laneHasOccupyingForces(lane);
  if (lane.eliminated && !laneHasActiveOccupiers)
    return;

  const laneActive = !lane.eliminated;

  materializeLaneSpawnQueue(game, lane, deps);
  decrementLaneCooldowns(lane);

  const dotDeadIds = new Set();

  for (const unit of lane.units || []) {
    if (unit.hp <= 0)
      continue;
    if (unit.isDefender)
      continue;

    const startedTickWithCombatTarget = !!(unit.combatTarget && unit.combatTarget.unitId);
    const startedTickInCombat = unit.combatState === waveUnitStates.COMBAT || unit.routeState === waveUnitStates.COMBAT;
    const priorMarketRouteProgress = unit.isMarketWorker ? computeUnitRoutePathIndex(unit) : null;
    const supportProfile = resolveUnitSupportProfile(unit);
    const healerMode = supportProfile.isHealer;
    unit.combatState = waveUnitStates.IDLE;
    unit.routeState = waveUnitStates.IDLE;
    unit.blockedByStructure = false;
    unit.blockedByStructureId = null;
    unit.blockedByStructureType = null;
    resolveStatuses(unit, game.tick);
    if (unit.hp <= 0) {
      unit.combatState = waveUnitStates.DEAD;
      unit.routeState = waveUnitStates.DEAD;
      dotDeadIds.add(unit.id);
      traceWaveUnitTick(game, lane, unit, null, {
        movementAdvanced: false,
        coreDamageApplied: false,
      });
      continue;
    }

    let preferredTarget = null;
    let resolvedCombatTarget = null;
    if (!healerMode) {
      resolvedCombatTarget = unit.combatTarget && unit.combatTarget.unitId
        ? resolveWaveCombatTarget(game, lane, unit.combatTarget)
        : null;
      if (unit.combatTarget && unit.combatTarget.unitId) {
        const targetStillValid = !!resolvedCombatTarget && isUnitCombatTargetStillValid(game, lane, unit, resolvedCombatTarget);
        if (!targetStillValid) {
          clearUnitCombatTarget(unit, game.tick, {
            suppressRegroup: !resolvedCombatTarget && hasImmediateFollowThroughCombatTarget(game, lane, unit),
          });
          resolvedCombatTarget = null;
        }
      }

      const canReacquireUnitTarget = !isLaneControlledUnitInRegroupWindow(unit, game.tick);
      let directPreferredTarget = null;
      if (canReacquireUnitTarget && canLaneControlledUnitSeekCombat(game, unit))
        directPreferredTarget = getWaveUnitPreferredTarget(game, lane, unit);

      if (directPreferredTarget)
        preferredTarget = directPreferredTarget;

      if (preferredTarget && preferredTarget.kind === "unit") {
        const shouldAssignPreferredTarget = !resolvedCombatTarget
          || shouldSwitchCombatTarget(game, lane, unit, resolvedCombatTarget, preferredTarget);
        if (shouldAssignPreferredTarget) {
          assignUnitCombatTarget(unit, {
            unitId: preferredTarget.entity.id,
            kind: "unit",
            laneIndex: preferredTarget.laneIndex,
          }, game.tick);
          resolvedCombatTarget = resolveWaveCombatTarget(game, lane, unit.combatTarget);
        }
      }

      if (!unit.combatTarget) {
        const blockingTarget = findBlockingStructureTarget(game, lane, unit);
        if (blockingTarget) {
          if (blockingTarget.buildingType === "town_core")
            markTownCoreBreach(game, lane, unit, blockingTarget);
          unit.blockedByStructure = true;
          unit.blockedByStructureId = blockingTarget.unitId;
          unit.blockedByStructureType = blockingTarget.buildingType || blockingTarget.type || null;
          assignUnitCombatTarget(unit, {
            unitId: blockingTarget.unitId,
            kind: "fortress_pad",
            padId: blockingTarget.padId,
            laneIndex: Number.isInteger(blockingTarget.laneIndex) ? blockingTarget.laneIndex : lane.laneIndex,
          }, game.tick);
          resolvedCombatTarget = resolveWaveCombatTarget(game, lane, unit.combatTarget);
        }
      }
    }

    let attackedTarget = false;
    let movementAdvanced = false;
    let coreDamageApplied = false;
    if (healerMode) {
      const healTarget = findFriendlyHealTarget(game, lane, unit, supportProfile);
      if (healTarget) {
        const healRange = getUnitAttackRange(unit.type);
        const stopDist = healRange + 0.15;
        const dist = dist2D(unit, healTarget);
        assignUnitSupportTarget(unit, {
          unitId: healTarget.id,
          kind: "unit",
          laneIndex: lane.laneIndex,
        });
        unit.combatState = waveUnitStates.COMBAT;
        unit.routeState = waveUnitStates.COMBAT;
        unit.movementMode = unitMovementModes.COMBAT_ENGAGE;
        if (dist <= stopDist + contactSlotTolerance) {
          if (unit.atkCd <= 0) {
            const healAmount = Math.max(1, Math.round(supportProfile.healAmount || 1));
            const maxHp = Math.max(1, Number(healTarget.maxHp) || Number(healTarget.hp) || 1);
            healTarget.hp = Math.min(maxHp, (Number(healTarget.hp) || 0) + healAmount);
            unit.attackPulse = (unit.attackPulse || 0) + 1;
            unit.atkCd = Math.max(1, Math.floor(Number(unit.atkCdTicks) || 20));
            attackedTarget = true;
          } else {
            attackedTarget = true;
          }
        } else {
          if (shouldUseSimpleContactApproach(unit, { kind: "unit" })) {
            moveTowardContact2D(unit, healTarget, unit.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
          } else {
            const slotPoint = getLaneControlledCombatPocketPoint(lane, unit, healTarget, stopDist);
            moveTowardPoint2D(unit, slotPoint.x, slotPoint.y, unit.baseSpeed || 0.18, -64, 64, -64, 64);
          }
          if (!shouldLaneControlledUnitFreeRoamInCombat(unit))
            clampLaneControlledUnitToCombatLeash(unit, -64, 64, -64, 64, healTarget);
          unit.movementMode = unitMovementModes.COMBAT_ENGAGE;
          movementAdvanced = true;
        }
      } else {
        clearUnitSupportTarget(unit);
      }
    } else if (unit.combatTarget && unit.combatTarget.unitId) {
      const target = resolveWaveCombatTarget(game, lane, unit.combatTarget);
      if (!target) {
        clearUnitCombatTarget(unit, game.tick, {
          suppressRegroup: hasImmediateFollowThroughCombatTarget(game, lane, unit),
        });
      } else if (target.kind === "unit") {
        unit.combatState = waveUnitStates.COMBAT;
        unit.routeState = waveUnitStates.COMBAT;
        unit.movementMode = unitMovementModes.COMBAT_ENGAGE;
        const targetUnit = target.entity;
        unit.combatTargetWorldX = Number(targetUnit && targetUnit.posX);
        unit.combatTargetWorldY = Number(targetUnit && targetUnit.posY);
        const targetLane = Number.isInteger(target.laneIndex)
          ? getLaneByIndex(game, target.laneIndex)
          : resolveRouteUnitLane(game, targetUnit, lane);
        const stopDist = getUnitStopDistance(unit.type, targetUnit.type);
        const directContactDistance = getWaveUnitTargetDistance(unit, targetUnit);
        const allowImmediateLaneContact = !!(
          isLaneControlledUnit(unit)
          && !startedTickWithCombatTarget
          && Number.isFinite(directContactDistance)
          && directContactDistance <= stopDist + contactSlotTolerance
        );
        const inCombatContact = allowImmediateLaneContact || isUnitInCombatContact(lane, unit, targetUnit);
        if (inCombatContact) {
          if (unit.atkCd <= 0) {
            const attackStats = resolveUnitAttackStats(unit, deps);
            targetUnit.hp = Math.max(0, targetUnit.hp - attackStats.damage);
            unit.attackPulse = (unit.attackPulse || 0) + 1;
            if (targetUnit.hp <= 0) {
              combatLog.logEvent(game, "route_unit_killed", {
                unitId: targetUnit.id,
                unitType: targetUnit.type,
                lane: targetLane ? targetLane.laneIndex : null,
                killedBy: unit.id,
                killedByType: unit.type,
                killedByLane: lane.laneIndex,
              });
              clearUnitCombatTarget(unit, game.tick, {
                suppressRegroup: hasImmediateFollowThroughCombatTarget(game, lane, unit),
              });
            }
            unit.atkCd = attackStats.cooldownTicks;
            attackedTarget = true;
          } else {
            attackedTarget = true;
          }
        } else {
          if (shouldUseSimpleContactApproach(unit, target)) {
            moveTowardSimpleContact2D(unit, targetUnit, unit.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
          } else {
            const slotPoint = getLaneControlledCombatPocketPoint(lane, unit, targetUnit, stopDist);
            moveTowardPoint2D(unit, slotPoint.x, slotPoint.y, unit.baseSpeed || 0.18, -64, 64, -64, 64);
          }
          if (!shouldLaneControlledUnitFreeRoamInCombat(unit))
            clampLaneControlledUnitToCombatLeash(unit, -64, 64, -64, 64, targetUnit);
          unit.movementMode = unitMovementModes.COMBAT_ENGAGE;
          movementAdvanced = true;
        }
      } else if (target.kind === "fortress_pad") {
        unit.combatState = waveUnitStates.COMBAT;
        unit.routeState = waveUnitStates.COMBAT;
        unit.movementMode = unitMovementModes.COMBAT_ENGAGE;
        const dist = Math.sqrt(
          Math.pow(target.posX - unit.posX, 2)
          + Math.pow(target.posY - unit.posY, 2)
        );
        const stopDist = getUnitStopDistance(unit.type, target.type);
        if (dist <= stopDist + contactSlotTolerance) {
          if (unit.atkCd <= 0) {
            const result = attackFortressPad(game, lane, unit, target);
            if (result.destroyed)
              clearUnitCombatTarget(unit, game.tick, { regroupUntilTick: game.tick });
            coreDamageApplied = result.damageApplied > 0;
            attackedTarget = true;
          } else {
            attackedTarget = true;
          }
        } else {
          const slotPoint = getLaneControlledCombatPocketPoint(lane, unit, target, stopDist);
          moveTowardPoint2D(unit, slotPoint.x, slotPoint.y, unit.baseSpeed || 0.18, -64, 64, -64, 64);
          if (!shouldLaneControlledUnitFreeRoamInCombat(unit))
            clampLaneControlledUnitToCombatLeash(unit, -64, 64, -64, 64, target);
          unit.movementMode = unitMovementModes.COMBAT_ENGAGE;
          movementAdvanced = true;
        }
      }
    }

    if (!unit.combatTarget && !attackedTarget) {
      unit.combatState = waveUnitStates.MOVING;
      unit.routeState = waveUnitStates.MOVING;
      if (isLaneControlledUnit(unit)) {
        const routeMarching = shouldLaneControlledUnitRouteMarch(unit);
        if (startedTickWithCombatTarget || startedTickInCombat || unit.movementMode === unitMovementModes.COMBAT_ENGAGE)
          syncUnitRouteStateToWorldPosition(unit);
        if (routeMarching)
          movementAdvanced = advanceLaneControlledUnitAlongRoute(unit);
        else
          movementAdvanced = moveLaneControlledUnitToAnchor(unit);
        if (unit.isMarketWorker && routeMarching) {
          const nextMarketRouteProgress = computeUnitRoutePathIndex(unit);
          if (Number.isFinite(priorMarketRouteProgress)
              && Number.isFinite(nextMarketRouteProgress)
              && nextMarketRouteProgress < priorMarketRouteProgress - 0.0001) {
            onMarketWorkerLapComplete(game, lane, unit);
          }
        }
        if (!movementAdvanced) {
          unit.movementMode = routeMarching
            ? unitMovementModes.LANE_TRAVEL
            : unitMovementModes.ANCHOR_JOIN;
        }
      } else {
        unit.movementMode = unitMovementModes.LANE_TRAVEL;
        if (!Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0) {
          logMissingRouteContract(game, lane, unit, deps);
        } else {
          const routeSpeed = unit.baseSpeed || 0.18;
          if (startedTickWithCombatTarget || startedTickInCombat)
            syncUnitRouteStateToWorldPosition(unit);
          if (startedTickWithCombatTarget || startedTickInCombat) {
            unit.routeRecoveringFromCombatTicks = Math.max(
              Number(unit.routeRecoveringFromCombatTicks) || 0,
              waveRouteCombatRecoveryTicks
            );
          }
          if ((Number(unit.routeRecoveringFromCombatTicks) || 0) > 0) {
            relaxUnitRouteOffsets(unit, routeSpeed);
            unit.routeRecoveringFromCombatTicks = Math.max(0, (Number(unit.routeRecoveringFromCombatTicks) || 0) - 1);
          }
          advanceRouteState(unit, routeSpeed);
          setUnitRouteSnapshotState(unit);
          if (unit.isMarketWorker) {
            const nextMarketRouteProgress = computeUnitRoutePathIndex(unit);
            if (Number.isFinite(priorMarketRouteProgress)
                && Number.isFinite(nextMarketRouteProgress)
                && nextMarketRouteProgress < priorMarketRouteProgress - 0.0001) {
              onMarketWorkerLapComplete(game, lane, unit);
            }
            unit.canEngage = false;
          }
          unit._missingRouteLogged = false;
          logWaveRouteProgress(game, lane, unit, deps);
        }
        movementAdvanced = true;
      }
    }

    const traceTarget = unit.combatTarget ? resolveWaveCombatTarget(game, lane, unit.combatTarget) : null;
    unit.state = unit.combatState || unit.routeState || unit.state || waveUnitStates.IDLE;
    unit.currentTargetId = unit.combatTargetId || (unit.combatTarget && unit.combatTarget.unitId) || null;
    traceWaveUnitTick(game, lane, unit, traceTarget, {
      movementAdvanced,
      coreDamageApplied,
      preferredTargetReason: preferredTarget ? preferredTarget.reason : null,
    });
  }

  const laneCollisionUnits = (lane.units || []).filter((unit) =>
    unit
    && unit.hp > 0
    && !unit.isDefender
    && (
      isLaneControlledUnit(unit)
      || unit.combatTarget
      || unit.movementMode === unitMovementModes.COMBAT_ENGAGE
      || unit.combatState === waveUnitStates.COMBAT
      || unit.routeState === waveUnitStates.COMBAT
    ));
  if (laneCollisionUnits.length > 1)
    applySeparation2D(game, lane, laneCollisionUnits, minUnitSpacing, -64, 64, -64, 64);

  for (const unit of lane.units || [])
    applyCanonicalUnitMirrors(game, lane, unit);

  if (laneActive && enableWaveUnitTrace && game.tick % 20 === 0 && (lane.units || []).length > 0) {
    const waveUnits = lane.units.filter((unit) => !unit.isDefender && unit.hp > 0);
    if (waveUnits.length > 0) {
      const maxIdx = Math.max(...waveUnits.map((unit) => unit.pathIdx));
      const minIdx = Math.min(...waveUnits.map((unit) => unit.pathIdx));
      console.log(
        `[DEBUG lane${lane.laneIndex} tick${game.tick}] wave units=${waveUnits.length} `
        + `pathIdx min=${minIdx.toFixed(2)} max=${maxIdx.toFixed(2)} GRID_H=${getGridHeight(deps)}`
      );
    }
  }

  const killedById = new Set();
  const stillFlying = [];
  for (const projectile of lane.projectiles || []) {
    projectile.ticksRemaining -= 1;
    if (projectile.ticksRemaining > 0) {
      stillFlying.push(projectile);
      continue;
    }
    const { dead, hit } = resolveProjectile(game, lane, projectile);
    for (const id of dead)
      killedById.add(id);
    if (projectile.abilities && projectile.abilities.length > 0 && hit.length > 0) {
      const attacker = { abilities: projectile.abilities };
      for (const hitId of hit) {
        if (killedById.has(hitId))
          continue;
        const hitUnit = (lane.units || []).find((unit) => unit.id === hitId && unit.hp > 0);
        if (hitUnit)
          resolveAbilityHook(game, lane, attacker, "onHit", { target: hitUnit });
      }
    }
  }
  lane.projectiles = stillFlying;

  for (const id of dotDeadIds)
    killedById.add(id);

  for (const unit of lane.units || []) {
    if (unit.hp > 0)
      continue;
    if (unit.abilities && unit.abilities.length > 0)
      resolveAbilityHook(game, lane, unit, "onDeath", {});
    if (isScheduledWaveUnit(unit)) {
      lane.gold += unit.bounty || 1;
      combatLog.logEvent(game, "wave_unit_killed", {
        unitId: unit.id,
        unitType: unit.type,
        bounty: unit.bounty || 1,
        lane: lane.laneIndex,
      });
    }
  }
  lane.units = (lane.units || []).filter((unit) => unit.hp > 0);
}

function finalizeTick(game, deps = {}) {
  const log = deps && deps.log;
  const createRoundSnapshotLane = requireDepFunction(deps, "createRoundSnapshotLane");
  const buildTownCoreStateSummary = requireDepFunction(deps, "buildTownCoreStateSummary");
  const isCurrentWaveComplete = requireDepFunction(deps, "isCurrentWaveComplete");
  const startNextWaveNow = requireDepFunction(deps, "startNextWaveNow");

  if (game.phase !== "playing")
    return;

  const activeLanes = (game.lanes || []).filter((lane) => !lane.eliminated);
  if (activeLanes.length === 0) {
    if (!game.finalSnapshotCaptured) {
      const roundSnapshots = Array.isArray(game.roundSnapshots)
        ? game.roundSnapshots
        : (game.roundSnapshots = []);
      roundSnapshots.push({
        round: game.roundNumber,
        terminal: true,
        elapsedSeconds: Math.floor(game.tick / getTickHz(deps)),
        lanes: (game.lanes || []).map((lane) => createRoundSnapshotLane(game, lane)),
      });
      game.finalSnapshotCaptured = true;
    }
    game.finalGameOverReason = "all_town_cores_destroyed";
    game.finalGameOverDebug = {
      tick: game.tick,
      coreStates: buildTownCoreStateSummary(game),
    };
    if (log && typeof log.warn === "function") {
      log.warn("[TownCoreTrace] final game over triggered", {
        tick: game.tick,
        reason: game.finalGameOverReason,
        coreStates: game.finalGameOverDebug.coreStates,
      });
    }
    game.phase = "ended";
    game.matchState = "final_game_over";
    game.winner = Number.isInteger(game.officialWinnerLane) ? game.officialWinnerLane : null;
  } else if (game.hasSpawnedWave && isCurrentWaveComplete(game)) {
    startNextWaveNow(game);
  }
}

function mlTick(game, deps = {}) {
  const grantScheduledIncome = requireDepFunction(deps, "grantScheduledIncome");
  const runScheduledWaves = requireDepFunction(deps, "runScheduledWaves");
  const runScheduledBuildingConstruction = requireDepFunction(deps, "runScheduledBuildingConstruction");
  const runScheduledBarracksSends = requireDepFunction(deps, "runScheduledBarracksSends");
  const syncLaneCommandAssignments = requireDepFunction(deps, "syncLaneCommandAssignments");

  if (!game || game.phase !== "playing")
    return;

  game.tick += 1;
  if (!game.startedAt)
    game.startedAt = Date.now();
  grantScheduledIncome(game);
  runScheduledWaves(game);
  runScheduledBuildingConstruction(game);
  runScheduledBarracksSends(game);
  syncLaneCommandAssignments(game);
  game.roundState = "combat";
  game.roundStateTicks = Number.isInteger(game.lastWaveSpawnTick)
    ? Math.max(0, game.tick - game.lastWaveSpawnTick)
    : game.tick;

  for (const lane of game.lanes || [])
    processLane(game, lane, deps);

  finalizeTick(game, deps);
}

module.exports = {
  mlTick,
};
