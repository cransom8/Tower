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
        const float SnapshotCatchupMinStep = 0.12f;
        const float SnapshotCatchupSpeedScale = 2.35f;
        const float VisualMotionSqrThreshold = 0.0004f;
        const float StationarySnapshotSnapDistance = 0.32f;
        const float AttackVisualHoldSeconds = 0.32f;
        const float HitReactVisualHoldSeconds = 0.18f;
        const float AttackVisualMinHoldSeconds = 0.22f;
        const float AttackVisualMaxHoldSeconds = 0.58f;
        const float HitReactVisualMinHoldSeconds = 0.10f;
        const float HitReactVisualMaxHoldSeconds = 0.30f;
        const float CombatSfxCooldownSeconds = 0.06f;
        const float ImpactFxPerTargetCooldownSeconds = 0.08f;
        const float GroundProbeHeight = 32f;
        const float GroundProbeDistance = 96f;
        const float GroundClearance = 0.04f;
        const float GroundNormalThreshold = 0.35f;
        const float UnitSelectionColliderPadding = 0.2f;
        const float UnitFootprintColliderRadiusScale = 0.4f;
        const float UnitFootprintColliderHeight = 1.2f;
        const float FrontGateForwardOffset = 10.2f;
        const float CombatProjectionForwardSlack = 4.5f;
        const float CombatProjectionRearSlack = 8f;
        const float CombatProjectionLateralLimit = 10f;
        const float RouteProjectionAnchorBlendDistance = 1.75f;
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
        const string UnitSelectionLayerName = "UnitSelection";
        const string UnitFootprintLayerName = "UnitFootprint";
        const string SnapshotUnitNamePrefix = "SnapshotUnit_";
        const float CombatFacingTurnSharpness = 20f;
        static readonly Color HostileBaseColor = new(0.90f, 0.30f, 0.10f);
        static readonly Color HostileRimColor = new(1.00f, 0.55f, 0.00f);
        static readonly bool EnableVerboseSpawnAuditLogs = false;
        static readonly RaycastHit[] GroundHitBuffer = new RaycastHit[24];
        static readonly HashSet<string> AllowedSnapshotPresenterBehaviours = new(StringComparer.Ordinal)
        {
            typeof(Animator).FullName,
            typeof(LaneSnapshotCombatant).FullName,
            typeof(SnapshotAnimationEventRelay).FullName,
            typeof(UnitAnimationBinding).FullName,
            "CastleDefender.Game.TeamColorMaterialProfile",
            "CastleDefender.Game.UnitTeamAccentMarkers",
            "CastleDefender.Game.TTRTSUnitProportionScaler",
        };
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

        class WaveView
        {
            public string id;
            public GameObject go;
            public LaneSnapshotCombatant combatant;
            public Animator[] animators;
            public Renderer[] renderers;
            public UnitAnimationResolver.ResolvedProfile animationProfile;
            public MLUnit latestSnapshotUnit;
            public Vector3 snapshotWorldPos;
            public Vector3 snapshotVelocity;
            public Vector3 desiredFacing = Vector3.forward;
            public float timeSinceSnap;
            public float lastAttackAt = float.MinValue;
            public float lastVisualAttackUntil;
            public float lastHitReactUntil;
            public int lastServerAttackPulse;
            public bool hadSnapshot;
            public bool lockCombatFacing;
            public bool hasDesiredFacing;
            public UnitAnimationStateIntent visualState = UnitAnimationStateIntent.Idle;
            public Vector3 lastFramePosition;
        }

        [Header("Runtime Snapshot Units")]
        public UnitPrefabRegistry Registry;
        public GameObject HpBarPrefab;

        [SerializeField] float spawnSnapDistance = 4f;
        [SerializeField] bool returnToLobbyOnContentFailure = true;
        [SerializeField] float contentFailureLobbyReturnDelaySeconds = 1.5f;

        readonly Dictionary<string, WaveView> _views = new();
        readonly HashSet<string> _seenIds = new();
        readonly List<string> _toRemove = new();
        readonly int[] _branchMap = new int[4] { 0, 1, 2, 3 };
        readonly HashSet<string> _loggedFailureKeys = new();
        readonly HashSet<string> _loggedMaterializationSummaryKeys = new();
        readonly HashSet<string> _loggedPathModeKeys = new();
        readonly HashSet<string> _loggedRejectionKeys = new();
        readonly HashSet<string> _loggedBattlefieldRouteErrors = new();
        readonly HashSet<string> _loggedUnitQueryLayerErrors = new();
        readonly Dictionary<string, Vector3> _battlefieldTownCoreByLaneKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3> _battlefieldFrontGateByLaneKey = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3> _battlefieldBarracksByLaneAndId = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3> _battlefieldFortressCombatWorldByLaneAndPadId = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector3[]> _battlefieldRouteWorldPolylineBySegment = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Vector2[]> _battlefieldRouteSpacePolylineBySegment = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _battlefieldRouteNodeLaneByNode = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, float> _lastImpactFxAtByTarget = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<AudioManager.SFX, float> _lastCombatSfxAt = new();
        string _lastSnapshotSummaryLog;
        float _lastSnapTime = -1f;
        bool _subscribed;
        bool _contentFailureTriggered;
        SnapshotApplier _boundSnapshotApplier;

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

        void Awake() => SyncDependenciesFromScene();

        void OnEnable()
        {
            SyncDependenciesFromScene();
            DestroyOrphanedRuntimePresenters();
            TrySubscribeSnapshots();
        }

        void OnDisable()
        {
            if (_boundSnapshotApplier != null)
                _boundSnapshotApplier.OnMLSnapshotApplied -= OnSnapshot;
            _boundSnapshotApplier = null;
            _subscribed = false;
            DestroyAllRuntimePresenters();
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
                    if (ShouldSnapToAuthoritativeSnapshotPosition(view.latestSnapshotUnit))
                    {
                        view.snapshotVelocity = Vector3.zero;
                        view.go.transform.position = desired;
                    }
                    else if (ShouldSnapMinorStationaryCorrection(view.latestSnapshotUnit, view.go.transform.position, desired))
                    {
                        view.snapshotVelocity = Vector3.zero;
                        view.go.transform.position = desired;
                    }
                    else
                    {
                        float catchupStep = Mathf.Max(
                            SnapshotCatchupMinStep,
                            view.snapshotVelocity.magnitude * dt * SnapshotCatchupSpeedScale);
                        Vector3 advanced = Vector3.MoveTowards(view.go.transform.position, desired, catchupStep);
                        float lerpT = 1f - Mathf.Exp(-TargetFollowSharpness * dt);
                        view.go.transform.position = Vector3.Lerp(advanced, desired, lerpT * 0.2f);
                    }
                }

                Vector3 currentPosition = view.go.transform.position;
                Vector3 moveDelta = currentPosition - view.lastFramePosition;
                Vector3 flatDelta = moveDelta;
                flatDelta.y = 0f;
                bool moving = ShouldTreatSnapshotAsMoving(view.latestSnapshotUnit, flatDelta);
                if (view.hasDesiredFacing)
                {
                    float turnSharpness = view.lockCombatFacing
                        ? CombatFacingTurnSharpness
                        : TargetFollowSharpness;
                    RotateTowardDirection(view.go.transform, view.desiredFacing, turnSharpness, dt);
                }
                else if (moving)
                {
                    FaceDirection(view.go.transform, flatDelta);
                }

                bool attacking = now <= view.lastVisualAttackUntil
                    || (view.combatant != null && now - view.combatant.LastAttackAt <= AttackVisualHoldSeconds);
                bool hitReacting = !attacking
                    && now <= view.lastHitReactUntil
                    && view.latestSnapshotUnit != null
                    && view.latestSnapshotUnit.hp > 0f;

                UnitAnimationStateIntent desiredState = hitReacting
                    ? UnitAnimationStateIntent.HitReact
                    : UnitAnimationResolver.ResolveRuntimeIntent(view.latestSnapshotUnit, moving, attacking);
                SetVisualState(view, desiredState);
                view.lastFramePosition = currentPosition;
            }
        }

        void TrySubscribeSnapshots()
        {
            var sa = SnapshotApplier.Instance;
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

                int branchCfg = BattlefieldSpaceMapper.GetBranchConfigIndex(lane.branchId);
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

                    MLUnit previousSnapshotUnit = view.latestSnapshotUnit;
                    view.combatant.ApplySnapshot(defenderTeamKey, ownerTeamKey, unit.hp, unit.maxHp, unit.moveSpeed);
                    view.combatant.ApplyPresentationSnapshot(unit);
                    ApplySnapshotTeamAccents(view.go, unit, ownerTeamKey);

                    bool hadPreviousSnapshot = view.hadSnapshot;
                    if (hadPreviousSnapshot && ShouldFreezeMinorStationarySnapshotDrift(unit, view.snapshotWorldPos, worldPos))
                        worldPos = view.snapshotWorldPos;

                    if (hadPreviousSnapshot && ShouldSnapToAuthoritativeSnapshotPosition(unit))
                        view.snapshotVelocity = Vector3.zero;
                    else if (hadPreviousSnapshot)
                        view.snapshotVelocity = (worldPos - view.snapshotWorldPos) / snapDt;
                    else
                        view.snapshotVelocity = Vector3.zero;

                    view.snapshotWorldPos = worldPos;
                    view.timeSinceSnap = 0f;
                    view.hadSnapshot = true;
                    view.latestSnapshotUnit = unit;
                    view.animationProfile = UnitAnimationResolver.ResolveForUnit(view.go, unit);
                    UnitAnimationResolver.PrepareAnimators(view.animators, view.animationProfile, forPortrait: false);

                    if (!view.go.activeSelf && !view.combatant.IsLocallyDefeated)
                        view.go.SetActive(true);

                    if (createdNow || !hadPreviousSnapshot || ShouldSnapToAuthoritativeSnapshotPosition(unit))
                        view.go.transform.position = worldPos;

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

                    if (hadPreviousSnapshot && previousSnapshotUnit != null && unit.hp + 0.01f < previousSnapshotUnit.hp)
                        PlayHitReactFeedback(view, now);

                    if (unit.attackPulse > 0 && unit.attackPulse != view.lastServerAttackPulse)
                    {
                        view.lastAttackAt = now;
                        view.combatant?.NotifyAttack(now);
                        PlayAttackAnimation(view);
                        PlayAttackFeedback(snap, lane, unit, previousSnapshotUnit, now);
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
                LogVerboseSpawnAudit(snapshotSummary);
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
                string detail =
                    $"Missing UnitPrefabRegistry on '{name}'. " +
                    $"Snapshot unit '{unit?.id ?? "<null>"}' type='{unit?.type ?? "<null>"}' lane={lane?.laneIndex ?? -1} cannot spawn.";
                ReportWaveFailure(
                    $"registry_{lane?.laneIndex ?? -1}",
                    spawnPos,
                    $"[WaveSnapshotRuntimeSpawner] {detail}");
                HardFailMatchContent(detail);
                return null;
            }

            RuntimeFailureMarker.Clear(transform, $"wave_failure_registry_{lane?.laneIndex ?? -1}");

            GameObject prefab = Registry.GetPrefabForSkin(resolvedCatalogUnitKey, resolvedSkinKey);
            if (prefab == null)
            {
                string detail =
                    $"Missing prefab for snapshot unit id='{unit?.id ?? "<null>"}' type='{unit?.type ?? "<null>"}' " +
                    $"resolvedCatalogUnitKey='{resolvedCatalogUnitKey ?? "<null>"}' skin='{resolvedSkinKey ?? "<default>"}' " +
                    $"lane={lane?.laneIndex ?? -1} sourceBarracksKey='{ResolveSourceBarracksKey(unit)}'.";
                ReportWaveFailure(
                    $"prefab_{unit?.id ?? "unknown"}",
                    spawnPos,
                    $"[WaveSnapshotRuntimeSpawner] {detail}");
                HardFailMatchContent(detail);
                return null;
            }

            RuntimeFailureMarker.Clear(transform, $"wave_failure_prefab_{unit.id}");

            Quaternion rotation = ResolveSnapshotSpawnRotation(unit, lane, spatialLane);
            var go = Instantiate(prefab, spawnPos, rotation, transform);
            go.name = $"SnapshotUnit_{unit.id}_{resolvedCatalogUnitKey}_{resolvedSkinKey ?? "default"}";
            go.SetActive(false);
            SanitizeSnapshotPresenter(go, unit);
            string sourceBarracksKey = ResolveSourceBarracksKey(unit);
            if (!string.IsNullOrWhiteSpace(sourceBarracksKey))
            {
                LogVerboseSpawnAudit(
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
            ApplySnapshotTeamAccents(go, unit, ownerTeamKey);

            var animators = go.GetComponentsInChildren<Animator>(true);
            var animationProfile = UnitAnimationResolver.ResolveForUnit(go, unit);
            UnitAnimationResolver.PrepareAnimators(animators, animationProfile, forPortrait: false);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                _ = animator.GetComponent<SnapshotAnimationEventRelay>()
                    ?? animator.gameObject.AddComponent<SnapshotAnimationEventRelay>();

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
            InstallUnitQueryColliders(go, renderers);

            var view = new WaveView
            {
                id = unit.id,
                go = go,
                combatant = combatant,
                animators = animators,
                renderers = renderers,
                animationProfile = animationProfile,
                latestSnapshotUnit = unit,
                snapshotWorldPos = spawnPos,
                snapshotVelocity = Vector3.zero,
                timeSinceSnap = 0f,
                hadSnapshot = false,
                lastServerAttackPulse = unit.attackPulse,
                lastFramePosition = spawnPos,
            };

            _views[unit.id] = view;
            go.SetActive(true);
            SetVisualState(view, UnitAnimationResolver.ResolveRuntimeIntent(unit, moving: false, attacking: false));
            AudioManager.I?.Play(AudioManager.SFX.UnitSpawn, 0.25f);
            LogVerboseSpawnAudit(
                $"[SpawnAudit][ClientInstantiate] unitId='{unit.id}' unitType='{unit.type}' " +
                $"resolvedCatalogUnitKey='{resolvedCatalogUnitKey ?? "<null>"}' skin='{resolvedSkinKey ?? "<default>"}' " +
                $"archetypeKey='{identity.ArchetypeKey ?? "<null>"}' presentationKey='{identity.PresentationKey ?? "<null>"}' " +
                $"sourceTeam='{unit.sourceTeam ?? "<none>"}' ownerLane={unit.ownerLane} sourceLane={unit.sourceLaneIndex} " +
                $"isWaveUnit={unit.isWaveUnit.ToString().ToLowerInvariant()} stance='{unit.stance ?? "<null>"}' " +
                $"resolvedPrefab='{prefab.name}' animationProfile='{animationProfile.ProfileId}' animationSource='{animationProfile.DebugSource}' " +
                $"spawnedName='{go.name}' worldPos=({spawnPos.x:0.###},{spawnPos.y:0.###},{spawnPos.z:0.###}) " +
                $"activeSelf={go.activeSelf} scale={scale:0.###} ownerTeam='{ownerTeamKey ?? "<none>"}'");
            return view;
        }

        void SanitizeSnapshotPresenter(GameObject root, MLUnit unit)
        {
            if (root == null)
                return;

            int disabledBehaviourCount = 0;
            int disabledColliderCount = 0;
            int hardenedRigidbodyCount = 0;
            int disabledAudioSourceCount = 0;

            var colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                    continue;

                collider.enabled = false;
                disabledColliderCount++;
            }

            var rigidbodies = root.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                var rigidbody = rigidbodies[i];
                if (rigidbody == null)
                    continue;

                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.useGravity = false;
                rigidbody.detectCollisions = false;
                rigidbody.isKinematic = true;
                rigidbody.Sleep();
                hardenedRigidbodyCount++;
            }

            var audioSources = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
            {
                var audioSource = audioSources[i];
                if (audioSource == null)
                    continue;

                audioSource.enabled = false;
                disabledAudioSourceCount++;
            }

            var behaviours = root.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour is Animator animator)
                {
                    animator.applyRootMotion = false;
                    continue;
                }

                string typeName = behaviour.GetType().FullName;
                if (!string.IsNullOrWhiteSpace(typeName) && AllowedSnapshotPresenterBehaviours.Contains(typeName))
                    continue;

                if (!behaviour.enabled)
                    continue;

                behaviour.enabled = false;
                disabledBehaviourCount++;
            }

            if (disabledBehaviourCount > 0 || disabledColliderCount > 0 || hardenedRigidbodyCount > 0 || disabledAudioSourceCount > 0)
            {
                LogVerboseSpawnAudit(
                    $"[WaveSnapshotRuntimeSpawner] Sanitized snapshot presenter unit='{unit?.id ?? "<null>"}' " +
                    $"type='{unit?.type ?? "<null>"}' disabledBehaviours={disabledBehaviourCount} " +
                    $"disabledColliders={disabledColliderCount} hardenedRigidbodies={hardenedRigidbodyCount} " +
                    $"disabledAudioSources={disabledAudioSourceCount}.");
            }
        }

        void InstallUnitQueryColliders(GameObject root, Renderer[] renderers)
        {
            if (root == null || !TryGetRenderableBounds(renderers, root.transform.position, out Bounds bounds))
                return;

            int selectionLayer = ResolveUnitQueryLayer(UnitSelectionLayerName, root.layer);
            int footprintLayer = ResolveUnitQueryLayer(UnitFootprintLayerName, root.layer);

            ConfigureUnitQueryCollider(
                root.transform,
                "RuntimeSelectionQuery",
                selectionLayer,
                bounds,
                radiusScale: 0.5f,
                minHeight: 1.8f,
                padding: UnitSelectionColliderPadding,
                alignToFootprint: false);

            ConfigureUnitQueryCollider(
                root.transform,
                "RuntimeFootprintQuery",
                footprintLayer,
                bounds,
                radiusScale: UnitFootprintColliderRadiusScale,
                minHeight: UnitFootprintColliderHeight,
                padding: 0f,
                alignToFootprint: true);
        }

        int ResolveUnitQueryLayer(string layerName, int fallbackLayer)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
                return layer;

            if (_loggedUnitQueryLayerErrors.Add(layerName))
            {
                Debug.LogError(
                    $"[WaveSnapshotRuntimeSpawner] Required physics/query layer '{layerName}' is missing. " +
                    "Add it to TagManager so unit selection and footprint triggers do not collapse onto Default.",
                    this);
            }

            return fallbackLayer;
        }

        static bool TryGetRenderableBounds(Renderer[] renderers, Vector3 fallbackCenter, out Bounds bounds)
        {
            bounds = new Bounds(fallbackCenter, Vector3.zero);
            if (renderers == null || renderers.Length == 0)
                return false;

            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        static void ConfigureUnitQueryCollider(
            Transform root,
            string childName,
            int layer,
            Bounds bounds,
            float radiusScale,
            float minHeight,
            float padding,
            bool alignToFootprint)
        {
            if (root == null)
                return;

            Transform child = root.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(root, false);
                child.localPosition = Vector3.zero;
                child.localRotation = Quaternion.identity;
                child.localScale = Vector3.one;
            }

            child.gameObject.layer = layer;
            var collider = child.GetComponent<CapsuleCollider>();
            if (collider == null)
                collider = child.gameObject.AddComponent<CapsuleCollider>();

            collider.isTrigger = true;
            collider.direction = 1;

            Vector3 localCenter = root.InverseTransformPoint(bounds.center);
            float width = Mathf.Max(bounds.size.x, bounds.size.z);
            float radius = Mathf.Max(0.18f, (width * Mathf.Max(0.05f, radiusScale)) + padding);
            float height = Mathf.Max(minHeight, bounds.size.y + (padding * 2f), radius * 2f);

            if (alignToFootprint)
            {
                Vector3 localFoot = root.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
                localCenter = new Vector3(localCenter.x, localFoot.y + (height * 0.5f), localCenter.z);
            }

            collider.radius = radius;
            collider.height = height;
            collider.center = localCenter;
        }

        void DestroyWaveView(string id)
        {
            if (!_views.TryGetValue(id, out var view))
                return;

            DestroyImpactFxHistory(id);
            DestroyImpactFxHistory(view.latestSnapshotUnit != null ? view.latestSnapshotUnit.combatTargetId : null);
            if (view.go != null)
                DestroyRuntimePresenter(view.go);

            _views.Remove(id);
            LogVerboseSpawnAudit($"[SpawnAudit][ClientRemove] unitId='{id}' presenterRemoved=true");
        }

        void DestroyAllRuntimePresenters()
        {
            if (_views.Count > 0)
            {
                _toRemove.Clear();
                foreach (var pair in _views)
                    _toRemove.Add(pair.Key);

                for (int i = 0; i < _toRemove.Count; i++)
                    DestroyWaveView(_toRemove[i]);
            }

            _toRemove.Clear();
            DestroyOrphanedRuntimePresenters();
        }

        void DestroyOrphanedRuntimePresenters()
        {
            var combatants = GetComponentsInChildren<LaneSnapshotCombatant>(true);
            int orphanCount = 0;

            for (int i = 0; i < combatants.Length; i++)
            {
                var combatant = combatants[i];
                if (combatant == null)
                    continue;

                var presenter = combatant.gameObject;
                if (presenter == null || presenter == gameObject)
                    continue;
                if (!presenter.name.StartsWith(SnapshotUnitNamePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string combatantId = combatant.CombatantId;
                if (_views.TryGetValue(combatantId, out var trackedView) && trackedView?.go == presenter)
                    continue;

                orphanCount++;
                DestroyRuntimePresenter(presenter);
            }

            if (orphanCount > 0)
            {
                Debug.LogWarning(
                    $"[WaveSnapshotRuntimeSpawner] Destroyed {orphanCount} orphaned runtime presenter(s) before rebuilding from authoritative snapshots.",
                    this);
            }
        }

        static void DestroyRuntimePresenter(GameObject presenter)
        {
            if (presenter == null)
                return;

            if (presenter.activeSelf)
                presenter.SetActive(false);

            Destroy(presenter);
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
            RefreshBattlefieldRouteNodeMap(snap);
            RefreshBattlefieldRouteSegments();
        }

        void RefreshBattlefieldRouteAnchors()
        {
            _battlefieldTownCoreByLaneKey.Clear();
            _battlefieldFrontGateByLaneKey.Clear();
            _battlefieldBarracksByLaneAndId.Clear();
            _battlefieldFortressCombatWorldByLaneAndPadId.Clear();

            var layout = SnapshotApplier.Instance?.GetBattlefieldLayout();
            if (layout == null)
            {
                LogBattlefieldRouteFailureOnce(
                    "layout:missing",
                    "Missing ML battlefieldLayout. Unity route projection requires server-authored layout data.");
                return;
            }

            if (layout.lanes == null || layout.lanes.Length == 0)
            {
                LogBattlefieldRouteFailureOnce(
                    "layout:lanes_missing",
                    $"Battlefield layout '{layout.layoutId ?? "<null>"}' has no lanes.");
                return;
            }

            for (int i = 0; i < layout.lanes.Length; i++)
            {
                var lane = layout.lanes[i];
                if (lane == null)
                    continue;

                string laneKey = NormalizeBattlefieldLaneKey(!string.IsNullOrWhiteSpace(lane.laneKey) ? lane.laneKey : lane.slotColor);
                if (string.IsNullOrWhiteSpace(laneKey))
                {
                    LogBattlefieldRouteFailureOnce(
                        $"layout:lane_key:{lane.laneIndex}",
                        $"Battlefield layout lane '{lane.laneIndex}' is missing a normalized lane key.");
                    continue;
                }

                if (TryResolveLayoutWorldPoint(lane.townCore, out Vector3 townCoreWorld))
                {
                    _battlefieldTownCoreByLaneKey[laneKey] = townCoreWorld;
                }
                else
                {
                    LogBattlefieldRouteFailureOnce(
                        $"layout:town_core:{laneKey}",
                        $"Battlefield layout lane '{laneKey}' is missing a valid townCore point.");
                }

                if (TryResolveLayoutWorldPoint(lane.frontGate, out Vector3 frontGateWorld))
                {
                    _battlefieldFrontGateByLaneKey[laneKey] = frontGateWorld;
                }
                else
                {
                    LogBattlefieldRouteFailureOnce(
                        $"layout:front_gate:{laneKey}",
                        $"Battlefield layout lane '{laneKey}' is missing a valid frontGate point.");
                }

                if (lane.fortressPads != null)
                {
                    for (int padIndex = 0; padIndex < lane.fortressPads.Length; padIndex++)
                    {
                        var pad = lane.fortressPads[padIndex];
                        if (pad == null || string.IsNullOrWhiteSpace(pad.padId))
                            continue;

                        if (!TryResolveLayoutWorldPoint(pad.combatWorld, out Vector3 combatWorld))
                        {
                            LogBattlefieldRouteFailureOnce(
                                $"layout:fortress_pad:{laneKey}:{pad.padId}",
                                $"Battlefield layout lane '{laneKey}' pad '{pad.padId}' is missing a valid combatWorld point.");
                            continue;
                        }

                        _battlefieldFortressCombatWorldByLaneAndPadId[
                            BuildBattlefieldFortressPadKey(laneKey, pad.padId)] = combatWorld;
                    }
                }

                if (lane.barracksSites == null)
                    continue;

                for (int siteIndex = 0; siteIndex < lane.barracksSites.Length; siteIndex++)
                {
                    var site = lane.barracksSites[siteIndex];
                    if (site == null || string.IsNullOrWhiteSpace(site.barracksId))
                        continue;

                    if (!TryResolveLayoutWorldPoint(site.world, out Vector3 barracksWorld))
                    {
                        LogBattlefieldRouteFailureOnce(
                            $"layout:barracks:{laneKey}:{site.barracksId}",
                            $"Battlefield layout lane '{laneKey}' barracks '{site.barracksId}' is missing a valid world point.");
                        continue;
                    }

                    _battlefieldBarracksByLaneAndId[
                        BuildBattlefieldBarracksKey(laneKey, site.barracksId)] = barracksWorld;
                }
            }
        }

        void RefreshBattlefieldRouteSegments()
        {
            _battlefieldRouteWorldPolylineBySegment.Clear();
            _battlefieldRouteSpacePolylineBySegment.Clear();

            var layout = SnapshotApplier.Instance?.GetBattlefieldLayout();
            if (layout == null)
                return;

            if (layout.routeSegments == null || layout.routeSegments.Length == 0)
            {
                LogBattlefieldRouteFailureOnce(
                    "layout:route_segments_missing",
                    $"Battlefield layout '{layout.layoutId ?? "<null>"}' has no routeSegments.");
                return;
            }

            for (int i = 0; i < layout.routeSegments.Length; i++)
            {
                var segment = layout.routeSegments[i];
                if (segment == null)
                    continue;

                if (!TryNormalizeBattlefieldSegmentId(segment.segmentId, out string normalizedSegmentId))
                {
                    LogBattlefieldRouteFailureOnce(
                        $"layout:route_segment_invalid:{segment.segmentId ?? "<null>"}",
                        $"Battlefield layout contains unsupported route segment '{segment.segmentId ?? "<null>"}'.");
                    continue;
                }

                if (segment.points == null || segment.points.Length < 2)
                {
                    LogBattlefieldRouteFailureOnce(
                        $"layout:route_segment_world_points_missing:{normalizedSegmentId}",
                        $"Battlefield layout route segment '{normalizedSegmentId}' is missing authored world polyline points.");
                    continue;
                }

                if (segment.routeSpacePoints == null || segment.routeSpacePoints.Length < 2)
                {
                    LogBattlefieldRouteFailureOnce(
                        $"layout:route_segment_route_space_points_missing:{normalizedSegmentId}",
                        $"Battlefield layout route segment '{normalizedSegmentId}' is missing authoritative routeSpacePoints.");
                    continue;
                }

                var worldPolyline = new Vector3[segment.points.Length];
                var routeSpacePolyline = new Vector2[segment.routeSpacePoints.Length];
                bool valid = true;

                for (int pointIndex = 0; pointIndex < segment.points.Length; pointIndex++)
                {
                    if (!TryResolveLayoutWorldPoint(segment.points[pointIndex], out Vector3 worldPoint))
                    {
                        LogBattlefieldRouteFailureOnce(
                            $"layout:route_segment_world_point:{normalizedSegmentId}:{pointIndex}",
                            $"Battlefield layout route segment '{normalizedSegmentId}' has an invalid world point at index {pointIndex}.");
                        valid = false;
                        break;
                    }

                    if (!TryResolveRouteSpacePoint(segment.routeSpacePoints[pointIndex], out Vector2 routeSpacePoint))
                    {
                        LogBattlefieldRouteFailureOnce(
                            $"layout:route_segment_route_space_point:{normalizedSegmentId}:{pointIndex}",
                            $"Battlefield layout route segment '{normalizedSegmentId}' has an invalid routeSpacePoint at index {pointIndex}.");
                        valid = false;
                        break;
                    }

                    worldPolyline[pointIndex] = worldPoint;
                    routeSpacePolyline[pointIndex] = routeSpacePoint;
                }

                if (!valid)
                    continue;

                _battlefieldRouteWorldPolylineBySegment[normalizedSegmentId] = worldPolyline;
                _battlefieldRouteSpacePolylineBySegment[normalizedSegmentId] = routeSpacePolyline;
            }
        }

        static bool HasAuthoritativeRouteSample(MLUnit unit)
        {
            return unit != null
                && !string.IsNullOrWhiteSpace(unit.currentSegment)
                && float.IsFinite(unit.segmentProgress)
                && float.IsFinite(unit.routeWorldX)
                && float.IsFinite(unit.routeWorldY);
        }

        static bool HasAuthoritativeAnchorSample(MLUnit unit)
        {
            return unit != null
                && float.IsFinite(unit.anchorTargetX)
                && float.IsFinite(unit.anchorTargetY);
        }

        static bool RequiresAuthoritativeCombatSpaceProjection(MLUnit unit)
        {
            if (unit == null)
                return false;

            if (ShouldUseStanceAnchorProjection(unit))
                return true;

            bool hasAuthoritativeCombatAnchor = unit.combatContact && HasAuthoritativeAnchorSample(unit);
            if (hasAuthoritativeCombatAnchor)
                return true;

            return unit.combatContact && IsCombatSnapshotUnit(unit);
        }

        static string DescribeAuthoritativeCombatSpaceFailure(MLUnit unit, MLLaneSnap lane, string routeFailure)
        {
            return
                $"Authoritative combat-space projection is required for unit '{unit?.id ?? "<null>"}' " +
                $"lane={lane?.laneIndex ?? -1} movementMode='{unit?.movementMode ?? "<null>"}' " +
                $"presentationPhase='{unit?.presentationPhase ?? "<null>"}' " +
                $"currentSegment='{unit?.currentSegment ?? "<null>"}' " +
                $"routeWorld=({(unit != null ? unit.routeWorldX : float.NaN):0.###},{(unit != null ? unit.routeWorldY : float.NaN):0.###}) " +
                $"anchorTarget=({(unit != null ? unit.anchorTargetX : float.NaN):0.###},{(unit != null ? unit.anchorTargetY : float.NaN):0.###}) " +
                $"failure='{routeFailure ?? "<unknown>"}'.";
        }

        static bool HasPresentationPhase(MLUnit unit, string presentationPhase)
        {
            return unit != null
                && !string.IsNullOrWhiteSpace(presentationPhase)
                && string.Equals(unit.presentationPhase, presentationPhase, StringComparison.OrdinalIgnoreCase);
        }

        static bool HasPresentationIntent(MLUnit unit, string presentationIntent)
        {
            return unit != null
                && !string.IsNullOrWhiteSpace(presentationIntent)
                && string.Equals(unit.presentationIntent, presentationIntent, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsMovementMode(MLUnit unit, string movementMode)
        {
            return unit != null
                && string.Equals(unit.movementMode, movementMode, StringComparison.OrdinalIgnoreCase);
        }

        static bool HasAuthoritativeMovingPresentation(MLUnit unit)
        {
            return HasPresentationIntent(unit, "Move")
                || HasPresentationIntent(unit, "Retreat")
                || HasPresentationPhase(unit, "CombatCommit");
        }

        static bool IsAuthoritativeStationaryPresentation(MLUnit unit)
        {
            if (unit == null || unit.hp <= 0f)
                return false;

            if (HasPresentationPhase(unit, "AnchorHold"))
                return true;

            if (HasAuthoritativeMovingPresentation(unit))
                return false;

            return HasPresentationIntent(unit, "Idle")
                || HasPresentationIntent(unit, "Defend");
        }

        static bool ShouldSnapMinorStationaryCorrection(MLUnit unit, Vector3 currentPosition, Vector3 desiredPosition)
        {
            if (!IsAuthoritativeStationaryPresentation(unit))
                return false;

            Vector3 delta = desiredPosition - currentPosition;
            delta.y = 0f;
            return delta.sqrMagnitude <= StationarySnapshotSnapDistance * StationarySnapshotSnapDistance;
        }

        static bool ShouldFreezeMinorStationarySnapshotDrift(MLUnit unit, Vector3 currentSnapshotWorldPos, Vector3 nextSnapshotWorldPos)
        {
            if (!IsAuthoritativeStationaryPresentation(unit))
                return false;

            Vector3 delta = nextSnapshotWorldPos - currentSnapshotWorldPos;
            delta.y = 0f;
            return delta.sqrMagnitude <= StationarySnapshotSnapDistance * StationarySnapshotSnapDistance;
        }

        static bool ShouldSnapToAuthoritativeSnapshotPosition(MLUnit unit)
        {
            if (unit == null)
                return false;

            return RequiresAuthoritativeCombatSpaceProjection(unit)
                || HasPresentationPhase(unit, "CombatRegroup")
                || HasPresentationPhase(unit, "ReturnToSlot");
        }

        static bool ShouldTreatSnapshotAsMoving(MLUnit unit, Vector3 flatDelta)
        {
            if (IsAuthoritativeStationaryPresentation(unit))
                return false;
            if (HasAuthoritativeMovingPresentation(unit))
                return true;

            return flatDelta.sqrMagnitude > VisualMotionSqrThreshold;
        }

        static bool TryGetAuthoritativeAnchorDistance(MLUnit unit, out float distance)
        {
            distance = float.PositiveInfinity;
            if (unit == null)
                return false;
            if (!float.IsFinite(unit.anchorTargetX) || !float.IsFinite(unit.anchorTargetY))
                return false;
            if (!float.IsFinite(unit.routeWorldX) || !float.IsFinite(unit.routeWorldY))
                return false;

            distance = Vector2.Distance(
                new Vector2(unit.routeWorldX, unit.routeWorldY),
                new Vector2(unit.anchorTargetX, unit.anchorTargetY));
            return float.IsFinite(distance);
        }

        static bool ShouldPreferBattlefieldRouteProjection(MLUnit unit)
        {
            if (!HasAuthoritativeRouteSample(unit))
                return false;
            if (IsMovementMode(unit, "CombatEngage"))
                return false;
            if (IsMovementMode(unit, "LaneTravel") || IsMovementMode(unit, "ReturnToAnchor"))
                return true;

            if (IsMovementMode(unit, "AnchorJoin")
                && TryGetAuthoritativeAnchorDistance(unit, out float anchorDistance))
            {
                return anchorDistance > RouteProjectionAnchorBlendDistance;
            }

            return false;
        }

        static int ResolveBattlefieldRouteSampleLaneIndex(int fromLaneIndex, int toLaneIndex, float segmentProgress, int fallbackLaneIndex)
        {
            if (fromLaneIndex >= 0 && toLaneIndex >= 0)
            {
                if (fromLaneIndex == toLaneIndex)
                    return fromLaneIndex;

                return Mathf.Clamp01(segmentProgress) < 0.5f
                    ? fromLaneIndex
                    : toLaneIndex;
            }

            if (fromLaneIndex >= 0)
                return fromLaneIndex;
            if (toLaneIndex >= 0)
                return toLaneIndex;

            return fallbackLaneIndex;
        }

        bool TryResolveBattlefieldRouteLaneKey(string routeNodeId, int laneIndex, out string laneKey)
        {
            laneKey = null;

            if (!string.IsNullOrWhiteSpace(routeNodeId)
                && _battlefieldRouteNodeLaneByNode.TryGetValue(routeNodeId, out string mappedLaneKey)
                && !string.IsNullOrWhiteSpace(mappedLaneKey))
            {
                laneKey = NormalizeBattlefieldLaneKey(mappedLaneKey);
                if (!string.IsNullOrWhiteSpace(laneKey))
                    return true;
            }

            laneKey = NormalizeBattlefieldLaneKey(ResolveFixedLaneKeyForLaneIndex(laneIndex));
            return !string.IsNullOrWhiteSpace(laneKey);
        }

        bool TryResolveBattlefieldRouteWorldFallbackPosition(
            MLUnit unit,
            MLLaneSnap lane,
            int spatialLane,
            out Vector3 worldPos,
            out Vector3 routeForward,
            out string resolvedRouteSource)
        {
            worldPos = default;
            routeForward = BattlefieldSpaceMapper.GetLaneForwardDir(lane?.laneIndex ?? 0);
            resolvedRouteSource = null;

            if (unit == null || !float.IsFinite(unit.routeWorldX) || !float.IsFinite(unit.routeWorldY))
                return false;

            int targetLaneIndex = spatialLane;
            string laneKey = NormalizeBattlefieldLaneKey(lane?.slotColor);

            if (TryParseBattlefieldSegmentId(unit.currentSegment, out string fromNode, out string toNode))
            {
                int fromLaneIndex = ResolveLaneIndexForRouteNode(fromNode);
                int toLaneIndex = ResolveLaneIndexForRouteNode(toNode);
                targetLaneIndex = ResolveBattlefieldRouteSampleLaneIndex(
                    fromLaneIndex,
                    toLaneIndex,
                    unit.segmentProgress,
                    spatialLane);

                string preferredNode = targetLaneIndex == fromLaneIndex
                    ? fromNode
                    : (targetLaneIndex == toLaneIndex ? toNode : null);
                if (TryResolveBattlefieldRouteLaneKey(preferredNode, targetLaneIndex, out string resolvedLaneKey))
                    laneKey = resolvedLaneKey;
            }
            else if (TryNormalizeRouteNodeId(unit.routeTargetNode, out string routeTargetNode))
            {
                int routeLaneIndex = ResolveLaneIndexForRouteNode(routeTargetNode);
                if (routeLaneIndex >= 0)
                {
                    targetLaneIndex = routeLaneIndex;
                    if (TryResolveBattlefieldRouteLaneKey(routeTargetNode, targetLaneIndex, out string resolvedLaneKey))
                        laneKey = resolvedLaneKey;
                }
            }

            if (targetLaneIndex < 0 || targetLaneIndex >= LaneCombatCoreSimPositions.Length)
                targetLaneIndex = spatialLane;

            laneKey = NormalizeBattlefieldLaneKey(laneKey);
            if (string.IsNullOrWhiteSpace(laneKey)
                && !TryResolveBattlefieldRouteLaneKey(null, targetLaneIndex, out laneKey))
            {
                return false;
            }

            if (!TryResolveLaneCombatSimWorldPosition(
                unit.routeWorldX,
                unit.routeWorldY,
                targetLaneIndex,
                laneKey,
                out worldPos,
                out routeForward,
                out resolvedRouteSource))
            {
                return false;
            }

            resolvedRouteSource = $"route_world_sim:{resolvedRouteSource}";
            return true;
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

            bool authoritativeCombatPhase =
                string.Equals(unit.presentationPhase, "CombatResolve", StringComparison.OrdinalIgnoreCase);
            bool isCombatState =
                string.Equals(unit.movementState, "COMBAT", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unit.state, "COMBAT", StringComparison.OrdinalIgnoreCase);
            bool hasResolvedCombatEvidence =
                unit.combatContact
                || HasAuthoritativeAnchorSample(unit)
                || unit.combatLockTicksRemaining > 0
                || unit.regroupTicksRemaining > 0;

            if (!hasResolvedCombatEvidence)
                return false;

            return authoritativeCombatPhase || isCombatState;
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
            if (!unit.combatContact)
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

            string laneKey = NormalizeBattlefieldLaneKey(lane?.slotColor);
            if (string.IsNullOrWhiteSpace(laneKey))
                return false;

            return _battlefieldFortressCombatWorldByLaneAndPadId.TryGetValue(
                BuildBattlefieldFortressPadKey(laneKey, targetId),
                out worldPos);
        }

        void RefreshBattlefieldRouteNodeMap(MLSnapshot snap)
        {
            _battlefieldRouteNodeLaneByNode.Clear();

            var layout = SnapshotApplier.Instance?.GetBattlefieldLayout();
            if (layout == null || layout.routeNodes == null)
                return;

            for (int i = 0; i < layout.routeNodes.Length; i++)
            {
                var node = layout.routeNodes[i];
                if (node == null)
                    continue;

                if (!TryNormalizeRouteNodeId(node.nodeId, out string normalizedNode))
                    continue;

                string laneKey = NormalizeBattlefieldLaneKey(node.laneKey);
                if (string.IsNullOrWhiteSpace(laneKey))
                    continue;

                _battlefieldRouteNodeLaneByNode[normalizedNode] = laneKey;
            }
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
                spatialLane,
                out worldPos,
                out Vector3 routeForward,
                out string resolvedRouteSource,
                out string routeFailure))
            {
                RuntimeFailureMarker.Clear(transform, $"wave_failure_{laneFailureKey}");
                worldPos = ProjectToVisibleGround(worldPos);
                failureReason = "resolved_via_battlefield_routes";
                LogVerboseSpawnAudit(
                    $"[SpawnAudit][ClientSnapshot] spawnType='{ResolveSpawnType(unit)}' unitId='{unit?.id ?? "<null>"}' unitType='{unit?.type ?? "<null>"}' " +
                    $"skinKey='{unit?.skinKey ?? "<default>"}' sourceTeam='{unit?.sourceTeam ?? "<none>"}' " +
                    $"ownerLane={unit?.ownerLane ?? -1} isWaveUnit={(unit != null && unit.isWaveUnit).ToString().ToLowerInvariant()} " +
                    $"stance='{unit?.stance ?? "<null>"}' " +
                    $"lane={lane?.laneIndex ?? -1} spatialLane={spatialLane} team='{lane?.team ?? "<null>"}' " +
                    $"sourceLane={unit?.sourceLaneIndex ?? -1} sourceBarracksKey='{ResolveSourceBarracksKey(unit)}' " +
                    $"routeType='{unit?.routeType ?? "<null>"}' currentSegment='{unit?.currentSegment ?? "<null>"}' " +
                    $"segmentProgress={(unit != null ? unit.segmentProgress : float.NaN):0.###} " +
                    $"routeWorld=({(unit != null ? unit.routeWorldX : float.NaN):0.###},{(unit != null ? unit.routeWorldY : float.NaN):0.###}) " +
                    $"resolvedLookupKey='{resolvedRouteSource ?? "<unknown>"}' worldPos=({worldPos.x:0.###},{worldPos.y:0.###},{worldPos.z:0.###})");
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

            Vector3 lateral = BattlefieldSpaceMapper.GetLaneLateralDir(spatialLane);
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

            return Quaternion.LookRotation(BattlefieldSpaceMapper.GetLaneForwardDir(spatialLane), Vector3.up);
        }

        bool TryResolveSnapshotFacing(MLLaneSnap lane, MLUnit unit, int spatialLane, out Vector3 facing)
        {
            facing = Vector3.zero;
            if (TryResolveBattlefieldRouteWorldPosition(
                unit,
                lane,
                spatialLane,
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
            int spatialLane,
            out Vector3 worldPos,
            out Vector3 routeForward,
            out string resolvedRouteSource,
            out string failureReason)
        {
            worldPos = default;
            routeForward = BattlefieldSpaceMapper.GetLaneForwardDir(lane?.laneIndex ?? 0);
            resolvedRouteSource = null;
            failureReason = null;

            if (unit == null)
            {
                failureReason = "unit is null";
                return false;
            }

            bool requiresAuthoritativeCombatSpace = RequiresAuthoritativeCombatSpaceProjection(unit);

            string routeFailure = null;
            if (TryResolveBattlefieldSegment(unit, out _, out _, out string segmentId, out string segmentFailure))
            {
                if (!TryResolveBattlefieldSegmentProgress(unit, out float segmentProgress))
                {
                    routeFailure = $"segmentProgress is invalid ({unit.segmentProgress:0.###})";
                }
                else if (!TryBuildBattlefieldRoutePolyline(
                    segmentId,
                    out Vector3[] worldPolyline,
                    out string segmentShape,
                    out string polylineFailure))
                {
                    routeFailure = polylineFailure;
                }
                else if (!TryResolveAuthoritativeRouteOffsets(
                    unit,
                    segmentId,
                    segmentProgress,
                    out float longitudinalOffset,
                    out float lateralOffset,
                    out string offsetFailure))
                {
                    routeFailure = offsetFailure;
                }
                else if (!TrySampleBattlefieldPolyline(
                    worldPolyline,
                    segmentProgress,
                    longitudinalOffset,
                    lateralOffset,
                    out worldPos,
                    out routeForward))
                {
                    routeFailure = $"failed to sample segment '{segmentId}'";
                }
                else
                {
                    resolvedRouteSource = $"battlefield_routes:{segmentShape}:{segmentId}";
                    return true;
                }
            }
            else
            {
                routeFailure = segmentFailure;
            }

            if (requiresAuthoritativeCombatSpace)
            {
                if (TryResolveBattlefieldRouteWorldFallbackPosition(unit, lane, spatialLane, out worldPos, out routeForward, out resolvedRouteSource))
                {
                    resolvedRouteSource = $"battlefield_routes:authoritative_fallback:{resolvedRouteSource}";
                    failureReason = routeFailure;
                    return true;
                }

                if (TryResolveStanceAnchorWorldPosition(unit, lane, spatialLane, out worldPos, out routeForward, out resolvedRouteSource))
                {
                    resolvedRouteSource = $"battlefield_routes:authoritative_fallback:{resolvedRouteSource}";
                    failureReason = routeFailure;
                    return true;
                }

                failureReason = DescribeAuthoritativeCombatSpaceFailure(unit, lane, routeFailure);
                return false;
            }

            if (HasAuthoritativeRouteSample(unit)
                && TryResolveBattlefieldRouteWorldFallbackPosition(unit, lane, spatialLane, out worldPos, out routeForward, out resolvedRouteSource))
            {
                resolvedRouteSource = $"battlefield_routes:fallback:{resolvedRouteSource}";
                failureReason = routeFailure;
                return true;
            }

            if (TryResolveStanceAnchorWorldPosition(unit, lane, spatialLane, out worldPos, out routeForward, out resolvedRouteSource))
            {
                resolvedRouteSource = $"battlefield_routes:fallback:{resolvedRouteSource}";
                failureReason = routeFailure;
                return true;
            }

            failureReason = routeFailure;
            return false;
        }

        bool TryBuildBattlefieldRoutePolyline(
            string segmentId,
            out Vector3[] points,
            out string segmentShape,
            out string failureReason)
        {
            points = null;
            segmentShape = null;
            failureReason = null;

            if (!TryNormalizeBattlefieldSegmentId(segmentId, out string normalizedSegmentId))
            {
                failureReason = $"Unsupported battlefield segment '{segmentId ?? "<null>"}'.";
                return false;
            }

            if (!_battlefieldRouteWorldPolylineBySegment.TryGetValue(normalizedSegmentId, out points)
                || points == null
                || points.Length < 2)
            {
                failureReason = $"Battlefield layout has no authored world polyline for segment '{normalizedSegmentId}'.";
                LogBattlefieldRouteFailureOnce($"layout:route_segment_world_polyline:{normalizedSegmentId}", failureReason);
                return false;
            }

            segmentShape = $"server_authored_{points.Length}pt";
            return true;
        }

        bool TryResolveStanceAnchorWorldPosition(
            MLUnit unit,
            MLLaneSnap lane,
            int spatialLane,
            out Vector3 worldPos,
            out Vector3 routeForward,
            out string resolvedRouteSource)
        {
            worldPos = default;
            routeForward = Vector3.forward;
            resolvedRouteSource = null;

            if (unit == null || lane == null)
                return false;
            if (!ShouldUseStanceAnchorProjection(unit))
                return false;

            string laneKey = NormalizeBattlefieldLaneKey(lane.slotColor);
            if (string.IsNullOrWhiteSpace(laneKey))
                return false;

            if (float.IsFinite(unit.routeWorldX) && float.IsFinite(unit.routeWorldY)
                && TryResolveLaneCombatSimWorldPosition(
                    unit.routeWorldX,
                    unit.routeWorldY,
                    spatialLane,
                    laneKey,
                    out worldPos,
                    out routeForward,
                    out resolvedRouteSource))
            {
                resolvedRouteSource = $"stance_anchor_route_world:{resolvedRouteSource}";
                return true;
            }

            if (float.IsFinite(unit.anchorTargetX) && float.IsFinite(unit.anchorTargetY)
                && TryResolveLaneCombatSimWorldPosition(
                    unit.anchorTargetX,
                    unit.anchorTargetY,
                    spatialLane,
                    laneKey,
                    out worldPos,
                    out routeForward,
                    out resolvedRouteSource))
            {
                resolvedRouteSource = $"stance_anchor_slot:{resolvedRouteSource}";
                return true;
            }

            return false;
        }

        static bool ShouldUseStanceAnchorProjection(MLUnit unit)
        {
            if (unit == null)
                return false;

            bool isBarracksUnit =
                string.Equals(unit.spawnSourceType, "barracks_roster", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unit.spawnSourceType, "barracks_hero", StringComparison.OrdinalIgnoreCase);
            if (!isBarracksUnit)
                return false;

            bool wantsAnchorProjection =
                string.Equals(unit.currentWaypointTargetKind, "outsideGateAnchor", StringComparison.OrdinalIgnoreCase)
                || string.Equals(unit.currentWaypointTargetKind, "insideGateAnchor", StringComparison.OrdinalIgnoreCase);
            if (!wantsAnchorProjection)
                return false;
            if (!HasAuthoritativeRouteSample(unit))
                return true;
            if (ShouldPreferBattlefieldRouteProjection(unit))
                return false;
            if (!IsMovementMode(unit, "AnchorJoin"))
                return false;

            return TryGetAuthoritativeAnchorDistance(unit, out float anchorDistance)
                && anchorDistance <= RouteProjectionAnchorBlendDistance;
        }

        bool TryResolveBattlefieldSegment(
            MLUnit unit,
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

        static bool TryNormalizeBattlefieldSegmentId(string segmentId, out string normalizedSegmentId)
        {
            normalizedSegmentId = null;
            if (!TryParseBattlefieldSegmentId(segmentId, out string fromNode, out string toNode))
                return false;

            normalizedSegmentId = $"{fromNode}_{toNode}";
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
            direction = -BattlefieldSpaceMapper.GetLaneForwardDir(spatialLane);
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
                return false;

            direction = direction.normalized;
            return true;
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

        static bool TrySampleBattlefieldPolyline(
            Vector3[] points,
            float progress,
            float longitudinalOffset,
            float lateralOffset,
            out Vector3 worldPos,
            out Vector3 routeForward)
        {
            worldPos = Vector3.zero;
            routeForward = Vector3.forward;

            if (!TrySampleVector3Polyline(points, progress, out worldPos, out routeForward))
                return false;

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

        bool TryResolveAuthoritativeRouteOffsets(
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

            if (float.IsFinite(unit.routeWorldX) && float.IsFinite(unit.routeWorldY))
            {
                point = new Vector2(unit.routeWorldX, unit.routeWorldY);
                return true;
            }

            failureReason =
                $"authoritative routeWorld sample is invalid " +
                $"routeWorld=({unit.routeWorldX:0.###},{unit.routeWorldY:0.###})";
            return false;
        }

        bool TrySampleRouteSpacePolyline(string segmentId, float progress, out Vector2 routeCenter, out Vector2 routeTangent)
        {
            routeCenter = Vector2.zero;
            routeTangent = Vector2.up;
            if (!TryNormalizeBattlefieldSegmentId(segmentId, out string normalizedSegmentId))
                return false;

            if (!_battlefieldRouteSpacePolylineBySegment.TryGetValue(normalizedSegmentId, out Vector2[] points)
                || points == null
                || points.Length < 2)
                return false;

            return TrySampleVector2Polyline(points, progress, out routeCenter, out routeTangent);
        }

        static bool TrySampleVector3Polyline(Vector3[] points, float progress, out Vector3 samplePoint, out Vector3 tangent)
        {
            samplePoint = Vector3.zero;
            tangent = Vector3.forward;

            if (points == null || points.Length < 2)
                return false;

            float totalLength = 0f;
            for (int i = 0; i < points.Length - 1; i++)
                totalLength += Vector3.Distance(points[i], points[i + 1]);

            if (totalLength <= 0.0001f)
                return false;

            float remainingDistance = totalLength * Mathf.Clamp01(progress);
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector3 from = points[i];
                Vector3 to = points[i + 1];
                float segmentLength = Vector3.Distance(from, to);
                if (segmentLength <= 0.0001f)
                    continue;

                if (remainingDistance <= segmentLength || i == points.Length - 2)
                {
                    float segmentT = Mathf.Clamp01(remainingDistance / segmentLength);
                    samplePoint = Vector3.Lerp(from, to, segmentT);
                    tangent = (to - from).normalized;
                    return tangent.sqrMagnitude > 0.0001f;
                }

                remainingDistance -= segmentLength;
            }

            return false;
        }

        static bool TrySampleVector2Polyline(Vector2[] points, float progress, out Vector2 samplePoint, out Vector2 tangent)
        {
            samplePoint = Vector2.zero;
            tangent = Vector2.up;

            if (points == null || points.Length < 2)
                return false;

            float totalLength = 0f;
            for (int i = 0; i < points.Length - 1; i++)
                totalLength += Vector2.Distance(points[i], points[i + 1]);

            if (totalLength <= 0.0001f)
                return false;

            float remainingDistance = totalLength * Mathf.Clamp01(progress);
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 from = points[i];
                Vector2 to = points[i + 1];
                float segmentLength = Vector2.Distance(from, to);
                if (segmentLength <= 0.0001f)
                    continue;

                if (remainingDistance <= segmentLength || i == points.Length - 2)
                {
                    float segmentT = Mathf.Clamp01(remainingDistance / segmentLength);
                    samplePoint = Vector2.Lerp(from, to, segmentT);
                    tangent = to - from;
                    return tangent.sqrMagnitude > 0.0001f;
                }

                remainingDistance -= segmentLength;
            }

            return false;
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

        static string BuildBattlefieldFortressPadKey(string laneKey, string padId)
        {
            return $"{NormalizeBattlefieldLaneKey(laneKey)}:{padId?.Trim().ToLowerInvariant()}";
        }

        static bool TryResolveLayoutWorldPoint(MLWorldPoint point, out Vector3 worldPos)
        {
            worldPos = default;
            if (point == null || !float.IsFinite(point.x) || !float.IsFinite(point.y))
                return false;

            worldPos = new Vector3(point.x, 0f, point.y);
            return true;
        }

        static bool TryResolveRouteSpacePoint(MLWorldPoint point, out Vector2 routePoint)
        {
            routePoint = default;
            if (point == null || !float.IsFinite(point.x) || !float.IsFinite(point.y))
                return false;

            routePoint = new Vector2(point.x, point.y);
            return true;
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

        void HardFailMatchContent(string detail)
        {
            if (_contentFailureTriggered)
                return;

            _contentFailureTriggered = true;
            Debug.LogError(
                $"[WaveSnapshotRuntimeSpawner] Match content failure. Returning to lobby because authoritative presentation content is missing. {detail}",
                this);

            if (returnToLobbyOnContentFailure && isActiveAndEnabled)
                StartCoroutine(ReturnToLobbyAfterContentFailure());
        }

        System.Collections.IEnumerator ReturnToLobbyAfterContentFailure()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, contentFailureLobbyReturnDelaySeconds));
            LoadingScreen.LoadScene("Lobby");
        }

        static Vector3 ResolveLaneFailureMarkerWorld(int spatialLane)
        {
            return BattlefieldSpaceMapper.TileToWorld(spatialLane, 5, 0) + Vector3.up * 2.4f;
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

        static bool IsDungeonWaveUnit(MLUnit unit)
        {
            if (unit == null)
                return false;

            if (unit.isWaveUnit)
                return true;

            string explicitAllegiance = BattleTeamUtility.NormalizeServerTeamKey(unit.allegianceKey);
            if (string.Equals(explicitAllegiance, "dungeon", StringComparison.OrdinalIgnoreCase))
                return true;

            string spawnSourceType = string.IsNullOrWhiteSpace(unit.spawnSourceType)
                ? null
                : unit.spawnSourceType.Trim();
            return string.Equals(spawnSourceType, "scheduled_wave", StringComparison.OrdinalIgnoreCase)
                || string.Equals(spawnSourceType, "dungeon_wave", StringComparison.OrdinalIgnoreCase);
        }

        static string ResolveOwnerTeamKey(MLUnit unit, MLLaneSnap lane, string defenderTeamKey)
        {
            if (IsDungeonWaveUnit(unit))
                return "dungeon";

            string explicitAllegiance = BattleTeamUtility.NormalizeServerTeamKey(unit?.allegianceKey);
            if (!string.IsNullOrWhiteSpace(explicitAllegiance))
            {
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

            return unit != null && unit.isWaveUnit
                ? "dungeon"
                : null;
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

            LogVerboseSpawnAudit(
                $"[SpawnAudit][PathMode] using {mode} method={methodName} unitId='{unitId}' " +
                $"unitType='{unit?.type ?? "<null>"}' lane={lane?.laneIndex ?? -1} spatialLane={spatialLane} " +
                $"branch='{branch ?? "<none>"}'");
        }

        void LogWorldPositionFailure(MLLaneSnap lane, MLUnit unit, int spatialLane, string reason)
        {
            Debug.LogError(
                $"[SpawnAudit][ClientPositionFail] lane={lane?.laneIndex ?? -1} spatialLane={spatialLane} " +
                $"unitId='{unit?.id ?? "<null>"}' unitType='{unit?.type ?? "<null>"}' team='{lane?.team ?? "<null>"}' " +
                $"waypointKind='{unit?.currentWaypointTargetKind ?? "<null>"}' " +
                $"sourceBarracksKey='{ResolveSourceBarracksKey(unit)}' " +
                $"routeType='{unit?.routeType ?? "<null>"}' currentSegment='{unit?.currentSegment ?? "<null>"}' " +
                $"segmentProgress={(unit != null ? unit.segmentProgress : float.NaN):0.###} " +
                $"pathIdx={(unit != null ? unit.pathIdx : float.NaN):0.###} " +
                $"grid=({(unit != null ? unit.gridX : float.NaN):0.###},{(unit != null ? unit.gridY : float.NaN):0.###}) " +
                $"routeWorld=({(unit != null ? unit.routeWorldX : float.NaN):0.###},{(unit != null ? unit.routeWorldY : float.NaN):0.###}) " +
                $"reason='{reason ?? "<unknown>"}'",
                this);
        }

        void LogVerboseSpawnAudit(string message)
        {
            if (!EnableVerboseSpawnAuditLogs)
                return;

            Debug.Log(message, this);
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

            if (IsDungeonWaveUnit(unit)
                || string.Equals(BattleTeamUtility.NormalizeServerTeamKey(ownerTeamKey), "dungeon", StringComparison.OrdinalIgnoreCase))
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

        static void ApplySnapshotTeamAccents(GameObject root, MLUnit unit, string ownerTeamKey)
        {
            if (root == null)
                return;

            if (IsDungeonWaveUnit(unit)
                || string.Equals(BattleTeamUtility.NormalizeServerTeamKey(ownerTeamKey), "dungeon", StringComparison.OrdinalIgnoreCase))
                return;

            if (!BattleTeamUtility.TryParseServerTeamKey(ownerTeamKey, out BattleTeam team))
                return;

            var accents = root.GetComponent<UnitTeamAccentMarkers>();
            if (accents == null)
                accents = root.AddComponent<UnitTeamAccentMarkers>();

            accents.Apply(team);
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

        static void SetVisualState(WaveView view, UnitAnimationStateIntent desired)
        {
            if (view == null || view.animators == null || view.visualState == desired)
                return;

            view.visualState = desired;
            string[] states = view.animationProfile != null
                ? view.animationProfile.GetStates(desired)
                : UnitAnimationResolver.DefaultIdleStates;
            float transitionSeconds = view.animationProfile != null
                ? view.animationProfile.GetTransitionSeconds(desired)
                : 0.08f;

            UnitAnimationResolver.CrossFadeFirstAvailable(view.animators, states, transitionSeconds);
        }

        void PlayAttackFeedback(MLSnapshot snap, MLLaneSnap lane, MLUnit unit, MLUnit previousSnapshotUnit, float now)
        {
            if (unit == null)
                return;

            UnitAnimationAttackFamily family = UnitAnimationResolver.ResolveRuntimeAttackFamily(unit);
            TryPlayCombatSfx(AttackSfxFor(family), 0.28f, now);
            if (!unit.combatContact)
                return;

            string targetId = !string.IsNullOrWhiteSpace(unit.combatTargetId)
                ? unit.combatTargetId
                : previousSnapshotUnit != null ? previousSnapshotUnit.combatTargetId : null;
            if (string.IsNullOrWhiteSpace(targetId))
                return;

            if (!_lastImpactFxAtByTarget.TryGetValue(targetId, out float lastImpactAt)
                || now - lastImpactAt >= ImpactFxPerTargetCooldownSeconds)
            {
                if (TryResolveCombatTargetWorldPosition(snap, targetId, lane, out Vector3 targetWorld))
                {
                    var hitEffect = HitEffectPool.Get();
                    if (hitEffect != null)
                        hitEffect.Play(targetWorld, ImpactTowerTypeFor(family));
                    _lastImpactFxAtByTarget[targetId] = now;
                }
            }
        }

        void PlayHitReactFeedback(WaveView view, float now)
        {
            if (view == null || view.latestSnapshotUnit == null || view.latestSnapshotUnit.hp <= 0f)
                return;

            float holdSeconds = PlayIntentAnimation(
                view,
                UnitAnimationStateIntent.HitReact,
                HitReactVisualHoldSeconds,
                HitReactVisualMinHoldSeconds,
                HitReactVisualMaxHoldSeconds);
            view.lastHitReactUntil = Mathf.Max(view.lastHitReactUntil, now + holdSeconds);
        }

        static void PlayAttackAnimation(WaveView view)
        {
            if (view == null)
                return;

            float holdSeconds = PlayIntentAnimation(
                view,
                UnitAnimationStateIntent.Attack,
                AttackVisualHoldSeconds,
                AttackVisualMinHoldSeconds,
                AttackVisualMaxHoldSeconds);
            view.lastVisualAttackUntil = Time.time + holdSeconds;
        }

        static float PlayIntentAnimation(
            WaveView view,
            UnitAnimationStateIntent intent,
            float fallbackHoldSeconds,
            float minHoldSeconds,
            float maxHoldSeconds)
        {
            if (view == null)
                return fallbackHoldSeconds;

            float holdSeconds = Mathf.Clamp(fallbackHoldSeconds, minHoldSeconds, maxHoldSeconds);
            bool played = UnitAnimationResolver.PlayIntent(
                view.animators,
                view.animationProfile,
                intent,
                fixedTime: false,
                out string playedState);
            if (played)
            {
                float clipLength = ResolvePlayedClipLength(view, playedState);
                if (clipLength > 0f)
                    holdSeconds = Mathf.Clamp(clipLength, minHoldSeconds, maxHoldSeconds);
                view.visualState = intent;
            }

            return holdSeconds;
        }

        static float ResolvePlayedClipLength(WaveView view, string playedState)
        {
            if (view == null || view.animators == null || string.IsNullOrWhiteSpace(playedState))
                return 0f;

            for (int i = 0; i < view.animators.Length; i++)
            {
                float clipLength = UnitAnimationResolver.ResolveClipLength(view.animators[i], playedState);
                if (clipLength > 0f)
                    return clipLength;
            }

            return 0f;
        }

        void TryPlayCombatSfx(AudioManager.SFX sfx, float volumeScale, float now)
        {
            if (_lastCombatSfxAt.TryGetValue(sfx, out float lastPlayedAt)
                && now - lastPlayedAt < CombatSfxCooldownSeconds)
            {
                return;
            }

            _lastCombatSfxAt[sfx] = now;
            AudioManager.I?.Play(sfx, volumeScale);
        }

        static AudioManager.SFX AttackSfxFor(UnitAnimationAttackFamily family) => family switch
        {
            UnitAnimationAttackFamily.Ranged => AudioManager.SFX.ArcherShoot,
            UnitAnimationAttackFamily.Magic => AudioManager.SFX.MageShoot,
            UnitAnimationAttackFamily.Support => AudioManager.SFX.MageShoot,
            UnitAnimationAttackFamily.Siege => AudioManager.SFX.BallistaShoot,
            _ => AudioManager.SFX.FighterSlash,
        };

        static HitEffect.TowerType ImpactTowerTypeFor(UnitAnimationAttackFamily family) => family switch
        {
            UnitAnimationAttackFamily.Ranged => HitEffect.TowerType.Archer,
            UnitAnimationAttackFamily.Magic => HitEffect.TowerType.Mage,
            UnitAnimationAttackFamily.Support => HitEffect.TowerType.Ballista,
            UnitAnimationAttackFamily.Siege => HitEffect.TowerType.Ballista,
            _ => HitEffect.TowerType.Fighter,
        };

        void DestroyImpactFxHistory(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return;

            _lastImpactFxAtByTarget.Remove(targetId);
        }
    }
}

