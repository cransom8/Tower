#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using CastleDefender.UI;

/// <summary>
/// Builds the LaneViewBar UI (4 lane-pan buttons, top-left) into the active scene's Canvas.
/// Menu: Castle Defender / Setup / Build Lane View Bar
/// </summary>
public static class BuildLaneViewBar
{
    const float BTN_SIZE    = 44f;
    const float BTN_SPACING = 6f;
    const float BAR_PAD_X   = 12f;
    const float BAR_PAD_Y   = 12f;

    static readonly string[] LaneLabels = { "R", "G", "B", "Gr" };
    static readonly Color[] LaneTints =
    {
        new Color(0.85f, 0.20f, 0.20f),
        new Color(0.85f, 0.70f, 0.10f),
        new Color(0.20f, 0.45f, 0.85f),
        new Color(0.20f, 0.65f, 0.25f),
    };

    [MenuItem("Castle Defender/Setup/Build Lane View Bar")]
    public static void Build()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[BuildLaneViewBar] No Canvas in scene."); return; }

        // Remove existing LaneViewBar if present
        var existing = canvas.transform.Find("LaneViewBar");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
            Debug.Log("[BuildLaneViewBar] Removed existing LaneViewBar.");
        }

        // ── Root bar ──────────────────────────────────────────────────────────
        var barGO  = new GameObject("LaneViewBar");
        Undo.RegisterCreatedObjectUndo(barGO, "Create LaneViewBar");
        barGO.transform.SetParent(canvas.transform, false);

        var barRT = barGO.AddComponent<RectTransform>();
        float barW = LaneLabels.Length * BTN_SIZE + (LaneLabels.Length - 1) * BTN_SPACING;
        float barH = BTN_SIZE;
        barRT.anchorMin        = new Vector2(0, 1);
        barRT.anchorMax        = new Vector2(0, 1);
        barRT.pivot            = new Vector2(0, 1);
        barRT.anchoredPosition = new Vector2(BAR_PAD_X, -BAR_PAD_Y);
        barRT.sizeDelta        = new Vector2(barW, barH);

        var hlg = barGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing            = BTN_SPACING;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        hlg.childAlignment     = TextAnchor.MiddleLeft;
        hlg.padding            = new RectOffset(0, 0, 0, 0);

        // ── Lane buttons ──────────────────────────────────────────────────────
        var buttons = new Button[LaneLabels.Length];
        var labels  = new TMP_Text[LaneLabels.Length];

        for (int i = 0; i < LaneLabels.Length; i++)
        {
            // Button GO
            var btnGO = new GameObject($"Btn_Lane{i}");
            btnGO.transform.SetParent(barGO.transform, false);

            var rt = btnGO.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(BTN_SIZE, BTN_SIZE);

            var img = btnGO.AddComponent<Image>();
            img.color = LaneTints[i];

            var btn = btnGO.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor     = new Color(0.7f, 0.7f, 0.7f);
            btn.colors = colors;

            buttons[i] = btn;

            // Label child
            var lblGO = new GameObject("Label");
            lblGO.transform.SetParent(btnGO.transform, false);

            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin = Vector2.zero;
            lblRT.anchorMax = Vector2.one;
            lblRT.offsetMin = Vector2.zero;
            lblRT.offsetMax = Vector2.zero;

            var txt = lblGO.AddComponent<TextMeshProUGUI>();
            txt.text      = LaneLabels[i];
            txt.fontSize  = 16f;
            txt.fontStyle = FontStyles.Bold;
            txt.color     = Color.white;
            txt.alignment = TextAlignmentOptions.Center;

            labels[i] = txt;
        }

        // ── Wire LaneViewBar component ────────────────────────────────────────
        var bar = barGO.AddComponent<LaneViewBar>();
        bar.LaneButtons = buttons;
        bar.LaneLabels  = labels;

        EditorUtility.SetDirty(barGO);
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[BuildLaneViewBar] Done — added to Canvas in '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}'.");
    }
}
#endif
