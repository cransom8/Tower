using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Singleton audio manager.
/// Usage: AudioManager.I.Play(SFX.UnitSpawn);
///
/// Setup in Editor:
///   1. Create an AudioMixer asset: Assets/Audio/GameMixer.mixer
///      Groups: Master → SFX, Master → Ambient
///   2. Expose parameters: "MasterVol", "SFXVol", "AmbientVol"
///   3. Assign all AudioClip fields + the mixer in this component's Inspector.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    // ── Mixer ─────────────────────────────────────────────────────────────────
    [Header("Mixer")]
    public AudioMixer mixer;            // GameMixer.mixer

    // ── SFX Clips ─────────────────────────────────────────────────────────────
    [Header("Unit SFX")]
    public AudioClip unitSpawn;         // brief whoosh
    public AudioClip unitDeath;         // small impact thud
    public AudioClip runnerMove;        // optional: light footstep loop (not used by default)

    [Header("Tower SFX")]
    public AudioClip archerShoot;       // arrow whip
    public AudioClip fighterSlash;      // sword swipe
    public AudioClip mageShoot;         // arcane zap
    public AudioClip ballistaShoot;     // heavy bolt
    public AudioClip cannonShoot;       // boom
    public AudioClip cannonSplash;      // ground explosion

    [Header("Build SFX")]
    public AudioClip placeWall;         // stone clink
    public AudioClip buildTower;        // construction thunk
    public AudioClip upgradeTower;      // shimmer chime
    public AudioClip upgradeBarracks;   // fanfare sting

    [Header("Economy SFX")]
    public AudioClip goldGain;          // coin jingle (soft)
    public AudioClip goldSpend;         // coin drop
    public AudioClip lifeLost;          // gate crack thud
    public AudioClip autosendToggle;    // click

    [Header("UI SFX")]
    public AudioClip buttonClick;
    public AudioClip tabSwitch;
    public AudioClip gameOver;          // defeat low horn
    public AudioClip victory;           // victory fanfare
    public AudioClip rematch;           // short confirm chime
    public AudioClip error;             // short buzz

    [Header("Ambient")]
    public AudioClip ambientLoop;       // medieval ambient wind/crowd loop

    // ── Sources ───────────────────────────────────────────────────────────────
    AudioSource _sfxSource;
    AudioSource _ambientSource;
    bool _applyingUserPreferences;

    public float CurrentMasterVolume { get; private set; } = 1f;
    public float CurrentSfxVolume { get; private set; } = 1f;
    public float CurrentAmbientVolume { get; private set; } = 0.5f;

    // ─────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        if (I != null) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        _sfxSource     = AddSource("SFX",     "SFXVol");
        _ambientSource = AddSource("Ambient",  "AmbientVol");

        LoadVolumes();
    }

    void Start()
    {
        StartCoroutine(BeginAmbientWhenReady());
    }

    void OnDestroy()
    {
        if (I == this)
            I = null;

        _sfxSource = null;
        _ambientSource = null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public enum SFX
    {
        UnitSpawn, UnitDeath,
        ArcherShoot, FighterSlash, MageShoot, BallistaShoot, CannonShoot, CannonSplash,
        PlaceWall, BuildTower, UpgradeTower, UpgradeBarracks,
        GoldGain, GoldSpend, LifeLost, AutosendToggle,
        ButtonClick, TabSwitch, GameOver, Victory, Rematch, Error
    }

    public void Play(SFX sfx, float volumeScale = 1f)
    {
        if (this == null || _sfxSource == null)
            return;

        AudioClip clip = ClipFor(sfx);
        if (clip == null) return;
        _sfxSource.PlayOneShot(clip, volumeScale);
    }

    /// <summary>Set master volume (0..1). Persists across sessions.</summary>
    public void SetMasterVolume(float linear)
    {
        linear = Mathf.Clamp01(linear);
        CurrentMasterVolume = linear;
        SetMixerVolume("MasterVol", linear);
        if (!_applyingUserPreferences)
            NotifyManagedAudioPreferenceChange("NotifyMasterVolumeChanged", linear);
    }

    public void SetSFXVolume(float linear)
    {
        linear = Mathf.Clamp01(linear);
        CurrentSfxVolume = linear;
        SetMixerVolume("SFXVol", linear);
        if (!_applyingUserPreferences)
            NotifyManagedAudioPreferenceChange("NotifySfxVolumeChanged", linear);
    }

    public void SetAmbientVolume(float linear)
    {
        linear = Mathf.Clamp01(linear);
        CurrentAmbientVolume = linear;
        SetMixerVolume("AmbientVol", linear);
        if (!_applyingUserPreferences)
            NotifyManagedAudioPreferenceChange("NotifyAmbientVolumeChanged", linear);
    }

    public void ApplyUserPreferenceVolumes(float masterVolume, float sfxVolume, float ambientVolume)
    {
        _applyingUserPreferences = true;
        try
        {
            SetMasterVolume(masterVolume);
            SetSFXVolume(sfxVolume);
            SetAmbientVolume(ambientVolume);
        }
        finally
        {
            _applyingUserPreferences = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────────

    AudioSource AddSource(string groupName, string exposedParam)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        if (mixer != null)
        {
            AudioMixerGroup[] groups = mixer.FindMatchingGroups(groupName);
            if (groups.Length > 0) src.outputAudioMixerGroup = groups[0];
        }
        return src;
    }

    void PlayAmbient()
    {
        if (ambientLoop == null) return;
        _ambientSource.clip = ambientLoop;
        _ambientSource.loop = true;
        _ambientSource.volume = 0.35f;
        _ambientSource.Play();
    }

    System.Collections.IEnumerator BeginAmbientWhenReady()
    {
        const float timeoutSeconds = 2f;
        float elapsed = 0f;

        while (!HasActiveAudioListener() && elapsed < timeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!HasActiveAudioListener())
        {
            Debug.LogWarning("[AudioManager] Ambient playback skipped because no active AudioListener was found.");
            yield break;
        }

        PlayAmbient();
    }

    static bool HasActiveAudioListener()
    {
        var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < listeners.Length; i++)
        {
            var listener = listeners[i];
            if (listener != null && listener.enabled && listener.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    void LoadVolumes()
    {
        if (TryGetManagedAudioPreferences(out float masterVolume, out float sfxVolume, out float ambientVolume))
        {
            ApplyUserPreferenceVolumes(masterVolume, sfxVolume, ambientVolume);
            return;
        }

        ApplyUserPreferenceVolumes(CurrentMasterVolume, CurrentSfxVolume, CurrentAmbientVolume);
    }

    void SetMixerVolume(string param, float linear)
    {
        if (mixer == null) return;
        // AudioMixer uses dB; map 0..1 → -80..0
        float db = linear <= 0f ? -80f : Mathf.Log10(linear) * 20f;
        mixer.SetFloat(param, db);
    }

    AudioClip ClipFor(SFX sfx) => sfx switch {
        SFX.UnitSpawn       => unitSpawn,
        SFX.UnitDeath       => unitDeath,
        SFX.ArcherShoot     => archerShoot,
        SFX.FighterSlash    => fighterSlash,
        SFX.MageShoot       => mageShoot,
        SFX.BallistaShoot   => ballistaShoot,
        SFX.CannonShoot     => cannonShoot,
        SFX.CannonSplash    => cannonSplash,
        SFX.PlaceWall       => placeWall,
        SFX.BuildTower      => buildTower,
        SFX.UpgradeTower    => upgradeTower,
        SFX.UpgradeBarracks => upgradeBarracks,
        SFX.GoldGain        => goldGain,
        SFX.GoldSpend       => goldSpend,
        SFX.LifeLost        => lifeLost,
        SFX.AutosendToggle  => autosendToggle,
        SFX.ButtonClick     => buttonClick,
        SFX.TabSwitch       => tabSwitch,
        SFX.GameOver        => gameOver,
        SFX.Victory         => victory,
        SFX.Rematch         => rematch,
        SFX.Error           => error,
        _                   => null
    };

    static bool TryGetManagedAudioPreferences(out float masterVolume, out float sfxVolume, out float ambientVolume)
    {
        masterVolume = 1f;
        sfxVolume = 1f;
        ambientVolume = 0.5f;

        System.Type managerType = FindType("CastleDefender.Net.UserPreferencesManager");
        if (managerType == null)
            return false;

        managerType.GetMethod("EnsureInstance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null);

        masterVolume = ReadStaticFloat(managerType, "SavedMasterVolume", masterVolume);
        sfxVolume = ReadStaticFloat(managerType, "SavedSfxVolume", sfxVolume);
        ambientVolume = ReadStaticFloat(managerType, "SavedAmbientVolume", ambientVolume);
        return true;
    }

    static void NotifyManagedAudioPreferenceChange(string methodName, float value)
    {
        System.Type managerType = FindType("CastleDefender.Net.UserPreferencesManager");
        if (managerType == null)
            return;

        managerType.GetMethod(
                methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null,
                new[] { typeof(float) },
                null)
            ?.Invoke(null, new object[] { value });
    }

    static float ReadStaticFloat(System.Type type, string propertyName, float fallback)
    {
        object value = type
            ?.GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.GetValue(null);
        return value is float floatValue ? floatValue : fallback;
    }

    static System.Type FindType(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return null;

        System.Type type = System.Type.GetType(fullName, false);
        if (type != null)
            return type;

        System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            type = assemblies[i].GetType(fullName, false);
            if (type != null)
                return type;
        }

        return null;
    }
}
