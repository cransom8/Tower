"use strict";

const { DEFAULT_FORT_PRESENTATION_KEY } = require("../fortUnitCatalog");
const { getMaxBarracksLevel } = require("../../barracksLevels");
const { FORTRESS_BUILDING_DEFS } = require("./fortressSystem");

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
  getBarracksRosterDefinition,
  getHeroRosterDefinition,
  getBarracksRoleSortIndex,
  getBarracksSpawnQueueRoleSortIndex,
};
