// FixCreaturePrefabs.cs — Adds Animator + Controller + Avatar to each Heroic Fantasy
// creature prefab. The _PBR prefabs ship as skinned mesh hierarchies with no Animator.
// This script adds the missing Animator component to each prefab's root GO,
// assigns the correct controller and avatar, then saves the prefab.
//
// Run via: Castle Defender / Setup / Fix Creature Prefabs (Add Animators)

using UnityEngine;
using UnityEditor;

namespace CastleDefender.Editor
{
    public static class FixCreaturePrefabs
    {
        const string HF = "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1";
        const string MV = HF + "/Must Have Fantasy Villains Pack";
        const string LD = HF + "/Living Dead Pack";
        const string FA = HF + "/Fantasy Animals Pack";
        const string FL = HF + "/Fantasy Lizards Pack";
        const string MY = HF + "/Mythological Creatures Pack";
        const string DC = HF + "/Demonic Creatures Pack";

        // (prefabPath, controllerPath, anyAnimFbxForAvatar)
        static readonly (string prefab, string ctrl, string fbxFolder)[] Creatures = {
            // Must Have Fantasy Villains
            (MV+"/Goblin/Prefabs/Goblin_PBR.prefab",
             MV+"/Goblin/Goblin_Controller.controller",
             MV+"/Goblin/FBX Files"),
            (MV+"/Kobold/Prefabs/Kobold_PBR.prefab",
             MV+"/Kobold/Kobold_Controller.controller",
             MV+"/Kobold/FBX Files"),
            (MV+"/Hobgoblin/Prefabs/Hobgoblin_PBR.prefab",
             MV+"/Hobgoblin/Hobgoblin_Controller.controller",
             MV+"/Hobgoblin/FBX Files"),
            (MV+"/Orc/Prefabs/Orc_PBR.prefab",
             MV+"/Orc/Orc_Controller.controller",
             MV+"/Orc/FBX Files"),
            (MV+"/Ogre/Prefabs/FatOgre_PBR.prefab",
             MV+"/Ogre/FatOgre_Controller.controller",
             MV+"/Ogre/FBX Files"),
            (MV+"/Troll/Prefabs/Troll_PBR.prefab",
             MV+"/Troll/Troll_Controller.controller",
             MV+"/Troll/FBX Files"),
            (MV+"/Cyclops/Prefabs/Cyclops_PBR.prefab",
             MV+"/Cyclops/Cyclops_Controller.controller",
             MV+"/Cyclops/FBX Files"),
            // Living Dead
            (LD+"/Ghoul/Prefabs/Ghoul_PBR.prefab",
             LD+"/Ghoul/Ghoul_Controller.controller",
             LD+"/Ghoul/FBX Files"),
            (LD+"/Skeleton Knight/Prefabs/SkeletonKnight_PBR.prefab",
             LD+"/Skeleton Knight/SkeletonKnight_Controller.controller",
             LD+"/Skeleton Knight/FBX Files"),
            (LD+"/Undead/Prefabs/Undead_PBR.prefab",
             LD+"/Undead/Undead_Controller.controller",
             LD+"/Undead/FBX Files"),
            (LD+"/Mummy/Prefabs/Mummy_PBR.prefab",
             LD+"/Mummy/Mummy_Controller.controller",
             LD+"/Mummy/FBX Files"),
            (LD+"/Vampire/Prefabs/Vampire_PBR.prefab",
             LD+"/Vampire/Vampire_Controller.controller",
             LD+"/Vampire/FBX Files"),
            // Fantasy Animals
            (FA+"/Giant Rat/Prefabs/GiantRat_PBR.prefab",
             FA+"/Giant Rat/GiantRat_Controller.controller",
             FA+"/Giant Rat/FBX Files"),
            (FA+"/Fantasy Wolf/Prefabs/M_FantasyWolf_PBR.prefab",
             FA+"/Fantasy Wolf/M_FantasyWolf_Controller.controller",
             FA+"/Fantasy Wolf/FBX Files"),
            (FA+"/Giant Viper/Prefabs/GiantViper_PBR.prefab",
             FA+"/Giant Viper/GiantViper_Controller.controller",
             FA+"/Giant Viper/FBX Files"),
            (FA+"/Darkness Spider/Prefabs/DarknessSpider_PBR.prefab",
             FA+"/Darkness Spider/DarknessSpider_Controller.controller",
             FA+"/Darkness Spider/FBX Files"),
            // Fantasy Lizards
            (FL+"/Lizard Warrior/Prefabs/LizardWarrior_PBR.prefab",
             FL+"/Lizard Warrior/LizardWarrior_Controller.controller",
             FL+"/Lizard Warrior/FBX Files"),
            (FL+"/Dragonide/Prefabs/Dragonide_PBR.prefab",
             FL+"/Dragonide/Dragonide_Controller.controller",
             FL+"/Dragonide/FBX Files"),
            (FL+"/Wyvern/Prefabs/Wyvern_PBR.prefab",
             FL+"/Wyvern/Wyvern_Controller.controller",
             FL+"/Wyvern/FBX Files"),
            (FL+"/Hydra/Prefabs/Hydra_PBR.prefab",
             FL+"/Hydra/Hydra_Controller.controller",
             FL+"/Hydra/FBX Files"),
            (FL+"/Mountain Dragon/Prefabs/MountainDragon_PBR.prefab",
             FL+"/Mountain Dragon/MountainDragon_Controller.controller",
             FL+"/Mountain Dragon/FBX Files"),
            // Mythological
            (MY+"/Werewolf/Prefabs/Werewolf_PBR.prefab",
             MY+"/Werewolf/Werewolf_Controller.controller",
             MY+"/Werewolf/FBX Files"),
            (MY+"/Harpy/Prefabs/HarpyBreastsCovered_PBR.prefab",
             MY+"/Harpy/Harpy_Controller.controller",
             MY+"/Harpy/FBX Files"),
            (MY+"/Griffin/Prefabs/Griffin1SidedFeathers_PBR.prefab",
             MY+"/Griffin/Griffin_Controller.controller",
             MY+"/Griffin/FBX Files"),
            (MY+"/Manticora/Prefabs/Manticora_PBR.prefab",
             MY+"/Manticora/Manticora_Controller.controller",
             MY+"/Manticora/FBX Files"),
            (MY+"/Chimera/Prefabs/Chimera_PBR.prefab",
             MY+"/Chimera/Chimera_Controller.controller",
             MY+"/Chimera/FBX Files"),
            // Demonic
            (DC+"/Evil Watcher/Prefabs/EvilWatcher.prefab",
             DC+"/Evil Watcher/EvilWatcher_Controller.controller",
             DC+"/Evil Watcher/FBX Files"),
            (DC+"/Oak Tree Ent/Prefabs/OakTreeEnt_PBR.prefab",
             DC+"/Oak Tree Ent/OakTreeEnt_Controller.controller",
             DC+"/Oak Tree Ent/FBX Files"),
            (DC+"/Golem/Prefabs/GolemIce_PBR.prefab",
             DC+"/Golem/Golem_Controller.controller",
             DC+"/Golem/FBX Files"),
            (DC+"/Demon Lord/Prefabs/DemonLord_PBR.prefab",
             DC+"/Demon Lord/DemonLord_Controller.controller",
             DC+"/Demon Lord/FBX Files"),
        };

