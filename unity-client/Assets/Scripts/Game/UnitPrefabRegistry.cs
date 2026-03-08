// UnitPrefabRegistry.cs — ScriptableObject mapping unit type keys (and optional skin keys)
// to 3D prefabs. One asset shared across all game scenes.
//
// Create via Assets > Create > CastleDefender > Unit Prefab Registry.
// Assign to LaneRenderer and TileGrid in the Inspector.
//
// Skin system:
//   Add entries to skinEntries with a skinKey that matches the server DB skin key.
//   LaneRenderer calls GetPrefabForSkin(type, skinKey) — if skinKey has an entry its
//   prefab is used, otherwise falls back to the base type entry.

using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [CreateAssetMenu(menuName = "CastleDefender/Unit Prefab Registry", fileName = "UnitPrefabRegistry")]
    public class UnitPrefabRegistry : ScriptableObject
    {
        // ── Base unit entries (one per unit type) ─────────────────────────────
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
        public Entry[] entries;

        [Tooltip("Used when no entry and no skin matches the unit type key.")]
        public GameObject fallbackPrefab;

        // ── Skin entries (override prefab for a specific unit type) ───────────
        [System.Serializable]
        public struct SkinEntry
        {
            [Tooltip("Matches server skin_key in the skin_catalog table.")]
            public string     skinKey;
            [Tooltip("Which unit type this skin applies to (must match an Entry key).")]
            public string     unitType;
            public GameObject prefab;
            [Range(0.1f, 5f)]
            public float      scale;
        }

        [Tooltip("Add one entry per purchasable skin. skinKey must match server DB skin_key.")]
        public SkinEntry[] skinEntries;

        // ── Internal lookup tables ────────────────────────────────────────────
        Dictionary<string, Entry>     _dict;
        Dictionary<string, SkinEntry> _skinDict; // key = skinKey

        void OnEnable() => Rebuild();

        public void Rebuild()
        {
            _dict = new Dictionary<string, Entry>(System.StringComparer.OrdinalIgnoreCase);
            if (entries != null)
                foreach (var e in entries)
                    if (!string.IsNullOrEmpty(e.key))
                        _dict[e.key] = e;

            _skinDict = new Dictionary<string, SkinEntry>(System.StringComparer.OrdinalIgnoreCase);
            if (skinEntries != null)
                foreach (var s in skinEntries)
                    if (!string.IsNullOrEmpty(s.skinKey))
                        _skinDict[s.skinKey] = s;
        }

        // ── Base type lookup ──────────────────────────────────────────────────
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

        // ── Skin-aware lookup (call this from LaneRenderer) ───────────────────
        /// <summary>
        /// Returns the skin prefab if skinKey is set and found, otherwise the base type prefab.
        /// </summary>
        public GameObject GetPrefabForSkin(string unitType, string skinKey)
        {
            if (!string.IsNullOrEmpty(skinKey))
            {
                if (_skinDict == null) Rebuild();
                if (_skinDict.TryGetValue(skinKey, out var s) && s.prefab != null)
                    return s.prefab;
            }
            return GetPrefab(unitType);
        }

        /// <summary>
        /// Returns the skin scale if skinKey is set and found, otherwise the base type scale.
        /// </summary>
        public float GetScaleForSkin(string unitType, string skinKey)
        {
            if (!string.IsNullOrEmpty(skinKey))
            {
                if (_skinDict == null) Rebuild();
                if (_skinDict.TryGetValue(skinKey, out var s) && s.scale > 0f)
                    return s.scale;
            }
            return GetScale(unitType);
        }
    }
}
