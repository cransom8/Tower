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
                new UnitCatalogEntry { key="goblin",          name="Goblin",          send_cost=1,  build_cost=8,  hp=55,  path_speed=0.058f, enabled=true },
                new UnitCatalogEntry { key="kobold",          name="Kobold",          send_cost=1,  build_cost=6,  hp=40,  path_speed=0.065f, enabled=true },
                new UnitCatalogEntry { key="hobgoblin",       name="Hobgoblin",       send_cost=2,  build_cost=10, hp=85,  path_speed=0.042f, enabled=true },
                new UnitCatalogEntry { key="orc",             name="Orc",             send_cost=3,  build_cost=14, hp=100, path_speed=0.038f, enabled=true },
                new UnitCatalogEntry { key="ogre",            name="Ogre",            send_cost=5,  build_cost=18, hp=180, path_speed=0.022f, enabled=true },
                new UnitCatalogEntry { key="troll",           name="Troll",           send_cost=4,  build_cost=16, hp=160, path_speed=0.025f, enabled=true },
                new UnitCatalogEntry { key="cyclops",         name="Cyclops",         send_cost=6,  build_cost=26, hp=200, path_speed=0.018f, enabled=true },
                new UnitCatalogEntry { key="ghoul",           name="Ghoul",           send_cost=2,  build_cost=10, hp=80,  path_speed=0.042f, enabled=true },
                new UnitCatalogEntry { key="skeleton_knight", name="Skeleton Knight", send_cost=3,  build_cost=14, hp=120, path_speed=0.030f, enabled=true },
                new UnitCatalogEntry { key="undead_warrior",  name="Undead Warrior",  send_cost=3,  build_cost=12, hp=110, path_speed=0.028f, enabled=true },
                new UnitCatalogEntry { key="mummy",           name="Mummy",           send_cost=4,  build_cost=16, hp=150, path_speed=0.020f, enabled=true },
                new UnitCatalogEntry { key="vampire",         name="Vampire",         send_cost=5,  build_cost=20, hp=100, path_speed=0.038f, enabled=true },                new UnitCatalogEntry { key="giant_viper",     name="Giant Viper",     send_cost=4,  build_cost=14, hp=70,  path_speed=0.050f, enabled=true },
                new UnitCatalogEntry { key="darkness_spider", name="Darkness Spider", send_cost=3,  build_cost=12, hp=75,  path_speed=0.048f, enabled=true },
                new UnitCatalogEntry { key="lizard_warrior",  name="Lizard Warrior",  send_cost=3,  build_cost=14, hp=90,  path_speed=0.045f, enabled=true },
                new UnitCatalogEntry { key="dragonide",       name="Dragonide",       send_cost=5,  build_cost=18, hp=120, path_speed=0.030f, enabled=true },
                new UnitCatalogEntry { key="wyvern",          name="Wyvern",          send_cost=6,  build_cost=22, hp=130, path_speed=0.042f, enabled=true },
                new UnitCatalogEntry { key="hydra",           name="Hydra",           send_cost=13, build_cost=32, hp=320, path_speed=0.018f, enabled=true },
                new UnitCatalogEntry { key="mountain_dragon", name="Mountain Dragon", send_cost=12, build_cost=35, hp=280, path_speed=0.025f, enabled=true },
                new UnitCatalogEntry { key="werewolf",        name="Werewolf",        send_cost=5,  build_cost=18, hp=140, path_speed=0.040f, enabled=true },
                new UnitCatalogEntry { key="harpy",           name="Harpy",           send_cost=4,  build_cost=12, hp=80,  path_speed=0.052f, enabled=true },
                new UnitCatalogEntry { key="griffin",         name="Griffin",         send_cost=8,  build_cost=30, hp=180, path_speed=0.035f, enabled=true },
                new UnitCatalogEntry { key="manticora",       name="Manticora",       send_cost=10, build_cost=22, hp=240, path_speed=0.028f, enabled=true },
                new UnitCatalogEntry { key="chimera",         name="Chimera",         send_cost=11, build_cost=38, hp=260, path_speed=0.030f, enabled=true },
                new UnitCatalogEntry { key="evil_watcher",    name="Evil Watcher",    send_cost=6,  build_cost=24, hp=70,  path_speed=0.025f, enabled=true },
                new UnitCatalogEntry { key="oak_tree_ent",    name="Oak Tree Ent",    send_cost=8,  build_cost=28, hp=300, path_speed=0.012f, enabled=true },
                new UnitCatalogEntry { key="ice_golem",       name="Ice Golem",       send_cost=9,  build_cost=30, hp=220, path_speed=0.020f, enabled=true },
                new UnitCatalogEntry { key="demon_lord",      name="Demon Lord",      send_cost=15, build_cost=50, hp=350, path_speed=0.020f, enabled=true },
            };
            foreach (var e in defaults) { Units.Add(e); UnitByKey[e.key] = e; }
        }

