-- 035_retire_classic_towers.sql
-- Retire the 5 classic tower unit types (archer, fighter, mage, ballista, cannon).
-- Set all 30 new creature unit types to behavior_mode='both' so they can be
-- sent as attackers OR placed as fixed defenders.

-- ── Retire classic towers ─────────────────────────────────────────────────────
UPDATE unit_types SET enabled = FALSE, updated_at = NOW()
WHERE key IN ('archer', 'fighter', 'mage', 'ballista', 'cannon');

-- ── Promote all 30 new creatures to 'both' ────────────────────────────────────
-- Tier 1 — cheap/fast (range stays short, cheap to build)
UPDATE unit_types SET behavior_mode='both', range=0.16, build_cost=8,  income=1, projectile_travel_ticks=5, updated_at=NOW() WHERE key='goblin';
UPDATE unit_types SET behavior_mode='both', range=0.14, build_cost=6,  income=1, projectile_travel_ticks=5, updated_at=NOW() WHERE key='kobold';
UPDATE unit_types SET behavior_mode='both', range=0.14, build_cost=6,  income=1, projectile_travel_ticks=5, updated_at=NOW() WHERE key='giant_rat';

-- Tier 2 — light infantry
UPDATE unit_types SET behavior_mode='both', range=0.18, build_cost=10, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='ghoul';
UPDATE unit_types SET behavior_mode='both', range=0.18, build_cost=10, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='hobgoblin';
UPDATE unit_types SET behavior_mode='both', range=0.20, build_cost=10, income=1, projectile_travel_ticks=5, updated_at=NOW() WHERE key='fantasy_wolf';
UPDATE unit_types SET behavior_mode='both', range=0.20, build_cost=14, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='orc';
UPDATE unit_types SET behavior_mode='both', range=0.20, build_cost=14, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='lizard_warrior';
UPDATE unit_types SET behavior_mode='both', range=0.22, build_cost=14, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='skeleton_knight';
UPDATE unit_types SET behavior_mode='both', range=0.20, build_cost=12, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='undead_warrior';

-- Tier 3 — mid-heavy
UPDATE unit_types SET behavior_mode='both', range=0.22, build_cost=12, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='darkness_spider';
UPDATE unit_types SET behavior_mode='both', range=0.28, build_cost=14, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='giant_viper';
UPDATE unit_types SET behavior_mode='both', range=0.22, build_cost=16, income=1, projectile_travel_ticks=7, updated_at=NOW() WHERE key='mummy';
UPDATE unit_types SET behavior_mode='both', range=0.16, build_cost=16, income=1, projectile_travel_ticks=5, updated_at=NOW() WHERE key='troll';
UPDATE unit_types SET behavior_mode='both', range=0.38, build_cost=12, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='harpy';
UPDATE unit_types SET behavior_mode='both', range=0.18, build_cost=18, income=2, projectile_travel_ticks=5, updated_at=NOW() WHERE key='werewolf';
UPDATE unit_types SET behavior_mode='both', range=0.28, build_cost=20, income=2, projectile_travel_ticks=6, updated_at=NOW() WHERE key='vampire';
UPDATE unit_types SET behavior_mode='both', range=0.18, build_cost=18, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='ogre';
UPDATE unit_types SET behavior_mode='both', range=0.24, build_cost=18, income=1, projectile_travel_ticks=6, updated_at=NOW() WHERE key='dragonide';

-- Tier 4 — elite
UPDATE unit_types SET behavior_mode='both', range=0.36, build_cost=24, income=1, projectile_travel_ticks=7, updated_at=NOW() WHERE key='evil_watcher';
UPDATE unit_types SET behavior_mode='both', range=0.16, build_cost=26, income=2, projectile_travel_ticks=6, updated_at=NOW() WHERE key='cyclops';
UPDATE unit_types SET behavior_mode='both', range=0.32, build_cost=22, income=2, projectile_travel_ticks=7, updated_at=NOW() WHERE key='wyvern';
UPDATE unit_types SET behavior_mode='both', range=0.34, build_cost=30, income=2, projectile_travel_ticks=7, updated_at=NOW() WHERE key='griffin';
UPDATE unit_types SET behavior_mode='both', range=0.22, build_cost=28, income=2, projectile_travel_ticks=7, updated_at=NOW() WHERE key='oak_tree_ent';

-- Boss tier
UPDATE unit_types SET behavior_mode='both', range=0.44, build_cost=22, income=1, projectile_travel_ticks=9, updated_at=NOW() WHERE key='manticora';
UPDATE unit_types SET behavior_mode='both', range=0.20, build_cost=30, income=2, projectile_travel_ticks=7, updated_at=NOW() WHERE key='ice_golem';
UPDATE unit_types SET behavior_mode='both', range=0.30, build_cost=38, income=3, projectile_travel_ticks=8, updated_at=NOW() WHERE key='chimera';
UPDATE unit_types SET behavior_mode='both', range=0.34, build_cost=35, income=2, projectile_travel_ticks=10, updated_at=NOW() WHERE key='mountain_dragon';
UPDATE unit_types SET behavior_mode='both', range=0.22, build_cost=32, income=2, projectile_travel_ticks=8, updated_at=NOW() WHERE key='hydra';
UPDATE unit_types SET behavior_mode='both', range=0.32, build_cost=50, income=3, projectile_travel_ticks=8, updated_at=NOW() WHERE key='demon_lord';

-- ── Bounty ────────────────────────────────────────────────────────────────────
UPDATE unit_types SET bounty=2  WHERE key IN ('goblin','kobold','giant_rat');
UPDATE unit_types SET bounty=3  WHERE key IN ('ghoul','hobgoblin','fantasy_wolf');
UPDATE unit_types SET bounty=4  WHERE key IN ('orc','skeleton_knight','undead_warrior','lizard_warrior','darkness_spider');
UPDATE unit_types SET bounty=5  WHERE key IN ('giant_viper','mummy','troll','harpy','dragonide');
UPDATE unit_types SET bounty=6  WHERE key IN ('ogre','vampire','werewolf','wyvern');
UPDATE unit_types SET bounty=7  WHERE key IN ('evil_watcher','cyclops','griffin');
UPDATE unit_types SET bounty=8  WHERE key IN ('oak_tree_ent');
UPDATE unit_types SET bounty=10 WHERE key IN ('manticora','ice_golem');
UPDATE unit_types SET bounty=12 WHERE key IN ('chimera','mountain_dragon');
UPDATE unit_types SET bounty=13 WHERE key IN ('hydra');
UPDATE unit_types SET bounty=15 WHERE key IN ('demon_lord');
