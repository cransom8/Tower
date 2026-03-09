// server/sim-survival.js
"use strict";

/**
 * Survival Mode simulation — wraps the multi-lane grid per player.
 * Enemies are server-spawned waves; player defends with towers/walls/units.
 * Supports 1P solo and 2P co-op (shared wave progression, pooled lives).
 *
 * Building mechanics (place_wall, convert_wall, bulk_convert_walls, remove_wall,
 * upgrade_tower, bulk_upgrade_towers, set_tower_target, upgrade_barracks,
 * sell_tower) are identical to Multi-Lane mode — routed through applyMLAction.
 * Tower combat uses the same stats, damage types, armor types, target modes,
 * projectile travel, warlock debuffs, and kill bounties as Multi-Lane.
 * "Sending" actions (spawn_unit, set_autosend) are blocked — no opponents.
 */

const simMl = require("./sim-multilane");
const {
  computeDamage: coreComputeDamage,
  fireProjectile,
  resolveProjectile,
  applySeparation,
  mulberry32,
} = require("./sim-core");
const { getUnitType } = require("./unitTypes");

const TICK_HZ    = simMl.TICK_HZ    || 20;
const TICK_MS    = simMl.TICK_MS    || 50;
const GRID_W     = simMl.GRID_W     || 11;
const GRID_H     = simMl.GRID_H     || 28;
const { resolveUnitDef, resolveTowerDef } = simMl;
const INCOME_INTERVAL_TICKS = simMl.INCOME_INTERVAL_TICKS || 240;

const PREP_TICKS        = 60;   // 3 seconds at 20Hz
const TOWER_MAX_LEVEL   = 10;
const SPLASH_RADIUS     = 1.5;  // tiles — matches sim-multilane SPLASH_RADIUS_TILES
const WARLOCK_DEBUFF_CD    = 60;
const WARLOCK_DEBUFF_TICKS = 60;
const WARLOCK_DEBUFF_MULT  = 0.75;
const WARLOCK_DEBUFF_RANGE = 3.5;

const TOWER_TARGET_MODES = new Set(["first", "last", "weakest", "strongest"]);
function isValidUnitType(key) {
  return !!getUnitType(key);
}

// ── Tower stats — delegates to sim-multilane resolveTowerDef for DB-driven stats.
function getTowerStats(type, level) {
  const base = resolveTowerDef(type);
  if (!base) return null;
  const lvl = Math.max(1, Math.min(TOWER_MAX_LEVEL, level));
  const s = lvl - 1;
  return {
    dmg:             base.dmg * (1 + 0.12 * s),
    range:           base.range * (1 + 0.015 * s),
    atkCdTicks:      Math.max(5, Math.round(base.atkCdTicks * (1 - 0.015 * s))),
    projectileTicks: base.projectileTicks,
    damageType:      base.damageType,
    isSplash:        base.isSplash || false,
  };
}

// Damage is now provided by sim-core.coreComputeDamage (imported above).

function tileDist(ax, ay, bx, by) {
  const dx = ax - bx, dy = ay - by;
  return Math.sqrt(dx * dx + dy * dy);
}

function getUnitTilePos(unit, path) {
  if (!path || path.length === 0) return null;
  return path[Math.min(Math.floor(unit.pathIdx), path.length - 1)];
}

// ── State factory ─────────────────────────────────────────────────────────────

function createSurvivalGame(playerCount, waveSet, matchSeed) {
  const count      = Math.min(2, Math.max(1, playerCount || 1));
  const startGold  = Number(waveSet.starting_gold)  || 150;
  const startLives = Number(waveSet.starting_lives) || 20;

  const lanes = [];
  for (let i = 0; i < count; i++) {
    // createMLGame gives us a fully-initialised lane with grid, path, barracks,
    // autosend, projectiles[], spawnQueue[], etc.
    const mlGame = simMl.createMLGame(1, { startGold, startIncome: 10, laneTeams: ['red'] });
    const lane = mlGame.lanes[0];
    lane.laneIndex = i;
    lanes.push(lane);
  }

  return {
    tick: 0,
    phase: "playing",        // playing | ended
    mode: "survival",
    playerCount: count,

    // Survival-specific shared state
    waveNumber: 0,
    wavePhase: "PREP",       // PREP | SPAWNING | CLEARING | COMPLETE | GAME_OVER
    prepTicksRemaining: PREP_TICKS,
    spawnQueue: [],          // [{unitType, spawnAtTick, laneIndex}]
    killCount: 0,
    totalWavesCleared: 0,
    goldEarned: startGold * count,
    runStartedAt: Date.now(),
    config: waveSet,

    lanes,
    incomeTickCounter: 0,
    nextUnitId: 1,
    nextProjectileId: 1,

    // Phase B: seeded RNG + version tracking
    rng: mulberry32(matchSeed !== undefined ? (matchSeed >>> 0) : (Date.now() >>> 0)),
    matchSeed: matchSeed !== undefined ? (matchSeed >>> 0) : null,
    configVersionId: null,

    // Lives pooled across co-op partners
    lives: startLives,
    maxLives: startLives,
  };
}

