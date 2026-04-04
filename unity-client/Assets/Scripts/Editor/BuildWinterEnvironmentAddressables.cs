#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CastleDefender.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class BuildWinterEnvironmentAddressables
    {
        const string ScenePath = "Assets/Scenes/Game_ML.unity";
        const string MapRootName = "Map";
        const string EnvironmentFolder = "Assets/AddressableContent/Environment";
        const string CriticalPrefabPath = EnvironmentFolder + "/GameEnvironment.prefab";
        const string OptionalPrefabPath = EnvironmentFolder + "/GameEnvironmentOptional.prefab";
        const string CriticalGroupName = "Remote Environment";
        const string OptionalGroupName = "Remote Environment Dressing";
        const string SharedGroupName = "Remote Environment Shared";
        const string TemplateGroup = "Remote Units 01";
        const string ReportRelativePath = "projects/Game_ML Winter Environment Addressables.md";
        const string RemoteBuildPathProfileId = "165fb4a3ad8d19e4aa002d6fc764a7ce";
        const string RemoteLoadPathProfileId = "247226ff3fd294f46b8dfca266320b8c";

        static readonly (string assetPath, string address)[] SharedAssetCatalog =
        {
            (
                "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Textures/Seamless Terrain/Snow_Dirt_Seamless_Texture_Albedo.png",
                "environment/shared/snow_dirt_albedo"
            ),
        };

        static readonly string[] OptionalAssetCatalog =
        {
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_extra_large.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_large.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_medium.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/pine_small.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_01.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_02.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_03.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/rock_04.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/bush_01.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/bush_02.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/bush_03.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/barrel.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/cottage.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/sled.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/snowman_01.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Winter/snowman_02.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/fence.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/fence_seamless.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/lantern_pole.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/bench.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/stacked_wood_01.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Misc/wheelbarrow.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_tree_outside_01.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_tree_large.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_01.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_03.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_06.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Christmas/christmas_gift_09.prefab",
            "Assets/Winter & Christmas Pack/Winter & Christmas Pack/Prefabs/Terrain/floor_horizon.prefab",
        };

        [MenuItem("Castle Defender/Remote Content/Build Winter Environment Addressables")]
        static void Build()
        {
            try
            {
                EnsureFolder("Assets/AddressableContent");
                EnsureFolder(EnvironmentFolder);

                SetupRemoteEnvironmentAddressables.SanitizeGameEnvironmentPrefab();
                BuildOptionalEnvironmentPrefab();
                EnsureAddressablesEntry(CriticalPrefabPath, RemoteContentManager.GameMlEnvironmentAddress, CriticalGroupName);
                EnsureAddressablesEntry(OptionalPrefabPath, RemoteContentManager.GameMlEnvironmentDressingAddress, OptionalGroupName);
                EnsureSharedAddressablesEntries();
                EnsureSceneLoaders();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                WriteReport();

                Debug.Log("[BuildWinterEnvironmentAddressables] Winter environment addressables updated.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        static void BuildOptionalEnvironmentPrefab()
        {
            var root = new GameObject("OptionalEnvironmentDressing");
            try
            {
                var outer = CreateChild(root.transform, "VisualOuterRing");
                var edgeCliffs = CreateChild(outer, "EdgeCliffs");
                var pineBorders = CreateChild(outer, "PineBorders");
                var rockBorders = CreateChild(outer, "RockBorders");
                var decorBorders = CreateChild(outer, "DecorBorders");
                var snowOverlay = CreateChild(outer, "SnowOverlayDetails");
                var landmarkProps = CreateChild(outer, "LandmarkProps");
                var lighting = CreateChild(outer, "LightingHelpersOptional");

                var backdrop = CreateChild(root.transform, "Backdrop");
                var midCliffs = CreateChild(backdrop, "MidCliffs");
                var farMountains = CreateChild(backdrop, "FarMountains");

                AddCliffRing(edgeCliffs);
                AddPineBorders(pineBorders);
                AddRockBorders(rockBorders);
                AddDecorBorders(decorBorders);
                AddSnowDetailBorders(snowOverlay);
                AddLandmarks(landmarkProps);
                AddBackdrop(midCliffs, farMountains);
                AddOptionalLights(lighting);

                StripColliders(root.transform);
                DisableBackdropShadows(midCliffs);
                DisableBackdropShadows(farMountains);

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, OptionalPrefabPath);
                if (prefab == null)
                    throw new InvalidOperationException($"Failed to save optional environment prefab at '{OptionalPrefabPath}'.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void EnsureSceneLoaders()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var mapRoot = GameObject.Find(MapRootName);
            if (mapRoot == null)
                throw new InvalidOperationException($"Could not find '{MapRootName}' in '{ScenePath}'.");

            var criticalLoader = mapRoot.GetComponent<EnvironmentLoader>();
            if (criticalLoader == null)
                criticalLoader = Undo.AddComponent<EnvironmentLoader>(mapRoot);

            criticalLoader.environmentAddress = RemoteContentManager.GameMlEnvironmentAddress;
            criticalLoader.instantiateParent = mapRoot.transform;
            criticalLoader.instantiatedRootName = RemoteContentManager.GameMlEnvironmentRootName;
            criticalLoader.failureTitle = "Required map environment failed to load.";
            criticalLoader.readinessTimeoutSeconds = 12f;
            criticalLoader.enabled = true;
            EditorUtility.SetDirty(criticalLoader);

            var optionalLoader = mapRoot.GetComponent<OptionalEnvironmentLoader>();
            if (optionalLoader == null)
                optionalLoader = Undo.AddComponent<OptionalEnvironmentLoader>(mapRoot);

            optionalLoader.optionalEnvironmentAddress = RemoteContentManager.GameMlEnvironmentDressingAddress;
            optionalLoader.instantiateParent = mapRoot.transform;
            optionalLoader.instantiatedRootName = RemoteContentManager.GameMlEnvironmentDressingRootName;
            optionalLoader.instantiatedRootScale = RemoteContentManager.GameMlEnvironmentDressingScale;
            optionalLoader.requiredRootName = RemoteContentManager.GameMlEnvironmentRootName;
            optionalLoader.waitForCriticalTimeoutSeconds = 15f;
            optionalLoader.loadStartDelaySeconds = 0.25f;
            optionalLoader.logWarnings = true;
            optionalLoader.enabled = true;
            EditorUtility.SetDirty(optionalLoader);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void EnsureSharedAddressablesEntries()
        {
            for (int i = 0; i < SharedAssetCatalog.Length; i++)
            {
                var entry = SharedAssetCatalog[i];
                EnsureAddressablesEntry(entry.assetPath, entry.address, SharedGroupName);
            }
        }

        static void AddCliffRing(Transform parent)
        {
            AddPrefab(parent, OptionalAssetCatalog[4], "EdgeCliff_NW_A", new Vector3(-63f, -9f, -61f), new Vector3(0f, 28f, 0f), new Vector3(18f, 16f, 18f));
            AddPrefab(parent, OptionalAssetCatalog[7], "EdgeCliff_NW_B", new Vector3(-48f, -8f, -71f), new Vector3(0f, -18f, 0f), new Vector3(14f, 18f, 14f));
            AddPrefab(parent, OptionalAssetCatalog[5], "EdgeCliff_NE_A", new Vector3(61f, -10f, -65f), new Vector3(0f, -34f, 0f), new Vector3(20f, 19f, 20f));
            AddPrefab(parent, OptionalAssetCatalog[6], "EdgeCliff_NE_B", new Vector3(77f, -7f, -48f), new Vector3(0f, 12f, 0f), new Vector3(14f, 15f, 14f));
            AddPrefab(parent, OptionalAssetCatalog[7], "EdgeCliff_SE_A", new Vector3(68f, -10f, 67f), new Vector3(0f, 208f, 0f), new Vector3(21f, 18f, 21f));
            AddPrefab(parent, OptionalAssetCatalog[4], "EdgeCliff_SE_B", new Vector3(47f, -7f, 78f), new Vector3(0f, 172f, 0f), new Vector3(16f, 14f, 16f));
            AddPrefab(parent, OptionalAssetCatalog[6], "EdgeCliff_SW_A", new Vector3(-66f, -10f, 64f), new Vector3(0f, 144f, 0f), new Vector3(22f, 18f, 22f));
            AddPrefab(parent, OptionalAssetCatalog[5], "EdgeCliff_SW_B", new Vector3(-79f, -6f, 46f), new Vector3(0f, 108f, 0f), new Vector3(15f, 14f, 15f));
        }

        static void AddPineBorders(Transform parent)
        {
            var placements = new[]
            {
                new Placement("PineNW_01", OptionalAssetCatalog[0], new Vector3(-52f, -2f, -55f), new Vector3(0f, 14f, 0f), new Vector3(5.5f, 5.5f, 5.5f)),
                new Placement("PineNW_02", OptionalAssetCatalog[1], new Vector3(-60f, -2f, -40f), new Vector3(0f, 41f, 0f), new Vector3(4.5f, 4.5f, 4.5f)),
                new Placement("PineNW_03", OptionalAssetCatalog[2], new Vector3(-43f, -2f, -66f), new Vector3(0f, -18f, 0f), new Vector3(3.6f, 3.6f, 3.6f)),
                new Placement("PineNE_01", OptionalAssetCatalog[0], new Vector3(50f, -2f, -55f), new Vector3(0f, -23f, 0f), new Vector3(5.8f, 5.8f, 5.8f)),
                new Placement("PineNE_02", OptionalAssetCatalog[1], new Vector3(65f, -2f, -38f), new Vector3(0f, 22f, 0f), new Vector3(4.2f, 4.2f, 4.2f)),
                new Placement("PineNE_03", OptionalAssetCatalog[3], new Vector3(41f, -2f, -68f), new Vector3(0f, 37f, 0f), new Vector3(3.2f, 3.2f, 3.2f)),
                new Placement("PineSE_01", OptionalAssetCatalog[0], new Vector3(58f, -2f, 51f), new Vector3(0f, 165f, 0f), new Vector3(5.6f, 5.6f, 5.6f)),
                new Placement("PineSE_02", OptionalAssetCatalog[2], new Vector3(44f, -2f, 67f), new Vector3(0f, 197f, 0f), new Vector3(3.8f, 3.8f, 3.8f)),
                new Placement("PineSE_03", OptionalAssetCatalog[3], new Vector3(69f, -2f, 34f), new Vector3(0f, 121f, 0f), new Vector3(3.1f, 3.1f, 3.1f)),
                new Placement("PineSW_01", OptionalAssetCatalog[0], new Vector3(-57f, -2f, 49f), new Vector3(0f, 206f, 0f), new Vector3(5.3f, 5.3f, 5.3f)),
                new Placement("PineSW_02", OptionalAssetCatalog[1], new Vector3(-67f, -2f, 32f), new Vector3(0f, 139f, 0f), new Vector3(4.3f, 4.3f, 4.3f)),
                new Placement("PineSW_03", OptionalAssetCatalog[2], new Vector3(-40f, -2f, 69f), new Vector3(0f, 230f, 0f), new Vector3(3.7f, 3.7f, 3.7f)),
                new Placement("PineNorth_01", OptionalAssetCatalog[1], new Vector3(-14f, -2f, -72f), new Vector3(0f, 8f, 0f), new Vector3(4.6f, 4.6f, 4.6f)),
                new Placement("PineNorth_02", OptionalAssetCatalog[2], new Vector3(13f, -2f, -74f), new Vector3(0f, -7f, 0f), new Vector3(3.9f, 3.9f, 3.9f)),
                new Placement("PineSouth_01", OptionalAssetCatalog[1], new Vector3(-12f, -2f, 76f), new Vector3(0f, 176f, 0f), new Vector3(4.4f, 4.4f, 4.4f)),
                new Placement("PineSouth_02", OptionalAssetCatalog[2], new Vector3(15f, -2f, 73f), new Vector3(0f, 202f, 0f), new Vector3(3.8f, 3.8f, 3.8f)),
            };

            for (int i = 0; i < placements.Length; i++)
                AddPrefab(parent, placements[i]);
        }

        static void AddRockBorders(Transform parent)
        {
            var placements = new[]
            {
                new Placement("RockBorder_SouthWest", OptionalAssetCatalog[7], new Vector3(-39f, -5f, 46f), new Vector3(0f, 202f, 0f), new Vector3(5.8f, 4.8f, 5.8f)),
                new Placement("RockBorder_SouthEast", OptionalAssetCatalog[4], new Vector3(41f, -5f, 44f), new Vector3(0f, 164f, 0f), new Vector3(6.4f, 4.6f, 6.4f)),
                new Placement("RockBorder_West", OptionalAssetCatalog[6], new Vector3(-48f, -6f, 3f), new Vector3(0f, 121f, 0f), new Vector3(7f, 5f, 7f)),
                new Placement("RockBorder_East", OptionalAssetCatalog[5], new Vector3(47f, -6f, -2f), new Vector3(0f, 51f, 0f), new Vector3(6.7f, 5.1f, 6.7f)),
            };

            for (int i = 0; i < placements.Length; i++)
                AddPrefab(parent, placements[i]);
        }

        static void AddDecorBorders(Transform parent)
        {
            AddPrefab(parent, OptionalAssetCatalog[17], "Fence_NW", new Vector3(-34f, -1.4f, -38f), new Vector3(0f, 40f, 0f), new Vector3(4.5f, 4.5f, 4.5f));
            AddPrefab(parent, OptionalAssetCatalog[17], "Fence_NE", new Vector3(35f, -1.4f, -39f), new Vector3(0f, -37f, 0f), new Vector3(4.5f, 4.5f, 4.5f));
            AddPrefab(parent, OptionalAssetCatalog[17], "Fence_SW", new Vector3(-36f, -1.4f, 39f), new Vector3(0f, 219f, 0f), new Vector3(4.5f, 4.5f, 4.5f));
            AddPrefab(parent, OptionalAssetCatalog[17], "Fence_SE", new Vector3(36f, -1.4f, 40f), new Vector3(0f, 140f, 0f), new Vector3(4.5f, 4.5f, 4.5f));

            AddPrefab(parent, OptionalAssetCatalog[18], "Lantern_NW", new Vector3(-29f, -0.5f, -31f), new Vector3(0f, 42f, 0f), new Vector3(3.2f, 3.2f, 3.2f));
            AddPrefab(parent, OptionalAssetCatalog[18], "Lantern_NE", new Vector3(29f, -0.5f, -30f), new Vector3(0f, -41f, 0f), new Vector3(3.2f, 3.2f, 3.2f));
            AddPrefab(parent, OptionalAssetCatalog[18], "Lantern_SW", new Vector3(-29f, -0.5f, 31f), new Vector3(0f, 220f, 0f), new Vector3(3.2f, 3.2f, 3.2f));
            AddPrefab(parent, OptionalAssetCatalog[18], "Lantern_SE", new Vector3(30f, -0.5f, 30f), new Vector3(0f, 139f, 0f), new Vector3(3.2f, 3.2f, 3.2f));
        }

        static void AddSnowDetailBorders(Transform parent)
        {
            var placements = new[]
            {
                new Placement("SnowBank_NW_A", OptionalAssetCatalog[8], new Vector3(-43f, -1.7f, -38f), new Vector3(0f, 16f, 0f), new Vector3(4f, 4f, 4f)),
                new Placement("SnowBank_NW_B", OptionalAssetCatalog[9], new Vector3(-37f, -1.7f, -43f), new Vector3(0f, 57f, 0f), new Vector3(3.2f, 3.2f, 3.2f)),
                new Placement("SnowBank_NE_A", OptionalAssetCatalog[10], new Vector3(44f, -1.7f, -35f), new Vector3(0f, -23f, 0f), new Vector3(3.5f, 3.5f, 3.5f)),
                new Placement("SnowBank_NE_B", OptionalAssetCatalog[8], new Vector3(36f, -1.7f, -42f), new Vector3(0f, 42f, 0f), new Vector3(4.1f, 4.1f, 4.1f)),
                new Placement("SnowBank_SW_A", OptionalAssetCatalog[9], new Vector3(-45f, -1.7f, 37f), new Vector3(0f, 201f, 0f), new Vector3(3.4f, 3.4f, 3.4f)),
                new Placement("SnowBank_SW_B", OptionalAssetCatalog[10], new Vector3(-36f, -1.7f, 44f), new Vector3(0f, 184f, 0f), new Vector3(3.2f, 3.2f, 3.2f)),
                new Placement("SnowBank_SE_A", OptionalAssetCatalog[8], new Vector3(46f, -1.7f, 38f), new Vector3(0f, 152f, 0f), new Vector3(4.2f, 4.2f, 4.2f)),
                new Placement("SnowBank_SE_B", OptionalAssetCatalog[9], new Vector3(39f, -1.7f, 45f), new Vector3(0f, 129f, 0f), new Vector3(3.2f, 3.2f, 3.2f)),
            };

            for (int i = 0; i < placements.Length; i++)
                AddPrefab(parent, placements[i]);
        }

        static void AddLandmarks(Transform parent)
        {
            var northWest = CreateChild(parent, "NorthWestVillage");
            AddPrefab(northWest, OptionalAssetCatalog[12], "Cottage_NW", new Vector3(-77f, -6f, -23f), new Vector3(0f, 60f, 0f), new Vector3(9f, 9f, 9f));
            AddPrefab(northWest, OptionalAssetCatalog[14], "Snowman_NW", new Vector3(-68f, -1.6f, -28f), Vector3.zero, new Vector3(3.2f, 3.2f, 3.2f));
            AddPrefab(northWest, OptionalAssetCatalog[24], "Gift_NW_A", new Vector3(-70f, -1.5f, -21f), new Vector3(0f, 22f, 0f), new Vector3(3f, 3f, 3f));
            AddPrefab(northWest, OptionalAssetCatalog[26], "Gift_NW_B", new Vector3(-66f, -1.5f, -22f), new Vector3(0f, 9f, 0f), new Vector3(2.8f, 2.8f, 2.8f));
            AddPrefab(northWest, OptionalAssetCatalog[18], "Lantern_NW_Focal", new Vector3(-61f, -0.5f, -26f), new Vector3(0f, 31f, 0f), new Vector3(3.2f, 3.2f, 3.2f));

            var northEast = CreateChild(parent, "NorthEastCamp");
            AddPrefab(northEast, OptionalAssetCatalog[22], "Tree_NE", new Vector3(77f, -4f, -26f), new Vector3(0f, -50f, 0f), new Vector3(8f, 8f, 8f));
            AddPrefab(northEast, OptionalAssetCatalog[13], "Sled_NE", new Vector3(68f, -1.7f, -18f), new Vector3(0f, -34f, 0f), new Vector3(4.5f, 4.5f, 4.5f));
            AddPrefab(northEast, OptionalAssetCatalog[25], "Gift_NE_A", new Vector3(71f, -1.5f, -21f), new Vector3(0f, 28f, 0f), new Vector3(2.8f, 2.8f, 2.8f));
            AddPrefab(northEast, OptionalAssetCatalog[27], "Gift_NE_B", new Vector3(73f, -1.5f, -19f), new Vector3(0f, -16f, 0f), new Vector3(2.8f, 2.8f, 2.8f));
            AddPrefab(northEast, OptionalAssetCatalog[19], "Bench_NE", new Vector3(63f, -1.7f, -24f), new Vector3(0f, -10f, 0f), new Vector3(3.8f, 3.8f, 3.8f));

            var southWest = CreateChild(parent, "SouthWestYard");
            AddPrefab(southWest, OptionalAssetCatalog[20], "Wood_SW", new Vector3(-73f, -1.7f, 17f), new Vector3(0f, 182f, 0f), new Vector3(4.2f, 4.2f, 4.2f));
            AddPrefab(southWest, OptionalAssetCatalog[21], "Wheelbarrow_SW", new Vector3(-66f, -1.7f, 24f), new Vector3(0f, 210f, 0f), new Vector3(3.8f, 3.8f, 3.8f));
            AddPrefab(southWest, OptionalAssetCatalog[15], "Snowman_SW", new Vector3(-60f, -1.6f, 30f), Vector3.zero, new Vector3(3f, 3f, 3f));
            AddPrefab(southWest, OptionalAssetCatalog[11], "Barrel_SW", new Vector3(-70f, -1.6f, 27f), Vector3.zero, new Vector3(4f, 4f, 4f));
            AddPrefab(southWest, OptionalAssetCatalog[18], "Lantern_SW_Focal", new Vector3(-58f, -0.5f, 22f), new Vector3(0f, 190f, 0f), new Vector3(3.2f, 3.2f, 3.2f));

            var southEast = CreateChild(parent, "SouthEastVillage");
            AddPrefab(southEast, OptionalAssetCatalog[12], "Cottage_SE", new Vector3(76f, -6f, 24f), new Vector3(0f, 232f, 0f), new Vector3(8.8f, 8.8f, 8.8f));
            AddPrefab(southEast, OptionalAssetCatalog[23], "Tree_SE", new Vector3(67f, -4f, 33f), new Vector3(0f, 188f, 0f), new Vector3(7f, 7f, 7f));
            AddPrefab(southEast, OptionalAssetCatalog[24], "Gift_SE_A", new Vector3(70f, -1.5f, 26f), new Vector3(0f, 41f, 0f), new Vector3(2.8f, 2.8f, 2.8f));
            AddPrefab(southEast, OptionalAssetCatalog[25], "Gift_SE_B", new Vector3(73f, -1.5f, 28f), new Vector3(0f, -11f, 0f), new Vector3(2.8f, 2.8f, 2.8f));
            AddPrefab(southEast, OptionalAssetCatalog[18], "Lantern_SE_Focal", new Vector3(61f, -0.5f, 25f), new Vector3(0f, 156f, 0f), new Vector3(3.2f, 3.2f, 3.2f));
        }

        static void AddBackdrop(Transform midCliffs, Transform farMountains)
        {
            AddPrefab(midCliffs, OptionalAssetCatalog[7], "MidCliff_North", new Vector3(0f, -17f, -102f), new Vector3(0f, 0f, 0f), new Vector3(34f, 24f, 18f));
            AddPrefab(midCliffs, OptionalAssetCatalog[6], "MidCliff_West", new Vector3(-104f, -18f, 0f), new Vector3(0f, 96f, 0f), new Vector3(30f, 22f, 16f));
            AddPrefab(midCliffs, OptionalAssetCatalog[5], "MidCliff_East", new Vector3(106f, -18f, -4f), new Vector3(0f, -88f, 0f), new Vector3(30f, 22f, 16f));
            AddPrefab(midCliffs, OptionalAssetCatalog[4], "MidCliff_South", new Vector3(0f, -17f, 104f), new Vector3(0f, 180f, 0f), new Vector3(34f, 24f, 18f));
            AddPrefab(midCliffs, OptionalAssetCatalog[0], "MidPine_NW", new Vector3(-90f, -8f, -88f), new Vector3(0f, 18f, 0f), new Vector3(10f, 10f, 10f));
            AddPrefab(midCliffs, OptionalAssetCatalog[0], "MidPine_NE", new Vector3(92f, -8f, -86f), new Vector3(0f, -31f, 0f), new Vector3(10f, 10f, 10f));
            AddPrefab(midCliffs, OptionalAssetCatalog[0], "MidPine_SW", new Vector3(-88f, -8f, 91f), new Vector3(0f, 204f, 0f), new Vector3(10f, 10f, 10f));
            AddPrefab(midCliffs, OptionalAssetCatalog[0], "MidPine_SE", new Vector3(90f, -8f, 90f), new Vector3(0f, 157f, 0f), new Vector3(10f, 10f, 10f));

            AddPrefab(farMountains, OptionalAssetCatalog[28], "FarSnowBase", new Vector3(0f, -23f, 0f), Vector3.zero, new Vector3(18f, 18f, 18f));
            AddPrefab(farMountains, OptionalAssetCatalog[7], "FarMountain_NorthWest", new Vector3(-154f, -34f, -156f), new Vector3(0f, 34f, 0f), new Vector3(56f, 40f, 34f));
            AddPrefab(farMountains, OptionalAssetCatalog[6], "FarMountain_North", new Vector3(0f, -32f, -178f), new Vector3(0f, -6f, 0f), new Vector3(64f, 42f, 30f));
            AddPrefab(farMountains, OptionalAssetCatalog[5], "FarMountain_NorthEast", new Vector3(156f, -33f, -150f), new Vector3(0f, -38f, 0f), new Vector3(58f, 38f, 34f));
            AddPrefab(farMountains, OptionalAssetCatalog[4], "FarMountain_West", new Vector3(-184f, -34f, 2f), new Vector3(0f, 92f, 0f), new Vector3(62f, 42f, 30f));
            AddPrefab(farMountains, OptionalAssetCatalog[7], "FarMountain_East", new Vector3(186f, -34f, -8f), new Vector3(0f, -88f, 0f), new Vector3(62f, 42f, 30f));
            AddPrefab(farMountains, OptionalAssetCatalog[6], "FarMountain_SouthWest", new Vector3(-152f, -33f, 156f), new Vector3(0f, 144f, 0f), new Vector3(56f, 38f, 34f));
            AddPrefab(farMountains, OptionalAssetCatalog[5], "FarMountain_South", new Vector3(0f, -32f, 184f), new Vector3(0f, 180f, 0f), new Vector3(66f, 42f, 30f));
            AddPrefab(farMountains, OptionalAssetCatalog[4], "FarMountain_SouthEast", new Vector3(152f, -34f, 158f), new Vector3(0f, 216f, 0f), new Vector3(56f, 38f, 34f));
        }

        static void AddOptionalLights(Transform parent)
        {
            AddWarmLight(parent, "LanternGlow_NW", new Vector3(-61f, 6f, -26f), 0.75f, 18f);
            AddWarmLight(parent, "LanternGlow_NE", new Vector3(61f, 6f, -25f), 0.75f, 18f);
            AddWarmLight(parent, "LanternGlow_SW", new Vector3(-58f, 6f, 22f), 0.7f, 16f);
            AddWarmLight(parent, "LanternGlow_SE", new Vector3(61f, 6f, 25f), 0.7f, 16f);
        }

        static void AddWarmLight(Transform parent, string name, Vector3 localPosition, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(1f, 0.83f, 0.62f, 1f);
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        static void DisableBackdropShadows(Transform root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static Transform CreateChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        static void AddPrefab(Transform parent, Placement placement)
        {
            AddPrefab(parent, placement.AssetPath, placement.Name, placement.LocalPosition, placement.LocalEulerAngles, placement.LocalScale);
        }

        static void AddPrefab(Transform parent, string assetPath, string name, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                throw new InvalidOperationException($"Missing optional environment asset '{assetPath}'.");

            var instance = UnityEngine.Object.Instantiate(prefab);
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate '{assetPath}'.");

            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localEulerAngles = localEulerAngles;
            instance.transform.localScale = localScale;
        }

        static void StripColliders(Transform root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }

        static void EnsureAddressablesEntry(string assetPath, string address, string groupName)
        {
            var settingsDefaultType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsDefaultType == null)
                throw new InvalidOperationException("Addressables editor API unavailable.");

            object settings = null;
            var getSettings = settingsDefaultType.GetMethod(
                "GetSettings", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (getSettings != null)
                settings = getSettings.Invoke(null, new object[] { true });
            settings ??= settingsDefaultType
                .GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (settings == null)
                throw new InvalidOperationException("Could not load AddressableAssetSettings.");

            var settingsType = settings.GetType();
            var findGroupMethod = settingsType.GetMethod(
                "FindGroup", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            var createOrMoveEntryMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "CreateOrMoveEntry" && m.GetParameters().Length >= 2);
            var createGroupMethod = settingsType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "CreateGroup")
                        return false;

                    var parameters = m.GetParameters();
                    return parameters.Length >= 4
                        && parameters[0].ParameterType == typeof(string)
                        && parameters[1].ParameterType == typeof(bool);
                });

            if (findGroupMethod == null || createOrMoveEntryMethod == null || createGroupMethod == null)
                throw new InvalidOperationException("Required Addressables API methods not found.");

            var bundledSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.BundledAssetGroupSchema, Unity.Addressables.Editor");
            var contentUpdateSchemaType = Type.GetType(
                "UnityEditor.AddressableAssets.Settings.GroupSchemas.ContentUpdateGroupSchema, Unity.Addressables.Editor");

            var group = findGroupMethod.Invoke(settings, new object[] { groupName });
            if (group == null)
            {
                var templateGroup = findGroupMethod.Invoke(settings, new object[] { TemplateGroup });
                var parameters = createGroupMethod.GetParameters();
                var args = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    if (parameter.ParameterType == typeof(string))
                        args[i] = groupName;
                    else if (parameter.ParameterType == typeof(bool))
                        args[i] = false;
                    else if (parameter.ParameterType == typeof(Type[]))
                        args[i] = new[] { bundledSchemaType, contentUpdateSchemaType };
                    else if (parameter.HasDefaultValue)
                        args[i] = parameter.DefaultValue;
                    else
                        args[i] = parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
                }

                group = createGroupMethod.Invoke(settings, args);
                if (group != null && templateGroup != null)
                    CopySchemaSettings(templateGroup, group, bundledSchemaType, contentUpdateSchemaType);
            }

            if (group == null)
                throw new InvalidOperationException($"Failed to resolve Addressables group '{groupName}'.");

            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrWhiteSpace(guid))
                throw new InvalidOperationException($"Could not resolve GUID for '{assetPath}'.");

            var entryParameters = createOrMoveEntryMethod.GetParameters();
            object entry;
            if (entryParameters.Length >= 4)
                entry = createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false, false });
            else if (entryParameters.Length == 3)
                entry = createOrMoveEntryMethod.Invoke(settings, new[] { guid, group, (object)false });
            else
                entry = createOrMoveEntryMethod.Invoke(settings, new[] { guid, group });

            if (entry == null)
                throw new InvalidOperationException($"Failed to create Addressables entry for '{assetPath}'.");

            entry.GetType()
                .GetProperty("address", BindingFlags.Public | BindingFlags.Instance)
                ?.SetValue(entry, address);

            ForceRemoteBundledSchema(group, groupName, bundledSchemaType, contentUpdateSchemaType);
            SafeSetDirty(entry);
            SafeSetDirty(group);
        }

        static void CopySchemaSettings(object sourceGroup, object targetGroup, Type bundledType, Type contentUpdateType)
        {
            var getSchema = sourceGroup.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchema == null)
                return;

            foreach (var schemaType in new[] { bundledType, contentUpdateType })
            {
                if (schemaType == null)
                    continue;

                var sourceSchema = getSchema.Invoke(sourceGroup, new object[] { schemaType });
                var targetSchema = getSchema.Invoke(targetGroup, new object[] { schemaType });
                if (sourceSchema == null || targetSchema == null)
                    continue;

                foreach (var property in schemaType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanRead || !property.CanWrite)
                        continue;
                    if (string.Equals(property.Name, "Group", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (property.GetIndexParameters().Length != 0)
                        continue;

                    try
                    {
                        property.SetValue(targetSchema, property.GetValue(sourceSchema));
                    }
                    catch
                    {
                    }
                }

                SafeSetDirty(targetSchema);
            }

            SafeSetDirty(targetGroup);
        }

        static void ForceRemoteBundledSchema(object group, string groupName, Type bundledSchemaType, Type contentUpdateSchemaType)
        {
            if (group == null || string.IsNullOrWhiteSpace(groupName) || bundledSchemaType == null)
                return;

            var getSchema = group.GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "GetSchema"
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Type));
            if (getSchema == null)
                return;

            var bundledSchema = getSchema.Invoke(group, new object[] { bundledSchemaType }) as UnityEngine.Object;
            if (bundledSchema == null)
                return;

            var serialized = new SerializedObject(bundledSchema);
            serialized.FindProperty("m_Name")?.SetValueIfPresent($"{groupName}_BundledAssetGroupSchema");
            serialized.FindProperty("m_BuildPath.m_Id")?.SetValueIfPresent(RemoteBuildPathProfileId);
            serialized.FindProperty("m_LoadPath.m_Id")?.SetValueIfPresent(RemoteLoadPathProfileId);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SafeSetDirty(bundledSchema);

            NormalizeSchemaName(group, getSchema, contentUpdateSchemaType, $"{groupName}_ContentUpdateGroupSchema");
        }

        static void NormalizeSchemaName(object group, MethodInfo getSchema, Type schemaType, string expectedName)
        {
            if (group == null || getSchema == null || schemaType == null || string.IsNullOrWhiteSpace(expectedName))
                return;

            var schema = getSchema.Invoke(group, new object[] { schemaType }) as UnityEngine.Object;
            if (schema == null)
                return;

            var serialized = new SerializedObject(schema);
            serialized.FindProperty("m_Name")?.SetValueIfPresent(expectedName);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            SafeSetDirty(schema);
        }

        static void WriteReport()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string reportPath = Path.Combine(projectRoot, ReportRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? projectRoot);

            var builder = new StringBuilder();
            builder.AppendLine("# Game_ML Winter Environment Addressables");
            builder.AppendLine();
            builder.AppendLine($"Generated: `{DateTime.Now:yyyy-MM-dd HH:mm:ss}`");
            builder.AppendLine();
            builder.AppendLine("## Critical Content");
            builder.AppendLine();
            builder.AppendLine($"- Group: `{CriticalGroupName}`");
            builder.AppendLine($"- Address: `{RemoteContentManager.GameMlEnvironmentAddress}`");
            builder.AppendLine($"- Prefab: `{CriticalPrefabPath}`");
            builder.AppendLine("- Loaded before match start.");
            builder.AppendLine("- Keeps the current gameplay-required board, bridges, forge areas, lane geometry, path markers, and map shell intact.");
            builder.AppendLine();
            builder.AppendLine("## Optional Content");
            builder.AppendLine();
            builder.AppendLine($"- Group: `{OptionalGroupName}`");
            builder.AppendLine($"- Address: `{RemoteContentManager.GameMlEnvironmentDressingAddress}`");
            builder.AppendLine($"- Prefab: `{OptionalPrefabPath}`");
            builder.AppendLine("- Streams after the critical environment is present.");
            builder.AppendLine("- Contains only collider-free visual dressing: edge cliffs, pines, rock borders, snow details, landmark props, backdrop silhouettes, and optional warm accent lights.");
            builder.AppendLine();
            builder.AppendLine("## Shared Content");
            builder.AppendLine();
            builder.AppendLine($"- Group: `{SharedGroupName}`");
            foreach (var asset in SharedAssetCatalog)
                builder.AppendLine($"- `{asset.assetPath}` -> `{asset.address}`");
            builder.AppendLine();
            builder.AppendLine("## Optional Winter Pack Assets Used");
            builder.AppendLine();
            foreach (var asset in OptionalAssetCatalog.Distinct())
                builder.AppendLine($"- `{asset}`");
            builder.AppendLine();
            builder.AppendLine("## Performance Notes");
            builder.AppendLine();
            builder.AppendLine("- Optional props are collider-free so they do not affect gameplay pathing or collision.");
            builder.AppendLine("- Backdrop renderers have shadows disabled to keep the streamed dressing lighter for WebGL/mobile.");
            builder.AppendLine("- Reused a small curated subset of winter prefabs with scale and rotation variation instead of pulling the full demo scene.");
            builder.AppendLine("- Optional warm lights are limited to four non-shadowed point lights near focal landmarks.");
            builder.AppendLine();
            builder.AppendLine("## Load Order");
            builder.AppendLine();
            builder.AppendLine($"- `EnvironmentLoader` loads the required environment and instantiates it as `{RemoteContentManager.GameMlEnvironmentRootName}`.");
            builder.AppendLine($"- `OptionalEnvironmentLoader` waits for `{RemoteContentManager.GameMlEnvironmentRootName}`, then downloads and instantiates `{RemoteContentManager.GameMlEnvironmentDressingRootName}`.");
            builder.AppendLine("- If optional dressing fails, gameplay still starts and continues with the critical map only.");

            File.WriteAllText(reportPath, builder.ToString());
        }

        static void SetValueIfPresent(this SerializedProperty property, string value)
        {
            if (property != null)
                property.stringValue = value;
        }

        static void SafeSetDirty(object value)
        {
            if (value is UnityEngine.Object unityObject && unityObject != null)
                EditorUtility.SetDirty(unityObject);
        }

        readonly struct Placement
        {
            public Placement(string name, string assetPath, Vector3 localPosition, Vector3 localEulerAngles, Vector3 localScale)
            {
                Name = name;
                AssetPath = assetPath;
                LocalPosition = localPosition;
                LocalEulerAngles = localEulerAngles;
                LocalScale = localScale;
            }

            public string Name { get; }
            public string AssetPath { get; }
            public Vector3 LocalPosition { get; }
            public Vector3 LocalEulerAngles { get; }
            public Vector3 LocalScale { get; }
        }
    }
}
#endif
