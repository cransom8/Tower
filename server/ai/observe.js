"use strict";

const simMl = require("../sim-multilane");

const MAX_LANES = 4;
const TRACKED_BRANCH_TYPES = Object.freeze([
  "town_core",
  "lumber_mill",
  "blacksmith",
  "archery_tower",
  "wizard_tower",
  "temple",
  "market",
  "stable",
  "workshop",
  "library",
]);
const DEFENSE_PAD_TYPES = new Set(["wall", "gate", "turret", "tower_archer"]);
const ROLE_WEIGHTS = Object.freeze({ melee: 1.1, ranged: 1.0, support: 0.7, hero: 1.5 });
const COMMAND_STATES = simMl.LANE_COMMAND_STATES || Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  RETREAT: "RETREAT",
});

function clamp01(v) {
  if (!Number.isFinite(v)) return 0;
  if (v <= 0) return 0;
  if (v >= 1) return 1;
  return v;
}

function norm(v, max) {
  if (!Number.isFinite(v) || max <= 0) return 0;
  return clamp01(Number(v) / max);
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

function getLeakHistory(runtime, laneIndex) {
  const hist = runtime && runtime.laneLeakHistory && runtime.laneLeakHistory[laneIndex];
  return Array.isArray(hist) ? hist : [];
}

function getLeaksLast(runtime, laneIndex, waves) {
  const hist = getLeakHistory(runtime, laneIndex);
  if (hist.length === 0) return 0;
  const count = Math.max(1, Number(waves) || 3);
  let total = 0;
  for (let i = Math.max(0, hist.length - count); i < hist.length; i++)
    total += Number(hist[i]) || 0;
  return total;
}

function getOpponents(game, laneIndex) {
  const self = game && game.lanes && game.lanes[laneIndex];
  if (!self) return [];
  return (game.lanes || []).filter((lane) => {
    if (!lane || lane.eliminated) return false;
    if (lane.laneIndex === laneIndex) return false;
    return lane.team !== self.team;
  });
}

function chooseObservationTarget(game, laneIndex, runtime) {
  const preferred = runtime && runtime.currentTargetByLane && runtime.currentTargetByLane[laneIndex];
  const opponents = getOpponents(game, laneIndex);
  if (Number.isInteger(preferred)) {
    const lane = opponents.find((entry) => entry.laneIndex === preferred);
    if (lane) return lane.laneIndex;
  }
  if (opponents.length === 0) return null;
  opponents.sort((a, b) => {
    const aScore = (a.lives || 0) - (a.income || 0) * 0.18;
    const bScore = (b.lives || 0) - (b.income || 0) * 0.18;
    return aScore - bScore;
  });
  return opponents[0].laneIndex;
}

function getPadSnapshots(game, lane) {
  if (!lane || !Array.isArray(lane.fortressPads)) return [];
  return lane.fortressPads
    .map((pad) => simMl.createFortressPadSnapshot(game, lane, pad))
    .filter(Boolean);
}

function getBarracksSiteSnapshots(game, lane) {
  return (simMl.BARRACKS_SITE_DEFS || [])
    .map((siteDef) => simMl.createBarracksSiteSnapshot(game, lane, siteDef.barracksId))
    .filter(Boolean);
}

function getPadTierMap(padSnapshots) {
  const tiers = {};
  for (const buildingType of TRACKED_BRANCH_TYPES)
    tiers[buildingType] = 0;
  for (const buildingType of DEFENSE_PAD_TYPES)
    tiers[buildingType] = 0;
  for (const pad of padSnapshots || []) {
    const current = tiers[pad.buildingType] || 0;
    tiers[pad.buildingType] = Math.max(current, Math.max(0, Number(pad.tier) || 0));
  }
  return tiers;
}

function getPadsByType(padSnapshots) {
  const out = {};
  for (const pad of padSnapshots || []) {
    if (!out[pad.buildingType]) out[pad.buildingType] = [];
    out[pad.buildingType].push(pad);
  }
  for (const pads of Object.values(out))
    pads.sort((a, b) => (a.gridY - b.gridY) || (a.gridX - b.gridX) || String(a.padId).localeCompare(String(b.padId)));
  return out;
}

function summarizeRoster(rosterEntries) {
  const byRole = { melee: 0, ranged: 0, support: 0, hero: 0 };
  const unlockedByRole = { melee: 0, ranged: 0, support: 0, hero: 0 };
  let ownedTotal = 0;
  let unlockedTotal = 0;
  let score = 0;

  for (const entry of rosterEntries || []) {
    if (!entry) continue;
    const role = entry.role || "melee";
    const owned = Math.max(0, Math.floor(Number(entry.ownedCount) || 0));
    if (Object.prototype.hasOwnProperty.call(byRole, role)) {
      byRole[role] += owned;
      if (entry.unlocked)
        unlockedByRole[role] += 1;
    }
    ownedTotal += owned;
    if (entry.unlocked)
      unlockedTotal += 1;
    score += owned * (ROLE_WEIGHTS[role] || 0.8) * (1 + (Math.max(1, Number(entry.tier) || 1) - 1) * 0.18);
  }

  return {
    byRole,
    unlockedByRole,
    ownedTotal,
    unlockedTotal,
    score,
  };
}

function estimateLaneThreat(lane, unitDefMap) {
  if (!lane || lane.eliminated || !Array.isArray(lane.units)) return 0;
  const pathLen = Math.max(1, (lane.fullPath && lane.fullPath.length) || (lane.path && lane.path.length) || simMl.GRID_H);
  let threat = 0;

  for (const unit of lane.units) {
    if (!unit || unit.hp <= 0) continue;
    const ownerLaneIndex = Number.isInteger(unit.ownerLaneIndex)
      ? unit.ownerLaneIndex
      : Number.isInteger(unit.ownerLane)
        ? unit.ownerLane
        : Number.isInteger(unit.sourceLaneIndex)
          ? unit.sourceLaneIndex
          : -1;
    if (ownerLaneIndex === lane.laneIndex) continue;

    const base = (unitDefMap && unitDefMap[unit.type]) || simMl.resolveUnitDef(unit.type);
    const baseHp = Math.max(1, Number(base && base.hp) || Math.max(1, Number(unit.maxHp) || 100));
    const hpNorm = Math.max(0.25, Math.min(3, (Number(unit.hp) || baseHp) / baseHp));
    const dps = base
      ? (Number(base.dmg) || 0) / Math.max(1, Number(base.atkCdTicks) || 20)
      : 0.2;
    const progress = clamp01((Number(unit.pathIdx) || 0) / Math.max(1, pathLen - 1));
    threat += hpNorm * (0.45 + progress * 1.35) * (1 + dps * 8);
  }

  return threat;
}

function estimateLaneDefense(game, lane) {
  if (!lane || lane.eliminated) return 0;

  const padSnapshots = getPadSnapshots(game, lane);
  const barracksSites = getBarracksSiteSnapshots(game, lane);
  const rosterEntries = simMl.createBarracksRosterSnapshot(game, lane);
  const heroEntries = simMl.createHeroRosterSnapshot(game, lane);

  let score = 0;
  for (const pad of padSnapshots) {
    const hpRatio = Math.max(0, Number(pad.maxHp) > 0 ? Number(pad.hp) / Number(pad.maxHp) : 0);
    const tier = Math.max(0, Number(pad.tier) || 0);
    let weight = 0.2;
    if (pad.buildingType === "town_core") weight = 0.55;
    else if (pad.buildingType === "gate") weight = 1.4;
    else if (pad.buildingType === "wall") weight = 1.2;
    else if (pad.buildingType === "turret" || pad.buildingType === "tower_archer") weight = 1.1;
    score += Math.max(0, hpRatio) * Math.max(0, tier) * weight;
  }

  for (const site of barracksSites) {
    if (!site.isBuilt) continue;
    score += 1 + Math.max(0, Number(site.level) || 1) * 0.75;
  }

  const rosterSummary = summarizeRoster(rosterEntries);
  score += rosterSummary.score;

  for (const hero of heroEntries) {
    if (!hero) continue;
    if (hero.state === "ready") score += 1.8;
    if (hero.state === "active") score += 2.2;
  }

  const commandState = typeof simMl.getLaneCommandState === "function"
    ? simMl.getLaneCommandState(lane)
    : (lane.commandState || COMMAND_STATES.ATTACK);
  if (commandState === COMMAND_STATES.DEFEND) score += 1.2;
  if (commandState === COMMAND_STATES.RETREAT) score -= 0.5;

  return score;
}

function summarizeLaneForAi(game, laneIndex, runtime, unitDefMap) {
  const lane = game && game.lanes && game.lanes[laneIndex];
  if (!lane) return null;

  const padSnapshots = getPadSnapshots(game, lane);
  const padsByType = getPadsByType(padSnapshots);
  const barracksSites = getBarracksSiteSnapshots(game, lane);
  const barracksRoster = simMl.createBarracksRosterSnapshot(game, lane);
  const heroRoster = simMl.createHeroRosterSnapshot(game, lane);
  const padTiers = getPadTierMap(padSnapshots);
  const rosterSummary = summarizeRoster(barracksRoster);
  const corePad = padSnapshots.find((pad) => pad.buildingType === "town_core") || null;
  const coreHp = corePad ? Math.max(0, Number(corePad.hp) || 0) : 0;
  const coreMaxHp = corePad ? Math.max(1, Number(corePad.maxHp) || 1) : 1;
  const builtBarracksSites = barracksSites.filter((site) => site && site.isBuilt);
  const totalBarracksLevels = builtBarracksSites.reduce((sum, site) => sum + Math.max(0, Number(site.level) || 0), 0);
  const lowestSendTimerTicks = builtBarracksSites.reduce((best, site) => Math.min(best, Number(site.sendTimerTicksRemaining) || 0), Infinity);
  const heroReady = heroRoster.filter((hero) => hero && hero.canSummon).length;
  const heroActive = heroRoster.filter((hero) => hero && hero.state === "active").length;
  const commandState = typeof simMl.getLaneCommandState === "function"
    ? simMl.getLaneCommandState(lane)
    : (lane.commandState || COMMAND_STATES.ATTACK);
  const currentTargetLaneIndex = Number.isInteger(lane.commandTargetLaneIndex) ? lane.commandTargetLaneIndex : laneIndex;
  const threat = estimateLaneThreat(lane, unitDefMap);
  const defense = estimateLaneDefense(game, lane);
  const recentLeaks = getLeaksLast(runtime, laneIndex, 3);
  const recentLifeLoss = Number(runtime && runtime.recentLifeLossByLane && runtime.recentLifeLossByLane[laneIndex]) || 0;
  const laneControlledUnits = Array.isArray(lane.units)
    ? lane.units.filter((unit) => unit && unit.hp > 0 && Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex === laneIndex).length
    : 0;
  const hostileUnits = Array.isArray(lane.units)
    ? lane.units.filter((unit) => unit && unit.hp > 0 && (Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex !== laneIndex : true)).length
    : 0;
  const frontDefensePads = ["gate", "wall", "turret", "tower_archer"]
    .flatMap((buildingType) => padsByType[buildingType] || []);

  return {
    laneIndex,
    team: lane.team,
    eliminated: !!lane.eliminated,
    gold: Math.max(0, Number(lane.gold) || 0),
    income: Math.max(0, Number(lane.income) || 0),
    lives: Math.max(0, Number(lane.lives) || 0),
    coreHp,
    coreMaxHp,
    coreHpRatio: coreHp / coreMaxHp,
    townCoreTier: Math.max(1, Number(padTiers.town_core) || 1),
    commandState,
    commandTargetLaneIndex: currentTargetLaneIndex,
    commandAnchorProgress: clamp01(Number(lane.commandAnchorProgress) || 0),
    threat,
    defense,
    pressureGap: threat - defense,
    recentLeaks,
    recentLifeLoss,
    laneControlledUnits,
    hostileUnits,
    builtBarracksSites: builtBarracksSites.length,
    totalBarracksLevels,
    lowestSendTimerTicks: Number.isFinite(lowestSendTimerTicks) ? lowestSendTimerTicks : 0,
    padSnapshots,
    padsByType,
    padTiers,
    barracksSites,
    barracksRoster,
    barracksRosterSummary: rosterSummary,
    heroRoster,
    heroReady,
    heroActive,
    frontDefensePads,
  };
}

function buildLastSendVector() {
  return [];
}

function buildObservation(game, laneIndex, runtime, unitDefMap) {
  const laneSummary = summarizeLaneForAi(game, laneIndex, runtime, unitDefMap);
  if (!laneSummary) {
    return {
      vector: [],
      targetLaneIndex: null,
      named: {},
      laneSummary: null,
      targetSummary: null,
    };
  }

  const targetLaneIndex = chooseObservationTarget(game, laneIndex, runtime);
  const targetSummary = Number.isInteger(targetLaneIndex)
    ? summarizeLaneForAi(game, targetLaneIndex, runtime, unitDefMap)
    : null;
  const { waveNumber, timeInWave } = getWaveAndTime(game, runtime);

  const named = {
    myGold: norm(laneSummary.gold, 600),
    myIncome: norm(laneSummary.income, 350),
    myCoreHpRatio: clamp01(laneSummary.coreHpRatio),
    myTownCoreTier: norm(laneSummary.townCoreTier, 4),
    myThreat: norm(laneSummary.threat, 40),
    myDefense: norm(laneSummary.defense, 40),
    myPressureGap: norm(Math.max(0, laneSummary.pressureGap), 20),
    myRecentLeaks: norm(laneSummary.recentLeaks, 20),
    myRecentLifeLoss: norm(laneSummary.recentLifeLoss, 8),
    builtBarracksSites: norm(laneSummary.builtBarracksSites, 3),
    totalBarracksLevels: norm(laneSummary.totalBarracksLevels, 9),
    meleeRoster: norm(laneSummary.barracksRosterSummary.byRole.melee, 24),
    rangedRoster: norm(laneSummary.barracksRosterSummary.byRole.ranged, 24),
    supportRoster: norm(laneSummary.barracksRosterSummary.byRole.support, 18),
    heroReady: norm(laneSummary.heroReady, 3),
    commandState: [
      laneSummary.commandState === COMMAND_STATES.ATTACK ? 1 : 0,
      laneSummary.commandState === COMMAND_STATES.DEFEND ? 1 : 0,
      laneSummary.commandState === COMMAND_STATES.RETREAT ? 1 : 0,
    ],
    waveNumber: norm(waveNumber, 30),
    timeInWave: clamp01(timeInWave),
    targetLane: norm(Number.isInteger(targetLaneIndex) ? targetLaneIndex : 0, MAX_LANES - 1),
    targetCoreHpRatio: targetSummary ? clamp01(targetSummary.coreHpRatio) : 0,
    targetThreat: targetSummary ? norm(targetSummary.threat, 40) : 0,
    targetDefense: targetSummary ? norm(targetSummary.defense, 40) : 0,
    targetBarracksSites: targetSummary ? norm(targetSummary.builtBarracksSites, 3) : 0,
    targetMeleeRoster: targetSummary ? norm(targetSummary.barracksRosterSummary.byRole.melee, 24) : 0,
    targetRangedRoster: targetSummary ? norm(targetSummary.barracksRosterSummary.byRole.ranged, 24) : 0,
    targetSupportRoster: targetSummary ? norm(targetSummary.barracksRosterSummary.byRole.support, 18) : 0,
    lastEnemySendSummary: buildLastSendVector(),
  };

  const vector = [
    named.myGold,
    named.myIncome,
    named.myCoreHpRatio,
    named.myTownCoreTier,
    named.myThreat,
    named.myDefense,
    named.myPressureGap,
    named.myRecentLeaks,
    named.builtBarracksSites,
    named.totalBarracksLevels,
    named.meleeRoster,
    named.rangedRoster,
    named.supportRoster,
    named.heroReady,
    ...named.commandState,
    named.waveNumber,
    named.timeInWave,
    named.targetLane,
    named.targetCoreHpRatio,
    named.targetThreat,
    named.targetDefense,
    named.targetBarracksSites,
    named.targetMeleeRoster,
    named.targetRangedRoster,
    named.targetSupportRoster,
  ];

  return {
    vector,
    targetLaneIndex,
    named,
    laneSummary,
    targetSummary,
  };
}

module.exports = {
  TRACKED_BRANCH_TYPES,
  DEFENSE_PAD_TYPES,
  getOpponents,
  chooseObservationTarget,
  summarizeLaneForAi,
  estimateLaneThreat,
  estimateLaneDefense,
  buildObservation,
};
