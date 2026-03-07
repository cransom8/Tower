"use strict";

const DEFAULT_BRANDING = Object.freeze({
  publicTitle: "Ransom Forge",
  publicSubtitle: "PvP Tower Defense",
  publicBrowserTitle: "Ransom Forge - Multiplayer",
  adminTitle: "Castle Defender Admin",
  adminLoginTitle: "Admin Dashboard",
  loadoutTitle: "Loadout",
  loadoutHint: "Click a slot to edit",
  loadoutBuilderTitle: "Loadout Builder",
});

const BRANDING_LIMITS = Object.freeze({
  publicTitle: 60,
  publicSubtitle: 120,
  publicBrowserTitle: 80,
  adminTitle: 80,
  adminLoginTitle: 80,
  loadoutTitle: 40,
  loadoutHint: 80,
  loadoutBuilderTitle: 60,
});

function _clean(value, limit, fallback) {
  const str = typeof value === "string" ? value.trim() : "";
  if (!str) return fallback;
  return str.slice(0, limit);
}

function normalizeBranding(input) {
  const src = input && typeof input === "object" ? input : {};
  return {
    publicTitle: _clean(src.publicTitle, BRANDING_LIMITS.publicTitle, DEFAULT_BRANDING.publicTitle),
    publicSubtitle: _clean(src.publicSubtitle, BRANDING_LIMITS.publicSubtitle, DEFAULT_BRANDING.publicSubtitle),
    publicBrowserTitle: _clean(src.publicBrowserTitle, BRANDING_LIMITS.publicBrowserTitle, DEFAULT_BRANDING.publicBrowserTitle),
    adminTitle: _clean(src.adminTitle, BRANDING_LIMITS.adminTitle, DEFAULT_BRANDING.adminTitle),
    adminLoginTitle: _clean(src.adminLoginTitle, BRANDING_LIMITS.adminLoginTitle, DEFAULT_BRANDING.adminLoginTitle),
    loadoutTitle: _clean(src.loadoutTitle, BRANDING_LIMITS.loadoutTitle, DEFAULT_BRANDING.loadoutTitle),
    loadoutHint: _clean(src.loadoutHint, BRANDING_LIMITS.loadoutHint, DEFAULT_BRANDING.loadoutHint),
    loadoutBuilderTitle: _clean(src.loadoutBuilderTitle, BRANDING_LIMITS.loadoutBuilderTitle, DEFAULT_BRANDING.loadoutBuilderTitle),
  };
}

async function getBranding(db) {
  if (!db) return { ...DEFAULT_BRANDING };
  try {
    const row = await db.query(
      "SELECT config_json, updated_at, updated_by FROM branding_settings WHERE singleton = TRUE LIMIT 1"
    ).then((r) => r.rows[0]);
    const branding = normalizeBranding(row?.config_json || {});
    return {
      ...branding,
      updatedAt: row?.updated_at || null,
      updatedBy: row?.updated_by || null,
    };
  } catch {
    return { ...DEFAULT_BRANDING, updatedAt: null, updatedBy: null };
  }
}

async function saveBranding(db, input, updatedBy) {
  const branding = normalizeBranding(input);
  const result = await db.query(
    `INSERT INTO branding_settings (singleton, config_json, updated_by, updated_at)
     VALUES (TRUE, $1::jsonb, $2, NOW())
     ON CONFLICT (singleton) DO UPDATE
       SET config_json = EXCLUDED.config_json,
           updated_by = EXCLUDED.updated_by,
           updated_at = NOW()
     RETURNING config_json, updated_at, updated_by`,
    [JSON.stringify(branding), updatedBy || null]
  );
  return {
    ...normalizeBranding(result.rows[0]?.config_json || branding),
    updatedAt: result.rows[0]?.updated_at || null,
    updatedBy: result.rows[0]?.updated_by || null,
  };
}

module.exports = {
  DEFAULT_BRANDING,
  normalizeBranding,
  getBranding,
  saveBranding,
};
