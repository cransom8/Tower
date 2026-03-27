using UnityEngine;

namespace CastleDefender.Game
{
    [ExecuteAlways]
    public class LaneColorVisual : MonoBehaviour
    {
        [Tooltip("Set >= 0 to force a lane color. Leave -1 to auto-detect from name, parent LanePathMarkers, or nearest lane bridge.")]
        public int LaneIndex = -1;

        [Tooltip("Apply tint to all child renderers and child lights.")]
        public bool IncludeChildren = true;

        [Tooltip("Boost the rim/emission so colored route pieces are easy to read.")]
        public float EmissionStrength = 0.8f;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        public void Apply()
        {
            if (!CanApplyToThisObject())
                return;

            int lane = ResolveLaneIndex(transform, LaneIndex);
            if (lane < 0)
                return;

            Color baseColor = GetLaneColor(lane, 1f);
            Color tintColor = GetLaneColor(lane, 0.72f);
            Color rimColor = Color.Lerp(baseColor, Color.white, 0.28f);

            var renderers = IncludeChildren
                ? GetComponentsInChildren<Renderer>(true)
                : GetComponents<Renderer>();

            foreach (var r in renderers)
            {
                if (r == null) continue;

                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null)
                {
                    bridge.SetBaseColor(tintColor);
                    bridge.SetRimColor(rimColor);
                    continue;
                }

                var materials = Application.isPlaying ? r.materials : r.sharedMaterials;
                foreach (var mat in materials)
                {
                    if (mat == null) continue;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tintColor);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", tintColor);
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", baseColor * EmissionStrength);
                    }
                }
            }

            var lights = IncludeChildren
                ? GetComponentsInChildren<Light>(true)
                : GetComponents<Light>();

            foreach (var light in lights)
            {
                if (light == null) continue;
                light.color = baseColor;
            }
        }

        bool CanApplyToThisObject()
        {
            if (Application.isPlaying)
                return gameObject.scene.IsValid() && gameObject.scene.isLoaded;

#if UNITY_EDITOR
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return false;
#endif

            return gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        public static int ResolveLaneIndex(Transform source, int explicitLane = -1)
        {
            if (explicitLane >= 0 && explicitLane <= 3)
                return explicitLane;

            for (Transform t = source; t != null; t = t.parent)
            {
                var markers = t.GetComponent<LanePathMarkers>();
                if (markers != null)
                    return Mathf.Clamp(markers.LaneIndex, 0, 3);
            }

            string n = source.name.ToLowerInvariant();
            if (n.Contains("red")) return 0;
            if (n.Contains("blue")) return 1;
            if (n.Contains("orange")) return 2;
            if (n.Contains("green")) return 3;

            var laneRoots = new (string name, int lane)[]
            {
                ("Red_Player_Lane", 0),
                ("Blue-Player_Lane", 1),
                ("Orange_Player_Lane", 2),
                ("Green_Player_Lane", 3),
            };

            float bestSq = float.MaxValue;
            int bestLane = -1;
            foreach (var root in laneRoots)
            {
                var laneTf = FindSceneTransform(root.name);
                if (laneTf == null) continue;
                float sq = (laneTf.position - source.position).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestLane = root.lane;
                }
            }
            return bestLane;
        }

        public static Color GetLaneColor(int laneIndex, float alpha)
        {
            return laneIndex switch
            {
                0 => new Color(0.92f, 0.22f, 0.22f, alpha), // red
                1 => new Color(0.18f, 0.55f, 0.95f, alpha), // blue
                2 => new Color(1.00f, 0.58f, 0.10f, alpha), // orange
                3 => new Color(0.22f, 0.82f, 0.38f, alpha), // green
                _ => new Color(1f, 1f, 1f, alpha),
            };
        }

        static Transform FindSceneTransform(string exactName)
        {
            foreach (var tr in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (tr != null && tr.name == exactName)
                    return tr;
            }
            return null;
        }
    }
}
