"use strict";

const simMl = require("../sim-multilane");
const { AI_ACTION_TYPE, BOT_DIFFICULTY, DIFFICULTY_KNOBS } = require("./types");
const { createRng, hashSeed } = require("./rng");
const { getPersonalityProfile, choosePersonality } = require("./personalities");
const {
  validateActionAgainstGame,
  createDoNothingAction,
  makeTileId,
  makeTowerId,
  clampCountBucket,
} = require("./actions");
const { buildObservation, estimateLaneThreat, estimateLaneDefense } = require("./observe");
const targeting = require("./targeting");

const SPAWN_X = 5;
const SPAWN_Y = 0;
const CASTLE_X = 5;
const CASTLE_Y = 27;
const GRID_W = simMl.GRID_W;
const GRID_H = simMl.GRID_H;

const TANK_UNITS  = new Set(["ogre","troll","cyclops","hydra","oak_tree_ent","manticora","chimera","ice_golem","demon_lord"]);
const SWARM_UNITS = new Set(["goblin","kobold","giant_rat","ghoul","harpy","fantasy_wolf"]);

function getKnobs(difficulty) {
  return DIFFICULTY_KNOBS[difficulty] || DIFFICULTY_KNOBS.medium;
}

function isInteger(v) {
  return Number.isInteger(v);
}

function getLane(game, laneIndex) {
  return game && game.lanes && game.lanes[laneIndex];
}

function getUnitCostMultiplier(lane) {
  const br = lane && lane.barracks ? lane.barracks : {};
  if (Number.isFinite(br.unitCostMult)) return Math.max(0, Number(br.unitCostMult));
  if (Number.isFinite(br.hpMult)) return Math.max(0, Number(br.hpMult));
  return 1;
}

function getUnitCost(lane, unitType, unitDefs) {
  const defs = unitDefs || simMl.UNIT_DEFS;
  const def = defs[unitType];
  if (!def) return Infinity;
  return Math.ceil(def.cost * getUnitCostMultiplier(lane));
}

function getTowerUpgradeCost(tile) {
  if (!tile || tile.type !== "tower" || !tile.towerType) return Infinity;
  const def = simMl.resolveTowerDef(tile.towerType);
  if (!def) return Infinity;
  const nextLevel = (Number(tile.towerLevel) || 1) + 1;
  return Math.ceil(def.cost * (0.75 + 0.25 * nextLevel));
}

function getBarracksUpgradeCost(lane) {
  const currentLevel = Number(lane && lane.barracks && lane.barracks.level) || 1;
  return simMl.getBarracksLevelDef(currentLevel + 1);
}

function scanUnitMix(units, ownerLane) {
  const mix = { swarm: 0, heavy: 0, magic: 0 };
  for (const u of units || []) {
    if (!u || (u.hp || 0) <= 0) continue;
    if (ownerLane !== null && ownerLane !== undefined && u.ownerLane === ownerLane) continue;
    if (SWARM_UNITS.has(u.type)) mix.swarm += 1;
    if (TANK_UNITS.has(u.type))  mix.heavy += 1;
    const def = simMl.resolveUnitDef(u.type);
    if (def && def.damageType === "MAGIC") mix.magic += 1;
  }
  return mix;
}


function estimateActionCost(game, laneIndex, action, unitDefs) {
  const lane = getLane(game, laneIndex);
  if (!lane) return 0;
  if (!action || action.type === AI_ACTION_TYPE.DO_NOTHING) return 0;
  if (action.type === AI_ACTION_TYPE.UPGRADE_INCOME) {
    return getBarracksUpgradeCost(lane).cost;
  }
  if (action.type === AI_ACTION_TYPE.BUILD_TOWER) {
    const def = simMl.resolveTowerDef(action.towerType);
    return def ? def.cost : 0;
  }
  if (action.type === AI_ACTION_TYPE.UPGRADE_TOWER) {
    const parsed = String(action.towerId || "").split(",");
    if (parsed.length !== 2) return 0;
    const x = Number(parsed[0]);
    const y = Number(parsed[1]);
    const tile = lane.grid[x] && lane.grid[x][y];
    return getTowerUpgradeCost(tile);
  }
  if (action.type === AI_ACTION_TYPE.SEND_UNITS) {
    const c = clampCountBucket(Number(action.countBucket) || 1);
    return getUnitCost(lane, action.unitType, unitDefs) * c;
  }
  return 0;
}

