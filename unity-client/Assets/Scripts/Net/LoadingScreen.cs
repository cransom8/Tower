using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CastleDefender.Net
{
/// <summary>
/// Persistent bootstrap-owned loading overlay and scene transition service.
/// Call LoadingScreen.LoadScene("Game_ML") from any script to transition scenes.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    const string BootstrapSceneName = "Bootstrap";

    public static LoadingScreen Instance { get; private set; }
    public static bool IsTransitionInProgress => _transitionInProgress;

    [Header("UI References")]
    public Image progressBar;
    public TextMeshProUGUI tipText;
    public TextMeshProUGUI loadingLabel;
    public CanvasGroup canvasGroup;

    [Header("Config")]
    public float minDisplayTime = 0.8f;
    public float tipRotateTime = 2.2f;
    public float fadeInDuration = 0.2f;
    public float fadeOutDuration = 0.35f;
    public float progressLerpSpeed = 0.45f;

    [Header("Tips")]
    [TextArea(2, 4)]
    public string[] tips =
    {
        "Warlocks reduce tower damage by 25% for 3 seconds.",
        "Golems deal +25% damage to gates.",
        "Ironclaids take 30% less pierce damage.",
        "Runners take 25% less splash damage.",
        "Upgrade Barracks to boost your income on every unit spawn.",
        "Cannons have a 1.5-tile splash radius - great against clustered units.",
        "Place defenders on your lane during the build phase - they respawn after each wave.",
        "Barracks are the only source of outbound units, so build and stock them to keep pressure on the battlefield.",
        "Ballistas outrange all other towers - great for back-line sniping.",
    };

    static string _pendingScene;
    static bool _pendingLobbyEntryPreparation;
    static bool _pendingT1GameplayPreload;
    static bool _pendingEnvironmentPreload;
    static string[] _pendingPortraitKeys = Array.Empty<string>();
    static bool _transitionInProgress;

    readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> _loadedSceneHandles =
        new(StringComparer.OrdinalIgnoreCase);

    Coroutine _activeTransition;
    float _tipTimer;
    int _tipIndex;
    float _dotTimer;
    int _dotCount;
    bool _suppressTipRotation;
    bool _suppressDotAnimation;
    float _displayedProgress;
    float _targetProgress;
    bool _retryRequested;
    Button _retryButton;
    TextMeshProUGUI _retryLabel;

    public static void LoadScene(string sceneName)
    {
        var instance = EnsureInstance();
        if (instance == null)
            return;

        instance.BeginTransition(sceneName, lobbyEntryPreparation: false, preloadT1Gameplay: false, portraitKeys: null, preloadEnvironment: false);
    }

    public static void LoadSceneWithCriticalContentPreload(string sceneName)
    {
        var instance = EnsureInstance();
        if (instance == null)
            return;

        instance.BeginTransition(sceneName, lobbyEntryPreparation: true, preloadT1Gameplay: false, portraitKeys: null, preloadEnvironment: false);
    }

    public static void LoadSceneWithRemoteContentGate(
        string sceneName,
        bool preloadT1Gameplay = false,
        IEnumerable<string> portraitKeys = null,
        bool preloadEnvironment = false)
    {
        var instance = EnsureInstance();
        if (instance == null)
            return;

        instance.BeginTransition(
            sceneName,
            lobbyEntryPreparation: false,
            preloadT1Gameplay: preloadT1Gameplay,
            portraitKeys: portraitKeys,
            preloadEnvironment: preloadEnvironment);
    }

    static LoadingScreen EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        Debug.Log("[LoadingScreen] Creating runtime singleton instance.");
        var go = new GameObject(nameof(LoadingScreen));
        return go.AddComponent<LoadingScreen>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[LoadingScreen] Awake completed; runtime overlay will be built if needed.");

        BuildRuntimeOverlayIfNeeded();
        ConfigureProgressBar();
        SetProgressImmediate(0f);
        SetOverlayImmediate(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        _transitionInProgress = false;
        _activeTransition = null;
    }

    void Update()
    {
        TickProgress();
        TickTips();
        TickDots();
    }

    void BeginTransition(
        string sceneName,
        bool lobbyEntryPreparation,
        bool preloadT1Gameplay,
        IEnumerable<string> portraitKeys,
        bool preloadEnvironment)
    {
        if (_transitionInProgress)
        {
            Debug.LogWarning($"[LoadingScreen] Ignoring duplicate transition request to '{sceneName}' while another transition is active.");
            return;
        }

        _pendingScene = sceneName;
        _pendingLobbyEntryPreparation = lobbyEntryPreparation;
        _pendingT1GameplayPreload = preloadT1Gameplay;
        _pendingEnvironmentPreload = preloadEnvironment;
        _pendingPortraitKeys = NormalizeKeys(portraitKeys);
        _retryRequested = false;
        _transitionInProgress = true;
        _suppressTipRotation = false;
        _suppressDotAnimation = false;
        _tipTimer = 0f;
        _dotTimer = 0f;
        _dotCount = 0;

        Debug.Log(
            $"[LoadingScreen] Begin transition to '{sceneName}' " +
            $"(lobbyGate={lobbyEntryPreparation}, preloadT1={preloadT1Gameplay}, preloadEnvironment={preloadEnvironment}, portraits={_pendingPortraitKeys.Length}).");

        if (!HasPendingRemotePreparation() && tips.Length > 0 && tipText)
        {
            _tipIndex = UnityEngine.Random.Range(0, tips.Length);
            tipText.text = tips[_tipIndex];
        }

        HideRetryAction();
        DisableLoadedSceneAudioListeners();

        if (_activeTransition != null)
            StopCoroutine(_activeTransition);

        _activeTransition = StartCoroutine(LoadRoutine());
    }

    void RestartPendingTransition()
    {
        if (string.IsNullOrWhiteSpace(_pendingScene))
            return;

        HideRetryAction();
        _retryRequested = false;
        _transitionInProgress = true;
        if (_activeTransition != null)
            StopCoroutine(_activeTransition);
        _activeTransition = StartCoroutine(LoadRoutine());
    }

    IEnumerator LoadRoutine()
    {
        yield return StartCoroutine(FadeCG(canvasGroup, 1f, fadeInDuration));

        var remoteContent = RemoteContentManager.EnsureInstance();
        if (loadingLabel)
            loadingLabel.text = "Initializing remote content system";

        yield return remoteContent.EnsureAddressablesReady((progress, status) =>
        {
            SetProgressTarget(Mathf.Lerp(0.02f, 0.14f, Mathf.Clamp01(progress)));
            if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                loadingLabel.text = status;
        }, requester: $"LoadingScreen.BootstrapInit:{_pendingScene}");

        if (!remoteContent.AreAddressablesInitialized)
        {
            FailTransition(
                "Addressables catalog failed to initialize.",
                string.IsNullOrWhiteSpace(remoteContent.LastError)
                    ? "The game cannot load remote scenes until the Addressables catalog is available."
                    : remoteContent.LastError);
            yield break;
        }

        if (HasPendingRemotePreparation())
        {
            yield return StartCoroutine(PreparePendingRemoteContentWithRetry());
            if (!_transitionInProgress)
                yield break;
        }

        float startTime = Time.realtimeSinceStartup;
        if (loadingLabel)
            loadingLabel.text = "Loading scene";
        SetProgressTarget(0.68f);

        if (RemoteContentVerification.ConsumeFailure(
                RemoteContentVerification.FaultKind.RemoteSceneCatalogLookup,
                $"LoadingScreen.ValidateSceneCatalog:{_pendingScene}",
                out string forcedSceneCatalogFailure))
        {
            FailTransition("Remote scene catalog lookup failed.", forcedSceneCatalogFailure);
            yield break;
        }

        AsyncOperationHandle<IList<IResourceLocation>> sceneLocationHandle;
        try
        {
            sceneLocationHandle = Addressables.LoadResourceLocationsAsync(_pendingScene, typeof(SceneInstance));
        }
        catch (Exception ex)
        {
            FailTransition(
                "Remote scene catalog lookup failed.",
                $"Addressables threw while resolving scene '{_pendingScene}' in the active catalog: {ex.Message}");
            yield break;
        }

        while (sceneLocationHandle.IsValid() && !sceneLocationHandle.IsDone)
            yield return null;

        if (!sceneLocationHandle.IsValid())
        {
            FailTransition(
                "Remote scene catalog lookup failed.",
                $"Scene catalog lookup for '{_pendingScene}' became invalid before completion.");
            yield break;
        }

        bool sceneCatalogResolved = sceneLocationHandle.Status == AsyncOperationStatus.Succeeded
            && sceneLocationHandle.Result != null
            && sceneLocationHandle.Result.Count > 0;
        if (sceneLocationHandle.IsValid())
            Addressables.Release(sceneLocationHandle);

        if (!sceneCatalogResolved)
        {
            FailTransition(
                "Remote scene catalog lookup failed.",
                BuildMissingSceneCatalogMessage(_pendingScene));
            yield break;
        }

        if (RemoteContentVerification.ConsumeFailure(
                RemoteContentVerification.FaultKind.RemoteSceneBundleDownload,
                $"LoadingScreen.LoadSceneAsync:{_pendingScene}",
                out string forcedSceneBundleFailure))
        {
            FailTransition("Remote scene bundle download failed.", forcedSceneBundleFailure);
            yield break;
        }

        AsyncOperationHandle<SceneInstance> loadHandle;
        try
        {
            loadHandle = Addressables.LoadSceneAsync(_pendingScene, LoadSceneMode.Additive, activateOnLoad: false);
        }
        catch (Exception ex)
        {
            FailTransition("Remote scene load failed.", $"Addressables threw before loading scene '{_pendingScene}': {ex.Message}");
            yield break;
        }

        while (loadHandle.IsValid() && !loadHandle.IsDone)
        {
            SetProgressTarget(Mathf.Lerp(0.72f, 0.92f, Mathf.Clamp01(loadHandle.PercentComplete)));
            if (loadingLabel)
                loadingLabel.text = "Loading scene";
            yield return null;
        }

        if (!loadHandle.IsValid())
        {
            FailTransition("Remote scene load failed.", $"Scene handle for '{_pendingScene}' became invalid before the load completed.");
            yield break;
        }

        if (loadHandle.Status != AsyncOperationStatus.Succeeded)
        {
            if (loadHandle.IsValid())
                Addressables.Release(loadHandle);

            FailTransition(
                BuildRemoteSceneLoadFailureTitle(loadHandle),
                BuildHandleFailureMessage($"Scene '{_pendingScene}' could not be loaded from Addressables.", loadHandle));
            yield break;
        }

        float elapsed = Time.realtimeSinceStartup - startTime;
        if (elapsed < minDisplayTime)
            yield return new WaitForSecondsRealtime(minDisplayTime - elapsed);

        if (loadingLabel)
            loadingLabel.text = "Finalizing scene";
        SetProgressTarget(0.96f);

        AsyncOperation activation = loadHandle.Result.ActivateAsync();
        while (activation != null && !activation.isDone)
            yield return null;

        Scene newScene = loadHandle.Result.Scene;
        if (!newScene.IsValid())
            newScene = SceneManager.GetSceneByName(_pendingScene);

        if (!newScene.IsValid() || !newScene.isLoaded)
        {
            if (loadHandle.IsValid())
                Addressables.Release(loadHandle);

            FailTransition(
                "Remote scene activation failed.",
                $"Scene '{_pendingScene}' finished downloading, but Unity did not report it as a loaded scene.");
            yield break;
        }

        RegisterLoadedSceneHandle(_pendingScene, loadHandle);
        SceneManager.SetActiveScene(newScene);
        Debug.Log($"[LoadingScreen] Activated remote scene '{_pendingScene}'. Tracked handles: {BuildTrackedSceneSummary()}");

        if (_pendingScene == "Game_ML")
        {
            yield return null;
            NetworkManager.Instance?.RequestGameplayReady();
            NetworkManager.Instance?.Emit("ml_content_progress", new { percent = 1.0f, state = "Ready" });
        }

        yield return StartCoroutine(UnloadPreviousScenesExcept(_pendingScene));
        SetAudioListenersEnabledForScene(_pendingScene, true);

        if (loadingLabel)
            loadingLabel.text = "Starting game";
        yield return StartCoroutine(FillBar(1f, 0.18f));
        yield return StartCoroutine(FadeCG(canvasGroup, 0f, fadeOutDuration));

        _activeTransition = null;
        _transitionInProgress = false;
        SetOverlayImmediate(false);
    }

    void RegisterLoadedSceneHandle(string sceneName, AsyncOperationHandle<SceneInstance> handle)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || !handle.IsValid())
            return;

        _loadedSceneHandles[sceneName] = handle;
    }

    IEnumerator UnloadPreviousScenesExcept(string sceneName)
    {
        var sceneNames = new List<string>(_loadedSceneHandles.Keys);
        for (int i = 0; i < sceneNames.Count; i++)
        {
            string loadedSceneName = sceneNames[i];
            if (string.Equals(loadedSceneName, sceneName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!_loadedSceneHandles.TryGetValue(loadedSceneName, out AsyncOperationHandle<SceneInstance> handle))
                continue;

            if (!handle.IsValid())
            {
                _loadedSceneHandles.Remove(loadedSceneName);
                continue;
            }

            var unloadHandle = Addressables.UnloadSceneAsync(handle, false);
            while (unloadHandle.IsValid() && !unloadHandle.IsDone)
                yield return null;

            string unloadStatus = unloadHandle.IsValid() ? unloadHandle.Status.ToString() : "Invalid";
            Debug.Log(
                $"[LoadingScreen] Unloaded previous remote scene '{loadedSceneName}' via Addressables " +
                $"(status={unloadStatus}).");
            if (unloadHandle.IsValid())
                Addressables.Release(unloadHandle);
            _loadedSceneHandles.Remove(loadedSceneName);
        }

        for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded
                || string.Equals(scene.name, sceneName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(scene.name, BootstrapSceneName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Debug.Log($"[LoadingScreen] Unloading non-bootstrap fallback scene '{scene.name}' via SceneManager.");
            yield return SceneManager.UnloadSceneAsync(scene);
        }
    }

    IEnumerator PreparePendingRemoteContentWithRetry()
    {
        _suppressTipRotation = true;
        _suppressDotAnimation = true;

        while (HasPendingRemotePreparation())
        {
            _retryRequested = false;
            HideRetryAction();
            SetProgressImmediate(0f);

            yield return StartCoroutine(PreparePendingRemoteContent());
            if (!HasPendingRemotePreparation())
                yield break;

            yield return new WaitUntil(() => _retryRequested);
        }
    }

    IEnumerator PreparePendingRemoteContent()
    {
        var remoteContent = RemoteContentManager.EnsureInstance();

        if (_pendingLobbyEntryPreparation)
        {
            if (loadingLabel)
                loadingLabel.text = "Preparing lobby content";

            yield return remoteContent.PrepareLobbyEntryContentForSession((progress, status) =>
            {
                SetProgressTarget(Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)));
                if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                    loadingLabel.text = status;
            }, requester: "LoadingScreen.T0LobbyGate");

            if (!remoteContent.HasCompletedLobbyEntryPreparation)
            {
                FailTransition(
                    BuildFailureTitle(remoteContent.LastFailureStage, "Required lobby content could not be prepared."),
                    string.IsNullOrWhiteSpace(remoteContent.LastError)
                        ? "The game cannot safely enter the lobby until the required manifest and catalog are available."
                        : remoteContent.LastError);
                yield break;
            }

            _pendingLobbyEntryPreparation = false;
        }

        if (_pendingT1GameplayPreload)
        {
            if (tipText)
                tipText.text = remoteContent.BuildCriticalContentRequirementMessage();
            if (loadingLabel)
                loadingLabel.text = "Preparing first-match gameplay content";

            yield return remoteContent.PreloadWaveContentForSession((progress, status) =>
            {
                SetProgressTarget(Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)));
                if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                    loadingLabel.text = status;
                NetworkManager.Instance?.Emit("ml_content_progress",
                    new { percent = Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)), state = status ?? "Downloading match assets" });
            }, requester: $"LoadingScreen.SceneGate:{_pendingScene}");

            if (!remoteContent.HasCompletedWavePreload)
            {
                FailTransition(
                    BuildFailureTitle(remoteContent.LastFailureStage, "Required gameplay content could not be prepared."),
                    string.IsNullOrWhiteSpace(remoteContent.LastError)
                        ? "The game cannot safely continue until the required gameplay bundles are downloaded."
                        : remoteContent.LastError);
                yield break;
            }

            _pendingT1GameplayPreload = false;
        }

        if (_pendingPortraitKeys.Length > 0)
        {
            if (tipText)
                tipText.text = "Portraits are loaded here so the loadout UI never has to trigger first-time downloads itself.";
            if (loadingLabel)
                loadingLabel.text = "Preparing loadout portraits";

            yield return remoteContent.EnsurePortraitsReady(_pendingPortraitKeys, (progress, status) =>
            {
                SetProgressTarget(Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)));
                if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                    loadingLabel.text = status;
            }, requester: $"LoadingScreen.PortraitGate:{_pendingScene}");

            if (!remoteContent.ArePortraitsReady(_pendingPortraitKeys))
            {
                FailTransition(
                    BuildFailureTitle(remoteContent.LastFailureStage, "Required portraits could not be prepared."),
                    string.IsNullOrWhiteSpace(remoteContent.LastError)
                        ? "The game cannot open the loadout screen until the required portraits are ready."
                        : remoteContent.LastError);
                yield break;
            }

            _pendingPortraitKeys = Array.Empty<string>();
        }

        if (_pendingEnvironmentPreload)
        {
            if (tipText)
                tipText.text = "The match environment is warmed remotely so the scene shell can stay lean while gameplay-critical objects remain local.";
            if (loadingLabel)
                loadingLabel.text = "Preparing match environment";

            string expectedEnvironmentContentHash = NetworkManager.Instance?.LastMLMatchConfig?.battlefieldLayout?.contentHash;

            yield return remoteContent.EnsureEnvironmentReady(
                RemoteContentManager.GameMlEnvironmentAddress,
                expectedEnvironmentContentHash,
                (progress, status) =>
                {
                    SetProgressTarget(Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)));
                    if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                        loadingLabel.text = status;
                    NetworkManager.Instance?.Emit("ml_content_progress",
                        new { percent = Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)), state = status ?? "Preparing environment" });
                },
                requester: $"LoadingScreen.EnvironmentGate:{_pendingScene}");

            if (!remoteContent.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress))
            {
                FailTransition(
                    BuildFailureTitle(remoteContent.LastFailureStage, "Required match environment could not be prepared."),
                    string.IsNullOrWhiteSpace(remoteContent.LastError)
                        ? "The match cannot safely start until the required remote environment is ready."
                        : remoteContent.LastError);
                yield break;
            }

            _pendingEnvironmentPreload = false;
        }

        if (loadingLabel)
            loadingLabel.text = "Required content ready";
    }

    static string BuildFailureTitle(RemoteContentManager.CriticalPreloadFailureStage stage, string fallback)
    {
        return stage switch
        {
            RemoteContentManager.CriticalPreloadFailureStage.ManifestDownload => "Content manifest failed to download.",
            RemoteContentManager.CriticalPreloadFailureStage.ManifestParse => "Content manifest could not be parsed.",
            RemoteContentManager.CriticalPreloadFailureStage.ManifestValidation => "Content manifest validation failed.",
            RemoteContentManager.CriticalPreloadFailureStage.AddressablesInitialization => "Addressables catalog failed to initialize.",
            RemoteContentManager.CriticalPreloadFailureStage.DownloadSizing => "Remote bundle sizing failed.",
            RemoteContentManager.CriticalPreloadFailureStage.ContentDownload => "Remote bundle download failed.",
            RemoteContentManager.CriticalPreloadFailureStage.AssetLoad => "Remote asset load failed.",
            _ => fallback,
        };
    }

    static string BuildHandleFailureMessage<T>(string fallback, AsyncOperationHandle<T> handle)
    {
        string detail = handle.OperationException?.Message;
        if (string.IsNullOrWhiteSpace(detail))
            return fallback;

        return $"{fallback} {detail}";
    }

    static string BuildMissingSceneCatalogMessage(string sceneName)
    {
        return
            $"Scene '{sceneName}' is missing from the active Addressables catalog. " +
            "Rebuild Addressables, clear the catalog/cache, and confirm the active player is loading the latest remote catalog.";
    }

    static string BuildRemoteSceneLoadFailureTitle(AsyncOperationHandle<SceneInstance> handle)
    {
        string detail = handle.OperationException?.Message ?? string.Empty;
        if (detail.IndexOf("No Location found", StringComparison.OrdinalIgnoreCase) >= 0
            || detail.IndexOf("InvalidKeyException", StringComparison.OrdinalIgnoreCase) >= 0
            || detail.IndexOf("catalog", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Remote scene catalog lookup failed.";
        }

        if (detail.IndexOf("download", StringComparison.OrdinalIgnoreCase) >= 0
            || detail.IndexOf("bundle", StringComparison.OrdinalIgnoreCase) >= 0
            || detail.IndexOf("UnityWebRequest", StringComparison.OrdinalIgnoreCase) >= 0
            || detail.IndexOf("request", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Remote scene bundle download failed.";
        }

        return "Remote scene load failed.";
    }

    static bool HasPendingRemotePreparation()
    {
        return _pendingLobbyEntryPreparation
            || _pendingT1GameplayPreload
            || _pendingEnvironmentPreload
            || (_pendingPortraitKeys?.Length ?? 0) > 0;
    }

    static void SetAudioListenersEnabledForScene(string sceneName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
            return;

        Scene scene = SceneManager.GetSceneByName(sceneName);
        if (!scene.IsValid() || !scene.isLoaded)
            return;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = enabled;
        }
    }

    static void DisableLoadedSceneAudioListeners()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded)
                continue;

            SetAudioListenersEnabledForScene(scene.name, false);
        }
    }

    void FailTransition(string title, string detail)
    {
        Debug.LogError($"[LoadingScreen] Transition failed. title='{title}' detail='{detail}'");
        _suppressTipRotation = true;
        _suppressDotAnimation = true;
        if (loadingLabel)
            loadingLabel.text = title;
        if (tipText)
            tipText.text = detail;

        SetProgressImmediate(0f);
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
            SetAudioListenersEnabledForScene(activeScene.name, true);
        _activeTransition = null;
        _transitionInProgress = false;
        ShowRetryAction("Retry");
    }

    void BuildRuntimeOverlayIfNeeded()
    {
        bool hasWiredUi = progressBar != null && tipText != null && loadingLabel != null && canvasGroup != null;
        if (hasWiredUi)
            return;

        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4000;

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        var backdrop = CreatePanel(
            "Backdrop",
            transform,
            new Color(0.05f, 0.06f, 0.09f, 0.94f),
            Vector2.zero,
            Vector2.one);

        loadingLabel = CreateText(
            "LoadingLabel",
            backdrop.transform,
            36f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            "Loading");
        Stretch(loadingLabel.rectTransform, new Vector2(0.18f, 0.56f), new Vector2(0.82f, 0.70f));

        tipText = CreateText(
            "TipText",
            backdrop.transform,
            24f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            "Preparing game...");
        tipText.enableWordWrapping = true;
        Stretch(tipText.rectTransform, new Vector2(0.16f, 0.30f), new Vector2(0.84f, 0.52f));

        var progressBg = CreatePanel(
            "ProgressBackground",
            backdrop.transform,
            new Color(1f, 1f, 1f, 0.12f),
            new Vector2(0.23f, 0.21f),
            new Vector2(0.77f, 0.25f));

        var fillGo = new GameObject("ProgressFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(progressBg.transform, false);
        progressBar = fillGo.GetComponent<Image>();
        progressBar.color = new Color(0.28f, 0.76f, 0.48f, 1f);
        Stretch(progressBar.rectTransform, Vector2.zero, Vector2.one);
    }

    static GameObject CreatePanel(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        go.GetComponent<Image>().color = color;
        return go;
    }

    static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        float fontSize,
        FontStyles style,
        TextAlignmentOptions alignment,
        string value)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.color = Color.white;
        text.text = value;
        return text;
    }

    static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    void EnsureRetryAction()
    {
        if (_retryButton != null)
            return;

        Transform parent = canvasGroup != null ? canvasGroup.transform : transform;
        var buttonGo = new GameObject("Btn_RetryRemoteContent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGo.transform.SetParent(parent, false);

        var rect = buttonGo.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(220f, 52f);
        rect.anchoredPosition = new Vector2(0f, 84f);

        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.18f, 0.55f, 0.28f, 0.96f);

        _retryButton = buttonGo.GetComponent<Button>();
        _retryButton.targetGraphic = image;
        _retryButton.onClick.AddListener(OnRetryClicked);

        var labelGo = new GameObject("Lbl", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelGo.transform.SetParent(buttonGo.transform, false);

        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        _retryLabel = labelGo.GetComponent<TextMeshProUGUI>();
        _retryLabel.alignment = TextAlignmentOptions.Center;
        _retryLabel.fontSize = 20f;
        _retryLabel.color = Color.white;
        _retryLabel.text = "Retry";
    }

    void ShowRetryAction(string label)
    {
        EnsureRetryAction();
        if (_retryLabel != null)
            _retryLabel.text = string.IsNullOrWhiteSpace(label) ? "Retry" : label;
        if (_retryButton != null)
            _retryButton.gameObject.SetActive(true);
    }

    void HideRetryAction()
    {
        if (_retryButton != null)
            _retryButton.gameObject.SetActive(false);
    }

    void OnRetryClicked()
    {
        _retryRequested = true;
        HideRetryAction();
        if (loadingLabel)
            loadingLabel.text = "Retrying...";

        if (!_transitionInProgress)
            RestartPendingTransition();
    }

    void TickTips()
    {
        if (_suppressTipRotation)
            return;
        if (tips.Length == 0 || !tipText)
            return;

        _tipTimer += Time.unscaledDeltaTime;
        if (_tipTimer >= tipRotateTime)
        {
            _tipTimer = 0f;
            _tipIndex = (_tipIndex + 1) % tips.Length;
            StartCoroutine(CrossfadeTip(tips[_tipIndex]));
        }
    }

    IEnumerator CrossfadeTip(string newTip)
    {
        yield return StartCoroutine(FadeTMP(tipText, 0f, 0.15f));
        tipText.text = newTip;
        yield return StartCoroutine(FadeTMP(tipText, 1f, 0.15f));
    }

    void TickDots()
    {
        if (_suppressDotAnimation)
            return;
        if (!loadingLabel)
            return;

        _dotTimer += Time.unscaledDeltaTime;
        if (_dotTimer >= 0.4f)
        {
            _dotTimer = 0f;
            _dotCount = (_dotCount + 1) % 4;
            loadingLabel.text = "Loading" + new string('.', _dotCount);
        }
    }

    void TickProgress()
    {
        if (!progressBar)
            return;

        _displayedProgress = Mathf.MoveTowards(
            _displayedProgress,
            _targetProgress,
            progressLerpSpeed * Time.unscaledDeltaTime);
        progressBar.fillAmount = _displayedProgress;
    }

    void SetProgressImmediate(float value)
    {
        _displayedProgress = Mathf.Clamp01(value);
        _targetProgress = _displayedProgress;
        if (progressBar)
            progressBar.fillAmount = _displayedProgress;
    }

    void SetProgressTarget(float value)
    {
        _targetProgress = Mathf.Max(_targetProgress, Mathf.Clamp01(value));
    }

    void ConfigureProgressBar()
    {
        if (!progressBar)
            return;

        progressBar.type = Image.Type.Filled;
        progressBar.fillMethod = Image.FillMethod.Horizontal;
        progressBar.fillOrigin = (int)Image.OriginHorizontal.Left;
        progressBar.fillClockwise = true;
    }

    void SetOverlayImmediate(bool visible)
    {
        if (!canvasGroup)
            return;

        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = visible;
        canvasGroup.blocksRaycasts = visible;
    }

    static IEnumerator FadeCG(CanvasGroup cg, float target, float duration)
    {
        if (!cg)
            yield break;

        cg.interactable = target > 0f;
        cg.blocksRaycasts = target > 0f;

        float start = cg.alpha;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            cg.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }

        cg.alpha = target;
        cg.interactable = target > 0f;
        cg.blocksRaycasts = target > 0f;
    }

    static IEnumerator FadeTMP(TextMeshProUGUI tmp, float target, float duration)
    {
        if (!tmp)
            yield break;

        float start = tmp.alpha;
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            tmp.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }

        tmp.alpha = target;
    }

    IEnumerator FillBar(float target, float duration)
    {
        float start = _displayedProgress;
        float clampedTarget = Mathf.Clamp01(target);
        for (float t = 0; t < duration; t += Time.unscaledDeltaTime)
        {
            float value = Mathf.Lerp(start, clampedTarget, t / duration);
            SetProgressImmediate(value);
            yield return null;
        }

        SetProgressImmediate(clampedTarget);
    }

    static string[] NormalizeKeys(IEnumerable<string> keys)
    {
        if (keys == null)
            return Array.Empty<string>();

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in keys)
        {
            string trimmed = key?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                continue;

            normalized.Add(trimmed);
        }

        return normalized.ToArray();
    }

    string BuildTrackedSceneSummary()
    {
        if (_loadedSceneHandles.Count == 0)
            return "<none>";

        var names = new List<string>(_loadedSceneHandles.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", names);
    }
}
}
