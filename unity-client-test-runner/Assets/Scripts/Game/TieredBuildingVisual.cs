using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class TieredBuildingVisual : MonoBehaviour
    {
        public enum VisualMode
        {
            AdditiveLayers = 0,
            ExclusiveSwaps = 1,
        }

        [Header("Layout")]
        [SerializeField] VisualMode mode = VisualMode.AdditiveLayers;
        [SerializeField] GameObject baseModel;
        [SerializeField] GameObject tier2Visuals;
        [SerializeField] GameObject tier3Visuals;
        [SerializeField] GameObject[] optionalHigherTierVisuals = Array.Empty<GameObject>();

        [Header("Tint")]
        [SerializeField] Renderer[] neutralTintRenderers = Array.Empty<Renderer>();
        [SerializeField] Renderer[] portraitFrameRenderers = Array.Empty<Renderer>();
        [SerializeField] float[] tierTintMultipliers = { 1f, 0.94f, 0.88f, 0.82f };

        [Header("Preview")]
        [SerializeField] int previewTier = 1;

        readonly Dictionary<Renderer, Color> _baseColorCache = new();
        MaterialPropertyBlock _propertyBlock;

        public VisualMode Mode => mode;
        public int CurrentTier { get; private set; } = 1;
        public int MaxTier => Mathf.Max(1, CollectTierRoots().Length);

        void OnEnable()
        {
            ApplyTier(previewTier);
        }

        void OnValidate()
        {
            ApplyTier(previewTier);
        }

        public void ConfigureForEditor(
            VisualMode configuredMode,
            GameObject configuredBaseModel,
            GameObject configuredTier2Visuals,
            GameObject configuredTier3Visuals,
            GameObject[] configuredHigherTierVisuals,
            Renderer[] configuredNeutralTintRenderers,
            Renderer[] configuredPortraitFrameRenderers,
            float[] configuredTierTintMultipliers)
        {
            mode = configuredMode;
            baseModel = configuredBaseModel;
            tier2Visuals = configuredTier2Visuals;
            tier3Visuals = configuredTier3Visuals;
            optionalHigherTierVisuals = configuredHigherTierVisuals ?? Array.Empty<GameObject>();
            neutralTintRenderers = configuredNeutralTintRenderers ?? Array.Empty<Renderer>();
            portraitFrameRenderers = configuredPortraitFrameRenderers ?? configuredNeutralTintRenderers ?? Array.Empty<Renderer>();
            tierTintMultipliers = configuredTierTintMultipliers != null && configuredTierTintMultipliers.Length > 0
                ? configuredTierTintMultipliers
                : new[] { 1f, 0.94f, 0.88f, 0.82f };
            previewTier = 1;
            ApplyTier(previewTier);
        }

        public void SetPreviewTier(int tier)
        {
            previewTier = Mathf.Clamp(tier, 1, MaxTier);
            ApplyTier(previewTier);
        }

        public void ApplyTier(int tier)
        {
            var tierRoots = CollectTierRoots();
            if (tierRoots.Length == 0)
            {
                CurrentTier = 1;
                return;
            }

            CurrentTier = Mathf.Clamp(tier, 1, tierRoots.Length);

            for (int i = 0; i < tierRoots.Length; i++)
            {
                var tierRoot = tierRoots[i];
                if (tierRoot == null)
                    continue;

                bool active = mode == VisualMode.AdditiveLayers
                    ? CurrentTier >= i + 1
                    : CurrentTier == i + 1;
                if (tierRoot.activeSelf != active)
                    tierRoot.SetActive(active);
            }

            ApplyNeutralTint(GetTierTintMultiplier(CurrentTier));
        }

        public Renderer[] GetPortraitFrameRenderers()
        {
            return portraitFrameRenderers != null && portraitFrameRenderers.Length > 0
                ? portraitFrameRenderers
                : neutralTintRenderers ?? Array.Empty<Renderer>();
        }

        public Renderer[] GetNeutralTintRenderers()
        {
            return neutralTintRenderers ?? Array.Empty<Renderer>();
        }

        GameObject[] CollectTierRoots()
        {
            var roots = new List<GameObject>(4);
            if (baseModel != null)
                roots.Add(baseModel);
            if (tier2Visuals != null)
                roots.Add(tier2Visuals);
            if (tier3Visuals != null)
                roots.Add(tier3Visuals);
            if (optionalHigherTierVisuals != null)
            {
                for (int i = 0; i < optionalHigherTierVisuals.Length; i++)
                {
                    if (optionalHigherTierVisuals[i] != null)
                        roots.Add(optionalHigherTierVisuals[i]);
                }
            }

            return roots.ToArray();
        }

        float GetTierTintMultiplier(int tier)
        {
            if (tierTintMultipliers == null || tierTintMultipliers.Length == 0)
                return 1f;

            int index = Mathf.Clamp(tier - 1, 0, tierTintMultipliers.Length - 1);
            return Mathf.Clamp(tierTintMultipliers[index], 0.55f, 1.25f);
        }

        void ApplyNeutralTint(float multiplier)
        {
            if (neutralTintRenderers == null || neutralTintRenderers.Length == 0)
                return;

            _propertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < neutralTintRenderers.Length; i++)
            {
                var renderer = neutralTintRenderers[i];
                if (renderer == null)
                    continue;

                if (!_baseColorCache.TryGetValue(renderer, out var baseColor))
                {
                    baseColor = ResolveRendererBaseColor(renderer);
                    _baseColorCache[renderer] = baseColor;
                }

                Color tinted = new Color(
                    Mathf.Clamp01(baseColor.r * multiplier),
                    Mathf.Clamp01(baseColor.g * multiplier),
                    Mathf.Clamp01(baseColor.b * multiplier),
                    baseColor.a);

                renderer.GetPropertyBlock(_propertyBlock);
                var material = renderer.sharedMaterial;
                if (material != null)
                {
                    if (material.HasProperty("_BaseColor"))
                        _propertyBlock.SetColor("_BaseColor", tinted);
                    if (material.HasProperty("_Color"))
                        _propertyBlock.SetColor("_Color", tinted);
                }

                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        static Color ResolveRendererBaseColor(Renderer renderer)
        {
            if (renderer == null)
                return Color.white;

            var material = renderer.sharedMaterial;
            if (material == null)
                return Color.white;

            if (material.HasProperty("_BaseColor"))
                return material.GetColor("_BaseColor");
            if (material.HasProperty("_Color"))
                return material.GetColor("_Color");

            return Color.white;
        }
    }
}
