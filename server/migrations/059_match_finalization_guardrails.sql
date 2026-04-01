-- 059_match_finalization_guardrails.sql
-- Add match finalization state plus idempotency guardrails for match/rating writes.

ALTER TABLE matches
  ADD COLUMN IF NOT EXISTS finalization_state TEXT NOT NULL DEFAULT 'pending';

ALTER TABLE matches
  ADD COLUMN IF NOT EXISTS finalized_at TIMESTAMPTZ;

UPDATE matches
SET
  finalization_state = CASE
    WHEN status = 'completed' THEN 'completed'
    WHEN status = 'abandoned' THEN 'abandoned'
    ELSE 'pending'
  END,
  finalized_at = CASE
    WHEN status IN ('completed', 'abandoned') AND finalized_at IS NULL THEN COALESCE(ended_at, NOW())
    ELSE finalized_at
  END
WHERE finalization_state NOT IN ('pending', 'processing', 'completed', 'abandoned')
   OR (status IN ('completed', 'abandoned') AND finalized_at IS NULL);

WITH duplicate_match_players AS (
  SELECT ctid
  FROM (
    SELECT
      ctid,
      ROW_NUMBER() OVER (
        PARTITION BY match_id, player_id
        ORDER BY id
      ) AS row_num
    FROM match_players
  ) ranked
  WHERE row_num > 1
)
DELETE FROM match_players
WHERE ctid IN (SELECT ctid FROM duplicate_match_players);

CREATE UNIQUE INDEX IF NOT EXISTS match_players_match_player_uidx
  ON match_players (match_id, player_id);

WITH duplicate_rating_history AS (
  SELECT ctid
  FROM (
    SELECT
      ctid,
      ROW_NUMBER() OVER (
        PARTITION BY player_id, match_id, mode
        ORDER BY created_at, id
      ) AS row_num
    FROM rating_history
    WHERE match_id IS NOT NULL
  ) ranked
  WHERE row_num > 1
)
DELETE FROM rating_history
WHERE ctid IN (SELECT ctid FROM duplicate_rating_history);

CREATE UNIQUE INDEX IF NOT EXISTS rating_history_player_match_mode_uidx
  ON rating_history (player_id, match_id, mode)
  WHERE match_id IS NOT NULL;
