"use strict";

const simMl = require("../sim-multilane");
const { AI_ACTION_TYPE, COUNT_BUCKETS } = require("./types");

const ACTION_TYPES = new Set(Object.values(AI_ACTION_TYPE));
const COUNT_BUCKET_SET = new Set(COUNT_BUCKETS);
const TOWER_MAX_LEVEL = 10;

function makeTileId(x, y) {
  return `${Number(x)},${Number(y)}`;
}

function parseGridId(id) {
  const raw = String(id || "").trim();
  const parts = raw.split(/[:,|]/).map((p) => p.trim());
  if (parts.length !== 2) return null;
  const x = Number(parts[0]);
  const y = Number(parts[1]);
  if (!Number.isInteger(x) || !Number.isInteger(y)) return null;
  return { x, y };
}

function makeTowerId(x, y) {
  return makeTileId(x, y);
}

function clampCountBucket(bucket) {
  if (COUNT_BUCKET_SET.has(bucket)) return bucket;
  let best = COUNT_BUCKETS[0];
  let bestDist = Infinity;
  for (const b of COUNT_BUCKETS) {
    const d = Math.abs(Number(bucket) - b);
    if (d < bestDist) {
      bestDist = d;
      best = b;
    }
  }
  return best;
}

function createDoNothingAction() {
  return { type: AI_ACTION_TYPE.DO_NOTHING };
}

function normalizeAction(action) {
  if (!action || typeof action !== "object") return createDoNothingAction();
  const type = String(action.type || AI_ACTION_TYPE.DO_NOTHING);
  if (!ACTION_TYPES.has(type)) return createDoNothingAction();
  if (type === AI_ACTION_TYPE.DO_NOTHING || type === AI_ACTION_TYPE.UPGRADE_INCOME) {
    return { type };
  }
  if (type === AI_ACTION_TYPE.BUILD_TOWER) {
    return {
      type,
      towerType: String(action.towerType || "").toLowerCase(),
      tileId: String(action.tileId || ""),
    };
  }
  if (type === AI_ACTION_TYPE.UPGRADE_TOWER) {
    return {
      type,
      towerId: String(action.towerId || ""),
    };
  }
  return {
    type,
    unitType: String(action.unitType || "").toLowerCase(),
    laneId: Number.isInteger(action.laneId) ? action.laneId : null,
    countBucket: clampCountBucket(Number(action.countBucket) || 1),
  };
}

function validateActionShape(action, defs) {
  const normalized = normalizeAction(action);
  const unitDefs = (defs && defs.unitDefs) || simMl.getMovingUnitDefMap();

  if (normalized.type === AI_ACTION_TYPE.BUILD_TOWER) {
    const towerResolved = simMl.resolveTowerDef(normalized.towerType);
    if (!towerResolved) {
      return { ok: false, reason: "Unknown tower type", normalized };
    }
    if (!parseGridId(normalized.tileId)) {
      return { ok: false, reason: "Invalid tileId", normalized };
    }
  }
  if (normalized.type === AI_ACTION_TYPE.UPGRADE_TOWER) {
    if (!parseGridId(normalized.towerId)) {
      return { ok: false, reason: "Invalid towerId", normalized };
    }
  }
  if (normalized.type === AI_ACTION_TYPE.SEND_UNITS) {
    if (!unitDefs[normalized.unitType]) {
      return { ok: false, reason: "Unknown unit type", normalized };
    }
    if (!COUNT_BUCKET_SET.has(normalized.countBucket)) {
      return { ok: false, reason: "Invalid countBucket", normalized };
    }
    if (normalized.laneId !== null && !Number.isInteger(normalized.laneId)) {
      return { ok: false, reason: "Invalid laneId", normalized };
    }
  }

  return { ok: true, normalized };
}

function getUnitCostMultiplierForLane(lane) {
  const br = lane && lane.barracks ? lane.barracks : {};
  if (Number.isFinite(br.unitCostMult)) return Math.max(0, Number(br.unitCostMult));
  if (Number.isFinite(br.hpMult)) return Math.max(0, Number(br.hpMult));
  return 1;
}

function getUnitCostForLane(lane, unitType, unitDefs) {
  const def = unitDefs[unitType];
  if (!def) return Infinity;
  return Math.ceil(def.cost * getUnitCostMultiplierForLane(lane));
}

function getTowerUpgradeCost(towerType, nextLevel) {
  const def = simMl.resolveTowerDef(towerType);
  if (!def) return Infinity;
  return Math.ceil(def.cost * (0.75 + 0.25 * nextLevel));
}

function isOpponentLane(game, laneIndex, targetLaneIndex) {
  const src = game && game.lanes && game.lanes[laneIndex];
  const target = game && game.lanes && game.lanes[targetLaneIndex];
  if (!src || !target) return false;
  if (laneIndex === targetLaneIndex) return false;
  if (target.eliminated) return false;
  return src.team !== target.team;
}

