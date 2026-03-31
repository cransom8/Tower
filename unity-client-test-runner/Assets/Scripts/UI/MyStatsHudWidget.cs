using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public class MyStatsHudWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
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
        [SerializeField] string prefsKey = "hud.my_stats";

        RectTransform _parentRect;
        Canvas _canvas;
        Vector2 _dragStartPointer;
        Vector2 _dragStartAnchored;
        Vector2 _defaultAnchoredPosition;
        bool _loadedState;
        bool _isCollapsed;

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
        }

        void Awake()
        {
            if (widgetRect == null)
                widgetRect = GetComponent<RectTransform>();
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();
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

        void OnDisable()
        {
            if (toggleButton != null)
                toggleButton.onClick.RemoveListener(ToggleCollapsed);
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
                widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _isCollapsed ? 58f : 230f);
                widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, _isCollapsed ? 58f : 150f);
            }
        }

        void ClampToParent()
        {
            if (widgetRect == null || _parentRect == null)
                return;

            var parentSize = _parentRect.rect.size;
            float width = widgetRect.rect.width;
            float height = widgetRect.rect.height;
            float minX = 8f;
            float maxX = Mathf.Max(8f, parentSize.x - width - 8f);
            float minY = Mathf.Min(-8f, -parentSize.y + height + 8f);
            float maxY = -8f;
            float x = Mathf.Clamp(widgetRect.anchoredPosition.x, minX, maxX);
            float y = Mathf.Clamp(widgetRect.anchoredPosition.y, minY, maxY);
            widgetRect.anchoredPosition = new Vector2(x, y);
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
