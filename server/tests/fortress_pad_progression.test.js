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

function laneSnapshot(game, laneIndex = 0) {
  return simMl.createMLSnapshot(game).lanes[laneIndex];
}

function findPad(snapshotLane, padId) {
  return (snapshotLane && snapshotLane.fortressPads || []).find((pad) => pad && pad.padId === padId) || null;
}

function advanceUntil(game, predicate, maxTicks = 4000) {
  for (let tick = 0; tick < maxTicks; tick += 1) {
    if (predicate())
      return;
    simMl.mlTick(game);
  }

  assert.fail("Timed out waiting for multilane state to settle");
}

function finishPadConstruction(game, laneIndex, padId) {
  advanceUntil(game, () => {
    const pad = findPad(laneSnapshot(game, laneIndex), padId);
    return !!(pad && !pad.isConstructing);
  });
}

function upgradeTownCoreToTier(game, laneIndex, targetTier) {
  let lane = laneSnapshot(game, laneIndex);
  let townCore = findPad(lane, "town_core_pad");
  assert.ok(townCore, "expected the Town Core pad to exist");

  while ((townCore && townCore.tier) < targetTier) {
    act(game, laneIndex, "upgrade_building", { padId: "town_core_pad" });
    finishPadConstruction(game, laneIndex, "town_core_pad");
    lane = laneSnapshot(game, laneIndex);
    townCore = findPad(lane, "town_core_pad");
  }
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

test("Town Core starts built at House tier", () => {
  const game = createGame();
  const townCore = findPad(laneSnapshot(game, 0), "town_core_pad");

  assert.ok(townCore, "expected the Town Core pad to exist");
  assert.equal(townCore.isBuilt, true);
  assert.equal(townCore.tier, 1);
  assert.equal(townCore.currentTierName, "House");
});

test("invalid startup Town Core state is restored before snapshotting", () => {
  const game = createGame();
  const liveTownCore = game.lanes[0].fortressPads.find((pad) => pad && pad.padId === "town_core_pad");
  assert.ok(liveTownCore, "expected live Town Core state");

  liveTownCore.tier = 0;
  liveTownCore.hp = 0;
  liveTownCore.maxHp = 0;
  liveTownCore.constructionKind = "build";
  liveTownCore.constructionTargetTier = 1;
  liveTownCore.constructionEndTick = 10;
  liveTownCore.constructionTotalTicks = 10;

  const townCore = findPad(laneSnapshot(game, 0), "town_core_pad");
  assert.ok(townCore, "expected the Town Core pad to exist");
  assert.equal(townCore.isBuilt, true);
  assert.equal(townCore.tier, 1);
  assert.equal(townCore.currentTierName, "House");
  assert.equal(townCore.isConstructing, false);
});

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
  assert.equal(padCountsByType.turret, 14);
  assert.equal(padCountsByType.tower_archer || 0, 0);
});

