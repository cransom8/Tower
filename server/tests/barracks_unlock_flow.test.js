"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const barracksSystem = require("../game/multilane/barracksSystem");
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
  makeUnit("tt_settler", { build_cost: 11 }),
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

function createGameWithStartingMilitia(startGold = 500, startingCombatMilitiaCount = 5) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold,
    startIncome: 0,
    startingCombatMilitiaCount,
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

function findMarketEntry(snapshotLane, unitKey) {
  return (snapshotLane?.marketRoster || []).find((entry) => entry && entry.unitKey === unitKey) || null;
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

  assert.fail("Timed out waiting for multilane construction to finish");
}

function finishBarracksConstruction(game, laneIndex, barracksId) {
  advanceUntil(game, () => {
    const site = findBarracksSite(laneSnapshot(game, laneIndex), barracksId);
    return !!(site && !site.isConstructing);
  });
}

function finishPadConstruction(game, laneIndex, padId) {
  advanceUntil(game, () => {
    const pad = findPad(laneSnapshot(game, laneIndex), padId);
    return !!(pad && !pad.isConstructing);
  });
}

function collectLaneUnits(game, laneIndex, predicate) {
  const lane = game.lanes[laneIndex];
  if (!lane)
    return [];

  const matches = [];
  for (const collection of [lane.units || [], lane.spawnQueue || []]) {
    for (const unit of collection) {
      if (!unit || (predicate && !predicate(unit)))
        continue;
      matches.push(unit);
    }
  }

  return matches;
}

function act(game, laneIndex, type, data) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, result.reason || `Expected '${type}' to succeed`);
  return result;
}

test("default games do not seed combat-test militia into live spawn queues", () => {
  const game = createGame();

  assert.equal(game.lanes[0].spawnQueue.length, 0);
  assert.equal(game.lanes[1].spawnQueue.length, 0);
});

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

