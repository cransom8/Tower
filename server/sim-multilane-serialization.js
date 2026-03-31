"use strict";

const crypto = require("crypto");

function getLaneBarracksSendTimerSnapshot(game, lane, deps) {
  const { ensureBarracksSiteStates, BARRACKS_SITE_DEFS, getBarracksSiteSendIntervalTicks } = deps;
  const states = ensureBarracksSiteStates(lane, game);
  let bestRemaining = Infinity;
  let bestTotal = 0;

  for (const siteDef of BARRACKS_SITE_DEFS) {
    const state = states[siteDef.barracksId];
    if (!state || !state.isBuilt)
      continue;

    const interval = getBarracksSiteSendIntervalTicks(state.level || 1);
    const remaining = Math.max(
      0,
      Math.floor(Number(state.nextSendTick) || 0) - Math.floor(Number(game && game.tick) || 0)
    );
    if (remaining < bestRemaining) {
      bestRemaining = remaining;
      bestTotal = interval;
    }
  }

  return {
    remaining: Number.isFinite(bestRemaining) ? bestRemaining : 0,
    total: bestTotal > 0 ? bestTotal : 0,
  };
}

function getUnitSnapshotBarracksLevel(game, lane, unit, deps) {
  const { normalizeBarracksSiteId, describeBarracksSite, isScheduledWaveUnit } = deps;
  if (!unit || (typeof isScheduledWaveUnit === "function" && isScheduledWaveUnit(unit)))
    return 1;

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : lane && lane.laneIndex;
  const sourceLane = game && Array.isArray(game.lanes) && sourceLaneIndex >= 0
    ? game.lanes[sourceLaneIndex]
    : lane;
  const sourceBarracksKey = normalizeBarracksSiteId(unit.sourceBarracksKey || unit.sourceBarracksId);
  if (sourceLane && sourceBarracksKey) {
    const descriptor = describeBarracksSite(game, sourceLane, sourceBarracksKey);
    if (descriptor && descriptor.isBuilt)
      return descriptor.level;
  }

  return Math.max(1, Math.floor(Number(sourceLane && sourceLane.barracks && sourceLane.barracks.level) || 1));
}

function getSerializedLaneBuildValue(lane, barracksRoster, deps) {
  let total = 0;

  if (lane && Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      if (!pad || !Array.isArray(pad.costHistory))
        continue;

      for (const entry of pad.costHistory)
        total += Number(entry && entry.cost) || 0;
    }
  }

  if (lane && lane.barracksSiteStates && typeof lane.barracksSiteStates === "object") {
    for (const siteState of Object.values(lane.barracksSiteStates)) {
      if (!siteState || !Array.isArray(siteState.costHistory))
        continue;

      for (const entry of siteState.costHistory)
        total += Number(entry && entry.cost) || 0;
    }
  }

  if (Array.isArray(barracksRoster)) {
    for (const entry of barracksRoster) {
      if (!entry)
        continue;

      const ownedCount = Math.max(0, Math.floor(Number(entry.ownedCount) || 0));
      const buyCost = Math.max(0, Math.floor(Number(entry.buyCost) || 0));
      total += ownedCount * buyCost;
    }
  }

  return total;
}

function equalsIgnoreCase(left, right) {
  return String(left || "").toLowerCase() === String(right || "").toLowerCase();
}

function getSnapshotUnitAnchorDistance(unit) {
  const posX = Number(unit && unit.posX);
  const posY = Number(unit && unit.posY);
  const anchorX = Number(unit && unit.anchorTargetX);
  const anchorY = Number(unit && unit.anchorTargetY);
  if (!Number.isFinite(posX) || !Number.isFinite(posY) || !Number.isFinite(anchorX) || !Number.isFinite(anchorY))
    return Infinity;

  const dx = posX - anchorX;
  const dy = posY - anchorY;
  return Math.sqrt((dx * dx) + (dy * dy));
}

function resolveSnapshotCombatMetadata(game, lane, unit, deps) {
  const {
    resolveWaveCombatTarget,
    getWaveUnitTargetDistance,
    getUnitStopDistance,
    CONTACT_SLOT_TOLERANCE,
  } = deps;

  const fallbackKind = unit && unit.combatTarget && unit.combatTarget.kind
    ? unit.combatTarget.kind
    : null;
  if (!unit || !unit.combatTarget || !unit.combatTarget.unitId || typeof resolveWaveCombatTarget !== "function") {
    return {
      combatTargetKind: fallbackKind,
      combatContact: false,
    };
  }

  const resolvedTarget = resolveWaveCombatTarget(game, lane, unit.combatTarget);
  if (!resolvedTarget) {
    return {
      combatTargetKind: fallbackKind,
      combatContact: false,
    };
  }

  const distance = typeof getWaveUnitTargetDistance === "function"
    ? getWaveUnitTargetDistance(unit, resolvedTarget)
    : Infinity;
  const stopDistance = typeof getUnitStopDistance === "function"
    ? getUnitStopDistance(unit.type, resolvedTarget.type)
    : Infinity;
  const contactTolerance = Number.isFinite(Number(CONTACT_SLOT_TOLERANCE))
    ? Number(CONTACT_SLOT_TOLERANCE)
    : 0;

  return {
    combatTargetKind: resolvedTarget.kind || fallbackKind,
    combatContact: Number.isFinite(distance)
      && Number.isFinite(stopDistance)
      && distance <= stopDistance + contactTolerance,
  };
}

