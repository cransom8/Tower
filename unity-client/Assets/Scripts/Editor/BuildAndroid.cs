using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildAndroid
{
    const string DefaultBundleOutputRelativePath = "../builds/android/forge-wars.aab";
    const string DefaultLocalApkOutputRelativePath = "../builds/android/forge-wars-local.apk";
    const string DefaultSecretsRelativePath = "../.local-secrets/forge-wars-upload.env";
    const string BundleVersionCodeSettingName = "ANDROID_BUNDLE_VERSION_CODE";
    const string AutoIncrementReleaseVersionCodeSettingName = "ANDROID_AUTO_INCREMENT_VERSION_CODE";

    [MenuItem("Castle Defender/Build/Build Android App Bundle")]
    public static void BuildReleaseMenu() => BuildRelease();

    [MenuItem("Castle Defender/Build/Build Local Android APK")]
    public static void BuildLocalApkMenu() => BuildLocalApk();

    [MenuItem("Castle Defender/Build/Build Local Android APK (Skip Addressables)")]
    public static void BuildLocalApkWithoutRemoteContentMenu() => BuildLocalApk(buildRemoteContent: false);

    public static AndroidBuildPreview PreviewReleaseBuild()
    {
        return PreviewConfiguredPackage(
            buildAppBundle: true,
            developmentBuild: false,
            outputSettingNames: new[] { "ANDROID_BUILD_OUTPUT" },
            defaultOutputRelativePath: DefaultBundleOutputRelativePath);
    }

    public static AndroidBuildPreview PreviewLocalApkBuild()
    {
        return PreviewConfiguredPackage(
            buildAppBundle: false,
            developmentBuild: true,
            outputSettingNames: new[] { "ANDROID_LOCAL_BUILD_OUTPUT", "ANDROID_APK_OUTPUT" },
            defaultOutputRelativePath: DefaultLocalApkOutputRelativePath);
    }

    public static AndroidBuildResult BuildRelease(bool buildRemoteContent = true)
    {
        return BuildConfiguredPackage(
            buildRemoteContent: buildRemoteContent,
            buildAppBundle: true,
            developmentBuild: false,
            allowDebugging: false,
            connectProfiler: false,
            outputSettingNames: new[] { "ANDROID_BUILD_OUTPUT" },
            defaultOutputRelativePath: DefaultBundleOutputRelativePath,
            requireCustomKeystore: true);
    }

    public static AndroidBuildResult BuildLocalApk(bool buildRemoteContent = true)
    {
        return BuildConfiguredPackage(
            buildRemoteContent: buildRemoteContent,
            buildAppBundle: false,
            developmentBuild: true,
            allowDebugging: true,
            connectProfiler: false,
            outputSettingNames: new[] { "ANDROID_LOCAL_BUILD_OUTPUT", "ANDROID_APK_OUTPUT" },
            defaultOutputRelativePath: DefaultLocalApkOutputRelativePath,
            requireCustomKeystore: false);
    }

    static AndroidBuildPreview PreviewConfiguredPackage(
        bool buildAppBundle,
        bool developmentBuild,
        IReadOnlyList<string> outputSettingNames,
        string defaultOutputRelativePath)
    {
        string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var localSettings = LoadLocalSettings(unityProjectRoot);
        string outputPath = ResolveOutputPath(unityProjectRoot, localSettings, outputSettingNames, defaultOutputRelativePath);

        string bundleVersion = ReadSetting("ANDROID_BUNDLE_VERSION", localSettings);
        if (string.IsNullOrWhiteSpace(bundleVersion))
            bundleVersion = PlayerSettings.bundleVersion;

        int versionCode = ResolveVersionCode(
            outputPath,
            buildAppBundle,
            developmentBuild,
            PlayerSettings.Android.bundleVersionCode,
            localSettings);

        string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        return new AndroidBuildPreview(
            outputPath,
            string.IsNullOrWhiteSpace(bundleVersion) ? "unset" : bundleVersion.Trim(),
            versionCode,
            string.IsNullOrWhiteSpace(applicationIdentifier) ? "not configured" : applicationIdentifier,
            buildAppBundle);
    }

    static AndroidBuildResult BuildConfiguredPackage(
        bool buildRemoteContent,
        bool buildAppBundle,
        bool developmentBuild,
        bool allowDebugging,
        bool connectProfiler,
        IReadOnlyList<string> outputSettingNames,
        string defaultOutputRelativePath,
        bool requireCustomKeystore)
    {
        string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        var localSettings = LoadLocalSettings(unityProjectRoot);
        string outputPath = ResolveOutputPath(unityProjectRoot, localSettings, outputSettingNames, defaultOutputRelativePath);
        string outputDirectory = Path.GetDirectoryName(outputPath);

        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("Failed to resolve Android build output directory.");

        Directory.CreateDirectory(outputDirectory);

        ValidateOutputPath(outputPath, buildAppBundle ? ".aab" : ".apk");

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
        string previousApplicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);

        try
        {
            if (previousTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            RansomForgeBranding.ApplyAndroidBrandingOrThrow();

            if (buildRemoteContent)
            {
                CastleDefender.Editor.AddressablesBuildResult addressablesBuild =
                    CastleDefender.Editor.RemoteContentBuildAddressables.BuildForTarget(BuildTarget.Android, restorePreviousTarget: false);
                Debug.Log(
                    $"[BuildAndroid] Remote content built for Android. " +
                    $"{addressablesBuild.Summary} PublishedPath={addressablesBuild.PublishedPath ?? "unpublished"}");
            }

            ConfigureSigning(localSettings, requireCustomKeystore);

            EditorUserBuildSettings.buildAppBundle = buildAppBundle;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.development = developmentBuild;
            EditorUserBuildSettings.connectProfiler = connectProfiler;
            EditorUserBuildSettings.allowDebugging = allowDebugging;

            string bundleVersion = ReadSetting("ANDROID_BUNDLE_VERSION", localSettings);
            if (!string.IsNullOrWhiteSpace(bundleVersion))
                PlayerSettings.bundleVersion = bundleVersion.Trim();

            PlayerSettings.Android.bundleVersionCode = ResolveVersionCode(
                outputPath,
                buildAppBundle,
                developmentBuild,
                previousVersionCode,
                localSettings);

            string buildOptionsLabel = buildAppBundle ? "Android App Bundle" : "local Android APK";
            string applicationIdentifier = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            if (string.IsNullOrWhiteSpace(applicationIdentifier))
                throw new InvalidOperationException($"Android application identifier is not configured. Set it in Player Settings before building the {buildOptionsLabel}.");

            BuildOptions buildOptions = BuildOptions.None;
            if (developmentBuild)
                buildOptions |= BuildOptions.Development;
            if (allowDebugging)
                buildOptions |= BuildOptions.AllowDebugging;
            if (connectProfiler)
                buildOptions |= BuildOptions.ConnectWithProfiler;

            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = enabledScenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = buildOptions
            };

            BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {report.summary.result}");

            string archivedOutputPath = ArchiveBuildOutput(outputPath, PlayerSettings.bundleVersion, PlayerSettings.Android.bundleVersionCode);
            string nativeSymbolsPath = TryFindNativeSymbolsZip(
                outputPath,
                PlayerSettings.bundleVersion,
                PlayerSettings.Android.bundleVersionCode);
            Debug.Log($"[BuildAndroid] Success. Output: {outputPath}");
            Debug.Log($"[BuildAndroid] Archived release: {archivedOutputPath}");
            if (!string.IsNullOrWhiteSpace(nativeSymbolsPath))
                Debug.Log($"[BuildAndroid] Native symbols zip: {nativeSymbolsPath}");
            else if (buildAppBundle)
                Debug.LogWarning("[BuildAndroid] Native symbols zip was not found next to the Android build output. Google Play will warn about missing native debug symbols for IL2CPP/native code builds.");
            Debug.Log($"[BuildAndroid] Version: {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})");
            return new AndroidBuildResult(
                outputPath,
                archivedOutputPath,
                nativeSymbolsPath,
                PlayerSettings.bundleVersion,
                PlayerSettings.Android.bundleVersionCode,
                applicationIdentifier,
                buildAppBundle);
        }
        finally
        {
            PlayerSettings.bundleVersion = previousBundleVersion;
            PlayerSettings.Android.bundleVersionCode = previousVersionCode;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, previousApplicationIdentifier);

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
        string extension = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(extension))
            throw new InvalidOperationException($"Android build output '{outputPath}' is missing a file extension.");

        string archivedOutputPath = Path.Combine(releasesDirectory, $"{baseName}-v{safeBundleVersion}-code{versionCode}{extension}");
        File.Copy(outputPath, archivedOutputPath, true);
        return archivedOutputPath;
    }

    static string SanitizeFileNameSegment(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray());
    }

    static void ValidateOutputPath(string outputPath, string expectedExtension)
    {
        if (!string.Equals(Path.GetExtension(outputPath), expectedExtension, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Android build output '{outputPath}' must use the '{expectedExtension}' extension.");
    }

    static string TryFindNativeSymbolsZip(string outputPath, string bundleVersion, int versionCode)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
            return null;

        string baseName = Path.GetFileNameWithoutExtension(outputPath);
        if (string.IsNullOrWhiteSpace(baseName))
            return null;

        string safeBundleVersion = SanitizeFileNameSegment(string.IsNullOrWhiteSpace(bundleVersion) ? "unknown" : bundleVersion.Trim());
        string exactMatch = Path.Combine(outputDirectory, $"{baseName}-{safeBundleVersion}-v{versionCode}-IL2CPP.symbols.zip");
        if (File.Exists(exactMatch))
            return exactMatch;

        string pattern = $"*v{versionCode}-IL2CPP.symbols.zip";
        return Directory.EnumerateFiles(outputDirectory, pattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    static void ConfigureSigning(IReadOnlyDictionary<string, string> localSettings, bool requireCustomKeystore)
    {
        string keystorePath = ReadSetting("ANDROID_KEYSTORE_PATH", localSettings);
        string keystorePassword = ReadSetting("ANDROID_KEYSTORE_PASS", localSettings);
        string keyaliasName = ReadSetting("ANDROID_KEYALIAS_NAME", localSettings);
        string keyaliasPassword = ReadSetting("ANDROID_KEYALIAS_PASS", localSettings);

        bool hasAnyKeystoreSetting =
            !string.IsNullOrWhiteSpace(keystorePath) ||
            !string.IsNullOrWhiteSpace(keystorePassword) ||
            !string.IsNullOrWhiteSpace(keyaliasName) ||
            !string.IsNullOrWhiteSpace(keyaliasPassword);

        bool hasAllKeystoreSettings =
            !string.IsNullOrWhiteSpace(keystorePath) &&
            !string.IsNullOrWhiteSpace(keystorePassword) &&
            !string.IsNullOrWhiteSpace(keyaliasName) &&
            !string.IsNullOrWhiteSpace(keyaliasPassword);

        if (requireCustomKeystore && !hasAllKeystoreSettings)
            throw new InvalidOperationException("Missing required Android signing settings for the release build.");

        if (!hasAnyKeystoreSetting)
        {
            if (requireCustomKeystore)
                throw new InvalidOperationException("Android release builds require a custom keystore.");

            PlayerSettings.Android.useCustomKeystore = false;
            Debug.LogWarning("[BuildAndroid] No custom keystore configured for the local APK build. Unity will use its default debug signing.");
            return;
        }

        if (!hasAllKeystoreSettings)
            throw new InvalidOperationException("Android keystore settings are incomplete. Either provide all ANDROID_KEYSTORE_* values or remove them for the local APK build.");

        if (!File.Exists(keystorePath))
            throw new FileNotFoundException($"Android keystore not found at '{keystorePath}'.");

        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystorePath;
        PlayerSettings.Android.keystorePass = keystorePassword;
        PlayerSettings.Android.keyaliasName = keyaliasName;
        PlayerSettings.Android.keyaliasPass = keyaliasPassword;
    }

    static string ResolveOutputPath(
        string projectRoot,
        IReadOnlyDictionary<string, string> localSettings,
        IReadOnlyList<string> settingNames,
        string defaultOutputRelativePath)
    {
        string configuredOutput = null;
        foreach (string settingName in settingNames)
        {
            configuredOutput = ReadSetting(settingName, localSettings);
            if (!string.IsNullOrWhiteSpace(configuredOutput))
                break;
        }

        string pathToResolve = string.IsNullOrWhiteSpace(configuredOutput)
            ? defaultOutputRelativePath
            : configuredOutput.Trim();

        return Path.IsPathRooted(pathToResolve)
            ? pathToResolve
            : Path.GetFullPath(Path.Combine(projectRoot, pathToResolve));
    }

    static int ResolveVersionCode(
        string outputPath,
        bool buildAppBundle,
        bool developmentBuild,
        int currentVersionCode,
        IReadOnlyDictionary<string, string> localSettings)
    {
        bool hasConfiguredVersionCode = TryReadPositiveIntegerSetting(BundleVersionCodeSettingName, localSettings, out int configuredVersionCode);
        bool autoIncrementReleaseVersionCode = ReadBooleanSetting(AutoIncrementReleaseVersionCodeSettingName, localSettings, defaultValue: false);

        if (!buildAppBundle || developmentBuild || !autoIncrementReleaseVersionCode)
            return hasConfiguredVersionCode ? configuredVersionCode : currentVersionCode;

        int highestArchivedVersionCode = FindHighestArchivedVersionCode(outputPath);
        bool hasReleaseHistory = hasConfiguredVersionCode || highestArchivedVersionCode > 0;
        if (!hasReleaseHistory)
            return currentVersionCode;

        int versionFloor = currentVersionCode;
        if (hasConfiguredVersionCode)
            versionFloor = Math.Max(versionFloor, configuredVersionCode);
        if (highestArchivedVersionCode > 0)
            versionFloor = Math.Max(versionFloor, highestArchivedVersionCode);

        int nextVersionCode = versionFloor + 1;
        Debug.Log(
            $"[BuildAndroid] Auto-incremented Android bundle version code to {nextVersionCode}. " +
            $"Floor={versionFloor} Configured={(hasConfiguredVersionCode ? configuredVersionCode.ToString() : "unset")} " +
            $"Archived={(highestArchivedVersionCode > 0 ? highestArchivedVersionCode.ToString() : "none")}");
        return nextVersionCode;
    }

    static int FindHighestArchivedVersionCode(string outputPath)
    {
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return 0;

        string releasesDirectory = Path.Combine(outputDirectory, "releases");
        if (!Directory.Exists(releasesDirectory))
            return 0;

        string extension = Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(extension))
            return 0;

        int highestVersionCode = 0;
        foreach (string archivedFilePath in Directory.EnumerateFiles(releasesDirectory, $"*{extension}", SearchOption.TopDirectoryOnly))
        {
            if (!TryParseArchivedVersionCode(archivedFilePath, out int versionCode))
                continue;

            highestVersionCode = Math.Max(highestVersionCode, versionCode);
        }

        return highestVersionCode;
    }

    static bool TryParseArchivedVersionCode(string archivedFilePath, out int versionCode)
    {
        versionCode = 0;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(archivedFilePath);
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
            return false;

        int markerIndex = fileNameWithoutExtension.LastIndexOf("-code", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return false;

        string versionCodeValue = fileNameWithoutExtension.Substring(markerIndex + "-code".Length);
        return int.TryParse(versionCodeValue, out versionCode) && versionCode > 0;
    }

    static bool TryReadPositiveIntegerSetting(
        string name,
        IReadOnlyDictionary<string, string> localSettings,
        out int parsedValue)
    {
        parsedValue = 0;
        string value = ReadSetting(name, localSettings);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (!int.TryParse(value, out parsedValue) || parsedValue <= 0)
            throw new InvalidOperationException($"{name} must be a positive integer.");

        return true;
    }

    static bool ReadBooleanSetting(
        string name,
        IReadOnlyDictionary<string, string> localSettings,
        bool defaultValue)
    {
        string value = ReadSetting(name, localSettings);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;

        switch (value.Trim().ToLowerInvariant())
        {
            case "1":
            case "true":
            case "yes":
            case "on":
                return true;

            case "0":
            case "false":
            case "no":
            case "off":
                return false;

            default:
                throw new InvalidOperationException($"{name} must be a boolean value such as true/false.");
        }
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
    public AndroidBuildResult(
        string outputPath,
        string archivedOutputPath,
        string nativeSymbolsPath,
        string bundleVersion,
        int versionCode,
        string applicationIdentifier,
        bool isAppBundle)
    {
        OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        ArchivedOutputPath = archivedOutputPath ?? throw new ArgumentNullException(nameof(archivedOutputPath));
        NativeSymbolsPath = nativeSymbolsPath;
        BundleVersion = bundleVersion ?? throw new ArgumentNullException(nameof(bundleVersion));
        VersionCode = versionCode;
        ApplicationIdentifier = applicationIdentifier ?? throw new ArgumentNullException(nameof(applicationIdentifier));
        IsAppBundle = isAppBundle;
    }

    public string OutputPath { get; }
    public string ArchivedOutputPath { get; }
    public string NativeSymbolsPath { get; }
    public string BundleVersion { get; }
    public int VersionCode { get; }
    public string ApplicationIdentifier { get; }
    public bool IsAppBundle { get; }
}

public sealed class AndroidBuildPreview
{
    public AndroidBuildPreview(
        string outputPath,
        string bundleVersion,
        int versionCode,
        string applicationIdentifier,
        bool isAppBundle)
    {
        OutputPath = outputPath ?? throw new ArgumentNullException(nameof(outputPath));
        BundleVersion = bundleVersion ?? throw new ArgumentNullException(nameof(bundleVersion));
        VersionCode = versionCode;
        ApplicationIdentifier = applicationIdentifier ?? throw new ArgumentNullException(nameof(applicationIdentifier));
        IsAppBundle = isAppBundle;
    }

    public string OutputPath { get; }
    public string BundleVersion { get; }
    public int VersionCode { get; }
    public string ApplicationIdentifier { get; }
    public bool IsAppBundle { get; }
}
