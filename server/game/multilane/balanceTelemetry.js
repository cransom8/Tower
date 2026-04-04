"use strict";

const barracksSystem = require("./barracksSystem");
const fortressSystem = require("./fortressSystem");
const { getUnitType } = require("../../unitTypes");
const { FORT_ARCHETYPE_DEFS, titleCaseFromKey } = require("../fortUnitCatalog");

const SCHEMA_VERSION = 1;
const DEFAULT_TICK_HZ = 20;
const EARLY_WAVE_END = 3;
const MID_WAVE_END = 8;
const GOLD_FLOAT_THRESHOLDS = Object.freeze([250, 500, 1000, 2000]);
const FORT_ARCHETYPE_BY_KEY = new Map(
  FORT_ARCHETYPE_DEFS.map((entry) => [String(entry.archetypeKey || "").trim().toLowerCase(), entry])
);

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

function clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function avg(values) {
  const filtered = values.filter((value) => Number.isFinite(Number(value))).map(Number);
  if (filtered.length <= 0)
    return 0;
  return filtered.reduce((sum, value) => sum + value, 0) / filtered.length;
}

function sum(values) {
  return values.reduce((total, value) => total + (Number(value) || 0), 0);
}

function clone(value) {
  return value == null ? value : JSON.parse(JSON.stringify(value));
}

function phaseBand(waveNumber) {
  const safeWave = Math.max(1, i(waveNumber, 1));
  if (safeWave <= EARLY_WAVE_END)
    return "early";
  if (safeWave <= MID_WAVE_END)
    return "mid";
  return "late";
}

function tickHz(game) {
  return Math.max(1, i(game && game.balanceTelemetry && game.balanceTelemetry.meta && game.balanceTelemetry.meta.tickHz, DEFAULT_TICK_HZ));
}

function sec(game, ticks) {
  return round(Math.max(0, n(ticks, 0)) / tickHz(game), 2);
}

function enemyUnit(unit) {
  const source = String(unit && unit.spawnSourceType || "").trim().toLowerCase();
  return !!(unit && (unit.isWaveUnit || source === "dungeon_wave" || source === "scheduled_wave" || i(unit.ownerLaneIndex, 0) < 0));
}

function playerUnit(unit) {
  return !!unit && !enemyUnit(unit);
}

function unitCategory(unit) {
  if (!unit)
    return "other";
  const archetype = String(unit.archetypeKey || unit.type || unit.unitTypeKey || "").trim().toLowerCase();
  const family = FORT_ARCHETYPE_BY_KEY.get(archetype);
  const hint = [archetype, String(unit.heroKey || ""), String(unit.marketUnitKey || ""), String(unit.combatRole || unit.role || "")].join("|");
  if (unit.isHero || hint.includes("hero"))
    return "hero";
  if (hint.includes("economy") || unit.isMarketWorker)
    return "economy";
  if (family && family.family === "shield")
    return "shield";
  if (family && family.family === "infantry")
    return hint.includes("knight") ? "cavalry" : "infantry";
  if (family && family.family === "polearm")
    return hint.includes("lancer") ? "cavalry" : "spear";
  if (family && family.family === "ranged")
    return hint.includes("scout") ? "cavalry" : "archer";
  if (family && family.family === "arcane")
    return "mage";
  if (family && family.family === "support")
    return "healer";
  return "other";
}

function frontlineCategory(category) {
  return category === "shield" || category === "infantry" || category === "spear" || category === "cavalry" || category === "hero";
}

function unitPower(unit) {
  if (!unit)
    return 0;
  const def = getUnitType(String(unit.type || unit.unitTypeKey || "")) || null;
  const hp = Math.max(1, n(unit.maxHp, n(unit.hp, n(def && def.hp, 1))));
  const damage = Math.max(1, n(unit.baseDmg, n(def && def.attack_damage, 1)));
  const cooldown = Math.max(1, n(unit.atkCdTicks, n(def && def.attack_speed, 20)));
  const speed = Math.max(0.01, n(unit.baseSpeed, n(def && def.path_speed, 0.2)));
  const dps = damage * (DEFAULT_TICK_HZ / cooldown);
  return round((hp) + (dps * 10) + (speed * 18) + (unit.isHero ? 22 : 0) + (unitCategory(unit) === "healer" ? 14 : 0), 2);
}

function structureBucket(buildingType) {
  const normalized = String(buildingType || "").trim().toLowerCase();
  if (normalized === "wall" || normalized === "gate")
    return "wall";
  if (normalized === "turret" || normalized === "archery_tower" || normalized === "wizard_tower")
    return "tower";
  if (normalized === "town_core")
    return "core";
  return "other";
}

function totalGold(game) {
  return round(sum((game && Array.isArray(game.lanes) ? game.lanes : []).map((lane) => n(lane && lane.gold, 0))), 2);
}

function affordableSnapshot(game) {
  const result = {
    unitsAffordableAtWaveStart: 0,
    upgradesAffordableAtWaveStart: 0,
    hadSurvivalVsScalingTradeoff: false,
    couldBuyBothFreely: false,
  };

  for (const lane of (game && Array.isArray(game.lanes) ? game.lanes : []).filter((entry) => entry && !entry.eliminated)) {
    const gold = Math.max(0, n(lane.gold, 0));
    const unitCosts = [];
    const longTermCosts = [];
    for (const siteState of lane.barracksSiteStates && typeof lane.barracksSiteStates === "object" ? Object.values(lane.barracksSiteStates) : []) {
      if (!siteState)
        continue;
      const site = barracksSystem.createBarracksSiteSnapshot(game, lane, siteState.barracksId, {});
      if (site.canBuild && site.buildCost > 0)
        longTermCosts.push(site.buildCost);
      if (site.canUpgrade && site.upgradeCost > 0)
        longTermCosts.push(site.upgradeCost);
      for (const entry of site.roster || []) {
        if (entry && entry.unlocked && entry.availableForPurchase && n(entry.buyCost, 0) > 0)
          unitCosts.push(entry.buyCost);
      }
    }
    for (const entry of barracksSystem.createMarketRosterSnapshot(game, lane, {})) {
      if (entry && entry.unlocked && entry.availableForPurchase && n(entry.buyCost, 0) > 0)
        unitCosts.push(entry.buyCost);
    }
    for (const entry of barracksSystem.createHeroRosterSnapshot(game, lane, {})) {
      if (entry && entry.unlocked && entry.canSummon && n(entry.summonCost, 0) > 0)
        unitCosts.push(entry.summonCost);
    }
    for (const pad of lane.fortressPads || []) {
      if (!pad)
        continue;
      const snapshot = fortressSystem.createFortressPadSnapshot(game, lane, pad, {});
      if (snapshot.canBuild && snapshot.buildCost > 0)
        longTermCosts.push(snapshot.buildCost);
      if (snapshot.canUpgrade && snapshot.upgradeCost > 0)
        longTermCosts.push(snapshot.upgradeCost);
      for (const upgrade of snapshot.buildingUpgrades || []) {
        if (upgrade && upgrade.canPurchase && n(upgrade.cost, 0) > 0)
          longTermCosts.push(upgrade.cost);
      }
    }
    result.unitsAffordableAtWaveStart += unitCosts.filter((cost) => gold >= cost).length;
    result.upgradesAffordableAtWaveStart += longTermCosts.filter((cost) => gold >= cost).length;
    const minUnit = unitCosts.length > 0 ? Math.min(...unitCosts) : null;
    const minLongTerm = longTermCosts.length > 0 ? Math.min(...longTermCosts) : null;
    if (minUnit != null && minLongTerm != null && gold >= Math.min(minUnit, minLongTerm) && gold < (minUnit + minLongTerm))
      result.hadSurvivalVsScalingTradeoff = true;
    if (minUnit != null && minLongTerm != null && gold >= (minUnit + minLongTerm))
      result.couldBuyBothFreely = true;
  }
  return result;
}

