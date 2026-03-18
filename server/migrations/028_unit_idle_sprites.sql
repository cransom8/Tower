-- Add idle sprite slots for unit types, including directional overrides.

ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS idle_sprite_url TEXT,
  ADD COLUMN IF NOT EXISTS idle_sprite_url_front TEXT,
  ADD COLUMN IF NOT EXISTS idle_sprite_url_back TEXT;
