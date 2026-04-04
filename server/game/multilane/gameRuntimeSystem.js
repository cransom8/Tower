"use strict";

const DEFAULT_GRID_WIDTH = 11;
const DEFAULT_GRID_HEIGHT = 28;
const DEFAULT_SPAWN_X = 5;
const DEFAULT_SPAWN_Y = 0;
const DEFAULT_CASTLE_X = 5;
const DEFAULT_CASTLE_Y = 27;
const DEFAULT_TEAM_HP_START = 20;
const DEFAULT_BUILD_PHASE_TICKS = 600;
const DEFAULT_TRANSITION_PHASE_TICKS = 200;
const DEFAULT_INCOME_INTERVAL_TICKS = 240;
const DEFAULT_WAVE_TIMER_TICKS = 2400;
const DEFAULT_WAVE_GROUP_INTERVAL_TICKS = 600;
const DEFAULT_INITIAL_WAVE_DELAY_TICKS = 600;
const DEFAULT_BARRACKS_SEND_TIMER_TICKS = DEFAULT_WAVE_GROUP_INTERVAL_TICKS;
const DEFAULT_LANE_COMMAND_COMBAT_LEASH = 8.0;

const DEFAULT_ALLEGIANCE_KEYS = Object.freeze({
  RED: "red",
  YELLOW: "yellow",
  BLUE: "blue",
  GREEN: "green",
});

const DEFAULT_LANE_COMMAND_STATES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  RETREAT: "RETREAT",
});

const DEFAULT_ROUTE_TYPES = Object.freeze({
  WAVE_LANE: "WAVE_LANE",
});

const DEFAULT_BUILDING_LIFECYCLE_STATES = Object.freeze({
  beingBuilt: "being_built",
  active: "active",
  destroyed: "destroyed",
  underRepair: "under_repair",
});

const DEFAULT_FIXED_SLOT_LAYOUT = Object.freeze([
  Object.freeze({ laneIndex: 0, slotKey: "left_a", side: "left", slotColor: "red", branchId: "left_branch_a", branchLabel: "Red Branch", castleSide: "right" }),
  Object.freeze({ laneIndex: 1, slotKey: "left_b", side: "left", slotColor: "yellow", branchId: "left_branch_b", branchLabel: "Yellow Branch", castleSide: "right" }),
  Object.freeze({ laneIndex: 2, slotKey: "right_a", side: "right", slotColor: "blue", branchId: "right_branch_a", branchLabel: "Blue Branch", castleSide: "left" }),
  Object.freeze({ laneIndex: 3, slotKey: "right_b", side: "right", slotColor: "green", branchId: "right_branch_b", branchLabel: "Green Branch", castleSide: "left" }),
]);

const DEFAULT_BATTLEFIELD_TOPOLOGY = Object.freeze({
  mapType: "lava_lake_funnel",
  centerIslandId: "center_spawn_island",
  sideOrder: Object.freeze(["left", "right"]),
  castles: Object.freeze([
    Object.freeze({ side: "left", castleId: "left_castle", bridgeId: "left_castle_bridge" }),
    Object.freeze({ side: "right", castleId: "right_castle", bridgeId: "right_castle_bridge" }),
  ]),
  mergeZones: Object.freeze([
    Object.freeze({ side: "left", landmassId: "left_merge_landmass", bridgeId: "left_castle_bridge" }),
    Object.freeze({ side: "right", landmassId: "right_merge_landmass", bridgeId: "right_castle_bridge" }),
  ]),
  buildZones: Object.freeze([
    Object.freeze({ branchId: "left_branch_a", ownerLaneIndex: 0, buildable: true }),
    Object.freeze({ branchId: "left_branch_b", ownerLaneIndex: 1, buildable: true }),
    Object.freeze({ branchId: "right_branch_a", ownerLaneIndex: 2, buildable: true }),
    Object.freeze({ branchId: "right_branch_b", ownerLaneIndex: 3, buildable: true }),
  ]),
  sharedZonesBuildable: false,
});

const DEFAULT_OPPOSING_LANE_INDEX = Object.freeze([1, 0, 3, 2]);
const DEFAULT_LEGACY_ACTION_REJECTION_REASONS = Object.freeze({});
const DEFAULT_BARRACKS_SITE_DEFS = Object.freeze([]);
const DEFAULT_BARRACKS_ROSTER_DEFS = Object.freeze([]);

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`gameRuntimeSystem requires deps.${name}`);
  return fn;
}

function getGridWidth(deps = {}) {
  const value = Math.floor(Number(deps.GRID_W));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_GRID_WIDTH;
}

function getGridHeight(deps = {}) {
  const value = Math.floor(Number(deps.GRID_H));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_GRID_HEIGHT;
}

function getSpawnX(deps = {}) {
  const value = Math.floor(Number(deps.SPAWN_X));
  return Number.isInteger(value) ? value : DEFAULT_SPAWN_X;
}

function getSpawnY(deps = {}) {
  const value = Math.floor(Number(deps.SPAWN_YG));
  return Number.isInteger(value) ? value : DEFAULT_SPAWN_Y;
}

function getCastleX(deps = {}) {
  const value = Math.floor(Number(deps.CASTLE_X));
  return Number.isInteger(value) ? value : DEFAULT_CASTLE_X;
}

function getCastleY(deps = {}) {
  const value = Math.floor(Number(deps.CASTLE_YG));
  return Number.isInteger(value) ? value : DEFAULT_CASTLE_Y;
}

