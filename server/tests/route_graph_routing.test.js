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
    bounty: overrides.bounty ?? 1,
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

function createMarketWorker(overrides = {}) {
  const sourceLaneIndex = overrides.sourceLaneIndex ?? 0;
  const sourceTeam = overrides.sourceTeam ?? "red";
  return {
    ...createAttacker({
      ...overrides,
      id: overrides.id || "market_worker",
      ownerLaneIndex: overrides.ownerLaneIndex ?? sourceLaneIndex,
      ownerLane: overrides.ownerLane ?? sourceLaneIndex,
      targetLaneIndex: overrides.targetLaneIndex ?? sourceLaneIndex,
      objectiveLaneIndex: overrides.objectiveLaneIndex ?? sourceLaneIndex,
      laneId: overrides.laneId ?? sourceLaneIndex,
      sourceLaneIndex,
      sourceTeam,
      sourceBarracksId: null,
      sourceBarracksKey: null,
      spawnSourceType: "market_roster",
      stance: overrides.stance ?? null,
      isWaveUnit: false,
    }),
    marketUnitKey: overrides.marketUnitKey ?? "peasant",
    isMarketWorker: overrides.isMarketWorker ?? true,
    rosterKey: null,
    role: overrides.role ?? "economy",
    archetypeKey: overrides.archetypeKey ?? "economy_t1",
    canEngage: overrides.canEngage ?? false,
  };
}

function createDefender(overrides = {}) {
  const sourceLaneIndex = overrides.sourceLaneIndex ?? 0;
  const sourceTeam = overrides.sourceTeam ?? "red";
  return {
    ...createAttacker({
      ...overrides,
      id: overrides.id || "route_defender",
      ownerLaneIndex: overrides.ownerLaneIndex ?? sourceLaneIndex,
      ownerLane: overrides.ownerLane ?? sourceLaneIndex,
      targetLaneIndex: overrides.targetLaneIndex ?? sourceLaneIndex,
      laneId: overrides.laneId ?? sourceLaneIndex,
      sourceLaneIndex,
      sourceTeam,
      sourceBarracksId: overrides.sourceBarracksId ?? "center",
      sourceBarracksKey: overrides.sourceBarracksKey ?? overrides.sourceBarracksId ?? "center",
      spawnSourceType: "barracks_roster",
      allegianceKey: overrides.allegianceKey ?? sourceTeam,
      isWaveUnit: false,
      stance: overrides.stance ?? "DEFEND",
    }),
    isWaveUnit: false,
    isDefender: true,
    baseDmg: overrides.baseDmg ?? 0,
    atkCd: overrides.atkCd ?? 999,
    atkCdTicks: overrides.atkCdTicks ?? 999,
    guardAnchorX: overrides.guardAnchorX ?? overrides.posX ?? 0,
    guardAnchorY: overrides.guardAnchorY ?? overrides.posY ?? 0,
  };
}

function tick(game, count = 1) {
  for (let i = 0; i < count; i += 1)
    simMl.mlTick(game);
}

function laneSnapshot(game, laneIndex = 0) {
  return simMl.createMLSnapshot(game).lanes[laneIndex];
}

function findPad(snapshotLane, padId) {
  return (snapshotLane && snapshotLane.fortressPads || []).find((pad) => pad && pad.padId === padId) || null;
}

function advanceUntil(game, predicate, maxTicks = 4000) {
  for (let tickIndex = 0; tickIndex < maxTicks; tickIndex += 1) {
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
  assert.equal(result.ok, true, result.reason || `expected '${type}' to succeed`);
  return result;
}

function upgradeTownCoreToTier(game, laneIndex, targetTier) {
  let lane = laneSnapshot(game, laneIndex);
  let townCore = findPad(lane, "town_core_pad");
  assert.ok(townCore, "expected the Town Core pad to exist");
  while ((townCore && townCore.tier) < targetTier) {
    act(game, laneIndex, "upgrade_building", { padId: "town_core_pad" });
    finishPadConstruction(game, laneIndex, "town_core_pad");
    lane = laneSnapshot(game, laneIndex);
    townCore = findPad(lane, "town_core_pad");
  }
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
    : lane && lane.commandAnchor;
  assert.ok(anchor, `expected lane ${lane && lane.laneIndex} to have a defend anchor`);

  const facing = lane && lane.commandFacing
    ? lane.commandFacing
    : { x: 0, y: -1 };
  const lateral = { x: -Number(facing.y) || 0, y: Number(facing.x) || 0 };

  return {
    posX: Number(anchor.x) + (Number(facing.x) * forwardOffset) + (lateral.x * lateralOffset),
    posY: Number(anchor.y) + (Number(facing.y) * forwardOffset) + (lateral.y * lateralOffset),
  };
}

test("lane command state controls barracks destinations regardless of barracks site", () => {
  const game = createGame();

  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "left"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "right"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "center"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 0);

  issueLaneCommand(game, 1, "set_lane_attack", { targetLaneIndex: 0 });
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "left"), 0);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "right"), 0);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "center"), 0);

  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: 1 });
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 1);

  issueLaneCommand(game, 1, "set_lane_retreat", { progress: 0 });
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "left"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "right"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 1, "center"), 1);
});

test("market workers keep marching their economy loop until retreat, then hold the town core and can fight", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const targetLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });

  const worker = createMarketWorker({
    id: "economy_route_worker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
  });
  sourceLane.units.push(worker);

  tick(game, 1);

  assert.equal(worker.routeType, "OUTER_LOOP");
  assert.equal(worker.pathContractType, "barracks_loop");
  assert.equal(worker.targetLaneIndex, sourceLane.laneIndex);
  assert.equal(worker.objectiveLaneIndex, sourceLane.laneIndex);
  assert.equal(worker.canEngage, false);

  const attackProgress = Number(worker.pathIdx);
  tick(game, 4);
  assert.ok(
    Math.abs(Number(worker.pathIdx) - attackProgress) > 0.0001,
    "market workers should keep advancing around the economy route while the lane attacks."
  );

  const attackStateHostile = createAttacker({
    id: "economy_worker_attack_state_hostile",
    spawnSourceType: "dungeon_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    ownerLaneIndex: -1,
    targetLaneIndex: sourceLane.laneIndex,
    laneId: sourceLane.laneIndex,
    posX: Number(worker.posX) + 0.15,
    posY: Number(worker.posY) + 0.15,
    baseSpeed: 0,
    atkCd: 999,
  });
  sourceLane.units.push(attackStateHostile);

  tick(game, 2);
  assert.equal(
    worker.combatTargetId,
    null,
    "market workers should not seek combat while still running the economy route."
  );

  sourceLane.units = sourceLane.units.filter((unit) => unit !== attackStateHostile);

  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const defendProgress = Number(worker.pathIdx);
  tick(game, 4);
  assert.equal(worker.pathContractType, "barracks_loop");
  assert.equal(worker.canEngage, false);
  assert.ok(
    Math.abs(Number(worker.pathIdx) - defendProgress) > 0.0001,
    "market workers should keep looping during DEFEND instead of joining the combat anchor."
  );

  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_retreat", { progress: 0 });
  const distanceBeforeRetreat = Math.hypot(
    Number(worker.posX) - Number(getTownCoreTargetPosition(sourceLane).posX),
    Number(worker.posY) - Number(getTownCoreTargetPosition(sourceLane).posY)
  );
  tick(game, 1);

  assert.equal(worker.pathContractType, "retreat_anchor");
  assert.equal(worker.currentWaypointTargetKind, "town_core");
  assert.equal(worker.canEngage, true);
  const distanceToRetreatSlotAfterFirstTick = Math.hypot(
    Number(worker.posX) - Number(worker.anchorTargetX),
    Number(worker.posY) - Number(worker.anchorTargetY)
  );
  assert.ok(
    distanceBeforeRetreat > 0.5,
    "expected the worker to be away from the Town Core before retreat begins."
  );
  assert.ok(
    distanceToRetreatSlotAfterFirstTick > 0.25,
    "retreating market workers should walk back to the Town Core instead of snapping into their retreat slot."
  );

  let reachedRetreatHold = false;
  for (let step = 0; step < 80; step += 1) {
    tick(game, 1);
    const distanceToRetreatSlot = Math.hypot(
      Number(worker.posX) - Number(worker.anchorTargetX),
      Number(worker.posY) - Number(worker.anchorTargetY)
    );
    if (distanceToRetreatSlot <= 0.25) {
      reachedRetreatHold = true;
      break;
    }
  }
  assert.equal(reachedRetreatHold, true, "retreating market workers should eventually walk back into their Town Core hold slot.");

  const retreatHostile = createAttacker({
    id: "economy_worker_retreat_hostile",
    spawnSourceType: "dungeon_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    ownerLaneIndex: -1,
    targetLaneIndex: sourceLane.laneIndex,
    laneId: sourceLane.laneIndex,
    posX: Number(worker.anchorCenterX) + 0.2,
    posY: Number(worker.anchorCenterY) + 0.2,
    baseSpeed: 0,
    atkCd: 999,
  });
  sourceLane.units.push(retreatHostile);

  let foundRetreatCombat = false;
  for (let step = 0; step < 20; step += 1) {
    tick(game, 1);
    if (worker.combatTargetId === retreatHostile.id) {
      foundRetreatCombat = true;
      break;
    }
  }

  assert.equal(foundRetreatCombat, true, "retreating market workers should acquire nearby hostiles once they fall back to the Town Core.");
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
  assert.equal(firstConfig.battlefieldLayout.layoutId, "lava_lake_funnel:server_v2");
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

