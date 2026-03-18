-- Migration 041: Add defender stats to Forge Wars loadout units
--
-- Goblin/orc/troll/vampire/wyvern remain behavior_mode='moving' so they still
-- function as wave enemies when spawned by the server. The place_unit handler
-- no longer gates on behavior_mode (see sim-multilane.js); it only requires
-- build_cost > 0 and range > 0.
--
-- range is normalised to [0,1]; sim multiplies by GRID_W (11) for tile distance.
-- build_cost and income match roughly the existing 'both' units from migration 031.
-- projectile_travel_ticks: ticks until projectile hits target when firing as a defender.

UPDATE unit_types SET
  range                   = 0.27,
  build_cost              = 8,
  income                  = 1,
  projectile_travel_ticks = 6,
  updated_at              = NOW()
WHERE key = 'goblin';

UPDATE unit_types SET
  range                   = 0.22,
  build_cost              = 14,
  income                  = 1,
  projectile_travel_ticks = 7,
  updated_at              = NOW()
WHERE key = 'orc';

UPDATE unit_types SET
  range                   = 0.20,
  build_cost              = 16,
  income                  = 2,
  projectile_travel_ticks = 7,
  updated_at              = NOW()
WHERE key = 'troll';

UPDATE unit_types SET
  range                   = 0.30,
  build_cost              = 20,
  income                  = 2,
  projectile_travel_ticks = 7,
  updated_at              = NOW()
WHERE key = 'vampire';

UPDATE unit_types SET
  range                   = 0.25,
  build_cost              = 22,
  income                  = 2,
  projectile_travel_ticks = 7,
  updated_at              = NOW()
WHERE key = 'wyvern';
