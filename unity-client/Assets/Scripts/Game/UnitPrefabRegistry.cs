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
using CastleDefender.Net;

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
        readonly HashSet<string> _loggedMissingUnits = new(System.StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _loggedMissingSkins = new(System.StringComparer.OrdinalIgnoreCase);
        static GameObject s_runtimeFallbackPrefab;

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
            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent != null && remoteContent.TryGetLoadedPrefabForUnit(key, out var remotePrefab) && remotePrefab != null)
                return remotePrefab;

            if (TryGet(key, out var e) && e.prefab != null) return e.prefab;
            return ResolveMissingUnitPrefab(key, remoteContent);
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
                var remoteContent = RemoteContentManager.Instance;
                if (remoteContent != null && remoteContent.TryGetLoadedPrefabForSkin(skinKey, out var remoteSkinPrefab) && remoteSkinPrefab != null)
                    return remoteSkinPrefab;

                if (_skinDict == null) Rebuild();
                if (_skinDict.TryGetValue(skinKey, out var s) && s.prefab != null)
                    return s.prefab;

                LogMissingSkinOnce(skinKey, unitType, remoteContent);
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

        GameObject ResolveMissingUnitPrefab(string key, RemoteContentManager remoteContent)
        {
            if (fallbackPrefab != null)
            {
                LogMissingUnitOnce(key, remoteContent, usedAssignedFallback: true);
                return fallbackPrefab;
            }

            LogMissingUnitOnce(key, remoteContent, usedAssignedFallback: false);
            return GetOrCreateRuntimeFallbackPrefab();
        }

        void LogMissingUnitOnce(string key, RemoteContentManager remoteContent, bool usedAssignedFallback)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(key) ? "<empty>" : key.Trim();
            if (!_loggedMissingUnits.Add(normalizedKey))
                return;

            string fallbackKind = usedAssignedFallback ? "assigned fallback prefab" : "runtime placeholder prefab";
            string message = $"[UnitPrefabRegistry] Missing prefab for unit '{normalizedKey}'. Using {fallbackKind}.";

            if (IsCriticalUnit(remoteContent, normalizedKey))
                Debug.LogError(message);
            else
                Debug.LogWarning(message);
        }

        void LogMissingSkinOnce(string skinKey, string unitType, RemoteContentManager remoteContent)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(skinKey) ? "<empty>" : skinKey.Trim();
            if (!_loggedMissingSkins.Add(normalizedKey))
                return;

            string message = $"[UnitPrefabRegistry] Missing prefab for skin '{normalizedKey}'. Falling back to unit '{unitType ?? "<unknown>"}'.";

            if (IsCriticalSkin(remoteContent, normalizedKey))
                Debug.LogError(message);
            else
                Debug.LogWarning(message);
        }

        static GameObject GetOrCreateRuntimeFallbackPrefab()
        {
            if (s_runtimeFallbackPrefab != null)
                return s_runtimeFallbackPrefab;

            var root = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            root.name = "RuntimeMissingUnitFallback";
            root.SetActive(false);
            root.hideFlags = HideFlags.HideAndDontSave;

            var collider = root.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            var renderer = root.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = renderer.material;
                material.color = new Color(1f, 0.25f, 0.25f, 1f);
            }

            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "MissingMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            marker.transform.localScale = new Vector3(0.75f, 0.2f, 0.75f);

            var markerCollider = marker.GetComponent<Collider>();
            if (markerCollider != null)
                Object.Destroy(markerCollider);

            var markerRenderer = marker.GetComponent<Renderer>();
            if (markerRenderer != null)
            {
                var markerMaterial = markerRenderer.material;
                markerMaterial.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            }

            s_runtimeFallbackPrefab = root;
            return s_runtimeFallbackPrefab;
        }

        static bool IsCriticalUnit(RemoteContentManager remoteContent, string unitKey)
        {
            if (remoteContent?.Manifest?.critical_content == null || string.IsNullOrWhiteSpace(unitKey))
                return false;

            foreach (var entry in remoteContent.Manifest.critical_content)
            {
                if (entry == null) continue;
                if (!string.Equals(entry.kind, "unit", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(entry.key, unitKey, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool IsCriticalSkin(RemoteContentManager remoteContent, string skinKey)
        {
            if (remoteContent?.Manifest?.critical_content == null || string.IsNullOrWhiteSpace(skinKey))
                return false;

            foreach (var entry in remoteContent.Manifest.critical_content)
            {
                if (entry == null) continue;
                if (!string.Equals(entry.kind, "skin", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(entry.key, skinKey, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
