"use strict";

require("dotenv").config();

const db = require("../db");

const DEFAULT_WAVE_TIMER_TICKS = 2400;
const DEFAULT_WAVE_GROUP_INTERVAL_TICKS = 600;
const DEFAULT_GROUPS_PER_WAVE = Math.max(1, Math.ceil(DEFAULT_WAVE_TIMER_TICKS / DEFAULT_WAVE_GROUP_INTERVAL_TICKS));

function n(value, fallback = 0) {
  const numeric = Number(value);
  return Number.isFinite(numeric) ? numeric : fallback;
}

function round(value, digits = 2) {
  const factor = 10 ** digits;
  return Math.round(n(value) * factor) / factor;
}

function pctDelta(current, previous) {
  if (!Number.isFinite(previous) || previous === 0) return null;
  return round(((current - previous) / previous) * 100, 2);
}

function unitIndex({ hp, damage, attackSpeed, pathSpeed }) {
  return hp + (damage * attackSpeed * 10) + (pathSpeed * 18);
}

function describeUnitIndex(unit, hpMult = 1, dmgMult = 1, speedMult = 1) {
  const hp = n(unit.hp) * hpMult;
  const damage = n(unit.attack_damage) * dmgMult;
  const attackSpeed = n(unit.attack_speed, 1);
  const pathSpeed = n(unit.path_speed) * speedMult;
  return round(unitIndex({ hp, damage, attackSpeed, pathSpeed }), 2);
}

function summarizeTopUnits(units, barracksMaxMultiplier) {
  const enriched = units.map((unit) => {
    const baseIndex = describeUnitIndex(unit);
    const lateIndex = describeUnitIndex(
      unit,
      unit.barracks_scales_hp ? barracksMaxMultiplier : 1,
      unit.barracks_scales_dmg ? barracksMaxMultiplier : 1,
      1
    );
    const buildCost = Math.max(1, n(unit.build_cost, 1));
    return {
      key: unit.key,
      name: unit.name,
      buildCost,
      baseIndex,
      lateIndex,
      basePerGold: round(baseIndex / buildCost, 2),
      latePerGold: round(lateIndex / buildCost, 2),
      scalesHp: !!unit.barracks_scales_hp,
      scalesDmg: !!unit.barracks_scales_dmg,
    };
  });

  const byBase = [...enriched].sort((a, b) => b.basePerGold - a.basePerGold).slice(0, 5);
  const byLate = [...enriched].sort((a, b) => b.latePerGold - a.latePerGold).slice(0, 5);
  return { byBase, byLate };
}

function buildWavePressureRows(waves, waveUnits) {
  return waves.map((wave) => {
    const unit = waveUnits.get(wave.unit_type);
    const perMobIndex = describeUnitIndex(unit, n(wave.hp_mult, 1), n(wave.dmg_mult, 1), n(wave.speed_mult, 1));
    const perPulsePressure = round(perMobIndex * n(wave.spawn_qty, 0), 2);
    const perWavePressure = round(perPulsePressure * DEFAULT_GROUPS_PER_WAVE, 2);
    return {
      waveNumber: n(wave.wave_number, 0),
      unitType: wave.unit_type,
      spawnQty: n(wave.spawn_qty, 0),
      perMobIndex,
      perPulsePressure,
      perWavePressure,
      hpMult: n(wave.hp_mult, 1),
      dmgMult: n(wave.dmg_mult, 1),
      speedMult: n(wave.speed_mult, 1),
    };
  }).map((row, index, rows) => ({
    ...row,
    deltaVsPreviousWavePct: index > 0 ? pctDelta(row.perWavePressure, rows[index - 1].perWavePressure) : null,
  }));
}

function findBackslides(rows) {
  return rows.filter((row) => row.deltaVsPreviousWavePct != null && row.deltaVsPreviousWavePct <= -5);
}

function findFlatSegments(rows) {
  return rows.filter((row) => row.deltaVsPreviousWavePct != null && row.deltaVsPreviousWavePct >= -5 && row.deltaVsPreviousWavePct <= 5);
}

