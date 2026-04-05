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
  makeUnit("stalker", {
    hp: 44,
    attack_damage: 8,
    attack_speed: 18,
    path_speed: 0.61,
    range: 0.12,
    armor_type: "MEDIUM",
  }),
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function createGame() {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    teamHpStart: 20,
    startGold: 0,
    startIncome: 0,
  });
}

test("validateSpawnDefinition rejects barracks spawns with an invalid source barracks id", () => {
  const game = createGame();
  const targetLane = game.lanes[1];

  const result = simMl.validateSpawnDefinition(game, targetLane, {
    unit_type: "goblin",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "ghost",
    spawnIndex: 0,
  });

  assert.equal(result.ok, false);
  assert.equal(result.spawnType, "barracks_roster");
  assert.match(result.reason, /source barracks/i);
});

test("validateSpawnDefinition resolves dungeon wave spawns to deterministic queue coordinates", () => {
  const game = createGame();
  const targetLane = game.lanes[0];

  const result = simMl.validateSpawnDefinition(game, targetLane, {
    unit_type: "giant_rat",
    spawn_qty: 1,
    spawnIndex: 14,
  });

  assert.equal(result.ok, true);
  assert.equal(result.spawnType, "dungeon_wave");
  assert.deepEqual(result.logicalPos, { x: 3, y: 1 });
  assert.equal(result.resolvedSpawnIndex, 14);
});

test("validateSpawnDefinition centers the first dungeon wave spawn on the mine", () => {
  const game = createGame();
  const targetLane = game.lanes[0];

  const result = simMl.validateSpawnDefinition(game, targetLane, {
    unit_type: "giant_rat",
    spawn_qty: 1,
    spawnIndex: 0,
  });

  assert.equal(result.ok, true);
  assert.equal(result.spawnType, "dungeon_wave");
  assert.deepEqual(result.logicalPos, { x: 5, y: 0 });
  assert.equal(result.resolvedSpawnIndex, 0);
});

test("starting a wave preserves the centered logical spawn position on queued units", () => {
  const game = createGame();
  game.waveConfig = [
    { wave_number: 1, unit_type: "raider", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
  ];

  assert.equal(simMl.startNextWaveNow(game), true);
  assert.equal(game.lanes[0].spawnQueue.length, 1);
  assert.deepEqual(game.lanes[0].spawnQueue[0].spawnLogicalPos, { x: 5, y: 0 });
});

test("starting a wave uses the unit's catalog path speed instead of a shared combat-speed blanket", () => {
  const game = createGame();
  game.waveConfig = [
    { wave_number: 1, unit_type: "stalker", spawn_qty: 1, hp_mult: 1, dmg_mult: 1, speed_mult: 1.5 },
  ];

  assert.equal(simMl.startNextWaveNow(game), true);
  assert.equal(game.lanes[0].spawnQueue.length, 1);
  assert.ok(
    Math.abs(Number(game.lanes[0].spawnQueue[0].baseSpeed) - (0.61 * 1.5)) <= 0.0001,
    `expected queued wave baseSpeed to inherit the unit path speed, got ${Number(game.lanes[0].spawnQueue[0].baseSpeed)}`
  );
});
