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
    routeState: overrides.routeState ?? "MOVING",
    routeType: overrides.routeType ?? null,
    routeStartNode: overrides.routeStartNode ?? null,
    routeTargetNode: overrides.routeTargetNode ?? null,
    routeSegments: overrides.routeSegments ?? null,
    routeSegmentIndex: overrides.routeSegmentIndex ?? 0,
    segmentProgress: overrides.segmentProgress ?? 0,
    currentSegment: overrides.currentSegment ?? null,
    routeWorldX: overrides.routeWorldX ?? overrides.posX ?? 5,
    routeWorldY: overrides.routeWorldY ?? overrides.posY ?? 27,
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
  const ownerLaneIndex = overrides.ownerLane ?? 0;
  const targetLaneIndex = overrides.targetLaneIndex ?? ownerLaneIndex;
  const sourceBarracksId = overrides.sourceBarracksId ?? "center";
  const sourceBarracksKey = overrides.sourceBarracksKey ?? sourceBarracksId;
  const spawnSourceType = overrides.spawnSourceType ?? "barracks_roster";
  return {
    id,
    unitId: id,
    unitTypeKey: typeKey,
    allegianceKey: overrides.allegianceKey ?? "red",
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    targetLaneIndex,
    sourceLaneIndex: overrides.sourceLaneIndex ?? 0,
    sourceTeam: overrides.sourceTeam ?? "red",
    sourceBarracksId,
    sourceBarracksKey,
    spawnSourceType,
    type: typeKey,
    skinKey: null,
    isHero: false,
    heroKey: null,
    heroVisualStyleKey: null,
    pathIdx: overrides.pathIdx ?? 24,
    hp: overrides.hp ?? def.hp,
    maxHp: overrides.maxHp ?? (overrides.hp ?? def.hp),
    baseDmg: overrides.baseDmg ?? def.dmg,
    baseSpeed: overrides.baseSpeed ?? def.pathSpeed,
    moveSpeed: overrides.moveSpeed ?? 0.35,
    atkCd: overrides.atkCd ?? 0,
    atkCdTicks: overrides.atkCdTicks ?? def.atkCdTicks,
    armorType: overrides.armorType ?? def.armorType,
    damageReductionPct: overrides.damageReductionPct ?? def.damageReductionPct,
    abilities: [],
    bounty: 0,
    stance: overrides.stance ?? null,
    pathContractType: overrides.pathContractType ?? null,
    isWaveUnit: false,
    isDefender: overrides.isDefender ?? false,
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

function issueLaneCommand(game, laneIndex, type, data = {}) {
  const result = simMl.applyMLAction(game, laneIndex, { type, data });
  assert.equal(result.ok, true, `expected ${type} on lane ${laneIndex} to succeed`);
  return result;
}

function activateCenterBarracks(lane) {
  const centerState = lane && lane.barracksSiteStates
    ? lane.barracksSiteStates.center
    : null;
  assert.ok(centerState, "expected the lane to expose center barracks state");
  centerState.isBuilt = true;
  centerState.level = 1;
  centerState.hp = 260;
  centerState.maxHp = 260;
  centerState.lifecycleState = "active";
  centerState.nextSendTick = 600;
  return centerState;
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

test("Town Core snapshot HP updates immediately after structure damage is applied", () => {
  const game = createGame(12);
  const lane = game.lanes[0];
  const target = simMl.getLaneTownCoreCombatTarget(lane);
  assert.ok(target, "expected the Town Core to expose a combat target");

  const attacker = createWaveUnit("raider", { atkCd: 0, baseDmg: 6 });
  const result = simMl.attackFortressPad(game, lane, attacker, target);
  assert.equal(result.damageApplied, 6, "expected Town Core damage to be applied through the structure combat path");

  const snapshot = simMl.createMLSnapshot(game);
  const snapshotLane = snapshot.lanes.find((entry) => entry && entry.laneIndex === lane.laneIndex);
  const snapshotCore = snapshotLane && snapshotLane.fortressPads
    ? snapshotLane.fortressPads.find((pad) => pad && pad.padId === target.padId)
    : null;

  assert.ok(snapshotCore, "expected the Town Core pad to be serialized into the snapshot");
  assert.equal(snapshotCore.hp, 6, "expected the Town Core snapshot HP to match the live damaged state");
  assert.equal(snapshotCore.maxHp, 12, "expected the Town Core snapshot max HP to stay intact");
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

test("defenders near the town core do not pull wave units into combat from mid-lane", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const coreApproach = getCoreApproachPosition(lane, 1);
  const farRoutePoint = getCoreApproachPosition(lane, 18);
  const defender = createDefender("guardian", {
    id: "midlane_aggro_guard",
    posX: coreApproach.posX - 0.8,
    posY: coreApproach.posY - 0.8,
    pathIdx: coreApproach.pathIdx - 0.8,
    guardAnchorX: coreApproach.posX - 0.8,
    guardAnchorY: coreApproach.posY - 0.8,
    homeTx: 4,
    homeTy: 24,
  });
  const attacker = createWaveUnit("raider", {
    id: "midlane_aggro_wave",
    posX: farRoutePoint.posX,
    posY: farRoutePoint.posY,
    pathIdx: farRoutePoint.pathIdx,
    routeType: simMl.ROUTE_TYPES.WAVE_LANE,
    routeStartNode: "WA",
    routeTargetNode: "A",
    routeSegments: ["WA_A"],
    routeSegmentIndex: 0,
    segmentProgress: 0.25,
    currentSegment: "WA_A",
    routeWorldX: farRoutePoint.posX,
    routeWorldY: farRoutePoint.posY,
  });

  lane.units.push(defender, attacker);

  tick(game, 1);

  assert.equal(attacker.combatTarget, null, "mid-lane waves should not acquire a fortress defender from the full-lane leash");
  assert.notEqual(attacker.combatState, "COMBAT");
});

test("wave units resume from their live combat position instead of snapping back to stale route progress", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  for (const pad of lane.fortressPads || []) {
    if (!pad)
      continue;
    pad.hp = 0;
    pad.maxHp = 0;
    pad.tier = 0;
  }

  const coreApproach = getCoreApproachPosition(lane, 2);
  const farRoutePoint = getCoreApproachPosition(lane, 18);
  const defender = createDefender("guardian", {
    id: "resume_anchor_guard",
    hp: 1,
    maxHp: 1,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: coreApproach.posX - 0.5,
    posY: coreApproach.posY - 0.5,
    pathIdx: coreApproach.pathIdx - 0.5,
    guardAnchorX: coreApproach.posX - 0.5,
    guardAnchorY: coreApproach.posY - 0.5,
    homeTx: 4,
    homeTy: 24,
  });
  const attacker = createWaveUnit("raider", {
    id: "resume_anchor_wave",
    hp: 24,
    maxHp: 24,
    baseDmg: 4,
    atkCd: 0,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: farRoutePoint.pathIdx,
    routeType: simMl.ROUTE_TYPES.WAVE_LANE,
    routeStartNode: "WA",
    routeTargetNode: "A",
    routeSegments: ["WA_A"],
    routeSegmentIndex: 0,
    segmentProgress: 0.25,
    currentSegment: "WA_A",
    routeWorldX: farRoutePoint.posX,
    routeWorldY: farRoutePoint.posY,
  });

  lane.units.push(defender, attacker);

  tick(game, 1);

  assert.equal(lane.units.some((unit) => unit.id === defender.id), false, "the low-health defender should die on first contact");
  const postCombatPosition = { x: attacker.posX, y: attacker.posY };

  tick(game, 1);

  const travelDistance = Math.sqrt(
    Math.pow(attacker.posX - postCombatPosition.x, 2) +
    Math.pow(attacker.posY - postCombatPosition.y, 2)
  );

  assert.ok(travelDistance < 2, "route resumption should continue from the live combat position instead of jumping back to stale path progress");
  assert.equal(attacker.currentSegment, "WA_A");
});

test("defenders can kill attackers before the Town Core is destroyed", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const corePad = getTownCorePad(lane);
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.6, -0.8);
  const attackerPoint = getDefendAnchorPosition(lane, 0.1, 0);
  const attacker = createWaveUnit("raider", {
    hp: 12,
    maxHp: 12,
    baseDmg: 4,
    atkCd: 0,
    posX: attackerPoint.posX,
    posY: attackerPoint.posY,
  });
  const defender = createDefender("guardian", {
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
    guardAnchorX: defenderPoint.posX,
    guardAnchorY: defenderPoint.posY,
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

test("DEFEND lane commands let barracks units intercept dungeon waves without using the legacy defender flag", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const corePad = getTownCorePad(lane);
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.5, 0);
  const attackerPoint = getDefendAnchorPosition(lane, 0.15, 0);
  const defender = createDefender("guardian", {
    id: "stance_guard_test",
    isDefender: false,
    sourceTeam: "red",
    sourceBarracksId: "center",
    sourceBarracksKey: "center",
    stance: null,
    pathContractType: null,
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
    guardAnchorX: defenderPoint.posX,
    guardAnchorY: defenderPoint.posY,
    homeTx: 4,
    homeTy: 24,
  });
  const attacker = createWaveUnit("raider", {
    id: "stance_guard_wave",
    hp: 24,
    maxHp: 24,
    baseDmg: 4,
    atkCd: 0,
    posX: attackerPoint.posX,
    posY: attackerPoint.posY,
  });

  lane.units.push(defender, attacker);

  tick(game, 1);

  const serverDefender = lane.units.find((unit) => unit.id === defender.id);
  const serverAttacker = lane.units.find((unit) => unit.id === attacker.id);

  assert.ok(serverDefender, "the DEFEND stance barracks unit should remain alive during interception");
  assert.ok(serverAttacker, "the wave unit should still be alive during initial interception");
  assert.equal(serverDefender.isDefender, false, "lane-command defenders should stay as barracks units instead of becoming legacy defender entities");
  assert.equal(serverDefender.stance, "DEFEND", "defend-mode barracks units should publish DEFEND stance");
  assert.equal(serverDefender.pathContractType, "intercept", "engaged DEFEND units should publish the intercept path contract");
  assert.equal(serverDefender.combatTarget?.unitId, attacker.id, "the DEFEND stance unit should acquire the incoming wave");
  assert.equal(serverAttacker.combatTarget?.unitId, defender.id, "the wave unit should retaliate against the intercepting defender");
  assert.equal(corePad.hp, 20, "the Town Core should stay untouched while the DEFEND stance unit owns interception");
});

test("wave units already in defender contact range do not backpedal to a slot before attacking", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.4, 0);
  const attackerPoint = getDefendAnchorPosition(lane, -0.95, 0);
  const defender = createDefender("guardian", {
    hp: 80,
    maxHp: 80,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
    guardAnchorX: defenderPoint.posX,
    guardAnchorY: defenderPoint.posY,
    homeTx: 4,
    homeTy: 24,
  });
  const attacker = createWaveUnit("raider", {
    hp: 24,
    maxHp: 24,
    baseDmg: 4,
    atkCd: 0,
    posX: attackerPoint.posX,
    posY: attackerPoint.posY,
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

test("wave units intercepted near the front gate keep the Town Core safe while defenders live", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const corePad = getTownCorePad(lane);
  activateCenterBarracks(lane);
  primeDefendAnchor(game, lane);
  const defenderPoint = getDefendAnchorPosition(lane, -0.4, 0);
  const leftWavePoint = getDefendAnchorPosition(lane, 0.1, -0.2);
  const rightWavePoint = getDefendAnchorPosition(lane, 0.1, 0.2);
  const defender = createDefender("guardian", {
    hp: 18,
    maxHp: 18,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: defenderPoint.posX,
    posY: defenderPoint.posY,
    guardAnchorX: defenderPoint.posX,
    guardAnchorY: defenderPoint.posY,
    homeTx: 4,
    homeTy: 24,
    defState: "merge_guard",
  });
  const attackers = [
    createWaveUnit("raider", {
      baseDmg: 3,
      atkCd: 0,
      atkCdTicks: 4,
      posX: leftWavePoint.posX,
      posY: leftWavePoint.posY,
      routeType: simMl.ROUTE_TYPES.WAVE_LANE,
      routeStartNode: "WA",
      routeTargetNode: "A",
      routeSegments: ["WA_A"],
      routeSegmentIndex: 0,
      segmentProgress: 0.5,
      currentSegment: "WA_A",
      routeWorldX: leftWavePoint.posX,
      routeWorldY: leftWavePoint.posY,
      spawnIndex: 0,
    }),
    createWaveUnit("raider", {
      baseDmg: 3,
      atkCd: 0,
      atkCdTicks: 4,
      posX: rightWavePoint.posX,
      posY: rightWavePoint.posY,
      routeType: simMl.ROUTE_TYPES.WAVE_LANE,
      routeStartNode: "WA",
      routeTargetNode: "A",
      routeSegments: ["WA_A"],
      routeSegmentIndex: 0,
      segmentProgress: 0.5,
      currentSegment: "WA_A",
      routeWorldX: rightWavePoint.posX,
      routeWorldY: rightWavePoint.posY,
      spawnIndex: 1,
    }),
  ];

  lane.units.push(defender, ...attackers);

  tick(game, 1);

  const centerBarracksState = lane.barracksSiteStates && lane.barracksSiteStates.center;
  assert.ok(centerBarracksState && centerBarracksState.isBuilt, "the active center barracks should be treated as a live fortress structure");
  assert.equal(corePad.hp, 20, "the Town Core should not take damage while a merge-guard defender is intercepting");
  assert.equal(centerBarracksState.hp, centerBarracksState.maxHp, "the center barracks should stay untouched during the interception");
  for (const attacker of attackers) {
    const serverAttacker = lane.units.find((unit) => unit.id === attacker.id);
    assert.ok(serverAttacker, "the attacker should still be alive during the initial interception");
    assert.equal(serverAttacker.combatTarget?.kind, "unit", "intercepted attackers should target the defender, not the Town Core");
    assert.equal(serverAttacker.combatTarget?.unitId, defender.id, "the defender should own aggro while alive");
    assert.equal(serverAttacker.combatState, "COMBAT", "intercepted attackers should enter COMBAT state");
  }

  tick(game, 6);

  assert.equal(corePad.hp, 20, "the Town Core should remain untouched while the defender is still alive");
  assert.equal((lane.barracksSiteStates && lane.barracksSiteStates.center).hp, centerBarracksState.maxHp, "the barracks should remain untouched while the defender is still alive");
  const livingDefender = lane.units.find((unit) => unit.id === defender.id);
  assert.ok(livingDefender, "the blocker should still be present during the interception window");
});

test("wave units near the fortress interior clear every defender in range before locking onto the Town Core", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const corePad = getTownCorePad(lane);
  const coreApproach = getCoreApproachPosition(lane, 1);
  const firstDefender = createDefender("guardian", {
    id: "core_defender_a",
    hp: 1,
    maxHp: 1,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: coreApproach.posX - 0.4,
    posY: coreApproach.posY - 0.3,
    pathIdx: coreApproach.pathIdx - 0.3,
    guardAnchorX: coreApproach.posX - 0.4,
    guardAnchorY: coreApproach.posY - 0.3,
  });
  const secondDefender = createDefender("guardian", {
    id: "core_defender_b",
    hp: 40,
    maxHp: 40,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: coreApproach.posX + 0.45,
    posY: coreApproach.posY - 0.55,
    pathIdx: coreApproach.pathIdx - 0.55,
    guardAnchorX: coreApproach.posX + 0.45,
    guardAnchorY: coreApproach.posY - 0.55,
  });
  const attacker = createWaveUnit("raider", {
    id: "core_clearance_wave",
    hp: 36,
    maxHp: 36,
    baseDmg: 4,
    atkCd: 0,
    atkCdTicks: 4,
    posX: coreApproach.posX,
    posY: coreApproach.posY,
    pathIdx: coreApproach.pathIdx,
  });

  lane.units.push(firstDefender, secondDefender, attacker);

  tick(game, 1);

  let serverAttacker = lane.units.find((unit) => unit.id === attacker.id);
  assert.ok(serverAttacker, "the attacker should still be alive during the first defender pickup");
  assert.equal(lane.units.some((unit) => unit.id === firstDefender.id), false, "the first low-health defender should die on the opening contact");
  assert.equal(corePad.hp, 20, "the Town Core should stay untouched while defenders remain in engagement range");

  tick(game, 1);

  serverAttacker = lane.units.find((unit) => unit.id === attacker.id);
  assert.ok(serverAttacker, "the attacker should stay alive after the first defender falls");
  assert.equal(serverAttacker.combatTarget?.unitId, secondDefender.id, "the wave should reacquire the remaining defender before the Town Core");
  assert.equal(corePad.hp, 20, "the Town Core should still remain untouched while another defender is still alive nearby");
});

test("wave units can target the center barracks instead of skipping straight to the Town Core", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  activateCenterBarracks(lane);

  const coreTarget = simMl.getLaneTownCoreCombatTarget(lane);
  const centerBarracksTarget = simMl.getBarracksSiteCombatTarget(lane, "center");
  assert.ok(coreTarget, "expected the Town Core to expose a combat target");
  assert.ok(centerBarracksTarget, "expected the center barracks to expose a combat target");

  const attacker = createWaveUnit("raider", {
    id: "barracks_priority_wave",
    atkCd: 0,
    posX: Number(centerBarracksTarget.posX),
    posY: Number(centerBarracksTarget.posY) + 0.6,
    pathIdx: Number(centerBarracksTarget.posY) + 0.6,
    routeWorldX: Number(centerBarracksTarget.posX),
    routeWorldY: Number(centerBarracksTarget.posY) + 0.6,
  });

  lane.units.push(attacker);

  tick(game, 1);

  assert.equal(attacker.combatTarget?.kind, "fortress_pad", "the wave should acquire a fortress structure target");
  assert.equal(attacker.combatTarget?.padId, centerBarracksTarget.padId, "the wave should be allowed to lock onto the center barracks instead of skipping to the Town Core");
});

test("wave units retarget from a prelocked Town Core to a nearer center barracks", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  activateCenterBarracks(lane);

  const coreTarget = simMl.getLaneTownCoreCombatTarget(lane);
  const centerBarracksTarget = simMl.getBarracksSiteCombatTarget(lane, "center");
  assert.ok(coreTarget, "expected the Town Core to expose a combat target");
  assert.ok(centerBarracksTarget, "expected the center barracks to expose a combat target");

  const attacker = createWaveUnit("raider", {
    id: "barracks_retarget_wave",
    atkCd: 0,
    posX: Number(centerBarracksTarget.posX),
    posY: Number(centerBarracksTarget.posY) + 0.6,
    pathIdx: Number(centerBarracksTarget.posY) + 0.6,
    routeWorldX: Number(centerBarracksTarget.posX),
    routeWorldY: Number(centerBarracksTarget.posY) + 0.6,
    combatTarget: {
      unitId: coreTarget.unitId,
      kind: "fortress_pad",
      padId: coreTarget.padId,
      laneIndex: lane.laneIndex,
    },
  });

  lane.units.push(attacker);

  tick(game, 1);

  assert.equal(attacker.combatTarget?.kind, "fortress_pad", "the wave should keep a fortress target after reevaluating");
  assert.equal(attacker.combatTarget?.padId, centerBarracksTarget.padId, "the wave should switch off the Town Core and onto the nearer center barracks");
});

