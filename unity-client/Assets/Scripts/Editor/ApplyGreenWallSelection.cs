using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class ApplyGreenWallSelection
{
    const string MenuPath = "Castle Defender/Setup/Apply Green Wall Materials To Selection";

    static readonly string[] GreenMaterialPaths =
    {
        "Assets/Materials/TT/TT_RTS_buildings_green_URP.mat",
        "Assets/Materials/TT_RTS_Buildings_Green_URP.mat",
    };

    [MenuItem(MenuPath)]
    static void Apply()
    {
        var green = LoadGreenMaterial();
        if (green == null)
        {
            Debug.LogError("[ApplyGreenWallSelection] Could not find the green TT_RTS building material.");
            return;
        }

        var selection = Selection.gameObjects;
        if (selection == null || selection.Length == 0)
        {
            Debug.LogWarning("[ApplyGreenWallSelection] Select one or more wall objects first.");
            return;
        }

        var processed = new HashSet<Renderer>();
        int rendererCount = 0;

        foreach (var go in selection)
        {
            if (go == null)
                continue;

            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !processed.Add(renderer) || !ShouldRetintRenderer(renderer, go))
                    continue;

                var shared = renderer.sharedMaterials;
                if (shared == null || shared.Length == 0)
                    continue;

                bool changed = false;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == green)
                        continue;

                    shared[i] = green;
                    changed = true;
                }

                if (!changed)
                    continue;

                Undo.RecordObject(renderer, "Apply Green Wall Materials");
                renderer.sharedMaterials = shared;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
                rendererCount++;
            }
        }

        if (rendererCount == 0)
        {
            Debug.LogWarning("[ApplyGreenWallSelection] No TT_RTS wall renderers were updated from the current selection.");
            return;
        }

        if (Selection.activeGameObject != null && Selection.activeGameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(Selection.activeGameObject.scene);

        AssetDatabase.SaveAssets();
        Debug.Log($"[ApplyGreenWallSelection] Applied green wall material to {rendererCount} renderer(s).");
    }

    [MenuItem(MenuPath, true)]
    static bool ValidateApply() => Selection.gameObjects != null && Selection.gameObjects.Length > 0;

    static Material LoadGreenMaterial()
    {
        foreach (var path in GreenMaterialPaths)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;
        }

        return null;
    }

    static bool ShouldRetintRenderer(Renderer renderer, GameObject selectedRoot)
    {
        if (renderer == null)
            return false;

        if (HasWallLikeName(renderer.gameObject.name) || HasWallLikeName(selectedRoot.name))
            return true;

        var materials = renderer.sharedMaterials;
        if (materials == null)
            return false;

        for (int i = 0; i < materials.Length; i++)
        {
            var mat = materials[i];
            if (mat == null)
                continue;

            var path = AssetDatabase.GetAssetPath(mat);
            if (path.IndexOf("TT_RTS", StringComparison.OrdinalIgnoreCase) >= 0 &&
                path.IndexOf("Buildings", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    static bool HasWallLikeName(string name)
    {
        return !string.IsNullOrEmpty(name) &&
               name.StartsWith("Wall_", StringComparison.OrdinalIgnoreCase);
    }
}
