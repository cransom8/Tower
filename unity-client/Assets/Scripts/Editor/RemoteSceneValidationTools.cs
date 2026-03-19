#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using CastleDefender.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    public static class RemoteSceneValidationTools
    {
        const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        static int s_framesUntilLog = -1;

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

        [MenuItem("Castle Defender/Remote Scene Validation/Transition/PostGame")]
        public static void TransitionToPostGame() => RunTransition(() => LoadingScreen.LoadScene("PostGame"));

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
            if (!Application.isPlaying || s_framesUntilLog < 0)
                return;

            s_framesUntilLog--;
            if (s_framesUntilLog > 0)
                return;

            s_framesUntilLog = -1;
            LogRuntimeSceneState();
        }
    }
}
#endif
