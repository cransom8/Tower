-- Add directional unit sprite/animation asset slots (front-facing and back-facing).
-- This enables separate uploads for units moving toward camera vs away from camera.

ALTER TABLE unit_types
  ADD COLUMN IF NOT EXISTS sprite_url_front TEXT,
  ADD COLUMN IF NOT EXISTS sprite_url_back  TEXT,
  ADD COLUMN IF NOT EXISTS animation_url_front TEXT,
  ADD COLUMN IF NOT EXISTS animation_url_back  TEXT;
