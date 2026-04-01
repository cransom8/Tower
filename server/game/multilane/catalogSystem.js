"use strict";

function requireDepFunction(deps, name) {
  const fn = deps && deps[name];
  if (typeof fn !== "function")
    throw new Error(`catalogSystem requires deps.${name}`);
  return fn;
}

function requireFiniteNumber(deps, name) {
  const value = Number(deps && deps[name]);
  if (!Number.isFinite(value))
    throw new Error(`catalogSystem requires finite deps.${name}`);
  return value;
}

function getDefaultFortPresentationKey(deps = {}) {
  const value = deps.DEFAULT_FORT_PRESENTATION_KEY;
  if (typeof value !== "string" || !value.trim())
    throw new Error("catalogSystem requires deps.DEFAULT_FORT_PRESENTATION_KEY");
  return value;
}

function resolveFortPresentationConfig(archetypeKey, presentationKey = null, fallbackDisplayName = null, deps = {}) {
  const resolveFortCatalogUnitKey = requireDepFunction(deps, "resolveFortCatalogUnitKey");
  const resolveFortDisplayName = requireDepFunction(deps, "resolveFortDisplayName");
  const resolveFortPortraitKey = requireDepFunction(deps, "resolveFortPortraitKey");
  const resolveFortSkinKey = requireDepFunction(deps, "resolveFortSkinKey");
  const isFortArchetypeKey = requireDepFunction(deps, "isFortArchetypeKey");
  const resolvedPresentationKey = presentationKey || getDefaultFortPresentationKey(deps);
  const catalogUnitKey = resolveFortCatalogUnitKey(archetypeKey, resolvedPresentationKey);
  if (!isFortArchetypeKey(archetypeKey)) {
    throw new Error(
      `[catalogSystem] Expected fortress archetype key, received '${String(archetypeKey || "")}'.`
    );
  }
  if (!catalogUnitKey) {
    throw new Error(
      `[catalogSystem] Missing fort presentation catalog mapping for archetype '${archetypeKey}' and presentation '${resolvedPresentationKey}'.`
    );
  }
  return {
    archetypeKey,
    presentationKey: resolvedPresentationKey,
    catalogUnitKey,
    skinKey: resolveFortSkinKey(archetypeKey, resolvedPresentationKey),
    portraitKey: resolveFortPortraitKey(archetypeKey, resolvedPresentationKey),
    displayName: resolveFortDisplayName(archetypeKey, resolvedPresentationKey, fallbackDisplayName),
  };
}

function resolveGameplayCatalogUnitKey(unitKey, presentationKey = null, deps = {}) {
  const isFortArchetypeKey = requireDepFunction(deps, "isFortArchetypeKey");
  const resolveFortCatalogUnitKey = requireDepFunction(deps, "resolveFortCatalogUnitKey");
  const resolvedPresentationKey = presentationKey || getDefaultFortPresentationKey(deps);
  if (!isFortArchetypeKey(unitKey))
    return unitKey;

  const catalogUnitKey = resolveFortCatalogUnitKey(unitKey, resolvedPresentationKey);
  if (!catalogUnitKey) {
    throw new Error(
      `[catalogSystem] Missing fort gameplay catalog unit mapping for archetype '${unitKey}' and presentation '${resolvedPresentationKey}'.`
    );
  }
  return catalogUnitKey;
}

const ABILITY_HOOKS = Object.freeze({
  splash_damage: "onAttack",
  pierce_targets: "onAttack",
  chain_lightning: "onAttack",
  slow: "onHit",
  freeze: "onHit",
  poison: "onHit",
  burn: "onHit",
  armor_reduction: "onHit",
  reveal_stealth: "onTick",
  knockback: "onHit",
  teleport_back: "onHit",
  aura_damage: "onSpawn",
  aura_atk_speed: "onSpawn",
  aura_range: "onSpawn",
  aura_cooldown: "onSpawn",
});

const ABILITY_AURA_TYPES = Object.freeze({
  aura_damage: "dmg_bonus",
  aura_atk_speed: "atk_speed_bonus",
  aura_range: "range_bonus",
  aura_cooldown: "cooldown_reduction",
});

