using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Restores island/bridge cubes as visible primitives with LVE rock materials,
/// removes lava_wall panel overlays (IslandRocks / BridgeSlabs), and adds a
/// full-coverage animated lava floor under the map.
///
/// Menu: Castle Defender/Map/Setup Map Primitives + Lava
/// </summary>
public static class SetupMapPrimitives
{
    // Materials
    const string ISLAND_MAT = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Ground and River Textures/Ground Material/Ground_01.mat";
    const string BRIDGE_MAT = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Materials/lava_rocks_01_BC.mat";
    const string LAVA_MAT   = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Lava Materials/Lava_2_flow Lake.mat";

    // Lava floor sits at LAVA_Y, large enough to cover the full map + margins
    const float LAVA_Y      = -15f;
    const float LAVA_WIDTH  = 400f;   // X extent  (map spans ±149)
    const float LAVA_DEPTH  = 150f;   // Z extent  (map spans ±43)

    [MenuItem("Castle Defender/Map/Setup Map Primitives + Lava")]
    public static void Run()
    {
        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[SetupMapPrimitives] 'Map' not found."); return; }

        // ── 1. Load materials ───────────────────────────────────────────
        var islandMat = AssetDatabase.LoadAssetAtPath<Material>(ISLAND_MAT);
        var bridgeMat = AssetDatabase.LoadAssetAtPath<Material>(BRIDGE_MAT);
        var lavaMat   = AssetDatabase.LoadAssetAtPath<Material>(LAVA_MAT);

        if (islandMat == null) Debug.LogWarning("[SetupMapPrimitives] Island mat not found: " + ISLAND_MAT);
        if (bridgeMat == null) Debug.LogWarning("[SetupMapPrimitives] Bridge mat not found: " + BRIDGE_MAT);
        if (lavaMat   == null) Debug.LogWarning("[SetupMapPrimitives] Lava mat not found: "   + LAVA_MAT);

        // ── 2. Remove lava_wall panel overlay groups ────────────────────
        foreach (string grp in new[] { "IslandRocks", "BridgeSlabs" })
        {
            Transform t = map.transform.Find(grp);
            if (t != null) { GameObject.DestroyImmediate(t.gameObject); Debug.Log("[SetupMapPrimitives] Removed " + grp); }
        }

        // ── 3. Restore MeshRenderers on all Island_* and Bridge_* ───────
        int restored = 0;
        foreach (Transform child in map.transform)
        {
            string n = child.name;
            bool isIsland = n.StartsWith("Island_");
            bool isBridge = n.StartsWith("Bridge_");
            if (!isIsland && !isBridge) continue;

            // Ensure MeshFilter has the default cube mesh
            var mf = child.GetComponent<MeshFilter>();
            if (mf == null) mf = child.gameObject.AddComponent<MeshFilter>();
            if (mf.sharedMesh == null)
            {
                // Assign the Unity built-in cube mesh
                GameObject tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mf.sharedMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
                GameObject.DestroyImmediate(tempCube);
            }

            // Add or fetch MeshRenderer
            var mr = child.GetComponent<MeshRenderer>();
            if (mr == null) mr = child.gameObject.AddComponent<MeshRenderer>();

            // Apply material
            if (isIsland && islandMat != null) mr.sharedMaterial = islandMat;
            else if (isBridge && bridgeMat != null) mr.sharedMaterial = bridgeMat;

            restored++;
        }
        Debug.Log($"[SetupMapPrimitives] Restored MeshRenderer on {restored} island/bridge cubes.");

        // ── 4. Add / replace lava floor ─────────────────────────────────
        Transform existingLava = map.transform.Find("LavaFloor");
        if (existingLava != null) GameObject.DestroyImmediate(existingLava.gameObject);

        GameObject lavaGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        lavaGo.name = "LavaFloor";
        lavaGo.transform.SetParent(map.transform, false);
        lavaGo.transform.position  = new Vector3(0f, LAVA_Y, 0f);
        lavaGo.transform.rotation  = Quaternion.Euler(90f, 0f, 0f);
        lavaGo.transform.localScale = new Vector3(LAVA_WIDTH, LAVA_DEPTH, 1f);
        GameObject.DestroyImmediate(lavaGo.GetComponent<MeshCollider>());
        if (lavaMat != null) lavaGo.GetComponent<Renderer>().sharedMaterial = lavaMat;
        else
        {
            // Fallback — glowing orange URP material
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var fallback = new Material(shader);
            fallback.SetColor("_BaseColor",     new Color(0.9f, 0.25f, 0.05f));
            fallback.SetColor("_EmissionColor", new Color(1.2f, 0.3f,  0f));
            fallback.EnableKeyword("_EMISSION");
            lavaGo.GetComponent<Renderer>().sharedMaterial = fallback;
            Debug.LogWarning("[SetupMapPrimitives] LVE lava mat missing — fallback orange used.");
        }
        Debug.Log($"[SetupMapPrimitives] Lava floor created at Y={LAVA_Y}, size={LAVA_WIDTH}x{LAVA_DEPTH}.");

        // ── 5. Fix fog scale for the large map ──────────────────────────
        // Map is ~310 units wide; fog needs to start well past the near edge.
        RenderSettings.fog              = true;
        RenderSettings.fogMode          = FogMode.Linear;
        RenderSettings.fogStartDistance = 80f;
        RenderSettings.fogEndDistance   = 350f;
        RenderSettings.fogColor         = new Color(0.10f, 0.04f, 0.02f);
        Debug.Log("[SetupMapPrimitives] Fog adjusted for map scale (80 → 350).");

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[SetupMapPrimitives] Done.");
    }
}
