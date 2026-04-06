"use strict";

const { logSpawnAuditInfo } = require("./spawnAuditLogging");

const DEFAULT_GRID_WIDTH = 11;
const DEFAULT_SPAWN_X = 5;
const DEFAULT_MAX_UNITS_PER_LANE = 200;
const DEFAULT_FORT_PRESENTATION_KEY = "fort_default";

const DEFAULT_SPAWN_SOURCE_TYPES = Object.freeze({
  DUNGEON_WAVE: "dungeon_wave",
  SCHEDULED_WAVE: "dungeon_wave",
  BARRACKS_ROSTER: "barracks_roster",
  BARRACKS_HERO: "barracks_hero",
  MARKET_ROSTER: "market_roster",
});

const DEFAULT_ALLEGIANCE_KEYS = Object.freeze({
  DUNGEON: "dungeon",
});

const DEFAULT_UNIT_STANCES = Object.freeze({
  ATTACK: "ATTACK",
});

const DEFAULT_PATH_CONTRACT_TYPES = Object.freeze({
  WAVE_LANE: "wave_lane",
});

const DEFAULT_UNIT_MOVEMENT_MODES = Object.freeze({
  LANE_TRAVEL: "LaneTravel",
});

const DEFAULT_WAVE_UNIT_STATES = Object.freeze({
  IDLE: "IDLE",
});

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`spawnSystem requires deps.${name}`);
  return fn;
}

function getGridWidth(deps = {}) {
  const value = Math.floor(Number(deps.GRID_W));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_GRID_WIDTH;
}

function getSpawnX(deps = {}) {
  const gridWidth = getGridWidth(deps);
  const value = Math.floor(Number(deps.SPAWN_X));
  if (!Number.isInteger(value))
    return Math.max(0, Math.min(gridWidth - 1, DEFAULT_SPAWN_X));
  return Math.max(0, Math.min(gridWidth - 1, value));
}

function getMaxUnitsPerLane(deps = {}) {
  const value = Math.floor(Number(deps.MAX_UNITS_PER_LANE));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_MAX_UNITS_PER_LANE;
}

function getDefaultFortPresentationKey(deps = {}) {
  return deps.DEFAULT_FORT_PRESENTATION_KEY || DEFAULT_FORT_PRESENTATION_KEY;
}

function getSpawnSourceTypes(deps = {}) {
  return deps.SPAWN_SOURCE_TYPES || DEFAULT_SPAWN_SOURCE_TYPES;
}

function getAllegianceKeys(deps = {}) {
  return deps.ALLEGIANCE_KEYS || DEFAULT_ALLEGIANCE_KEYS;
}

function getUnitStances(deps = {}) {
  return deps.UNIT_STANCES || DEFAULT_UNIT_STANCES;
}

function getPathContractTypes(deps = {}) {
  return deps.PATH_CONTRACT_TYPES || DEFAULT_PATH_CONTRACT_TYPES;
}

function getUnitMovementModes(deps = {}) {
  return deps.UNIT_MOVEMENT_MODES || DEFAULT_UNIT_MOVEMENT_MODES;
}

function getWaveUnitStates(deps = {}) {
  return deps.WAVE_UNIT_STATES || DEFAULT_WAVE_UNIT_STATES;
}

function normalizeSpawnSourceType(value, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case "scheduled_wave":
    case "dungeon_wave":
      return spawnSourceTypes.DUNGEON_WAVE;
    case spawnSourceTypes.BARRACKS_ROSTER:
      return spawnSourceTypes.BARRACKS_ROSTER;
    case spawnSourceTypes.BARRACKS_HERO:
      return spawnSourceTypes.BARRACKS_HERO;
    case spawnSourceTypes.MARKET_ROSTER:
      return spawnSourceTypes.MARKET_ROSTER;
    default:
      return null;
  }
}