function translateAbilityParams(abilityKey, rawParams, deps = {}) {
  const tickHz = requireFiniteNumber(deps, "TICK_HZ");
  const gridWidth = requireFiniteNumber(deps, "GRID_W");
  const gridHeight = requireFiniteNumber(deps, "GRID_H");
  switch (abilityKey) {
    case "slow":
      return {
        speedMult: 1 - (rawParams.slow_pct || 25) / 100,
        durationTicks: Math.round((rawParams.duration || 2) * tickHz),
      };
    case "freeze":
      return {
        durationTicks: Math.round((rawParams.duration || 1) * tickHz),
        procChance: rawParams.proc_chance || 20,
      };
    case "poison":
      return {
        dmgPerTick: (rawParams.dps || 5) / tickHz,
        durationTicks: Math.round((rawParams.duration || 4) * tickHz),
      };
    case "burn":
      return {
        dmgPerTick: (rawParams.dps || 8) / tickHz,
        durationTicks: Math.round((rawParams.duration || 3) * tickHz),
      };
    case "armor_reduction":
      return {
        reductionPct: rawParams.reduction_pct || 20,
        durationTicks: Math.round((rawParams.duration || 5) * tickHz),
      };
    case "knockback":
      return {
        tiles: Math.max(1, Math.round((rawParams.distance || 0.05) * gridHeight)),
        procChance: rawParams.proc_chance || 15,
      };
    case "teleport_back":
      return {
        procChance: rawParams.proc_chance || 10,
      };
    case "chain_lightning":
      return {
        maxJumps: rawParams.chains || 3,
        jumpRange: 2.0,
        dmgFalloff: 1 - (rawParams.decay_pct || 25) / 100,
      };
    case "pierce_targets":
      return {
        maxTargets: rawParams.max_targets || 3,
        pierceRadius: 1.0,
      };
    case "splash_damage":
      return {
        radius: (rawParams.radius || 0.05) * gridWidth,
      };
    default:
      return rawParams;
  }
}

function buildAbilitiesForUnitType(unitTypeKey, deps = {}) {
  const getUnitType = requireDepFunction(deps, "getUnitType");
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(unitTypeKey, null, deps);
  const unitType = getUnitType(resolvedUnitTypeKey);
  if (!unitType || !Array.isArray(unitType.abilities) || unitType.abilities.length === 0)
    return [];
  return unitType.abilities.map((ability, idx) => {
    const abilityKey = ability.ability_key;
    const rawParams = (ability.params && typeof ability.params === "object") ? ability.params : {};
    const hook = ABILITY_HOOKS[abilityKey] || "onTick";
    const isAura = hook === "onSpawn";
    const params = isAura
      ? {
          auraType: ABILITY_AURA_TYPES[abilityKey] || "dmg_bonus",
          value: rawParams.boost_pct || rawParams.value || 0,
          ...rawParams,
        }
      : translateAbilityParams(abilityKey, rawParams, deps);
    return {
      type: isAura ? "aura" : abilityKey,
      hook,
      params,
      priority: idx,
      abilityId: idx,
    };
  });
}

function resolveUnitDef(key, deps = {}) {
  const getUnitType = requireDepFunction(deps, "getUnitType");
  const tickHz = requireFiniteNumber(deps, "TICK_HZ");
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(key, null, deps);
  const unitType = getUnitType(resolvedUnitTypeKey);
  if (!unitType || Number(unitType.send_cost) <= 0)
    return null;
  const specialProps = (unitType.special_props && typeof unitType.special_props === "object")
    ? unitType.special_props
    : {};
  const attackSpeed = Math.max(0.01, Number(unitType.attack_speed) || 0.01);
  return {
    cost: Number(unitType.send_cost),
    income: Number(unitType.income),
    hp: Number(unitType.hp),
    dmg: Number(unitType.attack_damage),
    atkCdTicks: Math.max(1, Math.round(tickHz / attackSpeed)),
    pathSpeed: Number(unitType.path_speed),
    bounty: Number(unitType.bounty) || 1,
    range: Number(unitType.range),
    ranged: Number(unitType.range) > 0,
    armorType: unitType.armor_type || "MEDIUM",
    damageType: unitType.damage_type || "NORMAL",
    damageReductionPct: Number(unitType.damage_reduction_pct) || 0,
    warlockDebuff: specialProps.warlockDebuff != null ? !!specialProps.warlockDebuff : false,
    structBonus: specialProps.structBonus != null ? +specialProps.structBonus : 0,
    barracks_scales_hp: unitType.barracks_scales_hp === true,
    barracks_scales_dmg: unitType.barracks_scales_dmg === true,
  };
}

