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
]);

test.beforeEach(() => {
  gameConfig.setActiveConfig("multilane", VALID_MULTILANE_CONFIG);
});

test.afterEach(() => {
  gameConfig.setActiveConfig("multilane", null);
});

function createGame() {
  return simMl.createMLGame(4, {
    laneTeams: ["red", "yellow", "blue", "green"],
    startGold: 200,
    startIncome: 0,
    teamHpStart: 20,
  });
}

function createTwoPlayerGame(laneTeams = ["red", "yellow"]) {
  return simMl.createMLGame(2, {
    laneTeams,
    startGold: 200,
    startIncome: 0,
    teamHpStart: 20,
  });
}

function createAttacker(overrides = {}) {
  const def = simMl.resolveUnitDef("raider");
  const spawnSourceType = overrides.spawnSourceType ?? "barracks_roster";
  const isDungeonWave = spawnSourceType === "dungeon_wave" || spawnSourceType === "scheduled_wave";
  const canonicalSourceTeam = overrides.sourceTeam === "gold" ? "yellow" : (overrides.sourceTeam ?? null);
  const ownerLaneIndex = overrides.ownerLaneIndex ?? overrides.ownerLane ?? (isDungeonWave ? -1 : (overrides.sourceLaneIndex ?? -1));
  return {
    id: overrides.id || "route_attacker",
    unitId: overrides.id || "route_attacker",
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    targetLaneIndex: overrides.targetLaneIndex,
    laneId: overrides.laneId,
    sourceLaneIndex: overrides.sourceLaneIndex ?? -1,
    sourceTeam: canonicalSourceTeam,
    sourceBarracksId: overrides.sourceBarracksId ?? null,
    sourceBarracksKey: overrides.sourceBarracksKey ?? overrides.sourceBarracksId ?? null,
    spawnSourceType,
    type: "raider",
    unitTypeKey: "raider",
    allegianceKey: overrides.allegianceKey ?? (isDungeonWave ? "dungeon" : canonicalSourceTeam),
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 0,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? def.hp,
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    atkCd: overrides.atkCd ?? 5,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 1,
    isWaveUnit: overrides.isWaveUnit ?? isDungeonWave,
    isDefender: false,
    stance: overrides.stance ?? (isDungeonWave ? "ATTACK" : "ATTACK"),
    combatTarget: null,
    combatState: "IDLE",
    routeState: overrides.routeState ?? "MOVING",
    routeSegments: overrides.routeSegments ?? null,
    routeSegmentIndex: overrides.routeSegmentIndex ?? 0,
    segmentProgress: overrides.segmentProgress ?? 0,
    posX: overrides.posX ?? 0,
    posY: overrides.posY ?? 0,
  };
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

function getTownCoreTargetPosition(lane) {
  const target = simMl.getLaneTownCoreCombatTarget(lane);
  assert.ok(target, `expected lane ${lane && lane.laneIndex} to expose a Town Core combat target`);
  return target;
}

test("barracks target selection differentiates outer loop and center cross routes", () => {
  const game = createGame();

  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "left"), 2);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "right"), 2);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "center"), 0);
});

test("two-player seating uses red and yellow as the only live bases", () => {
  const config = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });

  assert.deepEqual(
    config.slotDefinitions.map((slot) => slot.slotColor),
    ["red", "yellow"]
  );
  assert.deepEqual(
    config.slotDefinitions.map((slot) => slot.branchId),
    ["left_branch_a", "left_branch_b"]
  );
  assert.equal(config.slotDefinitions[0].side, "left");
  assert.equal(config.slotDefinitions[1].side, "right");
  assert.equal(config.slotDefinitions[0].castleSide, "right");
  assert.equal(config.slotDefinitions[1].castleSide, "left");
  assert.deepEqual(
    config.battlefieldTopology.buildZones.map((zone) => zone.branchId),
    ["left_branch_a", "left_branch_b"]
  );
});

