using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class FortressPadAnchor : MonoBehaviour
    {
        public enum LaneColor
        {
            Any = 0,
            Red = 1,
            Gold = 2,
            Blue = 3,
            Green = 4,
        }

        [Header("Identity")]
        public string padId;
        public string[] padAliases;
        public string buildingType;
        public LaneColor laneColor = LaneColor.Any;

        [Header("Optional Focus")]
        public Transform focusTransform;
        public Transform labelTransform;

        [Header("Optional Visual Overrides")]
        public GameObject ghostVisualRoot;
        public GameObject builtVisualRoot;
        public Renderer[] explicitRenderers;

        [Header("Interaction")]
        public Collider interactionCollider;
        public bool autoSizeInteractionCollider = true;
        public Vector3 minimumColliderSize = new(2.2f, 2.4f, 2.2f);

        static readonly List<FortressPadAnchor> s_activeAnchors = new();
        static readonly HashSet<string> s_invalidConfigLogs = new();
        static readonly HashSet<string> s_missingRendererLogs = new();

        Renderer[] _cachedRenderers;
        Renderer[] _cachedGhostRenderers;
        Renderer[] _cachedBuiltRenderers;

        public string PadId => padId;
        public string BuildingType => buildingType;
        public LaneColor AnchorLaneColor => laneColor;
        public Transform FocusTransform => focusTransform != null ? focusTransform : transform;
        public Transform LabelTransform => labelTransform != null ? labelTransform : FocusTransform;
        public GameObject GhostVisualRoot => ghostVisualRoot;
        public GameObject BuiltVisualRoot => builtVisualRoot;

        void OnEnable()
        {
            if (!s_activeAnchors.Contains(this))
                s_activeAnchors.Add(this);

            if (IsRuntimeBindingPending())
                return;

            ValidateIdentity();
            EnsureInteractionCollider();
            EnsureRuntimeHud();
        }

        void OnDisable()
        {
            s_activeAnchors.Remove(this);
        }

        void OnValidate()
        {
            if (IsRuntimeBindingPending())
                return;

            ValidateIdentity();
            if (!Application.isPlaying)
                EnsureInteractionCollider();
        }

        public void FinalizeRuntimeBinding()
        {
            _cachedRenderers = null;
            _cachedGhostRenderers = null;
            _cachedBuiltRenderers = null;
            ValidateIdentity();
            EnsureInteractionCollider();
            EnsureRuntimeHud();

            var marker = GetComponent<FortressPadRuntimeBindingMarker>();
            if (marker != null)
                Destroy(marker);
        }

        public bool MatchesPad(string padId)
        {
            if (string.IsNullOrWhiteSpace(padId))
                return false;

            return string.Equals(this.padId, padId, StringComparison.OrdinalIgnoreCase);
        }

        public bool MatchesLane(string slotColor, int laneIndex)
        {
            return FortressLaneResolver.MatchesLane(transform, laneColor, slotColor, laneIndex);
        }

        public Renderer[] GetPrimaryRenderers()
        {
            if (_cachedRenderers == null)
                _cachedRenderers = ResolveRenderers(explicitRenderers, renderRoot: gameObject);
            return _cachedRenderers;
        }

        public Renderer[] GetGhostRenderers()
        {
            if (_cachedGhostRenderers == null)
                _cachedGhostRenderers = ResolveRenderers(null, ghostVisualRoot);
            return _cachedGhostRenderers;
        }

        public Renderer[] GetBuiltRenderers()
        {
            if (_cachedBuiltRenderers == null)
                _cachedBuiltRenderers = ResolveRenderers(null, builtVisualRoot);
            return _cachedBuiltRenderers;
        }

        public Collider EnsureInteractionCollider()
        {
            if (interactionCollider == null)
                interactionCollider = GetComponent<Collider>();

            if (interactionCollider == null)
            {
                Debug.LogError(
                    $"[FortressPadAnchor] Missing collider on authored pad '{GetScenePath()}'. " +
                    "Add a collider to the real scene object; no runtime substitute will be created.",
                    this);
                return null;
            }

            if (autoSizeInteractionCollider && interactionCollider is BoxCollider box)
                ApplyBoundsToBoxCollider(box);

            interactionCollider.isTrigger = true;
            return interactionCollider;
        }

        public Bounds GetWorldBounds()
        {
            var renderers = GetPrimaryRenderers();
            if (renderers == null || renderers.Length == 0)
            {
                LogMissingRenderers();
                Vector3 focus = FocusTransform != null ? FocusTransform.position : transform.position;
                return new Bounds(focus, minimumColliderSize);
            }

            bool hasBounds = false;
            Bounds worldBounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    worldBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    worldBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                LogMissingRenderers();
                Vector3 focus = FocusTransform != null ? FocusTransform.position : transform.position;
                worldBounds = new Bounds(focus, minimumColliderSize);
            }

            return worldBounds;
        }

        public static FortressPadAnchor FindAnchor(string padId, string slotColor, int laneIndex)
        {
            for (int i = 0; i < s_activeAnchors.Count; i++)
            {
                var anchor = s_activeAnchors[i];
                if (anchor == null || !anchor.MatchesLane(slotColor, laneIndex) || !anchor.MatchesPad(padId))
                    continue;

                return anchor;
            }

            return null;
        }

        public static int CollectAnchors(List<FortressPadAnchor> results, string slotColor = null, int laneIndex = -1)
        {
            if (results == null)
                return 0;

            results.Clear();
            bool filterByLane = laneIndex >= 0 || !string.IsNullOrWhiteSpace(slotColor);

            for (int i = 0; i < s_activeAnchors.Count; i++)
            {
                var anchor = s_activeAnchors[i];
                if (anchor == null || !anchor.isActiveAndEnabled)
                    continue;

                if (filterByLane && !anchor.MatchesLane(slotColor, laneIndex))
                    continue;

                results.Add(anchor);
            }

            return results.Count;
        }

        public static string NormalizeLaneKey(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "red":
                    return "red";
                case "gold":
                case "yellow":
                    return "yellow";
                case "blue":
                    return "blue";
                case "green":
                    return "green";
                default:
                    return string.Empty;
            }
        }

        public static string LaneIndexToLaneKey(int laneIndex)
        {
            return laneIndex switch
            {
                0 => "red",
                1 => "yellow",
                2 => "blue",
                3 => "green",
                _ => string.Empty,
            };
        }

        static string LaneColorToLaneKey(LaneColor color)
        {
            return color switch
            {
                LaneColor.Red => "red",
                LaneColor.Gold => "yellow",
                LaneColor.Blue => "blue",
                LaneColor.Green => "green",
                _ => string.Empty,
            };
        }

        static Renderer[] ResolveRenderers(Renderer[] explicitRenderers, GameObject renderRoot)
        {
            if (explicitRenderers != null && explicitRenderers.Length > 0)
            {
                var filtered = new List<Renderer>(explicitRenderers.Length);
                for (int i = 0; i < explicitRenderers.Length; i++)
                {
                    if (explicitRenderers[i] != null)
                        filtered.Add(explicitRenderers[i]);
                }

                if (filtered.Count > 0)
                    return filtered.ToArray();
            }

            if (renderRoot == null)
                return Array.Empty<Renderer>();

            return renderRoot.GetComponentsInChildren<Renderer>(true);
        }

        void EnsureRuntimeHud()
        {
            if (!Application.isPlaying)
                return;

            _ = GetComponent<FortressPadHealthView>() ?? gameObject.AddComponent<FortressPadHealthView>();
        }

        void ApplyBoundsToBoxCollider(BoxCollider box)
        {
            Bounds localBounds = CalculateLocalBounds();
            Vector3 size = localBounds.size;
            size.x = Mathf.Max(minimumColliderSize.x, size.x);
            size.y = Mathf.Max(minimumColliderSize.y, size.y);
            size.z = Mathf.Max(minimumColliderSize.z, size.z);

            box.center = localBounds.center;
            box.size = size;
        }

        Bounds CalculateLocalBounds()
        {
            var renderers = GetPrimaryRenderers();
            bool hasBounds = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var bounds = renderer.bounds;
                var corners = GetCorners(bounds);
                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    Vector3 localPoint = worldToLocal.MultiplyPoint3x4(corners[cornerIndex]);
                    if (!hasBounds)
                    {
                        min = max = localPoint;
                        hasBounds = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, localPoint);
                        max = Vector3.Max(max, localPoint);
                    }
                }
            }

            if (!hasBounds)
            {
                LogMissingRenderers();
                return new Bounds(Vector3.zero, minimumColliderSize);
            }

            var result = new Bounds((min + max) * 0.5f, max - min);
            if (result.size.sqrMagnitude < 0.001f)
                result.size = minimumColliderSize;
            return result;
        }

        static Vector3[] GetCorners(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(max.x, max.y, max.z),
            };
        }

        string GetScenePath()
        {
            var current = transform;
            var path = current.name;
            while (current.parent != null)
            {
                current = current.parent;
                path = $"{current.name}/{path}";
            }

            return path;
        }

        void ValidateIdentity()
        {
            string scenePath = GetScenePath();
            if (string.IsNullOrWhiteSpace(padId))
            {
                string key = $"padId::{scenePath}";
                if (s_invalidConfigLogs.Add(key))
                {
                    Debug.LogError(
                        $"[FortressPadAnchor] Missing padId on authored pad '{scenePath}'. " +
                        "Assign the real snapshot padId; no fallback mapping will be used.",
                        this);
                }
            }

            if (laneColor == LaneColor.Any)
            {
                string key = $"laneColor::{scenePath}";
                if (s_invalidConfigLogs.Add(key))
                {
                    Debug.LogError(
                        $"[FortressPadAnchor] Missing explicit laneColor on authored pad '{scenePath}'. " +
                        "Assign the correct team lane; catch-all matching is disabled.",
                        this);
                }
            }
        }

        void LogMissingRenderers()
        {
            string key = GetScenePath();
            if (s_missingRendererLogs.Add(key))
            {
                Debug.LogError(
                    $"[FortressPadAnchor] No renderers were found on authored pad '{key}'. " +
                    "Collider sizing and focus framing require real scene renderers.",
                    this);
            }
        }

        bool IsRuntimeBindingPending()
        {
            return Application.isPlaying
                && GetComponent<FortressPadRuntimeBindingMarker>() != null;
        }
    }
}
