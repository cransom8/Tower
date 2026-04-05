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
  makeUnit("tt_peasant", { build_cost: 9, path_speed: 0.61 }),
  makeUnit("tt_settler", { build_cost: 11, path_speed: 0.42 }),
  makeUnit("tt_spearman", { build_cost: 12, path_speed: 0.34 }),
  makeUnit("tt_heavy_infantry", { build_cost: 14, path_speed: 0.27 }),
  makeUnit("tt_light_infantry", { build_cost: 18, path_speed: 0.49 }),
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

function upgradeBarracksSiteToLevel(game, laneIndex, barracksId, targetLevel) {
  let site = findBarracksSite(laneSnapshot(game, laneIndex), barracksId);
  assert.ok(site, `expected barracks '${barracksId}' to exist`);

  if (!(site && site.isBuilt) && targetLevel > 0) {
    act(game, laneIndex, "build_barracks_site", { barracksId });
    finishBarracksConstruction(game, laneIndex, barracksId);
    site = findBarracksSite(laneSnapshot(game, laneIndex), barracksId);
  }

  while ((site && site.level) < targetLevel) {
    act(game, laneIndex, "upgrade_barracks_site", { barracksId });
    finishBarracksConstruction(game, laneIndex, barracksId);
    site = findBarracksSite(laneSnapshot(game, laneIndex), barracksId);
  }
}

