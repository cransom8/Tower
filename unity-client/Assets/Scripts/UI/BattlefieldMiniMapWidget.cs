using CastleDefender.Game;
using CastleDefender.Net;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public sealed class BattlefieldMiniMapWidget : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] DraggableHudPanel panel;
        [SerializeField] RectTransform mapViewportRect;
        [SerializeField] RawImage mapImage;
        [SerializeField] RectTransform focusIndicator;
        [SerializeField] TMP_Text laneLabel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Vector2Int renderTextureSize = new(512, 512);
        [SerializeField] float worldPadding = 8f;
        [SerializeField] float cameraHeight = 140f;

        Camera _miniMapCamera;
        RenderTexture _renderTexture;
        Bounds _battlefieldBounds;
        bool _hasBattlefieldBounds;

        public void Configure(
            DraggableHudPanel panelRef,
            RectTransform viewportRect,
            RawImage mapTarget,
            RectTransform focusMarker,
            TMP_Text laneText,
            TMP_Text statusText)
        {
            panel = panelRef;
            mapViewportRect = viewportRect;
            mapImage = mapTarget;
            focusIndicator = focusMarker;
            laneLabel = laneText;
            statusLabel = statusText;
            ApplyStaticLabels();
        }

        void Awake()
        {
            if (mapImage == null)
                mapImage = GetComponentInChildren<RawImage>(true);

            if (mapViewportRect == null && mapImage != null)
                mapViewportRect = mapImage.rectTransform.parent as RectTransform;
        }

        void OnEnable()
        {
            ApplyStaticLabels();
        }

        void Update()
        {
            ApplyStaticLabels();

            if (!Application.isPlaying)
            {
                SetCameraEnabled(false);
                SetFocusIndicatorVisible(false);
                return;
            }

            if (panel != null && panel.IsCollapsed)
            {
                SetCameraEnabled(false);
                SetFocusIndicatorVisible(false);
                return;
            }

            EnsureBattlefieldBounds();
            EnsureRenderTexture();
            EnsureMiniMapCamera();
            UpdateMiniMapCameraPose();
            UpdateFocusIndicator();
            SetCameraEnabled(true);
        }

        void OnDisable()
        {
            SetCameraEnabled(false);
        }

        void OnDestroy()
        {
            ReleaseResources();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Application.isPlaying || mapViewportRect == null || _miniMapCamera == null)
                return;

            if (panel != null && panel.IsCollapsed)
                return;

            if (!RectTransformUtility.RectangleContainsScreenPoint(mapViewportRect, eventData.position, eventData.pressEventCamera))
                return;

            if (!TryResolveWorldPoint(eventData.position, eventData.pressEventCamera, out var worldPoint))
                return;

            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
                controller.PanTo(worldPoint);
        }

        void ApplyStaticLabels()
        {
            if (laneLabel != null)
                laneLabel.text = ResolveLaneLabel();

            if (statusLabel != null)
            {
                statusLabel.text = Application.isPlaying
                    ? "Live map | Tap to center"
                    : "Live in play mode";
            }
        }

        string ResolveLaneLabel()
        {
            var myLane = SnapshotApplier.Instance?.MyLane;
            if (myLane != null)
            {
                if (!string.IsNullOrWhiteSpace(myLane.branchLabel))
                    return myLane.branchLabel.ToUpperInvariant();

                return $"LANE {myLane.laneIndex + 1}";
            }

            int laneIndex = NetworkManager.Instance != null ? NetworkManager.Instance.MyLaneIndex : -1;
            return laneIndex >= 0 ? $"LANE {laneIndex + 1}" : "BATTLEFIELD";
        }

        void EnsureBattlefieldBounds()
        {
            if (_hasBattlefieldBounds)
                return;

            if (TryBuildFloorBounds(out _battlefieldBounds) || TryBuildBattlefieldBounds(out _battlefieldBounds))
                _hasBattlefieldBounds = true;
        }

        void EnsureRenderTexture()
        {
            if (mapImage == null)
                return;

            int width = Mathf.Max(128, renderTextureSize.x);
            int height = Mathf.Max(128, renderTextureSize.y);

            if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
            {
                if (mapImage.texture != _renderTexture)
                    mapImage.texture = _renderTexture;
                return;
            }

            ReleaseRenderTexture();

            _renderTexture = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32)
            {
                name = "BattlefieldMiniMapRT",
                useMipMap = false,
                autoGenerateMips = false
            };
            _renderTexture.Create();
            mapImage.texture = _renderTexture;
        }

        void EnsureMiniMapCamera()
        {
            if (_miniMapCamera != null)
                return;

            var cameraGo = new GameObject("BattlefieldMiniMapCamera");
            _miniMapCamera = cameraGo.AddComponent<Camera>();
            _miniMapCamera.orthographic = true;
            _miniMapCamera.clearFlags = CameraClearFlags.SolidColor;
            _miniMapCamera.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 1f);
            _miniMapCamera.nearClipPlane = 0.5f;
            _miniMapCamera.farClipPlane = 500f;
            _miniMapCamera.depth = -100f;
            _miniMapCamera.allowHDR = false;
            _miniMapCamera.allowMSAA = false;
            _miniMapCamera.useOcclusionCulling = false;
            _miniMapCamera.enabled = false;
            _miniMapCamera.targetTexture = _renderTexture;

            int uiLayer = LayerMask.NameToLayer("UI");
            _miniMapCamera.cullingMask = uiLayer >= 0
                ? ~(1 << uiLayer)
                : ~0;
        }

        void UpdateMiniMapCameraPose()
        {
            if (_miniMapCamera == null || !_hasBattlefieldBounds)
                return;

            float aspect = _renderTexture != null && _renderTexture.height > 0
                ? _renderTexture.width / (float)_renderTexture.height
                : 1f;
            if (aspect <= 0.001f)
                aspect = 1f;

            float orthoSize = Mathf.Max(
                _battlefieldBounds.extents.z + worldPadding,
                (_battlefieldBounds.extents.x + worldPadding) / aspect);

            var center = _battlefieldBounds.center;
            float yaw = ResolveMiniMapYaw();
            _miniMapCamera.transform.SetPositionAndRotation(
                new Vector3(center.x, Mathf.Max(center.y + cameraHeight, BattlefieldSpaceMapper.TileToWorld(0, 0f, 0f).y + cameraHeight), center.z),
                Quaternion.Euler(90f, yaw, 0f));
            _miniMapCamera.orthographicSize = Mathf.Max(24f, orthoSize);
            _miniMapCamera.targetTexture = _renderTexture;
        }

        float ResolveMiniMapYaw()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null || mainCamera == _miniMapCamera)
                mainCamera = FindFirstObjectByType<Camera>();

            if (mainCamera != null && mainCamera != _miniMapCamera)
                return mainCamera.transform.eulerAngles.y;

            return 0f;
        }

        void UpdateFocusIndicator()
        {
            if (focusIndicator == null || mapViewportRect == null || _miniMapCamera == null)
            {
                SetFocusIndicatorVisible(false);
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null || mainCamera == _miniMapCamera)
                mainCamera = FindFirstObjectByType<Camera>();
            if (mainCamera == null || mainCamera == _miniMapCamera)
            {
                SetFocusIndicatorVisible(false);
                return;
            }

            if (!TryResolveMainCameraFocus(mainCamera, out var focusWorld))
            {
                SetFocusIndicatorVisible(false);
                return;
            }

            Vector3 viewport = _miniMapCamera.WorldToViewportPoint(focusWorld);
            if (viewport.z < 0f)
            {
                SetFocusIndicatorVisible(false);
                return;
            }

            float width = mapViewportRect.rect.width;
            float height = mapViewportRect.rect.height;
            focusIndicator.anchoredPosition = new Vector2(
                (Mathf.Clamp01(viewport.x) - 0.5f) * width,
                (Mathf.Clamp01(viewport.y) - 0.5f) * height);
            SetFocusIndicatorVisible(true);
        }

        bool TryResolveMainCameraFocus(Camera mainCamera, out Vector3 focusWorld)
        {
            float focusPlaneY = BattlefieldSpaceMapper.TileToWorld(0, 0f, 0f).y;
            var controller = FindFirstObjectByType<global::CameraController>();
            if (controller != null)
                focusPlaneY = controller.FocusPlaneY;

            var focusPlane = new Plane(Vector3.up, new Vector3(0f, focusPlaneY, 0f));
            var focusRay = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (focusPlane.Raycast(focusRay, out float distance))
            {
                focusWorld = focusRay.GetPoint(distance);
                return true;
            }

            focusWorld = Vector3.zero;
            return false;
        }

        bool TryResolveWorldPoint(Vector2 screenPoint, Camera eventCamera, out Vector3 worldPoint)
        {
            if (mapViewportRect == null || _miniMapCamera == null)
            {
                worldPoint = Vector3.zero;
                return false;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewportRect, screenPoint, eventCamera, out var localPoint))
            {
                worldPoint = Vector3.zero;
                return false;
            }

            Rect rect = mapViewportRect.rect;
            float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
            var worldPlane = new Plane(Vector3.up, new Vector3(0f, BattlefieldSpaceMapper.TileToWorld(0, 0f, 0f).y, 0f));
            var ray = _miniMapCamera.ViewportPointToRay(new Vector3(u, v, 0f));
            if (worldPlane.Raycast(ray, out float distance))
            {
                worldPoint = ray.GetPoint(distance);
                return true;
            }

            worldPoint = Vector3.zero;
            return false;
        }

        void SetCameraEnabled(bool enabled)
        {
            if (_miniMapCamera != null)
                _miniMapCamera.enabled = enabled;
        }

        void SetFocusIndicatorVisible(bool visible)
        {
            if (focusIndicator != null)
                focusIndicator.gameObject.SetActive(visible);
        }

        void ReleaseResources()
        {
            if (_miniMapCamera != null)
            {
                if (Application.isPlaying)
                    Destroy(_miniMapCamera.gameObject);
                else
                    DestroyImmediate(_miniMapCamera.gameObject);
                _miniMapCamera = null;
            }

            ReleaseRenderTexture();
        }

        void ReleaseRenderTexture()
        {
            if (mapImage != null && mapImage.texture == _renderTexture)
                mapImage.texture = null;

            if (_renderTexture == null)
                return;

            _renderTexture.Release();
            if (Application.isPlaying)
                Destroy(_renderTexture);
            else
                DestroyImmediate(_renderTexture);
            _renderTexture = null;
        }

        static bool TryBuildBattlefieldBounds(out Bounds bounds)
        {
            bool hasPoint = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            void Encapsulate(Vector3 point)
            {
                if (!hasPoint)
                {
                    min = point;
                    max = point;
                    hasPoint = true;
                    return;
                }

                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            for (int laneIndex = 0; laneIndex < BattlefieldSpaceMapper.LaneCount; laneIndex++)
            {
                Encapsulate(BattlefieldSpaceMapper.TileToWorld(laneIndex, 0f, 0f));
                Encapsulate(BattlefieldSpaceMapper.TileToWorld(laneIndex, BattlefieldSpaceMapper.LaneCols - 1, 0f));
                Encapsulate(BattlefieldSpaceMapper.TileToWorld(laneIndex, 0f, BattlefieldSpaceMapper.LaneRows - 1));
                Encapsulate(BattlefieldSpaceMapper.TileToWorld(laneIndex, BattlefieldSpaceMapper.LaneCols - 1, BattlefieldSpaceMapper.LaneRows - 1));

                var waypoints = BattlefieldSpaceMapper.GetLanePathWaypoints(laneIndex);
                for (int i = 0; i < waypoints.Length; i++)
                    Encapsulate(waypoints[i]);
            }

            if (!hasPoint)
            {
                bounds = default;
                return false;
            }

            bounds = new Bounds((min + max) * 0.5f, max - min);
            bounds.Expand(new Vector3(10f, 20f, 10f));
            return true;
        }

        static bool TryBuildFloorBounds(out Bounds bounds)
        {
            var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            bool hasBounds = false;
            bounds = default;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled)
                    continue;

                string objectName = renderer.gameObject.name;
                if (!string.Equals(objectName, "Map_Floor", System.StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(objectName, "Floor", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
                return false;

            bounds.Expand(new Vector3(8f, 20f, 8f));
            return true;
        }
    }
}
