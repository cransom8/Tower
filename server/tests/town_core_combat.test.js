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
    groupId: overrides.groupId ?? `test_packet:${ownerLaneIndex}:${sourceBarracksId}:${targetLaneIndex}`,
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

test("lane snapshots keep barracks sends as separate packets with explicit stance anchors", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const coreApproach = getCoreApproachPosition(lane, 2);
  const townCoreTarget = simMl.getLaneTownCoreCombatTarget(lane);
  const frontGatePadDef = simMl.FORTRESS_PAD_DEFS.find((pad) => pad && pad.padId === "gate_front_pad");
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });

  const leftPacket = createDefender("guardian", {
    id: "packet_left_guard",
    sourceBarracksId: "left",
    sourceBarracksKey: "left",
    posX: coreApproach.posX - 1.4,
    posY: coreApproach.posY - 1.4,
    pathIdx: coreApproach.pathIdx - 1.4,
    guardAnchorX: coreApproach.posX - 1.4,
    guardAnchorY: coreApproach.posY - 1.4,
    homeTx: 4,
    homeTy: 24,
  });
  const rightPacket = createDefender("guardian", {
    id: "packet_right_guard",
    sourceBarracksId: "right",
    sourceBarracksKey: "right",
    posX: coreApproach.posX + 1.4,
    posY: coreApproach.posY - 1.4,
    pathIdx: coreApproach.pathIdx - 1.4,
    guardAnchorX: coreApproach.posX + 1.4,
    guardAnchorY: coreApproach.posY - 1.4,
    homeTx: 4,
    homeTy: 24,
  });

  lane.units.push(leftPacket, rightPacket);
  tick(game, 1);

  const snapshot = simMl.createMLSnapshot(game);
  const laneSnap = snapshot.lanes.find((entry) => entry && entry.laneIndex === lane.laneIndex);
  const leftSnap = laneSnap.units.find((entry) => entry && entry.id === leftPacket.id);
  const rightSnap = laneSnap.units.find((entry) => entry && entry.id === rightPacket.id);

  assert.ok(laneSnap.insideGateAnchor, "lane snapshots should expose the retreat anchor");
  assert.ok(laneSnap.outsideGateAnchor, "lane snapshots should expose the defend anchor");
  assert.ok(laneSnap.enemyCoreAnchor, "lane snapshots should expose the attack anchor");
  const retreatAnchorDistance = Math.hypot(
    laneSnap.insideGateAnchor.x - townCoreTarget.posX,
    laneSnap.insideGateAnchor.y - townCoreTarget.posY
  );
  const defendAnchorDistance = Math.hypot(
    laneSnap.outsideGateAnchor.x - townCoreTarget.posX,
    laneSnap.outsideGateAnchor.y - townCoreTarget.posY
  );
  assert.ok(
    defendAnchorDistance > retreatAnchorDistance,
    "the defend anchor should sit in front of the gate, farther from the Town Core than the retreat anchor"
  );
  assert.ok(
    defendAnchorDistance >= Math.max(7.5, Number(frontGatePadDef?.combatOffsetY) - 1.5),
    "the defend anchor should stage close to the fortress front gate instead of clustering around the Town Core"
  );
  assert.equal(laneSnap.packets.length, 2, "separate barracks sends should remain separate packet snapshots");
  assert.ok(leftSnap && rightSnap, "packet units should remain present in the lane snapshot");
  assert.notEqual(leftSnap.groupId, rightSnap.groupId, "different barracks sends should not collapse into one shared group");
  assert.equal(leftSnap.currentWaypointTargetKind, "outsideGateAnchor");
  assert.equal(rightSnap.currentWaypointTargetKind, "outsideGateAnchor");
  assert.ok(
    Math.abs(leftSnap.groupCenterX - rightSnap.groupCenterX) > 0.1
      || Math.abs(leftSnap.groupCenterY - rightSnap.groupCenterY) > 0.1,
    "packet group centers should stay distinct instead of collapsing into one lane-wide anchor"
  );
  assert.deepEqual(
    laneSnap.packets.map((packet) => packet.groupId).sort(),
    [leftSnap.groupId, rightSnap.groupId].sort(),
    "lane packet snapshots should track the spawned packet ids"
  );
});

