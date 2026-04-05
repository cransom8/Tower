using CastleDefender.Game;
using CastleDefender.Net;
using System.Collections;
using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Gameplay camera with orthographic or perspective projection, pan, zoom, and tilt controls.
///
/// Desktop:
///   Scroll wheel = zoom
///   LMB drag     = pan when not starting over UI
///   RMB/MMB drag = pan
///   R            = reset
///
/// Mobile:
///   1-finger drag = pan
///   2-finger pinch = zoom
///
/// The camera keeps a ground focus point so tilt and zoom preserve the
/// current play area instead of rotating around the camera's own position.
/// </summary>
public class CameraController : MonoBehaviour
{
    static readonly System.Collections.Generic.List<FortressPadAnchor> s_fortressBoundsAnchors = new();
    const float FocusPlaneResolveEpsilon = 0.001f;

    [Header("References")]
    public Transform CameraTarget;
    public Camera MainCam;

    [Header("Zoom")]
    public float OrthoSizeMin = 4f;
    public float OrthoSizeMax = 1000f;
    public float PerspectiveFovMin = 1f;
    public float PerspectiveFovMax = 170f;
    public float ZoomSpeed = 4f;
    public float PinchSpeed = 0.02f;
    public float ZoomSmoothing = 8f;

    [Header("Tilt")]
    public float TiltMin = 0f;
    public float TiltMax = 52f;
    public float TiltSmoothing = 10f;
    public float DefaultTiltStep = 8f;

    [Header("Pan")]
    public float PanSmoothing = 12f;
    public float KeyPanSpeed = 20f;

    [Header("Focus Plane")]
    public float FocusPlaneY = 0f;

    [Header("Grid Bounds (world XZ)")]
    [Tooltip("Uncheck to allow free movement anywhere on the map.")]
    public bool EnableBoundsClamp = false;
    public Vector2 BoundsMin = new Vector2(-140f, -25f);
    public Vector2 BoundsMax = new Vector2(140f, 25f);

    Vector3 _targetPos;
    Quaternion _targetRotation;
    Vector3 _targetFocus;
    float _targetZoom;
    float _targetTilt;
    float _targetYaw;
    float _cameraHeight = 20f;
    bool _isPanning;
    Vector2 _lastPan;

    bool _lmbDragStarted;
    bool _lmbBlocked;
    Vector2 _lmbDownPos;
    Vector2 _lastLmbPos;
    const float LmbDragThreshold = 12f;

    Vector3 _defaultFocus;
    float _defaultZoom;
    float _defaultTilt;
    float _defaultYaw;
    bool _applyingSavedPreferences;

    public static bool IsLmbPanning { get; private set; }
    public float CurrentZoom => _targetZoom;
    public float CurrentTilt => _targetTilt;
    public float CurrentRotation => NormalizeRotationDegrees(_targetYaw);
    public float ZoomMin => IsPerspectiveProjection ? PerspectiveFovMin : OrthoSizeMin;
    public float ZoomMax => IsPerspectiveProjection ? PerspectiveFovMax : OrthoSizeMax;

    bool IsPerspectiveProjection => MainCam != null && !MainCam.orthographic;

    static float NormalizeRotationDegrees(float yawDegrees)
    {
        float normalized = Mathf.Repeat(yawDegrees, 360f);
        if (Mathf.Approximately(normalized, 0f) && yawDegrees > 0.001f)
            return 360f;

        return normalized;
    }

    void OnEnable()
    {
        UserPreferencesManager.PreferencesChanged += HandlePreferencesChanged;
    }

    void OnDisable()
    {
        UserPreferencesManager.PreferencesChanged -= HandlePreferencesChanged;
    }

    void OnDestroy()
    {
        UserPreferencesManager.PreferencesChanged -= HandlePreferencesChanged;
    }

