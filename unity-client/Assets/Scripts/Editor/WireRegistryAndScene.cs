// WireRegistryAndScene.cs - One-click scene wiring for the ML battlefield.
// Menu: Castle Defender -> Setup -> Wire Registry and Scene
//
// Creates (or refreshes) Assets/UnitPrefabRegistry.asset with all 30 creature
// unit types, then assigns it to GameplayPresentationRoot in the currently open scene.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using CastleDefender.Game;

namespace CastleDefender.Editor
{
    public static class WireRegistryAndScene
    {
        static readonly (string key, string path, float scale)[] UNIT_MAP =
        {
            ("goblin", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Goblin/Prefabs/Goblin_PBR.prefab", 0.6f),
            ("kobold", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Kobold/Prefabs/Kobold_PBR.prefab", 0.55f),
            ("hobgoblin", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Hobgoblin/Prefabs/Hobgoblin_PBR.prefab", 0.65f),
            ("orc", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Orc/Prefabs/Orc_PBR.prefab", 0.70f),
            ("ogre", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Ogre/Prefabs/FatOgre_PBR.prefab", 0.80f),
            ("troll", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Troll/Prefabs/Troll_PBR.prefab", 0.75f),
            ("cyclops", EditorPaths.HFC_ROOT + "/Must Have Fantasy Villains Pack/Cyclops/Prefabs/Cyclops_PBR.prefab", 0.85f),
            ("ghoul", EditorPaths.HFC_ROOT + "/Living Dead Pack/Ghoul/Prefabs/Ghoul_PBR.prefab", 0.65f),
            ("skeleton_knight", EditorPaths.HFC_ROOT + "/Living Dead Pack/Skeleton Knight/Prefabs/SkeletonKnight_PBR.prefab", 0.70f),
            ("undead_warrior", EditorPaths.HFC_ROOT + "/Living Dead Pack/Undead/Prefabs/Undead_PBR.prefab", 0.68f),
            ("mummy", EditorPaths.HFC_ROOT + "/Living Dead Pack/Mummy/Prefabs/Mummy_PBR.prefab", 0.72f),
            ("vampire", EditorPaths.HFC_ROOT + "/Living Dead Pack/Vampire/Prefabs/Vampire_PBR.prefab", 0.70f),
            ("giant_rat", EditorPaths.HFC_ROOT + "/Fantasy Animals Pack/Giant Rat/Prefabs/GiantRat_PBR.prefab", 0.50f),
            ("fantasy_wolf", EditorPaths.HFC_ROOT + "/Fantasy Animals Pack/Fantasy Wolf/Prefabs/M_FantasyWolf_PBR.prefab", 0.60f),
            ("giant_viper", EditorPaths.HFC_ROOT + "/Fantasy Animals Pack/Giant Viper/Prefabs/GiantViper_PBR.prefab", 0.60f),
            ("darkness_spider", EditorPaths.HFC_ROOT + "/Fantasy Animals Pack/Darkness Spider/Prefabs/DarknessSpider_PBR.prefab", 0.65f),
            ("lizard_warrior", EditorPaths.HFC_ROOT + "/Fantasy Lizards Pack/Lizard Warrior/Prefabs/LizardWarrior_PBR.prefab", 0.70f),
            ("dragonide", EditorPaths.HFC_ROOT + "/Fantasy Lizards Pack/Dragonide/Prefabs/Dragonide_PBR.prefab", 0.75f),
            ("wyvern", EditorPaths.HFC_ROOT + "/Fantasy Lizards Pack/Wyvern/Prefabs/Wyvern_PBR.prefab", 0.80f),
            ("hydra", EditorPaths.HFC_ROOT + "/Fantasy Lizards Pack/Hydra/Prefabs/Hydra_PBR.prefab", 0.90f),
            ("mountain_dragon", EditorPaths.HFC_ROOT + "/Fantasy Lizards Pack/Mountain Dragon/Prefabs/MountainDragon_PBR.prefab", 0.90f),
            ("werewolf", EditorPaths.HFC_ROOT + "/Mythological Creatures Pack/Werewolf/Prefabs/Werewolf_PBR.prefab", 0.75f),
            ("harpy", EditorPaths.HFC_ROOT + "/Mythological Creatures Pack/Harpy/Prefabs/HarpyBreastsCovered_PBR.prefab", 0.65f),
            ("griffin", EditorPaths.HFC_ROOT + "/Mythological Creatures Pack/Griffin/Prefabs/Griffin1SidedFeathers_PBR.prefab", 0.85f),
            ("manticora", EditorPaths.HFC_ROOT + "/Mythological Creatures Pack/Manticora/Prefabs/Manticora_PBR.prefab", 0.85f),
            ("chimera", EditorPaths.HFC_ROOT + "/Mythological Creatures Pack/Chimera/Prefabs/Chimera_PBR.prefab", 0.90f),
            ("evil_watcher", EditorPaths.HFC_ROOT + "/Demonic Creatures Pack/Evil Watcher/Prefabs/EvilWatcher.prefab", 0.60f),
            ("oak_tree_ent", EditorPaths.HFC_ROOT + "/Demonic Creatures Pack/Oak Tree Ent/Prefabs/OakTreeEnt_PBR.prefab", 0.95f),
            ("ice_golem", EditorPaths.HFC_ROOT + "/Demonic Creatures Pack/Golem/Prefabs/GolemIce_PBR.prefab", 0.85f),
            ("demon_lord", EditorPaths.HFC_ROOT + "/Demonic Creatures Pack/Demon Lord/Prefabs/DemonLord_PBR.prefab", 0.90f),
        };

        static readonly Color TINT_ALLY = Color.white;
        static readonly Color TINT_ENEMY = new(1f, 0.35f, 0.35f, 1f);

        [MenuItem("Castle Defender/Setup/Wire Registry and Scene")]
        static void WireAll()
        {
            int created = 0;
            int skipped = 0;
            int warnings = 0;

            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(EditorPaths.REGISTRY);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<UnitPrefabRegistry>();
                AssetDatabase.CreateAsset(registry, EditorPaths.REGISTRY);
                Debug.Log($"[WireRegistry] Created {EditorPaths.REGISTRY}");
            }
            else
            {
                Debug.Log($"[WireRegistry] Refreshing existing {EditorPaths.REGISTRY}");
            }

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
                    key = key,
                    prefab = prefab,
                    scale = scale,
                    tintMine = TINT_ALLY,
                    tintEnemy = TINT_ENEMY,
                });

                firstValid ??= prefab;
                created++;
            }

            registry.entries = entries.ToArray();
            registry.fallbackPrefab = firstValid;
            registry.Rebuild();
            EditorUtility.SetDirty(registry);
            AssetDatabase.SaveAssets();
            Debug.Log($"[WireRegistry] {created} entries written, {skipped} skipped.");

            var presentationRoot = Object.FindFirstObjectByType<GameplayPresentationRoot>();
            if (presentationRoot != null)
            {
                presentationRoot.Registry = registry;
                EditorUtility.SetDirty(presentationRoot);
                Debug.Log("[WireRegistry] GameplayPresentationRoot wired.");
            }
            else
            {
                Debug.LogWarning("[WireRegistry] No GameplayPresentationRoot found in scene - open Game_ML scene first.");
                warnings++;
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log($"[WireRegistry] Done. {warnings} warning(s). Save the scene (Ctrl+S).");
        }
    }
}
#endif
