"use strict";

const { getMaxBarracksLevel } = require("../../barracksLevels");

const DEFAULT_TEAM_HP_START = 20;
const FRONT_GATE_COMBAT_OFFSET = 10.2;

const FORTRESS_BUILD_STATES = Object.freeze({
  locked: "locked",
  availableToBuild: "available_to_build",
  built: "built",
  upgradeAvailable: "upgrade_available",
  maxTier: "max_tier",
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
  Object.freeze({ key: "core_03", displayName: "Inner Tower 03", gridX: 1, gridY: 21, combatOffsetX: -2.5, combatOffsetY: 4.0 }),
  Object.freeze({ key: "core_04", displayName: "Inner Tower 04", gridX: 3, gridY: 21, combatOffsetX: 0.0, combatOffsetY: 4.0 }),
  Object.freeze({ key: "core_05", displayName: "Inner Tower 05", gridX: 5, gridY: 21, combatOffsetX: 2.5, combatOffsetY: 4.0 }),
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
    displayName: "Civic",
    branchKey: "civic",
    branchLabel: "Civic Progression",
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
    startsBuilt: true,
    requiredTownCoreTier: 1,
    baseMaxHp: 260,
  },
  blacksmith: {
    displayName: "Blacksmith",
    branchKey: "blacksmith",
    branchLabel: "Blacksmith",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 225,
    buildCost: 60,
    upgradeCosts: {
      2: 95,
      3: 145,
    },
  },
  archery_tower: {
    displayName: "Archery Tower",
    branchKey: "archery",
    branchLabel: "Archery Tower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
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
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 205,
    buildCost: 70,
    upgradeCosts: {
      2: 105,
      3: 160,
    },
  },
  wizard_tower: {
    displayName: "Wizard Tower",
    branchKey: "wizard",
    branchLabel: "Wizard Tower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
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
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
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
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
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
    displayName: "Wall",
    branchKey: "wall",
    branchLabel: "Walls",
    progressionCategory: "Wall",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    dependencyRequirementsByTier: {
      2: Object.freeze([{ buildingType: "lumber_mill", minTier: 2 }]),
      3: Object.freeze([{ buildingType: "lumber_mill", minTier: 3 }]),
    },
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
    branchLabel: "Gates",
    progressionCategory: "Gate",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    dependencyRequirementsByTier: {
      2: Object.freeze([{ buildingType: "lumber_mill", minTier: 2 }]),
      3: Object.freeze([{ buildingType: "lumber_mill", minTier: 3 }]),
    },
    baseMaxHp: 260,
    buildCost: 20,
    upgradeCosts: {
      2: 40,
      3: 80,
    },
  },
  turret: {
    displayName: "Turret",
    branchKey: "base_tower",
    branchLabel: "Base Towers",
    progressionCategory: "BaseTower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    requiresLumberMill: true,
    requiresTurretTier3: false,
    dependencyRequirementsByTier: {
      1: Object.freeze([{ buildingType: "lumber_mill", minTier: 1 }]),
    },
    baseMaxHp: 210,
    buildCost: 40,
    upgradeCosts: {
      2: 80,
      3: 140,
    },
  },
  tower_archer: {
    displayName: "Archer Tower",
    branchKey: "archer_tower",
    branchLabel: "Archer Towers",
    progressionCategory: "ArcherTower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    requiresLumberMill: false,
    requiresTurretTier3: true,
    dependencyRequirementsByTier: {
      1: Object.freeze([{ buildingType: "turret", minTier: 3 }]),
    },
    baseMaxHp: 220,
    buildCost: 180,
    upgradeCosts: {
      2: 260,
      3: 380,
    },
  },
});

