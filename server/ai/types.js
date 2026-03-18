"use strict";

const BOT_DIFFICULTY = Object.freeze({
  EASY: "easy",
  MEDIUM: "medium",
  HARD: "hard",
  INSANE: "insane",
});

const BOT_PERSONALITY = Object.freeze({
  RUSH: "RUSH",
  ECO: "ECO",
  PRESSURE: "PRESSURE",
  ADAPTIVE: "ADAPTIVE",
});

const AI_ACTION_TYPE = Object.freeze({
  DO_NOTHING: "DO_NOTHING",
  UPGRADE_INCOME: "UPGRADE_INCOME",
  BUILD_TOWER: "BUILD_TOWER",
  UPGRADE_TOWER: "UPGRADE_TOWER",
  SEND_UNITS: "SEND_UNITS",
});

const COUNT_BUCKETS = Object.freeze([1, 3, 5, 10]);

const DIFFICULTY_KNOBS = Object.freeze({
  easy: Object.freeze({
    reactionDelayMinMs: 2500,
    reactionDelayMaxMs: 4000,
    mistakeRate: 0.24,
    planningDepth: 1,
  }),
  medium: Object.freeze({
    reactionDelayMinMs: 1200,
    reactionDelayMaxMs: 2500,
    mistakeRate: 0.11,
    planningDepth: 2,
  }),
  hard: Object.freeze({
    reactionDelayMinMs: 300,
    reactionDelayMaxMs: 500,
    mistakeRate: 0.03,
    planningDepth: 3,
  }),
  insane: Object.freeze({
    reactionDelayMinMs: 120,
    reactionDelayMaxMs: 320,
    mistakeRate: 0.0,
    planningDepth: 4,
  }),
});

module.exports = {
  BOT_DIFFICULTY,
  BOT_PERSONALITY,
  AI_ACTION_TYPE,
  COUNT_BUCKETS,
  DIFFICULTY_KNOBS,
};
