"use strict";

const { getMaxBarracksLevel } = require("../../barracksLevels");
const { logSpawnAuditInfo: logVerboseAuditInfo } = require("./spawnAuditLogging");
const {
  BUILDING_LIFECYCLE_STATES,
  resolveBuildingLifecycleState,
  resolveLifecycleStateAfterDamage,
  resolveLifecycleStateAfterRepair,
  resolveLifecycleStateAfterConstructionStart,
  resolveLifecycleStateAfterConstructionComplete,
} = require("./buildingLifecycle");

const DEFAULT_TEAM_HP_START = 20;
const FRONT_GATE_COMBAT_OFFSET = 10.2;

const FORTRESS_BUILD_STATES = Object.freeze({
  locked: "locked",
  availableToBuild: "available_to_build",
  built: "built",
  constructing: "constructing",
  upgrading: "upgrading",
  destroyed: "destroyed",
  underRepair: "under_repair",
  upgradeAvailable: "upgrade_available",
  maxTier: "max_tier",
});

const FORTRESS_CONSTRUCTION_KINDS = Object.freeze({
  build: "build",
  upgrade: "upgrade",
});

const DEFAULT_FORTRESS_BUILD_DURATION_TICKS = 12 * 20;
const DEFAULT_FORTRESS_UPGRADE_DURATION_TICKS = 16 * 20;
const FORTRESS_CONSTRUCTION_DURATION_TICKS = Object.freeze({
  town_core: Object.freeze({ build: 0, upgrade: 18 * 20 }),
  barracks: Object.freeze({ build: 12 * 20, upgrade: 14 * 20 }),
  blacksmith: Object.freeze({ build: 12 * 20, upgrade: 14 * 20 }),
  archery_tower: Object.freeze({ build: 12 * 20, upgrade: 14 * 20 }),
  temple: Object.freeze({ build: 12 * 20, upgrade: 14 * 20 }),
  wizard_tower: Object.freeze({ build: 12 * 20, upgrade: 14 * 20 }),
  market: Object.freeze({ build: 10 * 20, upgrade: 14 * 20 }),
  stable: Object.freeze({ build: 12 * 20, upgrade: 16 * 20 }),
  workshop: Object.freeze({ build: 12 * 20, upgrade: 16 * 20 }),
  library: Object.freeze({ build: 12 * 20, upgrade: 16 * 20 }),
  lumber_mill: Object.freeze({ build: 10 * 20, upgrade: 14 * 20 }),
  wall: Object.freeze({ build: 6 * 20, upgrade: 10 * 20 }),
  gate: Object.freeze({ build: 6 * 20, upgrade: 10 * 20 }),
  turret: Object.freeze({ build: 8 * 20, upgrade: 12 * 20 }),
});

const SHARED_DEFENSE_BUILDING_TYPES = new Set(["wall", "gate", "turret"]);

const BUILDING_UPGRADE_SECTION_TYPES = Object.freeze({
  repeatable: "repeatable",
  oneTime: "one_time",
});

const DEFAULT_REPEATABLE_UPGRADE_COST = 100;
const DEFAULT_ONE_TIME_UPGRADE_COST = 500;
const REPEATABLE_UPGRADE_STEP_TO_MULTIPLIER = 0.01;
const WALL_HP_UPGRADE_KEY = "wall_hp";
const WALL_ARCHERS_UPGRADE_KEY = "wall_archers";
const TURRET_DAMAGE_UPGRADE_KEY = "turret_damage";
const WALL_ARCHER_BASE_DAMAGE = 10;
const WALL_ARCHER_RANGE = 4.1;
const WALL_ARCHER_ATTACK_COOLDOWN_TICKS = 18;
const WALL_ARCHER_PROJECTILE_TICKS = 6;
const WALL_ARCHER_DAMAGE_TYPE = "PIERCE";
const WALL_ARCHER_SPLASH_RADIUS = 1.5;

const BUILDING_PANEL_DESCRIPTIONS = Object.freeze({
  blacksmith: "Frontline melee durability and damage.",
  archery_tower: "Archer performance and wall archer support.",
  lumber_mill: "Fortress defenses and turret improvements.",
  wizard_tower: "Mage damage and spell utility.",
  temple: "Healing power and support utility.",
  stable: "Movement speed improvements for mounted branches.",
});

const BUILDING_UPGRADE_DEFS = Object.freeze({
  blacksmith: Object.freeze([
    Object.freeze({
      key: "shield_armor",
      displayName: "Shield Armor",
      affectedLabel: "Shield units",
      description: "Increase shield unit armor by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 10,
    }),
    Object.freeze({
      key: "frontline_damage",
      displayName: "Frontline Damage",
      affectedLabel: "Infantry and spear units",
      description: "Increase infantry and spear attack damage by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 20,
    }),
  ]),
  archery_tower: Object.freeze([
    Object.freeze({
      key: "archer_range",
      displayName: "Archer Range",
      affectedLabel: "Archers",
      description: "Increase archer range by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 10,
    }),
    Object.freeze({
      key: "archer_damage",
      displayName: "Archer Damage",
      affectedLabel: "Archers",
      description: "Increase archer damage by 0.1%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.1,
      sortIndex: 20,
    }),
    Object.freeze({
      key: WALL_ARCHERS_UPGRADE_KEY,
      displayName: "Wall Archers",
      affectedLabel: "Built turrets",
      description: "Place one archer on each built tower.",
      section: BUILDING_UPGRADE_SECTION_TYPES.oneTime,
      cost: DEFAULT_ONE_TIME_UPGRADE_COST,
      sortIndex: 30,
    }),
  ]),
  lumber_mill: Object.freeze([
    Object.freeze({
      key: "buy_turrets",
      displayName: "Buy Turrets",
      affectedLabel: "Fortress towers",
      description: "Turret purchase cost is not set yet.",
      section: BUILDING_UPGRADE_SECTION_TYPES.oneTime,
      cost: null,
      unavailableReason: "Turret purchase cost is not set yet.",
      sortIndex: 10,
    }),
    Object.freeze({
      key: WALL_HP_UPGRADE_KEY,
      displayName: "Wall Integrity",
      affectedLabel: "Walls",
      description: "Increase wall HP by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 20,
    }),
    Object.freeze({
      key: TURRET_DAMAGE_UPGRADE_KEY,
      displayName: "Turret Damage",
      affectedLabel: "Wall archers",
      description: "Increase tower archer damage by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 30,
    }),
  ]),
  wizard_tower: Object.freeze([
    Object.freeze({
      key: "mage_splash",
      displayName: "Splash Upgrade",
      affectedLabel: "Mages",
      description: "Mage attacks splash onto 2 nearby enemies.",
      section: BUILDING_UPGRADE_SECTION_TYPES.oneTime,
      cost: DEFAULT_ONE_TIME_UPGRADE_COST,
      sortIndex: 10,
    }),
    Object.freeze({
      key: "mage_damage",
      displayName: "Mage Damage",
      affectedLabel: "Mages",
      description: "Increase mage damage by 0.1%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.1,
      sortIndex: 20,
    }),
  ]),
  temple: Object.freeze([
    Object.freeze({
      key: "heal_strength",
      displayName: "Healing Strength",
      affectedLabel: "Temple healers",
      description: "Increase heal strength by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 10,
    }),
    Object.freeze({
      key: "chain_heal",
      displayName: "Chain Heal",
      affectedLabel: "Temple healers",
      description: "Each heal also jumps to 2 extra nearby allies.",
      section: BUILDING_UPGRADE_SECTION_TYPES.oneTime,
      cost: DEFAULT_ONE_TIME_UPGRADE_COST,
      sortIndex: 20,
    }),
  ]),
  stable: Object.freeze([
    Object.freeze({
      key: "run_speed",
      displayName: "Run Speed Boost",
      affectedLabel: "Stable units",
      description: "Increase run speed by 0.5%.",
      section: BUILDING_UPGRADE_SECTION_TYPES.repeatable,
      cost: DEFAULT_REPEATABLE_UPGRADE_COST,
      stepPct: 0.5,
      sortIndex: 10,
    }),
  ]),
});

function createFortressPadDef(
  padId,
  buildingType,
  displayName,
  gridX,
  gridY,
  options = {}
) {
  return Object.freeze({
    padId,
    buildingType,
    displayName,
    gridX,
    gridY,
    combatOffsetX: Number.isFinite(Number(options.combatOffsetX))
      ? Number(options.combatOffsetX)
      : null,
    combatOffsetY: Number.isFinite(Number(options.combatOffsetY))
      ? Number(options.combatOffsetY)
      : null,
  });
}

const FORTRESS_WALL_PAD_LAYOUT = Object.freeze([
  Object.freeze({ key: "front_left_01", displayName: "Front Left Wall 01", gridX: -2, gridY: 15, combatOffsetX: -4.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_02", displayName: "Front Left Wall 02", gridX: -1, gridY: 15, combatOffsetX: -3.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_03", displayName: "Front Left Wall 03", gridX: 0, gridY: 15, combatOffsetX: -2.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_04", displayName: "Front Left Wall 04", gridX: 1, gridY: 15, combatOffsetX: -1.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_06", displayName: "Front Left Wall 06", gridX: 2, gridY: 15, combatOffsetX: 0.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_07", displayName: "Front Left Wall 07", gridX: 3, gridY: 15, combatOffsetX: 1.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_right_01", displayName: "Front Right Wall 01", gridX: 4, gridY: 15, combatOffsetX: 2.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_right_02", displayName: "Front Right Wall 02", gridX: 5, gridY: 15, combatOffsetX: 3.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "back_left_01", displayName: "Back Left Wall 01", gridX: -2, gridY: 29, combatOffsetX: -4.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_left_02", displayName: "Back Left Wall 02", gridX: -1, gridY: 29, combatOffsetX: -3.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_left_04", displayName: "Back Left Wall 04", gridX: 0, gridY: 29, combatOffsetX: -2.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_left_05", displayName: "Back Left Wall 05", gridX: 1, gridY: 29, combatOffsetX: -1.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_right_01", displayName: "Back Right Wall 01", gridX: 4, gridY: 29, combatOffsetX: 1.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_right_02", displayName: "Back Right Wall 02", gridX: 5, gridY: 29, combatOffsetX: 2.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "right_side_03", displayName: "Right Side Wall 03", gridX: 7, gridY: 17, combatOffsetX: 6.0, combatOffsetY: 9.0 }),
  Object.freeze({ key: "right_side_04_a", displayName: "Right Side Wall 04A", gridX: 7, gridY: 19, combatOffsetX: 6.0, combatOffsetY: 7.0 }),
  Object.freeze({ key: "right_side_04_b", displayName: "Right Side Wall 04B", gridX: 7, gridY: 21, combatOffsetX: 6.0, combatOffsetY: 5.0 }),
  Object.freeze({ key: "right_side_05", displayName: "Right Side Wall 05", gridX: 7, gridY: 23, combatOffsetX: 6.0, combatOffsetY: 3.0 }),
  Object.freeze({ key: "right_side_06", displayName: "Right Side Wall 06", gridX: 7, gridY: 25, combatOffsetX: 6.0, combatOffsetY: 1.0 }),
  Object.freeze({ key: "right_side_07", displayName: "Right Side Wall 07", gridX: 7, gridY: 27, combatOffsetX: 6.0, combatOffsetY: -1.0 }),
]);

