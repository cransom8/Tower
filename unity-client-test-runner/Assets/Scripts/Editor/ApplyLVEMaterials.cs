using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public class ApplyLVEMaterials
{
    [MenuItem("Castle Defender/Apply LVE Materials")]
    public static void Apply()
    {
        Material rockMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Cliffs/Materials/lava_rocks_01_BC.mat");
        Material lavaMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/NatureManufacture Assets/L.V.E- Lava and Volcano Environment/Lava Materials/Lava_3_flow.mat");
        // Stone-grey material for the game board (islands + bridges)
        Material stoneMat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Materials/GameBoard_Stone.mat");

        if (rockMat == null) { Debug.LogError("[LVE] lava_rocks_01_BC.mat not found"); return; }
        if (stoneMat == null) { Debug.LogWarning("[LVE] GameBoard_Stone.mat not found — islands/bridges will use rockMat fallback"); stoneMat = rockMat; }

        int count = 0;

        // Apply to every renderer under BridgeDetails
        GameObject bridgeDetails = GameObject.Find("BridgeDetails");
        if (bridgeDetails != null)
        {
            foreach (var r in bridgeDetails.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = rockMat;
                count++;
            }
        }

        // Apply stone mat to bridges A-F
        string[] bridgeNames = { "Bridge_A","Bridge_B","Bridge_C","Bridge_D","Bridge_E","Bridge_F" };
        foreach (var n in bridgeNames)
        {
            var go = GameObject.Find(n);
            if (go != null)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null) { r.sharedMaterial = stoneMat; count++; }
            }
        }

        // Re-apply lava to LavaPlane in case it got overwritten
        var lavaPlane = GameObject.Find("LavaPlane");
        if (lavaPlane != null && lavaMat != null)
        {
            var r = lavaPlane.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = lavaMat;
        }

        // Apply stone mat to islands
        string[] islandNames = { "Island_Dwarf","Island_Split","Island_Center","Island_Merge","Island_Goblin" };
        foreach (var n in islandNames)
        {
            var go = GameObject.Find(n);
            if (go != null)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = stoneMat;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[LVE] Applied materials to {count} bridge detail renderers.");
    }
}
