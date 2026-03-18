using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class RestoreGameplayUI
    {
        [MenuItem("Castle Defender/Setup/Restore Gameplay UI")]
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.name != "Game_ML")
            {
                Debug.LogError("[RestoreGameplayUI] Open Game_ML first.");
                return;
            }

            var canvas = EnsureCanvas();
            EnsureEventSystem();
            EnsureCmdBar(canvas.transform);
            for (int lane = 0; lane < 4; lane++)
                EnsureTileMenu(canvas.transform, lane);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[RestoreGameplayUI] Base gameplay UI restored. Run rebuild/wiring steps next.");
        }

        static Canvas EnsureCanvas()
        {
            var existing = Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (existing != null)
            {
                if (existing.GetComponent<GraphicRaycaster>() == null)
                    existing.gameObject.AddComponent<GraphicRaycaster>();
                var scaler = existing.GetComponent<CanvasScaler>();
                if (scaler == null) scaler = existing.gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1600f, 900f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.7f;
                existing.renderMode = RenderMode.ScreenSpaceOverlay;
                return existing;
            }

            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 0;
            var scalerNew = go.GetComponent<CanvasScaler>();
            scalerNew.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scalerNew.referenceResolution = new Vector2(1600f, 900f);
            scalerNew.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scalerNew.matchWidthOrHeight = 0.7f;
            return canvas;
        }

        static void EnsureEventSystem()
        {
            var es = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
            if (es != null)
            {
                if (es.GetComponent<StandaloneInputModule>() == null)
                    es.gameObject.AddComponent<StandaloneInputModule>();
                return;
            }

            new GameObject("GameplayEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        static CmdBar EnsureCmdBar(Transform canvas)
        {
            var existing = canvas.Find("CmdBar");
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject("CmdBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup), typeof(CmdBar));
                go.transform.SetParent(canvas, false);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(228f, 0f);

            var bg = go.GetComponent<Image>();
            bg.color = new Color(0.02f, 0.02f, 0.02f, 0.9f);
            bg.raycastTarget = true;

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 12f;
            layout.padding = new RectOffset(6, 6, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var cmdBar = go.GetComponent<CmdBar>();

            EnsureUnitButton(go.transform, "Btn_Unit0");
            EnsureUnitButton(go.transform, "Btn_Unit1");
            EnsureUnitButton(go.transform, "Btn_Unit2");
            EnsureUnitButton(go.transform, "Btn_Unit3");
            EnsureUnitButton(go.transform, "Btn_Unit4");

            return cmdBar;
        }

        static Button EnsureUnitButton(Transform parent, string name)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null)
            {
                go = existing.gameObject;
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(parent, false);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 188f);

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 188f;
            le.flexibleHeight = 0f;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.18f, 0.15f, 0.12f, 1f);
            img.raycastTarget = true;

            var button = go.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.95f, 0.85f, 1f);
            colors.pressedColor = new Color(0.85f, 0.80f, 0.75f, 1f);
            button.colors = colors;

            EnsureIconBg(go.transform);
            EnsureIcon(go.transform);
            EnsureLabel(go.transform);
            return button;
        }

        static void EnsureIconBg(Transform parent)
        {
            var existing = parent.Find("IconBg");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject("IconBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.04f, 0.16f);
            rt.anchorMax = new Vector2(0.96f, 0.84f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.20f, 0.96f);
            img.raycastTarget = false;
        }

        static void EnsureIcon(Transform parent)
        {
            var existing = parent.Find("Icon");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.transform.SetParent(parent, false);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.17f);
            rt.anchorMax = new Vector2(0.95f, 0.83f);
            rt.offsetMin = new Vector2(4f, 4f);
            rt.offsetMax = new Vector2(-4f, -4f);

            var img = go.GetComponent<Image>();
            img.color = Color.white;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        static TMP_Text EnsureLabel(Transform parent)
        {
            var existing = parent.Find("Label");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -4f);
            rt.sizeDelta = new Vector2(0f, 26f);

            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.text = "Unit\n0g";
            txt.fontSize = 11f;
            txt.fontStyle = FontStyles.Bold;
            txt.color = new Color(0.85f, 0.78f, 0.65f, 1f);
            txt.alignment = TextAlignmentOptions.Top;
            txt.raycastTarget = false;
            return txt;
        }

        static TileMenuUI EnsureTileMenu(Transform canvas, int lane)
        {
            var rootName = $"TileMenuUI_Lane{lane}";
            var existing = canvas.Find(rootName);
            GameObject root;
            if (existing != null)
            {
                root = existing.gameObject;
            }
            else
            {
                root = new GameObject(rootName, typeof(RectTransform), typeof(TileMenuUI));
                root.transform.SetParent(canvas, false);
            }

            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            rootRt.pivot = new Vector2(0.5f, 0.5f);
            rootRt.anchoredPosition = Vector2.zero;
            rootRt.sizeDelta = new Vector2(760f, 300f);

            var menu = root.GetComponent<TileMenuUI>();
            var panel = EnsurePanel(root.transform, "PanelTileMenu");
            panel.SetActive(false);
            menu.PanelTileMenu = panel;

            var txtInfo = EnsureText(panel.transform, "Txt_TileInfo", "Place unit", 14f, TextAlignmentOptions.Center);
            var towerLayout = EnsureTowerButtonRow(panel.transform);
            var btnArcher = EnsureMenuButton(towerLayout.transform, "Btn_Archer", "Unit\n0g");
            var btnFighter = EnsureMenuButton(towerLayout.transform, "Btn_Fighter", "Unit\n0g");
            var btnMage = EnsureMenuButton(towerLayout.transform, "Btn_Mage", "Unit\n0g");
            var btnBallista = EnsureMenuButton(towerLayout.transform, "Btn_Ballista", "Unit\n0g");
            var btnCannon = EnsureMenuButton(towerLayout.transform, "Btn_Cannon", "Unit\n0g");
            var btnUpgrade = EnsureActionButton(panel.transform, "Btn_Upgrade", "Upgrade\n0g", 44f);
            var btnRemove = EnsureActionButton(panel.transform, "Btn_Remove", "Remove", 38f);
            var btnClose = EnsureActionButton(panel.transform, "Btn_Close", "Close", 32f);

            menu.TxtTileInfo = txtInfo;
            menu.HLayoutTowerButtons = towerLayout;
            menu.BtnArcher = btnArcher;
            menu.BtnFighter = btnFighter;
            menu.BtnMage = btnMage;
            menu.BtnBallista = btnBallista;
            menu.BtnCannon = btnCannon;
            menu.BtnUpgrade = btnUpgrade;
            menu.TxtUpgradeCost = btnUpgrade.GetComponentInChildren<TextMeshProUGUI>(true);
            menu.BtnRemove = btnRemove;
            menu.BtnClose = btnClose;

            return menu;
        }

        static GameObject EnsurePanel(Transform parent, string name)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
                go.transform.SetParent(parent, false);
            }

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(760f, 0f);

            var img = go.GetComponent<Image>();
            img.color = new Color(0.10f, 0.09f, 0.08f, 0.96f);
            img.raycastTarget = true;

            var group = go.GetComponent<CanvasGroup>();
            group.interactable = true;
            group.blocksRaycasts = true;

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var fitter = go.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return go;
        }

        static TMP_Text EnsureText(Transform parent, string name, string text, float size, TextAlignmentOptions align)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                go.transform.SetParent(parent, false);
            }

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.minHeight = 36f;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 36f);

            var txt = go.GetComponent<TextMeshProUGUI>();
            txt.text = text;
            txt.fontSize = size;
            txt.fontStyle = FontStyles.Bold;
            txt.color = new Color(0.88f, 0.82f, 0.70f, 1f);
            txt.alignment = align;
            txt.raycastTarget = false;
            return txt;
        }

        static GameObject EnsureTowerButtonRow(Transform parent)
        {
            var existing = parent.Find("HLayout_TowerButtons");
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject("HLayout_TowerButtons", typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
                go.transform.SetParent(parent, false);
            }

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 128f;
            le.minHeight = 128f;

            var layout = go.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return go;
        }

        static Button EnsureMenuButton(Transform parent, string name, string label)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
            }

            var le = go.GetComponent<LayoutElement>();
            le.preferredWidth = 136f;
            le.preferredHeight = 128f;
            le.flexibleWidth = 1f;
            le.flexibleHeight = 0f;

            var img = go.GetComponent<Image>();
            img.color = new Color(0.18f, 0.16f, 0.13f, 1f);
            img.raycastTarget = true;

            EnsureIconBg(go.transform);
            EnsureIcon(go.transform);
            var txt = EnsureLabel(go.transform);
            txt.text = label;
            txt.fontSize = 11f;
            txt.alignment = TextAlignmentOptions.Bottom;

            return go.GetComponent<Button>();
        }

        static Button EnsureActionButton(Transform parent, string name, string label, float height)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                go.transform.SetParent(parent, false);
            }

            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = height;
            le.minHeight = height;

            var img = go.GetComponent<Image>();
            img.color = name == "Btn_Upgrade"
                ? new Color(0.15f, 0.55f, 0.45f, 1f)
                : name == "Btn_Remove"
                    ? new Color(0.55f, 0.18f, 0.18f, 1f)
                    : new Color(0.22f, 0.20f, 0.18f, 1f);

            var labelTransform = go.transform.Find("Label");
            TMP_Text txt;
            if (labelTransform != null) txt = labelTransform.GetComponent<TextMeshProUGUI>();
            else
            {
                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                labelGo.transform.SetParent(go.transform, false);
                txt = labelGo.GetComponent<TextMeshProUGUI>();
            }

            var rt = txt.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            txt.text = label;
            txt.fontSize = 11f;
            txt.fontStyle = FontStyles.Bold;
            txt.color = new Color(0.88f, 0.82f, 0.70f, 1f);
            txt.alignment = TextAlignmentOptions.Center;
            txt.raycastTarget = false;
            return go.GetComponent<Button>();
        }
    }
}
