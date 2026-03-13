using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CastleDefender.UI;

/// <summary>
/// Wires the post-game stats panel into all GameOverUI instances in the active scene.
/// Run via:  Castle Defender → Setup → Wire PostGame Stats UI
/// Safe to re-run: skips instances that already have StatsPanel wired.
/// </summary>
public static class SetupPostGameStatsUI
{
    const string MenuPath        = "Castle Defender/Setup/Wire PostGame Stats UI";
    const string WaveRowPrefPath = "Assets/Prefabs/UI/WaveRow.prefab";

    [MenuItem(MenuPath)]
    public static void Run()
    {
        var waveRowPrefab = GetOrCreateWaveRowPrefab();

        var gouis = Object.FindObjectsByType<GameOverUI>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (gouis.Length == 0) { Debug.LogError("[SetupPostGameStatsUI] No GameOverUI found."); return; }
        Debug.Log($"[SetupPostGameStatsUI] Found {gouis.Length} GameOverUI instance(s).");

        foreach (var goui in gouis)
            SetupSingle(goui, waveRowPrefab);

        EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[SetupPostGameStatsUI] Done — save the scene to persist.");
    }

    // ── Per-instance setup ────────────────────────────────────────────────────

    static void SetupSingle(GameOverUI goui, GameObject waveRowPrefab)
    {
        if (goui.StatsPanel != null)
        {
            Debug.Log($"[SetupPostGameStatsUI] {goui.name} already wired — skipping.");
            return;
        }

        var panel = goui.PanelGameOver;
        if (panel == null)
        {
            Debug.LogWarning($"[SetupPostGameStatsUI] {goui.name} has no PanelGameOver — skipping.");
            return;
        }

        Undo.RecordObject(goui, "Wire PostGame Stats");

        // ── New children of PanelGameOver ─────────────────────────────────────
        // Txt_Result is at anchoredPosition y=80; put info labels just below it.

        goui.Txt_CauseLoss = MakeTMP(panel.transform, "Txt_CauseLoss",
            "Lives reduced to 0",
            new Vector2(0, 42), new Vector2(480, 26), 13,
            new Color(0.85f, 0.6f, 0.6f, 1f));

        goui.Txt_Duration = MakeTMP(panel.transform, "Txt_Duration",
            "0m 0s",
            new Vector2(0, 20), new Vector2(300, 22), 12,
            new Color(0.6f, 0.6f, 0.6f, 1f));

        // Btn_Stats — positioned below the Rematch/Lobby buttons (~y=-105), starts hidden
        var btnStatsGO = MakeButton(panel.transform, "Btn_Stats", "\u25b6 Stats",
            new Vector2(0, -105), new Vector2(160, 38));
        btnStatsGO.SetActive(false);
        goui.BtnStats = btnStatsGO.GetComponent<Button>();

        // ── PanelPostGameStats root (full-screen, sibling of PanelGameOver) ────
        var root = MakeStretchPanel(goui.transform, "PanelPostGameStats",
            new Color(0.04f, 0.04f, 0.10f, 0.96f));
        root.SetActive(false);

        var statsComp     = root.AddComponent<PostGameStatsPanel>();
        statsComp.PanelRoot = root;

        // ── Header strip (top, h=50) ──────────────────────────────────────────
        var header = new GameObject("PanelHeader");
        header.transform.SetParent(root.transform, false);
        {
            var rt = header.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, 50);
            header.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.18f, 1f);
            var hlg = header.AddComponent<HorizontalLayoutGroup>();
            hlg.padding = new RectOffset(8, 8, 6, 6);
            hlg.spacing = 6;
            hlg.childControlWidth = hlg.childControlHeight = true;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;
        }

        statsComp.Btn_Tab_Summary = MakeTabBtn(header.transform, "Btn_Tab_Summary", "Summary");
        statsComp.Btn_Tab_Economy = MakeTabBtn(header.transform, "Btn_Tab_Economy", "Economy");
        statsComp.Btn_Tab_Build   = MakeTabBtn(header.transform, "Btn_Tab_Build",   "Build");
        statsComp.Btn_Tab_Waves   = MakeTabBtn(header.transform, "Btn_Tab_Waves",   "Waves");
        statsComp.Btn_Close       = MakeTabBtn(header.transform, "Btn_Close",       "\u2715");

        // ── Content area (fills rest below header) ────────────────────────────
        var contentArea = new GameObject("ContentArea");
        contentArea.transform.SetParent(root.transform, false);
        {
            var rt = contentArea.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(0, -50);
        }

        // ── PanelSummary (active by default) ──────────────────────────────────
        var pSummary = MakeStretchPanel(contentArea.transform, "PanelSummary", new Color(0, 0, 0, 0));
        {
            var vlg = pSummary.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 20, 20);
            vlg.spacing = 10;
            vlg.childControlHeight  = false;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth  = true;
        }

        var summaryRows = new TMP_Text[4];
        for (int i = 0; i < 4; i++)
        {
            var rowGO = new GameObject($"SummaryRow_{i}");
            rowGO.transform.SetParent(pSummary.transform, false);
            var rrt = rowGO.AddComponent<RectTransform>();
            rrt.sizeDelta = new Vector2(0, 38);
            var le  = rowGO.AddComponent<LayoutElement>();
            le.preferredHeight = 38;
            var txt = rowGO.AddComponent<TextMeshProUGUI>();
            txt.text      = "\u2014";
            txt.fontSize  = 13;
            txt.color     = new Color(0.85f, 0.85f, 0.85f, 1f);
            txt.alignment = TextAlignmentOptions.MidlineLeft;
            summaryRows[i] = txt;
        }
        statsComp.SummaryRows  = summaryRows;
        statsComp.PanelSummary = pSummary;

        // ── PanelEconomy ──────────────────────────────────────────────────────
        var pEconomy = MakeStretchPanel(contentArea.transform, "PanelEconomy", new Color(0, 0, 0, 0));
        pEconomy.SetActive(false);
        var econGO = new GameObject("EconomyGraph");
        econGO.transform.SetParent(pEconomy.transform, false);
        var econGraph = econGO.AddComponent<LineGraphUI>();
        {
            var rt = econGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.08f);
            rt.anchorMax = new Vector2(0.96f, 0.94f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        statsComp.EconomyGraph = econGraph;
        statsComp.PanelEconomy = pEconomy;

        // ── PanelBuild ────────────────────────────────────────────────────────
        var pBuild = MakeStretchPanel(contentArea.transform, "PanelBuild", new Color(0, 0, 0, 0));
        pBuild.SetActive(false);
        var buildGO = new GameObject("BuildGraph");
        buildGO.transform.SetParent(pBuild.transform, false);
        var buildGraph = buildGO.AddComponent<LineGraphUI>();
        {
            var rt = buildGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.08f);
            rt.anchorMax = new Vector2(0.96f, 0.94f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        statsComp.BuildGraph = buildGraph;
        statsComp.PanelBuild = pBuild;

        // ── PanelWaves (ScrollRect) ───────────────────────────────────────────
        var pWaves = MakeStretchPanel(contentArea.transform, "PanelWaves", new Color(0, 0, 0, 0));
        pWaves.SetActive(false);
        var sr = pWaves.AddComponent<ScrollRect>();

        var viewport = new GameObject("Viewport");
        viewport.transform.SetParent(pWaves.transform, false);
        {
            var rt = viewport.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            viewport.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewport.AddComponent<Mask>().showMaskGraphic = false;
        }

        var waveContent = new GameObject("WaveContent");
        waveContent.transform.SetParent(viewport.transform, false);
        var wrt = waveContent.AddComponent<RectTransform>();
        wrt.anchorMin        = new Vector2(0, 1);
        wrt.anchorMax        = new Vector2(1, 1);
        wrt.pivot            = new Vector2(0.5f, 1f);
        wrt.anchoredPosition = Vector2.zero;
        wrt.sizeDelta        = Vector2.zero;
        {
            var vlg = waveContent.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(10, 10, 8, 8);
            vlg.spacing = 4;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight     = false;
            var csf = waveContent.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        sr.content          = wrt;
        sr.viewport         = viewport.GetComponent<RectTransform>();
        sr.horizontal       = false;
        sr.vertical         = true;
        sr.scrollSensitivity = 30;

        statsComp.WaveRowContainer = wrt;
        statsComp.WaveRowPrefab    = waveRowPrefab;
        statsComp.PanelWaves       = pWaves;

        // ── Wire back to GameOverUI ───────────────────────────────────────────
        goui.StatsPanel = statsComp;

        EditorUtility.SetDirty(goui);
        EditorUtility.SetDirty(statsComp);
        Debug.Log($"[SetupPostGameStatsUI] Wired {goui.name}.");
    }

    // ── WaveRow prefab ────────────────────────────────────────────────────────

    static GameObject GetOrCreateWaveRowPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(WaveRowPrefPath);
        if (existing != null) { Debug.Log("[SetupPostGameStatsUI] WaveRow prefab already exists."); return existing; }

        System.IO.Directory.CreateDirectory(
            System.IO.Path.Combine(Application.dataPath, "Prefabs/UI"));

        var go = new GameObject("WaveRow");
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 36);
        go.AddComponent<Image>().color = new Color(0.10f, 0.10f, 0.16f, 0.80f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 36;

        var lbl = new GameObject("Label");
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(8, 0); lrt.offsetMax = new Vector2(-8, 0);
        var tmp = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text      = "Wave";
        tmp.fontSize  = 11;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, WaveRowPrefPath);
        Object.DestroyImmediate(go);
        Debug.Log("[SetupPostGameStatsUI] Created WaveRow prefab at " + WaveRowPrefPath);
        return prefab;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static TMP_Text MakeTMP(Transform parent, string name, string text,
        Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt  = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = text;
        tmp.fontSize  = fontSize;
        tmp.color     = color;
        tmp.alignment = TextAlignmentOptions.Center;
        return tmp;
    }

    static GameObject MakeButton(Transform parent, string name, string label,
        Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.AddComponent<Image>().color = new Color(0.25f, 0.45f, 0.75f, 0.9f);
        go.AddComponent<Button>();
        var lbl = new GameObject("Label");
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var tmp = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 14;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return go;
    }

    static Button MakeTabBtn(Transform parent, string name, string label)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<RectTransform>().sizeDelta = new Vector2(80, 38);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 38;
        go.AddComponent<Image>().color = new Color(0.22f, 0.22f, 0.32f, 0.9f);
        var btn = go.AddComponent<Button>();
        var lbl = new GameObject("Label");
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.AddComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var tmp = lbl.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 12;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        return btn;
    }

    static GameObject MakeStretchPanel(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        if (color.a > 0.001f)
            go.AddComponent<Image>().color = color;
        return go;
    }
}
