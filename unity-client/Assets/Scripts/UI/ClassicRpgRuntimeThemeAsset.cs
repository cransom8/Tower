using TMPro;
using UnityEngine;

namespace CastleDefender.UI
{
    [CreateAssetMenu(menuName = "Castle Defender/UI/Classic RPG Runtime Theme", fileName = "ClassicRpgRuntimeTheme")]
    public class ClassicRpgRuntimeThemeAsset : ScriptableObject
    {
        [Header("Fonts")]
        public TMP_FontAsset TitleFont;
        public TMP_FontAsset HeaderFont;
        public TMP_FontAsset BodyFont;

        [Header("Frames")]
        public Sprite FrameForSlicing;
        public Sprite Paper;
        public Sprite PaperMedium;
        public Sprite MainMenuBar;
        public Sprite TitleLong;
        public Sprite TitleMedium;
        public Sprite TitleMini;
        public Sprite ShadowBasic;
        public Sprite PortraitFrame;
        public Sprite PortraitFrameBackground;
        public Sprite SpellPickDarkPart;
        public Sprite SeparatorDark;
        public Sprite FlagBigClassic;
        public Sprite FlagLittleGold;
        public Sprite InventoryTitle;
        public Sprite QuestTitleYellow;

        [Header("Role Icons")]
        public Sprite ActivityShieldIcon;
        public Sprite ActivityArcherIcon;
        public Sprite ActivityInfantryIcon;
        public Sprite ActivityPriestIcon;
        public Sprite ActivityMageIcon;

        [Header("Buttons - Long")]
        public Sprite ButtonLongNormal;
        public Sprite ButtonLongHovered;
        public Sprite ButtonLongPressed;
        public Sprite ButtonLongDisabled;

        [Header("Buttons - Medium")]
        public Sprite ButtonMediumNormal;
        public Sprite ButtonMediumHovered;
        public Sprite ButtonMediumPressed;
        public Sprite ButtonMediumDisabled;

        [Header("Buttons - Mini Gold")]
        public Sprite ButtonMiniGoldNormal;
        public Sprite ButtonMiniGoldHovered;
        public Sprite ButtonMiniGoldPressed;
        public Sprite ButtonMiniGoldDisabled;

        [Header("Buttons - Mini Green")]
        public Sprite ButtonMiniGreenNormal;
        public Sprite ButtonMiniGreenHovered;
        public Sprite ButtonMiniGreenPressed;
        public Sprite ButtonMiniGreenDisabled;

        [Header("Buttons - Mini Brown")]
        public Sprite ButtonMiniBrownNormal;
        public Sprite ButtonMiniBrownHovered;
        public Sprite ButtonMiniBrownPressed;
        public Sprite ButtonMiniBrownDisabled;

        [Header("Buttons - Micro Exit")]
        public Sprite ButtonMicroExitNormal;
        public Sprite ButtonMicroExitHovered;
        public Sprite ButtonMicroExitPressed;
        public Sprite ButtonMicroExitDisabled;
    }
}
