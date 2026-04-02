"use strict";

const DEFAULT_ESCALATION_PER_EXTRA_ROUND = 0.10;
const DEFAULT_INCOME_INTERVAL_TICKS = 240;
const DEFAULT_WAVE_TIMER_TICKS = 2400;
const DEFAULT_TICK_HZ = 20;
const DEFAULT_BARRACKS_SITE_DEFS = Object.freeze([]);

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`waveSystem requires deps.${name}`);
  return fn;
}

function getEscalationPerExtraRound(deps = {}) {
  const value = Number(deps.ESCALATION_PER_EXTRA_ROUND);
  return Number.isFinite(value) ? value : DEFAULT_ESCALATION_PER_EXTRA_ROUND;
}

function getIncomeIntervalTicks(deps = {}) {
  const value = Math.floor(Number(deps.INCOME_INTERVAL_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_INCOME_INTERVAL_TICKS;
}

function getWaveTimerTicks(deps = {}) {
  const value = Math.floor(Number(deps.WAVE_TIMER_TICKS));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_WAVE_TIMER_TICKS;
}

function getTickHz(deps = {}) {
  const value = Math.floor(Number(deps.TICK_HZ));
  return Number.isInteger(value) && value > 0 ? value : DEFAULT_TICK_HZ;
}

function getBarracksSiteDefs(deps = {}) {
  return Array.isArray(deps.BARRACKS_SITE_DEFS)
    ? deps.BARRACKS_SITE_DEFS
    : DEFAULT_BARRACKS_SITE_DEFS;
}

function isScheduledWaveUnit(unit, deps = {}) {
  if (typeof deps.isScheduledWaveUnit === "function")
    return deps.isScheduledWaveUnit(unit);
  return !!(unit && unit.isWaveUnit);
}

function getEffectiveWaveEntrySpeedMult(game, lane, waveDef, deps = {}) {
  if (typeof deps.getEffectiveWaveEntrySpeedMult === "function")
    return deps.getEffectiveWaveEntrySpeedMult(game, lane, waveDef);
  return Math.max(0.01, Number(waveDef && waveDef.speed_mult) || 1);
}

function resolveWaveForRound(game, roundNumber, deps = {}) {
  const cfg = Array.isArray(game && game.waveConfig) ? game.waveConfig : [];
  const round = Math.max(1, Math.floor(Number(roundNumber) || 1));
  if (cfg.length === 0)
    return null;

  let first = null;
  let exact = null;
  let lastAtOrBefore = null;
  let last = null;

  for (const entry of cfg) {
    const waveNumber = Math.floor(Number(entry && entry.wave_number));
    if (!Number.isInteger(waveNumber) || waveNumber <= 0)
      continue;

    if (!first || waveNumber < Number(first.wave_number))
      first = entry;
    if (!last || waveNumber > Number(last.wave_number))
      last = entry;
    if (waveNumber === round)
      exact = entry;
    if (waveNumber <= round && (!lastAtOrBefore || waveNumber > Number(lastAtOrBefore.wave_number)))
      lastAtOrBefore = entry;
  }

  if (exact)
    return exact;
  if (!last)
    return null;
  if (round > Number(last.wave_number)) {
    const extra = round - Number(last.wave_number);
    const esc = 1 + (extra * getEscalationPerExtraRound(deps));
    return {
      ...last,
      hp_mult: Number(last.hp_mult) * esc,
      dmg_mult: Number(last.dmg_mult) * esc,
      speed_mult: Number(last.speed_mult),
    };
  }
  if (lastAtOrBefore)
    return lastAtOrBefore;
  if (first)
    return first;
  return null;
}

function getUpcomingWaveNumber(game) {
  if (!game)
    return 1;

  const currentRound = Math.max(1, Math.floor(Number(game.roundNumber) || 1));
  return game.hasSpawnedWave
    ? currentRound + 1
    : currentRound;
}

function countRemainingWaveMobs(game, deps = {}) {
  if (!game)
    return 0;

  let remaining = 0;
  for (const lane of game.lanes || []) {
    if (!lane || lane.eliminated)
      continue;

    for (const unit of lane.spawnQueue || []) {
      if (unit && isScheduledWaveUnit(unit, deps) && Number(unit.hp) > 0)
        remaining += 1;
    }

    for (const unit of lane.units || []) {
      if (unit && isScheduledWaveUnit(unit, deps) && Number(unit.hp) > 0)
        remaining += 1;
    }
  }

  return remaining;
}

function isCurrentWaveComplete(game, deps = {}) {
  return countRemainingWaveMobs(game, deps) <= 0;
}

function createLaneUpcomingWavePreview(game, lane, waveNumber = null, deps = {}) {
  const upcomingWaveNumber = Number.isInteger(waveNumber) && waveNumber > 0
    ? waveNumber
    : getUpcomingWaveNumber(game);
  const entriesByKey = new Map();

  const addEntry = ({
    source,
    unitType,
    archetypeKey = null,
    presentationKey = null,
    skinKey = null,
    count = 1,
    hpMult = 1,
    dmgMult = 1,
    speedMult = 1,
    sourceLaneIndex = -1,
    sourceBarracksId = null,
    isHero = false,
    heroKey = null,
    heroVisualStyleKey = null,
  }) => {
    if (!unitType)
      return;

    const normalizedCount = Math.max(0, Math.floor(Number(count) || 0));
    if (normalizedCount <= 0)
      return;

    const key = [
      source || "scheduled",
      String(unitType).trim().toLowerCase(),
      String(archetypeKey || "").trim().toLowerCase(),
      String(presentationKey || "").trim().toLowerCase(),
      String(skinKey || "").trim().toLowerCase(),
      Number(hpMult || 1).toFixed(3),
      Number(dmgMult || 1).toFixed(3),
      Number(speedMult || 1).toFixed(3),
      Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1,
      String(sourceBarracksId || "").trim().toLowerCase(),
      isHero ? "hero" : "unit",
      String(heroKey || "").trim().toLowerCase(),
    ].join("|");

    if (entriesByKey.has(key)) {
      entriesByKey.get(key).count += normalizedCount;
      return;
    }

    entriesByKey.set(key, {
      source: source || "scheduled",
      unitType: String(unitType).trim().toLowerCase(),
      archetypeKey: archetypeKey || null,
      presentationKey: presentationKey || null,
      skinKey: skinKey || null,
      count: normalizedCount,
      hpMult: Number(hpMult || 1),
      dmgMult: Number(dmgMult || 1),
      speedMult: Number(speedMult || 1),
      sourceLaneIndex: Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1,
      sourceBarracksId: sourceBarracksId || null,
      isHero: !!isHero,
      heroKey: heroKey || null,
      heroVisualStyleKey: heroVisualStyleKey || null,
    });
  };

  const scheduledWave = resolveWaveForRound(game, upcomingWaveNumber, deps);
  if (scheduledWave) {
    addEntry({
      source: "scheduled",
      unitType: scheduledWave.unit_type,
      archetypeKey: scheduledWave.archetypeKey || null,
      presentationKey: scheduledWave.presentationKey || null,
      skinKey: scheduledWave.skinKey || null,
      count: scheduledWave.spawn_qty,
      hpMult: scheduledWave.hp_mult,
      dmgMult: scheduledWave.dmg_mult,
      speedMult: getEffectiveWaveEntrySpeedMult(game, lane, scheduledWave, deps),
      isHero: !!scheduledWave.isHero,
      heroKey: scheduledWave.heroKey || null,
      heroVisualStyleKey: scheduledWave.heroVisualStyleKey || null,
    });
  }

  const entries = Array.from(entriesByKey.values()).sort((a, b) => {
    if (a.source !== b.source)
      return a.source === "scheduled" ? -1 : 1;
    if (a.count !== b.count)
      return b.count - a.count;
    return String(a.unitType).localeCompare(String(b.unitType));
  });

  return {
    waveNumber: upcomingWaveNumber,
    totalUnits: entries.reduce((sum, entry) => sum + (entry.count || 0), 0),
    entries,
  };
}

function createLaneUpcomingWaveQueue(game, lane, maxCount = 4, deps = {}) {
  const safeCount = Math.max(1, Math.floor(Number(maxCount) || 1));
  const firstWaveNumber = getUpcomingWaveNumber(game);
  const queue = [];

  for (let i = 0; i < safeCount; i += 1) {
    const waveNumber = firstWaveNumber + i;
    queue.push(createLaneUpcomingWavePreview(game, lane, waveNumber, deps));
  }

  return queue;
}

function finalizeCompletedWave(game, deps = {}) {
  if (!game || !game.hasSpawnedWave)
    return;

  const createRoundSnapshotLane = requireDepFunction(deps, "createRoundSnapshotLane");
  for (const lane of game.lanes) {
    if (lane.leakCountThisRound > 0 || lane.lifeLossThisRound > 0) {
      lane.wavesLeaked += 1;
      lane.currentHoldStreak = 0;
      lane.biggestLeakTaken = Math.max(
        lane.biggestLeakTaken || 0,
        lane.lifeLossThisRound || lane.leakCountThisRound || 0
      );
    } else {
      lane.wavesHeld += 1;
      lane.currentHoldStreak = (lane.currentHoldStreak || 0) + 1;
      lane.longestHoldStreak = Math.max(lane.longestHoldStreak || 0, lane.currentHoldStreak);
    }
  }
  game.roundSnapshots.push({
    round: game.roundNumber,
    elapsedSeconds: Math.floor(game.tick / getTickHz(deps)),
    lanes: game.lanes.map((lane) => createRoundSnapshotLane(game, lane)),
  });
  game._pendingEvents.push({
    type: "ml_round_end",
    roundNumber: game.roundNumber,
    teamHp: Object.assign({}, game.teamHp),
  });
}

function resetWaveIntervalState(game) {
  if (!game)
    return;
  for (const lane of game.lanes) {
    lane.leakCountThisRound = 0;
    lane.lifeLossThisRound = 0;
    lane.sendCountThisRound = 0;
    lane.sendSpendThisRound = 0;
    lane.buildSpendThisRound = 0;
  }
}

function spawnScheduledWave(game, deps = {}) {
  if (!game)
    return false;

  const spawnWaveUnit = requireDepFunction(deps, "spawnWaveUnit");
  const nextRoundNumber = getUpcomingWaveNumber(game);
  const waveDef = resolveWaveForRound(game, nextRoundNumber, deps);
  if (!waveDef)
    return false;

  if (game.hasSpawnedWave)
    finalizeCompletedWave(game, deps);

  game.roundNumber = nextRoundNumber;
  resetWaveIntervalState(game);
  const waveSizes = {};
  for (const lane of game.lanes) {
    if (lane.eliminated)
      continue;
    waveSizes[lane.laneIndex] = waveDef.spawn_qty;
    for (let i = 0; i < waveDef.spawn_qty; i += 1)
      spawnWaveUnit(game, lane, waveDef);
  }

  game.hasSpawnedWave = true;
  game.lastWaveSpawnTick = game.tick;
  game._pendingEvents.push({
    type: "ml_wave_start",
    roundNumber: game.roundNumber,
    waveSizes,
  });
  return true;
}

function grantScheduledIncome(game, deps = {}) {
  if (!game)
    return;
  const interval = Math.max(
    1,
    Math.floor(Number(game.incomeIntervalTicks) || getIncomeIntervalTicks(deps))
  );
  if (!Number.isInteger(game.nextIncomeTick) || game.nextIncomeTick <= 0)
    game.nextIncomeTick = game.tick + interval;

  while (game.tick >= game.nextIncomeTick) {
    for (const lane of game.lanes) {
      if (lane && !lane.eliminated)
        lane.gold += lane.income;
    }
    game.nextIncomeTick += interval;
  }
}

function runScheduledBarracksSends(game, deps = {}) {
  if (!game)
    return;

  const ensureBarracksSiteStates = requireDepFunction(deps, "ensureBarracksSiteStates");
  const describeBarracksSite = requireDepFunction(deps, "describeBarracksSite");
  const getBarracksSiteState = requireDepFunction(deps, "getBarracksSiteState");
  const createBarracksSiteSnapshot = requireDepFunction(deps, "createBarracksSiteSnapshot");
  const summarizeBarracksSiteRosterEntries = requireDepFunction(deps, "summarizeBarracksSiteRosterEntries");
  const spawnScheduledBarracksRoster = requireDepFunction(deps, "spawnScheduledBarracksRoster");

  for (const lane of game.lanes) {
    if (!lane || lane.eliminated)
      continue;

    ensureBarracksSiteStates(lane, game);
    for (const siteDef of getBarracksSiteDefs(deps)) {
      const descriptor = describeBarracksSite(game, lane, siteDef.barracksId);
      if (!descriptor || !descriptor.isBuilt || descriptor.destroyed)
        continue;

      const interval = Math.max(1, descriptor.sendIntervalTicks);
      let siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
      if (!siteState)
        continue;

      if (!Number.isInteger(siteState.nextSendTick) || siteState.nextSendTick <= 0)
        siteState.nextSendTick = game.tick + interval;

      while (siteState != null && game.tick >= siteState.nextSendTick) {
        const siteSnapshot = createBarracksSiteSnapshot(game, lane, siteDef.barracksId);
        console.log(
          `[BarracksTrace][ServerTimer] tick=${game.tick} sourceLane=${lane.laneIndex} ` +
          `barracksId='${siteDef.barracksId}' built=${siteSnapshot ? siteSnapshot.isBuilt : false} ` +
          `roster=${summarizeBarracksSiteRosterEntries(siteSnapshot && siteSnapshot.roster)}`
        );
        const spawnedCount = spawnScheduledBarracksRoster(game, lane, siteDef.barracksId);
        console.log(
          `[BarracksTrace][ServerTimer] tick=${game.tick} sourceLane=${lane.laneIndex} ` +
          `barracksId='${siteDef.barracksId}' spawnedCount=${spawnedCount}`
        );
        siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
        if (siteState != null)
          siteState.nextSendTick += interval;
      }
    }
  }
}

function runScheduledBuildingConstruction(game, deps = {}) {
  if (!game)
    return;

  const advanceFortressConstruction = requireDepFunction(deps, "advanceFortressConstruction");
  const advanceBarracksSiteConstruction = requireDepFunction(deps, "advanceBarracksSiteConstruction");
  advanceFortressConstruction(game);
  advanceBarracksSiteConstruction(game);
}

function runScheduledWaves(game, deps = {}) {
  if (!game)
    return;
  const interval = Math.max(
    1,
    Math.floor(Number(game.waveIntervalTicks) || getWaveTimerTicks(deps))
  );
  if (!Number.isInteger(game.nextWaveTick) || game.nextWaveTick <= 0)
    game.nextWaveTick = game.tick + interval;

  while (game.tick >= game.nextWaveTick) {
    spawnScheduledWave(game, deps);
    game.nextWaveTick += interval;
  }
}

function startNextWaveNow(game, deps = {}) {
  if (!game || game.phase !== "playing")
    return false;
  if (!isCurrentWaveComplete(game, deps))
    return false;

  const interval = Math.max(
    1,
    Math.floor(Number(game.waveIntervalTicks) || getWaveTimerTicks(deps))
  );
  const spawned = spawnScheduledWave(game, deps);
  if (spawned)
    game.nextWaveTick = Math.floor(Number(game.tick) || 0) + interval;
  return spawned;
}

module.exports = {
  resolveWaveForRound,
  getUpcomingWaveNumber,
  countRemainingWaveMobs,
  isCurrentWaveComplete,
  createLaneUpcomingWavePreview,
  createLaneUpcomingWaveQueue,
  finalizeCompletedWave,
  resetWaveIntervalState,
  spawnScheduledWave,
  grantScheduledIncome,
  runScheduledBuildingConstruction,
  runScheduledBarracksSends,
  runScheduledWaves,
  startNextWaveNow,
};
