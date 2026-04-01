"use strict";

const { DEFAULT_FORT_PRESENTATION_KEY } = require("../fortUnitCatalog");
const {
  getMaxBarracksLevel,
  getBarracksUpgradeDef,
} = require("../../barracksLevels");
const {
  FORTRESS_BUILD_STATES,
  FORTRESS_BUILDING_DEFS,
  getBuildingTierDisplayName,
  getBuildingBranchLabel,
  getBuildingDisplayName,
  getTownCoreTier,
  getFortressRequiredTownCoreTier,
  getHighestBuiltFortressPadTier,
  getFortressPadByBuildingType,
} = require("./fortressSystem");

const TICK_HZ = 20;
const BARRACKS_LEVEL_ONE_SPEED_MULT = 0.50;
const SPEED_UPGRADE_STEP = 0.25;
const BARRACKS_COST_BASE = 100;
const BARRACKS_REQ_INCOME_BASE = 8;
const BARRACKS_ROSTER_REFUND_PCT = 70;
const STARTING_COMBAT_TEST_BARRACKS_ID = "center";
const STARTING_COMBAT_TEST_MILITIA_ROSTER_KEY = "militia";

const BARRACKS_ROLE_SORT_ORDER = Object.freeze({
  melee: 0,
  ranged: 1,
  support: 2,
  siege: 3,
});

const BARRACKS_SPAWN_ROLE_ORDER = Object.freeze(["support", "ranged", "melee", "siege"]);

const BARRACKS_SITE_DEFS = Object.freeze([
  {
    barracksId: "center",
    displayName: "Barracks Center",
    slot: "center",
    sortIndex: 0,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    legacyTierUnlock: 1,
  },
  {
    barracksId: "left",
    displayName: "Barracks Left",
    slot: "left",
    sortIndex: 1,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    legacyTierUnlock: 2,
  },
  {
    barracksId: "right",
    displayName: "Barracks Right",
    slot: "right",
    sortIndex: 2,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    legacyTierUnlock: 3,
  },
]);

