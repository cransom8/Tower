-- 007_bans.sql
-- Adds ban tracking columns to players table.
-- Existing status column ('active' | 'suspended') is already used for ban state;
-- these columns add audit detail (reason + timestamp).

ALTER TABLE players ADD COLUMN IF NOT EXISTS ban_reason  TEXT;
ALTER TABLE players ADD COLUMN IF NOT EXISTS banned_at   TIMESTAMPTZ;
