-- 016_admin_2fa_sso.sql
-- Add TOTP 2FA and Google SSO support to admin_users.
-- password_hash is made nullable so SSO-only accounts need no password.

ALTER TABLE admin_users ALTER COLUMN password_hash DROP NOT NULL;

ALTER TABLE admin_users ADD COLUMN IF NOT EXISTS totp_secret  TEXT;
ALTER TABLE admin_users ADD COLUMN IF NOT EXISTS totp_enabled BOOLEAN NOT NULL DEFAULT false;
ALTER TABLE admin_users ADD COLUMN IF NOT EXISTS google_id    TEXT    UNIQUE;
