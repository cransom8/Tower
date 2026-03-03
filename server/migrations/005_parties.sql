-- 005_parties.sql
-- In-memory party state is authoritative in Phase 2; these tables support
-- Phase 4 reconnect/history queries only.

CREATE TABLE IF NOT EXISTS parties (
  id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  code         CHAR(6)     NOT NULL UNIQUE,
  leader_id    UUID        NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  status       TEXT        NOT NULL DEFAULT 'idle', -- idle | queued | in_match
  queue_mode   TEXT,       -- ranked_2v2 | casual_2v2
  region       TEXT        NOT NULL DEFAULT 'global',
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS parties_leader_idx ON parties (leader_id);

CREATE TABLE IF NOT EXISTS party_members (
  id         UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  party_id   UUID        NOT NULL REFERENCES parties(id) ON DELETE CASCADE,
  player_id  UUID        NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  joined_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (player_id)   -- a player can only be in one party at a time
);

CREATE INDEX IF NOT EXISTS pm_party_id_idx  ON party_members (party_id);
CREATE INDEX IF NOT EXISTS pm_player_id_idx ON party_members (player_id);