function resolveUnitSupportProfile(unit, deps = {}) {
  const getUnitType = requireDepFunction(deps, "getUnitType");
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(
    unit && (unit.type || unit.unitTypeKey || unit.key),
    null,
    deps
  );
  const unitType = getUnitType(resolvedUnitTypeKey);
  const specialProps = (unitType && unitType.special_props && typeof unitType.special_props === "object")
    ? unitType.special_props
    : {};
  const supportRoleRaw = specialProps.supportRole != null ? specialProps.supportRole : specialProps.support_role;
  const supportRole = typeof supportRoleRaw === "string"
    ? supportRoleRaw.trim().toLowerCase()
    : null;
  const isHealer = supportRole === "healer";
  const healAmountRaw = specialProps.healAmount != null ? specialProps.healAmount : specialProps.heal_amount;
  return {
    role: supportRole,
    isHealer,
    healAmount: isHealer ? Math.max(1, Number(healAmountRaw) || 1) : 0,
  };
}

function resolveTowerDef(key, deps = {}) {
  const getUnitType = requireDepFunction(deps, "getUnitType");
  const tickHz = requireFiniteNumber(deps, "TICK_HZ");
  const gridWidth = requireFiniteNumber(deps, "GRID_W");
  const splashRadiusTiles = requireFiniteNumber(deps, "SPLASH_RADIUS_TILES");
  const resolvedUnitTypeKey = resolveGameplayCatalogUnitKey(key, null, deps);
  const unitType = getUnitType(resolvedUnitTypeKey);
  if (!unitType)
    return null;
  const attackSpeed = Math.max(0.01, Number(unitType.attack_speed) || 0.01);
  const dbBehavior = unitType.proj_behavior || null;
  const dbBehaviorParams = (unitType.proj_behavior_params && typeof unitType.proj_behavior_params === "object")
    ? unitType.proj_behavior_params
    : null;
  const fallbackBehavior = unitType.damage_type === "SPLASH" ? "splash" : "single";
  const fallbackParams = unitType.damage_type === "SPLASH" ? { radius: splashRadiusTiles } : {};
  return {
    cost: Number(unitType.build_cost),
    range: Number(unitType.range) * gridWidth,
    dmg: Number(unitType.attack_damage),
    atkCdTicks: Math.max(1, Math.round(tickHz / attackSpeed)),
    projectileTicks: Number(unitType.projectile_travel_ticks) || 7,
    damageType: unitType.damage_type || "NORMAL",
    projBehavior: dbBehavior || fallbackBehavior,
    projBehaviorParams: dbBehaviorParams || fallbackParams,
    isSplash: (dbBehavior || fallbackBehavior) === "splash",
  };
}

function getMovingUnitDefMap(deps = {}) {
  const getAllUnitTypes = requireDepFunction(deps, "getAllUnitTypes");
  const map = {};
  for (const unitType of getAllUnitTypes()) {
    if (!unitType.enabled || Number(unitType.send_cost) <= 0)
      continue;
    const def = resolveUnitDef(unitType.key, deps);
    if (def)
      map[unitType.key] = def;
  }
  return map;
}

function getFixedUnitDefMap(deps = {}) {
  const getAllUnitTypes = requireDepFunction(deps, "getAllUnitTypes");
  const map = {};
  for (const unitType of getAllUnitTypes()) {
    if (!unitType.enabled || Number(unitType.build_cost) <= 0)
      continue;
    const def = resolveTowerDef(unitType.key, deps);
    if (def)
      map[unitType.key] = def;
  }
  return map;
}

module.exports = {
  resolveFortPresentationConfig,
  resolveGameplayCatalogUnitKey,
  translateAbilityParams,
  buildAbilitiesForUnitType,
  resolveUnitDef,
  resolveUnitSupportProfile,
  resolveTowerDef,
  getMovingUnitDefMap,
  getFixedUnitDefMap,
};
