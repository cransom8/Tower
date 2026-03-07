// TileGrid.cs — 11×28 square grid: floor, wall, tower tiles.
// Unit management is handled by LaneRenderer.cs.
//
// SETUP:
//   1. Attach to GameObject "TileGrid" in Game_ML scene.
//   2. Assign FloorPrefab, WallPrefab, CastlePrefab, Registry (UnitPrefabRegistry).
//   3. Assign Camera (main or Cinemachine brain).
//   4. Assign TileMenu (TileMenuUI component in scene).
//
// Tile coordinate system:
//   col 0-10 (x), row 0-27 (z)  |  Castle at (5,27), Spawn at (5,0)
// World position: TileToWorld(col, row) — public static for reuse.

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;
using CastleDefender.UI;

namespace CastleDefender.Game
{
    public class TileGrid : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
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
        public TileMenuUI TileMenu;         // assign in inspector

        [Header("Spawn offsets")]
        [Tooltip("Vertical lift for towers so they sit on top of floor tiles.")]
        public float TowerSpawnYOffset = 0.54f;
        [Tooltip("Horizontal tweak for tower centering on tile.")]
        public float TowerSpawnXOffset = 0.06f;
        [Tooltip("Depth tweak for tower centering on tile.")]
        public float TowerSpawnZOffset = -0.04f;

        // ── Public static geometry ────────────────────────────────────────────
        public const float TileW = 1f;
        public const float TileH = 1f;

        public static Vector3 TileToWorld(int col, int row)
        {
            float x = col * TileW;
            float z = row * TileH;
            return new Vector3(x, 0f, z);
        }


        // ── Runtime state ─────────────────────────────────────────────────────
        // Structure GameObjects indexed by [row * Cols + col]
        // Floor tiles are stored but never destroyed.
        // Walls/towers replace each other at a given index.
        GameObject[]       _tileObjects;
        string[]           _tileTypes;     // "floor"|"wall"|"tower"|"castle"
        string[]           _towerTypes;    // null or tower type string for towers

        // Wall placement interaction
        Vector3 _mouseDownPos;
        bool    _wasDrag;
        bool    _subscribed;
        bool    _wallDragActive;
        int     _dragStartCol = -1, _dragStartRow = -1;
        readonly List<Vector2Int> _pendingWallCells = new();
        readonly List<GameObject> _wallPreviewPool = new();

        // ─────────────────────────────────────────────────────────────────────
        void Awake()
        {
            _tileObjects = new GameObject[Cols * Rows];
            _tileTypes   = new string[Cols * Rows];
            _towerTypes  = new string[Cols * Rows];

            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int  idx      = row * Cols + col;
                bool isCastle = col == 5 && row == Rows - 1;
                var  prefab   = isCastle ? CastlePrefab : FloorPrefab;
                if (prefab == null) continue;

                var go = Instantiate(prefab, TileToWorld(col, row), Quaternion.identity, transform);
                go.name            = $"Tile_{col}_{row}";
                _tileObjects[idx]  = go;
                _tileTypes[idx]    = isCastle ? "castle" : "floor";
            }
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

