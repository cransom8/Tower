using System;
using System.Collections.Generic;

namespace CastleDefender.Game
{
    public readonly struct FortUnitPresentationDefinition
    {
        public readonly string archetypeKey;
        public readonly string presentationKey;
        public readonly string catalogUnitKey;
        public readonly string skinKey;
        public readonly string portraitKey;
        public readonly string displayName;

        public FortUnitPresentationDefinition(
            string archetypeKey,
            string presentationKey,
            string catalogUnitKey,
            string skinKey,
            string portraitKey,
            string displayName)
        {
            this.archetypeKey = archetypeKey;
            this.presentationKey = presentationKey;
            this.catalogUnitKey = catalogUnitKey;
            this.skinKey = skinKey;
            this.portraitKey = portraitKey;
            this.displayName = displayName;
        }
    }

    public readonly struct FortBarracksRosterDefinition
    {
        public readonly string rosterKey;
        public readonly string archetypeKey;
        public readonly string presentationKey;
        public readonly string displayName;
        public readonly string sourceRoleLabel;
        public readonly BarracksUnitRole barracksRole;
        public readonly int sortIndex;

        public FortBarracksRosterDefinition(
            string rosterKey,
            string archetypeKey,
            string presentationKey,
            string displayName,
            string sourceRoleLabel,
            BarracksUnitRole barracksRole,
            int sortIndex)
        {
            this.rosterKey = rosterKey;
            this.archetypeKey = archetypeKey;
            this.presentationKey = presentationKey;
            this.displayName = displayName;
            this.sourceRoleLabel = sourceRoleLabel;
            this.barracksRole = barracksRole;
            this.sortIndex = sortIndex;
        }

        public string DefaultSpawnedUnitName => $"Barracks{displayName.Replace(" ", string.Empty)}";
    }

    public static class FortUnitIdentityCatalog
    {
        public const string DefaultPresentationKey = "human_default";

        static readonly FortUnitPresentationDefinition[] PresentationDefinitions =
        {
            new("infantry_t1", "human_default", "tt_peasant", "tt_peasant", "tt_peasant", "Militia"),
            new("infantry_t2", "human_default", "tt_light_infantry", "tt_light_infantry", "tt_light_infantry", "Swordsman"),
            new("infantry_t3", "human_default", "tt_mounted_knight", "tt_mounted_knight", "tt_mounted_knight", "Knight"),
            new("polearm_t1", "human_default", "tt_spearman", "tt_spearman", "tt_spearman", "Spearman"),
            new("polearm_t2", "human_default", "tt_halberdier", "tt_halberdier", "tt_halberdier", "Halberdier"),
            new("polearm_t3", "human_default", "tt_light_cavalry", "tt_light_cavalry", "tt_light_cavalry", "Lancer"),
            new("shield_t1", "human_default", "tt_heavy_infantry", "tt_heavy_infantry", "tt_heavy_infantry", "Shieldman"),
            new("shield_t2", "human_default", "tt_heavy_swordman", "tt_heavy_swordman", "tt_heavy_swordman", "Shield Guard"),
            new("shield_t3", "human_default", "tt_heavy_cavalry", "tt_heavy_cavalry", "tt_heavy_cavalry", "Guardian"),
            new("support_t1", "human_default", "tt_mounted_priest", "tt_mounted_priest", "tt_mounted_priest", "Cleric"),
            new("support_t2", "human_default", "tt_priest", "tt_priest", "tt_priest", "Priest"),
            new("support_t3", "human_default", "tt_high_priest", "tt_high_priest", "tt_high_priest", "High Priest"),
            new("arcane_t1", "human_default", "tt_mage", "tt_mage", "tt_mage", "Mage"),
            new("arcane_t2", "human_default", "tt_mounted_mage", "tt_mounted_mage", "tt_mounted_mage", "Wizard"),
            new("arcane_t3", "human_default", "tt_mounted_king", "tt_mounted_king", "tt_mounted_king", "Thaumaturge"),
            new("ranged_t1", "human_default", "tt_archer", "tt_archer", "tt_archer", "Archer"),
            new("ranged_t2", "human_default", "tt_crossbowman", "tt_crossbowman", "tt_crossbowman", "Crossbowman"),
            new("ranged_t3", "human_default", "tt_mounted_scout", "tt_mounted_scout", "tt_mounted_scout", "Ranger"),
            new("economy_t1", "human_default", "tt_peasant", "tt_peasant", "tt_peasant", "Peasant"),
            new("economy_t2", "human_default", "tt_settler", "tt_settler", "tt_settler", "Settler"),
            new("economy_t3", "human_default", "tt_settler", "tt_settler", "tt_settler", "Trader"),
            new("hero_king", "human_default", "tt_king", "tt_king", "tt_king", "King"),
            new("hero_paladin", "human_default", "tt_paladin", "tt_paladin", "tt_paladin", "Paladin"),
            new("hero_bishop", "human_default", "tt_commander", "tt_commander", "tt_commander", "Bishop"),
        };

