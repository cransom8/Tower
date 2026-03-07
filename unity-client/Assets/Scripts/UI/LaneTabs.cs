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
            if (NetworkManager.Instance == null) return;
            NetworkManager.Instance.OnMLMatchReady += HandleMatchReady;
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
                    tab.GetComponentInChildren<TMP_Text>().text =
                        i == myLane ? $"Lane {i + 1} (You)" : $"Lane {i + 1}";
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
        }

        // Number keys 1-4 switch lanes even when no tab UI is set up
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1)) SwitchTo(0);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) SwitchTo(1);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) SwitchTo(2);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) SwitchTo(3);
        }

        public void SwitchTo(int laneIdx)
        {
            _viewing = laneIdx;
            SnapshotApplier.Instance.ViewingLane = laneIdx;
            RefreshColors();
            SetEnemyTint(laneIdx != _myLane);
        }

        void RefreshColors()
        {
            if (_tabs == null) return;
            for (int i = 0; i < _tabs.Length; i++)
            {
                if (_tabs[i] == null) continue;
                bool isActive = i == _viewing;
                bool isMine   = i == _myLane;
                Color c = isMine
                    ? (isActive ? ColorMineActive  : ColorMineInactive)
                    : (isActive ? ColorEnemyActive : ColorEnemyInactive);
                _tabs[i].image.color = c;
            }
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
