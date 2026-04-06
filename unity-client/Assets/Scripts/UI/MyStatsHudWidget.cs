using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public class MyStatsHudWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public event Action<HudPanelLayoutChangeKind> LayoutCommitted;

        [SerializeField] RectTransform widgetRect;
        [SerializeField] RectTransform bodyRoot;
        [SerializeField] RectTransform collapsedRoot;
        [SerializeField] Button toggleButton;
        [SerializeField] TMP_Text toggleLabel;
        [SerializeField] TMP_Text goldValue;
        [SerializeField] TMP_Text incomeValue;
        [SerializeField] TMP_Text secondaryValue;
        [SerializeField] TMP_Text workersValue;
        [SerializeField] TMP_Text buildValue;
        [SerializeField] TMP_Text targetValue;
        [SerializeField] Image meterFill;
        [SerializeField] Image badgeGlow;

        [Header("State")]
        [SerializeField] bool startCollapsed;
        [SerializeField] string prefsKey = "hud.my_stats_widget.top_right_v4";

        RectTransform _parentRect;
        Canvas _canvas;
        Vector2 _dragStartPointer;
        Vector2 _dragStartAnchored;
        Vector2 _defaultAnchoredPosition;
        bool _loadedState;
        bool _isCollapsed;

        public RectTransform WidgetRect
        {
            get
            {
                if (widgetRect == null)
                    widgetRect = GetComponent<RectTransform>();

                return widgetRect;
            }
        }

        public void Configure(
            Canvas canvas,
            RectTransform rect,
            RectTransform body,
            RectTransform collapsed,
            Button toggle,
            TMP_Text toggleText,
            TMP_Text gold,
            TMP_Text income,
            TMP_Text secondary,
            TMP_Text workers,
            TMP_Text build,
            TMP_Text target,
            Image fill,
            Image glow,
            bool collapsedByDefault,
            string persistentKey)
        {
            _canvas = canvas;
            widgetRect = rect;
            bodyRoot = body;
            collapsedRoot = collapsed;
            toggleButton = toggle;
            toggleLabel = toggleText;
            goldValue = gold;
            incomeValue = income;
            secondaryValue = secondary;
            workersValue = workers;
            buildValue = build;
            targetValue = target;
            meterFill = fill;
            badgeGlow = glow;
            startCollapsed = collapsedByDefault;
            prefsKey = persistentKey;
            _defaultAnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            CacheParent();
            BindToggle();
            _loadedState = false;
            LoadStateIfNeeded();
            ApplyCollapseVisuals();
            ClampToParent();
        }

        void Awake()
        {
            if (widgetRect == null)
                widgetRect = GetComponent<RectTransform>();
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();
            _defaultAnchoredPosition = widgetRect != null ? widgetRect.anchoredPosition : Vector2.zero;
            CacheParent();
        }

        void OnEnable()
        {
            BindToggle();
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
            UnbindToggle();
        }

        public void SetStats(string gold, string income, string secondary, string workers, string build, string target, float meterRatio, Color meterColor)
        {
            if (goldValue != null)
                goldValue.text = gold;
            if (incomeValue != null)
                incomeValue.text = income;
            if (secondaryValue != null)
                secondaryValue.text = secondary;
            if (workersValue != null)
                workersValue.text = workers;
            if (buildValue != null)
                buildValue.text = build;
            if (targetValue != null)
                targetValue.text = target;

            if (meterFill != null)
            {
                meterFill.fillAmount = Mathf.Clamp01(meterRatio);
                meterFill.color = meterColor;
            }

            if (badgeGlow != null)
                badgeGlow.color = meterColor;
        }

        public void SetAnchoredPosition(Vector2 anchoredPosition, bool persist = false)
        {
            if (WidgetRect == null)
                return;

            widgetRect.anchoredPosition = anchoredPosition;
            ClampToParent();
            if (persist)
                SaveState();
        }

        public void ToggleCollapsed()
        {
            _isCollapsed = !_isCollapsed;
            ApplyCollapseVisuals();
            ClampToParent();
            SaveState();
            LayoutCommitted?.Invoke(HudPanelLayoutChangeKind.CollapseChanged);
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
            LayoutCommitted?.Invoke(HudPanelLayoutChangeKind.DragEnded);
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
                widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _isCollapsed ? 58f : 220f);
                widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _isCollapsed ? 58f : 80f);
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
            if (maxPivot.x < minPivot.x)
                maxPivot.x = minPivot.x;
            if (maxPivot.y < minPivot.y)
                maxPivot.y = minPivot.y;

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

        void BindToggle()
        {
            if (toggleButton == null)
                return;

            toggleButton.onClick.RemoveListener(ToggleCollapsed);
            toggleButton.onClick.AddListener(ToggleCollapsed);
        }

        void UnbindToggle()
        {
            if (toggleButton == null)
                return;

            toggleButton.onClick.RemoveListener(ToggleCollapsed);
        }
    }
}