function armySnapshot(game) {
  const composition = {
    shield: 0,
    infantry: 0,
    spear: 0,
    archer: 0,
    mage: 0,
    healer: 0,
    cavalry: 0,
    economy: 0,
    hero: 0,
    other: 0,
  };
  const laneSnapshots = [];
  let playerAlive = 0;
  let enemyAlive = 0;
  let playerPowerTotal = 0;
  let enemyPowerTotal = 0;
  let frontlineCount = 0;

  for (const lane of game && Array.isArray(game.lanes) ? game.lanes : []) {
    if (!lane)
      continue;
    let lanePlayerAlive = 0;
    let laneEnemyAlive = 0;
    let lanePlayerPower = 0;
    let laneEnemyPower = 0;
    for (const unit of lane.units || []) {
      if (!unit || n(unit.hp, 0) <= 0)
        continue;
      const power = unitPower(unit);
      if (enemyUnit(unit)) {
        laneEnemyAlive += 1;
        laneEnemyPower += power;
        enemyAlive += 1;
        enemyPowerTotal += power;
        continue;
      }
      const category = unitCategory(unit);
      composition[category] = (composition[category] || 0) + 1;
      if (frontlineCategory(category))
        frontlineCount += 1;
      lanePlayerAlive += 1;
      lanePlayerPower += power;
      playerAlive += 1;
      playerPowerTotal += power;
    }
    laneSnapshots.push({
      laneIndex: i(lane.laneIndex, -1),
      gold: round(n(lane.gold, 0), 2),
      lives: n(lane.lives, 0),
      eliminated: !!lane.eliminated,
      playerUnitsAlive: lanePlayerAlive,
      enemyUnitsAlive: laneEnemyAlive,
      playerArmyValue: round(lanePlayerPower, 2),
      enemyArmyValue: round(laneEnemyPower, 2),
    });
  }

  return {
    composition,
    laneSnapshots,
    playerAlive,
    enemyAlive,
    playerPowerTotal: round(playerPowerTotal, 2),
    enemyPowerTotal: round(enemyPowerTotal, 2),
    frontlineCount,
  };
}

function waveStrength(game, waveDef = null) {
  const session = game && game.activeWaveSession;
  const activeLaneCount = (game && Array.isArray(game.lanes) ? game.lanes : []).filter((lane) => lane && !lane.eliminated).length;
  const def = waveDef || (session && session.waveDef) || null;
  const unitDef = getUnitType(String(def && def.unit_type || "")) || null;
  const perLane = Math.max(0, i(session && session.spawnQtyPerLane, def && def.spawn_qty));
  const groups = Math.max(1, i(session && session.totalGroups, 1));
  const count = activeLaneCount * perLane * groups;
  const hpPool = count * Math.max(1, n(unitDef && unitDef.hp, 1) * Math.max(0.01, n(def && def.hp_mult, 1)));
  const damagePotential = count * Math.max(1, n(unitDef && unitDef.attack_damage, 1) * Math.max(0.01, n(def && def.dmg_mult, 1)));
  return {
    totalEnemyCount: count,
    totalEnemyHpPool: round(hpPool, 2),
    totalEnemyDamageOutputPotential: round(damagePotential, 2),
    movementPressure: round(count * Math.max(0.01, n(def && def.speed_mult, 1)), 2),
    effectivePower: round(count * (hpPool / Math.max(1, count) + ((damagePotential / Math.max(1, count)) * 0.5)), 2),
  };
}

function ensureTelemetry(game, options = {}) {
  if (!game)
    return null;
  if (!game.balanceTelemetry) {
    const startingGold = totalGold(game);
    game.balanceTelemetry = {
      schemaVersion: SCHEMA_VERSION,
      meta: {
        mode: options.mode || "multilane",
        map: options.map || null,
        playerCount: i(game.playerCount, 0),
        tickHz: Math.max(1, i(options.tickHz, DEFAULT_TICK_HZ)),
        matchSeed: game.matchSeed != null ? game.matchSeed : null,
        configVersionId: game.configVersionId != null ? game.configVersionId : null,
      },
      match: {
        startingEconomyReport: {
          startingGold,
          goldAvailableBeforeFirstWave: startingGold,
          spendingOptionsAvailableFromStartingGold: affordableSnapshot(game),
          numberOfUnitsAffordableAtStart: affordableSnapshot(game).unitsAffordableAtWaveStart,
          startingGoldSupportsBasicSurvivalPlan: affordableSnapshot(game).unitsAffordableAtWaveStart >= Math.max(1, i(game.playerCount, 1)),
        },
        totalGoldEarned: 0,
        totalGoldSpent: 0,
        peakFloatingGold: startingGold,
        minFloatingGold: startingGold,
      },
      unitInstances: {},
      unitTypeStats: {},
      waveReports: [],
      activeWave: null,
      pendingPreWavePurchases: [],
      pendingPreWaveSpend: 0,
      finalizedSummary: null,
      finalizedFlags: [],
      finalizedDiagnosis: null,
    };
  }
  return game.balanceTelemetry;
}

function currentWave(game) {
  const telemetry = ensureTelemetry(game);
  return telemetry ? telemetry.activeWave : null;
}

function matchGoldBounds(game) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return;
  const gold = totalGold(game);
  telemetry.match.peakFloatingGold = Math.max(telemetry.match.peakFloatingGold, gold);
  telemetry.match.minFloatingGold = Math.min(telemetry.match.minFloatingGold, gold);
}

