"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const simMl = require("../sim-multilane");

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

test("public multilane config exposes market tiers and economy unit progression separately from barracks combat roster", () => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);

  const config = simMl.createMLPublicConfig({ laneTeams: ["red", "blue"] });

  const marketBuilding = config.fortressBuildingConfigs.find((entry) => entry.buildingType === "market");
  assert.ok(marketBuilding, "market building config should be published");
  assert.equal(marketBuilding.displayName, "Market");
  assert.equal(marketBuilding.maxTier, 3);
  assert.equal(marketBuilding.buildCost, 60);
  assert.deepEqual(marketBuilding.tierDisplayNames.slice(1), ["Tier 1", "Tier 2", "Tier 3"]);

  assert.equal(
    config.fortressPadConfigs.some((entry) => entry.padId === "market_pad" && entry.buildingType === "market"),
    true,
    "market should now have a live fortress pad"
  );

  assert.ok(Array.isArray(config.marketRosterConfigs), "market roster config should be present");
  assert.equal(config.marketRosterConfigs.length, 3);

  const marketByKey = new Map(config.marketRosterConfigs.map((entry) => [entry.unitKey, entry]));
  assert.deepEqual(
    ["peasant", "settler", "trader"],
    config.marketRosterConfigs.map((entry) => entry.unitKey)
  );

  assert.deepEqual(
    {
      unlockBuildingType: marketByKey.get("peasant").unlockBuildingType,
      requiredBuildingTier: marketByKey.get("peasant").requiredBuildingTier,
      economyLapGold: marketByKey.get("peasant").economyLapGold,
      routeStartBuildingType: marketByKey.get("peasant").routeStartBuildingType,
      routeEndBuildingType: marketByKey.get("peasant").routeEndBuildingType,
      routeEndBuildingName: marketByKey.get("peasant").routeEndBuildingName,
      nextUnitKey: marketByKey.get("peasant").nextUnitKey,
    },
    {
      unlockBuildingType: "market",
      requiredBuildingTier: 1,
      economyLapGold: 4,
      routeStartBuildingType: "market",
      routeEndBuildingType: "trade_outpost",
      routeEndBuildingName: "Beast Lair",
      nextUnitKey: "settler",
    }
  );

  assert.deepEqual(
    {
      unlockBuildingType: marketByKey.get("settler").unlockBuildingType,
      requiredBuildingTier: marketByKey.get("settler").requiredBuildingTier,
      economyLapGold: marketByKey.get("settler").economyLapGold,
      nextUnitKey: marketByKey.get("settler").nextUnitKey,
    },
    {
      unlockBuildingType: "market",
      requiredBuildingTier: 2,
      economyLapGold: 7,
      nextUnitKey: "trader",
    }
  );

  assert.deepEqual(
    {
      unlockBuildingType: marketByKey.get("trader").unlockBuildingType,
      requiredBuildingTier: marketByKey.get("trader").requiredBuildingTier,
      economyLapGold: marketByKey.get("trader").economyLapGold,
      nextUnitKey: marketByKey.get("trader").nextUnitKey,
    },
    {
      unlockBuildingType: "market",
      requiredBuildingTier: 3,
      economyLapGold: 10,
      nextUnitKey: null,
    }
  );

  const barracksRosterKeys = new Set(config.barracksRosterConfigs.map((entry) => entry.rosterKey));
  assert.equal(barracksRosterKeys.has("peasant"), false);
  assert.equal(barracksRosterKeys.has("settler"), false);
  assert.equal(barracksRosterKeys.has("trader"), false);
});
