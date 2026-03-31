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
  const targetLaneIndex = overrides.targetLaneIndex ?? overrides.laneId ?? ownerLaneIndex;
  const defaultGroupId = !isDungeonWave && ownerLaneIndex >= 0
    ? `test_packet:${ownerLaneIndex}:${overrides.sourceBarracksId ?? "center"}:${targetLaneIndex}`
    : null;
  return {
    id: overrides.id || "route_attacker",
    unitId: overrides.id || "route_attacker",
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    targetLaneIndex,
    laneId: overrides.laneId ?? targetLaneIndex,
    sourceLaneIndex: overrides.sourceLaneIndex ?? -1,
    sourceTeam: canonicalSourceTeam,
    sourceBarracksId: overrides.sourceBarracksId ?? null,
    sourceBarracksKey: overrides.sourceBarracksKey ?? overrides.sourceBarracksId ?? null,
    spawnSourceType,
    groupId: overrides.groupId ?? defaultGroupId,
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

function issueLaneCommand(game, laneIndex, type, data = {}) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, `expected ${type} on lane ${laneIndex} to succeed`);
  return result;
}

function getTownCoreTargetPosition(lane) {
  const target = simMl.getLaneTownCoreCombatTarget(lane);
  assert.ok(target, `expected lane ${lane && lane.laneIndex} to expose a Town Core combat target`);
  return target;
}

function primeDefendAnchor(game, lane) {
  tick(game, 1);
  assert.ok(lane.outsideGateAnchor, `expected lane ${lane && lane.laneIndex} to expose an outside gate anchor`);
  return lane.outsideGateAnchor;
}

function getDefendAnchorPosition(lane, forwardOffset = 0, lateralOffset = 0) {
  const anchor = lane && lane.outsideGateAnchor
    ? lane.outsideGateAnchor
    : lane && lane.formationAnchor;
  assert.ok(anchor, `expected lane ${lane && lane.laneIndex} to have a defend anchor`);

  const facing = lane && lane.formationFacing
    ? lane.formationFacing
    : { x: 0, y: -1 };
  const lateral = { x: -Number(facing.y) || 0, y: Number(facing.x) || 0 };

  return {
    posX: Number(anchor.x) + (Number(facing.x) * forwardOffset) + (lateral.x * lateralOffset),
    posY: Number(anchor.y) + (Number(facing.y) * forwardOffset) + (lateral.y * lateralOffset),
  };
}

test("lane command state controls barracks destinations regardless of barracks site", () => {
  const game = createGame();

  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "left"), 0);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "right"), 0);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "center"), 0);

  issueLaneCommand(game, 1, "set_lane_retreat", { progress: 0 });
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "left"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "right"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "center"), 1);
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

test("match config exposes a stable authoritative battlefield layout payload", () => {
  const firstConfig = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });
  const secondConfig = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });

  assert.ok(firstConfig.battlefieldLayout, "expected battlefieldLayout in ml_match_config");
  assert.equal(firstConfig.battlefieldLayout.layoutId, "lava_lake_funnel:server_v1");
  assert.equal(firstConfig.battlefieldLayout.playerCount, 2);
  assert.equal(firstConfig.battlefieldLayout.contentHash, secondConfig.battlefieldLayout.contentHash);
  assert.equal(firstConfig.battlefieldLayout.lanes.length, 2);

  const redLane = firstConfig.battlefieldLayout.lanes.find((lane) => lane && lane.laneKey === "red");
  assert.ok(redLane, "expected red lane layout");
  assert.equal(redLane.fortressPads.length, firstConfig.fortressPadConfigs.length);
  assert.equal(redLane.barracksSites.length, firstConfig.barracksSiteConfigs.length);
  assert.ok(Number.isFinite(Number(redLane.townCore?.x)));
  assert.ok(Number.isFinite(Number(redLane.townCore?.y)));
  assert.ok(Number.isFinite(Number(redLane.frontGate?.x)));
  assert.ok(Number.isFinite(Number(redLane.frontGate?.y)));

  const waveSegment = firstConfig.battlefieldLayout.routeSegments.find((segment) => segment && segment.segmentId === "WA_A");
  assert.ok(waveSegment, "expected lane wave route segment in battlefieldLayout");
  assert.equal(waveSegment.fromNodeId, "WA");
  assert.equal(waveSegment.toNodeId, "A");
  assert.ok(Array.isArray(waveSegment.points) && waveSegment.points.length >= 2);

  const redTownCoreNode = firstConfig.battlefieldLayout.routeNodes.find((node) => node && node.nodeId === "A");
  assert.ok(redTownCoreNode, "expected lane core node in battlefieldLayout");
  assert.equal(redTownCoreNode.laneKey, "red");
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

test("attack lanes keep their forward objective after the target lane falls until the player changes orders", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  game.lanes[1].eliminated = true;

  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "left"), 1);
});

