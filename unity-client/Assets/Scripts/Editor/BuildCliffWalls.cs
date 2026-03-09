using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 6 — Places LVE lava_wall prefabs around exposed island cliff faces.
/// Menu: Castle Defender → Build Cliff Walls
/// Re-runnable: destroys existing Cliffs children before rebuilding.
///
/// Tiling: 2D grid (columns along face × rows from lava surface to island top).
/// Prefabs are kept near natural scale (TARGET_SCALE multiplier) so detail is preserved.
/// </summary>
public class BuildCliffWalls
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

    // Scale multiplier applied to natural prefab size.
    // Keep this small so the mesh detail is visible; we tile to fill coverage.
    const float TARGET_SCALE = 2f;

    // Visible cliff: lava surface up to just BELOW the island top surface.
    // Stopping 4 units below game board (Y=1) ensures cliff panels never
    // protrude at or above the playing surface.
    const float LAVA_Y    = -15f;   // lava floor Y
    const float ISLAND_TOP = -4f;   // cliff top — panels center here, top edge ≈ Y -1.35

    // Island layout: (name, cx, cy, cz, sx, sy, sz, skipLeft, skipRight, skipFront, skipBack)
    static readonly (string n, float cx, float cy, float cz,
                     float sx, float sy, float sz,
                     bool sL, bool sR, bool sF, bool sB)[] Islands =
    {
        ("Island_Dwarf",  -129f, -9f, 0f, 40f,20f,40f,  false, true,  false, false),
        ("Island_Split",   -67f, -9f, 0f, 30f,20f,30f,  true,  false, false, false),
        ("Island_Center",    0f, -9f, 0f, 50f,20f,45f,  true,  true,  false, false),
        ("Island_Merge",    67f, -9f, 0f, 30f,20f,30f,  false, true,  false, false),
        ("Island_Goblin",  129f, -9f, 0f, 40f,20f,40f,  true,  false, false, false),
    };

    [MenuItem("Castle Defender/Build Cliff Walls")]
    public static void Build()
    {
        GameObject[] prefabs = LoadPrefabs();
        if (prefabs == null || prefabs.Length == 0) return;

        // Measure natural tile footprint from first prefab
        (float natW, float natH) = MeasurePrefabSize(prefabs[0]);
        if (natW <= 0f) { natW = 2.65f; natH = 2.65f; }

        float tileScale = TARGET_SCALE;
        float tileW = natW * tileScale;   // world-space tile width  (along face)
        float tileH = natH * tileScale;   // world-space tile height (vertical)

        // Vertical coverage: lava surface up to island top
        float cliffBottom = LAVA_Y;
        float cliffTop    = ISLAND_TOP;
        float cliffHeight = cliffTop - cliffBottom;   // e.g. 32 units

        Debug.Log($"[CliffWalls] natSize={natW:F2}x{natH:F2}  tileScale={tileScale}  " +
                  $"tileSize={tileW:F2}x{tileH:F2}  cliffH={cliffHeight:F1}");

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[CliffWalls] 'Map' not found in scene."); return; }

        // Remove existing CliffWalls root so re-run is safe
        Transform existingRoot = map.transform.Find("CliffWalls");
        if (existingRoot != null) GameObject.DestroyImmediate(existingRoot.gameObject);

        // Also clean up old-style Cliffs groups parented directly to each island
        foreach (var isl in Islands)
        {
            Transform islandTf = map.transform.Find(isl.n);
            if (islandTf == null) continue;
            Transform oldCliffs = islandTf.Find("Cliffs");
            if (oldCliffs != null) GameObject.DestroyImmediate(oldCliffs.gameObject);
        }

        // Parent to Map directly — avoids inheriting each island's non-uniform scale
        GameObject cliffWallsRoot = new GameObject("CliffWalls");
        cliffWallsRoot.transform.SetParent(map.transform, false);

        int prefabIdx = 0;
        int totalPlaced = 0;

        foreach (var isl in Islands)
        {
            var cliffsRoot = new GameObject(isl.n + "_Cliffs");
            cliffsRoot.transform.SetParent(cliffWallsRoot.transform, false);

            float hw = isl.sx * 0.5f;
            float hd = isl.sz * 0.5f;

            // LEFT face: tiles spread along Z, face outward (-X), yRot=270
            if (!isl.sL)
                totalPlaced += PlaceFace(cliffsRoot, prefabs, ref prefabIdx,
                    tileScale, tileW, tileH, cliffBottom, cliffHeight,
                    new Vector3(isl.cx - hw, 0f, isl.cz), isl.sz,
                    false, 270f, $"{isl.n}_L");

            // RIGHT face: tiles spread along Z, face outward (+X), yRot=90
            if (!isl.sR)
                totalPlaced += PlaceFace(cliffsRoot, prefabs, ref prefabIdx,
                    tileScale, tileW, tileH, cliffBottom, cliffHeight,
                    new Vector3(isl.cx + hw, 0f, isl.cz), isl.sz,
                    false, 90f, $"{isl.n}_R");

            // FRONT face: tiles spread along X, face outward (+Z), yRot=0
            if (!isl.sF)
                totalPlaced += PlaceFace(cliffsRoot, prefabs, ref prefabIdx,
                    tileScale, tileW, tileH, cliffBottom, cliffHeight,
                    new Vector3(isl.cx, 0f, isl.cz + hd), isl.sx,
                    true, 0f, $"{isl.n}_F");

            // BACK face: tiles spread along X, face outward (-Z), yRot=180
            if (!isl.sB)
                totalPlaced += PlaceFace(cliffsRoot, prefabs, ref prefabIdx,
                    tileScale, tileW, tileH, cliffBottom, cliffHeight,
                    new Vector3(isl.cx, 0f, isl.cz - hd), isl.sx,
                    true, 180f, $"{isl.n}_B");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[CliffWalls] Built {totalPlaced} cliff wall tiles across all islands.");
    }

    // Places a 2D grid of tiles: columns along the face × rows vertically.
    // faceAnchorXZ: world position at the face edge, Y ignored (we set Y per row)
    // faceLength: width of the face in world units
    // spreadAlongX: true → columns offset in X; false → columns offset in Z
    static int PlaceFace(GameObject parent, GameObject[] prefabs, ref int idx,
        float scale, float tileW, float tileH,
        float cliffBottom, float cliffHeight,
        Vector3 faceAnchorXZ, float faceLength,
        bool spreadAlongX, float yRot, string faceId)
    {
        int cols = Mathf.Max(1, Mathf.RoundToInt(faceLength / tileW));
        int rows = Mathf.Max(1, Mathf.RoundToInt(cliffHeight / tileH));

        float colStep = faceLength / cols;
        float rowStep = cliffHeight / rows;

        int placed = 0;
        for (int row = 0; row < rows; row++)
        {
            float y = cliffBottom + rowStep * (row + 0.5f);  // centre of each row band

            for (int col = 0; col < cols; col++)
            {
                float along = -faceLength * 0.5f + colStep * (col + 0.5f);
                Vector3 pos = faceAnchorXZ + new Vector3(
                    spreadAlongX ? along : 0f,
                    y,
                    spreadAlongX ? 0f : along);

                GameObject prefab = prefabs[idx % prefabs.Length];
                idx++;

                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.transform);
                inst.name = $"{faceId}_r{row:00}c{col:00}";
                inst.transform.position = pos;
                inst.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
                inst.transform.localScale = Vector3.one * scale;
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
            else Debug.LogWarning($"[CliffWalls] Prefab not found: {path}");
        }
        if (list.Count == 0) { Debug.LogError("[CliffWalls] No cliff wall prefabs loaded."); return null; }
        return list.ToArray();
    }

    // Instantiate prefab temporarily at origin to measure its world-space bounds.
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
