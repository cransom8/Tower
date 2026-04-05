"use strict";

const barracksSystem = require("./multilane/barracksSystem");
const fortressSystem = require("./multilane/fortressSystem");
const { getUnitType } = require("../unitTypes");
const { resolveFortDisplayName, titleCaseFromKey } = require("./fortUnitCatalog");

const SCHEMA_VERSION = 1;

function n(value, fallback = 0) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function i(value, fallback = 0) {
  const numeric = Math.floor(Number(value));
  return Number.isInteger(numeric) ? numeric : fallback;
}

function round(value, digits = 2) {
  const numeric = Number(value);
  if (!Number.isFinite(numeric))
    return 0;
  const factor = Math.pow(10, digits);
  return Math.round(numeric * factor) / factor;
}

function sum(values) {
  return (Array.isArray(values) ? values : []).reduce((total, value) => total + (Number(value) || 0), 0);
}

function normalizeKey(value) {
  return String(value || "").trim().toLowerCase();
}

function familyLabel(category) {
  switch (normalizeKey(category)) {
    case "shield": return "Shield";
    case "infantry": return "Infantry";
    case "spear": return "Spear";
    case "archer": return "Archers";
    case "mage": return "Mages";
    case "healer": return "Support";
    case "cavalry": return "Cavalry";
    case "economy": return "Economy";
    case "hero": return "Heroes";
    default: return "Other";
  }
}

function buildingLabel(buildingType) {
  const normalized = normalizeKey(buildingType);
  if (!normalized)
    return "Unknown";
  return fortressSystem.getBuildingBranchLabel(normalized)
    || fortressSystem.getBuildingDisplayName(normalized)
    || titleCaseFromKey(normalized);
}

function modeLabel(mode) {
  switch (normalizeKey(mode)) {
    case "multilane": return "Survival";
    case "classic": return "Classic Duel";
    default: return titleCaseFromKey(String(mode || "match"));
  }
}

function resolveUnitDisplayName(unitType) {
  const key = String(unitType || "").trim();
  if (!key)
    return "Unknown";
  const unitDef = getUnitType(key);
  if (unitDef && unitDef.name)
    return unitDef.name;
  return resolveFortDisplayName(key, undefined, titleCaseFromKey(key));
}

function shareRows(entries) {
  const rows = Array.isArray(entries) ? entries.slice() : [];
  const total = sum(rows.map((entry) => entry && entry.value));
  return rows
    .filter((entry) => entry && n(entry.value, 0) > 0)
    .sort((left, right) => n(right.value, 0) - n(left.value, 0))
    .map((entry) => ({
      label: entry.label,
      value: round(n(entry.value, 0), 2),
      sharePercent: total > 0 ? round((n(entry.value, 0) / total) * 100, 1) : 0,
    }));
}

function threatStateLabel(ratio, hadBreach, coreDamageTaken) {
  if (coreDamageTaken > 0 || (hadBreach && ratio < 0.9))
    return "Struggling";
  if (ratio < 0.95)
    return "Holding the Line";
  if (ratio < 1.2)
    return "Stable";
  return "Dominating";
}

function styleLabel(spending, buildingPaths) {
  const buckets = [
    { label: "Unit-Heavy", value: n(spending && spending.unitSpending, 0) },
    { label: "Upgrade-Heavy", value: n(spending && spending.upgradeSpending, 0) },
    { label: "Economy-Heavy", value: n(spending && spending.economySpending, 0) },
    { label: "Defense-Heavy", value: n(spending && spending.defenseSpending, 0) + n(spending && spending.repairs, 0) },
  ].sort((left, right) => right.value - left.value);

  const dominant = buckets[0];
  const runnerUp = buckets[1];
  const total = sum(buckets.map((entry) => entry.value));
  const strongestPath = Array.isArray(buildingPaths) ? buildingPaths[0] : null;
  if (strongestPath && strongestPath.sharePercent >= 40)
    return "Branch Specialist";
  if (!dominant || total <= 0)
    return "Balanced";
  if (runnerUp && dominant.value <= runnerUp.value * 1.15)
    return "Balanced";
  return dominant.label;
}

function topEntry(mapLike, scoreSelector = (value) => value) {
  let winner = null;
  for (const entry of Array.isArray(mapLike) ? mapLike : Object.values(mapLike || {})) {
    if (!entry)
      continue;
    const score = n(scoreSelector(entry), 0);
    if (!winner || score > winner.score)
      winner = { entry, score };
  }
  return winner ? winner.entry : null;
}

function laneStartGold(summary, laneCount) {
  const aggregate = n(summary && summary.economy && summary.economy.startingEconomyReport && summary.economy.startingEconomyReport.startingGold, 0);
  return laneCount > 0 ? round(aggregate / laneCount, 2) : 0;
}

