"use strict";

const fs = require("fs");
const path = require("path");

const LOCKED_WAVE_PLAN = Object.freeze([
  { wave_number: 1,  unit_type: "giant_rat",       spawn_qty: 12, hp_mult: 0.95, dmg_mult: 0.95, speed_mult: 1.00, is_boss: false, note: "starter swarm" },
  { wave_number: 2,  unit_type: "kobold",          spawn_qty: 14, hp_mult: 1.00, dmg_mult: 1.00, speed_mult: 1.03, is_boss: false, note: "fast pressure" },
  { wave_number: 3,  unit_type: "goblin",          spawn_qty: 15, hp_mult: 1.05, dmg_mult: 1.02, speed_mult: 1.00, is_boss: false, note: "baseline melee" },
  { wave_number: 4,  unit_type: "fantasy_wolf",    spawn_qty: 12, hp_mult: 1.10, dmg_mult: 1.08, speed_mult: 1.05, is_boss: false, note: "early spike" },
  { wave_number: 5,  unit_type: "wyvern",          spawn_qty: 1,  hp_mult: 1.15, dmg_mult: 1.12, speed_mult: 1.00, is_boss: true,  note: "locked boss" },
  { wave_number: 6,  unit_type: "ghoul",           spawn_qty: 14, hp_mult: 1.20, dmg_mult: 1.12, speed_mult: 1.00, is_boss: false, note: "durable fodder" },
  { wave_number: 7,  unit_type: "hobgoblin",       spawn_qty: 14, hp_mult: 1.25, dmg_mult: 1.18, speed_mult: 1.00, is_boss: false, note: "armored infantry" },
  { wave_number: 8,  unit_type: "lizard_warrior",  spawn_qty: 13, hp_mult: 1.30, dmg_mult: 1.22, speed_mult: 1.02, is_boss: false, note: "agile melee" },
  { wave_number: 9,  unit_type: "darkness_spider", spawn_qty: 13, hp_mult: 1.35, dmg_mult: 1.26, speed_mult: 1.04, is_boss: false, note: "ranged venom pressure" },
  { wave_number: 10, unit_type: "demon_lord",      spawn_qty: 1,  hp_mult: 1.45, dmg_mult: 1.40, speed_mult: 1.00, is_boss: true,  note: "locked boss" },
  { wave_number: 11, unit_type: "giant_viper",     spawn_qty: 13, hp_mult: 1.45, dmg_mult: 1.30, speed_mult: 1.06, is_boss: false, note: "pierce threat" },
  { wave_number: 12, unit_type: "undead_warrior",  spawn_qty: 12, hp_mult: 1.55, dmg_mult: 1.34, speed_mult: 1.00, is_boss: false, note: "slow wall" },
  { wave_number: 13, unit_type: "orc",             spawn_qty: 12, hp_mult: 1.65, dmg_mult: 1.40, speed_mult: 1.00, is_boss: false, note: "midgame bruiser" },
  { wave_number: 14, unit_type: "skeleton_knight", spawn_qty: 11, hp_mult: 1.75, dmg_mult: 1.48, speed_mult: 1.00, is_boss: false, note: "heavy armor check" },
  { wave_number: 15, unit_type: "oak_tree_ent",    spawn_qty: 1,  hp_mult: 1.85, dmg_mult: 1.55, speed_mult: 0.95, is_boss: true,  note: "team pick boss: giant tank" },
  { wave_number: 16, unit_type: "harpy",           spawn_qty: 12, hp_mult: 1.85, dmg_mult: 1.52, speed_mult: 1.10, is_boss: false, note: "fast ranged harass" },
  { wave_number: 17, unit_type: "troll",           spawn_qty: 10, hp_mult: 1.95, dmg_mult: 1.60, speed_mult: 1.00, is_boss: false, note: "regen bruiser" },
  { wave_number: 18, unit_type: "mummy",           spawn_qty: 10, hp_mult: 2.05, dmg_mult: 1.68, speed_mult: 0.98, is_boss: false, note: "durability ramp" },
  { wave_number: 19, unit_type: "dragonide",       spawn_qty: 9,  hp_mult: 2.15, dmg_mult: 1.78, speed_mult: 1.00, is_boss: false, note: "elite reptilian" },
  { wave_number: 20, unit_type: "chimera",         spawn_qty: 1,  hp_mult: 2.30, dmg_mult: 1.95, speed_mult: 1.00, is_boss: true,  note: "team pick boss: magical beast" },
  { wave_number: 21, unit_type: "werewolf",        spawn_qty: 9,  hp_mult: 2.35, dmg_mult: 1.95, speed_mult: 1.08, is_boss: false, note: "feral speed check" },
  { wave_number: 22, unit_type: "ogre",            spawn_qty: 8,  hp_mult: 2.50, dmg_mult: 2.05, speed_mult: 0.98, is_boss: false, note: "slow heavy hits" },
  { wave_number: 23, unit_type: "vampire",         spawn_qty: 8,  hp_mult: 2.65, dmg_mult: 2.15, speed_mult: 1.02, is_boss: false, note: "life drain elite" },
  { wave_number: 24, unit_type: "evil_watcher",    spawn_qty: 7,  hp_mult: 2.80, dmg_mult: 2.25, speed_mult: 1.00, is_boss: false, note: "magic artillery" },
  { wave_number: 25, unit_type: "mountain_dragon", spawn_qty: 1,  hp_mult: 3.00, dmg_mult: 2.50, speed_mult: 1.00, is_boss: true,  note: "locked boss" },
  { wave_number: 26, unit_type: "griffin",         spawn_qty: 7,  hp_mult: 3.05, dmg_mult: 2.45, speed_mult: 1.05, is_boss: false, note: "post-boss elite flier" },
  { wave_number: 27, unit_type: "cyclops",         spawn_qty: 6,  hp_mult: 3.20, dmg_mult: 2.60, speed_mult: 0.98, is_boss: false, note: "siege bruiser" },
  { wave_number: 28, unit_type: "manticora",       spawn_qty: 5,  hp_mult: 3.35, dmg_mult: 2.75, speed_mult: 1.00, is_boss: false, note: "ranged monster" },
  { wave_number: 29, unit_type: "ice_golem",       spawn_qty: 4,  hp_mult: 3.55, dmg_mult: 2.95, speed_mult: 0.96, is_boss: false, note: "final tank check" },
  { wave_number: 30, unit_type: "hydra",           spawn_qty: 1,  hp_mult: 4.50, dmg_mult: 4.00, speed_mult: 1.00, is_boss: true,  note: "final spike; Dragonhide placeholder until confirmed" },
]);

