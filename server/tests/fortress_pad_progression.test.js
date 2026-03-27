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

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function createGame(startGold = 2000) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold,
    startIncome: 0,
  });
}

function act(game, laneIndex, type, data) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, result.reason || `Expected '${type}' to succeed`);
  return result;
}

function fail(game, laneIndex, type, data, pattern) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, false, `Expected '${type}' to fail`);
  assert.match(String(result.reason || ""), pattern);
  return result;
}

test("public multilane config exposes the new branch pads and authored defense pad counts", () => {
  const config = simMl.createMLPublicConfig({ laneTeams: ["red", "blue"] });
  const buildingsByType = new Map(config.fortressBuildingConfigs.map((entry) => [entry.buildingType, entry]));
  const padCountsByType = config.fortressPadConfigs.reduce((counts, entry) => {
    const buildingType = entry && entry.buildingType;
    if (!buildingType) return counts;
    counts[buildingType] = (counts[buildingType] || 0) + 1;
    return counts;
  }, {});
  const padIds = new Set(config.fortressPadConfigs.map((entry) => entry && entry.padId).filter(Boolean));

  assert.equal(buildingsByType.get("stable").requiredTownCoreTier, 2);
  assert.equal(buildingsByType.get("workshop").requiredTownCoreTier, 2);
  assert.equal(buildingsByType.get("library").requiredTownCoreTier, 2);

  assert.equal(padIds.has("stable_pad"), true);
  assert.equal(padIds.has("workshop_pad"), true);
  assert.equal(padIds.has("library_pad"), true);
  assert.equal(padIds.has("lumber_mill_pad"), true);

  assert.equal(padCountsByType.market, 1);
  assert.equal(padCountsByType.stable, 1);
  assert.equal(padCountsByType.workshop, 1);
  assert.equal(padCountsByType.library, 1);
  assert.equal(padCountsByType.lumber_mill, 1);
  assert.equal(padCountsByType.wall, 20);
  assert.equal(padCountsByType.gate, 4);
  assert.equal(padCountsByType.turret, 17);
  assert.equal(padCountsByType.tower_archer || 0, 0);
});

test("live fortress pad actions honor new civic and lumber mill prerequisites", () => {
  const game = createGame();

  fail(game, 0, "build_on_pad", { padId: "stable_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "workshop_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "library_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "turret_front_left_pad" }, /Lumber Mill:\s*Tier 1/i);

  act(game, 0, "build_on_pad", { padId: "wall_front_left_01_pad" });
  act(game, 0, "build_on_pad", { padId: "gate_front_pad" });

  fail(game, 0, "upgrade_building", { padId: "wall_front_left_01_pad" }, /Lumber Mill:\s*Tier 2/i);
  fail(game, 0, "upgrade_building", { padId: "gate_front_pad" }, /Lumber Mill:\s*Tier 2/i);

  act(game, 0, "build_on_pad", { padId: "lumber_mill_pad" });
  act(game, 0, "build_on_pad", { padId: "turret_front_left_pad" });

  fail(game, 0, "upgrade_building", { padId: "wall_front_left_01_pad" }, /Lumber Mill:\s*Tier 2/i);

  act(game, 0, "upgrade_building", { padId: "town_core_pad" });
  act(game, 0, "build_on_pad", { padId: "stable_pad" });
  act(game, 0, "build_on_pad", { padId: "workshop_pad" });
  act(game, 0, "build_on_pad", { padId: "library_pad" });

  act(game, 0, "upgrade_building", { padId: "lumber_mill_pad" });
  act(game, 0, "upgrade_building", { padId: "wall_front_left_01_pad" });
  act(game, 0, "upgrade_building", { padId: "gate_front_pad" });
});
