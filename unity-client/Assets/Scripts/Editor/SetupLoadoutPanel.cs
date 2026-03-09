// SetupLoadoutPanel.cs — Rebuilds Panel_Loadout in Lobby scene with the unit-picker hierarchy.
// Run via: Castle Defender → Setup → Setup Loadout Panel
//
// Hierarchy created:
//   Panel_Loadout  (LoadoutUI component)
//   ├── Txt_Title
//   ├── Panel_Body  (HorizontalLayoutGroup)
//   │   ├── Panel_Catalog  (ScrollRect + Mask)
//   │   │   └── Viewport → Content  (RectTransform — cards spawned at runtime)
//   │   └── Panel_Preview
//   │       ├── Img_Portrait  (RawImage)
//   │       ├── Txt_PreviewName
//   │       └── Txt_PreviewStats
//   ├── Panel_Slots  (5 slot buttons, HorizontalLayoutGroup)
//   └── Panel_Buttons
//       ├── Btn_Confirm
//       └── Btn_Back
//
// Idempotent — removes existing Panel_Loadout before recreating.

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using CastleDefender.UI;

public static class SetupLoadoutPanel
{
    [MenuItem("Castle Defender/Setup/Setup Loadout Panel")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.name.Contains("Lobby"))
        {
            Debug.LogError("[SetupLoadoutPanel] Open the Lobby scene first.");
            return;
        }

        var canvasGO = GameObject.Find("Canvas");
        if (canvasGO == null) { Debug.LogError("[SetupLoadoutPanel] Canvas not found."); return; }

        // Remove existing (idempotent)
        var existing = canvasGO.transform.Find("Panel_Loadout");
        if (existing != null) { Undo.DestroyObjectImmediate(existing.gameObject); Debug.Log("[SetupLoadoutPanel] Removed existing Panel_Loadout."); }

        // ── Root panel ────────────────────────────────────────────────────────
        var panelGO = new GameObject("Panel_Loadout");
        Undo.RegisterCreatedObjectUndo(panelGO, "Create Panel_Loadout");
        panelGO.transform.SetParent(canvasGO.transform, false);

        var panelRT = panelGO.AddComponent<RectTransform>();
        panelRT.anchorMin = Vector2.zero;
        panelRT.anchorMax = Vector2.one;
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.sizeDelta = Vector2.zero;

        var panelImg = panelGO.AddComponent<Image>();
        panelImg.color = new Color(0.06f, 0.06f, 0.10f, 0.97f);

        var loadoutUI = panelGO.AddComponent<LoadoutUI>();

        // ── Txt_Title ─────────────────────────────────────────────────────────
        MakeTMP(panelGO, "Txt_Title", "Choose Your Units",
            new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
            new Vector2(0f, -20f), new Vector2(0f, 55f), 26f, true);

        // ── Panel_Body (horizontal split) ─────────────────────────────────────
        var bodyGO = new GameObject("Panel_Body");
        Undo.RegisterCreatedObjectUndo(bodyGO, "Create Panel_Body");
        bodyGO.transform.SetParent(panelGO.transform, false);

        var bodyRT = bodyGO.AddComponent<RectTransform>();
        bodyRT.anchorMin = new Vector2(0f, 0.25f);
        bodyRT.anchorMax = new Vector2(1f, 0.90f);
        bodyRT.anchoredPosition = new Vector2(0f, -30f);
        bodyRT.sizeDelta = new Vector2(-40f, 0f);

        var bodyHLG = bodyGO.AddComponent<HorizontalLayoutGroup>();
        bodyHLG.spacing = 16f;
        bodyHLG.childControlWidth  = true;
        bodyHLG.childControlHeight = true;
        bodyHLG.childForceExpandWidth  = false;
        bodyHLG.childForceExpandHeight = true;
        bodyHLG.padding = new RectOffset(20, 20, 0, 0);

        // ── Panel_Catalog (left, scroll view) ─────────────────────────────────
        var catalogGO = new GameObject("Panel_Catalog");
        Undo.RegisterCreatedObjectUndo(catalogGO, "Create Panel_Catalog");
        catalogGO.transform.SetParent(bodyGO.transform, false);

        var catRT = catalogGO.AddComponent<RectTransform>();
        catRT.anchorMin = Vector2.zero;
        catRT.anchorMax = Vector2.one;
        catRT.anchoredPosition = Vector2.zero;
        catRT.sizeDelta = Vector2.zero;
        var catImg = catalogGO.AddComponent<Image>();
        catImg.color = new Color(0.10f, 0.10f, 0.13f, 1f);

        var scrollRect = catalogGO.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical   = true;
        scrollRect.scrollSensitivity = 20f;