function startWaveReport(game, context = {}) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return null;
  const army = armySnapshot(game);
  const upgrades = summarizeUpgradeValue(game);
  const strength = waveStrength(game, context.waveDef || null);
  const gold = totalGold(game);
  telemetry.activeWave = {
    schemaVersion: SCHEMA_VERSION,
    waveNumber: i(context.waveNumber, i(game && game.roundNumber, 1)),
    matchId: null,
    phaseBand: phaseBand(i(context.waveNumber, i(game && game.roundNumber, 1))),
    elapsedMatchTimeAtWaveStart: sec(game, i(game && game.tick, 0)),
    elapsedMatchTimeAtWaveEnd: sec(game, i(game && game.tick, 0)),
    totalWaveDuration: 0,
    timeToFirstContact: null,
    timeToFirstBreach: null,
    timeToClearWave: null,
    cleared: false,
    defeat: false,
    economy: {
      goldAtWaveStart: gold,
      goldEarnedDuringWave: 0,
      goldSpentDuringWave: 0,
      goldAtWaveEnd: gold,
      highestGoldHeldDuringWave: gold,
      lowestGoldHeldDuringWave: gold,
      incomeSourceBreakdown: { passiveIncome: 0, killRewards: 0, traderMarketEconomySources: 0, bonusOrEventIncome: 0, refunds: 0 },
      spendingBreakdown: { unitsPurchased: 0, buildingsPurchased: 0, upgradesPurchased: 0, repairs: 0, otherSpending: 0 },
      preWaveSpend: round(telemetry.pendingPreWaveSpend, 2),
      affordabilityAtWaveStart: affordableSnapshot(game),
      affordabilityAtWaveEnd: affordableSnapshot(game),
    },
    armyState: {
      unitsAliveAtWaveStart: army.playerAlive,
      unitsPurchasedDuringWave: 0,
      unitsSpawnedDuringWave: 0,
      unitsLostDuringWave: 0,
      unitsAliveAtWaveEnd: army.playerAlive,
      totalAliveArmyValueAtWaveStart: army.playerPowerTotal,
      totalAliveArmyValueAtWaveEnd: army.playerPowerTotal,
      totalArmyValuePurchasedDuringWave: 0,
      totalArmyValueLostDuringWave: 0,
    },
    armyComposition: { start: clone(army.composition), end: clone(army.composition) },
    combatResults: {
      enemiesSpawned: 0,
      enemiesKilled: 0,
      enemiesLeakedPastFrontline: 0,
      maxEnemiesAliveAtOnce: army.enemyAlive,
      totalPlayerDamageDealt: 0,
      totalEnemyDamageDealt: 0,
      wallDamageTaken: 0,
      towerDamageTaken: 0,
      townCoreDamageTaken: 0,
      otherStructuralDamageTaken: 0,
      healingDoneByPlayerUnits: 0,
    },
    pressure: {
      wallsWereBreached: false,
      anyEnemyReachedCoreRange: false,
      playerEnteredNearLossState: false,
      secondsUnderBreachPressure: 0,
      secondsEnemiesInsideFortressBounds: 0,
      secondsWithoutFrontline: 0,
      breachPressureTicks: 0,
      enemiesInsideTicks: 0,
      noFrontlineTicks: 0,
    },
    purchasingAndResponse: {
      preWavePurchases: clone(telemetry.pendingPreWavePurchases),
      purchases: [],
      purchasesBeforeBreachPressure: 0,
      purchasesDuringBreachPressure: 0,
      purchasesAfterBreachPressure: 0,
      proactiveSpend: 0,
      reactiveSpend: 0,
      unspentGoldAfterWave: gold,
    },
    powerCurve: {
      playerArmyValue: army.playerPowerTotal,
      playerUpgradeValue: upgrades.totalUpgradeValue,
      playerTotalEffectivePower: round(army.playerPowerTotal + upgrades.totalUpgradeValue, 2),
      enemyWaveEffectivePower: strength.effectivePower,
      playerToEnemyPowerRatio: strength.effectivePower > 0 ? round((army.playerPowerTotal + upgrades.totalUpgradeValue) / strength.effectivePower, 3) : null,
    },
    laneSnapshots: { start: clone(army.laneSnapshots), end: clone(army.laneSnapshots) },
    waveStrength: strength,
    derived: { struggleScore: 0, economicEfficiencyScore: 0, unitLossRatio: 0, goldFloatScore: 0, pressureScore: 0, stabilizationScore: 0, powerRatio: strength.effectivePower > 0 ? round((army.playerPowerTotal + upgrades.totalUpgradeValue) / strength.effectivePower, 3) : 0, powerDeltaVersusEnemyWave: 0, armyEfficiencyScore: 0, upgradeEfficiencyScore: 0, recoveryScore: 0 },
    internal: { startTick: i(game && game.tick, 0), firstContactTick: null, firstBreachTick: null, clearTick: null, firstPlayerDeathTick: null, frontlineCollapseTick: null, frontlineReplacementTick: null, firstReactivePurchaseTick: null },
  };
  telemetry.pendingPreWavePurchases = [];
  telemetry.pendingPreWaveSpend = 0;
  matchGoldBounds(game);
  return telemetry.activeWave;
}

function summarizeUpgradeValue(game) {
  let total = 0;
  const spendingByBuilding = { blacksmith: 0, archery_tower: 0, mage_tower: 0, temple: 0, stables: 0, town_core: 0, barracks: 0, market: 0, wall: 0 };
  for (const lane of game && Array.isArray(game.lanes) ? game.lanes : []) {
    if (!lane)
      continue;
    for (const pad of lane.fortressPads || []) {
      if (!pad)
        continue;
      const buildingType = String(pad.buildingType || "").trim().toLowerCase();
      const bucket = buildingType === "wizard_tower" ? "mage_tower" : buildingType === "stable" ? "stables" : buildingType;
      for (let nextTier = 2; nextTier <= Math.max(0, i(pad.tier, 0)); nextTier += 1) {
        const cost = Math.max(0, n(fortressSystem.getFortressUpgradeCost(buildingType, nextTier), 0));
        total += cost;
        spendingByBuilding[bucket] = (spendingByBuilding[bucket] || 0) + cost;
      }
    }
    for (const siteState of lane.barracksSiteStates && typeof lane.barracksSiteStates === "object" ? Object.values(lane.barracksSiteStates) : []) {
      if (!siteState)
        continue;
      for (let nextLevel = 2; nextLevel <= Math.max(0, i(siteState.level, 0)); nextLevel += 1) {
        const cost = Math.max(0, n(barracksSystem.getBarracksLevelDef(nextLevel).cost, 0));
        total += cost;
        spendingByBuilding.barracks += cost;
      }
    }
    for (const [buildingType, upgrades] of lane.buildingUpgradeState && typeof lane.buildingUpgradeState === "object" ? Object.entries(lane.buildingUpgradeState) : []) {
      const bucket = buildingType === "wizard_tower" ? "mage_tower" : buildingType === "stable" ? "stables" : buildingType;
      for (const upgradeState of Object.values(upgrades || {})) {
        const spent = Math.max(0, n(upgradeState && upgradeState.totalSpent, 0));
        total += spent;
        spendingByBuilding[bucket] = (spendingByBuilding[bucket] || 0) + spent;
      }
    }
  }
  return { totalUpgradeValue: round(total, 2), spendingByBuilding };
}

function typeStat(telemetry, unitType, category, team) {
  if (!telemetry.unitTypeStats[unitType]) {
    telemetry.unitTypeStats[unitType] = {
      unitType,
      category,
      team,
      totalGoldInvested: 0,
      totalUnitsPurchased: 0,
      totalUnitsFielded: 0,
      totalLifetimeDamageDealt: 0,
      totalLifetimeDamageAbsorbed: 0,
      totalHealingPerformed: 0,
      totalKills: 0,
      totalDeaths: 0,
      totalLifetimeTicks: 0,
    };
  }
  return telemetry.unitTypeStats[unitType];
}

function registerUnit(game, lane, unit) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry || !unit || !unit.id)
    return null;
  const unitId = String(unit.id);
  if (!telemetry.unitInstances[unitId]) {
    telemetry.unitInstances[unitId] = {
      unitId,
      unitType: String(unit.type || unit.unitTypeKey || "unknown"),
      category: unitCategory(unit),
      team: enemyUnit(unit) ? "enemy" : "player",
      laneIndex: i(lane && lane.laneIndex, -1),
      spawnTick: i(game && game.tick, 0),
      estimatedPower: unitPower(unit),
      damageDealt: 0,
      damageTaken: 0,
      healingDone: 0,
      kills: 0,
      deathTick: null,
    };
  }
  return telemetry.unitInstances[unitId];
}

function purchaseTiming(report) {
  if (!report)
    return "between_waves";
  if (!Number.isInteger(report.internal.firstBreachTick))
    return "before_breach";
  return report.pressure.breachPressureTicks > 0 ? "during_breach" : "after_breach";
}

function spendBucket(category) {
  const normalized = String(category || "").trim().toLowerCase();
  if (normalized === "units" || normalized === "unit" || normalized === "hero")
    return "unitsPurchased";
  if (normalized === "buildings" || normalized === "building")
    return "buildingsPurchased";
  if (normalized === "upgrades" || normalized === "upgrade")
    return "upgradesPurchased";
  if (normalized === "repairs" || normalized === "repair")
    return "repairs";
  return "otherSpending";
}

function recordIncome(game, lane, source, amount) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return;
  const safeAmount = round(Math.max(0, n(amount, 0)), 2);
  if (safeAmount <= 0)
    return;
  telemetry.match.totalGoldEarned += safeAmount;
  const report = currentWave(game);
  if (!report) {
    matchGoldBounds(game);
    return;
  }
  report.economy.goldEarnedDuringWave += safeAmount;
  if (source === "passive_income")
    report.economy.incomeSourceBreakdown.passiveIncome += safeAmount;
  else if (source === "kill_reward")
    report.economy.incomeSourceBreakdown.killRewards += safeAmount;
  else if (source === "market_income")
    report.economy.incomeSourceBreakdown.traderMarketEconomySources += safeAmount;
  else if (source === "refund")
    report.economy.incomeSourceBreakdown.refunds += safeAmount;
  else
    report.economy.incomeSourceBreakdown.bonusOrEventIncome += safeAmount;
  matchGoldBounds(game);
}

