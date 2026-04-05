using CastleDefender.Game;
using CastleDefender.Net;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CastleDefender.UI
{
    [DisallowMultipleComponent]
    public sealed class BattlefieldMiniMapWidget : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] DraggableHudPanel panel;
        [SerializeField] RectTransform mapViewportRect;
        [SerializeField] RectTransform unitDotRoot;
        [SerializeField] RawImage mapImage;
        [SerializeField] RectTransform focusIndicator;
        [SerializeField] TMP_Text laneLabel;
        [SerializeField] TMP_Text statusLabel;
        [SerializeField] Vector2Int renderTextureSize = new(512, 512);
        [SerializeField] float worldPadding = 8f;
        [SerializeField] float cameraHeight = 140f;
        [SerializeField] Vector2 unitDotSize = new(6f, 6f);
        [SerializeField] Vector2 townCoreStarSize = new(18f, 18f);
        [SerializeField] float townCoreStarFontSize = 18f;

        static readonly string[] MinimapHiddenRootNames =
        {
            "RoadLanterns",
            "RoadLightPools",
            "FortressLanterns",
            "FortressLightPools",
        };
        static readonly Color DefaultMarkerColor = Color.white;
        static readonly Color DungeonMarkerColor = new(0.74f, 0.34f, 0.92f, 1f);
        static Sprite s_unitDotSprite;

        sealed class HiddenRendererState
        {
            public Renderer Renderer;
            public bool WasEnabled;
        }

        sealed class HiddenLightState
        {
            public Light Light;
            public bool WasEnabled;
        }

        Camera _miniMapCamera;
        RenderTexture _renderTexture;
        Bounds _battlefieldBounds;
        bool _hasBattlefieldBounds;
        bool _renderSuppressionActive;
        bool _hiddenVisualsCached;
        readonly List<Image> _unitDots = new();
        readonly List<TextMeshProUGUI> _townCoreStars = new();
        readonly List<HiddenRendererState> _hiddenRenderers = new();
        readonly List<HiddenLightState> _hiddenLights = new();

        public void Configure(
            DraggableHudPanel panelRef,
            RectTransform viewportRect,
            RectTransform dotRoot,
            RawImage mapTarget,
            RectTransform focusMarker,
            TMP_Text laneText,
            TMP_Text statusText)
        {
            panel = panelRef;
            mapViewportRect = viewportRect;
            unitDotRoot = dotRoot;
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
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }

        void Update()
        {
            ApplyStaticLabels();

            if (!Application.isPlaying)
            {
                SetCameraEnabled(false);
                SetFocusIndicatorVisible(false);
                HideUnitDots();
                HideTownCoreStars();
                return;
            }

            if (panel != null && panel.IsCollapsed)
            {
                SetCameraEnabled(false);
                SetFocusIndicatorVisible(false);
                HideUnitDots();
                HideTownCoreStars();
                return;
            }

            EnsureBattlefieldBounds();
            EnsureRenderTexture();
            EnsureMiniMapCamera();
            UpdateMiniMapCameraPose();
            UpdateUnitDots();
            UpdateTownCoreStars();
            UpdateFocusIndicator();
            SetCameraEnabled(true);
        }

        void OnDisable()
        {
            SetCameraEnabled(false);
            HideUnitDots();
            HideTownCoreStars();
            RestoreMinimapHiddenVisuals();
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
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

        void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _miniMapCamera)
                return;

            SuppressMinimapHiddenVisuals();
        }

        void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != _miniMapCamera)
                return;

            RestoreMinimapHiddenVisuals();
        }

        void SuppressMinimapHiddenVisuals()
        {
            if (_renderSuppressionActive)
                return;

            CacheMinimapHiddenVisuals();
            _renderSuppressionActive = true;

            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                var state = _hiddenRenderers[i];
                if (state.Renderer != null)
                    state.Renderer.enabled = false;
            }

            for (int i = 0; i < _hiddenLights.Count; i++)
            {
                var state = _hiddenLights[i];
                if (state.Light != null)
                    state.Light.enabled = false;
            }
        }

        void RestoreMinimapHiddenVisuals()
        {
            if (!_renderSuppressionActive)
                return;

            for (int i = 0; i < _hiddenRenderers.Count; i++)
            {
                var state = _hiddenRenderers[i];
                if (state.Renderer != null)
                    state.Renderer.enabled = state.WasEnabled;
            }

            for (int i = 0; i < _hiddenLights.Count; i++)
            {
                var state = _hiddenLights[i];
                if (state.Light != null)
                    state.Light.enabled = state.WasEnabled;
            }

            _renderSuppressionActive = false;
        }

        void CacheMinimapHiddenVisuals()
        {
            if (_hiddenVisualsCached)
                return;

            _hiddenVisualsCached = true;
            _hiddenRenderers.Clear();
            _hiddenLights.Clear();

            var roots = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < roots.Length; i++)
            {
                var root = roots[i];
                if (root == null || !ShouldHideForMinimap(root.name))
                    continue;

                var renderers = root.GetComponentsInChildren<Renderer>(true);
                for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null)
                        continue;

                    _hiddenRenderers.Add(new HiddenRendererState
                    {
                        Renderer = renderer,
                        WasEnabled = renderer.enabled
                    });
                }

                var lights = root.GetComponentsInChildren<Light>(true);
                for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
                {
                    var light = lights[lightIndex];
                    if (light == null)
                        continue;

                    _hiddenLights.Add(new HiddenLightState
                    {
                        Light = light,
                        WasEnabled = light.enabled
                    });
                }
            }
        }

        static bool ShouldHideForMinimap(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            for (int i = 0; i < MinimapHiddenRootNames.Length; i++)
            {
                if (string.Equals(name, MinimapHiddenRootNames[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        void UpdateUnitDots()
        {
            if (unitDotRoot == null || mapViewportRect == null || _miniMapCamera == null)
            {
                HideUnitDots();
                return;
            }

            var snap = SnapshotApplier.Instance?.LatestML;
            if (snap?.lanes == null || snap.lanes.Length == 0)
            {
                HideUnitDots();
                return;
            }

            int dotIndex = 0;
            float width = mapViewportRect.rect.width;
            float height = mapViewportRect.rect.height;
            float worldY = _battlefieldBounds.center.y;

            for (int laneIndex = 0; laneIndex < snap.lanes.Length; laneIndex++)
            {
                var lane = snap.lanes[laneIndex];
                if (lane?.units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.units.Length; unitIndex++)
                {
                    var unit = lane.units[unitIndex];
                    if (!ShouldRenderUnitDot(unit))
                        continue;

                    if (!TryResolveUnitWorldPosition(unit, lane, worldY, out var worldPos))
                        continue;

                    if (!TryProjectWorldPointToMiniMap(worldPos, width, height, out var anchoredPosition))
                        continue;

                    var dot = GetOrCreateUnitDot(dotIndex++);
                    var dotRect = dot.rectTransform;
                    dotRect.anchoredPosition = anchoredPosition;
                    dot.color = ResolveUnitDotColor(unit);
                    dot.gameObject.SetActive(true);
                }
            }

            for (int i = dotIndex; i < _unitDots.Count; i++)
                _unitDots[i].gameObject.SetActive(false);
        }

        void UpdateTownCoreStars()
        {
            if (unitDotRoot == null || mapViewportRect == null || _miniMapCamera == null)
            {
                HideTownCoreStars();
                return;
            }

            var snapshotApplier = SnapshotApplier.Instance;
            var snap = snapshotApplier?.LatestML;
            if (snap?.lanes == null || snap.lanes.Length == 0)
            {
                HideTownCoreStars();
                return;
            }

            int starIndex = 0;
            float width = mapViewportRect.rect.width;
            float height = mapViewportRect.rect.height;
            float worldY = _battlefieldBounds.center.y;

            for (int laneArrayIndex = 0; laneArrayIndex < snap.lanes.Length; laneArrayIndex++)
            {
                var lane = snap.lanes[laneArrayIndex];
                if (lane == null)
                    continue;

                int laneIndex = BattlefieldSpaceMapper.IsValidLaneIndex(lane.laneIndex)
                    ? lane.laneIndex
                    : laneArrayIndex;
                var townCorePad = snapshotApplier.GetTownCorePad(laneIndex);
                if (!ShouldRenderTownCoreStar(townCorePad))
                    continue;

                if (!TryResolveTownCoreWorldPosition(townCorePad, lane, laneIndex, worldY, out var worldPos))
                    continue;

                if (!TryProjectWorldPointToMiniMap(worldPos, width, height, out var anchoredPosition))
                    continue;

                var star = GetOrCreateTownCoreStar(starIndex++);
                var starRect = star.rectTransform;
                starRect.anchoredPosition = anchoredPosition;
                star.color = ResolveTownCoreStarColor(townCorePad);
                star.gameObject.SetActive(true);
            }

            for (int i = starIndex; i < _townCoreStars.Count; i++)
                _townCoreStars[i].gameObject.SetActive(false);
        }

        static bool ShouldRenderUnitDot(MLUnit unit)
        {
            return unit != null
                && unit.hp > 0.01f
                && !string.IsNullOrWhiteSpace(unit.id ?? unit.unitId ?? unit.type);
        }

        static bool ShouldRenderTownCoreStar(MLFortressPad townCorePad)
        {
            return townCorePad != null
                && townCorePad.hp > 0.01f
                && !townCorePad.isDestroyed
                && string.Equals(townCorePad.buildingType, "town_core", System.StringComparison.OrdinalIgnoreCase);
        }

        bool TryResolveUnitWorldPosition(MLUnit unit, MLLaneSnap lane, float worldY, out Vector3 worldPos)
        {
            if (unit != null && float.IsFinite(unit.routeWorldX) && float.IsFinite(unit.routeWorldY))
            {
                worldPos = new Vector3(unit.routeWorldX, worldY, unit.routeWorldY);
                return true;
            }

            int spatialLane = ResolveSpatialLane(unit, lane);
            if (BattlefieldSpaceMapper.IsValidLaneIndex(spatialLane))
            {
                if (float.IsFinite(unit.gridX) && float.IsFinite(unit.gridY))
                {
                    worldPos = BattlefieldSpaceMapper.TileToWorld(spatialLane, unit.gridX, unit.gridY);
                    return true;
                }

                if (float.IsFinite(unit.anchorCenterX) && float.IsFinite(unit.anchorCenterY))
                {
                    worldPos = BattlefieldSpaceMapper.TileToWorld(spatialLane, unit.anchorCenterX, unit.anchorCenterY);
                    return true;
                }

                if (float.IsFinite(unit.currentWaypointTargetX) && float.IsFinite(unit.currentWaypointTargetY))
                {
                    worldPos = BattlefieldSpaceMapper.TileToWorld(spatialLane, unit.currentWaypointTargetX, unit.currentWaypointTargetY);
                    return true;
                }

                if (float.IsFinite(unit.anchorTargetX) && float.IsFinite(unit.anchorTargetY))
                {
                    worldPos = BattlefieldSpaceMapper.TileToWorld(spatialLane, unit.anchorTargetX, unit.anchorTargetY);
                    return true;
                }

                if (float.IsFinite(unit.normProgress))
                {
                    worldPos = BattlefieldSpaceMapper.NormProgressToWorld(spatialLane, Mathf.Clamp01(unit.normProgress));
                    return true;
                }
            }

            worldPos = default;
            return false;
        }

        static int ResolveSpatialLane(MLUnit unit, MLLaneSnap lane)
        {
            if (lane != null && BattlefieldSpaceMapper.IsValidLaneIndex(lane.laneIndex))
                return lane.laneIndex;

            if (unit != null)
            {
                if (BattlefieldSpaceMapper.IsValidLaneIndex(unit.sourceLaneIndex))
                    return unit.sourceLaneIndex;
                if (BattlefieldSpaceMapper.IsValidLaneIndex(unit.ownerLaneIndex))
                    return unit.ownerLaneIndex;
                if (BattlefieldSpaceMapper.IsValidLaneIndex(unit.ownerLane))
                    return unit.ownerLane;
                if (BattlefieldSpaceMapper.IsValidLaneIndex(unit.laneId))
                    return unit.laneId;
            }

            return 0;
        }

        static bool TryResolveTownCoreWorldPosition(MLFortressPad townCorePad, MLLaneSnap lane, int fallbackLaneIndex, float worldY, out Vector3 worldPos)
        {
            int spatialLane = BattlefieldSpaceMapper.IsValidLaneIndex(lane?.laneIndex ?? -1)
                ? lane.laneIndex
                : BattlefieldSpaceMapper.IsValidLaneIndex(townCorePad?.ownerLaneIndex ?? -1)
                    ? townCorePad.ownerLaneIndex
                    : fallbackLaneIndex;
            if (!BattlefieldSpaceMapper.IsValidLaneIndex(spatialLane) || townCorePad == null)
            {
                worldPos = default;
                return false;
            }

            worldPos = BattlefieldSpaceMapper.TileToWorld(spatialLane, townCorePad.gridX, townCorePad.gridY);
            worldPos.y = worldY;
            return true;
        }

        bool TryProjectWorldPointToMiniMap(Vector3 worldPos, float width, float height, out Vector2 anchoredPosition)
        {
            anchoredPosition = default;
            if (_miniMapCamera == null)
                return false;

            Vector3 viewport = _miniMapCamera.WorldToViewportPoint(worldPos);
            if (viewport.z < 0f)
                return false;

            anchoredPosition = new Vector2(
                (Mathf.Clamp01(viewport.x) - 0.5f) * width,
                (Mathf.Clamp01(viewport.y) - 0.5f) * height);
            return true;
        }

        static Color ResolveUnitDotColor(MLUnit unit)
        {
            if (IsDungeonWaveUnit(unit))
                return DungeonMarkerColor;

            return DefaultMarkerColor;
        }

        static Color ResolveTownCoreStarColor(MLFortressPad townCorePad)
        {
            string teamKey = BattleTeamUtility.NormalizeServerTeamKey(townCorePad?.allegianceKey);
            if (string.Equals(teamKey, "dungeon", System.StringComparison.OrdinalIgnoreCase))
                return DungeonMarkerColor;

            return DefaultMarkerColor;
        }

        static bool IsDungeonWaveUnit(MLUnit unit)
        {
            if (unit == null)
                return false;

            if (unit.isWaveUnit)
                return true;

            string explicitAllegiance = BattleTeamUtility.NormalizeServerTeamKey(unit.allegianceKey);
            if (string.Equals(explicitAllegiance, "dungeon", System.StringComparison.OrdinalIgnoreCase))
                return true;

            string spawnSourceType = string.IsNullOrWhiteSpace(unit.spawnSourceType)
                ? null
                : unit.spawnSourceType.Trim();
            return string.Equals(spawnSourceType, "scheduled_wave", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(spawnSourceType, "dungeon_wave", System.StringComparison.OrdinalIgnoreCase);
        }

        Image GetOrCreateUnitDot(int index)
        {
            while (_unitDots.Count <= index)
            {
                var dotGo = new GameObject($"UnitDot{_unitDots.Count}", typeof(RectTransform), typeof(Image), typeof(Outline));
                dotGo.transform.SetParent(unitDotRoot, false);
                var dotRect = dotGo.GetComponent<RectTransform>();
                dotRect.anchorMin = new Vector2(0.5f, 0.5f);
                dotRect.anchorMax = new Vector2(0.5f, 0.5f);
                dotRect.pivot = new Vector2(0.5f, 0.5f);
                dotRect.sizeDelta = unitDotSize;

                var dotImage = dotGo.GetComponent<Image>();
                dotImage.sprite = GetUnitDotSprite();
                dotImage.type = Image.Type.Simple;
                dotImage.useSpriteMesh = false;
                dotImage.raycastTarget = false;

                var outline = dotGo.GetComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = true;

                _unitDots.Add(dotImage);
            }

            return _unitDots[index];
        }

        static Sprite GetUnitDotSprite()
        {
            if (s_unitDotSprite != null)
                return s_unitDotSprite;

            s_unitDotSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            s_unitDotSprite.name = "BattlefieldMiniMapUnitDot";
            return s_unitDotSprite;
        }

        TextMeshProUGUI GetOrCreateTownCoreStar(int index)
        {
            while (_townCoreStars.Count <= index)
            {
                var starGo = new GameObject($"TownCoreStar{_townCoreStars.Count}", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(Outline));
                starGo.transform.SetParent(unitDotRoot, false);
                var starRect = starGo.GetComponent<RectTransform>();
                starRect.anchorMin = new Vector2(0.5f, 0.5f);
                starRect.anchorMax = new Vector2(0.5f, 0.5f);
                starRect.pivot = new Vector2(0.5f, 0.5f);
                starRect.sizeDelta = townCoreStarSize;

                var starText = starGo.GetComponent<TextMeshProUGUI>();
                starText.raycastTarget = false;
                starText.text = "\u2605";
                starText.alignment = TextAlignmentOptions.Center;
                starText.fontSize = townCoreStarFontSize;
                starText.enableWordWrapping = false;
                starText.overflowMode = TextOverflowModes.Overflow;
                if (TMP_Settings.defaultFontAsset != null)
                    starText.font = TMP_Settings.defaultFontAsset;

                var outline = starGo.GetComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = true;

                _townCoreStars.Add(starText);
            }

            return _townCoreStars[index];
        }

        void HideUnitDots()
        {
            for (int i = 0; i < _unitDots.Count; i++)
                _unitDots[i].gameObject.SetActive(false);
        }

        void HideTownCoreStars()
        {
            for (int i = 0; i < _townCoreStars.Count; i++)
                _townCoreStars[i].gameObject.SetActive(false);
        }

        void ReleaseResources()
        {
            RestoreMinimapHiddenVisuals();

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
