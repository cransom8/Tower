using UnityEngine;
using CastleDefender.Game;

/// <summary>
/// Isometric camera pan + zoom — no Cinemachine API dependency.
///
/// SETUP (Game_ML.unity):
///   1. Create an empty GameObject "CameraTarget" in the scene.
///   2. Set your CinemachineCamera's Follow = CameraTarget.
///   3. Attach this script to any persistent GameObject (e.g. GameManager).
///   4. Assign CameraTarget and MainCam in the Inspector.
///
/// Desktop  : scroll wheel to zoom, middle-mouse-drag to pan
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
        Vector3 p = TileGrid.TileToWorld(col, row);
        _targetPos = new Vector3(p.x, CameraTarget.position.y, p.z);
    }

    public void ResetView()
    {
        _targetPos   = new Vector3(0f, CameraTarget.position.y, 0f);
        _targetOrtho = (OrthoSizeMin + OrthoSizeMax) * 0.5f;
    }

    void ConfigureBoundsFromGrid()
    {
        var tg = FindFirstObjectByType<TileGrid>();
        if (tg == null) return;

        var p0 = TileGrid.TileToWorld(0, 0);
        var p1 = TileGrid.TileToWorld(tg.Cols - 1, 0);
        var p2 = TileGrid.TileToWorld(0, tg.Rows - 1);
        var p3 = TileGrid.TileToWorld(tg.Cols - 1, tg.Rows - 1);

        float minX = Mathf.Min(Mathf.Min(p0.x, p1.x), Mathf.Min(p2.x, p3.x));
        float maxX = Mathf.Max(Mathf.Max(p0.x, p1.x), Mathf.Max(p2.x, p3.x));
        float minZ = Mathf.Min(Mathf.Min(p0.z, p1.z), Mathf.Min(p2.z, p3.z));
        float maxZ = Mathf.Max(Mathf.Max(p0.z, p1.z), Mathf.Max(p2.z, p3.z));

        const float marginX = 2.5f;
        const float marginZ = 1.5f;
        BoundsMin = new Vector2(minX - marginX, minZ - marginZ);
        BoundsMax = new Vector2(maxX + marginX, maxZ + marginZ);
    }
}
