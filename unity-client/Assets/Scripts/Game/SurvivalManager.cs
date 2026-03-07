// SurvivalManager.cs — Scene controller for Game_Survival.
//
// Replaces GameManager in the Game_Survival scene.
//
// Responsibilities:
//   • Pool FX objects (same as GameManager)
//   • Receive survival_match_ready / survival_state_snapshot / survival_wave_start / survival_ended
//   • Convert SurvivalSnapshot → MLSnapshot so LaneRenderer / InfoBar / TileGrid work unchanged
//   • Drive wave HUD overlay (wave number, prep countdown, wavePhase label)
//   • Show end-of-run summary panel on survival_ended
//
// SETUP (Game_Survival.unity — duplicate Game_ML then replace GameManager with this):
//   1. Create empty GameObject "SurvivalManager".
//   2. Attach this script.
//   3. Inspector: assign FX prefabs (same as GameManager).
//   4. Inspector: assign WaveHUD group and EndRun panel references.
//   5. Inspector: assign CastleTileTransform (col 5, row 27).

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Game
{
    public class SurvivalManager : MonoBehaviour
    {
        // ── FX (same as GameManager) ──────────────────────────────────────────
        [Header("FX Prefabs (assign from Assets/Prefabs/FX/)")]
        public HitEffect    HitEffectPrefab;
        public CannonSplash CannonSplashPrefab;
        public GoldPop      GoldPopPrefab;
        public FloatingText FloatingTextPrefab;

        [Header("Pool warm sizes")]
        public int HitEffectPoolSize   = 12;
        public int CannonSplashPoolSize = 4;
        public int GoldPopPoolSize      = 6;

        [Header("Castle tile Transform")]
        public Transform CastleTileTransform;

        // ── Wave HUD ──────────────────────────────────────────────────────────
        [Header("Wave HUD (top-center overlay)")]
        public GameObject ObjWaveHUD;
        public TMP_Text   TxtWaveLabel;      // "Wave 5"
        public TMP_Text   TxtWavePhase;      // "PREP" / "SPAWNING" / "CLEARING"
        public TMP_Text   TxtPrepCountdown;  // "3.0s" during PREP phase
        public TMP_Text   TxtLivesHUD;       // shared lives (co-op)
        public Image      ImgEnemyBar;       // Filled, Radial360 — aggregate enemy HP

        // ── End-of-run panel ──────────────────────────────────────────────────
        [Header("End-of-Run Panel")]
        public GameObject PanelEndRun;
        public TMP_Text   TxtEndTitle;       // "Run Over"
        public TMP_Text   TxtEndStats;       // waves / kills / time / gold
        public Button     BtnRetry;
        public Button     BtnLobby;

        // ── Camera preset (copy of GameManager fields) ────────────────────────
        [Header("Camera Preset")]
        public bool  ForceCameraPreset = true;
        public float CameraOrthoSize = 22f;
        public float CameraHeight = 20f;
        public float CameraFrameOffsetZ = -10f;

        // ── Runtime state ─────────────────────────────────────────────────────
        SurvivalSnapshot _latest;
        bool             _ended;

        const int SurvivalTickHz = 20;   // survival TICK_HZ

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            InitPools();
            WireInfoBarAnchor();
            EnsureCameraPOV();
        }

        void Start()
        {
            if (PanelEndRun != null) PanelEndRun.SetActive(false);
            if (ObjWaveHUD  != null) ObjWaveHUD.SetActive(false);

            if (BtnRetry != null) BtnRetry.onClick.AddListener(OnRetry);
            if (BtnLobby != null) BtnLobby.onClick.AddListener(OnLobby);

            EnsureCameraPOV();
            StartCoroutine(EnsureCameraPOVNextFrame());
        }

        void OnEnable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnSurvivalMatchReady += HandleMatchReady;
            nm.OnSurvivalSnapshot   += HandleSnapshot;
            nm.OnSurvivalWaveStart  += HandleWaveStart;
            nm.OnSurvivalEnded      += HandleEnded;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnSurvivalMatchReady -= HandleMatchReady;
            nm.OnSurvivalSnapshot   -= HandleSnapshot;
            nm.OnSurvivalWaveStart  -= HandleWaveStart;
            nm.OnSurvivalEnded      -= HandleEnded;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        void HandleMatchReady(SurvivalMatchReadyPayload p)
        {
            var sa = SnapshotApplier.Instance;
            if (sa != null)
            {
                sa.MyLaneIndex = NetworkManager.Instance.MyLaneIndex;
                sa.ViewingLane = sa.MyLaneIndex;
                sa.TotalLanes  = p.playerCount;
            }
            if (ObjWaveHUD != null) ObjWaveHUD.SetActive(true);
            Debug.Log($"[SurvivalMgr] match ready: {p.code} waveSet={p.waveSetName}");
        }

        void HandleSnapshot(SurvivalSnapshot snap)
        {
            if (_ended) return;
            _latest = snap;
            UpdateWaveHUD(snap);

            // Synthesize an MLSnapshot so LaneRenderer / InfoBar / TileGrid work unchanged.
            var mlSnap = SynthesizeMLSnapshot(snap);
            SnapshotApplier.Instance?.InjectML(mlSnap);
        }

        void HandleWaveStart(SurvivalWaveStartPayload p)
        {
            string label = p.isBoss  ? $"BOSS Wave {p.waveNumber}!"
                         : p.isRush  ? $"RUSH Wave {p.waveNumber}!"
                         : p.isElite ? $"ELITE Wave {p.waveNumber}!"
                                     : $"Wave {p.waveNumber}";
            if (TxtWaveLabel != null) TxtWaveLabel.text = label;
            // No dedicated WaveAlert SFX yet — play UpgradeBarracks fanfare as wave-incoming cue
            AudioManager.I?.Play(AudioManager.SFX.UpgradeBarracks, 0.6f);
        }

        void HandleEnded(SurvivalEndedPayload p)
        {
            _ended = true;
            ShowEndPanel(p);
        }

        // ── Wave HUD update ───────────────────────────────────────────────────

        void UpdateWaveHUD(SurvivalSnapshot snap)
        {
            if (TxtWaveLabel != null)
                TxtWaveLabel.text = $"Wave {snap.waveNumber}";

            if (TxtWavePhase != null)
                TxtWavePhase.text = snap.wavePhase switch
                {
                    "PREP"     => "Prep",
                    "SPAWNING" => "Incoming!",
                    "CLEARING" => "Clearing...",
                    "COMPLETE" => "Complete",
                    _          => snap.wavePhase
                };

            if (TxtPrepCountdown != null)
            {
                bool showCountdown = snap.wavePhase == "PREP";
                TxtPrepCountdown.gameObject.SetActive(showCountdown);
                if (showCountdown)
                {
                    float secs = snap.prepTicksRemaining / (float)SurvivalTickHz;
                    TxtPrepCountdown.text = $"{secs:0.0}s";
                }
            }

            if (TxtLivesHUD != null)
                TxtLivesHUD.text = $"Lives: {snap.lives}";

            // Enemy health bar — aggregate HP fraction of all enemy units across all lanes
            if (ImgEnemyBar != null && snap.lanes != null)
            {
                float totalHp = 0f, totalMaxHp = 0f;
                foreach (var lane in snap.lanes)
                {
                    if (lane?.units == null) continue;
                    foreach (var u in lane.units)
                    {
                        if (!u.isEnemy) continue;
                        totalHp    += u.hp;
                        totalMaxHp += u.maxHp;
                    }
                }
                ImgEnemyBar.gameObject.SetActive(totalMaxHp > 0f);
                ImgEnemyBar.fillAmount = totalMaxHp > 0f ? totalHp / totalMaxHp : 0f;
            }
        }

        // ── End-of-run panel ──────────────────────────────────────────────────

        void ShowEndPanel(SurvivalEndedPayload p)
        {
            if (PanelEndRun == null) return;
            PanelEndRun.SetActive(true);

            if (TxtEndTitle != null)
                TxtEndTitle.text = "Run Over";

            if (TxtEndStats != null)
            {
                int mins = p.timeSurvived / 60;
                int secs = p.timeSurvived % 60;
                TxtEndStats.text =
                    $"Waves cleared: {p.wavesCleared}\n" +
                    $"Kills: {p.killCount}\n" +
                    $"Time survived: {mins}:{secs:00}\n" +
                    $"Gold earned: {Mathf.FloorToInt(p.goldEarned)}";
            }

            AudioManager.I?.Play(AudioManager.SFX.GameOver);
            StartCoroutine(ScaleIn(PanelEndRun.transform, 0f, 1f, 0.35f));
        }

        void OnRetry()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            LoadingScreen.LoadScene("Game_Survival");
        }

        void OnLobby()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            LoadingScreen.LoadScene("Lobby");
        }

        // ── MLSnapshot synthesis ──────────────────────────────────────────────
        // Converts a SurvivalSnapshot into an MLSnapshot so LaneRenderer, InfoBar,
        // and TileGrid work in the survival scene without modification.

        static MLSnapshot SynthesizeMLSnapshot(SurvivalSnapshot snap)
        {
            var mlLanes = new MLLaneSnap[snap.lanes?.Length ?? 0];
            for (int i = 0; i < mlLanes.Length; i++)
            {
                var sl = snap.lanes[i];
                mlLanes[i] = new MLLaneSnap
                {
                    laneIndex      = sl.laneIndex,
                    eliminated     = false,
                    gold           = sl.gold,
                    income         = sl.income,
                    lives          = snap.lives,   // pooled lives injected per-lane for InfoBar
                    barracksLevel  = sl.barracksLevel,
                    wallCount      = sl.wallCount,
                    walls          = sl.walls,
                    towerCells     = sl.towerCells,
                    path           = sl.path,
                    units          = ConvertUnits(sl.units, sl.path, sl.laneIndex),
                    projectiles    = sl.projectiles,
                    spawnQueueLength = 0,
                };
            }

            return new MLSnapshot
            {
                tick                  = snap.tick,
                phase                 = snap.phase,
                winner                = 0,
                incomeTicksRemaining  = 0,
                lanes                 = mlLanes,
            };
        }

        static MLUnit[] ConvertUnits(SurvivalUnit[] survivalUnits, MLGridPos[] path, int laneIndex)
        {
            if (survivalUnits == null || survivalUnits.Length == 0)
                return new MLUnit[0];

            var result = new MLUnit[survivalUnits.Length];
            for (int i = 0; i < survivalUnits.Length; i++)
            {
                var su = survivalUnits[i];
                float norm = ComputeNormProgress(path, su.x, su.y);
                result[i] = new MLUnit
                {
                    id          = su.id,
                    ownerLane   = su.isEnemy ? -1 : laneIndex,
                    type        = su.type,
                    pathIdx     = norm * Mathf.Max(0, (path?.Length ?? 1) - 1),
                    gridX       = su.x,
                    gridY       = su.y,
                    normProgress = norm,
                    hp          = su.hp,
                    maxHp       = su.maxHp,
                };
            }
            return result;
        }

        static float ComputeNormProgress(MLGridPos[] path, int x, int y)
        {
            if (path == null || path.Length <= 1) return 0f;
            for (int i = 0; i < path.Length; i++)
                if (path[i].x == x && path[i].y == y)
                    return (float)i / (path.Length - 1);
            return 0f;
        }

        // ── FX / Camera setup (mirrors GameManager) ───────────────────────────

        void InitPools()
        {
            if (HitEffectPrefab != null)    HitEffectPool.Init(HitEffectPrefab, HitEffectPoolSize);
            if (CannonSplashPrefab != null) CannonSplash.Init(CannonSplashPrefab, CannonSplashPoolSize);
            if (GoldPopPrefab != null)      GoldPop.Init(GoldPopPrefab, GoldPopPoolSize);
            if (FloatingTextPrefab != null) FloatingText.Prefab = FloatingTextPrefab;
        }

        void WireInfoBarAnchor()
        {
            if (CastleTileTransform == null) return;
            var infoBar = FindFirstObjectByType<InfoBar>();
            if (infoBar != null) infoBar.FloatTextAnchor = CastleTileTransform;
        }

        void EnsureCameraPOV()
        {
            if (!ForceCameraPreset) return;
            var cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null) return;
            cam.orthographic     = true;
            cam.orthographicSize = CameraOrthoSize;
            Vector3 castlePos  = CastleTileTransform != null
                ? CastleTileTransform.position
                : TileGrid.TileToWorld(5, 27);
            Vector3 spawnPos   = TileGrid.TileToWorld(5, 0);
            Vector3 laneCenter = (castlePos + spawnPos) * 0.5f + new Vector3(0f, 0f, CameraFrameOffsetZ);
            cam.transform.position = laneCenter + Vector3.up * CameraHeight;
            cam.transform.LookAt(laneCenter);
        }

        IEnumerator EnsureCameraPOVNextFrame()
        {
            yield return null;
            EnsureCameraPOV();
        }

        // ── Coroutine helpers ─────────────────────────────────────────────────

        static IEnumerator ScaleIn(Transform t, float from, float to, float dur)
        {
            t.localScale = Vector3.one * from;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                float c1 = 1.70158f, c3 = c1 + 1f, tm1 = n - 1f;
                float scale = (1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1) * (to - from) + from;
                t.localScale = Vector3.one * scale;
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }
    }
}
