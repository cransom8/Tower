#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CastleDefender.Editor
{
    public static class StageLeanNightEnvironment
    {
        const string ScenePath = "Assets/Scenes/Game_ML.unity";
        const string MapRootName = "Map";

        const string LiveCriticalPrefabPath = "Assets/AddressableContent/Environment/GameEnvironment.prefab";
        const string SourceCriticalPrefabPath = "Assets/AddressableContent/Environment/GameEnvironment 1.prefab";
        const string LiveOptionalPrefabPath = "Assets/AddressableContent/Environment/GameEnvironmentOptional.prefab";

        const string TreePrefabPath = "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_large.prefab";
        const string LanternPrefabPath = "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/lantern_pole.prefab";
        const string DarknessMaterialPath = "Assets/Materials/Environment/GameMLDarknessField.mat";
        const string OuterDarknessMaterialPath = "Assets/Materials/Environment/GameMLOuterDarknessField.mat";
        const string ForestVolumeMaterialPath = "Assets/Materials/Environment/GameMLForestVolume.mat";
        const string LanternRoadGlowMaterialPath = "Assets/Materials/Environment/GameMLLanternRoadGlow.mat";
        const string RoadWashMaterialPath = "Assets/Materials/Environment/GameMLRoadWash.mat";
        const string RoadShoulderMaterialPath = "Assets/Materials/Environment/GameMLRoadShoulder.mat";
        const string RoadMaterialSourcePath = "Assets/Materials/Environment/WinterCobbleEnvironment.mat";
        const string RoadMaterialPath = "Assets/Materials/Environment/GameMLNightRoad.mat";
        const string NightProfilePath = "Assets/PostProcess/GameMLNightRoads.asset";

        const string CriticalPreviewName = "GameEnvironment";
        const string OptionalPreviewName = "GameEnvironmentOptional";

        const string CriticalAddress = "environment/game_ml";
        const string OptionalAddress = "environment/game_ml_dressing";

        const float TreeOffset = 3.75f;
        const float TreeTrim = 16f;
        const float TreeBaseScale = 2.55f;
        const float TreeScaleVariation = 0.275f;
        const float TreeRoadSideDrift = 0.75f;
        const float TreeRoadSideAlternate = 0.9f;
        const float TreeRoadTangentJitter = 1.25f;
        const float ForestVolumeInset = 8f;
        const float ForestVolumeHeight = 24f;
        const float ForestVolumeGroundSink = 0.35f;
        const int ForestCurtainLayerCount = 3;
        const float ForestCurtainSpacing = 4.5f;
        const float ForestCurtainHeightStep = 2.8f;
        const int ForestCanopyLayerCount = 4;
        const float ForestCanopyInsetStep = 6f;
        const float ForestCanopyHeightStep = 1.6f;
        const int ForestInteriorSliceCount = 3;
        const float ForestInteriorSliceMargin = 5f;
        const float ForestInteriorSliceScale = 0.92f;
        const float ForestInteriorSliceHeightBoost = 4.5f;
        const float ForestDiagonalSliceScale = 0.9f;
        const float ForestVolumeDarkness = 0.98f;
        const float ForestVolumeNoiseStrength = 0.62f;
        const float ForestVolumeFadeSoftness = 0.18f;
        const float ForestVolumeTopSilhouetteStrength = 1.08f;
        const float ForestVolumeNoiseScale = 0.045f;
        const float ForestVolumeCenterDensity = 1.12f;
        const float ForestVolumeViewOcclusionBoost = 0.4f;
        const float ForestVolumeEdgeFadeDistance = 0.065f;
        const float ExteriorForestSpan = 240f;
        const float ExteriorForestRoadGap = 2f;
        const float ExteriorForestInset = 3f;
        const float ForestCurtainFortressPadding = 3f;
        const float ForestCurtainEndCapDepth = 16f;
        const float FortressDarknessWrapPadding = 6f;
        const float FortressForestWrapPadding = 10f;
        const float FortressTreeOutsidePadding = 8f;
        const float OuterDarknessCrossPadding = 80f;
        const float LanternRoadEdgeInset = 1.25f;
        const float LanternRoadLengthInset = 24f;
        const float DarknessOuterSpan = 260f;
        const float DarknessLayerAlpha = 0.06f;
        const float OuterDarknessLayerAlpha = 0.095f;
        const float DarknessBehindTreesOffset = 10f;
        const float DarknessLayerYOffset = 0.0125f;
        const int DarknessLayerCount = 6;
        const float FortressPerimeterDarknessDepth = 52f;
        const float FortressPerimeterBoundsPadding = 4f;
        const float FortressPerimeterFrontExtension = 24f;
        const float FortressPerimeterRearExtension = 8f;
        const float FortressPerimeterCrossPadding = 4f;
        const int FortressBackdropLayerCount = 2;
        const float FortressBackdropHeight = 28f;
        const float FortressBackdropEdgeOffset = 3f;
        const float FortressBackdropLayerSpacing = 6f;
        const float FortressBackdropCrossPadding = 8f;
        const float FortressBackdropFrontExtension = 18f;
        const float FortressBackdropRearExtension = 10f;
        const float FortressBackdropRoadRetreat = 22f;
        const float FortressBackdropSideDistance = 12f;
        const float FortressBackdropRearDistance = 34f;
        const float FortressBackdropCornerReturnDepth = 18f;
        const float RoadShoulderWidth = 18f;
        const float RoadShoulderLengthPadding = 6f;
        const float RoadShoulderInnerAlpha = 0.22f;
        const float RoadShoulderOuterAlpha = 0.38f;
        const float RoadShoulderNoiseStrength = 0.12f;
        const float RoadShoulderEndFade = 0.08f;
        const float OuterLanternSpotIntensity = 12f;
        const float InnerLanternSpotIntensity = 13.5f;
        const float OuterLanternSpotRange = 40f;
        const float InnerLanternSpotRange = 46f;
        const float OuterLanternSpotAngle = 96f;
        const float InnerLanternSpotAngle = 104f;
        const float OuterLanternInnerAngle = 62f;
        const float InnerLanternInnerAngle = 70f;
        const float OuterLanternShadowStrength = 0.42f;
        const float InnerLanternShadowStrength = 0.48f;
        const float OuterLanternPointIntensity = 4.6f;
        const float InnerLanternPointIntensity = 5.2f;
        const float OuterLanternPointRange = 34f;
        const float InnerLanternPointRange = 40f;

        [MenuItem("Castle Defender/Remote Content/Stage Lean Night Environment Preview")]
        static void StagePreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mapRoot = RequireMapRoot();

            DestroyPreviewIfPresent(mapRoot);

            var criticalPreview = InstantiatePreviewPrefab(mapRoot, LiveCriticalPrefabPath, CriticalPreviewName);
            SyncCriticalPreviewFromSource(criticalPreview);
            ApplyRoadMaterial(criticalPreview.transform);
            EnvironmentPrefabSafety.AssertValidCriticalEnvironmentRoot(
                criticalPreview,
                "staged critical preview");

            var optionalPreview = InstantiatePreviewPrefab(mapRoot, LiveOptionalPrefabPath, OptionalPreviewName);
            RebuildOptionalPreview(optionalPreview.transform, criticalPreview.transform);

            ConfigureNightScene(mapRoot, previewMode: true);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log(
                "[StageLeanNightEnvironment] Staged GameEnvironment/GameEnvironmentOptional preview roots in Game_ML. " +
                "Use 'Apply Lean Night Environment Preview' to write them back to the live remote-content prefabs.");
        }

        [MenuItem("Castle Defender/Remote Content/Apply Lean Night Environment Preview")]
        static void ApplyPreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mapRoot = RequireMapRoot();

            var criticalPreview = mapRoot.Find(CriticalPreviewName)?.gameObject;
            var optionalPreview = mapRoot.Find(OptionalPreviewName)?.gameObject;
            if (criticalPreview == null || optionalPreview == null)
            {
                throw new InvalidOperationException(
                    "Could not find staged preview roots under Map. Run 'Stage Lean Night Environment Preview' first.");
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(criticalPreview))
                throw new InvalidOperationException($"'{CriticalPreviewName}' is not a prefab instance.");
            if (!PrefabUtility.IsPartOfPrefabInstance(optionalPreview))
                throw new InvalidOperationException($"'{OptionalPreviewName}' is not a prefab instance.");

            EnvironmentPrefabSafety.AssertValidCriticalEnvironmentRoot(
                criticalPreview,
                "applied critical preview");

            SavePreviewAsLivePrefab(criticalPreview, LiveCriticalPrefabPath);
            SavePreviewAsLivePrefab(optionalPreview, LiveOptionalPrefabPath);

            UnityEngine.Object.DestroyImmediate(optionalPreview);
            UnityEngine.Object.DestroyImmediate(criticalPreview);

            ConfigureNightScene(mapRoot, previewMode: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log(
                "[StageLeanNightEnvironment] Applied staged night environment preview to GameEnvironment.prefab " +
                "and GameEnvironmentOptional.prefab, then cleaned the scene preview roots.");
        }

        static void SavePreviewAsLivePrefab(GameObject previewRoot, string prefabPath)
        {
            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(previewRoot, prefabPath);
            if (savedPrefab == null)
                throw new InvalidOperationException(
                    $"Failed to save staged preview '{previewRoot.name}' to '{prefabPath}'.");
        }

        [MenuItem("Castle Defender/Remote Content/Cleanup Lean Night Environment Preview")]
        static void CleanupPreview()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var mapRoot = RequireMapRoot();

            DestroyPreviewIfPresent(mapRoot);
            ConfigureNightScene(mapRoot, previewMode: false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[StageLeanNightEnvironment] Removed staged environment preview roots from Game_ML.");
        }

        static Transform RequireMapRoot()
        {
            var mapRoot = GameObject.Find(MapRootName);
            if (mapRoot == null)
                throw new InvalidOperationException($"Could not find '{MapRootName}' in '{ScenePath}'.");

            return mapRoot.transform;
        }

        static void DestroyPreviewIfPresent(Transform mapRoot)
        {
            for (int i = mapRoot.childCount - 1; i >= 0; i--)
            {
                var child = mapRoot.GetChild(i);
                if (child == null)
                    continue;

                if (string.Equals(child.name, CriticalPreviewName, StringComparison.Ordinal) ||
                    string.Equals(child.name, OptionalPreviewName, StringComparison.Ordinal))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        static GameObject InstantiatePreviewPrefab(Transform parent, string prefabPath, string rootName)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"Could not load prefab at '{prefabPath}'.");

            var instance = PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate prefab '{prefabPath}'.");

            instance.name = rootName;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        static void SyncCriticalPreviewFromSource(GameObject criticalPreview)
        {
            using var sourceScope = new PrefabUtility.EditPrefabContentsScope(SourceCriticalPrefabPath);
            var sourceRoot = sourceScope.prefabContentsRoot;
            if (sourceRoot == null)
                throw new InvalidOperationException($"Could not open '{SourceCriticalPrefabPath}'.");

            ClearChildren(criticalPreview.transform);

            var sourceClone = UnityEngine.Object.Instantiate(sourceRoot);
            try
            {
                while (sourceClone.transform.childCount > 0)
                {
                    var child = sourceClone.transform.GetChild(0);
                    child.SetParent(criticalPreview.transform, false);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceClone);
            }
        }

        static void ApplyRoadMaterial(Transform criticalRoot)
        {
            var material = EnsureNightRoadMaterial();

            string[] roadNames =
            {
                "PB_Mine_Center",
                "PB_Red_Player_Lane",
                "Yellow_Player_Lane",
                "PB_Blue_Player_Lane",
                "PB_Green_Player_Lane",
                "Red_Left_Road",
                "Red_Right_Road",
                "Yellow_Left_Road",
                "Yellow_Right_Road",
                "Blue_Left_Road",
                "Blue_Right_Road",
                "Green_Left_Road",
                "Green_Right_road",
            };

            foreach (string roadName in roadNames)
            {
                var target = FindChildRecursive(criticalRoot, roadName);
                if (target == null)
                    continue;

                var renderer = target.GetComponent<Renderer>();
                if (renderer == null)
                    continue;

                renderer.sharedMaterial = material;
                EditorUtility.SetDirty(renderer);
            }
        }

        static Material EnsureNightRoadMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
            var sourceMaterial = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialSourcePath);
            if (sourceMaterial == null)
                throw new InvalidOperationException($"Could not load road material source '{RoadMaterialSourcePath}'.");

            if (material == null)
            {
                material = new Material(sourceMaterial)
                {
                    name = "GameMLNightRoad",
                };
                AssetDatabase.CreateAsset(material, RoadMaterialPath);
            }
            else
            {
                material.CopyPropertiesFromMaterial(sourceMaterial);
                material.shader = sourceMaterial.shader;
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.63f, 0.67f, 0.75f, 1f));
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(0.63f, 0.67f, 0.75f, 1f));
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.28f);
            if (material.HasProperty("_GlossyReflections"))
                material.SetFloat("_GlossyReflections", 1f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(0.04f, 0.045f, 0.055f, 1f));
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static void RebuildOptionalPreview(Transform optionalRoot, Transform criticalRoot)
        {
            ClearChildren(optionalRoot);

            var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreePrefabPath);
            var lanternPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefabPath);
            if (treePrefab == null)
                throw new InvalidOperationException($"Could not load tree prefab '{TreePrefabPath}'.");
            if (lanternPrefab == null)
                throw new InvalidOperationException($"Could not load lantern prefab '{LanternPrefabPath}'.");

            var darknessMaterial = EnsureDarknessMaterial();
            var outerDarknessMaterial = EnsureOuterDarknessMaterial();
            var forestVolumeMaterial = EnsureForestVolumeMaterial();
            var lanternGlowMaterial = EnsureLanternRoadGlowMaterial();
            var roadWashMaterial = EnsureRoadWashMaterial();
            var roadShoulderMaterial = EnsureRoadShoulderMaterial();

            var darknessRoot = CreateChild(optionalRoot, "DarknessFields");
            var treeRoot = CreateChild(optionalRoot, "TreeLines");
            var forestRoot = CreateChild(optionalRoot, "ForestVolumes");
            var fortressBackdropRoot = CreateChild(optionalRoot, "FortressBackdrops");
            var roadShoulderRoot = CreateChild(optionalRoot, "RoadShoulders");
            var roadWashRoot = CreateChild(optionalRoot, "RoadWash");
            var lanternRoot = CreateChild(optionalRoot, "RoadLanterns");
            var glowRoot = CreateChild(optionalRoot, "RoadLightPools");

            var layout = CaptureRoadLayout(criticalRoot);
            float darknessY = layout.MineBounds.center.y - 0.35f;
            float darknessBehindTrees = TreeOffset + DarknessBehindTreesOffset;
            CreateInnerQuadrantDarkness(darknessRoot, layout, darknessY, darknessMaterial);

            CreateWrappedOuterDarkness(
                darknessRoot,
                layout,
                darknessY,
                darknessBehindTrees,
                outerDarknessMaterial);
            CreateFortressBackdropCurtains(
                fortressBackdropRoot,
                layout,
                forestVolumeMaterial);

            foreach (var road in layout.AllRoads)
                AddTreesForRoad(treeRoot, treePrefab, road, layout.Fortresses);

            PopulateForestQuadrants(forestRoot, forestVolumeMaterial, layout);
            PopulateExteriorForestBands(forestRoot, forestVolumeMaterial, layout);

            foreach (var road in layout.AllRoads)
                CreateRoadShoulderBands(roadShoulderRoot, road, roadShoulderMaterial);

            foreach (var road in layout.AllRoads)
                CreateRoadVisibilityWash(roadWashRoot, road, roadWashMaterial);

            foreach (var road in layout.OuterRoads)
                AddLanternsForRoad(glowRoot, lanternRoot, lanternPrefab, lanternGlowMaterial, road, layout.CenterX, layout.CenterZ, outerRoad: true);
            foreach (var road in layout.CenterRoads)
                AddLanternsForRoad(glowRoot, lanternRoot, lanternPrefab, lanternGlowMaterial, road, layout.CenterX, layout.CenterZ, outerRoad: false);

            StripColliders(optionalRoot);
        }

        static RoadLayout CaptureRoadLayout(Transform criticalRoot)
        {
            var redLeft = BuildRoadMetrics(criticalRoot, "Red_Left_Road");
            var redRight = BuildRoadMetrics(criticalRoot, "Red_Right_Road");
            var yellowLeft = BuildRoadMetrics(criticalRoot, "Yellow_Left_Road");
            var yellowRight = BuildRoadMetrics(criticalRoot, "Yellow_Right_Road");
            var blueLeft = BuildRoadMetrics(criticalRoot, "Blue_Left_Road");
            var blueRight = BuildRoadMetrics(criticalRoot, "Blue_Right_Road");
            var greenLeft = BuildRoadMetrics(criticalRoot, "Green_Left_Road");
            var greenRight = BuildRoadMetrics(criticalRoot, "Green_Right_road");

            var playerLeft = BuildRoadMetrics(criticalRoot, "PB_Red_Player_Lane");
            var playerRight = BuildRoadMetrics(criticalRoot, "Yellow_Player_Lane");
            var playerBottom = BuildRoadMetrics(criticalRoot, "PB_Blue_Player_Lane");
            var playerTop = BuildRoadMetrics(criticalRoot, "PB_Green_Player_Lane");
            var fortresses = CaptureFortressBounds(criticalRoot);

            var mineCenter = FindChildRecursive(criticalRoot, "PB_Mine_Center");
            if (mineCenter == null)
                throw new InvalidOperationException("Could not locate PB_Mine_Center in the critical environment preview.");

            var mineRenderer = mineCenter.GetComponent<Renderer>();
            if (mineRenderer == null)
                throw new InvalidOperationException("PB_Mine_Center is missing a Renderer.");

            float leftInnerX = Mathf.Max(redLeft.Bounds.max.x, redRight.Bounds.max.x);
            float rightInnerX = Mathf.Min(yellowLeft.Bounds.min.x, yellowRight.Bounds.min.x);
            float topInnerZ = Mathf.Min(greenLeft.Bounds.min.z, greenRight.Bounds.min.z);
            float bottomInnerZ = Mathf.Max(blueLeft.Bounds.max.z, blueRight.Bounds.max.z);

            float leftOuterX = Mathf.Min(redLeft.Bounds.min.x, redRight.Bounds.min.x);
            float rightOuterX = Mathf.Max(yellowLeft.Bounds.max.x, yellowRight.Bounds.max.x);
            float topOuterZ = Mathf.Max(greenLeft.Bounds.max.z, greenRight.Bounds.max.z);
            float bottomOuterZ = Mathf.Min(blueLeft.Bounds.min.z, blueRight.Bounds.min.z);
            float groundY = new[]
            {
                mineRenderer.bounds.min.y,
                redLeft.Bounds.min.y,
                redRight.Bounds.min.y,
                yellowLeft.Bounds.min.y,
                yellowRight.Bounds.min.y,
                blueLeft.Bounds.min.y,
                blueRight.Bounds.min.y,
                greenLeft.Bounds.min.y,
                greenRight.Bounds.min.y,
                playerLeft.Bounds.min.y,
                playerRight.Bounds.min.y,
                playerBottom.Bounds.min.y,
                playerTop.Bounds.min.y,
            }.Min();

            return new RoadLayout
            {
                CenterX = mineRenderer.bounds.center.x,
                CenterZ = mineRenderer.bounds.center.z,
                LeftInnerX = leftInnerX,
                RightInnerX = rightInnerX,
                TopInnerZ = topInnerZ,
                BottomInnerZ = bottomInnerZ,
                LeftOuterX = leftOuterX,
                RightOuterX = rightOuterX,
                TopOuterZ = topOuterZ,
                BottomOuterZ = bottomOuterZ,
                HorizontalRoadZMin = Mathf.Min(playerLeft.Bounds.min.z, playerRight.Bounds.min.z),
                HorizontalRoadZMax = Mathf.Max(playerLeft.Bounds.max.z, playerRight.Bounds.max.z),
                VerticalRoadXMin = Mathf.Min(playerBottom.Bounds.min.x, playerTop.Bounds.min.x),
                VerticalRoadXMax = Mathf.Max(playerBottom.Bounds.max.x, playerTop.Bounds.max.x),
                GroundY = groundY,
                MineBounds = mineRenderer.bounds,
                Fortresses = fortresses,
                CenterRoads = new[]
                {
                    playerLeft,
                    playerRight,
                    playerBottom,
                    playerTop,
                },
                OuterRoads = new[]
                {
                    redLeft,
                    redRight,
                    yellowLeft,
                    yellowRight,
                    blueLeft,
                    blueRight,
                    greenLeft,
                    greenRight,
                },
            };
        }

        static RoadMetrics BuildRoadMetrics(Transform root, string name)
        {
            var target = FindChildRecursive(root, name);
            if (target == null)
                throw new InvalidOperationException($"Could not locate road object '{name}'.");

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
                throw new InvalidOperationException($"Road object '{name}' is missing a Renderer.");

            var bounds = renderer.bounds;
            return new RoadMetrics
            {
                Name = name,
                Transform = target,
                Bounds = bounds,
                IsHorizontal = bounds.size.x >= bounds.size.z,
            };
        }

        static FortressBounds[] CaptureFortressBounds(Transform root)
        {
            string[] fortNames =
            {
                "Red Fort",
                "Yellow Fort",
                "Blue Fort",
                "Green Fort",
            };

            var fortresses = new List<FortressBounds>(fortNames.Length);
            foreach (string fortName in fortNames)
            {
                var fortRoot = FindChildRecursive(root, fortName);
                if (fortRoot == null)
                    throw new InvalidOperationException($"Could not locate fortress root '{fortName}'.");

                var renderers = fortRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    throw new InvalidOperationException($"Fortress root '{fortName}' is missing renderable geometry.");

                var fullBounds = CaptureCombinedBounds(renderers);
                var perimeterRenderers = renderers
                    .Where(renderer => renderer != null && IsFortressPerimeterRenderer(renderer.transform, fortRoot))
                    .ToArray();
                var darknessBounds = perimeterRenderers.Length > 0
                    ? CaptureCombinedBounds(perimeterRenderers)
                    : fullBounds;

                fortresses.Add(new FortressBounds
                {
                    Name = fortName,
                    Bounds = fullBounds,
                    DarknessBounds = darknessBounds,
                });
            }

            return fortresses.ToArray();
        }

        static Bounds CaptureCombinedBounds(Renderer[] renderers)
        {
            if (renderers == null || renderers.Length == 0)
                throw new InvalidOperationException("Cannot capture bounds from an empty renderer collection.");

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        static bool IsFortressPerimeterRenderer(Transform candidate, Transform fortRoot)
        {
            for (var current = candidate; current != null; current = current.parent)
            {
                if (IsFortressPerimeterName(current.name))
                    return true;
                if (current == fortRoot)
                    break;
            }

            return false;
        }

        static bool IsFortressPerimeterName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return ContainsIgnoreCase(name, "Wall") ||
                   ContainsIgnoreCase(name, "Gate") ||
                   ContainsIgnoreCase(name, "Walls") ||
                   ContainsIgnoreCase(name, "Tower_Front") ||
                   ContainsIgnoreCase(name, "Tower_Back") ||
                   ContainsIgnoreCase(name, "Tower_Rear") ||
                   ContainsIgnoreCase(name, "Tower_Left_Side") ||
                   ContainsIgnoreCase(name, "Tower_Right_Side");
        }

        static bool ContainsIgnoreCase(string value, string token)
        {
            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static void AddTreesForRoad(Transform parent, GameObject treePrefab, RoadMetrics road, FortressBounds[] fortresses)
        {
            if (road.IsHorizontal)
            {
                float xMin = road.Bounds.min.x + TreeTrim;
                float xMax = road.Bounds.max.x - TreeTrim;
                if (xMax <= xMin)
                    return;

                AddTreesAlongSegment(
                    parent,
                    treePrefab,
                    $"{road.Name}_North",
                    new Vector3(xMin, 0f, road.Bounds.min.z - TreeOffset),
                    new Vector3(xMax, 0f, road.Bounds.min.z - TreeOffset),
                    new Vector3(0f, 0f, -1f),
                    fortresses);
                AddTreesAlongSegment(
                    parent,
                    treePrefab,
                    $"{road.Name}_South",
                    new Vector3(xMin, 0f, road.Bounds.max.z + TreeOffset),
                    new Vector3(xMax, 0f, road.Bounds.max.z + TreeOffset),
                    new Vector3(0f, 0f, 1f),
                    fortresses);
                return;
            }

            float zMin = road.Bounds.min.z + TreeTrim;
            float zMax = road.Bounds.max.z - TreeTrim;
            if (zMax <= zMin)
                return;

            AddTreesAlongSegment(
                parent,
                treePrefab,
                $"{road.Name}_West",
                new Vector3(road.Bounds.min.x - TreeOffset, 0f, zMin),
                new Vector3(road.Bounds.min.x - TreeOffset, 0f, zMax),
                new Vector3(-1f, 0f, 0f),
                fortresses);
            AddTreesAlongSegment(
                parent,
                treePrefab,
                $"{road.Name}_East",
                new Vector3(road.Bounds.max.x + TreeOffset, 0f, zMin),
                new Vector3(road.Bounds.max.x + TreeOffset, 0f, zMax),
                new Vector3(1f, 0f, 0f),
                fortresses);
        }

        static void AddTreesAlongSegment(
            Transform parent,
            GameObject treePrefab,
            string prefix,
            Vector3 start,
            Vector3 end,
            Vector3 outwardNormal,
            FortressBounds[] fortresses)
        {
            var delta = end - start;
            float length = delta.magnitude;
            int count = Mathf.Max(3, Mathf.RoundToInt(length / 60f));
            var tangent = delta.normalized;
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.right;

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (count - 1f);
                var position = Vector3.Lerp(start, end, t);
                position += outwardNormal * (TreeRoadSideDrift + ((i % 2) * TreeRoadSideAlternate));
                position += tangent * (((i % 3) - 1) * TreeRoadTangentJitter);
                position = MovePointOutsideFortresses(position, fortresses);

                float scale = Mathf.Max(0.9f, TreeBaseScale + (((i % 4) - 1.5f) * TreeScaleVariation));
                float yaw = 17f + (i * 41f);
                var tree = InstantiateNestedPrefab(treePrefab, parent, $"{prefix}_{i + 1:00}");
                tree.transform.position = position;
                tree.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                tree.transform.localScale = Vector3.one * scale;

                SetRendererShadows(tree, true, true);
            }
        }

        static Vector3 MovePointOutsideFortresses(Vector3 position, FortressBounds[] fortresses)
        {
            if (fortresses == null || fortresses.Length == 0)
                return position;

            Vector3 adjusted = position;
            for (int i = 0; i < fortresses.Length; i++)
            {
                var bounds = fortresses[i].Bounds;
                if (!ContainsXZ(bounds, adjusted))
                    continue;

                adjusted = MovePointOutsideBoundsXZ(adjusted, bounds, FortressTreeOutsidePadding);
            }

            return adjusted;
        }

        static void CreateFortressPerimeterDarkness(
            Transform parent,
            RoadLayout layout,
            float y,
            Material material)
        {
            if (layout.Fortresses == null || layout.Fortresses.Length == 0)
                return;

            for (int i = 0; i < layout.Fortresses.Length; i++)
                CreateFortressPerimeterDarkness(parent, layout, layout.Fortresses[i], y, material);
        }

        static void CreateFortressPerimeterDarkness(
            Transform parent,
            RoadLayout layout,
            FortressBounds fortress,
            float y,
            Material material)
        {
            var bounds = ExpandBoundsXZ(fortress.DarknessBounds, FortressPerimeterBoundsPadding);
            string safeName = fortress.Name.Replace(' ', '_');
            switch (ResolveFortressCompass(layout, fortress.DarknessBounds))
            {
                case FortressCompass.West:
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_Rear",
                        DarknessDirection.West,
                        bounds.min.x,
                        y,
                        bounds.min.z - FortressPerimeterCrossPadding,
                        bounds.max.z + FortressPerimeterCrossPadding,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_NorthFlank",
                        DarknessDirection.North,
                        bounds.max.z,
                        y,
                        bounds.min.x - FortressPerimeterRearExtension,
                        bounds.max.x + FortressPerimeterFrontExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_SouthFlank",
                        DarknessDirection.South,
                        bounds.min.z,
                        y,
                        bounds.min.x - FortressPerimeterRearExtension,
                        bounds.max.x + FortressPerimeterFrontExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    break;

                case FortressCompass.East:
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_Rear",
                        DarknessDirection.East,
                        bounds.max.x,
                        y,
                        bounds.min.z - FortressPerimeterCrossPadding,
                        bounds.max.z + FortressPerimeterCrossPadding,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_NorthFlank",
                        DarknessDirection.North,
                        bounds.max.z,
                        y,
                        bounds.min.x - FortressPerimeterFrontExtension,
                        bounds.max.x + FortressPerimeterRearExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_SouthFlank",
                        DarknessDirection.South,
                        bounds.min.z,
                        y,
                        bounds.min.x - FortressPerimeterFrontExtension,
                        bounds.max.x + FortressPerimeterRearExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    break;

                case FortressCompass.North:
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_Rear",
                        DarknessDirection.North,
                        bounds.max.z,
                        y,
                        bounds.min.x - FortressPerimeterCrossPadding,
                        bounds.max.x + FortressPerimeterCrossPadding,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_WestFlank",
                        DarknessDirection.West,
                        bounds.min.x,
                        y,
                        bounds.min.z - FortressPerimeterFrontExtension,
                        bounds.max.z + FortressPerimeterRearExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_EastFlank",
                        DarknessDirection.East,
                        bounds.max.x,
                        y,
                        bounds.min.z - FortressPerimeterFrontExtension,
                        bounds.max.z + FortressPerimeterRearExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    break;

                case FortressCompass.South:
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_Rear",
                        DarknessDirection.South,
                        bounds.min.z,
                        y,
                        bounds.min.x - FortressPerimeterCrossPadding,
                        bounds.max.x + FortressPerimeterCrossPadding,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_WestFlank",
                        DarknessDirection.West,
                        bounds.min.x,
                        y,
                        bounds.min.z - FortressPerimeterRearExtension,
                        bounds.max.z + FortressPerimeterFrontExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    CreateFortressPerimeterStack(
                        parent,
                        $"{safeName}_Darkness_EastFlank",
                        DarknessDirection.East,
                        bounds.max.x,
                        y,
                        bounds.min.z - FortressPerimeterRearExtension,
                        bounds.max.z + FortressPerimeterFrontExtension,
                        FortressPerimeterDarknessDepth,
                        material);
                    break;
            }
        }

        static void CreateFortressPerimeterStack(
            Transform parent,
            string namePrefix,
            DarknessDirection direction,
            float innerEdge,
            float y,
            float crossMin,
            float crossMax,
            float totalSpan,
            Material material)
        {
            if (crossMax <= crossMin)
                return;

            CreateAnchoredDarknessStack(
                parent,
                namePrefix,
                direction,
                innerEdge,
                y,
                (crossMin + crossMax) * 0.5f,
                crossMax - crossMin,
                totalSpan,
                material);
        }

        static FortressCompass ResolveFortressCompass(RoadLayout layout, Bounds bounds)
        {
            float deltaX = bounds.center.x - layout.CenterX;
            float deltaZ = bounds.center.z - layout.CenterZ;
            if (Mathf.Abs(deltaX) >= Mathf.Abs(deltaZ))
                return deltaX <= 0f ? FortressCompass.West : FortressCompass.East;

            return deltaZ <= 0f ? FortressCompass.South : FortressCompass.North;
        }

        static void CreateFortressBackdropCurtains(
            Transform parent,
            RoadLayout layout,
            Material material)
        {
            if (parent == null || material == null || layout.Fortresses == null || layout.Fortresses.Length == 0)
                return;

            float centerY = layout.GroundY + (FortressBackdropHeight * 0.5f) - ForestVolumeGroundSink;
            for (int i = 0; i < layout.Fortresses.Length; i++)
            {
                CreateFortressBackdropCurtains(
                    parent,
                    layout,
                    layout.Fortresses[i],
                    centerY,
                    material);
            }
        }

        static void CreateFortressBackdropCurtains(
            Transform parent,
            RoadLayout layout,
            FortressBounds fortress,
            float centerY,
            Material material)
        {
            var bounds = ExpandBoundsXZ(fortress.DarknessBounds, FortressPerimeterBoundsPadding);
            string safeName = fortress.Name.Replace(' ', '_');
            switch (ResolveFortressCompass(layout, fortress.DarknessBounds))
            {
                case FortressCompass.West:
                    CreateLinearBackdropStack(
                        parent,
                        $"{safeName}_Backdrop_Rear",
                        new Vector3(
                            bounds.min.x - FortressBackdropRearDistance,
                            centerY,
                            (bounds.min.z + bounds.max.z) * 0.5f),
                        Vector3.left * FortressBackdropLayerSpacing,
                        Quaternion.Euler(0f, 90f, 0f),
                        (bounds.max.z - bounds.min.z) + (FortressBackdropCrossPadding * 2f),
                        material);
                    CreateWestEastFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        northSide: true,
                        fortIsWest: true);
                    CreateWestEastFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        northSide: false,
                        fortIsWest: true);
                    break;

                case FortressCompass.East:
                    CreateLinearBackdropStack(
                        parent,
                        $"{safeName}_Backdrop_Rear",
                        new Vector3(
                            bounds.max.x + FortressBackdropRearDistance,
                            centerY,
                            (bounds.min.z + bounds.max.z) * 0.5f),
                        Vector3.right * FortressBackdropLayerSpacing,
                        Quaternion.Euler(0f, -90f, 0f),
                        (bounds.max.z - bounds.min.z) + (FortressBackdropCrossPadding * 2f),
                        material);
                    CreateWestEastFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        northSide: true,
                        fortIsWest: false);
                    CreateWestEastFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        northSide: false,
                        fortIsWest: false);
                    break;

                case FortressCompass.North:
                    CreateLinearBackdropStack(
                        parent,
                        $"{safeName}_Backdrop_Rear",
                        new Vector3(
                            (bounds.min.x + bounds.max.x) * 0.5f,
                            centerY,
                            bounds.max.z + FortressBackdropRearDistance),
                        Vector3.forward * FortressBackdropLayerSpacing,
                        Quaternion.Euler(0f, 180f, 0f),
                        (bounds.max.x - bounds.min.x) + (FortressBackdropCrossPadding * 2f),
                        material);
                    CreateNorthSouthFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        eastSide: false,
                        fortIsNorth: true);
                    CreateNorthSouthFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        eastSide: true,
                        fortIsNorth: true);
                    break;

                case FortressCompass.South:
                    CreateLinearBackdropStack(
                        parent,
                        $"{safeName}_Backdrop_Rear",
                        new Vector3(
                            (bounds.min.x + bounds.max.x) * 0.5f,
                            centerY,
                            bounds.min.z - FortressBackdropRearDistance),
                        Vector3.back * FortressBackdropLayerSpacing,
                        Quaternion.identity,
                        (bounds.max.x - bounds.min.x) + (FortressBackdropCrossPadding * 2f),
                        material);
                    CreateNorthSouthFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        eastSide: false,
                        fortIsNorth: false);
                    CreateNorthSouthFortSideBackdrop(
                        parent,
                        safeName,
                        bounds,
                        centerY,
                        material,
                        eastSide: true,
                        fortIsNorth: false);
                    break;
            }
        }

        static void CreateWestEastFortSideBackdrop(
            Transform parent,
            string namePrefix,
            Bounds bounds,
            float centerY,
            Material material,
            bool northSide,
            bool fortIsWest)
        {
            float frontLimit = fortIsWest
                ? bounds.max.x - FortressBackdropRoadRetreat
                : bounds.min.x + FortressBackdropRoadRetreat;
            float sideStart = fortIsWest
                ? bounds.min.x - FortressBackdropRearExtension
                : bounds.min.x + FortressBackdropRoadRetreat;
            float sideEnd = fortIsWest
                ? bounds.max.x - FortressBackdropRoadRetreat
                : bounds.max.x + FortressBackdropRearExtension;
            if (!fortIsWest)
            {
                sideStart = bounds.min.x + FortressBackdropRoadRetreat;
                sideEnd = bounds.max.x + FortressBackdropRearExtension;
            }

            float sideWidth = sideEnd - sideStart;
            if (sideWidth <= 0f)
                return;

            float z = northSide
                ? bounds.max.z + FortressBackdropSideDistance
                : bounds.min.z - FortressBackdropSideDistance;
            Quaternion rotation = northSide ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            CreateLinearBackdropStack(
                parent,
                $"{namePrefix}_Backdrop_{(northSide ? "North" : "South")}",
                new Vector3((sideStart + sideEnd) * 0.5f, centerY, z),
                (northSide ? Vector3.forward : Vector3.back) * FortressBackdropLayerSpacing,
                rotation,
                sideWidth,
                material);

            float returnX = frontLimit;
            float returnZCenter = northSide
                ? bounds.max.z + (FortressBackdropSideDistance * 0.5f)
                : bounds.min.z - (FortressBackdropSideDistance * 0.5f);
            CreateLinearBackdropStack(
                parent,
                $"{namePrefix}_Backdrop_{(northSide ? "North" : "South")}_Return",
                new Vector3(returnX, centerY, returnZCenter),
                (fortIsWest ? Vector3.left : Vector3.right) * FortressBackdropLayerSpacing,
                fortIsWest ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.Euler(0f, -90f, 0f),
                FortressBackdropCornerReturnDepth + FortressBackdropSideDistance,
                material);
        }

        static void CreateNorthSouthFortSideBackdrop(
            Transform parent,
            string namePrefix,
            Bounds bounds,
            float centerY,
            Material material,
            bool eastSide,
            bool fortIsNorth)
        {
            float sideStart = fortIsNorth
                ? bounds.min.z + FortressBackdropRoadRetreat
                : bounds.min.z - FortressBackdropRearExtension;
            float sideEnd = fortIsNorth
                ? bounds.max.z + FortressBackdropRearExtension
                : bounds.max.z - FortressBackdropRoadRetreat;
            if (!fortIsNorth)
            {
                sideStart = bounds.min.z - FortressBackdropRearExtension;
                sideEnd = bounds.max.z - FortressBackdropRoadRetreat;
            }

            float sideWidth = sideEnd - sideStart;
            if (sideWidth <= 0f)
                return;

            float x = eastSide
                ? bounds.max.x + FortressBackdropSideDistance
                : bounds.min.x - FortressBackdropSideDistance;
            Quaternion rotation = eastSide ? Quaternion.Euler(0f, -90f, 0f) : Quaternion.Euler(0f, 90f, 0f);
            CreateLinearBackdropStack(
                parent,
                $"{namePrefix}_Backdrop_{(eastSide ? "East" : "West")}",
                new Vector3(x, centerY, (sideStart + sideEnd) * 0.5f),
                (eastSide ? Vector3.right : Vector3.left) * FortressBackdropLayerSpacing,
                rotation,
                sideWidth,
                material);

            float returnZ = fortIsNorth
                ? bounds.min.z + FortressBackdropRoadRetreat
                : bounds.max.z - FortressBackdropRoadRetreat;
            float returnXCenter = eastSide
                ? bounds.max.x + (FortressBackdropSideDistance * 0.5f)
                : bounds.min.x - (FortressBackdropSideDistance * 0.5f);
            CreateLinearBackdropStack(
                parent,
                $"{namePrefix}_Backdrop_{(eastSide ? "East" : "West")}_Return",
                new Vector3(returnXCenter, centerY, returnZ),
                (fortIsNorth ? Vector3.forward : Vector3.back) * FortressBackdropLayerSpacing,
                fortIsNorth ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity,
                FortressBackdropCornerReturnDepth + FortressBackdropSideDistance,
                material);
        }

        static void CreateLinearBackdropStack(
            Transform parent,
            string namePrefix,
            Vector3 baseCenter,
            Vector3 layerOffset,
            Quaternion rotation,
            float width,
            Material material)
        {
            if (width <= 0f)
                return;

            for (int i = 0; i < FortressBackdropLayerCount; i++)
            {
                float layerHeight = FortressBackdropHeight - (i * 2.5f);
                if (layerHeight <= 4f)
                    break;

                CreateForestQuad(
                    parent,
                    $"{namePrefix}_{i + 1:00}",
                    baseCenter + (layerOffset * i) + (Vector3.up * (i * 0.6f)),
                    rotation,
                    width + (i * 4f),
                    layerHeight,
                    material);
            }
        }

        static void CreateWrappedOuterDarkness(
            Transform parent,
            RoadLayout layout,
            float y,
            float darknessBehindTrees,
            Material material)
        {
            float westInnerEdge = layout.LeftOuterX - darknessBehindTrees;
            float eastInnerEdge = layout.RightOuterX + darknessBehindTrees;
            float northInnerEdge = layout.TopOuterZ + darknessBehindTrees;
            float southInnerEdge = layout.BottomOuterZ - darknessBehindTrees;

            float crossZMin = layout.BottomOuterZ - OuterDarknessCrossPadding;
            float crossZMax = layout.TopOuterZ + OuterDarknessCrossPadding;
            float crossXMin = layout.LeftOuterX - OuterDarknessCrossPadding;
            float crossXMax = layout.RightOuterX + OuterDarknessCrossPadding;

            CreateWrappedAnchoredDarknessStack(
                parent,
                "Darkness_West",
                DarknessDirection.West,
                westInnerEdge,
                y,
                crossZMin,
                crossZMax,
                DarknessOuterSpan,
                material,
                layout.Fortresses);
            CreateWrappedAnchoredDarknessStack(
                parent,
                "Darkness_East",
                DarknessDirection.East,
                eastInnerEdge,
                y,
                crossZMin,
                crossZMax,
                DarknessOuterSpan,
                material,
                layout.Fortresses);
            CreateWrappedAnchoredDarknessStack(
                parent,
                "Darkness_North",
                DarknessDirection.North,
                northInnerEdge,
                y,
                crossXMin,
                crossXMax,
                DarknessOuterSpan,
                material,
                layout.Fortresses);
            CreateWrappedAnchoredDarknessStack(
                parent,
                "Darkness_South",
                DarknessDirection.South,
                southInnerEdge,
                y,
                crossXMin,
                crossXMax,
                DarknessOuterSpan,
                material,
                layout.Fortresses);
        }

        static void CreateWrappedAnchoredDarknessStack(
            Transform parent,
            string namePrefix,
            DarknessDirection direction,
            float innerEdge,
            float y,
            float crossMin,
            float crossMax,
            float totalSpan,
            Material material,
            FortressBounds[] fortresses)
        {
            if (crossMax <= crossMin)
                return;

            var segments = BuildOuterDarknessSegments(direction, innerEdge, crossMin, crossMax, totalSpan, fortresses);
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                CreateAnchoredDarknessStack(
                    parent,
                    $"{namePrefix}_{i + 1:00}",
                    direction,
                    innerEdge,
                    y,
                    (segment.Min + segment.Max) * 0.5f,
                    segment.Max - segment.Min,
                    totalSpan,
                    material);
            }
        }

        static List<Range1D> BuildOuterDarknessSegments(
            DarknessDirection direction,
            float innerEdge,
            float crossMin,
            float crossMax,
            float totalSpan,
            FortressBounds[] fortresses)
        {
            var exclusions = new List<Range1D>();
            float outerEdge = direction switch
            {
                DarknessDirection.West => innerEdge - totalSpan,
                DarknessDirection.East => innerEdge + totalSpan,
                DarknessDirection.North => innerEdge + totalSpan,
                DarknessDirection.South => innerEdge - totalSpan,
                _ => innerEdge,
            };

            if (fortresses != null)
            {
                for (int i = 0; i < fortresses.Length; i++)
                {
                    var expanded = ExpandBoundsXZ(fortresses[i].Bounds, FortressDarknessWrapPadding);
                    if (fortresses[i].DarknessBounds.size.sqrMagnitude > 0f)
                        expanded = ExpandBoundsXZ(fortresses[i].DarknessBounds, FortressDarknessWrapPadding);
                    if (!FortressOverlapsDarkness(direction, expanded, innerEdge, outerEdge))
                        continue;

                    float intervalMin = direction is DarknessDirection.West or DarknessDirection.East
                        ? expanded.min.z
                        : expanded.min.x;
                    float intervalMax = direction is DarknessDirection.West or DarknessDirection.East
                        ? expanded.max.z
                        : expanded.max.x;
                    intervalMin = Mathf.Max(intervalMin, crossMin);
                    intervalMax = Mathf.Min(intervalMax, crossMax);
                    if (intervalMax <= intervalMin)
                        continue;

                    exclusions.Add(new Range1D { Min = intervalMin, Max = intervalMax });
                }
            }

            exclusions.Sort((a, b) => a.Min.CompareTo(b.Min));

            var segments = new List<Range1D>();
            float cursor = crossMin;
            for (int i = 0; i < exclusions.Count; i++)
            {
                var exclusion = exclusions[i];
                if (exclusion.Max <= cursor)
                    continue;

                if (exclusion.Min > cursor)
                    segments.Add(new Range1D { Min = cursor, Max = exclusion.Min });

                cursor = Mathf.Max(cursor, exclusion.Max);
            }

            if (cursor < crossMax)
                segments.Add(new Range1D { Min = cursor, Max = crossMax });

            return segments;
        }

        static List<Range1D> BuildOuterForestSegments(
            DarknessDirection direction,
            float innerEdge,
            float crossMin,
            float crossMax,
            float totalSpan,
            FortressBounds[] fortresses)
        {
            var exclusions = new List<Range1D>();
            float outerEdge = direction switch
            {
                DarknessDirection.West => innerEdge - totalSpan,
                DarknessDirection.East => innerEdge + totalSpan,
                DarknessDirection.North => innerEdge + totalSpan,
                DarknessDirection.South => innerEdge - totalSpan,
                _ => innerEdge,
            };

            if (fortresses != null)
            {
                for (int i = 0; i < fortresses.Length; i++)
                {
                    var expanded = ExpandBoundsXZ(fortresses[i].Bounds, FortressForestWrapPadding);
                    if (!FortressOverlapsDarkness(direction, expanded, innerEdge, outerEdge))
                        continue;

                    float intervalMin = direction is DarknessDirection.West or DarknessDirection.East
                        ? expanded.min.z
                        : expanded.min.x;
                    float intervalMax = direction is DarknessDirection.West or DarknessDirection.East
                        ? expanded.max.z
                        : expanded.max.x;
                    intervalMin = Mathf.Max(intervalMin, crossMin);
                    intervalMax = Mathf.Min(intervalMax, crossMax);
                    if (intervalMax <= intervalMin)
                        continue;

                    exclusions.Add(new Range1D { Min = intervalMin, Max = intervalMax });
                }
            }

            exclusions.Sort((a, b) => a.Min.CompareTo(b.Min));

            var segments = new List<Range1D>();
            float cursor = crossMin;
            for (int i = 0; i < exclusions.Count; i++)
            {
                var exclusion = exclusions[i];
                if (exclusion.Max <= cursor)
                    continue;

                if (exclusion.Min > cursor)
                    segments.Add(new Range1D { Min = cursor, Max = exclusion.Min });

                cursor = Mathf.Max(cursor, exclusion.Max);
            }

            if (cursor < crossMax)
                segments.Add(new Range1D { Min = cursor, Max = crossMax });

            return segments;
        }

        static bool FortressOverlapsDarkness(DarknessDirection direction, Bounds fortressBounds, float innerEdge, float outerEdge)
        {
            return direction switch
            {
                DarknessDirection.West => fortressBounds.min.x <= innerEdge && fortressBounds.max.x >= outerEdge,
                DarknessDirection.East => fortressBounds.max.x >= innerEdge && fortressBounds.min.x <= outerEdge,
                DarknessDirection.North => fortressBounds.max.z >= innerEdge && fortressBounds.min.z <= outerEdge,
                DarknessDirection.South => fortressBounds.min.z <= innerEdge && fortressBounds.max.z >= outerEdge,
                _ => false,
            };
        }

        static Bounds ExpandBoundsXZ(Bounds bounds, float padding)
        {
            bounds.Expand(new Vector3(padding * 2f, 0f, padding * 2f));
            return bounds;
        }

        static bool ContainsXZ(Bounds bounds, Vector3 point)
        {
            return point.x >= bounds.min.x &&
                   point.x <= bounds.max.x &&
                   point.z >= bounds.min.z &&
                   point.z <= bounds.max.z;
        }

        static Vector3 MovePointOutsideBoundsXZ(Vector3 position, Bounds bounds, float padding)
        {
            float left = Mathf.Abs(position.x - bounds.min.x);
            float right = Mathf.Abs(bounds.max.x - position.x);
            float bottom = Mathf.Abs(position.z - bounds.min.z);
            float top = Mathf.Abs(bounds.max.z - position.z);

            float minDistance = Mathf.Min(left, right, bottom, top);
            if (Mathf.Approximately(minDistance, left))
            {
                position.x = bounds.min.x - padding;
                return position;
            }

            if (Mathf.Approximately(minDistance, right))
            {
                position.x = bounds.max.x + padding;
                return position;
            }

            if (Mathf.Approximately(minDistance, bottom))
            {
                position.z = bounds.min.z - padding;
                return position;
            }

            position.z = bounds.max.z + padding;
            return position;
        }

        static void PopulateForestQuadrants(Transform parent, Material material, RoadLayout layout)
        {
            CreateForestVolumeBlock(
                parent,
                "Forest_NW",
                layout.LeftInnerX,
                layout.VerticalRoadXMin,
                layout.HorizontalRoadZMax,
                layout.TopInnerZ,
                layout,
                material,
                ForestVolumeInset);
            CreateForestVolumeBlock(
                parent,
                "Forest_NE",
                layout.VerticalRoadXMax,
                layout.RightInnerX,
                layout.HorizontalRoadZMax,
                layout.TopInnerZ,
                layout,
                material,
                ForestVolumeInset);
            CreateForestVolumeBlock(
                parent,
                "Forest_SW",
                layout.LeftInnerX,
                layout.VerticalRoadXMin,
                layout.BottomInnerZ,
                layout.HorizontalRoadZMin,
                layout,
                material,
                ForestVolumeInset);
            CreateForestVolumeBlock(
                parent,
                "Forest_SE",
                layout.VerticalRoadXMax,
                layout.RightInnerX,
                layout.BottomInnerZ,
                layout.HorizontalRoadZMin,
                layout,
                material,
                ForestVolumeInset);
        }

        static void PopulateExteriorForestBands(Transform parent, Material material, RoadLayout layout)
        {
            float bandSpan = ExteriorForestSpan - ExteriorForestRoadGap;
            CreateWrappedExteriorForestBand(
                parent,
                "Forest_Outer_West",
                DarknessDirection.West,
                layout.LeftOuterX - ExteriorForestRoadGap,
                layout.BottomOuterZ - ExteriorForestSpan,
                layout.TopOuterZ + ExteriorForestSpan,
                bandSpan,
                layout,
                material);
            CreateWrappedExteriorForestBand(
                parent,
                "Forest_Outer_East",
                DarknessDirection.East,
                layout.RightOuterX + ExteriorForestRoadGap,
                layout.BottomOuterZ - ExteriorForestSpan,
                layout.TopOuterZ + ExteriorForestSpan,
                bandSpan,
                layout,
                material);
            CreateWrappedExteriorForestBand(
                parent,
                "Forest_Outer_North",
                DarknessDirection.North,
                layout.TopOuterZ + ExteriorForestRoadGap,
                layout.LeftOuterX - ExteriorForestSpan,
                layout.RightOuterX + ExteriorForestSpan,
                bandSpan,
                layout,
                material);
            CreateWrappedExteriorForestBand(
                parent,
                "Forest_Outer_South",
                DarknessDirection.South,
                layout.BottomOuterZ - ExteriorForestRoadGap,
                layout.LeftOuterX - ExteriorForestSpan,
                layout.RightOuterX + ExteriorForestSpan,
                bandSpan,
                layout,
                material);
        }

        static void CreateWrappedExteriorForestBand(
            Transform parent,
            string namePrefix,
            DarknessDirection direction,
            float innerEdge,
            float crossMin,
            float crossMax,
            float totalSpan,
            RoadLayout layout,
            Material material)
        {
            if (crossMax <= crossMin || totalSpan <= 0f)
                return;

            var segments = BuildOuterForestSegments(direction, innerEdge, crossMin, crossMax, totalSpan, layout.Fortresses);
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                switch (direction)
                {
                    case DarknessDirection.West:
                        CreateForestVolumeBlock(
                            parent,
                            $"{namePrefix}_{i + 1:00}",
                            innerEdge - totalSpan,
                            innerEdge,
                            segment.Min,
                            segment.Max,
                            layout,
                            material,
                            ExteriorForestInset);
                        break;
                    case DarknessDirection.East:
                        CreateForestVolumeBlock(
                            parent,
                            $"{namePrefix}_{i + 1:00}",
                            innerEdge,
                            innerEdge + totalSpan,
                            segment.Min,
                            segment.Max,
                            layout,
                            material,
                            ExteriorForestInset);
                        break;
                    case DarknessDirection.North:
                        CreateForestVolumeBlock(
                            parent,
                            $"{namePrefix}_{i + 1:00}",
                            segment.Min,
                            segment.Max,
                            innerEdge,
                            innerEdge + totalSpan,
                            layout,
                            material,
                            ExteriorForestInset);
                        break;
                    case DarknessDirection.South:
                        CreateForestVolumeBlock(
                            parent,
                            $"{namePrefix}_{i + 1:00}",
                            segment.Min,
                            segment.Max,
                            innerEdge - totalSpan,
                            innerEdge,
                            layout,
                            material,
                            ExteriorForestInset);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
                }
            }
        }

        static void CreateForestVolumeBlock(
            Transform parent,
            string name,
            float xMin,
            float xMax,
            float zMin,
            float zMax,
            RoadLayout layout,
            Material material,
            float edgeInset)
        {
            float blockWidth = xMax - xMin;
            float blockDepth = zMax - zMin;
            if (blockWidth <= edgeInset * 2f || blockDepth <= edgeInset * 2f)
                return;

            float forestMinX = xMin + edgeInset;
            float forestMaxX = xMax - edgeInset;
            float forestMinZ = zMin + edgeInset;
            float forestMaxZ = zMax - edgeInset;
            float forestWidth = forestMaxX - forestMinX;
            float forestDepth = forestMaxZ - forestMinZ;
            if (forestWidth <= 8f || forestDepth <= 8f)
                return;

            var forestRoot = CreateChild(parent, name);
            float centerX = (forestMinX + forestMaxX) * 0.5f;
            float centerZ = (forestMinZ + forestMaxZ) * 0.5f;
            float xSign = centerX < layout.CenterX ? -1f : 1f;
            float zSign = centerZ < layout.CenterZ ? -1f : 1f;

            for (int i = 0; i < ForestCurtainLayerCount; i++)
            {
                float inset = ForestCurtainSpacing * i;
                float curtainMinX = forestMinX + inset;
                float curtainMaxX = forestMaxX - inset;
                float curtainMinZ = forestMinZ + inset;
                float curtainMaxZ = forestMaxZ - inset;
                float curtainWidth = curtainMaxX - curtainMinX;
                float curtainDepth = curtainMaxZ - curtainMinZ;
                float curtainHeight = ForestVolumeHeight - (ForestCurtainHeightStep * i);
                if (curtainWidth <= 8f || curtainDepth <= 8f || curtainHeight <= 4f)
                    break;

                float curtainCenterY = layout.GroundY + (curtainHeight * 0.5f) - ForestVolumeGroundSink + (i * 0.35f);
                CreateForestCurtainWithFortCutouts(
                    forestRoot,
                    $"{name}_Curtain_North_{i + 1:00}",
                    DarknessDirection.North,
                    curtainCenterY,
                    curtainMaxZ,
                    curtainMinX,
                    curtainMaxX,
                    curtainMinZ,
                    curtainMaxZ,
                    curtainHeight,
                    layout.Fortresses,
                    material);
                CreateForestCurtainWithFortCutouts(
                    forestRoot,
                    $"{name}_Curtain_South_{i + 1:00}",
                    DarknessDirection.South,
                    curtainCenterY,
                    curtainMinZ,
                    curtainMinX,
                    curtainMaxX,
                    curtainMinZ,
                    curtainMaxZ,
                    curtainHeight,
                    layout.Fortresses,
                    material);
                CreateForestCurtainWithFortCutouts(
                    forestRoot,
                    $"{name}_Curtain_West_{i + 1:00}",
                    DarknessDirection.West,
                    curtainCenterY,
                    curtainMinX,
                    curtainMinZ,
                    curtainMaxZ,
                    curtainMinX,
                    curtainMaxX,
                    curtainHeight,
                    layout.Fortresses,
                    material);
                CreateForestCurtainWithFortCutouts(
                    forestRoot,
                    $"{name}_Curtain_East_{i + 1:00}",
                    DarknessDirection.East,
                    curtainCenterY,
                    curtainMaxX,
                    curtainMinZ,
                    curtainMaxZ,
                    curtainMinX,
                    curtainMaxX,
                    curtainHeight,
                    layout.Fortresses,
                    material);
            }

            CreateForestInteriorSlices(
                forestRoot,
                name,
                layout,
                centerX,
                centerZ,
                xSign,
                zSign,
                forestMinX,
                forestMaxX,
                forestMinZ,
                forestMaxZ,
                forestWidth,
                forestDepth,
                material);

            for (int i = 0; i < ForestCanopyLayerCount; i++)
            {
                float inset = ForestCanopyInsetStep * i;
                float canopyWidth = forestWidth - (inset * 2f);
                float canopyDepth = forestDepth - (inset * 2f);
                if (canopyWidth <= 8f || canopyDepth <= 8f)
                    break;

                float canopyY = layout.GroundY + ForestVolumeHeight - 1.3f + (i * ForestCanopyHeightStep);
                float canopyYaw = (i * 37f) + ((xSign + zSign) * 6f);
                float canopyOffsetX = i == 1 ? xSign * 2.2f : (i == 2 ? -xSign * 1.6f : 0f);
                float canopyOffsetZ = i == 1 ? -zSign * 1.8f : (i == 2 ? zSign * 2.6f : 0f);
                CreateForestQuad(
                    forestRoot,
                    $"{name}_Canopy_{i + 1:00}",
                    new Vector3(centerX + canopyOffsetX, canopyY, centerZ + canopyOffsetZ),
                    Quaternion.Euler(90f, canopyYaw, 0f),
                    canopyWidth,
                    canopyDepth,
                    material);
            }
        }

        static void CreateForestInteriorSlices(
            Transform parent,
            string name,
            RoadLayout layout,
            float centerX,
            float centerZ,
            float xSign,
            float zSign,
            float forestMinX,
            float forestMaxX,
            float forestMinZ,
            float forestMaxZ,
            float forestWidth,
            float forestDepth,
            Material material)
        {
            float sliceMarginX = Mathf.Min(ForestInteriorSliceMargin, forestWidth * 0.18f);
            float sliceMarginZ = Mathf.Min(ForestInteriorSliceMargin, forestDepth * 0.18f);
            float baseCenterY = layout.GroundY + ((ForestVolumeHeight - 2f) * 0.5f) - ForestVolumeGroundSink + 0.45f;

            for (int i = 0; i < ForestInteriorSliceCount; i++)
            {
                float t = (i + 1f) / (ForestInteriorSliceCount + 1f);
                float centeredT = 1f - Mathf.Abs((t - 0.5f) * 2f);
                float sliceHeight = ForestVolumeHeight - 3f + (centeredT * ForestInteriorSliceHeightBoost);
                float sliceCenterY = baseCenterY + (centeredT * 0.8f);
                float x = Mathf.Lerp(forestMinX + sliceMarginX, forestMaxX - sliceMarginX, t);
                float z = Mathf.Lerp(forestMinZ + sliceMarginZ, forestMaxZ - sliceMarginZ, t);

                CreateForestQuad(
                    parent,
                    $"{name}_Slice_X_{i + 1:00}",
                    new Vector3(centerX + (xSign * centeredT * 1.4f), sliceCenterY, z),
                    Quaternion.identity,
                    forestWidth * (ForestInteriorSliceScale + (centeredT * 0.05f)),
                    sliceHeight,
                    material);
                CreateForestQuad(
                    parent,
                    $"{name}_Slice_Z_{i + 1:00}",
                    new Vector3(x, sliceCenterY + 0.6f, centerZ + (zSign * centeredT * 1.6f)),
                    Quaternion.Euler(0f, 90f, 0f),
                    forestDepth * (ForestInteriorSliceScale + (centeredT * 0.05f)),
                    sliceHeight - 1f,
                    material);
            }

            float diagonalSpan = Mathf.Sqrt((forestWidth * forestWidth) + (forestDepth * forestDepth)) * ForestDiagonalSliceScale;
            float diagonalHeight = ForestVolumeHeight + 1.5f;
            CreateForestQuad(
                parent,
                $"{name}_Slice_Diag_A",
                new Vector3(centerX + (xSign * 1.5f), baseCenterY + 0.9f, centerZ - (zSign * 1.2f)),
                Quaternion.Euler(0f, 45f, 0f),
                diagonalSpan,
                diagonalHeight,
                material);
            CreateForestQuad(
                parent,
                $"{name}_Slice_Diag_B",
                new Vector3(centerX - (xSign * 1.25f), baseCenterY + 0.45f, centerZ + (zSign * 1.5f)),
                Quaternion.Euler(0f, -45f, 0f),
                diagonalSpan,
                diagonalHeight - 1.2f,
                material);
        }

        static void CreateForestQuad(
            Transform parent,
            string name,
            Vector3 center,
            Quaternion rotation,
            float width,
            float height,
            Material material)
        {
            if (width <= 0f || height <= 0f)
                return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = center;
            quad.transform.rotation = rotation;
            quad.transform.localScale = new Vector3(width, height, 1f);

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void AddLanternsForRoad(
            Transform glowParent,
            Transform lanternParent,
            GameObject lanternPrefab,
            Material glowMaterial,
            RoadMetrics road,
            float centerX,
            float centerZ,
            bool outerRoad)
        {
            int count = outerRoad
                ? Mathf.Max(2, Mathf.RoundToInt((road.IsHorizontal ? road.Bounds.size.x : road.Bounds.size.z) / 115f))
                : Mathf.Max(2, Mathf.RoundToInt((road.IsHorizontal ? road.Bounds.size.x : road.Bounds.size.z) / 150f));

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : (i + 1f) / (count + 1f);
                Vector3 position;
                float yaw;
                Vector3 roadTarget;
                float overlayY = road.Bounds.max.y + 0.45f;

                if (road.IsHorizontal)
                {
                    float x = Mathf.Lerp(road.Bounds.min.x + LanternRoadLengthInset, road.Bounds.max.x - LanternRoadLengthInset, t);
                    bool roadAboveCenter = road.Bounds.center.z > centerZ;
                    float z = roadAboveCenter
                        ? road.Bounds.max.z - LanternRoadEdgeInset
                        : road.Bounds.min.z + LanternRoadEdgeInset;
                    position = new Vector3(x, 0f, z);
                    roadTarget = new Vector3(x, overlayY, road.Bounds.center.z);
                    yaw = roadAboveCenter ? 180f : 0f;
                }
                else
                {
                    float z = Mathf.Lerp(road.Bounds.min.z + LanternRoadLengthInset, road.Bounds.max.z - LanternRoadLengthInset, t);
                    bool roadRightOfCenter = road.Bounds.center.x > centerX;
                    float x = roadRightOfCenter
                        ? road.Bounds.max.x - LanternRoadEdgeInset
                        : road.Bounds.min.x + LanternRoadEdgeInset;
                    position = new Vector3(x, 0f, z);
                    roadTarget = new Vector3(road.Bounds.center.x, overlayY, z);
                    yaw = roadRightOfCenter ? 270f : 90f;
                }

                var lantern = InstantiateNestedPrefab(
                    lanternPrefab,
                    lanternParent,
                    $"{road.Name}_Lantern_{i + 1:00}");
                lantern.transform.position = position;
                lantern.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                lantern.transform.localScale = Vector3.one * 3.2f;

                SetRendererShadows(lantern, true, true);
                ConfigureLanternProjector(lantern.transform, roadTarget, outerRoad);
                CreateLanternRoadGlow(
                    glowParent,
                    $"{road.Name}_RoadGlow_{i + 1:00}",
                    position,
                    roadTarget,
                    road.IsHorizontal,
                    outerRoad,
                    glowMaterial);
                AddWarmPointLight(
                    lantern.transform,
                    road.Name,
                    outerRoad ? OuterLanternPointIntensity : InnerLanternPointIntensity,
                    outerRoad ? OuterLanternPointRange : InnerLanternPointRange);
            }
        }

        static void ConfigureLanternProjector(Transform lanternTransform, Vector3 roadTarget, bool outerRoad)
        {
            var spotTransform = lanternTransform.Find("Spot Light");
            Light spotLight = null;
            if (spotTransform != null)
                spotLight = spotTransform.GetComponent<Light>();

            if (spotLight == null)
            {
                var spotGo = new GameObject("Spot Light");
                spotTransform = spotGo.transform;
                spotTransform.SetParent(lanternTransform, false);
                spotTransform.localPosition = new Vector3(0f, 1.85f, 0f);
                spotLight = spotGo.AddComponent<Light>();
                spotGo.AddComponent<UniversalAdditionalLightData>();
            }

            var sourcePosition = spotTransform.position;
            var lookTarget = roadTarget + Vector3.up * 0.1f;
            var projectDirection = (lookTarget - sourcePosition).normalized;
            if (projectDirection.sqrMagnitude < 0.0001f)
                projectDirection = Vector3.down;

            spotTransform.rotation = Quaternion.LookRotation(projectDirection, Vector3.up);

            spotLight.type = LightType.Spot;
            spotLight.color = new Color(1f, 0.74f, 0.48f, 1f);
            spotLight.intensity = outerRoad ? OuterLanternSpotIntensity : InnerLanternSpotIntensity;
            spotLight.range = outerRoad ? OuterLanternSpotRange : InnerLanternSpotRange;
            spotLight.spotAngle = outerRoad ? OuterLanternSpotAngle : InnerLanternSpotAngle;
            spotLight.innerSpotAngle = outerRoad ? OuterLanternInnerAngle : InnerLanternInnerAngle;
            // Road readability depends on these lights surviving URP additional-light prioritization.
            spotLight.renderMode = LightRenderMode.ForcePixel;
            spotLight.shadows = LightShadows.Soft;
            spotLight.shadowStrength = outerRoad ? OuterLanternShadowStrength : InnerLanternShadowStrength;
            spotLight.shadowBias = 0.04f;
            spotLight.shadowNormalBias = 0.3f;
            spotLight.bounceIntensity = 0.38f;
        }

        static void AddWarmPointLight(Transform parent, string seedName, float intensity, float range)
        {
            var lightGo = new GameObject($"{seedName}_Glow");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = new Vector3(0f, 5.8f, 0f);

            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.intensity = intensity;
            light.range = range;
            light.color = new Color(1f, 0.72f, 0.46f, 1f);
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        static void ConfigureNightScene(Transform mapRoot, bool previewMode)
        {
            var environmentLoader = mapRoot.GetComponent("EnvironmentLoader") as Behaviour;
            var optionalLoader = mapRoot.GetComponent("OptionalEnvironmentLoader") as Behaviour;
            if (environmentLoader == null || optionalLoader == null)
                throw new InvalidOperationException("Map root is missing EnvironmentLoader or OptionalEnvironmentLoader.");

            var serializedEnvironment = new SerializedObject(environmentLoader);
            serializedEnvironment.FindProperty("environmentAddress").stringValue = CriticalAddress;
            serializedEnvironment.ApplyModifiedPropertiesWithoutUndo();

            var serializedOptional = new SerializedObject(optionalLoader);
            serializedOptional.FindProperty("optionalEnvironmentAddress").stringValue = OptionalAddress;
            serializedOptional.FindProperty("instantiatedRootName").stringValue = "OptionalEnvironmentDressing";
            serializedOptional.FindProperty("instantiatedRootScale").floatValue = 1f;
            serializedOptional.ApplyModifiedPropertiesWithoutUndo();

            environmentLoader.enabled = !previewMode;
            optionalLoader.enabled = !previewMode;
            EditorUtility.SetDirty(environmentLoader);
            EditorUtility.SetDirty(optionalLoader);

            var mainCamera = GameObject.Find("Main Camera");
            var globalVolumeObject = GameObject.Find("GlobalVolume");
            var postProcessObject = GameObject.Find("PostProcessController");
            var lightObject = GameObject.Find("Directional Light");

            if (mainCamera == null || globalVolumeObject == null || postProcessObject == null || lightObject == null)
            {
                throw new InvalidOperationException(
                    "Expected Main Camera, GlobalVolume, PostProcessController, and Directional Light in Game_ML.");
            }

            var cameraData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = true;
                EditorUtility.SetDirty(cameraData);
            }

            var sunLight = lightObject.GetComponent<Light>();
            if (sunLight != null)
            {
                sunLight.color = new Color(0.43f, 0.5f, 0.66f, 1f);
                sunLight.intensity = 0.62f;
                sunLight.shadows = LightShadows.Soft;
                sunLight.shadowStrength = 0.58f;
                sunLight.shadowBias = 0.04f;
                sunLight.shadowNormalBias = 0.3f;
                EditorUtility.SetDirty(sunLight);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.1f, 0.11f, 0.14f, 1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.045f, 0.05f, 0.08f, 1f);
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 430f;

            var volume = globalVolumeObject.GetComponent<Volume>();
            if (volume == null)
                volume = globalVolumeObject.AddComponent<Volume>();

            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = EnsureNightVolumeProfile();
            EditorUtility.SetDirty(volume);

            var postProcessController = postProcessObject.GetComponent<global::PostProcessController>();
            if (postProcessController != null)
            {
                postProcessController.volume = volume;
                EditorUtility.SetDirty(postProcessController);
            }
        }

        static VolumeProfile EnsureNightVolumeProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PostProcess"))
                AssetDatabase.CreateFolder("Assets", "PostProcess");

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(NightProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, NightProfilePath);
            }

            if (!profile.TryGet<Tonemapping>(out var tonemapping))
                tonemapping = profile.Add<Tonemapping>(false);
            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = TonemappingMode.ACES;

            if (!profile.TryGet<Bloom>(out var bloom))
                bloom = profile.Add<Bloom>(false);
            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.85f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.75f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.72f;
            bloom.highQualityFiltering.overrideState = true;
            bloom.highQualityFiltering.value = false;

            if (!profile.TryGet<Vignette>(out var vignette))
                vignette = profile.Add<Vignette>(false);
            vignette.active = true;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.08f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.42f;
            vignette.color.overrideState = true;
            vignette.color.value = new Color(0.02f, 0.025f, 0.05f, 1f);

            if (!profile.TryGet<ColorAdjustments>(out var colorAdjustments))
                colorAdjustments = profile.Add<ColorAdjustments>(false);
            colorAdjustments.active = true;
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = 0.15f;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = 6f;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = -4f;
            colorAdjustments.colorFilter.overrideState = true;
            colorAdjustments.colorFilter.value = new Color(0.97f, 0.98f, 1f, 1f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static Material EnsureDarknessMaterial()
        {
            return EnsureDarknessMaterialAsset(
                DarknessMaterialPath,
                "GameMLDarknessField",
                DarknessLayerAlpha);
        }

        static Material EnsureOuterDarknessMaterial()
        {
            return EnsureDarknessMaterialAsset(
                OuterDarknessMaterialPath,
                "GameMLOuterDarknessField",
                OuterDarknessLayerAlpha);
        }

        static Material EnsureDarknessMaterialAsset(string assetPath, string assetName, float alpha)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Sprites/Default");
            if (shader == null)
                throw new InvalidOperationException(
                    "Could not locate a transparent unlit shader for the darkness field material.");

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = assetName,
                };
                AssetDatabase.CreateAsset(material, assetPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            Color darknessColor = new Color(0.01f, 0.015f, 0.03f, alpha);

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", darknessColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", darknessColor);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha"))
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha"))
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", (float)CullMode.Back);

            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Material EnsureForestVolumeMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ForestVolumeMaterialPath);
            Shader shader = RequireForestVolumeShader();

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "GameMLForestVolume",
                };
                AssetDatabase.CreateAsset(material, ForestVolumeMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", new Color(0.03f, 0.055f, 0.038f, 0.94f));
            if (material.HasProperty("_DepthColor"))
                material.SetColor("_DepthColor", new Color(0.006f, 0.012f, 0.012f, 0.99f));
            if (material.HasProperty("_TopColor"))
                material.SetColor("_TopColor", new Color(0.048f, 0.072f, 0.052f, 0.9f));
            if (material.HasProperty("_Darkness"))
                material.SetFloat("_Darkness", ForestVolumeDarkness);
            if (material.HasProperty("_NoiseStrength"))
                material.SetFloat("_NoiseStrength", ForestVolumeNoiseStrength);
            if (material.HasProperty("_FadeSoftness"))
                material.SetFloat("_FadeSoftness", ForestVolumeFadeSoftness);
            if (material.HasProperty("_TopSilhouetteStrength"))
                material.SetFloat("_TopSilhouetteStrength", ForestVolumeTopSilhouetteStrength);
            if (material.HasProperty("_NoiseScale"))
                material.SetFloat("_NoiseScale", ForestVolumeNoiseScale);
            if (material.HasProperty("_CenterDensity"))
                material.SetFloat("_CenterDensity", ForestVolumeCenterDensity);
            if (material.HasProperty("_ViewOcclusionBoost"))
                material.SetFloat("_ViewOcclusionBoost", ForestVolumeViewOcclusionBoost);
            if (material.HasProperty("_EdgeFadeDistance"))
                material.SetFloat("_EdgeFadeDistance", ForestVolumeEdgeFadeDistance);

            material.renderQueue = (int)RenderQueue.Transparent - 12;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Material EnsureLanternRoadGlowMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(LanternRoadGlowMaterialPath);
            Shader shader = RequireLanternRoadGlowShader();

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "GameMLLanternRoadGlow",
                };
                AssetDatabase.CreateAsset(material, LanternRoadGlowMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(1f, 0.68f, 0.33f, 1f));
            if (material.HasProperty("_EdgePower"))
                material.SetFloat("_EdgePower", 1.9f);
            if (material.HasProperty("_CenterBoost"))
                material.SetFloat("_CenterBoost", 1.45f);

            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Material EnsureRoadWashMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoadWashMaterialPath);
            Shader shader = RequireLanternRoadGlowShader();

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "GameMLRoadWash",
                };
                AssetDatabase.CreateAsset(material, RoadWashMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(0.52f, 0.57f, 0.66f, 0.3f));
            if (material.HasProperty("_EdgePower"))
                material.SetFloat("_EdgePower", 1.42f);
            if (material.HasProperty("_CenterBoost"))
                material.SetFloat("_CenterBoost", 1.16f);

            material.renderQueue = (int)RenderQueue.Transparent - 5;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Material EnsureRoadShoulderMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(RoadShoulderMaterialPath);
            Shader shader = RequireRoadDarknessBandShader();

            if (material == null)
            {
                material = new Material(shader)
                {
                    name = "GameMLRoadShoulder",
                };
                AssetDatabase.CreateAsset(material, RoadShoulderMaterialPath);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_Color"))
                material.SetColor("_Color", new Color(0.03f, 0.04f, 0.065f, 1f));
            if (material.HasProperty("_InnerAlpha"))
                material.SetFloat("_InnerAlpha", RoadShoulderInnerAlpha);
            if (material.HasProperty("_OuterAlpha"))
                material.SetFloat("_OuterAlpha", RoadShoulderOuterAlpha);
            if (material.HasProperty("_NoiseStrength"))
                material.SetFloat("_NoiseStrength", RoadShoulderNoiseStrength);
            if (material.HasProperty("_EndFade"))
                material.SetFloat("_EndFade", RoadShoulderEndFade);

            material.renderQueue = (int)RenderQueue.Transparent - 8;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        static Shader RequireLanternRoadGlowShader()
        {
            Shader shader = Shader.Find("CastleDefender/LanternRoadGlow");
            if (shader == null)
                throw new InvalidOperationException(
                    "Could not locate shader 'CastleDefender/LanternRoadGlow' for road glow pools.");
            return shader;
        }

        static Shader RequireRoadDarknessBandShader()
        {
            Shader shader = Shader.Find("CastleDefender/RoadDarknessBand");
            if (shader == null)
                throw new InvalidOperationException(
                    "Could not locate shader 'CastleDefender/RoadDarknessBand' for road shoulder darkness.");
            return shader;
        }

        static Shader RequireForestVolumeShader()
        {
            Shader shader = Shader.Find("CastleDefender/ForestVolume");
            if (shader == null)
                throw new InvalidOperationException(
                    "Could not locate shader 'CastleDefender/ForestVolume' for forest volume masses.");
            return shader;
        }

        static GameObject InstantiateNestedPrefab(GameObject prefab, Transform parent, string name)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate nested prefab '{prefab.name}'.");

            instance.name = name;
            instance.transform.SetParent(parent, false);
            return instance;
        }

        static void CreateCenteredDarknessStack(
            Transform parent,
            string namePrefix,
            Vector3 center,
            float width,
            float depth,
            Material material)
        {
            if (width <= 0f || depth <= 0f)
                return;

            float insetStep = Mathf.Clamp(Mathf.Min(width, depth) / 18f, 8f, 15f);
            for (int i = 0; i < DarknessLayerCount; i++)
            {
                float inset = insetStep * i;
                float layerWidth = width - (inset * 2f);
                float layerDepth = depth - (inset * 2f);
                if (layerWidth <= 0f || layerDepth <= 0f)
                    break;

                CreateDarknessQuad(
                    parent,
                    $"{namePrefix}_{i + 1:00}",
                    center + Vector3.up * (DarknessLayerYOffset * i),
                    layerWidth,
                    layerDepth,
                    material);
            }
        }

        static void CreateInnerQuadrantDarkness(
            Transform parent,
            RoadLayout layout,
            float y,
            Material material)
        {
            float westWidth = layout.VerticalRoadXMin - layout.LeftInnerX;
            float eastWidth = layout.RightInnerX - layout.VerticalRoadXMax;
            float northDepth = layout.TopInnerZ - layout.HorizontalRoadZMax;
            float southDepth = layout.HorizontalRoadZMin - layout.BottomInnerZ;

            CreateCenteredDarknessStack(
                parent,
                "Darkness_Quadrant_NW",
                new Vector3(
                    (layout.LeftInnerX + layout.VerticalRoadXMin) * 0.5f,
                    y,
                    (layout.HorizontalRoadZMax + layout.TopInnerZ) * 0.5f),
                westWidth,
                northDepth,
                material);
            CreateCenteredDarknessStack(
                parent,
                "Darkness_Quadrant_NE",
                new Vector3(
                    (layout.VerticalRoadXMax + layout.RightInnerX) * 0.5f,
                    y,
                    (layout.HorizontalRoadZMax + layout.TopInnerZ) * 0.5f),
                eastWidth,
                northDepth,
                material);
            CreateCenteredDarknessStack(
                parent,
                "Darkness_Quadrant_SW",
                new Vector3(
                    (layout.LeftInnerX + layout.VerticalRoadXMin) * 0.5f,
                    y,
                    (layout.BottomInnerZ + layout.HorizontalRoadZMin) * 0.5f),
                westWidth,
                southDepth,
                material);
            CreateCenteredDarknessStack(
                parent,
                "Darkness_Quadrant_SE",
                new Vector3(
                    (layout.VerticalRoadXMax + layout.RightInnerX) * 0.5f,
                    y,
                    (layout.BottomInnerZ + layout.HorizontalRoadZMin) * 0.5f),
                eastWidth,
                southDepth,
                material);
        }

        static void CreateAnchoredDarknessStack(
            Transform parent,
            string namePrefix,
            DarknessDirection direction,
            float innerEdge,
            float y,
            float crossCenter,
            float crossSize,
            float totalSpan,
            Material material)
        {
            if (crossSize <= 0f || totalSpan <= 0f)
                return;

            float spanStep = Mathf.Clamp(totalSpan / (DarknessLayerCount + 1f), 18f, 34f);
            float outerEdge = direction switch
            {
                DarknessDirection.West => innerEdge - totalSpan,
                DarknessDirection.East => innerEdge + totalSpan,
                DarknessDirection.North => innerEdge + totalSpan,
                DarknessDirection.South => innerEdge - totalSpan,
                _ => innerEdge,
            };

            for (int i = 0; i < DarknessLayerCount; i++)
            {
                float span = totalSpan - (spanStep * i);
                if (span <= spanStep)
                    break;

                Vector3 center;
                float width;
                float depth;
                switch (direction)
                {
                    case DarknessDirection.West:
                        center = new Vector3(outerEdge + (span * 0.5f), y + (DarknessLayerYOffset * i), crossCenter);
                        width = span;
                        depth = crossSize;
                        break;
                    case DarknessDirection.East:
                        center = new Vector3(outerEdge - (span * 0.5f), y + (DarknessLayerYOffset * i), crossCenter);
                        width = span;
                        depth = crossSize;
                        break;
                    case DarknessDirection.North:
                        center = new Vector3(crossCenter, y + (DarknessLayerYOffset * i), outerEdge - (span * 0.5f));
                        width = crossSize;
                        depth = span;
                        break;
                    case DarknessDirection.South:
                        center = new Vector3(crossCenter, y + (DarknessLayerYOffset * i), outerEdge + (span * 0.5f));
                        width = crossSize;
                        depth = span;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
                }

                CreateDarknessQuad(
                    parent,
                    $"{namePrefix}_{i + 1:00}",
                    center,
                    width,
                    depth,
                    material);
            }
        }

        static void CreateDarknessQuad(
            Transform parent,
            string name,
            Vector3 center,
            float width,
            float depth,
            Material material)
        {
            if (width <= 0f || depth <= 0f)
                return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = center;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(width, depth, 1f);

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void CreateLanternRoadGlow(
            Transform parent,
            string name,
            Vector3 lanternPosition,
            Vector3 roadTarget,
            bool roadIsHorizontal,
            bool outerRoad,
            Material material)
        {
            if (parent == null || material == null)
                return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);

            Vector3 center = Vector3.Lerp(lanternPosition, roadTarget, 0.72f);
            center.y = roadTarget.y;
            quad.transform.position = center;
            quad.transform.rotation = Quaternion.Euler(90f, roadIsHorizontal ? 0f : 90f, 0f);
            quad.transform.localScale = outerRoad
                ? new Vector3(26f, 16f, 1f)
                : new Vector3(30f, 19f, 1f);

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void CreateRoadVisibilityWash(Transform parent, RoadMetrics road, Material material)
        {
            if (parent == null || material == null)
                return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = $"{road.Name}_Wash";
            quad.transform.SetParent(parent, false);
            quad.transform.position = new Vector3(
                road.Bounds.center.x,
                road.Bounds.max.y + 0.35f,
                road.Bounds.center.z);
            quad.transform.rotation = Quaternion.Euler(90f, road.IsHorizontal ? 0f : 90f, 0f);
            quad.transform.localScale = road.IsHorizontal
                ? new Vector3(road.Bounds.size.x * 1.04f, Mathf.Max(9f, road.Bounds.size.z * 1.05f), 1f)
                : new Vector3(road.Bounds.size.z * 1.04f, Mathf.Max(9f, road.Bounds.size.x * 1.05f), 1f);

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void CreateRoadShoulderBands(Transform parent, RoadMetrics road, Material material)
        {
            if (parent == null || material == null)
                return;

            float overlayY = road.Bounds.max.y + 0.16f;
            float roadLength = road.IsHorizontal ? road.Bounds.size.x : road.Bounds.size.z;
            float bandLength = roadLength + RoadShoulderLengthPadding;

            if (road.IsHorizontal)
            {
                CreateRoadShoulderQuad(
                    parent,
                    $"{road.Name}_Shoulder_North",
                    new Vector3(road.Bounds.center.x, overlayY, road.Bounds.min.z - (RoadShoulderWidth * 0.5f)),
                    Quaternion.Euler(90f, 0f, 0f),
                    bandLength,
                    RoadShoulderWidth,
                    material);
                CreateRoadShoulderQuad(
                    parent,
                    $"{road.Name}_Shoulder_South",
                    new Vector3(road.Bounds.center.x, overlayY, road.Bounds.max.z + (RoadShoulderWidth * 0.5f)),
                    Quaternion.Euler(90f, 180f, 0f),
                    bandLength,
                    RoadShoulderWidth,
                    material);
                return;
            }

            CreateRoadShoulderQuad(
                parent,
                $"{road.Name}_Shoulder_East",
                new Vector3(road.Bounds.max.x + (RoadShoulderWidth * 0.5f), overlayY, road.Bounds.center.z),
                Quaternion.Euler(90f, 90f, 0f),
                bandLength,
                RoadShoulderWidth,
                material);
            CreateRoadShoulderQuad(
                parent,
                $"{road.Name}_Shoulder_West",
                new Vector3(road.Bounds.min.x - (RoadShoulderWidth * 0.5f), overlayY, road.Bounds.center.z),
                Quaternion.Euler(90f, -90f, 0f),
                bandLength,
                RoadShoulderWidth,
                material);
        }

        static void CreateRoadShoulderQuad(
            Transform parent,
            string name,
            Vector3 center,
            Quaternion rotation,
            float width,
            float depth,
            Material material)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.position = center;
            quad.transform.rotation = rotation;
            quad.transform.localScale = new Vector3(width, depth, 1f);

            var renderer = quad.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            var collider = quad.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void SetRendererShadows(GameObject go, bool cast, bool receive)
        {
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = cast ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = receive;
            }
        }

        static void ClearChildren(Transform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
        }

        static void StripColliders(Transform root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        static Transform FindChildRecursive(Transform root, string name)
        {
            return root
                .GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => string.Equals(t.name, name, StringComparison.Ordinal));
        }

        sealed class RoadLayout
        {
            public float CenterX;
            public float CenterZ;
            public float LeftInnerX;
            public float RightInnerX;
            public float TopInnerZ;
            public float BottomInnerZ;
            public float LeftOuterX;
            public float RightOuterX;
            public float TopOuterZ;
            public float BottomOuterZ;
            public float HorizontalRoadZMin;
            public float HorizontalRoadZMax;
            public float VerticalRoadXMin;
            public float VerticalRoadXMax;
            public float GroundY;
            public Bounds MineBounds;
            public FortressBounds[] Fortresses;
            public RoadMetrics[] OuterRoads;
            public RoadMetrics[] CenterRoads;

            public IEnumerable<RoadMetrics> AllRoads => OuterRoads.Concat(CenterRoads);
        }

        enum DarknessDirection
        {
            West,
            East,
            North,
            South,
        }

        enum FortressCompass
        {
            West,
            East,
            North,
            South,
        }

        struct RoadMetrics
        {
            public string Name;
            public Transform Transform;
            public Bounds Bounds;
            public bool IsHorizontal;
        }

        struct FortressBounds
        {
            public string Name;
            public Bounds Bounds;
            public Bounds DarknessBounds;
        }

        struct Range1D
        {
            public float Min;
            public float Max;
        }
    }
}
#endif