test("defend mode stages same-lane formations into spaced slots around the home anchor", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  issueLaneCommand(game, 0, "set_lane_defend_point", { progress: 0 });

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

  assert.deepEqual(firstRow.routeSegments, ["ACTR_A", "A_M", "M_B"]);
  assert.deepEqual(secondRow.routeSegments, ["ACTR_A", "A_M", "M_B"]);
  assert.equal(firstRow.stance, "DEFEND");
  assert.equal(secondRow.stance, "DEFEND");
  assert.equal(firstRow.pathContractType, "guard_anchor");
  assert.equal(secondRow.pathContractType, "guard_anchor");
  assert.ok(secondRow.routeLongitudinalOffset < firstRow.routeLongitudinalOffset);
  assert.ok(secondRow.posY < firstRow.posY, "the second hold row should stage deeper in the home-side hold formation.");
});

test("route initialization uses the modal lane-command route contract for every barracks site", () => {
  const game = createGame();
  const targetLane = game.lanes[2];

  const outerUnit = createAttacker({
    id: "outer_unit",
    sourceLaneIndex: 0,
    sourceBarracksId: "left",
  });
  simMl.initializeMovingUnitRouteState(game, targetLane, outerUnit, { x: 5, y: 0 });
  assert.equal(outerUnit.routeType, simMl.ROUTE_TYPES.CENTER_CROSS);
  assert.equal(outerUnit.pathContractType, "barracks_cross");
  assert.equal(outerUnit.stance, "ATTACK");
  assert.equal(outerUnit.routeStartNode, "ALFT");
  assert.equal(outerUnit.routeTargetNode, "B");
  assert.equal(outerUnit.currentSegment, "ALFT_A");
  assert.deepEqual(outerUnit.routeSegments, ["ALFT_A", "A_M", "M_B"]);

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

  assert.equal(snapLane.commandState, "ATTACK");
  assert.equal(snapLane.commandTargetLaneIndex, 0);
  assert.equal(snapLane.combatEnabled, true);
  assert.ok(Array.isArray(snapLane.formationSlots));
  assert.equal(snapUnit.sourceBarracksKey, "center");
  assert.equal(snapUnit.sourceBarracksId, "center");
  assert.equal(snapUnit.barracksId, "center");
  assert.equal(snapUnit.unitId, "center_contract_unit");
  assert.equal(snapUnit.unitTypeKey, "raider");
  assert.equal(snapUnit.allegianceKey, "red");
  assert.equal(snapUnit.ownerLaneIndex, 0);
  assert.equal(snapUnit.targetLaneIndex, 1);
  assert.equal(snapUnit.objectiveLaneIndex, 1);
  assert.equal(snapUnit.spawnSourceType, "barracks_roster");
  assert.equal(snapUnit.pathContractType, "barracks_cross");
  assert.equal(snapUnit.stance, "ATTACK");
  assert.equal(snapUnit.movementMode, "LaneTravel");
  assert.equal(snapUnit.canEngage, true);
  assert.equal(snapUnit.laneId, 1);
  assert.equal(snapUnit.routeType, simMl.ROUTE_TYPES.CENTER_CROSS);
  assert.equal(snapUnit.routeStartNode, "ACTR");
  assert.equal(snapUnit.routeTargetNode, "B");
  assert.equal(snapUnit.pathId, "ACTR_A>A_M>M_B");
  assert.equal(snapUnit.currentWaypointIndex, 0);
  assert.equal(snapUnit.nextWaypoint, "A");
  assert.equal(snapUnit.movementState, "MOVING");
  assert.equal(snapUnit.presentationPhase, "LaneTravel");
  assert.equal(snapUnit.presentationIntent, "Move");
  assert.equal(snapUnit.currentSegment, "ACTR_A");
  assert.equal(snapUnit.combatTargetKind, null);
  assert.equal(snapUnit.combatContact, false);
  assert.equal(snapUnit.regroupTicksRemaining, 0);
  assert.equal(snapUnit.combatLockTicksRemaining, 0);
  assert.equal(typeof snapUnit.routeWorldX, "number");
  assert.equal(typeof snapUnit.routeWorldY, "number");
});

test("blocking structures are targeted before the town core", () => {
  const game = createGame();
  const lane = game.lanes[0];
  issueLaneCommand(game, 1, "set_lane_attack", { targetLaneIndex: lane.laneIndex });

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
  issueLaneCommand(game, 1, "set_lane_attack", { targetLaneIndex: lane.laneIndex });

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
  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: attackerLane.laneIndex });

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

