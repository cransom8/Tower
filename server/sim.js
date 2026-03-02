// server/sim.js
"use strict";

/**
 * Phase 1 PvP simulation (authoritative server).
 * Lane is vertical: y=0 is TOP gate, y=1 is BOTTOM gate.
 * - top side units move DOWN (+y)
 * - bottom side units move UP (-y)
 */

const TICK_HZ = 20;
const TICK_MS = Math.floor(1000 / TICK_HZ);

const INCOME_INTERVAL_TICKS = 10 * TICK_HZ; // 10 seconds
const START_GOLD = 70;
const START_INCOME = 10;
const GATE_HP_START = 100;
const ALLOWED_ACTION_TYPES = new Set(["spawn_unit", "build_tower", "upgrade_tower", "sell_tower"]);
const TOWER_SLOTS = ["left_outer", "left_mid", "left_inner", "right_inner", "right_mid", "right_outer"];
const TOWER_MAX_LEVEL = 10;

// Combat + spacing (normalized lane units)
const COMBAT_RANGE = 0.045; // if enemy within this y-distance, you can attack
const MIN_GAP = 0.012; // friendly units cannot move closer than this gap
const GATE_Y_TOP = 0.05;
const GATE_Y_BOTTOM = 0.95;

// Spawn positions
const SPAWN_Y_TOP = 0.12;
const SPAWN_Y_BOTTOM = 0.88;

const MARCH_SPEED = 0.00129375; // shared march speed — only runner differs

const UNIT_DEFS = {
  footman: { cost: 10, income: 2, hp: 90,  dmg: 9,  atkCdTicks: 8,  speedPerTick: MARCH_SPEED,   bounty: 2, range: COMBAT_RANGE, ranged: false, armorType: "MEDIUM",    damageType: "NORMAL" },
  bowman:  { cost: 12, income: 2, hp: 50,  dmg: 9,  atkCdTicks: 9,  speedPerTick: MARCH_SPEED,   bounty: 2, range: 0.22,         ranged: true,  projectileTicks: 7, armorType: "LIGHT",     damageType: "PIERCE" },
  ironclad:{ cost: 15, income: 3, hp: 140, dmg: 11, atkCdTicks: 10, speedPerTick: MARCH_SPEED,   bounty: 3, range: COMBAT_RANGE, ranged: false, armorType: "HEAVY",     damageType: "NORMAL" },
  runner:  { cost: 8,  income: 1, hp: 55,  dmg: 7,  atkCdTicks: 7,  speedPerTick: 0.00215625,    bounty: 1, range: COMBAT_RANGE, ranged: false, armorType: "UNARMORED", damageType: "NORMAL" },
  warlock: { cost: 15, income: 3, hp: 80,  dmg: 14, atkCdTicks: 11, speedPerTick: MARCH_SPEED,   bounty: 3, range: 0.18,         ranged: true,  projectileTicks: 8, armorType: "MAGIC",     damageType: "MAGIC"  },
};

const TOWER_DEFS = {
  archer: { cost: 10, range: 0.30, dmg: 6.6, atkCdTicks: 12, projectileTicks: 7, damageType: "PIERCE" },
  fighter: { cost: 12, range: 0.22, dmg: 8.8, atkCdTicks: 11, projectileTicks: 6, damageType: "NORMAL" },
  cannon: { cost: 30, range: 0.50, dmg: 19.8, atkCdTicks: 16, projectileTicks: 9, damageType: "SPLASH" },
  ballista: { cost: 20, range: 0.40, dmg: 12.1, atkCdTicks: 14, projectileTicks: 8, damageType: "SIEGE" },
  mage: { cost: 24, range: 0.35, dmg: 13.2, atkCdTicks: 13, projectileTicks: 7, damageType: "MAGIC" },
};

const DAMAGE_MULTIPLIERS = {
  PIERCE: { UNARMORED: 1.35, LIGHT: 1.2, MEDIUM: 1.0, HEAVY: 0.75, MAGIC: 0.85 },
  NORMAL: { UNARMORED: 1.1, LIGHT: 1.0, MEDIUM: 1.0, HEAVY: 0.95, MAGIC: 0.9 },
  SPLASH: { UNARMORED: 1.1, LIGHT: 1.25, MEDIUM: 1.2, HEAVY: 0.8, MAGIC: 0.85 },
  SIEGE: { UNARMORED: 0.9, LIGHT: 0.9, MEDIUM: 1.0, HEAVY: 1.35, MAGIC: 0.8 },
  MAGIC: { UNARMORED: 1.0, LIGHT: 1.05, MEDIUM: 1.0, HEAVY: 0.95, MAGIC: 1.4 },
};

