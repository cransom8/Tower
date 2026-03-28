using System;
using System.Collections.Generic;
using CastleDefender.Net;
using CastleDefender.UI;
using UnityEngine;

namespace CastleDefender.Game
{
    [DefaultExecutionOrder(-85)]
    public class WaveSnapshotRuntimeSpawner : MonoBehaviour
    {
        const float TargetFollowSharpness = 18f;
        const float AttackVisualHoldSeconds = 0.32f;
        const float GroundProbeHeight = 32f;
        const float GroundProbeDistance = 96f;
        const float GroundClearance = 0.04f;
        const float GroundNormalThreshold = 0.35f;
        const float PerimeterRouteControlScale = 0.28f;
        const float FrontGateForwardOffset = 10.2f;
        const float CombatProjectionForwardSlack = 4.5f;
        const float CombatProjectionRearSlack = 8f;
        const float CombatProjectionLateralLimit = 10f;
        const float WaveSpawnDistance = 25f;
        const string RouteMineNode = "M";
        const string RouteWaveNodeA = "WA";
        const string RouteWaveNodeB = "WB";
        const string RouteWaveNodeC = "WC";
        const string RouteWaveNodeD = "WD";
        const string RouteNodeA = "A";
        const string RouteNodeB = "B";
        const string RouteNodeC = "C";
        const string RouteNodeD = "D";
        const string BarracksRouteNodeCenterSuffix = "CTR";
        const string BarracksRouteNodeLeftSuffix = "LFT";
        const string BarracksRouteNodeRightSuffix = "RGT";
        const float CombatFacingTurnSharpness = 20f;
        static readonly string[] MoveStates = { "Run", "Walk", "run", "walk" };
        static readonly string[] IdleStates = { "Idle", "IdleNormal", "IdleCombat", "idle" };
        static readonly string[] DefaultAttackStates = { "Attack1", "Attack2", "Attack", "attack" };
        static readonly string[] MeleeAttackStates =
        {
            "AttackSwordShield",
            "AttackDaggers",
            "AttackHeavy",
            "Attack1",
            "Attack2",
            "Attack",
            "attack",
        };
        static readonly string[] RangedAttackStates =
        {
            "Shoot",
            "AttackBow",
            "AttackCrossbow",
            "Attack1",
            "Attack2",
            "Attack",
            "attack",
        };
        static readonly string[] MagicAttackStates =
        {
            "Cast",
            "CastSpell",
            "AttackCast",
            "Shoot",
            "Attack1",
            "Attack2",
            "Attack",
            "attack",
        };
        static readonly Color HostileBaseColor = new(0.90f, 0.30f, 0.10f);
        static readonly Color HostileRimColor = new(1.00f, 0.55f, 0.00f);
        static readonly RaycastHit[] GroundHitBuffer = new RaycastHit[24];
        static readonly Dictionary<string, Vector2[]> RouteSegmentSimPolylines = BuildRouteSegmentSimPolylines();
        static readonly Vector2[] LaneCombatCoreSimPositions =
        {
            new(-24f, 24f),
            new(24f, 24f),
            new(24f, -24f),
            new(-24f, -24f),
        };
        static readonly Vector2[] LaneCombatLateralSimAxes =
        {
            new(1f, 0f),
            new(-1f, 0f),
            new(-1f, 0f),
            new(1f, 0f),
        };
        static readonly Vector2[] LaneCombatForwardSimAxes =
        {
            new(0f, -1f),
            new(0f, -1f),
            new(0f, 1f),
            new(0f, 1f),
        };

        enum VisualState
        {
            Idle,
            Moving,
            Attacking
        }

        class WaveView
        {
            public string id;
            public GameObject go;
            public LaneSnapshotCombatant combatant;
            public Animator[] animators;
            public Renderer[] renderers;
            public string[] attackStates = DefaultAttackStates;
            public Vector3 snapshotWorldPos;
            public Vector3 snapshotVelocity;
            public Vector3 desiredFacing = Vector3.forward;
            public float timeSinceSnap;
            public float lastAttackAt = float.MinValue;
            public float lastVisualAttackUntil;
            public int lastServerAttackPulse;
            public bool hadSnapshot;
            public bool lockCombatFacing;
            public bool hasDesiredFacing;
            public VisualState visualState = VisualState.Idle;
            public Vector3 lastFramePosition;
        }

        [Header("Runtime Snapshot Units")]
        public UnitPrefabRegistry Registry;
        public GameObject HpBarPrefab;

        [SerializeField] float spawnSnapDistance = 4f;

        readonly Dictionary<string, WaveView> _views = new();
        readonly HashSet<string> _seenIds = new();
        readonly List<string> _toRemove = new();
        readonly int[] _branchMap = new int[4] { 0, 1, 2, 3 };
        readonly HashSet<string> _loggedFailureKeys = new();
        readonly HashSet<string> _loggedMaterializationSummaryKeys = new();
        readonly HashSet<string> _loggedPathModeKeys = new();
        readonly HashSet<string> _loggedRejectionKeys = new();
        readonly HashSet<string> _loggedBattlefieldRouteErrors = new();
        readonly Dictionary<string, Vector3> _battlefieldTownCoreByLaneKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3> _battlefieldFrontGateByLaneKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3> _battlefieldBarracksByLaneAndId = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3> _battlefieldRouteNodeWorldByNode = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _battlefieldRouteNodeLaneByNode = new(StringComparer.OrdinalIgnoreCase);
        readonly List<FortressPadAnchor> _fortressAnchorScratch = new();
        readonly List<BarracksSiteView> _barracksSiteScratch = new();
        Transform _battlefieldMineAnchor;
        string _lastSnapshotSummaryLog;
        float _lastSnapTime = -1f;
        bool _subscribed;

        public static WaveSnapshotRuntimeSpawner EnsureRuntimeSpawner()
        {
            var existing = FindFirstObjectByType<WaveSnapshotRuntimeSpawner>();
            if (existing != null)
                return existing;

            var host = GameplayPresentationRoot.FindActive();
            if (host == null)
                return null;

            var runtime = host.GetComponent<WaveSnapshotRuntimeSpawner>();
            if (runtime == null)
                runtime = host.gameObject.AddComponent<WaveSnapshotRuntimeSpawner>();

            runtime.SyncDependenciesFromScene();
            return runtime;
        }

        static Dictionary<string, Vector2[]> BuildRouteSegmentSimPolylines()
        {
            var polylines = new Dictionary<string, Vector2[]>(StringComparer.OrdinalIgnoreCase)
            {
                [ "A_B" ] = new[]
                {
                    new Vector2(-24f, 24f),
                    new Vector2(0f, 28f),
                    new Vector2(24f, 24f),
                },
                [ "B_C" ] = new[]
                {
                    new Vector2(24f, 24f),
                    new Vector2(28f, 0f),
                    new Vector2(24f, -24f),
                },
                [ "C_D" ] = new[]
                {
                    new Vector2(24f, -24f),
                    new Vector2(0f, -28f),
                    new Vector2(-24f, -24f),
                },
                [ "D_A" ] = new[]
                {
                    new Vector2(-24f, -24f),
                    new Vector2(-28f, 0f),
                    new Vector2(-24f, 24f),
                },
                [ "A_C" ] = new[]
                {
                    new Vector2(-24f, 24f),
                    new Vector2(0f, 0f),
                    new Vector2(24f, -24f),
                },
                [ "C_A" ] = new[]
                {
                    new Vector2(24f, -24f),
                    new Vector2(0f, 0f),
                    new Vector2(-24f, 24f),
                },
                [ "B_D" ] = new[]
                {
                    new Vector2(24f, 24f),
                    new Vector2(0f, 0f),
                    new Vector2(-24f, -24f),
                },
                [ "D_B" ] = new[]
                {
                    new Vector2(-24f, -24f),
                    new Vector2(0f, 0f),
                    new Vector2(24f, 24f),
                },
                [ "A_M" ] = new[]
                {
                    new Vector2(-24f, 24f),
                    new Vector2(0f, 0f),
                },
                [ "M_A" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(-24f, 24f),
                },
                [ "B_M" ] = new[]
                {
                    new Vector2(24f, 24f),
                    new Vector2(0f, 0f),
                },
                [ "M_B" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(24f, 24f),
                },
                [ "C_M" ] = new[]
                {
                    new Vector2(24f, -24f),
                    new Vector2(0f, 0f),
                },
                [ "M_C" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(24f, -24f),
                },
                [ "D_M" ] = new[]
                {
                    new Vector2(-24f, -24f),
                    new Vector2(0f, 0f),
                },
                [ "M_D" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(-24f, -24f),
                },
                // Keep wave-lane route-space identical to the authoritative server
                // graph so routeWorldX/Y offsets resolve around the mine center.
                [ "WA_A" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(-24f, 24f),
                },
                [ "WB_B" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(24f, 24f),
                },
                [ "WC_C" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(24f, -24f),
                },
                [ "WD_D" ] = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(-24f, -24f),
                },
            };

            var barracksPositions = new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase)
            {
                [ "center" ] = new Vector2(0f, 2f),
                [ "left" ] = new Vector2(-4f, 2f),
                [ "right" ] = new Vector2(4f, 2f),
            };

            string[] coreNodeIds = { RouteNodeA, RouteNodeB, RouteNodeC, RouteNodeD };
            Vector2[] coreNodePositions =
            {
                new(-24f, 24f),
                new(24f, 24f),
                new(24f, -24f),
                new(-24f, -24f),
            };
            Vector2[] lateralDirs =
            {
                new(1f, 0f),
                new(-1f, 0f),
                new(-1f, 0f),
                new(1f, 0f),
            };
            Vector2[] forwardDirs =
            {
                new(0f, -1f),
                new(0f, -1f),
                new(0f, 1f),
                new(0f, 1f),
            };

            for (int laneIndex = 0; laneIndex < coreNodeIds.Length; laneIndex++)
            {
                string coreNodeId = coreNodeIds[laneIndex];
                Vector2 coreNodePos = coreNodePositions[laneIndex];
                Vector2 lateralDir = lateralDirs[laneIndex];
                Vector2 forwardDir = forwardDirs[laneIndex];

                foreach (var barracksPair in barracksPositions)
                {
                    string routeNodeId = GetBarracksRouteNodeId(coreNodeId, barracksPair.Key);
                    Vector2 offset = barracksPair.Value;
                    Vector2 routeNodePos = coreNodePos + (lateralDir * offset.x) + (forwardDir * offset.y);
                    polylines[$"{routeNodeId}_{coreNodeId}"] = new[]
                    {
                        routeNodePos,
                        coreNodePos,
                    };
                }
            }

