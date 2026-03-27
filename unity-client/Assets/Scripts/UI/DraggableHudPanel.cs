using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public class DraggableHudPanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] RectTransform widgetRect;
        [SerializeField] RectTransform bodyRoot;
        [SerializeField] RectTransform collapsedRoot;
        [SerializeField] Button toggleButton;
        [SerializeField] TMP_Text toggleLabel;
        [SerializeField] TMP_Text collapsedLabel;

        [Header("State")]
        [SerializeField] bool startCollapsed;
        [SerializeField] string prefsKey = "hud.panel";
        [SerializeField] Vector2 expandedSize = new(320f, 180f);
        [SerializeField] Vector2 collapsedSize = new(80f, 56f);

        RectTransform _parentRect;
        Vector2 _dragStartPointer;
        Vector2 _dragStartAnchored;
        Vector2 _defaultAnchoredPosition;
        bool _loadedState;
        bool _isCollapsed;

        public bool IsCollapsed => _isCollapsed;

        public void Configure(
            RectTransform rect,
            RectTransform body,
            RectTransform collapsed,
            Button toggle,
            TMP_Text toggleText,
            TMP_Text collapsedText,
            bool collapsedByDefault,
            string persistentKey,
            Vector2 expandedPanelSize,
            Vector2 collapsedPanelSize)
        {
            widgetRect = rect;
            bodyRoot = body;
            collapsedRoot = collapsed;
            UnbindToggle();
            toggleButton = toggle;
            toggleLabel = toggleText;
            collapsedLabel = collapsedText;
            startCollapsed = collapsedByDefault;
            prefsKey = persistentKey;
            expandedSize = expandedPanelSize;
            collapsedSize = collapsedPanelSize;
            _defaultAnchoredPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            CacheParent();
            BindToggle();
            LoadStateIfNeeded();
            ApplyCollapseVisuals();
            ClampToParent();
        }

        void Awake()
        {
            if (widgetRect == null)
                widgetRect = GetComponent<RectTransform>();
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

        void OnDisable()
        {
            UnbindToggle();
        }

        public void SetCollapsedLabel(string label)
        {
            if (collapsedLabel != null)
                collapsedLabel.text = label;
        }

        public void SetExpandedSize(Vector2 size)
        {
            expandedSize = size;
            ApplyCollapseVisuals();
            ClampToParent();
        }

        public void SetCollapsedSize(Vector2 size)
        {
            collapsedSize = size;
            ApplyCollapseVisuals();
            ClampToParent();
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
        }

        void ApplyCollapseVisuals()
        {
            if (bodyRoot != null)
                bodyRoot.gameObject.SetActive(!_isCollapsed);
            if (collapsedRoot != null)
                collapsedRoot.gameObject.SetActive(_isCollapsed);
            if (toggleLabel != null)
                toggleLabel.text = _isCollapsed ? "+" : "-";

            if (widgetRect == null)
                return;

            Vector2 currentSize = widgetRect.rect.size;
            Vector2 currentAnchoredPosition = widgetRect.anchoredPosition;
            Vector2 pivot = widgetRect.pivot;
            Vector2 targetSize = _isCollapsed ? collapsedSize : expandedSize;
            float preservedRight = currentAnchoredPosition.x + currentSize.x * (1f - pivot.x);
            float preservedTop = currentAnchoredPosition.y + currentSize.y * (1f - pivot.y);

            widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetSize.x);
            widgetRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetSize.y);
            widgetRect.anchoredPosition = new Vector2(
                preservedRight - targetSize.x * (1f - pivot.x),
                preservedTop - targetSize.y * (1f - pivot.y));
        }

        void ClampToParent()
        {
            if (widgetRect == null || _parentRect == null)
                return;

            var parentSize = _parentRect.rect.size;
            float width = widgetRect.rect.width;
            float height = widgetRect.rect.height;
            float minX = width * widgetRect.pivot.x + 8f;
            float maxX = parentSize.x - width * (1f - widgetRect.pivot.x) - 8f;
            float minY = -parentSize.y + height * widgetRect.pivot.y + 8f;
            float maxY = -(height * (1f - widgetRect.pivot.y)) - 8f;
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
