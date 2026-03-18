-- 019_admin_password_reset.sql
CREATE TABLE IF NOT EXISTS admin_password_reset_tokens (
  id             UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  admin_user_id  UUID        NOT NULL REFERENCES admin_users(id) ON DELETE CASCADE,
  token_hash     TEXT        NOT NULL UNIQUE,
  expires_at     TIMESTAMPTZ NOT NULL,
  used_at        TIMESTAMPTZ,
  created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS aprt_admin_user_id_idx ON admin_password_reset_tokens (admin_user_id);
