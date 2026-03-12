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
        public MonoBehaviour TileMenuBehaviour;
        ITileMenu TileMenu => TileMenuBehaviour as ITileMenu;

        [Header("Spawn offsets")]
        [Tooltip("Vertical lift for towers so they sit on top of floor tiles.")]
        public float TowerSpawnYOffset = 0.54f;

        [Header("HP bar prefab (optional WorldSpace Canvas Image, same as LaneRenderer)")]
        [Tooltip("If assigned, a fill-bar is shown above each living tower tile.")]
        public GameObject HpBarPrefab;

        [Header("Waypoints")]
        [Tooltip("Optional prefab placed at each path waypoint (should include a Light). If null a point light is created at runtime.")]
        public GameObject WaypointMarkerPrefab;

        // ── Public static geometry ────────────────────────────────────────────
        public const float TileW = 1f;  // 1 world unit per tile (bridge 27 units / 27 rows = 1; row 27 extends 1 tile onto island)
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
            // Origins computed from actual scene bridges (Player_Bridge_Lane_1–4, scale 27×3×11, Y top=1):
            //   Row 0 = spawn side (1 unit inside center-island edge), Row 27 = castle (1 tile onto split island)
            //   Col 5 = bridge centre in Z; colDir sign places col 0 at far-Z edge
            new BranchConfig(new Vector3(-26f, 1f, 17.5f), new Vector3(0f,0f,-1f), new Vector3(-1f,0f,0f)), // Lane 0 Red   upper-left
            new BranchConfig(new Vector3(-26f, 1f, -7.5f), new Vector3(0f,0f,-1f), new Vector3(-1f,0f,0f)), // Lane 1 Gold  lower-left
            new BranchConfig(new Vector3( 26f, 1f,  7.5f), new Vector3(0f,0f, 1f), new Vector3( 1f,0f,0f)), // Lane 2 Blue  upper-right
            new BranchConfig(new Vector3( 26f, 1f,-17.5f), new Vector3(0f,0f, 1f), new Vector3( 1f,0f,0f)), // Lane 3 Green lower-right
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

        /// <summary>Float overload — used by mobile defenders with sub-tile positions.</summary>
        public static Vector3 TileToWorld(int laneIndex, float col, float row)
        {
            if ((uint)laneIndex < (uint)_branchConfigs.Length)
            {
                var bc = _branchConfigs[laneIndex];
                return bc.origin + bc.colDir * (col * TileW) + bc.rowDir * (row * TileH);
            }
            return new Vector3(col * TileW, 0f, row * TileH);
        }

