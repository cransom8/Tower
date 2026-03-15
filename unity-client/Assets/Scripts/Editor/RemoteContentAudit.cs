#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CastleDefender.Game;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteContentAudit
    {
        const string MenuRoot = "Castle Defender/Remote Content/";
        const string DefaultRegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
        const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";
        const string ReportDirectory = "Assets/Reports";
        const string ReportAssetPath = "Assets/Reports/remote_content_audit.json";

        [Serializable]
        sealed class AuditReport
        {
            public string generatedAt;
            public string registryPath;
            public List<AuditEntry> units = new();
            public List<AuditEntry> skins = new();
            public List<string> warnings = new();
        }

        [Serializable]
        sealed class AuditEntry
        {
            public string kind;
            public string key;
            public string unitType;
            public string assetPath;
            public string assetGuid;
            public bool hasPrefab;
            public bool isAddressable;
            public string addressableAddress;
            public List<string> addressableLabels = new();
            public string recommendedContentKey;
            public string recommendedAddressablesLabel;
            public string recommendedPrefabAddress;
            public List<string> warnings = new();
        }

        [MenuItem(MenuRoot + "Audit Registry Readiness")]
        static void AuditRegistryReadiness()
        {
            var report = BuildReport();
            if (report == null) return;

            WriteReport(report);

            int warningCount = report.warnings.Count;
            foreach (var entry in report.units) warningCount += entry.warnings.Count;
            foreach (var entry in report.skins) warningCount += entry.warnings.Count;

            Debug.Log(
                $"[RemoteContentAudit] Units={report.units.Count} Skins={report.skins.Count} Warnings={warningCount}\n" +
                $"Report written to {ReportAssetPath}");
        }

        [MenuItem(MenuRoot + "Export Registry Seed JSON")]
        static void ExportRegistrySeedJson()
        {
            var report = BuildReport();
            if (report == null) return;

            WriteReport(report);
            EditorUtility.RevealInFinder(Path.GetFullPath(ReportAssetPath));
        }

        static AuditReport BuildReport()
        {
            string registryPath = ResolveRegistryPath();
            var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(registryPath);
            if (registry == null)
            {
                Debug.LogError("[RemoteContentAudit] UnitPrefabRegistry not found.");
                return null;
            }

            var report = new AuditReport
            {
                generatedAt = DateTime.UtcNow.ToString("o"),
                registryPath = registryPath,
            };

            var unitKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (registry.entries != null)
            {
                foreach (var entry in registry.entries)
                {
                    var audit = BuildAuditEntry(
                        kind: "unit",
                        key: entry.key,
                        unitType: entry.key,
                        prefab: entry.prefab);
                    report.units.Add(audit);

                    if (!string.IsNullOrWhiteSpace(entry.key) && !unitKeys.Add(entry.key))
                        report.warnings.Add($"Duplicate unit key in registry: {entry.key}");
                }
            }

            var skinKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (registry.skinEntries != null)
            {
                foreach (var entry in registry.skinEntries)
                {
                    var audit = BuildAuditEntry(
                        kind: "skin",
                        key: entry.skinKey,
                        unitType: entry.unitType,
                        prefab: entry.prefab);
                    report.skins.Add(audit);

                    if (!string.IsNullOrWhiteSpace(entry.skinKey) && !skinKeys.Add(entry.skinKey))
                        report.warnings.Add($"Duplicate skin key in registry: {entry.skinKey}");

                    if (!string.IsNullOrWhiteSpace(entry.unitType) && !unitKeys.Contains(entry.unitType))
                        audit.warnings.Add($"Skin references missing base unit key '{entry.unitType}' in registry.");
                }
            }

            if (registry.fallbackPrefab == null)
                report.warnings.Add("Registry fallback prefab is not assigned.");

            return report;
        }

        static AuditEntry BuildAuditEntry(string kind, string key, string unitType, GameObject prefab)
        {
            var audit = new AuditEntry
            {
                kind = kind,
                key = key ?? "",
                unitType = unitType ?? "",
                hasPrefab = prefab != null,
                recommendedContentKey = key ?? "",
                recommendedAddressablesLabel = key ?? "",
            };

            if (prefab == null)
            {
                audit.warnings.Add("Missing prefab reference.");
                return audit;
            }

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            string assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            audit.assetPath = assetPath;
            audit.assetGuid = assetGuid;
            audit.recommendedPrefabAddress = assetPath;

            var addressable = TryGetAddressableInfo(assetGuid);
            if (addressable.found)
            {
                audit.isAddressable = true;
                audit.addressableAddress = addressable.address;
                audit.addressableLabels.AddRange(addressable.labels);
                audit.recommendedPrefabAddress = string.IsNullOrWhiteSpace(addressable.address)
                    ? assetPath
                    : addressable.address;

                if (!addressable.labels.Contains(audit.recommendedAddressablesLabel))
                    audit.warnings.Add($"Missing recommended Addressables label '{audit.recommendedAddressablesLabel}'.");
            }
            else
            {
                audit.warnings.Add("Prefab is not in Addressables.");
            }

            if (string.IsNullOrWhiteSpace(assetGuid))
                audit.warnings.Add("Asset GUID could not be resolved.");

            return audit;
        }

        static (bool found, string address, List<string> labels) TryGetAddressableInfo(string guid)
        {
            var empty = (false, (string)null, new List<string>());
            if (string.IsNullOrWhiteSpace(guid)) return empty;

            var settingsType = Type.GetType(
                "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (settingsType == null) return empty;

            object settings = null;
            var getSettingsMethod = settingsType.GetMethod("GetSettings", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(bool) }, null);
            if (getSettingsMethod != null)
                settings = getSettingsMethod.Invoke(null, new object[] { true });

            if (settings == null)
            {
                var settingsProp = settingsType.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static);
                settings = settingsProp?.GetValue(null);
            }

            if (settings == null) return empty;

            var findAssetEntry = settings.GetType().GetMethod("FindAssetEntry", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(string) }, null);
            var entry = findAssetEntry?.Invoke(settings, new object[] { guid });
            if (entry == null) return empty;

            string address = entry.GetType().GetProperty("address", BindingFlags.Public | BindingFlags.Instance)?.GetValue(entry) as string;

            var labels = new List<string>();
            var labelsProp = entry.GetType().GetProperty("labels", BindingFlags.Public | BindingFlags.Instance);
            var labelsValue = labelsProp?.GetValue(entry) as System.Collections.IEnumerable;
            if (labelsValue != null)
            {
                foreach (var item in labelsValue)
                {
                    if (item != null) labels.Add(item.ToString());
                }
            }

            return (true, address, labels);
        }

        static string ResolveRegistryPath()
        {
            if (AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(DefaultRegistryPath) != null)
                return DefaultRegistryPath;
            return LegacyRegistryPath;
        }

        static void WriteReport(AuditReport report)
        {
            if (!AssetDatabase.IsValidFolder(ReportDirectory))
                AssetDatabase.CreateFolder("Assets", "Reports");

            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(ReportAssetPath, json);
            AssetDatabase.ImportAsset(ReportAssetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

    }
}
#endif
