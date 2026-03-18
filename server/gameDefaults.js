'use strict';

// Read-only snapshot of game constants from sim.js and sim-multilane.js.
// Used by the admin config viewer. The sims still use their own hardcoded copies.
// When a DB-backed config editor ships, the sims will import from here instead.

const CLASSIC = {
  globalParams: {
    tickHz: 20,
    incomeIntervalSeconds: 10,
    startGold: 70,
    startIncome: 10,
    livesStart: 20,
    towerMaxLevel: 10,
    towerUpgradeScaleBase: 0.75,
    towerUpgradeScalePerLevel: 0.25,
    towerDmgPerLevel: 0.12,
    towerRangePerLevel: 0.015,
    towerAtkCdReductionPerLevel: 0.015,
    barracksCostBase: 100,
    barracksReqIncomeBase: 8,
    combatRange: 0.045,
    marchSpeed: 0.00129375,
  },
  unitDefs: {
    // Unit roster is DB-driven via unit_types table (migrations 034+035). See unitTypes.js.
  },
  towerDefs: {
    // Classic tower types retired in migration 035. New creatures serve as both attackers and defenders.
  },
  damageMultipliers: {
    PIERCE: { UNARMORED: 1.35, LIGHT: 1.20, MEDIUM: 1.00, HEAVY: 0.75, MAGIC: 0.85 },
    NORMAL: { UNARMORED: 1.10, LIGHT: 1.00, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 0.90 },
    SPLASH: { UNARMORED: 1.10, LIGHT: 1.25, MEDIUM: 1.20, HEAVY: 0.80, MAGIC: 0.85 },
    SIEGE:  { UNARMORED: 0.90, LIGHT: 0.90, MEDIUM: 1.00, HEAVY: 1.35, MAGIC: 0.80 },
    MAGIC:  { UNARMORED: 1.00, LIGHT: 1.05, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 1.40 },
  },
};

const MULTILANE = {
  globalParams: {
    tickHz: 20,
    incomeIntervalSeconds: 12,
    startGold: 70,
    startIncome: 10,
    livesStart: 20,
    towerMaxLevel: 10,
    gridW: 11,
    gridH: 28,
    maxUnitsPerLane: 80,
    gateKillBounty: 10,
    barracksCostBase: 100,
    barracksReqIncomeBase: 8,
    warlockDebuffCd: 60,
    warlockDebuffTicks: 60,
    warlockDebuffMult: 0.75,
    warlockDebuffRange: 3.5,
    // Wave defense
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
    escalationPerExtraRound: 0.10,
  },
  unitDefs: {
    // Unit roster is DB-driven via unit_types table (migrations 034+035). See unitTypes.js.
  },
  towerDefs: {
    // Classic tower types retired in migration 035. New creatures serve as both attackers and defenders.
  },
  damageMultipliers: {
    PIERCE: { UNARMORED: 1.35, LIGHT: 1.20, MEDIUM: 1.00, HEAVY: 0.75, MAGIC: 0.85 },
    NORMAL: { UNARMORED: 1.10, LIGHT: 1.00, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 0.90 },
    SPLASH: { UNARMORED: 1.10, LIGHT: 1.25, MEDIUM: 1.20, HEAVY: 0.80, MAGIC: 0.85 },
    SIEGE:  { UNARMORED: 0.90, LIGHT: 0.90, MEDIUM: 1.00, HEAVY: 1.35, MAGIC: 0.80 },
    MAGIC:  { UNARMORED: 1.00, LIGHT: 1.05, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 1.40 },
  },
};

module.exports = { CLASSIC, MULTILANE };
