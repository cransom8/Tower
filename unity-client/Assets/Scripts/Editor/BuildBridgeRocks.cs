using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Each bridge = 1 long slab rock spanning the chasm + 1 anchor rock at each island end.
/// Re-runnable: destroys NaturalBridges before rebuilding.
/// Menu: Castle Defender > Map > Build Bridge Rocks
/// </summary>
public static class BuildBridgeRocks
{
    const string VP = "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Rocks/Vertex Paint Prefabs/";

    // Long slab rock — stretched across the bridge
    const string SlabPrefab   = VP + "prefab_A_small_lava_rock_01.prefab";
    // Anchor rocks at island connection points
    const string AnchorPrefab = VP + "prefab_A_big_lava_rock_01.prefab";

    // Natural rock size at scale 1 ≈ 1.125 units
    // Slab scaled to span bridge length (27) and width (11)
    const float SLAB_SCALE_X = 22f;   // slightly less than 27 — islands overlap the ends
    const float SLAB_SCALE_Y = 2f;
    const float SLAB_SCALE_Z = 9f;    // slightly less than 11

    // Anchor rocks — smaller boulders, kept deep in the gorge below the game board
    const float ANCHOR_SCALE = 2f;

    const float SURFACE_Y = -14f;  // deep in gorge (island top = Y 1, lava floor = Y -15)

    // Bridge data: (name, cx, cz, lenX, widZ)
    // Left anchor at (cx - lenX/2, ...) right anchor at (cx + lenX/2, ...)
    static readonly (string name, float cx, float cz, float lenX, float widZ)[] Bridges =
    {
        ("Bridge_A", -95.5f,   0f,   27f, 11f),
        ("Bridge_B", -38.5f,  12.5f, 27f, 11f),
        ("Bridge_C", -38.5f, -12.5f, 27f, 11f),
        ("Bridge_D",  38.5f,  12.5f, 27f, 11f),
        ("Bridge_E",  38.5f, -12.5f, 27f, 11f),
        ("Bridge_F",  95.5f,   0f,   27f, 11f),
    };

    [MenuItem("Castle Defender/Map/Build Bridge Rocks")]
    public static void Build()
    {
        GameObject slabPrefab   = AssetDatabase.LoadAssetAtPath<GameObject>(SlabPrefab);
        GameObject anchorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AnchorPrefab);
        if (slabPrefab == null)   { Debug.LogError($"[BridgeRocks] Slab prefab not found: {SlabPrefab}"); return; }
        if (anchorPrefab == null) { Debug.LogError($"[BridgeRocks] Anchor prefab not found: {AnchorPrefab}"); return; }

        GameObject map = GameObject.Find("Map");
        if (map == null) { Debug.LogError("[BridgeRocks] 'Map' not found."); return; }

        Transform existing = map.transform.Find("NaturalBridges");
        if (existing != null) GameObject.DestroyImmediate(existing.gameObject);

        GameObject bridgeRoot = new GameObject("NaturalBridges");
        bridgeRoot.transform.SetParent(map.transform, false);

        foreach (var br in Bridges)
        {
            GameObject brGroup = new GameObject(br.name);
            brGroup.transform.SetParent(bridgeRoot.transform, false);

            // ── Long slab spanning the chasm ──────────────────────────────
            GameObject slab = (GameObject)PrefabUtility.InstantiatePrefab(slabPrefab, brGroup.transform);
            slab.name = $"{br.name}_Slab";
            slab.transform.position = new Vector3(br.cx, SURFACE_Y, br.cz);
            slab.transform.rotation = Quaternion.identity;
            slab.transform.localScale = new Vector3(SLAB_SCALE_X, SLAB_SCALE_Y, SLAB_SCALE_Z);

            // ── Anchor rock at left island edge ───────────────────────────
            float leftX  = br.cx - br.lenX * 0.5f;
            float rightX = br.cx + br.lenX * 0.5f;

            GameObject anchorL = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab, brGroup.transform);
            anchorL.name = $"{br.name}_AnchorL";
            anchorL.transform.position = new Vector3(leftX, SURFACE_Y, br.cz);
            anchorL.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            anchorL.transform.localScale = Vector3.one * ANCHOR_SCALE;

            // ── Anchor rock at right island edge ──────────────────────────
            GameObject anchorR = (GameObject)PrefabUtility.InstantiatePrefab(anchorPrefab, brGroup.transform);
            anchorR.name = $"{br.name}_AnchorR";
            anchorR.transform.position = new Vector3(rightX, SURFACE_Y, br.cz);
            anchorR.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
            anchorR.transform.localScale = Vector3.one * ANCHOR_SCALE;
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[BridgeRocks] Built {Bridges.Length} bridges (1 slab + 2 anchors each).");
    }
}
