#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using CastleDefender.Game;

/// <summary>
/// Moves the editor camera to preview the game from each player's lane perspective.
/// Does NOT save the scene — preview only.
/// Menu: Castle Defender / Preview As / ...
/// </summary>
public static class PreviewAsPlayer
{
    const float CAM_HEIGHT   = 20f;
    const float CAM_Z_OFFSET = -10f;
    const float ORTHO_SIZE   = 30f;

    [MenuItem("Castle Defender/Preview As/Lane 0 — Red (Player 1)")]
    static void PreviewLane0() => Apply(0);

    [MenuItem("Castle Defender/Preview As/Lane 1 — Gold (Player 2)")]
    static void PreviewLane1() => Apply(1);

    [MenuItem("Castle Defender/Preview As/Lane 2 — Blue (Player 3)")]
    static void PreviewLane2() => Apply(2);

    [MenuItem("Castle Defender/Preview As/Lane 3 — Green (Player 4)")]
    static void PreviewLane3() => Apply(3);

    static readonly string[] LaneNames = { "Red", "Gold", "Blue", "Green" };

    static void Apply(int laneIndex)
    {
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null) { Debug.LogError("[PreviewAsPlayer] No Camera found in scene."); return; }

        Vector3 castlePos  = TileGrid.TileToWorld(laneIndex, 5, 27);
        Vector3 spawnPos   = TileGrid.TileToWorld(laneIndex, 5, 0);
        Vector3 laneCenter = (castlePos + spawnPos) * 0.5f + new Vector3(0f, 0f, CAM_Z_OFFSET);
        Vector3 camPos     = laneCenter + Vector3.up * CAM_HEIGHT;

        cam.orthographic     = true;
        cam.orthographicSize = ORTHO_SIZE;
        cam.transform.position = camPos;
        cam.transform.LookAt(laneCenter, Vector3.left); // −X up → lane vertical on screen

        // Snap scene view to match
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.LookAt(laneCenter, cam.transform.rotation, ORTHO_SIZE * 2f, false, instant: true);
            sv.Repaint();
        }

        Debug.Log($"[PreviewAsPlayer] Lane {laneIndex} ({LaneNames[laneIndex]}): pos={camPos} lookAt={laneCenter}");
    }
}
#endif
