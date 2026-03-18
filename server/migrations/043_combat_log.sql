-- Migration 043: Add combat_log JSONB column to matches for per-match battle event history.
-- Stores round summaries and kill events (not every hit) when COMBAT_LOG=true.
ALTER TABLE matches ADD COLUMN IF NOT EXISTS combat_log JSONB DEFAULT '[]';
