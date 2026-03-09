// RebuildTileMenuPanel.cs — Rebuilds and re-styles the TileMenuUI popup panel.
//
// Run via: Castle Defender → Setup → Rebuild Tile Menu Panel
//
// What it does:
//   1. Finds every TileMenuUI in the scene
//   2. Resizes PanelTileMenu to 240×auto with a dark card background
//   3. Moves Txt_UpgradeCost INSIDE Btn_Upgrade so the button label shows cost
//   4. Styles Btn_Upgrade (teal), Btn_Remove (muted red), Btn_Close (X, top-right)
//   5. Wires all public fields on TileMenuUI via SerializedObject
//
// Safe to re-run — idempotent.

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using CastleDefender.UI;

public static class RebuildTileMenuPanel
{
    static readonly Color ColCard      = new Color(0.10f, 0.09f, 0.08f, 0.96f);
    static readonly Color ColUpgrade   = new Color(0.15f, 0.55f, 0.45f, 1.00f);
    static readonly Color ColRemove    = new Color(0.55f, 0.18f, 0.18f, 1.00f);
    static readonly Color ColClose     = new Color(0.22f, 0.20f, 0.18f, 1.00f);
    static readonly Color ColText      = new Color(0.88f, 0.82f, 0.70f, 1.00f);
    static readonly Color ColSubtext   = new Color(0.60f, 0.56f, 0.48f, 1.00f);

    [MenuItem("Castle Defender/Setup/Rebuild Tile Menu Panel")]
    public static void Run()
    {
        var allMenus = Object.FindObjectsByType<TileMenuUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (allMenus.Length == 0) { Debug.LogError("[RebuildTileMenu] No TileMenuUI found in scene."); return; }

        foreach (var menu in allMenus)
            RebuildOne(menu);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[RebuildTileMenu] Done — rebuilt {allMenus.Length} TileMenuUI(s). Save the scene.");
    }

    static void RebuildOne(TileMenuUI menu)
    {
        var panelGO = menu.PanelTileMenu;
        if (panelGO == null)
        {
            Debug.LogWarning($"[RebuildTileMenu] {menu.name}: PanelTileMenu is null — skipping.");
            return;
        }

        Undo.RecordObject(panelGO, "Rebuild TileMenu Panel");

        // ── Panel background ─────────────────────────────────────────────────
        var panelRT = panelGO.GetComponent<RectTransform>();
        if (panelRT != null)
        {
            // Anchor center, width 240, height controlled by VLG
            panelRT.sizeDelta = new Vector2(240f, 0f);
        }

        var panelImg = panelGO.GetComponent<Image>();
        if (panelImg == null) panelImg = Undo.AddComponent<Image>(panelGO);
        Undo.RecordObject(panelImg, "Style panel bg");
        panelImg.color = ColCard;

        // Ensure a VerticalLayoutGroup drives the panel
        var vlg = panelGO.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = Undo.AddComponent<VerticalLayoutGroup>(panelGO);
        Undo.RecordObject(vlg, "Setup VLG");
        vlg.childAlignment       = TextAnchor.UpperCenter;
        vlg.spacing              = 4f;
        vlg.padding              = new RectOffset(8, 8, 8, 8);
        vlg.childControlWidth    = true;
        vlg.childControlHeight   = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        // ContentSizeFitter so panel height matches contents
        var csf = panelGO.GetComponent<ContentSizeFitter>();
        if (csf == null) csf = Undo.AddComponent<ContentSizeFitter>(panelGO);
        Undo.RecordObject(csf, "Setup CSF");
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // ── Txt_TileInfo ─────────────────────────────────────────────────────
        var infoT = panelGO.transform.Find("Txt_TileInfo");
        if (infoT != null)
        {
            Undo.RecordObject(infoT.gameObject, "Style Txt_TileInfo");
            var le = GetOrAdd<LayoutElement>(infoT.gameObject);
            Undo.RecordObject(le, "LE info");
            le.preferredHeight = 22f;
            le.minHeight       = 22f;

            var tmp = infoT.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                Undo.RecordObject(tmp, "Style info text");
                tmp.fontSize  = 11f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color     = ColText;
                tmp.alignment = TextAlignmentOptions.Center;
            }
        }

        // ── Btn_Upgrade ───────────────────────────────────────────────────────
        var upgradeT = panelGO.transform.Find("Btn_Upgrade");
        TMP_Text upgradeLabelTmp = null;
        if (upgradeT != null)
        {
            Undo.RecordObject(upgradeT.gameObject, "Style Btn_Upgrade");
            StyleButton(upgradeT.gameObject, ColUpgrade, 36f);

            // Get or create text label inside the button — this becomes TxtUpgradeCost
            var existingLabel = upgradeT.GetComponentInChildren<TMP_Text>(true);
            if (existingLabel != null)
            {
                Undo.RecordObject(existingLabel, "Style upgrade label");
                existingLabel.fontSize  = 11f;
                existingLabel.fontStyle = FontStyles.Bold;
                existingLabel.color     = ColText;
                existingLabel.alignment = TextAlignmentOptions.Center;
                upgradeLabelTmp = existingLabel;
            }
        }

