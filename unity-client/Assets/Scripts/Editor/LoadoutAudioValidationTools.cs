using System;
using System.Collections.Generic;
using System.Reflection;
using CastleDefender.Game;
using CastleDefender.Net;
using CastleDefender.UI;
using UnityEditor;
using UnityEngine;

namespace CastleDefender.Editor
{
    public static class LoadoutAudioValidationTools
    {
        const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
        const float VoiceValidationSpacingSeconds = 1.2f;

        [MenuItem("Castle Defender/Audio Validation/Validate Loadout Audio Coverage")]
        public static void ValidateLoadoutAudioCoverage()
        {
            var host = new GameObject("LoadoutAudioValidationHost");
            host.SetActive(false);
            var manager = host.AddComponent<LoadoutPhaseManager>();

            try
            {
                MethodInfo tryResolvePreviewSfx = typeof(LoadoutPhaseManager).GetMethod("TryResolvePreviewSfx", InstanceNonPublic);
                MethodInfo buildPreviewVoiceUnit = typeof(LoadoutPhaseManager).GetMethod("BuildPreviewVoiceUnit", StaticNonPublic);
                if (tryResolvePreviewSfx == null || buildPreviewVoiceUnit == null)
                {
                    Debug.LogError("[LoadoutAudioValidation] Could not resolve private loadout audio helpers.");
                    return;
                }

                List<string> failures = new();
                foreach (RaceProgressionUnitDefinition unit in EnumerateRaceUnits())
                {
                    if (unit == null)
                        continue;

                    bool isRequirement = unit.CardStyle == RaceProgressionUnitCardStyle.RequirementStep;
                    bool isBuildingCard = unit.CardDisplay != null;

                    object[] previewSfxArgs = { unit, null, null };
                    bool hasPreviewSfx = !isRequirement && (bool)tryResolvePreviewSfx.Invoke(manager, previewSfxArgs);
                    if (!isRequirement && !hasPreviewSfx)
                        failures.Add($"Missing preview SFX mapping for '{unit.DisplayName}' ({unit.Id}).");

                    if (isRequirement || isBuildingCard)
                        continue;

                    if (!ShouldExpectVoice(unit))
                        continue;

                    var previewUnit = buildPreviewVoiceUnit.Invoke(null, new object[] { unit }) as MLUnit;
                    if (previewUnit == null)
                    {
                        failures.Add($"Preview voice unit was not created for '{unit.DisplayName}' ({unit.Id}).");
                        continue;
                    }

                    if (!UnitVoiceLibrary.HasVoiceProfile(previewUnit))
                    {
                        failures.Add($"Voice profile was not resolved for '{unit.DisplayName}' ({unit.Id}).");
                        continue;
                    }

                    foreach (UnitVoiceCue cue in Enum.GetValues(typeof(UnitVoiceCue)))
                    {
                        if (!UnitVoiceLibrary.HasGeneratedClips(previewUnit, cue))
                            failures.Add($"Missing imported voice clips for '{unit.DisplayName}' ({unit.Id}) cue '{cue}'.");
                    }
                }

                if (failures.Count > 0)
                {
                    for (int i = 0; i < failures.Count; i++)
                        Debug.LogError($"[LoadoutAudioValidation] {failures[i]}");
                    Debug.LogError($"[LoadoutAudioValidation] Loadout audio coverage failed with {failures.Count} issue(s).");
                    return;
                }

                Debug.Log("[LoadoutAudioValidation] Loadout audio coverage passed for all current race units.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [MenuItem("Castle Defender/Audio Validation/Validate Runtime Unit Audio (Play Mode)")]
        public static void ValidateRuntimeUnitAudio()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[LoadoutAudioValidation] Enter Play Mode before validating runtime unit audio.");
                return;
            }

            if (AudioManager.I == null)
            {
                Debug.LogError("[LoadoutAudioValidation] AudioManager.I is null in play mode.");
                return;
            }

            List<string> failures = ValidateAssignedRuntimeSfx();

            var host = new GameObject("RuntimeUnitAudioValidationHost");
            try
            {
                var spawner = host.AddComponent<WaveSnapshotRuntimeSpawner>();
                MethodInfo tryPlayCombatSfx = typeof(WaveSnapshotRuntimeSpawner).GetMethod("TryPlayCombatSfx", InstanceNonPublic);
                MethodInfo tryPlayUnitVoice = typeof(WaveSnapshotRuntimeSpawner).GetMethod("TryPlayUnitVoice", InstanceNonPublic);
                FieldInfo combatHistoryField = typeof(WaveSnapshotRuntimeSpawner).GetField("_lastCombatSfxAt", InstanceNonPublic);
                FieldInfo cueHistoryField = typeof(WaveSnapshotRuntimeSpawner).GetField("_lastUnitVoiceAtByCue", InstanceNonPublic);
                FieldInfo lastAnyVoiceField = typeof(WaveSnapshotRuntimeSpawner).GetField("_lastAnyUnitVoiceAt", InstanceNonPublic);

                if (tryPlayCombatSfx == null || tryPlayUnitVoice == null || combatHistoryField == null || cueHistoryField == null || lastAnyVoiceField == null)
                {
                    Debug.LogError("[LoadoutAudioValidation] Could not resolve private runtime audio helpers.");
                    return;
                }

                var combatHistory = combatHistoryField.GetValue(spawner) as System.Collections.IDictionary;
                var cueHistory = cueHistoryField.GetValue(spawner) as System.Collections.IDictionary;
                if (combatHistory == null || cueHistory == null)
                {
                    Debug.LogError("[LoadoutAudioValidation] Runtime audio history dictionaries were not initialized.");
                    return;
                }

                float now = 1f;
                foreach (AudioManager.SFX sfx in new[]
                {
                    AudioManager.SFX.FighterSlash,
                    AudioManager.SFX.ArcherShoot,
                    AudioManager.SFX.MageShoot,
                    AudioManager.SFX.BallistaShoot,
                    AudioManager.SFX.UnitSpawn,
                    AudioManager.SFX.UnitDeath,
                })
                {
                    tryPlayCombatSfx.Invoke(spawner, new object[] { sfx, 0.85f, now });
                    if (!combatHistory.Contains(sfx))
                    {
                        failures.Add($"Runtime combat SFX '{sfx}' was not recorded.");
                    }
                    else if (!Mathf.Approximately(Convert.ToSingle(combatHistory[sfx]), now))
                    {
                        failures.Add($"Runtime combat SFX '{sfx}' recorded an unexpected timestamp.");
                    }

                    now += 1f;
                }

                foreach (MLUnit unit in BuildRepresentativeUnits())
                {
                    foreach (UnitVoiceCue cue in Enum.GetValues(typeof(UnitVoiceCue)))
                    {
                        tryPlayUnitVoice.Invoke(spawner, new object[] { unit, cue, now, 0.8f, true });
                        if (!cueHistory.Contains(cue))
                        {
                            failures.Add($"Runtime voice cue '{cue}' was not recorded for '{unit.catalogUnitKey}'.");
                        }
                        else if (!Mathf.Approximately(Convert.ToSingle(cueHistory[cue]), now))
                        {
                            failures.Add($"Runtime voice cue '{cue}' recorded an unexpected timestamp for '{unit.catalogUnitKey}'.");
                        }
                        else if (!Mathf.Approximately(Convert.ToSingle(lastAnyVoiceField.GetValue(spawner)), now))
                        {
                            failures.Add($"Runtime global voice timestamp did not update for '{unit.catalogUnitKey}' cue '{cue}'.");
                        }

                        now += VoiceValidationSpacingSeconds;
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }

            if (failures.Count > 0)
            {
                for (int i = 0; i < failures.Count; i++)
                    Debug.LogError($"[LoadoutAudioValidation] {failures[i]}");
                Debug.LogError($"[LoadoutAudioValidation] Runtime unit audio validation failed with {failures.Count} issue(s).");
                return;
            }

            Debug.Log("[LoadoutAudioValidation] Runtime unit audio validation passed for representative unit families.");
        }

        static IEnumerable<RaceProgressionUnitDefinition> EnumerateRaceUnits()
        {
            RaceProgressionDefinition race = RaceProgressionCatalog.GetOrDefault(RaceProgressionCatalog.DefaultRaceId, "LoadoutAudioValidation");
            if (race == null || race.Lanes == null)
                yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int laneIndex = 0; laneIndex < race.Lanes.Length; laneIndex++)
            {
                RaceProgressionLaneDefinition lane = race.Lanes[laneIndex];
                if (lane == null)
                    continue;

                foreach (RaceProgressionUnitDefinition unit in EnumerateUniqueUnits(lane.Units, seen))
                    yield return unit;
                foreach (RaceProgressionUnitDefinition unit in EnumerateUniqueUnits(lane.OutcomeUnits, seen))
                    yield return unit;
            }
        }

        static IEnumerable<RaceProgressionUnitDefinition> EnumerateUniqueUnits(RaceProgressionUnitDefinition[] units, HashSet<string> seen)
        {
            if (units == null)
                yield break;

            for (int i = 0; i < units.Length; i++)
            {
                RaceProgressionUnitDefinition unit = units[i];
                if (unit == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(unit.Id) && !seen.Add(unit.Id))
                    continue;

                yield return unit;
            }
        }

        static List<string> ValidateAssignedRuntimeSfx()
        {
            var failures = new List<string>();
            CheckAssignedClip(nameof(AudioManager.unitSpawn), failures);
            CheckAssignedClip(nameof(AudioManager.unitDeath), failures);
            CheckAssignedClip(nameof(AudioManager.archerShoot), failures);
            CheckAssignedClip(nameof(AudioManager.fighterSlash), failures);
            CheckAssignedClip(nameof(AudioManager.mageShoot), failures);
            CheckAssignedClip(nameof(AudioManager.ballistaShoot), failures);
            CheckAssignedClip(nameof(AudioManager.cannonShoot), failures);
            return failures;
        }

        static void CheckAssignedClip(string fieldName, List<string> failures)
        {
            FieldInfo field = typeof(AudioManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null)
            {
                failures.Add($"AudioManager field '{fieldName}' was not found.");
                return;
            }

            if (field.GetValue(AudioManager.I) as AudioClip == null)
                failures.Add($"AudioManager clip '{fieldName}' is not assigned.");
        }

        static MLUnit[] BuildRepresentativeUnits()
        {
            return new[]
            {
                CreateUnit("rep_infantry", "tt_peasant"),
                CreateUnit("rep_polearm", "tt_spearman"),
                CreateUnit("rep_shield", "tt_heavy_infantry"),
                CreateUnit("rep_ranged", "tt_archer"),
                CreateUnit("rep_support", "tt_priest"),
                CreateUnit("rep_arcane", "tt_mage"),
                CreateUnit("rep_economy", "tt_settler"),
                CreateUnit("rep_king", "tt_king", isHero: true, heroKey: "king"),
                CreateUnit("rep_paladin", "tt_paladin", isHero: true, heroKey: "paladin"),
                CreateUnit("rep_bishop", "tt_commander", isHero: true, heroKey: "bishop"),
            };
        }

        static MLUnit CreateUnit(string id, string catalogUnitKey, bool isHero = false, string heroKey = null)
        {
            return new MLUnit
            {
                id = id,
                unitId = id,
                type = catalogUnitKey,
                unitTypeKey = catalogUnitKey,
                catalogUnitKey = catalogUnitKey,
                skinKey = catalogUnitKey,
                isHero = isHero,
                heroKey = heroKey,
            };
        }

        static bool ShouldExpectVoice(RaceProgressionUnitDefinition unit)
        {
            return unit != null
                && !string.IsNullOrWhiteSpace(unit.Id)
                && !unit.Id.EndsWith("_siege", StringComparison.OrdinalIgnoreCase);
        }
    }
}
