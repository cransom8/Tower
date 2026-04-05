-- 022_barracks_levels.sql
-- Barracks level progression table.
-- Multiplier scales unit hp and/or attack_damage when barracks_scales_hp/dmg is TRUE on the unit type.

CREATE TABLE IF NOT EXISTS barracks_levels (
  level        INTEGER PRIMARY KEY CHECK (level >= 1 AND level <= 3),
  multiplier   NUMERIC(6,4) NOT NULL DEFAULT 1.0,
  upgrade_cost INTEGER      NOT NULL DEFAULT 0,
  notes        TEXT         NOT NULL DEFAULT '',
  created_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
  updated_at   TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- Seed: 3 levels
INSERT INTO barracks_levels (level, multiplier, upgrade_cost, notes) VALUES
  (1, 1.00,   0, 'Starting level - no bonus'),
  (2, 1.15,  50, '+15% HP/damage'),
  (3, 1.35, 100, 'Max level - +35% HP/damage')
ON CONFLICT (level) DO NOTHING;
