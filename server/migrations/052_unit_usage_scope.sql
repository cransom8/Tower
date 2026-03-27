-- 052_unit_usage_scope.sql
-- Add explicit unit usage scope and split the current creature roster from the
-- Tiny Toons / human loadout roster.

ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS usage_scope TEXT NOT NULL DEFAULT 'both'
  CHECK (usage_scope IN ('wave_only', 'loadout_only', 'both', 'disabled'));

WITH tt_source AS (
  SELECT
    s.skin_key AS key,
    s.name,
    COALESCE(NULLIF(TRIM(s.description), ''), src.description) AS description,
    src.enabled,
    src.hp,
    src.attack_damage,
    src.attack_speed,
    src.range,
    src.path_speed,
    src.damage_type,
    src.armor_type,
    src.damage_reduction_pct,
    src.send_cost,
    src.build_cost,
    src.income,
    src.refund_pct,
    src.barracks_scales_hp,
    src.barracks_scales_dmg,
    src.icon_url,
    src.sprite_url,
    src.animation_url,
    src.sound_spawn,
    src.sound_attack,
    src.sound_hit,
    src.sound_death,
    src.skin_pack_id,
    COALESCE(src.display_to_players, TRUE) AS display_to_players,
    src.sprite_url_front,
    src.sprite_url_back,
    src.animation_url_front,
    src.animation_url_back,
    src.idle_sprite_url,
    src.idle_sprite_url_front,
    src.idle_sprite_url_back,
    src.bounty,
    src.projectile_travel_ticks,
    src.special_props,
    src.projectile_def_id
  FROM skin_catalog s
  JOIN unit_types src ON src.key = s.unit_type
  WHERE s.skin_key LIKE 'tt\_%' ESCAPE '\'
)
INSERT INTO unit_types (
  key,
  name,
  description,
  enabled,
  hp,
  attack_damage,
  attack_speed,
  range,
  path_speed,
  damage_type,
  armor_type,
  damage_reduction_pct,
  send_cost,
  build_cost,
  income,
  refund_pct,
  barracks_scales_hp,
  barracks_scales_dmg,
  icon_url,
  sprite_url,
  animation_url,
  sound_spawn,
  sound_attack,
  sound_hit,
  sound_death,
  skin_pack_id,
  display_to_players,
  sprite_url_front,
  sprite_url_back,
  animation_url_front,
  animation_url_back,
  idle_sprite_url,
  idle_sprite_url_front,
  idle_sprite_url_back,
  bounty,
  projectile_travel_ticks,
  special_props,
  projectile_def_id,
  usage_scope
)
SELECT
  key,
  name,
  description,
  enabled,
  hp,
  attack_damage,
  attack_speed,
  range,
  path_speed,
  damage_type,
  armor_type,
  damage_reduction_pct,
  send_cost,
  build_cost,
  income,
  refund_pct,
  barracks_scales_hp,
  barracks_scales_dmg,
  icon_url,
  sprite_url,
  animation_url,
  sound_spawn,
  sound_attack,
  sound_hit,
  sound_death,
  skin_pack_id,
  display_to_players,
  sprite_url_front,
  sprite_url_back,
  animation_url_front,
  animation_url_back,
  idle_sprite_url,
  idle_sprite_url_front,
  idle_sprite_url_back,
  bounty,
  projectile_travel_ticks,
  special_props,
  projectile_def_id,
  'loadout_only'
FROM tt_source
ON CONFLICT (key) DO NOTHING;

UPDATE unit_types ut
SET
  name = s.name,
  description = COALESCE(NULLIF(TRIM(s.description), ''), ut.description),
  usage_scope = 'loadout_only',
  display_to_players = TRUE,
  updated_at = NOW()
FROM skin_catalog s
WHERE ut.key = s.skin_key
  AND s.skin_key LIKE 'tt\_%' ESCAPE '\';

INSERT INTO unit_content_metadata (
  unit_type_id,
  content_key,
  addressables_label,
  prefab_address,
  placeholder_key,
  catalog_url,
  content_url,
  version_tag,
  content_hash,
  dependency_keys,
  metadata,
  is_critical,
  enabled
)
SELECT
  ut.id,
  COALESCE(scm.content_key, s.skin_key),
  scm.addressables_label,
  scm.prefab_address,
  scm.placeholder_key,
  scm.catalog_url,
  scm.content_url,
  COALESCE(scm.version_tag, '1'),
  scm.content_hash,
  COALESCE(scm.dependency_keys, '[]'::jsonb),
  COALESCE(scm.metadata, '{}'::jsonb),
  COALESCE(scm.is_critical, FALSE),
  COALESCE(scm.enabled, TRUE)
FROM skin_catalog s
JOIN unit_types ut ON ut.key = s.skin_key
LEFT JOIN skin_content_metadata scm ON scm.skin_catalog_id = s.id
WHERE s.skin_key LIKE 'tt\_%' ESCAPE '\'
ON CONFLICT (unit_type_id) DO UPDATE SET
  content_key = EXCLUDED.content_key,
  addressables_label = EXCLUDED.addressables_label,
  prefab_address = EXCLUDED.prefab_address,
  placeholder_key = EXCLUDED.placeholder_key,
  catalog_url = EXCLUDED.catalog_url,
  content_url = EXCLUDED.content_url,
  version_tag = EXCLUDED.version_tag,
  content_hash = EXCLUDED.content_hash,
  dependency_keys = EXCLUDED.dependency_keys,
  metadata = EXCLUDED.metadata,
  is_critical = EXCLUDED.is_critical,
  enabled = EXCLUDED.enabled,
  updated_at = NOW();

UPDATE unit_types
SET usage_scope = 'disabled',
    updated_at = NOW()
WHERE enabled = FALSE;

UPDATE unit_types
SET usage_scope = 'wave_only',
    updated_at = NOW()
WHERE enabled = TRUE
  AND key NOT LIKE 'tt\_%' ESCAPE '\';

UPDATE unit_types
SET usage_scope = 'disabled',
    updated_at = NOW()
WHERE key = 'wall_placeholder';
