-- Migration 061: strengthen the default wave-defense curve so dungeon pressure
-- keeps climbing through boss rounds and the late game stops backsliding.

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
  (4,  'fantasy_wolf',    13, 1.15::numeric, 1.10::numeric, 1.05::numeric),
  (5,  'wyvern',           5, 1.85::numeric, 1.70::numeric, 1.02::numeric),
  (6,  'ghoul',           14, 1.20::numeric, 1.12::numeric, 1.00::numeric),
  (7,  'hobgoblin',       14, 1.25::numeric, 1.18::numeric, 1.00::numeric),
  (8,  'lizard_warrior',  14, 1.35::numeric, 1.26::numeric, 1.04::numeric),
  (9,  'darkness_spider', 14, 1.42::numeric, 1.32::numeric, 1.05::numeric),
  (10, 'demon_lord',       4, 2.00::numeric, 1.90::numeric, 1.02::numeric),
  (11, 'giant_viper',     14, 1.70::numeric, 1.50::numeric, 1.08::numeric),
  (12, 'undead_warrior',  14, 1.75::numeric, 1.50::numeric, 1.02::numeric),
  (13, 'orc',             14, 1.85::numeric, 1.58::numeric, 1.02::numeric),
  (14, 'skeleton_knight', 13, 1.95::numeric, 1.68::numeric, 1.03::numeric),
  (15, 'oak_tree_ent',     6, 2.25::numeric, 1.90::numeric, 0.98::numeric),
  (16, 'harpy',           15, 2.10::numeric, 1.75::numeric, 1.14::numeric),
  (17, 'troll',           11, 2.15::numeric, 1.75::numeric, 1.02::numeric),
  (18, 'mummy',           12, 2.35::numeric, 1.95::numeric, 1.00::numeric),
  (19, 'dragonide',       12, 2.55::numeric, 2.05::numeric, 1.02::numeric),
  (20, 'chimera',          6, 2.90::numeric, 2.35::numeric, 1.02::numeric),
  (21, 'werewolf',        11, 2.60::numeric, 2.15::numeric, 1.12::numeric),
  (22, 'ogre',            11, 2.75::numeric, 2.25::numeric, 1.00::numeric),
  (23, 'vampire',         13, 2.95::numeric, 2.40::numeric, 1.05::numeric),
  (24, 'evil_watcher',    14, 3.20::numeric, 2.60::numeric, 1.05::numeric),
  (25, 'mountain_dragon',  6, 3.80::numeric, 3.30::numeric, 1.02::numeric),
  (26, 'griffin',         10, 3.35::numeric, 2.70::numeric, 1.08::numeric),
  (27, 'cyclops',         10, 3.50::numeric, 2.90::numeric, 1.00::numeric),
  (28, 'manticora',        9, 3.65::numeric, 3.05::numeric, 1.02::numeric),
  (29, 'ice_golem',       10, 3.95::numeric, 3.25::numeric, 1.00::numeric),
  (30, 'hydra',            6, 5.10::numeric, 4.60::numeric, 1.02::numeric)
) AS w(wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult);