function upgradePadToTier(game, laneIndex, padId, targetTier) {
  let pad = findPad(laneSnapshot(game, laneIndex), padId);
  assert.ok(pad, `expected pad '${padId}' to exist`);

  if ((pad && pad.tier) <= 0 && targetTier > 0) {
    act(game, laneIndex, "build_on_pad", { padId });
    finishPadConstruction(game, laneIndex, padId);
    pad = findPad(laneSnapshot(game, laneIndex), padId);
  }

  while ((pad && pad.tier) < targetTier) {
    act(game, laneIndex, "upgrade_building", { padId });
    finishPadConstruction(game, laneIndex, padId);
    pad = findPad(laneSnapshot(game, laneIndex), padId);
  }
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

function collectGameUnits(game, predicate) {
  const matches = [];
  for (let laneIndex = 0; laneIndex < (game && Array.isArray(game.lanes) ? game.lanes.length : 0); laneIndex += 1) {
    matches.push(...collectLaneUnits(game, laneIndex, predicate));
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

test("House starts with Town Core only, and Center Barracks must be purchased through Town Core", () => {
  const game = createGame();
  let lane = laneSnapshot(game, 0);
  let centerBarracks = findBarracksSite(lane, "center");
  const leftBarracks = findBarracksSite(lane, "left");
  let militia = findRosterEntry(centerBarracks && centerBarracks.roster, "militia");

  assert.ok(centerBarracks, "expected the center barracks site to exist");
  assert.equal(centerBarracks.isBuilt, false);
  assert.equal(centerBarracks.canBuild, true);
  assert.equal(centerBarracks.buildCost, 100);
  assert.ok(leftBarracks, "expected the left barracks site to exist");
  assert.equal(leftBarracks.isBuilt, false);
  assert.equal(leftBarracks.canBuild, false);
  assert.equal(militia && militia.unlocked, false);
  assert.equal(militia && militia.archetypeKey, "infantry_t1");
  assert.equal(militia && militia.presentationKey, "human_default");
  assert.equal(militia && militia.unitTypeKey, "tt_peasant");

  const buyResult = simMl.applyMLAction(game, 0, {
    type: "buy_barracks_unit",
    data: { barracksId: "center", rosterKey: "militia" },
  });

  assert.equal(buyResult.ok, false);
  assert.match(buyResult.reason, /Town Core/i);

  act(game, 0, "build_barracks_site", { barracksId: "center" });
  lane = laneSnapshot(game, 0);
  centerBarracks = findBarracksSite(lane, "center");
  assert.equal(centerBarracks && centerBarracks.isConstructing, true);

  finishBarracksConstruction(game, 0, "center");

  lane = laneSnapshot(game, 0);
  centerBarracks = findBarracksSite(lane, "center");
  militia = findRosterEntry(centerBarracks && centerBarracks.roster, "militia");
  assert.equal(centerBarracks && centerBarracks.isBuilt, true);
  assert.equal(militia && militia.unlocked, true);
  assert.equal(militia && militia.availableForPurchase, true);
});

test("center barracks purchase requires 100 gold and deducts it exactly once", () => {
  const poorGame = createGame(99);
  const poorLaneGold = poorGame.lanes[0].gold;
  const poorResult = simMl.applyMLAction(poorGame, 0, {
    type: "build_barracks_site",
    data: { barracksId: "center" },
  });

  assert.equal(poorResult.ok, false);
  assert.match(String(poorResult.reason || ""), /Not enough gold/i);
  assert.equal(poorGame.lanes[0].gold, poorLaneGold);
  assert.equal(findBarracksSite(laneSnapshot(poorGame, 0), "center")?.isBuilt, false);
  assert.equal(findBarracksSite(laneSnapshot(poorGame, 0), "center")?.isConstructing, false);

  const game = createGame(140);
  const goldBefore = game.lanes[0].gold;
  act(game, 0, "build_barracks_site", { barracksId: "center" });
  assert.equal(game.lanes[0].gold, goldBefore - 100);

  const siteDuringConstruction = findBarracksSite(laneSnapshot(game, 0), "center");
  assert.equal(siteDuringConstruction && siteDuringConstruction.isBuilt, false);
  assert.equal(siteDuringConstruction && siteDuringConstruction.isConstructing, true);

  finishBarracksConstruction(game, 0, "center");
  assert.equal(game.lanes[0].gold, goldBefore - 100);
});

test("empty built barracks stay built and scheduled cycles no-op when no roster units are owned", () => {
  const game = createGame();
  game.lanes[0].barracksSiteStates.center = {
    barracksId: "center",
    isBuilt: true,
    level: 1,
    hp: 260,
    maxHp: 260,
    nextSendTick: 120,
    costHistory: [],
    constructionKind: null,
    constructionTargetLevel: 0,
    constructionEndTick: 0,
    constructionTotalTicks: 0,
  };

  let centerBarracks = findBarracksSite(laneSnapshot(game, 0), "center");
  assert.ok(centerBarracks, "expected the center barracks site to exist");
  assert.equal(centerBarracks.isBuilt, true);
  assert.equal(centerBarracks.isConstructing, false);
  assert.equal(centerBarracks.canBuild, false);
  assert.equal(game.lanes[0].barracksSiteStates.center.isBuilt, true);

  simMl.mlTick(game);

  centerBarracks = findBarracksSite(laneSnapshot(game, 0), "center");
  assert.ok(centerBarracks, "expected the center barracks site to still exist after the send cycle");
  assert.equal(centerBarracks.isBuilt, true);
  assert.equal(game.lanes[0].spawnQueue.length, 0);
  assert.equal(game.lanes[0].units.length, 0);
  assert.equal(game.lanes[0].totalSendCount, 0);
});

test("barracks site snapshot HP updates when the site takes structure damage", () => {
  const game = createGame();
  act(game, 0, "build_barracks_site", { barracksId: "center" });
  finishBarracksConstruction(game, 0, "center");

  const lane = game.lanes[0];
  const target = simMl.getBarracksSiteCombatTarget(lane, "center");
  assert.ok(target, "expected the built center barracks to expose a structure combat target");

  const attacker = {
    id: "barracks_damage_test",
    type: "tt_spearman",
    baseDmg: 12,
    atkCd: 0,
    atkCdTicks: 20,
    attackPulse: 0,
  };
  const result = simMl.attackFortressPad(game, lane, attacker, target);
  assert.equal(result.damageApplied, 12, "expected the barracks site to take structure damage");

  const laneAfterDamage = laneSnapshot(game, 0);
  const centerBarracks = findBarracksSite(laneAfterDamage, "center");
  assert.ok(centerBarracks, "expected the center barracks snapshot to still exist after taking damage");
  assert.equal(centerBarracks.hp, 248, "expected the barracks snapshot HP to match the damaged live site");
  assert.equal(centerBarracks.maxHp, 260, "expected the barracks snapshot max HP to stay intact");
});

test("destroyed barracks stay destroyed and do not resume spawning owned roster units", () => {
  const game = createGame();
  act(game, 0, "build_barracks_site", { barracksId: "center" });
  finishBarracksConstruction(game, 0, "center");
  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia" });

  const lane = game.lanes[0];
  lane.barracksSiteStates.center.nextSendTick = game.tick + 1;

  const target = simMl.getBarracksSiteCombatTarget(lane, "center");
  assert.ok(target, "expected the built center barracks to expose a combat target");

  const attacker = {
    id: "barracks_destroy_test",
    type: "tt_spearman",
    baseDmg: 1000,
    atkCd: 0,
    atkCdTicks: 20,
    attackPulse: 0,
  };
  const result = simMl.attackFortressPad(game, lane, attacker, target);
  assert.equal(result.destroyed, true, "expected the center barracks to be destroyed by lethal structure damage");

  let centerBarracks = findBarracksSite(laneSnapshot(game, 0), "center");
  assert.ok(centerBarracks, "expected the center barracks snapshot to remain visible after destruction");
  assert.equal(centerBarracks.isBuilt, true, "expected the destroyed barracks to remain a built structure site");
  assert.equal(centerBarracks.isDestroyed, true, "expected the barracks to enter the destroyed state");
  assert.equal(centerBarracks.buildState, "destroyed", "expected the barracks build state to report destroyed");
  assert.equal(centerBarracks.hp, 0, "expected the destroyed barracks HP to stay at zero");

  simMl.mlTick(game);

  centerBarracks = findBarracksSite(laneSnapshot(game, 0), "center");
  assert.ok(centerBarracks, "expected the center barracks snapshot to remain available after an additional tick");
  assert.equal(centerBarracks.isDestroyed, true, "expected the barracks to remain destroyed on later snapshots");
  assert.equal(centerBarracks.hp, 0, "expected the destroyed barracks HP to remain zero on later snapshots");
  assert.equal(game.lanes[0].spawnQueue.length, 0, "expected destroyed barracks to pause scheduled roster sends");
});

test("combat test militia keeps using the built center barracks identity", () => {
  const game = createGameWithStartingMilitia();
  const lane = laneSnapshot(game, 0);
  const centerBarracks = findBarracksSite(lane, "center");
  const centerMilitia = findRosterEntry(centerBarracks && centerBarracks.roster, "militia");

  assert.ok(centerBarracks, "expected the center barracks site to exist");
  assert.equal(centerBarracks.isBuilt, true);
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
  assert.equal(centerBarracksAfterTick && centerBarracksAfterTick.isBuilt, true);
});

test("barracks roster units inherit their unit-specific catalog path speed", () => {
  const game = createGameWithStartingMilitia();
  const expectedBaseSpeed = 0.61 * barracksSystem.getBarracksSpeedMultForLevel(1);

  assert.equal(game.lanes[0].spawnQueue.length, 5);
  assert.ok(
    game.lanes[0].spawnQueue.every((unit) =>
      unit
      && unit.rosterKey === "militia"
      && Math.abs(Number(unit.baseSpeed) - expectedBaseSpeed) <= 0.0001
    ),
    "expected center-barracks militia to keep the tt_peasant path speed instead of inheriting a shared combat-speed baseline"
  );
});

test("owned barracks roster counts alone do not auto-build an unpurchased site", () => {
  const game = createGame();
  game.lanes[0].barracksSiteRosterCounts.center.militia = 1;

  const centerBarracks = findBarracksSite(laneSnapshot(game, 0), "center");
  assert.ok(centerBarracks, "expected the center barracks site to exist in the snapshot");
  assert.equal(centerBarracks.isBuilt, false, "expected roster ownership alone to not construct the center barracks");
  assert.equal(centerBarracks.isDestroyed, false, "expected the unbuilt site to remain simply unavailable, not destroyed");
  assert.equal(centerBarracks.hp, 0, "expected the unbuilt site to keep zero HP");
  assert.equal(centerBarracks.canBuild, true, "expected the player to still need to purchase the center barracks normally");
});

test("building a barracks unlocks militia without requiring a blacksmith", () => {
  const game = createGame();

  upgradeTownCoreToTier(game, 0, 2);
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

  upgradeTownCoreToTier(game, 0, 2);
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

  upgradeTownCoreToTier(game, 0, 3);
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

  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_barracks_site", { barracksId: "left" });
  finishBarracksConstruction(game, 0, "left");
  act(game, 0, "buy_barracks_unit", { barracksId: "left", rosterKey: "militia", count: 2 });
  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });
  finishPadConstruction(game, 0, "blacksmith_pad");

  const preUpgradeMilitia = collectLaneUnits(game, 0, (unit) => unit && unit.rosterKey === "militia");
  assert.equal(preUpgradeMilitia.length, 2);

  upgradeTownCoreToTier(game, 0, 3);
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
  assert.equal(liveSwordsmen.length, 4);
  assert.ok(liveSwordsmen.every((unit) => unit.archetypeKey === "infantry_t2" && unit.isMarketWorker !== true));
});

test("market upgrades convert existing workers and future purchases to the current trade tier", () => {
  const game = createGame(900);

  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_on_pad", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");

  let lane = laneSnapshot(game, 0);
  let peasant = findMarketEntry(lane, "peasant");
  let settler = findMarketEntry(lane, "settler");
  assert.equal(peasant && peasant.availableForPurchase, true);
  assert.equal(settler && settler.availableForPurchase, false);

  act(game, 0, "buy_market_unit", { unitKey: "peasant", count: 2 });

  let livePeasants = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "peasant");
  assert.equal(livePeasants.length, 0);
  assert.equal(laneSnapshot(game, 0).income, 8);

  upgradeTownCoreToTier(game, 0, 3);
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
  assert.equal(liveSettlers.length, 0);
  assert.equal(lane.income, 14);

  act(game, 0, "buy_market_unit", { unitKey: "settler" });
  liveSettlers = collectLaneUnits(game, 0, (unit) => unit && unit.marketUnitKey === "settler");
  assert.equal(liveSettlers.length, 0);
  assert.equal(laneSnapshot(game, 0).income, 21);

  const oldPeasantBuy = simMl.applyMLAction(game, 0, {
    type: "buy_market_unit",
    data: { unitKey: "peasant" },
  });
  assert.equal(oldPeasantBuy.ok, false);
  assert.match(oldPeasantBuy.reason, /higher-tier market worker/i);
});

