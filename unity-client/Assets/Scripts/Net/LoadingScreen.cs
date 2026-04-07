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
    const string WinterBackdropResourcePath = "UI/Lobby/WinterForestBackdrop";
    const int OverlaySortingOrder = short.MaxValue - 1;
    const int ProgressStageCount = 4;
    static readonly string[] LoadingDotLabels = { "Loading", "Loading.", "Loading..", "Loading..." };
    static readonly string[] ProgressStageLabels = { "SYSTEM", "CONTENT", "SCENE", "READY" };
    static readonly Color ProgressStagePendingColor = new(0.08f, 0.06f, 0.05f, 0.86f);
    static readonly Color ProgressStageActiveColor = new(0.31f, 0.19f, 0.06f, 0.96f);
    static readonly Color ProgressStageCompleteColor = new(0.81f, 0.61f, 0.18f, 0.97f);
    static readonly Color ProgressStagePendingTextColor = new(0.72f, 0.70f, 0.66f, 0.94f);
    static readonly Color ProgressStageActiveTextColor = new(0.99f, 0.90f, 0.74f, 1f);
    static readonly Color ProgressStageCompleteTextColor = new(1f, 0.97f, 0.90f, 1f);
    static Sprite _winterBackdropSprite;
    static Sprite _progressFillSprite;

    enum LoadingStage
    {
        System = 0,
        Content = 1,
        Scene = 2,
        Ready = 3
    }

    public static LoadingScreen Instance { get; private set; }
    public static bool IsTransitionInProgress => _transitionInProgress;

    [Header("UI References")]
    public Image progressBar;
    public TextMeshProUGUI tipText;
    public TextMeshProUGUI loadingLabel;
    public TextMeshProUGUI loadingStatusText;
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
        "Build Barracks sites early so each lane can keep sending reinforcements.",
        "Attack pushes a lane forward, Defend holds the road, and Retreat pulls your forces back to safer ground.",
        "If a lane starts to crumble, spend there before the enemy reaches your fortress.",
        "Town Core upgrades open more branches and give you stronger battlefield options.",
        "Use loadout time to review race progression and enter the match with a plan.",
        "A lane with no Barracks pressure is easy for the enemy to reclaim.",
        "Retreating early is cheaper than rebuilding after a full lane wipe.",
        "Check every road between purchases so a quiet flank does not turn into a breach.",
        "Winning one lane helps, but protecting your fortress keeps the whole war effort alive.",
    };

    static string _pendingScene;
    static bool _pendingRequiredGameBootstrap;
    static bool _pendingLobbyEntryPreparation;
    static bool _pendingT1GameplayPreload;
    static bool _pendingEnvironmentPreload;
    static string[] _pendingPortraitKeys = Array.Empty<string>();
    static bool _transitionInProgress;

    readonly Dictionary<string, AsyncOperationHandle<SceneInstance>> _loadedSceneHandles =
        new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _validatedSceneCatalogEntries =
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
    TextMeshProUGUI _footerLoadingLabel;
    TextMeshProUGUI _progressBarPercentLabel;
    readonly Image[] _stagePanels = new Image[ProgressStageCount];
    readonly TextMeshProUGUI[] _stageTitleLabels = new TextMeshProUGUI[ProgressStageCount];
    readonly TextMeshProUGUI[] _stagePercentLabels = new TextMeshProUGUI[ProgressStageCount];
    int _activeStageIndex;

    public static void LoadScene(string sceneName)
    {
        var instance = EnsureInstance();
        if (instance == null)
            return;

        instance.BeginTransition(sceneName, requiredGameBootstrap: false, lobbyEntryPreparation: false, preloadT1Gameplay: false, portraitKeys: null, preloadEnvironment: false);
    }

    public static void LoadSceneWithCriticalContentPreload(string sceneName)
    {
        var instance = EnsureInstance();
        if (instance == null)
            return;

        instance.BeginTransition(sceneName, requiredGameBootstrap: true, lobbyEntryPreparation: false, preloadT1Gameplay: false, portraitKeys: null, preloadEnvironment: false);
    }

    public static void LoadSceneWithLobbyEntryPreparation(string sceneName)
    {
        var instance = EnsureInstance();
        if (instance == null)
            return;

        instance.BeginTransition(sceneName, requiredGameBootstrap: false, lobbyEntryPreparation: true, preloadT1Gameplay: false, portraitKeys: null, preloadEnvironment: false);
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
            requiredGameBootstrap: false,
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
        bool requiredGameBootstrap,
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
        _pendingRequiredGameBootstrap = requiredGameBootstrap;
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
        ClearLoadingStatusText();
        _activeStageIndex = 0;
        SetProgressImmediate(0f);

        Debug.Log(
            $"[LoadingScreen] Begin transition to '{sceneName}' " +
            $"(requiredBootstrap={requiredGameBootstrap}, lobbyGate={lobbyEntryPreparation}, preloadT1={preloadT1Gameplay}, preloadEnvironment={preloadEnvironment}, portraits={_pendingPortraitKeys.Length}).");

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
        ClearLoadingStatusText();
        _retryRequested = false;
        _transitionInProgress = true;
        _activeStageIndex = 0;
        SetProgressImmediate(0f);
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
        SetStageProgressTarget(LoadingStage.System, 0f);

        yield return remoteContent.EnsureAddressablesReady((progress, status) =>
        {
            SetStageProgressTarget(LoadingStage.System, progress);
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

        SetStageProgressTarget(LoadingStage.System, 1f);

        if (HasPendingRemotePreparation())
        {
            SetStageProgressTarget(LoadingStage.Content, 0f);
            yield return StartCoroutine(PreparePendingRemoteContentWithRetry());
            if (!_transitionInProgress)
                yield break;
        }
        else
        {
            SetStageProgressTarget(LoadingStage.Content, 1f);
        }

        float startTime = Time.realtimeSinceStartup;
        if (loadingLabel)
            loadingLabel.text = "Loading scene";
        SetStageProgressTarget(LoadingStage.Scene, 0f);

        if (RemoteContentVerification.ConsumeFailure(
                RemoteContentVerification.FaultKind.RemoteSceneCatalogLookup,
                $"LoadingScreen.ValidateSceneCatalog:{_pendingScene}",
                out string forcedSceneCatalogFailure))
        {
            FailTransition("Remote scene catalog lookup failed.", forcedSceneCatalogFailure);
            yield break;
        }

        bool sceneCatalogResolved = _validatedSceneCatalogEntries.Contains(_pendingScene);
        if (!sceneCatalogResolved)
        {
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

            sceneCatalogResolved = sceneLocationHandle.Status == AsyncOperationStatus.Succeeded
                && sceneLocationHandle.Result != null
                && sceneLocationHandle.Result.Count > 0;
            if (sceneLocationHandle.IsValid())
                Addressables.Release(sceneLocationHandle);

            if (sceneCatalogResolved)
                _validatedSceneCatalogEntries.Add(_pendingScene);
        }
        else
        {
            Debug.Log($"[LoadingScreen] Reusing cached scene catalog validation for '{_pendingScene}'.");
        }

        if (!sceneCatalogResolved)
        {
            FailTransition(
                "Remote scene catalog lookup failed.",
                BuildMissingSceneCatalogMessage(_pendingScene));
            yield break;
        }

        SetStageProgressTarget(LoadingStage.Scene, 0.20f);

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
            SetStageProgressTarget(LoadingStage.Scene, Mathf.Lerp(0.24f, 0.92f, Mathf.Clamp01(loadHandle.PercentComplete)));
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

        SetStageProgressTarget(LoadingStage.Scene, 1f);

        float elapsed = Time.realtimeSinceStartup - startTime;
        if (elapsed < minDisplayTime)
            yield return new WaitForSecondsRealtime(minDisplayTime - elapsed);

        if (loadingLabel)
            loadingLabel.text = "Finalizing scene";
        SetStageProgressTarget(LoadingStage.Ready, 0.12f);

        AsyncOperation activation = loadHandle.Result.ActivateAsync();
        while (activation != null && !activation.isDone)
            yield return null;

        SetStageProgressTarget(LoadingStage.Ready, 0.42f);

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
        SetStageProgressTarget(LoadingStage.Ready, 0.78f);
        SetAudioListenersEnabledForScene(_pendingScene, true);
        RefreshGlobalAudioManagerLoopPlayback(restartCurrentClip: true);

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
            SetStageProgressImmediate(LoadingStage.Content, 0f);

            yield return StartCoroutine(PreparePendingRemoteContent());
            if (!HasPendingRemotePreparation())
                yield break;

            yield return new WaitUntil(() => _retryRequested);
        }
    }

    IEnumerator PreparePendingRemoteContent()
    {
        var remoteContent = RemoteContentManager.EnsureInstance();
        int totalPreparationSteps = CountPendingRemotePreparationSteps();
        int completedPreparationSteps = 0;

        SetStageProgressTarget(LoadingStage.Content, 0f);

        if (_pendingRequiredGameBootstrap)
        {
            ApplyRequiredGameBootstrapMessaging(remoteContent);
            SetLoadingStatusText("Checking downloaded game content...");

            yield return remoteContent.PrepareRequiredGameContentForSession((progress, status) =>
            {
                ApplyRequiredGameBootstrapMessaging(remoteContent);
                SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, progress));
                SetLoadingStatusText(status);
            }, requester: "LoadingScreen.RequiredGameBootstrap");

            if (!remoteContent.HasCompletedRequiredGameBootstrap)
            {
                FailTransition(
                    BuildFailureTitle(remoteContent.LastFailureStage, "Required game content could not be prepared."),
                    string.IsNullOrWhiteSpace(remoteContent.LastError)
                        ? remoteContent.RequiredGameBootstrapNeedsDownload
                            ? "The game cannot safely continue until the one-time required content download finishes."
                            : "The game cannot safely continue until required game content is verified on this device."
                        : remoteContent.LastError);
                yield break;
            }

            _pendingRequiredGameBootstrap = false;
            completedPreparationSteps++;
            SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, 0f));
            SetLoadingStatusText("Opening lobby...");
        }

        if (_pendingLobbyEntryPreparation)
        {
            if (loadingLabel)
                loadingLabel.text = "Preparing lobby content";

            yield return remoteContent.PrepareLobbyEntryContentForSession((progress, status) =>
            {
                SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, progress));
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
            completedPreparationSteps++;
            SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, 0f));
        }

        if (_pendingT1GameplayPreload)
        {
            if (tipText)
                tipText.text = remoteContent.BuildCriticalContentRequirementMessage();
            if (loadingLabel)
                loadingLabel.text = "Preparing first-match gameplay content";

            yield return remoteContent.PreloadWaveContentForSession((progress, status) =>
            {
                float stageProgress = CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, progress);
                SetStageProgressTarget(LoadingStage.Content, stageProgress);
                if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                    loadingLabel.text = status;
                NetworkManager.Instance?.Emit("ml_content_progress",
                    new { percent = stageProgress, state = status ?? "Downloading match assets" });
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
            completedPreparationSteps++;
            SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, 0f));
        }

        if (_pendingPortraitKeys.Length > 0)
        {
            if (tipText)
                tipText.text = "Portraits are loaded here so the loadout UI never has to trigger first-time downloads itself.";
            if (loadingLabel)
                loadingLabel.text = "Preparing loadout portraits";

            yield return remoteContent.EnsurePortraitsReady(_pendingPortraitKeys, (progress, status) =>
            {
                SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, progress));
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
            completedPreparationSteps++;
            SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, 0f));
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
                    float stageProgress = CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, progress);
                    SetStageProgressTarget(LoadingStage.Content, stageProgress);
                    if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                        loadingLabel.text = status;
                    NetworkManager.Instance?.Emit("ml_content_progress",
                        new { percent = stageProgress, state = status ?? "Preparing environment" });
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
            completedPreparationSteps++;
            SetStageProgressTarget(LoadingStage.Content, CalculatePreparationStageProgress(completedPreparationSteps, totalPreparationSteps, 0f));
        }

        SetStageProgressTarget(LoadingStage.Content, 1f);
        if (loadingLabel)
            loadingLabel.text = "Required content ready";
    }

    void ApplyRequiredGameBootstrapMessaging(RemoteContentManager remoteContent)
    {
        bool needsDownload = remoteContent != null && remoteContent.RequiredGameBootstrapNeedsDownload;
        if (tipText)
        {
            tipText.text = needsDownload
                ? "This device is downloading and caching required game content one time. Future launches reuse it unless live content changes or the app data is cleared."
                : "Checking the game content already downloaded on this device so the session can start without another long in-game download.";
        }

        if (loadingLabel)
            loadingLabel.text = needsDownload ? "One-Time Game Download" : "Checking Downloaded Game Content";
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
        return _pendingRequiredGameBootstrap
            || _pendingLobbyEntryPreparation
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

    static void RefreshGlobalAudioManagerLoopPlayback(bool restartCurrentClip)
    {
        Type audioManagerType = FindType("AudioManager");
        if (audioManagerType == null)
            return;

        object instance = audioManagerType
            .GetProperty("I", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.GetValue(null);
        if (instance == null)
            return;

        audioManagerType
            .GetMethod("RefreshLoopPlaybackForCurrentScene", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.Invoke(instance, new object[] { restartCurrentClip });
    }

    static Type FindType(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        Type type = Type.GetType(fullName, false);
        if (type != null)
            return type;

        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            type = assemblies[i].GetType(fullName, false);
            if (type != null)
                return type;
        }

        return null;
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
        ClearLoadingStatusText();

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
        bool hasWiredUi = progressBar != null && tipText != null && loadingLabel != null && loadingStatusText != null && canvasGroup != null;
        if (hasWiredUi)
            return;

        var canvas = GetComponent<Canvas>();
        if (canvas == null)
            canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = OverlaySortingOrder;

        var scaler = GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        if (GetComponent<GraphicRaycaster>() == null)
            gameObject.AddComponent<GraphicRaycaster>();

        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();

        var backdrop = CreatePanel("Backdrop", transform, new Color(0.02f, 0.03f, 0.06f, 1f), Vector2.zero, Vector2.one);

        var scenic = CreatePanel("ScenicBackdrop", backdrop.transform, Color.white, Vector2.zero, Vector2.one);
        var scenicImage = scenic.GetComponent<Image>();
        scenicImage.color = new Color(1f, 1f, 1f, 0.98f);
        Sprite winterBackdrop = LoadWinterBackdropSprite();
        if (winterBackdrop != null)
        {
            scenicImage.sprite = winterBackdrop;
            scenicImage.type = Image.Type.Simple;
            scenicImage.preserveAspect = true;

            var fitter = scenic.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = winterBackdrop.rect.width / Mathf.Max(1f, winterBackdrop.rect.height);
        }

        CreateTintLayer(backdrop.transform, "BackdropWash", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(0.04f, 0.07f, 0.11f, 0.46f));
        CreateTintLayer(backdrop.transform, "TopShade", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(0f, 250f), new Color(0.01f, 0.02f, 0.05f, 0.72f));
        CreateTintLayer(backdrop.transform, "BottomShade", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(0f, 250f), new Color(0.02f, 0.03f, 0.05f, 0.72f));
        CreateTintLayer(backdrop.transform, "LeftShade", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(160f, 0f), new Color(0.01f, 0.02f, 0.04f, 0.46f));
        CreateTintLayer(backdrop.transform, "RightShade", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(160f, 0f), new Color(0.01f, 0.02f, 0.04f, 0.52f));

        var brandTitle = CreateText(
            "BrandTitle",
            backdrop.transform,
            74f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            "RANSOM FORGE");
        Stretch(brandTitle.rectTransform, new Vector2(0.16f, 0.84f), new Vector2(0.84f, 0.94f));
        ApplyBrandTitleTreatment(brandTitle);

        var brandTagline = CreateText(
            "BrandTagline",
            backdrop.transform,
            22f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            "FORGED FOR WAR");
        Stretch(brandTagline.rectTransform, new Vector2(0.28f, 0.79f), new Vector2(0.72f, 0.84f));
        ApplyBrandTaglineTreatment(brandTagline);

        loadingLabel = CreateText(
            "LoadingLabel",
            backdrop.transform,
            56f,
            FontStyles.Bold,
            TextAlignmentOptions.Center,
            "Loading");
        loadingLabel.textWrappingMode = TextWrappingModes.Normal;
        loadingLabel.overflowMode = TextOverflowModes.Overflow;
        Stretch(loadingLabel.rectTransform, new Vector2(0.14f, 0.48f), new Vector2(0.86f, 0.62f));
        ApplyPhaseTitleTreatment(loadingLabel);

        loadingStatusText = CreateText(
            "LoadingStatusText",
            backdrop.transform,
            23f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            "");
        loadingStatusText.textWrappingMode = TextWrappingModes.Normal;
        loadingStatusText.color = new Color(0.93f, 0.92f, 0.88f, 0.92f);
        loadingStatusText.overflowMode = TextOverflowModes.Overflow;
        Stretch(loadingStatusText.rectTransform, new Vector2(0.16f, 0.31f), new Vector2(0.84f, 0.41f));
        loadingStatusText.gameObject.SetActive(false);
        AddTextShadow(loadingStatusText, new Color(0f, 0f, 0f, 0.32f), new Vector2(0f, -3f));

        _footerLoadingLabel = null;

        var stageStripGo = new GameObject("ProgressStageStrip", typeof(RectTransform));
        stageStripGo.transform.SetParent(backdrop.transform, false);
        Stretch(stageStripGo.GetComponent<RectTransform>(), new Vector2(0.18f, 0.215f), new Vector2(0.82f, 0.285f));
        BuildProgressStageStrip(stageStripGo.transform);

        var progressBg = CreatePanel(
            "ProgressBackground",
            backdrop.transform,
            new Color(0.06f, 0.04f, 0.03f, 0.90f),
            new Vector2(0.23f, 0.15f),
            new Vector2(0.77f, 0.20f));
        var progressBgOutline = progressBg.AddComponent<Outline>();
        progressBgOutline.effectColor = new Color(0.62f, 0.41f, 0.17f, 0.42f);
        progressBgOutline.effectDistance = new Vector2(2f, -2f);

        var progressInner = CreatePanel(
            "ProgressInner",
            progressBg.transform,
            new Color(0.10f, 0.08f, 0.06f, 0.95f),
            Vector2.zero,
            Vector2.one);
        StretchWithOffsets(progressInner.GetComponent<RectTransform>(), new Vector2(10f, 9f), new Vector2(-10f, -9f));

        var fillGo = new GameObject("ProgressFill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        fillGo.transform.SetParent(progressInner.transform, false);
        progressBar = fillGo.GetComponent<Image>();
        progressBar.sprite = GetProgressFillSprite();
        progressBar.color = new Color(0.95f, 0.75f, 0.20f, 1f);
        progressBar.raycastTarget = false;
        Stretch(progressBar.rectTransform, Vector2.zero, Vector2.one);
        var progressShadow = progressBar.gameObject.AddComponent<Shadow>();
        progressShadow.effectColor = new Color(1f, 0.63f, 0.18f, 0.24f);
        progressShadow.effectDistance = new Vector2(0f, 0f);

        CreateProgressDivider(progressInner.transform, 0.25f);
        CreateProgressDivider(progressInner.transform, 0.50f);
        CreateProgressDivider(progressInner.transform, 0.75f);

        var progressPercentGo = new GameObject("ProgressPercentLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        progressPercentGo.transform.SetParent(progressInner.transform, false);
        _progressBarPercentLabel = progressPercentGo.GetComponent<TextMeshProUGUI>();
        _progressBarPercentLabel.fontSize = 24f;
        _progressBarPercentLabel.fontStyle = FontStyles.Bold;
        _progressBarPercentLabel.alignment = TextAlignmentOptions.Center;
        _progressBarPercentLabel.text = "0%";
        _progressBarPercentLabel.raycastTarget = false;
        Stretch(_progressBarPercentLabel.rectTransform, new Vector2(0.02f, 0.05f), new Vector2(0.98f, 0.95f));
        ApplyProgressBarPercentTreatment(_progressBarPercentLabel);

        var tipPlate = CreatePanel(
            "TipPlate",
            backdrop.transform,
            new Color(0.03f, 0.03f, 0.04f, 0.68f),
            new Vector2(0.18f, 0.05f),
            new Vector2(0.82f, 0.11f));
        var tipPlateOutline = tipPlate.AddComponent<Outline>();
        tipPlateOutline.effectColor = new Color(0.18f, 0.12f, 0.05f, 0.38f);
        tipPlateOutline.effectDistance = new Vector2(1f, -1f);

        tipText = CreateText(
            "TipText",
            tipPlate.transform,
            20f,
            FontStyles.Normal,
            TextAlignmentOptions.Center,
            "Preparing game...");
        tipText.textWrappingMode = TextWrappingModes.Normal;
        tipText.color = new Color(0.94f, 0.90f, 0.83f, 0.96f);
        StretchWithOffsets(tipText.rectTransform, new Vector2(18f, 8f), new Vector2(-18f, -8f));
        AddTextShadow(tipText, new Color(0f, 0f, 0f, 0.30f), new Vector2(0f, -3f));

        var versionLabel = CreateText(
            "VersionLabel",
            backdrop.transform,
            18f,
            FontStyles.Bold,
            TextAlignmentOptions.BottomLeft,
            RuntimeVersionDisplay.VersionLabel);
        Stretch(versionLabel.rectTransform, new Vector2(0.03f, 0.02f), new Vector2(0.32f, 0.07f));
        ApplyFooterMetaTreatment(versionLabel);
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
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
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
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static void StretchWithOffsets(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        rect.anchoredPosition = Vector2.zero;
    }

    static Sprite LoadWinterBackdropSprite()
    {
        if (_winterBackdropSprite != null)
            return _winterBackdropSprite;

        var texture = Resources.Load<Texture2D>(WinterBackdropResourcePath);
        if (texture == null)
        {
            Debug.LogWarning($"[LoadingScreen] Missing loading backdrop resource at Resources/{WinterBackdropResourcePath}.");
            return null;
        }

        texture.wrapMode = TextureWrapMode.Clamp;
        _winterBackdropSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        _winterBackdropSprite.name = "WinterForestBackdrop_LoadingRuntime";
        return _winterBackdropSprite;
    }

    static Sprite GetProgressFillSprite()
    {
        if (_progressFillSprite != null)
            return _progressFillSprite;

        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            name = "LoadingProgressFillTexture",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.white;

        texture.SetPixels(pixels);
        texture.Apply(false, true);

        _progressFillSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        _progressFillSprite.name = "LoadingProgressFillSprite";
        return _progressFillSprite;
    }

    static void CreateTintLayer(
        Transform parent,
        string name,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;

        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void BuildProgressStageStrip(Transform parent)
    {
        for (int i = 0; i < ProgressStageCount; i++)
        {
            var cell = CreatePanel($"StageCell_{i}", parent, ProgressStagePendingColor, Vector2.zero, Vector2.one);
            var rect = cell.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(i / (float)ProgressStageCount, 0f);
            rect.anchorMax = new Vector2((i + 1) / (float)ProgressStageCount, 1f);
            rect.offsetMin = new Vector2(i == 0 ? 0f : 5f, 0f);
            rect.offsetMax = new Vector2(i == ProgressStageCount - 1 ? 0f : -5f, 0f);

            var panel = cell.GetComponent<Image>();
            panel.raycastTarget = false;
            var outline = cell.AddComponent<Outline>();
            outline.effectColor = new Color(0.44f, 0.28f, 0.10f, 0.40f);
            outline.effectDistance = new Vector2(1f, -1f);

            var title = CreateText(
                "Title",
                cell.transform,
                13f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                ProgressStageLabels[i]);
            Stretch(title.rectTransform, new Vector2(0.08f, 0.52f), new Vector2(0.92f, 0.93f));
            ApplyStageTitleTreatment(title);

            var percent = CreateText(
                "Percent",
                cell.transform,
                20f,
                FontStyles.Bold,
                TextAlignmentOptions.Center,
                "0%");
            Stretch(percent.rectTransform, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.66f));
            ApplyStagePercentTreatment(percent);

            _stagePanels[i] = panel;
            _stageTitleLabels[i] = title;
            _stagePercentLabels[i] = percent;
        }
    }

    static void CreateProgressDivider(Transform parent, float anchorX)
    {
        CreateTintLayer(
            parent,
            $"ProgressDivider_{Mathf.RoundToInt(anchorX * 100f)}",
            new Vector2(anchorX, 0f),
            new Vector2(anchorX, 1f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(4f, 0f),
            new Color(0.18f, 0.10f, 0.04f, 0.92f));
    }

    static void ApplyBrandTitleTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.characterSpacing = 2.8f;
        text.enableVertexGradient = true;
        text.colorGradient = new VertexGradient(
            new Color(1.00f, 0.97f, 0.90f, 1f),
            new Color(0.96f, 0.90f, 0.78f, 1f),
            new Color(0.64f, 0.41f, 0.17f, 1f),
            new Color(0.34f, 0.19f, 0.08f, 1f));
        text.outlineColor = new Color(0.10f, 0.05f, 0.02f, 0.98f);
        text.outlineWidth = 0.24f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.40f), new Vector2(0f, -7f));
    }

    static void ApplyBrandTaglineTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.characterSpacing = 4.8f;
        text.color = new Color(0.88f, 0.90f, 0.95f, 0.96f);
        text.outlineColor = new Color(0.04f, 0.05f, 0.07f, 0.80f);
        text.outlineWidth = 0.14f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.32f), new Vector2(0f, -4f));
    }

    static void ApplyPhaseTitleTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.color = new Color(0.98f, 0.96f, 0.92f, 1f);
        text.outlineColor = new Color(0.03f, 0.03f, 0.04f, 0.96f);
        text.outlineWidth = 0.20f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.34f), new Vector2(0f, -5f));
    }

    static void ApplyFooterLoadingTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.characterSpacing = 1.6f;
        text.color = new Color(0.94f, 0.72f, 0.39f, 0.98f);
        text.outlineColor = new Color(0.12f, 0.08f, 0.04f, 0.92f);
        text.outlineWidth = 0.18f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.32f), new Vector2(0f, -4f));
    }

    static void ApplyFooterMetaTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.characterSpacing = 0.8f;
        text.color = new Color(0.93f, 0.90f, 0.83f, 0.92f);
        text.outlineColor = new Color(0.06f, 0.05f, 0.03f, 0.92f);
        text.outlineWidth = 0.16f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.28f), new Vector2(0f, -3f));
    }

    static void ApplyStageTitleTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.characterSpacing = 1.4f;
        text.color = ProgressStagePendingTextColor;
        text.outlineColor = new Color(0.06f, 0.04f, 0.02f, 0.94f);
        text.outlineWidth = 0.16f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.26f), new Vector2(0f, -2f));
    }

    static void ApplyStagePercentTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.color = ProgressStagePendingTextColor;
        text.outlineColor = new Color(0.06f, 0.04f, 0.02f, 0.94f);
        text.outlineWidth = 0.18f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.28f), new Vector2(0f, -2f));
    }

    static void ApplyProgressBarPercentTreatment(TextMeshProUGUI text)
    {
        if (text == null)
            return;

        text.characterSpacing = 1.2f;
        text.color = new Color(1f, 0.97f, 0.90f, 1f);
        text.outlineColor = new Color(0.08f, 0.05f, 0.02f, 0.96f);
        text.outlineWidth = 0.18f;
        AddTextShadow(text, new Color(0f, 0f, 0f, 0.32f), new Vector2(0f, -3f));
    }

    static void AddTextShadow(Graphic graphic, Color color, Vector2 distance)
    {
        if (graphic == null)
            return;

        var shadow = graphic.GetComponent<Shadow>();
        if (shadow == null)
            shadow = graphic.gameObject.AddComponent<Shadow>();

        shadow.effectColor = color;
        shadow.effectDistance = distance;
        shadow.useGraphicAlpha = true;
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
        rect.sizeDelta = new Vector2(240f, 56f);
        rect.anchoredPosition = new Vector2(0f, 168f);

        var image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.14f, 0.09f, 0.05f, 0.94f);
        var outline = buttonGo.AddComponent<Outline>();
        outline.effectColor = new Color(0.70f, 0.48f, 0.18f, 0.48f);
        outline.effectDistance = new Vector2(2f, -2f);

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
        _retryLabel.color = new Color(0.95f, 0.78f, 0.42f, 1f);
        _retryLabel.text = "Retry";
        _retryLabel.outlineColor = new Color(0.10f, 0.06f, 0.03f, 0.92f);
        _retryLabel.outlineWidth = 0.18f;
        AddTextShadow(_retryLabel, new Color(0f, 0f, 0f, 0.30f), new Vector2(0f, -3f));
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
        SetLoadingStatusText(
            _pendingRequiredGameBootstrap
                ? "Retrying required game content..."
                : _pendingLobbyEntryPreparation
                    ? "Retrying lobby content..."
                    : "Retrying...");

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
        if (_suppressDotAnimation || _footerLoadingLabel == null)
            return;

        _dotTimer += Time.unscaledDeltaTime;
        if (_dotTimer >= 0.4f)
        {
            _dotTimer = 0f;
            _dotCount = (_dotCount + 1) % 4;
            _footerLoadingLabel.text = LoadingDotLabels[_dotCount].ToUpperInvariant();
        }
    }

    void SetLoadingStatusText(string value)
    {
        if (!loadingStatusText)
            return;

        string normalized = value?.Trim() ?? "";
        loadingStatusText.text = normalized;
        loadingStatusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(normalized));
    }

    void ClearLoadingStatusText()
    {
        if (!loadingStatusText)
            return;

        loadingStatusText.text = "";
        loadingStatusText.gameObject.SetActive(false);
    }

    void TickProgress()
    {
        _displayedProgress = Mathf.MoveTowards(
            _displayedProgress,
            _targetProgress,
            progressLerpSpeed * Time.unscaledDeltaTime);
        RefreshProgressPresentation();
    }

    void SetProgressImmediate(float value)
    {
        _displayedProgress = Mathf.Clamp01(value);
        _targetProgress = _displayedProgress;
        RefreshProgressPresentation();
    }

    void SetProgressTarget(float value)
    {
        _targetProgress = Mathf.Max(_targetProgress, Mathf.Clamp01(value));
    }

    void SetStageProgressImmediate(LoadingStage stage, float value)
    {
        _activeStageIndex = Mathf.Clamp((int)stage, 0, ProgressStageCount - 1);
        SetProgressImmediate(MapStageProgressToOverall(stage, value));
    }

    void SetStageProgressTarget(LoadingStage stage, float value)
    {
        _activeStageIndex = Mathf.Clamp((int)stage, 0, ProgressStageCount - 1);
        SetProgressTarget(MapStageProgressToOverall(stage, value));
    }

    void ConfigureProgressBar()
    {
        if (!progressBar)
            return;

        if (progressBar.sprite == null)
            progressBar.sprite = GetProgressFillSprite();
        progressBar.type = Image.Type.Filled;
        progressBar.fillMethod = Image.FillMethod.Horizontal;
        progressBar.fillOrigin = (int)Image.OriginHorizontal.Left;
        progressBar.fillClockwise = true;
        progressBar.fillAmount = _displayedProgress;
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

    void RefreshProgressPresentation()
    {
        if (progressBar)
            progressBar.fillAmount = _displayedProgress;

        if (_progressBarPercentLabel)
            _progressBarPercentLabel.text = $"{Mathf.RoundToInt(Mathf.Clamp01(_displayedProgress) * 100f)}%";

        for (int i = 0; i < ProgressStageCount; i++)
        {
            float localProgress = Mathf.Clamp01((_displayedProgress * ProgressStageCount) - i);
            bool isComplete = localProgress >= 0.999f;
            bool isActive = !isComplete && _transitionInProgress && i == _activeStageIndex;

            if (_stagePanels[i] != null)
                _stagePanels[i].color = isComplete
                    ? ProgressStageCompleteColor
                    : isActive
                        ? ProgressStageActiveColor
                        : ProgressStagePendingColor;

            Color textColor = isComplete
                ? ProgressStageCompleteTextColor
                : isActive
                    ? ProgressStageActiveTextColor
                    : ProgressStagePendingTextColor;

            if (_stageTitleLabels[i] != null)
                _stageTitleLabels[i].color = textColor;

            if (_stagePercentLabels[i] != null)
            {
                _stagePercentLabels[i].color = textColor;
                _stagePercentLabels[i].text = $"{Mathf.RoundToInt(localProgress * 100f)}%";
            }
        }
    }

    static float MapStageProgressToOverall(LoadingStage stage, float value)
    {
        int stageIndex = Mathf.Clamp((int)stage, 0, ProgressStageCount - 1);
        return (stageIndex + Mathf.Clamp01(value)) / ProgressStageCount;
    }

    static int CountPendingRemotePreparationSteps()
    {
        int count = 0;
        if (_pendingRequiredGameBootstrap)
            count++;
        if (_pendingLobbyEntryPreparation)
            count++;
        if (_pendingT1GameplayPreload)
            count++;
        if (_pendingPortraitKeys.Length > 0)
            count++;
        if (_pendingEnvironmentPreload)
            count++;
        return count;
    }

    static float CalculatePreparationStageProgress(int completedSteps, int totalSteps, float localProgress)
    {
        if (totalSteps <= 0)
            return 1f;

        return Mathf.Clamp01((completedSteps + Mathf.Clamp01(localProgress)) / totalSteps);
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
