// LaneTabs.cs — Lane tab strip + enemy tint overlay
//
// SCENE SETUP:
//   Canvas
//   ├── LaneTabsRow (HorizontalLayoutGroup, anchored below InfoBar)
//   │   └── (tabs instantiated at runtime from TabPrefab)
//   └── EnemyLaneTint (Image, full-screen, alpha 0.4, raycastTarget=false)
//
// On match_ready: call Init(totalLanes, myLane).
// On tab click: updates SnapshotApplier.ViewingLane.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using CastleDefender.Net;

namespace CastleDefender.UI
{
    public class LaneTabs : MonoBehaviour
    {
        public Transform TabContainer;
        public Button    TabPrefab;
        public Image     EnemyLaneTint;
        public Button    ReturnToLaneButton;

        [Header("Colors")]
        public Color ColorMineActive    = new Color(0.2f, 0.7f, 0.6f);
        public Color ColorMineInactive  = new Color(0.1f, 0.4f, 0.35f);
        public Color ColorEnemyActive   = new Color(0.8f, 0.25f, 0.25f);
        public Color ColorEnemyInactive = new Color(0.45f, 0.1f, 0.1f);

        // ── State ─────────────────────────────────────────────────────────────
        Button[] _tabs;
        int      _myLane;
        int      _viewing = 0;

        Coroutine _tintCoroutine;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()
        {
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnMLMatchReady += HandleMatchReady;
            TryInitializeFromCurrentState();
        }

        void OnDisable()
        {
            if (NetworkManager.Instance == null) return;
            NetworkManager.Instance.OnMLMatchReady -= HandleMatchReady;
        }

        void HandleMatchReady(MLMatchReadyPayload p)
        {
            Init(p.playerCount, NetworkManager.Instance.MyLaneIndex);
        }

        // ─────────────────────────────────────────────────────────────────────
        public void Init(int totalLanes, int myLane)
        {
            _myLane  = myLane;
            _viewing = myLane;
            if (TabContainer == null)
                TabContainer = transform;
            EnsureReturnButton();

            // Build tab buttons only if the UI references are assigned
            if (TabContainer != null)
            {
                foreach (Transform child in TabContainer)
                    Destroy(child.gameObject);
            }

            _tabs = new Button[totalLanes];

            if (TabContainer != null && TabPrefab != null)
            {
                for (int i = 0; i < totalLanes; i++)
                {
                    int captured = i;
                    var tab = Instantiate(TabPrefab, TabContainer);
                    tab.GetComponentInChildren<TMP_Text>().text = GetLaneLabel(i, myLane);
                    tab.onClick.AddListener(() => SwitchTo(captured));
                    _tabs[i] = tab;
                }
                RefreshColors();
            }
            else
            {
                Debug.LogWarning("[LaneTabs] TabContainer or TabPrefab not assigned — " +
                                 "use number keys (1-4) to switch lanes.");
            }

            SetEnemyTint(false);
            RefreshReturnButton();
        }

        // Number keys 1-4 switch lanes even when no tab UI is set up
        void Update()
        {
            if (ReturnToLaneButton == null)
                TryInitializeFromCurrentState();

            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchTo(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchTo(3);
        }

        void TryInitializeFromCurrentState()
        {
            var nm = NetworkManager.Instance;
            var sa = SnapshotApplier.Instance;
            var ready = sa != null ? sa.LatestMLMatchReady : null;
            if (nm == null || ready == null || ready.playerCount <= 0)
                return;

            if (_tabs == null || _tabs.Length != ready.playerCount || ReturnToLaneButton == null)
                Init(ready.playerCount, nm.MyLaneIndex);
        }

        public void SwitchTo(int laneIdx)
        {
            _viewing = laneIdx;
            SnapshotApplier.Instance.ViewingLane = laneIdx;
            RefreshColors();
            bool isEnemy = SnapshotApplier.Instance == null || !SnapshotApplier.Instance.AreLanesAllied(laneIdx, _myLane);
            SetEnemyTint(isEnemy);
            RefreshReturnButton();
        }

        void RefreshColors()
        {
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                bool isActive = i == _viewing;
                bool isMine   = i == _myLane;
                bool isAlly   = SnapshotApplier.Instance != null && SnapshotApplier.Instance.AreLanesAllied(i, _myLane);
                var baseColor = ResolveTabBaseColor(i, isMine, isAlly);
                Color c = isActive
                    ? Color.Lerp(baseColor, Color.white, 0.18f)
                    : Color.Lerp(baseColor, Color.black, 0.30f);
                _tabs[i].image.color = c;
            }
        }

