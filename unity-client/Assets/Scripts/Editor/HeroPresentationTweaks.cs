using System;
using CastleDefender.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class HeroPresentationTweaks
{
    public const float HeroScale = 1.1f;

    const string ShaderAssetPath = "Assets/Shaders/HeroFootGlow.shader";
    const string ShaderName = "CastleDefender/Hero Foot Glow";
    const string MaterialAssetPath = "Assets/Materials/Units/HeroFootGlow.mat";
    const string PrefabRoot = "Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/prefabs";
    const string RegistryPath = "Assets/Registry/UnitPrefabRegistry.asset";
    const string LegacyRegistryPath = "Assets/UnitPrefabRegistry.asset";
    const string FootGlowObjectName = "HeroFootGlow";

    static readonly string[] HeroSkinKeys =
    {
        "tt_king",
        "tt_paladin",
        "tt_commander",
    };

    static readonly string[] HeroPrefabNames =
    {
        "TT_King",
        "TT_Paladin",
        "TT_Commander",
    };

    [MenuItem("Castle Defender/Setup/Apply Hero Presentation Tweaks")]
    public static void Run()
    {
        var glowMaterial = EnsureHeroFootGlowMaterial();
        if (glowMaterial == null)
        {
            Debug.LogError("[HeroPresentationTweaks] Hero foot glow material could not be created.");
            return;
        }

        int updatedPrefabs = 0;
        for (int i = 0; i < HeroPrefabNames.Length; i++)
        {
            string prefabPath = $"{PrefabRoot}/{HeroPrefabNames[i]}.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogWarning($"[HeroPresentationTweaks] Prefab not found: {prefabPath}");
                continue;
            }

            using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                ApplyHeroFootGlowIfNeeded(editScope.prefabContentsRoot, glowMaterial);
            }

            updatedPrefabs++;
        }

        bool registryChanged = ApplyHeroScaleToRegistry(LoadRegistry());

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            $"[HeroPresentationTweaks] Updated {updatedPrefabs} hero prefab(s). registryChanged={registryChanged} material='{MaterialAssetPath}'.");
    }

    public static bool IsHeroSkin(string skinKey)
    {
        if (string.IsNullOrWhiteSpace(skinKey))
            return false;

        for (int i = 0; i < HeroSkinKeys.Length; i++)
        {
            if (string.Equals(HeroSkinKeys[i], skinKey.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static bool IsHeroPrefab(string prefabName)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
            return false;

        for (int i = 0; i < HeroPrefabNames.Length; i++)
        {
            if (string.Equals(HeroPrefabNames[i], prefabName.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static void ApplyHeroFootGlowIfNeeded(GameObject root)
    {
        if (root == null || !IsHeroPrefab(root.name))
            return;

        ApplyHeroFootGlowIfNeeded(root, EnsureHeroFootGlowMaterial());
    }

    public static void ApplyHeroFootGlowIfNeeded(GameObject root, Material glowMaterial)
    {
        if (root == null || !IsHeroPrefab(root.name) || glowMaterial == null)
            return;

        GameObject glowObject = FindOrCreateGlowObject(root);
        var glowTransform = glowObject.transform;
        glowTransform.SetParent(root.transform, false);
        glowTransform.localPosition = new Vector3(0f, 0.025f, 0f);
        glowTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        glowTransform.localScale = new Vector3(1.2f, 1.2f, 1f);

        glowObject.layer = root.layer;
        glowObject.tag = "Untagged";
        glowObject.SetActive(true);

        var meshFilter = glowObject.GetComponent<MeshFilter>() ?? glowObject.AddComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = GetQuadMesh();

        var renderer = glowObject.GetComponent<MeshRenderer>() ?? glowObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = glowMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.sortingOrder = -1;

        var collider = glowObject.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        EditorUtility.SetDirty(glowObject);
        EditorUtility.SetDirty(renderer);
    }

    public static bool ApplyHeroScaleToRegistry(UnitPrefabRegistry registry)
    {
        if (registry == null)
            return false;

        bool changed = false;

        var entries = registry.entries ?? Array.Empty<UnitPrefabRegistry.Entry>();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            if (!IsHeroSkin(entry.key) || Mathf.Approximately(entry.scale, HeroScale))
                continue;

            entry.scale = HeroScale;
            entries[i] = entry;
            changed = true;
        }

        var skinEntries = registry.skinEntries ?? Array.Empty<UnitPrefabRegistry.SkinEntry>();
        for (int i = 0; i < skinEntries.Length; i++)
        {
            var skinEntry = skinEntries[i];
            if (!IsHeroSkin(skinEntry.skinKey) || Mathf.Approximately(skinEntry.scale, HeroScale))
                continue;

            skinEntry.scale = HeroScale;
            skinEntries[i] = skinEntry;
            changed = true;
        }

        if (!changed)
            return false;

        registry.entries = entries;
        registry.skinEntries = skinEntries;
        registry.Rebuild();
        EditorUtility.SetDirty(registry);
        return true;
    }

    static UnitPrefabRegistry LoadRegistry()
    {
        return AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(RegistryPath)
            ?? AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>(LegacyRegistryPath);
    }

    static Material EnsureHeroFootGlowMaterial()
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderAssetPath);
        if (shader == null)
            shader = Shader.Find(ShaderName);

        if (shader == null)
        {
            Debug.LogError(
                $"[HeroPresentationTweaks] Hero foot glow shader is missing. Expected '{ShaderAssetPath}' or Shader.Find('{ShaderName}').");
            return null;
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        if (material == null)
        {
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Materials/Units");
            material = new Material(shader)
            {
                name = "HeroFootGlow",
            };
            AssetDatabase.CreateAsset(material, MaterialAssetPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        ConfigureGlowMaterial(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    static void ConfigureGlowMaterial(Material material)
    {
        if (material == null)
            return;

        if (material.HasProperty("_GlowColor"))
            material.SetColor("_GlowColor", new Color(1.00f, 0.84f, 0.36f, 0.90f));
        if (material.HasProperty("_OuterRadius"))
            material.SetFloat("_OuterRadius", 0.88f);
        if (material.HasProperty("_InnerRadius"))
            material.SetFloat("_InnerRadius", 0.18f);
        if (material.HasProperty("_EdgeSoftness"))
            material.SetFloat("_EdgeSoftness", 0.28f);
        if (material.HasProperty("_PulseSpeed"))
            material.SetFloat("_PulseSpeed", 1.25f);
        if (material.HasProperty("_PulseAmount"))
            material.SetFloat("_PulseAmount", 0.06f);
        if (material.HasProperty("_Alpha"))
            material.SetFloat("_Alpha", 0.80f);
    }

    static GameObject FindOrCreateGlowObject(GameObject root)
    {
        var existing = root.transform.Find(FootGlowObjectName);
        if (existing != null)
            return existing.gameObject;

        var glowObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        glowObject.name = FootGlowObjectName;
        Object.DestroyImmediate(glowObject.GetComponent<Collider>());
        return glowObject;
    }

    static Mesh GetQuadMesh()
    {
        var tempQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        try
        {
            return tempQuad.GetComponent<MeshFilter>().sharedMesh;
        }
        finally
        {
            Object.DestroyImmediate(tempQuad);
        }
    }

    static void EnsureFolder(string assetPath)
    {
        if (AssetDatabase.IsValidFolder(assetPath))
            return;

        string[] parts = assetPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