const TT_UNIT_ROSTER = Object.freeze([
  { skin_key: "tt_peasant",         name: "Peasant",          unit_type: "tt_peasant",         role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_scout",           name: "Scout",            unit_type: "tt_scout",           role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_settler",         name: "Settler",          unit_type: "tt_settler",         role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_light_infantry",  name: "Light Infantry",   unit_type: "tt_light_infantry",  role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_spearman",        name: "Spearman",         unit_type: "tt_spearman",        role: "Melee w/ extended reach", expects_projectile: false },
  { skin_key: "tt_archer",          name: "Archer",           unit_type: "tt_archer",          role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_crossbowman",     name: "Crossbowman",      unit_type: "tt_crossbowman",     role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_heavy_infantry",  name: "Heavy Infantry",   unit_type: "tt_heavy_infantry",  role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_halberdier",      name: "Halberdier",       unit_type: "tt_halberdier",      role: "Melee w/ extended reach", expects_projectile: false },
  { skin_key: "tt_heavy_swordman",  name: "Heavy Swordsman",  unit_type: "tt_heavy_swordman",  role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_light_cavalry",   name: "Light Cavalry",    unit_type: "tt_light_cavalry",   role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_heavy_cavalry",   name: "Heavy Cavalry",    unit_type: "tt_heavy_cavalry",   role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_priest",          name: "Priest",           unit_type: "tt_priest",          role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_high_priest",     name: "High Priest",      unit_type: "tt_high_priest",     role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_mage",            name: "Mage",             unit_type: "tt_mage",            role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_paladin",         name: "Paladin",          unit_type: "tt_paladin",         role: "Tank",                    expects_projectile: false },
  { skin_key: "tt_commander",       name: "Commander",        unit_type: "tt_commander",       role: "Siege",                   expects_projectile: true  },
  { skin_key: "tt_king",            name: "King",             unit_type: "tt_king",            role: "Tank",                    expects_projectile: false },
  { skin_key: "tt_mounted_scout",   name: "Mounted Scout",    unit_type: "tt_mounted_scout",   role: "Melee",                   expects_projectile: false },
  { skin_key: "tt_mounted_knight",  name: "Mounted Knight",   unit_type: "tt_mounted_knight",  role: "Tank",                    expects_projectile: false },
  { skin_key: "tt_mounted_mage",    name: "Mounted Mage",     unit_type: "tt_mounted_mage",    role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_mounted_paladin", name: "Mounted Paladin",  unit_type: "tt_mounted_paladin", role: "Tank",                    expects_projectile: false },
  { skin_key: "tt_mounted_priest",  name: "Mounted Priest",   unit_type: "tt_mounted_priest",  role: "Ranged",                  expects_projectile: true  },
  { skin_key: "tt_mounted_king",    name: "Mounted King",     unit_type: "tt_mounted_king",    role: "Siege",                   expects_projectile: true  },
]);

function titleCaseFromKey(key) {
  return String(key || "")
    .split("_")
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");
}

function getLockedWavePlan() {
  return LOCKED_WAVE_PLAN.map((wave) => ({ ...wave }));
}

function getWavePreview(unitTypeMap = null) {
  return LOCKED_WAVE_PLAN.map((wave) => {
    const unit = unitTypeMap && unitTypeMap[wave.unit_type];
    const fallbackName = wave.wave_number === 30 ? "Dragonhide" : titleCaseFromKey(wave.unit_type);
    return {
      ...wave,
      unit_name: unit?.name || fallbackName,
      uses_placeholder: wave.wave_number === 30,
    };
  });
}

function tryReadRegistrySnapshot() {
  const registryPath = path.join(__dirname, "..", "unity-client", "Assets", "Registry", "UnitPrefabRegistry.asset");
  if (!fs.existsSync(registryPath)) {
    return { exists: false, path: registryPath, baseEntries: {}, skinEntries: {} };
  }

  const text = fs.readFileSync(registryPath, "utf8");
  const baseEntries = {};
  const skinEntries = {};
  let section = "";
  let current = null;

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (line === "entries:") { section = "entries"; current = null; continue; }
    if (line === "skinEntries:") { section = "skins"; current = null; continue; }
    if (line.startsWith("fallbackPrefab:")) { section = ""; current = null; continue; }

    if (section === "entries") {
      const keyMatch = line.match(/^- key:\s*(.+)$/);
      if (keyMatch) {
        current = { key: keyMatch[1].trim() };
        baseEntries[current.key] = current;
        continue;
      }
      if (!current) continue;
      const prefabMatch = line.match(/^prefab:\s*\{fileID:\s*([0-9-]+)/);
      if (prefabMatch) { current.prefabAssigned = prefabMatch[1] !== "0"; continue; }
      const scaleMatch = line.match(/^scale:\s*([0-9.]+)/);
      if (scaleMatch) current.scale = Number(scaleMatch[1]);
      continue;
    }

    if (section === "skins") {
      const skinKeyMatch = line.match(/^- skinKey:\s*(.+)$/);
      if (skinKeyMatch) {
        current = { skinKey: skinKeyMatch[1].trim() };
        skinEntries[current.skinKey] = current;
        continue;
      }
      if (!current) continue;
      const unitTypeMatch = line.match(/^unitType:\s*(.+)$/);
      if (unitTypeMatch) { current.unitType = unitTypeMatch[1].trim(); continue; }
      const prefabMatch = line.match(/^prefab:\s*\{fileID:\s*([0-9-]+)/);
      if (prefabMatch) { current.prefabAssigned = prefabMatch[1] !== "0"; continue; }
      const scaleMatch = line.match(/^scale:\s*([0-9.]+)/);
      if (scaleMatch) current.scale = Number(scaleMatch[1]);
    }
  }

  return { exists: true, path: registryPath, baseEntries, skinEntries };
}

function status(code, label) {
  return { code, label };
}

function getTTOnboarding(unitTypeMap = null) {
  const registry = tryReadRegistrySnapshot();

  return TT_UNIT_ROSTER.map((unit) => {
    const registrySkin = registry.skinEntries[unit.skin_key] || null;
    const baseEntry = registry.baseEntries[unit.unit_type] || null;
    const unitType = unitTypeMap && unitTypeMap[unit.unit_type];
    const projectileConfigured = !unit.expects_projectile
      ? status("na", "N/A")
      : (unitType?.proj_behavior || Number(unitType?.projectile_travel_ticks) > 0)
        ? status("ready", "Uses existing projectile")
        : status("pending", "Projectile missing");
    const prefabAssigned = !!registrySkin?.prefabAssigned;
    const scaleValue = Number.isFinite(registrySkin?.scale) ? registrySkin.scale : null;

    return {
      ...unit,
      mapped_unit_name: unitType?.name || titleCaseFromKey(unit.unit_type),
      registry_skin_present: !!registrySkin,
      registry_scale: scaleValue,
      checklist: {
        prefab_assigned: prefabAssigned ? status("ready", "Prefab assigned") : status("pending", "Skin prefab missing"),
        materials_correct: prefabAssigned ? status("review", "Verify TT materials in scene") : status("pending", "Blocked on prefab"),
        weapon_attached: prefabAssigned ? status("review", "Use TT prefab weapon as-is") : status("pending", "Blocked on prefab"),
        projectile_configured: projectileConfigured,
        role_assigned: unit.role ? status("ready", unit.role) : status("pending", "Role missing"),
        lane_color_works: prefabAssigned ? status("review", "Verify clothing/body tint only") : status("pending", "Blocked on prefab"),
        scale_correct: scaleValue === 1 ? status("ready", "Scale 1.0") : status("pending", scaleValue == null ? "Scale missing" : `Scale ${scaleValue}`),
        no_missing_refs: prefabAssigned && baseEntry?.prefabAssigned ? status("review", "Run final prefab reference pass") : status("pending", "Missing prefab reference"),
      },
    };
  });
}

module.exports = {
  getLockedWavePlan,
  getTTOnboarding,
  getWavePreview,
  titleCaseFromKey,
  tryReadRegistrySnapshot,
};
