-- 012_admin_audit_log.sql
-- Every admin action is recorded here for accountability.

CREATE TABLE IF NOT EXISTS admin_audit_log (
  id          UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  action      TEXT        NOT NULL,
  target_id   TEXT,
  target_type TEXT,
  payload     JSONB,
  admin_ip    TEXT,
  created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS aal_action_idx    ON admin_audit_log (action);
CREATE INDEX IF NOT EXISTS aal_created_idx   ON admin_audit_log (created_at DESC);
CREATE INDEX IF NOT EXISTS aal_target_idx    ON admin_audit_log (target_type, target_id);
