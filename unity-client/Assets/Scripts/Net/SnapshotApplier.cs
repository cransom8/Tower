// SnapshotApplier.cs — receives game snapshots and stores the latest world state.
// Other scripts (LaneRenderer, InfoBar, TileGrid, etc.) read from SnapshotApplier.Instance.
//
// SETUP:
//   Attach to any persistent GameObject in the ML/Classic game scenes.
//   (Recommend attaching to the NetworkManager GO so it persists across scenes.)

using System;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Net
{
    public class SnapshotApplier : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static SnapshotApplier Instance { get; private set; }

        // ── Current world state ───────────────────────────────────────────────
        public MLSnapshot      LatestML      { get; private set; }
        public ClassicSnapshot LatestClassic { get; private set; }
        public MLMatchReadyPayload LatestMLMatchReady { get; private set; }
        public MLMatchConfig       LatestMLMatchConfig { get; private set; }

        // My lane and viewing lane (ML mode)
        public int MyLaneIndex  { get; set; } = 0;
        public int ViewingLane  { get; set; } = 0;
        public int TotalLanes   { get; set; } = 1;

        // ── Events ────────────────────────────────────────────────────────────
        public event Action<MLSnapshot>      OnMLSnapshotApplied;
        public event Action<ClassicSnapshot> OnClassicSnapshotApplied;

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnEnable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLMatchReady         += HandleMLMatchReady;
            nm.OnMLMatchConfig        += HandleMLMatchConfig;
            nm.OnMLStateSnapshot      += HandleMLSnapshot;
            nm.OnClassicMatchReady    += HandleClassicMatchReady;
            nm.OnClassicStateSnapshot += HandleClassicSnapshot;

            // Catch up: ml_match_config events fire before Game_ML loads, so
            // NetworkManager caches the loadout. Seed LatestMLMatchConfig here
            // so anything that reads it in Start() gets the right data.
            if (LatestMLMatchConfig == null && nm.LastMatchLoadout != null && nm.LastMatchLoadout.Length > 0)
                LatestMLMatchConfig = new MLMatchConfig { loadout = nm.LastMatchLoadout };
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLMatchReady         -= HandleMLMatchReady;
            nm.OnMLMatchConfig        -= HandleMLMatchConfig;
            nm.OnMLStateSnapshot      -= HandleMLSnapshot;
            nm.OnClassicMatchReady    -= HandleClassicMatchReady;
            nm.OnClassicStateSnapshot -= HandleClassicSnapshot;
        }

        // ─────────────────────────────────────────────────────────────────────
        void HandleMLMatchReady(MLMatchReadyPayload p)
        {
            // laneIndex is stored on NetworkManager from ml_room_created/ml_room_joined
            MyLaneIndex = NetworkManager.Instance.MyLaneIndex;
            ViewingLane = MyLaneIndex;
            TotalLanes  = p.playerCount;
            LatestMLMatchReady = p;
            LatestML    = null;
        }

        void HandleMLMatchConfig(MLMatchConfig config)
        {
            LatestMLMatchConfig = config;
        }

        void HandleMLSnapshot(MLSnapshot snap)
        {
            LatestML = snap;
            OnMLSnapshotApplied?.Invoke(snap);
        }

        void HandleClassicMatchReady(ClassicMatchReadyPayload p)
        {
            LatestClassic = null;
        }

        void HandleClassicSnapshot(ClassicSnapshot snap)
        {
            LatestClassic = snap;
            OnClassicSnapshotApplied?.Invoke(snap);
        }

        // ─────────────────────────────────────────────────────────────────────
        /// <summary>Returns the lane snap for a given index, or null.</summary>
        public MLLaneSnap GetLane(int index)
        {
            if (LatestML?.lanes == null) return null;
            if (index < 0 || index >= LatestML.lanes.Length) return null;
            return LatestML.lanes[index];
        }

        public MLLaneSnap MyLane     => GetLane(MyLaneIndex);
        public MLLaneSnap ViewedLane => GetLane(ViewingLane);
        public MLBattlefieldTopology CurrentBattlefieldTopology
            => LatestML?.battlefieldTopology
            ?? LatestMLMatchConfig?.battlefieldTopology
            ?? LatestMLMatchReady?.battlefieldTopology;

        public MLLaneAssignment GetLaneAssignment(int index)
        {
            var assignments = LatestMLMatchReady?.laneAssignments;
            if (assignments == null) return null;
            for (int i = 0; i < assignments.Length; i++)
            {
                var a = assignments[i];
                if (a != null && a.laneIndex == index) return a;
            }
            return null;
        }

        public bool AreLanesAllied(int laneA, int laneB)
        {
            var a = GetLane(laneA);
            var b = GetLane(laneB);
            if (a != null && b != null && !string.IsNullOrEmpty(a.team) && !string.IsNullOrEmpty(b.team))
                return string.Equals(a.team, b.team, StringComparison.OrdinalIgnoreCase);

            var aa = GetLaneAssignment(laneA);
            var bb = GetLaneAssignment(laneB);
            if (aa != null && bb != null && !string.IsNullOrEmpty(aa.team) && !string.IsNullOrEmpty(bb.team))
                return string.Equals(aa.team, bb.team, StringComparison.OrdinalIgnoreCase);

            return laneA == laneB;
        }

        public Color GetLaneColor(int laneIndex, Color fallback)
        {
            var lane = GetLane(laneIndex);
            if (lane != null && TryResolveSlotColor(lane.slotColor, out var laneColor))
                return laneColor;

            var assignment = GetLaneAssignment(laneIndex);
            if (assignment != null && TryResolveSlotColor(assignment.slotColor, out var assignmentColor))
                return assignmentColor;

            return fallback;
        }

        public static bool TryResolveSlotColor(string slotColor, out Color color)
        {
            switch ((slotColor ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "red":
                    color = new Color(0.86f, 0.25f, 0.22f);
                    return true;
                case "gold":
                case "yellow":
                    color = new Color(0.92f, 0.74f, 0.20f);
                    return true;
                case "blue":
                    color = new Color(0.24f, 0.50f, 0.92f);
                    return true;
                case "green":
                    color = new Color(0.20f, 0.72f, 0.42f);
                    return true;
                default:
                    if (!string.IsNullOrWhiteSpace(slotColor) && ColorUtility.TryParseHtmlString(slotColor, out color))
                        return true;
                    color = Color.white;
                    return false;
            }
        }
    }
}
