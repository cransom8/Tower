const test = require("node:test");
const assert = require("node:assert/strict");

const { createLoadoutHelpers } = require("./loadoutHelpers");

function makeUnit(id, key, overrides = {}) {
  return {
    id,
    key,
    name: overrides.name || key,
    enabled: overrides.enabled ?? true,
    send_cost: overrides.send_cost ?? 10,
    build_cost: overrides.build_cost ?? 8,
    range: overrides.range ?? 1,
    hp: overrides.hp ?? 100,
    attack_damage: overrides.attack_damage ?? 10,
    attack_speed: overrides.attack_speed ?? 1,
    path_speed: overrides.path_speed ?? 0.2,
    damage_type: overrides.damage_type || "NORMAL",
    armor_type: overrides.armor_type || "UNARMORED",
    income: overrides.income ?? 1,
  };
}

test("resolveLoadout throws when inline selection includes non-buildable units", async () => {
  const units = [
    makeUnit(1, "goblin"),
    makeUnit(2, "orc"),
    makeUnit(3, "troll"),
    makeUnit(4, "vampire"),
    makeUnit(5, "wyvern"),
    makeUnit(6, "runner", { build_cost: 0, range: 0 }),
  ];
  const helpers = createLoadoutHelpers({
    db: null,
    unitTypes: { getAllUnitTypes: () => units },
  });

  await assert.rejects(
    () => helpers.resolveLoadout(null, [1, 2, 3, 4, 6], null),
    /inline loadout did not resolve to 5 buildable units/
  );
});

test("hasValidInlineLoadoutIds rejects non-buildable unit ids", () => {
  const units = [
    makeUnit(1, "goblin"),
    makeUnit(2, "orc"),
    makeUnit(3, "troll"),
    makeUnit(4, "vampire"),
    makeUnit(5, "wyvern"),
    makeUnit(6, "runner", { build_cost: 0, range: 0 }),
  ];
  const helpers = createLoadoutHelpers({
    db: null,
    unitTypes: { getAllUnitTypes: () => units },
  });

  assert.equal(helpers.hasValidInlineLoadoutIds([1, 2, 3, 4, 5]), true);
  assert.equal(helpers.hasValidInlineLoadoutIds([1, 2, 3, 4, 6]), false);
});

test("resolveLoadout derives a fixed humans roster from race selection", async () => {
  const units = [
    makeUnit(1, "tt_peasant"),
    makeUnit(2, "tt_spearman"),
    makeUnit(3, "tt_archer"),
    makeUnit(4, "tt_priest"),
    makeUnit(5, "tt_light_infantry"),
    makeUnit(6, "tt_halberdier"),
  ];
  const helpers = createLoadoutHelpers({
    db: null,
    unitTypes: { getAllUnitTypes: () => units },
  });

  const loadout = await helpers.resolveLoadout(null, null, "humans");

  assert.deepEqual(
    loadout.map((entry) => entry.key),
    ["tt_peasant", "tt_spearman", "tt_archer", "tt_priest", "tt_light_infantry"]
  );
});
