-- 002_ratings.sql
-- Glicko-2 style: mu (rating), sigma (uncertainty), rating = mu - 3*sigma (conservative estimate)

CREATE TABLE IF NOT EXISTS ratings (
  id          UUID             PRIMARY KEY DEFAULT gen_random_uuid(),
  player_id   UUID             NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  mode        TEXT             NOT NULL DEFAULT '2v2_ranked', -- 2v2_ranked | 2v2_casual
  mu          DOUBLE PRECISION NOT NULL DEFAULT 1500.0,
  sigma       DOUBLE PRECISION NOT NULL DEFAULT 350.0,
  -- conservative visible rating (updated manually, not generated column for portability)
  rating      DOUBLE PRECISION NOT NULL DEFAULT 450.0,
  wins        INT              NOT NULL DEFAULT 0,
  losses      INT              NOT NULL DEFAULT 0,
  updated_at  TIMESTAMPTZ      NOT NULL DEFAULT NOW(),
  UNIQUE (player_id, mode)
);

CREATE INDEX IF NOT EXISTS ratings_player_mode_idx ON ratings (player_id, mode);
CREATE INDEX IF NOT EXISTS ratings_leaderboard_idx ON ratings (mode, rating DESC);

CREATE TABLE IF NOT EXISTS rating_history (
  id          UUID             PRIMARY KEY DEFAULT gen_random_uuid(),
  player_id   UUID             NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  match_id    UUID,            -- populated in Phase 3
  mode        TEXT             NOT NULL DEFAULT '2v2_ranked',
  old_mu      DOUBLE PRECISION NOT NULL,
  old_sigma   DOUBLE PRECISION NOT NULL,
  new_mu      DOUBLE PRECISION NOT NULL,
  new_sigma   DOUBLE PRECISION NOT NULL,
  delta       DOUBLE PRECISION NOT NULL, -- new_rating - old_rating
  created_at  TIMESTAMPTZ      NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS rh_player_id_idx ON rating_history (player_id);
