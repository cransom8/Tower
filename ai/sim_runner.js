"use strict";

const simMl = require("../server/sim-multilane");
const { BotBrain } = require("./bot");
const { clampCountBucket, createDoNothingAction, translateActionToCommands, validateActionAgainstGame } = require("./actions");
const { computeReward } = require("./reward");

function captureStateLite(game) {
  return {
    tick: Number(game && game.tick) || 0,
    phase: game && game.phase,
    winner: game && game.winner,
    lanes: (game && game.lanes ? game.lanes : []).map((lane) => ({
      laneIndex: lane.laneIndex,
      team: lane.team,
      eliminated: !!lane.eliminated,
      gold: Number(lane.gold) || 0,
      income: Number(lane.income) || 0,
      lives: Number(lane.lives) || 0,
    })),
  };
}

function getTeamOpponents(game, laneIndex) {
  const lane = game && game.lanes && game.lanes[laneIndex];
  if (!lane) return [];
  return (game.lanes || []).filter((other) => other && other.laneIndex !== laneIndex && other.team !== lane.team);
}

class RuntimeTracker {
  constructor(game, options) {
    const opt = options && typeof options === "object" ? options : {};
    this.waveTickInterval = Math.max(1, Number(opt.waveTickInterval) || 240);
    this.maxHistory = Math.max(8, Number(opt.maxHistory) || 24);

    this.laneLeakHistory = {};
    this.sendHistoryBySourceLane = {};
    this.incomingSendHistoryByLane = {};
    this.lastSendBySourceLane = {};
    this.invalidActionCountByLane = {};
    this.invalidActionsThisTickByLane = {};
    this.recentLifeLossByLane = {};
    this.currentTargetByLane = {};
    this.teamPlans = {};
    this.cumulativeRewardByLane = {};
    this.lastRewardByLane = {};

    this.ensureLaneSlots(game);
  }

  ensureLaneSlots(game) {
    for (const lane of (game && game.lanes) || []) {
      const i = lane.laneIndex;
      if (!Array.isArray(this.laneLeakHistory[i])) this.laneLeakHistory[i] = [0];
      if (!Array.isArray(this.sendHistoryBySourceLane[i])) this.sendHistoryBySourceLane[i] = [];
      if (!Array.isArray(this.incomingSendHistoryByLane[i])) this.incomingSendHistoryByLane[i] = [];
      if (!Number.isFinite(this.invalidActionCountByLane[i])) this.invalidActionCountByLane[i] = 0;
      if (!Number.isFinite(this.invalidActionsThisTickByLane[i])) this.invalidActionsThisTickByLane[i] = 0;
      if (!Number.isFinite(this.recentLifeLossByLane[i])) this.recentLifeLossByLane[i] = 0;
      if (!Number.isFinite(this.cumulativeRewardByLane[i])) this.cumulativeRewardByLane[i] = 0;
      if (!Number.isFinite(this.lastRewardByLane[i])) this.lastRewardByLane[i] = 0;
    }
  }

  resetTickMetrics(game) {
    this.ensureLaneSlots(game);
    for (const lane of (game && game.lanes) || []) {
      this.invalidActionsThisTickByLane[lane.laneIndex] = 0;
      this.recentLifeLossByLane[lane.laneIndex] = 0;
      this.lastRewardByLane[lane.laneIndex] = 0;
    }
  }

  beginWaveIfNeeded(prevState, nextState) {
    const prevWave = Math.floor((Number(prevState && prevState.tick) || 0) / this.waveTickInterval);
    const nextWave = Math.floor((Number(nextState && nextState.tick) || 0) / this.waveTickInterval);
    if (nextWave <= prevWave) return;
    const steps = nextWave - prevWave;
    for (const lane of (nextState && nextState.lanes) || []) {
      const hist = this.laneLeakHistory[lane.laneIndex] || [0];
      for (let i = 0; i < steps; i++) hist.push(0);
      while (hist.length > this.maxHistory) hist.shift();
      this.laneLeakHistory[lane.laneIndex] = hist;
    }
  }

