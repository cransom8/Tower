// BootstrapManager.cs — Bootstraps all DontDestroyOnLoad singletons then loads Login.
//
// SETUP:
//   1. Create a "Bootstrap" scene (Assets/Scenes/Bootstrap.unity) and set it to
//      Build Index 0 (above Login).
//   2. Add a single "Bootstrap" GameObject in that scene and attach this script.
//   3. Move NetworkManager, AudioManager, AuthManager, CatalogLoader,
//      SnapshotApplier, LoadingScreen, PostProcessController prefabs/GameObjects into
//      the Bootstrap scene. Remove them from Login.unity.
//
// Flow: Bootstrap scene loads → singletons Awake (DontDestroyOnLoad) → Start() fires
// → Login scene loads additively → Bootstrap scene unloads → Login takes over.

using System;
using System.Collections;
using UnityEngine;

namespace CastleDefender.Net
{
    public class BootstrapManager : MonoBehaviour
    {
        public const string EditorSceneOverridePrefKey = "castle_defender.bootstrap.editor_scene_override";

        [Tooltip("Name of the first scene to load after singletons have initialised.")]
        [SerializeField] string FirstScene = "Login";
        GameObject _runtimeAudioListener;

        void Awake()
        {
            EnsureBootstrapAudioListener();
            Debug.Log($"[BootstrapManager] Awake on '{gameObject.scene.name}'.");
        }

        IEnumerator Start()
        {
            string firstScene = ResolveFirstScene();
            Debug.Log($"[BootstrapManager] Start beginning. FirstScene='{firstScene}'.");
            // Give all Awake() calls one frame to complete before transitioning.
            yield return null;
            Debug.Log($"[BootstrapManager] Requesting LoadingScreen transition to '{firstScene}'.");

            if (string.Equals(firstScene, "Game_ML", StringComparison.OrdinalIgnoreCase))
            {
                LoadingScreen.LoadSceneWithRemoteContentGate(
                    firstScene,
                    preloadEnvironment: true);
                yield break;
            }

            LoadingScreen.LoadScene(firstScene);
        }

        string ResolveFirstScene()
        {
#if UNITY_EDITOR
            string overrideScene = ConsumeEditorSceneOverride();
            if (!string.IsNullOrWhiteSpace(overrideScene))
                return overrideScene;
#endif
            return FirstScene;
        }

#if UNITY_EDITOR
        static string ConsumeEditorSceneOverride()
        {
            string overrideScene = PlayerPrefs.GetString(EditorSceneOverridePrefKey, string.Empty);
            if (string.IsNullOrWhiteSpace(overrideScene))
                return null;

            PlayerPrefs.DeleteKey(EditorSceneOverridePrefKey);
            PlayerPrefs.Save();
            return overrideScene.Trim();
        }
#endif

        void EnsureBootstrapAudioListener()
        {
            if (_runtimeAudioListener != null)
                return;

            if (FindFirstObjectByType<AudioListener>(FindObjectsInactive.Include) != null)
                return;

            _runtimeAudioListener = new GameObject("BootstrapAudioListener");
            _runtimeAudioListener.AddComponent<AudioListener>();
        }

        void OnDestroy()
        {
            if (_runtimeAudioListener == null)
                return;

            Destroy(_runtimeAudioListener);
            _runtimeAudioListener = null;
        }
    }
}
