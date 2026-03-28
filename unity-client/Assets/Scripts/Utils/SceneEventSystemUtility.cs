using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class SceneEventSystemUtility
{
    public static EventSystem FindBest(Scene scene)
    {
        if (scene.IsValid())
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var eventSystem = roots[i].GetComponentInChildren<EventSystem>(true);
                if (eventSystem != null)
                    return eventSystem;
            }
        }

        if (EventSystem.current != null && EventSystem.current.gameObject.scene.isLoaded)
            return EventSystem.current;

        return Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
    }

    public static EventSystem FindBest(Component component)
    {
        return component == null ? FindBest(default(Scene)) : FindBest(component.gameObject.scene);
    }
}