  recordInvalidAction(laneIndex) {
    this.invalidActionCountByLane[laneIndex] = (this.invalidActionCountByLane[laneIndex] || 0) + 1;
    this.invalidActionsThisTickByLane[laneIndex] = (this.invalidActionsThisTickByLane[laneIndex] || 0) + 1;
  }

  recordSendEvent(sourceLaneIndex, targetLaneIndex, unitType, requestedCountBucket, actualCount, tick) {
    const event = {
      tick: Number(tick) || 0,
      sourceLaneIndex,
      targetLaneIndex,
      unitType: String(unitType || ""),
      countBucket: clampCountBucket(requestedCountBucket || actualCount || 1),
      actualCount: Math.max(0, Number(actualCount) || 0),
    };
    const out = this.sendHistoryBySourceLane[sourceLaneIndex] || [];
    out.push(event);
    while (out.length > this.maxHistory) out.shift();
    this.sendHistoryBySourceLane[sourceLaneIndex] = out;
    this.lastSendBySourceLane[sourceLaneIndex] = event;

    if (Number.isInteger(targetLaneIndex)) {
      const incoming = this.incomingSendHistoryByLane[targetLaneIndex] || [];
      incoming.push(event);
      while (incoming.length > this.maxHistory) incoming.shift();
      this.incomingSendHistoryByLane[targetLaneIndex] = incoming;
    }
  }

  afterSimTick(prevState, nextState) {
    this.beginWaveIfNeeded(prevState, nextState);
    const nextLanes = (nextState && nextState.lanes) || [];
    for (const nextLane of nextLanes) {
      const i = nextLane.laneIndex;
      const prevLane = (prevState && prevState.lanes || []).find((l) => l.laneIndex === i);
      const prevLives = Number(prevLane && prevLane.lives) || 0;
      const nextLives = Number(nextLane.lives) || 0;
      const lifeLoss = Math.max(0, prevLives - nextLives);
      this.recentLifeLossByLane[i] = lifeLoss;
      if (lifeLoss > 0) {
        const hist = this.laneLeakHistory[i] || [0];
        hist[hist.length - 1] = (hist[hist.length - 1] || 0) + lifeLoss;
        this.laneLeakHistory[i] = hist;
      }
    }
  }

  rewardEventsForLane(prevState, nextState, laneIndex) {
    const opponents = getTeamOpponents({ lanes: nextState.lanes }, laneIndex);
    let leakDamageDealt = 0;
    for (const opp of opponents) {
      const loss = Number(this.recentLifeLossByLane[opp.laneIndex]) || 0;
      leakDamageDealt += loss;
    }
    return {
      leakDamageDealt,
      leakDamageTaken: Number(this.recentLifeLossByLane[laneIndex]) || 0,
      invalidActionCount: Number(this.invalidActionsThisTickByLane[laneIndex]) || 0,
    };
  }

  applyRewards(prevState, nextState) {
    for (const lane of nextState.lanes || []) {
      const events = this.rewardEventsForLane(prevState, nextState, lane.laneIndex);
      const r = computeReward(prevState, nextState, events, lane.laneIndex);
      this.lastRewardByLane[lane.laneIndex] = r;
      this.cumulativeRewardByLane[lane.laneIndex] = (this.cumulativeRewardByLane[lane.laneIndex] || 0) + r;
    }
  }

  getRuntimeView() {
    return {
      waveTickInterval: this.waveTickInterval,
      laneLeakHistory: this.laneLeakHistory,
      sendHistoryBySourceLane: this.sendHistoryBySourceLane,
      incomingSendHistoryByLane: this.incomingSendHistoryByLane,
      lastSendBySourceLane: this.lastSendBySourceLane,
      invalidActionCountByLane: this.invalidActionCountByLane,
      invalidActionsThisTickByLane: this.invalidActionsThisTickByLane,
      recentLifeLossByLane: this.recentLifeLossByLane,
      currentTargetByLane: this.currentTargetByLane,
      teamPlans: this.teamPlans,
      cumulativeRewardByLane: this.cumulativeRewardByLane,
      lastRewardByLane: this.lastRewardByLane,
    };
  }
}

