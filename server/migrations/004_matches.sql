-- 004_matches.sql  (scaffolded now; fully populated in Phase 3)

CREATE TABLE IF NOT EXISTS matches (
  id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  room_id      TEXT        NOT NULL,
  mode         TEXT        NOT NULL DEFAULT 'multilane', -- classic | multilane
  winner_lane  INT,        -- NULL = draw / abandoned
  started_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  ended_at     TIMESTAMPTZ,
  status       TEXT        NOT NULL DEFAULT 'in_progress' -- in_progress | completed | abandoned
);

CREATE INDEX IF NOT EXISTS matches_status_idx ON matches (status);

CREATE TABLE IF NOT EXISTS match_players (
  id          UUID  PRIMARY KEY DEFAULT gen_random_uuid(),
  match_id    UUID  NOT NULL REFERENCES matches(id) ON DELETE CASCADE,
  player_id   UUID  NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  lane_index  INT   NOT NULL,
  result      TEXT  NOT NULL DEFAULT 'pending' -- pending | win | loss | draw
);

CREATE INDEX IF NOT EXISTS mp_match_id_idx  ON match_players (match_id);
CREATE INDEX IF NOT EXISTS mp_player_id_idx ON match_players (player_id);
