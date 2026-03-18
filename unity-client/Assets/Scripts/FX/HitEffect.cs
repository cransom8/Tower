using UnityEngine;

/// <summary>
/// Object-pooled hit particle effect. One instance covers all non-splash hit types.
/// Pool is pre-warmed at game start via HitEffectPool (see below).
/// </summary>
public class HitEffect : MonoBehaviour
{
    [Header("Particle Systems — assign in prefab Inspector")]
    public ParticleSystem sparkPS;      // small burst sparks (all tower types)
    public ParticleSystem impactPS;     // ground ring flash

    [Header("Per-tower-type colors")]
    public Color archerColor  = new Color(0.9f, 0.8f, 0.3f);   // warm yellow
    public Color fighterColor = new Color(0.85f, 0.3f, 0.3f);  // red
    public Color mageColor    = new Color(0.5f, 0.3f, 1.0f);   // purple
    public Color ballistaColor= new Color(0.6f, 0.9f, 0.4f);   // green pierce
    public Color cannonColor  = new Color(1.0f, 0.55f, 0.1f);  // orange fire

    public enum TowerType { Archer, Fighter, Mage, Ballista, Cannon }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Play a hit burst at the given world position.</summary>
    public void Play(Vector3 worldPos, TowerType tower)
    {
        transform.position = worldPos;
        gameObject.SetActive(true);

        Color c = ColorFor(tower);
        SetParticleColor(sparkPS,  c);
        SetParticleColor(impactPS, c);

        if (sparkPS)  { sparkPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); sparkPS.Play(); }
        if (impactPS) { impactPS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); impactPS.Play(); }

        // Auto-return to pool after longest particle lifetime
        float maxLife = Mathf.Max(
            sparkPS  ? sparkPS.main.duration  + sparkPS.main.startLifetime.constantMax  : 0f,
            impactPS ? impactPS.main.duration + impactPS.main.startLifetime.constantMax : 0f
        );
        Invoke(nameof(ReturnToPool), maxLife + 0.1f);
    }

    void ReturnToPool()
    {
        CancelInvoke();
        gameObject.SetActive(false);
        HitEffectPool.Return(this);
    }

    Color ColorFor(TowerType t) => t switch {
        TowerType.Archer   => archerColor,
        TowerType.Fighter  => fighterColor,
        TowerType.Mage     => mageColor,
        TowerType.Ballista => ballistaColor,
        TowerType.Cannon   => cannonColor,
        _                  => Color.white
    };

    static void SetParticleColor(ParticleSystem ps, Color c)
    {
        if (!ps) return;
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(c);
    }
}

// =============================================================================
// Pool
// =============================================================================

public static class HitEffectPool
{
    static HitEffect _prefab;
    static readonly System.Collections.Generic.Queue<HitEffect> _pool = new();

    /// <summary>Call once at game start to warm the pool.</summary>
    public static void Init(HitEffect prefab, int preWarm = 12)
    {
        _prefab = prefab;
        for (int i = 0; i < preWarm; i++)
            Return(CreateNew());
    }

    public static HitEffect Get()
    {
        while (_pool.Count > 0)
        {
            var e = _pool.Dequeue();
            if (e != null) return e;
        }
        return CreateNew();
    }

    public static void Return(HitEffect e)
    {
        if (e == null) return;
        e.CancelInvoke();
        e.gameObject.SetActive(false);
        _pool.Enqueue(e);
    }

    static HitEffect CreateNew()
    {
        var go = Object.Instantiate(_prefab.gameObject);
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);
        return go.GetComponent<HitEffect>();
    }
}