test("combat test militia can be seeded before the center barracks is built", () => {
  const game = createGameWithStartingMilitia();
  const lane = laneSnapshot(game, 0);
  const centerBarracks = findBarracksSite(lane, "center");
  const centerMilitia = findRosterEntry(centerBarracks && centerBarracks.roster, "militia");

  assert.ok(centerBarracks, "expected the center barracks site to exist");
  assert.equal(centerBarracks.isBuilt, false);
  assert.equal(centerMilitia && centerMilitia.ownedCount, 0);
  assert.equal(game.lanes[0].spawnQueue.length, 5);
  assert.equal(game.lanes[1].spawnQueue.length, 5);
  assert.ok(
    game.lanes[0].spawnQueue.every((unit) =>
      unit
      && unit.spawnSourceType === "barracks_roster"
      && unit.sourceBarracksId === "center"
      && unit.rosterKey === "militia"
    ),
    "expected seeded starting militia to use center-barracks identity"
  );

  simMl.mlTick(game);

  const activeStartingMilitia = game.lanes[0].units.filter((unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceBarracksId === "center"
    && unit.rosterKey === "militia"
  );
  const laneAfterTick = laneSnapshot(game, 0);
  const centerBarracksAfterTick = findBarracksSite(laneAfterTick, "center");

  assert.equal(activeStartingMilitia.length, 5);
  assert.equal(centerBarracksAfterTick && centerBarracksAfterTick.isBuilt, false);
});

test("building a barracks unlocks militia without requiring a blacksmith", () => {
  const game = createGame();

  act(game, 0, "build_barracks_site", { barracksId: "left" });

  let lane = laneSnapshot(game, 0);
  let leftBarracks = findBarracksSite(lane, "left");
  assert.ok(leftBarracks, "expected the left barracks site to exist");
  assert.equal(leftBarracks.isBuilt, false);
  assert.equal(leftBarracks.isConstructing, true);
  assert.equal(leftBarracks.buildState, "constructing");
  assert.ok(leftBarracks.constructionTimerTicksRemaining > 0);

  finishBarracksConstruction(game, 0, "left");

  lane = laneSnapshot(game, 0);
  leftBarracks = findBarracksSite(lane, "left");
  const militia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  const spearman = findRosterEntry(leftBarracks && leftBarracks.roster, "spearman");
  const aggregatedMilitia = findRosterEntry(lane.barracksRoster, "militia");

  assert.equal(leftBarracks.isBuilt, true);
  assert.equal(militia && militia.unlocked, true);
  assert.equal(militia && militia.unlockBuildingType, "barracks");
  assert.equal(militia && militia.archetypeKey, "infantry_t1");
  assert.equal(militia && militia.skinKey, "tt_peasant");
  assert.equal(militia && militia.availableForPurchase, true);
  assert.equal(militia && militia.currentTier, true);
  assert.equal(aggregatedMilitia && aggregatedMilitia.unlocked, true);
  assert.equal(aggregatedMilitia && aggregatedMilitia.archetypeKey, "infantry_t1");
  assert.equal(aggregatedMilitia && aggregatedMilitia.availableForPurchase, true);
  assert.equal(spearman && spearman.unlocked, false);
  assert.equal(spearman && spearman.availableForPurchase, false);
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
  finishBarracksConstruction(game, 0, "left");
  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });
  finishPadConstruction(game, 0, "blacksmith_pad");

  let lane = laneSnapshot(game, 0);
  let leftBarracks = findBarracksSite(lane, "left");
  let spearman = findRosterEntry(leftBarracks && leftBarracks.roster, "spearman");
  let shieldman = findRosterEntry(leftBarracks && leftBarracks.roster, "shieldman");
  let militia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  let swordsman = findRosterEntry(leftBarracks && leftBarracks.roster, "swordsman");

  assert.equal(spearman && spearman.unlocked, true);
  assert.equal(spearman && spearman.availableForPurchase, true);
  assert.equal(shieldman && shieldman.unlocked, true);
  assert.equal(shieldman && shieldman.availableForPurchase, true);
  assert.equal(militia && militia.availableForPurchase, true);
  assert.equal(swordsman && swordsman.unlocked, false);
  assert.equal(swordsman && swordsman.availableForPurchase, false);
  assert.match(String(swordsman && swordsman.lockedReason), /Tier 2/i);

  act(game, 0, "upgrade_building", { padId: "blacksmith_pad" });
  finishPadConstruction(game, 0, "blacksmith_pad");

  lane = laneSnapshot(game, 0);
  leftBarracks = findBarracksSite(lane, "left");
  militia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  swordsman = findRosterEntry(leftBarracks && leftBarracks.roster, "swordsman");

  assert.equal(swordsman && swordsman.unlocked, true);
  assert.equal(swordsman && swordsman.availableForPurchase, true);
  assert.equal(militia && militia.availableForPurchase, false);
});

test("blacksmith tier upgrades convert owned and live infantry units into the current branch tier", () => {
  const game = createGameWithStartingMilitia(900, 2);

  act(game, 0, "build_barracks_site", { barracksId: "left" });
  finishBarracksConstruction(game, 0, "left");
  act(game, 0, "buy_barracks_unit", { barracksId: "left", rosterKey: "militia", count: 2 });
  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });
  finishPadConstruction(game, 0, "blacksmith_pad");

  const preUpgradeMilitia = collectLaneUnits(game, 0, (unit) => unit && unit.rosterKey === "militia");
  assert.equal(preUpgradeMilitia.length, 2);

  act(game, 0, "upgrade_building", { padId: "blacksmith_pad" });
  finishPadConstruction(game, 0, "blacksmith_pad");

  const lane = laneSnapshot(game, 0);
  const leftBarracks = findBarracksSite(lane, "left");
  const militia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  const swordsman = findRosterEntry(leftBarracks && leftBarracks.roster, "swordsman");
  const aggregatedMilitia = findRosterEntry(lane.barracksRoster, "militia");
  const aggregatedSwordsman = findRosterEntry(lane.barracksRoster, "swordsman");
  const liveSwordsmen = collectLaneUnits(game, 0, (unit) => unit && unit.rosterKey === "swordsman");

  assert.equal(militia && militia.ownedCount, 0);
  assert.equal(militia && militia.availableForPurchase, false);
  assert.equal(swordsman && swordsman.ownedCount, 2);
  assert.equal(swordsman && swordsman.availableForPurchase, true);
  assert.equal(aggregatedMilitia && aggregatedMilitia.ownedCount, 0);
  assert.equal(aggregatedSwordsman && aggregatedSwordsman.ownedCount, 2);
  assert.equal(liveSwordsmen.length, 2);
  assert.ok(liveSwordsmen.every((unit) => unit.archetypeKey === "infantry_t2" && unit.isMarketWorker !== true));
});

