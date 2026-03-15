#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteContentBuildAddressables
    {
        [MenuItem("Castle Defender/Remote Content/Build Addressables Content")]
        static void BuildAddressablesContent()
        {
            var settingsDefaultType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            var settingsType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
            var buildResultType = Type.GetType("UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult, Unity.Addressables.Editor");

            if (settingsType == null || settingsDefaultType == null)
            {
                Debug.LogError("[RemoteContentBuildAddressables] Addressables editor API unavailable.");
                return;
            }

            object settings = GetSettings(settingsDefaultType);
            if (settings == null)
            {
                Debug.LogError("[RemoteContentBuildAddressables] Failed to load Addressables settings.");
                return;
            }

            string previousBuilder = EditorUserBuildSettings.activeBuildTarget.ToString();
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            try
            {
                var cleanPlayerContent = settingsType.GetMethod("CleanPlayerContent", BindingFlags.Public | BindingFlags.Static);
                if (cleanPlayerContent != null)
                    cleanPlayerContent.Invoke(null, new object[] { null });

                var buildPlayerContent = settingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                var buildPlayerContentWithResult = buildResultType == null
                    ? null
                    : settingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, new[] { buildResultType.MakeByRefType() }, null);

                if (buildPlayerContent == null && buildPlayerContentWithResult == null)
                {
                    Debug.LogError("[RemoteContentBuildAddressables] BuildPlayerContent API not found.");
                    return;
                }

                object result = null;
                if (buildPlayerContentWithResult != null)
                {
                    var args = new[] { Activator.CreateInstance(buildResultType) };
                    buildPlayerContentWithResult.Invoke(null, args);
                    result = args[0];
                }
                else
                {
                    buildPlayerContent.Invoke(null, null);
                }

                string publishedPath = PublishBuildOutput(result);
                string summary = DescribeResult(result);
                string[] catalogs = FindCatalogs();
                Debug.Log($"[RemoteContentBuildAddressables] Build complete. {summary} PublishedPath={publishedPath ?? "unpublished"}");
                if (catalogs.Length > 0)
                    Debug.Log("[RemoteContentBuildAddressables] Catalogs:\n" + string.Join("\n", catalogs));
            }
            finally
            {
                if (!string.Equals(previousBuilder, "WebGL", StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.Refresh();
            }
        }

        static object GetSettings(Type settingsDefaultType)
        {
            var getSettingsMethod = settingsDefaultType.GetMethod("GetSettings", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (getSettingsMethod != null)
                return getSettingsMethod.Invoke(null, new object[] { true });

            return settingsDefaultType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        }

        static string DescribeResult(object result)
        {
            if (result == null) return "No result object returned.";

            var resultType = result.GetType();
            var error = resultType.GetProperty("Error")?.GetValue(result) as string
                ?? resultType.GetField("Error")?.GetValue(result) as string;
            if (!string.IsNullOrWhiteSpace(error))
                return $"Result error: {error}";

            var duration = resultType.GetProperty("Duration")?.GetValue(result)
                ?? resultType.GetField("Duration")?.GetValue(result);
            var outputPath = resultType.GetProperty("OutputPath")?.GetValue(result) as string
                ?? resultType.GetField("OutputPath")?.GetValue(result) as string;

            return $"Duration={duration ?? "unknown"} OutputPath={outputPath ?? "unknown"}";
        }

        static string PublishBuildOutput(object result)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath = GetOutputPath(result);
            if (string.IsNullOrWhiteSpace(outputPath)) return null;

            string sourceDir = Directory.Exists(outputPath)
                ? outputPath
                : Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                return null;

            string publishedRoot = Path.Combine(projectRoot, "ServerData", EditorUserBuildSettings.activeBuildTarget.ToString());
            CopyDirectory(sourceDir, publishedRoot);
            return publishedRoot;
        }

        static string GetOutputPath(object result)
        {
            if (result == null) return null;
            var resultType = result.GetType();
            return resultType.GetProperty("OutputPath")?.GetValue(result) as string
                ?? resultType.GetField("OutputPath")?.GetValue(result) as string;
        }

        static void CopyDirectory(string sourceDir, string destinationDir)
        {
            if (Directory.Exists(destinationDir))
                Directory.Delete(destinationDir, true);

            Directory.CreateDirectory(destinationDir);

            foreach (string filePath in Directory.GetFiles(sourceDir))
                File.Copy(filePath, Path.Combine(destinationDir, Path.GetFileName(filePath)), true);

            foreach (string childDir in Directory.GetDirectories(sourceDir))
                CopyDirectory(childDir, Path.Combine(destinationDir, Path.GetFileName(childDir)));
        }

        static string[] FindCatalogs()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string serverData = Path.Combine(projectRoot, "ServerData");
            if (!Directory.Exists(serverData))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(serverData, "*.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(serverData, "*.hash", SearchOption.AllDirectories))
                .OrderBy(path => path)
                .Select(path => path.Replace(projectRoot + Path.DirectorySeparatorChar, string.Empty))
                .ToArray();
        }
    }
}
#endif
