// GameOverUI.cs - Game over panel with rematch, lobby, and battle report actions.

using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class GameOverUI : MonoBehaviour
    {
        MLGameOverPayload _lastPayload;
        bool _rematchRequested;

        public GameObject PanelGameOver;
        public TMP_Text TxtResult;
        public Button BtnRematch;
        public TMP_Text TxtRematchBtn;
        public Button BtnLobby;

        [Header("Rating Panel")]
        public GameObject Panel_Rating;
        public TMP_Text Txt_Rating;

        [Header("Phase 1 additions")]
        public TMP_Text Txt_CauseLoss;
        public TMP_Text Txt_Duration;

        [Header("Stats Panel")]
        public Button BtnStats;
        public PostGameStatsPanel StatsPanel;

        void OnEnable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null)
                return;

            nm.OnMLGameOver += HandleMLGameOver;
            nm.OnClassicGameOver += HandleClassicGameOver;
            nm.OnRematchVote += HandleRematchVote;
            nm.OnRematchStatus += HandleRematchStatus;
            nm.OnRematchStarting += HandleRematchStarting;
            nm.OnMLMatchReady += HandleMatchRestarted;
            nm.OnClassicMatchReady += HandleClassicMatchRestarted;
            nm.OnRatingUpdate += HandleRatingUpdate;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null)
                return;

            nm.OnMLGameOver -= HandleMLGameOver;
            nm.OnClassicGameOver -= HandleClassicGameOver;
            nm.OnRematchVote -= HandleRematchVote;
            nm.OnRematchStatus -= HandleRematchStatus;
            nm.OnRematchStarting -= HandleRematchStarting;
            nm.OnMLMatchReady -= HandleMatchRestarted;
            nm.OnClassicMatchReady -= HandleClassicMatchRestarted;
            nm.OnRatingUpdate -= HandleRatingUpdate;
        }

        void Start()
        {
            if (PanelGameOver != null)
                PanelGameOver.SetActive(false);
            if (Panel_Rating != null)
                Panel_Rating.SetActive(false);
            if (BtnStats != null)
            {
                BtnStats.gameObject.SetActive(false);
                var statsLabel = BtnStats.GetComponentInChildren<TMP_Text>(true);
                if (statsLabel != null)
                    statsLabel.text = "Battle Report";
            }

            if (BtnRematch != null)
                BtnRematch.onClick.AddListener(OnRematch);
            if (BtnLobby != null)
                BtnLobby.onClick.AddListener(OnLobby);
            if (BtnStats != null && StatsPanel != null)
                BtnStats.onClick.AddListener(() => StatsPanel.Show(_lastPayload));
        }

        void HandleMLGameOver(MLGameOverPayload payload)
        {
            _lastPayload = payload;
            _rematchRequested = false;

            int myLaneIndex = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
            bool isWinner = payload.winnerLaneIndex == myLaneIndex;
            ShowPanel(isWinner);

            var report = ResolveCommanderReport(payload);
            var myStat = GetMyFinalStat(payload);

            if (Txt_CauseLoss != null)
            {
                bool hasWinner = payload.winnerLaneIndex >= 0;
                string winnerLine = hasWinner && !string.IsNullOrEmpty(payload.winnerName)
                    ? $"Winner: {payload.winnerName}"
                    : (hasWinner && !string.IsNullOrEmpty(payload.winningTeam)
                        ? $"Winner: {payload.winningTeam}"
                        : "Survival run complete");
                Txt_CauseLoss.text = $"{winnerLine}  |  {payload.causeLoss ?? "Town Core destroyed"}";
            }

            if (Txt_Duration != null)
            {
                string duration = $"{payload.gameDuration / 60}m {payload.gameDuration % 60}s";
                if (report != null)
                {
                    Txt_Duration.text =
                        $"{duration}  |  Wave {report.matchHeader?.finalWave ?? payload.finalRound}  |  " +
                        $"Enemies {report.snapshot?.enemiesDefeated ?? 0}  |  " +
                        $"Breaches {report.snapshot?.breachesSuffered ?? 0}";
                }
                else if (myStat != null)
                {
                    Txt_Duration.text =
                        $"{duration}  |  Inc {myStat.income:F0}  |  Build {myStat.buildValue:F0}  |  " +
                        $"Sends {myStat.totalSendSpend:F0}  |  Breaches {myStat.totalLeaksTaken}";
                }
                else
                {
                    Txt_Duration.text = duration;
                }
            }

            if (BtnStats != null)
            {
                BtnStats.gameObject.SetActive(
                    (payload.playerBattleReport?.lanes != null && payload.playerBattleReport.lanes.Length > 0) ||
                    (payload.waveSnapshots != null && payload.waveSnapshots.Length > 0));
            }
            if (BtnRematch != null)
                BtnRematch.interactable = true;
            if (TxtRematchBtn != null)
                TxtRematchBtn.text = "Rematch";
        }

        void HandleClassicGameOver(ClassicGameOverPayload payload)
        {
            bool isWinner = payload.winner == NetworkManager.Instance.MySocketId;
            ShowPanel(isWinner);
        }

        void ShowPanel(bool isWinner)
        {
            if (TxtResult != null)
            {
                TxtResult.text = isWinner ? "VICTORY" : "DEFEAT";
                TxtResult.color = isWinner
                    ? new Color(1f, 0.85f, 0.2f)
                    : new Color(0.9f, 0.25f, 0.25f);
            }

            if (PanelGameOver != null)
            {
                PanelGameOver.SetActive(true);
                StartCoroutine(ScaleIn(PanelGameOver.transform, 0f, 1f, 0.35f));
            }
            if (TxtRematchBtn != null)
                TxtRematchBtn.text = "Rematch";
            AudioManager.I?.Play(isWinner ? AudioManager.SFX.Victory : AudioManager.SFX.GameOver);
        }

        void HandleRematchVote(RematchVotePayload payload)
        {
            if (TxtRematchBtn != null && _rematchRequested)
                TxtRematchBtn.text = $"Waiting... ({payload.count}/{payload.needed})";
        }

        void HandleRematchStatus(RematchStatusPayload payload)
        {
            if (TxtRematchBtn == null)
                return;

            if (payload == null)
            {
                TxtRematchBtn.text = _rematchRequested ? "Waiting..." : "Rematch";
                return;
            }

            if (payload.allAccepted)
            {
                TxtRematchBtn.text = "Rematch starting...";
                if (BtnRematch != null)
                    BtnRematch.interactable = false;
                return;
            }

            if (_rematchRequested)
            {
                TxtRematchBtn.text = $"Waiting... ({payload.count}/{payload.needed})";
                if (BtnRematch != null)
                    BtnRematch.interactable = false;
            }
            else if (payload.count > 0)
            {
                TxtRematchBtn.text = $"Opponent ready ({payload.count}/{payload.needed})";
                if (BtnRematch != null)
                    BtnRematch.interactable = true;
            }
            else
            {
                TxtRematchBtn.text = "Rematch";
                if (BtnRematch != null)
                    BtnRematch.interactable = true;
            }
        }

        void HandleRematchStarting(RematchStartingPayload _)
        {
            if (TxtRematchBtn != null)
                TxtRematchBtn.text = "Rematch starting...";
            if (BtnRematch != null)
                BtnRematch.interactable = false;
        }

        void HandleMatchRestarted(MLMatchReadyPayload _) => HidePanel();
        void HandleClassicMatchRestarted(ClassicMatchReadyPayload _) => HidePanel();

        void HandleRatingUpdate(RatingUpdatePayload payload)
        {
            if (!payload.ranked || Panel_Rating == null || Txt_Rating == null)
                return;

            string sign = payload.delta >= 0 ? "+" : "";
            string change = $"{sign}{payload.delta:F0}";
            Txt_Rating.text = $"{change} Rating ({payload.oldRating:F0} -> {payload.newRating:F0})";
            Txt_Rating.color = payload.delta >= 0
                ? new Color(0.2f, 0.85f, 0.3f)
                : new Color(0.9f, 0.3f, 0.3f);

            Panel_Rating.SetActive(true);

            PlayerPrefs.DeleteKey("reconnect_token");
            PlayerPrefs.DeleteKey("reconnect_code");
            PlayerPrefs.DeleteKey("reconnect_lane");
            PlayerPrefs.DeleteKey("reconnect_gametype");
            PlayerPrefs.Save();
        }

        void HidePanel()
        {
            _rematchRequested = false;
            if (Panel_Rating != null)
                Panel_Rating.SetActive(false);
            if (StatsPanel != null)
                StatsPanel.HideImmediate();
            if (PanelGameOver != null)
                StartCoroutine(ScaleOut(PanelGameOver.transform, 0.2f));
        }

        void OnRematch()
        {
            if (_rematchRequested)
                return;

            _rematchRequested = true;
            ActionSender.RequestRematch();
            if (BtnRematch != null)
                BtnRematch.interactable = false;
            if (TxtRematchBtn != null)
                TxtRematchBtn.text = "Waiting...";
            AudioManager.I?.Play(AudioManager.SFX.Rematch);
        }

        void OnLobby()
        {
            AudioManager.I?.Play(AudioManager.SFX.ButtonClick);
            LoadingScreen.LoadScene("Lobby");
        }

        static IEnumerator ScaleIn(Transform target, float from, float to, float duration)
        {
            target.localScale = Vector3.one * from;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                float scale = EaseOutBack(normalized) * (to - from) + from;
                target.localScale = Vector3.one * scale;
                yield return null;
            }

            target.localScale = Vector3.one * to;
        }

        static IEnumerator ScaleOut(Transform target, float duration)
        {
            Vector3 startScale = target.localScale;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float normalized = Mathf.Clamp01(elapsed / duration);
                target.localScale = Vector3.Lerp(startScale, Vector3.zero, normalized * normalized);
                yield return null;
            }

            target.localScale = Vector3.zero;
            target.gameObject.SetActive(false);
        }

        static float EaseOutBack(float value)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float minusOne = value - 1f;
            return 1f + c3 * minusOne * minusOne * minusOne + c1 * minusOne * minusOne;
        }

        MLFinalLaneStat GetMyFinalStat(MLGameOverPayload payload)
        {
            if (payload?.finalStats == null || SnapshotApplier.Instance == null)
                return null;

            int myLaneIndex = SnapshotApplier.Instance.MyLaneIndex;
            foreach (var stat in payload.finalStats)
            {
                if (stat != null && stat.laneIndex == myLaneIndex)
                    return stat;
            }

            return null;
        }

        CommanderBattleReport ResolveCommanderReport(MLGameOverPayload payload)
        {
            if (payload?.playerBattleReport?.lanes == null || payload.playerBattleReport.lanes.Length == 0)
                return null;

            int myLaneIndex = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.MyLaneIndex : -1;
            foreach (var laneReport in payload.playerBattleReport.lanes)
            {
                if (laneReport != null && laneReport.laneIndex == myLaneIndex)
                    return laneReport;
            }

            foreach (var laneReport in payload.playerBattleReport.lanes)
            {
                if (laneReport != null && laneReport.laneIndex == payload.winnerLaneIndex)
                    return laneReport;
            }

            return payload.playerBattleReport.lanes[0];
        }
    }
}