class BotBrain {
  constructor(config) {
    const cfg = config && typeof config === "object" ? config : {};
    this.laneIndex = Number(cfg.laneIndex) || 0;
    this.difficulty = String(cfg.difficulty || BOT_DIFFICULTY.MEDIUM).toLowerCase();
    if (!getKnobs(this.difficulty)) this.difficulty = BOT_DIFFICULTY.MEDIUM;

    const seedBase = `${cfg.seed || "bot"}:${this.laneIndex}:${this.difficulty}`;
    this.seed = hashSeed(seedBase);
    this.rng = createRng(this.seed);

    this.tickMs = Math.max(1, Number(cfg.tickMs) || 50);
    this.knobs = getKnobs(this.difficulty);
    this.reactionDelayMs = this.rng.nextInt(this.knobs.reactionDelayMinMs, this.knobs.reactionDelayMaxMs);
    this.reactionDelayTicks = Math.max(1, Math.round(this.reactionDelayMs / this.tickMs));
    this.mistakeRate = Number(this.knobs.mistakeRate) || 0;
    this.planningDepth = Number(this.knobs.planningDepth) || 1;

    this.personality = choosePersonality(this.rng, cfg.personality);
    this.personalityProfile = getPersonalityProfile(this.personality);

    this.unitDefs = (cfg.unitDefMap && Object.keys(cfg.unitDefMap).length > 0)
      ? cfg.unitDefMap
      : simMl.getMovingUnitDefMap();
    this.unitTypes = Object.freeze(Object.keys(this.unitDefs));

    this.memory = {
      nextThinkTick: 0,
      invalidStreak: 0,
      lastActionTick: -1,
      currentTargetLaneIndex: null,
      targetHoldUntilTick: 0,
      tankPressureBias: 0,
      laneSwitches: 0,
      lastTargetLaneIndex: null,
      lastTargetSwitchTick: -1,
      lastSendByTarget: {},
      observationDim: 0,
    };
  }

  updateMemory(game, runtime) {
    const lane = getLane(game, this.laneIndex);
    if (!lane) return;

    // Adaptive memory: when target leaks shortly after tank sends, increase tank preference.
    const currentTarget = this.memory.currentTargetLaneIndex;
    const recentLifeLoss = runtime && runtime.recentLifeLossByLane && Number(runtime.recentLifeLossByLane[currentTarget]) || 0;
    const lastSend = runtime && runtime.lastSendBySourceLane && runtime.lastSendBySourceLane[this.laneIndex];
    if (lastSend && Number.isInteger(currentTarget) && lastSend.targetLaneIndex === currentTarget) {
      this.memory.lastSendByTarget[currentTarget] = lastSend;
      if (recentLifeLoss > 0 && TANK_UNITS.has(lastSend.unitType)) {
        this.memory.tankPressureBias = Math.min(2.5, this.memory.tankPressureBias + recentLifeLoss * 0.35);
      }
    }
    this.memory.tankPressureBias = Math.max(0, this.memory.tankPressureBias - 0.02);

    const obs = buildObservation(game, this.laneIndex, runtime, this.unitDefs);
    this.memory.observationDim = obs.vector.length;
  }