function resolveSnapshotPresentationState(game, lane, unit, deps) {
  const {
    LANE_COMMAND_STATES,
    UNIT_MOVEMENT_MODES,
    LANE_FORMATION_ARRIVAL_DEAD_ZONE,
  } = deps;

  const currentTick = Math.floor(Number(game && game.tick) || 0);
  const movementMode = unit && unit.movementMode ? unit.movementMode : null;
  const commandState = unit && unit.commandState ? unit.commandState : null;
  const anchorDistance = getSnapshotUnitAnchorDistance(unit);
  const arrivalDeadZone = Number.isFinite(Number(LANE_FORMATION_ARRIVAL_DEAD_ZONE))
    ? Number(LANE_FORMATION_ARRIVAL_DEAD_ZONE)
    : 0.2;
  const settledAtAnchor = Number.isFinite(anchorDistance) && anchorDistance <= arrivalDeadZone;
  const regroupTicksRemaining = Math.max(
    0,
    Math.floor(Number(unit && unit.regroupUntilTick) || 0) - currentTick
  );
  const combatLockTicksRemaining = Math.max(
    0,
    Math.floor(Number(unit && unit.combatTargetLockedUntilTick) || 0) - currentTick
  );
  const combatMetadata = resolveSnapshotCombatMetadata(game, lane, unit, deps);
  const isDefending = equalsIgnoreCase(commandState, LANE_COMMAND_STATES && LANE_COMMAND_STATES.DEFEND || "DEFEND");
  const isRetreating = equalsIgnoreCase(commandState, LANE_COMMAND_STATES && LANE_COMMAND_STATES.RETREAT || "RETREAT");

  let presentationPhase = "Idle";
  if (Number(unit && unit.hp) <= 0) {
    presentationPhase = "Dead";
  } else if (unit && unit.combatTarget && unit.combatTarget.unitId) {
    presentationPhase = combatMetadata.combatContact ? "CombatResolve" : "CombatCommit";
  } else if (isRetreating) {
    presentationPhase = "Retreat";
  } else if (regroupTicksRemaining > 0) {
    presentationPhase = "CombatRegroup";
  } else if (movementMode === (UNIT_MOVEMENT_MODES && UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR || "ReturnToAnchor")) {
    presentationPhase = "ReturnToSlot";
  } else if (movementMode === (UNIT_MOVEMENT_MODES && UNIT_MOVEMENT_MODES.FORMATION_JOIN || "FormationJoin")) {
    presentationPhase = settledAtAnchor ? "FormationHold" : "FormationJoin";
  } else if (movementMode === (UNIT_MOVEMENT_MODES && UNIT_MOVEMENT_MODES.LANE_TRAVEL || "LaneTravel")) {
    presentationPhase = "LaneTravel";
  } else if (settledAtAnchor) {
    presentationPhase = "FormationHold";
  }

  let presentationIntent = "Idle";
  switch (presentationPhase) {
    case "Dead":
      presentationIntent = "Death";
      break;
    case "Retreat":
      presentationIntent = "Retreat";
      break;
    case "CombatCommit":
      presentationIntent = "Move";
      break;
    case "CombatResolve":
      presentationIntent = "Attack";
      break;
    case "CombatRegroup":
      if (settledAtAnchor)
        presentationIntent = isDefending ? "Defend" : "Idle";
      else
        presentationIntent = isRetreating ? "Retreat" : "Move";
      break;
    case "ReturnToSlot":
      presentationIntent = settledAtAnchor
        ? (isDefending ? "Defend" : "Idle")
        : (isRetreating ? "Retreat" : "Move");
      break;
    case "FormationHold":
      presentationIntent = isDefending ? "Defend" : "Idle";
      break;
    case "FormationJoin":
    case "LaneTravel":
      presentationIntent = isRetreating ? "Retreat" : "Move";
      break;
    default:
      presentationIntent = isDefending && settledAtAnchor ? "Defend" : "Idle";
      break;
  }

  return {
    presentationPhase,
    presentationIntent,
    combatTargetKind: combatMetadata.combatTargetKind,
    combatContact: combatMetadata.combatContact,
    regroupTicksRemaining,
    combatLockTicksRemaining,
  };
}