test("market income is awarded on the shared income timer based on owned count and tier value", () => {
  const game = createGame(900);

  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_on_pad", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");
  act(game, 0, "buy_market_unit", { unitKey: "peasant", count: 2 });

  let lane = laneSnapshot(game, 0);
  assert.equal(lane.income, 8);

  const goldBeforePeasantTick = game.lanes[0].gold;
  advanceUntil(game, () => game.lanes[0].gold > goldBeforePeasantTick, simMl.INCOME_INTERVAL_TICKS + 5);
  assert.equal(game.lanes[0].gold, goldBeforePeasantTick + 8);

  upgradeTownCoreToTier(game, 0, 3);
  act(game, 0, "upgrade_building", { padId: "market_pad" });
  finishPadConstruction(game, 0, "market_pad");

  lane = laneSnapshot(game, 0);
  assert.equal(lane.income, 14);

  const goldBeforeSettlerTick = game.lanes[0].gold;
  advanceUntil(game, () => game.lanes[0].gold > goldBeforeSettlerTick, simMl.INCOME_INTERVAL_TICKS + 5);
  assert.equal(game.lanes[0].gold, goldBeforeSettlerTick + 14);
});

test("barracks food limits follow barracks tier capacity and troop tier food costs", () => {
  const cases = [
    {
      barracksLevel: 1,
      townCoreTier: 1,
      unlockPadId: null,
      unlockTier: 0,
      rosterKey: "militia",
      expectedFoodCost: 1,
      expectedFoodLimit: 20,
      purchaseCount: 20,
    },
    {
      barracksLevel: 2,
      townCoreTier: 3,
      unlockPadId: "blacksmith_pad",
      unlockTier: 2,
      rosterKey: "swordsman",
      expectedFoodCost: 2,
      expectedFoodLimit: 40,
      purchaseCount: 20,
    },
    {
      barracksLevel: 3,
      townCoreTier: 4,
      unlockPadId: "blacksmith_pad",
      unlockTier: 3,
      rosterKey: "knight",
      expectedFoodCost: 3,
      expectedFoodLimit: 60,
      purchaseCount: 20,
    },
  ];

  for (const testCase of cases) {
    const game = createGame(5000);
    upgradeTownCoreToTier(game, 0, testCase.townCoreTier);
    upgradeBarracksSiteToLevel(game, 0, "center", testCase.barracksLevel);
    if (testCase.unlockPadId && testCase.unlockTier > 0)
      upgradePadToTier(game, 0, testCase.unlockPadId, testCase.unlockTier);

    let lane = laneSnapshot(game, 0);
    let site = findBarracksSite(lane, "center");
    let entry = findRosterEntry(site && site.roster, testCase.rosterKey);

    assert.ok(entry, `expected barracks roster entry '${testCase.rosterKey}' to exist`);
    assert.equal(site && site.foodLimit, testCase.expectedFoodLimit);
    assert.equal(entry && entry.foodCost, testCase.expectedFoodCost);

    act(game, 0, "buy_barracks_unit", {
      barracksId: "center",
      rosterKey: testCase.rosterKey,
      count: testCase.purchaseCount,
    });

    lane = laneSnapshot(game, 0);
    site = findBarracksSite(lane, "center");
    entry = findRosterEntry(site && site.roster, testCase.rosterKey);

    assert.equal(site && site.foodUsed, testCase.expectedFoodLimit);
    assert.equal(site && site.foodRemaining, 0);
    assert.equal(site && site.isAtFoodLimit, true);
    assert.equal(entry && entry.ownedCount, testCase.purchaseCount);

    const overflow = simMl.applyMLAction(game, 0, {
      type: "buy_barracks_unit",
      data: { barracksId: "center", rosterKey: testCase.rosterKey },
    });

    assert.equal(overflow.ok, false);
    assert.match(String(overflow.reason || ""), /food/i);
  }
});

