-- 057_tt_human_roster_balance_pass.sql
-- Explicitly author the human TT roster instead of inheriting creature-era
-- defender stats. This restores intended battlefield reach ordering:
-- shield front, sword line, spear line, mages, archers, then priests/healers.
-- Priests and Bishop are marked as healer-support units in special_props.

WITH roster_balance (
  key,
  hp,
  attack_damage,
  attack_speed,
  range_value,
  path_speed,
  send_cost,
  build_cost,
  income,
  bounty,
  damage_type,
  armor_type,
  damage_reduction_pct,
  projectile_travel_ticks,
  projectile_key,
  support_role,
  heal_amount
) AS (
  VALUES
    ('tt_settler',         54,  4,  1.00, 0.08, 0.24,  8,  9, 3,  2, 'NORMAL',   'LIGHT',  0, 5,  NULL,           NULL,     NULL),
    ('tt_scout',           70,  7,  1.20, 0.13, 0.29, 10, 11, 1,  2, 'NORMAL',   'LIGHT',  0, 5,  NULL,           NULL,     NULL),
    ('tt_peasant',         80,  7,  1.10, 0.13, 0.24, 10, 12, 1,  2, 'NORMAL',   'LIGHT',  0, 5,  NULL,           NULL,     NULL),
    ('tt_light_infantry', 105, 10,  1.00, 0.15, 0.23, 14, 18, 1,  4, 'PHYSICAL', 'MEDIUM', 4, 6,  NULL,           NULL,     NULL),
    ('tt_mounted_knight', 145, 14,  0.95, 0.16, 0.24, 20, 26, 2,  7, 'PHYSICAL', 'MEDIUM',10, 6,  NULL,           NULL,     NULL),
    ('tt_spearman',        90,  8,  1.00, 0.17, 0.22, 12, 14, 1,  3, 'PIERCE',   'LIGHT',  2, 6,  NULL,           NULL,     NULL),
    ('tt_halberdier',     120, 12,  0.95, 0.18, 0.21, 17, 22, 2,  5, 'PIERCE',   'MEDIUM', 6, 6,  NULL,           NULL,     NULL),
    ('tt_light_cavalry',  150, 15,  0.95, 0.18, 0.25, 22, 28, 2,  7, 'PIERCE',   'MEDIUM', 8, 6,  NULL,           NULL,     NULL),
    ('tt_heavy_infantry', 130,  9,  0.90, 0.10, 0.20, 16, 19, 1,  4, 'PHYSICAL', 'HEAVY', 10, 6,  NULL,           NULL,     NULL),
    ('tt_heavy_swordman', 170, 13,  0.85, 0.11, 0.19, 20, 26, 2,  6, 'PHYSICAL', 'HEAVY', 16, 6,  NULL,           NULL,     NULL),
    ('tt_heavy_cavalry',  220, 17,  0.82, 0.12, 0.20, 28, 34, 2,  9, 'PHYSICAL', 'HEAVY', 20, 6,  NULL,           NULL,     NULL),
    ('tt_archer',          74, 11,  1.05, 0.36, 0.23, 16, 18, 2,  4, 'PIERCE',   'LIGHT',  0, 7,  'arrow',        NULL,     NULL),
    ('tt_crossbowman',     92, 16,  0.82, 0.39, 0.21, 21, 26, 2,  6, 'PIERCE',   'MEDIUM', 2, 8,  'bolt',         NULL,     NULL),
    ('tt_mounted_scout',  108, 15,  1.08, 0.42, 0.25, 26, 32, 2,  8, 'PIERCE',   'LIGHT',  2, 7,  'arrow',        NULL,     NULL),
    ('tt_mage',            78, 14,  0.95, 0.31, 0.20, 18, 21, 2,  5, 'MAGIC',    'LIGHT',  0, 7,  'magic_orb',    NULL,     NULL),
    ('tt_mounted_mage',    96, 18,  0.92, 0.34, 0.22, 24, 29, 2,  7, 'MAGIC',    'LIGHT',  2, 7,  'magic_orb',    NULL,     NULL),
    ('tt_mounted_king',   118, 24,  0.85, 0.37, 0.21, 32, 40, 3, 10, 'MAGIC',    'MEDIUM', 8, 8,  'magic_orb',    NULL,     NULL),
    ('tt_mounted_priest',  76,  3,  0.90, 0.44, 0.21, 17, 20, 2,  4, 'MAGIC',    'LIGHT',  0, 7,  NULL,           'healer',  8),
    ('tt_priest',          92,  4,  0.95, 0.47, 0.20, 22, 27, 2,  6, 'MAGIC',    'LIGHT',  2, 7,  NULL,           'healer', 12),
    ('tt_high_priest',    110,  5,  1.00, 0.50, 0.19, 28, 34, 3,  8, 'MAGIC',    'MEDIUM', 6, 7,  NULL,           'healer', 17),
    ('tt_paladin',        225, 16,  0.90, 0.12, 0.20, 60, 60, 4, 12, 'PHYSICAL', 'HEAVY', 18, 6,  NULL,           NULL,     NULL),
    ('tt_commander',      170,  6,  1.00, 0.52, 0.19, 55, 55, 4, 11, 'MAGIC',    'MEDIUM', 8, 7,  NULL,           'healer', 22),
    ('tt_king',           255, 18,  0.88, 0.13, 0.19, 70, 70, 4, 14, 'PHYSICAL', 'HEAVY', 20, 6,  NULL,           NULL,     NULL),
    ('tt_mounted_paladin',250, 20,  0.86, 0.14, 0.20, 38, 46, 3, 11, 'PHYSICAL', 'HEAVY', 18, 6,  NULL,           NULL,     NULL)
)
UPDATE unit_types AS ut
SET
  hp = rb.hp,
  attack_damage = rb.attack_damage,
  attack_speed = rb.attack_speed,
  range = rb.range_value,
  path_speed = rb.path_speed,
  send_cost = rb.send_cost,
  build_cost = rb.build_cost,
  income = rb.income,
  bounty = rb.bounty,
  damage_type = rb.damage_type,
  armor_type = rb.armor_type,
  damage_reduction_pct = rb.damage_reduction_pct,
  projectile_travel_ticks = rb.projectile_travel_ticks,
  projectile_def_id = (
    SELECT pd.id
    FROM projectile_definitions pd
    WHERE pd.key = rb.projectile_key
  ),
  special_props = CASE
    WHEN rb.support_role IS NOT NULL THEN
      (COALESCE(ut.special_props, '{}'::jsonb) - 'supportRole' - 'support_role' - 'healAmount' - 'heal_amount')
      || jsonb_build_object('supportRole', rb.support_role, 'healAmount', rb.heal_amount)
    ELSE
      COALESCE(ut.special_props, '{}'::jsonb) - 'supportRole' - 'support_role' - 'healAmount' - 'heal_amount'
  END,
  usage_scope = 'loadout_only',
  updated_at = NOW()
FROM roster_balance rb
WHERE ut.key = rb.key;