function buildUnitMetricsByLane(game) {
  const telemetry = game && game.balanceTelemetry;
  const unitInstances = telemetry && telemetry.unitInstances && typeof telemetry.unitInstances === "object"
    ? Object.values(telemetry.unitInstances)
    : [];
  const byLane = new Map();

  function ensureLane(laneIndex) {
    if (!byLane.has(laneIndex)) {
      byLane.set(laneIndex, {
        enemiesDefeated: 0,
        unitsLost: 0,
        armyValueFielded: 0,
        types: {},
        families: {},
        supportTypes: {},
        heroUnits: [],
      });
    }
    return byLane.get(laneIndex);
  }

  for (const instance of unitInstances) {
    if (!instance)
      continue;
    const laneIndex = i(instance.laneIndex, -1);
    if (laneIndex < 0)
      continue;
    const lane = ensureLane(laneIndex);
    if (normalizeKey(instance.team) === "enemy") {
      if (instance.deathTick != null)
        lane.enemiesDefeated += 1;
      continue;
    }

    const unitType = String(instance.unitType || "unknown").trim();
    const category = normalizeKey(instance.category || "other");
    if (!lane.types[unitType]) {
      lane.types[unitType] = {
        unitType,
        displayName: resolveUnitDisplayName(unitType),
        category,
        count: 0,
        damageDealt: 0,
        healingDone: 0,
        kills: 0,
        performanceScore: 0,
      };
    }

    const typeEntry = lane.types[unitType];
    typeEntry.count += 1;
    typeEntry.damageDealt += n(instance.damageDealt, 0);
    typeEntry.healingDone += n(instance.healingDone, 0);
    typeEntry.kills += i(instance.kills, 0);
    typeEntry.performanceScore += n(instance.damageDealt, 0) + (n(instance.healingDone, 0) * 0.6) + (i(instance.kills, 0) * 15);

    lane.armyValueFielded += n(instance.estimatedPower, 0);
    lane.families[category] = (lane.families[category] || 0) + 1;
    if (instance.deathTick != null)
      lane.unitsLost += 1;
    if (category === "healer")
      lane.supportTypes[unitType] = typeEntry;
    if (category === "hero")
      lane.heroUnits.push(typeEntry.displayName);
  }

  return byLane;
}

function mapSnapshotsByRound(roundSnapshots) {
  const byRound = new Map();
  for (const snapshot of Array.isArray(roundSnapshots) ? roundSnapshots : []) {
    if (!snapshot || !Number.isInteger(snapshot.round))
      continue;
    byRound.set(snapshot.round, snapshot);
  }
  return byRound;
}

function findLaneSnapshot(snapshot, laneIndex) {
  return Array.isArray(snapshot && snapshot.lanes)
    ? snapshot.lanes.find((lane) => lane && i(lane.laneIndex, -1) === laneIndex) || null
    : null;
}

function getWaveLaneSnapshots(wave, phase = "end") {
  return wave && wave.laneSnapshots && Array.isArray(wave.laneSnapshots[phase])
    ? wave.laneSnapshots[phase]
    : [];
}

function findWaveLaneSnapshot(wave, laneIndex, phase = "end") {
  const laneSnapshots = getWaveLaneSnapshots(wave, phase);
  return laneSnapshots.find((lane) => lane && i(lane.laneIndex, -1) === laneIndex) || null;
}

function resolveWaveLaneArmyValue(wave, laneIndex) {
  const peakSnapshot = findWaveLaneSnapshot(wave, laneIndex, "peak");
  const endSnapshot = findWaveLaneSnapshot(wave, laneIndex, "end");
  const startSnapshot = findWaveLaneSnapshot(wave, laneIndex, "start");
  const peak = n(peakSnapshot && peakSnapshot.playerArmyValue, 0);
  const end = n(endSnapshot && endSnapshot.playerArmyValue, 0);
  const start = n(startSnapshot && startSnapshot.playerArmyValue, 0);
  return round(Math.max(peak, end, start), 2);
}

