"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");
const balanceTelemetry = require("../game/multilane/balanceTelemetry");

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
  makeUnit("tt_peasant", {
    hp: 52,
    attack_damage: 10,
    attack_speed: 16,
    path_speed: 0.3,
    range: 0.08,
  }),
  makeUnit("raider", {
    hp: 20,
    attack_damage: 3,
    attack_speed: 24,
    path_speed: 0.22,
    range: 0.08,
    bounty: 2,
  }),
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function tickUntil(game, predicate, maxTicks = 1200) {
  for (let tick = 0; tick < maxTicks; tick += 1) {
    if (predicate())
      return;
    simMl.mlTick(game);
  }
  assert.fail("Timed out waiting for balance-reporting scenario");
}

function clearEnemyWaveUnits(game) {
  for (const lane of game.lanes || []) {
    lane.spawnQueue = (lane.spawnQueue || []).filter((unit) => !unit.isWaveUnit);
    for (const unit of lane.units || []) {
      if (unit && unit.isWaveUnit)
        unit.hp = 0;
    }
  }
}

test("balance telemetry captures a rich wave report without changing legacy wave snapshots", () => {
  const game = simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold: 120,
    startIncome: 0,
    startingCombatMilitiaCount: 4,
  });
  game.waveConfig = [
    {
      wave_number: 1,
      unit_type: "raider",
      spawn_qty: 1,
      hp_mult: 0.2,
      dmg_mult: 0.2,
      speed_mult: 0.25,
    },
  ];
  game.waveIntervalTicks = 30;
  game.waveGroupIntervalTicks = 10;
  game.initialWaveDelayTicks = 1;

  const started = simMl.startNextWaveNow(game);
  assert.equal(started, true, "expected wave 1 to start immediately");

  tickUntil(game, () => game.activeWaveSession && game.activeWaveSession.groupsSpawned >= 1, 200);
  clearEnemyWaveUnits(game);
  if (game.activeWaveSession) {
    game.activeWaveSession.groupsSpawned = game.activeWaveSession.totalGroups;
    game.activeWaveSession.endsAtTick = game.tick;
  }
  tickUntil(game, () => !game.activeWaveSession && simMl.countRemainingWaveMobs(game) <= 0, 400);
  assert.equal(simMl.startNextWaveNow(game), true, "expected next-wave start to finalize wave 1");

  const finalized = simMl.finalizeMatchBalance(game, { finalResult: "completed" });
  assert.ok(finalized.summary, "expected a match summary");
  assert.equal(Array.isArray(finalized.waveReports), true);
  assert.equal(finalized.waveReports.length >= 1, true);

  const wave = finalized.waveReports[0];
  assert.equal(wave.waveNumber, 1);
  assert.equal(typeof wave.economy.goldAtWaveStart, "number");
  assert.equal(typeof wave.armyState.unitsAliveAtWaveStart, "number");
  assert.equal(typeof wave.combatResults.enemiesSpawned, "number");
  assert.equal(typeof wave.derived.struggleScore, "number");
  assert.equal(typeof wave.powerCurve.playerToEnemyPowerRatio === "number" || wave.powerCurve.playerToEnemyPowerRatio === null, true);
  assert.equal(Array.isArray(wave.laneSnapshots && wave.laneSnapshots.peak), true);
  assert.equal(wave.laneSnapshots.peak.length >= 1, true);
  assert.equal(typeof wave.laneSnapshots.peak[0].playerArmyValue, "number");

  assert.equal(finalized.summary.economy.startingEconomyReport.startingGold > 0, true);
  assert.equal(Array.isArray(finalized.summary.readable.perWaveLog), true);
  assert.equal(finalized.summary.readable.perWaveLog.length >= 1, true);
  assert.equal(typeof finalized.summary.timeline.trendDatasetRow.finalWaveReached, "number");

  const legacySnapshots = game.roundSnapshots;
  assert.equal(Array.isArray(legacySnapshots), true);
  assert.equal(legacySnapshots.length >= 1, true);
  assert.equal(Array.isArray(legacySnapshots[0].lanes), true);
  assert.equal(typeof legacySnapshots[0].lanes[0].buildValue, "number");
  assert.equal(legacySnapshots[0].lanes[0].holdResult != null, true);
});

test("multi-match balance report aggregates stored trend rows", () => {
  const report = balanceTelemetry.buildMultiMatchBalanceReport([
    {
      id: "m1",
      mode: "multilane",
      started_at: "2026-04-03T10:00:00Z",
      ended_at: "2026-04-03T10:12:00Z",
      balance_flags: [{ key: "snowball_detected" }],
      balance_summary: {
        readable: { diagnosis: ["Snowball detected: yes."] },
        timeline: {
          trendDatasetRow: {
            finalWaveReached: 9,
            stabilizationWave: 4,
            snowballWave: 6,
            earlyGameDifficultyScore: 58,
            peakGoldFloat: 620,
            peakUnitCount: 14,
          },
        },
      },
    },
    {
      id: "m2",
      mode: "multilane",
      started_at: "2026-04-03T11:00:00Z",
      ended_at: "2026-04-03T11:15:00Z",
      balance_flags: [{ key: "economy_overflow" }],
      balance_summary: {
        readable: { diagnosis: ["Economy overflow: yes."] },
        timeline: {
          trendDatasetRow: {
            finalWaveReached: 11,
            stabilizationWave: 5,
            snowballWave: 7,
            earlyGameDifficultyScore: 42,
            peakGoldFloat: 880,
            peakUnitCount: 18,
          },
        },
      },
    },
  ]);

  assert.equal(report.aggregate.sampleSize, 2);
  assert.equal(report.aggregate.averageFinalWaveReached, 10);
  assert.equal(report.aggregate.averageWaveWherePlayerStabilizes, 4.5);
  assert.equal(report.aggregate.snowballRate, 50);
  assert.equal(report.aggregate.economyOverflowRate, 50);
  assert.equal(report.trendRows.length, 2);
});