// ── Wave config helpers ───────────────────────────────────────────────────────

function getWaveConfig(game, waveNumber) {
  const waves = (game.config && game.config.waves) || [];
  const wave = waves.find(w => w.wave_number === waveNumber);
  if (wave) return wave;

  if (!game.config || !game.config.auto_scale) return null;
  const lastWave = waves.reduce((best, w) =>
    (!best || w.wave_number > best.wave_number) ? w : best, null);
  if (!lastWave) return null;
  return autoScaleWave(lastWave, waveNumber);
}

function autoScaleWave(lastWave, targetWaveNumber) {
  const extra = targetWaveNumber - lastWave.wave_number;
  const scaleFactor = 1 + extra * 0.10;
  return {
    ...lastWave,
    wave_number: targetWaveNumber,
    is_boss: false,
    is_rush: false,
    gold_bonus: Math.ceil((lastWave.gold_bonus || 0) * scaleFactor),
    notes: `Auto-scaled from wave ${lastWave.wave_number}`,
    spawn_groups: (lastWave.spawn_groups || []).map(g => ({
      ...g,
      count: Math.ceil(g.count * scaleFactor),
      spawn_interval_ms: Math.max(300, (g.spawn_interval_ms || 1000) - extra * 50),
    })),
  };
}

// ── Spawn queue builder ───────────────────────────────────────────────────────

// Phase B: rng param replaces Math.random() for deterministic jitter
function buildSpawnQueue(waveConfig, currentTick, playerCount, rng) {
  const randFn = typeof rng === "function" ? rng : Math.random;
  const queue = [];
  for (const group of (waveConfig.spawn_groups || [])) {
    const unitType = group.unit_type;
    if (!isValidUnitType(unitType)) continue;
    const count         = Math.max(1, group.count || 1);
    const delayTicks    = Math.round((group.start_delay_ms || 0) / TICK_MS);
    const intervalTicks = Math.max(2, Math.round((group.spawn_interval_ms || 1000) / TICK_MS));
    const randPct       = Number(group.randomize_pct) || 0;

    for (let i = 0; i < count; i++) {
      const jitter = randPct > 0
        ? Math.round((randFn() * 2 - 1) * intervalTicks * randPct / 100)
        : 0;
      const baseTick = currentTick + delayTicks + i * intervalTicks + jitter;

      // Co-op: distribute enemies across lanes round-robin
      for (let laneIdx = 0; laneIdx < playerCount; laneIdx++) {
        queue.push({ unitType, spawnAtTick: baseTick + laneIdx, laneIndex: laneIdx });
      }
    }
  }
  return queue.sort((a, b) => a.spawnAtTick - b.spawnAtTick);
}

// ── Enemy unit spawning ───────────────────────────────────────────────────────

function spawnEnemyUnit(game, unitType, laneIndex) {
  const lane = game.lanes[laneIndex];
  if (!lane) return;

  const def = resolveUnitDef(unitType);
  if (!def) return;

  const isBossWave = game.wavePhase === 'SPAWNING' &&
    !!(game.config?.waves?.find(w => w.wave_number === game.waveNumber)?.is_boss);
  const hpMult = isBossWave ? 2.0 : 1.0;
  const hp = Math.ceil(def.hp * hpMult);

  const unit = {
    id: `sv_u${game.nextUnitId++}`,
    ownerLane: -1,         // -1 = server-owned enemy
    type: unitType,
    pathIdx: 0,
    hp,
    maxHp: hp,
    baseDmg: 0,            // enemies don't attack towers
    baseSpeed: def.pathSpeed,
    atkCd: 0,
    // Phase B: carry armor/reduction so resolveProjectile can read them
    armorType: def.armorType || "MEDIUM",
    damageReductionPct: def.damageReductionPct || 0,
    isEnemy: true,
    isBoss: isBossWave,
  };
  if (def.warlockDebuff) unit.warlockCd = WARLOCK_DEBUFF_CD;

  lane.units.push(unit);
}

