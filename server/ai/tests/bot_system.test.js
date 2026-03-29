"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../../gameConfig");
const { setUnitTypesForTests } = require("../../unitTypes");
const simMl = require("../../sim-multilane");
const { FORT_ARCHETYPE_DEFS, FORT_UNIT_PRESENTATION_DEFS } = require("../../game/fortUnitCatalog");
const { getLockedWavePlan } = require("../../waveDefenseSpec");
const { AI_ACTION_TYPE } = require("../types");
const { BotBrain } = require("../bot");
const { createBotController, runHeadlessMatch, captureStateLite } = require("../sim_runner");

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

const LIVE_ACTION_TYPES = new Set([
  AI_ACTION_TYPE.BUILD_PAD,
  AI_ACTION_TYPE.UPGRADE_PAD,
  AI_ACTION_TYPE.BUILD_BARRACKS_SITE,
  AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE,
  AI_ACTION_TYPE.BUY_BARRACKS_UNIT,
  AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO,
  AI_ACTION_TYPE.SET_LANE_COMMAND,
]);

function makeUnit(key, options = {}) {
  return {
    id: options.id || key,
    key,
    name: options.name || key,
    enabled: true,
    send_cost: options.send_cost ?? 12,
    build_cost: options.build_cost ?? 14,
    income: options.income ?? 1,
    hp: options.hp ?? 90,
    attack_damage: options.attack_damage ?? 10,
    attack_speed: options.attack_speed ?? 1,
    path_speed: options.path_speed ?? 0.24,
    range: options.range ?? 0.1,
    projectile_travel_ticks: options.projectile_travel_ticks ?? 6,
    damage_type: options.damage_type || "NORMAL",
    armor_type: options.armor_type || "MEDIUM",
    damage_reduction_pct: options.damage_reduction_pct ?? 0,
    bounty: options.bounty ?? 2,
    special_props: options.special_props || {},
    abilities: options.abilities || [],
    barracks_scales_hp: options.barracks_scales_hp ?? false,
    barracks_scales_dmg: options.barracks_scales_dmg ?? false,
  };
}

const FORT_ARCHETYPE_BY_KEY = new Map(FORT_ARCHETYPE_DEFS.map((entry) => [entry.archetypeKey, entry]));

function getFortStats(family, tier) {
  const lvl = Math.max(1, Number(tier) || 1);
  switch (family) {
    case "shield":
      return { send_cost: 12 + lvl * 4, build_cost: 12 + lvl * 7, income: 1 + Math.floor(lvl / 2), hp: 110 + lvl * 45, attack_damage: 10 + lvl * 5, attack_speed: 0.9, path_speed: 0.2, range: 0.1, armor_type: "HEAVY" };
    case "polearm":
      return { send_cost: 11 + lvl * 4, build_cost: 11 + lvl * 6, income: 1 + Math.floor(lvl / 2), hp: 92 + lvl * 34, attack_damage: 11 + lvl * 5, attack_speed: 1.0, path_speed: 0.23, range: 0.14 };
    case "support":
      return { send_cost: 12 + lvl * 5, build_cost: 12 + lvl * 7, income: 2, hp: 88 + lvl * 24, attack_damage: 12 + lvl * 5, attack_speed: 0.95, path_speed: 0.22, range: 0.28, damage_type: "MAGIC", armor_type: "LIGHT" };
    case "arcane":
      return { send_cost: 13 + lvl * 6, build_cost: 13 + lvl * 8, income: 2, hp: 82 + lvl * 22, attack_damage: 14 + lvl * 7, attack_speed: 0.9, path_speed: 0.22, range: 0.32, damage_type: "MAGIC", armor_type: "LIGHT" };
    case "ranged":
      return { send_cost: 12 + lvl * 5, build_cost: 12 + lvl * 7, income: 2, hp: 84 + lvl * 24, attack_damage: 13 + lvl * 6, attack_speed: 1.0, path_speed: 0.24, range: 0.34, damage_type: "PIERCE", armor_type: "LIGHT" };
    case "economy":
      return { send_cost: 9 + lvl * 2, build_cost: 9 + lvl * 3, income: 2 + lvl, hp: 70 + lvl * 18, attack_damage: 7 + lvl * 2, attack_speed: 1.1, path_speed: 0.25, range: 0.08, armor_type: "LIGHT" };
    case "hero":
      return { send_cost: 34 + lvl * 8, build_cost: 34 + lvl * 10, income: 4, hp: 220 + lvl * 50, attack_damage: 26 + lvl * 8, attack_speed: 0.8, path_speed: 0.2, range: 0.18, armor_type: "HEAVY", damage_type: "MAGIC" };
    case "infantry":
    default:
      return { send_cost: 10 + lvl * 4, build_cost: 10 + lvl * 6, income: 1 + Math.floor(lvl / 2), hp: 95 + lvl * 32, attack_damage: 10 + lvl * 5, attack_speed: 1.0, path_speed: 0.24, range: 0.1 };
  }
}

