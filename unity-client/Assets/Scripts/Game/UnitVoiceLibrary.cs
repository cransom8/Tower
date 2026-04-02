using System;
using System.Collections.Generic;
using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    public enum UnitVoiceCue
    {
        Attack,
        Defend,
        Retreat,
    }

    public static class UnitVoiceLibrary
    {
        const string ResourceRoot = "Generated/UnitVoices";

        static readonly Dictionary<string, AudioClip[]> ClipsByFolder = new(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> MissingFolders = new(StringComparer.OrdinalIgnoreCase);

        public static void ResetCache()
        {
            ClipsByFolder.Clear();
            MissingFolders.Clear();
        }

        public static bool HasVoiceProfile(MLUnit unit)
        {
            return TryResolveProfileKey(unit, out _);
        }

        public static bool TryPlay(MLUnit unit, UnitVoiceCue cue, float volumeScale = 1f)
        {
            if (AudioManager.I == null)
                return false;

            if (!TryResolveProfileKey(unit, out string profileKey))
                return false;

            string folder = BuildFolder(profileKey, cue);
            if (!TryGetClips(folder, out AudioClip[] clips))
                return false;

            AudioClip clip = clips[UnityEngine.Random.Range(0, clips.Length)];
            if (clip == null)
                return false;

            AudioManager.I.PlayClip(clip, volumeScale);
            return true;
        }

        public static bool HasGeneratedClips(MLUnit unit, UnitVoiceCue cue = UnitVoiceCue.Attack)
        {
            if (!TryResolveProfileKey(unit, out string profileKey))
                return false;

            return TryGetClips(BuildFolder(profileKey, cue), out _);
        }

        static bool TryGetClips(string folder, out AudioClip[] clips)
        {
            if (ClipsByFolder.TryGetValue(folder, out clips) && clips != null && clips.Length > 0)
                return true;

            clips = Resources.LoadAll<AudioClip>(folder) ?? Array.Empty<AudioClip>();
            ClipsByFolder[folder] = clips;
            if (clips.Length > 0)
            {
                MissingFolders.Remove(folder);
                return true;
            }

            if (MissingFolders.Add(folder))
            {
                Debug.LogWarning(
                    $"[UnitVoiceLibrary] No generated clips were found at Resources path '{folder}'. " +
                    "Run the unit voice generator and let Unity import the new WAV files.");
            }

            return false;
        }

        static string BuildFolder(string profileKey, UnitVoiceCue cue)
        {
            return $"{ResourceRoot}/{profileKey}/{ResolveCueFolder(cue)}";
        }

        static string ResolveCueFolder(UnitVoiceCue cue) => cue switch
        {
            UnitVoiceCue.Attack => "attack",
            UnitVoiceCue.Defend => "defend",
            UnitVoiceCue.Retreat => "retreat",
            _ => "attack",
        };

        static bool TryResolveProfileKey(MLUnit unit, out string profileKey)
        {
            profileKey = null;
            if (unit == null)
                return false;

            string archetypeKey = Normalize(unit.archetypeKey);
            if (!string.IsNullOrEmpty(archetypeKey))
            {
                if (archetypeKey.StartsWith("infantry_", StringComparison.Ordinal))
                {
                    profileKey = "infantry";
                    return true;
                }

                if (archetypeKey.StartsWith("polearm_", StringComparison.Ordinal))
                {
                    profileKey = "polearm";
                    return true;
                }

                if (archetypeKey.StartsWith("shield_", StringComparison.Ordinal))
                {
                    profileKey = "shield";
                    return true;
                }

                if (archetypeKey.StartsWith("ranged_", StringComparison.Ordinal))
                {
                    profileKey = "ranged";
                    return true;
                }

                if (archetypeKey.StartsWith("support_", StringComparison.Ordinal))
                {
                    profileKey = "support";
                    return true;
                }

                if (archetypeKey.StartsWith("arcane_", StringComparison.Ordinal))
                {
                    profileKey = "arcane";
                    return true;
                }

                if (archetypeKey.StartsWith("economy_", StringComparison.Ordinal))
                {
                    profileKey = "economy";
                    return true;
                }

                if (archetypeKey == "hero_king")
                {
                    profileKey = "hero_king";
                    return true;
                }

                if (archetypeKey == "hero_paladin")
                {
                    profileKey = "hero_paladin";
                    return true;
                }

                if (archetypeKey == "hero_bishop")
                {
                    profileKey = "hero_bishop";
                    return true;
                }
            }

            string unitKey = FirstNonEmpty(unit.catalogUnitKey, unit.skinKey, unit.unitTypeKey, unit.type);
            switch (Normalize(unitKey))
            {
                case "tt_settler":
                    profileKey = "economy";
                    return true;
                case "tt_archer":
                case "tt_crossbowman":
                case "tt_mounted_scout":
                    profileKey = "ranged";
                    return true;
                case "tt_spearman":
                case "tt_halberdier":
                case "tt_light_cavalry":
                    profileKey = "polearm";
                    return true;
                case "tt_heavy_infantry":
                case "tt_heavy_swordman":
                case "tt_heavy_cavalry":
                case "tt_mounted_paladin":
                    profileKey = "shield";
                    return true;
                case "tt_priest":
                case "tt_high_priest":
                case "tt_mounted_priest":
                    profileKey = "support";
                    return true;
                case "tt_mage":
                case "tt_mounted_mage":
                case "tt_mounted_king":
                    profileKey = "arcane";
                    return true;
                case "tt_king":
                    profileKey = "hero_king";
                    return true;
                case "tt_paladin":
                    profileKey = "hero_paladin";
                    return true;
                case "tt_commander":
                    profileKey = "hero_bishop";
                    return true;
                case "tt_peasant":
                case "tt_scout":
                case "tt_light_infantry":
                case "tt_mounted_knight":
                    profileKey = "infantry";
                    return true;
            }

            if (unit.isHero)
            {
                switch (Normalize(unit.heroKey))
                {
                    case "king":
                        profileKey = "hero_king";
                        return true;
                    case "paladin":
                        profileKey = "hero_paladin";
                        return true;
                    case "bishop":
                    case "commander":
                        profileKey = "hero_bishop";
                        return true;
                }
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
