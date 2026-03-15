using System;
using System.Collections;
using System.Collections.Generic;
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

        public static RemoteContentManager Instance { get; private set; }

        public ContentManifestResponse Manifest { get; private set; }
        public bool HasManifest => Manifest != null;
        public bool HasCompletedCriticalPreload { get; private set; }
        public string LastError { get; private set; }
        public float LastProgress { get; private set; }
        public string LastStatus { get; private set; } = "";
        public CriticalPreloadFailureStage LastFailureStage { get; private set; }
        public bool HasRetryableFailure => LastFailureStage != CriticalPreloadFailureStage.None && !HasCompletedCriticalPreload;
        public string LastAddressablesCallError { get; private set; }

        readonly HashSet<string> _preloadedContentKeys = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, GameObject> _loadedPrefabsByContentKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, AsyncOperationHandle<GameObject>> _assetHandlesByContentKey = new(StringComparer.OrdinalIgnoreCase);
        const string CachedManifestPlayerPrefsKey = "remote_content_manifest_cache_v1";
        bool _addressablesReady;
        AsyncOperationHandle _initializationHandle;
        bool _hasInitializationHandle;

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

        public IEnumerator PreloadCriticalContentForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false)
        {
            LastError = null;
            LastFailureStage = CriticalPreloadFailureStage.None;
            LastAddressablesCallError = null;
            HasCompletedCriticalPreload = false;
            ReportProgress(0f, "Preparing content...", onProgress);

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

            var critical = Manifest.critical_content ?? Array.Empty<CriticalContentEntry>();
            if (critical.Length == 0)
            {
                ReportProgress(1f, "No critical content required.", onProgress);
                HasCompletedCriticalPreload = true;
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
                ReportProgress(1f, "All critical content already cached.", onProgress);
                HasCompletedCriticalPreload = true;
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

            HasCompletedCriticalPreload = true;
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

            HasCompletedCriticalPreload = true;
            ReportProgress(1f, $"Prepared {_preloadedContentKeys.Count} content packs.", onProgress);
        }

        public IEnumerator EnsureManifestForSession(Action<float, string> onProgress = null, bool forceRefreshManifest = false)
        {
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

        public string BuildCriticalContentRequirementMessage(int previewCount = 5)
        {
            var critical = Manifest?.critical_content ?? Array.Empty<CriticalContentEntry>();
            if (critical.Length == 0)
                return "No required remote gameplay packs are needed before the first match.";

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

            string summary = $"Required before the first match: {critical.Length} remote gameplay pack";
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

            summary += " This download is required to render and run gameplay safely.";
            return summary;
        }

        public bool IsContentKeyPreloaded(string contentKey)
        {
            if (string.IsNullOrWhiteSpace(contentKey)) return false;
            return _preloadedContentKeys.Contains(contentKey.Trim());
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
            if (string.IsNullOrWhiteSpace(skinKey) || Manifest?.skins == null) return false;

            foreach (var skin in Manifest.skins)
            {
                if (skin == null || !string.Equals(skin.skin_key, skinKey, StringComparison.OrdinalIgnoreCase))
                    continue;

                return TryGetLoadedPrefabForRemote(skin.remote_content, out prefab);
            }

            return false;
        }

        IEnumerator FetchManifest(Action<float, string> onProgress)
        {
            ReportProgress(0.05f, "Downloading content manifest...", onProgress);

            using var req = UnityWebRequest.Get(BaseUrl.TrimEnd('/') + "/api/content-manifest");
            req.timeout = 15;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                LastFailureStage = CriticalPreloadFailureStage.ManifestDownload;
                if (TryLoadCachedManifest(onProgress))
                    yield break;

                LastError = $"Could not download content manifest: {req.error}";
                Debug.LogWarning($"[RemoteContent] Manifest fetch failed: {req.error}");
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

                CacheManifestJson(req.downloadHandler.text);
                Debug.Log($"[RemoteContent] Manifest ready — {(Manifest.units?.Length ?? 0)} units, {(Manifest.skins?.Length ?? 0)} skins, {(Manifest.critical_content?.Length ?? 0)} critical entries");
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

        bool TryLoadCachedManifest(Action<float, string> onProgress)
        {
            string cachedJson = PlayerPrefs.GetString(CachedManifestPlayerPrefsKey, "");
            if (!TryApplyManifestJson(cachedJson, out string error))
            {
                if (!string.IsNullOrWhiteSpace(cachedJson))
                    Debug.LogWarning($"[RemoteContent] Cached manifest was unusable: {error}");
                return false;
            }

            LastError = null;
            Debug.Log("[RemoteContent] Using cached manifest because the live manifest could not be downloaded.");
            ReportProgress(0.2f, "Using cached content manifest.", onProgress);
            return true;
        }

        void CacheManifestJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            PlayerPrefs.SetString(CachedManifestPlayerPrefsKey, json);
            PlayerPrefs.Save();
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
    }
}