test("battlefield layout emits authored world-space fortress positions instead of compressed sim-space footprints", () => {
  const config = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });

  const redLane = config.battlefieldLayout.lanes.find((lane) => lane && lane.laneKey === "red");
  const yellowLane = config.battlefieldLayout.lanes.find((lane) => lane && lane.laneKey === "yellow");
  assert.ok(redLane, "expected red lane layout");
  assert.ok(yellowLane, "expected yellow lane layout");

  const redPadXs = redLane.fortressPads.map((pad) => Number(pad && pad.world && pad.world.x));
  const yellowPadXs = yellowLane.fortressPads.map((pad) => Number(pad && pad.world && pad.world.x));
  assert.ok(
    Math.max(...redPadXs) < Math.min(...yellowPadXs),
    "red lane should stay on the authored left-side fortress footprint relative to yellow."
  );
  assert.ok(
    (Math.min(...yellowPadXs) - Math.max(...redPadXs)) > 300,
    "the two fortress footprints should preserve authored world-space separation instead of collapsing into compressed sim-space."
  );
  assert.ok(
    Math.abs(Number(redLane.townCore.x) - Number(yellowLane.townCore.x)) > 400,
    "town core coordinates should preserve authored world-space spacing."
  );
});

test("battlefield layout perimeter segments follow the town-core box corners", () => {
  const config = simMl.createMLPublicConfig({
    playerCount: 4,
    laneTeams: ["red", "yellow", "blue", "green"],
  });

  const laneByKey = new Map(
    config.battlefieldLayout.lanes
      .filter((lane) => lane && lane.laneKey)
      .map((lane) => [lane.laneKey, lane])
  );
  const segmentById = new Map(
    config.battlefieldLayout.routeSegments
      .filter((segment) => segment && segment.segmentId)
      .map((segment) => [segment.segmentId, segment])
  );

  const redLane = laneByKey.get("red");
  const yellowLane = laneByKey.get("yellow");
  const blueLane = laneByKey.get("blue");
  const greenLane = laneByKey.get("green");
  assert.ok(redLane && yellowLane && blueLane && greenLane, "expected all four battlefield lanes in the layout payload");

  const topLeftCorner = { x: Number(redLane.townCore.x), y: Number(greenLane.townCore.y) };
  const topRightCorner = { x: Number(yellowLane.townCore.x), y: Number(greenLane.townCore.y) };
  const bottomRightCorner = { x: Number(yellowLane.townCore.x), y: Number(blueLane.townCore.y) };
  const bottomLeftCorner = { x: Number(redLane.townCore.x), y: Number(blueLane.townCore.y) };

  assert.deepEqual(segmentById.get("A_D")?.points, [
    { x: Number(redLane.townCore.x), y: Number(redLane.townCore.y) },
    topLeftCorner,
    { x: Number(greenLane.townCore.x), y: Number(greenLane.townCore.y) },
  ]);
  assert.deepEqual(segmentById.get("D_B")?.points, [
    { x: Number(greenLane.townCore.x), y: Number(greenLane.townCore.y) },
    topRightCorner,
    { x: Number(yellowLane.townCore.x), y: Number(yellowLane.townCore.y) },
  ]);
  assert.deepEqual(segmentById.get("B_C")?.points, [
    { x: Number(yellowLane.townCore.x), y: Number(yellowLane.townCore.y) },
    bottomRightCorner,
    { x: Number(blueLane.townCore.x), y: Number(blueLane.townCore.y) },
  ]);
  assert.deepEqual(segmentById.get("C_A")?.points, [
    { x: Number(blueLane.townCore.x), y: Number(blueLane.townCore.y) },
    bottomLeftCorner,
    { x: Number(redLane.townCore.x), y: Number(redLane.townCore.y) },
  ]);
});

test("two-player battlefield layout still publishes outer-loop perimeter routes for side barracks", () => {
  const twoPlayerConfig = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });
  const fourPlayerConfig = simMl.createMLPublicConfig({
    playerCount: 4,
    laneTeams: ["red", "yellow", "blue", "green"],
  });

  const twoPlayerNodeIds = new Set(
    (twoPlayerConfig.battlefieldLayout.routeNodes || [])
      .filter((node) => node && node.nodeId)
      .map((node) => node.nodeId)
  );
  const twoPlayerSegments = new Map(
    (twoPlayerConfig.battlefieldLayout.routeSegments || [])
      .filter((segment) => segment && segment.segmentId)
      .map((segment) => [segment.segmentId, segment])
  );
  const fourPlayerSegments = new Map(
    (fourPlayerConfig.battlefieldLayout.routeSegments || [])
      .filter((segment) => segment && segment.segmentId)
      .map((segment) => [segment.segmentId, segment])
  );

  assert.equal(twoPlayerNodeIds.has("C"), true, "expected the 2-player route graph to keep the lower-right perimeter corner node");
  assert.equal(twoPlayerNodeIds.has("D"), true, "expected the 2-player route graph to keep the upper-left perimeter corner node");

  for (const segmentId of ["A_D", "D_B", "B_C", "C_A", "A_C", "C_B", "B_D", "D_A"]) {
    assert.deepEqual(
      twoPlayerSegments.get(segmentId)?.points,
      fourPlayerSegments.get(segmentId)?.points,
      `expected 2-player layout to publish authored perimeter segment '${segmentId}' for side-barracks attack routes`
    );
  }
});

test("battlefield layout content hash matches the authored environment footprint across player counts", () => {
  const twoPlayerConfig = simMl.createMLPublicConfig({
    playerCount: 2,
    laneTeams: ["red", "yellow"],
  });
  const fourPlayerConfig = simMl.createMLPublicConfig({
    playerCount: 4,
    laneTeams: ["red", "yellow", "blue", "green"],
  });

  assert.equal(
    twoPlayerConfig.battlefieldLayout.contentHash,
    fourPlayerConfig.battlefieldLayout.contentHash,
    "environment content hash should describe the authored environment, not the active lane subset");
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
  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: 1 });
  game.lanes[1].eliminated = true;

  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "center"), 1);
  assert.equal(simMl.resolveTargetLaneForBarracksSend(game, 0, "left"), 1);
});

test("defend mode stages same-lane units into spaced slots around the home anchor", () => {
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
  assert.ok(secondRow.posY < firstRow.posY, "the second hold row should stage deeper in the home-side anchor line.");
});

