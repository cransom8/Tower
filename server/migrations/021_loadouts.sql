-- 021_loadouts.sql
-- Player loadout slots (up to 4 per player, each storing up to 5 unit type IDs)

CREATE TABLE IF NOT EXISTS loadouts (
  id            SERIAL PRIMARY KEY,
  player_id     UUID    NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  slot          INTEGER NOT NULL CHECK (slot >= 0 AND slot <= 3),
  name          TEXT    NOT NULL DEFAULT '',
  unit_type_ids INTEGER[] NOT NULL DEFAULT '{}',
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (player_id, slot)
);

CREATE INDEX IF NOT EXISTS idx_loadouts_player ON loadouts(player_id);
