-- 055_tt_hero_identity_cleanup.sql
-- Stop the human barracks heroes from inheriting direct creature identity keys.
-- These TT hero unit rows remain loadout-only, but get authored human-facing stats
-- instead of continuing to mirror Cyclops/Chimera/Manticora defaults.

UPDATE skin_catalog
SET unit_type = skin_key
WHERE skin_key IN ('tt_king', 'tt_paladin', 'tt_commander');

UPDATE unit_types
SET
  name = 'King',
  description = 'Castle hero unlock. Regal frontline tank summoned from a Barracks once the civic branch reaches Castle.',
  hp = 240,
  attack_damage = 15.0,
  attack_speed = 0.90,
  range = 0.22,
  path_speed = 0.032,
  damage_type = 'PHYSICAL',
  armor_type = 'HEAVY',
  damage_reduction_pct = 18,
  send_cost = 0,
  build_cost = 70,
  income = 0,
  usage_scope = 'loadout_only',
  display_to_players = TRUE,
  updated_at = NOW()
WHERE key = 'tt_king';

UPDATE unit_types
SET
  name = 'Paladin',
  description = 'Castle hero unlock. Durable frontline champion summoned from a Barracks after reaching Castle.',
  hp = 210,
  attack_damage = 14.0,
  attack_speed = 1.00,
  range = 0.22,
  path_speed = 0.034,
  damage_type = 'PHYSICAL',
  armor_type = 'HEAVY',
  damage_reduction_pct = 14,
  send_cost = 0,
  build_cost = 60,
  income = 0,
  usage_scope = 'loadout_only',
  display_to_players = TRUE,
  updated_at = NOW()
WHERE key = 'tt_paladin';

UPDATE unit_types
SET
  name = 'Commander',
  description = 'Castle hero unlock. Support hero summoned from a Barracks to reinforce the line from range once Castle is complete.',
  hp = 165,
  attack_damage = 11.0,
  attack_speed = 0.95,
  range = 0.36,
  path_speed = 0.031,
  damage_type = 'MAGIC',
  armor_type = 'MEDIUM',
  damage_reduction_pct = 6,
  send_cost = 0,
  build_cost = 55,
  income = 0,
  projectile_travel_ticks = 8,
  usage_scope = 'loadout_only',
  display_to_players = TRUE,
  updated_at = NOW()
WHERE key = 'tt_commander';
