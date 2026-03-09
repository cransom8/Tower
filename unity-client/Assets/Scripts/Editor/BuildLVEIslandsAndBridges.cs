using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// RETIRED — LVE lava_wall panels on island/bridge tops have been removed.
/// The primitive stone (island/bridge cubes) is the game board and should remain clean.
/// LVE stone is only used in cliff-side and gorge areas:
///   - Cliff faces → BuildCliffWalls.cs
///   - Gorge scatter → BuildRockScatter.cs
///
/// This menu item is kept as a safety guard: running it logs a warning and exits.
/// Re-running will NOT recreate IslandRocks or BridgeSlabs groups.
/// </summary>
public static class BuildLVEIslandsAndBridges
{
    const string V1 = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Vertex Paint Prefabs/Prefabs/";
    const string V2 = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Vertex Paint Prefabs/Prefabs V2/";

    // All 9 lava_wall panel variants cycled for surface variety
    static readonly string[] WallPrefabs =
    {
        V1 + "lava_wall_01.prefab",
        V2 + "lava_wall_02_V2.prefab",
        V1 + "lava_wall_03.prefab",
        V2 + "lava_wall_04_V2.prefab",
        V1 + "lava_wall_05.prefab",
        V2 + "lava_wall_06_V2.prefab",
        V1 + "lava_wall_07.prefab",
        V2 + "lava_wall_08_V2.prefab",
        V1 + "lava_wall_09.prefab",
    };

    // Island top surface Y — panels sit slightly above so they're never buried
    const float SURFACE_Y = 1.1f;

    // Tile scale — smaller panels tile more tightly for a natural slab mosaic look
    const float ISLAND_TILE_SCALE  = 2.0f;
    const float BRIDGE_TILE_SCALE  = 2.0f;

    // Keep surfaces flush — almost no Y variation so panels form a continuous ground
    const float Y_SINK_MIN = 0.0f;
    const float Y_SINK_MAX = 0.04f;

    // Island data: (name, cx, cz, lenX, lenZ)
    static readonly (string name, float cx, float cz, float lenX, float lenZ)[] Islands =
    {
        ("Island_Dwarf",  -129f,  0f, 40f, 40f),
        ("Island_Split",   -67f,  0f, 30f, 30f),
        ("Island_Center",    0f,  0f, 50f, 45f),
        ("Island_Merge",    67f,  0f, 30f, 30f),
        ("Island_Goblin",  129f,  0f, 40f, 40f),
    };

    // Bridge data: (name, cx, cz, lenX, widZ)
    static readonly (string name, float cx, float cz, float lenX, float widZ)[] Bridges =
    {
        ("Bridge_A", -95.5f,   0f,   27f, 11f),
        ("Bridge_B", -38.5f,  12.5f, 27f, 11f),
        ("Bridge_C", -38.5f, -12.5f, 27f, 11f),
        ("Bridge_D",  38.5f,  12.5f, 27f, 11f),
        ("Bridge_E",  38.5f, -12.5f, 27f, 11f),
        ("Bridge_F",  95.5f,   0f,   27f, 11f),
    };

    [MenuItem("Castle Defender/Map/Build LVE Islands and Bridges (RETIRED)")]
    public static void Build()
    {
        Debug.LogWarning("[LVEIslandsBridges] RETIRED — LVE panels on island/bridge tops are no longer used. " +
                         "Use BuildCliffWalls for cliff faces and BuildRockScatter for gorge scatter instead.");
        return;

        // Dead code kept for reference only — never executes
#pragma warning disable CS0162
        GameObject[] wallPrefabs = LoadPrefabs(WallPrefabs);
        if (wallPrefabs == null) return;

        // Measure natural panel size from first prefab
        (float natW, float natH) = MeasurePrefabSize(wallPrefabs[0]);
        if (natW <= 0f) { natW = 2.65f; natH = 2.65f; }

        float islandTileW = natW * ISLAND_TILE_SCALE;
        float bridgeTileW = natW * BRIDGE_TILE_SCALE;

        Debug.Log($"[LVEIslandsBridges] Panel natural size: {natW:F2}x{natH:F2} | " +
                  $"island tile: {islandTileW:F2} | bridge tile: {bridgeTileW:F2}");

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[LVEIslandsBridges] 'Map' not found."); return; }

        // Rebuild island tops and bridge surface dressing
        DestroyChild(map, "IslandRocks");
        DestroyChild(map, "BridgeSlabs");

        GameObject islandRoot = new GameObject("IslandRocks");
        islandRoot.transform.SetParent(map.transform, false);

        System.Random rng = new System.Random(42);
        int islandTotal = 0;
        int prefabIdx = 0;

        // ── Island Tops ──────────────────────────────────────────────────
        // lava_wall panels rotated flat (Euler -90,yRot,0) and tiled in a grid.
        // This produces the cracked dark volcanic rock slab surface matching the refs.
        foreach (var isl in Islands)
        {
            // Hide the cube mesh — BoxCollider stays for gameplay
            HideMeshRenderer(map, isl.name);

            GameObject islGroup = new GameObject(isl.name + "_Rocks");
            islGroup.transform.SetParent(islandRoot.transform, false);

            int colsX = Mathf.Max(1, Mathf.CeilToInt(isl.lenX / islandTileW));
            int colsZ = Mathf.Max(1, Mathf.CeilToInt(isl.lenZ / islandTileW));
            float stepX = isl.lenX / colsX;
            float stepZ = isl.lenZ / colsZ;

            for (int ix = 0; ix < colsX; ix++)
            {
                for (int iz = 0; iz < colsZ; iz++)
                {
                    float x = isl.cx + (-isl.lenX * 0.5f + stepX * (ix + 0.5f));
                    float z = isl.cz + (-isl.lenZ * 0.5f + stepZ * (iz + 0.5f));

                    // Tiny jitter so panel edges don't perfectly align (natural look)
                    float jx = (float)(rng.NextDouble() - 0.5) * stepX * 0.08f;
                    float jz = (float)(rng.NextDouble() - 0.5) * stepZ * 0.08f;
                    // Slight Y variation — panels sit at slightly different depths
                    float y = SURFACE_Y - Mathf.Lerp(Y_SINK_MIN, Y_SINK_MAX, (float)rng.NextDouble());
                    // Random 90° snapped rotation for natural tiling, not uniform grid
                    float yRot = Mathf.Round((float)rng.NextDouble() * 3f) * 90f;

                    GameObject prefab = wallPrefabs[prefabIdx++ % wallPrefabs.Length];
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, islGroup.transform);
                    inst.name = $"{isl.name}_slab_{ix:00}_{iz:00}";
                    // Rotate -90° on X to lay flat (face up), then yRot for variety
                    inst.transform.position = new Vector3(x + jx, y, z + jz);
                    inst.transform.rotation = Quaternion.Euler(-90f, yRot, 0f);
                    inst.transform.localScale = Vector3.one * ISLAND_TILE_SCALE;
                    islandTotal++;
                }
            }
        }

