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
  makeUnit("raider", {
    hp: 36,
    attack_damage: 6,
    attack_speed: 20,
    path_speed: 0.4,
    range: 0.08,
    armor_type: "LIGHT",
  }),
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

let nextUnitId = 1;

function createGame(teamHpStart = 12) {
  const game = simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    teamHpStart,
    startGold: 0,
    startIncome: 0,
  });

  game.waveConfig = [
    { wave_number: 1, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
    { wave_number: 2, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
  ];

  return game;
}

function getTownCorePad(lane) {
  return lane.fortressPads.find((pad) => pad && pad.buildingType === "town_core");
}

function getCoreApproachPosition(lane, stepsBack = 0) {
  const fullPath = lane.fullPath || lane.path;
  const endPoint = fullPath[fullPath.length - 1];
  const y = endPoint.y - stepsBack;
  return {
    posX: endPoint.x,
    posY: y,
    pathIdx: y,
  };
}

function createWaveUnit(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  return {
    id: overrides.id || `wu_test_${nextUnitId++}`,
    ownerLane: -1,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    type: typeKey,
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 27,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? (overrides.hp ?? def.hp),
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 1,
    isWaveUnit: true,
    isDefender: false,
    combatTarget: null,
    hasBreachedTownCore: false,
    spawnIndex: overrides.spawnIndex ?? 0,
    posX: overrides.posX ?? 5,
    posY: overrides.posY ?? 27,
  };
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

test("free-for-all survival keeps advancing instead of resolving as soon as the first wave starts", () => {
  const game = createGame();
  game.waveIntervalTicks = 1;
  game.nextWaveTick = 1;

  tick(game, 1);

  assert.equal(game.phase, "playing", "FFA survival should stay live after wave 1 starts");
  assert.equal(game.matchState, "active_survival", "wave 1 should not enter a resolved PvP state");
  assert.equal(game.awaitingPostWinDecision, false, "FFA survival should not pause for a winner decision");
  assert.equal(game.roundNumber, 1, "wave 1 should still be the active round after the first spawn tick");

  for (const lane of game.lanes) {
    lane.units = [];
    lane.spawnQueue = [];
  }

  tick(game, 1);

  assert.equal(game.phase, "playing", "the match should still be active when wave 2 begins");
  assert.equal(game.matchState, "active_survival", "later waves should stay in survival");
  assert.equal(game.roundNumber, 2, "the survival run should advance into wave 2 after wave 1 clears");
});

test("another fortress can fall in FFA survival without ending the match", () => {
  const game = createGame(12);
  const firstLane = game.lanes[0];
  const secondLane = game.lanes[1];
  const firstApproach = getCoreApproachPosition(firstLane, 0);
  const secondApproach = getCoreApproachPosition(secondLane, 0);

  firstLane.units.push(createWaveUnit("raider", {
    baseDmg: 12,
    posX: firstApproach.posX,
    posY: firstApproach.posY,
    pathIdx: firstApproach.pathIdx,
  }));

  let ticksSpent = 0;
  while (!firstLane.eliminated && ticksSpent < 10) {
    tick(game, 1);
    ticksSpent += 1;
  }

  assert.equal(getTownCorePad(firstLane).hp, 0, "the first Town Core should be fully destroyed by the fatal hit");
  assert.equal(firstLane.eliminated, true, "the first lane should be eliminated once its core dies");
  assert.equal(secondLane.eliminated, false, "the match should stay alive while another Town Core remains");
  assert.equal(game.phase, "playing", "the game should continue after another FFA fortress falls");
  assert.equal(game.matchState, "active_survival", "another fortress dying should not resolve the FFA run");
  assert.equal(game.awaitingPostWinDecision, false, "FFA survival should not wait on a continue prompt");
  assert.equal(game.officialWinnerLane, null, "another side dying should not declare a winner");

  secondLane.units.push(createWaveUnit("raider", {
    baseDmg: 12,
    posX: secondApproach.posX,
    posY: secondApproach.posY,
    pathIdx: secondApproach.pathIdx,
  }));

  ticksSpent = 0;
  while (game.phase !== "ended" && ticksSpent < 10) {
    tick(game, 1);
    ticksSpent += 1;
  }

  assert.equal(getTownCorePad(secondLane).hp, 0, "the last Town Core should reach zero before the match ends");
  assert.equal(secondLane.eliminated, true, "the final lane should be eliminated when its core is destroyed");
  assert.equal(game.phase, "ended", "the match should end once every Town Core has fallen");
  assert.equal(game.matchState, "final_game_over", "all Town Cores down should become a final game over");
  assert.equal(game.officialWinnerLane, null, "survival wipeouts should not invent a surviving winner lane");
});
