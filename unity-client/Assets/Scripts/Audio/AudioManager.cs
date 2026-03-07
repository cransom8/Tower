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

    // Prefs keys
    const string PrefMaster  = "vol_master";
    const string PrefSFX     = "vol_sfx";
    const string PrefAmbient = "vol_ambient";

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
        PlayAmbient();
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
        AudioClip clip = ClipFor(sfx);
        if (clip == null) return;
        _sfxSource.PlayOneShot(clip, volumeScale);
    }

    /// <summary>Set master volume (0..1). Persists across sessions.</summary>
    public void SetMasterVolume(float linear)
    {
        SetMixerVolume("MasterVol", linear);
        PlayerPrefs.SetFloat(PrefMaster, linear);
    }

    public void SetSFXVolume(float linear)
    {
        SetMixerVolume("SFXVol", linear);
        PlayerPrefs.SetFloat(PrefSFX, linear);
    }

    public void SetAmbientVolume(float linear)
    {
        SetMixerVolume("AmbientVol", linear);
        PlayerPrefs.SetFloat(PrefAmbient, linear);
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

    void LoadVolumes()
    {
        SetMixerVolume("MasterVol",  PlayerPrefs.GetFloat(PrefMaster,  1f));
        SetMixerVolume("SFXVol",     PlayerPrefs.GetFloat(PrefSFX,     1f));
        SetMixerVolume("AmbientVol", PlayerPrefs.GetFloat(PrefAmbient, 0.5f));
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
}
