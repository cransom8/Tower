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
const LIVES_START = 20;
const ALLOWED_ACTION_TYPES = new Set([
  "spawn_unit",
  "build_tower",
  "upgrade_tower",
  "sell_tower",
  "upgrade_barracks",
  "set_autosend",
  "set_tower_target",
]);
const TOWER_TARGET_MODES = new Set(["first", "last", "weakest", "strongest"]);
const TOWER_SLOTS = ["left_outer", "left_mid", "left_inner", "right_inner", "right_mid", "right_outer"];
const TOWER_MAX_LEVEL = 10;
const AUTOSEND_INTERVAL_TICKS = 1; // instant cadence (every tick)
const AUTOSEND_BURST_MAX_PURCHASES = 20000;
const BARRACKS_COST_BASE = 100;
const BARRACKS_REQ_INCOME_BASE = 8;

// Combat + spacing (normalized lane units)
const COMBAT_RANGE = 0.045; // if enemy within this y-distance, you can attack
const GATE_Y_TOP = 0.05;
const GATE_Y_BOTTOM = 0.95;

// Spawn positions
const SPAWN_Y_TOP = 0.12;
const SPAWN_Y_BOTTOM = 0.88;

const MARCH_SPEED = 0.00129375; // shared march speed — only runner differs

const UNIT_DEFS = {
  runner:   { cost: 8,  income: 0.5, hp: 60,  dmg: 7,  atkCdTicks: 7,  speedPerTick: 0.00215625,  bounty: 2, range: COMBAT_RANGE, ranged: false, armorType: "UNARMORED", damageType: "NORMAL" },
  footman:  { cost: 10, income: 1,   hp: 90,  dmg: 8,  atkCdTicks: 8,  speedPerTick: MARCH_SPEED,  bounty: 3, range: COMBAT_RANGE, ranged: false, armorType: "MEDIUM",    damageType: "NORMAL" },
  ironclad: { cost: 16, income: 2,   hp: 160, dmg: 9,  atkCdTicks: 10, speedPerTick: MARCH_SPEED,  bounty: 4, range: COMBAT_RANGE, ranged: false, armorType: "HEAVY",     damageType: "NORMAL" },
  warlock:  { cost: 18, income: 2,   hp: 80,  dmg: 12, atkCdTicks: 11, speedPerTick: MARCH_SPEED,  bounty: 5, range: 0.18,         ranged: true,  projectileTicks: 8, armorType: "MAGIC",     damageType: "MAGIC"  },
  golem:    { cost: 25, income: 3,   hp: 240, dmg: 14, atkCdTicks: 13, speedPerTick: 0.00090563,   bounty: 6, range: COMBAT_RANGE, ranged: false, armorType: "HEAVY",     damageType: "NORMAL" },
};
const AUTOSEND_PRIORITY = Object.keys(UNIT_DEFS).sort((a, b) => UNIT_DEFS[b].cost - UNIT_DEFS[a].cost);

