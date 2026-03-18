using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReportMissingScripts
{
    [MenuItem("Castle Defender/Debug/Report Missing Scripts In Active Scene")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var results = new List<string>();

        foreach (var root in roots)
            Scan(root.transform, root.name, results);

        if (results.Count == 0)
        {
            Debug.Log($"[ReportMissingScripts] No missing scripts found in scene '{scene.name}'.");
            return;
        }

        Debug.LogError($"[ReportMissingScripts] Found {results.Count} missing script reference(s) in scene '{scene.name}':\n" + string.Join("\n", results));
    }

    static void Scan(Transform current, string path, List<string> results)
    {
        var components = current.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] == null)
                results.Add($"- {path} (component index {i})");
        }

        for (int i = 0; i < current.childCount; i++)
        {
            var child = current.GetChild(i);
            Scan(child, $"{path}/{child.name}", results);
        }
    }
}
