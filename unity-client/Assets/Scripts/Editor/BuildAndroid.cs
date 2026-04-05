using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildAndroid
{
    const string DefaultOutputRelativePath = "../builds/android/forge-wars.aab";
    const string DefaultSecretsRelativePath = "../.local-secrets/forge-wars-upload.env";

    [MenuItem("Castle Defender/Build/Build Android App Bundle")]
    public static void BuildReleaseMenu() => BuildRelease();

    public static AndroidBuildResult BuildRelease()
    {
        string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = ResolveOutputPath(unityProjectRoot);
        string outputDirectory = Path.GetDirectoryName(outputPath);
        var localSettings = LoadLocalSettings(unityProjectRoot);

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Failed to resolve Android build output directory.");

        Directory.CreateDirectory(outputDirectory);

        string keystorePath = RequireSetting("ANDROID_KEYSTORE_PATH", localSettings);
        string keystorePassword = RequireSetting("ANDROID_KEYSTORE_PASS", localSettings);
        string keyaliasName = RequireSetting("ANDROID_KEYALIAS_NAME", localSettings);
        string keyaliasPassword = RequireSetting("ANDROID_KEYALIAS_PASS", localSettings);

        if (!File.Exists(keystorePath))
            throw new FileNotFoundException($"Android keystore not found at '{keystorePath}'.");

        var enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (enabledScenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes found in EditorBuildSettings.");

        BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
        bool previousBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
        bool previousExportProject = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        bool previousDevelopment = EditorUserBuildSettings.development;
        bool previousConnectProfiler = EditorUserBuildSettings.connectProfiler;
        bool previousAllowDebugging = EditorUserBuildSettings.allowDebugging;

        bool previousUseCustomKeystore = PlayerSettings.Android.useCustomKeystore;
        string previousKeystoreName = PlayerSettings.Android.keystoreName;
        string previousKeystorePass = PlayerSettings.Android.keystorePass;
        string previousKeyaliasName = PlayerSettings.Android.keyaliasName;
        string previousKeyaliasPass = PlayerSettings.Android.keyaliasPass;

        string previousBundleVersion = PlayerSettings.bundleVersion;
        int previousVersionCode = PlayerSettings.Android.bundleVersionCode;

        try
        {
            if (previousTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            EditorUserBuildSettings.buildAppBundle = true;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.connectProfiler = false;
            EditorUserBuildSettings.allowDebugging = false;

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            PlayerSettings.Android.keystorePass = keystorePassword;
            PlayerSettings.Android.keyaliasName = keyaliasName;
            PlayerSettings.Android.keyaliasPass = keyaliasPassword;

            string bundleVersion = ReadSetting("ANDROID_BUNDLE_VERSION", localSettings);
            if (!string.IsNullOrWhiteSpace(bundleVersion))
                PlayerSettings.bundleVersion = bundleVersion.Trim();

            string versionCodeValue = ReadSetting("ANDROID_BUNDLE_VERSION_CODE", localSettings);
            if (!string.IsNullOrWhiteSpace(versionCodeValue))
            {
                if (!int.TryParse(versionCodeValue, out int parsedVersionCode) || parsedVersionCode <= 0)
                    throw new InvalidOperationException("ANDROID_BUNDLE_VERSION_CODE must be a positive integer.");

                PlayerSettings.Android.bundleVersionCode = parsedVersionCode;
            }

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");

            string archivedOutputPath = ArchiveBuildOutput(outputPath, PlayerSettings.bundleVersion, PlayerSettings.Android.bundleVersionCode);
            Debug.Log($"[BuildAndroid] Success. Output: {outputPath}");
            Debug.Log($"[BuildAndroid] Archived release: {archivedOutputPath}");
            Debug.Log($"[BuildAndroid] Version: {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
            return new AndroidBuildResult(outputPath, archivedOutputPath, PlayerSettings.bundleVersion, PlayerSettings.Android.bundleVersionCode);
        }
        finally
        {
            PlayerSettings.bundleVersion = previousBundleVersion;
            PlayerSettings.Android.bundleVersionCode = previousVersionCode;

            PlayerSettings.Android.useCustomKeystore = previousUseCustomKeystore;
            PlayerSettings.Android.keystoreName = previousKeystoreName;
            PlayerSettings.Android.keystorePass = previousKeystorePass;
            PlayerSettings.Android.keyaliasName = previousKeyaliasName;
            PlayerSettings.Android.keyaliasPass = previousKeyaliasPass;

            EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExportProject;
            EditorUserBuildSettings.development = previousDevelopment;
            EditorUserBuildSettings.connectProfiler = previousConnectProfiler;
            EditorUserBuildSettings.allowDebugging = previousAllowDebugging;
        }
    }

    static string ArchiveBuildOutput(string outputPath, string bundleVersion, int versionCode)
    {
        if (!File.Exists(outputPath))
            throw new FileNotFoundException($"Android build output not found at '{outputPath}'.");

        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Failed to resolve Android build archive directory.");

        string releasesDirectory = Path.Combine(outputDirectory, "releases");
        Directory.CreateDirectory(releasesDirectory);

        string baseName = Path.GetFileNameWithoutExtension(outputPath);
        string safeBundleVersion = SanitizeFileNameSegment(string.IsNullOrWhiteSpace(bundleVersion) ? "unknown" : bundleVersion.Trim());
        string archivedOutputPath = Path.Combine(releasesDirectory, $"{baseName}-v{safeBundleVersion}-code{versionCode}.aab");
        File.Copy(outputPath, archivedOutputPath, true);
        return archivedOutputPath;
    }

    static string SanitizeFileNameSegment(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
    }

    static string ResolveOutputPath(string projectRoot)
    {
        var localSettings = LoadLocalSettings(projectRoot);
        string configuredOutput = ReadSetting("ANDROID_BUILD_OUTPUT", localSettings);
        string pathToResolve = string.IsNullOrWhiteSpace(configuredOutput)
            ? DefaultOutputRelativePath
            : configuredOutput.Trim();

        return Path.IsPathRooted(pathToResolve)
            ? pathToResolve
            : Path.GetFullPath(Path.Combine(projectRoot, pathToResolve));
    }

    static string RequireSetting(string name, IReadOnlyDictionary<string, string> localSettings)
    {
        string value = ReadSetting(name, localSettings);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Missing required setting '{name}'.");

        return value.Trim();
    }

    static string ReadSetting(string name, IReadOnlyDictionary<string, string> localSettings)
    {
        string value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();

        if (localSettings.TryGetValue(name, out string localValue))
            return localValue.Trim();

        return null;
    }

    static Dictionary<string, string> LoadLocalSettings(string projectRoot)
    {
        string envFilePath = Path.GetFullPath(Path.Combine(projectRoot, DefaultSecretsRelativePath));
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(envFilePath))
            return settings;

        foreach (string rawLine in File.ReadAllLines(envFilePath))
        {
            if (string.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith("#"))
                continue;

            int separatorIndex = rawLine.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            string key = rawLine.Substring(0, separatorIndex).Trim();
            string value = rawLine.Substring(separatorIndex + 1).Trim();
            settings[key] = value;
        }

        return settings;
    }
}

public sealed class AndroidBuildResult
{
    public AndroidBuildResult(string outputPath, string archivedOutputPath, string bundleVersion, int versionCode)
    {
        OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        ArchivedOutputPath = archivedOutputPath ?? throw new ArgumentNullException(nameof(archivedOutputPath));
        BundleVersion = bundleVersion ?? throw new ArgumentNullException(nameof(bundleVersion));
        VersionCode = versionCode;
    }

    public string OutputPath { get; }
    public string ArchivedOutputPath { get; }
    public string BundleVersion { get; }
    public int VersionCode { get; }
}
