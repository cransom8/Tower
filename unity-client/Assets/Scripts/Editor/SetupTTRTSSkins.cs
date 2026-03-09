// SetupTTRTSSkins.cs
// Menu: Castle Defender > Setup > Register TT-RTS Skins
//
// For each of the 24 Toony Tiny RTS characters:
//   1. Duplicates the appropriate sample Animator Controller into
//      Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/controllers/
//   2. Assigns that controller to the character prefab's Animator.
//   3. Adds/updates a SkinEntry in Assets/UnitPrefabRegistry.asset.
//
// Run once after importing the TT_RTS pack; safe to re-run (idempotent).

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using CastleDefender.Game;

public static class SetupTTRTSSkins
{
    const string SAMPLE_DIR   = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/sample_scene/animation_samples";
    const string PREFAB_DIR   = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs";
    const string OUTPUT_DIR   = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/controllers";
    const string REGISTRY_PATH = "Assets/UnitPrefabRegistry.asset";

    struct Entry
    {
        public string skinKey;
        public string unitType;
        public string prefabName;       // filename without .prefab
        public string sampleController; // filename without .controller
        public float  scale;
    }

    static readonly Entry[] Entries =
    {
        // Tier 1 — cheap / fast
        new Entry { skinKey="tt_peasant",        unitType="goblin",          prefabName="TT_Peasant",         sampleController="sample_infantry",       scale=1.0f },
        new Entry { skinKey="tt_scout",          unitType="kobold",          prefabName="TT_Scout",           sampleController="sample_infantry",       scale=1.0f },
        new Entry { skinKey="tt_settler",        unitType="giant_rat",       prefabName="TT_Settler",         sampleController="sample_settler",        scale=1.0f },

        // Tier 2 — basic infantry
        new Entry { skinKey="tt_light_infantry", unitType="hobgoblin",       prefabName="TT_Light_Infantry",  sampleController="sample_infantry",       scale=1.0f },
        new Entry { skinKey="tt_spearman",       unitType="ghoul",           prefabName="TT_Spearman",        sampleController="sample_spearman",       scale=1.0f },

        // Tier 2–3 — ranged
        new Entry { skinKey="tt_archer",         unitType="harpy",           prefabName="TT_Archer",          sampleController="sample_archer",         scale=1.0f },
        new Entry { skinKey="tt_crossbowman",    unitType="darkness_spider", prefabName="TT_Crossbowman",     sampleController="sample_crossbow",       scale=1.0f },

        // Tier 3 — medium melee
        new Entry { skinKey="tt_heavy_infantry", unitType="orc",             prefabName="TT_Heavy_Infantry",  sampleController="sample_shield",         scale=1.0f },
        new Entry { skinKey="tt_halberdier",     unitType="troll",           prefabName="TT_Halberdier",      sampleController="sample_polearm",        scale=1.0f },
        new Entry { skinKey="tt_heavy_swordman", unitType="ogre",            prefabName="TT_HeavySwordman",   sampleController="sample_two_handed",     scale=1.1f },

        // Tier 3–4 — cavalry
        new Entry { skinKey="tt_light_cavalry",  unitType="werewolf",        prefabName="TT_Light_Cavalry",   sampleController="sample_cavalry",        scale=1.1f },
        new Entry { skinKey="tt_heavy_cavalry",  unitType="wyvern",          prefabName="TT_Heavy_Cavalry",   sampleController="sample_cavalry",        scale=1.2f },

        // Tier 4 — support / magic
        new Entry { skinKey="tt_priest",         unitType="undead_warrior",  prefabName="TT_Priest",          sampleController="sample_caster",         scale=1.0f },
        new Entry { skinKey="tt_high_priest",    unitType="mummy",           prefabName="TT_HighPriest",      sampleController="sample_caster",         scale=1.0f },
        new Entry { skinKey="tt_mage",           unitType="evil_watcher",    prefabName="TT_Mage",            sampleController="sample_caster",         scale=1.0f },

        // Tier 5 — elite foot
        new Entry { skinKey="tt_paladin",        unitType="griffin",         prefabName="TT_Paladin",         sampleController="sample_shield",         scale=1.1f },
        new Entry { skinKey="tt_commander",      unitType="manticora",       prefabName="TT_Commander",       sampleController="sample_infantry",       scale=1.1f },
        new Entry { skinKey="tt_king",           unitType="cyclops",         prefabName="TT_King",            sampleController="sample_two_handed",     scale=1.2f },

        // Mounted elite
        new Entry { skinKey="tt_mounted_scout",   unitType="fantasy_wolf",   prefabName="TT_Mounted_Scout",   sampleController="sample_cavalry",        scale=1.1f },
        new Entry { skinKey="tt_mounted_knight",  unitType="skeleton_knight",prefabName="TT_Mounted_Knight",  sampleController="sample_cavalry",        scale=1.2f },
        new Entry { skinKey="tt_mounted_mage",    unitType="vampire",        prefabName="TT_Mounted_Mage",    sampleController="sample_cavalry_caster", scale=1.1f },
        new Entry { skinKey="tt_mounted_paladin", unitType="chimera",        prefabName="TT_Mounted_Paladin", sampleController="sample_cavalry_spear",  scale=1.2f },
        new Entry { skinKey="tt_mounted_priest",  unitType="lizard_warrior", prefabName="TT_Mounted_Priest",  sampleController="sample_cavalry_caster", scale=1.1f },
        new Entry { skinKey="tt_mounted_king",    unitType="demon_lord",     prefabName="TT_Mounted_King",    sampleController="sample_cavalry",        scale=1.3f },
    };

