// UnitPortraitCamera.cs — Off-screen camera renders unit prefabs to a RenderTexture.
// Place one "PortraitStudio" object in Lobby scene. SetupPortraitStudio creates it.
// Match UI flows call ShowUnit(key) to update portrait captures.

using System;
using System.Collections;
using UnityEngine;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    public class UnitPortraitCamera : MonoBehaviour
    {
        [Header("References (wired by SetupPortraitStudio)")]
        public Camera             Cam;
        public RenderTexture      RenderTex;
        public Transform          StagePoint;
        public UnitPrefabRegistry Registry;

        [Header("Pose")]
        [Tooltip("Y rotation applied to staged unit. 0 = facing camera directly.")]
        public float RotationY = 0f;

        [Header("Auto-fit")]
        [Tooltip("Automatically scale each unit so it fills FitHeight world units in the frame.")]
        public bool  AutoFitSize = true;
        [Tooltip("Target height in world units that every unit is scaled to fill.")]
        public float FitHeight   = 2.25f;
        [Tooltip("How much of the frame height the unit should occupy. Higher = closer portrait.")]
        [Range(0.5f, 0.95f)] public float FrameFill = 0.82f;
        [Tooltip("Bias framing upward so the portrait feels like a closer character shot.")]
        [Range(0f, 1f)] public float VerticalFocus = 0.62f;

        GameObject _staged;

        public void ShowUnit(string key)
        {
            Clear();
            if (Registry == null || Cam == null) return;
            ClearStageChildren();

            var prefab = Registry.GetPrefab(key);
            if (prefab == null) return;

            // Start with registry scale, then auto-fit if enabled
            float scale = Registry.GetScale(key);
            _staged = Instantiate(prefab,
                StagePoint != null ? StagePoint.position : Cam.transform.position + Cam.transform.forward * 2f,
                Quaternion.Euler(0f, RotationY, 0f));
            if (StagePoint != null)
                _staged.transform.SetParent(StagePoint, true);
            SetLayerRecursively(_staged, RuntimePortraitStudio.PortraitLayer);
            _staged.transform.localScale = Vector3.one * scale;

            var renderers = _staged.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            if (AutoFitSize)
            {
                var bounds = CalculateBounds(renderers);
                float height = bounds.size.y;
                if (height > 0.001f)
                {
                    float fitScale = scale * (FitHeight / height);
                    _staged.transform.localScale = Vector3.one * fitScale;
                    renderers = _staged.GetComponentsInChildren<Renderer>();
                }
            }

            FrameStagedUnit(CalculateBounds(renderers));

            // Apply tint, auto-upgrading non-URP materials (Standard → URP Lit)
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");

            foreach (var r in _staged.GetComponentsInChildren<Renderer>())
            {
                var shared   = r.sharedMaterials;
                var instanced = r.materials;        // creates per-instance copies

                for (int mi = 0; mi < instanced.Length; mi++)
                {
                    var mat = instanced[mi];
                    if (mat == null) continue;

                    bool isURP = mat.shader != null &&
                                 mat.shader.name.StartsWith("Universal Render Pipeline");

                    if (!isURP && urpLit != null)
                    {
                        var upgraded = new Material(urpLit);
                        var orig = mi < shared.Length ? shared[mi] : null;
                        if (orig != null)
                        {
                            if (orig.HasProperty("_MainTex"))
                            {
                                var tex = orig.GetTexture("_MainTex");
                                if (tex != null) upgraded.SetTexture("_BaseMap", tex);
                            }
                            var baseCol = orig.HasProperty("_Color") ? orig.GetColor("_Color") : Color.white;
                            upgraded.SetColor("_BaseColor", baseCol);
                        }
                        else
                        {
                            upgraded.SetColor("_BaseColor", Color.white);
                        }
                        instanced[mi] = upgraded;
                    }
                    else
                    {
                        // Keep authored material colors for portraits.
                    }
                }
                r.materials = instanced;
            }
        }

        public void Clear()
        {
            if (_staged == null) return;

            _staged.SetActive(false);
            DestroyImmediate(_staged);
            _staged = null;
        }

        void ClearStageChildren()
        {
            if (StagePoint == null) return;
            for (int i = StagePoint.childCount - 1; i >= 0; i--)
            {
                var child = StagePoint.GetChild(i);
                if (_staged != null && child.gameObject == _staged) continue;
                DestroyImmediate(child.gameObject);
            }
        }

        static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
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
            if (_staged == null || Cam == null) return;

            float height = Mathf.Max(0.1f, bounds.size.y);
            float width = Mathf.Max(0.1f, bounds.size.x);
            Vector3 stageOrigin = StagePoint != null ? StagePoint.position : Vector3.zero;

            Vector3 focusPoint = new Vector3(
                bounds.center.x,
                bounds.min.y + (height * VerticalFocus),
                bounds.center.z);

            _staged.transform.position += stageOrigin - focusPoint;

            float halfHeight = height * 0.5f;
            float halfWidth = width * 0.5f;
            float verticalFov = Cam.fieldOfView * Mathf.Deg2Rad;
            float horizontalFov = 2f * Mathf.Atan(Mathf.Tan(verticalFov * 0.5f) * Cam.aspect);
            float fill = Mathf.Max(0.01f, FrameFill);

            float distForHeight = halfHeight / (Mathf.Tan(verticalFov * 0.5f) * fill);
            float distForWidth = halfWidth / (Mathf.Tan(horizontalFov * 0.5f) * fill);
            float camDistance = Mathf.Max(distForHeight, distForWidth, 1.1f);

            Cam.transform.position = stageOrigin + new Vector3(0f, 0.05f, -camDistance);
            Cam.transform.rotation = Quaternion.identity;
            Cam.transform.LookAt(stageOrigin + new Vector3(0f, height * 0.06f, 0f));
        }

        /// <summary>
        /// Renders the unit into the RenderTexture for one frame, captures it to a Texture2D,
        /// then calls callback. Runs as a coroutine so must be called on an active MonoBehaviour.
        /// </summary>
        public void StartIconCapture(string key, Action<Texture2D> callback)
            => StartCoroutine(CaptureCoroutine(key, callback));

        IEnumerator CaptureCoroutine(string key, Action<Texture2D> callback)
        {
            ShowUnit(key);
            yield return null;

            Texture2D result = null;
            if (Cam != null && RenderTex != null)
            {
                Cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = RenderTex;
                result = new Texture2D(RenderTex.width, RenderTex.height, TextureFormat.RGBA32, false);
                result.ReadPixels(new Rect(0, 0, RenderTex.width, RenderTex.height), 0, 0);
                result.Apply();
                RenderTexture.active = prev;
            }
            Clear();
            callback(result);
        }

        void OnDestroy() => Clear();
    }
}