function executeBotAction(game, laneIndex, action, runtime, options) {
  const opt = options && typeof options === "object" ? options : {};
  const tick = Number(game && game.tick) || 0;
  const safeAction = action || createDoNothingAction();

  const checked = validateActionAgainstGame(game, laneIndex, safeAction);
  if (!checked.ok) {
    runtime.recordInvalidAction(laneIndex);
    return {
      ok: false,
      laneIndex,
      tick,
      action: createDoNothingAction(),
      reason: checked.reason,
      appliedCommands: 0,
      sendSpawnedCount: 0,
    };
  }

  const mapped = translateActionToCommands(game, laneIndex, checked.normalized);
  if (!mapped.ok) {
    runtime.recordInvalidAction(laneIndex);
    return {
      ok: false,
      laneIndex,
      tick,
      action: checked.normalized,
      reason: mapped.reason,
      appliedCommands: 0,
      sendSpawnedCount: 0,
    };
  }

  let appliedCommands = 0;
  let sendSpawnedCount = 0;
  let lastReason = null;
  const maxCommands = Number.isFinite(opt.maxCommandsPerAction) ? Math.max(1, Math.floor(opt.maxCommandsPerAction)) : Infinity;
  for (let i = 0; i < mapped.commands.length && i < maxCommands; i++) {
    const command = mapped.commands[i];
    const res = simMl.applyMLAction(game, laneIndex, command);
    if (!res.ok) {
      lastReason = res.reason || "Action rejected";
      break;
    }
    appliedCommands += 1;
    if (command.type === "spawn_unit") sendSpawnedCount += 1;
  }

  if (mapped.commands.length > 0 && appliedCommands === 0) {
    runtime.recordInvalidAction(laneIndex);
  }

  if (checked.normalized.type === "SEND_UNITS" && sendSpawnedCount > 0) {
    runtime.recordSendEvent(
      laneIndex,
      checked.normalized.laneId,
      checked.normalized.unitType,
      checked.normalized.countBucket,
      sendSpawnedCount,
      tick
    );
  }

  return {
    ok: lastReason === null || appliedCommands > 0 || mapped.commands.length === 0,
    laneIndex,
    tick,
    action: checked.normalized,
    reason: lastReason,
    appliedCommands,
    sendSpawnedCount,
  };
}

class BotMatchController {
  constructor(config) {
    const cfg = config && typeof config === "object" ? config : {};
    this.game = cfg.game;
    this.tickMs = Math.max(1, Number(cfg.tickMs) || simMl.TICK_MS || 50);
    this.botTickMs = Math.max(100, Number(cfg.botTickMs) || 500);
    this.botTickIntervalTicks = Math.max(1, Math.round(this.botTickMs / this.tickMs));
    this.runtimeTracker = new RuntimeTracker(this.game, cfg.runtimeOptions || {});
    this.botsByLane = {};
    this.actionLog = [];

    const botConfigs = Array.isArray(cfg.botConfigs) ? cfg.botConfigs : [];
    for (const botCfg of botConfigs) {
      if (!botCfg || !Number.isInteger(botCfg.laneIndex)) continue;
      const fullCfg = Object.assign({}, botCfg, { tickMs: this.tickMs, seed: `${cfg.seed || "match"}:${botCfg.laneIndex}` });
      this.botsByLane[botCfg.laneIndex] = new BotBrain(fullCfg);
    }
  }

  getRuntimeView() {
    return this.runtimeTracker.getRuntimeView();
  }

  getBotCount() {
    return Object.keys(this.botsByLane).length;
  }

  onBeforeSimTick(game) {
    this.runtimeTracker.resetTickMetrics(game);
    if (!game || game.phase !== "playing") return;
    const tick = Number(game.tick) || 0;
    if (tick % this.botTickIntervalTicks !== 0) return;

    const laneIndices = Object.keys(this.botsByLane).map(Number).sort((a, b) => a - b);
    for (const laneIndex of laneIndices) {
      const lane = game.lanes && game.lanes[laneIndex];
      if (!lane || lane.eliminated) continue;
      const bot = this.botsByLane[laneIndex];
      const action = bot.tick({ game, runtime: this.runtimeTracker.getRuntimeView() });
      const result = executeBotAction(game, laneIndex, action, this.runtimeTracker);
      if (result.action && result.action.type !== "DO_NOTHING") {
        this.actionLog.push(result);
      }
    }
  }

