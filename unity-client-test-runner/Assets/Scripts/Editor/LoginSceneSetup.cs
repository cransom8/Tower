// LoginSceneSetup.cs — One-click Login scene builder.
// Menu: Castle Defender → Setup → Build Login Scene
//
// Creates Assets/Scenes/Login.unity with a fully wired Canvas,
// LoginUI component, AuthManager, and NetworkManager.
// Also inserts Login.unity at index 0 in Build Settings.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Editor
{
    public static class LoginSceneSetup
    {
        const string SCENE_PATH = "Assets/Scenes/Login.unity";

        [MenuItem("Castle Defender/Setup/Build Login Scene")]
        static void BuildLoginScene()
        {
            // Create a fresh empty scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera — required to suppress "no cameras rendering" warning.
            // Screen Space Overlay canvas doesn't need it for UI, but Unity
            // requires at least one active camera in the scene.
            var camGO  = new GameObject("Main Camera");
            camGO.tag  = "MainCamera";
            var cam    = camGO.AddComponent<Camera>();
            cam.clearFlags       = CameraClearFlags.SolidColor;
            cam.backgroundColor  = Hex("0D0F1A");   // matches Panel_Login background
            cam.cullingMask      = 0;               // renders nothing — UI is overlay
            cam.depth            = -1;

            // EventSystem (required for all UI interaction)
            var esGO = new GameObject("EventSystem");
            esGO.AddComponent<EventSystem>();
            esGO.AddComponent<StandaloneInputModule>();

            // Singleton managers — DontDestroyOnLoad, must exist in first scene
            new GameObject("AuthManager").AddComponent<AuthManager>();
            var nmGO = new GameObject("NetworkManager");
            nmGO.AddComponent<NetworkManager>();

            // Build the login canvas and get back the wired LoginUI
            BuildLoginCanvas();

            // Save
            EditorSceneManager.SaveScene(scene, SCENE_PATH);
            AssetDatabase.Refresh();

            // Put Login scene first in Build Settings
            InsertFirstInBuildSettings(SCENE_PATH);

            Debug.Log($"[LoginSceneSetup] Done — scene saved to {SCENE_PATH} and set as Build index 0.");
        }

        // ── Canvas builder ────────────────────────────────────────────────────

        static void BuildLoginCanvas()
        {
            // Root canvas
            var canvasGO = new GameObject("Canvas");
            var canvas   = canvasGO.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode          = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution  = new Vector2(1920, 1080);
            scaler.screenMatchMode      = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight   = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            // Full-screen dark background panel
            var panelGO  = MakeUIObj("Panel_Login", canvasGO.transform);
            StretchFull(panelGO);
            var panelImg = panelGO.AddComponent<Image>();
            panelImg.color = Hex("0D0F1A");

            // Centred card (400 × auto-size)
            var card   = MakeUIObj("Card", panelGO.transform);
            var cardRT = card.GetComponent<RectTransform>();
            cardRT.anchorMin        = new Vector2(0.5f, 0.5f);
            cardRT.anchorMax        = new Vector2(0.5f, 0.5f);
            cardRT.pivot            = new Vector2(0.5f, 0.5f);
            cardRT.sizeDelta        = new Vector2(400, 0);   // height driven by layout
            cardRT.anchoredPosition = Vector2.zero;

            var cardImg  = card.AddComponent<Image>();
            cardImg.color = Hex("191B29");

            var vlg = card.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment       = TextAnchor.UpperCenter;
            vlg.spacing              = 10;
            vlg.padding              = new RectOffset(24, 24, 28, 28);
            vlg.childControlWidth    = true;
            vlg.childControlHeight   = false;
            vlg.childForceExpandWidth  = true;
            vlg.childForceExpandHeight = false;

            var csf = card.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ── Title ─────────────────────────────────────────────────────────
            var titleTxt = MakeLabel("Txt_Title", card.transform, "Castle Defender",
                                     32, FontStyles.Bold, Hex("D4AF37"), 50);

            // ── Google button (hidden by default) ─────────────────────────────
            var googleGO = MakeButton("Btn_Google", card.transform,
                                      "Sign in with Google", Hex("2A5CB8"), Color.white, 44);
            googleGO.SetActive(false);

            // ── "or" divider (hidden by default) ──────────────────────────────
            var dividerGO = MakeUIObj("Obj_Divider", card.transform);
            dividerGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 22);
            var divTxt = dividerGO.AddComponent<TextMeshProUGUI>();
            divTxt.text      = "\u2500\u2500\u2500\u2500\u2500  or  \u2500\u2500\u2500\u2500\u2500";
            divTxt.fontSize  = 11;
            divTxt.alignment = TextAlignmentOptions.Center;
            divTxt.color     = Hex("555566");
            dividerGO.SetActive(false);

            // ── Tabs ──────────────────────────────────────────────────────────
            var tabsGO = MakeUIObj("Row_Tabs", card.transform);
            tabsGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 36);
            var hlg = tabsGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing              = 6;
            hlg.childControlWidth    = true;
            hlg.childControlHeight   = true;
            hlg.childForceExpandWidth  = true;
            hlg.childForceExpandHeight = true;

            var btnTabSignIn   = MakeButton("Btn_TabSignIn",   tabsGO.transform, "Sign In",  Hex("1E1F2E"), Color.white, 0);
            var btnTabRegister = MakeButton("Btn_TabRegister", tabsGO.transform, "Register", Hex("1E1F2E"), Hex("888899"), 0);

            // ── Inputs ────────────────────────────────────────────────────────
            var (emailGO,       emailField)    = MakeInputField("Input_Email",        card.transform, "Email",        TMP_InputField.ContentType.EmailAddress);
            var (displayNameGO, displayField)  = MakeInputField("Input_DisplayName",  card.transform, "Display Name", TMP_InputField.ContentType.Standard);
            var (passwordGO,    passwordField) = MakeInputField("Input_Password",     card.transform, "Password",     TMP_InputField.ContentType.Password);

            displayNameGO.SetActive(false); // shown only on Register tab

            // ── Error text (hidden by default) ────────────────────────────────
            var errorTxt = MakeLabel("Txt_Error", card.transform, "", 12, FontStyles.Normal, Hex("E84040"), 28);
            errorTxt.gameObject.SetActive(false);

            // ── Submit button ─────────────────────────────────────────────────
            var submitGO  = MakeButton("Btn_Submit", card.transform, "Sign In", Hex("D4AF37"), Hex("0D0F1A"), 44);
            var submitTxt = submitGO.GetComponentInChildren<TextMeshProUGUI>();

            // ── Status text ───────────────────────────────────────────────────
            var statusTxt = MakeLabel("Txt_Status", card.transform, "", 11, FontStyles.Normal, Hex("777788"), 28);

            // ── Browser sign-in button (Editor / Standalone fallback) ─────────
            var browserGO = MakeButton("Btn_Browser", card.transform,
                                       "Sign in via Browser", Hex("1A7A40"), Color.white, 44);

            // ── Device-code panel (shown while polling) ───────────────────────
            var devicePanelGO = MakeUIObj("Obj_DevicePanel", card.transform);
            devicePanelGO.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 80);
            var dpImg  = devicePanelGO.AddComponent<Image>();
            dpImg.color = Hex("15172A");
            var dpVlg  = devicePanelGO.AddComponent<VerticalLayoutGroup>();
            dpVlg.childAlignment       = TextAnchor.UpperCenter;
            dpVlg.spacing              = 4;
            dpVlg.padding              = new RectOffset(8, 8, 8, 8);
            dpVlg.childControlWidth    = true;
            dpVlg.childControlHeight   = false;
            dpVlg.childForceExpandWidth  = true;
            dpVlg.childForceExpandHeight = false;
            devicePanelGO.SetActive(false);

            var deviceCodeTxt   = MakeLabel("Txt_DeviceCode",   devicePanelGO.transform, "",                  20, FontStyles.Bold,   Color.white,      28);
            var deviceStatusTxt = MakeLabel("Txt_DeviceStatus", devicePanelGO.transform, "Waiting for browser…", 11, FontStyles.Normal, Hex("777788"), 20);
            var deviceCancelGO  = MakeButton("Btn_DeviceCancel", devicePanelGO.transform, "Cancel", Hex("3A1A1A"), Hex("E84040"), 24);

            // ── Wire LoginUI ──────────────────────────────────────────────────
            var loginUI = panelGO.AddComponent<LoginUI>();
            loginUI.PanelLogin        = panelGO;
            loginUI.Btn_Google        = googleGO.GetComponent<Button>();
            loginUI.Obj_Divider       = dividerGO;
            loginUI.Btn_TabSignIn     = btnTabSignIn.GetComponent<Button>();
            loginUI.Btn_TabRegister   = btnTabRegister.GetComponent<Button>();
            loginUI.Input_Email       = emailField;
            loginUI.Input_DisplayName = displayField;
            loginUI.Input_Password    = passwordField;
            loginUI.Btn_Submit        = submitGO.GetComponent<Button>();
            loginUI.TxtSubmitBtn      = submitTxt;
            loginUI.Txt_Error         = errorTxt;
            loginUI.Txt_Status        = statusTxt;
            loginUI.Btn_Browser       = browserGO.GetComponent<Button>();
            loginUI.Obj_DevicePanel   = devicePanelGO;
            loginUI.Txt_DeviceCode    = deviceCodeTxt;
            loginUI.Txt_DeviceStatus  = deviceStatusTxt;
            loginUI.Btn_DeviceCancel  = deviceCancelGO.GetComponent<Button>();

            EditorUtility.SetDirty(loginUI);
        }

        // ── UI factory helpers ────────────────────────────────────────────────

        static GameObject MakeUIObj(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void StretchFull(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.one;
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
        }

        static TextMeshProUGUI MakeLabel(string name, Transform parent, string text,
                                          float fontSize, FontStyles style, Color color, float height)
        {
            var go = MakeUIObj(name, parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, height);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text      = text;
            t.fontSize  = fontSize;
            t.fontStyle = style;
            t.alignment = TextAlignmentOptions.Center;
            t.color     = color;
            return t;
        }

        static GameObject MakeButton(string name, Transform parent,
                                      string label, Color bg, Color textColor, float height)
        {
            var go = MakeUIObj(name, parent);
            if (height > 0)
                go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, height);

            var img   = go.AddComponent<Image>();
            img.color = bg;

            var btn    = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor      = bg;
            colors.highlightedColor = Lighter(bg, 0.15f);
            colors.pressedColor     = Lighter(bg, -0.15f);
            colors.selectedColor    = bg;
            btn.colors              = colors;
            btn.targetGraphic       = img;

            var txtGO = MakeUIObj("Text", go.transform);
            StretchFull(txtGO);
            var t = txtGO.AddComponent<TextMeshProUGUI>();
            t.text      = label;
            t.fontSize  = 14;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color     = textColor;

            return go;
        }

        static (GameObject go, TMP_InputField field) MakeInputField(
            string name, Transform parent, string placeholder,
            TMP_InputField.ContentType contentType)
        {
            var go = MakeUIObj(name, parent);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 42);

            var bg   = go.AddComponent<Image>();
            bg.color = Hex("15172A");

            var field         = go.AddComponent<TMP_InputField>();
            field.contentType = contentType;

            // Text viewport
            var viewportGO = MakeUIObj("Text Area", go.transform);
            StretchFull(viewportGO);
            var vRT = viewportGO.GetComponent<RectTransform>();
            vRT.offsetMin = new Vector2(10, 2);
            vRT.offsetMax = new Vector2(-10, -2);
            viewportGO.AddComponent<RectMask2D>();

            // Placeholder
            var phGO = MakeUIObj("Placeholder", viewportGO.transform);
            StretchFull(phGO);
            var ph = phGO.AddComponent<TextMeshProUGUI>();
            ph.text      = placeholder;
            ph.fontSize  = 13;
            ph.color     = Hex("555566");
            ph.alignment = TextAlignmentOptions.MidlineLeft;

            // Text
            var txtGO = MakeUIObj("Text", viewportGO.transform);
            StretchFull(txtGO);
            var txt = txtGO.AddComponent<TextMeshProUGUI>();
            txt.fontSize  = 13;
            txt.color     = Color.white;
            txt.alignment = TextAlignmentOptions.MidlineLeft;

            field.textViewport  = viewportGO.GetComponent<RectTransform>();
            field.textComponent = txt;
            field.placeholder   = ph;

            return (go, field);
        }

        // ── Build Settings ────────────────────────────────────────────────────

        static void InsertFirstInBuildSettings(string path)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == path);
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ── Color helpers ─────────────────────────────────────────────────────

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        static Color Lighter(Color c, float delta) =>
            new Color(
                Mathf.Clamp01(c.r + delta),
                Mathf.Clamp01(c.g + delta),
                Mathf.Clamp01(c.b + delta),
                c.a);
    }
}
#endif
