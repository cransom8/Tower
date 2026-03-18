"use strict";

const DEFAULT_REWARD_CONFIG = Object.freeze({
  winReward: 1,
  lossReward: -1,
  leakDamageScale: 0.05,
  invalidActionPenalty: 0.03,
  floatingGoldThreshold: 120,
  floatingGoldScale: 0.0008,
  incomeGrowthScale: 0.02,
});

function getWinnerTeam(state) {
  if (!state || state.winner === null || state.winner === undefined) return null;
  const lane = state.lanes && state.lanes[state.winner];
  return lane ? lane.team : null;
}

function computeReward(prevState, nextState, events, laneIndex, config) {
  const cfg = Object.assign({}, DEFAULT_REWARD_CONFIG, config || {});
  const prevLane = prevState && prevState.lanes && prevState.lanes[laneIndex];
  const nextLane = nextState && nextState.lanes && nextState.lanes[laneIndex];
  if (!prevLane || !nextLane) return 0;

  let reward = 0;
  if (nextState && nextState.phase === "ended") {
    const winnerTeam = getWinnerTeam(nextState);
    if (winnerTeam && winnerTeam === nextLane.team) reward += cfg.winReward;
    else if (winnerTeam) reward += cfg.lossReward;
  }

  const leakDealt = Number(events && events.leakDamageDealt) || 0;
  const leakTaken = Number(events && events.leakDamageTaken) || 0;
  const invalidActions = Number(events && events.invalidActionCount) || 0;

  reward += leakDealt * cfg.leakDamageScale;
  reward -= leakTaken * cfg.leakDamageScale;
  reward -= invalidActions * cfg.invalidActionPenalty;

  const floatingGold = Math.max(0, (Number(nextLane.gold) || 0) - cfg.floatingGoldThreshold);
  reward -= floatingGold * cfg.floatingGoldScale;

  const incomeGrowth = (Number(nextLane.income) || 0) - (Number(prevLane.income) || 0);
  reward += incomeGrowth * cfg.incomeGrowthScale;

  return Number(reward.toFixed(6));
}

module.exports = {
  DEFAULT_REWARD_CONFIG,
  computeReward,
};

