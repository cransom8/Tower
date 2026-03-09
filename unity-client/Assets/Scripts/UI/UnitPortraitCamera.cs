// UnitPortraitCamera.cs — Off-screen camera renders unit prefabs to a RenderTexture.
// Place one "PortraitStudio" object in Lobby scene. SetupPortraitStudio creates it.
// LoadoutUI calls ShowUnit(key) to update the preview.

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
        public float FitHeight   = 1.6f;

        GameObject _staged;

        public void ShowUnit(string key)
        {
            Clear();
            if (Registry == null || Cam == null) return;

            var prefab = Registry.GetPrefab(key);
            if (prefab == null) return;

            // Start with registry scale, then auto-fit if enabled
            float scale = Registry.GetScale(key);
            _staged = Instantiate(prefab,
                StagePoint != null ? StagePoint.position : Cam.transform.position + Cam.transform.forward * 2f,
                Quaternion.Euler(0f, RotationY, 0f));
            _staged.transform.localScale = Vector3.one * scale;

            if (AutoFitSize)
            {
                // Calculate combined world-space bounds of all renderers
                var renderers = _staged.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    for (int ri = 1; ri < renderers.Length; ri++)
                        bounds.Encapsulate(renderers[ri].bounds);

                    float maxExtent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    if (maxExtent > 0.001f)
                    {
                        float fitScale = scale * (FitHeight / maxExtent);
                        _staged.transform.localScale = Vector3.one * fitScale;
                    }
                }
            }

            // Apply tint, auto-upgrading non-URP materials (Standard → URP Lit)
            Color tint = Registry.GetTintMine(key);
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
                            upgraded.SetColor("_BaseColor", new Color(
                                baseCol.r * tint.r, baseCol.g * tint.g,
                                baseCol.b * tint.b, baseCol.a * tint.a));
                        }
                        else
                        {
                            upgraded.SetColor("_BaseColor", tint);
                        }
                        instanced[mi] = upgraded;
                    }
                    else
                    {
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
                        else if (mat.HasProperty("_Color")) mat.color = tint;
                    }
                }
                r.materials = instanced;
            }
        }

        public void Clear()
        {
            if (_staged != null) { Destroy(_staged); _staged = null; }
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
            yield return new WaitForEndOfFrame();

            Texture2D result = null;
            if (RenderTex != null)
            {
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
