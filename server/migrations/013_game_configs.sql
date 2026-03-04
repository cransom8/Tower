-- 013_game_configs.sql
-- Versioned, DB-backed game balance config (unit/tower defs, global params).
-- Admin publishes configs here; server picks them up on next restart.

CREATE TABLE IF NOT EXISTS game_configs (
  id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  mode         TEXT        NOT NULL CHECK (mode IN ('classic','multilane')),
  version      INT         NOT NULL,
  label        TEXT,                    -- short human name, e.g. "Buffed Golem v2"
  config_json  JSONB       NOT NULL,
  notes        TEXT,
  published_by TEXT,                    -- admin email or 'ADMIN_SECRET'
  published_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  is_active    BOOLEAN     NOT NULL DEFAULT false
);

CREATE INDEX IF NOT EXISTS gc_mode_pub_idx ON game_configs (mode, published_at DESC);
CREATE INDEX IF NOT EXISTS gc_active_idx   ON game_configs (mode, is_active);
