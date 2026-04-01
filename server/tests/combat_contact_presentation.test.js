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
    makeUnit("tt_heavy_infantry", { hp: 130, range: 0.10, path_speed: 0.20 }),
    makeUnit("tt_light_infantry", { hp: 105, range: 0.15, path_speed: 0.23 }),
    makeUnit("tt_spearman", { hp: 90, range: 0.17, path_speed: 0.22 }),
    makeUnit("raider", { hp: 60, range: 0.10, path_speed: 0.24 }),
  ]);
}

function createGame() {
  return simMl.createMLGame(2, {
    laneTeams: ["red", "blue"],
    startGold: 300,
    startIncome: 0,
  });
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

function issueLaneCommand(game, laneIndex, type, data = {}) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, `expected ${type} on lane ${laneIndex} to succeed`);
  return result;
}

function createBarracksUnit(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  return {
    id: overrides.id || typeKey,
    unitId: overrides.id || typeKey,
    unitTypeKey: typeKey,
    type: typeKey,
    allegianceKey: overrides.allegianceKey ?? "red",
    ownerLaneIndex: overrides.ownerLaneIndex ?? 0,
    ownerLane: overrides.ownerLaneIndex ?? 0,
    targetLaneIndex: overrides.targetLaneIndex ?? 0,
    sourceLaneIndex: overrides.sourceLaneIndex ?? 0,
    sourceTeam: overrides.sourceTeam ?? "red",
    sourceBarracksId: overrides.sourceBarracksId ?? "center",
    sourceBarracksKey: overrides.sourceBarracksId ?? "center",
    spawnSourceType: "barracks_roster",
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? def.hp,
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 0,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    stance: null,
    pathContractType: null,
    isWaveUnit: false,
    isDefender: false,
    combatState: overrides.combatState ?? "COMBAT",
    routeState: overrides.routeState ?? "COMBAT",
    movementMode: overrides.movementMode ?? "CombatEngage",
    commandState: overrides.commandState ?? "DEFEND",
    combatTarget: overrides.combatTarget ?? null,
    combatTargetId: overrides.combatTargetId ?? null,
    currentTargetId: overrides.currentTargetId ?? overrides.combatTargetId ?? null,
    combatTargetLockedUntilTick: 0,
    regroupUntilTick: 0,
    posX: overrides.posX ?? 4,
    posY: overrides.posY ?? 24,
    pathIdx: overrides.pathIdx ?? 24,
    routeWorldX: overrides.routeWorldX ?? overrides.posX ?? 4,
    routeWorldY: overrides.routeWorldY ?? overrides.posY ?? 24,
    currentSegment: overrides.currentSegment ?? "A_M",
    segmentProgress: overrides.segmentProgress ?? 0.5,
  };
}

function createWaveUnit(typeKey, overrides = {}) {
  const def = simMl.resolveUnitDef(typeKey);
  return {
    id: overrides.id || typeKey,
    unitId: overrides.id || typeKey,
    unitTypeKey: typeKey,
    type: typeKey,
    allegianceKey: "dungeon",
    ownerLaneIndex: -1,
    ownerLane: -1,
    targetLaneIndex: 0,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? def.hp,
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 1,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    stance: "ATTACK",
    pathContractType: "wave_lane",
    isWaveUnit: true,
    isDefender: false,
    combatState: overrides.combatState ?? "COMBAT",
    routeState: overrides.routeState ?? "COMBAT",
    movementMode: overrides.movementMode ?? "CombatEngage",
    commandState: overrides.commandState ?? "ATTACK",
    combatTarget: overrides.combatTarget ?? null,
    combatTargetId: overrides.combatTargetId ?? null,
    currentTargetId: overrides.currentTargetId ?? overrides.combatTargetId ?? null,
    combatTargetLockedUntilTick: 0,
    regroupUntilTick: 0,
    posX: overrides.posX ?? 5.8,
    posY: overrides.posY ?? 24,
    pathIdx: overrides.pathIdx ?? 24,
    routeWorldX: overrides.routeWorldX ?? overrides.posX ?? 5.8,
    routeWorldY: overrides.routeWorldY ?? overrides.posY ?? 24,
    currentSegment: overrides.currentSegment ?? "WA_A",
    segmentProgress: overrides.segmentProgress ?? 0.5,
  };
}

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
  seedHumanRoster();
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
  setUnitTypesForTests([]);
});

