using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace CastleDefender.Net
{
    public sealed class RemoteLoopMusicLoader : MonoBehaviour
    {
        static readonly BindingFlags InstanceBindingFlags = BindingFlags.Public | BindingFlags.Instance;
        static readonly BindingFlags StaticBindingFlags = BindingFlags.Public | BindingFlags.Static;

        static RemoteLoopMusicLoader _instance;

        readonly Dictionary<string, AsyncOperationHandle<AudioClip>> _loadedHandlesByAddress = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _addressesInFlight = new(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> _failedAddresses = new(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureRuntimeInstance()
        {
            EnsureInstance();
        }

        static RemoteLoopMusicLoader EnsureInstance()
        {
            if (_instance != null)
                return _instance;

            var go = new GameObject("RemoteLoopMusicLoader");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<RemoteLoopMusicLoader>();
            return _instance;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void OnEnable()
        {
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
        }

        void Start()
        {
            QueueConfiguredLoopLoads();
        }

        void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
        }

        void OnDestroy()
        {
            if (_instance == this)
                _instance = null;

            foreach (AsyncOperationHandle<AudioClip> handle in _loadedHandlesByAddress.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }

            _loadedHandlesByAddress.Clear();
            _addressesInFlight.Clear();
            _failedAddresses.Clear();
        }

        void HandleActiveSceneChanged(Scene _, Scene __)
        {
            QueueConfiguredLoopLoads();
        }

        void QueueConfiguredLoopLoads()
        {
            if (!TryGetAudioManager(out Type audioManagerType, out object audioManager))
                return;

            QueueConfiguredLoopClip(audioManagerType, audioManager, "menuMusicLoop", "remoteMenuMusicLoopAddress");
            QueueConfiguredLoopClip(audioManagerType, audioManager, "gameplayMusicLoop", "remoteGameplayMusicLoopAddress");
            QueueConfiguredLoopClip(audioManagerType, audioManager, "ambientLoop", "remoteAmbientMusicLoopAddress");
            ApplyLoadedClips(audioManagerType, audioManager);
        }

        void QueueConfiguredLoopClip(Type audioManagerType, object audioManager, string clipFieldName, string remoteAddressFieldName)
        {
            if (audioManagerType == null || audioManager == null)
                return;

            if (ReadAudioClipField(audioManagerType, audioManager, clipFieldName) != null)
                return;

            string address = ReadStringField(audioManagerType, audioManager, remoteAddressFieldName);
            if (string.IsNullOrWhiteSpace(address))
                return;

            QueueRemoteLoopClip(address.Trim());
        }

        void QueueRemoteLoopClip(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                return;

            if (_loadedHandlesByAddress.ContainsKey(address)
                || _addressesInFlight.Contains(address)
                || _failedAddresses.Contains(address))
            {
                return;
            }

            StartCoroutine(LoadRemoteLoopClip(address));
        }

        IEnumerator LoadRemoteLoopClip(string address)
        {
            _addressesInFlight.Add(address);

            try
            {
                RemoteContentManager remoteContent = RemoteContentManager.EnsureInstance();
                if (remoteContent == null)
                {
                    Debug.LogWarning($"[RemoteLoopMusicLoader] RemoteContentManager could not be created while loading '{address}'.");
                    _failedAddresses.Add(address);
                    yield break;
                }

                if (!remoteContent.AreAddressablesInitialized)
                    yield return remoteContent.EnsureAddressablesReady(requester: "RemoteLoopMusicLoader");

                if (!remoteContent.AreAddressablesInitialized)
                {
                    Debug.LogWarning(
                        $"[RemoteLoopMusicLoader] Addressables were not ready for '{address}'. " +
                        $"{remoteContent.LastError ?? "No additional error was reported."}");
                    _failedAddresses.Add(address);
                    yield break;
                }

                AsyncOperationHandle<AudioClip> loadHandle;
                try
                {
                    loadHandle = Addressables.LoadAssetAsync<AudioClip>(address);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RemoteLoopMusicLoader] Failed to queue remote loop music '{address}': {ex.Message}");
                    _failedAddresses.Add(address);
                    yield break;
                }

                if (!loadHandle.IsValid())
                {
                    Debug.LogWarning($"[RemoteLoopMusicLoader] Addressables returned an invalid handle for '{address}'.");
                    _failedAddresses.Add(address);
                    yield break;
                }

                yield return loadHandle;

                if (!loadHandle.IsValid() || loadHandle.Status != AsyncOperationStatus.Succeeded || loadHandle.Result == null)
                {
                    string failure = loadHandle.OperationException?.Message ?? "The AudioClip result was null.";
                    Debug.LogWarning($"[RemoteLoopMusicLoader] Failed to load remote loop music '{address}': {failure}");
                    if (loadHandle.IsValid())
                        Addressables.Release(loadHandle);
                    _failedAddresses.Add(address);
                    yield break;
                }

                _loadedHandlesByAddress[address] = loadHandle;
                Debug.Log($"[RemoteLoopMusicLoader] Loaded remote loop music '{address}'.");
            }
            finally
            {
                _addressesInFlight.Remove(address);

                if (TryGetAudioManager(out Type audioManagerType, out object audioManager))
                    ApplyLoadedClips(audioManagerType, audioManager);
            }
        }

        void ApplyLoadedClips(Type audioManagerType, object audioManager)
        {
            if (audioManagerType == null || audioManager == null)
                return;

            bool assignedAnyClip = false;
            assignedAnyClip |= AssignLoadedClip(audioManagerType, audioManager, "menuMusicLoop", "remoteMenuMusicLoopAddress");
            assignedAnyClip |= AssignLoadedClip(audioManagerType, audioManager, "gameplayMusicLoop", "remoteGameplayMusicLoopAddress");
            assignedAnyClip |= AssignLoadedClip(audioManagerType, audioManager, "ambientLoop", "remoteAmbientMusicLoopAddress");

            if (assignedAnyClip)
            {
                audioManagerType
                    .GetMethod("RefreshLoopPlaybackForCurrentScene", InstanceBindingFlags)
                    ?.Invoke(audioManager, new object[] { true });
            }
        }

        bool AssignLoadedClip(Type audioManagerType, object audioManager, string clipFieldName, string remoteAddressFieldName)
        {
            if (ReadAudioClipField(audioManagerType, audioManager, clipFieldName) != null)
                return false;

            string address = ReadStringField(audioManagerType, audioManager, remoteAddressFieldName);
            if (string.IsNullOrWhiteSpace(address))
                return false;

            if (!TryGetLoadedClip(address.Trim(), out AudioClip clip) || clip == null)
                return false;

            FieldInfo clipField = audioManagerType.GetField(clipFieldName, InstanceBindingFlags);
            if (clipField == null)
                return false;

            clipField.SetValue(audioManager, clip);
            return true;
        }

        bool TryGetLoadedClip(string address, out AudioClip clip)
        {
            clip = null;
            if (string.IsNullOrWhiteSpace(address))
                return false;

            if (!_loadedHandlesByAddress.TryGetValue(address, out AsyncOperationHandle<AudioClip> handle))
                return false;

            if (!handle.IsValid() || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                return false;

            clip = handle.Result;
            return true;
        }

        static AudioClip ReadAudioClipField(Type audioManagerType, object audioManager, string fieldName)
        {
            return audioManagerType?.GetField(fieldName, InstanceBindingFlags)?.GetValue(audioManager) as AudioClip;
        }

        static string ReadStringField(Type audioManagerType, object audioManager, string fieldName)
        {
            object value = audioManagerType?.GetField(fieldName, InstanceBindingFlags)?.GetValue(audioManager);
            return value as string;
        }

        static bool TryGetAudioManager(out Type audioManagerType, out object audioManager)
        {
            audioManagerType = FindType("AudioManager");
            audioManager = null;
            if (audioManagerType == null)
                return false;

            audioManager = audioManagerType.GetProperty("I", StaticBindingFlags)?.GetValue(null);
            return audioManager != null;
        }

        static Type FindType(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
                return null;

            Type type = Type.GetType(fullName, false);
            if (type != null)
                return type;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                type = assemblies[i].GetType(fullName, false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }
}
