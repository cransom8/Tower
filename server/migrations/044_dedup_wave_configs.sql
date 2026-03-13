-- Migration 044: Remove duplicate ml_wave_configs created by the non-idempotent
-- INSERT in migration 039 (which ran on every server start before the fix).
-- Keeps the lowest-id config named 'Standard' that actually has waves; deletes the rest.
-- If no Standard config has waves yet, keeps only the lowest-id one.

DELETE FROM ml_wave_configs
WHERE name = 'Standard'
  AND id <> (
    SELECT id FROM ml_wave_configs c
    WHERE c.name = 'Standard'
    ORDER BY
      (SELECT COUNT(*) FROM ml_waves w WHERE w.config_id = c.id) DESC,
      c.id ASC
    LIMIT 1
  );

-- Ensure the surviving Standard config is marked as default if no other default exists.
UPDATE ml_wave_configs
SET is_default = TRUE
WHERE name = 'Standard'
  AND NOT EXISTS (SELECT 1 FROM ml_wave_configs WHERE is_default = TRUE);
