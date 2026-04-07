#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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

        [MenuItem("Castle Defender/Remote Content/Write Current Addressables Size Report")]
        static void WriteCurrentAddressablesSizeReport()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            AddressablesSizeReportSnapshot report = WriteSizeReportForCurrentOutput(target);
            if (report == null)
                throw new InvalidOperationException($"[RemoteContentBuildAddressables] No published Addressables output was found for '{target}'.");

            Debug.Log(
                $"[RemoteContentBuildAddressables] Wrote current size report for {target}. " +
                $"Total={FormatBytes(report.totalBytes)} Bundles={report.bundleCount} " +
                $"Largest={report.largestBundleName ?? "none"} ({FormatBytes(report.largestBundleBytes)}) " +
                $"Report={report.markdownReportPath}");
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
                AddressablesSizeReportSnapshot sizeReport = WriteSizeReport(target, publishedPath);

                Debug.Log($"[RemoteContentBuildAddressables] Build complete for {target}. {summary} PublishedPath={publishedPath ?? "unpublished"}");
                if (catalogs.Length > 0)
                    Debug.Log("[RemoteContentBuildAddressables] Catalogs:\n" + string.Join("\n", catalogs));
                if (sizeReport != null)
                {
                    string totalDeltaText = FormatSignedBytes(sizeReport.totalDeltaBytes);
                    string largestDeltaText = FormatSignedBytes(sizeReport.largestBundleDeltaBytes);
                    Debug.Log(
                        $"[RemoteContentBuildAddressables] Size report for {target}. " +
                        $"Total={FormatBytes(sizeReport.totalBytes)} ({totalDeltaText}) " +
                        $"Bundles={sizeReport.bundleCount} " +
                        $"Largest={sizeReport.largestBundleName ?? "none"} " +
                        $"({FormatBytes(sizeReport.largestBundleBytes)}, {largestDeltaText}) " +
                        $"Report={sizeReport.markdownReportPath}");
                }

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

        static AddressablesSizeReportSnapshot WriteSizeReportForCurrentOutput(BuildTarget target)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string publishedPath = Path.Combine(unityProjectRoot, "ServerData", target.ToString());
            return WriteSizeReport(target, Directory.Exists(publishedPath) ? publishedPath : null);
        }

        static AddressablesSizeReportSnapshot WriteSizeReport(BuildTarget target, string publishedPath)
        {
            if (string.IsNullOrWhiteSpace(publishedPath) || !Directory.Exists(publishedPath))
                return null;

            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(unityProjectRoot, ".."));
            string reportsDirectory = Path.Combine(repoRoot, "projects", "addressables-size-reports", target.ToString());
            Directory.CreateDirectory(reportsDirectory);

            string latestJsonPath = Path.Combine(reportsDirectory, "latest.json");
            AddressablesSizeReportSnapshot previousReport = LoadSizeReport(latestJsonPath);
            AddressablesSizeEntrySnapshot[] currentEntries = CollectPublishedSizeEntries(publishedPath);

            var previousEntriesByLogicalName = new Dictionary<string, AddressablesSizeEntrySnapshot>(StringComparer.OrdinalIgnoreCase);
            if (previousReport?.entries != null)
            {
                foreach (AddressablesSizeEntrySnapshot previousEntry in previousReport.entries)
                {
                    if (previousEntry == null || string.IsNullOrWhiteSpace(previousEntry.logicalName))
                        continue;

                    previousEntriesByLogicalName[previousEntry.logicalName] = previousEntry;
                }
            }

            foreach (AddressablesSizeEntrySnapshot entry in currentEntries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.logicalName))
                    continue;

                entry.previousBytes = previousEntriesByLogicalName.TryGetValue(entry.logicalName, out AddressablesSizeEntrySnapshot previousEntry)
                    ? previousEntry.bytes
                    : 0;
                entry.deltaBytes = entry.bytes - entry.previousBytes;
            }

            long totalBytes = currentEntries.Sum(entry => entry?.bytes ?? 0);
            long bundleBytes = currentEntries.Where(entry => entry != null && entry.isBundle).Sum(entry => entry.bytes);
            long previousTotalBytes = previousReport?.totalBytes ?? 0;
            long previousBundleBytes = previousReport?.bundleBytes ?? 0;

            AddressablesSizeEntrySnapshot[] bundleEntries = currentEntries
                .Where(entry => entry != null && entry.isBundle)
                .OrderByDescending(entry => entry.bytes)
                .ThenBy(entry => entry.logicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AddressablesSizeEntrySnapshot largestBundle = bundleEntries.FirstOrDefault();

            var report = new AddressablesSizeReportSnapshot
            {
                target = target.ToString(),
                generatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
                publishedPath = publishedPath,
                totalBytes = totalBytes,
                totalDeltaBytes = totalBytes - previousTotalBytes,
                bundleBytes = bundleBytes,
                bundleDeltaBytes = bundleBytes - previousBundleBytes,
                fileCount = currentEntries.Length,
                bundleCount = bundleEntries.Length,
                previousGeneratedAtUtc = previousReport?.generatedAtUtc ?? string.Empty,
                largestBundleName = largestBundle?.displayName ?? string.Empty,
                largestBundleBytes = largestBundle?.bytes ?? 0,
                largestBundleDeltaBytes = largestBundle?.deltaBytes ?? 0,
                entries = currentEntries
            };

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            string jsonPath = Path.Combine(reportsDirectory, $"{timestamp}.json");
            string markdownPath = Path.Combine(reportsDirectory, $"{timestamp}.md");
            report.jsonReportPath = jsonPath;
            report.markdownReportPath = markdownPath;

            string json = JsonUtility.ToJson(report, true);
            string markdown = BuildSizeReportMarkdown(report);

            File.WriteAllText(jsonPath, json);
            File.WriteAllText(markdownPath, markdown);
            File.WriteAllText(latestJsonPath, json);
            File.WriteAllText(Path.Combine(reportsDirectory, "latest.md"), markdown);
            return report;
        }

        static AddressablesSizeReportSnapshot LoadSizeReport(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return null;

            string json = File.ReadAllText(jsonPath);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<AddressablesSizeReportSnapshot>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RemoteContentBuildAddressables] Failed to read previous size report '{jsonPath}': {ex.Message}");
                return null;
            }
        }

        static AddressablesSizeEntrySnapshot[] CollectPublishedSizeEntries(string publishedPath)
        {
            return Directory.EnumerateFiles(publishedPath, "*", SearchOption.AllDirectories)
                .Select(filePath =>
                {
                    var info = new FileInfo(filePath);
                    string relativePath = ToRelativePath(publishedPath, filePath);
                    string logicalName = NormalizeLogicalName(relativePath);
                    bool isBundle = string.Equals(Path.GetExtension(filePath), ".bundle", StringComparison.OrdinalIgnoreCase);
                    return new AddressablesSizeEntrySnapshot
                    {
                        relativePath = relativePath,
                        logicalName = logicalName,
                        displayName = BuildDisplayName(logicalName, relativePath),
                        bytes = info.Exists ? info.Length : 0,
                        isBundle = isBundle
                    };
                })
                .OrderByDescending(entry => entry.bytes)
                .ThenBy(entry => entry.logicalName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        static string NormalizeLogicalName(string relativePath)
        {
            string normalized = relativePath.Replace('\\', '/');
            if (!string.Equals(Path.GetExtension(normalized), ".bundle", StringComparison.OrdinalIgnoreCase))
                return normalized;

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(normalized);
            int finalUnderscoreIndex = fileNameWithoutExtension.LastIndexOf('_');
            if (finalUnderscoreIndex <= 0)
                return fileNameWithoutExtension;

            string possibleHash = fileNameWithoutExtension.Substring(finalUnderscoreIndex + 1);
            return LooksLikeBundleHash(possibleHash)
                ? fileNameWithoutExtension.Substring(0, finalUnderscoreIndex)
                : fileNameWithoutExtension;
        }

        static bool LooksLikeBundleHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool isHex = (ch >= '0' && ch <= '9')
                    || (ch >= 'a' && ch <= 'f')
                    || (ch >= 'A' && ch <= 'F');
                if (!isHex)
                    return false;
            }

            return true;
        }

        static string BuildDisplayName(string logicalName, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(logicalName))
                return relativePath;

            if (logicalName.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
                || logicalName.Contains("/")
                || logicalName.StartsWith("catalog", StringComparison.OrdinalIgnoreCase)
                || logicalName.StartsWith("settings", StringComparison.OrdinalIgnoreCase))
            {
                return logicalName;
            }

            Match knownPrefixMatch = Regex.Match(
                logicalName,
                "^(remoteenvironmentshared|remoteenvironmentdressing|remoteenvironment|remoteportraits|remotescenes|remoteskinsshared|remoteskins|remoteunits|remoteaudio)(\\d*)_(assets|scenes)_all$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!knownPrefixMatch.Success)
                return logicalName;

            string prefix = knownPrefixMatch.Groups[1].Value.ToLowerInvariant();
            string numericSuffix = knownPrefixMatch.Groups[2].Value;
            string contentKind = knownPrefixMatch.Groups[3].Value.ToLowerInvariant();

            string label = prefix switch
            {
                "remoteenvironmentshared" => "Remote Environment Shared",
                "remoteenvironmentdressing" => "Remote Environment Dressing",
                "remoteenvironment" => "Remote Environment",
                "remoteportraits" => "Remote Portraits",
                "remotescenes" => "Remote Scenes",
                "remoteskinsshared" => "Remote Skins Shared",
                "remoteskins" => "Remote Skins",
                "remoteunits" => "Remote Units",
                "remoteaudio" => "Remote Audio",
                _ => logicalName
            };

            if (!string.IsNullOrWhiteSpace(numericSuffix))
                label = $"{label} {numericSuffix}";

            if (contentKind == "scenes")
                label += " Scenes";

            return label;
        }

        static string BuildSizeReportMarkdown(AddressablesSizeReportSnapshot report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Addressables Size Report");
            builder.AppendLine();
            builder.AppendLine($"Generated: {report.generatedAtUtc}");
            builder.AppendLine($"Target: `{report.target}`");
            builder.AppendLine($"Published path: `{report.publishedPath}`");
            builder.AppendLine();
            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine($"- Total published size: `{FormatBytes(report.totalBytes)}`");
            builder.AppendLine($"- Total published delta vs previous snapshot: `{FormatSignedBytes(report.totalDeltaBytes)}`");
            builder.AppendLine($"- Bundle-only size: `{FormatBytes(report.bundleBytes)}`");
            builder.AppendLine($"- Bundle-only delta vs previous snapshot: `{FormatSignedBytes(report.bundleDeltaBytes)}`");
            builder.AppendLine($"- Published file count: `{report.fileCount}`");
            builder.AppendLine($"- Bundle count: `{report.bundleCount}`");
            builder.AppendLine($"- Largest bundle: `{report.largestBundleName}` at `{FormatBytes(report.largestBundleBytes)}` ({FormatSignedBytes(report.largestBundleDeltaBytes)})");
            if (!string.IsNullOrWhiteSpace(report.previousGeneratedAtUtc))
                builder.AppendLine($"- Previous snapshot: `{report.previousGeneratedAtUtc}`");

            AppendEntryTable(
                builder,
                "Largest Bundles",
                report.entries
                    .Where(entry => entry != null && entry.isBundle)
                    .OrderByDescending(entry => entry.bytes)
                    .Take(15)
                    .ToArray());

            AppendEntryTable(
                builder,
                "Largest Changes",
                report.entries
                    .Where(entry => entry != null && entry.deltaBytes != 0)
                    .OrderByDescending(entry => Math.Abs(entry.deltaBytes))
                    .Take(20)
                    .ToArray());

            AppendEntryTable(
                builder,
                "All Published Artifacts",
                report.entries
                    .Where(entry => entry != null)
                    .OrderByDescending(entry => entry.bytes)
                    .ToArray());

            return builder.ToString();
        }

        static void AppendEntryTable(StringBuilder builder, string title, AddressablesSizeEntrySnapshot[] entries)
        {
            builder.AppendLine();
            builder.AppendLine($"## {title}");
            builder.AppendLine();

            if (entries == null || entries.Length == 0)
            {
                builder.AppendLine("- No entries.");
                return;
            }

            builder.AppendLine("| Artifact | Current Size | Delta | Previous Size | Source |");
            builder.AppendLine("|---|---:|---:|---:|---|");
            foreach (AddressablesSizeEntrySnapshot entry in entries)
            {
                builder.AppendLine(
                    $"| `{entry.displayName}` | `{FormatBytes(entry.bytes)}` | `{FormatSignedBytes(entry.deltaBytes)}` | `{FormatBytes(entry.previousBytes)}` | `{entry.relativePath}` |");
            }
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

        static string FormatSignedBytes(long bytes)
        {
            if (bytes == 0)
                return "0 B";

            string sign = bytes > 0 ? "+" : "-";
            return sign + FormatBytes(Math.Abs(bytes));
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

    [Serializable]
    public sealed class AddressablesSizeReportSnapshot
    {
        public string target;
        public string generatedAtUtc;
        public string previousGeneratedAtUtc;
        public string publishedPath;
        public long totalBytes;
        public long totalDeltaBytes;
        public long bundleBytes;
        public long bundleDeltaBytes;
        public int fileCount;
        public int bundleCount;
        public string largestBundleName;
        public long largestBundleBytes;
        public long largestBundleDeltaBytes;
        public string jsonReportPath;
        public string markdownReportPath;
        public AddressablesSizeEntrySnapshot[] entries;
    }

    [Serializable]
    public sealed class AddressablesSizeEntrySnapshot
    {
        public string relativePath;
        public string logicalName;
        public string displayName;
        public bool isBundle;
        public long bytes;
        public long previousBytes;
        public long deltaBytes;
    }
}
#endif
