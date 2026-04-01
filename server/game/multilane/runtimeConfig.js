"use strict";

const gameConfig = require("../../gameConfig");
const { FRONT_GATE_COMBAT_OFFSET } = require("./fortressSystem");

const TICK_HZ = 20;
const TICK_MS = Math.floor(1000 / TICK_HZ);
const INCOME_INTERVAL_TICKS = 240;
const TOWER_MAX_LEVEL = 10;
const MAX_UNITS_PER_LANE = 200;
const ENABLE_WAVE_UNIT_TRACE = process.env.ENABLE_WAVE_UNIT_TRACE === "1";

const WAVE_UNIT_STATES = Object.freeze({
  IDLE: "IDLE",
  MOVING: "MOVING",
  COMBAT: "COMBAT",
  DEAD: "DEAD",
});

const GRID_W = 11;
const GRID_H = 28;
const SPAWN_X = 5;
const SPAWN_YG = 0;
const CASTLE_X = 5;
const CASTLE_YG = 27;
const SPLASH_RADIUS_TILES = 1.5;
const MIN_UNIT_SPACING = 0.8;

const BASE_COMBAT_PATH_SPEED = 0.25;
const ENGAGEMENT_RANGE_PADDING = 2.0;
const SEP_DAMP = 0.35;
const SEP_MAX_PUSH = 0.16;
const CONTACT_SLOT_TOLERANCE = 0.10;
const USE_PER_UNIT_ANCHOR_SLOTS = true;

const TEAM_HP_START = 20;
const BARRACKS_SEND_TIMER_TICKS = 30 * TICK_HZ;
const WAVE_TIMER_TICKS = 120 * TICK_HZ;
const BUILD_PHASE_TICKS = 600;
const TRANSITION_PHASE_TICKS = 200;
const ESCALATION_PER_EXTRA_ROUND = 0.10;

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

const SHARED_SUFFIX_LENGTH = 28;