        [MenuItem("Castle Defender/Setup/Fix Creature Prefabs (Add Animators)")]
        public static void Run()
        {
            int fixed_ = 0, skipped = 0, failed = 0;

            foreach (var (prefabPath, ctrlPath, fbxFolder) in Creatures)
            {
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset == null)
                {
                    Debug.LogWarning($"[FixCreaturePrefabs] Prefab not found: {prefabPath}");
                    failed++;
                    continue;
                }

                // Skip if Animator already present
                if (prefabAsset.GetComponentInChildren<Animator>() != null)
                {
                    Debug.Log($"[FixCreaturePrefabs] Animator already present — skipping: {prefabAsset.name}");
                    skipped++;
                    continue;
                }

                var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ctrlPath);
                if (ctrl == null)
                {
                    // Controller name might differ slightly — search nearby
                    ctrl = FindController(fbxFolder.Replace("/FBX Files", ""), prefabAsset.name);
                }
                if (ctrl == null)
                {
                    Debug.LogWarning($"[FixCreaturePrefabs] Controller not found at: {ctrlPath}");
                    failed++;
                    continue;
                }

                // Get avatar from the first animation FBX in the folder
                var avatar = FindAvatarInFolder(fbxFolder);

                // Edit the prefab
                using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                {
                    var root = scope.prefabContentsRoot;
                    var anim = root.AddComponent<Animator>();
                    anim.runtimeAnimatorController = ctrl;
                    if (avatar != null)
                        anim.avatar = avatar;
                    anim.applyRootMotion    = false;   // we drive position ourselves
                    anim.updateMode         = AnimatorUpdateMode.Normal;
                    anim.cullingMode        = AnimatorCullingMode.AlwaysAnimate;
                }

                Debug.Log($"[FixCreaturePrefabs] ✓ {prefabAsset.name} — ctrl={ctrl.name}, avatar={(avatar != null ? avatar.name : "none")}");
                fixed_++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[FixCreaturePrefabs] DONE — fixed={fixed_}, skipped={skipped}, failed={failed}");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        static RuntimeAnimatorController FindController(string folder, string prefabName)
        {
            var guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { folder });
            foreach (var g in guids)
            {
                var path  = AssetDatabase.GUIDToAssetPath(g);
                var asset = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                if (asset != null) return asset;
            }
            return null;
        }

        static Avatar FindAvatarInFolder(string fbxFolder)
        {
            var guids = AssetDatabase.FindAssets("t:Model", new[] { fbxFolder });
            foreach (var g in guids)
            {
                var path   = AssetDatabase.GUIDToAssetPath(g);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var a in assets)
                    if (a is Avatar av && av != null)
                        return av;
            }
            return null;
        }
    }
}