test("market upgrades convert existing workers and future purchases to the current trade tier", () => {
  const game = createGame(900);

  act(game, 0, "build_on_pad", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");

  let lane = laneSnapshot(game, 0);
  let peasant = findMarketEntry(lane, "peasant");
  let settler = findMarketEntry(lane, "settler");
  assert.equal(peasant && peasant.availableForPurchase, true);
  assert.equal(settler && settler.availableForPurchase, false);

  act(game, 0, "buy_market_unit", { unitKey: "peasant", count: 2 });

  let livePeasants = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "peasant");
  assert.equal(livePeasants.length, 2);
  assert.ok(livePeasants.every((unit) => unit.isMarketWorker === true && unit.spawnSourceType === "market_roster"));

  act(game, 0, "upgrade_building", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");

  lane = laneSnapshot(game, 0);
  peasant = findMarketEntry(lane, "peasant");
  settler = findMarketEntry(lane, "settler");
  let trader = findMarketEntry(lane, "trader");
  assert.equal(peasant && peasant.ownedCount, 0);
  assert.equal(peasant && peasant.availableForPurchase, false);
  assert.equal(settler && settler.ownedCount, 2);
  assert.equal(settler && settler.availableForPurchase, true);
  assert.equal(trader && trader.availableForPurchase, false);

  let liveSettlers = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "settler");
  assert.equal(liveSettlers.length, 2);
  assert.ok(liveSettlers.every((unit) => unit.archetypeKey === "economy_t2" && unit.isMarketWorker === true));

  act(game, 0, "buy_market_unit", { unitKey: "settler" });
  liveSettlers = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "settler");
  assert.equal(liveSettlers.length, 3);

  const oldPeasantBuy = simMl.applyMLAction(game, 0, {
    type: "buy_market_unit",
    data: { unitKey: "peasant" },
  });
  assert.equal(oldPeasantBuy.ok, false);
  assert.match(oldPeasantBuy.reason, /higher-tier market worker/i);
});

test("completed market laps award the configured gold for the worker tier", () => {
  const game = createGame(900);

  act(game, 0, "build_on_pad", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");
  act(game, 0, "buy_market_unit", { unitKey: "peasant" });

  const peasantWorker = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "peasant")[0];
  assert.ok(peasantWorker, "expected a live peasant worker to exist");

  const goldBeforePeasantLap = game.lanes[0].gold;
  const peasantLapGold = barracksSystem.completeMarketWorkerLap(game, game.lanes[0], peasantWorker);
  assert.equal(peasantLapGold, 4);
  assert.equal(game.lanes[0].gold, goldBeforePeasantLap + 4);

  act(game, 0, "upgrade_building", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");
  const settlerWorker = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "settler")[0];
  assert.ok(settlerWorker, "expected the existing worker to convert into a settler");

  const goldBeforeSettlerLap = game.lanes[0].gold;
  const settlerLapGold = barracksSystem.completeMarketWorkerLap(game, game.lanes[0], settlerWorker);
  assert.equal(settlerLapGold, 7);
  assert.equal(game.lanes[0].gold, goldBeforeSettlerLap + 7);
});
