// SetupTTRTSSkins.cs
// Menu: Castle Defender > Setup > Register TT-RTS Skins
//
// For each of the 24 Toony Tiny RTS characters:
//   1. Duplicates the appropriate sample Animator Controller into
//      Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/controllers/
//   2. Assigns that controller to the character prefab's Animator.
//   3. Adds or updates a SkinEntry in UnitPrefabRegistry.
//
// Run once after importing the TT_RTS pack; safe to re-run.

using System.Collections.Generic;
using CastleDefender.Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class SetupTTRTSSkins
{
    const string SampleDir = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/sample_scene/animation_samples";
    const string PrefabDir = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs";
    const string OutputDir = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/controllers";
    const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
    const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";

    struct Entry
    {
        public string skinKey;
        public string unitType;
        public string prefabName;
        public string sampleController;
        public float scale;
    }

    static readonly Entry[] Entries =
    {
        new Entry { skinKey = "tt_peasant", unitType = "goblin", prefabName = "TT_Peasant", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_scout", unitType = "kobold", prefabName = "TT_Scout", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_settler", unitType = "giant_rat", prefabName = "TT_Settler", sampleController = "sample_settler", scale = 1.0f },
        new Entry { skinKey = "tt_light_infantry", unitType = "hobgoblin", prefabName = "TT_Light_Infantry", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_spearman", unitType = "ghoul", prefabName = "TT_Spearman", sampleController = "sample_spearman", scale = 1.0f },
        new Entry { skinKey = "tt_archer", unitType = "harpy", prefabName = "TT_Archer", sampleController = "sample_archer", scale = 1.0f },
        new Entry { skinKey = "tt_crossbowman", unitType = "darkness_spider", prefabName = "TT_Crossbowman", sampleController = "sample_crossbow", scale = 1.0f },
        new Entry { skinKey = "tt_heavy_infantry", unitType = "orc", prefabName = "TT_Heavy_Infantry", sampleController = "sample_shield", scale = 1.0f },
        new Entry { skinKey = "tt_halberdier", unitType = "troll", prefabName = "TT_Halberdier", sampleController = "sample_polearm", scale = 1.0f },
        new Entry { skinKey = "tt_heavy_swordman", unitType = "ogre", prefabName = "TT_HeavySwordman", sampleController = "sample_two_handed", scale = 1.1f },
        new Entry { skinKey = "tt_light_cavalry", unitType = "werewolf", prefabName = "TT_Light_Cavalry", sampleController = "sample_cavalry", scale = 1.1f },
        new Entry { skinKey = "tt_heavy_cavalry", unitType = "wyvern", prefabName = "TT_Heavy_Cavalry", sampleController = "sample_cavalry", scale = 1.2f },
        new Entry { skinKey = "tt_priest", unitType = "undead_warrior", prefabName = "TT_Priest", sampleController = "sample_caster", scale = 1.0f },
        new Entry { skinKey = "tt_high_priest", unitType = "mummy", prefabName = "TT_HighPriest", sampleController = "sample_caster", scale = 1.0f },
        new Entry { skinKey = "tt_mage", unitType = "evil_watcher", prefabName = "TT_Mage", sampleController = "sample_caster", scale = 1.0f },
        new Entry { skinKey = "tt_paladin", unitType = "griffin", prefabName = "TT_Paladin", sampleController = "sample_shield", scale = 1.1f },
        new Entry { skinKey = "tt_commander", unitType = "manticora", prefabName = "TT_Commander", sampleController = "sample_infantry", scale = 1.1f },
        new Entry { skinKey = "tt_king", unitType = "cyclops", prefabName = "TT_King", sampleController = "sample_two_handed", scale = 1.2f },
        new Entry { skinKey = "tt_mounted_scout", unitType = "fantasy_wolf", prefabName = "TT_Mounted_Scout", sampleController = "sample_cavalry", scale = 1.1f },
        new Entry { skinKey = "tt_mounted_knight", unitType = "skeleton_knight", prefabName = "TT_Mounted_Knight", sampleController = "sample_cavalry", scale = 1.2f },
        new Entry { skinKey = "tt_mounted_mage", unitType = "vampire", prefabName = "TT_Mounted_Mage", sampleController = "sample_cavalry_caster", scale = 1.1f },
        new Entry { skinKey = "tt_mounted_paladin", unitType = "chimera", prefabName = "TT_Mounted_Paladin", sampleController = "sample_cavalry_spear", scale = 1.2f },
        new Entry { skinKey = "tt_mounted_priest", unitType = "lizard_warrior", prefabName = "TT_Mounted_Priest", sampleController = "sample_cavalry_caster", scale = 1.1f },
        new Entry { skinKey = "tt_mounted_king", unitType = "demon_lord", prefabName = "TT_Mounted_King", sampleController = "sample_cavalry", scale = 1.3f },
    };

    [MenuItem("Castle Defender/Setup/Register TT-RTS Skins")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(OutputDir))
            AssetDatabase.CreateFolder("Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard", "controllers");

        var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
            ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
        if (registry == null)
        {
            Debug.LogError($"[SetupTTRTSSkins] Registry not found at {RegistryPath} or {LegacyRegistryPath}.");
            return;
        }

        var skinList = new List<UnitPrefabRegistry.SkinEntry>(registry.skinEntries ?? new UnitPrefabRegistry.SkinEntry[0]);

        int added = 0;
        int updated = 0;
        int warnings = 0;

        foreach (var entry in Entries)
        {
            string srcPath = $"{SampleDir}/{entry.sampleController}.controller";
            string destPath = $"{OutputDir}/{entry.skinKey}.controller";

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(srcPath) == null)
                {
                    Debug.LogWarning($"[SetupTTRTSSkins] Source controller not found: {srcPath}");
                    warnings++;
                    continue;
                }

                AssetDatabase.CopyAsset(srcPath, destPath);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath);
            string prefabPath = $"{PrefabDir}/{entry.prefabName}.prefab";
            var prefabGO = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabGO == null)
            {
                Debug.LogWarning($"[SetupTTRTSSkins] Prefab not found: {prefabPath}");
                warnings++;
                continue;
            }

            using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var animator = editScope.prefabContentsRoot.GetComponentInChildren<Animator>();
                if (animator != null)
                    animator.runtimeAnimatorController = controller;
                else
                    Debug.LogWarning($"[SetupTTRTSSkins] No Animator found in prefab: {prefabPath}");
            }

            int existingIndex = skinList.FindIndex(s => s.skinKey == entry.skinKey);
            var skinEntry = new UnitPrefabRegistry.SkinEntry
            {
                skinKey = entry.skinKey,
                unitType = entry.unitType,
                prefab = prefabGO,
                scale = entry.scale,
            };

            if (existingIndex >= 0)
            {
                skinList[existingIndex] = skinEntry;
                updated++;
            }
            else
            {
                skinList.Add(skinEntry);
                added++;
            }
        }

        registry.skinEntries = skinList.ToArray();
        registry.Rebuild();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SetupTTRTSSkins] Complete - added={added} updated={updated} warnings={warnings}. Inspect {AssetDatabase.GetAssetPath(registry)} to verify.");
    }
}
