"use strict";

const { BOT_PERSONALITY } = require("./types");

const PERSONALITY_PROFILES = Object.freeze({
  [BOT_PERSONALITY.RUSH]: Object.freeze({
    key: BOT_PERSONALITY.RUSH,
    ecoWeight: 0.75,
    defenseWeight: 0.9,
    pressureWeight: 1.35,
    floatGoldSoftCap: 90,
    defendProgress: 0.34,
    retreatProgress: 0.08,
    barracksSiteOrder: Object.freeze(["center", "left", "right"]),
    openingGoals: Object.freeze(["center_barracks", "lumber_mill", "blacksmith", "archery_tower"]),
    branchPriority: Object.freeze(["blacksmith", "archery_tower", "wizard_tower", "temple"]),
    preferredRoster: Object.freeze(["militia", "shieldman", "archer", "spearman", "swordsman", "crossbowman", "mage", "cleric"]),
    roleTargets: Object.freeze({ melee: 0.55, ranged: 0.3, support: 0.15 }),
    defenseStructurePriority: Object.freeze(["gate", "wall", "turret"]),
    townCoreUpgradeBias: 0.85,
  }),
  [BOT_PERSONALITY.ECO]: Object.freeze({
    key: BOT_PERSONALITY.ECO,
    ecoWeight: 1.35,
    defenseWeight: 1.0,
    pressureWeight: 0.78,
    floatGoldSoftCap: 145,
    defendProgress: 0.28,
    retreatProgress: 0.06,
    barracksSiteOrder: Object.freeze(["center", "left", "right"]),
    openingGoals: Object.freeze(["lumber_mill", "center_barracks", "blacksmith", "temple", "archery_tower"]),
    branchPriority: Object.freeze(["temple", "blacksmith", "archery_tower", "wizard_tower"]),
    preferredRoster: Object.freeze(["militia", "cleric", "priest", "archer", "shieldman", "crossbowman", "high_priest"]),
    roleTargets: Object.freeze({ melee: 0.42, ranged: 0.28, support: 0.3 }),
    defenseStructurePriority: Object.freeze(["gate", "wall", "turret"]),
    townCoreUpgradeBias: 1.25,
  }),
  [BOT_PERSONALITY.PRESSURE]: Object.freeze({
    key: BOT_PERSONALITY.PRESSURE,
    ecoWeight: 0.9,
    defenseWeight: 0.98,
    pressureWeight: 1.2,
    floatGoldSoftCap: 105,
    defendProgress: 0.4,
    retreatProgress: 0.1,
    barracksSiteOrder: Object.freeze(["center", "left", "right"]),
    openingGoals: Object.freeze(["center_barracks", "blacksmith", "lumber_mill", "wizard_tower", "archery_tower"]),
    branchPriority: Object.freeze(["blacksmith", "wizard_tower", "archery_tower", "temple"]),
    preferredRoster: Object.freeze(["shieldman", "mage", "archer", "swordsman", "wizard", "crossbowman", "guardian"]),
    roleTargets: Object.freeze({ melee: 0.46, ranged: 0.39, support: 0.15 }),
    defenseStructurePriority: Object.freeze(["gate", "wall", "turret"]),
    townCoreUpgradeBias: 0.95,
  }),
  [BOT_PERSONALITY.ADAPTIVE]: Object.freeze({
    key: BOT_PERSONALITY.ADAPTIVE,
    ecoWeight: 1.0,
    defenseWeight: 1.05,
    pressureWeight: 1.0,
    floatGoldSoftCap: 115,
    defendProgress: 0.32,
    retreatProgress: 0.08,
    barracksSiteOrder: Object.freeze(["center", "left", "right"]),
    openingGoals: Object.freeze(["lumber_mill", "center_barracks", "blacksmith", "archery_tower", "temple"]),
    branchPriority: Object.freeze(["blacksmith", "archery_tower", "temple", "wizard_tower"]),
    preferredRoster: Object.freeze(["militia", "shieldman", "archer", "cleric", "mage", "crossbowman", "guardian"]),
    roleTargets: Object.freeze({ melee: 0.5, ranged: 0.3, support: 0.2 }),
    defenseStructurePriority: Object.freeze(["gate", "wall", "turret"]),
    townCoreUpgradeBias: 1.05,
  }),
});

const PERSONALITY_ORDER = Object.freeze(Object.keys(PERSONALITY_PROFILES));

function getPersonalityProfile(personality) {
  return PERSONALITY_PROFILES[personality] || PERSONALITY_PROFILES[BOT_PERSONALITY.ADAPTIVE];
}

function choosePersonality(rng, preferred) {
  if (preferred && PERSONALITY_PROFILES[preferred]) return preferred;
  if (!rng || typeof rng.nextInt !== "function") return BOT_PERSONALITY.ADAPTIVE;
  const idx = rng.nextInt(0, PERSONALITY_ORDER.length - 1);
  return PERSONALITY_ORDER[idx];
}

module.exports = {
  PERSONALITY_PROFILES,
  PERSONALITY_ORDER,
  getPersonalityProfile,
  choosePersonality,
};
