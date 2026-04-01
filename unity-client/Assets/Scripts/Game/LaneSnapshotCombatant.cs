using System;
using System.Collections.Generic;
using CastleDefender.Net;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.Game
{
    // Snapshot units are presentation hosts for the authoritative server state.
    // They no longer run local target acquisition, movement, or combat loops.
    public class LaneSnapshotCombatant : MonoBehaviour, ITeamOwned
    {
        static readonly Color DungeonHpBarFill = new(0.74f, 0.28f, 0.98f, 1f);
        static readonly Color DungeonHpBarFrame = new(0.88f, 0.62f, 1.00f, 0.88f);
        static readonly Color HostileHpBarFill = new(0.98f, 0.48f, 0.20f, 0.98f);
        static readonly Color HostileHpBarFrame = new(1.00f, 0.74f, 0.44f, 0.84f);
        static readonly Dictionary<string, LaneSnapshotCombatant> s_activeCombatants = new(StringComparer.OrdinalIgnoreCase);
        BarracksSpawnCombatProfile _combatProfile;
        string _combatantId;
        string _registeredCombatantId;
        string _defenderTeamKey;
        string _ownerTeamKey;
        BattleTeam _team = BattleTeam.Red;
        bool _hasExplicitTeam;
        bool _isDungeonUnit;
        float _displayHp;
        float _maxHp = 1f;
        float _lastAttackAt = float.MinValue;
        Transform _hpBarRoot;
        Transform _hpBarFill;
        Image _hpBarImage;
        Vector3 _hpBarFillBaseScale = Vector3.one;
        Vector3 _hpBarFillBaseLocalPosition = Vector3.zero;
        Renderer[] _renderers;
        bool _initialized;
        bool _locallyDefeated;
        string _debugPresentationPhase;
        string _debugPresentationIntent;
        string _debugMovementMode;
        string _debugMovementState;
        string _debugCombatTargetId;
        string _debugCurrentTargetId;
        string _debugCurrentSegment;
        string _debugCurrentWaypointTargetKind;
        string _debugRouteType;
        string _debugSpawnSourceType;
        bool _debugCombatContact;
        bool _debugIsAttacking;
        bool _debugIsWaveUnit;
        int _debugAssignedSlotIndex = -1;
        int _debugOwnerLaneIndex = -1;
        int _debugTargetLaneIndex = -1;
        float _debugSegmentProgress;
        float _debugRouteWorldX;
        float _debugRouteWorldY;
        float _debugAnchorTargetX;
        float _debugAnchorTargetY;

        public string CombatantId => !string.IsNullOrWhiteSpace(_combatantId) ? _combatantId : name;
        public bool IsAlive => _initialized && !_locallyDefeated && _displayHp > 0f;
        public float CurrentHp => Mathf.Max(0f, _displayHp);
        public float MaxHp => Mathf.Max(1f, _maxHp);
        public float AttackRange => Mathf.Max(0.5f, _combatProfile.attackRange);
        public float EngagementRange => Mathf.Max(AttackRange, _combatProfile.engagementRange);
        public string DefenderTeamKey => _defenderTeamKey;
        public BattleTeam Team => _team;
        public bool IsLocallyControllingPosition => false;
        public bool IsLocallyDefeated => _locallyDefeated;
        public bool HasCombatTarget => false;
        public float LastAttackAt => _lastAttackAt;
        public string DebugPresentationPhase => _debugPresentationPhase ?? string.Empty;
        public string DebugPresentationIntent => _debugPresentationIntent ?? string.Empty;
        public string DebugMovementMode => _debugMovementMode ?? string.Empty;
        public string DebugMovementState => _debugMovementState ?? string.Empty;
        public string DebugCombatTargetId => _debugCombatTargetId ?? string.Empty;
        public string DebugCurrentTargetId => _debugCurrentTargetId ?? string.Empty;
        public string DebugCurrentSegment => _debugCurrentSegment ?? string.Empty;
        public string DebugCurrentWaypointTargetKind => _debugCurrentWaypointTargetKind ?? string.Empty;
        public string DebugRouteType => _debugRouteType ?? string.Empty;
        public string DebugSpawnSourceType => _debugSpawnSourceType ?? string.Empty;
        public bool DebugCombatContact => _debugCombatContact;
        public bool DebugIsAttacking => _debugIsAttacking;
        public bool DebugIsWaveUnit => _debugIsWaveUnit;
        public int DebugAssignedSlotIndex => _debugAssignedSlotIndex;
        public int DebugOwnerLaneIndex => _debugOwnerLaneIndex;
        public int DebugTargetLaneIndex => _debugTargetLaneIndex;
        public float DebugSegmentProgress => _debugSegmentProgress;
        public float DebugRouteWorldX => _debugRouteWorldX;
        public float DebugRouteWorldY => _debugRouteWorldY;
        public float DebugAnchorTargetX => _debugAnchorTargetX;
        public float DebugAnchorTargetY => _debugAnchorTargetY;

        public static bool TryResolveWorldPosition(string combatantId, out Vector3 worldPos)
        {
            worldPos = default;
            if (string.IsNullOrWhiteSpace(combatantId))
                return false;

            if (!s_activeCombatants.TryGetValue(combatantId.Trim(), out LaneSnapshotCombatant combatant)
                || combatant == null
                || !combatant.isActiveAndEnabled)
            {
                return false;
            }

            worldPos = combatant.transform.position;
            return true;
        }

        void OnEnable()
        {
            RegisterActiveCombatant();
            EnsureHpBar();
            RefreshHpBarVisual();
        }

        void OnDisable()
        {
            UnregisterActiveCombatant();
        }

        void OnDestroy()
        {
            UnregisterActiveCombatant();
        }

        public void DebugTick(float dt, float now)
        {
#if UNITY_EDITOR
            _ = dt;
            _ = now;
#endif
        }

        public void Initialize(string combatantId, string unitTypeKey, string skinKey, string defenderTeamKey, float hp, float maxHp, float serverMoveSpeed = 0f)
        {
            InitializeInternal(combatantId, unitTypeKey, skinKey, defenderTeamKey, null, hp, maxHp, serverMoveSpeed);
        }

        public void InitializeSnapshot(
            string combatantId,
            string unitTypeKey,
            string skinKey,
            string defenderTeamKey,
            string ownerTeamKey,
            float hp,
            float maxHp,
            float serverMoveSpeed = 0f)
        {
            InitializeInternal(combatantId, unitTypeKey, skinKey, defenderTeamKey, ownerTeamKey, hp, maxHp, serverMoveSpeed);
        }

        public void ApplySnapshot(string defenderTeamKey, float hp, float maxHp, float serverMoveSpeed = 0f)
        {
            ApplySnapshotInternal(defenderTeamKey, null, hp, maxHp, serverMoveSpeed);
        }

        public void ApplySnapshot(
            string defenderTeamKey,
            string ownerTeamKey,
            float hp,
            float maxHp,
            float serverMoveSpeed = 0f)
        {
            ApplySnapshotInternal(defenderTeamKey, ownerTeamKey, hp, maxHp, serverMoveSpeed);
        }

        public void NotifyAttack(float attackTime)
        {
            _lastAttackAt = attackTime;
        }

        public void ApplyPresentationSnapshot(MLUnit unit)
        {
            _debugPresentationPhase = unit?.presentationPhase;
            _debugPresentationIntent = unit?.presentationIntent;
            _debugMovementMode = unit?.movementMode;
            _debugMovementState = unit?.movementState;
            _debugCombatTargetId = unit?.combatTargetId;
            _debugCurrentTargetId = unit?.currentTargetId;
            _debugCurrentSegment = unit?.currentSegment;
            _debugCurrentWaypointTargetKind = unit?.currentWaypointTargetKind;
            _debugRouteType = unit?.routeType;
            _debugSpawnSourceType = unit?.spawnSourceType;
            _debugCombatContact = unit != null && unit.combatContact;
            _debugIsAttacking = unit != null && unit.isAttacking;
            _debugIsWaveUnit = unit != null && unit.isWaveUnit;
            _debugAssignedSlotIndex = unit != null ? unit.assignedSlotIndex : -1;
            _debugOwnerLaneIndex = unit != null ? unit.ownerLaneIndex : -1;
            _debugTargetLaneIndex = unit != null ? unit.targetLaneIndex : -1;
            _debugSegmentProgress = unit != null ? unit.segmentProgress : 0f;
            _debugRouteWorldX = unit != null ? unit.routeWorldX : 0f;
            _debugRouteWorldY = unit != null ? unit.routeWorldY : 0f;
            _debugAnchorTargetX = unit != null ? unit.anchorTargetX : 0f;
            _debugAnchorTargetY = unit != null ? unit.anchorTargetY : 0f;
        }

        // Kept as a snapshot-reconciliation hook for runtime tests and any future
        // purely-visual client hit feedback. The server snapshot remains authoritative.
        public void ReceiveDamage(float damage, object attacker)
        {
            _ = attacker;
            if (!IsAlive)
                return;

            _displayHp = Mathf.Max(0f, _displayHp - Mathf.Max(0f, damage));
            RefreshHpBarVisual();
            if (_displayHp > 0f)
                return;

            MarkLocallyDefeated();
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        void InitializeInternal(
            string combatantId,
            string unitTypeKey,
            string skinKey,
            string defenderTeamKey,
            string ownerTeamKey,
            float hp,
            float maxHp,
            float serverMoveSpeed)
        {
            _combatantId = combatantId;
            _combatProfile = BarracksSpawnCombatProfileResolver.Resolve(unitTypeKey, skinKey, serverMoveSpeed);
            ApplyAllegiance(defenderTeamKey, ownerTeamKey);
            _displayHp = Mathf.Max(1f, hp > 0f ? hp : _combatProfile.maxHp);
            _maxHp = Mathf.Max(1f, maxHp > 0f ? maxHp : _displayHp);
            _lastAttackAt = float.MinValue;
            _locallyDefeated = false;
            _initialized = true;
            _renderers = null;
            RegisterActiveCombatant();

            EnsureHpBar();
            RefreshHpBarVisual();
        }

        void ApplySnapshotInternal(
            string defenderTeamKey,
            string ownerTeamKey,
            float hp,
            float maxHp,
            float serverMoveSpeed)
        {
            ApplyAllegiance(defenderTeamKey, ownerTeamKey);
            _maxHp = Mathf.Max(1f, maxHp > 0f ? maxHp : _maxHp);

            if (serverMoveSpeed > 0f)
                _combatProfile.moveSpeed = BarracksSpawnCombatProfileResolver.ConvertServerPathSpeedToUnityMoveSpeed(serverMoveSpeed);

            float authoritativeHp = Mathf.Clamp(hp, 0f, _maxHp);
            if (!_initialized)
            {
                _displayHp = authoritativeHp > 0f ? authoritativeHp : _maxHp;
                _initialized = true;
            }
            else if (authoritativeHp > 0f)
            {
                if (_locallyDefeated)
                    RestoreFromSnapshot(authoritativeHp);
                else
                    _displayHp = authoritativeHp;
            }
            else
            {
                MarkLocallyDefeated();
            }

            RefreshHpBarVisual();
        }

        void RestoreFromSnapshot(float authoritativeHp)
        {
            _locallyDefeated = false;
            _displayHp = authoritativeHp;
        }

        void MarkLocallyDefeated()
        {
            _displayHp = 0f;
            _locallyDefeated = true;
        }

        void EnsureHpBar()
        {
            if (_hpBarRoot != null)
                return;

            GameObject hpBarPrefab = ResolveHpBarPrefab();
            if (hpBarPrefab == null)
                return;

            var bar = Instantiate(hpBarPrefab, transform);
            bar.name = "HpBarUI";
            _hpBarRoot = bar.transform;

            HpBarVisuals.EnsureStyled(_hpBarRoot);
            HpBarVisuals.ConfigureUnitBar(_hpBarRoot);
            _hpBarFill = FindChildRecursive(_hpBarRoot, "Fill");
            _hpBarImage = _hpBarFill != null ? _hpBarFill.GetComponent<Image>() : _hpBarRoot.GetComponentInChildren<Image>(true);
            _hpBarFillBaseScale = _hpBarFill != null ? _hpBarFill.localScale : Vector3.one;
            _hpBarFillBaseLocalPosition = _hpBarFill != null ? _hpBarFill.localPosition : Vector3.zero;
            PositionHpBarOverHead();
        }

        void PositionHpBarOverHead()
        {
            if (_hpBarRoot == null)
                return;

            _renderers ??= GetComponentsInChildren<Renderer>(true);
            if (_renderers == null || _renderers.Length == 0)
            {
                _hpBarRoot.localPosition = Vector3.up * 3.2f;
                return;
            }

            bool hasBounds = false;
            Bounds combined = default;
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    combined = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                _hpBarRoot.localPosition = Vector3.up * 3.2f;
                return;
            }

            float extraLift = Mathf.Max(1.15f, combined.size.y * 0.45f) + 0.56f;
            Vector3 headWorld = new(combined.center.x, combined.max.y + extraLift, combined.center.z);
            _hpBarRoot.localPosition = transform.InverseTransformPoint(headWorld);
        }

        void RefreshHpBarVisual()
        {
            if (_hpBarRoot == null)
                return;

            float hp01 = Mathf.Clamp01(CurrentHp / MaxHp);
            ResolveHpBarColors(out Color fillColor, out Color frameColor);
            HpBarVisuals.ApplyFill(_hpBarFill, _hpBarImage, hp01, fillColor);
            HpBarVisuals.ApplyFrameColor(_hpBarRoot, frameColor);

            if (_hpBarFill != null)
            {
                _hpBarFill.localScale = new Vector3(
                    _hpBarFillBaseScale.x * hp01,
                    _hpBarFillBaseScale.y,
                    _hpBarFillBaseScale.z);

                _hpBarFill.localPosition = new Vector3(
                    0.5f * hp01,
                    _hpBarFillBaseLocalPosition.y,
                    _hpBarFillBaseLocalPosition.z);
            }
        }

        void ResolveHpBarColors(out Color fillColor, out Color frameColor)
        {
            if (_hasExplicitTeam)
            {
                Color teamColor = BattleTeamUtility.ToColor(_team);
                fillColor = Color.Lerp(teamColor, Color.white, 0.04f);
                fillColor.a = 1f;
                frameColor = Color.Lerp(teamColor, Color.white, 0.18f);
                frameColor.a = 0.84f;
                return;
            }

            if (_isDungeonUnit)
            {
                fillColor = DungeonHpBarFill;
                frameColor = DungeonHpBarFrame;
                return;
            }

            fillColor = HostileHpBarFill;
            frameColor = HostileHpBarFrame;
        }

        static string NormalizeTeamKey(string teamKey)
        {
            return BattleTeamUtility.NormalizeServerTeamKey(teamKey);
        }

        void ApplyAllegiance(string defenderTeamKey, string ownerTeamKey)
        {
            _defenderTeamKey = NormalizeTeamKey(defenderTeamKey);
            _ownerTeamKey = NormalizeTeamKey(ownerTeamKey);
            _isDungeonUnit = string.Equals(_ownerTeamKey, "dungeon", StringComparison.OrdinalIgnoreCase);
            _hasExplicitTeam = BattleTeamUtility.TryParseServerTeamKey(ownerTeamKey, out _team);

            if (!_hasExplicitTeam && BattleTeamUtility.TryParseServerTeamKey(_defenderTeamKey, out BattleTeam parsedTeam))
                _team = parsedTeam;
        }

        static GameObject ResolveHpBarPrefab()
        {
            return GameplayPresentationRoot.ResolveHpBarPrefab();
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
                return null;
            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        void RegisterActiveCombatant()
        {
            string trimmedId = !string.IsNullOrWhiteSpace(_combatantId)
                ? _combatantId.Trim()
                : null;

            if (string.IsNullOrWhiteSpace(trimmedId))
            {
                UnregisterActiveCombatant();
                return;
            }

            if (!string.IsNullOrWhiteSpace(_registeredCombatantId)
                && !string.Equals(_registeredCombatantId, trimmedId, StringComparison.OrdinalIgnoreCase))
            {
                s_activeCombatants.Remove(_registeredCombatantId);
            }

            s_activeCombatants[trimmedId] = this;
            _registeredCombatantId = trimmedId;
        }

        void UnregisterActiveCombatant()
        {
            if (string.IsNullOrWhiteSpace(_registeredCombatantId))
                return;

            if (s_activeCombatants.TryGetValue(_registeredCombatantId, out LaneSnapshotCombatant registered)
                && registered == this)
            {
                s_activeCombatants.Remove(_registeredCombatantId);
            }

            _registeredCombatantId = null;
        }
    }
}
