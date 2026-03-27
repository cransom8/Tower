"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const simMl = require("../sim-multilane");
const { normalizeMatchSettings } = require("../socket/helpers");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 140,
    startIncome: 13,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

test("validateConfig rejects a multilane config missing startIncome", () => {
  assert.throws(
    () => gameConfig.validateConfig("multilane", {
      globalParams: {
        startGold: 140,
        livesStart: 20,
        teamHpStart: 20,
        buildPhaseTicks: 600,
        transitionPhaseTicks: 200,
      },
    }, "test multilane config"),
    /globalParams\.startIncome/
  );
});

test("normalizeMatchSettings uses the active multilane config instead of injecting fallback income", () => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);

  const normalized = normalizeMatchSettings({});

  assert.equal(normalized.startGold, 140);
  assert.equal(normalized.startIncome, 13);
  assert.equal(normalized.teamHpStart, 20);
});

test("normalizeMatchSettings fails loudly when no active multilane config is available", () => {
  assert.throws(
    () => normalizeMatchSettings({}),
    /Missing active multilane config/
  );
});

test("normalizeMatchSettings fails loudly on invalid explicit startIncome", () => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);

  assert.throws(
    () => normalizeMatchSettings({ startIncome: "not-a-number" }),
    /Invalid match setting 'startIncome'/
  );
});

test("createMLGame fails loudly when the active multilane config is missing", () => {
  assert.throws(
    () => simMl.createMLGame(2, { laneTeams: ["red", "blue"] }),
    /Missing active multilane config/
  );
});
