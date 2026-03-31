using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [ExecuteAlways]
    public class CollapsibleHudCard : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] RectTransform cardRect;
        [SerializeField] RectTransform contentRoot;
        [SerializeField] RectTransform headerRoot;
        [SerializeField] Button toggleButton;
        [SerializeField] TMP_Text toggleLabel;
        [SerializeField] TMP_Text titleLabel;
        [SerializeField] Graphic backgroundGraphic;
        [SerializeField] LayoutElement layoutElement;
        [SerializeField] CanvasGroup contentCanvasGroup;

        [Header("Sizing")]
        [SerializeField] float expandedWidth = 240f;
        [SerializeField] float collapsedWidth = 28f;
        [SerializeField] float animationDuration = 0.18f;
        [SerializeField] bool startCollapsed;

        [Header("Visuals")]
        [SerializeField] string expandedGlyph = "<";
        [SerializeField] string collapsedGlyph = ">";
        [SerializeField] Color expandedBackground = new Color(0.06f, 0.08f, 0.11f, 0.92f);
        [SerializeField] Color collapsedBackground = new Color(0.10f, 0.12f, 0.15f, 0.94f);

        Coroutine _animateRoutine;
        bool _isCollapsed;

        public bool IsCollapsed => _isCollapsed;

        public void Configure(
            RectTransform rect,
            RectTransform content,
            RectTransform header,
            Button toggle,
            TMP_Text label,
            TMP_Text title,
            Graphic background,
            LayoutElement layout,
            CanvasGroup canvasGroup,
            float expanded,
            float collapsed,
            bool collapsedByDefault)
        {
            cardRect = rect;
            contentRoot = content;
            headerRoot = header;
            toggleButton = toggle;
            toggleLabel = label;
            titleLabel = title;
            backgroundGraphic = background;
            layoutElement = layout;
            contentCanvasGroup = canvasGroup;
            expandedWidth = expanded;
            collapsedWidth = collapsed;
            startCollapsed = collapsedByDefault;
        }

        void Awake()
        {
            if (cardRect == null)
                cardRect = GetComponent<RectTransform>();
            if (layoutElement == null)
                layoutElement = GetComponent<LayoutElement>();
            if (toggleButton == null)
                toggleButton = GetComponentInChildren<Button>(true);
            if (headerRoot == null)
                headerRoot = transform.Find("Header") as RectTransform;
            if (contentRoot == null)
                contentRoot = transform.Find("Content") as RectTransform;
            if (contentCanvasGroup == null && contentRoot != null)
                contentCanvasGroup = contentRoot.GetComponent<CanvasGroup>();
            if (toggleLabel == null && toggleButton != null)
                toggleLabel = toggleButton.GetComponentInChildren<TMP_Text>(true);
            if (titleLabel == null && headerRoot != null)
                titleLabel = headerRoot.Find("Title")?.GetComponent<TMP_Text>();
            if (backgroundGraphic == null)
                backgroundGraphic = GetComponent<Graphic>();
        }

        void Start()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(Toggle);
                toggleButton.onClick.AddListener(Toggle);
            }

            SetCollapsed(startCollapsed, true);
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                SetCollapsed(startCollapsed, true);
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
                SetCollapsed(startCollapsed, true);
        }

        void OnDestroy()
        {
            if (toggleButton != null)
                toggleButton.onClick.RemoveListener(Toggle);
        }

        public void Toggle()
        {
            SetCollapsed(!_isCollapsed, false);
        }

        public void SetCollapsed(bool collapsed, bool immediate)
        {
            _isCollapsed = collapsed;
            UpdateVisualState();

            if (_animateRoutine != null)
                StopCoroutine(_animateRoutine);

            float targetWidth = _isCollapsed ? collapsedWidth : expandedWidth;
            if (immediate || !isActiveAndEnabled)
            {
                ApplyWidth(targetWidth);
                ApplyContentState(immediateState: true);
                return;
            }

            _animateRoutine = StartCoroutine(AnimateWidth(targetWidth));
        }

        IEnumerator AnimateWidth(float targetWidth)
        {
            float startWidth = cardRect != null ? cardRect.rect.width : targetWidth;
            float duration = Mathf.Max(0.01f, animationDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                ApplyWidth(Mathf.Lerp(startWidth, targetWidth, t));
                yield return null;
            }

            ApplyWidth(targetWidth);
            ApplyContentState(immediateState: true);
            _animateRoutine = null;
        }

        void ApplyWidth(float width)
        {
            if (cardRect != null)
                cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, width));

            if (layoutElement != null)
            {
                layoutElement.minWidth = width;
                layoutElement.preferredWidth = width;
                layoutElement.flexibleWidth = 0f;
            }
        }

        void ApplyContentState(bool immediateState)
        {
            bool visible = !_isCollapsed;

            if (contentRoot != null)
                contentRoot.gameObject.SetActive(visible || !immediateState);

            if (contentCanvasGroup != null)
            {
                contentCanvasGroup.alpha = visible ? 1f : 0f;
                contentCanvasGroup.interactable = visible;
                contentCanvasGroup.blocksRaycasts = visible;
            }
        }

        void UpdateVisualState()
        {
            if (toggleLabel != null)
                toggleLabel.text = _isCollapsed ? collapsedGlyph : expandedGlyph;

            if (titleLabel != null)
                titleLabel.gameObject.SetActive(!_isCollapsed);

            if (headerRoot != null)
                headerRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _isCollapsed ? cardRect.rect.height : 34f);

            if (backgroundGraphic != null)
                backgroundGraphic.color = _isCollapsed ? collapsedBackground : expandedBackground;

            ApplyContentState(immediateState: false);
        }
    }
}
