using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    public class UiAmbientMotion : MonoBehaviour
    {
        [SerializeField] Vector2 driftAmplitude = new(12f, 8f);
        [SerializeField] float driftFrequency = 0.22f;
        [SerializeField] float secondaryFrequency = 0.13f;
        [SerializeField] float rotationAmplitude = 1.5f;
        [SerializeField] bool affectAlpha = true;
        [SerializeField] float minAlpha = 0.78f;
        [SerializeField] float maxAlpha = 1f;

        RectTransform _rectTransform;
        Graphic _graphic;
        Vector2 _basePosition;
        Vector3 _baseEuler;
        Color _baseColor;
        float _phase;

        void Awake()
        {
            _rectTransform = transform as RectTransform;
            _graphic = GetComponent<Graphic>();
            _phase = Random.Range(0f, Mathf.PI * 2f);

            if (_rectTransform != null)
            {
                _basePosition = _rectTransform.anchoredPosition;
                _baseEuler = _rectTransform.localEulerAngles;
            }

            if (_graphic != null)
                _baseColor = _graphic.color;
        }

        void Update()
        {
            float t = Time.unscaledTime + _phase;
            if (_rectTransform != null)
            {
                float x = Mathf.Sin(t * driftFrequency) * driftAmplitude.x;
                float y = Mathf.Cos(t * secondaryFrequency) * driftAmplitude.y;
                _rectTransform.anchoredPosition = _basePosition + new Vector2(x, y);
                _rectTransform.localEulerAngles = _baseEuler + new Vector3(0f, 0f, Mathf.Sin(t * driftFrequency * 0.7f) * rotationAmplitude);
            }

            if (_graphic != null && affectAlpha)
            {
                var color = _baseColor;
                float alphaLerp = Mathf.InverseLerp(-1f, 1f, Mathf.Sin(t * driftFrequency * 0.8f));
                color.a = Mathf.Lerp(minAlpha, maxAlpha, alphaLerp);
                _graphic.color = color;
            }
        }
    }
}
