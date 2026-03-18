using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Places LVE lava_wall prefabs upright around the outer perimeter of the lava floor,
/// creating volcanic canyon walls that frame the map on all four sides.
///
/// Lava floor: X ±200, Z ±75, Y = -15 (matches SetupMapPrimitives constants).
/// Walls span from WALL_BOTTOM to WALL_TOP, tiled in a grid along each face.
///
/// Re-runnable: destroys existing MapEdgeWalls group before rebuilding.
/// Menu: Castle Defender/Map/Build Map Edge Walls
/// </summary>
public static class BuildMapEdgeWalls
{
    const string V1 = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Vertex Paint Prefabs/Prefabs/";
    const string V2 = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Vertex Paint Prefabs/Prefabs V2/";

    static readonly string[] PrefabPaths =
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

    // Lava floor outer edges (must match SetupMapPrimitives)
    const float LAVA_HALF_X = 200f;   // lava spans X -200..+200
    const float LAVA_HALF_Z =  75f;   // lava spans Z -75..+75

    // Wall vertical range — start below lava floor, rise well above it
    const float WALL_BOTTOM = -25f;
    const float WALL_TOP    =  20f;

    // Tile scale — larger tiles = fewer prefabs, better performance
    const float TILE_SCALE  = 4f;

    [MenuItem("Castle Defender/Map/Build Map Edge Walls")]
    public static void Build()
    {
        GameObject[] prefabs = LoadPrefabs();
        if (prefabs == null || prefabs.Length == 0) return;

        // Measure natural tile size from first prefab
        (float natW, float natH) = MeasurePrefabSize(prefabs[0]);
        if (natW <= 0f) { natW = 2.65f; natH = 2.65f; }

        float tileW = natW * TILE_SCALE;
        float tileH = natH * TILE_SCALE;
        float wallH = WALL_TOP - WALL_BOTTOM;

        Debug.Log($"[MapEdgeWalls] Tile size: {tileW:F2}w x {tileH:F2}h  " +
                  $"Wall height: {wallH:F1}  " +
                  $"North/South cols: {Mathf.CeilToInt(LAVA_HALF_X * 2 / tileW)}  " +
                  $"East/West cols: {Mathf.CeilToInt(LAVA_HALF_Z * 2 / tileW)}");

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[MapEdgeWalls] 'Map' not found."); return; }

        // Destroy previous run
        Transform existing = map.transform.Find("MapEdgeWalls");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        GameObject root = new GameObject("MapEdgeWalls");
        root.transform.SetParent(map.transform, false);

        int idx = 0;
        int total = 0;

        // ── NORTH wall (Z = +LAVA_HALF_Z, faces inward = -Z, yRot 180) ──────
        total += PlaceWall(root, prefabs, ref idx, tileW, tileH, wallH,
            centerX: 0f, faceZ: LAVA_HALF_Z,
            spanX: LAVA_HALF_X * 2f,
            spreadAlongX: true, yRot: 180f, label: "North");

        // ── SOUTH wall (Z = -LAVA_HALF_Z, faces inward = +Z, yRot 0) ────────
        total += PlaceWall(root, prefabs, ref idx, tileW, tileH, wallH,
            centerX: 0f, faceZ: -LAVA_HALF_Z,
            spanX: LAVA_HALF_X * 2f,
            spreadAlongX: true, yRot: 0f, label: "South");

        // ── WEST wall (X = -LAVA_HALF_X, faces inward = +X, yRot 90) ────────
        total += PlaceWall(root, prefabs, ref idx, tileW, tileH, wallH,
            centerX: 0f, faceZ: -LAVA_HALF_X,
            spanX: LAVA_HALF_Z * 2f,
            spreadAlongX: false, yRot: 90f, label: "West");

        // ── EAST wall (X = +LAVA_HALF_X, faces inward = -X, yRot 270) ───────
        total += PlaceWall(root, prefabs, ref idx, tileW, tileH, wallH,
            centerX: 0f, faceZ: LAVA_HALF_X,
            spanX: LAVA_HALF_Z * 2f,
            spreadAlongX: false, yRot: 270f, label: "East");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[MapEdgeWalls] Done — {total} wall tiles placed.");
    }

    /// <summary>
    /// Places a 2D grid of tiles (columns × rows) along one map edge.
    /// spreadAlongX=true  → columns spread in X (North/South walls)
    /// spreadAlongX=false → columns spread in Z (East/West walls), faceZ is then used as faceX
    /// </summary>
    static int PlaceWall(GameObject root, GameObject[] prefabs, ref int idx,
        float tileW, float tileH, float wallH,
        float centerX, float faceZ, float spanX,
        bool spreadAlongX, float yRot, string label)
    {
        GameObject wallGo = new GameObject("Wall_" + label);
        wallGo.transform.SetParent(root.transform, false);

        int cols = Mathf.Max(1, Mathf.CeilToInt(spanX / tileW));
        int rows = Mathf.Max(1, Mathf.CeilToInt(wallH  / tileH));

        float colStep = spanX / cols;
        float rowStep = wallH  / rows;

        int placed = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = WALL_BOTTOM + rowStep * (row + 0.5f);

            for (int col = 0; col < cols; col++)
            {
                float along = -spanX * 0.5f + colStep * (col + 0.5f);

                Vector3 pos;
                if (spreadAlongX)
                    pos = new Vector3(centerX + along, y, faceZ);
                else
                    pos = new Vector3(faceZ, y, centerX + along);

                GameObject prefab = prefabs[idx % prefabs.Length];
                idx++;

                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, wallGo.transform);
                inst.name = $"Wall_{label}_r{row:00}c{col:00}";
                inst.transform.position = pos;
                inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
                inst.transform.localScale = Vector3.one * TILE_SCALE;
                placed++;
            }
        }
        return placed;
    }

    static GameObject[] LoadPrefabs()
    {
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (string path in PrefabPaths)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) list.Add(go);
            else Debug.LogWarning($"[MapEdgeWalls] Prefab not found: {path}");
        }
        if (list.Count == 0) { Debug.LogError("[MapEdgeWalls] No wall prefabs loaded."); return null; }
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
