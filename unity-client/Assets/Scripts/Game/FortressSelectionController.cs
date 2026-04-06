using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Game
{
    [DefaultExecutionOrder(200)]
    public sealed class FortressSelectionController : MonoBehaviour
    {
        [SerializeField] Camera worldCamera;
        [SerializeField] float clickDragThreshold = 12f;
        [SerializeField] bool verboseDebugLogs = true;

        static FortressSelectionController s_instance;
        static readonly RaycastHit[] s_hitBuffer = new RaycastHit[32];
        static readonly List<FortressPadAnchor> s_anchorScratch = new();
        static readonly List<BarracksSiteView> s_barracksScratch = new();
        readonly HashSet<string> _missingPadLogs = new();
        readonly HashSet<string> _missingBarracksLogs = new();

        Vector3 _mouseDownPos;
        bool _wasDrag;
        int _selectedLaneIndex = -1;
        string _selectedBarracksId;

        void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(this);
                return;
            }

            s_instance = this;
        }

        void OnEnable()
        {
            SubscribeSnapshots();
            SyncBarracksSelection();
            ValidateBindings();
        }

        void OnDisable()
        {
            UnsubscribeSnapshots();
            if (s_instance == this)
                s_instance = null;
        }

        void Update()
        {
            if (!IsFortressModeActive())
                return;

            HandlePointer();
        }

        void SubscribeSnapshots()
        {
            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null)
                snapshotApplier.OnMLSnapshotApplied += HandleSnapshotApplied;
        }

        void UnsubscribeSnapshots()
        {
            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null)
                snapshotApplier.OnMLSnapshotApplied -= HandleSnapshotApplied;
        }

        void HandleSnapshotApplied(MLSnapshot _)
        {
            ValidateBindings();
            SyncBarracksSelection();
        }

        void HandlePointer()
        {
            if (Input.touchCount > 0)
            {
                HandleTouchPointer();
                return;
            }

            HandleMousePointer();
        }

        void HandleMousePointer()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _mouseDownPos = Input.mousePosition;
                _wasDrag = false;
            }

            if (Input.GetMouseButton(0) && Vector3.Distance(Input.mousePosition, _mouseDownPos) > clickDragThreshold)
                _wasDrag = true;

            if (!Input.GetMouseButtonUp(0))
                return;

            if (_wasDrag || CameraController.IsLmbPanning || IsPointerOverUi(Input.mousePosition))
                return;

            TryHandleSelection(Input.mousePosition);
        }

        void HandleTouchPointer()
        {
            Touch touch = Input.GetTouch(0);
            switch (touch.phase)
            {
                case TouchPhase.Began:
                    _mouseDownPos = touch.position;
                    _wasDrag = false;
                    return;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (Vector2.Distance(touch.position, new Vector2(_mouseDownPos.x, _mouseDownPos.y)) > clickDragThreshold)
                        _wasDrag = true;
                    return;
                case TouchPhase.Canceled:
                    _wasDrag = false;
                    return;
                case TouchPhase.Ended:
                    if (_wasDrag || CameraController.IsLmbPanning || IsPointerOverUi(touch.position, touch.fingerId))
                        return;

                    TryHandleSelection(touch.position);
                    return;
            }
        }

        bool TryHandleSelection(Vector3 screenPos)
        {
            if (!TryResolveInteractiveLane(out var lane))
                return false;

            var cameraToUse = ResolveCamera();
            if (cameraToUse == null)
            {
                Debug.LogError("[FortressSelection] Missing world camera. Fortress selection cannot run.");
                return false;
            }

            Ray ray = cameraToUse.ScreenPointToRay(screenPos);
            int hitCount = Physics.RaycastNonAlloc(ray, s_hitBuffer, 512f, ~0, QueryTriggerInteraction.Collide);
            if (hitCount <= 0)
            {
                if (verboseDebugLogs)
                    Debug.Log("[FortressSelection] Click did not hit any fortress-interactive collider.");
                return false;
            }

            BarracksSiteView bestBarracks = null;
            FortressPadAnchor bestPad = null;
            float bestBarracksDistance = float.MaxValue;
            float bestPadDistance = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                var hit = s_hitBuffer[i];
                var collider = hit.collider;
                var hitObject = collider != null ? collider.gameObject : null;
                var barracksView = collider != null ? collider.GetComponentInParent<BarracksSiteView>() : null;
                var padAnchor = collider != null ? collider.GetComponentInParent<FortressPadAnchor>() : null;
                if (verboseDebugLogs)
                {
                    Debug.Log(
                        $"[FortressSelection] Raycast hit '{(hitObject != null ? hitObject.name : "<null>")}' " +
                        $"distance={hit.distance:0.00} " +
                        $"BarracksSiteView={(barracksView != null)} " +
                        $"FortressPadAnchor={(padAnchor != null)}");
                }

                if (barracksView != null && barracksView.MatchesLane(lane.slotColor, lane.laneIndex) && hit.distance < bestBarracksDistance)
                {
                    bestBarracksDistance = hit.distance;
                    bestBarracks = barracksView;
                }

                if (padAnchor != null && padAnchor.MatchesLane(lane.slotColor, lane.laneIndex) && hit.distance < bestPadDistance)
                {
                    bestPadDistance = hit.distance;
                    bestPad = padAnchor;
                }
            }

            if (bestBarracks != null)
            {
                Debug.Log(
                    $"[BarracksTrace][ClientClick] clickedObject='{bestBarracks.gameObject.name}' " +
                    $"lane={lane.laneIndex} slotColor='{lane.slotColor}' resolvedBarracksId='{bestBarracks.BarracksId}'");
                return SelectBarracks(bestBarracks, lane);
            }

            if (bestPad != null)
            {
                Debug.Log(
                    $"[BarracksTrace][ClientClick] clickedObject='{bestPad.gameObject.name}' " +
                    $"lane={lane.laneIndex} slotColor='{lane.slotColor}' mode='pad' padId='{bestPad.PadId}'");
                return SelectPad(bestPad, lane);
            }

            if (verboseDebugLogs)
            {
                Debug.Log(
                    "[FortressSelection] Click hit world geometry, but none of the colliders belonged to a selectable " +
                    "fortress pad or barracks site for the active lane.");
            }
            return false;
        }

        bool SelectBarracks(BarracksSiteView barracksView, MLLaneSnap lane)
        {
            if (barracksView == null || lane == null)
                return false;

            var site = SnapshotApplier.Instance?.GetBarracksSite(lane.laneIndex, barracksView.BarracksId);
            if (site == null)
            {
                Debug.LogError(
                    $"[FortressSelection] Selection failed for barracks '{barracksView.BarracksId}' on lane '{lane.slotColor}'. " +
                    "Snapshot data for that authored barracks object was not found.",
                    barracksView);
                return false;
            }

            _selectedLaneIndex = lane.laneIndex;
            _selectedBarracksId = barracksView.BarracksId;
            SyncBarracksSelection();

            if (verboseDebugLogs)
            {
                Debug.Log(
                    $"[FortressSelection] Selected barracks '{barracksView.BarracksId}' on lane '{lane.slotColor}' " +
                    $"from '{barracksView.name}'.");
            }
            Debug.Log(
                $"[BarracksTrace][ClientSelect] lane={lane.laneIndex} slotColor='{lane.slotColor}' " +
                $"selectedBarracksId='{barracksView.BarracksId}' object='{barracksView.gameObject.name}'");

            PanCameraTo(barracksView.GetFocusWorldPosition());

            var panel = FindBarracksPanel();
            if (panel == null)
            {
                Debug.LogError("[FortressSelection] BarracksPanel not found. Barracks selection succeeded but no panel could be opened.");
                return false;
            }

            panel.ShowForBarracks(barracksView.BarracksId);
            return true;
        }

        bool SelectPad(FortressPadAnchor padAnchor, MLLaneSnap lane)
        {
            if (padAnchor == null || lane == null)
                return false;

            var pad = FindPad(lane, padAnchor.PadId);
            if (pad == null)
            {
                Debug.LogError(
                    $"[FortressSelection] Selection failed for pad '{padAnchor.PadId}' on lane '{lane.slotColor}'. " +
                    "Snapshot data for that authored pad anchor was not found.",
                    padAnchor);
                return false;
            }

            _selectedLaneIndex = -1;
            _selectedBarracksId = null;
            SyncBarracksSelection();

            if (verboseDebugLogs)
            {
                Debug.Log(
                    $"[FortressSelection] Selected pad '{padAnchor.PadId}' ({padAnchor.BuildingType}) " +
                    $"on lane '{lane.slotColor}' from '{padAnchor.name}'.");
            }
            Debug.Log(
                $"[BarracksTrace][ClientSelect] lane={lane.laneIndex} slotColor='{lane.slotColor}' " +
                $"selectedPadId='{padAnchor.PadId}' object='{padAnchor.gameObject.name}'");

            PanCameraTo(padAnchor.FocusTransform.position);

            var panel = FindBarracksPanel();
            if (panel == null)
            {
                Debug.LogError("[FortressSelection] BarracksPanel not found. Pad selection succeeded but no panel could be opened.");
                return false;
            }

            panel.ShowForPad(padAnchor.PadId);
            return true;
        }

        void ValidateBindings()
        {
            if (!TryResolveInteractiveLane(out var lane))
                return;

            // Remote environments arrive asynchronously, so skip validation until authored
            // fortress objects actually exist in the live scene.
            int activeAnchorCount = FortressPadAnchor.CollectAnchors(s_anchorScratch);
            int activeBarracksCount = BarracksSiteView.CollectSites(s_barracksScratch);
            if (activeAnchorCount == 0 && activeBarracksCount == 0)
                return;

            if (lane.fortressPads != null)
            {
                for (int i = 0; i < lane.fortressPads.Length; i++)
                {
                    var pad = lane.fortressPads[i];
                    if (pad == null || string.IsNullOrWhiteSpace(pad.padId))
                        continue;

                    var anchor = FortressPadAnchor.FindAnchor(pad.padId, lane.slotColor, lane.laneIndex);
                    if (anchor == null)
                    {
                        string key = $"{lane.slotColor}::{pad.padId}";
                        if (_missingPadLogs.Add(key))
                        {
                            Debug.LogError(
                                $"[FortressSelection] Missing authored FortressPadAnchor for lane '{lane.slotColor}' " +
                                $"pad '{pad.padId}' ({pad.buildingType}). No substitute will be created.");
                        }
                    }
                }
            }

            if (lane.barracksSites != null)
            {
                for (int i = 0; i < lane.barracksSites.Length; i++)
                {
                    var site = lane.barracksSites[i];
                    if (site == null || string.IsNullOrWhiteSpace(site.barracksId))
                        continue;

                    var barracksView = BarracksSiteView.FindSite(site.barracksId, lane.slotColor, lane.laneIndex);
                    if (barracksView == null)
                    {
                        string key = $"{lane.slotColor}::{site.barracksId}";
                        if (_missingBarracksLogs.Add(key))
                        {
                            Debug.LogError(
                                $"[FortressSelection] Missing authored BarracksSiteView for lane '{lane.slotColor}' " +
                                $"barracks '{site.barracksId}'. No substitute will be created.");
                        }
                    }
                }
            }
        }

        void SyncBarracksSelection()
        {
            var snapshotApplier = SnapshotApplier.Instance;
            var lane = snapshotApplier != null && _selectedLaneIndex >= 0
                ? snapshotApplier.GetLane(_selectedLaneIndex)
                : null;
            string slotColor = lane?.slotColor;

            var sites = FindObjectsByType<BarracksSiteView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < sites.Length; i++)
            {
                var site = sites[i];
                if (site == null)
                    continue;

                bool selected = !string.IsNullOrWhiteSpace(_selectedBarracksId)
                    && site.MatchesLane(slotColor, _selectedLaneIndex)
                    && site.MatchesBarracks(_selectedBarracksId);
                site.SetSelected(selected);
            }
        }

        bool TryResolveInteractiveLane(out MLLaneSnap lane)
        {
            lane = SnapshotApplier.Instance?.MyLane;
            return IsFortressLane(lane);
        }

        bool IsFortressModeActive()
        {
            return TryResolveInteractiveLane(out _);
        }

        public static bool ShouldBlockLegacyTileMenu(int laneIndex, out string reason)
        {
            var snapshotApplier = SnapshotApplier.Instance;
            int resolvedLaneIndex = laneIndex >= 0
                ? laneIndex
                : snapshotApplier != null ? snapshotApplier.MyLaneIndex : -1;
            var lane = snapshotApplier != null && resolvedLaneIndex >= 0
                ? snapshotApplier.GetLane(resolvedLaneIndex)
                : snapshotApplier?.MyLane;
            var controller = s_instance != null && s_instance.isActiveAndEnabled
                ? s_instance
                : FindFirstObjectByType<FortressSelectionController>(FindObjectsInactive.Include);

            if (IsFortressLane(lane))
            {
                reason =
                    $"Authoritative lane '{DescribeLane(lane?.slotColor, resolvedLaneIndex)}' exposes " +
                    $"{lane.fortressPads.Length} fortress pad snapshot entr{(lane.fortressPads.Length == 1 ? "y" : "ies")}.";
                return true;
            }

            var config = snapshotApplier?.LatestMLMatchConfig;
            if (config != null && config.fortressPadConfigs != null && config.fortressPadConfigs.Length > 0)
            {
                reason =
                    $"Match config exposes {config.fortressPadConfigs.Length} fortress pad config " +
                    $"entr{(config.fortressPadConfigs.Length == 1 ? "y" : "ies")}.";
                return true;
            }

            if (controller != null && controller.isActiveAndEnabled)
            {
                reason =
                    $"Active FortressSelectionController '{controller.name}' owns fortress world-click routing.";
                return true;
            }

            string slotColor = lane?.slotColor;
            if (FortressPadAnchor.CollectAnchors(s_anchorScratch, slotColor, resolvedLaneIndex) > 0)
            {
                reason =
                    $"Scene lane '{DescribeLane(slotColor, resolvedLaneIndex)}' has " +
                    $"{s_anchorScratch.Count} authored FortressPadAnchor object(s).";
                return true;
            }

            if (BarracksSiteView.CollectSites(s_barracksScratch, slotColor, resolvedLaneIndex) > 0)
            {
                reason =
                    $"Scene lane '{DescribeLane(slotColor, resolvedLaneIndex)}' has " +
                    $"{s_barracksScratch.Count} authored BarracksSiteView object(s).";
                return true;
            }

            reason = null;
            return false;
        }

        static bool IsFortressLane(MLLaneSnap lane)
        {
            return lane != null && lane.fortressPads != null && lane.fortressPads.Length > 0;
        }

        static string DescribeLane(string slotColor, int laneIndex)
        {
            string normalized = FortressPadAnchor.NormalizeLaneKey(slotColor);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;

            string fromIndex = FortressPadAnchor.LaneIndexToLaneKey(laneIndex);
            return !string.IsNullOrWhiteSpace(fromIndex) ? fromIndex : $"lane-{laneIndex}";
        }

        Camera ResolveCamera()
        {
            if (worldCamera != null)
                return worldCamera;

            worldCamera = Camera.main;
            if (worldCamera != null)
                return worldCamera;

            var controller = FindFirstObjectByType<CameraController>();
            if (controller != null)
                worldCamera = controller.MainCam;

            return worldCamera;
        }

        void PanCameraTo(Vector3 worldPosition)
        {
            var cameraController = FindFirstObjectByType<CameraController>();
            if (cameraController != null)
                cameraController.PanTo(worldPosition);
        }

        static bool IsPointerOverUi(Vector2 screenPosition, int pointerId = -1)
        {
            return SceneEventSystemUtility.IsPointerOverUi(screenPosition, pointerId);
        }

        static MLFortressPad FindPad(MLLaneSnap lane, string padId)
        {
            var pads = lane?.fortressPads;
            if (pads == null || string.IsNullOrWhiteSpace(padId))
                return null;

            for (int i = 0; i < pads.Length; i++)
            {
                var pad = pads[i];
                if (pad != null && string.Equals(pad.padId, padId, StringComparison.OrdinalIgnoreCase))
                    return pad;
            }

            return null;
        }

        static BarracksPanel FindBarracksPanel()
        {
            var panels = FindObjectsByType<BarracksPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return panels != null && panels.Length > 0 ? panels[0] : null;
        }

        public static bool OpenFortressPad(int laneIndex, string padId)
        {
            if (string.IsNullOrWhiteSpace(padId))
                return false;

            var controller = ResolveActiveController();
            var snapshotApplier = SnapshotApplier.Instance;
            var lane = snapshotApplier != null && laneIndex >= 0
                ? snapshotApplier.GetLane(laneIndex)
                : snapshotApplier?.MyLane;
            if (!IsFortressLane(lane))
                return false;

            var anchor = FortressPadAnchor.FindAnchor(padId, lane.slotColor, lane.laneIndex);
            if (anchor == null)
            {
                Debug.LogError(
                    $"[FortressSelection] Missing authored FortressPadAnchor for lane '{lane.slotColor}' " +
                    $"pad '{padId}'. No substitute will be created.");
                return false;
            }

            if (controller != null)
                return controller.SelectPad(anchor, lane);

            var panel = FindBarracksPanel();
            if (panel == null)
                return false;

            panel.ShowForPad(padId);
            return true;
        }

        public static bool OpenBarracksSite(int laneIndex, string barracksId)
        {
            if (string.IsNullOrWhiteSpace(barracksId))
                return false;

            var controller = ResolveActiveController();
            var snapshotApplier = SnapshotApplier.Instance;
            var lane = snapshotApplier != null && laneIndex >= 0
                ? snapshotApplier.GetLane(laneIndex)
                : snapshotApplier?.MyLane;
            if (!IsFortressLane(lane))
                return false;

            var barracksView = BarracksSiteView.FindSite(barracksId, lane.slotColor, lane.laneIndex);
            if (barracksView == null)
            {
                Debug.LogError(
                    $"[FortressSelection] Missing authored BarracksSiteView for lane '{lane.slotColor}' " +
                    $"barracks '{barracksId}'. No substitute will be created.");
                return false;
            }

            if (controller != null)
                return controller.SelectBarracks(barracksView, lane);

            var panel = FindBarracksPanel();
            if (panel == null)
                return false;

            panel.ShowForBarracks(barracksId);
            return true;
        }

        static FortressSelectionController ResolveActiveController()
        {
            if (s_instance != null && s_instance.isActiveAndEnabled)
                return s_instance;

            return FindFirstObjectByType<FortressSelectionController>(FindObjectsInactive.Include);
        }

        public static bool FocusFortressPad(int laneIndex, string padId)
        {
            if (string.IsNullOrWhiteSpace(padId))
                return false;

            var snapshotApplier = SnapshotApplier.Instance;
            var lane = snapshotApplier != null ? snapshotApplier.GetLane(laneIndex) : null;
            string slotColor = lane?.slotColor;

            var anchor = FortressPadAnchor.FindAnchor(padId, slotColor, laneIndex);
            if (anchor == null)
            {
                Debug.LogError(
                    $"[FortressSelection] Missing authored FortressPadAnchor for lane '{slotColor ?? FortressPadAnchor.LaneIndexToLaneKey(laneIndex)}' " +
                    $"pad '{padId}'. No substitute will be created.");
                return false;
            }

            if (s_instance != null)
            {
                s_instance._selectedLaneIndex = -1;
                s_instance._selectedBarracksId = null;
                s_instance.SyncBarracksSelection();
                s_instance.PanCameraTo(anchor.FocusTransform.position);
            }

            return true;
        }
    }
}
