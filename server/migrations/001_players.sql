-- 001_players.sql
CREATE EXTENSION IF NOT EXISTS "pgcrypto";

CREATE TABLE IF NOT EXISTS players (
  id            UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  google_id     TEXT        UNIQUE NOT NULL,
  display_name  TEXT        NOT NULL,
  region        TEXT        NOT NULL DEFAULT 'global',
  status        TEXT        NOT NULL DEFAULT 'active', -- active | suspended
  created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS players_display_name_lower_idx
  ON players (lower(display_name));
