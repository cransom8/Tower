"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 500,
    startIncome: 10,
    livesStart: 20,
    teamHpStart: 20,
    buildPhaseTicks: 600,
    transitionPhaseTicks: 200,
  },
};

function makeUnit(key, options = {}) {
  return {
    id: key,
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
    range: options.range ?? 0.2,
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

function seedFortressRoster() {
  setUnitTypesForTests([
    makeUnit("tt_peasant", {
      hp: 105,
      attack_damage: 12,
      attack_speed: 1,
      path_speed: 0.22,
      range: 0.12,
      damage_type: "PHYSICAL",
      armor_type: "MEDIUM",
    }),
    makeUnit("tt_heavy_infantry", {
      hp: 125,
      attack_damage: 9,
      attack_speed: 0.9,
      path_speed: 0.19,
      range: 0.10,
      damage_type: "PHYSICAL",
      armor_type: "HEAVY",
      damage_reduction_pct: 8,
    }),
    makeUnit("tt_mounted_priest", {
      hp: 90,
      attack_damage: 4,
      attack_speed: 1,
      path_speed: 0.18,
      range: 0.45,
      damage_type: "MAGIC",
      armor_type: "LIGHT",
      special_props: { supportRole: "healer", healAmount: 12 },
    }),
    makeUnit("tt_mage", {
      hp: 82,
      attack_damage: 14,
      attack_speed: 0.95,
      path_speed: 0.2,
      range: 0.36,
      damage_type: "MAGIC",
      armor_type: "LIGHT",
    }),
    makeUnit("tt_archer", {
      hp: 76,
      attack_damage: 11,
      attack_speed: 1.05,
      path_speed: 0.23,
      range: 0.38,
      damage_type: "PIERCE",
      armor_type: "LIGHT",
    }),
    makeUnit("wave_raider", {
      hp: 70,
      attack_damage: 7,
      attack_speed: 1,
      path_speed: 0.23,
      range: 0.10,
      damage_type: "NORMAL",
      armor_type: "LIGHT",
    }),
  ]);
}

function createGame(startGold = 2500) {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold,
    startIncome: 0,
  });
}

function laneSnapshot(game, laneIndex = 0) {
  return simMl.createMLSnapshot(game).lanes[laneIndex];
}

function findPad(snapshotLane, padId) {
  return (snapshotLane && snapshotLane.fortressPads || []).find((pad) => pad && pad.padId === padId) || null;
}

function findUpgrade(snapshotPad, upgradeKey) {
  return (snapshotPad && snapshotPad.buildingUpgrades || []).find((upgrade) => upgrade && upgrade.upgradeKey === upgradeKey) || null;
}

function advanceUntil(game, predicate, maxTicks = 4000) {
  for (let tick = 0; tick < maxTicks; tick += 1) {
    if (predicate())
      return;
    simMl.mlTick(game);
  }
  assert.fail("Timed out waiting for multilane state to settle");
}

function finishPadConstruction(game, laneIndex, padId) {
  advanceUntil(game, () => {
    const pad = findPad(laneSnapshot(game, laneIndex), padId);
    return !!(pad && !pad.isConstructing);
  });
}

function act(game, laneIndex, type, data) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, result.reason || `Expected '${type}' to succeed`);
  return result;
}

function fail(game, laneIndex, type, data, pattern) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, false, `Expected '${type}' to fail`);
  assert.match(String(result.reason || ""), pattern);
  return result;
}

function upgradeTownCoreToTier(game, laneIndex, targetTier) {
  let townCore = findPad(laneSnapshot(game, laneIndex), "town_core_pad");
  while ((townCore && townCore.tier) < targetTier) {
    act(game, laneIndex, "upgrade_building", { padId: "town_core_pad" });
    finishPadConstruction(game, laneIndex, "town_core_pad");
    townCore = findPad(laneSnapshot(game, laneIndex), "town_core_pad");
  }
}

function buildPad(game, laneIndex, padId) {
  act(game, laneIndex, "build_on_pad", { padId });
  finishPadConstruction(game, laneIndex, padId);
}

function createRosterUnit(rosterKey, typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  const ownerLaneIndex = overrides.ownerLaneIndex ?? 0;
  const targetLaneIndex = overrides.targetLaneIndex ?? (ownerLaneIndex === 0 ? 1 : 0);
  return {
    id: overrides.id || `${rosterKey}_${typeKey}`,
    unitId: overrides.id || `${rosterKey}_${typeKey}`,
    unitTypeKey: typeKey,
    allegianceKey: overrides.allegianceKey ?? "red",
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    targetLaneIndex,
    sourceLaneIndex: overrides.sourceLaneIndex ?? ownerLaneIndex,
    sourceTeam: overrides.sourceTeam ?? "red",
    sourceBarracksId: overrides.sourceBarracksId ?? "center",
    sourceBarracksKey: overrides.sourceBarracksId ?? "center",
    spawnSourceType: overrides.spawnSourceType ?? "barracks_roster",
    type: typeKey,
    rosterKey,
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 24,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? (overrides.hp ?? def.hp),
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    damageType: overrides.damageType ?? def.damageType,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 0,
    isWaveUnit: false,
    isDefender: false,
    combatState: "IDLE",
    routeState: "IDLE",
    movementMode: "AnchorJoin",
    combatTarget: null,
    combatTargetId: null,
    currentTargetId: null,
    combatTargetLockedUntilTick: 0,
    regroupUntilTick: 0,
    posX: overrides.posX ?? 4,
    posY: overrides.posY ?? 24,
    guardAnchorX: overrides.guardAnchorX ?? (overrides.posX ?? 4),
    guardAnchorY: overrides.guardAnchorY ?? (overrides.posY ?? 24),
    homeTx: 4,
    homeTy: 24,
  };
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
  seedFortressRoster();
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
  setUnitTypesForTests([]);
});