test("attack-mode lane formations share the first hostile contact instead of letting trailing units walk past", () => {
  const game = createGame();
  const attackerLane = game.lanes[1];
  const hostileLane = game.lanes[2];
  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: attackerLane.laneIndex });
  const attackers = [
    createAttacker({
      id: "formation_attack_front",
      sourceLaneIndex: 0,
      sourceTeam: "red",
      sourceBarracksId: "center",
      targetLaneIndex: attackerLane.laneIndex,
      laneId: attackerLane.laneIndex,
      routeSegments: ["ACTR_A", "A_M", "M_B"],
      routeSegmentIndex: 1,
      segmentProgress: 0.84,
      posX: -0.2,
      posY: -1.2,
      atkCd: 0,
      baseDmg: 4,
      baseSpeed: 1.2,
    }),
    createAttacker({
      id: "formation_attack_mid",
      sourceLaneIndex: 0,
      sourceTeam: "red",
      sourceBarracksId: "center",
      targetLaneIndex: attackerLane.laneIndex,
      laneId: attackerLane.laneIndex,
      routeSegments: ["ACTR_A", "A_M", "M_B"],
      routeSegmentIndex: 1,
      segmentProgress: 0.79,
      posX: 0,
      posY: -1.8,
      atkCd: 0,
      baseDmg: 4,
      baseSpeed: 1.2,
    }),
    createAttacker({
      id: "formation_attack_rear",
      sourceLaneIndex: 0,
      sourceTeam: "red",
      sourceBarracksId: "center",
      targetLaneIndex: attackerLane.laneIndex,
      laneId: attackerLane.laneIndex,
      routeSegments: ["ACTR_A", "A_M", "M_B"],
      routeSegmentIndex: 1,
      segmentProgress: 0.74,
      posX: 0.2,
      posY: -2.4,
      atkCd: 0,
      baseDmg: 4,
      baseSpeed: 1.2,
    }),
  ];
  const hostileWave = createAttacker({
    id: "formation_attack_wave",
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
    posY: 0.55,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
  });

  attackerLane.units.push(...attackers);
  hostileLane.units.push(hostileWave);

  tick(game, 2);

  const liveAttackers = game.lanes
    .flatMap((lane) => lane.units)
    .filter((unit) => unit && unit.id.startsWith("formation_attack_") && unit.id !== hostileWave.id);

  assert.equal(liveAttackers.length, 3);
  assert.ok(
    liveAttackers.every((unit) => unit.combatTarget?.unitId === hostileWave.id),
    "once the lead unit finds contact, the rest of the attack formation should also stop and join that fight."
  );
  assert.ok(
    liveAttackers.every((unit) => unit.pathContractType === "intercept"),
    "formation units that join the shared contact should publish intercept routing instead of continuing past the fight."
  );
});

test("lane-controlled attackers keep a short lock on their current contact instead of swapping to a slightly closer newcomer", () => {
  const game = createGame();
  const attackerLane = game.lanes[1];
  const hostileLane = game.lanes[2];
  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: attackerLane.laneIndex });
  const attacker = createAttacker({
    id: "sticky_target_attacker",
    sourceLaneIndex: 0,
    sourceTeam: "red",
    sourceBarracksId: "center",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    routeSegments: ["ACTR_A", "A_M", "M_B"],
    routeSegmentIndex: 1,
    segmentProgress: 0.84,
    posX: -0.2,
    posY: -1.2,
    atkCd: 999,
    baseSpeed: 1.2,
  });
  const firstWave = createAttacker({
    id: "sticky_target_wave_1",
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
    posY: 0.55,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
  });

  attackerLane.units.push(attacker);
  hostileLane.units.push(firstWave);

  tick(game, 1);

  assert.equal(attacker.combatTarget?.unitId, firstWave.id, "the attacker should lock onto the first hostile contact.");

  const secondWave = createAttacker({
    id: "sticky_target_wave_2",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: hostileLane.laneIndex,
    laneId: hostileLane.laneIndex,
    routeSegments: ["WC_C"],
    routeSegmentIndex: 0,
    segmentProgress: 0.85,
    posX: 0.1,
    posY: -0.2,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
  });

  hostileLane.units.push(secondWave);

  tick(game, 1);

  assert.equal(
    attacker.combatTarget?.unitId,
    firstWave.id,
    "a lane-controlled attacker should stay committed to its current combat target during the short lock window."
  );
});