test("wave units near the Town Core must retarget to fortress-interior defenders before attacking structures", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const coreTarget = simMl.getLaneTownCoreCombatTarget(lane);
  assert.ok(coreTarget, "expected the Town Core to expose a combat target");

  const defender = createDefender("guardian", {
    id: "fortress_interior_blocker",
    hp: 80,
    maxHp: 80,
    baseDmg: 0,
    atkCd: 999,
    atkCdTicks: 999,
    posX: Number(coreTarget.posX),
    posY: Number(coreTarget.posY) - 5.1,
    pathIdx: Number(coreTarget.posY) - 5.1,
    guardAnchorX: Number(coreTarget.posX),
    guardAnchorY: Number(coreTarget.posY) - 5.1,
  });
  const attacker = createWaveUnit("raider", {
    id: "fortress_interior_wave",
    atkCd: 0,
    posX: Number(coreTarget.posX),
    posY: Number(coreTarget.posY) - 0.45,
    pathIdx: Number(coreTarget.posY) - 0.45,
    routeWorldX: Number(coreTarget.posX),
    routeWorldY: Number(coreTarget.posY) - 0.45,
    combatTarget: {
      unitId: coreTarget.unitId,
      kind: "fortress_pad",
      padId: coreTarget.padId,
      laneIndex: lane.laneIndex,
    },
  });

  lane.units.push(defender, attacker);

  tick(game, 1);

  assert.equal(attacker.combatTarget?.kind, "unit", "the wave should drop the Town Core lock when a live defender still guards the fortress interior");
  assert.equal(attacker.combatTarget?.unitId, defender.id, "the wave should pick the fortress-interior defender before any structure");
  assert.equal(coreTarget.hp, 20, "the Town Core should stay untouched while the interior defender is still alive");
});

