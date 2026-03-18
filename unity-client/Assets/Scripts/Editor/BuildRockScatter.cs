using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 7 — Scatter LVE lava rock prefabs in the gorge zones between islands.
/// Menu: Castle Defender → Build Rock Scatter
/// Re-runnable: destroys existing RockScatter group before rebuilding.
/// Uses seeded RNG for deterministic placement.
/// </summary>
public class BuildRockScatter
{
    const string ROCKS_VP = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Rocks/Vertex Paint Prefabs/";

    static readonly string[] BigPrefabs =
    {
        ROCKS_VP + "prefab_A_big_lava_rock_01.prefab",
        ROCKS_VP + "prefab_A_big_lava_rock_02.prefab",
        ROCKS_VP + "prefab_B_big_lava_rock_01.prefab",
        ROCKS_VP + "prefab_B_big_lava_rock_02.prefab",
    };

    static readonly string[] MediumPrefabs =
    {
        ROCKS_VP + "prefab_A_medium_lava_rock_01.prefab",
        ROCKS_VP + "prefab_A_medium_lava_rock_02.prefab",
        ROCKS_VP + "prefab_A_medium_lava_rock_04.prefab",
        ROCKS_VP + "prefab_A_medium_lava_rock_05.prefab",
        ROCKS_VP + "prefab_A_medium_lava_rock_06.prefab",
    };

    // Scale ranges applied on top of natural prefab size
    const float BIG_SCALE_MIN    = 0.3f;
    const float BIG_SCALE_MAX    = 0.7f;
    const float MEDIUM_SCALE_MIN = 0.2f;
    const float MEDIUM_SCALE_MAX = 0.5f;

    // Y range — deep in the lava gorge, well below island tops (top = Y 1)
    const float Y_MIN = -22f;
    const float Y_MAX = -14f;

    // Scatter zones pushed to the far SIDES of the map (Z ±50–75, extreme X ends).
    // Islands occupy roughly Z ±22.5 max; keeping rocks at Z ±50+ keeps the game
    // board completely clear when viewed from the player camera.
    static readonly (string id, float x0, float x1, float z0, float z1, int big, int med)[] Zones =
    {
        // Far NORTH edge (z 50–75) — runs the full map width
        ("FarNorth_W", -160f,  -60f,  50f,  75f,   4,  5),
        ("FarNorth_C",  -60f,   60f,  50f,  75f,   4,  5),
        ("FarNorth_E",   60f,  160f,  50f,  75f,   4,  5),
        // Far SOUTH edge (z -75 – -50) — runs the full map width
        ("FarSouth_W", -160f,  -60f, -75f, -50f,   4,  5),
        ("FarSouth_C",  -60f,   60f, -75f, -50f,   4,  5),
        ("FarSouth_E",   60f,  160f, -75f, -50f,   4,  5),
        // Far WEST end (x -175 – -158) — beside Dwarf island
        ("FarWest",    -175f, -158f, -40f,  40f,   3,  4),
        // Far EAST end (x 158–175) — beside Goblin island
        ("FarEast",     158f,  175f, -40f,  40f,   3,  4),
    };

    [MenuItem("Castle Defender/Build Rock Scatter")]
    public static void Build()
    {
        GameObject[] bigPrefabs    = LoadPrefabs(BigPrefabs,    "big");
        GameObject[] mediumPrefabs = LoadPrefabs(MediumPrefabs, "medium");
        if (bigPrefabs == null || mediumPrefabs == null) return;

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[RockScatter] 'Map' not found."); return; }

        // Clean up previous run
        Transform existing = map.transform.Find("RockScatter");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        GameObject scatterRoot = new GameObject("RockScatter");
        scatterRoot.transform.SetParent(map.transform, false);

        // Seeded RNG for deterministic results
        System.Random rng = new System.Random(42);
        int total = 0;

        foreach (var zone in Zones)
        {
            GameObject zoneGo = new GameObject(zone.id);
            zoneGo.transform.SetParent(scatterRoot.transform, false);

            // Big rocks
            for (int i = 0; i < zone.big; i++)
            {
                float x   = Lerp(zone.x0, zone.x1, (float)rng.NextDouble());
                float z   = Lerp(zone.z0, zone.z1, (float)rng.NextDouble());
                float y   = Lerp(Y_MIN,   Y_MAX,   (float)rng.NextDouble());
                float s   = Lerp(BIG_SCALE_MIN, BIG_SCALE_MAX, (float)rng.NextDouble());
                float rot = (float)rng.NextDouble() * 360f;
                if (InBridgeZone(x, z)) continue;

                GameObject prefab = bigPrefabs[rng.Next(bigPrefabs.Length)];
                PlaceRock(zoneGo, prefab, new Vector3(x, y, z), s, rot, $"{zone.id}_big_{i:00}");
                total++;
            }

            // Medium rocks
            for (int i = 0; i < zone.med; i++)
            {
                float x   = Lerp(zone.x0, zone.x1, (float)rng.NextDouble());
                float z   = Lerp(zone.z0, zone.z1, (float)rng.NextDouble());
                float y   = Lerp(Y_MIN,   Y_MAX,   (float)rng.NextDouble());
                float s   = Lerp(MEDIUM_SCALE_MIN, MEDIUM_SCALE_MAX, (float)rng.NextDouble());
                float rot = (float)rng.NextDouble() * 360f;
                if (InBridgeZone(x, z)) continue;

                GameObject prefab = mediumPrefabs[rng.Next(mediumPrefabs.Length)];
                PlaceRock(zoneGo, prefab, new Vector3(x, y, z), s, rot, $"{zone.id}_med_{i:00}");
                total++;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[RockScatter] Placed {total} rocks across {Zones.Length} gorge zones.");
    }

    static void PlaceRock(GameObject parent, GameObject prefab, Vector3 pos, float scale, float yRot, string objName)
    {
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
        inst.name = objName;
        inst.transform.position = pos;
        inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
        inst.transform.localScale = Vector3.one * scale;
    }

    static GameObject[] LoadPrefabs(string[] paths, string label)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (string p in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) list.Add(go);
            else Debug.LogWarning($"[RockScatter] {label} prefab not found: {p}");
        }
        if (list.Count == 0) { Debug.LogError($"[RockScatter] No {label} prefabs loaded."); return null; }
        return list.ToArray();
    }

    // Bridge footprints to exclude — safety guard; new far-edge zones don't overlap
    // bridges, but kept in case zones are ever moved back toward the center.
    static readonly (float x0, float x1, float z0, float z1)[] BridgeExclusions =
    {
        (-109f, -82f,  -7f,   7f),   // Bridge_A
        ( -52f, -25f,   5f,  20f),   // Bridge_B
        ( -52f, -25f, -20f,  -5f),   // Bridge_C
        (  25f,  52f,   5f,  20f),   // Bridge_D
        (  25f,  52f, -20f,  -5f),   // Bridge_E
        (  82f, 109f,  -7f,   7f),   // Bridge_F
    };

    static bool InBridgeZone(float x, float z)
    {
        foreach (var b in BridgeExclusions)
            if (x >= b.x0 && x <= b.x1 && z >= b.z0 && z <= b.z1)
                return true;
        return false;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
