"use strict";

const DEFAULT_FORT_PRESENTATION_KEY = "human_default";

const FORT_ARCHETYPE_DEFS = Object.freeze([
  Object.freeze({ archetypeKey: "infantry_t1", family: "infantry", tier: 1, role: "melee" }),
  Object.freeze({ archetypeKey: "infantry_t2", family: "infantry", tier: 2, role: "melee" }),
  Object.freeze({ archetypeKey: "infantry_t3", family: "infantry", tier: 3, role: "melee" }),
  Object.freeze({ archetypeKey: "polearm_t1", family: "polearm", tier: 1, role: "melee" }),
  Object.freeze({ archetypeKey: "polearm_t2", family: "polearm", tier: 2, role: "melee" }),
  Object.freeze({ archetypeKey: "polearm_t3", family: "polearm", tier: 3, role: "melee" }),
  Object.freeze({ archetypeKey: "shield_t1", family: "shield", tier: 1, role: "melee" }),
  Object.freeze({ archetypeKey: "shield_t2", family: "shield", tier: 2, role: "melee" }),
  Object.freeze({ archetypeKey: "shield_t3", family: "shield", tier: 3, role: "melee" }),
  Object.freeze({ archetypeKey: "support_t1", family: "support", tier: 1, role: "support" }),
  Object.freeze({ archetypeKey: "support_t2", family: "support", tier: 2, role: "support" }),
  Object.freeze({ archetypeKey: "support_t3", family: "support", tier: 3, role: "support" }),
  Object.freeze({ archetypeKey: "arcane_t1", family: "arcane", tier: 1, role: "ranged" }),
  Object.freeze({ archetypeKey: "arcane_t2", family: "arcane", tier: 2, role: "ranged" }),
  Object.freeze({ archetypeKey: "arcane_t3", family: "arcane", tier: 3, role: "ranged" }),
  Object.freeze({ archetypeKey: "ranged_t1", family: "ranged", tier: 1, role: "ranged" }),
  Object.freeze({ archetypeKey: "ranged_t2", family: "ranged", tier: 2, role: "ranged" }),
  Object.freeze({ archetypeKey: "ranged_t3", family: "ranged", tier: 3, role: "ranged" }),
  Object.freeze({ archetypeKey: "economy_t1", family: "economy", tier: 1, role: "economy" }),
  Object.freeze({ archetypeKey: "economy_t2", family: "economy", tier: 2, role: "economy" }),
  Object.freeze({ archetypeKey: "economy_t3", family: "economy", tier: 3, role: "economy" }),
  Object.freeze({ archetypeKey: "hero_king", family: "hero", tier: 4, role: "hero" }),
  Object.freeze({ archetypeKey: "hero_paladin", family: "hero", tier: 4, role: "hero" }),
  Object.freeze({ archetypeKey: "hero_bishop", family: "hero", tier: 4, role: "hero" }),
]);

const FORT_ARCHETYPE_BY_KEY = new Map(
  FORT_ARCHETYPE_DEFS.map((entry) => [entry.archetypeKey, entry]),
);

const FORT_PRESENTATION_DEFS = Object.freeze([
  Object.freeze({
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    displayName: "Human Default",
    factionKey: "humans",
    style: "baseline",
  }),
]);

