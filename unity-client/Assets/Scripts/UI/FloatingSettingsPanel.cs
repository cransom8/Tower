using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public class FloatingSettingsPanel : MonoBehaviour
    {
        [SerializeField] RectTransform rootRect;
        [SerializeField] RectTransform panelRect;
        [SerializeField] CanvasGroup panelCanvasGroup;
        [SerializeField] Button toggleButton;
        [SerializeField] bool startExpanded;
        [SerializeField] string prefsKey = "hud.settings_panel";
        [SerializeField] float animationDuration = 0.18f;
        [SerializeField] Vector2 expandedAnchoredPosition = new Vector2(-62f, 0f);
        [SerializeField] Vector2 collapsedAnchoredPosition = new Vector2(-42f, 0f);

        Coroutine _animateRoutine;
        bool _loadedState;
        bool _isExpanded;

        public bool IsExpanded => _isExpanded;

        public void Configure(
            RectTransform root,
            RectTransform panel,
            CanvasGroup canvasGroup,
            Button toggle,
            bool expandedByDefault,
            string persistentKey,
            Vector2 expandedPosition,
            Vector2 collapsedPosition)
        {
            rootRect = root;
            panelRect = panel;
            panelCanvasGroup = canvasGroup;
            toggleButton = toggle;
            startExpanded = expandedByDefault;
            prefsKey = persistentKey;
            expandedAnchoredPosition = expandedPosition;
            collapsedAnchoredPosition = collapsedPosition;

            BindToggle();
            LoadStateIfNeeded();
            ApplyState(_isExpanded ? 1f : 0f, _isExpanded ? expandedAnchoredPosition : collapsedAnchoredPosition);
        }

        void Awake()
        {
            if (rootRect == null)
                rootRect = GetComponent<RectTransform>();
            if (panelRect == null)
                panelRect = transform.Find("Panel") as RectTransform;
            if (panelCanvasGroup == null && panelRect != null)
                panelCanvasGroup = panelRect.GetComponent<CanvasGroup>();
            if (toggleButton == null)
                toggleButton = transform.Find("GearButton")?.GetComponent<Button>();
        }

        void OnEnable()
        {
            BindToggle();
            LoadStateIfNeeded();
            SetExpanded(_isExpanded, true);
        }

        void OnDisable()
        {
            if (toggleButton != null)
                toggleButton.onClick.RemoveListener(Toggle);
        }

        public void Toggle()
        {
            SetExpanded(!_isExpanded, false);
        }

        public void SetExpanded(bool expanded, bool immediate)
        {
            _isExpanded = expanded;

            if (_animateRoutine != null)
                StopCoroutine(_animateRoutine);

            if (immediate || !isActiveAndEnabled)
            {
                ApplyState(_isExpanded ? 1f : 0f, _isExpanded ? expandedAnchoredPosition : collapsedAnchoredPosition);
                SaveState();
                return;
            }

            _animateRoutine = StartCoroutine(AnimateState());
            SaveState();
        }

        IEnumerator AnimateState()
        {
            float duration = Mathf.Max(0.01f, animationDuration);
            float elapsed = 0f;
            float startAlpha = panelCanvasGroup != null ? panelCanvasGroup.alpha : (_isExpanded ? 0f : 1f);
            Vector2 startPosition = panelRect != null ? panelRect.anchoredPosition : Vector2.zero;
            float targetAlpha = _isExpanded ? 1f : 0f;
            Vector2 targetPosition = _isExpanded ? expandedAnchoredPosition : collapsedAnchoredPosition;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                ApplyState(
                    Mathf.Lerp(startAlpha, targetAlpha, t),
                    Vector2.Lerp(startPosition, targetPosition, t));
                yield return null;
            }

            ApplyState(targetAlpha, targetPosition);
            _animateRoutine = null;
        }

        void ApplyState(float alpha, Vector2 anchoredPosition)
        {
            if (panelRect != null)
                panelRect.anchoredPosition = anchoredPosition;

            if (panelCanvasGroup != null)
            {
                panelCanvasGroup.alpha = alpha;
                bool visible = alpha > 0.001f;
                panelCanvasGroup.interactable = visible;
                panelCanvasGroup.blocksRaycasts = visible;
            }
        }

        void LoadStateIfNeeded()
        {
            if (_loadedState)
                return;

            _loadedState = true;
            _isExpanded = PlayerPrefs.GetInt($"{prefsKey}.expanded", startExpanded ? 1 : 0) == 1;
        }

        void SaveState()
        {
            PlayerPrefs.SetInt($"{prefsKey}.expanded", _isExpanded ? 1 : 0);
            PlayerPrefs.Save();
        }

        void BindToggle()
        {
            if (toggleButton == null)
                return;

            toggleButton.onClick.RemoveListener(Toggle);
            toggleButton.onClick.AddListener(Toggle);
        }
    }
}
