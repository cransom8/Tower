#if UNITY_EDITOR
using System;
using System.IO;
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
        const string MenuPath = "Castle Defender/Deploy Android";
        const string UploadScriptRelativePath = "../scripts/upload-addressables.ps1";

        [MenuItem(MenuPath, false, 5)]
        public static void Run()
        {
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("[DeployAndroid] Unity is still compiling scripts. Wait for compilation to finish before deploying.");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("[DeployAndroid] Exit Play Mode before running the Android deployment pipeline.");

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            AssetDatabase.SaveAssets();

            string unityProjectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string repoRoot = Path.GetFullPath(Path.Combine(unityProjectRoot, ".."));
            string uploadScriptPath = Path.GetFullPath(Path.Combine(unityProjectRoot, UploadScriptRelativePath));
            if (!File.Exists(uploadScriptPath))
                throw new FileNotFoundException($"[DeployAndroid] Upload script not found at '{uploadScriptPath}'.");

            try
            {
                EditorUtility.DisplayProgressBar("Deploy Android", "Building Android addressables...", 0.2f);
                AddressablesBuildResult addressablesBuild = RemoteContentBuildAddressables.BuildForTarget(BuildTarget.Android, restorePreviousTarget: false);

                EditorUtility.DisplayProgressBar("Deploy Android", "Building Android app bundle...", 0.55f);
                AndroidBuildResult androidBuild = BuildAndroid.BuildRelease();

                EditorUtility.DisplayProgressBar("Deploy Android", "Uploading Android addressables and staging Railway metadata...", 0.85f);
                ProcessResult uploadResult = RunPowerShellScript(
                    uploadScriptPath,
                    repoRoot,
                    $"-Platform Android -StageRailwayMetadata -SourceDir {QuotePowerShellArgument("unity-client/ServerData/Android")}"
                );

                if (!string.IsNullOrWhiteSpace(uploadResult.StandardOutput))
                    Debug.Log(uploadResult.StandardOutput.TrimEnd());
                if (!string.IsNullOrWhiteSpace(uploadResult.StandardError))
                    Debug.LogWarning(uploadResult.StandardError.TrimEnd());

                Debug.Log(
                    $"[DeployAndroid] Finished. Addressables={addressablesBuild.PublishedPath ?? "unpublished"} " +
                    $"AAB={androidBuild.ArchivedOutputPath} Version={androidBuild.BundleVersion} ({androidBuild.VersionCode})"
                );
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static ProcessResult RunPowerShellScript(string scriptPath, string workingDirectory, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File {QuotePowerShellArgument(scriptPath)} {arguments}",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            Process process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("[DeployAndroid] Failed to start the PowerShell deployment step.");

            using (process)
            {
                string standardOutput = process.StandardOutput.ReadToEnd();
                string standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    var message = new StringBuilder();
                    message.AppendLine($"[DeployAndroid] Upload step failed with exit code {process.ExitCode}.");
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

                return new ProcessResult(process.ExitCode, standardOutput, standardError);
            }
        }

        static string QuotePowerShellArgument(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "`\"") + "\"";
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
    }
}
#endif
