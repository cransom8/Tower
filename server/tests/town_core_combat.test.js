"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 140,
    startIncome: 10,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

function makeUnit(key, options = {}) {
  return {
    id: options.id || key,
    key,
    name: options.name || key,
    enabled: true,
    send_cost: options.send_cost ?? 10,
    build_cost: options.build_cost ?? 10,
    income: options.income ?? 1,
    hp: options.hp ?? 40,
    attack_damage: options.attack_damage ?? 6,
    attack_speed: options.attack_speed ?? 20,
    path_speed: options.path_speed ?? 0.35,
    range: options.range ?? 0.08,
    projectile_travel_ticks: options.projectile_travel_ticks ?? 6,
    damage_type: options.damage_type || "NORMAL",
    armor_type: options.armor_type || "MEDIUM",
    damage_reduction_pct: options.damage_reduction_pct ?? 0,
    bounty: options.bounty ?? 1,
    special_props: options.special_props || {},
    abilities: options.abilities || [],
    barracks_scales_hp: options.barracks_scales_hp ?? false,
    barracks_scales_dmg: options.barracks_scales_dmg ?? false,
  };
}

setUnitTypesForTests([
  makeUnit("raider", {
    hp: 36,
    attack_damage: 6,
    attack_speed: 20,
    path_speed: 0.4,
    range: 0.08,
    armor_type: "LIGHT",
  }),
  makeUnit("guardian", {
    hp: 80,
    attack_damage: 18,
    attack_speed: 20,
    path_speed: 0.28,
    range: 0.10,
    armor_type: "HEAVY",
  }),
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

let nextUnitId = 1;

function createGame(teamHpStart = 12) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    teamHpStart,
    startGold: 0,
    startIncome: 0,
  });
}

function getTownCorePad(lane) {
  return lane.fortressPads.find((pad) => pad && pad.buildingType === "town_core");
}

function getCoreApproachPosition(lane, stepsBack = 0) {
  const fullPath = lane.fullPath || lane.path;
  const endPoint = fullPath[fullPath.length - 1];
  const y = endPoint.y - stepsBack;
  return {
    posX: endPoint.x,
    posY: y,
    pathIdx: y,
  };
}

function createWaveUnit(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  const id = overrides.id || `wu_test_${nextUnitId++}`;
  return {
    id,
    unitId: id,
    unitTypeKey: typeKey,
    allegianceKey: "dungeon",
    ownerLaneIndex: -1,
    ownerLane: -1,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    type: typeKey,
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 27,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? (overrides.hp ?? def.hp),
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 1,
    stance: "ATTACK",
    pathContractType: "wave_lane",
    isWaveUnit: true,
    isDefender: false,
    combatState: overrides.combatState ?? "IDLE",
    combatTarget: null,
    hasBreachedTownCore: false,
    spawnIndex: overrides.spawnIndex ?? 0,
    posX: overrides.posX ?? 5,
    posY: overrides.posY ?? 27,
  };
}

function createDefender(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  const id = overrides.id || `def_test_${nextUnitId++}`;
  return {
    id,
    unitId: id,
    unitTypeKey: typeKey,
    allegianceKey: overrides.allegianceKey ?? "red",
    ownerLaneIndex: overrides.ownerLane ?? 0,
    ownerLane: overrides.ownerLane ?? 0,
    targetLaneIndex: overrides.targetLaneIndex ?? (overrides.ownerLane ?? 0),
    sourceLaneIndex: overrides.sourceLaneIndex ?? 0,
    sourceTeam: overrides.sourceTeam ?? null,
    sourceBarracksId: overrides.sourceBarracksId ?? null,
    sourceBarracksKey: overrides.sourceBarracksKey ?? overrides.sourceBarracksId ?? null,
    spawnSourceType: overrides.spawnSourceType ?? "barracks_roster",
    type: typeKey,
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 24,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? (overrides.hp ?? def.hp),
    baseDmg: overrides.baseDmg ?? def.dmg,
    moveSpeed: overrides.moveSpeed ?? 0.35,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 0,
    stance: overrides.stance ?? "DEFEND",
    pathContractType: overrides.pathContractType ?? (overrides.combatTarget ? "intercept" : "guard_anchor"),
    isWaveUnit: false,
    isDefender: overrides.isDefender ?? true,
    combatState: overrides.combatState ?? "IDLE",
    combatTarget: null,
    defState: overrides.defState ?? "merge_guard",
    guardAnchorX: overrides.guardAnchorX ?? (overrides.posX ?? 4),
    guardAnchorY: overrides.guardAnchorY ?? (overrides.posY ?? 24),
    homeTx: overrides.homeTx ?? 4,
    homeTy: overrides.homeTy ?? 24,
    posX: overrides.posX ?? 4,
    posY: overrides.posY ?? 24,
  };
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

test("reaching the Town Core only acquires a combat target and does not auto-damage on arrival", () => {
  const game = createGame(12);
  const lane = game.lanes[0];
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 0);
  const attacker = createWaveUnit("raider", {
    atkCd: 3,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: coreApproach.pathIdx,
  });

  lane.units.push(attacker);
  tick(game, 1);

  assert.equal(corePad.hp, 12, "Town Core HP should stay unchanged until a real attack lands");
  assert.equal(lane.eliminated, false, "lane should stay alive when the core has not taken fatal damage");
  assert.equal(game.matchState, "active_survival", "arrival alone should not resolve or end survival");
  assert.ok(attacker.combatTarget, "attacker should lock onto the Town Core");
  assert.equal(attacker.combatTarget.kind, "fortress_pad");
  assert.equal(attacker.combatTarget.padId, corePad.padId);
});

