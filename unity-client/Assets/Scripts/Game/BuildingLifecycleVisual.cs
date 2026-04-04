using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class BuildingLifecycleVisual : MonoBehaviour
    {
        [Serializable]
        public struct ConstructionVisualSet
        {
            public int targetTier;
            public GameObject[] stageRoots;
        }

        [SerializeField] TieredBuildingVisual tieredVisual;
        [SerializeField] GameObject[] constructionStageRoots = Array.Empty<GameObject>();
        [SerializeField] ConstructionVisualSet[] constructionVisualSets = Array.Empty<ConstructionVisualSet>();
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
        static Material s_hammerMaterial;

        void Awake()
        {
            tieredVisual ??= GetComponent<TieredBuildingVisual>();
            HideAllPresentation();
        }

        public void ConfigureForEditor(
            TieredBuildingVisual configuredTieredVisual,
            ConstructionVisualSet[] configuredConstructionVisualSets,
            GameObject configuredHammerPrefab,
            GameObject configuredBurningSmallFxPrefab,
            GameObject configuredBurningLargeFxPrefab,
            GameObject configuredDestroyedFxPrefab)
        {
            tieredVisual = configuredTieredVisual;
            constructionStageRoots = Array.Empty<GameObject>();
            constructionVisualSets = configuredConstructionVisualSets ?? Array.Empty<ConstructionVisualSet>();
            hammerPrefab = configuredHammerPrefab;
            burningSmallFxPrefab = configuredBurningSmallFxPrefab;
            burningLargeFxPrefab = configuredBurningLargeFxPrefab;
            destroyedFxPrefab = configuredDestroyedFxPrefab;
            _cachedLocalBoundsValid = false;
        }

        public bool HasConstructionStagesFor(int targetTier)
        {
            var stageRoots = ResolveConstructionStageRoots(targetTier);
            return CountValidStageRoots(stageRoots) > 0;
        }

        public void ApplyState(
            bool showTieredVisual,
            float constructionProgress01,
            bool showConstructionFx,
            bool showConstructionStages,
            int constructionTargetTier,
            bool showDamagedFx,
            bool showDestroyedFx)
        {
            tieredVisual?.SetVisualsVisible(showTieredVisual);
            var stageRoots = showConstructionStages
                ? ResolveConstructionStageRoots(constructionTargetTier)
                : null;
            SetConstructionStageIndex(
                stageRoots,
                showConstructionStages ? ResolveConstructionStageIndex(stageRoots, constructionProgress01) : -1);
            ToggleConstructionFx(showConstructionFx);
            ToggleDamageFx(showDamagedFx && !showDestroyedFx);
            ToggleDestroyedFx(showDestroyedFx);
        }

        public void HideAllPresentation()
        {
            tieredVisual?.SetVisualsVisible(true);
            SetConstructionStageIndex(null, -1);
            ToggleConstructionFx(false);
            ToggleDamageFx(false);
            ToggleDestroyedFx(false);
        }

        GameObject[] ResolveConstructionStageRoots(int targetTier)
        {
            int safeTargetTier = Mathf.Max(1, targetTier);
            if (constructionVisualSets != null && constructionVisualSets.Length > 0)
            {
                for (int i = 0; i < constructionVisualSets.Length; i++)
                {
                    var set = constructionVisualSets[i];
                    if (Mathf.Max(0, set.targetTier) != safeTargetTier)
                        continue;
                    if (CountValidStageRoots(set.stageRoots) > 0)
                        return set.stageRoots;
                }

                for (int i = 0; i < constructionVisualSets.Length; i++)
                {
                    var set = constructionVisualSets[i];
                    if (Mathf.Max(0, set.targetTier) > 0)
                        continue;
                    if (CountValidStageRoots(set.stageRoots) > 0)
                        return set.stageRoots;
                }
            }

            return constructionStageRoots ?? Array.Empty<GameObject>();
        }

        static int ResolveConstructionStageIndex(GameObject[] stageRoots, float progress01)
        {
            int stageCount = CountValidStageRoots(stageRoots);

            if (stageCount <= 0)
                return -1;

            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Clamp01(progress01) * stageCount),
                0,
                stageCount - 1);
        }

        static int CountValidStageRoots(GameObject[] stageRoots)
        {
            int stageCount = 0;
            if (stageRoots == null)
                return 0;

            for (int i = 0; i < stageRoots.Length; i++)
            {
                if (stageRoots[i] != null)
                    stageCount += 1;
            }

            return stageCount;
        }

        void SetConstructionStageIndex(GameObject[] activeRoots, int activeIndex)
        {
            if (constructionVisualSets != null && constructionVisualSets.Length > 0)
            {
                for (int setIndex = 0; setIndex < constructionVisualSets.Length; setIndex++)
                {
                    var stageRoots = constructionVisualSets[setIndex].stageRoots;
                    if (stageRoots == null)
                        continue;

                    bool showSet = ReferenceEquals(stageRoots, activeRoots) && activeIndex >= 0;
                    for (int i = 0; i < stageRoots.Length; i++)
                    {
                        var root = stageRoots[i];
                        if (root == null)
                            continue;

                        bool shouldShow = showSet && i == activeIndex;
                        if (root.activeSelf != shouldShow)
                            root.SetActive(shouldShow);
                    }

                    SetConstructionGroupRootsActive(stageRoots, showSet);
                }
            }

            bool showLegacyRoots = ReferenceEquals(constructionStageRoots, activeRoots) && activeIndex >= 0;
            if (constructionStageRoots == null)
                return;

            for (int i = 0; i < constructionStageRoots.Length; i++)
            {
                var root = constructionStageRoots[i];
                if (root == null)
                    continue;

                bool shouldShow = showLegacyRoots && i == activeIndex;
                if (root.activeSelf != shouldShow)
                    root.SetActive(shouldShow);
            }

            SetConstructionGroupRootsActive(constructionStageRoots, showLegacyRoots);
        }

        void SetConstructionGroupRootsActive(GameObject[] stageRoots, bool visible)
        {
            if (stageRoots == null || stageRoots.Length <= 0)
                return;

            var handledParents = new HashSet<Transform>();
            for (int i = 0; i < stageRoots.Length; i++)
            {
                var stageRoot = stageRoots[i];
                if (stageRoot == null)
                    continue;

                var parent = stageRoot.transform.parent;
                if (parent == null || parent == transform || !handledParents.Add(parent))
                    continue;

                if (parent.gameObject.activeSelf != visible)
                    parent.gameObject.SetActive(visible);
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
                    ApplyConstructionHammerMaterial(hammer);
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
            if (constructionStageRoots != null)
            {
                for (int i = 0; i < constructionStageRoots.Length; i++)
                {
                    var stageRoot = constructionStageRoots[i];
                    if (stageRoot == null)
                        continue;
                    renderers.AddRange(stageRoot.GetComponentsInChildren<Renderer>(true));
                }
            }

            if (constructionVisualSets != null)
            {
                for (int setIndex = 0; setIndex < constructionVisualSets.Length; setIndex++)
                {
                    var stageRoots = constructionVisualSets[setIndex].stageRoots;
                    if (stageRoots == null)
                        continue;

                    for (int i = 0; i < stageRoots.Length; i++)
                    {
                        var stageRoot = stageRoots[i];
                        if (stageRoot == null)
                            continue;
                        renderers.AddRange(stageRoot.GetComponentsInChildren<Renderer>(true));
                    }
                }
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

        static void ApplyConstructionHammerMaterial(GameObject hammerRoot)
        {
            if (hammerRoot == null)
                return;

            var material = ResolveHammerMaterial();
            if (material == null)
                return;

            foreach (var renderer in hammerRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                var replacementMaterials = renderer.sharedMaterials != null && renderer.sharedMaterials.Length > 0
                    ? new Material[renderer.sharedMaterials.Length]
                    : new[] { material };

                for (int i = 0; i < replacementMaterials.Length; i++)
                    replacementMaterials[i] = material;

                renderer.sharedMaterials = replacementMaterials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        static Material ResolveHammerMaterial()
        {
            if (s_hammerMaterial != null)
                return s_hammerMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            if (shader == null)
                return null;

            s_hammerMaterial = new Material(shader);
            if (s_hammerMaterial.HasProperty("_BaseColor"))
                s_hammerMaterial.SetColor("_BaseColor", new Color(0.48f, 0.45f, 0.40f, 1f));
            if (s_hammerMaterial.HasProperty("_Color"))
                s_hammerMaterial.SetColor("_Color", new Color(0.48f, 0.45f, 0.40f, 1f));
            if (s_hammerMaterial.HasProperty("_Smoothness"))
                s_hammerMaterial.SetFloat("_Smoothness", 0.18f);
            if (s_hammerMaterial.HasProperty("_Metallic"))
                s_hammerMaterial.SetFloat("_Metallic", 0.55f);
            return s_hammerMaterial;
        }
    }
}
