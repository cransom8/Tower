"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 500,
    startIncome: 10,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

function makeUnit(key, options = {}) {
  return {
    id: key,
    key,
    name: options.name || key,
    enabled: true,
    send_cost: options.send_cost ?? 10,
    build_cost: options.build_cost ?? 10,
    income: options.income ?? 1,
    hp: options.hp ?? 100,
    attack_damage: options.attack_damage ?? 10,
    attack_speed: options.attack_speed ?? 1,
    path_speed: options.path_speed ?? 0.2,
    range: options.range ?? 0.2,
    projectile_travel_ticks: options.projectile_travel_ticks ?? 6,
    damage_type: options.damage_type || "NORMAL",
    armor_type: options.armor_type || "MEDIUM",
    damage_reduction_pct: options.damage_reduction_pct ?? 0,
    bounty: options.bounty ?? 1,
    special_props: options.special_props || {},
    abilities: options.abilities || [],
    barracks_scales_hp: options.barracks_scales_hp ?? false,
    barracks_scales_dmg: options.barracks_scales_dmg ?? false,
  };
}

function seedFortressRoster() {
  setUnitTypesForTests([
    makeUnit("tt_peasant"),
    makeUnit("tt_heavy_infantry"),
    makeUnit("tt_mounted_priest"),
    makeUnit("tt_mage"),
    makeUnit("tt_archer"),
    makeUnit("wave_raider"),
  ]);
}

function createGame(startGold = 2500) {
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

function findBarracks(snapshotLane, barracksId) {
  return (snapshotLane && snapshotLane.barracksSites || []).find((site) => site && site.barracksId === barracksId) || null;
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

function finishBarracksConstruction(game, laneIndex, barracksId) {
  advanceUntil(game, () => {
    const site = findBarracks(laneSnapshot(game, laneIndex), barracksId);
    return !!(site && !site.isConstructing && site.isBuilt);
  });
}

function act(game, laneIndex, type, data) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, result.reason || `Expected '${type}' to succeed`);
  return result;
}

function upgradeTownCoreToTier(game, laneIndex, targetTier) {
  let townCore = findPad(laneSnapshot(game, laneIndex), "town_core_pad");
  while ((townCore && townCore.tier) < targetTier) {
    act(game, laneIndex, "upgrade_building", { padId: "town_core_pad" });
    finishPadConstruction(game, laneIndex, "town_core_pad");
    townCore = findPad(laneSnapshot(game, laneIndex), "town_core_pad");
  }
}

function buildPad(game, laneIndex, padId) {
  act(game, laneIndex, "build_on_pad", { padId });
  finishPadConstruction(game, laneIndex, padId);
}

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
  seedFortressRoster();
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
  setUnitTypesForTests([]);
});

test("fortress pads move through being_built, active, destroyed, under_repair, and back to active", () => {
  const game = createGame();
  const lane = game.lanes[0];

  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "lumber_mill_pad");

  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });
  let blacksmith = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(blacksmith.lifecycleState, "being_built");
  assert.equal(blacksmith.buildState, "constructing");

  finishPadConstruction(game, 0, "blacksmith_pad");
  blacksmith = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(blacksmith.lifecycleState, "active");
  assert.equal(blacksmith.buildState, "built");

  const liveBlacksmith = lane.fortressPads.find((pad) => pad && pad.padId === "blacksmith_pad");
  assert.ok(liveBlacksmith, "expected live blacksmith state");
  liveBlacksmith.hp = 0;

  blacksmith = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(blacksmith.lifecycleState, "destroyed");
  assert.equal(blacksmith.buildState, "destroyed");
  assert.equal(blacksmith.isDestroyed, true);

  lane.gold = 40;
  const partialRepair = act(game, 0, "repair_all_buildings", { padId: "lumber_mill_pad" });
  assert.equal(partialRepair.goldSpent, 40);
  assert.equal(partialRepair.hpRestored, 40);

  blacksmith = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(blacksmith.hp, 40);
  assert.equal(blacksmith.lifecycleState, "under_repair");
  assert.equal(blacksmith.buildState, "under_repair");
  assert.equal(blacksmith.isUnderRepair, true);

  lane.gold = 1000;
  act(game, 0, "repair_all_buildings", { padId: "lumber_mill_pad" });
  blacksmith = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  assert.equal(blacksmith.hp, blacksmith.maxHp);
  assert.equal(blacksmith.lifecycleState, "active");
  assert.equal(blacksmith.isUnderRepair, false);
});

test("lumber mill repair-all repairs barracks sites, spends available gold in bulk, and ignores the Town Core", () => {
  const game = createGame();
  const lane = game.lanes[0];

  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "lumber_mill_pad");
  act(game, 0, "build_barracks_site", { barracksId: "center" });
  finishBarracksConstruction(game, 0, "center");

  const liveTownCore = lane.fortressPads.find((pad) => pad && pad.padId === "town_core_pad");
  const liveBarracks = lane.barracksSiteStates && lane.barracksSiteStates.center;
  assert.ok(liveTownCore, "expected Town Core state");
  assert.ok(liveBarracks, "expected Center Barracks state");

  liveTownCore.hp = Math.max(0, liveTownCore.maxHp - 5);
  liveBarracks.hp = Math.max(0, liveBarracks.maxHp - 20);
  lane.gold = 15;

  const firstRepair = act(game, 0, "repair_all_buildings", { padId: "lumber_mill_pad" });
  assert.equal(firstRepair.goldSpent, 15);
  assert.equal(firstRepair.hpRestored, 15);

  let townCore = findPad(laneSnapshot(game, 0), "town_core_pad");
  let centerBarracks = findBarracks(laneSnapshot(game, 0), "center");
  assert.equal(townCore.hp, liveTownCore.maxHp - 5, "Town Core should stay excluded from Lumber Mill repair");
  assert.equal(centerBarracks.hp, liveBarracks.maxHp - 5, "Center Barracks should consume the available gold in one bulk repair");
  assert.equal(centerBarracks.lifecycleState, "under_repair");
  assert.equal(centerBarracks.buildState, "under_repair");

  lane.gold = 10;
  act(game, 0, "repair_all_buildings", { padId: "lumber_mill_pad" });
  townCore = findPad(laneSnapshot(game, 0), "town_core_pad");
  centerBarracks = findBarracks(laneSnapshot(game, 0), "center");
  assert.equal(townCore.hp, liveTownCore.maxHp - 5, "Town Core should remain untouched after subsequent repairs");
  assert.equal(centerBarracks.hp, centerBarracks.maxHp);
  assert.equal(centerBarracks.lifecycleState, "active");
  assert.equal(centerBarracks.isUnderRepair, false);
});
