"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 140,
    startIncome: 10,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

function makeUnit(key, options = {}) {
  return {
    id: options.id || key,
    key,
    name: options.name || key,
    enabled: true,
    send_cost: options.send_cost ?? 10,
    build_cost: options.build_cost ?? 10,
    income: options.income ?? 1,
    hp: options.hp ?? 40,
    attack_damage: options.attack_damage ?? 6,
    attack_speed: options.attack_speed ?? 20,
    path_speed: options.path_speed ?? 0.35,
    range: options.range ?? 0.08,
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

setUnitTypesForTests([
  makeUnit("tt_peasant", { build_cost: 9 }),
  makeUnit("tt_spearman", { build_cost: 12 }),
  makeUnit("tt_heavy_infantry", { build_cost: 14 }),
  makeUnit("tt_light_infantry", { build_cost: 18 }),
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function createGame(startGold = 500) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold,
    startIncome: 0,
  });
}

function laneSnapshot(game, laneIndex = 0) {
  const snapshot = simMl.createMLSnapshot(game);
  return snapshot.lanes[laneIndex];
}

function findBarracksSite(snapshotLane, barracksId) {
  return snapshotLane.barracksSites.find((site) => site && site.barracksId === barracksId) || null;
}

function findRosterEntry(roster, rosterKey) {
  return (roster || []).find((entry) => entry && entry.rosterKey === rosterKey) || null;
}

function act(game, laneIndex, type, data) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, result.reason || `Expected '${type}' to succeed`);
  return result;
}

test("militia stays unavailable until a barracks is built", () => {
  const game = createGame();
  const lane = laneSnapshot(game, 0);
  const leftBarracks = findBarracksSite(lane, "left");
  const militia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");

  assert.ok(leftBarracks, "expected the left barracks site to exist");
  assert.equal(leftBarracks.isBuilt, false);
  assert.equal(militia && militia.unlocked, false);
  assert.equal(militia && militia.archetypeKey, "infantry_t1");
  assert.equal(militia && militia.presentationKey, "human_default");
  assert.equal(militia && militia.unitTypeKey, "tt_peasant");

  const buyResult = simMl.applyMLAction(game, 0, {
    type: "buy_barracks_unit",
    data: { barracksId: "left", rosterKey: "militia" },
  });

  assert.equal(buyResult.ok, false);
  assert.match(buyResult.reason, /Buy Building first/i);
});

test("building a barracks unlocks militia without requiring a blacksmith", () => {
  const game = createGame();

  act(game, 0, "build_barracks_site", { barracksId: "left" });

  const lane = laneSnapshot(game, 0);
  const leftBarracks = findBarracksSite(lane, "left");
  const militia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  const spearman = findRosterEntry(leftBarracks && leftBarracks.roster, "spearman");
  const aggregatedMilitia = findRosterEntry(lane.barracksRoster, "militia");

  assert.equal(leftBarracks.isBuilt, true);
  assert.equal(militia && militia.unlocked, true);
  assert.equal(militia && militia.unlockBuildingType, "barracks");
  assert.equal(militia && militia.archetypeKey, "infantry_t1");
  assert.equal(militia && militia.skinKey, "tt_peasant");
  assert.equal(aggregatedMilitia && aggregatedMilitia.unlocked, true);
  assert.equal(aggregatedMilitia && aggregatedMilitia.archetypeKey, "infantry_t1");
  assert.equal(spearman && spearman.unlocked, false);
  assert.match(String(spearman && spearman.lockedReason), /Blacksmith/i);

  act(game, 0, "buy_barracks_unit", { barracksId: "left", rosterKey: "militia" });

  const afterBuy = laneSnapshot(game, 0);
  const afterBuyMilitia = findRosterEntry(findBarracksSite(afterBuy, "left").roster, "militia");
  assert.equal(afterBuyMilitia && afterBuyMilitia.ownedCount, 1);

  const spearmanBuy = simMl.applyMLAction(game, 0, {
    type: "buy_barracks_unit",
    data: { barracksId: "left", rosterKey: "spearman" },
  });

  assert.equal(spearmanBuy.ok, false);
  assert.match(spearmanBuy.reason, /Blacksmith/i);
});

test("blacksmith still unlocks the later melee progression after militia", () => {
  const game = createGame(900);

  act(game, 0, "build_barracks_site", { barracksId: "left" });
  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });

  let lane = laneSnapshot(game, 0);
  let leftBarracks = findBarracksSite(lane, "left");
  let spearman = findRosterEntry(leftBarracks && leftBarracks.roster, "spearman");
  let shieldman = findRosterEntry(leftBarracks && leftBarracks.roster, "shieldman");
  let swordsman = findRosterEntry(leftBarracks && leftBarracks.roster, "swordsman");

  assert.equal(spearman && spearman.unlocked, true);
  assert.equal(shieldman && shieldman.unlocked, true);
  assert.equal(swordsman && swordsman.unlocked, false);
  assert.match(String(swordsman && swordsman.lockedReason), /Tier 2/i);

  act(game, 0, "upgrade_building", { padId: "blacksmith_pad" });

  lane = laneSnapshot(game, 0);
  leftBarracks = findBarracksSite(lane, "left");
  swordsman = findRosterEntry(leftBarracks && leftBarracks.roster, "swordsman");

  assert.equal(swordsman && swordsman.unlocked, true);
});
