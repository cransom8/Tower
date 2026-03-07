using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Spawned on a World Space Canvas. Floats upward and fades out.
/// Use FloatingText.Spawn() — do NOT call new or Instantiate directly.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class FloatingText : MonoBehaviour
{
    // Assign in Inspector: a prefab with Canvas (World Space) + FloatingText + TMP.
    public static FloatingText Prefab;          // set by GameManager at startup

    [Header("Motion")]
    public float riseDistance   = 1.4f;         // world-space units to travel up
    public float duration       = 1.1f;         // total lifetime

    [Header("Colors — set per call")]
    public Color goldColor   = new Color(1f, 0.85f, 0.2f);
    public Color damageColor = new Color(1f, 0.3f, 0.3f);
    public Color lifeColor   = new Color(0.4f, 0.9f, 1f);

    TextMeshProUGUI _tmp;

    void Awake() => _tmp = GetComponent<TextMeshProUGUI>();

    public enum Kind { Gold, Damage, LifeLoss }

    /// <summary>Spawn a floating text at the given world position.</summary>
    public static void Spawn(string text, Vector3 worldPos, Kind kind = Kind.Gold)
    {
        if (Prefab == null) { Debug.LogWarning("[FloatingText] Prefab not set."); return; }
        var go = Instantiate(Prefab.gameObject, worldPos, Quaternion.identity);
        go.SetActive(true);
        go.GetComponent<FloatingText>().Play(text, kind);
    }

    void Play(string text, Kind kind)
    {
        _tmp.text = text;
        _tmp.color = kind switch {
            Kind.Damage   => damageColor,
            Kind.LifeLoss => lifeColor,
            _             => goldColor
        };
        float drift = Random.Range(-0.3f, 0.3f);
        StartCoroutine(FloatRoutine(drift));
    }

    IEnumerator FloatRoutine(float drift)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos   = startPos + new Vector3(drift, riseDistance, 0f);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float n = Mathf.Clamp01(t / duration);
            // OutCubic rise, InCubic fade
            float riseN  = 1f - Mathf.Pow(1f - n, 3f);
            float fadeN  = n * n * n;
            transform.position = Vector3.Lerp(startPos, endPos, riseN);
            _tmp.alpha = 1f - fadeN;
            yield return null;
        }
        Destroy(gameObject);
    }
}
