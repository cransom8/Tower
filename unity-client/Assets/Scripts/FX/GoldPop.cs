using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Coin-pop effect: a small coin icon + gold amount floats up from a tile.
/// Triggered by InfoBar.cs when it detects gold income payouts.
/// </summary>
public class GoldPop : MonoBehaviour
{
    [Header("References — assign in prefab")]
    public TextMeshProUGUI amountText;
    public RectTransform   coinIcon;    // small gold coin sprite

    [Header("Config")]
    public float riseDistance = 80f;   // UI units (canvas pixels)
    public float duration     = 1.0f;

    static GoldPop _prefab;
    static readonly System.Collections.Generic.Queue<GoldPop> _pool = new();

    // ── Pool API ─────────────────────────────────────────────────────────────

    public static void Init(GoldPop prefab, int preWarm = 6)
    {
        _prefab = prefab;
        for (int i = 0; i < preWarm; i++)
            Return(CreateNew());
    }

    public static void Spawn(Transform parent, Vector2 anchoredPos, float amount)
    {
        if (_prefab == null) { Debug.LogWarning("[GoldPop] Prefab not set."); return; }
        GoldPop pop = GetFromPool();
        pop.transform.SetParent(parent, false);
        ((RectTransform)pop.transform).anchoredPosition = anchoredPos;
        pop.gameObject.SetActive(true);
        pop.Play(amount);
    }

    static GoldPop GetFromPool()
    {
        while (_pool.Count > 0)
        {
            var p = _pool.Dequeue();
            if (p != null) return p;
        }
        return CreateNew();
    }

    static GoldPop CreateNew()
    {
        var go = Object.Instantiate(_prefab.gameObject);
        go.SetActive(false);
        return go.GetComponent<GoldPop>();
    }

    static void Return(GoldPop p)
    {
        if (p == null) return;
        p.StopAllCoroutines();
        p.gameObject.SetActive(false);
        _pool.Enqueue(p);
    }

    // ── Playback ─────────────────────────────────────────────────────────────

    void Play(float amount)
    {
        amountText.text = $"+{amount:0.#}g";
        var cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        StopAllCoroutines();
        StartCoroutine(PlayRoutine(cg));
    }

    IEnumerator PlayRoutine(CanvasGroup cg)
    {
        var rt = (RectTransform)transform;
        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos   = startPos + Vector2.up * riseDistance;

        // Coin punch scale: grow to 1.3, shrink back to 1.0
        yield return StartCoroutine(ScaleTo(coinIcon, Vector3.one * 1.3f, 0.10f));
        yield return StartCoroutine(ScaleTo(coinIcon, Vector3.one,        0.08f));

        // Float upward + fade out in last 40%
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float n     = Mathf.Clamp01(t / duration);
            float riseN = 1f - Mathf.Pow(1f - n, 3f);   // OutCubic
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, riseN);
            cg.alpha = 1f - Mathf.Clamp01((n - 0.6f) / 0.4f);
            yield return null;
        }

        Return(this);
    }

    static IEnumerator ScaleTo(Transform t, Vector3 target, float dur)
    {
        Vector3 from = t.localScale;
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            t.localScale = Vector3.Lerp(from, target, Mathf.Clamp01(elapsed / dur));
            yield return null;
        }
        t.localScale = target;
    }
}
