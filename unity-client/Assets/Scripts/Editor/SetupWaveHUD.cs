using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Game;
using CastleDefender.UI;

/// <summary>
/// One-shot editor utility:
///   1. Creates Assets/Prefabs/UI/HpBar.prefab  (world-space billboard bar)
///   2. Creates Canvas/WaveHUD panel with top HUD labels
///   3. Wires GameManager.TxtRound/Phase/Countdown/TeamHpLeft/Right
///   4. Wires an InfoBar component for Gold/Income top labels
///   5. Assigns HpBarPrefab to LaneRenderer + all 4 TileGrid components
/// Run via: Castle Defender → Setup → Setup Wave HUD
/// </summary>
public static class SetupWaveHUD
{
    const string MenuPath = "Castle Defender/Setup/Setup Wave HUD";

    [MenuItem(MenuPath)]
    public static void Run()
    {
        // ── 1. Create HpBar prefab ────────────────────────────────────────────
        string prefabPath = "Assets/Prefabs/UI/HpBar.prefab";
        GameObject hpBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

        if (hpBarPrefab == null)
        {
            // Root
            var root = new GameObject("HpBar");

            // Background — thin dark quad
            var bg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bg.name = "Background";
            bg.transform.SetParent(root.transform, false);
            bg.transform.localScale    = new Vector3(1f, 0.08f, 0.02f);
            bg.transform.localPosition = Vector3.zero;
            Object.DestroyImmediate(bg.GetComponent<BoxCollider>());
            SetColor(bg, new Color(0.1f, 0.1f, 0.1f, 0.85f));

            // FillAnchor — pivot at left edge so X-scale grows right
            var fillAnchor = new GameObject("FillAnchor");
            fillAnchor.transform.SetParent(root.transform, false);
            fillAnchor.transform.localPosition = new Vector3(-0.5f, 0f, -0.01f);

            // Fill — starts full width, localScale.x driven by code
            var fill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fill.name = "Fill";
            fill.transform.SetParent(fillAnchor.transform, false);
            fill.transform.localScale    = new Vector3(1f, 0.06f, 0.02f);
            fill.transform.localPosition = new Vector3(0.5f, 0f, 0f); // offset right so pivot is at left
            Object.DestroyImmediate(fill.GetComponent<BoxCollider>());
            SetColor(fill, new Color(0.15f, 0.90f, 0.25f, 1f));

            // Save as prefab
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Prefabs/UI");
            hpBarPrefab = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Debug.Log("[SetupWaveHUD] Created HpBar prefab at " + prefabPath);
        }
        else
        {
            Debug.Log("[SetupWaveHUD] HpBar prefab already exists — skipping creation.");
        }

        // ── 2. Find or create WaveHUD panel in Canvas ─────────────────────────
        var canvas = GameObject.Find("Canvas");
        if (canvas == null) { Debug.LogError("[SetupWaveHUD] No Canvas found in scene."); return; }

        var existingHUD = canvas.transform.Find("WaveHUD");
        GameObject hudGO;
        if (existingHUD != null)
        {
            hudGO = existingHUD.gameObject;
            Debug.Log("[SetupWaveHUD] WaveHUD panel already exists — reusing.");
        }
        else
        {
            hudGO = new GameObject("WaveHUD");
            hudGO.transform.SetParent(canvas.transform, false);
            var rt = hudGO.AddComponent<RectTransform>();
            // Top-center anchor, spans full width at top
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -2f);
            rt.sizeDelta        = new Vector2(0f, 44f);

            var hlg = hudGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing            = 12f;
            hlg.childAlignment     = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth  = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(8, 8, 4, 4);
        }