  chooseTowerType(game, lane, targetLaneIndex, danger) {
    const loadoutKeys = lane.autosend && lane.autosend.loadoutKeys;
    const pool = loadoutKeys && loadoutKeys.length > 0 ? loadoutKeys : null;

    const pick = (type) => {
      if (pool) return pool.includes(type) ? type : null;
      return simMl.resolveTowerDef(type) ? type : null;
    };

    const incomingMix = scanUnitMix(lane.units, lane.laneIndex);
    if (danger && incomingMix.swarm >= 4) { const t = pick("mountain_dragon"); if (t) return t; }
    if (danger && incomingMix.heavy >= 3) { const t = pick("giant_viper");     if (t) return t; }

    const targetLane = getLane(game, targetLaneIndex);
    const targetMix = scanUnitMix(targetLane ? targetLane.units : [], targetLaneIndex);
    if (targetMix.magic >= 3) { const t = pick("evil_watcher"); if (t) return t; }

    if (this.memory.tankPressureBias > 1.0) { const t = pick("ballista"); if (t) return t; }

    for (const t of this.personalityProfile.preferredTowers) {
      const chosen = pick(t);
      if (chosen) return chosen;
    }

    // Fall back to first available loadout key or default
    if (pool) return pool[0];
    return "archer";
  }

  findBuildTowerAction(game, lane, targetLaneIndex, danger) {
    if (game.roundState && game.roundState !== "build") return null;
    const towerType = this.chooseTowerType(game, lane, targetLaneIndex, danger);
    const candidates = [];
    for (let x = 0; x < GRID_W; x++) {
      for (let y = 1; y < GRID_H - 1; y++) {
        if (x === SPAWN_X && y === SPAWN_Y) continue;
        if (x === CASTLE_X && y === CASTLE_Y) continue;
        const tile = lane.grid[x][y];
        if (!tile || tile.type !== "empty") continue;
        const centerBias = 1 - Math.abs(x - CASTLE_X) / CASTLE_X;
        let score = danger ? y : Math.abs(14 - y);
        score += centerBias * 0.6;
        candidates.push({ x, y, score });
      }
    }
    candidates.sort((a, b) => b.score - a.score);
    for (const c of candidates) {
      const action = { type: AI_ACTION_TYPE.BUILD_TOWER, towerType, tileId: makeTileId(c.x, c.y) };
      const legal = validateActionAgainstGame(game, this.laneIndex, action, { unitDefs: this.unitDefs });
      if (legal.ok) return legal.normalized;
    }
    return null;
  }

  findUpgradeTowerAction(game, lane, danger) {
    const candidates = [];
    for (let x = 0; x < GRID_W; x++) {
      for (let y = 0; y < GRID_H; y++) {
        const tile = lane.grid[x][y];
        if (!tile || tile.type !== "tower" || !tile.towerType) continue;
        if ((tile.towerLevel || 1) >= 10) continue;
        const cost = getTowerUpgradeCost(tile);
        if (!Number.isFinite(cost) || lane.gold < cost) continue;
        const nearCastle = y / (GRID_H - 1);
        const base = simMl.resolveTowerDef(tile.towerType);
        const dps = base ? base.dmg / Math.max(1, base.atkCdTicks) : 0.1;
        const levelBonus = 1 / Math.max(1, tile.towerLevel || 1);
        let score = dps * 2 + levelBonus;
        if (danger) score += nearCastle * 1.8;
        score -= cost * 0.015;
        candidates.push({ x, y, score });
      }
    }
    candidates.sort((a, b) => b.score - a.score);
    for (const c of candidates) {
      const action = { type: AI_ACTION_TYPE.UPGRADE_TOWER, towerId: makeTowerId(c.x, c.y) };
      const legal = validateActionAgainstGame(game, this.laneIndex, action, { unitDefs: this.unitDefs });
      if (legal.ok) return legal.normalized;
    }
    return null;
  }

  findEcoAction(game, lane, danger) {
    if (danger) return null;
    const next = getBarracksUpgradeCost(lane);
    if (lane.income < next.reqIncome) return null;
    if (lane.gold < next.cost) return null;
    const action = { type: AI_ACTION_TYPE.UPGRADE_INCOME };
    const legal = validateActionAgainstGame(game, this.laneIndex, action, { unitDefs: this.unitDefs });
    return legal.ok ? legal.normalized : null;
  }

