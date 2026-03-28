using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CastleDefender.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class BlacksmithUpgradeMaterialRuntimeTests
{
    const float DefaultTimeoutSeconds = 75f;
    static readonly string[] LaneKeys = { "red", "gold", "blue", "green" };
    static readonly string[] LegacyBannerMaterialPrefixes =
    {
        "TT_Banner_red",
        "TT_Banner_blue_A",
        "TT_Banner_green_A",
        "TT_Banner_yellow",
    };

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
    public IEnumerator GameMl_Blacksmith_Upgrade_Uses_Urp_Materials_Across_Reload()
    {
        yield return LoadToGameMl();
        yield return ValidateBlacksmithStage(tier: 1, stageLabel: "initial_load");
        yield return ValidateBlacksmithStage(tier: 2, stageLabel: "after_upgrade");

        LoadingScreen.LoadScene("PostGame");
        yield return WaitForActiveScene("PostGame");
        LoadingScreen.LoadScene("Lobby");
        yield return WaitForActiveScene("Lobby");
        LoadingScreen.LoadScene("Loadout");
        yield return WaitForActiveScene("Loadout");
        LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML", preloadEnvironment: true);
        yield return WaitForActiveScene("Game_ML");

        yield return ValidateBlacksmithStage(tier: 2, stageLabel: "after_scene_reload");
    }

    static IEnumerator ValidateBlacksmithStage(int tier, string stageLabel)
    {
        var snapshotApplier = UnityEngine.Object.FindFirstObjectByType<SnapshotApplier>(FindObjectsInactive.Include);
        Assert.That(snapshotApplier, Is.Not.Null, $"SnapshotApplier should exist for stage '{stageLabel}'.");

        var blacksmithPad = FindFortressPad("blacksmith", "Red");
        Assert.That(blacksmithPad, Is.Not.Null, $"A red-lane blacksmith pad should exist in Game_ML for stage '{stageLabel}'.");

        snapshotApplier.DebugApplyMLSnapshot(BuildSnapshot(tier), myLaneIndex: 0, viewingLane: 0, totalLanes: LaneKeys.Length);
        yield return null;

        var generatedRoot = FindGeneratedVisualRoot(blacksmithPad);
        Assert.That(generatedRoot, Is.Not.Null, $"The generated blacksmith visual should exist for stage '{stageLabel}'.");

        int activeTier = ReadTieredVisualCurrentTier(generatedRoot);
        Assert.That(activeTier, Is.EqualTo(tier), $"Stage '{stageLabel}' should resolve blacksmith tier {tier}.");

        var renderers = generatedRoot.GetComponentsInChildren<Renderer>(true);
        Assert.That(renderers, Is.Not.Empty, $"Stage '{stageLabel}' should expose renderer components on the generated blacksmith visual.");

        var materialNames = new List<string>();
        bool sawBaseBuildingMaterial = false;
        bool sawTeamAccentMaterial = false;

        foreach (var renderer in renderers)
        {
            Assert.That(renderer, Is.Not.Null, $"Stage '{stageLabel}' should not contain null renderers.");
            var materials = renderer.materials;
            Assert.That(materials, Is.Not.Empty, $"Renderer '{renderer.name}' should have runtime materials during stage '{stageLabel}'.");

            foreach (var material in materials)
            {
                Assert.That(material, Is.Not.Null, $"Renderer '{renderer.name}' should not receive a null runtime material during stage '{stageLabel}'.");
                Assert.That(material.shader, Is.Not.Null, $"Material '{material.name}' should keep a shader during stage '{stageLabel}'.");
                Assert.That(
                    material.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal),
                    Is.True,
                    $"Renderer '{renderer.name}' used non-URP shader '{material.shader.name}' via material '{material.name}' during stage '{stageLabel}'.");

                materialNames.Add(material.name);
                if (material.name.StartsWith("TT_BuildingBase_Neutral", StringComparison.Ordinal))
                    sawBaseBuildingMaterial = true;
                if (material.name.StartsWith("TT_BannerAccent_", StringComparison.Ordinal))
                    sawTeamAccentMaterial = true;

                foreach (string legacyPrefix in LegacyBannerMaterialPrefixes)
                {
                    Assert.That(
                        material.name.StartsWith(legacyPrefix, StringComparison.Ordinal),
                        Is.False,
                        $"Legacy banner material '{material.name}' leaked into stage '{stageLabel}'.");
                }
            }
        }

        Assert.That(sawBaseBuildingMaterial, Is.True, $"Stage '{stageLabel}' should keep the URP building base material on the blacksmith body.");
        if (tier >= 2)
            Assert.That(sawTeamAccentMaterial, Is.True, $"Stage '{stageLabel}' should apply the URP team accent banner material after upgrade.");

        Debug.Log($"[BlacksmithUpgradeMaterialRuntimeTests] {stageLabel} tier={tier} materials={string.Join(", ", materialNames.Distinct().OrderBy(name => name, StringComparer.Ordinal))}");
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

            string candidateBuildingType = type.GetProperty("BuildingType", BindingFlags.Instance | BindingFlags.Public)?.GetValue(behaviour) as string;
            string candidateLaneColor = type.GetProperty("AnchorLaneColor", BindingFlags.Instance | BindingFlags.Public)?.GetValue(behaviour)?.ToString();
            if (string.Equals(candidateBuildingType, buildingType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidateLaneColor, laneColorName, StringComparison.Ordinal))
            {
                return behaviour.gameObject;
            }
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

            if (string.Equals(behaviour.GetType().FullName, "CastleDefender.Game.TieredBuildingVisual", StringComparison.Ordinal))
                return behaviour.gameObject;
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

            object value = type.GetProperty("CurrentTier", BindingFlags.Instance | BindingFlags.Public)?.GetValue(behaviour);
            return value is int tier ? tier : 0;
        }

        return 0;
    }

    static MLSnapshot BuildSnapshot(int blacksmithTier)
    {
        return new MLSnapshot
        {
            lanes = LaneKeys
                .Select((laneKey, laneIndex) => BuildLane(laneKey, laneIndex, blacksmithTier))
                .ToArray()
        };
    }

    static MLLaneSnap BuildLane(string laneKey, int laneIndex, int blacksmithTier)
    {
        var lane = new MLLaneSnap
        {
            laneIndex = laneIndex,
            team = laneKey,
            side = laneKey,
            slotColor = laneKey,
            fortressPads = Array.Empty<MLFortressPad>(),
            barracksSites = Array.Empty<MLBarracksSite>(),
            barracksRoster = Array.Empty<MLBarracksRosterEntry>(),
            heroRoster = Array.Empty<MLHeroRosterEntry>(),
            upcomingWave = new MLUpcomingWave { entries = Array.Empty<MLUpcomingWaveEntry>() },
            upcomingWaveQueue = Array.Empty<MLUpcomingWave>(),
            path = Array.Empty<MLGridPos>(),
            units = Array.Empty<MLUnit>(),
            projectiles = Array.Empty<MLProjectile>(),
        };

        if (laneIndex == 0)
        {
            lane.fortressPads = new[]
            {
                new MLFortressPad
                {
                    padId = "blacksmith_pad",
                    buildingType = "blacksmith",
                    buildingName = "Blacksmith",
                    displayName = "Blacksmith",
                    buildState = "built",
                    tier = Mathf.Max(1, blacksmithTier),
                    maxTier = 3,
                    isBuilt = true,
                    canBuild = false,
                    canUpgrade = blacksmithTier < 3,
                    currentTierName = $"Tier {Mathf.Max(1, blacksmithTier)}",
                }
            };
        }

        return lane;
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
