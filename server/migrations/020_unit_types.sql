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

-- ── Seed: 11 unit types ───────────────────────────────────────────────────────
-- Attackers (moving): runner, footman, ironclad, warlock, golem
-- Defenders (fixed):  archer, fighter, mage, ballista, cannon
-- Special:            wall_placeholder
INSERT INTO unit_types (
  key, name, description, behavior_mode,
  hp, attack_damage, attack_speed, range, path_speed,
  damage_type, armor_type, damage_reduction_pct,
  send_cost, build_cost, income, refund_pct,
  barracks_scales_hp, barracks_scales_dmg
) VALUES
  ('runner',
   'Runner',   'Fast light attacker. Low HP, cheap to send.',
   'moving', 60,   5.0, 1.0, 0.0,  0.055, 'NORMAL',   'UNARMORED',  0,  1,  0, 0, 70, TRUE,  FALSE),
  ('footman',
   'Footman',  'Balanced infantry attacker.',
   'moving', 90,   7.0, 1.0, 0.0,  0.040, 'NORMAL',   'LIGHT',      0,  2,  0, 0, 70, TRUE,  FALSE),
  ('ironclad',
   'Ironclad', 'Heavy armoured attacker. Slow but durable.',
   'moving', 160, 10.0, 0.8, 0.0,  0.025, 'NORMAL',   'HEAVY',     20,  4,  0, 0, 70, TRUE,  FALSE),
  ('warlock',
   'Warlock',  'Magic attacker. Bypasses physical armour.',
   'moving',  80, 12.0, 1.0, 0.0,  0.035, 'MAGIC',    'LIGHT',      0,  5,  0, 0, 70, TRUE,  TRUE),
  ('golem',
   'Golem',    'Boss-tier attacker. Extremely high HP and damage.',
   'moving', 240, 18.0, 0.7, 0.0,  0.020, 'PHYSICAL', 'HEAVY',     30, 10,  0, 0, 70, TRUE,  FALSE),
  ('archer',
   'Archer',   'Fast ranged defender. Effective against light armour.',
   'fixed',   50,  6.6, 1.0, 0.36, 0.0,  'PIERCE',   'UNARMORED',  0,  0, 10, 1, 70, FALSE, FALSE),
  ('fighter',
   'Fighter',  'Close-range melee defender. High single-target damage.',
   'fixed',   80,  8.8, 1.0, 0.22, 0.0,  'NORMAL',   'MEDIUM',     0,  0, 12, 1, 70, FALSE, FALSE),
  ('mage',
   'Mage',     'Magic damage defender. Bypasses physical armour.',
   'fixed',   60, 13.2, 1.0, 0.35, 0.0,  'MAGIC',    'LIGHT',      0,  0, 24, 1, 70, FALSE, TRUE),
  ('ballista',
   'Ballista', 'Long-range siege defender. Excels against heavy units.',
   'fixed',   70, 12.1, 1.0, 0.40, 0.0,  'SIEGE',    'MEDIUM',     0,  0, 20, 1, 70, FALSE, FALSE),
  ('cannon',
   'Cannon',   'Slow but devastating area-of-effect defender.',
   'fixed',   90,  8.0, 0.8, 0.32, 0.0,  'SPLASH',   'MEDIUM',     0,  0, 30, 1, 70, FALSE, FALSE),
  ('wall_placeholder',
   'Wall',     'Placeholder tile. Place to later upgrade to a fixed defender.',
   'both',   200,  0.0, 0.0, 0.0,  0.0,  'NORMAL',   'HEAVY',     40,  0,  2, 0, 100, FALSE, FALSE)
ON CONFLICT (key) DO NOTHING;