function resolveSourceBarracksId(source, deps = {}) {
  const normalizeBarracksSiteId = requireDepFunction(deps, "normalizeBarracksSiteId");
  return normalizeBarracksSiteId(
    source && (source.sourceBarracksId || source.sourceBarracksKey || source.barracksId)
  );
}

function getLaneWaveSpeedMult(lane) {
  if (!lane || !Number.isFinite(Number(lane.waveSpeedMult)))
    return 1;
  return Math.max(0.01, Number(lane.waveSpeedMult));
}

function resolveSpawnSourceTypeFromWaveDef(waveDef, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const explicitSourceType = normalizeSpawnSourceType(waveDef && waveDef.spawnSourceType, deps);
  if (explicitSourceType)
    return explicitSourceType;

  if (waveDef && String(waveDef.spawnSourceType || "").trim().toLowerCase() === DEFAULT_SPAWN_SOURCE_TYPES.MARKET_ROSTER)
    return spawnSourceTypes.MARKET_ROSTER;

  if (waveDef && waveDef.isHero)
    return spawnSourceTypes.BARRACKS_HERO;

  const sourceBarracksId = resolveSourceBarracksId(waveDef, deps);
  if (waveDef && (sourceBarracksId
      || (Number.isInteger(waveDef.sourceLaneIndex) && waveDef.sourceLaneIndex >= 0))) {
    return spawnSourceTypes.BARRACKS_ROSTER;
  }

  return spawnSourceTypes.DUNGEON_WAVE;
}

function resolveSpawnSourceTypeFromUnit(unit, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const explicitSourceType = normalizeSpawnSourceType(unit && unit.spawnSourceType, deps);
  if (explicitSourceType)
    return explicitSourceType;

  if (unit && unit.isDefender)
    return unit.isHero ? spawnSourceTypes.BARRACKS_HERO : spawnSourceTypes.BARRACKS_ROSTER;

  return resolveSpawnSourceTypeFromWaveDef(unit, deps);
}

function isScheduledWaveUnit(unit, deps = {}) {
  return resolveSpawnSourceTypeFromUnit(unit, deps) === getSpawnSourceTypes(deps).DUNGEON_WAVE;
}

function resolveCenteredSpawnColumn(slotIndex, deps = {}) {
  const gridWidth = getGridWidth(deps);
  const centerColumn = getSpawnX(deps);
  const safeSlotIndex = Math.max(0, Math.floor(Number(slotIndex) || 0));
  if (safeSlotIndex <= 0)
    return centerColumn;

  const step = Math.ceil(safeSlotIndex / 2);
  const offset = safeSlotIndex % 2 === 1 ? -step : step;
  return Math.max(0, Math.min(gridWidth - 1, centerColumn + offset));
}

function resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex, deps = {}) {
  const gridWidth = getGridWidth(deps);
  const safeSpawnIndex = Math.max(0, Math.floor(Number(resolvedSpawnIndex) || 0));
  const row = Math.floor(safeSpawnIndex / gridWidth);
  const slotInRow = safeSpawnIndex % gridWidth;
  if (spawnType === getSpawnSourceTypes(deps).SCHEDULED_WAVE) {
    return {
      x: resolveCenteredSpawnColumn(slotInRow, deps),
      y: row,
    };
  }

  return {
    x: slotInRow,
    y: row,
  };
}

