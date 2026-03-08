// TileGrid.cs — 11×28 tile grid for the viewed player branch, positioned in battlefield world space.
// Unit management is handled by LaneRenderer.cs.
//
// SETUP:
//   1. Attach to GameObject "TileGrid" in Game_ML scene.
//   2. Assign FloorPrefab, WallPrefab, CastlePrefab, Registry (UnitPrefabRegistry).
//   3. Assign Camera (main or Cinemachine brain).
//   4. Assign TileMenu (TileMenuUI component in scene).
//
// Tile coordinate system (per branch):
//   col 0-10 (local width axis), row 0-27 (local depth axis)
//   Castle at (5,27), Spawn at (5,0).
//
// World position: TileToWorld(laneIndex, col, row) — lane-aware static method.
// Legacy TileToWorld(col, row) uses straight +Z layout for backward compat.
//
// Battlefield layout (4-player):
//   Lane 0  Red    left side   upper strip  row goes −X, col goes −Z
//   Lane 1  Gold   left side   lower strip  row goes −X, col goes −Z
//   Lane 2  Blue   right side  upper strip  row goes +X, col goes +Z
//   Lane 3  Green  right side  lower strip  row goes +X, col goes +Z
//   Center island at world origin; left castles at X≈−27, right castles at X≈+27.

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Game
{
    public class TileGrid : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Lane assignment")]
        [Tooltip("Which branch (0=Red, 1=Gold, 2=Blue, 3=Green) this TileGrid renders.")]
        public int  LaneIndex     = 0;
        [Tooltip("Only this TileGrid accepts player input. Auto-set at runtime to match MyLaneIndex.")]
        public bool IsInteractive = true;

        [Header("Grid")]
        public int Cols = 11;
        public int Rows = 28;

        [Header("Tile prefabs")]
        public GameObject FloorPrefab;
        public GameObject WallPrefab;
        public GameObject CastlePrefab;

        [Header("Unit prefab registry (key → prefab for all units and towers)")]
        public UnitPrefabRegistry Registry;

        [Header("Camera (for tile picking)")]
        public Camera Cam;

        [Header("Tile interaction")]
        public TileMenuUI TileMenu;

        [Header("Spawn offsets")]
        [Tooltip("Vertical lift for towers so they sit on top of floor tiles.")]
        public float TowerSpawnYOffset = 0.54f;

        // ── Public static geometry ────────────────────────────────────────────
        public const float TileW = 1f;
        public const float TileH = 1f;

        // ── Battlefield branch configs ────────────────────────────────────────
        // Each branch has an origin (world pos of col=0, row=0), a per-column
        // step direction, and a per-row step direction.
        //
        // Col 5 is the center column.  Upper strips center at Z = +8, lower at Z = −8.
        // origin_Z = centerZ ± 5 depending on col direction sign:
        //   Lane 0  centerZ=+8  colDir=(0,0,−1) → origin_Z = 8+5 = 13
        //   Lane 1  centerZ=−8  colDir=(0,0,−1) → origin_Z = −8+5 = −3
        //   Lane 2  centerZ=+8  colDir=(0,0,+1) → origin_Z = 8−5 = 3
        //   Lane 3  centerZ=−8  colDir=(0,0,+1) → origin_Z = −8−5 = −13

        struct BranchConfig
        {
            public Vector3 origin;
            public Vector3 colDir;
            public Vector3 rowDir;
            public BranchConfig(Vector3 o, Vector3 c, Vector3 r)
            { origin = o; colDir = c; rowDir = r; }
        }

        static readonly BranchConfig[] _branchConfigs =
        {
            new BranchConfig(new Vector3(0f,0f, 13f), new Vector3(0f,0f,-1f), new Vector3(-1f,0f,0f)), // Lane 0 Red
            new BranchConfig(new Vector3(0f,0f, -3f), new Vector3(0f,0f,-1f), new Vector3(-1f,0f,0f)), // Lane 1 Gold
            new BranchConfig(new Vector3(0f,0f,  3f), new Vector3(0f,0f, 1f), new Vector3( 1f,0f,0f)), // Lane 2 Blue
            new BranchConfig(new Vector3(0f,0f,-13f), new Vector3(0f,0f, 1f), new Vector3( 1f,0f,0f)), // Lane 3 Green
        };

        /// <summary>Maps tile (col, row) on a given branch to world space.</summary>
        public static Vector3 TileToWorld(int laneIndex, int col, int row)
        {
            if ((uint)laneIndex < (uint)_branchConfigs.Length)
            {
                var bc = _branchConfigs[laneIndex];
                return bc.origin + bc.colDir * (col * TileW) + bc.rowDir * (row * TileH);
            }
            return TileToWorld(col, row);
        }

        /// <summary>Legacy single-lane mapping (straight +Z). Prefer the 3-arg overload.</summary>
        public static Vector3 TileToWorld(int col, int row)
            => new Vector3(col * TileW, 0f, row * TileH);

        // ── Runtime state ─────────────────────────────────────────────────────
        GameObject[] _tileObjects;
        string[]     _tileTypes;
        string[]     _towerTypes;
        int          _currentLaneIndex = -1;
        bool         _subscribed;

        Vector3              _mouseDownPos;
        bool                 _wasDrag;
        bool                 _wallDragActive;
        int                  _dragStartCol = -1, _dragStartRow = -1;
        readonly List<Vector2Int>  _pendingWallCells = new();
        readonly List<GameObject>  _wallPreviewPool  = new();

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            // Allocate arrays; floor tiles are built lazily on the first snapshot
            // so the grid is positioned at the correct branch location in world space.
            _tileObjects = new GameObject[Cols * Rows];
            _tileTypes   = new string[Cols * Rows];
            _towerTypes  = new string[Cols * Rows];
        }

        void OnEnable()
        {
            TrySubscribeSnapshots();
            CmdBar.OnWallModeChanged += SetWallMode;
        }

        void OnDisable()
        {
            if (_subscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnSnapshot;
            _subscribed = false;
            CmdBar.OnWallModeChanged -= SetWallMode;
            SetPreviewCount(0);
            _wallDragActive = false;
            _pendingWallCells.Clear();
        }

        void SetWallMode(bool active) { }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null || LaneIndex >= snap.lanes.Length) return;

            BuildFloorGridForLane(LaneIndex);
            UpdateTiles(snap.lanes[LaneIndex]);
        }

        // ── Grid rebuild ──────────────────────────────────────────────────────
        void BuildFloorGridForLane(int laneIndex)
        {
            if (laneIndex == _currentLaneIndex) return;

            // Destroy all existing tile objects
            for (int i = 0; i < _tileObjects.Length; i++)
            {
                if (_tileObjects[i] != null) { Destroy(_tileObjects[i]); _tileObjects[i] = null; }
                _tileTypes[i]  = null;
                _towerTypes[i] = null;
            }

            // Place floor/castle tiles at branch world positions
            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int  idx      = row * Cols + col;
                bool isCastle = col == 5 && row == Rows - 1;
                var  prefab   = isCastle ? CastlePrefab : FloorPrefab;
                if (prefab == null) continue;

                var go = Instantiate(prefab, TileToWorld(laneIndex, col, row), Quaternion.identity, transform);
                go.name           = $"Tile_{col}_{row}";
                _tileObjects[idx] = go;
                _tileTypes[idx]   = isCastle ? "castle" : "floor";
            }

            _currentLaneIndex = laneIndex;
        }

        // ── Tile sync ─────────────────────────────────────────────────────────
        void UpdateTiles(MLLaneSnap lane)
        {
            var wallSet  = new HashSet<int>();
            var towerMap = new Dictionary<int, MLTowerCell>();

            if (lane.walls != null)
                foreach (var w in lane.walls)
                {
                    int x = w.X, y = w.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        wallSet.Add(y * Cols + x);
                }

            if (lane.towerCells != null)
                foreach (var t in lane.towerCells)
                {
                    int x = t.X, y = t.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        towerMap[y * Cols + x] = t;
                }

            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int    idx    = row * Cols + col;
                if (_tileTypes[idx] == "castle") continue;

                bool   isWall  = wallSet.Contains(idx);
                bool   isTower = towerMap.TryGetValue(idx, out var tc);
                string wanted  = isWall ? "wall" : isTower ? "tower" : "floor";

                bool towerChanged = isTower && _towerTypes[idx] != tc.type;
                if (_tileTypes[idx] == wanted && !towerChanged) continue;

                if (_tileObjects[idx] != null && _tileTypes[idx] != "floor")
                {
                    Destroy(_tileObjects[idx]);
                    _tileObjects[idx] = null;
                }

                GameObject prefab = wanted switch
                {
                    "wall"  => WallPrefab,
                    "tower" => GetTowerPrefab(tc?.type),
                    _       => FloorPrefab
                };

                if (prefab != null)
                {
                    Vector3 pos = TileToWorld(_currentLaneIndex, col, row);
                    if (wanted == "tower") pos.y += TowerSpawnYOffset;

                    _tileObjects[idx]      = Instantiate(prefab, pos, Quaternion.identity, transform);
                    _tileObjects[idx].name = $"Tile_{col}_{row}";
                }

                _tileTypes[idx]  = wanted;
                _towerTypes[idx] = isTower ? tc.type : null;

                if (wanted == "wall")  AudioManager.I?.Play(AudioManager.SFX.PlaceWall,  0.6f);
                else if (wanted == "tower") AudioManager.I?.Play(AudioManager.SFX.BuildTower, 0.8f);

                if (isTower && _tileObjects[idx] != null)
                    ApplyDebuffTint(_tileObjects[idx], tc.debuffed);
            }

            if (lane.towerCells != null)
            {
                foreach (var t in lane.towerCells)
                {
                    int tx = t.X, ty = t.Y;
                    if (tx < 0 || tx >= Cols || ty < 0 || ty >= Rows) continue;
                    int idx = ty * Cols + tx;
                    if (_tileObjects[idx] != null && _tileTypes[idx] == "tower")
                        ApplyDebuffTint(_tileObjects[idx], t.debuffed);
                }
            }
        }

        static void ApplyDebuffTint(GameObject go, bool debuffed)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null) bridge.SetDebuffed(debuffed);
                else r.material.color = debuffed ? new Color(0.7f, 0.4f, 1f) : Color.white;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        void Update()
        {
            TrySubscribeSnapshots();

            // Automatically become interactive only for the player's assigned lane.
            var sa = SnapshotApplier.Instance;
            if (sa != null && sa.MyLaneIndex >= 0)
                IsInteractive = (sa.MyLaneIndex == LaneIndex);

            UpdateWallPreview();
            HandleInput();
        }

        void UpdateWallPreview()
        {
            if (!IsInteractive) { SetPreviewCount(0); return; }

            bool wallMode = CmdBar.WallModeActive;
            if (!wallMode || Cam == null || WallPrefab == null)
            {
                SetPreviewCount(0);
                return;
            }

            if (_wallDragActive)
            {
                for (int i = 0; i < _pendingWallCells.Count; i++)
                {
                    var p = GetPreviewAt(i);
                    p.SetActive(true);
                    p.transform.position = TileToWorld(_currentLaneIndex,
                        _pendingWallCells[i].x, _pendingWallCells[i].y);
                }
                SetPreviewCount(_pendingWallCells.Count);
                return;
            }

            if (!TryPickTile(Input.mousePosition, out int col, out int row))
            {
                SetPreviewCount(0);
                return;
            }

            int idx = row * Cols + col;
            if (_tileTypes[idx] != "floor")
            {
                SetPreviewCount(0);
                return;
            }

            var hover = GetPreviewAt(0);
            hover.SetActive(true);
            hover.transform.position = TileToWorld(_currentLaneIndex, col, row);
            SetPreviewCount(1);
        }

        GameObject GetPreviewAt(int index)
        {
            while (_wallPreviewPool.Count <= index)
            {
                var go = Instantiate(WallPrefab, Vector3.zero, Quaternion.identity, transform);
                go.name = $"WallPreview_{_wallPreviewPool.Count}";
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    r.material.color = new Color(0.7f, 0.9f, 1f, 0.55f);
                go.SetActive(false);
                _wallPreviewPool.Add(go);
            }
            return _wallPreviewPool[index];
        }

        void SetPreviewCount(int activeCount)
        {
            for (int i = activeCount; i < _wallPreviewPool.Count; i++)
                _wallPreviewPool[i].SetActive(false);
        }

        void TrySubscribeSnapshots()
        {
            if (_subscribed) return;
            var sa = SnapshotApplier.Instance;
            if (sa == null) return;

            sa.OnMLSnapshotApplied += OnSnapshot;
            _subscribed = true;

            if (sa.LatestML != null) OnSnapshot(sa.LatestML);
        }

        void HandleInput()
        {
            if (!IsInteractive) return;

            bool wallMode = CmdBar.WallModeActive;

            if (Input.GetMouseButtonDown(1))
            {
                if (TryPickTile(Input.mousePosition, out int rc, out int rr))
                    HandleTileClick(rc, rr);
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                _mouseDownPos = Input.mousePosition;
                _wasDrag      = false;

                if (!TryPickTile(Input.mousePosition, out int c, out int r)) return;

                int  idx         = r * Cols + c;
                bool isStructure = _tileTypes[idx] == "wall" || _tileTypes[idx] == "tower";

                if (!wallMode)
                {
                    if (isStructure) { HandleTileClick(c, r); _wasDrag = true; }
                    return;
                }

                if (isStructure) { HandleTileClick(c, r); _wasDrag = true; return; }

                _wallDragActive = true;
                _dragStartCol   = c;
                _dragStartRow   = r;
                _pendingWallCells.Clear();
                _pendingWallCells.Add(new Vector2Int(c, r));
            }

            if (Input.GetMouseButton(0))
            {
                if (Vector3.Distance(Input.mousePosition, _mouseDownPos) > 12f) _wasDrag = true;

                if (wallMode && _wallDragActive && TryPickTile(Input.mousePosition, out int col, out int row))
                {
                    _pendingWallCells.Clear();
                    foreach (var cell in BuildLine(_dragStartCol, _dragStartRow, col, row))
                    {
                        if (cell.x < 0 || cell.x >= Cols || cell.y < 0 || cell.y >= Rows) continue;
                        int cidx = cell.y * Cols + cell.x;
                        if (_tileTypes[cidx] == "floor") _pendingWallCells.Add(cell);
                    }
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (wallMode && _wallDragActive)
                {
                    foreach (var cell in _pendingWallCells) ActionSender.PlaceWall(cell.x, cell.y);
                    _wallDragActive = false;
                    _pendingWallCells.Clear();
                    SetPreviewCount(0);
                    return;
                }

                if (!_wasDrag && TryPickTile(Input.mousePosition, out int cc, out int rr))
                    HandleTileClick(cc, rr);
            }
        }

        static IEnumerable<Vector2Int> BuildLine(int x0, int y0, int x1, int y1)
        {
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                yield return new Vector2Int(x0, y0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx)  { err += dx; y0 += sy; }
            }
        }

        void HandleTileClick(int col, int row)
        {
            if (!IsInteractive) return;
            int idx = row * Cols + col;
            if (_tileTypes[idx] == "wall" || _tileTypes[idx] == "tower")
                TileMenu?.Show(col, row, _tileTypes[idx], _towerTypes[idx]);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Inverse transform: world hit point → tile (col, row) on current branch.
        bool TryPickTile(Vector3 screenPos, out int col, out int row)
        {
            col = row = -1;
            if (Cam == null) return false;

            Ray ray = Cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 hit = ray.GetPoint(enter);

            if (_currentLaneIndex >= 0 && _currentLaneIndex < _branchConfigs.Length)
            {
                // Project hit onto branch local axes via dot product
                var bc  = _branchConfigs[_currentLaneIndex];
                var loc = hit - bc.origin;
                col = Mathf.RoundToInt(Vector3.Dot(loc, bc.colDir) / TileW);
                row = Mathf.RoundToInt(Vector3.Dot(loc, bc.rowDir) / TileH);
            }
            else
            {
                col = Mathf.RoundToInt(hit.x / TileW);
                row = Mathf.RoundToInt(hit.z / TileH);
            }

            return col >= 0 && col < Cols && row >= 0 && row < Rows;
        }

        GameObject GetTowerPrefab(string type)
        {
            if (Registry != null) { var p = Registry.GetPrefab(type); if (p != null) return p; }
            return WallPrefab;
        }
    }
}
