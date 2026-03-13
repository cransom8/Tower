// CreateHFCUnitPrefabs.cs
// Menu: Castle Defender → Setup → Create HFC Unit Prefabs (30)
//
// For each of the 30 HFC units from 034_new_units.sql:
//   1. Creates a URP Lit material using the unit's body BaseColor texture.
//   2. Creates a prefab: FBX mesh (SK_*.FBX) with Animator wired to the pack's
//      pre-built *_Controller.controller.  All SkinnedMeshRenderers get the body
//      material (good enough for top-down; refine per-unit later if desired).
//   3. Registers the prefab in Assets/Registry/UnitPrefabRegistry.asset.
//
// Safe to re-run: skips units whose prefab already exists; updates registry
// entries in place if the key already has a row.
//
// Output folders created automatically:
//   Assets/Prefabs/Units/HFC/   — prefabs
//   Assets/Materials/Units/HFC/ — URP Lit materials

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using CastleDefender.Game;

public static class CreateHFCUnitPrefabs
{
    // ── Pack root shortcuts ───────────────────────────────────────────────────
    const string HFC  = "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1";
    const string VIL  = HFC + "/Must Have Fantasy Villains Pack";
    const string DEAD = HFC + "/Living Dead Pack";
    const string ANIM = HFC + "/Fantasy Animals Pack";
    const string LIZ  = HFC + "/Fantasy Lizards Pack";
    const string MYTH = HFC + "/Mythological Creatures Pack";
    const string DEM  = HFC + "/Demonic Creatures Pack";

    // ── Output paths ─────────────────────────────────────────────────────────
    const string OUT_PREFABS  = "Assets/Prefabs/Units/HFC";
    const string OUT_MATS     = "Assets/Materials/Units/HFC";
    const string REGISTRY_PATH = "Assets/Registry/UnitPrefabRegistry.asset";

    // ── Unit definition ───────────────────────────────────────────────────────
    struct Def
    {
        public string key;
        public string fbx;         // asset path to SK_*.FBX
        public string controller;  // asset path to *_Controller.controller
        public string texture;     // asset path to body BaseColor .png
        public float  scale;       // default registry scale
    }

