using CastleDefender.Game;
using CastleDefender.Net;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Orthographic gameplay camera with pan, zoom, and tilt controls.
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

    [Header("References")]
    public Transform CameraTarget;
    public Camera MainCam;

    [Header("Zoom")]
    public float OrthoSizeMin = 4f;
    public float OrthoSizeMax = 80f;
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
    float _targetOrtho;
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
    float _defaultOrtho;
    float _defaultTilt;
    float _defaultYaw;

    public static bool IsLmbPanning { get; private set; }
    public float CurrentZoom => _targetOrtho;
    public float CurrentTilt => _targetTilt;
    public float CurrentRotation => Mathf.DeltaAngle(0f, _targetYaw);

    void Start()
    {
        EnsureCameraReferences();
        if (MainCam == null)
            return;

        ConfigureBoundsFromGrid();
        SyncCameraStateFromTransform(true);
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
        if (MainCam == null)
            MainCam = Camera.main ?? FindFirstObjectByType<Camera>();
        if (MainCam == null)
            return false;

        if (CameraTarget == null)
            CameraTarget = MainCam.transform;

        return CameraTarget != null;
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
            float speed = KeyPanSpeed * (MainCam.orthographicSize / 10f) * Time.deltaTime;
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

        _targetFocus.x = Mathf.Clamp(_targetFocus.x, BoundsMin.x, BoundsMax.x);
        _targetFocus.z = Mathf.Clamp(_targetFocus.z, BoundsMin.y, BoundsMax.y);
    }

    void ApplySmoothing()
    {
        float dt = Time.deltaTime;
        CameraTarget.position = Vector3.Lerp(CameraTarget.position, _targetPos, PanSmoothing * dt);
        CameraTarget.rotation = Quaternion.Slerp(CameraTarget.rotation, _targetRotation, TiltSmoothing * dt);
        MainCam.orthographicSize = Mathf.Lerp(MainCam.orthographicSize, _targetOrtho, ZoomSmoothing * dt);
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
        Vector3 planarUp = Quaternion.Euler(0f, _targetYaw, 0f) * Vector3.forward;
        float tiltRadians = Mathf.Clamp(_targetTilt, TiltMin, TiltMax) * Mathf.Deg2Rad;
        Vector3 forward = (Vector3.down * Mathf.Cos(tiltRadians)) + (planarUp * Mathf.Sin(tiltRadians));
        forward = forward.normalized;

        float downComponent = Mathf.Max(0.05f, -forward.y);
        float distance = _cameraHeight / downComponent;

        _targetPos = _targetFocus - (forward * distance);
        _targetPos.y = FocusPlaneY + _cameraHeight;
        _targetRotation = Quaternion.LookRotation(forward, planarUp);
    }

    void SyncCameraStateFromTransform(bool captureDefaults)
    {
        _targetOrtho = Mathf.Clamp(MainCam.orthographicSize, OrthoSizeMin, OrthoSizeMax);

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

        UpdateTargetPose();

        if (captureDefaults)
        {
            _defaultFocus = _targetFocus;
            _defaultOrtho = _targetOrtho;
            _defaultTilt = _targetTilt;
            _defaultYaw = _targetYaw;
        }
    }

    public void FocusTile(int col, int row)
    {
        int lane = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.ViewingLane : 0;
        Vector3 point = TileGrid.TileToWorld(lane, col, row);
        PanTo(point);
    }

    public void ResetView()
    {
        _targetFocus = _defaultFocus;
        _targetOrtho = _defaultOrtho;
        _targetTilt = _defaultTilt;
        _targetYaw = _defaultYaw;
        ClampTargetFocus();
        UpdateTargetPose();
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
        UpdateTargetPose();
    }

    public void AdjustTilt(float delta)
    {
        SetTilt(_targetTilt + delta);
    }

    public void SetRotation(float yawDegrees)
    {
        _targetYaw = yawDegrees;
        UpdateTargetPose();
    }

    public void AdjustRotation(float deltaDegrees)
    {
        SetRotation(_targetYaw + deltaDegrees);
    }

    public void SetZoom(float zoomSize)
    {
        _targetOrtho = Mathf.Clamp(zoomSize, OrthoSizeMin, OrthoSizeMax);
    }

    public void AdjustZoom(float delta)
    {
        AdjustZoom(delta, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
    }

    public void AdjustZoom(float delta, Vector2 focusScreenPoint)
    {
        if (!TryScreenToGround(focusScreenPoint, out var worldBefore))
        {
            SetZoom(_targetOrtho + delta);
            return;
        }

        float previousOrtho = MainCam.orthographicSize;
        _targetOrtho = Mathf.Clamp(_targetOrtho + delta, OrthoSizeMin, OrthoSizeMax);
        MainCam.orthographicSize = _targetOrtho;

        if (TryScreenToGround(focusScreenPoint, out var worldAfter))
        {
            _targetFocus += new Vector3(worldBefore.x - worldAfter.x, 0f, worldBefore.z - worldAfter.z);
            ClampTargetFocus();
        }

        MainCam.orthographicSize = previousOrtho;
        UpdateTargetPose();
    }

    /// <summary>
    /// Called after another system positions and rotates the camera so the
    /// controller can adopt that new pose as its target and reset point.
    /// </summary>
    public void SnapToCurrentPosition()
    {
        if (!EnsureCameraReferences())
            return;

        SyncCameraStateFromTransform(true);
        CameraTarget.position = _targetPos;
        CameraTarget.rotation = _targetRotation;
        MainCam.orthographicSize = _targetOrtho;
    }

    void ConfigureBoundsFromGrid()
    {
        if (ConfigureBoundsFromFortressAnchors())
            return;

        var grid = FindFirstObjectByType<TileGrid>();
        if (grid == null)
            return;

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        for (int lane = 0; lane < 4; lane++)
        {
            foreach (var point in new[]
            {
                TileGrid.TileToWorld(lane, 0, 0),
                TileGrid.TileToWorld(lane, grid.Cols - 1, 0),
                TileGrid.TileToWorld(lane, 0, grid.Rows - 1),
                TileGrid.TileToWorld(lane, grid.Cols - 1, grid.Rows - 1),
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

    bool IsPointerOverUi()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    bool IsPointerOverUi(int pointerId)
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(pointerId);
    }
}
