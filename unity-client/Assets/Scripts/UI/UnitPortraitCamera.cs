// UnitPortraitCamera.cs - Off-screen camera renders unit or building prefabs to a RenderTexture.
// Place one "PortraitStudio" object in Lobby scene. SetupPortraitStudio creates it.
// Match UI flows call ShowUnit(key) to update portrait captures.

using System;
using System.Collections;
using CastleDefender.Game;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CastleDefender.UI
{
    public class UnitPortraitCamera : MonoBehaviour
    {
        [Header("References (wired by SetupPortraitStudio)")]
        public Camera Cam;
        public RenderTexture RenderTex;
        public Transform StagePoint;
        public UnitPrefabRegistry Registry;

        [Header("Pose")]
        [Tooltip("Y rotation applied to staged unit. 0 = facing camera directly.")]
        public float RotationY = 0f;

        [Header("Auto-fit")]
        [Tooltip("Automatically scale each unit so it fills FitHeight world units in the frame.")]
        public bool AutoFitSize = true;
        [Tooltip("Target height in world units that every unit is scaled to fill.")]
        public float FitHeight = 2.25f;
        [Tooltip("How much of the frame height the unit should occupy. Higher = closer portrait.")]
        [Range(0.5f, 0.95f)] public float FrameFill = 0.82f;
        [Tooltip("Bias framing upward so the portrait feels like a closer character shot.")]
        [Range(0f, 1f)] public float VerticalFocus = 0.62f;
        [Tooltip("Normalized camera height offset relative to framed bounds height. Negative values read more front-on for buildings.")]
        [Range(-0.5f, 0.5f)] public float CameraHeightBias = 0.02f;
        [Tooltip("Normalized look-at offset relative to framed bounds height.")]
        [Range(-0.25f, 0.5f)] public float LookAtHeightBias = 0.06f;

        GameObject _staged;
        Animator[] _stagedAnimators = Array.Empty<Animator>();
        UnitAnimationResolver.ResolvedProfile _stagedAnimationProfile;

        public void ShowUnit(string key)
        {
            if (Registry == null || Cam == null)
                return;

            var prefab = Registry.GetPrefab(key);
            if (prefab == null)
            {
                Debug.LogWarning($"[UnitPortraitCamera] No prefab resolved for '{key}'.");
                return;
            }

            ShowPrefab(prefab, Registry.GetScale(key));
        }

        public GameObject StagedObject => _staged;

        public void ShowPrefab(GameObject prefab, float scale = 1f, Action<GameObject> configureInstance = null)
        {
            Clear();
            if (prefab == null || Cam == null)
                return;

            ClearStageChildren();

            _staged = Instantiate(
                prefab,
                StagePoint != null ? StagePoint.position : Cam.transform.position + Cam.transform.forward * 2f,
                Quaternion.Euler(0f, RotationY, 0f));
            if (StagePoint != null)
                _staged.transform.SetParent(StagePoint, true);

            SetLayerRecursively(_staged, RuntimePortraitStudio.PortraitLayer);
            _staged.transform.localScale = Vector3.one * scale;

#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfAnyPrefab(_staged))
                PrefabUtility.UnpackPrefabInstance(_staged, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
#endif

            configureInstance?.Invoke(_staged);

            PrepareStagedObject(prefab.name, scale);
        }

        public Animator[] GetStagedAnimators()
        {
            return _stagedAnimators ?? Array.Empty<Animator>();
        }

        public void SetAnimatorSpeed(float speed)
        {
            var animators = GetStagedAnimators();
            float resolvedSpeed = Mathf.Max(0.05f, speed);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                animator.speed = resolvedSpeed;
            }
        }

        public bool HasAnyState(string[] stateNames)
        {
            return FindPlayableState(stateNames, out _, out _);
        }

        public bool PlayFirstAvailableState(string[] stateNames, float transitionDuration = 0.08f)
        {
            return TryPlayFirstAvailableState(stateNames, out _, out _, transitionDuration);
        }

        public bool TryPlayFirstAvailableState(
            string[] stateNames,
            out string playedState,
            out float clipLength,
            float transitionDuration = 0.08f)
        {
            playedState = null;
            clipLength = 0f;

            if (!FindPlayableState(stateNames, out var animator, out var stateName) || animator == null)
                return false;

            playedState = stateName;
            clipLength = UnitAnimationResolver.ResolveClipLength(animator, stateName);
            PlayStateOnAnimators(animator, stateName, transitionDuration);
            return true;
        }

        public void Clear()
        {
            if (_staged == null)
                return;

            _staged.SetActive(false);
            DestroyImmediate(_staged);
            _staged = null;
            _stagedAnimators = Array.Empty<Animator>();
            _stagedAnimationProfile = null;
        }

        void LateUpdate()
        {
            if (_staged == null || Cam == null || RenderTex == null)
                return;

            if (!RenderTex.IsCreated())
                RenderTex.Create();

            Cam.Render();
        }

        void ClearStageChildren()
        {
            if (StagePoint == null)
                return;

            for (int i = StagePoint.childCount - 1; i >= 0; i--)
            {
                var child = StagePoint.GetChild(i);
                if (_staged != null && child.gameObject == _staged)
                    continue;

                DestroyImmediate(child.gameObject);
            }
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        Bounds CalculateBounds(Renderer[] renderers)
        {
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        void FrameStagedUnit(Bounds bounds)
        {
            if (_staged == null || Cam == null)
                return;

            float height = Mathf.Max(0.1f, bounds.size.y);
            float width = Mathf.Max(0.1f, bounds.size.x);
            Vector3 focusPoint = new Vector3(
                bounds.center.x,
                bounds.min.y + (height * VerticalFocus),
                bounds.center.z);

            float halfHeight = height * 0.5f;
            float halfWidth = width * 0.5f;
            float verticalFov = Cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * Cam.aspect);
            float fill = Mathf.Max(0.01f, FrameFill);

            float distForHeight = halfHeight / (Mathf.Tan(verticalFov * 0.5f) * fill);
            float distForWidth = halfWidth / (Mathf.Tan(horizontalFov * 0.5f) * fill);
            float camDistance = Mathf.Max(distForHeight, distForWidth, 1.1f);

            Cam.transform.position = focusPoint + new Vector3(0f, height * CameraHeightBias, -camDistance);
            Cam.transform.rotation = Quaternion.identity;
            Cam.transform.LookAt(focusPoint + new Vector3(0f, height * LookAtHeightBias, 0f));
            var viewport = Cam.WorldToViewportPoint(bounds.center);
            Debug.Log($"[UnitPortraitCamera] Frame '{_staged.name}' center=({bounds.center.x:0.00},{bounds.center.y:0.00},{bounds.center.z:0.00}) size=({bounds.size.x:0.00},{bounds.size.y:0.00},{bounds.size.z:0.00}) cam=({Cam.transform.position.x:0.00},{Cam.transform.position.y:0.00},{Cam.transform.position.z:0.00}) viewport=({viewport.x:0.00},{viewport.y:0.00},{viewport.z:0.00})");
        }

        /// <summary>
        /// Renders the unit into the RenderTexture for one frame, captures it to a Texture2D,
        /// then calls callback. Runs as a coroutine so must be called on an active MonoBehaviour.
        /// </summary>
        public void StartIconCapture(string key, Action<Texture2D> callback)
            => StartCoroutine(CaptureCoroutine(key, callback));

        public Texture2D CaptureIconImmediate(string key)
        {
            ShowUnit(key);
            var result = CaptureCurrentRender(key);
            Clear();
            return result;
        }

        public Texture2D CapturePrefabImmediate(GameObject prefab, float scale = 1f, Action<GameObject> configureInstance = null)
        {
            if (prefab == null)
                return null;

            ShowPrefab(prefab, scale, configureInstance);
            var result = CaptureCurrentRender(prefab.name);
            Clear();
            return result;
        }

        IEnumerator CaptureCoroutine(string key, Action<Texture2D> callback)
        {
            ShowUnit(key);
            yield return null;
            yield return null;

            Texture2D result = CaptureCurrentRender(key);
            Clear();
            callback(result);
        }

        Texture2D CaptureCurrentRender(string key)
        {
            Texture2D result = null;
            if (Cam != null && RenderTex != null)
            {
                Cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = RenderTex;
                result = new Texture2D(RenderTex.width, RenderTex.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, RenderTex.width, RenderTex.height), 0, 0);
                result.Apply();
                var center = result.GetPixel(RenderTex.width / 2, RenderTex.height / 2);
                Debug.Log($"[UnitPortraitCamera] Captured '{key}' tex={RenderTex.width}x{RenderTex.height} center=({center.r:0.00},{center.g:0.00},{center.b:0.00},{center.a:0.00})");
                RenderTexture.active = prev;
            }

            return result;
        }

        void OnDestroy() => Clear();

        void PrepareStagedObject(string key, float scale)
        {
            foreach (var animator in _staged.GetComponentsInChildren<Animator>(true))
            {
                if (!animator.gameObject.activeInHierarchy)
                    continue;

                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            _stagedAnimators = _staged.GetComponentsInChildren<Animator>(true);
            _stagedAnimationProfile = UnitAnimationResolver.ResolveForUnit(_staged, unit: null);
            UnitAnimationResolver.PrepareAnimators(_stagedAnimators, _stagedAnimationProfile, forPortrait: true);

            for (int i = 0; i < _stagedAnimators.Length; i++)
            {
                var animator = _stagedAnimators[i];
                if (animator == null)
                    continue;

                animator.Rebind();
                animator.Update(0f);
            }

            foreach (var lodGroup in _staged.GetComponentsInChildren<LODGroup>(true))
            {
                lodGroup.ForceLOD(0);
                lodGroup.enabled = false;
            }

            foreach (var skinned in _staged.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skinned.updateWhenOffscreen = true;

            var renderers = _staged.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[UnitPortraitCamera] Prefab '{key}' has no renderers.");
                return;
            }

            var framingRenderers = ResolveFramingRenderers(renderers);

            if (AutoFitSize)
            {
                var bounds = CalculateBounds(framingRenderers);
                float height = bounds.size.y;
                if (height > 0.001f)
                {
                    float fitScale = scale * (FitHeight / height);
                    _staged.transform.localScale = Vector3.one * fitScale;
                    renderers = _staged.GetComponentsInChildren<Renderer>();
                    framingRenderers = ResolveFramingRenderers(renderers);
                }
            }

            int missingSkinnedMeshes = 0;
            int missingStaticMeshes = 0;
            int missingMaterials = 0;
            foreach (var skinned in _staged.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null)
                    missingSkinnedMeshes++;

                var materials = skinned.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    missingMaterials++;
                }
                else
                {
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] == null)
                            missingMaterials++;
                    }
                }
            }

            foreach (var filter in _staged.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    missingStaticMeshes++;
            }

            var rootAnimator = _staged.GetComponentInChildren<Animator>(true);
            bool missingController = rootAnimator != null && rootAnimator.runtimeAnimatorController == null;
            string controllerName = rootAnimator != null && rootAnimator.runtimeAnimatorController != null
                ? rootAnimator.runtimeAnimatorController.name
                : "<none>";

            FrameStagedUnit(CalculateBounds(framingRenderers));
            UpgradePortraitMaterialsToUrp();

            Debug.Log(
                $"[UnitPortraitCamera] Prepared portrait for '{key}' controller='{controllerName}' " +
                $"with {renderers.Length} renderers. missingSkinnedMeshes={missingSkinnedMeshes} " +
                $"missingStaticMeshes={missingStaticMeshes} missingMaterials={missingMaterials} " +
                $"missingController={missingController} animationProfile='{_stagedAnimationProfile?.ProfileId ?? "default"}'");
        }

        bool FindPlayableState(string[] stateNames, out Animator foundAnimator, out string foundState)
        {
            return UnitAnimationResolver.TryFindPlayableState(_stagedAnimators, stateNames, out foundAnimator, out foundState);
        }

        void PlayStateOnAnimators(Animator animator, string stateName, float transitionDuration)
        {
            int stateHash = Animator.StringToHash(stateName);
            PlayState(animator, stateHash, transitionDuration);

            for (int i = 0; i < _stagedAnimators.Length; i++)
            {
                var sibling = _stagedAnimators[i];
                if (sibling == null || sibling == animator || !sibling.HasState(0, stateHash))
                    continue;

                PlayState(sibling, stateHash, transitionDuration);
            }
        }

        static void PlayState(Animator animator, int stateHash, float transitionDuration)
        {
            if (animator == null)
                return;

            if (transitionDuration <= 0f)
                animator.Play(stateHash, 0, 0f);
            else
                animator.CrossFadeInFixedTime(stateHash, transitionDuration, 0, 0f);

            animator.Update(0f);
        }

        Renderer[] ResolveFramingRenderers(Renderer[] fallback)
        {
            var tieredVisual = _staged != null ? _staged.GetComponent<TieredBuildingVisual>() : null;
            var preferred = tieredVisual != null ? tieredVisual.GetPortraitFrameRenderers() : null;
            return preferred != null && preferred.Length > 0 ? preferred : fallback;
        }

        void UpgradePortraitMaterialsToUrp()
        {
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var renderer in _staged.GetComponentsInChildren<Renderer>())
            {
                var sharedMaterials = renderer.sharedMaterials;
                var instancedMaterials = (Material[])sharedMaterials.Clone();

                for (int materialIndex = 0; materialIndex < instancedMaterials.Length; materialIndex++)
                {
                    var material = instancedMaterials[materialIndex];
                    if (material == null)
                        continue;

                    bool isUrp = material.shader != null &&
                        material.shader.name.StartsWith("Universal Render Pipeline");
                    if (isUrp || urpLit == null)
                        continue;

                    var upgraded = new Material(urpLit);
                    var source = materialIndex < sharedMaterials.Length ? sharedMaterials[materialIndex] : null;
                    if (source != null)
                    {
                        if (source.HasProperty("_MainTex"))
                        {
                            var texture = source.GetTexture("_MainTex");
                            if (texture != null)
                                upgraded.SetTexture("_BaseMap", texture);
                        }

                        var baseColor = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
                        upgraded.SetColor("_BaseColor", baseColor);
                    }
                    else
                    {
                        upgraded.SetColor("_BaseColor", Color.white);
                    }

                    instancedMaterials[materialIndex] = upgraded;
                }

                renderer.sharedMaterials = instancedMaterials;
            }
        }
    }
}
