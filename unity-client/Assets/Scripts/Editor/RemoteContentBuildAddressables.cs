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
            BuildForTarget(EditorUserBuildSettings.activeBuildTarget);
        }

        public static AddressablesBuildResult BuildForTarget(BuildTarget target, bool restorePreviousTarget = true)
        {
            var settingsDefaultType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            var settingsType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
            var buildResultType = Type.GetType("UnityEditor.AddressableAssets.Build.AddressablesPlayerBuildResult, Unity.Addressables.Editor");

            if (settingsType == null || settingsDefaultType == null)
                throw new InvalidOperationException("[RemoteContentBuildAddressables] Addressables editor API unavailable.");

            object settings = GetSettings(settingsDefaultType);
            if (settings == null)
                throw new InvalidOperationException("[RemoteContentBuildAddressables] Failed to load Addressables settings.");

            BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;

            try
            {
                SwitchBuildTarget(target);
                SetupRemoteEnvironmentAddressables.RemoveEmbeddedGameMlEnvironmentRoots(logResult: true);
                SetupRemoteEnvironmentAddressables.SanitizeGameEnvironmentPrefab();
                SyncRegistryToAddressables();

                var cleanPlayerContent = settingsType.GetMethod("CleanPlayerContent", BindingFlags.Public | BindingFlags.Static);
                cleanPlayerContent?.Invoke(null, new object[] { null });

                var buildPlayerContent = settingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                var buildPlayerContentWithResult = buildResultType == null
                    ? null
                    : settingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, new[] { buildResultType.MakeByRefType() }, null);

                if (buildPlayerContent == null && buildPlayerContentWithResult == null)
                    throw new MissingMethodException("[RemoteContentBuildAddressables] BuildPlayerContent API not found.");

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

                string summary = DescribeResult(result);
                string publishedPath = PublishBuildOutput(result, target);
                string[] catalogs = FindCatalogs(target);
                string error = ExtractError(result);
                if (!string.IsNullOrWhiteSpace(error))
                    throw new InvalidOperationException($"[RemoteContentBuildAddressables] Addressables build failed: {error}");

                Debug.Log($"[RemoteContentBuildAddressables] Build complete for {target}. {summary} PublishedPath={publishedPath ?? "unpublished"}");
                if (catalogs.Length > 0)
                    Debug.Log("[RemoteContentBuildAddressables] Catalogs:\n" + string.Join("\n", catalogs));

                return new AddressablesBuildResult(target, publishedPath, summary, catalogs);
            }
            finally
            {
                if (restorePreviousTarget)
                    SwitchBuildTarget(previousTarget);

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

        static void SyncRegistryToAddressables()
        {
            if (!EditorApplication.ExecuteMenuItem("Castle Defender/Remote Content/Sync Registry To Addressables"))
                Debug.LogWarning("[RemoteContentBuildAddressables] Failed to execute registry-to-addressables sync before build.");

            SetupRemoteAudioAddressables.SyncRemoteAudio();
        }

        static void SwitchBuildTarget(BuildTarget target)
        {
            if (EditorUserBuildSettings.activeBuildTarget == target)
                return;

            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(target);
            if (targetGroup == BuildTargetGroup.Unknown)
                throw new InvalidOperationException($"[RemoteContentBuildAddressables] Unsupported build target '{target}'.");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, target))
                throw new InvalidOperationException($"[RemoteContentBuildAddressables] Failed to switch active build target to '{target}'.");
        }

        static string DescribeResult(object result)
        {
            if (result == null) return "No result object returned.";

            var resultType = result.GetType();
            string error = ExtractError(result);
            if (!string.IsNullOrWhiteSpace(error))
                return $"Result error: {error}";

            var duration = resultType.GetProperty("Duration")?.GetValue(result)
                ?? resultType.GetField("Duration")?.GetValue(result);
            var outputPath = resultType.GetProperty("OutputPath")?.GetValue(result) as string
                ?? resultType.GetField("OutputPath")?.GetValue(result) as string;

            return $"Duration={duration ?? "unknown"} OutputPath={outputPath ?? "unknown"}";
        }

        static string ExtractError(object result)
        {
            if (result == null) return null;
            var resultType = result.GetType();
            return resultType.GetProperty("Error")?.GetValue(result) as string
                ?? resultType.GetField("Error")?.GetValue(result) as string;
        }

        static string PublishBuildOutput(object result, BuildTarget target)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string remoteOutputDir = Path.Combine(unityProjectRoot, "ServerData", target.ToString());
            if (Directory.Exists(remoteOutputDir))
                return remoteOutputDir;

            string outputPath = GetOutputPath(result);
            if (string.IsNullOrWhiteSpace(outputPath)) return null;
            string sourceDir = Directory.Exists(outputPath)
                ? outputPath
                : Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                return null;

            string publishedRoot = remoteOutputDir;
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

        static string[] FindCatalogs(BuildTarget target)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string serverData = Path.Combine(unityProjectRoot, "ServerData", target.ToString());
            if (!Directory.Exists(serverData))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(serverData, "*.json", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(serverData, "*.hash", SearchOption.AllDirectories))
                .OrderBy(path => path)
                .Select(path => path.Replace(unityProjectRoot + Path.DirectorySeparatorChar, string.Empty))
                .ToArray();
        }
    }

    public sealed class AddressablesBuildResult
    {
        public AddressablesBuildResult(BuildTarget target, string publishedPath, string summary, string[] catalogs)
        {
            Target = target;
            PublishedPath = publishedPath;
            Summary = summary ?? throw new ArgumentNullException(nameof(summary));
            Catalogs = catalogs ?? Array.Empty<string>();
        }

        public BuildTarget Target { get; }
        public string PublishedPath { get; }
        public string Summary { get; }
        public string[] Catalogs { get; }
    }
}
#endif