        var catLE = catalogGO.AddComponent<LayoutElement>();
        catLE.flexibleWidth = 1f;

        // Viewport
        var viewportGO = new GameObject("Viewport");
        Undo.RegisterCreatedObjectUndo(viewportGO, "Create Viewport");
        viewportGO.transform.SetParent(catalogGO.transform, false);
        var vpRT = viewportGO.AddComponent<RectTransform>();
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.anchoredPosition = Vector2.zero;
        vpRT.sizeDelta = Vector2.zero;
        // RectMask2D clips by rect without stencil — required for URP ScrollRects
        viewportGO.AddComponent<RectMask2D>();
        scrollRect.viewport = vpRT;

        // Content
        var contentGO = new GameObject("Content");
        Undo.RegisterCreatedObjectUndo(contentGO, "Create Content");
        contentGO.transform.SetParent(viewportGO.transform, false);
        var contentRT = contentGO.AddComponent<RectTransform>();
        contentRT.anchorMin = new Vector2(0f, 1f);
        contentRT.anchorMax = new Vector2(1f, 1f);
        contentRT.pivot     = new Vector2(0.5f, 1f);
        contentRT.anchoredPosition = Vector2.zero;
        contentRT.sizeDelta = new Vector2(0f, 0f);
        scrollRect.content = contentRT;

        // ── Panel_Preview (right) ──────────────────────────────────────────────
        var previewGO = new GameObject("Panel_Preview");
        Undo.RegisterCreatedObjectUndo(previewGO, "Create Panel_Preview");
        previewGO.transform.SetParent(bodyGO.transform, false);

        previewGO.AddComponent<RectTransform>();
        var prevImg = previewGO.AddComponent<Image>();
        prevImg.color = new Color(0.10f, 0.10f, 0.13f, 1f);
        var prevLE = previewGO.AddComponent<LayoutElement>();
        prevLE.preferredWidth = 240f;
        prevLE.flexibleWidth  = 0f;

        // Img_Portrait (RawImage for RenderTexture)
        var portraitGO = new GameObject("Img_Portrait");
        Undo.RegisterCreatedObjectUndo(portraitGO, "Create Img_Portrait");
        portraitGO.transform.SetParent(previewGO.transform, false);
        var portRT = portraitGO.AddComponent<RectTransform>();
        portRT.anchorMin = new Vector2(0.05f, 0.45f);
        portRT.anchorMax = new Vector2(0.95f, 0.97f);
        portRT.anchoredPosition = Vector2.zero;
        portRT.sizeDelta = Vector2.zero;
        var rawImg = portraitGO.AddComponent<RawImage>();
        rawImg.color = Color.white;

