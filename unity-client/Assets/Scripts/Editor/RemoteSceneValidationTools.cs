#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CastleDefender.Net;
using CastleDefender.UI;
using CastleDefender.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    public static class RemoteSceneValidationTools
    {
        const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        const string CombatValidationScreenshotPath = "projects/combat-validation.png";
        const string LiveCadenceScreenshotPath = "projects/live-match-cadence.png";
        static int s_framesUntilLog = -1;
        static int s_framesUntilScreenshot = -1;
        static bool s_liveSoloValidationRunning;
        static bool s_liveContinueValidationRunning;
        static bool s_captureLiveGameplayScreenshot;

        static RemoteSceneValidationTools()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Start From Bootstrap")]
        public static void StartFromBootstrap()
        {
            EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Log Runtime Scene State")]
        public static void LogRuntimeSceneState()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            var loadedScenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    loadedScenes.Add(scene.name);
            }

            var loadingScreen = LoadingScreen.Instance;
            string trackedHandles = "<no loading screen>";
            bool transitionInProgress = false;
            string loadingLabel = "<no loading screen>";
            string tipLabel = "<no loading screen>";
            bool retryVisible = false;
            bool addressablesReady = false;
            if (loadingScreen != null)
            {
                const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
                const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.NonPublic;
                if (typeof(LoadingScreen).GetField("_loadedSceneHandles", InstanceFlags)?.GetValue(loadingScreen) is System.Collections.IDictionary handles)
                {
                    var names = new List<string>();
                    foreach (object key in handles.Keys)
                    {
                        if (key is string name && !string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }

                    names.Sort(System.StringComparer.OrdinalIgnoreCase);
                    trackedHandles = names.Count == 0 ? "<none>" : string.Join(", ", names);
                }

                transitionInProgress = (bool)(typeof(LoadingScreen).GetField("_transitionInProgress", StaticFlags)?.GetValue(null) ?? false);
                loadingLabel = string.IsNullOrWhiteSpace(loadingScreen.loadingLabel?.text) ? "<empty>" : loadingScreen.loadingLabel.text;
                tipLabel = string.IsNullOrWhiteSpace(loadingScreen.tipText?.text) ? "<empty>" : loadingScreen.tipText.text;
                retryVisible = (bool)(typeof(LoadingScreen).GetField("_retryButton", InstanceFlags)?.GetValue(loadingScreen) is UnityEngine.UI.Button button
                    && button.gameObject.activeInHierarchy);
            }

            if (RemoteContentManager.Instance != null)
            {
                addressablesReady = RemoteContentManager.Instance.AreAddressablesInitialized;
            }

            Debug.Log(
                $"[RemoteSceneValidation] Active='{SceneManager.GetActiveScene().name}', " +
                $"Loaded=[{string.Join(", ", loadedScenes)}], " +
                $"TrackedHandles=[{trackedHandles}], TransitionInProgress={transitionInProgress}, " +
                $"AddressablesReady={addressablesReady}, LoadingLabel='{loadingLabel}', RetryVisible={retryVisible}, Tip='{tipLabel}'");
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/Login")]
        public static void TransitionToLogin() => RunTransition(() => LoadingScreen.LoadScene("Login"));

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/Lobby")]
        public static void TransitionToLobby() => RunTransition(() => LoadingScreen.LoadScene("Lobby"));

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/Lobby With T0 Gate")]
        public static void TransitionToLobbyWithGate() => RunTransition(() => LoadingScreen.LoadSceneWithCriticalContentPreload("Lobby"));

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/Loadout")]
        public static void TransitionToLoadout() => RunTransition(() => LoadingScreen.LoadScene("Loadout"));

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/Game_ML")]
        public static void TransitionToGameMl() =>
            RunTransition(() => LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML", preloadEnvironment: true));

        [MenuItem("Castle Defender/Remote Scene Validation/Start Live Solo Match Validation")]
        public static void StartLiveSoloMatchValidation()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            var nm = NetworkManager.Instance;
            if (nm == null || !nm.IsConnected)
            {
                Debug.LogWarning("[RemoteSceneValidation] NetworkManager is not connected.");
                return;
            }

            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] RemoteContentManager is not available.");
                return;
            }

            if (s_liveSoloValidationRunning)
            {
                Debug.LogWarning("[RemoteSceneValidation] Live solo validation is already running.");
                return;
            }

            remoteContent.StartCoroutine(RunLiveSoloMatchValidation(nm, remoteContent));
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Start Live Continue-After-Win Validation")]
        public static void StartLiveContinueAfterWinValidation()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            var nm = NetworkManager.Instance;
            if (nm == null || !nm.IsConnected)
            {
                Debug.LogWarning("[RemoteSceneValidation] NetworkManager is not connected.");
                return;
            }

            var remoteContent = RemoteContentManager.EnsureInstance();
            if (remoteContent == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] RemoteContentManager is not available.");
                return;
            }

            if (s_liveSoloValidationRunning || s_liveContinueValidationRunning)
            {
                Debug.LogWarning("[RemoteSceneValidation] A live validation is already running.");
                return;
            }

            remoteContent.StartCoroutine(RunLiveContinueAfterWinValidation(nm, remoteContent));
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Capture Live Gameplay Screenshot")]
        public static void CaptureLiveGameplayScreenshot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            ScreenCapture.CaptureScreenshot(LiveCadenceScreenshotPath);
            Debug.Log($"[RemoteSceneValidation] Captured live gameplay screenshot to '{LiveCadenceScreenshotPath}'.");
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Inject Mock ML Snapshot")]
        public static void InjectMockMlSnapshot()
            => InjectSnapshot(BuildMockMlSnapshot(), "board validation");

        [MenuItem("Castle Defender/Remote Scene Validation/Inject Combat Contact Snapshot")]
        public static void InjectCombatContactSnapshot()
            => InjectSnapshot(BuildCombatContactSnapshot(), "combat contact validation");

        [MenuItem("Castle Defender/Remote Scene Validation/Capture Combat Validation Screenshot")]
        public static void CaptureCombatValidationScreenshot()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            InjectSnapshot(BuildCombatContactSnapshot(), "combat contact validation");
            FrameCombatValidationCamera();
            s_framesUntilScreenshot = 150;
        }

        static void InjectSnapshot(MLSnapshot snapshot, string label)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            if (SceneManager.GetActiveScene().name != "Game_ML")
            {
                Debug.LogWarning("[RemoteSceneValidation] Load Game_ML before injecting a mock snapshot.");
                return;
            }

            var snapshotApplier = SnapshotApplier.Instance ?? Object.FindFirstObjectByType<SnapshotApplier>();
            if (snapshotApplier == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] No SnapshotApplier instance is active.");
                return;
            }

            snapshotApplier.DebugApplyMLSnapshot(snapshot);
            LogSpawnedMockUnits();
            Debug.Log($"[RemoteSceneValidation] Injected mock ML snapshot for {label}.");
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/PostGame")]
        public static void TransitionToPostGame() => RunTransition(() => LoadingScreen.LoadScene("PostGame"));

        [MenuItem("Castle Defender/Remote Scene Validation/Validate TT Loadout Skins")]
        public static void ValidateTtLoadoutSkins()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] No RemoteContentManager instance is active.");
                return;
            }

            var loadout = Object.FindFirstObjectByType<LoadoutPhaseManager>();
            var resolvePortraitLookupKey = typeof(LoadoutPhaseManager).GetMethod(
                "ResolvePortraitLookupKey",
                BindingFlags.Instance | BindingFlags.NonPublic);
            remoteContent.StartCoroutine(ValidateTtLoadoutSkinsCoroutine(remoteContent, loadout, resolvePortraitLookupKey));
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Validate TT Portrait Requests")]
        public static void ValidateTtPortraitRequests()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            var remoteContent = RemoteContentManager.Instance;
            if (remoteContent == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] No RemoteContentManager instance is active.");
                return;
            }

            remoteContent.StartCoroutine(ValidateTtPortraitRequestsCoroutine(remoteContent));
        }

        [MenuItem("Castle Defender/Remote Scene Validation/Retry Failed Transition")]
        public static void RetryFailedTransition()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            var loadingScreen = LoadingScreen.Instance;
            if (loadingScreen == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] No LoadingScreen instance is active.");
                return;
            }

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (typeof(LoadingScreen).GetField("_retryButton", Flags)?.GetValue(loadingScreen) is not UnityEngine.UI.Button button
                || button == null
                || !button.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[RemoteSceneValidation] Retry button is not currently visible.");
                return;
            }

            button.onClick.Invoke();
            s_framesUntilLog = 150;
        }

        static void RunTransition(System.Action transition)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[RemoteSceneValidation] Enter Play Mode first.");
                return;
            }

            transition?.Invoke();
            s_framesUntilLog = 150;
        }

        static void OnEditorUpdate()
        {
            if (!Application.isPlaying)
                return;

            if (s_framesUntilLog < 0 && s_framesUntilScreenshot < 0)
                return;

            if (s_framesUntilLog >= 0)
            {
                s_framesUntilLog--;
                if (s_framesUntilLog == 0)
                {
                    s_framesUntilLog = -1;
                    LogRuntimeSceneState();
                }
            }

            if (s_framesUntilScreenshot >= 0)
            {
                s_framesUntilScreenshot--;
                if (s_framesUntilScreenshot == 0)
                {
                    s_framesUntilScreenshot = -1;
                    if (s_captureLiveGameplayScreenshot)
                    {
                        s_captureLiveGameplayScreenshot = false;
                        ScreenCapture.CaptureScreenshot(LiveCadenceScreenshotPath);
                        Debug.Log($"[RemoteSceneValidation] Captured live gameplay screenshot to '{LiveCadenceScreenshotPath}'.");
                    }
                    else
                    {
                        RenderCombatValidationScreenshot();
                        Debug.Log($"[RemoteSceneValidation] Captured combat validation screenshot to '{CombatValidationScreenshotPath}'.");
                    }
                }
            }
        }

        static System.Collections.IEnumerator RunLiveSoloMatchValidation(NetworkManager nm, RemoteContentManager remoteContent)
        {
            s_liveSoloValidationRunning = true;
            string initialRoomCode = nm.MyRoomCode;
            var initialPendingLoadoutPhase = nm.PendingLoadoutPhase;
            var initialMatchLoadout = nm.LastMatchLoadout;

            nm.Emit("queue:enter", new
            {
                mode = "solo_td",
                displayName = "CadenceValidation",
                filters = new
                {
                    botConfigs = new[]
                    {
                        new { difficulty = "medium" }
                    }
                }
            });

            yield return WaitForCondition(
                () => !string.IsNullOrWhiteSpace(nm.MyRoomCode) && nm.MyRoomCode != initialRoomCode,
                15f,
                "Timed out waiting for live solo match creation.");
            if (!Application.isPlaying)
                yield break;

            if (!remoteContent.HasCompletedLoadoutPreload)
            {
                Debug.Log("[RemoteSceneValidation] Preloading loadout content before emitting ml_loadout_ready.");
                yield return remoteContent.PreloadLoadoutContentForSession(requester: "RemoteSceneValidation.LiveSolo");
            }

            Debug.Log("[RemoteSceneValidation] Emitting ml_loadout_ready for live solo validation.");
            nm.RequestLoadoutReady();

            yield return WaitForCondition(
                () => nm.PendingLoadoutPhase != null && nm.PendingLoadoutPhase != initialPendingLoadoutPhase,
                20f,
                "Timed out waiting for ml_loadout_phase_start.");
            if (!Application.isPlaying)
                yield break;

            var phase = nm.PendingLoadoutPhase;
            if (phase == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] Live solo validation received an invalid progression phase payload.");
                s_liveSoloValidationRunning = false;
                yield break;
            }

            string selectedRaceId = ResolveValidationRaceId(phase);
            Debug.Log($"[RemoteSceneValidation] Auto-confirming live validation race='{selectedRaceId}'.");
            nm.EmitLoadoutConfirm(selectedRaceId);

            yield return WaitForCondition(
                () => nm.LastMatchLoadout != null && nm.LastMatchLoadout != initialMatchLoadout,
                25f,
                "Timed out waiting for resolved live-match loadout.");
            if (!Application.isPlaying)
                yield break;

            bool needT1 = !remoteContent.HasCompletedWavePreload;
            bool needEnvironment = !remoteContent.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress);
            Debug.Log("[RemoteSceneValidation] Loading Game_ML for live cadence validation.");
            LoadingScreen.LoadSceneWithRemoteContentGate(
                "Game_ML",
                preloadT1Gameplay: needT1,
                preloadEnvironment: needEnvironment);

            yield return WaitForCondition(
                () => SceneManager.GetActiveScene().name == "Game_ML" && SnapshotApplier.Instance != null && SnapshotApplier.Instance.LatestML != null,
                40f,
                "Timed out waiting for live gameplay snapshots in Game_ML.");
            if (!Application.isPlaying)
                yield break;

            Debug.Log("[RemoteSceneValidation] Live gameplay snapshots are flowing; scheduling cadence capture.");
            s_framesUntilLog = 300;
            s_framesUntilScreenshot = 480;
            s_captureLiveGameplayScreenshot = true;
            s_liveSoloValidationRunning = false;
        }

        static System.Collections.IEnumerator RunLiveContinueAfterWinValidation(NetworkManager nm, RemoteContentManager remoteContent)
        {
            s_liveContinueValidationRunning = true;
            void HandlePvpResolved(MLPvPResolvedPayload payload)
            {
                string winners = payload?.winnerLaneIndices == null ? "<none>" : string.Join(",", payload.winnerLaneIndices);
                Debug.Log($"[RemoteSceneValidation] Continue validation observed ml_pvp_resolved winners=[{winners}] myLane={nm.MyLaneIndex}.");
            }

            nm.OnMLPvPResolved += HandlePvpResolved;

            try
            {
                string initialRoomCode = nm.MyRoomCode;
                var initialPendingLoadoutPhase = nm.PendingLoadoutPhase;
                var initialMatchLoadout = nm.LastMatchLoadout;

                nm.Emit("queue:enter", new
                {
                    mode = "solo_td",
                    displayName = "ContinueValidation",
                    filters = new
                    {
                        botConfigs = new[]
                        {
                            new { difficulty = "easy" }
                        }
                    }
                });

                bool matchCreated = false;
                yield return WaitForCondition(
                    () => matchCreated = !string.IsNullOrWhiteSpace(nm.MyRoomCode) && nm.MyRoomCode != initialRoomCode,
                    15f,
                    "Timed out waiting for continue-validation match creation.");
                if (!Application.isPlaying || !matchCreated)
                    yield break;

                if (!remoteContent.HasCompletedLoadoutPreload)
                {
                    Debug.Log("[RemoteSceneValidation] Preloading loadout content before emitting ml_loadout_ready.");
                    yield return remoteContent.PreloadLoadoutContentForSession(requester: "RemoteSceneValidation.LiveContinue");
                }

                Debug.Log("[RemoteSceneValidation] Emitting ml_loadout_ready for continue-after-win validation.");
                nm.RequestLoadoutReady();

                bool phaseStarted = false;
                yield return WaitForCondition(
                    () => phaseStarted = nm.PendingLoadoutPhase != null && nm.PendingLoadoutPhase != initialPendingLoadoutPhase,
                    20f,
                    "Timed out waiting for continue-validation ml_loadout_phase_start.");
                if (!Application.isPlaying || !phaseStarted)
                    yield break;

                var phase = nm.PendingLoadoutPhase;
                if (phase == null)
                {
                    Debug.LogWarning("[RemoteSceneValidation] Continue validation received an invalid progression phase payload.");
                    yield break;
                }

                string selectedRaceId = ResolveValidationRaceId(phase);
                Debug.Log($"[RemoteSceneValidation] Auto-confirming continue validation race='{selectedRaceId}'.");
                nm.EmitLoadoutConfirm(selectedRaceId);

                bool loadoutResolved = false;
                yield return WaitForCondition(
                    () => loadoutResolved = nm.LastMatchLoadout != null && nm.LastMatchLoadout != initialMatchLoadout,
                    25f,
                    "Timed out waiting for resolved continue-validation loadout.");
                if (!Application.isPlaying || !loadoutResolved)
                    yield break;

                bool needT1 = !remoteContent.HasCompletedWavePreload;
                bool needEnvironment = !remoteContent.AreEnvironmentAssetsReady(RemoteContentManager.GameMlEnvironmentAddress);
                Debug.Log("[RemoteSceneValidation] Loading Game_ML for continue-after-win validation.");
                LoadingScreen.LoadSceneWithRemoteContentGate(
                    "Game_ML",
                    preloadT1Gameplay: needT1,
                    preloadEnvironment: needEnvironment);

                bool gameplayReady = false;
                yield return WaitForCondition(
                    () => gameplayReady = SceneManager.GetActiveScene().name == "Game_ML" && SnapshotApplier.Instance != null && SnapshotApplier.Instance.LatestML != null,
                    40f,
                    "Timed out waiting for continue-validation gameplay snapshots in Game_ML.");
                if (!Application.isPlaying || !gameplayReady)
                    yield break;

                bool pvpResolved = false;
                yield return WaitForCondition(
                    () => pvpResolved = nm.CurrentMLMatchState == "pvp_resolved" && nm.LastMLPvPResolved != null,
                    180f,
                    "Timed out waiting for PvP resolution before continue validation.");
                if (!Application.isPlaying || !pvpResolved)
                    yield break;

                int myLane = nm.MyLaneIndex;
                var resolvedPayload = nm.LastMLPvPResolved;
                bool amWinner = resolvedPayload != null
                    && resolvedPayload.winnerLaneIndices != null
                    && System.Array.IndexOf(resolvedPayload.winnerLaneIndices, myLane) >= 0;
                if (!amWinner)
                {
                    Debug.LogWarning("[RemoteSceneValidation] Continue validation reached PvP resolution, but the local player was not a winner.");
                    yield break;
                }

                int resolvedRound = SnapshotApplier.Instance?.LatestML?.roundNumber ?? 0;
                Debug.Log($"[RemoteSceneValidation] Continue validation sending ml_continue_after_win at round={resolvedRound}.");
                ActionSender.ContinueAfterWin();

                bool survivalStarted = false;
                yield return WaitForCondition(
                    () => survivalStarted = nm.CurrentMLMatchState == "survival_continuation",
                    30f,
                    "Timed out waiting for survival continuation to start.");
                if (!Application.isPlaying || !survivalStarted)
                    yield break;

                int expectedRound = Mathf.Max(2, resolvedRound + 1);
                bool survivalRoundReached = false;
                yield return WaitForCondition(
                    () =>
                    {
                        var snapshot = SnapshotApplier.Instance?.LatestML;
                        return survivalRoundReached = snapshot != null
                            && snapshot.matchState == "survival_continuation"
                            && snapshot.roundNumber >= expectedRound;
                    },
                    90f,
                    $"Timed out waiting for survival wave {expectedRound} after continue.");
                if (!Application.isPlaying || !survivalRoundReached)
                    yield break;

                var survivalSnapshot = SnapshotApplier.Instance?.LatestML;
                Debug.Log(
                    $"[RemoteSceneValidation] Continue-after-win validation reached round={survivalSnapshot?.roundNumber} " +
                    $"state='{survivalSnapshot?.matchState}'.");
            }
            finally
            {
                nm.OnMLPvPResolved -= HandlePvpResolved;
                s_liveContinueValidationRunning = false;
            }
        }

        static System.Collections.IEnumerator WaitForCondition(System.Func<bool> condition, float timeoutSeconds, string timeoutMessage)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (Application.isPlaying && Time.realtimeSinceStartup < deadline)
            {
                if (condition())
                    yield break;
                yield return null;
            }

            if (Application.isPlaying)
                Debug.LogWarning($"[RemoteSceneValidation] {timeoutMessage}");

            s_liveSoloValidationRunning = false;
            s_liveContinueValidationRunning = false;
        }

        static string ResolveValidationRaceId(MLLoadoutPhaseStartPayload phase)
        {
            return RaceProgressionCatalog.ResolveAllowedRaceId(
                phase?.availableRaceIds,
                phase?.selectedRaceId ?? phase?.defaultRaceId,
                "RemoteSceneValidation");
        }

        static System.Collections.IEnumerator ValidateTtLoadoutSkinsCoroutine(
            RemoteContentManager remoteContent,
            LoadoutPhaseManager loadout,
            MethodInfo resolvePortraitLookupKey)
        {
            string[] skinKeys =
            {
                "tt_peasant",
                "tt_scout",
                "tt_settler",
                "tt_mounted_priest",
            };

            var portraitKeys = new List<string>();
            foreach (string skinKey in skinKeys)
            {
                string portraitLookupKey = skinKey;
                if (loadout != null && resolvePortraitLookupKey != null)
                    portraitLookupKey = resolvePortraitLookupKey.Invoke(loadout, new object[] { skinKey }) as string ?? skinKey;

                if (!string.IsNullOrWhiteSpace(portraitLookupKey) && !portraitKeys.Contains(portraitLookupKey))
                    portraitKeys.Add(portraitLookupKey);
            }

            if (portraitKeys.Count > 0)
                yield return remoteContent.EnsurePortraitsReady(portraitKeys, requester: "RemoteSceneValidation.TTLoadoutSkins");

            var registry = RuntimePortraitStudio.ResolveRegistry();
            UnitPortraitCamera portraitCam = null;
            GameObject portraitRoot = null;
            RenderTexture portraitTexture = null;
            if (registry != null)
                portraitCam = RuntimePortraitStudio.Create("RemoteSceneValidationSkinStudio", registry, out portraitRoot, out portraitTexture);

            foreach (string skinKey in skinKeys)
            {
                bool prefabReady = remoteContent.TryGetLoadedPrefabForSkin(skinKey, out var prefab) && prefab != null;
                string portraitLookupKey = skinKey;
                if (loadout != null && resolvePortraitLookupKey != null)
                    portraitLookupKey = resolvePortraitLookupKey.Invoke(loadout, new object[] { skinKey }) as string ?? skinKey;

                bool portraitReady = remoteContent.TryGetLoadedPortraitTexture(portraitLookupKey, out var portrait) && portrait != null;

                Debug.Log(
                    $"[RemoteSceneValidation] TT skin '{skinKey}' prefabReady={prefabReady} prefab='{prefab?.name ?? "<null>"}' " +
                    $"portraitLookup='{portraitLookupKey}' portraitReady={portraitReady}");

                if (portraitCam != null)
                {
                    bool done = false;
                    portraitCam.StartIconCapture(skinKey, _ => done = true);
                    while (!done)
                        yield return null;
                }
            }

            if (portraitRoot != null)
                Object.Destroy(portraitRoot);
            if (portraitTexture != null)
            {
                portraitTexture.Release();
                Object.Destroy(portraitTexture);
            }
        }

        static System.Collections.IEnumerator ValidateTtPortraitRequestsCoroutine(RemoteContentManager remoteContent)
        {
            string[] requestedKeys =
            {
                "tt_peasant",
                "tt_scout",
                "tt_settler",
                "tt_mounted_priest",
            };

            yield return remoteContent.EnsurePortraitsReady(requestedKeys, requester: "RemoteSceneValidation.TTPortraitRequests");

            foreach (string key in requestedKeys)
            {
                bool portraitReady = remoteContent.TryGetLoadedPortraitTexture(key, out var portrait) && portrait != null;
                Debug.Log($"[RemoteSceneValidation] TT portrait request '{key}' portraitReady={portraitReady} size='{portrait?.width}x{portrait?.height}'");
            }
        }

        static MLSnapshot BuildMockMlSnapshot()
        {
            return new MLSnapshot
            {
                tick = 1,
                phase = "playing",
                matchState = "active_pvp",
                roundState = "combat",
                roundNumber = 1,
                incomeTicksRemaining = 30,
                roundStateTicks = 0,
                buildPhaseTotal = 30,
                transitionPhaseTotal = 5,
                teamHp = new MLTeamHp { left = 100, right = 100 },
                teamHpMax = 100,
                lanes = new[]
                {
                    BuildLane(0, "left",  "red",   "Red",   "left_branch_a"),
                    BuildLane(1, "left",  "gold",  "Gold",  "left_branch_b"),
                    BuildLane(2, "right", "blue",  "Blue",  "right_branch_a"),
                    BuildLane(3, "right", "green", "Green", "right_branch_b"),
                }
            };
        }

        static MLSnapshot BuildCombatContactSnapshot()
        {
            var snapshot = BuildMockMlSnapshot();
            snapshot.roundState = "combat";
            snapshot.lanes[0].path = BuildFullBranchPath();
            snapshot.lanes[0].units = new[]
            {
                BuildMockUnit("contact_defender", 0, 16.831f, 4.866f, 16.831f, 0f, true, false, true, "contact_wave_center", 3),
                BuildMockUnit("contact_wave_left", 0, 13.220f, 2.780f, 13.220f, 0f, true, true, false, "contact_defender", 1),
                BuildMockUnit("contact_wave_right", 0, 13.280f, 6.980f, 13.280f, 0f, true, true, false, "contact_defender", 2),
                BuildMockUnit("contact_wave_center", 0, 12.540f, 4.930f, 12.540f, 0f, true, true, false, "contact_defender", 3),
            };
            return snapshot;
        }

        static MLLaneSnap BuildLane(int laneIndex, string team, string slotColor, string slotKey, string branchId)
        {
            return new MLLaneSnap
            {
                laneIndex = laneIndex,
                team = team,
                side = team,
                slotKey = slotKey.ToLowerInvariant(),
                slotColor = slotColor,
                branchId = branchId,
                branchLabel = slotKey,
                castleSide = team,
                eliminated = false,
                gold = 10,
                income = 1,
                lives = 100,
                barracksLevel = 1,
                projectiles = System.Array.Empty<MLProjectile>(),
                units = BuildLaneUnits(laneIndex),
                path = BuildLanePath()
            };
        }

        static MLUnit[] BuildLaneUnits(int laneIndex)
        {
            if (laneIndex != 0)
                return System.Array.Empty<MLUnit>();

            return new[]
            {
                BuildMockUnit("mock_branch_left", laneIndex, 4f, 2f, 4f, 0f, false, true, false, null, 0),
                BuildMockUnit("mock_branch_right", laneIndex, 4f, 8f, 4f, 0f, false, true, false, null, 0),
                BuildMockUnit("mock_suffix_left", laneIndex, 38.660278f, 2f, 38.660278f, 0.39482507f, false, false, true, null, 0),
                BuildMockUnit("mock_suffix_right", laneIndex, 38.660278f, 8f, 38.660278f, 0.39482507f, true, false, true, null, 1),
            };
        }

        static MLUnit BuildMockUnit(
            string id,
            int ownerLane,
            float pathIdx,
            float gridX,
            float gridY,
            float normProgress,
            bool isAttacking,
            bool isWaveUnit,
            bool isDefender,
            string combatTargetId,
            int attackPulse)
        {
            return new MLUnit
            {
                id = id,
                ownerLane = ownerLane,
                type = "tt_peasant",
                skinKey = null,
                pathIdx = pathIdx,
                gridX = gridX,
                gridY = gridY,
                normProgress = normProgress,
                hp = 100f,
                maxHp = 100f,
                isWaveUnit = isWaveUnit,
                isDefender = isDefender,
                isAttacking = isAttacking,
                combatTargetId = combatTargetId,
                attackPulse = attackPulse,
                level = 1,
            };
        }

        static void LogSpawnedMockUnits()
        {
            var waveRuntime = Object.FindFirstObjectByType<WaveSnapshotRuntimeSpawner>();
            var presentationRoot = Object.FindFirstObjectByType<GameplayPresentationRoot>();
            var animatorRoot = waveRuntime != null ? waveRuntime.transform : presentationRoot != null ? presentationRoot.transform : null;
            if (animatorRoot == null)
            {
                Debug.LogWarning("[RemoteSceneValidation] Gameplay presentation root not found after mock snapshot injection.");
                return;
            }

            var animators = animatorRoot.GetComponentsInChildren<Animator>(includeInactive: true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || animator.gameObject == null)
                    continue;

                string name = animator.gameObject.name ?? "<unnamed>";
                if (!name.Contains("tt_peasant"))
                    continue;

                var state = animator.GetCurrentAnimatorStateInfo(0);
                var clips = animator.GetCurrentAnimatorClipInfo(0);
                string clipName = clips != null && clips.Length > 0 && clips[0].clip != null
                    ? clips[0].clip.name
                    : "<none>";
                string stateName =
                    state.IsName("Idle") ? "Idle" :
                    state.IsName("Walk") ? "Walk" :
                    state.IsName("Run") ? "Run" :
                    state.IsName("Attack1") ? "Attack1" :
                    state.IsName("Attack2") ? "Attack2" :
                    state.IsName("Damage") ? "Damage" :
                    state.IsName("Death") ? "Death" :
                    $"hash:{state.shortNameHash}";

                Debug.Log(
                    $"[RemoteSceneValidation] Mock unit '{name}' controller='{animator.runtimeAnimatorController?.name ?? "<null>"}' " +
                    $"state='{stateName}' clip='{clipName}' normalizedTime={state.normalizedTime:0.00} " +
                    $"worldPos={animator.transform.position} params={animator.parameterCount}");
            }
        }

        static void FrameCombatValidationCamera()
        {
            Camera cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (cam == null)
                return;

            cam.transform.position = new Vector3(19.5f, 15.5f, 0.0f);
            cam.transform.rotation = Quaternion.Euler(52f, 90f, 0f);
        }

        static void RenderCombatValidationScreenshot()
        {
            const int width = 1280;
            const int height = 720;

            var shotGo = new GameObject("CombatValidationScreenshotCamera");
            var shotCam = shotGo.AddComponent<Camera>();
            shotCam.clearFlags = CameraClearFlags.SolidColor;
            shotCam.backgroundColor = new Color(0.11f, 0.11f, 0.13f, 1f);
            shotCam.fieldOfView = 28f;
            shotCam.transform.position = new Vector3(20.5f, 11.0f, 0.0f);
            shotCam.transform.rotation = Quaternion.Euler(38f, 90f, 0f);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prevActive = RenderTexture.active;
            shotCam.targetTexture = rt;
            shotCam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            File.WriteAllBytes(CombatValidationScreenshotPath, tex.EncodeToPNG());

            RenderTexture.active = prevActive;
            shotCam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(shotGo);
        }

        static MLGridPos[] BuildLanePath()
        {
            return new[]
            {
                new MLGridPos { x = 5, y = 0 },
                new MLGridPos { x = 5, y = 7 },
                new MLGridPos { x = 5, y = 14 },
                new MLGridPos { x = 5, y = 21 },
                new MLGridPos { x = 5, y = 27 },
            };
        }

        static MLGridPos[] BuildFullBranchPath()
        {
            var path = new MLGridPos[28];
            for (int i = 0; i < path.Length; i++)
                path[i] = new MLGridPos { x = 5, y = i };
            return path;
        }
    }
}
#endif