        // ── Bridge Surface Tops ──────────────────────────────────────────
        // Tile lava_wall panels flat across each bridge top so bridges have the
        // same cracked volcanic rock look as the islands.
        GameObject bridgeSlabRoot = new GameObject("BridgeSlabs");
        bridgeSlabRoot.transform.SetParent(map.transform, false);

        int bridgeTotal = 0;

        foreach (var br in Bridges)
        {
            // Hide the cube mesh — BoxCollider stays for gameplay
            HideMeshRenderer(map, br.name);

            GameObject brGroup = new GameObject(br.name + "_Slabs");
            brGroup.transform.SetParent(bridgeSlabRoot.transform, false);

            int colsX = Mathf.Max(1, Mathf.CeilToInt(br.lenX / (natW * BRIDGE_TILE_SCALE)));
            int colsZ = Mathf.Max(1, Mathf.CeilToInt(br.widZ / (natW * BRIDGE_TILE_SCALE)));
            float stepX = br.lenX / colsX;
            float stepZ = br.widZ / colsZ;

            for (int ix = 0; ix < colsX; ix++)
            {
                for (int iz = 0; iz < colsZ; iz++)
                {
                    float x = br.cx + (-br.lenX * 0.5f + stepX * (ix + 0.5f));
                    float z = br.cz + (-br.widZ * 0.5f + stepZ * (iz + 0.5f));

                    float jx = (float)(rng.NextDouble() - 0.5) * stepX * 0.06f;
                    float jz = (float)(rng.NextDouble() - 0.5) * stepZ * 0.06f;
                    float y  = SURFACE_Y - Mathf.Lerp(Y_SINK_MIN, Y_SINK_MAX, (float)rng.NextDouble());
                    float yRot = Mathf.Round((float)rng.NextDouble() * 3f) * 90f;

                    GameObject prefab = wallPrefabs[prefabIdx++ % wallPrefabs.Length];
                    GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, brGroup.transform);
                    inst.name = $"{br.name}_slab_{ix:00}_{iz:00}";
                    inst.transform.position = new Vector3(x + jx, y, z + jz);
                    inst.transform.rotation = Quaternion.Euler(-90f, yRot, 0f);
                    inst.transform.localScale = Vector3.one * BRIDGE_TILE_SCALE;
                    bridgeTotal++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[LVEIslandsBridges] Done — {islandTotal} island slabs, {bridgeTotal} bridge slabs placed.");
#pragma warning restore CS0162
    }

    static void HideMeshRenderer(GameObject map, string childName)
    {
        Transform t = map.transform.Find(childName);
        if (t == null) return;
        var mr = t.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            // Destroy the renderer so the cube is 100% invisible — BoxCollider stays
            Undo.DestroyObjectImmediate(mr);
        }
    }

    static void DestroyChild(GameObject parent, string childName)
    {
        Transform t = parent.transform.Find(childName);
        if (t != null) GameObject.DestroyImmediate(t.gameObject);
    }

    static GameObject[] LoadPrefabs(string[] paths)
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (string p in paths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (go != null) list.Add(go);
            else Debug.LogWarning($"[LVEIslandsBridges] Prefab not found: {p}");
        }
        if (list.Count == 0) { Debug.LogError("[LVEIslandsBridges] No wall prefabs loaded."); return null; }
        return list.ToArray();
    }

    static (float w, float h) MeasurePrefabSize(GameObject prefab)
    {
        GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        temp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        temp.transform.localScale = Vector3.one;
        Bounds b = new Bounds(Vector3.zero, Vector3.zero);
        bool found = false;
        foreach (Renderer r in temp.GetComponentsInChildren<Renderer>())
        {
            if (!found) { b = r.bounds; found = true; }
            else b.Encapsulate(r.bounds);
        }
        GameObject.DestroyImmediate(temp);
        return found ? (b.size.x, b.size.y) : (2.65f, 2.65f);
    }
}
