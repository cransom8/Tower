-- 032_unit_special_fields.sql
-- Adds bounty, projectile_travel_ticks, and special_props to unit_types.
-- bounty: gold awarded to the defending lane when a moving unit is killed.
-- projectile_travel_ticks: ticks a projectile takes to reach its target (fixed defenders).
-- special_props: JSONB bag for per-unit special flags (warlockDebuff, structBonus, etc.)

ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS bounty                   INTEGER NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS projectile_travel_ticks  INTEGER NOT NULL DEFAULT 7,
  ADD COLUMN IF NOT EXISTS special_props            JSONB   NOT NULL DEFAULT '{}';

-- Seed bounty for the 5 attacker/both units
UPDATE unit_types SET bounty = 2 WHERE key = 'runner';
UPDATE unit_types SET bounty = 3 WHERE key = 'footman';
UPDATE unit_types SET bounty = 4 WHERE key = 'ironclad';
UPDATE unit_types SET bounty = 5 WHERE key = 'warlock';
UPDATE unit_types SET bounty = 6 WHERE key = 'golem';

-- Seed projectile_travel_ticks for the fixed defenders
UPDATE unit_types SET projectile_travel_ticks = 7 WHERE key = 'archer';
UPDATE unit_types SET projectile_travel_ticks = 6 WHERE key = 'fighter';
UPDATE unit_types SET projectile_travel_ticks = 7 WHERE key = 'mage';
UPDATE unit_types SET projectile_travel_ticks = 8 WHERE key = 'ballista';
UPDATE unit_types SET projectile_travel_ticks = 9 WHERE key = 'cannon';

-- Also set for the both-mode units (used when placed as defenders)
UPDATE unit_types SET projectile_travel_ticks = 7 WHERE key = 'runner';
UPDATE unit_types SET projectile_travel_ticks = 6 WHERE key = 'footman';
UPDATE unit_types SET projectile_travel_ticks = 7 WHERE key = 'ironclad';
UPDATE unit_types SET projectile_travel_ticks = 7 WHERE key = 'warlock';
UPDATE unit_types SET projectile_travel_ticks = 8 WHERE key = 'golem';

-- Warlock: debuffs nearby towers when moving
UPDATE unit_types SET special_props = '{"warlockDebuff": true}' WHERE key = 'warlock';

-- Golem: bonus damage to structures
UPDATE unit_types SET special_props = '{"structBonus": 0.25}' WHERE key = 'golem';
