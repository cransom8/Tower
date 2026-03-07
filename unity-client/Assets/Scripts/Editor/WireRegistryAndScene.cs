// WireRegistryAndScene.cs — One-click scene wiring for the ML battlefield.
// Menu: Castle Defender → Setup → Wire Registry and Scene
//
// Creates (or refreshes) Assets/UnitPrefabRegistry.asset with all 30 creature
// unit types, then assigns it + tile prefabs to TileGrid and LaneRenderer
// in the currently open scene.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CastleDefender.Game;

namespace CastleDefender.Editor
{
    public static class WireRegistryAndScene
    {
        const string REGISTRY_PATH = "Assets/UnitPrefabRegistry.asset";

        // ── Unit key → prefab asset path ──────────────────────────────────────
        static readonly (string key, string path, float scale)[] UNIT_MAP =
        {
            // Must Have Fantasy Villains
            ("goblin",        "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Goblin/Prefabs/Goblin_PBR.prefab",         0.6f),
            ("kobold",        "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Kobold/Prefabs/Kobold_PBR.prefab",         0.55f),
            ("hobgoblin",     "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Hobgoblin/Prefabs/Hobgoblin_PBR.prefab",   0.65f),
            ("orc",           "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Orc/Prefabs/Orc_PBR.prefab",               0.70f),
            ("ogre",          "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Ogre/Prefabs/FatOgre_PBR.prefab",          0.80f),
            ("troll",         "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Troll/Prefabs/Troll_PBR.prefab",           0.75f),
            ("cyclops",       "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Must Have Fantasy Villains Pack/Cyclops/Prefabs/Cyclops_PBR.prefab",       0.85f),
            // Living Dead
            ("ghoul",         "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Ghoul/Prefabs/Ghoul_PBR.prefab",                          0.65f),
            ("skeleton_knight","Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Skeleton Knight/Prefabs/SkeletonKnight_PBR.prefab",      0.70f),
            ("undead_warrior","Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Undead/Prefabs/Undead_PBR.prefab",                         0.68f),
            ("mummy",         "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Mummy/Prefabs/Mummy_PBR.prefab",                          0.72f),
            ("vampire",       "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Living Dead Pack/Vampire/Prefabs/Vampire_PBR.prefab",                      0.70f),
            // Fantasy Animals
            ("giant_rat",     "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Giant Rat/Prefabs/GiantRat_PBR.prefab",               0.50f),
            ("fantasy_wolf",  "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Fantasy Wolf/Prefabs/M_FantasyWolf_PBR.prefab",       0.60f),
            ("giant_viper",   "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Giant Viper/Prefabs/GiantViper_PBR.prefab",           0.60f),
            ("darkness_spider","Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Animals Pack/Darkness Spider/Prefabs/DarknessSpider_PBR.prefab",  0.65f),
            // Fantasy Lizards
            ("lizard_warrior","Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Lizard Warrior/Prefabs/LizardWarrior_PBR.prefab",     0.70f),
            ("dragonide",     "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Dragonide/Prefabs/Dragonide_PBR.prefab",              0.75f),
            ("wyvern",        "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Wyvern/Prefabs/Wyvern_PBR.prefab",                    0.80f),
            ("hydra",         "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Hydra/Prefabs/Hydra_PBR.prefab",                      0.90f),
            ("mountain_dragon","Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Fantasy Lizards Pack/Mountain Dragon/Prefabs/MountainDragon_PBR.prefab",  0.90f),
            // Mythological
            ("werewolf",      "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Werewolf/Prefabs/Werewolf_PBR.prefab",         0.75f),
            ("harpy",         "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Harpy/Prefabs/HarpyBreastsCovered_PBR.prefab", 0.65f),
            ("griffin",       "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Griffin/Prefabs/Griffin1SidedFeathers_PBR.prefab", 0.85f),
            ("manticora",     "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Manticora/Prefabs/Manticora_PBR.prefab",       0.85f),
            ("chimera",       "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Mythological Creatures Pack/Chimera/Prefabs/Chimera_PBR.prefab",           0.90f),
            // Demonic
            ("evil_watcher",  "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Demonic Creatures Pack/Evil Watcher/Prefabs/EvilWatcher.prefab",           0.60f),
            ("oak_tree_ent",  "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Demonic Creatures Pack/Oak Tree Ent/Prefabs/OakTreeEnt_PBR.prefab",        0.95f),
            ("ice_golem",     "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Demonic Creatures Pack/Golem/Prefabs/GolemIce_PBR.prefab",                 0.85f),
            ("demon_lord",    "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1/Demonic Creatures Pack/Demon Lord/Prefabs/DemonLord_PBR.prefab",           0.90f),
        };