test("wave units damage the Town Core over time instead of deleting it on contact", () => {
  const game = createGame(12);
  const lane = game.lanes[0];
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 0);
  const attacker = createWaveUnit("raider", {
    atkCd: 0,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: coreApproach.pathIdx,
  });

  lane.units.push(attacker);

  let damageTick = -1;
  for (let i = 0; i < 10; i += 1) {
    tick(game, 1);
    if (corePad.hp < 12) {
      damageTick = i + 1;
      break;
    }
  }

  assert.ok(damageTick > 0, "expected a real attack to damage the Town Core");
  assert.equal(corePad.hp, 6, "Town Core HP should drop by the attacker's damage, not by an automatic 1-point leak");
  assert.equal(lane.eliminated, false, "the lane should survive while the Town Core still has HP remaining");
  assert.ok(attacker.attackPulse > 0, "the unit should have performed a real attack to cause the damage");
});

test("destroying one Town Core eliminates that lane without ending the FFA survival match", () => {
  const game = createGame(12);
  const lane = game.lanes[0];
  const enemyLane = game.lanes[1];
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 0);
  const attacker = createWaveUnit("raider", {
    atkCd: 0,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: coreApproach.pathIdx,
  });

  lane.units.push(attacker);

  let ticksSpent = 0;
  while (!lane.eliminated && ticksSpent < 20) {
    tick(game, 1);
    ticksSpent += 1;
  }

  assert.equal(corePad.hp, 0, "the Town Core should be fully destroyed before defeat is applied");
  assert.equal(lane.eliminated, true, "lane defeat should come from Town Core destruction");
  assert.equal(enemyLane.eliminated, false, "the other lane should remain active after this fortress falls");
  assert.equal(game.phase, "playing", "the overall survival match should continue after a single elimination");
  assert.equal(game.officialWinnerLane, null, "surviving lanes should not auto-win when one fortress falls");
  assert.equal(game.matchState, "active_survival", "fatal core destruction should not move the match into PvP resolution");
  assert.equal(game.awaitingPostWinDecision, false, "post-win flow should not start from Town Core destruction");
});

test("defenders can kill attackers before the Town Core is destroyed", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 1);
  const attacker = createWaveUnit("raider", {
    hp: 12,
    maxHp: 12,
    baseDmg: 4,
    atkCd: 0,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: coreApproach.pathIdx,
  });
  const defender = createDefender("guardian", {
    posX: coreApproach.posX - 1,
    posY: coreApproach.posY - 1,
    pathIdx: coreApproach.pathIdx - 1,
    guardAnchorX: coreApproach.posX - 1,
    guardAnchorY: coreApproach.posY - 1,
    homeTx: 4,
    homeTy: 24,
  });

  lane.units.push(attacker);
  lane.units.push(defender);

  tick(game, 8);

  assert.equal(lane.units.some((unit) => unit.id === attacker.id), false, "the defender should be able to kill the core attacker");
  assert.ok(corePad.hp > 0, "the Town Core should remain alive if defenders stop the attackers in time");
  assert.equal(lane.eliminated, false, "the game should continue while the Town Core still has HP");
});

test("DEFEND stance barracks units intercept dungeon waves without relying on the legacy defender flag", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 1);
  const defender = createDefender("guardian", {
    id: "stance_guard_test",
    isDefender: false,
    sourceTeam: "red",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    posX: coreApproach.posX - 0.8,
    posY: coreApproach.posY - 0.8,
    pathIdx: coreApproach.pathIdx - 0.8,
    guardAnchorX: coreApproach.posX - 0.8,
    guardAnchorY: coreApproach.posY - 0.8,
    homeTx: 4,
    homeTy: 24,
  });
  const attacker = createWaveUnit("raider", {
    id: "stance_guard_wave",
    hp: 24,
    maxHp: 24,
    baseDmg: 4,
    atkCd: 0,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: coreApproach.pathIdx,
  });

  lane.units.push(defender, attacker);

  tick(game, 1);

  const serverDefender = lane.units.find((unit) => unit.id === defender.id);
  const serverAttacker = lane.units.find((unit) => unit.id === attacker.id);

  assert.ok(serverDefender, "the DEFEND stance barracks unit should remain alive during interception");
  assert.ok(serverAttacker, "the wave unit should still be alive during initial interception");
  assert.equal(serverDefender.isDefender, true, "canonical DEFEND stance should mirror into the legacy defender flag");
  assert.equal(serverDefender.pathContractType, "intercept", "engaged DEFEND units should publish the intercept path contract");
  assert.equal(serverDefender.combatTarget?.unitId, attacker.id, "the DEFEND stance unit should acquire the incoming wave");
  assert.equal(serverAttacker.combatTarget?.unitId, defender.id, "the wave unit should retaliate against the intercepting defender");
  assert.equal(corePad.hp, 20, "the Town Core should stay untouched while the DEFEND stance unit owns interception");
});

