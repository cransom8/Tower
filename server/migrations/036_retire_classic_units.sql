-- 036_retire_classic_units.sql
-- Disable the 5 classic attacker unit types (runner, footman, ironclad, warlock, golem).
-- These are replaced by the 30 Heroic Fantasy Creatures added in migrations 034 and 035.

UPDATE unit_types SET enabled = FALSE, updated_at = NOW()
WHERE key IN ('runner', 'footman', 'ironclad', 'warlock', 'golem');