test("lane-controlled defenders pause to regroup briefly after a kill before reacquiring the next threat", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.6, 0);
  const firstWavePoint = getDefendAnchorPosition(lane, 0.1, 0);
  const secondWavePoint = getDefendAnchorPosition(lane, 0.2, 1.2);
  const defender = createAttacker({
    id: "regroup_defender",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 80,
    maxHp: 80,
    atkCd: 0,
    baseDmg: 10,
    baseSpeed: 1.0,
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
  });
  const firstWave = createAttacker({
    id: "regroup_wave_1",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 6,
    maxHp: 6,
    atkCd: 999,
    baseSpeed: 0,
    posX: firstWavePoint.posX,
    posY: firstWavePoint.posY,
  });
  const secondWave = createAttacker({
    id: "regroup_wave_2",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 30,
    maxHp: 30,
    atkCd: 999,
    baseSpeed: 0,
    posX: secondWavePoint.posX,
    posY: secondWavePoint.posY,
  });

  lane.units.push(defender, firstWave, secondWave);

  tick(game, 1);

  assert.equal(
    lane.units.some((unit) => unit.id === firstWave.id),
    false,
    "the defender should finish the first weak contact on the opening attack."
  );

  tick(game, 1);

  const serverDefender = lane.units.find((unit) => unit.id === defender.id);
  assert.ok(serverDefender, "the regrouping defender should still be alive.");
  assert.equal(
    serverDefender.combatTarget,
    null,
    "after a kill, a lane-controlled defender should briefly regroup instead of immediately snapping to the next nearby target."
  );
  assert.ok(
    Number(serverDefender.regroupUntilTick) > game.tick,
    "the regroup window should stay active for a short time after combat ends."
  );

  tick(game, 10);

  const laterDefender = lane.units.find((unit) => unit.id === defender.id);
  const laterWave = lane.units.find((unit) => unit.id === secondWave.id);
  assert.ok(laterDefender, "the defender should still exist after the regroup window.");
  if (laterWave) {
    assert.ok(
      laterDefender.combatTarget?.unitId === laterWave.id || laterDefender.attackPulse > 0,
      "once the regroup window ends, the defender should be able to reacquire the remaining threat."
    );
  }
});

test("redirecting an attack lane reprojects existing attackers onto the new route instead of preserving a map-wide offset", () => {
  const game = createGame();
  const sourceLane = game.lanes[0];
  const originalTargetLane = game.lanes[1];
  const redirectedLane = game.lanes[3];
  const originalCore = getTownCoreTargetPosition(originalTargetLane);
  const attacker = createAttacker({
    id: "redirect_projection_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: originalTargetLane.laneIndex,
    objectiveLaneIndex: originalTargetLane.laneIndex,
    laneId: originalTargetLane.laneIndex,
    posX: originalCore.posX - 0.5,
    posY: originalCore.posY - 1.2,
    routeWorldX: originalCore.posX - 0.5,
    routeWorldY: originalCore.posY - 1.2,
    routeSegments: ["ACTR_A", "A_M", "M_B"],
    routeSegmentIndex: 2,
    segmentProgress: 0.92,
    currentSegment: "M_B",
    atkCd: 999,
  });

  originalTargetLane.units.push(attacker);

  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: redirectedLane.laneIndex });
  tick(game, 1);

  assert.ok(redirectedLane.units.includes(attacker), "redirected attackers should transfer into the newly targeted lane container.");
  assert.equal(attacker.routeTargetNode, "D");
  assert.equal(attacker.objectiveLaneIndex, redirectedLane.laneIndex);
  assert.ok(
    Math.abs(Number(attacker.routeLateralOffset) || 0) < 8,
    "redirecting an active attacker should reproject it near the new route instead of preserving a fortress-wide lateral offset."
  );
});

test("switching a defend packet to attack keeps it moving along the live route instead of skipping to the enemy core", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const targetLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, sourceLane);

  const attacker = createAttacker({
    id: "defend_to_attack_transition",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: sourceLane.laneIndex,
    laneId: sourceLane.laneIndex,
    baseSpeed: 2.4,
    atkCd: 999,
  });

  assert.equal(
    simMl.initializeMovingUnitRouteState(game, sourceLane, attacker, { x: 5, y: 0 }).ok,
    true,
    "expected the defend-stage attacker to receive an initial route contract"
  );
  sourceLane.units.push(attacker);

  tick(game, 6);

  const enemyCore = getTownCoreTargetPosition(targetLane);
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });

  let previousPoint = { x: Number(attacker.posX), y: Number(attacker.posY) };
  let sawMidRouteProgress = false;
  let liveUnit = attacker;

  for (let i = 0; i < 14; i += 1) {
    tick(game, 1);

    liveUnit = null;
    for (const lane of game.lanes) {
      const candidate = lane.units.find((unit) => unit && unit.id === attacker.id);
      if (candidate) {
        liveUnit = candidate;
        break;
      }
    }

    assert.ok(liveUnit, "the transitioned attacker should remain alive while traversing the attack route.");

    const stepDistance = Math.hypot(
      Number(liveUnit.posX) - previousPoint.x,
      Number(liveUnit.posY) - previousPoint.y
    );
    assert.ok(
      stepDistance <= Number(liveUnit.baseSpeed) + 0.2,
      `expected incremental travel during command transition, got step=${stepDistance.toFixed(3)} speed=${Number(liveUnit.baseSpeed).toFixed(3)}`
    );

    const coreDistance = Math.hypot(
      Number(liveUnit.posX) - enemyCore.posX,
      Number(liveUnit.posY) - enemyCore.posY
    );
    if (i === 0) {
      assert.ok(
        coreDistance > 10,
        "the first ATTACK tick should keep the unit en route instead of teleporting it onto the enemy Town Core."
      );
    }

    if (Number.isFinite(liveUnit.pathIdx) && liveUnit.pathIdx >= 0.3 && liveUnit.pathIdx <= 0.55)
      sawMidRouteProgress = true;

    previousPoint = { x: Number(liveUnit.posX), y: Number(liveUnit.posY) };
  }

  assert.equal(liveUnit.stance, "ATTACK");
  assert.equal(liveUnit.commandState, "ATTACK");
  assert.ok(
    sawMidRouteProgress,
    "the transitioned attacker should pass through the route midpoint instead of jumping from the defend anchor to the enemy objective."
  );
  assert.ok(
    Math.hypot(Number(liveUnit.posX) - enemyCore.posX, Number(liveUnit.posY) - enemyCore.posY) > 8,
    "after the midpoint sample window the attacker should still be visibly traveling toward the target, not already sitting on the core."
  );
});

