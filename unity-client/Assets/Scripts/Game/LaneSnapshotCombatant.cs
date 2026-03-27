using System;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.Game
{
    public class LaneSnapshotCombatant : MonoBehaviour, ILaneCombatant, IWaveHostileLaneCombatant, ITeamOwned
    {
        const float TargetRefreshIntervalSeconds = 0.08f;

        ILaneCombatant _combatTarget;
        BarracksSpawnCombatProfile _combatProfile;
        string _combatantId;
        string _defenderTeamKey;
        BattleTeam _team = BattleTeam.Red;
        bool _hasExplicitTeam;
        float _displayHp;
        float _maxHp = 1f;
        float _buildingClearanceRadius = 0.9f;
        float _attackCooldown;
        float _lastAttackAt = float.MinValue;
        float _retargetAt;
        Transform _hpBarRoot;
        Transform _hpBarFill;
        Image _hpBarImage;
        Vector3 _hpBarFillBaseScale = Vector3.one;
        Vector3 _hpBarFillBaseLocalPosition = Vector3.zero;
        Renderer[] _renderers;
        bool _initialized;
        bool _locallyDefeated;

        public string CombatantId => !string.IsNullOrWhiteSpace(_combatantId) ? _combatantId : name;
        public Vector3 CombatPosition => transform.position;
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

        void OnEnable()
        {
            EnsureHpBar();
            RefreshHpBarVisual();
        }

        void OnDisable()
        {
            LaneCombatRegistry.Unregister(this);
            _combatTarget = null;
        }

        void Update() { }

        public void DebugTick(float dt, float now)
        {
#if UNITY_EDITOR
            _ = dt;
            _ = now;
#endif
        }

        public void Initialize(string combatantId, string unitTypeKey, string skinKey, string defenderTeamKey, float hp, float maxHp, float serverMoveSpeed = 0f)
        {
            _combatantId = combatantId;
            _combatProfile = BarracksSpawnCombatProfileResolver.Resolve(unitTypeKey, skinKey, serverMoveSpeed);
            ApplyAllegiance(defenderTeamKey, null);
            _displayHp = Mathf.Max(1f, hp > 0f ? hp : _combatProfile.maxHp);
            _maxHp = Mathf.Max(1f, maxHp > 0f ? maxHp : _displayHp);
            _attackCooldown = 0f;
            _lastAttackAt = float.MinValue;
            _retargetAt = 0f;
            _combatTarget = null;
            _locallyDefeated = false;
            _initialized = true;
            _renderers = null;
            _buildingClearanceRadius = ResolveFootprintRadius();

            EnsureHpBar();
            RefreshHpBarVisual();
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
            _combatantId = combatantId;
            _combatProfile = BarracksSpawnCombatProfileResolver.Resolve(unitTypeKey, skinKey, serverMoveSpeed);
            ApplyAllegiance(defenderTeamKey, ownerTeamKey);
            _displayHp = Mathf.Max(1f, hp > 0f ? hp : _combatProfile.maxHp);
            _maxHp = Mathf.Max(1f, maxHp > 0f ? maxHp : _displayHp);
            _attackCooldown = 0f;
            _lastAttackAt = float.MinValue;
            _retargetAt = 0f;
            _combatTarget = null;
            _locallyDefeated = false;
            _initialized = true;
            _renderers = null;
            _buildingClearanceRadius = ResolveFootprintRadius();

            EnsureHpBar();
            RefreshHpBarVisual();
        }

        public void ApplySnapshot(string defenderTeamKey, float hp, float maxHp, float serverMoveSpeed = 0f)
        {
            ApplyAllegiance(defenderTeamKey, null);
            _maxHp = Mathf.Max(1f, maxHp > 0f ? maxHp : _maxHp);
            if (serverMoveSpeed > 0f)
                _combatProfile.moveSpeed = BarracksSpawnCombatProfileResolver.ConvertServerPathSpeedToUnityMoveSpeed(serverMoveSpeed);

            float authoritativeHp = Mathf.Clamp(hp, 0f, _maxHp);

            if (!_initialized)
            {
                _displayHp = authoritativeHp > 0f ? authoritativeHp : _maxHp;
            }
            else if (authoritativeHp > 0f)
            {
                // Shared combat can locally overkill wave units; snapshots must be able to
                // revive and resync them so server-authoritative attackers never stay hidden.
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

        public void ApplySnapshot(
            string defenderTeamKey,
            string ownerTeamKey,
            float hp,
            float maxHp,
            float serverMoveSpeed = 0f)
        {
            ApplyAllegiance(defenderTeamKey, ownerTeamKey);
            _maxHp = Mathf.Max(1f, maxHp > 0f ? maxHp : _maxHp);
            if (serverMoveSpeed > 0f)
                _combatProfile.moveSpeed = BarracksSpawnCombatProfileResolver.ConvertServerPathSpeedToUnityMoveSpeed(serverMoveSpeed);

            float authoritativeHp = Mathf.Clamp(hp, 0f, _maxHp);

            if (!_initialized)
            {
                _displayHp = authoritativeHp > 0f ? authoritativeHp : _maxHp;
            }
            else if (authoritativeHp > 0f)
            {
                // Shared combat can locally overkill wave units; snapshots must be able to
                // revive and resync them so server-authoritative attackers never stay hidden.
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

        public bool IsEnemyToTeam(BattleTeam team)
        {
            if (_hasExplicitTeam)
                return BattleTeamUtility.AreEnemies(_team, team);

            return BattleTeamUtility.MatchesServerTeamKey(team, _defenderTeamKey);
        }

        public bool IsEnemyTo(ILaneCombatant other)
        {
            if (other == null || ReferenceEquals(other, this))
                return false;

            if (_hasExplicitTeam)
            {
                if (other is IWaveHostileLaneCombatant waveHostile)
                    return waveHostile.IsEnemyToTeam(_team);

                return other is ITeamOwned hostileTeamOwned && BattleTeamUtility.AreEnemies(_team, hostileTeamOwned.Team);
            }

            return other is ITeamOwned teamOwned && IsEnemyToTeam(teamOwned.Team);
        }

        public void ReceiveDamage(float damage, ILaneCombatant attacker)
        {
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

        void RestoreFromSnapshot(float authoritativeHp)
        {
            _locallyDefeated = false;
            _combatTarget = null;
            _displayHp = authoritativeHp;
        }

        void MarkLocallyDefeated()
        {
            _displayHp = 0f;
            _locallyDefeated = true;
            _combatTarget = null;
            LaneCombatRegistry.Unregister(this);
        }

        void Tick(float dt, float now)
        {
            _ = dt;
            _ = now;
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

            float extraLift = Mathf.Max(0.85f, combined.size.y * 0.35f) + 0.32f;
            Vector3 headWorld = new(combined.center.x, combined.max.y + extraLift, combined.center.z);
            _hpBarRoot.localPosition = transform.InverseTransformPoint(headWorld);
        }

        void RefreshHpBarVisual()
        {
            if (_hpBarRoot == null)
                return;

            float hp01 = Mathf.Clamp01(CurrentHp / MaxHp);
            HpBarVisuals.ApplyFill(_hpBarFill, _hpBarImage, hp01);

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

        void MoveTowardCombatTarget(ILaneCombatant target, float dt)
        {
            if (!IsTargetValid(target))
                return;

            Vector3 current = transform.position;
            Vector3 destination = target.CombatPosition;
            current.y = 0f;
            destination.y = 0f;

            float currentDistance = Vector3.Distance(current, destination);
            float stopDistance = Mathf.Max(0.5f, AttackRange * 0.92f);
            if (currentDistance <= stopDistance)
                return;

            float moveSpeed = Mathf.Max(0.5f, _combatProfile.moveSpeed);
            float step = Mathf.Min(moveSpeed * dt, Mathf.Max(0f, currentDistance - stopDistance));
            if (step <= 0.001f)
                return;

            Vector3 next = Vector3.MoveTowards(transform.position, target.CombatPosition, step);
            next.y = transform.position.y;
            next = BuildingSpaceResolver.ConstrainGroundPosition(next, _buildingClearanceRadius, target.CombatPosition - transform.position);
            transform.position = next;
            FaceDirection(target.CombatPosition - next);
        }

        void TryAttack(ILaneCombatant target)
        {
            if (_attackCooldown > 0f || !IsTargetValid(target))
                return;

            target.ReceiveDamage(_combatProfile.damagePerHit, this);
            _attackCooldown = Mathf.Max(0.05f, _combatProfile.attackIntervalSeconds);
            _lastAttackAt = Time.time;
        }

        ILaneCombatant AcquireNearestEnemy()
        {
            ILaneCombatant best = null;
            float bestDistanceSqr = float.MaxValue;
            float maxDistanceSqr = EngagementRange * EngagementRange;
            var combatants = LaneCombatRegistry.Active;

            for (int i = 0; i < combatants.Count; i++)
            {
                var other = combatants[i];
                if (!IsTargetValid(other))
                    continue;

                float distanceSqr = FlattenDistanceSqr(transform.position, other.CombatPosition);
                if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
                    continue;

                best = other;
                bestDistanceSqr = distanceSqr;
            }

            return best;
        }

        bool IsTargetValid(ILaneCombatant candidate)
        {
            if (candidate == null || ReferenceEquals(candidate, this))
                return false;

            return candidate.IsAlive && IsEnemyTo(candidate);
        }

        bool HasTargetLeftEngagementRange(ILaneCombatant candidate)
        {
            if (!IsTargetValid(candidate))
                return true;

            float allowedDistance = Mathf.Max(AttackRange, EngagementRange);
            return FlattenDistanceSqr(transform.position, candidate.CombatPosition) > allowedDistance * allowedDistance;
        }

        void FaceDirection(Vector3 delta)
        {
            delta.y = 0f;
            if (delta.sqrMagnitude <= 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
        }

        float ResolveFootprintRadius()
        {
            _renderers ??= GetComponentsInChildren<Renderer>(true);
            if (_renderers == null || _renderers.Length == 0)
                return 0.9f;

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
                return 0.9f;

            return Mathf.Clamp(Mathf.Max(combined.extents.x, combined.extents.z) * 0.45f, 0.75f, 2.25f);
        }

        static string NormalizeTeamKey(string teamKey)
        {
            return BattleTeamUtility.NormalizeServerTeamKey(teamKey);
        }

        void ApplyAllegiance(string defenderTeamKey, string ownerTeamKey)
        {
            _defenderTeamKey = NormalizeTeamKey(defenderTeamKey);
            _hasExplicitTeam = BattleTeamUtility.TryParseServerTeamKey(ownerTeamKey, out _team);

            if (!_hasExplicitTeam && BattleTeamUtility.TryParseServerTeamKey(_defenderTeamKey, out BattleTeam parsedTeam))
                _team = parsedTeam;
        }

        static GameObject ResolveHpBarPrefab()
        {
            var waveRuntime = FindFirstObjectByType<WaveSnapshotRuntimeSpawner>();
            if (waveRuntime != null && waveRuntime.HpBarPrefab != null)
                return waveRuntime.HpBarPrefab;

            var laneRenderer = FindFirstObjectByType<LaneRenderer>();
            if (laneRenderer != null && laneRenderer.HpBarPrefab != null)
                return laneRenderer.HpBarPrefab;

            var tileGrid = FindFirstObjectByType<TileGrid>();
            if (tileGrid != null && tileGrid.HpBarPrefab != null)
                return tileGrid.HpBarPrefab;

            return null;
        }

        static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName))
                return null;
            if (root.name == childName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        static float FlattenDistanceSqr(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return (a - b).sqrMagnitude;
        }
    }
}