function createFortUnits() {
  const seen = new Set();
  const units = [];
  for (const entry of FORT_UNIT_PRESENTATION_DEFS) {
    if (!entry || seen.has(entry.catalogUnitKey)) continue;
    seen.add(entry.catalogUnitKey);
    const archetype = FORT_ARCHETYPE_BY_KEY.get(entry.archetypeKey) || { family: "infantry", tier: 1 };
    units.push(makeUnit(entry.catalogUnitKey, getFortStats(archetype.family, archetype.tier)));
  }
  return units;
}

function createWaveUnits(existingKeys) {
  const units = [];
  for (const wave of getLockedWavePlan()) {
    if (!wave || existingKeys.has(wave.unit_type)) continue;
    existingKeys.add(wave.unit_type);
    const boss = !!wave.is_boss;
    const ranged = /spider|watcher|wyvern|harpy|dragon|viper/i.test(wave.unit_type);
    const heavy = boss || /ogre|troll|ent|demon|golem|hydra|cyclops|manticora|chimera/i.test(wave.unit_type);
    units.push(makeUnit(wave.unit_type, {
      send_cost: boss ? 30 : 10 + Math.floor(wave.wave_number / 3),
      build_cost: boss ? 44 : 18 + Math.floor(wave.wave_number / 2),
      income: boss ? 4 : 1 + Math.floor(wave.wave_number / 8),
      hp: boss ? 240 + wave.wave_number * 12 : 74 + wave.wave_number * 9,
      attack_damage: boss ? 28 + wave.wave_number : 8 + Math.floor(wave.wave_number * 0.9),
      attack_speed: boss ? 0.72 : 1.0,
      path_speed: boss ? 0.17 : 0.22 + Math.min(0.08, (Number(wave.speed_mult) || 1) * 0.02),
      range: ranged ? 0.28 : 0.1,
      damage_type: ranged ? "MAGIC" : "NORMAL",
      armor_type: heavy ? "HEAVY" : "MEDIUM",
    }));
  }
  return units;
}

const FORT_UNITS = createFortUnits();
const UNIT_KEY_SET = new Set(FORT_UNITS.map((unit) => unit.key));
setUnitTypesForTests([...FORT_UNITS, ...createWaveUnits(UNIT_KEY_SET)]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function makeFfaGame(playerCount) {
  const teams = new Array(playerCount).fill(0).map((_, i) => `ffa-${i}`);
  return simMl.createMLGame(playerCount, { laneTeams: teams, startGold: 120, startIncome: 18 });
}

function act(game, laneIndex, type, data) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, result.reason || `Expected '${type}' to succeed`);
  return result;
}

