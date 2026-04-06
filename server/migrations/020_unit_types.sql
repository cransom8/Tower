-- 020_unit_types.sql
-- Unit Types, Unit Type Ability Assignments, Config Versions
-- Depends on: 017_towers.sql (tower_abilities table for FK)

-- ── Unit Types ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS unit_types (
  id                   SERIAL PRIMARY KEY,
  key                  TEXT NOT NULL UNIQUE,
  name                 TEXT NOT NULL,
  description          TEXT NOT NULL DEFAULT '',
  behavior_mode        TEXT NOT NULL DEFAULT 'moving'
                         CHECK (behavior_mode IN ('fixed','moving','both')),
  enabled              BOOLEAN NOT NULL DEFAULT TRUE,

  -- Combat stats
  hp                   NUMERIC(10,4) NOT NULL DEFAULT 100,
  attack_damage        NUMERIC(10,4) NOT NULL DEFAULT 10,
  attack_speed         NUMERIC(10,4) NOT NULL DEFAULT 1.0,
  range                NUMERIC(10,4) NOT NULL DEFAULT 0.0,
  path_speed           NUMERIC(10,4) NOT NULL DEFAULT 0.04,

  -- Damage / armor typing
  damage_type          TEXT NOT NULL DEFAULT 'NORMAL'
                         CHECK (damage_type IN ('NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE')),
  armor_type           TEXT NOT NULL DEFAULT 'UNARMORED'
                         CHECK (armor_type IN ('UNARMORED','LIGHT','MEDIUM','HEAVY','MAGIC')),
  damage_reduction_pct INTEGER NOT NULL DEFAULT 0
                         CHECK (damage_reduction_pct >= 0 AND damage_reduction_pct <= 80),

  -- Economy
  send_cost            INTEGER NOT NULL DEFAULT 1,
  build_cost           INTEGER NOT NULL DEFAULT 10,
  income               INTEGER NOT NULL DEFAULT 0,
  refund_pct           INTEGER NOT NULL DEFAULT 70
                         CHECK (refund_pct >= 0 AND refund_pct <= 100),

  -- Barracks scaling flags (Phase F)
  barracks_scales_hp   BOOLEAN NOT NULL DEFAULT TRUE,
  barracks_scales_dmg  BOOLEAN NOT NULL DEFAULT FALSE,

  -- Visual assets
  icon_url             TEXT,
  sprite_url           TEXT,
  animation_url        TEXT,

  -- Sound cues (Phase H)
  sound_spawn          TEXT,
  sound_attack         TEXT,
  sound_hit            TEXT,
  sound_death          TEXT,

  created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Startup replays the full migration chain, so older content migrations must still
-- be able to reference these legacy barracks scaling flags before 065 removes them.
ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS barracks_scales_hp BOOLEAN NOT NULL DEFAULT TRUE,
  ADD COLUMN IF NOT EXISTS barracks_scales_dmg BOOLEAN NOT NULL DEFAULT FALSE;

-- ── Unit Type → Ability assignments ──────────────────────────────────────────
CREATE TABLE IF NOT EXISTS unit_type_ability_assignments (
  id           SERIAL PRIMARY KEY,
  unit_type_id INTEGER NOT NULL REFERENCES unit_types(id) ON DELETE CASCADE,
  ability_key  TEXT    NOT NULL REFERENCES tower_abilities(key) ON DELETE CASCADE,
  params       JSONB   NOT NULL DEFAULT '{}',
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (unit_type_id, ability_key)
);

CREATE INDEX IF NOT EXISTS idx_unit_type_ability_asgn_unit
  ON unit_type_ability_assignments(unit_type_id);

-- ── Config Versions ───────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS config_versions (
  id         SERIAL PRIMARY KEY,
  snapshot   JSONB NOT NULL DEFAULT '{}',
  created_by TEXT,
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Legacy unit seeding removed. The live roster is defined by later migrations
-- (034+ and 052+) and should not recreate retired classic units on startup.
