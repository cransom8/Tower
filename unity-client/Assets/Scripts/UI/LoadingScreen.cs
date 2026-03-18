using System;
using System.Collections;
using System.Collections.Generic;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Loading screen controller. Place on the LoadingScreen GameObject in Loading.unity.
/// Call LoadingScreen.LoadScene("Game_ML") from any script to transition scenes.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

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
        "Send enemy units during the build phase to add pressure to opponents' waves.",
        "Ballistas outrange all other towers - great for back-line sniping.",
    };

    const string LoadingSceneName = "Loading";

    static string _pendingScene;
    static bool _pendingLobbyEntryPreparation;
    static bool _pendingT1GameplayPreload;
    static bool _pendingEnvironmentPreload;
    static string[] _pendingPortraitKeys = Array.Empty<string>();

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
        _pendingScene = sceneName;
        _pendingLobbyEntryPreparation = false;
        _pendingT1GameplayPreload = false;
        _pendingEnvironmentPreload = false;
        _pendingPortraitKeys = Array.Empty<string>();
        DisableLoadedSceneAudioListeners();
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Additive);
    }

    public static void LoadSceneWithCriticalContentPreload(string sceneName)
    {
        _pendingScene = sceneName;
        _pendingLobbyEntryPreparation = true;
        _pendingT1GameplayPreload = false;
        _pendingEnvironmentPreload = false;
        _pendingPortraitKeys = Array.Empty<string>();
        DisableLoadedSceneAudioListeners();
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Additive);
    }

    public static void LoadSceneWithRemoteContentGate(string sceneName, bool preloadT1Gameplay = false, IEnumerable<string> portraitKeys = null, bool preloadEnvironment = false)
    {
        _pendingScene = sceneName;
        _pendingLobbyEntryPreparation = false;
        _pendingT1GameplayPreload = preloadT1Gameplay;
        _pendingEnvironmentPreload = preloadEnvironment;
        _pendingPortraitKeys = NormalizeKeys(portraitKeys);
        DisableLoadedSceneAudioListeners();
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Additive);
    }

    void Awake()
    {
        Instance = this;
        ConfigureProgressBar();
        SetProgressImmediate(0f);
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    void Start()
    {
        if (string.IsNullOrEmpty(_pendingScene))
            _pendingScene = "Lobby";

        if (!HasPendingRemotePreparation() && tips.Length > 0 && tipText)
            tipText.text = tips[UnityEngine.Random.Range(0, tips.Length)];

        HideRetryAction();
        StartCoroutine(LoadRoutine());
    }

    void Update()
    {
        TickProgress();
        TickTips();
        TickDots();
    }

    IEnumerator LoadRoutine()
    {
        yield return StartCoroutine(FadeCG(canvasGroup, 1f, fadeInDuration));

        if (HasPendingRemotePreparation())
            yield return StartCoroutine(PreparePendingRemoteContentWithRetry());

        float startTime = Time.realtimeSinceStartup;
        SetAudioListenersEnabledForNonLoadingScenes(false);

        AsyncOperation op = SceneManager.LoadSceneAsync(_pendingScene, LoadSceneMode.Additive);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            SetProgressTarget(Mathf.Lerp(0.72f, 0.9f, Mathf.Clamp01(op.progress / 0.9f)));
            if (loadingLabel)
                loadingLabel.text = "Loading scene";
            yield return null;
        }

        float elapsed = Time.realtimeSinceStartup - startTime;
        if (elapsed < minDisplayTime)
            yield return new WaitForSeconds(minDisplayTime - elapsed);

        if (loadingLabel)
            loadingLabel.text = "Finalizing scene";
        SetProgressTarget(0.94f);

        SetAudioListenersEnabledForScene(LoadingSceneName, false);
        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);

        if (loadingLabel)
            loadingLabel.text = "Starting game";
        yield return StartCoroutine(FillBar(1f, 0.18f));

        Scene newScene = SceneManager.GetSceneByName(_pendingScene);
        SceneManager.SetActiveScene(newScene);

        if (_pendingScene == "Game_ML")
        {
            yield return null;
            NetworkManager.Instance?.Emit("ml_game_scene_ready");
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.name != _pendingScene && s.name != LoadingSceneName)
                SceneManager.UnloadSceneAsync(s);
        }

        SetAudioListenersEnabledForScene(_pendingScene, true);

        yield return StartCoroutine(FadeCG(canvasGroup, 0f, fadeOutDuration));
        SceneManager.UnloadSceneAsync(LoadingSceneName);
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
                if (ShouldAllowLocalContentFallback(remoteContent))
                {
                    Debug.LogWarning($"[LoadingScreen] Continuing with local content fallback. {remoteContent.LastError}");
                    _pendingLobbyEntryPreparation = false;
                    if (loadingLabel)
                        loadingLabel.text = "Remote content unavailable, using local assets";
                    if (tipText)
                        tipText.text = "Addressables could not initialize, so this session will use bundled local assets.";
                    SetProgressTarget(0.72f);
                    yield return new WaitForSeconds(0.35f);
                    yield break;
                }

                ShowBlockingFailure(
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

            yield return remoteContent.PreloadCriticalContentForSession((progress, status) =>
            {
                SetProgressTarget(Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)));
                if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                    loadingLabel.text = status;
            }, requester: $"LoadingScreen.SceneGate:{_pendingScene}");

            if (!remoteContent.HasCompletedCriticalPreload)
            {
                ShowBlockingFailure(
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
                ShowBlockingFailure(
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

            yield return remoteContent.EnsureEnvironmentReady(
                RemoteContentManager.GameMlEnvironmentAddress,
                (progress, status) =>
                {
                    SetProgressTarget(Mathf.Lerp(0f, 0.72f, Mathf.Clamp01(progress)));
                    if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                        loadingLabel.text = status;
                },
                requester: $"LoadingScreen.EnvironmentGate:{_pendingScene}");

            if (!remoteContent.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress))
            {
                ShowBlockingFailure(
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

    static bool ShouldAllowLocalContentFallback(RemoteContentManager remoteContent)
    {
        if (remoteContent == null)
            return false;

#if UNITY_WEBGL && !UNITY_EDITOR
        return false;
#endif

        if (remoteContent.LastFailureStage == RemoteContentManager.CriticalPreloadFailureStage.AddressablesInitialization)
            return true;

        string error = remoteContent.LastError ?? string.Empty;
        return error.IndexOf("RuntimeData is null", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("Unable to load runtime data", StringComparison.OrdinalIgnoreCase) >= 0
            || error.IndexOf("StreamingAssets/aa/settings.json", StringComparison.OrdinalIgnoreCase) >= 0;
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

    static bool HasPendingRemotePreparation()
    {
        return _pendingLobbyEntryPreparation
            || _pendingT1GameplayPreload
            || _pendingEnvironmentPreload
            || (_pendingPortraitKeys?.Length ?? 0) > 0;
    }

    static void SetAudioListenersEnabledForNonLoadingScenes(bool enabled)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.name == LoadingSceneName)
                continue;

            SetAudioListenersEnabledForScene(scene.name, enabled);
        }
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
            {
                listener.enabled = enabled;
            }
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

    void ShowBlockingFailure(string title, string detail)
    {
        if (loadingLabel)
            loadingLabel.text = title;
        if (tipText)
            tipText.text = detail;
        if (progressBar)
            progressBar.fillAmount = 0f;
        _displayedProgress = 0f;
        _targetProgress = 0f;
        ShowRetryAction("Retry");
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
    }

    void TickTips()
    {
        if (_suppressTipRotation) return;
        if (tips.Length == 0 || !tipText) return;
        _tipTimer += Time.deltaTime;
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
        if (_suppressDotAnimation) return;
        if (!loadingLabel) return;
        _dotTimer += Time.deltaTime;
        if (_dotTimer >= 0.4f)
        {
            _dotTimer = 0f;
            _dotCount = (_dotCount + 1) % 4;
            loadingLabel.text = "Loading" + new string('.', _dotCount);
        }
    }

    void TickProgress()
    {
        if (!progressBar) return;

        _displayedProgress = Mathf.MoveTowards(
            _displayedProgress,
            _targetProgress,
            progressLerpSpeed * Time.deltaTime);
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
        if (!progressBar) return;

        progressBar.type = Image.Type.Filled;
        progressBar.fillMethod = Image.FillMethod.Horizontal;
        progressBar.fillOrigin = (int)Image.OriginHorizontal.Left;
        progressBar.fillClockwise = true;
    }

    static IEnumerator FadeCG(CanvasGroup cg, float target, float duration)
    {
        if (!cg) yield break;
        float start = cg.alpha;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            cg.alpha = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        cg.alpha = target;
    }

    static IEnumerator FadeTMP(TextMeshProUGUI tmp, float target, float duration)
    {
        if (!tmp) yield break;
        float start = tmp.alpha;
        for (float t = 0; t < duration; t += Time.deltaTime)
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
        for (float t = 0; t < duration; t += Time.deltaTime)
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
}
