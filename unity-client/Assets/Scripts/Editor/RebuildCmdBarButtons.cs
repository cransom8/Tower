// RebuildCmdBarButtons.cs — Rebuilds CmdBar unit buttons with embedded AUTO strips.
// Run via: Castle Defender → Setup → Rebuild CmdBar Buttons
//
// Each unit button gets:
//   • Kept: main button image (icon) + Label (name/cost)
//   • Removed: old AutoBadge dot
//   • Added: AutoStrip (Button) at bottom with AutoLabel (TMP_Text "AUTO"/"AUTO ✓")
//   • Kept: QueueCount badge (top-right)
//
// Also removes the old standalone BtnAutoToggle and QueueDrainBar (recreates both).
// Wires: UnitButtons, AutoToggleButtons, AutoToggleTxts, QueueCountLabels, QueueDrainBar.

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using CastleDefender.UI;

public static class RebuildCmdBarButtons
{
    [MenuItem("Castle Defender/Setup/Rebuild CmdBar Buttons")]
    public static void Run()
    {
        var cmdBarGO = GameObject.Find("Canvas/CmdBar");
        if (cmdBarGO == null) { Debug.LogError("[RebuildCmdBar] Canvas/CmdBar not found."); return; }
        var cmdBar = cmdBarGO.GetComponent<CmdBar>();
        if (cmdBar == null) { Debug.LogError("[RebuildCmdBar] CmdBar component not found."); return; }

        // ── Colors ────────────────────────────────────────────────────────────
        var colBtnBg   = new Color(0.18f, 0.15f, 0.12f, 1f);
        var colAutoOff = new Color(0.13f, 0.11f, 0.09f, 0.95f);
        var colText    = new Color(0.85f, 0.78f, 0.65f, 1f);   // warm cream

        // ── 1. Remove old standalone AUTO toggle ──────────────────────────────
        foreach (string name in new[] { "Btn_AutoToggle", "Btn_Slow", "Btn_Normal", "Btn_Fast" })
        {
            var t = cmdBarGO.transform.Find(name);
            if (t != null) { Undo.DestroyObjectImmediate(t.gameObject); Debug.Log($"[RebuildCmdBar] Removed {name}"); }
        }

        // ── 2. Remove old QueueDrainBar (will recreate) ───────────────────────
        var oldBar = cmdBarGO.transform.Find("QueueDrainBar");
        if (oldBar != null) Undo.DestroyObjectImmediate(oldBar.gameObject);

        // ── 3. Rebuild each unit button ───────────────────────────────────────
        string[] unitNames = { "Btn_Unit0", "Btn_Unit1", "Btn_Unit2", "Btn_Unit3", "Btn_Unit4" };

        var autoToggleButtons = new Button[unitNames.Length];
        var autoToggleTxts    = new TMP_Text[unitNames.Length];
        var queueCountLabels  = new TMP_Text[unitNames.Length];
        var unitButtons       = new Button[unitNames.Length];

        for (int i = 0; i < unitNames.Length; i++)
        {
            var btnT = cmdBarGO.transform.Find(unitNames[i]);
            if (btnT == null) { Debug.LogWarning($"[RebuildCmdBar] {unitNames[i]} not found — skipping."); continue; }
            var btnGO = btnT.gameObject;

            // Collect the main Button
            unitButtons[i] = btnGO.GetComponent<Button>();

            // Remove old AutoBadge and QueueCount (will recreate)
            foreach (string child in new[] { "AutoBadge", "QueueCount", "AutoStrip" })
            {
                var c = btnT.Find(child);
                if (c != null) Undo.DestroyObjectImmediate(c.gameObject);
            }

            // ── Label ─────────────────────────────────────────────────────────
            // Keep existing "Label" TMP_Text but reposition to upper-center
            var labelT = btnT.Find("Label");
            if (labelT != null)
            {
                var lrt = labelT.GetComponent<RectTransform>();
                lrt.anchorMin        = new Vector2(0f, 1f);  // top stretch
                lrt.anchorMax        = new Vector2(1f, 1f);
                lrt.pivot            = new Vector2(0.5f, 1f);
                lrt.anchoredPosition = new Vector2(0f, -2f);
                lrt.sizeDelta        = new Vector2(0f, 20f);

                var lbl = labelT.GetComponent<TMP_Text>();
                if (lbl != null)
                {
                    lbl.fontSize  = 9f;
                    lbl.color     = colText;
                    lbl.alignment = TextAlignmentOptions.Top;
                }
            }

            // ── QueueCount badge ───────────────────────────────────────────────
            var qcGO = new GameObject("QueueCount");
            Undo.RegisterCreatedObjectUndo(qcGO, "Create QueueCount");
            qcGO.transform.SetParent(btnT, false);
            var qcRT = qcGO.AddComponent<RectTransform>();
            qcRT.anchorMin        = new Vector2(1f, 1f);
            qcRT.anchorMax        = new Vector2(1f, 1f);
            qcRT.pivot            = new Vector2(1f, 1f);
            qcRT.anchoredPosition = new Vector2(-2f, -2f);
            qcRT.sizeDelta        = new Vector2(26f, 14f);
            var qcTmp = qcGO.AddComponent<TextMeshProUGUI>();
            qcTmp.text            = "";
            qcTmp.fontSize        = 9f;
            qcTmp.fontStyle       = FontStyles.Bold;
            qcTmp.color           = new Color(1f, 0.85f, 0.25f, 1f); // yellow
            qcTmp.alignment       = TextAlignmentOptions.TopRight;
            qcTmp.raycastTarget   = false;
            qcGO.SetActive(false);
            queueCountLabels[i]   = qcTmp;

            // ── AutoStrip (button within the button) ──────────────────────────
            var stripGO = new GameObject("AutoStrip");
            Undo.RegisterCreatedObjectUndo(stripGO, "Create AutoStrip");
            stripGO.transform.SetParent(btnT, false);

            var stripRT = stripGO.AddComponent<RectTransform>();
            stripRT.anchorMin        = new Vector2(0f, 0f);   // bottom stretch
            stripRT.anchorMax        = new Vector2(1f, 0f);
            stripRT.pivot            = new Vector2(0.5f, 0f);
            stripRT.anchoredPosition = new Vector2(0f, 0f);
            stripRT.sizeDelta        = new Vector2(0f, 15f);

            var stripImg = stripGO.AddComponent<Image>();
            stripImg.color         = colAutoOff;
            stripImg.raycastTarget = true;

            var stripBtn = stripGO.AddComponent<Button>();
            stripBtn.targetGraphic = stripImg;
            // Subtle highlight on hover — slightly lighter than bg
            var cols = stripBtn.colors;
            cols.normalColor      = colAutoOff;
            cols.highlightedColor = new Color(0.22f, 0.19f, 0.16f, 0.95f);
            cols.pressedColor     = new Color(0.30f, 0.26f, 0.20f, 0.95f);
            cols.selectedColor    = colAutoOff;
            stripBtn.colors       = cols;

            autoToggleButtons[i] = stripBtn;

            // AutoLabel inside strip
            var lblGO = new GameObject("AutoLabel");
            Undo.RegisterCreatedObjectUndo(lblGO, "Create AutoLabel");
            lblGO.transform.SetParent(stripGO.transform, false);
            var lblRT = lblGO.AddComponent<RectTransform>();
            lblRT.anchorMin        = Vector2.zero;
            lblRT.anchorMax        = Vector2.one;
            lblRT.pivot            = new Vector2(0.5f, 0.5f);
            lblRT.anchoredPosition = Vector2.zero;
            lblRT.sizeDelta        = Vector2.zero;
            var lblTmp = lblGO.AddComponent<TextMeshProUGUI>();
            lblTmp.text          = "AUTO";
            lblTmp.fontSize      = 8f;
            lblTmp.fontStyle     = FontStyles.Bold;
            lblTmp.color         = new Color(0.7f, 0.65f, 0.58f, 1f);
            lblTmp.alignment     = TextAlignmentOptions.Center;
            lblTmp.raycastTarget = false;

            autoToggleTxts[i] = lblTmp;

            Debug.Log($"[RebuildCmdBar] Rebuilt {unitNames[i]}");
        }

        // ── 4. Create QueueDrainBar ───────────────────────────────────────────
        var barGO = new GameObject("QueueDrainBar");
        Undo.RegisterCreatedObjectUndo(barGO, "Create QueueDrainBar");
        barGO.transform.SetParent(cmdBarGO.transform, false);
        // Ignore layout — overlays at the bottom of CmdBar
        var le = barGO.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        var barRT = barGO.GetComponent<RectTransform>();
        barRT.anchorMin        = new Vector2(0f, 0f);
        barRT.anchorMax        = new Vector2(1f, 0f);
        barRT.pivot            = new Vector2(0.5f, 0f);
        barRT.anchoredPosition = Vector2.zero;
        barRT.sizeDelta        = new Vector2(0f, 3f);

        // Track (dark bg)
        var trackGO = new GameObject("Track");
        Undo.RegisterCreatedObjectUndo(trackGO, "Create Track");
        trackGO.transform.SetParent(barGO.transform, false);
        var trackRT = trackGO.AddComponent<RectTransform>();
        trackRT.anchorMin = Vector2.zero; trackRT.anchorMax = Vector2.one;
        trackRT.pivot = new Vector2(0.5f, 0.5f);
        trackRT.anchoredPosition = Vector2.zero; trackRT.sizeDelta = Vector2.zero;
        var trackImg = trackGO.AddComponent<Image>();
        trackImg.color = new Color(0.08f, 0.07f, 0.06f, 0.85f);
        trackImg.raycastTarget = false;

        // Fill (teal, driven at runtime)
        var fillGO = new GameObject("Fill");
        Undo.RegisterCreatedObjectUndo(fillGO, "Create Fill");
        fillGO.transform.SetParent(barGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
        fillRT.pivot = new Vector2(0.5f, 0.5f);
        fillRT.anchoredPosition = Vector2.zero; fillRT.sizeDelta = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color      = new Color(0.2f, 0.75f, 0.6f, 1f);
        fillImg.type       = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount = 0f;
        fillImg.raycastTarget = false;

        // ── 5. Wire CmdBar component ──────────────────────────────────────────
        var so = new SerializedObject(cmdBar);

        SetObjArray(so, "UnitButtons",       unitButtons);
        SetObjArray(so, "AutoToggleButtons", autoToggleButtons);
        SetObjArray(so, "AutoToggleTxts",    autoToggleTxts);
        SetObjArray(so, "QueueCountLabels",  queueCountLabels);

        // Clear old fields that no longer exist on the component
        // (BtnAutoToggle / TxtAutoToggle / AutoBadges are removed from CmdBar.cs)

        var drainProp = so.FindProperty("QueueDrainBar");
        if (drainProp != null) drainProp.objectReferenceValue = fillImg;

        so.ApplyModifiedProperties();
        Debug.Log("[RebuildCmdBar] CmdBar wired.");

        // ── 6. Mark dirty & save ──────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[RebuildCmdBar] Done — save the scene to persist.");
    }

    static void SetObjArray<T>(SerializedObject so, string propName, T[] items) where T : Object
    {
        var prop = so.FindProperty(propName);
        if (prop == null) { Debug.LogWarning($"[RebuildCmdBar] Property '{propName}' not found on CmdBar."); return; }
        prop.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }
}
