using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CastleDefender.UI;

public static class RefreshSideRailHud
{
    [MenuItem("Castle Defender/Setup/Refresh Side Rail HUD")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.name.Contains("Game_ML"))
        {
            Debug.LogError("[RefreshSideRailHud] Open Game_ML before running this.");
            return;
        }

        var infoBar = Object.FindFirstObjectByType<InfoBar>(FindObjectsInactive.Include);
        var cmdBar = Object.FindFirstObjectByType<CmdBar>(FindObjectsInactive.Include);

        if (infoBar == null || cmdBar == null)
        {
            Debug.LogError("[RefreshSideRailHud] InfoBar or CmdBar was not found.");
            return;
        }

        HideInfoBar(infoBar);
        StyleCmdBar(cmdBar);

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[RefreshSideRailHud] Left rail refreshed and right InfoBar hidden.");
    }

    static void HideInfoBar(InfoBar infoBar)
    {
        if (infoBar == null) return;
        Undo.RecordObject(infoBar.gameObject, "Hide InfoBar");
        infoBar.gameObject.SetActive(false);
        EditorUtility.SetDirty(infoBar.gameObject);
    }

    static void StyleCmdBar(CmdBar cmdBar)
    {
        var rootRt = cmdBar.GetComponent<RectTransform>();
        var rootImage = cmdBar.GetComponent<Image>();
        var layout = cmdBar.GetComponent<VerticalLayoutGroup>();

        if (rootRt != null)
            rootRt.sizeDelta = new Vector2(168f, 0f);

        if (rootImage != null)
            rootImage.color = new Color(0.02f, 0.02f, 0.02f, 0.90f);

        if (layout != null)
        {
            layout.padding = new RectOffset(8, 8, 10, 10);
            layout.spacing = 12f;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
        }

        for (int i = 0; i < cmdBar.UnitButtons.Length; i++)
        {
            var btn = cmdBar.UnitButtons[i];
            if (btn == null) continue;
            var iconSprite = cmdBar.UnitIcons != null && i < cmdBar.UnitIcons.Length
                ? cmdBar.UnitIcons[i]
                : btn.image != null ? btn.image.sprite : null;
            StyleSendButton(btn, iconSprite);

            if (cmdBar.AutoToggleButtons != null && i < cmdBar.AutoToggleButtons.Length && cmdBar.AutoToggleButtons[i] != null)
                StyleAutoStrip(cmdBar.AutoToggleButtons[i]);

            if (cmdBar.QueueCountLabels != null && i < cmdBar.QueueCountLabels.Length && cmdBar.QueueCountLabels[i] != null)
                StyleQueueBadge(cmdBar.QueueCountLabels[i]);
        }
    }

    static TMP_Text EnsureStat(RectTransform parent, string name, string defaultText)
    {
        var existing = parent.Find(name);
        if (existing != null)
        {
            var existingTxt = existing.GetComponent<TMP_Text>() ?? existing.GetComponentInChildren<TMP_Text>(true);
            if (existingTxt != null) return EnsureCard(existingTxt, name, parent, defaultText);

            var existingCard = existing.gameObject;
            if (!existingCard.name.EndsWith("_Card")) existingCard.name = $"{name}_Card";
            var newLabelGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(newLabelGo, $"Create {name}");
            newLabelGo.transform.SetParent(existingCard.transform, false);
            var createdTxt = newLabelGo.GetComponent<TextMeshProUGUI>();
            createdTxt.text = defaultText;
            return createdTxt;
        }

        var card = CreateCardRoot(parent, $"{name}_Card");
        var labelGo = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        Undo.RegisterCreatedObjectUndo(labelGo, $"Create {name}");
        labelGo.transform.SetParent(card.transform, false);

        var txt = labelGo.GetComponent<TextMeshProUGUI>();
        txt.text = defaultText;
        return txt;
    }

    static TMP_Text EnsureExistingStat(TMP_Text txt, string fallback)
    {
        return txt != null ? EnsureCard(txt, txt.name, txt.transform.parent as RectTransform, fallback) : null;
    }

    static void StyleStat(TMP_Text txt, Color bg, float fontSize, float height)
    {
        if (txt == null) return;

        var card = GetCardRoot(txt);
        var image = card.GetComponent<Image>();
        if (image == null) image = Undo.AddComponent<Image>(card.gameObject);
        image.color = bg;
        image.raycastTarget = false;

        var layout = card.GetComponent<LayoutElement>();
        if (layout == null) layout = Undo.AddComponent<LayoutElement>(card.gameObject);
        layout.preferredHeight = height;
        layout.flexibleHeight = 0f;
        layout.minHeight = height;

        var cardRt = card.GetComponent<RectTransform>();
        cardRt.sizeDelta = new Vector2(0f, height);

        var rt = txt.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        txt.fontSize = fontSize;
        txt.fontStyle = FontStyles.Bold;
        txt.alignment = TextAlignmentOptions.MidlineLeft;
        txt.margin = new Vector4(14f, 0f, 14f, 0f);
        txt.color = Color.white;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        txt.overflowMode = TextOverflowModes.Truncate;
        txt.raycastTarget = false;
    }

    static void StyleIncomeRing(Image ring)
    {
        var card = WrapGraphicInCard(ring.transform as RectTransform, "IncomeRing_Card");
        var layout = ring.GetComponent<LayoutElement>();
        if (layout == null) layout = Undo.AddComponent<LayoutElement>(ring.gameObject);
        layout.preferredHeight = 84f;
        layout.preferredWidth = 84f;
        layout.flexibleHeight = 0f;

        var rt = ring.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(84f, 84f);
        ring.color = new Color(0.31f, 0.77f, 0.67f, 1f);
        ring.raycastTarget = false;
        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;

        if (card != null)
        {
            var bg = card.GetComponent<Image>();
            if (bg == null) bg = Undo.AddComponent<Image>(card.gameObject);
            bg.color = new Color(0.08f, 0.15f, 0.14f, 0.96f);

            var cardLayout = card.GetComponent<LayoutElement>();
            if (cardLayout == null) cardLayout = Undo.AddComponent<LayoutElement>(card.gameObject);
            cardLayout.preferredHeight = 84f;
            cardLayout.minHeight = 84f;
            cardLayout.flexibleHeight = 0f;
        }
    }

    static void StyleBarracksButton(Button btn)
    {
        var image = btn.GetComponent<Image>();
        if (image != null) image.color = new Color(0.16f, 0.11f, 0.07f, 0.96f);

        var layout = btn.GetComponent<LayoutElement>();
        if (layout == null) layout = Undo.AddComponent<LayoutElement>(btn.gameObject);
        layout.preferredHeight = 102f;
        layout.flexibleHeight = 0f;

        var colors = btn.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 1f);
        colors.highlightedColor = new Color(1f, 0.95f, 0.85f, 1f);
        colors.pressedColor = new Color(0.85f, 0.80f, 0.75f, 1f);
        btn.colors = colors;

        var label = btn.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.text = "Barracks";
            label.fontSize = 14f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.98f, 0.89f, 0.70f, 1f);
        }
    }

    static TMP_Text EnsureTopStat(RectTransform parent, string name, string text, Color color, float width)
    {
        if (parent == null) return null;
        var existing = parent.Find(name);
        TMP_Text label;
        if (existing != null)
        {
            label = existing.GetComponent<TMP_Text>();
        }
        else
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.transform.SetParent(parent, false);
            label = go.GetComponent<TextMeshProUGUI>();
        }

        label.text = text;
        label.fontSize = 15f;
        label.fontStyle = FontStyles.Bold;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Truncate;
        label.raycastTarget = false;

        var layout = label.GetComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = 36f;
        layout.flexibleWidth = 0f;
        layout.flexibleHeight = 0f;

        return label;
    }

    static void StyleSendButton(Button btn, Sprite iconSprite)
    {
        var root = btn.GetComponent<RectTransform>();
        if (root == null) return;

        var layout = btn.GetComponent<LayoutElement>();
        if (layout == null) layout = Undo.AddComponent<LayoutElement>(btn.gameObject);
        layout.preferredHeight = 142f;
        layout.flexibleHeight = 0f;

        var bg = btn.GetComponent<Image>();
        if (bg != null)
        {
            bg.sprite = null;
            bg.type = Image.Type.Simple;
            bg.color = new Color(0.04f, 0.05f, 0.07f, 0.96f);
        }

        var icon = EnsureImageChild(btn.transform, "Icon");
        var iconRt = icon.rectTransform;
        iconRt.anchorMin = new Vector2(0.10f, 0.23f);
        iconRt.anchorMax = new Vector2(0.90f, 0.75f);
        iconRt.offsetMin = Vector2.zero;
        iconRt.offsetMax = Vector2.zero;
        icon.raycastTarget = false;
        icon.color = Color.white;
        if (iconSprite != null)
        {
            icon.sprite = iconSprite;
            icon.type = Image.Type.Simple;
            icon.preserveAspect = true;
        }

        var iconBg = EnsureImageChild(btn.transform, "IconBg");
        iconBg.transform.SetSiblingIndex(icon.transform.GetSiblingIndex());
        var iconBgRt = iconBg.rectTransform;
        iconBgRt.anchorMin = iconRt.anchorMin;
        iconBgRt.anchorMax = iconRt.anchorMax;
        iconBgRt.offsetMin = Vector2.zero;
        iconBgRt.offsetMax = Vector2.zero;
        iconBg.color = new Color(0.11f, 0.13f, 0.16f, 0.98f);
        iconBg.raycastTarget = false;

        var label = btn.transform.Find("Label")?.GetComponent<TMP_Text>();
        if (label != null)
        {
            label.rectTransform.anchorMin = new Vector2(0f, 1f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.pivot = new Vector2(0.5f, 1f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -4f);
            label.rectTransform.sizeDelta = new Vector2(-10f, 24f);
            label.fontSize = 9f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.92f, 0.95f, 1f, 1f);
            label.alignment = TextAlignmentOptions.Top;
        }

        var border = EnsureImageChild(btn.transform, "Border");
        border.transform.SetAsFirstSibling();
        var borderRt = border.rectTransform;
        borderRt.anchorMin = Vector2.zero;
        borderRt.anchorMax = Vector2.one;
        borderRt.offsetMin = Vector2.zero;
        borderRt.offsetMax = Vector2.zero;
        border.color = new Color(0.10f, 0.45f, 0.55f, 0.35f);
        border.raycastTarget = false;
    }

    static void StyleAutoStrip(Button btn)
    {
        var image = btn.GetComponent<Image>();
        if (image != null) image.color = new Color(0.06f, 0.10f, 0.12f, 0.96f);

        var rt = btn.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 24f);
        }

        var label = btn.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            label.fontSize = 9f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.70f, 0.90f, 0.90f, 1f);
        }

        var accent = EnsureImageChild(btn.transform, "Accent");
        var accentRt = accent.rectTransform;
        accentRt.anchorMin = new Vector2(0f, 0f);
        accentRt.anchorMax = new Vector2(1f, 0f);
        accentRt.pivot = new Vector2(0.5f, 0f);
        accentRt.anchoredPosition = Vector2.zero;
        accentRt.sizeDelta = new Vector2(0f, 3f);
        accent.color = new Color(0.24f, 0.84f, 0.78f, 1f);
        accent.raycastTarget = false;
    }

    static void StyleQueueBadge(TMP_Text txt)
    {
        txt.fontSize = 10f;
        txt.fontStyle = FontStyles.Bold;
        txt.color = new Color(1f, 0.87f, 0.30f, 1f);
    }

    static Image EnsureImageChild(Transform parent, string childName)
    {
        var existing = parent.Find(childName);
        if (existing != null)
        {
            var existingImage = existing.GetComponent<Image>();
            if (existingImage != null) return existingImage;
        }

        var go = new GameObject(childName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        Undo.RegisterCreatedObjectUndo(go, $"Create {childName}");
        go.transform.SetParent(parent, false);
        go.transform.SetSiblingIndex(0);
        return go.GetComponent<Image>();
    }

    static Transform GetCardRoot(TMP_Text txt)
    {
        if (txt == null) return null;
        var parent = txt.transform.parent;
        return parent != null && parent.name.EndsWith("_Card") ? parent : txt.transform;
    }

    static TMP_Text EnsureCard(TMP_Text txt, string labelName, RectTransform root, string fallback)
    {
        if (txt == null || root == null) return txt;
        var currentParent = txt.transform.parent as RectTransform;
        if (currentParent != null && currentParent.name.EndsWith("_Card")) return txt;

        var card = CreateCardRoot(root, $"{labelName}_Card");
        card.transform.SetSiblingIndex(txt.transform.GetSiblingIndex());
        Undo.SetTransformParent(txt.transform, card.transform, "Wrap stat in card");
        txt.rectTransform.anchorMin = Vector2.zero;
        txt.rectTransform.anchorMax = Vector2.one;
        txt.rectTransform.offsetMin = Vector2.zero;
        txt.rectTransform.offsetMax = Vector2.zero;
        if (string.IsNullOrWhiteSpace(txt.text)) txt.text = fallback;
        return txt;
    }

    static GameObject CreateCardRoot(RectTransform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
        go.transform.SetParent(parent, false);
        return go;
    }

    static RectTransform WrapGraphicInCard(RectTransform graphic, string cardName)
    {
        if (graphic == null) return null;

        var parent = graphic.parent as RectTransform;
        if (parent == null) return null;
        if (parent.name == cardName) return parent;

        var siblingIndex = graphic.GetSiblingIndex();
        var cardGo = CreateCardRoot(parent, cardName);
        var card = cardGo.GetComponent<RectTransform>();
        card.SetSiblingIndex(siblingIndex);
        Undo.SetTransformParent(graphic, card, "Wrap graphic in card");
        graphic.anchorMin = new Vector2(0.5f, 0.5f);
        graphic.anchorMax = new Vector2(0.5f, 0.5f);
        graphic.pivot = new Vector2(0.5f, 0.5f);
        return card;
    }

    static void SetCardVisible(TMP_Text txt, bool visible)
    {
        var root = GetCardRoot(txt);
        if (root != null && root.gameObject.activeSelf != visible)
            root.gameObject.SetActive(visible);
    }

    static void HideCardsByPrefix(RectTransform parent, string prefix)
    {
        if (parent == null) return;
        for (int i = 0; i < parent.childCount; i++)
        {
            var child = parent.GetChild(i);
            if (child.name.StartsWith(prefix))
                child.gameObject.SetActive(false);
        }
    }

    static void HideGraphicCard(Graphic graphic)
    {
        if (graphic == null) return;
        var parent = graphic.transform.parent;
        if (parent != null && parent.name.EndsWith("_Card"))
            parent.gameObject.SetActive(false);
        else
            graphic.gameObject.SetActive(false);
    }
}