static readonly Vector3[][] _lanePathWaypoints =
        {
            // 6 waypoints per lane — units must pass through each in sequence.
            // Y=1 = bridge top surface.  All points lie on bridges or island surfaces.
            //   pt0 = centre-island spawn edge
            //   pt1 = inner-bridge entry (centre-island side)
            //   pt2 = inner-bridge bottom-quarter checkpoint (75 % toward split island)
            //   pt3 = Island_Split exit / team-bridge entry (Z merged to 0)
            //   pt4 = team-bridge bottom-quarter checkpoint (75 % toward castle island)
            //   pt5 = castle island
            //   pt1  — 4 tiles inside the centre island, clear of the build zone (grid row 0 at X=±26)
            //   pt2  — 1 unit past the grid's castle-end (row 28, outside Rows=28 range) on Island_Split,
            //          so TryWorldToTile returns false → no tile is blocked by this checkpoint
            new[]{ new Vector3(  0f,1f, 12.5f), new Vector3(-22f,1f, 12.5f), new Vector3(-54f,1f, 12.5f), new Vector3(-82f,1f,0f), new Vector3(-102f,1f,0f), new Vector3(-129f,1f,0f) }, // Lane 0 Red
            new[]{ new Vector3(  0f,1f,-12.5f), new Vector3(-22f,1f,-12.5f), new Vector3(-54f,1f,-12.5f), new Vector3(-82f,1f,0f), new Vector3(-102f,1f,0f), new Vector3(-129f,1f,0f) }, // Lane 1 Gold
            new[]{ new Vector3(  0f,1f, 12.5f), new Vector3( 22f,1f, 12.5f), new Vector3( 54f,1f, 12.5f), new Vector3( 82f,1f,0f), new Vector3( 102f,1f,0f), new Vector3( 129f,1f,0f) }, // Lane 2 Blue
            new[]{ new Vector3(  0f,1f,-12.5f), new Vector3( 22f,1f,-12.5f), new Vector3( 54f,1f,-12.5f), new Vector3( 82f,1f,0f), new Vector3( 102f,1f,0f), new Vector3( 129f,1f,0f) }, // Lane 3 Green
        };

        /// <summary>
        /// Maps normProgress (0..1) to world position along the lane's polyline.
        /// Interpolation is proportional to arc length so visual speed stays consistent.
        /// Units follow: centre island → inner bridge → Island_Split → team bridge → castle.
        /// </summary>
        public static Vector3 NormProgressToWorld(int laneIndex, float normProgress)
        {
            var pts = (uint)laneIndex < (uint)_lanePathWaypoints.Length
                ? _lanePathWaypoints[laneIndex]
                : _lanePathWaypoints[0];

            float t = Mathf.Clamp01(normProgress);

            // Compute total arc length
            float totalLen = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
                totalLen += Vector3.Distance(pts[i], pts[i + 1]);

            // Walk segments until we reach the proportional distance
            float target = t * totalLen;
            float walked = 0f;
            for (int i = 0; i < pts.Length - 1; i++)
            {
                float segLen = Vector3.Distance(pts[i], pts[i + 1]);
                if (walked + segLen >= target)
                {
                    float segT = segLen > 0f ? (target - walked) / segLen : 0f;
                    return Vector3.Lerp(pts[i], pts[i + 1], segT);
                }
                walked += segLen;
            }
            return pts[pts.Length - 1];
        }


        /// <summary>Returns the world-space forward direction units travel along a lane (rowDir).</summary>
        public static Vector3 GetLaneForwardDir(int laneIndex)
        {
            if ((uint)laneIndex < (uint)_branchConfigs.Length)
                return _branchConfigs[laneIndex].rowDir;
            return Vector3.forward;
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
        readonly Dictionary<int, Transform>    _towerHpFills       = new(); // tileIdx → HP bar fill Transform
        readonly Dictionary<int, Animator>     _towerAnimators      = new(); // tileIdx → tower Animator
        readonly HashSet<string>               _activeProjectileIds = new(); // projectile IDs seen last snapshot
        // Reused each snapshot to avoid 10Hz GC allocs (P2)
        readonly Dictionary<int, MLTowerCell>  _towerMapBuf         = new();
        readonly Dictionary<int, MLDeadCell>   _deadMapBuf          = new();
        readonly HashSet<string>               _currentIdsBuf       = new();
        // Static attack state table (avoids per-call array alloc)
        static readonly string[] s_attackStates = { "Attack1", "Attack", "AttackSwordShield", "AttackDaggers", "Shoot", "Cast" };

        Vector3              _mouseDownPos;
        bool                 _wasDrag;
        readonly HashSet<int>      _waypointTileIndices = new();
        readonly List<GameObject>  _waypointMarkers     = new();

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
        }

        void OnDisable()
        {
            if (_subscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnSnapshot;
            _subscribed = false;
            foreach (var go in _waypointMarkers) if (go != null) Destroy(go);
            _waypointMarkers.Clear();
            _waypointTileIndices.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null || LaneIndex >= snap.lanes.Length) return;
            if (LaneIndex == 0) Debug.Log($"[TileGrid] snap roundState={snap.roundState} round={snap.roundNumber} lane={LaneIndex}");

            BuildFloorGridForLane(LaneIndex);
            UpdateTiles(snap.lanes[LaneIndex]);
        }

        // ── Grid rebuild ──────────────────────────────────────────────────────
        void BuildFloorGridForLane(int laneIndex)
        {
            if (laneIndex == _currentLaneIndex) return;

            // Destroy all existing tile objects
            _towerHpFills.Clear();
            _towerAnimators.Clear();
            _activeProjectileIds.Clear();
            for (int i = 0; i < _tileObjects.Length; i++)
            {
                if (_tileObjects[i] != null) { Destroy(_tileObjects[i]); _tileObjects[i] = null; }
                _tileTypes[i]  = null;
                _towerTypes[i] = null;
            }

            // Place floor tiles at branch world positions
            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int idx = row * Cols + col;
                if (FloorPrefab == null) continue;

                var go = Instantiate(FloorPrefab, TileToWorld(laneIndex, col, row), Quaternion.identity, transform);
                go.name           = $"Tile_{col}_{row}";
                _tileObjects[idx] = go;
                _tileTypes[idx]   = "floor";
            }

            _currentLaneIndex = laneIndex;
            BuildWaypointMarkers(laneIndex);
        }

        // ── Waypoint markers ──────────────────────────────────────────────────
        void BuildWaypointMarkers(int laneIndex)
        {
            foreach (var go in _waypointMarkers) if (go != null) Destroy(go);
            _waypointMarkers.Clear();
            _waypointTileIndices.Clear();

            if ((uint)laneIndex >= (uint)_lanePathWaypoints.Length) return;

            foreach (var worldPos in _lanePathWaypoints[laneIndex])
            {
                // Spawn marker
                GameObject marker;
                if (WaypointMarkerPrefab != null)
                {
                    marker = Instantiate(WaypointMarkerPrefab, worldPos, Quaternion.identity, transform);
                }
                else
                {
                    marker = new GameObject("WaypointMarker");
                    marker.transform.SetParent(transform);
                    marker.transform.position = worldPos;
                    var light = marker.AddComponent<Light>();
                    light.type      = LightType.Point;
                    light.color     = new Color(0.4f, 0.8f, 1f);  // cool blue — contrasts warm lava
                    light.intensity = 3f;
                    light.range     = 6f;
                }
                marker.name = "WaypointMarker";
                _waypointMarkers.Add(marker);

                // Register tile index if this waypoint falls on a tile in this grid
                if (TryWorldToTile(laneIndex, worldPos, out int col, out int row))
                    _waypointTileIndices.Add(row * Cols + col);
            }
        }

        /// <summary>Inverse of TileToWorld — returns the tile (col, row) for a world position, or false if outside the grid.</summary>
        bool TryWorldToTile(int laneIndex, Vector3 worldPos, out int col, out int row)
        {
            col = row = -1;
            if ((uint)laneIndex >= (uint)_branchConfigs.Length) return false;
            var bc  = _branchConfigs[laneIndex];
            var loc = worldPos - bc.origin;
            col = Mathf.RoundToInt(Vector3.Dot(loc, bc.colDir) / TileW);
            row = Mathf.RoundToInt(Vector3.Dot(loc, bc.rowDir) / TileH);
            return col >= 0 && col < Cols && row >= 0 && row < Rows;
        }

        // ── Tile sync ─────────────────────────────────────────────────────────
        void UpdateTiles(MLLaneSnap lane)
        {
            var towerMap = _towerMapBuf;
            var deadMap  = _deadMapBuf;
            towerMap.Clear();
            deadMap.Clear();

            if (lane.towerCells != null)
                foreach (var t in lane.towerCells)
                {
                    int x = t.X, y = t.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        towerMap[y * Cols + x] = t;
                }

            if (lane.deadCells != null)
                foreach (var d in lane.deadCells)
                {
                    if (d.x >= 0 && d.x < Cols && d.y >= 0 && d.y < Rows)
                        deadMap[d.y * Cols + d.x] = d;
                }

            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int         idx     = row * Cols + col;
                bool        isTower = towerMap.TryGetValue(idx, out var tc);
                MLDeadCell  dc      = null;
                bool        isDead  = !isTower && deadMap.TryGetValue(idx, out dc);
                string      wanted  = isTower ? "tower" : isDead ? "dead_tower" : "floor";
                string      wantedType = isTower ? tc.type : (dc != null ? dc.type : null);

                bool typeChanged = _towerTypes[idx] != wantedType;
                if (_tileTypes[idx] == wanted && !typeChanged) continue;

                if (_tileObjects[idx] != null && _tileTypes[idx] != "floor")
                {
                    _towerAnimators.Remove(idx);
                    Destroy(_tileObjects[idx]);
                    _tileObjects[idx] = null;
                }

                GameObject prefab = (wanted == "tower" || wanted == "dead_tower")
                    ? GetTowerPrefab(wantedType)
                    : FloorPrefab;

                if (prefab != null)
                {
                    Vector3 pos = TileToWorld(_currentLaneIndex, col, row);
                    if (wanted != "floor") pos.y += TowerSpawnYOffset;

                    _tileObjects[idx]      = Instantiate(prefab, pos, Quaternion.identity, transform);
                    _tileObjects[idx].name = $"Tile_{col}_{row}";

                    if (wanted == "tower")
                    {
                        AudioManager.I?.Play(AudioManager.SFX.BuildTower, 0.8f);
                        ApplyDebuffTint(_tileObjects[idx], tc.debuffed);

                        // Cache animator for attack triggers
                        var towerAnim = _tileObjects[idx].GetComponentInChildren<Animator>();
                        if (towerAnim != null) _towerAnimators[idx] = towerAnim;
                        else _towerAnimators.Remove(idx);

                        // Spawn HP bar for this tower slot
                        _towerHpFills.Remove(idx);
                        if (HpBarPrefab != null)
                        {
                            var bar  = Instantiate(HpBarPrefab, _tileObjects[idx].transform);
                            bar.transform.localPosition = Vector3.up * (TowerSpawnYOffset + 0.8f);
                            var fill = bar.transform.Find("Fill");
                            if (fill != null) _towerHpFills[idx] = fill;
                        }
                    }
                    else if (wanted == "dead_tower")
                    {
                        // Dead defenders have no HP bar or animator
                        _towerHpFills.Remove(idx);
                        _towerAnimators.Remove(idx);
                        ApplyDeadTint(_tileObjects[idx]);
                    }
                }

                _tileTypes[idx]  = wanted;
                _towerTypes[idx] = wantedType;
            }

            // Update debuff tint and HP bars on living defenders each snapshot
            if (lane.towerCells != null)
            {
                foreach (var t in lane.towerCells)
                {
                    int tx = t.X, ty = t.Y;
                    if (tx < 0 || tx >= Cols || ty < 0 || ty >= Rows) continue;
                    int idx = ty * Cols + tx;
                    if (_tileObjects[idx] != null && _tileTypes[idx] == "tower")
                    {
                        ApplyDebuffTint(_tileObjects[idx], t.debuffed);
                        if (_towerHpFills.TryGetValue(idx, out var fill) && fill != null && t.maxHp > 0f)
                            fill.localScale = new Vector3(Mathf.Clamp01(t.hp / t.maxHp), 1f, 1f);
                    }
                }
            }

            TriggerTowerAttacks(lane);
        }

        static void ApplyDebuffTint(GameObject go, bool debuffed)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null) bridge.SetDebuffed(debuffed);
                else r.material.SetColor("_BaseColor", debuffed ? new Color(0.7f, 0.4f, 1f) : Color.white);
            }
        }

        // Dead defenders render at reduced opacity/grey — they restore next build phase.
        static void ApplyDeadTint(GameObject go)
        {
            var grey = new Color(0.45f, 0.45f, 0.45f, 0.65f);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null) bridge.SetDebuffed(true);
                else r.material.SetColor("_BaseColor", grey);
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

            HandleInput();
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

            if (Input.GetMouseButtonDown(0))
            {
                _mouseDownPos = Input.mousePosition;
                _wasDrag      = false;
            }

            if (Input.GetMouseButton(0))
            {
                if (Vector3.Distance(Input.mousePosition, _mouseDownPos) > 12f) _wasDrag = true;
            }

            if (Input.GetMouseButtonUp(0))
            {
                bool overUI = UnityEngine.EventSystems.EventSystem.current != null
                    && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
                if (!_wasDrag && !overUI && TryPickTile(Input.mousePosition, out int cc, out int rr))
                    HandleTileClick(cc, rr);
            }
        }

        void HandleTileClick(int col, int row)
        {
            if (!IsInteractive) return;

            // Gate: placement only valid during build phase
            var snap = SnapshotApplier.Instance?.LatestML;
            Debug.Log($"[TileGrid] tap ({col},{row}) roundState={snap?.roundState ?? "NULL"} snapNull={snap == null}");
            if (snap == null || snap.roundState != "build") return;

            int idx = row * Cols + col;

            // Dead defenders: not buildable until next build phase
            if (_tileTypes[idx] == "dead_tower") return;

            // Path endpoints (spawn / castle): never buildable
            if (snap.lanes != null && LaneIndex < snap.lanes.Length)
            {
                var lane = snap.lanes[LaneIndex];
                if (lane.path != null && lane.path.Length > 0)
                {
                    var spawn  = lane.path[0];
                    var castle = lane.path[lane.path.Length - 1];
                    if ((col == spawn.x  && row == spawn.y) ||
                        (col == castle.x && row == castle.y)) return;
                }
            }

            // Tower tile: open upgrade/sell menu
            if (_tileTypes[idx] == "tower")
            {
                TileMenu?.Show(col, row, "tower", _towerTypes[idx]);
                return;
            }

            // Empty floor tile: open unit placement picker
            if (_tileTypes[idx] == "floor")
                TileMenu?.Show(col, row, "empty", null);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Inverse transform: world hit point → tile (col, row) on current branch.
        bool TryPickTile(Vector3 screenPos, out int col, out int row)
        {
            col = row = -1;
            if (Cam == null) return false;

            Ray ray = Cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, new Vector3(0f, 1f, 0f));
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

        // ── Tower attack animations ───────────────────────────────────────────
        void TriggerTowerAttacks(MLLaneSnap lane)
        {
            if (lane.projectiles == null || lane.projectiles.Length == 0)
            {
                _activeProjectileIds.Clear();
                return;
            }

            var currentIds = _currentIdsBuf;
            currentIds.Clear();
            foreach (var p in lane.projectiles)
            {
                if (p == null || p.id == null) continue;
                currentIds.Add(p.id);

                // Only trigger once per projectile (on first appearance)
                if (_activeProjectileIds.Contains(p.id)) continue;
                if (p.fromX < 0 || p.fromX >= Cols || p.fromY < 0 || p.fromY >= Rows) continue;

                int tileIdx = (int)p.fromY * Cols + (int)p.fromX;
                if (_towerAnimators.TryGetValue(tileIdx, out var anim) && anim != null)
                    TriggerAttackAnim(anim, p.projectileType);
            }

            _activeProjectileIds.Clear();
            foreach (var id in currentIds) _activeProjectileIds.Add(id);
        }

        static void TriggerAttackAnim(Animator anim, string projType)
        {
            // Try "Attack" trigger parameter first
            foreach (var p in anim.parameters)
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Attack")
                { anim.SetTrigger("Attack"); return; }

            // Fall back to CrossFade into a known attack state (table defined at class level)
            foreach (var s in s_attackStates)
                if (anim.HasState(0, Animator.StringToHash(s)))
                { anim.CrossFade(s, 0.05f, 0, 0); return; }
        }

    }
}
