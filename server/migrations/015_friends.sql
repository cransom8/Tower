CREATE TABLE IF NOT EXISTS friends (
  id           UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
  requester_id UUID        NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  addressee_id UUID        NOT NULL REFERENCES players(id) ON DELETE CASCADE,
  status       TEXT        NOT NULL DEFAULT 'pending',
  created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  CONSTRAINT friends_no_self CHECK (requester_id <> addressee_id)
);
-- Deduplicate regardless of who initiated (LEAST/GREATEST on text ensures order-independence)
CREATE UNIQUE INDEX IF NOT EXISTS friends_unique_pair_idx
  ON friends (LEAST(requester_id::text, addressee_id::text),
              GREATEST(requester_id::text, addressee_id::text));
CREATE INDEX IF NOT EXISTS friends_requester_idx ON friends (requester_id);
CREATE INDEX IF NOT EXISTS friends_addressee_idx ON friends (addressee_id);
