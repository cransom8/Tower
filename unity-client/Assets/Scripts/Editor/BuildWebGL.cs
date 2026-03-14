using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWebGL
{
    const string OutputFolderName = "server/client/Build";
    const string OutputBaseName = "WebGLBuild";
    static readonly string[] FallbackScenes =
    {
        "Assets/Scenes/Bootstrap.unity",
        "Assets/Scenes/Login.unity",
        "Assets/Scenes/Lobby.unity",
        "Assets/Scenes/Loadout.unity",
        "Assets/Scenes/Game_ML.unity",
        "Assets/Scenes/PostGame.unity",
    };

    [MenuItem("Castle Defender/Build/Build WebGL Release")]
    public static void BuildReleaseMenu() => BuildRelease();

    public static void BuildRelease()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
        string outputDirectory = Path.Combine(projectRoot, OutputFolderName);
        string outputPath = Path.Combine(outputDirectory, OutputBaseName);

        Directory.CreateDirectory(outputDirectory);

        var enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 0)
        {
            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
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

        try
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;

            var options = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.CompressWithLz4HC
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException($"WebGL build failed: {report.summary.result}");

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
        }
    }
}
