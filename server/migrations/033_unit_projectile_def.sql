-- 033_unit_projectile_def.sql
-- Links each unit_type to a projectile_definition so firing behavior
-- (single / pierce / chain / bounce / splash) is driven from the DB.

ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS projectile_def_id INTEGER REFERENCES projectile_definitions(id) ON DELETE SET NULL;

-- Fixed defenders
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'arrow')
  WHERE key = 'archer';
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'bolt')
  WHERE key = 'fighter';
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'magic_orb')
  WHERE key = 'mage';
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'ballista_bolt')
  WHERE key = 'ballista';
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'cannonball')
  WHERE key = 'cannon';

-- Both-mode units (used as defenders after loadout placement)
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'bolt')
  WHERE key IN ('runner', 'footman', 'ironclad', 'golem');
UPDATE unit_types SET projectile_def_id = (SELECT id FROM projectile_definitions WHERE key = 'magic_orb')
  WHERE key = 'warlock';
