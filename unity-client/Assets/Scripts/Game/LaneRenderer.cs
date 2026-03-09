// LaneRenderer.cs — Spawns, moves, and removes unit GameObjects for the full ML battlefield.
// Renders units from ALL lanes simultaneously, positioned in their branch world space.
// Uses normProgress-based dead-reckoning: velocity is estimated from consecutive snapshots
// and units are extrapolated forward each frame so movement is continuous at any framerate,
// not quantized to 10 Hz snapshot steps.
// Separate from TileGrid.cs which handles the viewed branch's tile structures.
//
// SETUP (Game_ML.unity):
//   Attach to any GameObject (e.g. "LaneRenderer" GO).
//   Inspector:
//     Registry       — UnitPrefabRegistry ScriptableObject with key→prefab mappings.
//     HpBarPrefab    — optional world-space Image prefab for HP bars above units.

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public class LaneRenderer : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Unit prefab registry (key → prefab)")]
        public UnitPrefabRegistry Registry;

        [Header("HP bar prefab (optional WorldSpace Canvas Image)")]
        public GameObject HpBarPrefab;

        // ── Runtime state ─────────────────────────────────────────────────────
        class UnitView
        {
            public GameObject go;
            public Transform  hpBarFill;
            public string     typeKey;
            public bool       isMine;
            public int        ownerLane;

            // Dead-reckoning: last known world position + estimated velocity
            public Vector3 worldPos;        // world position at last snapshot
            public Vector3 worldVelocity;   // world units/sec, estimated between snapshots
            public float   timeSinceSnap;   // seconds since last snapshot update
            public bool    hadSnapshot;     // true after the second snapshot (velocity valid)

            // Kept for backward compat / shared-suffix units
            public float normProgress;
        }

        readonly Dictionary<string, UnitView> _units    = new();
        readonly HashSet<string>              _seenIds  = new();
        readonly List<string>                 _toRemove = new();
        float _lastSnapTime = -1f;
        bool  _subscribed;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()  => TrySubscribeSnapshots();

        void OnDisable()
        {
            if (_subscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnSnapshot;
            _subscribed = false;
        }

        void Update()
        {
            TrySubscribeSnapshots();

            float dt = Time.deltaTime;
            foreach (var view in _units.Values)
            {
                if (view?.go == null) continue;
                view.timeSinceSnap += dt;
                // Extrapolate forward using world-space velocity estimated between snapshots.
                // worldVelocity is zeroed for shared-suffix units (they use normProgress).
                if (view.hadSnapshot)
                    view.go.transform.position = view.worldPos + view.worldVelocity * view.timeSinceSnap;
            }
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

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null) return;
            var sa = SnapshotApplier.Instance;

            float now      = Time.time;
            float snapDt   = _lastSnapTime >= 0f ? now - _lastSnapTime : 0.1f;
            _lastSnapTime  = now;
            _seenIds.Clear();

            foreach (var lane in snap.lanes)
            {
                if (lane?.units == null) continue;
                foreach (var u in lane.units)
                {
                    _seenIds.Add(u.id);

                    if (!_units.TryGetValue(u.id, out var view) || view.go == null)
                        view = CreateUnit(u, sa);

                    if (view?.go == null) continue;

                    // Determine world position: use actual tile coords for branch units so
                    // they visually respect wall placement. Fall back to the polyline for
                    // units on the shared suffix (beyond the private build grid).
                    MLLaneSnap ownerLaneSnap = null;
                    foreach (var ls in snap.lanes)
                        if (ls != null && ls.laneIndex == u.ownerLane) { ownerLaneSnap = ls; break; }

                    int branchLen = ownerLaneSnap?.path?.Length ?? 0;
                    bool onBranch = branchLen > 0 && u.pathIdx < branchLen;

                    Vector3 newWorldPos;
                    if (onBranch)
                    {
                        // Place at the exact tile the server says the unit occupies.
                        // Dead-reckoning will extrapolate in world space, matching the
                        // visual path through walls rather than the centerline polyline.
                        newWorldPos = TileGrid.TileToWorld(u.ownerLane, u.gridX, u.gridY);
                    }
                    else
                    {
                        // Shared suffix — no wall geometry here, centerline polyline is fine.
                        newWorldPos = TileGrid.NormProgressToWorld(u.ownerLane, u.normProgress);
                    }

                    // Estimate world-space velocity from position delta between snapshots.
                    if (view.hadSnapshot && snapDt > 0f)
                        view.worldVelocity = (newWorldPos - view.worldPos) / snapDt;
                    else
                        view.worldVelocity = Vector3.zero;

                    view.worldPos      = newWorldPos;
                    view.normProgress  = u.normProgress;
                    view.timeSinceSnap = 0f;
                    view.ownerLane     = u.ownerLane;
                    view.hadSnapshot   = true;

                    if (view.hpBarFill != null && u.maxHp > 0f)
                        view.hpBarFill.localScale = new Vector3(Mathf.Clamp01(u.hp / u.maxHp), 1f, 1f);
                }
            }

            // Remove units that are no longer in any lane's snapshot
            _toRemove.Clear();
            foreach (var kv in _units)
                if (!_seenIds.Contains(kv.Key)) _toRemove.Add(kv.Key);

            foreach (var id in _toRemove)
            {
                var v = _units[id];
                if (v?.go != null)
                {
                    AudioManager.I?.Play(AudioManager.SFX.UnitDeath, 0.4f);
                    Destroy(v.go);
                }
                _units.Remove(id);
            }
        }

        UnitView CreateUnit(MLUnit u, SnapshotApplier sa)
        {
            int  myLane = sa != null ? sa.MyLaneIndex : 0;
            bool isMine = u.ownerLane == myLane;
            bool isAlly = sa != null && sa.AreLanesAllied(u.ownerLane, myLane);

            GameObject prefab = Registry != null ? Registry.GetPrefabForSkin(u.type, u.skinKey) : null;
            if (prefab == null)
            {
                Debug.LogWarning("[LaneRenderer] No prefab for unit type: " + u.type);
                return new UnitView();
            }

            Vector3 spawnPos = TileGrid.NormProgressToWorld(u.ownerLane, u.normProgress);
            var go = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
            go.name = $"Unit_{u.id}_{u.type}_{u.skinKey ?? "default"}";

            float scale = Registry != null ? Registry.GetScaleForSkin(u.type, u.skinKey) : 1f;
            go.transform.localScale = Vector3.one * scale;

            // Wave enemies use a hostile red/orange tint; player-sent units use team colours.
            Color col, rim;
            if (u.isWaveUnit)
            {
                col = new Color(0.90f, 0.30f, 0.10f);  // hostile red-orange
                rim = new Color(1.00f, 0.55f, 0.00f);  // fiery orange rim
            }
            else
            {
                Color fallback = Registry != null
                    ? (isMine ? Registry.GetTintMine(u.type) : Registry.GetTintEnemy(u.type))
                    : (isMine ? new Color(0.20f, 0.80f, 0.70f) : new Color(0.90f, 0.25f, 0.25f));
                col = sa != null ? sa.GetLaneColor(u.ownerLane, fallback) : fallback;
                rim = isMine
                    ? Color.Lerp(col, Color.white, 0.35f)
                    : isAlly
                        ? Color.Lerp(col, Color.white, 0.20f)
                        : Color.Lerp(col, Color.black, 0.25f);
            }

            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null) { bridge.SetBaseColor(col); bridge.SetRimColor(rim); continue; }

                var shared    = r.sharedMaterials;
                var instanced = r.materials;          // per-instance copies
                for (int mi = 0; mi < instanced.Length; mi++)
                {
                    var mat = instanced[mi];
                    if (mat == null) continue;
                    bool isURP = mat.shader != null &&
                                 mat.shader.name.StartsWith("Universal Render Pipeline");
                    if (!isURP && urpLit != null)
                    {
                        var upgraded = new Material(urpLit);
                        var orig = mi < shared.Length ? shared[mi] : null;
                        if (orig != null)
                        {
                            if (orig.HasProperty("_MainTex"))
                            {
                                var tex = orig.GetTexture("_MainTex");
                                if (tex != null) upgraded.SetTexture("_BaseMap", tex);
                            }
                            var baseCol = orig.HasProperty("_Color") ? orig.GetColor("_Color") : Color.white;
                            upgraded.SetColor("_BaseColor", new Color(
                                baseCol.r * col.r, baseCol.g * col.g,
                                baseCol.b * col.b, baseCol.a));
                        }
                        else
                        {
                            upgraded.SetColor("_BaseColor", col);
                        }
                        instanced[mi] = upgraded;
                    }
                    else
                    {
                        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
                        else                               mat.color = col;
                    }
                }
                r.materials = instanced;
            }

            Transform hpFill = null;
            if (HpBarPrefab != null)
            {
                var bar = Instantiate(HpBarPrefab, go.transform);
                bar.transform.localPosition = Vector3.up * (scale * 1.2f + 0.3f);
                hpFill = bar.transform.Find("Fill");
            }

            var view = new UnitView
            {
                go             = go,
                hpBarFill      = hpFill,
                typeKey        = u.type,
                isMine         = isMine,
                ownerLane      = u.ownerLane,
                normProgress   = u.normProgress,
                worldPos       = spawnPos,
                worldVelocity  = Vector3.zero,
                timeSinceSnap  = 0f,
                hadSnapshot    = false,
            };
            _units[u.id] = view;

            AudioManager.I?.Play(AudioManager.SFX.UnitSpawn, isMine ? 0.6f : 0.3f);
            return view;
        }

        void DestroyAll()
        {
            foreach (var kv in _units)
                if (kv.Value?.go != null) Destroy(kv.Value.go);
            _units.Clear();
        }
    }
}
