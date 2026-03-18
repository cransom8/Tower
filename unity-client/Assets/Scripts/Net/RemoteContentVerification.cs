using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CastleDefender.Net
{
    public static class RemoteContentVerification
    {
        sealed class VerificationEventEntry
        {
            public DateTime TimeUtc;
            public string Category;
            public string Detail;

            public override string ToString()
            {
                return $"[{TimeUtc:HH:mm:ss}] {Category}: {Detail}";
            }
        }

        public enum FaultKind
        {
            ManifestDownload,
            AddressablesInitialization,
            T1GameplayDownload,
            PortraitDownload,
            EnvironmentDownload,
        }

        const int MaxEvents = 256;
        static readonly List<VerificationEventEntry> s_events = new();
        static readonly Dictionary<string, int> s_ownerRequestCounts = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> s_ownerWaitCounts = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> s_awaitOnlyCounts = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> s_reuseCounts = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, int> s_faultCounts = new(StringComparer.OrdinalIgnoreCase);

        public static void RecordEvent(string category, string detail)
        {
            var entry = new VerificationEventEntry
            {
                TimeUtc = DateTime.UtcNow,
                Category = NormalizeLabel(category),
                Detail = detail?.Trim() ?? string.Empty,
            };

            if (s_events.Count >= MaxEvents)
                s_events.RemoveAt(0);

            s_events.Add(entry);
            Debug.Log($"[RemoteContentVerification] {entry}");
        }

        public static void RecordOwnerRequest(string subset, string requester, bool waitedOnExistingWork)
        {
            string normalizedSubset = NormalizeLabel(subset);
            Increment(s_ownerRequestCounts, normalizedSubset);
            if (waitedOnExistingWork)
                Increment(s_ownerWaitCounts, normalizedSubset);

            RecordEvent(
                "owner",
                $"subset={normalizedSubset} requester={NormalizeRequester(requester)} waitedOnExistingWork={waitedOnExistingWork}");
        }

        public static void RecordAwaitOnly(string subsystem, string subset)
        {
            string normalizedSubset = NormalizeLabel(subset);
            Increment(s_awaitOnlyCounts, normalizedSubset);
            RecordEvent("await", $"subset={normalizedSubset} subsystem={NormalizeRequester(subsystem)}");
        }

        public static void RecordReuse(string subset, string detail)
        {
            string normalizedSubset = NormalizeLabel(subset);
            Increment(s_reuseCounts, normalizedSubset);
            RecordEvent("reuse", $"subset={normalizedSubset} {detail?.Trim()}".TrimEnd());
        }

        public static bool ConsumeFailure(FaultKind kind, string context, out string reason)
        {
            int remaining = PlayerPrefs.GetInt(GetFaultKey(kind), 0);
            if (remaining <= 0)
            {
                reason = null;
                return false;
            }

            PlayerPrefs.SetInt(GetFaultKey(kind), remaining - 1);
            PlayerPrefs.Save();

            reason = $"Verification override forced {GetFaultLabel(kind)} failure during {context}. Remaining={remaining - 1}.";
            Increment(s_faultCounts, GetFaultLabel(kind));
            RecordEvent("fault", reason);
            return true;
        }

        public static void SetFailureCount(FaultKind kind, int count)
        {
            if (count <= 0)
                PlayerPrefs.DeleteKey(GetFaultKey(kind));
            else
                PlayerPrefs.SetInt(GetFaultKey(kind), count);

            PlayerPrefs.Save();
        }

        public static int GetFailureCount(FaultKind kind)
        {
            return PlayerPrefs.GetInt(GetFaultKey(kind), 0);
        }

        public static void ClearFailureCounts()
        {
            foreach (FaultKind kind in Enum.GetValues(typeof(FaultKind)))
                PlayerPrefs.DeleteKey(GetFaultKey(kind));
            PlayerPrefs.Save();
        }

        public static void ClearEvidence()
        {
            s_events.Clear();
            s_ownerRequestCounts.Clear();
            s_ownerWaitCounts.Clear();
            s_awaitOnlyCounts.Clear();
            s_reuseCounts.Clear();
            s_faultCounts.Clear();
        }

        public static string BuildEvidenceReport()
        {
            if (s_events.Count == 0)
                return "Remote content verification log is empty.";

            var builder = new StringBuilder();
            builder.AppendLine("Summary:");
            AppendOwnerSummary(builder);
            AppendCountSummary(builder, "Await-only requests", s_awaitOnlyCounts, "count");
            AppendCountSummary(builder, "Reuse hits", s_reuseCounts, "count");
            AppendCountSummary(builder, "Fault injections consumed", s_faultCounts, "count");

            builder.AppendLine();
            builder.AppendLine("Timeline:");
            for (int i = 0; i < s_events.Count; i++)
                builder.AppendLine(s_events[i].ToString());
            return builder.ToString().TrimEnd();
        }

        static string NormalizeRequester(string requester)
        {
            return string.IsNullOrWhiteSpace(requester) ? "unknown" : requester.Trim();
        }

        static string NormalizeLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        }

        static void Increment(Dictionary<string, int> counts, string key)
        {
            if (counts.TryGetValue(key, out int current))
                counts[key] = current + 1;
            else
                counts[key] = 1;
        }

        static void AppendOwnerSummary(StringBuilder builder)
        {
            builder.AppendLine("Owner requests:");
            if (s_ownerRequestCounts.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            foreach (var pair in s_ownerRequestCounts)
            {
                s_ownerWaitCounts.TryGetValue(pair.Key, out int waitedCount);
                builder.AppendLine($"- subset={pair.Key} requests={pair.Value} waitedOnExisting={waitedCount}");
            }
        }

        static void AppendCountSummary(StringBuilder builder, string heading, Dictionary<string, int> counts, string label)
        {
            builder.AppendLine(heading + ":");
            if (counts.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            foreach (var pair in counts)
                builder.AppendLine($"- {pair.Key} {label}={pair.Value}");
        }

        static string GetFaultKey(FaultKind kind)
        {
            return kind switch
            {
                FaultKind.ManifestDownload => "remote_content_verification_fail_manifest_download",
                FaultKind.AddressablesInitialization => "remote_content_verification_fail_addressables_init",
                FaultKind.T1GameplayDownload => "remote_content_verification_fail_t1_download",
                FaultKind.PortraitDownload => "remote_content_verification_fail_portrait_download",
                FaultKind.EnvironmentDownload => "remote_content_verification_fail_environment_download",
                _ => "remote_content_verification_unknown",
            };
        }

        static string GetFaultLabel(FaultKind kind)
        {
            return kind switch
            {
                FaultKind.ManifestDownload => "manifest download",
                FaultKind.AddressablesInitialization => "Addressables initialization",
                FaultKind.T1GameplayDownload => "T1 gameplay download",
                FaultKind.PortraitDownload => "portrait download",
                FaultKind.EnvironmentDownload => "environment download",
                _ => "unknown",
            };
        }
    }
}
