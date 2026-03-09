using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Places lava-fall stream quads at a few spots on the exterior edge walls.
/// Each fall = a tall narrow Quad with the animated lava flow material, flush
/// against the interior face of the MapEdgeWalls, spanning from wall top down
/// to the lava floor.
///
/// A dedicated LavaFall material is created (or reused) with tiling set to
/// make the texture stream look narrow and tall rather than wide and flat.
///
/// Re-runnable: destroys existing LavaFalls group before rebuilding.
/// Menu: Castle Defender/Map/Build Lava Falls
/// </summary>
public static class BuildLavaFalls
{
    const string LAVA_MAT_PATH = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Lava Materials/Lava_3_flow.mat";
    const string FALL_MAT_PATH = "Assets/Materials/LavaFall.mat";

    // Fall quad dimensions
    const float FALL_WIDTH  =  6f;    // stream width in world units
    const float WALL_TOP    = 20f;    // matches BuildMapEdgeWalls.WALL_TOP
    const float LAVA_Y      = -15f;   // lava floor Y
    const float FALL_HEIGHT = WALL_TOP - LAVA_Y;   // 35 units

    // How far inside the wall to place the quad (avoids z-fighting)
    const float INSET = 0.3f;

    // Lava floor extents (matches SetupMapPrimitives)
    const float LAVA_HALF_X = 200f;
    const float LAVA_HALF_Z =  75f;

    // Fall positions: (x, z) spots on each wall.
    // North / South walls: vary X,  Z is fixed at ±LAVA_HALF_Z
    // East  / West  walls: vary Z,  X is fixed at ±LAVA_HALF_X
    static readonly float[] NorthFallsX = { -120f,  0f,  110f };
    static readonly float[] SouthFallsX = {  -90f, 50f         };
    static readonly float[] WestFallsZ  = {  -30f, 25f         };
    static readonly float[] EastFallsZ  = {   30f,-20f         };

    [MenuItem("Castle Defender/Map/Build Lava Falls")]
    public static void Build()
    {
        // ── Ensure LavaFall material exists ─────────────────────────────
        Material fallMat = AssetDatabase.LoadAssetAtPath<Material>(FALL_MAT_PATH);
        if (fallMat == null)
        {
            Material srcMat = AssetDatabase.LoadAssetAtPath<Material>(LAVA_MAT_PATH);
            if (srcMat == null)
            {
                Debug.LogError("[LavaFalls] Lava_3_flow.mat not found at: " + LAVA_MAT_PATH);
                return;
            }
            // Duplicate the lava material so we can set a narrow tiling
            // without changing the floor material
            AssetDatabase.CopyAsset(LAVA_MAT_PATH, FALL_MAT_PATH);
            AssetDatabase.Refresh();
            fallMat = AssetDatabase.LoadAssetAtPath<Material>(FALL_MAT_PATH);
            if (fallMat == null)
            {
                Debug.LogError("[LavaFalls] Could not create LavaFall.mat");
                return;
            }
            // Narrow tiling + downward flow speed for stream look
            fallMat.SetFloat("_GlobalTiling", 0.4f);
            fallMat.SetVector("_ColdLavaTiling",   new Vector4(0.4f, 1.5f, 0f, 0f));
            fallMat.SetVector("_MediumLavaTiling",  new Vector4(0.4f, 1.5f, 0f, 0f));
            fallMat.SetVector("_HotLavaTiling",     new Vector4(0.4f, 1.5f, 0f, 0f));
            fallMat.SetVector("_ColdLavaMainSpeed",   new Vector4(0f, 1.5f, 0f, 0f));
            fallMat.SetVector("_MediumLavaMainSpeed", new Vector4(0f, 1.2f, 0f, 0f));
            fallMat.SetVector("_HotLavaMainSpeed",    new Vector4(0f, 1.2f, 0f, 0f));
            EditorUtility.SetDirty(fallMat);
            AssetDatabase.SaveAssets();
            Debug.Log("[LavaFalls] Created LavaFall.mat");
        }

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[LavaFalls] 'Map' not found."); return; }

        // Destroy previous run
        Transform existing = map.transform.Find("LavaFalls");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        GameObject root = new GameObject("LavaFalls");
        root.transform.SetParent(map.transform, false);

        float centerY = LAVA_Y + FALL_HEIGHT * 0.5f;   // vertical centre of the fall quad
        int total = 0;

        // ── North wall  (Z = +LAVA_HALF_Z, inset = -INSET, face -Z = yRot 180) ──
        foreach (float x in NorthFallsX)
        {
            PlaceFall(root, fallMat, new Vector3(x, centerY, LAVA_HALF_Z - INSET),
                      Quaternion.Euler(0f, 180f, 0f),
                      $"Fall_North_{(int)x:+0;-0;0}");
            total++;
        }

        // ── South wall  (Z = -LAVA_HALF_Z, inset = +INSET, face +Z = yRot 0) ───
        foreach (float x in SouthFallsX)
        {
            PlaceFall(root, fallMat, new Vector3(x, centerY, -LAVA_HALF_Z + INSET),
                      Quaternion.Euler(0f, 0f, 0f),
                      $"Fall_South_{(int)x:+0;-0;0}");
            total++;
        }

        // ── West wall   (X = -LAVA_HALF_X, inset = +INSET, face +X = yRot 90) ──
        foreach (float z in WestFallsZ)
        {
            PlaceFall(root, fallMat, new Vector3(-LAVA_HALF_X + INSET, centerY, z),
                      Quaternion.Euler(0f, 90f, 0f),
                      $"Fall_West_{(int)z:+0;-0;0}");
            total++;
        }

        // ── East wall   (X = +LAVA_HALF_X, inset = -INSET, face -X = yRot 270) ─
        foreach (float z in EastFallsZ)
        {
            PlaceFall(root, fallMat, new Vector3(LAVA_HALF_X - INSET, centerY, z),
                      Quaternion.Euler(0f, 270f, 0f),
                      $"Fall_East_{(int)z:+0;-0;0}");
            total++;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[LavaFalls] Placed {total} lava fall streams.");
    }

    static void PlaceFall(GameObject parent, Material mat, Vector3 pos, Quaternion rot, string objName)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = objName;
        quad.transform.SetParent(parent.transform, false);
        quad.transform.position = pos;
        quad.transform.rotation = rot;
        quad.transform.localScale = new Vector3(FALL_WIDTH, FALL_HEIGHT, 1f);

        // Remove collider — purely visual
        Object.DestroyImmediate(quad.GetComponent<MeshCollider>());

        // Apply lava fall material
        quad.GetComponent<Renderer>().sharedMaterial = mat;
    }
}
