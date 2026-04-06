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
  makeUnit("infantry_t1", {
    hp: 42,
    attack_damage: 7,
    attack_speed: 22,
    path_speed: 0.34,
    range: 0.08,
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

function countSpawnedWaveUnits(game) {
  return (game.lanes || []).reduce((total, lane) => {
    if (!lane)
      return total;
    return total + (lane.spawnQueue?.length || 0) + (lane.units?.length || 0);
  }, 0);
}

function countUnitsMatching(game, predicate) {
  return (game.lanes || []).reduce((total, lane) => {
    if (!lane)
      return total;
    const laneUnits = [...(lane.spawnQueue || []), ...(lane.units || [])];
    return total + laneUnits.filter((unit) => unit && predicate(unit)).length;
  }, 0);
}

function getBarracksSendTimerTicks(game, lane, barracksId) {
  const snapshot = simMl.createBarracksSiteSnapshot(game, lane, barracksId);
  return snapshot ? snapshot.sendTimerTicksRemaining : -1;
}

test("startNextWaveNow refuses to spawn the next wave while the current wave is still active", () => {
  const game = createGame();

  assert.equal(simMl.startNextWaveNow(game), true, "the first wave should still be startable");
  assert.ok(simMl.countRemainingWaveMobs(game) > 0, "starting wave 1 should produce remaining mobs");
  assert.equal(simMl.startNextWaveNow(game), false, "the next wave should be blocked while wave units are still active");
  assert.equal(game.roundNumber, 1, "blocking the start should leave the active wave unchanged");
});

test("startNextWaveNow stays blocked while the current timed wave still has future scheduled groups", () => {
  const game = createGame();

  assert.equal(simMl.startNextWaveNow(game), true, "the first wave should still be startable");

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    lane.spawnQueue = [];
    lane.units = [];
  }

  assert.ok(simMl.countRemainingWaveMobs(game) > 0, "future scheduled groups should still count as remaining wave mobs");
  assert.equal(simMl.isCurrentWaveComplete(game), false, "the timed wave should stay active until its session expires");
  assert.equal(simMl.startNextWaveNow(game), false, "the next wave should stay blocked until the timed session finishes");
  assert.equal(game.roundNumber, 1, "blocking the start should leave the active wave unchanged");
});

test("timed waves respawn groups during the round and roll to the next wave on timer even if old mobs remain", () => {
  const game = createGame();
  game.waveIntervalTicks = 4;
  game.waveGroupIntervalTicks = 2;
  game.initialWaveDelayTicks = 1;
  game.nextWaveTick = 1;

  simMl.mlTick(game);
  assert.equal(game.roundNumber, 1, "the first timed wave should start on the configured opening delay");
  assert.equal(countSpawnedWaveUnits(game), 2, "wave 1 should queue one group per surviving lane on start");

  for (const lane of game.lanes) {
    lane.spawnQueue = [];
    lane.units = [];
  }

  simMl.mlTick(game);
  assert.equal(countSpawnedWaveUnits(game), 0, "no new timed group should appear before the next group interval");

  simMl.mlTick(game);
  assert.equal(countSpawnedWaveUnits(game), 2, "the same authored group should respawn on the next timed interval");

  simMl.mlTick(game);
  assert.equal(game.roundNumber, 1, "the round should stay active until the timed session expires");

  simMl.mlTick(game);
  assert.equal(game.roundNumber, 2, "the timer should advance into the next wave even while the prior group is still alive");
  assert.equal(countSpawnedWaveUnits(game), 4, "wave 2 should stack onto the remaining wave 1 mobs instead of waiting for a clear");
});

test("barracks sends stay synchronized with dungeon wave pulses and with each other", () => {
  const game = createGame();
  game.waveIntervalTicks = 6;
  game.waveGroupIntervalTicks = 2;
  game.initialWaveDelayTicks = 2;
  game.barracksSendIntervalTicks = 2;
  game.nextWaveTick = 2;
  game.nextBarracksSendTick = 2;

  const lane = game.lanes[0];
  lane.barracksSiteStates.center = {
    barracksId: "center",
    isBuilt: true,
    level: 1,
    hp: 260,
    maxHp: 260,
    nextSendTick: 99,
    commandState: "DEFEND",
    costHistory: [],
    constructionKind: null,
    constructionTargetLevel: 0,
    constructionEndTick: 0,
    constructionTotalTicks: 0,
  };
  lane.barracksSiteStates.left = {
    barracksId: "left",
    isBuilt: true,
    level: 1,
    hp: 260,
    maxHp: 260,
    nextSendTick: 1,
    commandState: "DEFEND",
    costHistory: [],
    constructionKind: null,
    constructionTargetLevel: 0,
    constructionEndTick: 0,
    constructionTotalTicks: 0,
  };
  lane.barracksSiteRosterCounts.center.militia = 1;
  lane.barracksSiteRosterCounts.left.militia = 1;

  simMl.mlTick(game);
  assert.equal(countUnitsMatching(game, (unit) => unit.isWaveUnit), 0, "no wave mobs should arrive before the shared opening pulse");
  assert.equal(lane.totalSendCount, 0, "no barracks units should drift ahead of the shared opening pulse");
  assert.equal(
    getBarracksSendTimerTicks(game, lane, "center"),
    getBarracksSendTimerTicks(game, lane, "left"),
    "all barracks should show the same countdown before the shared pulse"
  );

  simMl.mlTick(game);
  assert.equal(countUnitsMatching(game, (unit) => unit.isWaveUnit), 2, "wave mobs should arrive on the shared opening pulse");
  assert.equal(lane.totalSendCount, 2, "both barracks should fire on the shared pulse");
  assert.equal(
    getBarracksSendTimerTicks(game, lane, "center"),
    getBarracksSendTimerTicks(game, lane, "left"),
    "all barracks should stay aligned after firing"
  );

  for (const liveLane of game.lanes) {
    liveLane.spawnQueue = [];
    liveLane.units = [];
  }

  simMl.mlTick(game);
  assert.equal(countSpawnedWaveUnits(game), 0, "nothing new should spawn between shared pulses");
  assert.equal(lane.totalSendCount, 2, "barracks should wait between shared pulses");

  simMl.mlTick(game);
  assert.equal(countUnitsMatching(game, (unit) => unit.isWaveUnit), 2, "wave mobs should repeat on the next shared pulse");
  assert.equal(lane.totalSendCount, 4, "both barracks should stay aligned on later pulses");
});

test("resolveWaveForRound uses the most recent authored wave before a gap and only escalates past the authored tail", () => {
  const game = createGame();
  game.waveConfig = [
    { wave_number: 1, unit_type: "raider", spawn_qty: 2, hp_mult: 1, dmg_mult: 1, speed_mult: 1 },
    { wave_number: 3, unit_type: "raider", spawn_qty: 5, hp_mult: 2, dmg_mult: 3, speed_mult: 1.25 },
  ];

  const gapWave = simMl.resolveWaveForRound(game, 2);
  assert.equal(gapWave.spawn_qty, 2, "a missing authored round should reuse the last authored wave before the gap");
  assert.equal(gapWave.hp_mult, 1, "the gap wave should not downscale a future authored row");
  assert.equal(gapWave.dmg_mult, 1, "the gap wave should preserve the last authored combat values");

  const escalatedWave = simMl.resolveWaveForRound(game, 4);
  assert.equal(escalatedWave.spawn_qty, 5, "rounds past the authored tail should still use the last authored wave");
  assert.ok(Math.abs(escalatedWave.hp_mult - 2.2) < 1e-9, "tail escalation should scale the last authored hp multiplier");
  assert.ok(Math.abs(escalatedWave.dmg_mult - 3.3) < 1e-9, "tail escalation should scale the last authored damage multiplier");
  assert.equal(escalatedWave.speed_mult, 1.25, "tail escalation should preserve the authored speed multiplier");
});

test("barracks attackers do not block the next scheduled wave gate", () => {
  const game = createGame();
  const targetLane = game.lanes[1];
  const def = simMl.resolveUnitDef("raider");

  targetLane.units.push({
    id: "barracks_attacker_gate_test",
    unitId: "barracks_attacker_gate_test",
    unitTypeKey: "raider",
    allegianceKey: "red",
    ownerLaneIndex: 0,
    ownerLane: 0,
    targetLaneIndex: 1,
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    spawnSourceType: "barracks_roster",
    type: "raider",
    hp: def.hp,
    maxHp: def.hp,
    baseDmg: def.dmg,
    baseSpeed: def.pathSpeed,
    atkCd: 0,
    atkCdTicks: def.atkCdTicks,
    armorType: def.armorType,
    damageReductionPct: def.damageReductionPct,
    abilities: [],
    bounty: 1,
    stance: "ATTACK",
    pathContractType: "barracks_cross",
    isWaveUnit: false,
    isDefender: false,
    combatTarget: null,
    combatState: "MOVING",
  });

  assert.equal(simMl.countRemainingWaveMobs(game), 0, "scheduled wave gating should ignore barracks attackers");
  assert.equal(simMl.isCurrentWaveComplete(game), true, "scheduled wave completion should ignore barracks attackers");
  assert.equal(simMl.startNextWaveNow(game), true, "the next scheduled wave should still be allowed to start");
});