function estimateLaneInvestments(game, lane) {
  const pathTotals = {};
  let upgradeSpending = 0;
  let economySpending = 0;
  let defenseSpending = 0;
  let knownBuildSpend = 0;

  function addPath(buildingType, amount) {
    const label = buildingLabel(buildingType);
    pathTotals[label] = (pathTotals[label] || 0) + amount;
  }

  for (const pad of Array.isArray(lane && lane.fortressPads) ? lane.fortressPads : []) {
    if (!pad || !pad.isBuilt)
      continue;
    const buildingType = normalizeKey(pad.buildingType);
    const buildCost = Math.max(0, fortressSystem.getFortressBuildCost(buildingType));
    if (buildCost > 0) {
      addPath(buildingType, buildCost);
      knownBuildSpend += buildCost;
      if (buildingType === "market")
        economySpending += buildCost;
      else if (fortressSystem.isSharedDefenseBuildingType(buildingType) || buildingType === "town_core")
        defenseSpending += buildCost;
    }

    for (let nextTier = 2; nextTier <= Math.max(0, i(pad.tier, 0)); nextTier += 1) {
      const cost = Math.max(0, fortressSystem.getFortressUpgradeCost(buildingType, nextTier));
      if (cost <= 0)
        continue;
      addPath(buildingType, cost);
      knownBuildSpend += cost;
      upgradeSpending += cost;
      if (buildingType === "market")
        economySpending += cost;
      else if (fortressSystem.isSharedDefenseBuildingType(buildingType) || buildingType === "town_core")
        defenseSpending += cost;
    }
  }

  for (const siteState of Object.values(lane && lane.barracksSiteStates && typeof lane.barracksSiteStates === "object" ? lane.barracksSiteStates : {})) {
    if (!siteState || !siteState.isBuilt)
      continue;
    const buildCost = Math.max(0, barracksSystem.getBarracksSiteBuildCost(barracksSystem.getBarracksSiteDef(siteState.barracksId)));
    if (buildCost > 0) {
      addPath("barracks", buildCost);
      knownBuildSpend += buildCost;
    }

    for (let nextLevel = 2; nextLevel <= Math.max(0, i(siteState.level, 0)); nextLevel += 1) {
      const cost = Math.max(0, n(barracksSystem.getBarracksLevelDef(nextLevel).cost, 0));
      if (cost <= 0)
        continue;
      addPath("barracks", cost);
      knownBuildSpend += cost;
      upgradeSpending += cost;
    }
  }

  for (const [buildingType, upgrades] of Object.entries(lane && lane.buildingUpgradeState && typeof lane.buildingUpgradeState === "object" ? lane.buildingUpgradeState : {})) {
    for (const upgradeState of Object.values(upgrades || {})) {
      const spent = Math.max(0, n(upgradeState && upgradeState.totalSpent, 0));
      if (spent <= 0)
        continue;
      addPath(buildingType, spent);
      knownBuildSpend += spent;
      upgradeSpending += spent;
      if (normalizeKey(buildingType) === "market")
        economySpending += spent;
      else if (fortressSystem.isSharedDefenseBuildingType(buildingType) || normalizeKey(buildingType) === "town_core")
        defenseSpending += spent;
    }
  }

  const marketRoster = barracksSystem.createMarketRosterSnapshot(game, lane, {});
  const marketCounts = lane && lane.marketRosterCounts && typeof lane.marketRosterCounts === "object"
    ? lane.marketRosterCounts
    : {};
  for (const entry of marketRoster || []) {
    if (!entry)
      continue;
    const ownedCount = Math.max(0, i(marketCounts[entry.unitKey], 0));
    const totalCost = ownedCount * Math.max(0, n(entry.buyCost, 0));
    if (totalCost <= 0)
      continue;
    addPath("market", totalCost);
    economySpending += totalCost;
    knownBuildSpend += totalCost;
  }

  const repairs = Math.max(0, round(n(lane && lane.totalBuildSpend, 0) - knownBuildSpend, 2));
  if (repairs > 0)
    defenseSpending += repairs;

  return {
    pathTotals,
    pathShares: shareRows(Object.entries(pathTotals).map(([label, value]) => ({ label, value }))),
    upgradeSpending: round(upgradeSpending, 2),
    economySpending: round(economySpending, 2),
    defenseSpending: round(defenseSpending, 2),
    repairs,
  };
}

function estimateUpgradeCount(lane) {
  let total = 0;
  for (const tier of Object.values(lane && lane.fortressTiers && typeof lane.fortressTiers === "object" ? lane.fortressTiers : {}))
    total += Math.max(0, i(tier, 0) - 1);
  for (const site of Array.isArray(lane && lane.barracksSites) ? lane.barracksSites : [])
    total += Math.max(0, i(site && site.level, 0) - 1);
  for (const buildingBucket of Object.values(lane && lane.rawLaneState && lane.rawLaneState.buildingUpgradeState && typeof lane.rawLaneState.buildingUpgradeState === "object" ? lane.rawLaneState.buildingUpgradeState : {})) {
    for (const upgrade of Object.values(buildingBucket || {}))
      total += Math.max(0, i(upgrade && upgrade.purchaseCount, 0));
  }
  return total;
}

function chooseOpponent(finalStats, lane) {
  const candidates = (Array.isArray(finalStats) ? finalStats : [])
    .filter((entry) => entry && i(entry.laneIndex, -1) !== i(lane.laneIndex, -1) && normalizeKey(entry.team) !== normalizeKey(lane.team));
  if (candidates.length <= 0)
    return null;
  return candidates.sort((left, right) => n(right.buildValue, 0) - n(left.buildValue, 0))[0];
}

function buildRoundSeries(waveRows, roundSnapshotsByRound, laneIndex, selector) {
  return (Array.isArray(waveRows) ? waveRows : []).map((row) => {
    const roundSnapshot = roundSnapshotsByRound.get(i(row && row.wave, 0)) || null;
    const lane = findLaneSnapshot(roundSnapshot, laneIndex);
    return round(n(selector(lane, row, roundSnapshot), 0), 2);
  });
}

function findFirstSustainedIndex(leftValues, rightValues, comparator, runLength = 2) {
  const left = Array.isArray(leftValues) ? leftValues : [];
  const right = Array.isArray(rightValues) ? rightValues : [];
  const pointCount = Math.min(left.length, right.length);
  if (pointCount <= 0)
    return -1;

  const requiredRun = Math.max(1, Math.min(runLength, pointCount));
  for (let start = 0; start <= pointCount - requiredRun; start += 1) {
    let holds = true;
    for (let offset = 0; offset < requiredRun; offset += 1) {
      if (!comparator(n(left[start + offset], 0), n(right[start + offset], 0))) {
        holds = false;
        break;
      }
    }
    if (holds)
      return start;
  }

  return -1;
}

