-- 056_tt_bishop_public_name_cleanup.sql
-- Keep the existing tt_commander backend key for now, but normalize all
-- player-facing catalog text to Bishop so the tech tree, barracks, and
-- catalog APIs stop leaking the old placeholder label.

UPDATE unit_types
SET
  name = 'Bishop',
  description = 'Castle hero unlock. Summon the Bishop from a Barracks to reinforce the line from range once Castle is complete.',
  updated_at = NOW()
WHERE key = 'tt_commander';

UPDATE skin_catalog
SET
  name = 'Bishop',
  description = 'Castle hero unlock. Summon the Bishop from a Barracks to reinforce the line from range once Castle is complete.'
WHERE skin_key = 'tt_commander';
