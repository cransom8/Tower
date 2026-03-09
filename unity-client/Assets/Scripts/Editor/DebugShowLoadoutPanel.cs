// DebugShowLoadoutPanel.cs — forces LobbyUI to step 6 while in play mode to screenshot it.
// Castle Defender → Debug → Show Loadout Panel (Play Mode)

using UnityEngine;
using UnityEditor;
using CastleDefender.UI;

public static class DebugShowLoadoutPanel
{
    [MenuItem("Castle Defender/Debug/Show Loadout Panel (Play Mode)")]
    public static void Run()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Debug] Enter play mode first.");
            return;
        }

        var lobbyUIGO = GameObject.Find("LobbyUI");
        if (lobbyUIGO == null) { Debug.LogError("[Debug] LobbyUI not found."); return; }

        var lobbyUI = lobbyUIGO.GetComponent<LobbyUI>();
        if (lobbyUI == null) { Debug.LogError("[Debug] LobbyUI component not found."); return; }

        // Call GoToStep(6) via reflection
        var method = typeof(LobbyUI).GetMethod("GoToStep",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method == null) { Debug.LogError("[Debug] GoToStep method not found."); return; }

        method.Invoke(lobbyUI, new object[] { 6 });
        Debug.Log("[Debug] Called GoToStep(6) — Panel_Loadout should now be visible.");
    }
}
