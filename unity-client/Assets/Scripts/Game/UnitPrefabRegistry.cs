// UnitPrefabRegistry.cs — ScriptableObject mapping unit type keys (and optional skin keys)
// to 3D prefabs. One asset shared across all game scenes.
//
// Create via Assets > Create > CastleDefender > Unit Prefab Registry.
// Assign to GameplayPresentationRoot and other live presentation systems in the Inspector.
//
// Skin system:
//   Add entries to skinEntries with a skinKey that matches the server DB skin key.
//   GameplayPresentationRoot consumers call GetPrefabForSkin(type, skinKey) — if skinKey has an entry its
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

        [Tooltip("Legacy field. Runtime no longer substitutes fallback prefabs when resolution fails.")]
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
            {
                if (IsRenderablePrefab(remotePrefab, out string remoteIssue))
                    return remotePrefab;

                LogBrokenRemoteUnitPrefabOnce(key, remoteIssue, remoteContent);
                return null;
            }

            if (TryGet(key, out var e) && e.prefab != null)
            {
                if (IsRenderablePrefab(e.prefab, out string localIssue))
                    return e.prefab;

                LogBrokenLocalUnitPrefabOnce(key, localIssue);
                return null;
            }

            LogMissingUnitOnce(key, remoteContent);
            return null;
        }

        public float GetScale(string key) =>
            TryGet(key, out var e) ? (e.scale > 0f ? e.scale : 1f) : 1f;

        public Color GetTintMine(string key) =>
            TryGet(key, out var e) ? e.tintMine : new Color(0.20f, 0.80f, 0.70f);

        public Color GetTintEnemy(string key) =>
            TryGet(key, out var e) ? e.tintEnemy : new Color(0.90f, 0.25f, 0.25f);

        // ── Skin-aware lookup for gameplay presentation consumers ────────────
        /// <summary>
        /// Returns the skin prefab if skinKey is set and found, otherwise the base type prefab.
        /// </summary>
        public GameObject GetPrefabForSkin(string unitType, string skinKey)
        {
            if (!string.IsNullOrEmpty(skinKey))
            {
                var remoteContent = RemoteContentManager.Instance;
                if (remoteContent != null && remoteContent.TryGetLoadedPrefabForSkin(skinKey, out var remoteSkinPrefab) && remoteSkinPrefab != null)
                {
                    if (IsRenderablePrefab(remoteSkinPrefab, out string remoteIssue))
                        return remoteSkinPrefab;

                    LogBrokenRemoteSkinPrefabOnce(skinKey, unitType, remoteIssue, remoteContent);
                    return GetPrefab(unitType);
                }

                if (_skinDict == null) Rebuild();
                if (_skinDict.TryGetValue(skinKey, out var s) && s.prefab != null)
                {
                    if (IsRenderablePrefab(s.prefab, out string localIssue))
                        return s.prefab;

                    LogBrokenLocalSkinPrefabOnce(skinKey, unitType, localIssue);
                    return GetPrefab(unitType);
                }

                LogMissingSkinOnce(skinKey, unitType, remoteContent);
                return GetPrefab(unitType);
            }
            return GetPrefab(unitType);
        }

        public bool TryGetUnitTypeForSkin(string skinKey, out string unitType)
        {
            unitType = null;
            if (string.IsNullOrWhiteSpace(skinKey))
                return false;

            if (_skinDict == null) Rebuild();
            if (!_skinDict.TryGetValue(skinKey.Trim(), out var entry) || string.IsNullOrWhiteSpace(entry.unitType))
                return false;

            unitType = entry.unitType.Trim();
            return !string.IsNullOrWhiteSpace(unitType);
        }

        public static bool TryResolveUnitTypeForSkinFromLoadedRegistries(string skinKey, out string unitType)
        {
            unitType = null;
            if (string.IsNullOrWhiteSpace(skinKey))
                return false;

            var registries = Resources.FindObjectsOfTypeAll<UnitPrefabRegistry>();
            for (int i = 0; i < registries.Length; i++)
            {
                var registry = registries[i];
                if (registry == null)
                    continue;

                if (registry.TryGetUnitTypeForSkin(skinKey, out unitType))
                    return true;
            }

            return false;
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

        void LogMissingUnitOnce(string key, RemoteContentManager remoteContent)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(key) ? "<empty>" : key.Trim();
            if (!_loggedMissingUnits.Add(normalizedKey))
                return;

            string message =
                $"[UnitPrefabRegistry] Missing prefab for unit '{normalizedKey}'. " +
                "Runtime no longer substitutes a fallback prefab. Fix the registry entry or remote content manifest.";

            if (IsCriticalUnit(remoteContent, normalizedKey))
                Debug.LogError(message);
            else
                Debug.LogError(message);
        }

        void LogMissingSkinOnce(string skinKey, string unitType, RemoteContentManager remoteContent)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(skinKey) ? "<empty>" : skinKey.Trim();
            if (!_loggedMissingSkins.Add(normalizedKey))
                return;

            string message =
                $"[UnitPrefabRegistry] Missing prefab for skin '{normalizedKey}' on unit '{unitType ?? "<unknown>"}'. " +
                "Falling back to the base unit prefab when one is available.";

            Debug.LogWarning(message);
        }

        void LogBrokenRemoteUnitPrefabOnce(string key, string issue, RemoteContentManager remoteContent)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(key) ? "<empty>" : key.Trim();
            if (!_loggedMissingUnits.Add($"broken:{normalizedKey}"))
                return;

            string message =
                $"[UnitPrefabRegistry] Remote prefab for unit '{normalizedKey}' is broken ({issue}). " +
                "Runtime no longer falls back to the local registry or placeholder geometry.";

            if (IsCriticalUnit(remoteContent, normalizedKey))
                Debug.LogError(message);
            else
                Debug.LogError(message);
        }

        void LogBrokenRemoteSkinPrefabOnce(string skinKey, string unitType, string issue, RemoteContentManager remoteContent)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(skinKey) ? "<empty>" : skinKey.Trim();
            if (!_loggedMissingSkins.Add($"broken:{normalizedKey}"))
                return;

            string message =
                $"[UnitPrefabRegistry] Remote skin prefab '{normalizedKey}' for unit '{unitType ?? "<unknown>"}' is broken ({issue}). " +
                "Falling back to the base unit prefab when one is available.";

            Debug.LogWarning(message);
        }

        void LogBrokenLocalUnitPrefabOnce(string key, string issue)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(key) ? "<empty>" : key.Trim();
            if (!_loggedMissingUnits.Add($"broken_local:{normalizedKey}"))
                return;

            Debug.LogError(
                $"[UnitPrefabRegistry] Local prefab for unit '{normalizedKey}' is broken ({issue}). " +
                "Fix the registry entry so the authoritative unit can materialize correctly.");
        }

        void LogBrokenLocalSkinPrefabOnce(string skinKey, string unitType, string issue)
        {
            string normalizedKey = string.IsNullOrWhiteSpace(skinKey) ? "<empty>" : skinKey.Trim();
            if (!_loggedMissingSkins.Add($"broken_local:{normalizedKey}"))
                return;

            Debug.LogWarning(
                $"[UnitPrefabRegistry] Local skin prefab '{normalizedKey}' for unit '{unitType ?? "<unknown>"}' is broken ({issue}). " +
                "Falling back to the base unit prefab when one is available.");
        }

        static bool IsRenderablePrefab(GameObject prefab, out string issue)
        {
            issue = null;
            if (prefab == null)
            {
                issue = "prefab reference is null";
                return false;
            }

            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                issue = "no renderers found";
                return false;
            }

            int missingControllerCount = 0;
            foreach (var animator in prefab.GetComponentsInChildren<Animator>(true))
            {
                if (animator != null && animator.runtimeAnimatorController == null)
                    missingControllerCount++;
            }

            bool hasDrawableRenderer = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                    continue;

                var materials = renderer.sharedMaterials;
                bool hasMaterials = materials != null && materials.Length > 0;
                if (hasMaterials)
                {
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null)
                        {
                            hasMaterials = false;
                            break;
                        }
                    }
                }

                if (!hasMaterials)
                    continue;

                if (renderer is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh != null)
                    {
                        hasDrawableRenderer = true;
                        break;
                    }

                    continue;
                }

                var filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    hasDrawableRenderer = true;
                    break;
                }
            }

            if (!hasDrawableRenderer)
            {
                issue = missingControllerCount > 0
                    ? $"renderers are missing meshes/materials and {missingControllerCount} animator(s) have no controller"
                    : "renderers are missing meshes/materials";
                return false;
            }

            if (missingControllerCount > 0)
            {
                issue = $"{missingControllerCount} animator(s) have no controller";
                return false;
            }

            return true;
        }

        static bool IsCriticalUnit(RemoteContentManager remoteContent, string unitKey)
        {
            if (remoteContent?.Manifest == null || string.IsNullOrWhiteSpace(unitKey))
                return false;

            var entries = remoteContent.Manifest.t1_content ?? remoteContent.Manifest.critical_content;
            if (entries == null)
                return false;

            foreach (var entry in entries)
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
            if (remoteContent?.Manifest == null || string.IsNullOrWhiteSpace(skinKey))
                return false;

            var entries = remoteContent.Manifest.t1_content ?? remoteContent.Manifest.critical_content;
            if (entries == null)
                return false;

            foreach (var entry in entries)
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
