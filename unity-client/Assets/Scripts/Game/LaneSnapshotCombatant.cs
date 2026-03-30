using System;
using UnityEngine;
using UnityEngine.UI;

namespace CastleDefender.Game
{
    // Snapshot units are presentation hosts for the authoritative server state.
    // They no longer run local target acquisition, movement, or combat loops.
    public class LaneSnapshotCombatant : MonoBehaviour, ITeamOwned
    {
        BarracksSpawnCombatProfile _combatProfile;
        string _combatantId;
        string _defenderTeamKey;
        BattleTeam _team = BattleTeam.Red;
        bool _hasExplicitTeam;
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

        void OnEnable()
        {
            EnsureHpBar();
            RefreshHpBarVisual();
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
    }
}
