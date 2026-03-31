#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CastleDefender.Game;

/// <summary>
/// Sets the starting camera in Game_ML to Player_Bridge_Lane_1
/// (TileGrid lane 0 — upper-left bridge, world center approx (-77, 2, +25)).
///
/// Menu: Castle Defender/Setup/Set Player Bridge Camera (Player 1)
/// </summary>
public static class SetPlayerBridgeCamera
{
    const int   PLAYER_LANE   = 0;   // TileGrid lane index for Player_Bridge_Lane_1
    const float CAM_HEIGHT    = 20f;
    const float CAM_Z_OFFSET  = -10f;
    const float ORTHO_SIZE    = 30f;  // half-height: 60 world units fits the 54-unit lane length

    [MenuItem("Castle Defender/Setup/Set Player Bridge Camera (Player 1)")]
    public static void Run()
    {
        // Compute camera target from TileGrid lane 0 midpoint
        Vector3 castlePos  = TileGrid.TileToWorld(PLAYER_LANE, 5, 27);
        Vector3 spawnPos   = TileGrid.TileToWorld(PLAYER_LANE, 5, 0);
        Vector3 laneCenter = (castlePos + spawnPos) * 0.5f + new Vector3(0f, 0f, CAM_Z_OFFSET);
        Vector3 camPos     = laneCenter + Vector3.up * CAM_HEIGHT;

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Game_ML.unity", OpenSceneMode.Single);

        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            cam.orthographic       = true;
            cam.orthographicSize   = ORTHO_SIZE;
            cam.transform.position = camPos;
            cam.transform.LookAt(laneCenter, Vector3.left);
            EditorUtility.SetDirty(cam);
        }

        var gm = Object.FindFirstObjectByType<GameManager>();
        if (gm != null)
        {
            gm.BattlefieldCameraPosition = camPos;
            gm.BattlefieldLookTarget     = laneCenter;
            gm.CameraOrthoSize           = ORTHO_SIZE;
            EditorUtility.SetDirty(gm);
        }

        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[SetPlayerBridgeCamera] Game_ML: cam={camPos}, lookAt={laneCenter}");
    }
}
#endif