    // ── 30 HFC unit definitions ───────────────────────────────────────────────
    static readonly Def[] Defs = new[]
    {
        // ── Must Have Fantasy Villains ────────────────────────────────────────
        new Def { key="goblin",
            fbx        = VIL+"/Goblin/FBX Files/SK_Goblin.FBX",
            controller = VIL+"/Goblin/Goblin_Controller.controller",
            texture    = VIL+"/Goblin/Textures/GoblinBody_BaseColor.png",
            scale = 0.6f },

        new Def { key="kobold",
            fbx        = VIL+"/Kobold/FBX Files/SK_Kobold.FBX",
            controller = VIL+"/Kobold/Kobold_Controller.controller",
            texture    = VIL+"/Kobold/Textures/T_KoboldBody_BaseColor.png",
            scale = 0.5f },

        new Def { key="hobgoblin",
            fbx        = VIL+"/Hobgoblin/FBX Files/SK_Hobgoblin.FBX",
            controller = VIL+"/Hobgoblin/Hobgoblin_Controller.controller",
            texture    = VIL+"/Hobgoblin/Textures/T_HobgoblinBody_BaseColor.png",
            scale = 0.7f },

        new Def { key="orc",
            fbx        = VIL+"/Orc/FBX Files/SK_Orc.FBX",
            controller = VIL+"/Orc/Orc_Controller.controller",
            texture    = VIL+"/Orc/Textures/T_OrcBody_BaseColor.png",
            scale = 0.8f },

        new Def { key="ogre",
            fbx        = VIL+"/Ogre/FBX Files/SK_FatOgre.FBX",
            controller = VIL+"/Ogre/Ogre_Controller.controller",
            texture    = VIL+"/Ogre/Textures/T_FatOgre_BaseColor.png",
            scale = 1.0f },

        new Def { key="troll",
            fbx        = VIL+"/Troll/FBX Files/SK_Troll.FBX",
            controller = VIL+"/Troll/Troll_Controller.controller",
            texture    = VIL+"/Troll/Textures/T_Troll_BaseColor.png",
            scale = 0.9f },

        new Def { key="cyclops",
            fbx        = VIL+"/Cyclops/FBX Files/SK_Cyclops.FBX",
            controller = VIL+"/Cyclops/Cyclops_Controller.controller",
            texture    = VIL+"/Cyclops/Textures/T_Cyclops_BaseColor.png",
            scale = 1.1f },

        // ── Living Dead ───────────────────────────────────────────────────────
        new Def { key="ghoul",
            fbx        = DEAD+"/Ghoul/FBX Files/SK_Ghoul.FBX",
            controller = DEAD+"/Ghoul/Ghoul_Controller.controller",
            texture    = DEAD+"/Ghoul/Textures/T_Ghoul_BaseColor.png",
            scale = 0.75f },

        new Def { key="skeleton_knight",
            fbx        = DEAD+"/Skeleton Knight/FBX files/SK_SkeletonKnight.FBX",
            controller = DEAD+"/Skeleton Knight/SkeletonKnight_Controller.controller",
            texture    = DEAD+"/Skeleton Knight/Textures/T_SkeletonKnight_BaseColor.png",
            scale = 0.8f },

        new Def { key="undead_warrior",
            fbx        = DEAD+"/Undead/FBX Files/SK_Undead.FBX",
            controller = DEAD+"/Undead/Undead_Controller.controller",
            texture    = DEAD+"/Undead/Textures/T_UndeadBody_BaseColor.png",
            scale = 0.8f },

        new Def { key="mummy",
            fbx        = DEAD+"/Mummy/FBX Files/SK_Mummy.FBX",
            controller = DEAD+"/Mummy/Mummy_Controller.controller",
            texture    = DEAD+"/Mummy/Textures/T_Mummy_BaseColor.png",
            scale = 0.85f },

        new Def { key="vampire",
            fbx        = DEAD+"/Vampire/FBX Files/SK_Vampire.FBX",
            controller = DEAD+"/Vampire/Vampire_Controller.controller",
            texture    = DEAD+"/Vampire/Textures/T_Vampire_BaseColor.png",
            scale = 0.8f },

        // ── Fantasy Animals ───────────────────────────────────────────────────
        new Def { key="giant_rat",
            fbx        = ANIM+"/Giant Rat/FBX Files/SK_GiantRat.FBX",
            controller = ANIM+"/Giant Rat/GiantRat_Controller.controller",
            texture    = ANIM+"/Giant Rat/Textures/T_GiantRat_BaseColor.png",
            scale = 0.5f },

        new Def { key="fantasy_wolf",
            fbx        = ANIM+"/Fantasy Wolf/FBX Files/SK_FantasyWolf.FBX",
            controller = ANIM+"/Fantasy Wolf/FantasyWolf_Controller.controller",
            texture    = ANIM+"/Fantasy Wolf/Textures/T_FantasyWolf_BaseColor.png",
            scale = 0.7f },

        new Def { key="giant_viper",
            fbx        = ANIM+"/Giant Viper/FBX Files/SK_GiantViper.FBX",
            controller = ANIM+"/Giant Viper/GiantViper_Controller.controller",
            texture    = ANIM+"/Giant Viper/Textures/T_GiantViper_BaseColor1.png",
            scale = 0.8f },

        new Def { key="darkness_spider",
            fbx        = ANIM+"/Darkness Spider/FBX Files/SK_DarknessSpider.FBX",
            controller = ANIM+"/Darkness Spider/DarknessSpider_Controller.controller",
            texture    = ANIM+"/Darkness Spider/Textures/DarknessSpider_BaseColor.png",
            scale = 0.75f },

        // ── Fantasy Lizards ───────────────────────────────────────────────────
        new Def { key="lizard_warrior",
            fbx        = LIZ+"/Lizard Warrior/FBX Files/SK_LizardWarrior.FBX",
            controller = LIZ+"/Lizard Warrior/LizardWarrior_Controller.controller",
            texture    = LIZ+"/Lizard Warrior/Textures/T_LizardWarrior_BaseColor.png",
            scale = 0.8f },

        new Def { key="dragonide",
            fbx        = LIZ+"/Dragonide/FBX Files/SK_Dragonide.FBX",
            controller = LIZ+"/Dragonide/Dragonide_Controller.controller",
            texture    = LIZ+"/Dragonide/Textures/T_DragonideBody_BaseColor.png",
            scale = 0.9f },

        new Def { key="wyvern",
            fbx        = LIZ+"/Wyvern/FBX Files/SK_Wyvern.FBX",
            controller = LIZ+"/Wyvern/Wyvern_Controller.controller",
            texture    = LIZ+"/Wyvern/Textures/T_Wyvern_Albedo.png",
            scale = 1.0f },

        new Def { key="hydra",
            fbx        = LIZ+"/Hydra/FBX Files/SK_Hydra.FBX",
            controller = LIZ+"/Hydra/Hydra_Controller.controller",
            texture    = LIZ+"/Hydra/Textures/T_Hydra_BaseColor.png",
            scale = 1.4f },

        new Def { key="mountain_dragon",
            fbx        = LIZ+"/Mountain Dragon/FBX Files/SK_MountainDragon.FBX",
            controller = LIZ+"/Mountain Dragon/MountainDragon_Controller.controller",
            texture    = LIZ+"/Mountain Dragon/Textures/T_MountainDragon_BaseColor.png",
            scale = 1.3f },

        // ── Mythological ──────────────────────────────────────────────────────
        new Def { key="werewolf",
            fbx        = MYTH+"/Werewolf/FBX Files/SK_Werewolf.FBX",
            controller = MYTH+"/Werewolf/Werewolf_Controller.controller",
            texture    = MYTH+"/Werewolf/Textures/T_Werewolf_BaseColor.png",
            scale = 0.9f },

        new Def { key="harpy",
            fbx        = MYTH+"/Harpy/FBX Files/SK_Harpy.FBX",
            controller = MYTH+"/Harpy/Harpy_Controller.controller",
            texture    = MYTH+"/Harpy/Textures/T_Harpy_BaseColor.png",
            scale = 0.8f },

        new Def { key="griffin",
            // Use 2-sided feathers variant for better visual quality
            fbx        = MYTH+"/Griffin/FBX files/SK_Griffin2sidedFeathers.FBX",
            controller = MYTH+"/Griffin/Griffin_Controller.controller",
            texture    = MYTH+"/Griffin/Textures/T_Griffin_BaseColor.png",
            scale = 1.1f },

        new Def { key="manticora",
            fbx        = MYTH+"/Manticora/FBX Files/SK_Manticora.FBX",
            controller = MYTH+"/Manticora/Manticora_Controller.controller",
            texture    = MYTH+"/Manticora/Textures/T_Manticora_BaseColor.png",
            scale = 1.1f },

        new Def { key="chimera",
            fbx        = MYTH+"/Chimera/FBX Files/SK_Chimera.FBX",
            controller = MYTH+"/Chimera/Chimera_Controller.controller",
            texture    = MYTH+"/Chimera/Textures/T_Chimera_BaseColor.png",
            scale = 1.2f },

        // ── Demonic ───────────────────────────────────────────────────────────
        new Def { key="evil_watcher",
            fbx        = DEM+"/Evil Watcher/FBX Files/SK_EvilWatcher.fbx",
            controller = DEM+"/Evil Watcher/EvilWatcher_Controller.controller",
            texture    = DEM+"/Evil Watcher/Textures/T_EvilWatcher_BaseColor.png",
            scale = 0.8f },

        new Def { key="oak_tree_ent",
            fbx        = DEM+"/Oak Tree Ent/FBX Files/SK_OakTreeEnt.FBX",
            controller = DEM+"/Oak Tree Ent/OakTreeEnt_Controller.controller",
            texture    = DEM+"/Oak Tree Ent/Textures/T_OakTreeEnt_BaseColor.png",
            scale = 1.3f },

        new Def { key="ice_golem",
            fbx        = DEM+"/Golem/FBX Files/Skinned Golem/SK_Golem.FBX",
            controller = DEM+"/Golem/GolemSkinned_Controller.controller",
            texture    = DEM+"/Golem/Textures/T_GolemIce_BaseColor.png",
            scale = 1.2f },

        new Def { key="demon_lord",
            fbx        = DEM+"/Demon Lord/FBX Files/SK_DemonLord.FBX",
            controller = DEM+"/Demon Lord/DemonLord_Controller.controller",
            texture    = DEM+"/Demon Lord/Textures/T_DemonLordBody_BaseColor.png",
            scale = 1.4f },
    };

