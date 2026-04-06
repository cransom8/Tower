using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public static class SceneEventSystemUtility
{
    static readonly List<RaycastResult> s_uiRaycastScratch = new();

    public static EventSystem FindInScene(Scene scene, bool includeInactive = true)
    {
        if (scene.IsValid())
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var eventSystem = roots[i].GetComponentInChildren<EventSystem>(includeInactive);
                if (eventSystem != null)
                    return eventSystem;
            }
        }

        return null;
    }

    public static EventSystem FindInScene(Component component, bool includeInactive = true)
    {
        return component == null ? FindInScene(default(Scene), includeInactive) : FindInScene(component.gameObject.scene, includeInactive);
    }

    public static EventSystem FindBest(Scene scene)
    {
        var sceneLocal = FindInScene(scene, includeInactive: true);
        if (sceneLocal != null)
            return sceneLocal;

        if (EventSystem.current != null && EventSystem.current.gameObject.scene.isLoaded)
            return EventSystem.current;

        return Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
    }

    public static EventSystem FindBest(Component component)
    {
        return component == null ? FindBest(default(Scene)) : FindBest(component.gameObject.scene);
    }

    public static bool IsPointerOverUi(Vector2 screenPosition, int pointerId = -1, EventSystem eventSystem = null)
    {
        eventSystem ??= EventSystem.current;
        if (eventSystem == null)
            return false;

        bool pointerOverTrackedUi = pointerId >= 0
            ? eventSystem.IsPointerOverGameObject(pointerId)
            : eventSystem.IsPointerOverGameObject();
        if (pointerOverTrackedUi)
            return true;

        s_uiRaycastScratch.Clear();
        var eventData = new PointerEventData(eventSystem)
        {
            pointerId = pointerId,
            position = screenPosition,
        };
        eventSystem.RaycastAll(eventData, s_uiRaycastScratch);
        return s_uiRaycastScratch.Count > 0;
    }

    static void DeactivateCompetingEventSystems(EventSystem keep, Scene targetScene, string logContext)
    {
        var activeEventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < activeEventSystems.Length; i++)
        {
            var candidate = activeEventSystems[i];
            if (candidate == null || candidate == keep)
                continue;

            var candidateObject = candidate.gameObject;
            if (candidateObject == null || !candidateObject.activeInHierarchy)
                continue;

            candidateObject.SetActive(false);

            string targetSceneName = targetScene.IsValid() ? targetScene.name : "<invalid>";
            string candidateSceneName = candidateObject.scene.IsValid() ? candidateObject.scene.name : "<invalid>";
            Debug.Log(
                $"[{logContext}] Deactivated competing EventSystem '{candidateObject.name}' from scene '{candidateSceneName}' before enabling '{keep?.gameObject?.name ?? "<null>"}' in scene '{targetSceneName}'.");
        }
    }

    public static EventSystem EnsureSceneLocal(Component owner, string eventSystemName, string logContext)
    {
        var targetScene = owner != null ? owner.gameObject.scene : default(Scene);
        var existing = FindInScene(targetScene, includeInactive: true);

        if (existing == null)
        {
            var go = new GameObject(eventSystemName);
            go.SetActive(false);
            if (targetScene.IsValid())
                SceneManager.MoveGameObjectToScene(go, targetScene);

            existing = go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
            go.AddComponent<SingleEventSystem>();
            DeactivateCompetingEventSystems(existing, targetScene, logContext);
            go.SetActive(true);
            Debug.Log($"[{logContext}] Created scene-local EventSystem '{go.name}'.");
            return existing;
        }

        if (existing.GetComponent<BaseInputModule>() == null)
        {
            existing.gameObject.AddComponent<StandaloneInputModule>();
            Debug.Log($"[{logContext}] Added missing StandaloneInputModule to scene-local EventSystem '{existing.name}'.");
        }

        if (existing.GetComponent<SingleEventSystem>() == null)
            existing.gameObject.AddComponent<SingleEventSystem>();

        DeactivateCompetingEventSystems(existing, targetScene, logContext);

        if (!existing.gameObject.activeSelf)
        {
            existing.gameObject.SetActive(true);
            Debug.Log($"[{logContext}] Reactivated scene-local EventSystem '{existing.name}'.");
        }

        return existing;
    }
}
