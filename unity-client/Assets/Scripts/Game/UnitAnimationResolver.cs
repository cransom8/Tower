using System;
using System.Collections.Generic;
using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    public static class UnitAnimationResolver
    {
        public static readonly string[] DefaultMoveStates = { "WalkRun", "Run", "Walk", "Move", "run", "walk" };
        public static readonly string[] DefaultIdleStates = { "Idle", "IdleNormal", "IdleCombat", "Idle-Sheathed", "Sheathed", "UnSheathed", "Unsheathed", "idle" };
        public static readonly string[] DefaultAttackStates =
        {
            "Attack1",
            "Attack2",
            "Attack3",
            "MoveAttack1",
            "MoveAttack2",
            "SpecialAttack1",
            "SpecialAttack2",
            "Attack",
            "attack",
        };
        public static readonly string[] DefaultMeleeAttackStates =
        {
            "Attack1",
            "Attack2",
            "Attack3",
            "MoveAttack1",
            "MoveAttack2",
            "Run2-Attack1",
            "SpecialAttack1",
            "SpecialAttack2",
            "AttackSwordShield",
            "AttackDaggers",
            "AttackHeavy",
            "Attack",
            "attack",
        };
        public static readonly string[] DefaultRangedAttackStates =
        {
            "RangeAttack1",
            "Aiming-Firing",
            "Attack1",
            "MoveAttack1",
            "MoveAttack2",
            "SpecialAttack1",
            "Shoot",
            "AttackBow",
            "AttackCrossbow",
            "Attack",
            "attack",
        };
        public static readonly string[] DefaultMagicAttackStates =
        {
            "RangeAttack1",
            "Attack1",
            "Attack2",
            "SpecialAttack1",
            "SpecialAttack2",
            "Cast",
            "CastSpell",
            "AttackCast",
            "Shoot",
            "Attack",
            "attack",
        };
        public static readonly string[] DefaultSupportAttackStates =
        {
            "RangeAttack1",
            "Attack1",
            "Attack2",
            "SpecialAttack1",
            "SpecialAttack2",
            "Cast",
            "CastSpell",
            "AttackCast",
            "Attack",
            "attack",
        };
        public static readonly string[] DefaultSiegeAttackStates =
        {
            "RangeAttack1",
            "SpecialAttack1",
            "SpecialAttack2",
            "Shoot",
            "AttackHeavy",
            "Attack1",
            "Attack2",
            "Attack",
            "attack",
        };
        public static readonly string[] DefaultDefendStates = { "Blocking", "Block", "Defend", "ShieldBlock", "IdleCombat", "Idle" };
        public static readonly string[] DefaultRetreatStates = { "WalkRun", "Run", "Walk", "Move", "Retreat", "run", "walk" };
        public static readonly string[] DefaultHitReactStates = { "Damage", "LightHit", "Block-HitReact", "Hit", "HitReact", "Hurt" };
        public static readonly string[] DefaultDeathStates = { "Death", "Die", "Knockout", "death" };
        public static readonly string[] DefaultSpawnStates =
        {
            "WeaponUnSheath",
            "WeaponUnsheath2",
            "UnSheathed",
            "Unsheathed",
            "Spawn",
            "Summon",
            "Unsheathe",
            "Idle",
        };
        static readonly string[] CommonStatePathPrefixes =
        {
            "Base Layer.",
            "Base Layer.Movement.",
            "Base Layer.Attacks.",
            "Base Layer.Upperbody.",
            "Base Layer.Jumping.",
            "Base Layer.Dashes.",
            "Base Layer.Weapon Switching.",
            "Movement.",
            "Attacks.",
            "Upperbody.",
            "Jumping.",
            "Dashes.",
            "Weapon Switching.",
        };

        public sealed class ResolvedProfile
        {
            public string ProfileId { get; set; }
            public string DebugSource { get; set; }
            public RuntimeAnimatorController RuntimeController { get; set; }
            public RuntimeAnimatorController PortraitController { get; set; }
            public bool OverrideExistingControllers { get; set; }
            public bool ApplyRootMotion { get; set; }
            public float AnimatorSpeedMultiplier { get; set; }
            public string[] IdleStates { get; set; }
            public string[] MoveStates { get; set; }
            public string[] AttackStates { get; set; }
            public string[] DefendStates { get; set; }
            public string[] RetreatStates { get; set; }
            public string[] HitReactStates { get; set; }
            public string[] DeathStates { get; set; }
            public string[] SpawnStates { get; set; }
            public float IdleTransitionSeconds { get; set; }
            public float MoveTransitionSeconds { get; set; }
            public float AttackTransitionSeconds { get; set; }
            public float DefendTransitionSeconds { get; set; }
            public float RetreatTransitionSeconds { get; set; }
            public float HitTransitionSeconds { get; set; }
            public float DeathTransitionSeconds { get; set; }
            public float SpawnTransitionSeconds { get; set; }

            public string[] GetStates(UnitAnimationStateIntent intent)
            {
                return intent switch
                {
                    UnitAnimationStateIntent.Move => MoveStates,
                    UnitAnimationStateIntent.Attack => AttackStates,
                    UnitAnimationStateIntent.Defend => DefendStates,
                    UnitAnimationStateIntent.Retreat => RetreatStates,
                    UnitAnimationStateIntent.HitReact => HitReactStates,
                    UnitAnimationStateIntent.Death => DeathStates,
                    UnitAnimationStateIntent.Spawn => SpawnStates,
                    _ => IdleStates,
                };
            }

            public float GetTransitionSeconds(UnitAnimationStateIntent intent)
            {
                return intent switch
                {
                    UnitAnimationStateIntent.Move => MoveTransitionSeconds,
                    UnitAnimationStateIntent.Attack => AttackTransitionSeconds,
                    UnitAnimationStateIntent.Defend => DefendTransitionSeconds,
                    UnitAnimationStateIntent.Retreat => RetreatTransitionSeconds,
                    UnitAnimationStateIntent.HitReact => HitTransitionSeconds,
                    UnitAnimationStateIntent.Death => DeathTransitionSeconds,
                    UnitAnimationStateIntent.Spawn => SpawnTransitionSeconds,
                    _ => IdleTransitionSeconds,
                };
            }
        }

        public static ResolvedProfile ResolveForUnit(GameObject root, MLUnit unit)
        {
            var binding = root != null ? root.GetComponentInChildren<UnitAnimationBinding>(true) : null;
            var profile = binding != null ? binding.profile : null;
            var attackFamily = ResolveAttackFamily(unit, binding, profile);

            return new ResolvedProfile
            {
                ProfileId = ResolveProfileId(binding, profile),
                DebugSource = ResolveDebugSource(binding, profile),
                RuntimeController = binding != null && binding.runtimeControllerOverride != null
                    ? binding.runtimeControllerOverride
                    : profile != null ? profile.runtimeController : null,
                PortraitController = binding != null && binding.portraitControllerOverride != null
                    ? binding.portraitControllerOverride
                    : profile != null ? profile.portraitController : null,
                OverrideExistingControllers = (binding != null && binding.overrideExistingControllers)
                    || (profile != null && profile.overrideExistingControllers),
                ApplyRootMotion = ResolveApplyRootMotion(binding, profile),
                AnimatorSpeedMultiplier = ResolveAnimatorSpeed(binding, profile),
                IdleStates = ResolveStates(profile != null ? profile.idle : null, DefaultIdleStates),
                MoveStates = ResolveStates(profile != null ? profile.move : null, DefaultMoveStates),
                AttackStates = ResolveAttackStates(profile, attackFamily),
                DefendStates = ResolveStates(profile != null ? profile.defend : null, DefaultDefendStates),
                RetreatStates = ResolveStates(profile != null ? profile.retreat : null, DefaultRetreatStates),
                HitReactStates = ResolveStates(profile != null ? profile.hitReact : null, DefaultHitReactStates),
                DeathStates = ResolveStates(profile != null ? profile.death : null, DefaultDeathStates),
                SpawnStates = ResolveStates(profile != null ? profile.spawn : null, DefaultSpawnStates),
                IdleTransitionSeconds = profile != null ? profile.transitions.idleTransitionSeconds : 0.08f,
                MoveTransitionSeconds = profile != null ? profile.transitions.moveTransitionSeconds : 0.08f,
                AttackTransitionSeconds = profile != null ? profile.transitions.attackTransitionSeconds : 0.04f,
                DefendTransitionSeconds = profile != null ? profile.transitions.defendTransitionSeconds : 0.08f,
                RetreatTransitionSeconds = profile != null ? profile.transitions.retreatTransitionSeconds : 0.08f,
                HitTransitionSeconds = profile != null ? profile.transitions.hitTransitionSeconds : 0.05f,
                DeathTransitionSeconds = profile != null ? profile.transitions.deathTransitionSeconds : 0.05f,
                SpawnTransitionSeconds = profile != null ? profile.transitions.spawnTransitionSeconds : 0.05f,
            };
        }

        public static UnitAnimationStateIntent ResolveRuntimeIntent(MLUnit unit, bool moving, bool attacking)
        {
            if (unit != null && unit.hp <= 0f)
                return UnitAnimationStateIntent.Death;

            if (attacking)
                return UnitAnimationStateIntent.Attack;

            if (IsRetreating(unit) && moving)
                return UnitAnimationStateIntent.Retreat;

            if (IsDefending(unit) && !moving)
                return UnitAnimationStateIntent.Defend;

            return moving ? UnitAnimationStateIntent.Move : UnitAnimationStateIntent.Idle;
        }

        public static void PrepareAnimators(Animator[] animators, ResolvedProfile profile, bool forPortrait)
        {
            if (animators == null)
                return;

            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null)
                    continue;

                RuntimeAnimatorController desiredController = forPortrait
                    ? profile?.PortraitController
                    : profile?.RuntimeController;

                if (profile != null && profile.OverrideExistingControllers && desiredController != null)
                    animator.runtimeAnimatorController = desiredController;

                animator.applyRootMotion = !forPortrait && profile != null && profile.ApplyRootMotion;
                animator.speed = profile != null ? Mathf.Max(0.05f, profile.AnimatorSpeedMultiplier) : 1f;
            }
        }

        public static bool TryFindPlayableState(
            Animator[] animators,
            string[] stateNames,
            out Animator foundAnimator,
            out string foundState)
        {
            foundAnimator = null;
            foundState = null;

            if (animators == null || stateNames == null || stateNames.Length == 0)
                return false;

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

                    foundAnimator = animator;
                    foundState = stateName;
                    return true;
                }
            }

            return false;
        }

        public static bool CrossFadeFirstAvailable(
            Animator[] animators,
            string[] stateNames,
            float transitionDuration,
            bool fixedTime = false)
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

                    if (fixedTime)
                        animator.CrossFadeInFixedTime(stateHash, transitionDuration, 0, 0f);
                    else
                        animator.CrossFade(stateHash, transitionDuration, 0, 0f);

                    playedAny = true;
                    break;
                }
            }

            return playedAny;
        }

        public static bool PlayIntent(
            Animator[] animators,
            ResolvedProfile profile,
            UnitAnimationStateIntent intent,
            bool fixedTime,
            out string playedState)
        {
            playedState = null;
            if (animators == null)
                return false;

            string[] stateNames = profile != null ? profile.GetStates(intent) : DefaultIdleStates;
            if (!TryFindPlayableState(animators, stateNames, out _, out playedState))
                return false;

            return CrossFadeFirstAvailable(
                animators,
                stateNames,
                profile != null ? profile.GetTransitionSeconds(intent) : 0.08f,
                fixedTime);
        }

        public static float ResolveClipLength(Animator animator, string stateName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(stateName))
                return 0f;

            var controller = animator.runtimeAnimatorController;
            if (controller == null || controller.animationClips == null)
                return 0f;

            string terminalStateName = ResolveTerminalStateName(stateName);
            for (int i = 0; i < controller.animationClips.Length; i++)
            {
                var clip = controller.animationClips[i];
                if (clip == null)
                    continue;

                if (string.Equals(clip.name, stateName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(clip.name, terminalStateName, StringComparison.OrdinalIgnoreCase))
                    return clip.length;
            }

            return 0f;
        }

        static string ResolveProfileId(UnitAnimationBinding binding, UnitAnimationProfile profile)
        {
            if (binding != null && !string.IsNullOrWhiteSpace(binding.profileId))
                return binding.profileId.Trim();
            if (profile != null && !string.IsNullOrWhiteSpace(profile.profileId))
                return profile.profileId.Trim();
            return "default";
        }

        static string ResolveDebugSource(UnitAnimationBinding binding, UnitAnimationProfile profile)
        {
            if (profile != null)
                return $"profile:{profile.name}";
            if (binding != null && !string.IsNullOrWhiteSpace(binding.profileId))
                return $"binding:{binding.profileId.Trim()}";
            return "fallback";
        }

        static float ResolveAnimatorSpeed(UnitAnimationBinding binding, UnitAnimationProfile profile)
        {
            if (binding != null && binding.animatorSpeedMultiplier > 0.05f)
                return binding.animatorSpeedMultiplier;
            if (profile != null && profile.animatorSpeedMultiplier > 0.05f)
                return profile.animatorSpeedMultiplier;
            return 1f;
        }

        static bool ResolveApplyRootMotion(UnitAnimationBinding binding, UnitAnimationProfile profile)
        {
            if (binding != null && binding.applyRootMotion)
                return true;
            return profile != null && profile.applyRootMotion;
        }

        static string[] ResolveStates(UnitAnimationStateAliases aliases, string[] fallback)
        {
            return ExpandStateAliases(aliases != null && aliases.HasStates ? aliases.stateNames : fallback);
        }

        static string[] ResolveAttackStates(UnitAnimationProfile profile, UnitAnimationAttackFamily family)
        {
            return family switch
            {
                UnitAnimationAttackFamily.Melee => ResolveStates(profile != null ? profile.attackMelee : null, DefaultMeleeAttackStates),
                UnitAnimationAttackFamily.Ranged => ResolveStates(profile != null ? profile.attackRanged : null, DefaultRangedAttackStates),
                UnitAnimationAttackFamily.Magic => ResolveStates(profile != null ? profile.attackMagic : null, DefaultMagicAttackStates),
                UnitAnimationAttackFamily.Support => ResolveStates(profile != null ? profile.attackSupport : null, DefaultSupportAttackStates),
                UnitAnimationAttackFamily.Siege => ResolveStates(profile != null ? profile.attackSiege : null, DefaultSiegeAttackStates),
                _ => ResolveStates(profile != null ? profile.attackDefault : null, DefaultAttackStates),
            };
        }

        static UnitAnimationAttackFamily ResolveAttackFamily(
            MLUnit unit,
            UnitAnimationBinding binding,
            UnitAnimationProfile profile)
        {
            if (binding != null && binding.attackFamilyOverride != UnitAnimationAttackFamily.Unspecified)
                return binding.attackFamilyOverride;
            if (profile != null && profile.defaultAttackFamily != UnitAnimationAttackFamily.Unspecified)
                return profile.defaultAttackFamily;

            if (FortUnitIdentityCatalog.TryResolveBarracksDefinition(
                null,
                unit != null ? unit.archetypeKey : null,
                unit != null && !string.IsNullOrWhiteSpace(unit.catalogUnitKey) ? unit.catalogUnitKey : unit != null ? unit.type : null,
                unit != null ? unit.skinKey : null,
                out FortBarracksRosterDefinition definition))
            {
                return definition.barracksRole switch
                {
                    BarracksUnitRole.Ranged => LooksLikeMagicUnit(unit) ? UnitAnimationAttackFamily.Magic : UnitAnimationAttackFamily.Ranged,
                    BarracksUnitRole.Support => UnitAnimationAttackFamily.Support,
                    BarracksUnitRole.Siege => UnitAnimationAttackFamily.Siege,
                    _ => UnitAnimationAttackFamily.Melee,
                };
            }

            if (LooksLikeMagicUnit(unit))
                return UnitAnimationAttackFamily.Magic;
            if (LooksLikeRangedUnit(unit))
                return UnitAnimationAttackFamily.Ranged;

            return UnitAnimationAttackFamily.Default;
        }

        static string[] ExpandStateAliases(string[] stateNames)
        {
            if (stateNames == null || stateNames.Length == 0)
                return Array.Empty<string>();

            var expanded = new List<string>(stateNames.Length * 4);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < stateNames.Length; i++)
            {
                string stateName = stateNames[i];
                if (string.IsNullOrWhiteSpace(stateName))
                    continue;

                string trimmed = stateName.Trim();
                AppendStateCandidate(expanded, seen, trimmed);
                if (trimmed.Contains("."))
                    continue;

                for (int prefixIndex = 0; prefixIndex < CommonStatePathPrefixes.Length; prefixIndex++)
                    AppendStateCandidate(expanded, seen, CommonStatePathPrefixes[prefixIndex] + trimmed);
            }

            return expanded.ToArray();
        }

        static void AppendStateCandidate(List<string> expanded, HashSet<string> seen, string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName) || !seen.Add(stateName))
                return;

            expanded.Add(stateName);
        }

        static string ResolveTerminalStateName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
                return string.Empty;

            int lastDot = stateName.LastIndexOf('.');
            return lastDot >= 0 && lastDot < stateName.Length - 1
                ? stateName[(lastDot + 1)..]
                : stateName;
        }

        static bool IsDefending(MLUnit unit)
        {
            return EqualsIgnoreCase(unit != null ? unit.commandState : null, "defend");
        }

        static bool IsRetreating(MLUnit unit)
        {
            return EqualsIgnoreCase(unit != null ? unit.commandState : null, "retreat")
                || ContainsIgnoreCase(unit != null ? unit.movementMode : null, "retreat");
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
                && value.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool EqualsIgnoreCase(string value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
