using System.Collections.Generic;
using System.IO;
using CastleDefender.Game;
using UnityEditor;
using UnityEngine;

public static class PrepareWarriorPackBundle2Profiles
{
    const string ExplosiveRoot = "Assets/ExplosiveLLC";
    const string OutputRoot = "Assets/Registry/AnimationProfiles";
    const string TTRTSPrefabRoot = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs";

    enum ProfileFlavor
    {
        Default = 0,
        TwoHanded = 1,
        Knight = 2,
        Archer = 3,
        Mage = 4,
    }

    struct ProfileConfig
    {
        public string assetName;
        public string profileId;
        public UnitAnimationAttackFamily attackFamily;
        public string controllerPath;
        public string controllerHint;
        public string notes;
        public ProfileFlavor flavor;
    }

    struct PrefabBindingConfig
    {
        public string prefabName;
        public string profileAssetName;
        public string notes;
    }

    static readonly ProfileConfig[] ProfileConfigs =
    {
        new ProfileConfig
        {
            assetName = "Explosive_TTRTS_Default",
            profileId = "tt_rts_default",
            attackFamily = UnitAnimationAttackFamily.Melee,
            controllerPath = null,
            controllerHint = null,
            flavor = ProfileFlavor.Default,
            notes = "Fallback profile for TT_RTS prefabs that are still on their original controllers.",
        },
        new ProfileConfig
        {
            assetName = "Explosive_Knight",
            profileId = "explosive_knight",
            attackFamily = UnitAnimationAttackFamily.Melee,
            controllerPath = "Assets/ExplosiveLLC/Knight Warrior Mecanim Animation Pack/Animation Controller/Knight Warrior Animation Controller.controller",
            controllerHint = "knight warrior animation controller",
            flavor = ProfileFlavor.Knight,
            notes = "Sword-and-board profile for shield units and paladin variants.",
        },
        new ProfileConfig
        {
            assetName = "Explosive_Archer",
            profileId = "explosive_archer",
            attackFamily = UnitAnimationAttackFamily.Ranged,
            controllerPath = "Assets/ExplosiveLLC/Archer Warrior Mecanim Animation Pack/Animation Controller/Archer Warrior Animation Controller.controller",
            controllerHint = "archer warrior animation controller",
            flavor = ProfileFlavor.Archer,
            notes = "Ranged profile for archers, crossbowmen, and scout-style variants.",
        },
        new ProfileConfig
        {
            assetName = "Explosive_Mage",
            profileId = "explosive_mage",
            attackFamily = UnitAnimationAttackFamily.Magic,
            controllerPath = "Assets/ExplosiveLLC/Mage Warrior Mecanim Animation Pack/Animation Controller/Mage Warrior Animation Controller.controller",
            controllerHint = "mage warrior animation controller",
            flavor = ProfileFlavor.Mage,
            notes = "Caster profile for priests, mages, bishop, and arcane hero variants.",
        },
        new ProfileConfig
        {
            assetName = "Explosive_TwoHanded",
            profileId = "explosive_two_handed",
            attackFamily = UnitAnimationAttackFamily.Melee,
            controllerPath = "Assets/ExplosiveLLC/2 Handed Warrior Mecanim Animation Pack/Animation Controller/2Handed Warrior Animation Controller.controller",
            controllerHint = "2handed warrior animation controller",
            flavor = ProfileFlavor.TwoHanded,
            notes = "Heavy infantry profile for militia, footmen, knights, and the king.",
        },
    };