test("barracks snapshots report owned roster food separately from active field food", () => {
  const game = createGame(5000);
  upgradeBarracksSiteToLevel(game, 0, "center", 1);
  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia", count: 5 });

  function makeActiveMilitia(id) {
    return {
      id: `militia_${id}`,
      unitId: `militia_${id}`,
      type: "tt_peasant",
      unitTypeKey: "tt_peasant",
      hp: 40,
      spawnSourceType: "barracks_roster",
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      barracksId: "center",
      sourceLaneIndex: 0,
      ownerLaneIndex: 0,
      ownerLane: 0,
      rosterKey: "militia",
    };
  }

  for (let i = 0; i < 10; i += 1)
    game.lanes[0].units.push(makeActiveMilitia(`lane0_${i}`));
  for (let i = 0; i < 5; i += 1)
    game.lanes[1].units.push(makeActiveMilitia(`lane1_${i}`));

  let lane = laneSnapshot(game, 0);
  let site = findBarracksSite(lane, "center");
  assert.equal(site && site.foodUsed, 5, "owned roster food should stay tied to purchased units");
  assert.equal(site && site.foodRemaining, 15, "owned roster food should still leave room for more purchases");
  assert.equal(site && site.hasActiveFoodState, true, "snapshot should expose active field food state");
  assert.equal(site && site.activeFoodUsed, 15, "active field food should count live units from this barracks across lanes");
  assert.equal(site && site.activeFoodRemaining, 5);
  assert.equal(site && site.isAtActiveFoodLimit, false);

  for (let i = 0; i < 5; i += 1)
    game.lanes[1].spawnQueue.push(makeActiveMilitia(`queue_${i}`));

  lane = laneSnapshot(game, 0);
  site = findBarracksSite(lane, "center");
  assert.equal(site && site.activeFoodUsed, 20, "active field food should clamp at the barracks cap");
  assert.equal(site && site.activeFoodRemaining, 0);
  assert.equal(site && site.isAtActiveFoodLimit, true);
});