function recordSpend(game, lane, category, amount, context = {}) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return;
  const safeAmount = round(Math.max(0, n(amount, 0)), 2);
  if (safeAmount <= 0)
    return;
  telemetry.match.totalGoldSpent += safeAmount;
  const report = currentWave(game);
  const event = {
    tick: i(game && game.tick, 0),
    elapsedSeconds: sec(game, i(game && game.tick, 0)),
    laneIndex: i(lane && lane.laneIndex, -1),
    category: String(category || "").trim().toLowerCase(),
    amount: safeAmount,
    buildingType: context.buildingType || null,
    padId: context.padId || null,
    barracksId: context.barracksId || null,
    unitType: context.unitType || null,
    rosterKey: context.rosterKey || null,
    upgradeKey: context.upgradeKey || null,
    timing: purchaseTiming(report),
    proactive: report ? !Number.isInteger(report.internal.firstBreachTick) && report.pressure.noFrontlineTicks <= 0 : true,
    playerPowerAfterPurchase: round(armySnapshot(game).playerPowerTotal + summarizeUpgradeValue(game).totalUpgradeValue, 2),
  };
  if (!report) {
    telemetry.pendingPreWavePurchases.push(event);
    telemetry.pendingPreWaveSpend += safeAmount;
    matchGoldBounds(game);
    return;
  }
  report.economy.goldSpentDuringWave += safeAmount;
  report.economy.spendingBreakdown[spendBucket(category)] += safeAmount;
  report.purchasingAndResponse.purchases.push(event);
  if (event.timing === "before_breach")
    report.purchasingAndResponse.purchasesBeforeBreachPressure += 1;
  else if (event.timing === "during_breach")
    report.purchasingAndResponse.purchasesDuringBreachPressure += 1;
  else if (event.timing === "after_breach")
    report.purchasingAndResponse.purchasesAfterBreachPressure += 1;
  if (event.proactive)
    report.purchasingAndResponse.proactiveSpend += safeAmount;
  else
    report.purchasingAndResponse.reactiveSpend += safeAmount;
  if (!event.proactive && Number.isInteger(report.internal.firstBreachTick) && !Number.isInteger(report.internal.firstReactivePurchaseTick))
    report.internal.firstReactivePurchaseTick = i(game && game.tick, 0);
  if (event.category === "unit" || event.category === "units" || event.category === "hero") {
    report.armyState.unitsPurchasedDuringWave += Math.max(1, i(context.count, 1));
    report.armyState.totalArmyValuePurchasedDuringWave += round(n(context.estimatedPowerGain, safeAmount), 2);
    const unitType = String(context.unitType || context.rosterKey || "").trim();
    if (unitType) {
      const stat = typeStat(telemetry, unitType, unitCategory({ type: unitType, archetypeKey: unitType, isHero: event.category === "hero" }), "player");
      stat.totalGoldInvested += safeAmount;
      stat.totalUnitsPurchased += Math.max(1, i(context.count, 1));
    }
  }
  matchGoldBounds(game);
}

function recordUnitSpawned(game, lane, unit) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry || !unit)
    return;
  registerUnit(game, lane, unit);
  const report = currentWave(game);
  if (!report)
    return;
  if (enemyUnit(unit))
    report.combatResults.enemiesSpawned += 1;
  else {
    report.armyState.unitsSpawnedDuringWave += 1;
    typeStat(telemetry, String(unit.type || unit.unitTypeKey || "unknown"), unitCategory(unit), "player").totalUnitsFielded += 1;
  }
}

function recordDamage(game, attackerLane, attacker, targetLane, target, amount, context = {}) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return;
  const safeAmount = round(Math.max(0, n(amount, 0)), 2);
  if (safeAmount <= 0)
    return;
  const report = currentWave(game);
  if (!report)
    return;
  const attackerTeam = context.attackerTeam || (enemyUnit(attacker) ? "enemy" : "player");
  const attackerType = String(attacker && (attacker.type || attacker.unitTypeKey || context.projectileType) || "unknown");
  const stat = typeStat(telemetry, attackerType, unitCategory(attacker || { type: attackerType }), attackerTeam);
  stat.totalLifetimeDamageDealt += safeAmount;
  if (attackerTeam === "enemy")
    report.combatResults.totalEnemyDamageDealt += safeAmount;
  else
    report.combatResults.totalPlayerDamageDealt += safeAmount;
  if (target && target.id) {
    const targetInstance = registerUnit(game, targetLane || attackerLane, target);
    if (targetInstance)
      targetInstance.damageTaken += safeAmount;
    typeStat(telemetry, String(target.type || target.unitTypeKey || "unknown"), unitCategory(target), enemyUnit(target) ? "enemy" : "player").totalLifetimeDamageAbsorbed += safeAmount;
    target._rfLastDamager = { unitType: attackerType, attackerTeam };
  }
  if (!Number.isInteger(report.internal.firstContactTick))
    report.internal.firstContactTick = i(game && game.tick, 0);
}

function recordHealing(game, lane, healer, target, amount) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry || !playerUnit(healer))
    return;
  const safeAmount = round(Math.max(0, n(amount, 0)), 2);
  if (safeAmount <= 0)
    return;
  const report = currentWave(game);
  if (!report)
    return;
  report.combatResults.healingDoneByPlayerUnits += safeAmount;
  const instance = registerUnit(game, lane, healer);
  if (instance)
    instance.healingDone += safeAmount;
  typeStat(telemetry, String(healer.type || healer.unitTypeKey || "unknown"), unitCategory(healer), "player").totalHealingPerformed += safeAmount;
}

function recordStructureDamage(game, lane, buildingType, amount, context = {}) {
  const report = currentWave(game);
  if (!report)
    return;
  const safeAmount = round(Math.max(0, n(amount, 0)), 2);
  if (safeAmount <= 0)
    return;
  const bucket = structureBucket(buildingType);
  if (bucket === "wall")
    report.combatResults.wallDamageTaken += safeAmount;
  else if (bucket === "tower")
    report.combatResults.towerDamageTaken += safeAmount;
  else if (bucket === "core")
    report.combatResults.townCoreDamageTaken += safeAmount;
  else
    report.combatResults.otherStructuralDamageTaken += safeAmount;
  if (bucket === "wall")
    report.pressure.wallsWereBreached = true;
  if (bucket === "core")
    report.pressure.anyEnemyReachedCoreRange = true;
  if (context.attacker)
    recordDamage(game, lane, context.attacker, lane, null, safeAmount, { attackerTeam: enemyUnit(context.attacker) ? "enemy" : "player" });
  if (!Number.isInteger(report.internal.firstContactTick))
    report.internal.firstContactTick = i(game && game.tick, 0);
}

function recordLeak(game) {
  const report = currentWave(game);
  if (!report)
    return;
  report.combatResults.enemiesLeakedPastFrontline += 1;
  report.pressure.wallsWereBreached = true;
  report.pressure.anyEnemyReachedCoreRange = true;
  if (!Number.isInteger(report.internal.firstBreachTick))
    report.internal.firstBreachTick = i(game && game.tick, 0);
}

