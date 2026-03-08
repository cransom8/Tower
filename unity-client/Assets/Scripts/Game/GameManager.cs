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

        void Awake()
        {
            InitPools();
            WireInfoBarAnchor();
            EnsureCameraPOV();
            _cameraLockCountdown = CameraLockFrames;
        }

        void Start()
        {
            // When entering from Lobby -> Loading -> Game_ML, camera objects can finish
            // initialization after Awake; re-apply POV at Start and next frame.
            EnsureCameraPOV();
            StartCoroutine(EnsureCameraPOVNextFrame());
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

            cam.orthographic = true;
            cam.orthographicSize = CameraOrthoSize;

            // Phase 3: frame the full 4-branch battlefield centered at world origin.
            // BattlefieldCameraPosition and BattlefieldLookTarget are tunable in the Inspector.
            // Default: camera at (0, 20, -10) looking at (0, 0, 0) gives a natural RTS angle.
            cam.transform.position = BattlefieldCameraPosition;
            cam.transform.LookAt(BattlefieldLookTarget);

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
    }
}