  chooseSendUnitType(game, targetLaneIndex, danger) {
    const targetLane = getLane(game, targetLaneIndex);
    const unitMix = scanUnitMix(targetLane ? targetLane.units : [], targetLaneIndex);
    const preference = this.personalityProfile.preferredUnits.slice();

    if (this.memory.tankPressureBias > 0.8) {
      preference.unshift("ogre", "troll");
    }
    if (danger) {
      preference.unshift("goblin");
    }
    if (unitMix.swarm >= 6) {
      preference.unshift("mountain_dragon");
    }

    const dedup = [];
    for (const u of preference) {
      if (this.unitTypes.includes(u) && !dedup.includes(u)) dedup.push(u);
    }
    for (const fallback of this.unitTypes) {
      if (!dedup.includes(fallback)) dedup.push(fallback);
    }
    return dedup[0] || "goblin";
  }

  chooseSendBucket(lane, unitType, reserveGold, spikePlan) {
    const unitCost = getUnitCost(lane, unitType, this.unitDefs);
    if (!Number.isFinite(unitCost) || unitCost <= 0) return null;
    const maxAffordable = Math.floor(Math.max(0, lane.gold - reserveGold) / unitCost);
    if (maxAffordable <= 0) return null;

    if (spikePlan && Number.isFinite(spikePlan.countBucket)) {
      const spikeBucket = clampCountBucket(spikePlan.countBucket);
      if (spikeBucket <= maxAffordable) return spikeBucket;
    }

    for (const b of this.personalityProfile.sendBucketBias) {
      if (b <= maxAffordable) return b;
    }
    if (maxAffordable >= 10) return 10;
    if (maxAffordable >= 5) return 5;
    if (maxAffordable >= 3) return 3;
    return 1;
  }

  findSendAction(game, lane, targetLaneIndex, danger, spikePlan) {
    if (!isInteger(targetLaneIndex)) return null;
    const unitType = this.chooseSendUnitType(game, targetLaneIndex, danger);
    const reserveGold = danger ? 26 : 10;
    const countBucket = this.chooseSendBucket(lane, unitType, reserveGold, spikePlan);
    if (!countBucket) return null;
    const action = {
      type: AI_ACTION_TYPE.SEND_UNITS,
      unitType,
      laneId: targetLaneIndex,
      countBucket,
    };
    const legal = validateActionAgainstGame(game, this.laneIndex, action, { unitDefs: this.unitDefs });
    return legal.ok ? legal.normalized : null;
  }

  applyGuardrails(game, lane, action, danger, fallbackDefenseAction) {
    if (!action || action.type === AI_ACTION_TYPE.DO_NOTHING) return action;
    const spend = estimateActionCost(game, this.laneIndex, action, this.unitDefs);
    const postGold = lane.gold - spend;
    const safetyReserve = danger ? 20 : 6;

    if (postGold < safetyReserve && action.type === AI_ACTION_TYPE.SEND_UNITS) {
      return fallbackDefenseAction || createDoNothingAction();
    }
    if (postGold < 0) return createDoNothingAction();
    return action;
  }

  chooseTarget(game, runtime) {
    const target = targeting.chooseTargetOpponent(game, this.laneIndex, runtime, this.memory, this.rng);
    if (target !== this.memory.lastTargetLaneIndex) {
      this.memory.laneSwitches += 1;
      this.memory.lastTargetLaneIndex = target;
      this.memory.lastTargetSwitchTick = Number(game.tick) || 0;
    }
    if (runtime && runtime.currentTargetByLane) {
      runtime.currentTargetByLane[this.laneIndex] = target;
    }
    return target;
  }

  rankCandidates(candidates) {
    const scored = candidates.filter(Boolean);
    scored.sort((a, b) => b.score - a.score);
    return scored;
  }

  maybeInjectMistake(sortedCandidates) {
    if (sortedCandidates.length <= 1) return sortedCandidates[0];
    if (this.rng.next() >= this.mistakeRate) return sortedCandidates[0];
    const choices = sortedCandidates.slice(1, Math.min(3, sortedCandidates.length));
    if (choices.length === 0) return sortedCandidates[0];
    return choices[this.rng.nextInt(0, choices.length - 1)];
  }

