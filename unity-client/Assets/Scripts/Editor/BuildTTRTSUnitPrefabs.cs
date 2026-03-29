using System.Collections.Generic;
using CastleDefender.Game;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BuildTTRTSUnitPrefabs
{
    const string BasePath = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard";
    const string InfantryAnimationPath = BasePath + "/animation/animation_infantry";
    const string CavalryAnimationPath = BasePath + "/animation/animation_cavalry";
    const string ControllerPath = BasePath + "/controllers";
    const string PrefabPath = BasePath + "/prefabs";
    const string NeutralUnitMaterialPath = BasePath + "/models/materials/color/Units/TT_RTS_Units_white.mat";
    const string InfantrySourcePath = BasePath + "/models/units/TT_RTS_Characters_customizable.FBX";
    const string RedTeamMaterialPath = "Assets/Materials/TT/TT_RTS_Units_red_URP.mat";
    const string BlueTeamMaterialPath = "Assets/Materials/TT/TT_RTS_Units_blue_URP.mat";
    const string YellowTeamMaterialPath = "Assets/Materials/TT/TT_RTS_Units_yellow_URP.mat";
    const string GreenTeamMaterialPath = "Assets/Materials/TT/TT_RTS_Units_green_URP.mat";

    struct AnimGroup
    {
        public string idle;
        public string walk;
        public string run;
        public string attack;
        public string attack2;
        public string damage;
        public string death;
    }

    struct Loadout
    {
        public string body;
        public string head;
        public string rightHand;
        public string leftHand;
        public string shield;
        public string backpack;
        public bool tintBody;
        public bool tintShield;
    }

    struct UnitDef
    {
        public string prefabName;
        public string ctrlKey;
        public string animGroup;
        public Loadout loadout;
    }

    struct TeamMaterials
    {
        public Material red;
        public Material blue;
        public Material yellow;
        public Material green;
    }

    static AnimGroup InfantryGroup(string subFolder, string prefix)
    {
        return new AnimGroup
        {
            idle = $"{InfantryAnimationPath}/{subFolder}/{prefix}_01_idle.FBX",
            walk = $"{InfantryAnimationPath}/{subFolder}/{prefix}_02_walk.FBX",
            run = $"{InfantryAnimationPath}/{subFolder}/{prefix}_03_run.FBX",
            attack = $"{InfantryAnimationPath}/{subFolder}/{prefix}_04_attack_A.FBX",
            attack2 = $"{InfantryAnimationPath}/{subFolder}/{prefix}_04_attack_B.FBX",
            damage = $"{InfantryAnimationPath}/{subFolder}/{prefix}_05_damage.FBX",
            death = $"{InfantryAnimationPath}/{subFolder}/{prefix}_06_death_A.FBX",
        };
    }

    static AnimGroup CavalryGroup(string subFolder, string prefix)
    {
        return new AnimGroup
        {
            idle = $"{CavalryAnimationPath}/{subFolder}/{prefix}_01_idle.FBX",
            walk = $"{CavalryAnimationPath}/{subFolder}/{prefix}_02_walk.FBX",
            run = $"{CavalryAnimationPath}/{subFolder}/{prefix}_03_run.FBX",
            attack = $"{CavalryAnimationPath}/{subFolder}/{prefix}_04_attack.FBX",
            attack2 = null,
            damage = $"{CavalryAnimationPath}/{subFolder}/{prefix}_05_damage.FBX",
            death = $"{CavalryAnimationPath}/{subFolder}/{prefix}_06_death_A.FBX",
        };
    }

    static readonly Dictionary<string, AnimGroup> Groups = new Dictionary<string, AnimGroup>
    {
        ["Infantry"] = InfantryGroup("Infantry", "infantry"),
        ["Archer"] = InfantryGroup("Archer", "archer"),
        ["Crossbow"] = InfantryGroup("Crossbow", "crossbow"),
        ["Polearm"] = InfantryGroup("Polearm", "polearm"),
        ["Shield"] = InfantryGroup("Shield", "shield"),
        ["Cavalry"] = CavalryGroup("cavalry", "cavalry"),
        ["CavArcher"] = CavalryGroup("cavalry_archer", "cav_archer"),
        ["CavSpear"] = CavalryGroup("cavalry_spear_A", "cav_spear_A"),
    };

    static readonly UnitDef[] Units =
    {
        new UnitDef { prefabName = "TT_Peasant", ctrlKey = "tt_peasant", animGroup = "Infantry", loadout = LoadoutWith("Body_01a", "Head_01a", rightHand: "w_short_sword", tintBody: true) },
        new UnitDef { prefabName = "TT_Scout", ctrlKey = "tt_scout", animGroup = "Archer", loadout = LoadoutWith("Body_06a", "Head_05a", leftHand: "w_short_bow", backpack: "quiver_A", tintBody: true) },
        new UnitDef { prefabName = "TT_Settler", ctrlKey = "tt_settler", animGroup = "Infantry", loadout = LoadoutWith("Body_01b", "Head_01b", rightHand: "w_club", tintBody: true) },
        new UnitDef { prefabName = "TT_Light_Infantry", ctrlKey = "tt_light_infantry", animGroup = "Infantry", loadout = LoadoutWith("Body_10a", "Head_04a", rightHand: "w_sword", tintBody: true) },
        new UnitDef { prefabName = "TT_Spearman", ctrlKey = "tt_spearman", animGroup = "Polearm", loadout = LoadoutWith("Body_02a", "Head_02a", rightHand: "w_spear", tintBody: true) },
        new UnitDef { prefabName = "TT_Archer", ctrlKey = "tt_archer", animGroup = "Archer", loadout = LoadoutWith("Body_03a", "Head_03a", leftHand: "w_recurve_bow", backpack: "quiver_A", tintBody: true) },
        new UnitDef { prefabName = "TT_Crossbowman", ctrlKey = "tt_crossbowman", animGroup = "Crossbow", loadout = LoadoutWith("Body_10b", "Head_10b", rightHand: "w_crossbow", tintBody: true) },
        new UnitDef { prefabName = "TT_Heavy_Infantry", ctrlKey = "tt_heavy_infantry", animGroup = "Shield", loadout = LoadoutWith("Body_04a", "Head_04a", shield: "shield_02", tintBody: true, tintShield: true) },
        new UnitDef { prefabName = "TT_Halberdier", ctrlKey = "tt_halberdier", animGroup = "Polearm", loadout = LoadoutWith("Body_11a", "Head_10c", rightHand: "w_halberd", tintBody: true) },
        new UnitDef { prefabName = "TT_HeavySwordman", ctrlKey = "tt_heavy_swordman", animGroup = "Shield", loadout = LoadoutWith("Body_12a", "Head_04a", rightHand: "w_sword_B", shield: "shield_09", tintBody: true, tintShield: true) },
        new UnitDef { prefabName = "TT_Priest", ctrlKey = "tt_priest", animGroup = "Infantry", loadout = LoadoutWith("Body_11b", "Head_11b", leftHand: "w_staff_B", tintBody: true) },
        new UnitDef { prefabName = "TT_HighPriest", ctrlKey = "tt_high_priest", animGroup = "Infantry", loadout = LoadoutWith("Body_12b", "Head_12a", leftHand: "w_staff_D", tintBody: true) },
        new UnitDef { prefabName = "TT_Mage", ctrlKey = "tt_mage", animGroup = "Infantry", loadout = LoadoutWith("Body_10d", "Head_11c", leftHand: "w_staff_C", tintBody: true) },
        new UnitDef { prefabName = "TT_Paladin", ctrlKey = "tt_paladin", animGroup = "Shield", loadout = LoadoutWith("Body_11a", "Head_10c", rightHand: "w_broad_sword", shield: "shield_20", tintBody: true, tintShield: true) },
        new UnitDef { prefabName = "TT_Commander", ctrlKey = "tt_commander", animGroup = "Infantry", loadout = LoadoutWith("Body_12b", "Head_05a", leftHand: "w_staff_A", tintBody: true) },
        new UnitDef { prefabName = "TT_King", ctrlKey = "tt_king", animGroup = "Infantry", loadout = LoadoutWith("Body_13c", "Head_12e", rightHand: "w_broad_sword_B", tintBody: true) },
        new UnitDef { prefabName = "TT_Light_Cavalry", ctrlKey = "tt_light_cavalry", animGroup = "Polearm", loadout = LoadoutWith("Body_12d", "Head_10a", rightHand: "w_pike", shield: "shield_05", tintBody: true, tintShield: true) },
        new UnitDef { prefabName = "TT_Heavy_Cavalry", ctrlKey = "tt_heavy_cavalry", animGroup = "Shield", loadout = LoadoutWith("Body_10b", "Head_10c", rightHand: "w_broad_sword_B", shield: "shield_12", tintBody: true, tintShield: true) },
        new UnitDef { prefabName = "TT_Mounted_Scout", ctrlKey = "tt_mounted_scout", animGroup = "Archer", loadout = LoadoutWith("Body_06b", "Head_05b", leftHand: "w_long_bow", backpack: "quiver_B", tintBody: true) },
        new UnitDef { prefabName = "TT_Mounted_Knight", ctrlKey = "tt_mounted_knight", animGroup = "Infantry", loadout = LoadoutWith("Body_10b", "Head_10c", rightHand: "w_TH_sword", tintBody: true) },
        new UnitDef { prefabName = "TT_Mounted_Mage", ctrlKey = "tt_mounted_mage", animGroup = "Infantry", loadout = LoadoutWith("Body_11d", "Head_11e", leftHand: "w_staff_C", tintBody: true) },
        new UnitDef { prefabName = "TT_Mounted_Paladin", ctrlKey = "tt_mounted_paladin", animGroup = "Shield", loadout = LoadoutWith("Body_11a", "Head_10c", rightHand: "w_spear", shield: "shield_16", tintBody: true, tintShield: true) },
        new UnitDef { prefabName = "TT_Mounted_Priest", ctrlKey = "tt_mounted_priest", animGroup = "Infantry", loadout = LoadoutWith("Body_06a", "Head_05a", leftHand: "w_staff_A", tintBody: true) },
        new UnitDef { prefabName = "TT_Mounted_King", ctrlKey = "tt_mounted_king", animGroup = "Infantry", loadout = LoadoutWith("Body_12e", "Head_12d", leftHand: "w_staff_D", tintBody: true) },
    };

    static Loadout LoadoutWith(
        string body,
        string head,
        string rightHand = null,
        string leftHand = null,
        string shield = null,
        string backpack = null,
        bool tintBody = false,
        bool tintShield = false)
    {
        return new Loadout
        {
            body = body,
            head = head,
            rightHand = rightHand,
            leftHand = leftHand,
            shield = shield,
            backpack = backpack,
            tintBody = tintBody,
            tintShield = tintShield,
        };
    }

    [MenuItem("Castle Defender/Setup/Build TT-RTS Unit Prefabs (24)")]
    public static void Run()
    {
        var neutralMaterial = AssetDatabase.LoadAssetAtPath<Material>(NeutralUnitMaterialPath);
        var teamMaterials = new TeamMaterials
        {
            red = AssetDatabase.LoadAssetAtPath<Material>(RedTeamMaterialPath),
            blue = AssetDatabase.LoadAssetAtPath<Material>(BlueTeamMaterialPath),
            yellow = AssetDatabase.LoadAssetAtPath<Material>(YellowTeamMaterialPath),
            green = AssetDatabase.LoadAssetAtPath<Material>(GreenTeamMaterialPath),
        };

        if (neutralMaterial == null || teamMaterials.red == null || teamMaterials.blue == null || teamMaterials.yellow == null || teamMaterials.green == null)
        {
            Debug.LogError("[BuildTTRTSUnitPrefabs] Required TT materials are missing.");
            return;
        }

        var infantryAvatar = LoadAvatarFromFBX(InfantrySourcePath);
        int built = 0;
        int failed = 0;

        foreach (var unit in Units)
        {
            if (!Groups.TryGetValue(unit.animGroup, out var group))
            {
                Debug.LogError($"[BuildTTRTSUnitPrefabs] Unknown anim group '{unit.animGroup}' for {unit.prefabName}.");
                failed++;
                continue;
            }

            string controllerAssetPath = $"{ControllerPath}/{unit.ctrlKey}.controller";
            var controller = BuildController(controllerAssetPath, group);
            if (controller == null)
            {
                failed++;
                continue;
            }

            string prefabAssetPath = $"{PrefabPath}/{unit.prefabName}.prefab";
            if (BuildPrefab(prefabAssetPath, controller, infantryAvatar, neutralMaterial, teamMaterials, unit.loadout))
            {
                built++;
                Debug.Log($"[BuildTTRTSUnitPrefabs] Built {unit.prefabName}.");
            }
            else
            {
                failed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BuildTTRTSUnitPrefabs] Done. built={built} failed={failed}");
    }

    static AnimatorController BuildController(string path, AnimGroup group)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            AssetDatabase.DeleteAsset(path);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        var stateMachine = controller.layers[0].stateMachine;
        bool defaultStateAssigned = false;

        void AddState(string stateName, string clipPath)
        {
            if (string.IsNullOrEmpty(clipPath))
                return;

            var clip = LoadClip(clipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[BuildTTRTSUnitPrefabs] Missing clip '{clipPath}' for state '{stateName}'.");
                return;
            }

            var state = stateMachine.AddState(stateName);
            state.motion = clip;
            state.writeDefaultValues = false;
            if (!defaultStateAssigned)
            {
                stateMachine.defaultState = state;
                defaultStateAssigned = true;
            }
        }

        AddState("Idle", group.idle);
        AddState("Walk", group.walk);
        AddState("Run", group.run);
        AddState("Attack1", group.attack);
        AddState("Attack2", group.attack2);
        AddState("Damage", group.damage);
        AddState("Death", group.death);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static bool BuildPrefab(
        string prefabAssetPath,
        AnimatorController controller,
        Avatar avatar,
        Material neutralMaterial,
        TeamMaterials teamMaterials,
        Loadout loadout)
    {
        var sourceRoot = AssetDatabase.LoadAssetAtPath<GameObject>(InfantrySourcePath);
        if (sourceRoot == null)
        {
            Debug.LogError("[BuildTTRTSUnitPrefabs] Infantry source model is missing.");
            return false;
        }

        var instance = Object.Instantiate(sourceRoot);
        instance.name = System.IO.Path.GetFileNameWithoutExtension(prefabAssetPath);

        try
        {
            var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (instance.GetComponent<TTRTSUnitProportionScaler>() == null)
                instance.AddComponent<TTRTSUnitProportionScaler>();

            if (!ApplyLoadout(instance, loadout, neutralMaterial, teamMaterials, out string error))
            {
                Debug.LogError($"[BuildTTRTSUnitPrefabs] {instance.name}: {error}");
                return false;
            }

            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(instance, prefabAssetPath);
            if (savedPrefab == null)
            {
                Debug.LogError($"[BuildTTRTSUnitPrefabs] Failed to save prefab '{prefabAssetPath}'.");
                return false;
            }

            return true;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static bool ApplyLoadout(
        GameObject root,
        Loadout loadout,
        Material neutralMaterial,
        TeamMaterials teamMaterials,
        out string error)
    {
        error = null;
        var tintTargets = new List<TeamColorMaterialProfile.Target>();

        if (!TryKeepSingleBody(root, loadout.body, out var bodyRenderer))
        {
            error = $"Body '{loadout.body}' was not found.";
            return false;
        }

        if (!TryKeepSingleChildRenderer(root, "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Neck/Bip001 Head/HEAD_CONTAINER", loadout.head, out _))
        {
            error = $"Head '{loadout.head}' was not found.";
            return false;
        }

        if (!TryKeepSingleChildRenderer(root, "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 R Clavicle/Bip001 R UpperArm/Bip001 R Forearm/Bip001 R Hand/R_hand_container", loadout.rightHand, out _))
        {
            error = $"Right-hand item '{loadout.rightHand}' was not found.";
            return false;
        }

        if (!TryKeepSingleChildRenderer(root, "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 L Clavicle/Bip001 L UpperArm/Bip001 L Forearm/Bip001 L Hand/L_hand_container", loadout.leftHand, out _))
        {
            error = $"Left-hand item '{loadout.leftHand}' was not found.";
            return false;
        }

        if (!TryKeepSingleChildRenderer(root, "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 L Clavicle/Bip001 L UpperArm/Bip001 L Forearm/Bip001 L Hand/L_shield_container", loadout.shield, out var shieldRenderer))
        {
            error = $"Shield '{loadout.shield}' was not found.";
            return false;
        }

        if (!TryKeepSingleChildRenderer(root, "Bip001/Bip001 Pelvis/Bip001 Spine/Backpack_container", loadout.backpack, out _))
        {
            error = $"Backpack item '{loadout.backpack}' was not found.";
            return false;
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            AssignMaterial(renderer, neutralMaterial);

        if (loadout.tintBody && bodyRenderer != null)
        {
            AssignMaterial(bodyRenderer, teamMaterials.blue);
            tintTargets.Add(new TeamColorMaterialProfile.Target
            {
                renderer = bodyRenderer,
                replaceAllMaterials = true,
                materialIndex = -1,
            });
        }

        if (loadout.tintShield && shieldRenderer != null)
        {
            AssignMaterial(shieldRenderer, teamMaterials.blue);
            tintTargets.Add(new TeamColorMaterialProfile.Target
            {
                renderer = shieldRenderer,
                replaceAllMaterials = true,
                materialIndex = -1,
            });
        }

        var teamColorProfile = root.GetComponent<TeamColorMaterialProfile>() ?? root.AddComponent<TeamColorMaterialProfile>();
        teamColorProfile.ConfigureForEditor(
            teamMaterials.red,
            teamMaterials.blue,
            teamMaterials.yellow,
            teamMaterials.green,
            tintTargets.ToArray());
        EditorUtility.SetDirty(teamColorProfile);

        return true;
    }

    static bool TryKeepSingleBody(GameObject root, string selectedBodyName, out Renderer selectedRenderer)
    {
        selectedRenderer = null;
        foreach (var bodyRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!bodyRenderer.name.StartsWith("Body_", System.StringComparison.Ordinal))
                continue;

            if (string.Equals(bodyRenderer.name, selectedBodyName, System.StringComparison.Ordinal))
            {
                selectedRenderer = bodyRenderer;
                continue;
            }

            Object.DestroyImmediate(bodyRenderer.gameObject);
        }

        return selectedRenderer != null;
    }

    static bool TryKeepSingleChildRenderer(GameObject root, string containerPath, string selectedChildName, out Renderer selectedRenderer)
    {
        selectedRenderer = null;
        Transform container = FindTransformByPath(root.transform, containerPath);
        if (container == null)
            return string.IsNullOrEmpty(selectedChildName);

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            var child = container.GetChild(i);
            if (!string.Equals(child.name, selectedChildName, System.StringComparison.Ordinal))
            {
                Object.DestroyImmediate(child.gameObject);
                continue;
            }

            selectedRenderer = child.GetComponent<Renderer>();
        }

        return selectedChildName == null || selectedRenderer != null;
    }

    static Transform FindTransformByPath(Transform root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path))
            return null;

        string[] segments = path.Split('/');
        Transform current = root;
        for (int i = 0; i < segments.Length; i++)
        {
            current = current.Find(segments[i]);
            if (current == null)
                return null;
        }

        return current;
    }

    static void AssignMaterial(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
            return;

        int materialCount = renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0
            ? renderer.sharedMaterials.Length
            : 1;
        var materials = new Material[materialCount];
        for (int i = 0; i < materials.Length; i++)
            materials[i] = material;
        renderer.sharedMaterials = materials;
    }

    static AnimationClip LoadClip(string fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath))
            return null;

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                return clip;
        }

        return null;
    }

    static Avatar LoadAvatarFromFBX(string fbxPath)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (asset is Avatar avatar && avatar != null)
                return avatar;
        }

        Debug.LogWarning($"[BuildTTRTSUnitPrefabs] No avatar found in '{fbxPath}'.");
        return null;
    }
}
