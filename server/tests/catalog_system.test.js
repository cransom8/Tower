"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const catalogSystem = require("../game/multilane/catalogSystem");

function createDeps(overrides = {}) {
  const unitTypesByKey = {
    tt_peasant: {
      key: "tt_peasant",
      enabled: true,
      send_cost: 10,
      build_cost: 0,
      income: 1,
      hp: 35,
      attack_damage: 6,
      attack_speed: 1.5,
      path_speed: 0.2,
      range: 0,
      bounty: 2,
      damage_type: "NORMAL",
      armor_type: "LIGHT",
      damage_reduction_pct: 0,
      special_props: {},
      abilities: [],
    },
    tower_arrow: {
      key: "tower_arrow",
      enabled: true,
      send_cost: 0,
      build_cost: 25,
      income: 0,
      hp: 100,
      attack_damage: 9,
      attack_speed: 1.2,
      projectile_travel_ticks: 6,
      range: 0.3,
      damage_type: "NORMAL",
      armor_type: "MEDIUM",
      damage_reduction_pct: 0,
      special_props: {},
      abilities: [],
    },
  };

  return {
    DEFAULT_FORT_PRESENTATION_KEY: "human_default",
    resolveFortCatalogUnitKey(archetypeKey) {
      return archetypeKey === "infantry_t1" ? "tt_peasant" : null;
    },
    resolveFortDisplayName(_archetypeKey, _presentationKey, fallbackDisplayName) {
      return fallbackDisplayName || "Militia";
    },
    resolveFortPortraitKey() {
      return "tt_peasant";
    },
    resolveFortSkinKey() {
      return "tt_peasant";
    },
    isFortArchetypeKey(archetypeKey) {
      return archetypeKey === "infantry_t1";
    },
    getUnitType(unitTypeKey) {
      return unitTypesByKey[unitTypeKey] || null;
    },
    getAllUnitTypes() {
      return Object.values(unitTypesByKey);
    },
    TICK_HZ: 20,
    GRID_W: 11,
    GRID_H: 28,
    SPLASH_RADIUS_TILES: 1.5,
    ...overrides,
  };
}

test("resolveGameplayCatalogUnitKey maps fortress archetypes to their gameplay unit keys", () => {
  const resolvedKey = catalogSystem.resolveGameplayCatalogUnitKey("infantry_t1", "human_default", createDeps());
  assert.equal(resolvedKey, "tt_peasant");
});

test("resolveGameplayCatalogUnitKey fails loudly when a fortress archetype is missing its catalog mapping", () => {
  assert.throws(
    () => catalogSystem.resolveGameplayCatalogUnitKey(
      "infantry_t1",
      "human_default",
      createDeps({
        resolveFortCatalogUnitKey() {
          return null;
        },
      })
    ),
    /Missing fort gameplay catalog unit mapping/
  );
});

test("resolveUnitDef follows fortress presentation mapping before reading the unit catalog", () => {
  const def = catalogSystem.resolveUnitDef("infantry_t1", createDeps());
  assert.equal(def.cost, 10);
  assert.equal(def.hp, 35);
  assert.equal(def.atkCdTicks, 13);
  assert.equal(def.combatRange, 0);
});

test("resolveUnitDef exposes combat-range and projectile metadata for live unit combat", () => {
  const def = catalogSystem.resolveUnitDef("tt_peasant", createDeps({
    getUnitType(unitTypeKey) {
      if (unitTypeKey !== "tt_peasant")
        return null;

      return {
        key: "tt_peasant",
        enabled: true,
        send_cost: 10,
        build_cost: 12,
        income: 1,
        hp: 35,
        attack_damage: 6,
        attack_speed: 1.5,
        path_speed: 0.2,
        range: 0.36,
        projectile_travel_ticks: 9,
        bounty: 2,
        damage_type: "PIERCE",
        armor_type: "LIGHT",
        damage_reduction_pct: 0,
        proj_behavior: "pierce",
        proj_behavior_params: { maxTargets: 3 },
        special_props: {},
        abilities: [],
      };
    },
  }));

  assert.equal(def.combatRange, 3.96);
  assert.equal(def.projectileTicks, 9);
  assert.equal(def.projBehavior, "pierce");
  assert.deepEqual(def.projBehaviorParams, { maxTargets: 3 });
});
