using System;
using System.IO;
using System.Reflection;
using CastleDefender.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    [InitializeOnLoad]
    public static class ClassicRpgUiScreenshotAutomation
    {
        const string SessionKey = "CastleDefender.ClassicRpgUiScreenshotAutomation.State";
        const string LastOutputRootKey = "CastleDefender.ClassicRpgUiScreenshotAutomation.LastOutputRoot";

        enum CaptureStage
        {
            Idle = 0,
            WaitingForPlayMode = 1,
            WaitingForSceneBootstrap = 2,
            WaitingForTechTreeSetup = 3,
            WaitingForFileWrite = 4,
            WaitingForPlayModeExit = 5,
            WaitingForLoginFinalPresentation = 6,
        }

        enum CaptureSetup
        {
            None = 0,
            LoadoutRaceSelection = 1,
            LoadoutTechTree = 2,
            LoginFinalPresentation = 3,
        }

        [Serializable]
        struct CaptureState
        {
            public bool Running;
            public int RequestIndex;
            public int Stage;
            public int RemainingTicks;
            public int CaptureTimeoutTicks;
            public string OriginalScenePath;
            public string OutputRoot;
            public string PendingScreenshotPath;
            public bool IncludeMobile;
            public bool LoginOnly;
            public bool TriggeredLoginSkip;
        }

        readonly struct CaptureRequest
        {
            public readonly string Label;
            public readonly string ScenePath;
            public readonly int Width;
            public readonly int Height;
            public readonly int BootstrapTicks;
            public readonly int SettleTicks;
            public readonly CaptureSetup Setup;
            public readonly string RaceId;
            public readonly string SelectedUnitId;

            public CaptureRequest(
                string label,
                string scenePath,
                int width,
                int height,
                CaptureSetup setup,
                string raceId,
                int bootstrapTicks,
                int settleTicks,
                string selectedUnitId = null)
            {
                Label = label;
                ScenePath = scenePath;
                Width = width;
                Height = height;
                Setup = setup;
                RaceId = raceId;
                BootstrapTicks = bootstrapTicks;
                SettleTicks = settleTicks;
                SelectedUnitId = selectedUnitId;
            }
        }

        static readonly CaptureRequest[] DesktopRequests =
        {
            new("login-desktop", "Assets/Scenes/Login.unity", 1920, 1080, CaptureSetup.LoginFinalPresentation, null, 24, 6),
            new("lobby-desktop", "Assets/Scenes/Lobby.unity", 1920, 1080, CaptureSetup.None, null, 90, 20),
            new("loadout-desktop", "Assets/Scenes/Loadout.unity", 1920, 1080, CaptureSetup.LoadoutRaceSelection, RaceProgressionCatalog.DefaultRaceId, 110, 24),
            new("tech-tree-desktop", "Assets/Scenes/Loadout.unity", 1920, 1080, CaptureSetup.LoadoutTechTree, RaceProgressionCatalog.DefaultRaceId, 110, 40, "king"),
        };

        static readonly CaptureRequest[] MobileRequests =
        {
            new("login-mobile", "Assets/Scenes/Login.unity", 2400, 1080, CaptureSetup.LoginFinalPresentation, null, 24, 6),
            new("lobby-mobile", "Assets/Scenes/Lobby.unity", 2400, 1080, CaptureSetup.None, null, 90, 20),
            new("loadout-mobile", "Assets/Scenes/Loadout.unity", 2400, 1080, CaptureSetup.LoadoutRaceSelection, RaceProgressionCatalog.DefaultRaceId, 110, 24),
            new("tech-tree-mobile", "Assets/Scenes/Loadout.unity", 2400, 1080, CaptureSetup.LoadoutTechTree, RaceProgressionCatalog.DefaultRaceId, 110, 40, "king"),
        };

        static CaptureState _state;

        static ClassicRpgUiScreenshotAutomation()
        {
            LoadState();
            EditorApplication.update += OnEditorUpdate;
        }

        public static bool IsCaptureBatchRunning
        {
            get
            {
                LoadState();
                return _state.Running;
            }
        }

        [MenuItem("Castle Defender/UI/Capture Priority Screenshots/Desktop Only")]
        public static void CaptureDesktopOnly()
        {
            StartCapture(includeMobile: false, loginOnly: false);
        }

        [MenuItem("Castle Defender/UI/Capture Priority Screenshots/Desktop + Mobile")]
        public static void CaptureDesktopAndMobile()
        {
            StartCapture(includeMobile: true, loginOnly: false);
        }

        [MenuItem("Castle Defender/UI/Capture Login Screenshots/Desktop Only")]
        public static void CaptureLoginDesktopOnly()
        {
            StartCapture(includeMobile: false, loginOnly: true);
        }

        [MenuItem("Castle Defender/UI/Capture Login Screenshots/Desktop + Mobile")]
        public static void CaptureLoginDesktopAndMobile()
        {
            StartCapture(includeMobile: true, loginOnly: true);
        }

        [MenuItem("Castle Defender/UI/Open Last Screenshot Folder")]
        public static void OpenLastScreenshotFolder()
        {
            string path = SessionState.GetString(LastOutputRootKey, string.Empty);
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                Debug.LogWarning("[ClassicRpgUiScreenshots] No screenshot folder has been created yet.");
                return;
            }

            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Castle Defender/UI/Reset Screenshot Capture State")]
        public static void ResetCaptureState()
        {
            _state = default;
            SaveState();
            Debug.Log("[ClassicRpgUiScreenshots] Cleared screenshot capture state.");
        }

        static void StartCapture(bool includeMobile, bool loginOnly)
        {
            LoadState();
            if (_state.Running)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || Application.isPlaying)
                {
                    Debug.LogWarning("[ClassicRpgUiScreenshots] A capture batch is already running.");
                    return;
                }

                Debug.LogWarning("[ClassicRpgUiScreenshots] Found stale capture state. Resetting and starting a new batch.");
                ResetCaptureState();
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ClassicRpgUiScreenshots] Wait for play mode to stop before starting a screenshot batch.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            _state = new CaptureState
            {
                Running = true,
                RequestIndex = -1,
                Stage = (int)CaptureStage.Idle,
                RemainingTicks = 0,
                CaptureTimeoutTicks = 0,
                OriginalScenePath = SceneManager.GetActiveScene().path,
                OutputRoot = BuildOutputRoot(),
                PendingScreenshotPath = string.Empty,
                IncludeMobile = includeMobile,
                LoginOnly = loginOnly,
                TriggeredLoginSkip = false,
            };

            Directory.CreateDirectory(_state.OutputRoot);
            SessionState.SetString(LastOutputRootKey, _state.OutputRoot);
            SaveState();

            Debug.Log($"[ClassicRpgUiScreenshots] Saving screenshots to '{_state.OutputRoot}'.");
            StartNextRequest();
        }

        static void OnEditorUpdate()
        {
            LoadState();
            if (!_state.Running)
                return;

            var request = GetCurrentRequest();
            switch ((CaptureStage)_state.Stage)
            {
                case CaptureStage.WaitingForPlayMode:
                    if (Application.isPlaying)
                    {
                        _state.Stage = (int)CaptureStage.WaitingForSceneBootstrap;
                        _state.RemainingTicks = request.BootstrapTicks;
                        SaveState();
                    }
                    break;

                case CaptureStage.WaitingForSceneBootstrap:
                    if (!Application.isPlaying)
                        return;

                    if (_state.RemainingTicks > 0)
                    {
                        _state.RemainingTicks--;
                        SaveState();
                        return;
                    }

                    if (request.Setup == CaptureSetup.LoadoutTechTree)
                    {
                        if (!TryOpenTechTree(request.RaceId))
                        {
                            Debug.LogWarning("[ClassicRpgUiScreenshots] Loadout manager was not ready for tech tree setup yet. Waiting a bit longer.");
                            _state.RemainingTicks = 30;
                            SaveState();
                            return;
                        }

                        if (!TrySelectTechTreeUnit(request.SelectedUnitId))
                            Debug.LogWarning($"[ClassicRpgUiScreenshots] Could not select tech tree unit '{request.SelectedUnitId}'.");

                        _state.Stage = (int)CaptureStage.WaitingForTechTreeSetup;
                        _state.RemainingTicks = request.SettleTicks;
                        SaveState();
                        return;
                    }

                    if (request.Setup == CaptureSetup.LoginFinalPresentation)
                    {
                        _state.Stage = (int)CaptureStage.WaitingForLoginFinalPresentation;
                        _state.RemainingTicks = -1;
                        _state.CaptureTimeoutTicks = 1800;
                        SaveState();
                        return;
                    }

                    CaptureCurrentRequest(request);
                    break;

                case CaptureStage.WaitingForTechTreeSetup:
                    if (!Application.isPlaying)
                        return;

                    if (_state.RemainingTicks > 0)
                    {
                        _state.RemainingTicks--;
                        SaveState();
                        return;
                    }

                    CaptureCurrentRequest(request);
                    break;

                case CaptureStage.WaitingForLoginFinalPresentation:
                    if (!Application.isPlaying)
                        return;

                    var loginUi = UnityEngine.Object.FindFirstObjectByType<LoginUI>(FindObjectsInactive.Include);
                    string readiness = loginUi != null ? loginUi.FinalRuntimeScreenshotState : "LoginUI not found";
                    if (!_state.TriggeredLoginSkip && loginUi != null)
                    {
                        loginUi.SkipIntroForAutomation();
                        _state.TriggeredLoginSkip = true;
                        SaveState();
                        return;
                    }

                    bool readyForCapture = loginUi != null && loginUi.IsReadyForFinalRuntimeScreenshot;

                    if (!readyForCapture)
                    {
                        _state.RemainingTicks = -1;
                        if (_state.CaptureTimeoutTicks > 0)
                        {
                            _state.CaptureTimeoutTicks--;
                            if (_state.CaptureTimeoutTicks % 180 == 0)
                                Debug.Log($"[ClassicRpgUiScreenshots] Waiting for final login frame: {readiness}");
                            SaveState();
                            return;
                        }

                        Debug.LogWarning($"[ClassicRpgUiScreenshots] Timed out waiting for final login frame. Capturing current state: {readiness}");
                        CaptureCurrentRequest(request);
                        return;
                    }

                    if (_state.RemainingTicks < 0)
                    {
                        _state.RemainingTicks = request.SettleTicks;
                        SaveState();
                        return;
                    }

                    if (_state.RemainingTicks > 0)
                    {
                        _state.RemainingTicks--;
                        SaveState();
                        return;
                    }

                    Debug.Log($"[ClassicRpgUiScreenshots] Final login frame ready: {readiness}");
                    CaptureCurrentRequest(request);
                    return;

                case CaptureStage.WaitingForFileWrite:
                    if (File.Exists(_state.PendingScreenshotPath))
                    {
                        var info = new FileInfo(_state.PendingScreenshotPath);
                        if (info.Exists && info.Length > 0)
                        {
                            Debug.Log($"[ClassicRpgUiScreenshots] Captured '{request.Label}' -> '{_state.PendingScreenshotPath}'.");
                            _state.Stage = (int)CaptureStage.WaitingForPlayModeExit;
                            SaveState();
                            EditorApplication.isPlaying = false;
                            return;
                        }
                    }

                    if (_state.CaptureTimeoutTicks > 0)
                    {
                        _state.CaptureTimeoutTicks--;
                        SaveState();
                        return;
                    }

                    Debug.LogWarning($"[ClassicRpgUiScreenshots] Timed out waiting for '{_state.PendingScreenshotPath}'. Exiting play mode and continuing.");
                    _state.Stage = (int)CaptureStage.WaitingForPlayModeExit;
                    SaveState();
                    EditorApplication.isPlaying = false;
                    break;

                case CaptureStage.WaitingForPlayModeExit:
                    if (!EditorApplication.isPlayingOrWillChangePlaymode && !Application.isPlaying)
                        StartNextRequest();
                    break;
            }
        }

        static void CaptureCurrentRequest(CaptureRequest request)
        {
            string path = BuildScreenshotPath(_state.OutputRoot, request);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _state.OutputRoot);
            if (File.Exists(path))
                File.Delete(path);

            ScreenCapture.CaptureScreenshot(path);
            _state.PendingScreenshotPath = path;
            _state.CaptureTimeoutTicks = 180;
            _state.Stage = (int)CaptureStage.WaitingForFileWrite;
            SaveState();
        }

        static bool TryOpenTechTree(string raceId)
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<LoadoutPhaseManager>(FindObjectsInactive.Include);
            if (manager == null)
                return false;

            string resolvedRaceId = string.IsNullOrWhiteSpace(raceId) ? RaceProgressionCatalog.DefaultRaceId : raceId;
            var method = typeof(LoadoutPhaseManager).GetMethod("OnRaceSelected", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                Debug.LogError("[ClassicRpgUiScreenshots] Could not find LoadoutPhaseManager.OnRaceSelected.");
                return false;
            }

            method.Invoke(manager, new object[] { resolvedRaceId });
            var continueMethod = typeof(LoadoutPhaseManager).GetMethod("HandlePrimaryAction", BindingFlags.Instance | BindingFlags.NonPublic);
            if (continueMethod == null)
            {
                Debug.LogError("[ClassicRpgUiScreenshots] Could not find LoadoutPhaseManager.HandlePrimaryAction.");
                return false;
            }

            continueMethod.Invoke(manager, null);
            Canvas.ForceUpdateCanvases();
            return true;
        }

        static bool TrySelectTechTreeUnit(string unitId)
        {
            if (string.IsNullOrWhiteSpace(unitId))
                return true;

            var manager = UnityEngine.Object.FindFirstObjectByType<LoadoutPhaseManager>(FindObjectsInactive.Include);
            if (manager == null)
                return false;

            var method = typeof(LoadoutPhaseManager).GetMethod("OnUnitSelected", BindingFlags.Instance | BindingFlags.NonPublic);
            if (method == null)
            {
                Debug.LogError("[ClassicRpgUiScreenshots] Could not find LoadoutPhaseManager.OnUnitSelected.");
                return false;
            }

            method.Invoke(manager, new object[] { unitId });
            Canvas.ForceUpdateCanvases();
            return true;
        }

        static void StartNextRequest()
        {
            _state.RequestIndex++;
            var requests = GetRequestSet(_state.IncludeMobile, _state.LoginOnly);
            if (_state.RequestIndex >= requests.Length)
            {
                FinishCapture();
                return;
            }

            var request = requests[_state.RequestIndex];
            ProgressionViewerLaunchContext.Clear();
            if (request.Setup == CaptureSetup.LoadoutRaceSelection || request.Setup == CaptureSetup.LoadoutTechTree)
                ProgressionViewerLaunchContext.OpenLobbyViewer(request.RaceId);

            TryConfigureGameView(request.Width, request.Height, request.Label);
            EditorSceneManager.OpenScene(request.ScenePath, OpenSceneMode.Single);

            _state.PendingScreenshotPath = string.Empty;
            _state.CaptureTimeoutTicks = 0;
            _state.TriggeredLoginSkip = false;
            _state.Stage = (int)CaptureStage.WaitingForPlayMode;
            SaveState();

            Debug.Log($"[ClassicRpgUiScreenshots] Opening '{request.Label}' ({request.Width}x{request.Height}).");
            EditorApplication.isPlaying = true;
        }

        static void FinishCapture()
        {
            string outputRoot = _state.OutputRoot;
            string originalScenePath = _state.OriginalScenePath;
            _state = default;
            SaveState();

            if (!string.IsNullOrWhiteSpace(originalScenePath) && File.Exists(originalScenePath))
                EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);

            AssetDatabase.Refresh();
            Debug.Log($"[ClassicRpgUiScreenshots] Capture batch complete. Files saved to '{outputRoot}'.");
        }

        static string BuildOutputRoot()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return Path.Combine(projectRoot, "projects", "ui-rebuild-screenshots", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        }

        static string BuildScreenshotPath(string outputRoot, CaptureRequest request)
        {
            return Path.Combine(outputRoot, $"{request.Label}-{request.Width}x{request.Height}.png");
        }

        static CaptureRequest[] GetRequestSet(bool includeMobile, bool loginOnly)
        {
            if (loginOnly)
            {
                if (!includeMobile)
                    return new[] { DesktopRequests[0] };

                return new[] { DesktopRequests[0], MobileRequests[0] };
            }

            if (!includeMobile)
                return DesktopRequests;

            var requests = new CaptureRequest[DesktopRequests.Length + MobileRequests.Length];
            Array.Copy(DesktopRequests, requests, DesktopRequests.Length);
            Array.Copy(MobileRequests, 0, requests, DesktopRequests.Length, MobileRequests.Length);
            return requests;
        }

        static CaptureRequest GetCurrentRequest()
        {
            var requests = GetRequestSet(_state.IncludeMobile, _state.LoginOnly);
            if (_state.RequestIndex < 0 || _state.RequestIndex >= requests.Length)
                return default;
            return requests[_state.RequestIndex];
        }

        static void SaveState()
        {
            SessionState.SetString(SessionKey, JsonUtility.ToJson(_state));
        }

        static void LoadState()
        {
            string json = SessionState.GetString(SessionKey, string.Empty);
            _state = string.IsNullOrWhiteSpace(json) ? default : JsonUtility.FromJson<CaptureState>(json);
        }

        static void TryConfigureGameView(int width, int height, string label)
        {
            try
            {
                EditorApplication.ExecuteMenuItem("Window/General/Game");

                var editorAssembly = typeof(EditorWindow).Assembly;
                var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
                var gameView = EditorWindow.GetWindow(gameViewType);

                var sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
                var singleType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singleType.GetProperty("instance")?.GetValue(null, null);
                var getGroup = sizesType?.GetMethod("GetGroup");
                var group = getGroup?.Invoke(instance, new object[] { (int)GameViewSizeGroupType.Standalone });
                if (group == null || gameViewType == null || gameView == null)
                    return;

                var groupType = group.GetType();
                int builtinCount = (int)(groupType.GetMethod("GetBuiltinCount")?.Invoke(group, null) ?? 0);
                int customCount = (int)(groupType.GetMethod("GetCustomCount")?.Invoke(group, null) ?? 0);

                var sizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
                var sizeEnumType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
                var ctor = sizeType?.GetConstructor(new[] { sizeEnumType, typeof(int), typeof(int), typeof(string) });
                var fixedResolution = Enum.Parse(sizeEnumType!, "FixedResolution");
                object newSize = ctor?.Invoke(new object[] { fixedResolution, width, height, $"Codex {label}" });
                groupType.GetMethod("AddCustomSize")?.Invoke(group, new[] { newSize });

                int targetIndex = builtinCount + customCount;
                gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.SetValue(gameView, targetIndex, null);

                gameView.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClassicRpgUiScreenshots] Could not force Game View resolution {width}x{height}: {ex.Message}");
            }
        }
    }
}
