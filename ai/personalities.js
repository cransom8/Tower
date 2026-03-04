"use strict";

const { BOT_PERSONALITY } = require("./types");

const PERSONALITY_PROFILES = Object.freeze({
  [BOT_PERSONALITY.RUSH]: Object.freeze({
    key: BOT_PERSONALITY.RUSH,
    ecoWeight: 0.75,
    defenseWeight: 0.95,
    pressureWeight: 1.35,
    floatGoldSoftCap: 90,
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

