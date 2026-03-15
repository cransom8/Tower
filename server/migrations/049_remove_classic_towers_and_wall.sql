-- Remove legacy classic tower rows and the obsolete wall placeholder.
-- Keep classic unit_types rows present but hidden/disabled to avoid breaking
-- any historical references stored outside FK-constrained tables.

DELETE FROM tower_ability_assignments
WHERE tower_id IN (
  SELECT id
  FROM towers
  WHERE lower(name) IN ('archer', 'fighter', 'mage', 'ballista', 'cannon')
);

DELETE FROM towers
WHERE lower(name) IN ('archer', 'fighter', 'mage', 'ballista', 'cannon');

DELETE FROM unit_types
WHERE key = 'wall_placeholder';

UPDATE unit_types
SET
  enabled = FALSE,
  display_to_players = FALSE,
  updated_at = NOW()
WHERE key IN ('archer', 'fighter', 'mage', 'ballista', 'cannon');
