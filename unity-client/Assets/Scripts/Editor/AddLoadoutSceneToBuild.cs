// Temporary editor utility — run once via Castle Defender → Setup → Add Loadout Scene to Build
// Safe to delete after running.
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public static class AddLoadoutSceneToBuild
{
    [MenuItem("Castle Defender/Setup/Add Loadout Scene to Build")]
    public static void Run()
    {
        const string loadoutPath = "Assets/Scenes/Loadout.unity";

        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

        // Remove any existing entry for Loadout (stale path)
        scenes.RemoveAll(s => s.path.Contains("Loadout"));

        // Find index of Game_ML to insert before it
        int insertAt = scenes.FindIndex(s => s.path.Contains("Game_ML"));
        if (insertAt < 0) insertAt = scenes.Count;

        scenes.Insert(insertAt, new EditorBuildSettingsScene(loadoutPath, true));
        EditorBuildSettings.scenes = scenes.ToArray();

        Debug.Log($"[Setup] Loadout scene added to Build Settings at index {insertAt}.");
    }
}
