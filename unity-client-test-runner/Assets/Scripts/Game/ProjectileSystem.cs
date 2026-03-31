// ProjectileSystem.cs — Renders projectiles from ALL lanes in the ML battlefield.
// Projectiles from the server have a 0..1 progress value; this script
// positions them between source and target tiles in branch world space each frame.
//
// SETUP (Game_ML.unity):
//   Attach alongside LaneRenderer.
//   Inspector:
//     ProjectilePrefab  — small sphere / sprite (used for all types)
//     CannonPrefab      — larger sphere for cannon shots (optional, falls back to ProjectilePrefab)
//     SplashFxPrefab    — instantiated at landing position for cannon (optional)
//     ArcHeight         — how high cannon shots arc (default 1.5 units)
//
// Projectile sync is snapshot-driven (not interpolated between frames).
// Smooth motion between snapshots is achieved by lerping progress in Update.

using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Net;

namespace CastleDefender.Game
{
    public class ProjectileSystem : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────
        [Header("Prefabs")]
        public GameObject ProjectilePrefab;
        public GameObject CannonPrefab;
        public GameObject SplashFxPrefab;

        [Header("Arc")]
        public float ArcHeight = 1.5f;

        // ── Runtime state ─────────────────────────────────────────────────────
        class ProjView
        {
            public GameObject go;
            public Vector3    from;
            public Vector3    to;
            public float      progress;
            public float      smoothProg;
            public bool       isSplash;
            public bool       landedFxPlayed;
            public string     projectileType;
            public string     damageType;
        }

        readonly Dictionary<string, ProjView> _projs = new();
        bool _subscribed;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()  => TrySubscribeSnapshots();

        void OnDisable()
        {
            if (_subscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnSnapshot;
            _subscribed = false;
        }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null) return;

            var seen = new HashSet<string>();

            foreach (var lane in snap.lanes)
            {
                if (lane == null) continue;
                SyncLaneProjectiles(lane, seen);
            }

            // Remove projectiles that disappeared (assumed hit)
            var toRemove = new List<string>();
            foreach (var kv in _projs)
                if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);

            foreach (var id in toRemove)
            {
                var v = _projs[id];

                if (v.isSplash && !v.landedFxPlayed)
                {
                    if (CannonSplash._prefabSet) CannonSplash.Play(v.to);
                    else if (SplashFxPrefab != null)
                    {
                        var fx = Instantiate(SplashFxPrefab, v.to, Quaternion.identity);
                        Destroy(fx, 1.5f);
                    }
                    AudioManager.I?.Play(AudioManager.SFX.CannonSplash);
                }
                else if (!v.isSplash && v.go != null)
                {
                    var hitEffect = HitEffectPool.Get();
                    if (hitEffect != null) hitEffect.Play(v.to, TowerTypeFromString(v.projectileType));
                    AudioManager.I?.Play(HitSFXFor(v.projectileType));
                }

                if (v.go != null) Destroy(v.go);
                _projs.Remove(id);
            }
        }

        void SyncLaneProjectiles(MLLaneSnap lane, HashSet<string> seen)
        {
            if (lane.projectiles == null) return;

            foreach (var p in lane.projectiles)
            {
                seen.Add(p.id);

                if (!_projs.TryGetValue(p.id, out var view) || view.go == null)
                    view = CreateProjectile(p);

                // Use ownerLane for branch-local world positions
                view.from     = TileGrid.TileToWorld(p.ownerLane, p.fromX, p.fromY);
                view.to       = TileGrid.TileToWorld(p.ownerLane, p.toX,   p.toY);
                view.progress = p.progress;
                view.isSplash = p.isSplash;
            }
        }

        ProjView CreateProjectile(MLProjectile p)
        {
            bool isCannon = p.projectileType == "cannon";
            var  prefab   = (isCannon && CannonPrefab != null) ? CannonPrefab : ProjectilePrefab;
            if (prefab == null) return new ProjView();

            var from = TileGrid.TileToWorld(p.ownerLane, p.fromX, p.fromY);
            var to   = TileGrid.TileToWorld(p.ownerLane, p.toX,   p.toY);
            var go   = Instantiate(prefab, from, Quaternion.identity, transform);
            go.name  = $"Proj_{p.id}";

            float scale = p.projectileType switch
            {
                "cannon"   => 0.4f,
                "ballista" => 0.25f,
                "mage"     => 0.2f,
                _          => 0.15f,
            };
            go.transform.localScale = Vector3.one * scale;

            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                rend.material.color = p.damageType switch
                {
                    "MAGIC"  => new Color(0.6f, 0.3f, 1.0f),
                    "PIERCE" => new Color(0.8f, 0.7f, 0.3f),
                    "SPLASH" => new Color(1.0f, 0.5f, 0.1f),
                    "SIEGE"  => new Color(0.4f, 0.4f, 0.5f),
                    _        => new Color(0.8f, 0.8f, 0.8f),
                };
            }

            var view = new ProjView
            {
                go             = go,
                from           = from,
                to             = to,
                progress       = p.progress,
                smoothProg     = p.progress,
                isSplash       = p.isSplash,
                landedFxPlayed = false,
                projectileType = p.projectileType,
                damageType     = p.damageType,
            };
            _projs[p.id] = view;

            AudioManager.I?.Play(ShootSFXFor(p.projectileType));
            return view;
        }

        // ─────────────────────────────────────────────────────────────────────
        void Update()
        {
            TrySubscribeSnapshots();

            const float lerpSpeed = 8f;

            foreach (var kv in _projs)
            {
                var v = kv.Value;
                if (v.go == null) continue;

                v.smoothProg = Mathf.MoveTowards(v.smoothProg, v.progress, lerpSpeed * Time.deltaTime);
                float t = Mathf.Clamp01(v.smoothProg);

                Vector3 pos = Vector3.Lerp(v.from, v.to, t);
                if (v.isSplash) pos.y += ArcHeight * 4f * t * (1f - t);

                v.go.transform.position = pos;

                Vector3 dir = (v.to - v.from).normalized;
                if (dir != Vector3.zero) v.go.transform.forward = dir;
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

        void DestroyAll()
        {
            foreach (var kv in _projs)
                if (kv.Value?.go != null) Destroy(kv.Value.go);
            _projs.Clear();
        }

        // ── Audio / FX helpers ────────────────────────────────────────────────
        static AudioManager.SFX ShootSFXFor(string projType) => projType switch
        {
            "cannon"   => AudioManager.SFX.CannonShoot,
            "ballista" => AudioManager.SFX.BallistaShoot,
            "mage"     => AudioManager.SFX.MageShoot,
            "fighter"  => AudioManager.SFX.FighterSlash,
            _          => AudioManager.SFX.ArcherShoot,
        };

        static AudioManager.SFX HitSFXFor(string projType) => projType switch
        {
            "mage"   => AudioManager.SFX.MageShoot,
            "cannon" => AudioManager.SFX.CannonSplash,
            _        => AudioManager.SFX.UnitDeath,
        };

        static HitEffect.TowerType TowerTypeFromString(string projType) => projType switch
        {
            "archer"   => HitEffect.TowerType.Archer,
            "fighter"  => HitEffect.TowerType.Fighter,
            "mage"     => HitEffect.TowerType.Mage,
            "ballista" => HitEffect.TowerType.Ballista,
            "cannon"   => HitEffect.TowerType.Cannon,
            _          => HitEffect.TowerType.Archer,
        };
    }
}
