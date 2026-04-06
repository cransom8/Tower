// ProjectileSystem.cs - Renders authoritative projectile snapshots and
// lightweight client-side presentation shots for ranged/support attacks.

using System.Collections.Generic;
using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    public class ProjectileSystem : MonoBehaviour
    {
        [Header("Prefabs")]
        public GameObject ProjectilePrefab;
        public GameObject CannonPrefab;
        public GameObject SplashFxPrefab;

        [Header("Arc")]
        public float ArcHeight = 1.5f;

        public static ProjectileSystem Instance { get; private set; }

        class ProjView
        {
            public string id;
            public GameObject go;
            public Vector3 from;
            public Vector3 to;
            public float progress;
            public float smoothProg;
            public bool isSplash;
            public bool landedFxPlayed;
            public bool playAudio;
            public bool authoritative;
            public float manualDuration;
            public float manualElapsed;
            public string projectileType;
            public string damageType;
            public string sourceId;
            public string targetId;
        }

        readonly Dictionary<string, ProjView> _projs = new();
        readonly HashSet<string> _loggedResolutionFailures = new();
        readonly List<string> _completedProjectiles = new();
        readonly Queue<GameObject> _projPool = new Queue<GameObject>(16);
        readonly Queue<GameObject> _cannonPool = new Queue<GameObject>(8);
        MaterialPropertyBlock _materialBlock;
        Material _fallbackProjectileMaterial;
        bool _subscribed;
        SnapshotApplier _boundSnapshotApplier;

        void Awake()
        {
            Instance = this;
            _materialBlock = new MaterialPropertyBlock();
        }

        void OnEnable()
        {
            Instance = this;
            TrySubscribeSnapshots();
        }

        void OnDisable()
        {
            if (Instance == this)
                Instance = null;

            if (_boundSnapshotApplier != null)
                _boundSnapshotApplier.OnMLSnapshotApplied -= OnSnapshot;
            _boundSnapshotApplier = null;
            _subscribed = false;
            DestroyAll();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_fallbackProjectileMaterial != null)
                Destroy(_fallbackProjectileMaterial);
        }

        public bool SpawnPresentationProjectile(
            string projectileId,
            string projectileType,
            string damageType,
            Vector3 fromWorld,
            Vector3 toWorld,
            float travelSeconds,
            bool isSplash = false,
            bool playAudio = false)
        {
            if (!isActiveAndEnabled)
                return false;

            string resolvedId = !string.IsNullOrWhiteSpace(projectileId)
                ? projectileId.Trim()
                : $"client_proj_{Time.frameCount}_{_projs.Count}";
            DestroyProjectileView(resolvedId);

            ProjView view = CreateProjectileView(
                resolvedId,
                projectileType,
                damageType,
                fromWorld,
                toWorld,
                isSplash,
                playAudio,
                sourceId: null,
                targetId: null,
                authoritative: false,
                initialProgress: 0f);
            if (view == null || view.go == null)
                return false;

            view.manualDuration = Mathf.Max(0.06f, travelSeconds);
            view.manualElapsed = 0f;
            view.progress = 0f;
            view.smoothProg = 0f;
            return true;
        }

        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null)
                return;

            var seen = new HashSet<string>();
            int localLaneIndex = ResolveLocalLaneIndex();

            foreach (MLLaneSnap lane in snap.lanes)
            {
                if (lane == null)
                    continue;

                SyncLaneProjectiles(snap, lane, seen, localLaneIndex);
            }

            _completedProjectiles.Clear();
            foreach (var kv in _projs)
            {
                if (kv.Value == null)
                {
                    _completedProjectiles.Add(kv.Key);
                    continue;
                }

                if (kv.Value.authoritative && !seen.Contains(kv.Key))
                    _completedProjectiles.Add(kv.Key);
            }

            for (int i = 0; i < _completedProjectiles.Count; i++)
                CompleteProjectileView(_completedProjectiles[i]);
        }

        void SyncLaneProjectiles(MLSnapshot snap, MLLaneSnap lane, HashSet<string> seen, int localLaneIndex)
        {
            if (lane.projectiles == null)
                return;

            foreach (MLProjectile projectile in lane.projectiles)
            {
                seen.Add(projectile.id);
                bool playAudio = ShouldPlayProjectileAudio(snap, projectile, localLaneIndex);

                bool hasExistingAuthoritativeView =
                    _projs.TryGetValue(projectile.id, out ProjView existingView)
                    && existingView != null
                    && existingView.go != null
                    && existingView.authoritative;

                if (!TryResolveProjectileEndpointWorldPosition(projectile, lane, true, out Vector3 fromWorld, out string sourceFailure))
                {
                    LogProjectileResolutionFailure(projectile, lane, "source", sourceFailure);
                    if (!hasExistingAuthoritativeView)
                        DestroyProjectileView(projectile.id);
                    else
                        existingView.progress = projectile.progress;
                    continue;
                }

                if (!TryResolveProjectileEndpointWorldPosition(projectile, lane, false, out Vector3 toWorld, out string targetFailure))
                {
                    LogProjectileResolutionFailure(projectile, lane, "target", targetFailure);
                    if (!hasExistingAuthoritativeView)
                        DestroyProjectileView(projectile.id);
                    else
                        existingView.progress = projectile.progress;
                    continue;
                }

                if (!hasExistingAuthoritativeView)
                    existingView = CreateProjectile(projectile, fromWorld, toWorld, playAudio);

                if (existingView == null || existingView.go == null)
                    continue;

                existingView.from = fromWorld;
                existingView.to = toWorld;
                existingView.progress = projectile.progress;
                existingView.isSplash = projectile.isSplash;
                existingView.playAudio = playAudio;
                existingView.projectileType = projectile.projectileType;
                existingView.damageType = projectile.damageType;
                existingView.sourceId = projectile.sourceId;
                existingView.targetId = projectile.targetId;
            }
        }

        ProjView CreateProjectile(MLProjectile projectile, Vector3 from, Vector3 to, bool playAudio)
        {
            return CreateProjectileView(
                projectile.id,
                projectile.projectileType,
                projectile.damageType,
                from,
                to,
                projectile.isSplash,
                playAudio,
                projectile.sourceId,
                projectile.targetId,
                authoritative: true,
                initialProgress: projectile.progress);
        }

        ProjView CreateProjectileView(
            string id,
            string projectileType,
            string damageType,
            Vector3 from,
            Vector3 to,
            bool isSplash,
            bool playAudio,
            string sourceId,
            string targetId,
            bool authoritative,
            float initialProgress)
        {
            bool isCannon = string.Equals(projectileType, "cannon", System.StringComparison.OrdinalIgnoreCase);
            GameObject prefab = (isCannon && CannonPrefab != null) ? CannonPrefab : ProjectilePrefab;
            if (prefab == null)
                return null;

            Queue<GameObject> pool = isCannon ? _cannonPool : _projPool;
            GameObject go;
            if (pool.Count > 0)
            {
                go = pool.Dequeue();
                go.transform.SetPositionAndRotation(from, Quaternion.identity);
                go.SetActive(true);
            }
            else
            {
                go = Instantiate(prefab, from, Quaternion.identity, transform);
            }
            go.name = $"Proj_{id}";

            string family = ResolveProjectileFamily(projectileType, damageType);
            float scale = family switch
            {
                "cannon" => 0.4f,
                "ballista" => 0.25f,
                "mage" => 0.2f,
                "support" => 0.24f,
                _ => 0.15f,
            };
            go.transform.localScale = Vector3.one * scale;

            ApplyProjectileVisuals(go.GetComponentInChildren<Renderer>(), ResolveProjectileColor(projectileType, damageType));

            var view = new ProjView
            {
                id = id,
                go = go,
                from = from,
                to = to,
                progress = initialProgress,
                smoothProg = initialProgress,
                isSplash = isSplash,
                landedFxPlayed = false,
                playAudio = playAudio,
                authoritative = authoritative,
                manualDuration = 0f,
                manualElapsed = 0f,
                projectileType = projectileType,
                damageType = damageType,
                sourceId = sourceId,
                targetId = targetId,
            };
            _projs[id] = view;

            if (playAudio)
            {
                TryPlayProjectileCombatSfx(
                    id,
                    projectileType,
                    damageType,
                    UnitCombatSfxCue.Attack,
                    0.24f,
                    ShootSFXFor(projectileType, damageType));
            }

            return view;
        }

        void ApplyProjectileVisuals(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            if (_materialBlock == null)
                _materialBlock = new MaterialPropertyBlock();

            EnsureFallbackProjectileMaterial(renderer);
            Material material = renderer.sharedMaterial;
            if (material == null)
                return;

            _materialBlock.Clear();
            if (material.HasProperty("_BaseColor"))
                _materialBlock.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                _materialBlock.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
                _materialBlock.SetColor("_EmissionColor", color * 0.18f);
            renderer.SetPropertyBlock(_materialBlock);
        }

        void EnsureFallbackProjectileMaterial(Renderer renderer)
        {
            if (renderer == null || renderer.sharedMaterial != null)
                return;

            if (_fallbackProjectileMaterial == null)
            {
                Shader shader = ResolveFallbackProjectileShader();
                if (shader == null)
                    return;

                _fallbackProjectileMaterial = new Material(shader)
                {
                    name = "ProjectileFallbackMaterial",
                };
            }

            renderer.sharedMaterial = _fallbackProjectileMaterial;
        }

        static Shader ResolveFallbackProjectileShader()
        {
            string[] shaderNames =
            {
                "Universal Render Pipeline/Unlit",
                "Universal Render Pipeline/Lit",
                "Sprites/Default",
                "Standard",
            };

            for (int i = 0; i < shaderNames.Length; i++)
            {
                Shader shader = Shader.Find(shaderNames[i]);
                if (shader != null)
                    return shader;
            }

            return null;
        }

        static Color ResolveProjectileColor(string projectileType, string damageType)
        {
            switch (ResolveProjectileFamily(projectileType, damageType))
            {
                case "mage":
                    return new Color(0.42f, 0.86f, 1.00f);
                case "support":
                    return new Color(0.58f, 1.00f, 0.68f);
                case "cannon":
                    return new Color(1.00f, 0.52f, 0.14f);
                case "ballista":
                    return new Color(0.78f, 0.70f, 0.34f);
                case "fighter":
                    return new Color(0.95f, 0.80f, 0.30f);
                case "archer":
                    return new Color(0.95f, 0.88f, 0.50f);
            }

            return damageType switch
            {
                "MAGIC" => new Color(0.6f, 0.3f, 1.0f),
                "PIERCE" => new Color(0.8f, 0.7f, 0.3f),
                "SPLASH" => new Color(1.0f, 0.5f, 0.1f),
                "SIEGE" => new Color(0.4f, 0.4f, 0.5f),
                _ => new Color(0.8f, 0.8f, 0.8f),
            };
        }

        void Update()
        {
            TrySubscribeSnapshots();

            const float lerpSpeed = 8f;
            _completedProjectiles.Clear();

            foreach (var kv in _projs)
            {
                ProjView view = kv.Value;
                if (view?.go == null)
                {
                    _completedProjectiles.Add(kv.Key);
                    continue;
                }

                if (view.authoritative)
                {
                    view.smoothProg = Mathf.MoveTowards(view.smoothProg, view.progress, lerpSpeed * Time.deltaTime);
                }
                else
                {
                    float duration = Mathf.Max(0.06f, view.manualDuration);
                    view.manualElapsed += Time.deltaTime;
                    view.progress = Mathf.Clamp01(view.manualElapsed / duration);
                    view.smoothProg = view.progress;
                    if (view.progress >= 0.999f)
                        _completedProjectiles.Add(kv.Key);
                }

                float t = Mathf.Clamp01(view.smoothProg);
                Vector3 pos = Vector3.Lerp(view.from, view.to, t);
                if (view.isSplash)
                    pos.y += ArcHeight * 4f * t * (1f - t);

                view.go.transform.position = pos;

                Vector3 dir = (view.to - view.from).normalized;
                if (dir != Vector3.zero)
                    view.go.transform.forward = dir;
            }

            for (int i = 0; i < _completedProjectiles.Count; i++)
                CompleteProjectileView(_completedProjectiles[i]);
        }

        void CompleteProjectileView(string projectileId)
        {
            if (string.IsNullOrWhiteSpace(projectileId) || !_projs.TryGetValue(projectileId, out ProjView view) || view == null)
                return;

            if (view.isSplash && !view.landedFxPlayed)
            {
                if (CannonSplash._prefabSet)
                {
                    CannonSplash.Play(view.to);
                }
                else if (SplashFxPrefab != null)
                {
                    var fx = Instantiate(SplashFxPrefab, view.to, Quaternion.identity);
                    Destroy(fx, 1.5f);
                }

                if (view.playAudio)
                    AudioManager.I?.Play(AudioManager.SFX.CannonSplash);

                view.landedFxPlayed = true;
            }
            else if (!view.isSplash && view.go != null)
            {
                var hitEffect = HitEffectPool.Get();
                if (hitEffect != null)
                    hitEffect.Play(view.to, TowerTypeFromString(view.projectileType, view.damageType));

                if (view.playAudio)
                {
                    TryPlayProjectileCombatSfx(
                        view.id,
                        view.projectileType,
                        view.damageType,
                        UnitCombatSfxCue.Impact,
                        0.22f,
                        HitSFXFor(view.projectileType, view.damageType));
                }
            }

            if (view.go != null)
                ReturnProjectileToPool(view.go, view.projectileType);
            _projs.Remove(projectileId);
        }

        void ReturnProjectileToPool(GameObject go, string projectileType)
        {
            go.SetActive(false);
            bool isCannon = string.Equals(projectileType, "cannon", System.StringComparison.OrdinalIgnoreCase);
            (isCannon ? _cannonPool : _projPool).Enqueue(go);
        }

        void TrySubscribeSnapshots()
        {
            SnapshotApplier sa = SnapshotApplier.Instance;
            if (_subscribed && _boundSnapshotApplier == sa && sa != null)
                return;

            if (_boundSnapshotApplier != null)
            {
                _boundSnapshotApplier.OnMLSnapshotApplied -= OnSnapshot;
                _boundSnapshotApplier = null;
                _subscribed = false;
            }

            if (sa == null)
                return;

            sa.OnMLSnapshotApplied -= OnSnapshot;
            sa.OnMLSnapshotApplied += OnSnapshot;
            _boundSnapshotApplier = sa;
            _subscribed = true;

            if (sa.LatestML != null)
                OnSnapshot(sa.LatestML);
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
            {
                if (kv.Value?.go != null)
                    ReturnProjectileToPool(kv.Value.go, kv.Value.projectileType);
            }

            _projs.Clear();
            _completedProjectiles.Clear();
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

            FortressPadAnchor anchor = FortressPadAnchor.FindAnchor(padId, lane?.slotColor, lane?.laneIndex ?? -1);
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

            if (!_projs.TryGetValue(projectileId, out ProjView view))
                return;

            if (view?.go != null)
                ReturnProjectileToPool(view.go, view.projectileType);

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

        static AudioManager.SFX ShootSFXFor(string projType, string damageType = null) => ResolveProjectileFamily(projType, damageType) switch
        {
            "cannon" => AudioManager.SFX.CannonShoot,
            "ballista" => AudioManager.SFX.BallistaShoot,
            "mage" => AudioManager.SFX.MageShoot,
            "support" => AudioManager.SFX.MageShoot,
            "fighter" => AudioManager.SFX.FighterSlash,
            _ => AudioManager.SFX.ArcherShoot,
        };

        static AudioManager.SFX HitSFXFor(string projType, string damageType = null) => ResolveProjectileFamily(projType, damageType) switch
        {
            "mage" => AudioManager.SFX.MageShoot,
            "support" => AudioManager.SFX.MageShoot,
            "cannon" => AudioManager.SFX.CannonSplash,
            _ => AudioManager.SFX.UnitDeath,
        };

        static HitEffect.TowerType TowerTypeFromString(string projType, string damageType = null) => ResolveProjectileFamily(projType, damageType) switch
        {
            "archer" => HitEffect.TowerType.Archer,
            "fighter" => HitEffect.TowerType.Fighter,
            "mage" => HitEffect.TowerType.Mage,
            "support" => HitEffect.TowerType.Mage,
            "ballista" => HitEffect.TowerType.Ballista,
            "cannon" => HitEffect.TowerType.Cannon,
            _ => HitEffect.TowerType.Archer,
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
