-- 011_feature_flags.sql
-- Runtime toggles for operational control without deploys.

CREATE TABLE IF NOT EXISTS feature_flags (
  name        TEXT        PRIMARY KEY,
  enabled     BOOLEAN     NOT NULL DEFAULT true,
  notes       TEXT,
  updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO feature_flags (name, enabled, notes) VALUES
  ('maintenance_mode',        false, 'Block new socket connections and show maintenance screen'),
  ('ranked_queue_enabled',    true,  'Allow players to enter the ranked matchmaking queue'),
  ('new_player_registration', true,  'Allow new player account creation'),
  ('casual_queue_enabled',    true,  'Allow players to enter the casual matchmaking queue')
ON CONFLICT (name) DO NOTHING;