        void SetWallMode(bool active) { /* WallModeActive drives Update logic below */ }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            int viewIdx = SnapshotApplier.Instance.ViewingLane;
            if (snap?.lanes == null || viewIdx >= snap.lanes.Length) return;
            UpdateTiles(snap.lanes[viewIdx]);
        }

        // ── Tile sync ─────────────────────────────────────────────────────────
        void UpdateTiles(MLLaneSnap lane)
        {
            // ① Collect ground truth from snapshot
            var wallSet   = new HashSet<int>();
            var towerMap  = new Dictionary<int, MLTowerCell>();

            if (lane.walls != null)
                foreach (var w in lane.walls)
                {
                    int x = w.X;
                    int y = w.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        wallSet.Add(y * Cols + x);
                }

            if (lane.towerCells != null)
                foreach (var t in lane.towerCells)
                {
                    int x = t.X;
                    int y = t.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        towerMap[y * Cols + x] = t;
                }

            // ② Update each cell that differs
            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int idx = row * Cols + col;
                if (_tileTypes[idx] == "castle") continue;

                bool isWall   = wallSet.Contains(idx);
                bool isTower  = towerMap.TryGetValue(idx, out var tc);
                string wanted = isWall ? "wall" : isTower ? "tower" : "floor";

                bool towerChanged = isTower && _towerTypes[idx] != tc.type;
                if (_tileTypes[idx] == wanted && !towerChanged) continue;

                // Remove old structure
                if (_tileObjects[idx] != null && _tileTypes[idx] != "floor")
                {
                    Destroy(_tileObjects[idx]);
                    _tileObjects[idx] = null;
                }

                // Place new structure
                GameObject prefab = wanted switch
                {
                    "wall"  => WallPrefab,
                    "tower" => GetTowerPrefab(tc?.type),
                    _       => FloorPrefab
                };

                if (prefab != null)
                {
                    Vector3 spawnPos = TileToWorld(col, row);
                    if (wanted == "tower")
                    {
                        spawnPos.y += TowerSpawnYOffset;
                        spawnPos.x += TowerSpawnXOffset;
                        spawnPos.z += TowerSpawnZOffset;
                    }

                    _tileObjects[idx] = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
                    _tileObjects[idx].name = $"Tile_{col}_{row}";
                }

                _tileTypes[idx]  = wanted;
                _towerTypes[idx] = isTower ? tc.type : null;

                // Audio feedback on structure change
                if (wanted == "wall")
                    AudioManager.I?.Play(AudioManager.SFX.PlaceWall, 0.6f);
                else if (wanted == "tower")
                    AudioManager.I?.Play(AudioManager.SFX.BuildTower, 0.8f);

                // Debuff tint (purple) on tower
                if (isTower && _tileObjects[idx] != null)
                    ApplyDebuffTint(_tileObjects[idx], tc.debuffed);
            }

            // ③ Refresh debuff tints for existing towers
            if (lane.towerCells != null)
            {
                foreach (var t in lane.towerCells)
                {
                    int tx = t.X;
                    int ty = t.Y;
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
                if (bridge != null)
                    bridge.SetDebuffed(debuffed);
                else
                    r.material.color = debuffed ? new Color(0.7f, 0.4f, 1f) : Color.white;
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        void Update()
        {
            TrySubscribeSnapshots();
            UpdateWallPreview();
            HandleInput();
        }

        void UpdateWallPreview()
        {
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
                    p.transform.position = TileToWorld(_pendingWallCells[i].x, _pendingWallCells[i].y);
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
            bool canPlace = _tileTypes[idx] == "floor";
            if (!canPlace)
            {
                SetPreviewCount(0);
                return;
            }

            var hover = GetPreviewAt(0);
            hover.SetActive(true);
            hover.transform.position = TileToWorld(col, row);
            SetPreviewCount(1);
        }

        GameObject GetPreviewAt(int index)
        {
            while (_wallPreviewPool.Count <= index)
            {
                var go = Instantiate(WallPrefab, Vector3.zero, Quaternion.identity, transform);
                go.name = $"WallPreview_{_wallPreviewPool.Count}";
                foreach (var c in go.GetComponentsInChildren<Collider>(true))
                    c.enabled = false;
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

            if (sa.LatestML != null)
                OnSnapshot(sa.LatestML);
        }

        void HandleInput()
        {
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

                if (!TryPickTile(Input.mousePosition, out int c, out int r))
                    return;

                int idx = r * Cols + c;
                bool isStructure = _tileTypes[idx] == "wall" || _tileTypes[idx] == "tower";

                if (!wallMode)
                {
                    if (isStructure)
                    {
                        HandleTileClick(c, r);
                        _wasDrag = true;
                    }
                    return;
                }

                if (isStructure)
                {
                    HandleTileClick(c, r);
                    _wasDrag = true;
                    return;
                }

                _wallDragActive = true;
                _dragStartCol = c;
                _dragStartRow = r;
                _pendingWallCells.Clear();
                _pendingWallCells.Add(new Vector2Int(c, r));
            }

            if (Input.GetMouseButton(0))
            {
                if (Vector3.Distance(Input.mousePosition, _mouseDownPos) > 12f)
                    _wasDrag = true;

                if (wallMode && _wallDragActive && TryPickTile(Input.mousePosition, out int col, out int row))
                {
                    _pendingWallCells.Clear();
                    foreach (var cell in BuildLine(_dragStartCol, _dragStartRow, col, row))
                    {
                        if (cell.x < 0 || cell.x >= Cols || cell.y < 0 || cell.y >= Rows) continue;
                        int cidx = cell.y * Cols + cell.x;
                        if (_tileTypes[cidx] == "floor")
                            _pendingWallCells.Add(cell);
                    }
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (wallMode && _wallDragActive)
                {
                    for (int i = 0; i < _pendingWallCells.Count; i++)
                    {
                        var cell = _pendingWallCells[i];
                        ActionSender.PlaceWall(cell.x, cell.y);
                    }
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
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
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
            int idx = row * Cols + col;
            string tileType  = _tileTypes[idx];
            string towerType = _towerTypes[idx];

            if (tileType == "wall" || tileType == "tower")
                TileMenu?.Show(col, row, tileType, towerType);
        }

        // ─────────────────────────────────────────────────────────────────────
        bool TryPickTile(Vector3 screenPos, out int col, out int row)
        {
            col = row = -1;
            if (Cam == null) return false;

            Ray ray = Cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, Vector3.zero);
            if (!plane.Raycast(ray, out float enter)) return false;

            Vector3 hit = ray.GetPoint(enter);
            col = Mathf.RoundToInt(hit.x / TileW);
            row = Mathf.RoundToInt(hit.z / TileH);

            return col >= 0 && col < Cols && row >= 0 && row < Rows;
        }

        GameObject GetTowerPrefab(string type)
        {
            if (Registry != null)
            {
                var p = Registry.GetPrefab(type);
                if (p != null) return p;
            }
            return WallPrefab;
        }
    }
}