test("snapshot contract exposes combat resolve state for lane-controlled contact fights", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.5, 0);
  const wavePoint = getDefendAnchorPosition(lane, 0.15, 0);
  const defender = createAttacker({
    id: "resolve_snapshot_defender",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 80,
    maxHp: 80,
    atkCd: 999,
    baseDmg: 8,
    baseSpeed: 0.2,
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
  });
  const wave = createAttacker({
    id: "resolve_snapshot_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    posX: wavePoint.posX,
    posY: wavePoint.posY,
  });

  lane.units.push(defender, wave);

  tick(game, 1);

  const snapshot = simMl.createMLSnapshot(game);
  const snapLane = snapshot.lanes.find((entry) => entry && entry.laneIndex === lane.laneIndex);
  const snapDefender = snapLane.units.find((entry) => entry && entry.id === defender.id);

  assert.ok(snapDefender, "expected the defender to appear in the snapshot.");
  assert.equal(snapDefender.presentationPhase, "CombatResolve");
  assert.equal(snapDefender.presentationIntent, "Defend");
  assert.equal(snapDefender.combatTargetKind, "unit");
  assert.equal(snapDefender.combatContact, true);
  assert.ok(snapDefender.combatLockTicksRemaining > 0, "the snapshot should carry the short target-lock window while contact is active.");
});

test("snapshot contract exposes combat regroup after a lane-controlled kill", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.6, 0);
  const firstWavePoint = getDefendAnchorPosition(lane, 0.1, 0);
  const secondWavePoint = getDefendAnchorPosition(lane, 0.2, 1.2);
  const defender = createAttacker({
    id: "regroup_snapshot_defender",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 80,
    maxHp: 80,
    atkCd: 0,
    baseDmg: 10,
    baseSpeed: 1.0,
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
  });
  const firstWave = createAttacker({
    id: "regroup_snapshot_wave_1",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 6,
    maxHp: 6,
    atkCd: 999,
    baseSpeed: 0,
    posX: firstWavePoint.posX,
    posY: firstWavePoint.posY,
  });
  const secondWave = createAttacker({
    id: "regroup_snapshot_wave_2",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 30,
    maxHp: 30,
    atkCd: 999,
    baseSpeed: 0,
    posX: secondWavePoint.posX,
    posY: secondWavePoint.posY,
  });

  lane.units.push(defender, firstWave, secondWave);

  tick(game, 2);

  const snapshot = simMl.createMLSnapshot(game);
  const snapLane = snapshot.lanes.find((entry) => entry && entry.laneIndex === lane.laneIndex);
  const snapDefender = snapLane.units.find((entry) => entry && entry.id === defender.id);

  assert.ok(snapDefender, "expected the regrouping defender to appear in the snapshot.");
  assert.equal(snapDefender.presentationPhase, "CombatRegroup");
  assert.equal(snapDefender.combatTargetId, null);
  assert.equal(snapDefender.combatContact, false);
  assert.ok(snapDefender.regroupTicksRemaining > 0, "the regroup timer should remain visible to the client while the unit reforms.");
});

test("lane-controlled formations keep surround movement inside their combat leash", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenders = Array.from({ length: 8 }, (_, index) => createAttacker({
    id: `combat_pocket_defender_${index}`,
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    atkCd: 999,
    baseSpeed: 0.55,
    ...getDefendAnchorPosition(lane, -0.8 - (index * 0.15), ((index % 2) * 0.4)),
  }));

  lane.units.push(...defenders);
  tick(game, 1);

  assert.ok(lane.formationAnchor, "the lane should expose a defend formation anchor before combat starts.");
  const hostileWave = createAttacker({
    id: "combat_pocket_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 300,
    maxHp: 300,
    atkCd: 999,
    baseSpeed: 0,
    ...getDefendAnchorPosition(lane, 2.6, 0),
  });

  lane.units.push(hostileWave);
  tick(game, 16);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("combat_pocket_defender_"));
  assert.equal(liveDefenders.length, defenders.length);
  assert.ok(
    liveDefenders.some((unit) => unit.combatTarget?.unitId === hostileWave.id || unit.movementMode === "CombatEngage" || unit.attackPulse > 0),
    "the formation should actually commit to the hostile wave instead of the test only exercising idle formation movement."
  );
  assert.ok(
    liveDefenders.every((unit) => {
      const dx = Number(unit.posX) - Number(unit.anchorTargetX);
      const dy = Number(unit.posY) - Number(unit.anchorTargetY);
      const leashRadius = Number(unit.combatLeashRadius) || 0;
      return Math.sqrt((dx * dx) + (dy * dy)) <= leashRadius + 0.05;
    }),
    "lane-controlled surround movement should stay inside each unit's combat leash instead of drifting off-lane during combat."
  );
});