            return polylines;
        }

        void Awake() => SyncDependenciesFromScene();

        void OnEnable()
        {
            SyncDependenciesFromScene();
            TrySubscribeSnapshots();
        }

        void OnDisable()
        {
            if (_subscribed && SnapshotApplier.Instance != null)
                SnapshotApplier.Instance.OnMLSnapshotApplied -= OnSnapshot;
            _subscribed = false;
        }

        void Update()
        {
            SyncDependenciesFromScene();
            TrySubscribeSnapshots();

            float dt = Time.deltaTime;
            float now = Time.time;
            foreach (var pair in _views)
            {
                var view = pair.Value;
                if (view?.go == null || !view.go.activeInHierarchy)
                    continue;

                view.timeSinceSnap += dt;

                if (view.hadSnapshot)
                {
                    Vector3 desired = view.snapshotWorldPos;
                    float maxStep = Mathf.Max(spawnSnapDistance, view.snapshotVelocity.magnitude * dt * 1.5f);
                    if ((view.go.transform.position - desired).sqrMagnitude > spawnSnapDistance * spawnSnapDistance)
                    {
                        view.go.transform.position = Vector3.MoveTowards(view.go.transform.position, desired, maxStep);
                    }
                    else
                    {
                        float lerpT = 1f - Mathf.Exp(-TargetFollowSharpness * dt);
                        view.go.transform.position = Vector3.Lerp(view.go.transform.position, desired, lerpT);
                    }
                }

                Vector3 currentPosition = view.go.transform.position;
                Vector3 moveDelta = currentPosition - view.lastFramePosition;
                Vector3 flatDelta = moveDelta;
                flatDelta.y = 0f;
                if (view.hasDesiredFacing)
                {
                    float turnSharpness = view.lockCombatFacing
                        ? CombatFacingTurnSharpness
                        : TargetFollowSharpness;
                    RotateTowardDirection(view.go.transform, view.desiredFacing, turnSharpness, dt);
                }
                else if (flatDelta.sqrMagnitude > 0.0004f)
                {
                    FaceDirection(view.go.transform, flatDelta);
                }

                bool moving = flatDelta.sqrMagnitude > 0.0004f;
                bool attacking = now <= view.lastVisualAttackUntil
                    || (view.combatant != null && now - view.combatant.LastAttackAt <= AttackVisualHoldSeconds);

                SetVisualState(view, attacking ? VisualState.Attacking : moving ? VisualState.Moving : VisualState.Idle);
                view.lastFramePosition = currentPosition;
            }
        }

        void TrySubscribeSnapshots()
        {
            if (_subscribed)
                return;

            var sa = SnapshotApplier.Instance;
            if (sa == null)
                return;

            sa.OnMLSnapshotApplied += OnSnapshot;
            _subscribed = true;

            if (sa.LatestML != null)
                OnSnapshot(sa.LatestML);
        }

        void OnSnapshot(MLSnapshot snap)
        {
            if (snap?.lanes == null)
                return;

            SyncDependenciesFromScene();

            float now = Time.time;
            float snapDt = _lastSnapTime >= 0f ? Mathf.Max(0.01f, now - _lastSnapTime) : 0.1f;
            _lastSnapTime = now;

            for (int i = 0; i < _branchMap.Length; i++)
                _branchMap[i] = i;

            foreach (var lane in snap.lanes)
            {
                if (lane == null || (uint)lane.laneIndex >= (uint)_branchMap.Length)
                    continue;

                int branchCfg = TileGrid.GetBranchConfigIndex(lane.branchId);
                if (branchCfg >= 0)
                    _branchMap[lane.laneIndex] = branchCfg;
            }

            RefreshBattlefieldRouteCache(snap);

            _seenIds.Clear();
            int totalSnapshotUnitsReceived = 0;
            int materializableUnitCount = 0;
            int ownerResolutionFailureCount = 0;
            int worldPositionFailureCount = 0;
            int prefabFailureCount = 0;
            int instantiatedUnitCount = 0;

            foreach (var lane in snap.lanes)
            {
                if (lane?.units == null)
                    continue;

                for (int i = 0; i < lane.units.Length; i++)
                {
                    MLUnit unit = lane.units[i];
                    totalSnapshotUnitsReceived++;

                    if (!ShouldMaterializeUnit(unit, out string rejectionReason))
                    {
                        LogRejectedUnit(lane, unit, rejectionReason);
                        continue;
                    }

                    materializableUnitCount++;

                    int spatialLane = (uint)lane.laneIndex < (uint)_branchMap.Length
                        ? _branchMap[lane.laneIndex]
                        : lane.laneIndex;

                    if (!TryResolveSnapshotWorldPosition(unit, lane, spatialLane, out Vector3 worldPos, out string worldPosReason))
                    {
                        worldPositionFailureCount++;
                        LogWorldPositionFailure(lane, unit, spatialLane, worldPosReason);
                        continue;
                    }

                    if (!_seenIds.Add(unit.id))
                    {
                        Debug.LogError(
                            $"[SpawnAudit][DuplicateUnitId] lane={lane.laneIndex} unitId='{unit.id}' " +
                            $"type='{unit.type ?? "<null>"}' sourceBarracksKey='{ResolveSourceBarracksKey(unit)}' " +
                            "Duplicate snapshot IDs are invalid; skipping the later unit payload.",
                            this);
                        continue;
                    }

                    string defenderTeamKey = ResolveDefenderTeamKey(lane);
                    string ownerTeamKey = ResolveOwnerTeamKey(unit, lane, defenderTeamKey);
                    if (string.IsNullOrWhiteSpace(ownerTeamKey))
                        ownerResolutionFailureCount++;

                    bool createdNow = false;
                    if (!_views.TryGetValue(unit.id, out var view) || view.go == null)
                    {
                        view = CreateSnapshotView(unit, lane, spatialLane, worldPos, defenderTeamKey, ownerTeamKey);
                        if (view != null)
                            createdNow = true;
                        else
                            prefabFailureCount++;
                    }

                    if (view == null || view.go == null)
                        continue;

                    if (createdNow)
                        instantiatedUnitCount++;

                    view.combatant.ApplySnapshot(defenderTeamKey, ownerTeamKey, unit.hp, unit.maxHp, unit.moveSpeed);

                    bool hadPreviousSnapshot = view.hadSnapshot;
                    if (hadPreviousSnapshot)
                        view.snapshotVelocity = (worldPos - view.snapshotWorldPos) / snapDt;
                    else
                        view.snapshotVelocity = Vector3.zero;

                    view.snapshotWorldPos = worldPos;
                    view.timeSinceSnap = 0f;
                    view.hadSnapshot = true;

                    if (!view.go.activeSelf && !view.combatant.IsLocallyDefeated)
                        view.go.SetActive(true);

                    if (createdNow || !hadPreviousSnapshot || (view.go.transform.position - worldPos).sqrMagnitude > spawnSnapDistance * spawnSnapDistance)
                        view.go.transform.position = worldPos;

                    view.attackStates = ResolveAttackStateNames(unit);
                    if (TryResolveDesiredFacing(snap, unit, lane, spatialLane, worldPos, out Vector3 desiredFacing, out bool lockCombatFacing))
                    {
                        view.desiredFacing = desiredFacing;
                        view.hasDesiredFacing = true;
                        view.lockCombatFacing = lockCombatFacing;
                        FaceDirection(view.go.transform, desiredFacing);
                    }
                    else
                    {
                        view.hasDesiredFacing = false;
                        view.lockCombatFacing = false;
                    }

                    if (!view.hasDesiredFacing && view.snapshotVelocity.sqrMagnitude > 0.01f)
                        FaceDirection(view.go.transform, view.snapshotVelocity);

                    if (unit.attackPulse > 0 && unit.attackPulse != view.lastServerAttackPulse)
                    {
                        view.lastAttackAt = now;
                        view.lastVisualAttackUntil = now + AttackVisualHoldSeconds;
                        PlayAttackAnimation(view);
                    }

                    view.lastServerAttackPulse = unit.attackPulse;
                }
            }

            _toRemove.Clear();
            foreach (var pair in _views)
            {
                if (!_seenIds.Contains(pair.Key))
                    _toRemove.Add(pair.Key);
            }

            for (int i = 0; i < _toRemove.Count; i++)
                DestroyWaveView(_toRemove[i]);

            int materializedUnitCount = 0;
            foreach (var pair in _views)
            {
                var view = pair.Value;
                if (view?.go != null)
                    materializedUnitCount++;
            }

            string snapshotSummary =
                $"[SpawnAudit][ClientSnapshotSummary] tick={snap.tick} totalSnapshotUnitsReceived={totalSnapshotUnitsReceived} " +
                $"materializableUnitCount={materializableUnitCount} materializedUnitCount={materializedUnitCount} " +
                $"ownerResolutionFailureCount={ownerResolutionFailureCount} worldPositionFailureCount={worldPositionFailureCount} " +
                $"prefabFailureCount={prefabFailureCount} instantiatedUnitCount={instantiatedUnitCount}.";

            bool shouldLogInformationalSummary =
                totalSnapshotUnitsReceived > 0
                || materializableUnitCount > 0
                || materializedUnitCount > 0
                || ownerResolutionFailureCount > 0
                || worldPositionFailureCount > 0
                || prefabFailureCount > 0
                || instantiatedUnitCount > 0;

            if (materializedUnitCount != materializableUnitCount)
            {
                string summaryKey = $"{snap.tick}:{materializableUnitCount}:{materializedUnitCount}:{worldPositionFailureCount}:{prefabFailureCount}";
                if (_loggedMaterializationSummaryKeys.Add(summaryKey))
                {
                    Debug.LogError(
                        $"{snapshotSummary} " +
                        "If these counts diverge, snapshot units are being dropped before or during runtime materialization.",
                        this);
                }
            }
            else if (shouldLogInformationalSummary && _lastSnapshotSummaryLog != snapshotSummary)
            {
                _lastSnapshotSummaryLog = snapshotSummary;
                Debug.Log(snapshotSummary, this);
            }
        }

#if UNITY_EDITOR
        public void DebugApplySnapshot(MLSnapshot snap)
        {
            SyncDependenciesFromScene();
            OnSnapshot(snap);
        }
