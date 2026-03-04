"use strict";

const simMl = require("../server/sim-multilane");
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
const WALL_COST = 2;
const UNIT_TYPES = Object.keys(simMl.UNIT_DEFS);

const TANK_UNITS = new Set(["ironclad", "golem"]);
const SWARM_UNITS = new Set(["runner", "footman"]);

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

function getUnitCost(lane, unitType) {
  const def = simMl.UNIT_DEFS[unitType];
  if (!def) return Infinity;
  return Math.ceil(def.cost * getUnitCostMultiplier(lane));
}

function getTowerUpgradeCost(tile) {
  if (!tile || tile.type !== "tower" || !tile.towerType) return Infinity;
  const def = simMl.TOWER_DEFS[tile.towerType];
  if (!def) return Infinity;
  const nextLevel = (Number(tile.towerLevel) || 1) + 1;
  return Math.ceil(def.cost * (0.75 + 0.25 * nextLevel));
}

function getBarracksUpgradeCost(lane) {
  const currentLevel = Number(lane && lane.barracks && lane.barracks.level) || 1;
  return simMl.getBarracksLevelDef(currentLevel + 1);
}

function scanUnitMix(units, ownerLane) {
  const mix = { runner: 0, footman: 0, ironclad: 0, warlock: 0, golem: 0 };
  for (const u of units || []) {
    if (!u || (u.hp || 0) <= 0) continue;
    if (ownerLane !== null && ownerLane !== undefined && u.ownerLane === ownerLane) continue;
    if (mix[u.type] !== undefined) mix[u.type] += 1;
  }
  return mix;
}

function hasPathWithCandidateWall(lane, wallX, wallY) {
  if (!lane || !lane.grid) return false;
  if (wallX === SPAWN_X && wallY === SPAWN_Y) return false;
  if (wallX === CASTLE_X && wallY === CASTLE_Y) return false;
  const tile = lane.grid[wallX] && lane.grid[wallX][wallY];
  if (!tile || tile.type !== "empty") return false;

  const visited = Array.from({ length: GRID_W }, () => new Array(GRID_H).fill(false));
  const q = [[SPAWN_X, SPAWN_Y]];
  visited[SPAWN_X][SPAWN_Y] = true;
  const dirs = [[1, 0], [-1, 0], [0, 1], [0, -1]];
  let head = 0;

  while (head < q.length) {
    const [x, y] = q[head++];
    if (x === CASTLE_X && y === CASTLE_Y) return true;
    for (const [dx, dy] of dirs) {
      const nx = x + dx;
      const ny = y + dy;
      if (nx < 0 || nx >= GRID_W || ny < 0 || ny >= GRID_H) continue;
      if (visited[nx][ny]) continue;
      if (nx === wallX && ny === wallY) continue;
      const nt = lane.grid[nx] && lane.grid[nx][ny];
      if (!nt) continue;
      if (nt.type === "wall" || nt.type === "tower") continue;
      visited[nx][ny] = true;
      q.push([nx, ny]);
    }
  }
  return false;
}

function getPathLengthWithCandidateWall(lane, wallX, wallY) {
  if (!lane || !lane.grid) return null;
  if (wallX === SPAWN_X && wallY === SPAWN_Y) return null;
  if (wallX === CASTLE_X && wallY === CASTLE_Y) return null;
  const tile = lane.grid[wallX] && lane.grid[wallX][wallY];
  if (!tile || tile.type !== "empty") return null;

  const visited = Array.from({ length: GRID_W }, () => new Array(GRID_H).fill(false));
  const dist = Array.from({ length: GRID_W }, () => new Array(GRID_H).fill(-1));
  const q = [[SPAWN_X, SPAWN_Y]];
  visited[SPAWN_X][SPAWN_Y] = true;
  dist[SPAWN_X][SPAWN_Y] = 0;
  const dirs = [[1, 0], [-1, 0], [0, 1], [0, -1]];
  let head = 0;

  while (head < q.length) {
    const [x, y] = q[head++];
    if (x === CASTLE_X && y === CASTLE_Y) return dist[x][y] + 1;
    for (const [dx, dy] of dirs) {
      const nx = x + dx;
      const ny = y + dy;
      if (nx < 0 || nx >= GRID_W || ny < 0 || ny >= GRID_H) continue;
      if (visited[nx][ny]) continue;
      if (nx === wallX && ny === wallY) continue;
      const nt = lane.grid[nx] && lane.grid[nx][ny];
      if (!nt) continue;
      if (nt.type === "wall" || nt.type === "tower") continue;
      visited[nx][ny] = true;
      dist[nx][ny] = dist[x][y] + 1;
      q.push([nx, ny]);
    }
  }
  return null;
}