// ── Main tick function ────────────────────────────────────────────────────────

function tickSurvival(game) {
  if (game.phase === "ended") return;

  game.tick++;

  // Income for all lanes (same interval as ML)
  game.incomeTickCounter++;
  if (game.incomeTickCounter >= INCOME_INTERVAL_TICKS) {
    game.incomeTickCounter = 0;
    for (const lane of game.lanes) {
      lane.gold += lane.income;
      game.goldEarned += lane.income;
    }
  }

  // Wave phase machine
  if (game.wavePhase === "PREP") {
    game.prepTicksRemaining--;
    if (game.prepTicksRemaining <= 0) {
      _startNextWave(game);
    }
    // Tower cooldowns still tick during prep
    for (const lane of game.lanes) {
      _tickTowerCooldowns(lane);
    }
    return;
  }

  if (game.wavePhase === "SPAWNING") {
    const remaining = [];
    for (const entry of game.spawnQueue) {
      if (entry.spawnAtTick <= game.tick) {
        spawnEnemyUnit(game, entry.unitType, entry.laneIndex);
      } else {
        remaining.push(entry);
      }
    }
    game.spawnQueue = remaining;
    if (game.spawnQueue.length === 0) {
      game.wavePhase = "CLEARING";
    }
  }

  // Full combat tick for each lane
  let totalEnemiesAlive = 0;
  let livesLostThisTick = 0;

  for (const lane of game.lanes) {
    const leaked = _tickLaneFull(game, lane);
    livesLostThisTick += leaked;
    for (const u of lane.units) {
      if (u.isEnemy) totalEnemiesAlive++;
    }
  }

  if (livesLostThisTick > 0) {
    game.lives = Math.max(0, game.lives - livesLostThisTick);
    if (game.lives <= 0) {
      game.wavePhase = "GAME_OVER";
      game.phase = "ended";
      return;
    }
  }

  if (game.wavePhase === "CLEARING" && totalEnemiesAlive === 0) {
    _waveComplete(game);
  }
}

// ── Private helpers ───────────────────────────────────────────────────────────

function _startNextWave(game) {
  game.waveNumber++;
  const waveConfig = getWaveConfig(game, game.waveNumber);
  if (!waveConfig) {
    game.wavePhase = "COMPLETE";
    game.phase = "ended";
    return;
  }
  game.spawnQueue = buildSpawnQueue(waveConfig, game.tick, game.playerCount, game.rng);
  game.wavePhase = "SPAWNING";
}

function _waveComplete(game) {
  game.totalWavesCleared++;
  const waveConfig = (game.config?.waves || []).find(w => w.wave_number === game.waveNumber);
  const bonus = Number((waveConfig || {}).gold_bonus) || 0;
  if (bonus > 0) {
    for (const lane of game.lanes) {
      lane.gold += bonus;
    }
    game.goldEarned += bonus * game.playerCount;
  }
  game.wavePhase = "PREP";
  game.prepTicksRemaining = PREP_TICKS;
}

/** Tick tower attack cooldowns only (used during PREP when no enemies present). */
function _tickTowerCooldowns(lane) {
  for (let tx = 0; tx < GRID_W; tx++) {
    for (let ty = 0; ty < GRID_H; ty++) {
      const tile = lane.grid[tx][ty];
      if (tile.type === "tower" && tile.atkCd > 0) tile.atkCd--;
    }
  }
}

/**
 * Full ML-equivalent combat tick for one lane.
 * Uses real tower stats, damage/armor type multipliers, target modes,
 * projectile travel time, splash, warlock debuffs, and kill gold bounties —
 * identical to the multi-lane simulation.
 * Returns number of lives lost this tick (boss leaks count as 3).
 */
