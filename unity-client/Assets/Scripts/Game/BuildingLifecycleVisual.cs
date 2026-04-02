using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class BuildingLifecycleVisual : MonoBehaviour
    {
        [SerializeField] TieredBuildingVisual tieredVisual;
        [SerializeField] GameObject[] constructionStageRoots = Array.Empty<GameObject>();
        [SerializeField] GameObject hammerPrefab;
        [SerializeField] GameObject burningSmallFxPrefab;
        [SerializeField] GameObject burningLargeFxPrefab;
        [SerializeField] GameObject destroyedFxPrefab;

        GameObject _constructionFxRoot;
        BuildingConstructionFxAnimator _constructionAnimator;
        GameObject _damagedFxInstance;
        GameObject _destroyedFireFxInstance;
        GameObject _destroyedDebrisFxInstance;
        bool _constructionFxCreated;
        bool _damageFxCreated;
        bool _destroyedFxCreated;
        Bounds _cachedLocalBounds;
        bool _cachedLocalBoundsValid;

        static Material s_dustMaterial;

        void Awake()
        {
            tieredVisual ??= GetComponent<TieredBuildingVisual>();
            HideAllPresentation();
        }

        public void ConfigureForEditor(
            TieredBuildingVisual configuredTieredVisual,
            GameObject[] configuredConstructionStageRoots,
            GameObject configuredHammerPrefab,
            GameObject configuredBurningSmallFxPrefab,
            GameObject configuredBurningLargeFxPrefab,
            GameObject configuredDestroyedFxPrefab)
        {
            tieredVisual = configuredTieredVisual;
            constructionStageRoots = configuredConstructionStageRoots ?? Array.Empty<GameObject>();
            hammerPrefab = configuredHammerPrefab;
            burningSmallFxPrefab = configuredBurningSmallFxPrefab;
            burningLargeFxPrefab = configuredBurningLargeFxPrefab;
            destroyedFxPrefab = configuredDestroyedFxPrefab;
            _cachedLocalBoundsValid = false;
        }

        public void ApplyState(
            bool showTieredVisual,
            float constructionProgress01,
            bool showConstructionFx,
            bool showConstructionStages,
            bool showDamagedFx,
            bool showDestroyedFx)
        {
            tieredVisual?.SetVisualsVisible(showTieredVisual);
            SetConstructionStageIndex(showConstructionStages ? ResolveConstructionStageIndex(constructionProgress01) : -1);
            ToggleConstructionFx(showConstructionFx);
            ToggleDamageFx(showDamagedFx && !showDestroyedFx);
            ToggleDestroyedFx(showDestroyedFx);
        }

        public void HideAllPresentation()
        {
            tieredVisual?.SetVisualsVisible(true);
            SetConstructionStageIndex(-1);
            ToggleConstructionFx(false);
            ToggleDamageFx(false);
            ToggleDestroyedFx(false);
        }

        int ResolveConstructionStageIndex(float progress01)
        {
            int stageCount = 0;
            for (int i = 0; i < constructionStageRoots.Length; i++)
            {
                if (constructionStageRoots[i] != null)
                    stageCount += 1;
            }

            if (stageCount <= 0)
                return -1;

            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Clamp01(progress01) * stageCount),
                0,
                stageCount - 1);
        }

        void SetConstructionStageIndex(int activeIndex)
        {
            for (int i = 0; i < constructionStageRoots.Length; i++)
            {
                var root = constructionStageRoots[i];
                if (root == null)
                    continue;

                bool shouldShow = i == activeIndex;
                if (root.activeSelf != shouldShow)
                    root.SetActive(shouldShow);
            }
        }

        void ToggleConstructionFx(bool visible)
        {
            if (visible)
                EnsureConstructionFx();

            if (_constructionFxRoot != null)
                _constructionFxRoot.SetActive(visible);
        }

        void ToggleDamageFx(bool visible)
        {
            if (visible)
                EnsureDamageFx();

            if (_damagedFxInstance != null)
                _damagedFxInstance.SetActive(visible);
        }

        void ToggleDestroyedFx(bool visible)
        {
            if (visible)
                EnsureDestroyedFx();

            if (_destroyedFireFxInstance != null)
                _destroyedFireFxInstance.SetActive(visible);
            if (_destroyedDebrisFxInstance != null)
                _destroyedDebrisFxInstance.SetActive(visible);
        }

        void EnsureConstructionFx()
        {
            if (_constructionFxCreated)
                return;

            _constructionFxCreated = true;
            var bounds = ResolveLocalBounds();
            float footprint = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.z), 0.8f);
            float height = Mathf.Max(bounds.size.y, 0.8f);

            _constructionFxRoot = new GameObject("ConstructionFx");
            _constructionFxRoot.transform.SetParent(transform, false);
            _constructionFxRoot.transform.localPosition = new Vector3(
                bounds.center.x,
                bounds.center.y + (height * 0.18f),
                bounds.center.z);

            var hammerRoots = new List<Transform>(2);
            if (hammerPrefab != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    var hammer = Instantiate(hammerPrefab, _constructionFxRoot.transform, false);
                    hammer.name = $"Hammer_{i + 1}";
                    hammer.transform.localScale = Vector3.one * Mathf.Clamp(footprint * 0.26f, 0.30f, 0.75f);
                    hammerRoots.Add(hammer.transform);
                }
            }

            var dustRoot = new GameObject("Dust");
            dustRoot.transform.SetParent(_constructionFxRoot.transform, false);
            dustRoot.transform.localPosition = new Vector3(0f, -height * 0.1f, 0f);
            var dust = dustRoot.AddComponent<ParticleSystem>();
            ConfigureConstructionDust(dust, footprint, height);

            if (hammerRoots.Count > 0)
            {
                _constructionAnimator = _constructionFxRoot.AddComponent<BuildingConstructionFxAnimator>();
                _constructionAnimator.ConfigureForEditor(
                    hammerRoots.ToArray(),
                    Vector3.zero,
                    Mathf.Clamp(footprint * 0.22f, 0.22f, 1.05f),
                    Mathf.Clamp(height * 0.16f, 0.15f, 0.55f),
                    Mathf.Clamp(height * 0.12f, 0.08f, 0.30f));
            }

            _constructionFxRoot.SetActive(false);
        }

        void EnsureDamageFx()
        {
            if (_damageFxCreated)
                return;

            _damageFxCreated = true;
            var bounds = ResolveLocalBounds();
            float scale = ResolveFxScale(bounds, 0.42f, 0.55f, 1.15f);
            _damagedFxInstance = CreateFxInstance(
                "DamageFireFx",
                burningSmallFxPrefab,
                new Vector3(bounds.center.x, bounds.center.y + (bounds.size.y * 0.18f), bounds.center.z),
                scale);
            if (_damagedFxInstance != null)
                _damagedFxInstance.SetActive(false);
        }

        void EnsureDestroyedFx()
        {
            if (_destroyedFxCreated)
                return;

            _destroyedFxCreated = true;
            var bounds = ResolveLocalBounds();
            float fireScale = ResolveFxScale(bounds, 0.52f, 0.60f, 1.35f);
            float debrisScale = ResolveFxScale(bounds, 0.48f, 0.55f, 1.20f);

            _destroyedFireFxInstance = CreateFxInstance(
                "DestroyedFireFx",
                burningLargeFxPrefab != null ? burningLargeFxPrefab : burningSmallFxPrefab,
                new Vector3(bounds.center.x, bounds.center.y + (bounds.size.y * 0.12f), bounds.center.z),
                fireScale);
            _destroyedDebrisFxInstance = CreateFxInstance(
                "DestroyedDebrisFx",
                destroyedFxPrefab,
                new Vector3(bounds.center.x, bounds.center.y, bounds.center.z),
                debrisScale);

            if (_destroyedFireFxInstance != null)
                _destroyedFireFxInstance.SetActive(false);
            if (_destroyedDebrisFxInstance != null)
                _destroyedDebrisFxInstance.SetActive(false);
        }

        GameObject CreateFxInstance(string instanceName, GameObject prefab, Vector3 localPosition, float uniformScale)
        {
            if (prefab == null)
                return null;

            var instance = Instantiate(prefab, transform, false);
            instance.name = instanceName;
            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one * Mathf.Max(0.1f, uniformScale);

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return instance;
        }

        Bounds ResolveLocalBounds()
        {
            if (_cachedLocalBoundsValid)
                return _cachedLocalBounds;

            bool hasBounds = false;
            var renderers = new List<Renderer>();
            if (tieredVisual != null)
                renderers.AddRange(tieredVisual.GetComponentsInChildren<Renderer>(true));
            for (int i = 0; i < constructionStageRoots.Length; i++)
            {
                var stageRoot = constructionStageRoots[i];
                if (stageRoot == null)
                    continue;
                renderers.AddRange(stageRoot.GetComponentsInChildren<Renderer>(true));
            }

            Bounds worldBounds = default;
            for (int i = 0; i < renderers.Count; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    worldBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    worldBounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                _cachedLocalBounds = new Bounds(Vector3.zero, Vector3.one);
                _cachedLocalBoundsValid = true;
                return _cachedLocalBounds;
            }

            Vector3 lossyScale = transform.lossyScale;
            float safeScaleX = Mathf.Max(0.001f, Mathf.Abs(lossyScale.x));
            float safeScaleY = Mathf.Max(0.001f, Mathf.Abs(lossyScale.y));
            float safeScaleZ = Mathf.Max(0.001f, Mathf.Abs(lossyScale.z));
            _cachedLocalBounds = new Bounds(
                transform.InverseTransformPoint(worldBounds.center),
                new Vector3(
                    worldBounds.size.x / safeScaleX,
                    worldBounds.size.y / safeScaleY,
                    worldBounds.size.z / safeScaleZ));
            _cachedLocalBoundsValid = true;
            return _cachedLocalBounds;
        }

        void ConfigureConstructionDust(ParticleSystem particleSystem, float footprint, float height)
        {
            if (particleSystem == null)
                return;

            var main = particleSystem.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.40f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.18f, Mathf.Clamp(footprint * 0.18f, 0.25f, 0.55f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.92f, 0.90f, 0.82f, 0.50f),
                new Color(0.68f, 0.64f, 0.56f, 0.12f));
            main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.02f, 0.03f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;

            var emission = particleSystem.emission;
            emission.rateOverTime = Mathf.Clamp(footprint * 8f, 5f, 14f);

            var shape = particleSystem.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(
                Mathf.Clamp(footprint * 0.55f, 0.35f, 1.75f),
                Mathf.Clamp(height * 0.10f, 0.04f, 0.18f),
                Mathf.Clamp(footprint * 0.45f, 0.25f, 1.20f));

            var colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.96f, 0.94f, 0.88f), 0f),
                    new GradientColorKey(new Color(0.66f, 0.63f, 0.58f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.55f, 0f),
                    new GradientAlphaKey(0.18f, 0.45f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            var renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = ResolveDustMaterial();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static float ResolveFxScale(Bounds bounds, float multiplier, float min, float max)
        {
            float footprint = Mathf.Max(Mathf.Max(bounds.size.x, bounds.size.z), 0.8f);
            return Mathf.Clamp(footprint * multiplier, min, max);
        }

        static Material ResolveDustMaterial()
        {
            if (s_dustMaterial != null)
                return s_dustMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Standard");
            if (shader == null)
                return null;

            s_dustMaterial = new Material(shader);
            if (s_dustMaterial.HasProperty("_BaseColor"))
                s_dustMaterial.SetColor("_BaseColor", new Color(0.82f, 0.80f, 0.72f, 0.45f));
            if (s_dustMaterial.HasProperty("_Color"))
                s_dustMaterial.SetColor("_Color", new Color(0.82f, 0.80f, 0.72f, 0.45f));
            return s_dustMaterial;
        }
    }
}