function curveTakeaway(playerValues, opponentValues, threatValues, xLabels) {
  const playerLast = playerValues.length > 0 ? n(playerValues[playerValues.length - 1], 0) : 0;
  const opponentLast = opponentValues.length > 0 ? n(opponentValues[opponentValues.length - 1], 0) : 0;
  const threatLast = threatValues.length > 0 ? n(threatValues[threatValues.length - 1], 0) : 0;
  const firstLeadIndex = findFirstSustainedIndex(playerValues, opponentValues, (left, right) => left > right, 2);
  const firstThreatBeatIndex = findFirstSustainedIndex(playerValues, threatValues, (left, right) => left >= right, 2);

  const notes = [];
  if (firstLeadIndex >= 0 && opponentValues.length > 0 && xLabels[firstLeadIndex])
    notes.push(`You built a lasting lead at ${xLabels[firstLeadIndex]}.`);
  if (firstThreatBeatIndex >= 0 && threatValues.length > 0 && xLabels[firstThreatBeatIndex])
    notes.push(`You finally held above the horde at ${xLabels[firstThreatBeatIndex]}.`);
  if (playerLast > opponentLast && opponentValues.length > 0)
    notes.push("You finished with the stronger curve.");
  if (playerLast > threatLast && threatValues.length > 0)
    notes.push("You ended the match above the incoming threat.");
  if (notes.length <= 0 && threatValues.length > 0)
    notes.push("The dungeon kept pressure on your line through the finish.");
  if (notes.length <= 0 && opponentValues.length > 0)
    notes.push("Neither side held a lasting curve edge for long.");
  return notes.join(" ");
}

function buildWaveRows(waveReports, laneIndex, opponentLaneIndex, roundSnapshotsByRound) {
  const rows = [];
  for (const wave of Array.isArray(waveReports) ? waveReports : []) {
    if (!wave)
      continue;
    const playerLane = findWaveLaneSnapshot(wave, laneIndex, "end")
      || findWaveLaneSnapshot(wave, laneIndex, "peak")
      || findWaveLaneSnapshot(wave, laneIndex, "start");
    if (!playerLane && resolveWaveLaneArmyValue(wave, laneIndex) <= 0)
      continue;
    const roundSnapshot = roundSnapshotsByRound.get(i(wave.waveNumber, 0)) || null;
    const roundLane = findLaneSnapshot(roundSnapshot, laneIndex);
    const laneSnapshotsForCount = getWaveLaneSnapshots(wave, "end").length > 0
      ? getWaveLaneSnapshots(wave, "end")
      : getWaveLaneSnapshots(wave, "start");
    const activeLaneCount = Math.max(1, laneSnapshotsForCount.filter((entry) => entry && !entry.eliminated).length);
    const playerStrength = resolveWaveLaneArmyValue(wave, laneIndex);
    const dungeonThreat = round(n(wave && wave.powerCurve && wave.powerCurve.enemyWaveEffectivePower, 0) / activeLaneCount, 2);
    const breachCount = Math.max(0, i(roundLane && roundLane.leaksTaken, 0));
    const coreDamage = Math.max(0, i(roundLane && roundLane.leakDamage, 0));
    const ratio = dungeonThreat > 0 ? round(playerStrength / dungeonThreat, 2) : playerStrength > 0 ? 1.5 : 0;
    const state = threatStateLabel(ratio, breachCount > 0, coreDamage);
    rows.push({
      wave: i(wave.waveNumber, 0),
      state,
      bankedGold: round(n(playerLane.gold, 0), 2),
      armyStrength: playerStrength,
      dungeonThreat,
      breachCount,
      coreDamage,
      opponentArmyStrength: opponentLaneIndex >= 0 ? resolveWaveLaneArmyValue(wave, opponentLaneIndex) : 0,
    });
  }
  return rows;
}

function buildAwards(snapshot, fortressPressure, battleStory, strategy, armySummary) {
  const awards = [];
  if (snapshot.breachesSuffered <= 0 && fortressPressure.coreDamageTaken <= 0)
    awards.push({ title: "Iron Wall", detail: "Your fortress never yielded a breach." });
  if (snapshot.coreHealthRemaining > 0 && snapshot.coreHealthRemaining <= 3)
    awards.push({ title: "Last Stand", detail: "You finished the run with the core hanging by a thread." });
  if (snapshot.goldEarned > 0 && snapshot.goldSpent / Math.max(1, snapshot.goldEarned) >= 0.88)
    awards.push({ title: "Frugal Commander", detail: "You kept your war chest working instead of letting it sit idle." });
  if (armySummary && Array.isArray(armySummary.unitComposition) && armySummary.unitComposition[0] && armySummary.unitComposition[0].label === "Archers")
    awards.push({ title: "Arrow Storm", detail: "Ranged fire defined your battle line." });
  if (strategy && normalizeKey(strategy.playerStyleLabel) === "balanced")
    awards.push({ title: "War Machine", detail: "You kept your economy, army, and fortress growing together." });
  if (battleStory && battleStory.highlights && battleStory.highlights.some((entry) => normalizeKey(entry.title).includes("stabilized")))
    awards.push({ title: "Unbroken Line", detail: "You recovered your formation and held the field." });
  return awards.slice(0, 3);
}

