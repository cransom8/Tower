-- 018_survival_mode.sql
-- Survival Mode: wave sets, waves, and spawn groups

CREATE TABLE IF NOT EXISTS survival_wave_sets (
  id              SERIAL PRIMARY KEY,
  name            TEXT NOT NULL,
  description     TEXT NOT NULL DEFAULT '',
  enabled         BOOLEAN NOT NULL DEFAULT true,
  is_active       BOOLEAN NOT NULL DEFAULT false,
  auto_scale      BOOLEAN NOT NULL DEFAULT true,
  starting_gold   INTEGER NOT NULL DEFAULT 150,
  starting_lives  INTEGER NOT NULL DEFAULT 20,
  created_by      UUID REFERENCES admin_users(id) ON DELETE SET NULL,
  created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- Enforce only one active wave set at a time
CREATE UNIQUE INDEX IF NOT EXISTS survival_wave_sets_active_idx
  ON survival_wave_sets (is_active) WHERE is_active = true;
CREATE UNIQUE INDEX IF NOT EXISTS survival_wave_sets_name_key
  ON survival_wave_sets (name);

CREATE TABLE IF NOT EXISTS survival_waves (
  id           SERIAL PRIMARY KEY,
  wave_set_id  INTEGER NOT NULL REFERENCES survival_wave_sets(id) ON DELETE CASCADE,
  wave_number  INTEGER NOT NULL CHECK (wave_number >= 1),
  duration_ms  INTEGER,
  gold_bonus   INTEGER NOT NULL DEFAULT 0,
  is_boss      BOOLEAN NOT NULL DEFAULT false,
  is_rush      BOOLEAN NOT NULL DEFAULT false,
  is_elite     BOOLEAN NOT NULL DEFAULT false,
  notes        TEXT NOT NULL DEFAULT '',
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  UNIQUE (wave_set_id, wave_number)
);

CREATE INDEX IF NOT EXISTS survival_waves_set_num_idx ON survival_waves (wave_set_id, wave_number);

CREATE TABLE IF NOT EXISTS survival_spawn_groups (
  id                SERIAL PRIMARY KEY,
  wave_id           INTEGER NOT NULL REFERENCES survival_waves(id) ON DELETE CASCADE,
  unit_type         TEXT NOT NULL CHECK (unit_type IN ('runner','footman','ironclad','warlock','golem')),
  count             INTEGER NOT NULL CHECK (count >= 1),
  spawn_interval_ms INTEGER NOT NULL DEFAULT 1000 CHECK (spawn_interval_ms >= 100),
  start_delay_ms    INTEGER NOT NULL DEFAULT 0 CHECK (start_delay_ms >= 0),
  randomize_pct     SMALLINT NOT NULL DEFAULT 0 CHECK (randomize_pct BETWEEN 0 AND 100),
  created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS survival_spawn_groups_wave_idx ON survival_spawn_groups (wave_id);

-- Seed default wave set
INSERT INTO survival_wave_sets (name, description, is_active, starting_gold, starting_lives)
VALUES ('Default Wave Set', 'Starter wave configuration — edit in Admin → Survival', true, 150, 20)
ON CONFLICT (name) DO NOTHING;

-- Seed 5 waves for the default set (id=1)
DO $$
DECLARE
  ws_id INTEGER;
  w1 INTEGER; w2 INTEGER; w3 INTEGER; w4 INTEGER; w5 INTEGER;
BEGIN
  SELECT id INTO ws_id FROM survival_wave_sets WHERE name = 'Default Wave Set' LIMIT 1;
  IF ws_id IS NULL THEN RETURN; END IF;

  -- Wave 1
  INSERT INTO survival_waves (wave_set_id, wave_number, gold_bonus, notes)
  VALUES (ws_id, 1, 20, 'Warm-up wave')
  ON CONFLICT (wave_set_id, wave_number) DO NOTHING
  RETURNING id INTO w1;

  IF w1 IS NOT NULL THEN
    INSERT INTO survival_spawn_groups (wave_id, unit_type, count, spawn_interval_ms, start_delay_ms)
    VALUES (w1, 'runner', 8, 1500, 0);
  END IF;

  -- Wave 2
  INSERT INTO survival_waves (wave_set_id, wave_number, gold_bonus, notes)
  VALUES (ws_id, 2, 25, 'Footmen advance')
  ON CONFLICT (wave_set_id, wave_number) DO NOTHING
  RETURNING id INTO w2;

  IF w2 IS NOT NULL THEN
    INSERT INTO survival_spawn_groups (wave_id, unit_type, count, spawn_interval_ms, start_delay_ms)
    VALUES
      (w2, 'runner',  5, 1200, 0),
      (w2, 'footman', 4, 2000, 3000);
  END IF;

  -- Wave 3
  INSERT INTO survival_waves (wave_set_id, wave_number, gold_bonus, notes)
  VALUES (ws_id, 3, 30, 'Mixed assault')
  ON CONFLICT (wave_set_id, wave_number) DO NOTHING
  RETURNING id INTO w3;

  IF w3 IS NOT NULL THEN
    INSERT INTO survival_spawn_groups (wave_id, unit_type, count, spawn_interval_ms, start_delay_ms)
    VALUES
      (w3, 'footman',  6, 1500, 0),
      (w3, 'ironclad', 3, 3000, 5000);
  END IF;

  -- Wave 4: Rush
  INSERT INTO survival_waves (wave_set_id, wave_number, gold_bonus, is_rush, notes)
  VALUES (ws_id, 4, 40, true, 'Runner rush!')
  ON CONFLICT (wave_set_id, wave_number) DO NOTHING
  RETURNING id INTO w4;

  IF w4 IS NOT NULL THEN
    INSERT INTO survival_spawn_groups (wave_id, unit_type, count, spawn_interval_ms, start_delay_ms, randomize_pct)
    VALUES (w4, 'runner', 20, 400, 0, 20);
  END IF;

  -- Wave 5: Boss
  INSERT INTO survival_waves (wave_set_id, wave_number, gold_bonus, is_boss, notes)
  VALUES (ws_id, 5, 80, true, 'Boss wave — golem assault')
  ON CONFLICT (wave_set_id, wave_number) DO NOTHING
  RETURNING id INTO w5;

  IF w5 IS NOT NULL THEN
    INSERT INTO survival_spawn_groups (wave_id, unit_type, count, spawn_interval_ms, start_delay_ms)
    VALUES
      (w5, 'warlock', 4, 2000, 0),
      (w5, 'golem',   2, 5000, 4000);
  END IF;
END $$;

