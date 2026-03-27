using TMPro;
using UnityEngine;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public sealed class RuntimeFailureMarker : MonoBehaviour
    {
        const string MarkerPrefix = "__RuntimeFailureMarker__";

        [SerializeField] TextMeshPro label;
        [SerializeField] Vector3 localOffset = new(0f, 2.4f, 0f);

        Camera _cachedCamera;

        public static void Mark(Transform parent, string key, string message, Vector3? localMarkerOffset = null)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key))
                return;

            var markerName = MarkerPrefix + SanitizeKey(key);
            var markerTransform = parent.Find(markerName);
            RuntimeFailureMarker marker;
            if (markerTransform == null)
            {
                var markerGo = new GameObject(markerName);
                markerGo.transform.SetParent(parent, false);
                marker = markerGo.AddComponent<RuntimeFailureMarker>();
            }
            else
            {
                marker = markerTransform.GetComponent<RuntimeFailureMarker>();
                if (marker == null)
                    marker = markerTransform.gameObject.AddComponent<RuntimeFailureMarker>();
            }

            if (localMarkerOffset.HasValue)
                marker.localOffset = localMarkerOffset.Value;

            marker.SetMessage(message);
        }

        public static void MarkWorld(Transform parent, string key, Vector3 worldPosition, string message)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key))
                return;

            var markerName = MarkerPrefix + SanitizeKey(key);
            Transform markerTransform = parent.Find(markerName);
            RuntimeFailureMarker marker;
            if (markerTransform == null)
            {
                var markerGo = new GameObject(markerName);
                markerGo.transform.SetParent(parent, true);
                markerGo.transform.position = worldPosition;
                marker = markerGo.AddComponent<RuntimeFailureMarker>();
                marker.localOffset = Vector3.zero;
            }
            else
            {
                markerTransform.position = worldPosition;
                marker = markerTransform.GetComponent<RuntimeFailureMarker>();
                if (marker == null)
                    marker = markerTransform.gameObject.AddComponent<RuntimeFailureMarker>();
                marker.localOffset = Vector3.zero;
            }

            marker.SetMessage(message);
        }

        public static void Clear(Transform parent, string key)
        {
            if (parent == null || string.IsNullOrWhiteSpace(key))
                return;

            var markerTransform = parent.Find(MarkerPrefix + SanitizeKey(key));
            if (markerTransform != null)
                Destroy(markerTransform.gameObject);
        }

        void Awake()
        {
            EnsureLabel();
            ApplyPose();
        }

        void LateUpdate()
        {
            ApplyPose();
            FaceCamera();
        }

        void EnsureLabel()
        {
            if (label != null)
                return;

            var labelGo = new GameObject("Label", typeof(TextMeshPro));
            labelGo.transform.SetParent(transform, false);
            label = labelGo.GetComponent<TextMeshPro>();
            label.fontSize = 2.5f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.rectTransform.sizeDelta = new Vector2(8f, 2.4f);
            label.color = new Color(1f, 0.28f, 0.22f, 0.98f);
            label.outlineWidth = 0.2f;
            if (TMP_Settings.defaultFontAsset != null)
                label.font = TMP_Settings.defaultFontAsset;
        }

        void ApplyPose()
        {
            if (label == null)
                return;

            label.transform.localPosition = localOffset;
            label.transform.localRotation = Quaternion.identity;
            label.transform.localScale = Vector3.one * 0.18f;
        }

        void FaceCamera()
        {
            if (label == null)
                return;

            _cachedCamera = Camera.main != null ? Camera.main : _cachedCamera;
            if (_cachedCamera == null)
                return;

            Vector3 toCamera = label.transform.position - _cachedCamera.transform.position;
            if (toCamera.sqrMagnitude <= 0.001f)
                return;

            label.transform.rotation = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        }

        void SetMessage(string message)
        {
            EnsureLabel();
            if (label != null)
                label.text = $"BROKEN\n{message}";
        }

        static string SanitizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return "marker";

            var chars = key.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]))
                    chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