test("live fortress pad actions keep walls shared while turrets unlock from wall archers", () => {
  const game = createGame();

  fail(game, 0, "build_on_pad", { padId: "stable_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "workshop_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "library_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "wall_front_left_01_pad" }, /Town Hall/i);
  fail(game, 0, "build_on_pad", { padId: "turret_front_left_pad" }, /Wall Archers/i);

  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_on_pad", { padId: "wall_front_left_01_pad" });

  let lane = laneSnapshot(game, 0);
  let wallPad = findPad(lane, "wall_front_left_01_pad");
  let gatePad = findPad(lane, "gate_front_pad");
  let turretPad = findPad(lane, "turret_front_left_pad");
  assert.equal(wallPad && wallPad.isConstructing, true);
  assert.equal(gatePad && gatePad.isConstructing, true);
  assert.equal(turretPad && turretPad.isConstructing, false);
  assert.equal(turretPad && turretPad.isBuilt, false);
  assert.match(String(turretPad && turretPad.lockedReason || ""), /Wall Archers/i);

  finishPadConstruction(game, 0, "wall_front_left_01_pad");
  lane = laneSnapshot(game, 0);
  wallPad = findPad(lane, "wall_front_left_01_pad");
  gatePad = findPad(lane, "gate_front_pad");
  turretPad = findPad(lane, "turret_front_left_pad");
  assert.equal(wallPad && wallPad.isBuilt, true);
  assert.equal(gatePad && gatePad.isBuilt, true);
  assert.equal(turretPad && turretPad.isBuilt, false);

  fail(game, 0, "upgrade_building", { padId: "wall_front_left_01_pad" }, /Keep/i);
  fail(game, 0, "build_on_pad", { padId: "turret_front_left_pad" }, /Wall Archers/i);

  act(game, 0, "build_on_pad", { padId: "archery_tower_pad" });
  finishPadConstruction(game, 0, "archery_tower_pad");
  act(game, 0, "purchase_building_upgrade", { padId: "archery_tower_pad", upgradeKey: "wall_archers" });
  lane = laneSnapshot(game, 0);
  turretPad = findPad(lane, "turret_front_left_pad");
  assert.equal(turretPad && turretPad.canBuild, true);
  assert.equal(turretPad && turretPad.buildCost, 500);
  act(game, 0, "build_on_pad", { padId: "turret_front_left_pad" });
  finishPadConstruction(game, 0, "turret_front_left_pad");

  act(game, 0, "build_on_pad", { padId: "stable_pad" });
  act(game, 0, "build_on_pad", { padId: "workshop_pad" });
  act(game, 0, "build_on_pad", { padId: "library_pad" });

  upgradeTownCoreToTier(game, 0, 3);
  act(game, 0, "upgrade_building", { padId: "wall_front_left_01_pad" });
  finishPadConstruction(game, 0, "wall_front_left_01_pad");

  lane = laneSnapshot(game, 0);
  wallPad = findPad(lane, "wall_front_left_01_pad");
  gatePad = findPad(lane, "gate_front_pad");
  turretPad = findPad(lane, "turret_front_left_pad");
  assert.equal(wallPad && wallPad.tier, 2);
  assert.equal(gatePad && gatePad.tier, 2);
  assert.equal(turretPad && turretPad.tier, 1);

  act(game, 0, "upgrade_building", { padId: "turret_front_left_pad" });
  finishPadConstruction(game, 0, "turret_front_left_pad");
  turretPad = findPad(laneSnapshot(game, 0), "turret_front_left_pad");
  assert.equal(turretPad && turretPad.tier, 2);
});

test("fortress pad snapshots expose construction timers and destroyed state", () => {
  const game = createGame();

  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });

  let pad = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.ok(pad, "expected blacksmith snapshot");
  assert.equal(pad.isBuilt, false);
  assert.equal(pad.isConstructing, true);
  assert.equal(pad.buildState, "constructing");
  assert.ok(pad.constructionTimerTicksRemaining > 0);
  assert.ok(pad.constructionTimerTotalTicks >= pad.constructionTimerTicksRemaining);

  finishPadConstruction(game, 0, "blacksmith_pad");

  pad = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(pad.isBuilt, true);
  assert.equal(pad.isConstructing, false);
  assert.equal(pad.buildState, "built");

  const statePad = game.lanes[0].fortressPads.find((entry) => entry && entry.padId === "blacksmith_pad");
  assert.ok(statePad, "expected live blacksmith state");
  statePad.hp = 0;
  pad = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(pad.isDestroyed, true);
  assert.equal(pad.buildState, "destroyed");
});

test("legacy classic actions stay disabled in fortress mode", () => {
  const game = createGame();

  fail(game, 0, "spawn_unit", { unitType: "goblin" }, /Barracks/i);
  fail(game, 0, "place_unit", { gridX: 5, gridY: 10, unitTypeKey: "goblin" }, /disabled in fortress mode/i);
  fail(game, 0, "upgrade_tower", { gridX: 5, gridY: 10 }, /disabled in fortress mode/i);
  fail(game, 0, "bulk_upgrade_towers", { tiles: [{ gridX: 5, gridY: 10 }] }, /disabled in fortress mode/i);
  fail(game, 0, "set_tower_target", { gridX: 5, gridY: 10, targetMode: "first" }, /disabled in fortress mode/i);
  fail(game, 0, "sell_tower", { gridX: 5, gridY: 10 }, /disabled in fortress mode/i);
  fail(game, 0, "set_autosend", { enabled: true }, /Barracks/i);
  fail(game, 0, "upgrade_barracks", {}, /Town Core/i);
});

test("lane build value ignores legacy grid towers and only counts fortress investments", () => {
  const game = createGame();
  const lane = game.lanes[0];

  const baseline = simMl.getLaneBuildValue(lane);
  assert.equal(simMl.createMLSnapshot(game).lanes[0].buildValue, baseline);

  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_on_pad", { padId: "lumber_mill_pad" });
  const investedValue = simMl.getLaneBuildValue(lane);
  assert.ok(investedValue > baseline, "expected fortress pad spend to increase build value");

  const legacyTile = lane.grid[5][10];
  legacyTile.type = "tower";
  legacyTile.towerType = "archery_tower";
  legacyTile.costHistory = [{ cost: 999 }];

  assert.equal(simMl.getLaneBuildValue(lane), investedValue);
  assert.equal(simMl.createMLSnapshot(game).lanes[0].buildValue, investedValue);
});