test("legacy gold lane input normalizes to canonical yellow allegiance", () => {
  const game = createTwoPlayerGame(["red", "gold"]);
  const goldLane = game.lanes[1];
  const unit = createAttacker({
    id: "gold_lane_barracks_unit",
    ownerLane: goldLane.laneIndex,
    targetLaneIndex: 0,
    laneId: 0,
    sourceLaneIndex: goldLane.laneIndex,
    sourceTeam: "gold",
    sourceBarracksId: "center",
  });

  assert.equal(simMl.initializeMovingUnitRouteState(game, game.lanes[0], unit, { x: 5, y: 0 }).ok, true);
  game.lanes[0].units.push(unit);

  const snapshot = simMl.createMLSnapshot(game);
  assert.equal(snapshot.lanes[1].team, "yellow");
  assert.equal(snapshot.lanes[1].allegianceKey, "yellow");

  const snapUnit = snapshot.lanes[0].units.find((entry) => entry && entry.id === unit.id);
  assert.ok(snapUnit);
  assert.equal(snapUnit.allegianceKey, "yellow");
  assert.equal(snapUnit.sourceTeam, "yellow");
  assert.equal(snapUnit.ownerLaneIndex, 1);
});

test("barracks sends hold on the home gate when no enemy lane remains", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  game.lanes[1].eliminated = true;

  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 0);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "left"), 0);
  assert.deepEqual(
    simMl.buildRouteSegments(simMl.ROUTE_TYPES.CENTER_CROSS, "ACTR", "A"),
    ["ACTR_A"]
  );
  assert.deepEqual(
    simMl.buildRouteSegments(simMl.ROUTE_TYPES.OUTER_LOOP, "ALFT", "A"),
    ["ALFT_A"]
  );
});

test("same-lane hold formations stack forward toward the gate instead of backward into the base", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  game.lanes[1].eliminated = true;

  const firstRow = createAttacker({
    id: "hold_row_0",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
    targetLaneIndex: 0,
    laneId: 0,
  });
  const secondRow = createAttacker({
    id: "hold_row_1",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
    targetLaneIndex: 0,
    laneId: 0,
  });

  assert.equal(simMl.initializeMovingUnitRouteState(game, game.lanes[0], firstRow, { x: 5, y: 0 }).ok, true);
  assert.equal(simMl.initializeMovingUnitRouteState(game, game.lanes[0], secondRow, { x: 5, y: 1 }).ok, true);

  assert.deepEqual(firstRow.routeSegments, ["ACTR_A"]);
  assert.deepEqual(secondRow.routeSegments, ["ACTR_A"]);
  assert.equal(firstRow.stance, "HOLD");
  assert.equal(secondRow.stance, "HOLD");
  assert.ok(secondRow.routeLongitudinalOffset > firstRow.routeLongitudinalOffset);
  assert.ok(secondRow.posY > firstRow.posY, "the second hold row should stage farther out toward the gate.");
});

test("route initialization assigns deterministic segment state", () => {
  const game = createGame();
  const targetLane = game.lanes[2];

  const outerUnit = createAttacker({
    id: "outer_unit",
    sourceLaneIndex: 0,
    sourceBarracksId: "left",
  });
  simMl.initializeMovingUnitRouteState(game, targetLane, outerUnit, { x: 5, y: 0 });
  assert.equal(outerUnit.routeType, simMl.ROUTE_TYPES.OUTER_LOOP);
  assert.equal(outerUnit.pathContractType, "barracks_loop");
  assert.equal(outerUnit.stance, "ATTACK");
  assert.equal(outerUnit.routeStartNode, "ALFT");
  assert.equal(outerUnit.routeTargetNode, "C");
  assert.equal(outerUnit.currentSegment, "ALFT_A");

  const centerUnit = createAttacker({
    id: "center_unit",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
  });
  simMl.initializeMovingUnitRouteState(game, game.lanes[1], centerUnit, { x: 5, y: 0 });
  assert.equal(centerUnit.routeType, simMl.ROUTE_TYPES.CENTER_CROSS);
  assert.equal(centerUnit.pathContractType, "barracks_cross");
  assert.equal(centerUnit.stance, "ATTACK");
  assert.equal(centerUnit.routeStartNode, "ACTR");
  assert.equal(centerUnit.routeTargetNode, "B");
  assert.equal(centerUnit.currentSegment, "ACTR_A");
  assert.deepEqual(centerUnit.routeSegments, ["ACTR_A", "A_M", "M_B"]);
});

