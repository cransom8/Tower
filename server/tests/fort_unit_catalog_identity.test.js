"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const {
  DEFAULT_FORT_PRESENTATION_KEY,
  resolveFortCatalogUnitKey,
  resolveFortPortraitKey,
  resolveFortSkinKey,
  resolveFortUnitPresentation,
} = require("../game/fortUnitCatalog");

test("human hero presentations resolve directly to TT hero keys", () => {
  const heroExpectations = [
    ["hero_king", "tt_king"],
    ["hero_paladin", "tt_paladin"],
    ["hero_bishop", "tt_commander"],
  ];

  for (const [archetypeKey, expectedCatalogKey] of heroExpectations) {
    const presentation = resolveFortUnitPresentation(archetypeKey, DEFAULT_FORT_PRESENTATION_KEY);
    assert.ok(presentation, `expected presentation for ${archetypeKey}`);
    assert.equal(resolveFortCatalogUnitKey(archetypeKey, DEFAULT_FORT_PRESENTATION_KEY), expectedCatalogKey);
    assert.equal(resolveFortSkinKey(archetypeKey, DEFAULT_FORT_PRESENTATION_KEY), expectedCatalogKey);
    assert.equal(resolveFortPortraitKey(archetypeKey, DEFAULT_FORT_PRESENTATION_KEY), expectedCatalogKey);
  }
});
