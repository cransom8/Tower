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

function createGame() {
  const game = simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold: 0,
    startIncome: 0,
  });

  game.waveConfig = [
    { wave_number: 1, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
    { wave_number: 2, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
  ];

  return game;
}

test("mlTick advances to the next timed wave in active FFA survival once the round timer expires", () => {
  const game = createGame();
  game.waveIntervalTicks = 1;
  game.waveGroupIntervalTicks = 1;

  assert.equal(simMl.startNextWaveNow(game), true, "wave 1 should start");

  game.lanes[0].units = [];
  game.lanes[0].spawnQueue = [];
  game.lanes[1].units = [];
  game.lanes[1].spawnQueue = [];
  game.matchState = "active_survival";

  assert.equal(simMl.countRemainingWaveMobs(game), 0, "the wave should be clear before the survival tick");

  simMl.mlTick(game);

  assert.equal(game.roundNumber, 2, "the next survival wave should start automatically");
  assert.ok(simMl.countRemainingWaveMobs(game) > 0, "wave 2 should spawn enemies for the surviving lane");
});

test("mlTick still advances to the next timed wave after another lane has already been eliminated", () => {
  const game = createGame();
  game.waveIntervalTicks = 1;
  game.waveGroupIntervalTicks = 1;

  assert.equal(simMl.startNextWaveNow(game), true, "wave 1 should start");

  game.lanes[0].units = [];
  game.lanes[0].spawnQueue = [];
  game.lanes[1].units = [];
  game.lanes[1].spawnQueue = [];
  game.lanes[1].eliminated = true;
  game.matchState = "active_survival";

  assert.equal(simMl.countRemainingWaveMobs(game), 0, "the wave should be clear before the control tick");

  simMl.mlTick(game);

  assert.equal(game.roundNumber, 2, "survival should continue into the next wave with the remaining lane");
  assert.ok(simMl.countRemainingWaveMobs(game) > 0, "future waves should still spawn after another lane is dead");
});
