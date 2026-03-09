using System.Collections;
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

    float _tipTimer;
    int   _tipIndex;
    float _dotTimer;
    int   _dotCount;

    // ── Static entry point ─────────────────────────────────────────────
    public static void LoadScene(string sceneName)
    {
        _pendingScene = sceneName;
        SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Additive);
    }

    // ── Unity lifecycle ────────────────────────────────────────────────
    void Awake()
    {
        Instance = this;
        if (progressBar) progressBar.fillAmount = 0f;
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    void Start()
    {
        // When launched as the entry scene (WebGL cold start), default to Lobby
        if (string.IsNullOrEmpty(_pendingScene))
            _pendingScene = "Lobby";

        if (tips.Length > 0 && tipText)
            tipText.text = tips[Random.Range(0, tips.Length)];

        StartCoroutine(LoadRoutine());
    }

    void Update()
    {
        TickTips();
        TickDots();
    }

    // ── Load routine ───────────────────────────────────────────────────
    IEnumerator LoadRoutine()
    {
        yield return StartCoroutine(FadeCG(canvasGroup, 1f, fadeInDuration));

        float startTime = Time.realtimeSinceStartup;

        AsyncOperation op = SceneManager.LoadSceneAsync(_pendingScene, LoadSceneMode.Additive);
        op.allowSceneActivation = false;

        while (op.progress < 0.9f)
        {
            if (progressBar)
                progressBar.fillAmount = Mathf.Clamp01(op.progress / 0.9f);
            yield return null;
        }

        float elapsed = Time.realtimeSinceStartup - startTime;
        if (elapsed < minDisplayTime)
            yield return new WaitForSeconds(minDisplayTime - elapsed);

        yield return StartCoroutine(FillBar(1f, 0.2f));

        op.allowSceneActivation = true;
        yield return new WaitUntil(() => op.isDone);

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

    // ── Tip rotation ───────────────────────────────────────────────────
    void TickTips()
    {
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
        if (!progressBar) yield break;
        float start = progressBar.fillAmount;
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            progressBar.fillAmount = Mathf.Lerp(start, target, t / duration);
            yield return null;
        }
        progressBar.fillAmount = target;
    }
}
