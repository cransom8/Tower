using UnityEditor;
using UnityEngine;
using CastleDefender.Game;

public static class PrepareTTHumanCombatSfxBindings
{
    const string TTRTSPrefabRoot = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs";

    struct BindingConfig
    {
        public string prefabName;
        public string profileKey;
        public bool enableImpactCue;
        public bool enableDefendCue;
        public float spawnChance;
        public float attackChance;
        public float impactChance;
        public float defendChance;
        public float hurtChance;
        public float deathChance;
        public string notes;
    }

    static readonly BindingConfig[] BindingConfigs =
    {
        Create("TT_Peasant", "light_melee", false, false, 0.28f, 0.18f, 0f, 0f, 0.15f, 0.82f, "Militia light melee combat SFX."),
        Create("TT_Light_Infantry", "light_melee", false, false, 0.28f, 0.18f, 0f, 0f, 0.15f, 0.82f, "Swordsman light melee combat SFX."),
        Create("TT_Mounted_Knight", "heavy_melee", false, false, 0.24f, 0.14f, 0f, 0f, 0.12f, 0.80f, "Knight heavy melee combat SFX."),
        Create("TT_Spearman", "polearm", false, false, 0.26f, 0.16f, 0f, 0f, 0.14f, 0.82f, "Spearman polearm combat SFX."),
        Create("TT_Halberdier", "polearm", false, false, 0.26f, 0.16f, 0f, 0f, 0.14f, 0.82f, "Halberdier polearm combat SFX."),
        Create("TT_Light_Cavalry", "polearm", false, false, 0.26f, 0.16f, 0f, 0f, 0.14f, 0.82f, "Lancer polearm combat SFX."),
        Create("TT_Heavy_Infantry", "heavy_melee", false, true, 0.24f, 0.14f, 0f, 0.18f, 0.12f, 0.80f, "Shieldman heavy melee and defend combat SFX."),
        Create("TT_HeavySwordman", "heavy_melee", false, true, 0.24f, 0.14f, 0f, 0.18f, 0.12f, 0.80f, "Shield Guard heavy melee and defend combat SFX."),
        Create("TT_Heavy_Cavalry", "heavy_melee", false, true, 0.24f, 0.14f, 0f, 0.18f, 0.12f, 0.80f, "Guardian heavy melee and defend combat SFX."),
        Create("TT_Mounted_Priest", "support", false, false, 0.24f, 0.20f, 0f, 0f, 0.10f, 0.72f, "Cleric holy support combat SFX."),
        Create("TT_Priest", "support", false, false, 0.24f, 0.20f, 0f, 0f, 0.10f, 0.72f, "Priest holy support combat SFX."),
        Create("TT_HighPriest", "support", false, false, 0.24f, 0.20f, 0f, 0f, 0.10f, 0.72f, "High Priest holy support combat SFX."),
        Create("TT_Mage", "arcane", true, false, 0.22f, 0.18f, 0.16f, 0f, 0.12f, 0.75f, "Mage arcane cast and impact combat SFX."),
        Create("TT_Mounted_Mage", "arcane", true, false, 0.22f, 0.18f, 0.16f, 0f, 0.12f, 0.75f, "Wizard arcane cast and impact combat SFX."),
        Create("TT_Mounted_King", "arcane", true, false, 0.22f, 0.18f, 0.16f, 0f, 0.12f, 0.75f, "Thaumaturge arcane cast and impact combat SFX."),
        Create("TT_Archer", "bow", true, false, 0.24f, 0.22f, 0.18f, 0f, 0.12f, 0.78f, "Archer bow release and impact combat SFX."),
        Create("TT_Crossbowman", "crossbow", true, false, 0.22f, 0.18f, 0.18f, 0f, 0.12f, 0.78f, "Crossbowman bolt fire and impact combat SFX."),
        Create("TT_Mounted_Scout", "bow", true, false, 0.24f, 0.22f, 0.18f, 0f, 0.12f, 0.78f, "Ranger bow release and impact combat SFX."),
        Create("TT_King", "heavy_melee", false, false, 0.24f, 0.14f, 0f, 0f, 0.12f, 0.80f, "King heavy melee combat SFX."),
        Create("TT_Paladin", "heavy_melee", false, true, 0.24f, 0.14f, 0f, 0.18f, 0.12f, 0.80f, "Paladin heavy melee and defend combat SFX."),
        Create("TT_Commander", "support", false, false, 0.24f, 0.20f, 0f, 0f, 0.10f, 0.72f, "Bishop holy support combat SFX."),
    };

    [MenuItem("Castle Defender/Audio/Apply TT Human Combat SFX Bindings")]
    public static void ApplyBindings()
    {
        int updatedPrefabs = 0;
        int missingPrefabs = 0;

        for (int i = 0; i < BindingConfigs.Length; i++)
        {
            BindingConfig config = BindingConfigs[i];
            string prefabPath = $"{TTRTSPrefabRoot}/{config.prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogWarning($"[PrepareTTHumanCombatSfxBindings] Prefab not found: {prefabPath}");
                missingPrefabs++;
                continue;
            }

            using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                GameObject root = editScope.prefabContentsRoot;
                var binding = root.GetComponent<UnitCombatSfxBinding>();
                if (binding == null)
                    binding = root.AddComponent<UnitCombatSfxBinding>();

                binding.profileKey = config.profileKey;
                binding.enableImpactCue = config.enableImpactCue;
                binding.enableDefendCue = config.enableDefendCue;
                binding.spawnChance = config.spawnChance;
                binding.attackChance = config.attackChance;
                binding.impactChance = config.impactChance;
                binding.defendChance = config.defendChance;
                binding.hurtChance = config.hurtChance;
                binding.deathChance = config.deathChance;
                binding.notes = config.notes;

                EditorUtility.SetDirty(binding);
            }

            updatedPrefabs++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[PrepareTTHumanCombatSfxBindings] Applied TT human combat SFX bindings to {updatedPrefabs} prefab(s). " +
            $"missingPrefabs={missingPrefabs}");
    }

    static BindingConfig Create(
        string prefabName,
        string profileKey,
        bool enableImpactCue,
        bool enableDefendCue,
        float spawnChance,
        float attackChance,
        float impactChance,
        float defendChance,
        float hurtChance,
        float deathChance,
        string notes)
    {
        return new BindingConfig
        {
            prefabName = prefabName,
            profileKey = profileKey,
            enableImpactCue = enableImpactCue,
            enableDefendCue = enableDefendCue,
            spawnChance = spawnChance,
            attackChance = attackChance,
            impactChance = impactChance,
            defendChance = defendChance,
            hurtChance = hurtChance,
            deathChance = deathChance,
            notes = notes,
        };
    }
}
