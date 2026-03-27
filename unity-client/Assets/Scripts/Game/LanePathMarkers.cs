using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    /// <summary>
    /// Drag-and-drop world-space path markers for a lane's branch path.
    /// Units can sample these markers to travel from the center spawn toward
    /// their private lane regardless of the current map layout.
    /// </summary>
    public class LanePathMarkers : MonoBehaviour
    {
        static readonly Dictionary<int, HashSet<LanePathMarkers>> ActiveByLane = new();
        static readonly Dictionary<string, HashSet<LanePathMarkers>> ActiveByKey = new(StringComparer.OrdinalIgnoreCase);
        static readonly List<Vector3> SamplePoints = new();
        static readonly HashSet<string> LoggedRouteFailures = new(StringComparer.OrdinalIgnoreCase);

        [Tooltip("Branch/lane index this marker set drives (0-3).")]
        public int LaneIndex;

        [Tooltip("Center spawn marker for this lane route. Assign PB_Mine_Center here.")]
        public Transform CenterMarker;

        [Tooltip("Optional unique path key. Use this when a lane needs multiple path variants.")]
        public string PathKey;

        [Tooltip("Drag five world markers here in the order units should follow them.")]
        public Transform[] MarkerTransforms = new Transform[5];

        [Header("Debug")]
        public bool DrawGizmos = true;
        public Color GizmoColor = new(1f, 0.75f, 0.2f, 1f);

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            Register();
        }
        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            Unregister();
        }

        void OnValidate()
        {
            // Legacy marker registration is no longer part of the live route graph.
            // Keep the component editable in scenes without spamming duplicate
            // registration errors during editor validation and domain reloads.
            if (!Application.isPlaying)
            {
                Unregister();
                return;
            }

            Register();
        }

        void Register()
        {
            Unregister();
            RegisterLane(this);
            if (!string.IsNullOrWhiteSpace(PathKey))
                RegisterKey(PathKey.Trim(), this);
        }

        public static bool TrySample(int laneIndex, float normalizedProgress, out Vector3 worldPos)
            => TrySample(laneIndex, null, normalizedProgress, out worldPos);

        public static bool TrySample(int laneIndex, string pathKey, float normalizedProgress, out Vector3 worldPos)
            => TrySampleDetailed(laneIndex, pathKey, normalizedProgress, out worldPos, out _, out _);

        public static bool TrySampleRoute(
            int laneIndex,
            string primaryPathKey,
            string secondaryPathKey,
            float normalizedProgress,
            out Vector3 worldPos,
            out string resolvedMarkerName,
            out string resolvedLookupKey,
            out bool usedFallback,
            out string failureReason)
        {
            Debug.Log(
                $"[SpawnAudit][PathMode] using LanePathMarkers method={nameof(TrySampleRoute)} " +
                $"unitId='<unknown>' lane={laneIndex} primaryKey='{primaryPathKey ?? "<none>"}' " +
                $"secondaryKey='{secondaryPathKey ?? "<none>"}' normalizedProgress={normalizedProgress:0.###}");
            worldPos = Vector3.zero;
            resolvedMarkerName = null;
            resolvedLookupKey = null;
            usedFallback = false;
            failureReason = null;
            string primaryFailure = null;
            string secondaryFailure = "<skipped>";
            string laneFailure = null;

            if (TrySampleCandidate(laneIndex, primaryPathKey, normalizedProgress, out worldPos, out resolvedMarkerName, out primaryFailure))
            {
                resolvedLookupKey = string.IsNullOrWhiteSpace(primaryPathKey) ? $"lane:{laneIndex}" : primaryPathKey.Trim();
                return true;
            }

            bool hasSecondary = !string.IsNullOrWhiteSpace(secondaryPathKey)
                && !string.Equals(primaryPathKey?.Trim(), secondaryPathKey.Trim(), StringComparison.OrdinalIgnoreCase);
            if (hasSecondary
                && TrySampleCandidate(laneIndex, secondaryPathKey, normalizedProgress, out worldPos, out resolvedMarkerName, out secondaryFailure))
            {
                resolvedLookupKey = secondaryPathKey.Trim();
                usedFallback = true;
                return true;
            }

            if (TrySampleCandidate(laneIndex, null, normalizedProgress, out worldPos, out resolvedMarkerName, out laneFailure))
            {
                resolvedLookupKey = $"lane:{laneIndex}";
                usedFallback = !string.IsNullOrWhiteSpace(primaryPathKey) || hasSecondary;
                return true;
            }

            failureReason =
                $"primary='{DescribeLookup(primaryPathKey, laneIndex)}' failed: {primaryFailure ?? "<unknown>"}; " +
                $"secondary='{DescribeLookup(hasSecondary ? secondaryPathKey : null, laneIndex)}' failed: {(hasSecondary ? secondaryFailure : "<skipped>")}; " +
                $"lane fallback failed: {laneFailure ?? "<unknown>"}";
            LogRouteFailureOnce(
                $"route:{laneIndex}:{primaryPathKey?.Trim() ?? "<none>"}:{secondaryPathKey?.Trim() ?? "<none>"}",
                failureReason);
            return false;
        }

        public static bool TrySampleDetailed(int laneIndex, string pathKey, float normalizedProgress, out Vector3 worldPos, out string resolvedMarkerName, out string failureReason)
        {
            worldPos = Vector3.zero;
            resolvedMarkerName = null;
            failureReason = null;

            if (!TryResolveMarkers(laneIndex, pathKey, out LanePathMarkers markers, out failureReason, logFailure: true))
                return false;

            if (!markers.BuildPointList(SamplePoints, out failureReason) || SamplePoints.Count < 2)
                return false;

            resolvedMarkerName = markers.BuildResolvedMarkerName();

            float t = Mathf.Clamp01(normalizedProgress);
            float totalLen = 0f;
            for (int i = 0; i < SamplePoints.Count - 1; i++)
                totalLen += Vector3.Distance(SamplePoints[i], SamplePoints[i + 1]);

            if (totalLen <= 0.001f)
            {
                worldPos = SamplePoints[0];
                return true;
            }

            float target = t * totalLen;
            float walked = 0f;
            for (int i = 0; i < SamplePoints.Count - 1; i++)
            {
                float segLen = Vector3.Distance(SamplePoints[i], SamplePoints[i + 1]);
                if (walked + segLen >= target)
                {
                    float segT = segLen > 0.001f ? (target - walked) / segLen : 0f;
                    worldPos = Vector3.Lerp(SamplePoints[i], SamplePoints[i + 1], segT);
                    return true;
                }
                walked += segLen;
            }

            worldPos = SamplePoints[SamplePoints.Count - 1];
            return true;
        }

        bool BuildPointList(List<Vector3> points, out string failureReason)
        {
            points.Clear();
            failureReason = null;
            if (CenterMarker == null)
            {
                failureReason = $"LanePathMarkers '{name}' is missing CenterMarker.";
                return false;
            }

            points.Add(CenterMarker.position);
            if (MarkerTransforms == null || MarkerTransforms.Length != 5)
            {
                failureReason = $"LanePathMarkers '{name}' must define exactly 5 explicit MarkerTransforms.";
                return false;
            }

            for (int i = 0; i < MarkerTransforms.Length; i++)
            {
                var marker = MarkerTransforms[i];
                if (marker == null)
                {
                    failureReason = $"LanePathMarkers '{name}' is missing Marker_{i + 1}.";
                    return false;
                }

                points.Add(marker.position);
            }

            return points.Count >= 2;
        }

        string BuildResolvedMarkerName()
        {
            string key = !string.IsNullOrWhiteSpace(PathKey)
                ? PathKey.Trim()
                : $"lane:{LaneIndex}";
            return $"{name} [{key}]";
        }

        static bool TryResolveMarkers(int laneIndex, string pathKey, out LanePathMarkers markers, out string failureReason)
            => TryResolveMarkers(laneIndex, pathKey, out markers, out failureReason, logFailure: true);

        static bool TryResolveMarkers(int laneIndex, string pathKey, out LanePathMarkers markers, out string failureReason, bool logFailure)
        {
            markers = null;
            failureReason = null;

            if (!string.IsNullOrWhiteSpace(pathKey))
            {
                string trimmedKey = pathKey.Trim();
                if (!TryResolveFromRegistry(ActiveByKey, trimmedKey, out markers, out failureReason))
                {
                    if (logFailure)
                        LogRouteFailureOnce($"key:{trimmedKey}", failureReason);
                    return false;
                }

                return true;
            }

            if (!TryResolveFromRegistry(ActiveByLane, laneIndex, out markers, out failureReason))
            {
                if (logFailure)
                    LogRouteFailureOnce($"lane:{laneIndex}", failureReason);
                return false;
            }

            return true;
        }

        static bool TryResolveFromRegistry<TKey>(Dictionary<TKey, HashSet<LanePathMarkers>> registry, TKey key, out LanePathMarkers markers, out string failureReason)
        {
            markers = null;
            failureReason = null;
            if (!registry.TryGetValue(key, out var registrations) || registrations == null || registrations.Count == 0)
            {
                failureReason = $"No LanePathMarkers registration found for '{key}'.";
                return false;
            }

            if (registrations.Count > 1)
            {
                failureReason = $"Ambiguous LanePathMarkers registration for '{key}'. Found {registrations.Count} active marker sets.";
                return false;
            }

            foreach (var entry in registrations)
            {
                markers = entry;
                break;
            }

            if (markers == null)
            {
                failureReason = $"LanePathMarkers registry for '{key}' contained only null entries.";
                return false;
            }

            return true;
        }

        static void RegisterLane(LanePathMarkers markers)
        {
            if (!ActiveByLane.TryGetValue(markers.LaneIndex, out var registrations))
            {
                registrations = new HashSet<LanePathMarkers>();
                ActiveByLane[markers.LaneIndex] = registrations;
            }

            registrations.Add(markers);
            if (registrations.Count > 1)
                LogRouteFailureOnce($"lane:{markers.LaneIndex}", $"Ambiguous LanePathMarkers registration for lane '{markers.LaneIndex}'.");
        }

        static void RegisterKey(string pathKey, LanePathMarkers markers)
        {
            if (!ActiveByKey.TryGetValue(pathKey, out var registrations))
            {
                registrations = new HashSet<LanePathMarkers>();
                ActiveByKey[pathKey] = registrations;
            }

            registrations.Add(markers);
            if (registrations.Count > 1)
                LogRouteFailureOnce($"key:{pathKey}", $"Ambiguous LanePathMarkers registration for path key '{pathKey}'.");
        }

        void Unregister()
        {
            UnregisterFromRegistry(ActiveByLane, LaneIndex, this);
            if (!string.IsNullOrWhiteSpace(PathKey))
                UnregisterFromRegistry(ActiveByKey, PathKey.Trim(), this);
        }

        static void UnregisterFromRegistry<TKey>(Dictionary<TKey, HashSet<LanePathMarkers>> registry, TKey key, LanePathMarkers markers)
        {
            if (!registry.TryGetValue(key, out var registrations) || registrations == null)
                return;

            registrations.Remove(markers);
            if (registrations.Count == 0)
                registry.Remove(key);
        }

        static void LogRouteFailureOnce(string key, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            if (!LoggedRouteFailures.Add(key))
                return;

            Debug.LogError($"[LanePathMarkers] {message}");
        }

        static bool TrySampleCandidate(int laneIndex, string pathKey, float normalizedProgress, out Vector3 worldPos, out string resolvedMarkerName, out string failureReason)
        {
            worldPos = Vector3.zero;
            resolvedMarkerName = null;
            failureReason = null;

            if (!TryResolveMarkers(laneIndex, pathKey, out LanePathMarkers markers, out failureReason, logFailure: false))
                return false;

            if (!markers.BuildPointList(SamplePoints, out failureReason) || SamplePoints.Count < 2)
                return false;

            resolvedMarkerName = markers.BuildResolvedMarkerName();

            float t = Mathf.Clamp01(normalizedProgress);
            float totalLen = 0f;
            for (int i = 0; i < SamplePoints.Count - 1; i++)
                totalLen += Vector3.Distance(SamplePoints[i], SamplePoints[i + 1]);

            if (totalLen <= 0.001f)
            {
                worldPos = SamplePoints[0];
                return true;
            }

            float target = t * totalLen;
            float walked = 0f;
            for (int i = 0; i < SamplePoints.Count - 1; i++)
            {
                float segLen = Vector3.Distance(SamplePoints[i], SamplePoints[i + 1]);
                if (walked + segLen >= target)
                {
                    float segT = segLen > 0.001f ? (target - walked) / segLen : 0f;
                    worldPos = Vector3.Lerp(SamplePoints[i], SamplePoints[i + 1], segT);
                    return true;
                }
                walked += segLen;
            }

            worldPos = SamplePoints[SamplePoints.Count - 1];
            return true;
        }

        static string DescribeLookup(string pathKey, int laneIndex)
        {
            return !string.IsNullOrWhiteSpace(pathKey)
                ? pathKey.Trim()
                : $"lane:{laneIndex}";
        }

#if UNITY_EDITOR
        public static void DebugClearRegistry()
        {
            ActiveByLane.Clear();
            ActiveByKey.Clear();
            LoggedRouteFailures.Clear();
        }
#endif

        void OnDrawGizmos()
        {
            if (!DrawGizmos)
                return;

            if (!BuildPointList(SamplePoints, out _) || SamplePoints.Count == 0)
                return;

            Gizmos.color = GizmoColor;
            for (int i = 0; i < SamplePoints.Count; i++)
            {
                Gizmos.DrawSphere(SamplePoints[i], 0.45f);
                if (i < SamplePoints.Count - 1)
                    Gizmos.DrawLine(SamplePoints[i], SamplePoints[i + 1]);
            }
        }
    }
}
