// BootstrapManager.cs — Bootstraps all DontDestroyOnLoad singletons then loads Login.
//
// SETUP:
//   1. Create a "Bootstrap" scene (Assets/Scenes/Bootstrap.unity) and set it to
//      Build Index 0 (above Login).
//   2. Add a single "Bootstrap" GameObject in that scene and attach this script.
//   3. Move NetworkManager, AudioManager, AuthManager, CatalogLoader, LoadoutManager,
//      SnapshotApplier, LoadingScreen, PostProcessController prefabs/GameObjects into
//      the Bootstrap scene. Remove them from Login.unity.
//
// Flow: Bootstrap scene loads → singletons Awake (DontDestroyOnLoad) → Start() fires
// → Login scene loads additively → Bootstrap scene unloads → Login takes over.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Net
{
    public class BootstrapManager : MonoBehaviour
    {
        [Tooltip("Name of the first scene to load after singletons have initialised.")]
        [SerializeField] string FirstScene = "Login";

        IEnumerator Start()
        {
            // Give all Awake() calls one frame to complete before transitioning.
            yield return null;
            SceneManager.LoadScene(FirstScene);
        }
    }
}