        string GetLaneLabel(int laneIdx, int myLane)
        {
            var sa = SnapshotApplier.Instance;
            var assignment = sa != null ? sa.GetLaneAssignment(laneIdx) : null;
            string label = assignment != null && !string.IsNullOrWhiteSpace(assignment.branchLabel)
                ? assignment.branchLabel
                : $"P{laneIdx + 1}";
            return laneIdx == myLane ? $"{label} (You)" : label;
        }

        Color ResolveTabBaseColor(int laneIdx, bool isMine, bool isAlly)
        {
            var sa = SnapshotApplier.Instance;
            if (sa != null)
            {
                var laneColor = sa.GetLaneColor(laneIdx, Color.clear);
                if (laneColor.a > 0f)
                    return laneColor;
            }

            if (isMine) return ColorMineActive;
            return isAlly ? ColorMineInactive : ColorEnemyInactive;
        }

        void SetEnemyTint(bool show)
        {
            if (EnemyLaneTint == null) return;

            if (_tintCoroutine != null) StopCoroutine(_tintCoroutine);

            if (show)
            {
                EnemyLaneTint.gameObject.SetActive(true);
                _tintCoroutine = StartCoroutine(FadeImageAlpha(EnemyLaneTint, 0f, 0.4f, 0.2f));
            }
            else
            {
                _tintCoroutine = StartCoroutine(FadeOutAndHide(EnemyLaneTint, 0.15f));
            }
        }

        void EnsureReturnButton()
        {
            if (ReturnToLaneButton != null)
                return;

            var parent = TabContainer != null && TabContainer.parent != null ? TabContainer.parent : transform;
            Button button;

            if (TabPrefab != null)
            {
                button = Instantiate(TabPrefab, parent);
            }
            else
            {
                button = BuildFallbackButton(parent);
            }

            button.name = "Btn_ReturnToLane";
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(ReturnToMyLane);

            var text = button.GetComponentInChildren<TMP_Text>();
            if (text != null)
                text.text = "Return To Lane";

            var rt = button.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-20f, -20f);
            }

            ReturnToLaneButton = button;
        }

        Button BuildFallbackButton(Transform parent)
        {
            var go = new GameObject("Btn_ReturnToLane", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = new Color(0.14f, 0.52f, 0.46f, 0.96f);

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180f, 44f);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelGo.transform.SetParent(go.transform, false);

            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(10f, 6f);
            labelRt.offsetMax = new Vector2(-10f, -6f);

            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 22f;
            tmp.color = Color.white;
            tmp.text = "Return To Lane";
            if (TMP_Settings.defaultFontAsset != null)
                tmp.font = TMP_Settings.defaultFontAsset;

            var colors = go.GetComponent<Button>().colors;
            colors.normalColor = image.color;
            colors.highlightedColor = new Color(0.18f, 0.62f, 0.55f, 1f);
            colors.pressedColor = new Color(0.10f, 0.38f, 0.34f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.18f, 0.18f, 0.18f, 0.55f);
            go.GetComponent<Button>().colors = colors;

            return go.GetComponent<Button>();
        }

        void RefreshReturnButton()
        {
            if (ReturnToLaneButton == null)
                return;

            ReturnToLaneButton.gameObject.SetActive(true);
            ReturnToLaneButton.interactable = _viewing != _myLane;
        }

        public void ReturnToMyLane()
        {
            SwitchTo(_myLane);
        }

        static IEnumerator FadeImageAlpha(Image img, float from, float to, float dur)
        {
            Color c = img.color;
            c.a = from;
            img.color = c;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                c.a = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
                img.color = c;
                yield return null;
            }
            c.a = to;
            img.color = c;
        }

        IEnumerator FadeOutAndHide(Image img, float dur)
        {
            yield return StartCoroutine(FadeImageAlpha(img, img.color.a, 0f, dur));
            img.gameObject.SetActive(false);
        }
    }
}
