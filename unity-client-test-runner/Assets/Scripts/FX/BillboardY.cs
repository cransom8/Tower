using UnityEngine;

namespace CastleDefender.FX
{
    /// <summary>
    /// Rotates a flat visual to fully face the camera and can compensate for
    /// gameplay zoom so world-space UI stays visually stable.
    /// </summary>
    public class BillboardY : MonoBehaviour
    {
        const float ReferenceOrthographicSize = 5.3f;
        const float ReferencePerspectiveFov = 41f;
        const float MinScaleMultiplier = 1f;
        const float MinFullZoomCompensationMultiplier = 0.25f;
        const float MaxScaleMultiplier = 7.5f;
        const float ExternalScaleEpsilon = 0.0001f;

        [Min(0.01f)]
        public float ScaleFactor = 1f;
        public bool CompensatePerspectiveZoom = false;
        public bool CompensateZoomIn = false;

        Camera _cam;
        Vector3 _baseLocalScale;
        Vector3 _lastAppliedScale;
        bool _hasBaseScale;

        void LateUpdate()
        {
            if (_cam == null)
                _cam = Camera.main;
            if (_cam == null)
                return;

            SyncBaseScale();

            Vector3 toCam = _cam.transform.position - transform.position;
            if (toCam.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation((-toCam).normalized, _cam.transform.up);
            ApplyZoomScale();
        }

        void SyncBaseScale()
        {
            if (!_hasBaseScale)
            {
                _baseLocalScale = transform.localScale;
                _hasBaseScale = true;
                return;
            }

            if ((transform.localScale - _lastAppliedScale).sqrMagnitude > ExternalScaleEpsilon)
                _baseLocalScale = transform.localScale;
        }

        void ApplyZoomScale()
        {
            float multiplier = 1f;
            if (_cam.orthographic)
            {
                float minMultiplier = CompensateZoomIn
                    ? MinFullZoomCompensationMultiplier
                    : MinScaleMultiplier;
                multiplier = Mathf.Clamp(
                    _cam.orthographicSize / ReferenceOrthographicSize,
                    minMultiplier,
                    MaxScaleMultiplier);
            }
            else if (CompensatePerspectiveZoom)
            {
                float referenceFovTangent = Mathf.Tan(ReferencePerspectiveFov * 0.5f * Mathf.Deg2Rad);
                float currentFovTangent = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                if (referenceFovTangent > 0.0001f && currentFovTangent > 0.0001f)
                {
                    float minMultiplier = CompensateZoomIn
                        ? MinFullZoomCompensationMultiplier
                        : MinScaleMultiplier;
                    multiplier = Mathf.Clamp(
                        currentFovTangent / referenceFovTangent,
                        minMultiplier,
                        MaxScaleMultiplier);
                }
            }

            multiplier *= Mathf.Max(0.01f, ScaleFactor);
            _lastAppliedScale = Vector3.Scale(_baseLocalScale, new Vector3(multiplier, multiplier, multiplier));
            transform.localScale = _lastAppliedScale;
        }
    }
}
