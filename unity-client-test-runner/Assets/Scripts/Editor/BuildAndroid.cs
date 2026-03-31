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

    public static void BuildRelease()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = ResolveOutputPath(projectRoot);
        string outputDirectory = Path.GetDirectoryName(outputPath);
        var localSettings = LoadLocalSettings(projectRoot);

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

            Debug.Log($"[BuildAndroid] Success. Output: {outputPath}");
            Debug.Log($"[BuildAndroid] Version: {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
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