        // ── Remove standalone Txt_UpgradeCost sibling (fold into button) ─────
        var costSiblingT = panelGO.transform.Find("Txt_UpgradeCost");
        if (costSiblingT != null)
        {
            if (upgradeT != null && upgradeLabelTmp == null)
            {
                // Move the text object into the button
                Undo.SetTransformParent(costSiblingT, upgradeT, "Move Txt_UpgradeCost into Btn_Upgrade");
                var rt = costSiblingT.GetComponent<RectTransform>();
                if (rt != null)
                {
                    Undo.RecordObject(rt, "Stretch cost text");
                    rt.anchorMin        = Vector2.zero;
                    rt.anchorMax        = Vector2.one;
                    rt.pivot            = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta        = Vector2.zero;
                }
                var tmp = costSiblingT.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    Undo.RecordObject(tmp, "Style moved cost text");
                    tmp.fontSize  = 11f;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.color     = ColText;
                    tmp.alignment = TextAlignmentOptions.Center;
                    upgradeLabelTmp = tmp;
                }
            }
            else if (upgradeT != null && upgradeLabelTmp != null)
            {
                // Already have a label inside; kill the standalone sibling
                Undo.DestroyObjectImmediate(costSiblingT.gameObject);
            }
        }

        // ── Btn_Remove ────────────────────────────────────────────────────────
        var removeT = panelGO.transform.Find("Btn_Remove");
        if (removeT != null)
        {
            Undo.RecordObject(removeT.gameObject, "Style Btn_Remove");
            StyleButton(removeT.gameObject, ColRemove, 30f);
            var lbl = removeT.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null) { Undo.RecordObject(lbl, "Remove lbl"); lbl.text = "Remove"; lbl.color = ColText; lbl.fontSize = 10f; }
        }

        // ── Btn_Close ─────────────────────────────────────────────────────────
        var closeT = panelGO.transform.Find("Btn_Close");
        if (closeT != null)
        {
            Undo.RecordObject(closeT.gameObject, "Style Btn_Close");
            StyleButton(closeT.gameObject, ColClose, 24f);
            var lbl = closeT.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null) { Undo.RecordObject(lbl, "Close lbl"); lbl.text = "Close"; lbl.color = ColSubtext; lbl.fontSize = 9f; }
        }

        // ── HLayout_TowerButtons — style conversion buttons ───────────────────
        var hlT = panelGO.transform.Find("HLayout_TowerButtons");
        if (hlT != null)
        {
            var hl = hlT.GetComponent<HorizontalLayoutGroup>();
            if (hl == null) hl = Undo.AddComponent<HorizontalLayoutGroup>(hlT.gameObject);
            Undo.RecordObject(hl, "Style HLG");
            hl.spacing              = 3f;
            hl.childControlWidth    = true;
            hl.childControlHeight   = true;
            hl.childForceExpandWidth  = true;
            hl.childForceExpandHeight = true;

            var hlLE = GetOrAdd<LayoutElement>(hlT.gameObject);
            Undo.RecordObject(hlLE, "HL LE");
            hlLE.preferredHeight = 52f;
            hlLE.minHeight       = 52f;

            // Style each conversion button
            var convBtns = new string[] { "Btn_Archer","Btn_Fighter","Btn_Mage","Btn_Ballista","Btn_Cannon" };
            foreach (var bname in convBtns)
            {
                var bt = hlT.Find(bname);
                if (bt == null) continue;
                Undo.RecordObject(bt.gameObject, "Style conv btn");
                var img = bt.GetComponent<Image>();
                if (img != null) { Undo.RecordObject(img, "btn img"); img.color = new Color(0.18f, 0.16f, 0.13f, 1f); }
                var lbl = bt.GetComponentInChildren<TMP_Text>(true);
                if (lbl != null) { Undo.RecordObject(lbl, "conv lbl"); lbl.fontSize = 8f; lbl.color = ColText; lbl.alignment = TextAlignmentOptions.Bottom; }
            }
        }

        // ── Wire TileMenuUI inspector fields ──────────────────────────────────
        var so = new SerializedObject(menu);
        so.Update();

        // TxtUpgradeCost → the label inside Btn_Upgrade
        if (upgradeLabelTmp != null)
        {
            var prop = so.FindProperty("TxtUpgradeCost");
            if (prop != null) prop.objectReferenceValue = upgradeLabelTmp;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(menu.gameObject);
        Debug.Log($"[RebuildTileMenu] Rebuilt {menu.name}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static void StyleButton(GameObject go, Color bg, float minH)
    {
        var img = go.GetComponent<Image>();
        if (img != null) { Undo.RecordObject(img, "btn bg"); img.color = bg; }

        var btn = go.GetComponent<Button>();
        if (btn != null)
        {
            Undo.RecordObject(btn, "btn colors");
            var cols   = btn.colors;
            cols.normalColor      = bg;
            cols.highlightedColor = bg * 1.25f;
            cols.pressedColor     = bg * 0.75f;
            cols.disabledColor    = new Color(0.25f, 0.25f, 0.25f, 0.5f);
            btn.colors = cols;
        }

        var le = GetOrAdd<LayoutElement>(go);
        Undo.RecordObject(le, "btn LE");
        le.preferredHeight = minH;
        le.minHeight       = minH;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }
}
