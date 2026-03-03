-- 006_seasons.sql — Season & season_ratings tables (Phase 6)

CREATE TABLE IF NOT EXISTS seasons (
  id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  name        TEXT        NOT NULL,
  start_date  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  end_date    TIMESTAMPTZ,
  is_active   BOOLEAN     NOT NULL DEFAULT false,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS season_ratings (
  id            UUID             PRIMARY KEY DEFAULT gen_random_uuid(),
  season_id     UUID             NOT NULL REFERENCES seasons(id) ON DELETE CASCADE,
  player_id     UUID             NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  mode          TEXT             NOT NULL DEFAULT '2v2_ranked',
  peak_rating   DOUBLE PRECISION NOT NULL DEFAULT 450.0,
  final_rating  DOUBLE PRECISION,
  UNIQUE (season_id, player_id, mode)
);

CREATE INDEX IF NOT EXISTS sr_season_mode_idx ON season_ratings (season_id, mode, peak_rating DESC);
CREATE INDEX IF NOT EXISTS sr_player_idx      ON season_ratings (player_id);