test("melee stop distances force shield and sword units closer to visible contact", () => {
  const shieldStop = simMl.getUnitStopDistance("tt_heavy_infantry", "tt_heavy_infantry");
  const swordStop = simMl.getUnitStopDistance("tt_light_infantry", "tt_heavy_infantry");
  const spearStop = simMl.getUnitStopDistance("tt_spearman", "tt_heavy_infantry");
  const swordVsRaiderStop = simMl.getUnitStopDistance("tt_light_infantry", "raider");

  assert.ok(shieldStop < swordStop, "shield units should still make contact before swords");
  assert.ok(swordStop < spearStop, "swords should still make contact before spears");
  assert.ok(shieldStop <= 1.55, `shield stop distance should read like direct frontline contact, got ${shieldStop}`);
  assert.ok(swordStop <= 2.1, `sword stop distance should no longer hover so far from contact, got ${swordStop}`);
  assert.ok(swordVsRaiderStop <= 2.1, `sword units should step further into dungeon mobs before dealing damage, got ${swordVsRaiderStop}`);
});

test("combat-contact snapshots serialize defend intent for lane defenders", () => {
  const game = createGame();
  const lane = game.lanes[0];

  const enemy = createWaveUnit("raider", {
    id: "enemy",
    posX: 5.75,
    posY: 24,
  });

  const defender = createBarracksUnit("tt_light_infantry", {
    id: "defender",
    posX: 4.1,
    posY: 24,
    combatTarget: {
      unitId: enemy.id,
      kind: "unit",
      laneIndex: lane.laneIndex,
    },
    combatTargetId: enemy.id,
    currentTargetId: enemy.id,
  });

  enemy.combatTarget = {
    unitId: defender.id,
    kind: "unit",
    laneIndex: lane.laneIndex,
  };
  enemy.combatTargetId = defender.id;
  enemy.currentTargetId = defender.id;

  lane.units.push(defender, enemy);

  const snapshot = simMl.createMLSnapshot(game);
  const defenderSnap = snapshot.lanes[0].units.find((unit) => unit.id === defender.id);

  assert.ok(defenderSnap, "expected the defender to be serialized into the ML snapshot");
  assert.equal(defenderSnap.presentationPhase, "CombatResolve");
  assert.equal(defenderSnap.presentationIntent, "Defend");
  assert.equal(defenderSnap.combatContact, true);
  assert.equal(defenderSnap.combatTargetId, enemy.id);
});