#endif

        void SyncDependenciesFromScene()
        {
            Registry = GameplayPresentationRoot.ResolveRegistry(Registry);
            HpBarPrefab = GameplayPresentationRoot.ResolveHpBarPrefab(HpBarPrefab);
        }

        WaveView CreateSnapshotView(
            MLUnit unit,
            MLLaneSnap lane,
            int spatialLane,
            Vector3 spawnPos,
            string defenderTeamKey,
            string ownerTeamKey)
        {
            MLUnitResolvedIdentity identity = MLUnitPresentationIdentityResolver.Resolve(unit);
            string resolvedCatalogUnitKey = !string.IsNullOrWhiteSpace(identity.CatalogUnitKey)
                ? identity.CatalogUnitKey
                : unit?.type;
            string resolvedSkinKey = !string.IsNullOrWhiteSpace(identity.SkinKey)
                ? identity.SkinKey
                : unit?.skinKey;

            if (Registry == null)
            {
                ReportWaveFailure(
                    $"registry_{lane?.laneIndex ?? -1}",
                    spawnPos,
                    $"[WaveSnapshotRuntimeSpawner] Missing UnitPrefabRegistry on '{name}'. " +
                    $"Snapshot unit '{unit?.id ?? "<null>"}' type='{unit?.type ?? "<null>"}' lane={lane?.laneIndex ?? -1} cannot spawn.");
                return null;
            }

            RuntimeFailureMarker.Clear(transform, $"wave_failure_registry_{lane?.laneIndex ?? -1}");

            GameObject prefab = Registry.GetPrefabForSkin(resolvedCatalogUnitKey, resolvedSkinKey);
            if (prefab == null)
            {
                ReportWaveFailure(
                    $"prefab_{unit?.id ?? "unknown"}",
                    spawnPos,
                    $"[WaveSnapshotRuntimeSpawner] Missing prefab for snapshot unit id='{unit?.id ?? "<null>"}' type='{unit?.type ?? "<null>"}' " +
                    $"resolvedCatalogUnitKey='{resolvedCatalogUnitKey ?? "<null>"}' skin='{resolvedSkinKey ?? "<default>"}' " +
                    $"lane={lane?.laneIndex ?? -1} sourceBarracksKey='{ResolveSourceBarracksKey(unit)}'.");
                return null;
            }

            RuntimeFailureMarker.Clear(transform, $"wave_failure_prefab_{unit.id}");

            Quaternion rotation = ResolveSnapshotSpawnRotation(unit, lane, spatialLane);
            var go = Instantiate(prefab, spawnPos, rotation, transform);
            go.name = $"SnapshotUnit_{unit.id}_{resolvedCatalogUnitKey}_{resolvedSkinKey ?? "default"}";
            go.SetActive(true);
            string sourceBarracksKey = ResolveSourceBarracksKey(unit);
            if (!string.IsNullOrWhiteSpace(sourceBarracksKey))
            {
                Debug.Log(
                    $"[BarracksTrace][ClientSpawn] unit='{unit.id}' type='{unit.type}' " +
                    $"sourceLane={unit.sourceLaneIndex} barracksId='{sourceBarracksKey}' " +
                    $"spatialLane={spatialLane}");
            }

            float baseScale = Registry.GetScaleForSkin(resolvedCatalogUnitKey, resolvedSkinKey);
            float scale = Mathf.Max(0.01f, baseScale * GetLevelScale(unit.level > 0 ? unit.level : 1));
            go.transform.localScale = Vector3.one * scale;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                UpgradeLegacyRendererMaterials(renderers[i]);
            ApplySnapshotUnitTint(go, unit, renderers, ownerTeamKey);

            var animators = go.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                animator.Rebind();
                animator.Update(0f);
            }

            var combatant = go.GetComponent<LaneSnapshotCombatant>();
            if (combatant == null)
                combatant = go.AddComponent<LaneSnapshotCombatant>();
            combatant.InitializeSnapshot(
                unit.id,
                resolvedCatalogUnitKey,
                resolvedSkinKey,
                defenderTeamKey,
                ownerTeamKey,
                unit.hp,
                unit.maxHp,
                unit.moveSpeed);

            var view = new WaveView
            {
                id = unit.id,
                go = go,
                combatant = combatant,
                animators = animators,
                renderers = renderers,
                attackStates = ResolveAttackStateNames(unit),
                snapshotWorldPos = spawnPos,
                snapshotVelocity = Vector3.zero,
                timeSinceSnap = 0f,
                hadSnapshot = false,
                lastServerAttackPulse = unit.attackPulse,
                lastFramePosition = spawnPos,
            };

            _views[unit.id] = view;
            SetVisualState(view, VisualState.Moving);
            AudioManager.I?.Play(AudioManager.SFX.UnitSpawn, 0.25f);
            Debug.Log(
                $"[SpawnAudit][ClientInstantiate] unitId='{unit.id}' unitType='{unit.type}' " +
                $"resolvedCatalogUnitKey='{resolvedCatalogUnitKey ?? "<null>"}' skin='{resolvedSkinKey ?? "<default>"}' " +
                $"archetypeKey='{identity.ArchetypeKey ?? "<null>"}' presentationKey='{identity.PresentationKey ?? "<null>"}' " +
                $"sourceTeam='{unit.sourceTeam ?? "<none>"}' ownerLane={unit.ownerLane} sourceLane={unit.sourceLaneIndex} " +
                $"isWaveUnit={unit.isWaveUnit.ToString().ToLowerInvariant()} isDefender={unit.isDefender.ToString().ToLowerInvariant()} " +
                $"resolvedPrefab='{prefab.name}' " +
                $"spawnedName='{go.name}' worldPos=({spawnPos.x:0.###},{spawnPos.y:0.###},{spawnPos.z:0.###}) " +
                $"activeSelf={go.activeSelf} scale={scale:0.###} ownerTeam='{ownerTeamKey ?? "<none>"}'",
                this);
            return view;
        }

        void DestroyWaveView(string id)
        {
            if (!_views.TryGetValue(id, out var view))
                return;

            if (view.go != null)
                Destroy(view.go);

            _views.Remove(id);
            Debug.Log($"[SpawnAudit][ClientRemove] unitId='{id}' presenterRemoved=true", this);
        }

        bool ShouldMaterializeUnit(MLUnit unit, out string rejectionReason)
        {
            if (unit == null)
            {
                rejectionReason = "unit is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.id))
            {
                rejectionReason = "unit id is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.spawnSourceType))
            {
                rejectionReason = "spawnSourceType is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.allegianceKey))
            {
                rejectionReason = "allegianceKey is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.stance))
            {
                rejectionReason = "stance is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.pathContractType))
            {
                rejectionReason = "pathContractType is missing";
                return false;
            }

            if (unit.laneId < 0)
            {
                rejectionReason = "laneId is missing or invalid";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.pathId))
            {
                rejectionReason = "pathId is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.routeType))
            {
                rejectionReason = "routeType is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.currentSegment))
            {
                rejectionReason = "currentSegment is missing";
                return false;
            }

            if (!float.IsFinite(unit.segmentProgress))
            {
                rejectionReason = "segmentProgress is missing or invalid";
                return false;
            }

            if (!float.IsFinite(unit.routeWorldX) || !float.IsFinite(unit.routeWorldY))
            {
                rejectionReason = "routeWorld is missing or invalid";
                return false;
            }

            if (unit.currentWaypointIndex < 0)
            {
                rejectionReason = "currentWaypointIndex is missing or invalid";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.nextWaypoint))
            {
                rejectionReason = "nextWaypoint is missing";
                return false;
            }

            if (string.IsNullOrWhiteSpace(unit.movementState))
            {
                rejectionReason = "movementState is missing";
                return false;
            }

            if ((string.Equals(unit.spawnSourceType, "barracks_roster", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unit.spawnSourceType, "barracks_hero", StringComparison.OrdinalIgnoreCase))
                && string.IsNullOrWhiteSpace(unit.barracksId))
            {
                rejectionReason = "barracksId is missing for barracks spawn";
                return false;
            }

            rejectionReason = null;
            return true;
        }

        void RefreshBattlefieldRouteCache(MLSnapshot snap)
        {
            RefreshBattlefieldRouteAnchors();
            RefreshBattlefieldRouteNodeWorlds(snap);
            RefreshBattlefieldRouteNodeMap(snap);
        }

        void RefreshBattlefieldRouteAnchors()
        {
            _battlefieldTownCoreByLaneKey.Clear();
            _battlefieldFrontGateByLaneKey.Clear();
            _battlefieldBarracksByLaneAndId.Clear();
            _fortressAnchorScratch.Clear();
            FortressPadAnchor.CollectAnchors(_fortressAnchorScratch);

            for (int i = 0; i < _fortressAnchorScratch.Count; i++)
            {
                var anchor = _fortressAnchorScratch[i];
                if (anchor == null || !anchor.isActiveAndEnabled)
                    continue;
                if (!string.Equals(anchor.BuildingType, "town_core", StringComparison.OrdinalIgnoreCase))
                    continue;

                string laneKey = NormalizeBattlefieldLaneKey(
                    FortressLaneResolver.ResolveLaneKey(anchor.transform, anchor.AnchorLaneColor));
                if (string.IsNullOrWhiteSpace(laneKey))
                    continue;

                Vector3 anchorWorld = anchor.FocusTransform != null
                    ? anchor.FocusTransform.position
                    : anchor.transform.position;
                _battlefieldTownCoreByLaneKey[laneKey] = anchorWorld;
            }

            string[] requiredLaneKeys = { "red", "yellow", "blue", "green" };
            for (int i = 0; i < requiredLaneKeys.Length; i++)
            {
                string laneKey = requiredLaneKeys[i];
                if (!_battlefieldTownCoreByLaneKey.ContainsKey(laneKey))
                    LogBattlefieldRouteFailureOnce($"anchor:town_core:{laneKey}", $"Missing FortressPadAnchor town core for lane '{laneKey}'.");
            }

            var mineAnchor = WaveSpawnAnchor.FindAnchor("mine_center");
            _battlefieldMineAnchor = mineAnchor != null ? mineAnchor.FocusTransform : null;

            if (_battlefieldMineAnchor == null)
                LogBattlefieldRouteFailureOnce("anchor:mine_missing", "Missing WaveSpawnAnchor(mine_center).");

            BindNamedFrontGateAnchor("Red_Gate_Front", "red");
            BindNamedFrontGateAnchor("Yellow_Gate_Front", "yellow");
            BindNamedFrontGateAnchor("Blue_Gate_Front", "blue");
            BindNamedFrontGateAnchor("Green_Gate_Front", "green");

            string[] requiredFrontGateLaneKeys = { "red", "yellow", "blue", "green" };
            for (int i = 0; i < requiredFrontGateLaneKeys.Length; i++)
            {
                string laneKey = requiredFrontGateLaneKeys[i];
                if (_battlefieldFrontGateByLaneKey.ContainsKey(laneKey))
                    continue;

                string gateObjectName = ResolveFrontGateObjectNameForLaneKey(laneKey);
                LogBattlefieldRouteFailureOnce(
                    $"anchor:front_gate:{laneKey}",
                    $"Missing front gate object '{gateObjectName}' for lane '{laneKey}'.");
            }

            _barracksSiteScratch.Clear();
            BarracksSiteView.CollectSites(_barracksSiteScratch);
            for (int i = 0; i < _barracksSiteScratch.Count; i++)
            {
                var site = _barracksSiteScratch[i];
                if (site == null || !site.isActiveAndEnabled)
                    continue;

                string laneKey = NormalizeBattlefieldLaneKey(
                    FortressLaneResolver.ResolveLaneKey(site.transform, site.laneColor));
                string barracksId = BarracksActivityUtility.NormalizeBarracksId(site.BarracksId);
                if (string.IsNullOrWhiteSpace(laneKey) || string.IsNullOrWhiteSpace(barracksId))
                    continue;

                Vector3 anchorWorld = site.FocusTransform != null
                    ? site.FocusTransform.position
                    : site.transform.position;
                _battlefieldBarracksByLaneAndId[BuildBattlefieldBarracksKey(laneKey, barracksId)] = anchorWorld;
            }
        }

        void BindNamedTownCoreAnchor(string objectName, string laneKey)
        {
            if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(laneKey))
                return;

            var go = GameObject.Find(objectName);
            if (go == null)
                return;

            string normalizedLaneKey = NormalizeBattlefieldLaneKey(laneKey);
            if (string.IsNullOrWhiteSpace(normalizedLaneKey))
                return;

            _battlefieldTownCoreByLaneKey[normalizedLaneKey] = go.transform.position;
        }

        void BindNamedFrontGateAnchor(string objectName, string laneKey)
        {
            if (string.IsNullOrWhiteSpace(objectName) || string.IsNullOrWhiteSpace(laneKey))
                return;

            var go = GameObject.Find(objectName);
            if (go == null)
                return;

            string normalizedLaneKey = NormalizeBattlefieldLaneKey(laneKey);
            if (string.IsNullOrWhiteSpace(normalizedLaneKey))
                return;

            _battlefieldFrontGateByLaneKey[normalizedLaneKey] = go.transform.position;
        }

        void RefreshBattlefieldRouteNodeWorlds(MLSnapshot snap)
        {
            _battlefieldRouteNodeWorldByNode.Clear();
            if (_battlefieldMineAnchor != null)
                _battlefieldRouteNodeWorldByNode[RouteMineNode] = _battlefieldMineAnchor.position;

            for (int laneIndex = 0; laneIndex < 4; laneIndex++)
            {
                string laneKey = ResolveFixedLaneKeyForLaneIndex(laneIndex);
                string routeCoreNode = ResolveDefaultRouteNodeForLaneIndex(laneIndex);
                string waveRouteNode = ResolveWaveRouteNodeForLaneIndex(laneIndex);
                if (string.IsNullOrWhiteSpace(laneKey) || string.IsNullOrWhiteSpace(routeCoreNode))
                    continue;

                if (TryResolveLaneRouteCoreWorld(laneKey, laneIndex, out Vector3 coreWorld, out _))
                {
                    _battlefieldRouteNodeWorldByNode[routeCoreNode] = coreWorld;
                    if (!string.IsNullOrWhiteSpace(waveRouteNode))
                    {
                        if (_battlefieldMineAnchor != null)
                            _battlefieldRouteNodeWorldByNode[waveRouteNode] = _battlefieldMineAnchor.position;
                        else
                            LogBattlefieldRouteFailureOnce($"anchor:wave_node:{waveRouteNode}", $"Cannot resolve wave route node '{waveRouteNode}' without WaveSpawnAnchor(mine_center).");
                    }
                }
            }

            if (snap?.lanes == null)
                return;

            for (int i = 0; i < snap.lanes.Length; i++)
            {
                MLLaneSnap lane = snap.lanes[i];
                if (lane == null)
                    continue;

                string laneKey = NormalizeBattlefieldLaneKey(lane.slotColor);
                string routeCoreNode = ResolveDefaultRouteNodeForLaneIndex(lane.laneIndex);
                string waveRouteNode = ResolveWaveRouteNodeForLaneIndex(lane.laneIndex);
                if (string.IsNullOrWhiteSpace(laneKey) || string.IsNullOrWhiteSpace(routeCoreNode))
                    continue;

                int spatialLane = (uint)lane.laneIndex < (uint)_branchMap.Length
                    ? _branchMap[lane.laneIndex]
                    : lane.laneIndex;

                if (TryResolveLaneRouteCoreWorld(laneKey, spatialLane, out Vector3 coreWorld, out string failureReason))
                {
                    _battlefieldRouteNodeWorldByNode[routeCoreNode] = coreWorld;
                    if (!string.IsNullOrWhiteSpace(waveRouteNode))
                    {
                        if (_battlefieldMineAnchor != null)
                            _battlefieldRouteNodeWorldByNode[waveRouteNode] = _battlefieldMineAnchor.position;
                        else
                            LogBattlefieldRouteFailureOnce($"anchor:wave_node:{waveRouteNode}", $"Cannot resolve wave route node '{waveRouteNode}' without WaveSpawnAnchor(mine_center).");
                    }
                }
                else
                {
                    LogBattlefieldRouteFailureOnce(
                        $"route_core:{laneKey}",
                        $"Unable to resolve battlefield route core for lane '{laneKey}': {failureReason}");
                }
            }
        }

        bool TryResolveLaneRouteCoreWorld(string laneKey, int spatialLane, out Vector3 worldPos, out string failureReason)
        {
            worldPos = default;
            failureReason = null;

            string normalizedLaneKey = NormalizeBattlefieldLaneKey(laneKey);
            if (string.IsNullOrWhiteSpace(normalizedLaneKey))
            {
                failureReason = $"Route core lane key '{laneKey ?? "<null>"}' is invalid.";
                return false;
            }

            if (_battlefieldTownCoreByLaneKey.TryGetValue(normalizedLaneKey, out Vector3 townCoreWorld))
            {
                worldPos = townCoreWorld;
                return true;
            }

            failureReason = $"Town-core anchor is missing for lane '{normalizedLaneKey}'.";
            return false;
        }

        bool TryResolveLiveCombatWorldPosition(
            MLUnit unit,
            MLLaneSnap lane,
            out Vector3 worldPos,
            out Vector3 routeForward,
            out string resolvedRouteSource)
        {
            worldPos = default;
            routeForward = Vector3.forward;
            resolvedRouteSource = null;
            if (unit == null || lane == null)
                return false;
            if (!float.IsFinite(unit.gridX) || !float.IsFinite(unit.gridY))
                return false;
            if (!IsCombatSnapshotUnit(unit))
                return false;
            if (!ShouldUseLiveCombatProjection(unit, lane))
                return false;
            if (!TryNormalizeRouteNodeId(unit.routeTargetNode, out string routeTargetNode))
                return false;

            int targetLaneIndex = ResolveLaneIndexForRouteNode(routeTargetNode);
            if (targetLaneIndex < 0 || targetLaneIndex >= LaneCombatCoreSimPositions.Length)
                return false;

            string laneKey = null;
            if (!_battlefieldRouteNodeLaneByNode.TryGetValue(routeTargetNode, out laneKey) || string.IsNullOrWhiteSpace(laneKey))
                laneKey = ResolveFixedLaneKeyForLaneIndex(targetLaneIndex);
            laneKey = NormalizeBattlefieldLaneKey(laneKey);
            if (string.IsNullOrWhiteSpace(laneKey))
                return false;

            return TryResolveLaneCombatSimWorldPosition(
                unit.gridX,
                unit.gridY,
                targetLaneIndex,
                laneKey,
                out worldPos,
                out routeForward,
                out resolvedRouteSource);
        }

        bool ShouldUseLiveCombatProjection(MLUnit unit, MLLaneSnap lane)
        {
            if (unit == null)
                return false;

            if (unit.blockedByStructure || !string.IsNullOrWhiteSpace(unit.blockedByStructureId))
                return true;

            if (string.IsNullOrWhiteSpace(unit.combatTargetId))
                return false;

            return TryResolveFortressPadWorldPosition(unit.combatTargetId, lane, out _);
        }

        bool TryResolveLaneCombatSimWorldPosition(
            float simX,
            float simY,
            int targetLaneIndex,
            string laneKey,
            out Vector3 worldPos,
            out Vector3 routeForward,
            out string resolvedRouteSource)
        {
            worldPos = default;
            routeForward = Vector3.forward;
            resolvedRouteSource = null;

            if (targetLaneIndex < 0 || targetLaneIndex >= LaneCombatCoreSimPositions.Length)
                return false;
            laneKey = NormalizeBattlefieldLaneKey(laneKey);
            if (string.IsNullOrWhiteSpace(laneKey))
                return false;
            if (!_battlefieldTownCoreByLaneKey.TryGetValue(laneKey, out Vector3 townCoreWorld))
                return false;
            if (!_battlefieldFrontGateByLaneKey.TryGetValue(laneKey, out Vector3 frontGateWorld))
                return false;

            Vector3 outwardForward = frontGateWorld - townCoreWorld;
            outwardForward.y = 0f;
            float frontGateDistance = outwardForward.magnitude;
            if (frontGateDistance <= 0.0001f)
                return false;

            float worldUnitsPerSimUnit = frontGateDistance / FrontGateForwardOffset;
            Vector3 forwardWorldDir = outwardForward / frontGateDistance;
            Vector3 lateralWorldDir = new(-forwardWorldDir.z, 0f, forwardWorldDir.x);
            if (targetLaneIndex == 1 || targetLaneIndex == 2)
                lateralWorldDir *= -1f;

            Vector2 simDelta = new(simX, simY);
            simDelta -= LaneCombatCoreSimPositions[targetLaneIndex];

            float lateralOffset = Vector2.Dot(simDelta, LaneCombatLateralSimAxes[targetLaneIndex]);
            float forwardOffset = Vector2.Dot(simDelta, LaneCombatForwardSimAxes[targetLaneIndex]);
            if (!IsWithinCombatProjectionEnvelope(forwardOffset, lateralOffset))
                return false;

            worldPos = townCoreWorld
                + (lateralWorldDir * (lateralOffset * worldUnitsPerSimUnit))
                + (forwardWorldDir * (forwardOffset * worldUnitsPerSimUnit));

            Vector3 faceTowardCore = townCoreWorld - worldPos;
            faceTowardCore.y = 0f;
            routeForward = faceTowardCore.sqrMagnitude > 0.0001f
                ? faceTowardCore.normalized
                : -forwardWorldDir;
            resolvedRouteSource = $"combat_space:lane={laneKey}";
            return true;
        }

        static bool IsWithinCombatProjectionEnvelope(float forwardOffset, float lateralOffset)
        {
            if (!float.IsFinite(forwardOffset) || !float.IsFinite(lateralOffset))
                return false;

            if (forwardOffset > FrontGateForwardOffset + CombatProjectionForwardSlack)
                return false;
            if (forwardOffset < -CombatProjectionRearSlack)
                return false;

            return Mathf.Abs(lateralOffset) <= CombatProjectionLateralLimit;
        }

        static bool IsCombatSnapshotUnit(MLUnit unit)
        {
            if (unit == null)
                return false;

            bool isCombatState =
                string.Equals(unit.movementState, "COMBAT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unit.state, "COMBAT", StringComparison.OrdinalIgnoreCase);
            bool hasCombatTarget =
                !string.IsNullOrWhiteSpace(unit.combatTargetId)
                || !string.IsNullOrWhiteSpace(unit.blockedByStructureId)
                || unit.blockedByStructure
                || unit.isAttacking;

            return isCombatState || hasCombatTarget;
        }

        bool TryResolveDesiredFacing(
            MLSnapshot snap,
            MLUnit unit,
            MLLaneSnap lane,
            int spatialLane,
            Vector3 worldPos,
            out Vector3 facing,
            out bool lockCombatFacing)
        {
            facing = Vector3.zero;
            lockCombatFacing = false;

            if (TryResolveCombatFacing(snap, unit, lane, worldPos, spatialLane, out facing))
            {
                lockCombatFacing = true;
                return true;
            }

            if (TryResolveSnapshotFacing(lane, unit, spatialLane, out facing))
                return facing.sqrMagnitude > 0.0001f;

            return false;
        }

        bool TryResolveCombatFacing(
            MLSnapshot snap,
            MLUnit unit,
            MLLaneSnap lane,
            Vector3 worldPos,
            int spatialLane,
            out Vector3 facing)
        {
            facing = Vector3.zero;
            if (unit == null || lane == null || !IsCombatSnapshotUnit(unit))
                return false;

            string targetId = !string.IsNullOrWhiteSpace(unit.combatTargetId)
                ? unit.combatTargetId
                : unit.blockedByStructureId;

            if (TryResolveCombatTargetWorldPosition(snap, targetId, lane, out Vector3 targetWorld))
            {
                Vector3 targetDelta = targetWorld - worldPos;
                targetDelta.y = 0f;
                if (targetDelta.sqrMagnitude > 0.0001f)
                {
                    facing = targetDelta.normalized;
                    return true;
                }
            }

            if (TryResolveSnapshotFacing(lane, unit, spatialLane, out Vector3 routeFacing))
            {
                routeFacing.y = 0f;
                if (routeFacing.sqrMagnitude > 0.0001f)
                {
                    facing = routeFacing.normalized;
                    return true;
                }
            }

            return false;
        }

        bool TryResolveCombatTargetWorldPosition(
            MLSnapshot snap,
            string targetId,
            MLLaneSnap sourceLane,
            out Vector3 worldPos)
        {
            worldPos = default;
            if (string.IsNullOrWhiteSpace(targetId))
                return false;

            if (TryResolveFortressPadWorldPosition(targetId, sourceLane, out worldPos))
                return true;

            if (snap?.lanes != null)
            {
                for (int laneIndex = 0; laneIndex < snap.lanes.Length; laneIndex++)
                {
                    MLLaneSnap lane = snap.lanes[laneIndex];
                    if (lane?.units == null)
                        continue;

                    int spatialLane = (uint)lane.laneIndex < (uint)_branchMap.Length
                        ? _branchMap[lane.laneIndex]
                        : lane.laneIndex;
                    for (int unitIndex = 0; unitIndex < lane.units.Length; unitIndex++)
                    {
                        MLUnit candidate = lane.units[unitIndex];
                        if (candidate == null
                            || !string.Equals(candidate.id, targetId, System.StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return TryResolveSnapshotWorldPosition(candidate, lane, spatialLane, out worldPos, out _);
                    }
                }
            }

            if (_views.TryGetValue(targetId, out WaveView targetView) && targetView?.go != null)
            {
                worldPos = targetView.hadSnapshot
                    ? targetView.snapshotWorldPos
                    : targetView.go.transform.position;
                return true;
            }

            return false;
        }

        bool TryResolveFortressPadWorldPosition(string targetId, MLLaneSnap lane, out Vector3 worldPos)
        {
            worldPos = default;
            if (string.IsNullOrWhiteSpace(targetId))
                return false;

            _fortressAnchorScratch.Clear();
            FortressPadAnchor.CollectAnchors(_fortressAnchorScratch);
            for (int i = 0; i < _fortressAnchorScratch.Count; i++)
            {
                FortressPadAnchor anchor = _fortressAnchorScratch[i];
                if (anchor == null || !anchor.MatchesPad(targetId))
                    continue;
                if (lane != null && !anchor.MatchesLane(lane.slotColor, lane.laneIndex))
                    continue;

                worldPos = anchor.FocusTransform.position;
                return true;
            }

            return false;
        }

        void RefreshBattlefieldRouteNodeMap(MLSnapshot snap)
        {
            _battlefieldRouteNodeLaneByNode.Clear();

            for (int laneIndex = 0; laneIndex < 4; laneIndex++)
            {
                string laneKey = ResolveFixedLaneKeyForLaneIndex(laneIndex);
                string routeNode = ResolveDefaultRouteNodeForLaneIndex(laneIndex);
                if (!string.IsNullOrWhiteSpace(routeNode) && !string.IsNullOrWhiteSpace(laneKey))
                    _battlefieldRouteNodeLaneByNode[routeNode] = laneKey;

                string waveRouteNode = ResolveWaveRouteNodeForLaneIndex(laneIndex);
                if (!string.IsNullOrWhiteSpace(waveRouteNode) && !string.IsNullOrWhiteSpace(laneKey))
                    _battlefieldRouteNodeLaneByNode[waveRouteNode] = laneKey;
            }

            if (snap?.lanes == null)
                return;

            for (int i = 0; i < snap.lanes.Length; i++)
            {
                MLLaneSnap lane = snap.lanes[i];
                if (lane == null)
                    continue;

                string laneKey = NormalizeBattlefieldLaneKey(lane.slotColor);
                string routeNode = ResolveDefaultRouteNodeForLaneIndex(lane.laneIndex);
                if (!string.IsNullOrWhiteSpace(routeNode) && !string.IsNullOrWhiteSpace(laneKey))
                    _battlefieldRouteNodeLaneByNode[routeNode] = laneKey;

                string waveRouteNode = ResolveWaveRouteNodeForLaneIndex(lane.laneIndex);
                if (!string.IsNullOrWhiteSpace(waveRouteNode) && !string.IsNullOrWhiteSpace(laneKey))
                    _battlefieldRouteNodeLaneByNode[waveRouteNode] = laneKey;
            }

            for (int i = 0; i < snap.lanes.Length; i++)
            {
                MLLaneSnap lane = snap.lanes[i];
                if (lane?.units == null)
                    continue;

                string targetLaneKey = NormalizeBattlefieldLaneKey(lane.slotColor);
                for (int unitIndex = 0; unitIndex < lane.units.Length; unitIndex++)
                {
                    MLUnit unit = lane.units[unitIndex];
                    if (unit == null)
                        continue;

                    if (TryNormalizeRouteNodeId(unit.routeTargetNode, out string routeTargetNode) && !string.IsNullOrWhiteSpace(targetLaneKey))
                        _battlefieldRouteNodeLaneByNode[routeTargetNode] = targetLaneKey;

                    if (!TryNormalizeRouteNodeId(unit.routeStartNode, out string routeStartNode))
                        continue;

                    var sourceLane = FindLaneByIndex(snap, unit.sourceLaneIndex);
                    string sourceLaneKey = NormalizeBattlefieldLaneKey(sourceLane?.slotColor);
                    if (!string.IsNullOrWhiteSpace(sourceLaneKey))
                        _battlefieldRouteNodeLaneByNode[routeStartNode] = sourceLaneKey;
                }
            }
        }

        static MLLaneSnap FindLaneByIndex(MLSnapshot snap, int laneIndex)
        {
            if (snap?.lanes == null || laneIndex < 0)
                return null;

            for (int i = 0; i < snap.lanes.Length; i++)
            {
                var lane = snap.lanes[i];
                if (lane != null && lane.laneIndex == laneIndex)
                    return lane;
            }

            return null;
        }

        bool TryResolveSnapshotWorldPosition(MLUnit unit, MLLaneSnap lane, int spatialLane, out Vector3 worldPos, out string failureReason)
        {
            worldPos = default;
            failureReason = null;
            string laneFailureKey = $"battlefield_routes_lane_{lane?.laneIndex ?? -1}";
            Vector3 markerWorld = ResolveLaneFailureMarkerWorld(spatialLane);

            if (TryResolveBattlefieldRouteWorldPosition(
                unit,
                lane,
                out worldPos,
                out Vector3 routeForward,
                out string resolvedRouteSource,
                out string routeFailure))
            {
                RuntimeFailureMarker.Clear(transform, $"wave_failure_{laneFailureKey}");
                worldPos = ProjectToVisibleGround(worldPos);
                failureReason = "resolved_via_battlefield_routes";
                Debug.Log(
                    $"[SpawnAudit][ClientSnapshot] spawnType='{ResolveSpawnType(unit)}' unitId='{unit?.id ?? "<null>"}' unitType='{unit?.type ?? "<null>"}' " +
                    $"skinKey='{unit?.skinKey ?? "<default>"}' sourceTeam='{unit?.sourceTeam ?? "<none>"}' " +
                    $"ownerLane={unit?.ownerLane ?? -1} isWaveUnit={(unit != null && unit.isWaveUnit).ToString().ToLowerInvariant()} " +
                    $"isDefender={(unit != null && unit.isDefender).ToString().ToLowerInvariant()} " +
                    $"lane={lane?.laneIndex ?? -1} spatialLane={spatialLane} team='{lane?.team ?? "<null>"}' " +
                    $"sourceLane={unit?.sourceLaneIndex ?? -1} sourceBarracksKey='{ResolveSourceBarracksKey(unit)}' " +
                    $"routeType='{unit?.routeType ?? "<null>"}' currentSegment='{unit?.currentSegment ?? "<null>"}' " +
                    $"segmentProgress={(unit != null ? unit.segmentProgress : float.NaN):0.###} " +
                    $"routeWorld=({(unit != null ? unit.routeWorldX : float.NaN):0.###},{(unit != null ? unit.routeWorldY : float.NaN):0.###}) " +
                    $"resolvedLookupKey='{resolvedRouteSource ?? "<unknown>"}' worldPos=({worldPos.x:0.###},{worldPos.y:0.###},{worldPos.z:0.###})",
                    this);
                return true;
            }

            failureReason =
                $"battlefield_route_resolution_failed pathIdx={(unit != null ? unit.pathIdx : float.NaN):0.###} " +
                $"routeType='{unit?.routeType ?? "<null>"}' segment='{unit?.currentSegment ?? "<null>"}' " +
                $"segmentProgress={(unit != null ? unit.segmentProgress : float.NaN):0.###} " +
                $"routeWorld=({(unit != null ? unit.routeWorldX : float.NaN):0.###},{(unit != null ? unit.routeWorldY : float.NaN):0.###}) " +
                $"failure='{routeFailure ?? "<unknown>"}'";
            ReportWaveFailure(
                laneFailureKey,
                markerWorld,
                $"[WaveSnapshotRuntimeSpawner] Battlefield Routes failed to resolve lane={lane?.laneIndex ?? -1} " +
                $"unit='{unit?.id ?? "<null>"}' type='{unit?.type ?? "<null>"}' " +
                $"segment='{unit?.currentSegment ?? "<null>"}' routeType='{unit?.routeType ?? "<null>"}' " +
                $"failure='{routeFailure ?? "<unknown>"}'.");
            return false;
        }

        static Vector3 ApplyWaveLateralOffset(Vector3 worldPos, float lateralOffset, int spatialLane)
        {
            if (Mathf.Abs(lateralOffset) <= 0.0001f)
                return worldPos;

            Vector3 lateral = TileGrid.GetLaneLateralDir(spatialLane);
            lateral.y = 0f;
            if (lateral.sqrMagnitude <= 0.0001f)
                return worldPos;

            return worldPos + lateral.normalized * lateralOffset;
        }

        static Vector3 ProjectToVisibleGround(Vector3 worldPos)
        {
            float probeOriginY = Mathf.Max(worldPos.y + GroundProbeHeight, GroundProbeHeight);
            var rayOrigin = new Vector3(worldPos.x, probeOriginY, worldPos.z);
            int hitCount = Physics.RaycastNonAlloc(
                rayOrigin,
                Vector3.down,
                GroundHitBuffer,
                GroundProbeHeight + GroundProbeDistance,
                ~0,
                QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            float bestY = worldPos.y;

            for (int i = 0; i < hitCount; i++)
            {
                var hit = GroundHitBuffer[i];
                if (!IsGroundCandidate(hit))
                    continue;

                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    bestY = hit.point.y + GroundClearance;
                }
            }

            if (!float.IsPositiveInfinity(bestDistance))
                worldPos.y = bestY;

            return worldPos;
        }

        static bool IsGroundCandidate(RaycastHit hit)
        {
            if (hit.collider == null || hit.normal.y < GroundNormalThreshold)
                return false;

            Transform hitTransform = hit.collider.transform;
            if (hitTransform == null)
                return false;

            if (hitTransform.GetComponentInParent<LaneSnapshotCombatant>() != null)
                return false;

            return true;
        }

        Quaternion ResolveSnapshotSpawnRotation(MLUnit unit, MLLaneSnap lane, int spatialLane)
        {
            if (TryResolveSnapshotFacing(lane, unit, spatialLane, out Vector3 facing))
                return Quaternion.LookRotation(facing, Vector3.up);

            return Quaternion.LookRotation(TileGrid.GetLaneForwardDir(spatialLane), Vector3.up);
        }

        bool TryResolveSnapshotFacing(MLLaneSnap lane, MLUnit unit, int spatialLane, out Vector3 facing)
        {
            facing = Vector3.zero;
            if (TryResolveBattlefieldRouteWorldPosition(
                unit,
                lane,
                out _,
                out Vector3 routeForward,
                out _,
                out _))
            {
                facing = routeForward;
                return facing.sqrMagnitude > 0.0001f;
            }

            return false;
        }

        bool TryResolveBattlefieldRouteWorldPosition(
            MLUnit unit,
            MLLaneSnap lane,
            out Vector3 worldPos,
            out Vector3 routeForward,
            out string resolvedRouteSource,
            out string failureReason)
        {
            worldPos = default;
            routeForward = TileGrid.GetLaneForwardDir(lane?.laneIndex ?? 0);
            resolvedRouteSource = null;
            failureReason = null;

            if (unit == null)
            {
                failureReason = "unit is null";
                return false;
            }

            if (TryResolveLiveCombatWorldPosition(unit, lane, out worldPos, out routeForward, out resolvedRouteSource))
                return true;

            if (!TryResolveBattlefieldSegment(unit, lane, out string fromNode, out string toNode, out string segmentId, out failureReason))
                return false;

            if (!TryResolveBattlefieldNodeWorld(fromNode, out Vector3 fromWorld, out string fromLaneKey, out string fromReason))
            {
                failureReason = fromReason;
                return false;
            }

            if (!TryResolveBattlefieldNodeWorld(toNode, out Vector3 toWorld, out string toLaneKey, out string toReason))
            {
                failureReason = toReason;
                return false;
            }

            if (!TryResolveBattlefieldSegmentProgress(unit, out float segmentProgress))
            {
                failureReason = $"segmentProgress is invalid ({unit.segmentProgress:0.###})";
                return false;
            }

            Vector3 p0 = fromWorld;
            Vector3 p1;
            Vector3 p2 = default;
            int pointCount;
            string segmentShape;

            if (IsBarracksLinkSegment(fromNode, toNode))
            {
                p1 = toWorld;
                pointCount = 2;
                segmentShape = "barracks_link";
            }
            else if (IsWaveLaneSegment(fromNode, toNode))
            {
                p1 = toWorld;
                pointCount = 2;
                segmentShape = "wave_lane";
            }
            else if (IsMineBridgeSegment(fromNode, toNode))
            {
                p1 = toWorld;
                pointCount = 2;
                segmentShape = "center_bridge";
            }
            else if (IsCenterCrossSegment(fromNode, toNode))
            {
                if (_battlefieldMineAnchor == null)
                {
                    failureReason = "Wave center anchor is missing for center-cross route.";
                    LogBattlefieldRouteFailureOnce("anchor:mine_missing", failureReason);
                    return false;
                }

                Vector3 mineWorld = _battlefieldMineAnchor.position;
                p1 = mineWorld;
                p2 = toWorld;
                pointCount = 3;
                segmentShape = "center_cross";
            }
            else
            {
                if (_battlefieldMineAnchor == null)
                {
                    failureReason = "Wave center anchor is missing for perimeter route.";
                    LogBattlefieldRouteFailureOnce("anchor:mine_missing", failureReason);
                    return false;
                }

                Vector3 mineWorld = _battlefieldMineAnchor.position;
                p1 = ResolvePerimeterControlPoint(fromWorld, toWorld, mineWorld);
                p2 = toWorld;
                pointCount = 3;
                segmentShape = "outer_perimeter";
            }

            if (!TryResolveAuthoritativeRouteOffsets(
                unit,
                segmentId,
                segmentProgress,
                out float longitudinalOffset,
                out float lateralOffset,
                out string offsetFailure))
            {
                failureReason = offsetFailure;
                return false;
            }

            if (!TrySampleBattlefieldPolyline(
                p0,
                p1,
                p2,
                pointCount,
                segmentProgress,
                longitudinalOffset,
                lateralOffset,
                out worldPos,
                out routeForward))
            {
                failureReason = $"failed to sample segment '{segmentId}'";
                return false;
            }

            resolvedRouteSource =
                $"battlefield_routes:{segmentShape}:{segmentId}:from={fromLaneKey}:to={toLaneKey}";
            return true;
        }

        bool TryResolveBattlefieldSegment(
            MLUnit unit,
            MLLaneSnap lane,
            out string fromNode,
            out string toNode,
            out string segmentId,
            out string failureReason)
        {
            fromNode = null;
            toNode = null;
            segmentId = null;
            failureReason = null;

            if (TryParseBattlefieldSegmentId(unit?.currentSegment, out fromNode, out toNode))
            {
                segmentId = $"{fromNode}_{toNode}";
                return true;
            }

            if (TryNormalizeRouteNodeId(unit?.routeStartNode, out fromNode)
                && TryNormalizeRouteNodeId(unit?.routeTargetNode, out toNode))
            {
                segmentId = $"{fromNode}_{toNode}";
                return true;
            }

            failureReason =
                $"Missing battlefield segment data. currentSegment='{unit?.currentSegment ?? "<null>"}' " +
                $"routeStartNode='{unit?.routeStartNode ?? "<null>"}' routeTargetNode='{unit?.routeTargetNode ?? "<null>"}'.";
            return false;
        }

        static bool TryResolveBattlefieldSegmentProgress(MLUnit unit, out float segmentProgress)
        {
            segmentProgress = 0f;
            if (unit == null || !float.IsFinite(unit.segmentProgress))
                return false;

            segmentProgress = Mathf.Clamp01(unit.segmentProgress);
            return true;
        }

        bool TryResolveBattlefieldNodeWorld(string nodeId, out Vector3 worldPos, out string laneKey, out string failureReason)
        {
            worldPos = default;
            laneKey = null;
            failureReason = null;

            if (!TryNormalizeRouteNodeId(nodeId, out string normalizedNode))
            {
                failureReason = $"Unsupported route node '{nodeId ?? "<null>"}'.";
                return false;
            }

            if (string.Equals(normalizedNode, RouteMineNode, StringComparison.OrdinalIgnoreCase))
            {
                if (_battlefieldMineAnchor == null)
                {
                    failureReason = "WaveSpawnAnchor(mine_center) is missing.";
                    return false;
                }

                worldPos = _battlefieldMineAnchor.position;
                laneKey = "mine_center";
                return true;
            }

            if (TryParseBarracksRouteNodeId(normalizedNode, out int barracksLaneIndex, out string barracksId))
            {
                string coreNodeId = ResolveDefaultRouteNodeForLaneIndex(barracksLaneIndex);
                if (string.IsNullOrWhiteSpace(coreNodeId)
                    || !_battlefieldRouteNodeLaneByNode.TryGetValue(coreNodeId, out string barracksLaneKey))
                {
                    failureReason = $"No lane mapping found for barracks route node '{normalizedNode}'.";
                    LogBattlefieldRouteFailureOnce($"node:{normalizedNode}:lane_missing", failureReason);
                    return false;
                }

                barracksLaneKey = NormalizeBattlefieldLaneKey(barracksLaneKey);
                laneKey = barracksLaneKey;
                string barracksKey = BuildBattlefieldBarracksKey(barracksLaneKey, barracksId);
                if (!_battlefieldBarracksByLaneAndId.TryGetValue(barracksKey, out worldPos))
                {
                    failureReason = $"BarracksSiteView anchor is missing for lane '{barracksLaneKey}' barracks '{barracksId}'.";
                    LogBattlefieldRouteFailureOnce($"anchor:barracks:{barracksLaneKey}:{barracksId}", failureReason);
                    return false;
                }

                return true;
            }

            if (!_battlefieldRouteNodeLaneByNode.TryGetValue(normalizedNode, out string mappedLaneKey))
            {
                failureReason = $"No lane mapping found for route node '{normalizedNode}'.";
                LogBattlefieldRouteFailureOnce($"node:{normalizedNode}:lane_missing", failureReason);
                return false;
            }

            mappedLaneKey = NormalizeBattlefieldLaneKey(mappedLaneKey);
            laneKey = mappedLaneKey;
            if (string.IsNullOrWhiteSpace(mappedLaneKey))
            {
                failureReason = $"Resolved route node '{normalizedNode}' to an empty lane key.";
                LogBattlefieldRouteFailureOnce($"node:{normalizedNode}:lane_empty", failureReason);
                return false;
            }

            if (_battlefieldRouteNodeWorldByNode.TryGetValue(normalizedNode, out worldPos))
                return true;

            failureReason = $"Route node '{normalizedNode}' has no authored world anchor for lane '{mappedLaneKey}'.";
            LogBattlefieldRouteFailureOnce($"node:{normalizedNode}:world_missing", failureReason);
            return false;
        }

        static bool TryParseBattlefieldSegmentId(string segmentId, out string fromNode, out string toNode)
        {
            fromNode = null;
            toNode = null;
            if (string.IsNullOrWhiteSpace(segmentId))
                return false;

            string[] parts = segmentId.Trim().ToUpperInvariant().Split('_');
            if (parts.Length != 2)
                return false;

            if (!TryNormalizeRouteNodeId(parts[0], out fromNode)
                || !TryNormalizeRouteNodeId(parts[1], out toNode))
            {
                return false;
            }

            return true;
        }

        static bool TryNormalizeRouteNodeId(string nodeId, out string normalizedNode)
        {
            normalizedNode = string.IsNullOrWhiteSpace(nodeId)
                ? null
                : nodeId.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedNode))
                return false;

            return string.Equals(normalizedNode, RouteMineNode, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteWaveNodeA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteWaveNodeB, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteWaveNodeC, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteWaveNodeD, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteNodeA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteNodeB, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteNodeC, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedNode, RouteNodeD, StringComparison.OrdinalIgnoreCase)
                || TryParseBarracksRouteNodeId(normalizedNode, out _, out _);
        }

        static bool IsWaveSpawnNode(string nodeId)
        {
            return string.Equals(nodeId, RouteWaveNodeA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeB, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeC, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeD, StringComparison.OrdinalIgnoreCase);
        }

        static string ResolveWaveRouteNodeForLaneIndex(int laneIndex)
        {
            return laneIndex switch
            {
                0 => RouteWaveNodeA,
                1 => RouteWaveNodeB,
                2 => RouteWaveNodeC,
                3 => RouteWaveNodeD,
                _ => null,
            };
        }

        static int ResolveLaneIndexForRouteNode(string nodeId)
        {
            if (TryParseBarracksRouteNodeId(nodeId, out int barracksLaneIndex, out _))
                return barracksLaneIndex;

            if (string.Equals(nodeId, RouteNodeA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeA, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (string.Equals(nodeId, RouteNodeB, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeB, StringComparison.OrdinalIgnoreCase))
                return 1;
            if (string.Equals(nodeId, RouteNodeC, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeC, StringComparison.OrdinalIgnoreCase))
                return 2;
            if (string.Equals(nodeId, RouteNodeD, StringComparison.OrdinalIgnoreCase)
                || string.Equals(nodeId, RouteWaveNodeD, StringComparison.OrdinalIgnoreCase))
                return 3;
            return -1;
        }

        static bool TryResolveRouteTowardMineDirection(int spatialLane, out Vector3 direction)
        {
            direction = -TileGrid.GetLaneForwardDir(spatialLane);
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return false;

            direction = direction.normalized;
            return true;
        }

        static string GetBarracksRouteNodeId(string coreNodeId, string barracksId)
        {
            if (string.IsNullOrWhiteSpace(coreNodeId) || string.IsNullOrWhiteSpace(barracksId))
                return null;

            string suffix = BarracksActivityUtility.NormalizeBarracksId(barracksId) switch
            {
                "center" => BarracksRouteNodeCenterSuffix,
                "left" => BarracksRouteNodeLeftSuffix,
                "right" => BarracksRouteNodeRightSuffix,
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(suffix))
                return null;

            return $"{coreNodeId.Trim().ToUpperInvariant()}{suffix}";
        }

        static bool TryParseBarracksRouteNodeId(string nodeId, out int laneIndex, out string barracksId)
        {
            laneIndex = -1;
            barracksId = null;

            string normalizedNode = string.IsNullOrWhiteSpace(nodeId)
                ? null
                : nodeId.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(normalizedNode) || normalizedNode.Length <= 1)
                return false;

            string coreNodeId = normalizedNode[..1];
            laneIndex = coreNodeId switch
            {
                RouteNodeA => 0,
                RouteNodeB => 1,
                RouteNodeC => 2,
                RouteNodeD => 3,
                _ => -1,
            };
            if (laneIndex < 0)
                return false;

            string suffix = normalizedNode[1..];
            barracksId = suffix switch
            {
                BarracksRouteNodeCenterSuffix => "center",
                BarracksRouteNodeLeftSuffix => "left",
                BarracksRouteNodeRightSuffix => "right",
                _ => null,
            };
            return !string.IsNullOrWhiteSpace(barracksId);
        }

        static bool IsWaveLaneSegment(string fromNode, string toNode)
        {
            return (string.Equals(fromNode, RouteWaveNodeA, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeA, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fromNode, RouteWaveNodeB, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeB, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fromNode, RouteWaveNodeC, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeC, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fromNode, RouteWaveNodeD, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeD, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsBarracksLinkSegment(string fromNode, string toNode)
        {
            return TryParseBarracksRouteNodeId(fromNode, out int laneIndex, out _)
                && string.Equals(ResolveDefaultRouteNodeForLaneIndex(laneIndex), toNode, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsCenterCrossSegment(string fromNode, string toNode)
        {
            return (string.Equals(fromNode, RouteNodeA, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeC, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fromNode, RouteNodeC, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeA, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fromNode, RouteNodeB, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeD, StringComparison.OrdinalIgnoreCase))
                || (string.Equals(fromNode, RouteNodeD, StringComparison.OrdinalIgnoreCase) && string.Equals(toNode, RouteNodeB, StringComparison.OrdinalIgnoreCase));
        }

        static bool IsMineBridgeSegment(string fromNode, string toNode)
        {
            bool fromIsMine = string.Equals(fromNode, RouteMineNode, StringComparison.OrdinalIgnoreCase);
            bool toIsMine = string.Equals(toNode, RouteMineNode, StringComparison.OrdinalIgnoreCase);
            if (fromIsMine == toIsMine)
                return false;

            string laneNode = fromIsMine ? toNode : fromNode;
            return string.Equals(laneNode, RouteNodeA, StringComparison.OrdinalIgnoreCase)
                || string.Equals(laneNode, RouteNodeB, StringComparison.OrdinalIgnoreCase)
                || string.Equals(laneNode, RouteNodeC, StringComparison.OrdinalIgnoreCase)
                || string.Equals(laneNode, RouteNodeD, StringComparison.OrdinalIgnoreCase);
        }

        static Vector3 ResolvePerimeterControlPoint(Vector3 from, Vector3 to, Vector3 mineCenter)
        {
            Vector3 midpoint = (from + to) * 0.5f;
            Vector3 outward = midpoint - mineCenter;
            outward.y = 0f;
            if (outward.sqrMagnitude <= 0.0001f)
            {
                Vector3 edge = to - from;
                edge.y = 0f;
                outward = Vector3.Cross(Vector3.up, edge);
            }

            if (outward.sqrMagnitude <= 0.0001f)
                outward = Vector3.forward;

            float controlDistance = Mathf.Max(2f, Vector3.Distance(from, to) * PerimeterRouteControlScale);
            return midpoint + outward.normalized * controlDistance;
        }

        static bool TrySampleBattlefieldPolyline(
            Vector3 p0,
            Vector3 p1,
            Vector3 p2,
            int pointCount,
            float progress,
            float longitudinalOffset,
            float lateralOffset,
            out Vector3 worldPos,
            out Vector3 routeForward)
        {
            worldPos = p0;
            routeForward = Vector3.forward;
            float clampedProgress = Mathf.Clamp01(progress);

            if (pointCount <= 1)
                return false;

            if (pointCount == 2)
            {
                worldPos = Vector3.Lerp(p0, p1, clampedProgress);
                routeForward = (p1 - p0).normalized;
                worldPos = ApplyRouteOffsets(worldPos, routeForward, longitudinalOffset, lateralOffset);
                return true;
            }

            float len01 = Vector3.Distance(p0, p1);
            float len12 = Vector3.Distance(p1, p2);
            float totalLen = len01 + len12;
            if (totalLen <= 0.0001f)
                return false;

            float targetDistance = totalLen * clampedProgress;
            if (targetDistance <= len01 || len12 <= 0.0001f)
            {
                float segmentT = len01 > 0.0001f ? targetDistance / len01 : 0f;
                worldPos = Vector3.Lerp(p0, p1, segmentT);
                routeForward = (p1 - p0).normalized;
            }
            else
            {
                float segmentDistance = targetDistance - len01;
                float segmentT = len12 > 0.0001f ? segmentDistance / len12 : 0f;
                worldPos = Vector3.Lerp(p1, p2, segmentT);
                routeForward = (p2 - p1).normalized;
            }

            worldPos = ApplyRouteOffsets(worldPos, routeForward, longitudinalOffset, lateralOffset);
            return true;
        }

        static Vector3 ApplyRouteOffsets(Vector3 worldPos, Vector3 routeForward, float longitudinalOffset, float lateralOffset)
        {
            Vector3 forwardFlat = routeForward;
            forwardFlat.y = 0f;
            if (forwardFlat.sqrMagnitude <= 0.0001f)
                return worldPos;

            Vector3 normalizedForward = forwardFlat.normalized;
            if (Mathf.Abs(longitudinalOffset) > 0.0001f)
                worldPos += normalizedForward * longitudinalOffset;

            if (Mathf.Abs(lateralOffset) > 0.0001f)
            {
                Vector3 lateral = Vector3.Cross(normalizedForward, Vector3.up);
                if (lateral.sqrMagnitude > 0.0001f)
                    worldPos += lateral.normalized * lateralOffset;
            }

            return worldPos;
        }

        static bool TryResolveAuthoritativeRouteOffsets(
            MLUnit unit,
            string segmentId,
            float segmentProgress,
            out float longitudinalOffset,
            out float lateralOffset,
            out string failureReason)
        {
            longitudinalOffset = 0f;
            lateralOffset = 0f;
            failureReason = null;

            if (unit == null)
            {
                failureReason = "unit is null";
                return false;
            }

            if (!TrySampleRouteSpacePolyline(segmentId, segmentProgress, out Vector2 routeCenter, out Vector2 routeTangent))
            {
                failureReason = $"route-space sample failed for segment '{segmentId}'";
                return false;
            }

            if (!TryResolveAuthoritativeRouteSamplePoint(unit, out Vector2 authoritativePoint, out string pointFailure))
            {
                failureReason = pointFailure;
                return false;
            }

            Vector2 tangent = routeTangent.sqrMagnitude > 0.0001f ? routeTangent.normalized : Vector2.up;
            Vector2 lateral = new(-tangent.y, tangent.x);
            Vector2 delta = authoritativePoint - routeCenter;
            longitudinalOffset = Vector2.Dot(delta, tangent);
            lateralOffset = Vector2.Dot(delta, lateral);
            return true;
        }

        static bool TryResolveAuthoritativeRouteSamplePoint(MLUnit unit, out Vector2 point, out string failureReason)
        {
            point = Vector2.zero;
            failureReason = null;

            if (unit == null)
            {
                failureReason = "unit is null";
                return false;
            }

            if (float.IsFinite(unit.gridX) && float.IsFinite(unit.gridY))
            {
                point = new Vector2(unit.gridX, unit.gridY);
                return true;
            }

            if (float.IsFinite(unit.routeWorldX) && float.IsFinite(unit.routeWorldY))
            {
                point = new Vector2(unit.routeWorldX, unit.routeWorldY);
                return true;
            }

            failureReason =
                $"authoritative sim point is invalid grid=({unit.gridX:0.###},{unit.gridY:0.###}) " +
                $"routeWorld=({unit.routeWorldX:0.###},{unit.routeWorldY:0.###})";
            return false;
        }

        static bool TrySampleRouteSpacePolyline(string segmentId, float progress, out Vector2 routeCenter, out Vector2 routeTangent)
        {
            routeCenter = Vector2.zero;
            routeTangent = Vector2.up;
            if (!RouteSegmentSimPolylines.TryGetValue(segmentId ?? string.Empty, out Vector2[] points) || points == null || points.Length < 2)
                return false;

            float clampedProgress = Mathf.Clamp01(progress);
            if (points.Length == 2)
            {
                routeCenter = Vector2.Lerp(points[0], points[1], clampedProgress);
                routeTangent = points[1] - points[0];
                return routeTangent.sqrMagnitude > 0.0001f;
            }

            float len01 = Vector2.Distance(points[0], points[1]);
            float len12 = Vector2.Distance(points[1], points[2]);
            float totalLength = len01 + len12;
            if (totalLength <= 0.0001f)
                return false;

            float targetDistance = totalLength * clampedProgress;
            if (targetDistance <= len01 || len12 <= 0.0001f)
            {
                float segmentT = len01 > 0.0001f ? targetDistance / len01 : 0f;
                routeCenter = Vector2.Lerp(points[0], points[1], segmentT);
                routeTangent = points[1] - points[0];
                return routeTangent.sqrMagnitude > 0.0001f;
            }

            float segmentDistance = targetDistance - len01;
            float secondSegmentT = len12 > 0.0001f ? segmentDistance / len12 : 0f;
            routeCenter = Vector2.Lerp(points[1], points[2], secondSegmentT);
            routeTangent = points[2] - points[1];
            return routeTangent.sqrMagnitude > 0.0001f;
        }

        static string ResolveSourceBarracksKey(MLUnit unit)
        {
            if (!string.IsNullOrWhiteSpace(unit?.sourceBarracksKey))
                return unit.sourceBarracksKey.Trim().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(unit?.sourceBarracksId))
                return unit.sourceBarracksId.Trim().ToLowerInvariant();
            return string.Empty;
        }

        static string ResolveDefaultRouteNodeForLaneIndex(int laneIndex)
        {
            return laneIndex switch
            {
                0 => RouteNodeA,
                1 => RouteNodeB,
                2 => RouteNodeC,
                3 => RouteNodeD,
                _ => null,
            };
        }

        static string ResolveFixedLaneKeyForLaneIndex(int laneIndex)
        {
            return laneIndex switch
            {
                0 => "red",
                1 => "yellow",
                2 => "blue",
                3 => "green",
                _ => string.Empty,
            };
        }

        static string ResolveFrontGateObjectNameForLaneKey(string laneKey)
        {
            return NormalizeBattlefieldLaneKey(laneKey) switch
            {
                "red" => "Red_Gate_Front",
                "yellow" => "Yellow_Gate_Front",
                "blue" => "Blue_Gate_Front",
                "green" => "Green_Gate_Front",
                _ => string.Empty,
            };
        }

        static string NormalizeBattlefieldLaneKey(string slotColor)
        {
            if (string.IsNullOrWhiteSpace(slotColor))
                return string.Empty;

            return FortressPadAnchor.NormalizeLaneKey(slotColor);
        }

        static string BuildBattlefieldBarracksKey(string laneKey, string barracksId)
        {
            return $"{NormalizeBattlefieldLaneKey(laneKey)}:{BarracksActivityUtility.NormalizeBarracksId(barracksId)}";
        }

        void LogBattlefieldRouteFailureOnce(string key, string message)
        {
            if (!_loggedBattlefieldRouteErrors.Add(key))
                return;

            Debug.LogError($"[BattlefieldRoutes] {message}", this);
        }

        static string ResolveSpawnType(MLUnit unit)
        {
            if (unit == null)
                return "unknown";
            if (!string.IsNullOrWhiteSpace(unit.spawnSourceType))
            {
                string normalized = unit.spawnSourceType.Trim().ToLowerInvariant();
                return string.Equals(normalized, "scheduled_wave", StringComparison.OrdinalIgnoreCase)
                    ? "dungeon_wave"
                    : normalized;
            }
            if (unit.isHero)
                return "barracks_hero";
            if (!string.IsNullOrWhiteSpace(ResolveSourceBarracksKey(unit)))
                return "barracks_roster";
            if (string.Equals(BattleTeamUtility.NormalizeServerTeamKey(unit.allegianceKey), "dungeon", StringComparison.OrdinalIgnoreCase))
                return "dungeon_wave";
            return unit.isWaveUnit ? "dungeon_wave" : "snapshot_unit";
        }

        void ReportWaveFailure(string key, Vector3 worldPos, string message)
        {
            if (_loggedFailureKeys.Add(key))
                Debug.LogError(message, this);

            RuntimeFailureMarker.MarkWorld(transform, $"wave_failure_{key}", worldPos, message);
        }

        static Vector3 ResolveLaneFailureMarkerWorld(int spatialLane)
        {
            return TileGrid.TileToWorld(spatialLane, 5, 0) + Vector3.up * 2.4f;
        }

        static string ResolveDefenderTeamKey(MLLaneSnap lane)
        {
            string slotTeamKey = BattleTeamUtility.NormalizeServerTeamKey(lane?.slotColor);
            if (!string.IsNullOrWhiteSpace(slotTeamKey))
                return slotTeamKey;

            var sa = SnapshotApplier.Instance;
            var assignment = sa?.GetLaneAssignment(lane?.laneIndex ?? -1);

            string assignmentSlotTeamKey = BattleTeamUtility.NormalizeServerTeamKey(assignment?.slotColor);
            if (!string.IsNullOrWhiteSpace(assignmentSlotTeamKey))
                return assignmentSlotTeamKey;

            string teamKey = BattleTeamUtility.NormalizeServerTeamKey(lane?.team);
            if (!string.IsNullOrWhiteSpace(teamKey))
                return teamKey;

            string assignmentTeam = BattleTeamUtility.NormalizeServerTeamKey(assignment?.team);
            return !string.IsNullOrWhiteSpace(assignmentTeam)
                ? assignmentTeam
                : string.Empty;
        }

        static string ResolveOwnerTeamKey(MLUnit unit, MLLaneSnap lane, string defenderTeamKey)
        {
            string explicitAllegiance = BattleTeamUtility.NormalizeServerTeamKey(unit?.allegianceKey);
            if (!string.IsNullOrWhiteSpace(explicitAllegiance))
            {
                if (string.Equals(explicitAllegiance, "dungeon", StringComparison.OrdinalIgnoreCase))
                    return null;
                return explicitAllegiance;
            }

            if (!string.IsNullOrWhiteSpace(unit?.sourceTeam))
                return BattleTeamUtility.NormalizeServerTeamKey(unit.sourceTeam);

            if (unit != null && !unit.isWaveUnit)
            {
                string sourceLaneTeamKey = ResolveLaneTeamKey(unit.sourceLaneIndex);
                if (!string.IsNullOrWhiteSpace(sourceLaneTeamKey))
                    return sourceLaneTeamKey;

                int ownerLaneIndex = unit.ownerLaneIndex >= 0 ? unit.ownerLaneIndex : unit.ownerLane;
                string ownerLaneTeamKey = ResolveLaneTeamKey(ownerLaneIndex);
                if (!string.IsNullOrWhiteSpace(ownerLaneTeamKey))
                    return ownerLaneTeamKey;
                return defenderTeamKey;
            }

            return null;
        }

        static string ResolveLaneTeamKey(int laneIndex)
        {
            if (laneIndex < 0)
                return null;

            var sa = SnapshotApplier.Instance;
            string laneSlotColor = BattleTeamUtility.NormalizeServerTeamKey(sa?.GetLane(laneIndex)?.slotColor);
            if (!string.IsNullOrWhiteSpace(laneSlotColor))
                return laneSlotColor;

            string laneTeam = BattleTeamUtility.NormalizeServerTeamKey(sa?.GetLane(laneIndex)?.team);
            if (!string.IsNullOrWhiteSpace(laneTeam))
                return laneTeam;

            string assignmentSlotColor = BattleTeamUtility.NormalizeServerTeamKey(sa?.GetLaneAssignment(laneIndex)?.slotColor);
            if (!string.IsNullOrWhiteSpace(assignmentSlotColor))
                return assignmentSlotColor;

            return BattleTeamUtility.NormalizeServerTeamKey(sa?.GetLaneAssignment(laneIndex)?.team);
        }

        void LogRejectedUnit(MLLaneSnap lane, MLUnit unit, string reason)
        {
            string key = $"reject:{lane?.laneIndex ?? -1}:{unit?.id ?? "<null>"}:{reason}";
            if (!_loggedRejectionKeys.Add(key))
                return;

            Debug.LogWarning(
                $"[SpawnAudit][ClientReject] lane={lane?.laneIndex ?? -1} unitId='{unit?.id ?? "<null>"}' " +
                $"unitType='{unit?.type ?? "<null>"}' sourceBarracksKey='{ResolveSourceBarracksKey(unit)}' " +
                $"reason='{reason}'",
                this);
        }

        void LogPathMode(string mode, string methodName, MLUnit unit, MLLaneSnap lane, int spatialLane, string branch)
        {
            string unitId = unit?.id ?? "<null>";
            string key = $"{mode}:{methodName}:{unitId}:{spatialLane}:{branch}";
            if (!_loggedPathModeKeys.Add(key))
                return;

            Debug.Log(
                $"[SpawnAudit][PathMode] using {mode} method={methodName} unitId='{unitId}' " +
                $"unitType='{unit?.type ?? "<null>"}' lane={lane?.laneIndex ?? -1} spatialLane={spatialLane} " +
                $"branch='{branch ?? "<none>"}'",
                this);
        }

        void LogWorldPositionFailure(MLLaneSnap lane, MLUnit unit, int spatialLane, string reason)
        {
            Debug.LogError(
                $"[SpawnAudit][ClientPositionFail] lane={lane?.laneIndex ?? -1} spatialLane={spatialLane} " +
                $"unitId='{unit?.id ?? "<null>"}' unitType='{unit?.type ?? "<null>"}' team='{lane?.team ?? "<null>"}' " +
                $"sourceBarracksKey='{ResolveSourceBarracksKey(unit)}' " +
                $"routeType='{unit?.routeType ?? "<null>"}' currentSegment='{unit?.currentSegment ?? "<null>"}' " +
                $"segmentProgress={(unit != null ? unit.segmentProgress : float.NaN):0.###} " +
                $"pathIdx={(unit != null ? unit.pathIdx : float.NaN):0.###} " +
                $"grid=({(unit != null ? unit.gridX : float.NaN):0.###},{(unit != null ? unit.gridY : float.NaN):0.###}) " +
                $"routeWorld=({(unit != null ? unit.routeWorldX : float.NaN):0.###},{(unit != null ? unit.routeWorldY : float.NaN):0.###}) " +
                $"reason='{reason ?? "<unknown>"}'",
                this);
        }

        static float GetLevelScale(int level)
        {
            int clamped = Mathf.Clamp(level, 1, 10);
            return 1.55f + clamped * 0.10f;
        }

        static void ApplyHostileTint(Renderer[] renderers)
        {
            if (renderers == null)
                return;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var bridge = renderer.GetComponent<ToonShaderBridge>();
                if (bridge != null)
                {
                    bridge.SetBaseColor(HostileBaseColor);
                    bridge.SetRimColor(HostileRimColor);
                    continue;
                }

                var materials = renderer.materials;
                for (int mi = 0; mi < materials.Length; mi++)
                {
                    var material = materials[mi];
                    if (material == null)
                        continue;

                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", HostileBaseColor);
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", HostileBaseColor);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", HostileRimColor * 0.25f);
                    }
                }
            }
        }

        static void ApplySnapshotUnitTint(GameObject root, MLUnit unit, Renderer[] renderers, string ownerTeamKey)
        {
            if (renderers == null)
                return;

            if (!string.IsNullOrWhiteSpace(ownerTeamKey)
                && BattleTeamUtility.TryParseServerTeamKey(ownerTeamKey, out BattleTeam team))
            {
                if (ApplyTeamMaterialProfiles(root, team))
                    return;

                ApplyTeamTint(renderers, team);
                return;
            }

            ApplyHostileTint(renderers);
        }

        static bool ApplyTeamMaterialProfiles(GameObject root, BattleTeam team)
        {
            if (root == null)
                return false;

            bool appliedAny = false;
            var profiles = root.GetComponentsInChildren<TeamColorMaterialProfile>(true);
            for (int i = 0; i < profiles.Length; i++)
            {
                var profile = profiles[i];
                if (profile == null)
                    continue;

                appliedAny |= profile.Apply(team);
            }

            return appliedAny;
        }

        static void ApplyTeamTint(Renderer[] renderers, BattleTeam team)
        {
            Color baseColor = BattleTeamUtility.ToColor(team);
            Color rimColor = Color.Lerp(baseColor, Color.white, 0.25f);

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var bridge = renderer.GetComponent<ToonShaderBridge>();
                if (bridge != null)
                {
                    bridge.SetBaseColor(baseColor);
                    bridge.SetRimColor(rimColor);
                    continue;
                }

                var materials = renderer.materials;
                for (int mi = 0; mi < materials.Length; mi++)
                {
                    var material = materials[mi];
                    if (material == null)
                        continue;

                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", baseColor);
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", baseColor);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", rimColor * 0.20f);
                    }
                }
            }
        }

        static void UpgradeLegacyRendererMaterials(Renderer renderer)
        {
            if (renderer == null)
                return;

            var urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null)
                return;

            var shared = renderer.sharedMaterials;
            var instanced = renderer.materials;
            bool changed = false;

            for (int i = 0; i < instanced.Length; i++)
            {
                var mat = instanced[i];
                if (mat == null || mat.shader == null || mat.shader.name.StartsWith("Universal Render Pipeline"))
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

        static void FaceDirection(Transform target, Vector3 delta)
        {
            if (target == null)
                return;

            delta.y = 0f;
            if (delta.sqrMagnitude <= 0.0001f)
                return;

            target.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        static void RotateTowardDirection(Transform target, Vector3 delta, float turnSharpness, float dt)
        {
            if (target == null)
                return;

            delta.y = 0f;
            if (delta.sqrMagnitude <= 0.0001f)
                return;

            Quaternion desired = Quaternion.LookRotation(delta.normalized, Vector3.up);
            float turnT = 1f - Mathf.Exp(-Mathf.Max(0.01f, turnSharpness) * dt);
            target.rotation = Quaternion.Slerp(target.rotation, desired, turnT);
        }

        static void SetVisualState(WaveView view, VisualState desired)
        {
            if (view == null || view.animators == null || view.visualState == desired)
                return;

            view.visualState = desired;
            switch (desired)
            {
                case VisualState.Moving:
                    CrossFadeFirstAvailable(view.animators, MoveStates, 0.08f);
                    break;
                case VisualState.Attacking:
                    CrossFadeFirstAvailable(view.animators, view.attackStates, 0.04f);
                    break;
                default:
                    CrossFadeFirstAvailable(view.animators, IdleStates, 0.08f);
                    break;
            }
        }

        static void PlayAttackAnimation(WaveView view)
        {
            if (view == null)
                return;

            view.lastVisualAttackUntil = Time.time + AttackVisualHoldSeconds;
            CrossFadeFirstAvailable(view.animators, view.attackStates, 0.04f);
            view.visualState = VisualState.Attacking;
        }

        static string[] ResolveAttackStateNames(MLUnit unit)
        {
            if (unit == null)
                return DefaultAttackStates;

            if (FortUnitIdentityCatalog.TryResolveBarracksDefinition(
                null,
                unit.archetypeKey,
                string.IsNullOrWhiteSpace(unit.catalogUnitKey) ? unit.type : unit.catalogUnitKey,
                unit.skinKey,
                out FortBarracksRosterDefinition definition))
            {
                return definition.barracksRole switch
                {
                    BarracksUnitRole.Ranged => LooksLikeMagicUnit(unit) ? MagicAttackStates : RangedAttackStates,
                    BarracksUnitRole.Support => MagicAttackStates,
                    BarracksUnitRole.Siege => MagicAttackStates,
                    _ => MeleeAttackStates,
                };
            }

            if (LooksLikeMagicUnit(unit))
                return MagicAttackStates;
            if (LooksLikeRangedUnit(unit))
                return RangedAttackStates;

            return DefaultAttackStates;
        }

        static bool LooksLikeMagicUnit(MLUnit unit)
        {
            return ContainsAttackHint(unit, "mage")
                || ContainsAttackHint(unit, "wizard")
                || ContainsAttackHint(unit, "cleric")
                || ContainsAttackHint(unit, "priest")
                || ContainsAttackHint(unit, "thaum")
                || ContainsAttackHint(unit, "arcane")
                || ContainsAttackHint(unit, "bishop");
        }

        static bool LooksLikeRangedUnit(MLUnit unit)
        {
            return ContainsAttackHint(unit, "archer")
                || ContainsAttackHint(unit, "crossbow")
                || ContainsAttackHint(unit, "ranger")
                || ContainsAttackHint(unit, "bow")
                || ContainsAttackHint(unit, "scout");
        }

        static bool ContainsAttackHint(MLUnit unit, string hint)
        {
            if (unit == null || string.IsNullOrWhiteSpace(hint))
                return false;

            return ContainsIgnoreCase(unit.archetypeKey, hint)
                || ContainsIgnoreCase(unit.catalogUnitKey, hint)
                || ContainsIgnoreCase(unit.skinKey, hint)
                || ContainsIgnoreCase(unit.type, hint)
                || ContainsIgnoreCase(unit.heroKey, hint);
        }

        static bool ContainsIgnoreCase(string value, string hint)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool CrossFadeFirstAvailable(Animator[] animators, string[] stateNames, float transitionDuration)
        {
            if (animators == null || stateNames == null || stateNames.Length == 0)
                return false;

            bool playedAny = false;
            for (int ai = 0; ai < animators.Length; ai++)
            {
                var animator = animators[ai];
                if (animator == null)
                    continue;

                for (int si = 0; si < stateNames.Length; si++)
                {
                    string stateName = stateNames[si];
                    if (string.IsNullOrWhiteSpace(stateName))
                        continue;

                    int stateHash = Animator.StringToHash(stateName);
                    if (!animator.HasState(0, stateHash))
                        continue;

                    animator.CrossFade(stateHash, transitionDuration, 0, 0f);
                    playedAny = true;
                    break;
                }
            }

            return playedAny;
        }
    }
}
