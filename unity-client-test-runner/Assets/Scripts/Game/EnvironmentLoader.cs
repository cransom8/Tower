using System.Collections;
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

    GameObject _instance;
    Canvas _failureCanvas;

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

        RemoteContentVerification.RecordAwaitOnly(nameof(EnvironmentLoader), "t1.environment");

        float deadline = Time.realtimeSinceStartup + Mathf.Max(1f, readinessTimeoutSeconds);
        GameObject prefab = null;
        while (!remoteContent.TryGetLoadedEnvironmentPrefab(address, out prefab))
        {
            if (!string.IsNullOrWhiteSpace(remoteContent.LastError))
            {
                HardFail($"{failureTitle} {remoteContent.LastError}");
                yield break;
            }

            if (Time.realtimeSinceStartup >= deadline)
            {
                HardFail(
                    $"{failureTitle} Expected environment '{address}' to be ready before the match scene finished loading.");
                yield break;
            }

            yield return null;
        }

        if (_instance != null)
            yield break;

        var parent = instantiateParent != null ? instantiateParent : transform;
        _instance = Instantiate(prefab, parent, false);
        if (!string.IsNullOrWhiteSpace(instantiatedRootName))
            _instance.name = instantiatedRootName;

        RemoteContentVerification.RecordEvent(
            "environment_instantiated",
            $"address={address} parent={parent.name}");
    }

    void HardFail(string detail)
    {
        Debug.LogError($"[EnvironmentLoader] {detail}");
        EnsureFailureCanvas();
        var text = _failureCanvas.GetComponentInChildren<TextMeshProUGUI>(true);
        if (text != null)
            text.text = detail;
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
