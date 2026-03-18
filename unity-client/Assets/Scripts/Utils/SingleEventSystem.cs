// SingleEventSystem.cs
// Attach ONLY to the EventSystem in the LOBBY scene (the first scene).
// Do NOT add an EventSystem to Game_ML or Game_Classic at all —
// this one persists across scene loads via DontDestroyOnLoad.
//
// Unity 6 UI Toolkit creates a second EventSystem after scene load via its
// interop bridge. Update() catches and destroys it every frame until it stops.

using UnityEngine;
using UnityEngine.EventSystems;

public class SingleEventSystem : MonoBehaviour
{
    static SingleEventSystem _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Debug.Log($"[SingleEventSystem] Duplicate found on '{gameObject.name}' — destroying it.");
            DestroyImmediate(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[SingleEventSystem] Persisting Lobby EventSystem across scenes.");
    }

    void Update()
    {
        // Destroy any EventSystem that isn't us — catches Unity 6 UI Toolkit's
        // late-created interop EventSystem which bypasses Awake-time detection.
        var all = FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
        if (all.Length <= 1) return;
        foreach (var es in all)
        {
            if (es.gameObject != gameObject)
            {
                Debug.Log($"[SingleEventSystem] Late duplicate destroyed: '{es.gameObject.name}'");
                Destroy(es.gameObject);
            }
        }
    }
}