const FORTRESS_GATE_PAD_LAYOUT = Object.freeze([
  Object.freeze({ key: "front", displayName: "Front Gate", gridX: 3, gridY: 16, combatOffsetX: 0.0, combatOffsetY: FRONT_GATE_COMBAT_OFFSET }),
  Object.freeze({ key: "left", displayName: "Left Gate", gridX: -3, gridY: 24, combatOffsetX: -6.0, combatOffsetY: 1.0 }),
  Object.freeze({ key: "right", displayName: "Right Gate", gridX: 8, gridY: 24, combatOffsetX: 6.5, combatOffsetY: 1.0 }),
  Object.freeze({ key: "rear", displayName: "Rear Gate", gridX: 3, gridY: 30, combatOffsetX: 0.0, combatOffsetY: -6.0 }),
]);

const FORTRESS_TURRET_PAD_LAYOUT = Object.freeze([
  Object.freeze({ key: "front_left", displayName: "Front Left Tower", gridX: -2, gridY: 13, combatOffsetX: -5.0, combatOffsetY: 13.5 }),
  Object.freeze({ key: "front_left_05", displayName: "Front Left Tower 05", gridX: 0, gridY: 12, combatOffsetX: -2.5, combatOffsetY: 14.5 }),
  Object.freeze({ key: "front_right", displayName: "Front Right Tower", gridX: 6, gridY: 13, combatOffsetX: 5.0, combatOffsetY: 13.5 }),
  Object.freeze({ key: "front_gate_left", displayName: "Front Gate Tower Left", gridX: 1, gridY: 14, combatOffsetX: -2.5, combatOffsetY: 12.0 }),
  Object.freeze({ key: "front_gate_right", displayName: "Front Gate Tower Right", gridX: 5, gridY: 14, combatOffsetX: 2.5, combatOffsetY: 12.0 }),
  Object.freeze({ key: "back_left_03", displayName: "Back Left Tower 03", gridX: -2, gridY: 27, combatOffsetX: -5.0, combatOffsetY: -3.0 }),
  Object.freeze({ key: "back_left_06", displayName: "Back Left Tower 06", gridX: 0, gridY: 28, combatOffsetX: -2.5, combatOffsetY: -4.0 }),
  Object.freeze({ key: "back_left_07", displayName: "Back Left Tower 07", gridX: 2, gridY: 29, combatOffsetX: -0.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_right_03", displayName: "Back Right Tower 03", gridX: 6, gridY: 27, combatOffsetX: 5.0, combatOffsetY: -3.0 }),
  Object.freeze({ key: "rear_gate_left", displayName: "Rear Gate Tower Left", gridX: 1, gridY: 30, combatOffsetX: -2.5, combatOffsetY: -6.5 }),
  Object.freeze({ key: "rear_gate_right", displayName: "Rear Gate Tower Right", gridX: 5, gridY: 30, combatOffsetX: 2.5, combatOffsetY: -6.5 }),
  Object.freeze({ key: "right_side_05", displayName: "Right Side Tower 05", gridX: 8, gridY: 17, combatOffsetX: 7.4, combatOffsetY: 9.0 }),
  Object.freeze({ key: "right_side_06", displayName: "Right Side Tower 06", gridX: 8, gridY: 22, combatOffsetX: 7.4, combatOffsetY: 4.0 }),
  Object.freeze({ key: "right_side_07", displayName: "Right Side Tower 07", gridX: 8, gridY: 27, combatOffsetX: 7.4, combatOffsetY: -1.0 }),
]);

const FORTRESS_BUILDING_DEFS = Object.freeze({
  town_core: {
    displayName: "Town Core",
    branchKey: "town_core",
    branchLabel: "Town Core",
    tierDisplayNames: Object.freeze([null, "House", "Town Hall", "Keep", "Castle"]),
    maxTier: 4,
    startsBuilt: true,
    requiredTownCoreTier: 1,
    baseMaxHp: DEFAULT_TEAM_HP_START,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 2, 4: 3 },
    upgradeCosts: {
      2: 70,
      3: 110,
      4: 165,
    },
  },
  barracks: {
    displayName: "Barracks",
    branchKey: "barracks",
    branchLabel: "Barracks",
    tierDisplayNames: Object.freeze([null, "Barracks"]),
    maxTier: 1,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    baseMaxHp: 260,
    buildCost: 100,
  },
  blacksmith: {
    displayName: "Blacksmith",
    branchKey: "blacksmith",
    branchLabel: "Blacksmith",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 225,
    buildCost: 60,
    upgradeCosts: {
      2: 95,
      3: 145,
    },
  },
  archery_tower: {
    displayName: "Archery",
    branchKey: "archery",
    branchLabel: "Archery",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 190,
    buildCost: 50,
    upgradeCosts: {
      2: 85,
      3: 130,
    },
  },
  temple: {
    displayName: "Temple",
    branchKey: "temple",
    branchLabel: "Temple",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 205,
    buildCost: 70,
    upgradeCosts: {
      2: 105,
      3: 160,
    },
  },
  wizard_tower: {
    displayName: "Mage Tower",
    branchKey: "wizard",
    branchLabel: "Mage Tower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 195,
    buildCost: 75,
    upgradeCosts: {
      2: 115,
      3: 170,
    },
  },
  market: {
    displayName: "Market",
    branchKey: "market",
    branchLabel: "Market",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 200,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  stable: {
    displayName: "Stable",
    branchKey: "stable",
    branchLabel: "Stable",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 205,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  workshop: {
    displayName: "Workshop",
    branchKey: "workshop",
    branchLabel: "Workshop",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 205,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  library: {
    displayName: "Library",
    branchKey: "library",
    branchLabel: "Library",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 200,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  lumber_mill: {
    displayName: "Lumber Mill",
    branchKey: "lumber_mill",
    branchLabel: "Lumber Mill",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    requiresLumberMill: false,
    requiresTurretTier3: false,
    baseMaxHp: 210,
    buildCost: 50,
    upgradeCosts: {
      2: 80,
      3: 125,
    },
  },
  wall: {
    displayName: "Walls",
    branchKey: "wall",
    branchLabel: "Walls",
    progressionCategory: "Defense",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 180,
    buildCost: 20,
    upgradeCosts: {
      2: 40,
      3: 80,
    },
  },
  gate: {
    displayName: "Gate",
    branchKey: "gate",
    branchLabel: "Walls",
    progressionCategory: "Defense",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 260,
    buildCost: 20,
    upgradeCosts: {
      2: 40,
      3: 80,
    },
  },
  turret: {
    displayName: "Turret",
    branchKey: "turret",
    branchLabel: "Turrets",
    progressionCategory: "Defense",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    requiresLumberMill: false,
    requiresTurretTier3: false,
    baseMaxHp: 210,
    buildCost: 20,
    upgradeCosts: {
      2: 40,
      3: 80,
    },
  },
});

const FORTRESS_PAD_DEFS = Object.freeze([
  createFortressPadDef("town_core_pad", "town_core", "Town Core", 3, 25),
  createFortressPadDef("barracks_pad", "barracks", "Barracks", 7, 25),
  createFortressPadDef("blacksmith_pad", "blacksmith", "Blacksmith", 1, 20),
  createFortressPadDef("temple_pad", "temple", "Temple", 5, 20),
  createFortressPadDef("wizard_tower_pad", "wizard_tower", "Mage Tower", 7, 20),
  createFortressPadDef("archery_tower_pad", "archery_tower", "Archery", 9, 20),
  createFortressPadDef("market_pad", "market", "Market", 3, 20),
  createFortressPadDef("stable_pad", "stable", "Stable", 1, 15),
  createFortressPadDef("workshop_pad", "workshop", "Workshop", 3, 15),
  createFortressPadDef("library_pad", "library", "Library", 5, 15),
  createFortressPadDef("lumber_mill_pad", "lumber_mill", "Lumber Mill", 7, 15),
  ...FORTRESS_WALL_PAD_LAYOUT.map((slot) => createFortressPadDef(
    `wall_${slot.key}_pad`,
    "wall",
    slot.displayName,
    slot.gridX,
    slot.gridY,
    {
      combatOffsetX: slot.combatOffsetX,
      combatOffsetY: slot.combatOffsetY,
    }
  )),
  ...FORTRESS_GATE_PAD_LAYOUT.map((slot) => createFortressPadDef(
    `gate_${slot.key}_pad`,
    "gate",
    slot.displayName,
    slot.gridX,
    slot.gridY,
    {
      combatOffsetX: slot.combatOffsetX,
      combatOffsetY: slot.combatOffsetY,
    }
  )),
  ...FORTRESS_TURRET_PAD_LAYOUT.map((slot) => createFortressPadDef(
    `turret_${slot.key}_pad`,
    "turret",
    slot.displayName,
    slot.gridX,
    slot.gridY,
    {
      combatOffsetX: slot.combatOffsetX,
      combatOffsetY: slot.combatOffsetY,
    }
  )),
]);

function getFortressMaxTier(buildingType) {
  if (buildingType === "barracks")
    return Math.max(1, Math.floor(Number(getMaxBarracksLevel()) || 1));

  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef ? Math.max(1, Math.floor(Number(buildingDef.maxTier) || 1)) : 1;
}

function isSharedDefenseBuildingType(buildingType) {
  return SHARED_DEFENSE_BUILDING_TYPES.has(String(buildingType || "").trim().toLowerCase());
}

function getFortressActionBuildingType(buildingType) {
  return isSharedDefenseBuildingType(buildingType) ? "wall" : buildingType;
}

function getTownCoreRequirementLabel(requiredTownCoreTier) {
  return `Town Core: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
}

function resolveFortressBuildingMaxHp(buildingType, tier, teamHpStart = DEFAULT_TEAM_HP_START) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  if (!buildingDef)
    return 0;
  if (buildingType === "town_core")
    return Math.max(1, Math.floor(Number(teamHpStart) || DEFAULT_TEAM_HP_START));

  const safeTier = Math.max(1, Math.floor(Number(tier) || 1));
  const baseHp = Math.max(1, Number(buildingDef.baseMaxHp) || 100);
  return Math.floor(baseHp * (1 + (0.25 * (safeTier - 1))));
}

function createFortressPadState(padDef, teamHpStart = DEFAULT_TEAM_HP_START) {
  const buildingDef = FORTRESS_BUILDING_DEFS[padDef.buildingType];
  const tier = buildingDef && buildingDef.startsBuilt ? 1 : 0;
  const maxHp = tier > 0
    ? resolveFortressBuildingMaxHp(padDef.buildingType, tier, teamHpStart)
    : 0;
  return {
    padId: padDef.padId,
    buildingType: padDef.buildingType,
    displayName: padDef.displayName,
    gridX: padDef.gridX,
    gridY: padDef.gridY,
    combatOffsetX: Number.isFinite(Number(padDef.combatOffsetX))
      ? Number(padDef.combatOffsetX)
      : null,
    combatOffsetY: Number.isFinite(Number(padDef.combatOffsetY))
      ? Number(padDef.combatOffsetY)
      : null,
    tier,
    lifecycleState: tier > 0
      ? BUILDING_LIFECYCLE_STATES.active
      : null,
    maxHp,
    hp: maxHp,
    costHistory: tier > 0 ? [] : null,
    constructionKind: null,
    constructionTargetTier: 0,
      constructionEndTick: 0,
      constructionTotalTicks: 0,
    };
}

function createBuildingUpgradeState() {
  const state = {};
  for (const [buildingType, upgradeDefs] of Object.entries(BUILDING_UPGRADE_DEFS)) {
    const bucket = {};
    for (const upgradeDef of upgradeDefs) {
      bucket[upgradeDef.key] = {
        purchaseCount: 0,
        totalSpent: 0,
        costHistory: [],
      };
    }
    state[buildingType] = bucket;
  }
  return state;
}

function ensureBuildingUpgradeState(lane) {
  if (!lane || typeof lane !== "object")
    return {};

  if (!lane.buildingUpgradeState || typeof lane.buildingUpgradeState !== "object")
    lane.buildingUpgradeState = createBuildingUpgradeState();

  for (const [buildingType, upgradeDefs] of Object.entries(BUILDING_UPGRADE_DEFS)) {
    const bucket = lane.buildingUpgradeState[buildingType];
    if (!bucket || typeof bucket !== "object") {
      lane.buildingUpgradeState[buildingType] = {};
    }

    for (const upgradeDef of upgradeDefs) {
      const existing = lane.buildingUpgradeState[buildingType][upgradeDef.key];
      if (!existing || typeof existing !== "object") {
        lane.buildingUpgradeState[buildingType][upgradeDef.key] = {
          purchaseCount: 0,
          totalSpent: 0,
          costHistory: [],
        };
        continue;
      }

      existing.purchaseCount = Math.max(0, Math.floor(Number(existing.purchaseCount) || 0));
      existing.totalSpent = Math.max(0, Math.floor(Number(existing.totalSpent) || 0));
      if (!Array.isArray(existing.costHistory))
        existing.costHistory = [];
    }
  }

  return lane.buildingUpgradeState;
}

function getBuildingUpgradeDefinitionsForBuilding(buildingType) {
  const upgradeDefs = BUILDING_UPGRADE_DEFS[String(buildingType || "").trim().toLowerCase()];
  if (!Array.isArray(upgradeDefs))
    return [];
  return upgradeDefs.slice().sort((left, right) => (left.sortIndex || 0) - (right.sortIndex || 0));
}

function getBuildingUpgradeDefinition(buildingType, upgradeKey) {
  const normalizedKey = String(upgradeKey || "").trim().toLowerCase();
  if (!normalizedKey)
    return null;

  return getBuildingUpgradeDefinitionsForBuilding(buildingType).find((entry) => entry && entry.key === normalizedKey) || null;
}

function findBuildingUpgradeDefinition(upgradeKey) {
  const normalizedKey = String(upgradeKey || "").trim().toLowerCase();
  if (!normalizedKey)
    return null;

  for (const [buildingType, upgradeDefs] of Object.entries(BUILDING_UPGRADE_DEFS)) {
    const match = upgradeDefs.find((entry) => entry && entry.key === normalizedKey);
    if (match)
      return { buildingType, upgradeDef: match };
  }

  return null;
}

function getLaneBuildingUpgradeEntry(lane, buildingType, upgradeKey, createIfMissing = false) {
  const normalizedBuildingType = String(buildingType || "").trim().toLowerCase();
  const normalizedKey = String(upgradeKey || "").trim().toLowerCase();
  if (!normalizedBuildingType || !normalizedKey)
    return null;

  const upgradeState = createIfMissing
    ? ensureBuildingUpgradeState(lane)
    : (lane && lane.buildingUpgradeState);
  if (!upgradeState || typeof upgradeState !== "object")
    return null;

  if (!upgradeState[normalizedBuildingType] || typeof upgradeState[normalizedBuildingType] !== "object") {
    if (!createIfMissing)
      return null;
    upgradeState[normalizedBuildingType] = {};
  }

  if (!upgradeState[normalizedBuildingType][normalizedKey] || typeof upgradeState[normalizedBuildingType][normalizedKey] !== "object") {
    if (!createIfMissing)
      return null;
    upgradeState[normalizedBuildingType][normalizedKey] = {
      purchaseCount: 0,
      totalSpent: 0,
      costHistory: [],
    };
  }

  const entry = upgradeState[normalizedBuildingType][normalizedKey];
  entry.purchaseCount = Math.max(0, Math.floor(Number(entry.purchaseCount) || 0));
  entry.totalSpent = Math.max(0, Math.floor(Number(entry.totalSpent) || 0));
  if (!Array.isArray(entry.costHistory))
    entry.costHistory = [];
  return entry;
}

function getLaneBuildingUpgradePurchaseCount(lane, upgradeKey, buildingType = null) {
  if (buildingType) {
    const entry = getLaneBuildingUpgradeEntry(lane, buildingType, upgradeKey, false);
    return Math.max(0, Math.floor(Number(entry && entry.purchaseCount) || 0));
  }

  const resolved = findBuildingUpgradeDefinition(upgradeKey);
  if (!resolved)
    return 0;
  const entry = getLaneBuildingUpgradeEntry(lane, resolved.buildingType, resolved.upgradeDef.key, false);
  return Math.max(0, Math.floor(Number(entry && entry.purchaseCount) || 0));
}

function hasLaneBuildingUpgrade(lane, upgradeKey, buildingType = null) {
  return getLaneBuildingUpgradePurchaseCount(lane, upgradeKey, buildingType) > 0;
}

function createFortressPadStates(teamHpStart = DEFAULT_TEAM_HP_START) {
  return FORTRESS_PAD_DEFS.map((padDef) => createFortressPadState(padDef, teamHpStart));
}

function getCurrentFortressTick(game) {
  return Math.max(0, Math.floor(Number(game && game.tick) || 0));
}

function clearFortressConstructionState(padState) {
  if (!padState)
    return;

  padState.constructionKind = null;
  padState.constructionTargetTier = 0;
  padState.constructionEndTick = 0;
  padState.constructionTotalTicks = 0;
}

function getFortressConstructionDurationTicks(buildingType, targetTier, kind = FORTRESS_CONSTRUCTION_KINDS.build) {
  const durationDef = FORTRESS_CONSTRUCTION_DURATION_TICKS[buildingType] || null;
  const fallback = kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
    ? DEFAULT_FORTRESS_UPGRADE_DURATION_TICKS
    : DEFAULT_FORTRESS_BUILD_DURATION_TICKS;
  const configuredDuration = durationDef ? durationDef[kind] : null;
  const baseDuration = Number.isFinite(Number(configuredDuration))
    ? Math.max(0, Math.floor(Number(configuredDuration)))
    : fallback;
  const safeTargetTier = Math.max(1, Math.floor(Number(targetTier) || 1));
  const tierBonusTicks = kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
    ? Math.max(0, safeTargetTier - 2) * (2 * 20)
    : Math.max(0, safeTargetTier - 1) * 20;
  return Math.max(0, baseDuration + tierBonusTicks);
}

function getFortressConstructionState(padState, game = null) {
  if (!padState)
    return null;

  const targetTier = Math.max(0, Math.floor(Number(padState.constructionTargetTier) || 0));
  const totalTicks = Math.max(0, Math.floor(Number(padState.constructionTotalTicks) || 0));
  const endTick = Math.max(0, Math.floor(Number(padState.constructionEndTick) || 0));
  const kind = String(padState.constructionKind || "").trim().toLowerCase();
  if (targetTier <= 0 || totalTicks <= 0 || endTick <= 0)
    return null;
  if (kind !== FORTRESS_CONSTRUCTION_KINDS.build && kind !== FORTRESS_CONSTRUCTION_KINDS.upgrade)
    return null;

  const currentTick = getCurrentFortressTick(game);
  const remainingTicks = Math.max(0, endTick - currentTick);
  const progress01 = totalTicks > 0
    ? Math.min(1, Math.max(0, (totalTicks - remainingTicks) / totalTicks))
    : 1;

  return {
    kind,
    targetTier,
    endTick,
    totalTicks,
    remainingTicks,
    progress01,
  };
}

function getFortressPadLifecycleState(padState, game = null) {
  if (!padState)
    return null;

  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  const construction = getFortressConstructionState(padState, game);
  const lifecycleState = resolveBuildingLifecycleState({
    lifecycleState: padState.lifecycleState,
    built: currentTier > 0,
    constructionInProgress: !!construction && currentTier <= 0,
    hp: padState.hp,
    maxHp: padState.maxHp,
  });
  padState.lifecycleState = lifecycleState;
  return lifecycleState;
}

function applyFortressPadRepair(game, lane, padId, repairAmount, deps = {}) {
  if (!game || !lane || !padId)
    return { hpRestored: 0, hp: 0, maxHp: 0, lifecycleState: null };

  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { hpRestored: 0, hp: 0, maxHp: 0, lifecycleState: null };

  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  const construction = getFortressConstructionState(padState, game);
  const maxHp = Math.max(0, Math.floor(Number(padState.maxHp) || 0));
  const currentHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  const requestedRepair = Math.max(0, Math.floor(Number(repairAmount) || 0));
  if (currentTier <= 0 || construction || maxHp <= 0 || requestedRepair <= 0 || currentHp >= maxHp) {
    return {
      hpRestored: 0,
      hp: currentHp,
      maxHp,
      lifecycleState: getFortressPadLifecycleState(padState, game),
    };
  }

  padState.hp = Math.min(maxHp, currentHp + requestedRepair);
  padState.lifecycleState = resolveLifecycleStateAfterRepair({
    built: true,
    hp: padState.hp,
    maxHp,
  });
  if (String(padState.buildingType || "").trim().toLowerCase() === "town_core")
    recomputeTeamHpState(game, deps);

  return {
    hpRestored: Math.max(0, Math.floor(Number(padState.hp) || 0) - currentHp),
    hp: Math.max(0, Math.floor(Number(padState.hp) || 0)),
    maxHp,
    lifecycleState: getFortressPadLifecycleState(padState, game),
  };
}

function startFortressConstruction(game, padState, targetTier, kind = FORTRESS_CONSTRUCTION_KINDS.build) {
  if (!padState)
    return null;

  const safeKind = kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
    ? FORTRESS_CONSTRUCTION_KINDS.upgrade
    : FORTRESS_CONSTRUCTION_KINDS.build;
  const safeTargetTier = Math.max(1, Math.floor(Number(targetTier) || 1));
  const totalTicks = getFortressConstructionDurationTicks(padState.buildingType, safeTargetTier, safeKind);
  const currentTick = getCurrentFortressTick(game);

  if (totalTicks <= 0) {
    clearFortressConstructionState(padState);
    return {
      kind: safeKind,
      targetTier: safeTargetTier,
      totalTicks: 0,
      remainingTicks: 0,
      progress01: 1,
    };
  }

  padState.constructionKind = safeKind;
  padState.constructionTargetTier = safeTargetTier;
  padState.constructionTotalTicks = totalTicks;
  padState.constructionEndTick = currentTick + totalTicks;
  if (safeKind === FORTRESS_CONSTRUCTION_KINDS.build) {
    const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
    padState.lifecycleState = resolveLifecycleStateAfterConstructionStart(currentTier > 0, padState.lifecycleState);
  }

  return getFortressConstructionState(padState, game);
}

function advanceFortressConstruction(game, deps = {}) {
  if (!game || !Array.isArray(game.lanes))
    return false;

  let changed = false;
  const currentTick = getCurrentFortressTick(game);
  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.fortressPads))
      continue;

    for (const padState of lane.fortressPads) {
      const construction = getFortressConstructionState(padState, game);
      if (!construction || construction.endTick > currentTick)
        continue;

      const priorBlacksmithBranchDefs = construction.kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
        && String(padState.buildingType || "").trim().toLowerCase() === "blacksmith"
        && typeof deps.getCurrentBarracksRosterDefinitionForBranch === "function"
          ? new Map([
            ["infantry", deps.getCurrentBarracksRosterDefinitionForBranch(lane, "infantry")],
            ["polearm", deps.getCurrentBarracksRosterDefinitionForBranch(lane, "polearm")],
            ["shield", deps.getCurrentBarracksRosterDefinitionForBranch(lane, "shield")],
          ])
          : null;
      const priorMarketTierDef = construction.kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
        && String(padState.buildingType || "").trim().toLowerCase() === "market"
        && typeof deps.getCurrentMarketRosterDefinitionForLane === "function"
          ? deps.getCurrentMarketRosterDefinitionForLane(lane)
          : null;

      padState.tier = construction.targetTier;
      padState.maxHp = resolveFortressPadMaxHp(game, lane, padState, construction.targetTier);
      padState.hp = padState.maxHp;
      padState.lifecycleState = resolveLifecycleStateAfterConstructionComplete();
      clearFortressConstructionState(padState);

      if (priorBlacksmithBranchDefs
          && typeof deps.getCurrentBarracksRosterDefinitionForBranch === "function"
          && typeof deps.upgradeOwnedBarracksBranchUnits === "function") {
        for (const branchKey of ["infantry", "polearm", "shield"]) {
          const previousDef = priorBlacksmithBranchDefs.get(branchKey) || null;
          const nextDef = deps.getCurrentBarracksRosterDefinitionForBranch(lane, branchKey);
          if (!nextDef || !previousDef || nextDef.rosterKey === previousDef.rosterKey)
            continue;
          deps.upgradeOwnedBarracksBranchUnits(game, lane, branchKey, nextDef, deps);
        }
      }

      if (priorMarketTierDef
          && typeof deps.getCurrentMarketRosterDefinitionForLane === "function"
          && typeof deps.upgradeOwnedMarketUnits === "function") {
        const nextMarketTierDef = deps.getCurrentMarketRosterDefinitionForLane(lane);
        if (nextMarketTierDef && nextMarketTierDef.unitKey !== priorMarketTierDef.unitKey)
          deps.upgradeOwnedMarketUnits(game, lane, nextMarketTierDef, deps);
      }

      changed = true;
    }
  }

  if (changed)
    recomputeTeamHpState(game, deps);

  return changed;
}

function getFortressRequiredTownCoreTier(buildingType, targetTier) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  const safeTier = Math.max(1, Math.floor(Number(targetTier) || 1));
  if (!buildingDef)
    return safeTier;

  const perTier = buildingDef.requiredTownCoreTierByTier;
  if (perTier && Number.isFinite(Number(perTier[safeTier])))
    return Math.max(1, Math.floor(Number(perTier[safeTier]) || 1));

  const explicitTier = Math.floor(Number(buildingDef.requiredTownCoreTier) || 0);
  if (explicitTier > 0)
    return explicitTier;

  return safeTier;
}

function getFortressDependencyRequirements(buildingType, targetTier) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  const safeTier = Math.max(1, Math.floor(Number(targetTier) || 1));
  if (!buildingDef || !buildingDef.dependencyRequirementsByTier)
    return [];

  const requirements = buildingDef.dependencyRequirementsByTier[safeTier];
  return Array.isArray(requirements) ? requirements : [];
}

function getFortressBuildCost(buildingType) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef ? Math.max(0, Math.floor(Number(buildingDef.buildCost) || 0)) : 0;
}

function getBuildingTierDisplayName(buildingType, tier) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  const safeTier = Math.max(0, Math.floor(Number(tier) || 0));
  const tierNames = buildingDef && buildingDef.tierDisplayNames;
  if (Array.isArray(tierNames) && tierNames[safeTier])
    return String(tierNames[safeTier]);
  return safeTier > 0 ? `Tier ${safeTier}` : "Unbuilt";
}

function getBuildingBranchLabel(buildingType) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef && String(buildingDef.branchLabel || "").trim() !== ""
    ? buildingDef.branchLabel
    : buildingDef && buildingDef.displayName
      ? buildingDef.displayName
      : buildingType;
}

function getBuildingDisplayName(buildingType) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef && String(buildingDef.displayName || "").trim() !== ""
    ? buildingDef.displayName
    : buildingType;
}

function getBuildingPanelDescription(buildingType) {
  return BUILDING_PANEL_DESCRIPTIONS[String(buildingType || "").trim().toLowerCase()] || "";
}

function countBuiltFortressPadsByBuildingType(lane, buildingType) {
  if (!lane || !Array.isArray(lane.fortressPads))
    return 0;

  const normalizedBuildingType = String(buildingType || "").trim().toLowerCase();
  let total = 0;
  for (const pad of lane.fortressPads) {
    if (!pad || String(pad.buildingType || "").trim().toLowerCase() !== normalizedBuildingType)
      continue;
    if (Math.max(0, Math.floor(Number(pad.tier) || 0)) > 0 && Math.max(0, Math.floor(Number(pad.hp) || 0)) > 0)
      total += 1;
  }
  return total;
}

function getRepeatableUpgradeMultiplier(lane, upgradeKey, stepPct, buildingType = null) {
  const purchaseCount = getLaneBuildingUpgradePurchaseCount(lane, upgradeKey, buildingType);
  return 1 + (purchaseCount * ((Number(stepPct) || 0) * REPEATABLE_UPGRADE_STEP_TO_MULTIPLIER));
}

function getWallHpUpgradeMultiplier(lane) {
  return getRepeatableUpgradeMultiplier(lane, WALL_HP_UPGRADE_KEY, 0.5, "lumber_mill");
}

function resolveFortressPadMaxHp(game, lane, padState, tier) {
  const maxHp = resolveFortressBuildingMaxHp(
    padState && padState.buildingType,
    tier,
    game && Number.isFinite(Number(game.teamHpMax))
      ? Number(game.teamHpMax)
      : DEFAULT_TEAM_HP_START
  );
  if (!padState)
    return maxHp;

  if (String(padState.buildingType || "").trim().toLowerCase() === "wall")
    return Math.max(1, Math.floor(maxHp * getWallHpUpgradeMultiplier(lane)));

  return maxHp;
}

function applyWallDurabilityBonuses(game, lane) {
  if (!game || !lane || !Array.isArray(lane.fortressPads))
    return false;

  let changed = false;
  for (const padState of lane.fortressPads) {
    if (!padState || String(padState.buildingType || "").trim().toLowerCase() !== "wall")
      continue;

    const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
    if (currentTier <= 0)
      continue;

    const previousMaxHp = Math.max(1, Math.floor(Number(padState.maxHp) || 1));
    const hpRatio = Math.max(0, Math.min(1, (Number(padState.hp) || previousMaxHp) / previousMaxHp));
    const upgradedMaxHp = resolveFortressPadMaxHp(game, lane, padState, currentTier);
    if (upgradedMaxHp === previousMaxHp)
      continue;

    padState.maxHp = upgradedMaxHp;
    padState.hp = Math.max(0, Math.min(upgradedMaxHp, Math.round(upgradedMaxHp * hpRatio)));
    changed = true;
  }

  return changed;
}

function getFortressPadState(lane, padId) {
  if (!lane || !Array.isArray(lane.fortressPads))
    return null;
  return lane.fortressPads.find((pad) => pad && pad.padId === padId) || null;
}

function getFortressPadByBuildingType(lane, buildingType) {
  if (!lane || !Array.isArray(lane.fortressPads))
    return null;
  return lane.fortressPads.find((pad) => pad && pad.buildingType === buildingType) || null;
}

function getHighestBuiltFortressPadTier(lane, buildingType) {
  if (!lane || !Array.isArray(lane.fortressPads) || !buildingType)
    return 0;

  let bestTier = 0;
  for (const pad of lane.fortressPads) {
    if (!pad || pad.buildingType !== buildingType)
      continue;
    bestTier = Math.max(bestTier, Math.max(0, Math.floor(Number(pad.tier) || 0)));
  }
  return bestTier;
}

function resolveTownCoreRestoreTeamHpStart(game, lane) {
  const candidates = [
    Number(game && game.teamHpMax),
    Number(lane && lane.lives),
    DEFAULT_TEAM_HP_START,
  ];
  for (const value of candidates) {
    if (Number.isFinite(value) && value > 0)
      return Math.max(1, Math.floor(value));
  }

  return DEFAULT_TEAM_HP_START;
}

function ensureTownCorePadState(lane, game = null) {
  if (!lane)
    return null;

  if (!Array.isArray(lane.fortressPads))
    lane.fortressPads = [];

  const townCorePadDef = FORTRESS_PAD_DEFS.find((pad) => pad && pad.padId === "town_core_pad");
  if (!townCorePadDef)
    throw new Error("[TownCoreValidation] Missing required fortress pad definition 'town_core_pad'.");

  let corePad = lane.fortressPads.find((pad) => pad && pad.padId === townCorePadDef.padId) || null;
  if (!corePad) {
    corePad = createFortressPadState(townCorePadDef, resolveTownCoreRestoreTeamHpStart(game, lane));
    lane.fortressPads.unshift(corePad);
    console.warn(
      `[TownCoreValidation] Restored missing Town Core pad for lane ${lane.laneIndex ?? "unknown"}.`
    );
    return corePad;
  }

  const currentTier = Math.max(0, Math.floor(Number(corePad.tier) || 0));
  if (currentTier > 0) {
    corePad.lifecycleState = getFortressPadLifecycleState(corePad, game);
    return corePad;
  }

  const restoredMaxHp = resolveFortressBuildingMaxHp(
    "town_core",
    1,
    resolveTownCoreRestoreTeamHpStart(game, lane)
  );
  corePad.tier = 1;
  corePad.maxHp = restoredMaxHp;
  corePad.hp = restoredMaxHp;
  if (!Array.isArray(corePad.costHistory))
    corePad.costHistory = [];
  clearFortressConstructionState(corePad);
  corePad.lifecycleState = BUILDING_LIFECYCLE_STATES.active;
  console.warn(
    `[TownCoreValidation] Restored Town Core for lane ${lane.laneIndex ?? "unknown"} because its pad state was invalid.`
  );
  return corePad;
}

function getTownCoreTier(lane) {
  const corePad = ensureTownCorePadState(lane);
  return corePad ? Math.max(1, Math.floor(Number(corePad.tier) || 1)) : 1;
}

function getLaneTownCorePad(lane) {
  return ensureTownCorePadState(lane);
}

function getLaneTownCoreHp(lane) {
  const corePad = getLaneTownCorePad(lane);
  return corePad ? Math.max(0, Math.floor(Number(corePad.hp) || 0)) : 0;
}

function getLaneTownCoreMaxHp(lane) {
  const corePad = getLaneTownCorePad(lane);
  return corePad ? Math.max(1, Math.floor(Number(corePad.maxHp) || 1)) : 1;
}

function getFortressDependencyLockedReason(lane, buildingType, targetTier) {
  const requirements = getFortressDependencyRequirements(buildingType, targetTier);
  for (const requirement of requirements) {
    if (!requirement || !requirement.buildingType)
      continue;

    const requiredTier = Math.max(1, Math.floor(Number(requirement.minTier) || 1));
    const builtTier = getHighestBuiltFortressPadTier(lane, requirement.buildingType);
    if (builtTier >= requiredTier)
      continue;

    return `${getBuildingDisplayName(requirement.buildingType)}: ${getBuildingTierDisplayName(requirement.buildingType, requiredTier)}`;
  }

  return null;
}

function getFortressUpgradeCost(buildingType, nextTier, deps = {}) {
  if (buildingType === "barracks") {
    const barracksCost = typeof deps.getBarracksUpgradeCost === "function"
      ? deps.getBarracksUpgradeCost(nextTier)
      : Infinity;
    return Number.isFinite(Number(barracksCost))
      ? Math.max(0, Math.floor(Number(barracksCost)))
      : Infinity;
  }

  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  if (!buildingDef || !buildingDef.upgradeCosts)
    return Infinity;
  const cost = buildingDef.upgradeCosts[nextTier];
  return Number.isFinite(cost) ? Math.max(0, Math.floor(cost)) : Infinity;
}

function describeFortressPad(_game, lane, padState, deps = {}) {
  if (!padState)
    return null;

  const buildingDef = FORTRESS_BUILDING_DEFS[padState.buildingType];
  if (!buildingDef)
    return null;

  const construction = getFortressConstructionState(padState, _game);
  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  const maxTier = getFortressMaxTier(padState.buildingType);
  const built = currentTier > 0;
  const lifecycleState = getFortressPadLifecycleState(padState, _game);
  const destroyed = lifecycleState === BUILDING_LIFECYCLE_STATES.destroyed;
  const underRepair = lifecycleState === BUILDING_LIFECYCLE_STATES.underRepair;
  const nextTier = Math.min(maxTier, currentTier + 1);
  const targetTier = construction
    ? construction.targetTier
    : built ? nextTier : 1;
  const actionBuildingType = getFortressActionBuildingType(padState.buildingType);
  const townCoreTier = getTownCoreTier(lane);
  const requiredTownCoreTier = getFortressRequiredTownCoreTier(actionBuildingType, targetTier);
  const dependencyLockedReason = construction
    ? null
    : getFortressDependencyLockedReason(lane, actionBuildingType, targetTier);
  const canBuild = !built
    && !construction
    && townCoreTier >= requiredTownCoreTier
    && !dependencyLockedReason;
  const canUpgrade = built
    && !construction
    && !destroyed
    && !underRepair
    && currentTier < maxTier
    && townCoreTier >= requiredTownCoreTier
    && !dependencyLockedReason;

  let buildState = FORTRESS_BUILD_STATES.locked;
  if (destroyed) {
    buildState = FORTRESS_BUILD_STATES.destroyed;
  } else if (construction) {
    buildState = construction.kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
      ? FORTRESS_BUILD_STATES.upgrading
      : FORTRESS_BUILD_STATES.constructing;
  } else if (underRepair) {
    buildState = FORTRESS_BUILD_STATES.underRepair;
  } else if (!built) {
    buildState = canBuild ? FORTRESS_BUILD_STATES.availableToBuild : FORTRESS_BUILD_STATES.locked;
  } else if (currentTier >= maxTier) {
    buildState = FORTRESS_BUILD_STATES.maxTier;
  } else if (canUpgrade) {
    buildState = FORTRESS_BUILD_STATES.upgradeAvailable;
  } else {
    buildState = FORTRESS_BUILD_STATES.built;
  }

  let lockedReason = null;
  if (construction) {
    lockedReason = construction.kind === FORTRESS_CONSTRUCTION_KINDS.upgrade
      ? "Upgrade in progress"
      : "Construction in progress";
  } else if (destroyed) {
    lockedReason = "Destroyed";
  } else if (underRepair) {
    lockedReason = "Under repair";
  } else if (!canBuild && !canUpgrade && buildState === FORTRESS_BUILD_STATES.locked) {
    lockedReason = dependencyLockedReason
      ? `Requires ${dependencyLockedReason}`
      : `Requires ${getTownCoreRequirementLabel(requiredTownCoreTier)}`;
  } else if (built && currentTier < maxTier && !canUpgrade) {
    lockedReason = dependencyLockedReason
      ? `Upgrade requires ${dependencyLockedReason}`
      : `Upgrade requires ${getTownCoreRequirementLabel(requiredTownCoreTier)}`;
  }

  return {
    buildingDef,
    buildState,
    canBuild,
    canUpgrade,
    lockedReason,
    currentTier,
    maxTier,
    targetTier,
    construction,
    lifecycleState,
    destroyed,
    underRepair,
    buildCost: getFortressBuildCost(actionBuildingType),
    nextUpgradeCost: built && currentTier < maxTier
      ? getFortressUpgradeCost(actionBuildingType, nextTier, deps)
      : 0,
    requiredTownCoreTier,
  };
}

function getSharedDefenseGroupPads(lane) {
  if (!lane || !Array.isArray(lane.fortressPads))
    return [];

  return lane.fortressPads.filter((pad) => pad && isSharedDefenseBuildingType(pad.buildingType));
}

function applySharedDefenseGroupAction(game, lane, padState, deps = {}, kind = FORTRESS_CONSTRUCTION_KINDS.build) {
  const groupPads = getSharedDefenseGroupPads(lane);
  if (groupPads.length <= 0)
    return { ok: false, reason: "Unknown defense group" };

  const representativePad = groupPads.find((pad) => pad && pad.buildingType === "wall") || groupPads[0];
  if (!representativePad)
    return { ok: false, reason: "Unknown defense group" };

  const descriptor = describeFortressPad(game, lane, representativePad, deps);
  if (!descriptor)
    return { ok: false, reason: "Unknown defense group" };

  const isUpgrade = kind === FORTRESS_CONSTRUCTION_KINDS.upgrade;
  if (isUpgrade && !descriptor.canUpgrade)
    return { ok: false, reason: descriptor.lockedReason || "Upgrade unavailable" };
  if (!isUpgrade && !descriptor.canBuild)
    return { ok: false, reason: descriptor.lockedReason || "Building is not available" };

  const cost = isUpgrade ? descriptor.nextUpgradeCost : descriptor.buildCost;
  if (lane.gold < cost)
    return { ok: false, reason: "Not enough gold" };

  const targetTier = isUpgrade
    ? Math.max(1, descriptor.currentTier + 1)
    : 1;

  lane.gold -= cost;
  lane.totalBuildSpend += cost;
  lane.buildSpendThisRound += cost;

  for (const groupPad of groupPads) {
    if (!groupPad)
      continue;

    if (!isUpgrade) {
      groupPad.tier = 0;
      groupPad.maxHp = 0;
      groupPad.hp = 0;
    }

    const construction = startFortressConstruction(game, groupPad, targetTier, kind);
    if (construction && construction.totalTicks <= 0) {
      groupPad.tier = targetTier;
      groupPad.maxHp = resolveFortressPadMaxHp(game, lane, groupPad, targetTier);
      groupPad.hp = groupPad.maxHp;
      groupPad.lifecycleState = resolveLifecycleStateAfterConstructionComplete();
    }
  }

  if (!Array.isArray(padState.costHistory))
    padState.costHistory = [];
  padState.costHistory.push({ cost });

  recomputeTeamHpState(game, deps);
  return { ok: true };
}

function getFortressPadCombatTarget(lane, padState, positionOverride = null, deps = {}) {
  if (!lane || !padState)
    return null;

  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  if (currentTier <= 0)
    return null;
  const currentHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  if (currentHp <= 0)
    return null;

  const axes = typeof deps.getLaneCombatAxes === "function"
    ? deps.getLaneCombatAxes(lane.laneIndex)
    : null;
  const authoredPadPosition = typeof deps.getPadWorldPosition === "function"
    ? deps.getPadWorldPosition(lane.laneIndex, padState.gridX, padState.gridY)
    : null;
  const fallbackPos = Number.isFinite(Number(padState.combatOffsetX))
    && Number.isFinite(Number(padState.combatOffsetY))
    && axes
    ? {
        x: axes.core.x + (axes.lateral.x * Number(padState.combatOffsetX)) + (axes.forward.x * Number(padState.combatOffsetY)),
        y: axes.core.y + (axes.lateral.y * Number(padState.combatOffsetX)) + (axes.forward.y * Number(padState.combatOffsetY)),
      }
    : authoredPadPosition;
  const pos = positionOverride && Number.isFinite(positionOverride.x) && Number.isFinite(positionOverride.y)
    ? { x: Number(positionOverride.x), y: Number(positionOverride.y) }
    : fallbackPos;
  if (!pos || !Number.isFinite(Number(pos.x)) || !Number.isFinite(Number(pos.y)))
    return null;

  return {
    id: padState.padId,
    unitId: padState.padId,
    laneIndex: lane.laneIndex,
    kind: "fortress_pad",
    padId: padState.padId,
    buildingType: padState.buildingType,
    type: padState.buildingType,
    posX: Number(pos.x),
    posY: Number(pos.y),
    hp: currentHp,
    maxHp: Math.max(1, Math.floor(Number(padState.maxHp) || 1)),
  };
}

function getLaneTownCoreCombatTarget(lane, deps = {}) {
  if (!lane)
    return null;
  const corePad = getLaneTownCorePad(lane);
  if (!corePad)
    return null;
  const objectivePoint = typeof deps.getPadWorldPosition === "function"
    ? deps.getPadWorldPosition(lane.laneIndex, corePad.gridX, corePad.gridY)
    : null;
  return getFortressPadCombatTarget(lane, corePad, objectivePoint, deps);
}

function buildTownCoreStateSummary(game, deps = {}) {
  if (!game || !Array.isArray(game.lanes))
    return [];

  return game.lanes.map((lane) => {
    const corePad = getLaneTownCorePad(lane);
    return {
      laneIndex: lane ? lane.laneIndex : null,
      side: lane ? lane.side : null,
      eliminated: !!(lane && lane.eliminated),
      corePadId: corePad ? corePad.padId : null,
      coreHp: corePad ? Math.max(0, Math.floor(Number(corePad.hp) || 0)) : 0,
      coreMaxHp: corePad ? Math.max(1, Math.floor(Number(corePad.maxHp) || 1)) : 1,
      waveUnits: lane && Array.isArray(lane.units)
        ? lane.units.filter((unit) => unit && unit.hp > 0 && typeof deps.isScheduledWaveUnit === "function" && deps.isScheduledWaveUnit(unit)).length
        : 0,
      defenders: lane && Array.isArray(lane.units)
        ? lane.units.filter((unit) => unit && unit.hp > 0 && unit.isDefender).length
        : 0,
    };
  });
}

function markLaneDefeated(game, lane, defeatContext = null, deps = {}) {
  if (!lane || lane.eliminated)
    return;

  lane.eliminated = true;
  const corePad = getLaneTownCorePad(lane);
  if (deps.log && typeof deps.log.warn === "function") {
    deps.log.warn("[TownCoreTrace] lane eliminated", {
      tick: game && Number.isInteger(game.tick) ? game.tick : null,
      laneIndex: lane.laneIndex,
      side: lane.side,
      reason: defeatContext && defeatContext.reason ? defeatContext.reason : "town_core_destroyed",
      corePadId: corePad ? corePad.padId : null,
      coreHp: corePad ? Math.max(0, Math.floor(Number(corePad.hp) || 0)) : 0,
      coreMaxHp: corePad ? Math.max(1, Math.floor(Number(corePad.maxHp) || 1)) : 1,
      remainingActiveLanes: game && Array.isArray(game.lanes)
        ? game.lanes.filter((candidate) => candidate && candidate !== lane && !candidate.eliminated).map((candidate) => ({
            laneIndex: candidate.laneIndex,
            coreHp: getLaneTownCoreHp(candidate),
            coreMaxHp: getLaneTownCoreMaxHp(candidate),
          }))
        : [],
      preservedOccupiers: Array.isArray(lane.units) && typeof deps.shouldKeepUnitAfterLaneDefeat === "function"
        ? lane.units.filter((unit) => deps.shouldKeepUnitAfterLaneDefeat(lane, unit)).length
        : 0,
    });
  }

  if (typeof deps.shouldKeepUnitAfterLaneDefeat === "function") {
    lane.units = (lane.units || []).filter((unit) => deps.shouldKeepUnitAfterLaneDefeat(lane, unit));
    lane.spawnQueue = (lane.spawnQueue || []).filter((unit) => deps.shouldKeepUnitAfterLaneDefeat(lane, unit));
  }
  lane.projectiles = [];
}

function recomputeTeamHpState(game, deps = {}) {
  if (!game || !Array.isArray(game.lanes))
    return;

  const totals = { left: 0, right: 0 };
  const maxTotals = { left: 0, right: 0 };

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    const hp = getLaneTownCoreHp(lane);
    const maxHp = getLaneTownCoreMaxHp(lane);
    lane.lives = hp;

    if (hp <= 0)
      markLaneDefeated(game, lane, { reason: "town_core_destroyed" }, deps);

    if (lane.side === "left" || lane.side === "right") {
      totals[lane.side] += hp;
      maxTotals[lane.side] += maxHp;
    }
  }

  game.teamHp = totals;
  game.teamHpMax = Math.max(maxTotals.left, maxTotals.right, 0);
}

function applyFortressPadDamage(game, lane, padId, damage, deps = {}) {
  const recordBalanceStructureDamage = typeof deps.recordBalanceStructureDamage === "function"
    ? deps.recordBalanceStructureDamage
    : null;
  if (!game || !lane || lane.eliminated || !padId)
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };

  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };

  const prevHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  if (prevHp <= 0)
    return { damageApplied: 0, destroyed: true, remainingHp: 0 };

  const appliedDamage = Math.max(0, Math.floor(Number(damage) || 0));
  if (appliedDamage <= 0)
    return { damageApplied: 0, destroyed: false, remainingHp: prevHp };

  padState.hp = Math.max(0, prevHp - appliedDamage);
  padState.lifecycleState = resolveLifecycleStateAfterDamage(padState.lifecycleState, {
    built: Math.max(0, Math.floor(Number(padState.tier) || 0)) > 0,
    constructionInProgress: false,
    hp: padState.hp,
    maxHp: padState.maxHp,
  });
  const damageApplied = prevHp - padState.hp;
  if (padState.buildingType === "town_core" && damageApplied > 0)
    lane.lifeLossThisRound += damageApplied;

  recomputeTeamHpState(game, deps);
  if (padState.buildingType === "town_core" && damageApplied > 0) {
    logVerboseAuditInfo(deps, "[TownCoreTrace] core damaged", {
      tick: game.tick,
      laneIndex: lane.laneIndex,
      padId,
      damageApplied,
      previousHp: prevHp,
      remainingHp: Math.max(0, Math.floor(Number(padState.hp) || 0)),
      maxHp: Math.max(1, Math.floor(Number(padState.maxHp) || 1)),
      laneEliminated: !!lane.eliminated,
      teamHp: game.teamHp,
      coreStates: buildTownCoreStateSummary(game, deps),
    });
  }
  if (recordBalanceStructureDamage && damageApplied > 0)
    recordBalanceStructureDamage(game, lane, padState.buildingType, damageApplied, {});
  return {
    damageApplied,
    destroyed: padState.hp <= 0,
    remainingHp: Math.max(0, Math.floor(Number(padState.hp) || 0)),
  };
}

function formatBuildingUpgradePct(stepPct, purchaseCount) {
  const safeStepPct = Number(stepPct) || 0;
  const safePurchaseCount = Math.max(0, Math.floor(Number(purchaseCount) || 0));
  const totalPct = safeStepPct * safePurchaseCount;
  const decimals = Number.isInteger(safeStepPct) ? 0 : 1;
  return `+${totalPct.toFixed(decimals)}%`;
}

function buildBuildingUpgradeCurrentBonusText(lane, padState, upgradeDef, purchaseCount) {
  if (!upgradeDef)
    return "";

  if (upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.repeatable)
    return formatBuildingUpgradePct(upgradeDef.stepPct, purchaseCount);

  if (upgradeDef.key === WALL_ARCHERS_UPGRADE_KEY) {
    const builtTurretCount = countBuiltFortressPadsByBuildingType(lane, "turret");
    return purchaseCount > 0
      ? `Active on ${builtTurretCount} turret${builtTurretCount === 1 ? "" : "s"}`
      : "Not purchased";
  }

  return purchaseCount > 0 ? "Unlocked" : "Not purchased";
}

function buildBuildingUpgradeNextBonusText(upgradeDef, purchaseCount) {
  if (!upgradeDef)
    return "";

  if (upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.repeatable)
    return formatBuildingUpgradePct(upgradeDef.stepPct, purchaseCount + 1);

  return purchaseCount > 0 ? "Purchased" : "Unlock once";
}

function buildBuildingUpgradeLockedReason(game, lane, padState, descriptor, upgradeDef, purchaseCount) {
  if (!lane || !padState || !descriptor || !upgradeDef)
    return "Upgrade unavailable";

  if (descriptor.construction)
    return `${descriptor.buildingDef.displayName} is under construction.`;

  if (descriptor.destroyed)
    return `${descriptor.buildingDef.displayName} is destroyed.`;

  if (descriptor.underRepair)
    return `${descriptor.buildingDef.displayName} is under repair.`;

  if (descriptor.currentTier <= 0)
    return `Build ${descriptor.buildingDef.displayName} first.`;

  if (upgradeDef.unavailableReason)
    return upgradeDef.unavailableReason;

  if (upgradeDef.key === WALL_ARCHERS_UPGRADE_KEY && countBuiltFortressPadsByBuildingType(lane, "turret") <= 0)
    return "Build at least one turret first.";

  if (upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.oneTime && purchaseCount > 0)
    return "Already purchased.";

  const cost = Math.max(0, Math.floor(Number(upgradeDef.cost) || 0));
  if (lane.gold < cost)
    return `Need ${Math.max(0, cost - Math.floor(Number(lane.gold) || 0))}g`;

  return null;
}

function createBuildingUpgradeSnapshot(game, lane, padState, descriptor, upgradeDef) {
  if (!padState || !descriptor || !upgradeDef)
    return null;

  const entry = getLaneBuildingUpgradeEntry(lane, padState.buildingType, upgradeDef.key, false);
  const purchaseCount = Math.max(0, Math.floor(Number(entry && entry.purchaseCount) || 0));
  const lockedReason = buildBuildingUpgradeLockedReason(
    game,
    lane,
    padState,
    descriptor,
    upgradeDef,
    purchaseCount
  );
  const currentBonusPct = upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.repeatable
    ? (Number(upgradeDef.stepPct) || 0) * purchaseCount
    : 0;
  const nextBonusPct = upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.repeatable
    ? currentBonusPct + (Number(upgradeDef.stepPct) || 0)
    : currentBonusPct;

  return {
    buildingType: padState.buildingType,
    upgradeKey: upgradeDef.key,
    upgradeName: upgradeDef.displayName,
    affectedLabel: upgradeDef.affectedLabel || "",
    description: upgradeDef.description || "",
    section: upgradeDef.section,
    sectionLabel: upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.repeatable
      ? "Repeatable upgrades"
      : "One-time upgrades",
    sortIndex: Math.max(0, Math.floor(Number(upgradeDef.sortIndex) || 0)),
    cost: Number.isFinite(Number(upgradeDef.cost))
      ? Math.max(0, Math.floor(Number(upgradeDef.cost)))
      : 0,
    purchaseCount,
    totalSpent: Math.max(0, Math.floor(Number(entry && entry.totalSpent) || 0)),
    isRepeatable: upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.repeatable,
    isOneTime: upgradeDef.section === BUILDING_UPGRADE_SECTION_TYPES.oneTime,
    isPurchased: purchaseCount > 0,
    canPurchase: !lockedReason,
    lockedReason,
    currentBonusPct,
    nextBonusPct,
    currentBonusText: buildBuildingUpgradeCurrentBonusText(lane, padState, upgradeDef, purchaseCount),
    nextBonusText: buildBuildingUpgradeNextBonusText(upgradeDef, purchaseCount),
  };
}

function getLaneWallArcherTurretDefenseProfile(lane) {
  if (!lane || !hasLaneBuildingUpgrade(lane, WALL_ARCHERS_UPGRADE_KEY, "archery_tower"))
    return null;

  const turretPadIds = Array.isArray(lane.fortressPads)
    ? lane.fortressPads
      .filter((pad) =>
        pad
        && pad.buildingType === "turret"
        && Math.max(0, Math.floor(Number(pad.tier) || 0)) > 0
        && Math.max(0, Math.floor(Number(pad.hp) || 0)) > 0)
      .map((pad) => pad.padId)
    : [];
  if (turretPadIds.length <= 0)
    return null;

  const turretDamageMultiplier = getRepeatableUpgradeMultiplier(lane, TURRET_DAMAGE_UPGRADE_KEY, 0.5, "lumber_mill");
  return {
    turretPadIds,
    turretCount: turretPadIds.length,
    damage: Math.max(1, Math.round(WALL_ARCHER_BASE_DAMAGE * turretDamageMultiplier)),
    range: WALL_ARCHER_RANGE,
    attackCooldownTicks: WALL_ARCHER_ATTACK_COOLDOWN_TICKS,
    projectileTicks: WALL_ARCHER_PROJECTILE_TICKS,
    damageType: WALL_ARCHER_DAMAGE_TYPE,
    splashRadius: WALL_ARCHER_SPLASH_RADIUS,
  };
}

function createFortressPadSnapshot(game, lane, padState, deps = {}) {
  const descriptor = describeFortressPad(game, lane, padState, deps);
  if (!descriptor)
    return null;

  const buildingDef = descriptor.buildingDef;
  const currentTier = descriptor.currentTier;
  const built = currentTier > 0;
  const currentHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  const maxHp = Math.max(0, Math.floor(Number(padState.maxHp) || 0));
  const construction = descriptor.construction;
  const allegianceKey = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(lane)
    : null;
  const foodState = padState.buildingType === "market" && typeof deps.getMarketFoodState === "function"
    ? deps.getMarketFoodState(lane, currentTier)
    : null;
  const buildingUpgrades = getBuildingUpgradeDefinitionsForBuilding(padState.buildingType)
    .map((upgradeDef) => createBuildingUpgradeSnapshot(game, lane, padState, descriptor, upgradeDef))
    .filter(Boolean);

  return {
    padId: padState.padId,
    allegianceKey,
    ownerLaneIndex: lane && Number.isInteger(lane.laneIndex) ? lane.laneIndex : -1,
    gridX: padState.gridX,
    gridY: padState.gridY,
    buildingType: padState.buildingType,
    buildingName: buildingDef.displayName,
    displayName: padState.displayName,
    branchKey: buildingDef.branchKey || padState.buildingType,
    branchLabel: getBuildingBranchLabel(padState.buildingType),
    buildState: descriptor.buildState,
    lifecycleState: descriptor.lifecycleState,
    tier: currentTier,
    maxTier: descriptor.maxTier,
    currentTierName: getBuildingTierDisplayName(padState.buildingType, currentTier),
    nextTier: descriptor.targetTier,
    nextTierName: descriptor.targetTier > currentTier
      ? getBuildingTierDisplayName(padState.buildingType, descriptor.targetTier)
      : getBuildingTierDisplayName(padState.buildingType, currentTier),
    isBuilt: built,
    isConstructing: !!construction,
    constructionKind: construction ? construction.kind : null,
    constructionTargetTier: construction ? construction.targetTier : currentTier,
    constructionTargetTierName: getBuildingTierDisplayName(
      padState.buildingType,
      construction ? construction.targetTier : currentTier
    ),
    constructionTimerTicksRemaining: construction ? construction.remainingTicks : 0,
    constructionTimerTotalTicks: construction ? construction.totalTicks : 0,
    constructionProgress01: construction ? construction.progress01 : 0,
    isDestroyed: !!descriptor.destroyed,
    isUnderRepair: !!descriptor.underRepair,
    canBuild: descriptor.canBuild,
    canUpgrade: descriptor.canUpgrade,
    buildCost: descriptor.buildCost,
    upgradeCost: Number.isFinite(descriptor.nextUpgradeCost) ? descriptor.nextUpgradeCost : 0,
    requiredTownCoreTier: descriptor.requiredTownCoreTier,
    requiredTownCoreTierName: getBuildingTierDisplayName("town_core", descriptor.requiredTownCoreTier),
    foodLimit: foodState ? foodState.foodLimit : 0,
    foodUsed: foodState ? foodState.foodUsed : 0,
    foodRemaining: foodState ? foodState.foodRemaining : 0,
    isAtFoodLimit: foodState ? !!foodState.isAtFoodLimit : false,
    upgradePanelDescription: getBuildingPanelDescription(padState.buildingType),
    buildingUpgrades,
    hp: currentHp,
    maxHp,
    lockedReason: descriptor.lockedReason,
  };
}

function applyFortressBuildingUpgradePurchase(game, lane, padId, upgradeKey, deps = {}) {
  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { ok: false, reason: "Unknown building pad" };

  const descriptor = describeFortressPad(game, lane, padState, deps);
  if (!descriptor)
    return { ok: false, reason: "Unknown building pad" };

  const upgradeDef = getBuildingUpgradeDefinition(padState.buildingType, upgradeKey);
  if (!upgradeDef)
    return { ok: false, reason: "This building has no such upgrade" };

  const entry = getLaneBuildingUpgradeEntry(lane, padState.buildingType, upgradeDef.key, true);
  const purchaseCount = Math.max(0, Math.floor(Number(entry && entry.purchaseCount) || 0));
  const lockedReason = buildBuildingUpgradeLockedReason(
    game,
    lane,
    padState,
    descriptor,
    upgradeDef,
    purchaseCount
  );
  if (lockedReason)
    return { ok: false, reason: lockedReason };

  const cost = Math.max(0, Math.floor(Number(upgradeDef.cost) || 0));
  const recordBalanceSpend = typeof deps.recordBalanceSpend === "function"
    ? deps.recordBalanceSpend
    : null;
  if (cost <= 0)
    return { ok: false, reason: "Upgrade cost is invalid" };

  lane.gold -= cost;
  lane.totalBuildSpend += cost;
  lane.buildSpendThisRound += cost;
  entry.purchaseCount += 1;
  entry.totalSpent += cost;
  entry.costHistory.push({ cost });

  if (upgradeDef.key === WALL_HP_UPGRADE_KEY)
    applyWallDurabilityBonuses(game, lane);

  if (typeof deps.reapplyOwnedCombatUnitsForLane === "function")
    deps.reapplyOwnedCombatUnitsForLane(game, lane);

  recomputeTeamHpState(game, deps);
  if (recordBalanceSpend)
    recordBalanceSpend(game, lane, "upgrade", cost, {
      buildingType: padState.buildingType,
      padId,
      upgradeKey,
    });
  return { ok: true };
}

function applyFortressBuildOnPad(game, lane, padId, deps = {}) {
  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { ok: false, reason: "Unknown building pad" };
  if (isSharedDefenseBuildingType(padState.buildingType))
    return applySharedDefenseGroupAction(game, lane, padState, deps, FORTRESS_CONSTRUCTION_KINDS.build);
  if (padState.buildingType === "barracks")
    return { ok: false, reason: "Use Town Core to manage Barracks." };

  const descriptor = describeFortressPad(game, lane, padState, deps);
  if (!descriptor)
    return { ok: false, reason: "Unknown building pad" };
  if (!descriptor.canBuild)
    return { ok: false, reason: descriptor.lockedReason || "Building is not available" };
  if (lane.gold < descriptor.buildCost)
    return { ok: false, reason: "Not enough gold" };
  const recordBalanceSpend = typeof deps.recordBalanceSpend === "function"
    ? deps.recordBalanceSpend
    : null;

  lane.gold -= descriptor.buildCost;
  lane.totalBuildSpend += descriptor.buildCost;
  lane.buildSpendThisRound += descriptor.buildCost;
  padState.tier = 0;
  padState.maxHp = 0;
  padState.hp = 0;
  const construction = startFortressConstruction(game, padState, 1, FORTRESS_CONSTRUCTION_KINDS.build);
  if (construction && construction.totalTicks <= 0) {
    padState.tier = 1;
    padState.maxHp = resolveFortressPadMaxHp(game, lane, padState, 1);
    padState.hp = padState.maxHp;
    padState.lifecycleState = resolveLifecycleStateAfterConstructionComplete();
  }
  if (!Array.isArray(padState.costHistory))
    padState.costHistory = [];
  padState.costHistory.push({ cost: descriptor.buildCost });
  recomputeTeamHpState(game, deps);
  if (recordBalanceSpend)
    recordBalanceSpend(game, lane, "building", descriptor.buildCost, {
      buildingType: padState.buildingType,
      padId,
    });
  return { ok: true };
}

function applyFortressUpgrade(game, lane, padId, deps = {}) {
  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { ok: false, reason: "Unknown building pad" };
  if (isSharedDefenseBuildingType(padState.buildingType))
    return applySharedDefenseGroupAction(game, lane, padState, deps, FORTRESS_CONSTRUCTION_KINDS.upgrade);
  if (padState.buildingType === "barracks")
    return { ok: false, reason: "Use Town Core to manage Barracks." };

  const descriptor = describeFortressPad(game, lane, padState, deps);
  if (!descriptor)
    return { ok: false, reason: "Unknown building pad" };
  if (!descriptor.canUpgrade)
    return { ok: false, reason: descriptor.lockedReason || "Upgrade unavailable" };
  if (lane.gold < descriptor.nextUpgradeCost)
    return { ok: false, reason: "Not enough gold" };
  const recordBalanceSpend = typeof deps.recordBalanceSpend === "function"
    ? deps.recordBalanceSpend
    : null;

  const priorBlacksmithBranchDefs = String(padState.buildingType || "").trim().toLowerCase() === "blacksmith"
    && typeof deps.getCurrentBarracksRosterDefinitionForBranch === "function"
      ? new Map([
        ["infantry", deps.getCurrentBarracksRosterDefinitionForBranch(lane, "infantry")],
        ["polearm", deps.getCurrentBarracksRosterDefinitionForBranch(lane, "polearm")],
        ["shield", deps.getCurrentBarracksRosterDefinitionForBranch(lane, "shield")],
      ])
      : null;
  const priorMarketTierDef = String(padState.buildingType || "").trim().toLowerCase() === "market"
    && typeof deps.getCurrentMarketRosterDefinitionForLane === "function"
      ? deps.getCurrentMarketRosterDefinitionForLane(lane)
      : null;
  const nextTier = padState.tier + 1;
  lane.gold -= descriptor.nextUpgradeCost;
  lane.totalBuildSpend += descriptor.nextUpgradeCost;
  lane.buildSpendThisRound += descriptor.nextUpgradeCost;
  const construction = startFortressConstruction(game, padState, nextTier, FORTRESS_CONSTRUCTION_KINDS.upgrade);
  if (construction && construction.totalTicks <= 0) {
    padState.tier = nextTier;
    padState.maxHp = resolveFortressPadMaxHp(game, lane, padState, nextTier);
    padState.hp = padState.maxHp;
    padState.lifecycleState = resolveLifecycleStateAfterConstructionComplete();
  }
  if (!Array.isArray(padState.costHistory))
    padState.costHistory = [];
  padState.costHistory.push({ cost: descriptor.nextUpgradeCost });

  if (priorBlacksmithBranchDefs
      && typeof deps.getCurrentBarracksRosterDefinitionForBranch === "function"
      && typeof deps.upgradeOwnedBarracksBranchUnits === "function") {
    for (const branchKey of ["infantry", "polearm", "shield"]) {
      const previousDef = priorBlacksmithBranchDefs.get(branchKey) || null;
      const nextDef = deps.getCurrentBarracksRosterDefinitionForBranch(lane, branchKey);
      if (!nextDef || !previousDef || nextDef.rosterKey === previousDef.rosterKey)
        continue;
      deps.upgradeOwnedBarracksBranchUnits(game, lane, branchKey, nextDef, deps);
    }
  }

  if (priorMarketTierDef
      && typeof deps.getCurrentMarketRosterDefinitionForLane === "function"
      && typeof deps.upgradeOwnedMarketUnits === "function") {
    const nextMarketTierDef = deps.getCurrentMarketRosterDefinitionForLane(lane);
    if (nextMarketTierDef && nextMarketTierDef.unitKey !== priorMarketTierDef.unitKey)
      deps.upgradeOwnedMarketUnits(game, lane, nextMarketTierDef, deps);
  }
  if (recordBalanceSpend)
    recordBalanceSpend(game, lane, "upgrade", descriptor.nextUpgradeCost, {
      buildingType: padState.buildingType,
      padId,
      upgradeKey: `tier_${nextTier}`,
    });

  return { ok: true };
}

module.exports = {
  DEFAULT_TEAM_HP_START,
  FRONT_GATE_COMBAT_OFFSET,
  FORTRESS_BUILD_STATES,
  BUILDING_LIFECYCLE_STATES,
  FORTRESS_CONSTRUCTION_KINDS,
  FORTRESS_BUILDING_DEFS,
  BUILDING_UPGRADE_DEFS,
  BUILDING_UPGRADE_SECTION_TYPES,
  FORTRESS_PAD_DEFS,
  createFortressPadDef,
  createFortressPadStates,
  createBuildingUpgradeState,
  ensureBuildingUpgradeState,
  getFortressConstructionDurationTicks,
  getFortressConstructionState,
  advanceFortressConstruction,
  getFortressMaxTier,
  resolveFortressBuildingMaxHp,
  getFortressRequiredTownCoreTier,
  getFortressDependencyRequirements,
  getFortressBuildCost,
  getBuildingTierDisplayName,
  getBuildingBranchLabel,
  getBuildingDisplayName,
  getBuildingPanelDescription,
  getBuildingUpgradeDefinitionsForBuilding,
  getBuildingUpgradeDefinition,
  isSharedDefenseBuildingType,
  ensureTownCorePadState,
  getFortressPadState,
  getFortressPadByBuildingType,
  getHighestBuiltFortressPadTier,
  getLaneBuildingUpgradeEntry,
  getLaneBuildingUpgradePurchaseCount,
  hasLaneBuildingUpgrade,
  getTownCoreTier,
  getLaneTownCorePad,
  getLaneTownCoreHp,
  getLaneTownCoreMaxHp,
  getFortressDependencyLockedReason,
  getFortressUpgradeCost,
  describeFortressPad,
  getFortressPadLifecycleState,
  getFortressPadCombatTarget,
  getLaneTownCoreCombatTarget,
  buildTownCoreStateSummary,
  markLaneDefeated,
  recomputeTeamHpState,
  applyFortressPadDamage,
  applyFortressPadRepair,
  getLaneWallArcherTurretDefenseProfile,
  createFortressPadSnapshot,
  applyFortressBuildOnPad,
  applyFortressUpgrade,
  applyFortressBuildingUpgradePurchase,
};
