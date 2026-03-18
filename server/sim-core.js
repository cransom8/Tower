// server/sim-core.js
"use strict";

/**
 * Deterministic simulation core — Phase B0.
 * All shared primitives for sim-multilane and sim-survival.
 * Zero Math.random() — all randomness routed through mulberry32.
 */

// ── Seeded RNG ────────────────────────────────────────────────────────────────

/**
 * mulberry32 seeded PRNG factory.
 * @param {number} seed  32-bit unsigned integer seed
 * @returns {function(): number}  returns float in [0, 1) each call
 */
function mulberry32(seed) {
  let s = seed >>> 0;
  return function rng() {
    s += 0x6D2B79F5;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = t + Math.imul(t ^ (t >>> 7), 61 | t) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * Hash a string to a 32-bit unsigned integer seed for mulberry32.
 * Uses FNV-1a variant — deterministic, no dependencies.
 * @param {string} str
 * @returns {number} 32-bit unsigned integer
 */
function stringToSeed(str) {
  let h = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) {
    h ^= str.charCodeAt(i);
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h;
}

// ── Damage System ─────────────────────────────────────────────────────────────

const DAMAGE_MULTIPLIERS = {
  PIERCE:   { UNARMORED: 1.35, LIGHT: 1.20, MEDIUM: 1.00, HEAVY: 0.75, MAGIC: 0.85 },
  NORMAL:   { UNARMORED: 1.10, LIGHT: 1.00, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 0.90 },
  SPLASH:   { UNARMORED: 1.10, LIGHT: 1.25, MEDIUM: 1.20, HEAVY: 0.80, MAGIC: 0.85 },
  SIEGE:    { UNARMORED: 0.90, LIGHT: 0.90, MEDIUM: 1.00, HEAVY: 1.35, MAGIC: 0.80 },
  MAGIC:    { UNARMORED: 1.00, LIGHT: 1.05, MEDIUM: 1.00, HEAVY: 0.95, MAGIC: 1.40 },
  PHYSICAL: { UNARMORED: 1.05, LIGHT: 1.00, MEDIUM: 1.05, HEAVY: 1.10, MAGIC: 0.80 },
  // TRUE: bypasses both the multiplier table and damageReductionPct (handled inline)
};

/**
 * Compute final damage after type multipliers and armor reduction.
 * @param {number} baseDmg
 * @param {string} damageType          PIERCE|NORMAL|SPLASH|SIEGE|MAGIC|PHYSICAL|TRUE
 * @param {string} armorType           UNARMORED|LIGHT|MEDIUM|HEAVY|MAGIC
 * @param {number} damageReductionPct  0–80 flat % reduction (from unit_types.damage_reduction_pct)
 * @param {object} [specialProps]      { skipReduction: true } to bypass flat reduction
 * @returns {number} final damage (>= 0)
 */
function computeDamage(baseDmg, damageType, armorType, damageReductionPct, specialProps) {
  if (damageType === "TRUE") {
    // True damage bypasses both type multiplier and flat armor reduction
    return Math.max(0, baseDmg);
  }

  const table   = DAMAGE_MULTIPLIERS[damageType] || DAMAGE_MULTIPLIERS.NORMAL;
  const typeMult = table[armorType] != null ? table[armorType] : 1.0;
  let dmg = baseDmg * typeMult;

  const reductionPct = Number(damageReductionPct) || 0;
  if (reductionPct > 0 && !(specialProps && specialProps.skipReduction)) {
    dmg *= 1 - Math.min(80, reductionPct) / 100;
  }

  return Math.max(0, dmg);
}

// ── Projectile System ─────────────────────────────────────────────────────────

/**
 * Create and push a projectile onto lane.projectiles.
 * @param {object} game
 * @param {object} lane
 * @param {object} source  { id, x, y, kind: 'tower'|'unit' }
 * @param {string} targetId  unit id being targeted
 * @param {object} stats   { dmg, damageType, behavior, behaviorParams, travelTicks,
 *                           projectileTicks, isSplash, projectileType }
 * @returns {object} the created projectile
 */
function fireProjectile(game, lane, source, targetId, stats) {
  const path = lane.path || [];
  let toX = source.x, toY = source.y;
  const target = lane.units.find(u => u.id === targetId);
  if (target && path.length > 0) {
    const pos = path[Math.min(Math.floor(target.pathIdx), path.length - 1)];
    if (pos) { toX = pos.x; toY = pos.y; }
  }

  const travelTicks = stats.travelTicks || stats.projectileTicks || 1;
  const proj = {
    id:             `p${game.nextProjectileId++}`,
    ownerLane:      lane.laneIndex,
    sourceKind:     source.kind || "tower",
    sourceId:       source.id,
    projectileType: stats.projectileType || source.kind,
    damageType:     stats.damageType || "NORMAL",
    behavior:       stats.behavior   || "single",
    behaviorParams: stats.behaviorParams || {},
    isSplash:       !!(stats.isSplash || stats.behavior === "splash"),
    targetId,
    dmg:            stats.dmg,
    fromX:          source.x,
    fromY:          source.y,
    toX,
    toY,
    ticksRemaining: travelTicks,
    ticksTotal:     travelTicks,
    abilities:      stats.abilities  || [],  // carried for onHit resolution
  };

  lane.projectiles.push(proj);
  return proj;
}

/**
 * Resolve a landed projectile — deal damage based on behavior.
 * Behaviors: single | pierce | chain | bounce | splash
 * @param {object} game
 * @param {object} lane
 * @param {object} proj
 * @returns {string[]} unit IDs that died
 */
function resolveProjectile(game, lane, proj) {
  const path = lane.path || [];
  const dead = [];
  const hit  = [];  // all units that received damage this projectile

  function getUnitPos(unit) {
    if (!path.length) return null;
    return path[Math.min(Math.floor(unit.pathIdx), path.length - 1)];
  }

  function tileDist(ax, ay, bx, by) {
    const dx = ax - bx, dy = ay - by;
    return Math.sqrt(dx * dx + dy * dy);
  }

  function damageUnit(unit, overrideDmg) {
    if (!unit || unit.hp <= 0) return false;
    const reductionPct = Number(unit.damageReductionPct) || 0;
    const dmg = overrideDmg !== undefined ? overrideDmg : proj.dmg;
    unit.hp -= computeDamage(dmg, proj.damageType, unit.armorType || "MEDIUM", reductionPct);
    hit.push(unit.id);
    if (unit.hp <= 0) { dead.push(unit.id); return true; }
    return false;
  }

  const behavior = proj.behavior || "single";
  const bp = proj.behaviorParams || {};

  // ── Splash ──────────────────────────────────────────────────────────────────
  if (behavior === "splash") {
    const radius = bp.radius || 1.5;
    for (const u of lane.units) {
      if (u.hp <= 0) continue;
      const pos = getUnitPos(u);
      if (!pos) continue;
      if (tileDist(proj.toX, proj.toY, pos.x, pos.y) <= radius) damageUnit(u);
    }
    return { dead, hit };
  }

  // ── Pierce ──────────────────────────────────────────────────────────────────
  if (behavior === "pierce") {
    const maxTargets   = bp.maxTargets   || 3;
    const pierceRadius = bp.pierceRadius || 1.0;
    const primary      = lane.units.find(u => u.id === proj.targetId && u.hp > 0);
    if (primary) {
      damageUnit(primary);
      const primaryPos = getUnitPos(primary);
      if (primaryPos) {
        let count = 1;
        for (const u of lane.units) {
          if (count >= maxTargets) break;
          if (u.id === primary.id || u.hp <= 0) continue;
          const pos = getUnitPos(u);
          if (!pos) continue;
          if (tileDist(primaryPos.x, primaryPos.y, pos.x, pos.y) <= pierceRadius) {
            damageUnit(u);
            count++;
          }
        }
      }
    }
    return { dead, hit };
  }

  // ── Chain ───────────────────────────────────────────────────────────────────
  if (behavior === "chain") {
    const maxJumps   = bp.maxJumps   || 3;
    const jumpRange  = bp.jumpRange  || 2.0;
    const dmgFalloff = bp.dmgFalloff || 1.0;
    const chainHit   = new Set();
    let current    = lane.units.find(u => u.id === proj.targetId && u.hp > 0);
    let currentDmg = proj.dmg;
    let jumps = 0;
    while (current && jumps < maxJumps) {
      chainHit.add(current.id);
      damageUnit(current, currentDmg);
      const currentPos = getUnitPos(current);
      jumps++;
      if (jumps >= maxJumps || !currentPos) break;
      currentDmg *= dmgFalloff;
      let next = null, nextDist = Infinity;
      for (const u of lane.units) {
        if (chainHit.has(u.id) || u.hp <= 0) continue;
        const pos = getUnitPos(u);
        if (!pos) continue;
        const d = tileDist(currentPos.x, currentPos.y, pos.x, pos.y);
        if (d <= jumpRange && d < nextDist) { nextDist = d; next = u; }
      }
      current = next;
    }
    return { dead, hit };
  }

  // ── Bounce ──────────────────────────────────────────────────────────────────
  if (behavior === "bounce") {
    const maxBounces  = bp.maxBounces  || 2;
    const bounceRange = bp.bounceRange || 3.0;
    const bounceHit   = new Set();
    let current = lane.units.find(u => u.id === proj.targetId && u.hp > 0);
    let bounces = 0;
    while (current && bounces <= maxBounces) {
      bounceHit.add(current.id);
      damageUnit(current);
      const currentPos = getUnitPos(current);
      bounces++;
      if (bounces > maxBounces || !currentPos) break;
      let next = null, nextDist = Infinity;
      for (const u of lane.units) {
        if (bounceHit.has(u.id) || u.hp <= 0) continue;
        const pos = getUnitPos(u);
        if (!pos) continue;
        const d = tileDist(currentPos.x, currentPos.y, pos.x, pos.y);
        if (d <= bounceRange && d < nextDist) { nextDist = d; next = u; }
      }
      current = next;
    }
    return { dead, hit };
  }

  // ── Single (default) ────────────────────────────────────────────────────────
  const target = lane.units.find(u => u.id === proj.targetId && u.hp > 0);
  if (target) damageUnit(target);
  return { dead, hit };
}

// ── Ability System ────────────────────────────────────────────────────────────

/**
 * Execute all abilities on a unit for the given hook category.
 * Sorted: priority ASC, then abilityId ASC.
 * @param {object} game
 * @param {object} lane
 * @param {object} unit           unit triggering the hook
 * @param {string} hookCategory   onSpawn | onAttack | onHit | onTick | onDeath
 * @param {object} context        hook-specific data (e.g. { target, damage, projectile })
 * @returns {object} context (may be mutated)
 */
function resolveAbilityHook(game, lane, unit, hookCategory, context) {
  const abilities = (unit.abilities || []).filter(a => a.hook === hookCategory);
  if (!abilities.length) return context;

  abilities.sort((a, b) => {
    const pa = a.priority || 0, pb = b.priority || 0;
    if (pa !== pb) return pa - pb;
    return (a.abilityId || 0) - (b.abilityId || 0);
  });

  for (const ability of abilities) {
    _executeAbility(game, lane, unit, ability, context);
  }
  return context;
}

function _executeAbility(game, lane, unit, ability, context) {
  const params = ability.params || {};
  const tick   = game.tick || 0;

  switch (ability.type) {
    case "aura":
      // Registered at spawn; processed by applyAuras(), not here
      break;

    case "slow": {
      const target = context.target;
      if (!target) break;
      target.slowEndTick = tick + (params.durationTicks || 60);
      target.slowMult    = Math.min(target.slowMult || 1.0, params.speedMult || 0.5);
      break;
    }

    case "freeze": {
      const target = context.target;
      if (!target) break;
      const freezeProc = params.procChance !== undefined ? params.procChance : 100;
      if (freezeProc < 100 && game.rng && game.rng() * 100 > freezeProc) break;
      target.freezeEndTick = tick + (params.durationTicks || 40);
      break;
    }

    case "poison": {
      const target = context.target;
      if (!target) break;
      target.poisonEndTick    = tick + (params.durationTicks || 120);
      target.poisonDmgPerTick = params.dmgPerTick || 1;
      break;
    }

    case "burn": {
      const target = context.target;
      if (!target) break;
      target.burnEndTick    = tick + (params.durationTicks || 80);
      target.burnDmgPerTick = params.dmgPerTick || 2;
      break;
    }

    case "armor_reduction": {
      const target = context.target;
      if (!target) break;
      target.armorReductionEndTick = tick + (params.durationTicks || 100);
      target.armorReductionPct     = params.reductionPct || 20;
      break;
    }

    case "knockback": {
      const target = context.target;
      if (!target) break;
      const kbProc = params.procChance !== undefined ? params.procChance : 100;
      if (kbProc < 100 && game.rng && game.rng() * 100 > kbProc) break;
      target.pathIdx = Math.max(0, target.pathIdx - (params.tiles || 1));
      break;
    }

    case "teleport_back": {
      const target = context.target;
      if (!target) break;
      const tbProc = params.procChance !== undefined ? params.procChance : 100;
      if (tbProc < 100 && game.rng && game.rng() * 100 > tbProc) break;
      target.pathIdx = 0;
      break;
    }

    // ── onAttack: modify projectile behavior via context ────────────────────
    case "chain_lightning":
      context.behavior = "chain";
      context.behaviorParams = {
        maxJumps:   params.maxJumps   || 3,
        jumpRange:  params.jumpRange  || 2.0,
        dmgFalloff: params.dmgFalloff !== undefined ? params.dmgFalloff : 0.75,
      };
      break;

    case "pierce_targets":
      context.behavior = "pierce";
      context.behaviorParams = {
        maxTargets:   params.maxTargets   || 3,
        pierceRadius: params.pierceRadius || 1.0,
      };
      break;

    case "splash_damage":
      context.behavior = "splash";
      context.behaviorParams = {
        radius: params.radius || 1.5,
      };
      break;

    default:
      break;
  }
}

// ── Aura System ───────────────────────────────────────────────────────────────

/**
 * Collect auras from all units in the lane and store highest-value-only
 * per aura type onto lane.activeAuras. Sim tick reads lane.activeAuras
 * when computing bonuses; no stacking.
 * @param {object} game
 * @param {object} lane
 */
function applyAuras(game, lane, extraSources) {
  const best = {};
  const allSources = (lane.units || []).concat(extraSources || []);
  for (const unit of allSources) {
    for (const ability of (unit.abilities || [])) {
      if (ability.type !== "aura" || ability.hook !== "onSpawn") continue;
      const params    = ability.params || {};
      const auraType  = params.auraType || "dmg_bonus";
      const value     = params.value    || 0;
      if (best[auraType] === undefined || value > best[auraType]) {
        best[auraType] = value;
      }
    }
  }
  lane.activeAuras = best;
}

// ── Status Resolution ─────────────────────────────────────────────────────────

/**
 * Apply timed DoT effects and expire stale statuses on a unit.
 * Called once per tick per unit.
 * @param {object} unit
 * @param {number} tick  current game.tick
 * @returns {number} total raw DoT damage applied this tick
 */
function resolveStatuses(unit, tick) {
  let totalDot = 0;

  if (unit.poisonEndTick) {
    if (unit.poisonEndTick > tick) {
      unit.hp -= unit.poisonDmgPerTick || 0;
      totalDot += unit.poisonDmgPerTick || 0;
    } else {
      delete unit.poisonEndTick;
      delete unit.poisonDmgPerTick;
    }
  }

  if (unit.burnEndTick) {
    if (unit.burnEndTick > tick) {
      unit.hp -= unit.burnDmgPerTick || 0;
      totalDot += unit.burnDmgPerTick || 0;
    } else {
      delete unit.burnEndTick;
      delete unit.burnDmgPerTick;
    }
  }

  if (unit.slowEndTick && unit.slowEndTick <= tick) {
    delete unit.slowEndTick;
    delete unit.slowMult;
  }

  if (unit.freezeEndTick && unit.freezeEndTick <= tick) {
    delete unit.freezeEndTick;
  }

  if (unit.armorReductionEndTick && unit.armorReductionEndTick <= tick) {
    delete unit.armorReductionEndTick;
    delete unit.armorReductionPct;
  }

  return totalDot;
}

// ── Spatial Separation ────────────────────────────────────────────────────────

/**
 * Push overlapping units apart along the path to maintain minSpacing.
 * O(n log n) due to sort. Geometry-only — no RNG.
 * Push direction: lower pathIdx moves back; id string used as tie-break.
 * @param {object[]} units       units with { id, pathIdx, hp }
 * @param {number}   minSpacing  minimum pathIdx gap between adjacent units
 */
function applySeparation(units, minSpacing) {
  if (!units || units.length < 2 || minSpacing <= 0) return;

  // Sort descending by pathIdx (front-most first)
  const sorted = units.slice().sort((a, b) =>
    b.pathIdx !== a.pathIdx ? b.pathIdx - a.pathIdx : (a.id < b.id ? -1 : 1)
  );

  for (let i = 0; i < sorted.length - 1; i++) {
    const front = sorted[i];
    const back  = sorted[i + 1];
    if (front.hp <= 0 || back.hp <= 0) continue;
    const gap = front.pathIdx - back.pathIdx;
    if (gap < minSpacing) {
      const push = (minSpacing - gap) * 0.5;
      front.pathIdx += push;
      back.pathIdx   = Math.max(0, back.pathIdx - push);
    }
  }
}

// ── Action Sorting ────────────────────────────────────────────────────────────

/**
 * Sort actions into canonical deterministic order.
 * tickApply ASC → laneId ASC → playerId ASC → actionSeq ASC
 * @param {object[]} actions
 * @returns {object[]} sorted copy
 */
function sortActions(actions) {
  return actions.slice().sort((a, b) => {
    if (a.tickApply !== b.tickApply) return a.tickApply - b.tickApply;
    if (a.laneId    !== b.laneId)    return a.laneId    - b.laneId;
    const pa = String(a.playerId || ""), pb = String(b.playerId || "");
    if (pa !== pb) return pa < pb ? -1 : 1;
    return (a.actionSeq || 0) - (b.actionSeq || 0);
  });
}

// ── Action Dispatch ───────────────────────────────────────────────────────────

const _actionHandlers = {};

/**
 * Register a handler for an action type.
 * Used by sim-multilane / sim-survival to extend the dispatch table.
 * @param {string}   type
 * @param {function} handler  (game, lane, action) => { ok, reason? }
 */
function registerActionHandler(type, handler) {
  _actionHandlers[type] = handler;
}

/**
 * Dispatch a canonical action to the correct handler.
 * @param {object} game
 * @param {object} lane
 * @param {object} action  { type, payload, tickApply, laneId, playerId, actionSeq }
 * @returns {{ ok: boolean, reason?: string }}
 */
function applyAction(game, lane, action) {
  if (!action || typeof action.type !== "string") return { ok: false, reason: "Bad action" };
  const handler = _actionHandlers[action.type];
  if (typeof handler === "function") return handler(game, lane, action);
  return { ok: false, reason: `Unknown action type: ${action.type}` };
}

// ── Snapshots ─────────────────────────────────────────────────────────────────

/**
 * Deep-clone minimal sim state for diff / replay.
 * @param {object} game
 * @returns {object} snapshot
 */
function takeSnapshot(game) {
  return JSON.parse(JSON.stringify({
    tick:   game.tick,
    phase:  game.phase,
    winner: game.winner,
    lanes:  (game.lanes || []).map(lane => ({
      laneIndex:     lane.laneIndex,
      gold:          lane.gold,
      income:        lane.income,
      lives:         lane.lives,
      eliminated:    lane.eliminated,
      wallCount:     lane.wallCount,
      barracksLevel: lane.barracks ? lane.barracks.level : 1,
      units: (lane.units || []).map(u => ({
        id:      u.id,
        type:    u.type,
        hp:      u.hp,
        maxHp:   u.maxHp,
        pathIdx: u.pathIdx,
      })),
      projectiles: (lane.projectiles || []).map(p => ({
        id:             p.id,
        ticksRemaining: p.ticksRemaining,
      })),
      path: lane.path || [],
    })),
  }));
}

/**
 * Compute a delta between two snapshots.
 * Returns null if there are no changes.
 * @param {object} prev
 * @param {object} next
 * @returns {object|null}
 */
function diffSnapshot(prev, next) {
  const diff = {};
  let hasChange = false;

  if (prev.tick   !== next.tick)   { diff.tick   = next.tick;   hasChange = true; }
  if (prev.phase  !== next.phase)  { diff.phase  = next.phase;  hasChange = true; }
  if (prev.winner !== next.winner) { diff.winner = next.winner; hasChange = true; }

  const prevLaneMap = Object.fromEntries((prev.lanes || []).map(l => [l.laneIndex, l]));
  const laneDiffs   = [];

  for (const lane of (next.lanes || [])) {
    const p  = prevLaneMap[lane.laneIndex] || {};
    const ld = { laneIndex: lane.laneIndex };
    let laneChanged = false;
    for (const key of ["gold", "income", "lives", "eliminated", "wallCount", "barracksLevel"]) {
      if (p[key] !== lane[key]) { ld[key] = lane[key]; laneChanged = true; }
    }
    if (laneChanged) {
      ld.units       = lane.units;
      ld.projectiles = lane.projectiles;
      laneDiffs.push(ld);
      hasChange = true;
    }
  }

  if (laneDiffs.length > 0) diff.lanes = laneDiffs;
  return hasChange ? diff : null;
}

// ── Exports ───────────────────────────────────────────────────────────────────

module.exports = {
  mulberry32,
  stringToSeed,
  DAMAGE_MULTIPLIERS,
  computeDamage,
  fireProjectile,
  resolveProjectile,
  resolveAbilityHook,
  applyAuras,
  resolveStatuses,
  applySeparation,
  sortActions,
  applyAction,
  registerActionHandler,
  takeSnapshot,
  diffSnapshot,
};