test("determinism: same seed yields identical replay log", () => {
  const options = {
    seed: "determinism-seed-1",
    playerCount: 4,
    laneTeams: ["ffa-0", "ffa-1", "ffa-2", "ffa-3"],
    maxTicks: 500,
    snapshotIntervalTicks: 25,
    botConfigs: [
      { laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE" },
      { laneIndex: 1, difficulty: "medium", personality: "RUSH" },
      { laneIndex: 2, difficulty: "medium", personality: "PRESSURE" },
      { laneIndex: 3, difficulty: "hard", personality: "ECO" },
    ],
  };

  const run1 = runHeadlessMatch(options);
  const run2 = runHeadlessMatch(options);
  assert.equal(JSON.stringify(run1.replayLog), JSON.stringify(run2.replayLog));
});

test("action legality: bots stay on fortress-native legal actions", () => {
  const result = runHeadlessMatch({
    seed: "legality-seed-1",
    playerCount: 3,
    laneTeams: ["ffa-0", "ffa-1", "ffa-2"],
    maxTicks: 500,
    botConfigs: [
      { laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE" },
      { laneIndex: 1, difficulty: "medium", personality: "RUSH" },
      { laneIndex: 2, difficulty: "medium", personality: "ECO" },
    ],
  });

  for (const lane of result.summary.laneSummaries)
    assert.equal(lane.invalidActions, 0, `lane ${lane.laneIndex} emitted invalid actions`);
  for (const event of result.replayLog.actions)
    assert.ok(LIVE_ACTION_TYPES.has(event.action.type), `unexpected legacy action ${event.action.type}`);
});

test("smoke: bot produces a fortress-native action within the first think window", () => {
  const result = runHeadlessMatch({
    seed: "smoke-seed-1",
    playerCount: 2,
    laneTeams: ["red", "blue"],
    maxTicks: 120,
    botConfigs: [
      { laneIndex: 0, difficulty: "hard", personality: "PRESSURE" },
      { laneIndex: 1, difficulty: "hard", personality: "RUSH" },
    ],
  });

  const earlyAction = result.replayLog.actions.find((evt) => evt.laneIndex === 0 && evt.tick <= 10);
  assert.ok(earlyAction, "expected a bot action from lane 0 within first 10 ticks");
  assert.ok(LIVE_ACTION_TYPES.has(earlyAction.action.type), `expected fortress-native action, got ${earlyAction.action.type}`);
});

test("personalities diverge on their next fortress tech choice", () => {
  const personalities = ["RUSH", "ECO", "PRESSURE", "ADAPTIVE"];
  const signatures = new Set();

  for (const personality of personalities) {
    const game = simMl.createMLGame(2, { laneTeams: ["red", "blue"], startGold: 700, startIncome: 20 });
    act(game, 0, "build_barracks_site", { barracksId: "center" });
    act(game, 0, "build_on_pad", { padId: "lumber_mill_pad" });
    act(game, 0, "build_on_pad", { padId: "blacksmith_pad" });
    act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia", count: 3 });

    const bot = new BotBrain({
      laneIndex: 0,
      difficulty: "hard",
      personality,
      seed: `personality-tech-${personality}`,
      unitDefMap: simMl.getMovingUnitDefMap(),
    });

    bot.memory.nextThinkTick = 0;
    const action = bot.tick({
      game,
      runtime: { laneLeakHistory: { 0: [0], 1: [0] }, recentLifeLossByLane: { 0: 0 }, currentTargetByLane: {} },
    });
    assert.equal(action.type, AI_ACTION_TYPE.BUILD_PAD, `expected ${personality} to choose its next tech branch`);
    signatures.add(String(action.padId || ""));
  }

  assert.ok(signatures.size >= 2, "expected at least two distinct fortress opening signatures across personalities");
});

test("pressure response: bot switches the lane into defend posture", () => {
  const game = simMl.createMLGame(2, { laneTeams: ["red", "blue"], startGold: 140, startIncome: 14 });
  game.lanes[0].commandState = "ATTACK";
  game.lanes[0].commandAnchorProgress = 1;
  game.lanes[0].formationAnchorProgress = 1;
  game.lanes[0].commandTargetLaneIndex = 1;
  const bot = new BotBrain({
    laneIndex: 0,
    difficulty: "insane",
    personality: "ADAPTIVE",
    seed: "pressure-response-seed",
    unitDefMap: simMl.getMovingUnitDefMap(),
  });

  bot.memory.nextThinkTick = 0;
  const action = bot.tick({
    game,
    runtime: { laneLeakHistory: { 0: [1, 2], 1: [0] }, recentLifeLossByLane: { 0: 1 }, currentTargetByLane: {} },
  });

  assert.equal(action.type, AI_ACTION_TYPE.SET_LANE_COMMAND);
  assert.equal(action.commandState, "DEFEND");
});

test("banking logic: bot saves for first branch unlock instead of rebuying militia", () => {
  const game = simMl.createMLGame(2, { laneTeams: ["red", "blue"], startGold: 180, startIncome: 18 });
  act(game, 0, "build_barracks_site", { barracksId: "center" });
  act(game, 0, "build_on_pad", { padId: "lumber_mill_pad" });
  act(game, 0, "buy_barracks_unit", { barracksId: "center", rosterKey: "militia", count: 1 });
  game.lanes[0].gold = 45;

  const bot = new BotBrain({
    laneIndex: 0,
    difficulty: "insane",
    personality: "ADAPTIVE",
    seed: "bank-first-unlock",
    unitDefMap: simMl.getMovingUnitDefMap(),
  });

  bot.memory.nextThinkTick = 0;
  const action = bot.tick({
    game,
    runtime: { laneLeakHistory: { 0: [0], 1: [0] }, recentLifeLossByLane: { 0: 0, 1: 0 }, currentTargetByLane: {} },
  });

  assert.notEqual(action.type, AI_ACTION_TYPE.BUY_BARRACKS_UNIT, "expected bot to bank for branch tech instead of rebuying militia");
});

test("ffa targeting: bot picks a target and retargets when target is removed", () => {
  const game = makeFfaGame(4);
  const controller = createBotController({
    game,
    seed: "ffa-target-seed-1",
    botTickMs: 500,
    botConfigs: [{ laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE" }],
  });
  const bot = controller.botsByLane[0];
  assert.ok(bot instanceof BotBrain);

  bot.memory.nextThinkTick = 0;
  bot.tick({ game, runtime: controller.getRuntimeView() });
  const firstTarget = bot.memory.currentTargetLaneIndex;
  assert.ok(Number.isInteger(firstTarget), "expected initial FFA target selection");

  game.lanes[firstTarget].eliminated = true;
  game.tick += bot.reactionDelayTicks;
  bot.memory.nextThinkTick = 0;
  bot.tick({ game, runtime: controller.getRuntimeView() });
  const secondTarget = bot.memory.currentTargetLaneIndex;
  assert.ok(Number.isInteger(secondTarget), "expected retarget selection");
  assert.notEqual(secondTarget, firstTarget, "expected target change after elimination");
});

test("controller can attach and detach a live bot for takeover", () => {
  const game = simMl.createMLGame(2, { laneTeams: ["red", "blue"], startGold: 160, startIncome: 20 });
  const controller = createBotController({
    game,
    seed: "takeover-seed-1",
    botTickMs: 250,
    botConfigs: [{ laneIndex: 1, difficulty: "medium", personality: "ECO" }],
  });

  assert.equal(controller.getBotCount(), 1);
  assert.equal(controller.hasBot(0), false);

  const attached = controller.addBot({ laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE", seedSuffix: "takeover" });
  assert.ok(attached, "expected live bot attachment");
  assert.equal(controller.hasBot(0), true);
  assert.equal(controller.getBotCount(), 2);

  for (let i = 0; i < 80; i += 1) {
    const prev = captureStateLite(game);
    controller.onBeforeSimTick(game);
    simMl.mlTick(game);
    controller.onAfterSimTick(prev, captureStateLite(game));
  }

  const actions = controller.drainActionLog().filter((evt) => evt.laneIndex === 0);
  assert.ok(actions.length > 0, "expected takeover lane to produce actions");
  assert.ok(actions.some((evt) => LIVE_ACTION_TYPES.has(evt.action.type)), "expected fortress-native takeover actions");

  controller.removeBot(0);
  assert.equal(controller.hasBot(0), false);
  assert.equal(controller.getBotCount(), 1);
});

test("self-play 2v2 completes with active fortress bots and no invalid actions", () => {
  const result = runHeadlessMatch({
    seed: "team-full-match-1",
    playerCount: 4,
    laneTeams: ["red", "red", "blue", "blue"],
    maxTicks: 2200,
    botTickMs: 250,
    startGold: 140,
    startIncome: 20,
    botConfigs: [
      { laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE" },
      { laneIndex: 1, difficulty: "insane", personality: "PRESSURE" },
      { laneIndex: 2, difficulty: "hard", personality: "ECO" },
      { laneIndex: 3, difficulty: "medium", personality: "RUSH" },
    ],
  });

  assert.ok(result.replayLog.actions.length >= 8, "expected active bots to produce multiple actions");
  assert.ok(result.replayLog.actions.some((evt) => evt.action.type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE), "expected barracks expansion");
  assert.ok(result.replayLog.actions.some((evt) => evt.action.type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT), "expected roster purchases");
  assert.ok(result.replayLog.actions.filter((evt) => evt.action.type === AI_ACTION_TYPE.BUILD_PAD).length >= 8, "expected midgame branch progression");
  assert.ok(result.replayLog.actions.some((evt) => evt.action.type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT && evt.action.rosterKey !== "militia"), "expected roster diversification beyond militia");
  for (const lane of result.summary.laneSummaries)
    assert.equal(lane.invalidActions, 0, `lane ${lane.laneIndex} emitted invalid actions`);
});
