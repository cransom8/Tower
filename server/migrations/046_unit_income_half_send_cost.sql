-- One-time Forge Wars roster rebalance:
-- set each sendable unit's income to 50% of its send cost.

UPDATE unit_types
SET
  income = ROUND((send_cost * 0.5)::numeric)::int,
  updated_at = NOW()
WHERE send_cost > 0;