test("defend groups leave the waypoint and free-roam toward an intruder once combat starts", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const intruderOffset = { x: 0.4, y: -4.2 };
  const intruder = createWaveUnit("raider", {
    id: "intruder",
    posX: anchor.x + intruderOffset.x,
    posY: anchor.y + intruderOffset.y,
    baseSpeed: 0,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });

  const defenders = Array.from({ length: 6 }, (_, index) => createBarracksUnit("tt_light_infantry", {
    id: `engage_${index}`,
    posX: anchor.x + ((index % 3) - 1) * 0.9,
    posY: anchor.y + Math.floor(index / 3) * 0.8,
    routeWorldX: anchor.x + ((index % 3) - 1) * 0.9,
    routeWorldY: anchor.y + Math.floor(index / 3) * 0.8,
    baseSpeed: 0.24,
    atkCd: 0,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "AnchorJoin",
    commandState: "DEFEND",
  }));

  lane.units.push(...defenders, intruder);

  const intruderDirectionMagnitude = Math.max(0.001, Math.sqrt((intruderOffset.x * intruderOffset.x) + (intruderOffset.y * intruderOffset.y)));
  const intruderDirection = {
    x: intruderOffset.x / intruderDirectionMagnitude,
    y: intruderOffset.y / intruderDirectionMagnitude,
  };
  const startPositions = new Map(defenders.map((unit) => [unit.id, { x: unit.posX, y: unit.posY }]));

  tick(game, 4);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("engage_"));
  assert.equal(liveDefenders.length, defenders.length);
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === intruder.id),
    "every nearby defend unit should commit to the intruder once shared combat starts"
  );

  const movers = liveDefenders.filter((unit) => {
    const start = startPositions.get(unit.id);
    const towardIntruder = ((Number(unit.posX) - start.x) * intruderDirection.x)
      + ((Number(unit.posY) - start.y) * intruderDirection.y);
    const stopDistance = simMl.getUnitStopDistance(unit.type, intruder.type) + 0.35;
    const currentDistance = Math.sqrt(
      Math.pow(Number(unit.posX) - Number(intruder.posX), 2)
      + Math.pow(Number(unit.posY) - Number(intruder.posY), 2)
    );
    return towardIntruder >= 0.25 || currentDistance <= stopDistance;
  });

  assert.ok(
    movers.length >= 4,
    `expected the defend blob to step off the waypoint toward the intruder; only ${movers.length}/${liveDefenders.length} units advanced`
  );
});

test("defenders start charging toward an intruder on the first defend-bubble pickup tick", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const intruderOffset = { x: 0, y: -7.0 };
  const intruder = createWaveUnit("raider", {
    id: "intruder_charge",
    posX: anchor.x + intruderOffset.x,
    posY: anchor.y + intruderOffset.y,
    routeWorldX: anchor.x + intruderOffset.x,
    routeWorldY: anchor.y + intruderOffset.y,
    baseSpeed: 0,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });

  const defenders = Array.from({ length: 6 }, (_, index) => createBarracksUnit("tt_light_infantry", {
    id: `charge_${index}`,
    posX: anchor.x + ((index % 3) - 1) * 0.9,
    posY: anchor.y + Math.floor(index / 3) * 0.8,
    routeWorldX: anchor.x + ((index % 3) - 1) * 0.9,
    routeWorldY: anchor.y + Math.floor(index / 3) * 0.8,
    baseSpeed: 0.24,
    atkCd: 0,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "AnchorJoin",
    commandState: "DEFEND",
  }));

  lane.units.push(...defenders, intruder);

  const startPositions = new Map(defenders.map((unit) => [unit.id, { x: unit.posX, y: unit.posY }]));

  tick(game, 1);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("charge_"));
  const movers = liveDefenders.filter((unit) => {
    const start = startPositions.get(unit.id);
    const distanceClosed = Math.sqrt(
      Math.pow(Number(start.x) - Number(intruder.posX), 2)
      + Math.pow(Number(start.y) - Number(intruder.posY), 2)
    ) - Math.sqrt(
      Math.pow(Number(unit.posX) - Number(intruder.posX), 2)
      + Math.pow(Number(unit.posY) - Number(intruder.posY), 2)
    );
    return distanceClosed >= 0.12;
  });

  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === intruder.id),
    "defenders should acquire the intruder on the first pickup tick"
  );
  assert.ok(
    movers.length >= 3,
    `expected the defend line to visibly step toward the intruder immediately; only ${movers.length}/${liveDefenders.length} units advanced on the pickup tick`
  );
});