function validateSpawnDefinition(game, targetLane, waveDef, options = {}, deps = {}) {
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const getSourceLane = requireDepFunction(deps, "getSourceLane");
  const normalizeAllegianceKey = requireDepFunction(deps, "normalizeAllegianceKey");
  const describeBarracksSite = requireDepFunction(deps, "describeBarracksSite");
  const getWaveSpawnWorldPosition = requireDepFunction(deps, "getWaveSpawnWorldPosition");
  const resolveLaneAllegianceKey = requireDepFunction(deps, "resolveLaneAllegianceKey");
  const gridWidth = getGridWidth(deps);
  const spawnType = resolveSpawnSourceTypeFromWaveDef(waveDef, deps);
  const allowUnbuiltBarracks = !!(
    options.allowUnbuiltBarracks
    || (waveDef && waveDef.allowUnbuiltBarracks)
  );
  const requestedSpawnIndex = Number.isInteger(waveDef && waveDef.spawnIndex)
    ? waveDef.spawnIndex
    : Math.max(0, targetLane && Array.isArray(targetLane.spawnQueue) ? targetLane.spawnQueue.length : 0);
  const resolvedSpawnIndex = Math.max(0, requestedSpawnIndex);
  const logicalPos = resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex, deps);
  const sourceLaneIndex = Number.isInteger(waveDef && waveDef.sourceLaneIndex) ? waveDef.sourceLaneIndex : -1;
  const sourceLane = getSourceLane(game, sourceLaneIndex);
  const sourceBarracksKey = resolveSourceBarracksId(waveDef, deps);
  const sourceTeam = normalizeAllegianceKey(waveDef && waveDef.sourceTeam);
  const requiresSourceLane = spawnType === spawnSourceTypes.MARKET_ROSTER
    || spawnType === spawnSourceTypes.BARRACKS_ROSTER
    || spawnType === spawnSourceTypes.BARRACKS_HERO;

  if (!targetLane)
    return { ok: false, reason: "Missing target lane", spawnType };

  if (logicalPos.x < 0 || logicalPos.x >= gridWidth || logicalPos.y < 0)
    return { ok: false, reason: "Resolved spawn index is out of legal queue bounds", spawnType };

  if (requiresSourceLane && !sourceLane)
    return { ok: false, reason: "Spawn source lane is missing", spawnType };

  if ((spawnType === spawnSourceTypes.BARRACKS_ROSTER || spawnType === spawnSourceTypes.BARRACKS_HERO) && !sourceBarracksKey)
    return { ok: false, reason: "Spawn source barracks id is missing", spawnType };

  if (requiresSourceLane
      && sourceLane && sourceTeam && resolveLaneAllegianceKey(sourceLane) !== sourceTeam) {
    return { ok: false, reason: "Spawn source team does not match source lane ownership", spawnType };
  }

  if ((spawnType === spawnSourceTypes.BARRACKS_ROSTER || spawnType === spawnSourceTypes.BARRACKS_HERO)
      && sourceLane && !allowUnbuiltBarracks) {
    const descriptor = describeBarracksSite(game, sourceLane, sourceBarracksKey);
    if (!descriptor || !descriptor.isBuilt) {
      return {
        ok: false,
        reason: "Spawn source barracks does not exist or is not built on the source lane",
        spawnType,
      };
    }
  }

  if (spawnType === spawnSourceTypes.DUNGEON_WAVE && !getWaveSpawnWorldPosition(targetLane.laneIndex))
    return { ok: false, reason: "Wave spawn origin is missing for the target lane", spawnType };

  return {
    ok: true,
    spawnType,
    sourceLaneIndex,
    sourceTeam,
    sourceBarracksKey,
    requestedSpawnIndex,
    resolvedSpawnIndex,
    logicalPos,
  };
}

function getEffectiveWaveEntrySpeedMult(game, lane, waveDef, deps = {}) {
  const safeWaveDef = waveDef && typeof waveDef === "object" ? waveDef : {};
  const authoredSpeedMult = Math.max(0.01, Number(safeWaveDef.speed_mult || 1));
  return authoredSpeedMult * getLaneWaveSpeedMult(lane);
}

