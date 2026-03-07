-- 034_new_units.sql
-- Add 30 new unit types from HEROIC FANTASY CREATURES FULL PACK VOL 1.
-- All added as behavior_mode='moving' (attacker) by default.
-- Admin can promote any to 'both' or 'fixed' via the Units tab.
-- Keys match UnitPrefabRegistry entries in Unity.

INSERT INTO unit_types (
  key, name, description, behavior_mode,
  hp, attack_damage, attack_speed, range, path_speed,
  damage_type, armor_type, damage_reduction_pct,
  send_cost, build_cost, income, bounty, refund_pct,
  barracks_scales_hp, barracks_scales_dmg
) VALUES

-- ── Must Have Fantasy Villains ────────────────────────────────────────────────
  ('goblin',
   'Goblin', 'Cunning little menace. Fast and cheap to send in numbers.',
   'moving', 55, 4.5, 1.2, 0.0, 0.058, 'NORMAL', 'UNARMORED', 0,
   1, 0, 0, 2, 70, TRUE, FALSE),

  ('kobold', 'Kobold', 'Tiny and cowardly but incredibly fast.',
   'moving', 40, 3.0, 1.2, 0.0, 0.065, 'NORMAL', 'UNARMORED', 0,
   1, 0, 0, 2, 70, TRUE, FALSE),

  ('hobgoblin', 'Hobgoblin', 'Bigger and meaner than a regular goblin.',
   'moving', 85, 7.5, 1.0, 0.0, 0.042, 'NORMAL', 'LIGHT', 0,
   2, 0, 0, 3, 70, TRUE, FALSE),

  ('orc', 'Orc', 'Brutal warrior. Hits hard and takes punishment.',
   'moving', 100, 8.0, 1.0, 0.0, 0.038, 'PHYSICAL', 'MEDIUM', 0,
   3, 0, 0, 4, 70, TRUE, FALSE),

  ('ogre', 'Ogre', 'Massive brute. Slow but devastating.',
   'moving', 180, 12.0, 0.8, 0.0, 0.022, 'PHYSICAL', 'HEAVY', 15,
   5, 0, 0, 6, 70, TRUE, FALSE),

  ('troll', 'Troll', 'Regenerating bruiser. Hard to put down for good.',
   'moving', 160, 11.0, 0.9, 0.0, 0.025, 'PHYSICAL', 'HEAVY', 10,
   4, 0, 0, 5, 70, TRUE, FALSE),

  ('cyclops', 'Cyclops', 'One-eyed giant with crushing power.',
   'moving', 200, 16.0, 0.7, 0.0, 0.018, 'PHYSICAL', 'HEAVY', 20,
   6, 0, 0, 7, 70, TRUE, FALSE),

-- ── Living Dead Pack ──────────────────────────────────────────────────────────
  ('ghoul', 'Ghoul', 'Undead scavenger. Quick for something dead.',
   'moving', 80, 7.0, 1.0, 0.0, 0.042, 'NORMAL', 'LIGHT', 0,
   2, 0, 0, 3, 70, TRUE, FALSE),

  ('skeleton_knight', 'Skeleton Knight', 'Armoured undead soldier. Resists physical blows.',
   'moving', 120, 9.0, 1.0, 0.0, 0.030, 'NORMAL', 'MEDIUM', 10,
   3, 0, 0, 4, 70, TRUE, FALSE),

  ('undead_warrior', 'Undead Warrior', 'Shambling undead foot soldier. Tough but slow.',
   'moving', 110, 8.0, 0.9, 0.0, 0.028, 'NORMAL', 'MEDIUM', 0,
   3, 0, 0, 4, 70, TRUE, FALSE),

  ('mummy', 'Mummy', 'Ancient cursed undead. Extremely durable.',
   'moving', 150, 9.0, 0.8, 0.0, 0.020, 'NORMAL', 'HEAVY', 15,
   4, 0, 0, 5, 70, TRUE, FALSE),

  ('vampire', 'Vampire', 'Drains life with every strike. Magic damage bypasses armour.',
   'moving', 100, 14.0, 1.0, 0.0, 0.038, 'MAGIC', 'LIGHT', 0,
   5, 0, 0, 6, 70, TRUE, TRUE),

-- ── Fantasy Animals Pack ──────────────────────────────────────────────────────
  ('giant_rat', 'Giant Rat', 'Disease-ridden swarm unit. Extremely cheap.',
   'moving', 45, 4.0, 1.1, 0.0, 0.055, 'NORMAL', 'UNARMORED', 0,
   1, 0, 0, 2, 70, TRUE, FALSE),

  ('fantasy_wolf', 'Fantasy Wolf', 'Pack predator. Fast and piercing bite.',
   'moving', 65, 8.0, 1.1, 0.0, 0.055, 'PIERCE', 'UNARMORED', 0,
   2, 0, 0, 3, 70, TRUE, FALSE),

  ('giant_viper', 'Giant Viper', 'Venomous serpent. Piercing fangs hit light armour hard.',
   'moving', 70, 10.0, 1.0, 0.0, 0.050, 'PIERCE', 'UNARMORED', 0,
   4, 0, 0, 5, 70, TRUE, FALSE),

  ('darkness_spider', 'Darkness Spider', 'Lurks in shadows. Fast and venomous.',
   'moving', 75, 9.0, 1.1, 0.0, 0.048, 'PIERCE', 'LIGHT', 0,
   3, 0, 0, 4, 70, TRUE, FALSE),

