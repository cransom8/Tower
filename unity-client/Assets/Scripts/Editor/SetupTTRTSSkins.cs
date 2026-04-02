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
        new Entry { skinKey = "tt_peasant", unitType = "tt_peasant", prefabName = "TT_Peasant", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_scout", unitType = "tt_scout", prefabName = "TT_Scout", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_settler", unitType = "tt_settler", prefabName = "TT_Settler", sampleController = "sample_settler", scale = 1.0f },
        new Entry { skinKey = "tt_light_infantry", unitType = "tt_light_infantry", prefabName = "TT_Light_Infantry", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_spearman", unitType = "tt_spearman", prefabName = "TT_Spearman", sampleController = "sample_spearman", scale = 1.0f },
        new Entry { skinKey = "tt_archer", unitType = "tt_archer", prefabName = "TT_Archer", sampleController = "sample_archer", scale = 1.0f },
        new Entry { skinKey = "tt_crossbowman", unitType = "tt_crossbowman", prefabName = "TT_Crossbowman", sampleController = "sample_crossbow", scale = 1.0f },
        new Entry { skinKey = "tt_heavy_infantry", unitType = "tt_heavy_infantry", prefabName = "TT_Heavy_Infantry", sampleController = "sample_shield", scale = 1.0f },
        new Entry { skinKey = "tt_halberdier", unitType = "tt_halberdier", prefabName = "TT_Halberdier", sampleController = "sample_polearm", scale = 1.0f },
        new Entry { skinKey = "tt_heavy_swordman", unitType = "tt_heavy_swordman", prefabName = "TT_HeavySwordman", sampleController = "sample_two_handed", scale = 1.0f },
        new Entry { skinKey = "tt_light_cavalry", unitType = "tt_light_cavalry", prefabName = "TT_Light_Cavalry", sampleController = "sample_cavalry", scale = 1.0f },
        new Entry { skinKey = "tt_heavy_cavalry", unitType = "tt_heavy_cavalry", prefabName = "TT_Heavy_Cavalry", sampleController = "sample_cavalry", scale = 1.0f },
        new Entry { skinKey = "tt_priest", unitType = "tt_priest", prefabName = "TT_Priest", sampleController = "sample_caster", scale = 1.0f },
        new Entry { skinKey = "tt_high_priest", unitType = "tt_high_priest", prefabName = "TT_HighPriest", sampleController = "sample_caster", scale = 1.0f },
        new Entry { skinKey = "tt_mage", unitType = "tt_mage", prefabName = "TT_Mage", sampleController = "sample_caster", scale = 1.0f },
        new Entry { skinKey = "tt_paladin", unitType = "tt_paladin", prefabName = "TT_Paladin", sampleController = "sample_shield", scale = 1.0f },
        new Entry { skinKey = "tt_commander", unitType = "tt_commander", prefabName = "TT_Commander", sampleController = "sample_infantry", scale = 1.0f },
        new Entry { skinKey = "tt_king", unitType = "tt_king", prefabName = "TT_King", sampleController = "sample_two_handed", scale = 1.0f },
        new Entry { skinKey = "tt_mounted_scout", unitType = "tt_mounted_scout", prefabName = "TT_Mounted_Scout", sampleController = "sample_cavalry", scale = 1.0f },
        new Entry { skinKey = "tt_mounted_knight", unitType = "tt_mounted_knight", prefabName = "TT_Mounted_Knight", sampleController = "sample_cavalry", scale = 1.0f },
        new Entry { skinKey = "tt_mounted_mage", unitType = "tt_mounted_mage", prefabName = "TT_Mounted_Mage", sampleController = "sample_cavalry_caster", scale = 1.0f },
        new Entry { skinKey = "tt_mounted_paladin", unitType = "tt_mounted_paladin", prefabName = "TT_Mounted_Paladin", sampleController = "sample_cavalry_spear", scale = 1.0f },
        new Entry { skinKey = "tt_mounted_priest", unitType = "tt_mounted_priest", prefabName = "TT_Mounted_Priest", sampleController = "sample_cavalry_caster", scale = 1.0f },
        new Entry { skinKey = "tt_mounted_king", unitType = "tt_mounted_king", prefabName = "TT_Mounted_King", sampleController = "sample_cavalry", scale = 1.0f },
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
        PrepareWarriorPackBundle2Profiles.ApplyTTRTSMapping();

        Debug.Log($"[SetupTTRTSSkins] Complete - added={added} updated={updated} warnings={warnings}. Inspect {AssetDatabase.GetAssetPath(registry)} to verify.");
    }
}