function spawnWaveUnit(game, lane, waveDef, options = {}, deps = {}) {
  const resolveUnitDef = requireDepFunction(deps, "resolveUnitDef");
  const getBaseCombatPathSpeed = requireDepFunction(deps, "getBaseCombatPathSpeed");
  const resolveLaneAllegianceKey = requireDepFunction(deps, "resolveLaneAllegianceKey");
  const getSourceLane = requireDepFunction(deps, "getSourceLane");
  const getLaneCommandRouteObjectiveLaneIndex = requireDepFunction(deps, "getLaneCommandRouteObjectiveLaneIndex");
  const resolveGameplayCatalogUnitKey = requireDepFunction(deps, "resolveGameplayCatalogUnitKey");
  const buildAbilitiesForUnitType = requireDepFunction(deps, "buildAbilitiesForUnitType");
  const applyCanonicalUnitMirrors = requireDepFunction(deps, "applyCanonicalUnitMirrors");
  const isFortArchetypeKey = requireDepFunction(deps, "isFortArchetypeKey");
  const markLaneCommandAssignmentsDirty = typeof deps.markLaneCommandAssignmentsDirty === "function"
    ? deps.markLaneCommandAssignmentsDirty
    : null;
  const log = deps.log;
  const spawnSourceTypes = getSpawnSourceTypes(deps);
  const allegianceKeys = getAllegianceKeys(deps);
  const unitStances = getUnitStances(deps);
  const pathContractTypes = getPathContractTypes(deps);
  const unitMovementModes = getUnitMovementModes(deps);
  const waveUnitStates = getWaveUnitStates(deps);
  const unitType = waveDef.unit_type;
  const def = resolveUnitDef(unitType);
  if (!def)
    return;
  if (lane.units.length + lane.spawnQueue.length >= getMaxUnitsPerLane(deps))
    return;

  const spawnValidation = validateSpawnDefinition(game, lane, waveDef, options, deps);
  if (!spawnValidation.ok) {
    if (log && typeof log.error === "function") {
      log.error("[SpawnAudit][ServerQueue] rejected", {
        spawnType: spawnValidation.spawnType,
        reason: spawnValidation.reason,
        unitType,
        laneIndex: lane ? lane.laneIndex : null,
        sourceLaneIndex: waveDef && Number.isInteger(waveDef.sourceLaneIndex) ? waveDef.sourceLaneIndex : -1,
        sourceBarracksKey: resolveSourceBarracksId(waveDef, deps),
        sourceTeam: waveDef && waveDef.sourceTeam ? waveDef.sourceTeam : null,
        requestedSpawnIndex: waveDef && waveDef.spawnIndex,
      });
    }
    return;
  }

  const effectiveSpeedMult = getEffectiveWaveEntrySpeedMult(game, lane, waveDef, deps);
  const hp = Math.ceil(def.hp * Number(waveDef.hp_mult || 1));
  const dmg = def.dmg * Number(waveDef.dmg_mult || 1);
  const spd = getBaseCombatPathSpeed(unitType) * effectiveSpeedMult;
  logSpawnAuditInfo(deps, "[SpawnAudit][ServerQueue] queued", {
    spawnType: spawnValidation.spawnType,
    unitType,
    laneIndex: lane.laneIndex,
    team: lane.team || null,
    sourceLaneIndex: spawnValidation.sourceLaneIndex,
    sourceTeam: spawnValidation.sourceTeam,
    sourceBarracksKey: spawnValidation.sourceBarracksKey,
    requestedSpawnKey: spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE
      ? `lane:${lane.laneIndex}:wave_origin`
      : spawnValidation.spawnType === spawnSourceTypes.MARKET_ROSTER
        ? `lane:${spawnValidation.sourceLaneIndex}:market`
        : `lane:${spawnValidation.sourceLaneIndex}:barracks:${spawnValidation.sourceBarracksKey}`,
    resolvedMarkerName: `server_queue_${spawnValidation.spawnType}`,
    resolvedLogicalPosition: spawnValidation.logicalPos,
    requestedSpawnIndex: spawnValidation.requestedSpawnIndex,
    resolvedSpawnIndex: spawnValidation.resolvedSpawnIndex,
    fallbackUsed: false,
    authoring: "server",
  });

  const ownerLaneIndex = spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE
    ? -1
    : spawnValidation.sourceLaneIndex;
  const sourceLane = getSourceLane(game, spawnValidation.sourceLaneIndex);
  const objectiveLaneIndex = spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE
    ? lane.laneIndex
    : spawnValidation.spawnType === spawnSourceTypes.MARKET_ROSTER
      ? spawnValidation.sourceLaneIndex
    : getLaneCommandRouteObjectiveLaneIndex(game, sourceLane);
  const queuedUnit = {
    id: `wu${game.nextUnitId++}`,
    unitId: null,
    targetLaneIndex: lane.laneIndex,
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    objectiveLaneIndex,
    sourceLaneIndex: spawnValidation.sourceLaneIndex,
    sourceTeam: spawnValidation.sourceTeam,
    sourceBarracksKey: spawnValidation.sourceBarracksKey,
    sourceBarracksId: spawnValidation.sourceBarracksKey,
    barracksId: spawnValidation.sourceBarracksKey,
    spawnSourceType: spawnValidation.spawnType,
    allegianceKey: spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE
      ? allegianceKeys.DUNGEON
      : resolveLaneAllegianceKey(getSourceLane(game, spawnValidation.sourceLaneIndex)),
    type: unitType,
    unitTypeKey: unitType,
    archetypeKey: waveDef.archetypeKey || (isFortArchetypeKey(unitType) ? unitType : null),
    presentationKey: waveDef.presentationKey || null,
    catalogUnitKey: resolveGameplayCatalogUnitKey(
      unitType,
      waveDef.presentationKey || getDefaultFortPresentationKey(deps)
    ),
    skinKey: waveDef.skinKey || null,
    marketUnitKey: waveDef.marketUnitKey || null,
    isMarketWorker: spawnValidation.spawnType === spawnSourceTypes.MARKET_ROSTER,
    isHero: !!waveDef.isHero,
    heroKey: waveDef.heroKey || null,
    heroVisualStyleKey: waveDef.heroVisualStyleKey || null,
    rosterKey: waveDef.rosterKey || null,
    role: waveDef.role || null,
    combatRole: waveDef.combatRole || null,
    pathIdx: 0,
    hp,
    maxHp: hp,
    baseDmg: dmg,
    baseSpeed: spd,
    atkCd: 0,
    atkCdTicks: def.atkCdTicks,
    armorType: def.armorType || "MEDIUM",
    damageReductionPct: def.damageReductionPct || 0,
    abilities: buildAbilitiesForUnitType(unitType),
    bounty: def.bounty || 1,
    stance: spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE ? unitStances.ATTACK : null,
    pathContractType: spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE
      ? pathContractTypes.WAVE_LANE
      : null,
    isWaveUnit: spawnValidation.spawnType === spawnSourceTypes.DUNGEON_WAVE,
    isDefender: false,
    combatTarget: null,
    combatTargetId: null,
    combatTargetLockedUntilTick: 0,
    currentTargetId: null,
    regroupUntilTick: 0,
    combatState: waveUnitStates.IDLE,
    state: waveUnitStates.IDLE,
    movementMode: unitMovementModes.LANE_TRAVEL,
    hasBreachedTownCore: false,
    spawnIndex: spawnValidation.resolvedSpawnIndex,
    spawnLogicalPos: spawnValidation.logicalPos,
  };
  applyCanonicalUnitMirrors(game, lane, queuedUnit);
  lane.spawnQueue.push(queuedUnit);
  if (markLaneCommandAssignmentsDirty && spawnValidation.spawnType !== spawnSourceTypes.DUNGEON_WAVE)
    markLaneCommandAssignmentsDirty(game);
}

module.exports = {
  resolveSpawnSourceTypeFromWaveDef,
  resolveSpawnSourceTypeFromUnit,
  isScheduledWaveUnit,
  resolveSpawnLogicalPosition,
  validateSpawnDefinition,
  getEffectiveWaveEntrySpeedMult,
  spawnWaveUnit,
};
