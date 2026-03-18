-- 038_tt_rts_skins.sql
-- Seed 24 Toony Tiny RTS skins into skin_catalog.
-- Each skin maps a TT_RTS character to the closest existing unit_type by role/stats.
-- All are free (price=0, currency='free') — included in the base asset pack.

INSERT INTO skin_catalog (skin_key, unit_type, name, description, price, currency, preview_url, enabled) VALUES

-- ── Tier 1: Cheap / Fast ──────────────────────────────────────────────────────
  ('tt_peasant',        'goblin',          'Peasant',          'A humble villager pressed into service.',          0, 'free', '', true),
  ('tt_scout',          'kobold',          'Scout',            'Lightly armed skirmisher — fast and expendable.',  0, 'free', '', true),
  ('tt_settler',        'giant_rat',       'Settler',          'Civilian worker turned reluctant fighter.',        0, 'free', '', true),

-- ── Tier 2: Basic Infantry ────────────────────────────────────────────────────
  ('tt_light_infantry', 'hobgoblin',       'Light Infantry',   'Standard footsoldier in padded armour.',           0, 'free', '', true),
  ('tt_spearman',       'ghoul',           'Spearman',         'Disciplined spear-line unit. Holds formation.',    0, 'free', '', true),

-- ── Tier 2–3: Ranged ─────────────────────────────────────────────────────────
  ('tt_archer',         'harpy',           'Archer',           'Bowman trained for rapid volley fire.',            0, 'free', '', true),
  ('tt_crossbowman',    'darkness_spider', 'Crossbowman',      'Crossbow infantry. Slow reload, deadly bolt.',     0, 'free', '', true),

-- ── Tier 3: Medium Melee ─────────────────────────────────────────────────────
  ('tt_heavy_infantry', 'orc',             'Heavy Infantry',   'Armoured footsoldier with shield and sword.',      0, 'free', '', true),
  ('tt_halberdier',     'troll',           'Halberdier',       'Polearm specialist. Punishes heavy targets.',      0, 'free', '', true),
  ('tt_heavy_swordman', 'ogre',            'Heavy Swordsman',  'Two-handed swordsman. Massive swing, slow pace.',  0, 'free', '', true),

-- ── Tier 3–4: Cavalry ────────────────────────────────────────────────────────
  ('tt_light_cavalry',  'werewolf',        'Light Cavalry',    'Fast mounted skirmisher. Hits and retreats.',      0, 'free', '', true),
  ('tt_heavy_cavalry',  'wyvern',          'Heavy Cavalry',    'Armoured destrier. Charges through defences.',     0, 'free', '', true),

-- ── Tier 4: Support / Magic ───────────────────────────────────────────────────
  ('tt_priest',         'undead_warrior',  'Priest',           'Combat chaplain. Bolsters nearby troops.',         0, 'free', '', true),
  ('tt_high_priest',    'mummy',           'High Priest',      'Ancient ritual master. Durable and devout.',       0, 'free', '', true),
  ('tt_mage',           'evil_watcher',    'Mage',             'Arcane scholar. Pure magic damage output.',        0, 'free', '', true),

-- ── Tier 5: Elite Foot ───────────────────────────────────────────────────────
  ('tt_paladin',        'griffin',         'Paladin',          'Holy warrior in full plate. Stalwart defender.',   0, 'free', '', true),
  ('tt_commander',      'manticora',       'Commander',        'Veteran battlefield leader. Inspires the line.',   0, 'free', '', true),
  ('tt_king',           'cyclops',         'King',             'The crown itself rides to war. Crushing power.',   0, 'free', '', true),

-- ── Mounted Elite ─────────────────────────────────────────────────────────────
  ('tt_mounted_scout',   'fantasy_wolf',   'Mounted Scout',    'Swift cavalry outrider. Scouts ahead fast.',       0, 'free', '', true),
  ('tt_mounted_knight',  'skeleton_knight','Mounted Knight',   'Armoured lance cavalryman. Devastating charge.',   0, 'free', '', true),
  ('tt_mounted_mage',    'vampire',        'Mounted Mage',     'Sorcerer on horseback. Magic drain on the move.',  0, 'free', '', true),
  ('tt_mounted_paladin', 'chimera',        'Mounted Paladin',  'Holy champion astride a warhorse. Boss-tier.',     0, 'free', '', true),
  ('tt_mounted_priest',  'lizard_warrior', 'Mounted Priest',   'Agile chaplain — blesses and fights from the saddle.', 0, 'free', '', true),
  ('tt_mounted_king',    'demon_lord',     'Mounted King',     'The sovereign on his royal steed. Ultimate unit.', 0, 'free', '', true)

ON CONFLICT (skin_key) DO NOTHING;
