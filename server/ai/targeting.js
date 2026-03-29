"use strict";

const { summarizeLaneForAi, getOpponents } = require("./observe");

function getRecentLeaks(runtime, laneIndex, waves) {
  const arr = runtime && runtime.laneLeakHistory && runtime.laneLeakHistory[laneIndex];
  if (!Array.isArray(arr) || arr.length === 0) return 0;
  const n = Math.max(1, Number(waves) || 3);
  let total = 0;
  for (let i = Math.max(0, arr.length - n); i < arr.length; i++)
    total += Number(arr[i]) || 0;
  return total;
}

function scoreOpponent(game, sourceLaneIndex, targetLaneIndex, runtime, unitDefMap) {
  const source = summarizeLaneForAi(game, sourceLaneIndex, runtime, unitDefMap);
  const target = summarizeLaneForAi(game, targetLaneIndex, runtime, unitDefMap);
  if (!source || !target || target.eliminated || source.team === target.team) return -Infinity;

  const weakestCoreScore = Math.max(0, 1 - target.coreHpRatio);
  const highIncomeScore = Math.min(1, target.income / 220);
  const recentLeakScore = Math.min(1, getRecentLeaks(runtime, targetLaneIndex, 3) / 8);
  const vulnerability = Math.max(0, (target.threat - target.defense) / Math.max(1, target.threat + target.defense));
  const structureLightness = Math.max(0, 1 - Math.min(1, target.frontDefensePads.length / 12));
  const barracksWeakness = Math.max(0, 1 - Math.min(1, target.builtBarracksSites / 3));

  return (
    weakestCoreScore * 0.42 +
    highIncomeScore * 0.24 +
    recentLeakScore * 0.18 +
    vulnerability * 0.22 +
    structureLightness * 0.18 +
    barracksWeakness * 0.12
  );
}

function chooseTargetOpponent(game, laneIndex, runtime, memory, rng, unitDefMap) {
  const opponents = getOpponents(game, laneIndex);
  if (opponents.length === 0) return null;
  const currentTick = Number(game && game.tick) || 0;
  const holdUntilTick = Number(memory && memory.targetHoldUntilTick) || 0;
  const currentTarget = Number.isInteger(memory && memory.currentTargetLaneIndex)
    ? memory.currentTargetLaneIndex
    : null;

  if (currentTarget !== null && currentTick < holdUntilTick) {
    const lane = opponents.find((entry) => entry.laneIndex === currentTarget);
    if (lane) return currentTarget;
  }

  const scoredOpponents = opponents.map((opp) => ({
    laneIndex: opp.laneIndex,
    score: scoreOpponent(game, laneIndex, opp.laneIndex, runtime, unitDefMap),
  }));
  let best = null;
  for (const entry of scoredOpponents) {
    if (!best || entry.score > best.score)
      best = entry;
  }
  if (!best)
    return opponents[0].laneIndex;

  const currentScore = currentTarget === null
    ? -Infinity
    : (scoredOpponents.find((entry) => entry.laneIndex === currentTarget) || {}).score;
  if (currentTarget !== null && Number.isFinite(currentScore)) {
    if (currentTick < holdUntilTick || best.score <= currentScore + 0.08)
      return currentTarget;
  }

  if (rng && typeof rng.nextInt === "function") {
    const tieCandidates = scoredOpponents.filter((entry) => Math.abs(entry.score - best.score) < 0.03);
    if (tieCandidates.length > 1)
      best = tieCandidates[rng.nextInt(0, tieCandidates.length - 1)];
  }

  if (memory) {
    memory.currentTargetLaneIndex = best.laneIndex;
    memory.targetHoldUntilTick = currentTick + 120 + (rng && typeof rng.nextInt === "function" ? rng.nextInt(0, 40) : 0);
  }
  return best.laneIndex;
}

function isLaneOverDefended(game, laneIndex, runtime, unitDefMap) {
  const summary = summarizeLaneForAi(game, laneIndex, runtime, unitDefMap);
  if (!summary) return false;
  return summary.defense > Math.max(6, summary.threat * 1.9);
}

module.exports = {
  getOpponents,
  scoreOpponent,
  chooseTargetOpponent,
  isLaneOverDefended,
};
