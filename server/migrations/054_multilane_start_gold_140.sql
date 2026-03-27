-- Migration 054: double the default Forge Wars starting gold to 140
-- for multilane configs that are still using the old 70-gold baseline.

UPDATE game_configs
SET config_json = jsonb_set(
  COALESCE(config_json, '{}'::jsonb),
  '{globalParams,startGold}',
  to_jsonb(140),
  true
)
WHERE mode = 'multilane'
  AND COALESCE(config_json->'globalParams'->>'startGold', '70') IN ('70', '70.0');