    [MenuItem("Castle Defender/Setup/Register TT-RTS Skins")]
    public static void Run()
    {
        // ── 1. Ensure output folder ────────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder(OUTPUT_DIR))
            AssetDatabase.CreateFolder(
                "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard",
                "controllers");

        // ── 2. Load registry ───────────────────────────────────────────────────
        var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(REGISTRY_PATH);
        if (registry == null)
        {
            Debug.LogError($"[SetupTTRTSSkins] Registry not found at {REGISTRY_PATH}. " +
                           "Create it via Assets > Create > CastleDefender > Unit Prefab Registry first.");
            return;
        }

        var skinList = new List<UnitPrefabRegistry.SkinEntry>(
            registry.skinEntries ?? new UnitPrefabRegistry.SkinEntry[0]);

        int added = 0, updated = 0, warn = 0;

        foreach (var e in Entries)
        {
            // ── 3. Duplicate sample controller (skip if already done) ──────────
            string srcPath  = $"{SAMPLE_DIR}/{e.sampleController}.controller";
            string destPath = $"{OUTPUT_DIR}/{e.skinKey}.controller";

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath) == null)
            {
                if (AssetDatabase.LoadAssetAtPath<AnimatorController>(srcPath) == null)
                {
                    Debug.LogWarning($"[SetupTTRTSSkins] Source controller not found: {srcPath}");
                    warn++;
                    continue;
                }
                AssetDatabase.CopyAsset(srcPath, destPath);
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(destPath);

            // ── 4. Assign controller to prefab Animator ────────────────────────
            string prefabPath = $"{PREFAB_DIR}/{e.prefabName}.prefab";
            var prefabGO = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabGO == null)
            {
                Debug.LogWarning($"[SetupTTRTSSkins] Prefab not found: {prefabPath}");
                warn++;
                continue;
            }

            // Edit prefab contents without opening the prefab stage
            using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var anim = editScope.prefabContentsRoot.GetComponentInChildren<Animator>();
                if (anim != null)
                    anim.runtimeAnimatorController = controller;
                else
                    Debug.LogWarning($"[SetupTTRTSSkins] No Animator found in prefab: {prefabPath}");
            }

            // ── 5. Add / update SkinEntry ──────────────────────────────────────
            int idx = skinList.FindIndex(s => s.skinKey == e.skinKey);
            var entry = new UnitPrefabRegistry.SkinEntry
            {
                skinKey  = e.skinKey,
                unitType = e.unitType,
                prefab   = prefabGO,
                scale    = e.scale,
            };

            if (idx >= 0) { skinList[idx] = entry; updated++; }
            else          { skinList.Add(entry);    added++;   }
        }

        // ── 6. Save registry ───────────────────────────────────────────────────
        registry.skinEntries = skinList.ToArray();
        registry.Rebuild();
        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SetupTTRTSSkins] Complete — added={added} updated={updated} warnings={warn}. " +
                  $"Inspect {REGISTRY_PATH} to verify.");
    }
}
