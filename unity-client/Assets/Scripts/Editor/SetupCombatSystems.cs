// SetupCombatSystems.cs — Complete one-shot wiring for Game_ML combat visuals.
// Run via: Castle Defender / Setup / Setup Combat Systems
//
// What it does:
//   1.  Ensures Assets/Prefabs/FX and Assets/Prefabs/UI folders exist
//   2.  Creates Projectile.prefab + ProjectileCannon.prefab (sphere stand-ins)
//   3.  Creates HitEffect.prefab (root HitEffect + SparkPS + ImpactPS children)
//   4.  Creates CannonSplash.prefab (root CannonSplash + ShockwavePS + DebrisPS + DustPS)
//   5.  Creates GoldPop.prefab (UI RectTransform + CoinIcon + AmountText)
//   6.  Creates FloatingText.prefab (WorldSpace Canvas root + FloatingText + TMP)
//   7.  Creates HpBar.prefab (3D bar: Background cube + Fill cube child)
//   8.  Finds/creates GameManager GO and assigns all FX prefabs
//   9.  Adds ProjectileSystem to LaneRenderer GO and wires prefabs
//   10. Wires HpBarPrefab on LaneRenderer
//   11. Ensures TileGrids has 4 Lane children (Lane_0..3) with TileGrid + all refs
//   12. Saves the scene

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using CastleDefender.Game;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class SetupCombatSystems
    {
        [MenuItem("Castle Defender/Setup/Setup Combat Systems")]
        public static void Run()
        {
            // ── 0. Must be in Game_ML ─────────────────────────────────────────
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (scene.name != "Game_ML")
            {
                Debug.LogError("[SetupCombatSystems] Please open Game_ML scene first.");
                return;
            }

            // ── 1. Ensure directories ─────────────────────────────────────────
            EnsureDir("Assets/Prefabs");
            EnsureDir("Assets/Prefabs/FX");
            EnsureDir("Assets/Prefabs/UI");

            // ── 2. Projectile sphere prefabs ──────────────────────────────────
            var projPrefab   = EnsureSpherePrefab("Assets/Prefabs/FX/Projectile.prefab",
                                                   "Projectile", 0.2f, new Color(1f, 0.9f, 0.3f));
            var cannonPrefab = EnsureSpherePrefab("Assets/Prefabs/FX/ProjectileCannon.prefab",
                                                   "ProjectileCannon", 0.4f, new Color(0.3f, 0.3f, 0.35f));

            // ── 3. FX prefabs ─────────────────────────────────────────────────
            var hitEffectPrefab    = EnsureHitEffectPrefab();
            var cannonSplashPrefab = EnsureCannonSplashPrefab();
            var goldPopPrefab      = EnsureGoldPopPrefab();
            var floatingTextPrefab = EnsureFloatingTextPrefab();
            var hpBarPrefab        = EnsureHpBarPrefab();

            // ── 4. GameManager GO ─────────────────────────────────────────────
            WireGameManager(hitEffectPrefab, cannonSplashPrefab, goldPopPrefab, floatingTextPrefab);

            // ── 5. LaneRenderer GO ────────────────────────────────────────────
            var laneRendererGO = GameObject.Find("LaneRenderer");
            if (laneRendererGO == null)
            {
                Debug.LogError("[SetupCombatSystems] LaneRenderer GO not found — skipping ProjectileSystem + LaneRenderer wiring.");
            }
            else
            {
                // ProjectileSystem
                var projSys = laneRendererGO.GetComponent<ProjectileSystem>();
                if (projSys == null)
                    projSys = laneRendererGO.AddComponent<ProjectileSystem>();
                projSys.ProjectilePrefab = projPrefab;
                projSys.CannonPrefab     = cannonPrefab;
                projSys.ArcHeight        = 1.5f;
                Debug.Log("[SetupCombatSystems] ProjectileSystem wired on LaneRenderer.");

                // HpBarPrefab on LaneRenderer
                var lr = laneRendererGO.GetComponent<LaneRenderer>();
                if (lr != null && hpBarPrefab != null)
                {
                    lr.HpBarPrefab = hpBarPrefab;
                    Debug.Log("[SetupCombatSystems] HpBarPrefab assigned to LaneRenderer.");
                }
            }

            // ── 6. UnitPrefabRegistry ─────────────────────────────────────────
            var registry = EnsureUnitPrefabRegistry();

            // ── 7. TileGrid children ──────────────────────────────────────────
            var tileGridsGO = GameObject.Find("TileGrids");
            if (tileGridsGO == null)
            {
                Debug.LogError("[SetupCombatSystems] TileGrids GO not found — skipping TileGrid wiring.");
            }
            else
            {
                var floorPrefab  = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/FloorTile.prefab");
                var wallPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/WallTile.prefab");
                var castlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tiles/CastleTile.prefab");
                var mainCam      = Camera.main;
                var tileMenuUI   = Object.FindFirstObjectByType<TileMenuUI>(FindObjectsInactive.Include);

                if (floorPrefab == null) Debug.LogWarning("[SetupCombatSystems] FloorTile prefab not found — floors won't render.");

                string[] laneNames = { "Lane_0", "Lane_1", "Lane_2", "Lane_3" };
                for (int i = 0; i < 4; i++)
                {
                    var child  = tileGridsGO.transform.Find(laneNames[i]);
                    var laneGO = child != null ? child.gameObject : new GameObject(laneNames[i]);
                    if (child == null) laneGO.transform.SetParent(tileGridsGO.transform, false);

                    var tg = laneGO.GetComponent<TileGrid>();
                    if (tg == null) tg = laneGO.AddComponent<TileGrid>();

                    tg.LaneIndex         = i;
                    tg.IsInteractive     = true;
                    tg.Cols              = 11;
                    tg.Rows              = 28;
                    tg.FloorPrefab       = floorPrefab;
                    tg.WallPrefab        = wallPrefab;
                    tg.CastlePrefab      = castlePrefab;
                    tg.HpBarPrefab       = hpBarPrefab;
                    tg.Registry          = registry;
                    tg.Cam               = mainCam;
                    tg.TileMenuBehaviour          = (i == 0) ? tileMenuUI : null;
                    tg.TowerSpawnYOffset = 0.54f;

                    Debug.Log($"[SetupCombatSystems] TileGrid lane {i} ready on {laneGO.name}.");
                }
            }

            // ── 7. Save ───────────────────────────────────────────────────────
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[SetupCombatSystems] DONE — Game_ML saved.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // GameManager
        // ─────────────────────────────────────────────────────────────────────

        static void WireGameManager(HitEffect hitFx, CannonSplash cannonFx, GoldPop goldFx, FloatingText floatFx)
        {
            var gmGO = GameObject.Find("GameManager");
            if (gmGO == null)
            {
                gmGO = new GameObject("GameManager");
                Debug.Log("[SetupCombatSystems] Created GameManager GO.");
            }

            var gm = gmGO.GetComponent<GameManager>();
            if (gm == null) gm = gmGO.AddComponent<GameManager>();

            if (hitFx    != null) gm.HitEffectPrefab    = hitFx;
            if (cannonFx != null) gm.CannonSplashPrefab = cannonFx;
            if (goldFx   != null) gm.GoldPopPrefab      = goldFx;
            if (floatFx  != null) gm.FloatingTextPrefab = floatFx;

            Debug.Log("[SetupCombatSystems] GameManager GO wired with FX prefabs.");
        }

        // ─────────────────────────────────────────────────────────────────────
        // UnitPrefabRegistry
        // ─────────────────────────────────────────────────────────────────────

        static UnitPrefabRegistry EnsureUnitPrefabRegistry()
        {
            const string assetPath = "Assets/Registry/UnitPrefabRegistry.asset";
            EnsureDir("Assets/Registry");

            var existing = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(assetPath);
            if (existing != null)
            {
                Debug.Log("[SetupCombatSystems] UnitPrefabRegistry already exists — skipping.");
                return existing;
            }

            const string HF = "Assets/HEROIC FANTASY CREATURES FULL PACK VOL 1";
            const string MV = HF + "/Must Have Fantasy Villains Pack";
            const string LD = HF + "/Living Dead Pack";
            const string FA = HF + "/Fantasy Animals Pack";
            const string FL = HF + "/Fantasy Lizards Pack";
            const string MY = HF + "/Mythological Creatures Pack";
            const string DC = HF + "/Demonic Creatures Pack";

            // (key, prefabPath, scale)
            var defs = new (string key, string path, float scale)[]
            {
                // Must Have Fantasy Villains
                ("goblin",         MV + "/Goblin/Prefabs/Goblin_PBR.prefab",             0.65f),
                ("kobold",         MV + "/Kobold/Prefabs/Kobold_PBR.prefab",              0.60f),
                ("hobgoblin",      MV + "/Hobgoblin/Prefabs/Hobgoblin_PBR.prefab",        0.80f),
                ("orc",            MV + "/Orc/Prefabs/Orc_PBR.prefab",                   0.85f),
                ("ogre",           MV + "/Ogre/Prefabs/FatOgre_PBR.prefab",              1.10f),
                ("troll",          MV + "/Troll/Prefabs/Troll_PBR.prefab",               1.00f),
                ("cyclops",        MV + "/Cyclops/Prefabs/Cyclops_PBR.prefab",           1.20f),
                // Living Dead
                ("ghoul",          LD + "/Ghoul/Prefabs/Ghoul_PBR.prefab",               0.80f),
                ("skeleton_knight",LD + "/Skeleton Knight/Prefabs/SkeletonKnight_PBR.prefab", 0.90f),
                ("undead_warrior", LD + "/Undead/Prefabs/Undead_PBR.prefab",             0.85f),
                ("mummy",          LD + "/Mummy/Prefabs/Mummy_PBR.prefab",               0.90f),
                ("vampire",        LD + "/Vampire/Prefabs/Vampire_PBR.prefab",           0.85f),
                // Fantasy Animals
                ("giant_rat",      FA + "/Giant Rat/Prefabs/GiantRat_PBR.prefab",        0.55f),
                ("fantasy_wolf",   FA + "/Fantasy Wolf/Prefabs/M_FantasyWolf_PBR.prefab",0.80f),
                ("giant_viper",    FA + "/Giant Viper/Prefabs/GiantViper_PBR.prefab",    0.90f),
                ("darkness_spider",FA + "/Darkness Spider/Prefabs/DarknessSpider_PBR.prefab", 0.85f),
                // Fantasy Lizards
                ("lizard_warrior", FL + "/Lizard Warrior/Prefabs/LizardWarrior_PBR.prefab", 0.85f),
                ("dragonide",      FL + "/Dragonide/Prefabs/Dragonide_PBR.prefab",       0.95f),
                ("wyvern",         FL + "/Wyvern/Prefabs/Wyvern_PBR.prefab",             1.05f),
                ("hydra",          FL + "/Hydra/Prefabs/Hydra_PBR.prefab",               1.40f),
                ("mountain_dragon",FL + "/Mountain Dragon/Prefabs/MountainDragon_PBR.prefab", 1.30f),
                // Mythological
                ("werewolf",       MY + "/Werewolf/Prefabs/Werewolf_PBR.prefab",         1.00f),
                ("harpy",          MY + "/Harpy/Prefabs/HarpyBreastsCovered_PBR.prefab", 0.85f),
                ("griffin",        MY + "/Griffin/Prefabs/Griffin1SidedFeathers_PBR.prefab", 1.10f),
                ("manticora",      MY + "/Manticora/Prefabs/Manticora_PBR.prefab",       1.20f),
                ("chimera",        MY + "/Chimera/Prefabs/Chimera_PBR.prefab",           1.30f),
                // Demonic
                ("evil_watcher",   DC + "/Evil Watcher/Prefabs/EvilWatcher.prefab",      0.75f),
                ("oak_tree_ent",   DC + "/Oak Tree Ent/Prefabs/OakTreeEnt_PBR.prefab",   1.40f),
                ("ice_golem",      DC + "/Golem/Prefabs/GolemIce_PBR.prefab",            1.10f),
                ("demon_lord",     DC + "/Demon Lord/Prefabs/DemonLord_PBR.prefab",      1.30f),
            };

            var registry = ScriptableObject.CreateInstance<UnitPrefabRegistry>();
            var entries  = new UnitPrefabRegistry.Entry[defs.Length];
            int missing  = 0;

            for (int i = 0; i < defs.Length; i++)
            {
                var (key, path, scale) = defs[i];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"[SetupCombatSystems] Prefab not found for '{key}': {path}");
                    missing++;
                }
                entries[i] = new UnitPrefabRegistry.Entry
                {
                    key       = key,
                    prefab    = prefab,
                    scale     = scale,
                    tintMine  = new Color(0.20f, 0.80f, 0.70f),
                    tintEnemy = new Color(0.90f, 0.25f, 0.25f),
                };
            }

            registry.entries = entries;
            AssetDatabase.CreateAsset(registry, assetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SetupCombatSystems] Created UnitPrefabRegistry with {defs.Length - missing}/{defs.Length} prefabs resolved.");
            return registry;
        }

        // ─────────────────────────────────────────────────────────────────────
        // FX Prefab builders
        // ─────────────────────────────────────────────────────────────────────

        static HitEffect EnsureHitEffectPrefab()
        {
            const string path = "Assets/Prefabs/FX/HitEffect.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.Log("[SetupCombatSystems] HitEffect.prefab already exists — skipping.");
                return existing.GetComponent<HitEffect>();
            }

            var root     = new GameObject("HitEffect");
            var hitEffect = root.AddComponent<HitEffect>();

            // SparkPS child — burst of sparks, colored per tower type at runtime
            var sparkGO = new GameObject("SparkPS");
            sparkGO.transform.SetParent(root.transform, false);
            var sparkPS = sparkGO.AddComponent<ParticleSystem>();
            {
                var main       = sparkPS.main;
                main.duration      = 0.25f;
                main.loop          = false;
                main.startLifetime = 0.35f;
                main.startSpeed    = 4f;
                main.startSize     = 0.10f;
                main.startColor    = new Color(1f, 0.85f, 0.2f);
                main.maxParticles  = 24;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emit = sparkPS.emission;
                emit.rateOverTime = 0f;
                emit.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });

                var shape     = sparkPS.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius    = 0.08f;
            }
            hitEffect.sparkPS = sparkPS;

            // ImpactPS child — ground ring flash
            var impactGO = new GameObject("ImpactPS");
            impactGO.transform.SetParent(root.transform, false);
            var impactPS = impactGO.AddComponent<ParticleSystem>();
            {
                var main       = impactPS.main;
                main.duration      = 0.25f;
                main.loop          = false;
                main.startLifetime = 0.20f;
                main.startSpeed    = 1.5f;
                main.startSize     = 0.35f;
                main.startColor    = new Color(1f, 1f, 0.9f, 0.85f);
                main.maxParticles  = 6;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emit = impactPS.emission;
                emit.rateOverTime = 0f;
                emit.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });

                var shape     = impactPS.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius    = 0.25f;
            }
            hitEffect.impactPS = impactPS;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[SetupCombatSystems] Created HitEffect.prefab");
            return prefab.GetComponent<HitEffect>();
        }

        static CannonSplash EnsureCannonSplashPrefab()
        {
            const string path = "Assets/Prefabs/FX/CannonSplash.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.Log("[SetupCombatSystems] CannonSplash.prefab already exists — skipping.");
                return existing.GetComponent<CannonSplash>();
            }

            var root   = new GameObject("CannonSplash");
            var splash = root.AddComponent<CannonSplash>();

            // ShockwavePS — flat ring expanding outward
            var shockGO = new GameObject("ShockwavePS");
            shockGO.transform.SetParent(root.transform, false);
            var shockPS = shockGO.AddComponent<ParticleSystem>();
            {
                var main     = shockPS.main;
                main.duration      = 0.5f;
                main.loop          = false;
                main.startLifetime = 0.4f;
                main.startSpeed    = 3f;
                main.startSize     = 0.5f;
                main.startColor    = new Color(1f, 0.55f, 0.1f, 0.9f);
                main.maxParticles  = 20;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emit = shockPS.emission;
                emit.rateOverTime = 0f;
                emit.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });

                var shape     = shockPS.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius    = 0.4f;
            }
            splash.shockwavePS = shockPS;

            // DebrisPS — chunks flung upward with gravity
            var debrisGO = new GameObject("DebrisPS");
            debrisGO.transform.SetParent(root.transform, false);
            var debrisPS = debrisGO.AddComponent<ParticleSystem>();
            {
                var main     = debrisPS.main;
                main.duration        = 0.8f;
                main.loop            = false;
                main.startLifetime   = 0.9f;
                main.startSpeed      = 5f;
                main.startSize       = 0.18f;
                main.gravityModifier = 2f;
                main.startColor      = new Color(0.45f, 0.38f, 0.28f);
                main.maxParticles    = 12;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emit = debrisPS.emission;
                emit.rateOverTime = 0f;
                emit.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });

                var shape     = debrisPS.shape;
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius    = 0.25f;
            }
            splash.debrisPS = debrisPS;

            // DustPS — slow expanding smoke cloud
            var dustGO = new GameObject("DustPS");
            dustGO.transform.SetParent(root.transform, false);
            var dustPS = dustGO.AddComponent<ParticleSystem>();
            {
                var main     = dustPS.main;
                main.duration      = 1.0f;
                main.loop          = false;
                main.startLifetime = 1.1f;
                main.startSpeed    = 0.6f;
                main.startSize     = 0.8f;
                main.startColor    = new Color(0.70f, 0.63f, 0.52f, 0.55f);
                main.maxParticles  = 8;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emit = dustPS.emission;
                emit.rateOverTime = 0f;
                emit.SetBursts(new[] { new ParticleSystem.Burst(0f, 6) });

                var shape     = dustPS.shape;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius    = 0.5f;
            }
            splash.dustPS = dustPS;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[SetupCombatSystems] Created CannonSplash.prefab");
            return prefab.GetComponent<CannonSplash>();
        }

        static GoldPop EnsureGoldPopPrefab()
        {
            const string path = "Assets/Prefabs/FX/GoldPop.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.Log("[SetupCombatSystems] GoldPop.prefab already exists — skipping.");
                return existing.GetComponent<GoldPop>();
            }

            // Root: RectTransform-based UI element (gets parented to a canvas panel at runtime)
            var root    = new GameObject("GoldPop");
            var rootRT  = root.AddComponent<RectTransform>();
            rootRT.sizeDelta = new Vector2(120f, 36f);
            var goldPop = root.AddComponent<GoldPop>();
            root.AddComponent<CanvasGroup>();

            // CoinIcon child: small golden square (Image)
            var coinGO  = new GameObject("CoinIcon");
            coinGO.transform.SetParent(root.transform, false);
            var coinRT  = coinGO.AddComponent<RectTransform>();
            coinRT.sizeDelta        = new Vector2(22f, 22f);
            coinRT.anchoredPosition = new Vector2(-42f, 0f);
            var coinImg = coinGO.AddComponent<UnityEngine.UI.Image>();
            coinImg.color = new Color(1f, 0.82f, 0.08f);
            goldPop.coinIcon = coinRT;

            // AmountText child: TMP label
            var txtGO = new GameObject("AmountText");
            txtGO.transform.SetParent(root.transform, false);
            var txtRT = txtGO.AddComponent<RectTransform>();
            txtRT.sizeDelta        = new Vector2(96f, 36f);
            txtRT.anchoredPosition = new Vector2(12f, 0f);
            var tmp   = txtGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = "+10g";
            tmp.fontSize  = 18;
            tmp.color     = new Color(1f, 0.85f, 0.1f);
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontStyle = FontStyles.Bold;
            goldPop.amountText = tmp;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[SetupCombatSystems] Created GoldPop.prefab");
            return prefab.GetComponent<GoldPop>();
        }

        static FloatingText EnsureFloatingTextPrefab()
        {
            const string path = "Assets/Prefabs/FX/FloatingText.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.Log("[SetupCombatSystems] FloatingText.prefab already exists — skipping.");
                return existing.GetComponent<FloatingText>();
            }

            // Root: WorldSpace Canvas so TMP renders in 3D.
            // FloatingText + TextMeshProUGUI must be on the same GO (RequireComponent).
            // Scale 0.01 → 100 canvas units = 1 world unit (text stays readable).
            var root   = new GameObject("FloatingText");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRT = root.GetComponent<RectTransform>();
            canvasRT.sizeDelta = new Vector2(200f, 60f);
            root.transform.localScale = Vector3.one * 0.01f;

            var tmp = root.AddComponent<TextMeshProUGUI>();
            tmp.text        = "+100";
            tmp.fontSize    = 26;
            tmp.alignment   = TextAlignmentOptions.Center;
            tmp.color       = new Color(1f, 0.85f, 0.2f);
            tmp.fontStyle   = FontStyles.Bold;
            tmp.raycastTarget = false;

            // FloatingText after TMP so RequireComponent is satisfied
            root.AddComponent<FloatingText>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[SetupCombatSystems] Created FloatingText.prefab");
            return prefab.GetComponent<FloatingText>();
        }

        static GameObject EnsureHpBarPrefab()
        {
            const string path = "Assets/Prefabs/UI/HpBar.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                Debug.Log("[SetupCombatSystems] HpBar.prefab already exists — skipping.");
                return existing;
            }

            // 3D bar: thin flat cube (Background) + green cube child (Fill).
            // LaneRenderer drives Fill.localScale.x = hp/maxHp each frame.
            var root = new GameObject("HpBar");

            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "Background";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale = new Vector3(1.0f, 0.10f, 0.04f);
            var bgColl = bg.GetComponent<Collider>();
            if (bgColl != null) Object.DestroyImmediate(bgColl);
            SetMeshColor(bg.GetComponent<Renderer>(), new Color(0.08f, 0.08f, 0.08f, 0.85f));

            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "Fill";
            fill.transform.SetParent(root.transform, false);
            fill.transform.localScale   = new Vector3(1.0f, 0.10f, 0.05f);
            fill.transform.localPosition = new Vector3(0f, 0f, -0.005f);   // slightly in front of bg
            var fillColl = fill.GetComponent<Collider>();
            if (fillColl != null) Object.DestroyImmediate(fillColl);
            SetMeshColor(fill.GetComponent<Renderer>(), new Color(0.18f, 0.88f, 0.28f));

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[SetupCombatSystems] Created HpBar.prefab");
            return prefab;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Shared helpers
        // ─────────────────────────────────────────────────────────────────────

        static GameObject EnsureSpherePrefab(string path, string goName, float scale, Color color)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = goName;
            go.transform.localScale = Vector3.one * scale;

            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            SetMeshColor(go.GetComponent<Renderer>(), color);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log($"[SetupCombatSystems] Created sphere prefab: {path}");
            return prefab;
        }

        static void SetMeshColor(Renderer rend, Color color)
        {
            if (rend == null) return;
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit != null)
            {
                var mat = new Material(urpLit);
                mat.SetColor("_BaseColor", color);
                rend.sharedMaterial = mat;
            }
            else
            {
                rend.sharedMaterial.color = color;
            }
        }

        static void EnsureDir(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts  = path.Split('/');
            var parent = string.Join("/", parts, 0, parts.Length - 1);
            AssetDatabase.CreateFolder(parent, parts[parts.Length - 1]);
        }
    }
}
