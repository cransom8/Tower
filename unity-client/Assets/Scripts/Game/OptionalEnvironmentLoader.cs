using System.Collections;
using CastleDefender.Net;
using UnityEngine;

public class OptionalEnvironmentLoader : MonoBehaviour
{
    const string DisabledAddressToken = "none";

    [Header("Optional Dressing")]
    public string optionalEnvironmentAddress = RemoteContentManager.GameMlEnvironmentDressingAddress;
    public Transform instantiateParent;
    public string instantiatedRootName = "OptionalEnvironmentDressing";
    public float instantiatedRootScale = 0.2f;

    [Header("Startup Order")]
    public string requiredRootName = "CoreMapCritical";
    public float waitForCriticalTimeoutSeconds = 15f;
    public float loadStartDelaySeconds = 0.25f;

    [Header("Diagnostics")]
    public bool logWarnings = true;

    GameObject _instance;

    void Start()
    {
        StartCoroutine(LoadOptionalEnvironmentWhenReady());
    }

    IEnumerator LoadOptionalEnvironmentWhenReady()
    {
        if (loadStartDelaySeconds > 0f)
            yield return new WaitForSeconds(loadStartDelaySeconds);

        var parent = instantiateParent != null ? instantiateParent : transform;
        if (!string.IsNullOrWhiteSpace(requiredRootName))
        {
            float deadline = Time.realtimeSinceStartup + Mathf.Max(0.5f, waitForCriticalTimeoutSeconds);
            while (parent.Find(requiredRootName) == null)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    if (logWarnings)
                    {
                        Debug.LogWarning(
                            $"[OptionalEnvironmentLoader] Skipping optional dressing because required root '{requiredRootName}' was not found in time.");
                    }

                    yield break;
                }

                yield return null;
            }
        }

        string configuredAddress = optionalEnvironmentAddress != null
            ? optionalEnvironmentAddress.Trim()
            : string.Empty;
        if (IsOptionalDressingDisabled(configuredAddress))
            yield break;

        var remoteContent = RemoteContentManager.EnsureInstance();
        string address = string.IsNullOrWhiteSpace(configuredAddress)
            ? RemoteContentManager.GameMlEnvironmentDressingAddress
            : configuredAddress;

        yield return remoteContent.EnsureEnvironmentReady(
            address,
            requester: nameof(OptionalEnvironmentLoader),
            suppressCatalogWarnings: true);

        if (!remoteContent.TryGetLoadedEnvironmentPrefab(address, out var prefab) || prefab == null)
        {
            bool missingCatalogEntry = !string.IsNullOrWhiteSpace(remoteContent.LastError)
                && remoteContent.LastError.Contains("missing from the active Addressables catalog");

            if (logWarnings && !missingCatalogEntry)
            {
                Debug.LogWarning(
                    $"[OptionalEnvironmentLoader] Optional dressing '{address}' did not load. " +
                    $"Gameplay will continue without it. LastError={remoteContent.LastError}");
            }

            yield break;
        }

        if (_instance != null)
            yield break;

        _instance = Instantiate(prefab, parent, false);
        if (!string.IsNullOrWhiteSpace(instantiatedRootName))
            _instance.name = instantiatedRootName;

        if (instantiatedRootScale > 0f)
            _instance.transform.localScale = Vector3.one * instantiatedRootScale;
    }

    static bool IsOptionalDressingDisabled(string address)
    {
        return string.Equals(address, DisabledAddressToken, System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(address, "disabled", System.StringComparison.OrdinalIgnoreCase)
            || string.Equals(address, "off", System.StringComparison.OrdinalIgnoreCase);
    }
}
