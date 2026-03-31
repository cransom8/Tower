#if UNITY_EDITOR
// SceneSetup.cs - Wires manager components into the Lobby scene and sets
// Script Execution Order so AuthManager runs before NetworkManager.
//
// Menu: Castle Defender -> Setup -> Wire Managers into Lobby Scene

using CastleDefender.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneSetup
{
    const string LobbyScenePath = "Assets/Scenes/Lobby.unity";

    [MenuItem("Castle Defender/Setup/Wire Managers into Lobby Scene")]
    static void WireManagers()
    {
        var currentScene = SceneManager.GetActiveScene();
        bool lobbyAlreadyOpen = currentScene.path == LobbyScenePath;

        Scene lobbyScene;
        if (lobbyAlreadyOpen)
        {
            lobbyScene = currentScene;
        }
        else
        {
            lobbyScene = EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Additive);
        }

        SceneManager.SetActiveScene(lobbyScene);

        GameObject managersGO = null;
        foreach (var go in lobbyScene.GetRootGameObjects())
        {
            if (go.name == "Managers")
            {
                managersGO = go;
                break;
            }

            if (go.GetComponent<NetworkManager>() != null)
            {
                managersGO = go;
                break;
            }
        }

        if (managersGO == null)
        {
            managersGO = new GameObject("Managers");
            SceneManager.MoveGameObjectToScene(managersGO, lobbyScene);
            Debug.Log("[SceneSetup] Created 'Managers' GameObject in Lobby scene.");
        }
        else
        {
            Debug.Log($"[SceneSetup] Using existing GameObject '{managersGO.name}'.");
        }

        AddIfMissing<NetworkManager>(managersGO, "NetworkManager");
        AddIfMissing<AuthManager>(managersGO, "AuthManager");
        AddIfMissing<CatalogLoader>(managersGO, "CatalogLoader");

        SetExecutionOrder<AuthManager>(-200, "AuthManager");
        SetExecutionOrder<CatalogLoader>(-100, "CatalogLoader");
        SetExecutionOrder<NetworkManager>(0, "NetworkManager");

        EditorSceneManager.MarkSceneDirty(lobbyScene);
        EditorSceneManager.SaveScene(lobbyScene);

        if (!lobbyAlreadyOpen)
        {
            EditorSceneManager.CloseScene(lobbyScene, removeScene: true);
        }

        AssetDatabase.SaveAssets();

        Debug.Log("[SceneSetup] Done.");
        Debug.Log("[SceneSetup]   Managers GO has: NetworkManager, AuthManager, CatalogLoader");
        Debug.Log("[SceneSetup]   Execution order: AuthManager(-200) -> CatalogLoader(-100) -> NetworkManager(0)");
        Debug.Log("[SceneSetup]   Next: open Lobby scene and assign NetworkManager.ServerUrl in Inspector.");
    }

    static void AddIfMissing<T>(GameObject go, string label) where T : Component
    {
        if (go.GetComponent<T>() == null)
        {
            go.AddComponent<T>();
            Debug.Log($"[SceneSetup]   Added {label}");
        }
        else
        {
            Debug.Log($"[SceneSetup]   {label} already present - skipped");
        }
    }

    static void SetExecutionOrder<T>(int order, string label) where T : MonoBehaviour
    {
        foreach (var script in MonoImporter.GetAllRuntimeMonoScripts())
        {
            if (script.GetClass() == typeof(T))
            {
                if (MonoImporter.GetExecutionOrder(script) != order)
                {
                    MonoImporter.SetExecutionOrder(script, order);
                    Debug.Log($"[SceneSetup]   Execution order: {label} = {order}");
                }

                return;
            }
        }

        Debug.LogWarning($"[SceneSetup]   Could not find MonoScript for {label}");
    }
}
#endif
