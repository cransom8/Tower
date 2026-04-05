"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");
const { buildPlayerBattleReport } = require("../game/playerBattleReport");

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
  assert.fail("Timed out waiting for player battle report scenario");
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

function buildFinalStats(game) {
  return (game.lanes || []).map((lane) => ({
    laneIndex: lane.laneIndex,
    displayName: `Lane ${lane.laneIndex + 1}`,
    isAI: lane.laneIndex !== 0,
    difficulty: lane.laneIndex !== 0 ? "normal" : null,
    team: lane.team,
    side: lane.side,
    income: lane.income,
    buildValue: 0,
    gold: Math.floor(lane.gold),
    totalSendSpend: lane.totalSendSpend,
    totalSendCount: lane.totalSendCount || 0,
    totalBuildSpend: lane.totalBuildSpend || 0,
    totalLeaksTaken: lane.totalLeaksTaken || 0,
    biggestLeakTaken: lane.biggestLeakTaken || 0,
    wavesHeld: lane.wavesHeld || 0,
    wavesLeaked: lane.wavesLeaked || 0,
    longestHoldStreak: lane.longestHoldStreak || 0,
    teamHp: lane.lives,
    eliminated: !!lane.eliminated,
    builtStructureCounts: {},
    fortressTiers: {},
    barracksSites: [],
    barracksRosterOwned: {},
    marketRosterOwned: {},
    heroStates: [],
  }));
}

function makeFinalLaneStat(laneIndex, overrides = {}) {
  return {
    laneIndex,
    displayName: overrides.displayName || `Lane ${laneIndex + 1}`,
    isAI: overrides.isAI ?? laneIndex !== 0,
    difficulty: overrides.difficulty ?? (laneIndex !== 0 ? "normal" : null),
    team: overrides.team || (laneIndex === 0 ? "blue" : "red"),
    side: overrides.side || (laneIndex === 0 ? "left" : "right"),
    income: overrides.income ?? 10,
    buildValue: overrides.buildValue ?? 0,
    gold: overrides.gold ?? 0,
    totalSendSpend: overrides.totalSendSpend ?? 0,
    totalSendCount: overrides.totalSendCount ?? 0,
    totalBuildSpend: overrides.totalBuildSpend ?? 0,
    totalLeaksTaken: overrides.totalLeaksTaken ?? 0,
    biggestLeakTaken: overrides.biggestLeakTaken ?? 0,
    wavesHeld: overrides.wavesHeld ?? 0,
    wavesLeaked: overrides.wavesLeaked ?? 0,
    longestHoldStreak: overrides.longestHoldStreak ?? 0,
    teamHp: overrides.teamHp ?? 20,
    eliminated: overrides.eliminated ?? false,
    builtStructureCounts: overrides.builtStructureCounts || {},
    fortressTiers: overrides.fortressTiers || {},
    barracksSites: overrides.barracksSites || [],
    barracksRosterOwned: overrides.barracksRosterOwned || {},
    marketRosterOwned: overrides.marketRosterOwned || {},
    heroStates: overrides.heroStates || [],
  };
}

test("player-facing battle report exposes commander-friendly sections without leak terminology", () => {
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

  assert.equal(simMl.startNextWaveNow(game), true);
  tickUntil(game, () => game.activeWaveSession && game.activeWaveSession.groupsSpawned >= 1, 200);
  clearEnemyWaveUnits(game);
  if (game.activeWaveSession) {
    game.activeWaveSession.groupsSpawned = game.activeWaveSession.totalGroups;
    game.activeWaveSession.endsAtTick = game.tick;
  }
  tickUntil(game, () => !game.activeWaveSession && simMl.countRemainingWaveMobs(game) <= 0, 400);
  const balanceData = simMl.finalizeMatchBalance(game, { finalResult: "completed" });

  const report = buildPlayerBattleReport({
    game,
    balanceData,
    finalStats: buildFinalStats(game),
    roundSnapshots: game.roundSnapshots,
  });

  assert.equal(report.schemaVersion, 1);
  assert.equal(Array.isArray(report.tabs), true);
  assert.equal(Array.isArray(report.lanes), true);
  assert.equal(report.lanes.length, 2);
  assert.ok(report.lanes[0].snapshot);
  assert.ok(report.lanes[0].economyCurve);
  assert.ok(report.lanes[0].armyCurve);
  assert.ok(report.lanes[0].threatCurve);
  assert.ok(report.lanes[0].battleStory);
  assert.ok(report.lanes[0].advanced);
  assert.equal(report.lanes[0].armyCurve.lines.some((line) => line && /Lane 2 Army Strength/.test(line.label)), true);
  assert.equal(report.lanes[0].threatCurve.lines.some((line) => line && /Lane 2 Battle Strength/.test(line.label)), true);

  const serialized = JSON.stringify(report);
  assert.equal(/leak/i.test(serialized), false, "player-facing report should not surface legacy leak terminology");
});

