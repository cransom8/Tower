using System;
using UnityEngine;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class BuildingConstructionFxAnimator : MonoBehaviour
    {
        [SerializeField] Transform[] hammerTransforms = Array.Empty<Transform>();
        [SerializeField] Vector3 centerLocalOffset = Vector3.zero;
        [SerializeField] float orbitRadius = 0.55f;
        [SerializeField] float orbitHeight = 0.35f;
        [SerializeField] float orbitVerticalAmplitude = 0.14f;
        [SerializeField] float orbitSpeed = 1.25f;
        [SerializeField] float swingSpeed = 4.6f;
        [SerializeField] float swingAngle = 55f;

        float _phaseOffset;

        void Awake()
        {
            _phaseOffset = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        }

        public void ConfigureForEditor(
            Transform[] configuredHammerTransforms,
            Vector3 configuredCenterLocalOffset,
            float configuredOrbitRadius,
            float configuredOrbitHeight,
            float configuredOrbitVerticalAmplitude)
        {
            hammerTransforms = configuredHammerTransforms ?? Array.Empty<Transform>();
            centerLocalOffset = configuredCenterLocalOffset;
            orbitRadius = Mathf.Max(0.1f, configuredOrbitRadius);
            orbitHeight = Mathf.Max(0f, configuredOrbitHeight);
            orbitVerticalAmplitude = Mathf.Max(0f, configuredOrbitVerticalAmplitude);
        }

        void LateUpdate()
        {
            if (hammerTransforms == null || hammerTransforms.Length == 0)
                return;

            float time = (Time.time * orbitSpeed) + _phaseOffset;
            int hammerCount = hammerTransforms.Length;
            for (int i = 0; i < hammerCount; i++)
            {
                var hammer = hammerTransforms[i];
                if (hammer == null)
                    continue;

                float angle = time + ((Mathf.PI * 2f * i) / Mathf.Max(1, hammerCount));
                float swing = Mathf.Sin((Time.time * swingSpeed) + (i * 1.7f)) * swingAngle;
                hammer.localPosition = centerLocalOffset + new Vector3(
                    Mathf.Cos(angle) * orbitRadius,
                    orbitHeight + Mathf.Abs(Mathf.Sin((Time.time * swingSpeed * 0.6f) + (i * 1.3f))) * orbitVerticalAmplitude,
                    Mathf.Sin(angle) * orbitRadius * 0.65f);
                hammer.localRotation = Quaternion.Euler(-75f + swing, (-Mathf.Rad2Deg * angle) + 90f, 90f);
            }
        }
    }
}