async function loadState() {
  const [matches, configs, barracksLevels, waveConfig, unitTypes] = await Promise.all([
    db.query(`
      SELECT
        COUNT(*) FILTER (WHERE balance_summary IS NOT NULL)::int AS completed_reports,
        COUNT(*) FILTER (WHERE ended_at IS NULL)::int AS open_matches,
        COUNT(*)::int AS total_matches
      FROM matches
    `),
    db.query(`
      SELECT config_json
      FROM game_configs
      WHERE mode = 'multilane' AND is_active = TRUE
      ORDER BY id DESC
      LIMIT 1
    `),
    db.query(`
      SELECT level, multiplier, upgrade_cost
      FROM barracks_levels
      ORDER BY level ASC
    `),
    db.query(`
      SELECT w.wave_number, w.unit_type, w.spawn_qty, w.hp_mult, w.dmg_mult, w.speed_mult
      FROM ml_waves w
      JOIN ml_wave_configs c ON c.id = w.config_id
      WHERE c.is_default = TRUE
      ORDER BY w.wave_number ASC
    `),
    db.query(`
      SELECT
        key, name, usage_scope, build_cost, send_cost, income, hp, attack_damage,
        attack_speed, range, path_speed, barracks_scales_hp, barracks_scales_dmg,
        display_to_players, enabled
      FROM unit_types
      WHERE enabled = TRUE
    `),
  ]);

  return {
    reportState: matches.rows[0] || { completed_reports: 0, open_matches: 0, total_matches: 0 },
    activeConfig: configs.rows[0] ? configs.rows[0].config_json : null,
    barracksLevels: barracksLevels.rows,
    waves: waveConfig.rows,
    unitTypes: unitTypes.rows,
  };
}

function printSection(title) {
  console.log(`\n=== ${title} ===`);
}

function printKeyValue(label, value) {
  console.log(`${label}: ${value}`);
}

