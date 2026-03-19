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
using UnityEngine.UI;

public class BootstrapRemoteSceneFlowTests
{
    const float DefaultTimeoutSeconds = 45f;

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
    public IEnumerator Bootstrap_Loads_Remote_Login_Scene()
    {
        yield return LoadBootstrapFresh();
        yield return WaitForActiveScene("Login");

        Assert.That(SceneManager.GetSceneByName("Bootstrap").isLoaded, Is.True, "Bootstrap should remain loaded as the persistent local scene.");
        Assert.That(SceneManager.GetSceneByName("Login").isLoaded, Is.True, "Login should be loaded remotely.");
        Assert.That(GetTrackedRemoteSceneNames(), Is.EqualTo(new[] { "Login" }));
    }

    [UnityTest]
    public IEnumerator Sequential_Remote_Transitions_Unload_Previous_Remote_Scene_Handles()
    {
        yield return LoadBootstrapFresh();
        yield return WaitForActiveScene("Login");

        LoadingScreen.LoadScene("Lobby");
        yield return WaitForActiveScene("Lobby");
        Assert.That(SceneManager.GetSceneByName("Login").isLoaded, Is.False, "Login should unload after Lobby activates.");
        Assert.That(GetTrackedRemoteSceneNames(), Is.EqualTo(new[] { "Lobby" }));

        LoadingScreen.LoadScene("Loadout");
        yield return WaitForActiveScene("Loadout");
        Assert.That(SceneManager.GetSceneByName("Lobby").isLoaded, Is.False, "Lobby should unload after Loadout activates.");
        Assert.That(GetTrackedRemoteSceneNames(), Is.EqualTo(new[] { "Loadout" }));

        LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML", preloadEnvironment: true);
        yield return WaitForActiveScene("Game_ML", timeoutSeconds: 75f);
        Assert.That(SceneManager.GetSceneByName("Loadout").isLoaded, Is.False, "Loadout should unload after Game_ML activates.");
        Assert.That(GetTrackedRemoteSceneNames(), Is.EqualTo(new[] { "Game_ML" }));

        LoadingScreen.LoadScene("PostGame");
        yield return WaitForActiveScene("PostGame");
        Assert.That(SceneManager.GetSceneByName("Game_ML").isLoaded, Is.False, "Game_ML should unload after PostGame activates.");
        Assert.That(GetTrackedRemoteSceneNames(), Is.EqualTo(new[] { "PostGame" }));

        LoadingScreen.LoadScene("Lobby");
        yield return WaitForActiveScene("Lobby");
        Assert.That(SceneManager.GetSceneByName("PostGame").isLoaded, Is.False, "PostGame should unload after Lobby activates.");
        Assert.That(GetTrackedRemoteSceneNames(), Is.EqualTo(new[] { "Lobby" }));
    }

    [UnityTest]
    public IEnumerator Addressables_Init_Failure_Shows_Retry_UI()
    {
        yield return LoadBootstrapFresh();
        RemoteContentVerification.SetFailureCount(RemoteContentVerification.FaultKind.AddressablesInitialization, 1);

        LoadingScreen.LoadScene("Login");
        yield return WaitForRetryUi("Addressables catalog failed to initialize.");

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Bootstrap"), "Bootstrap should stay active when the first remote scene fails.");
    }

    [UnityTest]
    public IEnumerator Lobby_Content_Manifest_Failure_Shows_Retry_UI()
    {
        yield return LoadBootstrapFresh();
        yield return WaitForActiveScene("Login");

        RemoteContentVerification.SetFailureCount(RemoteContentVerification.FaultKind.ManifestDownload, 1);
        LoadingScreen.LoadSceneWithCriticalContentPreload("Lobby");
        yield return WaitForRetryUi("Content manifest failed to download.");

        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Login"), "Login should stay active when lobby entry content cannot be prepared.");
        Assert.That(SceneManager.GetSceneByName("Lobby").isLoaded, Is.False, "Lobby should not load when the manifest gate fails.");
    }

    static IEnumerator LoadBootstrapFresh()
    {
        SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
        yield return null;
    }

    IEnumerator WaitForActiveScene(string sceneName, float timeoutSeconds = DefaultTimeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid()
                && scene.isLoaded
                && string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal)
                && !IsTransitionInProgress())
            {
                yield break;
            }

            yield return null;
        }

        Assert.Fail($"Timed out waiting for active scene '{sceneName}'. Active='{SceneManager.GetActiveScene().name}', tracked=[{string.Join(", ", GetTrackedRemoteSceneNames())}]");
    }

    IEnumerator WaitForRetryUi(string expectedLabel, float timeoutSeconds = DefaultTimeoutSeconds)
    {
        float deadline = Time.realtimeSinceStartup + timeoutSeconds;
        while (Time.realtimeSinceStartup < deadline)
        {
            var loadingScreen = LoadingScreen.Instance;
            if (loadingScreen != null
                && string.Equals(loadingScreen.loadingLabel?.text, expectedLabel, StringComparison.Ordinal)
                && IsRetryButtonVisible(loadingScreen))
            {
                yield break;
            }

            yield return null;
        }

        string actualLabel = LoadingScreen.Instance != null ? LoadingScreen.Instance.loadingLabel?.text : "<no loading screen>";
        Assert.Fail($"Timed out waiting for retry UI '{expectedLabel}'. Actual label='{actualLabel}'.");
    }

    static bool IsTransitionInProgress()
    {
        const BindingFlags Flags = BindingFlags.Static | BindingFlags.NonPublic;
        return (bool)(typeof(LoadingScreen).GetField("_transitionInProgress", Flags)?.GetValue(null) ?? false);
    }

    static bool IsRetryButtonVisible(LoadingScreen loadingScreen)
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var button = typeof(LoadingScreen).GetField("_retryButton", Flags)?.GetValue(loadingScreen) as Button;
        return button != null && button.gameObject.activeInHierarchy;
    }

    static string[] GetTrackedRemoteSceneNames()
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var loadingScreen = LoadingScreen.Instance;
        if (loadingScreen == null)
            return Array.Empty<string>();

        var field = typeof(LoadingScreen).GetField("_loadedSceneHandles", Flags);
        if (field?.GetValue(loadingScreen) is not IDictionary dictionary)
            return Array.Empty<string>();

        return dictionary.Keys
            .Cast<object>()
            .Select(key => key?.ToString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
