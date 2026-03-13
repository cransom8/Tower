using UnityEngine;
using CastleDefender.Game;

namespace CastleDefender.UI
{
    static class RuntimePortraitStudio
    {
        public static UnitPrefabRegistry ResolveRegistry(UnitPrefabRegistry preferred = null)
        {
            if (preferred != null) return preferred;

            var laneRenderer = Object.FindFirstObjectByType<LaneRenderer>();
            if (laneRenderer != null && laneRenderer.Registry != null) return laneRenderer.Registry;

            var tileGrid = Object.FindFirstObjectByType<TileGrid>();
            if (tileGrid != null && tileGrid.Registry != null) return tileGrid.Registry;

            return null;
        }

        public static UnitPortraitCamera Create(string rootName, UnitPrefabRegistry registry, out GameObject root, out RenderTexture renderTexture)
        {
            root = new GameObject(rootName);
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.position = new Vector3(0f, -9999f, 0f);

            var stagePoint = new GameObject("StagePoint").transform;
            stagePoint.SetParent(root.transform, false);
            stagePoint.localPosition = Vector3.zero;

            var camGO = new GameObject("Camera");
            camGO.transform.SetParent(root.transform, false);
            camGO.transform.localPosition = new Vector3(0f, 1.2f, -4f);
            camGO.transform.localRotation = Quaternion.identity;

            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.11f, 0.14f, 0.20f, 1f);
            cam.cullingMask = -1;
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 30f;
            cam.depth = -10f;
            cam.enabled = true;

            var lightGO = new GameObject("KeyLight");
            lightGO.transform.SetParent(root.transform, false);
            lightGO.transform.localPosition = new Vector3(1.5f, 3f, -2f);
            lightGO.transform.localRotation = Quaternion.Euler(42f, -28f, 0f);
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.35f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.shadows = LightShadows.None;

            var fillLightGO = new GameObject("FillLight");
            fillLightGO.transform.SetParent(root.transform, false);
            fillLightGO.transform.localPosition = new Vector3(-1.2f, 2.2f, -1.5f);
            fillLightGO.transform.localRotation = Quaternion.Euler(34f, 30f, 0f);
            var fillLight = fillLightGO.AddComponent<Light>();
            fillLight.type = LightType.Directional;
            fillLight.intensity = 0.85f;
            fillLight.color = new Color(0.72f, 0.82f, 1f);
            fillLight.shadows = LightShadows.None;

            renderTexture = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 2;
            renderTexture.Create();
            cam.targetTexture = renderTexture;

            var portraitCam = root.AddComponent<UnitPortraitCamera>();
            portraitCam.Cam = cam;
            portraitCam.RenderTex = renderTexture;
            portraitCam.StagePoint = stagePoint;
            portraitCam.Registry = registry;
            portraitCam.RotationY = 180f;
            portraitCam.FrameFill = 0.86f;
            portraitCam.VerticalFocus = 0.64f;
            return portraitCam;
        }
    }
}
