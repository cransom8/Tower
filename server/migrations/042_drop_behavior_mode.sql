-- Migration 042: Drop behavior_mode column from unit_types.
-- Unit capability is now inferred from build_cost (placeable) and send_cost (sendable).
ALTER TABLE unit_types DROP COLUMN IF EXISTS behavior_mode;
