"use strict";

const test = require("node:test");
const assert = require("node:assert/strict");

const gameConfig = require("../gameConfig");
const { setUnitTypesForTests } = require("../unitTypes");
const simMl = require("../sim-multilane");

const VALID_MULTILANE_CONFIG = {
  globalParams: {
    startGold: 200,
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
    hp: options.hp ?? 36,
    attack_damage: options.attack_damage ?? 6,
    attack_speed: options.attack_speed ?? 20,
    path_speed: options.path_speed ?? 0.4,
    range: options.range ?? 0.08,
    projectile_travel_ticks: options.projectile_travel_ticks ?? 6,
    damage_type: options.damage_type || "NORMAL",
    armor_type: options.armor_type || "LIGHT",
    damage_reduction_pct: options.damage_reduction_pct ?? 0,
    bounty: options.bounty ?? 1,
    special_props: options.special_props || {},
    abilities: options.abilities || [],
    barracks_scales_hp: options.barracks_scales_hp ?? false,
    barracks_scales_dmg: options.barracks_scales_dmg ?? false,
  };
}

function createGame() {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "yellow"],
    startGold: 200,
    startIncome: 0,
    teamHpStart: 20,
  });
}

function createSnapshotUnit(id, overrides = {}) {
  return {
    id,
    unitId: id,
    ownerLaneIndex: overrides.ownerLaneIndex ?? 0,
    ownerLane: overrides.ownerLane ?? overrides.ownerLaneIndex ?? 0,
    targetLaneIndex: overrides.targetLaneIndex ?? 0,
    laneId: overrides.laneId ?? overrides.targetLaneIndex ?? 0,
    sourceLaneIndex: overrides.sourceLaneIndex ?? 0,
    sourceTeam: overrides.sourceTeam ?? "red",
    sourceBarracksId: overrides.sourceBarracksId ?? "center",
    sourceBarracksKey: overrides.sourceBarracksKey ?? "center",
    spawnSourceType: overrides.spawnSourceType ?? "barracks_roster",
    type: overrides.type ?? "raider",
    unitTypeKey: overrides.unitTypeKey ?? "raider",
    allegianceKey: overrides.allegianceKey ?? "red",
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    hp: overrides.hp ?? 36,
    maxHp: overrides.maxHp ?? 36,
    baseDmg: overrides.baseDmg ?? 6,
    baseSpeed: overrides.baseSpeed ?? 0.4,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? 5,
    armorType: overrides.armorType ?? "LIGHT",
    damageReductionPct: overrides.damageReductionPct ?? 0,
    abilities: [],
    bounty: 1,
    isWaveUnit: overrides.isWaveUnit ?? false,
    isDefender: false,
    stance: overrides.stance ?? "ATTACK",
    combatTarget: null,
    combatTargetId: null,
    currentTargetId: null,
    combatState: overrides.combatState ?? "IDLE",
    routeState: overrides.routeState ?? "MOVING",
    movementMode: overrides.movementMode ?? "LaneTravel",
    commandState: overrides.commandState ?? "ATTACK",
    routeType: overrides.routeType ?? "CENTER_CROSS",
    routeStartNode: overrides.routeStartNode ?? "A",
    routeTargetNode: overrides.routeTargetNode ?? "B",
    routeSegments: overrides.routeSegments ?? ["A_B"],
    routeSegmentIndex: overrides.routeSegmentIndex ?? 0,
    segmentProgress: overrides.segmentProgress ?? 0.25,
    currentSegment: overrides.currentSegment ?? "A_B",
    pathIdx: overrides.pathIdx ?? 0,
    posX: overrides.posX,
    posY: overrides.posY,
    routeWorldX: overrides.routeWorldX ?? null,
    routeWorldY: overrides.routeWorldY ?? null,
  };
}

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
  setUnitTypesForTests([makeUnit("raider")]);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

test("ML snapshot serializes projectile endpoint ids and uses live unit routeWorld when explicit routeWorld is absent", () => {
  const game = createGame();
  const lane = game.lanes[0];

  lane.projectiles.push({
    id: "proj_contract",
    ownerLane: lane.laneIndex,
    sourceKind: "unit",
    sourceId: "archer_01",
    targetId: "town_core_pad",
    projectileType: "mage",
    damageType: "MAGIC",
    isSplash: false,
    fromX: 12.5,
    fromY: 23.75,
    toX: 6.5,
    toY: 25.25,
    ticksRemaining: 2,
    ticksTotal: 4,
  });

  lane.units.push(createSnapshotUnit("route_unit_live_pos", {
    posX: 12.5,
    posY: 23.75,
    routeWorldX: null,
    routeWorldY: null,
    pathIdx: 4,
  }));

  const snapshot = simMl.createMLSnapshot(game);
  const projectile = snapshot.lanes[0].projectiles.find((entry) => entry && entry.id === "proj_contract");
  const unit = snapshot.lanes[0].units.find((entry) => entry && entry.id === "route_unit_live_pos");

  assert.ok(projectile, "expected the serialized projectile to appear in the snapshot");
  assert.equal(projectile.sourceId, "archer_01");
  assert.equal(projectile.targetId, "town_core_pad");

  assert.ok(unit, "expected the live route unit to appear in the snapshot");
  assert.equal(unit.routeWorldX, 12.5);
  assert.equal(unit.routeWorldY, 23.75);
});

test("ML snapshot leaves routeWorld null when a unit has no authoritative live world position", () => {
  const game = createGame();
  const lane = game.lanes[0];

  lane.units.push(createSnapshotUnit("route_unit_missing_live_pos", {
    posX: undefined,
    posY: undefined,
    routeWorldX: null,
    routeWorldY: null,
    pathIdx: 3,
  }));

  const snapshot = simMl.createMLSnapshot(game);
  const unit = snapshot.lanes[0].units.find((entry) => entry && entry.id === "route_unit_missing_live_pos");

  assert.ok(unit, "expected the lane unit to appear in the snapshot");
  assert.equal(unit.routeWorldX, null);
  assert.equal(unit.routeWorldY, null);
});

test("ML public config serializes authoritative battlefield route segment world and route-space polylines", () => {
  const config = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });

  const waveSegment = config.battlefieldLayout.routeSegments.find((segment) => segment && segment.segmentId === "WA_A");
  assert.ok(waveSegment, "expected wave route segment WA_A in battlefieldLayout");
  assert.ok(Array.isArray(waveSegment.points) && waveSegment.points.length >= 2, "expected authored world polyline points");
  assert.ok(Array.isArray(waveSegment.routeSpacePoints) && waveSegment.routeSpacePoints.length >= 2, "expected authoritative route-space polyline points");
  assert.equal(waveSegment.points.length, waveSegment.routeSpacePoints.length);
  assert.deepEqual(waveSegment.routeSpacePoints[0], { x: 0, y: 0 });
  assert.deepEqual(waveSegment.routeSpacePoints[waveSegment.routeSpacePoints.length - 1], { x: -24, y: 24 });

  const perimeterSegment = config.battlefieldLayout.routeSegments.find((segment) => segment && segment.segmentId === "A_B");
  assert.ok(perimeterSegment, "expected perimeter route segment A_B in battlefieldLayout");
  assert.ok(Array.isArray(perimeterSegment.points) && perimeterSegment.points.length === 3, "expected perimeter world polyline to include a control point");
  assert.ok(Array.isArray(perimeterSegment.routeSpacePoints) && perimeterSegment.routeSpacePoints.length === 3, "expected perimeter route-space polyline to include a control point");
  assert.equal(perimeterSegment.points.length, perimeterSegment.routeSpacePoints.length);
});
