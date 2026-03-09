-- Migration 037: Skin system
-- skin_catalog  — all available skins (seeded by devs, purchased by players)
-- player_skins  — which skins each player owns
-- player_skin_equip — which skin each player has equipped per unit type (null = default)

CREATE TABLE IF NOT EXISTS skin_catalog (
  id          SERIAL PRIMARY KEY,
  skin_key    TEXT    NOT NULL UNIQUE,   -- matches Unity SkinEntry.skinKey
  unit_type   TEXT    NOT NULL,          -- which unit type this skin replaces
  name        TEXT    NOT NULL,
  description TEXT    NOT NULL DEFAULT '',
  price       INTEGER NOT NULL DEFAULT 0, -- 0 = free/not-for-sale; >0 = gem cost
  currency    TEXT    NOT NULL DEFAULT 'gems', -- 'gems' | 'gold' | 'free'
  preview_url TEXT    NOT NULL DEFAULT '', -- thumbnail for the shop UI
  enabled     BOOLEAN NOT NULL DEFAULT true,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS player_skins (
  player_id   UUID    NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  skin_key    TEXT    NOT NULL REFERENCES skin_catalog(skin_key) ON DELETE CASCADE,
  acquired_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  PRIMARY KEY (player_id, skin_key)
);

CREATE TABLE IF NOT EXISTS player_skin_equip (
  player_id   UUID    NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  unit_type   TEXT    NOT NULL,
  skin_key    TEXT    NOT NULL REFERENCES skin_catalog(skin_key) ON DELETE CASCADE,
  PRIMARY KEY (player_id, unit_type)
);

CREATE INDEX IF NOT EXISTS idx_player_skins_player   ON player_skins(player_id);
CREATE INDEX IF NOT EXISTS idx_player_skin_equip_pid ON player_skin_equip(player_id);
