-- 024_asset_packs.sql
-- Skin / Asset Pack tables + skin_pack_id FK on unit_types (Phase H)

-- ── Asset Packs ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS asset_packs (
  id          SERIAL PRIMARY KEY,
  key         TEXT NOT NULL UNIQUE,
  name        TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  enabled     BOOLEAN NOT NULL DEFAULT TRUE,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── Asset Pack Items ──────────────────────────────────────────────────────────
-- One row per (pack, unit_type_key, asset_slot) override.
-- asset_slot: 'icon' | 'sprite' | 'animation'
CREATE TABLE IF NOT EXISTS asset_pack_items (
  id             SERIAL PRIMARY KEY,
  pack_id        INTEGER NOT NULL REFERENCES asset_packs(id) ON DELETE CASCADE,
  unit_type_key  TEXT    NOT NULL,
  asset_slot     TEXT    NOT NULL
                   CHECK (asset_slot IN ('icon', 'sprite', 'animation')),
  url            TEXT    NOT NULL,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (pack_id, unit_type_key, asset_slot)
);

CREATE INDEX IF NOT EXISTS idx_asset_pack_items_pack
  ON asset_pack_items (pack_id);

-- ── FK on unit_types ──────────────────────────────────────────────────────────
ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS skin_pack_id INTEGER
    REFERENCES asset_packs(id) ON DELETE SET NULL;
