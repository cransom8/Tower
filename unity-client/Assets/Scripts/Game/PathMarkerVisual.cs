using UnityEngine;

namespace CastleDefender.Game
{
    /// <summary>
    /// Legacy helper arrow for authored lane path markers.
    /// The route anchors still exist, but these old generated meshes stay hidden
    /// unless they are explicitly re-enabled for debugging.
    /// </summary>
    [ExecuteAlways]
    public class PathMarkerVisual : MonoBehaviour
    {
        const string VisualRootName = "__PathMarkerVisual";

        [Header("Shape")]
        public float BodyLength = 1.6f;
        public float BodyWidth = 0.28f;
        public float HeadLength = 0.85f;
        public float HeadWidth = 0.75f;
        public float Height = 0.12f;
        public float HoverHeight = 0.08f;

        [Header("Look")]
        public bool UseLaneColor = true;
        public Color BaseColor = new(0.25f, 0.95f, 1f, 0.34f);
        public Color EmissionColor = new(0.10f, 0.70f, 1f, 1f);
        public float EmissionStrength = 1.75f;
        public bool AddGlowLight = true;
        public float GlowRange = 3.5f;
        public float GlowIntensity = 1.4f;
        [Tooltip("Disabled by default so retired route helper arrows do not render in the live map.")]
        public bool ShowLegacyVisual = false;

        Transform _visualRoot;

        void OnEnable() => ScheduleRebuild();
        void OnDisable() => ClearVisualArtifacts();
        void OnDestroy() => ClearVisualArtifacts();
        void OnValidate()
        {
            ScheduleRebuild();
        }

        void ScheduleRebuild()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall -= RebuildDeferred;
                UnityEditor.EditorApplication.delayCall += RebuildDeferred;
                return;
            }
#endif
            Rebuild();
        }

#if UNITY_EDITOR
        void RebuildDeferred()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildDeferred;
            if (this == null)
                return;

            Rebuild();
        }
#endif

        void Rebuild()
        {
            if (!ShowLegacyVisual)
            {
                ClearVisualArtifacts();
                return;
            }

            if (!CanMutateVisuals())
                return;

            RefreshLaneColors();
            EnsureRoot();
            ClearChildren();

            CreateBox("Body", new Vector3(0f, HoverHeight, 0.25f), new Vector3(BodyWidth, Height, BodyLength), false);
            CreateBox("Head", new Vector3(0f, HoverHeight, BodyLength * 0.5f + HeadLength * 0.25f), new Vector3(HeadWidth, Height, HeadLength * 0.55f), true);
            CreateBox("HeadLeft", new Vector3(-HeadWidth * 0.22f, HoverHeight, BodyLength * 0.5f + HeadLength * 0.08f), new Vector3(HeadWidth * 0.55f, Height, HeadLength * 0.38f), true, new Vector3(0f, -35f, 0f));
            CreateBox("HeadRight", new Vector3(HeadWidth * 0.22f, HoverHeight, BodyLength * 0.5f + HeadLength * 0.08f), new Vector3(HeadWidth * 0.55f, Height, HeadLength * 0.38f), true, new Vector3(0f, 35f, 0f));

            if (AddGlowLight)
                EnsureLight();
        }

        bool CanMutateVisuals()
        {
            if (Application.isPlaying)
                return gameObject.scene.IsValid() && gameObject.scene.isLoaded;

#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return false;
#endif

            return gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        void RefreshLaneColors()
        {
            if (!UseLaneColor)
                return;

            int lane = LaneColorVisual.ResolveLaneIndex(transform);
            if (lane < 0)
                return;

            BaseColor = LaneColorVisual.GetLaneColor(lane, 0.34f);
            EmissionColor = LaneColorVisual.GetLaneColor(lane, 1f);
        }

        void EnsureRoot()
        {
            var existing = transform.Find(VisualRootName);
            if (existing != null)
            {
                _visualRoot = existing;
                return;
            }

            var root = new GameObject(VisualRootName);
            root.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            _visualRoot = root.transform;
            _visualRoot.SetParent(transform, false);
        }

        void ClearChildren()
        {
            if (_visualRoot == null)
                return;

            for (int i = _visualRoot.childCount - 1; i >= 0; i--)
            {
                var child = _visualRoot.GetChild(i);
                if (child == null) continue;

                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
        }

        void CreateBox(string name, Vector3 localPos, Vector3 localScale, bool emissive, Vector3? localEuler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
            go.transform.SetParent(_visualRoot, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.transform.localRotation = Quaternion.Euler(localEuler ?? Vector3.zero);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = CreateMaterial(emissive);
        }

        Material CreateMaterial(bool emissive)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            var mat = new Material(shader);
            mat.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;

            if (mat.HasProperty("_Color")) mat.SetColor("_Color", BaseColor);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", BaseColor);

            // Standard transparent setup.
            if (mat.HasProperty("_Mode"))
            {
                mat.SetFloat("_Mode", 3f);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);

            if (emissive && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", EmissionColor * EmissionStrength);
            }

            return mat;
        }

        void EnsureLight()
        {
            var existing = _visualRoot.Find("GlowLight");
            Light light;
            if (existing == null)
            {
                var lightGo = new GameObject("GlowLight");
                lightGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.NotEditable;
                lightGo.transform.SetParent(_visualRoot, false);
                light = lightGo.AddComponent<Light>();
            }
            else
            {
                light = existing.GetComponent<Light>();
                if (light == null) light = existing.gameObject.AddComponent<Light>();
            }

            light.type = LightType.Point;
            light.range = GlowRange;
            light.intensity = GlowIntensity;
            light.color = EmissionColor;
            light.shadows = LightShadows.None;
            light.transform.localPosition = new Vector3(0f, 0.35f, 0.2f);
        }

        void ClearVisualArtifacts()
        {
            if (transform == null)
                return;

            var existing = _visualRoot != null ? _visualRoot : transform.Find(VisualRootName);
            if (existing == null)
            {
                _visualRoot = null;
                return;
            }

            _visualRoot = null;
            if (Application.isPlaying)
                Destroy(existing.gameObject);
            else
                DestroyImmediate(existing.gameObject);
        }

    }
}
