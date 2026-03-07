-- 030_feature_flags_expanded.sql
-- Additional feature flags for queue/lobby control.

INSERT INTO feature_flags (name, enabled, notes) VALUES
  ('survival_public_enabled', true,  'Allow public survival queue'),
  ('public_ffa_enabled',      true,  'Allow public FFA matchmaking'),
  ('private_match_enabled',   true,  'Allow private lobby creation'),
  ('1v1_ranked_enabled',      false, 'Enable ranked 1v1 (off by default)')
ON CONFLICT (name) DO NOTHING;
