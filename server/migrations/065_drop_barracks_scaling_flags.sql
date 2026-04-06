-- Migration 065: Drop barracks_scales_hp and barracks_scales_dmg columns.
-- These flags were part of a scrapped "barracks level" system where upgrading the
-- barracks would multiply unit HP/damage. The system was removed; barracks now only
-- serves as a place to purchase units. The columns were never read during gameplay.

ALTER TABLE unit_types DROP COLUMN IF EXISTS barracks_scales_hp;
ALTER TABLE unit_types DROP COLUMN IF EXISTS barracks_scales_dmg;
