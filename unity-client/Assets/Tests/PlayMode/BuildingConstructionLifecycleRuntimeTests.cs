using System;
using System.Collections;
using System.Collections.Generic;
using CastleDefender.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class BuildingConstructionLifecycleRuntimeTests
{
    const float DefaultTimeoutSeconds = 75f;
    static readonly string[] LaneKeys = { "red", "gold", "blue", "green" };

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        LogAssert.ignoreFailingMessages = true;
        RemoteContentVerification.ClearFailureCounts();
        RemoteContentVerification.ClearEvidence();
        yield return DestroyDontDestroyOnLoadRoots();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        RemoteContentVerification.ClearFailureCounts();
        RemoteContentVerification.ClearEvidence();
        yield return DestroyDontDestroyOnLoadRoots();
        LogAssert.ignoreFailingMessages = false;
    }

    [UnityTest]
    public IEnumerator GameMl_Construction_Uses_TargetTier_Tt_Construction_Visuals()
    {
        yield return LoadToGameMl();

        var snapshotApplier = UnityEngine.Object.FindFirstObjectByType<SnapshotApplier>(FindObjectsInactive.Include);
        Assert.That(snapshotApplier, Is.Not.Null, "SnapshotApplier should exist in Game_ML.");

        var blacksmithPad = FindFortressPad("blacksmith", "Red");
        Assert.That(blacksmithPad, Is.Not.Null, "A red-lane blacksmith pad should exist in Game_ML.");

        ApplyFortressSnapshot(
            snapshotApplier,
            BuildPad(
                ReadFortressPadId(blacksmithPad),
                "blacksmith",
                "Blacksmith",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Blacksmith"));
        blacksmithPad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        yield return null;

        var blacksmithVisual = FindGeneratedVisualRoot(blacksmithPad.gameObject);
        Assert.That(blacksmithVisual, Is.Not.Null, "Blacksmith pad should spawn a generated visual root while constructing.");
        AssertConstructionStageState(
            blacksmithVisual,
            expectedTier: 1,
            expectedConstructionTier: 1,
            expectedStageIndex: 0,
            hiddenTierRoots: new[] { "BaseModel" });

        var townCorePad = FindFortressPad("town_core", "Red");
        Assert.That(townCorePad, Is.Not.Null, "A red-lane town core pad should exist in Game_ML.");

        ApplyFortressSnapshot(
            snapshotApplier,
            BuildPad(
                ReadFortressPadId(townCorePad),
                "town_core",
                "Town Core",
                built: true,
                tier: 1,
                maxTier: 4,
                constructing: true,
                constructionTargetTier: 2,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Town Hall"));
        townCorePad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        yield return null;

        var townCoreVisual = FindGeneratedVisualRoot(townCorePad.gameObject);
        Assert.That(townCoreVisual, Is.Not.Null, "Town core pad should spawn a generated visual root while upgrading.");
        AssertConstructionStageState(
            townCoreVisual,
            expectedTier: 2,
            expectedConstructionTier: 2,
            expectedStageIndex: 0,
            hiddenTierRoots: new[] { "BaseModel", "Tier2_Visuals" });

        ApplyFortressSnapshot(
            snapshotApplier,
            BuildPad(
                ReadFortressPadId(townCorePad),
                "town_core",
                "Town Hall",
                built: true,
                tier: 2,
                maxTier: 4,
                constructing: false,
                constructionTargetTier: 2,
                constructionProgress01: 1f,
                constructionTargetTierName: "Town Hall"));
        townCorePad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        yield return null;

        AssertFinishedTierState(townCoreVisual, expectedTier: 2, visibleTierRoots: new[] { "Tier2_Visuals" });
    }

    [UnityTest]
    public IEnumerator GameMl_SharedDefensePads_Use_Generated_Construction_Lifecycle_Visuals()
    {
        yield return LoadToGameMl();

        var snapshotApplier = UnityEngine.Object.FindFirstObjectByType<SnapshotApplier>(FindObjectsInactive.Include);
        Assert.That(snapshotApplier, Is.Not.Null, "SnapshotApplier should exist in Game_ML.");

        var wallPad = FindFortressPad("wall", "Red");
        var gatePad = FindFortressPad("gate", "Red");
        var turretPad = FindFortressPad("turret", "Red");

        Assert.That(wallPad, Is.Not.Null, "A red-lane wall pad should exist in Game_ML.");
        Assert.That(gatePad, Is.Not.Null, "A red-lane gate pad should exist in Game_ML.");
        Assert.That(turretPad, Is.Not.Null, "A red-lane turret pad should exist in Game_ML.");

        ApplyFortressSnapshot(
            snapshotApplier,
            BuildPad(
                ReadFortressPadId(wallPad),
                "wall",
                "Wall",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Wall 1"),
            BuildPad(
                ReadFortressPadId(gatePad),
                "gate",
                "Gate",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Gate 1"),
            BuildPad(
                ReadFortressPadId(turretPad),
                "turret",
                "Turret",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Turret 1"));

        wallPad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        gatePad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        turretPad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        yield return null;

        var wallVisual = FindGeneratedVisualRoot(wallPad);
        var gateVisual = FindGeneratedVisualRoot(gatePad);
        var turretVisual = FindGeneratedVisualRoot(turretPad);

        Assert.That(wallVisual, Is.Not.Null, "Wall pad should spawn a generated visual root while constructing.");
        Assert.That(gateVisual, Is.Not.Null, "Gate pad should spawn a generated visual root while constructing.");
        Assert.That(turretVisual, Is.Not.Null, "Turret pad should spawn a generated visual root while constructing.");

        AssertConstructionStageState(
            wallVisual,
            expectedTier: 1,
            expectedConstructionTier: 1,
            expectedStageIndex: 0,
            hiddenTierRoots: new[] { "BaseModel" });
        AssertConstructionStageState(
            gateVisual,
            expectedTier: 1,
            expectedConstructionTier: 1,
            expectedStageIndex: 0,
            hiddenTierRoots: new[] { "BaseModel" });
        AssertConstructionStageState(
            turretVisual,
            expectedTier: 1,
            expectedConstructionTier: 1,
            expectedStageIndex: 0,
            hiddenTierRoots: new[] { "BaseModel" });
    }

    [UnityTest]
    public IEnumerator GameMl_SharedDefenseConstruction_Shows_Supplemental_Grouped_Segments()
    {
        yield return LoadToGameMl();

        var snapshotApplier = UnityEngine.Object.FindFirstObjectByType<SnapshotApplier>(FindObjectsInactive.Include);
        Assert.That(snapshotApplier, Is.Not.Null, "SnapshotApplier should exist in Game_ML.");

        var wallFrontLeft01Pad = FindFortressPadById("wall_front_left_01_pad");
        var wallFrontLeft02Pad = FindFortressPadById("wall_front_left_02_pad");
        var turretFrontLeftPad = FindFortressPadById("turret_front_left_pad");

        Assert.That(wallFrontLeft01Pad, Is.Not.Null, "Wall front-left 01 pad should exist in Game_ML.");
        Assert.That(wallFrontLeft02Pad, Is.Not.Null, "Wall front-left 02 pad should exist in Game_ML.");
        Assert.That(turretFrontLeftPad, Is.Not.Null, "Turret front-left pad should exist in Game_ML.");

        var wallFrontLeft01Supplemental = FindSceneObjectByName("Red_Wall_Front_Left_01_Lvl_1 (1)");
        var wallFrontLeft02Supplemental = FindSceneObjectByName("Red_Wall_Front_Left_02_Lvl_1 (1)");
        var turretFrontLeftSupplemental = FindSceneObjectByName("Red_Tower_Front_Left_Lvl_1 (1)");

        Assert.That(wallFrontLeft01Supplemental, Is.Not.Null, "Grouped wall segment 'Red_Wall_Front_Left_01_Lvl_1 (1)' should exist in the live environment.");
        Assert.That(wallFrontLeft02Supplemental, Is.Not.Null, "Grouped wall segment 'Red_Wall_Front_Left_02_Lvl_1 (1)' should exist in the live environment.");
        Assert.That(turretFrontLeftSupplemental, Is.Not.Null, "Grouped tower segment 'Red_Tower_Front_Left_Lvl_1 (1)' should exist in the live environment.");

        ApplyFortressSnapshot(
            snapshotApplier,
            BuildPad(
                "wall_front_left_01_pad",
                "wall",
                "Wall",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Wall 1"),
            BuildPad(
                "wall_front_left_02_pad",
                "wall",
                "Wall",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Wall 1"),
            BuildPad(
                "turret_front_left_pad",
                "turret",
                "Turret",
                built: false,
                tier: 1,
                maxTier: 3,
                constructing: true,
                constructionTargetTier: 1,
                constructionProgress01: 0.10f,
                constructionTargetTierName: "Turret 1"));

        wallFrontLeft01Pad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        wallFrontLeft02Pad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        turretFrontLeftPad.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);
        yield return null;

        Assert.That(
            FindGeneratedVisualRoot(wallFrontLeft01Supplemental),
            Is.Not.Null,
            "The grouped wall segment 'Red_Wall_Front_Left_01_Lvl_1 (1)' should receive a generated construction visual.");
        Assert.That(
            FindGeneratedVisualRoot(wallFrontLeft02Supplemental),
            Is.Not.Null,
            "The grouped wall segment 'Red_Wall_Front_Left_02_Lvl_1 (1)' should receive a generated construction visual.");
        Assert.That(
            FindGeneratedVisualRoot(turretFrontLeftSupplemental),
            Is.Not.Null,
            "The grouped tower segment 'Red_Tower_Front_Left_Lvl_1 (1)' should receive a generated construction visual.");
    }

    static void AssertConstructionStageState(
        GameObject generatedRoot,
        int expectedTier,
        int expectedConstructionTier,
        int expectedStageIndex,
        string[] hiddenTierRoots)
    {
        Assert.That(
            ReadTieredVisualCurrentTier(generatedRoot),
            Is.EqualTo(expectedTier),
            $"Generated root '{generatedRoot.name}' should resolve tier {expectedTier} while constructing.");

        string constructionGroupPath = $"ConstructionTargetTier_{expectedConstructionTier}";
        var constructionGroup = FindChildByPath(generatedRoot.transform, constructionGroupPath);
        Assert.That(constructionGroup, Is.Not.Null, $"Generated root '{generatedRoot.name}' is missing '{constructionGroupPath}'.");
        Assert.That(constructionGroup.gameObject.activeSelf, Is.True, $"Construction group '{constructionGroupPath}' should be active.");

        for (int stageIndex = 0; stageIndex < 2; stageIndex++)
        {
            string stagePath = $"{constructionGroupPath}/ConstructionStage_{stageIndex + 1}";
            var stageRoot = FindChildByPath(generatedRoot.transform, stagePath);
            Assert.That(stageRoot, Is.Not.Null, $"Generated root '{generatedRoot.name}' is missing '{stagePath}'.");

            bool shouldBeActive = stageIndex == expectedStageIndex;
            Assert.That(
                stageRoot.gameObject.activeSelf,
                Is.EqualTo(shouldBeActive),
                $"Construction stage '{stagePath}' active state was incorrect.");
            Assert.That(
                stageRoot.gameObject.activeInHierarchy,
                Is.EqualTo(shouldBeActive),
                $"Construction stage '{stagePath}' hierarchy visibility was incorrect.");
        }

        if (hiddenTierRoots == null)
            return;

        for (int i = 0; i < hiddenTierRoots.Length; i++)
        {
            string rootPath = hiddenTierRoots[i];
            var tierRoot = FindChildByPath(generatedRoot.transform, rootPath);
            Assert.That(tierRoot, Is.Not.Null, $"Generated root '{generatedRoot.name}' is missing '{rootPath}'.");
            Assert.That(tierRoot.gameObject.activeSelf, Is.False, $"Tier root '{rootPath}' should be hidden while construction stages are active.");
        }
    }

    static void AssertFinishedTierState(GameObject generatedRoot, int expectedTier, string[] visibleTierRoots)
    {
        Assert.That(
            ReadTieredVisualCurrentTier(generatedRoot),
            Is.EqualTo(expectedTier),
            $"Generated root '{generatedRoot.name}' should resolve tier {expectedTier} after construction completes.");

        for (int tier = 1; tier <= 4; tier++)
        {
            string groupPath = $"ConstructionTargetTier_{tier}";
            var group = FindChildByPath(generatedRoot.transform, groupPath);
            if (group == null)
                continue;

            Assert.That(group.gameObject.activeSelf, Is.False, $"Construction group '{groupPath}' should be hidden after construction completes.");
        }

        for (int i = 0; i < visibleTierRoots.Length; i++)
        {
            string rootPath = visibleTierRoots[i];
            var tierRoot = FindChildByPath(generatedRoot.transform, rootPath);
            Assert.That(tierRoot, Is.Not.Null, $"Generated root '{generatedRoot.name}' is missing '{rootPath}'.");
            Assert.That(tierRoot.gameObject.activeSelf, Is.True, $"Tier root '{rootPath}' should be visible after construction completes.");
        }
    }

    static Transform FindChildByPath(Transform root, string relativePath)
    {
        if (root == null || string.IsNullOrWhiteSpace(relativePath))
            return null;

        var current = root;
        var segments = relativePath.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            current = current.Find(segments[i]);
            if (current == null)
                return null;
        }

        return current;
    }

    static void ApplyFortressSnapshot(SnapshotApplier snapshotApplier, params MLFortressPad[] pads)
    {
        snapshotApplier.DebugApplyMLSnapshot(BuildSnapshot(pads), myLaneIndex: 0, viewingLane: 0, totalLanes: LaneKeys.Length);
    }

    static MLSnapshot BuildSnapshot(IReadOnlyList<MLFortressPad> pads)
    {
        return new MLSnapshot
        {
            lanes = BuildLanes(pads)
        };
    }

    static MLLaneSnap[] BuildLanes(IReadOnlyList<MLFortressPad> pads)
    {
        var lanes = new MLLaneSnap[LaneKeys.Length];
        for (int laneIndex = 0; laneIndex < LaneKeys.Length; laneIndex++)
        {
            lanes[laneIndex] = new MLLaneSnap
            {
                laneIndex = laneIndex,
                team = LaneKeys[laneIndex],
                side = LaneKeys[laneIndex],
                slotColor = LaneKeys[laneIndex],
                fortressPads = laneIndex == 0 ? ToArray(pads) : Array.Empty<MLFortressPad>(),
                barracksSites = Array.Empty<MLBarracksSite>(),
                barracksRoster = Array.Empty<MLBarracksRosterEntry>(),
                heroRoster = Array.Empty<MLHeroRosterEntry>(),
                upcomingWave = new MLUpcomingWave { entries = Array.Empty<MLUpcomingWaveEntry>() },
                upcomingWaveQueue = Array.Empty<MLUpcomingWave>(),
                path = Array.Empty<MLGridPos>(),
                units = Array.Empty<MLUnit>(),
                projectiles = Array.Empty<MLProjectile>(),
            };
        }

        return lanes;
    }

    static MLFortressPad[] ToArray(IReadOnlyList<MLFortressPad> pads)
    {
        if (pads == null || pads.Count == 0)
            return Array.Empty<MLFortressPad>();

        var results = new MLFortressPad[pads.Count];
        for (int i = 0; i < pads.Count; i++)
            results[i] = pads[i];
        return results;
    }

    static MLFortressPad BuildPad(
        string padId,
        string buildingType,
        string displayName,
        bool built,
        int tier,
        int maxTier,
        bool constructing,
        int constructionTargetTier,
        float constructionProgress01,
        string constructionTargetTierName)
    {
        int safeTier = Mathf.Max(1, tier);
        int safeTargetTier = Mathf.Max(1, constructionTargetTier);

        return new MLFortressPad
        {
            padId = padId,
            buildingType = buildingType,
            buildingName = displayName,
            displayName = displayName,
            buildState = constructing ? "constructing" : (built ? "built" : "empty"),
            lifecycleState = constructing ? "constructing" : (built ? "built" : "empty"),
            tier = safeTier,
            maxTier = Mathf.Max(safeTier, maxTier),
            isBuilt = built,
            isConstructing = constructing,
            constructionTargetTier = safeTargetTier,
            constructionTargetTierName = constructionTargetTierName,
            constructionProgress01 = Mathf.Clamp01(constructionProgress01),
            isDestroyed = false,
            isUnderRepair = false,
            canBuild = !built && !constructing,
            canUpgrade = built && !constructing && safeTier < maxTier,
            currentTierName = $"Tier {safeTier}",
            hp = 100f,
            maxHp = 100f,
        };
    }

    static IEnumerator LoadToGameMl()
    {
        SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
        yield return null;
        yield return WaitForActiveScene("Login");

        LoadingScreen.LoadScene("Lobby");
        yield return WaitForActiveScene("Lobby");

        LoadingScreen.LoadScene("Loadout");
        yield return WaitForActiveScene("Loadout");

        LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML", preloadEnvironment: true);
        yield return WaitForActiveScene("Game_ML");
    }

    static IEnumerator WaitForActiveScene(string sceneName, float timeoutSeconds = DefaultTimeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded && string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal))
                yield break;

            yield return null;
        }

        Assert.Fail($"Timed out waiting for active scene '{sceneName}'. Active='{SceneManager.GetActiveScene().name}'.");
    }

    static GameObject FindFortressPad(string buildingType, string laneColorName)
    {
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null)
                continue;

            var type = behaviour.GetType();
            if (!string.Equals(type.FullName, "CastleDefender.Game.FortressPadAnchor", StringComparison.Ordinal))
                continue;

            string candidateBuildingType = type.GetProperty("BuildingType")?.GetValue(behaviour) as string;
            string candidateLaneColor = type.GetProperty("AnchorLaneColor")?.GetValue(behaviour)?.ToString();
            if (string.Equals(candidateBuildingType, buildingType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateLaneColor, laneColorName, StringComparison.Ordinal))
            {
                return behaviour.gameObject;
            }
        }

        return null;
    }

    static GameObject FindFortressPadById(string padId)
    {
        if (string.IsNullOrWhiteSpace(padId))
            return null;

        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (behaviour == null)
                continue;

            var type = behaviour.GetType();
            if (!string.Equals(type.FullName, "CastleDefender.Game.FortressPadAnchor", StringComparison.Ordinal))
                continue;

            string candidatePadId = type.GetProperty("PadId")?.GetValue(behaviour) as string;
            if (string.Equals(candidatePadId, padId, StringComparison.OrdinalIgnoreCase))
                return behaviour.gameObject;
        }

        return null;
    }

    static GameObject FindSceneObjectByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var transform in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (transform != null && string.Equals(transform.name, name, StringComparison.Ordinal))
                return transform.gameObject;
        }

        return null;
    }

    static GameObject FindGeneratedVisualRoot(GameObject padRoot)
    {
        if (padRoot == null)
            return null;

        foreach (var behaviour in padRoot.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;

            if (string.Equals(behaviour.GetType().FullName, "CastleDefender.Game.TieredBuildingVisual", StringComparison.Ordinal)
                && behaviour.transform.parent == padRoot.transform)
            {
                return behaviour.gameObject;
            }
        }

        return null;
    }

    static int ReadTieredVisualCurrentTier(GameObject generatedRoot)
    {
        if (generatedRoot == null)
            return 0;

        foreach (var behaviour in generatedRoot.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null)
                continue;

            var type = behaviour.GetType();
            if (!string.Equals(type.FullName, "CastleDefender.Game.TieredBuildingVisual", StringComparison.Ordinal))
                continue;

            object value = type.GetProperty("CurrentTier")?.GetValue(behaviour);
            return value is int tier ? tier : 0;
        }

        return 0;
    }

    static string ReadFortressPadId(GameObject padRoot)
    {
        if (padRoot == null)
            return null;

        foreach (var behaviour in padRoot.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null)
                continue;

            var type = behaviour.GetType();
            if (!string.Equals(type.FullName, "CastleDefender.Game.FortressPadAnchor", StringComparison.Ordinal))
                continue;

            return type.GetProperty("PadId")?.GetValue(behaviour) as string;
        }

        return null;
    }

    static IEnumerator DestroyDontDestroyOnLoadRoots()
    {
        var probe = new GameObject("PlayModeCleanupProbe");
        UnityEngine.Object.DontDestroyOnLoad(probe);
        Scene ddolScene = probe.scene;
        foreach (GameObject root in ddolScene.GetRootGameObjects())
        {
            if (root == probe)
                continue;

            UnityEngine.Object.Destroy(root);
        }

        UnityEngine.Object.Destroy(probe);
        yield return null;
        yield return null;
    }
}
