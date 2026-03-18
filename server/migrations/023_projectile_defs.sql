-- 023_projectile_defs.sql
-- Phase G: Projectile Definitions
-- Stores visual + behavior templates for projectiles fired by towers and units.

CREATE TABLE IF NOT EXISTS projectile_definitions (
  id              SERIAL PRIMARY KEY,
  key             TEXT   NOT NULL UNIQUE,
  name            TEXT   NOT NULL,
  sprite_url      TEXT,
  impact_vfx_url  TEXT,
  speed           REAL   NOT NULL DEFAULT 7,
  behavior        TEXT   NOT NULL DEFAULT 'single'
                    CHECK (behavior IN ('single','pierce','chain','bounce','splash')),
  behavior_params JSONB  NOT NULL DEFAULT '{}'
);

-- Seed: 5 base projectile types matching existing tower sims
INSERT INTO projectile_definitions (key, name, speed, behavior, behavior_params) VALUES
  ('arrow',         'Arrow',         7, 'single', '{}'),
  ('bolt',          'Bolt',          8, 'single', '{}'),
  ('magic_orb',     'Magic Orb',     7, 'single', '{}'),
  ('ballista_bolt', 'Ballista Bolt', 8, 'pierce', '{"maxTargets":3,"pierceRadius":1.0}'),
  ('cannonball',    'Cannonball',    6, 'splash', '{"radius":1.5}')
ON CONFLICT (key) DO NOTHING;