const FORT_UNIT_PRESENTATION_DEFS = Object.freeze([
  Object.freeze({ archetypeKey: "infantry_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_peasant", skinKey: "tt_peasant", portraitKey: "tt_peasant", displayName: "Militia" }),
  Object.freeze({ archetypeKey: "infantry_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_light_infantry", skinKey: "tt_light_infantry", portraitKey: "tt_light_infantry", displayName: "Swordsman" }),
  Object.freeze({ archetypeKey: "infantry_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_mounted_knight", skinKey: "tt_mounted_knight", portraitKey: "tt_mounted_knight", displayName: "Knight" }),
  Object.freeze({ archetypeKey: "polearm_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_spearman", skinKey: "tt_spearman", portraitKey: "tt_spearman", displayName: "Spearman" }),
  Object.freeze({ archetypeKey: "polearm_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_halberdier", skinKey: "tt_halberdier", portraitKey: "tt_halberdier", displayName: "Halberdier" }),
  Object.freeze({ archetypeKey: "polearm_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_light_cavalry", skinKey: "tt_light_cavalry", portraitKey: "tt_light_cavalry", displayName: "Lancer" }),
  Object.freeze({ archetypeKey: "shield_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_heavy_infantry", skinKey: "tt_heavy_infantry", portraitKey: "tt_heavy_infantry", displayName: "Shieldman" }),
  Object.freeze({ archetypeKey: "shield_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_heavy_swordman", skinKey: "tt_heavy_swordman", portraitKey: "tt_heavy_swordman", displayName: "Shield Guard" }),
  Object.freeze({ archetypeKey: "shield_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_heavy_cavalry", skinKey: "tt_heavy_cavalry", portraitKey: "tt_heavy_cavalry", displayName: "Guardian" }),
  Object.freeze({ archetypeKey: "support_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_mounted_priest", skinKey: "tt_mounted_priest", portraitKey: "tt_mounted_priest", displayName: "Cleric" }),
  Object.freeze({ archetypeKey: "support_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_priest", skinKey: "tt_priest", portraitKey: "tt_priest", displayName: "Priest" }),
  Object.freeze({ archetypeKey: "support_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_high_priest", skinKey: "tt_high_priest", portraitKey: "tt_high_priest", displayName: "High Priest" }),
  Object.freeze({ archetypeKey: "arcane_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_mage", skinKey: "tt_mage", portraitKey: "tt_mage", displayName: "Mage" }),
  Object.freeze({ archetypeKey: "arcane_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_mounted_mage", skinKey: "tt_mounted_mage", portraitKey: "tt_mounted_mage", displayName: "Wizard" }),
  Object.freeze({ archetypeKey: "arcane_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_mounted_king", skinKey: "tt_mounted_king", portraitKey: "tt_mounted_king", displayName: "Thaumaturge" }),
  Object.freeze({ archetypeKey: "ranged_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_archer", skinKey: "tt_archer", portraitKey: "tt_archer", displayName: "Archer" }),
  Object.freeze({ archetypeKey: "ranged_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_crossbowman", skinKey: "tt_crossbowman", portraitKey: "tt_crossbowman", displayName: "Crossbowman" }),
  Object.freeze({ archetypeKey: "ranged_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_mounted_scout", skinKey: "tt_mounted_scout", portraitKey: "tt_mounted_scout", displayName: "Ranger" }),
  Object.freeze({ archetypeKey: "economy_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_peasant", skinKey: "tt_peasant", portraitKey: "tt_peasant", displayName: "Peasant" }),
  Object.freeze({ archetypeKey: "economy_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_settler", skinKey: "tt_settler", portraitKey: "tt_settler", displayName: "Settler" }),
  Object.freeze({ archetypeKey: "economy_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "tt_settler", skinKey: "tt_settler", portraitKey: "tt_settler", displayName: "Trader" }),
  Object.freeze({ archetypeKey: "hero_king", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "cyclops", skinKey: "tt_king", portraitKey: "tt_king", displayName: "King" }),
  Object.freeze({ archetypeKey: "hero_paladin", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "chimera", skinKey: "tt_paladin", portraitKey: "tt_paladin", displayName: "Paladin" }),
  Object.freeze({ archetypeKey: "hero_bishop", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, catalogUnitKey: "manticora", skinKey: "tt_commander", portraitKey: "tt_commander", displayName: "Bishop" }),
]);

const FORT_UNIT_PRESENTATION_BY_KEY = new Map(
  FORT_UNIT_PRESENTATION_DEFS.map((entry) => [`${entry.presentationKey}:${entry.archetypeKey}`, entry]),
);

function normalizeKey(value) {
  return String(value || "").trim().toLowerCase();
}

function titleCaseFromKey(key) {
  return String(key || "")
    .split(/[_-]/)
    .filter(Boolean)
    .map((part) => part.length <= 1
      ? part.toUpperCase()
      : part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function buildPresentationLookupKey(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  const normalizedArchetype = normalizeKey(archetypeKey);
  const normalizedPresentation = normalizeKey(presentationKey) || DEFAULT_FORT_PRESENTATION_KEY;
  return `${normalizedPresentation}:${normalizedArchetype}`;
}

function isFortArchetypeKey(archetypeKey) {
  return FORT_ARCHETYPE_BY_KEY.has(normalizeKey(archetypeKey));
}

function resolveFortUnitPresentation(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  const exact = FORT_UNIT_PRESENTATION_BY_KEY.get(buildPresentationLookupKey(archetypeKey, presentationKey));
  if (exact)
    return exact;

  return FORT_UNIT_PRESENTATION_BY_KEY.get(buildPresentationLookupKey(archetypeKey, DEFAULT_FORT_PRESENTATION_KEY)) || null;
}

function resolveFortCatalogUnitKey(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  const resolved = resolveFortUnitPresentation(archetypeKey, presentationKey);
  return resolved ? resolved.catalogUnitKey : null;
}

function resolveFortPortraitKey(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  const resolved = resolveFortUnitPresentation(archetypeKey, presentationKey);
  if (!resolved)
    return null;

  return resolved.portraitKey || resolved.skinKey || resolved.catalogUnitKey || null;
}

function resolveFortSkinKey(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  const resolved = resolveFortUnitPresentation(archetypeKey, presentationKey);
  return resolved ? resolved.skinKey || null : null;
}

function resolveFortDisplayName(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY, fallback = null) {
  const resolved = resolveFortUnitPresentation(archetypeKey, presentationKey);
  if (resolved && resolved.displayName)
    return resolved.displayName;

  return fallback || titleCaseFromKey(archetypeKey);
}

module.exports = {
  DEFAULT_FORT_PRESENTATION_KEY,
  FORT_ARCHETYPE_DEFS,
  FORT_PRESENTATION_DEFS,
  FORT_UNIT_PRESENTATION_DEFS,
  isFortArchetypeKey,
  resolveFortCatalogUnitKey,
  resolveFortDisplayName,
  resolveFortPortraitKey,
  resolveFortSkinKey,
  resolveFortUnitPresentation,
  titleCaseFromKey,
};
