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

test("resolveLoadout falls back when inline selection includes send-only units", async () => {
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

  const loadout = await helpers.resolveLoadout(null, null, [1, 2, 3, 4, 6]);

  assert.deepEqual(
    loadout.map((entry) => entry.key),
    ["goblin", "orc", "troll", "vampire", "wyvern"]
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
