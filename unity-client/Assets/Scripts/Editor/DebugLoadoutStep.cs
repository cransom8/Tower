// DebugLoadoutStep.cs — temporary runtime diagnostic.
// Run: Castle Defender → Debug → Log Loadout Step State
// Works in both edit and play mode.

using UnityEngine;
using UnityEditor;
using CastleDefender.UI;

public static class DebugLoadoutStep
{
    [MenuItem("Castle Defender/Debug/Log Loadout Step State")]
    public static void Run()
    {
        // Find LobbyUI component
        var lobbyUIGO = GameObject.Find("LobbyUI");
        if (lobbyUIGO == null)
        {
            Debug.LogError("[Debug] LobbyUI GameObject not found.");
            return;
        }

        var lobbyUI = lobbyUIGO.GetComponent<LobbyUI>();
        if (lobbyUI == null)
        {
            Debug.LogError("[Debug] LobbyUI component not found on LobbyUI GO.");
            return;
        }

        // Read LoadoutStep via reflection so we don't need a code change
        var field = typeof(LobbyUI).GetField("LoadoutStep",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

        if (field == null)
        {
            Debug.LogError("[Debug] LobbyUI.LoadoutStep field not found via reflection.");
            return;
        }

        var value = field.GetValue(lobbyUI);
        if (value == null || value.Equals(null))
        {
            Debug.LogError("[Debug] LobbyUI.LoadoutStep IS NULL at runtime — wiring failed.");
        }
        else
        {
            var lui = value as LoadoutUI;
            Debug.Log("[Debug] LobbyUI.LoadoutStep = " + lui.gameObject.name
                + "  activeSelf=" + lui.gameObject.activeSelf
                + "  activeInHierarchy=" + lui.gameObject.activeInHierarchy);

            // Also check slots wiring
            Debug.Log("[Debug] LoadoutUI.Btn_Confirm = " + (lui.Btn_Confirm != null ? lui.Btn_Confirm.name : "NULL"));
            Debug.Log("[Debug] LoadoutUI.Btn_Back    = " + (lui.Btn_Back    != null ? lui.Btn_Back.name    : "NULL"));
            Debug.Log("[Debug] LoadoutUI.SlotButtons count = " + (lui.SlotButtons != null ? lui.SlotButtons.Length.ToString() : "NULL"));
        }
    }
}