const FIXED_SLOT_LAYOUT = [
  { laneIndex: 0, slotKey: "left_a", side: "left", slotColor: "red", branchId: "left_branch_a", branchLabel: "Red Branch", castleSide: "right" },
  { laneIndex: 1, slotKey: "left_b", side: "left", slotColor: "yellow", branchId: "left_branch_b", branchLabel: "Yellow Branch", castleSide: "right" },
  { laneIndex: 2, slotKey: "right_a", side: "right", slotColor: "blue", branchId: "right_branch_a", branchLabel: "Blue Branch", castleSide: "left" },
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
  ANCHOR_JOIN: "AnchorJoin",
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

const OPPOSING_LANE_INDEX = Object.freeze([1, 0, 3, 2]);
const ROUTE_TRACE_MIN_DISTANCE_TO_CORE = 6.5;
const ROUTE_BLOCKING_CORRIDOR_RADIUS = 4.5;
const DEFENDER_ENGAGEMENT_RANGE = ROUTE_BLOCKING_CORRIDOR_RADIUS;
const ROUTE_BLOCKING_FORWARD_DOT = 0.1;
const DEFENDER_HOLD_LEASH = 7.5;
const ROUTE_SLOT_ROW_SPACING = 1.15;
const ROUTE_SLOT_COLUMN_SPACING = 1.15;
const ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY = ROUTE_SLOT_COLUMN_SPACING * 0.7;
const LANE_COMMAND_DEFENSE_RADIUS = ROUTE_BLOCKING_CORRIDOR_RADIUS * 3;
const LANE_COMMAND_COMBAT_LEASH = 8.0;
const LANE_COMMAND_HOME_CONTAINER_PROGRESS = 0.18;
const LANE_ANCHOR_APPROACH_PROGRESS_TOLERANCE = 0.025;
const LANE_ANCHOR_JOIN_DISTANCE = 1.6;
const LANE_ANCHOR_ARRIVAL_DEAD_ZONE = 0.2;
const LANE_ANCHOR_MAX_COLUMNS = 5;
const LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE = 0.9;
const LANE_ANCHOR_SETTLED_SEPARATION_SCALE = 0.25;
const LANE_ANCHOR_MIXED_SEPARATION_SCALE = 0.6;
const LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET = 0.15;
const LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK = 0.12;
const LANE_COMBAT_POCKET_RADIUS_PADDING = ROUTE_SLOT_ROW_SPACING * 2.5;
const LANE_COMBAT_POCKET_RADIUS_SCALE = 0.6;
const LANE_COMBAT_TARGET_LOCK_TICKS = Math.max(1, Math.round(TICK_HZ * 0.5));
const LANE_COMBAT_REGROUP_TICKS = Math.max(1, Math.round(TICK_HZ * 0.35));
const LANE_COMBAT_SWITCH_DISTANCE_MARGIN = 0.75;
const FORTRESS_INTERIOR_ASSAULT_RADIUS =
  FRONT_GATE_COMBAT_OFFSET + ROUTE_BLOCKING_CORRIDOR_RADIUS;
const STRUCTURE_TARGET_VICINITY_PADDING = ROUTE_SLOT_ROW_SPACING * 1.5;
const INSIDE_GATE_ANCHOR_OFFSET = FRONT_GATE_COMBAT_OFFSET - 2.0;
const OUTSIDE_GATE_ANCHOR_OFFSET = FRONT_GATE_COMBAT_OFFSET + 4.0;
const ANCHOR_SUPPORT_TARGET_RADIUS = 7.5;
const WAVE_ROUTE_COMBAT_RECOVERY_TICKS = Math.max(1, Math.round(TICK_HZ * 1.0));

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

module.exports = {
  TICK_HZ,
  TICK_MS,
  INCOME_INTERVAL_TICKS,
  TOWER_MAX_LEVEL,
  MAX_UNITS_PER_LANE,
  ENABLE_WAVE_UNIT_TRACE,
  WAVE_UNIT_STATES,
  GRID_W,
  GRID_H,
  SPAWN_X,
  SPAWN_YG,
  CASTLE_X,
  CASTLE_YG,
  SPLASH_RADIUS_TILES,
  MIN_UNIT_SPACING,
  BASE_COMBAT_PATH_SPEED,
  ENGAGEMENT_RANGE_PADDING,
  SEP_DAMP,
  SEP_MAX_PUSH,
  CONTACT_SLOT_TOLERANCE,
  USE_PER_UNIT_ANCHOR_SLOTS,
  TEAM_HP_START,
  BARRACKS_SEND_TIMER_TICKS,
  WAVE_TIMER_TICKS,
  BUILD_PHASE_TICKS,
  TRANSITION_PHASE_TICKS,
  ESCALATION_PER_EXTRA_ROUND,
  getMlRuntimeSettings,
  SHARED_SUFFIX_LENGTH,
  FIXED_SLOT_LAYOUT,
  BATTLEFIELD_TOPOLOGY,
  SPAWN_SOURCE_TYPES,
  ALLEGIANCE_KEYS,
  PATH_CONTRACT_TYPES,
  UNIT_STANCES,
  LANE_COMMAND_STATES,
  UNIT_MOVEMENT_MODES,
  LANE_STANCE_ANCHOR_KINDS,
  UNIT_COMBAT_ROLES,
  OPPOSING_LANE_INDEX,
  ROUTE_TRACE_MIN_DISTANCE_TO_CORE,
  ROUTE_BLOCKING_CORRIDOR_RADIUS,
  DEFENDER_ENGAGEMENT_RANGE,
  ROUTE_BLOCKING_FORWARD_DOT,
  DEFENDER_HOLD_LEASH,
  ROUTE_SLOT_ROW_SPACING,
  ROUTE_SLOT_COLUMN_SPACING,
  ROUTE_TARGET_PRESSURE_DISTANCE_PENALTY,
  LANE_COMMAND_DEFENSE_RADIUS,
  LANE_COMMAND_COMBAT_LEASH,
  LANE_COMMAND_HOME_CONTAINER_PROGRESS,
  LANE_ANCHOR_APPROACH_PROGRESS_TOLERANCE,
  LANE_ANCHOR_JOIN_DISTANCE,
  LANE_ANCHOR_ARRIVAL_DEAD_ZONE,
  LANE_ANCHOR_MAX_COLUMNS,
  LANE_ANCHOR_SETTLED_SEPARATION_DISTANCE,
  LANE_ANCHOR_SETTLED_SEPARATION_SCALE,
  LANE_ANCHOR_MIXED_SEPARATION_SCALE,
  LANE_ANCHOR_SETTLED_MAX_LATERAL_OFFSET,
  LANE_ANCHOR_SETTLED_SLOT_SPACING_SLACK,
  LANE_COMBAT_POCKET_RADIUS_PADDING,
  LANE_COMBAT_POCKET_RADIUS_SCALE,
  LANE_COMBAT_TARGET_LOCK_TICKS,
  LANE_COMBAT_REGROUP_TICKS,
  LANE_COMBAT_SWITCH_DISTANCE_MARGIN,
  FORTRESS_INTERIOR_ASSAULT_RADIUS,
  STRUCTURE_TARGET_VICINITY_PADDING,
  INSIDE_GATE_ANCHOR_OFFSET,
  OUTSIDE_GATE_ANCHOR_OFFSET,
  ANCHOR_SUPPORT_TARGET_RADIUS,
  WAVE_ROUTE_COMBAT_RECOVERY_TICKS,
  LEGACY_ACTION_REJECTION_REASONS,
  normalizeAllegianceKey,
  normalizeUnitStance,
  areAllegiancesHostile,
  resolveLaneAllegianceKey,
};
