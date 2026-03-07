// LaneRenderer.cs — Spawns, moves, and removes unit GameObjects for the viewed ML lane.
// Interpolates positions between 10hz snapshots using Update-based Lerp (no DOTween).
// Separate from TileGrid.cs which handles tile structures only.
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

        // Time to lerp from one tile position to the next (slightly > snapshot period)
        const float MoveLerpDuration = 0.14f;

        // ── Runtime state ─────────────────────────────────────────────────────
        class UnitView
        {
            public GameObject go;
            public Transform  hpBarFill;
            public string     typeKey;
            public bool       isMine;
            public Vector3    moveFrom;
            public Vector3    moveTo;
            public float      lerpT;
        }

        readonly Dictionary<string, UnitView> _units = new();
        int  _lastViewingLane = -1;
        bool _subscribed;

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
                view.lerpT = Mathf.Min(1f, view.lerpT + dt / MoveLerpDuration);
                view.go.transform.position = Vector3.Lerp(view.moveFrom, view.moveTo, view.lerpT);
            }
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

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            var sa      = SnapshotApplier.Instance;
            int viewing = sa.ViewingLane;

            if (viewing != _lastViewingLane)
            {
                DestroyAll();
                _lastViewingLane = viewing;
            }

            if (snap?.lanes == null || viewing >= snap.lanes.Length) return;
            SyncUnits(snap.lanes[viewing], sa.MyLaneIndex);
        }

        void SyncUnits(MLLaneSnap lane, int myLaneIndex)
        {
            var seen = new HashSet<string>();

            if (lane.units != null)
            {
                foreach (var u in lane.units)
                {
                    seen.Add(u.id);

                    if (!_units.TryGetValue(u.id, out var view) || view.go == null)
                        view = CreateUnit(u, myLaneIndex);

                    if (view?.go == null) continue;
                    Vector3 target = TileGrid.TileToWorld(u.gridX, u.gridY);
                    view.moveFrom = view.go.transform.position;
                    view.moveTo   = target;
                    view.lerpT    = 0f;

                    if (view.hpBarFill != null && u.maxHp > 0f)
                        view.hpBarFill.localScale = new Vector3(
                            Mathf.Clamp01(u.hp / u.maxHp), 1f, 1f);
                }
            }

            var toRemove = new List<string>();
            foreach (var kv in _units)
                if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);

            foreach (var id in toRemove)
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

        UnitView CreateUnit(MLUnit u, int myLaneIndex)
        {
            bool isMine = u.ownerLane == myLaneIndex;

            GameObject prefab = Registry != null ? Registry.GetPrefab(u.type) : null;
            if (prefab == null)
            {
                Debug.LogWarning("[LaneRenderer] No prefab for unit type: " + u.type);
                return new UnitView();
            }

            Vector3 spawnPos = TileGrid.TileToWorld(u.gridX, u.gridY);
            var go = Instantiate(prefab, spawnPos, Quaternion.identity, transform);
            go.name = $"Unit_{u.id}_{u.type}";

            float scale = Registry != null ? Registry.GetScale(u.type) : 1f;
            go.transform.localScale = Vector3.one * scale;

            Color col = Registry != null
                ? (isMine ? Registry.GetTintMine(u.type) : Registry.GetTintEnemy(u.type))
                : (isMine ? new Color(0.20f, 0.80f, 0.70f) : new Color(0.90f, 0.25f, 0.25f));
            Color rim = isMine
                ? new Color(0.2f, 0.9f, 0.8f)
                : new Color(1.0f, 0.2f, 0.2f);

            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var bridge = r.GetComponent<ToonShaderBridge>();
                if (bridge != null)
                {
                    bridge.SetBaseColor(col);
                    bridge.SetRimColor(rim);
                }
                else
                {
                    r.material.color = col;
                }
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
                go        = go,
                hpBarFill = hpFill,
                typeKey   = u.type,
                isMine    = isMine,
                moveFrom  = spawnPos,
                moveTo    = spawnPos,
                lerpT     = 1f,
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