test("defenders prefer nearby individual hostiles before falling back to a shared target", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const leftIntruder = createWaveUnit("raider", {
    id: "intruder_left",
    posX: anchor.x - 1.35,
    posY: anchor.y - 4.0,
    baseSpeed: 0,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });
  const rightIntruder = createWaveUnit("raider", {
    id: "intruder_right",
    posX: anchor.x + 1.35,
    posY: anchor.y - 4.0,
    baseSpeed: 0,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });

  const defenders = [
    createBarracksUnit("tt_light_infantry", {
      id: "split_left_0",
      posX: anchor.x - 1.25,
      posY: anchor.y + 0.1,
      routeWorldX: anchor.x - 1.25,
      routeWorldY: anchor.y + 0.1,
      baseSpeed: 0.24,
      atkCd: 0,
      commandState: "DEFEND",
    }),
    createBarracksUnit("tt_light_infantry", {
      id: "split_left_1",
      posX: anchor.x - 0.8,
      posY: anchor.y + 0.8,
      routeWorldX: anchor.x - 0.8,
      routeWorldY: anchor.y + 0.8,
      baseSpeed: 0.24,
      atkCd: 0,
      commandState: "DEFEND",
    }),
    createBarracksUnit("tt_light_infantry", {
      id: "split_right_0",
      posX: anchor.x + 1.25,
      posY: anchor.y + 0.1,
      routeWorldX: anchor.x + 1.25,
      routeWorldY: anchor.y + 0.1,
      baseSpeed: 0.24,
      atkCd: 0,
      commandState: "DEFEND",
    }),
    createBarracksUnit("tt_light_infantry", {
      id: "split_right_1",
      posX: anchor.x + 0.8,
      posY: anchor.y + 0.8,
      routeWorldX: anchor.x + 0.8,
      routeWorldY: anchor.y + 0.8,
      baseSpeed: 0.24,
      atkCd: 0,
      commandState: "DEFEND",
    }),
  ];

  lane.units.push(...defenders, leftIntruder, rightIntruder);

  tick(game, 4);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("split_"));
  const chosenTargets = new Set(
    liveDefenders
      .map((unit) => unit.combatTarget?.unitId)
      .filter(Boolean)
  );

  assert.ok(
    chosenTargets.has(leftIntruder.id),
    "expected at least one defender to pick the left-side intruder"
  );
  assert.ok(
    chosenTargets.has(rightIntruder.id),
    "expected at least one defender to pick the right-side intruder instead of dogpiling a single shared target"
  );
});

test("defenders distribute across a clustered hostile blob instead of dogpiling one propagated target", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const defenders = Array.from({ length: 6 }, (_, index) => createBarracksUnit("tt_light_infantry", {
    id: `cluster_${index}`,
    posX: anchor.x + (((index % 3) - 1) * 0.55),
    posY: anchor.y + (Math.floor(index / 3) * 0.45),
    routeWorldX: anchor.x + (((index % 3) - 1) * 0.55),
    routeWorldY: anchor.y + (Math.floor(index / 3) * 0.45),
    baseSpeed: 0.24,
    atkCd: 0,
    commandState: "DEFEND",
  }));

  const intruders = [
    createWaveUnit("raider", {
      id: "cluster_intruder_a",
      posX: anchor.x - 0.8,
      posY: anchor.y - 4.4,
      routeWorldX: anchor.x - 0.8,
      routeWorldY: anchor.y - 4.4,
      baseSpeed: 0,
      atkCd: 999,
    }),
    createWaveUnit("raider", {
      id: "cluster_intruder_b",
      posX: anchor.x,
      posY: anchor.y - 4.8,
      routeWorldX: anchor.x,
      routeWorldY: anchor.y - 4.8,
      baseSpeed: 0,
      atkCd: 999,
    }),
    createWaveUnit("raider", {
      id: "cluster_intruder_c",
      posX: anchor.x + 0.8,
      posY: anchor.y - 4.4,
      routeWorldX: anchor.x + 0.8,
      routeWorldY: anchor.y - 4.4,
      baseSpeed: 0,
      atkCd: 999,
    }),
  ];

  lane.units.push(...defenders, ...intruders);

  tick(game, 2);

  const chosenTargets = new Set(
    lane.units
      .filter((unit) => unit && unit.id.startsWith("cluster_"))
      .map((unit) => unit.combatTarget?.unitId)
      .filter(Boolean)
  );

  assert.ok(
    chosenTargets.size >= 3,
    `expected the defenders to split across the nearby clustered hostiles, got ${chosenTargets.size} unique targets`
  );
});

