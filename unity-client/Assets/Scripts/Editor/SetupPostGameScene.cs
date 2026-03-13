using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CastleDefender.UI;

public static class SetupPostGameScene
{
    const string ScenePath = "Assets/Scenes/PostGame.unity";

    [MenuItem("Castle Defender/Setup/Create PostGame Scene")]
    public static void Run()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraGo = new GameObject("Main Camera");
        cameraGo.tag = "MainCamera";
        var camera = cameraGo.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.03f, 0.02f, 0.04f, 1f);
        camera.orthographic = true;
        camera.orthographicSize = 5f;

        var root = new GameObject("PostGameSceneRoot");
        root.AddComponent<PostGameSceneController>();

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log("[SetupPostGameScene] PostGame scene created and added to Build Settings.");
    }

    static void AddToBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == ScenePath);

        int insertAt = scenes.FindIndex(s => s.path.Contains("Game_ML"));
        insertAt = insertAt >= 0 ? insertAt + 1 : scenes.Count;
        scenes.Insert(insertAt, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
