// SingleEventSystem.cs
// Keeps accidental duplicate EventSystems from surviving scene boot,
// but no longer persists the EventSystem across scene loads.

using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public class SingleEventSystem : MonoBehaviour
{
    static SingleEventSystem _instance;

    void Awake()
    {
        if (_instance == null || !_instance)
        {
            _instance = this;
            return;
        }

        if (_instance != null && _instance != this)
        {
            if (ShouldReplaceExistingInstance(_instance))
            {
                Debug.Log($"[SingleEventSystem] Replacing stale EventSystem '{_instance.gameObject.name}' with '{gameObject.name}'.");
                if (_instance.gameObject.activeSelf)
                    _instance.gameObject.SetActive(false);
                Destroy(_instance.gameObject);
                _instance = this;
                return;
            }

            Debug.Log($"[SingleEventSystem] Duplicate found on '{gameObject.name}' - destroying it.");
            Destroy(gameObject);
            return;
        }

        _instance = this;
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    bool ShouldReplaceExistingInstance(SingleEventSystem existing)
    {
        if (existing == null || !existing)
            return true;

        var existingGameObject = existing.gameObject;
        if (existingGameObject == null)
            return true;

        if (!existingGameObject.activeInHierarchy)
            return true;

        return existingGameObject.scene != gameObject.scene;
    }
}