  tick(context) {
    const game = context && context.game;
    const runtime = (context && context.runtime) || {};
    const lane = getLane(game, this.laneIndex);
    if (!lane || lane.eliminated || !game || game.phase !== "playing") return createDoNothingAction();

    this.updateMemory(game, runtime);
    const currentTick = Number(game.tick) || 0;
    if (currentTick < this.memory.nextThinkTick) return createDoNothingAction();
    this.memory.nextThinkTick = currentTick + this.reactionDelayTicks;

    const targetLaneIndex = this.chooseTarget(game, runtime);
    let spikePlan = null;
    if (targetLaneIndex !== null) {
      targeting.planTeamSpike(game, this.laneIndex, runtime, this.rng, targetLaneIndex);
      spikePlan = targeting.shouldSyncSpikeNow(game, this.laneIndex, runtime, 4);
    }

    // Defensive and pressure context from actual lane state (no hidden info).
    const threat = estimateLaneThreat(lane, this.unitDefs);
    const defense = estimateLaneDefense(lane);
    const danger = lane.lives <= 8 || threat > Math.max(2, defense * 1.08);
    const overDefendedTarget = isInteger(targetLaneIndex) && targeting.isLaneOverDefended(game, targetLaneIndex);
    if (overDefendedTarget && (this.personality === "PRESSURE" || this.personality === "ADAPTIVE")) {
      // Allow quick pivot in FFA when one lane is clearly overdefended.
      this.memory.targetHoldUntilTick = currentTick;
    }

    const buildAction = this.findBuildTowerAction(game, lane, targetLaneIndex, danger);
    const upgradeAction = this.findUpgradeTowerAction(game, lane, danger);
    const defenseAction = buildAction || upgradeAction;
    const ecoAction = this.findEcoAction(game, lane, danger);
    const sendAction = this.findSendAction(game, lane, targetLaneIndex, danger, spikePlan);
    const doNothing = createDoNothingAction();

    const obs = buildObservation(game, this.laneIndex, runtime, this.unitDefs);
    const leakPressure = obs.named ? obs.named.myLeaksLast3Waves : 0;

    const candidates = [];
    if (defenseAction) {
      candidates.push({
        action: defenseAction,
        score: this.personalityProfile.defenseWeight * (1.0 + (danger ? 1.2 : 0.3) + leakPressure * 0.8),
      });
    }
    if (ecoAction) {
      const ecoNeed = lane.income < 40 ? 1.0 : lane.income < 120 ? 0.7 : 0.3;
      candidates.push({
        action: ecoAction,
        score: this.personalityProfile.ecoWeight * ecoNeed * (danger ? 0.25 : 1.0),
      });
    }
    if (sendAction) {
      const pressureBase = this.personalityProfile.pressureWeight * (danger ? 0.4 : 1.0);
      const spikeBonus = spikePlan ? 0.7 : 0;
      const depthBonus = 0.05 * this.planningDepth;
      candidates.push({
        action: sendAction,
        score: pressureBase + spikeBonus + depthBonus,
      });
    }
    candidates.push({
      action: doNothing,
      score: 0.05 + (danger ? -0.1 : 0.06),
    });

    const sorted = this.rankCandidates(candidates);
    let selected = this.maybeInjectMistake(sorted) || { action: doNothing };
    let action = selected.action || doNothing;
    action = this.applyGuardrails(game, lane, action, danger, defenseAction);

    const legal = validateActionAgainstGame(game, this.laneIndex, action, { unitDefs: this.unitDefs });
    if (!legal.ok) {
      this.memory.invalidStreak += 1;
      if (this.memory.invalidStreak >= 2 && defenseAction) {
        const fallbackLegal = validateActionAgainstGame(game, this.laneIndex, defenseAction, { unitDefs: this.unitDefs });
        if (fallbackLegal.ok) {
          this.memory.invalidStreak = 0;
          this.memory.lastActionTick = currentTick;
          return fallbackLegal.normalized;
        }
      }
      return createDoNothingAction();
    }

    this.memory.invalidStreak = 0;
    this.memory.lastActionTick = currentTick;
    return legal.normalized;
  }
}

module.exports = {
  BotBrain,
};

