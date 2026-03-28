// TileGrid.cs — 11×28 tile grid for the viewed player branch, positioned in battlefield world space.
// Unit presentation is handled by WaveSnapshotRuntimeSpawner.cs.
//
// SETUP:
//   1. Attach to GameObject "TileGrid" in Game_ML scene.
//   2. Assign FloorPrefab, WallPrefab, CastlePrefab, Registry (UnitPrefabRegistry).
//   3. Assign Camera (legacy tile picking only; current fortress flow does not use it).
//
// Tile coordinate system (per branch):
//   col 0-10 (local width axis), row 0-27 (local depth axis)
//   Castle at (5,27), Spawn at (5,0).
//
// World position: TileToWorld(laneIndex, col, row) — lane-aware static method.
// Legacy TileToWorld(col, row) uses straight +Z layout for backward compat.
//
// Battlefield layout (4-player):
//   Lane 0  Red    top-left branch
//   Lane 1  Gold   bottom-left branch
//   Lane 2  Blue   top-right branch
//   Lane 3  Green  bottom-right branch
//   The battlefield is arranged around a center mine shaft, with a top loop island,
//   a bottom loop island, and side bridges leading to the castle islands.

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
        public int Cols = BattlefieldSpaceMapper.LaneCols;
        public int Rows = BattlefieldSpaceMapper.LaneRows;

        [Header("Tile prefabs")]
        public GameObject FloorPrefab;
        public GameObject WallPrefab;
        public GameObject CastlePrefab;

        [Header("Unit prefab registry (key → prefab for all units and towers)")]
        public UnitPrefabRegistry Registry;

        [Header("Camera (for tile picking)")]
        public Camera Cam;

        [Header("Spawn offsets")]
        [Tooltip("Vertical lift for towers so they sit on top of floor tiles.")]
        public float TowerSpawnYOffset = 0.54f;

        [Header("HP bar prefab (optional WorldSpace Canvas Image, same as GameplayPresentationRoot)")]
        [Tooltip("If assigned, a fill-bar is shown above each living tower tile.")]
        public GameObject HpBarPrefab;

        [Header("Waypoints")]
        [Tooltip("Optional prefab placed at each path waypoint (should include a Light). If null a point light is created at runtime.")]
        public GameObject WaypointMarkerPrefab;

        [Header("Lane identity markers (optional)")]
        [Tooltip("If assigned, spawned at the lane entrance as a colored gate post. If null, a capsule primitive is created at runtime.")]
        public GameObject LaneMarkerPrefab;

        [Header("Deprecated Fortress Marker Settings")]
        [Tooltip("Deprecated. Fortress mode now binds directly to scene-authored FortressPadAnchor objects instead of spawning runtime pad markers.")]
        public GameObject FortressPadMarkerPrefab;
        [Tooltip("Deprecated. Fortress mode no longer spawns runtime pad markers.")]
        public float FortressPadMarkerYOffset = 0.18f;

        // ── Public static geometry ────────────────────────────────────────────
        public const float TileW = BattlefieldSpaceMapper.TileW;
        public const float TileH = BattlefieldSpaceMapper.TileH;

        // Fallback lane colors used before the server snapshot arrives.
        // Must match SnapshotApplier.TryResolveSlotColor: Red, Gold, Blue, Green.
        static readonly Color[] _laneFallbackColors =
        {
            new Color(0.86f, 0.25f, 0.22f),  // Lane 0 Red
            new Color(0.92f, 0.74f, 0.20f),  // Lane 1 Gold
            new Color(0.24f, 0.50f, 0.92f),  // Lane 2 Blue
            new Color(0.20f, 0.72f, 0.42f),  // Lane 3 Green
        };

        static readonly string[] _laneColorNames = { "Red", "Gold", "Blue", "Green" };

        /// <summary>Maps tile (col, row) on a given branch to world space.</summary>
        public static Vector3 TileToWorld(int laneIndex, int col, int row)
        {
            return BattlefieldSpaceMapper.TileToWorld(laneIndex, col, row);
        }

        /// <summary>Float overload — used by mobile defenders with sub-tile positions.</summary>
        public static Vector3 TileToWorld(int laneIndex, float col, float row)
        {
            return BattlefieldSpaceMapper.TileToWorld(laneIndex, col, row);
        }

        /// <summary>
        /// Maps normProgress (0..1) to world position along the lane's polyline.
        /// Interpolation is proportional to arc length so visual speed stays consistent.
        /// Units follow: centre island → inner bridge → Island_Split → team bridge → castle.
        /// </summary>
        public static Vector3 NormProgressToWorld(int laneIndex, float normProgress)
        {
            return BattlefieldSpaceMapper.NormProgressToWorld(laneIndex, normProgress);
        }


        /// <summary>Returns the world-space forward direction units travel along a lane (rowDir).</summary>
        public static Vector3 GetLaneForwardDir(int laneIndex)
        {
            return BattlefieldSpaceMapper.GetLaneForwardDir(laneIndex);
        }

        /// <summary>Returns the world-space lateral direction across a lane (colDir).</summary>
        public static Vector3 GetLaneLateralDir(int laneIndex)
        {
            return BattlefieldSpaceMapper.GetLaneLateralDir(laneIndex);
        }

        /// <summary>
        /// Maps suffixProgress (0..1) to world space along the suffix portion of the lane's polyline.
        /// suffixProgress=0 → pt2 (grid end = TileToWorld(branchCfg, 5, 28)), suffixProgress=1 → pt5 (castle).
        /// This avoids the position jump that occurs when using NormProgressToWorld with the full polyline.
        /// </summary>
        public static Vector3 SuffixProgressToWorld(int branchCfg, float suffixProgress)
        {
            return BattlefieldSpaceMapper.SuffixProgressToWorld(branchCfg, suffixProgress);
        }

        /// <summary>Maps branchId from server snapshot to the authoritative battlefield lane index.</summary>
        public static int GetBranchConfigIndex(string branchId)
        {
            return BattlefieldSpaceMapper.GetBranchConfigIndex(branchId);
        }

        /// <summary>Legacy single-lane mapping (straight +Z). Prefer the 3-arg overload.</summary>
        public static Vector3 TileToWorld(int col, int row)
            => BattlefieldSpaceMapper.TileToWorld(col, row);

        // ── Runtime state ─────────────────────────────────────────────────────
        GameObject[] _tileObjects;
        string[]     _tileTypes;
        string[]     _towerTypes;
        int          _currentBranchCfg = -1;
        bool         _subscribed;
        readonly Dictionary<int, Transform>    _towerHpFills       = new(); // tileIdx → HP bar fill Transform
        readonly Dictionary<int, Vector3>      _towerHpFillScales  = new(); // tileIdx → base fill scale
        readonly Dictionary<int, Vector3>      _towerHpFillPoses   = new(); // tileIdx → base fill localPosition
        readonly Dictionary<int, Animator>     _towerAnimators      = new(); // tileIdx → tower Animator
        readonly HashSet<string>               _activeProjectileIds = new(); // projectile IDs seen last snapshot
        // Reused each snapshot to avoid 10Hz GC allocs (P2)
        readonly Dictionary<int, MLTowerCell>  _towerMapBuf         = new();
        readonly Dictionary<int, MLTowerCell>  _mobilizedMapBuf     = new();
        readonly Dictionary<int, MLDeadCell>   _deadMapBuf          = new();
        readonly HashSet<string>               _currentIdsBuf       = new();
        // Static attack state table (avoids per-call array alloc)
        static readonly string[] s_attackStates = { "Attack1", "Attack", "AttackSwordShield", "AttackDaggers", "Shoot", "Cast" };

        readonly HashSet<int>      _waypointTileIndices = new();
        readonly List<GameObject>  _waypointMarkers     = new();
        readonly List<GameObject>  _laneMarkers         = new();

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
            ClearLegacyTileGridVisuals();
        }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null) return;

            var laneSnap = GetLaneSnap(snap, LaneIndex);
            if (laneSnap == null) return;
            if (ShouldBlockLegacyTileMenu(out _))
            {
                ClearLegacyTileGridVisuals();
                return;
            }

            int branchCfg = GetBranchConfigIndex(laneSnap?.branchId);
            if (branchCfg < 0)
            {
                Debug.LogError(
                    $"[TileGrid] Lane {LaneIndex} on '{name}' received unknown branchId '{laneSnap?.branchId ?? "<null>"}'. " +
                    "Grid rebuild is aborted instead of silently remapping the lane.");
                RuntimeFailureMarker.Mark(transform, $"branch_{LaneIndex}", $"Lane {LaneIndex} missing branch config");
                return;
            }

            RuntimeFailureMarker.Clear(transform, $"branch_{LaneIndex}");

            BuildFloorGridForLane(branchCfg);
            UpdateTiles(laneSnap);
            ApplyFloorLaneColor();
        }

        // ── Grid rebuild ──────────────────────────────────────────────────────
        void BuildFloorGridForLane(int branchCfg)
        {
            if (branchCfg == _currentBranchCfg) return;

            // Destroy all existing tile objects
            _towerHpFills.Clear();
            _towerHpFillScales.Clear();
            _towerHpFillPoses.Clear();
            _towerAnimators.Clear();
            _activeProjectileIds.Clear();
            for (int i = 0; i < _tileObjects.Length; i++)
            {
                if (_tileObjects[i] != null) { Destroy(_tileObjects[i]); _tileObjects[i] = null; }
                _tileTypes[i]  = null;
                _towerTypes[i] = null;
            }
            foreach (var go in _laneMarkers) if (go != null) Destroy(go);
            _laneMarkers.Clear();

            // Place floor tiles at branch world positions
            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int idx = row * Cols + col;
                if (FloorPrefab == null) continue;

                var go = Instantiate(FloorPrefab, TileToWorld(branchCfg, col, row), Quaternion.identity, transform);
                go.name           = $"Tile_{col}_{row}";
                _tileObjects[idx] = go;
                _tileTypes[idx]   = "floor";
            }

            ApplyFloorLaneColor();
            BuildLaneEntranceMarkers(branchCfg);
            _currentBranchCfg = branchCfg;
            BuildWaypointMarkers(branchCfg);
        }

        void ClearLegacyTileGridVisuals()
        {
            _towerHpFills.Clear();
            _towerHpFillScales.Clear();
            _towerHpFillPoses.Clear();
            _towerAnimators.Clear();
            _activeProjectileIds.Clear();

            for (int i = 0; i < _tileObjects.Length; i++)
            {
                if (_tileObjects[i] != null)
                {
                    Destroy(_tileObjects[i]);
                    _tileObjects[i] = null;
                }

                _tileTypes[i] = null;
                _towerTypes[i] = null;
            }

            foreach (var go in _waypointMarkers)
                if (go != null)
                    Destroy(go);
            _waypointMarkers.Clear();

            foreach (var go in _laneMarkers)
                if (go != null)
                    Destroy(go);
            _laneMarkers.Clear();
            _waypointTileIndices.Clear();
            _currentBranchCfg = -1;
        }

        // ── Waypoint markers ──────────────────────────────────────────────────
        void BuildWaypointMarkers(int laneIndex)
        {
            foreach (var go in _waypointMarkers) if (go != null) Destroy(go);
            _waypointMarkers.Clear();
            _waypointTileIndices.Clear();

            if (!BattlefieldSpaceMapper.IsValidLaneIndex(laneIndex)) return;

            foreach (var worldPos in BattlefieldSpaceMapper.GetLanePathWaypoints(laneIndex))
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

        // Lane floor color (Option E)
        void ApplyFloorLaneColor()
        {
            // Keep the playable surface in the same winter palette as the map.
            // Team identity still comes from units/UI/markers, not a saturated lane slab.
            Color frostBase = new Color(0.92f, 0.955f, 1f, 1f);
            Color frostEdge = new Color(0.82f, 0.88f, 0.94f, 1f);
            Color rimColor = new Color(0.58f, 0.66f, 0.74f, 1f);
            Color tintWeak = frostBase;
            Color tintEdge = frostEdge;

            for (int row = 0; row < Rows; row++)
            for (int col = 0; col < Cols; col++)
            {
                int idx = row * Cols + col;
                var go = _tileObjects[idx];
                if (go == null) continue;
                if (_tileTypes[idx] != "floor" && _tileTypes[idx] != "tower_mobilized") continue;

                bool isEdge = col == 0 || col == Cols - 1;
                Color tint = isEdge ? tintEdge : tintWeak;

                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var bridge = r.GetComponent<ToonShaderBridge>();
                    if (bridge != null)
                    {
                        bridge.SetBaseColor(tint);
                        bridge.SetRimColor(rimColor);
                    }
                    else
                    {
                        var mpb = new MaterialPropertyBlock();
                        r.GetPropertyBlock(mpb);
                        mpb.SetColor("_BaseColor", tint);
                        mpb.SetColor("_RimColor", rimColor);
                        r.SetPropertyBlock(mpb);
                    }
                }
            }
        }

        // Lane entrance markers (Option C)
        void BuildLaneEntranceMarkers(int branchCfg)
        {
            foreach (var go in _laneMarkers) if (go != null) Destroy(go);
            _laneMarkers.Clear();

            Color laneColor = SnapshotApplier.Instance != null
                ? SnapshotApplier.Instance.GetLaneColor(LaneIndex, _laneFallbackColors[Mathf.Clamp(LaneIndex, 0, 3)])
                : _laneFallbackColors[Mathf.Clamp(LaneIndex, 0, 3)];

            string label = LaneIndex >= 0 && LaneIndex < _laneColorNames.Length
                ? _laneColorNames[LaneIndex]
                : $"Lane {LaneIndex + 1}";

            int[] gateCols = { 0, Cols - 1 };
            foreach (int gateCol in gateCols)
            {
                Vector3 pos = TileToWorld(branchCfg, gateCol, 0);
                pos.y += 1.2f;

                GameObject marker;
                if (LaneMarkerPrefab != null)
                {
                    marker = Instantiate(LaneMarkerPrefab, pos, Quaternion.identity, transform);
                }
                else
                {
                    marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    marker.transform.SetParent(transform);
                    marker.transform.position = pos;
                    marker.transform.localScale = new Vector3(0.25f, 0.6f, 0.25f);

                    var capsuleCollider = marker.GetComponent<Collider>();
                    if (capsuleCollider != null) Destroy(capsuleCollider);

                }

                foreach (var rend in marker.GetComponentsInChildren<Renderer>(true))
                {
                    var bridge = rend.GetComponent<ToonShaderBridge>();
                    if (bridge != null)
                    {
                        bridge.SetBaseColor(laneColor);
                        bridge.SetRimColor(laneColor);
                    }
                    else
                    {
                        var mpb = new MaterialPropertyBlock();
                        rend.GetPropertyBlock(mpb);
                        mpb.SetColor("_BaseColor", laneColor);
                        mpb.SetColor("_EmissionColor", laneColor * 1.4f);
                        mpb.SetColor("_RimColor", laneColor);
                        rend.SetPropertyBlock(mpb);
                    }
                }

                var light = marker.GetComponentInChildren<Light>(true);
                if (light == null)
                {
                    var lightGo = new GameObject("MarkerLight");
                    lightGo.transform.SetParent(marker.transform);
                    lightGo.transform.localPosition = Vector3.up * 0.8f;
                    light = lightGo.AddComponent<Light>();
                }
                light.type = LightType.Point;
                light.color = laneColor;
                light.intensity = 2.5f;
                light.range = 5f;

                marker.name = $"LaneMarker_{label}_{gateCol}";
                _laneMarkers.Add(marker);
            }

            Vector3 labelPos = TileToWorld(branchCfg, 5, 0);
            labelPos.y += 3f;

            var labelGo = new GameObject($"LaneLabel_{label}");
            labelGo.transform.SetParent(transform);
            labelGo.transform.position = labelPos;

            var tm = labelGo.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = 24;
            tm.characterSize = 0.18f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = laneColor;

            _laneMarkers.Add(labelGo);
        }

        /// <summary>Inverse of TileToWorld — returns the tile (col, row) for a world position, or false if outside the grid.</summary>
        bool TryWorldToTile(int laneIndex, Vector3 worldPos, out int col, out int row)
        {
            return BattlefieldSpaceMapper.TryWorldToTile(laneIndex, worldPos, Cols, Rows, out col, out row);
        }

        // ── Tile sync ─────────────────────────────────────────────────────────
        void UpdateTiles(MLLaneSnap lane)
        {
            var towerMap     = _towerMapBuf;
            var mobilizedMap = _mobilizedMapBuf;
            var deadMap      = _deadMapBuf;
            towerMap.Clear();
            mobilizedMap.Clear();
            deadMap.Clear();

            if (lane.towerCells != null)
                foreach (var t in lane.towerCells)
                {
                    int x = t.X, y = t.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        towerMap[y * Cols + x] = t;
                }

            if (lane.mobilizedCells != null)
                foreach (var t in lane.mobilizedCells)
                {
                    int x = t.X, y = t.Y;
                    if (x >= 0 && x < Cols && y >= 0 && y < Rows)
                        mobilizedMap[y * Cols + x] = t;
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
                int         idx         = row * Cols + col;
                bool        isTower     = towerMap.TryGetValue(idx, out var tc);
                MLTowerCell mc          = default;
                bool        isMobilized = !isTower && mobilizedMap.TryGetValue(idx, out mc);
                MLDeadCell  dc          = null;
                bool        isDead      = !isTower && !isMobilized && deadMap.TryGetValue(idx, out dc);
                // "tower_mobilized" = defender walked out; renders as floor but stays selectable
                string      wanted      = isTower ? "tower" : isMobilized ? "tower_mobilized" : isDead ? "dead_tower" : "floor";
                string      wantedType  = isTower ? tc.type : isMobilized ? mc.type : (dc != null ? dc.type : null);

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
                string missingTowerMarkerKey = $"missing_tower_{LaneIndex}_{col}_{row}";

                if (prefab != null)
                {
                    RuntimeFailureMarker.Clear(transform, missingTowerMarkerKey);
                    Vector3 pos = TileToWorld(_currentBranchCfg, col, row);
                    if (wanted == "tower" || wanted == "dead_tower") pos.y += TowerSpawnYOffset;

                    _tileObjects[idx]      = Instantiate(prefab, pos, Quaternion.identity, transform);
                    _tileObjects[idx].name = $"Tile_{col}_{row}";

                    if (wanted == "tower" || wanted == "dead_tower")
                    {
                        foreach (var renderer in _tileObjects[idx].GetComponentsInChildren<Renderer>(true))
                            UpgradeLegacyRendererMaterials(renderer);
                    }

                    if (wanted == "tower")
                    {
                        AudioManager.I?.Play(AudioManager.SFX.BuildTower, 0.8f);
                        ApplyDebuffTint(_tileObjects[idx], tc.debuffed);

                        // Scale tower based on its level (level 1 = small, grows with each level)
                        int   towerLevel     = Mathf.Max(1, tc.level);
                        float towerBaseScale = Registry != null ? Registry.GetScaleForSkin(wantedType, null) : 1f;
                        float towerScale     = towerBaseScale * GetLevelScale(towerLevel);
                        _tileObjects[idx].transform.localScale = Vector3.one * towerScale;

                        // Cache animator for attack triggers
                        var towerAnim = _tileObjects[idx].GetComponentInChildren<Animator>();
                        if (towerAnim != null) _towerAnimators[idx] = towerAnim;
                        else _towerAnimators.Remove(idx);

                        // Spawn HP bar for this tower slot
                        _towerHpFills.Remove(idx);
                        _towerHpFillScales.Remove(idx);
                        _towerHpFillPoses.Remove(idx);
                        if (HpBarPrefab != null)
                        {
                            var bar  = Instantiate(HpBarPrefab, _tileObjects[idx].transform);
                            // Keep bar at a fixed world-space height above tile regardless of tower scale
                            const float worldBarHeight = 1.34f;
                            bar.transform.localPosition = Vector3.up * (worldBarHeight / towerScale);
                            HpBarVisuals.EnsureStyled(bar.transform);
                            var fill = FindChildRecursive(bar.transform, "Fill");
                            if (fill != null)
                            {
                                _towerHpFills[idx] = fill;
                                _towerHpFillScales[idx] = fill.localScale;
                                _towerHpFillPoses[idx] = fill.localPosition;
                            }
                            AddHpBarNotches(bar.transform, towerLevel);
                        }
                    }
                    else if (wanted == "dead_tower")
                    {
                        // Dead defenders have no HP bar or animator
                        _towerHpFills.Remove(idx);
                        _towerHpFillScales.Remove(idx);
                        _towerHpFillPoses.Remove(idx);
                        _towerAnimators.Remove(idx);
                        ApplyDeadTint(_tileObjects[idx]);
                    }
                }
                else if (wanted == "tower" || wanted == "dead_tower")
                {
                    Vector3 markerWorld = TileToWorld(_currentBranchCfg, col, row) + Vector3.up * (TowerSpawnYOffset + 1.8f);
                    RuntimeFailureMarker.MarkWorld(
                        transform,
                        missingTowerMarkerKey,
                        markerWorld,
                        $"Missing tower prefab '{wantedType}' lane {LaneIndex} [{col},{row}]");
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
                        if (_towerHpFills.TryGetValue(idx, out var fill)
                            && _towerHpFillScales.TryGetValue(idx, out var baseScale)
                            && _towerHpFillPoses.TryGetValue(idx, out var basePos)
                            && fill != null && t.maxHp > 0f)
                        {
                            float hp01 = Mathf.Clamp01(t.hp / t.maxHp);
                            fill.localScale = new Vector3(baseScale.x * hp01, baseScale.y, baseScale.z);
                            fill.localPosition = new Vector3(0.5f * hp01, basePos.y, basePos.z);
                            HpBarVisuals.ApplyFill(fill, fill.GetComponent<UnityEngine.UI.Image>(), hp01);
                        }
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
            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier != null && snapshotApplier.MyLaneIndex >= 0)
                IsInteractive = snapshotApplier.MyLaneIndex == LaneIndex;
        }

        void TrySubscribeSnapshots()
        {
            if (_subscribed) return;
            var snapshotApplier = SnapshotApplier.Instance;
            if (snapshotApplier == null) return;

            snapshotApplier.OnMLSnapshotApplied += OnSnapshot;
            _subscribed = true;

            if (snapshotApplier.LatestML != null)
                OnSnapshot(snapshotApplier.LatestML);
        }

        bool ShouldBlockLegacyTileMenu(out string reason)
        {
            return FortressSelectionController.ShouldBlockLegacyTileMenu(LaneIndex, out reason);
        }

        GameObject GetTowerPrefab(string type)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                Debug.LogError(
                    $"[TileGrid] Lane {LaneIndex} on '{name}' tried to spawn a tower with an empty type key. " +
                    "Runtime will not substitute WallPrefab.",
                    this);
                return null;
            }

            if (Registry == null)
            {
                Debug.LogError(
                    $"[TileGrid] Lane {LaneIndex} on '{name}' is missing UnitPrefabRegistry. " +
                    $"Tower '{type}' cannot spawn and runtime will not substitute WallPrefab.",
                    this);
                return null;
            }

            return Registry.GetPrefab(type);
        }

        static MLLaneSnap GetLaneSnap(MLSnapshot snap, int laneIndex)
        {
            if (snap?.lanes == null) return null;
            for (int i = 0; i < snap.lanes.Length; i++)
            {
                var lane = snap.lanes[i];
                if (lane != null && lane.laneIndex == laneIndex)
                    return lane;
            }
            return null;
        }

        static void UpgradeLegacyRendererMaterials(Renderer renderer)
        {
            if (renderer == null) return;
            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;

            var shared = renderer.sharedMaterials;
            var instanced = renderer.materials;
            bool changed = false;

            for (int i = 0; i < instanced.Length; i++)
            {
                var mat = instanced[i];
                if (mat == null || mat.shader == null) continue;
                if (mat.shader.name.StartsWith("Universal Render Pipeline"))
                    continue;

                var upgraded = new Material(urpLit);
                var source = i < shared.Length ? shared[i] : mat;

                if (source != null)
                {
                    CopyTextureIfPresent(source, upgraded, "_MainTex", "_BaseMap");
                    CopyTextureIfPresent(source, upgraded, "_BumpMap", "_BumpMap");
                    CopyTextureIfPresent(source, upgraded, "_MetallicGlossMap", "_MetallicGlossMap");
                    CopyTextureIfPresent(source, upgraded, "_OcclusionMap", "_OcclusionMap");
                    CopyTextureIfPresent(source, upgraded, "_EmissionMap", "_EmissionMap");

                    if (source.HasProperty("_Color"))
                        upgraded.SetColor("_BaseColor", source.GetColor("_Color"));
                    if (source.HasProperty("_EmissionColor"))
                        upgraded.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                    if (source.HasProperty("_Glossiness"))
                        upgraded.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
                    if (source.HasProperty("_BumpScale"))
                        upgraded.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                    if (source.HasProperty("_Metallic"))
                        upgraded.SetFloat("_Metallic", source.GetFloat("_Metallic"));
                    if (source.HasProperty("_OcclusionStrength"))
                        upgraded.SetFloat("_OcclusionStrength", source.GetFloat("_OcclusionStrength"));
                }

                instanced[i] = upgraded;
                changed = true;
            }

            if (changed)
                renderer.materials = instanced;
        }

        static void CopyTextureIfPresent(Material source, Material destination, string sourceProp, string destinationProp)
        {
            if (!source.HasProperty(sourceProp) || !destination.HasProperty(destinationProp))
                return;

            var tex = source.GetTexture(sourceProp);
            if (tex == null)
                return;

            destination.SetTexture(destinationProp, tex);
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

        // ── Level helpers (mirrors gameplay presentation helpers) ───────────

        static float GetLevelScale(int level)
        {
            int lvl = Mathf.Clamp(level, 1, 10);
            return 1.55f + lvl * 0.10f;  // 1→1.65, 2→1.75, 3→1.85, 4→1.95 …
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName)) return null;
            if (root.name == childName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null) return found;
            }

            return null;
        }

        static void AddHpBarNotches(Transform barRoot, int level)
        {
            if (level <= 1) return;

            for (int i = 1; i < level; i++)
            {
                float xPos = (float)i / level - 0.5f;

                var notch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                notch.name = "Notch";
                notch.transform.SetParent(barRoot, false);
                notch.transform.localPosition = new Vector3(xPos, 0f, -0.009f);
                notch.transform.localScale    = new Vector3(0.018f, 0.14f, 0.07f);

                var col = notch.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                var rend = notch.GetComponent<Renderer>();
                if (rend != null)
                    rend.material.color = new Color(0.04f, 0.04f, 0.04f, 1f);
            }
        }

    }
}