    void Start()
    {
        EnsureCameraReferences();
        if (MainCam == null)
            return;

        ConfigureBoundsFromGrid();
        SyncCameraStateFromTransform(true);
        ApplySavedPreferences(UserPreferencesManager.CurrentPreferences.camera);
        StartCoroutine(ApplySavedPreferencesNextFrame());
    }

    void Update()
    {
        if (!EnsureCameraReferences())
            return;

        ScrollZoom();
        KeyboardInput();
        TouchInput();
        RmbPan();
        LmbPan();
        ResetKey();
        ApplySmoothing();
    }

    bool EnsureCameraReferences()
    {
        ResolveFocusPlaneYIfNeeded();

        if (MainCam == null)
            MainCam = Camera.main ?? FindFirstObjectByType<Camera>();
        if (MainCam == null)
            return false;

        if (CameraTarget == null)
            CameraTarget = MainCam.transform;

        return CameraTarget != null;
    }

    void ResolveFocusPlaneYIfNeeded()
    {
        if (Mathf.Abs(FocusPlaneY) > FocusPlaneResolveEpsilon)
            return;

        FocusPlaneY = BattlefieldSpaceMapper.TileToWorld(0, 0f, 0f).y;
    }

    float ClampZoom(float zoomValue)
    {
        return Mathf.Clamp(zoomValue, ZoomMin, ZoomMax);
    }

    float GetCurrentCameraZoomValue()
    {
        if (MainCam == null)
            return OrthoSizeMin;

        return IsPerspectiveProjection ? MainCam.fieldOfView : MainCam.orthographicSize;
    }

    void ApplyZoomImmediate(float zoomValue)
    {
        if (MainCam == null)
            return;

        if (IsPerspectiveProjection)
            MainCam.fieldOfView = zoomValue;
        else
            MainCam.orthographicSize = zoomValue;
    }

    float GetKeyboardPanScale()
    {
        if (!IsPerspectiveProjection)
            return Mathf.Max(0.25f, _targetZoom / 10f);

        float normalized = Mathf.InverseLerp(PerspectiveFovMin, PerspectiveFovMax, _targetZoom);
        return Mathf.Lerp(0.75f, 1.5f, normalized);
    }