function makeTowerSlots() {
  return {
    left_outer: { type: null, level: 0, atkCd: 0 },
    left_mid: { type: null, level: 0, atkCd: 0 },
    left_inner: { type: null, level: 0, atkCd: 0 },
    right_inner: { type: null, level: 0, atkCd: 0 },
    right_mid: { type: null, level: 0, atkCd: 0 },
    right_outer: { type: null, level: 0, atkCd: 0 },
  };
}

function slotX(slot) {
  if (slot === "left_outer") return 0.15;
  if (slot === "left_mid") return 0.27;
  if (slot === "left_inner") return 0.39;
  if (slot === "right_inner") return 0.61;
  if (slot === "right_mid") return 0.73;
  return 0.85;
}

function getDamageMultiplier(damageType, armorType) {
  const byDamage = DAMAGE_MULTIPLIERS[damageType] || DAMAGE_MULTIPLIERS.NORMAL;
  const m = byDamage[armorType];
  return Number.isFinite(m) ? m : 1;
}

function computeDamage(baseDamage, damageType, armorType) {
  return baseDamage * getDamageMultiplier(damageType, armorType);
}

function getTowerUpgradeCost(type, nextLevel) {
  const base = TOWER_DEFS[type];
  if (!base) return Infinity;
  const scale = 0.75 + (0.25 * nextLevel);
  return Math.ceil(base.cost * scale);
}

function getTowerStats(type, level) {
  const base = TOWER_DEFS[type];
  if (!base) return null;
  const lvl = Math.max(1, Math.min(TOWER_MAX_LEVEL, level));
  const levelScale = lvl - 1;
  const dmg = base.dmg * (1 + 0.12 * levelScale);
  const range = Math.min(0.65, base.range * (1 + 0.015 * levelScale));
  const atkCdTicks = Math.max(5, Math.round(base.atkCdTicks * (1 - 0.015 * levelScale)));
  return {
    dmg,
    range,
    atkCdTicks,
    projectileTicks: base.projectileTicks,
    damageType: base.damageType,
  };
}

function createPublicConfig() {
  return {
    tickHz: TICK_HZ,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    startGold: START_GOLD,
    startIncome: START_INCOME,
    gateHpStart: GATE_HP_START,
    unitDefs: UNIT_DEFS,
    towerDefs: TOWER_DEFS,
    towerMaxLevel: TOWER_MAX_LEVEL,
    towerSlots: TOWER_SLOTS.slice(),
  };
}

function clamp01(n) {
  if (n < 0) return 0;
  if (n > 1) return 1;
  return n;
}

function otherSide(side) {
  return side === "bottom" ? "top" : "bottom";
}

function createGame() {
  return {
    tick: 0,
    phase: "playing",
    winner: null,
    incomeTickCounter: 0,
    players: {
      bottom: { gold: START_GOLD, income: START_INCOME, gateHp: GATE_HP_START, towers: makeTowerSlots() },
      top: { gold: START_GOLD, income: START_INCOME, gateHp: GATE_HP_START, towers: makeTowerSlots() },
    },
    units: [],
    nextUnitId: 1,
    projectiles: [],
    nextProjectileId: 1,
  };
}

