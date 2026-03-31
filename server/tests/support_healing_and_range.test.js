"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 160,
    startIncome: 10,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

let nextUnitId = 1;

function makeUnit(key, options = {}) {
  return {
    id: options.id || key,
    key,
    name: options.name || key,
    enabled: true,
    send_cost: options.send_cost ?? 10,
    build_cost: options.build_cost ?? 10,
    income: options.income ?? 1,
    hp: options.hp ?? 100,
    attack_damage: options.attack_damage ?? 10,
    attack_speed: options.attack_speed ?? 1,
    path_speed: options.path_speed ?? 0.2,
    range: options.range ?? 0.1,
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

function seedHumanRoster() {
  setUnitTypesForTests([
    makeUnit("tt_heavy_infantry", {
      hp: 130, attack_damage: 9, attack_speed: 0.9, path_speed: 0.2, range: 0.10,
      send_cost: 16, build_cost: 19, bounty: 4, damage_type: "PHYSICAL", armor_type: "HEAVY", damage_reduction_pct: 10,
    }),
    makeUnit("tt_light_infantry", {
      hp: 105, attack_damage: 10, attack_speed: 1.0, path_speed: 0.23, range: 0.15,
      send_cost: 14, build_cost: 18, bounty: 4, damage_type: "PHYSICAL", armor_type: "MEDIUM", damage_reduction_pct: 4,
    }),
    makeUnit("tt_spearman", {
      hp: 90, attack_damage: 8, attack_speed: 1.0, path_speed: 0.22, range: 0.17,
      send_cost: 12, build_cost: 14, bounty: 3, damage_type: "PIERCE", armor_type: "LIGHT", damage_reduction_pct: 2,
    }),
    makeUnit("tt_mage", {
      hp: 78, attack_damage: 14, attack_speed: 0.95, path_speed: 0.2, range: 0.31,
      send_cost: 18, build_cost: 21, bounty: 5, damage_type: "MAGIC", armor_type: "LIGHT",
    }),
    makeUnit("tt_archer", {
      hp: 74, attack_damage: 11, attack_speed: 1.05, path_speed: 0.23, range: 0.36,
      send_cost: 16, build_cost: 18, bounty: 4, damage_type: "PIERCE", armor_type: "LIGHT",
    }),
    makeUnit("tt_mounted_priest", {
      hp: 76, attack_damage: 3, attack_speed: 0.9, path_speed: 0.21, range: 0.44,
      send_cost: 17, build_cost: 20, bounty: 4, damage_type: "MAGIC", armor_type: "LIGHT",
      special_props: { supportRole: "healer", healAmount: 8 },
    }),
    makeUnit("tt_priest", {
      hp: 92, attack_damage: 4, attack_speed: 0.95, path_speed: 0.2, range: 0.47,
      send_cost: 22, build_cost: 27, bounty: 6, damage_type: "MAGIC", armor_type: "LIGHT", damage_reduction_pct: 2,
      special_props: { supportRole: "healer", healAmount: 12 },
    }),
    makeUnit("tt_high_priest", {
      hp: 110, attack_damage: 5, attack_speed: 1.0, path_speed: 0.19, range: 0.50,
      send_cost: 28, build_cost: 34, bounty: 8, damage_type: "MAGIC", armor_type: "MEDIUM", damage_reduction_pct: 6,
      special_props: { supportRole: "healer", healAmount: 17 },
    }),
    makeUnit("tt_commander", {
      hp: 170, attack_damage: 6, attack_speed: 1.0, path_speed: 0.19, range: 0.52,
      send_cost: 55, build_cost: 55, bounty: 11, damage_type: "MAGIC", armor_type: "MEDIUM", damage_reduction_pct: 8,
      special_props: { supportRole: "healer", healAmount: 22 },
    }),
    makeUnit("raider", {
      hp: 60, attack_damage: 6, attack_speed: 1.0, path_speed: 0.24, range: 0.1,
      send_cost: 10, build_cost: 10, bounty: 1, damage_type: "NORMAL", armor_type: "LIGHT",
    }),
  ]);
}

function createGame(startGold = 300) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold,
    startIncome: 0,
  });
}

function createBarracksUnit(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  const id = overrides.id || `fort_unit_${nextUnitId++}`;
  const ownerLaneIndex = overrides.ownerLaneIndex ?? 0;
  const targetLaneIndex = overrides.targetLaneIndex ?? ownerLaneIndex;
  const sourceBarracksId = overrides.sourceBarracksId ?? "center";

  return {
    id,
    unitId: id,
    unitTypeKey: typeKey,
    allegianceKey: overrides.allegianceKey ?? "red",
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    targetLaneIndex,
    sourceLaneIndex: overrides.sourceLaneIndex ?? ownerLaneIndex,
    sourceTeam: overrides.sourceTeam ?? "red",
    sourceBarracksId,
    sourceBarracksKey: sourceBarracksId,
    spawnSourceType: overrides.spawnSourceType ?? "barracks_roster",
    groupId: overrides.groupId ?? `packet:${ownerLaneIndex}:${sourceBarracksId}:${targetLaneIndex}`,
    type: typeKey,
    skinKey: null,
    isHero: overrides.isHero ?? false,
    heroKey: overrides.heroKey ?? null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 24,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? (overrides.hp ?? def.hp),
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 0,
    stance: null,
    pathContractType: null,
    isWaveUnit: false,
    isDefender: false,
    combatState: "IDLE",
    routeState: "IDLE",
    movementMode: "FORMATION_JOIN",
    combatTarget: null,
    combatTargetId: null,
    currentTargetId: null,
    combatTargetLockedUntilTick: 0,
    regroupUntilTick: 0,
    defState: "merge_guard",
    guardAnchorX: overrides.guardAnchorX ?? (overrides.posX ?? 4),
    guardAnchorY: overrides.guardAnchorY ?? (overrides.posY ?? 24),
    homeTx: 4,
    homeTy: 24,
    posX: overrides.posX ?? 4,
    posY: overrides.posY ?? 24,
  };
}

