"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const { setUnitTypesForTests } = require("../../unitTypes");
const simMl = require("../../sim-multilane");
const { BotBrain } = require("../bot");
const { createBotController, runHeadlessMatch, captureStateLite } = require("../sim_runner");

function makeUnit(key, options = {}) {
  return {
    id: options.id || key,
    key,
    name: options.name || key,
    enabled: true,
    send_cost: options.send_cost ?? 20,
    build_cost: options.build_cost ?? 35,
    income: options.income ?? 2,
    hp: options.hp ?? 120,
    attack_damage: options.attack_damage ?? 12,
    attack_speed: options.attack_speed ?? 1,
    path_speed: options.path_speed ?? 0.22,
    range: options.range ?? 0.22,
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

setUnitTypesForTests([
  makeUnit("archer", { send_cost: 16, build_cost: 30, income: 2, hp: 95, attack_damage: 10, attack_speed: 1.2, range: 0.36, armor_type: "LIGHT" }),
  makeUnit("fighter", { send_cost: 22, build_cost: 36, income: 2, hp: 160, attack_damage: 16, attack_speed: 0.9, range: 0.12, armor_type: "HEAVY" }),
  makeUnit("mage", { send_cost: 26, build_cost: 42, income: 3, hp: 110, attack_damage: 20, attack_speed: 0.95, range: 0.34, damage_type: "MAGIC", armor_type: "LIGHT" }),
  makeUnit("ballista", { send_cost: 30, build_cost: 48, income: 3, hp: 135, attack_damage: 28, attack_speed: 0.75, range: 0.42, damage_type: "PIERCE" }),
  makeUnit("cannon", { send_cost: 34, build_cost: 54, income: 4, hp: 155, attack_damage: 30, attack_speed: 0.7, range: 0.28, damage_type: "SPLASH" }),
  makeUnit("runner", { send_cost: 14, build_cost: 28, income: 2, hp: 85, attack_damage: 9, attack_speed: 1.3, path_speed: 0.3, range: 0.1, armor_type: "LIGHT" }),
  makeUnit("footman", { send_cost: 20, build_cost: 34, income: 2, hp: 130, attack_damage: 13, attack_speed: 1.0, path_speed: 0.22, range: 0.12 }),
  makeUnit("ironclad", { send_cost: 30, build_cost: 48, income: 3, hp: 185, attack_damage: 22, attack_speed: 0.72, path_speed: 0.18, range: 0.1, armor_type: "HEAVY" }),
  makeUnit("warlock", { send_cost: 28, build_cost: 46, income: 3, hp: 120, attack_damage: 19, attack_speed: 0.85, path_speed: 0.2, range: 0.32, damage_type: "MAGIC", special_props: { warlockDebuff: true } }),
  makeUnit("golem", { send_cost: 38, build_cost: 56, income: 4, hp: 220, attack_damage: 24, attack_speed: 0.65, path_speed: 0.16, range: 0.1, armor_type: "HEAVY" }),
  makeUnit("goblin", { send_cost: 10, build_cost: 24, income: 1, hp: 70, attack_damage: 7, attack_speed: 1.45, path_speed: 0.32, range: 0.08, armor_type: "LIGHT" }),
  makeUnit("kobold", { send_cost: 11, build_cost: 24, income: 1, hp: 72, attack_damage: 7, attack_speed: 1.42, path_speed: 0.31, range: 0.08, armor_type: "LIGHT" }),
  makeUnit("ogre", { send_cost: 34, build_cost: 52, income: 4, hp: 210, attack_damage: 26, attack_speed: 0.7, path_speed: 0.18, range: 0.1, armor_type: "HEAVY" }),
  makeUnit("troll", { send_cost: 26, build_cost: 44, income: 3, hp: 160, attack_damage: 18, attack_speed: 0.88, path_speed: 0.21, range: 0.12 }),
  makeUnit("werewolf", { send_cost: 32, build_cost: 48, income: 3, hp: 150, attack_damage: 24, attack_speed: 1.0, path_speed: 0.26, range: 0.1 }),
  makeUnit("griffin", { send_cost: 36, build_cost: 52, income: 4, hp: 145, attack_damage: 26, attack_speed: 1.05, path_speed: 0.28, range: 0.28, damage_type: "PIERCE" }),
  makeUnit("chimera", { send_cost: 42, build_cost: 62, income: 5, hp: 240, attack_damage: 34, attack_speed: 0.68, path_speed: 0.17, range: 0.26, damage_type: "SPLASH", armor_type: "HEAVY" }),
  makeUnit("manticora", { send_cost: 44, build_cost: 64, income: 5, hp: 255, attack_damage: 36, attack_speed: 0.62, path_speed: 0.17, range: 0.18, armor_type: "HEAVY" }),
  makeUnit("mountain_dragon", { send_cost: 48, build_cost: 68, income: 5, hp: 250, attack_damage: 38, attack_speed: 0.58, path_speed: 0.16, range: 0.34, damage_type: "SPLASH", armor_type: "HEAVY" }),
  makeUnit("giant_viper", { send_cost: 24, build_cost: 40, income: 3, hp: 118, attack_damage: 21, attack_speed: 1.05, path_speed: 0.23, range: 0.26, damage_type: "MAGIC" }),
  makeUnit("evil_watcher", { send_cost: 28, build_cost: 44, income: 3, hp: 126, attack_damage: 22, attack_speed: 0.95, path_speed: 0.2, range: 0.32, damage_type: "MAGIC" }),
]);

function makeFfaGame(playerCount) {
  const teams = new Array(playerCount).fill(0).map((_, i) => `ffa-${i}`);
  return simMl.createMLGame(playerCount, { laneTeams: teams, startGold: 80, startIncome: 12 });
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

test("action legality: bots do not emit invalid actions", () => {
  const result = runHeadlessMatch({
    seed: "legality-seed-1",
    playerCount: 3,
    laneTeams: ["ffa-0", "ffa-1", "ffa-2"],
    maxTicks: 400,
    botConfigs: [
      { laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE" },
      { laneIndex: 1, difficulty: "medium", personality: "RUSH" },
      { laneIndex: 2, difficulty: "medium", personality: "ECO" },
    ],
  });
  for (const lane of result.summary.laneSummaries) {
    assert.equal(lane.invalidActions, 0, `lane ${lane.laneIndex} emitted invalid actions`);
  }
});

test("smoke: bot produces at least one non-idle action within 10 ticks", () => {
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
});

test("personalities diverge on first defensive build from identical state", () => {
  const personalities = ["RUSH", "ECO", "PRESSURE", "ADAPTIVE"];
  const signatures = new Set();

  for (const personality of personalities) {
    const game = simMl.createMLGame(2, { laneTeams: ["red", "blue"], startGold: 80, startIncome: 8 });
    const lane = game.lanes[0];
    lane.lives = 6;
    lane.gold = 80;
    lane.income = 8;
    lane.autosend.loadoutKeys = ["archer", "fighter", "mage", "ballista", "cannon"];
    lane.autosend.enabledUnits = Object.fromEntries(lane.autosend.loadoutKeys.map((key) => [key, false]));

    const bot = new BotBrain({
      laneIndex: 0,
      difficulty: "hard",
      personality,
      seed: `personality-build-${personality}`,
      unitDefMap: Object.fromEntries(lane.autosend.loadoutKeys.map((key) => [key, simMl.resolveUnitDef(key)])),
    });

    bot.memory.nextThinkTick = 0;
    const action = bot.tick({ game, runtime: { laneLeakHistory: { 0: [1] }, recentLifeLossByLane: { 0: 1 } } });
    assert.equal(action.type, "BUILD_TOWER", `expected ${personality} to choose a build action`);
    signatures.add(`${action.towerType}@${action.tileId}`);
  }

  assert.ok(signatures.size >= 2, "expected at least two distinct opening build signatures across personalities");
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
  const game = simMl.createMLGame(2, { laneTeams: ["red", "blue"], startGold: 120, startIncome: 20 });
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

  controller.removeBot(0);
  assert.equal(controller.hasBot(0), false);
  assert.equal(controller.getBotCount(), 1);
});

test("self-play 2v2 completes with active bots and no invalid actions", () => {
  const result = runHeadlessMatch({
    seed: "team-full-match-1",
    playerCount: 4,
    laneTeams: ["red", "red", "blue", "blue"],
    maxTicks: 1800,
    botTickMs: 250,
    startGold: 120,
    startIncome: 20,
    botConfigs: [
      { laneIndex: 0, difficulty: "hard", personality: "ADAPTIVE" },
      { laneIndex: 1, difficulty: "insane", personality: "PRESSURE" },
      { laneIndex: 2, difficulty: "hard", personality: "ECO" },
      { laneIndex: 3, difficulty: "medium", personality: "RUSH" },
    ],
  });

  assert.ok(result.replayLog.actions.length >= 4, "expected all lanes to act at least once");
  for (const lane of result.summary.laneSummaries) {
    assert.equal(lane.invalidActions, 0, `lane ${lane.laneIndex} emitted invalid actions`);
  }
});
