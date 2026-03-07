-- 031_unit_types_both_mode.sql
-- Make the 5 attacker units usable as defenders by:
--   1. Setting behavior_mode = 'both'
--   2. Adding attack_range so they can fire when placed as fixed defenders
--   3. Adding build_cost so place_wall → upgrade_wall has a price
--   4. Adding income matching existing defender tiers

UPDATE unit_types SET
  behavior_mode = 'both',
  range         = 0.25,
  build_cost    = 10,
  income        = 1,
  updated_at    = NOW()
WHERE key = 'runner';

UPDATE unit_types SET
  behavior_mode = 'both',
  range         = 0.20,
  build_cost    = 12,
  income        = 1,
  updated_at    = NOW()
WHERE key = 'footman';

UPDATE unit_types SET
  behavior_mode = 'both',
  range         = 0.18,
  build_cost    = 20,
  income        = 2,
  updated_at    = NOW()
WHERE key = 'ironclad';

UPDATE unit_types SET
  behavior_mode = 'both',
  range         = 0.32,
  build_cost    = 24,
  income        = 2,
  updated_at    = NOW()
WHERE key = 'warlock';

UPDATE unit_types SET
  behavior_mode = 'both',
  range         = 0.14,
  build_cost    = 30,
  income        = 3,
  updated_at    = NOW()
WHERE key = 'golem';
