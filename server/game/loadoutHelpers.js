"use strict";

function createLoadoutHelpers({ db, unitTypes }) {
  function loadoutEntry(ut) {
    return {
      id: ut.id,
      key: ut.key,
      name: ut.name,
      send_cost: Number(ut.send_cost) || 0,
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

  async function resolveLoadout(playerId, loadoutSlot, inlineUnitTypeIds) {
    const all = unitTypes.getAllUnitTypes();
    const byId = {};
    for (const ut of all) byId[ut.id] = ut;

    let ids = null;

    if (Number.isInteger(loadoutSlot) && loadoutSlot >= 0 && loadoutSlot <= 3 && db && playerId) {
      try {
        const row = await db
          .query("SELECT unit_type_ids FROM loadouts WHERE player_id = $1 AND slot = $2", [playerId, loadoutSlot])
          .then((result) => result.rows[0]);
        if (row) ids = row.unit_type_ids;
      } catch {
        // Fall through to guest/default loadout resolution.
      }
    }

    if (!ids && Array.isArray(inlineUnitTypeIds) && inlineUnitTypeIds.length === 5) {
      ids = inlineUnitTypeIds;
    }

    if (ids) {
      const resolved = ids
        .map((id) => byId[id])
        .filter((ut) => ut && ut.enabled && ut.behavior_mode === "both");
      if (resolved.length === 5) return resolved.map(loadoutEntry);
    }

    return all
      .filter((ut) => ut.enabled && ut.behavior_mode === "both")
      .sort((a, b) => (Number(a.send_cost) || 0) - (Number(b.send_cost) || 0))
      .slice(0, 5)
      .map(loadoutEntry);
  }

  function hasValidInlineLoadoutIds(unitTypeIds) {
    if (!Array.isArray(unitTypeIds) || unitTypeIds.length !== 5) return false;
    const allowedIds = new Set(
      unitTypes
        .getAllUnitTypes()
        .filter((ut) => ut.enabled && ut.behavior_mode === "both")
        .map((ut) => ut.id)
    );
    return unitTypeIds.every((id) => {
      const parsed = Number(id);
      return Number.isInteger(parsed) && allowedIds.has(parsed);
    });
  }

  function validateLoadoutSelection(socket, loadoutSlot, unitTypeIds) {
    // null/undefined = use default loadout (guests + players who skip loadout step)
    if (loadoutSlot == null) return true;
    const validSlot = Number.isInteger(loadoutSlot) && loadoutSlot >= 0 && loadoutSlot <= 3;
    if (!validSlot) {
      socket.emit("error_message", { message: "Invalid loadout slot." });
      return false;
    }
    // Guests without a saved/inline loadout are allowed — resolveLoadout falls back to defaults.
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
