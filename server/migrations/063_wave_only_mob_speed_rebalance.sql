-- Migration 063: rebalance wave-only dungeon mob move speeds so they stay
-- meaningfully slower than the current defender roster.
--
-- We preserve the original authored creature ordering from migrations 034/051,
-- map that ordering into a target band equal to 75% of the active
-- loadout_only defender speed band, and clamp with LEAST() so this migration
-- only slows mobs down. Any mob that is already below the target band keeps
-- its slower current speed.

WITH defender_band AS (
  SELECT
    MIN(path_speed) * 0.75::numeric AS min_target_speed,
    MAX(path_speed) * 0.75::numeric AS max_target_speed
  FROM unit_types
  WHERE enabled = TRUE
    AND usage_scope = 'loadout_only'
),
mob_baseline (key, authored_path_speed) AS (
  VALUES
    ('kobold',          0.0650::numeric),
    ('goblin',          0.0580::numeric),
    ('giant_rat',       0.0550::numeric),
    ('fantasy_wolf',    0.0550::numeric),
    ('harpy',           0.0520::numeric),
    ('giant_viper',     0.0500::numeric),
    ('darkness_spider', 0.0480::numeric),
    ('lizard_warrior',  0.0450::numeric),
    ('ghoul',           0.0420::numeric),
    ('hobgoblin',       0.0420::numeric),
    ('wyvern',          0.0420::numeric),
    ('werewolf',        0.0400::numeric),
    ('orc',             0.0380::numeric),
    ('vampire',         0.0380::numeric),
    ('griffin',         0.0350::numeric),
    ('skeleton_knight', 0.0300::numeric),
    ('dragonide',       0.0300::numeric),
    ('chimera',         0.0300::numeric),
    ('undead_warrior',  0.0280::numeric),
    ('manticora',       0.0280::numeric),
    ('troll',           0.0250::numeric),
    ('mountain_dragon', 0.0250::numeric),
    ('evil_watcher',    0.0250::numeric),
    ('ogre',            0.0220::numeric),
    ('mummy',           0.0200::numeric),
    ('ice_golem',       0.0200::numeric),
    ('demon_lord',      0.0200::numeric),
    ('cyclops',         0.0180::numeric),
    ('hydra',           0.0180::numeric),
    ('oak_tree_ent',    0.0120::numeric)
),
baseline_range AS (
  SELECT
    MIN(authored_path_speed) AS min_authored_speed,
    MAX(authored_path_speed) AS max_authored_speed
  FROM mob_baseline
),
rebalance AS (
  SELECT
    ut.id,
    ROUND((
      CASE
        WHEN COALESCE(ut.path_speed, 0) > 0 THEN LEAST(ut.path_speed, computed.target_speed)
        ELSE computed.target_speed
      END
    )::numeric, 4) AS new_path_speed
  FROM unit_types ut
  JOIN mob_baseline mb
    ON mb.key = ut.key
  CROSS JOIN defender_band db
  CROSS JOIN baseline_range br
  CROSS JOIN LATERAL (
    SELECT CASE
      WHEN db.min_target_speed IS NULL
        OR db.max_target_speed IS NULL
        OR br.min_authored_speed IS NULL
        OR br.max_authored_speed IS NULL
        OR br.max_authored_speed = br.min_authored_speed
      THEN mb.authored_path_speed
      ELSE (
        db.min_target_speed
        + ((mb.authored_path_speed - br.min_authored_speed)
            / NULLIF(br.max_authored_speed - br.min_authored_speed, 0))
          * (db.max_target_speed - db.min_target_speed)
      )
    END AS target_speed
  ) AS computed
  WHERE ut.enabled = TRUE
    AND ut.usage_scope = 'wave_only'
)
UPDATE unit_types ut
SET
  path_speed = rebalance.new_path_speed,
  updated_at = NOW()
FROM rebalance
WHERE ut.id = rebalance.id
  AND ut.path_speed IS DISTINCT FROM rebalance.new_path_speed;
