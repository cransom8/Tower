using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    public enum ClassicRpgUiPartKind
    {
        HeaderTitleFrame,
        ContentPanelFrame,
        DetailInfoPanelFrame,
        PrimaryButton,
        SecondaryButton,
        TabButton,
        NodeCard,
        TooltipBox,
        StatusFooterStrip,
    }

    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ClassicRpgUiPart : MonoBehaviour
    {
        public ClassicRpgUiPartKind Kind;
        public Image Background;
        public Button Button;
        public TMP_Text Label;
        public LayoutElement Layout;
        public bool ApplyAtRuntime = true;

        void Reset()
        {
            CacheReferences();
            ApplyStyle();
        }

        void OnValidate()
        {
            CacheReferences();
            ApplyStyle();
        }

        void Awake()
        {
            if (!ApplyAtRuntime && Application.isPlaying)
                return;

            CacheReferences();
            ApplyStyle();
        }

        [ContextMenu("Apply Style")]
        public void ApplyStyle()
        {
            CacheReferences();

            switch (Kind)
            {
                case ClassicRpgUiPartKind.HeaderTitleFrame:
                    ApplyFrame(ClassicRpgPanelSkin.TitleLong, Color.white, preferredHeight: 96f, minHeight: 72f);
                    ApplyLabel(ClassicRpgTextStyle.Title, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold, allowWrap: false);
                    break;

                case ClassicRpgUiPartKind.ContentPanelFrame:
                    ApplyFrame(ClassicRpgPanelSkin.PaperMedium, ClassicRpgUiRuntime.PanelFill, minHeight: 180f);
                    ApplyLabel(ClassicRpgTextStyle.Body, TextAlignmentOptions.TopLeft, ClassicRpgUiRuntime.BrightText);
                    break;

                case ClassicRpgUiPartKind.DetailInfoPanelFrame:
                    ApplyFrame(ClassicRpgPanelSkin.PaperMedium, ClassicRpgUiRuntime.DetailFill, minHeight: 220f);
                    ApplyLabel(ClassicRpgTextStyle.Body, TextAlignmentOptions.TopLeft, ClassicRpgUiRuntime.BrightText);
                    break;

                case ClassicRpgUiPartKind.PrimaryButton:
                    ApplyButtonSkin(ClassicRpgButtonSkin.LongGold, preferredHeight: 56f, minHeight: 48f);
                    break;

                case ClassicRpgUiPartKind.SecondaryButton:
                    ApplyButtonSkin(ClassicRpgButtonSkin.MediumGold, preferredHeight: 50f, minHeight: 44f);
                    break;

                case ClassicRpgUiPartKind.TabButton:
                    ApplyButtonSkin(ClassicRpgButtonSkin.MiniBrown, preferredHeight: 46f, minHeight: 42f);
                    break;

                case ClassicRpgUiPartKind.NodeCard:
                    ApplyFrame(ClassicRpgPanelSkin.PaperMedium, new Color(0.10f, 0.12f, 0.19f, 0.98f), preferredHeight: 148f, minHeight: 120f);
                    ApplyLabel(ClassicRpgTextStyle.SectionHeader, TextAlignmentOptions.TopLeft, ClassicRpgUiRuntime.BrightText);
                    break;

                case ClassicRpgUiPartKind.TooltipBox:
                    ApplyFrame(ClassicRpgPanelSkin.PaperMedium, new Color(0.08f, 0.10f, 0.16f, 0.98f), preferredHeight: 96f, minHeight: 84f);
                    ApplyLabel(ClassicRpgTextStyle.HelperText, TextAlignmentOptions.TopLeft, ClassicRpgUiRuntime.BrightText);
                    break;

                case ClassicRpgUiPartKind.StatusFooterStrip:
                    ApplyFrame(ClassicRpgPanelSkin.MainMenuBar, ClassicRpgUiRuntime.FooterTint, preferredHeight: 62f, minHeight: 56f, sliced: false);
                    ApplyLabel(ClassicRpgTextStyle.HelperText, TextAlignmentOptions.Center, ClassicRpgUiRuntime.BrightText);
                    break;
            }
        }

        void CacheReferences()
        {
            if (Background == null)
                Background = GetComponent<Image>();
            if (Button == null)
                Button = GetComponent<Button>();
            if (Layout == null)
                Layout = GetComponent<LayoutElement>();
            if (Label == null)
                Label = GetComponentInChildren<TMP_Text>(true);
        }

        void ApplyFrame(
            ClassicRpgPanelSkin skin,
            Color tint,
            float preferredHeight = 0f,
            float minHeight = 0f,
            bool sliced = true)
        {
            if (Background != null)
                ClassicRpgUiRuntime.ApplyPanel(Background, skin, sliced, tint);

            if (Layout != null)
            {
                if (preferredHeight > 0f)
                    Layout.preferredHeight = preferredHeight;
                if (minHeight > 0f)
                    Layout.minHeight = minHeight;
            }
        }

        void ApplyButtonSkin(ClassicRpgButtonSkin skin, float preferredHeight, float minHeight)
        {
            if (Layout != null)
            {
                Layout.preferredHeight = preferredHeight;
                Layout.minHeight = minHeight;
            }

            if (Button != null)
                ClassicRpgUiRuntime.ApplyButton(Button, skin, Label);
            else if (Label != null)
                ApplyLabel(ClassicRpgTextStyle.ButtonLabel, TextAlignmentOptions.Center, ClassicRpgUiRuntime.WarmGold, allowWrap: false);
        }

        void ApplyLabel(ClassicRpgTextStyle style, TextAlignmentOptions alignment, Color color, bool allowWrap = true)
        {
            if (Label == null)
                return;

            ClassicRpgUiRuntime.ApplyTextStyle(Label, style, alignment, color, allowWrap);
            Label.raycastTarget = false;
        }
    }
}