function recordUnitDeath(game, lane, unit) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry || !unit)
    return;
  const instance = registerUnit(game, lane, unit);
  if (instance && !Number.isInteger(instance.deathTick))
    instance.deathTick = i(game && game.tick, 0);
  const report = currentWave(game);
  const team = enemyUnit(unit) ? "enemy" : "player";
  const stat = typeStat(telemetry, String(unit.type || unit.unitTypeKey || "unknown"), unitCategory(unit), team);
  stat.totalDeaths += 1;
  stat.totalLifetimeTicks += Math.max(0, i(game && game.tick, 0) - i(instance && instance.spawnTick, i(game && game.tick, 0)));
  if (report) {
    if (team === "enemy") {
      report.combatResults.enemiesKilled += 1;
    } else {
      report.armyState.unitsLostDuringWave += 1;
      report.armyState.totalArmyValueLostDuringWave += round(instance ? instance.estimatedPower : unitPower(unit), 2);
      if (!Number.isInteger(report.internal.firstPlayerDeathTick))
        report.internal.firstPlayerDeathTick = i(game && game.tick, 0);
    }
  }
  if (unit._rfLastDamager && unit._rfLastDamager.unitType) {
    typeStat(telemetry, String(unit._rfLastDamager.unitType), unitCategory({ type: unit._rfLastDamager.unitType }), unit._rfLastDamager.attackerTeam || "player").totalKills += 1;
  }
}

function remainingWaveUnits(game) {
  let remaining = 0;
  for (const lane of game && Array.isArray(game.lanes) ? game.lanes : []) {
    remaining += (lane.spawnQueue || []).filter((unit) => enemyUnit(unit)).length;
    remaining += (lane.units || []).filter((unit) => unit && n(unit.hp, 0) > 0 && enemyUnit(unit)).length;
  }
  return remaining;
}

function recordTick(game) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return;
  matchGoldBounds(game);
  const report = currentWave(game);
  if (!report)
    return;
  const army = armySnapshot(game);
  const gold = totalGold(game);
  report.economy.highestGoldHeldDuringWave = Math.max(report.economy.highestGoldHeldDuringWave, gold);
  report.economy.lowestGoldHeldDuringWave = Math.min(report.economy.lowestGoldHeldDuringWave, gold);
  report.combatResults.maxEnemiesAliveAtOnce = Math.max(report.combatResults.maxEnemiesAliveAtOnce, army.enemyAlive);
  let enemiesInside = 0;
  for (const lane of game.lanes || []) {
    if (n(lane && lane.lives, 0) <= 3)
      report.pressure.playerEnteredNearLossState = true;
    for (const unit of lane.units || []) {
      if (unit && n(unit.hp, 0) > 0 && enemyUnit(unit) && (unit.blockedByStructure || unit.hasBreachedTownCore))
        enemiesInside += 1;
    }
  }
  if (enemiesInside > 0) {
    report.pressure.enemiesInsideTicks += 1;
    report.pressure.breachPressureTicks += 1;
  }
  if (army.frontlineCount <= 0) {
    report.pressure.noFrontlineTicks += 1;
    if (!Number.isInteger(report.internal.frontlineCollapseTick))
      report.internal.frontlineCollapseTick = i(game && game.tick, 0);
  } else if (Number.isInteger(report.internal.frontlineCollapseTick) && !Number.isInteger(report.internal.frontlineReplacementTick)) {
    report.internal.frontlineReplacementTick = i(game && game.tick, 0);
  }
  if (!Number.isInteger(report.internal.clearTick) && remainingWaveUnits(game) <= 0 && !game.activeWaveSession)
    report.internal.clearTick = i(game && game.tick, 0);
}

function finalizeWaveReport(game, context = {}) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry || !telemetry.activeWave)
    return null;
  const report = telemetry.activeWave;
  const army = armySnapshot(game);
  const upgrades = summarizeUpgradeValue(game);
  report.defeat = !!context.defeat;
  report.cleared = !!context.cleared || (!report.defeat && remainingWaveUnits(game) <= 0);
  report.elapsedMatchTimeAtWaveEnd = sec(game, i(game && game.tick, 0));
  report.totalWaveDuration = round(report.elapsedMatchTimeAtWaveEnd - report.elapsedMatchTimeAtWaveStart, 2);
  report.timeToFirstContact = Number.isInteger(report.internal.firstContactTick) ? sec(game, report.internal.firstContactTick - report.internal.startTick) : null;
  report.timeToFirstBreach = Number.isInteger(report.internal.firstBreachTick) ? sec(game, report.internal.firstBreachTick - report.internal.startTick) : null;
  report.timeToClearWave = Number.isInteger(report.internal.clearTick) ? sec(game, report.internal.clearTick - report.internal.startTick) : (report.cleared ? report.totalWaveDuration : null);
  report.economy.goldAtWaveEnd = totalGold(game);
  report.economy.affordabilityAtWaveEnd = affordableSnapshot(game);
  report.armyState.unitsAliveAtWaveEnd = army.playerAlive;
  report.armyState.totalAliveArmyValueAtWaveEnd = army.playerPowerTotal;
  report.armyComposition.end = clone(army.composition);
  report.laneSnapshots.end = clone(army.laneSnapshots);
  report.purchasingAndResponse.unspentGoldAfterWave = report.economy.goldAtWaveEnd;
  report.pressure.secondsUnderBreachPressure = sec(game, report.pressure.breachPressureTicks);
  report.pressure.secondsEnemiesInsideFortressBounds = sec(game, report.pressure.enemiesInsideTicks);
  report.pressure.secondsWithoutFrontline = sec(game, report.pressure.noFrontlineTicks);
  report.powerCurve.playerArmyValue = army.playerPowerTotal;
  report.powerCurve.playerUpgradeValue = upgrades.totalUpgradeValue;
  report.powerCurve.playerTotalEffectivePower = round(army.playerPowerTotal + upgrades.totalUpgradeValue, 2);
  report.powerCurve.playerToEnemyPowerRatio = report.waveStrength.effectivePower > 0 ? round((army.playerPowerTotal + upgrades.totalUpgradeValue) / report.waveStrength.effectivePower, 3) : null;
  const structural = report.combatResults.wallDamageTaken + report.combatResults.towerDamageTaken + report.combatResults.townCoreDamageTaken + report.combatResults.otherStructuralDamageTaken;
  const lossRatio = report.armyState.unitsAliveAtWaveStart + report.armyState.unitsSpawnedDuringWave > 0
    ? report.armyState.unitsLostDuringWave / Math.max(1, report.armyState.unitsAliveAtWaveStart + report.armyState.unitsSpawnedDuringWave)
    : 0;
  const pressureScore = clamp((clamp(structural / 15, 0, 1) * 45) + (clamp(report.pressure.secondsUnderBreachPressure / 12, 0, 1) * 25) + (report.pressure.anyEnemyReachedCoreRange ? 15 : 0) + (clamp(report.combatResults.totalEnemyDamageDealt / 120, 0, 1) * 15), 0, 100);
  const struggleScore = clamp((lossRatio * 45) + (clamp(structural / 12, 0, 1) * 25) + (clamp((report.timeToClearWave || report.totalWaveDuration || 0) / 45, 0, 1) * 10) + (pressureScore * 0.2), 0, 100);
  report.derived.struggleScore = round(struggleScore, 2);
  report.derived.economicEfficiencyScore = round(clamp((clamp(report.economy.goldSpentDuringWave / Math.max(1, report.economy.goldEarnedDuringWave + report.economy.preWaveSpend), 0, 1) * 70) + (clamp((report.armyState.totalAliveArmyValueAtWaveEnd - report.armyState.totalAliveArmyValueAtWaveStart + report.armyState.totalArmyValueLostDuringWave) / Math.max(1, report.armyState.totalArmyValuePurchasedDuringWave || 1), 0, 1) * 30), 0, 100), 2);
  report.derived.unitLossRatio = round(lossRatio, 3);
  report.derived.goldFloatScore = round(clamp((clamp(report.economy.goldAtWaveEnd / 1000, 0, 1) * 65) + (clamp(report.economy.highestGoldHeldDuringWave / 1500, 0, 1) * 35), 0, 100), 2);
  report.derived.pressureScore = round(pressureScore, 2);
  report.derived.stabilizationScore = round(clamp(100 - (pressureScore * 0.45) - (struggleScore * 0.35) + (clamp(n(report.powerCurve.playerToEnemyPowerRatio, 0) / 1.5, 0, 1) * 25), 0, 100), 2);
  report.derived.powerRatio = report.powerCurve.playerToEnemyPowerRatio || 0;
  report.derived.powerDeltaVersusEnemyWave = round(report.powerCurve.playerTotalEffectivePower - report.powerCurve.enemyWaveEffectivePower, 2);
  report.derived.armyEfficiencyScore = report.armyState.totalArmyValuePurchasedDuringWave > 0 ? round((report.combatResults.totalPlayerDamageDealt + (report.combatResults.healingDoneByPlayerUnits * 0.6)) / report.armyState.totalArmyValuePurchasedDuringWave, 2) : 0;
  report.derived.upgradeEfficiencyScore = report.economy.spendingBreakdown.upgradesPurchased > 0 ? round((100 - struggleScore) / (report.economy.spendingBreakdown.upgradesPurchased / 100), 2) : 0;
  report.derived.recoveryScore = round(clamp(100 - struggleScore + (report.pressure.playerEnteredNearLossState ? -10 : 10), 0, 100), 2);
  telemetry.waveReports.push(clone(report));
  telemetry.activeWave = null;
  return telemetry.waveReports[telemetry.waveReports.length - 1];
}