test("route initialization reflects the active lane-command contract for every barracks site", () => {
  const game = createGame();
  const targetLane = game.lanes[2];

  const outerUnit = createAttacker({
    id: "outer_unit",
    sourceLaneIndex: 0,
    sourceBarracksId: "left",
  });
  simMl.initializeMovingUnitRouteState(game, targetLane, outerUnit, { x: 5, y: 0 });
  assert.equal(outerUnit.routeType, simMl.ROUTE_TYPES.CENTER_CROSS);
  assert.equal(outerUnit.pathContractType, "guard_anchor");
  assert.equal(outerUnit.stance, "DEFEND");
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
  assert.equal(centerUnit.pathContractType, "guard_anchor");
  assert.equal(centerUnit.stance, "DEFEND");
  assert.equal(centerUnit.routeStartNode, "ACTR");
  assert.equal(centerUnit.routeTargetNode, "B");
  assert.equal(centerUnit.currentSegment, "ACTR_A");
  assert.deepEqual(centerUnit.routeSegments, ["ACTR_A", "A_M", "M_B"]);

  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });

  const attackUnit = createAttacker({
    id: "attack_outer_unit",
    sourceLaneIndex: 0,
    sourceBarracksId: "left",
  });
  simMl.initializeMovingUnitRouteState(game, targetLane, attackUnit, { x: 5, y: 0 });
  assert.equal(attackUnit.routeType, simMl.ROUTE_TYPES.OUTER_LOOP);
  assert.equal(attackUnit.pathContractType, "barracks_loop");
  assert.equal(attackUnit.stance, "ATTACK");
  assert.equal(attackUnit.routeTargetNode, "C");
  assert.deepEqual(attackUnit.routeSegments, ["ALFT_A", "A_D", "D_B", "B_C", "C_A"]);

  const reverseAttackUnit = createAttacker({
    id: "attack_reverse_outer_unit",
    sourceLaneIndex: 0,
    sourceBarracksId: "right",
  });
  simMl.initializeMovingUnitRouteState(game, targetLane, reverseAttackUnit, { x: 5, y: 0 });
  assert.equal(reverseAttackUnit.routeType, simMl.ROUTE_TYPES.OUTER_LOOP);
  assert.equal(reverseAttackUnit.pathContractType, "barracks_loop");
  assert.equal(reverseAttackUnit.stance, "ATTACK");
  assert.equal(reverseAttackUnit.routeTargetNode, "C");
  assert.deepEqual(reverseAttackUnit.routeSegments, ["ARGT_A", "A_C", "C_B", "B_D", "D_A"]);
});

test("live left and right barracks attackers keep their authored outer-loop routes after reassignment", () => {
  const game = createGame();
  const sourceLane = game.lanes[0];
  const targetLane = game.lanes[2];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });

  const leftUnit = createAttacker({
    id: "left_live_outer_unit",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceBarracksId: "left",
    sourceBarracksKey: "left",
  });
  const rightUnit = createAttacker({
    id: "right_live_outer_unit",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceBarracksId: "right",
    sourceBarracksKey: "right",
  });

  assert.equal(simMl.initializeMovingUnitRouteState(game, targetLane, leftUnit, { x: 5, y: 0 }).ok, true);
  assert.equal(simMl.initializeMovingUnitRouteState(game, targetLane, rightUnit, { x: 6, y: 0 }).ok, true);

  sourceLane.units.push(leftUnit, rightUnit);
  tick(game, 1);

  const liveLeft = game.lanes.flatMap((lane) => lane.units || []).find((unit) => unit && unit.id === leftUnit.id);
  const liveRight = game.lanes.flatMap((lane) => lane.units || []).find((unit) => unit && unit.id === rightUnit.id);
  assert.ok(liveLeft, "expected the live left-barracks unit to remain in the simulation");
  assert.ok(liveRight, "expected the live right-barracks unit to remain in the simulation");
  assert.equal(liveLeft.targetLaneIndex, targetLane.laneIndex);
  assert.equal(liveRight.targetLaneIndex, targetLane.laneIndex);
  assert.equal(liveLeft.routeType, simMl.ROUTE_TYPES.OUTER_LOOP);
  assert.equal(liveRight.routeType, simMl.ROUTE_TYPES.OUTER_LOOP);
  assert.equal(liveLeft.pathContractType, "barracks_loop");
  assert.equal(liveRight.pathContractType, "barracks_loop");
  assert.deepEqual(liveLeft.routeSegments, ["ALFT_A", "A_D", "D_B", "B_C", "C_A"]);
  assert.deepEqual(liveRight.routeSegments, ["ARGT_A", "A_C", "C_B", "B_D", "D_A"]);
});

test("barracks route initialization preserves lateral travel spread before units reach the anchor", () => {
  const game = createGame();
  const targetLane = game.lanes[0];

  const leftColumn = createAttacker({
    id: "travel_spread_left",
    sourceLaneIndex: 0,
    sourceBarracksId: "center",
  });
  const rightColumn = createAttacker({
    id: "travel_spread_right",
    sourceLaneIndex: 0,
    sourceBarracksId: "center",
  });

  const leftResult = simMl.initializeMovingUnitRouteState(game, targetLane, leftColumn, { x: 4, y: 0 });
  const rightResult = simMl.initializeMovingUnitRouteState(game, targetLane, rightColumn, { x: 6, y: 0 });

  assert.equal(leftResult.ok, true);
  assert.equal(rightResult.ok, true);
  assert.notEqual(
    leftColumn.routeLateralOffset,
    rightColumn.routeLateralOffset,
    "barracks march columns should keep distinct route lateral offsets instead of collapsing into one stacked travel line"
  );
  assert.ok(
    Math.abs(Number(leftColumn.routeWorldX) - Number(rightColumn.routeWorldX)) > 0.5,
    "route snapshot world positions should reflect travel-time spread for different barracks columns"
  );
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
  issueLaneCommand(game, 0, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });
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

  assert.equal(snapLane.commandState, "DEFEND");
  assert.equal(snapLane.commandTargetLaneIndex, 0);
  assert.equal(snapLane.combatEnabled, true);
  assert.ok(Array.isArray(snapLane.commandSlots));
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

test("structures are ignored until they enter local combat vicinity", () => {
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

  assert.equal(attacker.blockedByStructure, false);
  assert.equal(attacker.blockedByStructureId, null);
  assert.equal(attacker.combatTarget, null);
});

test("nearby attackers pick the nearest hostile structure regardless of building type", () => {
  const game = createGame();
  const lane = game.lanes[0];
  issueLaneCommand(game, 1, "set_lane_attack", { targetLaneIndex: lane.laneIndex });
  upgradeTownCoreToTier(game, lane.laneIndex, 2);

  const buildResult = simMl.applyMLAction(game, 0, {
    type: "build_on_pad",
    data: { padId: "blacksmith_pad" },
  });
  assert.equal(buildResult.ok, true);

  const attacker = createAttacker({
    id: "nearest_structure_priority_test",
    sourceLaneIndex: 1,
    sourceTeam: "yellow",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    posX: -24,
    posY: 23,
    atkCd: 10,
  });

  lane.units.push(attacker);
  tick(game, 1);

  assert.equal(attacker.blockedByStructure, true);
  assert.equal(attacker.combatTarget?.padId, "town_core_pad");
  assert.equal(attacker.blockedByStructureType, "town_core");
});

test("nearby hostile structures are targeted by proximity instead of the current objective lane", () => {
  const game = createGame();
  const sourceLane = game.lanes[0];
  const objectiveLane = game.lanes[1];
  const nearbyHostileLane = game.lanes[2];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: objectiveLane.laneIndex });

  const nearbyTownCore = getTownCoreTargetPosition(nearbyHostileLane);
  const attacker = createAttacker({
    id: "off_objective_structure_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    targetLaneIndex: objectiveLane.laneIndex,
    laneId: objectiveLane.laneIndex,
    posX: Number(nearbyTownCore.posX) + 0.4,
    posY: Number(nearbyTownCore.posY) + 0.2,
    atkCd: 999,
    baseSpeed: 0,
  });

  objectiveLane.units.push(attacker);

  tick(game, 1);

  assert.equal(attacker.combatTarget?.kind, "fortress_pad");
  assert.equal(
    attacker.combatTarget?.laneIndex,
    nearbyHostileLane.laneIndex,
    "once no hostile units are nearby, the closest hostile structure should be selected even when it is off the current objective lane."
  );
});