test("scheduled wave route initialization uses the shared mine-center origin", () => {
  const game = createGame();
  const targetLane = game.lanes[0];
  const scheduledUnit = createAttacker({
    id: "scheduled_wave_unit",
    ownerLane: -1,
    targetLaneIndex: 0,
    laneId: 0,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    atkCd: 0,
  });

  const result = simMl.initializeMovingUnitRouteState(game, targetLane, scheduledUnit, { x: 5, y: 0 });
  assert.equal(result.ok, true);
  assert.equal(scheduledUnit.spawnSourceType, "dungeon_wave");
  assert.equal(scheduledUnit.allegianceKey, "dungeon");
  assert.equal(scheduledUnit.pathContractType, "wave_lane");
  assert.equal(scheduledUnit.stance, "ATTACK");
  assert.equal(scheduledUnit.routeType, simMl.ROUTE_TYPES.WAVE_LANE);
  assert.equal(scheduledUnit.routeStartNode, "WA");
  assert.equal(scheduledUnit.routeTargetNode, "A");
  assert.equal(scheduledUnit.currentSegment, "WA_A");
  assert.equal(scheduledUnit.routeWorldX, 0);
  assert.equal(scheduledUnit.routeWorldY, 0);
  assert.equal(scheduledUnit.posX, 0);
  assert.equal(scheduledUnit.posY, 0);
});

test("snapshot contract preserves explicit barracks route assignment fields", () => {
  const game = createGame();
  const targetLane = game.lanes[1];
  const unit = createAttacker({
    id: "center_contract_unit",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
  });

  unit.sourceBarracksKey = "center";
  simMl.initializeMovingUnitRouteState(game, targetLane, unit, { x: 7, y: 2 });
  targetLane.units.push(unit);

  const snapshot = simMl.createMLSnapshot(game);
  const snapLane = snapshot.lanes.find((lane) => lane && lane.laneIndex === 1);
  const snapUnit = snapLane.units.find((entry) => entry && entry.id === "center_contract_unit");

  assert.equal(snapUnit.sourceBarracksKey, "center");
  assert.equal(snapUnit.sourceBarracksId, "center");
  assert.equal(snapUnit.barracksId, "center");
  assert.equal(snapUnit.unitId, "center_contract_unit");
  assert.equal(snapUnit.unitTypeKey, "raider");
  assert.equal(snapUnit.allegianceKey, "red");
  assert.equal(snapUnit.ownerLaneIndex, 0);
  assert.equal(snapUnit.targetLaneIndex, 1);
  assert.equal(snapUnit.spawnSourceType, "barracks_roster");
  assert.equal(snapUnit.pathContractType, "barracks_cross");
  assert.equal(snapUnit.stance, "ATTACK");
  assert.equal(snapUnit.laneId, 1);
  assert.equal(snapUnit.routeType, simMl.ROUTE_TYPES.CENTER_CROSS);
  assert.equal(snapUnit.routeStartNode, "ACTR");
  assert.equal(snapUnit.routeTargetNode, "B");
  assert.equal(snapUnit.pathId, "ACTR_A>A_M>M_B");
  assert.equal(snapUnit.currentWaypointIndex, 0);
  assert.equal(snapUnit.nextWaypoint, "A");
  assert.equal(snapUnit.movementState, "MOVING");
  assert.equal(snapUnit.currentSegment, "ACTR_A");
  assert.equal(typeof snapUnit.routeWorldX, "number");
  assert.equal(typeof snapUnit.routeWorldY, "number");
});

test("blocking structures are targeted before the town core", () => {
  const game = createGame();
  const lane = game.lanes[0];

  const buildResult = simMl.applyMLAction(game, 0, {
    type: "build_barracks_site",
    data: { barracksId: "center" },
  });
  assert.equal(buildResult.ok, true);

  const attacker = createAttacker({
    id: "block_test",
    sourceLaneIndex: 1,
    sourceTeam: "yellow",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    posX: -24,
    posY: 18.5,
    atkCd: 10,
  });

  lane.units.push(attacker);
  tick(game, 1);

  assert.equal(attacker.blockedByStructure, true);
  assert.equal(attacker.blockedByStructureId, "barracks_site:center");
  assert.equal(attacker.combatTarget?.padId, "barracks_site:center");
});

