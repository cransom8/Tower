"use strict";

const {
  resolveFortCatalogUnitKey,
  resolveFortPortraitKey,
} = require("./fortUnitCatalog");

const HUMANS_RACE_ID = "humans";

const RACES = Object.freeze([
  Object.freeze({
    id: HUMANS_RACE_ID,
    displayName: "Humans",
    featuredPortraitKey: resolveFortPortraitKey("hero_king"),
    matchLoadoutArchetypeKeys: Object.freeze([
      "infantry_t1",
      "polearm_t1",
      "ranged_t1",
      "support_t2",
      "infantry_t2",
    ]),
  }),
]);

const RACES_BY_ID = new Map(RACES.map((race) => [race.id, race]));

function normalizeRaceId(raceId) {
  const normalized = String(raceId || "").trim().toLowerCase();
  if (!normalized || !RACES_BY_ID.has(normalized))
    return null;

  return normalized;
}

function isValidRaceId(raceId) {
  return normalizeRaceId(raceId) !== null;
}

function getDefaultRaceId() {
  return HUMANS_RACE_ID;
}

function getRaceDefinition(raceId) {
  const normalized = normalizeRaceId(raceId) || HUMANS_RACE_ID;
  return RACES_BY_ID.get(normalized) || RACES[0];
}

function getAvailableRaces() {
  return RACES.map((race) => ({
    id: race.id,
    displayName: race.displayName,
    featuredPortraitKey: race.featuredPortraitKey,
    enabled: true,
  }));
}

function getMatchLoadoutKeysForRace(raceId) {
  return getRaceDefinition(raceId)
    .matchLoadoutArchetypeKeys
    .map((archetypeKey) => resolveFortCatalogUnitKey(archetypeKey))
    .filter(Boolean);
}

function getMatchLoadoutArchetypeKeysForRace(raceId) {
  return getRaceDefinition(raceId).matchLoadoutArchetypeKeys.slice();
}

module.exports = {
  getAvailableRaces,
  getDefaultRaceId,
  getMatchLoadoutArchetypeKeysForRace,
  getMatchLoadoutKeysForRace,
  getRaceDefinition,
  isValidRaceId,
  normalizeRaceId,
};