test("waves do not tunnel through intact front defenses to chase defenders deeper inside the fortress", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  primeDefendAnchor(game, lane);
  upgradeTownCoreToTier(game, lane.laneIndex, 2);
  act(game, lane.laneIndex, "build_on_pad", { padId: "wall_front_left_01_pad" });
  finishPadConstruction(game, lane.laneIndex, "wall_front_left_01_pad");
  const defenderInteriorY = Number(lane.outsideGateAnchor.y) + 10.5;
  const breachLimitY = defenderInteriorY - 2;

  const defender = createDefender({
    id: "fortress_inner_guard",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    posX: Number(lane.outsideGateAnchor.x),
    posY: defenderInteriorY,
    pathIdx: defenderInteriorY,
    guardAnchorX: Number(lane.outsideGateAnchor.x),
    guardAnchorY: defenderInteriorY,
  });
  const attacker = createAttacker({
    id: "blocked_wave_chaser",
    ownerLane: -1,
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    allegianceKey: "dungeon",
    baseSpeed: 0.9,
    atkCd: 0,
  });
  assert.equal(simMl.initializeMovingUnitRouteState(game, lane, attacker, { x: 5, y: 0 }).ok, true);

  lane.units.push(defender, attacker);

  let sawStructureBlock = false;
  for (let step = 0; step < 40; step += 1) {
    tick(game, 1);
    const liveAttacker = lane.units.find((unit) => unit.id === attacker.id);
    const gatePad = lane.fortressPads.find((pad) => pad && pad.padId === "gate_front_pad");
    assert.ok(liveAttacker, "expected the dungeon wave to remain alive during the gate approach");
    assert.ok(gatePad && gatePad.hp > 0, "expected the front gate to still be intact during the interception window");
    assert.ok(
      Number(liveAttacker.posY) < breachLimitY,
      "the wave should not cross the intact front defense line just because it sees a defender deeper inside"
    );
    if (liveAttacker.combatTarget?.kind === "fortress_pad")
      sawStructureBlock = true;
    assert.notEqual(
      liveAttacker.combatTarget?.unitId,
      defender.id,
      "the wave should not lock onto a deeper defender while an intact structure still blocks the assault path"
    );
  }

  assert.equal(sawStructureBlock, true, "expected the wave to be held on a fortress structure target before it could chase the inner defender");
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

test("route units pick the closest hostile unit even when it lives in another lane container", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const homeLane = game.lanes[0];
  const otherLane = game.lanes[1];
  const wave = createAttacker({
    id: "cross_lane_wave",
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: homeLane.laneIndex,
    laneId: homeLane.laneIndex,
    posX: 0,
    posY: 0,
    atkCd: 999,
    baseSpeed: 0,
  });
  const fartherTarget = createAttacker({
    id: "route_lane_target",
    sourceLaneIndex: homeLane.laneIndex,
    sourceTeam: homeLane.team,
    sourceBarracksId: "center",
    targetLaneIndex: homeLane.laneIndex,
    laneId: homeLane.laneIndex,
    posX: 3.6,
    posY: 0,
    atkCd: 999,
    baseSpeed: 0,
  });
  const closerTarget = createAttacker({
    id: "other_lane_target",
    sourceLaneIndex: otherLane.laneIndex,
    sourceTeam: otherLane.team,
    sourceBarracksId: "center",
    targetLaneIndex: otherLane.laneIndex,
    laneId: otherLane.laneIndex,
    posX: 0.8,
    posY: 0.2,
    atkCd: 999,
    baseSpeed: 0,
  });

  homeLane.units.push(wave, fartherTarget);
  otherLane.units.push(closerTarget);

  tick(game, 1);

  assert.equal(
    wave.combatTarget?.unitId,
    closerTarget.id,
    "combat targeting should choose the closest hostile unit, not the one that happens to share the current route lane container."
  );
});

test("attack-mode lane units share the first hostile contact instead of letting trailing units walk past", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const attackerLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: attackerLane.laneIndex });
  const attackers = [
    createAttacker({
      id: "attack_line_front",
      sourceLaneIndex: sourceLane.laneIndex,
      sourceTeam: sourceLane.team,
      sourceBarracksId: "center",
      targetLaneIndex: attackerLane.laneIndex,
      laneId: attackerLane.laneIndex,
      atkCd: 0,
      baseDmg: 4,
      baseSpeed: 1.2,
    }),
    createAttacker({
      id: "attack_line_mid",
      sourceLaneIndex: sourceLane.laneIndex,
      sourceTeam: sourceLane.team,
      sourceBarracksId: "center",
      targetLaneIndex: attackerLane.laneIndex,
      laneId: attackerLane.laneIndex,
      atkCd: 0,
      baseDmg: 4,
      baseSpeed: 1.2,
    }),
    createAttacker({
      id: "attack_line_rear",
      sourceLaneIndex: sourceLane.laneIndex,
      sourceTeam: sourceLane.team,
      sourceBarracksId: "center",
      targetLaneIndex: attackerLane.laneIndex,
      laneId: attackerLane.laneIndex,
      atkCd: 0,
      baseDmg: 4,
      baseSpeed: 1.2,
    }),
  ];
  const hostileWave = createAttacker({
    id: "attack_line_wave",
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 1.1,
  });

  attackers.forEach((unit, index) => {
    assert.equal(simMl.initializeMovingUnitRouteState(game, attackerLane, unit, { x: 5, y: index }).ok, true);
  });
  assert.equal(simMl.initializeMovingUnitRouteState(game, attackerLane, hostileWave, { x: 5, y: 0 }).ok, true);
  attackerLane.units.push(...attackers, hostileWave);

  let sharedContactEstablished = false;
  for (let i = 0; i < 80 && !sharedContactEstablished; i += 1) {
    tick(game, 1);
    const liveAttackers = attackerLane.units.filter((unit) => unit && unit.id.startsWith("attack_line_") && unit.id !== hostileWave.id);
    sharedContactEstablished = liveAttackers.length === 3
      && liveAttackers.every((unit) => unit.combatTarget?.unitId === hostileWave.id);
  }

  const liveAttackers = attackerLane.units.filter((unit) => unit && unit.id.startsWith("attack_line_") && unit.id !== hostileWave.id);

  assert.equal(liveAttackers.length, 3);
  assert.ok(
    sharedContactEstablished,
    "once the lead unit finds contact, the rest of the attackers should also stop and join that fight."
  );
  assert.ok(
    liveAttackers.every((unit) => unit.pathContractType === "intercept"),
    "units that join the shared contact should publish intercept routing instead of continuing past the fight."
  );
});

test("attack-mode lane units wait for local engagement range instead of auto-locking everything inside the wider combat leash", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const attackerLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: attackerLane.laneIndex });

  const attacker = createAttacker({
    id: "attack_leash_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    atkCd: 999,
    baseSpeed: 1.2,
  });
  assert.equal(simMl.initializeMovingUnitRouteState(game, attackerLane, attacker, { x: 5, y: 0 }).ok, true);

  const yellowHostile = createAttacker({
    id: "attack_leash_yellow_hostile",
    ownerLaneIndex: -1,
    ownerLane: -1,
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    allegianceKey: "yellow",
    routeSegments: ["WB_B"],
    routeSegmentIndex: 0,
    segmentProgress: 0,
    atkCd: 999,
    baseSpeed: 0,
    posX: Number(attacker.posX) + 6,
    posY: Number(attacker.posY),
  });

  attackerLane.units.push(attacker, yellowHostile);
  tick(game, 1);

  assert.equal(attacker.combatLeashRadius, 8);
  assert.equal(
    attacker.combatTarget ?? null,
    null,
    "attack-mode units should not pre-aggro hostiles that are only inside the broader combat leash."
  );

  yellowHostile.posX = Number(attacker.posX) + 4.2;
  yellowHostile.posY = Number(attacker.posY);
  yellowHostile.routeWorldX = yellowHostile.posX;
  yellowHostile.routeWorldY = yellowHostile.posY;

  advanceUntil(game, () => attacker.combatTarget?.unitId === yellowHostile.id, 8);

  assert.equal(
    attacker.combatTarget?.unitId,
    yellowHostile.id,
    "attack-mode units should still acquire a hostile once it enters true local engagement range."
  );
  assert.equal(attacker.movementMode, "CombatEngage");
});