        static readonly FortBarracksRosterDefinition[] BarracksDefinitions =
        {
            new("militia", "infantry_t1", "human_default", "Militia", "Melee", BarracksUnitRole.Frontline, 10),
            new("spearman", "polearm_t1", "human_default", "Spearman", "Melee w/ extended reach", BarracksUnitRole.Frontline, 20),
            new("shieldman", "shield_t1", "human_default", "Shieldman", "Melee", BarracksUnitRole.Frontline, 30),
            new("swordsman", "infantry_t2", "human_default", "Swordsman", "Melee", BarracksUnitRole.Frontline, 40),
            new("halberdier", "polearm_t2", "human_default", "Halberdier", "Melee w/ extended reach", BarracksUnitRole.Frontline, 50),
            new("shield_guard", "shield_t2", "human_default", "Shield Guard", "Melee", BarracksUnitRole.Frontline, 60),
            new("knight", "infantry_t3", "human_default", "Knight", "Tank", BarracksUnitRole.Frontline, 70),
            new("lancer", "polearm_t3", "human_default", "Lancer", "Melee", BarracksUnitRole.Frontline, 80),
            new("guardian", "shield_t3", "human_default", "Guardian", "Tank", BarracksUnitRole.Frontline, 90),
            new("cleric", "support_t1", "human_default", "Cleric", "Ranged", BarracksUnitRole.Support, 110),
            new("priest", "support_t2", "human_default", "Priest", "Ranged", BarracksUnitRole.Support, 120),
            new("high_priest", "support_t3", "human_default", "High Priest", "Ranged", BarracksUnitRole.Support, 130),
            new("mage", "arcane_t1", "human_default", "Mage", "Ranged", BarracksUnitRole.Ranged, 140),
            new("wizard", "arcane_t2", "human_default", "Wizard", "Ranged", BarracksUnitRole.Ranged, 150),
            new("thaumaturge", "arcane_t3", "human_default", "Thaumaturge", "Siege", BarracksUnitRole.Siege, 160),
            new("archer", "ranged_t1", "human_default", "Archer", "Ranged", BarracksUnitRole.Ranged, 170),
            new("crossbowman", "ranged_t2", "human_default", "Crossbowman", "Ranged", BarracksUnitRole.Ranged, 180),
            new("ranger", "ranged_t3", "human_default", "Ranger", "Ranged", BarracksUnitRole.Ranged, 190),
        };

