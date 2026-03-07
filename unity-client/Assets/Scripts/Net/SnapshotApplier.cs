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
            nm.OnMLStateSnapshot      += HandleMLSnapshot;
            nm.OnClassicMatchReady    += HandleClassicMatchReady;
            nm.OnClassicStateSnapshot += HandleClassicSnapshot;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLMatchReady         -= HandleMLMatchReady;
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
            LatestML    = null;
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

        /// <summary>
        /// Called by SurvivalManager to inject a synthesized ML snapshot so that
        /// LaneRenderer, InfoBar, TileGrid, and other ML-mode scripts work in the
        /// Game_Survival scene without modification.
        /// </summary>
        public void InjectML(MLSnapshot snap)
        {
            HandleMLSnapshot(snap);
        }
    }
}