test("selling a barracks unit only decrements the selected barracks roster once", () => {
  const game = createGame(2000);
  upgradeTownCoreToTier(game, 0, 2);
  upgradeBarracksSiteToLevel(game, 0, "left", 1);
  upgradeBarracksSiteToLevel(game, 0, "center", 1);

  act(game, 0, "buy_barracks_unit", { barracksId: "left", rosterKey: "militia", count: 2 });
  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia", count: 3 });

  let lane = laneSnapshot(game, 0);
  let leftBarracks = findBarracksSite(lane, "left");
  let centerBarracks = findBarracksSite(lane, "center");
  let leftMilitia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  let centerMilitia = findRosterEntry(centerBarracks && centerBarracks.roster, "militia");
  const aggregatedMilitiaBefore = findRosterEntry(lane.barracksRoster, "militia");
  const goldBeforeSell = Math.floor(Number(lane.gold) || 0);

  assert.equal(leftMilitia && leftMilitia.ownedCount, 2);
  assert.equal(centerMilitia && centerMilitia.ownedCount, 3);
  assert.equal(aggregatedMilitiaBefore && aggregatedMilitiaBefore.ownedCount, 5);

  act(game, 0, "sell_barracks_unit", { barracksId: "left", rosterKey: "militia" });

  lane = laneSnapshot(game, 0);
  leftBarracks = findBarracksSite(lane, "left");
  centerBarracks = findBarracksSite(lane, "center");
  leftMilitia = findRosterEntry(leftBarracks && leftBarracks.roster, "militia");
  centerMilitia = findRosterEntry(centerBarracks && centerBarracks.roster, "militia");
  const aggregatedMilitiaAfter = findRosterEntry(lane.barracksRoster, "militia");

  assert.equal(leftMilitia && leftMilitia.ownedCount, 1, "selling should only remove one unit from the targeted barracks");
  assert.equal(centerMilitia && centerMilitia.ownedCount, 3, "other barracks should keep their owned units");
  assert.equal(aggregatedMilitiaAfter && aggregatedMilitiaAfter.ownedCount, 4, "lane-wide totals should only fall by one");
  assert.equal(
    Math.floor(Number(lane.gold) || 0),
    goldBeforeSell + Math.max(0, Math.floor(Number(leftMilitia && leftMilitia.sellRefund) || 0)),
    "sell should refund the selected unit exactly once"
  );
});

