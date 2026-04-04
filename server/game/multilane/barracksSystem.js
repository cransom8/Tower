"use strict";

const { DEFAULT_FORT_PRESENTATION_KEY } = require("../fortUnitCatalog");
const { logSpawnAuditLine } = require("./spawnAuditLogging");
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
  getLaneBuildingUpgradePurchaseCount,
  hasLaneBuildingUpgrade,
} = require("./fortressSystem");

const TICK_HZ = 20;
const BARRACKS_CONSTRUCTION_KINDS = Object.freeze({
  build: "build",
  upgrade: "upgrade",
});
const BARRACKS_LEVEL_ONE_SPEED_MULT = 0.50;
const SPEED_UPGRADE_STEP = 0.25;
const BARRACKS_COST_BASE = 100;
const BARRACKS_REQ_INCOME_BASE = 8;
const BARRACKS_ROSTER_REFUND_PCT = 70;
const STARTING_COMBAT_TEST_BARRACKS_ID = "center";
const STARTING_COMBAT_TEST_MILITIA_ROSTER_KEY = "militia";
const DEFAULT_BARRACKS_BUILD_DURATION_TICKS = 12 * TICK_HZ;
const DEFAULT_BARRACKS_UPGRADE_DURATION_TICKS = 14 * TICK_HZ;
const MARKET_MAX_OWNED = 10;
const MARKET_CAPACITY_COST = 1;
const FOOD_LIMIT_BY_TIER = Object.freeze({
  1: 20,
  2: 40,
  3: 60,
});

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
    displayName: "Center Barracks",
    slot: "center",
    sortIndex: 0,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    requiredTownCoreTierByLevel: Object.freeze({
      1: 1,
      2: 2,
      3: 3,
      4: 4,
      5: 4,
    }),
  },
  {
    barracksId: "left",
    displayName: "Left Barracks",
    slot: "left",
    sortIndex: 1,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    requiredTownCoreTierByLevel: Object.freeze({
      1: 2,
      2: 3,
      3: 4,
      4: 4,
      5: 4,
    }),
  },
  {
    barracksId: "right",
    displayName: "Right Barracks",
    slot: "right",
    sortIndex: 2,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    requiredTownCoreTierByLevel: Object.freeze({
      1: 3,
      2: 3,
      3: 4,
      4: 4,
      5: 4,
    }),
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
  { rosterKey: "mage", displayName: "Mage", role: "ranged", branchKey: "arcane", branchLabel: "Mage Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 1, tier: 1, sortIndex: 140 },
  { rosterKey: "wizard", displayName: "Wizard", role: "ranged", branchKey: "arcane", branchLabel: "Mage Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 2, tier: 2, sortIndex: 150 },
  { rosterKey: "thaumaturge", displayName: "Thaumaturge", role: "ranged", branchKey: "arcane", branchLabel: "Mage Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 3, tier: 3, sortIndex: 160 },
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
    routeStartBuildingType: "market",
    routeEndBuildingType: "trade_outpost",
    routeEndBuildingName: "Beast Lair",
    nextUnitKey: "settler",
    description: "Starter market contract that adds 4 gold on every shared income cycle.",
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
    routeStartBuildingType: "market",
    routeEndBuildingType: "trade_outpost",
    routeEndBuildingName: "Beast Lair",
    nextUnitKey: "trader",
    description: "Mid-tier market contract that replaces Peasant income and adds 7 gold on every shared income cycle.",
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
    routeStartBuildingType: "market",
    routeEndBuildingType: "trade_outpost",
    routeEndBuildingName: "Beast Lair",
    nextUnitKey: null,
    description: "Top-tier market contract that replaces lower tiers and adds 10 gold on every shared income cycle.",
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

function getFoodLimitForTier(tier) {
  const safeTier = Math.max(0, Math.floor(Number(tier) || 0));
  if (safeTier <= 0)
    return 0;
  if (safeTier === 1)
    return FOOD_LIMIT_BY_TIER[1];
  if (safeTier === 2)
    return FOOD_LIMIT_BY_TIER[2];
  return FOOD_LIMIT_BY_TIER[3];
}

function getFoodCostForTier(tier) {
  return Math.min(3, Math.max(1, Math.floor(Number(tier) || 1)));
}

function getBarracksRosterFoodCost(rosterDef) {
  return getFoodCostForTier(rosterDef && rosterDef.tier);
}

function getMarketRosterFoodCost(unitDef) {
  return unitDef ? MARKET_CAPACITY_COST : 0;
}

function buildFoodLimitLockedReason(buildingName, foodUsed, foodLimit, foodNeeded = 0) {
  const safeName = String(buildingName || "Building").trim() || "Building";
  const safeUsed = Math.max(0, Math.floor(Number(foodUsed) || 0));
  const safeLimit = Math.max(0, Math.floor(Number(foodLimit) || 0));
  const safeNeeded = Math.max(0, Math.floor(Number(foodNeeded) || 0));
  const remainingFood = Math.max(0, safeLimit - safeUsed);
  if (safeLimit <= 0)
    return `${safeName} food limit is unavailable.`;
  if (safeNeeded > 0 && remainingFood > 0 && safeNeeded > remainingFood)
    return `${safeName} needs ${safeNeeded - remainingFood} more food space (${safeUsed}/${safeLimit}).`;
  return `${safeName} food limit reached (${safeUsed}/${safeLimit}).`;
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

function createMarketRosterCounts() {
  const counts = {};
  for (const def of MARKET_ROSTER_DEFS)
    counts[def.unitKey] = 0;
  return counts;
}

function getBarracksSiteDef(barracksId) {
  return BARRACKS_SITE_DEFS.find((entry) => entry && entry.barracksId === barracksId) || null;
}

function getBarracksRosterDefinition(rosterKey) {
  return BARRACKS_ROSTER_DEFS.find((entry) => entry.rosterKey === rosterKey) || null;
}

function getHeroRosterDefinition(heroKey) {
  return HERO_ROSTER_DEFS.find((entry) => entry.heroKey === heroKey) || null;
}

function getMarketRosterDefinition(unitKey) {
  return MARKET_ROSTER_DEFS.find((entry) => entry && entry.unitKey === unitKey) || null;
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

function getBarracksSiteFoodState(lane, barracksId, level = 0) {
  const siteCounts = getBarracksSiteCounts(lane, barracksId) || {};
  let foodUsed = 0;
  for (const rosterDef of BARRACKS_ROSTER_DEFS) {
    if (!rosterDef)
      continue;
    const ownedCount = Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
    if (ownedCount <= 0)
      continue;
    foodUsed += ownedCount * getBarracksRosterFoodCost(rosterDef);
  }

  const foodLimit = getFoodLimitForTier(level);
  return {
    foodLimit,
    foodUsed,
    foodRemaining: Math.max(0, foodLimit - foodUsed),
    isAtFoodLimit: foodLimit > 0 && foodUsed >= foodLimit,
  };
}

function getMarketRosterCounts(lane) {
  if (!lane)
    return null;
  if (!lane.marketRosterCounts || typeof lane.marketRosterCounts !== "object")
    lane.marketRosterCounts = createMarketRosterCounts();
  return lane.marketRosterCounts;
}

function getMarketFoodState(lane, marketTier = null) {
  const marketCounts = getMarketRosterCounts(lane) || {};
  let foodUsed = 0;
  for (const unitDef of MARKET_ROSTER_DEFS) {
    if (!unitDef)
      continue;
    const ownedCount = Math.max(0, Math.floor(Number(marketCounts[unitDef.unitKey]) || 0));
    if (ownedCount <= 0)
      continue;
    foodUsed += ownedCount * getMarketRosterFoodCost(unitDef);
  }

  const resolvedTier = Number.isFinite(Number(marketTier))
    ? Math.max(0, Math.floor(Number(marketTier) || 0))
    : Math.max(
      0,
      Math.floor(Number(getFortressPadByBuildingType(lane, "market")?.tier) || 0)
    );
  const foodLimit = resolvedTier > 0 ? MARKET_MAX_OWNED : 0;
  return {
    foodLimit,
    foodUsed,
    foodRemaining: Math.max(0, foodLimit - foodUsed),
    isAtFoodLimit: foodLimit > 0 && foodUsed >= foodLimit,
  };
}

function getTotalOwnedMarketWorkers(lane) {
  const marketCounts = getMarketRosterCounts(lane);
  if (!marketCounts)
    return 0;

  let total = 0;
  for (const unitDef of MARKET_ROSTER_DEFS) {
    if (!unitDef)
      continue;
    total += Math.max(0, Math.floor(Number(marketCounts[unitDef.unitKey]) || 0));
  }
  return total;
}

function buildMarketCapacityLockedReason(ownedCount, limit, neededCount = 0) {
  const safeOwned = Math.max(0, Math.floor(Number(ownedCount) || 0));
  const safeLimit = Math.max(0, Math.floor(Number(limit) || 0));
  const safeNeeded = Math.max(0, Math.floor(Number(neededCount) || 0));
  if (safeLimit <= 0)
    return "Build the Market first";

  const remaining = Math.max(0, safeLimit - safeOwned);
  if (safeNeeded > remaining && remaining > 0)
    return `Need ${safeNeeded - remaining} more trader slots (${safeOwned}/${safeLimit})`;
  return `Trader cap reached (${safeOwned}/${safeLimit})`;
}

function getBarracksActiveFoodState(activeLane, sourceLaneIndex, barracksId, level = 0) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  const foodLimit = getFoodLimitForTier(level);
  if (!activeLane || !normalizedId) {
    return {
      foodLimit,
      foodUsed: 0,
      foodRemaining: foodLimit,
      isAtFoodLimit: false,
    };
  }

  let foodUsed = 0;
  for (const collection of [activeLane.units, activeLane.spawnQueue]) {
    if (!Array.isArray(collection))
      continue;

    for (const unit of collection) {
      if (!unit)
        continue;
      if (collection === activeLane.units && Math.max(0, Math.floor(Number(unit.hp) || 0)) <= 0)
        continue;
      if (String(unit.spawnSourceType || "").trim().toLowerCase() !== "barracks_roster")
        continue;
      if (normalizeBarracksSiteId(unit.sourceBarracksId) !== normalizedId)
        continue;
      if (Number.isInteger(sourceLaneIndex) && Number(unit.sourceLaneIndex) !== Number(sourceLaneIndex))
        continue;

      const rosterDef = getBarracksRosterDefinition(unit.rosterKey);
      foodUsed += getBarracksRosterFoodCost(rosterDef);
    }
  }

  return {
    foodLimit,
    foodUsed,
    foodRemaining: Math.max(0, foodLimit - foodUsed),
    isAtFoodLimit: foodLimit > 0 && foodUsed >= foodLimit,
  };
}

function hasOwnedBarracksUnits(lane, barracksId) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return false;
  const ownerLaneIndex = Number.isInteger(lane && lane.laneIndex)
    ? lane.laneIndex
    : -1;

  const siteCounts = getBarracksSiteCounts(lane, barracksId);
  if (!siteCounts)
    return false;
  for (const value of Object.values(siteCounts)) {
    if (Math.max(0, Math.floor(Number(value) || 0)) > 0)
      return true;
  }

  for (const collection of [lane && lane.units, lane && lane.spawnQueue]) {
    if (!Array.isArray(collection))
      continue;

    for (const unit of collection) {
      if (!unit)
        continue;
      if (collection === lane.units && Math.max(0, Math.floor(Number(unit.hp) || 0)) <= 0)
        continue;
      if (String(unit.spawnSourceType || "").trim().toLowerCase() !== "barracks_roster")
        continue;
      if (ownerLaneIndex >= 0 && Number(unit.sourceLaneIndex) !== ownerLaneIndex)
        continue;
      if (normalizeBarracksSiteId(unit.sourceBarracksId) === normalizedId)
        return true;
    }
  }

  return false;
}

function getBarracksSiteTierRequirement(siteDef) {
  return getBarracksSiteRequiredTownCoreTier(siteDef, 1);
}

function getBarracksSiteRequiredTownCoreTier(siteDef, targetLevel = 1) {
  const safeLevel = Math.max(1, Math.floor(Number(targetLevel) || 1));
  if (!siteDef || !siteDef.requiredTownCoreTierByLevel)
    return getFortressRequiredTownCoreTier("barracks", safeLevel);

  const explicit = Number(siteDef.requiredTownCoreTierByLevel[safeLevel]);
  if (Number.isFinite(explicit))
    return Math.max(1, Math.floor(explicit || 1));

  let fallback = 1;
  for (const [levelKey, tierValue] of Object.entries(siteDef.requiredTownCoreTierByLevel)) {
    const parsedLevel = Math.max(1, Math.floor(Number(levelKey) || 0));
    const parsedTier = Math.max(1, Math.floor(Number(tierValue) || 1));
    if (parsedLevel <= safeLevel)
      fallback = Math.max(fallback, parsedTier);
  }

  return fallback;
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

function getCurrentBarracksTick(game) {
  return Math.max(0, Math.floor(Number(game && game.tick) || 0));
}

function getBarracksSiteConstructionDurationTicks(targetLevel, kind = BARRACKS_CONSTRUCTION_KINDS.build) {
  const safeLevel = Math.max(1, normalizeBarracksSiteLevel(targetLevel));
  if (kind === BARRACKS_CONSTRUCTION_KINDS.upgrade)
    return DEFAULT_BARRACKS_UPGRADE_DURATION_TICKS + (Math.max(0, safeLevel - 2) * (2 * TICK_HZ));
  return DEFAULT_BARRACKS_BUILD_DURATION_TICKS + (Math.max(0, safeLevel - 1) * TICK_HZ);
}

function clearBarracksSiteConstructionState(siteState) {
  if (!siteState)
    return;

  siteState.constructionKind = null;
  siteState.constructionTargetLevel = 0;
  siteState.constructionEndTick = 0;
  siteState.constructionTotalTicks = 0;
}

function getBarracksSiteConstructionState(siteState, game = null) {
  if (!siteState)
    return null;

  const targetLevel = Math.max(0, Math.floor(Number(siteState.constructionTargetLevel) || 0));
  const totalTicks = Math.max(0, Math.floor(Number(siteState.constructionTotalTicks) || 0));
  const endTick = Math.max(0, Math.floor(Number(siteState.constructionEndTick) || 0));
  const kind = String(siteState.constructionKind || "").trim().toLowerCase();
  if (targetLevel <= 0 || totalTicks <= 0 || endTick <= 0)
    return null;
  if (kind !== BARRACKS_CONSTRUCTION_KINDS.build && kind !== BARRACKS_CONSTRUCTION_KINDS.upgrade)
    return null;

  const currentTick = getCurrentBarracksTick(game);
  const remainingTicks = Math.max(0, endTick - currentTick);
  const progress01 = totalTicks > 0
    ? Math.min(1, Math.max(0, (totalTicks - remainingTicks) / totalTicks))
    : 1;
  return {
    kind,
    targetLevel,
    totalTicks,
    endTick,
    remainingTicks,
    progress01,
  };
}

function startBarracksSiteConstruction(game, siteState, targetLevel, kind = BARRACKS_CONSTRUCTION_KINDS.build) {
  if (!siteState)
    return null;

  const safeKind = kind === BARRACKS_CONSTRUCTION_KINDS.upgrade
    ? BARRACKS_CONSTRUCTION_KINDS.upgrade
    : BARRACKS_CONSTRUCTION_KINDS.build;
  const safeTargetLevel = Math.max(1, normalizeBarracksSiteLevel(targetLevel));
  const totalTicks = getBarracksSiteConstructionDurationTicks(safeTargetLevel, safeKind);
  const currentTick = getCurrentBarracksTick(game);

  if (totalTicks <= 0) {
    clearBarracksSiteConstructionState(siteState);
    return {
      kind: safeKind,
      targetLevel: safeTargetLevel,
      totalTicks: 0,
      remainingTicks: 0,
      progress01: 1,
    };
  }

  siteState.constructionKind = safeKind;
  siteState.constructionTargetLevel = safeTargetLevel;
  siteState.constructionTotalTicks = totalTicks;
  siteState.constructionEndTick = currentTick + totalTicks;
  return getBarracksSiteConstructionState(siteState, game);
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
  const rawCommandState = String(options.commandState || "").trim().toUpperCase();
  const commandState = rawCommandState === "ATTACK" || rawCommandState === "RETREAT" || rawCommandState === "DEFEND"
    ? rawCommandState
    : "DEFEND";

  return {
    barracksId: siteDef.barracksId,
    isBuilt: built,
    level,
    hp,
    maxHp,
    nextSendTick: built
      ? Math.max(1, Math.floor(Number(options.nextSendTick) || interval))
      : 0,
    commandState,
    costHistory: Array.isArray(options.costHistory) ? options.costHistory.slice() : [],
    constructionKind: options.constructionKind || null,
    constructionTargetLevel: Math.max(0, Math.floor(Number(options.constructionTargetLevel) || 0)),
    constructionEndTick: Math.max(0, Math.floor(Number(options.constructionEndTick) || 0)),
    constructionTotalTicks: Math.max(0, Math.floor(Number(options.constructionTotalTicks) || 0)),
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
      commandState: "DEFEND",
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
      || hasOwnedBarracksUnits(lane, siteId);

    if (current && typeof current === "object") {
      const hasOwnedUnits = hasOwnedBarracksUnits(lane, siteId);
      const built = !!siteDef.startsBuilt || !!current.isBuilt || hasOwnedUnits;
      next[siteId] = createBarracksSiteState(siteDef, {
        isBuilt: built,
        level: built ? Math.max(1, normalizeBarracksSiteLevel(current.level || legacyBarracksLevel || 1)) : 0,
        hp: current.hp,
        nextSendTick: current.nextSendTick,
        commandState: current.commandState || lane.commandState || "DEFEND",
        costHistory: current.costHistory,
        constructionKind: current.constructionKind,
        constructionTargetLevel: current.constructionTargetLevel,
        constructionEndTick: current.constructionEndTick,
        constructionTotalTicks: current.constructionTotalTicks,
      });
      continue;
    }

    next[siteId] = createBarracksSiteState(siteDef, {
      isBuilt: shouldStartBuilt,
      level: shouldStartBuilt ? legacyBarracksLevel : 0,
      hp: shouldStartBuilt ? getBarracksSiteBaseMaxHp() : 0,
      nextSendTick: shouldStartBuilt ? legacyNextSendTick : 0,
      commandState: lane.commandState || "DEFEND",
    });
  }

  lane.barracksSiteStates = next;
  syncLegacyBarracksAggregate(lane);
  return next;
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

function logBarracksRosterState(lane, reason, deps = {}) {
  if (!lane)
    return;

  const summary = BARRACKS_SITE_DEFS
    .map((siteDef) => {
      const siteCounts = lane.barracksSiteRosterCounts && lane.barracksSiteRosterCounts[siteDef.barracksId];
      return `${siteDef.barracksId}=${summarizeBarracksSiteCounts(siteCounts)}`;
    })
    .join(" ");
  logSpawnAuditLine(deps,
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

  const construction = getBarracksSiteConstructionState(siteState, game);
  const built = !!siteState.isBuilt;
  const level = built ? normalizeBarracksSiteLevel(siteState.level) : 0;
  const destroyed = built && Math.max(0, Math.floor(Number(siteState.hp) || 0)) <= 0;
  const maxLevel = getBarracksSiteMaxLevel();
  const nextLevel = built ? Math.min(maxLevel, level + 1) : 1;
  const targetLevel = construction ? construction.targetLevel : nextLevel;
  const townCoreTier = getTownCoreTier(lane);
  const requiredTownCoreTier = getBarracksSiteRequiredTownCoreTier(siteDef, targetLevel);
  const canBuild = !built && !construction && townCoreTier >= requiredTownCoreTier;
  const canUpgrade = built && !construction && !destroyed && level < maxLevel && townCoreTier >= requiredTownCoreTier;

  let buildState = FORTRESS_BUILD_STATES.locked;
  if (destroyed) {
    buildState = FORTRESS_BUILD_STATES.destroyed;
  } else if (construction) {
    buildState = construction.kind === BARRACKS_CONSTRUCTION_KINDS.upgrade
      ? FORTRESS_BUILD_STATES.upgrading
      : FORTRESS_BUILD_STATES.constructing;
  } else if (!built) {
    buildState = canBuild ? FORTRESS_BUILD_STATES.availableToBuild : FORTRESS_BUILD_STATES.locked;
  } else if (level >= maxLevel) {
    buildState = FORTRESS_BUILD_STATES.maxTier;
  } else if (canUpgrade) {
    buildState = FORTRESS_BUILD_STATES.upgradeAvailable;
  } else {
    buildState = FORTRESS_BUILD_STATES.built;
  }

  let lockedReason = null;
  if (construction) {
    lockedReason = construction.kind === BARRACKS_CONSTRUCTION_KINDS.upgrade
      ? "Upgrade in progress"
      : "Construction in progress";
  } else if (destroyed) {
    lockedReason = "Destroyed";
  } else if (!built && !canBuild) {
    lockedReason = `Requires Town Core: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  } else if (built && level < maxLevel && !canUpgrade) {
    lockedReason = `Upgrade requires Town Core: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  }

  const hasDungeonWaveSchedule = !!(
    game
    && Array.isArray(game.waveConfig)
    && game.waveConfig.length > 0
  );
  const fallbackSendIntervalTicks = built
    ? getBarracksSiteSendIntervalTicks(level)
    : getBarracksSiteSendIntervalTicks(1);
  const synchronizedSendIntervalTicks = game
    ? Math.max(0, Math.floor(Number(game.barracksSendIntervalTicks) || 0))
    : 0;
  const sendIntervalTicks = hasDungeonWaveSchedule && synchronizedSendIntervalTicks > 0
    ? synchronizedSendIntervalTicks
    : fallbackSendIntervalTicks;
  const currentTick = game ? Math.floor(Number(game.tick) || 0) : 0;
  const globalNextSendTick = game ? Math.floor(Number(game.nextBarracksSendTick)) : 0;
  const hasSynchronizedSendSchedule = built
    && hasDungeonWaveSchedule
    && synchronizedSendIntervalTicks > 0
    && Number.isInteger(globalNextSendTick)
    && globalNextSendTick > 0;
  if (hasSynchronizedSendSchedule) {
    siteState.nextSendTick = globalNextSendTick;
  } else if (built && (!Number.isInteger(siteState.nextSendTick) || siteState.nextSendTick <= 0)) {
    siteState.nextSendTick = currentTick + sendIntervalTicks;
  }

  const nextSendTick = hasSynchronizedSendSchedule
    ? globalNextSendTick
    : Math.floor(Number(siteState.nextSendTick) || 0);
  const sendTimerTicksRemaining = built && game
    ? Math.max(0, nextSendTick - currentTick)
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
    targetLevel,
    construction,
    destroyed,
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
  return siteDef ? `${siteDef.displayName} must be managed from the Town Core.` : "Barracks unavailable";
}

function isBarracksSiteAvailable(lane, barracksId) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  return !!(descriptor && descriptor.isBuilt && !descriptor.destroyed);
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
  if (!descriptor || !descriptor.isBuilt || descriptor.destroyed)
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

function resolveMarketRosterUnlockContext(lane, unitDef) {
  const requiredBuildingTier = Math.max(1, Math.floor(Number(unitDef && unitDef.requiredBuildingTier) || 1));
  const unlockBuildingType = String((unitDef && unitDef.unlockBuildingType) || "market").trim().toLowerCase();
  const unlockPad = getFortressPadByBuildingType(lane, unlockBuildingType);
  const unlockTier = getHighestBuiltFortressPadTier(lane, unlockBuildingType);
  return {
    unlockPad,
    unlockPadId: unlockPad ? unlockPad.padId : null,
    unlockTier,
    unlocked: unlockTier >= requiredBuildingTier,
    buildingDef: FORTRESS_BUILDING_DEFS[unlockBuildingType] || null,
  };
}

function getCurrentBarracksRosterDefinitionForBranch(lane, branchKey, barracksId = null) {
  if (!lane || !branchKey)
    return null;

  return BARRACKS_ROSTER_DEFS
    .filter((entry) => entry && entry.branchKey === branchKey)
    .sort((left, right) => Math.max(1, left.tier || 1) - Math.max(1, right.tier || 1))
    .reduce((current, rosterDef) => (
      resolveBarracksRosterUnlockContext(lane, rosterDef, barracksId).unlocked ? rosterDef : current
    ), null);
}

function getCurrentMarketRosterDefinitionForLane(lane) {
  if (!lane)
    return null;

  return MARKET_ROSTER_DEFS
    .slice()
    .sort((left, right) => Math.max(1, left.tier || 1) - Math.max(1, right.tier || 1))
    .reduce((current, unitDef) => (
      resolveMarketRosterUnlockContext(lane, unitDef).unlocked ? unitDef : current
    ), null);
}

function resolveLaneMarketIncomeDefinition(lane) {
  const currentTierDef = getCurrentMarketRosterDefinitionForLane(lane);
  if (currentTierDef)
    return currentTierDef;

  const marketCounts = getMarketRosterCounts(lane);
  if (!marketCounts)
    return null;

  for (let index = MARKET_ROSTER_DEFS.length - 1; index >= 0; index -= 1) {
    const unitDef = MARKET_ROSTER_DEFS[index];
    if (!unitDef)
      continue;
    if (Math.max(0, Math.floor(Number(marketCounts[unitDef.unitKey]) || 0)) > 0)
      return unitDef;
  }

  return null;
}

function getLaneMarketIncome(lane) {
  const totalOwned = getTotalOwnedMarketWorkers(lane);
  if (totalOwned <= 0)
    return 0;

  const tierDef = resolveLaneMarketIncomeDefinition(lane);
  if (!tierDef)
    return 0;

  return totalOwned * Math.max(0, Math.floor(Number(tierDef.economyLapGold) || 0));
}

function getLaneTotalIncome(lane) {
  if (!lane)
    return 0;
  return Math.max(0, Number(lane.income) || 0) + getLaneMarketIncome(lane);
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

function getMarketRosterLockedReason(unitDef) {
  if (!unitDef)
    return "Market unit locked";

  const buildingDef = FORTRESS_BUILDING_DEFS[unitDef.unlockBuildingType];
  const buildingName = buildingDef ? buildingDef.displayName : unitDef.unlockBuildingType;
  const tierName = getBuildingTierDisplayName(unitDef.unlockBuildingType, unitDef.requiredBuildingTier);
  if (String(buildingName || "").trim().toLowerCase() === String(tierName || "").trim().toLowerCase())
    return `${buildingName} required`;
  return `${buildingName}: ${tierName}`;
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

function applyBarracksSiteDamage(game, lane, barracksId, damage, deps = {}) {
  const recordBalanceStructureDamage = typeof deps.recordBalanceStructureDamage === "function"
    ? deps.recordBalanceStructureDamage
    : null;
  if (!game || !lane || lane.eliminated)
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };

  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };

  const siteState = getBarracksSiteState(lane, normalizedId, game);
  if (!siteState || !siteState.isBuilt)
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };

  const prevHp = Math.max(0, Math.floor(Number(siteState.hp) || 0));
  if (prevHp <= 0)
    return { damageApplied: 0, destroyed: true, remainingHp: 0 };

  const appliedDamage = Math.max(0, Math.floor(Number(damage) || 0));
  if (appliedDamage <= 0)
    return { damageApplied: 0, destroyed: false, remainingHp: prevHp };

  siteState.hp = Math.max(0, prevHp - appliedDamage);
  const damageApplied = prevHp - siteState.hp;
  if (recordBalanceStructureDamage && damageApplied > 0)
    recordBalanceStructureDamage(game, lane, "barracks", damageApplied, {});
  return {
    damageApplied,
    destroyed: siteState.hp <= 0,
    remainingHp: Math.max(0, Math.floor(Number(siteState.hp) || 0)),
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
    const descriptor = describeBarracksSite(null, lane, siteDef.barracksId);
    if (descriptor && descriptor.isBuilt && !descriptor.destroyed)
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

function getMarketRosterBuyCost(unitDef, deps = {}) {
  if (!unitDef)
    return 0;

  const presentation = resolveFortPresentation(
    unitDef.archetypeKey,
    unitDef.presentationKey,
    unitDef.displayName,
    deps
  );
  const unitType = typeof deps.getUnitType === "function"
    ? deps.getUnitType(presentation.catalogUnitKey)
    : null;
  const buildCost = Math.floor(Number(unitType && unitType.build_cost) || 0);
  if (buildCost > 0)
    return buildCost;

  const unitDefResolved = typeof deps.resolveUnitDef === "function"
    ? deps.resolveUnitDef(unitDef.archetypeKey)
    : null;
  const sendCost = Math.max(1, Math.floor(Number(unitDefResolved && unitDefResolved.cost) || 1));
  return Math.max(5, sendCost * 5);
}

function createBarracksSiteRosterSnapshot(game, lane, barracksId, deps = {}) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return [];

  const descriptor = describeBarracksSite(game, lane, normalizedId);
  if (!descriptor)
    return [];

  const siteCounts = getBarracksSiteCounts(lane, normalizedId) || {};
  const siteBuilt = !!descriptor.isBuilt && !descriptor.destroyed;
  const foodState = getBarracksSiteFoodState(lane, normalizedId, descriptor.level);

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
      const currentTierDef = getCurrentBarracksRosterDefinitionForBranch(lane, rosterDef.branchKey, normalizedId);
      const currentTier = !!(
        unlocked
        && currentTierDef
        && currentTierDef.rosterKey === rosterDef.rosterKey
      );
      const foodCost = getBarracksRosterFoodCost(rosterDef);
      const availableForPurchase = !!(
        currentTier
        && foodState.foodRemaining >= foodCost
      );
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
        foodCost,
        availableForPurchase,
        currentTier,
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
          ? !currentTier
            ? "A higher-tier version is now the current purchase for this branch"
            : foodState.foodRemaining < foodCost
              ? buildFoodLimitLockedReason(
                descriptor.siteDef.displayName,
                foodState.foodUsed,
                foodState.foodLimit,
                foodCost
              )
              : null
          : !siteBuilt
            ? descriptor.lockedReason || "Use Town Core to manage Barracks."
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
  const construction = descriptor.construction;
  const allegianceKey = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(lane)
    : null;
  const foodState = getBarracksSiteFoodState(lane, normalizedId, descriptor.level);

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
    isConstructing: !!construction,
    constructionKind: construction ? construction.kind : null,
    constructionTargetLevel: construction ? construction.targetLevel : descriptor.level,
    constructionTargetTierName: getBuildingTierDisplayName(
      "barracks",
      construction ? construction.targetLevel : descriptor.level
    ),
    constructionTimerTicksRemaining: construction ? construction.remainingTicks : 0,
    constructionTimerTotalTicks: construction ? construction.totalTicks : 0,
    constructionProgress01: construction ? construction.progress01 : 0,
    isDestroyed: !!descriptor.destroyed,
    canBuild: descriptor.canBuild,
    canUpgrade: descriptor.canUpgrade,
    buildCost: descriptor.buildCost,
    upgradeCost: descriptor.upgradeCost,
    requiredTownCoreTier: descriptor.requiredTownCoreTier,
    requiredTownCoreTierName: getBuildingTierDisplayName("town_core", descriptor.requiredTownCoreTier),
    sendIntervalTicks: descriptor.sendIntervalTicks,
    sendTimerTicksRemaining: descriptor.sendTimerTicksRemaining,
    sendTimerTotalTicks: descriptor.sendTimerTotalTicks,
    commandState: siteState && siteState.commandState ? siteState.commandState : (lane && lane.commandState ? lane.commandState : "DEFEND"),
    foodLimit: foodState.foodLimit,
    foodUsed: foodState.foodUsed,
    foodRemaining: foodState.foodRemaining,
    isAtFoodLimit: foodState.isAtFoodLimit,
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
      const currentTierDef = getCurrentBarracksRosterDefinitionForBranch(lane, rosterDef.branchKey);
      const currentTier = !!(
        unlocked
        && currentTierDef
        && currentTierDef.rosterKey === rosterDef.rosterKey
      );
      const foodCost = getBarracksRosterFoodCost(rosterDef);
      let availableForPurchase = false;
      if (currentTier) {
        for (const siteDef of BARRACKS_SITE_DEFS) {
          const descriptor = describeBarracksSite(game, lane, siteDef.barracksId);
          if (!descriptor || !descriptor.isBuilt || descriptor.destroyed)
            continue;
          const foodState = getBarracksSiteFoodState(lane, siteDef.barracksId, descriptor.level);
          if (foodState.foodRemaining >= foodCost) {
            availableForPurchase = true;
            break;
          }
        }
      }
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
        foodCost,
        availableForPurchase,
        currentTier,
        ownedCount,
        buyCost,
        sellRefund,
        unlocked,
        unlockBuildingType: rosterDef.unlockBuildingType,
        unlockBuildingName: buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType,
        unlockBuildingTierName: getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier),
        requiredBuildingTier: rosterDef.requiredBuildingTier,
        unlockPadId: unlockContext.unlockPadId,
        lockedReason: unlocked
          ? !currentTier
            ? "A higher-tier version is now the current purchase for this branch"
            : !availableForPurchase
              ? "All built Barracks are at their food limit."
              : null
          : getBarracksRosterLockedReason(rosterDef),
      };
    });
}

function createMarketRosterSnapshot(game, lane, deps = {}) {
  const marketCounts = getMarketRosterCounts(lane) || {};
  const currentTierDef = getCurrentMarketRosterDefinitionForLane(lane);
  const marketTier = Math.max(0, Math.floor(Number(getFortressPadByBuildingType(lane, "market")?.tier) || 0));
  const marketFoodState = getMarketFoodState(lane, marketTier);

  return MARKET_ROSTER_DEFS
    .slice()
    .sort((left, right) => (left.sortIndex || 0) - (right.sortIndex || 0))
    .map((unitDef) => {
      const presentation = resolveFortPresentation(
        unitDef.archetypeKey,
        unitDef.presentationKey,
        unitDef.displayName,
        deps
      );
      const unlockContext = resolveMarketRosterUnlockContext(lane, unitDef);
      const unlocked = unlockContext.unlocked;
      const currentTier = !!(
        unlocked
        && currentTierDef
        && currentTierDef.unitKey === unitDef.unitKey
      );
      const foodCost = getMarketRosterFoodCost(unitDef);
      const availableForPurchase = !!(
        currentTier
        && marketFoodState.foodRemaining >= foodCost
      );
      const ownedCount = Math.max(0, Math.floor(Number(marketCounts[unitDef.unitKey]) || 0));

      return {
        unitKey: unitDef.unitKey,
        displayName: unitDef.displayName,
        role: unitDef.role,
        roleLabel: unitDef.roleLabel || "Economy",
        sortIndex: unitDef.sortIndex || 0,
        archetypeKey: unitDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: unitDef.branchKey || "market",
        branchLabel: unitDef.branchLabel || "Market",
        productionBuildingType: unitDef.productionBuildingType || "market",
        productionBuildingName: getBuildingDisplayName(unitDef.productionBuildingType || "market"),
        tier: Math.max(1, Math.floor(Number(unitDef.tier) || 1)),
        foodCost,
        availableForPurchase,
        currentTier,
        ownedCount,
        buyCost: getMarketRosterBuyCost(unitDef, deps),
        unlocked,
        unlockBuildingType: unitDef.unlockBuildingType || "market",
        unlockBuildingName: unlockContext.buildingDef ? unlockContext.buildingDef.displayName : (unitDef.unlockBuildingType || "market"),
        unlockBuildingTierName: getBuildingTierDisplayName(unitDef.unlockBuildingType || "market", unitDef.requiredBuildingTier),
        requiredBuildingTier: Math.max(1, Math.floor(Number(unitDef.requiredBuildingTier) || 1)),
        unlockPadId: unlockContext.unlockPadId,
        economyLapGold: Math.max(0, Math.floor(Number(unitDef.economyLapGold) || 0)),
        routeStartBuildingType: unitDef.routeStartBuildingType || "market",
        routeStartBuildingName: unitDef.routeStartBuildingName || getBuildingDisplayName(unitDef.routeStartBuildingType || "market"),
        routeEndBuildingType: unitDef.routeEndBuildingType || "trade_outpost",
        routeEndBuildingName: unitDef.routeEndBuildingName || getBuildingDisplayName(unitDef.routeEndBuildingType || "trade_outpost"),
        nextUnitKey: unitDef.nextUnitKey || null,
        description: unitDef.description || "",
        lockedReason: unlocked
          ? !currentTier
            ? "A higher-tier market worker is now the current purchase"
            : marketFoodState.foodRemaining < foodCost
              ? buildMarketCapacityLockedReason(marketFoodState.foodUsed, marketFoodState.foodLimit, foodCost)
              : null
          : getMarketRosterLockedReason(unitDef),
      };
    });
}

function forEachLiveRouteUnit(game, visitor) {
  if (!game || !Array.isArray(game.lanes) || typeof visitor !== "function")
    return;

  for (const liveLane of game.lanes) {
    if (!liveLane)
      continue;

    for (const collectionName of ["units", "spawnQueue"]) {
      const collection = liveLane[collectionName];
      if (!Array.isArray(collection))
        continue;

      for (const unit of collection) {
        if (!unit)
          continue;
        visitor(liveLane, unit, collectionName);
      }
    }
  }
}

function getResolvedUpgradePurchaseCount(deps, lane, buildingType, upgradeKey) {
  if (!lane || typeof deps.getLaneBuildingUpgradePurchaseCount !== "function")
    return 0;
  return Math.max(0, Math.floor(Number(deps.getLaneBuildingUpgradePurchaseCount(lane, upgradeKey, buildingType)) || 0));
}

function getResolvedUpgradeMultiplier(deps, lane, buildingType, upgradeKey, stepPct) {
  const purchaseCount = getResolvedUpgradePurchaseCount(deps, lane, buildingType, upgradeKey);
  return 1 + (purchaseCount * (Number(stepPct) || 0) * 0.01);
}

function hasResolvedBuildingUpgrade(deps, lane, buildingType, upgradeKey) {
  if (!lane || typeof deps.hasLaneBuildingUpgrade !== "function")
    return false;
  return !!deps.hasLaneBuildingUpgrade(lane, upgradeKey, buildingType);
}

function resolveUnitSpecialProps(unitDef) {
  if (!unitDef || typeof unitDef !== "object")
    return {};
  if (unitDef.specialProps && typeof unitDef.specialProps === "object")
    return unitDef.specialProps;
  if (unitDef.special_props && typeof unitDef.special_props === "object")
    return unitDef.special_props;
  return {};
}

function applyLiveRosterDefinition(game, liveLane, unit, rosterDef, deps = {}) {
  if (!unit || !rosterDef || !rosterDef.archetypeKey)
    return false;

  const resolvedUnitDef = typeof deps.resolveUnitDef === "function"
    ? deps.resolveUnitDef(rosterDef.archetypeKey)
    : null;
  if (!resolvedUnitDef)
    return false;

  const presentation = resolveFortPresentation(
    rosterDef.archetypeKey,
    rosterDef.presentationKey,
    rosterDef.displayName,
    deps
  );
  const previousMaxHp = Math.max(1, Math.floor(Number(unit.maxHp) || Number(unit.hp) || resolvedUnitDef.hp || 1));
  const hpRatio = Math.max(0, Math.min(1, (Number(unit.hp) || previousMaxHp) / previousMaxHp));
  const sourceLane = game && Array.isArray(game.lanes) && Number.isInteger(unit.sourceLaneIndex)
    ? game.lanes[unit.sourceLaneIndex]
    : null;
  const speedScale = String(rosterDef.productionBuildingType || "").trim().toLowerCase() === "blacksmith"
    ? getBarracksSpeedMult(sourceLane && sourceLane.barracks)
    : 1;
  const specialProps = resolveUnitSpecialProps(resolvedUnitDef);
  const productionBuildingType = String(rosterDef.productionBuildingType || "").trim().toLowerCase();
  const branchKey = String(rosterDef.branchKey || "").trim().toLowerCase();
  const shieldArmorBonusPct = branchKey === "shield"
    ? getResolvedUpgradePurchaseCount(deps, sourceLane, "blacksmith", "shield_armor") * 0.5
    : 0;
  const frontlineDamageMultiplier = branchKey === "infantry" || branchKey === "polearm"
    ? getResolvedUpgradeMultiplier(deps, sourceLane, "blacksmith", "frontline_damage", 0.5)
    : 1;
  const archerRangeMultiplier = productionBuildingType === "archery_tower"
    ? getResolvedUpgradeMultiplier(deps, sourceLane, "archery_tower", "archer_range", 0.5)
    : 1;
  const archerDamageMultiplier = productionBuildingType === "archery_tower"
    ? getResolvedUpgradeMultiplier(deps, sourceLane, "archery_tower", "archer_damage", 0.1)
    : 1;
  const mageDamageMultiplier = productionBuildingType === "wizard_tower"
    ? getResolvedUpgradeMultiplier(deps, sourceLane, "wizard_tower", "mage_damage", 0.1)
    : 1;
  const healStrengthMultiplier = productionBuildingType === "temple"
    ? getResolvedUpgradeMultiplier(deps, sourceLane, "temple", "heal_strength", 0.5)
    : 1;
  const runSpeedMultiplier = productionBuildingType === "stable"
    ? getResolvedUpgradeMultiplier(deps, sourceLane, "stable", "run_speed", 0.5)
    : 1;
  const baseSpeed = typeof deps.getBaseCombatPathSpeed === "function"
    ? deps.getBaseCombatPathSpeed(rosterDef.archetypeKey)
    : Number(resolvedUnitDef.pathSpeed) || Number(unit.baseSpeed) || 0.18;
  const towerDef = typeof deps.resolveTowerDef === "function"
    ? deps.resolveTowerDef(rosterDef.archetypeKey)
    : null;
  const baseAttackRange = Number(towerDef && towerDef.range) || Number(resolvedUnitDef.combatRange) || 1.5;
  let resolvedDamage = Number(resolvedUnitDef.dmg) || Number(unit.baseDmg) || 0;
  if (frontlineDamageMultiplier !== 1)
    resolvedDamage *= frontlineDamageMultiplier;
  if (archerDamageMultiplier !== 1)
    resolvedDamage *= archerDamageMultiplier;
  if (mageDamageMultiplier !== 1)
    resolvedDamage *= mageDamageMultiplier;
  const healAmountRaw = specialProps.healAmount != null ? specialProps.healAmount : specialProps.heal_amount;
  const baseHealAmount = Math.max(1, Number(healAmountRaw) || 1);
  const hasMageSplash = productionBuildingType === "wizard_tower"
    && hasResolvedBuildingUpgrade(deps, sourceLane, "wizard_tower", "mage_splash");
  const hasChainHeal = productionBuildingType === "temple"
    && hasResolvedBuildingUpgrade(deps, sourceLane, "temple", "chain_heal");

  unit.type = rosterDef.archetypeKey;
  unit.unitTypeKey = rosterDef.archetypeKey;
  unit.archetypeKey = rosterDef.archetypeKey;
  unit.presentationKey = presentation.presentationKey;
  unit.catalogUnitKey = presentation.catalogUnitKey;
  unit.skinKey = presentation.skinKey || null;
  unit.role = rosterDef.role || unit.role || null;
  unit.baseDmg = Math.max(0, Number(resolvedDamage) || 0);
  unit.baseSpeed = Math.max(0.01, Number(baseSpeed) * speedScale * runSpeedMultiplier);
  unit.atkCdTicks = Math.max(1, Math.floor(Number(resolvedUnitDef.atkCdTicks) || Number(unit.atkCdTicks) || 1));
  unit.damageType = resolvedUnitDef.damageType || unit.damageType || "NORMAL";
  unit.armorType = resolvedUnitDef.armorType || unit.armorType || "MEDIUM";
  unit.damageReductionPct = Math.max(0, (Number(resolvedUnitDef.damageReductionPct) || 0) + shieldArmorBonusPct);
  unit.directDamageReductionPctBonus = shieldArmorBonusPct;
  unit.bounty = Number(resolvedUnitDef.bounty) || Number(unit.bounty) || 1;
  unit.maxHp = Math.max(1, Math.ceil(Number(resolvedUnitDef.hp) || previousMaxHp));
  unit.hp = Math.max(1, Math.min(unit.maxHp, Math.ceil(unit.maxHp * hpRatio)));
  unit.attackRangeOverride = archerRangeMultiplier !== 1
    ? Math.max(0.1, baseAttackRange * archerRangeMultiplier)
    : null;
  unit.splashExtraTargets = hasMageSplash ? 2 : 0;
  unit.splashRadius = hasMageSplash ? 1.5 : 0;
  unit.healAmountOverride = productionBuildingType === "temple"
    ? Math.max(1, Math.round(baseHealAmount * healStrengthMultiplier))
    : null;
  unit.chainHealExtraTargets = hasChainHeal ? 2 : 0;
  unit.chainHealRadius = hasChainHeal ? 1.75 : 0;
  if (typeof deps.buildAbilitiesForUnitType === "function")
    unit.abilities = deps.buildAbilitiesForUnitType(rosterDef.archetypeKey);

  if (Object.prototype.hasOwnProperty.call(rosterDef, "rosterKey")) {
    unit.rosterKey = rosterDef.rosterKey;
    unit.marketUnitKey = null;
    unit.isMarketWorker = false;
  } else if (Object.prototype.hasOwnProperty.call(rosterDef, "unitKey")) {
    unit.marketUnitKey = rosterDef.unitKey;
    unit.rosterKey = null;
    unit.isMarketWorker = true;
    unit.canEngage = false;
  }

  if (typeof deps.applyCanonicalUnitMirrors === "function")
    deps.applyCanonicalUnitMirrors(game, liveLane, unit);
  if (unit.isMarketWorker)
    unit.canEngage = false;
  return true;
}

function reapplyOwnedCombatUnitsForLane(game, lane, deps = {}) {
  if (!game || !lane)
    return 0;

  let reapplied = 0;
  forEachLiveRouteUnit(game, (liveLane, unit) => {
    if (!unit)
      return;
    if (Number(unit.sourceLaneIndex) !== Number(lane.laneIndex))
      return;
    if (!unit.rosterKey)
      return;

    const rosterDef = getBarracksRosterDefinition(unit.rosterKey);
    if (!rosterDef)
      return;
    if (applyLiveRosterDefinition(game, liveLane, unit, rosterDef, deps))
      reapplied += 1;
  });
  return reapplied;
}

function collapseBarracksBranchCountsToTier(lane, branchKey, targetRosterDef) {
  if (!lane || !branchKey || !targetRosterDef || !targetRosterDef.rosterKey)
    return 0;

  let totalMoved = 0;
  for (const siteDef of BARRACKS_SITE_DEFS) {
    const siteCounts = getBarracksSiteCounts(lane, siteDef.barracksId);
    if (!siteCounts)
      continue;

    let branchTotal = 0;
    for (const rosterDef of BARRACKS_ROSTER_DEFS) {
      if (!rosterDef || rosterDef.branchKey !== branchKey)
        continue;
      branchTotal += Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
      siteCounts[rosterDef.rosterKey] = 0;
    }
    siteCounts[targetRosterDef.rosterKey] = branchTotal;
    totalMoved += branchTotal;
  }

  return totalMoved;
}

function collapseMarketCountsToTier(lane, targetUnitDef) {
  const marketCounts = getMarketRosterCounts(lane);
  if (!marketCounts || !targetUnitDef || !targetUnitDef.unitKey)
    return 0;

  let totalMoved = 0;
  for (const unitDef of MARKET_ROSTER_DEFS) {
    if (!unitDef)
      continue;
    totalMoved += Math.max(0, Math.floor(Number(marketCounts[unitDef.unitKey]) || 0));
    marketCounts[unitDef.unitKey] = 0;
  }
  marketCounts[targetUnitDef.unitKey] = totalMoved;
  return totalMoved;
}

function removeOwnedMarketWorkers(game, lane) {
  if (!game || !lane)
    return 0;

  let removed = 0;
  const ownerLaneIndex = Number.isInteger(lane.laneIndex) ? lane.laneIndex : -1;
  for (const liveLane of game.lanes || []) {
    if (!liveLane)
      continue;

    for (const collectionName of ["units", "spawnQueue"]) {
      const collection = Array.isArray(liveLane[collectionName]) ? liveLane[collectionName] : null;
      if (!collection)
        continue;

      const nextCollection = [];
      for (const unit of collection) {
        const isOwnedMarketWorker = !!(
          unit
          && (unit.isMarketWorker || unit.marketUnitKey)
          && ownerLaneIndex >= 0
          && Number(unit.sourceLaneIndex) === ownerLaneIndex
        );
        if (isOwnedMarketWorker) {
          removed += 1;
          continue;
        }
        nextCollection.push(unit);
      }

      liveLane[collectionName] = nextCollection;
    }
  }

  return removed;
}

function upgradeOwnedBarracksBranchUnits(game, lane, branchKey, targetRosterDef, deps = {}) {
  if (!game || !lane || !branchKey || !targetRosterDef)
    return 0;

  collapseBarracksBranchCountsToTier(lane, branchKey, targetRosterDef);
  let upgraded = 0;
  forEachLiveRouteUnit(game, (liveLane, unit) => {
    if (!unit || Number(unit.hp) <= 0)
      return;
    if (Number(unit.sourceLaneIndex) !== Number(lane.laneIndex))
      return;
    const unitRosterDef = getBarracksRosterDefinition(unit.rosterKey);
    if (!unitRosterDef || unitRosterDef.branchKey !== branchKey)
      return;
    if (applyLiveRosterDefinition(game, liveLane, unit, targetRosterDef, deps))
      upgraded += 1;
  });
  return upgraded;
}

function upgradeOwnedMarketUnits(game, lane, targetUnitDef, deps = {}) {
  if (!game || !lane || !targetUnitDef)
    return 0;

  collapseMarketCountsToTier(lane, targetUnitDef);
  return removeOwnedMarketWorkers(game, lane);
}

function spawnPurchasedMarketWorker(game, lane, unitDef, deps = {}) {
  if (!game || !lane || !unitDef || typeof deps.spawnWaveUnit !== "function")
    return false;

  const presentation = resolveFortPresentation(
    unitDef.archetypeKey,
    unitDef.presentationKey,
    unitDef.displayName,
    deps
  );
  deps.spawnWaveUnit(game, lane, {
    unit_type: unitDef.archetypeKey,
    hp_mult: 1,
    dmg_mult: 1,
    speed_mult: 1,
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team || null,
    spawnSourceType: "market_roster",
    marketUnitKey: unitDef.unitKey,
    archetypeKey: unitDef.archetypeKey,
    presentationKey: presentation.presentationKey,
    skinKey: presentation.skinKey || null,
    role: unitDef.role || "economy",
    spawnIndex: Math.max(0, Array.isArray(lane.spawnQueue) ? lane.spawnQueue.length : 0),
  });
  return true;
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
    if (!descriptor || !descriptor.isBuilt || descriptor.destroyed)
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
          foodCost: Math.max(1, Math.floor(Number(entry.foodCost) || getBarracksRosterFoodCost(rosterDef))),
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

function trimSpawnEntriesToAvailableFood(spawnEntries, availableFood) {
  if (!Array.isArray(spawnEntries) || spawnEntries.length === 0)
    return [];

  let remainingFood = Math.max(0, Math.floor(Number(availableFood) || 0));
  if (remainingFood <= 0)
    return [];

  const allowedEntries = [];
  for (const entry of spawnEntries) {
    if (!entry)
      continue;

    const foodCost = Math.max(1, Math.floor(Number(entry.foodCost) || 1));
    if (foodCost > remainingFood)
      continue;

    allowedEntries.push(entry);
    remainingFood -= foodCost;
    if (remainingFood <= 0)
      break;
  }

  return allowedEntries;
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

  const sourceDescriptor = describeBarracksSite(game, lane, normalizedBarracksId);
  if (!sourceDescriptor || !sourceDescriptor.isBuilt || sourceDescriptor.destroyed)
    return 0;

  const activeFoodState = getBarracksActiveFoodState(
    targetLane,
    lane.laneIndex,
    normalizedBarracksId,
    sourceDescriptor.level
  );
  if (activeFoodState.foodRemaining <= 0)
    return 0;

  const spawnEntries = buildBarracksRosterSpawnEntries(game, lane, normalizedBarracksId, deps);
  if (spawnEntries.length === 0)
    return 0;

  const allowedSpawnEntries = trimSpawnEntriesToAvailableFood(spawnEntries, activeFoodState.foodRemaining);
  if (allowedSpawnEntries.length === 0)
    return 0;

  spawnBarracksRosterLayout(game, targetLane, allowedSpawnEntries, null, deps);
  lane.totalSendCount += allowedSpawnEntries.length;
  lane.sendCountThisRound += allowedSpawnEntries.length;
  return allowedSpawnEntries.length;
}

function applyBarracksSiteBuildAction(game, lane, barracksId, deps = {}) {
  const descriptor = describeBarracksSite(game, lane, barracksId);
  if (!descriptor)
    return { ok: false, reason: "Unknown barracks" };
  if (!descriptor.canBuild)
    return { ok: false, reason: descriptor.lockedReason || "Building is not available" };
  if (lane.gold < descriptor.buildCost)
    return { ok: false, reason: "Not enough gold" };
  const recordBalanceSpend = typeof deps.recordBalanceSpend === "function"
    ? deps.recordBalanceSpend
    : null;

  const siteState = descriptor.siteState;
  const nextLevel = 1;

  const goldBefore = Number(lane.gold);
  lane.gold -= descriptor.buildCost;
  const expectedGold = goldBefore - descriptor.buildCost;
  if (!Number.isFinite(goldBefore) || !Number.isFinite(Number(lane.gold)) || Number(lane.gold) !== expectedGold) {
    console.warn(
      `[BarracksValidation] Gold deduction mismatch during barracks purchase `
      + `lane=${lane.laneIndex ?? "unknown"} barracksId='${barracksId}' `
      + `before=${goldBefore} cost=${descriptor.buildCost} after=${lane.gold} expected=${expectedGold}`
    );
  }
  lane.totalBuildSpend += descriptor.buildCost;
  lane.buildSpendThisRound += descriptor.buildCost;
  siteState.isBuilt = false;
  siteState.level = 0;
  siteState.maxHp = 0;
  siteState.hp = 0;
  siteState.nextSendTick = 0;
  const construction = startBarracksSiteConstruction(game, siteState, nextLevel, BARRACKS_CONSTRUCTION_KINDS.build);
  if (construction && construction.totalTicks <= 0) {
    siteState.isBuilt = true;
    siteState.level = nextLevel;
    siteState.maxHp = getBarracksSiteBaseMaxHp();
    siteState.hp = siteState.maxHp;
    siteState.nextSendTick = getCurrentBarracksTick(game) + getBarracksSiteSendIntervalTicks(nextLevel);
  }
  if (!Array.isArray(siteState.costHistory))
    siteState.costHistory = [];
  siteState.costHistory.push({ cost: descriptor.buildCost });
  if (siteState.isBuilt)
    syncLegacyBarracksAggregate(lane);
  if (recordBalanceSpend)
    recordBalanceSpend(game, lane, "building", descriptor.buildCost, {
      buildingType: "barracks",
      barracksId,
    });
  return { ok: true };
}

function applyBarracksSiteUpgradeAction(game, lane, barracksId, deps = {}) {
  const descriptor = describeBarracksSite(game, lane, barracksId);
  if (!descriptor)
    return { ok: false, reason: "Unknown barracks" };
  if (!descriptor.isBuilt)
    return { ok: false, reason: descriptor.lockedReason || "Use Town Core to manage Barracks." };
  if (!descriptor.canUpgrade)
    return { ok: false, reason: descriptor.lockedReason || "Barracks upgrade unavailable" };
  if (lane.gold < descriptor.upgradeCost)
    return { ok: false, reason: "Not enough gold" };
  const recordBalanceSpend = typeof deps.recordBalanceSpend === "function"
    ? deps.recordBalanceSpend
    : null;

  const siteState = descriptor.siteState;
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  const currentRemaining = Math.max(0, Math.floor(Number(siteState.nextSendTick) || 0) - currentTick);
  const nextLevel = normalizeBarracksSiteLevel(descriptor.level + 1);
  const nextInterval = getBarracksSiteSendIntervalTicks(nextLevel);

  lane.gold -= descriptor.upgradeCost;
  lane.totalBuildSpend += descriptor.upgradeCost;
  lane.buildSpendThisRound += descriptor.upgradeCost;
  const construction = startBarracksSiteConstruction(game, siteState, nextLevel, BARRACKS_CONSTRUCTION_KINDS.upgrade);
  if (construction && construction.totalTicks <= 0) {
    siteState.level = nextLevel;
    siteState.maxHp = Math.max(getBarracksSiteBaseMaxHp(), Math.floor(Number(siteState.maxHp) || 0));
    siteState.hp = siteState.maxHp;
    siteState.nextSendTick = currentTick + Math.max(1, Math.min(currentRemaining || nextInterval, nextInterval));
    syncLegacyBarracksAggregate(lane);
  }
  if (!Array.isArray(siteState.costHistory))
    siteState.costHistory = [];
  siteState.costHistory.push({ cost: descriptor.upgradeCost });
  if (recordBalanceSpend)
    recordBalanceSpend(game, lane, "upgrade", descriptor.upgradeCost, {
      buildingType: "barracks",
      barracksId,
      upgradeKey: `level_${nextLevel}`,
    });
  return { ok: true };
}

function advanceBarracksSiteConstruction(game) {
  if (!game || !Array.isArray(game.lanes))
    return false;

  let changed = false;
  const currentTick = getCurrentBarracksTick(game);
  for (const lane of game.lanes) {
    if (!lane || !lane.barracksSiteStates || typeof lane.barracksSiteStates !== "object")
      continue;

    let laneChanged = false;
    for (const siteState of Object.values(lane.barracksSiteStates)) {
      const construction = getBarracksSiteConstructionState(siteState, game);
      if (!construction || construction.endTick > currentTick)
        continue;

      const wasBuilt = !!siteState.isBuilt;
      const previousLevel = Math.max(0, Math.floor(Number(siteState.level) || 0));
      const nextLevel = Math.max(1, normalizeBarracksSiteLevel(construction.targetLevel));
      const nextInterval = getBarracksSiteSendIntervalTicks(nextLevel);
      const currentRemaining = wasBuilt
        ? Math.max(0, Math.floor(Number(siteState.nextSendTick) || 0) - currentTick)
        : 0;

      siteState.isBuilt = true;
      siteState.level = nextLevel;
      siteState.maxHp = getBarracksSiteBaseMaxHp();
      siteState.hp = siteState.maxHp;
      siteState.nextSendTick = wasBuilt && previousLevel > 0
        ? currentTick + Math.max(1, Math.min(currentRemaining || nextInterval, nextInterval))
        : currentTick + nextInterval;
      clearBarracksSiteConstructionState(siteState);
      laneChanged = true;
      changed = true;
    }

    if (laneChanged)
      syncLegacyBarracksAggregate(lane);
  }

  return changed;
}

function buyBarracksUnit(game, laneIndex, lane, rosterKey, barracksId, count = 1, deps = {}) {
  const safeCount = Math.min(25, Math.max(1, Math.floor(Number(count) || 1)));
  logSpawnAuditLine(deps,
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
  if (!siteSnapshot.isBuilt || siteSnapshot.isDestroyed)
    return { ok: false, reason: siteSnapshot.lockedReason || "Use Town Core to manage Barracks." };

  const rosterEntry = Array.isArray(siteSnapshot.roster)
    ? siteSnapshot.roster.find((entry) => entry.rosterKey === rosterKey)
    : null;
  if (!rosterEntry)
    return { ok: false, reason: "Unknown barracks unit" };
  if (!rosterEntry.unlocked)
    return { ok: false, reason: rosterEntry.lockedReason || "Unit is locked" };
  if (!rosterEntry.availableForPurchase)
    return { ok: false, reason: rosterEntry.lockedReason || "A higher-tier version is now the current purchase for this branch" };

  const totalFoodCost = Math.max(0, Math.floor(Number(rosterEntry.foodCost) || getBarracksRosterFoodCost(rosterDef)) * safeCount);
  if (Math.max(0, Math.floor(Number(siteSnapshot.foodRemaining) || 0)) < totalFoodCost) {
    return {
      ok: false,
      reason: buildFoodLimitLockedReason(
        siteSnapshot.displayName || "Barracks",
        siteSnapshot.foodUsed,
        siteSnapshot.foodLimit,
        totalFoodCost
      ),
    };
  }

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
  logSpawnAuditLine(deps,
    `[BarracksTrace][ServerBuy] lane=${laneIndex} barracksId='${normalizedBarracksId}' `
    + `rosterKey='${rosterKey}' ownedCount=${siteCounts[rosterKey]} totalCost=${totalCost}`
  );
  logBarracksRosterState(lane, `after_buy:${normalizedBarracksId}:${rosterKey}`, deps);
  if (typeof deps.recordBalanceSpend === "function")
    deps.recordBalanceSpend(game, lane, "unit", totalCost, {
      buildingType: "barracks",
      barracksId: normalizedBarracksId,
      rosterKey,
      unitType: rosterEntry.unitTypeKey || rosterEntry.archetypeKey || rosterDef.archetypeKey || rosterKey,
      count: safeCount,
      estimatedPowerGain: totalCost,
    });
  return { ok: true };
}

function buyMarketUnit(game, laneIndex, lane, unitKey, count = 1, deps = {}) {
  const safeCount = Math.min(25, Math.max(1, Math.floor(Number(count) || 1)));
  console.log(
    `[MarketTrace][ServerBuy] action='buy_market_unit' lane=${laneIndex} ` +
    `unitKey='${unitKey}' count=${safeCount}`
  );

  const unitDef = getMarketRosterDefinition(unitKey);
  if (!unitDef)
    return { ok: false, reason: "Unknown market unit" };

  const marketPad = getFortressPadByBuildingType(lane, "market");
  if (!marketPad || Math.max(0, Math.floor(Number(marketPad.tier) || 0)) <= 0)
    return { ok: false, reason: "Build the Market first" };

  const marketRoster = createMarketRosterSnapshot(game, lane, deps);
  const marketEntry = Array.isArray(marketRoster)
    ? marketRoster.find((entry) => entry && entry.unitKey === unitKey)
    : null;
  if (!marketEntry)
    return { ok: false, reason: "Unknown market unit" };
  if (!marketEntry.unlocked)
    return { ok: false, reason: marketEntry.lockedReason || "Market unit is locked" };
  if (!marketEntry.availableForPurchase)
    return { ok: false, reason: marketEntry.lockedReason || "A higher-tier market worker is now the current purchase" };

  const marketFoodState = getMarketFoodState(lane, Math.max(0, Math.floor(Number(marketPad.tier) || 0)));
  const totalFoodCost = Math.max(0, Math.floor(Number(marketEntry.foodCost) || getMarketRosterFoodCost(unitDef)) * safeCount);
  if (marketFoodState.foodRemaining < totalFoodCost) {
    return {
      ok: false,
      reason: buildMarketCapacityLockedReason(
        marketFoodState.foodUsed,
        marketFoodState.foodLimit,
        totalFoodCost
      ),
    };
  }

  const totalCost = Math.max(0, marketEntry.buyCost * safeCount);
  if (lane.gold < totalCost)
    return { ok: false, reason: "Not enough gold" };

  lane.gold -= totalCost;
  lane.totalBuildSpend += totalCost;
  lane.buildSpendThisRound += totalCost;
  const marketCounts = getMarketRosterCounts(lane);
  if (!marketCounts)
    return { ok: false, reason: "Market roster state is unavailable" };
  marketCounts[unitKey] = Math.max(0, Math.floor(Number(marketCounts[unitKey]) || 0) + safeCount);
  removeOwnedMarketWorkers(game, lane);

  console.log(
    `[MarketTrace][ServerBuy] lane=${laneIndex} unitKey='${unitKey}' ` +
    `ownedCount=${marketCounts[unitKey]} totalCost=${totalCost}`
  );
  if (typeof deps.recordBalanceSpend === "function")
    deps.recordBalanceSpend(game, lane, "unit", totalCost, {
      buildingType: "market",
      unitType: marketEntry.unitTypeKey || marketEntry.archetypeKey || unitKey,
      rosterKey: unitKey,
      count: safeCount,
      estimatedPowerGain: totalCost,
    });
  return { ok: true };
}

function completeMarketWorkerLap(game, fallbackLane, unit) {
  if (!game || !unit)
    return 0;
  unit.marketLapCount = Math.max(0, Math.floor(Number(unit.marketLapCount) || 0)) + 1;
  unit.lastMarketLapTick = Math.floor(Number(game.tick) || 0);
  return 0;
}

function sellBarracksUnit(laneIndex, lane, rosterKey, barracksId, deps = {}) {
  logSpawnAuditLine(deps,
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
  const refund = getBarracksRosterSellRefund(rosterDef, deps);
  lane.gold += refund;
  logSpawnAuditLine(deps,
    `[BarracksTrace][ServerBuy] lane=${laneIndex} barracksId='${normalizedBarracksId}' `
    + `rosterKey='${rosterKey}' ownedCount=${siteCounts[rosterKey]}`
  );
  logBarracksRosterState(lane, `after_sell:${normalizedBarracksId}:${rosterKey}`, deps);
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
  if (!siteDescriptor.isBuilt || siteDescriptor.destroyed)
    return { ok: false, reason: siteDescriptor.lockedReason || "Use Town Core to manage Barracks." };

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
  if (typeof deps.recordBalanceSpend === "function")
    deps.recordBalanceSpend(game, lane, "hero", summonCost, {
      buildingType: "barracks",
      barracksId: normalizedBarracksId,
      heroKey: heroDef.heroKey,
      unitType: heroDef.archetypeKey,
      count: 1,
      estimatedPowerGain: summonCost,
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
  BARRACKS_CONSTRUCTION_KINDS,
  BARRACKS_ROLE_SORT_ORDER,
  BARRACKS_SPAWN_ROLE_ORDER,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  HERO_ROSTER_DEFS,
  MARKET_ROSTER_DEFS,
  getBarracksLevelDef,
  getBarracksSpeedMultForLevel,
  getBarracksSpeedMult,
  getFoodLimitForTier,
  getBarracksRosterFoodCost,
  getMarketRosterFoodCost,
  createBarracksRosterCounts,
  createBarracksSiteRosterCounts,
  createMarketRosterCounts,
  getBarracksSiteDef,
  getMarketRosterDefinition,
  normalizeBarracksSiteId,
  summarizeBarracksSiteCounts,
  summarizeBarracksSiteRosterEntries,
  getBarracksSiteCounts,
  getBarracksSiteFoodState,
  getBarracksActiveFoodState,
  getMarketRosterCounts,
  getMarketFoodState,
  getTotalOwnedMarketWorkers,
  getLaneMarketIncome,
  getLaneTotalIncome,
  hasOwnedBarracksUnits,
  getBarracksSiteTierRequirement,
  getBarracksSiteBuildCost,
  getBarracksSiteMaxLevel,
  normalizeBarracksSiteLevel,
  getBarracksSiteBaseMaxHp,
  getBarracksSiteSendIntervalTicks,
  getBarracksSiteConstructionDurationTicks,
  getBarracksSiteConstructionState,
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
  resolveMarketRosterUnlockContext,
  getBarracksRosterDefinition,
  getHeroRosterDefinition,
  getCurrentBarracksRosterDefinitionForBranch,
  getCurrentMarketRosterDefinitionForLane,
  getHeroRosterLockedReason,
  getMarketRosterLockedReason,
  getBarracksSiteCombatTarget,
  applyBarracksSiteDamage,
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
  advanceBarracksSiteConstruction,
  createBarracksRosterSnapshot,
  createMarketRosterSnapshot,
  getBarracksRoleSortIndex,
  getBarracksSpawnQueueRoleSortIndex,
  buildBarracksRosterSpawnEntries,
  getBarracksSpawnColumns,
  spawnBarracksRosterLayout,
  spawnScheduledBarracksRoster,
  applyBarracksSiteBuildAction,
  applyBarracksSiteUpgradeAction,
  buyBarracksUnit,
  buyMarketUnit,
  upgradeOwnedBarracksBranchUnits,
  upgradeOwnedMarketUnits,
  removeOwnedMarketWorkers,
  reapplyOwnedCombatUnitsForLane,
  completeMarketWorkerLap,
  sellBarracksUnit,
  deployBarracksHero,
  seedStartingCombatTestMilitia,
};