async function main() {
  const { reportState, activeConfig, barracksLevels, waves, unitTypes } = await loadState();
  const globalParams = activeConfig && activeConfig.globalParams ? activeConfig.globalParams : {};
  const startGold = n(globalParams.startGold, 0);
  const startIncome = n(globalParams.startIncome, 0);
  const effectiveStartingGold = startGold + startIncome;
  const teamHpStart = n(globalParams.teamHpStart, 0);
  const transitionPhaseTicks = n(globalParams.transitionPhaseTicks, 0);
  const waveUnits = new Map(unitTypes.filter((unit) => unit.usage_scope === "wave_only").map((unit) => [unit.key, unit]));
  const playerUnits = unitTypes.filter((unit) => unit.usage_scope === "loadout_only");
  const barracksMaxMultiplier = barracksLevels.length > 0
    ? Math.max(...barracksLevels.map((entry) => n(entry.multiplier, 1)))
    : 1;

  const waveRows = buildWavePressureRows(waves, waveUnits);
  const backslides = findBackslides(waveRows);
  const flatSegments = findFlatSegments(waveRows);
  const flatOnly = flatSegments.filter((row) => row.deltaVsPreviousWavePct != null && row.deltaVsPreviousWavePct > -5);
  const topUnits = summarizeTopUnits(playerUnits, barracksMaxMultiplier);

  printSection("Persisted Reports");
  printKeyValue("Completed balance reports", reportState.completed_reports);
  printKeyValue("Open matches without final reports", reportState.open_matches);
  printKeyValue("Total matches in DB", reportState.total_matches);
  if (n(reportState.completed_reports, 0) === 0) {
    printKeyValue("Status", "No persisted balance output exists yet; this audit is based on authored data and live DB balance tables.");
  }

  printSection("Active Economy");
  printKeyValue("Configured startGold", startGold);
  printKeyValue("Configured startIncome", startIncome);
  printKeyValue("Effective starting lane gold", effectiveStartingGold);
  printKeyValue("Configured teamHpStart", teamHpStart);
  printKeyValue("Transition phase ticks", transitionPhaseTicks);
  printKeyValue("Assumed wave groups per round", DEFAULT_GROUPS_PER_WAVE);

  printSection("Barracks Multipliers");
  for (const level of barracksLevels) {
    console.log(
      `L${level.level}: x${round(level.multiplier, 2)} stats for ${n(level.upgrade_cost, 0)} gold`
    );
  }

  printSection("Top Human Efficiency");
  console.log("Best base efficiency per gold:");
  for (const unit of topUnits.byBase) {
    console.log(
      `- ${unit.name} (${unit.key}) cost ${unit.buildCost}, base/gold ${unit.basePerGold}, late/gold ${unit.latePerGold}, scales hp=${unit.scalesHp} dmg=${unit.scalesDmg}`
    );
  }
  console.log("Best late efficiency per gold:");
  for (const unit of topUnits.byLate) {
    console.log(
      `- ${unit.name} (${unit.key}) cost ${unit.buildCost}, base/gold ${unit.basePerGold}, late/gold ${unit.latePerGold}, scales hp=${unit.scalesHp} dmg=${unit.scalesDmg}`
    );
  }

  printSection("Wave Pressure Curve");
  for (const row of waveRows) {
    const delta = row.deltaVsPreviousWavePct == null ? "n/a" : `${row.deltaVsPreviousWavePct}%`;
    console.log(
      `W${row.waveNumber}: ${row.unitType} x${row.spawnQty}, wavePressure ${row.perWavePressure}, delta ${delta}`
    );
  }

  printSection("Wave Weak Spots");
  if (backslides.length <= 0) {
    console.log("No large backslides detected.");
  } else {
    for (const row of backslides) {
      console.log(
        `- Wave ${row.waveNumber} drops ${row.deltaVsPreviousWavePct}% from wave ${row.waveNumber - 1} (${row.unitType}, pressure ${row.perWavePressure})`
      );
    }
  }

  printSection("Wave Flat Spots");
  if (flatOnly.length <= 0) {
    console.log("No flat segments detected.");
  } else {
    for (const row of flatOnly) {
      console.log(
        `- Wave ${row.waveNumber} only changes ${row.deltaVsPreviousWavePct}% from wave ${row.waveNumber - 1} (${row.unitType}, pressure ${row.perWavePressure})`
      );
    }
  }

  printSection("Likely Tuning Targets");
  if (backslides.length > 0) {
    console.log("- Large authored pressure backslides still exist and should be removed before fine-tuning anything else.");
  } else {
    console.log("- No large authored pressure backslides remain in the default curve.");
  }
  if (flatOnly.length > 0) {
    console.log(`- Remaining flat spots (${flatOnly.map((row) => row.waveNumber).join(", ")}) are the next places to tune if players still stabilize too early.`);
  } else {
    console.log("- The authored wave curve is consistently climbing; remaining difficulty problems will be more about player economy and scaling than raw wave shape.");
  }
  console.log("- The active mode starts each lane with startGold + startIncome, so the current config opens at 210 gold per lane before the first wave.");
  console.log("- Most human units gain barracks HP scaling, and several mounted/mage late units gain both HP and damage scaling, so player efficiency rises faster than the wave table expects.");
  console.log("- Town Core, barracks, and first branch unlocks are cheap enough that the player reaches scaling systems early while team HP is still very forgiving.");

  printSection("Recommended First Pass");
  if (backslides.length > 0) {
    console.log("- Remove the remaining backslides before touching player scaling. Wave shape comes first.");
  } else {
    console.log("- The next biggest lever is reducing early player cushion: lower startGold, lower effective starting gold, or raise early build friction before branch unlocks.");
  }
  if (flatOnly.length > 0) {
    console.log(`- If dungeon pressure is still too soft, raise the flatter authored rounds next: ${flatOnly.map((row) => row.waveNumber).join(", ")}.`);
  }
  console.log("- Consider softening player compounding by delaying cheap barracks upgrades or reducing how quickly tier-2/tier-3 branch unlocks come online.");
  console.log("- Finish or terminate at least one local match so the balance-report pipeline writes wave_stats and balance_summary; that will let future tuning use actual play outputs instead of authored proxies.");
}

main()
  .catch((err) => {
    console.error(err && err.stack ? err.stack : err);
    process.exitCode = 1;
  })
  .finally(async () => {
    await db.pool.end().catch(() => {});
  });
