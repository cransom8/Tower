"use strict";

const simMl = require("../server/sim-multilane");

const UNIT_TYPES = Object.freeze(Object.keys(simMl.UNIT_DEFS));
const TOWER_TYPES = Object.freeze(Object.keys(simMl.TOWER_DEFS));
const COUNT_BUCKETS = Object.freeze([1, 3, 5, 10]);
const MAX_LANES = 4;

function clamp01(v) {
  if (!Number.isFinite(v)) return 0;
  if (v <= 0) return 0;
  if (v >= 1) return 1;
  return v;
}

function norm(v, max) {
  if (max <= 0) return 0;
  return clamp01(Number(v || 0) / max);
}

function getWaveTickInterval(runtime) {
  const n = Number(runtime && runtime.waveTickInterval);
  return Number.isFinite(n) && n > 0 ? Math.floor(n) : 240;
}

function getWaveAndTime(game, runtime) {
  const waveTicks = getWaveTickInterval(runtime);
  const tick = Number(game && game.tick) || 0;
  const waveNumber = Math.floor(tick / waveTicks) + 1;
  const timeInWave = (tick % waveTicks) / waveTicks;
  return { waveNumber, timeInWave };
}

function summarizeTowerStats(lane) {
  const counts = Object.fromEntries(TOWER_TYPES.map((t) => [t, 0]));
  const levelSums = Object.fromEntries(TOWER_TYPES.map((t) => [t, 0]));
  if (!lane || !lane.grid) {
    return { counts, avgLevels: Object.fromEntries(TOWER_TYPES.map((t) => [t, 0])) };
  }
  for (let x = 0; x < lane.grid.length; x++) {
    const col = lane.grid[x] || [];
    for (let y = 0; y < col.length; y++) {
      const tile = col[y];
      if (!tile || tile.type !== "tower" || !tile.towerType) continue;
      if (!(tile.towerType in counts)) continue;
      counts[tile.towerType] += 1;
      levelSums[tile.towerType] += Number(tile.towerLevel) || 1;
    }
  }
  const avgLevels = {};
  for (const t of TOWER_TYPES) {
    avgLevels[t] = counts[t] > 0 ? levelSums[t] / counts[t] : 0;
  }
  return { counts, avgLevels };
}

function estimateLaneThreat(lane) {
  if (!lane || lane.eliminated) return 0;
  const pathLen = Math.max(1, (lane.path && lane.path.length) || 1);
  let threat = 0;
  for (const u of lane.units || []) {
    if (u.ownerLane === lane.laneIndex || (u.hp || 0) <= 0) continue;
    const base = simMl.UNIT_DEFS[u.type];
    const maxHp = base ? base.hp : 100;
    const hpNorm = Math.max(0.2, Math.min(3, (Number(u.hp) || 0) / maxHp));
    const progress = Math.max(0, Math.min(1, (Number(u.pathIdx) || 0) / Math.max(1, pathLen - 1)));
    threat += hpNorm * (0.25 + 0.75 * progress);
  }
  return threat;
}

function estimateLaneDefense(lane) {
  if (!lane || lane.eliminated || !lane.grid) return 0;
  let defense = 0;
  for (let x = 0; x < lane.grid.length; x++) {
    const col = lane.grid[x] || [];
    for (let y = 0; y < col.length; y++) {
      const tile = col[y];
      if (!tile || tile.type !== "tower" || !tile.towerType) continue;
      const def = simMl.TOWER_DEFS[tile.towerType];
      if (!def) continue;
      const lvl = Math.max(1, Number(tile.towerLevel) || 1);
      const dps = (def.dmg * (1 + 0.12 * (lvl - 1))) / Math.max(1, def.atkCdTicks);
      defense += dps;
    }
  }
  return defense;
}

function getLeakHistory(runtime, laneIndex) {
  if (!runtime || !runtime.laneLeakHistory) return [];
  const arr = runtime.laneLeakHistory[laneIndex];
  return Array.isArray(arr) ? arr : [];
}

function getLeaksLast3(runtime, laneIndex) {
  const hist = getLeakHistory(runtime, laneIndex);
  if (hist.length === 0) return 0;
  const start = Math.max(0, hist.length - 3);
  let total = 0;
  for (let i = start; i < hist.length; i++) total += Number(hist[i]) || 0;
  return total;
}

function getLaneLeakRate(runtime, laneIndex) {
  const hist = getLeakHistory(runtime, laneIndex);
  if (hist.length === 0) return 0;
  const waves = Math.min(6, hist.length);
  let leaks = 0;
  for (let i = hist.length - waves; i < hist.length; i++) leaks += Number(hist[i]) || 0;
  return leaks / waves;
}

function getOpponents(game, laneIndex) {
  if (!game || !Array.isArray(game.lanes)) return [];
  const self = game.lanes[laneIndex];
  if (!self) return [];
  return game.lanes.filter((lane) => {
    if (!lane || lane.eliminated) return false;
    if (lane.laneIndex === laneIndex) return false;
    return lane.team !== self.team;
  });
}

