using UnityEngine;
using UnityEngine.EventSystems;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public sealed class DraggablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] RectTransform panelRect;
        [SerializeField] string prefsKey = "runtime.panel";
        [SerializeField] float clampMargin = 8f;

        RectTransform _parentRect;
        Vector2 _dragStartPointer;
        Vector2 _dragStartAnchored;
        Vector2 _defaultAnchoredPosition;
        bool _loadedState;

        public void Configure(RectTransform targetPanel, string persistentKey, float margin = 8f)
        {
            panelRect = targetPanel;
            prefsKey = string.IsNullOrWhiteSpace(persistentKey) ? prefsKey : persistentKey;
            clampMargin = Mathf.Max(0f, margin);
            _defaultAnchoredPosition = panelRect != null ? panelRect.anchoredPosition : Vector2.zero;
            CacheParent();
            LoadStateIfNeeded();
            ClampToParent();
        }

        void Awake()
        {
            if (panelRect == null)
                panelRect = transform as RectTransform;

            _defaultAnchoredPosition = panelRect != null ? panelRect.anchoredPosition : Vector2.zero;
            CacheParent();
            LoadStateIfNeeded();
            ClampToParent();
        }

        void OnEnable()
        {
            CacheParent();
            LoadStateIfNeeded();
            ClampToParent();
        }

        void OnRectTransformDimensionsChange()
        {
            CacheParent();
            ClampToParent();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (panelRect == null || _parentRect == null)
                return;

            panelRect.SetAsLastSibling();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect,
                eventData.position,
                eventData.pressEventCamera,
                out _dragStartPointer);
            _dragStartAnchored = panelRect.anchoredPosition;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (panelRect == null || _parentRect == null)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRect,
                eventData.position,
                eventData.pressEventCamera,
                out var localPoint);
            panelRect.anchoredPosition = _dragStartAnchored + (localPoint - _dragStartPointer);
            ClampToParent();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            SaveState();
        }

        void CacheParent()
        {
            _parentRect = panelRect != null ? panelRect.parent as RectTransform : null;
        }

        void LoadStateIfNeeded()
        {
            if (_loadedState || panelRect == null || string.IsNullOrWhiteSpace(prefsKey))
                return;

            _loadedState = true;
            string xKey = $"{prefsKey}.x";
            string yKey = $"{prefsKey}.y";
            if (PlayerPrefs.HasKey(xKey) && PlayerPrefs.HasKey(yKey))
            {
                panelRect.anchoredPosition = new Vector2(
                    PlayerPrefs.GetFloat(xKey),
                    PlayerPrefs.GetFloat(yKey));
                return;
            }

            panelRect.anchoredPosition = _defaultAnchoredPosition;
        }

        void ClampToParent()
        {
            if (panelRect == null || _parentRect == null)
                return;

            var parentSize = _parentRect.rect.size;
            if (parentSize.x <= 0.01f || parentSize.y <= 0.01f)
                return;

            float width = panelRect.rect.width;
            float height = panelRect.rect.height;
            Vector2 parentPivot = _parentRect.pivot;
            Vector2 anchorCenter = (panelRect.anchorMin + panelRect.anchorMax) * 0.5f;
            Vector2 anchorLocal = new(
                (anchorCenter.x - parentPivot.x) * parentSize.x,
                (anchorCenter.y - parentPivot.y) * parentSize.y);

            Vector2 minPivot = new(
                -parentSize.x * parentPivot.x + width * panelRect.pivot.x + clampMargin,
                -parentSize.y * parentPivot.y + height * panelRect.pivot.y + clampMargin);
            Vector2 maxPivot = new(
                parentSize.x * (1f - parentPivot.x) - width * (1f - panelRect.pivot.x) - clampMargin,
                parentSize.y * (1f - parentPivot.y) - height * (1f - panelRect.pivot.y) - clampMargin);

            Vector2 pivotLocal = anchorLocal + panelRect.anchoredPosition;
            pivotLocal.x = Mathf.Clamp(pivotLocal.x, minPivot.x, maxPivot.x);
            pivotLocal.y = Mathf.Clamp(pivotLocal.y, minPivot.y, maxPivot.y);
            panelRect.anchoredPosition = pivotLocal - anchorLocal;
        }

        void SaveState()
        {
            if (panelRect == null || string.IsNullOrWhiteSpace(prefsKey))
                return;

            PlayerPrefs.SetFloat($"{prefsKey}.x", panelRect.anchoredPosition.x);
            PlayerPrefs.SetFloat($"{prefsKey}.y", panelRect.anchoredPosition.y);
            PlayerPrefs.Save();
        }
    }
}
