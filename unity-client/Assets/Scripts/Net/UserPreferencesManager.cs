using System;
using System.Collections;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleDefender.Net
{
    [DisallowMultipleComponent]
    public sealed class UserPreferencesManager : MonoBehaviour
    {
        const string GuestCacheKey = "userprefs.guest";
        const string PlayerCachePrefix = "userprefs.player.";
        const float LocalSaveDebounceSeconds = 0.2f;
        const float RemoteSaveDebounceSeconds = 1.0f;
        const float FloatCompareEpsilon = 0.01f;

        static readonly UserPreferencesData DefaultPreferences = UserPreferencesData.CreateDefault();

        public static UserPreferencesManager Instance { get; private set; }
        public static event Action<UserPreferencesData> PreferencesChanged;

        string _activePlayerId;
        string _activeCacheKey = GuestCacheKey;
        UserPreferencesData _current = UserPreferencesData.CreateDefault();
        float _lastMutationAt = float.MinValue;
        int _mutationVersion;
        bool _localSavePending;
        bool _remoteFetchPending;
        bool _remoteFetchInFlight;
        bool _remoteSavePending;
        bool _remoteSaveInFlight;

        public static bool ShowHealthBars => Instance != null
            ? Instance._current.visuals.showHealthBars
            : DefaultPreferences.visuals.showHealthBars;

        public static bool ShowEngagementCircles => Instance != null
            ? Instance._current.visuals.showEngagementCircles
            : DefaultPreferences.visuals.showEngagementCircles;

        public static bool ShowAttackRangeCircles => Instance != null
            ? Instance._current.visuals.showAttackRangeCircles
            : DefaultPreferences.visuals.showAttackRangeCircles;

        public static bool ShowTooltips => Instance != null
            ? Instance._current.visuals.showTooltips
            : DefaultPreferences.visuals.showTooltips;

        public static UserPreferencesData CurrentPreferences => Instance != null
            ? Instance._current.Clone()
            : DefaultPreferences.Clone();

        public static UserPreferencesData CurrentPreferenceView => Instance != null
            ? Instance._current
            : DefaultPreferences;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            EnsureInstance();
        }

        public static UserPreferencesManager EnsureInstance()
        {
            if (Instance != null)
                return Instance;

            var existing = FindFirstObjectByType<UserPreferencesManager>();
            if (existing != null)
                return existing;

            var go = new GameObject("UserPreferencesManager");
            return go.AddComponent<UserPreferencesManager>();
        }

        public static void NotifyAuthenticationChanged()
        {
            EnsureInstance().ReloadForCurrentPlayer(force: true);
        }

        public static void NotifyCameraPreferencesChanged(float tilt, float zoom, float rotation)
        {
            EnsureInstance().UpdateCameraPreferences(tilt, zoom, rotation);
        }

        public static void SetEngagementCirclesVisible(bool enabled)
        {
            EnsureInstance().SetEngagementCirclesVisibleInternal(enabled);
        }

        public static void SetAttackRangeCirclesVisible(bool enabled)
        {
            EnsureInstance().SetAttackRangeCirclesVisibleInternal(enabled);
        }

        public static void SetHealthBarsVisible(bool enabled)
        {
            EnsureInstance().SetHealthBarsVisibleInternal(enabled);
        }

        public static void SetTooltipsVisible(bool enabled)
        {
            EnsureInstance().SetTooltipsVisibleInternal(enabled);
        }

        public static void NotifyMasterVolumeChanged(float linear)
        {
            EnsureInstance().SetAudioVolumeInternal(AudioChannel.Master, linear, applyRuntime: false);
        }

        public static void NotifySfxVolumeChanged(float linear)
        {
            EnsureInstance().SetAudioVolumeInternal(AudioChannel.Sfx, linear, applyRuntime: false);
        }

        public static void NotifyAmbientVolumeChanged(float linear)
        {
            EnsureInstance().SetAudioVolumeInternal(AudioChannel.Ambient, linear, applyRuntime: false);
        }

        public static void NotifyMenuMusicVolumeChanged(float linear)
        {
            EnsureInstance().SetAudioVolumeInternal(AudioChannel.MenuMusic, linear, applyRuntime: false);
        }

        public static void NotifyGameplayMusicVolumeChanged(float linear)
        {
            EnsureInstance().SetAudioVolumeInternal(AudioChannel.GameplayMusic, linear, applyRuntime: false);
        }

        public static UserAudioPreferences GetCurrentAudioPreferences()
        {
            return Instance != null
                ? Instance._current.audio.Clone()
                : DefaultPreferences.audio.Clone();
        }

        public static float SavedMasterVolume => Instance != null
            ? Instance._current.audio.masterVolume
            : DefaultPreferences.audio.masterVolume;

        public static float SavedSfxVolume => Instance != null
            ? Instance._current.audio.sfxVolume
            : DefaultPreferences.audio.sfxVolume;

        public static float SavedAmbientVolume => Instance != null
            ? Instance._current.audio.ambientVolume
            : DefaultPreferences.audio.ambientVolume;

        public static float SavedMenuMusicVolume => Instance != null
            ? Instance._current.audio.menuMusicVolume ?? DefaultPreferences.audio.menuMusicVolume ?? DefaultPreferences.audio.ambientVolume
            : DefaultPreferences.audio.menuMusicVolume ?? DefaultPreferences.audio.ambientVolume;

        public static float SavedGameplayMusicVolume => Instance != null
            ? Instance._current.audio.gameplayMusicVolume
                ?? Instance._current.audio.menuMusicVolume
                ?? DefaultPreferences.audio.menuMusicVolume
                ?? DefaultPreferences.audio.ambientVolume
            : DefaultPreferences.audio.gameplayMusicVolume
                ?? DefaultPreferences.audio.menuMusicVolume
                ?? DefaultPreferences.audio.ambientVolume;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ReloadForCurrentPlayer(force: true);
        }

        void Update()
        {
            if (_localSavePending && Time.unscaledTime - _lastMutationAt >= LocalSaveDebounceSeconds)
                FlushLocalCache();

            if (_remoteFetchPending
                && !_remoteFetchInFlight
                && AuthManager.IsAuthenticated
                && TryResolveBaseUrl(out _))
            {
                StartCoroutine(FetchPreferencesFromServer());
            }

            if (_remoteSavePending
                && !_remoteFetchInFlight
                && !_remoteSaveInFlight
                && AuthManager.IsAuthenticated
                && TryResolveBaseUrl(out _)
                && Time.unscaledTime - _lastMutationAt >= RemoteSaveDebounceSeconds)
            {
                StartCoroutine(PushPreferencesToServer());
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                FlushLocalCache();
        }

        void OnApplicationQuit()
        {
            FlushLocalCache();
        }

        void ReloadForCurrentPlayer(bool force)
        {
            string nextPlayerId = AuthManager.IsAuthenticated
                ? NormalizePlayerId(AuthManager.PlayerId)
                : null;

            if (!force && string.Equals(_activePlayerId, nextPlayerId, StringComparison.OrdinalIgnoreCase))
                return;

            FlushLocalCache();

            _activePlayerId = nextPlayerId;
            _activeCacheKey = BuildCacheKey(nextPlayerId);
            _remoteFetchPending = AuthManager.IsAuthenticated;
            _remoteFetchInFlight = false;
            _remoteSavePending = false;
            _remoteSaveInFlight = false;

            UserPreferencesData loaded = LoadLocalCache(_activeCacheKey) ?? UserPreferencesData.CreateDefault();
            SetCurrent(loaded, markDirty: false, applyToRuntime: true, broadcast: true);
        }

        void SetCurrent(UserPreferencesData preferences, bool markDirty, bool applyToRuntime, bool broadcast)
        {
            _current = NormalizePreferences(preferences);

            if (markDirty)
            {
                _mutationVersion++;
                _lastMutationAt = Time.unscaledTime;
                _localSavePending = true;
                if (AuthManager.IsAuthenticated)
                    _remoteSavePending = true;
            }

            if (applyToRuntime)
                ApplyCurrentToRuntime();

            if (broadcast)
                PreferencesChanged?.Invoke(_current.Clone());
        }

        void ApplyCurrentToRuntime()
        {
            ApplyEngagementCirclePreference();
            ApplyAttackRangeCirclePreference();
            ApplyAudioPreferencesToRuntime();
        }

        void ApplyEngagementCirclePreference()
        {
            Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");
            if (combatantType == null)
                return;

            MethodInfo method = combatantType.GetMethod(
                "SetEngagementRingDebugEnabled",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            method?.Invoke(null, new object[] { _current.visuals.showEngagementCircles });
        }

        void ApplyAttackRangeCirclePreference()
        {
            Type combatantType = FindType("CastleDefender.Game.LaneSnapshotCombatant");
            if (combatantType == null)
                return;

            MethodInfo method = combatantType.GetMethod(
                "SetAttackRingDebugEnabled",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(bool) },
                null);
            method?.Invoke(null, new object[] { _current.visuals.showAttackRangeCircles });
        }

        void ApplyAudioPreferencesToRuntime()
        {
            Type audioManagerType = FindType("AudioManager");
            if (audioManagerType == null)
                return;

            PropertyInfo instanceProperty = audioManagerType.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            object audioManager = instanceProperty?.GetValue(null);
            if (audioManager == null)
                return;

            MethodInfo applyMethod = audioManagerType.GetMethod(
                "ApplyUserPreferenceVolumes",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float), typeof(float), typeof(float), typeof(float) },
                null);
            if (applyMethod != null)
            {
                applyMethod.Invoke(audioManager, new object[]
                {
                    _current.audio.masterVolume,
                    _current.audio.sfxVolume,
                    _current.audio.menuMusicVolume ?? _current.audio.ambientVolume,
                    _current.audio.gameplayMusicVolume ?? _current.audio.menuMusicVolume ?? _current.audio.ambientVolume,
                });
                return;
            }

            applyMethod = audioManagerType.GetMethod(
                "ApplyUserPreferenceVolumes",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(float), typeof(float), typeof(float) },
                null);

            applyMethod?.Invoke(audioManager, new object[]
            {
                _current.audio.masterVolume,
                _current.audio.sfxVolume,
                _current.audio.ambientVolume,
            });
        }

        void UpdateCameraPreferences(float tilt, float zoom, float rotation)
        {
            bool changed = false;
            changed |= UpdateNullableFloat(ref _current.camera.tilt, Clamp(tilt, 0f, 52f));
            changed |= UpdateNullableFloat(ref _current.camera.zoom, Clamp(zoom, 1f, 1000f));
            changed |= UpdateNullableFloat(ref _current.camera.rotation, NormalizeRotation(rotation));

            if (!changed)
                return;

            SetCurrent(_current, markDirty: true, applyToRuntime: false, broadcast: true);
        }

        void SetEngagementCirclesVisibleInternal(bool enabled)
        {
            if (_current.visuals.showEngagementCircles == enabled)
                return;

            _current.visuals.showEngagementCircles = enabled;
            SetCurrent(_current, markDirty: true, applyToRuntime: true, broadcast: true);
        }

        void SetAttackRangeCirclesVisibleInternal(bool enabled)
        {
            if (_current.visuals.showAttackRangeCircles == enabled)
                return;

            _current.visuals.showAttackRangeCircles = enabled;
            SetCurrent(_current, markDirty: true, applyToRuntime: true, broadcast: true);
        }

        void SetHealthBarsVisibleInternal(bool enabled)
        {
            if (_current.visuals.showHealthBars == enabled)
                return;

            _current.visuals.showHealthBars = enabled;
            SetCurrent(_current, markDirty: true, applyToRuntime: false, broadcast: true);
        }

        void SetTooltipsVisibleInternal(bool enabled)
        {
            if (_current.visuals.showTooltips == enabled)
                return;

            _current.visuals.showTooltips = enabled;
            SetCurrent(_current, markDirty: true, applyToRuntime: false, broadcast: true);
        }

        void SetAudioVolumeInternal(AudioChannel channel, float linear, bool applyRuntime)
        {
            linear = Clamp(linear, 0f, 1f);
            bool changed = channel switch
            {
                AudioChannel.Master => UpdateFloat(ref _current.audio.masterVolume, linear),
                AudioChannel.Sfx => UpdateFloat(ref _current.audio.sfxVolume, linear),
                AudioChannel.Ambient =>
                    UpdateFloat(ref _current.audio.ambientVolume, linear)
                    | UpdateNullableFloat(ref _current.audio.menuMusicVolume, linear)
                    | UpdateNullableFloat(ref _current.audio.gameplayMusicVolume, linear),
                AudioChannel.MenuMusic =>
                    UpdateFloat(ref _current.audio.ambientVolume, linear)
                    | UpdateNullableFloat(ref _current.audio.menuMusicVolume, linear),
                AudioChannel.GameplayMusic =>
                    UpdateNullableFloat(ref _current.audio.gameplayMusicVolume, linear),
                _ => false,
            };

            if (!changed)
                return;

            if (applyRuntime)
                ApplyAudioPreferencesToRuntime();

            SetCurrent(_current, markDirty: true, applyToRuntime: false, broadcast: true);
        }

        UserPreferencesData LoadLocalCache(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || !PlayerPrefs.HasKey(cacheKey))
                return null;

            try
            {
                string json = PlayerPrefs.GetString(cacheKey, string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return NormalizePreferences(JsonConvert.DeserializeObject<UserPreferencesData>(json));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UserPreferences] Failed to load '{cacheKey}': {ex.Message}");
                return null;
            }
        }

        void FlushLocalCache()
        {
            if (!_localSavePending || string.IsNullOrWhiteSpace(_activeCacheKey))
                return;

            try
            {
                PlayerPrefs.SetString(_activeCacheKey, JsonConvert.SerializeObject(_current));
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UserPreferences] Failed to cache '{_activeCacheKey}': {ex.Message}");
            }

            _localSavePending = false;
        }

        IEnumerator FetchPreferencesFromServer()
        {
            if (!TryResolveBaseUrl(out string baseUrl) || string.IsNullOrWhiteSpace(AuthManager.Token))
                yield break;

            _remoteFetchPending = false;
            _remoteFetchInFlight = true;
            int startedAtMutationVersion = _mutationVersion;

            using var req = UnityWebRequest.Get(baseUrl + "/players/me/preferences");
            ApplyAuthorization(req);
            yield return req.SendWebRequest();

            _remoteFetchInFlight = false;

            if (!AuthManager.IsAuthenticated)
                yield break;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[UserPreferences] Fetch failed: {req.error}");
                yield break;
            }

            UserPreferencesResponse response;
            try
            {
                response = JsonConvert.DeserializeObject<UserPreferencesResponse>(req.downloadHandler.text);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UserPreferences] Fetch parse failed: {ex.Message}");
                yield break;
            }

            if (_mutationVersion != startedAtMutationVersion)
            {
                _remoteSavePending = true;
                yield break;
            }

            SetCurrent(response?.preferences ?? UserPreferencesData.CreateDefault(), markDirty: false, applyToRuntime: true, broadcast: true);
            _localSavePending = true;
            FlushLocalCache();
        }

        IEnumerator PushPreferencesToServer()
        {
            if (!TryResolveBaseUrl(out string baseUrl) || string.IsNullOrWhiteSpace(AuthManager.Token))
                yield break;

            _remoteSaveInFlight = true;
            int requestMutationVersion = _mutationVersion;
            string payload = JsonConvert.SerializeObject(new UserPreferencesResponse
            {
                preferences = _current.Clone(),
            });

            using var req = new UnityWebRequest(baseUrl + "/players/me/preferences", "PUT");
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            ApplyAuthorization(req);
            yield return req.SendWebRequest();

            _remoteSaveInFlight = false;

            if (!AuthManager.IsAuthenticated)
                yield break;

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[UserPreferences] Save failed: {req.error}");
                _remoteSavePending = true;
                yield break;
            }

            if (_mutationVersion != requestMutationVersion)
            {
                _remoteSavePending = true;
                yield break;
            }

            _remoteSavePending = false;
        }

        static void ApplyAuthorization(UnityWebRequest req)
        {
            if (req == null || string.IsNullOrWhiteSpace(AuthManager.Token))
                return;

            req.SetRequestHeader("Authorization", $"Bearer {AuthManager.Token}");
        }

        static string NormalizePlayerId(string playerId)
        {
            return string.IsNullOrWhiteSpace(playerId) ? null : playerId.Trim();
        }

        static string BuildCacheKey(string playerId)
        {
            return string.IsNullOrWhiteSpace(playerId)
                ? GuestCacheKey
                : $"{PlayerCachePrefix}{playerId}";
        }

        static bool TryResolveBaseUrl(out string baseUrl)
        {
            baseUrl = null;
            var networkManager = NetworkManager.Instance ?? FindFirstObjectByType<NetworkManager>();
            if (networkManager == null || string.IsNullOrWhiteSpace(networkManager.ResolvedServerUrl))
                return false;

            baseUrl = networkManager.ResolvedServerUrl.TrimEnd('/');
            return true;
        }

        static bool UpdateFloat(ref float target, float value)
        {
            if (Mathf.Abs(target - value) <= FloatCompareEpsilon)
                return false;

            target = value;
            return true;
        }

        static bool UpdateNullableFloat(ref float? target, float value)
        {
            if (target.HasValue && Mathf.Abs(target.Value - value) <= FloatCompareEpsilon)
                return false;

            target = value;
            return true;
        }

        static UserPreferencesData NormalizePreferences(UserPreferencesData preferences)
        {
            var normalized = UserPreferencesData.CreateDefault();
            if (preferences == null)
                return normalized;

            if (preferences.camera != null)
            {
                normalized.camera.tilt = NormalizeNullableCameraValue(preferences.camera.tilt, 0f, 52f);
                normalized.camera.zoom = NormalizeNullableCameraValue(preferences.camera.zoom, 1f, 1000f);
                normalized.camera.rotation = preferences.camera.rotation.HasValue
                    ? NormalizeRotation(preferences.camera.rotation.Value)
                    : null;
            }

            if (preferences.visuals != null)
            {
                normalized.visuals.showEngagementCircles = preferences.visuals.showEngagementCircles;
                normalized.visuals.showAttackRangeCircles = preferences.visuals.showAttackRangeCircles;
                normalized.visuals.showHealthBars = preferences.visuals.showHealthBars;
                normalized.visuals.showTooltips = preferences.visuals.showTooltips;
            }

            if (preferences.audio != null)
            {
                normalized.audio.masterVolume = Clamp(preferences.audio.masterVolume, 0f, 1f);
                normalized.audio.sfxVolume = Clamp(preferences.audio.sfxVolume, 0f, 1f);
                normalized.audio.ambientVolume = Clamp(preferences.audio.ambientVolume, 0f, 1f);
                normalized.audio.menuMusicVolume = preferences.audio.menuMusicVolume.HasValue
                    ? Clamp(preferences.audio.menuMusicVolume.Value, 0f, 1f)
                    : normalized.audio.ambientVolume;
                normalized.audio.gameplayMusicVolume = preferences.audio.gameplayMusicVolume.HasValue
                    ? Clamp(preferences.audio.gameplayMusicVolume.Value, 0f, 1f)
                    : null;
            }

            return normalized;
        }

        static float? NormalizeNullableCameraValue(float? value, float min, float max)
        {
            if (!value.HasValue)
                return null;

            return Clamp(value.Value, min, max);
        }

        static float NormalizeRotation(float value)
        {
            float normalized = Mathf.Repeat(value, 360f);
            if (Mathf.Approximately(normalized, 0f) && value > 0.001f)
                return 360f;

            return normalized;
        }

        static float Clamp(float value, float min, float max)
        {
            return Mathf.Clamp(value, min, max);
        }

        static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            Type type = Type.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }

        enum AudioChannel
        {
            Master,
            Sfx,
            Ambient,
            MenuMusic,
            GameplayMusic,
        }
    }

    [Serializable]
    public sealed class UserPreferencesData
    {
        public UserCameraPreferences camera = new();
        public UserVisualPreferences visuals = new();
        public UserAudioPreferences audio = new();

        public static UserPreferencesData CreateDefault()
        {
            return new UserPreferencesData
            {
                camera = UserCameraPreferences.CreateDefault(),
                visuals = UserVisualPreferences.CreateDefault(),
                audio = UserAudioPreferences.CreateDefault(),
            };
        }

        public UserPreferencesData Clone()
        {
            return new UserPreferencesData
            {
                camera = camera != null ? camera.Clone() : UserCameraPreferences.CreateDefault(),
                visuals = visuals != null ? visuals.Clone() : UserVisualPreferences.CreateDefault(),
                audio = audio != null ? audio.Clone() : UserAudioPreferences.CreateDefault(),
            };
        }
    }

    [Serializable]
    public sealed class UserCameraPreferences
    {
        public const float DefaultTilt = 28f;
        public const float DefaultZoom = 41f;
        public const float DefaultRotation = 90f;

        public float? tilt;
        public float? zoom;
        public float? rotation;

        public static UserCameraPreferences CreateDefault()
        {
            return new UserCameraPreferences();
        }

        public static float ResolveTilt(float? value)
        {
            return value ?? DefaultTilt;
        }

        public static float ResolveZoom(float? value)
        {
            return value ?? DefaultZoom;
        }

        public static float ResolveRotation(float? value)
        {
            return value ?? DefaultRotation;
        }

        public UserCameraPreferences Clone()
        {
            return new UserCameraPreferences
            {
                tilt = tilt,
                zoom = zoom,
                rotation = rotation,
            };
        }
    }

    [Serializable]
    public sealed class UserVisualPreferences
    {
        public bool showEngagementCircles = true;
        public bool showAttackRangeCircles = false;
        public bool showHealthBars = true;
        public bool showTooltips = true;

        public static UserVisualPreferences CreateDefault()
        {
            return new UserVisualPreferences();
        }

        public UserVisualPreferences Clone()
        {
            return new UserVisualPreferences
            {
                showEngagementCircles = showEngagementCircles,
                showAttackRangeCircles = showAttackRangeCircles,
                showHealthBars = showHealthBars,
                showTooltips = showTooltips,
            };
        }
    }

    [Serializable]
    public sealed class UserAudioPreferences
    {
        public float masterVolume = 1f;
        public float sfxVolume = 1f;
        public float ambientVolume = 0.5f;
        public float? menuMusicVolume = 0.5f;
        public float? gameplayMusicVolume;

        public static UserAudioPreferences CreateDefault()
        {
            return new UserAudioPreferences();
        }

        public UserAudioPreferences Clone()
        {
            return new UserAudioPreferences
            {
                masterVolume = masterVolume,
                sfxVolume = sfxVolume,
                ambientVolume = ambientVolume,
                menuMusicVolume = menuMusicVolume,
                gameplayMusicVolume = gameplayMusicVolume,
            };
        }
    }

    [Serializable]
    sealed class UserPreferencesResponse
    {
        public UserPreferencesData preferences;
    }
}