    void ScrollZoom()
    {
        if (IsPointerOverUi())
            return;

        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) <= 0.001f)
            return;

        AdjustZoom(-scroll * ZoomSpeed, Input.mousePosition);
    }

    void LmbPan()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _lmbDownPos = Input.mousePosition;
            _lmbDragStarted = false;
            IsLmbPanning = false;
            _lmbBlocked = IsPointerOverUi();
        }

        if (!_lmbBlocked && Input.GetMouseButton(0))
        {
            if (!_lmbDragStarted && Vector2.Distance(Input.mousePosition, _lmbDownPos) > LmbDragThreshold)
                _lmbDragStarted = true;

            if (_lmbDragStarted)
            {
                IsLmbPanning = true;
                PanBetweenScreenPoints(_lastLmbPos, Input.mousePosition);
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            _lmbDragStarted = false;
            IsLmbPanning = false;
        }

        _lastLmbPos = Input.mousePosition;
    }

    void RmbPan()
    {
        bool pressed = Input.GetMouseButton(1) || Input.GetMouseButton(2);
        bool pressedDown = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool pressedUp = Input.GetMouseButtonUp(1) || Input.GetMouseButtonUp(2);

        if (pressedDown)
        {
            _isPanning = !IsPointerOverUi();
            _lastPan = Input.mousePosition;
        }

        if (pressedUp)
            _isPanning = false;

        if (_isPanning && pressed)
            PanBetweenScreenPoints(_lastPan, Input.mousePosition);

        _lastPan = Input.mousePosition;
    }

    void KeyboardInput()
    {
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");

        if (h != 0f || v != 0f)
        {
            Vector3 right = Vector3.ProjectOnPlane(MainCam.transform.right, Vector3.up).normalized;
            Vector3 upOnGround = Quaternion.Euler(0f, _targetYaw, 0f) * Vector3.forward;
            float speed = KeyPanSpeed * GetKeyboardPanScale() * Time.deltaTime;
            TranslateFocus((right * h + upOnGround * v) * speed);
        }

        if (Input.GetKey(KeyCode.Q))
            AdjustZoom(-ZoomSpeed * Time.deltaTime * 10f);
        if (Input.GetKey(KeyCode.E))
            AdjustZoom(ZoomSpeed * Time.deltaTime * 10f);
    }

    void TouchInput()
    {
        int touchCount = Input.touchCount;
        if (touchCount == 0)
            return;

        if (touchCount == 1)
        {
            Touch touch = Input.GetTouch(0);
            if (IsPointerOverUi(touch.fingerId))
                return;

            if (touch.phase == TouchPhase.Moved)
                PanBetweenScreenPoints(touch.position - touch.deltaPosition, touch.position);
            return;
        }

        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);
        if (IsPointerOverUi(touch0.fingerId) || IsPointerOverUi(touch1.fingerId))
            return;

        Vector2 prev0 = touch0.position - touch0.deltaPosition;
        Vector2 prev1 = touch1.position - touch1.deltaPosition;
        float prevDistance = Vector2.Distance(prev0, prev1);
        float currentDistance = Vector2.Distance(touch0.position, touch1.position);
        float delta = prevDistance - currentDistance;
        Vector2 midpoint = (touch0.position + touch1.position) * 0.5f;
        AdjustZoom(delta * PinchSpeed, midpoint);
    }

    void ResetKey()
    {
        if (Input.GetKeyDown(KeyCode.R))
            ResetView();
    }

    void PanBetweenScreenPoints(Vector2 previousScreen, Vector2 currentScreen)
    {
        if (!TryScreenToGround(previousScreen, out var previousWorld) ||
            !TryScreenToGround(currentScreen, out var currentWorld))
        {
            return;
        }

        TranslateFocus(previousWorld - currentWorld);
    }

    void TranslateFocus(Vector3 worldDelta)
    {
        _targetFocus += new Vector3(worldDelta.x, 0f, worldDelta.z);
        ClampTargetFocus();
        UpdateTargetPose();
    }

    void ClampTargetFocus()
    {
        if (!EnableBoundsClamp)
            return;

        ClampZoomToBounds();
        _targetFocus.x = Mathf.Clamp(_targetFocus.x, BoundsMin.x, BoundsMax.x);
        _targetFocus.z = Mathf.Clamp(_targetFocus.z, BoundsMin.y, BoundsMax.y);
    }

    void ApplySmoothing()
    {
        float dt = Time.deltaTime;
        CameraTarget.position = Vector3.Lerp(CameraTarget.position, _targetPos, PanSmoothing * dt);
        CameraTarget.rotation = Quaternion.Slerp(CameraTarget.rotation, _targetRotation, TiltSmoothing * dt);
        if (IsPerspectiveProjection)
            MainCam.fieldOfView = Mathf.Lerp(MainCam.fieldOfView, _targetZoom, ZoomSmoothing * dt);
        else
            MainCam.orthographicSize = Mathf.Lerp(MainCam.orthographicSize, _targetZoom, ZoomSmoothing * dt);
    }

    bool TryScreenToGround(Vector2 screenPos, out Vector3 worldPoint)
    {
        var plane = new Plane(Vector3.up, new Vector3(0f, FocusPlaneY, 0f));
        Ray ray = MainCam.ScreenPointToRay(screenPos);
        if (!plane.Raycast(ray, out float distance))
        {
            worldPoint = _targetFocus;
            return false;
        }

        worldPoint = ray.GetPoint(distance);
        return true;
    }

    void UpdateTargetPose()
    {
        ComputeTargetPose(_targetFocus, out _targetPos, out _targetRotation);
    }

    void SyncCameraStateFromTransform(bool captureDefaults)
    {
        _targetZoom = ClampZoom(GetCurrentCameraZoomValue());

        if (TryScreenToGround(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), out var focus))
            _targetFocus = new Vector3(focus.x, FocusPlaneY, focus.z);
        else
            _targetFocus = new Vector3(CameraTarget.position.x, FocusPlaneY, CameraTarget.position.z);

        Vector3 screenUp = Vector3.ProjectOnPlane(MainCam.transform.up, Vector3.up);
        if (screenUp.sqrMagnitude < 0.001f)
            screenUp = Vector3.forward;
        screenUp.Normalize();
        _targetYaw = Mathf.Atan2(screenUp.x, screenUp.z) * Mathf.Rad2Deg;

        _cameraHeight = Mathf.Max(4f, CameraTarget.position.y - FocusPlaneY);

        Vector3 forward = MainCam.transform.forward;
        Vector3 planarForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (planarForward.sqrMagnitude < 0.0001f)
            _targetTilt = 0f;
        else
            _targetTilt = Mathf.Clamp(Vector3.Angle(Vector3.down, forward), TiltMin, TiltMax);

        ClampZoomToBounds();
        ClampTargetFocus();
        UpdateTargetPose();

        if (captureDefaults)
        {
            _defaultFocus = _targetFocus;
            _defaultZoom = _targetZoom;
            _defaultTilt = _targetTilt;
            _defaultYaw = _targetYaw;
        }
    }

    public void FocusTile(int col, int row)
    {
        int lane = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.ViewingLane : 0;
        Vector3 point = BattlefieldSpaceMapper.TileToWorld(lane, col, row);
        PanTo(point);
    }

    public void ResetView()
    {
        _targetFocus = _defaultFocus;
        _targetZoom = _defaultZoom;
        _targetTilt = _defaultTilt;
        _targetYaw = _defaultYaw;
        ClampZoomToBounds();
        ClampTargetFocus();
        UpdateTargetPose();
        ReportPreferenceChange();
    }

    public void PanTo(Vector3 worldPos)
    {
        _targetFocus = new Vector3(worldPos.x, FocusPlaneY, worldPos.z);
        ClampTargetFocus();
        UpdateTargetPose();
    }

    public void SetTilt(float tiltDegrees)
    {
        _targetTilt = Mathf.Clamp(tiltDegrees, TiltMin, TiltMax);
        ClampZoomToBounds();
        ClampTargetFocus();
        UpdateTargetPose();
        ReportPreferenceChange();
    }

    public void AdjustTilt(float delta)
    {
        SetTilt(_targetTilt + delta);
    }

    public void SetRotation(float yawDegrees)
    {
        _targetYaw = yawDegrees;
        ClampTargetFocus();
        UpdateTargetPose();
        ReportPreferenceChange();
    }

    public void AdjustRotation(float deltaDegrees)
    {
        SetRotation(_targetYaw + deltaDegrees);
    }

    public void SetZoom(float zoomSize)
    {
        _targetZoom = ClampZoom(zoomSize);
        ClampZoomToBounds();
        ClampTargetFocus();
        UpdateTargetPose();
        ReportPreferenceChange();
    }

    public void AdjustZoom(float delta)
    {
        AdjustZoom(delta, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
    }

    public void AdjustZoom(float delta, Vector2 focusScreenPoint)
    {
        if (!TryScreenToGround(focusScreenPoint, out var worldBefore))
        {
            SetZoom(_targetZoom + delta);
            return;
        }

        float previousZoom = GetCurrentCameraZoomValue();
        _targetZoom = ClampZoom(_targetZoom + delta);
        ClampZoomToBounds();
        ApplyZoomImmediate(_targetZoom);

        if (TryScreenToGround(focusScreenPoint, out var worldAfter))
        {
            _targetFocus += new Vector3(worldBefore.x - worldAfter.x, 0f, worldBefore.z - worldAfter.z);
            ClampTargetFocus();
        }

        ApplyZoomImmediate(previousZoom);
        UpdateTargetPose();
        ReportPreferenceChange();
    }

    /// <summary>
    /// Called after another system positions and rotates the camera so the
    /// controller can adopt that new pose as its target and reset point.
    /// </summary>
    public void SnapToCurrentPosition()
    {
        if (!EnsureCameraReferences())
            return;

        if (EnableBoundsClamp)
            ConfigureBoundsFromGrid();

        SyncCameraStateFromTransform(true);
        CameraTarget.position = _targetPos;
        CameraTarget.rotation = _targetRotation;
        ApplyZoomImmediate(_targetZoom);
    }

    public void ApplyGameplayPreset(float zoomValue, float tiltDegrees)
    {
        if (!EnsureCameraReferences())
            return;

        if (EnableBoundsClamp)
            ConfigureBoundsFromGrid();

        SyncCameraStateFromTransform(false);
        _targetZoom = ClampZoom(zoomValue);
        _targetTilt = Mathf.Clamp(tiltDegrees, TiltMin, TiltMax);
        ClampZoomToBounds();
        ClampTargetFocus();
        UpdateTargetPose();

        CameraTarget.position = _targetPos;
        CameraTarget.rotation = _targetRotation;
        ApplyZoomImmediate(_targetZoom);

        _defaultFocus = _targetFocus;
        _defaultZoom = _targetZoom;
        _defaultTilt = _targetTilt;
        _defaultYaw = _targetYaw;
    }

    IEnumerator ApplySavedPreferencesNextFrame()
    {
        yield return null;
        ApplySavedPreferences(UserPreferencesManager.CurrentPreferences.camera);
    }

    void HandlePreferencesChanged(UserPreferencesData preferences)
    {
        ApplySavedPreferences(preferences?.camera);
    }

    public void ApplySavedPreferences(UserCameraPreferences preferences)
    {
        if (!EnsureCameraReferences())
            return;

        _applyingSavedPreferences = true;
        try
        {
            bool changed = false;

            if (preferences?.tilt.HasValue == true)
            {
                _targetTilt = Mathf.Clamp(preferences.tilt.Value, TiltMin, TiltMax);
                _defaultTilt = _targetTilt;
                changed = true;
            }

            float resolvedZoom = ClampZoom(UserCameraPreferences.ResolveZoom(preferences?.zoom));
            if (!Mathf.Approximately(_targetZoom, resolvedZoom) || !Mathf.Approximately(_defaultZoom, resolvedZoom))
            {
                _targetZoom = resolvedZoom;
                _defaultZoom = _targetZoom;
                changed = true;
            }

            if (preferences?.rotation.HasValue == true)
            {
                float resolvedRotation = NormalizeRotationDegrees(preferences.rotation.Value);
                if (!Mathf.Approximately(NormalizeRotationDegrees(_targetYaw), resolvedRotation) ||
                    !Mathf.Approximately(NormalizeRotationDegrees(_defaultYaw), resolvedRotation))
                {
                    _targetYaw = resolvedRotation;
                    _defaultYaw = _targetYaw;
                    changed = true;
                }
            }

            if (!changed)
                return;

            ClampZoomToBounds();
            ClampTargetFocus();
            UpdateTargetPose();
            CameraTarget.position = _targetPos;
            CameraTarget.rotation = _targetRotation;
            ApplyZoomImmediate(_targetZoom);
        }
        finally
        {
            _applyingSavedPreferences = false;
        }
    }

    void ConfigureBoundsFromGrid()
    {
        if (ConfigureBoundsFromFloor())
            return;

        if (ConfigureBoundsFromFortressAnchors())
            return;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        for (int lane = 0; lane < 4; lane++)
        {
            foreach (var point in new[]
            {
                BattlefieldSpaceMapper.TileToWorld(lane, 0, 0),
                BattlefieldSpaceMapper.TileToWorld(lane, BattlefieldSpaceMapper.LaneCols - 1, 0),
                BattlefieldSpaceMapper.TileToWorld(lane, 0, BattlefieldSpaceMapper.LaneRows - 1),
                BattlefieldSpaceMapper.TileToWorld(lane, BattlefieldSpaceMapper.LaneCols - 1, BattlefieldSpaceMapper.LaneRows - 1),
            })
            {
                if (point.x < minX) minX = point.x;
                if (point.x > maxX) maxX = point.x;
                if (point.z < minZ) minZ = point.z;
                if (point.z > maxZ) maxZ = point.z;
            }
        }

        const float marginX = 30f;
        const float marginZ = 8f;
        BoundsMin = new Vector2(Mathf.Min(minX - marginX, -140f), minZ - marginZ);
        BoundsMax = new Vector2(Mathf.Max(maxX + marginX, 140f), maxZ + marginZ);
    }

    bool ConfigureBoundsFromFloor()
    {
        var renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        bool hasBounds = false;
        Bounds floorBounds = default;

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            string objectName = renderer.gameObject.name;
            if (!string.Equals(objectName, "Map_Floor", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(objectName, "Floor", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!hasBounds)
            {
                floorBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                floorBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
            return false;

        BoundsMin = new Vector2(floorBounds.min.x, floorBounds.min.z);
        BoundsMax = new Vector2(floorBounds.max.x, floorBounds.max.z);
        return true;
    }

    bool ConfigureBoundsFromFortressAnchors()
    {
        if (FortressPadAnchor.CollectAnchors(s_fortressBoundsAnchors) <= 0)
            return false;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        for (int i = 0; i < s_fortressBoundsAnchors.Count; i++)
        {
            var anchor = s_fortressBoundsAnchors[i];
            if (anchor == null)
                continue;

            var bounds = anchor.GetWorldBounds();
            if (bounds.min.x < minX) minX = bounds.min.x;
            if (bounds.max.x > maxX) maxX = bounds.max.x;
            if (bounds.min.z < minZ) minZ = bounds.min.z;
            if (bounds.max.z > maxZ) maxZ = bounds.max.z;
        }

        if (minX == float.MaxValue || minZ == float.MaxValue)
            return false;

        const float marginX = 10f;
        const float marginZ = 10f;
        BoundsMin = new Vector2(minX - marginX, minZ - marginZ);
        BoundsMax = new Vector2(maxX + marginX, maxZ + marginZ);
        return true;
    }

    void ClampZoomToBounds()
    {
        _targetZoom = ClampZoom(_targetZoom);
    }

    bool TryGetAllowedFocusRange(float zoomValue, out Vector2 minFocus, out Vector2 maxFocus)
    {
        minFocus = new Vector2(BoundsMin.x, BoundsMin.y);
        maxFocus = new Vector2(BoundsMax.x, BoundsMax.y);

        if (!TryGetVisibleGroundDeltas(zoomValue, out var deltas))
            return false;

        float minAllowedX = float.NegativeInfinity;
        float maxAllowedX = float.PositiveInfinity;
        float minAllowedZ = float.NegativeInfinity;
        float maxAllowedZ = float.PositiveInfinity;

        for (int i = 0; i < deltas.Length; i++)
        {
            var delta = deltas[i];
            minAllowedX = Mathf.Max(minAllowedX, BoundsMin.x - delta.x);
            maxAllowedX = Mathf.Min(maxAllowedX, BoundsMax.x - delta.x);
            minAllowedZ = Mathf.Max(minAllowedZ, BoundsMin.y - delta.z);
            maxAllowedZ = Mathf.Min(maxAllowedZ, BoundsMax.y - delta.z);
        }

        if (minAllowedX > maxAllowedX || minAllowedZ > maxAllowedZ)
            return false;

        minFocus = new Vector2(minAllowedX, minAllowedZ);
        maxFocus = new Vector2(maxAllowedX, maxAllowedZ);
        return true;
    }

    bool TryGetVisibleGroundDeltas(float zoomValue, out Vector3[] deltas)
    {
        deltas = null;
        if (zoomValue <= 0f || MainCam == null)
            return false;

        float aspect = MainCam != null && MainCam.pixelHeight > 0
            ? Mathf.Max(0.1f, MainCam.aspect)
            : Mathf.Max(0.1f, (float)Screen.width / Mathf.Max(1f, Screen.height));

        ComputeTargetPose(_targetFocus, out var cameraPos, out var cameraRotation);
        Vector3 cameraForward = cameraRotation * Vector3.forward;
        if (Mathf.Abs(cameraForward.y) < FocusPlaneResolveEpsilon)
            return false;

        deltas = new Vector3[4];
        int index = 0;

        if (!IsPerspectiveProjection)
        {
            float halfHeight = zoomValue;
            float halfWidth = zoomValue * aspect;
            Vector3 cameraRight = cameraRotation * Vector3.right;
            Vector3 cameraUp = cameraRotation * Vector3.up;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    Vector3 rayOrigin = cameraPos
                        + (cameraRight * (sx * halfWidth))
                        + (cameraUp * (sy * halfHeight));
                    float distance = (FocusPlaneY - rayOrigin.y) / cameraForward.y;
                    if (distance < 0f)
                        return false;

                    Vector3 groundPoint = rayOrigin + (cameraForward * distance);
                    deltas[index++] = new Vector3(
                        groundPoint.x - _targetFocus.x,
                        0f,
                        groundPoint.z - _targetFocus.z);
                }
            }

            return true;
        }

        float halfVerticalFovTangent = Mathf.Tan(Mathf.Clamp(zoomValue, PerspectiveFovMin, PerspectiveFovMax) * 0.5f * Mathf.Deg2Rad);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            for (int sy = -1; sy <= 1; sy += 2)
            {
                Vector3 rayDirectionCamera = new Vector3(
                    sx * halfVerticalFovTangent * aspect,
                    sy * halfVerticalFovTangent,
                    1f).normalized;
                Vector3 rayDirectionWorld = cameraRotation * rayDirectionCamera;
                if (Mathf.Abs(rayDirectionWorld.y) < FocusPlaneResolveEpsilon)
                    return false;

                float distance = (FocusPlaneY - cameraPos.y) / rayDirectionWorld.y;
                if (distance < 0f)
                    return false;

                Vector3 groundPoint = cameraPos + (rayDirectionWorld * distance);
                deltas[index++] = new Vector3(
                    groundPoint.x - _targetFocus.x,
                    0f,
                    groundPoint.z - _targetFocus.z);
            }
        }

        return true;
    }

    void ComputeTargetPose(Vector3 focus, out Vector3 targetPos, out Quaternion targetRotation)
    {
        Vector3 planarUp = Quaternion.Euler(0f, _targetYaw, 0f) * Vector3.forward;
        float tiltRadians = Mathf.Clamp(_targetTilt, TiltMin, TiltMax) * Mathf.Deg2Rad;
        Vector3 forward = (Vector3.down * Mathf.Cos(tiltRadians)) + (planarUp * Mathf.Sin(tiltRadians));
        forward = forward.normalized;

        float downComponent = Mathf.Max(0.05f, -forward.y);
        float distance = _cameraHeight / downComponent;

        targetPos = focus - (forward * distance);
        targetPos.y = FocusPlaneY + _cameraHeight;
        targetRotation = Quaternion.LookRotation(forward, planarUp);
    }

    bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    bool IsPointerOverUi(int pointerId)
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
    }

    void ReportPreferenceChange()
    {
        if (_applyingSavedPreferences)
            return;

        UserPreferencesManager.NotifyCameraPreferencesChanged(CurrentTilt, CurrentZoom, CurrentRotation);
    }
}
