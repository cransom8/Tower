"use strict";

const simMl = require("../sim-multilane");
const { AI_ACTION_TYPE, COUNT_BUCKETS } = require("./types");

const ACTION_TYPES = new Set(Object.values(AI_ACTION_TYPE));
const COUNT_BUCKET_SET = new Set(COUNT_BUCKETS);
const COMMAND_STATES = simMl.LANE_COMMAND_STATES || Object.freeze({
  ATTACK: "ATTACK",
  DEFEND: "DEFEND",
  RETREAT: "RETREAT",
});

function clampCountBucket(bucket) {
  if (COUNT_BUCKET_SET.has(bucket)) return bucket;
  let best = COUNT_BUCKETS[0];
  let bestDist = Infinity;
  for (const candidate of COUNT_BUCKETS) {
    const d = Math.abs(Number(bucket) - candidate);
    if (d < bestDist) {
      bestDist = d;
      best = candidate;
    }
  }
  return best;
}

function clampProgress(progress) {
  if (!Number.isFinite(progress)) return null;
  return Math.max(0, Math.min(1, Number(progress)));
}

function normalizeCommandState(commandState) {
  const raw = String(commandState || "").trim().toUpperCase();
  switch (raw) {
    case COMMAND_STATES.ATTACK:
      return COMMAND_STATES.ATTACK;
    case COMMAND_STATES.DEFEND:
    case "HOLD":
      return COMMAND_STATES.DEFEND;
    case COMMAND_STATES.RETREAT:
    case "CALLBACK":
      return COMMAND_STATES.RETREAT;
    default:
      return null;
  }
}

function createDoNothingAction() {
  return { type: AI_ACTION_TYPE.DO_NOTHING };
}

function normalizeAction(action) {
  if (!action || typeof action !== "object") return createDoNothingAction();
  const type = String(action.type || AI_ACTION_TYPE.DO_NOTHING);
  if (!ACTION_TYPES.has(type)) return createDoNothingAction();

  if (type === AI_ACTION_TYPE.DO_NOTHING)
    return { type };

  if (type === AI_ACTION_TYPE.BUILD_PAD || type === AI_ACTION_TYPE.UPGRADE_PAD) {
    return {
      type,
      padId: String(action.padId || "").trim(),
    };
  }

  if (type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE || type === AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE) {
    return {
      type,
      barracksId: String(action.barracksId || "").trim().toLowerCase(),
    };
  }

  if (type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT) {
    return {
      type,
      barracksId: String(action.barracksId || "").trim().toLowerCase(),
      rosterKey: String(action.rosterKey || "").trim().toLowerCase(),
      count: clampCountBucket(Math.max(1, Math.floor(Number(action.count) || 1))),
    };
  }

  if (type === AI_ACTION_TYPE.BUY_MARKET_UNIT) {
    return {
      type,
      unitKey: String(action.unitKey || "").trim().toLowerCase(),
      count: clampCountBucket(Math.max(1, Math.floor(Number(action.count) || 1))),
    };
  }

  if (type === AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO) {
    return {
      type,
      barracksId: String(action.barracksId || "").trim().toLowerCase(),
      heroKey: String(action.heroKey || "").trim().toLowerCase(),
    };
  }

  if (type === AI_ACTION_TYPE.SET_LANE_COMMAND) {
    return {
      type,
      commandState: normalizeCommandState(action.commandState),
      targetLaneIndex: Number.isInteger(action.targetLaneIndex) ? action.targetLaneIndex : null,
      progress: clampProgress(action.progress),
    };
  }

  return createDoNothingAction();
}

