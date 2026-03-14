"use strict";

const { BOT_PERSONALITY } = require("./types");

const PERSONALITY_PROFILES = Object.freeze({
  [BOT_PERSONALITY.RUSH]: Object.freeze({
    key: BOT_PERSONALITY.RUSH,
    ecoWeight: 0.75,
    defenseWeight: 0.95,
    pressureWeight: 1.35,
    floatGoldSoftCap: 90,
    frontBias: 1.25,
    backBias: 0.35,
    centerBias: 0.9,
    edgeBias: 0.35,
    spreadBias: -0.25,
    towerRangeWeight: 0.35,
    towerHealthWeight: 0.95,
    towerDamageWeight: 1.05,
    towerIncomeWeight: 0.2,
    towerCheapnessWeight: 0.95,
    towerMagicWeight: 0.1,
    preferredUnits: Object.freeze(["runner", "footman", "ironclad"]),
    preferredTowers: Object.freeze(["fighter", "archer", "ballista"]),
    sendBucketBias: Object.freeze([5, 10, 3, 1]),
  }),
  [BOT_PERSONALITY.ECO]: Object.freeze({
    key: BOT_PERSONALITY.ECO,
    ecoWeight: 1.35,
    defenseWeight: 1.05,
    pressureWeight: 0.8,
    floatGoldSoftCap: 130,
    frontBias: 0.4,
    backBias: 1.15,
    centerBias: 0.75,
    edgeBias: 0.55,
    spreadBias: 0.95,
    towerRangeWeight: 0.95,
    towerHealthWeight: 0.45,
    towerDamageWeight: 0.55,
    towerIncomeWeight: 1.15,
    towerCheapnessWeight: 0.7,
    towerMagicWeight: 0.35,
    preferredUnits: Object.freeze(["footman", "runner", "warlock"]),
    preferredTowers: Object.freeze(["archer", "fighter", "mage"]),
    sendBucketBias: Object.freeze([1, 3, 5, 10]),
  }),
  [BOT_PERSONALITY.PRESSURE]: Object.freeze({
    key: BOT_PERSONALITY.PRESSURE,
    ecoWeight: 0.9,
    defenseWeight: 1.05,
    pressureWeight: 1.2,
    floatGoldSoftCap: 105,
    frontBias: 1.0,
    backBias: 0.5,
    centerBias: 0.45,
    edgeBias: 1.0,
    spreadBias: 0.35,
    towerRangeWeight: 1.05,
    towerHealthWeight: 0.5,
    towerDamageWeight: 1.1,
    towerIncomeWeight: 0.25,
    towerCheapnessWeight: 0.55,
    towerMagicWeight: 0.7,
    preferredUnits: Object.freeze(["ironclad", "warlock", "footman", "golem"]),
    preferredTowers: Object.freeze(["ballista", "mage", "cannon"]),
    sendBucketBias: Object.freeze([3, 5, 10, 1]),
  }),
  [BOT_PERSONALITY.ADAPTIVE]: Object.freeze({
    key: BOT_PERSONALITY.ADAPTIVE,
    ecoWeight: 1.0,
    defenseWeight: 1.0,
    pressureWeight: 1.0,
    floatGoldSoftCap: 110,
    frontBias: 0.75,
    backBias: 0.8,
    centerBias: 0.8,
    edgeBias: 0.6,
    spreadBias: 0.55,
    towerRangeWeight: 0.75,
    towerHealthWeight: 0.7,
    towerDamageWeight: 0.8,
    towerIncomeWeight: 0.45,
    towerCheapnessWeight: 0.55,
    towerMagicWeight: 0.45,
    preferredUnits: Object.freeze(["footman", "ironclad", "warlock", "golem", "runner"]),
    preferredTowers: Object.freeze(["archer", "fighter", "mage", "ballista", "cannon"]),
    sendBucketBias: Object.freeze([3, 1, 5, 10]),
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
