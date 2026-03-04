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
    runner:   { cost: 8,  income: 0.5, hp: 60,  dmg: 7,  atkCdTicks: 7,  bounty: 2, armorType: 'UNARMORED', damageType: 'NORMAL' },
    footman:  { cost: 10, income: 1,   hp: 90,  dmg: 8,  atkCdTicks: 8,  bounty: 3, armorType: 'MEDIUM',    damageType: 'NORMAL' },
    ironclad: { cost: 16, income: 2,   hp: 160, dmg: 9,  atkCdTicks: 10, bounty: 4, armorType: 'HEAVY',     damageType: 'NORMAL' },
    warlock:  { cost: 18, income: 2,   hp: 80,  dmg: 12, atkCdTicks: 11, bounty: 5, armorType: 'MAGIC',     damageType: 'MAGIC'  },
    golem:    { cost: 25, income: 3,   hp: 240, dmg: 14, atkCdTicks: 13, bounty: 6, armorType: 'HEAVY',     damageType: 'NORMAL' },
  },
  towerDefs: {
    archer:   { cost: 10, range: 0.36, dmg: 6.6,  atkCdTicks: 12, damageType: 'PIERCE' },
    fighter:  { cost: 12, range: 0.22, dmg: 8.8,  atkCdTicks: 11, damageType: 'NORMAL' },
    cannon:   { cost: 30, range: 0.32, dmg: 8,    atkCdTicks: 16, damageType: 'SPLASH' },
    ballista: { cost: 20, range: 0.40, dmg: 12.1, atkCdTicks: 14, damageType: 'SIEGE'  },
    mage:     { cost: 24, range: 0.35, dmg: 13.2, atkCdTicks: 13, damageType: 'MAGIC'  },
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
    wallCost: 2,
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
  },
  unitDefs: {
    runner:   { cost: 8,  income: 0.5, hp: 60,  dmg: 7,  atkCdTicks: 7,  pathSpeed: 0.060375, bounty: 2, armorType: 'UNARMORED', damageType: 'NORMAL', special: 'splashReduction:0.25' },
    footman:  { cost: 10, income: 1,   hp: 90,  dmg: 8,  atkCdTicks: 8,  pathSpeed: 0.036225, bounty: 3, armorType: 'MEDIUM',    damageType: 'NORMAL' },
    ironclad: { cost: 16, income: 2,   hp: 160, dmg: 9,  atkCdTicks: 10, pathSpeed: 0.036225, bounty: 4, armorType: 'HEAVY',     damageType: 'NORMAL', special: 'pierceReduction:0.30' },
    warlock:  { cost: 18, income: 2,   hp: 80,  dmg: 12, atkCdTicks: 11, pathSpeed: 0.036225, bounty: 5, armorType: 'MAGIC',     damageType: 'MAGIC',  special: 'warlockDebuff' },
    golem:    { cost: 25, income: 3,   hp: 240, dmg: 14, atkCdTicks: 13, pathSpeed: 0.024150, bounty: 6, armorType: 'HEAVY',     damageType: 'NORMAL', special: 'structBonus:0.25' },
  },
  towerDefs: {
    archer:   { cost: 10, range: 4.2,  dmg: 8.5,  atkCdTicks: 11, damageType: 'PIERCE', splash: false },
    fighter:  { cost: 12, range: 2.5,  dmg: 10.0, atkCdTicks: 10, damageType: 'NORMAL', splash: false },
    mage:     { cost: 24, range: 4.0,  dmg: 15.0, atkCdTicks: 13, damageType: 'MAGIC',  splash: false },
    ballista: { cost: 20, range: 5.0,  dmg: 12.1, atkCdTicks: 14, damageType: 'SIEGE',  splash: false },
    cannon:   { cost: 30, range: 3.84, dmg: 8,    atkCdTicks: 16, damageType: 'SPLASH', splash: true  },
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