function applyAction(game, side, action) {
  if (!game || game.phase !== "playing") return { ok: false, reason: "Game not active" };
  if (side !== "top" && side !== "bottom") return { ok: false, reason: "Bad side" };
  if (!action || typeof action.type !== "string") return { ok: false, reason: "Bad action" };
  if (!ALLOWED_ACTION_TYPES.has(action.type)) return { ok: false, reason: "Unknown action type" };

  if (action.type === "spawn_unit") {
    const unitType = String((action.data && action.data.unitType) || "").toLowerCase();
    const def = UNIT_DEFS[unitType];
    if (!def) return { ok: false, reason: "Unknown unitType" };

    const p = game.players[side];
    if (p.gold < def.cost) return { ok: false, reason: "Not enough gold" };

    p.gold -= def.cost;
    p.income += def.income;

    game.units.push({
      id: `u${game.nextUnitId++}`,
      side,
      type: unitType,
      y: side === "top" ? SPAWN_Y_TOP : SPAWN_Y_BOTTOM,
      hp: def.hp,
      maxHp: def.hp,
      atkCd: 0,
    });
    return { ok: true };
  }

  if (action.type === "build_tower") {
    const p = game.players[side];
    const towerType = String((action.data && action.data.towerType) || "").toLowerCase();
    const slot = String((action.data && action.data.slot) || "").toLowerCase();
    if (!TOWER_DEFS[towerType]) return { ok: false, reason: "Unknown towerType" };
    if (!TOWER_SLOTS.includes(slot)) return { ok: false, reason: "Bad tower slot" };
    if (p.towers[slot].type) return { ok: false, reason: "Slot already occupied" };
    if (p.gold < TOWER_DEFS[towerType].cost) return { ok: false, reason: "Not enough gold" };

    p.gold -= TOWER_DEFS[towerType].cost;
    p.towers[slot].type = towerType;
    p.towers[slot].level = 1;
    p.towers[slot].atkCd = 0;
    return { ok: true };
  }

  if (action.type === "upgrade_tower") {
    const p = game.players[side];
    const slot = String((action.data && action.data.slot) || "").toLowerCase();
    if (!TOWER_SLOTS.includes(slot)) return { ok: false, reason: "Bad tower slot" };

    const currentType = p.towers[slot].type;
    const currentLevel = p.towers[slot].level || 0;
    if (!currentType || currentLevel <= 0) return { ok: false, reason: "No tower in slot" };
    if (currentLevel >= TOWER_MAX_LEVEL) return { ok: false, reason: "Tower already maxed" };

    const nextLevel = currentLevel + 1;
    const cost = getTowerUpgradeCost(currentType, nextLevel);
    if (p.gold < cost) return { ok: false, reason: "Not enough gold" };

    p.gold -= cost;
    p.towers[slot].level = nextLevel;
    const stats = getTowerStats(currentType, nextLevel);
    if (p.towers[slot].atkCd > stats.atkCdTicks) p.towers[slot].atkCd = stats.atkCdTicks;
    return { ok: true };
  }

  if (action.type === "sell_tower") {
    return { ok: true, stub: true };
  }

  return { ok: false, reason: "Unknown action type" };
}

