"use strict";

const { performance } = require("node:perf_hooks");

const {
  isSpawnAuditLoggingEnabled,
  logSpawnAuditInfo,
} = require("./spawnAuditLogging");

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

function roundPerfDuration(value) {
  if (!Number.isFinite(value))
    return 0;
  return Math.round(value * 10) / 10;
}

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
  const normalizeLegacyDefenderUnit = typeof deps.normalizeLegacyDefenderUnit === "function"
    ? deps.normalizeLegacyDefenderUnit
    : null;
  const markLaneCommandAssignmentsDirty = typeof deps.markLaneCommandAssignmentsDirty === "function"
    ? deps.markLaneCommandAssignmentsDirty
    : null;
  const recordBalanceUnitSpawned = typeof deps.recordBalanceUnitSpawned === "function"
    ? deps.recordBalanceUnitSpawned
    : null;
  const gridWidth = getGridWidth(deps);
  let materializedLaneControlledUnit = false;

  while (Array.isArray(lane.spawnQueue) && lane.spawnQueue.length > 0) {
    const unit = lane.spawnQueue.shift();
    if (normalizeLegacyDefenderUnit && unit && unit.isDefender) {
      if (normalizeLegacyDefenderUnit(game, lane, unit))
        applyCanonicalUnitMirrors(game, lane, unit);
    }
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
    if (unit.ownerLane >= 0)
      materializedLaneControlledUnit = true;
    applyCanonicalUnitMirrors(game, lane, unit);
    logSpawnAuditInfo(deps, "[SpawnAudit][ServerLive] materialized", {
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
    if (isSpawnAuditLoggingEnabled(deps) && (unit.sourceBarracksKey || unit.sourceBarracksId)) {
      console.log(
        `[BarracksTrace][ServerSpawn] tick=${game.tick} targetLane=${lane.laneIndex} `
        + `unit='${unit.id}' type='${unit.type}' barracksId='${unit.sourceBarracksKey || unit.sourceBarracksId}' `
        + `sourceLane=${unit.sourceLaneIndex} sourceTeam='${unit.sourceTeam || ""}'`
      );
    }
    lane.units.push(unit);
    if (recordBalanceUnitSpawned)
      recordBalanceUnitSpawned(game, lane, unit);
    if (unit.abilities && unit.abilities.length > 0)
      resolveAbilityHook(game, lane, unit, "onSpawn", {});
  }

  for (const unit of lane.units || [])
    applyCanonicalUnitMirrors(game, lane, unit);
  if (materializedLaneControlledUnit && markLaneCommandAssignmentsDirty)
    markLaneCommandAssignmentsDirty(game);
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

function applyDirectUnitDamage(targetUnit, baseDamage) {
  if (!targetUnit || Number(targetUnit.hp) <= 0)
    return 0;
  const safeDamage = Math.max(0, Number(baseDamage) || 0);
  if (safeDamage <= 0)
    return 0;
  const directReductionPct = Math.max(
    0,
    Math.min(80, Number(targetUnit.directDamageReductionPctBonus) || 0)
  );
  const resolvedDamage = Math.max(0, Math.floor(safeDamage * (1 - (directReductionPct / 100))));
  if (resolvedDamage <= 0)
    return 0;
  targetUnit.hp = Math.max(0, Number(targetUnit.hp) - resolvedDamage);
  return resolvedDamage;
}

function getUnitDistance(a, b) {
  const ax = Number(a && a.posX);
  const ay = Number(a && a.posY);
  const bx = Number(b && b.posX);
  const by = Number(b && b.posY);
  if (!Number.isFinite(ax) || !Number.isFinite(ay) || !Number.isFinite(bx) || !Number.isFinite(by))
    return Infinity;
  return Math.hypot(ax - bx, ay - by);
}

function areUnitsHostile(game, lane, sourceUnit, targetUnit, deps = {}) {
  const resolveUnitAllegianceKey = requireDepFunction(deps, "resolveUnitAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  const sourceAllegiance = resolveUnitAllegianceKey(game, lane, sourceUnit);
  const targetAllegiance = resolveUnitAllegianceKey(game, lane, targetUnit);
  return areAllegiancesHostile(sourceAllegiance, targetAllegiance);
}

function areSourceAndUnitHostile(game, lane, source, targetUnit, deps = {}) {
  const resolveLaneAllegianceKey = requireDepFunction(deps, "resolveLaneAllegianceKey");
  const resolveUnitAllegianceKey = requireDepFunction(deps, "resolveUnitAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  if (!source || !targetUnit)
    return false;
  if (source.unit)
    return areUnitsHostile(game, lane, source.unit, targetUnit, deps);
  const sourceAllegiance = source.allegianceKey
    || (source.kind === "tower" ? resolveLaneAllegianceKey(lane) : null);
  if (!sourceAllegiance)
    return false;
  const targetAllegiance = resolveUnitAllegianceKey(game, lane, targetUnit);
  return areAllegiancesHostile(sourceAllegiance, targetAllegiance);
}

function resolveKillRewardLaneIndex(game, lane, source, deps = {}) {
  if (!source)
    return -1;
  if (Number.isInteger(source.rewardLaneIndex))
    return source.rewardLaneIndex;
  if (source.unit) {
    const resolveUnitOwnerLaneIndex = typeof deps.resolveUnitOwnerLaneIndex === "function"
      ? deps.resolveUnitOwnerLaneIndex
      : null;
    if (resolveUnitOwnerLaneIndex)
      return resolveUnitOwnerLaneIndex(game, lane, source.unit);
    if (Number.isInteger(source.unit.ownerLaneIndex))
      return source.unit.ownerLaneIndex;
    if (Number.isInteger(source.unit.ownerLane))
      return source.unit.ownerLane;
    if (Number.isInteger(source.unit.sourceLaneIndex))
      return source.unit.sourceLaneIndex;
    return -1;
  }
  if (source.kind === "tower" && lane && Number.isInteger(lane.laneIndex))
    return lane.laneIndex;
  return -1;
}

function markUnitKillCredit(game, lane, targetUnit, source, deps = {}) {
  if (!game || !lane || !targetUnit || !source)
    return;
  if (!areSourceAndUnitHostile(game, lane, source, targetUnit, deps))
    return;
  const rewardLaneIndex = resolveKillRewardLaneIndex(game, lane, source, deps);
  if (!Number.isInteger(rewardLaneIndex) || rewardLaneIndex < 0)
    return;
  targetUnit.lastHostileKillCredit = {
    rewardLaneIndex,
    sourceKind: source.kind || "unit",
    sourceId: source.sourceId || (source.unit && source.unit.id) || null,
    sourceAllegianceKey: source.allegianceKey || null,
    tick: Number.isInteger(game.tick) ? game.tick : 0,
  };
}

function applyUnitKillReward(game, deadUnit, deps = {}) {
  const getLaneByIndex = requireDepFunction(deps, "getLaneByIndex");
  const killCredit = deadUnit && deadUnit.lastHostileKillCredit;
  const rewardLaneIndex = Number.isInteger(killCredit && killCredit.rewardLaneIndex)
    ? killCredit.rewardLaneIndex
    : -1;
  if (rewardLaneIndex < 0)
    return null;
  const rewardLane = getLaneByIndex(game, rewardLaneIndex);
  if (!rewardLane)
    return null;
  const bounty = Math.max(0, Number(deadUnit && deadUnit.bounty) || 0);
  if (bounty <= 0)
    return null;
  rewardLane.gold = Math.max(0, Number(rewardLane.gold) || 0) + bounty;
  return {
    bounty,
    killCredit,
    rewardLane,
  };
}

function findAdditionalSplashTargets(game, lane, attacker, primaryTarget, targetLane, maxTargets, radius, deps = {}) {
  if (!game || !attacker || !primaryTarget || !targetLane || !Array.isArray(targetLane.units) || maxTargets <= 0)
    return [];

  return targetLane.units
    .filter((candidate) =>
      candidate
      && candidate !== primaryTarget
      && Number(candidate.hp) > 0
      && areUnitsHostile(game, targetLane, attacker, candidate, deps)
      && getUnitDistance(primaryTarget, candidate) <= radius)
    .sort((left, right) => {
      const distDelta = getUnitDistance(primaryTarget, left) - getUnitDistance(primaryTarget, right);
      if (Math.abs(distDelta) > 0.0001)
        return distDelta;
      return String(left.id || "").localeCompare(String(right.id || ""));
    })
    .slice(0, maxTargets);
}

function findAdditionalChainHealTargets(game, lane, healer, primaryTarget, maxTargets, radius, deps = {}) {
  const resolveUnitAllegianceKey = requireDepFunction(deps, "resolveUnitAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  if (!game || !lane || !healer || !primaryTarget || !Array.isArray(lane.units) || maxTargets <= 0)
    return [];

  const healerAllegiance = resolveUnitAllegianceKey(game, lane, healer);
  return lane.units
    .filter((candidate) => {
      if (!candidate || candidate === healer || candidate === primaryTarget || Number(candidate.hp) <= 0)
        return false;
      const maxHp = Math.max(1, Number(candidate.maxHp) || Number(candidate.hp) || 1);
      if ((Number(candidate.hp) || 0) >= maxHp)
        return false;
      const candidateAllegiance = resolveUnitAllegianceKey(game, lane, candidate);
      if (areAllegiancesHostile(healerAllegiance, candidateAllegiance))
        return false;
      return getUnitDistance(primaryTarget, candidate) <= radius;
    })
    .sort((left, right) => {
      const leftRatio = (Number(left.hp) || 0) / Math.max(1, Number(left.maxHp) || Number(left.hp) || 1);
      const rightRatio = (Number(right.hp) || 0) / Math.max(1, Number(right.maxHp) || Number(right.hp) || 1);
      if (Math.abs(leftRatio - rightRatio) > 0.0001)
        return leftRatio - rightRatio;
      const distDelta = getUnitDistance(primaryTarget, left) - getUnitDistance(primaryTarget, right);
      if (Math.abs(distDelta) > 0.0001)
        return distDelta;
      return String(left.id || "").localeCompare(String(right.id || ""));
    })
    .slice(0, maxTargets);
}

function fireLaneWallArcherTurrets(game, lane, deps = {}) {
  const fireProjectile = requireDepFunction(deps, "fireProjectile");
  const getFortressPadState = requireDepFunction(deps, "getFortressPadState");
  const getFortressPadCombatTarget = requireDepFunction(deps, "getFortressPadCombatTarget");
  const getLaneWallArcherTurretDefenseProfile = requireDepFunction(deps, "getLaneWallArcherTurretDefenseProfile");
  const resolveLaneAllegianceKey = requireDepFunction(deps, "resolveLaneAllegianceKey");
  const resolveUnitAllegianceKey = requireDepFunction(deps, "resolveUnitAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  const profile = getLaneWallArcherTurretDefenseProfile(lane);
  if (!game || !lane || !profile || !Array.isArray(profile.turretPadIds) || profile.turretPadIds.length <= 0)
    return 0;

  if (!lane.wallArcherCooldowns || typeof lane.wallArcherCooldowns !== "object")
    lane.wallArcherCooldowns = {};

  const laneAllegiance = resolveLaneAllegianceKey(lane);
  let shotsFired = 0;
  for (const turretPadId of profile.turretPadIds) {
    const cooldownKey = `wall_archer:${turretPadId}`;
    const nextReadyTick = Math.max(0, Math.floor(Number(lane.wallArcherCooldowns[cooldownKey]) || 0));
    if (game.tick < nextReadyTick)
      continue;

    const turretState = getFortressPadState(lane, turretPadId);
    if (!turretState)
      continue;
    const turretTarget = getFortressPadCombatTarget(lane, turretState);
    if (!turretTarget)
      continue;

    let bestTarget = null;
    let bestDistance = Infinity;
    for (const candidate of lane.units || []) {
      if (!candidate || Number(candidate.hp) <= 0)
        continue;
      const candidateAllegiance = resolveUnitAllegianceKey(game, lane, candidate);
      if (!areAllegiancesHostile(laneAllegiance, candidateAllegiance))
        continue;
      const distance = getUnitDistance(turretTarget, candidate);
      if (!Number.isFinite(distance) || distance > profile.range)
        continue;
      if (distance < bestDistance - 0.0001
          || (Math.abs(distance - bestDistance) <= 0.0001
            && String(candidate.id || "").localeCompare(String(bestTarget && bestTarget.id || "")) < 0)) {
        bestTarget = candidate;
        bestDistance = distance;
      }
    }

    if (!bestTarget)
      continue;

    fireProjectile(
      game,
      lane,
      { id: turretPadId, kind: "tower", x: turretTarget.posX, y: turretTarget.posY },
      bestTarget.id,
      {
        dmg: profile.damage,
        damageType: profile.damageType,
        behavior: "single",
        travelTicks: profile.projectileTicks,
        projectileType: "wall_archer",
        abilities: [],
      }
    );
    lane.wallArcherCooldowns[cooldownKey] = game.tick + Math.max(1, Math.floor(Number(profile.attackCooldownTicks) || 1));
    shotsFired += 1;
  }

  return shotsFired;
}

function processLane(game, lane, deps = {}) {
  const log = deps && deps.log;
  const combatLog = deps && deps.combatLog;
  const createCombatTargetingContext = typeof deps.createCombatTargetingContext === "function"
    ? deps.createCombatTargetingContext
    : null;
  const waveUnitStates = getWaveUnitStates(deps);
  const unitMovementModes = getUnitMovementModes(deps);
  const minUnitSpacing = getMinUnitSpacing(deps);
  const contactSlotTolerance = getContactSlotTolerance(deps);
  const waveRouteCombatRecoveryTicks = getWaveRouteCombatRecoveryTicks(deps);
  const enableWaveUnitTrace = getEnableWaveUnitTrace(deps);
  const markLaneCommandAssignmentsDirty = typeof deps.markLaneCommandAssignmentsDirty === "function"
    ? deps.markLaneCommandAssignmentsDirty
    : null;
  const laneHasOccupyingForces = requireDepFunction(deps, "laneHasOccupyingForces");
  const resolveUnitSupportProfile = requireDepFunction(deps, "resolveUnitSupportProfile");
  const resolveStatuses = requireDepFunction(deps, "resolveStatuses");
  const traceWaveUnitTick = enableWaveUnitTrace
    ? requireDepFunction(deps, "traceWaveUnitTick")
    : null;
  const resolveWaveCombatTarget = requireDepFunction(deps, "resolveWaveCombatTarget");
  const isUnitCombatTargetStillValid = requireDepFunction(deps, "isUnitCombatTargetStillValid");
  const clearUnitCombatTarget = requireDepFunction(deps, "clearUnitCombatTarget");
  const hasImmediateFollowThroughCombatTarget = requireDepFunction(deps, "hasImmediateFollowThroughCombatTarget");
  const isLaneControlledUnitInRegroupWindow = requireDepFunction(deps, "isLaneControlledUnitInRegroupWindow");
  const canLaneControlledUnitSeekCombat = requireDepFunction(deps, "canLaneControlledUnitSeekCombat");
  const getWaveUnitPreferredTarget = requireDepFunction(deps, "getWaveUnitPreferredTarget");
  const isRouteUnitTargetBlockedByStructure = requireDepFunction(deps, "isRouteUnitTargetBlockedByStructure");
  const shouldSwitchCombatTarget = requireDepFunction(deps, "shouldSwitchCombatTarget");
  const assignUnitCombatTarget = requireDepFunction(deps, "assignUnitCombatTarget");
  const findBlockingStructureTarget = requireDepFunction(deps, "findBlockingStructureTarget");
  const markTownCoreBreach = requireDepFunction(deps, "markTownCoreBreach");
  const getRouteUnitTargetPressureCount = requireDepFunction(deps, "getRouteUnitTargetPressureCount");
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
  const shouldUseLaneControlledSurroundSlots = requireDepFunction(deps, "shouldUseLaneControlledSurroundSlots");
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
  const recordBalanceUnitDeath = typeof deps.recordBalanceUnitDeath === "function"
    ? deps.recordBalanceUnitDeath
    : null;
  const recordBalanceIncome = typeof deps.recordBalanceIncome === "function"
    ? deps.recordBalanceIncome
    : null;
  const recordBalanceDamage = typeof deps.recordBalanceDamage === "function"
    ? deps.recordBalanceDamage
    : null;
  const recordBalanceHealing = typeof deps.recordBalanceHealing === "function"
    ? deps.recordBalanceHealing
    : null;
  const isScheduledWaveUnit = requireDepFunction(deps, "isScheduledWaveUnit");
  const applyCanonicalUnitMirrors = requireDepFunction(deps, "applyCanonicalUnitMirrors");
  const normalizeLegacyDefenderUnit = typeof deps.normalizeLegacyDefenderUnit === "function"
    ? deps.normalizeLegacyDefenderUnit
    : null;
  const targetingContext = createCombatTargetingContext
    ? createCombatTargetingContext(game)
    : null;
  const lanePerf = {
    prepMs: 0,
    unitLogicMs: 0,
    separationMs: 0,
    projectileMs: 0,
    cleanupMs: 0,
  };

  const laneHasActiveOccupiers = laneHasOccupyingForces(lane);
  if (lane.eliminated && !laneHasActiveOccupiers)
    return lanePerf;

  const laneActive = !lane.eliminated;

  let phaseStartedAt = performance.now();
  materializeLaneSpawnQueue(game, lane, deps);
  decrementLaneCooldowns(lane);
  lanePerf.prepMs = performance.now() - phaseStartedAt;

  const dotDeadIds = new Set();

  phaseStartedAt = performance.now();
  for (const unit of lane.units || []) {
    if (unit.hp <= 0)
      continue;
    if (unit.isDefender && normalizeLegacyDefenderUnit) {
      if (normalizeLegacyDefenderUnit(game, lane, unit))
        applyCanonicalUnitMirrors(game, lane, unit);
    }
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
      if (traceWaveUnitTick) {
        traceWaveUnitTick(game, lane, unit, null, {
          movementAdvanced: false,
          coreDamageApplied: false,
        });
      }
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
            suppressRegroup: !resolvedCombatTarget && hasImmediateFollowThroughCombatTarget(game, lane, unit, targetingContext),
          });
          resolvedCombatTarget = null;
        }
      }

      const currentTargetInDirectContact = !!(
        resolvedCombatTarget
        && resolvedCombatTarget.kind === "unit"
        && resolvedCombatTarget.entity
        && isUnitInCombatContact(lane, unit, resolvedCombatTarget.entity)
      );
      const currentDefenseStructureLock = !!(
        resolvedCombatTarget
        && resolvedCombatTarget.kind === "fortress_pad"
        && ["wall", "gate", "turret"].includes(String(resolvedCombatTarget.buildingType || resolvedCombatTarget.type || "").trim().toLowerCase())
      );
      const canReacquireUnitTarget = !currentTargetInDirectContact
        && !isLaneControlledUnitInRegroupWindow(unit, game.tick);
      const canSeekCombat = canLaneControlledUnitSeekCombat(game, unit);
      const currentUnitTargetDescriptor = resolvedCombatTarget
        && resolvedCombatTarget.kind === "unit"
        && resolvedCombatTarget.entity
        ? {
            kind: "unit",
            entity: resolvedCombatTarget.entity,
            laneIndex: resolvedCombatTarget.laneIndex,
            reason: "current_unit_target",
          }
        : null;
      let blockingTarget = null;
      if (canSeekCombat && (!resolvedCombatTarget || resolvedCombatTarget.kind !== "unit" || canReacquireUnitTarget || currentUnitTargetDescriptor))
        blockingTarget = findBlockingStructureTarget(game, lane, unit);
      if (currentUnitTargetDescriptor
          && !currentTargetInDirectContact
          && isRouteUnitTargetBlockedByStructure(game, lane, unit, currentUnitTargetDescriptor, blockingTarget)) {
        clearUnitCombatTarget(unit, game.tick, { suppressRegroup: true });
        resolvedCombatTarget = null;
      }
      let directPreferredTarget = null;
      if (canReacquireUnitTarget && canSeekCombat) {
        directPreferredTarget = getWaveUnitPreferredTarget(game, lane, unit, targetingContext);
        if (directPreferredTarget && currentDefenseStructureLock) {
          directPreferredTarget = null;
        }
        if (directPreferredTarget
            && isRouteUnitTargetBlockedByStructure(game, lane, unit, directPreferredTarget, blockingTarget)) {
          directPreferredTarget = null;
        }
      }

      if (directPreferredTarget)
        preferredTarget = directPreferredTarget;

      if (preferredTarget && preferredTarget.kind === "unit") {
        const shouldAssignPreferredTarget = !resolvedCombatTarget
          || shouldSwitchCombatTarget(game, lane, unit, resolvedCombatTarget, preferredTarget, targetingContext);
        if (shouldAssignPreferredTarget) {
          assignUnitCombatTarget(unit, {
            unitId: preferredTarget.entity.id,
            kind: "unit",
            laneIndex: preferredTarget.laneIndex,
          }, game.tick);
          resolvedCombatTarget = resolveWaveCombatTarget(game, lane, unit.combatTarget);
        }
      }

      const canReevaluateStructureTarget = !resolvedCombatTarget || resolvedCombatTarget.kind !== "unit";
      if (canReevaluateStructureTarget) {
        const activeBlockingTarget = blockingTarget || findBlockingStructureTarget(game, lane, unit);
        const currentStructureTargetId = resolvedCombatTarget && resolvedCombatTarget.kind === "fortress_pad"
          ? String(resolvedCombatTarget.padId || resolvedCombatTarget.unitId || "")
          : "";
        const nextStructureTargetId = activeBlockingTarget
          ? String(activeBlockingTarget.padId || activeBlockingTarget.unitId || "")
          : "";
        if (activeBlockingTarget && currentStructureTargetId !== nextStructureTargetId) {
          if (activeBlockingTarget.buildingType === "town_core")
            markTownCoreBreach(game, lane, unit, activeBlockingTarget);
          unit.blockedByStructure = true;
          unit.blockedByStructureId = activeBlockingTarget.unitId;
          unit.blockedByStructureType = activeBlockingTarget.buildingType || activeBlockingTarget.type || null;
          assignUnitCombatTarget(unit, {
            unitId: activeBlockingTarget.unitId,
            kind: "fortress_pad",
            padId: activeBlockingTarget.padId,
            laneIndex: Number.isInteger(activeBlockingTarget.laneIndex) ? activeBlockingTarget.laneIndex : lane.laneIndex,
          }, game.tick);
          resolvedCombatTarget = resolveWaveCombatTarget(game, lane, unit.combatTarget);
        } else if (!activeBlockingTarget && !unit.combatTarget) {
          unit.blockedByStructure = false;
          unit.blockedByStructureId = null;
          unit.blockedByStructureType = null;
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
            const healAmount = Math.max(1, Math.round(Number(unit.healAmountOverride) || Number(supportProfile.healAmount) || 1));
            const maxHp = Math.max(1, Number(healTarget.maxHp) || Number(healTarget.hp) || 1);
            healTarget.hp = Math.min(maxHp, (Number(healTarget.hp) || 0) + healAmount);
            if (recordBalanceHealing)
              recordBalanceHealing(game, lane, unit, healTarget, healAmount);
            const chainHealExtraTargets = Math.max(0, Math.floor(Number(unit.chainHealExtraTargets) || 0));
            if (chainHealExtraTargets > 0) {
              const chainTargets = findAdditionalChainHealTargets(
                game,
                lane,
                unit,
                healTarget,
                chainHealExtraTargets,
                Math.max(0.5, Number(unit.chainHealRadius) || 1.75),
                deps
              );
              for (const extraTarget of chainTargets) {
                const extraMaxHp = Math.max(1, Number(extraTarget.maxHp) || Number(extraTarget.hp) || 1);
                extraTarget.hp = Math.min(extraMaxHp, (Number(extraTarget.hp) || 0) + healAmount);
                if (recordBalanceHealing)
                  recordBalanceHealing(game, lane, unit, extraTarget, healAmount);
              }
            }
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
          suppressRegroup: hasImmediateFollowThroughCombatTarget(game, lane, unit, targetingContext),
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
          !startedTickWithCombatTarget
          && Number.isFinite(directContactDistance)
          && directContactDistance <= stopDist + contactSlotTolerance
          && shouldUseLaneControlledSurroundSlots(unit, targetUnit)
        );
        const inCombatContact = allowImmediateLaneContact || isUnitInCombatContact(lane, unit, targetUnit);
        if (inCombatContact) {
            if (unit.atkCd <= 0) {
              const attackStats = resolveUnitAttackStats(unit, deps);
              const appliedDamage = applyDirectUnitDamage(targetUnit, attackStats.damage);
              if (appliedDamage > 0)
                markUnitKillCredit(game, targetLane || lane, targetUnit, { kind: "unit", unit }, deps);
              if (recordBalanceDamage && appliedDamage > 0)
                recordBalanceDamage(game, lane, unit, targetLane || lane, targetUnit, appliedDamage);
              const splashExtraTargets = Math.max(0, Math.floor(Number(unit.splashExtraTargets) || 0));
              if (splashExtraTargets > 0) {
                const splashTargets = findAdditionalSplashTargets(
                game,
                targetLane || lane,
                unit,
                targetUnit,
                targetLane || lane,
                splashExtraTargets,
                Math.max(0.5, Number(unit.splashRadius) || 1.5),
                deps
                );
                for (const splashTarget of splashTargets) {
                  const splashDamage = applyDirectUnitDamage(splashTarget, attackStats.damage);
                  if (splashDamage > 0)
                    markUnitKillCredit(game, targetLane || lane, splashTarget, { kind: "unit", unit }, deps);
                  if (recordBalanceDamage && splashDamage > 0)
                    recordBalanceDamage(game, lane, unit, targetLane || lane, splashTarget, splashDamage);
                }
              }
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
                suppressRegroup: hasImmediateFollowThroughCombatTarget(game, lane, unit, targetingContext),
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
          moveTowardSimpleContact2D(unit, target, unit.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
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

    const traceTarget = traceWaveUnitTick && unit.combatTarget
      ? resolveWaveCombatTarget(game, lane, unit.combatTarget)
      : null;
    unit.state = unit.combatState || unit.routeState || unit.state || waveUnitStates.IDLE;
    unit.currentTargetId = unit.combatTargetId || (unit.combatTarget && unit.combatTarget.unitId) || null;
    if (traceWaveUnitTick) {
      traceWaveUnitTick(game, lane, unit, traceTarget, {
        movementAdvanced,
        coreDamageApplied,
        preferredTargetReason: preferredTarget ? preferredTarget.reason : null,
      });
    }
  }
  lanePerf.unitLogicMs = performance.now() - phaseStartedAt;

  phaseStartedAt = performance.now();
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
  lanePerf.separationMs = performance.now() - phaseStartedAt;

  const killedById = new Set();
  const stillFlying = [];
  phaseStartedAt = performance.now();
  for (const projectile of lane.projectiles || []) {
    projectile.ticksRemaining -= 1;
    if (projectile.ticksRemaining > 0) {
      stillFlying.push(projectile);
      continue;
    }
    const { dead, hit } = resolveProjectile(game, lane, projectile);
    const attackerUnit = projectile.sourceKind === "unit"
      ? (lane.units || []).find((unit) => unit && unit.id === projectile.sourceId) || { type: projectile.projectileType || "projectile" }
      : { type: projectile.projectileType || "tower" };
    for (const hitId of hit) {
      const hitUnit = (lane.units || []).find((unit) => unit && unit.id === hitId);
      if (hitUnit) {
        markUnitKillCredit(game, lane, hitUnit, {
          kind: projectile.sourceKind || "tower",
          unit: projectile.sourceKind === "unit" && attackerUnit && attackerUnit.id === projectile.sourceId
            ? attackerUnit
            : null,
          rewardLaneIndex: Number.isInteger(projectile.rewardLaneIndex) ? projectile.rewardLaneIndex : null,
          allegianceKey: projectile.sourceAllegianceKey || null,
          sourceId: projectile.sourceId || null,
        }, deps);
      }
    }
    if (recordBalanceDamage && hit.length > 0) {
      const attackerTeam = projectile.sourceKind === "tower"
        ? "player"
        : attackerUnit && (attackerUnit.isWaveUnit || String(attackerUnit.spawnSourceType || "").trim().toLowerCase() === "dungeon_wave")
          ? "enemy"
          : "player";
      for (const hitId of hit) {
        const hitUnit = (lane.units || []).find((unit) => unit && unit.id === hitId);
        if (hitUnit)
          recordBalanceDamage(game, lane, attackerUnit, lane, hitUnit, projectile.dmg || 0, {
            projectileType: projectile.projectileType || null,
            attackerTeam,
          });
      }
    }
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
  lanePerf.projectileMs = performance.now() - phaseStartedAt;

  for (const id of dotDeadIds)
    killedById.add(id);

  phaseStartedAt = performance.now();
  let removedLaneControlledUnit = false;
  for (const unit of lane.units || []) {
    if (unit.hp > 0)
      continue;
    if (Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0)
      removedLaneControlledUnit = true;
    if (recordBalanceUnitDeath)
      recordBalanceUnitDeath(game, lane, unit);
    if (unit.abilities && unit.abilities.length > 0)
      resolveAbilityHook(game, lane, unit, "onDeath", {});
    const reward = applyUnitKillReward(game, unit, deps);
    if (reward && recordBalanceIncome)
      recordBalanceIncome(game, reward.rewardLane, "kill_reward", reward.bounty);
    if (isScheduledWaveUnit(unit)) {
      combatLog.logEvent(game, "wave_unit_killed", {
        unitId: unit.id,
        unitType: unit.type,
        bounty: reward ? reward.bounty : Math.max(0, Number(unit.bounty) || 0),
        lane: lane.laneIndex,
        rewardedLane: reward && reward.rewardLane ? reward.rewardLane.laneIndex : null,
      });
    }
  }
  lane.units = (lane.units || []).filter((unit) => unit.hp > 0);
  if (removedLaneControlledUnit && markLaneCommandAssignmentsDirty)
    markLaneCommandAssignmentsDirty(game);
  fireLaneWallArcherTurrets(game, lane, deps);
  lanePerf.cleanupMs = performance.now() - phaseStartedAt;
  return lanePerf;
}

function finalizeTick(game, deps = {}) {
  const log = deps && deps.log;
  const createRoundSnapshotLane = requireDepFunction(deps, "createRoundSnapshotLane");
  const buildTownCoreStateSummary = requireDepFunction(deps, "buildTownCoreStateSummary");
  const finalizeBalanceWaveReport = typeof deps.finalizeBalanceWaveReport === "function"
    ? deps.finalizeBalanceWaveReport
    : null;

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
    if (finalizeBalanceWaveReport)
      finalizeBalanceWaveReport(game, { defeat: true, cleared: false });
    game.phase = "ended";
    game.matchState = "final_game_over";
    game.winner = Number.isInteger(game.officialWinnerLane) ? game.officialWinnerLane : null;
  }
}

function mlTick(game, deps = {}) {
  const grantScheduledIncome = requireDepFunction(deps, "grantScheduledIncome");
  const runScheduledWaves = requireDepFunction(deps, "runScheduledWaves");
  const runScheduledBuildingConstruction = requireDepFunction(deps, "runScheduledBuildingConstruction");
  const runScheduledBarracksSends = requireDepFunction(deps, "runScheduledBarracksSends");
  const syncLaneCommandAssignments = requireDepFunction(deps, "syncLaneCommandAssignments");
  const recordBalanceTick = typeof deps.recordBalanceTick === "function"
    ? deps.recordBalanceTick
    : null;

  if (!game)
    return;

  game._lastTickPerfBreakdown = null;
  if (game.phase !== "playing")
    return;

  const tickBreakdown = {
    incomeMs: 0,
    scheduledWavesMs: 0,
    buildingConstructionMs: 0,
    barracksSendsMs: 0,
    syncLaneCommandsMs: 0,
    lanesMs: 0,
    balanceTickMs: 0,
    finalizeMs: 0,
    slowestLanes: [],
  };

  game.tick += 1;
  if (!game.startedAt)
    game.startedAt = Date.now();

  let phaseStartedAt = performance.now();
  grantScheduledIncome(game);
  tickBreakdown.incomeMs = performance.now() - phaseStartedAt;

  phaseStartedAt = performance.now();
  runScheduledWaves(game);
  tickBreakdown.scheduledWavesMs = performance.now() - phaseStartedAt;

  phaseStartedAt = performance.now();
  runScheduledBuildingConstruction(game);
  tickBreakdown.buildingConstructionMs = performance.now() - phaseStartedAt;

  phaseStartedAt = performance.now();
  runScheduledBarracksSends(game);
  tickBreakdown.barracksSendsMs = performance.now() - phaseStartedAt;

  phaseStartedAt = performance.now();
  syncLaneCommandAssignments(game);
  tickBreakdown.syncLaneCommandsMs = performance.now() - phaseStartedAt;

  game.roundState = "combat";
  game.roundStateTicks = Number.isInteger(game.lastWaveSpawnTick)
    ? Math.max(0, game.tick - game.lastWaveSpawnTick)
    : game.tick;

  const laneTimings = [];
  phaseStartedAt = performance.now();
  for (const lane of game.lanes || []) {
    const laneStartedAt = performance.now();
    const lanePerf = processLane(game, lane, deps) || null;
    laneTimings.push({
      laneIndex: lane && Number.isInteger(lane.laneIndex) ? lane.laneIndex : -1,
      ms: performance.now() - laneStartedAt,
      unitCount: Array.isArray(lane && lane.units) ? lane.units.length : 0,
      projectileCount: Array.isArray(lane && lane.projectiles) ? lane.projectiles.length : 0,
      eliminated: !!(lane && lane.eliminated),
      perf: lanePerf,
    });
  }
  tickBreakdown.lanesMs = performance.now() - phaseStartedAt;

  if (recordBalanceTick) {
    phaseStartedAt = performance.now();
    recordBalanceTick(game);
    tickBreakdown.balanceTickMs = performance.now() - phaseStartedAt;
  }

  phaseStartedAt = performance.now();
  finalizeTick(game, deps);
  tickBreakdown.finalizeMs = performance.now() - phaseStartedAt;

  tickBreakdown.slowestLanes = laneTimings
    .sort((a, b) => b.ms - a.ms)
    .slice(0, 3)
    .map((lanePerf) => ({
      laneIndex: lanePerf.laneIndex,
      ms: roundPerfDuration(lanePerf.ms),
      unitCount: lanePerf.unitCount,
      projectileCount: lanePerf.projectileCount,
      eliminated: lanePerf.eliminated,
      prepMs: roundPerfDuration(lanePerf.perf && lanePerf.perf.prepMs),
      unitLogicMs: roundPerfDuration(lanePerf.perf && lanePerf.perf.unitLogicMs),
      separationMs: roundPerfDuration(lanePerf.perf && lanePerf.perf.separationMs),
      projectileMs: roundPerfDuration(lanePerf.perf && lanePerf.perf.projectileMs),
      cleanupMs: roundPerfDuration(lanePerf.perf && lanePerf.perf.cleanupMs),
    }));

  game._lastTickPerfBreakdown = {
    incomeMs: roundPerfDuration(tickBreakdown.incomeMs),
    scheduledWavesMs: roundPerfDuration(tickBreakdown.scheduledWavesMs),
    buildingConstructionMs: roundPerfDuration(tickBreakdown.buildingConstructionMs),
    barracksSendsMs: roundPerfDuration(tickBreakdown.barracksSendsMs),
    syncLaneCommandsMs: roundPerfDuration(tickBreakdown.syncLaneCommandsMs),
    lanesMs: roundPerfDuration(tickBreakdown.lanesMs),
    balanceTickMs: roundPerfDuration(tickBreakdown.balanceTickMs),
    finalizeMs: roundPerfDuration(tickBreakdown.finalizeMs),
    slowestLanes: tickBreakdown.slowestLanes,
  };
}

module.exports = {
  mlTick,
};