test("attack-mode home units still treat a hostile inside the home fortress interior as an emergency target", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const homeLane = game.lanes[0];
  const enemyLane = game.lanes[1];
  issueLaneCommand(game, homeLane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, homeLane);
  issueLaneCommand(game, homeLane.laneIndex, "set_barracks_attack", {
    barracksId: "center",
    targetLaneIndex: enemyLane.laneIndex,
  });

  const attackers = [
    createAttacker({
      id: "attack_home_emergency_front",
      sourceLaneIndex: homeLane.laneIndex,
      sourceTeam: homeLane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: enemyLane.laneIndex,
      laneId: homeLane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(homeLane, -0.9, -0.3),
    }),
    createAttacker({
      id: "attack_home_emergency_rear",
      sourceLaneIndex: homeLane.laneIndex,
      sourceTeam: homeLane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: enemyLane.laneIndex,
      laneId: homeLane.laneIndex,
      atkCd: 0,
      baseDmg: 3,
      baseSpeed: 1.1,
      ...getDefendAnchorPosition(homeLane, -1.5, 0.35),
    }),
  ];
  const townCore = getTownCoreTargetPosition(homeLane);
  const hostileWave = createAttacker({
    id: "attack_home_emergency_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: homeLane.laneIndex,
    laneId: homeLane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    posX: townCore.posX + 0.8,
    posY: townCore.posY - 5.0,
  });

  homeLane.units.push(...attackers, hostileWave);

  for (const attacker of attackers) {
    const preferredTarget = simMl.getWaveUnitPreferredTarget(game, homeLane, attacker);
    assert.equal(preferredTarget?.kind, "unit", "attack-mode home units should publish an emergency unit target inside the home fortress.");
    assert.equal(
      preferredTarget?.entity?.id,
      hostileWave.id,
      "attack-mode home units should pick the intruder instead of ignoring a hostile already inside the fortress."
    );
  }
});

test("combat target selection still picks the nearby fortress intruder when many distant wave units exist", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const homeLane = game.lanes[0];
  const enemyLane = game.lanes[1];
  issueLaneCommand(game, homeLane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, homeLane);
  issueLaneCommand(game, homeLane.laneIndex, "set_barracks_attack", {
    barracksId: "center",
    targetLaneIndex: enemyLane.laneIndex,
  });

  const attacker = createAttacker({
    id: "attack_home_spatial_context_attacker",
    sourceLaneIndex: homeLane.laneIndex,
    sourceTeam: homeLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: enemyLane.laneIndex,
    laneId: homeLane.laneIndex,
    atkCd: 0,
    baseDmg: 3,
    baseSpeed: 1.1,
    ...getDefendAnchorPosition(homeLane, -0.9, -0.3),
  });
  const townCore = getTownCoreTargetPosition(homeLane);
  const nearbyHostileWave = createAttacker({
    id: "attack_home_spatial_context_nearby_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: homeLane.laneIndex,
    laneId: homeLane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    posX: townCore.posX + 0.8,
    posY: townCore.posY - 5.0,
  });

  homeLane.units.push(attacker, nearbyHostileWave);
  for (let index = 0; index < 48; index += 1) {
    enemyLane.units.push(createAttacker({
      id: `attack_home_spatial_context_far_wave_${index}`,
      sourceLaneIndex: -1,
      sourceTeam: null,
      sourceBarracksId: null,
      sourceBarracksKey: null,
      spawnSourceType: "dungeon_wave",
      targetLaneIndex: enemyLane.laneIndex,
      laneId: enemyLane.laneIndex,
      hp: 120,
      maxHp: 120,
      atkCd: 999,
      baseSpeed: 0,
      posX: townCore.posX + 24 + (index % 8),
      posY: townCore.posY - 24 - Math.floor(index / 8),
    }));
  }

  const preferredTarget = simMl.getWaveUnitPreferredTarget(game, homeLane, attacker);
  assert.equal(preferredTarget?.kind, "unit");
  assert.equal(
    preferredTarget?.entity?.id,
    nearbyHostileWave.id,
    "combat target selection should still resolve the closest emergency hostile instead of being distracted by distant wave units."
  );
});

test("attack-mode home units stay committed to a fortress intruder after combat has already started", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const homeLane = game.lanes[0];
  const enemyLane = game.lanes[1];
  issueLaneCommand(game, homeLane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, homeLane);
  issueLaneCommand(game, homeLane.laneIndex, "set_barracks_attack", {
    barracksId: "center",
    targetLaneIndex: enemyLane.laneIndex,
  });

  const attacker = createAttacker({
    id: "attack_home_commit_attacker",
    sourceLaneIndex: homeLane.laneIndex,
    sourceTeam: homeLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: enemyLane.laneIndex,
    laneId: homeLane.laneIndex,
    atkCd: 0,
    baseDmg: 3,
    baseSpeed: 1.1,
    ...getDefendAnchorPosition(homeLane, -0.9, -0.3),
  });
  const townCore = getTownCoreTargetPosition(homeLane);
  const hostileWave = createAttacker({
    id: "attack_home_commit_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    sourceBarracksKey: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: homeLane.laneIndex,
    laneId: homeLane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    posX: townCore.posX + 0.8,
    posY: townCore.posY - 5.0,
  });

  homeLane.units.push(attacker, hostileWave);

  advanceUntil(game, () => attacker.combatTarget?.unitId === hostileWave.id, 8);
  assert.equal(
    attacker.combatTarget?.unitId,
    hostileWave.id,
    "the attacker should first acquire the fortress intruder as an emergency target."
  );

  hostileWave.posX = townCore.posX + 0.8;
  hostileWave.posY = townCore.posY - 15.2;
  hostileWave.routeWorldX = hostileWave.posX;
  hostileWave.routeWorldY = hostileWave.posY;
  hostileWave.pathIdx = hostileWave.posY;

  tick(game, 1);

  assert.equal(
    attacker.combatTarget?.unitId,
    hostileWave.id,
    "once combat has started, the attacker should stay committed instead of abandoning the intruder and running back to route."
  );
  assert.equal(attacker.movementMode, "CombatEngage");
});

test("cross-lane hostile kills pay the attacker's owning lane instead of the victim lane container", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const targetLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });

  const attacker = createAttacker({
    id: "reward_lane_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: targetLane.laneIndex,
    laneId: targetLane.laneIndex,
    atkCd: 0,
    baseDmg: 12,
    baseSpeed: 1.1,
  });
  assert.equal(simMl.initializeMovingUnitRouteState(game, targetLane, attacker, { x: 5, y: 0 }).ok, true);

  const yellowVictim = createAttacker({
    id: "reward_lane_yellow_victim",
    sourceLaneIndex: targetLane.laneIndex,
    sourceTeam: targetLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: targetLane.laneIndex,
    laneId: targetLane.laneIndex,
    hp: 6,
    maxHp: 6,
    atkCd: 999,
    baseDmg: 0,
    baseSpeed: 0,
    bounty: 5,
    posX: attacker.posX,
    posY: attacker.posY,
  });

  const redGoldBefore = sourceLane.gold;
  const yellowGoldBefore = targetLane.gold;
  targetLane.units.push(attacker, yellowVictim);

  advanceUntil(game, () => !targetLane.units.some((unit) => unit && unit.id === yellowVictim.id), 80);

  assert.equal(sourceLane.gold, redGoldBefore + 5);
  assert.equal(targetLane.gold, yellowGoldBefore);
});