test("defend units pick up an intruder while it is still near the gate approach, not only after it reaches local melee range", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const intruder = createWaveUnit("raider", {
    id: "early_intruder",
    posX: anchor.x,
    posY: anchor.y - 7.8,
    baseSpeed: 0,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });

  const defenders = [
    createBarracksUnit("tt_light_infantry", {
      id: "early_left",
      posX: anchor.x - 0.8,
      posY: anchor.y + 0.3,
      routeWorldX: anchor.x - 0.8,
      routeWorldY: anchor.y + 0.3,
      baseSpeed: 0.24,
      atkCd: 0,
      combatState: "IDLE",
      routeState: "MOVING",
      movementMode: "AnchorJoin",
      commandState: "DEFEND",
    }),
    createBarracksUnit("tt_light_infantry", {
      id: "early_right",
      posX: anchor.x + 0.8,
      posY: anchor.y + 0.3,
      routeWorldX: anchor.x + 0.8,
      routeWorldY: anchor.y + 0.3,
      baseSpeed: 0.24,
      atkCd: 0,
      combatState: "IDLE",
      routeState: "MOVING",
      movementMode: "AnchorJoin",
      commandState: "DEFEND",
    }),
  ];

  lane.units.push(...defenders, intruder);

  tick(game, 2);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("early_") && unit.id !== intruder.id);
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === intruder.id),
    "defenders should acquire the intruder while it is still on the gate approach"
  );
});

test("defenders prefer a close hostile inside the defend bubble even if it still lives in another lane container", () => {
  const game = createGame();
  const lane = game.lanes[0];
  const adjacentLane = game.lanes[1];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const defenders = Array.from({ length: 4 }, (_, index) => createBarracksUnit("tt_light_infantry", {
    id: `cross_lane_red_${index}`,
    posX: anchor.x + (index * 0.45),
    posY: anchor.y + 0.3,
    routeWorldX: anchor.x + (index * 0.45),
    routeWorldY: anchor.y + 0.3,
    baseSpeed: 0.24,
    atkCd: 0,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "AnchorJoin",
    commandState: "DEFEND",
  }));

  const closeYellowIntruder = createBarracksUnit("tt_light_infantry", {
    id: "yellow_intruder_close",
    allegianceKey: "yellow",
    ownerLaneIndex: 1,
    ownerLane: 1,
    sourceLaneIndex: 1,
    sourceTeam: "yellow",
    targetLaneIndex: lane.laneIndex,
    posX: anchor.x,
    posY: anchor.y - 4.5,
    routeWorldX: anchor.x,
    routeWorldY: anchor.y - 4.5,
    baseSpeed: 0.24,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
    commandState: "ATTACK",
  });

  const farSameLaneMob = createWaveUnit("raider", {
    id: "dungeon_far",
    posX: anchor.x,
    posY: anchor.y - 8.0,
    routeWorldX: anchor.x,
    routeWorldY: anchor.y - 8.0,
    baseSpeed: 0,
    atkCd: 999,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });

  lane.units.push(...defenders, farSameLaneMob);
  adjacentLane.units.push(closeYellowIntruder);

  tick(game, 2);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("cross_lane_red_"));
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === closeYellowIntruder.id),
    "defenders should prioritize the nearby yellow intruder inside the defend bubble over a farther same-lane hostile"
  );
});

test("exactly overlapping defenders break apart instead of staying as a single stacked point", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const defenders = [
    createBarracksUnit("tt_light_infantry", {
      id: "stack_a",
      posX: anchor.x,
      posY: anchor.y,
      routeWorldX: anchor.x,
      routeWorldY: anchor.y,
      baseSpeed: 0.24,
      atkCd: 0,
      combatState: "IDLE",
      routeState: "MOVING",
      movementMode: "AnchorJoin",
      commandState: "DEFEND",
    }),
    createBarracksUnit("tt_light_infantry", {
      id: "stack_b",
      posX: anchor.x,
      posY: anchor.y,
      routeWorldX: anchor.x,
      routeWorldY: anchor.y,
      baseSpeed: 0.24,
      atkCd: 0,
      combatState: "IDLE",
      routeState: "MOVING",
      movementMode: "AnchorJoin",
      commandState: "DEFEND",
    }),
  ];

  lane.units.push(...defenders);

  tick(game, 3);

  const [left, right] = lane.units.filter((unit) => unit && unit.id.startsWith("stack_"));
  const distance = Math.sqrt(
    Math.pow(Number(left.posX) - Number(right.posX), 2)
    + Math.pow(Number(left.posY) - Number(right.posY), 2)
  );

  assert.ok(distance >= 0.2, `expected stacked defenders to separate, got distance ${distance}`);
});