        // Txt_PreviewName
        MakeTMP(previewGO, "Txt_PreviewName", "—",
            new Vector2(0f, 0.26f), new Vector2(1f, 0.44f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(-16f, 0f), 17f, true);

        // Txt_PreviewStats
        MakeTMP(previewGO, "Txt_PreviewStats", "",
            new Vector2(0f, 0.02f), new Vector2(1f, 0.26f), new Vector2(0.5f, 0.5f),
            Vector2.zero, new Vector2(-16f, 0f), 13f, false);

        // ── Panel_Slots (5 slot buttons) ───────────────────────────────────────
        var slotsGO = new GameObject("Panel_Slots");
        Undo.RegisterCreatedObjectUndo(slotsGO, "Create Panel_Slots");
        slotsGO.transform.SetParent(panelGO.transform, false);

        var slotsRT = slotsGO.AddComponent<RectTransform>();
        slotsRT.anchorMin = new Vector2(0f, 0.13f);
        slotsRT.anchorMax = new Vector2(1f, 0.24f);
        slotsRT.anchoredPosition = Vector2.zero;
        slotsRT.sizeDelta = new Vector2(-40f, 0f);

        var slotsHLG = slotsGO.AddComponent<HorizontalLayoutGroup>();
        slotsHLG.spacing = 10f;
        slotsHLG.childControlWidth  = true;
        slotsHLG.childControlHeight = true;
        slotsHLG.childForceExpandWidth  = true;
        slotsHLG.childForceExpandHeight = true;
        slotsHLG.padding = new RectOffset(0, 0, 0, 0);

        var slotButtons = new Button[5];
        var slotLabels  = new TMP_Text[5];
        for (int i = 0; i < 5; i++)
        {
            var s = MakeButton(slotsGO, $"Btn_Slot{i}", $"Slot {i + 1}", 14f);
            slotButtons[i] = s.GetComponent<Button>();
            slotLabels[i]  = s.GetComponentInChildren<TMP_Text>();
            s.GetComponent<Image>().color = new Color(0.13f, 0.13f, 0.18f);
        }

        // ── Panel_Buttons ──────────────────────────────────────────────────────
        var btnsGO = new GameObject("Panel_Buttons");
        Undo.RegisterCreatedObjectUndo(btnsGO, "Create Panel_Buttons");
        btnsGO.transform.SetParent(panelGO.transform, false);

        var btnsRT = btnsGO.AddComponent<RectTransform>();
        btnsRT.anchorMin = new Vector2(0.5f, 0.02f);
        btnsRT.anchorMax = new Vector2(0.5f, 0.12f);
        btnsRT.pivot     = new Vector2(0.5f, 0f);
        btnsRT.anchoredPosition = Vector2.zero;
        btnsRT.sizeDelta = new Vector2(500f, 0f);

        var btnsHLG = btnsGO.AddComponent<HorizontalLayoutGroup>();
        btnsHLG.spacing = 20f;
        btnsHLG.childControlWidth  = true;
        btnsHLG.childControlHeight = true;
        btnsHLG.childForceExpandWidth  = true;
        btnsHLG.childForceExpandHeight = true;

        var btnConfirmGO = MakeButton(btnsGO, "Btn_Confirm", "Confirm", 18f);
        var btnBackGO    = MakeButton(btnsGO, "Btn_Back",    "Back",    16f);

        // Set inactive
        panelGO.SetActive(false);

        // ── Wire LoadoutUI ─────────────────────────────────────────────────────
        var so = new SerializedObject(loadoutUI);
        so.FindProperty("Img_Portrait").objectReferenceValue     = rawImg;
        so.FindProperty("Txt_PreviewName").objectReferenceValue  = FindTMP(previewGO, "Txt_PreviewName");
        so.FindProperty("Txt_PreviewStats").objectReferenceValue = FindTMP(previewGO, "Txt_PreviewStats");
        so.FindProperty("CatalogContent").objectReferenceValue   = contentRT;

        var slotBtnsProp = so.FindProperty("SlotButtons");
        slotBtnsProp.arraySize = 5;
        var slotLblsProp = so.FindProperty("SlotLabels");
        slotLblsProp.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            slotBtnsProp.GetArrayElementAtIndex(i).objectReferenceValue = slotButtons[i];
            slotLblsProp.GetArrayElementAtIndex(i).objectReferenceValue = slotLabels[i];
        }

        so.FindProperty("Btn_Confirm").objectReferenceValue = btnConfirmGO.GetComponent<Button>();
        so.FindProperty("Btn_Back").objectReferenceValue    = btnBackGO.GetComponent<Button>();
        so.ApplyModifiedProperties();
        Debug.Log("[SetupLoadoutPanel] Wired LoadoutUI inspector fields.");

        // ── Wire LobbyUI.LoadoutStep ───────────────────────────────────────────
        var lobbyUIGO = GameObject.Find("LobbyUI");
        if (lobbyUIGO != null)
        {
            var lobbyUI = lobbyUIGO.GetComponent<LobbyUI>();
            if (lobbyUI != null)
            {
                var luiSO = new SerializedObject(lobbyUI);
                luiSO.FindProperty("LoadoutStep").objectReferenceValue = loadoutUI;
                luiSO.ApplyModifiedProperties();
                Debug.Log("[SetupLoadoutPanel] Wired LobbyUI.LoadoutStep.");
            }
        }
        else
        {
            Debug.LogWarning("[SetupLoadoutPanel] LobbyUI GO not found — wire LoadoutStep manually.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[SetupLoadoutPanel] Done. Run 'Setup Portrait Studio' next, then save the scene.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static void MakeTMP(GameObject parent, string name, string text,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size, float fontSize, bool bold)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
        rt.pivot = pivot; rt.anchoredPosition = pos; rt.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = fontSize;
        tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white; tmp.raycastTarget = false;
    }

    static GameObject MakeButton(GameObject parent, string name, string label, float fontSize)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        var img = go.AddComponent<Image>();
        img.color = new Color(0.22f, 0.22f, 0.28f);
        go.AddComponent<Button>();

        var textGO = new GameObject("Text");
        Undo.RegisterCreatedObjectUndo(textGO, $"Create {name}/Text");
        textGO.transform.SetParent(go.transform, false);
        var tRT = textGO.AddComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.anchoredPosition = Vector2.zero; tRT.sizeDelta = Vector2.zero;
        var tmp = textGO.AddComponent<TextMeshProUGUI>();
        tmp.text = label; tmp.fontSize = fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white; tmp.raycastTarget = false;

        return go;
    }

    static TMP_Text FindTMP(GameObject parent, string childName)
    {
        var t = parent.transform.Find(childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }
}
