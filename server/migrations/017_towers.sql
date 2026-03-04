-- 017_towers.sql
-- Tower Management & Onboarding System
-- Creates tower_abilities (predefined ability templates), towers (tower definitions),
-- and tower_ability_assignments (many-to-many with configurable params).

-- ── Predefined ability templates ─────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tower_abilities (
  id         SERIAL PRIMARY KEY,
  key        TEXT NOT NULL UNIQUE,
  name       TEXT NOT NULL,
  category   TEXT NOT NULL CHECK (category IN ('damage','status_effect','utility','aura')),
  description TEXT NOT NULL DEFAULT '',
  param_schema JSONB NOT NULL DEFAULT '{}',
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── Tower definitions ─────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS towers (
  id          SERIAL PRIMARY KEY,
  name        TEXT NOT NULL,
  description TEXT NOT NULL DEFAULT '',
  category    TEXT NOT NULL DEFAULT 'damage'
                CHECK (category IN ('damage','slow_control','aura_support','economy','special')),
  enabled     BOOLEAN NOT NULL DEFAULT TRUE,

  -- Base stats
  attack_damage        NUMERIC(10,4) NOT NULL DEFAULT 10,
  attack_speed         NUMERIC(10,4) NOT NULL DEFAULT 1.0,
  range                NUMERIC(10,4) NOT NULL DEFAULT 0.35,
  projectile_speed     NUMERIC(10,4),
  splash_radius        NUMERIC(10,4),
  damage_type          TEXT NOT NULL DEFAULT 'NORMAL'
                         CHECK (damage_type IN ('NORMAL','PIERCE','SPLASH','SIEGE','MAGIC','PHYSICAL','TRUE')),

  -- Targeting (comma-separated list: first,last,strongest,weakest,closest)
  targeting_options    TEXT NOT NULL DEFAULT 'first',

  -- Economy
  base_cost            INTEGER NOT NULL DEFAULT 10,
  upgrade_cost_mult    NUMERIC(6,4) NOT NULL DEFAULT 1.0,

  -- Upgrade scaling (per level)
  damage_scaling       NUMERIC(6,4) NOT NULL DEFAULT 0.12,
  range_scaling        NUMERIC(6,4) NOT NULL DEFAULT 0.015,
  attack_speed_scaling NUMERIC(6,4) NOT NULL DEFAULT 0.015,

  -- Visual asset URLs
  icon_url        TEXT,
  sprite_url      TEXT,
  projectile_url  TEXT,
  animation_url   TEXT,

  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ── Tower → Ability assignments ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS tower_ability_assignments (
  id          SERIAL PRIMARY KEY,
  tower_id    INTEGER NOT NULL REFERENCES towers(id) ON DELETE CASCADE,
  ability_key TEXT    NOT NULL REFERENCES tower_abilities(key) ON DELETE CASCADE,
  params      JSONB   NOT NULL DEFAULT '{}',
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (tower_id, ability_key)
);

CREATE INDEX IF NOT EXISTS idx_tower_abilities_asgn_tower
  ON tower_ability_assignments(tower_id);

-- ── Seed: predefined ability templates ───────────────────────────────────────
INSERT INTO tower_abilities (key, name, category, description, param_schema) VALUES
  ('splash_damage',   'Splash Damage',            'damage',        'Deals damage in an area around the primary target.',
    '{"radius":{"type":"number","label":"Radius","default":0.05,"min":0.01,"max":0.3}}'),
  ('pierce_targets',  'Pierce Targets',            'damage',        'Projectile passes through multiple enemies.',
    '{"max_targets":{"type":"integer","label":"Max Targets","default":3,"min":2,"max":10}}'),
  ('chain_lightning', 'Chain Lightning',           'damage',        'Damage chains to nearby enemies with decay.',
    '{"chains":{"type":"integer","label":"Chains","default":3,"min":1,"max":8},"decay_pct":{"type":"number","label":"Damage Decay %","default":25,"min":0,"max":80}}'),
  ('slow',            'Slow',                      'status_effect', 'Reduces enemy movement speed on hit.',
    '{"slow_pct":{"type":"number","label":"Slow %","default":25,"min":5,"max":90},"duration":{"type":"number","label":"Duration (s)","default":2,"min":0.5,"max":10}}'),
  ('freeze',          'Freeze',                    'status_effect', 'Temporarily stops enemy movement.',
    '{"duration":{"type":"number","label":"Duration (s)","default":1,"min":0.2,"max":5},"proc_chance":{"type":"number","label":"Proc Chance %","default":20,"min":1,"max":100}}'),
  ('poison',          'Poison',                    'status_effect', 'Deals damage over time after hit.',
    '{"dps":{"type":"number","label":"DPS","default":5,"min":1,"max":100},"duration":{"type":"number","label":"Duration (s)","default":4,"min":1,"max":20}}'),
  ('burn',            'Burn',                      'status_effect', 'Deals fire damage over time.',
    '{"dps":{"type":"number","label":"DPS","default":8,"min":1,"max":100},"duration":{"type":"number","label":"Duration (s)","default":3,"min":1,"max":20}}'),
  ('armor_reduction', 'Armor Reduction',           'status_effect', 'Reduces enemy armor on hit.',
    '{"reduction_pct":{"type":"number","label":"Reduction %","default":20,"min":5,"max":75},"duration":{"type":"number","label":"Duration (s)","default":5,"min":1,"max":15}}'),
  ('reveal_stealth',  'Reveal Stealth',            'utility',       'Reveals stealthed enemies in radius.',
    '{"radius":{"type":"number","label":"Radius","default":0.15,"min":0.05,"max":0.5}}'),
  ('knockback',       'Knockback',                 'utility',       'Pushes enemies back toward their spawn on hit.',
    '{"distance":{"type":"number","label":"Distance","default":0.05,"min":0.01,"max":0.2},"proc_chance":{"type":"number","label":"Proc Chance %","default":15,"min":1,"max":100}}'),
  ('teleport_back',   'Teleport Enemy Backwards',  'utility',       'Teleports enemy back toward spawn.',
    '{"distance":{"type":"number","label":"Distance","default":0.2,"min":0.05,"max":0.8},"proc_chance":{"type":"number","label":"Proc Chance %","default":10,"min":1,"max":50}}'),
  ('aura_damage',     'Aura: Damage Boost',        'aura',          'Passively increases nearby tower damage.',
    '{"boost_pct":{"type":"number","label":"Boost %","default":15,"min":1,"max":100},"radius":{"type":"number","label":"Radius","default":0.1,"min":0.05,"max":0.5}}'),
  ('aura_atk_speed',  'Aura: Attack Speed Boost',  'aura',          'Passively increases nearby tower attack speed.',
    '{"boost_pct":{"type":"number","label":"Boost %","default":15,"min":1,"max":100},"radius":{"type":"number","label":"Radius","default":0.1,"min":0.05,"max":0.5}}'),
  ('aura_range',      'Aura: Range Boost',         'aura',          'Passively increases nearby tower range.',
    '{"boost_pct":{"type":"number","label":"Boost %","default":10,"min":1,"max":100},"radius":{"type":"number","label":"Radius","default":0.1,"min":0.05,"max":0.5}}'),
  ('aura_cooldown',   'Aura: Cooldown Reduction',  'aura',          'Passively reduces nearby tower cooldowns.',
    '{"reduction_pct":{"type":"number","label":"Reduction %","default":10,"min":1,"max":75},"radius":{"type":"number","label":"Radius","default":0.1,"min":0.05,"max":0.5}}')
ON CONFLICT (key) DO NOTHING;

-- ── Seed: built-in game towers ────────────────────────────────────────────────
INSERT INTO towers (name, description, category, attack_damage, attack_speed, range, damage_type,
                    base_cost, damage_scaling, range_scaling, attack_speed_scaling, targeting_options) VALUES
  ('Archer',   'Fast ranged tower. Effective against lightly armoured units.',
   'damage', 6.6,  1.0, 0.36, 'PIERCE', 10, 0.12, 0.015, 0.015, 'first'),
  ('Fighter',  'Close-range melee tower. High single-target damage.',
   'damage', 8.8,  1.0, 0.22, 'NORMAL', 12, 0.12, 0.015, 0.015, 'first'),
  ('Cannon',   'Slow but devastating area-of-effect tower.',
   'damage', 8.0,  1.0, 0.32, 'SPLASH', 30, 0.12, 0.015, 0.015, 'first'),
  ('Ballista', 'Long-range siege tower. Excels against heavy units.',
   'damage', 12.1, 1.0, 0.40, 'SIEGE',  20, 0.12, 0.015, 0.015, 'first'),
  ('Mage',     'Magic damage tower. Bypasses physical armour.',
   'damage', 13.2, 1.0, 0.35, 'MAGIC',  24, 0.12, 0.015, 0.015, 'first')
ON CONFLICT DO NOTHING;
