using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Runtime post-processing control.
///
/// Setup:
///   1. In Game_ML scene, create a Global Volume (GameObject → Volume → Global Volume).
///   2. Add a Volume Profile: Bloom, Vignette, Color Grading (Lift/Gamma/Gain), Depth of Field.
///   3. Assign the Volume component to this script's `volume` field.
///   4. Attach this script to a persistent manager GameObject.
///
/// Auto-detects mobile (iOS/Android) and reduces quality on first run.
/// Exposed for the Settings UI to call SetQualityPreset().
/// </summary>
public class PostProcessController : MonoBehaviour
{
    public static PostProcessController I { get; private set; }

    [Header("Volume Reference")]
    public Volume volume;

    // ── Cached effect references ──────────────────────────────────────────────
    Bloom        _bloom;
    Vignette     _vignette;
    ColorAdjustments _colorAdj;

    // ── Presets ───────────────────────────────────────────────────────────────
    public enum QualityPreset { Low, Medium, High }

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        if (volume == null)
            volume = FindFirstObjectByType<Volume>();

        if (volume == null || volume.profile == null)
        {
            Debug.LogWarning("[PostProcess] No Volume assigned.");
            return;
        }

        volume.profile.TryGet(out _bloom);
        volume.profile.TryGet(out _vignette);
        volume.profile.TryGet(out _colorAdj);

        // Auto-select preset
        bool isMobile = Application.platform == RuntimePlatform.Android
                     || Application.platform == RuntimePlatform.IPhonePlayer;
        SetQualityPreset(isMobile ? QualityPreset.Low : QualityPreset.High);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public void SetQualityPreset(QualityPreset preset)
    {
        switch (preset)
        {
            case QualityPreset.Low:
                SetBloom(enabled: false, threshold: 1f, intensity: 0f, scatter: 0.7f);
                SetVignette(enabled: false);
                break;

            case QualityPreset.Medium:
                SetBloom(enabled: true,  threshold: 0.9f, intensity: 0.8f, scatter: 0.7f);
                SetVignette(enabled: true, intensity: 0.2f);
                if (_colorAdj != null) _colorAdj.active = false;
                break;

            case QualityPreset.High:
                SetBloom(enabled: true,  threshold: 0.75f, intensity: 1.4f, scatter: 0.65f);
                SetVignette(enabled: true, intensity: 0.28f);
                SetColorGrading(saturation: 15f, contrast: 12f);
                break;
        }
    }

    /// <summary>Flash bloom for a brief "impact" moment (cannon hit, life lost).</summary>
    public void ImpactFlash()
    {
        if (_bloom == null || !_bloom.active) return;
        StartCoroutine(BloomFlashRoutine());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    void SetBloom(bool enabled, float threshold, float intensity, float scatter)
    {
        if (_bloom == null) return;
        _bloom.active          = enabled;
        _bloom.threshold.value = threshold;
        _bloom.intensity.value = intensity;
        _bloom.scatter.value   = scatter;
    }

    void SetVignette(bool enabled, float intensity = 0.25f, float smoothness = 0.4f)
    {
        if (_vignette == null) return;
        _vignette.active            = enabled;
        _vignette.intensity.value   = intensity;
        _vignette.smoothness.value  = smoothness;
    }

    void SetColorGrading(float saturation = 0f, float contrast = 0f)
    {
        if (_colorAdj == null) return;
        _colorAdj.active            = true;
        _colorAdj.saturation.value  = saturation;
        _colorAdj.contrast.value    = contrast;
    }

    System.Collections.IEnumerator BloomFlashRoutine()
    {
        float baseIntensity = _bloom.intensity.value;
        float peak          = baseIntensity + 2.5f;
        float t = 0f;
        float riseTime  = 0.05f;
        float fallTime  = 0.25f;

        // Rise
        while (t < riseTime)
        {
            t += Time.deltaTime;
            _bloom.intensity.value = Mathf.Lerp(baseIntensity, peak, t / riseTime);
            yield return null;
        }

        t = 0f;
        // Fall
        while (t < fallTime)
        {
            t += Time.deltaTime;
            _bloom.intensity.value = Mathf.Lerp(peak, baseIntensity, t / fallTime);
            yield return null;
        }

        _bloom.intensity.value = baseIntensity;
    }
}
