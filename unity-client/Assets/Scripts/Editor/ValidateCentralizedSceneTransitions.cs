#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CastleDefender.Editor
{
    public sealed class ValidateCentralizedSceneTransitions : IPreprocessBuildWithReport
    {
        static readonly string[] RootFolders =
        {
            "Assets/Scripts",
            "Assets/Tests",
        };

        static readonly string[] AllowedFiles =
        {
            NormalizePath("Assets/Scripts/Net/LoadingScreen.cs"),
            NormalizePath("Assets/Tests/PlayMode/BootstrapRemoteSceneFlowTests.cs"),
        };

        public int callbackOrder => 0;

        [MenuItem("Castle Defender/Remote Content/Validate Centralized Scene Transitions")]
        public static void ValidateMenu()
        {
            var violations = FindViolations();
            if (violations.Count == 0)
            {
                Debug.Log("[ValidateCentralizedSceneTransitions] No forbidden SceneManager.LoadScene usage found outside the transition service.");
                return;
            }

            Debug.LogError(BuildFailureMessage(violations));
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var violations = FindViolations();
            if (violations.Count == 0)
                return;

            throw new BuildFailedException(BuildFailureMessage(violations));
        }

        static List<string> FindViolations()
        {
            var violations = new List<string>();

            for (int rootIndex = 0; rootIndex < RootFolders.Length; rootIndex++)
            {
                string root = RootFolders[rootIndex];
                if (!AssetDatabase.IsValidFolder(root))
                    continue;

                string absoluteRoot = Path.GetFullPath(root);
                string[] files = Directory.GetFiles(absoluteRoot, "*.cs", SearchOption.AllDirectories);
                for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    string absoluteFile = files[fileIndex];
                    string assetPath = NormalizePath(RelativeToProject(absoluteFile));
                    if (IsAllowed(assetPath) || IsEditorOnly(assetPath))
                        continue;

                    string[] lines = File.ReadAllLines(absoluteFile);
                    for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                    {
                        string line = lines[lineIndex];
                        if (!ContainsForbiddenLoadSceneCall(line))
                            continue;

                        violations.Add($"{assetPath}:{lineIndex + 1} -> {line.Trim()}");
                    }
                }
            }

            return violations;
        }

        static bool ContainsForbiddenLoadSceneCall(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return line.Contains("SceneManager.LoadScene(", StringComparison.Ordinal)
                || line.Contains("SceneManager.LoadSceneAsync(", StringComparison.Ordinal);
        }

        static bool IsAllowed(string assetPath)
        {
            for (int i = 0; i < AllowedFiles.Length; i++)
            {
                if (string.Equals(assetPath, AllowedFiles[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool IsEditorOnly(string assetPath)
        {
            return assetPath.Contains("/Editor/", StringComparison.OrdinalIgnoreCase)
                || assetPath.Contains("/Tests/", StringComparison.OrdinalIgnoreCase);
        }

        static string BuildFailureMessage(List<string> violations)
        {
            return
                "[ValidateCentralizedSceneTransitions] Direct SceneManager.LoadScene usage is forbidden outside LoadingScreen.\n"
                + string.Join("\n", violations)
                + "\nUse LoadingScreen.LoadScene(...) or one of its gated helpers instead.";
        }

        static string RelativeToProject(string absolutePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string relativePath = Path.GetRelativePath(projectRoot, absolutePath);
            return NormalizePath(relativePath);
        }

        static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
#endif
