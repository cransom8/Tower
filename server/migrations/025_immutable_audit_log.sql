-- Migration 025: Immutable audit log
-- Prevents UPDATE and DELETE on admin_audit_log at the DB level.
-- SOC 2 requirement: audit records must be tamper-evident.
-- Only schema migrations run by a superuser can bypass this trigger.

CREATE OR REPLACE FUNCTION prevent_audit_log_modification()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  RAISE EXCEPTION 'admin_audit_log records are immutable and cannot be modified or deleted';
END;
$$;

DROP TRIGGER IF EXISTS no_update_audit_log ON admin_audit_log;
CREATE TRIGGER no_update_audit_log
  BEFORE UPDATE ON admin_audit_log
  FOR EACH ROW EXECUTE FUNCTION prevent_audit_log_modification();

DROP TRIGGER IF EXISTS no_delete_audit_log ON admin_audit_log;
CREATE TRIGGER no_delete_audit_log
  BEFORE DELETE ON admin_audit_log
  FOR EACH ROW EXECUTE FUNCTION prevent_audit_log_modification();
