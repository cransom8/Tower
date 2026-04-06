const test = require("node:test");
const assert = require("node:assert/strict");

const { resolveProjectile } = require("../sim-core");

function makeUnit(id, allegianceKey, posX, posY) {
  return {
    id,
    allegianceKey,
    sourceTeam: allegianceKey,
    hp: 20,
    maxHp: 20,
    armorType: "LIGHT",
    damageReductionPct: 0,
    posX,
    posY,
  };
}

test("splash projectiles only damage hostile units near the impact point", () => {
  const lane = {
    laneIndex: 0,
    path: [],
    units: [
      makeUnit("yellow_primary", "yellow", 0, 0),
      makeUnit("blue_secondary", "blue", 0.6, 0),
      makeUnit("red_ally", "red", 0.4, 0.3),
    ],
  };

  const result = resolveProjectile({}, lane, {
    targetId: "yellow_primary",
    sourceAllegianceKey: "red",
    dmg: 5,
    damageType: "NORMAL",
    behavior: "splash",
    behaviorParams: { radius: 1.0 },
    toX: 0,
    toY: 0,
  });

  assert.deepEqual(result.hit.sort(), ["blue_secondary", "yellow_primary"]);
  assert.equal(lane.units[0].hp, 15, "the primary hostile should take splash damage");
  assert.equal(lane.units[1].hp, 15, "nearby hostile units should still be hit");
  assert.equal(lane.units[2].hp, 20, "nearby allies should be ignored by splash resolution");
});

test("chain projectiles skip allies and continue through hostile units", () => {
  const lane = {
    laneIndex: 0,
    path: [],
    units: [
      makeUnit("yellow_primary", "yellow", 0, 0),
      makeUnit("red_ally", "red", 0.35, 0),
      makeUnit("blue_secondary", "blue", 0.7, 0),
    ],
  };

  const result = resolveProjectile({}, lane, {
    targetId: "yellow_primary",
    sourceAllegianceKey: "red",
    dmg: 5,
    damageType: "NORMAL",
    behavior: "chain",
    behaviorParams: { maxJumps: 3, jumpRange: 1.0, dmgFalloff: 1.0 },
    toX: 0,
    toY: 0,
  });

  assert.deepEqual(result.hit.sort(), ["blue_secondary", "yellow_primary"]);
  assert.equal(lane.units[0].hp, 15, "the primary hostile should take the opening chain hit");
  assert.equal(lane.units[1].hp, 20, "allies should not become bounce or chain follow-ups");
  assert.equal(lane.units[2].hp, 15, "the chain should still continue to another hostile");
});
