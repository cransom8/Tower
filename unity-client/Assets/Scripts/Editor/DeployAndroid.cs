#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace CastleDefender.Editor
{
    public static class DeployAndroid
    {
        const string ReleaseMenuPath = "Castle Defender/Deploy Android";
        const string LocalMenuPath = "Castle Defender/Deploy Local Android";
        const string UploadScriptRelativePath = "../scripts/upload-addressables.ps1";
        const string RepoDotEnvRelativePath = ".env";
        const string RepoDotEnvLocalRelativePath = ".env.local";
        const string AddressablesSourceRelativePath = "unity-client/ServerData/Android";
        const string UploadProgressPrefix = "##UPLOAD_PROGRESS|";

        [MenuItem(ReleaseMenuPath, false, 5)]
        public static void Run()
        {
            DeploymentContext context = PrepareDeploymentContext("[DeployAndroid]");
            if (context == null)
                return;

            try
            {
                RunReleasePipeline(context);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem(LocalMenuPath, false, 6)]
        public static void RunLocal()
        {
            DeploymentContext context = PrepareDeploymentContext("[DeployLocalAndroid]");
            if (context == null)
                return;

            try
            {
                RunLocalPipeline(context);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void RunReleasePipeline(DeploymentContext context)
        {
            const string progressTitle = "Deploy Android";

            EditorUtility.DisplayProgressBar(progressTitle, "Building Android addressables...", 0.15f);
            AddressablesBuildResult addressablesBuild = RemoteContentBuildAddressables.BuildForTarget(BuildTarget.Android, restorePreviousTarget: false);

            EditorUtility.DisplayProgressBar(progressTitle, "Starting GCS upload...", 0.52f);
            using RunningProcess uploadProcess = StartUploadScript(
                context,
                progressTitle,
                progressStart: 0.84f,
                progressEnd: 0.97f,
                stageRailwayMetadata: true,
                failurePrefix: "[DeployAndroid] Upload step failed.");

            ExceptionDispatchInfo buildFailure = null;
            AndroidBuildResult androidBuild = null;
            try
            {
                EditorUtility.DisplayProgressBar(progressTitle, "Building Android app bundle while addressables upload...", 0.55f);
                androidBuild = BuildAndroid.BuildRelease(buildRemoteContent: false);
            }
            catch (Exception ex)
            {
                buildFailure = ExceptionDispatchInfo.Capture(ex);
            }

            ExceptionDispatchInfo uploadFailure = null;
            ProcessResult uploadResult = null;
            try
            {
                uploadResult = WaitForProcess(uploadProcess);
            }
            catch (Exception ex)
            {
                uploadFailure = ExceptionDispatchInfo.Capture(ex);
            }

            if (uploadResult != null)
                LogProcessOutput(uploadResult);

            if (buildFailure != null)
                buildFailure.Throw();

            if (uploadFailure != null)
                uploadFailure.Throw();

            EditorUtility.DisplayProgressBar(progressTitle, "Finishing Android deploy...", 1f);
            Debug.Log(
                $"[DeployAndroid] Finished. Addressables={addressablesBuild.PublishedPath ?? "unpublished"} " +
                $"AAB={androidBuild.ArchivedOutputPath} Version={androidBuild.BundleVersion} ({androidBuild.VersionCode})");
        }

        static void RunLocalPipeline(DeploymentContext context)
        {
            const string progressTitle = "Deploy Local Android";

            var envOverrides = LoadDotEnvFiles(context.RepoRoot, RepoDotEnvRelativePath, RepoDotEnvLocalRelativePath);

            EditorUtility.DisplayProgressBar(progressTitle, "Running database migrations...", 0.08f);
            ProcessResult migrationResult = RunDatabaseMigrations(context, envOverrides);
            LogProcessOutput(migrationResult);

            EditorUtility.DisplayProgressBar(progressTitle, "Building Android addressables...", 0.32f);
            AddressablesBuildResult addressablesBuild = RemoteContentBuildAddressables.BuildForTarget(BuildTarget.Android, restorePreviousTarget: false);

            EditorUtility.DisplayProgressBar(progressTitle, "Starting GCS upload...", 0.56f);
            using RunningProcess uploadProcess = StartUploadScript(
                context,
                progressTitle,
                progressStart: 0.84f,
                progressEnd: 0.94f,
                stageRailwayMetadata: false,
                failurePrefix: "[DeployLocalAndroid] Addressables upload failed.");

            ExceptionDispatchInfo buildFailure = null;
            AndroidBuildResult androidBuild = null;
            try
            {
                EditorUtility.DisplayProgressBar(progressTitle, "Building local Android APK while addressables upload...", 0.60f);
                androidBuild = BuildAndroid.BuildLocalApk(buildRemoteContent: false);
            }
            catch (Exception ex)
            {
                buildFailure = ExceptionDispatchInfo.Capture(ex);
            }

            ExceptionDispatchInfo uploadFailure = null;
            ProcessResult uploadResult = null;
            try
            {
                uploadResult = WaitForProcess(uploadProcess);
            }
            catch (Exception ex)
            {
                uploadFailure = ExceptionDispatchInfo.Capture(ex);
            }

            if (uploadResult != null)
                LogProcessOutput(uploadResult);

            if (buildFailure != null)
                buildFailure.Throw();

            if (uploadFailure != null)
                uploadFailure.Throw();

            InstallResult installResult = TryInstallApkOnConnectedDevice(context, androidBuild, progressTitle, 0.95f, 0.99f);
            EditorUtility.DisplayProgressBar(progressTitle, "Finalizing local Android deploy...", 1f);

            if (!installResult.Installed && !string.IsNullOrWhiteSpace(installResult.Message))
                Debug.LogWarning($"[DeployLocalAndroid] {installResult.Message}");

            Debug.Log(
                $"[DeployLocalAndroid] Finished. Addressables={addressablesBuild.PublishedPath ?? "unpublished"} " +
                $"APK={androidBuild.ArchivedOutputPath} Version={androidBuild.BundleVersion} ({androidBuild.VersionCode}) " +
                $"{installResult.GetSummary()}");
        }

        static DeploymentContext PrepareDeploymentContext(string logPrefix)
        {
            EnsureEditorReady(logPrefix);

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            AssetDatabase.SaveAssets();

            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(unityProjectRoot, ".."));
            string uploadScriptPath = Path.GetFullPath(Path.Combine(unityProjectRoot, UploadScriptRelativePath));
            if (!File.Exists(uploadScriptPath))
                throw new FileNotFoundException($"{logPrefix} Upload script not found at '{uploadScriptPath}'.");

            return new DeploymentContext(unityProjectRoot, repoRoot, uploadScriptPath);
        }

        static void EnsureEditorReady(string logPrefix)
        {
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException($"{logPrefix} Unity is still compiling scripts. Wait for compilation to finish before deploying.");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException($"{logPrefix} Exit Play Mode before running the Android deployment pipeline.");
        }

        static ProcessResult RunDatabaseMigrations(DeploymentContext context, IReadOnlyDictionary<string, string> envOverrides)
        {
            string databaseUrl = ResolveEnvironmentValue("DATABASE_URL", envOverrides);
            if (string.IsNullOrWhiteSpace(databaseUrl))
                throw new InvalidOperationException("[DeployLocalAndroid] DATABASE_URL was not found in the Unity process environment, .env, or .env.local.");

            string npmCommandPath = TryResolveNpmCommandPath(envOverrides);
            if (string.IsNullOrWhiteSpace(npmCommandPath))
                throw new InvalidOperationException("[DeployLocalAndroid] npm.cmd was not found. Install Node.js or set NPM_CMD_PATH before running the local Android deploy.");

            return RunProcess(
                "cmd.exe",
                $"/d /s /c \"\"{npmCommandPath}\" run migrate\"",
                context.RepoRoot,
                "[DeployLocalAndroid] Database migration step failed.",
                envOverrides);
        }

        static ProcessResult RunUploadScript(
            DeploymentContext context,
            string progressTitle,
            float progressStart,
            float progressEnd,
            bool stageRailwayMetadata,
            string failurePrefix)
        {
            using RunningProcess runningProcess = StartUploadScript(
                context,
                progressTitle,
                progressStart,
                progressEnd,
                stageRailwayMetadata,
                failurePrefix);
            return WaitForProcess(runningProcess);
        }

        static RunningProcess StartUploadScript(
            DeploymentContext context,
            string progressTitle,
            float progressStart,
            float progressEnd,
            bool stageRailwayMetadata,
            string failurePrefix)
        {
            var arguments = new StringBuilder();
            arguments.Append("-NoProfile -ExecutionPolicy Bypass -File ");
            arguments.Append(QuotePowerShellArgument(context.UploadScriptPath));
            arguments.Append(" -Platform Android -SourceDir ");
            arguments.Append(QuotePowerShellArgument(AddressablesSourceRelativePath));
            if (stageRailwayMetadata)
                arguments.Append(" -StageRailwayMetadata");

            return StartProcess(
                "powershell.exe",
                arguments.ToString(),
                context.RepoRoot,
                failurePrefix,
                outputInterceptor: line => HandleUploadProgressLine(progressTitle, progressStart, progressEnd, line));
        }

        static InstallResult TryInstallApkOnConnectedDevice(
            DeploymentContext context,
            AndroidBuildResult androidBuild,
            string progressTitle,
            float progressStart,
            float progressEnd)
        {
            if (androidBuild.IsAppBundle)
                return InstallResult.Skipped("Local deployment produced an Android App Bundle instead of an APK.");

            string adbPath = TryResolveAdbPath();
            if (string.IsNullOrWhiteSpace(adbPath))
                return InstallResult.Skipped("adb.exe was not found. The APK was built successfully, but automatic phone install was skipped.");

            EditorUtility.DisplayProgressBar(progressTitle, "Checking connected Android devices...", progressStart);
            ProcessResult devicesResult = RunProcess(
                adbPath,
                "devices",
                context.RepoRoot,
                "[DeployLocalAndroid] Failed to query adb devices.");

            LogProcessOutput(devicesResult);

            List<string> connectedDevices = ParseConnectedDevices(devicesResult.StandardOutput);
            if (connectedDevices.Count == 0)
                return InstallResult.Skipped("No Android device was connected through adb. Plug in your phone with USB debugging enabled to auto-install.");

            if (connectedDevices.Count > 1)
                return InstallResult.Skipped($"Multiple adb devices were connected ({string.Join(", ", connectedDevices)}). Automatic install was skipped to avoid choosing the wrong target.");

            string targetDevice = connectedDevices[0];
            EditorUtility.DisplayProgressBar(progressTitle, $"Installing APK to {targetDevice}...", progressEnd);
            ProcessResult installResult = RunProcess(
                adbPath,
                $"install -r -d -g {QuoteCommandArgument(androidBuild.OutputPath)}",
                context.RepoRoot,
                "[DeployLocalAndroid] APK install failed.");

            LogProcessOutput(installResult);
            return InstallResult.InstalledTo(targetDevice);
        }

        static IReadOnlyDictionary<string, string> LoadDotEnvFiles(string repoRoot, params string[] relativePaths)
        {
            var envValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string relativePath in relativePaths)
            {
                string absolutePath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
                if (!File.Exists(absolutePath))
                    continue;

                foreach (string rawLine in File.ReadAllLines(absolutePath))
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    string trimmedLine = rawLine.Trim();
                    if (trimmedLine.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    int separatorIndex = trimmedLine.IndexOf('=');
                    if (separatorIndex <= 0)
                        continue;

                    string key = trimmedLine.Substring(0, separatorIndex).Trim();
                    string value = trimmedLine.Substring(separatorIndex + 1).Trim();
                    envValues[key] = TrimMatchingQuotes(value);
                }
            }

            return envValues;
        }

        static string ResolveEnvironmentValue(string name, IReadOnlyDictionary<string, string> envOverrides)
        {
            string currentValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(currentValue))
                return currentValue.Trim();

            if (envOverrides != null && envOverrides.TryGetValue(name, out string overrideValue) && !string.IsNullOrWhiteSpace(overrideValue))
                return overrideValue.Trim();

            return null;
        }

        static string TrimMatchingQuotes(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 2)
                return value;

            char first = value[0];
            char last = value[value.Length - 1];
            if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                return value.Substring(1, value.Length - 2);

            return value;
        }

        static string TryResolveAdbPath()
        {
            string adbPath = Environment.GetEnvironmentVariable("ADB_PATH");
            if (!string.IsNullOrWhiteSpace(adbPath) && File.Exists(adbPath))
                return Path.GetFullPath(adbPath);

            string[] sdkRoots =
            {
                Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
                EditorPrefs.GetString("AndroidSdkRoot", string.Empty),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK")
            };

            foreach (string sdkRoot in sdkRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                string candidate = Path.Combine(sdkRoot, "platform-tools", "adb.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            string pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string pathEntry in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(pathEntry))
                    continue;

                string candidate = Path.Combine(pathEntry.Trim(), "adb.exe");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return null;
        }

        static string TryResolveNpmCommandPath(IReadOnlyDictionary<string, string> envOverrides)
        {
            string configuredPath = ResolveEnvironmentValue("NPM_CMD_PATH", envOverrides);
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return Path.GetFullPath(configuredPath);

            string pathValue = ResolveEnvironmentValue("PATH", envOverrides) ?? string.Empty;
            foreach (string pathEntry in pathValue.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(pathEntry))
                    continue;

                string candidate = Path.Combine(pathEntry.Trim(), "npm.cmd");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            string[] commonInstallRoots =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "npm.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "npm.cmd")
            };

            foreach (string candidate in commonInstallRoots)
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }

            return null;
        }

        static List<string> ParseConnectedDevices(string standardOutput)
        {
            return (standardOutput ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.EndsWith("\tdevice", StringComparison.OrdinalIgnoreCase))
                .Select(line => line.Substring(0, line.IndexOf('\t')))
                .Where(serial => !string.IsNullOrWhiteSpace(serial))
                .ToList();
        }

        static bool HandleUploadProgressLine(string progressTitle, float progressStart, float progressEnd, string line)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(UploadProgressPrefix, StringComparison.Ordinal))
                return false;

            string[] parts = line.Split(new[] { '|' }, 5);
            if (parts.Length < 5)
                return true;

            long processedBytes = ParseLongOrDefault(parts[2]);
            long totalBytes = Math.Max(1L, ParseLongOrDefault(parts[3]));
            string currentFile = string.IsNullOrWhiteSpace(parts[4]) ? "addressables" : parts[4];

            float fraction = Mathf.Clamp01((float)processedBytes / totalBytes);
            float progress = Mathf.Lerp(progressStart, progressEnd, fraction);
            string status = parts[1] == "end"
                ? $"Uploaded addressables ({FormatMegabytes(processedBytes)} / {FormatMegabytes(totalBytes)} MB)"
                : $"Uploading {currentFile} ({FormatMegabytes(processedBytes)} / {FormatMegabytes(totalBytes)} MB)";

            EditorUtility.DisplayProgressBar(progressTitle, status, progress);
            return true;
        }

        static long ParseLongOrDefault(string value)
        {
            return long.TryParse(value, out long parsed) ? parsed : 0L;
        }

        static string FormatMegabytes(long bytes)
        {
            return (bytes / (1024f * 1024f)).ToString("0.0");
        }

        static void LogProcessOutput(ProcessResult processResult)
        {
            if (!string.IsNullOrWhiteSpace(processResult.StandardOutput))
                Debug.Log(processResult.StandardOutput.TrimEnd());

            if (!string.IsNullOrWhiteSpace(processResult.StandardError))
                Debug.LogWarning(processResult.StandardError.TrimEnd());
        }

        static ProcessResult RunProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            string failurePrefix,
            IReadOnlyDictionary<string, string> environmentOverrides = null,
            Func<string, bool> outputInterceptor = null)
        {
            using RunningProcess runningProcess = StartProcess(
                fileName,
                arguments,
                workingDirectory,
                failurePrefix,
                environmentOverrides,
                outputInterceptor);
            return WaitForProcess(runningProcess);
        }

        static RunningProcess StartProcess(
            string fileName,
            string arguments,
            string workingDirectory,
            string failurePrefix,
            IReadOnlyDictionary<string, string> environmentOverrides = null,
            Func<string, bool> outputInterceptor = null)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            if (environmentOverrides != null)
            {
                foreach (KeyValuePair<string, string> pair in environmentOverrides)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
                        continue;

                    startInfo.EnvironmentVariables[pair.Key] = pair.Value;
                }
            }

            Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"{failurePrefix} Failed to start '{fileName}'.");

            var runningProcess = new RunningProcess(process, failurePrefix, outputInterceptor);

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                lock (runningProcess.SyncRoot)
                {
                    runningProcess.PendingStdout.Enqueue(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null)
                    return;

                lock (runningProcess.SyncRoot)
                {
                    runningProcess.PendingStderr.Enqueue(e.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return runningProcess;
        }

        static ProcessResult WaitForProcess(RunningProcess runningProcess)
        {
            while (!runningProcess.Process.WaitForExit(50))
                DrainProcessOutput(runningProcess);

            runningProcess.Process.WaitForExit();
            DrainProcessOutput(runningProcess);

            string standardOutput = string.Join(Environment.NewLine, runningProcess.StandardOutputLines);
            string standardError = string.Join(Environment.NewLine, runningProcess.StandardErrorLines);

            if (runningProcess.Process.ExitCode != 0)
            {
                var message = new StringBuilder();
                message.AppendLine($"{runningProcess.FailurePrefix} Exit code {runningProcess.Process.ExitCode}.");
                if (!string.IsNullOrWhiteSpace(standardOutput))
                {
                    message.AppendLine("STDOUT:");
                    message.AppendLine(standardOutput.TrimEnd());
                }

                if (!string.IsNullOrWhiteSpace(standardError))
                {
                    message.AppendLine("STDERR:");
                    message.AppendLine(standardError.TrimEnd());
                }

                throw new InvalidOperationException(message.ToString().TrimEnd());
            }

            return new ProcessResult(runningProcess.Process.ExitCode, standardOutput, standardError);
        }

        static void DrainProcessOutput(RunningProcess runningProcess)
        {
            lock (runningProcess.SyncRoot)
            {
                while (runningProcess.PendingStdout.Count > 0)
                {
                    string line = runningProcess.PendingStdout.Dequeue();
                    bool consumed = runningProcess.OutputInterceptor != null && runningProcess.OutputInterceptor(line);
                    if (!consumed)
                        runningProcess.StandardOutputLines.Add(line);
                }

                while (runningProcess.PendingStderr.Count > 0)
                    runningProcess.StandardErrorLines.Add(runningProcess.PendingStderr.Dequeue());
            }
        }

        static string QuotePowerShellArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "`\"") + "\"";
        }

        static string QuoteCommandArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
        }

        sealed class DeploymentContext
        {
            public DeploymentContext(string unityProjectRoot, string repoRoot, string uploadScriptPath)
            {
                UnityProjectRoot = unityProjectRoot ?? throw new ArgumentNullException(nameof(unityProjectRoot));
                RepoRoot = repoRoot ?? throw new ArgumentNullException(nameof(repoRoot));
                UploadScriptPath = uploadScriptPath ?? throw new ArgumentNullException(nameof(uploadScriptPath));
            }

            public string UnityProjectRoot { get; }
            public string RepoRoot { get; }
            public string UploadScriptPath { get; }
        }

        sealed class RunningProcess : IDisposable
        {
            public RunningProcess(Process process, string failurePrefix, Func<string, bool> outputInterceptor)
            {
                Process = process ?? throw new ArgumentNullException(nameof(process));
                FailurePrefix = failurePrefix ?? throw new ArgumentNullException(nameof(failurePrefix));
                OutputInterceptor = outputInterceptor;
            }

            public Process Process { get; }
            public string FailurePrefix { get; }
            public Func<string, bool> OutputInterceptor { get; }
            public object SyncRoot { get; } = new object();
            public Queue<string> PendingStdout { get; } = new Queue<string>();
            public Queue<string> PendingStderr { get; } = new Queue<string>();
            public List<string> StandardOutputLines { get; } = new List<string>();
            public List<string> StandardErrorLines { get; } = new List<string>();

            public void Dispose()
            {
                Process.Dispose();
            }
        }

        sealed class ProcessResult
        {
            public ProcessResult(int exitCode, string standardOutput, string standardError)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput;
                StandardError = standardError;
            }

            public int ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
        }

        sealed class InstallResult
        {
            InstallResult(bool installed, string deviceName, string message)
            {
                Installed = installed;
                DeviceName = deviceName;
                Message = message;
            }

            public bool Installed { get; }
            public string DeviceName { get; }
            public string Message { get; }

            public static InstallResult InstalledTo(string deviceName)
            {
                return new InstallResult(true, deviceName, null);
            }

            public static InstallResult Skipped(string message)
            {
                return new InstallResult(false, null, message);
            }

            public string GetSummary()
            {
                return Installed
                    ? $"Installed to {DeviceName}."
                    : Message ?? "Automatic device install was skipped.";
            }
        }
    }
}
#endif