function diagnosticsLabel(score, hard, easy) {
  if (score >= hard)
    return "too hard";
  if (score <= easy)
    return "too easy";
  return "acceptable";
}

function firstRun(waves, predicate, needed = 2) {
  let streak = 0;
  for (const wave of waves) {
    if (predicate(wave)) {
      streak += 1;
      if (streak >= needed)
        return wave.waveNumber - needed + 1;
    } else {
      streak = 0;
    }
  }
  return null;
}

function summaryFlags(summary, waves) {
  const flags = [];
  const early = waves.filter((wave) => wave.phaseBand === "early");
  const earlyStruggle = avg(early.map((wave) => wave.derived.struggleScore));
  if (earlyStruggle >= 65 || early.some((wave) => wave.pressure.playerEnteredNearLossState))
    flags.push({ key: "early_game_too_hard", severity: "high", wave: 1 });
  if (earlyStruggle <= 25 && avg(early.map((wave) => wave.derived.goldFloatScore)) >= 40)
    flags.push({ key: "early_game_too_easy", severity: "medium", wave: 1 });
  const snowballWave = firstRun(waves, (wave) => n(wave.powerCurve.playerToEnemyPowerRatio, 0) >= 1.35 && wave.derived.goldFloatScore >= 45 && wave.derived.pressureScore <= 25, 2);
  if (snowballWave)
    flags.push({ key: "snowball_detected", severity: "high", wave: snowballWave });
  const overflowWave = firstRun(waves, (wave) => wave.derived.goldFloatScore >= 55, 2);
  if (overflowWave)
    flags.push({ key: "economy_overflow", severity: "medium", wave: overflowWave });
  if (summary.economy.noMeaningfulDecisionPressure)
    flags.push({ key: "no_meaningful_decision_pressure", severity: "medium", wave: 1 });
  if (!summary.economy.startingEconomyReport.startingGoldSupportsBasicSurvivalPlan)
    flags.push({ key: "forced_starvation", severity: "high", wave: 1 });
  for (let index = 1; index < waves.length; index += 1) {
    if (waves[index].derived.struggleScore - waves[index - 1].derived.struggleScore >= 30) {
      flags.push({ key: "wave_scaling_too_sharp", severity: "medium", wave: waves[index].waveNumber });
      break;
    }
  }
  if (firstRun(waves, (wave) => wave.derived.pressureScore <= 20, 3))
    flags.push({ key: "wave_scaling_too_weak", severity: "medium", wave: firstRun(waves, (wave) => wave.derived.pressureScore <= 20, 3) });
  if (waves.some((wave, index) => wave.derived.struggleScore >= 55 && waves[index + 1] && waves[index + 1].derived.struggleScore <= 25 && waves[index + 1].derived.goldFloatScore >= 45))
    flags.push({ key: "recovery_too_easy", severity: "medium", wave: waves.find((wave) => wave.derived.struggleScore >= 55)?.waveNumber || 1 });
  if (waves.some((wave, index) => wave.derived.struggleScore >= 70 && waves.slice(index + 1, index + 3).every((later) => later && later.derived.struggleScore >= 60)))
    flags.push({ key: "recovery_impossible", severity: "high", wave: waves.find((wave) => wave.derived.struggleScore >= 70)?.waveNumber || 1 });
  return flags;
}

