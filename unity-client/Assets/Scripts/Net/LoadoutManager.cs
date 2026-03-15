using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Net
{
    // Deprecated compatibility shim.
    // Saved preset loadouts are no longer fetched or persisted; the live game
    // now uses the dedicated in-match loadout phase. Keep this singleton around
    // for now so older bootstrap/scene references do not break.
    [Obsolete("Saved preset loadouts are deprecated. Use the in-match loadout phase instead.")]
    [Serializable]
    public class LoadoutSlot
    {
        public int slot;
        public string name;
        public int[] unit_type_ids;
    }

    public class LoadoutManager : MonoBehaviour
    {
        public static LoadoutManager Instance { get; private set; }

        public static bool IsReady { get; private set; }
        public static event Action OnLoadoutsReady;
        public static List<LoadoutSlot> Slots { get; } = new();

        // Deprecated. Kept only so any lingering callers compile.
        public static int SelectedSlot { get; set; } = -1;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            Slots.Clear();
            MarkReady();
        }

        static void MarkReady()
        {
            IsReady = true;
            OnLoadoutsReady?.Invoke();
        }

        public static IEnumerator SaveSlot(int slot, string name, int[] unitTypeIds, Action<bool> onDone = null)
        {
            Debug.LogWarning("[Loadout] SaveSlot ignored: saved preset loadouts are deprecated.");
            onDone?.Invoke(false);
            yield break;
        }
    }
}