test("settled lane formations separate with small lateral nudges instead of being shoved off-slot", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const coreTarget = getTownCoreTargetPosition(lane);
  const formationUnits = Array.from({ length: 6 }, (_, index) => createAttacker({
    id: `soft_separation_unit_${index}`,
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    atkCd: 999,
    baseSpeed: 0.01,
    posX: coreTarget.posX,
    posY: coreTarget.posY - 0.5 - (index * 0.1),
  }));

  lane.units.push(...formationUnits);
  tick(game, 1);

  const settledUnits = lane.units.filter((unit) => unit.id.startsWith("soft_separation_unit_"));
  const facing = lane.formationFacing || { x: 0, y: -1 };
  const lateral = { x: -facing.y, y: facing.x };
  let chosenPair = null;
  let smallestLateralDelta = Infinity;
  for (let i = 0; i < settledUnits.length; i += 1) {
    for (let j = i + 1; j < settledUnits.length; j += 1) {
      const a = settledUnits[i];
      const b = settledUnits[j];
      const anchorDx = Number(b.anchorTargetX) - Number(a.anchorTargetX);
      const anchorDy = Number(b.anchorTargetY) - Number(a.anchorTargetY);
      const lateralDelta = Math.abs((anchorDx * lateral.x) + (anchorDy * lateral.y));
      const depthDelta = Math.abs((anchorDx * facing.x) + (anchorDy * facing.y));
      if (depthDelta <= 0.5 || lateralDelta >= smallestLateralDelta)
        continue;
      smallestLateralDelta = lateralDelta;
      chosenPair = [a, b];
    }
  }

  const [frontUnit, rearUnit] = chosenPair || [];
  assert.ok(frontUnit && rearUnit, "expected to find a front and rear formation pair in the same column.");
  assert.ok(
    smallestLateralDelta < 0.001,
    "the chosen front and rear units should share a formation column so lateral separation is the drift we are testing."
  );

  for (const unit of settledUnits) {
    unit.posX = Number(unit.anchorTargetX);
    unit.posY = Number(unit.anchorTargetY);
    unit.pathIdx = unit.posY;
    unit.routeSegments = null;
  }

  const midpointX = (Number(frontUnit.anchorTargetX) + Number(rearUnit.anchorTargetX)) / 2;
  const midpointY = (Number(frontUnit.anchorTargetY) + Number(rearUnit.anchorTargetY)) / 2;
  frontUnit.posX = midpointX;
  frontUnit.posY = midpointY - 0.01;
  frontUnit.pathIdx = frontUnit.posY;
  rearUnit.posX = midpointX;
  rearUnit.posY = midpointY + 0.01;
  rearUnit.pathIdx = rearUnit.posY;

  tick(game, 1);

  const updatedFront = lane.units.find((unit) => unit.id === frontUnit.id);
  const updatedRear = lane.units.find((unit) => unit.id === rearUnit.id);
  assert.ok(updatedFront && updatedRear, "the overlapped formation units should still exist after separation resolves.");
  const relativeDx = Number(updatedRear.posX) - Number(updatedFront.posX);
  const relativeDy = Number(updatedRear.posY) - Number(updatedFront.posY);
  const lateralSpacing = Math.abs((relativeDx * lateral.x) + (relativeDy * lateral.y));
  const frontLateralOffset = Math.abs(((Number(updatedFront.posX) - Number(updatedFront.anchorTargetX)) * lateral.x)
    + ((Number(updatedFront.posY) - Number(updatedFront.anchorTargetY)) * lateral.y));
  const rearLateralOffset = Math.abs(((Number(updatedRear.posX) - Number(updatedRear.anchorTargetX)) * lateral.x)
    + ((Number(updatedRear.posY) - Number(updatedRear.anchorTargetY)) * lateral.y));
  assert.ok(
    lateralSpacing > 0.05,
    "the spacing pass should still separate settled units enough to avoid overlap."
  );
  assert.ok(
    frontLateralOffset < 0.16,
    "settled front-row units should only get a light sideways nudge instead of being shoved far off their assigned slot."
  );
  assert.ok(
    rearLateralOffset < 0.16,
    "settled rear-row units should also stay close to their assigned slot while the overlap is resolved."
  );
});

