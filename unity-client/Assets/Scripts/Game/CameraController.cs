using UnityEngine;
using UnityEngine.EventSystems;
using CastleDefender.Game;
using CastleDefender.Net;
using CastleDefender.UI;

/// <summary>
/// Isometric camera pan + zoom — no Cinemachine API dependency.
///
/// SETUP (Game_ML.unity):
///   1. Create an empty GameObject "CameraTarget" in the scene.
///   2. Set your CinemachineCamera's Follow = CameraTarget.
///   3. Attach this script to any persistent GameObject (e.g. GameManager).
///   4. Assign CameraTarget and MainCam in the Inspector.
///
/// Desktop  : scroll wheel to zoom, left-mouse-drag to pan (non-wall mode), middle-mouse-drag to pan (always)
/// Mobile   : one-finger drag to pan, two-finger pinch to zoom
/// </summary>
public class CameraController : MonoBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────────
    [Header("References")]
    public Transform CameraTarget;   // Cinemachine Follow target (plain empty GO)
    public Camera    MainCam;        // Main Camera (or Cinemachine Brain camera)

    [Header("Zoom")]
    public float OrthoSizeMin  = 4f;
    public float OrthoSizeMax  = 30f;
    public float ZoomSpeed     = 2f;
    public float PinchSpeed    = 0.02f;
    public float ZoomSmoothing = 8f;

    [Header("Pan")]
    public float PanSpeed      = 0.015f;
    public float PanSmoothing  = 10f;

    [Header("Grid Bounds (world XZ)")]
    public Vector2 BoundsMin = new Vector2(-16f, -2f);
    public Vector2 BoundsMax = new Vector2(8f,   10f);

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
        if (MainCam.orthographicSize < OrthoSizeMin || MainCam.orthographicSize > OrthoSizeMax)
            MainCam.orthographicSize = (OrthoSizeMin + OrthoSizeMax) * 0.5f;

        _targetPos = CameraTarget.position;
        _targetOrtho = MainCam != null ? MainCam.orthographicSize : 8f;
    }

    void Update()
    {
        if (MainCam == null || CameraTarget == null) return;

        ScrollZoom();
        TouchInput();
        RmbPan();
        LmbPan();
        ApplySmoothing();
    }

    // ── Desktop scroll zoom ───────────────────────────────────────────────────

    void ScrollZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            _targetOrtho = Mathf.Clamp(_targetOrtho - scroll * ZoomSpeed,
                                       OrthoSizeMin, OrthoSizeMax);
    }

    // ── Desktop LMB pan (non-wall mode only) ─────────────────────────────────

    void LmbPan()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _lmbDownPos     = Input.mousePosition;
            _lmbDragStarted = false;
            IsLmbPanning    = false;

            // Block LMB pan if wall mode is active or the click landed on a UI element.
            _lmbBlocked = CmdBar.WallModeActive
                       || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
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

    // ── Desktop RMB pan ───────────────────────────────────────────────────────

    void RmbPan()
    {
        if (Input.GetMouseButtonDown(2)) { _isPanning = true; _lastPan = Input.mousePosition; }
        if (Input.GetMouseButtonUp(2))     _isPanning = false;
        if (_isPanning && Input.GetMouseButton(2))
            Pan((Vector2)Input.mousePosition - _lastPan);
        _lastPan = Input.mousePosition;
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

    // ── Shared pan ────────────────────────────────────────────────────────────

    void Pan(Vector2 screenDelta)
    {
        // Pan relative to current camera yaw so drag stays intuitive
        // even if the camera view preset changes.
        Vector3 right = Vector3.ProjectOnPlane(MainCam.transform.right, Vector3.up).normalized;
        Vector3 up    = Vector3.ProjectOnPlane(MainCam.transform.forward, Vector3.up).normalized;
        if (right.sqrMagnitude < 0.001f || up.sqrMagnitude < 0.001f)
        {
            right = new Vector3(0.707f, 0f, 0.707f);
            up    = new Vector3(-0.354f, 0f, 0.354f);
        }
        Vector3 delta  = -(right * screenDelta.x + up * screenDelta.y) * PanSpeed;
        Vector3 proposed = _targetPos + delta;
        proposed.x = Mathf.Clamp(proposed.x, BoundsMin.x, BoundsMax.x);
        proposed.z = Mathf.Clamp(proposed.z, BoundsMin.y, BoundsMax.y);
        _targetPos = proposed;
    }

    // ── Smoothing ─────────────────────────────────────────────────────────────

    void ApplySmoothing()
    {
        float dt = Time.deltaTime;
        CameraTarget.position      = Vector3.Lerp(CameraTarget.position, _targetPos, PanSmoothing * dt);
        MainCam.orthographicSize   = Mathf.Lerp(MainCam.orthographicSize, _targetOrtho, ZoomSmoothing * dt);
    }

    // ── Public helpers ────────────────────────────────────────────────────────

    public void FocusTile(int col, int row)
    {
        int lane = SnapshotApplier.Instance != null ? SnapshotApplier.Instance.ViewingLane : 0;
        Vector3 p = TileGrid.TileToWorld(lane, col, row);
        _targetPos = new Vector3(p.x, CameraTarget.position.y, p.z);
    }

    public void ResetView()
    {
        _targetPos   = new Vector3(0f, CameraTarget.position.y, 0f);
        _targetOrtho = (OrthoSizeMin + OrthoSizeMax) * 0.5f;
    }

    /// <summary>
    /// Smoothly pan the camera to the given world position.
    /// Bounds clamping is applied; smoothing is handled by ApplySmoothing each frame.
    /// </summary>
    public void PanTo(Vector3 worldPos)
    {
        worldPos.x = Mathf.Clamp(worldPos.x, BoundsMin.x, BoundsMax.x);
        worldPos.z = Mathf.Clamp(worldPos.z, BoundsMin.y, BoundsMax.y);
        _targetPos = worldPos;
    }

    void ConfigureBoundsFromGrid()
    {
        var tg = FindFirstObjectByType<TileGrid>();
        if (tg == null) return;

        // Compute bounds from all 4 branch corners to cover the full battlefield.
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

        const float marginX = 2.5f;
        const float marginZ = 1.5f;
        BoundsMin = new Vector2(minX - marginX, minZ - marginZ);
        BoundsMax = new Vector2(maxX + marginX, maxZ + marginZ);
    }
}