  onAfterSimTick(prevState, nextState) {
    this.runtimeTracker.afterSimTick(prevState, nextState);
    this.runtimeTracker.applyRewards(prevState, nextState);
  }

  drainActionLog() {
    const out = this.actionLog.slice();
    this.actionLog.length = 0;
    return out;
  }
}

function createBotController(config) {
  return new BotMatchController(config);
}

function runHeadlessMatch(options) {
  const opt = options && typeof options === "object" ? options : {};
  const playerCount = Math.max(2, Math.min(4, Number(opt.playerCount) || 2));
  const maxTicks = Math.max(1, Number(opt.maxTicks) || 6000);
  const snapshotIntervalTicks = Math.max(1, Number(opt.snapshotIntervalTicks) || 40);
  const laneTeams = Array.isArray(opt.laneTeams) && opt.laneTeams.length === playerCount
    ? opt.laneTeams
    : new Array(playerCount).fill(0).map((_, i) => `ffa-${i}`);

  const game = simMl.createMLGame(playerCount, {
    startGold: opt.startGold,
    startIncome: opt.startIncome,
    laneTeams,
  });

  const botConfigs = Array.isArray(opt.botConfigs)
    ? opt.botConfigs
    : new Array(playerCount).fill(0).map((_, laneIndex) => ({ laneIndex, difficulty: "medium" }));
  const controller = createBotController({
    game,
    botConfigs,
    seed: opt.seed || "headless-seed",
    tickMs: simMl.TICK_MS,
    botTickMs: opt.botTickMs || 500,
    runtimeOptions: { waveTickInterval: opt.waveTickInterval || 240 },
  });

  const replayLog = {
    seed: String(opt.seed || "headless-seed"),
    options: {
      playerCount,
      laneTeams,
      maxTicks,
      botTickMs: opt.botTickMs || 500,
    },
    actions: [],
    snapshots: [],
  };

  let ticks = 0;
  while (game.phase === "playing" && ticks < maxTicks) {
    const prev = captureStateLite(game);
    controller.onBeforeSimTick(game);
    simMl.mlTick(game);
    const next = captureStateLite(game);
    controller.onAfterSimTick(prev, next);

    const actionEvents = controller.drainActionLog();
    for (const event of actionEvents) replayLog.actions.push(event);
    if (ticks % snapshotIntervalTicks === 0) {
      replayLog.snapshots.push({
        tick: game.tick,
        phase: game.phase,
        winner: game.winner,
        lanes: next.lanes.map((lane) => ({
          laneIndex: lane.laneIndex,
          team: lane.team,
          eliminated: lane.eliminated,
          lives: lane.lives,
          gold: lane.gold,
          income: lane.income,
        })),
      });
    }
    ticks++;
  }

  const finalState = captureStateLite(game);
  const summary = {
    seed: replayLog.seed,
    ticksElapsed: ticks,
    phase: game.phase,
    winnerLaneIndex: game.winner,
    laneSummaries: finalState.lanes.map((lane) => ({
      laneIndex: lane.laneIndex,
      team: lane.team,
      lives: lane.lives,
      income: lane.income,
      gold: lane.gold,
      eliminated: lane.eliminated,
      cumulativeReward: controller.runtimeTracker.cumulativeRewardByLane[lane.laneIndex] || 0,
      invalidActions: controller.runtimeTracker.invalidActionCountByLane[lane.laneIndex] || 0,
    })),
  };

  return { summary, replayLog };
}

function runSelfPlaySeries(options) {
  const opt = options && typeof options === "object" ? options : {};
  const count = Math.max(1, Number(opt.matches) || 1);
  const results = [];
  for (let i = 0; i < count; i++) {
    const seed = `${opt.seed || "selfplay"}:${i}`;
    const out = runHeadlessMatch(Object.assign({}, opt, { seed }));
    results.push(out.summary);
  }
  return results;
}

module.exports = {
  RuntimeTracker,
  BotMatchController,
  createBotController,
  executeBotAction,
  captureStateLite,
  runHeadlessMatch,
  runSelfPlaySeries,
};

