using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace CastleDefender.Net
{
    public class RemoteContentManager : MonoBehaviour
    {
        public enum CriticalPreloadFailureStage
        {
            None,
            ManifestDownload,
            ManifestParse,
            ManifestValidation,
            AddressablesInitialization,
            DownloadSizing,
            ContentDownload,
            AssetLoad,
        }

        enum ManifestPreloadPhase
        {
            Loadout,
            WaveGameplay,
        }

        public static RemoteContentManager Instance { get; private set; }
        public const string GameMlEnvironmentAddress = "environment/game_ml";
        public const string GameMlEnvironmentDressingAddress = "environment/game_ml_dressing";

        public ContentManifestResponse Manifest { get; private set; }
        public bool HasManifest => Manifest != null;
        public bool HasCompletedLobbyEntryPreparation { get; private set; }
        public bool HasCompletedLoadoutPreload { get; private set; }
        public bool HasCompletedWavePreload { get; private set; }
        public bool HasCompletedCriticalPreload => HasCompletedWavePreload;
        public bool AreAddressablesInitialized => _addressablesReady;
        public string LastError { get; private set; }
        public float LastProgress { get; private set; }
        public string LastStatus { get; private set; } = "";
        public CriticalPreloadFailureStage LastFailureStage { get; private set; }
        public bool HasRetryableFailure => LastFailureStage != CriticalPreloadFailureStage.None;
        public string LastAddressablesCallError { get; private set; }

        readonly HashSet<string> _preloadedContentKeys = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, GameObject> _loadedPrefabsByContentKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AsyncOperationHandle<GameObject>> _assetHandlesByContentKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Texture2D> _loadedPortraitsByKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AsyncOperationHandle<Texture2D>> _portraitHandlesByKey = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _portraitKeysInFlight = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, GameObject> _loadedEnvironmentPrefabsByAddress = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AsyncOperationHandle<GameObject>> _environmentHandlesByAddress = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _environmentAddressesInFlight = new(StringComparer.OrdinalIgnoreCase);
        bool _addressablesReady;
        AsyncOperationHandle _initializationHandle;
        bool _hasInitializationHandle;
        bool _lobbyEntryPreparationRunning;
        bool _loadoutPreloadRunning;
        bool _wavePreloadRunning;

        public static RemoteContentManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("RemoteContentManager");
            return go.AddComponent<RemoteContentManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ConfigureAddressablesRuntimeProperties();
            Addressables.InternalIdTransformFunc = TransformInternalId;
        }

        void OnDestroy()
        {
            foreach (var handle in _assetHandlesByContentKey.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }

            _assetHandlesByContentKey.Clear();
            _loadedPrefabsByContentKey.Clear();

            foreach (var handle in _portraitHandlesByKey.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }

            _portraitHandlesByKey.Clear();
            _loadedPortraitsByKey.Clear();

            foreach (var handle in _environmentHandlesByAddress.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }

            _environmentHandlesByAddress.Clear();
            _loadedEnvironmentPrefabsByAddress.Clear();

            if (_hasInitializationHandle && _initializationHandle.IsValid())
                Addressables.Release(_initializationHandle);

            _hasInitializationHandle = false;
            _addressablesReady = false;

            if (Instance == this)
                Instance = null;
        }

        string BaseUrl
        {
            get
            {
                if (NetworkManager.Instance != null)
                    return NetworkManager.Instance.ResolvedServerUrl;
#if UNITY_WEBGL && !UNITY_EDITOR
                var page = new Uri(Application.absoluteURL);
                bool standard = (page.Scheme == "https" && page.Port == 443)
                             || (page.Scheme == "http" && page.Port == 80)
                             || page.Port < 0;
                return standard
                    ? $"{page.Scheme}://{page.Host}"
                    : $"{page.Scheme}://{page.Host}:{page.Port}";
#else
                return "http://127.0.0.1:3000";
#endif
            }
        }

        string TransformInternalId(IResourceLocation location)
        {
            string internalId = location?.InternalId;
            if (string.IsNullOrWhiteSpace(internalId))
                return internalId;

            if (Uri.TryCreate(internalId, UriKind.Absolute, out _))
                return internalId;

            if (internalId.StartsWith("/"))
                return BaseUrl.TrimEnd('/') + internalId;

            return internalId;
        }

        public IEnumerator PrepareLobbyEntryContentForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false, string requester = null)
        {
            RemoteContentVerification.RecordOwnerRequest("t0.lobby_entry", requester, _lobbyEntryPreparationRunning);
            if (_lobbyEntryPreparationRunning)
            {
                while (_lobbyEntryPreparationRunning)
                    yield return null;
                yield break;
            }

            _lobbyEntryPreparationRunning = true;
            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;
            HasCompletedLobbyEntryPreparation = false;

            try
            {
                ReportProgress(0f, "Preparing lobby content...", onProgress);

                yield return EnsureManifestForSession((progress, status) =>
                {
                    ReportProgress(Mathf.Lerp(0f, 0.45f, Mathf.Clamp01(progress)), status, onProgress);
                }, forceRefreshManifest, requester);

                if (!HasManifest)
                {
                    if (string.IsNullOrWhiteSpace(LastError))
                        LastError = "Could not download the content manifest.";
                    if (LastFailureStage == CriticalPreloadFailureStage.None)
                        LastFailureStage = CriticalPreloadFailureStage.ManifestDownload;
                    yield break;
                }

                yield return InitializeAddressables((progress, status) =>
                {
                    ReportProgress(Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(progress)), status, onProgress);
                });

                if (!_addressablesReady)
                {
                    if (string.IsNullOrWhiteSpace(LastError))
                        LastError = "Remote content system failed to initialize.";
                    yield break;
                }

                HasCompletedLobbyEntryPreparation = true;
                ReportProgress(1f, "Lobby content ready.", onProgress);
            }
            finally
            {
                _lobbyEntryPreparationRunning = false;
            }
        }

        public IEnumerator PreloadLoadoutContentForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false, string requester = null)
        {
            yield return PreloadManifestPhaseContentForSession(ManifestPreloadPhase.Loadout, onProgress, forceRefreshManifest, requester);
        }

        public IEnumerator PreloadWaveContentForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false, string requester = null)
        {
            yield return PreloadManifestPhaseContentForSession(ManifestPreloadPhase.WaveGameplay, onProgress, forceRefreshManifest, requester);
        }

        public IEnumerator PreloadCriticalContentForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false, string requester = null)
        {
            yield return PreloadWaveContentForSession(onProgress, forceRefreshManifest, requester);
        }

        IEnumerator PreloadManifestPhaseContentForSession(
            ManifestPreloadPhase phase,
            Action<float, string> onProgress = null,
            bool forceRefreshManifest = false,
            string requester = null)
        {
            string verificationKey = phase == ManifestPreloadPhase.Loadout ? "loadout.content" : "wave.content";
            bool alreadyReady = phase == ManifestPreloadPhase.Loadout ? HasCompletedLoadoutPreload : HasCompletedWavePreload;
            bool isRunning = phase == ManifestPreloadPhase.Loadout ? _loadoutPreloadRunning : _wavePreloadRunning;
            string readyMessage = phase == ManifestPreloadPhase.Loadout
                ? "All loadout-critical content already ready."
                : "All wave-critical content already ready.";
            string emptyMessage = phase == ManifestPreloadPhase.Loadout
                ? "No loadout-critical content required."
                : "No wave-critical content required.";
            string preparingMessage = phase == ManifestPreloadPhase.Loadout
                ? "Preparing loadout content..."
                : "Preparing gameplay content...";

            RemoteContentVerification.RecordOwnerRequest(verificationKey, requester, isRunning);
            if (alreadyReady && !forceRefreshManifest)
            {
                RemoteContentVerification.RecordReuse(verificationKey, "source=already_ready");
                ReportProgress(1f, readyMessage, onProgress);
                yield break;
            }

            if (isRunning)
            {
                while (phase == ManifestPreloadPhase.Loadout ? _loadoutPreloadRunning : _wavePreloadRunning)
                    yield return null;
                yield break;
            }

            if (phase == ManifestPreloadPhase.Loadout) _loadoutPreloadRunning = true;
            else _wavePreloadRunning = true;

            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;
            if (phase == ManifestPreloadPhase.Loadout) HasCompletedLoadoutPreload = false;
            else HasCompletedWavePreload = false;

            try
            {
                ReportProgress(0f, preparingMessage, onProgress);

                if (forceRefreshManifest || Manifest == null)
                {
                    yield return FetchManifest(onProgress);
                    if (Manifest == null)
                    {
                        if (string.IsNullOrEmpty(LastError))
                            LastError = "Could not download the content manifest.";
                        if (LastFailureStage == CriticalPreloadFailureStage.None)
                            LastFailureStage = CriticalPreloadFailureStage.ManifestDownload;
                        yield break;
                    }
                }

                var critical = phase == ManifestPreloadPhase.Loadout
                    ? GetLoadoutCriticalContentEntries()
                    : GetWaveCriticalContentEntries();
                if (critical.Length == 0)
                {
                    RemoteContentVerification.RecordReuse(verificationKey, "source=no_required_entries");
                    ReportProgress(1f, emptyMessage, onProgress);
                    if (phase == ManifestPreloadPhase.Loadout) HasCompletedLoadoutPreload = true;
                    else HasCompletedWavePreload = true;
                    yield break;
                }

                yield return InitializeAddressables(onProgress);
                if (!_addressablesReady)
                {
                    if (string.IsNullOrEmpty(LastError))
                        LastError = "Addressables failed to initialize.";
                    yield break;
                }

                var failures = new List<string>();
                var requests = BuildDownloadRequests(critical, failures);
                if (failures.Count > 0)
                {
                    LastFailureStage = CriticalPreloadFailureStage.ManifestValidation;
                    LastError = string.Join("\n", failures);
                    ReportProgress(1f, "Required content is missing.", onProgress);
                    yield break;
                }

                if (requests.Count == 0)
                {
                    RemoteContentVerification.RecordReuse(verificationKey, "source=cached_all");
                    ReportProgress(1f, "All required content already cached.", onProgress);
                    if (phase == ManifestPreloadPhase.Loadout) HasCompletedLoadoutPreload = true;
                    else HasCompletedWavePreload = true;
                    yield break;
                }

                if (phase == ManifestPreloadPhase.WaveGameplay
                    && RemoteContentVerification.ConsumeFailure(
                        RemoteContentVerification.FaultKind.T1GameplayDownload,
                        "PreloadWaveContentForSession",
                        out string forcedWaveFailure))
                {
                    LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                    LastError = forcedWaveFailure;
                    ReportProgress(1f, "Required content could not be prepared.", onProgress);
                    yield break;
                }

#if UNITY_WEBGL && !UNITY_EDITOR
                yield return LoadCriticalAssetsDirectly(requests, failures, onProgress);
                if (failures.Count > 0)
                {
                    if (LastFailureStage == CriticalPreloadFailureStage.None)
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    LastError = string.Join("\n", failures);
                    ReportProgress(1f, "Required content is missing.", onProgress);
                    yield break;
                }

                if (phase == ManifestPreloadPhase.Loadout) HasCompletedLoadoutPreload = true;
                else HasCompletedWavePreload = true;
                ReportProgress(1f, $"Prepared {_preloadedContentKeys.Count} content packs.", onProgress);
                yield break;
#endif

                long totalDownloadBytes = 0L;
                for (int i = 0; i < requests.Count; i++)
                {
                    AsyncOperationHandle<long> sizeHandle;
                    try
                    {
                        sizeHandle = Addressables.GetDownloadSizeAsync(requests[i].DownloadKey);
                    }
                    catch (Exception ex)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.DownloadSizing;
                        failures.Add($"Could not estimate download size for '{requests[i].DisplayKey}': {ex.Message}");
                        continue;
                    }

                    if (!sizeHandle.IsValid())
                    {
                        LastFailureStage = CriticalPreloadFailureStage.DownloadSizing;
                        failures.Add($"Could not estimate download size for '{requests[i].DisplayKey}' because Addressables returned an invalid handle.");
                        continue;
                    }

                    yield return sizeHandle;
                    if (!sizeHandle.IsValid())
                    {
                        LastFailureStage = CriticalPreloadFailureStage.DownloadSizing;
                        failures.Add($"Could not estimate download size for '{requests[i].DisplayKey}' because the Addressables handle became invalid.");
                        continue;
                    }

                    if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.DownloadSizing;
                        failures.Add(BuildHandleFailureMessage(
                            $"Could not estimate download size for '{requests[i].DisplayKey}'.",
                            sizeHandle));
                    }
                    else
                    {
                        requests[i].DownloadSizeBytes = Math.Max(0L, sizeHandle.Result);
                        totalDownloadBytes += requests[i].DownloadSizeBytes;
                    }
                    if (sizeHandle.IsValid())
                        Addressables.Release(sizeHandle);
                }

                if (failures.Count > 0)
                {
                    if (LastFailureStage == CriticalPreloadFailureStage.None)
                        LastFailureStage = CriticalPreloadFailureStage.DownloadSizing;
                    LastError = string.Join("\n", failures);
                    ReportProgress(1f, "Required content could not be prepared.", onProgress);
                    yield break;
                }

                long downloadedBytes = 0L;
                for (int i = 0; i < requests.Count; i++)
                {
                    var request = requests[i];
                    string label = $"Downloading {request.Kind} {i + 1}/{requests.Count}: {request.DisplayKey}";
                    float baseProgress = totalDownloadBytes > 0L
                        ? Mathf.Clamp01((float)downloadedBytes / totalDownloadBytes)
                        : Mathf.Clamp01((float)i / requests.Count);

                    ReportProgress(Mathf.Max(0.2f, Mathf.Lerp(0.2f, 0.95f, baseProgress)), label, onProgress);

                    if (request.DownloadSizeBytes <= 0L)
                    {
                        downloadedBytes += request.DownloadSizeBytes;
                        _preloadedContentKeys.Add(request.ContentKey);
                        continue;
                    }

                    AsyncOperationHandle downloadHandle;
                    try
                    {
                        downloadHandle = Addressables.DownloadDependenciesAsync(request.DownloadKey, false);
                    }
                    catch (Exception ex)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                        failures.Add($"Failed to queue required {request.Kind} content '{request.DisplayKey}': {ex.Message}");
                        continue;
                    }

                    if (!downloadHandle.IsValid())
                    {
                        LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                        failures.Add($"Failed to queue required {request.Kind} content '{request.DisplayKey}' because Addressables returned an invalid handle.");
                        continue;
                    }

                    while (downloadHandle.IsValid() && !downloadHandle.IsDone)
                    {
                        float currentPortion = Mathf.Clamp01(downloadHandle.PercentComplete);
                        float aggregate = totalDownloadBytes > 0L
                            ? (downloadedBytes + (long)(request.DownloadSizeBytes * currentPortion)) / (float)totalDownloadBytes
                            : Mathf.Clamp01((i + currentPortion) / requests.Count);
                        ReportProgress(Mathf.Lerp(0.2f, 0.95f, aggregate), label, onProgress);
                        yield return null;
                    }

                    if (!downloadHandle.IsValid())
                    {
                        LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                        failures.Add($"Failed to download required {request.Kind} content '{request.DisplayKey}' because the Addressables handle became invalid.");
                        continue;
                    }

                    if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                        failures.Add(BuildHandleFailureMessage(
                            $"Failed to download required {request.Kind} content '{request.DisplayKey}'.",
                            downloadHandle));
                    }
                    else
                    {
                        downloadedBytes += request.DownloadSizeBytes;
                        _preloadedContentKeys.Add(request.ContentKey);
                        foreach (var dependencyKey in request.DependencyKeys)
                        {
                            if (!string.IsNullOrWhiteSpace(dependencyKey))
                                _preloadedContentKeys.Add(dependencyKey);
                        }
                    }

                    if (downloadHandle.IsValid())
                        Addressables.Release(downloadHandle);
                }

                for (int i = 0; i < requests.Count; i++)
                {
                    var request = requests[i];
                    if (request.IsDependencyOnly || string.IsNullOrWhiteSpace(request.AssetKey))
                        continue;

                    if (_loadedPrefabsByContentKey.ContainsKey(request.ContentKey))
                        continue;

                    string label = $"Loading prefab {i + 1}/{requests.Count}: {request.DisplayKey}";
                    AsyncOperationHandle<GameObject> loadHandle;
                    try
                    {
                        loadHandle = Addressables.LoadAssetAsync<GameObject>(request.AssetKey);
                    }
                    catch (Exception ex)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                        failures.Add($"Failed to start prefab load for '{request.DisplayKey}': {ex.Message}");
                        continue;
                    }

                    if (!loadHandle.IsValid())
                    {
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                        failures.Add($"Failed to start prefab load for '{request.DisplayKey}' because Addressables returned an invalid handle.");
                        continue;
                    }

                    while (loadHandle.IsValid() && !loadHandle.IsDone)
                    {
                        ReportProgress(0.95f, label, onProgress);
                        yield return null;
                    }

                    if (!loadHandle.IsValid())
                    {
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                        failures.Add($"Failed to load required prefab '{request.DisplayKey}' because the Addressables handle became invalid.");
                        continue;
                    }

                    if (loadHandle.Status != AsyncOperationStatus.Succeeded)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                        failures.Add(BuildHandleFailureMessage(
                            $"Failed to load required prefab '{request.DisplayKey}'.",
                            loadHandle));
                        if (loadHandle.IsValid())
                            Addressables.Release(loadHandle);
                        continue;
                    }

                    GameObject prefab = loadHandle.Result;
                    if (prefab == null)
                    {
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                        failures.Add($"Remote prefab '{request.DisplayKey}' was not a GameObject.");
                        if (loadHandle.IsValid())
                            Addressables.Release(loadHandle);
                        continue;
                    }

                    _loadedPrefabsByContentKey[request.ContentKey] = prefab;
                    _assetHandlesByContentKey[request.ContentKey] = loadHandle;
                }

                if (failures.Count > 0)
                {
                    if (LastFailureStage == CriticalPreloadFailureStage.None)
                        LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    LastError = string.Join("\n", failures);
                    ReportProgress(1f, "Required content is missing.", onProgress);
                    yield break;
                }

                if (phase == ManifestPreloadPhase.Loadout) HasCompletedLoadoutPreload = true;
                else HasCompletedWavePreload = true;
                ReportProgress(1f, $"Prepared {_preloadedContentKeys.Count} content packs.", onProgress);
            }
            finally
            {
                if (phase == ManifestPreloadPhase.Loadout) _loadoutPreloadRunning = false;
                else _wavePreloadRunning = false;
            }
        }

        public IEnumerator EnsureManifestForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false, string requester = null)
        {
            RemoteContentVerification.RecordAwaitOnly(requester ?? "unknown", "manifest");
            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;

            if (!forceRefreshManifest && Manifest != null)
            {
                ReportProgress(1f, "Content manifest ready.", onProgress);
                yield break;
            }

            yield return FetchManifest(onProgress);
            if (Manifest == null && string.IsNullOrWhiteSpace(LastError))
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestDownload;
                LastError = "Could not download the content manifest.";
            }
        }

        public IEnumerator EnsureAddressablesReady(Action<float, string> onProgress = null, string requester = null)
        {
            RemoteContentVerification.RecordAwaitOnly(requester ?? "unknown", "addressables");
            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;

            yield return InitializeAddressables(onProgress);
            if (!_addressablesReady && string.IsNullOrWhiteSpace(LastError))
            {
                LastFailureStage = CriticalPreloadFailureStage.AddressablesInitialization;
                LastError = "Addressables failed to initialize.";
            }
        }

        public string BuildCriticalContentRequirementMessage(int previewCount = 5)
        {
            var critical = GetWaveCriticalContentEntries();
            if (critical.Length == 0)
                return "No wave-critical packs are needed before gameplay begins.";

            int unitCount = 0;
            int skinCount = 0;
            var preview = new List<string>();
            for (int i = 0; i < critical.Length; i++)
            {
                var entry = critical[i];
                if (entry == null) continue;

                if (string.Equals(entry.kind, "skin", StringComparison.OrdinalIgnoreCase))
                    skinCount++;
                else
                    unitCount++;

                if (preview.Count < previewCount && !string.IsNullOrWhiteSpace(entry.key))
                    preview.Add(entry.key.Trim());
            }

            string summary = $"Required before gameplay: {critical.Length} wave-critical pack";
            if (critical.Length != 1) summary += "s";
            summary += $" ({unitCount} unit";
            if (unitCount != 1) summary += "s";
            if (skinCount > 0)
            {
                summary += $", {skinCount} skin";
                if (skinCount != 1) summary += "s";
            }
            summary += ").";

            if (preview.Count > 0)
                summary += $" Required content includes: {string.Join(", ", preview)}.";

            summary += " This gameplay download is required to render and run wave spawning safely.";
            return summary;
        }

        public bool IsContentKeyPreloaded(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey)) return false;
            return _preloadedContentKeys.Contains(contentKey.Trim());
        }

        public bool ManifestContainsUnit(string unitKey)
        {
            if (string.IsNullOrWhiteSpace(unitKey) || Manifest?.units == null)
                return false;

            string normalizedKey = unitKey.Trim();
            foreach (var unit in Manifest.units)
            {
                if (unit != null && string.Equals(unit.key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public bool ManifestContainsSkin(string skinKey)
        {
            if (string.IsNullOrWhiteSpace(skinKey) || Manifest?.skins == null)
                return false;

            string normalizedKey = skinKey.Trim();
            foreach (var skin in Manifest.skins)
            {
                if (skin != null && string.Equals(skin.skin_key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        public bool TryGetLoadedPrefabForUnit(string unitKey, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrWhiteSpace(unitKey) || Manifest?.units == null) return false;

            foreach (var unit in Manifest.units)
            {
                if (unit == null || !string.Equals(unit.key, unitKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                return TryGetLoadedPrefabForRemote(unit.remote_content, out prefab);
            }

            return false;
        }

        public bool TryGetLoadedPrefabForSkin(string skinKey, out GameObject prefab)
        {
            prefab = null;
            if (string.IsNullOrWhiteSpace(skinKey) || Manifest == null) return false;

            foreach (var skin in Manifest.skins ?? Array.Empty<ContentManifestSkinEntry>())
            {
                if (skin == null || !string.Equals(skin.skin_key, skinKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                return TryGetLoadedPrefabForRemote(skin.remote_content, out prefab);
            }

            foreach (var unit in Manifest.units ?? Array.Empty<ContentManifestEntry>())
            {
                if (unit == null || !string.Equals(unit.key, skinKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!LooksLikeLoadoutSkin(unit))
                    continue;

                return TryGetLoadedPrefabForRemote(unit.remote_content, out prefab);
            }

            return false;
        }

        public bool TryGetLoadedPortraitTexture(string unitKey, out Texture2D texture)
        {
            texture = null;
            if (!TryResolvePortraitKey(unitKey, out string portraitKey, out _))
                return false;

            return _loadedPortraitsByKey.TryGetValue(portraitKey, out texture) && texture != null;
        }

        public string ResolvePortraitLookupKey(string unitKey)
        {
            return TryResolvePortraitKey(unitKey, out string portraitKey, out _)
                ? portraitKey
                : unitKey?.Trim();
        }

        public bool ArePortraitsReady(IEnumerable<string> unitKeys)
        {
            foreach (string key in NormalizeUniqueKeys(unitKeys))
            {
                if (!TryGetLoadedPortraitTexture(key, out _))
                    return false;
            }

            return true;
        }

        public bool TryGetLoadedEnvironmentPrefab(string address, out GameObject prefab)
        {
            prefab = null;
            string normalizedAddress = NormalizeAddress(address, ResolveEnvironmentAddressFromManifest());
            return _loadedEnvironmentPrefabsByAddress.TryGetValue(normalizedAddress, out prefab) && prefab != null;
        }

        public bool AreEnvironmentAssetsReady(string address = null)
        {
            return TryGetLoadedEnvironmentPrefab(address, out _);
        }

        public bool ValidateEnvironmentContentHash(string address, string expectedContentHash, out string error)
        {
            error = null;

            string normalizedExpectedHash = expectedContentHash?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedExpectedHash))
                return true;

            if (Manifest == null)
            {
                error =
                    $"Content manifest is unavailable while validating environment '{NormalizeAddress(address, GameMlEnvironmentAddress)}' " +
                    $"against authoritative layout hash '{normalizedExpectedHash}'.";
                return false;
            }

            if (!TryGetEnvironmentManifestEntry(address, out var entry, out string normalizedAddress))
            {
                error =
                    $"Content manifest is missing an environment entry for address '{normalizedAddress}'. " +
                    "Runtime will not assume the loaded environment matches the authoritative battlefield layout.";
                return false;
            }

            string manifestHash = entry?.content_hash?.Trim();
            if (string.IsNullOrWhiteSpace(manifestHash))
            {
                error =
                    $"Content manifest entry for environment '{normalizedAddress}' is missing content_hash. " +
                    $"Expected authoritative layout hash '{normalizedExpectedHash}'.";
                return false;
            }

            if (!string.Equals(manifestHash, normalizedExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                error =
                    $"Environment '{normalizedAddress}' content hash mismatch. " +
                    $"Manifest='{manifestHash}', authoritative layout='{normalizedExpectedHash}'.";
                return false;
            }

            return true;
        }

        public IEnumerator EnsureEnvironmentReady(
            string address = null,
            string expectedContentHash = null,
            Action<float, string> onProgress = null,
            string requester = null,
            bool suppressCatalogWarnings = false)
        {
            string normalizedAddress = NormalizeAddress(address, GameMlEnvironmentAddress);
            bool waitedOnExistingWork = _environmentAddressesInFlight.Contains(normalizedAddress);

            RemoteContentVerification.RecordOwnerRequest("t1.environment", requester, waitedOnExistingWork);
            RemoteContentVerification.RecordEvent(
                "environment_request",
                $"requester={requester ?? "unknown"} address={normalizedAddress}");

            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;

            if (Manifest == null)
            {
                yield return EnsureManifestForSession(requester: requester);
                if (Manifest == null)
                    yield break;
            }

            if (!ValidateEnvironmentContentHash(normalizedAddress, expectedContentHash, out string manifestValidationError))
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestValidation;
                LastError = manifestValidationError;
                ReportProgress(1f, "Environment manifest validation failed.", onProgress);
                yield break;
            }

            if (TryGetLoadedEnvironmentPrefab(normalizedAddress, out _))
            {
                RemoteContentVerification.RecordReuse("t1.environment", $"address={normalizedAddress} source=cache");
                ReportProgress(1f, "Environment ready.", onProgress);
                yield break;
            }

            yield return InitializeAddressables((progress, status) =>
            {
                ReportProgress(Mathf.Lerp(0f, 0.15f, Mathf.Clamp01(progress)), status, onProgress);
            });

            if (!_addressablesReady)
            {
                if (string.IsNullOrWhiteSpace(LastError))
                    LastError = "Addressables failed to initialize for environment loading.";
                yield break;
            }

            bool waitedOnInFlight = false;
            while (_environmentAddressesInFlight.Contains(normalizedAddress))
            {
                waitedOnInFlight = true;
                if (TryGetLoadedEnvironmentPrefab(normalizedAddress, out _))
                    break;
                yield return null;
            }

            if (TryGetLoadedEnvironmentPrefab(normalizedAddress, out _))
            {
                RemoteContentVerification.RecordReuse(
                    "t1.environment",
                    waitedOnInFlight
                        ? $"address={normalizedAddress} source=inflight_wait"
                        : $"address={normalizedAddress} source=cache_post_wait");
                ReportProgress(1f, "Environment ready.", onProgress);
                yield break;
            }

            _environmentAddressesInFlight.Add(normalizedAddress);
            try
            {
                yield return ValidateEnvironmentAddress(normalizedAddress, onProgress, suppressCatalogWarnings);
                if (!string.IsNullOrWhiteSpace(LastError))
                    yield break;

                if (RemoteContentVerification.ConsumeFailure(
                        RemoteContentVerification.FaultKind.EnvironmentDownload,
                        $"EnsureEnvironmentReady:{normalizedAddress}",
                        out string forcedEnvironmentFailure))
                {
                    LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                    LastError = forcedEnvironmentFailure;
                    ReportProgress(1f, "Environment download failed.", onProgress);
                    yield break;
                }

                string label = $"Preparing environment: {normalizedAddress}";
                AsyncOperationHandle downloadHandle;
                try
                {
                    downloadHandle = Addressables.DownloadDependenciesAsync(normalizedAddress, false);
                }
                catch (Exception ex)
                {
                    LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                    LastError = $"Failed to queue remote environment '{normalizedAddress}': {ex.Message}";
                    ReportProgress(1f, "Environment download failed.", onProgress);
                    yield break;
                }

                if (!downloadHandle.IsValid())
                {
                    LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                    LastError = $"Failed to queue remote environment '{normalizedAddress}' because Addressables returned an invalid handle.";
                    ReportProgress(1f, "Environment download failed.", onProgress);
                    yield break;
                }

                while (downloadHandle.IsValid() && !downloadHandle.IsDone)
                {
                    ReportProgress(Mathf.Lerp(0.15f, 0.85f, Mathf.Clamp01(downloadHandle.PercentComplete)), label, onProgress);
                    yield return null;
                }

                if (!downloadHandle.IsValid())
                {
                    LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                    LastError = $"Failed to download remote environment '{normalizedAddress}' because the Addressables handle became invalid.";
                    ReportProgress(1f, "Environment download failed.", onProgress);
                    yield break;
                }

                if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                    LastError = BuildHandleFailureMessage(
                        $"Failed to download remote environment '{normalizedAddress}'.",
                        downloadHandle);
                    if (downloadHandle.IsValid())
                        Addressables.Release(downloadHandle);
                    ReportProgress(1f, "Environment download failed.", onProgress);
                    yield break;
                }

                if (downloadHandle.IsValid())
                    Addressables.Release(downloadHandle);

                AsyncOperationHandle<GameObject> loadHandle;
                try
                {
                    loadHandle = Addressables.LoadAssetAsync<GameObject>(normalizedAddress);
                }
                catch (Exception ex)
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    LastError = $"Failed to start environment asset load for '{normalizedAddress}': {ex.Message}";
                    ReportProgress(1f, "Environment load failed.", onProgress);
                    yield break;
                }

                if (!loadHandle.IsValid())
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    LastError = $"Failed to start environment asset load for '{normalizedAddress}' because Addressables returned an invalid handle.";
                    ReportProgress(1f, "Environment load failed.", onProgress);
                    yield break;
                }

                while (loadHandle.IsValid() && !loadHandle.IsDone)
                {
                    ReportProgress(Mathf.Lerp(0.85f, 0.98f, Mathf.Clamp01(loadHandle.PercentComplete)), label, onProgress);
                    yield return null;
                }

                if (!loadHandle.IsValid())
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    LastError = $"Failed to load remote environment '{normalizedAddress}' because the Addressables handle became invalid.";
                    ReportProgress(1f, "Environment load failed.", onProgress);
                    yield break;
                }

                if (loadHandle.Status != AsyncOperationStatus.Succeeded || loadHandle.Result == null)
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    LastError = BuildHandleFailureMessage(
                        $"Failed to load remote environment '{normalizedAddress}'.",
                        loadHandle);
                    if (loadHandle.IsValid())
                        Addressables.Release(loadHandle);
                    ReportProgress(1f, "Environment load failed.", onProgress);
                    yield break;
                }

                if (_environmentHandlesByAddress.TryGetValue(normalizedAddress, out var existingHandle) && existingHandle.IsValid())
                    Addressables.Release(existingHandle);

                _environmentHandlesByAddress[normalizedAddress] = loadHandle;
                _loadedEnvironmentPrefabsByAddress[normalizedAddress] = loadHandle.Result;
            }
            finally
            {
                _environmentAddressesInFlight.Remove(normalizedAddress);
            }

            ReportProgress(1f, "Environment ready.", onProgress);
        }

        public IEnumerator EnsurePortraitsReady(IEnumerable<string> unitKeys, Action<float, string> onProgress = null, string requester = null)
        {
            var portraitRequests = BuildPortraitRequests(unitKeys);
            void RecordPortraitFailure(CriticalPreloadFailureStage stage, string error)
            {
                LastFailureStage = stage;
                LastError = error;
            }

            bool waitedOnExistingWork = false;
            for (int i = 0; i < portraitRequests.Count; i++)
            {
                if (_portraitKeysInFlight.Contains(portraitRequests[i].PortraitKey))
                {
                    waitedOnExistingWork = true;
                    break;
                }
            }

            RemoteContentVerification.RecordOwnerRequest("t1.portraits", requester, waitedOnExistingWork);
            string portraitSummary = DescribePortraitRequests(portraitRequests);
            Debug.Log($"[RemoteContent] Portrait gate requested by '{requester ?? "unknown"}' for keys: {portraitSummary}");
            RemoteContentVerification.RecordEvent(
                "portrait_request",
                $"requester={requester ?? "unknown"} keys={portraitSummary}");
            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;

            if (portraitRequests.Count == 0)
            {
                RemoteContentVerification.RecordReuse("t1.portraits", "source=empty_request");
                ReportProgress(1f, "Portraits ready.", onProgress);
                yield break;
            }

            yield return InitializeAddressables((progress, status) =>
            {
                ReportProgress(Mathf.Lerp(0f, 0.15f, Mathf.Clamp01(progress)), status, onProgress);
            });

            if (!_addressablesReady)
            {
                if (string.IsNullOrWhiteSpace(LastError))
                    LastError = "Addressables failed to initialize for portrait loading.";
                yield break;
            }

            int completed = 0;
            for (int i = 0; i < portraitRequests.Count; i++)
            {
                var request = portraitRequests[i];
                string requestedKey = request.RequestedKey;
                string key = request.PortraitKey;
                bool waitedOnInFlight = false;
                while (_portraitKeysInFlight.Contains(key))
                {
                    waitedOnInFlight = true;
                    if (TryGetLoadedPortraitTexture(key, out _))
                        break;
                    yield return null;
                }

                if (TryGetLoadedPortraitTexture(key, out _))
                {
                    RemoteContentVerification.RecordReuse(
                        "t1.portraits",
                        waitedOnInFlight
                            ? $"key={requestedKey}->{key} source=inflight_wait"
                            : $"key={requestedKey}->{key} source=cache");
                    completed++;
                    continue;
                }

                string address = $"portraits/{key}";
                string label = $"Preparing portrait {completed + 1}/{portraitRequests.Count}: {requestedKey}";
                ReportProgress(Mathf.Lerp(0.15f, 0.85f, Mathf.Clamp01((float)completed / portraitRequests.Count)), label, onProgress);
                _portraitKeysInFlight.Add(key);

                try
                {
                    string portraitValidationError = null;
                    CriticalPreloadFailureStage portraitValidationStage = CriticalPreloadFailureStage.None;
                    Debug.Log($"[RemoteContent] Portrait resolve request='{requestedKey}' resolved='{key}' address='{address}'");
                    yield return ValidatePortraitAddress(
                        address,
                        requestedKey,
                        key,
                        portraitRequests,
                        onProgress,
                        (stage, error) =>
                        {
                            portraitValidationStage = stage;
                            portraitValidationError = error;
                            RecordPortraitFailure(stage, error);
                        });
                    if (!string.IsNullOrWhiteSpace(portraitValidationError))
                    {
                        RecordPortraitFailure(portraitValidationStage, portraitValidationError);
                        yield break;
                    }

                    if (RemoteContentVerification.ConsumeFailure(
                            RemoteContentVerification.FaultKind.PortraitDownload,
                            $"EnsurePortraitsReady:{key}",
                            out string forcedPortraitFailure))
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.ContentDownload, forcedPortraitFailure);
                        ReportProgress(1f, "Portrait download failed.", onProgress);
                        yield break;
                    }

                    AsyncOperationHandle downloadHandle;
                    try
                    {
                        downloadHandle = Addressables.DownloadDependenciesAsync(address, false);
                    }
                    catch (Exception ex)
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.ContentDownload, $"Failed to queue portrait content '{key}': {ex.Message}");
                        ReportProgress(1f, "Portrait download failed.", onProgress);
                        yield break;
                    }

                    if (!downloadHandle.IsValid())
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.ContentDownload, $"Failed to queue portrait content '{key}' because Addressables returned an invalid handle.");
                        ReportProgress(1f, "Portrait download failed.", onProgress);
                        yield break;
                    }

                    while (downloadHandle.IsValid() && !downloadHandle.IsDone)
                    {
                        float aggregateProgress = (completed + Mathf.Clamp01(downloadHandle.PercentComplete)) / portraitRequests.Count;
                        ReportProgress(Mathf.Lerp(0.15f, 0.85f, aggregateProgress), label, onProgress);
                        yield return null;
                    }

                    if (!downloadHandle.IsValid())
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.ContentDownload, $"Failed to download portrait content '{key}' because the Addressables handle became invalid.");
                        ReportProgress(1f, "Portrait download failed.", onProgress);
                        yield break;
                    }

                    if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.ContentDownload, BuildHandleFailureMessage(
                            $"Failed to download portrait content '{key}'.",
                            downloadHandle));
                        if (downloadHandle.IsValid())
                            Addressables.Release(downloadHandle);
                        ReportProgress(1f, "Portrait download failed.", onProgress);
                        yield break;
                    }

                    if (downloadHandle.IsValid())
                        Addressables.Release(downloadHandle);

                    AsyncOperationHandle<Texture2D> loadHandle;
                    try
                    {
                        loadHandle = Addressables.LoadAssetAsync<Texture2D>(address);
                    }
                    catch (Exception ex)
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.AssetLoad, $"Failed to start portrait asset load for '{key}': {ex.Message}");
                        ReportProgress(1f, "Portrait load failed.", onProgress);
                        yield break;
                    }

                    if (!loadHandle.IsValid())
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.AssetLoad, $"Failed to start portrait asset load for '{key}' because Addressables returned an invalid handle.");
                        ReportProgress(1f, "Portrait load failed.", onProgress);
                        yield break;
                    }

                    while (loadHandle.IsValid() && !loadHandle.IsDone)
                    {
                        float aggregateProgress = (completed + Mathf.Clamp01(loadHandle.PercentComplete)) / portraitRequests.Count;
                        ReportProgress(Mathf.Lerp(0.85f, 0.98f, aggregateProgress), label, onProgress);
                        yield return null;
                    }

                    if (!loadHandle.IsValid())
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.AssetLoad, $"Failed to load portrait asset '{key}' because the Addressables handle became invalid.");
                        ReportProgress(1f, "Portrait load failed.", onProgress);
                        yield break;
                    }

                    if (loadHandle.Status != AsyncOperationStatus.Succeeded || loadHandle.Result == null)
                    {
                        RecordPortraitFailure(CriticalPreloadFailureStage.AssetLoad, BuildHandleFailureMessage(
                            $"Failed to load portrait asset '{key}'.",
                            loadHandle));
                        if (loadHandle.IsValid())
                            Addressables.Release(loadHandle);
                        ReportProgress(1f, "Portrait load failed.", onProgress);
                        yield break;
                    }

                    if (_portraitHandlesByKey.TryGetValue(key, out var existingHandle) && existingHandle.IsValid())
                        Addressables.Release(existingHandle);

                    _portraitHandlesByKey[key] = loadHandle;
                    _loadedPortraitsByKey[key] = loadHandle.Result;
                    Debug.Log($"[RemoteContent] Portrait loaded request='{requestedKey}' resolved='{key}' address='{address}' size={loadHandle.Result.width}x{loadHandle.Result.height}");
                }
                finally
                {
                    _portraitKeysInFlight.Remove(key);
                }

                completed++;
            }

            ReportProgress(1f, "Portraits ready.", onProgress);
        }

        IEnumerator ValidatePortraitAddress(
            string address,
            string requestedKey,
            string resolvedKey,
            List<PortraitRequest> requests,
            Action<float, string> onProgress,
            Action<CriticalPreloadFailureStage, string> onFailure)
        {
            Debug.Log(
                $"[RemoteContent] Validating portrait request='{requestedKey}' resolved='{resolvedKey}' address='{address}' " +
                $"catalogState=({DescribeActiveCatalogState()})");
            AsyncOperationHandle<IList<IResourceLocation>> locationsHandle;
            try
            {
                locationsHandle = Addressables.LoadResourceLocationsAsync(address, typeof(Texture2D));
            }
            catch (Exception ex)
            {
                onFailure?.Invoke(
                    CriticalPreloadFailureStage.ManifestValidation,
                    $"Failed to resolve portrait address '{address}' for key '{requestedKey}' (resolved '{resolvedKey}'): {ex.Message}");
                ReportProgress(1f, "Portrait catalog lookup failed.", onProgress);
                yield break;
            }

            if (!locationsHandle.IsValid())
            {
                onFailure?.Invoke(
                    CriticalPreloadFailureStage.ManifestValidation,
                    $"Addressables returned an invalid lookup handle for portrait address '{address}' (key '{requestedKey}', resolved '{resolvedKey}').");
                ReportProgress(1f, "Portrait catalog lookup failed.", onProgress);
                yield break;
            }

            while (locationsHandle.IsValid() && !locationsHandle.IsDone)
                yield return null;

            if (!locationsHandle.IsValid())
            {
                onFailure?.Invoke(
                    CriticalPreloadFailureStage.ManifestValidation,
                    $"Portrait address lookup for '{address}' became invalid before completion.");
                ReportProgress(1f, "Portrait catalog lookup failed.", onProgress);
                yield break;
            }

            bool hasLocations = locationsHandle.Status == AsyncOperationStatus.Succeeded
                && locationsHandle.Result != null
                && locationsHandle.Result.Count > 0;

            Debug.Log(
                $"[RemoteContent] Portrait catalog lookup request='{requestedKey}' resolved='{resolvedKey}' address='{address}' " +
                $"found={hasLocations} locations={(locationsHandle.Result?.Count ?? 0)} catalogState=({DescribeActiveCatalogState()})");

            if (!hasLocations)
            {
                string requestedSummary = DescribePortraitRequests(requests);
                string error =
                    $"Portrait address '{address}' for key '{requestedKey}' (resolved '{resolvedKey}') is missing from the active Addressables catalog. " +
                    $"Requested portrait mappings this pass: {requestedSummary}. " +
                    $"Active catalog state: {DescribeActiveCatalogState()}. " +
                    "Rebuild Addressables, clear the catalog/cache, and confirm the active player is loading the latest remote catalog.";
                onFailure?.Invoke(CriticalPreloadFailureStage.ManifestValidation, error);
                RemoteContentVerification.RecordEvent(
                    "portrait_catalog_miss",
                    $"address={address} key={requestedKey} resolved={resolvedKey} requested={requestedSummary}");
                Debug.LogWarning($"[RemoteContent] {error}");
                if (locationsHandle.IsValid())
                    Addressables.Release(locationsHandle);
                ReportProgress(1f, "Portrait catalog lookup failed.", onProgress);
                yield break;
            }

            if (locationsHandle.IsValid())
                Addressables.Release(locationsHandle);
        }

        IEnumerator ValidateEnvironmentAddress(string address, Action<float, string> onProgress, bool suppressCatalogWarnings)
        {
            AsyncOperationHandle<IList<IResourceLocation>> locationsHandle;
            try
            {
                locationsHandle = Addressables.LoadResourceLocationsAsync(address, typeof(GameObject));
            }
            catch (Exception ex)
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestValidation;
                LastError = $"Failed to resolve environment address '{address}': {ex.Message}";
                ReportProgress(1f, "Environment catalog lookup failed.", onProgress);
                yield break;
            }

            if (!locationsHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestValidation;
                LastError = $"Addressables returned an invalid lookup handle for environment address '{address}'.";
                ReportProgress(1f, "Environment catalog lookup failed.", onProgress);
                yield break;
            }

            while (locationsHandle.IsValid() && !locationsHandle.IsDone)
                yield return null;

            if (!locationsHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestValidation;
                LastError = $"Environment address lookup for '{address}' became invalid before completion.";
                ReportProgress(1f, "Environment catalog lookup failed.", onProgress);
                yield break;
            }

            bool hasLocations = locationsHandle.Status == AsyncOperationStatus.Succeeded
                && locationsHandle.Result != null
                && locationsHandle.Result.Count > 0;

            if (!hasLocations)
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestValidation;
                LastError =
                    $"Environment address '{address}' is missing from the active Addressables catalog. " +
                    "Rebuild Addressables, clear the catalog/cache, and confirm the active player is loading the latest remote catalog.";
                RemoteContentVerification.RecordEvent(
                    "environment_catalog_miss",
                    $"address={address}");
                if (!suppressCatalogWarnings)
                    Debug.LogWarning($"[RemoteContent] {LastError}");
                if (locationsHandle.IsValid())
                    Addressables.Release(locationsHandle);
                ReportProgress(1f, "Environment catalog lookup failed.", onProgress);
                yield break;
            }

            if (locationsHandle.IsValid())
                Addressables.Release(locationsHandle);
        }

        public IEnumerator EnsureUnitPrefabLoaded(string unitKey, string requester = null)
        {
            RemoteContentVerification.RecordAwaitOnly(requester ?? "unknown", $"unit_prefab:{unitKey}");
            if (string.IsNullOrWhiteSpace(unitKey))
                yield break;

            string normalizedKey = unitKey.Trim();
            if (TryGetLoadedPrefabForUnit(normalizedKey, out _))
                yield break;

            if (Manifest == null)
            {
                yield return EnsureManifestForSession(requester: requester);
                if (Manifest == null)
                    yield break;
            }

            yield return InitializeAddressables(null);
            if (!_addressablesReady)
                yield break;

            ContentManifestEntry manifestUnit = null;
            var units = Manifest.units ?? Array.Empty<ContentManifestEntry>();
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                if (unit == null) continue;
                if (string.Equals(unit.key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                {
                    manifestUnit = unit;
                    break;
                }
            }

            if (manifestUnit?.remote_content == null || !manifestUnit.remote_content.enabled)
                yield break;

            string contentKey = manifestUnit.remote_content.content_key?.Trim();
            if (string.IsNullOrWhiteSpace(contentKey))
                yield break;

            if (TryGetLoadedPrefabForRemote(manifestUnit.remote_content, out _))
                yield break;

            object downloadKey = !string.IsNullOrWhiteSpace(manifestUnit.remote_content.addressables_label)
                ? manifestUnit.remote_content.addressables_label.Trim()
                : contentKey;

            AsyncOperationHandle downloadHandle;
            try
            {
                downloadHandle = Addressables.DownloadDependenciesAsync(downloadKey, false);
            }
            catch (Exception ex)
            {
                LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                LastError = $"Failed to queue unit content '{normalizedKey}': {ex.Message}";
                yield break;
            }

            if (!downloadHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                LastError = $"Failed to queue unit content '{normalizedKey}' because Addressables returned an invalid handle.";
                yield break;
            }

            while (downloadHandle.IsValid() && !downloadHandle.IsDone)
                yield return null;

            if (!downloadHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                LastError = $"Failed to download unit content '{normalizedKey}' because the Addressables handle became invalid.";
                yield break;
            }

            if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
            {
                LastFailureStage = CriticalPreloadFailureStage.ContentDownload;
                LastError = BuildHandleFailureMessage(
                    $"Failed to download unit content '{normalizedKey}'.",
                    downloadHandle);
                Addressables.Release(downloadHandle);
                yield break;
            }

            Addressables.Release(downloadHandle);
            _preloadedContentKeys.Add(contentKey);
            foreach (var dependencyKey in manifestUnit.remote_content.dependency_keys ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(dependencyKey))
                    _preloadedContentKeys.Add(dependencyKey.Trim());
            }

            string assetKey = ResolveAssetKey(
                new CriticalContentEntry { kind = "unit", key = normalizedKey, content_key = contentKey },
                manifestUnit.remote_content,
                contentKey);

            if (string.IsNullOrWhiteSpace(assetKey))
                yield break;

            AsyncOperationHandle<GameObject> loadHandle;
            try
            {
                loadHandle = Addressables.LoadAssetAsync<GameObject>(assetKey);
            }
            catch (Exception ex)
            {
                LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                LastError = $"Failed to start unit prefab load for '{normalizedKey}': {ex.Message}";
                yield break;
            }

            if (!loadHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                LastError = $"Failed to start unit prefab load for '{normalizedKey}' because Addressables returned an invalid handle.";
                yield break;
            }

            while (loadHandle.IsValid() && !loadHandle.IsDone)
                yield return null;

            if (!loadHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                LastError = $"Failed to load unit prefab '{normalizedKey}' because the Addressables handle became invalid.";
                yield break;
            }

            if (loadHandle.Status != AsyncOperationStatus.Succeeded || loadHandle.Result == null)
            {
                LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                LastError = BuildHandleFailureMessage(
                    $"Failed to load unit prefab '{normalizedKey}'.",
                    loadHandle);
                if (loadHandle.IsValid())
                    Addressables.Release(loadHandle);
                yield break;
            }

            if (_assetHandlesByContentKey.TryGetValue(contentKey, out var existingHandle) && existingHandle.IsValid())
                Addressables.Release(existingHandle);

            _assetHandlesByContentKey[contentKey] = loadHandle;
            _loadedPrefabsByContentKey[contentKey] = loadHandle.Result;
        }

        IEnumerator FetchManifest(Action<float, string> onProgress)
        {
            ReportProgress(0.05f, "Downloading content manifest...", onProgress);

            if (RemoteContentVerification.ConsumeFailure(
                    RemoteContentVerification.FaultKind.ManifestDownload,
                    "FetchManifest",
                    out string forcedManifestFailure))
            {
                Manifest = null;
                LastFailureStage = CriticalPreloadFailureStage.ManifestDownload;
                LastError = forcedManifestFailure;
                Debug.LogWarning($"[RemoteContent] {LastError}");
                yield break;
            }

            using var req = UnityWebRequest.Get(BaseUrl.TrimEnd('/') + "/api/content-manifest");
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestDownload;
                LastError =
                    $"Could not download the live content manifest: {req.error}. " +
                    "Runtime will not reuse cached manifest data.";
                Debug.LogError($"[RemoteContent] Manifest fetch failed: {req.error}");
                yield break;
            }

            try
            {
                if (!TryApplyManifestJson(req.downloadHandler.text, out string error))
                {
                    LastFailureStage = CriticalPreloadFailureStage.ManifestParse;
                    LastError = error;
                    yield break;
                }

                Debug.Log(
                    $"[RemoteContent] Manifest ready — {(Manifest.units?.Length ?? 0)} units, {(Manifest.skins?.Length ?? 0)} skins, " +
                    $"{(Manifest.t0_content?.Length ?? 0)} T0 entries, {(Manifest.t1_content?.Length ?? 0)} T1 entries, " +
                    $"{(Manifest.loadout_critical_content?.Length ?? 0)} loadout-critical entries, " +
                    $"{(Manifest.wave_critical_content?.Length ?? 0)} wave-critical entries, " +
                    $"{(Manifest.critical_content?.Length ?? 0)} legacy critical entries");
                ReportProgress(0.2f, "Content manifest ready.", onProgress);
            }
            catch (Exception ex)
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestParse;
                LastError = $"Could not parse content manifest: {ex.Message}";
                Debug.LogWarning($"[RemoteContent] Manifest parse failed: {ex.Message}");
            }
        }

        IEnumerator InitializeAddressables(Action<float, string> onProgress)
        {
            if (_addressablesReady) yield break;

            ReportProgress(0.1f, "Initializing remote content system...", onProgress);
            if (RemoteContentVerification.ConsumeFailure(
                    RemoteContentVerification.FaultKind.AddressablesInitialization,
                    "InitializeAddressables",
                    out string forcedAddressablesFailure))
            {
                LastFailureStage = CriticalPreloadFailureStage.AddressablesInitialization;
                LastError = forcedAddressablesFailure;
                Debug.LogWarning($"[RemoteContent] {LastError}");
                yield break;
            }

            ConfigureAddressablesRuntimeProperties();
            AsyncOperationHandle initHandle;
            try
            {
                initHandle = Addressables.InitializeAsync(false);
            }
            catch (Exception ex)
            {
                LastFailureStage = CriticalPreloadFailureStage.AddressablesInitialization;
                LastError = $"Addressables initialization threw before creating a handle: {ex.Message}";
                Debug.LogWarning($"[RemoteContent] {LastError}");
                yield break;
            }

            if (!initHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.AddressablesInitialization;
                LastError = "Addressables initialization returned an invalid handle.";
                Debug.LogWarning($"[RemoteContent] {LastError}");
                yield break;
            }

            yield return initHandle;
            if (!initHandle.IsValid())
            {
                LastFailureStage = CriticalPreloadFailureStage.AddressablesInitialization;
                LastError = "Addressables initialization handle became invalid before completion.";
                Debug.LogWarning($"[RemoteContent] {LastError}");
                yield break;
            }

            if (initHandle.Status != AsyncOperationStatus.Succeeded)
            {
                LastFailureStage = CriticalPreloadFailureStage.AddressablesInitialization;
                LastError = BuildHandleFailureMessage("Addressables initialization failed.", initHandle);
                Debug.LogWarning($"[RemoteContent] {LastError}");
                if (initHandle.IsValid())
                    Addressables.Release(initHandle);
                yield break;
            }

            _addressablesReady = true;
            _initializationHandle = initHandle;
            _hasInitializationHandle = initHandle.IsValid();
            Debug.Log($"[RemoteContent] Addressables initialized. {DescribeActiveCatalogState()}");
        }

        void ConfigureAddressablesRuntimeProperties()
        {
            string remoteLoadPath = RemoteAddressablesRuntimePath.RemoteLoadPath;
            AddressablesRuntimeProperties.SetPropertyValue(
                "CastleDefender.Net.RemoteAddressablesRuntimePath.RemoteLoadPath",
                remoteLoadPath);
        }

        IEnumerator LoadCriticalAssetsDirectly(List<DownloadRequest> requests, List<string> failures, Action<float, string> onProgress)
        {
            int totalAssets = 0;
            for (int i = 0; i < requests.Count; i++)
            {
                if (!requests[i].IsDependencyOnly && !string.IsNullOrWhiteSpace(requests[i].AssetKey))
                    totalAssets++;
            }

            if (totalAssets == 0)
                yield break;

            int loadedAssets = 0;
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                if (request.IsDependencyOnly || string.IsNullOrWhiteSpace(request.AssetKey))
                    continue;

                if (_loadedPrefabsByContentKey.ContainsKey(request.ContentKey))
                {
                    MarkRequestAsPreloaded(request);
                    loadedAssets++;
                    continue;
                }

                string label = $"Loading required {request.Kind} {loadedAssets + 1}/{totalAssets}: {request.DisplayKey}";
                AsyncOperationHandle<GameObject> loadHandle;
                try
                {
                    loadHandle = Addressables.LoadAssetAsync<GameObject>(request.AssetKey);
                }
                catch (Exception ex)
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    failures.Add($"Failed to start prefab load for '{request.DisplayKey}': {ex.Message}");
                    continue;
                }

                if (!loadHandle.IsValid())
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    failures.Add($"Failed to start prefab load for '{request.DisplayKey}' because Addressables returned an invalid handle.");
                    continue;
                }

                while (loadHandle.IsValid() && !loadHandle.IsDone)
                {
                    // WebGL Addressables percent-complete often jumps to near-finished long
                    // before the browser has actually resolved the download. Use completed
                    // asset count for steadier, more honest progress.
                    float progress = Mathf.Clamp01((float)loadedAssets / totalAssets);
                    ReportProgress(Mathf.Lerp(0.2f, 0.95f, progress), label, onProgress);
                    yield return null;
                }

                if (!loadHandle.IsValid())
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    failures.Add($"Failed to load required prefab '{request.DisplayKey}' because the Addressables handle became invalid.");
                    continue;
                }

                if (loadHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    failures.Add(BuildHandleFailureMessage(
                        $"Failed to load required prefab '{request.DisplayKey}'.",
                        loadHandle));
                    if (loadHandle.IsValid())
                        Addressables.Release(loadHandle);
                    continue;
                }

                GameObject prefab = loadHandle.Result;
                if (prefab == null)
                {
                    LastFailureStage = CriticalPreloadFailureStage.AssetLoad;
                    failures.Add($"Remote prefab '{request.DisplayKey}' was not a GameObject.");
                    if (loadHandle.IsValid())
                        Addressables.Release(loadHandle);
                    continue;
                }

                _loadedPrefabsByContentKey[request.ContentKey] = prefab;
                _assetHandlesByContentKey[request.ContentKey] = loadHandle;
                MarkRequestAsPreloaded(request);
                loadedAssets++;
            }
        }

        void MarkRequestAsPreloaded(DownloadRequest request)
        {
            if (request == null)
                return;

            if (!string.IsNullOrWhiteSpace(request.ContentKey))
                _preloadedContentKeys.Add(request.ContentKey);

            foreach (var dependencyKey in request.DependencyKeys ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(dependencyKey))
                    _preloadedContentKeys.Add(dependencyKey);
            }
        }

        bool ValidateManifestEntry(CriticalContentEntry critical, out string error)
        {
            error = null;
            if (critical == null)
            {
                error = "Encountered an empty critical content entry.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(critical.content_key))
            {
                error = $"Critical {critical.kind ?? "content"} '{critical.key ?? "unknown"}' is missing a content key.";
                return false;
            }

            RemoteContentEntry remote = ResolveRemoteContent(critical);
            if (remote == null)
            {
                error = $"Critical {critical.kind ?? "content"} '{critical.key ?? critical.content_key}' is missing remote metadata.";
                return false;
            }

            if (!remote.enabled)
            {
                error = $"Critical {critical.kind ?? "content"} '{critical.key ?? critical.content_key}' is disabled in remote metadata.";
                return false;
            }

            return true;
        }

        RemoteContentEntry ResolveRemoteContent(CriticalContentEntry critical)
        {
            if (Manifest == null || critical == null) return null;

            if (string.Equals(critical.kind, "environment", StringComparison.OrdinalIgnoreCase))
            {
                string address = critical.address?.Trim();
                return new RemoteContentEntry
                {
                    content_key = critical.content_key?.Trim(),
                    content_hash = critical.content_hash?.Trim(),
                    addressables_label = address,
                    prefab_address = address,
                    dependency_keys = Array.Empty<string>(),
                    enabled = true,
                    tier = critical.tier?.Trim(),
                    preload_reason = critical.reason?.Trim(),
                };
            }

            if (string.Equals(critical.kind, "skin", StringComparison.OrdinalIgnoreCase))
            {
                var skins = Manifest.skins ?? Array.Empty<ContentManifestSkinEntry>();
                for (int i = 0; i < skins.Length; i++)
                {
                    var skin = skins[i];
                    if (skin == null) continue;
                    if (string.Equals(skin.skin_key, critical.key, StringComparison.OrdinalIgnoreCase))
                        return skin.remote_content;
                }
                return null;
            }

            var units = Manifest.units ?? Array.Empty<ContentManifestEntry>();
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                if (unit == null) continue;
                if (string.Equals(unit.key, critical.key, StringComparison.OrdinalIgnoreCase))
                    return unit.remote_content;
            }
            return null;
        }

        List<DownloadRequest> BuildDownloadRequests(CriticalContentEntry[] criticalEntries, List<string> failures)
        {
            var requests = new List<DownloadRequest>();
            var seenContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < criticalEntries.Length; i++)
            {
                var critical = criticalEntries[i];
                if (!ValidateManifestEntry(critical, out string validationError))
                {
                    failures.Add(validationError);
                    continue;
                }

                var remote = ResolveRemoteContent(critical);
                if (ShouldSkipRemotePreload(critical, remote))
                    continue;

                string contentKey = remote.content_key?.Trim();
                if (string.IsNullOrWhiteSpace(contentKey))
                {
                    failures.Add($"Critical {critical.kind ?? "content"} '{critical.key ?? "unknown"}' is missing a content key.");
                    continue;
                }

                if (!seenContent.Add(contentKey))
                    continue;

                object downloadKey = !string.IsNullOrWhiteSpace(remote.addressables_label)
                    ? remote.addressables_label.Trim()
                    : contentKey;

                requests.Add(new DownloadRequest
                {
                    Kind = string.IsNullOrWhiteSpace(critical.kind) ? "content" : critical.kind,
                    DisplayKey = !string.IsNullOrWhiteSpace(critical.key) ? critical.key : contentKey,
                    ContentKey = contentKey,
                    DownloadKey = downloadKey,
                    AssetKey = ResolveAssetKey(critical, remote, contentKey),
                    DependencyKeys = remote.dependency_keys ?? Array.Empty<string>(),
                    IsDependencyOnly = false,
                });

                var dependencyKeys = remote.dependency_keys ?? Array.Empty<string>();
                for (int depIndex = 0; depIndex < dependencyKeys.Length; depIndex++)
                {
                    string dependencyKey = dependencyKeys[depIndex]?.Trim();
                    if (string.IsNullOrWhiteSpace(dependencyKey) || !seenContent.Add(dependencyKey))
                        continue;

                    requests.Add(new DownloadRequest
                    {
                        Kind = "dependency",
                        DisplayKey = dependencyKey,
                        ContentKey = dependencyKey,
                        DownloadKey = dependencyKey,
                        AssetKey = null,
                        DependencyKeys = Array.Empty<string>(),
                        IsDependencyOnly = true,
                    });
                }
            }

            return requests;
        }

        static string ResolveAssetKey(CriticalContentEntry critical, RemoteContentEntry remote, string contentKey)
        {
            if (!string.IsNullOrWhiteSpace(remote?.prefab_address))
                return remote.prefab_address.Trim();

            string key = !string.IsNullOrWhiteSpace(critical?.key)
                ? critical.key.Trim()
                : contentKey;
            if (string.IsNullOrWhiteSpace(key))
                return contentKey;

            if (string.Equals(critical?.kind, "skin", StringComparison.OrdinalIgnoreCase))
                return $"skins/{key}";

            if (string.Equals(critical?.kind, "unit", StringComparison.OrdinalIgnoreCase))
                return $"units/{key}";

            return contentKey;
        }

        static bool ShouldSkipRemotePreload(CriticalContentEntry critical, RemoteContentEntry remote)
        {
            string key = critical?.key?.Trim();
            if (string.IsNullOrWhiteSpace(key))
                return false;

            if (key.EndsWith("_placeholder", StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[RemoteContent] Skipping remote preload for placeholder content '{key}'.");
                return true;
            }

            if (remote == null)
                return false;

            bool hasExplicitRemoteReference =
                !string.IsNullOrWhiteSpace(remote.addressables_label) ||
                !string.IsNullOrWhiteSpace(remote.prefab_address) ||
                !string.IsNullOrWhiteSpace(remote.catalog_url) ||
                !string.IsNullOrWhiteSpace(remote.content_url);

            if (!hasExplicitRemoteReference && !string.IsNullOrWhiteSpace(remote.placeholder_key))
            {
                Debug.Log($"[RemoteContent] Skipping placeholder-only remote metadata for '{key}'.");
                return true;
            }

            return false;
        }

        string ReadHandleOperationExceptionMessage(object handle)
        {
            if (!IsHandleValid(handle)) return null;
            object value = ReadHandleProperty(handle, "OperationException");
            return value switch
            {
                null => null,
                Exception ex => ex.Message,
                _ => value.ToString(),
            };
        }

        object ReadHandleProperty(object handle, string propertyName)
        {
            if (handle == null) return null;
            if (!IsHandleValid(handle)) return null;

            try
            {
                var prop = handle.GetType().GetProperty(propertyName);
                return prop?.GetValue(handle);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteContent] Failed to read Addressables handle property '{propertyName}': {ex.Message}");
                return null;
            }
        }

        bool IsHandleValid(object handle)
        {
            if (handle == null) return false;

            try
            {
                var method = handle.GetType().GetMethod("IsValid", Type.EmptyTypes);
                if (method == null) return true;
                object result = method.Invoke(handle, null);
                return result is bool valid && valid;
            }
            catch
            {
                return false;
            }
        }

        float ConvertToFloat(object value)
        {
            if (value == null) return 0f;
            try
            {
                return Convert.ToSingle(value);
            }
            catch
            {
                return 0f;
            }
        }

        string BuildHandleFailureMessage(string baseMessage, object handle)
        {
            string operationError = ReadHandleOperationExceptionMessage(handle);
            string status = ReadHandleProperty(handle, "Status")?.ToString();
            bool isValid = IsHandleValid(handle);
            string suffix = string.IsNullOrWhiteSpace(operationError)
                ? $"HandleStatus={status ?? "unknown"}, HandleValid={isValid}."
                : $"{operationError} HandleStatus={status ?? "unknown"}, HandleValid={isValid}.";
            return $"{baseMessage} {suffix}";
        }

        bool TryGetLoadedPrefabForRemote(RemoteContentEntry remote, out GameObject prefab)
        {
            prefab = null;
            string contentKey = remote?.content_key;
            if (string.IsNullOrWhiteSpace(contentKey)) return false;
            return _loadedPrefabsByContentKey.TryGetValue(contentKey.Trim(), out prefab) && prefab != null;
        }

        bool TryApplyManifestJson(string json, out string error)
        {
            error = null;
            Manifest = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Content manifest response was empty.";
                return false;
            }

            try
            {
                Manifest = JsonConvert.DeserializeObject<ContentManifestResponse>(json);
                if (Manifest == null)
                {
                    error = "Content manifest response was empty.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"Could not parse content manifest: {ex.Message}";
                return false;
            }
        }


        static List<string> NormalizeUniqueKeys(IEnumerable<string> keys)
        {
            var normalized = new List<string>();
            if (keys == null)
                return normalized;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string key in keys)
            {
                string trimmed = key?.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
                    continue;

                normalized.Add(trimmed);
            }

            return normalized;
        }

        bool TryResolvePortraitKey(string unitKey, out string portraitKey, out string resolutionSource)
        {
            portraitKey = null;
            resolutionSource = null;

            string normalizedKey = unitKey?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKey))
                return false;

            var units = Manifest?.units ?? Array.Empty<ContentManifestEntry>();
            for (int i = 0; i < units.Length; i++)
            {
                var unit = units[i];
                if (unit == null || !string.Equals(unit.key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                portraitKey = normalizedKey;
                resolutionSource = "unit.key.direct";
                break;
            }

            var skins = Manifest?.skins ?? Array.Empty<ContentManifestSkinEntry>();
            for (int i = 0; i < skins.Length; i++)
            {
                var skin = skins[i];
                if (skin == null || !string.Equals(skin.skin_key, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(skin.unit_type))
                {
                    portraitKey = skin.unit_type.Trim();
                    resolutionSource = string.Equals(portraitKey, normalizedKey, StringComparison.OrdinalIgnoreCase)
                        ? "skin.direct"
                        : "skin.unit_type";
                }

                if ((string.IsNullOrWhiteSpace(portraitKey) || string.Equals(portraitKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    && TryResolveUnitTypeForSkinFromLoadedRegistry(normalizedKey, out string registryUnitType)
                    && !string.IsNullOrWhiteSpace(registryUnitType))
                {
                    portraitKey = registryUnitType.Trim();
                    resolutionSource = "skin.unit_registry";
                }

                return true;
            }

            portraitKey = normalizedKey;
            resolutionSource = "direct";

            if (IsKnownUnitKeyInLoadedRegistry(normalizedKey))
            {
                resolutionSource = "registry.unit_key.direct";
                return true;
            }

            if (TryResolveUnitTypeForSkinFromLoadedRegistry(normalizedKey, out string fallbackUnitType)
                && !string.IsNullOrWhiteSpace(fallbackUnitType))
            {
                portraitKey = fallbackUnitType.Trim();
                resolutionSource = "legacy.registry_fallback";
                return true;
            }

            return true;
        }

        static bool IsKnownUnitKeyInLoadedRegistry(string unitKey)
        {
            if (string.IsNullOrWhiteSpace(unitKey))
                return false;

            var registries = Resources.FindObjectsOfTypeAll<ScriptableObject>();
            for (int i = 0; i < registries.Length; i++)
            {
                var registry = registries[i];
                if (registry == null)
                    continue;

                Type registryType = registry.GetType();
                if (!string.Equals(registryType.FullName, "CastleDefender.Game.UnitPrefabRegistry", StringComparison.Ordinal))
                    continue;

                Type entryType = registryType.GetNestedType("Entry", BindingFlags.Public | BindingFlags.NonPublic);
                if (entryType == null)
                    continue;

                MethodInfo method = registryType.GetMethod(
                    "TryGet",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), entryType.MakeByRefType() },
                    null);

                if (method == null)
                    continue;

                object entry = Activator.CreateInstance(entryType);
                object[] args = { unitKey, entry };
                if (method.Invoke(registry, args) is bool success && success)
                    return true;
            }

            return false;
        }

        static bool TryResolveUnitTypeForSkinFromLoadedRegistry(string skinKey, out string unitType)
        {
            unitType = null;
            if (string.IsNullOrWhiteSpace(skinKey))
                return false;

            var registries = Resources.FindObjectsOfTypeAll<ScriptableObject>();
            for (int i = 0; i < registries.Length; i++)
            {
                var registry = registries[i];
                if (registry == null)
                    continue;

                Type registryType = registry.GetType();
                if (!string.Equals(registryType.FullName, "CastleDefender.Game.UnitPrefabRegistry", StringComparison.Ordinal))
                    continue;

                MethodInfo method = registryType.GetMethod(
                    "TryGetUnitTypeForSkin",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(string), typeof(string).MakeByRefType() },
                    null);

                if (method == null)
                    continue;

                object[] args = { skinKey, null };
                if (method.Invoke(registry, args) is bool success && success)
                {
                    unitType = args[1] as string;
                    if (!string.IsNullOrWhiteSpace(unitType))
                        return true;
                }
            }

            return false;
        }

        static bool LooksLikeLoadoutSkin(ContentManifestEntry unit)
        {
            if (unit == null || unit.remote_content == null)
                return false;

            if (!string.IsNullOrWhiteSpace(unit.unit_type))
                return true;

            return string.Equals(unit.content_kind?.Trim(), "skin_variant", StringComparison.OrdinalIgnoreCase);
        }

        List<PortraitRequest> BuildPortraitRequests(IEnumerable<string> unitKeys)
        {
            var requests = new List<PortraitRequest>();
            var seenPortraitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string requestedKey in NormalizeUniqueKeys(unitKeys))
            {
                if (!TryResolvePortraitKey(requestedKey, out string portraitKey, out string resolutionSource))
                    continue;

                if (!seenPortraitKeys.Add(portraitKey))
                    continue;

                requests.Add(new PortraitRequest
                {
                    RequestedKey = requestedKey,
                    PortraitKey = portraitKey,
                    ResolutionSource = resolutionSource,
                });
            }

            return requests;
        }

        static string DescribePortraitRequests(List<PortraitRequest> requests)
        {
            if (requests == null || requests.Count == 0)
                return "<none>";

            var parts = new string[requests.Count];
            for (int i = 0; i < requests.Count; i++)
            {
                var request = requests[i];
                parts[i] = string.Equals(request.RequestedKey, request.PortraitKey, StringComparison.OrdinalIgnoreCase)
                    ? request.RequestedKey
                    : $"{request.RequestedKey}->{request.PortraitKey}";
            }

            return string.Join(", ", parts);
        }

        string DescribeActiveCatalogState()
        {
            string manifestTimestamp = Manifest?.generated_at ?? "<manifest-unavailable>";
            var locatorIds = Addressables.ResourceLocators?
                .Select(locator => locator?.LocatorId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? Array.Empty<string>();

            string locatorSummary = locatorIds.Length > 0
                ? string.Join(", ", locatorIds)
                : "<no-locators>";

            return $"manifest_generated_at={manifestTimestamp}; locator_count={locatorIds.Length}; locators={locatorSummary}";
        }

        static string DescribeKeys(IEnumerable<string> keys)
        {
            if (keys == null)
                return "<none>";

            var normalized = new List<string>();
            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                normalized.Add(key.Trim());
            }

            if (normalized.Count == 0)
                return "<none>";

            return string.Join(", ", normalized);
        }

        static string NormalizeAddress(string address, string fallback)
        {
            string normalized = string.IsNullOrWhiteSpace(address) ? fallback : address.Trim();
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        CriticalContentEntry[] GetT1ContentEntries()
        {
            if (Manifest?.t1_content != null && Manifest.t1_content.Length > 0)
                return Manifest.t1_content;

            return Manifest?.critical_content ?? Array.Empty<CriticalContentEntry>();
        }

        CriticalContentEntry[] GetLoadoutCriticalContentEntries()
        {
            if (Manifest?.loadout_critical_content != null && Manifest.loadout_critical_content.Length > 0)
                return Manifest.loadout_critical_content;

            return BuildManifestEntriesFromCatalog(scope => scope == "loadout_only" || scope == "both");
        }

        CriticalContentEntry[] GetWaveCriticalContentEntries()
        {
            if (Manifest?.wave_critical_content != null && Manifest.wave_critical_content.Length > 0)
                return Manifest.wave_critical_content;

            return BuildManifestEntriesFromCatalog(scope => scope == "wave_only" || scope == "both");
        }

        CriticalContentEntry[] GetT1GameplayContentEntries()
        {
            return GetWaveCriticalContentEntries();
        }

        CriticalContentEntry[] BuildManifestEntriesFromCatalog(Func<string, bool> scopePredicate)
        {
            if (Manifest == null || scopePredicate == null)
                return Array.Empty<CriticalContentEntry>();

            var entries = new List<CriticalContentEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var unit in Manifest.units ?? Array.Empty<ContentManifestEntry>())
            {
                if (unit == null || unit.remote_content == null || !scopePredicate(unit.usage_scope))
                    continue;

                AddDerivedCriticalEntry(entries, seen, "unit", unit.key, unit.remote_content);
            }

            foreach (var skin in Manifest.skins ?? Array.Empty<ContentManifestSkinEntry>())
            {
                if (skin == null || skin.remote_content == null || !scopePredicate(skin.usage_scope))
                    continue;

                AddDerivedCriticalEntry(entries, seen, "skin", skin.skin_key, skin.remote_content);
            }

            return entries.Count == 0 ? Array.Empty<CriticalContentEntry>() : entries.ToArray();
        }

        static void AddDerivedCriticalEntry(
            List<CriticalContentEntry> entries,
            HashSet<string> seen,
            string kind,
            string key,
            RemoteContentEntry remote)
        {
            string contentKey = remote?.content_key?.Trim();
            if (entries == null || seen == null || string.IsNullOrWhiteSpace(contentKey))
                return;

            string dedupeKey = $"{kind}:{contentKey}";
            if (!seen.Add(dedupeKey))
                return;

            string address = !string.IsNullOrWhiteSpace(remote.prefab_address)
                ? remote.prefab_address.Trim()
                : !string.IsNullOrWhiteSpace(remote.addressables_label)
                    ? remote.addressables_label.Trim()
                    : !string.IsNullOrWhiteSpace(remote.content_url)
                        ? remote.content_url.Trim()
                        : remote.catalog_url?.Trim();

            entries.Add(new CriticalContentEntry
            {
                kind = kind,
                key = key,
                content_key = contentKey,
                content_hash = remote?.content_hash?.Trim(),
                tier = remote.tier,
                address = address,
                reason = remote.preload_reason,
            });
        }

        string ResolveEnvironmentAddressFromManifest()
        {
            var entries = GetWaveCriticalContentEntries();
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                if (!string.Equals(entry.kind, "environment", StringComparison.OrdinalIgnoreCase))
                    continue;

                string address = entry.address?.Trim();
                if (!string.IsNullOrWhiteSpace(address))
                    return address;
            }

            return GameMlEnvironmentAddress;
        }

        bool TryGetEnvironmentManifestEntry(string address, out CriticalContentEntry entry, out string normalizedAddress)
        {
            normalizedAddress = NormalizeAddress(address, ResolveEnvironmentAddressFromManifest());
            entry = null;

            var entries = GetWaveCriticalContentEntries();
            for (int i = 0; i < entries.Length; i++)
            {
                var candidate = entries[i];
                if (candidate == null)
                    continue;
                if (!string.Equals(candidate.kind, "environment", StringComparison.OrdinalIgnoreCase))
                    continue;

                string candidateAddress = NormalizeAddress(candidate.address, normalizedAddress);
                if (!string.Equals(candidateAddress, normalizedAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                entry = candidate;
                return true;
            }

            return false;
        }

        void ReportProgress(float progress, string status, Action<float, string> callback)
        {
            LastProgress = Mathf.Clamp01(progress);
            LastStatus = status ?? "";
            callback?.Invoke(LastProgress, LastStatus);
        }

        sealed class DownloadRequest
        {
            public string Kind;
            public string DisplayKey;
            public string ContentKey;
            public object DownloadKey;
            public string AssetKey;
            public string[] DependencyKeys;
            public long DownloadSizeBytes;
            public bool IsDependencyOnly;
        }

        sealed class PortraitRequest
        {
            public string RequestedKey;
            public string PortraitKey;
            public string ResolutionSource;
        }
    }
}
