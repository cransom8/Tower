-- Restore the two Fantasy Animals Pack units if they were manually removed or hidden.
-- This keeps the DB-driven roster aligned with the Unity registry and remote portrait/unit linking.

INSERT INTO unit_types (
  key, name, description, enabled,
  hp, attack_damage, attack_speed, range, path_speed,
  damage_type, armor_type, damage_reduction_pct,
  send_cost, build_cost, income, bounty, refund_pct,
  barracks_scales_hp, barracks_scales_dmg,
  display_to_players, projectile_travel_ticks, special_props,
  updated_at
) VALUES
  (
    'giant_rat',
    'Giant Rat',
    'Disease-ridden swarm unit. Extremely cheap.',
    TRUE,
    45, 4.0, 1.1, 0.14, 0.055,
    'NORMAL', 'UNARMORED', 0,
    1, 6, 1, 2, 70,
    TRUE, FALSE,
    TRUE, 5, '{}'::jsonb,
    NOW()
  ),
  (
    'fantasy_wolf',
    'Fantasy Wolf',
    'Pack predator. Fast and piercing bite.',
    TRUE,
    65, 8.0, 1.1, 0.20, 0.055,
    'PIERCE', 'UNARMORED', 0,
    2, 10, 1, 3, 70,
    TRUE, FALSE,
    TRUE, 5, '{}'::jsonb,
    NOW()
  )
ON CONFLICT (key) DO UPDATE SET
  name = EXCLUDED.name,
  description = EXCLUDED.description,
  enabled = TRUE,
  hp = EXCLUDED.hp,
  attack_damage = EXCLUDED.attack_damage,
  attack_speed = EXCLUDED.attack_speed,
  range = EXCLUDED.range,
  path_speed = EXCLUDED.path_speed,
  damage_type = EXCLUDED.damage_type,
  armor_type = EXCLUDED.armor_type,
  damage_reduction_pct = EXCLUDED.damage_reduction_pct,
  send_cost = EXCLUDED.send_cost,
  build_cost = EXCLUDED.build_cost,
  income = EXCLUDED.income,
  bounty = EXCLUDED.bounty,
  refund_pct = EXCLUDED.refund_pct,
  barracks_scales_hp = EXCLUDED.barracks_scales_hp,
  barracks_scales_dmg = EXCLUDED.barracks_scales_dmg,
  display_to_players = TRUE,
  projectile_travel_ticks = EXCLUDED.projectile_travel_ticks,
  special_props = EXCLUDED.special_props,
  updated_at = NOW();

INSERT INTO unit_content_metadata (
  unit_type_id,
  content_key,
  is_critical,
  enabled,
  updated_at
)
SELECT
  u.id,
  u.key,
  TRUE,
  TRUE,
  NOW()
FROM unit_types u
WHERE u.key IN ('giant_rat', 'fantasy_wolf')
ON CONFLICT (unit_type_id) DO UPDATE SET
  content_key = EXCLUDED.content_key,
  is_critical = TRUE,
  enabled = TRUE,
  updated_at = NOW();
