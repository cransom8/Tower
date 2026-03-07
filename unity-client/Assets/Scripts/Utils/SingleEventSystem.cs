// SingleEventSystem.cs
// Attach ONLY to the EventSystem in the LOBBY scene (the first scene).
// Do NOT add an EventSystem to Game_ML or Game_Classic at all —
// this one persists across scene loads via DontDestroyOnLoad.
//
// If a second EventSystem somehow appears (e.g. duplicate in another scene),
// this script destroys the newcomer and keeps itself.

using UnityEngine;
using UnityEngine.EventSystems;

public class SingleEventSystem : MonoBehaviour
{
    static SingleEventSystem _instance;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            // A persistent EventSystem already exists — destroy this duplicate immediately.
            Debug.Log($"[SingleEventSystem] Duplicate found on '{gameObject.name}' — destroying it.");
            DestroyImmediate(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
        Debug.Log("[SingleEventSystem] Persisting Lobby EventSystem across scenes.");
    }
}
