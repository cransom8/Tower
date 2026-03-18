"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const simMl = require("../../server/sim-multilane");
const { BotBrain } = require("../bot");
const { createBotController, runHeadlessMatch } = require("../sim_runner");

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