    // ── Entry point ───────────────────────────────────────────────────────────
    [MenuItem("Castle Defender/Setup/Create HFC Unit Prefabs (30)")]
    public static void Run()
    {
        var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(REGISTRY_PATH);
        if (registry == null)
        {
            Debug.LogError($"[CreateHFCUnitPrefabs] Registry not found at {REGISTRY_PATH}");
            return;
        }

        EnsureDir(OUT_PREFABS);
        EnsureDir(OUT_MATS);

        int created = 0, skipped = 0, failed = 0;

        foreach (var d in Defs)
        {
            string prefabPath = $"{OUT_PREFABS}/Unit_{d.key}.prefab";

            // Skip if prefab already exists
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
            {
                // Still update registry entry in case it's missing
                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                UpdateRegistry(registry, d.key, existingPrefab, d.scale);
                skipped++;
                continue;
            }

            // Load FBX
            var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(d.fbx);
            if (fbxAsset == null)
            {
                Debug.LogError($"[CreateHFCUnitPrefabs] FBX not found: {d.fbx}");
                failed++;
                continue;
            }

            // Load AnimatorController
            var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(d.controller);
            if (ctrl == null)
                Debug.LogWarning($"[CreateHFCUnitPrefabs] Controller not found: {d.controller}  (unit will have no animations)");

            // Create or load URP material
            var mat = LoadOrCreateMaterial(d.key, d.texture);

            // Instantiate FBX as scene object so we can modify it
            var go = Object.Instantiate(fbxAsset);
            go.name = PrettyName(d.key);

            // Ensure Animator on root; set controller
            var anim = go.GetComponent<Animator>();
            if (anim == null) anim = go.AddComponent<Animator>();
            if (ctrl != null) anim.runtimeAnimatorController = ctrl;

            // Assign URP material to every SkinnedMeshRenderer
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = new Material[smr.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
            }

            // Save as new prefab
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);

            if (prefab == null)
            {
                Debug.LogError($"[CreateHFCUnitPrefabs] Failed to save prefab for {d.key}");
                failed++;
                continue;
            }

            UpdateRegistry(registry, d.key, prefab, d.scale);
            Debug.Log($"[CreateHFCUnitPrefabs] ✓ {d.key}  →  {prefabPath}");
            created++;
        }

