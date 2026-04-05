using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    [System.Serializable]
    public struct BarracksSpawnCombatProfile
    {
        public string resolvedUnitTypeKey;
        public float maxHp;
        public float damagePerHit;
        public float attackIntervalSeconds;
        public float moveSpeed;
        public float attackRange;
        public float engagementRange;

        public bool IsConfigured =>
            maxHp > 0f
            || damagePerHit > 0f
            || attackIntervalSeconds > 0f
            || moveSpeed > 0f
            || attackRange > 0f
            || engagementRange > 0f;
    }

    public static class BarracksSpawnCombatProfileResolver
    {
        const float RangeToGameFeet = 30f;
        const float MinimumAttackRange = 4f;
        const float MaximumAttackRange = 16f;
        const float MinimumEngagementRange = 28f;
        const float EngagementPadding = 8f;
        const float DefaultMaxHp = 35f;
        const float DefaultDamagePerHit = 8f;
        const float DefaultAttackIntervalSeconds = 0.9f;
        const float DefaultMoveSpeed = 1.1f;
        const float DefaultBaseCombatPathSpeed = 0.25f;
        const float DefaultBarracksLevelOneSpeedMultiplier = 0.5f;
        const float DefaultSpeedUpgradeStep = 0.25f;
        const float DefaultServerPathSpeedToUnityMoveSpeedScale = 20f;
        const float DefaultServerCombatGridWidth = 11f;
        const float MinimumAttackIntervalSeconds = 0.25f;
        const float MaximumAttackIntervalSeconds = 2.25f;

        public static BarracksSpawnCombatProfile Resolve(string unitTypeKey, string skinKey = null, float serverPathSpeed = 0f)
        {
            string resolvedUnitTypeKey = ResolveUnitTypeKey(unitTypeKey, skinKey);
            if (!string.IsNullOrWhiteSpace(resolvedUnitTypeKey)
                && CatalogLoader.UnitByKey.TryGetValue(resolvedUnitTypeKey, out var catalogEntry))
            {
                return BuildFromCatalog(resolvedUnitTypeKey, catalogEntry, skinKey, serverPathSpeed);
            }

            return BuildFallback(resolvedUnitTypeKey, skinKey, serverPathSpeed);
        }

        public static float ResolveBaseCombatPathSpeed(MLMovementTuning movementTuning = null)
        {
            float configured = movementTuning != null ? movementTuning.baseCombatPathSpeed : 0f;
            return configured > 0f ? configured : DefaultBaseCombatPathSpeed;
        }

        public static float ResolveBarracksLevelSpeedMultiplier(int level, MLMovementTuning movementTuning = null)
        {
            float levelOneMult = movementTuning != null && movementTuning.barracksLevelOneSpeedMultiplier > 0f
                ? movementTuning.barracksLevelOneSpeedMultiplier
                : DefaultBarracksLevelOneSpeedMultiplier;
            float upgradeStep = movementTuning != null && movementTuning.barracksSpeedUpgradeStep > 0f
                ? movementTuning.barracksSpeedUpgradeStep
                : DefaultSpeedUpgradeStep;
            int clampedLevel = Mathf.Max(1, level);
            return Mathf.Max(0.01f, levelOneMult + ((clampedLevel - 1) * upgradeStep));
        }

        public static float ResolveBarracksServerPathSpeed(int level, MLMovementTuning movementTuning = null)
        {
            return ResolveBaseCombatPathSpeed(movementTuning) * ResolveBarracksLevelSpeedMultiplier(level, movementTuning);
        }

        public static float ResolveUpcomingWaveServerPathSpeed(MLUpcomingWaveEntry entry, MLMovementTuning movementTuning = null, float baseUnitPathSpeed = 0f)
        {
            float effectiveSpeedMult = entry != null ? Mathf.Max(0.01f, entry.speedMult) : 1f;
            float resolvedBasePathSpeed = baseUnitPathSpeed > 0f
                ? baseUnitPathSpeed
                : ResolveBaseCombatPathSpeed(movementTuning);
            return resolvedBasePathSpeed * effectiveSpeedMult;
        }

        public static float ConvertServerPathSpeedToUnityMoveSpeed(float serverPathSpeed, MLMovementTuning movementTuning = null)
        {
            if (serverPathSpeed <= 0f)
                return 0f;

            float configuredScale = movementTuning != null ? movementTuning.serverPathSpeedToUnityMoveSpeedScale : 0f;
            float speedScale = configuredScale > 0f
                ? configuredScale
                : DefaultServerPathSpeedToUnityMoveSpeedScale;
            return Mathf.Max(0.1f, serverPathSpeed * speedScale);
        }

        public static float ConvertServerCombatRangeToUnityAttackRange(float serverCombatRange)
        {
            if (serverCombatRange <= 0f)
                return 0f;

            float configuredGridWidth = SnapshotApplier.Instance?.LatestMLMatchConfig != null
                ? SnapshotApplier.Instance.LatestMLMatchConfig.gridW
                : 0f;
            float gridWidth = configuredGridWidth > 0f ? configuredGridWidth : DefaultServerCombatGridWidth;
            float normalizedRange = serverCombatRange / Mathf.Max(1f, gridWidth);
            return Mathf.Clamp(normalizedRange * RangeToGameFeet, MinimumAttackRange, MaximumAttackRange);
        }

        public static float ResolveEngagementRangeFromAttackRange(float attackRange)
        {
            return Mathf.Max(attackRange + EngagementPadding, MinimumEngagementRange);
        }

        public static void ApplyAuthoritativeSnapshot(
            ref BarracksSpawnCombatProfile profile,
            float serverPathSpeed = 0f,
            float authoritativeDamagePerHit = 0f,
            float authoritativeAttackIntervalSeconds = 0f,
            float authoritativeAttackRange = 0f,
            MLMovementTuning movementTuning = null)
        {
            if (serverPathSpeed > 0f)
                profile.moveSpeed = ConvertServerPathSpeedToUnityMoveSpeed(serverPathSpeed, movementTuning);

            if (authoritativeDamagePerHit > 0f)
                profile.damagePerHit = Mathf.Max(1f, authoritativeDamagePerHit);

            if (authoritativeAttackIntervalSeconds > 0f)
            {
                profile.attackIntervalSeconds = Mathf.Clamp(
                    authoritativeAttackIntervalSeconds,
                    MinimumAttackIntervalSeconds,
                    MaximumAttackIntervalSeconds);
            }

            if (authoritativeAttackRange > 0f)
            {
                profile.attackRange = ConvertServerCombatRangeToUnityAttackRange(authoritativeAttackRange);
                profile.engagementRange = ResolveEngagementRangeFromAttackRange(profile.attackRange);
            }
        }

        static BarracksSpawnCombatProfile BuildFromCatalog(string resolvedUnitTypeKey, UnitCatalogEntry catalogEntry, string skinKey, float serverPathSpeed)
        {
            float attackRange = ResolveAttackRange(catalogEntry, resolvedUnitTypeKey, skinKey);
            return new BarracksSpawnCombatProfile
            {
                resolvedUnitTypeKey = resolvedUnitTypeKey,
                maxHp = Mathf.Max(1f, catalogEntry != null && catalogEntry.hp > 0f ? catalogEntry.hp : DefaultMaxHp),
                damagePerHit = Mathf.Max(1f, catalogEntry != null && catalogEntry.attack_damage > 0f ? catalogEntry.attack_damage : DefaultDamagePerHit),
                attackIntervalSeconds = ResolveAttackIntervalSeconds(catalogEntry),
                moveSpeed = ResolveMoveSpeed(catalogEntry, serverPathSpeed),
                attackRange = attackRange,
                engagementRange = ResolveEngagementRangeFromAttackRange(attackRange),
            };
        }

        static BarracksSpawnCombatProfile BuildFallback(string resolvedUnitTypeKey, string skinKey, float serverPathSpeed)
        {
            float attackRange = ResolveFallbackAttackRange(resolvedUnitTypeKey, skinKey);
            return new BarracksSpawnCombatProfile
            {
                resolvedUnitTypeKey = resolvedUnitTypeKey,
                maxHp = DefaultMaxHp,
                damagePerHit = DefaultDamagePerHit,
                attackIntervalSeconds = DefaultAttackIntervalSeconds,
                moveSpeed = ResolveFallbackMoveSpeed(serverPathSpeed),
                attackRange = attackRange,
                engagementRange = ResolveEngagementRangeFromAttackRange(attackRange),
            };
        }

        static string ResolveUnitTypeKey(string unitTypeKey, string skinKey)
        {
            if (!string.IsNullOrWhiteSpace(unitTypeKey))
                return unitTypeKey.Trim();

            if (!string.IsNullOrWhiteSpace(skinKey)
                && UnitPrefabRegistry.TryResolveUnitTypeForSkinFromLoadedRegistries(skinKey, out var resolvedUnitType))
            {
                return resolvedUnitType;
            }

            return unitTypeKey;
        }

        static float ResolveAttackRange(UnitCatalogEntry catalogEntry, string unitTypeKey, string skinKey)
        {
            if (catalogEntry != null && catalogEntry.range > 0f)
            {
                return Mathf.Clamp(catalogEntry.range * RangeToGameFeet, MinimumAttackRange, MaximumAttackRange);
            }

            return ResolveFallbackAttackRange(unitTypeKey, skinKey);
        }

        static float ResolveAttackIntervalSeconds(UnitCatalogEntry catalogEntry)
        {
            if (catalogEntry != null && catalogEntry.attack_speed > 0f)
            {
                return Mathf.Clamp(1f / catalogEntry.attack_speed, MinimumAttackIntervalSeconds, MaximumAttackIntervalSeconds);
            }

            return DefaultAttackIntervalSeconds;
        }

        static float ResolveMoveSpeed(UnitCatalogEntry catalogEntry, float serverPathSpeed)
        {
            if (serverPathSpeed > 0f)
                return ConvertServerPathSpeedToUnityMoveSpeed(serverPathSpeed);

            if (catalogEntry != null && catalogEntry.path_speed > 0f)
                return ConvertServerPathSpeedToUnityMoveSpeed(catalogEntry.path_speed);

            return DefaultMoveSpeed;
        }

        static float ResolveFallbackMoveSpeed(float serverPathSpeed)
        {
            if (serverPathSpeed > 0f)
                return ConvertServerPathSpeedToUnityMoveSpeed(serverPathSpeed);

            return DefaultMoveSpeed;
        }

        static float ResolveFallbackAttackRange(string unitTypeKey, string skinKey)
        {
            string token = $"{unitTypeKey} {skinKey}".ToLowerInvariant();

            if (ContainsAny(token, "archer", "harpy", "watcher", "wyvern", "griffin", "manticora", "dragon", "chimera", "vampire", "mage", "wizard"))
                return 12f;

            if (ContainsAny(token, "spear", "pike", "pole", "halberd", "lance", "viper", "hydra", "trident"))
                return 7.5f;

            if (ContainsAny(token, "ogre", "cyclops", "ent", "golem"))
                return 5.5f;

            return 4.8f;
        }

        static bool ContainsAny(string haystack, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(haystack) || needles == null)
                return false;

            for (int i = 0; i < needles.Length; i++)
            {
                string needle = needles[i];
                if (!string.IsNullOrWhiteSpace(needle) && haystack.Contains(needle))
                    return true;
            }

            return false;
        }
    }
}
