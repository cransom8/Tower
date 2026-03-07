-- 029_branding_settings.sql
-- Singleton branding configuration for public and admin UI copy.

CREATE TABLE IF NOT EXISTS branding_settings (
  singleton  BOOLEAN     PRIMARY KEY DEFAULT TRUE CHECK (singleton = TRUE),
  config_json JSONB      NOT NULL DEFAULT '{}'::jsonb,
  updated_by TEXT,
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO branding_settings (singleton, config_json, updated_by)
VALUES (TRUE, '{}'::jsonb, 'migration')
ON CONFLICT (singleton) DO NOTHING;