const TOWER_DEFS = {
  archer: { cost: 10, range: 0.36, dmg: 6.6, atkCdTicks: 12, projectileTicks: 7, damageType: "PIERCE" },
  fighter: { cost: 12, range: 0.22, dmg: 8.8, atkCdTicks: 11, projectileTicks: 6, damageType: "NORMAL" },
  cannon: { cost: 30, range: 0.32, dmg: 13.44, atkCdTicks: 16, projectileTicks: 9, damageType: "SPLASH" },
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
    left_outer: { type: null, level: 0, atkCd: 0, targetMode: "first" },
    left_mid: { type: null, level: 0, atkCd: 0, targetMode: "first" },
    left_inner: { type: null, level: 0, atkCd: 0, targetMode: "first" },
    right_inner: { type: null, level: 0, atkCd: 0, targetMode: "first" },
    right_mid: { type: null, level: 0, atkCd: 0, targetMode: "first" },
    right_outer: { type: null, level: 0, atkCd: 0, targetMode: "first" },
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

function getBarracksLevelDef(level) {
  const lvl = Math.max(1, Math.floor(Number(level) || 1));
  if (lvl === 1) {
    return {
      hpMult: 1,
      dmgMult: 1,
      speedMult: 1,
      incomeBonus: 0,
      cost: 0,
      reqIncome: 0,
    };
  }
  const statMult = Math.pow(2, lvl - 1);
  const gateMult = Math.pow(2, lvl - 2);
  return {
    hpMult: statMult,
    dmgMult: statMult,
    speedMult: statMult,
    incomeBonus: 0,
    cost: Math.ceil(BARRACKS_COST_BASE * gateMult),
    reqIncome: Math.ceil(BARRACKS_REQ_INCOME_BASE * gateMult),
  };
}

function clampNum(v, min, max, fallback) {
  const n = Number(v);
  if (!Number.isFinite(n)) return fallback;
  return Math.max(min, Math.min(max, n));
}

function normalizeGameOptions(options) {
  const src = options && typeof options === "object" ? options : {};
  return {
    startGold: Math.floor(clampNum(src.startGold, 0, 10000, START_GOLD)),
    startIncome: clampNum(src.startIncome, 0, 1000, START_INCOME),
  };
}

function createPublicConfig(options) {
  const opt = normalizeGameOptions(options);
  return {
    tickHz: TICK_HZ,
    incomeIntervalTicks: INCOME_INTERVAL_TICKS,
    startGold: opt.startGold,
    startIncome: opt.startIncome,
    livesStart: LIVES_START,
    unitDefs: UNIT_DEFS,
    towerDefs: TOWER_DEFS,
    towerMaxLevel: TOWER_MAX_LEVEL,
    towerSlots: TOWER_SLOTS.slice(),
    barracksInfinite: true,
    barracksCostBase: BARRACKS_COST_BASE,
    barracksReqIncomeBase: BARRACKS_REQ_INCOME_BASE,
    barracksLevels: [],
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

function createGame(options) {
  const opt = normalizeGameOptions(options);
  const baseBarracks = getBarracksLevelDef(1);
  const createAutosend = () => ({
    enabled: false,
    enabledUnits: Object.fromEntries(Object.keys(UNIT_DEFS).map((k) => [k, false])),
    rate: "normal",
    tickCounter: 0,
  });
  return {
    tick: 0,
    phase: "playing",
    winner: null,
    incomeTickCounter: 0,
    players: {
      bottom: {
        gold: opt.startGold,
        income: opt.startIncome,
        incomeRemainder: 0,
        lives: LIVES_START,
        towers: makeTowerSlots(),
        barracks: { level: 1, hpMult: baseBarracks.hpMult, dmgMult: baseBarracks.dmgMult, speedMult: baseBarracks.speedMult, incomeBonus: baseBarracks.incomeBonus },
        autosend: createAutosend(),
      },
      top: {
        gold: opt.startGold,
        income: opt.startIncome,
        incomeRemainder: 0,
        lives: LIVES_START,
        towers: makeTowerSlots(),
        barracks: { level: 1, hpMult: baseBarracks.hpMult, dmgMult: baseBarracks.dmgMult, speedMult: baseBarracks.speedMult, incomeBonus: baseBarracks.incomeBonus },
        autosend: createAutosend(),
      },
    },
    units: [],
    nextUnitId: 1,
    projectiles: [],
    nextProjectileId: 1,
  };
}

function spawnUnitForSide(game, side, unitType) {
  const p = game.players[side];
  const def = UNIT_DEFS[unitType];
  if (!p || !def) return { ok: false, reason: "Unknown unitType" };
  if (p.gold < def.cost) return { ok: false, reason: "Not enough gold" };

  const br = p.barracks || getBarracksLevelDef(1);
  p.gold -= def.cost;
  p.income += def.income + (br.incomeBonus || 0);

  game.units.push({
    id: `u${game.nextUnitId++}`,
    side,
    type: unitType,
    y: side === "top" ? SPAWN_Y_TOP : SPAWN_Y_BOTTOM,
    hp: Math.ceil(def.hp * (br.hpMult || 1)),
    maxHp: Math.ceil(def.hp * (br.hpMult || 1)),
    baseDmg: def.dmg * (br.dmgMult || 1),
    baseSpeed: def.speedPerTick * (br.speedMult || 1),
    atkCd: 0,
  });
  return { ok: true };
}

function runAutosendBurst(game, side) {
  const p = game.players[side];
  if (!p || !p.autosend || !p.autosend.enabled) return 0;
  const enabledUnits = p.autosend.enabledUnits || {};
  const priority = AUTOSEND_PRIORITY.filter((ut) => !!enabledUnits[ut]);
  if (priority.length === 0) return 0;
  let purchases = 0;
  while (purchases < AUTOSEND_BURST_MAX_PURCHASES) {
    let bought = false;
    for (const ut of priority) {
      const def = UNIT_DEFS[ut];
      if (!def) continue;
      if (p.gold < def.cost) continue;
      const res = spawnUnitForSide(game, side, ut);
      if (!res.ok) continue;
      purchases += 1;
      bought = true;
      break; // restart from highest-cost option
    }
    if (!bought) break;
  }
  return purchases;
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
    return spawnUnitForSide(game, side, unitType);
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
    p.towers[slot].targetMode = "first";
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

  if (action.type === "upgrade_barracks") {
    const p = game.players[side];
    const currentLevel = (p.barracks && p.barracks.level) || 1;
    const nextLevel = currentLevel + 1;
    const nextDef = getBarracksLevelDef(nextLevel);
    if (p.income < nextDef.reqIncome) return { ok: false, reason: `Need ${nextDef.reqIncome}g income` };
    if (p.gold < nextDef.cost) return { ok: false, reason: "Not enough gold" };

    p.gold -= nextDef.cost;
    p.barracks = {
      level: nextLevel,
      hpMult: nextDef.hpMult,
      dmgMult: nextDef.dmgMult,
      speedMult: nextDef.speedMult,
      incomeBonus: nextDef.incomeBonus || 0,
    };
    return { ok: true };
  }

  if (action.type === "set_autosend") {
    const as = game.players[side].autosend;
    const { enabled, enabledUnits, rate } = action.data || {};
    if (typeof enabled === "boolean") {
      as.enabled = enabled;
    }
    if (enabledUnits && typeof enabledUnits === "object") {
      for (const ut of Object.keys(UNIT_DEFS)) {
        as.enabledUnits[ut] = !!enabledUnits[ut];
      }
    }
    if (rate === "slow" || rate === "normal" || rate === "fast") as.rate = rate;
    as.tickCounter = 0;
    if (as.enabled) runAutosendBurst(game, side); // instant on-toggle spend
    return { ok: true };
  }
  
  if (action.type === "set_tower_target") {
    const p = game.players[side];
    const slot = String((action.data && action.data.slot) || "").toLowerCase();
    const targetMode = String((action.data && action.data.targetMode) || "").toLowerCase();
    if (!TOWER_SLOTS.includes(slot)) return { ok: false, reason: "Bad tower slot" };
    const tower = p.towers[slot];
    if (!tower || !tower.type) return { ok: false, reason: "No tower in slot" };
    if (!TOWER_TARGET_MODES.has(targetMode)) return { ok: false, reason: "Bad target mode" };
    tower.targetMode = targetMode;
    return { ok: true };
  }

  return { ok: false, reason: "Unknown action type" };
}

function tick(game) {
  if (!game || game.phase !== "playing") return;
  game.tick += 1;

  game.incomeTickCounter += 1;
  if (game.incomeTickCounter >= INCOME_INTERVAL_TICKS) {
    game.incomeTickCounter = 0;
    for (const side of ["bottom", "top"]) {
      const p = game.players[side];
      p.gold += p.income;
    }
  }

  for (const side of ["bottom", "top"]) {
    const p = game.players[side];
    const as = p.autosend;
    if (!as || !as.enabled) continue;
    as.tickCounter = 0;
    runAutosendBurst(game, side);
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
  const deadIds = new Set();
  const goldGains = { bottom: 0, top: 0 };
  
  function pickTowerTarget(defenderSide, inRange, targetMode) {
    const mode = TOWER_TARGET_MODES.has(targetMode) ? targetMode : "first";
    let target = inRange[0];
    if (mode === "strongest") {
      for (const e of inRange) {
        if (e.hp > target.hp) target = e;
      }
      return target;
    }
    if (mode === "weakest") {
      for (const e of inRange) {
        if (e.hp < target.hp) target = e;
      }
      return target;
    }
    if (mode === "last") {
      if (defenderSide === "bottom") {
        for (const e of inRange) if (e.y < target.y) target = e;
      } else {
        for (const e of inRange) if (e.y > target.y) target = e;
      }
      return target;
    }
    if (defenderSide === "bottom") {
      for (const e of inRange) if (e.y > target.y) target = e;
    } else {
      for (const e of inRange) if (e.y < target.y) target = e;
    }
    return target;
  }

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
    const target = pickTowerTarget(defenderSide, inRange, tower.targetMode);

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

  for (const u of alive) {
    if (deadIds.has(u.id) || u.hp <= 0) continue;
    const def = UNIT_DEFS[u.type];
    const enemyGateY = u.side === "bottom" ? GATE_Y_TOP : GATE_Y_BOTTOM;

    if (u.side === "bottom" && u.y <= GATE_Y_TOP) {
      game.players.top.lives -= 1;
      deadIds.add(u.id);
      if (game.players.top.lives <= 0) {
        game.players.top.lives = 0;
        game.phase = "ended";
        game.winner = "bottom";
        break;
      }
      continue;
    }

    if (u.side === "top" && u.y >= GATE_Y_BOTTOM) {
      game.players.bottom.lives -= 1;
      deadIds.add(u.id);
      if (game.players.bottom.lives <= 0) {
        game.players.bottom.lives = 0;
        game.phase = "ended";
        game.winner = "top";
        break;
      }
      continue;
    }

    const target = findNearestEnemyWithinRange(u);
    if (target) {
      if (u.atkCd <= 0) {
        const unitDmg = Number.isFinite(u.baseDmg) ? u.baseDmg : def.dmg;
        if (def.ranged) {
          game.projectiles.push({
            id: `p${game.nextProjectileId++}`,
            side: u.side,
            sourceKind: "unit",
            projectileType: u.type,
            damageType: def.damageType,
            targetKind: "unit",
            targetId: target.id,
            dmg: unitDmg,
            startX: 0.5,
            startY: u.y,
            targetY: target.y,
            ticksRemaining: def.projectileTicks || 7,
            ticksTotal: def.projectileTicks || 7,
          });
        } else {
          const targetArmor = UNIT_DEFS[target.type].armorType;
          target.hp -= computeDamage(unitDmg, def.damageType, targetArmor);
          if (target.hp <= 0) {
            deadIds.add(target.id);
            goldGains[u.side] += UNIT_DEFS[target.type].bounty;
          }
        }
        u.atkCd = def.atkCdTicks;
      }
      continue;
    }

    const unitSpeed = Number.isFinite(u.baseSpeed) ? u.baseSpeed : def.speedPerTick;
    if (u.side === "bottom") u.y -= unitSpeed;
    else u.y += unitSpeed;
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

  }
  game.projectiles = stillFlying;

  game.players.bottom.gold += goldGains.bottom;
  game.players.top.gold += goldGains.top;
  game.units = game.units.filter((u) => u.hp > 0);
}

function packTower(t) {
  return { type: t.type, level: t.level || 0, targetMode: t.targetMode || "first" };
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
        lives: game.players.bottom.lives,
        barracksLevel: (game.players.bottom.barracks && game.players.bottom.barracks.level) || 1,
        autosend: game.players.bottom.autosend ? {
          enabled: !!game.players.bottom.autosend.enabled,
          enabledUnits: Object.assign({}, game.players.bottom.autosend.enabledUnits || {}),
          rate: game.players.bottom.autosend.rate || "normal",
          tickCounter: Number(game.players.bottom.autosend.tickCounter) || 0,
        } : {
          enabled: false,
          enabledUnits: {},
          rate: "normal",
          tickCounter: 0,
        },
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
        lives: game.players.top.lives,
        barracksLevel: (game.players.top.barracks && game.players.top.barracks.level) || 1,
        autosend: game.players.top.autosend ? {
          enabled: !!game.players.top.autosend.enabled,
          enabledUnits: Object.assign({}, game.players.top.autosend.enabledUnits || {}),
          rate: game.players.top.autosend.rate || "normal",
          tickCounter: Number(game.players.top.autosend.tickCounter) || 0,
        } : {
          enabled: false,
          enabledUnits: {},
          rate: "normal",
          tickCounter: 0,
        },
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
