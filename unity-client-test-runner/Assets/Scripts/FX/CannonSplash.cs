using UnityEngine;

/// <summary>
/// Cannon splash FX: a ground shockwave ring + debris burst spawned at landing tile.
/// Server sends splash as part of ml_snapshot projectile data; SnapshotApplier calls
/// CannonSplash.Play() when it detects a projectile that just landed (or via hit event).
/// </summary>
public class CannonSplash : MonoBehaviour
{
    [Header("Particle Systems — assign in prefab")]
    public ParticleSystem shockwavePS;  // flat ring expanding outward (Y=0)
    public ParticleSystem debrisPS;     // chunks flung upward
    public ParticleSystem dustPS;       // low smoke cloud

    [Header("Radius visual hint")]
    public LineRenderer radiusRing;     // optional: shows 1.5-tile splash radius indicator briefly
    public float ringFadeDuration = 0.35f;

    static CannonSplash _prefab;
    public static bool _prefabSet => _prefab != null;
    static readonly System.Collections.Generic.Queue<CannonSplash> _pool = new();

    // ─────────────────────────────────────────────────────────────────────────
    // Pool API
    // ─────────────────────────────────────────────────────────────────────────

    public static void Init(CannonSplash prefab, int preWarm = 4)
    {
        _prefab = prefab;
        for (int i = 0; i < preWarm; i++)
            Return(CreateNew());
    }

    public static void Play(Vector3 worldPos)
    {
        var splash = GetFromPool();
        splash.transform.position = worldPos;
        splash.gameObject.SetActive(true);
        splash.StartEffect();
    }

    static CannonSplash GetFromPool()
    {
        while (_pool.Count > 0)
        {
            var s = _pool.Dequeue();
            if (s != null) return s;
        }
        return CreateNew();
    }

    static CannonSplash CreateNew()
    {
        var go = Object.Instantiate(_prefab.gameObject);
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);
        return go.GetComponent<CannonSplash>();
    }

    static void Return(CannonSplash s)
    {
        if (s == null) return;
        s.CancelInvoke();
        s.gameObject.SetActive(false);
        _pool.Enqueue(s);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Effect playback
    // ─────────────────────────────────────────────────────────────────────────

    void StartEffect()
    {
        PlayPS(shockwavePS);
        PlayPS(debrisPS);
        PlayPS(dustPS);

        if (radiusRing != null)
            StartCoroutine(FadeRing());

        // Return to pool after all particles die
        float maxLife = MaxLifetime(shockwavePS, debrisPS, dustPS);
        Invoke(nameof(ReturnSelf), maxLife + 0.2f);
    }

    static void PlayPS(ParticleSystem ps)
    {
        if (!ps) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Play();
    }

    static float MaxLifetime(params ParticleSystem[] systems)
    {
        float max = 0f;
        foreach (var ps in systems)
        {
            if (!ps) continue;
            float life = ps.main.duration + ps.main.startLifetime.constantMax;
            if (life > max) max = life;
        }
        return max;
    }

    System.Collections.IEnumerator FadeRing()
    {
        radiusRing.enabled = true;
        float t = 0f;
        Color start = radiusRing.startColor;
        while (t < ringFadeDuration)
        {
            t += Time.deltaTime;
            float a = Mathf.Lerp(1f, 0f, t / ringFadeDuration);
            radiusRing.startColor = new Color(start.r, start.g, start.b, a);
            radiusRing.endColor   = radiusRing.startColor;
            yield return null;
        }
        radiusRing.enabled = false;
    }

    void ReturnSelf()
    {
        StopAllCoroutines();
        Return(this);
    }
}
