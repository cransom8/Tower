using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Correctly halves every object in the Map to its proper scale,
/// then replaces flat-stone surfaces on islands and bridges with
/// LVE volcanic-rock materials.
///
/// Rules:
///   - ALL local positions under Map are halved (containers at origin stay at origin)
///   - Local scale is halved ONLY when it is not Vector3.one (containers keep scale 1,1,1)
///   - Island_* objects get the LVE Ground_01 rocky-ground material
///   - Bridge_* objects get the LVE lava_rocks_01_BC cliff-rock material
///   - Camera is moved to match the halved map size
///
/// Menu: Castle Defender > Map > Rescale Map and Apply Rock Materials
/// </summary>
public static class RescaleMapAndApplyRockMaterials
{
    private const string IslandMatPath =
        "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Ground and River Textures/Ground Material/Ground_01.mat";

    private const string BridgeMatPath =
        "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Materials/lava_rocks_01_BC.mat";

    private static readonly Vector3 NewCameraPos = new Vector3(0f, 100f, -125f);
    private static readonly Vector3 NewCameraRot = new Vector3(40f, 0f, 0f);

    [MenuItem("Castle Defender/Map/Rescale Map and Apply Rock Materials")]
    public static void Run()
    {
        var mapGO = GameObject.Find("Map");
        if (mapGO == null)
        {
            Debug.LogError("[RescaleMap] 'Map' GameObject not found in scene.");
            return;
        }

        var islandMat = AssetDatabase.LoadAssetAtPath<Material>(IslandMatPath);
        var bridgeMat = AssetDatabase.LoadAssetAtPath<Material>(BridgeMatPath);

        if (islandMat == null)
            Debug.LogWarning("[RescaleMap] Island material not found at: " + IslandMatPath);
        if (bridgeMat == null)
            Debug.LogWarning("[RescaleMap] Bridge material not found at: " + BridgeMatPath);

        Undo.RegisterFullObjectHierarchyUndo(mapGO, "Rescale Map and Apply Rock Materials");

        int posHalved = 0, scaleHalved = 0, matsApplied = 0;

        // ── 1. Recursively rescale every child of Map ─────────────────────
        // Halve ALL local positions (containers at 0,0,0 stay at 0,0,0).
        // Halve scale ONLY when it differs from Vector3.one so containers
        // (CliffWalls, RockScatter, BridgeDetails, LavaParticles and their
        // sub-groups) remain at scale 1,1,1 and don't double-apply to children.
        foreach (Transform t in mapGO.GetComponentsInChildren<Transform>(true))
        {
            if (t == mapGO.transform) continue;

            t.localPosition = t.localPosition * 0.5f;
            posHalved++;

            if (t.localScale != Vector3.one)
            {
                t.localScale = t.localScale * 0.5f;
                scaleHalved++;
            }
        }

        // ── 2. Apply rock materials to islands and bridges ─────────────────
        foreach (Transform child in mapGO.transform)
        {
            var mr = child.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            string n = child.name;

            if (n.StartsWith("Island_") && islandMat != null)
            {
                mr.sharedMaterial = islandMat;
                matsApplied++;
            }
            else if (n.StartsWith("Bridge_") && bridgeMat != null)
            {
                mr.sharedMaterial = bridgeMat;
                matsApplied++;
            }
        }

        // ── 3. Reposition camera to match halved map ──────────────────────
        var camGO = GameObject.Find("Main Camera");
        if (camGO != null)
        {
            Undo.RecordObject(camGO.transform, "Reposition Camera");
            camGO.transform.position    = NewCameraPos;
            camGO.transform.eulerAngles = NewCameraRot;
            Debug.Log("[RescaleMap] Camera repositioned to " + NewCameraPos);
        }
        else
        {
            Debug.LogWarning("[RescaleMap] 'Main Camera' not found — camera not moved.");
        }

        // ── 4. Mark scene dirty ───────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[RescaleMap] Complete — positions halved: {posHalved}, scales halved: {scaleHalved}, materials applied: {matsApplied}");
    }
}
