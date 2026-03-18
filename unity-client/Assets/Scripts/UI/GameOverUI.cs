// GameOverUI.cs — Game over panel (VICTORY / DEFEAT) with rematch + lobby buttons.
//
// SETUP:
//   Canvas
//   └── PanelGameOver (inactive by default, scale starts at 0)
//       ├── Txt_Result      (TMP_Text)
//       ├── Txt_RematchBtn  (TMP_Text — inside BtnRematch)
//       ├── Btn_Rematch
//       ├── Btn_Lobby
//       └── Panel_Rating    (inactive by default — shown only for ranked matches)
//           └── Txt_Rating  (TMP_Text — e.g. "+32 Rating (1842 → 1874)")
//
// Works for both ML (ml_game_over) and Classic (game_over) scenes.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class GameOverUI : MonoBehaviour
    {
        private MLGameOverPayload _lastPayload;
        private bool _rematchRequested;
        public GameObject PanelGameOver;
        public TMP_Text   TxtResult;
        public Button     BtnRematch;
        public TMP_Text   TxtRematchBtn;
        public Button     BtnLobby;

        [Header("Rating Panel (Phase U8 — ranked only)")]
        public GameObject Panel_Rating;
        public TMP_Text   Txt_Rating;

        [Header("Phase 1 additions")]
        public TMP_Text Txt_CauseLoss;
        public TMP_Text Txt_Duration;
        [Header("Stats Panel")]
        public Button             BtnStats;
        public PostGameStatsPanel StatsPanel;

        void OnEnable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLGameOver          += HandleMLGameOver;
            nm.OnClassicGameOver     += HandleClassicGameOver;
            nm.OnRematchVote         += HandleRematchVote;
            nm.OnRematchStatus       += HandleRematchStatus;
            nm.OnRematchStarting     += HandleRematchStarting;
            nm.OnMLMatchReady        += HandleMatchRestarted;
            nm.OnClassicMatchReady   += HandleClassicMatchRestarted;
            nm.OnRatingUpdate        += HandleRatingUpdate;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnMLGameOver          -= HandleMLGameOver;
            nm.OnClassicGameOver     -= HandleClassicGameOver;
            nm.OnRematchVote         -= HandleRematchVote;
            nm.OnRematchStatus       -= HandleRematchStatus;
            nm.OnRematchStarting     -= HandleRematchStarting;
            nm.OnMLMatchReady        -= HandleMatchRestarted;
            nm.OnClassicMatchReady   -= HandleClassicMatchRestarted;
            nm.OnRatingUpdate        -= HandleRatingUpdate;
        }

        void Start()
        {
            PanelGameOver.SetActive(false);
            if (Panel_Rating != null) Panel_Rating.SetActive(false);
            if (BtnStats != null) BtnStats.gameObject.SetActive(false);
            BtnRematch.onClick.AddListener(OnRematch);
            BtnLobby.onClick.AddListener(OnLobby);
            if (BtnStats != null && StatsPanel != null)
                BtnStats.onClick.AddListener(() => StatsPanel.Show(_lastPayload));
        }

        // ─────────────────────────────────────────────────────────────────────
        void HandleMLGameOver(MLGameOverPayload p)
        {
            _lastPayload = p;
            _rematchRequested = false;
            bool isWinner = p.winnerLaneIndex == SnapshotApplier.Instance.MyLaneIndex;
            ShowPanel(isWinner);
            var myStat = GetMyFinalStat(p);
            if (Txt_CauseLoss != null)
            {
                string winnerLine = !string.IsNullOrEmpty(p.winnerName)
                    ? $"Winner: {p.winnerName}"
                    : (!string.IsNullOrEmpty(p.winningTeam) ? $"Winner: {p.winningTeam}" : "Match complete");
                Txt_CauseLoss.text = $"{winnerLine}  •  {p.causeLoss ?? "Lives reduced to 0"}";
            }
            if (Txt_Duration != null)
            {
                string duration = $"{p.gameDuration / 60}m {p.gameDuration % 60}s";
                if (myStat != null)
                {
                    Txt_Duration.text =
                        $"{duration}  •  Inc {myStat.income:F0}  •  Build {myStat.buildValue:F0}  •  Sends {myStat.totalSendSpend:F0}  •  Leaks {myStat.totalLeaksTaken}";
                }
                else
                {
                    Txt_Duration.text = duration;
                }
            }
            if (BtnStats != null)
                BtnStats.gameObject.SetActive(p.waveSnapshots != null && p.waveSnapshots.Length > 0);
            if (BtnRematch != null) BtnRematch.interactable = true;
            if (TxtRematchBtn != null) TxtRematchBtn.text = "Rematch";
        }

        void HandleClassicGameOver(ClassicGameOverPayload p)
        {
            bool isWinner = p.winner == NetworkManager.Instance.MySocketId;
            ShowPanel(isWinner);
        }

        void ShowPanel(bool isWinner)
        {
            TxtResult.text  = isWinner ? "VICTORY" : "DEFEAT";
            TxtResult.color = isWinner
                ? new Color(1f, 0.85f, 0.2f)
                : new Color(0.9f, 0.25f, 0.25f);

            PanelGameOver.SetActive(true);
            TxtRematchBtn.text = "⚔ Rematch";
            StartCoroutine(ScaleIn(PanelGameOver.transform, 0f, 1f, 0.35f));
            AudioManager.I?.Play(isWinner ? AudioManager.SFX.Victory : AudioManager.SFX.GameOver);
        }

        void HandleRematchVote(RematchVotePayload p)
        {
            if (TxtRematchBtn != null && _rematchRequested)
                TxtRematchBtn.text = $"Waiting... ({p.count}/{p.needed})";
        }

        void HandleRematchStatus(RematchStatusPayload p)
        {
            if (TxtRematchBtn == null) return;
            if (p == null)
            {
                TxtRematchBtn.text = _rematchRequested ? "Waiting..." : "Rematch";
                return;
            }

            if (p.allAccepted)
            {
                TxtRematchBtn.text = "Rematch starting...";
                if (BtnRematch != null) BtnRematch.interactable = false;
                return;
            }

            if (_rematchRequested)
            {
                TxtRematchBtn.text = $"Waiting... ({p.count}/{p.needed})";
                if (BtnRematch != null) BtnRematch.interactable = false;
            }
            else if (p.count > 0)
            {
                TxtRematchBtn.text = $"Opponent ready ({p.count}/{p.needed})";
                if (BtnRematch != null) BtnRematch.interactable = true;
            }
            else
            {
                TxtRematchBtn.text = "Rematch";
                if (BtnRematch != null) BtnRematch.interactable = true;
            }
        }

        void HandleRematchStarting(RematchStartingPayload _)
        {
            if (TxtRematchBtn != null) TxtRematchBtn.text = "Rematch starting...";
            if (BtnRematch != null) BtnRematch.interactable = false;
        }

        void HandleMatchRestarted(MLMatchReadyPayload _)             => HidePanel();
        void HandleClassicMatchRestarted(ClassicMatchReadyPayload _) => HidePanel();

        void HandleRatingUpdate(RatingUpdatePayload p)
        {
            if (!p.ranked || Panel_Rating == null || Txt_Rating == null) return;

            string sign   = p.delta >= 0 ? "+" : "";
            string change = $"{sign}{p.delta:F0}";
            Txt_Rating.text  = $"{change} Rating ({p.oldRating:F0} → {p.newRating:F0})";
            Txt_Rating.color = p.delta >= 0
                ? new Color(0.2f, 0.85f, 0.3f)
                : new Color(0.9f, 0.3f, 0.3f);

            Panel_Rating.SetActive(true);

            // Clear reconnect token on game end
            PlayerPrefs.DeleteKey("reconnect_token");
            PlayerPrefs.DeleteKey("reconnect_code");
            PlayerPrefs.DeleteKey("reconnect_lane");
            PlayerPrefs.DeleteKey("reconnect_gametype");
            PlayerPrefs.Save();
        }

        void HidePanel()
        {
            _rematchRequested = false;
            if (Panel_Rating != null) Panel_Rating.SetActive(false);
            if (StatsPanel != null) StatsPanel.HideImmediate();
            StartCoroutine(ScaleOut(PanelGameOver.transform, 0.2f));
        }

        void OnRematch()
        {
            if (_rematchRequested) return;
            _rematchRequested = true;
            ActionSender.RequestRematch();
            if (BtnRematch != null) BtnRematch.interactable = false;
            if (TxtRematchBtn != null) TxtRematchBtn.text = "Waiting...";
            AudioManager.I?.Play(AudioManager.SFX.Rematch);
        }

        void OnLobby()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            LoadingScreen.LoadScene("Lobby");
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
                // OutBack ease: overshoots slightly before settling
                float scale = EaseOutBack(n) * (to - from) + from;
                t.localScale = Vector3.one * scale;
                yield return null;
            }
            t.localScale = Vector3.one * to;
        }

        static IEnumerator ScaleOut(Transform t, float dur)
        {
            Vector3 startScale = t.localScale;
            float elapsed = 0f;
            while (elapsed < dur)
            {
                elapsed += Time.deltaTime;
                float n = Mathf.Clamp01(elapsed / dur);
                t.localScale = Vector3.Lerp(startScale, Vector3.zero, n * n);
                yield return null;
            }
            t.localScale = Vector3.zero;
            t.gameObject.SetActive(false);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float tm1 = t - 1f;
            return 1f + c3 * tm1 * tm1 * tm1 + c1 * tm1 * tm1;
        }

        MLFinalLaneStat GetMyFinalStat(MLGameOverPayload payload)
        {
            if (payload?.finalStats == null || SnapshotApplier.Instance == null) return null;
            int myLaneIndex = SnapshotApplier.Instance.MyLaneIndex;
            foreach (var stat in payload.finalStats)
            {
                if (stat != null && stat.laneIndex == myLaneIndex) return stat;
            }
            return null;
        }
    }
}