function finalizeMatch(game, context = {}) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return { waveReports: [], summary: null, flags: [], diagnosis: null };
  if (telemetry.activeWave)
    finalizeWaveReport(game, { defeat: !!context.defeat, cleared: !context.defeat });
  const waves = telemetry.waveReports || [];
  const upgrades = summarizeUpgradeValue(game);
  const early = avg(waves.filter((wave) => wave.phaseBand === "early").map((wave) => wave.derived.struggleScore));
  const mid = avg(waves.filter((wave) => wave.phaseBand === "mid").map((wave) => wave.derived.struggleScore));
  const late = avg(waves.filter((wave) => wave.phaseBand === "late").map((wave) => wave.derived.struggleScore));
  const stabilizationWave = firstRun(waves, (wave) => wave.combatResults.wallDamageTaken <= 0 && wave.derived.struggleScore <= 35, 2);
  const snowballWave = firstRun(waves, (wave) => n(wave.powerCurve.playerToEnemyPowerRatio, 0) >= 1.35 && wave.derived.goldFloatScore >= 45 && wave.derived.pressureScore <= 25, 2);
  const typeStats = Object.fromEntries(Object.entries(telemetry.unitTypeStats).map(([unitType, stat]) => [unitType, { ...stat, averageSurvivalTimeSeconds: round(sec({ balanceTelemetry: telemetry }, stat.totalLifetimeTicks) / Math.max(1, stat.totalDeaths || stat.totalUnitsFielded || 1), 2), goldEfficiencyScore: stat.totalGoldInvested > 0 ? round((stat.totalLifetimeDamageDealt + (stat.totalHealingPerformed * 0.6)) / stat.totalGoldInvested, 3) : 0, combatEfficiencyScore: stat.totalUnitsFielded > 0 ? round((stat.totalLifetimeDamageDealt + stat.totalHealingPerformed + (stat.totalKills * 15)) / stat.totalUnitsFielded, 3) : 0 }]));
  const summary = {
    schemaVersion: SCHEMA_VERSION,
    matchId: null,
    matchIdentity: { mode: telemetry.meta.mode || "multilane", map: telemetry.meta.map || null, playerCount: telemetry.meta.playerCount, aiEnemyConfiguration: context.aiEnemyConfiguration || "dungeon_wave_schedule", finalResult: context.finalResult || "completed", finalWaveReached: waves.length > 0 ? waves[waves.length - 1].waveNumber : 0, totalMatchDuration: round(sec(game, i(game && game.tick, 0)), 2), matchSeed: telemetry.meta.matchSeed, configVersionId: telemetry.meta.configVersionId },
    economy: {
      startingEconomyReport: telemetry.match.startingEconomyReport,
      totalGoldEarned: round(telemetry.match.totalGoldEarned, 2),
      totalGoldSpent: round(telemetry.match.totalGoldSpent, 2),
      totalGoldUnspentAtMatchEnd: waves.length > 0 ? waves[waves.length - 1].economy.goldAtWaveEnd : totalGold(game),
      peakFloatingGold: round(telemetry.match.peakFloatingGold, 2),
      averageGoldEarnedPerWave: round(avg(waves.map((wave) => wave.economy.goldEarnedDuringWave)), 2),
      averageGoldSpentPerWave: round(avg(waves.map((wave) => wave.economy.goldSpentDuringWave)), 2),
      percentageEarnedGoldSpent: telemetry.match.totalGoldEarned > 0 ? round((telemetry.match.totalGoldSpent / telemetry.match.totalGoldEarned) * 100, 2) : 0,
      percentageWavesEndingWithExcessUnspentGold: waves.length > 0 ? round((waves.filter((wave) => wave.economy.goldAtWaveEnd >= 500).length / waves.length) * 100, 2) : 0,
      goldFloatThresholdHits: Object.fromEntries(GOLD_FLOAT_THRESHOLDS.map((threshold) => [threshold, waves.filter((wave) => wave.economy.goldAtWaveEnd >= threshold).length])),
      noMeaningfulDecisionPressure: waves.filter((wave) => wave.economy.affordabilityAtWaveStart.couldBuyBothFreely && wave.derived.goldFloatScore >= 45 && wave.economy.spendingBreakdown.unitsPurchased > 0 && wave.economy.spendingBreakdown.upgradesPurchased > 0).length >= 2,
    },
    army: {
      totalUnitsPurchased: sum(waves.map((wave) => wave.armyState.unitsPurchasedDuringWave)),
      totalUnitsSpawned: sum(waves.map((wave) => wave.armyState.unitsSpawnedDuringWave)),
      totalUnitsLost: sum(waves.map((wave) => wave.armyState.unitsLostDuringWave)),
      peakUnitCount: Math.max(0, ...waves.map((wave) => Math.max(wave.armyState.unitsAliveAtWaveStart, wave.armyState.unitsAliveAtWaveEnd))),
      averageUnitCountAcrossMatch: round(avg(waves.map((wave) => wave.armyState.unitsAliveAtWaveEnd)), 2),
      peakAliveArmyValue: round(Math.max(0, ...waves.map((wave) => wave.armyState.totalAliveArmyValueAtWaveEnd)), 2),
      averageAliveArmyValueAcrossMatch: round(avg(waves.map((wave) => wave.armyState.totalAliveArmyValueAtWaveEnd)), 2),
      totalArmyValueLostDuringMatch: round(sum(waves.map((wave) => wave.armyState.totalArmyValueLostDuringWave)), 2),
      unitEfficiencyReport: typeStats,
    },
    combat: {
      averageWaveClearTime: round(avg(waves.map((wave) => wave.timeToClearWave || wave.totalWaveDuration)), 2),
      slowestClearTime: round(Math.max(0, ...waves.map((wave) => wave.timeToClearWave || 0)), 2),
      fastestClearTime: round(Math.min(...waves.map((wave) => wave.timeToClearWave || wave.totalWaveDuration || 0).filter((value) => value > 0), Infinity), 2),
      totalWallDamageTaken: round(sum(waves.map((wave) => wave.combatResults.wallDamageTaken)), 2),
      totalTowerDamageTaken: round(sum(waves.map((wave) => wave.combatResults.towerDamageTaken)), 2),
      totalTownCoreDamageTaken: round(sum(waves.map((wave) => wave.combatResults.townCoreDamageTaken)), 2),
      numberOfWavesWithBreach: waves.filter((wave) => wave.pressure.wallsWereBreached || wave.combatResults.townCoreDamageTaken > 0).length,
      numberOfWavesWithNearLossPressure: waves.filter((wave) => wave.pressure.playerEnteredNearLossState).length,
      numberOfWavesWithZeroMeaningfulPressure: waves.filter((wave) => wave.derived.pressureScore <= 15).length,
    },
    upgrades: { totalUpgradeGoldSpent: round(sum(waves.map((wave) => wave.economy.spendingBreakdown.upgradesPurchased)), 2), spendingByBuilding: upgrades.spendingByBuilding, estimatedPowerGainedFromUpgradesOverMatch: upgrades.totalUpgradeValue },
    timeline: { powerCurveReport: waves.map((wave) => ({ waveNumber: wave.waveNumber, playerArmyValue: wave.powerCurve.playerArmyValue, playerUpgradeValue: wave.powerCurve.playerUpgradeValue, playerTotalEffectivePower: wave.powerCurve.playerTotalEffectivePower, enemyWaveEffectivePower: wave.powerCurve.enemyWaveEffectivePower, playerToEnemyPowerRatio: wave.powerCurve.playerToEnemyPowerRatio })), stabilizationReport: { firstWaveStableWithoutMeaningfulWallDamage: stabilizationWave, firstWaveClearTimesTrendDown: firstRun(waves.slice(1), (wave) => { const previous = waves.find((entry) => entry.waveNumber === wave.waveNumber - 1); return previous && n(wave.timeToClearWave, Infinity) < n(previous.timeToClearWave, Infinity); }, 2), firstWaveExcessGoldBecomesConsistent: firstRun(waves, (wave) => wave.economy.goldAtWaveEnd >= 500, 2), firstWaveLossesStopMattering: firstRun(waves, (wave) => wave.derived.unitLossRatio <= 0.1, 2) }, snowballOnsetReport: { firstWavePowerRatioExceedsTarget: firstRun(waves, (wave) => n(wave.powerCurve.playerToEnemyPowerRatio, 0) >= 1.35, 2), firstWaveLossesTrendDown: firstRun(waves, (wave) => wave.derived.unitLossRatio <= 0.2, 2), firstWaveUnspentGoldTrendsUp: firstRun(waves, (wave) => wave.derived.goldFloatScore >= 45, 2), firstWaveBreachRiskDisappears: firstRun(waves, (wave) => !wave.pressure.wallsWereBreached && wave.derived.pressureScore <= 20, 2) }, trendDatasetRow: { mode: telemetry.meta.mode || "multilane", finalWaveReached: waves.length > 0 ? waves[waves.length - 1].waveNumber : 0, totalMatchDuration: round(sec(game, i(game && game.tick, 0)), 2), stabilizationWave, snowballWave, earlyGameDifficultyScore: round(early, 2), midGameDifficultyScore: round(mid, 2), lateGameDifficultyScore: round(late, 2), averagePlayerPowerRatio: round(avg(waves.map((wave) => wave.powerCurve.playerToEnemyPowerRatio)), 3), peakGoldFloat: round(telemetry.match.peakFloatingGold, 2), peakUnitCount: Math.max(0, ...waves.map((wave) => Math.max(wave.armyState.unitsAliveAtWaveStart, wave.armyState.unitsAliveAtWaveEnd))), economyOverflowWaveCount: waves.filter((wave) => wave.derived.goldFloatScore >= 55).length } },
    balanceRatings: { earlyGameDifficultyRating: diagnosticsLabel(early, 65, 25), midGameDifficultyRating: diagnosticsLabel(mid || early, 65, 30), lateGameDifficultyRating: diagnosticsLabel(late || mid || early, 70, 30), snowballRiskRating: snowballWave != null ? "high" : avg(waves.map((wave) => wave.powerCurve.playerToEnemyPowerRatio)) >= 1.2 ? "medium" : "low", economyOverflowRating: waves.filter((wave) => wave.derived.goldFloatScore >= 55).length >= 2 ? "high" : avg(waves.map((wave) => wave.economy.goldAtWaveEnd)) >= 300 ? "medium" : "low", upgradePacingRating: sum(waves.map((wave) => wave.economy.spendingBreakdown.upgradesPurchased)) > 0 && snowballWave != null ? "too fast" : "acceptable", unitAffordabilityRating: telemetry.match.startingEconomyReport.startingGoldSupportsBasicSurvivalPlan ? (waves.filter((wave) => wave.derived.goldFloatScore >= 55).length >= 2 ? "too cheap" : "acceptable") : "too expensive", enemyScalingAdequacyRating: avg(waves.map((wave) => wave.powerCurve.playerToEnemyPowerRatio)) >= 1.4 ? "too weak" : avg(waves.map((wave) => wave.powerCurve.playerToEnemyPowerRatio)) <= 0.8 ? "too strong" : "acceptable" },
    readable: { perWaveLog: waves.map((wave) => `Wave ${wave.waveNumber} (${wave.phaseBand}) gold ${wave.economy.goldAtWaveStart}->${wave.economy.goldAtWaveEnd}, clear ${wave.timeToClearWave != null ? `${wave.timeToClearWave}s` : "uncleared"}, losses ${wave.armyState.unitsLostDuringWave}, pressure ${wave.derived.pressureScore}, struggle ${wave.derived.struggleScore}, ratio ${wave.powerCurve.playerToEnemyPowerRatio != null ? wave.powerCurve.playerToEnemyPowerRatio : "n/a"}`), matchAAR: [], diagnosis: [] },
  };
  const flags = summaryFlags(summary, waves);
  const flagSet = new Set(flags.map((flag) => flag.key));
  const diagnosis = { earlyGame: summary.balanceRatings.earlyGameDifficultyRating, midGame: summary.balanceRatings.midGameDifficultyRating, lateGame: summary.balanceRatings.lateGameDifficultyRating, snowballDetected: flagSet.has("snowball_detected"), economyOverflow: flagSet.has("economy_overflow"), likelyTuningTargets: [] };
  if (flagSet.has("early_game_too_hard"))
    diagnosis.likelyTuningTargets.push("Increase opening economy flexibility or soften wave 1-3 pressure.");
  if (flagSet.has("forced_starvation"))
    diagnosis.likelyTuningTargets.push("Raise starting gold or lower early survival costs.");
  if (flagSet.has("early_game_too_easy"))
    diagnosis.likelyTuningTargets.push("Increase opening wave pressure or trim starting economy.");
  if (flagSet.has("economy_overflow"))
    diagnosis.likelyTuningTargets.push("Slow income ramp or add stronger spend sinks.");
  if (flagSet.has("snowball_detected"))
    diagnosis.likelyTuningTargets.push("Strengthen mid/late wave scaling or slow player scaling spikes.");
  if (flagSet.has("wave_scaling_too_weak"))
    diagnosis.likelyTuningTargets.push("Increase late-wave count, HP, armor, or composition pressure.");
  if (flagSet.has("wave_scaling_too_sharp"))
    diagnosis.likelyTuningTargets.push("Smooth adjacent wave jumps so the curve does not cliff.");
  if (diagnosis.likelyTuningTargets.length <= 0)
    diagnosis.likelyTuningTargets.push("No urgent balance flag triggered in this sample.");
  summary.readable.matchAAR = [`Result: ${summary.matchIdentity.finalResult} on wave ${summary.matchIdentity.finalWaveReached}.`, `Economy: earned ${summary.economy.totalGoldEarned}, spent ${summary.economy.totalGoldSpent}, peak float ${summary.economy.peakFloatingGold}.`, `Pressure: ${summary.combat.numberOfWavesWithBreach} breach waves, ${summary.combat.numberOfWavesWithNearLossPressure} near-loss waves.`, `Power curve: stabilize around wave ${stabilizationWave ?? "n/a"}, snowball around wave ${snowballWave ?? "n/a"}.`];
  summary.readable.diagnosis = [`Early game: ${diagnosis.earlyGame}.`, `Mid game: ${diagnosis.midGame}.`, `Late game: ${diagnosis.lateGame}.`, `Snowball detected: ${diagnosis.snowballDetected ? "yes" : "no"}.`, `Economy overflow: ${diagnosis.economyOverflow ? "yes" : "no"}.`, `Likely tuning targets: ${diagnosis.likelyTuningTargets.join(" ")}`];
  telemetry.finalizedSummary = summary;
  telemetry.finalizedFlags = flags;
  telemetry.finalizedDiagnosis = diagnosis;
  return { waveReports: clone(waves), summary: clone(summary), flags: clone(flags), diagnosis: clone(diagnosis) };
}

