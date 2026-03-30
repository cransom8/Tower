using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    public enum ClassicRpgTextTone
    {
        Title,
        Heading,
        Body,
        BodyStrong,
        Muted,
        Accent,
        Success,
        Error,
    }

    public enum ClassicRpgTextStyle
    {
        Title,
        SectionHeader,
        Body,
        SmallBody,
        ButtonLabel,
        HelperText,
    }

    public enum ClassicRpgButtonSkin
    {
        LongGold,
        MediumGold,
        MiniGold,
        MiniGreen,
        MiniBrown,
        MicroExit,
    }

    public enum ClassicRpgPanelSkin
    {
        Frame,
        Paper,
        PaperMedium,
        MainMenuBar,
        TitleLong,
        TitleMedium,
        TitleMini,
        PortraitFrame,
        PortraitBackdrop,
        Shadow,
        DarkSpell,
        FlagClassic,
        SeparatorDark,
        InventoryTitle,
        QuestTitle,
    }

    public static class ClassicRpgUiRuntime
    {
        const string ResourcePath = "ClassicRpgRuntimeTheme";
        const float CompactWidthThreshold = 1320f;
        const float CompactAspectThreshold = 1.34f;

        struct SpriteSwapSet
        {
            public Sprite Normal;
            public Sprite Highlighted;
            public Sprite Pressed;
            public Sprite Disabled;
        }

        static ClassicRpgRuntimeThemeAsset _theme;

        public static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

        public static float MinimumTouchTarget => 48f;
        public static float LargeTouchTarget => 56f;
        public static float CompactPadding => 18f;
        public static float StandardPadding => 28f;
        public static float LargePadding => 36f;

        public static Color BackdropColor => new(0.03f, 0.04f, 0.08f, 1f);
        public static Color BackdropOverlayColor => new(0.10f, 0.07f, 0.03f, 0.56f);
        public static Color CardShadowColor => new(0f, 0f, 0f, 0.38f);
        public static Color WarmGold => new(0.96f, 0.84f, 0.50f, 1f);
        public static Color SoftGold => new(0.88f, 0.78f, 0.42f, 1f);
        public static Color BrightText => new(0.96f, 0.93f, 0.88f, 1f);
        public static Color MutedText => new(0.73f, 0.75f, 0.79f, 1f);
        public static Color ErrorText => new(0.95f, 0.54f, 0.46f, 1f);
        public static Color SuccessText => new(0.70f, 0.89f, 0.64f, 1f);
        public static Color DeepNavy => new(0.07f, 0.09f, 0.15f, 0.96f);
        public static Color DeepBluePanel => new(0.09f, 0.12f, 0.19f, 0.96f);
        public static Color InkBlue => new(0.12f, 0.16f, 0.24f, 0.98f);
        public static Color PaperInk => new(0.18f, 0.14f, 0.07f, 1f);
        public static Color PanelFill => new(0.15f, 0.13f, 0.09f, 0.96f);
        public static Color DetailFill => new(0.10f, 0.12f, 0.18f, 0.98f);
        public static Color FooterTint => new(1f, 1f, 1f, 0.92f);

        public static ClassicRpgRuntimeThemeAsset Theme
        {
            get
            {
                if (_theme == null)
                    _theme = Resources.Load<ClassicRpgRuntimeThemeAsset>(ResourcePath);

                return _theme;
            }
        }

        public static void ApplyCanvasScaler(CanvasScaler scaler, Vector2? referenceResolution = null)
        {
            if (scaler == null)
                return;

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution ?? ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        public static bool IsCompactLayout(RectTransform canvasRect)
        {
            float width = canvasRect != null && canvasRect.rect.width > 0f ? canvasRect.rect.width : Screen.width;
            float height = canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : Screen.height;
            if (height <= 0f)
                height = 1f;

            float aspect = width / height;
            return width <= CompactWidthThreshold || aspect <= CompactAspectThreshold;
        }

        public static float PickByLayout(RectTransform canvasRect, float wideValue, float compactValue)
        {
            return IsCompactLayout(canvasRect) ? compactValue : wideValue;
        }

        public static void Stretch(RectTransform rect, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            if (rect == null)
                return;

            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        public static void ApplySafeArea(
            RectTransform rect,
            RectTransform canvasRect,
            float horizontalPadding,
            float topPadding,
            float bottomPadding,
            float? leftPadding = null,
            float? rightPadding = null)
        {
            if (rect == null)
                return;

            float canvasWidth = canvasRect != null && canvasRect.rect.width > 0f ? canvasRect.rect.width : ReferenceResolution.x;
            float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : ReferenceResolution.y;
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);

            var safeArea = Screen.safeArea;
            float widthScale = canvasWidth / screenWidth;
            float heightScale = canvasHeight / screenHeight;

            float leftInset = safeArea.xMin * widthScale;
            float rightInset = (screenWidth - safeArea.xMax) * widthScale;
            float topInset = (screenHeight - safeArea.yMax) * heightScale;
            float bottomInset = safeArea.yMin * heightScale;

            Stretch(
                rect,
                new Vector2(leftInset + (leftPadding ?? horizontalPadding), bottomInset + bottomPadding),
                new Vector2(-(rightInset + (rightPadding ?? horizontalPadding)), -(topInset + topPadding)));
        }

        public static void ApplyText(TMP_Text label, ClassicRpgTextTone tone, TextAlignmentOptions? alignment = null, Color? color = null)
        {
            ApplyTextStyle(label, MapToneToStyle(tone), alignment, color ?? GetTextColor(tone));

            if (label == null)
                return;

            switch (tone)
            {
                case ClassicRpgTextTone.Title:
                    label.fontStyle = FontStyles.Bold;
                    break;
                case ClassicRpgTextTone.Heading:
                case ClassicRpgTextTone.BodyStrong:
                case ClassicRpgTextTone.Accent:
                    label.fontStyle = FontStyles.Bold;
                    break;
                case ClassicRpgTextTone.Muted:
                    label.fontStyle = FontStyles.Normal;
                    break;
            }
        }

        public static void ApplyTextStyle(
            TMP_Text label,
            ClassicRpgTextStyle style,
            TextAlignmentOptions? alignment = null,
            Color? color = null,
            bool allowWrap = true)
        {
            if (label == null)
                return;

            var font = GetFont(style) ?? TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                label.font = font;
                TryAssignSharedMaterial(label, font.material);
            }

            label.color = color ?? GetTextColor(style);
            label.fontStyle = GetFontStyle(style);
            label.richText = true;
            label.textWrappingMode = allowWrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.overflowMode = allowWrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            label.margin = Vector4.zero;

            if (alignment.HasValue)
                label.alignment = alignment.Value;

            ApplyOutline(label, style);
        }

        public static void ApplyPanel(Image image, ClassicRpgPanelSkin skin, bool sliced = true, Color? tint = null)
        {
            if (image == null)
                return;

            var sprite = GetPanelSprite(skin);
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
                image.preserveAspect = !sliced;
            }

            if (tint.HasValue)
                image.color = tint.Value;
        }

        public static void ApplyFooterStrip(Image image, Color? tint = null)
        {
            ApplyPanel(image, ClassicRpgPanelSkin.MainMenuBar, false, tint ?? FooterTint);
        }

        public static void ApplyButton(Button button, ClassicRpgButtonSkin skin, TMP_Text label = null, string overrideText = null)
        {
            if (button == null)
                return;

            var image = button.targetGraphic as Image;
            if (image == null)
            {
                image = button.GetComponent<Image>();
                if (image != null)
                    button.targetGraphic = image;
            }

            if (image != null)
            {
                var sprites = GetButtonSprites(skin);
                if (sprites.Normal != null)
                {
                    image.sprite = sprites.Normal;
                    image.type = Image.Type.Sliced;
                    image.color = Color.white;

                    var state = button.spriteState;
                    state.highlightedSprite = sprites.Highlighted ?? sprites.Normal;
                    state.pressedSprite = sprites.Pressed ?? sprites.Normal;
                    state.disabledSprite = sprites.Disabled ?? sprites.Normal;
                    button.transition = Selectable.Transition.SpriteSwap;
                    button.spriteState = state;
                }
            }

            label ??= button.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
                return;

            if (!string.IsNullOrEmpty(overrideText))
                label.text = overrideText;

            ApplyTextStyle(
                label,
                ClassicRpgTextStyle.ButtonLabel,
                TextAlignmentOptions.Center,
                skin switch
                {
                    ClassicRpgButtonSkin.MiniGreen => new Color(0.86f, 0.95f, 0.82f, 1f),
                    ClassicRpgButtonSkin.MiniBrown => BrightText,
                    _ => WarmGold,
                },
                allowWrap: false);
            label.raycastTarget = false;
        }

        public static void StyleInputField(TMP_InputField input, string placeholderText = null)
        {
            if (input == null)
                return;

            var image = input.GetComponent<Image>();
            if (image != null)
                ApplyPanel(image, ClassicRpgPanelSkin.Frame, true, new Color(1f, 1f, 1f, 0.98f));

            var backing = EnsureChildImage(input.transform, "PremiumBacking");
            backing.transform.SetAsFirstSibling();
            backing.raycastTarget = false;
            ApplyPanel(backing, ClassicRpgPanelSkin.PaperMedium, true, new Color(0.18f, 0.16f, 0.12f, 0.94f));

            if (backing.rectTransform != null)
            {
                Stretch(backing.rectTransform, new Vector2(10f, 9f), new Vector2(-10f, -9f));
            }

            if (input.textViewport != null)
            {
                input.textViewport.offsetMin = new Vector2(20f, 12f);
                input.textViewport.offsetMax = new Vector2(-20f, -12f);
            }

            if (input.textComponent != null)
            {
                ApplyTextStyle(input.textComponent, ClassicRpgTextStyle.Body, TextAlignmentOptions.MidlineLeft, BrightText);
                input.textComponent.margin = Vector4.zero;
            }

            if (input.placeholder is TMP_Text placeholder)
            {
                if (!string.IsNullOrWhiteSpace(placeholderText))
                    placeholder.text = placeholderText;

                ApplyTextStyle(
                    placeholder,
                    ClassicRpgTextStyle.HelperText,
                    TextAlignmentOptions.MidlineLeft,
                    new Color(0.62f, 0.59f, 0.55f, 0.98f));
                placeholder.fontStyle = FontStyles.Italic;
                placeholder.margin = Vector4.zero;
            }

            input.caretColor = WarmGold;
            input.selectionColor = new Color(0.87f, 0.68f, 0.26f, 0.35f);
        }

        public static Image EnsureChildImage(Transform parent, string name)
        {
            if (parent == null)
                return null;

            Transform existing = parent.Find(name);
            if (existing != null)
            {
                var existingImage = existing.GetComponent<Image>();
                if (existingImage != null)
                    return existingImage;
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        static ClassicRpgTextStyle MapToneToStyle(ClassicRpgTextTone tone)
        {
            return tone switch
            {
                ClassicRpgTextTone.Title => ClassicRpgTextStyle.Title,
                ClassicRpgTextTone.Heading => ClassicRpgTextStyle.SectionHeader,
                ClassicRpgTextTone.BodyStrong => ClassicRpgTextStyle.Body,
                ClassicRpgTextTone.Muted => ClassicRpgTextStyle.HelperText,
                ClassicRpgTextTone.Accent => ClassicRpgTextStyle.SectionHeader,
                _ => ClassicRpgTextStyle.Body,
            };
        }

        static TMP_FontAsset GetFont(ClassicRpgTextStyle style)
        {
            var theme = Theme;
            if (theme == null)
                return TMP_Settings.defaultFontAsset;

            return style switch
            {
                ClassicRpgTextStyle.Title => theme.TitleFont != null ? theme.TitleFont : theme.HeaderFont,
                ClassicRpgTextStyle.SectionHeader => theme.HeaderFont != null ? theme.HeaderFont : theme.BodyFont,
                ClassicRpgTextStyle.ButtonLabel => theme.HeaderFont != null ? theme.HeaderFont : theme.BodyFont,
                ClassicRpgTextStyle.Body => theme.BodyFont != null ? theme.BodyFont : TMP_Settings.defaultFontAsset,
                ClassicRpgTextStyle.SmallBody => theme.BodyFont != null ? theme.BodyFont : TMP_Settings.defaultFontAsset,
                ClassicRpgTextStyle.HelperText => theme.BodyFont != null ? theme.BodyFont : TMP_Settings.defaultFontAsset,
                _ => theme.BodyFont != null ? theme.BodyFont : TMP_Settings.defaultFontAsset,
            };
        }

        static FontStyles GetFontStyle(ClassicRpgTextStyle style)
        {
            return style switch
            {
                ClassicRpgTextStyle.Title => FontStyles.Bold,
                ClassicRpgTextStyle.SectionHeader => FontStyles.Bold,
                ClassicRpgTextStyle.ButtonLabel => FontStyles.Bold,
                _ => FontStyles.Normal,
            };
        }

        static void TryAssignSharedMaterial(TMP_Text label, Material material)
        {
            if (label == null || material == null)
                return;

            try
            {
                label.fontSharedMaterial = material;
                label.fontMaterial = material;
                label.UpdateMeshPadding();
            }
            catch
            {
                // Some runtime-created TMP instances are not ready for a material swap yet.
            }
        }

        static void ApplyOutline(TMP_Text label, ClassicRpgTextStyle style)
        {
            if (label == null)
                return;

            try
            {
                if (label.fontSharedMaterial == null)
                    return;

                switch (style)
                {
                    case ClassicRpgTextStyle.Title:
                        label.outlineWidth = 0.22f;
                        label.outlineColor = new Color(0.11f, 0.06f, 0.02f, 0.94f);
                        break;
                    case ClassicRpgTextStyle.SectionHeader:
                    case ClassicRpgTextStyle.ButtonLabel:
                        label.outlineWidth = 0.16f;
                        label.outlineColor = new Color(0.11f, 0.07f, 0.03f, 0.76f);
                        break;
                    default:
                        label.outlineWidth = 0.08f;
                        label.outlineColor = new Color(0.05f, 0.04f, 0.02f, 0.48f);
                        break;
                }
            }
            catch
            {
                // Runtime material creation can lag one frame behind on some TMP instances.
            }
        }

        static Color GetTextColor(ClassicRpgTextTone tone)
        {
            return tone switch
            {
                ClassicRpgTextTone.Title => WarmGold,
                ClassicRpgTextTone.Heading => BrightText,
                ClassicRpgTextTone.BodyStrong => BrightText,
                ClassicRpgTextTone.Muted => MutedText,
                ClassicRpgTextTone.Accent => WarmGold,
                ClassicRpgTextTone.Success => SuccessText,
                ClassicRpgTextTone.Error => ErrorText,
                _ => new Color(0.85f, 0.87f, 0.91f, 1f),
            };
        }

        static Color GetTextColor(ClassicRpgTextStyle style)
        {
            return style switch
            {
                ClassicRpgTextStyle.Title => WarmGold,
                ClassicRpgTextStyle.SectionHeader => BrightText,
                ClassicRpgTextStyle.Body => new Color(0.88f, 0.90f, 0.94f, 1f),
                ClassicRpgTextStyle.SmallBody => new Color(0.82f, 0.84f, 0.89f, 1f),
                ClassicRpgTextStyle.ButtonLabel => WarmGold,
                ClassicRpgTextStyle.HelperText => MutedText,
                _ => BrightText,
            };
        }

        static Sprite GetPanelSprite(ClassicRpgPanelSkin skin)
        {
            var theme = Theme;
            if (theme == null)
                return null;

            return skin switch
            {
                ClassicRpgPanelSkin.Frame => theme.FrameForSlicing,
                ClassicRpgPanelSkin.Paper => theme.Paper,
                ClassicRpgPanelSkin.PaperMedium => theme.PaperMedium,
                ClassicRpgPanelSkin.MainMenuBar => theme.MainMenuBar,
                ClassicRpgPanelSkin.TitleLong => theme.TitleLong,
                ClassicRpgPanelSkin.TitleMedium => theme.TitleMedium,
                ClassicRpgPanelSkin.TitleMini => theme.TitleMini,
                ClassicRpgPanelSkin.PortraitFrame => theme.PortraitFrame,
                ClassicRpgPanelSkin.PortraitBackdrop => theme.PortraitFrameBackground,
                ClassicRpgPanelSkin.Shadow => theme.ShadowBasic,
                ClassicRpgPanelSkin.DarkSpell => theme.SpellPickDarkPart,
                ClassicRpgPanelSkin.FlagClassic => theme.FlagBigClassic,
                ClassicRpgPanelSkin.SeparatorDark => theme.SeparatorDark,
                ClassicRpgPanelSkin.InventoryTitle => theme.InventoryTitle,
                ClassicRpgPanelSkin.QuestTitle => theme.QuestTitleYellow,
                _ => null,
            };
        }

        static SpriteSwapSet GetButtonSprites(ClassicRpgButtonSkin skin)
        {
            var theme = Theme;
            if (theme == null)
                return default;

            return skin switch
            {
                ClassicRpgButtonSkin.LongGold => new SpriteSwapSet
                {
                    Normal = theme.ButtonLongNormal,
                    Highlighted = theme.ButtonLongHovered,
                    Pressed = theme.ButtonLongPressed,
                    Disabled = theme.ButtonLongDisabled,
                },
                ClassicRpgButtonSkin.MediumGold => new SpriteSwapSet
                {
                    Normal = theme.ButtonMediumNormal,
                    Highlighted = theme.ButtonMediumHovered,
                    Pressed = theme.ButtonMediumPressed,
                    Disabled = theme.ButtonMediumDisabled,
                },
                ClassicRpgButtonSkin.MiniGold => new SpriteSwapSet
                {
                    Normal = theme.ButtonMiniGoldNormal,
                    Highlighted = theme.ButtonMiniGoldHovered,
                    Pressed = theme.ButtonMiniGoldPressed,
                    Disabled = theme.ButtonMiniGoldDisabled,
                },
                ClassicRpgButtonSkin.MiniGreen => new SpriteSwapSet
                {
                    Normal = theme.ButtonMiniGreenNormal,
                    Highlighted = theme.ButtonMiniGreenHovered,
                    Pressed = theme.ButtonMiniGreenPressed,
                    Disabled = theme.ButtonMiniGreenDisabled,
                },
                ClassicRpgButtonSkin.MiniBrown => new SpriteSwapSet
                {
                    Normal = theme.ButtonMiniBrownNormal,
                    Highlighted = theme.ButtonMiniBrownHovered,
                    Pressed = theme.ButtonMiniBrownPressed,
                    Disabled = theme.ButtonMiniBrownDisabled,
                },
                ClassicRpgButtonSkin.MicroExit => new SpriteSwapSet
                {
                    Normal = theme.ButtonMicroExitNormal,
                    Highlighted = theme.ButtonMicroExitHovered,
                    Pressed = theme.ButtonMicroExitPressed,
                    Disabled = theme.ButtonMicroExitDisabled,
                },
                _ => default,
            };
        }
    }
}