test("retreating units only re-engage after each unit reaches the Town Core defense zone", () => {
  const game = createGame(20);
  const homeLane = game.lanes[0];
  const enemyLane = game.lanes[1];
  const townCoreTarget = simMl.getLaneTownCoreCombatTarget(homeLane);
  assert.ok(townCoreTarget, "expected the home lane to expose a Town Core combat target");

  issueLaneCommand(game, homeLane.laneIndex, "set_lane_retreat");
  tick(game, 1);

  const coreDefender = createDefender("guardian", {
    id: "retreat_home_ready",
    posX: Number(townCoreTarget.posX) + 0.2,
    posY: Number(townCoreTarget.posY) + 0.2,
    pathIdx: 0,
  });
  const farDefender = createDefender("guardian", {
    id: "retreat_far_away",
    posX: Number(townCoreTarget.posX),
    posY: Number(townCoreTarget.posY) + 18,
    pathIdx: 18,
  });
  const intruder = createWaveUnit("raider", {
    id: "home_intruder",
    hp: 200,
    maxHp: 200,
    baseDmg: 0,
    posX: Number(townCoreTarget.posX) + 0.6,
    posY: Number(townCoreTarget.posY) + 0.4,
    pathIdx: 1,
    atkCd: 0,
    atkCdTicks: 999,
  });

  homeLane.units.push(coreDefender, intruder);
  enemyLane.units.push(farDefender);

  tick(game, 1);

  const liveUnits = game.lanes.flatMap((lane) => Array.isArray(lane.units) ? lane.units : []);
  const liveCoreDefender = liveUnits.find((unit) => unit && unit.id === coreDefender.id);
  const liveFarDefender = liveUnits.find((unit) => unit && unit.id === farDefender.id);
  const liveIntruder = liveUnits.find((unit) => unit && unit.id === intruder.id);

  assert.ok(liveCoreDefender, "expected the home-ready retreat unit to stay alive in the simulation");
  assert.ok(liveFarDefender, "expected the far retreat unit to stay alive in the simulation");
  assert.ok(liveIntruder, "expected the intruder to stay alive long enough for target acquisition");
  assert.equal(liveCoreDefender.combatTarget?.unitId, liveIntruder.id, "expected the unit that reached the Town Core to start defending immediately");
  assert.equal(liveCoreDefender.canEngage, true, "expected the Town Core defender to regain combat permission");
  assert.equal(liveCoreDefender.stance, "DEFEND", "expected the recovered retreat unit to present as defending the Town Core");
  assert.equal(liveFarDefender.commandState, "RETREAT", "expected far-away units to remain in retreat");
  assert.equal(liveFarDefender.combatTarget, null, "expected units still traveling home to remain non-engaging");
  assert.equal(liveFarDefender.canEngage, false, "expected units still far from home to keep combat disabled");
});