const BARRACKS_ROSTER_DEFS = Object.freeze([
  { rosterKey: "militia", displayName: "Militia", role: "melee", branchKey: "infantry", branchLabel: "Infantry", productionBuildingType: "blacksmith", archetypeKey: "infantry_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "barracks", requiredBuildingTier: 1, tier: 1, sortIndex: 10 },
  { rosterKey: "spearman", displayName: "Spearman", role: "melee", branchKey: "polearm", branchLabel: "Polearm", productionBuildingType: "blacksmith", archetypeKey: "polearm_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 1, tier: 1, sortIndex: 20 },
  { rosterKey: "shieldman", displayName: "Shieldman", role: "melee", branchKey: "shield", branchLabel: "Shield", productionBuildingType: "blacksmith", archetypeKey: "shield_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 1, tier: 1, sortIndex: 30 },
  { rosterKey: "swordsman", displayName: "Swordsman", role: "melee", branchKey: "infantry", branchLabel: "Infantry", productionBuildingType: "blacksmith", archetypeKey: "infantry_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 2, tier: 2, sortIndex: 40 },
  { rosterKey: "halberdier", displayName: "Halberdier", role: "melee", branchKey: "polearm", branchLabel: "Polearm", productionBuildingType: "blacksmith", archetypeKey: "polearm_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 2, tier: 2, sortIndex: 50 },
  { rosterKey: "shield_guard", displayName: "Shield Guard", role: "melee", branchKey: "shield", branchLabel: "Shield", productionBuildingType: "blacksmith", archetypeKey: "shield_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 2, tier: 2, sortIndex: 60 },
  { rosterKey: "knight", displayName: "Knight", role: "melee", branchKey: "infantry", branchLabel: "Infantry", productionBuildingType: "blacksmith", archetypeKey: "infantry_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 3, tier: 3, sortIndex: 70 },
  { rosterKey: "lancer", displayName: "Lancer", role: "melee", branchKey: "polearm", branchLabel: "Polearm", productionBuildingType: "blacksmith", archetypeKey: "polearm_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 3, tier: 3, sortIndex: 80 },
  { rosterKey: "guardian", displayName: "Guardian", role: "melee", branchKey: "shield", branchLabel: "Shield", productionBuildingType: "blacksmith", archetypeKey: "shield_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 3, tier: 3, sortIndex: 90 },
  { rosterKey: "cleric", displayName: "Cleric", role: "support", branchKey: "healing", branchLabel: "Temple", productionBuildingType: "temple", archetypeKey: "support_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "temple", requiredBuildingTier: 1, tier: 1, sortIndex: 110 },
  { rosterKey: "priest", displayName: "Priest", role: "support", branchKey: "healing", branchLabel: "Temple", productionBuildingType: "temple", archetypeKey: "support_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "temple", requiredBuildingTier: 2, tier: 2, sortIndex: 120 },
  { rosterKey: "high_priest", displayName: "High Priest", role: "support", branchKey: "healing", branchLabel: "Temple", productionBuildingType: "temple", archetypeKey: "support_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "temple", requiredBuildingTier: 3, tier: 3, sortIndex: 130 },
  { rosterKey: "mage", displayName: "Mage", role: "ranged", branchKey: "arcane", branchLabel: "Wizard Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 1, tier: 1, sortIndex: 140 },
  { rosterKey: "wizard", displayName: "Wizard", role: "ranged", branchKey: "arcane", branchLabel: "Wizard Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 2, tier: 2, sortIndex: 150 },
  { rosterKey: "thaumaturge", displayName: "Thaumaturge", role: "ranged", branchKey: "arcane", branchLabel: "Wizard Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 3, tier: 3, sortIndex: 160 },
  { rosterKey: "archer", displayName: "Archer", role: "ranged", branchKey: "ranged", branchLabel: "Archery", productionBuildingType: "archery_tower", archetypeKey: "ranged_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "archery_tower", requiredBuildingTier: 1, tier: 1, sortIndex: 170 },
  { rosterKey: "crossbowman", displayName: "Crossbowman", role: "ranged", branchKey: "ranged", branchLabel: "Archery", productionBuildingType: "archery_tower", archetypeKey: "ranged_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "archery_tower", requiredBuildingTier: 2, tier: 2, sortIndex: 180 },
  { rosterKey: "ranger", displayName: "Ranger", role: "ranged", branchKey: "ranged", branchLabel: "Archery", productionBuildingType: "archery_tower", archetypeKey: "ranged_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "archery_tower", requiredBuildingTier: 3, tier: 3, sortIndex: 190 },
]);

const HERO_ROSTER_DEFS = Object.freeze([
  { heroKey: "king", displayName: "King", role: "hero", roleLabel: "Hero", spawnRole: "melee", branchKey: "heroes", branchLabel: "Heroes", archetypeKey: "hero_king", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "town_core", requiredBuildingTier: 4, summonSourceBuildingType: "barracks", summonCost: 70, cooldownTicks: 90 * TICK_HZ, activeLimit: 1, heroVisualStyleKey: "regal_gold", sortIndex: 10 },
  { heroKey: "paladin", displayName: "Paladin", role: "hero", roleLabel: "Hero", spawnRole: "melee", branchKey: "heroes", branchLabel: "Heroes", archetypeKey: "hero_paladin", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "town_core", requiredBuildingTier: 4, summonSourceBuildingType: "barracks", summonCost: 60, cooldownTicks: 75 * TICK_HZ, activeLimit: 1, heroVisualStyleKey: "holy_silver", sortIndex: 20 },
  { heroKey: "bishop", displayName: "Bishop", role: "hero", roleLabel: "Hero", spawnRole: "support", branchKey: "heroes", branchLabel: "Heroes", archetypeKey: "hero_bishop", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "town_core", requiredBuildingTier: 4, summonSourceBuildingType: "barracks", summonCost: 55, cooldownTicks: 65 * TICK_HZ, activeLimit: 1, heroVisualStyleKey: "radiant_bishop", sortIndex: 30 },
]);

const MARKET_ROSTER_DEFS = Object.freeze([
  {
    unitKey: "peasant",
    displayName: "Peasant",
    role: "economy",
    roleLabel: "Economy",
    branchKey: "market",
    branchLabel: "Market",
    productionBuildingType: "market",
    archetypeKey: "economy_t1",
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    unlockBuildingType: "market",
    requiredBuildingTier: 1,
    tier: 1,
    economyLapGold: 4,
    routeStartBuildingType: "town_core",
    routeEndBuildingType: "market",
    nextUnitKey: "settler",
    description: "Carries starter trade goods between the Town Core and Market.",
    sortIndex: 10,
  },
  {
    unitKey: "settler",
    displayName: "Settler",
    role: "economy",
    roleLabel: "Economy",
    branchKey: "market",
    branchLabel: "Market",
    productionBuildingType: "market",
    archetypeKey: "economy_t2",
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    unlockBuildingType: "market",
    requiredBuildingTier: 2,
    tier: 2,
    economyLapGold: 7,
    routeStartBuildingType: "town_core",
    routeEndBuildingType: "market",
    nextUnitKey: "trader",
    description: "Carries more goods and trade value than Peasants between the Town Core and Market.",
    sortIndex: 20,
  },
  {
    unitKey: "trader",
    displayName: "Trader",
    role: "economy",
    roleLabel: "Economy",
    branchKey: "market",
    branchLabel: "Market",
    productionBuildingType: "market",
    archetypeKey: "economy_t3",
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    unlockBuildingType: "market",
    requiredBuildingTier: 3,
    tier: 3,
    economyLapGold: 10,
    routeStartBuildingType: "town_core",
    routeEndBuildingType: "market",
    nextUnitKey: null,
    description: "Top-tier trade runner carrying the highest-value cargo between the Town Core and Market.",
    sortIndex: 30,
  },
]);

function getBarracksLevelDef(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  if (lvl === 1) {
    return {
      hpMult: 1,
      dmgMult: 1,
      speedMult: getBarracksSpeedMultForLevel(1),
      structMult: 1,
      unitCostMult: 1,
      unitIncomeMult: 1,
      incomeBonus: 0,
      cost: 0,
      reqIncome: 0,
    };
  }

  const statMult = Math.pow(2, lvl - 1);
  const gateMult = Math.pow(2, lvl - 2);
  return {
    hpMult: statMult,
    dmgMult: statMult,
    speedMult: getBarracksSpeedMultForLevel(lvl),
    structMult: statMult,
    unitCostMult: statMult,
    unitIncomeMult: statMult,
    incomeBonus: 0,
    cost: Math.ceil(BARRACKS_COST_BASE * gateMult),
    reqIncome: Math.ceil(BARRACKS_REQ_INCOME_BASE * gateMult),
  };
}

function getBarracksSpeedMultForLevel(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  return BARRACKS_LEVEL_ONE_SPEED_MULT + ((lvl - 1) * SPEED_UPGRADE_STEP);
}

function getBarracksSpeedMult(br) {
  const barracks = br && typeof br === "object" ? br : null;
  if (barracks && Number.isFinite(barracks.speedMult))
    return Math.max(0.01, Number(barracks.speedMult));
  if (barracks && Number.isFinite(barracks.level))
    return Math.max(0.01, getBarracksSpeedMultForLevel(barracks.level));
  return 1;
}

function createBarracksRosterCounts() {
  const counts = {};
  for (const def of BARRACKS_ROSTER_DEFS)
    counts[def.rosterKey] = 0;
  return counts;
}

function createBarracksSiteRosterCounts() {
  const siteCounts = {};
  for (const siteDef of BARRACKS_SITE_DEFS)
    siteCounts[siteDef.barracksId] = createBarracksRosterCounts();
  return siteCounts;
}

function getBarracksSiteDef(barracksId) {
  return BARRACKS_SITE_DEFS.find((entry) => entry && entry.barracksId === barracksId) || null;
}

function normalizeBarracksSiteId(value) {
  const raw = String(value || "").trim().toLowerCase();
  switch (raw) {
    case "left":
    case "left_barracks":
    case "barracks_left":
      return "left";
    case "right":
    case "right_barracks":
    case "barracks_right":
      return "right";
    case "center":
    case "centre":
    case "center_barracks":
    case "barracks_center":
    case "barracks_centre":
      return "center";
    default:
      return "";
  }
}

function summarizeBarracksSiteCounts(siteCounts) {
  if (!siteCounts || typeof siteCounts !== "object")
    return "<missing>";
  const entries = Object.entries(siteCounts)
    .map(([rosterKey, count]) => ({
      rosterKey,
      ownedCount: Math.max(0, Math.floor(Number(count) || 0)),
    }))
    .filter((entry) => entry.ownedCount > 0)
    .sort((a, b) => String(a.rosterKey).localeCompare(String(b.rosterKey)));
  if (entries.length === 0)
    return "<empty>";
  return entries.map((entry) => `${entry.rosterKey}:${entry.ownedCount}`).join(",");
}

function summarizeBarracksSiteRosterEntries(rosterEntries) {
  if (!Array.isArray(rosterEntries))
    return "<missing>";
  const entries = rosterEntries
    .filter((entry) => entry && Math.max(0, Math.floor(Number(entry.ownedCount) || 0)) > 0)
    .map((entry) => `${entry.rosterKey}:${Math.max(0, Math.floor(Number(entry.ownedCount) || 0))}`);
  return entries.length > 0 ? entries.join(",") : "<empty>";
}

function getBarracksSiteCounts(lane, barracksId) {
  if (!lane)
    return null;
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return null;
  if (!lane.barracksSiteRosterCounts || typeof lane.barracksSiteRosterCounts !== "object")
    lane.barracksSiteRosterCounts = createBarracksSiteRosterCounts();

  if (!lane.barracksSiteRosterCounts[normalizedId])
    lane.barracksSiteRosterCounts[normalizedId] = createBarracksRosterCounts();

  return lane.barracksSiteRosterCounts[normalizedId];
}

function hasOwnedBarracksUnits(lane, barracksId) {
  const siteCounts = getBarracksSiteCounts(lane, barracksId);
  if (!siteCounts)
    return false;
  for (const value of Object.values(siteCounts)) {
    if (Math.max(0, Math.floor(Number(value) || 0)) > 0)
      return true;
  }
  return false;
}

function getBarracksSiteTierRequirement(siteDef) {
  return siteDef ? Math.max(1, Math.floor(Number(siteDef.legacyTierUnlock) || 1)) : 1;
}

function getBarracksSiteBuildCost(siteDef) {
  return siteDef ? Math.max(0, Math.floor(Number(siteDef.buildCost) || 0)) : 0;
}

function getBarracksSiteMaxLevel() {
  return Math.max(1, Math.floor(Number(getMaxBarracksLevel()) || 1));
}

function normalizeBarracksSiteLevel(level) {
  return Math.min(
    getBarracksSiteMaxLevel(),
    Math.max(1, Math.floor(Number(level) || 1))
  );
}

function getBarracksSiteBaseMaxHp() {
  const buildingDef = FORTRESS_BUILDING_DEFS.barracks;
  return Math.max(1, Math.floor(Number(buildingDef && buildingDef.baseMaxHp) || 260));
}

function getBarracksSiteSendIntervalTicks(level) {
  const safeLevel = normalizeBarracksSiteLevel(level);
  const seconds = Math.max(5, 30 - ((safeLevel - 1) * 10));
  return seconds * TICK_HZ;
}

function createBarracksSiteState(siteDef, options = {}) {
  const built = !!options.isBuilt;
  const level = built ? normalizeBarracksSiteLevel(options.level) : 0;
  const baseMaxHp = getBarracksSiteBaseMaxHp();
  const interval = level > 0 ? getBarracksSiteSendIntervalTicks(level) : 0;
  const maxHp = built ? baseMaxHp : 0;
  const hp = built
    ? Math.max(0, Math.min(maxHp, Math.floor(Number(options.hp) || maxHp)))
    : 0;

  return {
    barracksId: siteDef.barracksId,
    isBuilt: built,
    level,
    hp,
    maxHp,
    nextSendTick: built
      ? Math.max(1, Math.floor(Number(options.nextSendTick) || interval))
      : 0,
    costHistory: Array.isArray(options.costHistory) ? options.costHistory.slice() : [],
  };
}

function createBarracksSiteStates(_teamHpStart, legacyBarracksLevel = 1) {
  const states = {};
  const safeLegacyLevel = normalizeBarracksSiteLevel(legacyBarracksLevel);
  for (const siteDef of BARRACKS_SITE_DEFS) {
    const built = !!siteDef.startsBuilt;
    states[siteDef.barracksId] = createBarracksSiteState(siteDef, {
      isBuilt: built,
      level: built ? safeLegacyLevel : 0,
      hp: built ? getBarracksSiteBaseMaxHp() : 0,
      nextSendTick: built ? getBarracksSiteSendIntervalTicks(safeLegacyLevel) : 0,
    });
  }
  return states;
}

function syncLegacyBarracksAggregate(lane) {
  if (!lane)
    return;

  const states = lane.barracksSiteStates && typeof lane.barracksSiteStates === "object"
    ? Object.values(lane.barracksSiteStates)
    : [];
  let aggregateLevel = 1;
  for (const state of states) {
    if (!state || !state.isBuilt)
      continue;
    aggregateLevel = Math.max(aggregateLevel, normalizeBarracksSiteLevel(state.level));
  }

  lane.barracks = Object.assign({ level: aggregateLevel }, getBarracksLevelDef(aggregateLevel));
}

function ensureBarracksSiteStates(lane, game) {
  if (!lane)
    return {};

  const legacyBarracksLevel = normalizeBarracksSiteLevel(
    lane.barracks && lane.barracks.level
  );
  const legacyNextSendTick = game && Number.isInteger(game.nextBarracksSendTick)
    ? game.nextBarracksSendTick
    : null;
  const existing = lane.barracksSiteStates && typeof lane.barracksSiteStates === "object"
    ? lane.barracksSiteStates
    : {};
  const next = {};

  for (const siteDef of BARRACKS_SITE_DEFS) {
    const siteId = siteDef.barracksId;
    const current = existing[siteId];
    const shouldStartBuilt = !!siteDef.startsBuilt
      || hasOwnedBarracksUnits(lane, siteId)
      || legacyBarracksLevel >= getBarracksSiteTierRequirement(siteDef);

    if (current && typeof current === "object") {
      const built = !!current.isBuilt || hasOwnedBarracksUnits(lane, siteId);
      next[siteId] = createBarracksSiteState(siteDef, {
        isBuilt: built,
        level: built ? (current.level || legacyBarracksLevel) : 0,
        hp: current.hp,
        nextSendTick: current.nextSendTick,
        costHistory: current.costHistory,
      });
      continue;
    }

    next[siteId] = createBarracksSiteState(siteDef, {
      isBuilt: shouldStartBuilt,
      level: shouldStartBuilt ? legacyBarracksLevel : 0,
      hp: shouldStartBuilt ? getBarracksSiteBaseMaxHp() : 0,
      nextSendTick: shouldStartBuilt ? legacyNextSendTick : 0,
    });
  }

  lane.barracksSiteStates = next;
  syncLegacyBarracksAggregate(lane);
  return next;
}

function getBarracksRosterDefinition(rosterKey) {
  return BARRACKS_ROSTER_DEFS.find((entry) => entry.rosterKey === rosterKey) || null;
}

function getHeroRosterDefinition(heroKey) {
  return HERO_ROSTER_DEFS.find((entry) => entry.heroKey === heroKey) || null;
}

function getBarracksRoleSortIndex(role) {
  return BARRACKS_ROLE_SORT_ORDER[role] ?? 99;
}

function getBarracksSpawnQueueRoleSortIndex(role) {
  const index = BARRACKS_SPAWN_ROLE_ORDER.indexOf(role);
  return index >= 0 ? index : 99;
}

function resolveFortPresentation(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY, fallbackDisplayName = null, deps = {}) {
  if (typeof deps.resolveFortPresentationConfig === "function") {
    return deps.resolveFortPresentationConfig(archetypeKey, presentationKey, fallbackDisplayName);
  }

  return {
    presentationKey: presentationKey || DEFAULT_FORT_PRESENTATION_KEY,
    catalogUnitKey: archetypeKey || null,
    skinKey: null,
    portraitKey: null,
  };
}

function logBarracksRosterState(lane, reason) {
  if (!lane)
    return;

  const summary = BARRACKS_SITE_DEFS
    .map((siteDef) => {
      const siteCounts = lane.barracksSiteRosterCounts && lane.barracksSiteRosterCounts[siteDef.barracksId];
      return `${siteDef.barracksId}=${summarizeBarracksSiteCounts(siteCounts)}`;
    })
    .join(" ");
  console.log(
    `[BarracksTrace][ServerRoster] lane=${lane.laneIndex} reason='${reason}' ${summary}`
  );
}

function getBarracksSiteState(lane, barracksId, game) {
  if (!lane)
    return null;

  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return null;

  const states = ensureBarracksSiteStates(lane, game);
  return states[normalizedId] || null;
}

function describeBarracksSite(game, lane, barracksId) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return null;

  const siteDef = getBarracksSiteDef(normalizedId);
  const siteState = getBarracksSiteState(lane, normalizedId, game);
  if (!siteDef || !siteState)
    return null;

  const built = !!siteState.isBuilt;
  const level = built ? normalizeBarracksSiteLevel(siteState.level) : 0;
  const maxLevel = getBarracksSiteMaxLevel();
  const nextLevel = built ? Math.min(maxLevel, level + 1) : 1;
  const townCoreTier = getTownCoreTier(lane);
  const requiredTownCoreTier = getFortressRequiredTownCoreTier("barracks", nextLevel);
  const canBuild = !built && townCoreTier >= requiredTownCoreTier;
  const canUpgrade = built && level < maxLevel && townCoreTier >= requiredTownCoreTier;

  let buildState = FORTRESS_BUILD_STATES.locked;
  if (!built) {
    buildState = canBuild ? FORTRESS_BUILD_STATES.availableToBuild : FORTRESS_BUILD_STATES.locked;
  } else if (level >= maxLevel) {
    buildState = FORTRESS_BUILD_STATES.maxTier;
  } else if (canUpgrade) {
    buildState = FORTRESS_BUILD_STATES.upgradeAvailable;
  } else {
    buildState = FORTRESS_BUILD_STATES.built;
  }

  let lockedReason = null;
  if (!built && !canBuild) {
    lockedReason = `Requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  } else if (built && level < maxLevel && !canUpgrade) {
    lockedReason = `Upgrade requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  }

  const sendIntervalTicks = built
    ? getBarracksSiteSendIntervalTicks(level)
    : getBarracksSiteSendIntervalTicks(1);
  if (built && (!Number.isInteger(siteState.nextSendTick) || siteState.nextSendTick <= 0)) {
    const baseTick = game ? Math.floor(Number(game.tick) || 0) : 0;
    siteState.nextSendTick = baseTick + sendIntervalTicks;
  }

  const sendTimerTicksRemaining = built && game
    ? Math.max(0, Math.floor(Number(siteState.nextSendTick) || 0) - Math.floor(Number(game.tick) || 0))
    : 0;
  const nextDef = built && level < maxLevel ? getBarracksUpgradeDef(nextLevel) : null;
  const upgradeCost = built && level < maxLevel
    ? Math.max(
        0,
        Math.floor(Number(nextDef && nextDef.upgradeCost) || Number(getBarracksLevelDef(nextLevel).cost) || 0)
      )
    : 0;

  return {
    siteDef,
    siteState,
    isBuilt: built,
    level,
    maxLevel,
    buildState,
    canBuild,
    canUpgrade,
    buildCost: getBarracksSiteBuildCost(siteDef),
    upgradeCost,
    requiredTownCoreTier,
    lockedReason,
    sendIntervalTicks,
    sendTimerTicksRemaining,
    sendTimerTotalTicks: built ? sendIntervalTicks : 0,
  };
}

function getBarracksSiteLockedReason(siteDef) {
  return siteDef ? `${siteDef.displayName} is not available yet.` : "Barracks unavailable";
}

function isBarracksSiteAvailable(lane, barracksId) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  return !!(descriptor && descriptor.isBuilt);
}

function getBarracksRosterLockedReason(rosterDef) {
  if (!rosterDef)
    return "Locked";

  const buildingDef = FORTRESS_BUILDING_DEFS[rosterDef.unlockBuildingType];
  const buildingName = buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType;
  const tierName = getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier);
  if (String(buildingName || "").trim().toLowerCase() === String(tierName || "").trim().toLowerCase())
    return `${buildingName} required`;
  return `${buildingName}: ${tierName}`;
}

function getBuiltBarracksSiteTier(lane, barracksId) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  if (!descriptor || !descriptor.isBuilt)
    return 0;

  return Math.max(1, Math.floor(Number(descriptor.level) || 1));
}

function getHighestBuiltBarracksSiteTier(lane) {
  let highestTier = 0;
  for (const siteDef of BARRACKS_SITE_DEFS)
    highestTier = Math.max(highestTier, getBuiltBarracksSiteTier(lane, siteDef.barracksId));
  return highestTier;
}

function resolveBarracksRosterUnlockContext(lane, rosterDef, barracksId = null) {
  const requiredBuildingTier = Math.max(1, Math.floor(Number(rosterDef && rosterDef.requiredBuildingTier) || 1));
  const unlockBuildingType = String((rosterDef && rosterDef.unlockBuildingType) || "").trim().toLowerCase();

  if (unlockBuildingType === "barracks") {
    const unlockTier = barracksId
      ? getBuiltBarracksSiteTier(lane, barracksId)
      : getHighestBuiltBarracksSiteTier(lane);
    return {
      unlockPad: null,
      unlockPadId: null,
      unlockTier,
      unlocked: unlockTier >= requiredBuildingTier,
      buildingDef: FORTRESS_BUILDING_DEFS.barracks || null,
    };
  }

  const unlockPad = getFortressPadByBuildingType(lane, rosterDef && rosterDef.unlockBuildingType);
  const unlockTier = getHighestBuiltFortressPadTier(lane, rosterDef && rosterDef.unlockBuildingType);
  return {
    unlockPad,
    unlockPadId: unlockPad ? unlockPad.padId : null,
    unlockTier,
    unlocked: unlockTier >= requiredBuildingTier,
    buildingDef: FORTRESS_BUILDING_DEFS[rosterDef && rosterDef.unlockBuildingType] || null,
  };
}

function getHeroRosterLockedReason(heroDef) {
  if (!heroDef)
    return "Hero locked";

  const buildingDef = FORTRESS_BUILDING_DEFS[heroDef.unlockBuildingType];
  const tierName = getBuildingTierDisplayName(heroDef.unlockBuildingType, heroDef.requiredBuildingTier);
  if (buildingDef && heroDef.requiredBuildingTier > 0)
    return `${buildingDef.displayName}: ${tierName}`;
  return "Castle required";
}

function getBarracksSiteCombatTarget(lane, barracksId, deps = {}) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  if (!descriptor || !descriptor.isBuilt || !descriptor.siteState)
    return null;

  const hp = Math.max(0, Math.floor(Number(descriptor.siteState.hp) || 0));
  if (hp <= 0)
    return null;

  const pos = typeof deps.getBarracksSiteWorldPosition === "function"
    ? deps.getBarracksSiteWorldPosition(lane && lane.laneIndex, barracksId)
    : null;
  if (!pos)
    return null;

  return {
    id: `barracks_site:${descriptor.siteState.barracksId}`,
    unitId: `barracks_site:${descriptor.siteState.barracksId}`,
    laneIndex: lane.laneIndex,
    kind: "fortress_pad",
    padId: `barracks_site:${descriptor.siteState.barracksId}`,
    barracksId: descriptor.siteState.barracksId,
    buildingType: "barracks_site",
    type: "barracks",
    posX: pos.x,
    posY: pos.y,
    hp,
    maxHp: Math.max(1, Math.floor(Number(descriptor.siteState.maxHp) || 1)),
  };
}

function isHeroUnlockedForLane(lane, heroDef) {
  if (!lane || !heroDef)
    return false;

  const unlockTier = getHighestBuiltFortressPadTier(lane, heroDef.unlockBuildingType);
  return unlockTier >= Math.max(1, Math.floor(Number(heroDef.requiredBuildingTier) || 1));
}

function getHeroCooldownReadyTick(lane, heroKey) {
  if (!lane || !lane.heroCooldownReadyTicks || typeof lane.heroCooldownReadyTicks !== "object")
    return 0;
  return Math.max(0, Math.floor(Number(lane.heroCooldownReadyTicks[heroKey]) || 0));
}

function countActiveHeroDeployments(game, sourceLaneIndex, heroKey) {
  if (!game || !Array.isArray(game.lanes) || !heroKey)
    return 0;

  let total = 0;
  for (const lane of game.lanes) {
    if (!lane)
      continue;

    const collections = [lane.units, lane.spawnQueue];
    for (const collection of collections) {
      if (!Array.isArray(collection))
        continue;

      for (const unit of collection) {
        if (!unit || unit.hp <= 0 || !unit.isHero)
          continue;
        if (unit.sourceLaneIndex !== sourceLaneIndex)
          continue;
        if (unit.heroKey !== heroKey)
          continue;
        total += 1;
      }
    }
  }

  return total;
}

function countBuiltBarracksSites(lane) {
  if (!lane || !lane.barracksSiteStates || typeof lane.barracksSiteStates !== "object")
    return 0;

  let built = 0;
  for (const siteDef of BARRACKS_SITE_DEFS) {
    const state = lane.barracksSiteStates[siteDef.barracksId];
    if (state && state.isBuilt)
      built += 1;
  }
  return built;
}

function getHeroDisabledReason(game, lane, heroDef, deps = {}) {
  if (!lane || !heroDef)
    return "Hero unavailable";

  if (!isHeroUnlockedForLane(lane, heroDef))
    return getHeroRosterLockedReason(heroDef);

  if (countBuiltBarracksSites(lane) <= 0)
    return "Buy a Barracks to summon heroes";

  const cooldownReadyTick = getHeroCooldownReadyTick(lane, heroDef.heroKey);
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  if (cooldownReadyTick > currentTick) {
    const secondsRemaining = Math.max(0, Math.ceil((cooldownReadyTick - currentTick) / Math.max(1, TICK_HZ)));
    return `Cooldown ${secondsRemaining}s`;
  }

  const activeLimit = Math.max(0, Math.floor(Number(heroDef.activeLimit) || 0));
  const activeCount = countActiveHeroDeployments(game, lane.laneIndex, heroDef.heroKey);
  if (activeLimit > 0 && activeCount >= activeLimit)
    return `${heroDef.displayName} is already deployed`;

  if (!Number.isInteger(
    typeof deps.resolveTargetLaneForBarracksSend === "function"
      ? deps.resolveTargetLaneForBarracksSend(game, lane.laneIndex, "center")
      : null
  )) {
    return "Lane command target unavailable";
  }

  return null;
}

function createHeroRosterSnapshot(game, lane, deps = {}) {
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  const builtBarracksCount = countBuiltBarracksSites(lane);

  return HERO_ROSTER_DEFS
    .slice()
    .sort((a, b) => (a.sortIndex || 0) - (b.sortIndex || 0))
    .map((heroDef) => {
      const presentation = resolveFortPresentation(
        heroDef.archetypeKey,
        heroDef.presentationKey,
        heroDef.displayName,
        deps
      );
      const unlocked = isHeroUnlockedForLane(lane, heroDef);
      const cooldownReadyTick = getHeroCooldownReadyTick(lane, heroDef.heroKey);
      const cooldownTicksRemaining = Math.max(0, cooldownReadyTick - currentTick);
      const activeLimit = Math.max(0, Math.floor(Number(heroDef.activeLimit) || 0));
      const activeCount = countActiveHeroDeployments(game, lane && lane.laneIndex, heroDef.heroKey);
      const summonSourceBuildingName = getBuildingDisplayName(heroDef.summonSourceBuildingType);
      const disabledReason = getHeroDisabledReason(game, lane, heroDef, deps);

      let state = "disabled";
      if (!unlocked) {
        state = "locked";
      } else if (activeLimit > 0 && activeCount >= activeLimit) {
        state = "active";
      } else if (cooldownTicksRemaining > 0) {
        state = "cooldown";
      } else if (!disabledReason) {
        state = "ready";
      }

      return {
        heroKey: heroDef.heroKey,
        displayName: heroDef.displayName,
        role: heroDef.role,
        roleLabel: heroDef.roleLabel || "Hero",
        sortIndex: heroDef.sortIndex || 0,
        archetypeKey: heroDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: heroDef.branchKey || "heroes",
        branchLabel: heroDef.branchLabel || "Heroes",
        isHero: true,
        unlocked,
        unlockBuildingType: heroDef.unlockBuildingType,
        unlockBuildingName: getBuildingDisplayName(heroDef.unlockBuildingType),
        unlockBuildingTierName: getBuildingTierDisplayName(heroDef.unlockBuildingType, heroDef.requiredBuildingTier),
        requiredBuildingTier: heroDef.requiredBuildingTier,
        summonSourceBuildingType: heroDef.summonSourceBuildingType,
        summonSourceBuildingName,
        summonCost: Math.max(0, Math.floor(Number(heroDef.summonCost) || 0)),
        cooldownTicks: Math.max(0, Math.floor(Number(heroDef.cooldownTicks) || 0)),
        cooldownReadyTick,
        cooldownTicksRemaining,
        activeLimit,
        activeCount,
        builtBarracksCount,
        state,
        canSummon: !disabledReason,
        heroVisualStyleKey: heroDef.heroVisualStyleKey || null,
        lockedReason: unlocked ? null : getHeroRosterLockedReason(heroDef),
        disabledReason,
      };
    });
}

function getBarracksRosterBuyCost(rosterDef, deps = {}) {
  if (!rosterDef)
    return 0;

  const presentation = resolveFortPresentation(
    rosterDef.archetypeKey,
    rosterDef.presentationKey,
    rosterDef.displayName,
    deps
  );
  const unitType = typeof deps.getUnitType === "function"
    ? deps.getUnitType(presentation.catalogUnitKey)
    : null;
  const buildCost = Math.floor(Number(unitType && unitType.build_cost) || 0);
  if (buildCost > 0)
    return buildCost;

  const towerDef = typeof deps.resolveTowerDef === "function"
    ? deps.resolveTowerDef(rosterDef.archetypeKey)
    : null;
  if (towerDef && Number.isFinite(towerDef.cost) && towerDef.cost > 0)
    return Math.floor(towerDef.cost);

  const unitDef = typeof deps.resolveUnitDef === "function"
    ? deps.resolveUnitDef(rosterDef.archetypeKey)
    : null;
  const sendCost = Math.max(1, Math.floor(Number(unitDef && unitDef.cost) || 1));
  return Math.max(5, sendCost * 5);
}

function getBarracksRosterSellRefund(rosterDef, deps = {}) {
  const buyCost = getBarracksRosterBuyCost(rosterDef, deps);
  if (buyCost <= 0)
    return 0;
  return Math.max(1, Math.floor((buyCost * BARRACKS_ROSTER_REFUND_PCT) / 100));
}

function createBarracksSiteRosterSnapshot(game, lane, barracksId, deps = {}) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return [];

  const descriptor = describeBarracksSite(game, lane, normalizedId);
  if (!descriptor)
    return [];

  const siteCounts = getBarracksSiteCounts(lane, normalizedId) || {};
  const siteBuilt = !!descriptor.isBuilt;

  return BARRACKS_ROSTER_DEFS
    .slice()
    .sort((a, b) => {
      const roleA = getBarracksRoleSortIndex(a.role);
      const roleB = getBarracksRoleSortIndex(b.role);
      if (roleA !== roleB)
        return roleA - roleB;
      return (a.sortIndex || 0) - (b.sortIndex || 0);
    })
    .map((rosterDef) => {
      const presentation = resolveFortPresentation(
        rosterDef.archetypeKey,
        rosterDef.presentationKey,
        rosterDef.displayName,
        deps
      );
      const unlockContext = resolveBarracksRosterUnlockContext(lane, rosterDef, normalizedId);
      const unlocked = siteBuilt && unlockContext.unlocked;
      const buildingDef = unlockContext.buildingDef;
      const buyCost = getBarracksRosterBuyCost(rosterDef, deps);
      const sellRefund = getBarracksRosterSellRefund(rosterDef, deps);
      const ownedCount = Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));

      return {
        rosterKey: rosterDef.rosterKey,
        displayName: rosterDef.displayName,
        role: rosterDef.role,
        roleLabel: rosterDef.role.charAt(0).toUpperCase() + rosterDef.role.slice(1),
        sortIndex: rosterDef.sortIndex || 0,
        archetypeKey: rosterDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: rosterDef.branchKey || "",
        branchLabel: rosterDef.branchLabel || getBuildingBranchLabel(rosterDef.productionBuildingType),
        productionBuildingType: rosterDef.productionBuildingType,
        productionBuildingName: getBuildingDisplayName(rosterDef.productionBuildingType),
        tier: Math.max(1, Math.floor(Number(rosterDef.tier) || 1)),
        ownedCount,
        buyCost,
        sellRefund,
        unlocked,
        unlockBuildingType: rosterDef.unlockBuildingType,
        unlockBuildingName: buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType,
        unlockBuildingTierName: getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier),
        requiredBuildingTier: rosterDef.requiredBuildingTier,
        unlockPadId: unlockContext.unlockPadId,
        barracksId: normalizedId,
        lockedReason: unlocked
          ? null
          : !siteBuilt
            ? descriptor.lockedReason || "Buy Building first"
            : getBarracksRosterLockedReason(rosterDef),
      };
    });
}

function createBarracksSiteSnapshot(game, lane, barracksId, deps = {}) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return null;

  const descriptor = describeBarracksSite(game, lane, normalizedId);
  if (!descriptor)
    return null;

  const siteState = descriptor.siteState;
  const hp = descriptor.isBuilt ? Math.max(0, Math.floor(Number(siteState.hp) || 0)) : 0;
  const maxHp = descriptor.isBuilt ? Math.max(0, Math.floor(Number(siteState.maxHp) || 0)) : 0;
  const allegianceKey = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(lane)
    : null;

  return {
    barracksId: normalizedId,
    allegianceKey,
    ownerLaneIndex: lane && Number.isInteger(lane.laneIndex) ? lane.laneIndex : -1,
    displayName: descriptor.siteDef.displayName,
    slot: descriptor.siteDef.slot,
    sortIndex: descriptor.siteDef.sortIndex,
    requiredBarracksTier: getBarracksSiteTierRequirement(descriptor.siteDef),
    available: descriptor.isBuilt,
    isBuilt: descriptor.isBuilt,
    level: descriptor.level,
    maxLevel: descriptor.maxLevel,
    buildState: descriptor.buildState,
    canBuild: descriptor.canBuild,
    canUpgrade: descriptor.canUpgrade,
    buildCost: descriptor.buildCost,
    upgradeCost: descriptor.upgradeCost,
    requiredTownCoreTier: descriptor.requiredTownCoreTier,
    requiredTownCoreTierName: getBuildingTierDisplayName("town_core", descriptor.requiredTownCoreTier),
    sendIntervalTicks: descriptor.sendIntervalTicks,
    sendTimerTicksRemaining: descriptor.sendTimerTicksRemaining,
    sendTimerTotalTicks: descriptor.sendTimerTotalTicks,
    lockedReason: descriptor.lockedReason,
    hp,
    maxHp,
    roster: createBarracksSiteRosterSnapshot(game, lane, normalizedId, deps),
  };
}

function createBarracksRosterSnapshot(game, lane, deps = {}) {
  return BARRACKS_ROSTER_DEFS
    .slice()
    .sort((a, b) => {
      const roleA = getBarracksRoleSortIndex(a.role);
      const roleB = getBarracksRoleSortIndex(b.role);
      if (roleA !== roleB)
        return roleA - roleB;
      return (a.sortIndex || 0) - (b.sortIndex || 0);
    })
    .map((rosterDef) => {
      const unlockContext = resolveBarracksRosterUnlockContext(lane, rosterDef);
      const unlocked = unlockContext.unlocked;
      const buildingDef = unlockContext.buildingDef;
      const buyCost = getBarracksRosterBuyCost(rosterDef, deps);
      const sellRefund = getBarracksRosterSellRefund(rosterDef, deps);
      let ownedCount = 0;
      for (const siteDef of BARRACKS_SITE_DEFS) {
        const siteCounts = getBarracksSiteCounts(lane, siteDef.barracksId) || {};
        ownedCount += Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
      }
      const presentation = resolveFortPresentation(
        rosterDef.archetypeKey,
        rosterDef.presentationKey,
        rosterDef.displayName,
        deps
      );

      return {
        rosterKey: rosterDef.rosterKey,
        displayName: rosterDef.displayName,
        role: rosterDef.role,
        roleLabel: rosterDef.role.charAt(0).toUpperCase() + rosterDef.role.slice(1),
        sortIndex: rosterDef.sortIndex || 0,
        archetypeKey: rosterDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: rosterDef.branchKey || "",
        branchLabel: rosterDef.branchLabel || getBuildingBranchLabel(rosterDef.productionBuildingType),
        productionBuildingType: rosterDef.productionBuildingType,
        productionBuildingName: getBuildingDisplayName(rosterDef.productionBuildingType),
        tier: Math.max(1, Math.floor(Number(rosterDef.tier) || 1)),
        ownedCount,
        buyCost,
        sellRefund,
        unlocked,
        unlockBuildingType: rosterDef.unlockBuildingType,
        unlockBuildingName: buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType,
        unlockBuildingTierName: getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier),
        requiredBuildingTier: rosterDef.requiredBuildingTier,
        unlockPadId: unlockContext.unlockPadId,
        lockedReason: unlocked ? null : getBarracksRosterLockedReason(rosterDef),
      };
    });
}

function buildBarracksRosterSpawnEntries(game, lane, barracksId = null, deps = {}) {
  if (!game || !lane)
    return [];

  const spawnEntries = [];
  const siteDefs = barracksId
    ? [getBarracksSiteDef(barracksId)].filter(Boolean)
    : BARRACKS_SITE_DEFS;
  for (const siteDef of siteDefs) {
    const descriptor = describeBarracksSite(game, lane, siteDef.barracksId);
    if (!descriptor || !descriptor.isBuilt)
      continue;

    const rosterEntries = createBarracksSiteRosterSnapshot(game, lane, siteDef.barracksId, deps)
      .filter((entry) => entry.unlocked && entry.ownedCount > 0)
      .sort((a, b) => {
        const roleA = getBarracksSpawnQueueRoleSortIndex(a.role);
        const roleB = getBarracksSpawnQueueRoleSortIndex(b.role);
        if (roleA !== roleB)
          return roleA - roleB;
        return (a.sortIndex || 0) - (b.sortIndex || 0);
      });

    for (const entry of rosterEntries) {
      const rosterDef = getBarracksRosterDefinition(entry.rosterKey);
      const presentation = resolveFortPresentation(
        rosterDef && rosterDef.archetypeKey,
        rosterDef && rosterDef.presentationKey,
        entry.displayName,
        deps
      );
      for (let ownedIndex = 0; ownedIndex < entry.ownedCount; ownedIndex += 1) {
        spawnEntries.push({
          unitType: rosterDef && rosterDef.archetypeKey ? rosterDef.archetypeKey : entry.unitTypeKey,
          count: 1,
          source: "barracks_roster",
          barracksId: siteDef.barracksId,
          rosterKey: entry.rosterKey,
          archetypeKey: rosterDef && rosterDef.archetypeKey ? rosterDef.archetypeKey : null,
          presentationKey: presentation.presentationKey,
          role: entry.role,
          sortIndex: entry.sortIndex || 0,
          sourceLaneIndex: lane.laneIndex,
          sourceTeam: lane.team || null,
          skinKey: presentation.skinKey || null,
        });
      }
    }
  }

  return spawnEntries;
}

function getBarracksSpawnColumns(role, unitCount) {
  const safeCount = Math.max(0, Math.floor(Number(unitCount) || 0));
  if (safeCount <= 1)
    return 1;
  if (safeCount === 2)
    return 2;
  if (safeCount === 3)
    return 3;

  switch (role) {
    case "melee":
      return Math.min(4, safeCount);
    case "support":
    case "ranged":
      return Math.min(3, safeCount);
    default:
      return Math.min(2, safeCount);
  }
}

function spawnBarracksRosterLayout(game, lane, pendingEntries, options = null, deps = {}) {
  if (!game || !lane || !Array.isArray(pendingEntries) || pendingEntries.length === 0)
    return;

  const safeOptions = options && typeof options === "object" ? options : null;
  const gridWidth = Math.max(1, Math.floor(Number(deps.gridWidth) || 11));
  if (typeof deps.spawnWaveUnit !== "function")
    return;

  const entriesByRole = new Map();
  for (const role of BARRACKS_SPAWN_ROLE_ORDER)
    entriesByRole.set(role, []);
  for (const entry of pendingEntries) {
    if (!entry)
      continue;
    const roleEntries = entriesByRole.get(entry.role) || [];
    roleEntries.push(entry);
    entriesByRole.set(entry.role, roleEntries);
  }

  let nextRoleRow = Math.ceil(lane.spawnQueue.length / gridWidth);
  for (const role of BARRACKS_SPAWN_ROLE_ORDER) {
    const roleEntries = (entriesByRole.get(role) || [])
      .slice()
      .sort((a, b) => {
        const sortA = Math.floor(Number(a.sortIndex) || 0);
        const sortB = Math.floor(Number(b.sortIndex) || 0);
        if (sortA !== sortB)
          return sortA - sortB;
        return String(a.rosterKey || "").localeCompare(String(b.rosterKey || ""));
      });
    if (roleEntries.length === 0)
      continue;

    const columns = getBarracksSpawnColumns(role, roleEntries.length);
    const startCol = Math.max(0, Math.floor((gridWidth - columns) / 2));

    for (let i = 0; i < roleEntries.length; i += 1) {
      const rowOffset = Math.floor(i / columns);
      const columnOffset = i % columns;
      const spawnIndex = ((nextRoleRow + rowOffset) * gridWidth) + startCol + columnOffset;
      const entry = roleEntries[i];
      const combatRole = typeof deps.normalizeCombatRole === "function"
        ? deps.normalizeCombatRole(entry.combatRole || entry.role)
        : null;
      const resolvedCombatRole = combatRole
        || (typeof deps.resolveUnitCombatRole === "function" ? deps.resolveUnitCombatRole(entry) : null);
      deps.spawnWaveUnit(game, lane, {
        unit_type: entry.unitType,
        spawn_qty: 1,
        hp_mult: 1,
        dmg_mult: 1,
        speed_mult: 1,
        sourceLaneIndex: entry.sourceLaneIndex,
        sourceTeam: entry.sourceTeam,
        sourceBarracksId: entry.barracksId,
        rosterKey: entry.rosterKey || null,
        role: entry.role || null,
        combatRole: resolvedCombatRole,
        skinKey: entry.skinKey || null,
        spawnIndex,
      }, safeOptions || undefined);
    }

    nextRoleRow += Math.ceil(roleEntries.length / columns);
  }
}

function spawnScheduledBarracksRoster(game, lane, barracksId = null, deps = {}) {
  if (!game || !lane || lane.eliminated)
    return 0;

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!normalizedBarracksId) {
    if (deps.log && typeof deps.log.error === "function") {
      deps.log.error("[BarracksTrace][ServerSpawn] rejected", {
        reason: "Missing or invalid barracksId",
        sourceLaneIndex: lane.laneIndex,
        requestedBarracksId: barracksId || null,
      });
    }
    return 0;
  }

  const targetLaneIdx = typeof deps.resolveTargetLaneForBarracksSend === "function"
    ? deps.resolveTargetLaneForBarracksSend(game, lane.laneIndex, normalizedBarracksId)
    : null;
  if (targetLaneIdx === null) {
    if (deps.log && typeof deps.log.error === "function") {
      deps.log.error("[BarracksTrace][ServerSpawn] rejected", {
        reason: "Unable to resolve target lane for barracks send",
        sourceLaneIndex: lane.laneIndex,
        barracksId: normalizedBarracksId,
      });
    }
    return 0;
  }

  const targetLane = game.lanes[targetLaneIdx];
  if (!targetLane) {
    if (deps.log && typeof deps.log.error === "function") {
      deps.log.error("[BarracksTrace][ServerSpawn] rejected", {
        reason: "Resolved target lane is missing",
        sourceLaneIndex: lane.laneIndex,
        targetLaneIndex: targetLaneIdx,
        barracksId: normalizedBarracksId,
      });
    }
    return 0;
  }

  const spawnEntries = buildBarracksRosterSpawnEntries(game, lane, normalizedBarracksId, deps);
  if (spawnEntries.length === 0)
    return 0;

  spawnBarracksRosterLayout(game, targetLane, spawnEntries, null, deps);
  lane.totalSendCount += spawnEntries.length;
  lane.sendCountThisRound += spawnEntries.length;
  return spawnEntries.length;
}

function applyBarracksSiteBuildAction(game, lane, barracksId) {
  const descriptor = describeBarracksSite(game, lane, barracksId);
  if (!descriptor)
    return { ok: false, reason: "Unknown barracks" };
  if (!descriptor.canBuild)
    return { ok: false, reason: descriptor.lockedReason || "Building is not available" };
  if (lane.gold < descriptor.buildCost)
    return { ok: false, reason: "Not enough gold" };

  const siteState = descriptor.siteState;
  const nextLevel = 1;
  const interval = getBarracksSiteSendIntervalTicks(nextLevel);
  const maxHp = getBarracksSiteBaseMaxHp();

  lane.gold -= descriptor.buildCost;
  lane.totalBuildSpend += descriptor.buildCost;
  lane.buildSpendThisRound += descriptor.buildCost;
  siteState.isBuilt = true;
  siteState.level = nextLevel;
  siteState.maxHp = maxHp;
  siteState.hp = maxHp;
  siteState.nextSendTick = Math.floor(Number(game && game.tick) || 0) + interval;
  if (!Array.isArray(siteState.costHistory))
    siteState.costHistory = [];
  siteState.costHistory.push({ cost: descriptor.buildCost });
  syncLegacyBarracksAggregate(lane);
  return { ok: true };
}

function applyBarracksSiteUpgradeAction(game, lane, barracksId) {
  const descriptor = describeBarracksSite(game, lane, barracksId);
  if (!descriptor)
    return { ok: false, reason: "Unknown barracks" };
  if (!descriptor.isBuilt)
    return { ok: false, reason: "Buy Building first" };
  if (!descriptor.canUpgrade)
    return { ok: false, reason: descriptor.lockedReason || "Barracks upgrade unavailable" };
  if (lane.gold < descriptor.upgradeCost)
    return { ok: false, reason: "Not enough gold" };

  const siteState = descriptor.siteState;
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  const currentRemaining = Math.max(0, Math.floor(Number(siteState.nextSendTick) || 0) - currentTick);
  const nextLevel = normalizeBarracksSiteLevel(descriptor.level + 1);
  const nextInterval = getBarracksSiteSendIntervalTicks(nextLevel);

  lane.gold -= descriptor.upgradeCost;
  lane.totalBuildSpend += descriptor.upgradeCost;
  lane.buildSpendThisRound += descriptor.upgradeCost;
  siteState.level = nextLevel;
  siteState.maxHp = Math.max(getBarracksSiteBaseMaxHp(), Math.floor(Number(siteState.maxHp) || 0));
  siteState.hp = siteState.maxHp;
  siteState.nextSendTick = currentTick + Math.max(1, Math.min(currentRemaining || nextInterval, nextInterval));
  if (!Array.isArray(siteState.costHistory))
    siteState.costHistory = [];
  siteState.costHistory.push({ cost: descriptor.upgradeCost });
  syncLegacyBarracksAggregate(lane);
  return { ok: true };
}

function buyBarracksUnit(game, laneIndex, lane, rosterKey, barracksId, count = 1, deps = {}) {
  const safeCount = Math.min(25, Math.max(1, Math.floor(Number(count) || 1)));
  console.log(
    `[BarracksTrace][ServerBuy] action='buy_barracks_unit' lane=${laneIndex} `
    + `requestedBarracksId='${barracksId}' resolvedBarracksId='${normalizeBarracksSiteId(barracksId)}' `
    + `rosterKey='${rosterKey}' count=${safeCount}`
  );

  const rosterDef = getBarracksRosterDefinition(rosterKey);
  if (!rosterDef)
    return { ok: false, reason: "Unknown barracks unit" };

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!normalizedBarracksId)
    return { ok: false, reason: "Missing or invalid barracksId" };

  const siteSnapshot = createBarracksSiteSnapshot(game, lane, normalizedBarracksId, deps);
  if (!siteSnapshot)
    return { ok: false, reason: "Unknown barracks" };
  if (!siteSnapshot.isBuilt)
    return { ok: false, reason: siteSnapshot.lockedReason || "Buy Building first" };

  const rosterEntry = Array.isArray(siteSnapshot.roster)
    ? siteSnapshot.roster.find((entry) => entry.rosterKey === rosterKey)
    : null;
  if (!rosterEntry)
    return { ok: false, reason: "Unknown barracks unit" };
  if (!rosterEntry.unlocked)
    return { ok: false, reason: rosterEntry.lockedReason || "Unit is locked" };

  const totalCost = Math.max(0, rosterEntry.buyCost * safeCount);
  if (lane.gold < totalCost)
    return { ok: false, reason: "Not enough gold" };

  lane.gold -= totalCost;
  lane.totalBuildSpend += totalCost;
  lane.buildSpendThisRound += totalCost;
  const siteCounts = getBarracksSiteCounts(lane, normalizedBarracksId);
  if (!siteCounts)
    return { ok: false, reason: "Missing or invalid barracksId" };
  siteCounts[rosterKey] = Math.max(0, Math.floor(Number(siteCounts[rosterKey]) || 0) + safeCount);
  console.log(
    `[BarracksTrace][ServerBuy] lane=${laneIndex} barracksId='${normalizedBarracksId}' `
    + `rosterKey='${rosterKey}' ownedCount=${siteCounts[rosterKey]} totalCost=${totalCost}`
  );
  logBarracksRosterState(lane, `after_buy:${normalizedBarracksId}:${rosterKey}`);
  return { ok: true };
}

function sellBarracksUnit(laneIndex, lane, rosterKey, barracksId, deps = {}) {
  console.log(
    `[BarracksTrace][ServerBuy] action='sell_barracks_unit' lane=${laneIndex} `
    + `requestedBarracksId='${barracksId}' resolvedBarracksId='${normalizeBarracksSiteId(barracksId)}' `
    + `rosterKey='${rosterKey}'`
  );

  const rosterDef = getBarracksRosterDefinition(rosterKey);
  if (!rosterDef)
    return { ok: false, reason: "Unknown barracks unit" };

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!normalizedBarracksId)
    return { ok: false, reason: "Missing or invalid barracksId" };

  const siteCounts = getBarracksSiteCounts(lane, normalizedBarracksId);
  if (!siteCounts)
    return { ok: false, reason: "Missing or invalid barracksId" };

  const currentOwned = Math.max(0, Math.floor(Number(siteCounts[rosterKey]) || 0));
  if (currentOwned <= 0)
    return { ok: false, reason: "No owned units to sell" };

  siteCounts[rosterKey] = currentOwned - 1;
  lane.gold += getBarracksRosterSellRefund(rosterDef, deps);
  console.log(
    `[BarracksTrace][ServerBuy] lane=${laneIndex} barracksId='${normalizedBarracksId}' `
    + `rosterKey='${rosterKey}' ownedCount=${siteCounts[rosterKey]}`
  );
  logBarracksRosterState(lane, `after_sell:${normalizedBarracksId}:${rosterKey}`);
  return { ok: true };
}

function deployBarracksHero(game, laneIndex, lane, heroKey, barracksId, deps = {}) {
  const heroDef = getHeroRosterDefinition(heroKey);
  if (!heroDef)
    return { ok: false, reason: "Unknown hero" };

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!normalizedBarracksId)
    return { ok: false, reason: "Missing or invalid barracksId" };

  const siteDescriptor = describeBarracksSite(game, lane, normalizedBarracksId);
  if (!siteDescriptor)
    return { ok: false, reason: "Unknown barracks" };
  if (!siteDescriptor.isBuilt)
    return { ok: false, reason: siteDescriptor.lockedReason || "Buy Building first" };

  if (!isHeroUnlockedForLane(lane, heroDef))
    return { ok: false, reason: getHeroRosterLockedReason(heroDef) };

  const disabledReason = getHeroDisabledReason(game, lane, heroDef, deps);
  if (disabledReason)
    return { ok: false, reason: disabledReason };

  const summonCost = Math.max(0, Math.floor(Number(heroDef.summonCost) || 0));
  if (lane.gold < summonCost)
    return { ok: false, reason: "Not enough gold" };

  const targetLaneIdx = typeof deps.resolveTargetLaneForBarracksSend === "function"
    ? deps.resolveTargetLaneForBarracksSend(game, laneIndex, normalizedBarracksId)
    : null;
  if (targetLaneIdx === null)
    return { ok: false, reason: "Lane command target unavailable" };

  const targetLane = game.lanes[targetLaneIdx];
  if (!targetLane)
    return { ok: false, reason: "Lane command target unavailable" };
  if (typeof deps.spawnWaveUnit !== "function")
    return { ok: false, reason: "Barracks hero spawner unavailable" };

  lane.gold -= summonCost;
  lane.totalSendSpend += summonCost;
  lane.sendSpendThisRound += summonCost;
  lane.totalSendCount += 1;
  lane.sendCountThisRound += 1;
  lane.heroCooldownReadyTicks[heroDef.heroKey] = Math.floor(Number(game && game.tick) || 0)
    + Math.max(0, Math.floor(Number(heroDef.cooldownTicks) || 0));
  const presentation = resolveFortPresentation(
    heroDef.archetypeKey,
    heroDef.presentationKey,
    heroDef.displayName,
    deps
  );
  const normalizedCombatRole = typeof deps.normalizeCombatRole === "function"
    ? deps.normalizeCombatRole(heroDef.spawnRole || heroDef.role)
    : null;

  deps.spawnWaveUnit(game, targetLane, {
    unit_type: heroDef.archetypeKey,
    hp_mult: 1,
    dmg_mult: 1,
    speed_mult: 1,
    sourceLaneIndex: laneIndex,
    sourceTeam: lane.team || null,
    sourceBarracksId: normalizedBarracksId,
    archetypeKey: heroDef.archetypeKey,
    presentationKey: presentation.presentationKey,
    skinKey: presentation.skinKey || null,
    isHero: true,
    heroKey: heroDef.heroKey,
    heroVisualStyleKey: heroDef.heroVisualStyleKey || null,
    role: heroDef.role || null,
    combatRole: normalizedCombatRole || deps.defaultHeroCombatRole || "sword",
  });

  return { ok: true };
}

function seedStartingCombatTestMilitia(game, lane, count, deps = {}) {
  const safeCount = Math.max(0, Math.floor(Number(count) || 0));
  if (!game || !lane || safeCount <= 0)
    return 0;

  const rosterDef = getBarracksRosterDefinition(STARTING_COMBAT_TEST_MILITIA_ROSTER_KEY);
  if (!rosterDef)
    return 0;

  const presentation = resolveFortPresentation(
    rosterDef.archetypeKey,
    rosterDef.presentationKey,
    rosterDef.displayName,
    deps
  );
  const barracksRosterSource = deps.spawnSourceTypes && deps.spawnSourceTypes.BARRACKS_ROSTER
    ? deps.spawnSourceTypes.BARRACKS_ROSTER
    : "barracks_roster";
  const pendingEntries = [];
  for (let i = 0; i < safeCount; i += 1) {
    pendingEntries.push({
      unitType: rosterDef.archetypeKey || presentation.catalogUnitKey,
      count: 1,
      source: barracksRosterSource,
      barracksId: STARTING_COMBAT_TEST_BARRACKS_ID,
      rosterKey: rosterDef.rosterKey,
      archetypeKey: rosterDef.archetypeKey,
      presentationKey: presentation.presentationKey,
      role: rosterDef.role,
      sortIndex: rosterDef.sortIndex || 0,
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team || null,
      skinKey: presentation.skinKey || null,
    });
  }

  spawnBarracksRosterLayout(game, lane, pendingEntries, { allowUnbuiltBarracks: true }, deps);
  return pendingEntries.length;
}

module.exports = {
  TICK_HZ,
  BARRACKS_LEVEL_ONE_SPEED_MULT,
  SPEED_UPGRADE_STEP,
  BARRACKS_COST_BASE,
  BARRACKS_REQ_INCOME_BASE,
  BARRACKS_ROSTER_REFUND_PCT,
  STARTING_COMBAT_TEST_BARRACKS_ID,
  STARTING_COMBAT_TEST_MILITIA_ROSTER_KEY,
  BARRACKS_ROLE_SORT_ORDER,
  BARRACKS_SPAWN_ROLE_ORDER,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  HERO_ROSTER_DEFS,
  MARKET_ROSTER_DEFS,
  getBarracksLevelDef,
  getBarracksSpeedMultForLevel,
  getBarracksSpeedMult,
  createBarracksRosterCounts,
  createBarracksSiteRosterCounts,
  getBarracksSiteDef,
  normalizeBarracksSiteId,
  summarizeBarracksSiteCounts,
  summarizeBarracksSiteRosterEntries,
  getBarracksSiteCounts,
  hasOwnedBarracksUnits,
  getBarracksSiteTierRequirement,
  getBarracksSiteBuildCost,
  getBarracksSiteMaxLevel,
  normalizeBarracksSiteLevel,
  getBarracksSiteBaseMaxHp,
  getBarracksSiteSendIntervalTicks,
  createBarracksSiteState,
  createBarracksSiteStates,
  syncLegacyBarracksAggregate,
  ensureBarracksSiteStates,
  resolveFortPresentation,
  logBarracksRosterState,
  getBarracksSiteState,
  describeBarracksSite,
  getBarracksSiteLockedReason,
  isBarracksSiteAvailable,
  getBarracksRosterLockedReason,
  getBuiltBarracksSiteTier,
  getHighestBuiltBarracksSiteTier,
  resolveBarracksRosterUnlockContext,
  getBarracksRosterDefinition,
  getHeroRosterDefinition,
  getHeroRosterLockedReason,
  getBarracksSiteCombatTarget,
  isHeroUnlockedForLane,
  getHeroCooldownReadyTick,
  countActiveHeroDeployments,
  countBuiltBarracksSites,
  getHeroDisabledReason,
  createHeroRosterSnapshot,
  getBarracksRosterBuyCost,
  getBarracksRosterSellRefund,
  createBarracksSiteRosterSnapshot,
  createBarracksSiteSnapshot,
  createBarracksRosterSnapshot,
  getBarracksRoleSortIndex,
  getBarracksSpawnQueueRoleSortIndex,
  buildBarracksRosterSpawnEntries,
  getBarracksSpawnColumns,
  spawnBarracksRosterLayout,
  spawnScheduledBarracksRoster,
  applyBarracksSiteBuildAction,
  applyBarracksSiteUpgradeAction,
  buyBarracksUnit,
  sellBarracksUnit,
  deployBarracksHero,
  seedStartingCombatTestMilitia,
};
