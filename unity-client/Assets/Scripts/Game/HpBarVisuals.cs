using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.Game
{
    static class HpBarVisuals
    {
        static readonly Color FillColor = new(0.18f, 0.94f, 0.34f, 0.96f);
        static readonly Color FrameColor = new(0.88f, 0.96f, 1f, 0.72f);
        const string FillVisualRootName = "FillVisual";

        public static void EnsureStyled(Transform barRoot)
        {
            if (barRoot == null) return;

            var background = FindChildRecursive(barRoot, "Background");
            if (background != null)
            {
                var renderer = background.GetComponent<Renderer>();
                if (renderer != null) renderer.enabled = false;
            }

            var fill = FindChildRecursive(barRoot, "Fill");
            if (fill != null) EnsureFillVisual(fill);

            EnsureFrame(barRoot);
        }

        public static void ApplyFill(Transform fill, Image image, float hp01)
        {
            hp01 = Mathf.Clamp01(hp01);

            if (image != null)
            {
                image.fillAmount = hp01;
                image.color = FillColor;
            }

            if (fill != null)
                TintFillVisual(fill, FillColor);
        }

        static void EnsureFrame(Transform barRoot)
        {
            var frameRoot = FindChildRecursive(barRoot, "HpFrame");
            if (frameRoot == null)
            {
                var go = new GameObject("HpFrame");
                go.transform.SetParent(barRoot, false);
                go.transform.localPosition = new Vector3(0f, 0f, -0.012f);
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                frameRoot = go.transform;

                CreateFramePiece(frameRoot, "Top", new Vector3(0f, 0.05f, 0f), new Vector3(1.04f, 0.012f, 0.012f), false);
                CreateFramePiece(frameRoot, "Bottom", new Vector3(0f, -0.05f, 0f), new Vector3(1.04f, 0.012f, 0.012f), false);
                CreateFramePiece(frameRoot, "Left", new Vector3(-0.52f, 0f, 0f), new Vector3(0.02f, 0.11f, 0.012f), true);
                CreateFramePiece(frameRoot, "Right", new Vector3(0.52f, 0f, 0f), new Vector3(0.02f, 0.11f, 0.012f), true);
            }

            for (int i = 0; i < frameRoot.childCount; i++)
                SetMeshColor(frameRoot.GetChild(i), FrameColor);
        }

        static void EnsureFillVisual(Transform fillRoot)
        {
            var renderer = fillRoot.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;

            var visualRoot = FindChildRecursive(fillRoot, FillVisualRootName);
            if (visualRoot == null)
            {
                var go = new GameObject(FillVisualRootName);
                go.transform.SetParent(fillRoot, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                visualRoot = go.transform;

                CreateFillPiece(visualRoot, "Core", Vector3.zero, new Vector3(1f, 1f, 1f), false);
                CreateFillPiece(visualRoot, "LeftCap", new Vector3(-0.5f, 0f, 0f), new Vector3(0.24f, 1f, 1f), true);
                CreateFillPiece(visualRoot, "RightCap", new Vector3(0.5f, 0f, 0f), new Vector3(0.24f, 1f, 1f), true);
            }

            TintFillVisual(fillRoot, FillColor);
        }

        static void CreateFillPiece(Transform parent, string name, Vector3 localPosition, Vector3 localScale, bool rounded)
        {
            var primitive = GameObject.CreatePrimitive(rounded ? PrimitiveType.Sphere : PrimitiveType.Cube);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.identity;
            primitive.transform.localScale = localScale;

            var collider = primitive.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            SetMeshColor(primitive.transform, FillColor);
        }

        static void TintFillVisual(Transform fillRoot, Color color)
        {
            var visualRoot = FindChildRecursive(fillRoot, FillVisualRootName);
            if (visualRoot == null)
            {
                SetMeshColor(fillRoot, color);
                return;
            }

            for (int i = 0; i < visualRoot.childCount; i++)
                SetMeshColor(visualRoot.GetChild(i), color);
        }

        static void CreateFramePiece(Transform parent, string name, Vector3 localPosition, Vector3 localScale, bool rounded)
        {
            var primitive = GameObject.CreatePrimitive(rounded ? PrimitiveType.Sphere : PrimitiveType.Cube);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.identity;
            primitive.transform.localScale = localScale;

            var collider = primitive.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);

            SetMeshColor(primitive.transform, FrameColor);
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName)) return null;
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }

        static void SetMeshColor(Transform target, Color color)
        {
            if (target == null) return;

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;

            var material = renderer.material;
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
                if (shader == null) return;
                material = new Material(shader);
                renderer.material = material;
            }

            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
