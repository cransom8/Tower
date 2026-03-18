-- 003_refresh_tokens.sql

CREATE TABLE IF NOT EXISTS refresh_tokens (
  id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  player_id   UUID        NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  token_hash  TEXT        NOT NULL UNIQUE, -- SHA-256 hex of the raw token
  expires_at  TIMESTAMPTZ NOT NULL,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  revoked_at  TIMESTAMPTZ             -- NULL = still valid
);

CREATE INDEX IF NOT EXISTS rt_player_id_idx   ON refresh_tokens (player_id);
CREATE INDEX IF NOT EXISTS rt_token_hash_idx  ON refresh_tokens (token_hash);
