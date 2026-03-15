-- 047_remote_content_metadata.sql
-- Remote content metadata owned by admin/Postgres.
-- Actual bundle/catalog binaries should live in object storage/CDN; this schema
-- stores authoritative keys, addresses, versions, and dependency metadata.

CREATE TABLE IF NOT EXISTS unit_content_metadata (
  id                 SERIAL PRIMARY KEY,
  unit_type_id       INTEGER NOT NULL UNIQUE REFERENCES unit_types(id) ON DELETE CASCADE,
  content_key        TEXT NOT NULL UNIQUE,
  addressables_label TEXT,
  prefab_address     TEXT,
  placeholder_key    TEXT,
  catalog_url        TEXT,
  content_url        TEXT,
  version_tag        TEXT NOT NULL DEFAULT '1',
  content_hash       TEXT,
  dependency_keys    JSONB NOT NULL DEFAULT '[]'::jsonb,
  metadata           JSONB NOT NULL DEFAULT '{}'::jsonb,
  is_critical        BOOLEAN NOT NULL DEFAULT TRUE,
  enabled            BOOLEAN NOT NULL DEFAULT TRUE,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT unit_content_dependency_keys_array CHECK (jsonb_typeof(dependency_keys) = 'array'),
  CONSTRAINT unit_content_metadata_object CHECK (jsonb_typeof(metadata) = 'object')
);

CREATE INDEX IF NOT EXISTS idx_unit_content_metadata_unit_type_id
  ON unit_content_metadata(unit_type_id);

CREATE TABLE IF NOT EXISTS skin_content_metadata (
  id                 SERIAL PRIMARY KEY,
  skin_catalog_id    INTEGER NOT NULL UNIQUE REFERENCES skin_catalog(id) ON DELETE CASCADE,
  content_key        TEXT NOT NULL UNIQUE,
  addressables_label TEXT,
  prefab_address     TEXT,
  placeholder_key    TEXT,
  catalog_url        TEXT,
  content_url        TEXT,
  version_tag        TEXT NOT NULL DEFAULT '1',
  content_hash       TEXT,
  dependency_keys    JSONB NOT NULL DEFAULT '[]'::jsonb,
  metadata           JSONB NOT NULL DEFAULT '{}'::jsonb,
  is_critical        BOOLEAN NOT NULL DEFAULT FALSE,
  enabled            BOOLEAN NOT NULL DEFAULT TRUE,
  created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT skin_content_dependency_keys_array CHECK (jsonb_typeof(dependency_keys) = 'array'),
  CONSTRAINT skin_content_metadata_object CHECK (jsonb_typeof(metadata) = 'object')
);

CREATE INDEX IF NOT EXISTS idx_skin_content_metadata_skin_catalog_id
  ON skin_content_metadata(skin_catalog_id);

-- Seed default metadata rows so existing units/skins immediately have stable keys.
INSERT INTO unit_content_metadata (unit_type_id, content_key)
SELECT u.id, u.key
FROM unit_types u
LEFT JOIN unit_content_metadata ucm ON ucm.unit_type_id = u.id
WHERE ucm.id IS NULL
ON CONFLICT (unit_type_id) DO NOTHING;

INSERT INTO skin_content_metadata (skin_catalog_id, content_key)
SELECT s.id, s.skin_key
FROM skin_catalog s
LEFT JOIN skin_content_metadata scm ON scm.skin_catalog_id = s.id
WHERE scm.id IS NULL
ON CONFLICT (skin_catalog_id) DO NOTHING;
