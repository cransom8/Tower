// BuildTTRTSUnitPrefabs.cs
// Menu: Castle Defender → Setup → Build TT-RTS Unit Prefabs (24)
//
// The 24 TT-RTS placeholder prefabs (TT_Light_Infantry, TT_Archer, etc.) were
// empty shell GameObjects.  The sample controllers in controllers/ are also
// stubs — each has only one state (e.g. "infantry_01_idle") whose name never
// matches the state-name arrays in LaneRenderer ("Idle", "Walk", "Attack1" …).
//
// This script fixes both problems in one pass:
//   1. Rebuilds each controller in controllers/ with states named exactly as
//      LaneRenderer.TryCrossFade() expects:
//        Idle · Walk · Run · Attack1 · Attack2 · Damage · Death
//      Each state is wired to the correct animation clip from the pack FBXes.
//   2. Rebuilds each prefab from the source character FBX
//      (TT_RTS_Characters_customizable or TT_RTS_Cavalry_customizable), adds
//      an Animator (controller + avatar), applies TT_RTS_Units_white.mat to all
//      SkinnedMeshRenderers, and overwrites the placeholder.
//
// Safe to re-run: controllers and prefabs are fully replaced each time.
// Run AFTER importing the TT-RTS pack; then run SetupTTRTSSkins to register the
// skin entries in UnitPrefabRegistry.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class BuildTTRTSUnitPrefabs
{
    // ── Base paths ────────────────────────────────────────────────────────────
    const string BASE    = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard";
    const string AI      = BASE + "/animation/animation_infantry";
    const string AC      = BASE + "/animation/animation_cavalry";
    const string CTRL    = BASE + "/controllers";
    const string PFB     = BASE + "/prefabs";
    const string MAT_W   = BASE + "/models/materials/color/Units/TT_RTS_Units_white.mat";
    const string FBX_INF = BASE + "/models/units/TT_RTS_Characters_customizable.FBX";
    const string FBX_CAV = BASE + "/models/units/TT_RTS_Cavalry_customizable.FBX";

    // ── Animation group descriptor ────────────────────────────────────────────
    struct AnimGroup
    {
        public string idle, walk, run, attack, attack2, damage, death;
    }

    // Infantry-style group: attack_A / attack_B variants
    static AnimGroup InfGrp(string sub, string pfx) => new AnimGroup
    {
        idle    = $"{AI}/{sub}/{pfx}_01_idle.FBX",
        walk    = $"{AI}/{sub}/{pfx}_02_walk.FBX",
        run     = $"{AI}/{sub}/{pfx}_03_run.FBX",
        attack  = $"{AI}/{sub}/{pfx}_04_attack_A.FBX",
        attack2 = $"{AI}/{sub}/{pfx}_04_attack_B.FBX",
        damage  = $"{AI}/{sub}/{pfx}_05_damage.FBX",
        death   = $"{AI}/{sub}/{pfx}_06_death_A.FBX",
    };

    // Cavalry-style group: single attack, no _B variant
    static AnimGroup CavGrp(string sub, string pfx) => new AnimGroup
    {
        idle    = $"{AC}/{sub}/{pfx}_01_idle.FBX",
        walk    = $"{AC}/{sub}/{pfx}_02_walk.FBX",
        run     = $"{AC}/{sub}/{pfx}_03_run.FBX",
        attack  = $"{AC}/{sub}/{pfx}_04_attack.FBX",
        attack2 = null,
        damage  = $"{AC}/{sub}/{pfx}_05_damage.FBX",
        death   = $"{AC}/{sub}/{pfx}_06_death_A.FBX",
    };

    static readonly Dictionary<string, AnimGroup> Groups = new Dictionary<string, AnimGroup>
    {
        ["Infantry"]  = InfGrp("Infantry",        "infantry"),
        ["Archer"]    = InfGrp("Archer",           "archer"),
        ["Crossbow"]  = InfGrp("Crossbow",         "crossbow"),
        ["Polearm"]   = InfGrp("Polearm",          "polearm"),
        ["Shield"]    = InfGrp("Shield",           "shield"),
        ["Cavalry"]   = CavGrp("cavalry",          "cavalry"),
        ["CavArcher"] = CavGrp("cavalry_archer",   "cav_archer"),
        ["CavSpear"]  = CavGrp("cavalry_spear_A",  "cav_spear_A"),
    };

    // ── Unit definitions ──────────────────────────────────────────────────────
    struct UnitDef
    {
        public string prefabName;  // filename in prefabs/ (no .prefab)
        public string ctrlKey;     // filename in controllers/ (no .controller)
        public string animGroup;   // key in Groups dict
        public bool   isCavalry;   // true → use Cavalry FBX; false → Characters FBX
    }

    static readonly UnitDef[] Units =
    {
        // ── Foot: basic ───────────────────────────────────────────────────────
        new UnitDef { prefabName="TT_Peasant",        ctrlKey="tt_peasant",        animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_Scout",          ctrlKey="tt_scout",          animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_Settler",        ctrlKey="tt_settler",        animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_Light_Infantry", ctrlKey="tt_light_infantry", animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_Spearman",       ctrlKey="tt_spearman",       animGroup="Polearm",   isCavalry=false },
        // ── Foot: ranged ──────────────────────────────────────────────────────
        new UnitDef { prefabName="TT_Archer",         ctrlKey="tt_archer",         animGroup="Archer",    isCavalry=false },
        new UnitDef { prefabName="TT_Crossbowman",    ctrlKey="tt_crossbowman",    animGroup="Crossbow",  isCavalry=false },
        // ── Foot: medium / heavy ──────────────────────────────────────────────
        new UnitDef { prefabName="TT_Heavy_Infantry", ctrlKey="tt_heavy_infantry", animGroup="Shield",    isCavalry=false },
        new UnitDef { prefabName="TT_Halberdier",     ctrlKey="tt_halberdier",     animGroup="Polearm",   isCavalry=false },
        new UnitDef { prefabName="TT_HeavySwordman",  ctrlKey="tt_heavy_swordman", animGroup="Infantry",  isCavalry=false },
        // ── Foot: support / magic ─────────────────────────────────────────────
        new UnitDef { prefabName="TT_Priest",         ctrlKey="tt_priest",         animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_HighPriest",     ctrlKey="tt_high_priest",    animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_Mage",           ctrlKey="tt_mage",           animGroup="Infantry",  isCavalry=false },
        // ── Foot: elite ───────────────────────────────────────────────────────
        new UnitDef { prefabName="TT_Paladin",        ctrlKey="tt_paladin",        animGroup="Shield",    isCavalry=false },
        new UnitDef { prefabName="TT_Commander",      ctrlKey="tt_commander",      animGroup="Infantry",  isCavalry=false },
        new UnitDef { prefabName="TT_King",           ctrlKey="tt_king",           animGroup="Infantry",  isCavalry=false },
        // ── Cavalry ───────────────────────────────────────────────────────────
        new UnitDef { prefabName="TT_Light_Cavalry",   ctrlKey="tt_light_cavalry",   animGroup="Cavalry",  isCavalry=true  },
        new UnitDef { prefabName="TT_Heavy_Cavalry",   ctrlKey="tt_heavy_cavalry",   animGroup="Cavalry",  isCavalry=true  },
        new UnitDef { prefabName="TT_Mounted_Scout",   ctrlKey="tt_mounted_scout",   animGroup="Cavalry",  isCavalry=true  },
        new UnitDef { prefabName="TT_Mounted_Knight",  ctrlKey="tt_mounted_knight",  animGroup="Cavalry",  isCavalry=true  },
        new UnitDef { prefabName="TT_Mounted_Mage",    ctrlKey="tt_mounted_mage",    animGroup="CavArcher",isCavalry=true  },
        new UnitDef { prefabName="TT_Mounted_Paladin", ctrlKey="tt_mounted_paladin", animGroup="CavSpear", isCavalry=true  },
        new UnitDef { prefabName="TT_Mounted_Priest",  ctrlKey="tt_mounted_priest",  animGroup="CavArcher",isCavalry=true  },
        new UnitDef { prefabName="TT_Mounted_King",    ctrlKey="tt_mounted_king",    animGroup="Cavalry",  isCavalry=true  },
    };

    // ── Entry point ───────────────────────────────────────────────────────────
    [MenuItem("Castle Defender/Setup/Build TT-RTS Unit Prefabs (24)")]
    public static void Run()
    {
        var matWhite = AssetDatabase.LoadAssetAtPath<Material>(MAT_W);
        if (matWhite == null)
        {
            Debug.LogError("[BuildTTRTSUnitPrefabs] White material not found: " + MAT_W);
            return;
        }

        var infAvatar = LoadAvatarFromFBX(FBX_INF);
        var cavAvatar = LoadAvatarFromFBX(FBX_CAV);

        int built = 0, failed = 0;

        foreach (var u in Units)
        {
            if (!Groups.TryGetValue(u.animGroup, out var grp))
            {
                Debug.LogError($"[BuildTTRTSUnitPrefabs] Unknown anim group '{u.animGroup}' for {u.prefabName}");
                failed++;
                continue;
            }

            // 1. Rebuild AnimatorController
            string ctrlPath = $"{CTRL}/{u.ctrlKey}.controller";
            var ctrl = BuildController(ctrlPath, grp);
            if (ctrl == null) { failed++; continue; }

            // 2. Rebuild prefab from source FBX
            string fbxPath    = u.isCavalry ? FBX_CAV : FBX_INF;
            var    avatar     = u.isCavalry ? cavAvatar : infAvatar;
            string prefabPath = $"{PFB}/{u.prefabName}.prefab";

            bool ok = BuildPrefab(prefabPath, fbxPath, ctrl, avatar, matWhite);
            if (ok)
            {
                Debug.Log($"[BuildTTRTSUnitPrefabs] ✓ {u.prefabName}");
                built++;
            }
            else
            {
                failed++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[BuildTTRTSUnitPrefabs] Done — built={built}  failed={failed}");
    }

    // ── Build a controller with LaneRenderer-compatible state names ───────────
    static AnimatorController BuildController(string path, AnimGroup g)
    {
        // Delete stale controller (the old single-state copy from SetupTTRTSSkins)
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            AssetDatabase.DeleteAsset(path);

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        var sm   = ctrl.layers[0].stateMachine;

        bool defaultSet = false;
        void Add(string stateName, string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath)) return;
            var clip = LoadClip(fbxPath);
            if (clip == null)
            {
                Debug.LogWarning($"[BuildTTRTSUnitPrefabs] Clip not found, skipping state '{stateName}': {fbxPath}");
                return;
            }
            var state = sm.AddState(stateName);
            state.motion = clip;
            state.writeDefaultValues = false;
            if (!defaultSet) { sm.defaultState = state; defaultSet = true; }
        }

        Add("Idle",    g.idle);
        Add("Walk",    g.walk);
        Add("Run",     g.run);
        Add("Attack1", g.attack);
        Add("Attack2", g.attack2);
        Add("Damage",  g.damage);
        Add("Death",   g.death);

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    // ── Build (overwrite) a unit prefab ───────────────────────────────────────
    static bool BuildPrefab(string prefabPath, string fbxPath,
                            AnimatorController ctrl, Avatar avatar, Material mat)
    {
        var fbxRoot = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxRoot == null)
        {
            Debug.LogError("[BuildTTRTSUnitPrefabs] FBX not found: " + fbxPath);
            return false;
        }

        var go = Object.Instantiate(fbxRoot);
        go.name = System.IO.Path.GetFileNameWithoutExtension(prefabPath);

        // Ensure Animator on root
        var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl;
        if (avatar != null) anim.avatar = avatar;
        anim.applyRootMotion = false;
        anim.updateMode      = AnimatorUpdateMode.Normal;
        anim.cullingMode     = AnimatorCullingMode.AlwaysAnimate;

        // Apply white material to all SkinnedMeshRenderers
        // (LaneRenderer will tint it per-team via _BaseColor at runtime)
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mats = new Material[smr.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            smr.sharedMaterials = mats;
        }

        var saved = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);

        if (saved == null)
        {
            Debug.LogError("[BuildTTRTSUnitPrefabs] Failed to save prefab: " + prefabPath);
            return false;
        }
        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Loads the first non-preview AnimationClip sub-asset from an FBX.
    static AnimationClip LoadClip(string fbxPath)
    {
        if (string.IsNullOrEmpty(fbxPath)) return null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (a is AnimationClip c && !c.name.Contains("__preview__"))
                return c;
        return null;
    }

    // Loads the Avatar sub-asset embedded in an FBX.
    static Avatar LoadAvatarFromFBX(string fbxPath)
    {
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (a is Avatar av && av != null)
                return av;
        Debug.LogWarning("[BuildTTRTSUnitPrefabs] No avatar found in FBX: " + fbxPath);
        return null;
    }
}