function createMLSnapshot(game, deps) {
  const {
    WAVE_TIMER_TICKS,
    TEAM_HP_START,
    GRID_W,
    GRID_H,
    SPAWN_X,
    SPAWN_YG,
    SHARED_SUFFIX_LENGTH,
    BARRACKS_SITE_DEFS,
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
    LANE_FORMATION_ARRIVAL_DEAD_ZONE,
    CONTACT_SLOT_TOLERANCE,
    resolveWaveCombatTarget,
    getWaveUnitTargetDistance,
    getUnitStopDistance,
  } = deps;
  const survivalTicks = game.continuedIntoSurvival && Number.isInteger(game.survivalStartedAtTick)
    ? Math.max(0, game.tick - game.survivalStartedAtTick)
    : 0;
  const incomeTicksRemaining = Math.max(
    0,
    Math.floor(Number(game.nextIncomeTick) || 0) - Math.floor(Number(game.tick) || 0)
  );
  const waveTimerTotalTicks = Math.max(1, Math.floor(Number(game.waveIntervalTicks) || WAVE_TIMER_TICKS));
  const waveTimerTicksRemaining = Math.max(
    0,
    Math.floor(Number(game.nextWaveTick) || waveTimerTotalTicks) - Math.floor(Number(game.tick) || 0)
  );
  return {
    tick: game.tick,
    phase: game.phase,
    winner: game.winner,
    matchState: game.matchState || (game.phase === "ended" ? "final_game_over" : "active_survival"),
    officialWinnerLane: game.officialWinnerLane,
    continuedIntoSurvival: !!game.continuedIntoSurvival,
    survivalDurationTicks: survivalTicks,
    incomeTicksRemaining,
    roundState: game.roundState || "combat",
    roundNumber: game.roundNumber || 1,
    roundStateTicks: game.roundStateTicks || 0,
    buildPhaseTotal: 0,
    transitionPhaseTotal: 0,
    waveTimerTicksRemaining,
    waveTimerTotalTicks,
    teamHp: game.teamHp || { left: game.teamHpMax || TEAM_HP_START, right: game.teamHpMax || TEAM_HP_START },
    teamHpMax: game.teamHpMax || TEAM_HP_START,
    lanes: game.lanes.map((lane) => {
      const projectiles = lane.projectiles.map((p) => ({
        id: p.id,
        ownerLane: p.ownerLane,
        sourceKind: p.sourceKind,
        projectileType: p.projectileType,
        damageType: p.damageType,
        isSplash: p.isSplash,
        fromX: p.fromX,
        fromY: p.fromY,
        toX: p.toX,
        toY: p.toY,
        progress: 1 - p.ticksRemaining / p.ticksTotal,
      }));

      const snapFullPath = lane.fullPath || lane.path || [];
      const barracksTimerSnapshot = getLaneBarracksSendTimerSnapshot(game, lane, deps);
      syncLegacyBarracksAggregate(lane);
      const barracksRoster = createBarracksRosterSnapshot(game, lane);
      const mapSnapshotUnit = (u) => {
        const presentation = resolveSnapshotPresentationState(game, lane, u, {
          LANE_COMMAND_STATES,
          UNIT_MOVEMENT_MODES,
          LANE_FORMATION_ARRIVAL_DEAD_ZONE,
          CONTACT_SLOT_TOLERANCE,
          resolveWaveCombatTarget,
          getWaveUnitTargetDistance,
          getUnitStopDistance,
        });
        const canonicalTargetLaneIndex = typeof resolveUnitTargetLaneIndex === "function"
          ? resolveUnitTargetLaneIndex(game, lane, u)
          : (Number.isInteger(u.targetLaneIndex) ? u.targetLaneIndex : lane.laneIndex);
        const canonicalOwnerLaneIndex = typeof resolveUnitOwnerLaneIndex === "function"
          ? resolveUnitOwnerLaneIndex(game, lane, u)
          : (Number.isInteger(u.ownerLane) ? u.ownerLane : -1);
        const canonicalSourceBarracksId = typeof resolveUnitSourceBarracksId === "function"
          ? resolveUnitSourceBarracksId(u)
          : (u.sourceBarracksId || u.sourceBarracksKey || null);
        const canonicalAllegianceKey = typeof resolveUnitAllegianceKey === "function"
          ? resolveUnitAllegianceKey(game, lane, u)
          : (u.allegianceKey || null);
        const canonicalStance = typeof resolveUnitStance === "function"
          ? resolveUnitStance(game, lane, u)
          : (u.stance || null);
        const canonicalPathContractType = typeof resolveUnitPathContractType === "function"
          ? resolveUnitPathContractType(game, lane, u)
          : (u.pathContractType || null);
        const pos = getUnitTilePos(u, lane.path);
        const gx = (u.posX !== undefined) ? u.posX : (pos ? pos.x : SPAWN_X);
        const gy = (u.posY !== undefined) ? u.posY : (pos ? pos.y : SPAWN_YG);
        const currentWaypointIndex = Math.max(0, Math.floor(Number(u.routeSegmentIndex) || 0));
        const pathId = typeof buildRoutePathId === "function"
          ? buildRoutePathId(u.routeSegments)
          : (Array.isArray(u.routeSegments) && u.routeSegments.length > 0 ? u.routeSegments.join(">") : null);
        const nextWaypoint = typeof resolveUnitNextWaypoint === "function"
          ? resolveUnitNextWaypoint(u)
          : null;
        const spawnSourceType = typeof resolveSpawnSourceTypeFromUnit === "function"
          ? resolveSpawnSourceTypeFromUnit(u)
          : (u && u.spawnSourceType) || null;
        return {
          id: u.id,
          unitId: u.unitId || u.id,
          laneId: canonicalTargetLaneIndex,
          unitTypeKey: u.unitTypeKey || u.type,
          allegianceKey: canonicalAllegianceKey,
          ownerLaneIndex: canonicalOwnerLaneIndex,
          targetLaneIndex: canonicalTargetLaneIndex,
          objectiveLaneIndex: Number.isInteger(u.objectiveLaneIndex) ? u.objectiveLaneIndex : canonicalTargetLaneIndex,
          pathContractType: canonicalPathContractType,
          ownerLane: canonicalOwnerLaneIndex,
          sourceLaneIndex: Number.isInteger(u.sourceLaneIndex) ? u.sourceLaneIndex : -1,
          sourceTeam: u.sourceTeam || null,
          barracksId: canonicalSourceBarracksId,
          sourceBarracksKey: canonicalSourceBarracksId,
          sourceBarracksId: canonicalSourceBarracksId,
          spawnSourceType,
          type: u.type,
          archetypeKey: u.archetypeKey || null,
          presentationKey: u.presentationKey || null,
          catalogUnitKey: u.catalogUnitKey || null,
          skinKey: u.skinKey || null,
          isHero: !!u.isHero,
          heroKey: u.heroKey || null,
          heroVisualStyleKey: u.heroVisualStyleKey || null,
          groupId: u.groupId || null,
          combatRole: u.combatRole || null,
          preferredBand: u.preferredBand || null,
          pathIdx: u.pathIdx,
          gridX: gx,
          gridY: gy,
          normProgress: u.pathIdx <= GRID_H ? 0 : Math.min(1, (u.pathIdx - GRID_H) / (SHARED_SUFFIX_LENGTH - 1)),
          routeType: u.routeType || null,
          routeStartNode: u.routeStartNode || null,
          routeTargetNode: u.routeTargetNode || null,
          pathId,
          currentWaypointIndex,
          nextWaypoint,
          currentSegment: u.currentSegment || null,
          segmentProgress: Number.isFinite(Number(u.segmentProgress)) ? Number(u.segmentProgress) : 0,
          stance: canonicalStance,
          commandState: u.commandState || null,
          movementMode: u.movementMode || null,
          movementState: u.routeState || u.combatState || null,
          state: u.routeState || u.combatState || null,
          presentationPhase: presentation.presentationPhase,
          presentationIntent: presentation.presentationIntent,
          blockedByStructure: !!u.blockedByStructure,
          blockedByStructureId: u.blockedByStructureId || null,
          routeWorldX: Number.isFinite(Number(u.routeWorldX)) ? Number(u.routeWorldX) : gx,
          routeWorldY: Number.isFinite(Number(u.routeWorldY)) ? Number(u.routeWorldY) : gy,
          currentSlotIndex: Number.isInteger(u.currentSlotIndex) ? u.currentSlotIndex : -1,
          anchorTargetX: Number.isFinite(Number(u.anchorTargetX)) ? Number(u.anchorTargetX) : null,
          anchorTargetY: Number.isFinite(Number(u.anchorTargetY)) ? Number(u.anchorTargetY) : null,
          anchorTargetProgress: Number.isFinite(Number(u.anchorTargetProgress)) ? Number(u.anchorTargetProgress) : null,
          groupCenterX: Number.isFinite(Number(u.groupCenterX)) ? Number(u.groupCenterX) : null,
          groupCenterY: Number.isFinite(Number(u.groupCenterY)) ? Number(u.groupCenterY) : null,
          cohesionRadius: Number.isFinite(Number(u.cohesionRadius)) ? Number(u.cohesionRadius) : null,
          leashFromGroupCenter: Number.isFinite(Number(u.leashFromGroupCenter)) ? Number(u.leashFromGroupCenter) : null,
          currentWaypointTargetX: Number.isFinite(Number(u.currentWaypointTargetX)) ? Number(u.currentWaypointTargetX) : null,
          currentWaypointTargetY: Number.isFinite(Number(u.currentWaypointTargetY)) ? Number(u.currentWaypointTargetY) : null,
          currentWaypointTargetKind: u.currentWaypointTargetKind || null,
          combatLeashRadius: Number.isFinite(Number(u.combatLeashRadius)) ? Number(u.combatLeashRadius) : null,
          canEngage: !!u.canEngage,
          hp: u.hp,
          maxHp: u.maxHp,
          moveSpeed: Number(u.baseSpeed) || 0,
          isWaveUnit: !!(typeof isScheduledWaveUnit === "function" ? isScheduledWaveUnit(u) : u.isWaveUnit),
          isAttacking: !!(u.combatTarget && u.combatTarget.unitId),
          combatTargetKind: presentation.combatTargetKind,
          combatTargetId: u.combatTargetId || (u.combatTarget && u.combatTarget.unitId ? u.combatTarget.unitId : null),
          currentTargetId: u.currentTargetId || u.combatTargetId || (u.combatTarget && u.combatTarget.unitId ? u.combatTarget.unitId : null),
          combatContact: presentation.combatContact,
          regroupTicksRemaining: presentation.regroupTicksRemaining,
          combatLockTicksRemaining: presentation.combatLockTicksRemaining,
          attackPulse: Number(u.attackPulse) || 0,
          level: getUnitSnapshotBarracksLevel(game, lane, u, deps),
        };
      };
      return {
        laneIndex: lane.laneIndex,
        team: lane.team || "red",
        allegianceKey: typeof resolveLaneAllegianceKey === "function"
          ? resolveLaneAllegianceKey(lane)
          : (lane.allegianceKey || lane.team || "red"),
        side: lane.side || null,
        slotKey: lane.slotKey || null,
        slotColor: lane.slotColor || null,
        branchId: lane.branchId || null,
        branchLabel: lane.branchLabel || null,
        castleSide: lane.castleSide || null,
        eliminated: lane.eliminated,
        commandState: lane.commandState || null,
        commandTargetLaneIndex: Number.isInteger(lane.commandTargetLaneIndex) ? lane.commandTargetLaneIndex : lane.laneIndex,
        commandAnchorProgress: Number.isFinite(Number(lane.commandAnchorProgress)) ? Number(lane.commandAnchorProgress) : 0,
        insideGateAnchor: lane.insideGateAnchor
          ? {
              x: Number(lane.insideGateAnchor.x) || 0,
              y: Number(lane.insideGateAnchor.y) || 0,
            }
          : null,
        outsideGateAnchor: lane.outsideGateAnchor
          ? {
              x: Number(lane.outsideGateAnchor.x) || 0,
              y: Number(lane.outsideGateAnchor.y) || 0,
            }
          : null,
        enemyCoreAnchor: lane.enemyCoreAnchor
          ? {
              x: Number(lane.enemyCoreAnchor.x) || 0,
              y: Number(lane.enemyCoreAnchor.y) || 0,
            }
          : null,
        formationAnchor: lane.formationAnchor
          ? {
              x: Number(lane.formationAnchor.x) || 0,
              y: Number(lane.formationAnchor.y) || 0,
            }
          : null,
        formationFacing: lane.formationFacing
          ? {
              x: Number(lane.formationFacing.x) || 0,
              y: Number(lane.formationFacing.y) || 0,
            }
          : null,
        formationSlots: Array.isArray(lane.formationSlots)
          ? lane.formationSlots.map((slot) => ({
              slotIndex: Number.isInteger(slot && slot.slotIndex) ? slot.slotIndex : -1,
              unitId: slot && slot.unitId ? slot.unitId : null,
              x: Number(slot && slot.x) || 0,
              y: Number(slot && slot.y) || 0,
            }))
          : [],
        assignedUnits: Array.isArray(lane.assignedUnits) ? lane.assignedUnits.slice() : [],
        packets: Array.isArray(lane.packetStates)
          ? lane.packetStates.map((packet) => ({
              groupId: packet && packet.groupId ? packet.groupId : null,
              laneId: Number.isInteger(packet && packet.laneId) ? packet.laneId : null,
              sourceLaneIndex: Number.isInteger(packet && packet.sourceLaneIndex) ? packet.sourceLaneIndex : null,
              sourceBarracksId: packet && packet.sourceBarracksId ? packet.sourceBarracksId : null,
              stance: packet && packet.stance ? packet.stance : null,
              currentWaypointTarget: packet && packet.currentWaypointTarget
                ? {
                    kind: packet.currentWaypointTarget.kind || null,
                    laneIndex: Number.isInteger(packet.currentWaypointTarget.laneIndex) ? packet.currentWaypointTarget.laneIndex : null,
                    x: Number(packet.currentWaypointTarget.x) || 0,
                    y: Number(packet.currentWaypointTarget.y) || 0,
                  }
                : null,
              groupCenter: packet && packet.groupCenter
                ? {
                    x: Number(packet.groupCenter.x) || 0,
                    y: Number(packet.groupCenter.y) || 0,
                  }
                : null,
              cohesionRadius: Number(packet && packet.cohesionRadius) || 0,
              movementMode: packet && packet.movementMode ? packet.movementMode : null,
              packetIndex: Number.isInteger(packet && packet.packetIndex) ? packet.packetIndex : -1,
              assignedUnits: Array.isArray(packet && packet.assignedUnits) ? packet.assignedUnits.slice() : [],
              formationSlots: Array.isArray(packet && packet.formationSlots)
                ? packet.formationSlots.map((slot) => ({
                    slotIndex: Number.isInteger(slot && slot.slotIndex) ? slot.slotIndex : -1,
                    unitId: slot && slot.unitId ? slot.unitId : null,
                    band: slot && slot.band ? slot.band : null,
                    x: Number(slot && slot.x) || 0,
                    y: Number(slot && slot.y) || 0,
                  }))
                : [],
            }))
          : [],
        engagementRadius: Number.isFinite(Number(lane.engagementRadius)) ? Number(lane.engagementRadius) : 0,
        combatEnabled: !!lane.combatEnabled,
        gold: lane.gold,
        income: lane.income,
        buildValue: getSerializedLaneBuildValue(lane, barracksRoster, deps),
        lives: lane.lives,
        barracksLevel: Math.max(1, Math.floor(Number(lane.barracks && lane.barracks.level) || 1)),
        fortressPads: Array.isArray(lane.fortressPads)
          ? lane.fortressPads.map((pad) => createFortressPadSnapshot(game, lane, pad)).filter(Boolean)
          : [],
        barracksSites: BARRACKS_SITE_DEFS
          .map((siteDef) => createBarracksSiteSnapshot(game, lane, siteDef.barracksId))
          .filter(Boolean),
        upcomingWave: createLaneUpcomingWavePreview(game, lane),
        upcomingWaveQueue: createLaneUpcomingWaveQueue(game, lane, 5),
        barracksRoster,
        heroRoster: createHeroRosterSnapshot(game, lane),
        barracksSendTimerTicksRemaining: barracksTimerSnapshot.remaining,
        barracksSendTimerTotalTicks: barracksTimerSnapshot.total,
        path: lane.path || [],
        fullPathLength: snapFullPath.length,
        units: lane.units.map(mapSnapshotUnit),
        spawnQueueUnits: lane.spawnQueue.map(mapSnapshotUnit),
        spawnQueueLength: lane.spawnQueue.length,
        projectiles,
      };
    }),
  };
}

