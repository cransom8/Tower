"use strict";

const { estimateLaneDefense, estimateLaneThreat } = require("./observe");

function getOpponents(game, laneIndex) {
  const self = game && game.lanes && game.lanes[laneIndex];
  if (!self) return [];
  return (game.lanes || []).filter((lane) => {
    if (!lane || lane.eliminated) return false;
    if (lane.laneIndex === laneIndex) return false;
    return lane.team !== self.team;
  });
}

function getTeammates(game, laneIndex) {
  const self = game && game.lanes && game.lanes[laneIndex];
  if (!self) return [];
  return (game.lanes || []).filter((lane) => {
    if (!lane || lane.eliminated) return false;
    if (lane.laneIndex === laneIndex) return false;
    return lane.team === self.team;
  });
}

function getRecentLeaks(runtime, laneIndex, waves) {
  const arr = runtime && runtime.laneLeakHistory && runtime.laneLeakHistory[laneIndex];
  if (!Array.isArray(arr) || arr.length === 0) return 0;
  const n = Math.max(1, Number(waves) || 3);
  let total = 0;
  for (let i = Math.max(0, arr.length - n); i < arr.length; i++) {
    total += Number(arr[i]) || 0;
  }
  return total;
}

function scoreOpponent(game, sourceLaneIndex, targetLaneIndex, runtime) {
  const source = game && game.lanes && game.lanes[sourceLaneIndex];
  const target = game && game.lanes && game.lanes[targetLaneIndex];
  if (!source || !target || target.eliminated || source.team === target.team) return -Infinity;

  const weakestScore = Math.max(0, (20 - (target.lives || 0)) / 20);
  const highIncomeScore = Math.min(1, (target.income || 0) / 220);
  const recentLeakScore = Math.min(1, getRecentLeaks(runtime, targetLaneIndex, 3) / 8);

  const defense = estimateLaneDefense(target);
  const threat = estimateLaneThreat(target);
  const defensePressure = threat <= 0 ? 0 : Math.max(0, (defense - threat) / Math.max(1, defense));
  const vulnerability = Math.max(0, (threat - defense) / Math.max(1, threat + defense));

  return (
    weakestScore * 0.4 +
    highIncomeScore * 0.28 +
    recentLeakScore * 0.22 +
    vulnerability * 0.25 -
    defensePressure * 0.2
  );
}

function chooseTargetOpponent(game, laneIndex, runtime, memory, rng) {
  const opponents = getOpponents(game, laneIndex);
  if (opponents.length === 0) return null;
  const currentTick = Number(game && game.tick) || 0;
  const holdUntilTick = Number(memory && memory.targetHoldUntilTick) || 0;
  const currentTarget = Number.isInteger(memory && memory.currentTargetLaneIndex)
    ? memory.currentTargetLaneIndex
    : null;

  if (currentTarget !== null && currentTick < holdUntilTick) {
    const lane = opponents.find((o) => o.laneIndex === currentTarget);
    if (lane) return currentTarget;
  }

  let best = null;
  for (const opp of opponents) {
    const score = scoreOpponent(game, laneIndex, opp.laneIndex, runtime);
    if (!best || score > best.score) best = { laneIndex: opp.laneIndex, score };
  }
  if (!best) return opponents[0].laneIndex;

  // Small deterministic noise avoids identical tie-lock in mirrored states.
  if (rng && typeof rng.next === "function") {
    const tieCandidates = opponents
      .map((opp) => ({ laneIndex: opp.laneIndex, score: scoreOpponent(game, laneIndex, opp.laneIndex, runtime) }))
      .filter((s) => Math.abs(s.score - best.score) < 0.03);
    if (tieCandidates.length > 1) {
      const idx = rng.nextInt(0, tieCandidates.length - 1);
      best = tieCandidates[idx];
    }
  }

  if (memory) {
    memory.currentTargetLaneIndex = best.laneIndex;
    memory.targetHoldUntilTick = currentTick + 24;
  }
  return best.laneIndex;
}

function isLaneOverDefended(game, laneIndex) {
  const lane = game && game.lanes && game.lanes[laneIndex];
  if (!lane) return false;
  const defense = estimateLaneDefense(lane);
  const threat = estimateLaneThreat(lane);
  return defense > Math.max(8, threat * 2.2);
}

function buildTeamPlanKey(game, laneIndex) {
  const lane = game && game.lanes && game.lanes[laneIndex];
  if (!lane) return `lane:${laneIndex}`;
  const teammates = getTeammates(game, laneIndex)
    .map((l) => l.laneIndex)
    .concat([laneIndex])
    .sort((a, b) => a - b);
  return `team:${teammates.join("-")}`;
}

function planTeamSpike(game, laneIndex, runtime, rng, preferredTargetLane) {
  const teammates = getTeammates(game, laneIndex);
  if (teammates.length === 0) return null;
  if (!runtime.teamPlans) runtime.teamPlans = {};
  const key = buildTeamPlanKey(game, laneIndex);
  const nowTick = Number(game.tick) || 0;
  const existing = runtime.teamPlans[key];
  if (existing && existing.spikeTick >= nowTick - 2) return existing;

  const opponents = getOpponents(game, laneIndex);
  let targetLaneIndex = preferredTargetLane;
  if (!Number.isInteger(targetLaneIndex) || !opponents.some((o) => o.laneIndex === targetLaneIndex)) {
    targetLaneIndex = opponents.length > 0 ? opponents[0].laneIndex : null;
  }

  const delay = rng ? rng.nextInt(26, 48) : 36;
  const bucketChoices = [3, 5, 10];
  const bucket = rng ? bucketChoices[rng.nextInt(0, bucketChoices.length - 1)] : 5;
  const plan = {
    targetLaneIndex,
    spikeTick: nowTick + delay,
    countBucket: bucket,
    createdByLane: laneIndex,
  };
  runtime.teamPlans[key] = plan;
  return plan;
}

function getSpikePlanForLane(game, laneIndex, runtime) {
  if (!runtime || !runtime.teamPlans) return null;
  return runtime.teamPlans[buildTeamPlanKey(game, laneIndex)] || null;
}

function shouldSyncSpikeNow(game, laneIndex, runtime, horizonTicks) {
  const plan = getSpikePlanForLane(game, laneIndex, runtime);
  if (!plan || !Number.isInteger(plan.spikeTick)) return null;
  const tick = Number(game && game.tick) || 0;
  const h = Math.max(1, Number(horizonTicks) || 4);
  if (Math.abs(plan.spikeTick - tick) <= h) return plan;
  return null;
}

module.exports = {
  getOpponents,
  getTeammates,
  scoreOpponent,
  chooseTargetOpponent,
  isLaneOverDefended,
  planTeamSpike,
  getSpikePlanForLane,
  shouldSyncSpikeNow,
};

