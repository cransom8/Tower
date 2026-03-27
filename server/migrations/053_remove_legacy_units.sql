-- 053_remove_legacy_units.sql
-- Permanently remove legacy unit rows that are no longer supported by current
-- art/content. Keep this idempotent because the migration runner replays SQL.

DELETE FROM asset_pack_items
WHERE unit_type_key IN (
  'archer',
  'ballista',
  'cannon',
  'fighter',
  'footman',
  'golem',
  'ironclad',
  'mage',
  'runner',
  'wall_placeholder',
  'warlock'
);

DELETE FROM unit_types
WHERE key IN (
  'archer',
  'ballista',
  'cannon',
  'fighter',
  'footman',
  'golem',
  'ironclad',
  'mage',
  'runner',
  'wall_placeholder',
  'warlock'
);