        static readonly Dictionary<string, FortUnitPresentationDefinition> PresentationByArchetype =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortUnitPresentationDefinition> PresentationByIdentityKey =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortUnitPresentationDefinition> PresentationByCatalogUnitKey =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortUnitPresentationDefinition> PresentationBySkinKey =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortBarracksRosterDefinition> BarracksByRosterKey =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortBarracksRosterDefinition> BarracksByArchetypeKey =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortBarracksRosterDefinition> BarracksByCatalogUnitKey =
            new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, FortBarracksRosterDefinition> BarracksBySkinKey =
            new(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<FortBarracksRosterDefinition> AllBarracksDefinitions => BarracksDefinitions;

        static FortUnitIdentityCatalog()
        {
            for (int i = 0; i < PresentationDefinitions.Length; i++)
            {
                FortUnitPresentationDefinition definition = PresentationDefinitions[i];
                PresentationByIdentityKey[$"{definition.presentationKey}:{definition.archetypeKey}"] = definition;
                PresentationByArchetype[definition.archetypeKey] = definition;
                if (!string.IsNullOrWhiteSpace(definition.catalogUnitKey))
                    PresentationByCatalogUnitKey[definition.catalogUnitKey] = definition;
                if (!string.IsNullOrWhiteSpace(definition.skinKey))
                    PresentationBySkinKey[definition.skinKey] = definition;
            }

            for (int i = 0; i < BarracksDefinitions.Length; i++)
            {
                FortBarracksRosterDefinition definition = BarracksDefinitions[i];
                BarracksByRosterKey[definition.rosterKey] = definition;
                BarracksByArchetypeKey[definition.archetypeKey] = definition;
                if (TryResolvePresentation(definition.archetypeKey, definition.presentationKey, out FortUnitPresentationDefinition presentation))
                {
                    if (!string.IsNullOrWhiteSpace(presentation.catalogUnitKey))
                        BarracksByCatalogUnitKey[presentation.catalogUnitKey] = definition;
                    if (!string.IsNullOrWhiteSpace(presentation.skinKey))
                        BarracksBySkinKey[presentation.skinKey] = definition;
                }
            }
        }

        public static bool IsFortArchetypeKey(string archetypeKey)
        {
            return !string.IsNullOrWhiteSpace(archetypeKey)
                && PresentationByArchetype.ContainsKey(archetypeKey.Trim());
        }

        public static bool TryResolvePresentation(string archetypeKey, string presentationKey, out FortUnitPresentationDefinition definition)
        {
            definition = default;
            if (string.IsNullOrWhiteSpace(archetypeKey))
                return false;

            string resolvedPresentationKey = string.IsNullOrWhiteSpace(presentationKey)
                ? DefaultPresentationKey
                : presentationKey.Trim();
            if (PresentationByIdentityKey.TryGetValue($"{resolvedPresentationKey}:{archetypeKey.Trim()}", out definition))
                return true;

            if (PresentationByArchetype.TryGetValue(archetypeKey.Trim(), out definition))
                return true;

            return false;
        }

        public static bool TryResolvePresentationFromAnyKey(
            string archetypeKey,
            string unitTypeKey,
            string skinKey,
            out FortUnitPresentationDefinition definition)
        {
            definition = default;
            if (TryResolvePresentation(archetypeKey, DefaultPresentationKey, out definition))
                return true;

            if (!string.IsNullOrWhiteSpace(unitTypeKey) && PresentationByCatalogUnitKey.TryGetValue(unitTypeKey.Trim(), out definition))
                return true;

            if (!string.IsNullOrWhiteSpace(skinKey) && PresentationBySkinKey.TryGetValue(skinKey.Trim(), out definition))
                return true;

            return false;
        }

        public static bool TryResolveBarracksDefinition(
            string rosterKey,
            string archetypeKey,
            string unitTypeKey,
            string skinKey,
            out FortBarracksRosterDefinition definition)
        {
            definition = default;
            if (!string.IsNullOrWhiteSpace(rosterKey) && BarracksByRosterKey.TryGetValue(rosterKey.Trim(), out definition))
                return true;

            if (!string.IsNullOrWhiteSpace(archetypeKey) && BarracksByArchetypeKey.TryGetValue(archetypeKey.Trim(), out definition))
                return true;

            if (!string.IsNullOrWhiteSpace(unitTypeKey) && BarracksByCatalogUnitKey.TryGetValue(unitTypeKey.Trim(), out definition))
                return true;

            if (!string.IsNullOrWhiteSpace(skinKey) && BarracksBySkinKey.TryGetValue(skinKey.Trim(), out definition))
                return true;

            return false;
        }

        public static string ResolveCatalogUnitKey(string archetypeKey, string presentationKey, string unitTypeKey, string skinKey)
        {
            if (TryResolvePresentation(archetypeKey, presentationKey, out FortUnitPresentationDefinition presentation)
                && !string.IsNullOrWhiteSpace(presentation.catalogUnitKey))
            {
                return presentation.catalogUnitKey;
            }

            return ResolveCatalogUnitKey(archetypeKey, unitTypeKey, skinKey);
        }

        public static string ResolveCatalogUnitKey(string archetypeKey, string unitTypeKey = null, string skinKey = null)
        {
            if (TryResolvePresentationFromAnyKey(archetypeKey, unitTypeKey, skinKey, out FortUnitPresentationDefinition definition)
                && !string.IsNullOrWhiteSpace(definition.catalogUnitKey))
                return definition.catalogUnitKey;

            return !string.IsNullOrWhiteSpace(unitTypeKey) ? unitTypeKey.Trim() : null;
        }

        public static string ResolveSkinKey(string archetypeKey, string presentationKey, string unitTypeKey, string skinKey)
        {
            if (TryResolvePresentation(archetypeKey, presentationKey, out FortUnitPresentationDefinition presentation)
                && !string.IsNullOrWhiteSpace(presentation.skinKey))
            {
                return presentation.skinKey;
            }

            return ResolveSkinKey(archetypeKey, unitTypeKey, skinKey);
        }

        public static string ResolveSkinKey(string archetypeKey, string unitTypeKey = null, string skinKey = null)
        {
            if (TryResolvePresentationFromAnyKey(archetypeKey, unitTypeKey, skinKey, out FortUnitPresentationDefinition definition)
                && !string.IsNullOrWhiteSpace(definition.skinKey))
                return definition.skinKey;

            return !string.IsNullOrWhiteSpace(skinKey) ? skinKey.Trim() : null;
        }

        public static string ResolvePortraitKey(string archetypeKey, string presentationKey, string unitTypeKey, string skinKey)
        {
            if (TryResolvePresentation(archetypeKey, presentationKey, out FortUnitPresentationDefinition presentation))
            {
                if (!string.IsNullOrWhiteSpace(presentation.portraitKey))
                    return presentation.portraitKey;
                if (!string.IsNullOrWhiteSpace(presentation.skinKey))
                    return presentation.skinKey;
                if (!string.IsNullOrWhiteSpace(presentation.catalogUnitKey))
                    return presentation.catalogUnitKey;
            }

            return ResolvePortraitKey(archetypeKey, unitTypeKey, skinKey);
        }

        public static string ResolvePortraitKey(string archetypeKey, string unitTypeKey = null, string skinKey = null)
        {
            if (TryResolvePresentationFromAnyKey(archetypeKey, unitTypeKey, skinKey, out FortUnitPresentationDefinition definition))
            {
                if (!string.IsNullOrWhiteSpace(definition.portraitKey))
                    return definition.portraitKey;
                if (!string.IsNullOrWhiteSpace(definition.skinKey))
                    return definition.skinKey;
                if (!string.IsNullOrWhiteSpace(definition.catalogUnitKey))
                    return definition.catalogUnitKey;
            }

            if (!string.IsNullOrWhiteSpace(skinKey))
                return skinKey.Trim();
            if (!string.IsNullOrWhiteSpace(unitTypeKey))
                return unitTypeKey.Trim();
            return null;
        }

        public static string ResolveDisplayName(string archetypeKey, string presentationKey, string unitTypeKey, string skinKey, string fallback = null)
        {
            if (TryResolvePresentation(archetypeKey, presentationKey, out FortUnitPresentationDefinition presentation)
                && !string.IsNullOrWhiteSpace(presentation.displayName))
            {
                return presentation.displayName;
            }

            return ResolveDisplayName(archetypeKey, unitTypeKey, skinKey, fallback);
        }

        public static string ResolveDisplayName(string archetypeKey, string unitTypeKey = null, string skinKey = null, string fallback = null)
        {
            if (TryResolvePresentationFromAnyKey(archetypeKey, unitTypeKey, skinKey, out FortUnitPresentationDefinition definition)
                && !string.IsNullOrWhiteSpace(definition.displayName))
                return definition.displayName;

            return fallback;
        }
    }
}
