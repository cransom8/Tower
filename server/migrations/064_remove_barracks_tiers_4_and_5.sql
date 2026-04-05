-- 064_remove_barracks_tiers_4_and_5.sql
-- Barracks progression now ends at tier 3.

DELETE FROM barracks_levels
WHERE level > 3;

ALTER TABLE barracks_levels
  DROP CONSTRAINT IF EXISTS barracks_levels_level_check;

ALTER TABLE barracks_levels
  ADD CONSTRAINT barracks_levels_level_check
  CHECK (level >= 1 AND level <= 3);
