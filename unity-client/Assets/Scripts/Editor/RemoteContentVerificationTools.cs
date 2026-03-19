#if UNITY_EDITOR
using CastleDefender.Net;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class RemoteContentVerificationTools
    {
        [MenuItem("Castle Defender/Remote Content Verification/Clear All Overrides")]
        public static void ClearAllOverrides()
        {
            RemoteContentVerification.ClearFailureCounts();
            RemoteContentVerification.ClearEvidence();
            Debug.Log("[RemoteContentVerificationTools] Cleared verification overrides and evidence.");
        }

        [MenuItem("Castle Defender/Remote Content Verification/Fail Manifest Download Once")]
        public static void FailManifestOnce() => SetFailOnce(RemoteContentVerification.FaultKind.ManifestDownload);

        [MenuItem("Castle Defender/Remote Content Verification/Fail Addressables Init Once")]
        public static void FailAddressablesOnce() => SetFailOnce(RemoteContentVerification.FaultKind.AddressablesInitialization);

        [MenuItem("Castle Defender/Remote Content Verification/Fail T1 Gameplay Download Once")]
        public static void FailT1Once() => SetFailOnce(RemoteContentVerification.FaultKind.T1GameplayDownload);

        [MenuItem("Castle Defender/Remote Content Verification/Fail Portrait Download Once")]
        public static void FailPortraitOnce() => SetFailOnce(RemoteContentVerification.FaultKind.PortraitDownload);

        [MenuItem("Castle Defender/Remote Content Verification/Fail Environment Download Once")]
        public static void FailEnvironmentOnce() => SetFailOnce(RemoteContentVerification.FaultKind.EnvironmentDownload);

        [MenuItem("Castle Defender/Remote Content Verification/Fail Remote Scene Catalog Lookup Once")]
        public static void FailRemoteSceneCatalogLookupOnce() => SetFailOnce(RemoteContentVerification.FaultKind.RemoteSceneCatalogLookup);

        [MenuItem("Castle Defender/Remote Content Verification/Fail Remote Scene Bundle Download Once")]
        public static void FailRemoteSceneBundleDownloadOnce() => SetFailOnce(RemoteContentVerification.FaultKind.RemoteSceneBundleDownload);

        [MenuItem("Castle Defender/Remote Content Verification/Dump Evidence To Console")]
        public static void DumpEvidence()
        {
            Debug.Log("[RemoteContentVerificationTools] Evidence report:\n" + RemoteContentVerification.BuildEvidenceReport());
        }

        static void SetFailOnce(RemoteContentVerification.FaultKind kind)
        {
            RemoteContentVerification.SetFailureCount(kind, 1);
            Debug.Log($"[RemoteContentVerificationTools] {kind} will fail once.");
        }
    }
}
#endif