test("barracks attackers clear built non-core fortress structures before the town core", () => {
  const game = createGame();
  const lane = game.lanes[0];

  const buildResult = simMl.applyMLAction(game, 0, {
    type: "build_on_pad",
    data: { padId: "blacksmith_pad" },
  });
  assert.equal(buildResult.ok, true);

  const attacker = createAttacker({
    id: "non_core_priority_test",
    sourceLaneIndex: 1,
    sourceTeam: "yellow",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    posX: -25,
    posY: 21,
    atkCd: 10,
  });

  lane.units.push(attacker);
  tick(game, 1);

  assert.equal(attacker.blockedByStructure, true);
  assert.notEqual(attacker.combatTarget?.padId, "town_core_pad");
  assert.notEqual(attacker.blockedByStructureType, "town_core");
  assert.ok(
    attacker.combatTarget?.padId === "blacksmith_pad" || attacker.combatTarget?.padId === "barracks_pad",
    `expected a built non-core structure target, got ${attacker.combatTarget?.padId ?? "none"}`
  );
});

test("route units engage hostile movers on shared center routes instead of passing through", () => {
  const game = createGame();
  const attackerLane = game.lanes[1];
  const hostileLane = game.lanes[2];

  const attacker = createAttacker({
    id: "red_center_route_unit",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    routeSegments: ["ACTR_A", "A_M", "M_B"],
    routeSegmentIndex: 1,
    segmentProgress: 0.95,
    posX: 0,
    posY: -0.5,
    atkCd: 0,
    baseDmg: 12,
    baseSpeed: 1.5,
  });
  const hostileWave = createAttacker({
    id: "gold_wave_route_unit",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: hostileLane.laneIndex,
    laneId: hostileLane.laneIndex,
    routeSegments: ["WC_C"],
    routeSegmentIndex: 0,
    segmentProgress: 0.9,
    posX: 0,
    posY: 0.5,
    hp: 6,
    maxHp: 6,
    atkCd: 999,
    baseSpeed: 0,
  });

  attackerLane.units.push(attacker);
  hostileLane.units.push(hostileWave);

  tick(game, 1);

  assert.equal(hostileLane.units.find((unit) => unit.id === hostileWave.id), undefined);
  assert.equal(attacker.attackPulse, 1);
  assert.equal(attacker.combatState, "COMBAT");
});

test("red center barracks units intercept center-spawn waves before they reach the red Town Core", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const redLane = game.lanes[0];
  const yellowLane = game.lanes[1];

  const redAttacker = createAttacker({
    id: "red_center_interceptor",
    ownerLane: redLane.laneIndex,
    targetLaneIndex: yellowLane.laneIndex,
    laneId: yellowLane.laneIndex,
    sourceLaneIndex: redLane.laneIndex,
    sourceTeam: "red",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    atkCd: 0,
    baseDmg: 12,
    baseSpeed: 1.5,
  });
  const incomingWave = createAttacker({
    id: "center_spawn_wave",
    ownerLane: -1,
    targetLaneIndex: redLane.laneIndex,
    laneId: redLane.laneIndex,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    hp: 6,
    maxHp: 6,
    atkCd: 999,
    baseDmg: 3,
    baseSpeed: 1.5,
  });

  assert.equal(simMl.initializeMovingUnitRouteState(game, yellowLane, redAttacker, { x: 5, y: 0 }).ok, true);
  assert.equal(simMl.initializeMovingUnitRouteState(game, redLane, incomingWave, { x: 5, y: 0 }).ok, true);

  yellowLane.units.push(redAttacker);
  redLane.units.push(incomingWave);

  tick(game, 12);

  const serverWave = redLane.units.find((unit) => unit.id === incomingWave.id);
  if (serverWave) {
    assert.equal(serverWave.allegianceKey, "dungeon");
    assert.equal(serverWave.pathContractType, "wave_lane");
    assert.ok(
      serverWave.combatState === "COMBAT" || serverWave.hp <= 0,
      "the intercepted wave should still be fighting or already be dead from the interceptor's hit."
    );
    assert.ok(
      serverWave.combatTarget?.unitId === redAttacker.id || serverWave.hp <= 0,
      "the wave should either still be targeting the interceptor or already be dead."
    );
    assert.ok(
      redAttacker.combatTarget?.unitId === incomingWave.id || serverWave.hp <= 0,
      "the interceptor should keep the wave targeted until the kill lands."
    );
  } else {
    assert.ok(redAttacker.attackPulse > 0, "the interceptor should have landed a real hit before the wave could reach the Town Core.");
  }
  assert.ok(
    redAttacker.combatState === "COMBAT" || redAttacker.attackPulse > 0,
    "the interceptor should still be engaged or have just finished the kill."
  );
  assert.equal(game.teamHp.left, 20, "the red Town Core should stay untouched while the interception is happening.");
  assert.equal(game.lanes[0].eliminated, false);
});

