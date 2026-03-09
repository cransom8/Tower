using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 8 — Place LVE particle effects in the lava gorge.
/// Menu: Castle Defender → Build Lava Particles
/// Re-runnable: destroys existing LavaParticles group before rebuilding.
/// </summary>
public class BuildLavaParticles
{
    const string PARTICLES = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Particles/";

    // Y levels
    const float LAVA_Y      = -28f;   // just above lava surface — geysers, smoke, distortion
    const float EMBER_Y     = -10f;   // mid-gorge height — embers drift higher

    [MenuItem("Castle Defender/Build Lava Particles")]
    public static void Build()
    {
        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[LavaParticles] 'Map' not found."); return; }

        // Clean up previous run
        Transform existing = map.transform.Find("LavaParticles");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        GameObject root = new GameObject("LavaParticles");
        root.transform.SetParent(map.transform, false);

        System.Random rng = new System.Random(7);
        int total = 0;

        // --- Lava Ejaculation geysers near rock zones ---
        // Placed at lava surface level, scattered within each gorge X/Z range
        var geyserPrefab      = Load(PARTICLES + "Lava Ejaculation Small.prefab");
        var geyserPrefab2     = Load(PARTICLES + "Lava Ejaculation Small 2.prefab");
        var geyserBigPrefab   = Load(PARTICLES + "Lava Ejaculation Big.prefab");

        if (geyserPrefab != null && geyserPrefab2 != null)
        {
            // Outer gorges (AB, EF) — bigger, more geysers
            total += ScatterParticles(root, rng, new[]{ geyserBigPrefab ?? geyserPrefab, geyserPrefab, geyserPrefab2 },
                "Geyser_AB",  -218f, -164f, -35f, 35f, LAVA_Y, 4, 1f, 2f);
            total += ScatterParticles(root, rng, new[]{ geyserBigPrefab ?? geyserPrefab, geyserPrefab, geyserPrefab2 },
                "Geyser_EF",   164f,  218f, -35f, 35f, LAVA_Y, 4, 1f, 2f);

            // Inner corridor gorges — smaller
            total += ScatterParticles(root, rng, new[]{ geyserPrefab, geyserPrefab2 },
                "Geyser_BC_Hi", -104f, -50f, 12f, 43f, LAVA_Y, 2, 0.8f, 1.5f);
            total += ScatterParticles(root, rng, new[]{ geyserPrefab, geyserPrefab2 },
                "Geyser_BC_Lo", -104f, -50f, -43f, -12f, LAVA_Y, 2, 0.8f, 1.5f);
            total += ScatterParticles(root, rng, new[]{ geyserPrefab, geyserPrefab2 },
                "Geyser_DE_Hi",  50f, 104f, 12f, 43f, LAVA_Y, 2, 0.8f, 1.5f);
            total += ScatterParticles(root, rng, new[]{ geyserPrefab, geyserPrefab2 },
                "Geyser_DE_Lo",  50f, 104f, -43f, -12f, LAVA_Y, 2, 0.8f, 1.5f);
        }

        // --- Lava Smoke rising from lava surface ---
        var smokePrefab  = Load(PARTICLES + "Lava Smoke.prefab");
        var smokeSmall   = Load(PARTICLES + "Lava Smoke Small.prefab");

        if (smokePrefab != null && smokeSmall != null)
        {
            total += ScatterParticles(root, rng, new[]{ smokePrefab, smokeSmall },
                "Smoke_AB",  -218f, -164f, -38f, 38f, LAVA_Y, 5, 1f, 1.8f);
            total += ScatterParticles(root, rng, new[]{ smokePrefab, smokeSmall },
                "Smoke_EF",   164f,  218f, -38f, 38f, LAVA_Y, 5, 1f, 1.8f);
            total += ScatterParticles(root, rng, new[]{ smokeSmall },
                "Smoke_BC",  -104f,  -50f, -43f, 43f, LAVA_Y, 4, 0.8f, 1.4f);
            total += ScatterParticles(root, rng, new[]{ smokeSmall },
                "Smoke_DE",    50f,  104f, -43f, 43f, LAVA_Y, 4, 0.8f, 1.4f);
        }

        // --- Heat distortion over lava surface ---
        var distortion = Load(PARTICLES + "Lava Distortion.prefab");
        if (distortion != null)
        {
            // One distortion emitter per gorge, centred
            Place(root, distortion, "Distortion_AB",  new Vector3(-191f, LAVA_Y,  0f), 3f);
            Place(root, distortion, "Distortion_BC",  new Vector3( -77f, LAVA_Y,  25f), 2f);
            Place(root, distortion, "Distortion_BC2", new Vector3( -77f, LAVA_Y, -25f), 2f);
            Place(root, distortion, "Distortion_DE",  new Vector3(  77f, LAVA_Y,  25f), 2f);
            Place(root, distortion, "Distortion_DE2", new Vector3(  77f, LAVA_Y, -25f), 2f);
            Place(root, distortion, "Distortion_EF",  new Vector3( 191f, LAVA_Y,  0f), 3f);
            total += 6;
        }

        // --- Fire embers drifting across the whole gorge ---
        var embers = Load(PARTICLES + "Vulcano FireEmbers.prefab");
        if (embers != null)
        {
            // Three wide emitters spread across the map at mid-height
            Place(root, embers, "Embers_Left",   new Vector3(-191f, EMBER_Y,  0f), 2f);
            Place(root, embers, "Embers_Centre",  new Vector3(   0f, EMBER_Y,  0f), 2f);
            Place(root, embers, "Embers_Right",  new Vector3( 191f, EMBER_Y,  0f), 2f);
            total += 3;
        }

        // --- Glow light particles near lava surface ---
        var light1 = Load(PARTICLES + "ParticlesLight_1.prefab");
        var light2 = Load(PARTICLES + "ParticlesLight_2.prefab");
        if (light1 != null && light2 != null)
        {
            total += ScatterParticles(root, rng, new[]{ light1, light2 },
                "GlowLights_AB",  -218f, -164f, -38f, 38f, LAVA_Y + 2f, 3, 1f, 1.5f);
            total += ScatterParticles(root, rng, new[]{ light1, light2 },
                "GlowLights_EF",   164f,  218f, -38f, 38f, LAVA_Y + 2f, 3, 1f, 1.5f);
            total += ScatterParticles(root, rng, new[]{ light1, light2 },
                "GlowLights_Mid", -104f,  104f, -43f, 43f, LAVA_Y + 2f, 4, 1f, 1.5f);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[LavaParticles] Placed {total} particle emitters.");
    }

    // Scatter count prefabs randomly within an XZ bounding box at a fixed Y
    static int ScatterParticles(GameObject parent, System.Random rng, GameObject[] prefabs,
        string groupName, float x0, float x1, float z0, float z1, float y,
        int count, float scaleMin, float scaleMax)
    {
        GameObject group = new GameObject(groupName);
        group.transform.SetParent(parent.transform, false);

        for (int i = 0; i < count; i++)
        {
            float x = x0 + (float)rng.NextDouble() * (x1 - x0);
            float z = z0 + (float)rng.NextDouble() * (z1 - z0);
            float s = scaleMin + (float)rng.NextDouble() * (scaleMax - scaleMin);
            float yRot = (float)rng.NextDouble() * 360f;

            GameObject prefab = prefabs[rng.Next(prefabs.Length)];
            if (prefab == null) continue;

            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            inst.name = $"{groupName}_{i:00}";
            inst.transform.position = new Vector3(x, y, z);
            inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
            inst.transform.localScale = Vector3.one * s;
        }
        return count;
    }

    static void Place(GameObject parent, GameObject prefab, string objName, Vector3 pos, float scale)
    {
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        inst.name = objName;
        inst.transform.position = pos;
        inst.transform.localScale = Vector3.one * scale;
    }

    static GameObject Load(string path)
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (go == null) Debug.LogWarning($"[LavaParticles] Not found: {path}");
        return go;
    }
}
