#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// Aligns the active SceneView camera to the Main Camera in the current scene.
/// Menu: Castle Defender / Setup / Align Scene View to Player Camera
/// </summary>
public static class AlignSceneViewToCamera
{
    const float DEFAULT_VIEW_DIST = 60f;

    [MenuItem("Castle Defender/Setup/Align Scene View to Player Camera")]
    public static void Align()
    {
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("[AlignSceneViewToCamera] No Camera found in scene.");
            return;
        }

        var sv = SceneView.lastActiveSceneView;
        if (sv == null)
        {
            Debug.LogError("[AlignSceneViewToCamera] No SceneView is open.");
            return;
        }

        // Pivot = point in front of the camera so the scene view orbits naturally
        float dist  = cam.orthographic ? cam.orthographicSize * 2f : DEFAULT_VIEW_DIST;
        Vector3 pivot = cam.transform.position + cam.transform.forward * dist;

        sv.LookAt(pivot, cam.transform.rotation, dist, false, instant: true); // always perspective in editor
        sv.Repaint();

        Debug.Log($"[AlignSceneViewToCamera] Aligned to '{cam.name}' " +
                  $"pos={cam.transform.position} rot={cam.transform.rotation.eulerAngles} " +
                  $"ortho={cam.orthographic} dist={dist}");
    }
}
#endif