function buildCommanderReport({
  game,
  laneReport,
  opponentReport,
  summary,
  waveReports,
  roundSnapshots,
  unitMetricsByLane,
  laneCount,
}) {
  const laneIndex = i(laneReport.laneIndex, -1);
  const opponentLaneIndex = opponentReport ? i(opponentReport.laneIndex, -1) : -1;
  const roundSnapshotsByRound = mapSnapshotsByRound(roundSnapshots);
  const waveRows = buildWaveRows(waveReports, laneIndex, opponentLaneIndex, roundSnapshotsByRound);
  const xLabels = waveRows.map((row) => `W${row.wave}`);
  const playerGold = buildRoundSeries(waveRows, roundSnapshotsByRound, laneIndex, (lane, row) => lane ? lane.gold : row.bankedGold);
  const opponentGold = opponentLaneIndex >= 0
    ? buildRoundSeries(waveRows, roundSnapshotsByRound, opponentLaneIndex, (lane) => lane && lane.gold)
    : [];
  const playerIncome = buildRoundSeries(waveRows, roundSnapshotsByRound, laneIndex, (lane) => lane && lane.income);
  const opponentIncome = opponentLaneIndex >= 0
    ? buildRoundSeries(waveRows, roundSnapshotsByRound, opponentLaneIndex, (lane) => lane && lane.income)
    : [];
  const playerSpend = buildRoundSeries(waveRows, roundSnapshotsByRound, laneIndex, (lane) => n(lane && lane.sendSpend, 0) + n(lane && lane.buildSpend, 0));
  const opponentSpend = opponentLaneIndex >= 0
    ? buildRoundSeries(waveRows, roundSnapshotsByRound, opponentLaneIndex, (lane) => n(lane && lane.sendSpend, 0) + n(lane && lane.buildSpend, 0))
    : [];

  const playerArmy = waveRows.map((row) => row.armyStrength);
  const opponentArmy = waveRows.map((row) => row.opponentArmyStrength);
  const dungeonThreat = waveRows.map((row) => row.dungeonThreat);

  const metrics = unitMetricsByLane.get(laneIndex) || {
    enemiesDefeated: 0,
    unitsLost: 0,
    armyValueFielded: 0,
    types: {},
    families: {},
    supportTypes: {},
    heroUnits: [],
  };
  const startGold = laneStartGold(summary, laneCount);
  const goldSpent = round(n(laneReport.totalSendSpend, 0) + n(laneReport.totalBuildSpend, 0), 2);
  const goldEarned = round(Math.max(0, goldSpent + n(laneReport.gold, 0) - startGold), 2);
  const investments = estimateLaneInvestments(game, laneReport.rawLaneState);
  const fortressStructureCount = sum(Object.entries(laneReport.builtStructureCounts || {}).filter(([key]) => normalizeKey(key) !== "town_core").map(([, value]) => value));
  const buildingsConstructed = fortressStructureCount + (Array.isArray(laneReport.barracksSites) ? laneReport.barracksSites.length : 0);
  const breachesSuffered = (Array.isArray(roundSnapshots) ? roundSnapshots : []).reduce((total, snapshot) => {
    const lane = findLaneSnapshot(snapshot, laneIndex);
    return total + ((lane && (i(lane.leaksTaken, 0) > 0 || i(lane.leakDamage, 0) > 0)) ? 1 : 0);
  }, 0);
  const fortressEntries = (Array.isArray(roundSnapshots) ? roundSnapshots : []).reduce((total, snapshot) => {
    const lane = findLaneSnapshot(snapshot, laneIndex);
    return total + Math.max(0, i(lane && lane.leaksTaken, 0));
  }, 0);
  const aggregateEntries = (Array.isArray(roundSnapshots) ? roundSnapshots : []).reduce((total, snapshot) => total + sum((snapshot && Array.isArray(snapshot.lanes) ? snapshot.lanes : []).map((lane) => Math.max(0, i(lane && lane.leaksTaken, 0)))), 0);
  const entryShare = aggregateEntries > 0 ? fortressEntries / aggregateEntries : laneCount > 0 ? 1 / laneCount : 1;
  const totalWallDamage = sum((Array.isArray(waveReports) ? waveReports : []).map((wave) => n(wave && wave.combatResults && wave.combatResults.wallDamageTaken, 0)));
  const totalTowerDamage = sum((Array.isArray(waveReports) ? waveReports : []).map((wave) => n(wave && wave.combatResults && wave.combatResults.towerDamageTaken, 0)));
  const totalCoreDamage = sum((Array.isArray(roundSnapshots) ? roundSnapshots : []).map((snapshot) => Math.max(0, i(findLaneSnapshot(snapshot, laneIndex) && findLaneSnapshot(snapshot, laneIndex).leakDamage, 0))));
  const timeUnderBreachPressure = round(sum((Array.isArray(waveReports) ? waveReports : []).map((wave) => n(wave && wave.pressure && wave.pressure.secondsUnderBreachPressure, 0))) * entryShare, 1);
  const timeWithoutFrontline = round(sum((Array.isArray(waveReports) ? waveReports : []).map((wave) => n(wave && wave.pressure && wave.pressure.secondsWithoutFrontline, 0))) * entryShare, 1);
  const coreHealthStart = Math.max(n(game && game.teamHpMax, 0), n(laneReport.teamHp, 0));
  const coreDamageTaken = Math.max(0, coreHealthStart - i(laneReport.teamHp, 0));

  const familyBreakdown = shareRows(
    Object.entries(metrics.families || {}).map(([category, value]) => ({ label: familyLabel(category), value }))
  );
  const mostRecruited = topEntry(metrics.types, (entry) => entry.count);
  const bestPerformer = topEntry(metrics.types, (entry) => entry.performanceScore);
  const bestSupport = topEntry(metrics.supportTypes, (entry) => entry.healingDone);

  const playerStrategy = {
    commanderName: laneReport.displayName,
    unitSpending: round(n(laneReport.totalSendSpend, 0), 2),
    upgradeSpending: investments.upgradeSpending,
    economySpending: investments.economySpending,
    defenseSpending: investments.defenseSpending,
    repairs: investments.repairs,
    otherSpending: 0,
    unitComposition: familyBreakdown,
    buildingPaths: investments.pathShares.slice(0, 4),
    highlights: [],
  };
  playerStrategy.highlights.push(`${playerStrategy.buildingPaths[0] ? playerStrategy.buildingPaths[0].label : "Barracks"} was your main building path.`);
  if (playerStrategy.unitComposition[0])
    playerStrategy.highlights.push(`${playerStrategy.unitComposition[0].label} made up ${playerStrategy.unitComposition[0].sharePercent}% of your fielded force.`);
  if (playerStrategy.repairs > 0)
    playerStrategy.highlights.push(`You spent ${playerStrategy.repairs.toFixed(0)} gold restoring damaged fortifications.`);

  let opponentStrategy = null;
  if (opponentReport) {
    const opponentInvestments = estimateLaneInvestments(game, opponentReport.rawLaneState);
    opponentStrategy = {
      commanderName: opponentReport.displayName,
      unitSpending: round(n(opponentReport.totalSendSpend, 0), 2),
      upgradeSpending: opponentInvestments.upgradeSpending,
      economySpending: opponentInvestments.economySpending,
      defenseSpending: opponentInvestments.defenseSpending,
      repairs: opponentInvestments.repairs,
      otherSpending: 0,
      unitComposition: [],
      buildingPaths: opponentInvestments.pathShares.slice(0, 4),
      highlights: [],
    };
  }

  playerStrategy.styleLabel = styleLabel(playerStrategy, playerStrategy.buildingPaths);
  if (opponentStrategy)
    opponentStrategy.styleLabel = styleLabel(opponentStrategy, opponentStrategy.buildingPaths);

  const stabilizationWave = waveRows.find((row, index) =>
    index < waveRows.length - 1 &&
    row.state !== "Struggling" &&
    row.coreDamage <= 0 &&
    waveRows[index + 1].state !== "Struggling" &&
    waveRows[index + 1].coreDamage <= 0);
  const firstSurpassedIndex = findFirstSustainedIndex(playerArmy, dungeonThreat, (left, right) => left >= right, 2);
  const firstSurpassed = firstSurpassedIndex >= 0 ? waveRows[firstSurpassedIndex] : null;
  const hardestWave = waveRows.slice().sort((left, right) => {
    const leftScore = (left.coreDamage * 100) + (left.breachCount * 12) + (left.dungeonThreat - left.armyStrength);
    const rightScore = (right.coreDamage * 100) + (right.breachCount * 12) + (right.dungeonThreat - right.armyStrength);
    return rightScore - leftScore;
  })[0] || null;
  const firstBreach = waveRows.find((row) => row.breachCount > 0 || row.coreDamage > 0) || null;

  let biggestSwing = null;
  let previousDelta = null;
  for (const row of waveRows) {
    const delta = round(row.armyStrength - row.dungeonThreat, 2);
    if (previousDelta != null) {
      const swing = round(delta - previousDelta, 2);
      if (!biggestSwing || Math.abs(swing) > Math.abs(biggestSwing.swing))
        biggestSwing = { wave: row.wave, swing };
    }
    previousDelta = delta;
  }

  const strongestSpikeSource = playerStrategy.buildingPaths[0]
    ? playerStrategy.buildingPaths[0].label
    : (mostRecruited ? mostRecruited.displayName : "Barracks reinforcement");

  const battleStoryHighlights = [];
  if (hardestWave)
    battleStoryHighlights.push({ title: "Hardest Wave", detail: `Wave ${hardestWave.wave} brought the heaviest pressure.`, wave: hardestWave.wave });
  if (firstBreach)
    battleStoryHighlights.push({ title: "First Breach", detail: `Your fortress first cracked at Wave ${firstBreach.wave}.`, wave: firstBreach.wave });
  if (stabilizationWave)
    battleStoryHighlights.push({ title: "Stabilized", detail: `You steadied the line at Wave ${stabilizationWave.wave}.`, wave: stabilizationWave.wave });
  if (firstSurpassed)
    battleStoryHighlights.push({ title: "Surpassed the Horde", detail: `Your warband first cleared the dungeon curve at Wave ${firstSurpassed.wave}.`, wave: firstSurpassed.wave });
  if (biggestSwing)
    battleStoryHighlights.push({ title: "Biggest Swing", detail: `Wave ${biggestSwing.wave} swung your battle strength by ${biggestSwing.swing > 0 ? "+" : ""}${biggestSwing.swing}.`, wave: biggestSwing.wave });
  battleStoryHighlights.push({ title: "Strongest Spike Source", detail: `${strongestSpikeSource} drove your biggest surge.`, wave: firstSurpassed ? firstSurpassed.wave : (hardestWave ? hardestWave.wave : 0) });

  const commanderNotes = [];
  commanderNotes.push(`${metrics.enemiesDefeated} enemies fell before your defenses.`);
  if (stabilizationWave)
    commanderNotes.push(`You stabilized at Wave ${stabilizationWave.wave}.`);
  else
    commanderNotes.push("The match never fully settled into a safe rhythm.");
  if (firstSurpassed)
    commanderNotes.push(`You moved ahead of the incoming horde at Wave ${firstSurpassed.wave}.`);
  else
    commanderNotes.push("You never fully pulled ahead of dungeon pressure.");
  if (opponentReport && opponentGold.length > 0) {
    const overtakeIndex = findFirstSustainedIndex(opponentGold, playerGold, (left, right) => left > right, 2);
    if (overtakeIndex >= 0 && xLabels[overtakeIndex])
      commanderNotes.push(`${opponentReport.displayName} carried the larger war chest by ${xLabels[overtakeIndex]}.`);
  }

  const recommendations = [];
  const peakBankedGold = Math.max(0, ...playerGold);
  if (peakBankedGold >= 250 && coreDamageTaken > 0)
    recommendations.push(`Spend sooner when your war chest rises above ${Math.round(peakBankedGold)} gold.`);
  if (playerStrategy.economySpending > playerStrategy.unitSpending && breachesSuffered > 0)
    recommendations.push("Shift some economy spending into frontline units before the next pressure spike.");
  if (playerStrategy.upgradeSpending > playerStrategy.unitSpending && breachesSuffered > 0)
    recommendations.push("Delay long upgrades until your line is stable.");
  if (recommendations.length <= 0)
    recommendations.push("Your curve held up well. Try squeezing a little more economy before your next army spike.");

  const strategySummary = opponentStrategy
    ? `You played ${playerStrategy.styleLabel.toLowerCase()} while ${opponentReport.displayName} leaned ${opponentStrategy.styleLabel.toLowerCase()}.`
    : `Your build leaned ${playerStrategy.styleLabel.toLowerCase()} across the match.`;

  const snapshot = {
    enemiesDefeated: Math.max(metrics.enemiesDefeated, 0),
    unitsRecruited: Math.max(0, i(laneReport.totalSendCount, 0)),
    unitsLost: Math.max(0, metrics.unitsLost),
    buildingsConstructed: Math.max(0, i(buildingsConstructed, 0)),
    upgradesPurchased: Math.max(0, estimateUpgradeCount(laneReport)),
    goldEarned,
    goldSpent,
    breachesSuffered: Math.max(0, breachesSuffered),
    coreHealthRemaining: Math.max(0, i(laneReport.teamHp, 0)),
    coreDamageTaken: Math.max(0, coreDamageTaken),
  };

  const fortressPressure = {
    breachCount: Math.max(0, breachesSuffered),
    wallDamageTaken: Math.max(0, Math.round(totalWallDamage * entryShare)),
    towerDamageTaken: Math.max(0, Math.round(totalTowerDamage * entryShare)),
    coreDamageTaken: Math.max(0, totalCoreDamage),
    timeUnderBreachPressure,
    fortressEntries: Math.max(0, fortressEntries),
    timeWithoutFrontline,
    pressureVerdict: breachesSuffered <= 0 ? "Unbroken" : coreDamageTaken > 0 ? "Frontline Collapse" : "Under Pressure",
    summary: breachesSuffered <= 0
      ? "The fortress held cleanly from wall to core."
      : `The fortress spent ${timeUnderBreachPressure.toFixed(1)}s under breach pressure and took ${coreDamageTaken} core damage.`,
  };

  const armySummary = {
    mostRecruitedUnitType: mostRecruited ? mostRecruited.displayName : "No unit line stood out",
    bestPerformingUnitType: bestPerformer ? bestPerformer.displayName : "No unit line stood out",
    armyValueFielded: round(metrics.armyValueFielded, 2),
    unitComposition: familyBreakdown.slice(0, 5),
    bestSupportType: bestSupport ? bestSupport.displayName : "No support spike recorded",
    heroContribution: metrics.heroUnits.length > 0
      ? `${Array.from(new Set(metrics.heroUnits)).join(", ")} joined the field.`
      : "No hero spike was recorded.",
    summary: bestPerformer
      ? `${bestPerformer.displayName} delivered the strongest overall contribution.`
      : "No single unit line clearly carried the match.",
  };

  const awards = buildAwards(snapshot, fortressPressure, { highlights: battleStoryHighlights }, { playerStyleLabel: playerStrategy.styleLabel }, armySummary);

  return {
    laneIndex,
    displayName: laneReport.displayName,
    isAI: !!laneReport.isAI,
    difficulty: laneReport.difficulty || null,
    result: normalizeKey(summary && summary.matchIdentity && summary.matchIdentity.finalResult) === "completed" && i(summary && summary.matchIdentity && summary.matchIdentity.finalWaveReached, 0) > 0 && !laneReport.eliminated
      ? "victory"
      : (laneReport.eliminated ? "defeat" : "completed"),
    resultLabel: laneReport.eliminated ? "Defeat" : "Victory",
    opponentLaneIndex,
    opponentName: opponentReport ? opponentReport.displayName : null,
    commanderNotes,
    matchHeader: {
      resultLabel: laneReport.eliminated ? "Defeat" : "Victory",
      finalWave: i(summary && summary.matchIdentity && summary.matchIdentity.finalWaveReached, 0),
      durationSeconds: i(summary && summary.matchIdentity && summary.matchIdentity.totalMatchDuration, 0),
      modeLabel: modeLabel(summary && summary.matchIdentity && summary.matchIdentity.mode),
      difficultyLabel: opponentReport && opponentReport.isAI && opponentReport.difficulty
        ? `${titleCaseFromKey(opponentReport.difficulty)} Opponent`
        : null,
      scoreLabel: null,
      rankLabel: null,
      ratingLabel: null,
    },
    snapshot,
    economyCurve: {
      title: "War Chest",
      subtitle: "Banked gold, income, and match spending by wave.",
      valueFormat: "F0",
      takeaway: curveTakeaway(playerGold, opponentGold, [], xLabels),
      xLabels,
      lines: [
        { label: "Your War Chest", values: playerGold, tone: "player", isPrimary: true },
        ...(opponentGold.length > 0 ? [{ label: `${opponentReport.displayName} War Chest`, values: opponentGold, tone: "opponent", isPrimary: false }] : []),
        { label: "Your Gold Flow", values: playerIncome, tone: "support", isPrimary: false },
        ...(opponentIncome.length > 0 ? [{ label: `${opponentReport.displayName} Gold Flow`, values: opponentIncome, tone: "opponent_support", isPrimary: false }] : []),
        { label: "Your Spending", values: playerSpend, tone: "spend", isPrimary: false },
        ...(opponentSpend.length > 0 ? [{ label: `${opponentReport.displayName} Spending`, values: opponentSpend, tone: "opponent_spend", isPrimary: false }] : []),
      ],
    },
    armyCurve: {
      title: "Warband Value",
      subtitle: "Army strength growth through the match.",
      valueFormat: "F0",
      takeaway: curveTakeaway(playerArmy, opponentArmy, [], xLabels),
      xLabels,
      lines: [
        { label: "Your Army Strength", values: playerArmy, tone: "player", isPrimary: true },
        ...(opponentReport && opponentArmy.length > 0 ? [{ label: `${opponentReport.displayName} Army Strength`, values: opponentArmy, tone: "opponent", isPrimary: false }] : []),
      ],
    },
    threatCurve: {
      title: "War Strength vs Incoming Horde",
      subtitle: "Your fielded strength against dungeon pressure.",
      valueFormat: "F0",
      takeaway: curveTakeaway(playerArmy, opponentArmy, dungeonThreat, xLabels),
      xLabels,
      lines: [
        { label: "Your Battle Strength", values: playerArmy, tone: "player", isPrimary: true },
        ...(opponentReport && opponentArmy.length > 0 ? [{ label: `${opponentReport.displayName} Battle Strength`, values: opponentArmy, tone: "opponent", isPrimary: false }] : []),
        { label: "Dungeon Threat", values: dungeonThreat, tone: "threat", isPrimary: false },
      ],
    },
    strategyComparison: {
      playerStyleLabel: playerStrategy.styleLabel,
      opponentStyleLabel: opponentStrategy ? opponentStrategy.styleLabel : null,
      summary: strategySummary,
      player: playerStrategy,
      opponent: opponentStrategy,
    },
    fortressPressure,
    armySummary,
    battleStory: {
      highlights: battleStoryHighlights,
      storyLines: [
        hardestWave ? `Wave ${hardestWave.wave} was the hardest point of the match.` : null,
        firstBreach ? `The first fortress breach came on Wave ${firstBreach.wave}.` : null,
        stabilizationWave ? `You stabilized at Wave ${stabilizationWave.wave}.` : "You never found a fully safe stabilization window.",
        firstSurpassed ? `You first outpaced dungeon pressure at Wave ${firstSurpassed.wave}.` : "The dungeon stayed ahead of your curve all match.",
        `Your strongest spike came from ${strongestSpikeSource}.`,
      ].filter(Boolean),
      recommendations,
    },
    awards,
    advanced: {
      breakdownLines: [
        strategySummary,
        `Spending buckets: Units ${playerStrategy.unitSpending.toFixed(0)} | Upgrades ${playerStrategy.upgradeSpending.toFixed(0)} | Economy ${playerStrategy.economySpending.toFixed(0)} | Defense ${playerStrategy.defenseSpending.toFixed(0)} | Repairs ${playerStrategy.repairs.toFixed(0)}`,
        armySummary.summary,
        fortressPressure.summary,
      ],
      waveRows,
    },
  };
}

