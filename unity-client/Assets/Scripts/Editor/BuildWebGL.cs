using System.IO;
using System.Linq;
using CastleDefender.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWebGL
{
    const string TempBuildFolderName = "WebGLBuild_Auto";
    const string OutputBaseName = "WebGLBuild";
    const string RegistryAssetPath = "Assets/Registry/UnitPrefabRegistry.asset";
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

    [MenuItem("Castle Defender/Build/Build WebGL Release")]
    public static void BuildReleaseMenu() => BuildRelease();

    public static void BuildRelease()
    {
        string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputDirectory = ResolveServerClientPath();
        string tempBuildDirectory = Path.Combine(unityProjectRoot, TempBuildFolderName);
        string registryAbsolutePath = Path.Combine(unityProjectRoot, RegistryAssetPath.Replace('/', Path.DirectorySeparatorChar));

        if (Directory.Exists(tempBuildDirectory))
            Directory.Delete(tempBuildDirectory, true);
        Directory.CreateDirectory(tempBuildDirectory);

        var enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 0)
        {
            enabledScenes = FallbackScenes
                .Where(scenePath => File.Exists(Path.Combine(unityProjectRoot, scenePath.Replace('/', Path.DirectorySeparatorChar))))
                .ToArray();
        }

        if (enabledScenes.Length == 0)
            throw new BuildFailedException("No enabled scenes found in Build Settings, and no fallback scenes were found under Assets/Scenes.");

        var previousCompression = PlayerSettings.WebGL.compressionFormat;
        bool previousDecompressionFallback = PlayerSettings.WebGL.decompressionFallback;
        var previousDevelopment = EditorUserBuildSettings.development;
        var previousConnectProfiler = EditorUserBuildSettings.connectProfiler;
        var previousAllowDebugging = EditorUserBuildSettings.allowDebugging;
        string registryBackupContents = null;
        bool strippedRegistryForBuild = false;

        try
        {
            if (File.Exists(registryAbsolutePath))
            {
                registryBackupContents = File.ReadAllText(registryAbsolutePath);
                strippedRegistryForBuild = RemoteContentStripRegistryReferences.StripRegistryReferences(saveAssets: true);
                if (!strippedRegistryForBuild)
                    throw new BuildFailedException("Failed to strip local prefab references from UnitPrefabRegistry before WebGL build.");
            }

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;

            var options = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = tempBuildDirectory,
                target = BuildTarget.WebGL,
                options = BuildOptions.CompressWithLz4HC
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"WebGL build failed: {report.summary.result}");

            CopyDirectory(tempBuildDirectory, outputDirectory);
            RemoveNestedBuildWrapper(outputDirectory);

            long totalBytes = Directory.Exists(outputDirectory)
                ? Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length)
                : 0;

            Debug.Log($"[BuildWebGL] Success. Output: {outputDirectory}");
            Debug.Log($"[BuildWebGL] Total size: {totalBytes} bytes ({totalBytes / (1024f * 1024f):0.00} MB)");
        }
        finally
        {
            PlayerSettings.WebGL.compressionFormat = previousCompression;
            PlayerSettings.WebGL.decompressionFallback = previousDecompressionFallback;
            EditorUserBuildSettings.development = previousDevelopment;
            EditorUserBuildSettings.connectProfiler = previousConnectProfiler;
            EditorUserBuildSettings.allowDebugging = previousAllowDebugging;

            if (strippedRegistryForBuild && registryBackupContents != null)
                RestoreRegistryAsset(registryAbsolutePath, registryBackupContents);

            if (Directory.Exists(tempBuildDirectory))
                Directory.Delete(tempBuildDirectory, true);
        }
    }

    static string ResolveServerClientPath()
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../../server/client")),
            Path.GetFullPath(Path.Combine(Application.dataPath, "../../server/client")),
        };

        foreach (string candidate in candidates)
        {
            string parent = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                return candidate;
        }

        return candidates[0];
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

    static void RemoveNestedBuildWrapper(string serverClientDir)
    {
        string nestedWrapperDir = Path.Combine(serverClientDir, "Build", OutputBaseName);
        if (Directory.Exists(nestedWrapperDir))
            Directory.Delete(nestedWrapperDir, true);
    }

    static void RestoreRegistryAsset(string registryAbsolutePath, string contents)
    {
        File.WriteAllText(registryAbsolutePath, contents);
        AssetDatabase.ImportAsset(RegistryAssetPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh();
    }
}