test("battle report uses peak lane army values so rival strength survives end-of-wave cleanup", () => {
  const game = {
    teamHpMax: 20,
    balanceTelemetry: { unitInstances: {} },
    lanes: [
      { laneIndex: 0, fortressPads: [], barracksSiteStates: {}, buildingUpgradeState: {} },
      { laneIndex: 1, fortressPads: [], barracksSiteStates: {}, buildingUpgradeState: {} },
    ],
  };

  const balanceData = {
    summary: {
      matchIdentity: {
        finalResult: "completed",
        finalWaveReached: 1,
        totalMatchDuration: 42,
        mode: "multilane",
      },
      economy: {
        startingEconomyReport: {
          startingGold: 240,
        },
      },
    },
    waveReports: [
      {
        waveNumber: 1,
        laneSnapshots: {
          start: [
            { laneIndex: 0, eliminated: false, playerArmyValue: 150, gold: 80 },
            { laneIndex: 1, eliminated: false, playerArmyValue: 320, gold: 70 },
          ],
          peak: [
            { laneIndex: 0, playerArmyValue: 210 },
            { laneIndex: 1, playerArmyValue: 480 },
          ],
          end: [
            { laneIndex: 0, eliminated: false, playerArmyValue: 20, gold: 95 },
            { laneIndex: 1, eliminated: false, playerArmyValue: 0, gold: 40 },
          ],
        },
        powerCurve: {
          enemyWaveEffectivePower: 260,
        },
        pressure: {
          secondsUnderBreachPressure: 0,
          secondsWithoutFrontline: 0,
        },
        combatResults: {
          wallDamageTaken: 0,
          towerDamageTaken: 0,
        },
      },
    ],
  };

  const report = buildPlayerBattleReport({
    game,
    balanceData,
    finalStats: [
      makeFinalLaneStat(0, { displayName: "Blue Commander", team: "blue", side: "left", totalSendSpend: 30, totalBuildSpend: 20, gold: 95 }),
      makeFinalLaneStat(1, { displayName: "Red Commander", team: "red", side: "right", totalSendSpend: 24, totalBuildSpend: 16, gold: 40 }),
    ],
    roundSnapshots: [
      {
        round: 1,
        lanes: [
          { laneIndex: 0, gold: 95, income: 10, sendSpend: 30, buildSpend: 20, leaksTaken: 0, leakDamage: 0 },
          { laneIndex: 1, gold: 40, income: 9, sendSpend: 24, buildSpend: 16, leaksTaken: 0, leakDamage: 0 },
        ],
      },
    ],
  });

  const armyCurve = report.lanes[0].armyCurve.lines.find((line) => line && line.label === "Red Commander Army Strength");
  const threatCurve = report.lanes[0].threatCurve.lines.find((line) => line && line.label === "Red Commander Battle Strength");

  assert.deepEqual(armyCurve && armyCurve.values, [480]);
  assert.deepEqual(threatCurve && threatCurve.values, [480]);
  assert.equal(report.lanes[0].advanced.waveRows[0].armyStrength, 210);
  assert.equal(report.lanes[0].advanced.waveRows[0].opponentArmyStrength, 480);
});
