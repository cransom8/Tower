-- 058_player_preferences.sql
-- Persist per-player client and menu preferences.

ALTER TABLE players
  ADD COLUMN IF NOT EXISTS preferences_json JSONB NOT NULL DEFAULT '{}'::jsonb;