test("wave units already in defender contact range do not backpedal to a slot before attacking", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const coreApproach = getCoreApproachPosition(lane, 0);
  const defender = createDefender("guardian", {
    hp: 80,
    maxHp: 80,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: coreApproach.posX,
    posY: coreApproach.posY - 2,
    pathIdx: coreApproach.pathIdx - 2,
    guardAnchorX: coreApproach.posX,
    guardAnchorY: coreApproach.posY - 2,
    homeTx: 4,
    homeTy: 24,
  });
  const attacker = createWaveUnit("raider", {
    hp: 24,
    maxHp: 24,
    baseDmg: 4,
    atkCd: 0,
    posX: coreApproach.posX,
    posY: coreApproach.posY - 3,
    pathIdx: coreApproach.pathIdx - 3,
  });

  const initialAttackerY = attacker.posY;
  const initialDistance = Math.abs(attacker.posY - defender.posY);
  lane.units.push(defender, attacker);

  tick(game, 1);

  const serverAttacker = lane.units.find((unit) => unit.id === attacker.id);
  const serverDefender = lane.units.find((unit) => unit.id === defender.id);

  assert.ok(serverAttacker, "the wave unit should remain present during the opening contact");
  assert.ok(serverDefender, "the defender should remain present during the opening contact");
  assert.equal(serverAttacker.combatTarget?.unitId, defender.id, "the wave unit should stay locked on the nearby defender");
  assert.ok(serverAttacker.posY >= initialAttackerY, "the wave unit should not step backward away from the defender on first contact");
  assert.ok(Math.abs(serverAttacker.posY - serverDefender.posY) <= initialDistance, "the first combat tick should not increase the gap");
  assert.ok((serverAttacker.attackPulse || 0) > 0, "the wave unit should use its real attack once already in stop distance");
  assert.ok(serverDefender.hp < 80, "the defender should take the opening hit instead of kiting the attacker backward");
});

test("wave units intercepted near the town square do not damage the Town Core until defenders fall", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 1);
  const defender = createDefender("guardian", {
    hp: 18,
    maxHp: 18,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: coreApproach.posX,
    posY: coreApproach.posY - 0.8,
    pathIdx: coreApproach.pathIdx - 0.8,
    guardAnchorX: coreApproach.posX,
    guardAnchorY: coreApproach.posY - 0.8,
    homeTx: 4,
    homeTy: 24,
    defState: "merge_guard",
  });
  const attackers = [
    createWaveUnit("raider", {
      baseDmg: 3,
      atkCd: 0,
      atkCdTicks: 4,
      posX: coreApproach.posX - 0.2,
      posY: coreApproach.posY,
      pathIdx: coreApproach.pathIdx,
      spawnIndex: 0,
    }),
    createWaveUnit("raider", {
      baseDmg: 3,
      atkCd: 0,
      atkCdTicks: 4,
      posX: coreApproach.posX + 0.2,
      posY: coreApproach.posY,
      pathIdx: coreApproach.pathIdx,
      spawnIndex: 1,
    }),
  ];

  lane.units.push(defender, ...attackers);

  tick(game, 1);

  assert.equal(corePad.hp, 20, "the Town Core should not take damage while a merge-guard defender is intercepting");
  for (const attacker of attackers) {
    const serverAttacker = lane.units.find((unit) => unit.id === attacker.id);
    assert.ok(serverAttacker, "the attacker should still be alive during the initial interception");
    assert.equal(serverAttacker.combatTarget?.kind, "unit", "intercepted attackers should target the defender, not the Town Core");
    assert.equal(serverAttacker.combatTarget?.unitId, defender.id, "the defender should own aggro while alive");
    assert.equal(serverAttacker.combatState, "COMBAT", "intercepted attackers should enter COMBAT state");
  }

  tick(game, 6);

  assert.equal(corePad.hp, 20, "the Town Core should remain untouched while the defender is still alive");
  const livingDefender = lane.units.find((unit) => unit.id === defender.id);
  assert.ok(livingDefender, "the blocker should still be present during the interception window");
  livingDefender.hp = 0;
  tick(game, 1);

  let damageApplied = false;
  for (let i = 0; i < 12; i += 1) {
    tick(game, 1);
    if (corePad.hp < 20) {
      damageApplied = true;
      break;
    }
  }

  assert.equal(damageApplied, true, "surviving attackers should only damage the Town Core after the defender dies");
  assert.equal(lane.eliminated, false, "the match should not end early during defender interception");
});