function createWaveUnit(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  const id = overrides.id || `wave_unit_${nextUnitId++}`;
  return {
    id,
    unitId: id,
    unitTypeKey: typeKey,
    allegianceKey: "dungeon",
    ownerLaneIndex: -1,
    ownerLane: -1,
    targetLaneIndex: 0,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
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
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LANE_TRAVEL",
    combatTarget: null,
    combatTargetId: null,
    currentTargetId: null,
    combatTargetLockedUntilTick: 0,
    regroupUntilTick: 0,
    hasBreachedTownCore: false,
    spawnIndex: 0,
    posX: overrides.posX ?? 4,
    posY: overrides.posY ?? 24,
  };
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

function measureSingleHeal(healerType) {
  const game = createGame();
  const lane = game.lanes[0];
  const healer = createBarracksUnit(healerType, { id: `${healerType}_caster`, posX: 4, posY: 24, atkCd: 0 });
  const ally = createBarracksUnit("tt_heavy_infantry", { id: `${healerType}_ally`, hp: 90, maxHp: 130, posX: 4.4, posY: 23.4 });
  lane.units.push(healer, ally);
  tick(game, 1);
  return ally.hp - 90;
}

test.beforeEach(() => {
  nextUnitId = 1;
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
  seedHumanRoster();
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
  setUnitTypesForTests([]);
});

test("human roster range ordering keeps shield, sword, spear, mage, archer, then priest", () => {
  const ordered = [
    "tt_heavy_infantry",
    "tt_light_infantry",
    "tt_spearman",
    "tt_mage",
    "tt_archer",
    "tt_priest",
  ];

  const ranges = ordered.map((key) => simMl.resolveTowerDef(key).range);
  for (let i = 0; i < ranges.length - 1; i += 1)
    assert.ok(ranges[i] < ranges[i + 1], `${ordered[i]} should stay shorter than ${ordered[i + 1]}`);

  const shieldStop = simMl.getUnitStopDistance("tt_heavy_infantry", "tt_heavy_infantry");
  const swordStop = simMl.getUnitStopDistance("tt_light_infantry", "tt_heavy_infantry");
  const spearStop = simMl.getUnitStopDistance("tt_spearman", "tt_heavy_infantry");
  const swordVsRaiderStop = simMl.getUnitStopDistance("tt_light_infantry", "raider");

  assert.ok(shieldStop < swordStop, "shield units should make contact before swords");
  assert.ok(swordStop < spearStop, "swords should make contact before spears");
  assert.ok(shieldStop < 2.5, `shield stop distance should read like frontline contact, got ${shieldStop}`);
  assert.ok(swordStop < 2.4, `sword stop distance should stay visibly closer to contact, got ${swordStop}`);
  assert.ok(swordVsRaiderStop < 2.4, `sword units should not hover too far from dungeon mobs, got ${swordVsRaiderStop}`);
});

test("priests heal wounded allies before trying to damage enemies", () => {
  const game = createGame();
  const lane = game.lanes[0];
  const priest = createBarracksUnit("tt_priest", { id: "priest", posX: 4, posY: 24, atkCd: 0 });
  const ally = createBarracksUnit("tt_heavy_infantry", { id: "ally", hp: 100, maxHp: 130, posX: 4.5, posY: 23.4, atkCd: 999 });
  const enemy = createWaveUnit("raider", { id: "enemy", hp: 60, maxHp: 60, posX: 4.6, posY: 23.6, atkCd: 999 });

  lane.units.push(priest, ally, enemy);
  tick(game, 1);

  assert.equal(ally.hp, 112, "tier-two priest should restore its configured heal amount");
  assert.equal(enemy.hp, 60, "healer should not burn its cast on enemy damage while an ally is wounded");
  assert.equal(priest.combatTargetId, ally.id, "healer feedback should point at the healed ally");
  assert.ok((priest.attackPulse || 0) > 0, "healer should emit an attack/support pulse when the heal lands");
});

test("higher priest tiers heal more per cast", () => {
  const clericHeal = measureSingleHeal("tt_mounted_priest");
  const priestHeal = measureSingleHeal("tt_priest");
  const highPriestHeal = measureSingleHeal("tt_high_priest");

  assert.equal(clericHeal, 8);
  assert.equal(priestHeal, 12);
  assert.equal(highPriestHeal, 17);
  assert.ok(clericHeal < priestHeal && priestHeal < highPriestHeal);
});

test("mages stay on the magic-damage branch instead of healing allies", () => {
  const game = createGame();
  const lane = game.lanes[0];
  const mage = createBarracksUnit("tt_mage", { id: "mage", posX: 4, posY: 24, atkCd: 0 });
  const ally = createBarracksUnit("tt_heavy_infantry", { id: "ally", hp: 100, maxHp: 130, posX: 4.4, posY: 23.8, atkCd: 999 });
  const enemy = createWaveUnit("raider", { id: "enemy", hp: 60, maxHp: 60, posX: 4.8, posY: 23.9, atkCd: 999 });

  lane.units.push(mage, ally, enemy);
  tick(game, 1);

  assert.equal(ally.hp, 100, "mages should not heal wounded allies");
  assert.equal(enemy.hp, 46, "mages should spend their cast on magic damage");
  assert.equal(mage.combatTargetId, enemy.id, "mage target should stay enemy-facing");
});
