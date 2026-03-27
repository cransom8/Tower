// CatalogLoader.cs - Fetches unit and barracks catalogs from the server on startup.
// Singleton - persists across scenes (same lifetime as NetworkManager).
//
// Usage:
//   CatalogLoader.IsReady            - true once all fetches complete successfully
//   CatalogLoader.HasCriticalFailure - true after any required catalog fetch fails
//   CatalogLoader.LastFailure        - most recent blocking failure detail
//   CatalogLoader.OnCatalogReady     - fires once when all required catalogs are valid
//   CatalogLoader.OnCatalogFailed    - fires once when a required catalog fails

using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace CastleDefender.Net
{
    public class CatalogLoader : MonoBehaviour
    {
        public static CatalogLoader Instance { get; private set; }

        public static bool IsReady { get; private set; }
        public static bool HasCriticalFailure { get; private set; }
        public static string LastFailure { get; private set; }

        public static event Action OnCatalogReady;
        public static event Action<string> OnCatalogFailed;

        public static List<UnitCatalogEntry> Units { get; } = new();
        public static List<BarracksLevelEntry> BarracksLevels { get; } = new();

        public static Dictionary<string, UnitCatalogEntry> UnitByKey { get; } = new();

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start() => StartCoroutine(FetchAll());

        string BaseUrl
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var page = new Uri(Application.absoluteURL);
                bool standard = (page.Scheme == "https" && page.Port == 443)
                             || (page.Scheme == "http" && page.Port == 80)
                             || page.Port < 0;
                return standard
                    ? $"{page.Scheme}://{page.Host}"
                    : $"{page.Scheme}://{page.Host}:{page.Port}";
#else
                return NetworkManager.Instance != null
                    ? NetworkManager.Instance.ResolvedServerUrl
                    : "http://localhost:3000";
#endif
            }
        }

        [Serializable]
        class UnitTypesResponse
        {
            public List<UnitCatalogEntry> unitTypes;
        }

        IEnumerator FetchAll()
        {
            IsReady = false;
            HasCriticalFailure = false;
            LastFailure = null;
            Units.Clear();
            UnitByKey.Clear();
            BarracksLevels.Clear();

            yield return FetchUnits();
            if (HasCriticalFailure)
                yield break;

            yield return FetchBarracksLevels();
            if (HasCriticalFailure)
                yield break;

            IsReady = true;
            Debug.Log($"[Catalog] Ready - {Units.Count} units, {BarracksLevels.Count} barracks levels");
            OnCatalogReady?.Invoke();
        }

        IEnumerator FetchUnits()
        {
            string path = "/api/unit-types";
            string url = BaseUrl + path;
            Debug.Log($"[Catalog] GET {url}");
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                FailCatalog(path, $"Request failed: {req.error}");
                yield break;
            }

            try
            {
                var resp = JsonConvert.DeserializeObject<UnitTypesResponse>(req.downloadHandler.text);
                var entries = resp?.unitTypes;
                if (entries == null || entries.Count == 0)
                {
                    FailCatalog(path, "The server returned an empty unit catalog.");
                    yield break;
                }

                Units.Clear();
                UnitByKey.Clear();
                foreach (var entry in entries)
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.key))
                        continue;

                    Units.Add(entry);
                    UnitByKey[entry.key] = entry;
                }

                if (Units.Count == 0)
                {
                    FailCatalog(path, "No valid unit entries remained after parsing the server response.");
                    yield break;
                }

                Debug.Log($"[Catalog] {path} -> {Units.Count} entries");
            }
            catch (Exception ex)
            {
                FailCatalog(path, $"Parse error: {ex.Message}");
            }
        }

        IEnumerator FetchBarracksLevels()
        {
            string path = "/api/barracks-levels";
            string url = BaseUrl + path;
            Debug.Log($"[Catalog] GET {url}");
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                FailCatalog(path, $"Request failed: {req.error}");
                yield break;
            }

            try
            {
                var entries = JsonConvert.DeserializeObject<List<BarracksLevelEntry>>(req.downloadHandler.text);
                if (entries == null || entries.Count == 0)
                {
                    FailCatalog(path, "The server returned an empty barracks level catalog.");
                    yield break;
                }

                BarracksLevels.Clear();
                BarracksLevels.AddRange(entries);
                Debug.Log($"[Catalog] {path} -> {BarracksLevels.Count} levels");
            }
            catch (Exception ex)
            {
                FailCatalog(path, $"Parse error: {ex.Message}");
            }
        }

        static void FailCatalog(string path, string detail)
        {
            if (HasCriticalFailure)
                return;

            IsReady = false;
            HasCriticalFailure = true;
            LastFailure = $"[Catalog] Required catalog '{path}' failed. {detail}";
            Debug.LogError(LastFailure);
            OnCatalogFailed?.Invoke(LastFailure);
        }
    }
}