function chooseObservationTarget(game, laneIndex, runtime) {
  const preferred = runtime && runtime.currentTargetByLane && runtime.currentTargetByLane[laneIndex];
  const opponents = getOpponents(game, laneIndex);
  if (Number.isInteger(preferred)) {
    const lane = opponents.find((l) => l.laneIndex === preferred);
    if (lane) return lane.laneIndex;
  }
  if (opponents.length === 0) return null;
  opponents.sort((a, b) => {
    const aScore = (a.lives || 0) - (a.income || 0) * 0.2;
    const bScore = (b.lives || 0) - (b.income || 0) * 0.2;
    return aScore - bScore;
  });
  return opponents[0].laneIndex;
}

function getLastEnemySendSummary(runtime, myLaneIndex, targetLaneIndex) {
  const incoming = runtime && runtime.incomingSendHistoryByLane && runtime.incomingSendHistoryByLane[myLaneIndex];
  const list = Array.isArray(incoming) ? incoming : [];
  if (list.length === 0) return null;
  if (Number.isInteger(targetLaneIndex)) {
    for (let i = list.length - 1; i >= 0; i--) {
      if (list[i].sourceLaneIndex === targetLaneIndex) return list[i];
    }
  }
  return list[list.length - 1];
}

function buildLastSendVector(summary) {
  const unitOneHot = UNIT_TYPES.map((u) => (summary && summary.unitType === u ? 1 : 0));
  const bucketOneHot = COUNT_BUCKETS.map((b) => (summary && Number(summary.countBucket) === b ? 1 : 0));
  return unitOneHot.concat(bucketOneHot);
}

function buildObservation(game, laneIndex, runtime) {
  const lane = game && game.lanes && game.lanes[laneIndex];
  if (!lane) {
    return {
      vector: [],
      targetLaneIndex: null,
      named: {},
    };
  }

  const { waveNumber, timeInWave } = getWaveAndTime(game, runtime);
  const myTowerStats = summarizeTowerStats(lane);
  const targetLaneIndex = chooseObservationTarget(game, laneIndex, runtime);
  const targetLane = Number.isInteger(targetLaneIndex) ? game.lanes[targetLaneIndex] : null;
  const targetTowerStats = summarizeTowerStats(targetLane);
  const opponents = getOpponents(game, laneIndex);

  const myLeaksLast3 = getLeaksLast3(runtime, laneIndex);
  const enemyLeaksLast3 = opponents.length > 0
    ? opponents.reduce((sum, o) => sum + getLeaksLast3(runtime, o.laneIndex), 0) / opponents.length
    : 0;

  const laneThreatEstimate = new Array(MAX_LANES).fill(0);
  const laneDefenseEstimate = new Array(MAX_LANES).fill(0);
  const laneLeakRate = new Array(MAX_LANES).fill(0);
  for (const l of game.lanes || []) {
    if (!l || !Number.isInteger(l.laneIndex) || l.laneIndex < 0 || l.laneIndex >= MAX_LANES) continue;
    laneThreatEstimate[l.laneIndex] = norm(estimateLaneThreat(l), 18);
    laneDefenseEstimate[l.laneIndex] = norm(estimateLaneDefense(l), 22);
    laneLeakRate[l.laneIndex] = norm(getLaneLeakRate(runtime, l.laneIndex), 6);
  }

  const myTowerCountsByType = TOWER_TYPES.map((t) => norm(myTowerStats.counts[t], 18));
  const avgTowerLevelByType = TOWER_TYPES.map((t) => norm(myTowerStats.avgLevels[t], 10));

  const targetTowerTotal = TOWER_TYPES.reduce((sum, t) => sum + targetTowerStats.counts[t], 0);
  const enemyTowerMixSummary = TOWER_TYPES.map((t) => {
    if (targetTowerTotal <= 0) return 0;
    return clamp01(targetTowerStats.counts[t] / targetTowerTotal);
  });

  const lastEnemySend = getLastEnemySendSummary(runtime, laneIndex, targetLaneIndex);
  const lastEnemySendSummary = buildLastSendVector(lastEnemySend);

  const named = {
    myGold: norm(lane.gold, 500),
    myIncome: norm(lane.income, 350),
    waveNumber: norm(waveNumber, 100),
    timeInWave: clamp01(timeInWave),
    myLeaksLast3Waves: norm(myLeaksLast3, 20),
    enemyLeaksLast3Waves: norm(enemyLeaksLast3, 20),
    myTowerCountsByType,
    avgTowerLevelByType,
    laneThreatEstimate,
    laneDefenseEstimate,
    laneLeakRate,
    enemyEstimatedIncome: norm(targetLane ? targetLane.income : 0, 350),
    enemyTowerMixSummary,
    lastEnemySendSummary,
    targetLane: norm(Number.isInteger(targetLaneIndex) ? targetLaneIndex : 0, MAX_LANES - 1),
  };

  const vector = [
    named.myGold,
    named.myIncome,
    named.waveNumber,
    named.timeInWave,
    named.myLeaksLast3Waves,
    named.enemyLeaksLast3Waves,
    ...named.myTowerCountsByType,
    ...named.avgTowerLevelByType,
    ...named.laneThreatEstimate,
    ...named.laneDefenseEstimate,
    ...named.laneLeakRate,
    named.enemyEstimatedIncome,
    ...named.enemyTowerMixSummary,
    ...named.lastEnemySendSummary,
    named.targetLane,
  ];

  return { vector, targetLaneIndex, named };
}

module.exports = {
  UNIT_TYPES,
  TOWER_TYPES,
  COUNT_BUCKETS,
  buildObservation,
  estimateLaneThreat,
  estimateLaneDefense,
  summarizeTowerStats,
};