    static readonly PrefabBindingConfig[] PrefabBindingConfigs =
    {
        new PrefabBindingConfig { prefabName = "TT_Peasant", profileAssetName = "Explosive_TwoHanded", notes = "Militia presentation." },
        new PrefabBindingConfig { prefabName = "TT_Settler", profileAssetName = "Explosive_TTRTS_Default", notes = "Peasant / settler / trader civilian presentation." },
        new PrefabBindingConfig { prefabName = "TT_Light_Infantry", profileAssetName = "Explosive_TwoHanded", notes = "Footman / swordsman presentation." },
        new PrefabBindingConfig { prefabName = "TT_Mounted_Knight", profileAssetName = "Explosive_TwoHanded", notes = "Knight presentation using the imported heavy melee controller." },
        new PrefabBindingConfig { prefabName = "TT_King", profileAssetName = "Explosive_TwoHanded", notes = "Hero king presentation." },

        new PrefabBindingConfig { prefabName = "TT_Heavy_Infantry", profileAssetName = "Explosive_Knight", notes = "Shieldman presentation." },
        new PrefabBindingConfig { prefabName = "TT_HeavySwordman", profileAssetName = "Explosive_Knight", notes = "Shield guard presentation." },
        new PrefabBindingConfig { prefabName = "TT_Heavy_Cavalry", profileAssetName = "Explosive_Knight", notes = "Guardian presentation." },
        new PrefabBindingConfig { prefabName = "TT_Paladin", profileAssetName = "Explosive_Knight", notes = "Paladin presentation." },
        new PrefabBindingConfig { prefabName = "TT_Mounted_Paladin", profileAssetName = "Explosive_Knight", notes = "Mounted paladin variant presentation." },

        new PrefabBindingConfig { prefabName = "TT_Archer", profileAssetName = "Explosive_Archer", notes = "Archer presentation." },
        new PrefabBindingConfig { prefabName = "TT_Crossbowman", profileAssetName = "Explosive_Archer", notes = "Crossbowman presentation." },
        new PrefabBindingConfig { prefabName = "TT_Mounted_Scout", profileAssetName = "Explosive_Archer", notes = "Ranger / mounted scout presentation." },

        new PrefabBindingConfig { prefabName = "TT_Mounted_Priest", profileAssetName = "Explosive_Mage", notes = "Cleric presentation." },
        new PrefabBindingConfig { prefabName = "TT_Priest", profileAssetName = "Explosive_Mage", notes = "Priest presentation." },
        new PrefabBindingConfig { prefabName = "TT_HighPriest", profileAssetName = "Explosive_Mage", notes = "High priest presentation." },
        new PrefabBindingConfig { prefabName = "TT_Mage", profileAssetName = "Explosive_Mage", notes = "Mage presentation." },
        new PrefabBindingConfig { prefabName = "TT_Mounted_Mage", profileAssetName = "Explosive_Mage", notes = "Wizard presentation." },
        new PrefabBindingConfig { prefabName = "TT_Mounted_King", profileAssetName = "Explosive_Mage", notes = "Thaumaturge / arcane king presentation." },
        new PrefabBindingConfig { prefabName = "TT_Commander", profileAssetName = "Explosive_Mage", notes = "Current bishop presentation." },
    };

    [MenuItem("Castle Defender/Animation/Prepare Warrior Pack Bundle 2 Profiles")]
    public static void Run()
    {
        EnsureFolder("Assets/Registry");
        EnsureFolder(OutputRoot);

        bool hasImportedAnimationAssets = HasImportedAnimationAssets(ExplosiveRoot);
        PrepareProfiles(out _, out int assignedControllers);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (!hasImportedAnimationAssets)
        {
            Debug.LogWarning(
                "[PrepareWarriorPackBundle2Profiles] Created profile assets, but no real animation content was found under Assets/ExplosiveLLC. " +
                "Import the actual Warrior Pack Bundle 2 assets before applying bindings.");
            return;
        }

        Debug.Log(
            $"[PrepareWarriorPackBundle2Profiles] Prepared Warrior Pack profiles in '{OutputRoot}' and assigned {assignedControllers} controller reference(s).");
    }

    [MenuItem("Castle Defender/Animation/Apply Warrior Pack Bundle 2 TT_RTS Mapping")]
    public static void ApplyTTRTSMapping()
    {
        Run();
        PrepareProfiles(out var profilesByAssetName, out _);

        int updatedPrefabs = 0;
        int missingPrefabs = 0;
        int missingProfiles = 0;

        for (int i = 0; i < PrefabBindingConfigs.Length; i++)
        {
            PrefabBindingConfig config = PrefabBindingConfigs[i];
            if (!profilesByAssetName.TryGetValue(config.profileAssetName, out var profile) || profile == null)
            {
                Debug.LogWarning(
                    $"[PrepareWarriorPackBundle2Profiles] Missing animation profile '{config.profileAssetName}' while binding '{config.prefabName}'.");
                missingProfiles++;
                continue;
            }

            string prefabPath = $"{TTRTSPrefabRoot}/{config.prefabName}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogWarning($"[PrepareWarriorPackBundle2Profiles] Prefab not found: {prefabPath}");
                missingPrefabs++;
                continue;
            }

            using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                var root = editScope.prefabContentsRoot;
                var binding = root.GetComponent<UnitAnimationBinding>();
                if (binding == null)
                    binding = root.AddComponent<UnitAnimationBinding>();

                binding.profile = profile;
                binding.profileId = profile.profileId;
                binding.attackFamilyOverride = UnitAnimationAttackFamily.Unspecified;
                binding.runtimeControllerOverride = null;
                binding.portraitControllerOverride = null;
                binding.overrideExistingControllers = false;
                binding.applyRootMotion = false;
                binding.animatorSpeedMultiplier = 1f;
                binding.notes = config.notes;

                var animator = root.GetComponentInChildren<Animator>(true);
                if (animator != null && profile.runtimeController != null)
                    animator.runtimeAnimatorController = profile.runtimeController;

                EditorUtility.SetDirty(binding);
                if (animator != null)
                    EditorUtility.SetDirty(animator);
            }