test("anchor-slot hold layout spreads defenders into a shallow line instead of a circular clump", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const defenders = Array.from({ length: 8 }, (_, index) => createBarracksUnit("tt_light_infantry", {
    id: `line_${index}`,
    posX: anchor.x,
    posY: anchor.y,
    routeWorldX: anchor.x,
    routeWorldY: anchor.y,
    baseSpeed: 0.24,
    atkCd: 0,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "AnchorJoin",
    commandState: "DEFEND",
  }));

  lane.units.push(...defenders);

  tick(game, 2);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("line_"));
  const distinctAnchorColumns = new Set(
    liveDefenders.map((unit) => Number((Number(unit.anchorTargetX) || 0).toFixed(2)))
  );

  assert.ok(
    distinctAnchorColumns.size >= 3,
    `expected defend units to spread laterally across multiple hold columns, got ${distinctAnchorColumns.size}`
  );
});

test("melee surround wraps a stationary intruder instead of leaving all defenders on the approach side", () => {
  const game = createGame();
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, "expected defend mode to expose an outside gate anchor");

  const anchor = lane.outsideGateAnchor;
  const defenders = Array.from({ length: 12 }, (_, index) => createBarracksUnit("tt_light_infantry", {
    id: `surround_${index}`,
    posX: anchor.x + (((index % 4) - 1.5) * 0.6),
    posY: anchor.y + (Math.floor(index / 4) * 0.5),
    routeWorldX: anchor.x + (((index % 4) - 1.5) * 0.6),
    routeWorldY: anchor.y + (Math.floor(index / 4) * 0.5),
    baseSpeed: 0.24,
    atkCd: 0,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "AnchorJoin",
    commandState: "DEFEND",
  }));

  const intruder = createWaveUnit("raider", {
    id: "surround_intruder",
    posX: anchor.x,
    posY: anchor.y - 4.5,
    routeWorldX: anchor.x,
    routeWorldY: anchor.y - 4.5,
    baseSpeed: 0,
    atkCd: 999,
    hp: 300,
    maxHp: 300,
    combatState: "IDLE",
    routeState: "MOVING",
    movementMode: "LaneTravel",
  });

  lane.units.push(...defenders, intruder);

  tick(game, 60);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("surround_"));
  const stopDistance = simMl.getUnitStopDistance("tt_light_infantry", "raider") + 0.1;
  const defendersAboveTarget = liveDefenders.filter((unit) => unit.posY < intruder.posY - 0.5);
  const defendersLeftOfTarget = liveDefenders.filter((unit) => unit.posX < intruder.posX - 0.5);
  const defendersRightOfTarget = liveDefenders.filter((unit) => unit.posX > intruder.posX + 0.5);
  const defendersInContact = liveDefenders.filter((unit) =>
    Math.hypot(Number(unit.posX) - Number(intruder.posX), Number(unit.posY) - Number(intruder.posY)) <= stopDistance
  );

  assert.ok(
    defendersInContact.length >= 2,
    `expected several defenders to reach true contact around one intruder, got ${defendersInContact.length}`
  );
  assert.ok(
    defendersAboveTarget.length >= 2,
    `expected surround behavior to wrap defenders around the target, got ${defendersAboveTarget.length} above-target units`
  );
  assert.ok(
    defendersLeftOfTarget.length >= 2 && defendersRightOfTarget.length >= 2,
    "expected defenders to occupy both lateral sides of the target instead of staying in a single approach blob"
  );
});