function createMLPublicConfig(options, deps) {
  const {
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
    normalizeAllegianceKey,
    FRONT_GATE_COMBAT_OFFSET,
  } = deps;
  const opt = normalizeGameOptions(options, getMlRuntimeSettings);
  const allUnitTypes = typeof getAllUnitTypes === "function" ? getAllUnitTypes() : [];

  function createWorldPoint(x, y) {
    return {
      x: Number.isFinite(Number(x)) ? Number(x) : 0,
      y: Number.isFinite(Number(y)) ? Number(y) : 0,
    };
  }

  function buildBattlefieldLayout() {
    const slotDefinitions = Array.isArray(opt.battlefieldTopology?.slotDefinitions)
      ? opt.battlefieldTopology.slotDefinitions
      : [];
    const laneCount = slotDefinitions.length;
    const routeNodes = [];
    const routeSegments = [];
    const lanes = [];
    const liveLaneKeys = new Set();
    const slotByLaneIndex = new Map();

    for (let laneIndex = 0; laneIndex < slotDefinitions.length; laneIndex += 1) {
      const slot = slotDefinitions[laneIndex];
      if (!slot)
        continue;

      const slotColor = normalizeAllegianceKey(slot.slotColor || slot.team || slot.side) || slot.slotColor || slot.team || `lane_${laneIndex}`;
      liveLaneKeys.add(slotColor);
      slotByLaneIndex.set(laneIndex, slot);
    }

    const mineWorld = getWaveSpawnWorldPosition(0) || createWorldPoint(0, 0);
    routeNodes.push({
      nodeId: "M",
      laneIndex: -1,
      laneKey: "mine_center",
      world: createWorldPoint(mineWorld.x, mineWorld.y),
    });

    for (let laneIndex = 0; laneIndex < laneCount; laneIndex += 1) {
      const slot = slotByLaneIndex.get(laneIndex);
      if (!slot)
        continue;

      const laneKey = normalizeAllegianceKey(slot.slotColor || slot.team || slot.side) || slot.slotColor || slot.team || `lane_${laneIndex}`;
      const coreNodeId = getLaneNodeId(laneIndex);
      const waveNodeId = getWaveSpawnNodeId(laneIndex);
      const coreWorld = ROUTE_GRAPH_NODE_POSITIONS[coreNodeId] || createWorldPoint(0, 0);
      const waveWorld = getWaveSpawnWorldPosition(laneIndex) || mineWorld;
      const axes = typeof getLaneCombatAxes === "function"
        ? getLaneCombatAxes(laneIndex)
        : null;
      const frontGateWorld = axes
        ? createWorldPoint(
            Number(axes.core.x) + (Number(axes.forward.x) * Number(FRONT_GATE_COMBAT_OFFSET || 0)),
            Number(axes.core.y) + (Number(axes.forward.y) * Number(FRONT_GATE_COMBAT_OFFSET || 0)))
        : createWorldPoint(coreWorld.x, coreWorld.y);

      routeNodes.push({
        nodeId: coreNodeId,
        laneIndex,
        laneKey,
        world: createWorldPoint(coreWorld.x, coreWorld.y),
      });
      routeNodes.push({
        nodeId: waveNodeId,
        laneIndex,
        laneKey,
        world: createWorldPoint(waveWorld.x, waveWorld.y),
      });

      const fortressPads = FORTRESS_PAD_DEFS.map((pad) => {
        const world = getPadWorldPosition(laneIndex, pad.gridX, pad.gridY) || createWorldPoint(0, 0);
        const combatWorld = Number.isFinite(Number(pad.combatOffsetX)) && Number.isFinite(Number(pad.combatOffsetY)) && axes
          ? createWorldPoint(
              Number(axes.core.x) + (Number(axes.lateral.x) * Number(pad.combatOffsetX)) + (Number(axes.forward.x) * Number(pad.combatOffsetY)),
              Number(axes.core.y) + (Number(axes.lateral.y) * Number(pad.combatOffsetX)) + (Number(axes.forward.y) * Number(pad.combatOffsetY)))
          : createWorldPoint(world.x, world.y);

        return {
          padId: pad.padId,
          buildingType: pad.buildingType,
          displayName: pad.displayName,
          gridX: pad.gridX,
          gridY: pad.gridY,
          world: createWorldPoint(world.x, world.y),
          combatWorld,
        };
      });

      const barracksSites = BARRACKS_SITE_DEFS.map((siteDef) => {
        const world = getBarracksSiteWorldPosition(laneIndex, siteDef.barracksId) || createWorldPoint(0, 0);
        const routeNodeId = getBarracksRouteStartNodeId(laneIndex, siteDef.barracksId);
        routeNodes.push({
          nodeId: routeNodeId,
          laneIndex,
          laneKey,
          world: createWorldPoint(world.x, world.y),
        });

        return {
          barracksId: siteDef.barracksId,
          displayName: siteDef.displayName,
          slot: siteDef.slot,
          sortIndex: siteDef.sortIndex,
          world: createWorldPoint(world.x, world.y),
          routeNodeId,
        };
      });

      const townCorePad = fortressPads.find((entry) => entry && entry.padId === "town_core_pad") || null;

      lanes.push({
        laneIndex,
        laneKey,
        slotColor: slot.slotColor,
        slotKey: slot.slotKey,
        branchId: slot.branchId,
        townCore: townCorePad ? createWorldPoint(townCorePad.world.x, townCorePad.world.y) : createWorldPoint(coreWorld.x, coreWorld.y),
        frontGate: frontGateWorld,
        waveSpawn: createWorldPoint(waveWorld.x, waveWorld.y),
        fortressPads,
        barracksSites,
      });
    }

    for (const [segmentId, points] of Object.entries(ROUTE_SEGMENT_POLYLINES || {})) {
      if (!Array.isArray(points) || points.length < 2)
        continue;

      const splitIndex = segmentId.indexOf("_");
      if (splitIndex <= 0 || splitIndex >= segmentId.length - 1)
        continue;

      const fromNodeId = segmentId.slice(0, splitIndex);
      const toNodeId = segmentId.slice(splitIndex + 1);
      const fromLaneKey = normalizeAllegianceKey((slotByLaneIndex.get(routeNodes.find((entry) => entry && entry.nodeId === fromNodeId)?.laneIndex)?.slotColor) || null);
      const toLaneKey = normalizeAllegianceKey((slotByLaneIndex.get(routeNodes.find((entry) => entry && entry.nodeId === toNodeId)?.laneIndex)?.slotColor) || null);

      routeSegments.push({
        segmentId,
        fromNodeId,
        toNodeId,
        laneKey: fromLaneKey || toLaneKey || null,
        points: points.map((point) => createWorldPoint(point.x, point.y)),
      });
    }

    const canonicalLayout = {
      mapType: opt.battlefieldTopology?.mapType || "unknown",
      playerCount: laneCount,
      lanes,
      routeNodes,
      routeSegments,
    };

    const contentHash = crypto
      .createHash("sha256")
      .update(JSON.stringify(canonicalLayout))
      .digest("hex");

    return {
      layoutId: `${canonicalLayout.mapType}:server_v1`,
      mapType: canonicalLayout.mapType,
      playerCount: canonicalLayout.playerCount,
      contentHash,
      lanes: canonicalLayout.lanes,
      routeNodes: canonicalLayout.routeNodes,
      routeSegments: canonicalLayout.routeSegments,
    };
  }

  function pickSoundFields(ut) {
    return {
      sound_spawn: ut.sound_spawn || null,
      sound_attack: ut.sound_attack || null,
      sound_hit: ut.sound_hit || null,
      sound_death: ut.sound_death || null,
    };
  }

  const fixedUnitTypes = allUnitTypes
    .filter((ut) => ut.enabled !== false && Number(ut.build_cost) > 0)
    .map((ut) => ({
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
    .filter((ut) => ut.enabled !== false && Number(ut.send_cost) > 0)
    .map((ut) => ({
      key: ut.key,
      name: ut.name,
      send_cost: Number(ut.send_cost) || 1,
      income: Number(ut.income) || 0,
      ...pickSoundFields(ut),
    }));

  const fortressBuildingConfigs = Object.entries(FORTRESS_BUILDING_DEFS).map(([buildingType, def]) => ({
    buildingType,
    displayName: def.displayName,
    branchKey: def.branchKey || buildingType,
    branchLabel: getBuildingBranchLabel(buildingType),
    progressionCategory: def.progressionCategory || null,
    maxTier: getFortressMaxTier(buildingType),
    tierDisplayNames: Array.isArray(def.tierDisplayNames) ? def.tierDisplayNames.map((entry) => entry || "") : [],
    startsBuilt: !!def.startsBuilt,
    requiredTownCoreTier: Math.max(1, Math.floor(Number(def.requiredTownCoreTier) || 1)),
    requiresLumberMill: !!def.requiresLumberMill,
    requiresTurretTier3: !!def.requiresTurretTier3,
    baseMaxHp: Math.max(1, Math.floor(Number(def.baseMaxHp) || 1)),
    buildCost: getFortressBuildCost(buildingType),
  }));

  const fortressPadConfigs = FORTRESS_PAD_DEFS.map((pad) => ({
    padId: pad.padId,
    buildingType: pad.buildingType,
    displayName: pad.displayName,
    branchKey: (FORTRESS_BUILDING_DEFS[pad.buildingType] || {}).branchKey || pad.buildingType,
    branchLabel: getBuildingBranchLabel(pad.buildingType),
    gridX: pad.gridX,
    gridY: pad.gridY,
  }));

  const barracksRosterConfigs = BARRACKS_ROSTER_DEFS.map((rosterDef) => {
    const presentation = typeof resolveFortPresentationConfig === "function"
      ? resolveFortPresentationConfig(rosterDef.archetypeKey, rosterDef.presentationKey, rosterDef.displayName)
      : null;
    return {
    rosterKey: rosterDef.rosterKey,
    displayName: rosterDef.displayName,
    role: rosterDef.role,
    roleLabel: rosterDef.role.charAt(0).toUpperCase() + rosterDef.role.slice(1),
    sortIndex: rosterDef.sortIndex || 0,
    archetypeKey: rosterDef.archetypeKey || null,
    presentationKey: presentation ? presentation.presentationKey : (rosterDef.presentationKey || null),
    unitTypeKey: presentation ? presentation.catalogUnitKey : (rosterDef.unitTypeKey || rosterDef.catalogUnitKey || null),
    catalogUnitKey: presentation ? presentation.catalogUnitKey : (rosterDef.catalogUnitKey || rosterDef.unitTypeKey || null),
    skinKey: presentation ? presentation.skinKey : (rosterDef.skinKey || null),
    portraitKey: presentation ? presentation.portraitKey : (rosterDef.portraitKey || null),
    branchKey: rosterDef.branchKey || "",
    branchLabel: rosterDef.branchLabel || getBuildingBranchLabel(rosterDef.productionBuildingType),
    productionBuildingType: rosterDef.productionBuildingType,
    productionBuildingName: getBuildingDisplayName(rosterDef.productionBuildingType),
    tier: Math.max(1, Math.floor(Number(rosterDef.tier) || 1)),
    buyCost: getBarracksRosterBuyCost(rosterDef),
    sellRefund: getBarracksRosterSellRefund(rosterDef),
    unlockBuildingType: rosterDef.unlockBuildingType,
    unlockBuildingName: (FORTRESS_BUILDING_DEFS[rosterDef.unlockBuildingType] || {}).displayName || rosterDef.unlockBuildingType,
    unlockBuildingTierName: getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier),
    requiredBuildingTier: rosterDef.requiredBuildingTier,
    lockedReason: getBarracksRosterLockedReason(rosterDef),
    };
  });

  const heroRosterConfigs = HERO_ROSTER_DEFS.map((heroDef) => {
    const presentation = typeof resolveFortPresentationConfig === "function"
      ? resolveFortPresentationConfig(heroDef.archetypeKey, heroDef.presentationKey, heroDef.displayName)
      : null;
    return {
    heroKey: heroDef.heroKey,
    displayName: heroDef.displayName,
    role: heroDef.role,
    roleLabel: heroDef.roleLabel || "Hero",
    sortIndex: heroDef.sortIndex || 0,
    archetypeKey: heroDef.archetypeKey || null,
    presentationKey: presentation ? presentation.presentationKey : (heroDef.presentationKey || null),
    unitTypeKey: presentation ? presentation.catalogUnitKey : (heroDef.unitTypeKey || heroDef.catalogUnitKey || null),
    catalogUnitKey: presentation ? presentation.catalogUnitKey : (heroDef.catalogUnitKey || heroDef.unitTypeKey || null),
    skinKey: presentation ? presentation.skinKey : (heroDef.skinKey || null),
    portraitKey: presentation ? presentation.portraitKey : (heroDef.portraitKey || null),
    branchKey: heroDef.branchKey || "heroes",
    branchLabel: heroDef.branchLabel || "Heroes",
    unlockBuildingType: heroDef.unlockBuildingType,
    unlockBuildingName: getBuildingDisplayName(heroDef.unlockBuildingType),
    unlockBuildingTierName: getBuildingTierDisplayName(heroDef.unlockBuildingType, heroDef.requiredBuildingTier),
    requiredBuildingTier: Math.max(1, Math.floor(Number(heroDef.requiredBuildingTier) || 1)),
    summonSourceBuildingType: heroDef.summonSourceBuildingType,
    summonSourceBuildingName: getBuildingDisplayName(heroDef.summonSourceBuildingType),
    summonCost: Math.max(0, Math.floor(Number(heroDef.summonCost) || 0)),
    cooldownTicks: Math.max(0, Math.floor(Number(heroDef.cooldownTicks) || 0)),
    activeLimit: Math.max(0, Math.floor(Number(heroDef.activeLimit) || 0)),
    heroVisualStyleKey: heroDef.heroVisualStyleKey || null,
    lockedReason: getHeroRosterLockedReason(heroDef),
    };
  });

  const marketRosterConfigs = MARKET_ROSTER_DEFS.map((unitDef) => {
    const presentation = typeof resolveFortPresentationConfig === "function"
      ? resolveFortPresentationConfig(unitDef.archetypeKey, unitDef.presentationKey, unitDef.displayName)
      : null;
    return {
    unitKey: unitDef.unitKey,
    displayName: unitDef.displayName,
    role: unitDef.role,
    roleLabel: unitDef.roleLabel || "Economy",
    sortIndex: unitDef.sortIndex || 0,
    archetypeKey: unitDef.archetypeKey || null,
    presentationKey: presentation ? presentation.presentationKey : (unitDef.presentationKey || null),
    skinKey: presentation ? presentation.skinKey : (unitDef.skinKey || null),
    portraitKey: presentation ? presentation.portraitKey : (unitDef.portraitKey || unitDef.skinKey || null),
    branchKey: unitDef.branchKey || "market",
    branchLabel: unitDef.branchLabel || "Market",
    productionBuildingType: unitDef.productionBuildingType || "market",
    productionBuildingName: getBuildingDisplayName(unitDef.productionBuildingType || "market"),
    tier: Math.max(1, Math.floor(Number(unitDef.tier) || 1)),
    unlockBuildingType: unitDef.unlockBuildingType || "market",
    unlockBuildingName: getBuildingDisplayName(unitDef.unlockBuildingType || "market"),
    unlockBuildingTierName: getBuildingTierDisplayName(unitDef.unlockBuildingType || "market", unitDef.requiredBuildingTier),
    requiredBuildingTier: Math.max(1, Math.floor(Number(unitDef.requiredBuildingTier) || 1)),
    economyLapGold: Math.max(0, Math.floor(Number(unitDef.economyLapGold) || 0)),
    routeStartBuildingType: unitDef.routeStartBuildingType || "town_core",
    routeStartBuildingName: getBuildingDisplayName(unitDef.routeStartBuildingType || "town_core"),
    routeEndBuildingType: unitDef.routeEndBuildingType || "market",
    routeEndBuildingName: getBuildingDisplayName(unitDef.routeEndBuildingType || "market"),
    nextUnitKey: unitDef.nextUnitKey || null,
    description: unitDef.description || "",
    };
  });

  const barracksSiteConfigs = BARRACKS_SITE_DEFS.map((siteDef) => ({
    barracksId: siteDef.barracksId,
    displayName: siteDef.displayName,
    slot: siteDef.slot,
    sortIndex: siteDef.sortIndex,
    requiredBarracksTier: getBarracksSiteTierRequirement(siteDef),
    startsBuilt: !!siteDef.startsBuilt,
    buildCost: getBarracksSiteBuildCost(siteDef),
    maxLevel: getBarracksSiteMaxLevel(),
  }));

  const battlefieldLayout = buildBattlefieldLayout();

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
    teamHpStart: opt.teamHpStart,
    buildPhaseTicks: 0,
    transitionPhaseTicks: 0,
    escalationPerExtraRound: ESCALATION_PER_EXTRA_ROUND,
    battlefieldTopology: opt.battlefieldTopology,
    battlefieldLayout,
    slotDefinitions: opt.battlefieldTopology.slotDefinitions,
    fortressBuildingConfigs,
    fortressPadConfigs,
    barracksSiteConfigs,
    barracksRosterConfigs,
    heroRosterConfigs,
    marketRosterConfigs,
    barracksRosterRefundPct: BARRACKS_ROSTER_REFUND_PCT,
    barracksSendTimerTicks: BARRACKS_SEND_TIMER_TICKS,
    waveTimerTicks: WAVE_TIMER_TICKS,
    movementTuning: {
      baseCombatPathSpeed: BASE_COMBAT_PATH_SPEED,
      barracksLevelOneSpeedMultiplier: BARRACKS_LEVEL_ONE_SPEED_MULT,
      barracksSpeedUpgradeStep: SPEED_UPGRADE_STEP,
      waveSpeedUpgradeStep: SPEED_UPGRADE_STEP,
      serverPathSpeedToUnityMoveSpeedScale: 20,
    },
    fixedUnitTypes,
    movingUnitTypes,
  };
}

module.exports = {
  createMLSnapshot,
  createMLPublicConfig,
};
