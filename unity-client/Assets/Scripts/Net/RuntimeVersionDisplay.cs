using UnityEngine;

namespace CastleDefender.Net
{
    public static class RuntimeVersionDisplay
    {
        public static string VersionNumber
        {
            get
            {
                string version = Application.version;
                return string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();
            }
        }

        public static string VersionLabel => $"Version {VersionNumber}";
    }
}
