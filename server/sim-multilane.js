// server/sim-multilane.js
"use strict";

/**
 * Fortress-survival PvP simulation.
 * Center-spawned waves route into each lane's Town Core while barracks units
 * sabotage opponents over shared battlefield routes.
 * Authored fortress pads, walls, gates, and turrets remain part of the active
 * game, but retired classic tile-grid actions stay blocked here on purpose so
 * older clients, AI code, and room flows cannot quietly reactivate them.
 *
 * Wired to sim-core (computeDamage, fireProjectile, resolveProjectile, mulberry32 RNG)
 * and the unitTypes DB cache. UNIT_DEFS / TOWER_DEFS serve as last-resort fallbacks
 * for units not present in the DB (e.g. during local dev without migrations).
 * Match-start economy and wave settings are expected to come from the active
 * multilane config and should fail loudly when missing or invalid.
 */

const {
  computeDamage: coreComputeDamage,
  fireProjectile,
  resolveProjectile,
  resolveAbilityHook,
  applyAuras,
  resolveStatuses,
  applySeparation,
  mulberry32,
} = require("./sim-core");
const gameConfig = require("./gameConfig");
const { getUnitType, getAllUnitTypes } = require("./unitTypes");
const combatLog = require("./combatLog");
const log = require("./logger");
const {
  DEFAULT_FORT_PRESENTATION_KEY,
  resolveFortCatalogUnitKey,
  resolveFortDisplayName,
  resolveFortPortraitKey,
  resolveFortSkinKey,
  isFortArchetypeKey,
} = require("./game/fortUnitCatalog");
const {
  getCurrentBarracksMult,
  getBarracksUpgradeDef,
  getMaxBarracksLevel,
} = require("./barracksLevels");
const {
  createMLSnapshot: buildMLSnapshot,
  createMLPublicConfig: buildMLPublicConfig,
} = require("./sim-multilane-serialization");

const TICK_HZ = 20;
const TICK_MS = Math.floor(1000 / TICK_HZ);
const INCOME_INTERVAL_TICKS = 240; // 12 s
const TOWER_MAX_LEVEL = 10;
const MAX_UNITS_PER_LANE = 200;
const GATE_KILL_BOUNTY = 10;
const ENABLE_WAVE_UNIT_TRACE = process.env.ENABLE_WAVE_UNIT_TRACE === "1";
const WAVE_UNIT_STATES = Object.freeze({
  IDLE: "IDLE",
  MOVING: "MOVING",
  COMBAT: "COMBAT",
  DEAD: "DEAD",
});

// Grid constants
const GRID_W = 11;
const GRID_H = 28;
const SPAWN_X = 5;
const SPAWN_YG = 0;
const CASTLE_X = 5;
const CASTLE_YG = 27;
const MAX_PATH_LEN = GRID_W * GRID_H; // kept for reference; path is always GRID_H steps
const SPLASH_RADIUS_TILES = 1.5;
const MIN_UNIT_SPACING = 0.8;      // minimum pathIdx gap enforced by applySeparation

// Combat movement tuning
// All combat movers share a fixed server-side base path speed so the battlefield
// reads consistently. Barracks progression then scales that baseline up in
// 0.25x steps, starting at half-speed until the player invests in upgrades.
const BASE_COMBAT_PATH_SPEED = 0.25;
const BARRACKS_LEVEL_ONE_SPEED_MULT = 0.50;
const SPEED_UPGRADE_STEP = 0.25;

// Mobile defender constants
const DEFENDER_BASE_SPEED   = BASE_COMBAT_PATH_SPEED * BARRACKS_LEVEL_ONE_SPEED_MULT;
const ENGAGEMENT_RANGE_PADDING = 2.0; // extra leash beyond attack range before a unit opens fire
const SEP_DAMP              = 0.35;
const SEP_MAX_PUSH          = 0.16;
const CONTACT_SLOT_TOLERANCE = 0.10;
const DISABLE_RIGID_PACKET_FORMATIONS = true;

// Wave defense constants (Forge Wars Wave Rework Phase 1)
const TEAM_HP_START = 20;
const BARRACKS_SEND_TIMER_TICKS = 30 * TICK_HZ;
const WAVE_TIMER_TICKS = 120 * TICK_HZ;
const BUILD_PHASE_TICKS = 600;       // 30 s at 20 Hz
const TRANSITION_PHASE_TICKS = 200;  // 10 s at 20 Hz
const ESCALATION_PER_EXTRA_ROUND = 0.10; // +10% HP/DMG per round beyond last wave

function getMlRuntimeSettings() {
  const cfg = gameConfig.getRequiredActiveConfig("multilane");
  const gp = cfg.globalParams;
  return {
    startGold: gp.startGold,
    startIncome: gp.startIncome,
    livesStart: gp.livesStart,
    teamHpStart: gp.teamHpStart,
    buildPhaseTicks: gp.buildPhaseTicks,
    transitionPhaseTicks: gp.transitionPhaseTicks,
  };
}

// Shared path suffix appended after each branch's BFS path.
// Represents the outer shared bridge leading to the enemy base:
//   Left side  (lanes 0,1): Bridge_A → Dwarf Base
//   Right side (lanes 2,3): Bridge_F → Goblin Base
// Both lanes on the same side share this bridge. Towers cannot target units here
// (virtual coordinates are outside the private build grid).
const SHARED_SUFFIX_LENGTH = 28; // matches private branch grid length (GRID_H)

function buildFullPath(branchPath) {
  if (!branchPath || branchPath.length === 0) return [];
  const last = branchPath[branchPath.length - 1];
  const suffix = [];
  for (let i = 1; i <= SHARED_SUFFIX_LENGTH; i++) {
    suffix.push({ x: last.x, y: last.y + i });
  }
  return branchPath.concat(suffix);
}

const FIXED_SLOT_LAYOUT = [
  { laneIndex: 0, slotKey: "left_a", side: "left",  slotColor: "red",   branchId: "left_branch_a",  branchLabel: "Red Branch",   castleSide: "right" },
  { laneIndex: 1, slotKey: "left_b", side: "left",  slotColor: "yellow", branchId: "left_branch_b",  branchLabel: "Yellow Branch", castleSide: "right" },
  { laneIndex: 2, slotKey: "right_a", side: "right", slotColor: "blue",  branchId: "right_branch_a", branchLabel: "Blue Branch",  castleSide: "left" },
  { laneIndex: 3, slotKey: "right_b", side: "right", slotColor: "green", branchId: "right_branch_b", branchLabel: "Green Branch", castleSide: "left" },
];

const BATTLEFIELD_TOPOLOGY = Object.freeze({
  mapType: "lava_lake_funnel",
  centerIslandId: "center_spawn_island",
  sideOrder: ["left", "right"],
  castles: [
    { side: "left", castleId: "left_castle", bridgeId: "left_castle_bridge" },
    { side: "right", castleId: "right_castle", bridgeId: "right_castle_bridge" },
  ],
  mergeZones: [
    { side: "left", landmassId: "left_merge_landmass", bridgeId: "left_castle_bridge" },
    { side: "right", landmassId: "right_merge_landmass", bridgeId: "right_castle_bridge" },
  ],
  buildZones: [
    { branchId: "left_branch_a", ownerLaneIndex: 0, buildable: true },
    { branchId: "left_branch_b", ownerLaneIndex: 1, buildable: true },
    { branchId: "right_branch_a", ownerLaneIndex: 2, buildable: true },
    { branchId: "right_branch_b", ownerLaneIndex: 3, buildable: true },
  ],
  sharedZonesBuildable: false,
});

// Battlefield Routes are authoritative movement state for all live mobile units.
// OUTER_LOOP   -> perimeter route chaining town cores
// CENTER_CROSS -> center bridge route between opposing cores
// WAVE_LANE    -> lane-specific wave spawn routes from battlefield edge to town core
const ROUTE_TYPES = Object.freeze({
  OUTER_LOOP: "OUTER_LOOP",
  CENTER_CROSS: "CENTER_CROSS",
  WAVE_LANE: "WAVE_LANE",
});

// Keep SCHEDULED_WAVE as a compatibility alias while older callers are archived.
const SPAWN_SOURCE_TYPES = Object.freeze({
  DUNGEON_WAVE: "dungeon_wave",
  SCHEDULED_WAVE: "dungeon_wave",
  BARRACKS_ROSTER: "barracks_roster",
  BARRACKS_HERO: "barracks_hero",
});

const ALLEGIANCE_KEYS = Object.freeze({
  RED: "red",
  YELLOW: "yellow",
  BLUE: "blue",
  GREEN: "green",
  DUNGEON: "dungeon",
});

const PLAYER_ALLEGIANCE_KEYS = new Set([
  ALLEGIANCE_KEYS.RED,
  ALLEGIANCE_KEYS.YELLOW,
  ALLEGIANCE_KEYS.BLUE,
  ALLEGIANCE_KEYS.GREEN,
]);

const PATH_CONTRACT_TYPES = Object.freeze({
  WAVE_LANE: "wave_lane",
  BARRACKS_CROSS: "barracks_cross",
  BARRACKS_LOOP: "barracks_loop",
  GUARD_ANCHOR: "guard_anchor",
  INTERCEPT: "intercept",
  RETREAT_ANCHOR: "retreat_anchor",
});

const UNIT_STANCES = Object.freeze({
  DEFEND: "DEFEND",
  ATTACK: "ATTACK",
  HOLD: "HOLD",
  RETREAT: "RETREAT",
  ADVANCE: "ATTACK",
});

const LANE_COMMAND_STATES = Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  HOLD: "DEFEND",
  RETREAT: "RETREAT",
  CALLBACK: "RETREAT",
  FORWARD: "ATTACK",
});

const UNIT_MOVEMENT_MODES = Object.freeze({
  LANE_TRAVEL: "LaneTravel",
  FORMATION_JOIN: "FormationJoin",
  COMBAT_ENGAGE: "CombatEngage",
  RETURN_TO_ANCHOR: "ReturnToAnchor",
});

const LANE_STANCE_ANCHOR_KINDS = Object.freeze({
  INSIDE_GATE: "insideGateAnchor",
  OUTSIDE_GATE: "outsideGateAnchor",
  ENEMY_CORE: "enemyCoreAnchor",
});

const UNIT_COMBAT_ROLES = Object.freeze({
  SHIELD: "shield",
  SWORD: "sword",
  SPEAR: "spear",
  ARCHER: "archer",
  MAGE: "mage",
  PRIEST: "priest",
});

const UNIT_PREFERRED_BANDS = Object.freeze({
  SHIELD_FRONT: "shield_front",
  SWORD_LINE: "sword_line",
  SPEAR_LINE: "spear_line",
  RANGED_REAR: "ranged_rear",
  PRIEST_REAR: "priest_rear",
});

const ROUTE_NODE_IDS = Object.freeze(["A", "B", "C", "D"]);
const WAVE_SPAWN_NODE_IDS = Object.freeze(["WA", "WB", "WC", "WD"]);
const LANE_NODE_IDS = Object.freeze(["A", "B", "C", "D"]);
const RouteMineNode = "M";
const OPPOSING_LANE_INDEX = Object.freeze([1, 0, 3, 2]);
const ROUTE_TRACE_MIN_DISTANCE_TO_CORE = 6.5;
const ROUTE_BLOCKING_CORRIDOR_RADIUS = 4.5;
const DEFENDER_ENGAGEMENT_RANGE = ROUTE_BLOCKING_CORRIDOR_RADIUS;
const ROUTE_BLOCKING_FORWARD_DOT = 0.1;
const DEFENDER_HOLD_LEASH = 7.5;
const ROUTE_FORMATION_ROW_SPACING = 1.15;
const ROUTE_FORMATION_COLUMN_SPACING = 1.15;
const ROUTE_TANGENT_SAMPLE_DELTA = 0.003;
const LANE_COMMAND_DEFENSE_RADIUS = 9.0;
const LANE_COMMAND_COMBAT_LEASH = 8.0;
const LANE_COMMAND_HOME_CONTAINER_PROGRESS = 0.18;
const LANE_FORMATION_APPROACH_PROGRESS_TOLERANCE = 0.025;
const LANE_FORMATION_JOIN_DISTANCE = 1.6;
const LANE_FORMATION_ARRIVAL_DEAD_ZONE = 0.2;
const LANE_FORMATION_MAX_COLUMNS = 5;
const LANE_FORMATION_SETTLED_SEPARATION_DISTANCE = 0.9;
const LANE_FORMATION_SETTLED_SEPARATION_SCALE = 0.25;
const LANE_FORMATION_MIXED_SEPARATION_SCALE = 0.6;
const LANE_FORMATION_SETTLED_MAX_LATERAL_OFFSET = 0.15;
const LANE_FORMATION_SETTLED_SLOT_SPACING_SLACK = 0.12;
const LANE_COMBAT_POCKET_RADIUS_PADDING = ROUTE_FORMATION_ROW_SPACING * 2.5;
const LANE_COMBAT_POCKET_RADIUS_SCALE = 0.6;
const LANE_COMBAT_TARGET_LOCK_TICKS = Math.max(1, Math.round(TICK_HZ * 0.5));
const LANE_COMBAT_REGROUP_TICKS = Math.max(1, Math.round(TICK_HZ * 0.35));
const LANE_COMBAT_SWITCH_DISTANCE_MARGIN = 0.75;
// Keep home-side packet anchors aligned with the fortress front-gate combat offset so
// DEFEND packets actually stage at the gate instead of collapsing around the Town Core.
const FRONT_GATE_COMBAT_OFFSET = 10.2;
const FORTRESS_INTERIOR_ASSAULT_RADIUS = FRONT_GATE_COMBAT_OFFSET + ROUTE_BLOCKING_CORRIDOR_RADIUS;
const FORTRESS_INTERIOR_STRUCTURE_TARGET_RADIUS = Math.max(ROUTE_BLOCKING_CORRIDOR_RADIUS, FRONT_GATE_COMBAT_OFFSET * 0.65);
const PACKET_INSIDE_GATE_OFFSET = FRONT_GATE_COMBAT_OFFSET - 2.0;
const PACKET_OUTSIDE_GATE_OFFSET = FRONT_GATE_COMBAT_OFFSET + 4.0;
const PACKET_INTER_PACKET_DEPTH = ROUTE_FORMATION_ROW_SPACING * 3.5;
const PACKET_BARRACKS_LATERAL_SPACING = ROUTE_FORMATION_COLUMN_SPACING * 2.25;
const PACKET_ROLE_COLUMN_SPACING = ROUTE_FORMATION_COLUMN_SPACING;
const PACKET_ROLE_ROW_SPACING = ROUTE_FORMATION_ROW_SPACING;
const PACKET_FORMATION_MAX_COLUMNS = 4;
const PACKET_SUPPORT_TARGET_RADIUS = 7.5;
const PACKET_HOME_STAGING_DEPTH = ROUTE_FORMATION_ROW_SPACING * 1.35;
const LEGACY_ACTION_REJECTION_REASONS = Object.freeze({
  spawn_unit: "Manual CMD sends were removed. Units must come from Barracks.",
  place_unit: "Tile-grid defender placement is disabled in fortress mode",
  upgrade_tower: "Tile-grid defender upgrades are disabled in fortress mode",
  bulk_upgrade_towers: "Tile-grid defender upgrades are disabled in fortress mode",
  set_tower_target: "Tile-grid defender targeting is disabled in fortress mode",
  upgrade_barracks: "Select Barracks Left, Center, or Right to upgrade that building.",
  sell_tower: "Tile-grid defender selling is disabled in fortress mode",
  set_autosend: "CMD autosend was removed. Units must come from Barracks.",
});

const ROUTE_GRAPH_CORE_NODE_POSITIONS = Object.freeze({
  M: Object.freeze({ x: 0, y: 0 }),
  A: Object.freeze({ x: -24, y: 24 }),
  B: Object.freeze({ x: 24, y: 24 }),
  C: Object.freeze({ x: 24, y: -24 }),
  D: Object.freeze({ x: -24, y: -24 }),
});

const LANE_COMBAT_AXES = Object.freeze([
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.A,
    lateral: Object.freeze({ x: 1, y: 0 }),
    forward: Object.freeze({ x: 0, y: -1 }),
  }),
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.B,
    lateral: Object.freeze({ x: -1, y: 0 }),
    forward: Object.freeze({ x: 0, y: -1 }),
  }),
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.C,
    lateral: Object.freeze({ x: -1, y: 0 }),
    forward: Object.freeze({ x: 0, y: 1 }),
  }),
  Object.freeze({
    core: ROUTE_GRAPH_CORE_NODE_POSITIONS.D,
    lateral: Object.freeze({ x: 1, y: 0 }),
    forward: Object.freeze({ x: 0, y: 1 }),
  }),
]);

const BARRACKS_SITE_COMBAT_OFFSETS = Object.freeze({
  center: Object.freeze({ x: 0, y: 2 }),
  left: Object.freeze({ x: -4, y: 2 }),
  right: Object.freeze({ x: 4, y: 2 }),
});

const BARRACKS_ROUTE_NODE_SUFFIXES = Object.freeze({
  center: "CTR",
  left: "LFT",
  right: "RGT",
});

const ROUTE_GRAPH_NODE_POSITIONS = Object.freeze(buildBarracksRouteGraphNodePositions());

// Route graph polylines are simulation-space Battlefield Route segments.
// Client runtime projects these segment ids into rendered board-space anchors.
const ROUTE_SEGMENT_POLYLINES = Object.freeze({
  A_B: Object.freeze([
    Object.freeze({ x: -24, y: 24 }),
    Object.freeze({ x: 0, y: 28 }),
    Object.freeze({ x: 24, y: 24 }),
  ]),
  B_C: Object.freeze([
    Object.freeze({ x: 24, y: 24 }),
    Object.freeze({ x: 28, y: 0 }),
    Object.freeze({ x: 24, y: -24 }),
  ]),
  C_D: Object.freeze([
    Object.freeze({ x: 24, y: -24 }),
    Object.freeze({ x: 0, y: -28 }),
    Object.freeze({ x: -24, y: -24 }),
  ]),
  D_A: Object.freeze([
    Object.freeze({ x: -24, y: -24 }),
    Object.freeze({ x: -28, y: 0 }),
    Object.freeze({ x: -24, y: 24 }),
  ]),
  A_C: Object.freeze([
    Object.freeze({ x: -24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: -24 }),
  ]),
  C_A: Object.freeze([
    Object.freeze({ x: 24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: 24 }),
  ]),
  B_D: Object.freeze([
    Object.freeze({ x: 24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: -24 }),
  ]),
  D_B: Object.freeze([
    Object.freeze({ x: -24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: 24 }),
  ]),
  A_M: Object.freeze([
    Object.freeze({ x: -24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_A: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: 24 }),
  ]),
  B_M: Object.freeze([
    Object.freeze({ x: 24, y: 24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_B: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: 24 }),
  ]),
  C_M: Object.freeze([
    Object.freeze({ x: 24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_C: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: 24, y: -24 }),
  ]),
  D_M: Object.freeze([
    Object.freeze({ x: -24, y: -24 }),
    Object.freeze({ x: 0, y: 0 }),
  ]),
  M_D: Object.freeze([
    Object.freeze({ x: 0, y: 0 }),
    Object.freeze({ x: -24, y: -24 }),
  ]),
  ...buildWaveLanePolylines(),
  ...buildBarracksRouteLinkPolylines(),
});

const ROUTE_SEGMENT_LENGTHS = Object.freeze(Object.fromEntries(
  Object.entries(ROUTE_SEGMENT_POLYLINES).map(([segmentId, points]) => [segmentId, getPolylineLength(points)])
));

function getPolylineLength(points) {
  if (!Array.isArray(points) || points.length < 2) return 0;
  let total = 0;
  for (let i = 0; i < points.length - 1; i++) {
    total += pointDistance(points[i], points[i + 1]);
  }
  return total;
}

function pointDistance(a, b) {
  const dx = Number(b.x) - Number(a.x);
  const dy = Number(b.y) - Number(a.y);
  return Math.sqrt((dx * dx) + (dy * dy));
}

function samplePolyline(points, progress) {
  const clamped = Math.max(0, Math.min(1, Number(progress) || 0));
  const total = getPolylineLength(points);
  if (!Array.isArray(points) || points.length === 0)
    return { point: { x: 0, y: 0 }, tangent: { x: 0, y: 1 } };
  if (points.length === 1 || total <= 0)
    return { point: { x: Number(points[0].x) || 0, y: Number(points[0].y) || 0 }, tangent: { x: 0, y: 1 } };

  const target = total * clamped;
  let walked = 0;
  for (let i = 0; i < points.length - 1; i++) {
    const from = points[i];
    const to = points[i + 1];
    const segLen = pointDistance(from, to);
    if (segLen <= 0)
      continue;
    if (walked + segLen >= target) {
      const localT = (target - walked) / segLen;
      const tangent = normalize2D({
        x: Number(to.x) - Number(from.x),
        y: Number(to.y) - Number(from.y),
      });
      return {
        point: {
          x: lerp(Number(from.x), Number(to.x), localT),
          y: lerp(Number(from.y), Number(to.y), localT),
        },
        tangent,
      };
    }
    walked += segLen;
  }

  const last = points[points.length - 1];
  const prev = points[points.length - 2];
  return {
    point: { x: Number(last.x) || 0, y: Number(last.y) || 0 },
    tangent: normalize2D({
      x: Number(last.x) - Number(prev.x),
      y: Number(last.y) - Number(prev.y),
    }),
  };
}

function lerp(a, b, t) {
  return a + ((b - a) * t);
}

function normalize2D(vec) {
  const x = Number(vec && vec.x) || 0;
  const y = Number(vec && vec.y) || 0;
  const len = Math.sqrt((x * x) + (y * y));
  if (len <= 0.00001)
    return { x: 0, y: 0 };
  return { x: x / len, y: y / len };
}

function perpendicular2D(vec) {
  const safe = normalize2D(vec);
  return { x: -safe.y, y: safe.x };
}

function getLaneNodeId(laneIndex) {
  return Number.isInteger(laneIndex) && laneIndex >= 0 && laneIndex < LANE_NODE_IDS.length
    ? LANE_NODE_IDS[laneIndex]
    : null;
}

function getWaveSpawnNodeId(laneIndex) {
  return Number.isInteger(laneIndex) && laneIndex >= 0 && laneIndex < WAVE_SPAWN_NODE_IDS.length
    ? WAVE_SPAWN_NODE_IDS[laneIndex]
    : null;
}

function getNodeIndex(nodeId) {
  return ROUTE_NODE_IDS.indexOf(nodeId);
}

function getLaneCombatAxes(laneIndex) {
  return Number.isInteger(laneIndex) && laneIndex >= 0 && laneIndex < LANE_COMBAT_AXES.length
    ? LANE_COMBAT_AXES[laneIndex]
    : null;
}

function getBarracksRouteStartNodeId(laneIndex, barracksId) {
  const coreNodeId = getLaneNodeId(laneIndex);
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  const suffix = normalizedBarracksId ? BARRACKS_ROUTE_NODE_SUFFIXES[normalizedBarracksId] : null;
  if (!coreNodeId || !suffix)
    return null;
  return `${coreNodeId}${suffix}`;
}

function getLaneCoreNodeIdForRouteNode(nodeId) {
  const normalizedNodeId = String(nodeId || "").trim().toUpperCase();
  if (ROUTE_NODE_IDS.includes(normalizedNodeId))
    return normalizedNodeId;

  for (const coreNodeId of ROUTE_NODE_IDS) {
    if (normalizedNodeId.startsWith(coreNodeId)) {
      const suffix = normalizedNodeId.slice(coreNodeId.length);
      if (suffix === BARRACKS_ROUTE_NODE_SUFFIXES.center
          || suffix === BARRACKS_ROUTE_NODE_SUFFIXES.left
          || suffix === BARRACKS_ROUTE_NODE_SUFFIXES.right) {
        return coreNodeId;
      }
    }
  }

  return null;
}

function isBarracksRouteStartNode(nodeId) {
  const normalizedNodeId = String(nodeId || "").trim().toUpperCase();
  return normalizedNodeId.length > 1
    && getLaneCoreNodeIdForRouteNode(normalizedNodeId) !== normalizedNodeId
    && getLaneCoreNodeIdForRouteNode(normalizedNodeId) !== null;
}

function buildBarracksRouteGraphNodePositions() {
  const positions = {
    ...ROUTE_GRAPH_CORE_NODE_POSITIONS,
  };

  for (let laneIndex = 0; laneIndex < LANE_COMBAT_AXES.length; laneIndex += 1) {
    const axes = LANE_COMBAT_AXES[laneIndex];
    const coreNodeId = LANE_NODE_IDS[laneIndex];
    if (!axes || !coreNodeId)
      continue;

    for (const barracksId of Object.keys(BARRACKS_SITE_COMBAT_OFFSETS)) {
      const routeNodeId = getBarracksRouteStartNodeId(laneIndex, barracksId);
      const offset = BARRACKS_SITE_COMBAT_OFFSETS[barracksId];
      if (!routeNodeId || !offset)
        continue;

      positions[routeNodeId] = Object.freeze({
        x: axes.core.x + (axes.lateral.x * offset.x) + (axes.forward.x * offset.y),
        y: axes.core.y + (axes.lateral.y * offset.x) + (axes.forward.y * offset.y),
      });
    }
  }

  return positions;
}

function buildBarracksRouteLinkPolylines() {
  const segments = {};
  for (let laneIndex = 0; laneIndex < LANE_NODE_IDS.length; laneIndex += 1) {
    const coreNodeId = LANE_NODE_IDS[laneIndex];
    const corePos = ROUTE_GRAPH_CORE_NODE_POSITIONS[coreNodeId];
    if (!coreNodeId || !corePos)
      continue;

    for (const barracksId of Object.keys(BARRACKS_SITE_COMBAT_OFFSETS)) {
      const routeNodeId = getBarracksRouteStartNodeId(laneIndex, barracksId);
      const routeNodePos = routeNodeId ? ROUTE_GRAPH_NODE_POSITIONS[routeNodeId] : null;
      if (!routeNodeId || !routeNodePos)
        continue;

      segments[`${routeNodeId}_${coreNodeId}`] = Object.freeze([
        Object.freeze({ x: routeNodePos.x, y: routeNodePos.y }),
        Object.freeze({ x: corePos.x, y: corePos.y }),
      ]);
    }
  }

  return segments;
}

function buildWaveLanePolylines() {
  const segments = {};
  for (let laneIndex = 0; laneIndex < LANE_NODE_IDS.length; laneIndex += 1) {
    const waveNodeId = WAVE_SPAWN_NODE_IDS[laneIndex];
    const coreNodeId = LANE_NODE_IDS[laneIndex];
    const axes = getLaneCombatAxes(laneIndex);
    const corePos = coreNodeId ? ROUTE_GRAPH_CORE_NODE_POSITIONS[coreNodeId] : null;
    if (!waveNodeId || !coreNodeId || !axes || !corePos)
      continue;

    const frontGatePoint = Object.freeze({
      x: corePos.x + (Number(axes.forward.x) * FRONT_GATE_COMBAT_OFFSET),
      y: corePos.y + (Number(axes.forward.y) * FRONT_GATE_COMBAT_OFFSET),
    });
    segments[`${waveNodeId}_${coreNodeId}`] = Object.freeze([
      Object.freeze({ x: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.x, y: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.y }),
      frontGatePoint,
      Object.freeze({ x: corePos.x, y: corePos.y }),
    ]);
  }

  return segments;
}

function getWaveSpawnWorldPosition(laneIndex) {
  if (!Number.isInteger(laneIndex) || laneIndex < 0 || laneIndex >= LANE_NODE_IDS.length)
    return null;

  return {
    x: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.x,
    y: ROUTE_GRAPH_CORE_NODE_POSITIONS.M.y,
  };
}

function getPadWorldPosition(laneIndex, gridX, gridY) {
  const axes = getLaneCombatAxes(laneIndex);
  if (!axes)
    return { x: 0, y: 0 };

  const lateralOffset = (Number(gridX) || 0) - 3;
  const forwardOffset = 25 - (Number(gridY) || 0);
  return {
    x: axes.core.x + (axes.lateral.x * lateralOffset) + (axes.forward.x * forwardOffset),
    y: axes.core.y + (axes.lateral.y * lateralOffset) + (axes.forward.y * forwardOffset),
  };
}

function getBarracksSiteWorldPosition(laneIndex, barracksId) {
  const axes = getLaneCombatAxes(laneIndex);
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  const offset = normalizedBarracksId ? BARRACKS_SITE_COMBAT_OFFSETS[normalizedBarracksId] : null;
  if (!axes || !offset)
    return null;

  return {
    x: axes.core.x + (axes.lateral.x * offset.x) + (axes.forward.x * offset.y),
    y: axes.core.y + (axes.lateral.y * offset.x) + (axes.forward.y * offset.y),
  };
}

function getNextClockwiseLaneIndex(laneIndex) {
  if (!Number.isInteger(laneIndex))
    return null;
  return (laneIndex + 1 + LANE_NODE_IDS.length) % LANE_NODE_IDS.length;
}

function resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex) {
  if (!game || !Array.isArray(game.lanes))
    return null;

  for (let step = 1; step <= game.lanes.length; step++) {
    const idx = (sourceLaneIndex + step) % game.lanes.length;
    if (isOpponentLane(game, sourceLaneIndex, idx))
      return idx;
  }
  return null;
}

function resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex) {
  const opposingIdx = Number.isInteger(sourceLaneIndex) && sourceLaneIndex >= 0 && sourceLaneIndex < OPPOSING_LANE_INDEX.length
    ? OPPOSING_LANE_INDEX[sourceLaneIndex]
    : null;
  if (Number.isInteger(opposingIdx) && isOpponentLane(game, sourceLaneIndex, opposingIdx))
    return opposingIdx;
  return resolveOuterLoopTargetLaneIndex(game, sourceLaneIndex);
}

function normalizeLaneCommandState(value) {
  const normalized = String(value || "").trim().toUpperCase();
  switch (normalized) {
    case LANE_COMMAND_STATES.ATTACK:
    case "ADVANCE":
      return LANE_COMMAND_STATES.ATTACK;
    case LANE_COMMAND_STATES.DEFEND:
    case "HOLD":
      return LANE_COMMAND_STATES.DEFEND;
    case LANE_COMMAND_STATES.RETREAT:
    case "CALLBACK":
      return LANE_COMMAND_STATES.RETREAT;
    default:
      return null;
  }
}

function isLaneCombatEnabledCommandState(commandState) {
  return commandState === LANE_COMMAND_STATES.ATTACK
    || commandState === LANE_COMMAND_STATES.DEFEND;
}

function getDefaultLaneObjectiveLaneIndex(game, sourceLaneIndex) {
  if (!Number.isInteger(sourceLaneIndex) || sourceLaneIndex < 0)
    return null;

  const opposingLaneIndex = OPPOSING_LANE_INDEX[sourceLaneIndex];
  if (game && Array.isArray(game.lanes) && Number.isInteger(opposingLaneIndex)
      && opposingLaneIndex >= 0 && opposingLaneIndex < game.lanes.length) {
    return opposingLaneIndex;
  }

  if (game && Array.isArray(game.lanes) && game.lanes.length > 1) {
    const resolved = resolveCenterCrossTargetLaneIndex(game, sourceLaneIndex);
    if (Number.isInteger(resolved))
      return resolved;
  }

  return sourceLaneIndex;
}

function getLaneCommandState(lane) {
  return normalizeLaneCommandState(lane && lane.commandState) || LANE_COMMAND_STATES.DEFEND;
}

function getLaneCommandObjectiveLaneIndex(game, laneOrLaneIndex) {
  const lane = typeof laneOrLaneIndex === "object"
    ? laneOrLaneIndex
    : getSourceLane(game, laneOrLaneIndex);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const explicit = Number.isInteger(lane.commandTargetLaneIndex)
    ? lane.commandTargetLaneIndex
    : null;
  if (explicit !== null && game && Array.isArray(game.lanes)
      && explicit >= 0 && explicit < game.lanes.length) {
    return explicit;
  }

  return getDefaultLaneObjectiveLaneIndex(game, lane.laneIndex);
}

function getLaneCommandRouteObjectiveLaneIndex(game, laneOrLaneIndex) {
  const lane = typeof laneOrLaneIndex === "object"
    ? laneOrLaneIndex
    : getSourceLane(game, laneOrLaneIndex);
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane);
  if (commandState === LANE_COMMAND_STATES.DEFEND || commandState === LANE_COMMAND_STATES.RETREAT)
    return lane.laneIndex;

  return getLaneCommandObjectiveLaneIndex(game, lane);
}

function getLaneCommandEngagementRadius(lane) {
  const commandState = getLaneCommandState(lane);
  if (!isLaneCombatEnabledCommandState(commandState))
    return 0;
  const explicitRadius = Number(lane && lane.engagementRadius);
  if (Number.isFinite(explicitRadius) && explicitRadius > 0)
    return commandState === LANE_COMMAND_STATES.DEFEND
      ? Math.max(explicitRadius, LANE_COMMAND_DEFENSE_RADIUS)
      : Math.max(explicitRadius, LANE_COMMAND_COMBAT_LEASH);
  return commandState === LANE_COMMAND_STATES.DEFEND
    ? LANE_COMMAND_DEFENSE_RADIUS
    : LANE_COMMAND_COMBAT_LEASH;
}

function getLaneCommandAnchorProgress(lane) {
  const commandState = getLaneCommandState(lane);
  if (commandState === LANE_COMMAND_STATES.ATTACK)
    return 1;
  if (commandState === LANE_COMMAND_STATES.DEFEND || commandState === LANE_COMMAND_STATES.RETREAT)
    return 0;
  const raw = Number(lane && (lane.commandAnchorProgress ?? lane.formationAnchorProgress));
  if (!Number.isFinite(raw))
    return 0;
  return Math.max(0, Math.min(1, raw));
}

function resolveLaneCommandContainerLaneIndex(game, lane) {
  if (!lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane);
  if (commandState === LANE_COMMAND_STATES.RETREAT)
    return lane.laneIndex;

  const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  if (!Number.isInteger(objectiveLaneIndex))
    return lane.laneIndex;

  return getLaneCommandAnchorProgress(lane) <= LANE_COMMAND_HOME_CONTAINER_PROGRESS
    ? lane.laneIndex
    : objectiveLaneIndex;
}

function resolveRouteNodeLaneIndex(nodeId) {
  const coreNodeId = getLaneCoreNodeIdForRouteNode(nodeId);
  return coreNodeId ? getNodeIndex(coreNodeId) : -1;
}

function resolveLaneControlledUnitCurrentSegmentLaneIndex(unit) {
  const currentSegment = String(unit && unit.currentSegment || "").trim().toUpperCase();
  if (currentSegment === "")
    return null;

  const parts = currentSegment.split("_");
  if (parts.length !== 2)
    return null;

  const fromLaneIndex = resolveRouteNodeLaneIndex(parts[0]);
  const toLaneIndex = resolveRouteNodeLaneIndex(parts[1]);
  if (fromLaneIndex >= 0 && toLaneIndex >= 0)
    return fromLaneIndex === toLaneIndex
      ? fromLaneIndex
      : ((Math.max(0, Math.min(1, Number(unit && unit.segmentProgress) || 0)) < 0.5)
        ? fromLaneIndex
        : toLaneIndex);
  if (fromLaneIndex >= 0)
    return fromLaneIndex;
  if (toLaneIndex >= 0)
    return toLaneIndex;
  return null;
}

function resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit) {
  const fallbackLaneIndex = resolveLaneCommandContainerLaneIndex(game, ownerLane);
  if (!ownerLane || !isLaneControlledUnit(unit))
    return fallbackLaneIndex;

  const currentSegmentLaneIndex = resolveLaneControlledUnitCurrentSegmentLaneIndex(unit);
  if (Number.isInteger(currentSegmentLaneIndex) && currentSegmentLaneIndex >= 0)
    return currentSegmentLaneIndex;

  const routeStartLaneIndex = resolveRouteNodeLaneIndex(unit && unit.routeStartNode);
  const routeTargetLaneIndex = resolveRouteNodeLaneIndex(unit && unit.routeTargetNode);
  if (routeStartLaneIndex < 0 || routeTargetLaneIndex < 0)
    return fallbackLaneIndex;
  if (routeStartLaneIndex === routeTargetLaneIndex)
    return routeStartLaneIndex;

  const routeProgress = computeUnitRoutePathIndex(unit);
  if (!Number.isFinite(routeProgress))
    return fallbackLaneIndex;

  return routeProgress < 0.5
    ? routeStartLaneIndex
    : routeTargetLaneIndex;
}

function buildLaneCommandCoreRouteSegments(sourceLaneIndex, objectiveLaneIndex) {
  const sourceNodeId = getLaneNodeId(sourceLaneIndex);
  const objectiveNodeId = getLaneNodeId(objectiveLaneIndex);
  if (!sourceNodeId || !objectiveNodeId)
    return null;
  if (sourceNodeId === objectiveNodeId)
    return [];
  return buildRouteSegments(ROUTE_TYPES.CENTER_CROSS, sourceNodeId, objectiveNodeId);
}

function buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksId, objectiveLaneIndex) {
  const sourceNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksId);
  const sourceCoreNodeId = getLaneNodeId(sourceLaneIndex);
  const coreRouteSegments = buildLaneCommandCoreRouteSegments(sourceLaneIndex, objectiveLaneIndex);
  if (!sourceNodeId || !sourceCoreNodeId || !Array.isArray(coreRouteSegments))
    return null;
  return [`${sourceNodeId}_${sourceCoreNodeId}`, ...coreRouteSegments];
}

function buildLaneCommandAnchorSet(game, lane) {
  if (!game || !lane || !Number.isInteger(lane.laneIndex))
    return null;

  const axes = getLaneCombatAxes(lane.laneIndex);
  if (!axes)
    return null;

  const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  const objectiveLane = getLaneByIndex(game, objectiveLaneIndex);
  const enemyCoreTarget = objectiveLane ? getLaneTownCoreCombatTarget(objectiveLane) : null;
  const attackFacing = enemyCoreTarget
    ? normalize2D({
        x: Number(enemyCoreTarget.posX) - Number(axes.core.x),
        y: Number(enemyCoreTarget.posY) - Number(axes.core.y),
      })
    : axes.forward;

  return {
    insideGateAnchor: {
      x: Number(axes.core.x) - (Number(axes.forward.x) * PACKET_INSIDE_GATE_OFFSET),
      y: Number(axes.core.y) - (Number(axes.forward.y) * PACKET_INSIDE_GATE_OFFSET),
    },
    outsideGateAnchor: {
      x: Number(axes.core.x) + (Number(axes.forward.x) * PACKET_OUTSIDE_GATE_OFFSET),
      y: Number(axes.core.y) + (Number(axes.forward.y) * PACKET_OUTSIDE_GATE_OFFSET),
    },
    enemyCoreAnchor: enemyCoreTarget
      ? {
          x: Number(enemyCoreTarget.posX) || 0,
          y: Number(enemyCoreTarget.posY) || 0,
        }
      : {
          x: Number(axes.core.x) || 0,
          y: Number(axes.core.y) || 0,
        },
    objectiveLaneIndex,
    forward: axes.forward,
    lateral: axes.lateral,
    attackFacing,
  };
}

function sampleLaneCommandAnchor(game, lane) {
  if (!game || !lane || !Number.isInteger(lane.laneIndex))
    return null;

  const commandState = getLaneCommandState(lane);
  const anchorSet = buildLaneCommandAnchorSet(game, lane);
  if (!anchorSet)
    return null;
  const objectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, lane);
  const baseRouteSegments = buildLaneCommandCoreRouteSegments(lane.laneIndex, objectiveLaneIndex);
  if (!Array.isArray(baseRouteSegments))
    return null;
  const anchorProgress = getLaneCommandAnchorProgress(lane);
  const anchorKind = commandState === LANE_COMMAND_STATES.RETREAT
    ? LANE_STANCE_ANCHOR_KINDS.INSIDE_GATE
    : (commandState === LANE_COMMAND_STATES.DEFEND
      ? LANE_STANCE_ANCHOR_KINDS.OUTSIDE_GATE
      : LANE_STANCE_ANCHOR_KINDS.ENEMY_CORE);
  const anchorPoint = anchorKind === LANE_STANCE_ANCHOR_KINDS.INSIDE_GATE
    ? anchorSet.insideGateAnchor
    : (anchorKind === LANE_STANCE_ANCHOR_KINDS.OUTSIDE_GATE
      ? anchorSet.outsideGateAnchor
      : anchorSet.enemyCoreAnchor);
  const facing = normalize2D(
    commandState === LANE_COMMAND_STATES.ATTACK
      ? anchorSet.attackFacing
      : anchorSet.forward
  );
  return {
    commandState,
    combatEnabled: isLaneCombatEnabledCommandState(commandState),
    engagementRadius: getLaneCommandEngagementRadius(lane),
    objectiveLaneIndex,
    containerLaneIndex: resolveLaneCommandContainerLaneIndex(game, lane),
    anchorProgress,
    anchorKind,
    anchorX: Number(anchorPoint && anchorPoint.x) || 0,
    anchorY: Number(anchorPoint && anchorPoint.y) || 0,
    facing,
    lateral: perpendicular2D(facing),
    insideGateAnchor: anchorSet.insideGateAnchor,
    outsideGateAnchor: anchorSet.outsideGateAnchor,
    enemyCoreAnchor: anchorSet.enemyCoreAnchor,
    baseRouteSegments,
  };
}

function normalizeLegacyDefenderUnit(game, fallbackLane, unit) {
  if (!unit || !unit.isDefender)
    return false;

  const ownerLane = getSourceLane(game, Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1) || fallbackLane;
  if (!ownerLane || !Number.isInteger(ownerLane.laneIndex))
    return false;

  const normalizedBarracksId = resolveUnitSourceBarracksId(unit) || "center";
  const allegianceKey = resolveLaneAllegianceKey(ownerLane) || resolveUnitAllegianceKey(game, ownerLane, unit);

  unit.isDefender = false;
  unit.spawnSourceType = unit.isHero ? SPAWN_SOURCE_TYPES.BARRACKS_HERO : SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;
  unit.sourceLaneIndex = ownerLane.laneIndex;
  unit.ownerLaneIndex = ownerLane.laneIndex;
  unit.ownerLane = ownerLane.laneIndex;
  unit.targetLaneIndex = Number.isInteger(unit.targetLaneIndex) ? unit.targetLaneIndex : ownerLane.laneIndex;
  unit.laneId = unit.targetLaneIndex;
  unit.sourceBarracksId = normalizedBarracksId;
  unit.sourceBarracksKey = normalizedBarracksId;
  unit.barracksId = normalizedBarracksId;
  unit.allegianceKey = allegianceKey;
  unit.sourceTeam = resolveLegacySourceTeamFromAllegianceKey(allegianceKey) || ownerLane.team || null;
  unit.stance = null;
  unit.pathContractType = null;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.defState = null;
  unit._legacyDefenderNormalized = true;
  return true;
}

function normalizeLegacyDefenderUnits(game) {
  if (!game || !Array.isArray(game.lanes))
    return;

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (const unit of lane.units) {
      if (normalizeLegacyDefenderUnit(game, lane, unit))
        applyCanonicalUnitMirrors(game, lane, unit);
    }
  }
}

function isLaneControlledUnit(unit) {
  if (!unit || !Number.isInteger(unit.sourceLaneIndex) || unit.sourceLaneIndex < 0)
    return false;
  if (unit.isDefender)
    return false;
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  return spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER
    || spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_HERO;
}

function getLaneCommandOwnerLane(game, unit) {
  if (!isLaneControlledUnit(unit))
    return null;
  return getSourceLane(game, unit.sourceLaneIndex);
}

function getLaneCommandStateForUnit(game, unit) {
  return getLaneCommandState(getLaneCommandOwnerLane(game, unit));
}

function isLaneCommandCombatEnabledForUnit(game, unit) {
  if (!isLaneControlledUnit(unit))
    return true;
  return isLaneCombatEnabledCommandState(getLaneCommandStateForUnit(game, unit));
}

function resolveTargetLaneForBarracksSend(game, sourceLaneIndex, barracksId) {
  const sourceLane = getSourceLane(game, sourceLaneIndex);
  if (!sourceLane)
    return null;
  return resolveLaneCommandContainerLaneIndex(game, sourceLane);
}

function buildRouteSegments(routeType, sourceNodeId, targetNodeId) {
  if (!routeType)
    return null;

  const routeSourceNodeId = String(sourceNodeId || "").trim().toUpperCase();
  const routeTargetNodeId = String(targetNodeId || "").trim().toUpperCase();
  const sourceCoreNodeId = getLaneCoreNodeIdForRouteNode(routeSourceNodeId);
  const prependBarracksLink = isBarracksRouteStartNode(routeSourceNodeId) && sourceCoreNodeId
    ? `${routeSourceNodeId}_${sourceCoreNodeId}`
    : null;

  if (prependBarracksLink && routeTargetNodeId && sourceCoreNodeId && routeTargetNodeId === sourceCoreNodeId)
    return [prependBarracksLink];

  if (routeType === ROUTE_TYPES.WAVE_LANE) {
    if (!routeSourceNodeId || !routeTargetNodeId)
      return null;
    return [`${routeSourceNodeId}_${routeTargetNodeId}`];
  }

  if (routeType === ROUTE_TYPES.CENTER_CROSS) {
    if (!routeSourceNodeId || !routeTargetNodeId || !sourceCoreNodeId)
      return null;
    const segments = [];
    if (prependBarracksLink)
      segments.push(prependBarracksLink);
    segments.push(`${sourceCoreNodeId}_${RouteMineNode}`);
    segments.push(`${RouteMineNode}_${routeTargetNodeId}`);
    return segments;
  }

  if (routeType !== ROUTE_TYPES.OUTER_LOOP)
    return null;

  if (!sourceCoreNodeId)
    return null;

  const sourceIndex = getNodeIndex(sourceCoreNodeId);
  if (sourceIndex < 0)
    return null;

  const segments = prependBarracksLink ? [prependBarracksLink] : [];
  for (let step = 0; step < ROUTE_NODE_IDS.length; step++) {
    const from = ROUTE_NODE_IDS[(sourceIndex + step) % ROUTE_NODE_IDS.length];
    const to = ROUTE_NODE_IDS[(sourceIndex + step + 1) % ROUTE_NODE_IDS.length];
    segments.push(`${from}_${to}`);
  }
  return segments;
}

function parseRouteSegmentId(segmentId) {
  const parts = String(segmentId || "").trim().toUpperCase().split("_");
  if (parts.length !== 2 || !parts[0] || !parts[1])
    return null;
  return {
    fromNode: parts[0],
    toNode: parts[1],
  };
}

function getRouteLength(routeSegments) {
  if (!Array.isArray(routeSegments))
    return 0;
  return routeSegments.reduce((sum, segmentId) => sum + (ROUTE_SEGMENT_LENGTHS[segmentId] || 0), 0);
}

function sampleRoutePosition(routeSegments, segmentIndex, segmentProgress, lateralOffset = 0) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;

  const safeIndex = Math.max(0, Math.min(routeSegments.length - 1, Math.floor(Number(segmentIndex) || 0)));
  const segmentId = routeSegments[safeIndex];
  const points = ROUTE_SEGMENT_POLYLINES[segmentId];
  if (!Array.isArray(points) || points.length < 2)
    return null;
  const sample = samplePolyline(ROUTE_SEGMENT_POLYLINES[segmentId], segmentProgress);
  const lateral = perpendicular2D(sample.tangent);
  return {
    segmentId,
    point: {
      x: sample.point.x + (lateral.x * (Number(lateralOffset) || 0)),
      y: sample.point.y + (lateral.y * (Number(lateralOffset) || 0)),
    },
    tangent: sample.tangent,
  };
}

function advanceRouteState(unit, deltaDistance) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  let remaining = Number(deltaDistance) || 0;
  const isLooping = unit.routeType === ROUTE_TYPES.OUTER_LOOP;
  let advanced = false;

  while (Math.abs(remaining) > 0.0001) {
    const currentSegmentId = unit.routeSegments[unit.routeSegmentIndex];
    const segmentLength = Math.max(0.0001, ROUTE_SEGMENT_LENGTHS[currentSegmentId] || 0.0001);
    const distanceOnSegment = Math.max(0, Math.min(1, Number(unit.segmentProgress) || 0)) * segmentLength;

    if (remaining > 0) {
      const distanceToEnd = segmentLength - distanceOnSegment;
      if (remaining < distanceToEnd) {
        unit.segmentProgress = (distanceOnSegment + remaining) / segmentLength;
        remaining = 0;
        advanced = true;
        break;
      }

      remaining -= distanceToEnd;
      unit.segmentProgress = 1;
      advanced = true;

      if (unit.routeSegmentIndex >= unit.routeSegments.length - 1) {
        if (!isLooping) {
          remaining = 0;
          break;
        }
        unit.routeSegmentIndex = 0;
        unit.segmentProgress = 0;
        continue;
      }

      unit.routeSegmentIndex += 1;
      unit.segmentProgress = 0;
      continue;
    }
    const distanceToStart = distanceOnSegment;
    if (Math.abs(remaining) < distanceToStart) {
      unit.segmentProgress = (distanceOnSegment + remaining) / segmentLength;
      remaining = 0;
      advanced = true;
      break;
    }

    remaining += distanceToStart;
    unit.segmentProgress = 0;
    advanced = true;

    if (unit.routeSegmentIndex <= 0) {
      if (!isLooping) {
        remaining = 0;
        break;
      }
      unit.routeSegmentIndex = unit.routeSegments.length - 1;
      unit.segmentProgress = 1;
      continue;
    }

    unit.routeSegmentIndex -= 1;
    unit.segmentProgress = 1;
  }

  return advanced;
}

function moveScalarToward(value, target, maxDelta) {
  const numericValue = Number(value);
  const numericTarget = Number(target);
  const safeDelta = Math.max(0, Number(maxDelta) || 0);
  if (!Number.isFinite(numericValue))
    return Number.isFinite(numericTarget) ? numericTarget : 0;
  if (!Number.isFinite(numericTarget))
    return numericValue;
  if (safeDelta <= 0)
    return numericValue;

  const delta = numericTarget - numericValue;
  if (Math.abs(delta) <= safeDelta)
    return numericTarget;
  return numericValue + (Math.sign(delta) * safeDelta);
}

function relaxUnitRouteOffsets(unit, speed) {
  if (!unit)
    return;

  const baseRelaxStep = Math.max(0.12, Math.abs(Number(speed) || 0) * 0.25);
  const relaxStep = DISABLE_RIGID_PACKET_FORMATIONS && isLaneControlledUnit(unit)
    ? Math.max(0.45, Math.abs(Number(speed) || 0) * 0.85)
    : baseRelaxStep;
  unit.routeLateralOffset = moveScalarToward(unit.routeLateralOffset, 0, relaxStep);
  unit.routeLongitudinalOffset = moveScalarToward(
    unit.routeLongitudinalOffset,
    0,
    DISABLE_RIGID_PACKET_FORMATIONS && isLaneControlledUnit(unit)
      ? relaxStep
      : (relaxStep * 0.6)
  );
}

function setUnitRouteSnapshotState(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const routeProgress = computeUnitRoutePathIndex(unit);
  const longitudinalOffset = Number(unit.routeLongitudinalOffset) || 0;
  const lateralOffset = Number(unit.routeLateralOffset) || 0;
  const sample = sampleContinuousRoutePosition(
    unit.routeSegments,
    routeProgress,
    longitudinalOffset,
    lateralOffset
  );
  if (!sample)
    return false;

  unit.currentSegment = sample.segmentId;
  unit.posX = sample.point.x;
  unit.posY = sample.point.y;
  unit.pathIdx = routeProgress;
  unit.routeWorldX = unit.posX;
  unit.routeWorldY = unit.posY;
  return true;
}

function computeUnitRoutePathIndex(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return 0;

  const totalLength = Math.max(0.0001, getRouteLength(unit.routeSegments));
  let distance = 0;
  for (let i = 0; i < unit.routeSegmentIndex; i++) {
    distance += ROUTE_SEGMENT_LENGTHS[unit.routeSegments[i]] || 0;
  }
  const currentSegmentId = unit.routeSegments[unit.routeSegmentIndex];
  const currentSegmentLength = ROUTE_SEGMENT_LENGTHS[currentSegmentId] || 0;
  distance += Math.max(0, Math.min(1, Number(unit.segmentProgress) || 0)) * currentSegmentLength;
  return distance / totalLength;
}

function sampleContinuousRoutePosition(routeSegments, routeProgress, longitudinalOffset = 0, lateralOffset = 0) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;

  const clampedProgress = Math.max(0, Math.min(1, Number(routeProgress) || 0));
  const centerSample = sampleRouteByDistanceNorm(routeSegments, clampedProgress, 0);
  if (!centerSample)
    return null;

  const beforeSample = sampleRouteByDistanceNorm(
    routeSegments,
    Math.max(0, clampedProgress - ROUTE_TANGENT_SAMPLE_DELTA),
    0
  );
  const afterSample = sampleRouteByDistanceNorm(
    routeSegments,
    Math.min(1, clampedProgress + ROUTE_TANGENT_SAMPLE_DELTA),
    0
  );

  let tangent = centerSample.tangent;
  if (beforeSample && afterSample) {
    tangent = normalize2D({
      x: Number(afterSample.point.x) - Number(beforeSample.point.x),
      y: Number(afterSample.point.y) - Number(beforeSample.point.y),
    });
  }
  tangent = normalize2D(tangent);
  const lateral = perpendicular2D(tangent);

  return {
    segmentId: centerSample.segmentId,
    point: {
      x: Number(centerSample.point.x) + (tangent.x * (Number(longitudinalOffset) || 0)) + (lateral.x * (Number(lateralOffset) || 0)),
      y: Number(centerSample.point.y) + (tangent.y * (Number(longitudinalOffset) || 0)) + (lateral.y * (Number(lateralOffset) || 0)),
    },
    tangent,
  };
}

function resolveSpawnOriginForUnit(unit, targetLane) {
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.SCHEDULED_WAVE)
    return getWaveSpawnWorldPosition(targetLane && targetLane.laneIndex);

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const sourceBarracksKey = normalizeBarracksSiteId(unit && (unit.sourceBarracksKey || unit.sourceBarracksId));
  if (sourceLaneIndex < 0 || !sourceBarracksKey)
    return null;

  return getBarracksSiteWorldPosition(sourceLaneIndex, sourceBarracksKey);
}

function resolveRouteContractForUnit(game, targetLane, unit) {
  if (!game || !targetLane || !unit)
    return { ok: false, reason: "Missing game, target lane, or unit" };

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  const targetLaneIndex = targetLane.laneIndex;
  const targetNodeId = getLaneNodeId(targetLaneIndex);
  if (!targetNodeId)
    return { ok: false, reason: `Target lane ${targetLaneIndex} is missing a route node` };

  if (spawnSourceType === SPAWN_SOURCE_TYPES.SCHEDULED_WAVE) {
    const sourceNodeId = getWaveSpawnNodeId(targetLaneIndex);
    const routeSegments = buildRouteSegments(ROUTE_TYPES.WAVE_LANE, sourceNodeId, targetNodeId);
    if (!sourceNodeId || !routeSegments)
      return { ok: false, reason: `Wave route is missing for lane ${targetLaneIndex}` };

    const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane);
    if (!spawnOrigin)
      return { ok: false, reason: `Wave spawn origin is missing for lane ${targetLaneIndex}` };

    return {
      ok: true,
      spawnSourceType,
      routeType: ROUTE_TYPES.WAVE_LANE,
      sourceNodeId,
      targetNodeId,
      routeSegments,
      spawnOrigin,
      pathId: buildRoutePathId(routeSegments),
    };
  }

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const sourceBarracksKey = normalizeBarracksSiteId(unit.sourceBarracksKey || unit.sourceBarracksId);
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, targetLane, unit);
  const objectiveNodeId = getLaneNodeId(objectiveLaneIndex);
  if (sourceLaneIndex < 0)
    return { ok: false, reason: "Barracks unit is missing sourceLaneIndex" };
  if (!sourceBarracksKey)
    return { ok: false, reason: "Barracks unit is missing a valid sourceBarracksId" };
  if (!objectiveNodeId)
    return { ok: false, reason: `Barracks unit is missing a valid objective lane (${objectiveLaneIndex})` };

  const sourceNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksKey);
  if (!sourceNodeId)
    return {
      ok: false,
      reason: `Barracks route start node is missing for lane=${sourceLaneIndex} barracks='${sourceBarracksKey}'`,
    };

  const routeSegments = buildLaneCommandRouteSegments(sourceLaneIndex, sourceBarracksKey, objectiveLaneIndex);
  if (!routeSegments)
    return { ok: false, reason: `Lane command route could not be built for barracks '${sourceBarracksKey}'` };

  const spawnOrigin = resolveSpawnOriginForUnit(unit, targetLane);
  if (!spawnOrigin)
    return { ok: false, reason: `Barracks spawn origin is missing for lane=${sourceLaneIndex} barracks='${sourceBarracksKey}'` };

  return {
    ok: true,
      spawnSourceType,
      routeType: ROUTE_TYPES.CENTER_CROSS,
      sourceNodeId,
      targetNodeId: objectiveNodeId,
      objectiveLaneIndex,
      routeSegments,
    spawnOrigin,
    pathId: buildRoutePathId(routeSegments),
    barracksKey: sourceBarracksKey,
    routeLabel: "lane_command_cross",
  };
}

function resolveRedirectRouteContractForExistingLaneControlledUnit(game, currentLane, targetLane, unit) {
  if (!game || !currentLane || !targetLane || !unit)
    return { ok: false, reason: "Missing game, current lane, target lane, or unit" };
  if (!isLaneControlledUnit(unit))
    return resolveRouteContractForUnit(game, targetLane, unit);

  const currentLaneIndex = Number.isInteger(currentLane.laneIndex)
    ? currentLane.laneIndex
    : resolveUnitTargetLaneIndex(game, currentLane, unit);
  const routeTargetLaneIndex = resolveUnitObjectiveLaneIndex(game, targetLane, unit);
  let sourceNodeId = getLaneNodeId(currentLaneIndex);
  let targetNodeId = getLaneNodeId(routeTargetLaneIndex);
  let routeSegments = buildLaneCommandCoreRouteSegments(currentLaneIndex, routeTargetLaneIndex);
  let routeType = ROUTE_TYPES.CENTER_CROSS;
  let routeLabel = "lane_command_redirect";

  if ((!sourceNodeId || !targetNodeId || !Array.isArray(routeSegments) || routeSegments.length <= 0)
      && Number.isInteger(currentLaneIndex)
      && currentLaneIndex === routeTargetLaneIndex) {
    const currentCoreNodeId = getLaneNodeId(currentLaneIndex);
    const currentSegment = parseRouteSegmentId(unit && unit.currentSegment);
    const sourceBarracksId = resolveUnitSourceBarracksId(unit);
    const barracksNodeId = getBarracksRouteStartNodeId(
      Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : currentLaneIndex,
      sourceBarracksId
    );

    if (currentCoreNodeId && currentSegment) {
      const fromCoreNodeId = getLaneCoreNodeIdForRouteNode(currentSegment.fromNode);
      const toCoreNodeId = getLaneCoreNodeIdForRouteNode(currentSegment.toNode);
      if (currentSegment.fromNode === RouteMineNode && toCoreNodeId === currentCoreNodeId) {
        sourceNodeId = RouteMineNode;
        targetNodeId = currentCoreNodeId;
        routeSegments = [`${RouteMineNode}_${currentCoreNodeId}`];
      } else if (currentSegment.toNode === RouteMineNode && fromCoreNodeId === currentCoreNodeId) {
        sourceNodeId = RouteMineNode;
        targetNodeId = currentCoreNodeId;
        routeSegments = [`${RouteMineNode}_${currentCoreNodeId}`];
      } else if (barracksNodeId
          && ((currentSegment.fromNode === barracksNodeId && currentSegment.toNode === currentCoreNodeId)
            || (currentSegment.toNode === barracksNodeId && currentSegment.fromNode === currentCoreNodeId))) {
        sourceNodeId = barracksNodeId;
        targetNodeId = currentCoreNodeId;
        routeSegments = [`${barracksNodeId}_${currentCoreNodeId}`];
      }
      routeLabel = "lane_command_redirect_home";
    }
  }

  if (!sourceNodeId || !targetNodeId || !Array.isArray(routeSegments) || routeSegments.length <= 0)
    return resolveRouteContractForUnit(game, targetLane, unit);

  return {
    ok: true,
    spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
    routeType,
    sourceNodeId,
    targetNodeId,
    objectiveLaneIndex: routeTargetLaneIndex,
    routeSegments,
    pathId: buildRoutePathId(routeSegments),
    routeLabel,
  };
}

function isSameLaneHoldRouteContract(routeContract, sourceLaneIndex, targetLaneIndex) {
  if (!routeContract)
    return false;
  if (!Number.isInteger(sourceLaneIndex) || !Number.isInteger(targetLaneIndex))
    return false;
  if (sourceLaneIndex !== targetLaneIndex)
    return false;

  const sourceCoreNodeId = getLaneCoreNodeIdForRouteNode(routeContract.sourceNodeId);
  return !!sourceCoreNodeId && routeContract.targetNodeId === sourceCoreNodeId;
}

function initializeMovingUnitRouteState(game, targetLane, unit, spawnLogicalPos) {
  if (!game || !targetLane || !unit)
    return { ok: false, reason: "Missing game, target lane, or unit" };

  const routeContract = resolveRouteContractForUnit(game, targetLane, unit);
  if (!routeContract.ok) {
    log.error("[SpawnAudit][ServerRoute] rejected", {
      unitId: unit && unit.id,
      unitType: unit && unit.type,
      targetLaneIndex: targetLane && targetLane.laneIndex,
      sourceLaneIndex: unit && unit.sourceLaneIndex,
      sourceBarracksKey: unit && (unit.sourceBarracksKey || unit.sourceBarracksId) || null,
      spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
      reason: routeContract.reason,
    });
    return routeContract;
  }

  const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = targetLane.laneIndex;
  const logicalX = spawnLogicalPos && Number.isFinite(spawnLogicalPos.x) ? Number(spawnLogicalPos.x) : SPAWN_X;
  const logicalY = spawnLogicalPos && Number.isFinite(spawnLogicalPos.y) ? Number(spawnLogicalPos.y) : SPAWN_YG;

  unit.spawnSourceType = routeContract.spawnSourceType;
  unit.routeType = routeContract.routeType;
  unit.routeStartNode = routeContract.sourceNodeId;
  unit.routeTargetNode = routeContract.targetNodeId;
  unit.objectiveLaneIndex = routeContract.objectiveLaneIndex ?? unit.objectiveLaneIndex ?? targetLane.laneIndex;
  unit.routeSegments = routeContract.routeSegments;
  unit.routeSegmentIndex = 0;
  unit.segmentProgress = 0;
  const startSample = sampleRoutePosition(unit.routeSegments, 0, 0, 0);
  if (!startSample) {
    const failure = {
      ok: false,
      reason: `Route sample is missing for path '${routeContract.pathId}'`,
    };
    log.error("[SpawnAudit][ServerRoute] rejected", {
      unitId: unit.id,
      unitType: unit.type,
      targetLaneIndex,
      sourceLaneIndex,
      sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
      spawnSourceType: unit.spawnSourceType,
      reason: failure.reason,
    });
    return failure;
  }
  const routeTangent = normalize2D(startSample.tangent);
  const routeLateral = perpendicular2D(routeTangent);
  const originDelta = {
    x: Number(routeContract.spawnOrigin.x) - Number(startSample.point.x),
    y: Number(routeContract.spawnOrigin.y) - Number(startSample.point.y),
  };
  const formationLateralOffset = DISABLE_RIGID_PACKET_FORMATIONS && isLaneControlledUnit(unit)
    ? 0
    : ((logicalX - SPAWN_X) * ROUTE_FORMATION_COLUMN_SPACING);
  const formationLongitudinalOffset = DISABLE_RIGID_PACKET_FORMATIONS && isLaneControlledUnit(unit)
    ? 0
    : (-logicalY * ROUTE_FORMATION_ROW_SPACING);
  unit.routeLateralOffset = dot2D(originDelta, routeLateral) + formationLateralOffset;
  unit.routeLongitudinalOffset = dot2D(originDelta, routeTangent) + formationLongitudinalOffset;
  unit.stance = routeContract.spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
    ? UNIT_STANCES.ATTACK
    : null;
  unit.routeState = WAVE_UNIT_STATES.MOVING;
  unit.combatState = WAVE_UNIT_STATES.MOVING;
  unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  unit.blockedByStructure = false;
  unit.blockedByStructureId = null;
  unit.blockedByStructureType = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.targetLaneIndex = targetLaneIndex;
  unit.laneId = targetLaneIndex;
  if (!setUnitRouteSnapshotState(unit)) {
    const failure = {
      ok: false,
      reason: `Failed to materialize path snapshot state for path '${routeContract.pathId}'`,
    };
    log.error("[SpawnAudit][ServerRoute] rejected", {
      unitId: unit.id,
      unitType: unit.type,
      targetLaneIndex,
      sourceLaneIndex,
      sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
      spawnSourceType: unit.spawnSourceType,
      reason: failure.reason,
    });
    return failure;
  }
  applyCanonicalUnitMirrors(game, targetLane, unit);

  log.info("[SpawnAudit][ServerRoute] assigned", {
    unitId: unit.id,
    unitType: unit.type,
    spawnSourceType: unit.spawnSourceType,
    sourceLaneIndex,
    targetLaneIndex,
    sourceBarracksKey: routeContract.barracksKey || null,
    spawnOriginX: Number.isFinite(routeContract.spawnOrigin.x) ? Number(routeContract.spawnOrigin.x.toFixed(3)) : null,
    spawnOriginY: Number.isFinite(routeContract.spawnOrigin.y) ? Number(routeContract.spawnOrigin.y.toFixed(3)) : null,
    routeType: routeContract.routeType,
    routeLabel: routeContract.routeLabel || routeContract.pathId,
    routeStartNode: routeContract.sourceNodeId,
    routeTargetNode: routeContract.targetNodeId,
    pathId: routeContract.pathId,
    currentWaypointIndex: unit.routeSegmentIndex,
    nextWaypoint: resolveUnitNextWaypoint(unit),
    movementState: unit.routeState,
    currentSegment: unit.currentSegment || null,
    segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
    routeWorldX: Number.isFinite(unit.routeWorldX) ? Number(unit.routeWorldX.toFixed(3)) : null,
    routeWorldY: Number.isFinite(unit.routeWorldY) ? Number(unit.routeWorldY.toFixed(3)) : null,
    routeLateralOffset: Number.isFinite(unit.routeLateralOffset) ? Number(unit.routeLateralOffset.toFixed(3)) : null,
    routeLongitudinalOffset: Number.isFinite(unit.routeLongitudinalOffset) ? Number(unit.routeLongitudinalOffset.toFixed(3)) : null,
  });

  return {
    ok: true,
    pathId: routeContract.pathId,
  };
}

function applyRouteContractToExistingUnit(unit, routeContract, currentPosition = null) {
  if (!unit || !routeContract || routeContract.ok === false)
    return { ok: false, reason: "Missing unit or route contract" };
  if (!Array.isArray(routeContract.routeSegments) || routeContract.routeSegments.length <= 0)
    return { ok: false, reason: "Route contract is missing route segments" };

  const startSample = sampleRoutePosition(routeContract.routeSegments, 0, 0, 0);
  if (!startSample) {
    return {
      ok: false,
      reason: `Route sample is missing for path '${routeContract.pathId || "<unknown>"}'`,
    };
  }

  const safeCurrentPosition = currentPosition
    && Number.isFinite(Number(currentPosition.x))
    && Number.isFinite(Number(currentPosition.y))
    ? { x: Number(currentPosition.x), y: Number(currentPosition.y) }
    : { x: Number(unit.posX) || Number(startSample.point.x), y: Number(unit.posY) || Number(startSample.point.y) };
  const projection = projectPointOntoRouteSegments(routeContract.routeSegments, safeCurrentPosition);
  const routeSample = projection || {
    segmentIndex: 0,
    segmentProgress: 0,
    segmentId: routeContract.routeSegments[0],
    point: startSample.point,
    tangent: startSample.tangent,
  };
  const routeTangent = normalize2D(routeSample.tangent);
  const routeLateral = perpendicular2D(routeTangent);
  const routeDelta = {
    x: safeCurrentPosition.x - Number(routeSample.point.x),
    y: safeCurrentPosition.y - Number(routeSample.point.y),
  };

  unit.routeType = routeContract.routeType;
  unit.routeStartNode = routeContract.sourceNodeId;
  unit.routeTargetNode = routeContract.targetNodeId;
  unit.objectiveLaneIndex = routeContract.objectiveLaneIndex ?? unit.objectiveLaneIndex ?? unit.targetLaneIndex;
  unit.routeSegments = routeContract.routeSegments;
  unit.routeSegmentIndex = Math.max(
    0,
    Math.min(routeContract.routeSegments.length - 1, Math.floor(Number(routeSample.segmentIndex) || 0))
  );
  unit.segmentProgress = Math.max(0, Math.min(1, Number(routeSample.segmentProgress) || 0));
  unit.currentSegment = routeSample.segmentId || routeContract.routeSegments[unit.routeSegmentIndex];
  unit.routeLateralOffset = dot2D(routeDelta, routeLateral);
  unit.routeLongitudinalOffset = dot2D(routeDelta, routeTangent);
  unit.stance = null;
  unit.routeState = WAVE_UNIT_STATES.MOVING;
  unit.combatState = WAVE_UNIT_STATES.MOVING;
  unit.movementMode = UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.blockedByStructure = false;
  unit.blockedByStructureId = null;
  unit.blockedByStructureType = null;
  unit._missingRouteLogged = false;

  if (!setUnitRouteSnapshotState(unit)) {
    return {
      ok: false,
      reason: `Failed to materialize path snapshot state for path '${routeContract.pathId || "<unknown>"}'`,
    };
  }
  applyCanonicalUnitMirrors(null, null, unit);

  return {
    ok: true,
    pathId: routeContract.pathId || buildRoutePathId(routeContract.routeSegments),
  };
}

function getUnitForwardDirection(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return { x: 0, y: 1 };
  const sample = sampleContinuousRoutePosition(
    unit.routeSegments,
    computeUnitRoutePathIndex(unit),
    0,
    0
  );
  return sample ? sample.tangent : { x: 0, y: 1 };
}

function buildSampledPathFromSegments(routeSegments, sampleCount = 28) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return [];

  const safeCount = Math.max(2, Math.floor(Number(sampleCount) || 28));
  const samples = [];
  for (let i = 0; i < safeCount; i++) {
    const distanceNorm = safeCount === 1 ? 0 : (i / (safeCount - 1));
    const sample = sampleRouteByDistanceNorm(routeSegments, distanceNorm, 0);
    if (!sample)
      return [];
    samples.push({
      x: Number(sample.point.x.toFixed(3)),
      y: Number(sample.point.y.toFixed(3)),
    });
  }
  return samples;
}

function sampleRouteByDistanceNorm(routeSegments, routeProgress, lateralOffset = 0) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;

  const totalLength = Math.max(0.0001, getRouteLength(routeSegments));
  let remainingDistance = Math.max(0, Math.min(1, Number(routeProgress) || 0)) * totalLength;

  for (let i = 0; i < routeSegments.length; i++) {
    const segmentId = routeSegments[i];
    const segmentLength = Math.max(0.0001, ROUTE_SEGMENT_LENGTHS[segmentId] || 0.0001);
    if (remainingDistance <= segmentLength || i === routeSegments.length - 1) {
      return sampleRoutePosition(routeSegments, i, remainingDistance / segmentLength, lateralOffset);
    }
    remainingDistance -= segmentLength;
  }

  return sampleRoutePosition(routeSegments, routeSegments.length - 1, 1, lateralOffset);
}

function projectPointOntoPolyline(points, targetPoint) {
  if (!Array.isArray(points) || points.length < 2 || !targetPoint)
    return null;

  const totalLength = Math.max(0.0001, getPolylineLength(points));
  let walkedLength = 0;
  let best = null;

  for (let i = 0; i < points.length - 1; i++) {
    const from = points[i];
    const to = points[i + 1];
    const segVec = {
      x: Number(to.x) - Number(from.x),
      y: Number(to.y) - Number(from.y),
    };
    const segLenSq = (segVec.x * segVec.x) + (segVec.y * segVec.y);
    const segLen = Math.sqrt(segLenSq);
    if (segLen <= 0.0001)
      continue;

    const toTarget = {
      x: Number(targetPoint.x) - Number(from.x),
      y: Number(targetPoint.y) - Number(from.y),
    };
    const localT = Math.max(0, Math.min(1, dot2D(toTarget, segVec) / segLenSq));
    const point = {
      x: lerp(Number(from.x), Number(to.x), localT),
      y: lerp(Number(from.y), Number(to.y), localT),
    };
    const delta = {
      x: Number(targetPoint.x) - point.x,
      y: Number(targetPoint.y) - point.y,
    };
    const distanceSq = (delta.x * delta.x) + (delta.y * delta.y);
    const candidate = {
      point,
      tangent: normalize2D(segVec),
      distanceSq,
      progress: (walkedLength + (segLen * localT)) / totalLength,
    };

    if (!best || candidate.distanceSq < best.distanceSq)
      best = candidate;

    walkedLength += segLen;
  }

  return best;
}

function projectPointOntoRouteSegments(routeSegments, targetPoint) {
  if (!Array.isArray(routeSegments) || routeSegments.length === 0 || !targetPoint)
    return null;

  const totalRouteLength = Math.max(0.0001, getRouteLength(routeSegments));
  let walkedRouteLength = 0;
  let best = null;

  for (let i = 0; i < routeSegments.length; i++) {
    const segmentId = routeSegments[i];
    const points = ROUTE_SEGMENT_POLYLINES[segmentId];
    const segmentLength = Math.max(0.0001, ROUTE_SEGMENT_LENGTHS[segmentId] || 0.0001);
    const projection = projectPointOntoPolyline(points, targetPoint);
    if (projection) {
      const candidate = {
        segmentIndex: i,
        segmentId,
        point: projection.point,
        tangent: projection.tangent,
        segmentProgress: Math.max(0, Math.min(1, Number(projection.progress) || 0)),
        routeProgress: (walkedRouteLength + (segmentLength * projection.progress)) / totalRouteLength,
        distanceSq: projection.distanceSq,
      };
      if (!best || candidate.distanceSq < best.distanceSq)
        best = candidate;
    }
    walkedRouteLength += segmentLength;
  }

  return best;
}

function syncUnitRouteStateToWorldPosition(unit, worldPosition = null) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const targetPoint = worldPosition
    && Number.isFinite(Number(worldPosition.x))
    && Number.isFinite(Number(worldPosition.y))
    ? { x: Number(worldPosition.x), y: Number(worldPosition.y) }
    : (Number.isFinite(Number(unit.posX)) && Number.isFinite(Number(unit.posY))
      ? { x: Number(unit.posX), y: Number(unit.posY) }
      : null);
  if (!targetPoint)
    return false;

  const best = projectPointOntoRouteSegments(unit.routeSegments, targetPoint);
  if (!best)
    return false;

  const tangent = normalize2D(best.tangent);
  const lateral = perpendicular2D(tangent);
  const delta = {
    x: targetPoint.x - best.point.x,
    y: targetPoint.y - best.point.y,
  };

  unit.routeSegmentIndex = best.segmentIndex;
  unit.segmentProgress = Math.max(0, Math.min(1, Number(best.segmentProgress) || 0));
  unit.currentSegment = best.segmentId;
  unit.routeLongitudinalOffset = dot2D(delta, tangent);
  unit.routeLateralOffset = dot2D(delta, lateral);
  unit.posX = targetPoint.x;
  unit.posY = targetPoint.y;
  unit.pathIdx = computeUnitRoutePathIndex(unit);
  unit.routeWorldX = targetPoint.x;
  unit.routeWorldY = targetPoint.y;
  return true;
}

function syncMovedUnitPathState(unit) {
  if (!unit)
    return false;

  if (syncUnitRouteStateToWorldPosition(unit))
    return true;

  unit.pathIdx = unit.posY;
  return false;
}

function resolveLaneFormationColumns(unitCount) {
  const safeCount = Math.max(1, Math.floor(Number(unitCount) || 1));
  return Math.max(1, Math.min(LANE_FORMATION_MAX_COLUMNS, safeCount >= 4 ? 5 : safeCount));
}

function resolveCenteredFormationOffset(columnIndex, columns) {
  const safeColumns = Math.max(1, Math.floor(Number(columns) || 1));
  const safeColumnIndex = Math.max(0, Math.min(safeColumns - 1, Math.floor(Number(columnIndex) || 0)));
  const center = Math.floor(safeColumns / 2);
  if (safeColumns % 2 === 1)
    return safeColumnIndex - center;

  const raw = safeColumnIndex < center
    ? safeColumnIndex - center
    : safeColumnIndex - center + 1;
  return raw >= 0 ? raw - 0.5 : raw + 0.5;
}

function buildLaneFormationSlot(anchorState, slotIndex, unitCount) {
  const safeSlotIndex = Math.max(0, Math.floor(Number(slotIndex) || 0));
  const columns = resolveLaneFormationColumns(unitCount);
  const row = Math.floor(safeSlotIndex / columns);
  const column = safeSlotIndex % columns;
  const lateralIndex = resolveCenteredFormationOffset(column, columns);
  const lateralDistance = lateralIndex * ROUTE_FORMATION_COLUMN_SPACING;
  const depthDistance = row * ROUTE_FORMATION_ROW_SPACING;
  const anchorX = Number(anchorState && anchorState.anchorX) || 0;
  const anchorY = Number(anchorState && anchorState.anchorY) || 0;
  const facing = anchorState && anchorState.facing ? anchorState.facing : { x: 0, y: -1 };
  const lateral = anchorState && anchorState.lateral ? anchorState.lateral : perpendicular2D(facing);
  return {
    slotIndex: safeSlotIndex,
    row,
    column,
    x: anchorX + (lateral.x * lateralDistance) - (facing.x * depthDistance),
    y: anchorY + (lateral.y * lateralDistance) - (facing.y * depthDistance),
  };
}

function resolveLaneControlledUnitSortKey(unit) {
  const currentSlotIndex = Number(unit && unit.currentSlotIndex);
  if (Number.isFinite(currentSlotIndex))
    return `slot:${String(Math.floor(currentSlotIndex)).padStart(6, "0")}:${unit.id}`;
  const spawnIndex = Number(unit && unit.spawnIndex);
  if (Number.isFinite(spawnIndex))
    return `spawn:${String(Math.floor(spawnIndex)).padStart(6, "0")}:${unit.id}`;
  return `id:${String(unit && unit.id || "")}`;
}

function normalizeCombatRole(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case UNIT_COMBAT_ROLES.SHIELD:
      return UNIT_COMBAT_ROLES.SHIELD;
    case UNIT_COMBAT_ROLES.SPEAR:
    case "polearm":
      return UNIT_COMBAT_ROLES.SPEAR;
    case UNIT_COMBAT_ROLES.ARCHER:
    case "ranged":
      return UNIT_COMBAT_ROLES.ARCHER;
    case UNIT_COMBAT_ROLES.MAGE:
    case "arcane":
      return UNIT_COMBAT_ROLES.MAGE;
    case UNIT_COMBAT_ROLES.PRIEST:
    case "support":
    case "healer":
      return UNIT_COMBAT_ROLES.PRIEST;
    case UNIT_COMBAT_ROLES.SWORD:
    case "melee":
    case "hero":
      return UNIT_COMBAT_ROLES.SWORD;
    default:
      return null;
  }
}

function resolvePreferredBandForCombatRole(combatRole) {
  switch (normalizeCombatRole(combatRole)) {
    case UNIT_COMBAT_ROLES.SHIELD:
      return UNIT_PREFERRED_BANDS.SHIELD_FRONT;
    case UNIT_COMBAT_ROLES.SPEAR:
      return UNIT_PREFERRED_BANDS.SPEAR_LINE;
    case UNIT_COMBAT_ROLES.ARCHER:
    case UNIT_COMBAT_ROLES.MAGE:
      return UNIT_PREFERRED_BANDS.RANGED_REAR;
    case UNIT_COMBAT_ROLES.PRIEST:
      return UNIT_PREFERRED_BANDS.PRIEST_REAR;
    case UNIT_COMBAT_ROLES.SWORD:
    default:
      return UNIT_PREFERRED_BANDS.SWORD_LINE;
  }
}

function normalizePreferredBand(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case UNIT_PREFERRED_BANDS.SHIELD_FRONT:
      return UNIT_PREFERRED_BANDS.SHIELD_FRONT;
    case UNIT_PREFERRED_BANDS.SWORD_LINE:
      return UNIT_PREFERRED_BANDS.SWORD_LINE;
    case UNIT_PREFERRED_BANDS.SPEAR_LINE:
      return UNIT_PREFERRED_BANDS.SPEAR_LINE;
    case UNIT_PREFERRED_BANDS.PRIEST_REAR:
      return UNIT_PREFERRED_BANDS.PRIEST_REAR;
    case UNIT_PREFERRED_BANDS.RANGED_REAR:
      return UNIT_PREFERRED_BANDS.RANGED_REAR;
    default:
      return null;
  }
}

function getPacketBandSortIndex(preferredBand) {
  switch (normalizePreferredBand(preferredBand)) {
    case UNIT_PREFERRED_BANDS.SHIELD_FRONT:
      return 0;
    case UNIT_PREFERRED_BANDS.SWORD_LINE:
      return 1;
    case UNIT_PREFERRED_BANDS.SPEAR_LINE:
      return 2;
    case UNIT_PREFERRED_BANDS.RANGED_REAR:
      return 3;
    case UNIT_PREFERRED_BANDS.PRIEST_REAR:
      return 4;
    default:
      return 5;
  }
}

function resolvePacketFormationColumns(preferredBand, unitCount) {
  const safeCount = Math.max(1, Math.floor(Number(unitCount) || 1));
  switch (normalizePreferredBand(preferredBand)) {
    case UNIT_PREFERRED_BANDS.SHIELD_FRONT:
      return Math.max(1, Math.min(3, safeCount));
    case UNIT_PREFERRED_BANDS.SWORD_LINE:
      return Math.max(1, Math.min(PACKET_FORMATION_MAX_COLUMNS, safeCount));
    case UNIT_PREFERRED_BANDS.SPEAR_LINE:
      return Math.max(1, Math.min(3, safeCount));
    case UNIT_PREFERRED_BANDS.RANGED_REAR:
    case UNIT_PREFERRED_BANDS.PRIEST_REAR:
      return Math.max(1, Math.min(3, safeCount));
    default:
      return Math.max(1, Math.min(PACKET_FORMATION_MAX_COLUMNS, safeCount));
  }
}

function resolvePacketLateralBias(barracksId) {
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  switch (normalizedBarracksId) {
    case "left":
      return -1;
    case "right":
      return 1;
    default:
      return 0;
  }
}

function resolvePacketDepthForAnchorState(anchorState, packetIndex, barracksPacketOrdinal) {
  const safePacketIndex = Math.max(0, Math.floor(Number(packetIndex) || 0));
  const safeBarracksOrdinal = Math.max(0, Math.floor(Number(barracksPacketOrdinal) || 0));
  const commandState = anchorState && anchorState.commandState;
  if (commandState === LANE_COMMAND_STATES.DEFEND || commandState === LANE_COMMAND_STATES.RETREAT)
    return safeBarracksOrdinal * PACKET_HOME_STAGING_DEPTH;
  return safePacketIndex * PACKET_INTER_PACKET_DEPTH;
}

function resolvePacketForwardOffsetForAnchorState(anchorState, packetDepth, packetFormationDepth) {
  const safePacketDepth = Math.max(0, Number(packetDepth) || 0);
  const safeFormationDepth = Math.max(0, Number(packetFormationDepth) || 0);
  const commandState = anchorState && anchorState.commandState;
  if (commandState === LANE_COMMAND_STATES.DEFEND)
    return safePacketDepth + safeFormationDepth;
  return -safePacketDepth;
}

function createSpawnPacketId(game, sourceLaneIndex, barracksId, prefix = "packet") {
  const nextPacketId = Math.max(1, Math.floor(Number(game && game.nextPacketId) || 1));
  if (game)
    game.nextPacketId = nextPacketId + 1;
  const safeLaneIndex = Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1;
  const safeBarracksId = normalizeBarracksSiteId(barracksId) || "center";
  return `${prefix}:${safeLaneIndex}:${safeBarracksId}:${nextPacketId}`;
}

function resolveUnitPacketGroupId(unit) {
  if (!isLaneControlledUnit(unit))
    return null;

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = Number.isInteger(unit && unit.targetLaneIndex)
    ? unit.targetLaneIndex
    : (Number.isInteger(unit && unit.laneId) ? unit.laneId : -1);
  if (DISABLE_RIGID_PACKET_FORMATIONS)
    return `lane_group:${sourceLaneIndex}:${targetLaneIndex}`;

  const explicitGroupId = String(unit && unit.groupId || "").trim();
  if (explicitGroupId !== "")
    return explicitGroupId;

  const sourceBarracksId = resolveUnitSourceBarracksId(unit) || "center";
  return `legacy_packet:${sourceLaneIndex}:${sourceBarracksId}:${targetLaneIndex}:${resolveSpawnSourceTypeFromUnit(unit) || "lane"}`;
}

function resolveUnitCombatRole(unit) {
  const explicitRole = normalizeCombatRole(unit && unit.combatRole);
  if (explicitRole)
    return explicitRole;

  const sourceTokens = [
    unit && unit.heroKey,
    unit && unit.rosterKey,
    unit && unit.role,
    unit && unit.archetypeKey,
    unit && unit.unitTypeKey,
    unit && unit.type,
  ]
    .filter(Boolean)
    .map((value) => String(value).trim().toLowerCase());
  const joined = sourceTokens.join("|");
  if (joined.includes("shield") || joined.includes("guardian"))
    return UNIT_COMBAT_ROLES.SHIELD;
  if (joined.includes("spear") || joined.includes("polearm") || joined.includes("halber") || joined.includes("lancer"))
    return UNIT_COMBAT_ROLES.SPEAR;
  if (joined.includes("priest") || joined.includes("cleric") || joined.includes("bishop") || joined.includes("support"))
    return UNIT_COMBAT_ROLES.PRIEST;
  if (joined.includes("mage") || joined.includes("wizard") || joined.includes("arcane") || joined.includes("thaum"))
    return UNIT_COMBAT_ROLES.MAGE;
  if (joined.includes("archer") || joined.includes("crossbow") || joined.includes("ranger") || joined.includes("ranged"))
    return UNIT_COMBAT_ROLES.ARCHER;
  return UNIT_COMBAT_ROLES.SWORD;
}

function resolveUnitSupportProfile(unitOrType) {
  const typeKey = typeof unitOrType === "string"
    ? unitOrType
    : (unitOrType && (unitOrType.type || unitOrType.unitTypeKey || unitOrType.catalogUnitKey || unitOrType.skinKey));
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(typeKey);
  const unitType = resolvedUnitTypeKey ? getUnitType(resolvedUnitTypeKey) : null;
  const specialProps = unitType && unitType.special_props && typeof unitType.special_props === "object"
    ? unitType.special_props
    : {};
  const combatRole = typeof unitOrType === "string"
    ? normalizeCombatRole(typeKey)
    : resolveUnitCombatRole(unitOrType);
  const supportRole = String(specialProps.supportRole || specialProps.support_role || "").trim().toLowerCase();
  const roleText = typeof unitOrType === "string"
    ? String(unitOrType || "")
    : [
        unitOrType && unitOrType.heroKey,
        unitOrType && unitOrType.rosterKey,
        unitOrType && unitOrType.type,
        unitOrType && unitOrType.unitTypeKey,
        unitOrType && unitOrType.catalogUnitKey,
      ]
        .filter(Boolean)
        .join("|");
  const joined = roleText.toLowerCase();
  const isHealer = supportRole === "healer"
    || supportRole === "heal"
    || (supportRole === "" && combatRole === UNIT_COMBAT_ROLES.PRIEST);
  let healAmount = Number(specialProps.healAmount ?? specialProps.heal_amount) || 0;
  if (healAmount <= 0 && isHealer) {
    if (joined.includes("bishop") || joined.includes("commander"))
      healAmount = 22;
    else if (joined.includes("high_priest"))
      healAmount = 17;
    else if (joined.includes("mounted_priest") || joined.includes("cleric"))
      healAmount = 8;
    else
      healAmount = 12;
  }

  return {
    isHealer,
    healAmount: Math.max(0, healAmount),
  };
}

function assignUnitSupportTarget(unit, targetDescriptor) {
  if (!unit || !targetDescriptor || !targetDescriptor.unitId)
    return false;

  unit.combatTarget = {
    unitId: targetDescriptor.unitId,
    kind: targetDescriptor.kind || "unit",
    laneIndex: Number.isInteger(targetDescriptor.laneIndex) ? targetDescriptor.laneIndex : null,
    padId: targetDescriptor.padId || null,
  };
  unit.combatTargetId = targetDescriptor.unitId;
  unit.currentTargetId = targetDescriptor.unitId;
  unit.combatTargetLockedUntilTick = 0;
  return true;
}

function clearUnitSupportTarget(unit) {
  if (!unit)
    return;

  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.currentTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
}

function findFriendlyHealTarget(game, lane, healer, supportProfile = null) {
  if (!game || !lane || !healer || healer.hp <= 0)
    return null;

  const resolvedSupportProfile = supportProfile || resolveUnitSupportProfile(healer);
  if (!resolvedSupportProfile.isHealer)
    return null;

  const healerAllegiance = resolveUnitAllegianceKey(game, lane, healer);
  const healRange = getUnitAttackRange(healer.type);
  const healerPacketId = resolveUnitPacketGroupId(healer);
  const healAmount = Math.max(1, Math.round(resolvedSupportProfile.healAmount || 1));
  let best = null;
  let bestScore = -Infinity;

  for (const candidate of lane.units) {
    if (!candidate || candidate.hp <= 0 || candidate.id === healer.id)
      continue;
    if (resolveUnitAllegianceKey(game, lane, candidate) !== healerAllegiance)
      continue;

    const maxHp = Math.max(1, Number(candidate.maxHp) || Number(candidate.hp) || 1);
    const currentHp = Math.max(0, Number(candidate.hp) || 0);
    const missingHp = maxHp - currentHp;
    if (missingHp < Math.max(2, healAmount * 0.35))
      continue;

    const healthRatio = currentHp / maxHp;
    const distance = dist2D(healer, candidate);
    const samePacket = healerPacketId && healerPacketId === resolveUnitPacketGroupId(candidate) ? 1 : 0;
    const inRange = distance <= healRange + CONTACT_SLOT_TOLERANCE ? 1 : 0;
    const heroBonus = candidate.isHero ? 1 : 0;
    const score = ((1 - healthRatio) * 1000)
      + Math.min(250, missingHp * 4)
      + (samePacket * 75)
      + (inRange * 60)
      + (heroBonus * 20)
      - (distance * 12);

    if (!best || score > bestScore) {
      best = candidate;
      bestScore = score;
    }
  }

  return best;
}

function resolveUnitPreferredBand(unit) {
  const explicitBand = normalizePreferredBand(unit && unit.preferredBand);
  if (explicitBand)
    return explicitBand;
  return resolvePreferredBandForCombatRole(resolveUnitCombatRole(unit));
}

function resolvePacketUnitSortKey(unit) {
  const bandSort = getPacketBandSortIndex(resolveUnitPreferredBand(unit));
  return `${String(bandSort).padStart(2, "0")}:${resolveLaneControlledUnitSortKey(unit)}`;
}

function buildPersistentPacketOrder(ownerLane, packetEntries) {
  const entries = Array.isArray(packetEntries) ? packetEntries.slice() : [];
  if (!ownerLane || entries.length <= 0) {
    if (ownerLane)
      ownerLane.packetOrder = [];
    return [];
  }

  const entryById = new Map();
  for (const entry of entries) {
    const packetId = entry && entry.packetId;
    if (packetId)
      entryById.set(packetId, entry);
  }

  const ordered = [];
  const seen = new Set();
  const previousOrder = Array.isArray(ownerLane.packetOrder)
    ? ownerLane.packetOrder
    : [];
  for (const packetId of previousOrder) {
    const entry = entryById.get(packetId);
    if (!entry)
      continue;
    ordered.push(entry);
    seen.add(packetId);
  }

  const newcomers = entries
    .filter((entry) => entry && entry.packetId && !seen.has(entry.packetId))
    .sort((left, right) => {
      const leftOrdinal = Number(left && left.groupSpawnOrdinal);
      const rightOrdinal = Number(right && right.groupSpawnOrdinal);
      if (Number.isFinite(leftOrdinal) && Number.isFinite(rightOrdinal) && leftOrdinal !== rightOrdinal)
        return leftOrdinal - rightOrdinal;
      const leftTick = Number(left && left.groupSpawnTick);
      const rightTick = Number(right && right.groupSpawnTick);
      if (Number.isFinite(leftTick) && Number.isFinite(rightTick) && leftTick !== rightTick)
        return leftTick - rightTick;
      return String(left && left.packetId || "").localeCompare(String(right && right.packetId || ""));
    });
  for (const entry of newcomers) {
    seen.add(entry.packetId);
    ordered.push(entry);
  }

  ownerLane.packetOrder = ordered.map((entry) => entry.packetId);
  return ordered;
}

function buildPersistentLanePacketUnitRoster(ownerLane, packetId, packetEntries) {
  const entries = Array.isArray(packetEntries) ? packetEntries.slice() : [];
  if (!ownerLane || !packetId || entries.length <= 0)
    return [];

  if (!ownerLane.packetUnitOrder || typeof ownerLane.packetUnitOrder !== "object")
    ownerLane.packetUnitOrder = {};

  const entryById = new Map();
  for (const entry of entries) {
    const unitId = entry && entry.unit && entry.unit.id;
    if (unitId)
      entryById.set(unitId, entry);
  }

  const ordered = [];
  const seen = new Set();
  const previousOrder = Array.isArray(ownerLane.packetUnitOrder[packetId])
    ? ownerLane.packetUnitOrder[packetId]
    : [];
  for (const unitId of previousOrder) {
    const entry = entryById.get(unitId);
    if (!entry)
      continue;
    ordered.push(entry);
    seen.add(unitId);
  }

  const newcomers = entries
    .filter((entry) => {
      const unitId = entry && entry.unit && entry.unit.id;
      return unitId && !seen.has(unitId);
    })
    .sort((left, right) =>
      resolvePacketUnitSortKey(left.unit).localeCompare(resolvePacketUnitSortKey(right.unit)));
  for (const entry of newcomers) {
    const unitId = entry && entry.unit && entry.unit.id;
    if (unitId)
      seen.add(unitId);
    ordered.push(entry);
  }

  ownerLane.packetUnitOrder[packetId] = ordered.map((entry) => entry.unit.id);
  return ordered;
}

function resolveLooseHoldDepthBias(preferredBand) {
  switch (normalizePreferredBand(preferredBand)) {
    case UNIT_PREFERRED_BANDS.SHIELD_FRONT:
      return -0.6;
    case UNIT_PREFERRED_BANDS.SWORD_LINE:
      return -0.25;
    case UNIT_PREFERRED_BANDS.SPEAR_LINE:
      return 0.1;
    case UNIT_PREFERRED_BANDS.RANGED_REAR:
      return 0.45;
    case UNIT_PREFERRED_BANDS.PRIEST_REAR:
      return 0.8;
    default:
      return 0;
  }
}

function buildLooseHoldPlacement(unitIndex, unit, totalUnits = 1) {
  const preferredBand = resolveUnitPreferredBand(unit);
  const safeTotalUnits = Math.max(1, Math.floor(Number(totalUnits) || 1));
  const columns = Math.max(3, Math.min(9, safeTotalUnits));
  const row = Math.floor(unitIndex / columns);
  const column = unitIndex % columns;
  const lateralIndex = resolveCenteredFormationOffset(column, columns);
  const lateralDistance = lateralIndex * ROUTE_FORMATION_COLUMN_SPACING * 1.15;
  const depthDistance = (row * ROUTE_FORMATION_ROW_SPACING * 0.78) + resolveLooseHoldDepthBias(preferredBand);
  return {
    band: preferredBand,
    row,
    column,
    lateralDistance,
    depthDistance,
  };
}

function buildPacketFormationLayout(orderedEntries) {
  const safeEntries = Array.isArray(orderedEntries) ? orderedEntries : [];
  const placementByUnitId = new Map();

  if (DISABLE_RIGID_PACKET_FORMATIONS) {
    const totalUnitCount = safeEntries.length;
    for (let unitIndex = 0; unitIndex < safeEntries.length; unitIndex += 1) {
      const entry = safeEntries[unitIndex];
      const unit = entry && entry.unit;
      const unitId = unit && unit.id;
      if (!unitId)
        continue;

      placementByUnitId.set(unitId, buildLooseHoldPlacement(unitIndex, unit, totalUnitCount));
    }

    return {
      placementByUnitId,
      maxDepthDistance: ROUTE_FORMATION_ROW_SPACING,
    };
  }

  const bandEntriesByBand = new Map();
  let maxDepthDistance = 0;

  for (const entry of safeEntries) {
    const unit = entry && entry.unit;
    const unitId = unit && unit.id;
    if (!unitId)
      continue;

    const preferredBand = resolveUnitPreferredBand(unit);
    if (!bandEntriesByBand.has(preferredBand))
      bandEntriesByBand.set(preferredBand, []);
    bandEntriesByBand.get(preferredBand).push(entry);
  }

  for (const [preferredBand, bandEntries] of bandEntriesByBand.entries()) {
    const columns = resolvePacketFormationColumns(preferredBand, bandEntries.length);
    const bandDepth = getPacketBandSortIndex(preferredBand);
    for (let bandIndex = 0; bandIndex < bandEntries.length; bandIndex += 1) {
      const entry = bandEntries[bandIndex];
      const unitId = entry && entry.unit && entry.unit.id;
      if (!unitId)
        continue;

      const row = Math.floor(bandIndex / columns);
      const column = bandIndex % columns;
      const lateralIndex = resolveCenteredFormationOffset(column, columns);
      const lateralDistance = lateralIndex * PACKET_ROLE_COLUMN_SPACING;
      const depthDistance = ((bandDepth * PACKET_ROLE_ROW_SPACING) + (row * PACKET_ROLE_ROW_SPACING));
      if (depthDistance > maxDepthDistance)
        maxDepthDistance = depthDistance;
      placementByUnitId.set(unitId, {
        band: preferredBand,
        row,
        column,
        lateralDistance,
        depthDistance,
      });
    }
  }

  return {
    placementByUnitId,
    maxDepthDistance,
  };
}

function buildPacketFormationSlot(packetAnchorState, unit, localIndex, formationPlacement) {
  const preferredBand = normalizePreferredBand(formationPlacement && formationPlacement.band) || resolveUnitPreferredBand(unit);
  const row = Number.isInteger(formationPlacement && formationPlacement.row) ? formationPlacement.row : 0;
  const column = Number.isInteger(formationPlacement && formationPlacement.column) ? formationPlacement.column : 0;
  const lateralDistance = Number.isFinite(Number(formationPlacement && formationPlacement.lateralDistance))
    ? Number(formationPlacement.lateralDistance)
    : 0;
  const depthDistance = Number.isFinite(Number(formationPlacement && formationPlacement.depthDistance))
    ? Number(formationPlacement.depthDistance)
    : getPacketBandSortIndex(preferredBand) * PACKET_ROLE_ROW_SPACING;
  const anchorX = Number(packetAnchorState && packetAnchorState.groupCenterX) || 0;
  const anchorY = Number(packetAnchorState && packetAnchorState.groupCenterY) || 0;
  const facing = packetAnchorState && packetAnchorState.facing ? packetAnchorState.facing : { x: 0, y: -1 };
  const lateral = packetAnchorState && packetAnchorState.lateral ? packetAnchorState.lateral : perpendicular2D(facing);
  return {
    slotIndex: Math.max(0, Math.floor(Number(localIndex) || 0)),
    band: preferredBand,
    row,
    column,
    x: anchorX + (lateral.x * lateralDistance) - (facing.x * depthDistance),
    y: anchorY + (lateral.y * lateralDistance) - (facing.y * depthDistance),
  };
}

function resolvePacketMovementMode(unitEntries) {
  const entries = Array.isArray(unitEntries) ? unitEntries : [];
  if (entries.some((entry) => entry && entry.unit && entry.unit.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE))
    return UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
  if (entries.some((entry) => entry && entry.unit && entry.unit.movementMode === UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR))
    return UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR;
  if (entries.some((entry) => entry && entry.unit && entry.unit.movementMode === UNIT_MOVEMENT_MODES.FORMATION_JOIN))
    return UNIT_MOVEMENT_MODES.FORMATION_JOIN;
  return UNIT_MOVEMENT_MODES.LANE_TRAVEL;
}

function computePacketCohesionRadius(packetState, packetSlots) {
  const slots = Array.isArray(packetSlots) ? packetSlots : [];
  const centerX = Number(packetState && packetState.groupCenterX) || 0;
  const centerY = Number(packetState && packetState.groupCenterY) || 0;
  let maxDistance = ROUTE_FORMATION_COLUMN_SPACING;
  for (const slot of slots) {
    const dx = (Number(slot && slot.x) || 0) - centerX;
    const dy = (Number(slot && slot.y) || 0) - centerY;
    maxDistance = Math.max(maxDistance, Math.sqrt((dx * dx) + (dy * dy)));
  }
  return maxDistance + ROUTE_FORMATION_ROW_SPACING;
}

function resolveUnitLeashFromGroupCenter(unit, cohesionRadius) {
  const preferredBand = resolveUnitPreferredBand(unit);
  const baseRadius = Math.max(cohesionRadius, ROUTE_FORMATION_ROW_SPACING);
  switch (preferredBand) {
    case UNIT_PREFERRED_BANDS.PRIEST_REAR:
      return Math.max(baseRadius + 0.75, 3.25);
    case UNIT_PREFERRED_BANDS.RANGED_REAR:
      return Math.max(baseRadius + 1.25, 4.0);
    default:
      return Math.max(baseRadius + 2.0, 5.0);
  }
}

function buildPacketWaypointTarget(anchorState) {
  return anchorState
    ? {
        kind: anchorState.anchorKind || null,
        laneIndex: Number.isInteger(anchorState.containerLaneIndex) ? anchorState.containerLaneIndex : null,
        x: Number(anchorState.anchorX) || 0,
        y: Number(anchorState.anchorY) || 0,
      }
    : null;
}

function shouldKeepUnitAfterLaneDefeat(lane, unit) {
  if (!unit)
    return false;
  if (unit.isDefender)
    return false;
  if (isScheduledWaveUnit(unit))
    return false;
  return Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0 && unit.sourceLaneIndex !== lane.laneIndex;
}

function laneHasOccupyingForces(lane) {
  if (!lane)
    return false;
  const activeUnits = Array.isArray(lane.units)
    && lane.units.some((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit) && unit.hp > 0);
  if (activeUnits)
    return true;
  return Array.isArray(lane.spawnQueue)
    && lane.spawnQueue.some((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit));
}

function requeueLaneControlledUnit(targetLane, unit) {
  if (!targetLane || !unit)
    return false;
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  unit.targetLaneIndex = targetLane.laneIndex;
  unit.laneId = targetLane.laneIndex;
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.regroupUntilTick = 0;
  unit.currentSlotIndex = null;
  unit.stance = null;
  unit.pathContractType = null;
  unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  unit.routeType = null;
  unit.routeStartNode = null;
  unit.routeTargetNode = null;
  unit.routeSegments = null;
  unit.routeSegmentIndex = 0;
  unit.segmentProgress = 0;
  unit.currentSegment = null;
  unit.routeWorldX = null;
  unit.routeWorldY = null;
  unit.routeLateralOffset = 0;
  unit.routeLongitudinalOffset = 0;
  unit.spawnIndex = Math.max(0, targetLane.spawnQueue.length);
  unit.spawnLogicalPos = resolveSpawnLogicalPosition(spawnSourceType, unit.spawnIndex);
  targetLane.spawnQueue.push(unit);
  return true;
}

function syncLaneCommandAssignments(game) {
  if (!game || !Array.isArray(game.lanes))
    return;

  normalizeLegacyDefenderUnits(game);

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    lane.formationSlots = [];
    lane.assignedUnits = [];
    lane.formationUnitOrder = Array.isArray(lane.formationUnitOrder)
      ? lane.formationUnitOrder
      : [];
    lane.packetOrder = Array.isArray(lane.packetOrder)
      ? lane.packetOrder
      : [];
    lane.packetUnitOrder = lane.packetUnitOrder && typeof lane.packetUnitOrder === "object"
      ? lane.packetUnitOrder
      : {};
    lane.packetStates = [];
    lane.insideGateAnchor = null;
    lane.outsideGateAnchor = null;
    lane.enemyCoreAnchor = null;
  }

  for (const lane of game.lanes) {
    if (!lane || !Array.isArray(lane.units))
      continue;

    for (let unitIndex = lane.units.length - 1; unitIndex >= 0; unitIndex -= 1) {
      const unit = lane.units[unitIndex];
      if (!isLaneControlledUnit(unit))
        continue;

      const ownerLane = getLaneCommandOwnerLane(game, unit);
      if (!ownerLane)
        continue;

      const targetLaneIndex = resolveLaneControlledUnitContainerLaneIndex(game, ownerLane, unit);
      const targetLane = getLaneByIndex(game, targetLaneIndex) || lane;
      const routeObjectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, ownerLane);
      unit.targetLaneIndex = targetLane.laneIndex;
      unit.laneId = targetLane.laneIndex;
      unit.objectiveLaneIndex = routeObjectiveLaneIndex;

      const currentPathId = buildRoutePathId(unit.routeSegments);
      const sourceLaneIndex = Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
      const sourceBarracksId = resolveUnitSourceBarracksId(unit);
      const sourceRouteNodeId = getBarracksRouteStartNodeId(sourceLaneIndex, sourceBarracksId);
      const sourceCoreNodeId = getLaneNodeId(sourceLaneIndex);
      const expectedSpawnEntrySegment = sourceRouteNodeId && sourceCoreNodeId
        ? `${sourceRouteNodeId}_${sourceCoreNodeId}`
        : null;
      const currentRouteUsesBattlefieldCore = !!(
        Array.isArray(unit.routeSegments)
        && unit.routeSegments.length > 0
        && expectedSpawnEntrySegment
        && unit.routeSegments[0] !== expectedSpawnEntrySegment
      );
      const desiredTargetNodeId = getLaneNodeId(routeObjectiveLaneIndex);

      let routeContract = null;
      if (lane === targetLane
          && currentRouteUsesBattlefieldCore
          && desiredTargetNodeId
          && unit.routeTargetNode === desiredTargetNodeId
          && currentPathId) {
        routeContract = {
          ok: true,
          pathId: currentPathId,
          objectiveLaneIndex: routeObjectiveLaneIndex,
          routeSegments: unit.routeSegments,
        };
      } else {
        routeContract = resolveRouteContractForUnit(game, targetLane, unit);
      }
      const desiredPathId = routeContract && routeContract.ok ? routeContract.pathId : null;
      if (routeContract && routeContract.ok
          && Array.isArray(unit.routeSegments) && unit.routeSegments.length > 0
          && (lane !== targetLane || desiredPathId !== currentPathId || unit.objectiveLaneIndex !== routeObjectiveLaneIndex)) {
        routeContract = resolveRedirectRouteContractForExistingLaneControlledUnit(game, lane, targetLane, unit);
      }
      const resolvedPathId = routeContract && routeContract.ok ? routeContract.pathId : null;
      if (routeContract && routeContract.ok
          && (lane !== targetLane || resolvedPathId !== currentPathId || unit.objectiveLaneIndex !== routeObjectiveLaneIndex)) {
        applyRouteContractToExistingUnit(unit, routeContract, {
          x: Number(unit.posX),
          y: Number(unit.posY),
        });
      }

      if (lane !== targetLane) {
        lane.units.splice(unitIndex, 1);
        targetLane.units.push(unit);
      }
      applyCanonicalUnitMirrors(game, targetLane, unit);
    }

    if (!Array.isArray(lane.spawnQueue))
      continue;

    for (let queueIndex = lane.spawnQueue.length - 1; queueIndex >= 0; queueIndex -= 1) {
      const unit = lane.spawnQueue[queueIndex];
      if (!isLaneControlledUnit(unit))
        continue;

      const ownerLane = getLaneCommandOwnerLane(game, unit);
      if (!ownerLane)
        continue;

      const targetLaneIndex = resolveLaneCommandContainerLaneIndex(game, ownerLane);
      const targetLane = getLaneByIndex(game, targetLaneIndex) || lane;
      unit.targetLaneIndex = targetLane.laneIndex;
      unit.laneId = targetLane.laneIndex;
      unit.objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, ownerLane);

      if (lane !== targetLane) {
        lane.spawnQueue.splice(queueIndex, 1);
        requeueLaneControlledUnit(targetLane, unit);
      } else {
        unit.spawnIndex = queueIndex;
        unit.spawnLogicalPos = resolveSpawnLogicalPosition(resolveSpawnSourceTypeFromUnit(unit), queueIndex);
      }
      applyCanonicalUnitMirrors(game, targetLane, unit);
    }
  }

  for (const ownerLane of game.lanes) {
    if (!ownerLane)
      continue;

    const anchorState = sampleLaneCommandAnchor(game, ownerLane);
    const activePacketIds = new Set();
    ownerLane.combatEnabled = !!(anchorState && anchorState.combatEnabled);
    ownerLane.engagementRadius = anchorState ? anchorState.engagementRadius : 0;
    ownerLane.formationAnchorProgress = anchorState ? anchorState.anchorProgress : 0;
    ownerLane.commandAnchorProgress = ownerLane.formationAnchorProgress;
    ownerLane.formationAnchor = anchorState
      ? { x: anchorState.anchorX, y: anchorState.anchorY, laneIndex: anchorState.containerLaneIndex }
      : null;
    ownerLane.formationFacing = anchorState
      ? { x: anchorState.facing.x, y: anchorState.facing.y }
      : null;
    ownerLane.insideGateAnchor = anchorState && anchorState.insideGateAnchor
      ? { x: anchorState.insideGateAnchor.x, y: anchorState.insideGateAnchor.y }
      : null;
    ownerLane.outsideGateAnchor = anchorState && anchorState.outsideGateAnchor
      ? { x: anchorState.outsideGateAnchor.x, y: anchorState.outsideGateAnchor.y }
      : null;
    ownerLane.enemyCoreAnchor = anchorState && anchorState.enemyCoreAnchor
      ? { x: anchorState.enemyCoreAnchor.x, y: anchorState.enemyCoreAnchor.y }
      : null;

    const packetEntriesById = new Map();
    for (const lane of game.lanes) {
      for (const unit of lane.units || []) {
        if (!isLaneControlledUnit(unit) || unit.sourceLaneIndex !== ownerLane.laneIndex || unit.hp <= 0)
          continue;

        const packetId = resolveUnitPacketGroupId(unit);
        if (!packetId)
          continue;
        unit.groupId = packetId;
        unit.combatRole = resolveUnitCombatRole(unit);
        unit.preferredBand = resolveUnitPreferredBand(unit);

        let packetEntry = packetEntriesById.get(packetId);
        if (!packetEntry) {
          packetEntry = {
            packetId,
            sourceLaneIndex: ownerLane.laneIndex,
            sourceBarracksId: resolveUnitSourceBarracksId(unit),
            groupSpawnTick: Number.isFinite(Number(unit.groupSpawnTick)) ? Number(unit.groupSpawnTick) : 0,
            groupSpawnOrdinal: Number.isFinite(Number(unit.groupSpawnOrdinal)) ? Number(unit.groupSpawnOrdinal) : null,
            entries: [],
          };
          packetEntriesById.set(packetId, packetEntry);
        }
        packetEntry.entries.push({ lane, unit });
      }
    }

    const orderedPackets = buildPersistentPacketOrder(ownerLane, Array.from(packetEntriesById.values()));
    const packetDepthOrdinalByBarracks = new Map();
    for (let packetIndex = 0; packetIndex < orderedPackets.length; packetIndex += 1) {
      const packetEntry = orderedPackets[packetIndex];
      if (!packetEntry || !packetEntry.packetId)
        continue;

      activePacketIds.add(packetEntry.packetId);
      const orderedPacketEntries = buildPersistentLanePacketUnitRoster(
        ownerLane,
        packetEntry.packetId,
        packetEntry.entries
      );
      const packetFormationLayout = buildPacketFormationLayout(orderedPacketEntries);
      const packetLateralBias = DISABLE_RIGID_PACKET_FORMATIONS
        ? 0
        : resolvePacketLateralBias(packetEntry.sourceBarracksId);
      const packetDepthKey = normalizeBarracksSiteId(packetEntry.sourceBarracksId) || "center";
      const barracksPacketOrdinal = packetDepthOrdinalByBarracks.get(packetDepthKey) || 0;
      packetDepthOrdinalByBarracks.set(packetDepthKey, barracksPacketOrdinal + 1);
      const packetDepth = DISABLE_RIGID_PACKET_FORMATIONS
        ? 0
        : resolvePacketDepthForAnchorState(anchorState, packetIndex, barracksPacketOrdinal);
      const packetForwardOffset = DISABLE_RIGID_PACKET_FORMATIONS
        ? 0
        : resolvePacketForwardOffsetForAnchorState(
          anchorState,
          packetDepth,
          packetFormationLayout.maxDepthDistance
        );
      const groupCenterX = anchorState
        ? Number(anchorState.anchorX) + ((Number(anchorState.lateral.x) || 0) * packetLateralBias * PACKET_BARRACKS_LATERAL_SPACING)
          + ((Number(anchorState.facing.x) || 0) * packetForwardOffset)
        : 0;
      const groupCenterY = anchorState
        ? Number(anchorState.anchorY) + ((Number(anchorState.lateral.y) || 0) * packetLateralBias * PACKET_BARRACKS_LATERAL_SPACING)
          + ((Number(anchorState.facing.y) || 0) * packetForwardOffset)
        : 0;

      const packetState = {
        groupId: packetEntry.packetId,
        laneId: anchorState && Number.isInteger(anchorState.containerLaneIndex)
          ? anchorState.containerLaneIndex
          : ownerLane.laneIndex,
        sourceLaneIndex: ownerLane.laneIndex,
        sourceBarracksId: packetEntry.sourceBarracksId || null,
        stance: anchorState ? anchorState.commandState : getLaneCommandState(ownerLane),
        currentWaypointTarget: buildPacketWaypointTarget(anchorState),
        groupCenterX,
        groupCenterY,
        facing: anchorState ? anchorState.facing : { x: 0, y: -1 },
        lateral: anchorState ? anchorState.lateral : { x: 1, y: 0 },
        cohesionRadius: ROUTE_FORMATION_ROW_SPACING,
        movementMode: resolvePacketMovementMode(orderedPacketEntries),
        packetIndex,
        assignedUnits: [],
        formationSlots: [],
      };

      for (let unitIndex = 0; unitIndex < orderedPacketEntries.length; unitIndex += 1) {
        const entry = orderedPacketEntries[unitIndex];
        const formationPlacement = packetFormationLayout.placementByUnitId.get(entry.unit.id);
        const slot = buildPacketFormationSlot(packetState, entry.unit, unitIndex, formationPlacement);
        packetState.assignedUnits.push(entry.unit.id);
        packetState.formationSlots.push({
          slotIndex: unitIndex,
          band: slot.band,
          x: Number(slot.x.toFixed(3)),
          y: Number(slot.y.toFixed(3)),
          unitId: entry.unit.id,
        });
      }

      packetState.cohesionRadius = computePacketCohesionRadius(packetState, packetState.formationSlots);
      ownerLane.packetStates.push({
        groupId: packetState.groupId,
        laneId: packetState.laneId,
        sourceLaneIndex: packetState.sourceLaneIndex,
        sourceBarracksId: packetState.sourceBarracksId,
        stance: packetState.stance,
        currentWaypointTarget: packetState.currentWaypointTarget,
        groupCenter: { x: Number(packetState.groupCenterX.toFixed(3)), y: Number(packetState.groupCenterY.toFixed(3)) },
        cohesionRadius: Number(packetState.cohesionRadius.toFixed(3)),
        movementMode: packetState.movementMode,
        packetIndex: packetState.packetIndex,
        assignedUnits: packetState.assignedUnits.slice(),
        formationSlots: packetState.formationSlots.slice(),
      });

        for (let unitIndex = 0; unitIndex < orderedPacketEntries.length; unitIndex += 1) {
          const entry = orderedPacketEntries[unitIndex];
          const slot = packetState.formationSlots[unitIndex];
          const leashFromGroupCenter = resolveUnitLeashFromGroupCenter(entry.unit, packetState.cohesionRadius);
          entry.unit.currentSlotIndex = DISABLE_RIGID_PACKET_FORMATIONS ? null : unitIndex;
          entry.unit.anchorTargetX = slot.x;
          entry.unit.anchorTargetY = slot.y;
        entry.unit.anchorTargetProgress = anchorState ? anchorState.anchorProgress : 0;
        entry.unit.anchorFacingX = packetState.facing.x;
        entry.unit.anchorFacingY = packetState.facing.y;
        entry.unit.anchorLateralX = packetState.lateral.x;
        entry.unit.anchorLateralY = packetState.lateral.y;
        entry.unit.commandState = packetState.stance;
        entry.unit.groupCenterX = packetState.groupCenterX;
        entry.unit.groupCenterY = packetState.groupCenterY;
        entry.unit.cohesionRadius = packetState.cohesionRadius;
        entry.unit.leashFromGroupCenter = leashFromGroupCenter;
        entry.unit.groupLaneId = packetState.laneId;
        entry.unit.packetIndex = packetState.packetIndex;
        entry.unit.currentWaypointTargetX = packetState.currentWaypointTarget ? packetState.currentWaypointTarget.x : null;
        entry.unit.currentWaypointTargetY = packetState.currentWaypointTarget ? packetState.currentWaypointTarget.y : null;
        entry.unit.currentWaypointTargetKind = packetState.currentWaypointTarget ? packetState.currentWaypointTarget.kind : null;
        entry.unit.canEngage = !!(anchorState && anchorState.combatEnabled);
        entry.unit.combatLeashRadius = anchorState
          ? Math.max(anchorState.engagementRadius, leashFromGroupCenter)
          : leashFromGroupCenter;
        ownerLane.assignedUnits.push(entry.unit.id);
        ownerLane.formationSlots.push({
          slotIndex: ownerLane.formationSlots.length,
          x: Number(slot.x.toFixed(3)),
          y: Number(slot.y.toFixed(3)),
          unitId: entry.unit.id,
        });
        applyCanonicalUnitMirrors(game, entry.lane, entry.unit);
      }
    }

    ownerLane.formationUnitOrder = ownerLane.assignedUnits.slice();
    for (const packetId of Object.keys(ownerLane.packetUnitOrder || {})) {
      if (!activePacketIds.has(packetId))
        delete ownerLane.packetUnitOrder[packetId];
    }
  }
}

// Unit and tower definitions are DB-driven via unitTypes.js.
// These empty objects are kept for backward-compat exports only.
const UNIT_DEFS  = {};
const TOWER_DEFS = {};

const BARRACKS_COST_BASE = 100;
const BARRACKS_REQ_INCOME_BASE = 8;
const BARRACKS_ROSTER_REFUND_PCT = 70;

const FORTRESS_BUILD_STATES = Object.freeze({
  locked: "locked",
  availableToBuild: "available_to_build",
  built: "built",
  upgradeAvailable: "upgrade_available",
  maxTier: "max_tier",
});

function createFortressPadDef(
  padId,
  buildingType,
  displayName,
  gridX,
  gridY,
  options = {}
) {
  return Object.freeze({
    padId,
    buildingType,
    displayName,
    gridX,
    gridY,
    combatOffsetX: Number.isFinite(Number(options.combatOffsetX))
      ? Number(options.combatOffsetX)
      : null,
    combatOffsetY: Number.isFinite(Number(options.combatOffsetY))
      ? Number(options.combatOffsetY)
      : null,
  });
}

const FORTRESS_WALL_PAD_LAYOUT = Object.freeze([
  Object.freeze({ key: "front_left_01", displayName: "Front Left Wall 01", gridX: -2, gridY: 15, combatOffsetX: -4.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_02", displayName: "Front Left Wall 02", gridX: -1, gridY: 15, combatOffsetX: -3.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_03", displayName: "Front Left Wall 03", gridX: 0, gridY: 15, combatOffsetX: -2.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_04", displayName: "Front Left Wall 04", gridX: 1, gridY: 15, combatOffsetX: -1.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_06", displayName: "Front Left Wall 06", gridX: 2, gridY: 15, combatOffsetX: 0.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_left_07", displayName: "Front Left Wall 07", gridX: 3, gridY: 15, combatOffsetX: 1.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_right_01", displayName: "Front Right Wall 01", gridX: 4, gridY: 15, combatOffsetX: 2.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "front_right_02", displayName: "Front Right Wall 02", gridX: 5, gridY: 15, combatOffsetX: 3.0, combatOffsetY: 11.2 }),
  Object.freeze({ key: "back_left_01", displayName: "Back Left Wall 01", gridX: -2, gridY: 29, combatOffsetX: -4.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_left_02", displayName: "Back Left Wall 02", gridX: -1, gridY: 29, combatOffsetX: -3.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_left_04", displayName: "Back Left Wall 04", gridX: 0, gridY: 29, combatOffsetX: -2.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_left_05", displayName: "Back Left Wall 05", gridX: 1, gridY: 29, combatOffsetX: -1.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_right_01", displayName: "Back Right Wall 01", gridX: 4, gridY: 29, combatOffsetX: 1.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_right_02", displayName: "Back Right Wall 02", gridX: 5, gridY: 29, combatOffsetX: 2.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "right_side_03", displayName: "Right Side Wall 03", gridX: 7, gridY: 17, combatOffsetX: 6.0, combatOffsetY: 9.0 }),
  Object.freeze({ key: "right_side_04_a", displayName: "Right Side Wall 04A", gridX: 7, gridY: 19, combatOffsetX: 6.0, combatOffsetY: 7.0 }),
  Object.freeze({ key: "right_side_04_b", displayName: "Right Side Wall 04B", gridX: 7, gridY: 21, combatOffsetX: 6.0, combatOffsetY: 5.0 }),
  Object.freeze({ key: "right_side_05", displayName: "Right Side Wall 05", gridX: 7, gridY: 23, combatOffsetX: 6.0, combatOffsetY: 3.0 }),
  Object.freeze({ key: "right_side_06", displayName: "Right Side Wall 06", gridX: 7, gridY: 25, combatOffsetX: 6.0, combatOffsetY: 1.0 }),
  Object.freeze({ key: "right_side_07", displayName: "Right Side Wall 07", gridX: 7, gridY: 27, combatOffsetX: 6.0, combatOffsetY: -1.0 }),
]);

const FORTRESS_GATE_PAD_LAYOUT = Object.freeze([
  Object.freeze({ key: "front", displayName: "Front Gate", gridX: 3, gridY: 16, combatOffsetX: 0.0, combatOffsetY: FRONT_GATE_COMBAT_OFFSET }),
  Object.freeze({ key: "left", displayName: "Left Gate", gridX: -3, gridY: 24, combatOffsetX: -6.0, combatOffsetY: 1.0 }),
  Object.freeze({ key: "right", displayName: "Right Gate", gridX: 8, gridY: 24, combatOffsetX: 6.5, combatOffsetY: 1.0 }),
  Object.freeze({ key: "rear", displayName: "Rear Gate", gridX: 3, gridY: 30, combatOffsetX: 0.0, combatOffsetY: -6.0 }),
]);

const FORTRESS_TURRET_PAD_LAYOUT = Object.freeze([
  Object.freeze({ key: "front_left", displayName: "Front Left Tower", gridX: -2, gridY: 13, combatOffsetX: -5.0, combatOffsetY: 13.5 }),
  Object.freeze({ key: "front_left_05", displayName: "Front Left Tower 05", gridX: 0, gridY: 12, combatOffsetX: -2.5, combatOffsetY: 14.5 }),
  Object.freeze({ key: "front_right", displayName: "Front Right Tower", gridX: 6, gridY: 13, combatOffsetX: 5.0, combatOffsetY: 13.5 }),
  Object.freeze({ key: "core_03", displayName: "Inner Tower 03", gridX: 1, gridY: 21, combatOffsetX: -2.5, combatOffsetY: 4.0 }),
  Object.freeze({ key: "core_04", displayName: "Inner Tower 04", gridX: 3, gridY: 21, combatOffsetX: 0.0, combatOffsetY: 4.0 }),
  Object.freeze({ key: "core_05", displayName: "Inner Tower 05", gridX: 5, gridY: 21, combatOffsetX: 2.5, combatOffsetY: 4.0 }),
  Object.freeze({ key: "front_gate_left", displayName: "Front Gate Tower Left", gridX: 1, gridY: 14, combatOffsetX: -2.5, combatOffsetY: 12.0 }),
  Object.freeze({ key: "front_gate_right", displayName: "Front Gate Tower Right", gridX: 5, gridY: 14, combatOffsetX: 2.5, combatOffsetY: 12.0 }),
  Object.freeze({ key: "back_left_03", displayName: "Back Left Tower 03", gridX: -2, gridY: 27, combatOffsetX: -5.0, combatOffsetY: -3.0 }),
  Object.freeze({ key: "back_left_06", displayName: "Back Left Tower 06", gridX: 0, gridY: 28, combatOffsetX: -2.5, combatOffsetY: -4.0 }),
  Object.freeze({ key: "back_left_07", displayName: "Back Left Tower 07", gridX: 2, gridY: 29, combatOffsetX: -0.5, combatOffsetY: -5.0 }),
  Object.freeze({ key: "back_right_03", displayName: "Back Right Tower 03", gridX: 6, gridY: 27, combatOffsetX: 5.0, combatOffsetY: -3.0 }),
  Object.freeze({ key: "rear_gate_left", displayName: "Rear Gate Tower Left", gridX: 1, gridY: 30, combatOffsetX: -2.5, combatOffsetY: -6.5 }),
  Object.freeze({ key: "rear_gate_right", displayName: "Rear Gate Tower Right", gridX: 5, gridY: 30, combatOffsetX: 2.5, combatOffsetY: -6.5 }),
  Object.freeze({ key: "right_side_05", displayName: "Right Side Tower 05", gridX: 8, gridY: 17, combatOffsetX: 7.4, combatOffsetY: 9.0 }),
  Object.freeze({ key: "right_side_06", displayName: "Right Side Tower 06", gridX: 8, gridY: 22, combatOffsetX: 7.4, combatOffsetY: 4.0 }),
  Object.freeze({ key: "right_side_07", displayName: "Right Side Tower 07", gridX: 8, gridY: 27, combatOffsetX: 7.4, combatOffsetY: -1.0 }),
]);

const FORTRESS_BUILDING_DEFS = Object.freeze({
  town_core: {
    displayName: "Civic",
    branchKey: "civic",
    branchLabel: "Civic Progression",
    tierDisplayNames: Object.freeze([null, "House", "Town Hall", "Keep", "Castle"]),
    maxTier: 4,
    startsBuilt: true,
    requiredTownCoreTier: 1,
    baseMaxHp: TEAM_HP_START,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 2, 4: 3 },
    upgradeCosts: {
      2: 70,
      3: 110,
      4: 165,
    },
  },
  barracks: {
    displayName: "Barracks",
    branchKey: "barracks",
    branchLabel: "Barracks",
    tierDisplayNames: Object.freeze([null, "Barracks"]),
    maxTier: 1,
    startsBuilt: true,
    requiredTownCoreTier: 1,
    baseMaxHp: 260,
  },
  blacksmith: {
    displayName: "Blacksmith",
    branchKey: "blacksmith",
    branchLabel: "Blacksmith",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 225,
    buildCost: 60,
    upgradeCosts: {
      2: 95,
      3: 145,
    },
  },
  archery_tower: {
    displayName: "Archery Tower",
    branchKey: "archery",
    branchLabel: "Archery Tower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 190,
    buildCost: 50,
    upgradeCosts: {
      2: 85,
      3: 130,
    },
  },
  temple: {
    displayName: "Temple",
    branchKey: "temple",
    branchLabel: "Temple",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 205,
    buildCost: 70,
    upgradeCosts: {
      2: 105,
      3: 160,
    },
  },
  wizard_tower: {
    displayName: "Wizard Tower",
    branchKey: "wizard",
    branchLabel: "Wizard Tower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 195,
    buildCost: 75,
    upgradeCosts: {
      2: 115,
      3: 170,
    },
  },
  market: {
    displayName: "Market",
    branchKey: "market",
    branchLabel: "Market",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    baseMaxHp: 200,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  stable: {
    displayName: "Stable",
    branchKey: "stable",
    branchLabel: "Stable",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 205,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  workshop: {
    displayName: "Workshop",
    branchKey: "workshop",
    branchLabel: "Workshop",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 205,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  library: {
    displayName: "Library",
    branchKey: "library",
    branchLabel: "Library",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 2,
    requiredTownCoreTierByTier: { 1: 2, 2: 3, 3: 4 },
    baseMaxHp: 200,
    buildCost: 60,
    upgradeCosts: {
      2: 100,
      3: 150,
    },
  },
  lumber_mill: {
    displayName: "Lumber Mill",
    branchKey: "lumber_mill",
    branchLabel: "Lumber Mill",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    requiresLumberMill: false,
    requiresTurretTier3: false,
    baseMaxHp: 210,
    buildCost: 50,
    upgradeCosts: {
      2: 80,
      3: 125,
    },
  },
  wall: {
    displayName: "Wall",
    branchKey: "wall",
    branchLabel: "Walls",
    progressionCategory: "Wall",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    dependencyRequirementsByTier: {
      2: Object.freeze([{ buildingType: "lumber_mill", minTier: 2 }]),
      3: Object.freeze([{ buildingType: "lumber_mill", minTier: 3 }]),
    },
    baseMaxHp: 180,
    buildCost: 20,
    upgradeCosts: {
      2: 40,
      3: 80,
    },
  },
  gate: {
    displayName: "Gate",
    branchKey: "gate",
    branchLabel: "Gates",
    progressionCategory: "Gate",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    dependencyRequirementsByTier: {
      2: Object.freeze([{ buildingType: "lumber_mill", minTier: 2 }]),
      3: Object.freeze([{ buildingType: "lumber_mill", minTier: 3 }]),
    },
    baseMaxHp: 260,
    buildCost: 20,
    upgradeCosts: {
      2: 40,
      3: 80,
    },
  },
  turret: {
    displayName: "Turret",
    branchKey: "base_tower",
    branchLabel: "Base Towers",
    progressionCategory: "BaseTower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    requiresLumberMill: true,
    requiresTurretTier3: false,
    dependencyRequirementsByTier: {
      1: Object.freeze([{ buildingType: "lumber_mill", minTier: 1 }]),
    },
    baseMaxHp: 210,
    buildCost: 40,
    upgradeCosts: {
      2: 80,
      3: 140,
    },
  },
  tower_archer: {
    displayName: "Archer Tower",
    branchKey: "archer_tower",
    branchLabel: "Archer Towers",
    progressionCategory: "ArcherTower",
    tierDisplayNames: Object.freeze([null, "Tier 1", "Tier 2", "Tier 3"]),
    maxTier: 3,
    startsBuilt: false,
    requiredTownCoreTier: 1,
    requiredTownCoreTierByTier: { 1: 1, 2: 1, 3: 1 },
    requiresLumberMill: false,
    requiresTurretTier3: true,
    dependencyRequirementsByTier: {
      1: Object.freeze([{ buildingType: "turret", minTier: 3 }]),
    },
    baseMaxHp: 220,
    buildCost: 180,
    upgradeCosts: {
      2: 260,
      3: 380,
    },
  },
});

const FORTRESS_PAD_DEFS = Object.freeze([
  createFortressPadDef("town_core_pad", "town_core", "Civic", 3, 25),
  createFortressPadDef("barracks_pad", "barracks", "Barracks", 7, 25),
  createFortressPadDef("blacksmith_pad", "blacksmith", "Blacksmith", 1, 20),
  createFortressPadDef("temple_pad", "temple", "Temple", 5, 20),
  createFortressPadDef("wizard_tower_pad", "wizard_tower", "Wizard Tower", 7, 20),
  createFortressPadDef("archery_tower_pad", "archery_tower", "Archery Tower", 9, 20),
  createFortressPadDef("market_pad", "market", "Market", 3, 20),
  createFortressPadDef("stable_pad", "stable", "Stable", 1, 15),
  createFortressPadDef("workshop_pad", "workshop", "Workshop", 3, 15),
  createFortressPadDef("library_pad", "library", "Library", 5, 15),
  createFortressPadDef("lumber_mill_pad", "lumber_mill", "Lumber Mill", 7, 15),
  ...FORTRESS_WALL_PAD_LAYOUT.map((slot) => createFortressPadDef(
    `wall_${slot.key}_pad`,
    "wall",
    slot.displayName,
    slot.gridX,
    slot.gridY,
    {
      combatOffsetX: slot.combatOffsetX,
      combatOffsetY: slot.combatOffsetY,
    }
  )),
  ...FORTRESS_GATE_PAD_LAYOUT.map((slot) => createFortressPadDef(
    `gate_${slot.key}_pad`,
    "gate",
    slot.displayName,
    slot.gridX,
    slot.gridY,
    {
      combatOffsetX: slot.combatOffsetX,
      combatOffsetY: slot.combatOffsetY,
    }
  )),
  ...FORTRESS_TURRET_PAD_LAYOUT.map((slot) => createFortressPadDef(
    `turret_${slot.key}_pad`,
    "turret",
    slot.displayName,
    slot.gridX,
    slot.gridY,
    {
      combatOffsetX: slot.combatOffsetX,
      combatOffsetY: slot.combatOffsetY,
    }
  )),
]);

const BARRACKS_ROLE_SORT_ORDER = Object.freeze({
  melee: 0,
  ranged: 1,
  support: 2,
  siege: 3,
});

// Spawn queue rows advance toward the castle as spawnIndex grows, so queue support first
// and melee last to produce the intended formation: melee front, ranged middle, support back.
const BARRACKS_SPAWN_ROLE_ORDER = Object.freeze(["support", "ranged", "melee", "siege"]);

const BARRACKS_SITE_DEFS = Object.freeze([
  {
    barracksId: "center",
    displayName: "Barracks Center",
    slot: "center",
    sortIndex: 0,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    legacyTierUnlock: 1,
  },
  {
    barracksId: "left",
    displayName: "Barracks Left",
    slot: "left",
    sortIndex: 1,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    legacyTierUnlock: 2,
  },
  {
    barracksId: "right",
    displayName: "Barracks Right",
    slot: "right",
    sortIndex: 2,
    startsBuilt: false,
    buildCost: BARRACKS_COST_BASE,
    legacyTierUnlock: 3,
  },
]);

const BARRACKS_ROSTER_DEFS = Object.freeze([
  { rosterKey: "militia", displayName: "Militia", role: "melee", branchKey: "infantry", branchLabel: "Infantry", productionBuildingType: "blacksmith", archetypeKey: "infantry_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "barracks", requiredBuildingTier: 1, tier: 1, sortIndex: 10 },
  { rosterKey: "spearman", displayName: "Spearman", role: "melee", branchKey: "polearm", branchLabel: "Polearm", productionBuildingType: "blacksmith", archetypeKey: "polearm_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 1, tier: 1, sortIndex: 20 },
  { rosterKey: "shieldman", displayName: "Shieldman", role: "melee", branchKey: "shield", branchLabel: "Shield", productionBuildingType: "blacksmith", archetypeKey: "shield_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 1, tier: 1, sortIndex: 30 },
  { rosterKey: "swordsman", displayName: "Swordsman", role: "melee", branchKey: "infantry", branchLabel: "Infantry", productionBuildingType: "blacksmith", archetypeKey: "infantry_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 2, tier: 2, sortIndex: 40 },
  { rosterKey: "halberdier", displayName: "Halberdier", role: "melee", branchKey: "polearm", branchLabel: "Polearm", productionBuildingType: "blacksmith", archetypeKey: "polearm_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 2, tier: 2, sortIndex: 50 },
  { rosterKey: "shield_guard", displayName: "Shield Guard", role: "melee", branchKey: "shield", branchLabel: "Shield", productionBuildingType: "blacksmith", archetypeKey: "shield_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 2, tier: 2, sortIndex: 60 },
  { rosterKey: "knight", displayName: "Knight", role: "melee", branchKey: "infantry", branchLabel: "Infantry", productionBuildingType: "blacksmith", archetypeKey: "infantry_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 3, tier: 3, sortIndex: 70 },
  { rosterKey: "lancer", displayName: "Lancer", role: "melee", branchKey: "polearm", branchLabel: "Polearm", productionBuildingType: "blacksmith", archetypeKey: "polearm_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 3, tier: 3, sortIndex: 80 },
  { rosterKey: "guardian", displayName: "Guardian", role: "melee", branchKey: "shield", branchLabel: "Shield", productionBuildingType: "blacksmith", archetypeKey: "shield_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "blacksmith", requiredBuildingTier: 3, tier: 3, sortIndex: 90 },
  { rosterKey: "cleric", displayName: "Cleric", role: "support", branchKey: "healing", branchLabel: "Temple", productionBuildingType: "temple", archetypeKey: "support_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "temple", requiredBuildingTier: 1, tier: 1, sortIndex: 110 },
  { rosterKey: "priest", displayName: "Priest", role: "support", branchKey: "healing", branchLabel: "Temple", productionBuildingType: "temple", archetypeKey: "support_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "temple", requiredBuildingTier: 2, tier: 2, sortIndex: 120 },
  { rosterKey: "high_priest", displayName: "High Priest", role: "support", branchKey: "healing", branchLabel: "Temple", productionBuildingType: "temple", archetypeKey: "support_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "temple", requiredBuildingTier: 3, tier: 3, sortIndex: 130 },
  { rosterKey: "mage", displayName: "Mage", role: "ranged", branchKey: "arcane", branchLabel: "Wizard Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 1, tier: 1, sortIndex: 140 },
  { rosterKey: "wizard", displayName: "Wizard", role: "ranged", branchKey: "arcane", branchLabel: "Wizard Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 2, tier: 2, sortIndex: 150 },
  { rosterKey: "thaumaturge", displayName: "Thaumaturge", role: "ranged", branchKey: "arcane", branchLabel: "Wizard Tower", productionBuildingType: "wizard_tower", archetypeKey: "arcane_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "wizard_tower", requiredBuildingTier: 3, tier: 3, sortIndex: 160 },
  { rosterKey: "archer", displayName: "Archer", role: "ranged", branchKey: "ranged", branchLabel: "Archery", productionBuildingType: "archery_tower", archetypeKey: "ranged_t1", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "archery_tower", requiredBuildingTier: 1, tier: 1, sortIndex: 170 },
  { rosterKey: "crossbowman", displayName: "Crossbowman", role: "ranged", branchKey: "ranged", branchLabel: "Archery", productionBuildingType: "archery_tower", archetypeKey: "ranged_t2", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "archery_tower", requiredBuildingTier: 2, tier: 2, sortIndex: 180 },
  { rosterKey: "ranger", displayName: "Ranger", role: "ranged", branchKey: "ranged", branchLabel: "Archery", productionBuildingType: "archery_tower", archetypeKey: "ranged_t3", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "archery_tower", requiredBuildingTier: 3, tier: 3, sortIndex: 190 },
]);

const HERO_ROSTER_DEFS = Object.freeze([
  { heroKey: "king", displayName: "King", role: "hero", roleLabel: "Hero", spawnRole: "melee", branchKey: "heroes", branchLabel: "Heroes", archetypeKey: "hero_king", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "town_core", requiredBuildingTier: 4, summonSourceBuildingType: "barracks", summonCost: 70, cooldownTicks: 90 * TICK_HZ, activeLimit: 1, heroVisualStyleKey: "regal_gold", sortIndex: 10 },
  { heroKey: "paladin", displayName: "Paladin", role: "hero", roleLabel: "Hero", spawnRole: "melee", branchKey: "heroes", branchLabel: "Heroes", archetypeKey: "hero_paladin", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "town_core", requiredBuildingTier: 4, summonSourceBuildingType: "barracks", summonCost: 60, cooldownTicks: 75 * TICK_HZ, activeLimit: 1, heroVisualStyleKey: "holy_silver", sortIndex: 20 },
  { heroKey: "bishop", displayName: "Bishop", role: "hero", roleLabel: "Hero", spawnRole: "support", branchKey: "heroes", branchLabel: "Heroes", archetypeKey: "hero_bishop", presentationKey: DEFAULT_FORT_PRESENTATION_KEY, unlockBuildingType: "town_core", requiredBuildingTier: 4, summonSourceBuildingType: "barracks", summonCost: 55, cooldownTicks: 65 * TICK_HZ, activeLimit: 1, heroVisualStyleKey: "radiant_bishop", sortIndex: 30 },
]);

const MARKET_ROSTER_DEFS = Object.freeze([
  {
    unitKey: "peasant",
    displayName: "Peasant",
    role: "economy",
    roleLabel: "Economy",
    branchKey: "market",
    branchLabel: "Market",
    productionBuildingType: "market",
    archetypeKey: "economy_t1",
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    unlockBuildingType: "market",
    requiredBuildingTier: 1,
    tier: 1,
    economyLapGold: 4,
    routeStartBuildingType: "town_core",
    routeEndBuildingType: "market",
    nextUnitKey: "settler",
    description: "Carries starter trade goods between the Town Core and Market.",
    sortIndex: 10,
  },
  {
    unitKey: "settler",
    displayName: "Settler",
    role: "economy",
    roleLabel: "Economy",
    branchKey: "market",
    branchLabel: "Market",
    productionBuildingType: "market",
    archetypeKey: "economy_t2",
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    unlockBuildingType: "market",
    requiredBuildingTier: 2,
    tier: 2,
    economyLapGold: 7,
    routeStartBuildingType: "town_core",
    routeEndBuildingType: "market",
    nextUnitKey: "trader",
    description: "Carries more goods and trade value than Peasants between the Town Core and Market.",
    sortIndex: 20,
  },
  {
    unitKey: "trader",
    displayName: "Trader",
    role: "economy",
    roleLabel: "Economy",
    branchKey: "market",
    branchLabel: "Market",
    productionBuildingType: "market",
    archetypeKey: "economy_t3",
    presentationKey: DEFAULT_FORT_PRESENTATION_KEY,
    unlockBuildingType: "market",
    requiredBuildingTier: 3,
    tier: 3,
    economyLapGold: 10,
    routeStartBuildingType: "town_core",
    routeEndBuildingType: "market",
    nextUnitKey: null,
    description: "Top-tier trade runner carrying the highest-value cargo between the Town Core and Market.",
    sortIndex: 30,
  },
]);

// ── Phase G: Ability system helpers ───────────────────────────────────────────

function resolveFortPresentationConfig(archetypeKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY, fallbackDisplayName = null) {
  const resolvedPresentationKey = presentationKey || DEFAULT_FORT_PRESENTATION_KEY;
  const catalogUnitKey = resolveFortCatalogUnitKey(archetypeKey, resolvedPresentationKey);
  const skinKey = resolveFortSkinKey(archetypeKey, resolvedPresentationKey);
  const portraitKey = resolveFortPortraitKey(archetypeKey, resolvedPresentationKey);
  const displayName = resolveFortDisplayName(archetypeKey, resolvedPresentationKey, fallbackDisplayName);
  return {
    archetypeKey,
    presentationKey: resolvedPresentationKey,
    catalogUnitKey,
    skinKey,
    portraitKey,
    displayName,
  };
}

function resolveGameplayCatalogUnitKey(unitKey, presentationKey = DEFAULT_FORT_PRESENTATION_KEY) {
  if (!isFortArchetypeKey(unitKey))
    return unitKey;

  return resolveFortCatalogUnitKey(unitKey, presentationKey) || unitKey;
}

// Maps ability_key → hook category
const ABILITY_HOOKS = {
  splash_damage:   "onAttack",
  pierce_targets:  "onAttack",
  chain_lightning: "onAttack",
  slow:            "onHit",
  freeze:          "onHit",
  poison:          "onHit",
  burn:            "onHit",
  armor_reduction: "onHit",
  reveal_stealth:  "onTick",
  knockback:       "onHit",
  teleport_back:   "onHit",
  aura_damage:     "onSpawn",
  aura_atk_speed:  "onSpawn",
  aura_range:      "onSpawn",
  aura_cooldown:   "onSpawn",
};

// Maps aura ability_key → auraType used in lane.activeAuras
const ABILITY_AURA_TYPES = {
  aura_damage:    "dmg_bonus",
  aura_atk_speed: "atk_speed_bonus",
  aura_range:     "range_bonus",
  aura_cooldown:  "cooldown_reduction",
};

/**
 * Translate raw DB ability params into the format expected by sim-core
 * _executeAbility. Converts named params (e.g. slow_pct, dps) to
 * internal names (speedMult, dmgPerTick).
 */
function translateAbilityParams(abilityKey, rawParams) {
  switch (abilityKey) {
    case "slow":
      return {
        speedMult:     1 - (rawParams.slow_pct || 25) / 100,
        durationTicks: Math.round((rawParams.duration || 2) * TICK_HZ),
      };
    case "freeze":
      return {
        durationTicks: Math.round((rawParams.duration || 1) * TICK_HZ),
        procChance:    rawParams.proc_chance || 20,
      };
    case "poison":
      return {
        dmgPerTick:    (rawParams.dps || 5) / TICK_HZ,
        durationTicks: Math.round((rawParams.duration || 4) * TICK_HZ),
      };
    case "burn":
      return {
        dmgPerTick:    (rawParams.dps || 8) / TICK_HZ,
        durationTicks: Math.round((rawParams.duration || 3) * TICK_HZ),
      };
    case "armor_reduction":
      return {
        reductionPct:  rawParams.reduction_pct || 20,
        durationTicks: Math.round((rawParams.duration || 5) * TICK_HZ),
      };
    case "knockback":
      return {
        tiles:      Math.max(1, Math.round((rawParams.distance || 0.05) * GRID_H)),
        procChance: rawParams.proc_chance || 15,
      };
    case "teleport_back":
      return {
        procChance: rawParams.proc_chance || 10,
      };
    case "chain_lightning":
      return {
        maxJumps:   rawParams.chains     || 3,
        jumpRange:  2.0,
        dmgFalloff: 1 - (rawParams.decay_pct || 25) / 100,
      };
    case "pierce_targets":
      return {
        maxTargets:   rawParams.max_targets || 3,
        pierceRadius: 1.0,
      };
    case "splash_damage":
      return {
        radius: (rawParams.radius || 0.05) * GRID_W,
      };
    default:
      return rawParams;
  }
}

/**
 * Build the abilities array for a unit/tower type from the DB-loaded unitType.
 * Returns [] if the type has no abilities or isn't in the DB.
 * @param {string} unitTypeKey
 * @returns {object[]} abilities in sim-core format
 */
function buildAbilitiesForUnitType(unitTypeKey) {
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(unitTypeKey);
  const ut = getUnitType(resolvedUnitTypeKey);
  if (!ut || !Array.isArray(ut.abilities) || ut.abilities.length === 0) return [];
  return ut.abilities.map((a, idx) => {
    const abilityKey = a.ability_key;
    const rawParams  = (a.params && typeof a.params === "object") ? a.params : {};
    const hook       = ABILITY_HOOKS[abilityKey] || "onTick";
    const isAura     = hook === "onSpawn";
    const params     = isAura
      ? { auraType: ABILITY_AURA_TYPES[abilityKey] || "dmg_bonus",
          value:    rawParams.boost_pct || rawParams.value || 0,
          ...rawParams }
      : translateAbilityParams(abilityKey, rawParams);
    return {
      type:      isAura ? "aura" : abilityKey,
      hook,
      params,
      priority:  idx,
      abilityId: idx,
    };
  });
}

// ── DB-first unit/tower resolution ────────────────────────────────────────────

/**
 * Resolve a unit definition from the DB (authoritative).
 * Returns null if the unit type is unknown or fixed-only.
 */
function resolveUnitDef(key) {
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(key);
  const ut = getUnitType(resolvedUnitTypeKey);
  if (!ut) return null;
  if (Number(ut.send_cost) <= 0) return null;
  const sp = (ut.special_props && typeof ut.special_props === "object") ? ut.special_props : {};
  const attackSpeed = Math.max(0.01, Number(ut.attack_speed) || 0.01);
  return {
    cost:               Number(ut.send_cost),
    income:             Number(ut.income),
    hp:                 Number(ut.hp),
    dmg:                Number(ut.attack_damage),
    atkCdTicks:         Math.max(1, Math.round(TICK_HZ / attackSpeed)),
    pathSpeed:          Number(ut.path_speed),
    bounty:             Number(ut.bounty) || 1,
    range:              Number(ut.range),
    ranged:             Number(ut.range) > 0,
    armorType:          ut.armor_type   || "MEDIUM",
    damageType:         ut.damage_type  || "NORMAL",
    damageReductionPct: Number(ut.damage_reduction_pct) || 0,
    warlockDebuff:      sp.warlockDebuff != null ? !!sp.warlockDebuff : false,
    structBonus:        sp.structBonus   != null ?  +sp.structBonus   : 0,
    barracks_scales_hp:  ut.barracks_scales_hp  === true,
    barracks_scales_dmg: ut.barracks_scales_dmg === true,
  };
}

/**
 * Resolve a tower definition from the DB (authoritative).
 * DB range is stored normalised to [0,1] × GRID_W.
 */
function resolveTowerDef(key) {
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(key);
  const ut = getUnitType(resolvedUnitTypeKey);
  if (!ut) return null;
  const attackSpeed = Math.max(0.01, Number(ut.attack_speed) || 0.01);
  const dbBehavior = ut.proj_behavior || null;
  const dbBehaviorParams = (ut.proj_behavior_params && typeof ut.proj_behavior_params === "object")
    ? ut.proj_behavior_params : null;
  // Fall back: splash damage type → splash behavior; otherwise single
  const fallbackBehavior = ut.damage_type === "SPLASH" ? "splash" : "single";
  const fallbackParams   = ut.damage_type === "SPLASH" ? { radius: SPLASH_RADIUS_TILES } : {};
  return {
    cost:            Number(ut.build_cost),
    range:           Number(ut.range) * GRID_W,   // DB range normalised to [0,1] × GRID_W
    dmg:             Number(ut.attack_damage),
    atkCdTicks:      Math.max(1, Math.round(TICK_HZ / attackSpeed)),
    projectileTicks: Number(ut.projectile_travel_ticks) || 7,
    damageType:      ut.damage_type || "NORMAL",
    projBehavior:       dbBehavior       || fallbackBehavior,
    projBehaviorParams: dbBehaviorParams || fallbackParams,
    isSplash:        (dbBehavior || fallbackBehavior) === "splash",
  };
}

// ── Barracks helpers ───────────────────────────────────────────────────────────

function getBarracksLevelDef(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  if (lvl === 1) {
    return {
      hpMult: 1,
      dmgMult: 1,
      speedMult: getBarracksSpeedMultForLevel(1),
      structMult: 1,
      unitCostMult: 1,
      unitIncomeMult: 1,
      incomeBonus: 0,
      cost: 0,
      reqIncome: 0,
    };
  }
  const statMult = Math.pow(2, lvl - 1);       // x2 per barracks upgrade
  const gateMult = Math.pow(2, lvl - 2);
  return {
    hpMult: statMult,
    dmgMult: statMult,
    speedMult: getBarracksSpeedMultForLevel(lvl),
    structMult: statMult,
    unitCostMult: statMult,
    unitIncomeMult: statMult,
    incomeBonus: 0,
    cost: Math.ceil(BARRACKS_COST_BASE * gateMult),
    reqIncome: Math.ceil(BARRACKS_REQ_INCOME_BASE * gateMult),
  };
}

function getBarracksSpeedMultForLevel(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  return BARRACKS_LEVEL_ONE_SPEED_MULT + ((lvl - 1) * SPEED_UPGRADE_STEP);
}

function getBarracksSpeedMult(_br) {
  const br = _br && typeof _br === "object" ? _br : null;
  if (br && Number.isFinite(br.speedMult))
    return Math.max(0.01, Number(br.speedMult));
  if (br && Number.isFinite(br.level))
    return Math.max(0.01, getBarracksSpeedMultForLevel(br.level));
  return 1;
}

function getLaneWaveSpeedMult(lane) {
  if (!lane || !Number.isFinite(Number(lane.waveSpeedMult)))
    return 1;
  return Math.max(0.01, Number(lane.waveSpeedMult));
}

function getBaseCombatPathSpeed(_unitTypeKey) {
  return BASE_COMBAT_PATH_SPEED;
}

function normalizeAllegianceKey(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case "red":
      return ALLEGIANCE_KEYS.RED;
    case "gold":
    case "yellow":
      return ALLEGIANCE_KEYS.YELLOW;
    case "blue":
      return ALLEGIANCE_KEYS.BLUE;
    case "green":
      return ALLEGIANCE_KEYS.GREEN;
    case "dungeon":
      return ALLEGIANCE_KEYS.DUNGEON;
    default:
      return null;
  }
}

function normalizeSpawnSourceType(value) {
  const normalized = String(value || "").trim().toLowerCase();
  switch (normalized) {
    case "scheduled_wave":
    case "dungeon_wave":
      return SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
    case SPAWN_SOURCE_TYPES.BARRACKS_ROSTER:
      return SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;
    case SPAWN_SOURCE_TYPES.BARRACKS_HERO:
      return SPAWN_SOURCE_TYPES.BARRACKS_HERO;
    default:
      return null;
  }
}

function normalizeUnitStance(value) {
  const normalized = String(value || "").trim().toUpperCase();
  switch (normalized) {
    case UNIT_STANCES.DEFEND:
      return UNIT_STANCES.DEFEND;
    case UNIT_STANCES.ATTACK:
    case "ADVANCE":
      return UNIT_STANCES.ATTACK;
    case UNIT_STANCES.HOLD:
      return UNIT_STANCES.HOLD;
    case "RETREAT":
    case UNIT_STANCES.RETREAT:
      return UNIT_STANCES.RETREAT;
    default:
      return null;
  }
}

function isPlayerAllegianceKey(allegianceKey) {
  return PLAYER_ALLEGIANCE_KEYS.has(normalizeAllegianceKey(allegianceKey));
}

function areAllegiancesHostile(a, b) {
  const left = normalizeAllegianceKey(a);
  const right = normalizeAllegianceKey(b);
  if (!left || !right)
    return false;
  if (left === right)
    return false;
  if (left === ALLEGIANCE_KEYS.DUNGEON || right === ALLEGIANCE_KEYS.DUNGEON)
    return true;
  return isPlayerAllegianceKey(left) && isPlayerAllegianceKey(right);
}

function resolveLaneAllegianceKey(lane) {
  if (!lane)
    return null;
  return normalizeAllegianceKey(lane.allegianceKey || lane.team || lane.slotColor);
}

function getSourceLane(game, sourceLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || !Number.isInteger(sourceLaneIndex))
    return null;
  if (sourceLaneIndex < 0 || sourceLaneIndex >= game.lanes.length)
    return null;
  return game.lanes[sourceLaneIndex] || null;
}

function resolveSpawnSourceTypeFromWaveDef(waveDef) {
  const explicitSourceType = normalizeSpawnSourceType(waveDef && waveDef.spawnSourceType);
  if (explicitSourceType)
    return explicitSourceType;

  if (waveDef && waveDef.isHero)
    return SPAWN_SOURCE_TYPES.BARRACKS_HERO;

  const sourceBarracksId = resolveUnitSourceBarracksId(waveDef);
  if (waveDef && (sourceBarracksId
      || Number.isInteger(waveDef.sourceLaneIndex) && waveDef.sourceLaneIndex >= 0))
    return SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;

  if (waveDef && waveDef.isWaveUnit)
    return SPAWN_SOURCE_TYPES.DUNGEON_WAVE;

  return SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
}

function resolveSpawnSourceTypeFromUnit(unit) {
  const explicitSourceType = normalizeSpawnSourceType(unit && unit.spawnSourceType);
  if (explicitSourceType)
    return explicitSourceType;

  if (unit && unit.isDefender)
    return unit.isHero ? SPAWN_SOURCE_TYPES.BARRACKS_HERO : SPAWN_SOURCE_TYPES.BARRACKS_ROSTER;

  return resolveSpawnSourceTypeFromWaveDef(unit);
}

function isScheduledWaveUnit(unit) {
  return resolveSpawnSourceTypeFromUnit(unit) === SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
}

function resolveUnitSourceBarracksId(unit) {
  return normalizeBarracksSiteId(unit && (unit.sourceBarracksId || unit.sourceBarracksKey || unit.barracksId));
}

function resolveUnitTargetLaneIndex(game, fallbackLane, unit) {
  if (Number.isInteger(unit && unit.targetLaneIndex))
    return unit.targetLaneIndex;
  if (Number.isInteger(unit && unit.laneId))
    return unit.laneId;
  if (fallbackLane && Number.isInteger(fallbackLane.laneIndex))
    return fallbackLane.laneIndex;
  if (Number.isInteger(unit && unit.ownerLaneIndex))
    return unit.ownerLaneIndex;
  return -1;
}

function resolveUnitObjectiveLaneIndex(game, fallbackLane, unit) {
  if (Number.isInteger(unit && unit.objectiveLaneIndex))
    return unit.objectiveLaneIndex;

  if (isLaneControlledUnit(unit)) {
    const sourceLane = getSourceLane(game, Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1);
    const objectiveLaneIndex = getLaneCommandRouteObjectiveLaneIndex(game, sourceLane);
    if (Number.isInteger(objectiveLaneIndex))
      return objectiveLaneIndex;
  }

  return resolveUnitTargetLaneIndex(game, fallbackLane, unit);
}

function resolveUnitOwnerLaneIndex(game, fallbackLane, unit) {
  if (Number.isInteger(unit && unit.ownerLaneIndex))
    return unit.ownerLaneIndex;
  if (Number.isInteger(unit && unit.ownerLane))
    return unit.ownerLane;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return -1;

  if (Number.isInteger(unit && unit.sourceLaneIndex))
    return unit.sourceLaneIndex;

  if (unit && unit.isDefender && fallbackLane && Number.isInteger(fallbackLane.laneIndex))
    return fallbackLane.laneIndex;

  return -1;
}

function resolveUnitStance(game, fallbackLane, unit) {
  if (isLaneControlledUnit(unit)) {
    const commandState = getLaneCommandStateForUnit(game, unit);
    if (commandState === LANE_COMMAND_STATES.RETREAT)
      return UNIT_STANCES.RETREAT;
    if (commandState === LANE_COMMAND_STATES.DEFEND)
      return UNIT_STANCES.DEFEND;
    return UNIT_STANCES.ATTACK;
  }

  const explicitStance = normalizeUnitStance(unit && unit.stance);
  if (explicitStance)
    return explicitStance;

  const explicitPathContractType = String(unit && unit.pathContractType || "").trim().toLowerCase();
  if (explicitPathContractType === PATH_CONTRACT_TYPES.GUARD_ANCHOR
      || explicitPathContractType === PATH_CONTRACT_TYPES.INTERCEPT)
    return UNIT_STANCES.DEFEND;

  if (unit && unit.isDefender)
    return UNIT_STANCES.DEFEND;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return UNIT_STANCES.ATTACK;

  const sourceLaneIndex = Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1;
  const targetLaneIndex = resolveUnitTargetLaneIndex(game, fallbackLane, unit);
  if (sourceLaneIndex >= 0 && sourceLaneIndex === targetLaneIndex)
    return UNIT_STANCES.HOLD;

  return UNIT_STANCES.ATTACK;
}

function mapRouteTypeToPathContractType(routeType, barracksId) {
  if (routeType === ROUTE_TYPES.WAVE_LANE)
    return PATH_CONTRACT_TYPES.WAVE_LANE;
  if (routeType === ROUTE_TYPES.CENTER_CROSS)
    return PATH_CONTRACT_TYPES.BARRACKS_CROSS;
  if (routeType === ROUTE_TYPES.OUTER_LOOP)
    return PATH_CONTRACT_TYPES.BARRACKS_LOOP;

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (normalizedBarracksId === "center")
    return PATH_CONTRACT_TYPES.BARRACKS_CROSS;
  if (normalizedBarracksId === "left" || normalizedBarracksId === "right")
    return PATH_CONTRACT_TYPES.BARRACKS_LOOP;
  return null;
}

function resolveUnitPathContractType(game, fallbackLane, unit) {
  if (isLaneControlledUnit(unit)) {
    const commandState = getLaneCommandStateForUnit(game, unit);
    const combatTargetId = unit && (unit.combatTargetId || unit.combatTarget && unit.combatTarget.unitId);
    if (combatTargetId && isLaneCommandCombatEnabledForUnit(game, unit))
      return PATH_CONTRACT_TYPES.INTERCEPT;
    if (commandState === LANE_COMMAND_STATES.RETREAT)
      return PATH_CONTRACT_TYPES.RETREAT_ANCHOR;
    if (commandState === LANE_COMMAND_STATES.DEFEND)
      return PATH_CONTRACT_TYPES.GUARD_ANCHOR;
    return PATH_CONTRACT_TYPES.BARRACKS_CROSS;
  }

  const stance = resolveUnitStance(game, fallbackLane, unit);
  if (stance === UNIT_STANCES.DEFEND)
    return unit && (unit.combatTargetId || unit.combatTarget && unit.combatTarget.unitId)
      ? PATH_CONTRACT_TYPES.INTERCEPT
      : PATH_CONTRACT_TYPES.GUARD_ANCHOR;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return PATH_CONTRACT_TYPES.WAVE_LANE;

  return mapRouteTypeToPathContractType(unit && unit.routeType, resolveUnitSourceBarracksId(unit));
}

function resolveUnitAllegianceKey(game, fallbackLane, unit) {
  const explicitAllegiance = normalizeAllegianceKey(unit && unit.allegianceKey);
  if (explicitAllegiance)
    return explicitAllegiance;

  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  if (spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE)
    return ALLEGIANCE_KEYS.DUNGEON;

  const sourceTeam = normalizeAllegianceKey(unit && unit.sourceTeam);
  if (sourceTeam)
    return sourceTeam;

  const sourceLane = getSourceLane(game, Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1);
  const sourceLaneAllegiance = resolveLaneAllegianceKey(sourceLane);
  if (sourceLaneAllegiance)
    return sourceLaneAllegiance;

  const ownerLane = getSourceLane(game, resolveUnitOwnerLaneIndex(game, fallbackLane, unit));
  const ownerLaneAllegiance = resolveLaneAllegianceKey(ownerLane);
  if (ownerLaneAllegiance)
    return ownerLaneAllegiance;

  return resolveLaneAllegianceKey(fallbackLane);
}

function resolveLegacySourceTeamFromAllegianceKey(allegianceKey) {
  const canonical = normalizeAllegianceKey(allegianceKey);
  return canonical === ALLEGIANCE_KEYS.DUNGEON
    ? null
    : canonical;
}

function applyCanonicalUnitMirrors(game, fallbackLane, unit) {
  if (!unit)
    return unit;

  const unitId = unit.id || unit.unitId || null;
  const unitTypeKey = unit.type || unit.unitTypeKey || null;
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  const ownerLaneIndex = resolveUnitOwnerLaneIndex(game, fallbackLane, unit);
  const targetLaneIndex = resolveUnitTargetLaneIndex(game, fallbackLane, unit);
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, fallbackLane, unit);
  const allegianceKey = resolveUnitAllegianceKey(game, fallbackLane, unit);
  const pathContractType = resolveUnitPathContractType(game, fallbackLane, unit);
  const sourceBarracksId = resolveUnitSourceBarracksId(unit);
  const stance = resolveUnitStance(game, fallbackLane, unit);
  const combatTargetId = unit.combatTargetId || (unit.combatTarget && unit.combatTarget.unitId) || null;
  const groupId = resolveUnitPacketGroupId(unit);
  const combatRole = resolveUnitCombatRole(unit);
  const preferredBand = resolveUnitPreferredBand(unit);
  const isPacketControlledUnit = Number.isInteger(unit.sourceLaneIndex)
    && unit.sourceLaneIndex >= 0
    && (spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER
      || spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_HERO);
  const isDefenderUnit = !!unit.isDefender && !isPacketControlledUnit;
  const canEngage = isLaneCommandCombatEnabledForUnit(game, unit);

  unit.unitId = unitId;
  unit.id = unitId;
  unit.unitTypeKey = unitTypeKey;
  unit.type = unitTypeKey;
  unit.spawnSourceType = spawnSourceType;
  unit.allegianceKey = allegianceKey;
  unit.ownerLaneIndex = ownerLaneIndex;
  unit.ownerLane = ownerLaneIndex;
  unit.targetLaneIndex = targetLaneIndex;
  unit.laneId = targetLaneIndex;
  unit.objectiveLaneIndex = objectiveLaneIndex;
  unit.sourceBarracksId = sourceBarracksId;
  unit.sourceBarracksKey = sourceBarracksId;
  unit.barracksId = sourceBarracksId;
  unit.heroKey = unit.heroKey || null;
  unit.stance = stance;
  unit.pathContractType = pathContractType;
  unit.groupId = groupId;
  unit.combatRole = combatRole;
  unit.preferredBand = preferredBand;
  unit.combatTargetId = combatTargetId;
  unit.currentTargetId = combatTargetId;
  unit.sourceTeam = resolveLegacySourceTeamFromAllegianceKey(allegianceKey);
  unit.isWaveUnit = spawnSourceType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE;
  unit.isDefender = isDefenderUnit;
  unit.canEngage = canEngage;
  unit.state = unit.combatState || unit.routeState || unit.state || WAVE_UNIT_STATES.IDLE;
  if (!unit.movementMode)
    unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  return unit;
}

function buildRoutePathId(routeSegments) {
  if (!Array.isArray(routeSegments) || routeSegments.length <= 0)
    return null;
  return routeSegments.join(">");
}

function resolveUnitNextWaypoint(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length <= 0)
    return null;

  const currentIndex = Math.max(0, Math.min(unit.routeSegments.length - 1, Math.floor(Number(unit.routeSegmentIndex) || 0)));
  const segmentId = unit.routeSegments[currentIndex];
  const parts = String(segmentId || "").split("_");
  return parts.length === 2 ? parts[1] : null;
}

function dot2D(a, b) {
  const ax = Number(a && a.x) || 0;
  const ay = Number(a && a.y) || 0;
  const bx = Number(b && b.x) || 0;
  const by = Number(b && b.y) || 0;
  return (ax * bx) + (ay * by);
}

function resolveCenteredFormationColumn(slotIndex) {
  const centerColumn = Math.max(0, Math.min(GRID_W - 1, Math.floor(Number(SPAWN_X) || 0)));
  const safeSlotIndex = Math.max(0, Math.floor(Number(slotIndex) || 0));
  if (safeSlotIndex <= 0)
    return centerColumn;

  const step = Math.ceil(safeSlotIndex / 2);
  const offset = safeSlotIndex % 2 === 1 ? -step : step;
  return Math.max(0, Math.min(GRID_W - 1, centerColumn + offset));
}

function resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex) {
  const safeSpawnIndex = Math.max(0, Math.floor(Number(resolvedSpawnIndex) || 0));
  const row = Math.floor(safeSpawnIndex / GRID_W);
  const slotInRow = safeSpawnIndex % GRID_W;
  if (spawnType === SPAWN_SOURCE_TYPES.SCHEDULED_WAVE) {
    return {
      x: resolveCenteredFormationColumn(slotInRow),
      y: row,
    };
  }

  return {
    x: slotInRow,
    y: row,
  };
}

function validateSpawnDefinition(game, targetLane, waveDef) {
  const spawnType = resolveSpawnSourceTypeFromWaveDef(waveDef);
  const requestedSpawnIndex = Number.isInteger(waveDef && waveDef.spawnIndex)
    ? waveDef.spawnIndex
    : Math.max(0, targetLane && Array.isArray(targetLane.spawnQueue) ? targetLane.spawnQueue.length : 0);
  const resolvedSpawnIndex = Math.max(0, requestedSpawnIndex);
  const logicalPos = resolveSpawnLogicalPosition(spawnType, resolvedSpawnIndex);
  const sourceLaneIndex = Number.isInteger(waveDef && waveDef.sourceLaneIndex) ? waveDef.sourceLaneIndex : -1;
  const sourceLane = getSourceLane(game, sourceLaneIndex);
  const sourceBarracksKey = normalizeBarracksSiteId(
    waveDef && (waveDef.sourceBarracksKey || waveDef.sourceBarracksId)
  );
  const sourceTeam = normalizeAllegianceKey(waveDef && waveDef.sourceTeam);

  if (!targetLane)
    return { ok: false, reason: "Missing target lane", spawnType };

  if (logicalPos.x < 0 || logicalPos.x >= GRID_W || logicalPos.y < 0)
    return { ok: false, reason: "Resolved spawn index is out of legal queue bounds", spawnType };

  if ((spawnType === "barracks_roster" || spawnType === "barracks_hero") && !sourceLane)
    return { ok: false, reason: "Spawn source lane is missing", spawnType };

  if ((spawnType === "barracks_roster" || spawnType === "barracks_hero") && !sourceBarracksKey)
    return { ok: false, reason: "Spawn source barracks id is missing", spawnType };

  if ((spawnType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER || spawnType === SPAWN_SOURCE_TYPES.BARRACKS_HERO)
      && sourceLane && sourceTeam && resolveLaneAllegianceKey(sourceLane) !== sourceTeam)
    return { ok: false, reason: "Spawn source team does not match source lane ownership", spawnType };

  if ((spawnType === "barracks_roster" || spawnType === "barracks_hero") && sourceLane) {
    const descriptor = describeBarracksSite(game, sourceLane, sourceBarracksKey);
    if (!descriptor || !descriptor.isBuilt)
      return { ok: false, reason: "Spawn source barracks does not exist or is not built on the source lane", spawnType };
  }

  if (spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE && !getWaveSpawnWorldPosition(targetLane.laneIndex))
    return { ok: false, reason: "Wave spawn origin is missing for the target lane", spawnType };

  return {
    ok: true,
    spawnType,
    sourceLaneIndex,
    sourceTeam,
    sourceBarracksKey,
    requestedSpawnIndex,
    resolvedSpawnIndex,
    logicalPos,
  };
}

function getEffectiveWaveEntrySpeedMult(game, lane, waveDef) {
  const safeWaveDef = waveDef && typeof waveDef === "object" ? waveDef : {};
  const authoredSpeedMult = Math.max(0.01, Number(safeWaveDef.speed_mult || 1));
  const sourceLane = getSourceLane(game, safeWaveDef.sourceLaneIndex);
  if (sourceLane)
    return authoredSpeedMult * getBarracksSpeedMult(sourceLane.barracks);
  return authoredSpeedMult * getLaneWaveSpeedMult(lane);
}

function getBarracksUnitCostMult(br) {
  if (!br || typeof br !== "object") return 1;
  if (Number.isFinite(br.unitCostMult)) return Math.max(0, Number(br.unitCostMult));
  if (Number.isFinite(br.hpMult)) return Math.max(0, Number(br.hpMult));
  return 1;
}

function getBarracksUnitIncomeMult(br) {
  if (!br || typeof br !== "object") return 1;
  if (Number.isFinite(br.unitIncomeMult)) return Math.max(0, Number(br.unitIncomeMult));
  if (Number.isFinite(br.hpMult)) return Math.max(0, Number(br.hpMult));
  return 1;
}

function getFortressMaxTier(buildingType) {
  if (buildingType === "barracks")
    return Math.max(1, Math.floor(Number(getMaxBarracksLevel()) || 1));

  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef ? Math.max(1, Math.floor(Number(buildingDef.maxTier) || 1)) : 1;
}

function createFortressPadStates(teamHpStart) {
  return FORTRESS_PAD_DEFS.map((padDef) => {
    const buildingDef = FORTRESS_BUILDING_DEFS[padDef.buildingType];
    const tier = buildingDef && buildingDef.startsBuilt ? 1 : 0;
    const maxHp = tier > 0
      ? resolveFortressBuildingMaxHp(padDef.buildingType, tier, teamHpStart)
      : 0;
    return {
      padId: padDef.padId,
      buildingType: padDef.buildingType,
      displayName: padDef.displayName,
      gridX: padDef.gridX,
      gridY: padDef.gridY,
      combatOffsetX: Number.isFinite(Number(padDef.combatOffsetX))
        ? Number(padDef.combatOffsetX)
        : null,
      combatOffsetY: Number.isFinite(Number(padDef.combatOffsetY))
        ? Number(padDef.combatOffsetY)
        : null,
      tier,
      maxHp,
      hp: maxHp,
      costHistory: tier > 0 ? [] : null,
    };
  });
}

function createBarracksRosterCounts() {
  const counts = {};
  for (const def of BARRACKS_ROSTER_DEFS) counts[def.rosterKey] = 0;
  return counts;
}

function createBarracksSiteRosterCounts() {
  const siteCounts = {};
  for (const siteDef of BARRACKS_SITE_DEFS)
    siteCounts[siteDef.barracksId] = createBarracksRosterCounts();
  return siteCounts;
}

function getBarracksSiteDef(barracksId) {
  return BARRACKS_SITE_DEFS.find((entry) => entry && entry.barracksId === barracksId) || null;
}

function normalizeBarracksSiteId(value) {
  const raw = String(value || "").trim().toLowerCase();
  switch (raw) {
    case "left":
    case "left_barracks":
    case "barracks_left":
      return "left";
    case "right":
    case "right_barracks":
    case "barracks_right":
      return "right";
    case "center":
    case "centre":
    case "center_barracks":
    case "barracks_center":
    case "barracks_centre":
      return "center";
    default:
      return "";
  }
}

function summarizeBarracksSiteCounts(siteCounts) {
  if (!siteCounts || typeof siteCounts !== "object") return "<missing>";
  const entries = Object.entries(siteCounts)
    .map(([rosterKey, count]) => ({
      rosterKey,
      ownedCount: Math.max(0, Math.floor(Number(count) || 0)),
    }))
    .filter((entry) => entry.ownedCount > 0)
    .sort((a, b) => String(a.rosterKey).localeCompare(String(b.rosterKey)));
  if (entries.length === 0) return "<empty>";
  return entries.map((entry) => `${entry.rosterKey}:${entry.ownedCount}`).join(",");
}

function summarizeBarracksSiteRosterEntries(rosterEntries) {
  if (!Array.isArray(rosterEntries)) return "<missing>";
  const entries = rosterEntries
    .filter((entry) => entry && Math.max(0, Math.floor(Number(entry.ownedCount) || 0)) > 0)
    .map((entry) => `${entry.rosterKey}:${Math.max(0, Math.floor(Number(entry.ownedCount) || 0))}`);
  return entries.length > 0 ? entries.join(",") : "<empty>";
}

function logBarracksRosterState(lane, reason) {
  if (!lane) return;
  const summary = BARRACKS_SITE_DEFS
    .map((siteDef) => {
      const siteCounts = lane.barracksSiteRosterCounts && lane.barracksSiteRosterCounts[siteDef.barracksId];
      return `${siteDef.barracksId}=${summarizeBarracksSiteCounts(siteCounts)}`;
    })
    .join(" ");
  console.log(
    `[BarracksTrace][ServerRoster] lane=${lane.laneIndex} reason='${reason}' ${summary}`);
}

function getBarracksSiteCounts(lane, barracksId) {
  if (!lane) return null;
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId)
    return null;
  if (!lane.barracksSiteRosterCounts || typeof lane.barracksSiteRosterCounts !== "object")
    lane.barracksSiteRosterCounts = createBarracksSiteRosterCounts();

  if (!lane.barracksSiteRosterCounts[normalizedId])
    lane.barracksSiteRosterCounts[normalizedId] = createBarracksRosterCounts();

  return lane.barracksSiteRosterCounts[normalizedId];
}

function hasOwnedBarracksUnits(lane, barracksId) {
  const siteCounts = getBarracksSiteCounts(lane, barracksId);
  if (!siteCounts) return false;
  for (const value of Object.values(siteCounts)) {
    if (Math.max(0, Math.floor(Number(value) || 0)) > 0)
      return true;
  }
  return false;
}

function getBarracksSiteTierRequirement(siteDef) {
  return siteDef ? Math.max(1, Math.floor(Number(siteDef.legacyTierUnlock) || 1)) : 1;
}

function getBarracksSiteBuildCost(siteDef) {
  return siteDef ? Math.max(0, Math.floor(Number(siteDef.buildCost) || 0)) : 0;
}

function getBarracksSiteMaxLevel() {
  return Math.max(1, Math.floor(Number(getMaxBarracksLevel()) || 1));
}

function normalizeBarracksSiteLevel(level) {
  return Math.min(
    getBarracksSiteMaxLevel(),
    Math.max(1, Math.floor(Number(level) || 1))
  );
}

function getBarracksSiteBaseMaxHp() {
  const buildingDef = FORTRESS_BUILDING_DEFS.barracks;
  return Math.max(1, Math.floor(Number(buildingDef && buildingDef.baseMaxHp) || 260));
}

function getBarracksSiteSendIntervalTicks(level) {
  const safeLevel = normalizeBarracksSiteLevel(level);
  const seconds = Math.max(5, 30 - ((safeLevel - 1) * 10));
  return seconds * TICK_HZ;
}

function createBarracksSiteState(siteDef, options = {}) {
  const built = !!options.isBuilt;
  const level = built ? normalizeBarracksSiteLevel(options.level) : 0;
  const baseMaxHp = getBarracksSiteBaseMaxHp();
  const interval = level > 0 ? getBarracksSiteSendIntervalTicks(level) : 0;
  const maxHp = built ? baseMaxHp : 0;
  const hp = built
    ? Math.max(0, Math.min(maxHp, Math.floor(Number(options.hp) || maxHp)))
    : 0;

  return {
    barracksId: siteDef.barracksId,
    isBuilt: built,
    level,
    hp,
    maxHp,
    nextSendTick: built
      ? Math.max(1, Math.floor(Number(options.nextSendTick) || interval))
      : 0,
    costHistory: Array.isArray(options.costHistory) ? options.costHistory.slice() : [],
  };
}

function createBarracksSiteStates(_teamHpStart, legacyBarracksLevel = 1) {
  const states = {};
  const safeLegacyLevel = normalizeBarracksSiteLevel(legacyBarracksLevel);
  for (const siteDef of BARRACKS_SITE_DEFS) {
    const built = !!siteDef.startsBuilt;
    states[siteDef.barracksId] = createBarracksSiteState(siteDef, {
      isBuilt: built,
      level: built ? safeLegacyLevel : 0,
      hp: built ? getBarracksSiteBaseMaxHp() : 0,
      nextSendTick: built ? getBarracksSiteSendIntervalTicks(safeLegacyLevel) : 0,
    });
  }
  return states;
}

function syncLegacyBarracksAggregate(lane) {
  if (!lane) return;

  const states = lane.barracksSiteStates && typeof lane.barracksSiteStates === "object"
    ? Object.values(lane.barracksSiteStates)
    : [];
  let aggregateLevel = 1;
  for (const state of states) {
    if (!state || !state.isBuilt) continue;
    aggregateLevel = Math.max(aggregateLevel, normalizeBarracksSiteLevel(state.level));
  }

  lane.barracks = Object.assign({ level: aggregateLevel }, getBarracksLevelDef(aggregateLevel));
}

function ensureBarracksSiteStates(lane, game) {
  if (!lane) return {};

  const legacyBarracksLevel = normalizeBarracksSiteLevel(
    lane.barracks && lane.barracks.level
  );
  const legacyNextSendTick = game && Number.isInteger(game.nextBarracksSendTick)
    ? game.nextBarracksSendTick
    : null;
  const existing = lane.barracksSiteStates && typeof lane.barracksSiteStates === "object"
    ? lane.barracksSiteStates
    : {};
  const next = {};

  for (const siteDef of BARRACKS_SITE_DEFS) {
    const siteId = siteDef.barracksId;
    const current = existing[siteId];
    const shouldStartBuilt = !!siteDef.startsBuilt
      || hasOwnedBarracksUnits(lane, siteId)
      || legacyBarracksLevel >= getBarracksSiteTierRequirement(siteDef);

    if (current && typeof current === "object") {
      const built = !!current.isBuilt || hasOwnedBarracksUnits(lane, siteId);
      next[siteId] = createBarracksSiteState(siteDef, {
        isBuilt: built,
        level: built ? (current.level || legacyBarracksLevel) : 0,
        hp: current.hp,
        nextSendTick: current.nextSendTick,
        costHistory: current.costHistory,
      });
      continue;
    }

    next[siteId] = createBarracksSiteState(siteDef, {
      isBuilt: shouldStartBuilt,
      level: shouldStartBuilt ? legacyBarracksLevel : 0,
      hp: shouldStartBuilt ? getBarracksSiteBaseMaxHp() : 0,
      nextSendTick: shouldStartBuilt ? legacyNextSendTick : 0,
    });
  }

  lane.barracksSiteStates = next;
  syncLegacyBarracksAggregate(lane);
  return next;
}

function getBarracksSiteState(lane, barracksId, game) {
  if (!lane) return null;
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId) return null;

  const states = ensureBarracksSiteStates(lane, game);
  return states[normalizedId] || null;
}

function describeBarracksSite(game, lane, barracksId) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId) return null;

  const siteDef = getBarracksSiteDef(normalizedId);
  const siteState = getBarracksSiteState(lane, normalizedId, game);
  if (!siteDef || !siteState) return null;

  const built = !!siteState.isBuilt;
  const level = built ? normalizeBarracksSiteLevel(siteState.level) : 0;
  const maxLevel = getBarracksSiteMaxLevel();
  const nextLevel = built ? Math.min(maxLevel, level + 1) : 1;
  const townCoreTier = getTownCoreTier(lane);
  const requiredTownCoreTier = getFortressRequiredTownCoreTier("barracks", nextLevel);
  const canBuild = !built && townCoreTier >= requiredTownCoreTier;
  const canUpgrade = built && level < maxLevel && townCoreTier >= requiredTownCoreTier;

  let buildState = FORTRESS_BUILD_STATES.locked;
  if (!built) {
    buildState = canBuild ? FORTRESS_BUILD_STATES.availableToBuild : FORTRESS_BUILD_STATES.locked;
  } else if (level >= maxLevel) {
    buildState = FORTRESS_BUILD_STATES.maxTier;
  } else if (canUpgrade) {
    buildState = FORTRESS_BUILD_STATES.upgradeAvailable;
  } else {
    buildState = FORTRESS_BUILD_STATES.built;
  }

  let lockedReason = null;
  if (!built && !canBuild) {
    lockedReason = `Requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  } else if (built && level < maxLevel && !canUpgrade) {
    lockedReason = `Upgrade requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  }

  const sendIntervalTicks = built ? getBarracksSiteSendIntervalTicks(level) : getBarracksSiteSendIntervalTicks(1);
  if (built && (!Number.isInteger(siteState.nextSendTick) || siteState.nextSendTick <= 0)) {
    const baseTick = game ? Math.floor(Number(game.tick) || 0) : 0;
    siteState.nextSendTick = baseTick + sendIntervalTicks;
  }

  const sendTimerTicksRemaining = built && game
    ? Math.max(0, Math.floor(Number(siteState.nextSendTick) || 0) - Math.floor(Number(game.tick) || 0))
    : 0;
  const nextDef = built && level < maxLevel ? getBarracksUpgradeDef(nextLevel) : null;
  const upgradeCost = built && level < maxLevel
    ? Math.max(
        0,
        Math.floor(
          Number(nextDef && nextDef.upgradeCost) || getFortressUpgradeCost("barracks", nextLevel) || 0
        )
      )
    : 0;

  return {
    siteDef,
    siteState,
    isBuilt: built,
    level,
    maxLevel,
    buildState,
    canBuild,
    canUpgrade,
    buildCost: getBarracksSiteBuildCost(siteDef),
    upgradeCost,
    requiredTownCoreTier,
    lockedReason,
    sendIntervalTicks,
    sendTimerTicksRemaining,
    sendTimerTotalTicks: built ? sendIntervalTicks : 0,
  };
}

function getBarracksSiteLockedReason(siteDef) {
  return siteDef ? `${siteDef.displayName} is not available yet.` : "Barracks unavailable";
}

function isBarracksSiteAvailable(lane, barracksId) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  return !!(descriptor && descriptor.isBuilt);
}

function getBarracksRosterLockedReason(rosterDef) {
  if (!rosterDef) return "Locked";
  const buildingDef = FORTRESS_BUILDING_DEFS[rosterDef.unlockBuildingType];
  const buildingName = buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType;
  const tierName = getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier);
  if (String(buildingName || "").trim().toLowerCase() === String(tierName || "").trim().toLowerCase())
    return `${buildingName} required`;
  return `${buildingName}: ${tierName}`;
}

function getBuiltBarracksSiteTier(lane, barracksId) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  if (!descriptor || !descriptor.isBuilt)
    return 0;

  return Math.max(1, Math.floor(Number(descriptor.level) || 1));
}

function getHighestBuiltBarracksSiteTier(lane) {
  let highestTier = 0;
  for (const siteDef of BARRACKS_SITE_DEFS)
    highestTier = Math.max(highestTier, getBuiltBarracksSiteTier(lane, siteDef.barracksId));

  return highestTier;
}

function resolveBarracksRosterUnlockContext(lane, rosterDef, barracksId = null) {
  const requiredBuildingTier = Math.max(1, Math.floor(Number(rosterDef && rosterDef.requiredBuildingTier) || 1));
  const unlockBuildingType = String((rosterDef && rosterDef.unlockBuildingType) || "").trim().toLowerCase();

  if (unlockBuildingType === "barracks") {
    const unlockTier = barracksId
      ? getBuiltBarracksSiteTier(lane, barracksId)
      : getHighestBuiltBarracksSiteTier(lane);
    return {
      unlockPad: null,
      unlockPadId: null,
      unlockTier,
      unlocked: unlockTier >= requiredBuildingTier,
      buildingDef: FORTRESS_BUILDING_DEFS.barracks || null,
    };
  }

  const unlockPad = getFortressPadByBuildingType(lane, rosterDef && rosterDef.unlockBuildingType);
  const unlockTier = getHighestBuiltFortressPadTier(lane, rosterDef && rosterDef.unlockBuildingType);
  return {
    unlockPad,
    unlockPadId: unlockPad ? unlockPad.padId : null,
    unlockTier,
    unlocked: unlockTier >= requiredBuildingTier,
    buildingDef: FORTRESS_BUILDING_DEFS[rosterDef && rosterDef.unlockBuildingType] || null,
  };
}

function getHeroRosterDefinition(heroKey) {
  return HERO_ROSTER_DEFS.find((entry) => entry.heroKey === heroKey) || null;
}

function getHeroRosterLockedReason(heroDef) {
  if (!heroDef) return "Hero locked";
  const buildingDef = FORTRESS_BUILDING_DEFS[heroDef.unlockBuildingType];
  const tierName = getBuildingTierDisplayName(heroDef.unlockBuildingType, heroDef.requiredBuildingTier);
  if (buildingDef && heroDef.requiredBuildingTier > 0)
    return `${buildingDef.displayName}: ${tierName}`;
  return "Castle required";
}

function getFortressPadState(lane, padId) {
  if (!lane || !Array.isArray(lane.fortressPads)) return null;
  return lane.fortressPads.find((pad) => pad && pad.padId === padId) || null;
}

function getFortressPadByBuildingType(lane, buildingType) {
  if (!lane || !Array.isArray(lane.fortressPads)) return null;
  return lane.fortressPads.find((pad) => pad && pad.buildingType === buildingType) || null;
}

function getHighestBuiltFortressPadTier(lane, buildingType) {
  if (!lane || !Array.isArray(lane.fortressPads) || !buildingType)
    return 0;

  let bestTier = 0;
  for (const pad of lane.fortressPads) {
    if (!pad || pad.buildingType !== buildingType)
      continue;
    bestTier = Math.max(bestTier, Math.max(0, Math.floor(Number(pad.tier) || 0)));
  }
  return bestTier;
}

function getTownCoreTier(lane) {
  return Math.max(1, getHighestBuiltFortressPadTier(lane, "town_core") || 1);
}

function getLaneTownCorePad(lane) {
  return getFortressPadByBuildingType(lane, "town_core");
}

function getLaneTownCoreHp(lane) {
  const corePad = getLaneTownCorePad(lane);
  return corePad ? Math.max(0, Math.floor(Number(corePad.hp) || 0)) : 0;
}

function getLaneTownCoreMaxHp(lane) {
  const corePad = getLaneTownCorePad(lane);
  return corePad ? Math.max(1, Math.floor(Number(corePad.maxHp) || 1)) : 1;
}

function buildTownCoreStateSummary(game) {
  if (!game || !Array.isArray(game.lanes)) return [];
  return game.lanes.map((lane) => {
    const corePad = getLaneTownCorePad(lane);
    return {
      laneIndex: lane ? lane.laneIndex : null,
      side: lane ? lane.side : null,
      eliminated: !!(lane && lane.eliminated),
      corePadId: corePad ? corePad.padId : null,
      coreHp: corePad ? Math.max(0, Math.floor(Number(corePad.hp) || 0)) : 0,
      coreMaxHp: corePad ? Math.max(1, Math.floor(Number(corePad.maxHp) || 1)) : 1,
      waveUnits: lane && Array.isArray(lane.units)
        ? lane.units.filter((unit) => unit && unit.hp > 0 && isScheduledWaveUnit(unit)).length
        : 0,
      defenders: lane && Array.isArray(lane.units)
        ? lane.units.filter((unit) => unit && unit.hp > 0 && unit.isDefender).length
        : 0,
    };
  });
}

function getFortressPadCombatTarget(lane, padState, positionOverride = null) {
  if (!lane || !padState) return null;
  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  if (currentTier <= 0)
    return null;
  const currentHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  if (currentHp <= 0) return null;
  const fallbackPos = Number.isFinite(Number(padState.combatOffsetX))
    && Number.isFinite(Number(padState.combatOffsetY))
    ? (() => {
        const axes = getLaneCombatAxes(lane.laneIndex);
        if (!axes)
          return getPadWorldPosition(lane.laneIndex, padState.gridX, padState.gridY);
        return {
          x: axes.core.x + (axes.lateral.x * Number(padState.combatOffsetX)) + (axes.forward.x * Number(padState.combatOffsetY)),
          y: axes.core.y + (axes.lateral.y * Number(padState.combatOffsetX)) + (axes.forward.y * Number(padState.combatOffsetY)),
        };
      })()
    : getPadWorldPosition(lane.laneIndex, padState.gridX, padState.gridY);
  const pos = positionOverride && Number.isFinite(positionOverride.x) && Number.isFinite(positionOverride.y)
    ? { x: Number(positionOverride.x), y: Number(positionOverride.y) }
    : fallbackPos;
  return {
    id: padState.padId,
    unitId: padState.padId,
    laneIndex: lane.laneIndex,
    kind: "fortress_pad",
    padId: padState.padId,
    buildingType: padState.buildingType,
    type: padState.buildingType,
    posX: pos.x,
    posY: pos.y,
    hp: currentHp,
    maxHp: Math.max(1, Math.floor(Number(padState.maxHp) || 1)),
  };
}

function getBarracksSiteCombatTarget(lane, barracksId) {
  const descriptor = describeBarracksSite(null, lane, barracksId);
  if (!descriptor || !descriptor.isBuilt || !descriptor.siteState)
    return null;

  const hp = Math.max(0, Math.floor(Number(descriptor.siteState.hp) || 0));
  if (hp <= 0)
    return null;

  const pos = getBarracksSiteWorldPosition(lane.laneIndex, barracksId);
  if (!pos)
    return null;
  return {
    id: `barracks_site:${descriptor.siteState.barracksId}`,
    unitId: `barracks_site:${descriptor.siteState.barracksId}`,
    laneIndex: lane.laneIndex,
    kind: "fortress_pad",
    padId: `barracks_site:${descriptor.siteState.barracksId}`,
    barracksId: descriptor.siteState.barracksId,
    buildingType: "barracks_site",
    type: "barracks",
    posX: pos.x,
    posY: pos.y,
    hp,
    maxHp: Math.max(1, Math.floor(Number(descriptor.siteState.maxHp) || 1)),
  };
}

function getLaneTownCoreCombatTarget(lane) {
  const corePad = getLaneTownCorePad(lane);
  const objectivePoint = corePad ? getPadWorldPosition(lane.laneIndex, corePad.gridX, corePad.gridY) : null;
  return getFortressPadCombatTarget(lane, corePad, objectivePoint);
}

function getBuildingTierDisplayName(buildingType, tier) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  const safeTier = Math.max(0, Math.floor(Number(tier) || 0));
  const tierNames = buildingDef && buildingDef.tierDisplayNames;
  if (Array.isArray(tierNames) && tierNames[safeTier])
    return String(tierNames[safeTier]);
  return safeTier > 0 ? `Tier ${safeTier}` : "Unbuilt";
}

function getBuildingBranchLabel(buildingType) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef && String(buildingDef.branchLabel || "").trim() !== ""
    ? buildingDef.branchLabel
    : buildingDef && buildingDef.displayName
      ? buildingDef.displayName
      : buildingType;
}

function getBuildingDisplayName(buildingType) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef && String(buildingDef.displayName || "").trim() !== ""
    ? buildingDef.displayName
    : buildingType;
}

function isHeroUnlockedForLane(lane, heroDef) {
  if (!lane || !heroDef)
    return false;

  const unlockTier = getHighestBuiltFortressPadTier(lane, heroDef.unlockBuildingType);
  return unlockTier >= Math.max(1, Math.floor(Number(heroDef.requiredBuildingTier) || 1));
}

function getHeroDisabledReason(game, lane, heroDef) {
  if (!lane || !heroDef)
    return "Hero unavailable";

  if (!isHeroUnlockedForLane(lane, heroDef))
    return getHeroRosterLockedReason(heroDef);

  if (countBuiltBarracksSites(lane) <= 0)
    return "Buy a Barracks to summon heroes";

  const cooldownReadyTick = getHeroCooldownReadyTick(lane, heroDef.heroKey);
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  if (cooldownReadyTick > currentTick) {
    const secondsRemaining = Math.max(0, Math.ceil((cooldownReadyTick - currentTick) / Math.max(1, TICK_HZ)));
    return `Cooldown ${secondsRemaining}s`;
  }

  const activeLimit = Math.max(0, Math.floor(Number(heroDef.activeLimit) || 0));
  const activeCount = countActiveHeroDeployments(game, lane.laneIndex, heroDef.heroKey);
  if (activeLimit > 0 && activeCount >= activeLimit)
    return `${heroDef.displayName} is already deployed`;

  if (!Number.isInteger(resolveTargetLaneForBarracksSend(game, lane.laneIndex, "center")))
    return "Lane command target unavailable";

  return null;
}

function createHeroRosterSnapshot(game, lane) {
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  const builtBarracksCount = countBuiltBarracksSites(lane);

  return HERO_ROSTER_DEFS
    .slice()
    .sort((a, b) => (a.sortIndex || 0) - (b.sortIndex || 0))
    .map((heroDef) => {
      const presentation = resolveFortPresentationConfig(
        heroDef.archetypeKey,
        heroDef.presentationKey,
        heroDef.displayName,
      );
      const unlocked = isHeroUnlockedForLane(lane, heroDef);
      const cooldownReadyTick = getHeroCooldownReadyTick(lane, heroDef.heroKey);
      const cooldownTicksRemaining = Math.max(0, cooldownReadyTick - currentTick);
      const activeLimit = Math.max(0, Math.floor(Number(heroDef.activeLimit) || 0));
      const activeCount = countActiveHeroDeployments(game, lane && lane.laneIndex, heroDef.heroKey);
      const summonSourceBuildingName = getBuildingDisplayName(heroDef.summonSourceBuildingType);
      const disabledReason = getHeroDisabledReason(game, lane, heroDef);

      let state = "disabled";
      if (!unlocked) {
        state = "locked";
      } else if (activeLimit > 0 && activeCount >= activeLimit) {
        state = "active";
      } else if (cooldownTicksRemaining > 0) {
        state = "cooldown";
      } else if (!disabledReason) {
        state = "ready";
      }

      return {
        heroKey: heroDef.heroKey,
        displayName: heroDef.displayName,
        role: heroDef.role,
        roleLabel: heroDef.roleLabel || "Hero",
        sortIndex: heroDef.sortIndex || 0,
        archetypeKey: heroDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: heroDef.branchKey || "heroes",
        branchLabel: heroDef.branchLabel || "Heroes",
        isHero: true,
        unlocked,
        unlockBuildingType: heroDef.unlockBuildingType,
        unlockBuildingName: getBuildingDisplayName(heroDef.unlockBuildingType),
        unlockBuildingTierName: getBuildingTierDisplayName(heroDef.unlockBuildingType, heroDef.requiredBuildingTier),
        requiredBuildingTier: heroDef.requiredBuildingTier,
        summonSourceBuildingType: heroDef.summonSourceBuildingType,
        summonSourceBuildingName,
        summonCost: Math.max(0, Math.floor(Number(heroDef.summonCost) || 0)),
        cooldownTicks: Math.max(0, Math.floor(Number(heroDef.cooldownTicks) || 0)),
        cooldownReadyTick,
        cooldownTicksRemaining,
        activeLimit,
        activeCount,
        builtBarracksCount,
        state,
        canSummon: !disabledReason,
        heroVisualStyleKey: heroDef.heroVisualStyleKey || null,
        lockedReason: unlocked ? null : getHeroRosterLockedReason(heroDef),
        disabledReason,
      };
    });
}

function getHeroCooldownReadyTick(lane, heroKey) {
  if (!lane || !lane.heroCooldownReadyTicks || typeof lane.heroCooldownReadyTicks !== "object")
    return 0;
  return Math.max(0, Math.floor(Number(lane.heroCooldownReadyTicks[heroKey]) || 0));
}

function countActiveHeroDeployments(game, sourceLaneIndex, heroKey) {
  if (!game || !Array.isArray(game.lanes) || !heroKey)
    return 0;

  let total = 0;
  for (const lane of game.lanes) {
    if (!lane)
      continue;

    const collections = [lane.units, lane.spawnQueue];
    for (const collection of collections) {
      if (!Array.isArray(collection))
        continue;

      for (const unit of collection) {
        if (!unit || unit.hp <= 0 || !unit.isHero)
          continue;
        if (unit.sourceLaneIndex !== sourceLaneIndex)
          continue;
        if (unit.heroKey !== heroKey)
          continue;
        total += 1;
      }
    }
  }

  return total;
}

function countBuiltBarracksSites(lane) {
  if (!lane || !lane.barracksSiteStates || typeof lane.barracksSiteStates !== "object")
    return 0;

  let built = 0;
  for (const siteDef of BARRACKS_SITE_DEFS) {
    const state = lane.barracksSiteStates[siteDef.barracksId];
    if (state && state.isBuilt)
      built += 1;
  }
  return built;
}

function markLaneDefeated(game, lane, defeatContext = null) {
  if (!lane || lane.eliminated) return;
  lane.eliminated = true;
  const corePad = getLaneTownCorePad(lane);
  log.warn("[TownCoreTrace] lane eliminated", {
    tick: game && Number.isInteger(game.tick) ? game.tick : null,
    laneIndex: lane.laneIndex,
    side: lane.side,
    reason: defeatContext && defeatContext.reason ? defeatContext.reason : "town_core_destroyed",
    corePadId: corePad ? corePad.padId : null,
    coreHp: corePad ? Math.max(0, Math.floor(Number(corePad.hp) || 0)) : 0,
    coreMaxHp: corePad ? Math.max(1, Math.floor(Number(corePad.maxHp) || 1)) : 1,
    remainingActiveLanes: game && Array.isArray(game.lanes)
      ? game.lanes.filter((candidate) => candidate && candidate !== lane && !candidate.eliminated).map((candidate) => ({
          laneIndex: candidate.laneIndex,
          coreHp: getLaneTownCoreHp(candidate),
          coreMaxHp: getLaneTownCoreMaxHp(candidate),
        }))
      : [],
    preservedOccupiers: Array.isArray(lane.units)
      ? lane.units.filter((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit)).length
      : 0,
  });
  lane.units = (lane.units || []).filter((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit));
  lane.spawnQueue = (lane.spawnQueue || []).filter((unit) => shouldKeepUnitAfterLaneDefeat(lane, unit));
  lane.projectiles = [];
}

function recomputeTeamHpState(game) {
  if (!game || !Array.isArray(game.lanes)) return;

  const totals = { left: 0, right: 0 };
  const maxTotals = { left: 0, right: 0 };

  for (const lane of game.lanes) {
    if (!lane) continue;
    const hp = getLaneTownCoreHp(lane);
    const maxHp = getLaneTownCoreMaxHp(lane);
    lane.lives = hp;

    if (hp <= 0) markLaneDefeated(game, lane, { reason: "town_core_destroyed" });

    if (lane.side === "left" || lane.side === "right") {
      totals[lane.side] += hp;
      maxTotals[lane.side] += maxHp;
    }
  }

  game.teamHp = totals;
  game.teamHpMax = Math.max(maxTotals.left, maxTotals.right, 0);
}

function applyFortressPadDamage(game, lane, padId, damage) {
  if (!game || !lane || lane.eliminated || !padId) {
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };
  }

  const padState = getFortressPadState(lane, padId);
  if (!padState) {
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };
  }

  const prevHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  if (prevHp <= 0) {
    return { damageApplied: 0, destroyed: true, remainingHp: 0 };
  }

  const appliedDamage = Math.max(0, Math.floor(Number(damage) || 0));
  if (appliedDamage <= 0) {
    return { damageApplied: 0, destroyed: false, remainingHp: prevHp };
  }

  padState.hp = Math.max(0, prevHp - appliedDamage);
  const damageApplied = prevHp - padState.hp;
  if (padState.buildingType === "town_core" && damageApplied > 0)
    lane.lifeLossThisRound += damageApplied;

  recomputeTeamHpState(game);
  if (padState.buildingType === "town_core" && damageApplied > 0) {
    log.info("[TownCoreTrace] core damaged", {
      tick: game.tick,
      laneIndex: lane.laneIndex,
      padId,
      damageApplied,
      previousHp: prevHp,
      remainingHp: Math.max(0, Math.floor(Number(padState.hp) || 0)),
      maxHp: Math.max(1, Math.floor(Number(padState.maxHp) || 1)),
      laneEliminated: !!lane.eliminated,
      teamHp: game.teamHp,
      coreStates: buildTownCoreStateSummary(game),
    });
  }
  return {
    damageApplied,
    destroyed: padState.hp <= 0,
    remainingHp: Math.max(0, Math.floor(Number(padState.hp) || 0)),
  };
}

function resolveFortressBuildingMaxHp(buildingType, tier, teamHpStart) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  if (!buildingDef) return 0;
  if (buildingType === "town_core")
    return Math.max(1, Math.floor(Number(teamHpStart) || TEAM_HP_START));

  const safeTier = Math.max(1, Math.floor(Number(tier) || 1));
  const baseHp = Math.max(1, Number(buildingDef.baseMaxHp) || 100);
  return Math.floor(baseHp * (1 + 0.25 * (safeTier - 1)));
}

function getFortressRequiredTownCoreTier(buildingType, targetTier) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  const safeTier = Math.max(1, Math.floor(Number(targetTier) || 1));
  if (!buildingDef) return safeTier;

  const perTier = buildingDef.requiredTownCoreTierByTier;
  if (perTier && Number.isFinite(Number(perTier[safeTier])))
    return Math.max(1, Math.floor(Number(perTier[safeTier]) || 1));

  const explicitTier = Math.floor(Number(buildingDef.requiredTownCoreTier) || 0);
  if (explicitTier > 0) return explicitTier;

  return safeTier;
}

function getFortressDependencyRequirements(buildingType, targetTier) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  const safeTier = Math.max(1, Math.floor(Number(targetTier) || 1));
  if (!buildingDef || !buildingDef.dependencyRequirementsByTier)
    return [];

  const requirements = buildingDef.dependencyRequirementsByTier[safeTier];
  return Array.isArray(requirements) ? requirements : [];
}

function getFortressDependencyLockedReason(lane, buildingType, targetTier) {
  const requirements = getFortressDependencyRequirements(buildingType, targetTier);
  for (const requirement of requirements) {
    if (!requirement || !requirement.buildingType)
      continue;

    const requiredTier = Math.max(1, Math.floor(Number(requirement.minTier) || 1));
    const builtTier = getHighestBuiltFortressPadTier(lane, requirement.buildingType);
    if (builtTier >= requiredTier)
      continue;

    return `${getBuildingDisplayName(requirement.buildingType)}: ${getBuildingTierDisplayName(requirement.buildingType, requiredTier)}`;
  }

  return null;
}

function getFortressBuildCost(buildingType) {
  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  return buildingDef ? Math.max(0, Math.floor(Number(buildingDef.buildCost) || 0)) : 0;
}

function getFortressUpgradeCost(buildingType, nextTier) {
  if (buildingType === "barracks") {
    const nextDef = getBarracksUpgradeDef(nextTier);
    if (nextDef && Number.isFinite(nextDef.upgradeCost))
      return Math.max(0, Math.floor(Number(nextDef.upgradeCost)));

    const fallback = getBarracksLevelDef(nextTier);
    return Math.max(0, Math.floor(Number(fallback.cost) || 0));
  }

  const buildingDef = FORTRESS_BUILDING_DEFS[buildingType];
  if (!buildingDef || !buildingDef.upgradeCosts) return Infinity;
  const cost = buildingDef.upgradeCosts[nextTier];
  return Number.isFinite(cost) ? Math.max(0, Math.floor(cost)) : Infinity;
}

function describeFortressPad(game, lane, padState) {
  if (!padState) return null;
  const buildingDef = FORTRESS_BUILDING_DEFS[padState.buildingType];
  if (!buildingDef) return null;

  const currentTier = Math.max(0, Math.floor(Number(padState.tier) || 0));
  const maxTier = getFortressMaxTier(padState.buildingType);
  const built = currentTier > 0;
  const nextTier = Math.min(maxTier, currentTier + 1);
  const targetTier = built ? nextTier : 1;
  const townCoreTier = getTownCoreTier(lane);
  const requiredTownCoreTier = getFortressRequiredTownCoreTier(
    padState.buildingType,
    targetTier
  );
  const dependencyLockedReason = getFortressDependencyLockedReason(
    lane,
    padState.buildingType,
    targetTier
  );
  const canBuild = !built
    && townCoreTier >= requiredTownCoreTier
    && !dependencyLockedReason;
  const canUpgrade = built
    && currentTier < maxTier
    && townCoreTier >= requiredTownCoreTier
    && !dependencyLockedReason;

  let buildState = FORTRESS_BUILD_STATES.locked;
  if (!built) {
    buildState = canBuild ? FORTRESS_BUILD_STATES.availableToBuild : FORTRESS_BUILD_STATES.locked;
  } else if (currentTier >= maxTier) {
    buildState = FORTRESS_BUILD_STATES.maxTier;
  } else if (canUpgrade) {
    buildState = FORTRESS_BUILD_STATES.upgradeAvailable;
  } else {
    buildState = FORTRESS_BUILD_STATES.built;
  }

  let lockedReason = null;
  if (!canBuild && !canUpgrade && buildState === FORTRESS_BUILD_STATES.locked) {
    lockedReason = dependencyLockedReason
      ? `Requires ${dependencyLockedReason}`
      : `Requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  } else if (built && currentTier < maxTier && !canUpgrade) {
    lockedReason = dependencyLockedReason
      ? `Upgrade requires ${dependencyLockedReason}`
      : `Upgrade requires Civic: ${getBuildingTierDisplayName("town_core", requiredTownCoreTier)}`;
  }

  return {
    buildingDef,
    buildState,
    canBuild,
    canUpgrade,
    lockedReason,
    currentTier,
    maxTier,
    buildCost: getFortressBuildCost(padState.buildingType),
    nextUpgradeCost: built && currentTier < maxTier
      ? getFortressUpgradeCost(padState.buildingType, nextTier)
      : 0,
    requiredTownCoreTier,
  };
}

function getBarracksRosterDefinition(rosterKey) {
  return BARRACKS_ROSTER_DEFS.find((entry) => entry.rosterKey === rosterKey) || null;
}

function getBarracksRosterBuyCost(rosterDef) {
  if (!rosterDef) return 0;
  const presentation = resolveFortPresentationConfig(
    rosterDef.archetypeKey,
    rosterDef.presentationKey,
    rosterDef.displayName,
  );
  const unitType = getUnitType(presentation.catalogUnitKey);
  const buildCost = Math.floor(Number(unitType && unitType.build_cost) || 0);
  if (buildCost > 0) return buildCost;
  const towerDef = resolveTowerDef(rosterDef.archetypeKey);
  if (towerDef && Number.isFinite(towerDef.cost) && towerDef.cost > 0)
    return Math.floor(towerDef.cost);
  const unitDef = resolveUnitDef(rosterDef.archetypeKey);
  const sendCost = Math.max(1, Math.floor(Number(unitDef && unitDef.cost) || 1));
  return Math.max(5, sendCost * 5);
}

function getBarracksRosterSellRefund(rosterDef) {
  const buyCost = getBarracksRosterBuyCost(rosterDef);
  if (buyCost <= 0) return 0;
  return Math.max(1, Math.floor(buyCost * BARRACKS_ROSTER_REFUND_PCT / 100));
}

function getBarracksRoleSortIndex(role) {
  return BARRACKS_ROLE_SORT_ORDER[role] ?? 99;
}

function getBarracksSpawnQueueRoleSortIndex(role) {
  const index = BARRACKS_SPAWN_ROLE_ORDER.indexOf(role);
  return index >= 0 ? index : 99;
}

function createBarracksSiteRosterSnapshot(game, lane, barracksId) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId) return [];
  const descriptor = describeBarracksSite(game, lane, normalizedId);
  if (!descriptor) return [];

  const siteCounts = getBarracksSiteCounts(lane, normalizedId) || {};
  const siteBuilt = !!descriptor.isBuilt;

  return BARRACKS_ROSTER_DEFS
    .slice()
    .sort((a, b) => {
      const roleA = getBarracksRoleSortIndex(a.role);
      const roleB = getBarracksRoleSortIndex(b.role);
      if (roleA !== roleB) return roleA - roleB;
      return (a.sortIndex || 0) - (b.sortIndex || 0);
    })
    .map((rosterDef) => {
      const presentation = resolveFortPresentationConfig(
        rosterDef.archetypeKey,
        rosterDef.presentationKey,
        rosterDef.displayName,
      );
      const unlockContext = resolveBarracksRosterUnlockContext(lane, rosterDef, normalizedId);
      const unlocked = siteBuilt && unlockContext.unlocked;
      const buildingDef = unlockContext.buildingDef;
      const buyCost = getBarracksRosterBuyCost(rosterDef);
      const sellRefund = getBarracksRosterSellRefund(rosterDef);
      const ownedCount = Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
      return {
        rosterKey: rosterDef.rosterKey,
        displayName: rosterDef.displayName,
        role: rosterDef.role,
        roleLabel: rosterDef.role.charAt(0).toUpperCase() + rosterDef.role.slice(1),
        sortIndex: rosterDef.sortIndex || 0,
        archetypeKey: rosterDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: rosterDef.branchKey || "",
        branchLabel: rosterDef.branchLabel || getBuildingBranchLabel(rosterDef.productionBuildingType),
        productionBuildingType: rosterDef.productionBuildingType,
        productionBuildingName: getBuildingDisplayName(rosterDef.productionBuildingType),
        tier: Math.max(1, Math.floor(Number(rosterDef.tier) || 1)),
        ownedCount,
        buyCost,
        sellRefund,
        unlocked,
        unlockBuildingType: rosterDef.unlockBuildingType,
        unlockBuildingName: buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType,
        unlockBuildingTierName: getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier),
        requiredBuildingTier: rosterDef.requiredBuildingTier,
        unlockPadId: unlockContext.unlockPadId,
        barracksId: normalizedId,
        lockedReason: unlocked
          ? null
          : !siteBuilt
            ? descriptor.lockedReason || "Buy Building first"
            : getBarracksRosterLockedReason(rosterDef),
      };
    });
}

function createBarracksSiteSnapshot(game, lane, barracksId) {
  const normalizedId = normalizeBarracksSiteId(barracksId);
  if (!normalizedId) return null;
  const descriptor = describeBarracksSite(game, lane, normalizedId);
  if (!descriptor) return null;
  const siteState = descriptor.siteState;
  const hp = descriptor.isBuilt ? Math.max(0, Math.floor(Number(siteState.hp) || 0)) : 0;
  const maxHp = descriptor.isBuilt ? Math.max(0, Math.floor(Number(siteState.maxHp) || 0)) : 0;

  return {
    barracksId: normalizedId,
    allegianceKey: resolveLaneAllegianceKey(lane),
    ownerLaneIndex: lane && Number.isInteger(lane.laneIndex) ? lane.laneIndex : -1,
    displayName: descriptor.siteDef.displayName,
    slot: descriptor.siteDef.slot,
    sortIndex: descriptor.siteDef.sortIndex,
    requiredBarracksTier: getBarracksSiteTierRequirement(descriptor.siteDef),
    available: descriptor.isBuilt,
    isBuilt: descriptor.isBuilt,
    level: descriptor.level,
    maxLevel: descriptor.maxLevel,
    buildState: descriptor.buildState,
    canBuild: descriptor.canBuild,
    canUpgrade: descriptor.canUpgrade,
    buildCost: descriptor.buildCost,
    upgradeCost: descriptor.upgradeCost,
    requiredTownCoreTier: descriptor.requiredTownCoreTier,
    requiredTownCoreTierName: getBuildingTierDisplayName("town_core", descriptor.requiredTownCoreTier),
    sendIntervalTicks: descriptor.sendIntervalTicks,
    sendTimerTicksRemaining: descriptor.sendTimerTicksRemaining,
    sendTimerTotalTicks: descriptor.sendTimerTotalTicks,
    lockedReason: descriptor.lockedReason,
    hp,
    maxHp,
    roster: createBarracksSiteRosterSnapshot(game, lane, normalizedId),
  };
}

function createBarracksRosterSnapshot(game, lane) {
  return BARRACKS_ROSTER_DEFS
    .slice()
    .sort((a, b) => {
      const roleA = getBarracksRoleSortIndex(a.role);
      const roleB = getBarracksRoleSortIndex(b.role);
      if (roleA !== roleB) return roleA - roleB;
      return (a.sortIndex || 0) - (b.sortIndex || 0);
    })
    .map((rosterDef) => {
      const unlockContext = resolveBarracksRosterUnlockContext(lane, rosterDef);
      const unlocked = unlockContext.unlocked;
      const buildingDef = unlockContext.buildingDef;
      const buyCost = getBarracksRosterBuyCost(rosterDef);
      const sellRefund = getBarracksRosterSellRefund(rosterDef);
      let ownedCount = 0;
      for (const siteDef of BARRACKS_SITE_DEFS) {
        const siteCounts = getBarracksSiteCounts(lane, siteDef.barracksId) || {};
        ownedCount += Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
      }
      const presentation = resolveFortPresentationConfig(
        rosterDef.archetypeKey,
        rosterDef.presentationKey,
        rosterDef.displayName,
      );

      return {
        rosterKey: rosterDef.rosterKey,
        displayName: rosterDef.displayName,
        role: rosterDef.role,
        roleLabel: rosterDef.role.charAt(0).toUpperCase() + rosterDef.role.slice(1),
        sortIndex: rosterDef.sortIndex || 0,
        archetypeKey: rosterDef.archetypeKey,
        presentationKey: presentation.presentationKey,
        unitTypeKey: presentation.catalogUnitKey,
        catalogUnitKey: presentation.catalogUnitKey,
        skinKey: presentation.skinKey || null,
        portraitKey: presentation.portraitKey || null,
        branchKey: rosterDef.branchKey || "",
        branchLabel: rosterDef.branchLabel || getBuildingBranchLabel(rosterDef.productionBuildingType),
        productionBuildingType: rosterDef.productionBuildingType,
        productionBuildingName: getBuildingDisplayName(rosterDef.productionBuildingType),
        tier: Math.max(1, Math.floor(Number(rosterDef.tier) || 1)),
        ownedCount,
        buyCost,
        sellRefund,
        unlocked,
        unlockBuildingType: rosterDef.unlockBuildingType,
        unlockBuildingName: buildingDef ? buildingDef.displayName : rosterDef.unlockBuildingType,
        unlockBuildingTierName: getBuildingTierDisplayName(rosterDef.unlockBuildingType, rosterDef.requiredBuildingTier),
        requiredBuildingTier: rosterDef.requiredBuildingTier,
        unlockPadId: unlockContext.unlockPadId,
        lockedReason: unlocked
          ? null
          : getBarracksRosterLockedReason(rosterDef),
      };
    });
}

function createFortressPadSnapshot(game, lane, padState) {
  const descriptor = describeFortressPad(game, lane, padState);
  if (!descriptor) return null;
  const buildingDef = descriptor.buildingDef;
  const currentTier = descriptor.currentTier;
  const built = currentTier > 0;
  const currentHp = Math.max(0, Math.floor(Number(padState.hp) || 0));
  const maxHp = Math.max(0, Math.floor(Number(padState.maxHp) || 0));

  return {
    padId: padState.padId,
    allegianceKey: resolveLaneAllegianceKey(lane),
    ownerLaneIndex: lane && Number.isInteger(lane.laneIndex) ? lane.laneIndex : -1,
    gridX: padState.gridX,
    gridY: padState.gridY,
    buildingType: padState.buildingType,
    buildingName: buildingDef.displayName,
    displayName: padState.displayName,
    branchKey: buildingDef.branchKey || padState.buildingType,
    branchLabel: getBuildingBranchLabel(padState.buildingType),
    buildState: descriptor.buildState,
    tier: currentTier,
    maxTier: descriptor.maxTier,
    currentTierName: getBuildingTierDisplayName(padState.buildingType, currentTier),
    nextTier: currentTier < descriptor.maxTier ? currentTier + 1 : currentTier,
    nextTierName: currentTier < descriptor.maxTier
      ? getBuildingTierDisplayName(padState.buildingType, currentTier + 1)
      : getBuildingTierDisplayName(padState.buildingType, currentTier),
    isBuilt: built,
    canBuild: descriptor.canBuild,
    canUpgrade: descriptor.canUpgrade,
    buildCost: descriptor.buildCost,
    upgradeCost: Number.isFinite(descriptor.nextUpgradeCost) ? descriptor.nextUpgradeCost : 0,
    requiredTownCoreTier: descriptor.requiredTownCoreTier,
    requiredTownCoreTierName: getBuildingTierDisplayName("town_core", descriptor.requiredTownCoreTier),
    hp: currentHp,
    maxHp,
    lockedReason: descriptor.lockedReason,
  };
}

function buildBarracksRosterSpawnEntries(game, lane, barracksId = null) {
  if (!game || !lane) return [];

  const spawnEntries = [];
  const siteDefs = barracksId
    ? [getBarracksSiteDef(barracksId)].filter(Boolean)
    : BARRACKS_SITE_DEFS;
  for (const siteDef of siteDefs) {
    const descriptor = describeBarracksSite(game, lane, siteDef.barracksId);
    if (!descriptor || !descriptor.isBuilt)
      continue;

    const rosterEntries = createBarracksSiteRosterSnapshot(game, lane, siteDef.barracksId)
      .filter((entry) => entry.unlocked && entry.ownedCount > 0)
      .sort((a, b) => {
        const roleA = getBarracksSpawnQueueRoleSortIndex(a.role);
        const roleB = getBarracksSpawnQueueRoleSortIndex(b.role);
        if (roleA !== roleB) return roleA - roleB;
        return (a.sortIndex || 0) - (b.sortIndex || 0);
      });

    for (const entry of rosterEntries) {
      const rosterDef = getBarracksRosterDefinition(entry.rosterKey);
      const presentation = resolveFortPresentationConfig(
        rosterDef && rosterDef.archetypeKey,
        rosterDef && rosterDef.presentationKey,
        entry.displayName,
      );
      for (let ownedIndex = 0; ownedIndex < entry.ownedCount; ownedIndex++) {
        spawnEntries.push({
          unitType: rosterDef && rosterDef.archetypeKey ? rosterDef.archetypeKey : entry.unitTypeKey,
          count: 1,
          source: "barracks_roster",
          barracksId: siteDef.barracksId,
          rosterKey: entry.rosterKey,
          archetypeKey: rosterDef && rosterDef.archetypeKey ? rosterDef.archetypeKey : null,
          presentationKey: presentation.presentationKey,
          role: entry.role,
          sortIndex: entry.sortIndex || 0,
          sourceLaneIndex: lane.laneIndex,
          sourceTeam: lane.team || null,
          skinKey: presentation.skinKey || null,
        });
      }
    }
  }

  return spawnEntries;
}

function spawnScheduledBarracksRoster(game, lane, barracksId = null) {
  if (!game || !lane || lane.eliminated) return 0;
  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!normalizedBarracksId) {
    log.error("[BarracksTrace][ServerSpawn] rejected", {
      reason: "Missing or invalid barracksId",
      sourceLaneIndex: lane.laneIndex,
      requestedBarracksId: barracksId || null,
    });
    return 0;
  }

  const targetLaneIdx = resolveTargetLaneForBarracksSend(game, lane.laneIndex, normalizedBarracksId);
  if (targetLaneIdx === null) {
    log.error("[BarracksTrace][ServerSpawn] rejected", {
      reason: "Unable to resolve target lane for barracks send",
      sourceLaneIndex: lane.laneIndex,
      barracksId: normalizedBarracksId,
    });
    return 0;
  }

  const targetLane = game.lanes[targetLaneIdx];
  if (!targetLane) {
    log.error("[BarracksTrace][ServerSpawn] rejected", {
      reason: "Resolved target lane is missing",
      sourceLaneIndex: lane.laneIndex,
      targetLaneIndex: targetLaneIdx,
      barracksId: normalizedBarracksId,
    });
    return 0;
  }

  const spawnEntries = buildBarracksRosterSpawnEntries(game, lane, normalizedBarracksId);
  if (spawnEntries.length === 0) return 0;

  spawnBarracksRosterFormation(game, targetLane, spawnEntries);
  lane.totalSendCount += spawnEntries.length;
  lane.sendCountThisRound += spawnEntries.length;
  return spawnEntries.length;
}

function getBarracksFormationColumns(role, unitCount) {
  const safeCount = Math.max(0, Math.floor(Number(unitCount) || 0));
  if (safeCount <= 1) return 1;
  if (safeCount === 2) return 2;
  if (safeCount === 3) return 3;

  switch (role) {
    case "melee":
      return Math.min(4, safeCount);
    case "support":
    case "ranged":
      return Math.min(3, safeCount);
    default:
      return Math.min(2, safeCount);
  }
}

function spawnBarracksRosterFormation(game, lane, pendingEntries) {
  if (!game || !lane || !Array.isArray(pendingEntries) || pendingEntries.length === 0) return;

  const sourceLaneIndex = Number.isInteger(pendingEntries[0] && pendingEntries[0].sourceLaneIndex)
    ? pendingEntries[0].sourceLaneIndex
    : -1;
  const sourceBarracksId = normalizeBarracksSiteId(pendingEntries[0] && pendingEntries[0].barracksId) || "center";
  const groupId = createSpawnPacketId(game, sourceLaneIndex, sourceBarracksId, "packet");
  const groupSpawnOrdinal = Math.max(1, Math.floor(Number(game && game.nextPacketId) || 1) - 1);
  const groupSpawnTick = Math.floor(Number(game && game.tick) || 0);

  const entriesByRole = new Map();
  for (const role of BARRACKS_SPAWN_ROLE_ORDER) entriesByRole.set(role, []);
  for (const entry of pendingEntries) {
    if (!entry) continue;
    const roleEntries = entriesByRole.get(entry.role) || [];
    roleEntries.push(entry);
    entriesByRole.set(entry.role, roleEntries);
  }

  let nextRoleRow = Math.ceil(lane.spawnQueue.length / GRID_W);
  for (const role of BARRACKS_SPAWN_ROLE_ORDER) {
    const roleEntries = (entriesByRole.get(role) || [])
      .slice()
      .sort((a, b) => {
        const sortA = Math.floor(Number(a.sortIndex) || 0);
        const sortB = Math.floor(Number(b.sortIndex) || 0);
        if (sortA !== sortB) return sortA - sortB;
        return String(a.rosterKey || "").localeCompare(String(b.rosterKey || ""));
      });
    if (roleEntries.length === 0) continue;

    const columns = getBarracksFormationColumns(role, roleEntries.length);
    const startCol = Math.max(0, Math.floor((GRID_W - columns) / 2));

    for (let i = 0; i < roleEntries.length; i++) {
      const rowOffset = Math.floor(i / columns);
      const columnOffset = i % columns;
      const spawnIndex = ((nextRoleRow + rowOffset) * GRID_W) + startCol + columnOffset;
      const entry = roleEntries[i];
      const combatRole = normalizeCombatRole(entry.combatRole || entry.role) || resolveUnitCombatRole(entry);
      _spawnWaveUnit(game, lane, {
        unit_type: entry.unitType,
        spawn_qty: 1,
        hp_mult: 1,
        dmg_mult: 1,
        speed_mult: 1,
        sourceLaneIndex: entry.sourceLaneIndex,
        sourceTeam: entry.sourceTeam,
        sourceBarracksId: entry.barracksId,
        rosterKey: entry.rosterKey || null,
        role: entry.role || null,
        combatRole,
        preferredBand: resolvePreferredBandForCombatRole(combatRole),
        skinKey: entry.skinKey || null,
        groupId,
        groupSpawnOrdinal,
        groupSpawnTick,
        spawnIndex,
      });
    }

    nextRoleRow += Math.ceil(roleEntries.length / columns);
  }
}

// ── Tower stat helpers ─────────────────────────────────────────────────────────

function getTowerUpgradeCost(type, nextLevel) {
  const base = resolveTowerDef(type);
  if (!base) return Infinity;
  return Math.ceil(base.cost * (0.75 + 0.25 * nextLevel));
}

function getTowerTotalCost(type, level) {
  const base = resolveTowerDef(type);
  if (!base) return 0;
  let total = base.cost; // direct placement cost (no wall step)
  for (let lvl = 2; lvl <= level; lvl++) {
    total += getTowerUpgradeCost(type, lvl);
  }
  return total;
}

function getTowerSellValue(type, level) {
  return Math.floor(getTowerTotalCost(type, level) * 0.7);
}

function getTowerStats(type, level) {
  const base = resolveTowerDef(type);
  if (!base) return null;
  const lvl = Math.max(1, Math.min(TOWER_MAX_LEVEL, level));
  const s = lvl - 1;
  return {
    dmg: base.dmg * (1 + 0.12 * s),
    range: base.range * (1 + 0.015 * s),
    atkCdTicks: Math.max(5, Math.round(base.atkCdTicks * (1 - 0.015 * s))),
    projectileTicks:    base.projectileTicks,
    damageType:         base.damageType,
    projBehavior:       base.projBehavior       || (base.isSplash ? "splash" : "single"),
    projBehaviorParams: base.projBehaviorParams  || (base.isSplash ? { radius: SPLASH_RADIUS_TILES } : {}),
    isSplash: base.isSplash || false,
  };
}

// ── Grid helpers ──────────────────────────────────────────────────────────────

function makeGrid() {
  const grid = [];
  for (let x = 0; x < GRID_W; x++) {
    grid[x] = [];
    for (let y = 0; y < GRID_H; y++) {
      grid[x][y] = { type: "empty", towerType: null, towerLevel: 0, atkCd: 0, targetMode: "first" };
    }
  }
  grid[SPAWN_X][SPAWN_YG].type = "spawn";
  grid[CASTLE_X][CASTLE_YG].type = "castle";
  return grid;
}

// Straight-line path from spawn (SPAWN_X, 0) to castle (CASTLE_X, CASTLE_YG).
// Walls no longer exist; path is always the fixed vertical centre column.
function straightLinePath() {
  const path = [];
  for (let y = SPAWN_YG; y <= CASTLE_YG; y++) {
    path.push({ x: SPAWN_X, y });
  }
  return path;
}

function tileDist(ax, ay, bx, by) {
  const dx = ax - bx, dy = ay - by;
  return Math.sqrt(dx * dx + dy * dy);
}

function hasExplicitMlOption(src, key) {
  return Object.prototype.hasOwnProperty.call(src, key)
    && src[key] !== undefined
    && src[key] !== null
    && src[key] !== "";
}

function normalizeMlOptionNumber(src, key, defaultValue, min, max, integer = false) {
  if (!hasExplicitMlOption(src, key))
    return defaultValue;

  const n = Number(src[key]);
  if (!Number.isFinite(n)) {
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected a finite number.`);
  }
  if (n < min || n > max) {
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected ${min}-${max}.`);
  }
  if (integer && !Number.isInteger(n)) {
    throw new Error(`[multilane-config] Invalid game option '${key}'; expected a whole number.`);
  }
  return Math.max(min, Math.min(max, n));
}

function cloneSlotDef(slot, laneTeam) {
  const canonicalLaneTeam = normalizeAllegianceKey(laneTeam || slot.team || slot.slotColor || (slot.side === "left" ? "red" : "blue"));
  const canonicalSlotColor = normalizeAllegianceKey(slot.slotColor) || canonicalLaneTeam;
  return {
    laneIndex: slot.laneIndex,
    slotKey: slot.slotKey,
    side: slot.side,
    slotColor: canonicalSlotColor || slot.slotColor,
    branchId: slot.branchId,
    branchLabel: slot.branchLabel,
    castleSide: slot.castleSide,
    team: canonicalLaneTeam || ALLEGIANCE_KEYS.RED,
    allegianceKey: canonicalLaneTeam || ALLEGIANCE_KEYS.RED,
  };
}

// For 2-player, seat players on the authored Red vs Yellow branches.
// Blue and Green stay unoccupied and function as the outer-loop travel space between them.
const TWO_PLAYER_SLOT_BASES = [
  FIXED_SLOT_LAYOUT[0],
  Object.assign({}, FIXED_SLOT_LAYOUT[1], {
    laneIndex: 1,
    side: "right",
    castleSide: "left",
  }),
];

function getDefaultSlotDefinitions(playerCount, laneTeams) {
  const safeCount = Math.max(0, Math.floor(Number(playerCount) || 0));
  const defs = [];
  for (let i = 0; i < safeCount; i++) {
    const base = safeCount === 2
      ? (TWO_PLAYER_SLOT_BASES[i] || FIXED_SLOT_LAYOUT[i])
      : (FIXED_SLOT_LAYOUT[i] || {
          laneIndex: i,
          slotKey: `slot_${i}`,
          side: i % 2 === 0 ? "left" : "right",
          slotColor: `slot-${i}`,
          branchId: `branch_${i}`,
          branchLabel: `Branch ${i + 1}`,
          castleSide: i % 2 === 0 ? "right" : "left",
        });
    defs.push(cloneSlotDef(base, laneTeams && laneTeams[i]));
  }
  return defs;
}

function getBattlefieldTopology(playerCount, laneTeams) {
  return {
    mapType: BATTLEFIELD_TOPOLOGY.mapType,
    centerIslandId: BATTLEFIELD_TOPOLOGY.centerIslandId,
    sideOrder: BATTLEFIELD_TOPOLOGY.sideOrder.slice(),
    castles: BATTLEFIELD_TOPOLOGY.castles.map((castle) => Object.assign({}, castle)),
    mergeZones: BATTLEFIELD_TOPOLOGY.mergeZones.map((zone) => Object.assign({}, zone)),
    buildZones: BATTLEFIELD_TOPOLOGY.buildZones
      .filter((zone) => zone.ownerLaneIndex < playerCount)
      .map((zone) => Object.assign({}, zone)),
    sharedZonesBuildable: !!BATTLEFIELD_TOPOLOGY.sharedZonesBuildable,
    slotDefinitions: getDefaultSlotDefinitions(playerCount, laneTeams),
  };
}

function normalizeGameOptions(options) {
  const src = options && typeof options === "object" ? options : {};
  const runtime = getMlRuntimeSettings();
  const laneTeamsRaw = Array.isArray(src.laneTeams) ? src.laneTeams : [];
  const laneTeams = laneTeamsRaw.map((team, idx) => {
    const normalized = normalizeAllegianceKey(team);
    if (normalized) return normalized;
    return [ALLEGIANCE_KEYS.RED, ALLEGIANCE_KEYS.YELLOW, ALLEGIANCE_KEYS.BLUE, ALLEGIANCE_KEYS.GREEN][idx] || `p${idx}`;
  });
  return {
    startGold: normalizeMlOptionNumber(src, "startGold", runtime.startGold, 0, 10000, true),
    startIncome: normalizeMlOptionNumber(src, "startIncome", runtime.startIncome, 0, 1000),
    livesStart: normalizeMlOptionNumber(src, "livesStart", runtime.livesStart, 1, 1000, true),
    teamHpStart: normalizeMlOptionNumber(src, "teamHpStart", runtime.teamHpStart, 1, 1000, true),
    buildPhaseTicks: normalizeMlOptionNumber(src, "buildPhaseTicks", runtime.buildPhaseTicks, 20, 7200, true),
    transitionPhaseTicks: normalizeMlOptionNumber(src, "transitionPhaseTicks", runtime.transitionPhaseTicks, 20, 7200, true),
    laneTeams,
    matchSeed: typeof src.matchSeed === "number" ? (src.matchSeed >>> 0) : undefined,
    battlefieldTopology: getBattlefieldTopology(Number(src.playerCount) || laneTeams.length || 4, laneTeams),
  };
}

// Get current tile position of a unit from its pathIdx
function getUnitTilePos(unit, path) {
  if (unit && Number.isFinite(Number(unit.posX)) && Number.isFinite(Number(unit.posY))) {
    return {
      x: Number(unit.posX),
      y: Number(unit.posY),
    };
  }
  if (!path || path.length === 0) return null;
  const idx = Math.min(Math.floor(unit.pathIdx), path.length - 1);
  return path[idx];
}

// ── Public API ────────────────────────────────────────────────────────────────

function createMLGame(playerCount, options) {
  const opt = normalizeGameOptions(options);
  const battlefieldTopology = getBattlefieldTopology(playerCount, opt.laneTeams);
  const slotDefinitions = battlefieldTopology.slotDefinitions;
  const lanes = [];
  for (let i = 0; i < playerCount; i++) {
    const grid = makeGrid();
    const routeToCore = buildRouteSegments(ROUTE_TYPES.WAVE_LANE, getWaveSpawnNodeId(i), getLaneNodeId(i));
    // Legacy compatibility path samples for systems that still read lane.path/pathIdx.
    // Live movement authority remains routeSegments/currentSegment/segmentProgress.
    const path = buildSampledPathFromSegments(routeToCore, 28);
    const slot = slotDefinitions[i] || cloneSlotDef({
      laneIndex: i,
      slotKey: `slot_${i}`,
      side: i % 2 === 0 ? "left" : "right",
      slotColor: `slot-${i}`,
      branchId: `branch_${i}`,
      branchLabel: `Branch ${i + 1}`,
      castleSide: i % 2 === 0 ? "right" : "left",
    }, opt.laneTeams[i]);
    lanes.push({
      laneIndex: i,
      team: slot.team,
      allegianceKey: normalizeAllegianceKey(slot.team),
      side: slot.side,
      slotKey: slot.slotKey,
      slotColor: slot.slotColor,
      branchId: slot.branchId,
      branchLabel: slot.branchLabel,
      castleSide: slot.castleSide,
      eliminated: false,
      gold: opt.startGold + opt.startIncome,
      income: opt.startIncome,
      incomeRemainder: 0,
      lives: opt.livesStart,
      totalSendSpend: 0,
      totalSendCount: 0,
      totalBuildSpend: 0,
      totalLeaksTaken: 0,
      biggestLeakTaken: 0,
      wavesHeld: 0,
      wavesLeaked: 0,
      currentHoldStreak: 0,
      longestHoldStreak: 0,
      leakCountThisRound: 0,
      lifeLossThisRound: 0,
      sendCountThisRound: 0,
      sendSpendThisRound: 0,
      buildSpendThisRound: 0,
      grid,
      path,
      fullPath: buildSampledPathFromSegments(routeToCore, 56),
      barracks: Object.assign({ level: 1 }, getBarracksLevelDef(1)),
      waveSpeedMult: 1,
      fortressPads: createFortressPadStates(opt.teamHpStart),
      barracksSiteStates: createBarracksSiteStates(opt.teamHpStart, 1),
      barracksSiteRosterCounts: createBarracksSiteRosterCounts(),
      heroCooldownReadyTicks: {},
      units: [],
      spawnQueue: [],
      projectiles: [],
      loadoutKeys: null,
      commandState: LANE_COMMAND_STATES.DEFEND,
      commandTargetLaneIndex: Number.isInteger(OPPOSING_LANE_INDEX[i]) && OPPOSING_LANE_INDEX[i] < playerCount
        ? OPPOSING_LANE_INDEX[i]
        : (playerCount > 1 ? ((i + 1) % playerCount) : i),
      commandAnchorProgress: 0,
      formationAnchorProgress: 0,
      formationAnchor: null,
      formationFacing: null,
      formationSlots: [],
      assignedUnits: [],
      formationUnitOrder: [],
      packetOrder: [],
      packetUnitOrder: {},
      packetStates: [],
      insideGateAnchor: null,
      outsideGateAnchor: null,
      enemyCoreAnchor: null,
      engagementRadius: LANE_COMMAND_COMBAT_LEASH,
      combatEnabled: true,
    });
  }
  const game = {
    tick: 0,
    phase: "playing",
    winner: null,
    matchState: "active_survival",
    officialWinnerLane: null,
    officialWinningTeam: null,
    officialWinningSide: null,
    losingTeam: null,
    losingSide: null,
    nextPacketId: 1,
    awaitingPostWinDecision: false,
    continuedIntoSurvival: true,
    pvpResolvedAtTick: null,
    survivalStartedAtTick: 0,
    survivalStartRound: 1,
    finalGameOverReason: null,
    finalGameOverDebug: null,
    playerCount,
    lanes,
    battlefieldTopology,
    // Wave defense state
    teamHp: { left: opt.teamHpStart, right: opt.teamHpStart },
    teamHpMax: opt.teamHpStart,
    buildPhaseTicks: opt.buildPhaseTicks,
    transitionPhaseTicks: opt.transitionPhaseTicks,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    barracksSendIntervalTicks: BARRACKS_SEND_TIMER_TICKS,
    waveIntervalTicks: WAVE_TIMER_TICKS,
    roundState: "combat",
    roundNumber: 1,
    roundStateTicks: 0,
    nextIncomeTick: INCOME_INTERVAL_TICKS,
    nextBarracksSendTick: BARRACKS_SEND_TIMER_TICKS,
    nextWaveTick: WAVE_TIMER_TICKS,
    lastWaveSpawnTick: null,
    hasSpawnedWave: false,
    waveConfig: [],      // loaded at match start by multilaneRuntime
    roundSnapshots: [],        // one entry per completed wave + one terminal entry
    startedAt: null,           // set on first live tick (not at object creation)
    finalSnapshotCaptured: false,
    _pendingEvents: [],  // drained by runtime each tick
    nextUnitId: 1,
    nextProjectileId: 1,
    // Phase B: seeded RNG + versioning + action sequencing
    rng: mulberry32(opt.matchSeed !== undefined ? opt.matchSeed : (Date.now() >>> 0)),
    matchSeed: opt.matchSeed !== undefined ? opt.matchSeed : null,
    configVersionId: null,
    actionSeq: 0,
  };
  recomputeTeamHpState(game);
  return game;
}

function parseBulkTiles(rawTiles) {
  if (!Array.isArray(rawTiles)) return { ok: false, reason: "Tiles must be an array" };
  if (rawTiles.length === 0) return { ok: false, reason: "No tiles selected" };
  if (rawTiles.length > 150) return { ok: false, reason: "Too many tiles selected" };

  const dedup = new Map();
  for (const raw of rawTiles) {
    const gx = Number((raw && raw.gridX !== undefined) ? raw.gridX : raw && raw.x);
    const gy = Number((raw && raw.gridY !== undefined) ? raw.gridY : raw && raw.y);
    if (!Number.isInteger(gx) || !Number.isInteger(gy) || gx < 0 || gx >= GRID_W || gy < 0 || gy >= GRID_H) {
      return { ok: false, reason: "Invalid grid position in tiles list" };
    }
    dedup.set(gx + "," + gy, { gx, gy });
  }
  return { ok: true, tiles: Array.from(dedup.values()) };
}

function getLaneBuildValue(lane) {
  let total = 0;
  if (Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      if (!pad || !Array.isArray(pad.costHistory)) continue;
      for (const entry of pad.costHistory)
        total += Number(entry && entry.cost) || 0;
    }
  }
  if (lane.barracksSiteStates && typeof lane.barracksSiteStates === "object") {
    for (const siteState of Object.values(lane.barracksSiteStates)) {
      if (!siteState || !Array.isArray(siteState.costHistory)) continue;
      for (const entry of siteState.costHistory)
        total += Number(entry && entry.cost) || 0;
    }
  }
  if (lane.barracksSiteRosterCounts) {
    for (const siteDef of BARRACKS_SITE_DEFS) {
      const siteCounts = getBarracksSiteCounts(lane, siteDef.barracksId) || {};
      for (const rosterDef of BARRACKS_ROSTER_DEFS) {
        const ownedCount = Math.max(0, Math.floor(Number(siteCounts[rosterDef.rosterKey]) || 0));
        total += ownedCount * getBarracksRosterBuyCost(rosterDef);
      }
    }
  }
  return total;
}

function getLaneWaveResult(lane) {
  if (!lane) return "Unknown";
  if (lane.eliminated && lane.lifeLossThisRound > 0) return "Defeated";
  if (lane.leakCountThisRound >= 5 || lane.lifeLossThisRound >= 5) return "Crushed";
  if (lane.leakCountThisRound > 0 || lane.lifeLossThisRound > 0) return "Leaked";
  return "Held";
}

function createRoundSnapshotLane(game, lane) {
  return {
    laneIndex: lane.laneIndex,
    income: lane.income,
    buildValue: getLaneBuildValue(lane),
    gold: Math.floor(lane.gold),
    leaksTaken: lane.leakCountThisRound,
    leakDamage: lane.lifeLossThisRound,
    sendSpend: lane.sendSpendThisRound,
    sendCount: lane.sendCountThisRound,
    buildSpend: lane.buildSpendThisRound,
    lives: lane.lives,
    teamHp: lane.lives,
    eliminated: lane.eliminated,
    holdResult: getLaneWaveResult(lane),
  };
}

function isOpponentLane(game, sourceLaneIndex, targetLaneIndex) {
  const sourceLane = game && game.lanes && game.lanes[sourceLaneIndex];
  const targetLane = game && game.lanes && game.lanes[targetLaneIndex];
  if (!sourceLane || !targetLane) return false;
  if (targetLane.eliminated) return false;
  if (sourceLaneIndex === targetLaneIndex) return false;
  return areAllegiancesHostile(resolveLaneAllegianceKey(sourceLane), resolveLaneAllegianceKey(targetLane));
}

function findNextActiveOpponentLaneIndex(game, fromLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || game.lanes.length <= 1) return null;
  const total = Math.min(Number(game.playerCount) || game.lanes.length, game.lanes.length);
  for (let step = 1; step < total; step++) {
    const idx = (fromLaneIndex + step) % total;
    if (isOpponentLane(game, fromLaneIndex, idx)) return idx;
  }
  return null;
}

function applyFortressBuildOnPad(game, lane, padId) {
  const padState = getFortressPadState(lane, padId);
  if (!padState) return { ok: false, reason: "Unknown building pad" };
  if (padState.buildingType === "barracks")
    return { ok: false, reason: "Select Barracks Left, Center, or Right to buy that building." };
  const descriptor = describeFortressPad(game, lane, padState);
  if (!descriptor) return { ok: false, reason: "Unknown building pad" };
  if (!descriptor.canBuild) return { ok: false, reason: descriptor.lockedReason || "Building is not available" };
  if (lane.gold < descriptor.buildCost) return { ok: false, reason: "Not enough gold" };

  lane.gold -= descriptor.buildCost;
  lane.totalBuildSpend += descriptor.buildCost;
  lane.buildSpendThisRound += descriptor.buildCost;
  padState.tier = 1;
  padState.maxHp = resolveFortressBuildingMaxHp(padState.buildingType, 1, game.teamHpMax);
  padState.hp = padState.maxHp;
  if (!Array.isArray(padState.costHistory)) padState.costHistory = [];
  padState.costHistory.push({ cost: descriptor.buildCost });
  return { ok: true };
}

function applyBarracksSiteBuildAction(game, lane, barracksId) {
  const descriptor = describeBarracksSite(game, lane, barracksId);
  if (!descriptor) return { ok: false, reason: "Unknown barracks" };
  if (!descriptor.canBuild)
    return { ok: false, reason: descriptor.lockedReason || "Building is not available" };
  if (lane.gold < descriptor.buildCost)
    return { ok: false, reason: "Not enough gold" };

  const siteState = descriptor.siteState;
  const nextLevel = 1;
  const interval = getBarracksSiteSendIntervalTicks(nextLevel);
  const maxHp = getBarracksSiteBaseMaxHp();

  lane.gold -= descriptor.buildCost;
  lane.totalBuildSpend += descriptor.buildCost;
  lane.buildSpendThisRound += descriptor.buildCost;
  siteState.isBuilt = true;
  siteState.level = nextLevel;
  siteState.maxHp = maxHp;
  siteState.hp = maxHp;
  siteState.nextSendTick = Math.floor(Number(game && game.tick) || 0) + interval;
  if (!Array.isArray(siteState.costHistory)) siteState.costHistory = [];
  siteState.costHistory.push({ cost: descriptor.buildCost });
  syncLegacyBarracksAggregate(lane);
  return { ok: true };
}

function applyBarracksSiteUpgradeAction(game, lane, barracksId) {
  const descriptor = describeBarracksSite(game, lane, barracksId);
  if (!descriptor) return { ok: false, reason: "Unknown barracks" };
  if (!descriptor.isBuilt) return { ok: false, reason: "Buy Building first" };
  if (!descriptor.canUpgrade)
    return { ok: false, reason: descriptor.lockedReason || "Barracks upgrade unavailable" };
  if (lane.gold < descriptor.upgradeCost)
    return { ok: false, reason: "Not enough gold" };

  const siteState = descriptor.siteState;
  const currentTick = Math.floor(Number(game && game.tick) || 0);
  const currentRemaining = Math.max(0, Math.floor(Number(siteState.nextSendTick) || 0) - currentTick);
  const nextLevel = normalizeBarracksSiteLevel(descriptor.level + 1);
  const nextInterval = getBarracksSiteSendIntervalTicks(nextLevel);

  lane.gold -= descriptor.upgradeCost;
  lane.totalBuildSpend += descriptor.upgradeCost;
  lane.buildSpendThisRound += descriptor.upgradeCost;
  siteState.level = nextLevel;
  siteState.maxHp = Math.max(getBarracksSiteBaseMaxHp(), Math.floor(Number(siteState.maxHp) || 0));
  siteState.hp = siteState.maxHp;
  siteState.nextSendTick = currentTick + Math.max(1, Math.min(currentRemaining || nextInterval, nextInterval));
  if (!Array.isArray(siteState.costHistory)) siteState.costHistory = [];
  siteState.costHistory.push({ cost: descriptor.upgradeCost });
  syncLegacyBarracksAggregate(lane);
  return { ok: true };
}

function applyFortressUpgrade(game, lane, padId) {
  const padState = getFortressPadState(lane, padId);
  if (!padState) return { ok: false, reason: "Unknown building pad" };
  if (padState.buildingType === "barracks")
    return { ok: false, reason: "Select Barracks Left, Center, or Right to upgrade that building." };

  const descriptor = describeFortressPad(game, lane, padState);
  if (!descriptor) return { ok: false, reason: "Unknown building pad" };
  if (!descriptor.canUpgrade) return { ok: false, reason: descriptor.lockedReason || "Upgrade unavailable" };
  if (lane.gold < descriptor.nextUpgradeCost) return { ok: false, reason: "Not enough gold" };

  const nextTier = padState.tier + 1;
  lane.gold -= descriptor.nextUpgradeCost;
  lane.totalBuildSpend += descriptor.nextUpgradeCost;
  lane.buildSpendThisRound += descriptor.nextUpgradeCost;
  padState.tier = nextTier;
  padState.maxHp = resolveFortressBuildingMaxHp(padState.buildingType, nextTier, game.teamHpMax);
  padState.hp = padState.maxHp;
  if (!Array.isArray(padState.costHistory)) padState.costHistory = [];
  padState.costHistory.push({ cost: descriptor.nextUpgradeCost });
  return { ok: true };
}

function deployBarracksHero(game, laneIndex, lane, heroKey, barracksId) {
  const heroDef = getHeroRosterDefinition(heroKey);
  if (!heroDef)
    return { ok: false, reason: "Unknown hero" };

  const normalizedBarracksId = normalizeBarracksSiteId(barracksId);
  if (!normalizedBarracksId)
    return { ok: false, reason: "Missing or invalid barracksId" };

  const siteDescriptor = describeBarracksSite(game, lane, normalizedBarracksId);
  if (!siteDescriptor)
    return { ok: false, reason: "Unknown barracks" };
  if (!siteDescriptor.isBuilt)
    return { ok: false, reason: siteDescriptor.lockedReason || "Buy Building first" };

  if (!isHeroUnlockedForLane(lane, heroDef))
    return { ok: false, reason: getHeroRosterLockedReason(heroDef) };

  const disabledReason = getHeroDisabledReason(game, lane, heroDef);
  if (disabledReason)
    return { ok: false, reason: disabledReason };

  const summonCost = Math.max(0, Math.floor(Number(heroDef.summonCost) || 0));
  if (lane.gold < summonCost)
    return { ok: false, reason: "Not enough gold" };

  const targetLaneIdx = resolveTargetLaneForBarracksSend(game, laneIndex, normalizedBarracksId);
  if (targetLaneIdx === null)
    return { ok: false, reason: "Lane command target unavailable" };

  const targetLane = game.lanes[targetLaneIdx];
  if (!targetLane)
    return { ok: false, reason: "Lane command target unavailable" };

  lane.gold -= summonCost;
  lane.totalSendSpend += summonCost;
  lane.sendSpendThisRound += summonCost;
  lane.totalSendCount += 1;
  lane.sendCountThisRound += 1;
  lane.heroCooldownReadyTicks[heroDef.heroKey] = Math.floor(Number(game && game.tick) || 0)
    + Math.max(0, Math.floor(Number(heroDef.cooldownTicks) || 0));
  const presentation = resolveFortPresentationConfig(
    heroDef.archetypeKey,
    heroDef.presentationKey,
    heroDef.displayName,
  );

  _spawnWaveUnit(game, targetLane, {
    unit_type: heroDef.archetypeKey,
    hp_mult: 1,
    dmg_mult: 1,
    speed_mult: 1,
    sourceLaneIndex: laneIndex,
    sourceTeam: lane.team || null,
    sourceBarracksId: normalizedBarracksId,
    archetypeKey: heroDef.archetypeKey,
    presentationKey: presentation.presentationKey,
    skinKey: presentation.skinKey || null,
    isHero: true,
    heroKey: heroDef.heroKey,
    heroVisualStyleKey: heroDef.heroVisualStyleKey || null,
    role: heroDef.role || null,
    combatRole: normalizeCombatRole(heroDef.spawnRole || heroDef.role) || UNIT_COMBAT_ROLES.SWORD,
    preferredBand: resolvePreferredBandForCombatRole(normalizeCombatRole(heroDef.spawnRole || heroDef.role) || UNIT_COMBAT_ROLES.SWORD),
    groupId: createSpawnPacketId(game, laneIndex, normalizedBarracksId, "hero_packet"),
    groupSpawnOrdinal: Math.max(1, Math.floor(Number(game && game.nextPacketId) || 1) - 1),
    groupSpawnTick: Math.floor(Number(game && game.tick) || 0),
  });

  return { ok: true };
}

function resolveRequestedLaneAnchorProgress(game, lane, data, fallbackProgress) {
  const rawProgress = Number(
    data && (
      data.routeProgress
      ?? data.anchorProgress
      ?? data.progress
      ?? data.normProgress
    )
  );
  if (Number.isFinite(rawProgress))
    return Math.max(0, Math.min(1, rawProgress));

  const worldX = Number(data && (data.worldX ?? data.x));
  const worldY = Number(data && (data.worldY ?? data.y));
  if (Number.isFinite(worldX) && Number.isFinite(worldY) && lane && Number.isInteger(lane.laneIndex)) {
    const objectiveLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
    const routeSegments = buildLaneCommandCoreRouteSegments(lane.laneIndex, objectiveLaneIndex);
    const projection = projectPointOntoRouteSegments(routeSegments, { x: worldX, y: worldY });
    if (projection)
      return Math.max(0, Math.min(1, Number(projection.routeProgress) || 0));
  }

  return Math.max(0, Math.min(1, Number(fallbackProgress) || 0));
}

function applyLaneCommandAction(game, lane, commandState, data = null) {
  const normalizedCommandState = normalizeLaneCommandState(commandState);
  if (!lane || !normalizedCommandState)
    return { ok: false, reason: "Invalid lane command" };

  const requestedTargetLaneIndex = Number.isInteger(data && data.targetLaneIndex)
    ? data.targetLaneIndex
    : null;
  if (requestedTargetLaneIndex !== null && isOpponentLane(game, lane.laneIndex, requestedTargetLaneIndex))
    lane.commandTargetLaneIndex = requestedTargetLaneIndex;

  let anchorProgress = getLaneCommandAnchorProgress(lane);
  if (normalizedCommandState === LANE_COMMAND_STATES.ATTACK) {
    anchorProgress = 1;
  } else {
    anchorProgress = 0;
  }

  lane.commandState = normalizedCommandState;
  lane.commandAnchorProgress = anchorProgress;
  lane.formationAnchorProgress = anchorProgress;
  lane.commandTargetLaneIndex = getLaneCommandObjectiveLaneIndex(game, lane);
  lane.combatEnabled = isLaneCombatEnabledCommandState(normalizedCommandState);
  lane.engagementRadius = getLaneCommandEngagementRadius(lane);
  syncLaneCommandAssignments(game);
  return { ok: true };
}

function warnLegacyActionOnce(game, laneIndex, type) {
  if (!game || !type) return;
  if (!(game.__legacyActionWarnings instanceof Set))
    game.__legacyActionWarnings = new Set();
  const warningKey = `${laneIndex}:${type}`;
  if (game.__legacyActionWarnings.has(warningKey)) return;
  game.__legacyActionWarnings.add(warningKey);
  log.warn("[ActionBoundary] rejected legacy fortress action", {
    actionType: type,
    laneIndex,
    tick: Number(game.tick) || 0,
  });
}

function rejectLegacyFortressAction(game, laneIndex, type) {
  const reason = LEGACY_ACTION_REJECTION_REASONS[type];
  if (!reason) return null;
  warnLegacyActionOnce(game, laneIndex, type);
  return { ok: false, reason };
}

function applyMLAction(game, laneIndex, action) {
  if (!game || game.phase !== "playing") return { ok: false, reason: "Game not active" };
  if (laneIndex < 0 || laneIndex >= game.lanes.length) return { ok: false, reason: "Bad laneIndex" };
  const lane = game.lanes[laneIndex];
  if (lane.eliminated) return { ok: false, reason: "You have been eliminated" };
  if (!action || typeof action.type !== "string") return { ok: false, reason: "Bad action" };

  // Phase B: action sequencing — stamp canonical replay fields
  game.actionSeq = (game.actionSeq || 0) + 1;
  action.tickApply = game.tick;
  action.laneId    = laneIndex;
  action.actionSeq = game.actionSeq;

  const { type, data } = action;

  if (type === "build_on_pad") {
    const padId = String((data && data.padId) || "").trim();
    if (!padId) return { ok: false, reason: "Missing padId" };
    return applyFortressBuildOnPad(game, lane, padId);
  }

  if (type === "upgrade_building") {
    const rawPadId = String((data && data.padId) || "").trim();
    const rawBuildingType = String((data && data.buildingType) || "").trim();
    const padId = rawPadId || (getFortressPadByBuildingType(lane, rawBuildingType) || {}).padId;
    if (!padId) return { ok: false, reason: "Missing padId" };
    return applyFortressUpgrade(game, lane, padId);
  }

  if (type === "build_barracks_site") {
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    if (!barracksId) return { ok: false, reason: "Missing or invalid barracksId" };
    return applyBarracksSiteBuildAction(game, lane, barracksId);
  }

  if (type === "upgrade_barracks_site") {
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    if (!barracksId) return { ok: false, reason: "Missing or invalid barracksId" };
    return applyBarracksSiteUpgradeAction(game, lane, barracksId);
  }

  if (type === "buy_barracks_unit") {
    const rosterKey = String((data && data.rosterKey) || "").trim();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const requestedCount = Math.max(1, Math.floor(Number((data && data.count) || 1) || 1));
    const count = Math.min(25, requestedCount);
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    console.log(
      `[BarracksTrace][ServerBuy] action='buy_barracks_unit' lane=${laneIndex} ` +
      `requestedBarracksId='${requestedBarracksId}' resolvedBarracksId='${barracksId}' rosterKey='${rosterKey}' count=${count}`);
    const rosterDef = getBarracksRosterDefinition(rosterKey);
    if (!rosterDef) return { ok: false, reason: "Unknown barracks unit" };
    if (!barracksId) return { ok: false, reason: "Missing or invalid barracksId" };

    const siteSnapshot = createBarracksSiteSnapshot(game, lane, barracksId);
    if (!siteSnapshot) return { ok: false, reason: "Unknown barracks" };
    if (!siteSnapshot.isBuilt) return { ok: false, reason: siteSnapshot.lockedReason || "Buy Building first" };

    const rosterEntry = Array.isArray(siteSnapshot.roster)
      ? siteSnapshot.roster.find((entry) => entry.rosterKey === rosterKey)
      : null;
    if (!rosterEntry) return { ok: false, reason: "Unknown barracks unit" };
    if (!rosterEntry.unlocked) return { ok: false, reason: rosterEntry.lockedReason || "Unit is locked" };
    const totalCost = Math.max(0, rosterEntry.buyCost * count);
    if (lane.gold < totalCost) return { ok: false, reason: "Not enough gold" };

    lane.gold -= totalCost;
    lane.totalBuildSpend += totalCost;
    lane.buildSpendThisRound += totalCost;
    const siteCounts = getBarracksSiteCounts(lane, barracksId);
    if (!siteCounts) return { ok: false, reason: "Missing or invalid barracksId" };
    siteCounts[rosterKey] = Math.max(0, Math.floor(Number(siteCounts[rosterKey]) || 0) + count);
    console.log(
      `[BarracksTrace][ServerBuy] lane=${laneIndex} barracksId='${barracksId}' rosterKey='${rosterKey}' ` +
      `ownedCount=${siteCounts[rosterKey]} totalCost=${totalCost}`);
    logBarracksRosterState(lane, `after_buy:${barracksId}:${rosterKey}`);
    return { ok: true };
  }

  if (type === "sell_barracks_unit") {
    const rosterKey = String((data && data.rosterKey) || "").trim();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    const barracksId = normalizeBarracksSiteId(requestedBarracksId);
    console.log(
      `[BarracksTrace][ServerBuy] action='sell_barracks_unit' lane=${laneIndex} ` +
      `requestedBarracksId='${requestedBarracksId}' resolvedBarracksId='${barracksId}' rosterKey='${rosterKey}'`);
    const rosterDef = getBarracksRosterDefinition(rosterKey);
    if (!rosterDef) return { ok: false, reason: "Unknown barracks unit" };
    if (!barracksId) return { ok: false, reason: "Missing or invalid barracksId" };
    const siteCounts = getBarracksSiteCounts(lane, barracksId);
    if (!siteCounts) return { ok: false, reason: "Missing or invalid barracksId" };
    const currentOwned = Math.max(0, Math.floor(Number(siteCounts[rosterKey]) || 0));
    if (currentOwned <= 0) return { ok: false, reason: "No owned units to sell" };

    siteCounts[rosterKey] = currentOwned - 1;
    lane.gold += getBarracksRosterSellRefund(rosterDef);
    console.log(
      `[BarracksTrace][ServerBuy] lane=${laneIndex} barracksId='${barracksId}' rosterKey='${rosterKey}' ` +
      `ownedCount=${siteCounts[rosterKey]}`);
    logBarracksRosterState(lane, `after_sell:${barracksId}:${rosterKey}`);
    return { ok: true };
  }

  if (type === "deploy_barracks_hero") {
    const heroKey = String((data && data.heroKey) || "").trim().toLowerCase();
    const requestedBarracksId = String((data && data.barracksId) || "").trim();
    return deployBarracksHero(game, laneIndex, lane, heroKey, requestedBarracksId);
  }

  if (type === "set_lane_attack")
    return applyLaneCommandAction(game, lane, LANE_COMMAND_STATES.ATTACK, data);

  if (type === "set_lane_defend" || type === "set_lane_hold" || type === "set_lane_defend_point")
    return applyLaneCommandAction(game, lane, LANE_COMMAND_STATES.DEFEND, data);

  if (type === "set_lane_retreat" || type === "set_lane_callback")
    return applyLaneCommandAction(game, lane, LANE_COMMAND_STATES.RETREAT, data);

  if (type === "set_lane_command") {
    const requestedCommandState = data && data.commandState;
    return applyLaneCommandAction(game, lane, requestedCommandState, data);
  }

  // Keep retired classic actions fenced here so old clients and AI paths fail
  // loudly without re-enabling removed game modes inside the active runtime.
  const legacyRejection = rejectLegacyFortressAction(game, laneIndex, type);
  if (legacyRejection) return legacyRejection;

  return { ok: false, reason: "Unknown action type" };
}

// ── Mobile defender helpers ───────────────────────────────────────────────────

function dist2D(a, b) {
  const dx = a.posX - b.posX, dy = a.posY - b.posY;
  return Math.sqrt(dx * dx + dy * dy);
}

function moveToward2D(unit, tx, ty, speed, minX, maxX, minY, maxY) {
  const dx = tx - unit.posX, dy = ty - unit.posY;
  const d  = Math.sqrt(dx * dx + dy * dy);
  if (d < 0.01) {
    unit.posX = Math.max(minX, Math.min(maxX, tx));
    unit.posY = Math.max(minY, Math.min(maxY, ty));
    syncMovedUnitPathState(unit);
    return;
  }
  const step = Math.min(speed, d);
  unit.posX  = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY  = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  syncMovedUnitPathState(unit);
}

function moveTowardContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY) {
  if (!target) return;
  const dx = target.posX - unit.posX;
  const dy = target.posY - unit.posY;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d <= stopDistance || d < 0.01) return;
  const step = Math.min(speed, Math.max(0, d - stopDistance));
  if (step <= 0) return;
  unit.posX = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  syncMovedUnitPathState(unit);
}

function updateSimpleContactApproachMemory(unit, target) {
  if (!unit || !target || !target.id)
    return null;

  const currentTargetId = String(target.id);
  const cachedTargetId = String(unit.simpleContactApproachTargetId || "");
  let memoryX = Number(unit.simpleContactApproachX);
  let memoryY = Number(unit.simpleContactApproachY);
  let memoryMagnitude = Math.sqrt((memoryX * memoryX) + (memoryY * memoryY));
  if (cachedTargetId !== currentTargetId || !Number.isFinite(memoryMagnitude) || memoryMagnitude < 0.001) {
    let dx = Number(unit.posX) - Number(target.posX);
    let dy = Number(unit.posY) - Number(target.posY);
    let distance = Math.sqrt((dx * dx) + (dy * dy));
    if (!Number.isFinite(distance) || distance < 0.001) {
      const fallbackForward = getUnitForwardDirection(unit);
      dx = -(Number(fallbackForward && fallbackForward.x) || 0);
      dy = -(Number(fallbackForward && fallbackForward.y) || 1);
      distance = Math.sqrt((dx * dx) + (dy * dy));
    }

    if (!Number.isFinite(distance) || distance < 0.001)
      return null;

    memoryX = dx / distance;
    memoryY = dy / distance;
    memoryMagnitude = Math.sqrt((memoryX * memoryX) + (memoryY * memoryY));
    unit.simpleContactApproachTargetId = currentTargetId;
    unit.simpleContactApproachX = memoryX;
    unit.simpleContactApproachY = memoryY;
  }

  return {
    x: memoryX / memoryMagnitude,
    y: memoryY / memoryMagnitude,
  };
}

function moveTowardSimpleContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY) {
  if (!target)
    return;

  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (!Number.isFinite(distance) || distance <= stopDistance || distance < 0.01)
    return;

  const approachMemory = updateSimpleContactApproachMemory(unit, target);
  const captureDistance = stopDistance + Math.max(0.55, getUnitContactRadius(unit && unit.type) * 0.65);
  if (approachMemory && distance <= captureDistance) {
    moveTowardPoint2D(
      unit,
      Number(target.posX) + (approachMemory.x * stopDistance),
      Number(target.posY) + (approachMemory.y * stopDistance),
      speed,
      minX,
      maxX,
      minY,
      maxY
    );
    return;
  }

  moveTowardContact2D(unit, target, speed, stopDistance, minX, maxX, minY, maxY);
}

function moveTowardPoint2D(unit, tx, ty, speed, minX, maxX, minY, maxY) {
  const dx = tx - unit.posX;
  const dy = ty - unit.posY;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d < 0.01) {
    unit.posX = Math.max(minX, Math.min(maxX, tx));
    unit.posY = Math.max(minY, Math.min(maxY, ty));
    syncMovedUnitPathState(unit);
    return;
  }
  const step = Math.min(speed, d);
  unit.posX = Math.max(minX, Math.min(maxX, unit.posX + (dx / d) * step));
  unit.posY = Math.max(minY, Math.min(maxY, unit.posY + (dy / d) * step));
  syncMovedUnitPathState(unit);
}

function getUnitContactRadius(typeKey) {
  if (typeKey && FORTRESS_BUILDING_DEFS[typeKey]) {
    if (typeKey === "town_core") return 1.15;
    return 0.95;
  }

  const def = resolveUnitDef(typeKey);
  const hp = Number(def && def.hp) || 80;
  const atkRange = getUnitAttackRange(typeKey);

  let radius;
  if (hp >= 140) radius = 0.85;
  else if (hp >= 100) radius = 0.75;
  else if (hp >= 70) radius = 0.65;
  else if (hp >= 50) radius = 0.55;
  else radius = 0.45;

  if (atkRange > 2.5)
    radius = Math.max(0.45, radius - 0.10);

  if (typeof typeKey === "string" && (typeKey.startsWith("tt_") || isFortArchetypeKey(typeKey)))
    radius += 0.20;

  return radius;
}

function getUnitStopDistance(attackerType, targetType) {
  const attackRange = getUnitAttackRange(attackerType);
  const base = attackRange + (attackRange > 2.0 ? 0.15 : 0.05);
  if (attackRange > 2.0) return base;
  const attackerRadius = getUnitContactRadius(attackerType);
  const targetRadius = getUnitContactRadius(targetType);
  const bodyPadding = Math.max(attackerRadius, targetRadius, (attackerRadius + targetRadius) * 0.5);
  const attackerRole = resolveUnitCombatRole({
    type: attackerType,
    unitTypeKey: attackerType,
    archetypeKey: attackerType,
    rosterKey: attackerType,
  });

  let meleeContactBias = 0;
  switch (attackerRole) {
    case UNIT_COMBAT_ROLES.SHIELD:
      meleeContactBias = -0.35;
      break;
    case UNIT_COMBAT_ROLES.SWORD:
      meleeContactBias = -0.65;
      break;
    case UNIT_COMBAT_ROLES.SPEAR:
      meleeContactBias = -0.20;
      break;
    default:
      meleeContactBias = 0;
      break;
  }

  return base + Math.max(0.35, bodyPadding + meleeContactBias);
}

function getLaneByIndex(game, laneIndex) {
  if (!game || !Array.isArray(game.lanes) || !Number.isInteger(laneIndex))
    return null;
  return game.lanes.find((candidate) => candidate && candidate.laneIndex === laneIndex) || null;
}

function resolveRouteUnitLane(game, unit, fallbackLane = null) {
  if (!game || !unit)
    return fallbackLane;

  if (Number.isInteger(unit.targetLaneIndex)) {
    const explicitTargetLane = getLaneByIndex(game, unit.targetLaneIndex);
    if (explicitTargetLane)
      return explicitTargetLane;
  }

  if (Number.isInteger(unit.laneId)) {
    const explicitLane = getLaneByIndex(game, unit.laneId);
    if (explicitLane)
      return explicitLane;
  }

  return fallbackLane;
}

function findRouteUnitById(game, unitId, preferredLaneIndex = -1) {
  if (!game || !Array.isArray(game.lanes) || !unitId)
    return null;

  if (Number.isInteger(preferredLaneIndex)) {
    const preferredLane = getLaneByIndex(game, preferredLaneIndex);
    if (preferredLane) {
      const match = preferredLane.units.find((candidate) => candidate && candidate.id === unitId && candidate.hp > 0);
      if (match)
        return { lane: preferredLane, unit: match };
    }
  }

  for (const lane of game.lanes) {
    if (!lane)
      continue;
    const match = lane.units.find((candidate) => candidate && candidate.id === unitId && candidate.hp > 0);
    if (match)
      return { lane, unit: match };
  }

  return null;
}

function resolveRouteUnitFactionKey(game, unit) {
  if (!unit)
    return null;
  return resolveUnitAllegianceKey(game, resolveRouteUnitLane(game, unit, null), unit);
}

function getLaneCommandAnchorStateForUnit(game, unit) {
  const ownerLane = getLaneCommandOwnerLane(game, unit);
  return ownerLane ? sampleLaneCommandAnchor(game, ownerLane) : null;
}

function isTargetInsideHomeFortressEmergencyZone(game, attacker, target) {
  if (!game || !attacker || !target || !isLaneControlledUnit(attacker))
    return false;

  const ownerLane = getLaneCommandOwnerLane(game, attacker);
  const townCoreTarget = getLaneTownCoreCombatTarget(ownerLane);
  const distanceToCore = getWaveUnitTargetDistance(target, townCoreTarget);
  return Number.isFinite(distanceToCore)
    && distanceToCore <= FORTRESS_INTERIOR_ASSAULT_RADIUS;
}

function isTargetInsideLaneDefenseZone(game, attacker, target) {
  if (!attacker || !target || !isLaneControlledUnit(attacker))
    return true;

  const commandState = getLaneCommandStateForUnit(game, attacker);
  if (commandState !== LANE_COMMAND_STATES.DEFEND)
    return true;
  if (isTargetInsideHomeFortressEmergencyZone(game, attacker, target))
    return true;

  const anchorState = getLaneCommandAnchorStateForUnit(game, attacker);
  if (!anchorState || !Number.isFinite(anchorState.engagementRadius) || anchorState.engagementRadius <= 0)
    return false;

  const tx = Number(target.posX);
  const ty = Number(target.posY);
  if (!Number.isFinite(tx) || !Number.isFinite(ty))
    return false;

  const dx = tx - anchorState.anchorX;
  const dy = ty - anchorState.anchorY;
  return Math.sqrt((dx * dx) + (dy * dy)) <= anchorState.engagementRadius;
}

function canLaneControlledUnitSeekCombat(game, attacker, target = null) {
  if (!isLaneControlledUnit(attacker))
    return true;
  if (!isLaneCommandCombatEnabledForUnit(game, attacker))
    return false;
  return !target || isTargetInsideLaneDefenseZone(game, attacker, target);
}

function resolveLaneObjectiveLane(game, fallbackLane, unit) {
  const objectiveLaneIndex = resolveUnitObjectiveLaneIndex(game, fallbackLane, unit);
  return getLaneByIndex(game, objectiveLaneIndex) || fallbackLane;
}

function areRouteUnitsHostile(game, attacker, target) {
  if (!attacker || !target || attacker === target)
    return false;
  if (attacker.hp <= 0 || target.hp <= 0)
    return false;
  if (attacker.isDefender || target.isDefender)
    return false;

  const attackerFaction = resolveRouteUnitFactionKey(game, attacker);
  const targetFaction = resolveRouteUnitFactionKey(game, target);
  if (attackerFaction && targetFaction)
    return areAllegiancesHostile(attackerFaction, targetFaction);

  return attacker.id !== target.id;
}

function canEngageRouteUnitTarget(game, attacker, target) {
  return canLaneControlledUnitSeekCombat(game, attacker, target)
    && areRouteUnitsHostile(game, attacker, target);
}

function isRouteUnitHostileToLane(game, lane, unit) {
  if (!lane || !unit || unit.hp <= 0 || unit.isDefender)
    return false;

  const laneTeam = resolveLaneAllegianceKey(lane);
  const unitFaction = resolveRouteUnitFactionKey(game, unit);
  if (laneTeam && unitFaction)
    return areAllegiancesHostile(laneTeam, unitFaction);

  if (Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0)
    return unit.sourceLaneIndex !== lane.laneIndex;

  return true;
}

function canRouteUnitEngageTarget(game, lane, attacker, target) {
  if (!attacker || !target)
    return false;
  if (!canLaneControlledUnitSeekCombat(game, attacker, target))
    return false;
  if (target.isDefender)
    return false;
  return canEngageRouteUnitTarget(game, attacker, target);
}

function getLaneControlledSharedCombatAnchor(unit) {
  if (shouldLaneControlledUnitRouteMarch(unit)) {
    const liveRouteX = Number.isFinite(Number(unit && unit.routeWorldX))
      ? Number(unit.routeWorldX)
      : Number(unit && unit.posX);
    const liveRouteY = Number.isFinite(Number(unit && unit.routeWorldY))
      ? Number(unit.routeWorldY)
      : Number(unit && unit.posY);
    if (Number.isFinite(liveRouteX) && Number.isFinite(liveRouteY))
      return { x: liveRouteX, y: liveRouteY };
  }

  const anchorX = Number.isFinite(Number(unit && unit.groupCenterX))
    ? Number(unit.groupCenterX)
    : (Number.isFinite(Number(unit && unit.anchorTargetX))
      ? Number(unit.anchorTargetX)
      : Number(unit && unit.posX));
  const anchorY = Number.isFinite(Number(unit && unit.groupCenterY))
    ? Number(unit.groupCenterY)
    : (Number.isFinite(Number(unit && unit.anchorTargetY))
      ? Number(unit.anchorTargetY)
      : Number(unit && unit.posY));
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY))
    return null;
  return { x: anchorX, y: anchorY };
}

function shouldLaneControlledUnitFreeRoamInCombat(unit) {
  if (!DISABLE_RIGID_PACKET_FORMATIONS || !isLaneControlledUnit(unit))
    return false;

  return !!(
    (unit.combatTarget && unit.combatTarget.unitId)
    || unit.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE
    || unit.combatState === WAVE_UNIT_STATES.COMBAT
    || unit.routeState === WAVE_UNIT_STATES.COMBAT
  );
}

function getLaneControlledSharedCombatJoinRadius(unit) {
  const baseRadius = getLaneControlledCombatLeashRadius(unit) + ROUTE_FORMATION_ROW_SPACING;
  if (!DISABLE_RIGID_PACKET_FORMATIONS || !isLaneControlledUnit(unit))
    return baseRadius;

  const commandState = normalizeLaneCommandState(unit.commandState);
  if (commandState === LANE_COMMAND_STATES.DEFEND)
    return Math.max(baseRadius, LANE_COMMAND_DEFENSE_RADIUS + PACKET_SUPPORT_TARGET_RADIUS);
  if (commandState === LANE_COMMAND_STATES.ATTACK)
    return Math.max(baseRadius, LANE_COMMAND_COMBAT_LEASH + PACKET_SUPPORT_TARGET_RADIUS);
  return Math.max(baseRadius, LANE_COMMAND_COMBAT_LEASH + ROUTE_FORMATION_ROW_SPACING);
}

function isLaneControlledUnitNearSharedCombat(unit, target) {
  if (!unit || !target)
    return false;

  const unitX = Number(unit.posX);
  const unitY = Number(unit.posY);
  const targetX = Number(target.posX);
  const targetY = Number(target.posY);
  if (!Number.isFinite(unitX) || !Number.isFinite(unitY) || !Number.isFinite(targetX) || !Number.isFinite(targetY))
    return false;

  const anchor = getLaneControlledSharedCombatAnchor(unit);
  const maxDistance = getLaneControlledSharedCombatJoinRadius(unit);
  const anchorDistance = anchor
    ? Math.sqrt(Math.pow(targetX - anchor.x, 2) + Math.pow(targetY - anchor.y, 2))
    : Infinity;
  const liveDistance = Math.sqrt(Math.pow(targetX - unitX, 2) + Math.pow(targetY - unitY, 2));

  return Math.min(anchorDistance, liveDistance) <= maxDistance;
}

function shouldLaneControlledUnitRouteMarch(unit) {
  if (!isLaneControlledUnit(unit))
    return false;

  const commandState = normalizeLaneCommandState(unit.commandState);
  if (commandState === LANE_COMMAND_STATES.ATTACK)
    return true;

  return String(unit.currentWaypointTargetKind || "").trim().toLowerCase()
    === String(LANE_STANCE_ANCHOR_KINDS.ENEMY_CORE).toLowerCase();
}

function clearUnitCombatTarget(unit, gameTick = 0, options = {}) {
  if (!unit)
    return;

  const hadTarget = !!(unit.combatTarget && unit.combatTarget.unitId);
  unit.combatTarget = null;
  unit.combatTargetId = null;
  unit.currentTargetId = null;
  unit.combatTargetLockedUntilTick = 0;
  unit.combatTargetWorldX = null;
  unit.combatTargetWorldY = null;
  unit.simpleContactApproachTargetId = null;
  unit.simpleContactApproachX = 0;
  unit.simpleContactApproachY = 0;

  if (!hadTarget || !isLaneControlledUnit(unit))
    return;

  if (DISABLE_RIGID_PACKET_FORMATIONS) {
    unit.regroupUntilTick = 0;
    return;
  }

  const nextRegroupTick = Number.isFinite(Number(options.regroupUntilTick))
    ? Number(options.regroupUntilTick)
    : gameTick + LANE_COMBAT_REGROUP_TICKS;
  unit.regroupUntilTick = Math.max(Number(unit.regroupUntilTick) || 0, nextRegroupTick);
}

function assignUnitCombatTarget(unit, targetDescriptor, gameTick = 0) {
  if (!unit || !targetDescriptor || !targetDescriptor.unitId)
    return false;

  const previousTargetId = unit.combatTarget && unit.combatTarget.unitId
    ? unit.combatTarget.unitId
    : null;
  unit.combatTarget = {
    unitId: targetDescriptor.unitId,
    kind: targetDescriptor.kind || "unit",
    laneIndex: Number.isInteger(targetDescriptor.laneIndex) ? targetDescriptor.laneIndex : null,
    padId: targetDescriptor.padId || null,
  };
  unit.combatTargetId = targetDescriptor.unitId;
  unit.currentTargetId = targetDescriptor.unitId;
  if (isLaneControlledUnit(unit) && unit.combatTarget.kind === "unit")
    unit.combatTargetLockedUntilTick = gameTick + LANE_COMBAT_TARGET_LOCK_TICKS;
  else
    unit.combatTargetLockedUntilTick = 0;
  if (!previousTargetId || previousTargetId !== targetDescriptor.unitId)
    unit.regroupUntilTick = 0;
  return true;
}

function isLaneControlledUnitInRegroupWindow(unit, gameTick) {
  if (DISABLE_RIGID_PACKET_FORMATIONS && isLaneControlledUnit(unit))
    return false;
  return !!(isLaneControlledUnit(unit) && Number(unit.regroupUntilTick) > gameTick);
}

function isUnitCombatTargetStillValid(game, lane, attacker, target) {
  if (!attacker || !target)
    return false;

  if (target.kind === "unit") {
    if (!canRouteUnitEngageTarget(game, lane, attacker, target.entity))
      return false;
    if (isLaneControlledUnit(attacker))
      return isLaneControlledUnitNearSharedCombat(attacker, target);
    return true;
  }

  return canLaneControlledUnitSeekCombat(game, attacker, target);
}

function getResolvedCombatTargetDistance(unit, target) {
  const distance = getWaveUnitTargetDistance(unit, target);
  return Number.isFinite(distance) ? distance : Infinity;
}

function shouldSwitchCombatTarget(unit, currentTarget, nextTarget) {
  if (!unit || !currentTarget || !nextTarget)
    return false;
  if (currentTarget.kind !== "unit" || nextTarget.kind !== "unit")
    return true;
  if (currentTarget.entity && nextTarget.entity && currentTarget.entity.id === nextTarget.entity.id)
    return false;

  const currentDistance = getResolvedCombatTargetDistance(unit, currentTarget);
  const nextDistance = getResolvedCombatTargetDistance(unit, nextTarget);
  if (!Number.isFinite(currentDistance))
    return true;
  if (!Number.isFinite(nextDistance))
    return false;
  return nextDistance + LANE_COMBAT_SWITCH_DISTANCE_MARGIN < currentDistance;
}

function isSameLaneControlledPacket(left, right) {
  return !!(
    isLaneControlledUnit(left)
    && isLaneControlledUnit(right)
    && left.groupId
    && right.groupId
    && left.groupId === right.groupId
  );
}

function findLaneFormationSharedCombatTarget(game, lane, unit) {
  if (DISABLE_RIGID_PACKET_FORMATIONS)
    return null;
  if (!game || !lane || !isLaneControlledUnit(unit))
    return null;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return null;

  let best = null;
  let bestDist = Infinity;

  for (const allyLane of game.lanes || []) {
    if (!allyLane || !Array.isArray(allyLane.units))
      continue;

    for (const ally of allyLane.units) {
      if (!ally || ally === unit || ally.hp <= 0)
        continue;
      if (!isLaneControlledUnit(ally) || ally.sourceLaneIndex !== unit.sourceLaneIndex)
        continue;
      if (!ally.combatTarget || ally.combatTarget.kind !== "unit" || !ally.combatTarget.unitId)
        continue;

      const sharingPacket = isSameLaneControlledPacket(ally, unit);
      if (!canLaneControlledUnitsShareCombatTarget(ally, unit))
        continue;

      const target = resolveWaveCombatTarget(game, allyLane, ally.combatTarget);
      if (!target || target.kind !== "unit")
        continue;
      if (!canRouteUnitEngageTarget(game, lane, unit, target.entity))
        continue;
      if (!isLaneControlledUnitNearSharedCombat(unit, target))
        continue;

      const dist = getWaveUnitTargetDistance(unit, target);
      if (!Number.isFinite(dist))
        continue;

      if (!best || dist < bestDist) {
        best = {
          kind: "unit",
          entity: target.entity,
          laneIndex: target.laneIndex,
          reason: sharingPacket ? "packet_shared" : "packet_support",
        };
        bestDist = dist;
      }
    }
  }

  return best;
}

function getLaneControlledCombatLeashRadius(unit) {
  const cohesionLeash = Number(unit && unit.leashFromGroupCenter);
  const commandLeash = Number(unit && unit.combatLeashRadius);
  const leashRadius = Math.max(
    Number.isFinite(cohesionLeash) && cohesionLeash > 0 ? cohesionLeash : 0,
    Number.isFinite(commandLeash) && commandLeash > 0 ? commandLeash : 0
  );
  if (leashRadius > 0)
    return leashRadius;
  return LANE_COMMAND_COMBAT_LEASH;
}

function clampPointToRadius(pointX, pointY, centerX, centerY, maxRadius) {
  const safePointX = Number(pointX);
  const safePointY = Number(pointY);
  if (!Number.isFinite(safePointX) || !Number.isFinite(safePointY))
    return null;
  if (!Number.isFinite(centerX) || !Number.isFinite(centerY) || !Number.isFinite(maxRadius))
    return { x: safePointX, y: safePointY };
  if (maxRadius <= 0)
    return { x: centerX, y: centerY };

  const dx = safePointX - centerX;
  const dy = safePointY - centerY;
  const distance = Math.sqrt((dx * dx) + (dy * dy));
  if (distance <= maxRadius || distance < 0.001) {
    return { x: safePointX, y: safePointY };
  }

  const scale = maxRadius / distance;
  return {
    x: centerX + (dx * scale),
    y: centerY + (dy * scale),
  };
}

function isPointWithinRadius(pointX, pointY, centerX, centerY, maxRadius) {
  const safePointX = Number(pointX);
  const safePointY = Number(pointY);
  const safeCenterX = Number(centerX);
  const safeCenterY = Number(centerY);
  const safeRadius = Number(maxRadius);
  if (!Number.isFinite(safePointX) || !Number.isFinite(safePointY)
      || !Number.isFinite(safeCenterX) || !Number.isFinite(safeCenterY)
      || !Number.isFinite(safeRadius) || safeRadius < 0) {
    return false;
  }

  const dx = safePointX - safeCenterX;
  const dy = safePointY - safeCenterY;
  return ((dx * dx) + (dy * dy)) <= (safeRadius * safeRadius);
}

function shouldAnchorClampLaneControlledCombat(unit, target) {
  if (!isLaneControlledUnit(unit) || !target)
    return false;
  if (shouldLaneControlledUnitFreeRoamInCombat(unit))
    return false;

  const anchor = getLaneControlledSharedCombatAnchor(unit);
  if (!anchor)
    return false;

  const targetX = Number(target.posX);
  const targetY = Number(target.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return false;

  const leashRadius = getLaneControlledCombatLeashRadius(unit);
  const sharedCombatRadius = leashRadius + ROUTE_FORMATION_ROW_SPACING;
  if (isPointWithinRadius(targetX, targetY, anchor.x, anchor.y, sharedCombatRadius))
    return true;

  return !isPointWithinRadius(targetX, targetY, Number(unit.posX), Number(unit.posY), sharedCombatRadius);
}

function canLaneControlledUnitsShareCombatTarget(left, right) {
  if (!isLaneControlledUnit(left) || !isLaneControlledUnit(right))
    return false;
  if (isSameLaneControlledPacket(left, right))
    return true;

  const leftAnchor = getLaneControlledSharedCombatAnchor(left);
  const rightAnchor = getLaneControlledSharedCombatAnchor(right);
  if (!leftAnchor || !rightAnchor)
    return false;

  const packetDistance = Math.sqrt(
    Math.pow(leftAnchor.x - rightAnchor.x, 2) + Math.pow(leftAnchor.y - rightAnchor.y, 2)
  );
  const sameSourceLane = Number(left.sourceLaneIndex) === Number(right.sourceLaneIndex);
  const leftCommandState = normalizeLaneCommandState(left.commandState);
  const rightCommandState = normalizeLaneCommandState(right.commandState);
  const sharedDefenseCluster = sameSourceLane
    && leftCommandState === LANE_COMMAND_STATES.DEFEND
    && rightCommandState === LANE_COMMAND_STATES.DEFEND;
  const supportRadius = sharedDefenseCluster
    ? Math.max(PACKET_SUPPORT_TARGET_RADIUS, LANE_COMMAND_DEFENSE_RADIUS + PACKET_BARRACKS_LATERAL_SPACING)
    : PACKET_SUPPORT_TARGET_RADIUS;
  return packetDistance <= supportRadius;
}

function propagateLaneFormationCombatTarget(game, sourceUnit, targetEntity, targetLaneIndex) {
  if (!game || !targetEntity || targetEntity.hp <= 0 || !isLaneControlledUnit(sourceUnit))
    return;
  if (DISABLE_RIGID_PACKET_FORMATIONS)
    return;

  const sharedTarget = {
    kind: "unit",
    unitId: targetEntity.id,
    laneIndex: targetLaneIndex,
    posX: targetEntity.posX,
    posY: targetEntity.posY,
    entity: targetEntity,
  };

  for (const allyLane of game.lanes || []) {
    if (!allyLane || !Array.isArray(allyLane.units))
      continue;

    for (const ally of allyLane.units) {
      if (!ally || ally.hp <= 0 || ally === sourceUnit)
        continue;
      if (!canLaneControlledUnitsShareCombatTarget(ally, sourceUnit))
        continue;
      if (!canRouteUnitEngageTarget(game, allyLane, ally, targetEntity))
        continue;
      if (!isLaneControlledUnitNearSharedCombat(ally, sharedTarget))
        continue;

      const currentTarget = ally.combatTarget
        ? resolveWaveCombatTarget(game, allyLane, ally.combatTarget)
        : null;
      const targetLockActive = Number(ally.combatTargetLockedUntilTick) > Number(game && game.tick);
      if (targetLockActive && currentTarget && currentTarget.kind === "unit" && currentTarget.unitId !== targetEntity.id)
        continue;
      if (currentTarget && currentTarget.kind === "unit" && currentTarget.unitId !== targetEntity.id)
        continue;

      assignUnitCombatTarget(ally, {
        unitId: targetEntity.id,
        kind: "unit",
        laneIndex: targetLaneIndex,
      }, Number(game && game.tick) || 0);
    }
  }
}

function resolveWaveCombatTarget(game, lane, combatTarget) {
  if (!lane || !combatTarget || !combatTarget.unitId) return null;

  if (combatTarget.kind === "fortress_pad") {
    const targetLane = Number.isInteger(combatTarget.laneIndex)
      ? (getLaneByIndex(game, combatTarget.laneIndex) || lane)
      : lane;
    const barracksSiteMatch = String(combatTarget.padId || combatTarget.unitId || "").match(/^barracks_site:(.+)$/);
    if (barracksSiteMatch)
      return getBarracksSiteCombatTarget(targetLane, barracksSiteMatch[1]);

    const padState = combatTarget.padId
      ? getFortressPadState(targetLane, combatTarget.padId)
      : getLaneTownCorePad(targetLane);
    if (padState && padState.buildingType === "town_core")
      return getLaneTownCoreCombatTarget(targetLane);
    return getFortressPadCombatTarget(targetLane, padState);
  }

  const unit = lane.units.find((candidate) => candidate && candidate.id === combatTarget.unitId && candidate.hp > 0);
  if (!unit) {
    const resolved = findRouteUnitById(game, combatTarget.unitId, Number.isInteger(combatTarget.laneIndex) ? combatTarget.laneIndex : lane.laneIndex);
    if (!resolved || !resolved.unit)
      return null;
    return {
      id: resolved.unit.id,
      unitId: resolved.unit.id,
      kind: "unit",
      type: resolved.unit.type,
      posX: resolved.unit.posX,
      posY: resolved.unit.posY,
      laneIndex: resolved.lane ? resolved.lane.laneIndex : null,
      entity: resolved.unit,
    };
  }

  return {
    id: unit.id,
    unitId: unit.id,
    kind: "unit",
    type: unit.type,
    posX: unit.posX,
    posY: unit.posY,
    laneIndex: lane.laneIndex,
    entity: unit,
  };
}

function markTownCoreBreach(game, lane, unit, townCoreTarget) {
  if (!game || !lane || !unit || unit.hasBreachedTownCore) return;
  unit.hasBreachedTownCore = true;
  lane.totalLeaksTaken += 1;
  lane.leakCountThisRound += 1;
  log.info("[TownCoreTrace] core target acquired", {
    tick: game.tick,
    laneIndex: lane.laneIndex,
    attackerId: unit.id,
    attackerType: unit.type,
    attackerHp: Math.max(0, Math.floor(Number(unit.hp) || 0)),
    pathIdx: Number.isFinite(unit.pathIdx) ? Number(unit.pathIdx.toFixed(2)) : null,
    corePadId: townCoreTarget ? townCoreTarget.padId || townCoreTarget.id : null,
    coreHp: getLaneTownCoreHp(lane),
    coreMaxHp: getLaneTownCoreMaxHp(lane),
  });
  combatLog.logEvent(game, "leak", {
    unitId: unit.id,
    unitType: unit.type,
    lane: lane.laneIndex,
    targetPadId: townCoreTarget ? townCoreTarget.padId || townCoreTarget.id : null,
    breachOnly: true,
  });
}

function attackFortressPad(game, lane, attacker, target) {
  if (!game || !lane || !attacker || !target || target.kind !== "fortress_pad") {
    return { damageApplied: 0, destroyed: false, remainingHp: 0 };
  }

  const targetLane = Number.isInteger(target.laneIndex)
    ? (getLaneByIndex(game, target.laneIndex) || lane)
    : lane;
  const def = resolveUnitDef(attacker.type);
  const rawDamage = Math.max(1, Math.floor(Number(attacker.baseDmg) || Number(def && def.dmg) || 1));
  const cooldownTicks = Math.max(1, Math.floor(Number(attacker.atkCdTicks) || Number(def && def.atkCdTicks) || 20));
  const targetPad = getFortressPadState(targetLane, target.padId || target.id);
  const prevHp = targetPad ? Math.max(0, Math.floor(Number(targetPad.hp) || 0)) : 0;
  const result = applyFortressPadDamage(game, targetLane, target.padId || target.id, rawDamage);

  attacker.attackPulse = (attacker.attackPulse || 0) + 1;
  attacker.atkCd = cooldownTicks;
  if (targetPad && targetPad.buildingType === "town_core") {
    log.info("[TownCoreTrace] core attacked", {
      tick: game.tick,
      laneIndex: targetLane.laneIndex,
      attackerId: attacker.id,
      attackerType: attacker.type,
      corePadId: targetPad.padId,
      attemptedDamage: rawDamage,
      appliedDamage: result.damageApplied,
      previousHp: prevHp,
      remainingHp: result.remainingHp,
      destroyed: !!result.destroyed,
    });
  }
  return result;
}

function clampUnitToAnchor(unit, anchorX, anchorY, maxRadius, minX, maxX, minY, maxY) {
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY) || maxRadius <= 0) return;
  const dx = unit.posX - anchorX;
  const dy = unit.posY - anchorY;
  const d = Math.sqrt(dx * dx + dy * dy);
  if (d <= maxRadius || d < 0.001) return;
  const scale = maxRadius / d;
  unit.posX = Math.max(minX, Math.min(maxX, anchorX + dx * scale));
  unit.posY = Math.max(minY, Math.min(maxY, anchorY + dy * scale));
  syncMovedUnitPathState(unit);
}

function clampLaneControlledUnitToCombatLeash(unit, minX, maxX, minY, maxY, target = null) {
  if (!isLaneControlledUnit(unit))
    return false;
  if (target && !shouldAnchorClampLaneControlledCombat(unit, target))
    return false;

  const anchor = getLaneControlledSharedCombatAnchor(unit);
  if (!anchor)
    return false;

  const previousX = Number(unit.posX);
  const previousY = Number(unit.posY);
  clampUnitToAnchor(
    unit,
    anchor.x,
    anchor.y,
    getLaneControlledCombatLeashRadius(unit),
    minX,
    maxX,
    minY,
    maxY
  );
  return previousX !== Number(unit.posX) || previousY !== Number(unit.posY);
}

function resolveContactFrame(attacker, target) {
  const dx = Number(attacker && attacker.posX) - Number(target && target.posX);
  const dy = Number(attacker && attacker.posY) - Number(target && target.posY);
  const dist = Math.sqrt((dx * dx) + (dy * dy));
  if (dist > 0.0001) {
    const forward = { x: dx / dist, y: dy / dist };
    return {
      forward,
      right: { x: -forward.y, y: forward.x },
    };
  }

  const attackerForward = getUnitForwardDirection(attacker);
  const fallbackForward = {
    x: -(Number(attackerForward && attackerForward.x) || 0),
    y: -(Number(attackerForward && attackerForward.y) || 1),
  };
  const fallbackMag = Math.sqrt((fallbackForward.x * fallbackForward.x) + (fallbackForward.y * fallbackForward.y));
  if (fallbackMag > 0.0001) {
    const forward = {
      x: fallbackForward.x / fallbackMag,
      y: fallbackForward.y / fallbackMag,
    };
    return {
      forward,
      right: { x: -forward.y, y: forward.x },
    };
  }

  return {
    forward: { x: 0, y: -1 },
    right: { x: 1, y: 0 },
  };
}

function getContactSlotPoint(lane, attacker, target, stopDistance) {
  const attackers = lane.units
    .filter(u =>
      u &&
      u.hp > 0 &&
      !u.isDefender &&
      u.combatTarget &&
      u.combatTarget.unitId === target.id &&
      (!isLaneControlledUnit(attacker) || !isLaneControlledUnit(u) || isSameLaneControlledPacket(u, attacker)))
    .sort((a, b) => {
      const leftKey = resolvePacketUnitSortKey(a);
      const rightKey = resolvePacketUnitSortKey(b);
      return leftKey.localeCompare(rightKey);
    });

  const slotIndex = Math.max(0, attackers.findIndex(u => u.id === attacker.id));
  const radius = Math.max(0.75, getUnitContactRadius(attacker.type) + getUnitContactRadius(target.type));
  const centroid = attackers.reduce((sum, unit) => ({
    x: sum.x + (Number(unit.posX) || 0),
    y: sum.y + (Number(unit.posY) || 0),
  }), { x: 0, y: 0 });
  const centroidX = attackers.length > 0 ? centroid.x / attackers.length : Number(attacker.posX) || Number(target.posX) || 0;
  const centroidY = attackers.length > 0 ? centroid.y / attackers.length : Number(attacker.posY) || Number(target.posY) || 0;
  let baseAngle = Math.atan2(centroidY - Number(target.posY), centroidX - Number(target.posX));
  if (!Number.isFinite(baseAngle)) {
    const frame = resolveContactFrame(attacker, target);
    baseAngle = Math.atan2(frame.forward.y, frame.forward.x);
  }

  let ring = 0;
  let slotOnRing = slotIndex;
  let slotsThisRing = 6;
  let attackersBeforeRing = 0;
  while (slotOnRing >= slotsThisRing) {
    slotOnRing -= slotsThisRing;
    attackersBeforeRing += slotsThisRing;
    ring += 1;
    slotsThisRing = 6 + (ring * 2);
  }

  const ringSpacing = Math.max(0.70, radius * 0.90);
  const ringRadius = Math.max(stopDistance, radius * 1.1) + (ring * ringSpacing);
  const occupiedSlotsOnRing = Math.max(1, Math.min(slotsThisRing, attackers.length - attackersBeforeRing));
  const angle = baseAngle + ((slotOnRing / occupiedSlotsOnRing) * Math.PI * 2);

  return {
    x: target.posX + (Math.cos(angle) * ringRadius),
    y: target.posY + (Math.sin(angle) * ringRadius),
  };
}

function isBacklinePreferredBand(preferredBand) {
  const normalizedBand = normalizePreferredBand(preferredBand);
  return normalizedBand === UNIT_PREFERRED_BANDS.RANGED_REAR
    || normalizedBand === UNIT_PREFERRED_BANDS.PRIEST_REAR;
}

function resolvePacketBandCombatDistance(preferredBand, stopDistance) {
  const normalizedBand = normalizePreferredBand(preferredBand);
  switch (normalizedBand) {
    case UNIT_PREFERRED_BANDS.PRIEST_REAR:
      return Math.max(stopDistance, 3.0);
    case UNIT_PREFERRED_BANDS.RANGED_REAR:
      return Math.max(stopDistance, 2.35);
    case UNIT_PREFERRED_BANDS.SPEAR_LINE:
      return Math.max(stopDistance, 1.75);
    case UNIT_PREFERRED_BANDS.SWORD_LINE:
      return Math.max(stopDistance, 1.25);
    case UNIT_PREFERRED_BANDS.SHIELD_FRONT:
    default:
      return Math.max(stopDistance, 0.9);
  }
}

function getPacketBandCombatPoint(lane, attacker, target, stopDistance) {
  if (DISABLE_RIGID_PACKET_FORMATIONS)
    return null;

  const preferredBand = resolveUnitPreferredBand(attacker);
  const desiredDistance = resolvePacketBandCombatDistance(preferredBand, stopDistance);
  if (desiredDistance > stopDistance + CONTACT_SLOT_TOLERANCE && !isBacklinePreferredBand(preferredBand))
    return null;

  const packetUnits = lane.units
    .filter((unit) =>
      unit
      && unit.hp > 0
      && !unit.isDefender
      && isSameLaneControlledPacket(unit, attacker))
    .sort((left, right) =>
      resolvePacketUnitSortKey(left).localeCompare(resolvePacketUnitSortKey(right)));
  const bandUnits = packetUnits.filter((unit) => resolveUnitPreferredBand(unit) === preferredBand);
  const bandIndex = Math.max(0, bandUnits.findIndex((unit) => unit && unit.id === attacker.id));
  const columns = resolvePacketFormationColumns(preferredBand, bandUnits.length || 1);
  const row = Math.floor(bandIndex / columns);
  const column = bandIndex % columns;
  const lateralIndex = resolveCenteredFormationOffset(column, columns);
  const anchor = getLaneControlledSharedCombatAnchor(attacker);
  const frame = anchor
    ? resolveContactFrame({ posX: anchor.x, posY: anchor.y }, target)
    : resolveContactFrame(attacker, target);
  const depth = desiredDistance + (row * ROUTE_FORMATION_ROW_SPACING * 0.8);
  const lateralOffset = lateralIndex * ROUTE_FORMATION_COLUMN_SPACING * 0.9;
  return {
    x: Number(target.posX) + (frame.forward.x * depth) + (frame.right.x * lateralOffset),
    y: Number(target.posY) + (frame.forward.y * depth) + (frame.right.y * lateralOffset),
  };
}

function getLaneControlledCombatPocketPoint(lane, attacker, target, stopDistance) {
  const bandPoint = isLaneControlledUnit(attacker)
    ? getPacketBandCombatPoint(lane, attacker, target, stopDistance)
    : null;
  const desiredPoint = bandPoint || getContactSlotPoint(lane, attacker, target, stopDistance);
  if (!isLaneControlledUnit(attacker))
    return desiredPoint;
  if (shouldLaneControlledUnitFreeRoamInCombat(attacker))
    return desiredPoint;

  const anchor = getLaneControlledSharedCombatAnchor(attacker);
  if (!anchor)
    return desiredPoint;

  const targetX = Number(target && target.posX);
  const targetY = Number(target && target.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return desiredPoint;

  const leashRadius = getLaneControlledCombatLeashRadius(attacker);
  const shouldAnchorClamp = shouldAnchorClampLaneControlledCombat(attacker, target);
  const pocketRadius = Math.max(
    stopDistance + ROUTE_FORMATION_ROW_SPACING,
    Math.min(leashRadius * LANE_COMBAT_POCKET_RADIUS_SCALE, stopDistance + LANE_COMBAT_POCKET_RADIUS_PADDING)
  );
  const boundedCenter = shouldAnchorClamp
    ? (clampPointToRadius(
      targetX,
      targetY,
      anchor.x,
      anchor.y,
      Math.max(0, leashRadius - pocketRadius)
    ) || { x: targetX, y: targetY })
    : { x: targetX, y: targetY };
  const offsetX = Number(desiredPoint.x) - targetX;
  const offsetY = Number(desiredPoint.y) - targetY;
  const offsetDistance = Math.sqrt((offsetX * offsetX) + (offsetY * offsetY));
  const boundedOffsetScale = offsetDistance > pocketRadius && offsetDistance > 0.001
    ? pocketRadius / offsetDistance
    : 1;

  const boundedPoint = shouldAnchorClamp
    ? clampPointToRadius(
      boundedCenter.x + (offsetX * boundedOffsetScale),
      boundedCenter.y + (offsetY * boundedOffsetScale),
      anchor.x,
      anchor.y,
      leashRadius
    )
    : {
      x: boundedCenter.x + (offsetX * boundedOffsetScale),
      y: boundedCenter.y + (offsetY * boundedOffsetScale),
    };
  return boundedPoint || desiredPoint;
}

function shouldUseSimpleContactApproach(unit, target) {
  return !!(
    DISABLE_RIGID_PACKET_FORMATIONS
    && isLaneControlledUnit(unit)
    && target
    && target.kind === "unit"
  );
}

function isLaneControlledUnitOutsideDefenseZone(game, unit, target) {
  if (!isLaneControlledUnit(unit))
    return false;
  if (getLaneCommandStateForUnit(game, unit) !== LANE_COMMAND_STATES.DEFEND)
    return false;
  return !isTargetInsideLaneDefenseZone(game, unit, target);
}

function advanceLaneControlledUnitAlongRoute(unit) {
  if (!unit || !Array.isArray(unit.routeSegments) || unit.routeSegments.length === 0)
    return false;

  const speed = Number(unit.baseSpeed) || 0.18;
  const advanced = advanceRouteState(unit, speed);
  relaxUnitRouteOffsets(unit, speed);
  setUnitRouteSnapshotState(unit);
  unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
  return advanced;
}

function moveLaneControlledUnitToAnchor(unit) {
  if (!unit)
    return false;

  const waypointTargetX = Number(unit.currentWaypointTargetX);
  const waypointTargetY = Number(unit.currentWaypointTargetY);
  const anchorTargetX = Number(unit.anchorTargetX);
  const anchorTargetY = Number(unit.anchorTargetY);
  const speed = Number(unit.baseSpeed) || 0.18;
  const useLooseHoldTarget = DISABLE_RIGID_PACKET_FORMATIONS
    && Number.isFinite(anchorTargetX)
    && Number.isFinite(anchorTargetY);
  const targetPoint = {
    x: useLooseHoldTarget
      ? anchorTargetX
      : (DISABLE_RIGID_PACKET_FORMATIONS && Number.isFinite(waypointTargetX)
        ? waypointTargetX
        : Number(unit.anchorTargetX)),
    y: useLooseHoldTarget
      ? anchorTargetY
      : (DISABLE_RIGID_PACKET_FORMATIONS && Number.isFinite(waypointTargetY)
        ? waypointTargetY
        : Number(unit.anchorTargetY)),
  };
  if (!Number.isFinite(targetPoint.x) || !Number.isFinite(targetPoint.y))
    return false;

  const projection = projectPointOntoRouteSegments(unit.routeSegments, targetPoint);
  const currentProgress = computeUnitRoutePathIndex(unit);
  if (projection && Math.abs((projection.routeProgress || 0) - currentProgress) > LANE_FORMATION_APPROACH_PROGRESS_TOLERANCE) {
    const direction = projection.routeProgress > currentProgress ? 1 : -1;
    advanceRouteState(unit, direction * speed);
    relaxUnitRouteOffsets(unit, speed);
    setUnitRouteSnapshotState(unit);
    unit.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
    return true;
  }

  const dx = Number(unit.posX) - targetPoint.x;
  const dy = Number(unit.posY) - targetPoint.y;
  const distanceToAnchor = Math.sqrt((dx * dx) + (dy * dy));
  if (DISABLE_RIGID_PACKET_FORMATIONS) {
    const holdRadius = useLooseHoldTarget
      ? 0.28
      : Math.max(
        1.1,
        Math.min(2.25, (Number(unit.cohesionRadius) || ROUTE_FORMATION_ROW_SPACING) * 0.9)
      );
    if (distanceToAnchor <= holdRadius) {
      unit.movementMode = UNIT_MOVEMENT_MODES.FORMATION_JOIN;
      return false;
    }
  } else if (distanceToAnchor <= LANE_FORMATION_ARRIVAL_DEAD_ZONE) {
    unit.posX = targetPoint.x;
    unit.posY = targetPoint.y;
    syncUnitRouteStateToWorldPosition(unit);
    unit.movementMode = UNIT_MOVEMENT_MODES.FORMATION_JOIN;
    return false;
  }

  moveTowardPoint2D(unit, targetPoint.x, targetPoint.y, speed, -64, 64, -64, 64);
  syncUnitRouteStateToWorldPosition(unit);
  const nextDx = Number(unit.posX) - targetPoint.x;
  const nextDy = Number(unit.posY) - targetPoint.y;
  unit.movementMode = Math.sqrt((nextDx * nextDx) + (nextDy * nextDy)) <= LANE_FORMATION_JOIN_DISTANCE
    ? UNIT_MOVEMENT_MODES.FORMATION_JOIN
    : UNIT_MOVEMENT_MODES.RETURN_TO_ANCHOR;
  return true;
}

function getPairSpacing(a, b, fallback) {
  let spacing = Math.max(
    fallback,
    getUnitContactRadius(a && a.type) + getUnitContactRadius(b && b.type)
  );

  const leftTargetId = a && a.combatTarget && a.combatTarget.unitId
    ? a.combatTarget.unitId
    : (a ? a.combatTargetId : null);
  const rightTargetId = b && b.combatTarget && b.combatTarget.unitId
    ? b.combatTarget.unitId
    : (b ? b.combatTargetId : null);
  const sharedTargetSpacing = !!(
    DISABLE_RIGID_PACKET_FORMATIONS
    && leftTargetId
    && rightTargetId
    && leftTargetId === rightTargetId
    && getUnitAttackRange(a && a.type) <= 2.0
    && getUnitAttackRange(b && b.type) <= 2.0
  );
  if (sharedTargetSpacing)
    spacing = Math.max(spacing, getUnitContactRadius(a && a.type) + getUnitContactRadius(b && b.type) + 0.20);

  return spacing;
}

function tryResolveSettledFormationPairSpacing(a, b, minSpacing, fallbackSpacing) {
  if (!isLaneControlledUnitSettledInFormation(a) || !isLaneControlledUnitSettledInFormation(b))
    return null;
  if (!Number.isFinite(Number(a && a.anchorTargetX)) || !Number.isFinite(Number(a && a.anchorTargetY)))
    return null;
  if (!Number.isFinite(Number(b && b.anchorTargetX)) || !Number.isFinite(Number(b && b.anchorTargetY)))
    return null;

  const anchorDx = Number(b.anchorTargetX) - Number(a.anchorTargetX);
  const anchorDy = Number(b.anchorTargetY) - Number(a.anchorTargetY);
  const anchorDistance = Math.sqrt((anchorDx * anchorDx) + (anchorDy * anchorDy));
  if (!Number.isFinite(anchorDistance) || anchorDistance <= 0)
    return null;

  const settledSlotSpacing = Math.max(
    minSpacing,
    anchorDistance - LANE_FORMATION_SETTLED_SLOT_SPACING_SLACK
  );
  return Math.min(fallbackSpacing, settledSlotSpacing);
}

function isLaneControlledUnitSettledInFormation(unit) {
  if (DISABLE_RIGID_PACKET_FORMATIONS)
    return false;

  if (!isLaneControlledUnit(unit))
    return false;
  if (unit.combatTarget || unit.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
    return false;

  const anchorX = Number.isFinite(Number(unit && unit.anchorTargetX))
    ? Number(unit.anchorTargetX)
    : Number(unit && unit.groupCenterX);
  const anchorY = Number.isFinite(Number(unit && unit.anchorTargetY))
    ? Number(unit.anchorTargetY)
    : Number(unit && unit.groupCenterY);
  if (!Number.isFinite(anchorX) || !Number.isFinite(anchorY))
    return false;

  const dx = Number(unit.posX) - anchorX;
  const dy = Number(unit.posY) - anchorY;
  if (!Number.isFinite(dx) || !Number.isFinite(dy))
    return false;
  return Math.sqrt((dx * dx) + (dy * dy)) <= LANE_FORMATION_SETTLED_SEPARATION_DISTANCE;
}

function getUnitFormationLateralAxis(unit) {
  const axisX = Number(unit && unit.anchorLateralX);
  const axisY = Number(unit && unit.anchorLateralY);
  const magnitude = Math.sqrt((axisX * axisX) + (axisY * axisY));
  if (!Number.isFinite(axisX) || !Number.isFinite(axisY) || magnitude < 0.001)
    return null;
  return {
    x: axisX / magnitude,
    y: axisY / magnitude,
  };
}

function clampLaneControlledUnitToFormationDrift(unit, minX, maxX, minY, maxY) {
  if (!isLaneControlledUnitSettledInFormation(unit))
    return false;
  if (!Number.isFinite(Number(unit && unit.anchorTargetX)) || !Number.isFinite(Number(unit && unit.anchorTargetY)))
    return false;

  const lateralAxis = getUnitFormationLateralAxis(unit);
  if (!lateralAxis)
    return false;

  const anchorX = Number(unit.anchorTargetX);
  const anchorY = Number(unit.anchorTargetY);
  const lateralOffset = ((Number(unit.posX) - anchorX) * lateralAxis.x)
    + ((Number(unit.posY) - anchorY) * lateralAxis.y);
  const clampedOffset = Math.max(
    -LANE_FORMATION_SETTLED_MAX_LATERAL_OFFSET,
    Math.min(LANE_FORMATION_SETTLED_MAX_LATERAL_OFFSET, lateralOffset)
  );
  if (Math.abs(clampedOffset - lateralOffset) <= 0.0001)
    return false;

  unit.posX = Math.max(minX, Math.min(maxX, Number(unit.posX) + (lateralAxis.x * (clampedOffset - lateralOffset))));
  unit.posY = Math.max(minY, Math.min(maxY, Number(unit.posY) + (lateralAxis.y * (clampedOffset - lateralOffset))));
  syncMovedUnitPathState(unit);
  return true;
}

function getPairLateralAxis(a, b) {
  const axisA = getUnitFormationLateralAxis(a);
  const axisB = getUnitFormationLateralAxis(b);
  if (axisA && axisB) {
    const summedX = axisA.x + axisB.x;
    const summedY = axisA.y + axisB.y;
    const magnitude = Math.sqrt((summedX * summedX) + (summedY * summedY));
    if (magnitude >= 0.001) {
      return {
        x: summedX / magnitude,
        y: summedY / magnitude,
      };
    }
  }
  return axisA || axisB || { x: 1, y: 0 };
}

function shouldPreferLateralSpacing(a, b, dx, dy) {
  if (DISABLE_RIGID_PACKET_FORMATIONS && (isLaneControlledUnit(a) || isLaneControlledUnit(b)))
    return false;
  return !!(a && b && !a.isDefender && !b.isDefender && Math.abs(dy) >= Math.abs(dx));
}

function resolveExactOverlapAxis(a, b) {
  const seedSource = `${String(a && a.id || "")}|${String(b && b.id || "")}`;
  let hash = 0;
  for (let i = 0; i < seedSource.length; i += 1)
    hash = ((hash * 33) + seedSource.charCodeAt(i)) >>> 0;

  const angle = (hash % 360) * (Math.PI / 180);
  return {
    x: Math.cos(angle),
    y: Math.sin(angle),
  };
}

function getSharedTargetTangentialAxis(game, lane, a, b) {
  const leftTargetId = a && a.combatTarget && a.combatTarget.unitId
    ? a.combatTarget.unitId
    : (a ? a.combatTargetId : null);
  const rightTargetId = b && b.combatTarget && b.combatTarget.unitId
    ? b.combatTarget.unitId
    : (b ? b.combatTargetId : null);
  if (!leftTargetId || !rightTargetId || leftTargetId !== rightTargetId)
    return null;

  const liveTarget = resolveWaveCombatTarget(
    game,
    lane,
    a && a.combatTarget && a.combatTarget.unitId ? a.combatTarget : (b ? b.combatTarget : null)
  );
  const targetX = Number(liveTarget && liveTarget.posX);
  const targetY = Number(liveTarget && liveTarget.posY);
  if (!Number.isFinite(targetX) || !Number.isFinite(targetY))
    return null;

  const leftDistance = getWaveUnitTargetDistance(a, liveTarget);
  const rightDistance = getWaveUnitTargetDistance(b, liveTarget);
  const leftStopDistance = getUnitStopDistance(a && a.type, liveTarget && liveTarget.type);
  const rightStopDistance = getUnitStopDistance(b && b.type, liveTarget && liveTarget.type);
  const tangentialSpreadDistance = Math.max(
    2.0,
    Math.max(leftStopDistance, rightStopDistance) + 1.25
  );
  if (!Number.isFinite(leftDistance) || !Number.isFinite(rightDistance)
      || leftDistance > tangentialSpreadDistance
      || rightDistance > tangentialSpreadDistance) {
    return null;
  }

  const midX = (Number(a.posX) + Number(b.posX)) * 0.5;
  const midY = (Number(a.posY) + Number(b.posY)) * 0.5;
  const radialX = midX - targetX;
  const radialY = midY - targetY;
  const radialMagnitude = Math.sqrt((radialX * radialX) + (radialY * radialY));
  if (!Number.isFinite(radialMagnitude) || radialMagnitude < 0.001)
    return null;

  return {
    x: -radialY / radialMagnitude,
    y: radialX / radialMagnitude,
  };
}

function shouldApplySimpleLaneHostileCollision(game, a, b, distance) {
  if (!DISABLE_RIGID_PACKET_FORMATIONS)
    return false;
  if (!a || !b || !Number.isFinite(distance))
    return false;

  const laneControlled = isLaneControlledUnit(a)
    ? a
    : (isLaneControlledUnit(b) ? b : null);
  const other = laneControlled === a ? b : a;
  if (!laneControlled || !other)
    return false;
  if (!areRouteUnitsHostile(game, laneControlled, other))
    return false;
  if (!canLaneControlledUnitSeekCombat(game, laneControlled, other))
    return false;

  const stopDistance = getUnitStopDistance(laneControlled.type, other.type);
  const collisionWakeDistance = Math.max(2.4, stopDistance + 0.9);
  return distance <= collisionWakeDistance;
}

function applySeparation2D(game, lane, units, minSpacing, minX, maxX, minY, maxY) {
  for (let pass = 0; pass < 2; pass++) {
    for (let i = 0; i < units.length; i++) {
      for (let j = i + 1; j < units.length; j++) {
        const a = units[i], b = units[j];
        const simpleLaneSpacing = DISABLE_RIGID_PACKET_FORMATIONS
          && (isLaneControlledUnit(a) || isLaneControlledUnit(b));
        let dx = b.posX - a.posX;
        let dy = b.posY - a.posY;
        let d  = Math.sqrt(dx * dx + dy * dy);
        const activeCombatSpacing = !simpleLaneSpacing
          || shouldLaneControlledUnitFreeRoamInCombat(a)
          || shouldLaneControlledUnitFreeRoamInCombat(b)
          || shouldApplySimpleLaneHostileCollision(game, a, b, d);
        if (!activeCombatSpacing)
          continue;
        if (d < 0.001) {
          const overlapAxis = resolveExactOverlapAxis(a, b);
          dx = overlapAxis.x * 0.001;
          dy = overlapAxis.y * 0.001;
          d = 0.001;
        }
        let pairSpacing = getPairSpacing(a, b, minSpacing);
        if (d >= pairSpacing) continue;
        const settledA = isLaneControlledUnitSettledInFormation(a);
        const settledB = isLaneControlledUnitSettledInFormation(b);
        if (settledA && settledB) {
          const settledPairSpacing = tryResolveSettledFormationPairSpacing(a, b, minSpacing, pairSpacing);
          if (Number.isFinite(settledPairSpacing))
            pairSpacing = settledPairSpacing;
        }
        if (d >= pairSpacing)
          continue;
        const separationScale = settledA && settledB
          ? LANE_FORMATION_SETTLED_SEPARATION_SCALE
          : (settledA || settledB ? LANE_FORMATION_MIXED_SEPARATION_SCALE : 1);
        const push = Math.min((pairSpacing - d) * SEP_DAMP, SEP_MAX_PUSH) * separationScale;
        if (push <= 0.0001)
          continue;
        const sharedTargetTangentialAxis = simpleLaneSpacing && activeCombatSpacing
          ? getSharedTargetTangentialAxis(game, lane, a, b)
          : null;
        if (sharedTargetTangentialAxis) {
          const projectionA = (Number(a.posX) * sharedTargetTangentialAxis.x) + (Number(a.posY) * sharedTargetTangentialAxis.y);
          const projectionB = (Number(b.posX) * sharedTargetTangentialAxis.x) + (Number(b.posY) * sharedTargetTangentialAxis.y);
          const chooseNegativeA = projectionA < projectionB || (Math.abs(projectionA - projectionB) < 0.001 && String(a.id) <= String(b.id));
          const negative = chooseNegativeA ? a : b;
          const positive = chooseNegativeA ? b : a;
          negative.posX = Math.max(minX, Math.min(maxX, negative.posX - (sharedTargetTangentialAxis.x * push)));
          negative.posY = Math.max(minY, Math.min(maxY, negative.posY - (sharedTargetTangentialAxis.y * push)));
          positive.posX = Math.max(minX, Math.min(maxX, positive.posX + (sharedTargetTangentialAxis.x * push)));
          positive.posY = Math.max(minY, Math.min(maxY, positive.posY + (sharedTargetTangentialAxis.y * push)));
          syncMovedUnitPathState(negative);
          syncMovedUnitPathState(positive);
          continue;
        }
        if (!simpleLaneSpacing && (shouldPreferLateralSpacing(a, b, dx, dy) || (settledA && settledB))) {
          const lateralAxis = getPairLateralAxis(a, b);
          const projectionA = (Number(a.posX) * lateralAxis.x) + (Number(a.posY) * lateralAxis.y);
          const projectionB = (Number(b.posX) * lateralAxis.x) + (Number(b.posY) * lateralAxis.y);
          const chooseNegativeA = projectionA < projectionB || (Math.abs(projectionA - projectionB) < 0.001 && String(a.id) <= String(b.id));
          const negative = chooseNegativeA ? a : b;
          const positive = chooseNegativeA ? b : a;
          negative.posX = Math.max(minX, Math.min(maxX, negative.posX - (lateralAxis.x * push)));
          negative.posY = Math.max(minY, Math.min(maxY, negative.posY - (lateralAxis.y * push)));
          positive.posX = Math.max(minX, Math.min(maxX, positive.posX + (lateralAxis.x * push)));
          positive.posY = Math.max(minY, Math.min(maxY, positive.posY + (lateralAxis.y * push)));
          syncMovedUnitPathState(negative);
          syncMovedUnitPathState(positive);
          if (negative.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
            clampLaneControlledUnitToCombatLeash(negative, minX, maxX, minY, maxY);
          if (positive.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
            clampLaneControlledUnitToCombatLeash(positive, minX, maxX, minY, maxY);
          if (!simpleLaneSpacing) {
            clampLaneControlledUnitToFormationDrift(negative, minX, maxX, minY, maxY);
            clampLaneControlledUnitToFormationDrift(positive, minX, maxX, minY, maxY);
          }
          continue;
        }
        a.posX = Math.max(minX, Math.min(maxX, a.posX - (dx / d) * push));
        a.posY = Math.max(minY, Math.min(maxY, a.posY - (dy / d) * push));
        syncMovedUnitPathState(a);
        if (a.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
          clampLaneControlledUnitToCombatLeash(a, minX, maxX, minY, maxY);
        if (!simpleLaneSpacing)
          clampLaneControlledUnitToFormationDrift(a, minX, maxX, minY, maxY);
        b.posX = Math.max(minX, Math.min(maxX, b.posX + (dx / d) * push));
        b.posY = Math.max(minY, Math.min(maxY, b.posY + (dy / d) * push));
        syncMovedUnitPathState(b);
        if (b.movementMode !== UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
          clampLaneControlledUnitToCombatLeash(b, minX, maxX, minY, maxY);
        if (!simpleLaneSpacing)
          clampLaneControlledUnitToFormationDrift(b, minX, maxX, minY, maxY);
      }
    }
  }
}

function getUnitAttackRange(typeKey) {
  const stats = getTowerStats(typeKey, 1);
  if (stats && stats.range) return stats.range;
  const uDef = resolveUnitDef(typeKey);
  if (uDef && uDef.combatRange) return uDef.combatRange;
  return 1.5;  // melee default
}

function getUnitEngagementRange(typeKey) {
  // Engagement and attack are intentionally separate:
  // route units should commit to nearby defenders before they are in attack range,
  // but the leash must stay inside the local fortress combat pocket. Using the
  // full lane height here caused mid-route units to aggro early, which in turn
  // made the client yank them into combat-space near the Town Core.
  return Math.max(getUnitAttackRange(typeKey) + ENGAGEMENT_RANGE_PADDING, DEFENDER_ENGAGEMENT_RANGE);
}

function isSplitZoneUnit(unit) {
  return Number(unit.posY) < GRID_H;
}

function isMergeZoneUnit(unit) {
  return !isSplitZoneUnit(unit);
}

function getWaveUnitTargetDistance(unit, target) {
  if (!unit || !target) return null;
  const dx = Number(target.posX) - Number(unit.posX);
  const dy = Number(target.posY) - Number(unit.posY);
  return Math.sqrt(dx * dx + dy * dy);
}

function findHostileRouteUnitTarget(game, lane, unit, requireAttackRange = false) {
  if (!game || !lane || !unit)
    return null;

  const preferredLane = resolveRouteUnitLane(game, unit, lane);
  const forward = getUnitForwardDirection(unit);
  const hasRouteState = Array.isArray(unit.routeSegments) && unit.routeSegments.length > 0;
  const defendOmnidirectionalSeek = isLaneControlledUnit(unit)
    && getLaneCommandStateForUnit(game, unit) === LANE_COMMAND_STATES.DEFEND;
  const defendAnchorState = defendOmnidirectionalSeek
    ? getLaneCommandAnchorStateForUnit(game, unit)
    : null;
  let best = null;
  let bestLaneIndex = -1;
  let bestLanePriority = Infinity;
  let bestForwardDistance = Infinity;
  let bestDist = Infinity;

  for (const candidateLane of game.lanes || []) {
    if (!candidateLane || !Array.isArray(candidateLane.units))
      continue;

    const lanePriority = defendOmnidirectionalSeek
      ? 0
      : (preferredLane && candidateLane.laneIndex === preferredLane.laneIndex
        ? 0
        : 1);

    for (const candidate of candidateLane.units) {
      if (!candidate || !canEngageRouteUnitTarget(game, unit, candidate))
        continue;

      const dx = Number(candidate.posX) - Number(unit.posX);
      const dy = Number(candidate.posY) - Number(unit.posY);
      const dist = Math.sqrt((dx * dx) + (dy * dy));
      const emergencyInteriorTarget = defendOmnidirectionalSeek
        && isTargetInsideHomeFortressEmergencyZone(game, unit, candidate);
      const rangeLimit = requireAttackRange
        ? getUnitStopDistance(unit.type, candidate.type) + CONTACT_SLOT_TOLERANCE
        : (emergencyInteriorTarget
          ? Math.max(ROUTE_BLOCKING_CORRIDOR_RADIUS, FORTRESS_INTERIOR_ASSAULT_RADIUS)
          : (defendOmnidirectionalSeek
            ? Math.max(
              ROUTE_BLOCKING_CORRIDOR_RADIUS,
              (Number(defendAnchorState && defendAnchorState.engagementRadius) || LANE_COMMAND_DEFENSE_RADIUS) + ROUTE_FORMATION_ROW_SPACING
            )
            : ROUTE_BLOCKING_CORRIDOR_RADIUS));
      if (dist > rangeLimit)
        continue;

      let forwardDistance = dist;
      if (!requireAttackRange && hasRouteState && !defendOmnidirectionalSeek) {
        const direction = dist > 0.0001
          ? { x: dx / dist, y: dy / dist }
          : forward;
        const dot = (direction.x * forward.x) + (direction.y * forward.y);
        if (dot < ROUTE_BLOCKING_FORWARD_DOT)
          continue;
        forwardDistance = (dx * forward.x) + (dy * forward.y);
        if (forwardDistance < 0)
          continue;
      }

      if (
        lanePriority < bestLanePriority
        || (lanePriority === bestLanePriority && forwardDistance < bestForwardDistance)
        || (lanePriority === bestLanePriority && Math.abs(forwardDistance - bestForwardDistance) <= 0.0001 && dist < bestDist)
      ) {
        best = candidate;
        bestLaneIndex = candidateLane.laneIndex;
        bestLanePriority = lanePriority;
        bestForwardDistance = forwardDistance;
        bestDist = dist;
      }
    }
  }

  return best
    ? { entity: best, laneIndex: bestLaneIndex }
    : null;
}

function getWaveUnitPreferredTarget(game, lane, unit) {
  const routeUnitInAttackRange = findHostileRouteUnitTarget(game, lane, unit, true);
  if (routeUnitInAttackRange) return { kind: "unit", entity: routeUnitInAttackRange.entity, laneIndex: routeUnitInAttackRange.laneIndex, reason: "route_unit_attack_range" };

  const routeUnitInEngageRange = findHostileRouteUnitTarget(game, lane, unit, false);
  if (routeUnitInEngageRange) return { kind: "unit", entity: routeUnitInEngageRange.entity, laneIndex: routeUnitInEngageRange.laneIndex, reason: "route_unit_engage_range" };

  return null;
}

function shouldBarracksAttackerClearEnemyStructures(unit) {
  const spawnSourceType = resolveSpawnSourceTypeFromUnit(unit);
  return spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_ROSTER
    || spawnSourceType === SPAWN_SOURCE_TYPES.BARRACKS_HERO;
}

function isNonCoreFortressStructureTarget(target) {
  return !!(
    target
    && target.kind === "fortress_pad"
    && target.buildingType
    && target.buildingType !== "town_core"
  );
}

function isUnitInsideObjectiveFortressAssaultZone(unit, objectiveLane) {
  if (!unit || !objectiveLane)
    return false;

  const townCoreTarget = getLaneTownCoreCombatTarget(objectiveLane);
  const distanceToCore = getWaveUnitTargetDistance(unit, townCoreTarget);
  return Number.isFinite(distanceToCore) && distanceToCore <= FORTRESS_INTERIOR_ASSAULT_RADIUS;
}

function getLaneBlockingStructureTargets(lane, unit = null) {
  const targets = [];
  let townCoreTarget = null;
  if (!lane)
    return targets;

  if (Array.isArray(lane.fortressPads)) {
    for (const pad of lane.fortressPads) {
      const target = getFortressPadCombatTarget(lane, pad);
      if (!target)
        continue;
      if (target.buildingType === "town_core")
        townCoreTarget = target;
      else
        targets.push(target);
    }
  }

  for (const siteDef of BARRACKS_SITE_DEFS) {
    const target = getBarracksSiteCombatTarget(lane, siteDef.barracksId);
    if (target)
      targets.push(target);
  }

  if (shouldBarracksAttackerClearEnemyStructures(unit))
    return targets.length > 0
      ? targets
      : (townCoreTarget ? [townCoreTarget] : []);

  return townCoreTarget
    ? [townCoreTarget, ...targets]
    : targets;
}

function findBlockingStructureTarget(game, lane, unit) {
  if (!lane || !unit)
    return null;
  if (!canLaneControlledUnitSeekCombat(game, unit))
    return null;
  const objectiveLane = resolveLaneObjectiveLane(game, lane, unit);
  if (!objectiveLane || objectiveLane.eliminated || !isRouteUnitHostileToLane(game, objectiveLane, unit))
    return null;

  const forward = getUnitForwardDirection(unit);
  const hasRouteState = Array.isArray(unit.routeSegments) && unit.routeSegments.length > 0;
  const insideObjectiveFortress = isUnitInsideObjectiveFortressAssaultZone(unit, objectiveLane);
  const candidates = getLaneBlockingStructureTargets(objectiveLane, unit);
  let best = null;
  let bestForwardDistance = Infinity;
  let bestDistance = Infinity;

  for (const candidate of candidates) {
    const isInteriorStructure = isNonCoreFortressStructureTarget(candidate);
    const dx = Number(candidate.posX) - Number(unit.posX);
    const dy = Number(candidate.posY) - Number(unit.posY);
    const straightDistance = Math.sqrt((dx * dx) + (dy * dy));
    const distanceLimit = isInteriorStructure && insideObjectiveFortress
      ? FORTRESS_INTERIOR_STRUCTURE_TARGET_RADIUS
      : ROUTE_BLOCKING_CORRIDOR_RADIUS;
    if (straightDistance > distanceLimit)
      continue;

    if (!hasRouteState) {
      if (straightDistance < bestDistance) {
        best = candidate;
        bestDistance = straightDistance;
      }
      continue;
    }

      const direction = straightDistance > 0.0001
        ? { x: dx / straightDistance, y: dy / straightDistance }
        : { x: 0, y: 0 };

      let forwardDistance = straightDistance;
      if (!(isInteriorStructure && insideObjectiveFortress)) {
        const dot = (direction.x * forward.x) + (direction.y * forward.y);
        if (dot < ROUTE_BLOCKING_FORWARD_DOT)
          continue;

        forwardDistance = (dx * forward.x) + (dy * forward.y);
        if (forwardDistance < 0)
          continue;
      }

      if (forwardDistance < bestForwardDistance
          || (Math.abs(forwardDistance - bestForwardDistance) <= 0.0001 && straightDistance < bestDistance)) {
        best = candidate;
        bestForwardDistance = forwardDistance;
        bestDistance = straightDistance;
      }
    }

  return best;
}

function traceWaveUnitTick(game, lane, unit, target, details = {}) {
  if (!ENABLE_WAVE_UNIT_TRACE) return;
  if (!game || !lane || !unit) return;
  const targetId = target ? (target.unitId || target.id || null) : null;
  const targetType = target
    ? (target.kind === "unit"
      ? "defender"
      : target.kind === "fortress_pad"
        ? (target.buildingType || target.type || "fortress_pad")
        : target.kind || null)
    : null;
  const distanceToTarget = target ? getWaveUnitTargetDistance(unit, target) : null;
  const shouldLog = !!(
    details.coreDamageApplied ||
    details.movementAdvanced ||
    targetId ||
    unit.combatState === WAVE_UNIT_STATES.COMBAT ||
    unit.posY >= GRID_H - 2
  );
  if (!shouldLog) return;

  log.info("[WaveUnitTrace] tick", {
    tick: game.tick,
    laneIndex: lane.laneIndex,
    unitId: unit.id,
    unitType: unit.type,
    routeType: unit.routeType || null,
    currentSegment: unit.currentSegment || null,
    segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
    state: unit.combatState || WAVE_UNIT_STATES.IDLE,
    movementMode: unit.movementMode || null,
    inCombat: unit.combatState === WAVE_UNIT_STATES.COMBAT,
    targetId,
    targetType,
    blockedByStructure: !!unit.blockedByStructure,
    blockedByStructureId: unit.blockedByStructureId || null,
    distanceToTarget: Number.isFinite(distanceToTarget) ? Number(distanceToTarget.toFixed(3)) : null,
    movementAdvanced: !!details.movementAdvanced,
    coreDamageApplied: !!details.coreDamageApplied,
    pathIdx: Number.isFinite(unit.pathIdx) ? Number(unit.pathIdx.toFixed(2)) : null,
    preferredTargetReason: details.preferredTargetReason || null,
  });
}

function _doAttack(game, lane, attacker, target) {
  const stats    = getTowerStats(attacker.type, 1);
  const uDef     = resolveUnitDef(attacker.type);
  const dmg      = attacker.baseDmg || (stats ? stats.dmg : (uDef ? uDef.dmg : 5));
  const cdTk     = attacker.atkCdTicks || (stats ? stats.atkCdTicks : 30);
  const atkRange = getUnitAttackRange(attacker.type);

  if (atkRange > 2.0) {
    // Ranged — fire projectile
    const behavior = stats ? (stats.projBehavior || 'single') : 'single';
    fireProjectile(game, lane,
      { id: attacker.id, kind: 'unit', x: attacker.posX, y: attacker.posY },
      target.id,
      {
        dmg,
        damageType:     stats ? stats.damageType : 'NORMAL',
        behavior,
        behaviorParams: stats ? (stats.projBehaviorParams || {}) : {},
        travelTicks:    stats ? (stats.projectileTicks || 8) : 8,
        isSplash:       behavior === 'splash',
        projectileType: attacker.type,
        abilities:      [],
      }
    );
  } else {
    // Melee — instant damage
    target.hp = Math.max(0, target.hp - dmg);
  }
  attacker.attackPulse = (attacker.attackPulse || 0) + 1;
  attacker.atkCd = cdTk;
}

// ── Wave defense helpers ──────────────────────────────────────────────────────

function resolveWaveForRound(game, roundNumber) {
  const cfg = Array.isArray(game.waveConfig) ? game.waveConfig : [];
  const round = Math.max(1, Math.floor(Number(roundNumber) || 1));
  if (cfg.length === 0)
    return null;

  // Find exact wave row; if past the last, use last row with escalation.
  const exact = cfg.find(w => Number(w.wave_number) === round);
  if (exact) return exact;

  const last = cfg.reduce((a, b) => Number(a.wave_number) >= Number(b.wave_number) ? a : b);
  if (!last)
    return null;

  const extra = round - Number(last.wave_number);
  const esc = 1 + extra * ESCALATION_PER_EXTRA_ROUND;
  return {
    unit_type: last.unit_type,
    spawn_qty: last.spawn_qty,
    hp_mult:    Number(last.hp_mult)    * esc,
    dmg_mult:   Number(last.dmg_mult)   * esc,
    speed_mult: Number(last.speed_mult),
  };
}

function getUpcomingWaveNumber(game) {
  if (!game)
    return 1;

  const currentRound = Math.max(1, Math.floor(Number(game.roundNumber) || 1));
  return game.hasSpawnedWave
    ? currentRound + 1
    : currentRound;
}

function countRemainingWaveMobs(game) {
  if (!game)
    return 0;

  let remaining = 0;
  for (const lane of game.lanes || []) {
    if (!lane || lane.eliminated)
      continue;

    for (const unit of lane.spawnQueue || []) {
      if (unit && isScheduledWaveUnit(unit) && Number(unit.hp) > 0)
        remaining += 1;
    }

    for (const unit of lane.units || []) {
      if (unit && isScheduledWaveUnit(unit) && Number(unit.hp) > 0)
        remaining += 1;
    }
  }

  return remaining;
}

function isCurrentWaveComplete(game) {
  return countRemainingWaveMobs(game) <= 0;
}

function createLaneUpcomingWavePreview(game, lane, waveNumber = null) {
  const upcomingWaveNumber = Number.isInteger(waveNumber) && waveNumber > 0
    ? waveNumber
    : getUpcomingWaveNumber(game);
  const entriesByKey = new Map();

  const addEntry = ({
    source,
    unitType,
    archetypeKey = null,
    presentationKey = null,
    skinKey = null,
    count = 1,
    hpMult = 1,
    dmgMult = 1,
    speedMult = 1,
    sourceLaneIndex = -1,
    sourceBarracksId = null,
    isHero = false,
    heroKey = null,
    heroVisualStyleKey = null,
  }) => {
    if (!unitType)
      return;

    const normalizedCount = Math.max(0, Math.floor(Number(count) || 0));
    if (normalizedCount <= 0)
      return;

    const key = [
      source || "scheduled",
      String(unitType).trim().toLowerCase(),
      String(archetypeKey || "").trim().toLowerCase(),
      String(presentationKey || "").trim().toLowerCase(),
      String(skinKey || "").trim().toLowerCase(),
      Number(hpMult || 1).toFixed(3),
      Number(dmgMult || 1).toFixed(3),
      Number(speedMult || 1).toFixed(3),
      Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1,
      String(sourceBarracksId || "").trim().toLowerCase(),
      isHero ? "hero" : "unit",
      String(heroKey || "").trim().toLowerCase(),
    ].join("|");

    if (entriesByKey.has(key)) {
      entriesByKey.get(key).count += normalizedCount;
      return;
    }

    entriesByKey.set(key, {
      source: source || "scheduled",
      unitType: String(unitType).trim().toLowerCase(),
      archetypeKey: archetypeKey || null,
      presentationKey: presentationKey || null,
      skinKey: skinKey || null,
      count: normalizedCount,
      hpMult: Number(hpMult || 1),
      dmgMult: Number(dmgMult || 1),
      speedMult: Number(speedMult || 1),
      sourceLaneIndex: Number.isInteger(sourceLaneIndex) ? sourceLaneIndex : -1,
      sourceBarracksId: sourceBarracksId || null,
      isHero: !!isHero,
      heroKey: heroKey || null,
      heroVisualStyleKey: heroVisualStyleKey || null,
    });
  };

  const scheduledWave = resolveWaveForRound(game, upcomingWaveNumber);
  if (scheduledWave) {
    addEntry({
      source: "scheduled",
      unitType: scheduledWave.unit_type,
      archetypeKey: null,
      presentationKey: null,
      count: scheduledWave.spawn_qty,
      hpMult: scheduledWave.hp_mult,
      dmgMult: scheduledWave.dmg_mult,
      speedMult: getEffectiveWaveEntrySpeedMult(game, lane, scheduledWave),
    });
  }

  const entries = Array.from(entriesByKey.values()).sort((a, b) => {
    if (a.source !== b.source)
      return a.source === "scheduled" ? -1 : 1;
    if (a.count !== b.count)
      return b.count - a.count;
    return String(a.unitType).localeCompare(String(b.unitType));
  });

  return {
    waveNumber: upcomingWaveNumber,
    totalUnits: entries.reduce((sum, entry) => sum + (entry.count || 0), 0),
    entries,
  };
}

function createLaneUpcomingWaveQueue(game, lane, maxCount = 4) {
  const safeCount = Math.max(1, Math.floor(Number(maxCount) || 1));
  const firstWaveNumber = getUpcomingWaveNumber(game);
  const queue = [];

  for (let i = 0; i < safeCount; i++) {
    const waveNumber = firstWaveNumber + i;
    queue.push(createLaneUpcomingWavePreview(game, lane, waveNumber));
  }

  return queue;
}

function _spawnWaveUnit(game, lane, waveDef) {
  const unitType = waveDef.unit_type;
  const def = resolveUnitDef(unitType);
  if (!def) return;
  if (lane.units.length + lane.spawnQueue.length >= MAX_UNITS_PER_LANE) return;
  const spawnValidation = validateSpawnDefinition(game, lane, waveDef);
  if (!spawnValidation.ok) {
    log.error("[SpawnAudit][ServerQueue] rejected", {
      spawnType: spawnValidation.spawnType,
      reason: spawnValidation.reason,
      unitType,
      laneIndex: lane ? lane.laneIndex : null,
      sourceLaneIndex: waveDef && Number.isInteger(waveDef.sourceLaneIndex) ? waveDef.sourceLaneIndex : -1,
      sourceBarracksKey: normalizeBarracksSiteId(
        waveDef && (waveDef.sourceBarracksKey || waveDef.sourceBarracksId)
      ),
      sourceTeam: waveDef && waveDef.sourceTeam ? waveDef.sourceTeam : null,
      requestedSpawnIndex: waveDef && waveDef.spawnIndex,
    });
    return;
  }
  const effectiveSpeedMult = getEffectiveWaveEntrySpeedMult(game, lane, waveDef);
  const hp  = Math.ceil(def.hp    * Number(waveDef.hp_mult    || 1));
  const dmg =           def.dmg   * Number(waveDef.dmg_mult   || 1);
  const spd =           getBaseCombatPathSpeed(unitType) * effectiveSpeedMult;
  log.info("[SpawnAudit][ServerQueue] queued", {
    spawnType: spawnValidation.spawnType,
    unitType,
    laneIndex: lane.laneIndex,
    team: lane.team || null,
    sourceLaneIndex: spawnValidation.sourceLaneIndex,
    sourceTeam: spawnValidation.sourceTeam,
    sourceBarracksKey: spawnValidation.sourceBarracksKey,
    requestedSpawnKey: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
      ? `lane:${lane.laneIndex}:wave_origin`
      : `lane:${spawnValidation.sourceLaneIndex}:barracks:${spawnValidation.sourceBarracksKey}`,
    resolvedMarkerName: `server_queue_${spawnValidation.spawnType}`,
    resolvedLogicalPosition: spawnValidation.logicalPos,
    requestedSpawnIndex: spawnValidation.requestedSpawnIndex,
    resolvedSpawnIndex: spawnValidation.resolvedSpawnIndex,
    fallbackUsed: false,
    authoring: "server",
  });
  const ownerLaneIndex = spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
    ? -1
    : spawnValidation.sourceLaneIndex;
  const sourceLane = getSourceLane(game, spawnValidation.sourceLaneIndex);
  const objectiveLaneIndex = spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
    ? lane.laneIndex
    : getLaneCommandRouteObjectiveLaneIndex(game, sourceLane);
  const queuedUnit = {
    id: `wu${game.nextUnitId++}`,
    unitId: null,
    targetLaneIndex: lane.laneIndex,
    ownerLaneIndex,
    ownerLane: ownerLaneIndex,
    objectiveLaneIndex,
    sourceLaneIndex: spawnValidation.sourceLaneIndex,
    sourceTeam: spawnValidation.sourceTeam,
    sourceBarracksKey: spawnValidation.sourceBarracksKey,
    sourceBarracksId: spawnValidation.sourceBarracksKey,
    barracksId: spawnValidation.sourceBarracksKey,
    spawnSourceType: spawnValidation.spawnType,
    allegianceKey: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
      ? ALLEGIANCE_KEYS.DUNGEON
      : resolveLaneAllegianceKey(getSourceLane(game, spawnValidation.sourceLaneIndex)),
    type: unitType,
    unitTypeKey: unitType,
    archetypeKey: waveDef.archetypeKey || (isFortArchetypeKey(unitType) ? unitType : null),
    presentationKey: waveDef.presentationKey || null,
    catalogUnitKey: resolveGameplayCatalogUnitKey(unitType, waveDef.presentationKey || DEFAULT_FORT_PRESENTATION_KEY),
    skinKey: waveDef.skinKey || null,
    isHero: !!waveDef.isHero,
    heroKey: waveDef.heroKey || null,
    heroVisualStyleKey: waveDef.heroVisualStyleKey || null,
    groupId: waveDef.groupId || null,
    groupSpawnTick: Number.isFinite(Number(waveDef.groupSpawnTick)) ? Number(waveDef.groupSpawnTick) : Math.floor(Number(game && game.tick) || 0),
    groupSpawnOrdinal: Number.isFinite(Number(waveDef.groupSpawnOrdinal)) ? Number(waveDef.groupSpawnOrdinal) : null,
    rosterKey: waveDef.rosterKey || null,
    role: waveDef.role || null,
    combatRole: waveDef.combatRole || null,
    preferredBand: waveDef.preferredBand || null,
    pathIdx: 0,
    hp,
    maxHp: hp,
    baseDmg: dmg,
    baseSpeed: spd,
    atkCd: 0,
    atkCdTicks: def.atkCdTicks,
    armorType: def.armorType || "MEDIUM",
    damageReductionPct: def.damageReductionPct || 0,
    abilities: buildAbilitiesForUnitType(unitType),
    bounty: def.bounty || 1,
    stance: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE ? UNIT_STANCES.ATTACK : null,
    pathContractType: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE
      ? PATH_CONTRACT_TYPES.WAVE_LANE
      : null,
    isWaveUnit: spawnValidation.spawnType === SPAWN_SOURCE_TYPES.DUNGEON_WAVE,
    isDefender: false,
    combatTarget: null,
    combatTargetId: null,
    combatTargetLockedUntilTick: 0,
    currentTargetId: null,
    regroupUntilTick: 0,
    combatState: WAVE_UNIT_STATES.IDLE,
    state: WAVE_UNIT_STATES.IDLE,
    movementMode: UNIT_MOVEMENT_MODES.LANE_TRAVEL,
    hasBreachedTownCore: false,
    spawnIndex: spawnValidation.resolvedSpawnIndex,  // position in wave formation rectangle
    spawnLogicalPos: spawnValidation.logicalPos,
  };
  applyCanonicalUnitMirrors(game, lane, queuedUnit);
  lane.spawnQueue.push(queuedUnit);
}

function finalizeCompletedWave(game) {
  if (!game || !game.hasSpawnedWave) return;

  for (const lane of game.lanes) {
    if (lane.leakCountThisRound > 0 || lane.lifeLossThisRound > 0) {
      lane.wavesLeaked += 1;
      lane.currentHoldStreak = 0;
      lane.biggestLeakTaken = Math.max(lane.biggestLeakTaken || 0, lane.lifeLossThisRound || lane.leakCountThisRound || 0);
    } else {
      lane.wavesHeld += 1;
      lane.currentHoldStreak = (lane.currentHoldStreak || 0) + 1;
      lane.longestHoldStreak = Math.max(lane.longestHoldStreak || 0, lane.currentHoldStreak);
    }
  }
  game.roundSnapshots.push({
    round: game.roundNumber,
    elapsedSeconds: Math.floor(game.tick / TICK_HZ),
    lanes: game.lanes.map(l => createRoundSnapshotLane(game, l)),
  });
  game._pendingEvents.push({
    type: "ml_round_end",
    roundNumber: game.roundNumber,
    teamHp: Object.assign({}, game.teamHp),
  });
}

function resetWaveIntervalState(game) {
  if (!game) return;
  for (const lane of game.lanes) {
    lane.leakCountThisRound = 0;
    lane.lifeLossThisRound = 0;
    lane.sendCountThisRound = 0;
    lane.sendSpendThisRound = 0;
    lane.buildSpendThisRound = 0;
  }
}

function spawnScheduledWave(game) {
  if (!game) return;

  const nextRoundNumber = getUpcomingWaveNumber(game);
  const waveDef = resolveWaveForRound(game, nextRoundNumber);
  if (!waveDef)
    return false;

  if (game.hasSpawnedWave)
    finalizeCompletedWave(game);

  game.roundNumber = nextRoundNumber;

  resetWaveIntervalState(game);
  const waveSizes = {};
  for (const lane of game.lanes) {
    if (lane.eliminated) continue;
    waveSizes[lane.laneIndex] = waveDef.spawn_qty;
    for (let i = 0; i < waveDef.spawn_qty; i++) _spawnWaveUnit(game, lane, waveDef);
  }

  game.hasSpawnedWave = true;
  game.lastWaveSpawnTick = game.tick;
  game._pendingEvents.push({
    type: "ml_wave_start",
    roundNumber: game.roundNumber,
    waveSizes,
  });
  return true;
}

function grantScheduledIncome(game) {
  if (!game) return;
  const interval = Math.max(1, Math.floor(Number(game.incomeIntervalTicks) || INCOME_INTERVAL_TICKS));
  if (!Number.isInteger(game.nextIncomeTick) || game.nextIncomeTick <= 0)
    game.nextIncomeTick = game.tick + interval;

  while (game.tick >= game.nextIncomeTick) {
    for (const lane of game.lanes) {
      if (lane && !lane.eliminated)
        lane.gold += lane.income;
    }
    game.nextIncomeTick += interval;
  }
}

function runScheduledBarracksSends(game) {
  if (!game) return;
  for (const lane of game.lanes) {
    if (!lane || lane.eliminated)
      continue;

    ensureBarracksSiteStates(lane, game);
    for (const siteDef of BARRACKS_SITE_DEFS) {
      const descriptor = describeBarracksSite(game, lane, siteDef.barracksId);
      if (!descriptor || !descriptor.isBuilt)
        continue;

      const interval = Math.max(1, descriptor.sendIntervalTicks);
      let siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
      if (!siteState)
        continue;

      if (!Number.isInteger(siteState.nextSendTick) || siteState.nextSendTick <= 0)
        siteState.nextSendTick = game.tick + interval;

      while (siteState != null && game.tick >= siteState.nextSendTick) {
        const siteSnapshot = createBarracksSiteSnapshot(game, lane, siteDef.barracksId);
        console.log(
          `[BarracksTrace][ServerTimer] tick=${game.tick} sourceLane=${lane.laneIndex} ` +
          `barracksId='${siteDef.barracksId}' built=${siteSnapshot ? siteSnapshot.isBuilt : false} ` +
          `roster=${summarizeBarracksSiteRosterEntries(siteSnapshot && siteSnapshot.roster)}`);
        const spawnedCount = spawnScheduledBarracksRoster(game, lane, siteDef.barracksId);
        console.log(
          `[BarracksTrace][ServerTimer] tick=${game.tick} sourceLane=${lane.laneIndex} ` +
          `barracksId='${siteDef.barracksId}' spawnedCount=${spawnedCount}`);
        siteState = getBarracksSiteState(lane, siteDef.barracksId, game);
        if (siteState != null)
            siteState.nextSendTick += interval;
      }
    }
  }
}

function runScheduledWaves(game) {
  if (!game) return;
  const interval = Math.max(1, Math.floor(Number(game.waveIntervalTicks) || WAVE_TIMER_TICKS));
  if (!Number.isInteger(game.nextWaveTick) || game.nextWaveTick <= 0)
    game.nextWaveTick = game.tick + interval;

  while (game.tick >= game.nextWaveTick) {
    spawnScheduledWave(game);
    game.nextWaveTick += interval;
  }
}

function startNextWaveNow(game) {
  if (!game || game.phase !== "playing")
    return false;

  if (!isCurrentWaveComplete(game))
    return false;

  const interval = Math.max(1, Math.floor(Number(game.waveIntervalTicks) || WAVE_TIMER_TICKS));
  const spawned = spawnScheduledWave(game);
  if (spawned)
    game.nextWaveTick = Math.floor(Number(game.tick) || 0) + interval;
  return spawned;
}

function mlTick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;
  if (!game.startedAt) game.startedAt = Date.now();
  grantScheduledIncome(game);
  runScheduledWaves(game);
  runScheduledBarracksSends(game);
  syncLaneCommandAssignments(game);
  game.roundState = "combat";
  game.roundStateTicks = Number.isInteger(game.lastWaveSpawnTick)
    ? Math.max(0, game.tick - game.lastWaveSpawnTick)
    : game.tick;

  for (const lane of game.lanes) {
    const laneHasActiveOccupiers = laneHasOccupyingForces(lane);
    if (lane.eliminated && !laneHasActiveOccupiers) continue;
    const laneActive = !lane.eliminated;

    // Drain spawn queue — place each unit at a unique position in a rectangle
    // so the whole wave arrives at once but spread across the grid width.
    // col = spawnIndex % GRID_W, row = floor(spawnIndex / GRID_W)
    while (lane.spawnQueue.length > 0) {
      const unit = lane.spawnQueue.shift();
      const idx  = unit.spawnIndex ?? 0;
      const spawnLogicalPos = unit.spawnLogicalPos || {
        x: idx % GRID_W,
        y: Math.floor(idx / GRID_W),
      };
      const routeInit = initializeMovingUnitRouteState(game, lane, unit, spawnLogicalPos);
      if (!routeInit || !routeInit.ok) {
        log.error("[SpawnAudit][ServerLive] rejected", {
          reason: routeInit && routeInit.reason ? routeInit.reason : "Route initialization failed",
          unitId: unit && unit.id || null,
          unitType: unit && unit.type || null,
          targetLaneIndex: lane.laneIndex,
          sourceLaneIndex: Number.isInteger(unit && unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1,
          sourceBarracksKey: unit && (unit.sourceBarracksKey || unit.sourceBarracksId) || null,
          spawnSourceType: resolveSpawnSourceTypeFromUnit(unit),
          requestedLogicalPosition: spawnLogicalPos,
        });
        continue;
      }
      unit.ownerLane = Number.isInteger(unit.sourceLaneIndex) && unit.sourceLaneIndex >= 0
        ? unit.sourceLaneIndex
        : -1;
      unit.ownerLaneIndex = unit.ownerLane;
      applyCanonicalUnitMirrors(game, lane, unit);
      log.info("[SpawnAudit][ServerLive] materialized", {
        spawnType: resolveSpawnSourceTypeFromUnit(unit),
        unitId: unit.id,
        unitType: unit.type,
        targetLaneIndex: lane.laneIndex,
        targetTeam: lane.team || null,
        sourceLaneIndex: Number.isInteger(unit.sourceLaneIndex) ? unit.sourceLaneIndex : -1,
        sourceTeam: unit.sourceTeam || null,
        sourceBarracksKey: unit.sourceBarracksKey || unit.sourceBarracksId || null,
        routeType: unit.routeType,
        currentSegment: unit.currentSegment,
        segmentProgress: Number.isFinite(unit.segmentProgress) ? Number(unit.segmentProgress.toFixed(3)) : null,
        resolvedMarkerName: "server_route_graph",
        resolvedLogicalPosition: spawnLogicalPos,
        fallbackUsed: false,
        authoring: "server",
      });
      if (unit.sourceBarracksKey || unit.sourceBarracksId) {
        console.log(
          `[BarracksTrace][ServerSpawn] tick=${game.tick} targetLane=${lane.laneIndex} ` +
          `unit='${unit.id}' type='${unit.type}' barracksId='${unit.sourceBarracksKey || unit.sourceBarracksId}' ` +
          `sourceLane=${unit.sourceLaneIndex} sourceTeam='${unit.sourceTeam || ""}'`);
      }
      lane.units.push(unit);
      if (unit.abilities && unit.abilities.length > 0) {
        resolveAbilityHook(game, lane, unit, "onSpawn", {});
      }
    }
    for (const unit of lane.units) {
      applyCanonicalUnitMirrors(game, lane, unit);
    }

    // Decrement cooldowns
    for (const u of lane.units) {
      if (u.atkCd > 0) u.atkCd -= 1;
    }
          // Use raw float pathIdx for row distance — avoids Math.floor rounding units out of range.
    // ── Mobile defender AI ────────────────────────────────────────────────────
    // Wave unit movement, combat targeting, and Town Core assaults
    const dotDeadIds = new Set();

    for (const u of lane.units) {
      if (u.hp <= 0) continue;
      if (u.isDefender) continue;  // defenders handled above
      const startedTickWithCombatTarget = !!(u.combatTarget && u.combatTarget.unitId);
      const startedTickInCombat = u.combatState === WAVE_UNIT_STATES.COMBAT || u.routeState === WAVE_UNIT_STATES.COMBAT;
      const supportProfile = resolveUnitSupportProfile(u);
      const healerMode = supportProfile.isHealer;
      u.combatState = WAVE_UNIT_STATES.IDLE;
      u.routeState = WAVE_UNIT_STATES.IDLE;
      u.blockedByStructure = false;
      u.blockedByStructureId = null;
      u.blockedByStructureType = null;
      resolveStatuses(u, game.tick);
      if (u.hp <= 0) {
        u.combatState = WAVE_UNIT_STATES.DEAD;
        u.routeState = WAVE_UNIT_STATES.DEAD;
        dotDeadIds.add(u.id);
        traceWaveUnitTick(game, lane, u, null, {
          movementAdvanced: false,
          coreDamageApplied: false,
        });
        continue;
      }

      // ── Wave unit combat target tracking (mobile defenders) ─────────────────
      let preferredTarget = null;
      let resolvedCombatTarget = null;
      if (!healerMode) {
        resolvedCombatTarget = u.combatTarget && u.combatTarget.unitId
          ? resolveWaveCombatTarget(game, lane, u.combatTarget)
          : null;
        if (u.combatTarget && u.combatTarget.unitId) {
          const targetStillValid = !!resolvedCombatTarget && isUnitCombatTargetStillValid(game, lane, u, resolvedCombatTarget);
          if (!targetStillValid) {
            clearUnitCombatTarget(u, game.tick);
            resolvedCombatTarget = null;
          }
        }

        const laneUnitTargetLockActive = !!(
          isLaneControlledUnit(u)
          && resolvedCombatTarget
          && resolvedCombatTarget.kind === "unit"
          && Number(u.combatTargetLockedUntilTick) > game.tick
        );
        const canReacquireUnitTarget = !isLaneControlledUnitInRegroupWindow(u, game.tick);
        if (!laneUnitTargetLockActive && canReacquireUnitTarget) {
          if (canLaneControlledUnitSeekCombat(game, u))
            preferredTarget = getWaveUnitPreferredTarget(game, lane, u);
          if (!preferredTarget && isLaneControlledUnit(u) && !DISABLE_RIGID_PACKET_FORMATIONS)
            preferredTarget = findLaneFormationSharedCombatTarget(game, lane, u);
        }
        if (preferredTarget && preferredTarget.kind === "unit") {
          const shouldAssignPreferredTarget = !resolvedCombatTarget
            || shouldSwitchCombatTarget(u, resolvedCombatTarget, preferredTarget);
          if (shouldAssignPreferredTarget) {
            assignUnitCombatTarget(u, {
              unitId: preferredTarget.entity.id,
              kind: "unit",
              laneIndex: preferredTarget.laneIndex,
            }, game.tick);
            resolvedCombatTarget = resolveWaveCombatTarget(game, lane, u.combatTarget);
          }
          if (isLaneControlledUnit(u))
            propagateLaneFormationCombatTarget(game, u, preferredTarget.entity, preferredTarget.laneIndex);
        }

        if (!u.combatTarget) {
          const blockingTarget = findBlockingStructureTarget(game, lane, u);
          if (blockingTarget) {
            if (blockingTarget.buildingType === "town_core")
              markTownCoreBreach(game, lane, u, blockingTarget);
            u.blockedByStructure = true;
            u.blockedByStructureId = blockingTarget.unitId;
            u.blockedByStructureType = blockingTarget.buildingType || blockingTarget.type || null;
            assignUnitCombatTarget(u, {
              unitId: blockingTarget.unitId,
              kind: "fortress_pad",
              padId: blockingTarget.padId,
              laneIndex: Number.isInteger(blockingTarget.laneIndex) ? blockingTarget.laneIndex : lane.laneIndex,
            }, game.tick);
            resolvedCombatTarget = resolveWaveCombatTarget(game, lane, u.combatTarget);
          }
        }
      }

      let attackedTarget = false;
      let movementAdvanced = false;
      let coreDamageApplied = false;
      if (healerMode) {
        const healTarget = findFriendlyHealTarget(game, lane, u, supportProfile);
        if (healTarget) {
          const healRange = getUnitAttackRange(u.type);
          const stopDist = healRange + 0.15;
          const dist = dist2D(u, healTarget);
          assignUnitSupportTarget(u, {
            unitId: healTarget.id,
            kind: "unit",
            laneIndex: lane.laneIndex,
          });
          u.combatState = WAVE_UNIT_STATES.COMBAT;
          u.routeState = WAVE_UNIT_STATES.COMBAT;
          u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
          if (dist <= stopDist + CONTACT_SLOT_TOLERANCE) {
            if (u.atkCd <= 0) {
              const healAmount = Math.max(1, Math.round(supportProfile.healAmount || 1));
              const maxHp = Math.max(1, Number(healTarget.maxHp) || Number(healTarget.hp) || 1);
              healTarget.hp = Math.min(maxHp, (Number(healTarget.hp) || 0) + healAmount);
              u.attackPulse = (u.attackPulse || 0) + 1;
              u.atkCd = Math.max(1, Math.floor(Number(u.atkCdTicks) || 20));
              attackedTarget = true;
            } else {
              attackedTarget = true;
            }
          } else {
            if (shouldUseSimpleContactApproach(u, { kind: "unit" })) {
              moveTowardContact2D(u, healTarget, u.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
            } else {
              const slotPoint = getLaneControlledCombatPocketPoint(lane, u, healTarget, stopDist);
              moveTowardPoint2D(u, slotPoint.x, slotPoint.y, u.baseSpeed || 0.18, -64, 64, -64, 64);
            }
            if (!shouldLaneControlledUnitFreeRoamInCombat(u))
              clampLaneControlledUnitToCombatLeash(u, -64, 64, -64, 64, healTarget);
            u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
            movementAdvanced = true;
          }
        } else {
          clearUnitSupportTarget(u);
        }
      } else if (u.combatTarget && u.combatTarget.unitId) {
        let target = resolveWaveCombatTarget(game, lane, u.combatTarget);
        if (!target) {
          clearUnitCombatTarget(u, game.tick);
        } else if (target.kind === "unit") {
          u.combatState = WAVE_UNIT_STATES.COMBAT;
          u.routeState = WAVE_UNIT_STATES.COMBAT;
          u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
          const t = target.entity;
          u.combatTargetWorldX = Number(t && t.posX);
          u.combatTargetWorldY = Number(t && t.posY);
          const targetLane = Number.isInteger(target.laneIndex)
            ? getLaneByIndex(game, target.laneIndex)
            : resolveRouteUnitLane(game, t, lane);
          const dist = dist2D(u, t);
          const stopDist = getUnitStopDistance(u.type, t.type);
          if (dist <= stopDist + CONTACT_SLOT_TOLERANCE) {
            if (u.atkCd <= 0) {
              const dmg = Math.max(1, Math.floor(Number(u.baseDmg) || Number(def && def.dmg) || 1));
              const cooldownTicks = Math.max(1, Math.floor(Number(u.atkCdTicks) || Number(def && def.atkCdTicks) || 20));
              t.hp = Math.max(0, t.hp - dmg);
              u.attackPulse = (u.attackPulse || 0) + 1;
              if (t.hp <= 0) {
                combatLog.logEvent(game, "route_unit_killed", {
                  unitId: t.id,
                  unitType: t.type,
                  lane: targetLane ? targetLane.laneIndex : null,
                  killedBy: u.id,
                  killedByType: u.type,
                  killedByLane: lane.laneIndex,
                });
                clearUnitCombatTarget(u, game.tick);
              }
              u.atkCd = cooldownTicks;
              attackedTarget = true;
            } else {
              attackedTarget = true;
            }
          } else {
            if (shouldUseSimpleContactApproach(u, target)) {
              moveTowardSimpleContact2D(u, t, u.baseSpeed || 0.18, stopDist, -64, 64, -64, 64);
            } else {
              const slotPoint = getLaneControlledCombatPocketPoint(lane, u, t, stopDist);
              moveTowardPoint2D(u, slotPoint.x, slotPoint.y, u.baseSpeed || 0.18, -64, 64, -64, 64);
            }
            if (!shouldLaneControlledUnitFreeRoamInCombat(u))
              clampLaneControlledUnitToCombatLeash(u, -64, 64, -64, 64, t);
            u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
            movementAdvanced = true;
          }
        } else if (target.kind === "fortress_pad") {
          u.combatState = WAVE_UNIT_STATES.COMBAT;
          u.routeState = WAVE_UNIT_STATES.COMBAT;
          u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
          const dist = Math.sqrt(
            Math.pow(target.posX - u.posX, 2) +
            Math.pow(target.posY - u.posY, 2)
          );
          const stopDist = getUnitStopDistance(u.type, target.type);
          if (dist <= stopDist + CONTACT_SLOT_TOLERANCE) {
            if (u.atkCd <= 0) {
              const result = attackFortressPad(game, lane, u, target);
              if (result.destroyed)
                clearUnitCombatTarget(u, game.tick, { regroupUntilTick: game.tick });
              coreDamageApplied = result.damageApplied > 0;
              attackedTarget = true;
            } else {
              attackedTarget = true;
            }
          } else {
            const slotPoint = getLaneControlledCombatPocketPoint(lane, u, target, stopDist);
            moveTowardPoint2D(u, slotPoint.x, slotPoint.y, u.baseSpeed || 0.18, -64, 64, -64, 64);
            if (!shouldLaneControlledUnitFreeRoamInCombat(u))
              clampLaneControlledUnitToCombatLeash(u, -64, 64, -64, 64, target);
            u.movementMode = UNIT_MOVEMENT_MODES.COMBAT_ENGAGE;
            movementAdvanced = true;
          }
        }
      }

      if (!u.combatTarget && !attackedTarget) {
        u.combatState = WAVE_UNIT_STATES.MOVING;
        u.routeState = WAVE_UNIT_STATES.MOVING;
        if (isLaneControlledUnit(u)) {
          if (startedTickWithCombatTarget || startedTickInCombat || u.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE)
            syncUnitRouteStateToWorldPosition(u);
          if (shouldLaneControlledUnitRouteMarch(u))
            movementAdvanced = advanceLaneControlledUnitAlongRoute(u);
          else
            movementAdvanced = moveLaneControlledUnitToAnchor(u);
          if (!movementAdvanced)
            u.movementMode = shouldLaneControlledUnitRouteMarch(u)
              ? UNIT_MOVEMENT_MODES.LANE_TRAVEL
              : UNIT_MOVEMENT_MODES.FORMATION_JOIN;
        } else {
          u.movementMode = UNIT_MOVEMENT_MODES.LANE_TRAVEL;
          if (!Array.isArray(u.routeSegments) || u.routeSegments.length === 0) {
            if (!u._missingRouteLogged) {
              u._missingRouteLogged = true;
              log.error("[WaveUnitTrace] missing_route_contract", {
                tick: game.tick,
                laneIndex: lane.laneIndex,
                unitId: u.id,
                unitType: u.type,
                spawnSourceType: resolveSpawnSourceTypeFromUnit(u),
                sourceLaneIndex: Number.isInteger(u.sourceLaneIndex) ? u.sourceLaneIndex : -1,
                sourceBarracksKey: u.sourceBarracksKey || u.sourceBarracksId || null,
              });
            }
          } else {
            if (startedTickWithCombatTarget || startedTickInCombat)
              syncUnitRouteStateToWorldPosition(u);
            advanceRouteState(u, u.baseSpeed || 0.18);
            setUnitRouteSnapshotState(u);
            u._missingRouteLogged = false;
            if (ENABLE_WAVE_UNIT_TRACE) {
              log.info("[WaveUnitTrace] movement_progress", {
                tick: game.tick,
                laneIndex: lane.laneIndex,
                unitId: u.id,
                unitType: u.type,
                spawnSourceType: resolveSpawnSourceTypeFromUnit(u),
                pathId: buildRoutePathId(u.routeSegments),
                currentWaypointIndex: Math.max(0, Math.floor(Number(u.routeSegmentIndex) || 0)),
                nextWaypoint: resolveUnitNextWaypoint(u),
                movementState: u.routeState,
                segmentProgress: Number.isFinite(u.segmentProgress) ? Number(u.segmentProgress.toFixed(3)) : null,
                pathIdx: Number.isFinite(u.pathIdx) ? Number(u.pathIdx.toFixed(3)) : null,
                routeWorldX: Number.isFinite(u.routeWorldX) ? Number(u.routeWorldX.toFixed(3)) : null,
                routeWorldY: Number.isFinite(u.routeWorldY) ? Number(u.routeWorldY.toFixed(3)) : null,
              });
            }
          }
          movementAdvanced = true;
        }
      }

      const traceTarget = u.combatTarget ? resolveWaveCombatTarget(game, lane, u.combatTarget) : null;
      u.state = u.combatState || u.routeState || u.state || WAVE_UNIT_STATES.IDLE;
      u.currentTargetId = u.combatTargetId || (u.combatTarget && u.combatTarget.unitId) || null;
      traceWaveUnitTick(game, lane, u, traceTarget, {
        movementAdvanced,
        coreDamageApplied,
        preferredTargetReason: preferredTarget ? preferredTarget.reason : null,
      });
      continue;
    }

    const laneCollisionUnits = lane.units.filter((unit) =>
      unit
      && unit.hp > 0
      && !unit.isDefender
      && (
        isLaneControlledUnit(unit)
        || unit.combatTarget
        || unit.movementMode === UNIT_MOVEMENT_MODES.COMBAT_ENGAGE
        || unit.combatState === WAVE_UNIT_STATES.COMBAT
        || unit.routeState === WAVE_UNIT_STATES.COMBAT
      ));
    if (laneCollisionUnits.length > 1)
      applySeparation2D(game, lane, laneCollisionUnits, MIN_UNIT_SPACING, -64, 64, -64, 64);

    for (const unit of lane.units) {
      applyCanonicalUnitMirrors(game, lane, unit);
    }

    // Keep hot-path movement debug opt-in so local terminals do not stall under log spam.
    if (laneActive && ENABLE_WAVE_UNIT_TRACE && game.tick % 20 === 0 && lane.units.length > 0) {
      const waveUnits = lane.units.filter(u => !u.isDefender && u.hp > 0);
      if (waveUnits.length > 0) {
        const maxIdx = Math.max(...waveUnits.map(u => u.pathIdx));
        const minIdx = Math.min(...waveUnits.map(u => u.pathIdx));
        console.log(`[DEBUG lane${lane.laneIndex} tick${game.tick}] wave units=${waveUnits.length} pathIdx min=${minIdx.toFixed(2)} max=${maxIdx.toFixed(2)} GRID_H=${GRID_H}`);
      }
    }

    // Resolve projectiles
    const killedById = new Set();
    const stillFlying = [];
    for (const p of lane.projectiles) {
      p.ticksRemaining -= 1;
      if (p.ticksRemaining > 0) { stillFlying.push(p); continue; }
      const { dead, hit } = resolveProjectile(game, lane, p);
      for (const id of dead) killedById.add(id);
      if (p.abilities && p.abilities.length > 0 && hit.length > 0) {
        const attacker = { abilities: p.abilities };
        for (const hitId of hit) {
          if (killedById.has(hitId)) continue;
          const hitUnit = lane.units.find(u => u.id === hitId && u.hp > 0);
          if (hitUnit) resolveAbilityHook(game, lane, attacker, "onHit", { target: hitUnit });
        }
      }
    }
    lane.projectiles = stillFlying;

    for (const id of dotDeadIds) killedById.add(id);

    // onDeath hooks + combat log for killed wave units
    for (const u of lane.units) {
      if (u.hp > 0) continue;
      if (u.abilities && u.abilities.length > 0) {
        resolveAbilityHook(game, lane, u, "onDeath", {});
      }
      if (isScheduledWaveUnit(u)) {
        lane.gold += u.bounty || 1;
        combatLog.logEvent(game, 'wave_unit_killed', { unitId: u.id, unitType: u.type, bounty: u.bounty || 1, lane: lane.laneIndex });
      }
    }
    lane.units = lane.units.filter(u => u.hp > 0);
  }

  // Win condition: teamHp elimination
  if (game.phase === "playing") {
    const activeLanes = game.lanes.filter(l => !l.eliminated);
    if (activeLanes.length === 0) {
      if (!game.finalSnapshotCaptured) {
        game.roundSnapshots.push({
          round: game.roundNumber,
          terminal: true,
          elapsedSeconds: Math.floor(game.tick / TICK_HZ),
          lanes: game.lanes.map(l => createRoundSnapshotLane(game, l)),
        });
        game.finalSnapshotCaptured = true;
      }
      game.finalGameOverReason = "all_town_cores_destroyed";
      game.finalGameOverDebug = {
        tick: game.tick,
        coreStates: buildTownCoreStateSummary(game),
      };
      log.warn("[TownCoreTrace] final game over triggered", {
        tick: game.tick,
        reason: game.finalGameOverReason,
        coreStates: game.finalGameOverDebug.coreStates,
      });
      game.phase = "ended";
      game.matchState = "final_game_over";
      game.winner = Number.isInteger(game.officialWinnerLane) ? game.officialWinnerLane : null;
    } else if (game.hasSpawnedWave && isCurrentWaveComplete(game)) {
      startNextWaveNow(game);
    }
  }
}

function createMLSnapshot(game) {
  return buildMLSnapshot(game, {
    WAVE_TIMER_TICKS,
    TEAM_HP_START,
    GRID_W,
    GRID_H,
    SPAWN_X,
    SPAWN_YG,
    SHARED_SUFFIX_LENGTH,
    BARRACKS_SITE_DEFS,
    ensureBarracksSiteStates,
    getBarracksSiteSendIntervalTicks,
    normalizeBarracksSiteId,
    describeBarracksSite,
    isScheduledWaveUnit,
    resolveSpawnSourceTypeFromUnit,
    buildRoutePathId,
    resolveUnitNextWaypoint,
    syncLegacyBarracksAggregate,
    getUnitTilePos,
    createFortressPadSnapshot,
    createBarracksSiteSnapshot,
    createLaneUpcomingWavePreview,
    createLaneUpcomingWaveQueue,
    createBarracksRosterSnapshot,
    createHeroRosterSnapshot,
    resolveLaneAllegianceKey,
    resolveUnitAllegianceKey,
    resolveUnitOwnerLaneIndex,
    resolveUnitTargetLaneIndex,
    resolveUnitPathContractType,
    resolveUnitSourceBarracksId,
    resolveUnitStance,
    LANE_COMMAND_STATES,
    UNIT_MOVEMENT_MODES,
    LANE_FORMATION_ARRIVAL_DEAD_ZONE,
    CONTACT_SLOT_TOLERANCE,
    resolveWaveCombatTarget,
    getWaveUnitTargetDistance,
    getUnitStopDistance,
  });
}

function createMLPublicConfig(options) {
  return buildMLPublicConfig(options, {
    TICK_HZ,
    INCOME_INTERVAL_TICKS,
    GRID_W,
    GRID_H,
    TOWER_MAX_LEVEL,
    TEAM_HP_START,
    BARRACKS_COST_BASE,
    BARRACKS_REQ_INCOME_BASE,
    BARRACKS_ROSTER_REFUND_PCT,
    BARRACKS_SEND_TIMER_TICKS,
    WAVE_TIMER_TICKS,
    ESCALATION_PER_EXTRA_ROUND,
    BASE_COMBAT_PATH_SPEED,
    BARRACKS_LEVEL_ONE_SPEED_MULT,
    SPEED_UPGRADE_STEP,
    FORTRESS_BUILDING_DEFS,
    FORTRESS_PAD_DEFS,
    BARRACKS_SITE_DEFS,
    BARRACKS_ROSTER_DEFS,
    HERO_ROSTER_DEFS,
    MARKET_ROSTER_DEFS,
    normalizeGameOptions,
    getMlRuntimeSettings,
    getAllUnitTypes,
    getMovingUnitDefMap,
    getFixedUnitDefMap,
    getBuildingBranchLabel,
    getFortressMaxTier,
    getFortressBuildCost,
    getBarracksRosterBuyCost,
    getBarracksRosterSellRefund,
    getBuildingDisplayName,
    getBuildingTierDisplayName,
    getBarracksRosterLockedReason,
    getHeroRosterLockedReason,
    getBarracksSiteTierRequirement,
    getBarracksSiteBuildCost,
    getBarracksSiteMaxLevel,
    resolveFortPresentationConfig,
    ROUTE_SEGMENT_POLYLINES,
    ROUTE_GRAPH_NODE_POSITIONS,
    getPadWorldPosition,
    getBarracksSiteWorldPosition,
    getWaveSpawnWorldPosition,
    getLaneNodeId,
    getWaveSpawnNodeId,
    getBarracksRouteStartNodeId,
    getLaneCombatAxes,
    normalizeAllegianceKey,
    FRONT_GATE_COMBAT_OFFSET,
  });
}

/**
 * Returns a key→unitDef map for all sendable (moving/both-mode) units from the DB.
 */
function getMovingUnitDefMap() {
  const map = {};
  for (const ut of getAllUnitTypes()) {
    if (!ut.enabled) continue;
    if (Number(ut.send_cost) <= 0) continue;
    const def = resolveUnitDef(ut.key);
    if (def) map[ut.key] = def;
  }
  return map;
}

/**
 * Returns a key→towerDef map for all placeable (fixed/both-mode) units from the DB.
 */
function getFixedUnitDefMap() {
  const map = {};
  for (const ut of getAllUnitTypes()) {
    if (!ut.enabled) continue;
    if (Number(ut.build_cost) <= 0) continue;
    const def = resolveTowerDef(ut.key);
    if (def) map[ut.key] = def;
  }
  return map;
}

module.exports = {
  TICK_HZ,
  TICK_MS,
  INCOME_INTERVAL_TICKS,
  UNIT_DEFS,
  TOWER_DEFS,
  TEAM_HP_START,
  BARRACKS_SEND_TIMER_TICKS,
  WAVE_TIMER_TICKS,
  BUILD_PHASE_TICKS,
  TRANSITION_PHASE_TICKS,
  getBarracksLevelDef,
  resolveUnitDef,
  getMovingUnitDefMap,
  getFixedUnitDefMap,
  GRID_W,
  GRID_H,
  validateSpawnDefinition,
  createMLGame,
  applyMLAction,
  mlTick,
  startNextWaveNow,
  createMLSnapshot,
  createMLPublicConfig,
  resolveTowerDef,
  getLaneBuildValue,
  getUpcomingWaveNumber,
  countRemainingWaveMobs,
  isCurrentWaveComplete,
  resolveWaveForRound,
  ROUTE_TYPES,
  resolveTargetLaneForBarracksSend,
  buildRouteSegments,
  initializeMovingUnitRouteState,
  getLaneTownCoreCombatTarget,
  getUnitStopDistance,
  LANE_COMMAND_STATES,
  getLaneCommandState,
  createFortressPadSnapshot,
  createBarracksSiteSnapshot,
  createBarracksRosterSnapshot,
  createHeroRosterSnapshot,
  FORTRESS_BUILDING_DEFS,
  FORTRESS_PAD_DEFS,
  BARRACKS_SITE_DEFS,
  BARRACKS_ROSTER_DEFS,
  HERO_ROSTER_DEFS,
};