test("market caps owned traders at 10 regardless of tier and reports timed income correctly", () => {
  const cases = [
    {
      marketTier: 1,
      townCoreTier: 2,
      unitKey: "peasant",
      expectedFoodCost: 1,
      expectedFoodLimit: 10,
      purchaseCount: 10,
      expectedIncome: 40,
    },
    {
      marketTier: 2,
      townCoreTier: 3,
      unitKey: "settler",
      expectedFoodCost: 1,
      expectedFoodLimit: 10,
      purchaseCount: 10,
      expectedIncome: 70,
    },
    {
      marketTier: 3,
      townCoreTier: 4,
      unitKey: "trader",
      expectedFoodCost: 1,
      expectedFoodLimit: 10,
      purchaseCount: 10,
      expectedIncome: 100,
    },
  ];

  for (const testCase of cases) {
    const game = createGame(5000);
    upgradeTownCoreToTier(game, 0, testCase.townCoreTier);
    upgradePadToTier(game, 0, "market_pad", testCase.marketTier);

    let lane = laneSnapshot(game, 0);
    let marketPad = findPad(lane, "market_pad");
    let entry = findMarketEntry(lane, testCase.unitKey);

    assert.ok(entry, `expected market roster entry '${testCase.unitKey}' to exist`);
    assert.equal(marketPad && marketPad.foodLimit, testCase.expectedFoodLimit);
    assert.equal(entry && entry.foodCost, testCase.expectedFoodCost);

    act(game, 0, "buy_market_unit", {
      unitKey: testCase.unitKey,
      count: testCase.purchaseCount,
    });

    lane = laneSnapshot(game, 0);
    marketPad = findPad(lane, "market_pad");
    entry = findMarketEntry(lane, testCase.unitKey);

    assert.equal(marketPad && marketPad.foodUsed, testCase.expectedFoodLimit);
    assert.equal(marketPad && marketPad.foodRemaining, 0);
    assert.equal(marketPad && marketPad.isAtFoodLimit, true);
    assert.equal(entry && entry.ownedCount, testCase.purchaseCount);
    assert.equal(lane.income, testCase.expectedIncome);

    const overflow = simMl.applyMLAction(game, 0, {
      type: "buy_market_unit",
      data: { unitKey: testCase.unitKey },
    });

    assert.equal(overflow.ok, false);
    assert.match(String(overflow.reason || ""), /(slot|cap)/i);
  }
});

