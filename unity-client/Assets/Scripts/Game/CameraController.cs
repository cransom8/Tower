using UnityEngine;
using UnityEngine.EventSystems;
using CastleDefender.Game;
using CastleDefender.Net;

/// <summary>
/// Top-down camera pan + zoom — works with the straight-down camera used in Game_ML.
///
/// Controls:
///   Desktop : scroll wheel = zoom in/out
///             LMB drag     = pan (when wall-mode is off)
///             MMB drag     = pan (always)
///             R key        = reset view to default position
///   Mobile  : 1-finger drag = pan, 2-finger pinch = zoom
///
/// Pan is pixel-accurate: dragging keeps the world point under the cursor
/// in place regardless of the current zoom level.
/// </summary>
public class CameraController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("References")]
    public Transform CameraTarget;   // Transform to move (assigned to cam.transform by GameManager)
    public Camera    MainCam;        // Main Camera

    [Header("Zoom")]
    public float OrthoSizeMin  = 4f;
    public float OrthoSizeMax  = 80f;   // large enough to see the full 4-bridge map
    public float ZoomSpeed     = 4f;
    public float PinchSpeed    = 0.02f;
    public float ZoomSmoothing = 8f;

    [Header("Pan")]
    public float PanSmoothing  = 12f;
    public float KeyPanSpeed   = 20f;

    [Header("Grid Bounds (world XZ)")]
    [Tooltip("Uncheck to allow free movement anywhere on the map.")]
    public bool EnableBoundsClamp = false;
    public Vector2 BoundsMin = new Vector2(-140f, -25f);
    public Vector2 BoundsMax = new Vector2( 140f,  25f);

    // ── State ─────────────────────────────────────────────────────────────────
    Vector3 _targetPos;
    float   _targetOrtho;
    bool    _isPanning;
    Vector2 _lastPan;
    float   _pinchStartDist;
    float   _pinchStartOrtho;

    // LMB drag pan (non-wall mode only)
    bool    _lmbDragStarted;
    bool    _lmbBlocked;
    Vector2 _lmbDownPos;
    Vector2 _lastLmbPos;
    const float LmbDragThreshold = 12f;

    // Default view restored by R key
    Vector3 _defaultPos;
    float   _defaultOrtho;

    /// <summary>True while a left-mouse drag pan is in progress.</summary>
    public static bool IsLmbPanning { get; private set; }

    // ─────────────────────────────────────────────────────────────────────────

    void Start()
    {
        if (MainCam == null) MainCam = Camera.main ?? FindFirstObjectByType<Camera>();
        if (MainCam == null) return;

        if (CameraTarget == null)
            CameraTarget = MainCam.transform;

        ConfigureBoundsFromGrid();

        _targetPos   = CameraTarget.position;
        _targetOrtho = Mathf.Clamp(MainCam.orthographicSize, OrthoSizeMin, OrthoSizeMax);

        _defaultPos   = _targetPos;
        _defaultOrtho = _targetOrtho;
    }

    void Update()
    {
        // If the camera we were given got destroyed (e.g. previous scene unloaded),
        // try to find a replacement in the current scene.
        if (MainCam == null)
        {
            MainCam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (MainCam == null) return;
            CameraTarget = MainCam.transform;
            ConfigureBoundsFromGrid();
            SnapToCurrentPosition();
            return;
        }
        if (CameraTarget == null) CameraTarget = MainCam.transform;

        ScrollZoom();
        KeyboardInput();
        TouchInput();
        RmbPan();
        LmbPan();
        ResetKey();
        ApplySmoothing();
    }

    // ── Desktop scroll zoom ───────────────────────────────────────────────────

    void ScrollZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            // Zoom toward the cursor: compute world point under cursor before/after
            // and shift _targetPos to compensate, so the point stays fixed.
            Vector3 worldBefore = ScreenToWorld(Input.mousePosition);
            _targetOrtho = Mathf.Clamp(_targetOrtho - scroll * ZoomSpeed,
                                       OrthoSizeMin, OrthoSizeMax);
            // Apply zoom immediately for the world-point calculation (undo smoothing for 1 frame)
            float prevOrtho = MainCam.orthographicSize;
            MainCam.orthographicSize = _targetOrtho;
            Vector3 worldAfter = ScreenToWorld(Input.mousePosition);
            MainCam.orthographicSize = prevOrtho;

            Vector3 shift = worldBefore - worldAfter;
            shift.y = 0f;
            _targetPos += shift;
            ClampTargetPos();
        }
    }

    // ── Desktop LMB pan (non-wall mode only) ─────────────────────────────────

    void LmbPan()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _lmbDownPos     = Input.mousePosition;
            _lmbDragStarted = false;
            IsLmbPanning    = false;

            _lmbBlocked = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        if (!_lmbBlocked && Input.GetMouseButton(0))
        {
            if (!_lmbDragStarted && Vector2.Distance(Input.mousePosition, _lmbDownPos) > LmbDragThreshold)
                _lmbDragStarted = true;

            if (_lmbDragStarted)
            {
                IsLmbPanning = true;
                Pan((Vector2)Input.mousePosition - _lastLmbPos);
            }
        }

        if (Input.GetMouseButtonUp(0))
        {
            _lmbDragStarted = false;
            IsLmbPanning    = false;
        }

        _lastLmbPos = Input.mousePosition;
    }

    // ── Desktop RMB / MMB pan ────────────────────────────────────────────────

    void RmbPan()
    {
        // Right mouse button (1) or middle mouse button (2) both pan
        bool pressed   = Input.GetMouseButton(1)    || Input.GetMouseButton(2);
        bool pressedDn = Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2);
        bool pressedUp = Input.GetMouseButtonUp(1)   || Input.GetMouseButtonUp(2);

        if (pressedDn) { _isPanning = true; _lastPan = Input.mousePosition; }
        if (pressedUp)   _isPanning = false;
        if (_isPanning && pressed)
            Pan((Vector2)Input.mousePosition - _lastPan);
        _lastPan = Input.mousePosition;
    }

    // ── Keyboard pan / zoom (WASD + Q/E) ─────────────────────────────────────

    void KeyboardInput()
    {
        float h = Input.GetAxisRaw("Horizontal"); // A/D or Left/Right
        float v = Input.GetAxisRaw("Vertical");   // W/S or Up/Down

        if (h != 0f || v != 0f)
        {
            Vector3 right = Vector3.ProjectOnPlane(MainCam.transform.right, Vector3.up).normalized;
            Vector3 fwd   = Vector3.ProjectOnPlane(MainCam.transform.up,    Vector3.up).normalized;
            if (right.sqrMagnitude < 0.001f) right = Vector3.right;
            if (fwd.sqrMagnitude   < 0.001f) fwd   = Vector3.forward;

            float speed = KeyPanSpeed * (MainCam.orthographicSize / 10f) * Time.deltaTime;
            _targetPos += (right * h + fwd * v) * speed;
            ClampTargetPos();
        }

        // Q = zoom in, E = zoom out
        if (Input.GetKey(KeyCode.Q))
            _targetOrtho = Mathf.Clamp(_targetOrtho - ZoomSpeed * Time.deltaTime * 10f, OrthoSizeMin, OrthoSizeMax);
        if (Input.GetKey(KeyCode.E))
            _targetOrtho = Mathf.Clamp(_targetOrtho + ZoomSpeed * Time.deltaTime * 10f, OrthoSizeMin, OrthoSizeMax);
    }

    // ── Touch ─────────────────────────────────────────────────────────────────

    void TouchInput()
    {
        int tc = Input.touchCount;
        if (tc == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Moved) Pan(t.deltaPosition);
        }
        else if (tc == 2)
        {
            Touch t0 = Input.GetTouch(0), t1 = Input.GetTouch(1);
            if (t1.phase == TouchPhase.Began)
            {
                _pinchStartDist  = Vector2.Distance(t0.position, t1.position);
                _pinchStartOrtho = _targetOrtho;
                return;
            }
            float delta = _pinchStartDist - Vector2.Distance(t0.position, t1.position);
            _targetOrtho = Mathf.Clamp(_pinchStartOrtho + delta * PinchSpeed,
                                       OrthoSizeMin, OrthoSizeMax);
        }
    }

    // ── Reset key ─────────────────────────────────────────────────────────────

    void ResetKey()
    {
        if (Input.GetKeyDown(KeyCode.R))
            ResetView();
    }

    // ── Shared pan ────────────────────────────────────────────────────────────

    void Pan(Vector2 screenDelta)
    {
        // Convert screen pixel delta to world-space movement.
        // For a top-down orthographic camera we use camera.right and camera.up
        // projected onto the world XZ plane (camera.forward may be (0,-1,0) = zero after projection).
        Vector3 right = Vector3.ProjectOnPlane(MainCam.transform.right, Vector3.up);
        Vector3 fwd   = Vector3.ProjectOnPlane(MainCam.transform.up,    Vector3.up);

        // Fallback if camera is axis-aligned (shouldn't normally happen)
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        if (fwd.sqrMagnitude   < 0.001f) fwd   = Vector3.forward;
        right.Normalize();
        fwd.Normalize();

        // Scale so dragging 1 pixel moves exactly 1 pixel worth of world space
        float pixToWorld = (2f * MainCam.orthographicSize) / Screen.height;

        Vector3 delta = -(right * screenDelta.x + fwd * screenDelta.y) * pixToWorld;
        _targetPos += delta;
        ClampTargetPos();
    }

    void ClampTargetPos()
    {
        if (!EnableBoundsClamp) return;
        _targetPos.x = Mathf.Clamp(_targetPos.x, BoundsMin.x, BoundsMax.x);
        _targetPos.z = Mathf.Clamp(_targetPos.z, BoundsMin.y, BoundsMax.y);
    }

    // ── Smoothing ─────────────────────────────────────────────────────────────

    void ApplySmoothing()
    {
        float dt = Time.deltaTime;
        CameraTarget.position    = Vector3.Lerp(CameraTarget.position, _targetPos,   PanSmoothing  * dt);
        MainCam.orthographicSize = Mathf.Lerp(MainCam.orthographicSize, _targetOrtho, ZoomSmoothing * dt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    Vector3 ScreenToWorld(Vector3 screenPos)
    {
        // For a top-down orthographic camera, map screen point to XZ world plane.
        Ray ray = MainCam.ScreenPointToRay(screenPos);
        float t = ray.origin.y / -ray.direction.y;
        return ray.origin + ray.direction * t;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void FocusTile(int col, int row)
    {
        int lane = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.ViewingLane : 0;
        Vector3 p = TileGrid.TileToWorld(lane, col, row);
        _targetPos = new Vector3(p.x, CameraTarget.position.y, p.z);
    }

    public void ResetView()
    {
        _targetPos   = _defaultPos;
        _targetOrtho = _defaultOrtho;
    }

    public void PanTo(Vector3 worldPos)
    {
        _targetPos = worldPos;
        ClampTargetPos();
    }

    /// <summary>Called by GameManager after it positions the camera at game start.</summary>
    public void SnapToCurrentPosition()
    {
        if (MainCam == null || CameraTarget == null) return;
        _targetPos    = CameraTarget.position;
        _targetOrtho  = MainCam.orthographicSize;
        _defaultPos   = _targetPos;
        _defaultOrtho = _targetOrtho;
    }

    void ConfigureBoundsFromGrid()
    {
        var tg = FindFirstObjectByType<TileGrid>();
        if (tg == null) return;

        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int lane = 0; lane < 4; lane++)
        {
            foreach (var p in new[]
            {
                TileGrid.TileToWorld(lane, 0,            0),
                TileGrid.TileToWorld(lane, tg.Cols - 1,  0),
                TileGrid.TileToWorld(lane, 0,            tg.Rows - 1),
                TileGrid.TileToWorld(lane, tg.Cols - 1,  tg.Rows - 1),
            })
            {
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.z > maxZ) maxZ = p.z;
            }
        }

        // Add generous margin and extend to cover the polyline waypoints (up to ±129 on X)
        const float marginX = 30f;
        const float marginZ = 8f;
        BoundsMin = new Vector2(Mathf.Min(minX - marginX, -140f), minZ - marginZ);
        BoundsMax = new Vector2(Mathf.Max(maxX + marginX,  140f), maxZ + marginZ);
    }
}
