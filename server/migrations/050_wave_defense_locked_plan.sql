-- Migration 050: lock the default Forge Wars wave-defense plan to 30 waves
-- and rename the player-facing loadout copy to Race Progression.

WITH target_config AS (
  SELECT id
  FROM ml_wave_configs
  WHERE is_default = TRUE
  ORDER BY id
  LIMIT 1
),
created_config AS (
  INSERT INTO ml_wave_configs (name, description, is_default)
  SELECT
    'Standard',
    'Locked 30-wave creature progression for Forge Wars wave defense.',
    TRUE
  WHERE NOT EXISTS (SELECT 1 FROM target_config)
  RETURNING id
),
active_config AS (
  SELECT id FROM target_config
  UNION ALL
  SELECT id FROM created_config
  LIMIT 1
)
UPDATE ml_wave_configs
SET name = 'Standard',
    description = 'Locked 30-wave creature progression for Forge Wars wave defense.'
WHERE id = (SELECT id FROM active_config);

WITH active_config AS (
  SELECT id
  FROM ml_wave_configs
  WHERE is_default = TRUE
  ORDER BY id
  LIMIT 1
)
DELETE FROM ml_waves
WHERE config_id = (SELECT id FROM active_config);

WITH active_config AS (
  SELECT id
  FROM ml_wave_configs
  WHERE is_default = TRUE
  ORDER BY id
  LIMIT 1
)
INSERT INTO ml_waves (config_id, wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
SELECT
  (SELECT id FROM active_config),
  w.wave_number,
  w.unit_type,
  w.spawn_qty,
  w.hp_mult,
  w.dmg_mult,
  w.speed_mult
FROM (VALUES
  (1,  'giant_rat',       12, 0.95::numeric, 0.95::numeric, 1.00::numeric),
  (2,  'kobold',          14, 1.00::numeric, 1.00::numeric, 1.03::numeric),
  (3,  'goblin',          15, 1.05::numeric, 1.02::numeric, 1.00::numeric),
  (4,  'fantasy_wolf',    12, 1.10::numeric, 1.08::numeric, 1.05::numeric),
  (5,  'wyvern',           1, 1.15::numeric, 1.12::numeric, 1.00::numeric),
  (6,  'ghoul',           14, 1.20::numeric, 1.12::numeric, 1.00::numeric),
  (7,  'hobgoblin',       14, 1.25::numeric, 1.18::numeric, 1.00::numeric),
  (8,  'lizard_warrior',  13, 1.30::numeric, 1.22::numeric, 1.02::numeric),
  (9,  'darkness_spider', 13, 1.35::numeric, 1.26::numeric, 1.04::numeric),
  (10, 'demon_lord',       1, 1.45::numeric, 1.40::numeric, 1.00::numeric),
  (11, 'giant_viper',     13, 1.45::numeric, 1.30::numeric, 1.06::numeric),
  (12, 'undead_warrior',  12, 1.55::numeric, 1.34::numeric, 1.00::numeric),
  (13, 'orc',             12, 1.65::numeric, 1.40::numeric, 1.00::numeric),
  (14, 'skeleton_knight', 11, 1.75::numeric, 1.48::numeric, 1.00::numeric),
  (15, 'oak_tree_ent',     1, 1.85::numeric, 1.55::numeric, 0.95::numeric),
  (16, 'harpy',           12, 1.85::numeric, 1.52::numeric, 1.10::numeric),
  (17, 'troll',           10, 1.95::numeric, 1.60::numeric, 1.00::numeric),
  (18, 'mummy',           10, 2.05::numeric, 1.68::numeric, 0.98::numeric),
  (19, 'dragonide',        9, 2.15::numeric, 1.78::numeric, 1.00::numeric),
  (20, 'chimera',          1, 2.30::numeric, 1.95::numeric, 1.00::numeric),
  (21, 'werewolf',         9, 2.35::numeric, 1.95::numeric, 1.08::numeric),
  (22, 'ogre',             8, 2.50::numeric, 2.05::numeric, 0.98::numeric),
  (23, 'vampire',          8, 2.65::numeric, 2.15::numeric, 1.02::numeric),
  (24, 'evil_watcher',     7, 2.80::numeric, 2.25::numeric, 1.00::numeric),
  (25, 'mountain_dragon',  1, 3.00::numeric, 2.50::numeric, 1.00::numeric),
  (26, 'griffin',          7, 3.05::numeric, 2.45::numeric, 1.05::numeric),
  (27, 'cyclops',          6, 3.20::numeric, 2.60::numeric, 0.98::numeric),
  (28, 'manticora',        5, 3.35::numeric, 2.75::numeric, 1.00::numeric),
  (29, 'ice_golem',        4, 3.55::numeric, 2.95::numeric, 0.96::numeric),
  (30, 'hydra',            1, 4.50::numeric, 4.00::numeric, 1.00::numeric)
) AS w(wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult);

UPDATE branding_settings
SET config_json =
  jsonb_set(
    jsonb_set(
      jsonb_set(
        COALESCE(config_json, '{}'::jsonb),
        '{loadoutTitle}',
        CASE
          WHEN COALESCE(config_json->>'loadoutTitle', '') IN ('', 'Loadout')
            THEN '"Race Progression"'::jsonb
          ELSE to_jsonb(config_json->>'loadoutTitle')
        END,
        true
      ),
      '{loadoutHint}',
      CASE
        WHEN COALESCE(config_json->>'loadoutHint', '') IN ('', 'Choose five units for this match')
          THEN '"Review races, units, and upgrade paths before the match begins"'::jsonb
        ELSE to_jsonb(config_json->>'loadoutHint')
      END,
      true
    ),
    '{loadoutBuilderTitle}',
    CASE
      WHEN COALESCE(config_json->>'loadoutBuilderTitle', '') IN ('', 'Loadout Builder')
        THEN '"Race Progression Viewer"'::jsonb
      ELSE to_jsonb(config_json->>'loadoutBuilderTitle')
    END,
    true
  )
WHERE singleton = TRUE;
