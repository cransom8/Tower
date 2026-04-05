using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class PostGameSceneController : MonoBehaviour
    {
        const string SceneName = "PostGame";
        const float StableOpenLogDelaySeconds = 0.2f;

        Canvas _canvas;
        TMP_Text _resultText;
        TMP_Text _winnerText;
        TMP_Text _causeText;
        TMP_Text _summaryText;
        TMP_Text _rematchLabel;
        Button _rematchButton;
        Button _lobbyButton;
        Button _statsButton;
        PostGameStatsPanel _statsPanel;
        MLGameOverPayload _payload;
        bool _rematchRequested;
        bool _lobbyRequested;
        bool _transitioningToLoadout;

        void Awake()
        {
            EnsureUi();
        }

        void OnEnable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnRematchStatus += HandleRematchStatus;
            nm.OnRematchStarting += HandleRematchStarting;
            nm.OnMLLoadoutPhaseStart += HandleLoadoutPhaseStart;
            nm.OnMLMatchReady += HandleMatchReady;
        }

        void OnDisable()
        {
            var nm = NetworkManager.Instance;
            if (nm == null) return;
            nm.OnRematchStatus -= HandleRematchStatus;
            nm.OnRematchStarting -= HandleRematchStarting;
            nm.OnMLLoadoutPhaseStart -= HandleLoadoutPhaseStart;
            nm.OnMLMatchReady -= HandleMatchReady;
        }

        void Start()
        {
            var nm = NetworkManager.Instance;
            _payload = nm != null ? nm.LastMLGameOver : null;
            bool injectedEditorPayload = false;
#if UNITY_EDITOR
            if (_payload == null)
            {
                _payload = BuildEditorValidationPayload();
                injectedEditorPayload = true;
                Debug.LogWarning("[PostGameFlow] No game-over payload found in editor Play Mode. Injecting mock payload so the report UI can be validated.");
            }
#endif
            ShowPayload(_payload);
            Debug.Log(
                $"[PostGameFlow] Post-game opened. scene={SceneName} " +
                $"result={_resultText?.text ?? "UNKNOWN"} winnerLane={_payload?.winnerLaneIndex ?? -1} round={_payload?.finalRound ?? -1}.");

#if UNITY_EDITOR
            if (injectedEditorPayload && _statsPanel != null)
                StartCoroutine(ValidateInjectedEditorReport());
#endif

            StartCoroutine(EnsureEventSystemReady());

            if (nm != null && nm.LastRematchStatus != null)
                HandleRematchStatus(nm.LastRematchStatus);

            if (ShouldResumePendingLoadoutTransition(nm))
            {
                BeginLoadoutTransition(nm.PendingLoadoutPhase);
            }
            else
            {
                StartCoroutine(LogStableOpenState());
            }
        }

        void ShowPayload(MLGameOverPayload payload)
        {
            if (_resultText == null) return;

            if (payload == null)
            {
                _resultText.text = "MATCH COMPLETE";
                _winnerText.text = "No post-game data available.";
                _causeText.text = "Exit to Lobby or start a new match.";
                _summaryText.text = string.Empty;
                return;
            }

            int myLaneIndex = NetworkManager.Instance != null ? NetworkManager.Instance.MyLaneIndex : -1;
            bool isWinner = payload.winnerLaneIndex == myLaneIndex;
            bool hasWinner = payload.winnerLaneIndex >= 0;
            _resultText.text = isWinner ? "VICTORY" : "DEFEAT";
            _resultText.color = isWinner
                ? new Color(1f, 0.84f, 0.25f)
                : new Color(0.92f, 0.34f, 0.32f);

            _winnerText.text = hasWinner && !string.IsNullOrWhiteSpace(payload.winnerName)
                ? $"Winner: {payload.winnerName}"
                : (hasWinner && !string.IsNullOrWhiteSpace(payload.winningTeam) ? $"Winner: {payload.winningTeam}" : "Survival run complete");

            int survivalSeconds = payload.survivalDuration > 0 ? payload.survivalDuration : payload.gameDuration;
            string summaryCause = !string.IsNullOrWhiteSpace(payload.causeLoss)
                ? payload.causeLoss
                : $"Survival ended on Wave {payload.finalRound}";
            string baseSummary = $"{summaryCause}  -  Survival {survivalSeconds / 60}m {survivalSeconds % 60}s";
            _causeText.text = baseSummary;

            var report = ResolveCommanderReport(payload, myLaneIndex);
            if (report != null)
            {
                _winnerText.text = $"{report.matchHeader?.resultLabel ?? _resultText.text}  |  Final Wave {report.matchHeader?.finalWave ?? payload.finalRound}";
                _summaryText.text =
                    $"Enemies Defeated {report.snapshot?.enemiesDefeated ?? 0}    " +
                    $"Units Recruited {report.snapshot?.unitsRecruited ?? 0}    " +
                    $"Breaches {report.snapshot?.breachesSuffered ?? 0}    " +
                    $"Core {report.snapshot?.coreHealthRemaining ?? 0} left";
                return;
            }

            var myStat = GetMyStat(payload, myLaneIndex);
            if (myStat != null)
            {
                string continuation = $"    Final Wave {payload.finalRound}";
                _summaryText.text =
                    $"Income {myStat.income:F0}    Build {myStat.buildValue:F0}    Sends {myStat.totalSendSpend:F0}/{myStat.totalSendCount}    " +
                    $"Breaches {myStat.totalLeaksTaken}    Hold {myStat.longestHoldStreak}{continuation}";
            }
            else
            {
                _summaryText.text = $"Final Wave {payload.finalRound}";
            }
        }

        CommanderBattleReport ResolveCommanderReport(MLGameOverPayload payload, int myLaneIndex)
        {
            if (payload?.playerBattleReport?.lanes == null || payload.playerBattleReport.lanes.Length == 0)
                return null;

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

        void HandleRematchStatus(RematchStatusPayload payload)
        {
            if (_lobbyRequested) return;
            if (_rematchLabel == null || _rematchButton == null) return;
            if (payload == null)
            {
                _rematchLabel.text = _rematchRequested ? "Waiting for opponent..." : "Ready for rematch?";
                _rematchButton.interactable = !_rematchRequested;
                return;
            }

            if (payload.allAccepted)
            {
                _rematchLabel.text = "Rematch accepted. Starting...";
                _rematchButton.interactable = false;
                return;
            }

            if (_rematchRequested)
            {
                _rematchLabel.text = $"Waiting for opponent... ({payload.count}/{payload.needed})";
                _rematchButton.interactable = false;
            }
            else if (payload.count > 0)
            {
                _rematchLabel.text = $"Opponent accepted rematch ({payload.count}/{payload.needed})";
                _rematchButton.interactable = true;
            }
            else
            {
                _rematchLabel.text = "Ready for rematch?";
                _rematchButton.interactable = true;
            }
        }

        void HandleRematchStarting(RematchStartingPayload _)
        {
            if (_lobbyRequested) return;
            if (_rematchLabel != null) _rematchLabel.text = "Rematch starting...";
            if (_rematchButton != null) _rematchButton.interactable = false;
        }

        void HandleMatchReady(MLMatchReadyPayload _)
        {
            if (_lobbyRequested || _transitioningToLoadout) return;
            Debug.Log("[PostGameFlow] Rematch accepted. Preparing loadout readiness from post-game.");
            if (_rematchLabel != null) _rematchLabel.text = "Preparing rematch...";
            StartCoroutine(WaitForContentAndEmitLoadoutReady());
        }

        System.Collections.IEnumerator WaitForContentAndEmitLoadoutReady()
        {
            var rc = RemoteContentManager.EnsureInstance();
            if (!rc.HasCompletedLoadoutPreload)
            {
                Debug.Log("[PostGameFlow] Loadout content not ready; preloading before rematch.");
                yield return rc.PreloadLoadoutContentForSession(requester: "PostGame.MatchReady");
            }

            Debug.Log("[PostGameFlow] Emitting ml_loadout_ready from post-game.");
            NetworkManager.Instance?.RequestLoadoutReady();
        }

        void HandleLoadoutPhaseStart(MLLoadoutPhaseStartPayload payload)
        {
            if (_lobbyRequested) return;
            BeginLoadoutTransition(payload);
        }

        void BeginLoadoutTransition(MLLoadoutPhaseStartPayload payload = null)
        {
            if (_transitioningToLoadout) return;
            _transitioningToLoadout = true;
            Debug.Log("[PostGameFlow] Transitioning cleanly from post-game to loadout.");
            NetworkManager.Instance?.ClearPostGameData();
            LoadingScreen.LoadSceneWithRemoteContentGate(
                "Loadout",
                portraitKeys: RaceProgressionCatalog.GetPortraitWarmupKeys(payload?.availableRaceIds));
        }

        void OnRematch()
        {
            if (_rematchRequested || _lobbyRequested || _transitioningToLoadout) return;
            _rematchRequested = true;
            _rematchLabel.text = "Waiting for opponent...";
            _rematchButton.interactable = false;
            Debug.Log("[PostGameFlow] Rematch clicked. Waiting for opponent vote.");
            ActionSender.RequestRematch();
        }

        void OnLobby()
        {
            if (_lobbyRequested || _transitioningToLoadout) return;
            _lobbyRequested = true;
            if (_rematchButton != null) _rematchButton.interactable = false;
            if (_lobbyButton != null) _lobbyButton.interactable = false;
            Debug.Log("[PostGameFlow] Exit to Lobby clicked.");
            ClearReconnectPrefs();
            NetworkManager.Instance?.Emit("leave_game", null);
            NetworkManager.Instance?.ClearPostGameData();
            Debug.Log("[PostGameFlow] Transitioning cleanly from post-game to lobby.");
            LoadingScreen.LoadScene("Lobby");
        }

        void OnStats()
        {
            if (_statsPanel != null && _payload != null)
            {
                int finalStatsCount = _payload.finalStats != null ? _payload.finalStats.Length : 0;
                int waveSnapshotCount = _payload.waveSnapshots != null ? _payload.waveSnapshots.Length : 0;
                int readableLogCount = _payload.balanceReadableLog != null ? _payload.balanceReadableLog.Length : 0;
                int diagnosisCount = _payload.balanceDiagnosisLines != null ? _payload.balanceDiagnosisLines.Length : 0;
                Debug.Log(
                    $"[PostGameReport] View Report clicked. " +
                    $"finalStats={finalStatsCount} waveSnapshots={waveSnapshotCount} " +
                    $"readableLog={readableLogCount} diagnosis={diagnosisCount}.");
                if (!_statsPanel.gameObject.activeSelf)
                    _statsPanel.gameObject.SetActive(true);
                _statsPanel.Show(_payload);
            }
        }

        MLFinalLaneStat GetMyStat(MLGameOverPayload payload, int myLaneIndex)
        {
            if (payload == null || payload.finalStats == null) return null;
            foreach (var stat in payload.finalStats)
            {
                if (stat != null && stat.laneIndex == myLaneIndex) return stat;
            }
            return null;
        }

        void EnsureUi()
        {
            if (_canvas != null && _resultText != null)
                return;

            _canvas = FindCanvasInScene(gameObject.scene);
            if (_canvas == null)
            {
                var canvasGo = new GameObject("PostGameCanvas");
                SceneManager.MoveGameObjectToScene(canvasGo, gameObject.scene);
                _canvas = canvasGo.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 200;
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                canvasGo.AddComponent<GraphicRaycaster>();
            }
            else if (_canvas.GetComponent<GraphicRaycaster>() == null)
            {
                _canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            var backdrop = MakePanel(_canvas.transform, "Backdrop", new Color(0.04f, 0.03f, 0.05f, 1f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var card = MakePanel(backdrop.transform, "Card", new Color(0.10f, 0.08f, 0.12f, 0.96f), new Vector2(0.16f, 0.12f), new Vector2(0.84f, 0.88f), Vector2.zero, Vector2.zero);

            _resultText = MakeText(card.transform, "TxtResult", "VICTORY", 42, new Vector2(0.08f, 0.78f), new Vector2(0.92f, 0.94f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            _winnerText = MakeText(card.transform, "TxtWinner", "Winner", 22, new Vector2(0.08f, 0.68f), new Vector2(0.92f, 0.78f), FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            _causeText = MakeText(card.transform, "TxtCause", "Cause", 18, new Vector2(0.08f, 0.60f), new Vector2(0.92f, 0.68f), FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            _summaryText = MakeText(card.transform, "TxtSummary", "Summary", 16, new Vector2(0.08f, 0.50f), new Vector2(0.92f, 0.60f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
            _rematchLabel = MakeText(card.transform, "TxtRematchStatus", "Ready for rematch?", 16, new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.42f), FontStyles.Italic, TextAlignmentOptions.MidlineLeft);

            _rematchButton = MakeButton(card.transform, "BtnRematch", "Rematch", new Vector2(0.08f, 0.18f), new Vector2(0.32f, 0.28f), new Color(0.72f, 0.23f, 0.18f, 1f));
            _lobbyButton = MakeButton(card.transform, "BtnLobby", "Exit to Lobby", new Vector2(0.36f, 0.18f), new Vector2(0.62f, 0.28f), new Color(0.23f, 0.25f, 0.30f, 1f));
            _statsButton = MakeButton(card.transform, "BtnStats", "Battle Report", new Vector2(0.66f, 0.18f), new Vector2(0.92f, 0.28f), new Color(0.18f, 0.38f, 0.56f, 1f));

            _rematchButton.onClick.AddListener(OnRematch);
            _lobbyButton.onClick.AddListener(OnLobby);
            _statsButton.onClick.AddListener(OnStats);

            _statsPanel = BuildStatsPanel(backdrop.transform);
        }

        bool ShouldResumePendingLoadoutTransition(NetworkManager nm)
        {
            if (nm == null || nm.PendingLoadoutPhase == null)
                return false;

            if (string.Equals(nm.CurrentMLMatchState, "final_game_over", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[PostGameFlow] Ignoring stale pending loadout phase while post-game modal is active.");
                return false;
            }

            return true;
        }

        System.Collections.IEnumerator EnsureEventSystemReady()
        {
            yield return null;
            yield return null;

            SceneEventSystemUtility.EnsureSceneLocal(this, "PostGameEventSystem", "PostGameFlow");
        }

#if UNITY_EDITOR
        System.Collections.IEnumerator ValidateInjectedEditorReport()
        {
            if (_statsPanel == null || _payload == null)
                yield break;

            if (!_statsPanel.gameObject.activeSelf)
                _statsPanel.gameObject.SetActive(true);

            _statsPanel.Show(_payload);
            yield return null;

            ValidateEditorReportTab("Economy", _statsPanel.Btn_Tab_Economy, _statsPanel.PanelEconomy);
            yield return null;

            ValidateEditorReportTab("Army", _statsPanel.Btn_Tab_Build, _statsPanel.PanelBuild);
            yield return null;

            ValidateEditorReportTab("Threat", _statsPanel.Btn_Tab_Threat, _statsPanel.PanelThreat);
            yield return null;

            ValidateEditorReportTab("Story", _statsPanel.Btn_Tab_Story, _statsPanel.PanelStory);
            yield return null;

            ValidateEditorReportTab("Advanced", _statsPanel.Btn_Tab_Waves, _statsPanel.PanelWaves);
            yield return null;

            ValidateEditorReportTab("Summary", _statsPanel.Btn_Tab_Summary, _statsPanel.PanelSummary);
            yield return null;

            if (_statsPanel.Btn_Close != null)
            {
                _statsPanel.Btn_Close.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.25f);

                bool panelClosed = _statsPanel.PanelRoot == null || !_statsPanel.PanelRoot.activeSelf;
                Debug.Log($"[PostGameFlow] Close validation. panelClosed={panelClosed}.");
            }

            _statsPanel.Show(_payload);
            yield return null;
            ValidateEditorReportTab("Summary (reopened)", _statsPanel.Btn_Tab_Summary, _statsPanel.PanelSummary);
        }

        void ValidateEditorReportTab(string tabName, Button button, GameObject panel)
        {
            if (button == null)
            {
                Debug.LogWarning($"[PostGameFlow] {tabName} tab validation skipped because the button reference is missing.");
                return;
            }

            button.onClick.Invoke();
            bool panelActive = panel != null && panel.activeSelf;
            Debug.Log($"[PostGameFlow] {tabName} tab validation. panelActive={panelActive} panel={panel?.name ?? "null"}.");
        }
#endif

        System.Collections.IEnumerator LogStableOpenState()
        {
            yield return new WaitForSecondsRealtime(StableOpenLogDelaySeconds);

            if (_lobbyRequested || _transitioningToLoadout)
                yield break;

            bool modalActive =
                _canvas != null &&
                _canvas.gameObject.activeInHierarchy &&
                _rematchButton != null &&
                _rematchButton.gameObject.activeInHierarchy;
            Debug.Log($"[PostGameFlow] Post-game remained active and is awaiting input (modalActive={modalActive}).");
        }

        static Canvas FindCanvasInScene(Scene scene)
        {
            if (!scene.IsValid())
                return null;

            foreach (var root in scene.GetRootGameObjects())
            {
                var canvas = root.GetComponentInChildren<Canvas>(true);
                if (canvas != null)
                    return canvas;
            }

            return null;
        }

        static void ClearReconnectPrefs()
        {
            PlayerPrefs.DeleteKey("reconnect_token");
            PlayerPrefs.DeleteKey("reconnect_code");
            PlayerPrefs.DeleteKey("reconnect_lane");
            PlayerPrefs.DeleteKey("reconnect_gametype");
            PlayerPrefs.Save();
        }

        PostGameStatsPanel BuildStatsPanel(Transform parent)
        {
            var root = MakePanel(parent, "PanelPostGameStats", new Color(0.04f, 0.04f, 0.10f, 0.98f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.SetActive(false);

            var panel = root.AddComponent<PostGameStatsPanel>();
            panel.PanelRoot = root;

            var header = MakePanel(root.transform, "PanelHeader", new Color(0.10f, 0.10f, 0.18f, 1f), new Vector2(0f, 0.92f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
            var layout = header.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;

            panel.Btn_Tab_Summary = MakeLayoutButton(header.transform, "BtnTabSummary", "Summary");
            panel.Btn_Tab_Economy = MakeLayoutButton(header.transform, "BtnTabEconomy", "Economy");
            panel.Btn_Tab_Build = MakeLayoutButton(header.transform, "BtnTabBuild", "Army");
            panel.Btn_Tab_Threat = MakeLayoutButton(header.transform, "BtnTabThreat", "Threat");
            panel.Btn_Tab_Story = MakeLayoutButton(header.transform, "BtnTabStory", "Story");
            panel.Btn_Tab_Waves = MakeLayoutButton(header.transform, "BtnTabWaves", "Advanced");
            panel.Btn_Close = MakeLayoutButton(header.transform, "BtnClose", "Close");

            var content = new GameObject("Content");
            content.transform.SetParent(root.transform, false);
            var contentRt = content.AddComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = new Vector2(1f, 0.92f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;

            Color reportPanelColor = new Color(0.05f, 0.05f, 0.09f, 0.96f);

            panel.PanelSummary = MakePanel(content.transform, "PanelSummary", reportPanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.SummaryRows = Array.Empty<TMP_Text>();
            panel.SummaryBodyText = MakeText(
                panel.PanelSummary.transform,
                "SummaryBody",
                string.Empty,
                16,
                new Vector2(0.03f, 0.04f),
                new Vector2(0.97f, 0.96f),
                FontStyles.Normal,
                TextAlignmentOptions.TopLeft);
            panel.SummaryBodyText.textWrappingMode = TextWrappingModes.Normal;
            panel.SummaryBodyText.overflowMode = TextOverflowModes.Overflow;

            panel.PanelEconomy = MakePanel(content.transform, "PanelEconomy", reportPanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelEconomy.SetActive(false);

            panel.PanelBuild = MakePanel(content.transform, "PanelBuild", reportPanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelBuild.SetActive(false);

            panel.PanelThreat = MakePanel(content.transform, "PanelThreat", reportPanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelThreat.SetActive(false);

            panel.PanelStory = MakePanel(content.transform, "PanelStory", reportPanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.PanelStory.SetActive(false);

            panel.PanelWaves = MakePanel(content.transform, "PanelWaves", reportPanelColor, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var scrollGo = new GameObject("ScrollRect");
            scrollGo.transform.SetParent(panel.PanelWaves.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            var scroll = scrollGo.AddComponent<ScrollRect>();

            var viewport = MakePanel(scrollGo.transform, "Viewport", new Color(0f, 0f, 0f, 0.01f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            viewport.AddComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("WaveContent");
            contentGo.transform.SetParent(viewport.transform, false);
            var wavesRt = contentGo.AddComponent<RectTransform>();
            wavesRt.anchorMin = new Vector2(0f, 1f);
            wavesRt.anchorMax = new Vector2(1f, 1f);
            wavesRt.pivot = new Vector2(0.5f, 1f);
            wavesRt.offsetMin = new Vector2(0f, 0f);
            wavesRt.offsetMax = new Vector2(0f, 0f);
            var wavesLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            wavesLayout.padding = new RectOffset(0, 0, 12, 12);
            wavesLayout.spacing = 0;
            wavesLayout.childControlHeight = true;
            wavesLayout.childControlWidth = true;
            wavesLayout.childForceExpandHeight = false;
            wavesLayout.childForceExpandWidth = true;
            var wavesBody = new GameObject("WavesBody");
            wavesBody.transform.SetParent(contentGo.transform, false);
            var wavesBodyRt = wavesBody.AddComponent<RectTransform>();
            wavesBodyRt.anchorMin = new Vector2(0f, 1f);
            wavesBodyRt.anchorMax = new Vector2(1f, 1f);
            wavesBodyRt.pivot = new Vector2(0.5f, 1f);
            wavesBodyRt.sizeDelta = new Vector2(0f, 0f);
            wavesBody.AddComponent<LayoutElement>().preferredHeight = 32f;
            var wavesBodyText = wavesBody.AddComponent<TextMeshProUGUI>();
            wavesBodyText.text = string.Empty;
            var wavesFont = panel.SummaryBodyText != null ? panel.SummaryBodyText.font : TMP_Settings.defaultFontAsset;
            if (wavesFont != null)
                wavesBodyText.font = wavesFont;
            if (panel.SummaryBodyText != null && panel.SummaryBodyText.fontSharedMaterial != null)
                wavesBodyText.fontSharedMaterial = panel.SummaryBodyText.fontSharedMaterial;
            wavesBodyText.fontSize = 13;
            wavesBodyText.color = Color.white;
            wavesBodyText.alignment = TextAlignmentOptions.TopLeft;
            wavesBodyText.margin = new Vector4(12f, 12f, 12f, 12f);
            wavesBodyText.textWrappingMode = TextWrappingModes.Normal;
            wavesBodyText.overflowMode = TextOverflowModes.Overflow;
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            wavesBody.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport.GetComponent<RectTransform>();
            scroll.content = wavesRt;
            scroll.horizontal = false;
            scroll.vertical = true;

            panel.WaveRowContainer = wavesRt;
            panel.WaveRowPrefab = BuildWaveRowPrefab();
            panel.WavesBodyText = wavesBodyText;
            panel.PanelWaves.SetActive(false);

            return panel;
        }

        GameObject BuildWaveRowPrefab()
        {
            var go = new GameObject("WaveRowRuntime");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 48f);
            go.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.18f, 0.88f);
            go.AddComponent<LayoutElement>().preferredHeight = 48f;

            var text = new GameObject("Label");
            text.transform.SetParent(go.transform, false);
            var textRt = text.AddComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(10f, 0f);
            textRt.offsetMax = new Vector2(-10f, 0f);
            var tmp = text.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = 13;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            return go;
        }

        LineGraphUI BuildGraph(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.10f);
            rt.anchorMax = new Vector2(0.95f, 0.90f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            if (go.GetComponent<CanvasRenderer>() == null)
                go.AddComponent<CanvasRenderer>();
            return go.AddComponent<LineGraphUI>();
        }

#if UNITY_EDITOR
        public void DebugShowReport(MLGameOverPayload payload, bool openReportPanel = true)
        {
            _payload = payload;
            ShowPayload(payload);
            Debug.Log(
                $"[PostGameReport] DebugShowReport applied. " +
                $"finalStats={(payload?.finalStats?.Length ?? 0)} " +
                $"waveSnapshots={(payload?.waveSnapshots?.Length ?? 0)} " +
                $"readableLog={(payload?.balanceReadableLog?.Length ?? 0)} " +
                $"diagnosis={(payload?.balanceDiagnosisLines?.Length ?? 0)}.");

            if (openReportPanel && _statsPanel != null && payload != null)
            {
                if (!_statsPanel.gameObject.activeSelf)
                    _statsPanel.gameObject.SetActive(true);
                _statsPanel.Show(payload);
            }
        }

        public static MLGameOverPayload BuildEditorValidationPayload()
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
                    BuildEditorValidationWave(9, 540, 41, 1740, 0, 0, 80, 2, 140, "Held", 18, 1, 36, 1400, 1, 2, 60, 2, 110, "Leaked", 11),
                    BuildEditorValidationWave(10, 625, 42, 1825, 1, 3, 95, 3, 120, "Leaked", 17, 1, 38, 1490, 3, 6, 70, 2, 90, "Crushed", 9, true),
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
                playerBattleReport = BuildEditorValidationBattleReport(),
            };
        }

        static MLWaveSnapshot BuildEditorValidationWave(
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

        static PlayerBattleReportPayload BuildEditorValidationBattleReport()
        {
            return new PlayerBattleReportPayload
            {
                schemaVersion = 1,
                tabs = new[] { "Summary", "Economy", "Army", "Threat", "Story", "Advanced" },
                lanes = new[]
                {
                    new CommanderBattleReport
                    {
                        laneIndex = 0,
                        displayName = "Blue Commander",
                        result = "victory",
                        resultLabel = "Victory",
                        opponentLaneIndex = 1,
                        opponentName = "Red Commander",
                        commanderNotes = new[]
                        {
                            "You stabilized at Wave 6.",
                            "You moved ahead of the incoming horde at Wave 8.",
                            "Red Commander carried the larger war chest late, but you kept the stronger army curve."
                        },
                        matchHeader = new MatchResultHeaderReport
                        {
                            resultLabel = "Victory",
                            finalWave = 10,
                            durationSeconds = 625,
                            modeLabel = "Survival",
                            difficultyLabel = "Dungeon Pressure",
                        },
                        snapshot = new CorePerformanceSnapshotReport
                        {
                            enemiesDefeated = 84,
                            unitsRecruited = 16,
                            unitsLost = 6,
                            buildingsConstructed = 7,
                            upgradesPurchased = 9,
                            goldEarned = 2155,
                            goldSpent = 2040,
                            breachesSuffered = 1,
                            coreHealthRemaining = 17,
                            coreDamageTaken = 3,
                        },
                        economyCurve = BuildCurve(
                            "War Chest",
                            "Banked gold, income, and match spending by wave.",
                            new[] { "W9", "W10" },
                            ("Your War Chest", new[] { 88f, 115f }, "player"),
                            ("Red Commander War Chest", new[] { 102f, 132f }, "opponent"),
                            ("Your Gold Flow", new[] { 41f, 42f }, "support"),
                            ("Red Commander Gold Flow", new[] { 36f, 38f }, "opponent_support"),
                            ("Your Spending", new[] { 220f, 215f }, "spend"),
                            ("Red Commander Spending", new[] { 170f, 160f }, "opponent_spend")),
                        armyCurve = BuildCurve(
                            "Warband Value",
                            "Army strength growth through the match.",
                            new[] { "W9", "W10" },
                            ("Your Army Strength", new[] { 720f, 790f }, "player"),
                            ("Red Commander Army Strength", new[] { 640f, 610f }, "opponent")),
                        threatCurve = BuildCurve(
                            "War Strength vs Incoming Horde",
                            "Your fielded strength against dungeon pressure.",
                            new[] { "W9", "W10" },
                            ("Your Battle Strength", new[] { 720f, 790f }, "player"),
                            ("Red Commander Battle Strength", new[] { 640f, 610f }, "opponent"),
                            ("Dungeon Threat", new[] { 690f, 840f }, "threat")),
                        strategyComparison = new StrategyComparisonReport
                        {
                            playerStyleLabel = "Balanced",
                            opponentStyleLabel = "Economy-Heavy",
                            summary = "You played a balanced plan while Red Commander leaned economy-heavy.",
                            player = BuildSpend("Blue Commander", "Balanced", 620f, 410f, 285f, 510f, 55f, new[] { ("Barracks", 42f), ("Archery", 28f), ("Market", 18f) }, new[] { ("Infantry", 34f), ("Archers", 29f), ("Shield", 18f) }),
                            opponent = BuildSpend("Red Commander", "Economy-Heavy", 540f, 260f, 420f, 350f, 40f, new[] { ("Market", 44f), ("Barracks", 25f), ("Walls", 17f) }, null),
                        },
                        fortressPressure = new FortressPressureReport
                        {
                            breachCount = 1,
                            wallDamageTaken = 12,
                            towerDamageTaken = 4,
                            coreDamageTaken = 3,
                            timeUnderBreachPressure = 18.5f,
                            fortressEntries = 2,
                            timeWithoutFrontline = 6.2f,
                            pressureVerdict = "Under Pressure",
                            summary = "The fortress bent once, then recovered before the line fully collapsed."
                        },
                        armySummary = new ArmySummaryReport
                        {
                            mostRecruitedUnitType = "Militia",
                            bestPerformingUnitType = "Archer",
                            armyValueFielded = 1825f,
                            unitComposition = new[]
                            {
                                new StrategyShareReport { label = "Infantry", value = 34f, sharePercent = 34f },
                                new StrategyShareReport { label = "Archers", value = 29f, sharePercent = 29f },
                                new StrategyShareReport { label = "Shield", value = 18f, sharePercent = 18f },
                            },
                            bestSupportType = "Priest",
                            heroContribution = "Paladin joined the field and helped lock down the final breach.",
                            summary = "Archer upgrades and a sturdy infantry shell carried the run."
                        },
                        battleStory = new BattleStoryReport
                        {
                            highlights = new[]
                            {
                                new BattleStoryHighlightReport { title = "Hardest Wave", detail = "Wave 10 brought the heaviest pressure.", wave = 10 },
                                new BattleStoryHighlightReport { title = "First Breach", detail = "Your fortress first cracked at Wave 10.", wave = 10 },
                                new BattleStoryHighlightReport { title = "Stabilized", detail = "You steadied the line at Wave 6.", wave = 6 },
                                new BattleStoryHighlightReport { title = "Surpassed the Horde", detail = "Your warband first cleared the dungeon curve at Wave 8.", wave = 8 },
                                new BattleStoryHighlightReport { title = "Strongest Spike Source", detail = "Archery upgrades drove your biggest surge.", wave = 8 },
                            },
                            storyLines = new[]
                            {
                                "Wave 10 was the hardest point of the match.",
                                "You stabilized at Wave 6.",
                                "You first outpaced dungeon pressure at Wave 8.",
                                "Your strongest spike came from Archery upgrades.",
                            },
                            recommendations = new[]
                            {
                                "Spend a little earlier once your war chest rises above 100 gold.",
                                "A faster repair after the first breach would keep the finish cleaner.",
                            }
                        },
                        awards = new[]
                        {
                            new BattleHonorReport { title = "War Machine", detail = "You kept your economy, army, and fortress growing together." },
                            new BattleHonorReport { title = "Arrow Storm", detail = "Ranged fire defined the winning curve." },
                            new BattleHonorReport { title = "Frugal Commander", detail = "You kept your gold moving instead of floating it for too long." },
                        },
                        advanced = new AdvancedBattleStatsReport
                        {
                            breakdownLines = new[]
                            {
                                "You played a balanced plan while Red Commander leaned economy-heavy.",
                                "Spending buckets: Units 620 | Upgrades 410 | Economy 285 | Defense 510 | Repairs 55",
                                "Archer upgrades and a sturdy infantry shell carried the run.",
                                "The fortress bent once, then recovered before the line fully collapsed.",
                            },
                            waveRows = new[]
                            {
                                new AdvancedWaveRowReport { wave = 9, state = "Stable", bankedGold = 88f, armyStrength = 720f, dungeonThreat = 690f, breachCount = 0, coreDamage = 0, opponentArmyStrength = 640f },
                                new AdvancedWaveRowReport { wave = 10, state = "Holding the Line", bankedGold = 115f, armyStrength = 790f, dungeonThreat = 840f, breachCount = 1, coreDamage = 3, opponentArmyStrength = 610f },
                            }
                        }
                    }
                }
            };
        }

        static CurveReport BuildCurve(string title, string subtitle, string[] labels, params (string label, float[] values, string tone)[] lines)
        {
            var result = new CurveLineReport[lines.Length];
            for (int index = 0; index < lines.Length; index++)
            {
                result[index] = new CurveLineReport
                {
                    label = lines[index].label,
                    values = lines[index].values,
                    tone = lines[index].tone,
                    isPrimary = index == 0,
                };
            }

            return new CurveReport
            {
                title = title,
                subtitle = subtitle,
                valueFormat = "F0",
                xLabels = labels,
                lines = result,
            };
        }

        static StrategySpendBreakdownReport BuildSpend(string commanderName, string styleLabel, float units, float upgrades, float economy, float defense, float repairs, (string label, float share)[] paths, (string label, float share)[] composition)
        {
            var report = new StrategySpendBreakdownReport
            {
                commanderName = commanderName,
                styleLabel = styleLabel,
                unitSpending = units,
                upgradeSpending = upgrades,
                economySpending = economy,
                defenseSpending = defense,
                repairs = repairs,
                otherSpending = 0f,
                highlights = new[] { $"{paths[0].label} was the defining path." },
            };

            report.buildingPaths = new StrategyShareReport[paths.Length];
            for (int index = 0; index < paths.Length; index++)
            {
                report.buildingPaths[index] = new StrategyShareReport
                {
                    label = paths[index].label,
                    value = paths[index].share,
                    sharePercent = paths[index].share,
                };
            }

            if (composition != null)
            {
                report.unitComposition = new StrategyShareReport[composition.Length];
                for (int index = 0; index < composition.Length; index++)
                {
                    report.unitComposition[index] = new StrategyShareReport
                    {
                        label = composition[index].label,
                        value = composition[index].share,
                        sharePercent = composition[index].share,
                    };
                }
            }

            return report;
        }
#endif

        GameObject MakePanel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            go.AddComponent<Image>().color = color;
            return go;
        }

        TMP_Text MakeText(Transform parent, string name, string text, float fontSize, Vector2 anchorMin, Vector2 anchorMax, FontStyles style, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.color = Color.white;
            tmp.alignment = alignment;
            return tmp;
        }

        Button MakeButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var go = MakePanel(parent, name, color, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            var button = go.AddComponent<Button>();
            var text = MakeText(go.transform, "Label", label, 18, Vector2.zero, Vector2.one, FontStyles.Bold, TextAlignmentOptions.Center);
            text.color = Color.white;
            return button;
        }

        Button MakeLayoutButton(Transform parent, string name, string label)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<LayoutElement>().preferredHeight = 38f;
            go.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.32f, 1f);
            var button = go.AddComponent<Button>();
            MakeText(go.transform, "Label", label, 14, Vector2.zero, Vector2.one, FontStyles.Bold, TextAlignmentOptions.Center);
            return button;
        }
    }
}