function tick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;

  game.incomeTickCounter += 1;
  if (game.incomeTickCounter >= INCOME_INTERVAL_TICKS) {
    game.incomeTickCounter = 0;
    game.players.bottom.gold += game.players.bottom.income;
    game.players.top.gold += game.players.top.income;
  }

  for (const u of game.units) {
    if (u.atkCd > 0) u.atkCd -= 1;
  }
  for (const side of ["bottom", "top"]) {
    for (const slot of TOWER_SLOTS) {
      const t = game.players[side].towers[slot];
      if (t.atkCd > 0) t.atkCd -= 1;
    }
  }

  const alive = game.units.filter((u) => u.hp > 0);
  const bottomFriends = alive.filter((u) => u.side === "bottom").sort((a, b) => a.y - b.y);
  const topFriends = alive.filter((u) => u.side === "top").sort((a, b) => b.y - a.y);

  const deadIds = new Set();
  const goldGains = { bottom: 0, top: 0 };

  function applyTowerAttack(defenderSide, slot) {
    const tower = game.players[defenderSide].towers[slot];
    if (!tower.type || tower.atkCd > 0) return;
    const stats = getTowerStats(tower.type, tower.level || 1);
    if (!stats) return;

    const enemies = alive.filter((u) => u.side !== defenderSide && u.hp > 0 && !deadIds.has(u.id));
    if (enemies.length === 0) return;

    const inRange = defenderSide === "bottom"
      ? enemies.filter((u) => u.y >= 1 - stats.range)
      : enemies.filter((u) => u.y <= stats.range);
    if (inRange.length === 0) return;

    let target = inRange[0];
    if (defenderSide === "bottom") {
      for (const e of inRange) if (e.y > target.y) target = e;
    } else {
      for (const e of inRange) if (e.y < target.y) target = e;
    }

    game.projectiles.push({
      id: `p${game.nextProjectileId++}`,
      side: defenderSide,
      sourceKind: "tower",
      slot,
      projectileType: tower.type,
      damageType: stats.damageType,
      targetKind: "unit",
      targetId: target.id,
      dmg: stats.dmg,
      startX: slotX(slot),
      startY: defenderSide === "bottom" ? GATE_Y_BOTTOM : GATE_Y_TOP,
      targetY: target.y,
      ticksRemaining: stats.projectileTicks,
      ticksTotal: stats.projectileTicks,
    });
    tower.atkCd = stats.atkCdTicks;
  }

  for (const slot of TOWER_SLOTS) {
    applyTowerAttack("bottom", slot);
    applyTowerAttack("top", slot);
  }

  function findNearestEnemyWithinRange(me) {
    const meDef = UNIT_DEFS[me.type];
    const range = meDef.range || COMBAT_RANGE;
    const enemies = alive.filter((u) => u.side !== me.side && u.hp > 0 && !deadIds.has(u.id));
    let best = null;
    let bestDist = Infinity;
    for (const e of enemies) {
      const d = Math.abs(e.y - me.y);
      if (d <= range && d < bestDist) {
        bestDist = d;
        best = e;
      }
    }
    return best;
  }

  function isBlockedByFriend(me) {
    const friends = me.side === "bottom" ? bottomFriends : topFriends;
    const idx = friends.findIndex((u) => u.id === me.id);
    if (idx < 0) return false;
    const ahead = idx > 0 ? friends[idx - 1] : null;
    if (!ahead) return false;
    if (deadIds.has(ahead.id) || ahead.hp <= 0) return false;
    return Math.abs(ahead.y - me.y) < MIN_GAP;
  }

  for (const u of alive) {
    if (deadIds.has(u.id) || u.hp <= 0) continue;
    const def = UNIT_DEFS[u.type];
    const enemyGateY = u.side === "bottom" ? GATE_Y_TOP : GATE_Y_BOTTOM;

    if (u.side === "bottom" && u.y <= GATE_Y_TOP) {
      if (u.atkCd <= 0) {
        game.players.top.gateHp -= def.dmg;
        u.atkCd = def.atkCdTicks;
        if (game.players.top.gateHp <= 0) {
          game.players.top.gateHp = 0;
          game.phase = "ended";
          game.winner = "bottom";
          break;
        }
      }
      continue;
    }

    if (u.side === "top" && u.y >= GATE_Y_BOTTOM) {
      if (u.atkCd <= 0) {
        game.players.bottom.gateHp -= def.dmg;
        u.atkCd = def.atkCdTicks;
        if (game.players.bottom.gateHp <= 0) {
          game.players.bottom.gateHp = 0;
          game.phase = "ended";
          game.winner = "top";
          break;
        }
      }
      continue;
    }

    const target = findNearestEnemyWithinRange(u);
    if (target) {
      if (u.atkCd <= 0) {
        if (def.ranged) {
          game.projectiles.push({
            id: `p${game.nextProjectileId++}`,
            side: u.side,
            sourceKind: "unit",
            projectileType: u.type,
            damageType: def.damageType,
            targetKind: "unit",
            targetId: target.id,
            dmg: def.dmg,
            startX: 0.5,
            startY: u.y,
            targetY: target.y,
            ticksRemaining: def.projectileTicks || 7,
            ticksTotal: def.projectileTicks || 7,
          });
        } else {
          const targetArmor = UNIT_DEFS[target.type].armorType;
          target.hp -= computeDamage(def.dmg, def.damageType, targetArmor);
          if (target.hp <= 0) {
            deadIds.add(target.id);
            goldGains[u.side] += UNIT_DEFS[target.type].bounty;
          }
        }
        u.atkCd = def.atkCdTicks;
      }
      continue;
    }

    const gateDistance = Math.abs(u.y - enemyGateY);
    if (def.ranged && gateDistance <= def.range) {
      if (u.atkCd <= 0) {
        game.projectiles.push({
          id: `p${game.nextProjectileId++}`,
          side: u.side,
          sourceKind: "unit",
          projectileType: u.type,
          damageType: def.damageType,
          targetKind: "gate",
          targetSide: otherSide(u.side),
          dmg: def.dmg,
          startX: 0.5,
          startY: u.y,
          targetY: enemyGateY,
          ticksRemaining: def.projectileTicks || 7,
          ticksTotal: def.projectileTicks || 7,
        });
        u.atkCd = def.atkCdTicks;
      }
      continue;
    }

    if (isBlockedByFriend(u)) {
      // If the unit ahead is already in combat, push through to join the melee
      const friends = u.side === "bottom" ? bottomFriends : topFriends;
      const myIdx = friends.findIndex((f) => f.id === u.id);
      const aheadUnit = myIdx > 0 ? friends[myIdx - 1] : null;
      const aheadFighting = aheadUnit && !deadIds.has(aheadUnit.id) && aheadUnit.hp > 0
        && !!findNearestEnemyWithinRange(aheadUnit);
      if (!aheadFighting) continue; // normal march — hold formation
      // fall through: advance into the combat cluster
    }
    if (u.side === "bottom") u.y -= def.speedPerTick;
    else u.y += def.speedPerTick;
    u.y = clamp01(u.y);
  }

  const stillFlying = [];
  for (const p of game.projectiles) {
    p.ticksRemaining -= 1;
    if (p.ticksRemaining > 0) {
      stillFlying.push(p);
      continue;
    }

    if (p.targetKind === "unit") {
      const target = game.units.find((u) => u.id === p.targetId && u.hp > 0);
      if (!target) continue;
      const targetArmor = UNIT_DEFS[target.type].armorType;
      target.hp -= computeDamage(p.dmg, p.damageType || "NORMAL", targetArmor);
      if (target.hp <= 0) {
        goldGains[p.side] += UNIT_DEFS[target.type].bounty;
      }
      continue;
    }

    if (p.targetKind === "gate") {
      const targetSide = p.targetSide;
      if (targetSide !== "top" && targetSide !== "bottom") continue;
      game.players[targetSide].gateHp -= p.dmg;
      if (game.players[targetSide].gateHp <= 0) {
        game.players[targetSide].gateHp = 0;
        game.phase = "ended";
        game.winner = otherSide(targetSide);
      }
    }
  }
  game.projectiles = stillFlying;

  game.players.bottom.gold += goldGains.bottom;
  game.players.top.gold += goldGains.top;
  game.units = game.units.filter((u) => u.hp > 0);
}