test("defend staging pushes later packets forward from the gate instead of back toward the core", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const coreApproach = getCoreApproachPosition(lane, 2);
  const townCoreTarget = simMl.getLaneTownCoreCombatTarget(lane);
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });

  lane.units.push(
    createDefender("guardian", {
      id: "left_packet_a",
      groupId: "packet:0:left:a",
      sourceBarracksId: "left",
      sourceBarracksKey: "left",
      posX: coreApproach.posX - 1.6,
      posY: coreApproach.posY - 1.2,
      pathIdx: coreApproach.pathIdx - 1.2,
    }),
    createDefender("guardian", {
      id: "left_packet_b",
      groupId: "packet:0:left:b",
      sourceBarracksId: "left",
      sourceBarracksKey: "left",
      posX: coreApproach.posX - 1.0,
      posY: coreApproach.posY - 1.8,
      pathIdx: coreApproach.pathIdx - 1.8,
    }),
    createDefender("guardian", {
      id: "right_packet_a",
      groupId: "packet:0:right:a",
      sourceBarracksId: "right",
      sourceBarracksKey: "right",
      posX: coreApproach.posX + 1.6,
      posY: coreApproach.posY - 1.2,
      pathIdx: coreApproach.pathIdx - 1.2,
    })
  );

  tick(game, 1);

  const snapshot = simMl.createMLSnapshot(game);
  const laneSnap = snapshot.lanes.find((entry) => entry && entry.laneIndex === lane.laneIndex);
  assert.ok(laneSnap?.outsideGateAnchor, "lane snapshots should expose the defend anchor for packet staging");

  const outsideAnchor = laneSnap.outsideGateAnchor;
  const forwardDx = outsideAnchor.x - townCoreTarget.posX;
  const forwardDy = outsideAnchor.y - townCoreTarget.posY;
  const forwardDistance = Math.hypot(forwardDx, forwardDy) || 1;
  const forwardX = forwardDx / forwardDistance;
  const forwardY = forwardDy / forwardDistance;
  const packetById = new Map((laneSnap.packets || []).map((packet) => [packet.groupId, packet]));
  const leftA = packetById.get("packet:0:left:a");
  const leftB = packetById.get("packet:0:left:b");
  const rightA = packetById.get("packet:0:right:a");
  assert.ok(leftA && leftB && rightA, "all defend packets should remain visible in the lane snapshot");

  const projectForward = (packet) => {
    const dx = packet.groupCenter.x - townCoreTarget.posX;
    const dy = packet.groupCenter.y - townCoreTarget.posY;
    return (dx * forwardX) + (dy * forwardY);
  };

  const leftAForward = projectForward(leftA);
  const leftBForward = projectForward(leftB);
  const rightAForward = projectForward(rightA);
  const anchorForward = projectForward({ groupCenter: outsideAnchor });

  assert.ok(
    Math.abs(rightAForward - anchorForward) <= 1.25,
    "a fresh packet from another barracks should still stage at the gate instead of being pushed deep behind older packets"
  );
  assert.ok(
    leftBForward > leftAForward && leftAForward >= anchorForward - 0.25,
    "repeat packets from the same barracks should stack outward from the gate instead of collapsing back toward the Town Core"
  );
});

test("large defend packets fill outward from the gate instead of spilling behind it", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  const coreApproach = getCoreApproachPosition(lane, 2);
  const townCoreTarget = simMl.getLaneTownCoreCombatTarget(lane);
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });

  for (let index = 0; index < 12; index += 1) {
    lane.units.push(createDefender("guardian", {
      id: `bulk_defend_${index}`,
      groupId: "packet:0:center:bulk",
      sourceBarracksId: "center",
      sourceBarracksKey: "center",
      posX: coreApproach.posX + ((index % 3) - 1) * 0.8,
      posY: coreApproach.posY - (index * 0.18),
      pathIdx: coreApproach.pathIdx - (index * 0.2),
    }));
  }

  tick(game, 1);

  const snapshot = simMl.createMLSnapshot(game);
  const laneSnap = snapshot.lanes.find((entry) => entry && entry.laneIndex === lane.laneIndex);
  assert.ok(laneSnap?.outsideGateAnchor, "lane snapshots should expose the defend anchor for packet slot staging");

  const outsideAnchor = laneSnap.outsideGateAnchor;
  const forwardDx = outsideAnchor.x - townCoreTarget.posX;
  const forwardDy = outsideAnchor.y - townCoreTarget.posY;
  const forwardDistance = Math.hypot(forwardDx, forwardDy) || 1;
  const forwardX = forwardDx / forwardDistance;
  const forwardY = forwardDy / forwardDistance;
  const anchorForward = (forwardDx * forwardX) + (forwardDy * forwardY);
  const stagedUnits = lane.units.filter((unit) => unit.groupId === "packet:0:center:bulk");
  assert.equal(stagedUnits.length, 12, "the large defend packet should remain intact after staging");

  const stagedForwardValues = stagedUnits.map((unit) => {
    const dx = Number(unit.anchorTargetX) - townCoreTarget.posX;
    const dy = Number(unit.anchorTargetY) - townCoreTarget.posY;
    return (dx * forwardX) + (dy * forwardY);
  });
  const minForward = Math.min(...stagedForwardValues);
  const maxForward = Math.max(...stagedForwardValues);

  assert.ok(
    minForward >= anchorForward - 0.25,
    "the rearmost slot of a defend packet should stay at the gate anchor instead of slipping back behind it"
  );
  assert.ok(
    maxForward >= anchorForward + 2.0,
    "as a defend packet grows, additional rows should extend outward into the lane instead of piling back toward the fortress interior"
  );
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

test("wave units intercepted near the front gate do not damage the Town Core until defenders fall", () => {
  const game = createGame(20);
  const lane = game.lanes[0];
  issueLaneCommand(game, lane.laneIndex, "set_lane_defend_point", { progress: 0 });
  const corePad = getTownCorePad(lane);
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
  for (let i = 0; i < 40; i += 1) {
    tick(game, 1);
    if (corePad.hp < 20) {
      damageApplied = true;
      break;
    }
  }

  assert.equal(damageApplied, true, "surviving attackers should only damage the Town Core after the defender dies");
  assert.equal(lane.eliminated, false, "the match should not end early during defender interception");
});