test("projectile kills on dungeon units still pay the hostile shooter's owning lane", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const targetLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: targetLane.laneIndex });

  const attacker = createAttacker({
    id: "reward_projectile_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: targetLane.laneIndex,
    laneId: targetLane.laneIndex,
    atkCd: 999,
    baseSpeed: 0,
  });
  assert.equal(simMl.initializeMovingUnitRouteState(game, targetLane, attacker, { x: 5, y: 0 }).ok, true);

  const dungeonVictim = createAttacker({
    id: "reward_projectile_dungeon_victim",
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: targetLane.laneIndex,
    laneId: targetLane.laneIndex,
    hp: 4,
    maxHp: 4,
    atkCd: 999,
    baseDmg: 0,
    baseSpeed: 0,
    bounty: 4,
    posX: attacker.posX,
    posY: attacker.posY,
  });

  const redGoldBefore = sourceLane.gold;
  const yellowGoldBefore = targetLane.gold;
  targetLane.units.push(attacker, dungeonVictim);
  targetLane.projectiles.push({
    id: "reward_projectile",
    ownerLane: targetLane.laneIndex,
    sourceKind: "unit",
    sourceId: attacker.id,
    rewardLaneIndex: sourceLane.laneIndex,
    sourceAllegianceKey: attacker.allegianceKey,
    projectileType: attacker.type,
    damageType: "NORMAL",
    behavior: "single",
    behaviorParams: {},
    targetId: dungeonVictim.id,
    dmg: 8,
    fromX: attacker.posX,
    fromY: attacker.posY,
    toX: dungeonVictim.posX,
    toY: dungeonVictim.posY,
    ticksRemaining: 1,
    ticksTotal: 1,
    abilities: [],
  });

  tick(game, 1);

  assert.equal(sourceLane.gold, redGoldBefore + 4);
  assert.equal(targetLane.gold, yellowGoldBefore);
  assert.equal(
    targetLane.units.some((unit) => unit && unit.id === dungeonVictim.id),
    false,
    "the projectile target should be removed after the lethal hit resolves."
  );
});

test("lane-controlled attackers stay committed while already in direct contact instead of swapping to a slightly closer newcomer", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const sourceLane = game.lanes[0];
  const attackerLane = game.lanes[1];
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: attackerLane.laneIndex });
  const attacker = createAttacker({
    id: "sticky_target_attacker",
    sourceLaneIndex: sourceLane.laneIndex,
    sourceTeam: sourceLane.team,
    sourceBarracksId: "center",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    atkCd: 999,
    baseSpeed: 1.2,
  });
  const firstWave = createAttacker({
    id: "sticky_target_wave_1",
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 1.1,
  });

  assert.equal(simMl.initializeMovingUnitRouteState(game, attackerLane, attacker, { x: 5, y: 0 }).ok, true);
  assert.equal(simMl.initializeMovingUnitRouteState(game, attackerLane, firstWave, { x: 5, y: 0 }).ok, true);
  attackerLane.units.push(attacker, firstWave);

  for (let i = 0; i < 80 && attacker.combatTarget?.unitId !== firstWave.id; i += 1)
    tick(game, 1);

  assert.equal(attacker.combatTarget?.unitId, firstWave.id, "the attacker should lock onto the first hostile contact.");

  const secondWave = createAttacker({
    id: "sticky_target_wave_2",
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: attackerLane.laneIndex,
    laneId: attackerLane.laneIndex,
    hp: 120,
    maxHp: 120,
    atkCd: 999,
    baseSpeed: 0,
    posX: Number(attacker.posX) + 0.2,
    posY: Number(attacker.posY) + 0.2,
  });

  attackerLane.units.push(secondWave);

  tick(game, 1);

  assert.equal(
    attacker.combatTarget?.unitId,
    firstWave.id,
    "a lane-controlled attacker should stay committed to the unit it is already actively fighting."
  );
});

test("lane-controlled defenders can switch from a queued target to a closer hostile before contact", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);

  const defender = createAttacker({
    id: "queued_switch_defender",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    hp: 80,
    maxHp: 80,
    atkCd: 999,
    baseDmg: 8,
    baseSpeed: 0.45,
    ...getDefendAnchorPosition(lane, -0.6, 0),
  });
  const firstWave = createAttacker({
    id: "queued_switch_wave_1",
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
    ...getDefendAnchorPosition(lane, 3.8, 0),
  });

  lane.units.push(defender, firstWave);

  tick(game, 1);

  assert.equal(defender.combatTarget?.unitId, firstWave.id, "the defender should initially queue onto the first hostile.");
  assert.equal(defender.attackPulse || 0, 0, "the defender should not already be in direct contact with the first hostile.");

  const secondWave = createAttacker({
    id: "queued_switch_wave_2",
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
    ...getDefendAnchorPosition(lane, 1.4, 0.5),
  });

  lane.units.push(secondWave);

  tick(game, 1);

  assert.equal(
    defender.combatTarget?.unitId,
    secondWave.id,
    "before reaching contact, a queued defender should switch to the closer hostile that enters engagement range."
  );
});

test("lane-controlled defenders keep chaining through nearby threats instead of regrouping between adjacent kills", () => {
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
  assert.ok(serverDefender, "the chaining defender should still be alive.");
  assert.equal(
    serverDefender.combatTarget?.unitId,
    secondWave.id,
    "after a kill, a lane-controlled defender should immediately chain onto another hostile that is already inside direct engagement range."
  );
  assert.ok(
    Number(serverDefender.regroupUntilTick) <= game.tick,
    "the regroup window should stay cleared while the defender is chaining through adjacent threats."
  );
});

