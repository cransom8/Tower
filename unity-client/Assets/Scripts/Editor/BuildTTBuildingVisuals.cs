#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using CastleDefender.Game;
using CastleDefender.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CastleDefender.Editor
{
    public static class BuildTTBuildingVisuals
    {
        const string MenuPath = "Castle Defender/Buildings/Build TT Building Visual Pass";
        const int PortraitTextureSize = 512;
        const byte PortraitAlphaCropThreshold = 8;
        const int PortraitCounterClockwiseQuarterTurns = 3;
        const float DefaultPortraitYaw = 150f;
        const float DefaultPortraitFrameFill = 0.88f;
        const float DefaultPortraitCanvasFill = 0.90f;
        const float DefaultPortraitVerticalFocus = 0.56f;
        const float DefaultPortraitCameraHeightBias = -0.10f;
        const float DefaultPortraitLookAtHeightBias = 0.00f;
        static readonly float[] TierTintMultipliers = { 1f, 0.94f, 0.88f, 0.82f };

        enum AccentStyle
        {
            None = 0,
            StandardTier2 = 1,
            StandardTier3 = 2,
            WallTier3 = 3,
            GateTier3 = 4,
            TurretTier3 = 5,
            ArcherTier2 = 6,
            ArcherTier3 = 7,
        }

        sealed class TierSpec
        {
            public string label;
            public string sourceModelPath;
            public AccentStyle accentStyle;
            public string cardId;
        }

        sealed class ChainSpec
        {
            public string key;
            public string buildingType;
            public string displayName;
            public TieredBuildingVisual.VisualMode mode;
            public float portraitYaw = DefaultPortraitYaw;
            public float portraitFrameFill = DefaultPortraitFrameFill;
            public float portraitCanvasFill = DefaultPortraitCanvasFill;
            public float portraitVerticalFocus = DefaultPortraitVerticalFocus;
            public float portraitCameraHeightBias = DefaultPortraitCameraHeightBias;
            public float portraitLookAtHeightBias = DefaultPortraitLookAtHeightBias;
            public TierSpec[] tiers = Array.Empty<TierSpec>();
        }

        struct TeamAccentMaterials
        {
            public Material red;
            public Material blue;
            public Material yellow;
            public Material green;
        }

        struct TrimMaterials
        {
            public Material baseNeutral;
            public Material metal;
            public Material royal;
        }

        [MenuItem(MenuPath)]
        public static void Run()
        {
            EnsureFolderPath(EditorPaths.GENERATED_BUILDING_PREFABS_ROOT);
            EnsureFolderPath(EditorPaths.GENERATED_BUILDING_MATERIALS_ROOT);
            EnsureFolderPath(EditorPaths.GENERATED_BUILDING_PORTRAITS_ROOT);
            EnsureFolderPath(Path.GetDirectoryName(EditorPaths.GENERATED_BUILDING_CATALOG_ASSET)?.Replace("\\", "/"));

            var teamAccentMaterials = LoadTeamAccentMaterials();
            var trimMaterials = EnsureTrimMaterials();
            var chains = BuildChainSpecs();
            var catalogEntries = new List<BuildingVisualCatalog.ChainEntry>(chains.Length);

            int builtPrefabs = 0;
            int bakedPortraits = 0;

            for (int i = 0; i < chains.Length; i++)
            {
                var chain = chains[i];
                var prefab = BuildChainPrefab(chain, teamAccentMaterials, trimMaterials);
                if (prefab == null)
                    continue;

                builtPrefabs++;
                var portraitPaths = BakeChainPortraits(chain, prefab);
                bakedPortraits += portraitPaths.Length;

                catalogEntries.Add(new BuildingVisualCatalog.ChainEntry
                {
                    key = chain.key,
                    buildingType = chain.buildingType,
                    displayName = chain.displayName,
                    maxTier = chain.tiers.Length,
                    prefab = prefab,
                    cardIds = CollectCardIds(chain),
                    portraitResourcePaths = portraitPaths,
                });
            }

            var catalog = SaveCatalog(catalogEntries.ToArray());
            WireGameEnvironment(catalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BuildTTBuildingVisuals] Done. prefabs={builtPrefabs} portraits={bakedPortraits} catalogEntries={catalogEntries.Count}");
        }

        static ChainSpec[] BuildChainSpecs()
        {
            return new[]
            {
                new ChainSpec
                {
                    key = "town_core",
                    buildingType = "town_core",
                    displayName = "Civic",
                    mode = TieredBuildingVisual.VisualMode.ExclusiveSwaps,
                    portraitYaw = 150f,
                    portraitFrameFill = 0.90f,
                    portraitCanvasFill = 0.92f,
                    portraitVerticalFocus = 0.56f,
                    portraitCameraHeightBias = -0.12f,
                    portraitLookAtHeightBias = 0.00f,
                    tiers = new[]
                    {
                        new TierSpec { label = "House", sourceModelPath = EditorPaths.TT_BUILDING_HOUSE, accentStyle = AccentStyle.None, cardId = "building_house" },
                        new TierSpec { label = "Town Hall", sourceModelPath = EditorPaths.TT_BUILDING_TOWN_HALL, accentStyle = AccentStyle.None, cardId = "building_town_hall" },
                        new TierSpec { label = "Keep", sourceModelPath = EditorPaths.TT_BUILDING_KEEP, accentStyle = AccentStyle.None, cardId = "building_keep" },
                        new TierSpec { label = "Castle", sourceModelPath = EditorPaths.TT_BUILDING_CASTLE, accentStyle = AccentStyle.None, cardId = "building_castle" },
                    },
                },
                StandardBuilding("barracks", "barracks", "Barracks", EditorPaths.TT_BUILDING_BARRACKS),
                StandardBuilding("blacksmith", "blacksmith", "Blacksmith", EditorPaths.TT_BUILDING_BLACKSMITH),
                StandardBuilding("temple", "temple", "Temple", EditorPaths.TT_BUILDING_TEMPLE),
                StandardBuilding("wizard_tower", "wizard_tower", "Wizard Tower", EditorPaths.TT_BUILDING_WIZARD_TOWER),
                StandardBuilding("archery_tower", "archery_tower", "Archery Tower", EditorPaths.TT_BUILDING_ARCHERY),
                StandardBuilding("stable", "stable", "Stable", EditorPaths.TT_BUILDING_STABLE),
                StandardBuilding("workshop", "workshop", "Workshop", EditorPaths.TT_BUILDING_WORKSHOP),
                StandardBuilding("library", "library", "Library", EditorPaths.TT_BUILDING_LIBRARY),
                StandardBuilding("market", "market", "Market", EditorPaths.TT_BUILDING_MARKET),
                StandardBuilding("lumber_mill", "lumber_mill", "Lumber Mill", EditorPaths.TT_BUILDING_LUMBER_MILL),
                new ChainSpec
                {
                    key = "wall",
                    buildingType = "wall",
                    displayName = "Walls",
                    mode = TieredBuildingVisual.VisualMode.ExclusiveSwaps,
                    portraitYaw = 150f,
                    portraitFrameFill = 0.92f,
                    portraitCanvasFill = 0.92f,
                    portraitVerticalFocus = 0.52f,
                    portraitCameraHeightBias = -0.16f,
                    portraitLookAtHeightBias = -0.02f,
                    tiers = new[]
                    {
                        new TierSpec { label = "Wall 1", sourceModelPath = EditorPaths.TT_BUILDING_WALL_A, accentStyle = AccentStyle.None, cardId = "wall_tier_1" },
                        new TierSpec { label = "Wall 2", sourceModelPath = EditorPaths.TT_BUILDING_WALL_B, accentStyle = AccentStyle.None, cardId = "wall_tier_2" },
                        new TierSpec { label = "Wall 3", sourceModelPath = EditorPaths.TT_BUILDING_WALL_B, accentStyle = AccentStyle.WallTier3, cardId = "wall_tier_3" },
                    },
                },
                new ChainSpec
                {
                    key = "gate",
                    buildingType = "gate",
                    displayName = "Gates",
                    mode = TieredBuildingVisual.VisualMode.ExclusiveSwaps,
                    portraitYaw = 150f,
                    portraitFrameFill = 0.90f,
                    portraitCanvasFill = 0.92f,
                    portraitVerticalFocus = 0.52f,
                    portraitCameraHeightBias = -0.14f,
                    portraitLookAtHeightBias = -0.02f,
                    tiers = new[]
                    {
                        new TierSpec { label = "Gate 1", sourceModelPath = EditorPaths.TT_BUILDING_GATE_A, accentStyle = AccentStyle.None, cardId = "gate_tier_1" },
                        new TierSpec { label = "Gate 2", sourceModelPath = EditorPaths.TT_BUILDING_GATE_B, accentStyle = AccentStyle.None, cardId = "gate_tier_2" },
                        new TierSpec { label = "Gate 3", sourceModelPath = EditorPaths.TT_BUILDING_GATE_B, accentStyle = AccentStyle.GateTier3, cardId = "gate_tier_3" },
                    },
                },
                new ChainSpec
                {
                    key = "turret",
                    buildingType = "turret",
                    displayName = "Turrets",
                    mode = TieredBuildingVisual.VisualMode.ExclusiveSwaps,
                    portraitYaw = 150f,
                    portraitFrameFill = 0.90f,
                    portraitCanvasFill = 0.92f,
                    portraitVerticalFocus = 0.54f,
                    portraitCameraHeightBias = -0.14f,
                    portraitLookAtHeightBias = -0.02f,
                    tiers = new[]
                    {
                        new TierSpec { label = "Turret 1", sourceModelPath = EditorPaths.TT_BUILDING_CORNER_A, accentStyle = AccentStyle.None, cardId = "turret_tier_1" },
                        new TierSpec { label = "Turret 2", sourceModelPath = EditorPaths.TT_BUILDING_CORNER_B, accentStyle = AccentStyle.None, cardId = "turret_tier_2" },
                        new TierSpec { label = "Turret 3", sourceModelPath = EditorPaths.TT_BUILDING_CORNER_B, accentStyle = AccentStyle.TurretTier3, cardId = "turret_tier_3" },
                    },
                },
                new ChainSpec
                {
                    key = "tower_archer",
                    buildingType = "tower_archer",
                    displayName = "Archer Towers",
                    mode = TieredBuildingVisual.VisualMode.ExclusiveSwaps,
                    portraitYaw = 150f,
                    portraitFrameFill = 0.90f,
                    portraitCanvasFill = 0.92f,
                    portraitVerticalFocus = 0.54f,
                    portraitCameraHeightBias = -0.14f,
                    portraitLookAtHeightBias = -0.01f,
                    tiers = new[]
                    {
                        new TierSpec { label = "Archer Tower 1", sourceModelPath = EditorPaths.TT_BUILDING_TOWER_A, accentStyle = AccentStyle.None, cardId = "archer_tower_tier_1" },
                        new TierSpec { label = "Archer Tower 2", sourceModelPath = EditorPaths.TT_BUILDING_TOWER_B, accentStyle = AccentStyle.ArcherTier2, cardId = "archer_tower_tier_2" },
                        new TierSpec { label = "Archer Tower 3", sourceModelPath = EditorPaths.TT_BUILDING_TOWER_C, accentStyle = AccentStyle.ArcherTier3, cardId = "archer_tower_tier_3" },
                    },
                },
            };
        }

        static ChainSpec StandardBuilding(string cardKey, string buildingType, string displayName, string modelPath)
        {
            return new ChainSpec
            {
                key = buildingType,
                buildingType = buildingType,
                displayName = displayName,
                mode = TieredBuildingVisual.VisualMode.AdditiveLayers,
                portraitYaw = 150f,
                portraitFrameFill = 0.88f,
                portraitCanvasFill = 0.90f,
                portraitVerticalFocus = 0.56f,
                portraitCameraHeightBias = -0.10f,
                portraitLookAtHeightBias = 0.00f,
                tiers = new[]
                {
                    new TierSpec { label = $"{displayName} Tier 1", sourceModelPath = modelPath, accentStyle = AccentStyle.None, cardId = $"buildings_{cardKey}_tier1" },
                    new TierSpec { label = $"{displayName} Tier 2", sourceModelPath = modelPath, accentStyle = AccentStyle.StandardTier2, cardId = $"buildings_{cardKey}_tier2" },
                    new TierSpec { label = $"{displayName} Tier 3", sourceModelPath = modelPath, accentStyle = AccentStyle.StandardTier3, cardId = $"buildings_{cardKey}_tier3" },
                },
            };
        }

        static string[] CollectCardIds(ChainSpec chain)
        {
            var result = new string[chain.tiers.Length];
            for (int i = 0; i < chain.tiers.Length; i++)
                result[i] = chain.tiers[i].cardId;
            return result;
        }

        static TeamAccentMaterials LoadTeamAccentMaterials()
        {
            return new TeamAccentMaterials
            {
                red = EnsureTeamAccentMaterial("TT_BannerAccent_Red_URP.mat", EditorPaths.TT_BANNER_MATERIAL_RED),
                blue = EnsureTeamAccentMaterial("TT_BannerAccent_Blue_URP.mat", EditorPaths.TT_BANNER_MATERIAL_BLUE),
                yellow = EnsureTeamAccentMaterial("TT_BannerAccent_Yellow_URP.mat", EditorPaths.TT_BANNER_MATERIAL_YELLOW),
                green = EnsureTeamAccentMaterial("TT_BannerAccent_Green_URP.mat", EditorPaths.TT_BANNER_MATERIAL_GREEN),
            };
        }

        static Material LoadFirstRendererMaterial(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return null;

            var renderer = prefab.GetComponentInChildren<Renderer>(true);
            return renderer != null ? renderer.sharedMaterial : null;
        }

        static TrimMaterials EnsureTrimMaterials()
        {
            return new TrimMaterials
            {
                baseNeutral = EnsureBaseBuildingMaterial("TT_BuildingBase_Neutral.mat", EditorPaths.TT_BUILDING_WHITE_MATERIAL),
                metal = EnsureTrimMaterial("TT_BuildingTrim_Metal.mat", new Color(0.42f, 0.44f, 0.49f, 1f), 0.15f, 0.20f),
                royal = EnsureTrimMaterial("TT_BuildingTrim_Royal.mat", new Color(0.86f, 0.73f, 0.32f, 1f), 0.10f, 0.16f),
            };
        }

        static Material EnsureBaseBuildingMaterial(string fileName, string sourceMaterialPath)
        {
            string assetPath = $"{EditorPaths.GENERATED_BUILDING_MATERIALS_ROOT}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
                return existing;

            var source = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterialPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);

            if (source != null)
            {
                Texture baseTexture = null;
                if (source.HasProperty("_BaseMap"))
                    baseTexture = source.GetTexture("_BaseMap");
                if (baseTexture == null && source.HasProperty("_MainTex"))
                    baseTexture = source.GetTexture("_MainTex");

                Color baseColor = Color.white;
                if (source.HasProperty("_BaseColor"))
                    baseColor = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color"))
                    baseColor = source.GetColor("_Color");

                if (material.HasProperty("_BaseMap") && baseTexture != null)
                    material.SetTexture("_BaseMap", baseTexture);
                if (material.HasProperty("_MainTex") && baseTexture != null)
                    material.SetTexture("_MainTex", baseTexture);
                if (material.HasProperty("_BaseColor"))
                    material.SetColor("_BaseColor", baseColor);
                if (material.HasProperty("_Color"))
                    material.SetColor("_Color", baseColor);
            }

            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.08f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0.08f);

            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        static Material EnsureTrimMaterial(string fileName, Color color, float metallic, float smoothness)
        {
            string assetPath = $"{EditorPaths.GENERATED_BUILDING_MATERIALS_ROOT}/{fileName}";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", smoothness);

            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        static Material EnsureTeamAccentMaterial(string fileName, string sourceMaterialPath)
        {
            string assetPath = $"{EditorPaths.GENERATED_BUILDING_MATERIALS_ROOT}/{fileName}";
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, assetPath);
            }

            var source = AssetDatabase.LoadAssetAtPath<Material>(sourceMaterialPath);
            SyncMaterialFromSource(material, source, 0.5f);
            EditorUtility.SetDirty(material);
            return material;
        }

        static void SyncMaterialFromSource(Material destination, Material source, float smoothness)
        {
            if (destination == null)
                return;

            Texture baseTexture = null;
            Color baseColor = Color.white;

            if (source != null)
            {
                if (source.HasProperty("_BaseMap"))
                    baseTexture = source.GetTexture("_BaseMap");
                if (baseTexture == null && source.HasProperty("_MainTex"))
                    baseTexture = source.GetTexture("_MainTex");

                if (source.HasProperty("_BaseColor"))
                    baseColor = source.GetColor("_BaseColor");
                else if (source.HasProperty("_Color"))
                    baseColor = source.GetColor("_Color");
            }

            if (destination.HasProperty("_BaseMap"))
                destination.SetTexture("_BaseMap", baseTexture);
            if (destination.HasProperty("_MainTex"))
                destination.SetTexture("_MainTex", baseTexture);
            if (destination.HasProperty("_BaseColor"))
                destination.SetColor("_BaseColor", baseColor);
            if (destination.HasProperty("_Color"))
                destination.SetColor("_Color", baseColor);

            if (destination.HasProperty("_Metallic"))
                destination.SetFloat("_Metallic", 0f);
            if (destination.HasProperty("_Smoothness"))
                destination.SetFloat("_Smoothness", smoothness);
            if (destination.HasProperty("_Glossiness"))
                destination.SetFloat("_Glossiness", smoothness);
        }

        static GameObject BuildChainPrefab(ChainSpec chain, TeamAccentMaterials teamAccentMaterials, TrimMaterials trimMaterials)
        {
            string prefabAssetPath = $"{EditorPaths.GENERATED_BUILDING_PREFABS_ROOT}/{chain.key}.prefab";
            var root = new GameObject(chain.key);
            var neutralTintRenderers = new List<Renderer>();
            var portraitFrameRenderers = new List<Renderer>();
            var teamColorTargets = new List<TeamColorMaterialProfile.Target>();
            var higherTierRoots = new List<GameObject>();

            try
            {
                GameObject baseRoot = null;
                GameObject tier2Root = null;
                GameObject tier3Root = null;

                if (chain.mode == TieredBuildingVisual.VisualMode.AdditiveLayers)
                {
                    baseRoot = new GameObject("BaseModel");
                    baseRoot.transform.SetParent(root.transform, false);
                    var baseModel = InstantiateAssetAsChild(chain.tiers[0].sourceModelPath, baseRoot.transform);
                    ApplyNeutralBuildingMaterial(baseModel, trimMaterials.baseNeutral);
                    AddRange(neutralTintRenderers, baseModel.GetComponentsInChildren<Renderer>(true));
                    AddRange(portraitFrameRenderers, baseModel.GetComponentsInChildren<Renderer>(true));
                    Bounds baseBounds = CalculateLocalBounds(baseRoot.transform);

                    for (int tierIndex = 1; tierIndex < chain.tiers.Length; tierIndex++)
                    {
                        string layerName = GetTierRootName(tierIndex + 1);
                        var layerRoot = new GameObject(layerName);
                        layerRoot.transform.SetParent(root.transform, false);
                        AddAccentSet(layerRoot.transform, baseBounds, chain.tiers[tierIndex].accentStyle, teamColorTargets, trimMaterials);

                        if (tierIndex == 1)
                            tier2Root = layerRoot;
                        else if (tierIndex == 2)
                            tier3Root = layerRoot;
                        else
                            higherTierRoots.Add(layerRoot);
                    }
                }
                else
                {
                    for (int tierIndex = 0; tierIndex < chain.tiers.Length; tierIndex++)
                    {
                        string layerName = GetTierRootName(tierIndex + 1);
                        var layerRoot = new GameObject(layerName);
                        layerRoot.transform.SetParent(root.transform, false);

                        var modelInstance = InstantiateAssetAsChild(chain.tiers[tierIndex].sourceModelPath, layerRoot.transform);
                        ApplyNeutralBuildingMaterial(modelInstance, trimMaterials.baseNeutral);
                        AddRange(neutralTintRenderers, modelInstance.GetComponentsInChildren<Renderer>(true));
                        AddRange(portraitFrameRenderers, modelInstance.GetComponentsInChildren<Renderer>(true));
                        Bounds tierBounds = CalculateLocalBounds(layerRoot.transform);
                        AddAccentSet(layerRoot.transform, tierBounds, chain.tiers[tierIndex].accentStyle, teamColorTargets, trimMaterials);

                        if (tierIndex == 0)
                            baseRoot = layerRoot;
                        else if (tierIndex == 1)
                            tier2Root = layerRoot;
                        else if (tierIndex == 2)
                            tier3Root = layerRoot;
                        else
                            higherTierRoots.Add(layerRoot);
                    }
                }

                var tieredVisual = root.GetComponent<TieredBuildingVisual>() ?? root.AddComponent<TieredBuildingVisual>();
                tieredVisual.ConfigureForEditor(
                    chain.mode,
                    baseRoot,
                    tier2Root,
                    tier3Root,
                    higherTierRoots.ToArray(),
                    neutralTintRenderers.ToArray(),
                    portraitFrameRenderers.ToArray(),
                    TierTintMultipliers);

                if (teamColorTargets.Count > 0)
                {
                    var teamProfile = root.GetComponent<TeamColorMaterialProfile>() ?? root.AddComponent<TeamColorMaterialProfile>();
                    teamProfile.ConfigureForEditor(
                        teamAccentMaterials.red,
                        teamAccentMaterials.blue,
                        teamAccentMaterials.yellow,
                        teamAccentMaterials.green,
                        teamColorTargets.ToArray());
                    EditorUtility.SetDirty(teamProfile);
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath);
                if (prefab == null)
                    Debug.LogError($"[BuildTTBuildingVisuals] Failed to save prefab '{prefabAssetPath}'.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static string[] BakeChainPortraits(ChainSpec chain, GameObject prefab)
        {
            var results = new string[chain.tiers.Length];
            for (int i = 0; i < chain.tiers.Length; i++)
            {
                string cardId = chain.tiers[i].cardId;
                string assetPath = $"{EditorPaths.GENERATED_BUILDING_PORTRAITS_ROOT}/{cardId}.png";
                results[i] = $"TechTree/Buildings/{cardId}";

                GameObject studioRoot = null;
                RenderTexture studioTexture = null;
                Texture2D bakedTexture = null;
                try
                {
                    var portraitCamera = RuntimePortraitStudio.Create(
                        $"BuildingPortrait_{chain.key}_{i + 1}",
                        null,
                        out studioRoot,
                        out studioTexture,
                        PortraitTextureSize,
                        new Color(0f, 0f, 0f, 0f));
                    portraitCamera.RotationY = chain.portraitYaw;
                    portraitCamera.FrameFill = chain.portraitFrameFill;
                    portraitCamera.VerticalFocus = chain.portraitVerticalFocus;
                    portraitCamera.CameraHeightBias = chain.portraitCameraHeightBias;
                    portraitCamera.LookAtHeightBias = chain.portraitLookAtHeightBias;

                    int targetTier = i + 1;
                    bakedTexture = portraitCamera.CapturePrefabImmediate(
                        prefab,
                        1f,
                        staged =>
                        {
                            var tieredVisual = staged.GetComponent<TieredBuildingVisual>();
                            if (tieredVisual != null)
                                tieredVisual.ApplyTier(targetTier);

                            NormalizePortraitFacing(staged);
                        });
                    var rotatedTexture = RotateTextureCounterClockwise(bakedTexture, PortraitCounterClockwiseQuarterTurns);
                    if (!ReferenceEquals(rotatedTexture, bakedTexture))
                    {
                        Object.DestroyImmediate(bakedTexture);
                        bakedTexture = rotatedTexture;
                    }

                    var reframedTexture = ReframePortraitTexture(bakedTexture, chain.portraitCanvasFill);
                    if (!ReferenceEquals(reframedTexture, bakedTexture))
                    {
                        Object.DestroyImmediate(bakedTexture);
                        bakedTexture = reframedTexture;
                    }

                    WriteTextureAsset(bakedTexture, assetPath);
                    ConfigurePortraitImporter(assetPath);
                }
                finally
                {
                    if (bakedTexture != null)
                        Object.DestroyImmediate(bakedTexture);
                    if (studioRoot != null)
                        Object.DestroyImmediate(studioRoot);
                    if (studioTexture != null)
                    {
                        studioTexture.Release();
                        Object.DestroyImmediate(studioTexture);
                    }
                }
            }

            return results;
        }

        static BuildingVisualCatalog SaveCatalog(BuildingVisualCatalog.ChainEntry[] entries)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BuildingVisualCatalog>(EditorPaths.GENERATED_BUILDING_CATALOG_ASSET);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<BuildingVisualCatalog>();
                AssetDatabase.CreateAsset(catalog, EditorPaths.GENERATED_BUILDING_CATALOG_ASSET);
            }

            catalog.ConfigureForEditor(entries);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        static void WireGameEnvironment(BuildingVisualCatalog catalog)
        {
            var prefabRoot = PrefabUtility.LoadPrefabContents(EditorPaths.GAME_ENVIRONMENT_PREFAB);
            try
            {
                var anchors = prefabRoot.GetComponentsInChildren<FortressPadAnchor>(true);
                for (int i = 0; i < anchors.Length; i++)
                {
                    var anchor = anchors[i];
                    if (anchor == null || anchor.GetComponent<BarracksSiteView>() != null)
                        continue;
                    if (!catalog.TryGetByBuildingType(anchor.BuildingType, out var entry) || entry?.prefab == null)
                        continue;

                    var bridge = anchor.GetComponent<SnapshotBuildingVisualBridge>() ?? anchor.gameObject.AddComponent<SnapshotBuildingVisualBridge>();
                    bridge.ConfigureForEditor(catalog, null, anchor.GetComponents<Renderer>());
                    EditorUtility.SetDirty(bridge);
                }

                var barracksSites = prefabRoot.GetComponentsInChildren<BarracksSiteView>(true);
                for (int i = 0; i < barracksSites.Length; i++)
                {
                    var site = barracksSites[i];
                    if (site == null)
                        continue;

                    var bridge = site.GetComponent<SnapshotBuildingVisualBridge>() ?? site.gameObject.AddComponent<SnapshotBuildingVisualBridge>();
                    bridge.ConfigureForEditor(catalog, "barracks", site.GetComponents<Renderer>());
                    EditorUtility.SetDirty(bridge);
                }

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, EditorPaths.GAME_ENVIRONMENT_PREFAB);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static void AddAccentSet(
            Transform parent,
            Bounds bounds,
            AccentStyle accentStyle,
            List<TeamColorMaterialProfile.Target> teamColorTargets,
            TrimMaterials trimMaterials)
        {
            switch (accentStyle)
            {
                case AccentStyle.StandardTier2:
                    AddBanner(parent, bounds, -0.55f, 0.74f, 0.06f, 0.72f, teamColorTargets);
                    AddShield(parent, bounds, 0f, 0.42f, 0.07f, 0.42f, trimMaterials.royal);
                    break;
                case AccentStyle.StandardTier3:
                    AddBanner(parent, bounds, -0.58f, 0.76f, 0.06f, 0.78f, teamColorTargets);
                    AddBanner(parent, bounds, 0.58f, 0.76f, 0.06f, 0.72f, teamColorTargets);
                    AddShield(parent, bounds, 0f, 0.46f, 0.08f, 0.48f, trimMaterials.royal);
                    AddTrimBar(parent, trimMaterials.royal, bounds, 0f, 0.57f, 0.09f, bounds.size.x * 0.42f, bounds.size.y * 0.06f);
                    break;
                case AccentStyle.WallTier3:
                    AddBanner(parent, bounds, 0f, 0.78f, 0.05f, 0.68f, teamColorTargets);
                    AddShield(parent, bounds, 0f, 0.44f, 0.08f, 0.44f, trimMaterials.royal);
                    AddTrimBar(parent, trimMaterials.metal, bounds, 0f, 0.96f, 0.03f, bounds.size.x * 0.78f, bounds.size.y * 0.05f);
                    break;
                case AccentStyle.GateTier3:
                    AddBanner(parent, bounds, 0f, 0.84f, 0.05f, 0.72f, teamColorTargets);
                    AddShield(parent, bounds, 0f, 0.58f, 0.07f, 0.44f, trimMaterials.royal);
                    AddTrimBar(parent, trimMaterials.royal, bounds, 0f, 0.92f, 0.03f, bounds.size.x * 0.50f, bounds.size.y * 0.05f);
                    AddVerticalBand(parent, trimMaterials.metal, bounds, -0.20f, 0.46f, 0.03f, bounds.size.y * 0.58f);
                    AddVerticalBand(parent, trimMaterials.metal, bounds, 0.20f, 0.46f, 0.03f, bounds.size.y * 0.58f);
                    break;
                case AccentStyle.TurretTier3:
                    AddBanner(parent, bounds, 0.40f, 0.84f, 0.05f, 0.62f, teamColorTargets);
                    AddShield(parent, bounds, 0f, 0.46f, 0.07f, 0.40f, trimMaterials.royal);
                    AddTrimBar(parent, trimMaterials.metal, bounds, 0f, 0.94f, 0.03f, bounds.size.x * 0.52f, bounds.size.y * 0.05f);
                    break;
                case AccentStyle.ArcherTier2:
                    AddBanner(parent, bounds, 0.42f, 0.82f, 0.05f, 0.60f, teamColorTargets);
                    break;
                case AccentStyle.ArcherTier3:
                    AddBanner(parent, bounds, 0.46f, 0.84f, 0.05f, 0.64f, teamColorTargets);
                    AddShield(parent, bounds, 0f, 0.48f, 0.07f, 0.38f, trimMaterials.royal);
                    AddTrimBar(parent, trimMaterials.royal, bounds, 0f, 0.66f, 0.05f, bounds.size.x * 0.32f, bounds.size.y * 0.05f);
                    break;
            }
        }

        static void AddBanner(Transform parent, Bounds bounds, float xNormalized, float yNormalized, float zOffset, float relativeScale, List<TeamColorMaterialProfile.Target> teamColorTargets)
        {
            var banner = InstantiateAssetAsChild(EditorPaths.TT_BANNER_BLUE, parent, "Banner");
            float scale = Mathf.Clamp(bounds.size.y * relativeScale * 0.18f, 0.42f, 1.10f);
            banner.transform.localPosition = ResolveFrontPoint(bounds, xNormalized, yNormalized, zOffset);
            banner.transform.localRotation = Quaternion.identity;
            banner.transform.localScale = Vector3.one * scale;

            var renderers = banner.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;

                teamColorTargets.Add(new TeamColorMaterialProfile.Target
                {
                    renderer = renderers[i],
                    replaceAllMaterials = true,
                    materialIndex = -1,
                });
            }
        }

        static void AddShield(Transform parent, Bounds bounds, float xNormalized, float yNormalized, float zOffset, float relativeScale, Material material)
        {
            var shield = InstantiateAssetAsChild(EditorPaths.TT_SHIELD, parent, "Shield");
            float scale = Mathf.Clamp(bounds.size.y * relativeScale * 0.12f, 0.20f, 0.70f);
            shield.transform.localPosition = ResolveFrontPoint(bounds, xNormalized, yNormalized, zOffset);
            shield.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            shield.transform.localScale = Vector3.one * scale;
            ReplaceAllRendererMaterials(shield.GetComponentsInChildren<Renderer>(true), material);
        }

        static void AddTrimBar(Transform parent, Material material, Bounds bounds, float xNormalized, float yNormalized, float zOffset, float width, float thickness)
        {
            var trim = CreateTrimCube(parent, material, "TrimBar");
            trim.transform.localPosition = ResolveFrontPoint(bounds, xNormalized, yNormalized, zOffset);
            trim.transform.localRotation = Quaternion.identity;
            trim.transform.localScale = new Vector3(Mathf.Max(0.08f, width), Mathf.Max(0.03f, thickness), Mathf.Max(0.03f, thickness));
        }

        static void AddVerticalBand(Transform parent, Material material, Bounds bounds, float xNormalized, float yNormalized, float zOffset, float height)
        {
            var trim = CreateTrimCube(parent, material, "VerticalBand");
            trim.transform.localPosition = ResolveFrontPoint(bounds, xNormalized, yNormalized, zOffset);
            trim.transform.localRotation = Quaternion.identity;
            trim.transform.localScale = new Vector3(
                Mathf.Max(0.05f, bounds.size.x * 0.06f),
                Mathf.Max(0.12f, height),
                Mathf.Max(0.03f, bounds.size.z * 0.04f));
        }

        static GameObject CreateTrimCube(Transform parent, Material material, string name)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            var collider = cube.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;

            return cube;
        }

        static Vector3 ResolveFrontPoint(Bounds bounds, float xNormalized, float yNormalized, float zOffset)
        {
            float x = Mathf.Lerp(bounds.min.x, bounds.max.x, Mathf.Clamp01((xNormalized + 1f) * 0.5f));
            float y = Mathf.Lerp(bounds.min.y, bounds.max.y, Mathf.Clamp01(yNormalized));
            float z = bounds.max.z + Mathf.Max(0.02f, zOffset);
            return new Vector3(x, y, z);
        }

        static GameObject InstantiateAssetAsChild(string assetPath, Transform parent, string nameOverride = null)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null)
                throw new InvalidOperationException($"Missing required asset '{assetPath}'.");

            var instance = PrefabUtility.InstantiatePrefab(asset, parent) as GameObject;
            if (instance == null)
                instance = Object.Instantiate(asset, parent, false);

            if (!string.IsNullOrWhiteSpace(nameOverride))
                instance.name = nameOverride;

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        static void ApplyNeutralBuildingMaterial(GameObject root, Material material)
        {
            if (root == null || material == null)
                return;

            ReplaceAllRendererMaterials(root.GetComponentsInChildren<Renderer>(true), material);
        }

        static void ReplaceAllRendererMaterials(Renderer[] renderers, Material material)
        {
            if (renderers == null || material == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var sharedMaterials = renderer.sharedMaterials;
                if (sharedMaterials == null || sharedMaterials.Length == 0)
                    continue;

                for (int materialIndex = 0; materialIndex < sharedMaterials.Length; materialIndex++)
                    sharedMaterials[materialIndex] = material;

                renderer.sharedMaterials = sharedMaterials;
            }
        }

        static string GetTierRootName(int tier)
        {
            return tier switch
            {
                1 => "BaseModel",
                2 => "Tier2_Visuals",
                3 => "Tier3_Visuals",
                _ => $"Tier{tier}_Visuals",
            };
        }

        static Bounds CalculateLocalBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds localBounds = default;
            Matrix4x4 worldToLocal = root.worldToLocalMatrix;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                Bounds worldBounds = renderer.bounds;
                Vector3 extents = worldBounds.extents;
                Vector3 center = worldBounds.center;
                var corners = new[]
                {
                    center + new Vector3(-extents.x, -extents.y, -extents.z),
                    center + new Vector3(-extents.x, -extents.y, extents.z),
                    center + new Vector3(-extents.x, extents.y, -extents.z),
                    center + new Vector3(-extents.x, extents.y, extents.z),
                    center + new Vector3(extents.x, -extents.y, -extents.z),
                    center + new Vector3(extents.x, -extents.y, extents.z),
                    center + new Vector3(extents.x, extents.y, -extents.z),
                    center + new Vector3(extents.x, extents.y, extents.z),
                };

                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    Vector3 localPoint = worldToLocal.MultiplyPoint3x4(corners[cornerIndex]);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localPoint, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localPoint);
                    }
                }
            }

            return hasBounds ? localBounds : new Bounds(Vector3.zero, Vector3.one);
        }

        static void NormalizePortraitFacing(GameObject staged)
        {
            if (staged == null)
                return;

            float correction = ResolvePortraitQuarterTurn(staged);
            if (Mathf.Abs(correction) <= 0.001f)
                return;

            staged.transform.rotation *= Quaternion.Euler(0f, correction, 0f);
        }

        static float ResolvePortraitQuarterTurn(GameObject staged)
        {
            if (staged == null)
                return 0f;

            var candidateRenderers = ResolvePortraitRenderers(staged);
            if (candidateRenderers.Length == 0)
                return 0f;

            Quaternion originalRotation = staged.transform.rotation;
            float bestTurn = 0f;
            float bestScore = float.MaxValue;
            float[] candidateTurns = { 0f, 90f, 180f, 270f };

            for (int i = 0; i < candidateTurns.Length; i++)
            {
                float turn = candidateTurns[i];
                staged.transform.rotation = originalRotation * Quaternion.Euler(0f, turn, 0f);
                Bounds bounds = CalculateWorldBounds(candidateRenderers);
                float width = Mathf.Max(0.01f, bounds.size.x);
                float depth = Mathf.Max(0.01f, bounds.size.z);
                float score = Mathf.Abs(width - depth);

                if (score < bestScore - 0.001f
                    || (Mathf.Abs(score - bestScore) <= 0.001f && turn < bestTurn))
                {
                    bestScore = score;
                    bestTurn = turn;
                }
            }

            staged.transform.rotation = originalRotation;
            return bestTurn;
        }

        static Renderer[] ResolvePortraitRenderers(GameObject staged)
        {
            var tieredVisual = staged != null ? staged.GetComponent<TieredBuildingVisual>() : null;
            Renderer[] preferred = tieredVisual != null ? tieredVisual.GetPortraitFrameRenderers() : null;
            var source = preferred != null && preferred.Length > 0
                ? preferred
                : staged.GetComponentsInChildren<Renderer>(true);

            var filtered = new List<Renderer>(source.Length);
            for (int i = 0; i < source.Length; i++)
            {
                var renderer = source[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                filtered.Add(renderer);
            }

            return filtered.ToArray();
        }

        static Bounds CalculateWorldBounds(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.one);

            bool hasBounds = false;
            Bounds combined = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds ? combined : new Bounds(Vector3.zero, Vector3.one);
        }

        static void AddRange(List<Renderer> target, Renderer[] renderers)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer != null)
                    target.Add(renderer);
            }
        }

        static void WriteTextureAsset(Texture2D texture, string assetPath)
        {
            if (texture == null)
                throw new InvalidOperationException($"Texture bake failed for '{assetPath}'.");

            byte[] png = texture.EncodeToPNG();
            if (png == null || png.Length == 0)
                throw new InvalidOperationException($"PNG encoding returned no bytes for '{assetPath}'.");

            string absolutePath = Path.GetFullPath(assetPath);
            string directory = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllBytes(absolutePath, png);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        static Texture2D ReframePortraitTexture(Texture2D source, float fillFraction)
        {
            if (source == null)
                return null;

            int width = source.width;
            int height = source.height;
            if (width <= 0 || height <= 0)
                return source;

            var pixels = source.GetPixels32();
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (pixels[rowOffset + x].a < PortraitAlphaCropThreshold)
                        continue;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
                return source;

            int contentWidth = maxX - minX + 1;
            int contentHeight = maxY - minY + 1;
            float clampedFill = Mathf.Clamp(fillFraction, 0.60f, 0.96f);
            float scale = Mathf.Min(
                (width * clampedFill) / Mathf.Max(1, contentWidth),
                (height * clampedFill) / Mathf.Max(1, contentHeight));

            int destWidth = Mathf.Clamp(Mathf.RoundToInt(contentWidth * scale), 1, width);
            int destHeight = Mathf.Clamp(Mathf.RoundToInt(contentHeight * scale), 1, height);
            int offsetX = Mathf.RoundToInt((width - destWidth) * 0.5f);
            int offsetY = Mathf.RoundToInt((height - destHeight) * 0.5f);

            if (contentWidth == width
                && contentHeight == height
                && offsetX == 0
                && offsetY == 0
                && destWidth == width
                && destHeight == height)
            {
                return source;
            }

            var output = new Color[width * height];
            float widthDenominator = Mathf.Max(1f, width - 1f);
            float heightDenominator = Mathf.Max(1f, height - 1f);
            float srcWidthRange = Mathf.Max(0f, contentWidth - 1f);
            float srcHeightRange = Mathf.Max(0f, contentHeight - 1f);

            for (int y = 0; y < destHeight; y++)
            {
                float normalizedY = destHeight > 1 ? y / (destHeight - 1f) : 0.5f;
                float sourceV = (minY + (normalizedY * srcHeightRange)) / heightDenominator;
                int rowOffset = (offsetY + y) * width;

                for (int x = 0; x < destWidth; x++)
                {
                    float normalizedX = destWidth > 1 ? x / (destWidth - 1f) : 0.5f;
                    float sourceU = (minX + (normalizedX * srcWidthRange)) / widthDenominator;
                    output[rowOffset + offsetX + x] = source.GetPixelBilinear(sourceU, sourceV);
                }
            }

            var reframed = new Texture2D(width, height, TextureFormat.RGBA32, false);
            reframed.SetPixels(output);
            reframed.Apply();
            return reframed;
        }

        static Texture2D RotateTextureCounterClockwise(Texture2D source, int quarterTurns)
        {
            if (source == null)
                return null;

            int normalizedTurns = ((quarterTurns % 4) + 4) % 4;
            if (normalizedTurns == 0)
                return source;

            Texture2D rotated = source;
            for (int turn = 0; turn < normalizedTurns; turn++)
            {
                var next = RotateTextureCounterClockwise90(rotated);
                if (!ReferenceEquals(rotated, source))
                    Object.DestroyImmediate(rotated);
                rotated = next;
            }

            return rotated;
        }

        static Texture2D RotateTextureCounterClockwise90(Texture2D source)
        {
            if (source == null)
                return null;

            int width = source.width;
            int height = source.height;
            if (width <= 0 || height <= 0)
                return source;

            var rotated = new Texture2D(height, width, TextureFormat.RGBA32, false);
            var pixels = source.GetPixels32();
            var rotatedPixels = new Color32[pixels.Length];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                for (int x = 0; x < width; x++)
                {
                    int sourceIndex = rowOffset + x;
                    int destX = y;
                    int destY = width - 1 - x;
                    int destIndex = (destY * height) + destX;
                    rotatedPixels[destIndex] = pixels[sourceIndex];
                }
            }

            rotated.SetPixels32(rotatedPixels);
            rotated.Apply();
            return rotated;
        }

        static void ConfigurePortraitImporter(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 1024;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        static void EnsureFolderPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || AssetDatabase.IsValidFolder(folderPath))
                return;

            string normalized = folderPath.Replace("\\", "/");
            string[] segments = normalized.Split('/');
            string current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                string next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }
}
#endif
