// SetupCmdBarQueueUI.cs — One-shot editor script to upgrade the CmdBar in Game_ML.
// Run via: Castle Defender → Setup → Setup CmdBar Queue UI
//
// What it does:
//   1. Deletes Btn_Slow, Btn_Normal, Btn_Fast (rate buttons — server drain is fixed-speed)
//   2. Adds a "QueueCount" TMP_Text badge to each unit button (bottom-left corner)
//   3. Creates a "QueueDrainBar" filled Image at the bottom of CmdBar
//   4. Wires QueueCountLabels[] and QueueDrainBar on the CmdBar component

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using CastleDefender.UI;

public static class SetupCmdBarQueueUI
{
    [MenuItem("Castle Defender/Setup/Setup CmdBar Queue UI")]
    public static void Run()
    {
        // ── Find CmdBar ───────────────────────────────────────────────────────
        var cmdBarGO = GameObject.Find("Canvas/CmdBar");
        if (cmdBarGO == null)
        {
            Debug.LogError("[SetupCmdBarQueueUI] Could not find Canvas/CmdBar in scene.");
            return;
        }

        var cmdBar = cmdBarGO.GetComponent<CmdBar>();
        if (cmdBar == null)
        {
            Debug.LogError("[SetupCmdBarQueueUI] CmdBar component not found.");
            return;
        }

        // ── 1. Delete rate buttons ────────────────────────────────────────────
        string[] rateButtonNames = { "Btn_Slow", "Btn_Normal", "Btn_Fast" };
        foreach (var btnName in rateButtonNames)
        {
            var t = cmdBarGO.transform.Find(btnName);
            if (t != null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                Debug.Log($"[SetupCmdBarQueueUI] Deleted {btnName}");
            }
            else
            {
                Debug.Log($"[SetupCmdBarQueueUI] {btnName} not found (already removed?)");
            }
        }

        // ── 2. Add QueueCount label to each unit button ───────────────────────
        // Unit buttons are named Btn_Unit0 .. Btn_Unit4
        var queueLabels = new TMP_Text[5];

        for (int i = 0; i < 5; i++)
        {
            var unitBtnT = cmdBarGO.transform.Find($"Btn_Unit{i}");
            if (unitBtnT == null)
            {
                Debug.LogWarning($"[SetupCmdBarQueueUI] Btn_Unit{i} not found — skipping.");
                continue;
            }

            // Remove existing QueueCount if present (idempotent re-run)
            var existing = unitBtnT.Find("QueueCount");
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            // Create QueueCount child
            var labelGO = new GameObject("QueueCount");
            Undo.RegisterCreatedObjectUndo(labelGO, "Create QueueCount");
            labelGO.transform.SetParent(unitBtnT, false);

            var rt = labelGO.AddComponent<RectTransform>();
            // Anchor: bottom-left corner of the button
            rt.anchorMin        = new Vector2(0f, 0f);
            rt.anchorMax        = new Vector2(0f, 0f);
            rt.pivot            = new Vector2(0f, 0f);
            rt.anchoredPosition = new Vector2(3f, 3f);
            rt.sizeDelta        = new Vector2(38f, 16f);

            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text      = "";
            tmp.fontSize  = 11f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color     = new Color(1f, 0.85f, 0.2f); // warm yellow
            tmp.alignment = TextAlignmentOptions.BottomLeft;
            tmp.raycastTarget = false;

            // Start hidden — CmdBar.HandleQueueUpdate will activate it when count > 0
            labelGO.SetActive(false);

            queueLabels[i] = tmp;
            Debug.Log($"[SetupCmdBarQueueUI] Created QueueCount on Btn_Unit{i}");
        }

        // ── 3. Create QueueDrainBar ───────────────────────────────────────────
        // Remove existing if present (idempotent)
        var existingBar = cmdBarGO.transform.Find("QueueDrainBar");
        if (existingBar != null)
            Undo.DestroyObjectImmediate(existingBar.gameObject);

        var barGO = new GameObject("QueueDrainBar");
        Undo.RegisterCreatedObjectUndo(barGO, "Create QueueDrainBar");
        barGO.transform.SetParent(cmdBarGO.transform, false);

        // Background track (dark)
        var trackGO = new GameObject("Track");
        Undo.RegisterCreatedObjectUndo(trackGO, "Create QueueDrainBar Track");
        trackGO.transform.SetParent(barGO.transform, false);
        var trackRT = trackGO.AddComponent<RectTransform>();
        trackRT.anchorMin        = new Vector2(0f, 0f);
        trackRT.anchorMax        = new Vector2(1f, 1f);
        trackRT.pivot            = new Vector2(0.5f, 0.5f);
        trackRT.anchoredPosition = Vector2.zero;
        trackRT.sizeDelta        = Vector2.zero;
        var trackImg = trackGO.AddComponent<Image>();
        trackImg.color        = new Color(0.1f, 0.1f, 0.12f, 0.8f);
        trackImg.raycastTarget = false;

        // Fill bar — this is the one CmdBar drives
        var fillGO = new GameObject("Fill");
        Undo.RegisterCreatedObjectUndo(fillGO, "Create QueueDrainBar Fill");
        fillGO.transform.SetParent(barGO.transform, false);
        var fillRT = fillGO.AddComponent<RectTransform>();
        fillRT.anchorMin        = new Vector2(0f, 0f);
        fillRT.anchorMax        = new Vector2(1f, 1f);
        fillRT.pivot            = new Vector2(0.5f, 0.5f);
        fillRT.anchoredPosition = Vector2.zero;
        fillRT.sizeDelta        = Vector2.zero;
        var fillImg = fillGO.AddComponent<Image>();
        fillImg.color        = new Color(0.2f, 0.75f, 0.6f, 1f); // teal, matches AutoOn
        fillImg.type         = Image.Type.Filled;
        fillImg.fillMethod   = Image.FillMethod.Horizontal;
        fillImg.fillOrigin   = (int)Image.OriginHorizontal.Left;
        fillImg.fillAmount   = 0f;
        fillImg.raycastTarget = false;

        // Bar RectTransform — stretch across full width of CmdBar, 5px tall at bottom
        var barRT = barGO.GetComponent<RectTransform>();
        if (barRT == null) barRT = barGO.AddComponent<RectTransform>();
        barRT.anchorMin        = new Vector2(0f, 0f);
        barRT.anchorMax        = new Vector2(1f, 0f);
        barRT.pivot            = new Vector2(0.5f, 0f);
        barRT.anchoredPosition = new Vector2(0f, 0f);
        barRT.sizeDelta        = new Vector2(0f, 5f);

        // Ignore layout so it overlays the CmdBar bottom edge
        var le = barGO.AddComponent<LayoutElement>();
        le.ignoreLayout = true;

        Debug.Log("[SetupCmdBarQueueUI] Created QueueDrainBar");

        // ── 4. Wire CmdBar component references ───────────────────────────────
        var so = new SerializedObject(cmdBar);

        // QueueCountLabels array
        var labelsProp = so.FindProperty("QueueCountLabels");
        labelsProp.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            if (queueLabels[i] != null)
                labelsProp.GetArrayElementAtIndex(i).objectReferenceValue = queueLabels[i];
        }

        // QueueDrainBar — wire the Fill image (the one with fillAmount driven at runtime)
        var drainProp = so.FindProperty("QueueDrainBar");
        drainProp.objectReferenceValue = fillImg;

        so.ApplyModifiedProperties();
        Debug.Log("[SetupCmdBarQueueUI] Wired QueueCountLabels and QueueDrainBar on CmdBar.");

        // ── 5. Mark scene dirty and save ─────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[SetupCmdBarQueueUI] Done. Save the scene to persist changes.");
    }
}