static void FallbackTowers()
        {
            Towers.Clear(); TowerByKey.Clear();
            var defaults = new[]
            {
                new TowerCatalogEntry { key="goblin",          name="Goblin",          build_cost=8,  enabled=true },
                new TowerCatalogEntry { key="kobold",          name="Kobold",          build_cost=6,  enabled=true },
                new TowerCatalogEntry { key="hobgoblin",       name="Hobgoblin",       build_cost=10, enabled=true },
                new TowerCatalogEntry { key="orc",             name="Orc",             build_cost=14, enabled=true },
                new TowerCatalogEntry { key="ogre",            name="Ogre",            build_cost=18, enabled=true },
                new TowerCatalogEntry { key="troll",           name="Troll",           build_cost=16, enabled=true },
                new TowerCatalogEntry { key="cyclops",         name="Cyclops",         build_cost=26, enabled=true },
                new TowerCatalogEntry { key="ghoul",           name="Ghoul",           build_cost=10, enabled=true },
                new TowerCatalogEntry { key="skeleton_knight", name="Skeleton Knight", build_cost=14, enabled=true },
                new TowerCatalogEntry { key="undead_warrior",  name="Undead Warrior",  build_cost=12, enabled=true },
                new TowerCatalogEntry { key="mummy",           name="Mummy",           build_cost=16, enabled=true },
                new TowerCatalogEntry { key="vampire",         name="Vampire",         build_cost=20, enabled=true },                new TowerCatalogEntry { key="giant_viper",     name="Giant Viper",     build_cost=14, enabled=true },
                new TowerCatalogEntry { key="darkness_spider", name="Darkness Spider", build_cost=12, enabled=true },
                new TowerCatalogEntry { key="lizard_warrior",  name="Lizard Warrior",  build_cost=14, enabled=true },
                new TowerCatalogEntry { key="dragonide",       name="Dragonide",       build_cost=18, enabled=true },
                new TowerCatalogEntry { key="wyvern",          name="Wyvern",          build_cost=22, enabled=true },
                new TowerCatalogEntry { key="hydra",           name="Hydra",           build_cost=32, enabled=true },
                new TowerCatalogEntry { key="mountain_dragon", name="Mountain Dragon", build_cost=35, enabled=true },
                new TowerCatalogEntry { key="werewolf",        name="Werewolf",        build_cost=18, enabled=true },
                new TowerCatalogEntry { key="harpy",           name="Harpy",           build_cost=12, enabled=true },
                new TowerCatalogEntry { key="griffin",         name="Griffin",         build_cost=30, enabled=true },
                new TowerCatalogEntry { key="manticora",       name="Manticora",       build_cost=22, enabled=true },
                new TowerCatalogEntry { key="chimera",         name="Chimera",         build_cost=38, enabled=true },
                new TowerCatalogEntry { key="evil_watcher",    name="Evil Watcher",    build_cost=24, enabled=true },
                new TowerCatalogEntry { key="oak_tree_ent",    name="Oak Tree Ent",    build_cost=28, enabled=true },
                new TowerCatalogEntry { key="ice_golem",       name="Ice Golem",       build_cost=30, enabled=true },
                new TowerCatalogEntry { key="demon_lord",      name="Demon Lord",      build_cost=50, enabled=true },
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

