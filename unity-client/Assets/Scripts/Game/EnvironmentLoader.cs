using System;
using System.Collections;
using System.Collections.Generic;
using CastleDefender.Game;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EnvironmentLoader : MonoBehaviour
{
    [Header("Remote Environment")]
    public string environmentAddress = RemoteContentManager.GameMlEnvironmentAddress;
    public Transform instantiateParent;
    public string instantiatedRootName = "RemoteEnvironment";

    [Header("Failure UI")]
    public string failureTitle = "Remote environment failed to load.";
    public float readinessTimeoutSeconds = 10f;
    public float layoutReadinessTimeoutSeconds = 10f;
    public bool returnToLobbyOnFailure = true;
    public float lobbyReturnDelaySeconds = 1.5f;

    GameObject _instance;
    Canvas _failureCanvas;
    bool _returningToLobby;

    void Start()
    {
        StartCoroutine(InstantiateEnvironmentWhenReady());
    }

    IEnumerator InstantiateEnvironmentWhenReady()
    {
        var remoteContent = RemoteContentManager.EnsureInstance();
        string address = string.IsNullOrWhiteSpace(environmentAddress)
            ? RemoteContentManager.GameMlEnvironmentAddress
            : environmentAddress.Trim();
        string expectedContentHash = ResolveExpectedEnvironmentContentHash();

        RemoteContentVerification.RecordAwaitOnly(nameof(EnvironmentLoader), "t1.environment");
        GameObject prefab = null;
        if (!remoteContent.TryGetLoadedEnvironmentPrefab(address, out prefab))
        {
            yield return remoteContent.EnsureEnvironmentReady(
                address,
                expectedContentHash,
                requester: nameof(EnvironmentLoader));

            if (!remoteContent.TryGetLoadedEnvironmentPrefab(address, out prefab))
            {
                HardFail(
                    string.IsNullOrWhiteSpace(remoteContent.LastError)
                        ? $"{failureTitle} Expected environment '{address}' to be ready before the match scene finished loading."
                        : $"{failureTitle} {remoteContent.LastError}");
                yield break;
            }
        }

        if (_instance != null)
            yield break;

        var parent = instantiateParent != null ? instantiateParent : transform;
        _instance = Instantiate(prefab, parent, false);
        if (!string.IsNullOrWhiteSpace(instantiatedRootName))
            _instance.name = instantiatedRootName;

        yield return WaitForBattlefieldLayoutAndApply(address);
        if (_instance == null)
            yield break;

        RemoteContentVerification.RecordEvent(
            "environment_instantiated",
            $"address={address} parent={parent.name}");
    }

    IEnumerator WaitForBattlefieldLayoutAndApply(string address)
    {
        if (ShouldUseAuthoredEnvironmentPreview())
        {
            Debug.LogWarning(
                $"[EnvironmentLoader] No live match bootstrap detected while loading '{address}'. " +
                "Using the authored environment preview positions in editor play mode.");
            RefreshBattlefieldCamera();
            yield break;
        }

        float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, layoutReadinessTimeoutSeconds);
        SnapshotApplier snapshotApplier = null;
        while (snapshotApplier == null && Time.realtimeSinceStartup < deadline)
        {
            snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null)
                break;

            yield return null;
        }

        if (snapshotApplier == null)
        {
            HardFail(
                $"{failureTitle} SnapshotApplier never became available while waiting for battlefield layout for environment '{address}'.");
            yield break;
        }

        MLBattlefieldLayout resolvedLayout = snapshotApplier.GetBattlefieldLayout();
        bool layoutReady = resolvedLayout != null;

        void HandleLayoutReady(MLBattlefieldLayout layout)
        {
            resolvedLayout = layout;
            layoutReady = layout != null;
        }

        snapshotApplier.OnBattlefieldLayoutReady += HandleLayoutReady;
        try
        {
            while (!layoutReady)
            {
                resolvedLayout = snapshotApplier.GetBattlefieldLayout();
                layoutReady = resolvedLayout != null;
                if (layoutReady)
                    break;

                if (Time.realtimeSinceStartup >= deadline)
                {
                    HardFail(
                        $"{failureTitle} {BuildLayoutWaitFailureDetail(snapshotApplier, address)}");
                    yield break;
                }

                yield return null;
            }
        }
        finally
        {
            if (snapshotApplier != null)
                snapshotApplier.OnBattlefieldLayoutReady -= HandleLayoutReady;
        }

        if (!ValidateEnvironmentContentHash(address, resolvedLayout, out string hashFailureReason))
        {
            HardFail($"{failureTitle} {hashFailureReason}");
            yield break;
        }

        if (!TryApplyBattlefieldLayout(_instance, resolvedLayout, out string failureReason))
        {
            HardFail($"{failureTitle} {failureReason}");
            yield break;
        }

        RemoteContentVerification.RecordEvent(
            "environment_layout_applied",
            $"address={address} layout={resolvedLayout?.layoutId ?? "<null>"}");
        RefreshBattlefieldCamera();
    }

    bool ValidateEnvironmentContentHash(string address, MLBattlefieldLayout layout, out string failureReason)
    {
        failureReason = null;

        string expectedHash = layout?.contentHash?.Trim();
        if (string.IsNullOrWhiteSpace(expectedHash))
            return true;

        var remoteContent = RemoteContentManager.Instance;
        if (remoteContent == null)
        {
            failureReason =
                $"RemoteContentManager is unavailable while validating environment '{address}' against authoritative layout hash '{expectedHash}'.";
            return false;
        }

        if (remoteContent.ValidateEnvironmentContentHash(address, expectedHash, out string error))
            return true;

        failureReason = error;
        return false;
    }

    void HardFail(string detail)
    {
        Debug.LogError($"[EnvironmentLoader] {detail}");
        if (_instance != null)
        {
            Destroy(_instance);
            _instance = null;
        }
        EnsureFailureCanvas();
        var text = _failureCanvas.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.text = detail;

        if (returnToLobbyOnFailure && !_returningToLobby && isActiveAndEnabled)
            StartCoroutine(ReturnToLobbyAfterFailure());
    }

    IEnumerator ReturnToLobbyAfterFailure()
    {
        _returningToLobby = true;
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, lobbyReturnDelaySeconds));
        LoadingScreen.LoadScene("Lobby");
    }

    static bool TryApplyBattlefieldLayout(GameObject environmentRoot, MLBattlefieldLayout layout, out string failureReason)
    {
        failureReason = null;
        if (environmentRoot == null)
        {
            failureReason = "Environment instance was destroyed before layout binding completed.";
            return false;
        }

        if (layout == null)
        {
            failureReason = "Missing ML battlefieldLayout.";
            return false;
        }

        if (layout.lanes == null || layout.lanes.Length == 0)
        {
            failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' has no lanes.";
            return false;
        }

        var laneByKey = new Dictionary<string, MLBattlefieldLayoutLane>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < layout.lanes.Length; i++)
        {
            var lane = layout.lanes[i];
            if (lane == null)
                continue;

            string laneKey = NormalizeLaneKey(!string.IsNullOrWhiteSpace(lane.laneKey) ? lane.laneKey : lane.slotColor);
            if (string.IsNullOrWhiteSpace(laneKey))
            {
                failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' contains a lane with no normalized key.";
                return false;
            }

            laneByKey[laneKey] = lane;
        }

        var matchedPadKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var padAnchors = environmentRoot.GetComponentsInChildren<FortressPadAnchor>(true);
        for (int i = 0; i < padAnchors.Length; i++)
        {
            var anchor = padAnchors[i];
            if (anchor == null)
                continue;

            string laneKey = NormalizeLaneKey(FortressLaneResolver.ResolveLaneKey(anchor.transform, anchor.AnchorLaneColor));
            if (string.IsNullOrWhiteSpace(laneKey))
            {
                failureReason = $"Authored FortressPadAnchor '{anchor.PadId}' cannot resolve a lane key.";
                return false;
            }

            if (!laneByKey.TryGetValue(laneKey, out var lane))
            {
                anchor.gameObject.SetActive(false);
                continue;
            }

            if (!anchor.gameObject.activeSelf)
                anchor.gameObject.SetActive(true);

            var pad = FindFortressPad(lane, anchor.PadId);
            if (pad == null)
            {
                failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' is missing fortress pad '{anchor.PadId}' for lane '{laneKey}'.";
                return false;
            }

            if (!TryResolveWorldPoint(pad.world, anchor.transform.position.y, out Vector3 worldPosition))
            {
                failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' pad '{anchor.PadId}' for lane '{laneKey}' has no valid world position.";
                return false;
            }

            anchor.transform.position = worldPosition;
            matchedPadKeys.Add(BuildPadKey(laneKey, anchor.PadId));
        }

        for (int i = 0; i < layout.lanes.Length; i++)
        {
            var lane = layout.lanes[i];
            if (lane == null || lane.fortressPads == null)
                continue;

            string laneKey = NormalizeLaneKey(!string.IsNullOrWhiteSpace(lane.laneKey) ? lane.laneKey : lane.slotColor);
            for (int padIndex = 0; padIndex < lane.fortressPads.Length; padIndex++)
            {
                var pad = lane.fortressPads[padIndex];
                if (pad == null || string.IsNullOrWhiteSpace(pad.padId))
                    continue;

                if (!matchedPadKeys.Contains(BuildPadKey(laneKey, pad.padId)))
                {
                    failureReason = $"Environment prefab is missing authored FortressPadAnchor '{pad.padId}' for lane '{laneKey}'.";
                    return false;
                }
            }
        }

        var matchedBarracksKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var barracksSites = environmentRoot.GetComponentsInChildren<BarracksSiteView>(true);
        for (int i = 0; i < barracksSites.Length; i++)
        {
            var site = barracksSites[i];
            if (site == null)
                continue;

            string laneKey = NormalizeLaneKey(FortressLaneResolver.ResolveLaneKey(site.transform, site.laneColor));
            if (string.IsNullOrWhiteSpace(laneKey))
            {
                failureReason = $"Authored BarracksSiteView '{site.BarracksId}' cannot resolve a lane key.";
                return false;
            }

            if (!laneByKey.TryGetValue(laneKey, out var lane))
            {
                site.gameObject.SetActive(false);
                continue;
            }

            if (!site.gameObject.activeSelf)
                site.gameObject.SetActive(true);

            var barracks = FindBarracksSite(lane, site.BarracksId);
            if (barracks == null)
            {
                failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' is missing barracks '{site.BarracksId}' for lane '{laneKey}'.";
                return false;
            }

            if (!TryResolveWorldPoint(barracks.world, site.transform.position.y, out Vector3 worldPosition))
            {
                failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' barracks '{site.BarracksId}' for lane '{laneKey}' has no valid world position.";
                return false;
            }

            site.transform.position = worldPosition;
            matchedBarracksKeys.Add(BuildBarracksKey(laneKey, site.BarracksId));
        }

        for (int i = 0; i < layout.lanes.Length; i++)
        {
            var lane = layout.lanes[i];
            if (lane == null || lane.barracksSites == null)
                continue;

            string laneKey = NormalizeLaneKey(!string.IsNullOrWhiteSpace(lane.laneKey) ? lane.laneKey : lane.slotColor);
            for (int siteIndex = 0; siteIndex < lane.barracksSites.Length; siteIndex++)
            {
                var site = lane.barracksSites[siteIndex];
                if (site == null || string.IsNullOrWhiteSpace(site.barracksId))
                    continue;

                if (!matchedBarracksKeys.Contains(BuildBarracksKey(laneKey, site.barracksId)))
                {
                    failureReason = $"Environment prefab is missing authored BarracksSiteView '{site.barracksId}' for lane '{laneKey}'.";
                    return false;
                }
            }
        }

        var waveAnchors = environmentRoot.GetComponentsInChildren<WaveSpawnAnchor>(true);
        if (waveAnchors.Length > 0)
        {
            if (!TryFindRouteNode(layout, "M", out var mineNode))
            {
                failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' is missing route node 'M' for wave center placement.";
                return false;
            }

            for (int i = 0; i < waveAnchors.Length; i++)
            {
                var anchor = waveAnchors[i];
                if (anchor == null || !string.Equals(anchor.AnchorId, "mine_center", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryResolveWorldPoint(mineNode.world, anchor.transform.position.y, out Vector3 worldPosition))
                {
                    failureReason = $"Battlefield layout '{layout.layoutId ?? "<null>"}' route node 'M' has no valid world position.";
                    return false;
                }

                anchor.transform.position = worldPosition;
            }
        }

        return true;
    }

    static MLFortressPadPlacement FindFortressPad(MLBattlefieldLayoutLane lane, string padId)
    {
        if (lane?.fortressPads == null || string.IsNullOrWhiteSpace(padId))
            return null;

        for (int i = 0; i < lane.fortressPads.Length; i++)
        {
            var pad = lane.fortressPads[i];
            if (pad != null && string.Equals(pad.padId, padId, StringComparison.OrdinalIgnoreCase))
                return pad;
        }

        return null;
    }

    static MLBarracksSitePlacement FindBarracksSite(MLBattlefieldLayoutLane lane, string barracksId)
    {
        if (lane?.barracksSites == null || string.IsNullOrWhiteSpace(barracksId))
            return null;

        string normalizedBarracksId = NormalizeBarracksId(barracksId);
        for (int i = 0; i < lane.barracksSites.Length; i++)
        {
            var site = lane.barracksSites[i];
            if (site != null && string.Equals(NormalizeBarracksId(site.barracksId), normalizedBarracksId, StringComparison.OrdinalIgnoreCase))
                return site;
        }

        return null;
    }

    static bool TryFindRouteNode(MLBattlefieldLayout layout, string nodeId, out MLBattlefieldRouteNode routeNode)
    {
        routeNode = null;
        if (layout?.routeNodes == null || string.IsNullOrWhiteSpace(nodeId))
            return false;

        for (int i = 0; i < layout.routeNodes.Length; i++)
        {
            var candidate = layout.routeNodes[i];
            if (candidate != null && string.Equals(candidate.nodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            {
                routeNode = candidate;
                return true;
            }
        }

        return false;
    }

    static string NormalizeLaneKey(string laneKey)
    {
        return FortressPadAnchor.NormalizeLaneKey(laneKey);
    }

    static string BuildPadKey(string laneKey, string padId)
    {
        return $"{NormalizeLaneKey(laneKey)}:{padId?.Trim().ToLowerInvariant()}";
    }

    static string BuildBarracksKey(string laneKey, string barracksId)
    {
        return $"{NormalizeLaneKey(laneKey)}:{NormalizeBarracksId(barracksId)}";
    }

    static bool TryResolveWorldPoint(MLWorldPoint point, float currentY, out Vector3 worldPosition)
    {
        worldPosition = default;
        if (point == null)
            return false;

        worldPosition = new Vector3(point.x, currentY, point.y);
        return true;
    }

    static string BuildLayoutWaitFailureDetail(SnapshotApplier snapshotApplier, string address)
    {
        if (snapshotApplier == null)
            return $"SnapshotApplier was unavailable while waiting for authoritative battlefield layout for environment '{address}'.";

        bool hasMatchReady = snapshotApplier.LatestMLMatchReady != null;
        bool hasMatchConfig = snapshotApplier.LatestMLMatchConfig != null;
        bool hasLayout = snapshotApplier.HasAuthoritativeBattlefieldLayout();
        bool hasSnapshot = snapshotApplier.LatestML != null;
        int loadoutCount = snapshotApplier.LatestMLMatchConfig?.loadout?.Length ?? 0;
        int laneAssignments = snapshotApplier.LatestMLMatchReady?.laneAssignments?.Length ?? 0;
        string layoutId = snapshotApplier.LatestMLMatchConfig?.battlefieldLayout?.layoutId ?? "<none>";
        return
            $"Expected authoritative battlefield layout for environment '{address}' before gameplay began. " +
            $"hasMatchReady={hasMatchReady} laneAssignments={laneAssignments} hasMatchConfig={hasMatchConfig} " +
            $"loadoutEntries={loadoutCount} hasLayout={hasLayout} layoutId={layoutId} hasFirstSnapshot={hasSnapshot}.";
    }

    static string NormalizeBarracksId(string barracksId)
    {
        if (string.IsNullOrWhiteSpace(barracksId))
            return string.Empty;

        return barracksId.Trim().ToLowerInvariant();
    }

    static string ResolveExpectedEnvironmentContentHash()
    {
        string fromSnapshot = SnapshotApplier.Instance?.LatestMLMatchConfig?.battlefieldLayout?.contentHash;
        if (!string.IsNullOrWhiteSpace(fromSnapshot))
            return fromSnapshot.Trim();

        string fromNetwork = NetworkManager.Instance?.LastMLMatchConfig?.battlefieldLayout?.contentHash;
        return string.IsNullOrWhiteSpace(fromNetwork) ? null : fromNetwork.Trim();
    }

    static bool ShouldUseAuthoredEnvironmentPreview()
    {
#if UNITY_EDITOR
        if (!Application.isEditor)
            return false;

        if (NetworkManager.Instance != null)
            return false;

        return SnapshotApplier.Instance?.HasAuthoritativeBattlefieldLayout() != true;
#else
        return false;
#endif
    }

    void RefreshBattlefieldCamera()
    {
        var gameManager = FindFirstObjectByType<GameManager>(FindObjectsInactive.Include);
        gameManager?.RefreshBattlefieldCamera();
    }

    void EnsureFailureCanvas()
    {
        if (_failureCanvas != null)
            return;

        var canvasObject = new GameObject(
            "EnvironmentLoaderFailureCanvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        _failureCanvas = canvasObject.GetComponent<Canvas>();
        _failureCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _failureCanvas.sortingOrder = short.MaxValue;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(panel.transform, false);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.15f, 0.35f);
        labelRect.anchorMax = new Vector2(0.85f, 0.65f);
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        var tmp = label.GetComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.fontSize = 34f;
        tmp.color = Color.white;
    }
}
