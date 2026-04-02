using System;
using System.Collections.Generic;
using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    public enum UnitCombatSfxPlaybackResult
    {
        Played = 0,
        MissingAudioManager = 1,
        MissingProfile = 2,
        MissingClips = 3,
        Disabled = 4,
        SkippedChance = 5,
        SkippedCooldown = 6,
    }

    public static class UnitCombatSfxLibrary
    {
        public sealed class ResolvedProfile
        {
            public string ProfileKey { get; set; }
            public bool EnableImpactCue { get; set; }
            public bool EnableDefendCue { get; set; }
            public float SpawnChance { get; set; }
            public float AttackChance { get; set; }
            public float ImpactChance { get; set; }
            public float DefendChance { get; set; }
            public float HurtChance { get; set; }
            public float DeathChance { get; set; }

            public bool AllowsCue(UnitCombatSfxCue cue) => cue switch
            {
                UnitCombatSfxCue.Impact => EnableImpactCue,
                UnitCombatSfxCue.Defend => EnableDefendCue,
                _ => true,
            };

            public float ResolveChance(UnitCombatSfxCue cue) => cue switch
            {
                UnitCombatSfxCue.Spawn => SpawnChance,
                UnitCombatSfxCue.Attack => AttackChance,
                UnitCombatSfxCue.Impact => ImpactChance,
                UnitCombatSfxCue.Defend => DefendChance,
                UnitCombatSfxCue.Hurt => HurtChance,
                UnitCombatSfxCue.Death => DeathChance,
                _ => 0f,
            };
        }

        const string ResourceRoot = "Generated/UnitCombatSfx";
        const float AnyGlobalCooldownSeconds = 0.10f;
        const float SpawnCueGlobalCooldownSeconds = 0.18f;
        const float AttackCueGlobalCooldownSeconds = 0.08f;
        const float ImpactCueGlobalCooldownSeconds = 0.08f;
        const float DefendCueGlobalCooldownSeconds = 0.20f;
        const float HurtCueGlobalCooldownSeconds = 0.14f;
        const float DeathCueGlobalCooldownSeconds = 0.24f;
        const float SpawnEmitterCooldownSeconds = 0.90f;
        const float AttackEmitterCooldownSeconds = 0.48f;
        const float ImpactEmitterCooldownSeconds = 0.22f;
        const float DefendEmitterCooldownSeconds = 0.75f;
        const float HurtEmitterCooldownSeconds = 0.55f;
        const float DeathEmitterCooldownSeconds = 1.60f;
        const float DefaultSpawnChance = 0.28f;
        const float DefaultAttackChance = 0.18f;
        const float DefaultImpactChance = 0.16f;
        const float DefaultDefendChance = 0.14f;
        const float DefaultHurtChance = 0.14f;
        const float DefaultDeathChance = 0.82f;

        static readonly Dictionary<string, AudioClip[]> ClipsByFolder = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> MissingProfileCueKeys = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, float> LastEmitterCueAt = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, float> LastProfileCueAt = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<UnitCombatSfxCue, float> LastCueAt = new();
        static float _lastAnyPlayedAt = float.MinValue;

        public static void ResetCache()
        {
            ClipsByFolder.Clear();
            MissingProfileCueKeys.Clear();
        }

        public static void ResetPlaybackState()
        {
            LastEmitterCueAt.Clear();
            LastProfileCueAt.Clear();
            LastCueAt.Clear();
            _lastAnyPlayedAt = float.MinValue;
        }

        public static ResolvedProfile ResolveForUnit(GameObject root, MLUnit unit)
        {
            var binding = root != null ? root.GetComponentInChildren<UnitCombatSfxBinding>(true) : null;
            if (binding != null && !string.IsNullOrWhiteSpace(binding.profileKey))
            {
                return new ResolvedProfile
                {
                    ProfileKey = Normalize(binding.profileKey),
                    EnableImpactCue = binding.enableImpactCue,
                    EnableDefendCue = binding.enableDefendCue,
                    SpawnChance = Mathf.Clamp01(binding.spawnChance),
                    AttackChance = Mathf.Clamp01(binding.attackChance),
                    ImpactChance = Mathf.Clamp01(binding.impactChance),
                    DefendChance = Mathf.Clamp01(binding.defendChance),
                    HurtChance = Mathf.Clamp01(binding.hurtChance),
                    DeathChance = Mathf.Clamp01(binding.deathChance),
                };
            }

            if (!TryResolveDefaultProfile(unit, out string profileKey, out bool enableImpactCue, out bool enableDefendCue))
                return null;

            return CreateDefaultResolvedProfile(profileKey, enableImpactCue, enableDefendCue);
        }

        public static ResolvedProfile ResolveForProjectile(string projectileType, string damageType)
        {
            if (!TryResolveProjectileProfile(projectileType, damageType, out string profileKey, out bool enableImpactCue))
                return null;

            return CreateDefaultResolvedProfile(profileKey, enableImpactCue, enableDefendCue: false);
        }

        public static bool HasGeneratedClips(MLUnit unit, GameObject root, UnitCombatSfxCue cue = UnitCombatSfxCue.Attack)
        {
            return HasGeneratedClips(ResolveForUnit(root, unit), cue);
        }

        public static bool HasGeneratedClips(ResolvedProfile profile, UnitCombatSfxCue cue = UnitCombatSfxCue.Attack)
        {
            return TryGetClips(profile, cue, out _);
        }

        public static UnitCombatSfxPlaybackResult TryPlay(
            ResolvedProfile profile,
            string emitterId,
            UnitCombatSfxCue cue,
            float now,
            float volumeScale = 1f,
            bool bypassChance = false)
        {
            if (AudioManager.I == null)
                return UnitCombatSfxPlaybackResult.MissingAudioManager;

            if (profile == null || string.IsNullOrWhiteSpace(profile.ProfileKey))
                return UnitCombatSfxPlaybackResult.MissingProfile;

            if (!profile.AllowsCue(cue))
                return UnitCombatSfxPlaybackResult.Disabled;

            float chance = Mathf.Clamp01(profile.ResolveChance(cue));
            if (chance <= 0f)
                return UnitCombatSfxPlaybackResult.Disabled;

            if (!TryGetClips(profile, cue, out AudioClip[] clips) || clips == null || clips.Length == 0)
            {
                LogMissingProfileCue(profile.ProfileKey, cue);
                return UnitCombatSfxPlaybackResult.MissingClips;
            }

            string normalizedEmitter = Normalize(emitterId) ?? Normalize(profile.ProfileKey);
            string emitterCueKey = $"{normalizedEmitter}:{cue}";
            string profileCueKey = $"{Normalize(profile.ProfileKey)}:{cue}";

            if (LastEmitterCueAt.TryGetValue(emitterCueKey, out float lastEmitterCueAt)
                && now - lastEmitterCueAt < ResolveEmitterCooldown(cue))
            {
                return UnitCombatSfxPlaybackResult.SkippedCooldown;
            }

            if (LastProfileCueAt.TryGetValue(profileCueKey, out float lastProfileCueAt)
                && now - lastProfileCueAt < ResolveCueCooldown(cue))
            {
                return UnitCombatSfxPlaybackResult.SkippedCooldown;
            }

            if (LastCueAt.TryGetValue(cue, out float lastCueAt)
                && now - lastCueAt < ResolveCueCooldown(cue))
            {
                return UnitCombatSfxPlaybackResult.SkippedCooldown;
            }

            if (now - _lastAnyPlayedAt < AnyGlobalCooldownSeconds)
                return UnitCombatSfxPlaybackResult.SkippedCooldown;

            if (!bypassChance && UnityEngine.Random.value > chance)
                return UnitCombatSfxPlaybackResult.SkippedChance;

            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null)
                return UnitCombatSfxPlaybackResult.MissingClips;

            AudioManager.I.PlayClip(clip, Mathf.Max(0f, volumeScale));
            LastEmitterCueAt[emitterCueKey] = now;
            LastProfileCueAt[profileCueKey] = now;
            LastCueAt[cue] = now;
            _lastAnyPlayedAt = now;
            return UnitCombatSfxPlaybackResult.Played;
        }

        static ResolvedProfile CreateDefaultResolvedProfile(string profileKey, bool enableImpactCue, bool enableDefendCue)
        {
            return new ResolvedProfile
            {
                ProfileKey = Normalize(profileKey),
                EnableImpactCue = enableImpactCue,
                EnableDefendCue = enableDefendCue,
                SpawnChance = DefaultSpawnChance,
                AttackChance = DefaultAttackChance,
                ImpactChance = DefaultImpactChance,
                DefendChance = DefaultDefendChance,
                HurtChance = DefaultHurtChance,
                DeathChance = DefaultDeathChance,
            };
        }

        static bool TryGetClips(ResolvedProfile profile, UnitCombatSfxCue cue, out AudioClip[] clips)
        {
            clips = null;
            if (profile == null || string.IsNullOrWhiteSpace(profile.ProfileKey))
                return false;

            string[] candidates = BuildFolderCandidates(profile.ProfileKey, cue);
            for (int i = 0; i < candidates.Length; i++)
            {
                string folder = candidates[i];
                if (string.IsNullOrWhiteSpace(folder))
                    continue;

                if (!TryGetClipsForFolder(folder, out clips) || clips == null || clips.Length == 0)
                    continue;

                return true;
            }

            clips = Array.Empty<AudioClip>();
            return false;
        }

        static bool TryGetClipsForFolder(string folder, out AudioClip[] clips)
        {
            if (ClipsByFolder.TryGetValue(folder, out clips) && clips != null && clips.Length > 0)
                return true;

            clips = Resources.LoadAll<AudioClip>(folder) ?? Array.Empty<AudioClip>();
            ClipsByFolder[folder] = clips;
            return clips.Length > 0;
        }

        static string[] BuildFolderCandidates(string profileKey, UnitCombatSfxCue cue)
        {
            string cueFolder = ResolveCueFolder(cue);
            string normalizedProfile = Normalize(profileKey);
            switch (cue)
            {
                case UnitCombatSfxCue.Spawn:
                case UnitCombatSfxCue.Hurt:
                case UnitCombatSfxCue.Death:
                    return new[]
                    {
                        BuildFolder(normalizedProfile, cueFolder),
                        BuildFolder("common", cueFolder),
                    };
                default:
                    return new[] { BuildFolder(normalizedProfile, cueFolder) };
            }
        }

        static string BuildFolder(string profileKey, string cueFolder)
        {
            return string.IsNullOrWhiteSpace(profileKey) || string.IsNullOrWhiteSpace(cueFolder)
                ? null
                : $"{ResourceRoot}/{profileKey}/{cueFolder}";
        }

        static string ResolveCueFolder(UnitCombatSfxCue cue) => cue switch
        {
            UnitCombatSfxCue.Spawn => "spawn",
            UnitCombatSfxCue.Attack => "attack",
            UnitCombatSfxCue.Impact => "impact",
            UnitCombatSfxCue.Defend => "defend",
            UnitCombatSfxCue.Hurt => "hurt",
            UnitCombatSfxCue.Death => "death",
            _ => "attack",
        };

        static float ResolveCueCooldown(UnitCombatSfxCue cue) => cue switch
        {
            UnitCombatSfxCue.Spawn => SpawnCueGlobalCooldownSeconds,
            UnitCombatSfxCue.Attack => AttackCueGlobalCooldownSeconds,
            UnitCombatSfxCue.Impact => ImpactCueGlobalCooldownSeconds,
            UnitCombatSfxCue.Defend => DefendCueGlobalCooldownSeconds,
            UnitCombatSfxCue.Hurt => HurtCueGlobalCooldownSeconds,
            UnitCombatSfxCue.Death => DeathCueGlobalCooldownSeconds,
            _ => AttackCueGlobalCooldownSeconds,
        };

        static float ResolveEmitterCooldown(UnitCombatSfxCue cue) => cue switch
        {
            UnitCombatSfxCue.Spawn => SpawnEmitterCooldownSeconds,
            UnitCombatSfxCue.Attack => AttackEmitterCooldownSeconds,
            UnitCombatSfxCue.Impact => ImpactEmitterCooldownSeconds,
            UnitCombatSfxCue.Defend => DefendEmitterCooldownSeconds,
            UnitCombatSfxCue.Hurt => HurtEmitterCooldownSeconds,
            UnitCombatSfxCue.Death => DeathEmitterCooldownSeconds,
            _ => AttackEmitterCooldownSeconds,
        };

        static void LogMissingProfileCue(string profileKey, UnitCombatSfxCue cue)
        {
            string key = $"{Normalize(profileKey)}:{cue}";
            if (!MissingProfileCueKeys.Add(key))
                return;

            Debug.LogWarning(
                $"[UnitCombatSfxLibrary] No generated clips were found for profile '{profileKey}' cue '{cue}'. " +
                "Generate the TT combat SFX set and let Unity import the new audio files.");
        }

        static bool TryResolveDefaultProfile(MLUnit unit, out string profileKey, out bool enableImpactCue, out bool enableDefendCue)
        {
            profileKey = null;
            enableImpactCue = false;
            enableDefendCue = false;

            if (unit == null)
                return false;

            string normalizedArchetype = Normalize(unit.archetypeKey);
            if (!string.IsNullOrEmpty(normalizedArchetype))
            {
                switch (normalizedArchetype)
                {
                    case "infantry_t1":
                    case "infantry_t2":
                        profileKey = "light_melee";
                        return true;
                    case "infantry_t3":
                    case "hero_king":
                        profileKey = "heavy_melee";
                        return true;
                    case "polearm_t1":
                    case "polearm_t2":
                    case "polearm_t3":
                        profileKey = "polearm";
                        return true;
                    case "shield_t1":
                    case "shield_t2":
                    case "shield_t3":
                        profileKey = "heavy_melee";
                        enableDefendCue = true;
                        return true;
                    case "ranged_t1":
                    case "ranged_t3":
                        profileKey = "bow";
                        enableImpactCue = true;
                        return true;
                    case "ranged_t2":
                        profileKey = "crossbow";
                        enableImpactCue = true;
                        return true;
                    case "support_t1":
                    case "support_t2":
                    case "support_t3":
                    case "hero_bishop":
                        profileKey = "support";
                        return true;
                    case "arcane_t1":
                    case "arcane_t2":
                    case "arcane_t3":
                        profileKey = "arcane";
                        enableImpactCue = true;
                        return true;
                    case "hero_paladin":
                        profileKey = "heavy_melee";
                        enableDefendCue = true;
                        return true;
                }
            }

            string unitKey = Normalize(FirstNonEmpty(unit.catalogUnitKey, unit.skinKey, unit.unitTypeKey, unit.type));
            switch (unitKey)
            {
                case "tt_peasant":
                case "tt_light_infantry":
                    profileKey = "light_melee";
                    return true;
                case "tt_mounted_knight":
                case "tt_king":
                    profileKey = "heavy_melee";
                    return true;
                case "tt_heavy_infantry":
                case "tt_heavy_swordman":
                case "tt_heavy_cavalry":
                case "tt_paladin":
                case "tt_mounted_paladin":
                    profileKey = "heavy_melee";
                    enableDefendCue = true;
                    return true;
                case "tt_spearman":
                case "tt_halberdier":
                case "tt_light_cavalry":
                    profileKey = "polearm";
                    return true;
                case "tt_archer":
                case "tt_mounted_scout":
                case "tt_scout":
                    profileKey = "bow";
                    enableImpactCue = true;
                    return true;
                case "tt_crossbowman":
                    profileKey = "crossbow";
                    enableImpactCue = true;
                    return true;
                case "tt_mage":
                case "tt_mounted_mage":
                case "tt_mounted_king":
                    profileKey = "arcane";
                    enableImpactCue = true;
                    return true;
                case "tt_mounted_priest":
                case "tt_priest":
                case "tt_high_priest":
                case "tt_commander":
                    profileKey = "support";
                    return true;
            }

            if (unit.isHero)
            {
                switch (Normalize(unit.heroKey))
                {
                    case "king":
                        profileKey = "heavy_melee";
                        return true;
                    case "paladin":
                        profileKey = "heavy_melee";
                        enableDefendCue = true;
                        return true;
                    case "bishop":
                    case "commander":
                        profileKey = "support";
                        return true;
                }
            }

            return false;
        }

        static bool TryResolveProjectileProfile(string projectileType, string damageType, out string profileKey, out bool enableImpactCue)
        {
            profileKey = null;
            enableImpactCue = true;

            string token = Normalize(projectileType);
            string damage = Normalize(damageType);
            if (string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(damage))
                return false;

            if (!string.IsNullOrWhiteSpace(token) && token.Contains("crossbow"))
            {
                profileKey = "crossbow";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(token)
                && (token.Contains("archer") || token.Contains("scout") || token.Contains("ranger")))
            {
                profileKey = "bow";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(token)
                && (token.Contains("mage") || token.Contains("wizard") || token.Contains("arcane") || token.Contains("thaum")))
            {
                profileKey = "arcane";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(token)
                && (token.Contains("priest") || token.Contains("cleric") || token.Contains("bishop") || token.Contains("support")))
            {
                profileKey = "support";
                enableImpactCue = false;
                return true;
            }

            if (damage == "magic")
            {
                profileKey = "arcane";
                return true;
            }

            if (damage == "pierce")
            {
                profileKey = "bow";
                return true;
            }

            return false;
        }

        static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
                return null;

            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return null;
        }

        static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().ToLowerInvariant();
        }
    }
}
