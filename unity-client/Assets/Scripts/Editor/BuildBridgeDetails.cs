using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds natural volcanic-rock bridge sides from LVE lava_wall prefabs.
/// Each bridge gets a cliff-wall row along both the front (+Z) and back (-Z) faces,
/// tiled along the bridge length, giving a natural rocky ledge-over-lava look.
/// The bridge cube remains as the flat walkable collision surface.
///
/// Menu: Castle Defender → Build Bridge Details
/// Re-runnable: destroys BridgeDetails before rebuilding.
/// </summary>
public class BuildBridgeDetails
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
    };

    // Small scale — keep rock detail visible; tiles along each bridge face
    const float TILE_SCALE   = 1f;
    const float LAVA_Y       = -15f;  // lava surface Y
    const float BRIDGE_TOP_Y =  1f;   // top of bridge walking surface

    // Halved bridge data: (name, centreX, centreZ, lengthX, widthZ)
    static readonly (string name, float cx, float cz, float lenX, float widZ)[] Bridges =
    {
        ("Bridge_A", -95.5f,   0f,  27f, 11f),
        ("Bridge_B", -38.5f,  12.5f, 27f, 11f),
        ("Bridge_C", -38.5f, -12.5f, 27f, 11f),
        ("Bridge_D",  38.5f,  12.5f, 27f, 11f),
        ("Bridge_E",  38.5f, -12.5f, 27f, 11f),
        ("Bridge_F",  95.5f,   0f,  27f, 11f),
    };

    [MenuItem("Castle Defender/Build Bridge Details")]
    public static void Build()
    {
        GameObject[] prefabs = LoadPrefabs();
        if (prefabs == null || prefabs.Length == 0) return;

        // Measure natural tile size from first prefab
        (float natW, float natH) = MeasurePrefabSize(prefabs[0]);
        if (natW <= 0f) { natW = 2.65f; natH = 2.65f; }

        float tileW = natW * TILE_SCALE;
        float tileH = natH * TILE_SCALE;
        float cliffHeight = BRIDGE_TOP_Y - LAVA_Y;   // e.g. 16 units

        Debug.Log($"[BridgeDetails] tileSize={tileW:F2}x{tileH:F2}  cliffH={cliffHeight:F1}");

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[BridgeDetails] 'Map' not found."); return; }

        Transform existing = map.transform.Find("BridgeDetails");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        GameObject detailsRoot = new GameObject("BridgeDetails");
        detailsRoot.transform.SetParent(map.transform, false);

        int prefabIdx = 0;
        int total = 0;

        foreach (var b in Bridges)
        {
            GameObject bParent = new GameObject(b.name + "_Details");
            bParent.transform.SetParent(detailsRoot.transform, false);

            float sideOffset = b.widZ * 0.5f;

            // Front face (+Z side of bridge), tiles along X, face outward (+Z), yRot=0
            total += PlaceFace(bParent, prefabs, ref prefabIdx,
                TILE_SCALE, tileW, tileH, LAVA_Y, cliffHeight,
                new Vector3(b.cx, 0f, b.cz + sideOffset), b.lenX,
                true, 0f, $"{b.name}_Front");

            // Back face (-Z side of bridge), tiles along X, face outward (-Z), yRot=180
            total += PlaceFace(bParent, prefabs, ref prefabIdx,
                TILE_SCALE, tileW, tileH, LAVA_Y, cliffHeight,
                new Vector3(b.cx, 0f, b.cz - sideOffset), b.lenX,
                true, 180f, $"{b.name}_Back");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[BridgeDetails] Built {total} rock-wall tiles across all bridges.");
    }

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
            float y = cliffBottom + rowStep * (row + 0.5f);
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
            else Debug.LogWarning($"[BridgeDetails] Prefab not found: {path}");
        }
        if (list.Count == 0) { Debug.LogError("[BridgeDetails] No cliff prefabs loaded."); return null; }
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
