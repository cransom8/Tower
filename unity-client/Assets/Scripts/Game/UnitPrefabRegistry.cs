// UnitPrefabRegistry.cs — ScriptableObject mapping unit type keys to 3D prefabs.
// Create via Assets > Create > CastleDefender > Unit Prefab Registry.
// Assign to LaneRenderer and TileGrid in the Inspector.

using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [CreateAssetMenu(menuName = "CastleDefender/Unit Prefab Registry", fileName = "UnitPrefabRegistry")]
    public class UnitPrefabRegistry : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string     key;
            public GameObject prefab;
            [Range(0.1f, 5f)]
            public float      scale;
            public Color      tintMine;
            public Color      tintEnemy;
        }

        [Tooltip("One entry per unit type key (must match server DB key exactly).")]
        public Entry[]    entries;

        [Tooltip("Used when no entry matches the unit type key.")]
        public GameObject fallbackPrefab;

        Dictionary<string, Entry> _dict;

        void OnEnable() => Rebuild();

        public void Rebuild()
        {
            _dict = new Dictionary<string, Entry>(System.StringComparer.OrdinalIgnoreCase);
            if (entries == null) return;
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.key))
                    _dict[e.key] = e;
        }

        public bool TryGet(string key, out Entry entry)
        {
            if (_dict == null) Rebuild();
            return _dict.TryGetValue(key ?? "", out entry);
        }

        public GameObject GetPrefab(string key)
        {
            if (TryGet(key, out var e) && e.prefab != null) return e.prefab;
            return fallbackPrefab;
        }

        public float GetScale(string key) =>
            TryGet(key, out var e) ? (e.scale > 0f ? e.scale : 1f) : 1f;

        public Color GetTintMine(string key) =>
            TryGet(key, out var e) ? e.tintMine : new Color(0.20f, 0.80f, 0.70f);

        public Color GetTintEnemy(string key) =>
            TryGet(key, out var e) ? e.tintEnemy : new Color(0.90f, 0.25f, 0.25f);
    }
}
