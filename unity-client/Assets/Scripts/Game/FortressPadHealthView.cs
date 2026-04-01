using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(FortressPadAnchor))]
    public sealed class FortressPadHealthView : MonoBehaviour
    {
        static readonly Color HealthyLabelColor = new(0.84f, 0.96f, 0.88f, 0.98f);
        static readonly Color WoundedLabelColor = new(1f, 0.80f, 0.66f, 0.98f);
        static readonly Color CriticalLabelColor = new(1f, 0.58f, 0.52f, 0.98f);
        static readonly System.Collections.Generic.HashSet<string> s_missingHpBarLogs = new();

        FortressPadAnchor _anchor;
        Transform _overlayRoot;
        Transform _labelRoot;
        TMP_Text _healthLabel;
        Transform _hpBarRoot;
        Transform _hpBarFill;
        Image _hpBarImage;
        Vector3 _hpBarFillBaseScale = Vector3.one;
        Vector3 _hpBarFillBaseLocalPosition = Vector3.zero;
        bool _subscribed;

        void Awake()
        {
            _anchor ??= GetComponent<FortressPadAnchor>();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;

            _anchor ??= GetComponent<FortressPadAnchor>();
            UserPreferencesManager.PreferencesChanged += HandlePreferencesChanged;
            SubscribeSnapshots();
            RefreshFromSnapshot();
        }

        void LateUpdate()
        {
            if (_overlayRoot == null || !_overlayRoot.gameObject.activeSelf || _labelRoot == null)
                return;

            FaceToCamera(_labelRoot);
        }

        void OnDisable()
        {
            if (!Application.isPlaying)
                return;

            UserPreferencesManager.PreferencesChanged -= HandlePreferencesChanged;
            UnsubscribeSnapshots();
            HideHud();
        }

        void OnDestroy()
        {
            UserPreferencesManager.PreferencesChanged -= HandlePreferencesChanged;
            if (_overlayRoot != null)
                Destroy(_overlayRoot.gameObject);
        }

        void SubscribeSnapshots()
        {
            if (_subscribed)
                return;

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier == null)
                return;

            snapshotApplier.OnMLSnapshotApplied += HandleSnapshotApplied;
            _subscribed = true;
        }

        void UnsubscribeSnapshots()
        {
            if (!_subscribed)
                return;

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null)
                snapshotApplier.OnMLSnapshotApplied -= HandleSnapshotApplied;
            _subscribed = false;
        }

        void HandleSnapshotApplied(MLSnapshot _)
        {
            RefreshFromSnapshot();
        }

        void RefreshFromSnapshot()
        {
            if (!UserPreferencesManager.ShowHealthBars)
            {
                HideHud();
                return;
            }

            if (_anchor == null)
            {
                HideHud();
                return;
            }

            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier == null
                || !FortressLaneResolver.TryResolveSnapshotLane(
                    snapshotApplier,
                    transform,
                    _anchor.AnchorLaneColor,
                    out var lane,
                    out _)
                || lane == null)
            {
                HideHud();
                return;
            }

            var pad = snapshotApplier.GetFortressPad(lane.laneIndex, _anchor.PadId);
            if (pad == null || !pad.isBuilt)
            {
                HideHud();
                return;
            }

            EnsureHud();
            if (_overlayRoot == null)
                return;

            _overlayRoot.gameObject.SetActive(true);
            UpdateHud(pad);
        }

        void HandlePreferencesChanged(UserPreferencesData _)
        {
            RefreshFromSnapshot();
        }

        void UpdateHud(MLFortressPad pad)
        {
            Vector3 focus = _anchor.LabelTransform != null
                ? _anchor.LabelTransform.position
                : _anchor.FocusTransform.position;

            Bounds bounds = _anchor.GetWorldBounds();
            float verticalOffset = Mathf.Clamp(bounds.size.y * 0.14f, 0.55f, 1.15f);
            focus.y = bounds.max.y + verticalOffset;
            _overlayRoot.position = focus;
            _overlayRoot.rotation = Quaternion.identity;

            if (_labelRoot != null)
            {
                _labelRoot.localPosition = Vector3.zero;
                FaceToCamera(_labelRoot);
            }

            float hp01 = pad.maxHp > 0f
                ? Mathf.Clamp01(pad.hp / pad.maxHp)
                : 1f;

            if (_healthLabel != null)
            {
                int displayHp = Mathf.Max(0, Mathf.RoundToInt(pad.hp));
                int displayMax = Mathf.Max(displayHp, Mathf.RoundToInt(pad.maxHp));
                _healthLabel.transform.localPosition = new Vector3(0f, 0.22f, 0f);
                _healthLabel.text = $"HP {displayHp}/{displayMax}";
                _healthLabel.color = ResolveHealthLabelColor(hp01);
            }

            if (_hpBarRoot != null)
            {
                _hpBarRoot.gameObject.SetActive(true);
                _hpBarRoot.localPosition = new Vector3(0f, -0.10f, 0f);
                _hpBarRoot.localScale = Vector3.one * 0.92f;
                HpBarVisuals.ApplyFill(_hpBarFill, _hpBarImage, hp01);
                if (_hpBarFill != null)
                {
                    _hpBarFill.localScale = new Vector3(
                        _hpBarFillBaseScale.x * hp01,
                        _hpBarFillBaseScale.y,
                        _hpBarFillBaseScale.z);
                    _hpBarFill.localPosition = new Vector3(
                        0.5f * hp01,
                        _hpBarFillBaseLocalPosition.y,
                        _hpBarFillBaseLocalPosition.z);
                }
            }
        }

        void EnsureHud()
        {
            if (_overlayRoot != null)
                return;

            _overlayRoot = new GameObject($"FortressPadHud_{name}").transform;
            _overlayRoot.SetParent(transform.parent, false);

            _labelRoot = new GameObject("Hud").transform;
            _labelRoot.SetParent(_overlayRoot, false);

            _healthLabel = CreateWorldLabel("Health", _labelRoot, 1.35f);
            EnsureHpBar();
        }

        void EnsureHpBar()
        {
            if (_hpBarRoot != null)
                return;

            var hpBarPrefab = ResolveHpBarPrefab();
            if (hpBarPrefab == null || _labelRoot == null)
                return;

            var bar = Instantiate(hpBarPrefab, _labelRoot);
            bar.name = "FortressPadHpBar";
            _hpBarRoot = bar.transform;

            HpBarVisuals.EnsureStyled(_hpBarRoot);
            _hpBarFill = FindChildRecursive(_hpBarRoot, "Fill");
            _hpBarImage = _hpBarFill != null
                ? _hpBarFill.GetComponent<Image>()
                : _hpBarRoot.GetComponentInChildren<Image>(true);
            _hpBarFillBaseScale = _hpBarFill != null ? _hpBarFill.localScale : Vector3.one;
            _hpBarFillBaseLocalPosition = _hpBarFill != null ? _hpBarFill.localPosition : Vector3.zero;
        }

        void HideHud()
        {
            if (_overlayRoot != null)
                _overlayRoot.gameObject.SetActive(false);
        }

        static TMP_Text CreateWorldLabel(string name, Transform parent, float fontSize)
        {
            var go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            if (go.GetComponent<CastleDefender.FX.BillboardY>() == null)
                go.AddComponent<CastleDefender.FX.BillboardY>();

            var tmp = go.GetComponent<TextMeshPro>();
            tmp.font = TMP_Settings.defaultFontAsset;
            tmp.fontSize = fontSize;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.outlineWidth = 0.15f;
            tmp.outlineColor = new Color(0f, 0f, 0f, 0.9f);
            return tmp;
        }

        static void FaceToCamera(Transform target)
        {
            if (target == null)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            Vector3 direction = target.position - cam.transform.position;
            if (direction.sqrMagnitude > 0.001f)
                target.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        static GameObject ResolveHpBarPrefab()
        {
            var hpBarPrefab = GameplayPresentationRoot.ResolveHpBarPrefab();
            if (hpBarPrefab != null)
                return hpBarPrefab;

            if (s_missingHpBarLogs.Add("GameplayPresentationRoot.HpBarPrefab"))
            {
                Debug.LogError(
                    "[FortressPadHealthView] Missing GameplayPresentationRoot.HpBarPrefab. " +
                    "Fortress pad HP bars will not render until the real scene reference is assigned.");
            }

            return null;
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return null;
            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        static Color ResolveHealthLabelColor(float hp01)
        {
            if (hp01 >= 0.7f)
                return HealthyLabelColor;
            if (hp01 >= 0.35f)
                return WoundedLabelColor;
            return CriticalLabelColor;
        }
    }
}