test("lane-controlled defenders still regroup after a kill when the next threat is outside direct engagement range", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.6, 0);
  const firstWavePoint = getDefendAnchorPosition(lane, 0.1, 0);
  const secondWavePoint = getDefendAnchorPosition(lane, 0.2, 5.5);
  const defender = createAttacker({
    id: "regroup_far_defender",
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
    id: "regroup_far_wave_1",
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
    id: "regroup_far_wave_2",
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
    "after a kill, a lane-controlled defender should still regroup when no other hostile remains inside direct engagement range."
  );
  assert.ok(
    Number(serverDefender.regroupUntilTick) > game.tick,
    "the regroup window should stay active for a short time when there is no immediate follow-through target."
  );
  assert.ok(
    lane.units.some((unit) => unit.id === secondWave.id),
    "the distant follow-up threat should still be present so the test is only measuring regroup behavior."
  );
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

test("switching a defend lane from hold to attack keeps it moving along the live route instead of skipping to the enemy core", () => {
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
  assert.equal(snapDefender.combatLockTicksRemaining, 0, "lane-controlled contact no longer relies on a synthetic target-lock window.");
});

test("snapshot contract keeps nearby follow-through combat instead of exposing regroup between adjacent kills", () => {
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

  assert.ok(snapDefender, "expected the chaining defender to appear in the snapshot.");
  assert.equal(
    snapDefender.combatTargetId,
    secondWave.id,
    "the snapshot should keep the nearby follow-through target visible instead of dropping into regroup."
  );
  assert.ok(
    snapDefender.presentationPhase === "CombatCommit" || snapDefender.presentationPhase === "CombatResolve",
    `expected the chaining defender to remain in a combat presentation phase, got ${snapDefender.presentationPhase}`
  );
  assert.equal(snapDefender.regroupTicksRemaining, 0);
});

test("snapshot contract still exposes combat regroup after a lane-controlled kill when no nearby follow-through threat exists", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.6, 0);
  const firstWavePoint = getDefendAnchorPosition(lane, 0.1, 0);
  const secondWavePoint = getDefendAnchorPosition(lane, 0.2, 5.5);
  const defender = createAttacker({
    id: "regroup_far_snapshot_defender",
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
    id: "regroup_far_snapshot_wave_1",
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
    id: "regroup_far_snapshot_wave_2",
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

test("lane-controlled units keep surround movement inside their combat leash", () => {
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

  assert.ok(lane.commandAnchor, "the lane should expose a defend command anchor before combat starts.");
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
    "the defenders should actually commit to the hostile wave instead of the test only exercising idle anchor movement."
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

test("settled lane units resolve overlap with small nudges while staying near their shallow hold slots", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const coreTarget = getTownCoreTargetPosition(lane);
  const anchorUnits = Array.from({ length: 6 }, (_, index) => createAttacker({
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

  lane.units.push(...anchorUnits);
  tick(game, 1);

  const settledUnits = lane.units.filter((unit) => unit.id.startsWith("soft_separation_unit_"));
  const facing = lane.commandFacing || { x: 0, y: -1 };
  const lateral = { x: -facing.y, y: facing.x };
  let chosenPair = null;
  let smallestAnchorDistance = Infinity;
  for (let i = 0; i < settledUnits.length; i += 1) {
    for (let j = i + 1; j < settledUnits.length; j += 1) {
      const a = settledUnits[i];
      const b = settledUnits[j];
      const anchorDx = Number(b.anchorTargetX) - Number(a.anchorTargetX);
      const anchorDy = Number(b.anchorTargetY) - Number(a.anchorTargetY);
      const depthDelta = Math.abs((anchorDx * facing.x) + (anchorDy * facing.y));
      const anchorDistance = Math.hypot(anchorDx, anchorDy);
      if (depthDelta > 0.05 || anchorDistance >= smallestAnchorDistance)
        continue;
      smallestAnchorDistance = anchorDistance;
      chosenPair = [a, b];
    }
  }

  const [frontUnit, rearUnit] = chosenPair || [];
  assert.ok(frontUnit && rearUnit, "expected to find a nearby pair in the same shallow hold row.");

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

  const startingFrontAnchorDistance = Math.hypot(
    Number(frontUnit.posX) - Number(frontUnit.anchorTargetX),
    Number(frontUnit.posY) - Number(frontUnit.anchorTargetY)
  );
  const startingRearAnchorDistance = Math.hypot(
    Number(rearUnit.posX) - Number(rearUnit.anchorTargetX),
    Number(rearUnit.posY) - Number(rearUnit.anchorTargetY)
  );

  tick(game, 10);

  const updatedFront = lane.units.find((unit) => unit.id === frontUnit.id);
  const updatedRear = lane.units.find((unit) => unit.id === rearUnit.id);
  assert.ok(updatedFront && updatedRear, "the overlapped anchor-slot units should still exist after separation resolves.");
  const relativeDx = Number(updatedRear.posX) - Number(updatedFront.posX);
  const relativeDy = Number(updatedRear.posY) - Number(updatedFront.posY);
  const pairSpacing = Math.hypot(relativeDx, relativeDy);
  const frontAnchorDistance = Math.hypot(
    Number(updatedFront.posX) - Number(updatedFront.anchorTargetX),
    Number(updatedFront.posY) - Number(updatedFront.anchorTargetY)
  );
  const rearAnchorDistance = Math.hypot(
    Number(updatedRear.posX) - Number(updatedRear.anchorTargetX),
    Number(updatedRear.posY) - Number(updatedRear.anchorTargetY)
  );
  assert.ok(
    pairSpacing > 0.1,
    "the spacing pass should gradually separate settled units enough to avoid overlap."
  );
  assert.ok(
    frontAnchorDistance < startingFrontAnchorDistance,
    "settled shallow-line units should start returning toward their assigned slot after the overlap is resolved."
  );
  assert.ok(
    rearAnchorDistance < startingRearAnchorDistance,
    "settled shallow-line neighbors should also recover toward their assigned slot after the overlap is resolved."
  );
});

test("settled defend units stay parked on their assigned slots instead of buzzing at idle", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const coreTarget = getTownCoreTargetPosition(lane);
  const anchorUnits = Array.from({ length: 6 }, (_, index) => createAttacker({
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

  lane.units.push(...anchorUnits);
  tick(game, 1);

  for (const unit of lane.units.filter((entry) => entry.id.startsWith("idle_settle_unit_"))) {
    unit.posX = Number(unit.anchorTargetX);
    unit.posY = Number(unit.anchorTargetY);
    unit.pathIdx = unit.posY;
    unit.routeSegments = null;
    unit.routeSegmentIndex = 0;
    unit.segmentProgress = 0;
    unit.movementMode = "AnchorJoin";
  }

  tick(game, 3);

  const settledUnits = lane.units.filter((unit) => unit.id.startsWith("idle_settle_unit_"));
  assert.equal(settledUnits.length, anchorUnits.length);
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

test("lane-controlled defenders keep authoritative slot indices at the rally anchor", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });

  const defendAnchor = primeDefendAnchor(game, lane);
  const anchorUnits = Array.from({ length: 4 }, (_, index) => createAttacker({
    id: `rigid_slot_unit_${index}`,
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    posX: Number(defendAnchor.x) + (index * 0.08),
    posY: Number(defendAnchor.y) - 0.35 - (index * 0.06),
    atkCd: 999,
  }));

  lane.units.push(...anchorUnits);
  tick(game, 1);

  const updatedUnits = lane.units.filter((unit) => unit.id.startsWith("rigid_slot_unit_"));
  assert.equal(updatedUnits.length, anchorUnits.length, "all staged defenders should remain active.");

  for (const unit of updatedUnits) {
    assert.equal(
      Number.isInteger(unit.assignedSlotIndex),
      true,
      `expected ${unit.id} to keep an authoritative assigned slot index at the defend anchor.`
    );
    assert.ok(
      Number.isFinite(Number(unit.anchorTargetX)) && Number.isFinite(Number(unit.anchorTargetY)),
      `expected ${unit.id} to expose a defend anchor target.`
    );
    assert.ok(
      Number.isFinite(Number(unit.anchorCenterX)) && Number.isFinite(Number(unit.anchorCenterY)),
      `expected ${unit.id} to expose an anchor center for leash and slot logic.`
    );
  }
});

test("defend rally progress keeps the command anchor away from the fixed outside-gate rally", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0.5 });

  assert.ok(lane.commandAnchor, "expected a live command anchor after updating the defend rally.");
  assert.ok(lane.outsideGateAnchor, "expected the fixed outside-gate helper anchor to remain available.");
  assert.ok(
    Math.abs(Number(lane.commandAnchorProgress) - 0.5) <= 0.001,
    `expected defend rally progress to stay at 0.5, got ${lane.commandAnchorProgress}`
  );

  const rallyShift = Math.hypot(
    Number(lane.commandAnchor.x) - Number(lane.outsideGateAnchor.x),
    Number(lane.commandAnchor.y) - Number(lane.outsideGateAnchor.y)
  );
  assert.ok(
    rallyShift > 5,
    `expected a mid-lane defend rally to move away from the fixed outside gate, got shift ${rallyShift.toFixed(3)}`
  );
});

test("defend rally world anchors preserve the sampled command anchor instead of snapping back to the gate", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0.5 });
  const sampledAnchor = {
    x: Number(lane.commandAnchor && lane.commandAnchor.x),
    y: Number(lane.commandAnchor && lane.commandAnchor.y),
  };

  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", sampledAnchor);

  assert.ok(lane.commandAnchor, "expected the defend command to keep exposing a sampled command anchor.");
  assert.ok(
    Math.abs(Number(lane.commandAnchorProgress) - 0.5) <= 0.05,
    `expected defend world-anchor requests to preserve mid-lane rally progress, got ${lane.commandAnchorProgress}`
  );
  assert.ok(
    Math.abs(Number(lane.commandAnchor.x) - sampledAnchor.x) <= 0.35
      && Math.abs(Number(lane.commandAnchor.y) - sampledAnchor.y) <= 0.35,
    `expected defend world-anchor requests to stay near (${sampledAnchor.x.toFixed(3)}, ${sampledAnchor.y.toFixed(3)}), got (${Number(lane.commandAnchor.x).toFixed(3)}, ${Number(lane.commandAnchor.y).toFixed(3)})`
  );
});

test("lane-controlled defenders use combat pocket movement instead of simple contact memory against unit targets", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });

  const defendAnchor = primeDefendAnchor(game, lane);
  const defender = createAttacker({
    id: "combat_pocket_defender",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    posX: Number(defendAnchor.x),
    posY: Number(defendAnchor.y),
    atkCd: 999,
  });
  const hostileWave = createAttacker({
    id: "combat_pocket_wave",
    spawnSourceType: "dungeon_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    ownerLaneIndex: -1,
    ownerLane: -1,
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    allegianceKey: "dungeon",
    posX: Number(defendAnchor.x),
    posY: Number(defendAnchor.y) - 4.25,
    atkCd: 999,
  });

  lane.units.push(defender, hostileWave);
  tick(game, 2);

  const liveDefender = lane.units.find((unit) => unit.id === defender.id);
  assert.ok(liveDefender, "expected the defender to remain alive during the pocket approach.");
  assert.equal(liveDefender.combatTargetId, hostileWave.id, "defender should acquire the hostile wave unit as its combat target.");
  assert.equal(liveDefender.movementMode, "CombatEngage", "defender should already be in combat-engage movement while closing.");
  assert.equal(
    !!liveDefender.simpleContactApproachTargetId,
    false,
    "lane-controlled unit-vs-unit combat should route through combat pocket positioning, not the legacy simple-contact memory path."
  );
});