function validateActionShape(action) {
  const normalized = normalizeAction(action);

  if (normalized.type === AI_ACTION_TYPE.BUILD_PAD || normalized.type === AI_ACTION_TYPE.UPGRADE_PAD) {
    if (!normalized.padId)
      return { ok: false, reason: "Missing padId", normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE || normalized.type === AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE) {
    if (!normalized.barracksId)
      return { ok: false, reason: "Missing barracksId", normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT) {
    if (!normalized.barracksId)
      return { ok: false, reason: "Missing barracksId", normalized };
    if (!normalized.rosterKey)
      return { ok: false, reason: "Missing rosterKey", normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUY_MARKET_UNIT) {
    if (!normalized.unitKey)
      return { ok: false, reason: "Missing unitKey", normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO) {
    if (!normalized.barracksId)
      return { ok: false, reason: "Missing barracksId", normalized };
    if (!normalized.heroKey)
      return { ok: false, reason: "Missing heroKey", normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.SET_LANE_COMMAND) {
    if (!normalized.commandState)
      return { ok: false, reason: "Missing commandState", normalized };
  }

  return { ok: true, normalized };
}

function isHostileTargetLane(game, laneIndex, targetLaneIndex) {
  if (!game || !Array.isArray(game.lanes) || !Number.isInteger(targetLaneIndex))
    return false;
  const sourceLane = game.lanes[laneIndex];
  const targetLane = game.lanes[targetLaneIndex];
  if (!sourceLane || !targetLane)
    return false;
  if (targetLane.eliminated)
    return false;
  if (laneIndex === targetLaneIndex)
    return false;
  return sourceLane.team !== targetLane.team;
}

function getFortressPadSnapshot(game, lane, padId) {
  const pad = lane && Array.isArray(lane.fortressPads)
    ? lane.fortressPads.find((entry) => entry && entry.padId === padId)
    : null;
  return pad ? simMl.createFortressPadSnapshot(game, lane, pad) : null;
}

function getBarracksSiteSnapshot(game, lane, barracksId) {
  return simMl.createBarracksSiteSnapshot(game, lane, barracksId);
}

function getBarracksRosterEntry(siteSnapshot, rosterKey) {
  return siteSnapshot && Array.isArray(siteSnapshot.roster)
    ? siteSnapshot.roster.find((entry) => entry && entry.rosterKey === rosterKey)
    : null;
}

function getMarketRosterEntry(game, lane, unitKey) {
  const entries = typeof simMl.createMarketRosterSnapshot === "function"
    ? simMl.createMarketRosterSnapshot(game, lane)
    : [];
  return Array.isArray(entries)
    ? entries.find((entry) => entry && entry.unitKey === unitKey)
    : null;
}

function getHeroSnapshot(game, lane, heroKey) {
  const heroes = simMl.createHeroRosterSnapshot(game, lane);
  return Array.isArray(heroes)
    ? heroes.find((entry) => entry && entry.heroKey === heroKey)
    : null;
}

function validateActionAgainstGame(game, laneIndex, action) {
  const shaped = validateActionShape(action);
  if (!shaped.ok) return shaped;

  const normalized = shaped.normalized;
  if (!game || game.phase !== "playing") return { ok: false, reason: "Game not active", normalized };
  const lane = game.lanes && game.lanes[laneIndex];
  if (!lane || lane.eliminated) return { ok: false, reason: "Lane inactive", normalized };

  if (normalized.type === AI_ACTION_TYPE.DO_NOTHING)
    return { ok: true, normalized };

  if (normalized.type === AI_ACTION_TYPE.BUILD_PAD) {
    const pad = getFortressPadSnapshot(game, lane, normalized.padId);
    if (!pad) return { ok: false, reason: "Unknown building pad", normalized };
    if (!pad.canBuild) return { ok: false, reason: pad.lockedReason || "Building is not available", normalized };
    if (lane.gold < pad.buildCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_PAD) {
    const pad = getFortressPadSnapshot(game, lane, normalized.padId);
    if (!pad) return { ok: false, reason: "Unknown building pad", normalized };
    if (!pad.canUpgrade) return { ok: false, reason: pad.lockedReason || "Upgrade unavailable", normalized };
    if (lane.gold < pad.upgradeCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE) {
    const site = getBarracksSiteSnapshot(game, lane, normalized.barracksId);
    if (!site) return { ok: false, reason: "Unknown barracks", normalized };
    if (!site.canBuild) return { ok: false, reason: site.lockedReason || "Building is not available", normalized };
    if (lane.gold < site.buildCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE) {
    const site = getBarracksSiteSnapshot(game, lane, normalized.barracksId);
    if (!site) return { ok: false, reason: "Unknown barracks", normalized };
    if (!site.canUpgrade) return { ok: false, reason: site.lockedReason || "Barracks upgrade unavailable", normalized };
    if (lane.gold < site.upgradeCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT) {
    const site = getBarracksSiteSnapshot(game, lane, normalized.barracksId);
    if (!site) return { ok: false, reason: "Unknown barracks", normalized };
    if (!site.isBuilt || site.isDestroyed) return { ok: false, reason: site.lockedReason || "Barracks unavailable", normalized };
    const rosterEntry = getBarracksRosterEntry(site, normalized.rosterKey);
    if (!rosterEntry) return { ok: false, reason: "Unknown barracks unit", normalized };
    if (!rosterEntry.unlocked) return { ok: false, reason: rosterEntry.lockedReason || "Unit is locked", normalized };
    if (!rosterEntry.availableForPurchase)
      return { ok: false, reason: rosterEntry.lockedReason || "Unit is not available for purchase", normalized };
    const totalFoodCost = Math.max(0, Number(rosterEntry.foodCost) || 0) * normalized.count;
    if (Math.max(0, Number(site.foodRemaining) || 0) < totalFoodCost)
      return { ok: false, reason: "Not enough barracks food space", normalized };
    const totalCost = Math.max(0, Number(rosterEntry.buyCost) || 0) * normalized.count;
    if (lane.gold < totalCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUY_MARKET_UNIT) {
    const marketEntry = getMarketRosterEntry(game, lane, normalized.unitKey);
    if (!marketEntry) return { ok: false, reason: "Unknown market unit", normalized };
    if (!marketEntry.unlocked) return { ok: false, reason: marketEntry.lockedReason || "Market unit is locked", normalized };
    if (!marketEntry.availableForPurchase)
      return { ok: false, reason: marketEntry.lockedReason || "Market unit is not available for purchase", normalized };
    const marketPad = getFortressPadSnapshot(game, lane, marketEntry.unlockPadId || "market_pad");
    if (!marketPad || !marketPad.isBuilt) return { ok: false, reason: "Market unavailable", normalized };
    const totalFoodCost = Math.max(0, Number(marketEntry.foodCost) || 0) * normalized.count;
    if (Math.max(0, Number(marketPad.foodRemaining) || 0) < totalFoodCost)
      return { ok: false, reason: "Not enough market food space", normalized };
    const totalCost = Math.max(0, Number(marketEntry.buyCost) || 0) * normalized.count;
    if (lane.gold < totalCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO) {
    const site = getBarracksSiteSnapshot(game, lane, normalized.barracksId);
    if (!site || !site.isBuilt) return { ok: false, reason: "Buy Building first", normalized };
    const hero = getHeroSnapshot(game, lane, normalized.heroKey);
    if (!hero) return { ok: false, reason: "Unknown hero", normalized };
    if (!hero.canSummon) return { ok: false, reason: hero.disabledReason || hero.lockedReason || "Hero unavailable", normalized };
    if (lane.gold < hero.summonCost) return { ok: false, reason: "Not enough gold", normalized };
    return { ok: true, normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.SET_LANE_COMMAND) {
    if (normalized.targetLaneIndex !== null && !isHostileTargetLane(game, laneIndex, normalized.targetLaneIndex))
      return { ok: false, reason: "Invalid target lane", normalized };
    return { ok: true, normalized };
  }

  return { ok: true, normalized };
}

function translateActionToCommands(game, laneIndex, action) {
  const checked = validateActionAgainstGame(game, laneIndex, action);
  if (!checked.ok)
    return { ok: false, reason: checked.reason, commands: [], normalized: checked.normalized };

  const normalized = checked.normalized;
  if (normalized.type === AI_ACTION_TYPE.DO_NOTHING) {
    return { ok: true, commands: [], normalized };
  }

  if (normalized.type === AI_ACTION_TYPE.BUILD_PAD) {
    return {
      ok: true,
      commands: [{ type: "build_on_pad", data: { padId: normalized.padId } }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_PAD) {
    return {
      ok: true,
      commands: [{ type: "upgrade_building", data: { padId: normalized.padId } }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.BUILD_BARRACKS_SITE) {
    return {
      ok: true,
      commands: [{ type: "build_barracks_site", data: { barracksId: normalized.barracksId } }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.UPGRADE_BARRACKS_SITE) {
    return {
      ok: true,
      commands: [{ type: "upgrade_barracks_site", data: { barracksId: normalized.barracksId } }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.BUY_BARRACKS_UNIT) {
    return {
      ok: true,
      commands: [{
        type: "buy_barracks_unit",
        data: {
          barracksId: normalized.barracksId,
          rosterKey: normalized.rosterKey,
          count: normalized.count,
        },
      }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.BUY_MARKET_UNIT) {
    return {
      ok: true,
      commands: [{
        type: "buy_market_unit",
        data: {
          unitKey: normalized.unitKey,
          count: normalized.count,
        },
      }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.DEPLOY_BARRACKS_HERO) {
    return {
      ok: true,
      commands: [{
        type: "deploy_barracks_hero",
        data: {
          barracksId: normalized.barracksId,
          heroKey: normalized.heroKey,
        },
      }],
      normalized,
    };
  }

  if (normalized.type === AI_ACTION_TYPE.SET_LANE_COMMAND) {
    const data = {
      commandState: normalized.commandState,
    };
    if (normalized.targetLaneIndex !== null)
      data.targetLaneIndex = normalized.targetLaneIndex;
    if (normalized.progress !== null)
      data.progress = normalized.progress;
    return {
      ok: true,
      commands: [{ type: "set_lane_command", data }],
      normalized,
    };
  }

  return { ok: true, commands: [], normalized };
}

module.exports = {
  ACTION_TYPES,
  COMMAND_STATES,
  COUNT_BUCKETS,
  normalizeCommandState,
  normalizeAction,
  validateActionShape,
  validateActionAgainstGame,
  translateActionToCommands,
  createDoNothingAction,
  clampCountBucket,
};
