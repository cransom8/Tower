// SetupPortraitStudio.cs — Creates the off-screen portrait camera in Lobby scene.
// Run via: Castle Defender → Setup → Setup Portrait Studio
//
// Creates:
//   PortraitStudio  (UnitPortraitCamera component)
//   ├── Camera  (renders to UnitPortrait RenderTexture, positioned at y=-9999)
//   └── StagePoint  (unit prefab spawned here)
//
// Also:
//   - Creates Assets/RenderTextures/UnitPortrait.renderTexture (256x256)
//   - Assigns the RenderTexture to LoadoutUI.Img_Portrait
//   - Assigns UnitPrefabRegistry from Assets/UnitPrefabRegistry.asset
//   - Assigns PortraitCam on LoadoutUI

using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using CastleDefender.UI;
using CastleDefender.Game;

public static class SetupPortraitStudio
{
    [MenuItem("Castle Defender/Setup/Setup Portrait Studio")]
    public static void Run()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.name.Contains("Lobby"))
        {
            Debug.LogError("[SetupPortraitStudio] Open the Lobby scene first.");
            return;
        }

        // ── 1. Create / load RenderTexture asset ──────────────────────────────
        const string rtPath = "Assets/RenderTextures/UnitPortrait.renderTexture";
        if (!System.IO.Directory.Exists("Assets/RenderTextures"))
            System.IO.Directory.CreateDirectory("Assets/RenderTextures");

        RenderTexture rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(rtPath);
        if (rt == null)
        {
            rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 2;
            rt.name = "UnitPortrait";
            AssetDatabase.CreateAsset(rt, rtPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SetupPortraitStudio] Created RenderTexture at " + rtPath);
        }
        else
        {
            Debug.Log("[SetupPortraitStudio] Reusing existing RenderTexture.");
        }

        // ── 2. Remove existing PortraitStudio (idempotent) ────────────────────
        var existingPS = GameObject.Find("PortraitStudio");
        if (existingPS != null) { Undo.DestroyObjectImmediate(existingPS); }

        // ── 3. Create PortraitStudio root ─────────────────────────────────────
        var studioGO = new GameObject("PortraitStudio");
        Undo.RegisterCreatedObjectUndo(studioGO, "Create PortraitStudio");
        studioGO.transform.position = new Vector3(0f, -9999f, 0f);

        // ── 4. Camera child ───────────────────────────────────────────────────
        var camGO = new GameObject("Camera");
        Undo.RegisterCreatedObjectUndo(camGO, "Create PortraitCamera");
        camGO.transform.SetParent(studioGO.transform, false);
        // Camera at y=1.2 (model midpoint), pulled back 4 units — fits tall models (cyclops etc.)
        camGO.transform.localPosition = new Vector3(0f, 1.2f, -4f);
        camGO.transform.localRotation = Quaternion.identity;

        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.08f, 0.08f, 0.12f, 1f);
        cam.cullingMask      = -1; // everything — only portrait objects are at y=-9999
        cam.fieldOfView      = 45f;
        cam.nearClipPlane    = 0.1f;
        cam.farClipPlane     = 30f;
        cam.depth            = -10f;
        cam.targetTexture    = rt;

        // Add URP camera data if available
        var urpType = System.Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        if (urpType != null) camGO.AddComponent(urpType);

        // ── 5. StagePoint child ───────────────────────────────────────────────
        var stageGO = new GameObject("StagePoint");
        Undo.RegisterCreatedObjectUndo(stageGO, "Create StagePoint");
        stageGO.transform.SetParent(studioGO.transform, false);
        stageGO.transform.localPosition = new Vector3(0f, 0f, 0f);

        // ── 6. UnitPortraitCamera component on root ───────────────────────────
        var portraitCam = studioGO.AddComponent<UnitPortraitCamera>();

        var pcSO = new SerializedObject(portraitCam);
        pcSO.FindProperty("Cam").objectReferenceValue        = cam;
        pcSO.FindProperty("RenderTex").objectReferenceValue  = rt;
        pcSO.FindProperty("StagePoint").objectReferenceValue = stageGO.transform;
        pcSO.ApplyModifiedProperties();

        // Assign UnitPrefabRegistry if it exists
        var registry = AssetDatabase.LoadAssetAtPath<UnitPrefabRegistry>("Assets/UnitPrefabRegistry.asset");
        if (registry != null)
        {
            var pcSO2 = new SerializedObject(portraitCam);
            pcSO2.FindProperty("Registry").objectReferenceValue = registry;
            pcSO2.ApplyModifiedProperties();
            Debug.Log("[SetupPortraitStudio] Assigned UnitPrefabRegistry.");
        }
        else
        {
            Debug.LogWarning("[SetupPortraitStudio] UnitPrefabRegistry.asset not found at Assets/ root — assign manually on PortraitStudio.");
        }

        // ── 7. Wire PortraitCam and Img_Portrait on LoadoutUI ──────────────────
        var canvasT     = GameObject.Find("Canvas")?.transform;
        var panelT      = canvasT?.Find("Panel_Loadout");
        if (panelT != null)
        {
            var lui = panelT.GetComponent<LoadoutUI>();
            if (lui != null)
            {
                var luiSO = new SerializedObject(lui);
                luiSO.FindProperty("PortraitCam").objectReferenceValue = portraitCam;

                // Find Img_Portrait RawImage
                var imgPortrait = panelT.GetComponentInChildren<RawImage>();
                if (imgPortrait != null)
                {
                    imgPortrait.texture = rt;
                    luiSO.FindProperty("Img_Portrait").objectReferenceValue = imgPortrait;
                    Debug.Log("[SetupPortraitStudio] Assigned RenderTexture to Img_Portrait.");
                }
                luiSO.ApplyModifiedProperties();
                Debug.Log("[SetupPortraitStudio] Wired LoadoutUI.PortraitCam.");
            }
        }
        else
        {
            Debug.LogWarning("[SetupPortraitStudio] Panel_Loadout not found — run Setup Loadout Panel first.");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[SetupPortraitStudio] Done. Save the Lobby scene (Ctrl+S).");
    }
}