test("center-spawn waves pass through the red front gate corridor before they can reach the Town Core", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const redLane = game.lanes[0];
  primeDefendAnchor(game, redLane);
  const townCoreTarget = simMl.getLaneTownCoreCombatTarget(redLane);
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

  let sawGateApproach = false;
  let minGateDistance = Infinity;
  let serverWave = incomingWave;
  for (let i = 0; i < 28; i += 1) {
    tick(game, 1);
    serverWave = redLane.units.find((unit) => unit.id === incomingWave.id);
    assert.ok(serverWave, "the routed wave should still be alive while approaching the fortress gate.");
    assert.equal(serverWave.allegianceKey, "dungeon");
    assert.equal(serverWave.pathContractType, "wave_lane");
    const coreDistance = Math.hypot(
      Number(serverWave.posX) - Number(townCoreTarget.posX),
      Number(serverWave.posY) - Number(townCoreTarget.posY)
    );
    if (coreDistance > 2.25) {
      assert.notEqual(
        serverWave.combatTarget?.padId,
        "town_core_pad",
        "the wave should not aggro the Town Core until it enters local structure vicinity."
      );
    }
    minGateDistance = Math.min(
      minGateDistance,
      Math.hypot(
        Number(serverWave.posX) - Number(redLane.outsideGateAnchor.x),
        Number(serverWave.posY) - Number(redLane.outsideGateAnchor.y)
      )
    );
    if (minGateDistance <= 1.5) {
      sawGateApproach = true;
      break;
    }
  }

  assert.ok(
    sawGateApproach || minGateDistance <= 4.0,
    "center-spawn wave routing should still pass through the front-gate corridor instead of cutting directly into the Town Core."
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

test("defend-mode units fan around an intercepted wave at the gate instead of collapsing into one point", () => {
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
    "DEFEND units should commit together to a nearby interception target."
  );
  const commandFacing = lane.commandFacing || { x: 0, y: -1 };
  const anchorLateral = { x: -commandFacing.y, y: commandFacing.x };
  const lateralOffsets = liveDefenders.map((unit) =>
    ((Number(unit.posX) - Number(hostileWave.posX)) * anchorLateral.x)
    + ((Number(unit.posY) - Number(hostileWave.posY)) * anchorLateral.y));
  assert.ok(
    Math.max(...lateralOffsets) - Math.min(...lateralOffsets) > 0.7,
    "defenders should occupy distinct surround slots instead of collapsing into one narrow stack."
  );
});

test("defend-mode units from different barracks share the same gate interception target", () => {
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
    "nearby defenders from separate barracks should all commit to the same intercepted wave."
  );
});

test("intact home walls let ranged defenders shoot over the gate line", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  upgradeTownCoreToTier(game, lane.laneIndex, 2);
  act(game, lane.laneIndex, "build_on_pad", { padId: "wall_front_left_01_pad" });
  finishPadConstruction(game, lane.laneIndex, "wall_front_left_01_pad");

  const gatePad = lane.fortressPads.find((pad) => pad && pad.padId === "gate_front_pad");
  assert.ok(gatePad && gatePad.hp > 0, "expected the front gate to remain intact for the wall-fire test");
  const facing = lane.commandFacing || { x: 0, y: -1 };
  const lateral = { x: -(Number(facing.y) || 0), y: Number(facing.x) || 0 };
  const insideBaseX = Number(gatePad.posX) - ((Number(facing.x) || 0) * 1.1);
  const insideBaseY = Number(gatePad.posY) - ((Number(facing.y) || 0) * 1.1);
  const outsideBaseX = Number(gatePad.posX) + ((Number(facing.x) || 0) * 0.7);
  const outsideBaseY = Number(gatePad.posY) + ((Number(facing.y) || 0) * 0.7);

  const rangedDefender = {
    ...createAttacker({
      id: "wall_ranged_defender",
      sourceLaneIndex: lane.laneIndex,
      sourceTeam: lane.team,
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      targetLaneIndex: lane.laneIndex,
      laneId: lane.laneIndex,
      posX: insideBaseX + (lateral.x * 0.35),
      posY: insideBaseY + (lateral.y * 0.35),
    }),
    attackRangeOverride: 4.0,
  };
  const hostileWave = createAttacker({
    id: "wall_fire_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    posX: outsideBaseX,
    posY: outsideBaseY,
  });
  const targetDescriptor = { kind: "unit", entity: hostileWave, laneIndex: lane.laneIndex };
  assert.equal(
    simMl.isRouteUnitTargetBlockedByStructure(game, lane, rangedDefender, targetDescriptor, null),
    false,
    "ranged home defenders should be allowed to target a hostile over their own intact wall."
  );
});

test("intact home walls do not block defenders from targeting a hostile already behind the gate line", () => {
  const game = createTwoPlayerGame(["red", "yellow"]);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  upgradeTownCoreToTier(game, lane.laneIndex, 2);
  act(game, lane.laneIndex, "build_on_pad", { padId: "wall_front_left_01_pad" });
  finishPadConstruction(game, lane.laneIndex, "wall_front_left_01_pad");

  const gatePad = lane.fortressPads.find((pad) => pad && pad.padId === "gate_front_pad");
  assert.ok(gatePad && gatePad.hp > 0, "expected the front gate to remain intact for the same-side target test");
  const facing = lane.commandFacing || { x: 0, y: -1 };
  const defender = createAttacker({
    id: "wall_same_side_defender",
    sourceLaneIndex: lane.laneIndex,
    sourceTeam: lane.team,
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    posX: Number(gatePad.posX) - ((Number(facing.x) || 0) * 1.4),
    posY: Number(gatePad.posY) - ((Number(facing.y) || 0) * 1.4),
  });
  const hostileWave = createAttacker({
    id: "wall_same_side_wave",
    sourceLaneIndex: -1,
    sourceTeam: null,
    sourceBarracksId: null,
    spawnSourceType: "dungeon_wave",
    targetLaneIndex: lane.laneIndex,
    laneId: lane.laneIndex,
    posX: Number(gatePad.posX) - ((Number(facing.x) || 0) * 0.6),
    posY: Number(gatePad.posY) - ((Number(facing.y) || 0) * 0.6),
  });
  const targetDescriptor = { kind: "unit", entity: hostileWave, laneIndex: lane.laneIndex };
  assert.equal(
    simMl.isRouteUnitTargetBlockedByStructure(game, lane, defender, targetDescriptor, null),
    false,
    "once both units are already on the same side of the gate line, the intact wall should not suppress combat."
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
  issueLaneCommand(game, sourceLane.laneIndex, "set_lane_attack", { targetLaneIndex: defeatedLane.laneIndex });
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
    posY: coreTarget.posY - 1.5,
    pathIdx: 0.98,
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


