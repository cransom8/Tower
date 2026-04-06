#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteContentBuildAddressables
    {
        static readonly Regex BundleNameRegex = new Regex("\"Name\":\"([^\"]+\\.bundle)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        static readonly Regex BuildStartTimeRegex = new Regex("\"BuildStartTime\":\"([^\"]+)\"", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                SyncManagedRemoteContent();
                DateTime buildStartedAtUtc = DateTime.UtcNow;

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

                string error = ExtractError(result);
                if (!string.IsNullOrWhiteSpace(error))
                    throw new InvalidOperationException($"[RemoteContentBuildAddressables] Addressables build failed: {error}");

                string summary = DescribeResult(result);
                string publishedPath = PublishBuildOutput(result, target, buildStartedAtUtc);
                string[] catalogs = FindCatalogs(target);

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

        static void SyncManagedRemoteContent()
        {
            SetupRemoteSceneAddressables.SyncRemoteScenes();
            SetupPortraitAddressables.MovePortraitsToAddressables();
            BuildWinterEnvironmentAddressables.SyncManagedAddressables();
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

        static string PublishBuildOutput(object result, BuildTarget target, DateTime buildStartedAtUtc)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string normalizedPublishedRoot = Path.GetFullPath(Path.Combine(unityProjectRoot, "ServerData", target.ToString()));
            if (!Directory.Exists(normalizedPublishedRoot))
            {
                string outputPath = GetOutputPath(result);
                if (!string.IsNullOrWhiteSpace(outputPath))
                    Debug.LogWarning($"[RemoteContentBuildAddressables] Expected remote publish directory '{normalizedPublishedRoot}' was not created. Addressables reported OutputPath='{outputPath}'.");

                return null;
            }

            PruneOrphanedPublishedFiles(normalizedPublishedRoot, target, buildStartedAtUtc);
            return normalizedPublishedRoot;
        }

        static string GetOutputPath(object result)
        {
            if (result == null) return null;
            var resultType = result.GetType();
            return resultType.GetProperty("OutputPath")?.GetValue(result) as string
                ?? resultType.GetField("OutputPath")?.GetValue(result) as string;
        }

        static void PruneOrphanedPublishedFiles(string publishedRoot, BuildTarget target, DateTime buildStartedAtUtc)
        {
            if (!Directory.Exists(publishedRoot))
                return;

            HashSet<string> retainedPaths = CollectRetainedPublishedArtifacts(publishedRoot, target, buildStartedAtUtc);
            if (retainedPaths == null || retainedPaths.Count == 0)
            {
                Debug.LogWarning($"[RemoteContentBuildAddressables] Skipped pruning {target} publish output because the active bundle list could not be resolved.");
                return;
            }

            string[] publishedFiles = Directory.EnumerateFiles(publishedRoot, "*", SearchOption.AllDirectories).ToArray();
            int prunedCount = 0;
            long reclaimedBytes = 0;

            foreach (string filePath in publishedFiles)
            {
                string relativePath = ToRelativePath(publishedRoot, filePath);
                if (retainedPaths.Contains(relativePath))
                    continue;

                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Exists)
                    reclaimedBytes += fileInfo.Length;

                File.Delete(filePath);
                prunedCount++;
            }

            RemoveEmptyDirectories(publishedRoot);

            if (prunedCount > 0)
                Debug.Log($"[RemoteContentBuildAddressables] Pruned {prunedCount} orphaned published files for {target}, reclaiming {FormatBytes(reclaimedBytes)}.");
        }

        static HashSet<string> CollectRetainedPublishedArtifacts(string publishedRoot, BuildTarget target, DateTime buildStartedAtUtc)
        {
            var retainedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string catalogPath in Directory.EnumerateFiles(publishedRoot, "catalog_*", SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(catalogPath);
                if (string.Equals(extension, ".bin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".hash", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase))
                {
                    retainedPaths.Add(Path.GetFileName(catalogPath));
                }
            }

            bool layoutMatchesCurrentBuild;
            string[] bundleNames = LoadPublishedBundleNames(target, buildStartedAtUtc, out layoutMatchesCurrentBuild);
            if (!layoutMatchesCurrentBuild)
            {
                DateTime buildWindowStartUtc = buildStartedAtUtc.AddSeconds(-5);
                foreach (string filePath in Directory.EnumerateFiles(publishedRoot, "*", SearchOption.AllDirectories))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists && fileInfo.LastWriteTimeUtc >= buildWindowStartUtc)
                        retainedPaths.Add(ToRelativePath(publishedRoot, filePath));
                }
            }

            if (bundleNames.Length == 0 && retainedPaths.Count == 0)
                return null;

            foreach (string bundleName in bundleNames)
                retainedPaths.Add(bundleName);

            return retainedPaths;
        }

        static string[] LoadPublishedBundleNames(BuildTarget target, DateTime buildStartedAtUtc, out bool layoutMatchesCurrentBuild)
        {
            layoutMatchesCurrentBuild = false;
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string buildLayoutPath = Path.Combine(unityProjectRoot, "Library", "com.unity.addressables", "buildlayout.json");
            if (!File.Exists(buildLayoutPath))
                return Array.Empty<string>();

            string buildLayoutContents = File.ReadAllText(buildLayoutPath);
            string expectedRemotePathForwardSlash = $"\"RemoteCatalogBuildPath\":\"ServerData/{target}\"";
            string expectedRemotePathBackslash = $"\"RemoteCatalogBuildPath\":\"ServerData\\\\{target}\"";
            if (buildLayoutContents.IndexOf(expectedRemotePathForwardSlash, StringComparison.OrdinalIgnoreCase) < 0
                && buildLayoutContents.IndexOf(expectedRemotePathBackslash, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return Array.Empty<string>();
            }

            Match buildStartMatch = BuildStartTimeRegex.Match(buildLayoutContents);
            if (buildStartMatch.Success && DateTime.TryParse(buildStartMatch.Groups[1].Value, out DateTime buildStartTime))
            {
                DateTime buildLayoutStartUtc = buildStartTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(buildStartTime, DateTimeKind.Local).ToUniversalTime()
                    : buildStartTime.ToUniversalTime();
                layoutMatchesCurrentBuild = buildLayoutStartUtc >= buildStartedAtUtc.AddSeconds(-10);
            }

            return BundleNameRegex.Matches(buildLayoutContents)
                .Cast<Match>()
                .Select(match => match.Groups[1].Value)
                .Where(bundleName => !string.IsNullOrWhiteSpace(bundleName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static string ToRelativePath(string rootPath, string fullPath)
        {
            string relativePath = Path.GetRelativePath(rootPath, fullPath);
            return relativePath.Replace(Path.DirectorySeparatorChar, '/');
        }

        static void RemoveEmptyDirectories(string rootPath)
        {
            foreach (string childDir in Directory.GetDirectories(rootPath))
            {
                RemoveEmptyDirectories(childDir);
                if (!Directory.EnumerateFileSystemEntries(childDir).Any())
                    Directory.Delete(childDir);
            }
        }

        static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = Math.Max(bytes, 0);
            int unitIndex = 0;
            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return unitIndex == 0 ? $"{size:0} {units[unitIndex]}" : $"{size:0.##} {units[unitIndex]}";
        }

        static string[] FindCatalogs(BuildTarget target)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string serverData = Path.Combine(unityProjectRoot, "ServerData", target.ToString());
            if (!Directory.Exists(serverData))
                return Array.Empty<string>();

            return Directory.EnumerateFiles(serverData, "*.bin", SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(serverData, "*.json", SearchOption.AllDirectories))
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