function getSerpentineConfig(difficulty) {
  if (difficulty === BOT_DIFFICULTY.HARD) {
    return { startY: 4, rowSpacing: 3, maxRows: 8 };
  }
  if (difficulty === BOT_DIFFICULTY.MEDIUM) {
    return { startY: 5, rowSpacing: 4, maxRows: 5 };
  }
  return null;
}

function getSerpentineRows(difficulty) {
  const cfg = getSerpentineConfig(difficulty);
  if (!cfg) return [];
  const rows = [];
  for (let y = cfg.startY; y < GRID_H - 3; y += cfg.rowSpacing) {
    rows.push(y);
    if (rows.length >= cfg.maxRows) break;
  }
  return rows;
}

function getSerpentineGapX(rowIdx, laneIndex) {
  const leftGap = 1;
  const rightGap = GRID_W - 2;
  return ((rowIdx + laneIndex) % 2 === 0) ? leftGap : rightGap;
}

function getSerpentineRowIndex(difficulty, y) {
  const rows = getSerpentineRows(difficulty);
  return rows.indexOf(y);
}

function isSerpentineWallCell(difficulty, laneIndex, x, y) {
  const rowIdx = getSerpentineRowIndex(difficulty, y);
  if (rowIdx < 0) return false;
  const gapX = getSerpentineGapX(rowIdx, laneIndex);
  return x !== gapX;
}

function isSerpentineAnchorCell(difficulty, laneIndex, x, y) {
  const rowIdx = getSerpentineRowIndex(difficulty, y);
  if (rowIdx < 0) return false;
  const gapX = getSerpentineGapX(rowIdx, laneIndex);
  return x === Math.max(0, gapX - 1) || x === Math.min(GRID_W - 1, gapX + 1);
}