function packTower(t) {
  return { type: t.type, level: t.level || 0 };
}

function createSnapshot(game) {
  return {
    tick: game.tick,
    phase: game.phase,
    winner: game.winner,
    incomeTicksRemaining: INCOME_INTERVAL_TICKS - game.incomeTickCounter,
    players: {
      bottom: {
        gold: game.players.bottom.gold,
        income: game.players.bottom.income,
        gateHp: game.players.bottom.gateHp,
        towers: {
          left_outer: packTower(game.players.bottom.towers.left_outer),
          left_mid: packTower(game.players.bottom.towers.left_mid),
          left_inner: packTower(game.players.bottom.towers.left_inner),
          right_inner: packTower(game.players.bottom.towers.right_inner),
          right_mid: packTower(game.players.bottom.towers.right_mid),
          right_outer: packTower(game.players.bottom.towers.right_outer),
        },
      },
      top: {
        gold: game.players.top.gold,
        income: game.players.top.income,
        gateHp: game.players.top.gateHp,
        towers: {
          left_outer: packTower(game.players.top.towers.left_outer),
          left_mid: packTower(game.players.top.towers.left_mid),
          left_inner: packTower(game.players.top.towers.left_inner),
          right_inner: packTower(game.players.top.towers.right_inner),
          right_mid: packTower(game.players.top.towers.right_mid),
          right_outer: packTower(game.players.top.towers.right_outer),
        },
      },
    },
    units: game.units.map((u) => ({
      id: u.id,
      side: u.side,
      type: u.type,
      y: u.y,
      hp: u.hp,
      maxHp: u.maxHp,
    })),
    projectiles: game.projectiles.map((p) => {
      const progress = 1 - (p.ticksRemaining / p.ticksTotal);
      return {
        id: p.id,
        side: p.side,
        slot: p.slot,
        sourceKind: p.sourceKind,
        projectileType: p.projectileType,
        damageType: p.damageType,
        x: p.startX,
        y: p.startY + (p.targetY - p.startY) * progress,
      };
    }),
  };
}

module.exports = {
  TICK_HZ,
  TICK_MS,
  UNIT_DEFS,
  TOWER_DEFS,
  createPublicConfig,
  createGame,
  applyAction,
  tick,
  createSnapshot,
};
