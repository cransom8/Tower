// ProjectileSystem.cs — Renders projectiles from ALL lanes in the ML battlefield.
// Projectiles from the server have a 0..1 progress value; this script
// resolves their endpoints from authoritative live objects each frame.
//
// SETUP (Game_ML.unity):
//   Attach alongside GameplayPresentationRoot.
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
            public string     id;
            public GameObject go;
            public Vector3    from;
            public Vector3    to;
            public float      progress;
            public float      smoothProg;
            public bool       isSplash;
            public bool       landedFxPlayed;
            public bool       playAudio;
            public string     projectileType;
            public string     damageType;
            public string     sourceId;
            public string     targetId;
        }

        readonly Dictionary<string, ProjView> _projs = new();
        readonly HashSet<string> _loggedResolutionFailures = new();
        bool _subscribed;
        SnapshotApplier _boundSnapshotApplier;

        // ─────────────────────────────────────────────────────────────────────
        void OnEnable()  => TrySubscribeSnapshots();

        void OnDisable()
        {
            if (_boundSnapshotApplier != null)
                _boundSnapshotApplier.OnMLSnapshotApplied -= OnSnapshot;
            _boundSnapshotApplier = null;
            _subscribed = false;
            DestroyAll();
        }

        // ─────────────────────────────────────────────────────────────────────
        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null) return;

            var seen = new HashSet<string>();
            int localLaneIndex = ResolveLocalLaneIndex();

            foreach (var lane in snap.lanes)
            {
                if (lane == null) continue;
                SyncLaneProjectiles(snap, lane, seen, localLaneIndex);
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
                    if (v.playAudio)
                        AudioManager.I?.Play(AudioManager.SFX.CannonSplash);
                }
                else if (!v.isSplash && v.go != null)
                {
                    var hitEffect = HitEffectPool.Get();
                    if (hitEffect != null) hitEffect.Play(v.to, TowerTypeFromString(v.projectileType, v.damageType));
                    if (v.playAudio)
                    {
                        TryPlayProjectileCombatSfx(v.id, v.projectileType, v.damageType, UnitCombatSfxCue.Impact, 0.22f, HitSFXFor(v.projectileType, v.damageType));
                    }
                }

                if (v.go != null) Destroy(v.go);
                _projs.Remove(id);
            }
        }

        void SyncLaneProjectiles(MLSnapshot snap, MLLaneSnap lane, HashSet<string> seen, int localLaneIndex)
        {
            if (lane.projectiles == null) return;

            foreach (var p in lane.projectiles)
            {
                seen.Add(p.id);
                bool playAudio = ShouldPlayProjectileAudio(snap, p, localLaneIndex);

                if (!TryResolveProjectileEndpointWorldPosition(p, lane, resolveSource: true, out Vector3 fromWorld, out string sourceFailure))
                {
                    LogProjectileResolutionFailure(p, lane, "source", sourceFailure);
                    DestroyProjectileView(p.id);
                    continue;
                }

                if (!TryResolveProjectileEndpointWorldPosition(p, lane, resolveSource: false, out Vector3 toWorld, out string targetFailure))
                {
                    LogProjectileResolutionFailure(p, lane, "target", targetFailure);
                    DestroyProjectileView(p.id);
                    continue;
                }

                if (!_projs.TryGetValue(p.id, out var view) || view.go == null)
                    view = CreateProjectile(p, fromWorld, toWorld, playAudio);

                if (view == null || view.go == null)
                    continue;

                view.from     = fromWorld;
                view.to       = toWorld;
                view.progress = p.progress;
                view.isSplash = p.isSplash;
                view.playAudio = playAudio;
                view.sourceId = p.sourceId;
                view.targetId = p.targetId;
            }
        }

        ProjView CreateProjectile(MLProjectile p, Vector3 from, Vector3 to, bool playAudio)
        {
            bool isCannon = p.projectileType == "cannon";
            var  prefab   = (isCannon && CannonPrefab != null) ? CannonPrefab : ProjectilePrefab;
            if (prefab == null) return new ProjView();

            var go   = Instantiate(prefab, from, Quaternion.identity, transform);
            go.name  = $"Proj_{p.id}";

            string family = ResolveProjectileFamily(p.projectileType, p.damageType);
            float scale = family switch
            {
                "cannon"   => 0.4f,
                "ballista" => 0.25f,
                "mage"     => 0.2f,
                "support"  => 0.22f,
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
                id             = p.id,
                go             = go,
                from           = from,
                to             = to,
                progress       = p.progress,
                smoothProg     = p.progress,
                isSplash       = p.isSplash,
                landedFxPlayed = false,
                playAudio      = playAudio,
                projectileType = p.projectileType,
                damageType     = p.damageType,
                sourceId       = p.sourceId,
                targetId       = p.targetId,
            };
            _projs[p.id] = view;

            if (playAudio)
            {
                TryPlayProjectileCombatSfx(p.id, p.projectileType, p.damageType, UnitCombatSfxCue.Attack, 0.24f, ShootSFXFor(p.projectileType, p.damageType));
            }
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
            var sa = SnapshotApplier.Instance;
            if (_subscribed && _boundSnapshotApplier == sa && sa != null) return;

            if (_boundSnapshotApplier != null)
            {
                _boundSnapshotApplier.OnMLSnapshotApplied -= OnSnapshot;
                _boundSnapshotApplier = null;
                _subscribed = false;
            }

            if (sa == null) return;

            sa.OnMLSnapshotApplied -= OnSnapshot;
            sa.OnMLSnapshotApplied += OnSnapshot;
            _boundSnapshotApplier = sa;
            _subscribed = true;

            if (sa.LatestML != null) OnSnapshot(sa.LatestML);
        }

        int ResolveLocalLaneIndex()
        {
            if (_boundSnapshotApplier != null)
                return _boundSnapshotApplier.MyLaneIndex;
            if (SnapshotApplier.Instance != null)
                return SnapshotApplier.Instance.MyLaneIndex;
            return NetworkManager.Instance != null ? NetworkManager.Instance.MyLaneIndex : -1;
        }

        void DestroyAll()
        {
            foreach (var kv in _projs)
                if (kv.Value?.go != null) Destroy(kv.Value.go);
            _projs.Clear();
        }

        bool TryResolveProjectileEndpointWorldPosition(
            MLProjectile projectile,
            MLLaneSnap lane,
            bool resolveSource,
            out Vector3 worldPos,
            out string failureReason)
        {
            worldPos = default;
            failureReason = null;

            if (projectile == null)
            {
                failureReason = "projectile payload is null";
                return false;
            }

            string endpointId = resolveSource ? projectile.sourceId : projectile.targetId;
            if (string.IsNullOrWhiteSpace(endpointId))
            {
                failureReason = resolveSource ? "sourceId is missing" : "targetId is missing";
                return false;
            }

            if (LaneSnapshotCombatant.TryResolveWorldPosition(endpointId, out worldPos))
                return true;

            if (TryResolveFortressPadWorldPosition(endpointId, lane, out worldPos))
                return true;

            failureReason =
                $"authoritative endpoint '{endpointId}' was not found for " +
                $"{(resolveSource ? "source" : "target")} kind='{projectile.sourceKind ?? "<null>"}'";
            return false;
        }

        static bool TryResolveFortressPadWorldPosition(string padId, MLLaneSnap lane, out Vector3 worldPos)
        {
            worldPos = default;
            if (string.IsNullOrWhiteSpace(padId))
                return false;

            var anchor = FortressPadAnchor.FindAnchor(padId, lane?.slotColor, lane?.laneIndex ?? -1);
            if (anchor == null)
                return false;

            Transform focus = anchor.FocusTransform != null ? anchor.FocusTransform : anchor.transform;
            if (focus == null)
                return false;

            worldPos = focus.position;
            return true;
        }

        void LogProjectileResolutionFailure(MLProjectile projectile, MLLaneSnap lane, string endpointKind, string failureReason)
        {
            string key = $"{projectile?.id ?? "<null>"}:{endpointKind}:{failureReason}";
            if (!_loggedResolutionFailures.Add(key))
                return;

            Debug.LogError(
                $"[ProjectileSystem] Failed to resolve authoritative {endpointKind} endpoint " +
                $"for projectile='{projectile?.id ?? "<null>"}' lane={lane?.laneIndex ?? -1} " +
                $"sourceKind='{projectile?.sourceKind ?? "<null>"}' sourceId='{projectile?.sourceId ?? "<null>"}' " +
                $"targetId='{projectile?.targetId ?? "<null>"}' reason='{failureReason ?? "<unknown>"}'.",
                this);
        }

        void DestroyProjectileView(string projectileId)
        {
            if (string.IsNullOrWhiteSpace(projectileId))
                return;

            if (!_projs.TryGetValue(projectileId, out var view))
                return;

            if (view?.go != null)
                Destroy(view.go);

            _projs.Remove(projectileId);
        }

        static bool ShouldPlayProjectileAudio(MLSnapshot snap, MLProjectile projectile, int localLaneIndex)
        {
            if (projectile == null || localLaneIndex < 0)
                return false;

            if (string.Equals(projectile.sourceKind, "unit", System.StringComparison.OrdinalIgnoreCase)
                && TryFindSnapshotUnit(snap, projectile.sourceId, out MLUnit sourceUnit))
            {
                return ShouldPlayUnitAudioForLocalLane(sourceUnit, localLaneIndex);
            }

            return projectile.ownerLane == localLaneIndex;
        }

        static bool TryFindSnapshotUnit(MLSnapshot snap, string unitId, out MLUnit unit)
        {
            unit = null;
            if (snap?.lanes == null || string.IsNullOrWhiteSpace(unitId))
                return false;

            for (int laneIndex = 0; laneIndex < snap.lanes.Length; laneIndex++)
            {
                MLLaneSnap lane = snap.lanes[laneIndex];
                if (lane?.units == null)
                    continue;

                for (int unitIndex = 0; unitIndex < lane.units.Length; unitIndex++)
                {
                    MLUnit candidate = lane.units[unitIndex];
                    if (candidate != null && string.Equals(candidate.id, unitId, System.StringComparison.Ordinal))
                    {
                        unit = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        static bool ShouldPlayUnitAudioForLocalLane(MLUnit unit, int localLaneIndex)
        {
            if (unit == null || localLaneIndex < 0)
                return false;

            if (IsDungeonWaveUnit(unit))
                return ResolveDungeonAudioLaneIndex(unit) == localLaneIndex;

            return ResolveOwnedAudioLaneIndex(unit) == localLaneIndex;
        }

        static bool IsDungeonWaveUnit(MLUnit unit)
        {
            if (unit == null)
                return false;
            if (unit.isWaveUnit)
                return true;

            string explicitAllegiance = unit.allegianceKey != null
                ? unit.allegianceKey.Trim().ToLowerInvariant()
                : null;
            if (string.Equals(explicitAllegiance, "dungeon", System.StringComparison.Ordinal))
                return true;

            string spawnSourceType = unit.spawnSourceType != null
                ? unit.spawnSourceType.Trim().ToLowerInvariant()
                : null;
            return string.Equals(spawnSourceType, "scheduled_wave", System.StringComparison.Ordinal)
                || string.Equals(spawnSourceType, "dungeon_wave", System.StringComparison.Ordinal);
        }

        static int ResolveOwnedAudioLaneIndex(MLUnit unit)
        {
            if (unit == null)
                return -1;
            if (unit.ownerLaneIndex >= 0)
                return unit.ownerLaneIndex;
            if (unit.ownerLane >= 0)
                return unit.ownerLane;
            return unit.sourceLaneIndex >= 0 ? unit.sourceLaneIndex : -1;
        }

        static int ResolveDungeonAudioLaneIndex(MLUnit unit)
        {
            if (unit == null)
                return -1;
            if (unit.targetLaneIndex >= 0)
                return unit.targetLaneIndex;
            if (unit.laneId >= 0)
                return unit.laneId;
            return unit.objectiveLaneIndex >= 0 ? unit.objectiveLaneIndex : -1;
        }

        static void TryPlayProjectileCombatSfx(
            string projectileId,
            string projectileType,
            string damageType,
            UnitCombatSfxCue cue,
            float volumeScale,
            AudioManager.SFX legacyFallback)
        {
            UnitCombatSfxPlaybackResult result = UnitCombatSfxLibrary.TryPlay(
                UnitCombatSfxLibrary.ResolveForProjectile(projectileType, damageType),
                projectileId,
                cue,
                Time.time,
                volumeScale);
            if (!ShouldFallbackToLegacyCombatSfx(result))
                return;

            AudioManager.I?.Play(legacyFallback, volumeScale);
        }

        static bool ShouldFallbackToLegacyCombatSfx(UnitCombatSfxPlaybackResult result)
        {
            return result == UnitCombatSfxPlaybackResult.MissingProfile
                || result == UnitCombatSfxPlaybackResult.MissingClips;
        }

        // ── Audio / FX helpers ────────────────────────────────────────────────
        static AudioManager.SFX ShootSFXFor(string projType, string damageType = null) => ResolveProjectileFamily(projType, damageType) switch
        {
            "cannon"   => AudioManager.SFX.CannonShoot,
            "ballista" => AudioManager.SFX.BallistaShoot,
            "mage"     => AudioManager.SFX.MageShoot,
            "support"  => AudioManager.SFX.MageShoot,
            "fighter"  => AudioManager.SFX.FighterSlash,
            _          => AudioManager.SFX.ArcherShoot,
        };

        static AudioManager.SFX HitSFXFor(string projType, string damageType = null) => ResolveProjectileFamily(projType, damageType) switch
        {
            "mage"    => AudioManager.SFX.MageShoot,
            "support" => AudioManager.SFX.MageShoot,
            "cannon"  => AudioManager.SFX.CannonSplash,
            _         => AudioManager.SFX.UnitDeath,
        };

        static HitEffect.TowerType TowerTypeFromString(string projType, string damageType = null) => ResolveProjectileFamily(projType, damageType) switch
        {
            "archer"   => HitEffect.TowerType.Archer,
            "fighter"  => HitEffect.TowerType.Fighter,
            "mage"     => HitEffect.TowerType.Mage,
            "support"  => HitEffect.TowerType.Ballista,
            "ballista" => HitEffect.TowerType.Ballista,
            "cannon"   => HitEffect.TowerType.Cannon,
            _          => HitEffect.TowerType.Archer,
        };

        static string ResolveProjectileFamily(string projType, string damageType)
        {
            string token = string.IsNullOrWhiteSpace(projType)
                ? string.Empty
                : projType.Trim().ToLowerInvariant();
            string damage = string.IsNullOrWhiteSpace(damageType)
                ? string.Empty
                : damageType.Trim().ToUpperInvariant();

            if (token == "cannon" || damage == "SPLASH")
                return "cannon";
            if (token == "ballista" || token.Contains("ballista") || damage == "SIEGE")
                return "ballista";
            if (token == "mage" || token.Contains("mage") || token.Contains("wizard") || token.Contains("arcane") || token.Contains("thaum"))
                return "mage";
            if (token.Contains("priest") || token.Contains("cleric") || token.Contains("bishop") || token.Contains("support"))
                return "support";
            if (token == "fighter"
                || token.Contains("shield")
                || token.Contains("sword")
                || token.Contains("spear")
                || token.Contains("halber")
                || token.Contains("knight")
                || token.Contains("guardian")
                || token.Contains("militia"))
            {
                return "fighter";
            }
            if (token == "archer"
                || token.Contains("archer")
                || token.Contains("crossbow")
                || token.Contains("ranger")
                || token.Contains("scout")
                || damage == "PIERCE")
            {
                return "archer";
            }
            if (damage == "MAGIC")
                return "mage";
            return "archer";
        }
    }
}
