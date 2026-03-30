using System.Collections.Generic;
using CastleDefender.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CastleDefender.Editor
{
    public static class ClassicRpgUiRebuildTools
    {
        const string ThemePath = "Assets/Resources/ClassicRpgRuntimeTheme.asset";
        const string PrefabFolder = "Assets/Prefabs/UI/ClassicRpg";

        static readonly string[] PriorityScenePaths =
        {
            "Assets/Scenes/Login.unity",
            "Assets/Scenes/Lobby.unity",
            "Assets/Scenes/Loadout.unity",
        };

        [MenuItem("Castle Defender/UI/Rebuild Classic RPG Priority Kit")]
        public static void RebuildClassicRpgPriorityKit()
        {
            var theme = AssetDatabase.LoadAssetAtPath<ClassicRpgRuntimeThemeAsset>(ThemePath);
            if (theme == null)
            {
                Debug.LogError($"[ClassicRpgUiRebuild] Theme asset missing at '{ThemePath}'.");
                return;
            }

            EnsureFolder("Assets/Prefabs");
            EnsureFolder("Assets/Prefabs/UI");
            EnsureFolder(PrefabFolder);

            EnsureTmpFallbacks(theme);
            BuildPrefabKit();
            SanitizePriorityScenes();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ClassicRpgUiRebuild] Rebuilt reusable Classic RPG UI kit and sanitized Login, Lobby, and Loadout scenes.");
        }

        static void BuildPrefabKit()
        {
            CreateKitPrefab("HeaderTitleFrame", ClassicRpgUiPartKind.HeaderTitleFrame, "Fortress Command");
            CreateKitPrefab("ContentPanelFrame", ClassicRpgUiPartKind.ContentPanelFrame, "Primary content lives here.");
            CreateKitPrefab("DetailInfoPanelFrame", ClassicRpgUiPartKind.DetailInfoPanelFrame, "Selected upgrade details appear here.");
            CreateKitPrefab("PrimaryButton", ClassicRpgUiPartKind.PrimaryButton, "Confirm");
            CreateKitPrefab("SecondaryButton", ClassicRpgUiPartKind.SecondaryButton, "Back");
            CreateKitPrefab("TabButton", ClassicRpgUiPartKind.TabButton, "Units");
            CreateKitPrefab("NodeCard", ClassicRpgUiPartKind.NodeCard, "Knight Captain");
            CreateKitPrefab("TooltipBox", ClassicRpgUiPartKind.TooltipBox, "Requirement and cost notes.");
            CreateKitPrefab("StatusFooterStrip", ClassicRpgUiPartKind.StatusFooterStrip, "Waiting for allies to confirm.");
        }

        static void CreateKitPrefab(string name, ClassicRpgUiPartKind kind, string sampleText)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            root.layer = 5;

            bool needsImage = true;
            bool needsButton = kind == ClassicRpgUiPartKind.PrimaryButton
                || kind == ClassicRpgUiPartKind.SecondaryButton
                || kind == ClassicRpgUiPartKind.TabButton;

            if (needsImage)
                root.AddComponent<Image>();
            if (needsButton)
                root.AddComponent<Button>();

            var part = root.AddComponent<ClassicRpgUiPart>();
            part.Kind = kind;
            part.Layout = root.GetComponent<LayoutElement>();
            part.Background = root.GetComponent<Image>();
            part.Button = root.GetComponent<Button>();

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.layer = 5;
            labelGo.transform.SetParent(root.transform, false);
            var labelRect = labelGo.GetComponent<RectTransform>();
            ClassicRpgUiRuntime.Stretch(labelRect, new Vector2(18f, 12f), new Vector2(-18f, -12f));
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.text = sampleText;
            label.fontSize = kind switch
            {
                ClassicRpgUiPartKind.HeaderTitleFrame => 26f,
                ClassicRpgUiPartKind.NodeCard => 18f,
                _ => 15f,
            };
            part.Label = label;
            part.ApplyStyle();

            string prefabPath = $"{PrefabFolder}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }

        static void SanitizePriorityScenes()
        {
            var originalScene = SceneManager.GetActiveScene().path;
            foreach (string scenePath in PriorityScenePaths)
            {
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                SanitizeScene(scene);
                EditorSceneManager.SaveScene(scene);
            }

            if (!string.IsNullOrWhiteSpace(originalScene))
                EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
        }

        static void SanitizeScene(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
                    SanitizeCanvas(canvas);

                foreach (var label in root.GetComponentsInChildren<TMP_Text>(true))
                    SanitizeLabel(label);

                foreach (var input in root.GetComponentsInChildren<TMP_InputField>(true))
                    SanitizeInputField(input);
            }

            EditorSceneManager.MarkSceneDirty(scene);
        }

        static void SanitizeCanvas(Canvas canvas)
        {
            if (canvas == null)
                return;

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            ClassicRpgUiRuntime.ApplyCanvasScaler(scaler, ClassicRpgUiRuntime.ReferenceResolution);

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();

            EditorUtility.SetDirty(canvas.gameObject);
        }

        static void SanitizeLabel(TMP_Text label)
        {
            if (label == null)
                return;

            var style = GuessTextStyle(label);
            bool allowWrap = style != ClassicRpgTextStyle.ButtonLabel;
            ClassicRpgUiRuntime.ApplyTextStyle(label, style, label.alignment, label.color, allowWrap);
            EditorUtility.SetDirty(label);
        }

        static void SanitizeInputField(TMP_InputField input)
        {
            if (input == null)
                return;

            ClassicRpgUiRuntime.StyleInputField(input);
            if (input.textComponent != null)
                SanitizeLabel(input.textComponent);
            if (input.placeholder is TMP_Text placeholder)
                SanitizeLabel(placeholder);

            EditorUtility.SetDirty(input);
        }

        static ClassicRpgTextStyle GuessTextStyle(TMP_Text label)
        {
            string name = label.name.ToLowerInvariant();
            if (label.GetComponentInParent<Button>() != null)
                return ClassicRpgTextStyle.ButtonLabel;
            if (name.Contains("title") || label.fontSize >= 24f)
                return ClassicRpgTextStyle.Title;
            if (name.Contains("header") || name.Contains("subtitle") || label.fontStyle.HasFlag(FontStyles.Bold) || label.fontSize >= 16f)
                return ClassicRpgTextStyle.SectionHeader;
            if (name.Contains("helper") || name.Contains("hint") || name.Contains("status"))
                return ClassicRpgTextStyle.HelperText;
            if (label.fontSize <= 12f)
                return ClassicRpgTextStyle.SmallBody;
            return ClassicRpgTextStyle.Body;
        }

        static void EnsureTmpFallbacks(ClassicRpgRuntimeThemeAsset theme)
        {
            var settingsAsset = TMP_Settings.instance;
            if (settingsAsset == null || theme == null)
                return;

            bool changed = false;
            if (theme.BodyFont != null && TMP_Settings.defaultFontAsset != theme.BodyFont)
            {
                TMP_Settings.defaultFontAsset = theme.BodyFont;
                changed = true;
            }

            var fallbackFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset");
            changed |= EnsureFallbacks(TMP_Settings.fallbackFontAssets, theme.HeaderFont, theme.TitleFont, fallbackFont);
            changed |= EnsureFontFallbackTable(theme.BodyFont, theme.HeaderFont, theme.TitleFont, fallbackFont);
            changed |= EnsureFontFallbackTable(theme.HeaderFont, theme.BodyFont, theme.TitleFont, fallbackFont);
            changed |= EnsureFontFallbackTable(theme.TitleFont, theme.HeaderFont, theme.BodyFont, fallbackFont);

            if (changed)
                EditorUtility.SetDirty(settingsAsset);
        }

        static bool EnsureFallbacks(List<TMP_FontAsset> target, params TMP_FontAsset[] fonts)
        {
            bool changed = false;
            if (target == null)
                return false;

            foreach (var font in fonts)
            {
                if (font == null || target.Contains(font))
                    continue;

                target.Add(font);
                changed = true;
            }

            return changed;
        }

        static bool EnsureFontFallbackTable(TMP_FontAsset font, params TMP_FontAsset[] fallbacks)
        {
            if (font == null)
                return false;

            bool changed = false;
            var table = font.fallbackFontAssetTable;
            if (table == null)
            {
                table = new List<TMP_FontAsset>();
                font.fallbackFontAssetTable = table;
                changed = true;
            }

            foreach (var fallback in fallbacks)
            {
                if (fallback == null || fallback == font || table.Contains(fallback))
                    continue;

                table.Add(fallback);
                changed = true;
            }

            if (changed)
                EditorUtility.SetDirty(font);
            return changed;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            int slash = path.LastIndexOf('/');
            string parent = slash > 0 ? path[..slash] : "Assets";
            string name = slash > 0 ? path[(slash + 1)..] : path;
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
