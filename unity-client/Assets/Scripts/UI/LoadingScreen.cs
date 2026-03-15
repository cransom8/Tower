using System;
using System.Collections;
using CastleDefender.Net;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Loading screen controller. Place on the LoadingScreen GameObject in Loading.unity.
/// Call LoadingScreen.LoadScene("Game_ML") from any script to transition scenes.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    public static LoadingScreen Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────
    [Header("UI References")]
    public Image          progressBar;   // fill image (fill method = Filled, Horizontal)
    public TextMeshProUGUI tipText;
    public TextMeshProUGUI loadingLabel; // "Loading..." dot animation
    public CanvasGroup    canvasGroup;   // root canvas group for fade in/out

    [Header("Config")]
    public float minDisplayTime  = 0.8f;
    public float tipRotateTime   = 2.2f;
    public float fadeInDuration  = 0.2f;
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
        "Cannons have a 1.5-tile splash radius — great against clustered units.",
        "Place defenders on your lane during the build phase — they respawn after each wave.",
        "Send enemy units during the build phase to add pressure to opponents' waves.",
        "Ballistas outrange all other towers — great for back-line sniping.",
    };

    // ── Internal ───────────────────────────────────────────────────────
    const string LoadingSceneName = "Loading";
    static string _pendingScene;
    static bool _pendingCriticalContentPreload;

    float _tipTimer;
    int   _tipIndex;
    float _dotTimer;
    int   _dotCount;
    bool  _suppressTipRotation;
    bool  _suppressDotAnimation;
    float _displayedProgress;
    float _targetProgress;

    // ── Static entry point ─────────────────────────────────────────────
    public static void LoadScene(string sceneName)
    {
        _pendingScene = sceneName;
        _pendingCriticalContentPreload = false;
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Additive);
    }

    public static void LoadSceneWithCriticalContentPreload(string sceneName)
    {
        _pendingScene = sceneName;
        _pendingCriticalContentPreload = true;
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Additive);
    }

    // ── Unity lifecycle ────────────────────────────────────────────────
    void Awake()
    {
        Instance = this;
        ConfigureProgressBar();
        SetProgressImmediate(0f);
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    void Start()
    {
        // When launched as the entry scene (WebGL cold start), default to Lobby
        if (string.IsNullOrEmpty(_pendingScene))
            _pendingScene = "Lobby";

        if (!_pendingCriticalContentPreload && tips.Length > 0 && tipText)
            tipText.text = tips[UnityEngine.Random.Range(0, tips.Length)];

        StartCoroutine(LoadRoutine());
    }

    void Update()
    {
        TickProgress();
        TickTips();
        TickDots();
    }

    // ── Load routine ───────────────────────────────────────────────────
    IEnumerator LoadRoutine()
    {
        yield return StartCoroutine(FadeCG(canvasGroup, 1f, fadeInDuration));

        if (_pendingCriticalContentPreload)
        {
            yield return StartCoroutine(PrepareCriticalRemoteContent());
            if (_pendingCriticalContentPreload)
                yield break;
        }

        float startTime = Time.realtimeSinceStartup;

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

        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);

        if (loadingLabel)
            loadingLabel.text = "Starting game";
        yield return StartCoroutine(FillBar(1f, 0.18f));

        Scene newScene = SceneManager.GetSceneByName(_pendingScene);
        SceneManager.SetActiveScene(newScene);

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene s = SceneManager.GetSceneAt(i);
            if (s.name != _pendingScene && s.name != LoadingSceneName)
                SceneManager.UnloadSceneAsync(s);
        }

        yield return StartCoroutine(FadeCG(canvasGroup, 0f, fadeOutDuration));
        SceneManager.UnloadSceneAsync(LoadingSceneName);
    }

    IEnumerator PrepareCriticalRemoteContent()
    {
        _suppressTipRotation = true;
        _suppressDotAnimation = true;

        if (loadingLabel)
            loadingLabel.text = "Preparing required game content";
        SetProgressImmediate(0f);

        var remoteContent = RemoteContentManager.EnsureInstance();
        yield return remoteContent.EnsureManifestForSession((progress, status) =>
        {
            SetProgressTarget(Mathf.Clamp01(progress * 0.12f));
            if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                loadingLabel.text = status;
        });

        if (!remoteContent.HasManifest)
        {
            ShowBlockingFailure(
                "Required content manifest failed to load.",
                string.IsNullOrWhiteSpace(remoteContent.LastError)
                    ? "The game cannot determine which remote packs are required."
                    : remoteContent.LastError);
            yield break;
        }

        if (tipText)
            tipText.text = remoteContent.BuildCriticalContentRequirementMessage();
        if (loadingLabel)
            loadingLabel.text = "Downloading required game packs";

        yield return new WaitForSeconds(0.35f);

        yield return remoteContent.PreloadCriticalContentForSession((progress, status) =>
        {
            SetProgressTarget(Mathf.Lerp(0.12f, 0.72f, Mathf.Clamp01(progress)));
            if (loadingLabel && !string.IsNullOrWhiteSpace(status))
                loadingLabel.text = status;
        });

        if (!remoteContent.HasCompletedCriticalPreload)
        {
            if (ShouldAllowLocalContentFallback(remoteContent))
            {
                Debug.LogWarning($"[LoadingScreen] Continuing with local content fallback. {remoteContent.LastError}");
                _pendingCriticalContentPreload = false;
                if (loadingLabel)
                    loadingLabel.text = "Remote content unavailable, using local assets";
                if (tipText)
                    tipText.text = "Some remote content could not be prepared. Continuing with bundled local assets for this session.";
                SetProgressTarget(0.72f);
                yield return new WaitForSeconds(0.35f);
                yield break;
            }

            ShowBlockingFailure(
                "Required gameplay packs could not be prepared.",
                string.IsNullOrWhiteSpace(remoteContent.LastError)
                    ? "The game cannot safely enter the lobby until required remote packs are downloaded."
                    : remoteContent.LastError);
            yield break;
        }

        _pendingCriticalContentPreload = false;
        if (loadingLabel)
            loadingLabel.text = "Required game content ready";
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

    void ShowBlockingFailure(string title, string detail)
    {
        _pendingCriticalContentPreload = true;
        if (loadingLabel)
            loadingLabel.text = title;
        if (tipText)
            tipText.text = detail;
        if (progressBar)
            progressBar.fillAmount = 0f;
        _displayedProgress = 0f;
        _targetProgress = 0f;
    }

    // ── Tip rotation ───────────────────────────────────────────────────
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

    // ── Dot animation ──────────────────────────────────────────────────
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

    // ── Coroutine helpers ──────────────────────────────────────────────
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
}
