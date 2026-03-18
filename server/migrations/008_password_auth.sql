-- 008_password_auth.sql
-- Make google_id optional (password-auth players have no google_id)
ALTER TABLE players ALTER COLUMN google_id DROP NOT NULL;

-- Add email (unique, nullable — Google-only players may not have one stored)
ALTER TABLE players ADD COLUMN IF NOT EXISTS email TEXT;
CREATE UNIQUE INDEX IF NOT EXISTS players_email_lower_idx
  ON players (lower(email)) WHERE email IS NOT NULL;

-- Add password hash (nullable — Google-only players have none)
ALTER TABLE players ADD COLUMN IF NOT EXISTS password_hash TEXT;

-- TOTP 2FA fields
ALTER TABLE players ADD COLUMN IF NOT EXISTS totp_secret  TEXT;
ALTER TABLE players ADD COLUMN IF NOT EXISTS totp_enabled BOOLEAN NOT NULL DEFAULT false;

-- Ensure every player has at least one auth method
DO $$ BEGIN
  IF NOT EXISTS (
    SELECT 1 FROM pg_constraint WHERE conname = 'players_auth_method_check'
  ) THEN
    ALTER TABLE players ADD CONSTRAINT players_auth_method_check
      CHECK (google_id IS NOT NULL OR password_hash IS NOT NULL);
  END IF;
END $$;
