// LoadoutManager.cs — Fetches player loadout slots from the server on startup.
// Singleton. Persists across scenes (same lifetime as NetworkManager).
//
// SETUP:
//   Add to the same scene as NetworkManager (Lobby scene).
//   Use Script Execution Order: AuthManager → NetworkManager → LoadoutManager.
//
// Usage:
//   LoadoutManager.IsReady         — true once fetch completes (or auth skipped for guests)
//   LoadoutManager.OnLoadoutsReady — fires once when ready
//   LoadoutManager.Slots           — list of 0-4 saved slots
//   LoadoutManager.SelectedSlot    — -1 = default, 0-3 = slot index (set before queuing)

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;

namespace CastleDefender.Net
{
    // ─── Data types ───────────────────────────────────────────────────────────

    [Serializable]
    public class LoadoutSlot
    {
        public int    slot;             // 0-3
        public string name;             // user-given label
        public int[]  unit_type_ids;    // 5 unit type DB ids
    }

    [Serializable]
    class LoadoutListResponse
    {
        public LoadoutSlot[] loadouts;
    }

    // ─── Manager ──────────────────────────────────────────────────────────────

    public class LoadoutManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static LoadoutManager Instance { get; private set; }

        // ── State ─────────────────────────────────────────────────────────────
        public static bool              IsReady       { get; private set; }
        public static event Action      OnLoadoutsReady;
        public static List<LoadoutSlot> Slots         { get; } = new();

        /// <summary>-1 = server default; 0-3 = specific slot. Set before QueueEnter.</summary>
        public static int SelectedSlot { get; set; } = -1;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            if (AuthManager.IsAuthenticated)
                StartCoroutine(FetchLoadouts());
            else
                MarkReady();
        }

        // ── Fetch ─────────────────────────────────────────────────────────────
        IEnumerator FetchLoadouts()
        {
            string url = BaseUrl + "/api/loadouts";
            using var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            req.SetRequestHeader("Authorization", $"Bearer {AuthManager.Token}");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    var resp = JsonConvert.DeserializeObject<LoadoutListResponse>(req.downloadHandler.text);
                    Slots.Clear();
                    if (resp?.loadouts != null)
                        Slots.AddRange(resp.loadouts);
                    Debug.Log($"[Loadout] Fetched {Slots.Count} slots");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Loadout] Parse error: {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"[Loadout] Fetch failed ({req.error}) — using default");
            }

            MarkReady();
        }

        static void MarkReady()
        {
            IsReady = true;
            OnLoadoutsReady?.Invoke();
        }

        // ── Save slot (PUT /api/loadouts/:slot) ───────────────────────────────
        public static IEnumerator SaveSlot(int slot, string name, int[] unitTypeIds, Action<bool> onDone = null)
        {
            if (!AuthManager.IsAuthenticated) { onDone?.Invoke(false); yield break; }

            string url  = Instance.BaseUrl + $"/api/loadouts/{slot}";
            string body = JsonConvert.SerializeObject(new { name, unitTypeIds });
            using var req = new UnityWebRequest(url, "PUT");
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(body);
            req.uploadHandler   = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {AuthManager.Token}");
            req.timeout = 10;
            yield return req.SendWebRequest();

            bool ok = req.result == UnityWebRequest.Result.Success;
            if (ok)
            {
                // Update local cache
                var existing = Slots.FindIndex(s => s.slot == slot);
                var updated  = new LoadoutSlot { slot = slot, name = name, unit_type_ids = unitTypeIds };
                if (existing >= 0) Slots[existing] = updated;
                else               Slots.Add(updated);
                Slots.Sort((a, b) => a.slot.CompareTo(b.slot));
                Debug.Log($"[Loadout] Slot {slot} saved");
            }
            else
            {
                Debug.LogWarning($"[Loadout] Save failed ({req.error})");
            }

            onDone?.Invoke(ok);
        }

        // ── Base URL (mirrors CatalogLoader logic) ────────────────────────────
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
    }
}
