using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class UnitTeamAccentMarkers : MonoBehaviour
    {
        static readonly string[] LeftUpperArmNames =
        {
            "Bip001 L UpperArm",
            "B_L_UpperArm",
            "L UpperArm",
        };

        static readonly string[] LeftForearmNames =
        {
            "Bip001 L Forearm",
            "B_L_Forearm",
            "L Forearm",
        };

        static readonly string[] RightUpperArmNames =
        {
            "Bip001 R UpperArm",
            "B_R_UpperArm",
            "R UpperArm",
        };

        static readonly string[] RightForearmNames =
        {
            "Bip001 R Forearm",
            "B_R_Forearm",
            "R Forearm",
        };

        static readonly string[] SpineNames =
        {
            "B_Spine2",
            "B_Spine1",
            "Bip001 Spine2",
            "Bip001 Spine1",
            "Bip001 Spine",
            "Spine2",
            "Spine1",
            "Spine",
        };

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        static readonly Dictionary<BattleTeam, Material> AccentMaterials = new();

        Renderer[] _markerRenderers;

        public void Apply(BattleTeam team)
        {
            EnsureMarkers();
            if (_markerRenderers == null || _markerRenderers.Length == 0)
                return;

            Material accentMaterial = ResolveAccentMaterial(team);
            for (int i = 0; i < _markerRenderers.Length; i++)
            {
                var renderer = _markerRenderers[i];
                if (renderer == null)
                    continue;

                renderer.sharedMaterial = accentMaterial;
            }
        }

        void EnsureMarkers()
        {
            if (_markerRenderers != null)
                return;

            var renderers = new List<Renderer>(4);
            if (TryCreateArmBand("Left", LeftUpperArmNames, LeftForearmNames, out var leftRenderer))
                renderers.Add(leftRenderer);
            if (TryCreateArmBand("Right", RightUpperArmNames, RightForearmNames, out var rightRenderer))
                renderers.Add(rightRenderer);
            if (TryCreateChestVest(out var chestRenderer))
                renderers.Add(chestRenderer);
            if (TryCreateBackBanner(out var backRenderer))
                renderers.Add(backRenderer);

            _markerRenderers = renderers.ToArray();
        }

        bool TryCreateArmBand(string sideLabel, string[] upperArmNames, string[] forearmNames, out Renderer markerRenderer)
        {
            markerRenderer = null;

            Transform upperArm = FindDescendantByNames(transform, upperArmNames);
            if (upperArm == null)
                return false;

            Transform forearm = FindDescendantByNames(upperArm, forearmNames);
            Vector3 armDirection = forearm != null
                ? forearm.position - upperArm.position
                : upperArm.TransformDirection(Vector3.down);

            float armLength = armDirection.magnitude;
            if (armLength < 0.01f)
            {
                armDirection = upperArm.TransformDirection(Vector3.down);
                armLength = Mathf.Max(0.14f, armDirection.magnitude);
            }

            Vector3 worldDir = armDirection.normalized;
            Vector3 outward = Vector3.Cross(worldDir, transform.forward);
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.Cross(worldDir, Vector3.up);
            if (outward.sqrMagnitude < 0.0001f)
                outward = Vector3.right;
            outward.Normalize();

            Vector3 worldPosition = upperArm.position
                + worldDir * Mathf.Clamp(armLength * 0.38f, 0.05f, 0.16f)
                + outward * Mathf.Clamp(armLength * 0.05f, 0.012f, 0.03f);
            Quaternion worldRotation = Quaternion.LookRotation(outward, worldDir);

            var band = GameObject.CreatePrimitive(PrimitiveType.Cube);
            band.name = $"__TeamAccent{sideLabel}ArmBand";
            band.layer = gameObject.layer;
            band.transform.SetParent(upperArm, false);
            band.transform.position = worldPosition;
            band.transform.rotation = worldRotation;
            band.transform.localScale = new Vector3(
                Mathf.Clamp(armLength * 0.36f, 0.08f, 0.16f),
                Mathf.Clamp(armLength * 0.26f, 0.05f, 0.10f),
                Mathf.Clamp(armLength * 0.36f, 0.08f, 0.16f));

            var collider = band.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = band.GetComponent<Renderer>();
            if (renderer == null)
                return false;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            markerRenderer = renderer;
            return true;
        }

        bool TryCreateChestVest(out Renderer markerRenderer)
        {
            markerRenderer = null;

            Transform spine = FindDescendantByNames(transform, SpineNames);
            if (spine == null)
                return false;

            Transform leftUpperArm = FindDescendantByNames(transform, LeftUpperArmNames);
            Transform rightUpperArm = FindDescendantByNames(transform, RightUpperArmNames);
            Vector3 shoulderAxis = rightUpperArm != null && leftUpperArm != null
                ? rightUpperArm.position - leftUpperArm.position
                : transform.right;
            float shoulderWidth = shoulderAxis.magnitude;
            if (shoulderWidth < 0.01f)
            {
                shoulderAxis = transform.right;
                shoulderWidth = 0.24f;
            }

            Vector3 right = shoulderAxis.normalized;
            Vector3 up = spine.parent != null
                ? spine.position - spine.parent.position
                : transform.up;
            if (up.sqrMagnitude < 0.0001f)
                up = transform.up;
            up.Normalize();

            Vector3 forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = transform.forward;
            if (Vector3.Dot(forward, transform.forward) < 0f)
                forward = -forward;
            forward.Normalize();

            Vector3 worldPosition = spine.position
                + up * Mathf.Clamp(shoulderWidth * 0.22f, 0.06f, 0.12f)
                + forward * Mathf.Clamp(shoulderWidth * 0.03f, 0.015f, 0.05f);
            Quaternion worldRotation = Quaternion.LookRotation(forward, up);

            var vest = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vest.name = "__TeamAccentVest";
            vest.layer = gameObject.layer;
            vest.transform.SetParent(spine, false);
            vest.transform.position = worldPosition;
            vest.transform.rotation = worldRotation;
            vest.transform.localScale = new Vector3(
                Mathf.Clamp(shoulderWidth * 1.14f, 0.22f, 0.40f),
                Mathf.Clamp(shoulderWidth * 0.18f, 0.028f, 0.06f),
                Mathf.Clamp(shoulderWidth * 0.92f, 0.18f, 0.34f));

            var collider = vest.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = vest.GetComponent<Renderer>();
            if (renderer == null)
                return false;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            markerRenderer = renderer;
            return true;
        }

        bool TryCreateBackBanner(out Renderer markerRenderer)
        {
            markerRenderer = null;

            Transform spine = FindDescendantByNames(transform, SpineNames);
            if (spine == null)
                return false;

            Transform leftUpperArm = FindDescendantByNames(transform, LeftUpperArmNames);
            Transform rightUpperArm = FindDescendantByNames(transform, RightUpperArmNames);
            Vector3 shoulderAxis = rightUpperArm != null && leftUpperArm != null
                ? rightUpperArm.position - leftUpperArm.position
                : transform.right;
            float shoulderWidth = shoulderAxis.magnitude;
            if (shoulderWidth < 0.01f)
                shoulderWidth = 0.24f;

            Vector3 up = spine.parent != null
                ? spine.position - spine.parent.position
                : transform.up;
            if (up.sqrMagnitude < 0.0001f)
                up = transform.up;
            up.Normalize();

            Vector3 forward = transform.forward;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 worldPosition = spine.position
                - forward * Mathf.Clamp(shoulderWidth * 0.12f, 0.04f, 0.09f)
                + up * Mathf.Clamp(shoulderWidth * 0.14f, 0.035f, 0.08f);
            Quaternion worldRotation = Quaternion.LookRotation(forward, up);

            var banner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            banner.name = "__TeamAccentBackBanner";
            banner.layer = gameObject.layer;
            banner.transform.SetParent(spine, false);
            banner.transform.position = worldPosition;
            banner.transform.rotation = worldRotation;
            banner.transform.localScale = new Vector3(
                Mathf.Clamp(shoulderWidth * 1.10f, 0.20f, 0.38f),
                Mathf.Clamp(shoulderWidth * 0.10f, 0.016f, 0.032f),
                Mathf.Clamp(shoulderWidth * 1.05f, 0.18f, 0.34f));

            var collider = banner.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = banner.GetComponent<Renderer>();
            if (renderer == null)
                return false;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            markerRenderer = renderer;
            return true;
        }

        static Material ResolveAccentMaterial(BattleTeam team)
        {
            if (AccentMaterials.TryGetValue(team, out var cached) && cached != null)
                return cached;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = $"RuntimeTeamAccent_{team}"
            };

            Color accent = Color.Lerp(BattleTeamUtility.ToColor(team), Color.white, 0.08f);
            Color emission = Color.Lerp(accent, Color.white, 0.32f) * 0.75f;

            if (material.HasProperty(BaseColorId))
                material.SetColor(BaseColorId, accent);
            if (material.HasProperty(ColorId))
                material.SetColor(ColorId, accent);
            if (material.HasProperty(EmissionColorId))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor(EmissionColorId, emission);
            }
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);

            AccentMaterials[team] = material;
            return material;
        }

        static Transform FindDescendantByNames(Transform root, IReadOnlyList<string> targetNames)
        {
            if (root == null || targetNames == null)
                return null;

            for (int i = 0; i < targetNames.Count; i++)
            {
                Transform found = FindDescendantByName(root, targetNames[i]);
                if (found != null)
                    return found;
            }

            return null;
        }

        static Transform FindDescendantByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
                return null;
            if (string.Equals(root.name, targetName, System.StringComparison.Ordinal))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDescendantByName(root.GetChild(i), targetName);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
