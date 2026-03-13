// CatalogLoader.cs — Fetches unit/tower/barracks catalogs from the server on startup.
// Singleton — persists across scenes (same lifetime as NetworkManager).
//
// SETUP:
//   Add to the same scene as NetworkManager (Lobby scene).
//   Both use DontDestroyOnLoad so they survive scene transitions.
//
// Usage:
//   CatalogLoader.IsReady            — true once all fetches complete
//   CatalogLoader.OnCatalogReady     — fires once when ready
//   CatalogLoader.Units              — ordered list (maps to CmdBar button slots)
//   CatalogLoader.Towers             — ordered list (maps to TileMenuUI button slots)
//   CatalogLoader.BarracksLevels     — indexed by target level (level 2, 3, 4)

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace CastleDefender.Net
{
    public class CatalogLoader : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static CatalogLoader Instance { get; private set; }

        // ── Status ────────────────────────────────────────────────────────────
        public static bool   IsReady { get; private set; }
        public static event Action OnCatalogReady;

        // ── Catalogs ──────────────────────────────────────────────────────────
        public static List<UnitCatalogEntry>    Units          { get; } = new();
        public static List<TowerCatalogEntry>   Towers         { get; } = new();
        public static List<BarracksLevelEntry>  BarracksLevels { get; } = new();

        // Key lookups
        public static Dictionary<string, UnitCatalogEntry>  UnitByKey  { get; } = new();
        public static Dictionary<string, TowerCatalogEntry> TowerByKey { get; } = new();

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start() => StartCoroutine(FetchAll());

        // ── Base URL (mirrors NetworkManager.ResolvedServerUrl logic) ─────────
        string BaseUrl
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                var page = new Uri(Application.absoluteURL);
                bool standard = (page.Scheme == "https" && page.Port == 443)
                             || (page.Scheme == "http"  && page.Port == 80)
                             || page.Port < 0;
                return standard
                    ? $"{page.Scheme}://{page.Host}"
                    : $"{page.Scheme}://{page.Host}:{page.Port}";
#else
                return NetworkManager.Instance != null
                    ? NetworkManager.Instance.ServerUrl
                    : "http://localhost:3000";
#endif
            }
        }

        // ── Fetch sequence ────────────────────────────────────────────────────
        // Server wraps arrays in objects: { unitTypes: [...] } and { towers: [...] }
        [Serializable] class UnitTypesResponse  { public List<UnitCatalogEntry>  unitTypes; }
        [Serializable] class TowersResponse     { public List<TowerCatalogEntry> towers; }

        IEnumerator FetchAll()
        {
            yield return FetchWrapped<UnitTypesResponse, UnitCatalogEntry>(
                "/api/unit-types",
                r => r.unitTypes,
                Units, UnitByKey, e => e.key,
                FallbackUnits);

            yield return FetchWrapped<TowersResponse, TowerCatalogEntry>(
                "/api/towers",
                r => r.towers,
                Towers, TowerByKey, e => e.key,
                FallbackTowers);

            yield return FetchBarracksLevels();

            IsReady = true;
            Debug.Log($"[Catalog] Ready — {Units.Count} units, {Towers.Count} towers, " +
                      $"{BarracksLevels.Count} barracks levels");
            OnCatalogReady?.Invoke();
        }

        IEnumerator FetchWrapped<TResp, TItem>(
            string path,
            Func<TResp, List<TItem>> extractor,
            List<TItem> list,
            Dictionary<string, TItem> dict,
            Func<TItem, string> keySelector,
            Action fallback)
        {
            string url = BaseUrl + path;
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Catalog] {path} failed ({req.error}) — using defaults");
                fallback();
                yield break;
            }

            try
            {
                var resp    = JsonConvert.DeserializeObject<TResp>(req.downloadHandler.text);
                var entries = resp != null ? extractor(resp) : null;
                list.Clear();
                dict.Clear();
                if (entries != null && entries.Count > 0)
                {
                    foreach (var e in entries)
                    {
                        list.Add(e);
                        string key = keySelector(e);
                        if (!string.IsNullOrEmpty(key)) dict[key] = e;
                    }
                    Debug.Log($"[Catalog] {path} → {list.Count} entries");
                }
                else
                {
                    fallback();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Catalog] Parse error for {path}: {ex.Message} — using defaults");
                fallback();
            }
        }

        IEnumerator FetchList<T>(
            string path,
            List<T> list,
            Dictionary<string, T> dict,
            Func<T, string> keySelector,
            Action fallback)
        {
            string url = BaseUrl + path;
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Catalog] {path} failed ({req.error}) — using defaults");
                fallback();
                yield break;
            }

            try
            {
                var entries = JsonConvert.DeserializeObject<List<T>>(req.downloadHandler.text);
                list.Clear();
                dict.Clear();
                if (entries != null)
                {
                    foreach (var e in entries)
                    {
                        list.Add(e);
                        string key = keySelector(e);
                        if (!string.IsNullOrEmpty(key)) dict[key] = e;
                    }
                }
                Debug.Log($"[Catalog] {path} → {list.Count} entries");

                // Fall back if server returned an empty list
                if (list.Count == 0) fallback();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Catalog] Parse error for {path}: {ex.Message} — using defaults");
                fallback();
            }
        }

        IEnumerator FetchBarracksLevels()
        {
            string url = BaseUrl + "/api/barracks-levels";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Catalog] /api/barracks-levels not available — using defaults");
                FallbackBarracks();
                yield break;
            }

            try
            {
                var entries = JsonConvert.DeserializeObject<List<BarracksLevelEntry>>(
                    req.downloadHandler.text);
                BarracksLevels.Clear();
                if (entries != null && entries.Count > 0)
                    BarracksLevels.AddRange(entries);
                else
                    FallbackBarracks();
                Debug.Log($"[Catalog] /api/barracks-levels → {BarracksLevels.Count} levels");
            }
            catch
            {
                FallbackBarracks();
            }
        }

        // ── Hardcoded fallbacks (match server seed data) ──────────────────────
        static void FallbackUnits()
        {
            Units.Clear(); UnitByKey.Clear();
            var defaults = new[]
            {
                new UnitCatalogEntry { key="goblin",  name="Goblin",  send_cost=1, build_cost= 8, hp= 55, path_speed=0.058f },
                new UnitCatalogEntry { key="orc",     name="Orc",     send_cost=3, build_cost=14, hp=100, path_speed=0.038f },
                new UnitCatalogEntry { key="troll",   name="Troll",   send_cost=4, build_cost=16, hp=160, path_speed=0.025f },
                new UnitCatalogEntry { key="vampire", name="Vampire", send_cost=5, build_cost=20, hp=100, path_speed=0.038f },
                new UnitCatalogEntry { key="wyvern",  name="Wyvern",  send_cost=6, build_cost=22, hp=130, path_speed=0.042f },
            };
            foreach (var e in defaults) { Units.Add(e); UnitByKey[e.key] = e; }
        }

        static void FallbackTowers()
        {
            Towers.Clear(); TowerByKey.Clear();
            var defaults = new[]
            {
                new TowerCatalogEntry { key="goblin",  name="Goblin",  build_cost= 8 },
                new TowerCatalogEntry { key="orc",     name="Orc",     build_cost=14 },
                new TowerCatalogEntry { key="troll",   name="Troll",   build_cost=16 },
                new TowerCatalogEntry { key="vampire", name="Vampire", build_cost=20 },
                new TowerCatalogEntry { key="wyvern",  name="Wyvern",  build_cost=22 },
            };
            foreach (var e in defaults) { Towers.Add(e); TowerByKey[e.key] = e; }
        }

        static void FallbackBarracks()
        {
            BarracksLevels.Clear();
            BarracksLevels.Add(new BarracksLevelEntry { level=2, upgrade_cost=100, multiplier=1.15f, notes="Fallback level 2" });
            BarracksLevels.Add(new BarracksLevelEntry { level=3, upgrade_cost=220, multiplier=1.30f, notes="Fallback level 3" });
            BarracksLevels.Add(new BarracksLevelEntry { level=4, upgrade_cost=400, multiplier=1.45f, notes="Fallback level 4" });
        }
    }
}