function _tickLaneFull(game, lane) {
  let leakCount = 0;
  const path    = lane.path || [];
  const pathLen = path.length;

  // ── Tick cooldowns ────────────────────────────────────────────────────────
  for (const u of lane.units) {
    if (u.atkCd > 0) u.atkCd--;
    if (u.warlockCd !== undefined && u.warlockCd > 0) u.warlockCd--;
  }
  for (let tx = 0; tx < GRID_W; tx++) {
    for (let ty = 0; ty < GRID_H; ty++) {
      const tile = lane.grid[tx][ty];
      if (tile.type === "tower" && tile.atkCd > 0) tile.atkCd--;
    }
  }

  // ── Tower attacks → fire projectiles ─────────────────────────────────────
  for (let tx = 0; tx < GRID_W; tx++) {
    for (let ty = 0; ty < GRID_H; ty++) {
      const tile = lane.grid[tx][ty];
      if (tile.type !== "tower" || !tile.towerType || tile.atkCd > 0) continue;
      const stats = getTowerStats(tile.towerType, tile.towerLevel || 1);
      if (!stats) continue;

      const unitsInRange = lane.units.filter(u => {
        if (!u.isEnemy || u.hp <= 0) return false;
        const pos = getUnitTilePos(u, path);
        return pos && tileDist(tx, ty, pos.x, pos.y) <= stats.range;
      });
      if (unitsInRange.length === 0) continue;

      const mode = TOWER_TARGET_MODES.has(tile.targetMode) ? tile.targetMode : "first";
      let target = unitsInRange[0];
      for (const u of unitsInRange) {
        if (mode === "first"     && u.pathIdx > target.pathIdx) target = u;
        if (mode === "last"      && u.pathIdx < target.pathIdx) target = u;
        if (mode === "strongest" && u.hp      > target.hp)      target = u;
        if (mode === "weakest"   && u.hp      < target.hp)      target = u;
      }

      const targetPos = getUnitTilePos(target, path);
      const debuffMult = (tile.debuffEndTick !== undefined && tile.debuffEndTick > game.tick)
        ? (tile.debuffMult || 1.0) : 1.0;

      fireProjectile(game, lane,
        { id: `tower_${tx}_${ty}`, kind: "tower", x: tx, y: ty },
        target.id,
        {
          dmg:            stats.dmg * debuffMult,
          damageType:     stats.damageType,
          behavior:       stats.isSplash ? "splash" : "single",
          behaviorParams: stats.isSplash ? { radius: SPLASH_RADIUS } : {},
          travelTicks:    stats.projectileTicks,
          isSplash:       stats.isSplash,
          projectileType: tile.towerType,
        }
      );
      tile.atkCd = stats.atkCdTicks;
    }
  }

  // ── Enemy unit movement + leak detection ──────────────────────────────────
  for (const u of lane.units) {
    if (!u.isEnemy || u.hp <= 0) continue;
    if (pathLen === 0) continue;

    // Warlock: debuff nearest tower every WARLOCK_DEBUFF_CD ticks
    const def = resolveUnitDef(u.type);
    if (def && def.warlockDebuff && (u.warlockCd === undefined || u.warlockCd <= 0)) {
      u.warlockCd = WARLOCK_DEBUFF_CD;
      const pos = getUnitTilePos(u, path);
      if (pos) {
        let bestTile = null, bestDist = Infinity;
        for (let dtx = 0; dtx < GRID_W; dtx++) {
          for (let dty = 0; dty < GRID_H; dty++) {
            const dtile = lane.grid[dtx][dty];
            if (dtile.type !== "tower") continue;
            const d = tileDist(pos.x, pos.y, dtx, dty);
            if (d <= WARLOCK_DEBUFF_RANGE && d < bestDist) { bestDist = d; bestTile = dtile; }
          }
        }
        if (bestTile) {
          bestTile.debuffEndTick = game.tick + WARLOCK_DEBUFF_TICKS;
          bestTile.debuffMult    = WARLOCK_DEBUFF_MULT;
        }
      }
    }

    u.pathIdx = Math.min(u.pathIdx + u.baseSpeed, pathLen - 1);
    if (u.pathIdx >= pathLen - 1) {
      u.hp = 0;
      leakCount += u.isBoss ? 3 : 1;
    }
  }

  // Phase H: push overlapping enemy units apart
  applySeparation(lane.units.filter(u => u.isEnemy && u.hp > 0), 0.15);

  // ── Resolve projectiles — Phase B: use resolveProjectile from sim-core ──────
  const killedById = new Set();
  const stillFlying = [];
  for (const p of lane.projectiles) {
    p.ticksRemaining--;
    if (p.ticksRemaining > 0) { stillFlying.push(p); continue; }
    const { dead } = resolveProjectile(game, lane, p);
    for (const id of dead) killedById.add(id);
  }
  lane.projectiles = stillFlying;

  // Apply kill bounties for enemies that died from projectiles
  if (killedById.size > 0) {
    for (const u of lane.units) {
      if (!killedById.has(u.id) || !u.isEnemy) continue;
      game.killCount++;
      const bounty = (resolveUnitDef(u.type) || {}).bounty || 1;
      lane.gold += bounty;
      game.goldEarned += bounty;
    }
  }

  // Remove dead units
  lane.units = lane.units.filter(u => u.hp > 0);

  return leakCount;
}