function validateActionAgainstGame(game, laneIndex, action, defs) {
  const shaped = validateActionShape(action, defs);
  if (!shaped.ok) return shaped;

  const normalized = shaped.normalized;
  if (!game || game.phase !== "playing") return { ok: false, reason: "Game not active", normalized };
  const lane = game.lanes && game.lanes[laneIndex];
  if (!lane || lane.eliminated) return { ok: false, reason: "Lane inactive", normalized };
  const unitDefs = (defs && defs.unitDefs) || simMl.getMovingUnitDefMap();

  if (normalized.type === AI_ACTION_TYPE.DO_NOTHING) return { ok: true, normalized };

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_INCOME) {
    const currentLevel = Number(lane.barracks && lane.barracks.level) || 1;
    const next = simMl.getBarracksLevelDef(currentLevel + 1);
    if (lane.income < next.reqIncome) {
      return { ok: false, reason: "Income requirement not met", normalized };
    }
    if (lane.gold < next.cost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUILD_TOWER) {
    // In wave defense: direct placement — tile must be empty, cost = towerDef.cost
    if (game.roundState && game.roundState !== "build") {
      return { ok: false, reason: "Can only place units during build phase", normalized };
    }
    const pos = parseGridId(normalized.tileId);
    const tile = lane.grid && lane.grid[pos.x] && lane.grid[pos.x][pos.y];
    if (!tile) return { ok: false, reason: "Tile out of bounds", normalized };
    if (tile.type === "spawn" || tile.type === "castle") {
      return { ok: false, reason: "Cannot build on spawn/castle", normalized };
    }
    if (tile.type !== "empty") return { ok: false, reason: "Tile not empty", normalized };
    const towerResolved = simMl.resolveTowerDef(normalized.towerType);
    const cost = towerResolved ? towerResolved.cost : Infinity;
    if (lane.gold < cost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_TOWER) {
    const pos = parseGridId(normalized.towerId);
    const tile = lane.grid && lane.grid[pos.x] && lane.grid[pos.x][pos.y];
    if (!tile || tile.type !== "tower" || !tile.towerType) {
      return { ok: false, reason: "Tower not found", normalized };
    }
    if (tile.towerLevel >= TOWER_MAX_LEVEL) {
      return { ok: false, reason: "Tower maxed", normalized };
    }
    const cost = getTowerUpgradeCost(tile.towerType, tile.towerLevel + 1);
    if (lane.gold < cost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  const unitCost = getUnitCostForLane(lane, normalized.unitType, unitDefs);
  if (lane.gold < unitCost) return { ok: false, reason: "Not enough gold", normalized };
  if (normalized.laneId !== null && !isOpponentLane(game, laneIndex, normalized.laneId)) {
    return { ok: false, reason: "Invalid target lane", normalized };
  }
  return { ok: true, normalized };
}

function translateActionToCommands(game, laneIndex, action, defs) {
  const checked = validateActionAgainstGame(game, laneIndex, action, defs);
  if (!checked.ok) return { ok: false, reason: checked.reason, commands: [], normalized: checked.normalized };
  const normalized = checked.normalized;
  const lane = game.lanes[laneIndex];

  if (normalized.type === AI_ACTION_TYPE.DO_NOTHING) {
    return { ok: true, commands: [], normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_INCOME) {
    return { ok: true, commands: [{ type: "upgrade_barracks", data: {} }], normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUILD_TOWER) {
    const pos = parseGridId(normalized.tileId);
    return {
      ok: true,
      commands: [{
        type: "place_unit",
        data: { gridX: pos.x, gridY: pos.y, unitTypeKey: normalized.towerType },
      }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_TOWER) {
    const pos = parseGridId(normalized.towerId);
    return {
      ok: true,
      commands: [{ type: "upgrade_tower", data: { gridX: pos.x, gridY: pos.y } }],
      normalized,
    };
  }

  const commands = [];
  for (let i = 0; i < normalized.countBucket; i++) {
    const data = { unitType: normalized.unitType };
    if (Number.isInteger(normalized.laneId)) data.targetLaneIndex = normalized.laneId;
    commands.push({ type: "spawn_unit", data });
  }
  return { ok: true, commands, normalized };
}

module.exports = {
  ACTION_TYPES,
  COUNT_BUCKETS,
  makeTileId,
  parseGridId,
  makeTowerId,
  normalizeAction,
  validateActionShape,
  validateActionAgainstGame,
  translateActionToCommands,
  createDoNothingAction,
  clampCountBucket,
  isOpponentLane,
  getUnitCostForLane,
};

