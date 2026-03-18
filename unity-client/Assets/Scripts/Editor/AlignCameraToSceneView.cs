using UnityEditor;
using UnityEngine;

public class AlignCameraToSceneView
{
    public static void Execute()
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null && SceneView.sceneViews.Count > 0)
            sv = (SceneView)SceneView.sceneViews[0];

        if (sv == null)
        {
            Debug.LogError("[AlignCamera] No SceneView found.");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[AlignCamera] No Main Camera found.");
            return;
        }

        Vector3 pos = sv.pivot - sv.rotation * Vector3.forward * sv.cameraDistance;
        Quaternion rot = sv.rotation;

        Undo.RecordObject(cam.transform, "Align Lobby Camera to Scene View");
        cam.transform.SetPositionAndRotation(pos, rot);
        EditorUtility.SetDirty(cam.gameObject);

        Debug.Log($"[AlignCamera] Done. pos={pos}, euler={rot.eulerAngles}");
    }

    [MenuItem("Castle Defender/Setup/Align Lobby Camera to Scene View")]
    static void AlignMenuItem() => Execute();
}
