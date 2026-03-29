using System;
using UnityEngine;

namespace CastleDefender.Game
{
    public enum UnitAnimationAttackFamily
    {
        Unspecified = 0,
        Default = 1,
        Melee = 2,
        Ranged = 3,
        Magic = 4,
        Support = 5,
        Siege = 6,
    }

    public enum UnitAnimationStateIntent
    {
        Idle = 0,
        Move = 1,
        Attack = 2,
        Defend = 3,
        Retreat = 4,
        HitReact = 5,
        Death = 6,
        Spawn = 7,
    }

    [Serializable]
    public class UnitAnimationStateAliases
    {
        public string[] stateNames = Array.Empty<string>();

        public bool HasStates => stateNames != null && stateNames.Length > 0;
    }

    [Serializable]
    public class UnitAnimationTransitionSettings
    {
        [Min(0f)] public float idleTransitionSeconds = 0.08f;
        [Min(0f)] public float moveTransitionSeconds = 0.08f;
        [Min(0f)] public float attackTransitionSeconds = 0.04f;
        [Min(0f)] public float defendTransitionSeconds = 0.08f;
        [Min(0f)] public float retreatTransitionSeconds = 0.08f;
        [Min(0f)] public float hitTransitionSeconds = 0.05f;
        [Min(0f)] public float deathTransitionSeconds = 0.05f;
        [Min(0f)] public float spawnTransitionSeconds = 0.05f;
    }

    [CreateAssetMenu(
        menuName = "CastleDefender/Animation/Unit Animation Profile",
        fileName = "UnitAnimationProfile")]
    public class UnitAnimationProfile : ScriptableObject
    {
        [Header("Identity")]
        public string profileId = "unit_animation_profile";

        [TextArea(2, 6)]
        public string notes;

        [Header("Optional Controller Overrides")]
        public RuntimeAnimatorController runtimeController;
        public RuntimeAnimatorController portraitController;
        public bool overrideExistingControllers;

        [Header("Animator Settings")]
        public bool applyRootMotion;
        [Min(0.05f)] public float animatorSpeedMultiplier = 1f;
        public UnitAnimationAttackFamily defaultAttackFamily = UnitAnimationAttackFamily.Unspecified;

        [Header("State Aliases")]
        public UnitAnimationStateAliases idle = new()
        {
            stateNames = new[] { "Idle", "IdleNormal", "IdleCombat", "Idle-Sheathed", "Sheathed", "UnSheathed", "Unsheathed", "idle" }
        };

        public UnitAnimationStateAliases move = new()
        {
            stateNames = new[] { "WalkRun", "Run", "Walk", "Move", "run", "walk" }
        };

        public UnitAnimationStateAliases defend = new()
        {
            stateNames = new[] { "Blocking", "Block", "Defend", "ShieldBlock", "IdleCombat", "Idle" }
        };

        public UnitAnimationStateAliases retreat = new()
        {
            stateNames = new[] { "WalkRun", "Run", "Walk", "Move", "Retreat", "run", "walk" }
        };

        public UnitAnimationStateAliases hitReact = new()
        {
            stateNames = new[] { "Damage", "LightHit", "Block-HitReact", "Hit", "HitReact", "Hurt" }
        };

        public UnitAnimationStateAliases death = new()
        {
            stateNames = new[] { "Death", "Die", "Knockout", "death" }
        };

        public UnitAnimationStateAliases spawn = new()
        {
            stateNames = new[] { "WeaponUnSheath", "WeaponUnsheath2", "UnSheathed", "Unsheathed", "Spawn", "Summon", "Unsheathe", "Idle" }
        };

        [Header("Attack Aliases")]
        public UnitAnimationStateAliases attackDefault = new()
        {
            stateNames = new[] { "Attack1", "Attack2", "Attack3", "MoveAttack1", "MoveAttack2", "SpecialAttack1", "SpecialAttack2", "Attack", "attack" }
        };

        public UnitAnimationStateAliases attackMelee = new()
        {
            stateNames = new[] { "Attack1", "Attack2", "Attack3", "MoveAttack1", "MoveAttack2", "Run2-Attack1", "SpecialAttack1", "SpecialAttack2", "AttackSwordShield", "AttackDaggers", "AttackHeavy", "Attack", "attack" }
        };

        public UnitAnimationStateAliases attackRanged = new()
        {
            stateNames = new[] { "RangeAttack1", "Aiming-Firing", "Attack1", "MoveAttack1", "MoveAttack2", "SpecialAttack1", "Shoot", "AttackBow", "AttackCrossbow", "Attack", "attack" }
        };

        public UnitAnimationStateAliases attackMagic = new()
        {
            stateNames = new[] { "RangeAttack1", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2", "Cast", "CastSpell", "AttackCast", "Shoot", "Attack", "attack" }
        };

        public UnitAnimationStateAliases attackSupport = new()
        {
            stateNames = new[] { "RangeAttack1", "Attack1", "Attack2", "SpecialAttack1", "SpecialAttack2", "Cast", "CastSpell", "AttackCast", "Attack", "attack" }
        };

        public UnitAnimationStateAliases attackSiege = new()
        {
            stateNames = new[] { "RangeAttack1", "SpecialAttack1", "SpecialAttack2", "Shoot", "AttackHeavy", "Attack1", "Attack2", "Attack", "attack" }
        };

        [Header("Transitions")]
        public UnitAnimationTransitionSettings transitions = new();
    }
}
