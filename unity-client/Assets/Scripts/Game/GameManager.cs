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
using TMPro;
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
        public float CameraOrthoSize = 22f;

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
        [Tooltip("Shows BUILD / COMBAT / TRANSITION phase badge.")]
        public TMP_Text TxtPhase;
        [Tooltip("Seconds remaining in the current phase.")]
        public TMP_Text TxtCountdown;
        [Tooltip("Left-team HP display.")]
        public TMP_Text TxtTeamHpLeft;
        [Tooltip("Right-team HP display.")]
        public TMP_Text TxtTeamHpRight;

        const int TickHz = 20; // must match server TICK_HZ

        string _playerSide;     // "left" | "right" — cached on first snapshot
        string _prevRoundState; // tracks previous round state for transition detection
        bool   _hudSubscribed;

        [Header("Legacy Single-Lane Camera (unused in battlefield mode)")]
        public float CameraBehind = 0f;
        public float CameraSide = 0f;
        public float CameraHeight = 20f;
        public float CameraLookAhead = 0f;
        public float CameraLookSide = 0f;
        public float CameraLookHeight = 0f;
        [Range(0f, 1f)] public float CameraLaneAnchorT = 0.5f;
        public float CameraFrameOffsetX = 0f;
        public float CameraFrameOffsetZ = -10f;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void OnEnable()
        {
            var sa = SnapshotApplier.Instance;
            if (sa != null && !_hudSubscribed)
            {
                sa.OnMLSnapshotApplied += OnMLSnapshot;
                _hudSubscribed = true;
            }
        }

        void OnDisable()
        {
            if (_hudSubscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnMLSnapshot;
            _hudSubscribed = false;
        }

        void OnMLSnapshot(MLSnapshot snap)
        {
            if (snap == null) return;

            // Derive player side once from authoritative snapshot data
            if (_playerSide == null)
            {
                var sa = SnapshotApplier.Instance;
                if (sa != null && sa.MyLaneIndex >= 0 &&
                    snap.lanes != null && sa.MyLaneIndex < snap.lanes.Length)
                {
                    var myLane = snap.lanes[sa.MyLaneIndex];
                    if (myLane != null)
                        _playerSide = myLane.side; // "left" | "right"
                }
            }

            UpdateWaveHUD(snap);
        }

        void UpdateWaveHUD(MLSnapshot snap)
        {
            // Round number
            if (TxtRound != null)
                TxtRound.text = $"Wave {snap.roundNumber}";

            // Phase badge
            if (TxtPhase != null)
            {
                TxtPhase.text = snap.roundState switch
                {
                    "build"      => "BUILD",
                    "combat"     => "COMBAT",
                    "transition" => "NEXT WAVE",
                    _            => snap.roundState?.ToUpper() ?? ""
                };
            }

            // Countdown (ticks → seconds, ceil so "0" only shows on exact zero)
            if (TxtCountdown != null)
            {
                int secs = Mathf.CeilToInt((float)snap.roundStateTicks / TickHz);
                TxtCountdown.text = secs > 0 ? $"{secs}s" : "";
            }

            // Wave-start audio cue: play a sound when build phase transitions to combat
            if (_prevRoundState == "build" && snap.roundState == "combat")
                AudioManager.I?.Play(AudioManager.SFX.UpgradeBarracks, 0.6f);
            _prevRoundState = snap.roundState;

            // Team HP — own team is emphasised via colour
            // teamHp is a class and can be null if the server omits it on early snapshots
            var hp = snap.teamHp;
            if (hp != null)
            {
                if (TxtTeamHpLeft != null)
                {
                    TxtTeamHpLeft.text = $"♥ {hp.left}";
                    TxtTeamHpLeft.color = _playerSide == "left"
                        ? new Color(1f, 0.92f, 0.2f)   // gold — own team
                        : new Color(1f, 1f, 1f, 0.6f);  // dim white — opponent
                }

                if (TxtTeamHpRight != null)
                {
                    TxtTeamHpRight.text = $"♥ {hp.right}";
                    TxtTeamHpRight.color = _playerSide == "right"
                        ? new Color(1f, 0.92f, 0.2f)
                        : new Color(1f, 1f, 1f, 0.6f);
                }
            }
        }

        void Awake()
        {
            InitPools();
            WireInfoBarAnchor();
            EnsureCameraPOV();
            _cameraLockCountdown = CameraLockFrames;
            EnsureWaveHUD();   // auto-create phase/round labels if not wired
        }

        void Start()
        {
            // When entering from Lobby -> Loading -> Game_ML, camera objects can finish
            // initialization after Awake; re-apply POV at Start and next frame.
            EnsureCameraPOV();
            StartCoroutine(EnsureCameraPOVNextFrame());

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

        void WireInfoBarAnchor()
        {
            if (CastleTileTransform == null) return;

            var infoBar = FindFirstObjectByType<CastleDefender.UI.InfoBar>();
            if (infoBar != null)
                infoBar.FloatTextAnchor = CastleTileTransform;
        }

        void EnsureCameraPOV()
        {
            if (!ForceCameraPreset) return;

            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null) return;

            cam.orthographic     = true;
            cam.orthographicSize = CameraOrthoSize;

            // Position camera over the local player's assigned lane.
            // Falls back to Inspector preset (Lane 0) if NetworkManager isn't ready yet.
            int laneIndex = CastleDefender.Net.NetworkManager.Instance != null
                ? CastleDefender.Net.NetworkManager.Instance.MyLaneIndex
                : 0;

            Vector3 castlePos  = TileGrid.TileToWorld(laneIndex, 5, 27);
            Vector3 spawnPos   = TileGrid.TileToWorld(laneIndex, 5, 0);
            Vector3 laneCenter = (castlePos + spawnPos) * 0.5f + new Vector3(0f, 0f, CameraFrameOffsetZ);
            Vector3 camPos     = laneCenter + Vector3.up * CameraHeight;

            cam.transform.position = camPos;
            cam.transform.LookAt(laneCenter, Vector3.left); // −X up → lane runs vertically

            var ctrl = FindFirstObjectByType<global::CameraController>();
            if (ctrl == null)
                ctrl = gameObject.AddComponent<global::CameraController>();

            ctrl.MainCam = cam;
            if (ctrl.CameraTarget == null)
                ctrl.CameraTarget = cam.transform;
        }

        IEnumerator EnsureCameraPOVNextFrame()
        {
            yield return null;
            EnsureCameraPOV();
        }

        // ── Auto-create Wave HUD if not wired in Inspector ────────────────────
        void EnsureWaveHUD()
        {
            if (TxtRound != null) return; // already wired

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

            TxtRound      = GetOrMakeLabel(hudGO, "Txt_Round",      "Wave 1",  100f, 18, Color.white);
            TxtPhase      = GetOrMakeLabel(hudGO, "Txt_Phase",      "BUILD",    90f, 18, new Color(0.3f, 1f, 0.45f));
            TxtCountdown  = GetOrMakeLabel(hudGO, "Txt_Countdown",  "30s",      64f, 18, new Color(1f, 0.9f, 0.3f));
            TxtTeamHpLeft  = GetOrMakeLabel(hudGO, "Txt_HpLeft",   "♥ 20",     80f, 16, new Color(1f, 0.92f, 0.2f));
            TxtTeamHpRight = GetOrMakeLabel(hudGO, "Txt_HpRight",  "♥ 20",     80f, 16, Color.white);
            Debug.Log("[GameManager] Auto-created WaveHUD panel.");
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
    }
}
