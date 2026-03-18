-- Migration 040: Extend ML wave config to 21 authored waves
-- Adds waves 11–21 to the default config (id=1) seeded in migration 039.
-- All unit keys are confirmed present in migration 034 (unit_types).
-- Wave 21 (demon_lord) is the terminal authored wave. Wave 22+ is handled
-- by _resolveWave endless scaling at +10% hp/dmg per wave beyond the last config row.
--
-- ON CONFLICT DO NOTHING makes this idempotent — safe to re-run.
-- Duplicate wave numbers within the same config are prevented by the
-- UNIQUE(config_id, wave_number) constraint from migration 039; any attempt
-- to insert a wave_number that already exists will be silently skipped.

INSERT INTO ml_waves (config_id, wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
SELECT 1, w.wave_number, w.unit_type, w.spawn_qty, w.hp_mult, w.dmg_mult, w.speed_mult
FROM (VALUES
  (11, 'cyclops',         5, 1.00::numeric, 1.00::numeric, 1.00::numeric),
  (12, 'cyclops',         6, 1.30::numeric, 1.20::numeric, 1.00::numeric),
  (13, 'werewolf',        6, 1.00::numeric, 1.00::numeric, 1.00::numeric),
  (14, 'werewolf',        7, 1.35::numeric, 1.25::numeric, 1.05::numeric),
  (15, 'griffin',         5, 1.00::numeric, 1.00::numeric, 1.00::numeric),
  (16, 'griffin',         6, 1.40::numeric, 1.30::numeric, 1.05::numeric),
  (17, 'manticora',       4, 1.00::numeric, 1.00::numeric, 1.00::numeric),
  (18, 'chimera',         4, 1.00::numeric, 1.00::numeric, 1.00::numeric),
  (19, 'mountain_dragon', 3, 1.00::numeric, 1.00::numeric, 1.00::numeric),
  (20, 'mountain_dragon', 4, 1.50::numeric, 1.40::numeric, 1.05::numeric),
  (21, 'demon_lord',      3, 1.00::numeric, 1.00::numeric, 1.00::numeric)
) AS w(wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
ON CONFLICT (config_id, wave_number) DO NOTHING;