function getTeamHpStart(deps = {}) {
  const value = Math.floor(Number(deps.TEAM_HP_START));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_TEAM_HP_START;
}

function getBuildPhaseTicks(deps = {}) {
  const value = Math.floor(Number(deps.BUILD_PHASE_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_BUILD_PHASE_TICKS;
}

function getTransitionPhaseTicks(deps = {}) {
  const value = Math.floor(Number(deps.TRANSITION_PHASE_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_TRANSITION_PHASE_TICKS;
}

function getIncomeIntervalTicks(deps = {}) {
  const value = Math.floor(Number(deps.INCOME_INTERVAL_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_INCOME_INTERVAL_TICKS;
}

function getBarracksSendTimerTicks(deps = {}) {
  const value = Math.floor(Number(deps.BARRACKS_SEND_TIMER_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_BARRACKS_SEND_TIMER_TICKS;
}

function getWaveTimerTicks(deps = {}) {
  const value = Math.floor(Number(deps.WAVE_TIMER_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_WAVE_TIMER_TICKS;
}

function getWaveGroupIntervalTicks(deps = {}) {
  const value = Math.floor(Number(deps.WAVE_GROUP_INTERVAL_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_WAVE_GROUP_INTERVAL_TICKS;
}

function getInitialWaveDelayTicks(deps = {}) {
  const value = Math.floor(Number(deps.INITIAL_WAVE_DELAY_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_INITIAL_WAVE_DELAY_TICKS;
}

function getLaneCommandCombatLeash(deps = {}) {
  const value = Number(deps.LANE_COMMAND_COMBAT_LEASH);
  return Number.isFinite(value) && value > 0 ? value : DEFAULT_LANE_COMMAND_COMBAT_LEASH;
}

function getAllegianceKeys(deps = {}) {
  return deps.ALLEGIANCE_KEYS || DEFAULT_ALLEGIANCE_KEYS;
}

function getLaneCommandStates(deps = {}) {
  return deps.LANE_COMMAND_STATES || DEFAULT_LANE_COMMAND_STATES;
}

function getRouteTypes(deps = {}) {
  return deps.ROUTE_TYPES || DEFAULT_ROUTE_TYPES;
}

function getBuildingLifecycleStates(deps = {}) {
  return deps.BUILDING_LIFECYCLE_STATES || DEFAULT_BUILDING_LIFECYCLE_STATES;
}

function getFixedSlotLayout(deps = {}) {
  return Array.isArray(deps.FIXED_SLOT_LAYOUT) ? deps.FIXED_SLOT_LAYOUT : DEFAULT_FIXED_SLOT_LAYOUT;
}

function getBattlefieldTopologyTemplate(deps = {}) {
  return deps.BATTLEFIELD_TOPOLOGY || DEFAULT_BATTLEFIELD_TOPOLOGY;
}

function getOpposingLaneIndex(deps = {}) {
  return Array.isArray(deps.OPPOSING_LANE_INDEX) ? deps.OPPOSING_LANE_INDEX : DEFAULT_OPPOSING_LANE_INDEX;
}

function getLegacyActionRejectionReasons(deps = {}) {
  return deps.LEGACY_ACTION_REJECTION_REASONS || DEFAULT_LEGACY_ACTION_REJECTION_REASONS;
}

function getBarracksSiteDefs(deps = {}) {
  return Array.isArray(deps.BARRACKS_SITE_DEFS) ? deps.BARRACKS_SITE_DEFS : DEFAULT_BARRACKS_SITE_DEFS;
}

function getBarracksRosterDefs(deps = {}) {
  return Array.isArray(deps.BARRACKS_ROSTER_DEFS) ? deps.BARRACKS_ROSTER_DEFS : DEFAULT_BARRACKS_ROSTER_DEFS;
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

  const numericValue = Number(src[key]);
  if (!Number.isFinite(numericValue))
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected a finite number.`);
  if (numericValue < min || numericValue > max)
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected ${min}-${max}.`);
  if (integer && !Number.isInteger(numericValue))
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected a whole number.`);
  return Math.max(min, Math.min(max, numericValue));
}

function cloneSlotDef(slot, laneTeam, deps = {}) {
  const normalizeAllegianceKey = requireDepFunction(deps, "normalizeAllegianceKey");
  const allegianceKeys = getAllegianceKeys(deps);
  const canonicalLaneTeam = normalizeAllegianceKey(
    laneTeam || slot.team || slot.slotColor || (slot.side === "left" ? allegianceKeys.RED : allegianceKeys.BLUE)
  );
  const canonicalSlotColor = normalizeAllegianceKey(slot.slotColor) || canonicalLaneTeam;
  return {
    laneIndex: slot.laneIndex,
    slotKey: slot.slotKey,
    side: slot.side,
    slotColor: canonicalSlotColor || slot.slotColor,
    branchId: slot.branchId,
    branchLabel: slot.branchLabel,
    castleSide: slot.castleSide,
    team: canonicalLaneTeam || allegianceKeys.RED,
    allegianceKey: canonicalLaneTeam || allegianceKeys.RED,
  };
}

function getTwoPlayerSlotBases(deps = {}) {
  const fixedSlotLayout = getFixedSlotLayout(deps);
  return [
    fixedSlotLayout[0],
    fixedSlotLayout[1]
      ? Object.assign({}, fixedSlotLayout[1], {
          laneIndex: 1,
          side: "right",
          castleSide: "left",
        })
      : null,
  ].filter(Boolean);
}

function getDefaultSlotDefinitions(playerCount, laneTeams, deps = {}) {
  const fixedSlotLayout = getFixedSlotLayout(deps);
  const twoPlayerSlotBases = getTwoPlayerSlotBases(deps);
  const safePlayerCount = Math.max(0, Math.floor(Number(playerCount) || 0));
  const defs = [];
  for (let i = 0; i < safePlayerCount; i += 1) {
    const base = safePlayerCount === 2
      ? (twoPlayerSlotBases[i] || fixedSlotLayout[i])
      : (fixedSlotLayout[i] || {
          laneIndex: i,
          slotKey: `slot_${i}`,
          side: i % 2 === 0 ? "left" : "right",
          slotColor: `slot-${i}`,
          branchId: `branch_${i}`,
          branchLabel: `Branch ${i + 1}`,
          castleSide: i % 2 === 0 ? "right" : "left",
        });
    defs.push(cloneSlotDef(base, laneTeams && laneTeams[i], deps));
  }
  return defs;
}

function getBattlefieldTopology(playerCount, laneTeams, deps = {}) {
  const topology = getBattlefieldTopologyTemplate(deps);
  const safePlayerCount = Math.max(0, Math.floor(Number(playerCount) || 0));
  return {
    mapType: topology.mapType,
    centerIslandId: topology.centerIslandId,
    sideOrder: Array.isArray(topology.sideOrder) ? topology.sideOrder.slice() : [],
    castles: Array.isArray(topology.castles)
      ? topology.castles.map((castle) => Object.assign({}, castle))
      : [],
    mergeZones: Array.isArray(topology.mergeZones)
      ? topology.mergeZones.map((zone) => Object.assign({}, zone))
      : [],
    buildZones: Array.isArray(topology.buildZones)
      ? topology.buildZones
        .filter((zone) => zone.ownerLaneIndex < safePlayerCount)
        .map((zone) => Object.assign({}, zone))
      : [],
    sharedZonesBuildable: !!topology.sharedZonesBuildable,
    slotDefinitions: getDefaultSlotDefinitions(safePlayerCount, laneTeams, deps),
  };
}

function normalizeGameOptions(options, deps = {}) {
  const getMlRuntimeSettings = requireDepFunction(deps, "getMlRuntimeSettings");
  const normalizeAllegianceKey = requireDepFunction(deps, "normalizeAllegianceKey");
  const runtime = getMlRuntimeSettings();
  const src = options && typeof options === "object" ? options : {};
  const allegianceKeys = getAllegianceKeys(deps);
  const defaultLaneTeams = [
    allegianceKeys.RED,
    allegianceKeys.YELLOW,
    allegianceKeys.BLUE,
    allegianceKeys.GREEN,
  ];
  const laneTeamsRaw = Array.isArray(src.laneTeams) ? src.laneTeams : [];
  const laneTeams = laneTeamsRaw.map((team, idx) => {
    const normalized = normalizeAllegianceKey(team);
    if (normalized)
      return normalized;
    return defaultLaneTeams[idx] || `p${idx}`;
  });
  const topologyPlayerCount = Math.max(
    0,
    Math.floor(Number(src.playerCount) || laneTeams.length || getFixedSlotLayout(deps).length || 4)
  );
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
    battlefieldTopology: getBattlefieldTopology(topologyPlayerCount, laneTeams, deps),
  };
}

function makeGrid(deps = {}) {
  const gridWidth = getGridWidth(deps);
  const gridHeight = getGridHeight(deps);
  const spawnX = getSpawnX(deps);
  const spawnY = getSpawnY(deps);
  const castleX = getCastleX(deps);
  const castleY = getCastleY(deps);
  const grid = [];
  for (let x = 0; x < gridWidth; x += 1) {
    grid[x] = [];
    for (let y = 0; y < gridHeight; y += 1)
      grid[x][y] = { type: "empty", towerType: null, towerLevel: 0, atkCd: 0, targetMode: "first" };
  }
  if (grid[spawnX] && grid[spawnX][spawnY])
    grid[spawnX][spawnY].type = "spawn";
  if (grid[castleX] && grid[castleX][castleY])
    grid[castleX][castleY].type = "castle";
  return grid;
}

function createMLGame(playerCount, options, deps = {}) {
  const buildRouteSegments = requireDepFunction(deps, "buildRouteSegments");
  const getWaveSpawnNodeId = requireDepFunction(deps, "getWaveSpawnNodeId");
  const getLaneNodeId = requireDepFunction(deps, "getLaneNodeId");
  const buildSampledPathFromSegments = requireDepFunction(deps, "buildSampledPathFromSegments");
  const getBarracksLevelDef = requireDepFunction(deps, "getBarracksLevelDef");
  const createFortressPadStates = requireDepFunction(deps, "createFortressPadStates");
  const createBuildingUpgradeState = requireDepFunction(deps, "createBuildingUpgradeState");
  const createBarracksSiteStates = requireDepFunction(deps, "createBarracksSiteStates");
  const createBarracksSiteRosterCounts = requireDepFunction(deps, "createBarracksSiteRosterCounts");
  const createMarketRosterCounts = requireDepFunction(deps, "createMarketRosterCounts");
  const seedStartingCombatTestMilitia = requireDepFunction(deps, "seedStartingCombatTestMilitia");
  const recomputeTeamHpState = requireDepFunction(deps, "recomputeTeamHpState");
  const mulberry32 = requireDepFunction(deps, "mulberry32");
  const normalizeAllegianceKey = requireDepFunction(deps, "normalizeAllegianceKey");
  const ensureBalanceTelemetry = typeof deps.ensureBalanceTelemetry === "function"
    ? deps.ensureBalanceTelemetry
    : null;
  const safePlayerCount = Math.max(0, Math.floor(Number(playerCount) || 0));
  const opt = normalizeGameOptions(options, deps);
  const battlefieldTopology = getBattlefieldTopology(safePlayerCount, opt.laneTeams, deps);
  const slotDefinitions = battlefieldTopology.slotDefinitions;
  const routeTypes = getRouteTypes(deps);
  const laneCommandStates = getLaneCommandStates(deps);
  const opposingLaneIndex = getOpposingLaneIndex(deps);
  const lanes = [];

  for (let i = 0; i < safePlayerCount; i += 1) {
    const grid = makeGrid(deps);
    const routeToCore = buildRouteSegments(routeTypes.WAVE_LANE, getWaveSpawnNodeId(i), getLaneNodeId(i));
    const slot = slotDefinitions[i] || cloneSlotDef({
      laneIndex: i,
      slotKey: `slot_${i}`,
      side: i % 2 === 0 ? "left" : "right",
      slotColor: `slot-${i}`,
      branchId: `branch_${i}`,
      branchLabel: `Branch ${i + 1}`,
      castleSide: i % 2 === 0 ? "right" : "left",
    }, opt.laneTeams[i], deps);
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
      path: buildSampledPathFromSegments(routeToCore, 28),
      fullPath: buildSampledPathFromSegments(routeToCore, 56),
      barracks: Object.assign({ level: 1 }, getBarracksLevelDef(1)),
      waveSpeedMult: 1,
      fortressPads: createFortressPadStates(opt.teamHpStart),
      buildingUpgradeState: createBuildingUpgradeState(),
      barracksSiteStates: createBarracksSiteStates(opt.teamHpStart, 1),
      barracksSiteRosterCounts: createBarracksSiteRosterCounts(),
      marketRosterCounts: createMarketRosterCounts(),
      heroCooldownReadyTicks: {},
      units: [],
      spawnQueue: [],
      projectiles: [],
      loadoutKeys: null,
      commandState: laneCommandStates.DEFEND,
      commandTargetLaneIndex: Number.isInteger(opposingLaneIndex[i]) && opposingLaneIndex[i] < safePlayerCount
        ? opposingLaneIndex[i]
        : (safePlayerCount > 1 ? ((i + 1) % safePlayerCount) : i),
      commandAnchorProgress: 0,
      commandAnchor: null,
      commandFacing: null,
      commandSlots: [],
      assignedUnits: [],
      assignedUnitOrder: [],
      insideGateAnchor: null,
      outsideGateAnchor: null,
      enemyCoreAnchor: null,
      engagementRadius: getLaneCommandCombatLeash(deps),
      combatEnabled: true,
    });
  }

  const incomeIntervalTicks = getIncomeIntervalTicks(deps);
  const barracksSendIntervalTicks = getBarracksSendTimerTicks(deps);
  const waveIntervalTicks = getWaveTimerTicks(deps);
  const waveGroupIntervalTicks = getWaveGroupIntervalTicks(deps);
  const initialWaveDelayTicks = getInitialWaveDelayTicks(deps);

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
    playerCount: safePlayerCount,
    lanes,
    battlefieldTopology,
    teamHp: { left: opt.teamHpStart, right: opt.teamHpStart },
    teamHpMax: opt.teamHpStart,
    buildPhaseTicks: opt.buildPhaseTicks,
    transitionPhaseTicks: opt.transitionPhaseTicks,
    incomeIntervalTicks,
    barracksSendIntervalTicks,
    waveIntervalTicks,
    waveGroupIntervalTicks,
    initialWaveDelayTicks,
    roundState: "combat",
    roundNumber: 1,
    roundStateTicks: 0,
    nextIncomeTick: incomeIntervalTicks,
    nextBarracksSendTick: barracksSendIntervalTicks,
    nextWaveTick: initialWaveDelayTicks,
    lastWaveSpawnTick: null,
    hasSpawnedWave: false,
    activeWaveSession: null,
    waveConfig: [],
    roundSnapshots: [],
    startedAt: null,
    finalSnapshotCaptured: false,
    _pendingEvents: [],
    nextUnitId: 1,
    nextProjectileId: 1,
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
  if (ensureBalanceTelemetry)
    ensureBalanceTelemetry(game, { mode: "multilane", tickHz: Math.max(1, Math.floor(Number(deps.TICK_HZ) || 20)) });
  return game;
}

function getLaneBuildValue(lane, deps = {}) {
  const getBarracksSiteCounts = requireDepFunction(deps, "getBarracksSiteCounts");
  const getBarracksRosterBuyCost = requireDepFunction(deps, "getBarracksRosterBuyCost");
  let total = 0;
  if (Array.isArray(lane && lane.fortressPads)) {
    for (const pad of lane.fortressPads || []) {
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
  if (lane && lane.buildingUpgradeState && typeof lane.buildingUpgradeState === "object") {
    for (const buildingBucket of Object.values(lane.buildingUpgradeState)) {
      if (!buildingBucket || typeof buildingBucket !== "object")
        continue;
      for (const upgradeState of Object.values(buildingBucket)) {
        if (!upgradeState || !Array.isArray(upgradeState.costHistory))
          continue;
        for (const entry of upgradeState.costHistory)
          total += Number(entry && entry.cost) || 0;
      }
    }
  }
  if (lane && lane.barracksSiteRosterCounts) {
    for (const siteDef of getBarracksSiteDefs(deps)) {
      const siteCounts = getBarracksSiteCounts(lane, siteDef.barracksId) || {};
      for (const rosterDef of getBarracksRosterDefs(deps)) {
        const ownedCount = Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
        total += ownedCount * getBarracksRosterBuyCost(rosterDef);
      }
    }
  }
  return total;
}

function getLaneWaveResult(lane) {
  if (!lane)
    return "Unknown";
  if (lane.eliminated && lane.lifeLossThisRound > 0)
    return "Defeated";
  if (lane.leakCountThisRound >= 5 || lane.lifeLossThisRound >= 5)
    return "Crushed";
  if (lane.leakCountThisRound > 0 || lane.lifeLossThisRound > 0)
    return "Leaked";
  return "Held";
}

function createRoundSnapshotLane(game, lane, deps = {}) {
  return {
    laneIndex: lane.laneIndex,
    income: lane.income,
    buildValue: getLaneBuildValue(lane, deps),
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

function isOpponentLane(game, sourceLaneIndex, targetLaneIndex, deps = {}) {
  const resolveLaneAllegianceKey = requireDepFunction(deps, "resolveLaneAllegianceKey");
  const areAllegiancesHostile = requireDepFunction(deps, "areAllegiancesHostile");
  const sourceLane = game && game.lanes && game.lanes[sourceLaneIndex];
  const targetLane = game && game.lanes && game.lanes[targetLaneIndex];
  if (!sourceLane || !targetLane)
    return false;
  if (targetLane.eliminated)
    return false;
  if (sourceLaneIndex === targetLaneIndex)
    return false;
  return areAllegiancesHostile(resolveLaneAllegianceKey(sourceLane), resolveLaneAllegianceKey(targetLane));
}

function findNextActiveOpponentLaneIndex(game, fromLaneIndex, deps = {}) {
  if (!game || !Array.isArray(game.lanes) || game.lanes.length <= 1)
    return null;
  const total = Math.min(Number(game.playerCount) || game.lanes.length, game.lanes.length);
  for (let step = 1; step < total; step += 1) {
    const idx = (fromLaneIndex + step) % total;
    if (isOpponentLane(game, fromLaneIndex, idx, deps))
      return idx;
  }
  return null;
}

function applyLaneCommandAction(game, lane, commandState, data = null, deps = {}) {
  const normalizeLaneCommandState = requireDepFunction(deps, "normalizeLaneCommandState");
  const resolveLaneCommandAnchorProgressRequest = requireDepFunction(deps, "resolveLaneCommandAnchorProgressRequest");
  const getLaneCommandObjectiveLaneIndex = requireDepFunction(deps, "getLaneCommandObjectiveLaneIndex");
  const isLaneCombatEnabledCommandState = requireDepFunction(deps, "isLaneCombatEnabledCommandState");
  const getLaneCommandEngagementRadius = requireDepFunction(deps, "getLaneCommandEngagementRadius");
  const syncLaneCommandAssignments = requireDepFunction(deps, "syncLaneCommandAssignments");
  const laneCommandStates = getLaneCommandStates(deps);
  const normalizedCommandState = normalizeLaneCommandState(commandState);
  if (!lane || !normalizedCommandState)
    return { ok: false, reason: "Invalid lane command" };

  const requestedTargetLaneIndex = Number.isInteger(data && data.targetLaneIndex)
    ? data.targetLaneIndex
    : null;
  if (requestedTargetLaneIndex !== null && isOpponentLane(game, lane.laneIndex, requestedTargetLaneIndex, deps))
    lane.commandTargetLaneIndex = requestedTargetLaneIndex;

  const anchorProgress = resolveLaneCommandAnchorProgressRequest(
    game,
    lane,
    normalizedCommandState,
    data
  );

  lane.commandState = normalizedCommandState;
  lane.commandAnchorProgress = anchorProgress;
  lane.commandTargetLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  lane.combatEnabled = isLaneCombatEnabledCommandState(normalizedCommandState);
  lane.engagementRadius = getLaneCommandEngagementRadius(lane);
  syncBarracksSiteCommandStates(lane, normalizedCommandState, deps);
  syncLaneCommandAssignments(game);
  return { ok: true };
}

function syncBarracksSiteCommandStates(lane, commandState, deps = {}) {
  const ensureBarracksSiteStates = requireDepFunction(deps, "ensureBarracksSiteStates");
  const normalizeLaneCommandState = requireDepFunction(deps, "normalizeLaneCommandState");
  const normalizedCommandState = normalizeLaneCommandState(commandState);
  if (!lane || !normalizedCommandState)
    return;

  const states = ensureBarracksSiteStates(lane);
  if (!states || typeof states !== "object")
    return;

  for (const siteState of Object.values(states)) {
    if (!siteState || typeof siteState !== "object")
      continue;
    siteState.commandState = normalizedCommandState;
  }
}

function applyBarracksSiteCommandAction(game, lane, barracksId, commandState, data = null, deps = {}) {
  const normalizeLaneCommandState = requireDepFunction(deps, "normalizeLaneCommandState");
  const normalizeBarracksSiteId = requireDepFunction(deps, "normalizeBarracksSiteId");
  const ensureBarracksSiteStates = requireDepFunction(deps, "ensureBarracksSiteStates");
  const resolveLaneCommandAnchorProgressRequest = requireDepFunction(deps, "resolveLaneCommandAnchorProgressRequest");
  const syncLaneCommandAssignments = requireDepFunction(deps, "syncLaneCommandAssignments");
  const normalizedCommandState = normalizeLaneCommandState(commandState);
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!lane || !normalizedCommandState || !normalizedBarracksId)
    return { ok: false, reason: "Invalid barracks command" };

  const siteStates = ensureBarracksSiteStates(lane, game);
  const siteState = siteStates && typeof siteStates === "object"
    ? siteStates[normalizedBarracksId]
    : null;
  if (!siteState)
    return { ok: false, reason: "Missing or invalid barracksId" };

  const requestedTargetLaneIndex = Number.isInteger(data && data.targetLaneIndex)
    ? data.targetLaneIndex
    : null;
  if (requestedTargetLaneIndex !== null && isOpponentLane(game, lane.laneIndex, requestedTargetLaneIndex, deps))
    lane.commandTargetLaneIndex = requestedTargetLaneIndex;

  if (normalizedCommandState === getLaneCommandStates(deps).DEFEND) {
    const anchorProgress = resolveLaneCommandAnchorProgressRequest(game, lane, normalizedCommandState, data);
    if (Number.isFinite(anchorProgress))
      lane.commandAnchorProgress = anchorProgress;
  }

  siteState.commandState = normalizedCommandState;
  syncLaneCommandAssignments(game);
  return { ok: true };
}

function warnLegacyActionOnce(game, laneIndex, type, deps = {}) {
  const log = deps && deps.log;
  if (!game || !type)
    return;
  if (!(game.__legacyActionWarnings instanceof Set))
    game.__legacyActionWarnings = new Set();
  const warningKey = `${laneIndex}:${type}`;
  if (game.__legacyActionWarnings.has(warningKey))
    return;
  game.__legacyActionWarnings.add(warningKey);
  if (log && typeof log.warn === "function") {
    log.warn("[ActionBoundary] rejected legacy fortress action", {
      actionType: type,
      laneIndex,
      tick: Number(game.tick) || 0,
    });
  }
}

function rejectLegacyFortressAction(game, laneIndex, type, deps = {}) {
  const reason = getLegacyActionRejectionReasons(deps)[type];
  if (!reason)
    return null;
  warnLegacyActionOnce(game, laneIndex, type, deps);
  return { ok: false, reason };
}

function applyLumberMillRepairAllAction(game, lane, data = null, deps = {}) {
  const getFortressPadByBuildingType = requireDepFunction(deps, "getFortressPadByBuildingType");
  const getFortressPadLifecycleState = requireDepFunction(deps, "getFortressPadLifecycleState");
  const applyFortressPadRepair = requireDepFunction(deps, "applyFortressPadRepair");
  const getBarracksSiteState = requireDepFunction(deps, "getBarracksSiteState");
  const getBarracksSiteLifecycleState = requireDepFunction(deps, "getBarracksSiteLifecycleState");
  const applyBarracksSiteRepair = requireDepFunction(deps, "applyBarracksSiteRepair");
  const lifecycleStates = getBuildingLifecycleStates(deps);
  const requestedPadId = String((data && data.padId) || "").trim();
  const lumberMillPad = getFortressPadByBuildingType(lane, "lumber_mill");
  if (!lumberMillPad || Math.max(0, Math.floor(Number(lumberMillPad.tier) || 0)) <= 0)
    return { ok: false, reason: "Build the Lumber Mill first" };
  if (requestedPadId && requestedPadId !== lumberMillPad.padId)
    return { ok: false, reason: "Repair must be triggered from the Lumber Mill" };
  if (getFortressPadLifecycleState(lumberMillPad, game) === lifecycleStates.beingBuilt)
    return { ok: false, reason: "Lumber Mill construction is not complete" };

  const availableGold = Math.max(0, Math.floor(Number(lane && lane.gold) || 0));
  if (availableGold <= 0)
    return { ok: false, reason: "Not enough gold" };

  const repairTargets = [];
  if (lane && Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      if (!pad)
        continue;
      if (String(pad.buildingType || "").trim().toLowerCase() === "town_core")
        continue;

      const lifecycleState = getFortressPadLifecycleState(pad, game);
      const maxHp = Math.max(0, Math.floor(Number(pad.maxHp) || 0));
      const hp = Math.max(0, Math.floor(Number(pad.hp) || 0));
      if (lifecycleState === lifecycleStates.beingBuilt || maxHp <= 0 || hp >= maxHp)
        continue;

      repairTargets.push({
        kind: "fortress_pad",
        id: pad.padId,
        buildingType: pad.buildingType,
        lifecycleState,
        missingHp: maxHp - hp,
        sortKey: `fortress:${pad.padId}`,
        applyRepair(amount) {
          return applyFortressPadRepair(game, lane, pad.padId, amount, deps);
        },
      });
    }
  }

  for (const siteDef of getBarracksSiteDefs(deps)) {
    if (!siteDef)
      continue;

    const siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
    if (!siteState || !siteState.isBuilt)
      continue;

    const lifecycleState = getBarracksSiteLifecycleState(siteState, game);
    const maxHp = Math.max(0, Math.floor(Number(siteState.maxHp) || 0));
    const hp = Math.max(0, Math.floor(Number(siteState.hp) || 0));
    if (lifecycleState === lifecycleStates.beingBuilt || maxHp <= 0 || hp >= maxHp)
      continue;

    repairTargets.push({
      kind: "barracks_site",
      id: siteDef.barracksId,
      buildingType: "barracks",
      lifecycleState,
      missingHp: maxHp - hp,
      sortKey: `barracks:${siteDef.barracksId}`,
      applyRepair(amount) {
        return applyBarracksSiteRepair(game, lane, siteDef.barracksId, amount);
      },
    });
  }

  if (repairTargets.length <= 0)
    return { ok: false, reason: "No damaged buildings to repair" };

  const lifecyclePriority = new Map([
    [lifecycleStates.destroyed, 0],
    [lifecycleStates.underRepair, 1],
    [lifecycleStates.active, 2],
  ]);
  repairTargets.sort((left, right) => {
    const leftPriority = lifecyclePriority.get(left.lifecycleState) ?? 99;
    const rightPriority = lifecyclePriority.get(right.lifecycleState) ?? 99;
    if (leftPriority !== rightPriority)
      return leftPriority - rightPriority;
    return String(left.sortKey || "").localeCompare(String(right.sortKey || ""));
  });

  const totalMissingHp = repairTargets.reduce((sum, target) => sum + Math.max(0, Math.floor(Number(target.missingHp) || 0)), 0);
  const repairBudget = Math.min(availableGold, totalMissingHp);
  if (repairBudget <= 0)
    return { ok: false, reason: "No damaged buildings to repair" };

  let remainingGold = repairBudget;
  let hpRestored = 0;
  let fullyRestoredCount = 0;
  let partiallyRepairedCount = 0;
  const repairedTargetIds = [];
  for (const target of repairTargets) {
    if (!target || remainingGold <= 0)
      continue;

    const repairAmount = Math.min(
      remainingGold,
      Math.max(0, Math.floor(Number(target.missingHp) || 0))
    );
    if (repairAmount <= 0)
      continue;

    const repairResult = target.applyRepair(repairAmount);
    const restoredNow = Math.max(0, Math.floor(Number(repairResult && repairResult.hpRestored) || 0));
    if (restoredNow <= 0)
      continue;

    remainingGold -= restoredNow;
    hpRestored += restoredNow;
    repairedTargetIds.push(`${target.kind}:${target.id}`);
    if (repairResult.lifecycleState === lifecycleStates.active)
      fullyRestoredCount += 1;
    else if (repairResult.lifecycleState === lifecycleStates.underRepair)
      partiallyRepairedCount += 1;
  }

  if (hpRestored <= 0)
    return { ok: false, reason: "No damaged buildings to repair" };

  lane.gold -= hpRestored;
  lane.totalBuildSpend += hpRestored;
  lane.buildSpendThisRound += hpRestored;
  if (typeof deps.recordBalanceSpend === "function") {
    deps.recordBalanceSpend(game, lane, "repair", hpRestored, {
      buildingType: "lumber_mill",
      padId: lumberMillPad.padId,
      eligibleTargetCount: repairTargets.length,
      repairedTargetIds,
    });
  }

  return {
    ok: true,
    goldSpent: hpRestored,
    hpRestored,
    eligibleTargetCount: repairTargets.length,
    fullyRestoredCount,
    partiallyRepairedCount,
    repairedTargetIds,
    totalMissingHp,
  };
}

function applyMLAction(game, laneIndex, action, deps = {}) {
  const applyFortressBuildOnPad = requireDepFunction(deps, "applyFortressBuildOnPad");
  const getFortressPadByBuildingType = requireDepFunction(deps, "getFortressPadByBuildingType");
  const applyFortressUpgrade = requireDepFunction(deps, "applyFortressUpgrade");
  const applyFortressBuildingUpgradePurchase = requireDepFunction(deps, "applyFortressBuildingUpgradePurchase");
  const normalizeBarracksSiteId = requireDepFunction(deps, "normalizeBarracksSiteId");
  const applyBarracksSiteBuildAction = requireDepFunction(deps, "applyBarracksSiteBuildAction");
  const applyBarracksSiteUpgradeAction = requireDepFunction(deps, "applyBarracksSiteUpgradeAction");
  const buyBarracksUnit = requireDepFunction(deps, "buyBarracksUnit");
  const buyMarketUnit = requireDepFunction(deps, "buyMarketUnit");
  const sellBarracksUnit = requireDepFunction(deps, "sellBarracksUnit");
  const deployBarracksHero = requireDepFunction(deps, "deployBarracksHero");
  const laneCommandStates = getLaneCommandStates(deps);

  if (!game || game.phase !== "playing")
    return { ok: false, reason: "Game not active" };
  if (laneIndex < 0 || laneIndex >= game.lanes.length)
    return { ok: false, reason: "Bad laneIndex" };
  const lane = game.lanes[laneIndex];
  if (lane.eliminated)
    return { ok: false, reason: "You have been eliminated" };
  if (!action || typeof action.type !== "string")
    return { ok: false, reason: "Bad action" };

  game.actionSeq = (game.actionSeq || 0) + 1;
  action.tickApply = game.tick;
  action.laneId = laneIndex;
  action.actionSeq = game.actionSeq;

  const { type, data } = action;
  const requestedBarracksId = String((data && data.barracksId) || "").trim();

  if (type === "build_on_pad") {
    const padId = String((data && data.padId) || "").trim();
    if (!padId)
      return { ok: false, reason: "Missing padId" };
    return applyFortressBuildOnPad(game, lane, padId);
  }

  if (type === "upgrade_building") {
    const rawPadId = String((data && data.padId) || "").trim();
    const rawBuildingType = String((data && data.buildingType) || "").trim();
    const padId = rawPadId || (getFortressPadByBuildingType(lane, rawBuildingType) || {}).padId;
    if (!padId)
      return { ok: false, reason: "Missing padId" };
    return applyFortressUpgrade(game, lane, padId, deps);
  }

  if (type === "purchase_building_upgrade") {
    const padId = String((data && data.padId) || "").trim();
    const upgradeKey = String((data && data.upgradeKey) || "").trim();
    if (!padId)
      return { ok: false, reason: "Missing padId" };
    if (!upgradeKey)
      return { ok: false, reason: "Missing upgradeKey" };
    return applyFortressBuildingUpgradePurchase(game, lane, padId, upgradeKey, deps);
  }

  if (type === "repair_all_buildings")
    return applyLumberMillRepairAllAction(game, lane, data, deps);

  if (type === "build_barracks_site") {
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    if (!barracksId)
      return { ok: false, reason: "Missing or invalid barracksId" };
    return applyBarracksSiteBuildAction(game, lane, barracksId);
  }

  if (type === "upgrade_barracks_site") {
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    if (!barracksId)
      return { ok: false, reason: "Missing or invalid barracksId" };
    return applyBarracksSiteUpgradeAction(game, lane, barracksId);
  }

  if (type === "buy_barracks_unit") {
    const rosterKey = String((data && data.rosterKey) || "").trim();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const requestedCount = Math.max(1, Math.floor(Number((data && data.count) || 1) || 1));
    const count = Math.min(25, requestedCount);
    return buyBarracksUnit(game, laneIndex, lane, rosterKey, requestedBarracksId, count);
  }

  if (type === "buy_market_unit") {
    const unitKey = String((data && data.unitKey) || "").trim();
    const requestedCount = Math.max(1, Math.floor(Number((data && data.count) || 1) || 1));
    const count = Math.min(25, requestedCount);
    return buyMarketUnit(game, laneIndex, lane, unitKey, count, deps);
  }

  if (type === "sell_barracks_unit") {
    const rosterKey = String((data && data.rosterKey) || "").trim();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    return sellBarracksUnit(laneIndex, lane, rosterKey, requestedBarracksId);
  }

  if (type === "deploy_barracks_hero") {
    const heroKey = String((data && data.heroKey) || "").trim().toLowerCase();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    return deployBarracksHero(game, laneIndex, lane, heroKey, requestedBarracksId);
  }

  if (type === "set_barracks_attack")
    return applyBarracksSiteCommandAction(game, lane, requestedBarracksId, laneCommandStates.ATTACK, data, deps);

  if (type === "set_barracks_defend" || type === "set_barracks_hold" || type === "set_barracks_defend_point")
    return applyBarracksSiteCommandAction(game, lane, requestedBarracksId, laneCommandStates.DEFEND, data, deps);

  if (type === "set_barracks_retreat" || type === "set_barracks_callback")
    return applyBarracksSiteCommandAction(game, lane, requestedBarracksId, laneCommandStates.RETREAT, data, deps);

  if (type === "set_lane_attack" && requestedBarracksId)
    return applyBarracksSiteCommandAction(game, lane, requestedBarracksId, laneCommandStates.ATTACK, data, deps);

  if ((type === "set_lane_defend" || type === "set_lane_hold" || type === "set_lane_defend_point") && requestedBarracksId)
    return applyBarracksSiteCommandAction(game, lane, requestedBarracksId, laneCommandStates.DEFEND, data, deps);

  if ((type === "set_lane_retreat" || type === "set_lane_callback") && requestedBarracksId)
    return applyBarracksSiteCommandAction(game, lane, requestedBarracksId, laneCommandStates.RETREAT, data, deps);

  if (type === "set_lane_attack")
    return applyLaneCommandAction(game, lane, laneCommandStates.ATTACK, data, deps);

  if (type === "set_lane_defend" || type === "set_lane_hold" || type === "set_lane_defend_point")
    return applyLaneCommandAction(game, lane, laneCommandStates.DEFEND, data, deps);

  if (type === "set_lane_retreat" || type === "set_lane_callback")
    return applyLaneCommandAction(game, lane, laneCommandStates.RETREAT, data, deps);

  if (type === "set_lane_command")
    return applyLaneCommandAction(game, lane, data && data.commandState, data, deps);

  const legacyRejection = rejectLegacyFortressAction(game, laneIndex, type, deps);
  if (legacyRejection)
    return legacyRejection;

  return { ok: false, reason: "Unknown action type" };
}

module.exports = {
  getDefaultSlotDefinitions,
  normalizeGameOptions,
  createMLGame,
  getLaneBuildValue,
  getLaneWaveResult,
  createRoundSnapshotLane,
  isOpponentLane,
  findNextActiveOpponentLaneIndex,
  applyMLAction,
};