        static readonly Color TINT_ALLY  = Color.white;
        static readonly Color TINT_ENEMY = new Color(1f, 0.35f, 0.35f, 1f);

        [MenuItem("Castle Defender/Setup/Wire Registry and Scene")]
        static void WireAll()
        {
            int created = 0, skipped = 0, warnings = 0;

            // ── 1. Create or load registry ─────────────────────────────────
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(REGISTRY_PATH);
            bool isNew = registry == null;
            if (isNew)
            {
                registry = ScriptableObject.CreateInstance<UnitPrefabRegistry>();
                AssetDatabase.CreateAsset(registry, REGISTRY_PATH);
                Debug.Log($"[WireRegistry] Created {REGISTRY_PATH}");
            }
            else
            {
                Debug.Log($"[WireRegistry] Refreshing existing {REGISTRY_PATH}");
            }

            // ── 2. Populate entries ────────────────────────────────────────
            var entries = new List<UnitPrefabRegistry.Entry>();
            GameObject firstValid = null;

            foreach (var (key, path, scale) in UNIT_MAP)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"[WireRegistry] Prefab not found: {path}");
                    warnings++;
                    skipped++;
                    continue;
                }

                entries.Add(new UnitPrefabRegistry.Entry
                {
                    key        = key,
                    prefab     = prefab,
                    scale      = scale,
                    tintMine   = TINT_ALLY,
                    tintEnemy  = TINT_ENEMY,
                });

                firstValid ??= prefab;
                created++;
            }

            registry.entries        = entries.ToArray();
            registry.fallbackPrefab = firstValid; // goblin as fallback
            registry.Rebuild();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WireRegistry] {created} entries written, {skipped} skipped.");

            // ── 3. Wire TileGrid ───────────────────────────────────────────
            var tileGrid = Object.FindFirstObjectByType<TileGrid>();
            if (tileGrid != null)
            {
                tileGrid.Registry = registry;

                if (tileGrid.FloorPrefab == null)
                    tileGrid.FloorPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/FloorTile.prefab");
                if (tileGrid.WallPrefab == null)
                    tileGrid.WallPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/WallTile.prefab");
                if (tileGrid.CastlePrefab == null)
                    tileGrid.CastlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/CastleTile.prefab");

                EditorUtility.SetDirty(tileGrid);
                Debug.Log("[WireRegistry] TileGrid wired.");
            }
            else
            {
                Debug.LogWarning("[WireRegistry] No TileGrid found in scene — open Game_ML scene first.");
                warnings++;
            }

            // ── 4. Wire LaneRenderer ───────────────────────────────────────
            var laneRenderer = Object.FindFirstObjectByType<LaneRenderer>();
            if (laneRenderer != null)
            {
                laneRenderer.Registry = registry;
                EditorUtility.SetDirty(laneRenderer);
                Debug.Log("[WireRegistry] LaneRenderer wired.");
            }
            else
            {
                Debug.LogWarning("[WireRegistry] No LaneRenderer found in scene — open Game_ML scene first.");
                warnings++;
            }

            // ── 5. Save scene ─────────────────────────────────────────────
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log($"[WireRegistry] Done. {warnings} warning(s). Save the scene (Ctrl+S).");
        }
    }
}
#endif
