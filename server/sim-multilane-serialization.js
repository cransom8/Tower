"use strict";

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
  const { GRID_W, GRID_H } = deps;
  let total = 0;

  if (lane && Array.isArray(lane.grid)) {
    for (let x = 0; x < GRID_W; x++) {
      for (let y = 0; y < GRID_H; y++) {
        const tile = lane.grid[x] && lane.grid[x][y];
        if (!tile || (tile.type !== "tower" && tile.type !== "dead_tower") || !Array.isArray(tile.costHistory))
          continue;

        for (const entry of tile.costHistory)
          total += Number(entry && entry.cost) || 0;
      }
    }
  }

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
          blockedByStructure: !!u.blockedByStructure,
          blockedByStructureId: u.blockedByStructureId || null,
          routeWorldX: Number.isFinite(Number(u.routeWorldX)) ? Number(u.routeWorldX) : gx,
          routeWorldY: Number.isFinite(Number(u.routeWorldY)) ? Number(u.routeWorldY) : gy,
          currentSlotIndex: Number.isInteger(u.currentSlotIndex) ? u.currentSlotIndex : -1,
          anchorTargetX: Number.isFinite(Number(u.anchorTargetX)) ? Number(u.anchorTargetX) : null,
          anchorTargetY: Number.isFinite(Number(u.anchorTargetY)) ? Number(u.anchorTargetY) : null,
          anchorTargetProgress: Number.isFinite(Number(u.anchorTargetProgress)) ? Number(u.anchorTargetProgress) : null,
          combatLeashRadius: Number.isFinite(Number(u.combatLeashRadius)) ? Number(u.combatLeashRadius) : null,
          canEngage: !!u.canEngage,
          hp: u.hp,
          maxHp: u.maxHp,
          moveSpeed: Number(u.baseSpeed) || 0,
          isWaveUnit: !!(typeof isScheduledWaveUnit === "function" ? isScheduledWaveUnit(u) : u.isWaveUnit),
          isDefender: canonicalStance === "DEFEND" || !!u.isDefender,
          isAttacking: !!(u.combatTarget && u.combatTarget.unitId),
          combatTargetId: u.combatTargetId || (u.combatTarget && u.combatTarget.unitId ? u.combatTarget.unitId : null),
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
  } = deps;
  const opt = normalizeGameOptions(options, getMlRuntimeSettings);
  const allUnitTypes = typeof getAllUnitTypes === "function" ? getAllUnitTypes() : [];

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