function getFinalizedBalanceData(game) {
  const telemetry = ensureTelemetry(game);
  if (!telemetry)
    return { waveReports: [], summary: null, flags: [], diagnosis: null };
  return telemetry.finalizedSummary ? { waveReports: clone(telemetry.waveReports), summary: clone(telemetry.finalizedSummary), flags: clone(telemetry.finalizedFlags), diagnosis: clone(telemetry.finalizedDiagnosis) } : finalizeMatch(game, {});
}

function attachMatchId(payload, matchId) {
  if (!matchId || payload == null)
    return payload;
  if (Array.isArray(payload))
    return payload.map((entry) => attachMatchId(entry, matchId));
  if (typeof payload !== "object")
    return payload;
  const cloned = { ...payload };
  if (Object.prototype.hasOwnProperty.call(cloned, "waveNumber") || Object.prototype.hasOwnProperty.call(cloned, "matchIdentity"))
    cloned.matchId = matchId;
  if (cloned.matchIdentity && typeof cloned.matchIdentity === "object")
    cloned.matchIdentity = { ...cloned.matchIdentity, matchId };
  return cloned;
}

function buildMultiMatchBalanceReport(matchRows) {
  const rows = Array.isArray(matchRows) ? matchRows : [];
  const trendRows = rows.filter((row) => row && row.balance_summary && row.balance_summary.timeline && row.balance_summary.timeline.trendDatasetRow).map((row) => ({ matchId: row.id, startedAt: row.started_at || null, endedAt: row.ended_at || null, mode: row.mode || null, flags: Array.isArray(row.balance_flags) ? row.balance_flags : [], trend: row.balance_summary.timeline.trendDatasetRow, diagnosis: row.balance_summary.readable && row.balance_summary.readable.diagnosis ? row.balance_summary.readable.diagnosis : [] }));
  return {
    aggregate: {
      sampleSize: trendRows.length,
      averageFinalWaveReached: round(avg(trendRows.map((row) => row.trend.finalWaveReached)), 2),
      averageWaveWherePlayerStabilizes: round(avg(trendRows.map((row) => row.trend.stabilizationWave)), 2),
      averageWaveWhereSnowballBegins: round(avg(trendRows.map((row) => row.trend.snowballWave)), 2),
      averageEarlyGameDifficultyScore: round(avg(trendRows.map((row) => row.trend.earlyGameDifficultyScore)), 2),
      averagePeakGoldFloat: round(avg(trendRows.map((row) => row.trend.peakGoldFloat)), 2),
      averagePeakUnitCount: round(avg(trendRows.map((row) => row.trend.peakUnitCount)), 2),
      snowballRate: trendRows.length > 0 ? round((trendRows.filter((row) => row.flags.some((flag) => flag.key === "snowball_detected")).length / trendRows.length) * 100, 2) : 0,
      economyOverflowRate: trendRows.length > 0 ? round((trendRows.filter((row) => row.flags.some((flag) => flag.key === "economy_overflow")).length / trendRows.length) * 100, 2) : 0,
      earlyGameTooHardRate: trendRows.length > 0 ? round((trendRows.filter((row) => row.flags.some((flag) => flag.key === "early_game_too_hard")).length / trendRows.length) * 100, 2) : 0,
    },
    trendRows,
  };
}

module.exports = {
  ensureTelemetry,
  startWaveReport,
  finalizeWaveReport,
  finalizeMatch,
  getFinalizedBalanceData,
  recordTick,
  recordIncome,
  recordSpend,
  recordUnitSpawned,
  recordUnitDeath,
  recordDamage,
  recordHealing,
  recordStructureDamage,
  recordLeak,
  attachMatchId,
  buildMultiMatchBalanceReport,
};
