"use strict";

function createLoadoutHelpers({ db, unitTypes }) {
  function loadoutEntry(ut) {
    return {
      id: ut.id,
      key: ut.key,
      name: ut.name,
      send_cost: Number(ut.send_cost) || 0,
      build_cost: Number(ut.build_cost) || 0,
      hp: Number(ut.hp) || 0,
      attack_damage: Number(ut.attack_damage) || 0,
      attack_speed: Number(ut.attack_speed) || 1,
      range: Number(ut.range) || 0,
      path_speed: Number(ut.path_speed) || 0,
      damage_type: ut.damage_type || "NORMAL",
      armor_type: ut.armor_type || "UNARMORED",
      income: Number(ut.income) || 0,
      icon_url: ut.icon_url || null,
      sprite_url: ut.sprite_url || null,
      animation_url: ut.animation_url || null,
      sprite_url_front: ut.sprite_url_front || null,
      sprite_url_back: ut.sprite_url_back || null,
      animation_url_front: ut.animation_url_front || null,
      animation_url_back: ut.animation_url_back || null,
      idle_sprite_url: ut.idle_sprite_url || null,
      idle_sprite_url_front: ut.idle_sprite_url_front || null,
      idle_sprite_url_back: ut.idle_sprite_url_back || null,
    };
  }

  async function resolveLoadout(_playerId, inlineUnitTypeIds) {
    const all = unitTypes.getAllUnitTypes();
    const byId = {};
    for (const ut of all) byId[ut.id] = ut;

    let ids = null;

    if (!ids && Array.isArray(inlineUnitTypeIds) && inlineUnitTypeIds.length === 5) {
      ids = inlineUnitTypeIds;
    }

    const isSendable = (ut) => ut && ut.enabled && Number(ut.send_cost) > 0;
    const isBuildable = (ut) => isSendable(ut) && Number(ut.build_cost) > 0 && Number(ut.range) > 0;

    if (ids) {
      const resolved = ids
        .map((id) => byId[id])
        .filter(isBuildable);
      if (resolved.length === 5) return resolved.map(loadoutEntry);
    }

    // Default loadout should match the current Unity-era Hero Pack roster.
    const DEFAULT_KEYS = ["goblin", "orc", "troll", "vampire", "wyvern"];
    const byKey = {};
    for (const ut of all) byKey[ut.key] = ut;
    const preferred = DEFAULT_KEYS
      .map((k) => byKey[k])
      .filter(isBuildable);
    if (preferred.length === 5) return preferred.map(loadoutEntry);

    const fallback = all.filter(isBuildable).slice(0, 5);
    return fallback.map(loadoutEntry);
  }

  function hasValidInlineLoadoutIds(unitTypeIds) {
    if (!Array.isArray(unitTypeIds) || unitTypeIds.length !== 5) return false;
    const allowedIds = new Set(
      unitTypes
        .getAllUnitTypes()
        .filter((ut) => ut.enabled && Number(ut.send_cost) > 0 && Number(ut.build_cost) > 0 && Number(ut.range) > 0)
        .map((ut) => ut.id)
    );
    return unitTypeIds.every((id) => {
      const parsed = Number(id);
      return Number.isInteger(parsed) && allowedIds.has(parsed);
    });
  }

  function validateLoadoutSelection(_socket, _loadoutSlot, _unitTypeIds) {
    // Saved preset loadout slots are deprecated. Keep accepting legacy payloads
    // so older clients do not fail validation, but ignore loadoutSlot entirely.
    return true;
  }

  return {
    hasValidInlineLoadoutIds,
    loadoutEntry,
    resolveLoadout,
    validateLoadoutSelection,
  };
}

module.exports = { createLoadoutHelpers };