            updatedPrefabs++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[PrepareWarriorPackBundle2Profiles] Applied Warrior Pack mapping to {updatedPrefabs} prefab(s). " +
            $"missingPrefabs={missingPrefabs} missingProfiles={missingProfiles}");
    }

    static void PrepareProfiles(out Dictionary<string, UnitAnimationProfile> profilesByAssetName, out int assignedControllers)
    {
        profilesByAssetName = new Dictionary<string, UnitAnimationProfile>();
        assignedControllers = 0;

        for (int i = 0; i < ProfileConfigs.Length; i++)
        {
            ProfileConfig config = ProfileConfigs[i];
            RuntimeAnimatorController controller = LoadController(config.controllerPath, config.controllerHint);
            if (controller != null)
                assignedControllers++;

            UnitAnimationProfile profile = CreateOrUpdateProfile(config, controller);
            profilesByAssetName[config.assetName] = profile;
        }
    }

    static UnitAnimationProfile CreateOrUpdateProfile(ProfileConfig config, RuntimeAnimatorController controller)
    {
        string assetPath = $"{OutputRoot}/{config.assetName}.asset";
        var profile = AssetDatabase.LoadAssetAtPath<UnitAnimationProfile>(assetPath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<UnitAnimationProfile>();
            AssetDatabase.CreateAsset(profile, assetPath);
        }

        profile.profileId = config.profileId;
        profile.defaultAttackFamily = config.attackFamily;
        profile.runtimeController = controller;
        profile.portraitController = controller;
        profile.notes = config.notes;
        profile.overrideExistingControllers = controller != null;
        profile.applyRootMotion = false;
        profile.animatorSpeedMultiplier = 1f;

        ApplyProfileFlavor(profile, config.flavor);

        EditorUtility.SetDirty(profile);
        return profile;
    }

    static void ApplyProfileFlavor(UnitAnimationProfile profile, ProfileFlavor flavor)
    {
        profile.transitions.idleTransitionSeconds = 0.08f;
        profile.transitions.moveTransitionSeconds = 0.08f;
        profile.transitions.attackTransitionSeconds = 0.05f;
        profile.transitions.defendTransitionSeconds = 0.07f;
        profile.transitions.retreatTransitionSeconds = 0.08f;
        profile.transitions.hitTransitionSeconds = 0.05f;
        profile.transitions.deathTransitionSeconds = 0.05f;
        profile.transitions.spawnTransitionSeconds = 0.05f;

        SetStateAliases(profile.idle, "Idle", "Idle-Sheathed", "Sheathed", "UnSheathed", "Unsheathed");
        SetStateAliases(profile.move, "WalkRun", "Run", "Walk", "Move");
        SetStateAliases(profile.defend, "Blocking", "Block", "Defend", "ShieldBlock", "Idle");
        SetStateAliases(profile.retreat, "WalkRun", "Run", "Walk", "Move", "Retreat");
        SetStateAliases(profile.hitReact, "Damage", "LightHit", "Block-HitReact", "Hit", "HitReact", "Hurt");
        SetStateAliases(profile.death, "Death", "Die", "Knockout");
        SetStateAliases(profile.spawn, "WeaponUnSheath", "WeaponUnsheath2", "UnSheathed", "Unsheathed", "Spawn", "Idle");

        switch (flavor)
        {
            case ProfileFlavor.Knight:
                SetStateAliases(profile.attackDefault, "Attack1", "Attack2", "Attack3", "MoveAttack1", "Jump-Attack1", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackMelee, "Attack1", "Attack2", "Attack3", "MoveAttack1", "Jump-Attack1", "SpecialAttack1", "SpecialAttack2", "Block");
                SetStateAliases(profile.attackRanged, "RangeAttack1", "Attack1", "MoveAttack1");
                SetStateAliases(profile.attackMagic, "RangeAttack1", "SpecialAttack1", "Attack1");
                SetStateAliases(profile.attackSupport, "RangeAttack1", "SpecialAttack1", "Attack1");
                SetStateAliases(profile.attackSiege, "RangeAttack1", "SpecialAttack1", "Attack1");
                break;

            case ProfileFlavor.Archer:
                SetStateAliases(profile.attackDefault, "RangeAttack1", "RangeAttack1_Run", "Aiming-Firing", "Attack1", "MoveAttack1", "MoveAttack2", "SpecialAttack1");
                SetStateAliases(profile.attackMelee, "Attack1", "MoveAttack1", "SpecialAttack1");
                SetStateAliases(profile.attackRanged, "RangeAttack1", "RangeAttack1_Run", "Aiming-Firing", "Attack1", "MoveAttack1", "MoveAttack2", "SpecialAttack1");
                SetStateAliases(profile.attackMagic, "RangeAttack1", "SpecialAttack1", "Attack1");
                SetStateAliases(profile.attackSupport, "RangeAttack1", "SpecialAttack1", "Attack1");
                SetStateAliases(profile.attackSiege, "RangeAttack1", "SpecialAttack1", "Attack1");
                break;

            case ProfileFlavor.Mage:
                SetStateAliases(profile.attackDefault, "RangeAttack1", "RangeAttack2", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackMelee, "Attack1", "Attack2", "SpecialAttack1");
                SetStateAliases(profile.attackRanged, "RangeAttack1", "RangeAttack2", "Attack1", "SpecialAttack1");
                SetStateAliases(profile.attackMagic, "RangeAttack1", "RangeAttack2", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackSupport, "RangeAttack1", "RangeAttack2", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackSiege, "RangeAttack1", "SpecialAttack1", "SpecialAttack2");
                break;

            case ProfileFlavor.TwoHanded:
                SetStateAliases(profile.attackDefault, "Attack1", "Attack2", "Attack3", "MoveAttack1", "Run2-Attack1", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackMelee, "Attack1", "Attack2", "Attack3", "MoveAttack1", "Run2-Attack1", "Jump-Attack1", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackRanged, "RangeAttack1", "Attack1", "MoveAttack1");
                SetStateAliases(profile.attackMagic, "RangeAttack1", "SpecialAttack1", "Attack1");
                SetStateAliases(profile.attackSupport, "RangeAttack1", "SpecialAttack1", "Attack1");
                SetStateAliases(profile.attackSiege, "RangeAttack1", "SpecialAttack1", "Attack1");
                break;

            default:
                SetStateAliases(profile.attackDefault, "Attack1", "Attack2", "Attack3", "MoveAttack1", "MoveAttack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackMelee, "Attack1", "Attack2", "Attack3", "MoveAttack1", "MoveAttack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackRanged, "RangeAttack1", "Aiming-Firing", "Attack1", "MoveAttack1", "MoveAttack2", "SpecialAttack1");
                SetStateAliases(profile.attackMagic, "RangeAttack1", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackSupport, "RangeAttack1", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2");
                SetStateAliases(profile.attackSiege, "RangeAttack1", "SpecialAttack1", "SpecialAttack2", "Attack1");
                break;
        }
    }

    static void SetStateAliases(UnitAnimationStateAliases aliases, params string[] stateNames)
    {
        if (aliases == null)
            return;

        aliases.stateNames = stateNames ?? System.Array.Empty<string>();
    }

    static RuntimeAnimatorController LoadController(string controllerPath, string controllerHint)
    {
        if (!string.IsNullOrWhiteSpace(controllerPath))
        {
            var exactMatch = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            if (exactMatch != null)
                return exactMatch;
        }

        if (string.IsNullOrWhiteSpace(controllerHint))
            return null;

        string normalizedHint = controllerHint.Trim().ToLowerInvariant();
        string[] controllerGuids = AssetDatabase.FindAssets("t:RuntimeAnimatorController", new[] { ExplosiveRoot });
        for (int i = 0; i < controllerGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(controllerGuids[i]);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            string fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!fileName.Contains(normalizedHint))
                continue;

            var match = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
            if (match != null)
                return match;
        }

        return null;
    }

    static bool HasImportedAnimationAssets(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return false;

        string projectRoot = Directory.GetCurrentDirectory();
        string absoluteRoot = Path.Combine(projectRoot, root.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(absoluteRoot))
            return false;

        var allowedExtensions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ".anim",
            ".controller",
            ".overridecontroller",
            ".fbx",
            ".prefab",
        };

        foreach (string filePath in Directory.EnumerateFiles(absoluteRoot, "*.*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(filePath);
            if (allowedExtensions.Contains(extension))
                return true;
        }

        return false;
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string[] parts = assetPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