test("barracks food limit also caps how much of one barracks can stay active on its target lane", () => {
  const game = createGame(5000);
  upgradeBarracksSiteToLevel(game, 0, "center", 1);
  act(game, 0, "buy_barracks_unit", {
    barracksId: "center",
    rosterKey: "militia",
    count: 20,
  });

  const sourceLane = game.lanes[0];
  const targetLaneIndex = simMl.resolveTargetLaneForBarracksSend(game, 0, "center");
  assert.ok(Number.isInteger(targetLaneIndex) && targetLaneIndex >= 0, "expected a valid barracks target lane");
  const siteState = sourceLane.barracksSiteStates.center;
  assert.ok(siteState, "expected live center barracks site state");

  siteState.nextSendTick = game.tick;
  simMl.mlTick(game);

  let activeUnits = collectLaneUnits(game, targetLaneIndex, (unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceBarracksId === "center"
    && unit.sourceLaneIndex === 0
  );
  assert.equal(activeUnits.length, 20);

  siteState.nextSendTick = game.tick;
  simMl.mlTick(game);

  activeUnits = collectLaneUnits(game, targetLaneIndex, (unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceBarracksId === "center"
    && unit.sourceLaneIndex === 0
  );
  assert.equal(activeUnits.length, 20);
});

test("scheduled barracks sends refill the missing roster type instead of repeating the first sorted unit", () => {
  const game = createGame(5000);
  upgradeBarracksSiteToLevel(game, 0, "center", 1);
  upgradeTownCoreToTier(game, 0, 2);
  act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });
  finishPadConstruction(game, 0, "blacksmith_pad");
  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia", count: 5 });
  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "shieldman", count: 5 });

  const targetLaneIndex = simMl.resolveTargetLaneForBarracksSend(game, 0, "center");
  assert.ok(Number.isInteger(targetLaneIndex) && targetLaneIndex >= 0, "expected a valid barracks target lane");

  game.lanes[0].barracksSiteStates.center.nextSendTick = game.tick;
  simMl.mlTick(game);

  let activeUnits = collectGameUnits(game, (unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceLaneIndex === 0
    && unit.sourceBarracksId === "center"
  );
  assert.equal(activeUnits.filter((unit) => unit.rosterKey === "militia").length, 5);
  assert.equal(activeUnits.filter((unit) => unit.rosterKey === "shieldman").length, 5);

  const targetLane = game.lanes[targetLaneIndex];
  let removed = false;
  for (const collectionKey of ["units", "spawnQueue"]) {
    const collection = Array.isArray(targetLane && targetLane[collectionKey]) ? targetLane[collectionKey] : [];
    const removeIndex = collection.findIndex((unit) =>
      unit
      && unit.spawnSourceType === "barracks_roster"
      && unit.sourceLaneIndex === 0
      && unit.sourceBarracksId === "center"
      && unit.rosterKey === "shieldman"
    );
    if (removeIndex >= 0) {
      collection.splice(removeIndex, 1);
      removed = true;
      break;
    }
  }
  assert.equal(removed, true, "expected to remove one active shieldman from the barracks roster");

  game.lanes[0].barracksSiteStates.center.nextSendTick = game.tick;
  simMl.mlTick(game);

  activeUnits = collectGameUnits(game, (unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceLaneIndex === 0
    && unit.sourceBarracksId === "center"
  );
  assert.equal(activeUnits.filter((unit) => unit.rosterKey === "militia").length, 5);
  assert.equal(activeUnits.filter((unit) => unit.rosterKey === "shieldman").length, 5);
});