test("settled defend formations stay parked on their assigned slots instead of buzzing at idle", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const coreTarget = getTownCoreTargetPosition(lane);
  const formationUnits = Array.from({ length: 6 }, (_, index) => createAttacker({
    id: `idle_settle_unit_${index}`,
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    atkCd: 999,
    baseSpeed: 0.01,
    posX: coreTarget.posX,
    posY: coreTarget.posY - 0.6 - (index * 0.08),
  }));

  lane.units.push(...formationUnits);
  tick(game, 1);

  for (const unit of lane.units.filter((entry) => entry.id.startsWith("idle_settle_unit_"))) {
    unit.posX = Number(unit.anchorTargetX);
    unit.posY = Number(unit.anchorTargetY);
    unit.pathIdx = unit.posY;
    unit.routeSegments = null;
    unit.routeSegmentIndex = 0;
    unit.segmentProgress = 0;
    unit.movementMode = "FormationJoin";
  }

  tick(game, 3);

  const settledUnits = lane.units.filter((unit) => unit.id.startsWith("idle_settle_unit_"));
  assert.equal(settledUnits.length, formationUnits.length);
  for (const unit of settledUnits) {
    const drift = Math.hypot(
      Number(unit.posX) - Number(unit.anchorTargetX),
      Number(unit.posY) - Number(unit.anchorTargetY)
    );
    assert.ok(
      drift <= 0.05,
      `settled defend units should stay parked near their slot instead of buzzing around, got drift=${drift.toFixed(3)} for ${unit.id}`
    );
  }
});

test("center-spawn waves travel through the red front gate approach before they reach the Town Core", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const redLane = game.lanes[0];
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

  assert.equal(simMl.initializeMovingUnitRouteState(game, redLane, incomingWave, { x: 5, y: 0 }).ok, true);
  redLane.units.push(incomingWave);

  tick(game, 18);

  const serverWave = redLane.units.find((unit) => unit.id === incomingWave.id);
  assert.ok(serverWave, "the routed wave should still be alive while approaching the fortress gate.");
  assert.equal(serverWave.allegianceKey, "dungeon");
  assert.equal(serverWave.pathContractType, "wave_lane");
  assert.equal(serverWave.combatTarget, null, "the wave should not reach the Town Core before it reaches the front gate approach.");
  assert.ok(redLane.outsideGateAnchor, "the lane should expose a front-gate defend anchor.");
  assert.ok(
    Math.hypot(
      Number(serverWave.posX) - Number(redLane.outsideGateAnchor.x),
      Number(serverWave.posY) - Number(redLane.outsideGateAnchor.y)
    ) <= 1.5,
    "center-spawn wave routing should now bend through the front-gate approach instead of cutting diagonally into the Town Core."
  );
  assert.equal(game.teamHp.left, 20, "the red Town Core should stay untouched while the interception is happening.");
  assert.equal(game.lanes[0].eliminated, false);
});

test("same-lane hold units do not aggro their own defenders while staging at the gate", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
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
  const defender = createAttacker({
    id: "friendly_gate_defender",
    sourceLaneIndex: 0,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: def.hp,
    maxHp: def.hp,
    baseDmg: def.dmg,
    atkCd: 0,
    atkCdTicks: def.atkCdTicks,
    posX: 0,
    posY: 0,
    pathIdx: 0,
  });

  simMl.initializeMovingUnitRouteState(game, lane, stagingUnit, { x: 0.2, y: 0.2 });
  lane.units.push(defender);
  lane.units.push(stagingUnit);

  tick(game, 1);

  assert.deepEqual(stagingUnit.routeSegments, ["ACTR_A", "A_M", "M_B"]);
  assert.equal(stagingUnit.stance, "DEFEND");
  assert.equal(stagingUnit.pathContractType, "guard_anchor");
  assert.equal(defender.combatTarget, null);
  assert.equal(stagingUnit.combatTarget, null);
  assert.equal(defender.hp, def.hp);
  assert.equal(stagingUnit.hp, def.hp);
});

test("defend-mode formations fan around an intercepted wave at the gate instead of collapsing into one point", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenders = [
    createAttacker({
      id: "defend_ring_left",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -0.8, -0.3),
    }),
    createAttacker({
      id: "defend_ring_center",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -1.4, 0),
    }),
    createAttacker({
      id: "defend_ring_right",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -2.0, 0.3),
    }),
  ];
  const hostileWave = createAttacker({
    id: "defend_ring_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    ...getDefendAnchorPosition(lane, 0.15, 0),
  });

  lane.units.push(...defenders, hostileWave);

  tick(game, 6);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("defend_ring_") && unit.id !== hostileWave.id);

  assert.equal(liveDefenders.length, 3);
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === hostileWave.id),
    "a DEFEND formation should commit together to a nearby interception target."
  );
  const formationFacing = lane.formationFacing || { x: 0, y: -1 };
  const formationLateral = { x: -formationFacing.y, y: formationFacing.x };
  const lateralOffsets = liveDefenders.map((unit) =>
    ((Number(unit.posX) - Number(hostileWave.posX)) * formationLateral.x)
    + ((Number(unit.posY) - Number(hostileWave.posY)) * formationLateral.y));
  assert.ok(
    Math.max(...lateralOffsets) - Math.min(...lateralOffsets) > 0.7,
    "defenders should occupy distinct surround slots instead of collapsing into one narrow stack."
  );
});

