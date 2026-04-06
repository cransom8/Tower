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
  };
}

setUnitTypesForTests([
  makeUnit("tt_peasant", { build_cost: 9, path_speed: 0.61 }),
  makeUnit("tt_settler", { build_cost: 11, path_speed: 0.42 }),
  makeUnit("tt_spearman", { build_cost: 12, path_speed: 0.34 }),
  makeUnit("tt_heavy_infantry", { build_cost: 14, path_speed: 0.27 }),
  makeUnit("tt_light_infantry", { build_cost: 18, path_speed: 0.49 }),
  makeUnit("tt_archer", { build_cost: 20, path_speed: 0.33, attack_damage: 10, range: 0.25, damage_type: "PIERCE" }),
  makeUnit("tt_crossbowman", { build_cost: 28, path_speed: 0.29, attack_damage: 16, range: 0.24, damage_type: "PIERCE" }),
  makeUnit("raider", { build_cost: 10, hp: 50, attack_damage: 8, path_speed: 0.38, bounty: 10 }),
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function createGame(startGold = 4000) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold,
    startIncome: 0,
  });
}

function assertAlmostEqual(actual, expected, message) {
  assert.ok(Math.abs(Number(actual) - Number(expected)) <= 0.0001, message || `Expected ${actual} to be close to ${expected}`);
}

function laneSnapshot(game, laneIndex = 0) {
  return simMl.createMLSnapshot(game).lanes[laneIndex];
}

function findPad(snapshotLane, padId) {
  return (snapshotLane?.fortressPads || []).find((pad) => pad && pad.padId === padId) || null;
}

function advanceUntil(game, predicate, maxTicks = 4000) {
  for (let tick = 0; tick < maxTicks; tick += 1) {
    if (predicate())
      return;
    simMl.mlTick(game);
  }

  assert.fail("Timed out waiting for test state to advance");
}

function finishPadConstruction(game, laneIndex, padId) {
  advanceUntil(game, () => {
    const pad = findPad(laneSnapshot(game, laneIndex), padId);
    return !!(pad && !pad.isConstructing);
  });
}

function findBarracksSite(snapshotLane, barracksId) {
  return (snapshotLane?.barracksSites || []).find((site) => site && site.barracksId === barracksId) || null;
}

