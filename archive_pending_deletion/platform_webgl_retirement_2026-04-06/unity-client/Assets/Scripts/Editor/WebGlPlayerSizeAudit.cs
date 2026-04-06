#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class WebGlPlayerSizeAudit
    {
        const string ReportPath = "projects/WebGL Player Size Audit.md";
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";

        static readonly string[] FallbackScenes =
        {
            "Assets/Scenes/Bootstrap.unity",
            "Assets/Scenes/Login.unity",
            "Assets/Scenes/Loading.unity",
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/Loadout.unity",
            "Assets/Scenes/Game_ML.unity",
            "Assets/Scenes/PostGame.unity",
        };

        static readonly string[] SuspiciousRemotePrefixes =
        {
            "Assets/Prefabs/Units/",
            "Assets/AddressableContent/Environment/",
            "Assets/AddressableContent/Portraits/",
        };

        [MenuItem("Castle Defender/Build/Audit WebGL Player Dependencies")]
        public static void Audit()
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));
            string reportAbsolutePath = Path.Combine(repositoryRoot, ReportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(reportAbsolutePath) ?? repositoryRoot);

            var scenePaths = ResolveBuildScenes(unityProjectRoot);
            var sceneDependencyPaths = AssetDatabase.GetDependencies(scenePaths, true)
                .Where(IsRealAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var registryDependencyPaths = ResolveRegistryDependencyPaths();
            var strippedSceneDependencyPaths = sceneDependencyPaths
                .Except(registryDependencyPaths, StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var resourceAssetPaths = FindResourceAssetPaths()
                .Where(IsRealAssetPath)
                .Except(strippedSceneDependencyPaths, StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var rawSceneAssets = sceneDependencyPaths
                .Select(path => BuildRecord(path, "Scene dependency (raw)"))
                .Where(record => record != null)
                .ToArray();

            var sceneAssets = strippedSceneDependencyPaths
                .Select(path => BuildRecord(path, "Scene dependency"))
                .Where(record => record != null)
                .ToArray();

            var resourceAssets = resourceAssetPaths
                .Select(path => BuildRecord(path, "Resources"))
                .Where(record => record != null)
                .ToArray();

            long buildBytes = GetBuildOutputBytes(repositoryRoot, out string buildOutputPath);
            long localStreamingAssetsBytes = GetLocalStreamingAddressablesBytes(repositoryRoot);

            string report = BuildReport(
                scenePaths,
                rawSceneAssets,
                sceneAssets,
                resourceAssets,
                buildBytes,
                buildOutputPath,
                localStreamingAssetsBytes);

            File.WriteAllText(reportAbsolutePath, report, Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[WebGlPlayerSizeAudit] Wrote report to {reportAbsolutePath}");
        }

        static string[] ResolveBuildScenes(string projectRoot)
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(Path.Combine(projectRoot, scene.path.Replace('/', Path.DirectorySeparatorChar))))
                .Select(scene => scene.path)
                .ToArray();

            if (enabledScenes.Length > 0)
                return enabledScenes;

            return FallbackScenes
                .Where(scenePath => File.Exists(Path.Combine(projectRoot, scenePath.Replace('/', Path.DirectorySeparatorChar))))
                .ToArray();
        }

        static IEnumerable<string> FindResourceAssetPaths()
        {
            foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsRealAssetPath(path))
                    continue;

                if (path.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) < 0
                    && !path.EndsWith("/Resources", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                yield return path;
            }
        }

        static AssetRecord BuildRecord(string assetPath, string source)
        {
            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!File.Exists(absolutePath))
                return null;

            long bytes = new FileInfo(absolutePath).Length;
            return new AssetRecord
            {
                AssetPath = assetPath,
                Source = source,
                Bytes = bytes,
                Extension = Path.GetExtension(assetPath) ?? string.Empty,
                TopFolder = GetTopFolder(assetPath),
                IsSuspiciousRemoteAsset = SuspiciousRemotePrefixes.Any(prefix =>
                    assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
            };
        }

        static long GetBuildOutputBytes(string projectRoot, out string buildOutputPath)
        {
            string[] candidates =
            {
                Path.Combine(projectRoot, "server", "client"),
                Path.Combine(projectRoot, "unity-client", "WebGLBuild_Auto"),
            };

            foreach (string candidate in candidates)
            {
                if (!Directory.Exists(candidate))
                    continue;

                buildOutputPath = candidate;
                return Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length);
            }

            buildOutputPath = "(not found)";
            return 0;
        }

        static long GetLocalStreamingAddressablesBytes(string projectRoot)
        {
            string path = Path.Combine(projectRoot, "server", "client", "StreamingAssets", "aa");
            if (!Directory.Exists(path))
                return 0;

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
        }

        static string BuildReport(
            IReadOnlyList<string> scenePaths,
            IReadOnlyList<AssetRecord> rawSceneAssets,
            IReadOnlyList<AssetRecord> sceneAssets,
            IReadOnlyList<AssetRecord> resourceAssets,
            long buildBytes,
            string buildOutputPath,
            long localStreamingAssetsBytes)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# WebGL Player Size Audit");
            builder.AppendLine();
            builder.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine();
            builder.AppendLine("## Build Output");
            builder.AppendLine();
            builder.AppendLine($"- Build output path: `{buildOutputPath}`");
            builder.AppendLine($"- Current build output size: `{FormatMegabytes(buildBytes)}`");
            builder.AppendLine($"- Local `StreamingAssets/aa` size inside player: `{FormatMegabytes(localStreamingAssetsBytes)}`");
            builder.AppendLine();
            builder.AppendLine("## Build Scenes");
            builder.AppendLine();
            foreach (string scenePath in scenePaths)
                builder.AppendLine($"- `{scenePath}`");

            builder.AppendLine();
            builder.AppendLine("## Dependency Totals");
            builder.AppendLine();
            builder.AppendLine($"- Raw scene dependency source assets: `{FormatMegabytes(rawSceneAssets.Sum(asset => asset.Bytes))}` across `{rawSceneAssets.Count}` files");
            builder.AppendLine($"- Scene dependency source assets after registry-strip approximation: `{FormatMegabytes(sceneAssets.Sum(asset => asset.Bytes))}` across `{sceneAssets.Count}` files");
            builder.AppendLine($"- Resources source assets: `{FormatMegabytes(resourceAssets.Sum(asset => asset.Bytes))}` across `{resourceAssets.Count}` files");

            AppendGroupSummary(builder, "Top Scene Dependency Folders", sceneAssets);
            AppendGroupSummary(builder, "Top Resources Folders", resourceAssets);
            AppendTopAssets(builder, "Largest Scene Dependency Assets", sceneAssets);
            AppendTopAssets(builder, "Largest Resources Assets", resourceAssets);
            AppendSuspiciousAssets(builder, sceneAssets, resourceAssets);

            builder.AppendLine();
            builder.AppendLine("## Notes");
            builder.AppendLine();
            builder.AppendLine("- This audit ranks source asset file sizes, which is directional rather than a perfect one-to-one build-size measurement.");
            builder.AppendLine("- Scene dependency totals are reported both raw and with `UnitPrefabRegistry` dependencies removed, because WebGL builds strip that registry before build.");
            builder.AppendLine("- The goal is to identify what is still being pulled into the player build, especially assets that should now be remote-only or deferred.");

            return builder.ToString();
        }

        static void AppendGroupSummary(StringBuilder builder, string title, IReadOnlyList<AssetRecord> assets)
        {
            builder.AppendLine();
            builder.AppendLine($"## {title}");
            builder.AppendLine();

            foreach (var group in assets
                .GroupBy(asset => asset.TopFolder, StringComparer.OrdinalIgnoreCase)
                .Select(group => new { Folder = group.Key, Bytes = group.Sum(asset => asset.Bytes), Count = group.Count() })
                .OrderByDescending(group => group.Bytes)
                .Take(12))
            {
                builder.AppendLine($"- `{group.Folder}`: `{FormatMegabytes(group.Bytes)}` across `{group.Count}` files");
            }
        }

        static void AppendTopAssets(StringBuilder builder, string title, IReadOnlyList<AssetRecord> assets)
        {
            builder.AppendLine();
            builder.AppendLine($"## {title}");
            builder.AppendLine();

            foreach (var asset in assets.OrderByDescending(asset => asset.Bytes).Take(25))
                builder.AppendLine($"- `{asset.AssetPath}`: `{FormatMegabytes(asset.Bytes)}`");
        }

        static void AppendSuspiciousAssets(StringBuilder builder, IReadOnlyList<AssetRecord> sceneAssets, IReadOnlyList<AssetRecord> resourceAssets)
        {
            builder.AppendLine();
            builder.AppendLine("## Remote-Only Leak Candidates");
            builder.AppendLine();

            var suspicious = sceneAssets
                .Concat(resourceAssets)
                .Where(asset => asset.IsSuspiciousRemoteAsset)
                .OrderByDescending(asset => asset.Bytes)
                .ToArray();

            if (suspicious.Length == 0)
            {
                builder.AppendLine("- No scene/resource dependency paths currently match the known remote-only folders.");
                return;
            }

            foreach (var asset in suspicious)
                builder.AppendLine($"- `{asset.AssetPath}` via `{asset.Source}`: `{FormatMegabytes(asset.Bytes)}`");
        }

        static bool IsRealAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return false;

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return false;

            string extension = Path.GetExtension(assetPath);
            if (string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".js", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".asmdef", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".meta", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        static string GetTopFolder(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            string[] parts = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return normalized;

            return $"{parts[0]}/{parts[1]}";
        }

        static string FormatMegabytes(long bytes)
        {
            return $"{bytes / (1024f * 1024f):0.00} MB";
        }

        static string[] ResolveRegistryDependencyPaths()
        {
            string registryPath = null;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(RegistryPath) != null)
                registryPath = RegistryPath;
            else if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(LegacyRegistryPath) != null)
                registryPath = LegacyRegistryPath;

            if (string.IsNullOrEmpty(registryPath))
                return Array.Empty<string>();

            return AssetDatabase.GetDependencies(registryPath, true)
                .Where(IsRealAssetPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        sealed class AssetRecord
        {
            public string AssetPath;
            public string Source;
            public long Bytes;
            public string Extension;
            public string TopFolder;
            public bool IsSuspiciousRemoteAsset;
        }
    }
}
#endif