// ── Apply player action ───────────────────────────────────────────────────────

/**
 * Route a building action to the underlying ML sim for the player's lane.
 * All building mechanics (walls, towers, upgrades, sell, barracks) are
 * identical to Multi-Lane mode via applyMLAction.
 * "Sending" actions (spawn_unit, set_autosend) are blocked — no opponents.
 */
function applySurvivalAction(game, laneIndex, action) {
  if (game.phase === "ended") return { ok: false, reason: "Game over" };
  if (laneIndex < 0 || laneIndex >= game.lanes.length) return { ok: false, reason: "Bad laneIndex" };

  // Block sending actions — survival has no opponents to send to
  if (action && (action.type === "spawn_unit" || action.type === "set_autosend")) {
    return { ok: false, reason: "Not available in survival mode" };
  }

  // Build a synthetic 1-lane game so applyMLAction can operate on the lane
  const lane = game.lanes[laneIndex];
  const syntheticGame = {
    tick:               game.tick,
    phase:              "playing",
    winner:             null,
    incomeTickCounter:  game.incomeTickCounter,
    playerCount:        1,
    lanes:              [{ ...lane, laneIndex: 0 }],
    nextUnitId:         game.nextUnitId,
    nextProjectileId:   game.nextProjectileId,
  };

  const res = simMl.applyMLAction(syntheticGame, 0, action);

  // Sync mutated lane state back (preserves laneIndex)
  Object.assign(lane, syntheticGame.lanes[0], { laneIndex });
  game.nextUnitId       = syntheticGame.nextUnitId;
  game.nextProjectileId = syntheticGame.nextProjectileId;

  return res;
}

// ── Snapshot ──────────────────────────────────────────────────────────────────

function createSurvivalSnapshot(game) {
  const elapsed = Math.floor((Date.now() - game.runStartedAt) / 1000);
  return {
    tick: game.tick,
    waveNumber: game.waveNumber,
    wavePhase: game.wavePhase,
    prepTicksRemaining: game.prepTicksRemaining,
    lives: game.lives,
    maxLives: game.maxLives,
    killCount: game.killCount,
    totalWavesCleared: game.totalWavesCleared,
    goldEarned: game.goldEarned,
    timeSurvived: elapsed,
    phase: game.phase,
    lanes: game.lanes.map(lane => {
      const walls = [], towerCells = [];
      for (let x = 0; x < GRID_W; x++) {
        for (let y = 0; y < GRID_H; y++) {
          const tile = lane.grid[x][y];
          if (tile.type === "wall") {
            walls.push({ x, y });
          } else if (tile.type === "tower") {
            towerCells.push({
              x, y,
              type: tile.towerType,
              level: tile.towerLevel,
              targetMode: tile.targetMode || "first",
              debuffed: tile.debuffEndTick !== undefined && tile.debuffEndTick > game.tick,
            });
          }
        }
      }
      return {
        laneIndex: lane.laneIndex,
        gold: lane.gold,
        income: lane.income,
        wallCount: lane.wallCount,
        barracksLevel: lane.barracks ? lane.barracks.level : 1,
        walls,
        towerCells,
        path: lane.path || [],
        units: lane.units.map(u => {
          const pos = getUnitTilePos(u, lane.path || []);
          return {
            id: u.id,
            type: u.type,
            isEnemy: !!u.isEnemy,
            isBoss: !!u.isBoss,
            hp: u.hp,
            maxHp: u.maxHp,
            x: pos ? pos.x : 0,
            y: pos ? pos.y : 0,
          };
        }),
        projectiles: (lane.projectiles || []).map(p => ({
          id: p.id,
          projectileType: p.projectileType,
          damageType: p.damageType,
          isSplash: p.isSplash,
          fromX: p.fromX, fromY: p.fromY,
          toX: p.toX,     toY: p.toY,
          progress: 1 - p.ticksRemaining / p.ticksTotal,
        })),
      };
    }),
  };
}

module.exports = {
  TICK_HZ,
  TICK_MS,
  createSurvivalGame,
  tickSurvival,
  applySurvivalAction,
  createSurvivalSnapshot,
};