function finishBarracksConstruction(game, laneIndex, barracksId) {
  advanceUntil(game, () => {
    const site = findBarracksSite(laneSnapshot(game, laneIndex), barracksId);
    return !!(site && !site.isConstructing);
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
  if (pattern)
    assert.match(String(result.reason || ""), pattern);
  return result;
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

function unlockMarket(game, laneIndex) {
  upgradeTownCoreToTier(game, laneIndex, 2);
  act(game, laneIndex, "build_on_pad", { padId: "market_pad" });
  finishPadConstruction(game, laneIndex, "market_pad");
}

function clearWaveUnits(game) {
  for (const lane of game.lanes || []) {
    if (!lane)
      continue;
    lane.units = [];
    lane.spawnQueue = [];
  }
}

function collectLaneUnits(game, laneIndex, predicate) {
  const lane = game && Array.isArray(game.lanes) ? game.lanes[laneIndex] : null;
  if (!lane)
    return [];

  const units = [];
  for (const collection of [lane.units || [], lane.spawnQueue || []]) {
    for (const unit of collection) {
      if (!unit || (predicate && !predicate(unit)))
        continue;
      units.push(unit);
    }
  }

  return units;
}

function collectGameUnits(game, predicate) {
  const units = [];
  const laneCount = game && Array.isArray(game.lanes) ? game.lanes.length : 0;
  for (let laneIndex = 0; laneIndex < laneCount; laneIndex += 1)
    units.push(...collectLaneUnits(game, laneIndex, predicate));
  return units;
}

function prepareWaveStart(game, nextWaveNumber) {
  clearWaveUnits(game);
  game.activeWaveSession = null;
  game.waveConfig = [
    { wave_number: nextWaveNumber, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
    { wave_number: nextWaveNumber + 1, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
  ];
  game.roundNumber = Math.max(1, nextWaveNumber - 1);
  game.hasSpawnedWave = nextWaveNumber > 1;
}

function startWave(game, waveNumber) {
  prepareWaveStart(game, waveNumber);
  assert.equal(simMl.startNextWaveNow(game), true, `expected wave ${waveNumber} to start`);
}

test("feed_dungeon stays locked through wave 5 and exposes feed state once wave 6 starts", () => {
  const game = createGame();
  unlockMarket(game, 0);

  game.roundNumber = 5;
  game.hasSpawnedWave = true;
  fail(game, 0, "feed_dungeon", null, /wave 6/i);

  startWave(game, 6);
  const purchase = act(game, 0, "feed_dungeon");
  assert.equal(purchase.cost, 500);

  const snapshot = simMl.createMLSnapshot(game);
  const lane = snapshot.lanes[0];
  assert.equal(snapshot.roundNumber, 6);
  assertAlmostEqual(snapshot.dungeonHpMult, 1.02);
  assertAlmostEqual(snapshot.dungeonDmgMult, 1.01);
  assert.equal(snapshot.totalDungeonScalingApplied, 1);
  assert.equal(lane.feedDungeonCount, 1);
  assert.equal(lane.feedDungeonPurchasedThisWave, true);
  assert.equal(lane.feedDungeonCost, 600);
  assert.equal(lane.totalGoldSpentOnFeedDungeon, 500);
  assertAlmostEqual(lane.goldPerKillMult, 1.03);
});

test("feed_dungeon is limited to one purchase per wave and cost scales per player", () => {
  const game = createGame();
  unlockMarket(game, 0);
  unlockMarket(game, 1);

  startWave(game, 6);

  act(game, 0, "feed_dungeon");
  fail(game, 0, "feed_dungeon", null, /already purchased this wave/i);

  const laneZeroAfterFirst = laneSnapshot(game, 0);
  const laneOneAfterZeroBuys = laneSnapshot(game, 1);
  assert.equal(laneZeroAfterFirst.feedDungeonCount, 1);
  assert.equal(laneZeroAfterFirst.feedDungeonCost, 600);
  assert.equal(laneOneAfterZeroBuys.feedDungeonCount, 0);
  assert.equal(laneOneAfterZeroBuys.feedDungeonCost, 500);

  const laneOneGoldBefore = game.lanes[1].gold;
  act(game, 1, "feed_dungeon");
  assert.equal(game.lanes[1].gold, laneOneGoldBefore - 500);
  assertAlmostEqual(game.dungeonHpMult, 1.04);
  assertAlmostEqual(game.dungeonDmgMult, 1.02);
  assert.equal(game.totalDungeonScalingApplied, 2);

  startWave(game, 7);
  const secondPurchase = act(game, 0, "feed_dungeon");
  assert.equal(secondPurchase.cost, 600);
  assert.equal(game.lanes[0].feedDungeonPurchasedThisWave, true);
  assert.equal(game.lanes[0].feedDungeonCount, 2);
  assertAlmostEqual(game.lanes[0].goldPerKillMult, 1.06);
  assertAlmostEqual(game.dungeonHpMult, 1.06);
  assertAlmostEqual(game.dungeonDmgMult, 1.03);
});

test("feed_dungeon only buffs dungeon wave spawns and leaves player units unchanged", () => {
  const game = createGame();
  unlockMarket(game, 0);
  startWave(game, 6);
  act(game, 0, "feed_dungeon");

  clearWaveUnits(game);

  const lane = game.lanes[0];
  game.waveConfig = [
    { wave_number: 6, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
  ];
  game.roundNumber = 5;
  game.hasSpawnedWave = true;
  game.activeWaveSession = null;
  assert.equal(simMl.startNextWaveNow(game), true);
  const dungeonMob = lane.spawnQueue.pop();
  const raiderDef = simMl.resolveUnitDef("raider");
  assert.equal(dungeonMob.spawnSourceType, "dungeon_wave");
  assert.equal(dungeonMob.hp, Math.ceil(raiderDef.hp * 1.02));
  assert.ok(Math.abs(Number(dungeonMob.baseDmg) - (raiderDef.dmg * 1.01)) <= 0.0001);

  game.activeWaveSession = null;
  clearWaveUnits(game);
  simMl.spawnWaveUnit(game, lane, {
    unit_type: "tt_peasant",
    sourceLaneIndex: 0,
    sourceBarracksId: "center",
    allowUnbuiltBarracks: true,
    hp_mult: 1,
    dmg_mult: 1,
    speed_mult: 1,
  });
  const playerUnit = lane.spawnQueue.pop();
  const peasantDef = simMl.resolveUnitDef("tt_peasant");
  assert.equal(playerUnit.spawnSourceType, "barracks_roster");
  assert.equal(playerUnit.hp, Math.ceil(peasantDef.hp));
  assert.ok(Math.abs(Number(playerUnit.baseDmg) - peasantDef.dmg) <= 0.0001);
});

test("feed_dungeon only boosts kill gold for the buyer and only on dungeon mob deaths", () => {
  const game = createGame();
  unlockMarket(game, 0);
  startWave(game, 6);
  act(game, 0, "feed_dungeon");

  const laneZeroGoldBefore = game.lanes[0].gold;
  const laneOneGoldBefore = game.lanes[1].gold;

  game.lanes[0].units.push({
    id: "dead_dungeon_rewarded",
    unitId: "dead_dungeon_rewarded",
    type: "raider",
    unitTypeKey: "raider",
    spawnSourceType: "dungeon_wave",
    isWaveUnit: true,
    hp: 0,
    maxHp: 50,
    bounty: 10,
    lastHostileKillCredit: {
      rewardLaneIndex: 0,
      sourceKind: "unit",
      sourceId: "lane0_attacker",
    },
    abilities: [],
  });

  game.lanes[1].units.push({
    id: "dead_dungeon_unbuffed",
    unitId: "dead_dungeon_unbuffed",
    type: "raider",
    unitTypeKey: "raider",
    spawnSourceType: "dungeon_wave",
    isWaveUnit: true,
    hp: 0,
    maxHp: 50,
    bounty: 10,
    lastHostileKillCredit: {
      rewardLaneIndex: 1,
      sourceKind: "unit",
      sourceId: "lane1_attacker",
    },
    abilities: [],
  });

  game.lanes[1].units.push({
    id: "dead_player_unit",
    unitId: "dead_player_unit",
    type: "tt_peasant",
    unitTypeKey: "tt_peasant",
    spawnSourceType: "barracks_roster",
    isWaveUnit: false,
    hp: 0,
    maxHp: 40,
    bounty: 10,
    lastHostileKillCredit: {
      rewardLaneIndex: 0,
      sourceKind: "unit",
      sourceId: "lane0_attacker_2",
    },
    abilities: [],
  });

  simMl.mlTick(game);

  assert.ok(Math.abs((game.lanes[0].gold - laneZeroGoldBefore) - 20.3) <= 0.0001);
  assert.ok(Math.abs((game.lanes[1].gold - laneOneGoldBefore) - 10) <= 0.0001);
});
