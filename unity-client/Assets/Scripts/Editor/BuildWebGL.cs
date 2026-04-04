using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWebGL
{
    const string TempBuildFolderName = "WebGLBuild_Auto";
    const string OutputBaseName = "WebGLBuild";
    const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
    const string ServerClientRelativePath = "server/client";

    [MenuItem("Castle Defender/Build/Build WebGL Release")]
    public static void BuildReleaseMenu() => BuildRelease();

    public static void BuildRelease()
    {
        string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string repoRoot = Path.GetFullPath(Path.Combine(unityProjectRoot, ".."));
        string outputDirectory = ResolveServerClientPath(repoRoot);
        string tempBuildDirectory = Path.Combine(unityProjectRoot, TempBuildFolderName);

        if (Directory.Exists(tempBuildDirectory))
            Directory.Delete(tempBuildDirectory, true);
        Directory.CreateDirectory(tempBuildDirectory);

        string bootstrapSceneAbsolutePath = Path.Combine(unityProjectRoot, BootstrapScenePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(bootstrapSceneAbsolutePath))
            throw new BuildFailedException($"Bootstrap scene not found at '{BootstrapScenePath}'.");

        var buildScenes = new[] { BootstrapScenePath };

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
                scenes = buildScenes,
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

            if (Directory.Exists(tempBuildDirectory))
                Directory.Delete(tempBuildDirectory, true);
        }
    }

    static string ResolveServerClientPath(string repoRoot)
    {
        string resolvedRepoRoot = Path.GetFullPath(repoRoot);
        string outputDirectory = Path.GetFullPath(Path.Combine(resolvedRepoRoot, ServerClientRelativePath));

        if (!IsSubPathOf(outputDirectory, resolvedRepoRoot))
            throw new BuildFailedException($"Refusing to write WebGL build outside repo root. Repo: '{resolvedRepoRoot}', Output: '{outputDirectory}'.");

        string parentDirectory = Path.GetDirectoryName(outputDirectory);
        if (string.IsNullOrEmpty(parentDirectory) || !Directory.Exists(parentDirectory))
            throw new BuildFailedException($"Expected server client parent directory not found at '{parentDirectory}'.");

        return outputDirectory;
    }

    static bool IsSubPathOf(string candidatePath, string rootPath)
    {
        string normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
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
}