test("defend-mode packets from different barracks share the same gate interception target", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);

  const defenders = [
    createAttacker({
      id: "defend_shared_center_front",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -1.1, -0.5),
    }),
    createAttacker({
      id: "defend_shared_center_rear",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -2.0, -0.1),
    }),
    createAttacker({
      id: "defend_shared_left_front",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "left",
      sourceBarracksKey: "left",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -1.1, 0.6),
    }),
    createAttacker({
      id: "defend_shared_left_rear",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "left",
      sourceBarracksKey: "left",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -2.0, 1.0),
    }),
  ];
  const hostileWave = createAttacker({
    id: "defend_shared_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    ...getDefendAnchorPosition(lane, 0.25, 0.1),
  });

  lane.units.push(...defenders, hostileWave);

  tick(game, 6);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("defend_shared_") && unit.id !== hostileWave.id);
  assert.equal(liveDefenders.length, defenders.length);
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === hostileWave.id),
    "nearby defend packets from separate barracks should all commit to the same intercepted wave."
  );
});

test("defend-mode units engage a hostile that slips behind the gate line instead of only checking forward contacts", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);

  const defenders = [
    createAttacker({
      id: "defend_backtrack_front",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -0.9, -0.35),
    }),
    createAttacker({
      id: "defend_backtrack_rear",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -1.5, 0.35),
    }),
  ];
  const hostileWave = createAttacker({
    id: "defend_backtrack_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    ...getDefendAnchorPosition(lane, -3.4, 0.1),
  });

  lane.units.push(...defenders, hostileWave);

  tick(game, 6);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("defend_backtrack_") && unit.id !== hostileWave.id);
  assert.equal(liveDefenders.length, defenders.length);
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === hostileWave.id),
    "defenders should still react to a hostile inside the camp even if it is no longer in the route-forward direction."
  );
});

test("defend-mode units engage a hostile that reaches the home fortress interior even outside the defend anchor bubble", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);

  const townCore = getTownCoreTargetPosition(lane);
  const defenders = [
    createAttacker({
      id: "defend_core_emergency_front",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -0.9, -0.3),
    }),
    createAttacker({
      id: "defend_core_emergency_rear",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "left",
      sourceBarracksKey: "left",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(lane, -1.5, 0.35),
    }),
  ];
  const hostileWave = createAttacker({
    id: "defend_core_emergency_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    posX: townCore.posX + 0.8,
    posY: townCore.posY - 5.0,
  });

  lane.units.push(...defenders, hostileWave);

  tick(game, 6);

  const liveDefenders = lane.units.filter((unit) => unit && unit.id.startsWith("defend_core_emergency_") && unit.id !== hostileWave.id);
  assert.equal(liveDefenders.length, defenders.length);
  assert.ok(
    liveDefenders.every((unit) => unit.combatTarget?.unitId === hostileWave.id),
    "once a hostile reaches the home fortress interior, defenders should treat it as an emergency target even if it is no longer near the outside-gate anchor."
  );
});

test("attackers stay on the destroyed forward objective until the player explicitly recalls them", () => {
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
  assert.ok(defeatedLane.units.includes(attacker), "the attacker should remain on the captured forward objective");
  assert.equal(sourceLane.units.includes(attacker), false, "attackers should not auto-rescue back home after destroying the objective");
  assert.equal(attacker.targetLaneIndex, defeatedLane.laneIndex);
  assert.equal(attacker.laneId, defeatedLane.laneIndex);
  assert.equal(attacker.stance, "ATTACK");
  assert.equal(attacker.combatTarget, null);

  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_retreat", { progress: 0 });
  tick(game, 1);
  assert.ok(sourceLane.units.includes(attacker), "the player recall order should bring the attacker back onto its home lane");
  assert.equal(defeatedLane.units.includes(attacker), false, "the captured lane should release the attacker once the retreat order is issued");
  assert.equal(attacker.targetLaneIndex, sourceLane.laneIndex);
  assert.equal(attacker.laneId, sourceLane.laneIndex);
  assert.equal(attacker.stance, "RETREAT");
  assert.equal(attacker.pathContractType, "retreat_anchor");
  assert.equal(attacker.combatTarget, null);
  assert.ok(
    Math.abs(Number(attacker.routeLateralOffset) || 0) < 8,
    "recalling a forward attacker should keep it close to its live route instead of preserving a map-wide offset from the route start."
  );
});
