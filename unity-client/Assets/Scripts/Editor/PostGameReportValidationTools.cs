#if UNITY_EDITOR
using System;
using CastleDefender.Net;
using CastleDefender.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace CastleDefender.Editor
{
    public static class PostGameReportValidationTools
    {
        const string ScreenshotPath = "projects/postgame-report-validation.png";
        static int s_framesUntilCapture = -1;

        static PostGameReportValidationTools()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Show Mock Report")]
        public static void ShowMockReport()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[PostGameReportValidation] Enter Play Mode first.");
                return;
            }

            if (SceneManager.GetActiveScene().name != "PostGame")
            {
                Debug.LogWarning("[PostGameReportValidation] Load the PostGame scene before showing the mock report.");
                return;
            }

            var controller = Object.FindFirstObjectByType<PostGameSceneController>();
            if (controller == null)
            {
                Debug.LogError("[PostGameReportValidation] No PostGameSceneController is active.");
                return;
            }

            controller.DebugShowReport(BuildMockPayload(), openReportPanel: true);
            LogVisibleReportState();
        }

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Capture Mock Report Screenshot")]
        public static void CaptureMockReportScreenshot()
        {
            ShowMockReport();
            if (!Application.isPlaying || SceneManager.GetActiveScene().name != "PostGame")
                return;

            s_framesUntilCapture = 10;
        }

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Validate Report Panels")]
        public static void ValidateReportPanels()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[PostGameReportValidation] Enter Play Mode first.");
                return;
            }

            if (SceneManager.GetActiveScene().name != "PostGame")
            {
                Debug.LogWarning("[PostGameReportValidation] Load the PostGame scene before validating report panels.");
                return;
            }

            var controller = Object.FindFirstObjectByType<PostGameSceneController>();
            if (controller == null)
            {
                Debug.LogError("[PostGameReportValidation] No PostGameSceneController is active.");
                return;
            }

            controller.DebugShowReport(BuildMockPayload(), openReportPanel: true);

            var panel = Object.FindFirstObjectByType<PostGameStatsPanel>();
            if (panel == null)
            {
                Debug.LogError("[PostGameReportValidation] No PostGameStatsPanel is active.");
                return;
            }

            panel.StartCoroutine(ValidatePanelSequence(panel));
        }

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Open Summary Tab")]
        public static void OpenSummaryTab() => InvokeTabButton(panel => panel.Btn_Tab_Summary, "Summary");

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Open Economy Tab")]
        public static void OpenEconomyTab() => InvokeTabButton(panel => panel.Btn_Tab_Economy, "Economy");

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Open Build Tab")]
        public static void OpenBuildTab() => InvokeTabButton(panel => panel.Btn_Tab_Build, "Build");

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Open Waves Tab")]
        public static void OpenWavesTab() => InvokeTabButton(panel => panel.Btn_Tab_Waves, "Waves");

        [MenuItem("Castle Defender/Remote Scene Validation/PostGame/Press Close")]
        public static void PressClose()
        {
            if (!TryGetPanelForValidation(out var panel))
                return;

            if (panel.Btn_Close == null)
            {
                Debug.LogWarning("[PostGameReportValidation] Btn_Close is missing.");
                return;
            }

            panel.Btn_Close.onClick.Invoke();
        }

        static void OnEditorUpdate()
        {
            if (!Application.isPlaying || s_framesUntilCapture < 0)
                return;

            s_framesUntilCapture--;
            if (s_framesUntilCapture > 0)
                return;

            s_framesUntilCapture = -1;
            ScreenCapture.CaptureScreenshot(ScreenshotPath);
            Debug.Log($"[PostGameReportValidation] Captured screenshot to '{ScreenshotPath}'.");
            LogVisibleReportState();
        }

        static System.Collections.IEnumerator ValidatePanelSequence(PostGameStatsPanel panel)
        {
            if (panel == null)
                yield break;

            yield return null;
            LogTabResult("Summary", panel.Btn_Tab_Summary, panel.PanelSummary);
            yield return null;

            LogTabResult("Economy", panel.Btn_Tab_Economy, panel.PanelEconomy);
            yield return null;

            LogTabResult("Build", panel.Btn_Tab_Build, panel.PanelBuild);
            yield return null;

            LogTabResult("Waves", panel.Btn_Tab_Waves, panel.PanelWaves);
            yield return null;

            if (panel.Btn_Close == null)
            {
                Debug.LogWarning("[PostGameReportValidation] Close validation skipped because Btn_Close is missing.");
            }
            else
            {
                panel.Btn_Close.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.25f);
                bool closed = panel.PanelRoot == null || !panel.PanelRoot.activeInHierarchy;
                Debug.Log($"[PostGameReportValidation] Close button validation: closed={closed}.");
            }

            panel.Show(BuildMockPayload());
            yield return null;
            LogTabResult("Summary (reopened)", panel.Btn_Tab_Summary, panel.PanelSummary);
            LogVisibleReportState();
        }

        static void InvokeTabButton(Func<PostGameStatsPanel, UnityEngine.UI.Button> selector, string label)
        {
            if (!TryGetPanelForValidation(out var panel))
                return;

            var button = selector(panel);
            if (button == null)
            {
                Debug.LogWarning($"[PostGameReportValidation] {label} tab button is missing.");
                return;
            }

            button.onClick.Invoke();
        }

        static bool TryGetPanelForValidation(out PostGameStatsPanel panel)
        {
            panel = null;

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[PostGameReportValidation] Enter Play Mode first.");
                return false;
            }

            if (SceneManager.GetActiveScene().name != "PostGame")
            {
                Debug.LogWarning("[PostGameReportValidation] Load the PostGame scene before validating report panels.");
                return false;
            }

            var controller = Object.FindFirstObjectByType<PostGameSceneController>();
            if (controller == null)
            {
                Debug.LogError("[PostGameReportValidation] No PostGameSceneController is active.");
                return false;
            }

            controller.DebugShowReport(BuildMockPayload(), openReportPanel: true);
            panel = Object.FindFirstObjectByType<PostGameStatsPanel>();
            if (panel == null)
            {
                Debug.LogError("[PostGameReportValidation] No PostGameStatsPanel is active.");
                return false;
            }

            return true;
        }

        static void LogTabResult(string tabName, UnityEngine.UI.Button button, GameObject expectedPanel)
        {
            if (button == null)
            {
                Debug.LogWarning($"[PostGameReportValidation] {tabName} validation skipped because the button reference is missing.");
                return;
            }

            button.onClick.Invoke();
            bool active = expectedPanel != null && expectedPanel.activeInHierarchy;
            Debug.Log($"[PostGameReportValidation] {tabName} tab validation: active={active}, panel={expectedPanel?.name ?? "null"}.");
        }

        static void LogVisibleReportState()
        {
            var summary = Object.FindFirstObjectByType<PostGameStatsPanel>();
            if (summary == null)
            {
                Debug.LogWarning("[PostGameReportValidation] No PostGameStatsPanel is active.");
                return;
            }

            string summaryText = summary.SummaryBodyText != null ? summary.SummaryBodyText.text : "<no summary text>";
            string wavesText = summary.WavesBodyText != null ? summary.WavesBodyText.text : "<no waves text>";
            Debug.Log(
                "[PostGameReportValidation] Visible report state: " +
                $"panelActive={summary.PanelRoot != null && summary.PanelRoot.activeInHierarchy}, " +
                $"summaryActive={summary.PanelSummary != null && summary.PanelSummary.activeInHierarchy}, " +
                $"wavesActive={summary.PanelWaves != null && summary.PanelWaves.activeInHierarchy}, " +
                $"summaryChars={summaryText.Length}, wavesChars={wavesText.Length}.");
            Debug.Log($"[PostGameReportValidation] Summary text:\n{summaryText}");
            Debug.Log($"[PostGameReportValidation] Waves text:\n{wavesText}");
        }

        static MLGameOverPayload BuildMockPayload()
        {
            return new MLGameOverPayload
            {
                winnerLaneIndex = 0,
                winnerName = "Blue Commander",
                winningTeam = "blue",
                winningSide = "left",
                finalRound = 10,
                matchState = "final_game_over",
                gameDuration = 625,
                causeLoss = "Survival ended on Wave 10",
                continuedIntoSurvival = true,
                survivalDuration = 625,
                survivalExtraRounds = 9,
                pvpWinnerLaneIndex = 0,
                finalStats = new[]
                {
                    new MLFinalLaneStat
                    {
                        laneIndex = 0,
                        displayName = "Blue Commander",
                        team = "blue",
                        side = "left",
                        income = 42,
                        buildValue = 1825,
                        gold = 115,
                        totalSendSpend = 620,
                        totalSendCount = 16,
                        totalBuildSpend = 1420,
                        totalLeaksTaken = 1,
                        biggestLeakTaken = 3,
                        wavesHeld = 8,
                        wavesLeaked = 2,
                        longestHoldStreak = 5,
                        lives = 17,
                        teamHp = 17,
                        eliminated = false,
                    },
                    new MLFinalLaneStat
                    {
                        laneIndex = 1,
                        displayName = "Red Commander",
                        team = "red",
                        side = "right",
                        income = 38,
                        buildValue = 1490,
                        gold = 72,
                        totalSendSpend = 540,
                        totalSendCount = 13,
                        totalBuildSpend = 1260,
                        totalLeaksTaken = 4,
                        biggestLeakTaken = 7,
                        wavesHeld = 6,
                        wavesLeaked = 4,
                        longestHoldStreak = 3,
                        lives = 9,
                        teamHp = 9,
                        eliminated = true,
                    },
                },
                waveSnapshots = new[]
                {
                    BuildWave(9, 540, 41, 1740, 0, 0, 80, 2, 140, "Held", 18, 1, 36, 1400, 1, 2, 60, 2, 110, "Leaked", 11),
                    BuildWave(10, 625, 42, 1825, 1, 3, 95, 3, 120, "Leaked", 17, 1, 38, 1490, 3, 6, 70, 2, 90, "Crushed", 9, terminal: true),
                },
                balanceReadableLog = new[]
                {
                    "Wave 9 (late_game) gold 88->115, clear 62s, losses 2, pressure 48, struggle 32, ratio 1.12",
                    "Wave 10 (late_game) gold 115->72, clear uncleared, losses 6, pressure 91, struggle 87, ratio 0.58",
                },
                balanceDiagnosisLines = new[]
                {
                    "Early game: stable.",
                    "Mid game: manageable.",
                    "Late game: overtuned.",
                    "Snowball detected: no.",
                    "Economy overflow: no.",
                    "Likely tuning targets: inspect wave 10 boss reach and defender retargeting.",
                },
            };
        }

        static MLWaveSnapshot BuildWave(
            int round,
            int elapsedSeconds,
            float leftIncome,
            float leftBuild,
            int leftLeaks,
            int leftLeakDamage,
            float leftSendSpend,
            int leftSendCount,
            float leftBuildSpend,
            string leftResult,
            int leftHp,
            int rightLaneIndex,
            float rightIncome,
            float rightBuild,
            int rightLeaks,
            int rightLeakDamage,
            float rightSendSpend,
            int rightSendCount,
            float rightBuildSpend,
            string rightResult,
            int rightHp,
            bool terminal = false)
        {
            return new MLWaveSnapshot
            {
                round = round,
                elapsedSeconds = elapsedSeconds,
                terminal = terminal,
                lanes = new[]
                {
                    new MLWaveLaneStat
                    {
                        laneIndex = 0,
                        income = leftIncome,
                        buildValue = leftBuild,
                        gold = 0,
                        leaksTaken = leftLeaks,
                        leakDamage = leftLeakDamage,
                        sendSpend = leftSendSpend,
                        sendCount = leftSendCount,
                        buildSpend = leftBuildSpend,
                        lives = leftHp,
                        teamHp = leftHp,
                        eliminated = false,
                        holdResult = leftResult,
                    },
                    new MLWaveLaneStat
                    {
                        laneIndex = rightLaneIndex,
                        income = rightIncome,
                        buildValue = rightBuild,
                        gold = 0,
                        leaksTaken = rightLeaks,
                        leakDamage = rightLeakDamage,
                        sendSpend = rightSendSpend,
                        sendCount = rightSendCount,
                        buildSpend = rightBuildSpend,
                        lives = rightHp,
                        teamHp = rightHp,
                        eliminated = rightHp <= 0,
                        holdResult = rightResult,
                    },
                },
            };
        }
    }
}
#endif