function estimateActionCost(game, laneIndex, action) {
  const lane = getLane(game, laneIndex);
  if (!lane) return 0;
  if (!action || action.type === AI_ACTION_TYPE.DO_NOTHING) return 0;
  if (action.type === AI_ACTION_TYPE.UPGRADE_INCOME) {
    return getBarracksUpgradeCost(lane).cost;
  }
  if (action.type === AI_ACTION_TYPE.BUILD_TOWER) {
    const parsed = String(action.tileId || "").split(",");
    if (parsed.length !== 2) return 0;
    const x = Number(parsed[0]);
    const y = Number(parsed[1]);
    const tile = lane.grid[x] && lane.grid[x][y];
    if (!tile) return 0;
    if (tile.type === "empty") return WALL_COST;
    if (tile.type === "wall") {
      const def = simMl.TOWER_DEFS[action.towerType];
      return def ? def.cost : 0;
    }
    return 0;
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
    return getUnitCost(lane, action.unitType) * c;
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
      nextPressureTick: 0,
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

    const obs = buildObservation(game, this.laneIndex, runtime);
    this.memory.observationDim = obs.vector.length;
  }

  chooseTowerType(game, lane, targetLaneIndex, danger) {
    const incomingMix = scanUnitMix(lane.units, lane.laneIndex);
    const swarmCount = incomingMix.runner + incomingMix.footman;
    const heavyCount = incomingMix.ironclad + incomingMix.golem;
    if (danger && swarmCount >= 4) return "cannon";
    if (danger && heavyCount >= 3) return "ballista";

    const targetLane = getLane(game, targetLaneIndex);
    const targetMix = scanUnitMix(targetLane ? targetLane.units : [], targetLaneIndex);
    if (targetMix.warlock >= 3) return "mage";

    // Adaptive: if tanks are repeatedly leaking, prefer anti-heavy and heavy sends.
    if (this.memory.tankPressureBias > 1.0) return "ballista";

    for (const t of this.personalityProfile.preferredTowers) {
      if (simMl.TOWER_DEFS[t]) return t;
    }
    return "archer";
  }

  findBuildTowerAction(game, lane, targetLaneIndex, danger) {
    const towerType = this.chooseTowerType(game, lane, targetLaneIndex, danger);
    const basePathLen = (lane.path && lane.path.length) || 0;

    // First convert a valid existing wall.
    const wallCandidates = [];
    for (let x = 0; x < GRID_W; x++) {
      for (let y = 1; y < GRID_H - 1; y++) {
        const tile = lane.grid[x][y];
        if (tile.type !== "wall") continue;
        if (isSerpentineWallCell(this.difficulty, this.laneIndex, x, y) &&
            !isSerpentineAnchorCell(this.difficulty, this.laneIndex, x, y)) {
          // Keep maze band walls intact; only convert near turning gaps.
          continue;
        }
        let score = danger ? y : Math.abs(14 - y);
        if (x === 0 || x === GRID_W - 1) score += 0.5;
        wallCandidates.push({ x, y, score });
      }
    }
    wallCandidates.sort((a, b) => b.score - a.score);
    for (const c of wallCandidates) {
      const action = { type: AI_ACTION_TYPE.BUILD_TOWER, towerType, tileId: makeTileId(c.x, c.y) };
      const legal = validateActionAgainstGame(game, this.laneIndex, action);
      if (legal.ok) return legal.normalized;
    }

    // Else place a wall at a path-safe tile (conversion will happen on a future tick).
    const serpRows = getSerpentineRows(this.difficulty);
    if (serpRows.length > 0) {
      let bestSerp = null;
      for (let rowIdx = 0; rowIdx < serpRows.length; rowIdx++) {
        const y = serpRows[rowIdx];
        const gapX = getSerpentineGapX(rowIdx, this.laneIndex);
        for (let x = 0; x < GRID_W; x++) {
          if (x === gapX) continue;
          if ((x === SPAWN_X && y === SPAWN_Y) || (x === CASTLE_X && y === CASTLE_Y)) continue;
          const tile = lane.grid[x] && lane.grid[x][y];
          if (!tile || tile.type !== "empty") continue;
          if (!hasPathWithCandidateWall(lane, x, y)) continue;
          const candidatePathLen = getPathLengthWithCandidateWall(lane, x, y);
          const pathGain = (candidatePathLen && basePathLen) ? Math.max(0, candidatePathLen - basePathLen) : 0;
          const nearGap = Math.min(Math.abs(x - (gapX - 1)), Math.abs(x - (gapX + 1))) <= 1;
          let score = 100 + rowIdx * 4 + pathGain * 2.0;
          if (nearGap) score += 1.2;
          if (!bestSerp || score > bestSerp.score) bestSerp = { x, y, score };
        }
      }
      if (bestSerp) {
        const action = { type: AI_ACTION_TYPE.BUILD_TOWER, towerType, tileId: makeTileId(bestSerp.x, bestSerp.y) };
        const legal = validateActionAgainstGame(game, this.laneIndex, action);
        if (legal.ok) return legal.normalized;
      }
    }

    const candidates = [];
    for (let x = 0; x < GRID_W; x++) {
      for (let y = 1; y < GRID_H - 1; y++) {
        if ((x === SPAWN_X && y === SPAWN_Y) || (x === CASTLE_X && y === CASTLE_Y)) continue;
        const tile = lane.grid[x][y];
        if (!tile || tile.type !== "empty") continue;
        if (!hasPathWithCandidateWall(lane, x, y)) continue;
        const candidatePathLen = getPathLengthWithCandidateWall(lane, x, y);
        const pathGain = (candidatePathLen && basePathLen) ? Math.max(0, candidatePathLen - basePathLen) : 0;

        const castleDist = Math.abs(CASTLE_Y - y);
        const centerBias = 1 - Math.abs(x - CASTLE_X) / CASTLE_X;
        let score = danger ? (GRID_H - castleDist) : (14 - Math.abs(14 - y));
        score += centerBias * 0.6;
        score += pathGain * (danger ? 0.8 : 1.5);
        candidates.push({ x, y, score });
      }
    }
    candidates.sort((a, b) => b.score - a.score);
    for (const c of candidates) {
      const action = { type: AI_ACTION_TYPE.BUILD_TOWER, towerType, tileId: makeTileId(c.x, c.y) };
      const legal = validateActionAgainstGame(game, this.laneIndex, action);
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
        const base = simMl.TOWER_DEFS[tile.towerType];
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
      const legal = validateActionAgainstGame(game, this.laneIndex, action);
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
    const legal = validateActionAgainstGame(game, this.laneIndex, action);
    return legal.ok ? legal.normalized : null;
  }

  chooseSendUnitType(game, targetLaneIndex, danger) {
    const targetLane = getLane(game, targetLaneIndex);
    const unitMix = scanUnitMix(targetLane ? targetLane.units : [], targetLaneIndex);
    const preference = this.personalityProfile.preferredUnits.slice();

    if (this.memory.tankPressureBias > 0.8) {
      preference.unshift("golem", "ironclad");
    }
    if (danger) {
      preference.unshift("footman");
    }
    if ((unitMix.runner + unitMix.footman) >= 6) {
      preference.unshift("cannon");
    }

    const dedup = [];
    for (const u of preference) {
      if (UNIT_TYPES.includes(u) && !dedup.includes(u)) dedup.push(u);
    }
    for (const fallback of UNIT_TYPES) {
      if (!dedup.includes(fallback)) dedup.push(fallback);
    }
    return dedup[0] || "footman";
  }

  chooseSendBucket(lane, unitType, reserveGold, spikePlan) {
    const unitCost = getUnitCost(lane, unitType);
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
    const reserveGold = danger ? 18 : 6;
    const countBucket = this.chooseSendBucket(lane, unitType, reserveGold, spikePlan);
    if (!countBucket) return null;
    const action = {
      type: AI_ACTION_TYPE.SEND_UNITS,
      unitType,
      laneId: targetLaneIndex,
      countBucket,
    };
    const legal = validateActionAgainstGame(game, this.laneIndex, action);
    return legal.ok ? legal.normalized : null;
  }

  applyGuardrails(game, lane, action, danger, fallbackDefenseAction) {
    if (!action || action.type === AI_ACTION_TYPE.DO_NOTHING) return action;
    const spend = estimateActionCost(game, this.laneIndex, action);
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
    const threat = estimateLaneThreat(lane);
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

    const obs = buildObservation(game, this.laneIndex, runtime);
    const leakPressure = obs.named ? obs.named.myLeaksLast3Waves : 0;
    const forcePressure = currentTick >= (this.memory.nextPressureTick || 0);

    const candidates = [];
    if (defenseAction) {
      candidates.push({
        action: defenseAction,
        score: this.personalityProfile.defenseWeight * (1.0 + (danger ? 1.2 : 0.15) + leakPressure * 0.8),
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
        score: pressureBase + spikeBonus + depthBonus + (forcePressure ? 0.9 : 0),
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

    const legal = validateActionAgainstGame(game, this.laneIndex, action);
    if (!legal.ok) {
      this.memory.invalidStreak += 1;
      if (this.memory.invalidStreak >= 2 && defenseAction) {
        const fallbackLegal = validateActionAgainstGame(game, this.laneIndex, defenseAction);
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
    if (legal.normalized.type === AI_ACTION_TYPE.SEND_UNITS) {
      let minTicks = 26;
      let maxTicks = 44;
      if (this.difficulty === BOT_DIFFICULTY.EASY) { minTicks = 42; maxTicks = 72; }
      else if (this.difficulty === BOT_DIFFICULTY.HARD) { minTicks = 16; maxTicks = 30; }
      this.memory.nextPressureTick = currentTick + this.rng.nextInt(minTicks, maxTicks);
    } else if (!Number.isFinite(this.memory.nextPressureTick) || this.memory.nextPressureTick <= 0) {
      this.memory.nextPressureTick = currentTick + this.rng.nextInt(18, 34);
    }
    return legal.normalized;
  }
}

module.exports = {
  BotBrain,
};
