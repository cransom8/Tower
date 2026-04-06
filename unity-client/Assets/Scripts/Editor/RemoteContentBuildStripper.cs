#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CastleDefender.Editor
{
    // Ensures player builds do not accidentally embed remote-only unit prefabs via registry references.
    public sealed class RemoteContentBuildStripper : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";
        const string BackupFolder = "Library/RemoteContentBuildStripper";
        const string BackupAssetPathFile = "Library/RemoteContentBuildStripper/registry_path.txt";
        const string BackupContentsFile = "Library/RemoteContentBuildStripper/registry_backup.txt";

        static string s_backupContents;
        static string s_registryAssetPath;
        static bool s_registryStripped;

        public int callbackOrder => -1000;

        [InitializeOnLoadMethod]
        static void RestorePendingBackupOnEditorLoad()
        {
            if (HasBackupOnDisk())
            {
                Debug.LogWarning("[RemoteContentBuildStripper] Found a pending registry backup from a previous build. Restoring it now.");
                RestoreRegistryAfterBuild();
            }
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!ShouldStripForBuild(report?.summary.platform ?? BuildTarget.NoTarget))
                return;

            PrepareRegistryForPlayerBuild(report.summary.platform);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!ShouldStripForBuild(report?.summary.platform ?? BuildTarget.NoTarget))
                return;

            RestoreRegistryAfterBuild();
        }

        [MenuItem("Castle Defender/Remote Content/Restore Registry After Build")]
        static void RestoreRegistryAfterBuildMenu()
        {
            RestoreRegistryAfterBuild();
        }

        static void PrepareRegistryForPlayerBuild(BuildTarget target)
        {
            if (s_registryStripped)
                return;

            string assetPath = ResolveRegistryAssetPath();
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogWarning("[RemoteContentBuildStripper] UnitPrefabRegistry not found. Skipping strip step.");
                return;
            }

            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath));
            if (!File.Exists(absolutePath))
            {
                Debug.LogWarning($"[RemoteContentBuildStripper] Registry file not found at {absolutePath}. Skipping strip step.");
                return;
            }

            s_backupContents = File.ReadAllText(absolutePath);
            s_registryAssetPath = assetPath;
            PersistBackupToDisk(assetPath, s_backupContents);

            if (!RemoteContentStripRegistryReferences.StripRegistryReferences(saveAssets: true))
                throw new BuildFailedException($"Failed to strip remote prefab references from UnitPrefabRegistry before {target} build.");

            s_registryStripped = true;
            Debug.Log($"[RemoteContentBuildStripper] Stripped registry references for {target} build: {assetPath}");
        }

        static void RestoreRegistryAfterBuild()
        {
            LoadBackupFromDiskIfNeeded();

            if (string.IsNullOrEmpty(s_registryAssetPath) || s_backupContents == null)
                return;

            string absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", s_registryAssetPath));
            File.WriteAllText(absolutePath, s_backupContents);
            AssetDatabase.ImportAsset(s_registryAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            Debug.Log($"[RemoteContentBuildStripper] Restored registry references after build: {s_registryAssetPath}");

            s_backupContents = null;
            s_registryAssetPath = null;
            s_registryStripped = false;
            DeleteBackupFromDisk();
        }

        static string ResolveRegistryAssetPath()
        {
            if (File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", RegistryPath))))
                return RegistryPath;

            if (File.Exists(Path.GetFullPath(Path.Combine(Application.dataPath, "..", LegacyRegistryPath))))
                return LegacyRegistryPath;

            return null;
        }

        static bool ShouldStripForBuild(BuildTarget target)
        {
            return target == BuildTarget.Android;
        }

        static void PersistBackupToDisk(string assetPath, string contents)
        {
            string backupFolderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupFolder));
            Directory.CreateDirectory(backupFolderPath);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupAssetPathFile)), assetPath, Encoding.UTF8);
            File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupContentsFile)), contents, Encoding.UTF8);
        }

        static void LoadBackupFromDiskIfNeeded()
        {
            if (!string.IsNullOrEmpty(s_registryAssetPath) && s_backupContents != null)
                return;

            string assetPathFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupAssetPathFile));
            string contentsFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupContentsFile));
            if (!File.Exists(assetPathFile) || !File.Exists(contentsFile))
                return;

            s_registryAssetPath = File.ReadAllText(assetPathFile, Encoding.UTF8).Trim();
            s_backupContents = File.ReadAllText(contentsFile, Encoding.UTF8);
            s_registryStripped = true;
        }

        static bool HasBackupOnDisk()
        {
            string assetPathFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupAssetPathFile));
            string contentsFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupContentsFile));
            return File.Exists(assetPathFile) && File.Exists(contentsFile);
        }

        static void DeleteBackupFromDisk()
        {
            string assetPathFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupAssetPathFile));
            string contentsFile = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupContentsFile));
            string folderPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BackupFolder));

            if (File.Exists(assetPathFile))
                File.Delete(assetPathFile);
            if (File.Exists(contentsFile))
                File.Delete(contentsFile);
            if (Directory.Exists(folderPath) && Directory.GetFileSystemEntries(folderPath).Length == 0)
                Directory.Delete(folderPath);
        }
    }
}
#endif
