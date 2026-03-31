// GameManager.cs — Scene initialization hub for Game_ML and Game_Classic scenes.
//
// Responsibilities:
//   • Warm all object pools (HitEffect, CannonSplash, GoldPop)
//   • Register FloatingText.Prefab
//   • Wire CameraController to the CinemachineVirtualCamera
//   • Optionally kick off PostProcessController quality preset
//
// SETUP (Game_ML.unity):
//   1. Create empty GameObject "GameManager".
//   2. Attach this script.
//   3. Inspector: assign all prefab references.
//   4. Leave pools at default sizes (can tune in Inspector).

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    [DefaultExecutionOrder(-100)]
    public class GameManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("FX Prefabs (assign from Assets/Prefabs/FX/)")]
        public HitEffect   HitEffectPrefab;
        public CannonSplash CannonSplashPrefab;
        public GoldPop     GoldPopPrefab;
        public FloatingText FloatingTextPrefab;

        [Header("Pool warm sizes")]
        public int HitEffectPoolSize   = 12;
        public int CannonSplashPoolSize = 4;
        public int GoldPopPoolSize      = 6;

        [Header("Castle tile Transform (for life-loss FloatingText anchor)")]
        [Tooltip("Assign the Transform of the castle tile (col 5, row 27) so FloatingText spawns there on life loss.")]
        public Transform CastleTileTransform;

        [Header("Camera Preset")]
        public bool ForceCameraPreset = true;
        public float CameraOrthoSize = 9.2f;

        [Header("Battlefield Camera (Phase 3)")]
        [Tooltip("World position of the camera for the full-battlefield view.")]
        public Vector3 BattlefieldCameraPosition = new Vector3(0f, 20f, -10f);
        [Tooltip("World point the battlefield camera looks at.")]
        public Vector3 BattlefieldLookTarget = new Vector3(0f, 0f, 0f);

        [Header("Camera Re-lock (frames to hold camera after scene load)")]
        [Tooltip("Number of frames to re-apply the battlefield camera POV, preventing any late-initializing component from overriding it.")]
        public int CameraLockFrames = 5;

        int _cameraLockCountdown;

        [Header("Wave Defense HUD (assign TMP_Text references)")]
        [Tooltip("Shows 'Wave N' or 'Round N'.")]
        public TMP_Text TxtRound;
        [Tooltip("Shows the live fortress loop label.")]
        public TMP_Text TxtPhase;
        [Tooltip("Shows the live wave and barracks timers.")]
        public TMP_Text TxtCountdown;
        [Tooltip("Top gold display for the active lane.")]
        public TMP_Text TxtGoldTop;
        [Tooltip("Top income display for the active lane.")]
        public TMP_Text TxtIncomeTop;
        [Tooltip("Left-team HP display.")]
        public TMP_Text TxtTeamHpLeft;
        [Tooltip("Right-team HP display.")]
        public TMP_Text TxtTeamHpRight;
        Image _teamHpLeftFill;
        Image _teamHpRightFill;

        const int TickHz = 20; // must match server TICK_HZ
        static readonly List<FortressPadAnchor> s_cameraAnchors = new();

        string _playerSide;     // "left" | "right" — cached on first snapshot
        string _prevRoundState; // tracks previous round state for transition detection
        bool   _hudSubscribed;

        [Header("Legacy Single-Lane Camera (unused in battlefield mode)")]
        public float CameraBehind = 0f;
        public float CameraSide = 0f;
        public float CameraHeight = 11f;
        public float CameraLookAhead = 0f;
        public float CameraLookSide = 0f;
        public float CameraLookHeight = 0f;
        [Range(0f, 1f)] public float CameraLaneAnchorT = 0.5f;
        public float CameraFrameOffsetX = 0f;
        public float CameraFrameOffsetZ = -10f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void OnEnable()
        {
            EnsureWaveCombatRuntime();
            EnsureCenterBarracksOnboardingRuntime();

            var sa = SnapshotApplier.Instance;
            if (sa != null && !_hudSubscribed)
            {
                sa.OnMLSnapshotApplied += OnMLSnapshot;
                _hudSubscribed = true;
            }

            var nm = CastleDefender.Net.NetworkManager.Instance;
            if (nm != null)
            {
                nm.OnMLMatchReady += OnMatchReadySnapCamera;
                nm.OnMLMatchConfig += OnMatchConfigUpdateHud;
            }

            ApplyConfiguredHudDefaults();
        }

        void OnDisable()
        {
            if (_hudSubscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnMLSnapshot;
            _hudSubscribed = false;

            var nm = CastleDefender.Net.NetworkManager.Instance;
            if (nm != null)
            {
                nm.OnMLMatchReady -= OnMatchReadySnapCamera;
                nm.OnMLMatchConfig -= OnMatchConfigUpdateHud;
            }
        }

        // Called when match_ready fires — lane index is now authoritative.
        void OnMatchReadySnapCamera(MLMatchReadyPayload _)
        {
            EnsureCameraPOV();
            // Re-arm the lock so a few frames of position jitter are absorbed.
            _cameraLockCountdown = CameraLockFrames;
        }

        void OnMLSnapshot(MLSnapshot snap)
        {
            if (snap == null) return;

            // Derive player side once from authoritative snapshot data
            if (_playerSide == null)
            {
                var sa = SnapshotApplier.Instance;
                if (sa != null)
                {
                    var myLane = sa.MyLane;
                    if (myLane != null)
                        _playerSide = myLane.side; // "left" | "right"
                }
            }

            UpdateWaveHUD(snap);
        }

        void OnMatchConfigUpdateHud(MLMatchConfig config)
        {
            ApplyConfiguredHudDefaults(config);
        }

        void ApplyConfiguredHudDefaults(MLMatchConfig config = null)
        {
            EnsureWaveHUDRefs();

            var cfg = config ?? SnapshotApplier.Instance?.LatestMLMatchConfig;
            int teamHpStart = cfg != null && cfg.teamHpStart > 0 ? cfg.teamHpStart : 20;
            float startIncome = cfg != null && cfg.startIncome >= 0f ? cfg.startIncome : 0f;
            float startGold = cfg != null && cfg.startGold >= 0f ? cfg.startGold : 0f;

            if (TxtGoldTop != null)
                TxtGoldTop.text = $"Gold {Mathf.FloorToInt(startGold)}";

            if (TxtIncomeTop != null)
                TxtIncomeTop.text = $"Inc {startIncome:0.0}";

            UpdateTeamHpVisual(
                TxtTeamHpLeft,
                _teamHpLeftFill,
                "Left Side",
                teamHpStart,
                teamHpStart,
                new Color(1f, 0.92f, 0.2f));

            UpdateTeamHpVisual(
                TxtTeamHpRight,
                _teamHpRightFill,
                "Right Side",
                teamHpStart,
                teamHpStart,
                Color.white);
        }

        void UpdateWaveHUD(MLSnapshot snap)
        {
            EnsureWaveHUDRefs();

            var sa = SnapshotApplier.Instance;
            var lane = sa?.MyLane;

            // Round number
            if (TxtRound != null)
                TxtRound.text = $"Wave {snap.roundNumber}";

            int waveSeconds = sa != null ? sa.GetWaveTimerSecondsRemaining() : 0;
            int sendSeconds = lane != null && sa != null ? sa.GetBarracksSendSecondsRemaining(lane.laneIndex) : 0;

            if (TxtPhase != null)
                TxtPhase.text = "LIVE";

            if (TxtCountdown != null)
                TxtCountdown.text = lane != null
                    ? $"Wave {waveSeconds}s  Send {sendSeconds}s"
                    : $"Wave {waveSeconds}s";

            if (lane != null)
            {
                if (TxtGoldTop != null)
                    TxtGoldTop.text = $"Gold {Mathf.FloorToInt(lane.gold)}";

                if (TxtIncomeTop != null)
                    TxtIncomeTop.text = $"Inc {lane.income:0.0}";
            }

            // Legacy phase transition cue kept inert for compatibility snapshots.
            if (_prevRoundState == "build" && snap.roundState == "combat")
                AudioManager.I?.Play(AudioManager.SFX.UpgradeBarracks, 0.6f);
            _prevRoundState = snap.roundState;

            // Team HP — own team is emphasised via colour
            // teamHp is a class and can be null if the server omits it on early snapshots
            var hp = snap.teamHp;
            if (hp != null)
            {
                int teamHpMax = snap.teamHpMax > 0
                    ? snap.teamHpMax
                    : SnapshotApplier.Instance?.LatestMLMatchConfig?.teamHpStart > 0
                        ? SnapshotApplier.Instance.LatestMLMatchConfig.teamHpStart
                        : Mathf.Max(hp.left, hp.right, 20);

                UpdateTeamHpVisual(
                    TxtTeamHpLeft,
                    _teamHpLeftFill,
                    "Left Side",
                    hp.left,
                    teamHpMax,
                    _playerSide == "left"
                        ? new Color(1f, 0.92f, 0.2f)
                        : new Color(1f, 1f, 1f, 0.6f));

                UpdateTeamHpVisual(
                    TxtTeamHpRight,
                    _teamHpRightFill,
                    "Right Side",
                    hp.right,
                    teamHpMax,
                    _playerSide == "right"
                        ? new Color(1f, 0.92f, 0.2f)
                        : new Color(1f, 1f, 1f, 0.6f));
            }
        }

        void Awake()
        {
            InitPools();
            EnsureWaveCombatRuntime();
            EnsureCenterBarracksOnboardingRuntime();
            EnsureCameraPOV();
            _cameraLockCountdown = CameraLockFrames;
            EnsureWaveHUD();   // auto-create phase/round labels if not wired
            ApplyConfiguredHudDefaults();
        }

        void Start()
        {
            EnsureWaveCombatRuntime();

            // When entering from Lobby -> Loading -> Game_ML, camera objects can finish
            // initialization after Awake; re-apply POV at Start and next frame.
            EnsureCameraPOV();
            StartCoroutine(EnsureCameraPOVNextFrame());

            // match_ready may have already fired before this scene loaded (e.g. the
            // Loading screen absorbed the event).  If so, snap the camera immediately
            // to the correct lane rather than waiting for the next match_ready.
            {
                var saReady = SnapshotApplier.Instance;
                if (saReady != null && saReady.LatestMLMatchReady != null)
                {
                    EnsureCameraPOV();
                    _cameraLockCountdown = CameraLockFrames;
                }
            }

            // SnapshotApplier may have initialised after OnEnable — subscribe if missed.
            if (!_hudSubscribed)
            {
                var sa = SnapshotApplier.Instance;
                if (sa != null)
                {
                    sa.OnMLSnapshotApplied += OnMLSnapshot;
                    _hudSubscribed = true;
                    if (sa.LatestML != null) OnMLSnapshot(sa.LatestML);
                }
            }
        }

        void LateUpdate()
        {
            if (_cameraLockCountdown > 0)
            {
                EnsureCameraPOV();
                _cameraLockCountdown--;
            }
        }

        // ─────────────────────────────────────────────────────────────────────

        void InitPools()
        {
            if (HitEffectPrefab != null)
                HitEffectPool.Init(HitEffectPrefab, HitEffectPoolSize);
            else
                Debug.LogWarning("[GameManager] HitEffectPrefab not assigned.");

            if (CannonSplashPrefab != null)
                CannonSplash.Init(CannonSplashPrefab, CannonSplashPoolSize);
            else
                Debug.LogWarning("[GameManager] CannonSplashPrefab not assigned.");

            if (GoldPopPrefab != null)
                GoldPop.Init(GoldPopPrefab, GoldPopPoolSize);
            else
                Debug.LogWarning("[GameManager] GoldPopPrefab not assigned.");

            if (FloatingTextPrefab != null)
                FloatingText.Prefab = FloatingTextPrefab;
            else
                Debug.LogWarning("[GameManager] FloatingTextPrefab not assigned.");
        }

        // Finds a Camera only within this GameObject's scene, ignoring cameras
        // in other additively-loaded scenes (e.g. Lobby still unloading during transition).
        Camera FindCameraInScene()
        {
            var scene = gameObject.scene;
            foreach (var root in scene.GetRootGameObjects())
            {
                var cam = root.GetComponentInChildren<Camera>(true);
                if (cam != null) return cam;
            }
            return null;
        }

        static void EnsureWaveCombatRuntime()
        {
            WaveSnapshotRuntimeSpawner.EnsureRuntimeSpawner();
        }

        void EnsureCenterBarracksOnboardingRuntime()
        {
            if (GetComponent<CenterBarracksOnboardingController>() == null)
                gameObject.AddComponent<CenterBarracksOnboardingController>();
        }

        void EnsureCameraPOV()
        {
            if (!ForceCameraPreset) return;

            var cam = FindCameraInScene();
            if (cam == null) return;

            cam.orthographic     = true;
            cam.orthographicSize = CameraOrthoSize;

            // Position camera over the local player's assigned lane.
            // Falls back to lane 0 if NetworkManager isn't ready yet.
            int laneIndex = CastleDefender.Net.NetworkManager.Instance != null
                ? CastleDefender.Net.NetworkManager.Instance.MyLaneIndex
                : 0;

            // Try to resolve server lane index → spatial branch config via branchId
            int branchCfg = laneIndex;
            var sa = SnapshotApplier.Instance;
            // Check match-ready assignments first (available before first snapshot)
            if (sa?.LatestMLMatchReady?.laneAssignments != null)
            {
                foreach (var a in sa.LatestMLMatchReady.laneAssignments)
                    if (a.laneIndex == laneIndex)
                    { int bc = BattlefieldSpaceMapper.GetBranchConfigIndex(a.branchId); if (bc >= 0) { branchCfg = bc; break; } }
            }
            else if (sa?.LatestML?.lanes != null)
            {
                foreach (var ls in sa.LatestML.lanes)
                    if (ls != null && ls.laneIndex == laneIndex)
                    { int bc = BattlefieldSpaceMapper.GetBranchConfigIndex(ls.branchId); if (bc >= 0) { branchCfg = bc; break; } }
            }

            if (TryGetFortressLaneCameraFrame(cam, laneIndex, out var fortressFocus, out var fortressScreenUp, out var fortressOrthoSize))
            {
                cam.orthographicSize = fortressOrthoSize;
                cam.transform.position = fortressFocus + Vector3.up * Mathf.Max(CameraHeight, 11f);
                cam.transform.LookAt(fortressFocus, fortressScreenUp);

                var fortressCtrl = FindFirstObjectByType<global::CameraController>();
                if (fortressCtrl == null)
                    fortressCtrl = gameObject.AddComponent<global::CameraController>();

                fortressCtrl.MainCam = cam;
                if (fortressCtrl.CameraTarget == null)
                    fortressCtrl.CameraTarget = cam.transform;

                fortressCtrl.SnapToCurrentPosition();
                return;
            }

            Vector3 castlePos  = BattlefieldSpaceMapper.TileToWorld(branchCfg, 5, BattlefieldSpaceMapper.LaneRows - 1);
            Vector3 spawnPos   = BattlefieldSpaceMapper.TileToWorld(branchCfg, 5, 0);
            Vector3 laneCenter = (castlePos + spawnPos) * 0.5f;
            Vector3 camPos     = laneCenter + Vector3.up * CameraHeight;

            cam.transform.position = camPos;

            // Orient so spawn (row 0) appears at the TOP of the screen.
            // rowDir points spawn→castle, so negating it gives the screen-up direction.
            Vector3 screenUp = -BattlefieldSpaceMapper.GetLaneForwardDir(branchCfg);
            if (screenUp.sqrMagnitude < 0.001f) screenUp = Vector3.back;
            cam.transform.LookAt(laneCenter, screenUp);

            var ctrl = FindFirstObjectByType<global::CameraController>();
            if (ctrl == null)
                ctrl = gameObject.AddComponent<global::CameraController>();

            ctrl.MainCam = cam;
            if (ctrl.CameraTarget == null)
                ctrl.CameraTarget = cam.transform;

            // Sync the controller so pan/zoom starts from exactly this position.
            ctrl.SnapToCurrentPosition();
        }

        bool TryGetFortressLaneCameraFrame(Camera cam, int laneIndex, out Vector3 focusWorld, out Vector3 screenUp, out float orthoSize)
        {
            focusWorld = Vector3.zero;
            screenUp = Vector3.forward;
            orthoSize = CameraOrthoSize;

            string slotColor = ResolveLaneSlotColor(laneIndex);
            if (FortressPadAnchor.CollectAnchors(s_cameraAnchors, slotColor, laneIndex) <= 0)
                return false;

            bool hasBounds = false;
            Bounds laneBounds = default;
            FortressPadAnchor townCoreAnchor = null;
            FortressPadAnchor barracksAnchor = null;

            for (int i = 0; i < s_cameraAnchors.Count; i++)
            {
                var anchor = s_cameraAnchors[i];
                if (anchor == null)
                    continue;

                var bounds = anchor.GetWorldBounds();
                if (!hasBounds)
                {
                    laneBounds = bounds;
                    hasBounds = true;
                }
                else
                {
                    laneBounds.Encapsulate(bounds);
                }

                if (townCoreAnchor == null &&
                    anchor.MatchesPad("town_core_pad"))
                {
                    townCoreAnchor = anchor;
                }

                if (barracksAnchor == null &&
                    anchor.MatchesPad("barracks_pad"))
                {
                    barracksAnchor = anchor;
                }
            }

            if (!hasBounds)
                return false;

            focusWorld = laneBounds.center;
            if (townCoreAnchor != null)
            {
                Vector3 outward = barracksAnchor != null
                    ? barracksAnchor.FocusTransform.position - townCoreAnchor.FocusTransform.position
                    : laneBounds.center - townCoreAnchor.FocusTransform.position;
                outward.y = 0f;
                if (outward.sqrMagnitude > 0.001f)
                    screenUp = outward.normalized;
            }

            if (screenUp.sqrMagnitude < 0.001f)
                screenUp = Vector3.forward;

            Vector3 screenRight = Vector3.Cross(Vector3.up, screenUp).normalized;
            if (screenRight.sqrMagnitude < 0.001f)
                screenRight = Vector3.right;

            float halfVertical = 0f;
            float halfHorizontal = 0f;
            for (int i = 0; i < s_cameraAnchors.Count; i++)
            {
                var anchor = s_cameraAnchors[i];
                if (anchor == null)
                    continue;

                ExpandProjectedExtents(anchor.GetWorldBounds(), focusWorld, screenRight, screenUp, ref halfHorizontal, ref halfVertical);
            }

            const float verticalPadding = 2.5f;
            const float horizontalPadding = 3.5f;
            float aspect = cam != null && cam.pixelHeight > 0
                ? Mathf.Max(0.1f, cam.aspect)
                : 16f / 9f;
            orthoSize = Mathf.Max(
                CameraOrthoSize,
                halfVertical + verticalPadding,
                (halfHorizontal + horizontalPadding) / aspect);

            float gameplaySurfaceY = BattlefieldSpaceMapper.TileToWorld(laneIndex, 0f, 0f).y;
            focusWorld = new Vector3(focusWorld.x, gameplaySurfaceY, focusWorld.z);
            return true;
        }

        string ResolveLaneSlotColor(int laneIndex)
        {
            var sa = SnapshotApplier.Instance;
            var lane = sa?.GetLane(laneIndex);
            if (!string.IsNullOrWhiteSpace(lane?.slotColor))
                return lane.slotColor;

            var assignments = sa?.LatestMLMatchReady?.laneAssignments;
            if (assignments != null)
            {
                for (int i = 0; i < assignments.Length; i++)
                {
                    var assignment = assignments[i];
                    if (assignment != null && assignment.laneIndex == laneIndex && !string.IsNullOrWhiteSpace(assignment.slotColor))
                        return assignment.slotColor;
                }
            }

            return FortressPadAnchor.LaneIndexToLaneKey(laneIndex);
        }

        static void ExpandProjectedExtents(Bounds bounds, Vector3 center, Vector3 rightAxis, Vector3 upAxis, ref float halfWidth, ref float halfHeight)
        {
            Vector3 ext = bounds.extents;
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = bounds.center + Vector3.Scale(ext, new Vector3(sx, sy, sz));
                        Vector3 delta = corner - center;
                        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(delta, rightAxis)));
                        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(delta, upAxis)));
                    }
                }
            }
        }

        IEnumerator EnsureCameraPOVNextFrame()
        {
            yield return null;
            EnsureCameraPOV();
        }

        // ── Auto-create Wave HUD if not wired in Inspector ────────────────────
        void EnsureWaveHUD()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            var existing = canvas.transform.Find("WaveHUD");
            GameObject hudGO = existing != null ? existing.gameObject : null;

            if (hudGO == null)
            {
                hudGO = new GameObject("WaveHUD");
                hudGO.transform.SetParent(canvas.transform, false);
                var rt = hudGO.AddComponent<RectTransform>();
                rt.anchorMin        = new Vector2(0.1f, 1f);
                rt.anchorMax        = new Vector2(0.9f, 1f);
                rt.pivot            = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -4f);
                rt.sizeDelta        = new Vector2(0f, 48f);

                var bg = hudGO.AddComponent<UnityEngine.UI.Image>();
                bg.color = new Color(0f, 0f, 0f, 0.5f);

                var hlg = hudGO.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                hlg.spacing            = 16f;
                hlg.childAlignment     = TextAnchor.MiddleCenter;
                hlg.childForceExpandWidth  = false;
                hlg.childForceExpandHeight = true;
                hlg.padding = new RectOffset(12, 12, 4, 4);
            }

            if (TxtRound == null)
                TxtRound = GetOrMakeLabel(hudGO, "Txt_Round", "Wave 1", 100f, 18, Color.white);
            if (TxtPhase == null)
                TxtPhase = GetOrMakeLabel(hudGO, "Txt_Phase", "LIVE", 90f, 18, new Color(0.3f, 1f, 0.45f));
            if (TxtCountdown == null)
                TxtCountdown = GetOrMakeLabel(hudGO, "Txt_Countdown", "Wave 120s", 128f, 18, new Color(1f, 0.9f, 0.3f));
            if (TxtGoldTop == null)
                TxtGoldTop = GetOrMakeLabel(hudGO, "Txt_GoldTop", "Gold 0", 92f, 18, new Color(1f, 0.86f, 0.27f));
            if (TxtIncomeTop == null)
                TxtIncomeTop = GetOrMakeLabel(hudGO, "Txt_IncomeTop", "Inc 0.0", 92f, 18, new Color(0.36f, 0.92f, 0.86f, 1f));
            if (TxtTeamHpLeft == null)
                TxtTeamHpLeft = FindHudLabel(hudGO.transform, "Txt_TeamHpLeft")
                    ?? FindHudLabel(hudGO.transform, "Txt_HpLeft")
                    ?? GetOrMakeLabel(hudGO, "Txt_TeamHpLeft", "Left 0/0", 110f, 16, new Color(1f, 0.92f, 0.2f));
            if (TxtTeamHpRight == null)
                TxtTeamHpRight = FindHudLabel(hudGO.transform, "Txt_TeamHpRight")
                    ?? FindHudLabel(hudGO.transform, "Txt_HpRight")
                    ?? GetOrMakeLabel(hudGO, "Txt_TeamHpRight", "Right 0/0", 110f, 16, Color.white);

            _teamHpLeftFill = GetOrMakeHpBar(hudGO, "Bar_TeamHpLeft", 180f, new Color(0.95f, 0.78f, 0.16f, 1f));
            _teamHpRightFill = GetOrMakeHpBar(hudGO, "Bar_TeamHpRight", 180f, new Color(0.32f, 0.84f, 1f, 1f));
        }

        void EnsureWaveHUDRefs()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;

            var hud = canvas.transform.Find("WaveHUD");
            if (hud == null) return;

            if (TxtRound == null)
                TxtRound = FindHudLabel(hud, "Txt_Round") ?? FindHudLabel(hud, "Txt_Wave");
            if (TxtPhase == null)
                TxtPhase = FindHudLabel(hud, "Txt_Phase");
            if (TxtCountdown == null)
                TxtCountdown = FindHudLabel(hud, "Txt_Countdown");
            if (TxtGoldTop == null)
                TxtGoldTop = FindHudLabel(hud, "Txt_GoldTop");
            if (TxtIncomeTop == null)
                TxtIncomeTop = FindHudLabel(hud, "Txt_IncomeTop");
            if (TxtTeamHpLeft == null)
                TxtTeamHpLeft = FindHudLabel(hud, "Txt_TeamHpLeft") ?? FindHudLabel(hud, "Txt_HpLeft");
            if (TxtTeamHpRight == null)
                TxtTeamHpRight = FindHudLabel(hud, "Txt_TeamHpRight") ?? FindHudLabel(hud, "Txt_HpRight");
            if (_teamHpLeftFill == null)
                _teamHpLeftFill = FindHudImage(hud, "Bar_TeamHpLeft/Fill");
            if (_teamHpRightFill == null)
                _teamHpRightFill = FindHudImage(hud, "Bar_TeamHpRight/Fill");
        }

        static TMP_Text FindHudLabel(Transform parent, string name)
        {
            var child = parent.Find(name);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        static Image FindHudImage(Transform parent, string path)
        {
            var child = parent.Find(path);
            return child != null ? child.GetComponent<Image>() : null;
        }

        static void UpdateTeamHpVisual(TMP_Text label, Image fill, string teamName, int currentHp, int maxHp, Color color)
        {
            if (label != null)
            {
                label.text = $"{teamName} {currentHp}/{maxHp}";
                label.color = color;
            }

            if (fill != null)
            {
                fill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
                fill.color = color;
            }
        }

        static TMPro.TMP_Text GetOrMakeLabel(GameObject parent, string name, string defaultText, float width, int fontSize, Color color)
        {
            var existing = parent.transform.Find(name);
            if (existing != null) return existing.GetComponent<TMPro.TMP_Text>();

            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 40f);
            var le = go.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth  = width;
            le.preferredHeight = 40f;
            var txt = go.AddComponent<TMPro.TextMeshProUGUI>();
            txt.text      = defaultText;
            txt.fontSize  = fontSize;
            txt.color     = color;
            txt.alignment = TMPro.TextAlignmentOptions.Center;
            txt.fontStyle = TMPro.FontStyles.Bold;
            return txt;
        }

        static Image GetOrMakeHpBar(GameObject parent, string name, float width, Color fillColor)
        {
            var existing = parent.transform.Find(name);
            if (existing != null)
            {
                var existingFill = existing.Find("Fill");
                if (existingFill != null)
                {
                    var img = existingFill.GetComponent<Image>();
                    if (img != null) return img;
                }
            }

            var root = new GameObject(name);
            root.transform.SetParent(parent.transform, false);
            var rt = root.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(width, 18f);

            var le = root.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 18f;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);

            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(root.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;

            var fill = fillGo.AddComponent<Image>();
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            fill.color = fillColor;
            return fill;
        }
    }
}
