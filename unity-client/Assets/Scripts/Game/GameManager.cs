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
        }

        void Start()
        {
            // When entering from Lobby -> Loading -> Game_ML, camera objects can finish
            // initialization after Awake; re-apply POV at Start and next frame.
            EnsureCameraPOV();
            StartCoroutine(EnsureCameraPOVNextFrame());
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
            Vector3 castlePos = CastleTileTransform != null
                ? CastleTileTransform.position
                : TileGrid.TileToWorld(5, 27);
            Vector3 spawnPos = TileGrid.TileToWorld(5, 0);
            Vector3 laneDir = spawnPos - castlePos;
            laneDir.y = 0f;
            if (laneDir.sqrMagnitude < 0.001f) laneDir = new Vector3(0.9f, 0f, -0.45f);
            laneDir.Normalize();
            Vector3 laneRight = Vector3.Cross(Vector3.up, laneDir).normalized;
            Vector3 laneAnchor = Vector3.Lerp(castlePos, spawnPos, CameraLaneAnchorT);
            Vector3 framingAnchor = laneAnchor + new Vector3(CameraFrameOffsetX, 0f, CameraFrameOffsetZ);

            // Anchor camera on lane center so tweaks affect lane framing, not just board edge.
            cam.transform.position = framingAnchor
                                   - laneDir * CameraBehind
                                   + laneRight * CameraSide
                                   + Vector3.up * CameraHeight;
            cam.transform.LookAt(framingAnchor
                               + laneDir * CameraLookAhead
                               + laneRight * CameraLookSide
                               + Vector3.up * CameraLookHeight);

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