function buildPlayerBattleReport({ game, balanceData, finalStats, roundSnapshots }) {
  const summary = balanceData && balanceData.summary ? balanceData.summary : null;
  const waveReports = Array.isArray(balanceData && balanceData.waveReports) ? balanceData.waveReports : [];
  const lanes = Array.isArray(finalStats) ? finalStats : [];
  const laneCount = lanes.length;
  const unitMetricsByLane = buildUnitMetricsByLane(game);
  const laneStateByIndex = new Map((game && Array.isArray(game.lanes) ? game.lanes : []).map((lane) => [i(lane && lane.laneIndex, -1), lane]));
  const preparedLaneReports = lanes.map((lane) => ({
    ...lane,
    rawLaneState: laneStateByIndex.get(i(lane && lane.laneIndex, -1)) || null,
  }));

  return {
    schemaVersion: SCHEMA_VERSION,
    tabs: ["Summary", "Economy", "Army", "Threat", "Story", "Advanced"],
    lanes: preparedLaneReports.map((lane) => buildCommanderReport({
      game,
      laneReport: lane,
      opponentReport: chooseOpponent(preparedLaneReports, lane),
      summary,
      waveReports,
      roundSnapshots,
      unitMetricsByLane,
      laneCount,
    })),
  };
}

module.exports = {
  buildPlayerBattleReport,
};