const FORTRESS_PAD_DEFS = Object.freeze([
  createFortressPadDef("town_core_pad", "town_core", "Civic", 3, 25),
  createFortressPadDef("barracks_pad", "barracks", "Barracks", 7, 25),
  createFortressPadDef("blacksmith_pad", "blacksmith", "Blacksmith", 1, 20),
  createFortressPadDef("temple_pad", "temple", "Temple", 5, 20),
  createFortressPadDef("wizard_tower_pad", "wizard_tower", "Wizard Tower", 7, 20),
  createFortressPadDef("archery_tower_pad", "archery_tower", "Archery Tower", 9, 20),
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

function createFortressPadStates(teamHpStart = DEFAULT_TEAM_HP_START) {
  return FORTRESS_PAD_DEFS.map((padDef) => {
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
      maxHp,
      hp: maxHp,
      costHistory: tier > 0 ? [] : null,
    };
  });
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

function getTownCoreTier(lane) {
  return Math.max(1, getHighestBuiltFortressPadTier(lane, "town_core") || 1);
}

function getLaneTownCorePad(lane) {
  return getFortressPadByBuildingType(lane, "town_core");
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

  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  const maxTier = getFortressMaxTier(padState.buildingType);
  const built = currentTier > 0;
  const nextTier = Math.min(maxTier, currentTier + 1);
  const targetTier = built ? nextTier : 1;
  const townCoreTier = getTownCoreTier(lane);
  const requiredTownCoreTier = getFortressRequiredTownCoreTier(padState.buildingType, targetTier);
  const dependencyLockedReason = getFortressDependencyLockedReason(lane, padState.buildingType, targetTier);
  const canBuild = !built
    && townCoreTier >= requiredTownCoreTier
    && !dependencyLockedReason;
  const canUpgrade = built
    && currentTier < maxTier
    && townCoreTier >= requiredTownCoreTier
    && !dependencyLockedReason;

  let buildState = FORTRESS_BUILD_STATES.locked;
  if (!built) {
    buildState = canBuild ? FORTRESS_BUILD_STATES.availableToBuild : FORTRESS_BUILD_STATES.locked;
  } else if (currentTier >= maxTier) {
    buildState = FORTRESS_BUILD_STATES.maxTier;
  } else if (canUpgrade) {
    buildState = FORTRESS_BUILD_STATES.upgradeAvailable;
  } else {
    buildState = FORTRESS_BUILD_STATES.built;
  }

  let lockedReason = null;
  if (!canBuild && !canUpgrade && buildState === FORTRESS_BUILD_STATES.locked) {
    lockedReason = dependencyLockedReason
      ? `Requires ${dependencyLockedReason}`
      : `Requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  } else if (built && currentTier < maxTier && !canUpgrade) {
    lockedReason = dependencyLockedReason
      ? `Upgrade requires ${dependencyLockedReason}`
      : `Upgrade requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  }

  return {
    buildingDef,
    buildState,
    canBuild,
    canUpgrade,
    lockedReason,
    currentTier,
    maxTier,
    buildCost: getFortressBuildCost(padState.buildingType),
    nextUpgradeCost: built && currentTier < maxTier
      ? getFortressUpgradeCost(padState.buildingType, nextTier, deps)
      : 0,
    requiredTownCoreTier,
  };
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
  const damageApplied = prevHp - padState.hp;
  if (padState.buildingType === "town_core" && damageApplied > 0)
    lane.lifeLossThisRound += damageApplied;

  recomputeTeamHpState(game, deps);
  if (padState.buildingType === "town_core" && damageApplied > 0 && deps.log && typeof deps.log.info === "function") {
    deps.log.info("[TownCoreTrace] core damaged", {
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
  return {
    damageApplied,
    destroyed: padState.hp <= 0,
    remainingHp: Math.max(0, Math.floor(Number(padState.hp) || 0)),
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
  const allegianceKey = typeof deps.resolveLaneAllegianceKey === "function"
    ? deps.resolveLaneAllegianceKey(lane)
    : null;

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
    tier: currentTier,
    maxTier: descriptor.maxTier,
    currentTierName: getBuildingTierDisplayName(padState.buildingType, currentTier),
    nextTier: currentTier < descriptor.maxTier ? currentTier + 1 : currentTier,
    nextTierName: currentTier < descriptor.maxTier
      ? getBuildingTierDisplayName(padState.buildingType, currentTier + 1)
      : getBuildingTierDisplayName(padState.buildingType, currentTier),
    isBuilt: built,
    canBuild: descriptor.canBuild,
    canUpgrade: descriptor.canUpgrade,
    buildCost: descriptor.buildCost,
    upgradeCost: Number.isFinite(descriptor.nextUpgradeCost) ? descriptor.nextUpgradeCost : 0,
    requiredTownCoreTier: descriptor.requiredTownCoreTier,
    requiredTownCoreTierName: getBuildingTierDisplayName("town_core", descriptor.requiredTownCoreTier),
    hp: currentHp,
    maxHp,
    lockedReason: descriptor.lockedReason,
  };
}

function applyFortressBuildOnPad(game, lane, padId, deps = {}) {
  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { ok: false, reason: "Unknown building pad" };
  if (padState.buildingType === "barracks")
    return { ok: false, reason: "Select Barracks Left, Center, or Right to buy that building." };

  const descriptor = describeFortressPad(game, lane, padState, deps);
  if (!descriptor)
    return { ok: false, reason: "Unknown building pad" };
  if (!descriptor.canBuild)
    return { ok: false, reason: descriptor.lockedReason || "Building is not available" };
  if (lane.gold < descriptor.buildCost)
    return { ok: false, reason: "Not enough gold" };

  lane.gold -= descriptor.buildCost;
  lane.totalBuildSpend += descriptor.buildCost;
  lane.buildSpendThisRound += descriptor.buildCost;
  padState.tier = 1;
  padState.maxHp = resolveFortressBuildingMaxHp(padState.buildingType, 1, game.teamHpMax);
  padState.hp = padState.maxHp;
  if (!Array.isArray(padState.costHistory))
    padState.costHistory = [];
  padState.costHistory.push({ cost: descriptor.buildCost });
  return { ok: true };
}

function applyFortressUpgrade(game, lane, padId, deps = {}) {
  const padState = getFortressPadState(lane, padId);
  if (!padState)
    return { ok: false, reason: "Unknown building pad" };
  if (padState.buildingType === "barracks")
    return { ok: false, reason: "Select Barracks Left, Center, or Right to upgrade that building." };

  const descriptor = describeFortressPad(game, lane, padState, deps);
  if (!descriptor)
    return { ok: false, reason: "Unknown building pad" };
  if (!descriptor.canUpgrade)
    return { ok: false, reason: descriptor.lockedReason || "Upgrade unavailable" };
  if (lane.gold < descriptor.nextUpgradeCost)
    return { ok: false, reason: "Not enough gold" };

  const nextTier = padState.tier + 1;
  lane.gold -= descriptor.nextUpgradeCost;
  lane.totalBuildSpend += descriptor.nextUpgradeCost;
  lane.buildSpendThisRound += descriptor.nextUpgradeCost;
  padState.tier = nextTier;
  padState.maxHp = resolveFortressBuildingMaxHp(padState.buildingType, nextTier, game.teamHpMax);
  padState.hp = padState.maxHp;
  if (!Array.isArray(padState.costHistory))
    padState.costHistory = [];
  padState.costHistory.push({ cost: descriptor.nextUpgradeCost });
  return { ok: true };
}

module.exports = {
  DEFAULT_TEAM_HP_START,
  FRONT_GATE_COMBAT_OFFSET,
  FORTRESS_BUILD_STATES,
  FORTRESS_BUILDING_DEFS,
  FORTRESS_PAD_DEFS,
  createFortressPadDef,
  createFortressPadStates,
  getFortressMaxTier,
  resolveFortressBuildingMaxHp,
  getFortressRequiredTownCoreTier,
  getFortressDependencyRequirements,
  getFortressBuildCost,
  getBuildingTierDisplayName,
  getBuildingBranchLabel,
  getBuildingDisplayName,
  getFortressPadState,
  getFortressPadByBuildingType,
  getHighestBuiltFortressPadTier,
  getTownCoreTier,
  getLaneTownCorePad,
  getLaneTownCoreHp,
  getLaneTownCoreMaxHp,
  getFortressDependencyLockedReason,
  getFortressUpgradeCost,
  describeFortressPad,
  getFortressPadCombatTarget,
  getLaneTownCoreCombatTarget,
  buildTownCoreStateSummary,
  markLaneDefeated,
  recomputeTeamHpState,
  applyFortressPadDamage,
  createFortressPadSnapshot,
  applyFortressBuildOnPad,
  applyFortressUpgrade,
};