test("built branch snapshots expose building upgrade cards", () => {
  const game = createGame();
  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "blacksmith_pad");

  const blacksmithPad = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  const shieldArmor = findUpgrade(blacksmithPad, "shield_armor");
  const frontlineDamage = findUpgrade(blacksmithPad, "frontline_damage");

  assert.ok(blacksmithPad, "expected blacksmith pad snapshot");
  assert.equal(blacksmithPad.upgradePanelDescription, "Frontline melee durability and damage.");
  assert.ok(shieldArmor, "expected shield armor upgrade");
  assert.ok(frontlineDamage, "expected frontline damage upgrade");
  assert.equal(shieldArmor.cost, 100);
  assert.equal(shieldArmor.purchaseCount, 0);
  assert.equal(shieldArmor.currentBonusText, "+0.0%");
  assert.equal(shieldArmor.canPurchase, true);
});

test("repeatable building upgrades spend gold and update live shield units", () => {
  const game = createGame();
  const lane = game.lanes[0];
  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "blacksmith_pad");

  const shieldUnit = createRosterUnit("shieldman", "shield_t1", {
    id: "shield_guard_test",
    posX: 4,
    posY: 24,
  });
  lane.units.push(shieldUnit);

  const goldBefore = Math.floor(lane.gold);
  act(game, 0, "purchase_building_upgrade", { padId: "blacksmith_pad", upgradeKey: "shield_armor" });

  const blacksmithPad = findPad(laneSnapshot(game, 0), "blacksmith_pad");
  const shieldArmor = findUpgrade(blacksmithPad, "shield_armor");
  assert.equal(goldBefore - Math.floor(lane.gold), 100);
  assert.equal(shieldArmor.purchaseCount, 1);
  assert.equal(shieldArmor.currentBonusText, "+0.5%");
  assert.equal(shieldUnit.directDamageReductionPctBonus, 0.5);
  assert.equal(shieldUnit.damageReductionPct, 8.5);
});

test("one-time building upgrades can only be purchased once", () => {
  const game = createGame();
  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "temple_pad");

  act(game, 0, "purchase_building_upgrade", { padId: "temple_pad", upgradeKey: "chain_heal" });
  fail(game, 0, "purchase_building_upgrade", { padId: "temple_pad", upgradeKey: "chain_heal" }, /already purchased/i);

  const templePad = findPad(laneSnapshot(game, 0), "temple_pad");
  const chainHeal = findUpgrade(templePad, "chain_heal");
  assert.equal(chainHeal.isPurchased, true);
  assert.equal(chainHeal.canPurchase, false);
});

test("wall hp upgrades immediately increase built wall durability", () => {
  const game = createGame();
  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "lumber_mill_pad");
  buildPad(game, 0, "wall_front_left_01_pad");

  const wallBefore = findPad(laneSnapshot(game, 0), "wall_front_left_01_pad");
  act(game, 0, "purchase_building_upgrade", { padId: "lumber_mill_pad", upgradeKey: "wall_hp" });
  act(game, 0, "purchase_building_upgrade", { padId: "lumber_mill_pad", upgradeKey: "wall_hp" });
  const wallAfter = findPad(laneSnapshot(game, 0), "wall_front_left_01_pad");

  assert.ok(wallAfter.maxHp > wallBefore.maxHp, "expected wall max hp to increase after the wall hp upgrade");
  assert.equal(findUpgrade(findPad(laneSnapshot(game, 0), "lumber_mill_pad"), "wall_hp").currentBonusText, "+1.0%");
});

test("chain heal upgrade heals the primary target and two extra nearby allies", () => {
  const game = createGame();
  const lane = game.lanes[0];
  upgradeTownCoreToTier(game, 0, 2);
  buildPad(game, 0, "temple_pad");

  const healer = createRosterUnit("cleric", "support_t1", { id: "cleric_caster", posX: 4.0, posY: 24.0, atkCd: 0 });
  const allyOne = createRosterUnit("militia", "infantry_t1", { id: "ally_one", hp: 60, maxHp: 105, posX: 4.3, posY: 24.1 });
  const allyTwo = createRosterUnit("shieldman", "shield_t1", { id: "ally_two", hp: 80, maxHp: 125, posX: 4.6, posY: 23.8 });
  const allyThree = createRosterUnit("militia", "infantry_t1", { id: "ally_three", hp: 75, maxHp: 105, posX: 4.8, posY: 24.2 });
  lane.units.push(healer, allyOne, allyTwo, allyThree);

  act(game, 0, "purchase_building_upgrade", { padId: "temple_pad", upgradeKey: "chain_heal" });
  tick(game, 1);

  assert.ok(allyOne.hp > 60, "expected the most wounded ally to receive the primary heal");
  assert.ok(allyTwo.hp > 80, "expected the chain heal to jump to a second ally");
  assert.ok(allyThree.hp > 75, "expected the chain heal to jump to a third ally");
});