test("same-lane hold units do not aggro their own defenders while staging at the gate", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  const def = simMl.resolveUnitDef("raider");
  const stagingUnit = createAttacker({
    id: "staging_center_unit",
    sourceLaneIndex: 0,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    atkCd: 0,
    posX: 0.2,
    posY: 0.2,
  });
  const defender = {
    id: "friendly_gate_defender",
    type: "raider",
    hp: def.hp,
    maxHp: def.hp,
    baseDmg: def.dmg,
    atkCd: 0,
    atkCdTicks: def.atkCdTicks,
    isDefender: true,
    isWaveUnit: false,
    moveSpeed: 0,
    combatTarget: null,
    posX: 0,
    posY: 0,
    pathIdx: 0,
    homeTx: 0,
    homeTy: 0,
    guardAnchorX: 0,
    guardAnchorY: 0,
  };

  simMl.initializeMovingUnitRouteState(game, lane, stagingUnit, { x: 0.2, y: 0.2 });
  lane.units.push(defender);
  lane.units.push(stagingUnit);

  tick(game, 1);

  assert.deepEqual(stagingUnit.routeSegments, ["ACTR_A"]);
  assert.equal(stagingUnit.stance, "HOLD");
  assert.equal(stagingUnit.pathContractType, "barracks_cross");
  assert.equal(defender.combatTarget, null);
  assert.equal(stagingUnit.combatTarget, null);
  assert.equal(defender.hp, def.hp);
  assert.equal(stagingUnit.hp, def.hp);
});

test("active barracks attackers retreat to the surviving home gate when their target fortress falls", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const defeatedLane = game.lanes[1];
  const coreTarget = getTownCoreTargetPosition(defeatedLane);
  for (const pad of defeatedLane.fortressPads || []) {
    if (!pad || pad.buildingType === "town_core")
      continue;
    pad.tier = 0;
    pad.hp = 0;
    pad.maxHp = 0;
  }
  for (const siteState of Object.values(defeatedLane.barracksSiteStates || {})) {
    if (!siteState)
      continue;
    siteState.isBuilt = false;
    siteState.hp = 0;
    siteState.maxHp = 0;
  }
  const attacker = createAttacker({
    id: "retreating_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksKey: "center",
    targetLaneIndex: defeatedLane.laneIndex,
    laneId: defeatedLane.laneIndex,
    baseDmg: 20,
    atkCd: 0,
    spawnIndex: 0,
    posX: coreTarget.posX,
    posY: coreTarget.posY - 2.8,
    pathIdx: 0.95,
    routeSegments: ["A_M", "M_B"],
    routeSegmentIndex: 1,
    segmentProgress: 0.9,
  });

  defeatedLane.units.push(attacker);

  let ticksSpent = 0;
  while (!defeatedLane.eliminated && ticksSpent < 20) {
    tick(game, 1);
    ticksSpent += 1;
  }

  assert.equal(defeatedLane.eliminated, true, "fatal Town Core damage should eliminate the target lane");
  assert.equal(defeatedLane.units.length, 0, "the defeated lane should clear out after elimination");
  assert.ok(sourceLane.units.includes(attacker), "the attacker should be rescued onto the surviving home lane");
  assert.equal(attacker.targetLaneIndex, sourceLane.laneIndex);
  assert.equal(attacker.laneId, sourceLane.laneIndex);
  assert.equal(attacker.stance, "HOLD");
  assert.equal(attacker.pathContractType, "barracks_cross");
  assert.equal(attacker.routeStartNode, "B");
  assert.equal(attacker.routeTargetNode, "A");
  assert.deepEqual(attacker.routeSegments, ["B_M", "M_A"]);
  assert.equal(attacker.combatTarget, null);

  const startX = attacker.posX;
  tick(game, 1);
  assert.ok(attacker.posX < startX, "the rescued unit should travel back toward the surviving home gate instead of despawning");
});
