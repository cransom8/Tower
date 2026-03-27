"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 140,
    startIncome: 13,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

test("public multilane config exposes tower progression categories and live fortress pads", () => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);

  const config = simMl.createMLPublicConfig({ laneTeams: ["red", "blue"] });
  const buildingsByType = new Map(config.fortressBuildingConfigs.map((entry) => [entry.buildingType, entry]));
  const padCountsByType = config.fortressPadConfigs.reduce((counts, entry) => {
    const buildingType = entry && entry.buildingType;
    if (!buildingType) return counts;
    counts[buildingType] = (counts[buildingType] || 0) + 1;
    return counts;
  }, {});

  assert.deepEqual(
    {
      buildingType: buildingsByType.get("lumber_mill").buildingType,
      displayName: buildingsByType.get("lumber_mill").displayName,
      progressionCategory: buildingsByType.get("lumber_mill").progressionCategory,
      requiresLumberMill: buildingsByType.get("lumber_mill").requiresLumberMill,
      requiresTurretTier3: buildingsByType.get("lumber_mill").requiresTurretTier3,
      buildCost: buildingsByType.get("lumber_mill").buildCost,
      tierDisplayNames: buildingsByType.get("lumber_mill").tierDisplayNames.slice(1),
    },
    {
      buildingType: "lumber_mill",
      displayName: "Lumber Mill",
      progressionCategory: null,
      requiresLumberMill: false,
      requiresTurretTier3: false,
      buildCost: 50,
      tierDisplayNames: ["Tier 1", "Tier 2", "Tier 3"],
    }
  );

  assert.deepEqual(
    {
      displayName: buildingsByType.get("turret").displayName,
      branchLabel: buildingsByType.get("turret").branchLabel,
      progressionCategory: buildingsByType.get("turret").progressionCategory,
      requiresLumberMill: buildingsByType.get("turret").requiresLumberMill,
      requiresTurretTier3: buildingsByType.get("turret").requiresTurretTier3,
      buildCost: buildingsByType.get("turret").buildCost,
      tierDisplayNames: buildingsByType.get("turret").tierDisplayNames.slice(1),
    },
    {
      displayName: "Turret",
      branchLabel: "Base Towers",
      progressionCategory: "BaseTower",
      requiresLumberMill: true,
      requiresTurretTier3: false,
      buildCost: 40,
      tierDisplayNames: ["Tier 1", "Tier 2", "Tier 3"],
    }
  );

  assert.deepEqual(
    {
      displayName: buildingsByType.get("tower_archer").displayName,
      branchLabel: buildingsByType.get("tower_archer").branchLabel,
      progressionCategory: buildingsByType.get("tower_archer").progressionCategory,
      requiresLumberMill: buildingsByType.get("tower_archer").requiresLumberMill,
      requiresTurretTier3: buildingsByType.get("tower_archer").requiresTurretTier3,
      buildCost: buildingsByType.get("tower_archer").buildCost,
      tierDisplayNames: buildingsByType.get("tower_archer").tierDisplayNames.slice(1),
    },
    {
      displayName: "Archer Tower",
      branchLabel: "Archer Towers",
      progressionCategory: "ArcherTower",
      requiresLumberMill: false,
      requiresTurretTier3: true,
      buildCost: 180,
      tierDisplayNames: ["Tier 1", "Tier 2", "Tier 3"],
    }
  );

  assert.equal(padCountsByType.lumber_mill, 1, "lumber mill should have one fortress pad");
  assert.equal(padCountsByType.wall, 20, "authored fortress shell should expose twenty wall pads");
  assert.equal(padCountsByType.gate, 4, "authored fortress shell should expose four gate pads");
  assert.equal(padCountsByType.turret, 17, "authored fortress shell should expose seventeen tower hardpoints");
  assert.equal(padCountsByType.tower_archer || 0, 0, "archer tower conversion pads are not authored yet");
});