test("barracks snapshots keep independent command states per site", () => {
  const game = createGame(5000);
  upgradeBarracksSiteToLevel(game, 0, "center", 1);
  upgradeTownCoreToTier(game, 0, 2);
  upgradeBarracksSiteToLevel(game, 0, "left", 1);

  act(game, 0, "set_barracks_attack", { barracksId: "center" });
  act(game, 0, "set_barracks_retreat", { barracksId: "left" });

  const lane = laneSnapshot(game, 0);
  const centerBarracks = findBarracksSite(lane, "center");
  const leftBarracks = findBarracksSite(lane, "left");
  const rightBarracks = findBarracksSite(lane, "right");

  assert.equal(centerBarracks && centerBarracks.commandState, "ATTACK");
  assert.equal(leftBarracks && leftBarracks.commandState, "RETREAT");
  assert.equal(rightBarracks && rightBarracks.commandState, "DEFEND");
});

test("barracks command actions only retask units from the targeted barracks", () => {
  const game = createGame(5000);
  upgradeBarracksSiteToLevel(game, 0, "center", 1);
  upgradeTownCoreToTier(game, 0, 2);
  upgradeBarracksSiteToLevel(game, 0, "left", 1);

  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia", count: 1 });
  act(game, 0, "buy_barracks_unit", { barracksId: "left", rosterKey: "militia", count: 1 });

  game.lanes[0].barracksSiteStates.center.nextSendTick = game.tick;
  game.lanes[0].barracksSiteStates.left.nextSendTick = game.tick;
  simMl.mlTick(game);

  let centerUnit = collectGameUnits(game, (unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceLaneIndex === 0
    && unit.sourceBarracksId === "center"
  )[0];
  let leftUnit = collectGameUnits(game, (unit) =>
    unit
    && unit.spawnSourceType === "barracks_roster"
    && unit.sourceLaneIndex === 0
    && unit.sourceBarracksId === "left"
  )[0];

  assert.ok(centerUnit, "expected the center barracks to field a unit");
  assert.ok(leftUnit, "expected the left barracks to field a unit");
  assert.equal(centerUnit.commandState, "DEFEND");
  assert.equal(leftUnit.commandState, "DEFEND");
  assert.equal(centerUnit.targetLaneIndex, 0);
  assert.equal(leftUnit.targetLaneIndex, 0);

  act(game, 0, "set_barracks_attack", { barracksId: "center" });

  centerUnit = collectGameUnits(game, (unit) => unit && unit.id === centerUnit.id)[0];
  leftUnit = collectGameUnits(game, (unit) => unit && unit.id === leftUnit.id)[0];
  assert.equal(centerUnit && centerUnit.commandState, "ATTACK");
  assert.equal(centerUnit && centerUnit.targetLaneIndex, 1);
  assert.equal(leftUnit && leftUnit.commandState, "DEFEND");
  assert.equal(leftUnit && leftUnit.targetLaneIndex, 0);

  act(game, 0, "set_barracks_retreat", { barracksId: "left" });

  centerUnit = collectGameUnits(game, (unit) => unit && unit.id === centerUnit.id)[0];
  leftUnit = collectGameUnits(game, (unit) => unit && unit.id === leftUnit.id)[0];
  assert.equal(centerUnit && centerUnit.commandState, "ATTACK");
  assert.equal(centerUnit && centerUnit.targetLaneIndex, 1);
  assert.equal(leftUnit && leftUnit.commandState, "RETREAT");
  assert.equal(leftUnit && leftUnit.targetLaneIndex, 0);
});