-- ── Fantasy Lizards Pack ──────────────────────────────────────────────────────
  ('lizard_warrior', 'Lizard Warrior', 'Agile reptilian melee fighter.',
   'moving', 90, 8.5, 1.0, 0.0, 0.045, 'NORMAL', 'LIGHT', 0,
   3, 0, 0, 4, 70, TRUE, FALSE),

  ('dragonide', 'Dragonide', 'Dragon-kin warrior. Resilient with a fierce bite.',
   'moving', 120, 11.0, 0.9, 0.0, 0.030, 'NORMAL', 'MEDIUM', 0,
   5, 0, 0, 6, 70, TRUE, FALSE),

  ('wyvern', 'Wyvern', 'Winged lizard. Fast aerial attacker.',
   'moving', 130, 13.0, 0.9, 0.0, 0.042, 'PIERCE', 'MEDIUM', 0,
   6, 0, 0, 7, 70, TRUE, FALSE),

  ('hydra', 'Hydra', 'Multi-headed horror. Boss-tier health and damage.',
   'moving', 320, 22.0, 0.7, 0.0, 0.018, 'PHYSICAL', 'HEAVY', 20,
   13, 0, 0, 14, 70, TRUE, FALSE),

  ('mountain_dragon', 'Mountain Dragon', 'Ancient dragon. Breathes fire across an area.',
   'moving', 280, 20.0, 0.7, 0.0, 0.025, 'SPLASH', 'HEAVY', 25,
   12, 0, 0, 13, 70, TRUE, FALSE),

-- ── Mythological Creatures Pack ───────────────────────────────────────────────
  ('werewolf', 'Werewolf', 'Cursed beast. Fast and ferocious.',
   'moving', 140, 13.0, 1.0, 0.0, 0.040, 'PHYSICAL', 'MEDIUM', 0,
   5, 0, 0, 6, 70, TRUE, FALSE),

  ('harpy', 'Harpy', 'Winged predator. Swoops in fast.',
   'moving', 80, 10.0, 1.1, 0.0, 0.052, 'PIERCE', 'LIGHT', 0,
   4, 0, 0, 5, 70, TRUE, FALSE),

  ('griffin', 'Griffin', 'Noble flying beast. Powerful aerial attacker.',
   'moving', 180, 16.0, 0.9, 0.0, 0.035, 'PIERCE', 'MEDIUM', 0,
   8, 0, 0, 9, 70, TRUE, FALSE),

  ('manticora', 'Manticora', 'Venomous lion-scorpion. Ranged tail spike.',
   'moving', 240, 18.0, 0.8, 0.0, 0.028, 'PIERCE', 'HEAVY', 20,
   10, 0, 0, 11, 70, TRUE, FALSE),

  ('chimera', 'Chimera', 'Multi-headed magical beast. Boss-tier.',
   'moving', 260, 19.0, 0.8, 0.0, 0.030, 'MAGIC', 'HEAVY', 20,
   11, 0, 0, 12, 70, TRUE, TRUE),

-- ── Demonic Creatures Pack ────────────────────────────────────────────────────
  ('evil_watcher', 'Evil Watcher', 'Floating eyeball horror. Pure magic damage.',
   'moving', 70, 15.0, 1.0, 0.0, 0.025, 'MAGIC', 'LIGHT', 0,
   6, 0, 0, 7, 70, FALSE, TRUE),

  ('oak_tree_ent', 'Oak Tree Ent', 'Ancient tree spirit. Immense HP, crushingly slow.',
   'moving', 300, 15.0, 0.7, 0.0, 0.012, 'PHYSICAL', 'HEAVY', 25,
   8, 0, 0, 9, 70, TRUE, FALSE),

  ('ice_golem', 'Ice Golem', 'Frozen construct. Chills and slows nearby enemies.',
   'moving', 220, 14.0, 0.8, 0.0, 0.020, 'MAGIC', 'HEAVY', 25,
   9, 0, 0, 10, 70, TRUE, FALSE),

  ('demon_lord', 'Demon Lord', 'Supreme demonic overlord. Ultimate boss unit.',
   'moving', 350, 25.0, 0.7, 0.0, 0.020, 'MAGIC', 'HEAVY', 30,
   15, 0, 0, 16, 70, TRUE, TRUE)

ON CONFLICT (key) DO NOTHING;
