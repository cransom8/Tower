using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public class WaveStatusHudWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] RectTransform widgetRect;
        [SerializeField] RectTransform bodyRoot;
        [SerializeField] RectTransform collapsedRoot;
        [SerializeField] Button toggleButton;
        [SerializeField] TMP_Text toggleLabel;
        [SerializeField] TMP_Text waveValue;
        [SerializeField] TMP_Text phaseValue;
        [SerializeField] TMP_Text leftValue;
        [SerializeField] TMP_Text rightValue;
        [SerializeField] TMP_Text leftLabel;
        [SerializeField] TMP_Text rightLabel;
        [SerializeField] TMP_Text footerValue;
        [SerializeField] Image leftFill;
        [SerializeField] Image rightFill;
        [SerializeField] Image centerGlow;
        [SerializeField] TMP_Text collapsedLabel;

        [Header("State")]
        [SerializeField] bool startCollapsed;
        [SerializeField] string prefsKey = "hud.wave_status";

        RectTransform _parentRect;
        Vector2 _dragStartPointer;
        Vector2 _dragStartAnchored;
        Vector2 _defaultAnchoredPosition;
        bool _loadedState;
        bool _isCollapsed;

        public void Configure(
            RectTransform rect,
            RectTransform body,
            RectTransform collapsed,
            Button toggle,
            TMP_Text toggleText,
            TMP_Text wave,
            TMP_Text phase,
            TMP_Text left,
            TMP_Text right,
            TMP_Text leftSideLabel,
            TMP_Text rightSideLabel,
            TMP_Text footer,
            Image leftBar,
            Image rightBar,
            Image glow,
            TMP_Text collapsedText,
            bool collapsedByDefault,
            string persistentKey)
        {
            widgetRect = rect;
            bodyRoot = body;
            collapsedRoot = collapsed;
            toggleButton = toggle;
            toggleLabel = toggleText;
            waveValue = wave;
            phaseValue = phase;
            leftValue = left;
            rightValue = right;
            leftLabel = leftSideLabel;
            rightLabel = rightSideLabel;
            footerValue = footer;
            leftFill = leftBar;
            rightFill = rightBar;
            centerGlow = glow;
            collapsedLabel = collapsedText;
            startCollapsed = collapsedByDefault;
            prefsKey = persistentKey;
            _defaultAnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            CacheParent();
        }

        void Awake()
        {
            if (widgetRect == null)
                widgetRect = GetComponent<RectTransform>();
            CacheParent();
        }

        void OnEnable()
        {
            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(ToggleCollapsed);
                toggleButton.onClick.AddListener(ToggleCollapsed);
            }

            LoadStateIfNeeded();
            ApplyCollapseVisuals();
            ClampToParent();
        }

        void OnRectTransformDimensionsChange()
        {
            CacheParent();
            ClampToParent();
        }

        void OnDisable()
        {
            if (toggleButton != null)
                toggleButton.onClick.RemoveListener(ToggleCollapsed);
        }

        public void SetStatus(
            string wave,
            string phase,
            string left,
            string right,
            string leftSideName,
            string rightSideName,
            string footer,
            float leftRatio,
            float rightRatio,
            Color accent)
        {
            if (waveValue != null)
                waveValue.text = wave;
            if (phaseValue != null)
                phaseValue.text = phase;
            if (leftValue != null)
                leftValue.text = left;
            if (rightValue != null)
                rightValue.text = right;
            if (leftLabel != null)
                leftLabel.text = leftSideName;
            if (rightLabel != null)
                rightLabel.text = rightSideName;
            if (footerValue != null)
                footerValue.text = footer;
            if (leftFill != null)
                leftFill.fillAmount = Mathf.Clamp01(leftRatio);
            if (rightFill != null)
                rightFill.fillAmount = Mathf.Clamp01(rightRatio);
            if (centerGlow != null)
                centerGlow.color = accent;
            if (collapsedLabel != null)
                collapsedLabel.text = wave;
        }

        public void ToggleCollapsed()
        {
            _isCollapsed = !_isCollapsed;
            ApplyCollapseVisuals();
            ClampToParent();
            SaveState();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (widgetRect == null || _parentRect == null)
                return;

            widgetRect.SetAsLastSibling();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, eventData.position, eventData.pressEventCamera, out _dragStartPointer);
            _dragStartAnchored = widgetRect.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (widgetRect == null || _parentRect == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(_parentRect, eventData.position, eventData.pressEventCamera, out var localPoint);
            widgetRect.anchoredPosition = _dragStartAnchored + (localPoint - _dragStartPointer);
            ClampToParent();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            SaveState();
        }

        void CacheParent()
        {
            _parentRect = widgetRect != null ? widgetRect.parent as RectTransform : null;
        }

        void LoadStateIfNeeded()
        {
            if (_loadedState)
                return;

            _loadedState = true;
            _isCollapsed = PlayerPrefs.GetInt($"{prefsKey}.collapsed", startCollapsed ? 1 : 0) == 1;

            if (widgetRect != null && PlayerPrefs.HasKey($"{prefsKey}.x") && PlayerPrefs.HasKey($"{prefsKey}.y"))
            {
                widgetRect.anchoredPosition = new Vector2(
                    PlayerPrefs.GetFloat($"{prefsKey}.x"),
                    PlayerPrefs.GetFloat($"{prefsKey}.y"));
            }
            else if (widgetRect != null)
            {
                widgetRect.anchoredPosition = _defaultAnchoredPosition;
            }

            ClampToParent();
        }

        void ApplyCollapseVisuals()
        {
            if (bodyRoot != null)
                bodyRoot.gameObject.SetActive(!_isCollapsed);
            if (collapsedRoot != null)
                collapsedRoot.gameObject.SetActive(_isCollapsed);
            if (toggleLabel != null)
                toggleLabel.text = _isCollapsed ? "+" : "-";

            if (widgetRect != null)
            {
                widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _isCollapsed ? 74f : 452f);
                widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _isCollapsed ? 62f : 132f);
            }
        }

        void ClampToParent()
        {
            if (widgetRect == null || _parentRect == null)
                return;

            var parentSize = _parentRect.rect.size;
            if (parentSize.x <= 0.01f || parentSize.y <= 0.01f)
                return;

            float width = widgetRect.rect.width;
            float height = widgetRect.rect.height;
            const float margin = 8f;

            Vector2 parentPivot = _parentRect.pivot;
            Vector2 anchorCenter = (widgetRect.anchorMin + widgetRect.anchorMax) * 0.5f;
            Vector2 anchorLocal = new(
                (anchorCenter.x - parentPivot.x) * parentSize.x,
                (anchorCenter.y - parentPivot.y) * parentSize.y);

            Vector2 minPivot = new(
                -parentSize.x * parentPivot.x + width * widgetRect.pivot.x + margin,
                -parentSize.y * parentPivot.y + height * widgetRect.pivot.y + margin);
            Vector2 maxPivot = new(
                parentSize.x * (1f - parentPivot.x) - width * (1f - widgetRect.pivot.x) - margin,
                parentSize.y * (1f - parentPivot.y) - height * (1f - widgetRect.pivot.y) - margin);

            Vector2 pivotLocal = anchorLocal + widgetRect.anchoredPosition;
            pivotLocal.x = Mathf.Clamp(pivotLocal.x, minPivot.x, maxPivot.x);
            pivotLocal.y = Mathf.Clamp(pivotLocal.y, minPivot.y, maxPivot.y);
            widgetRect.anchoredPosition = pivotLocal - anchorLocal;
        }

        void SaveState()
        {
            if (widgetRect == null)
                return;

            PlayerPrefs.SetFloat($"{prefsKey}.x", widgetRect.anchoredPosition.x);
            PlayerPrefs.SetFloat($"{prefsKey}.y", widgetRect.anchoredPosition.y);
            PlayerPrefs.SetInt($"{prefsKey}.collapsed", _isCollapsed ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