        EditorUtility.SetDirty(registry);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[CreateHFCUnitPrefabs] Done — created: {created}  skipped: {skipped}  failed: {failed}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Creates a new URP Lit material from the body BaseColor texture, or loads
    /// the existing one if already created by a previous run.
    static Material LoadOrCreateMaterial(string key, string texPath)
    {
        string matPath = $"{OUT_MATS}/M_{key}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("[CreateHFCUnitPrefabs] URP Lit shader not found — falling back to Standard.");
            shader = Shader.Find("Standard");
        }

        var mat = new Material(shader) { name = $"M_{key}" };

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex != null)
        {
            // URP Lit uses _BaseMap for albedo; Standard uses _MainTex
            if (mat.HasProperty("_BaseMap"))  mat.SetTexture("_BaseMap",  tex);
            if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex",  tex);
            mat.SetColor("_BaseColor", Color.white);
        }
        else
        {
            Debug.LogWarning($"[CreateHFCUnitPrefabs] Texture not found: {texPath}");
        }

        AssetDatabase.CreateAsset(mat, matPath);
        return mat;
    }

    /// Adds or updates a UnitPrefabRegistry entry via SerializedObject so existing
    /// entries (original 5 units) are never disturbed.
    static void UpdateRegistry(UnitPrefabRegistry registry, string key,
                                GameObject prefab, float scale)
    {
        var so = new SerializedObject(registry);
        var arr = so.FindProperty("entries");

        // Look for existing entry with this key
        for (int i = 0; i < arr.arraySize; i++)
        {
            var elem = arr.GetArrayElementAtIndex(i);
            if (elem.FindPropertyRelative("key").stringValue == key)
            {
                elem.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                elem.FindPropertyRelative("scale").floatValue = scale;
                so.ApplyModifiedProperties();
                return;
            }
        }

        // Append new entry
        arr.InsertArrayElementAtIndex(arr.arraySize);
        var e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
        e.FindPropertyRelative("key").stringValue                = key;
        e.FindPropertyRelative("prefab").objectReferenceValue    = prefab;
        e.FindPropertyRelative("scale").floatValue               = scale;
        e.FindPropertyRelative("tintMine").colorValue            = new Color(0.20f, 0.80f, 0.70f);
        e.FindPropertyRelative("tintEnemy").colorValue           = new Color(0.90f, 0.25f, 0.25f);
        so.ApplyModifiedProperties();
    }

    /// Creates the physical directory and refreshes AssetDatabase so Unity
    /// recognises the new folder.
    static void EnsureDir(string assetPath)
    {
        string full = Path.Combine(
            Application.dataPath,
            assetPath.Substring("Assets/".Length));
        if (!Directory.Exists(full))
        {
            Directory.CreateDirectory(full);
            AssetDatabase.Refresh();
        }
    }

    /// "skeleton_knight" → "SkeletonKnight"
    static string PrettyName(string key)
    {
        var sb = new StringBuilder();
        foreach (var part in key.Split('_'))
        {
            if (part.Length == 0) continue;
            sb.Append(char.ToUpper(part[0]));
            sb.Append(part.Substring(1));
        }
        return sb.ToString();
    }
}