        // Helper: get-or-create a TMP_Text child
        TMP_Text GetOrCreateLabel(string childName, string defaultText, float preferredWidth, int fontSize, Color color)
        {
            var existing = hudGO.transform.Find(childName);
            if (existing != null) return existing.GetComponent<TMP_Text>();

            var go = new GameObject(childName);
            go.transform.SetParent(hudGO.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(preferredWidth, 36f);

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth  = preferredWidth;
            le.preferredHeight = 36f;

            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.text      = defaultText;
            txt.fontSize  = fontSize;
            txt.color     = color;
            txt.alignment = TextAlignmentOptions.Center;
            txt.fontStyle = FontStyles.Bold;
            return txt;
        }

        var txtRound     = GetOrCreateLabel("Txt_Round",       "Wave 1",    100f, 18, Color.white);
        var txtPhase     = GetOrCreateLabel("Txt_Phase",       "BUILD",      90f, 16, new Color(0.3f, 1f, 0.4f));
        var txtCountdown = GetOrCreateLabel("Txt_Countdown",   "30s",        60f, 16, Color.white);
        var txtGoldTop   = GetOrCreateLabel("Txt_GoldTop",     "Gold 0",    110f, 16, new Color(1f, 0.82f, 0.08f));
        var txtIncomeTop = GetOrCreateLabel("Txt_IncomeTop",   "Inc 0.0",   110f, 16, new Color(0.3f, 0.95f, 1f));
        var txtHpLeft    = GetOrCreateLabel("Txt_TeamHpLeft",  "Left 20/20", 100f, 16, new Color(1f, 0.92f, 0.2f));
        var txtHpRight   = GetOrCreateLabel("Txt_TeamHpRight", "Right 20/20",100f, 16, Color.white);

        // ── 3. Wire GameManager ───────────────────────────────────────────────
        var gmGO = GameObject.Find("GameManager");
        if (gmGO == null) { Debug.LogError("[SetupWaveHUD] GameManager GO not found."); return; }

        var gm = gmGO.GetComponent<GameManager>();
        if (gm == null) { Debug.LogError("[SetupWaveHUD] GameManager component not found."); return; }

        Undo.RecordObject(gm, "Wire Wave HUD");
        gm.TxtRound      = txtRound;
        gm.TxtPhase      = txtPhase;
        gm.TxtCountdown  = txtCountdown;
        gm.TxtTeamHpLeft  = txtHpLeft;
        gm.TxtTeamHpRight = txtHpRight;
        EditorUtility.SetDirty(gm);
        Debug.Log("[SetupWaveHUD] GameManager wave HUD fields wired.");

        // Keep top gold/income updating through InfoBar logic without reviving the old right rail.
        var infoBar = hudGO.GetComponent<InfoBar>();
        if (infoBar == null) infoBar = hudGO.AddComponent<InfoBar>();
        Undo.RecordObject(infoBar, "Wire Top HUD InfoBar");
        infoBar.TxtWave = txtRound;
        infoBar.TxtPhase = txtPhase;
        infoBar.TxtCountdown = txtCountdown;
        infoBar.TxtGoldTop = txtGoldTop;
        infoBar.TxtIncomeTop = txtIncomeTop;
        infoBar.TxtTeamHpLeft = txtHpLeft;
        infoBar.TxtTeamHpRight = txtHpRight;
        infoBar.TxtGold = null;
        infoBar.TxtIncome = null;
        infoBar.TxtLives = null;
        infoBar.TxtBarracksLv = null;
        infoBar.BtnBarracks = null;
        infoBar.ImgIncomeRing = null;
        EditorUtility.SetDirty(infoBar);

        // ── 4. Assign HpBarPrefab to LaneRenderer and all TileGrids ──────────
        var lr = Object.FindFirstObjectByType<LaneRenderer>();
        if (lr != null)
        {
            Undo.RecordObject(lr, "Wire HpBar");
            lr.HpBarPrefab = hpBarPrefab;
            EditorUtility.SetDirty(lr);
            Debug.Log("[SetupWaveHUD] LaneRenderer.HpBarPrefab assigned.");
        }

        var grids = Object.FindObjectsByType<TileGrid>(FindObjectsSortMode.None);
        foreach (var g in grids)
        {
            Undo.RecordObject(g, "Wire HpBar");
            g.HpBarPrefab = hpBarPrefab;
            EditorUtility.SetDirty(g);
        }
        Debug.Log($"[SetupWaveHUD] HpBarPrefab assigned to {grids.Length} TileGrid(s).");

        // ── 5. Save scene ─────────────────────────────────────────────────────
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[SetupWaveHUD] Done. Save the scene to persist changes.");
    }

    static void SetColor(GameObject go, Color col)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = col;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        rend.sharedMaterial = mat;
    }
}
