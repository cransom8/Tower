-- Migration 039: ML Wave Config
-- Replaces the survival wave system for multilane wave defense (Forge Wars Wave Rework).
-- Each config is a named sequence of waves. One config is flagged is_default.
-- After the last configured wave, the sim repeats the last wave with +10% HP/DMG per extra round.

CREATE TABLE IF NOT EXISTS ml_wave_configs (
  id          SERIAL PRIMARY KEY,
  name        VARCHAR(100) NOT NULL,
  description TEXT,
  is_default  BOOLEAN DEFAULT FALSE,
  created_at  TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ml_waves (
  id          SERIAL PRIMARY KEY,
  config_id   INTEGER NOT NULL REFERENCES ml_wave_configs(id) ON DELETE CASCADE,
  wave_number INTEGER NOT NULL,
  unit_type   VARCHAR(64) NOT NULL,
  spawn_qty   INTEGER NOT NULL DEFAULT 8,
  hp_mult     NUMERIC(5,2) NOT NULL DEFAULT 1.00,
  dmg_mult    NUMERIC(5,2) NOT NULL DEFAULT 1.00,
  speed_mult  NUMERIC(5,2) NOT NULL DEFAULT 1.00,
  UNIQUE(config_id, wave_number)
);

CREATE INDEX IF NOT EXISTS idx_ml_waves_config_id ON ml_waves(config_id);

-- Seed one default config with 10 waves.
-- Unit types reference keys expected from migration 034 (new_units).
-- Adjust unit_type values if your DB uses different keys.
INSERT INTO ml_wave_configs (name, description, is_default)
SELECT 'Standard', 'Default 10-wave progression. Repeats wave 10 with +10% stats per extra round.', TRUE
WHERE NOT EXISTS (SELECT 1 FROM ml_wave_configs WHERE name = 'Standard');

-- Seed waves for config id=1
INSERT INTO ml_waves (config_id, wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
SELECT 1, w.wave_number, w.unit_type, w.spawn_qty, w.hp_mult, w.dmg_mult, w.speed_mult
FROM (VALUES
  (1,  'goblin',   8,  1.00, 1.00, 1.00),
  (2,  'goblin',  10,  1.20, 1.10, 1.00),
  (3,  'orc',      8,  1.00, 1.00, 1.00),
  (4,  'orc',     10,  1.25, 1.15, 1.00),
  (5,  'troll',    6,  1.00, 1.00, 1.00),
  (6,  'troll',    8,  1.30, 1.20, 1.05),
  (7,  'vampire',  6,  1.00, 1.00, 1.00),
  (8,  'vampire',  8,  1.35, 1.25, 1.05),
  (9,  'wyvern',   5,  1.00, 1.00, 1.00),
  (10, 'wyvern',   6,  1.50, 1.40, 1.10)
) AS w(wave_number, unit_type, spawn_qty, hp_mult, dmg_mult, speed_mult)
ON CONFLICT (config_id, wave_number) DO NOTHING;
