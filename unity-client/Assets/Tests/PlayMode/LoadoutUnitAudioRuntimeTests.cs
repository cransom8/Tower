using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CastleDefender.Net;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class LoadoutUnitAudioRuntimeTests
{
    const float VoiceValidationSpacingSeconds = 1.2f;
    static readonly string[] VoiceCueNames = { "Spawn", "Attack", "Hurt", "Death" };
    static readonly string[] AudioClipFieldNames =
    {
        "unitSpawn",
        "unitDeath",
        "runnerMove",
        "archerShoot",
        "fighterSlash",
        "mageShoot",
        "ballistaShoot",
        "cannonShoot",
        "cannonSplash",
        "placeWall",
        "buildTower",
        "upgradeTower",
        "upgradeBarracks",
        "goldGain",
        "goldSpend",
        "lifeLost",
        "autosendToggle",
        "buttonClick",
        "tabSwitch",
        "gameOver",
        "victory",
        "rematch",
        "error",
        "ambientLoop",
    };

    [UnityTest]
    public IEnumerator LoadoutPreview_Covers_All_CurrentRaceUnits_With_Sfx_And_Imported_VoiceClips()
    {
        Type loadoutType = FindType("CastleDefender.UI.LoadoutPhaseManager");
        Type catalogType = FindType("CastleDefender.UI.RaceProgressionCatalog");
        Type voiceLibraryType = FindType("CastleDefender.Game.UnitVoiceLibrary");
        Type voiceCueType = FindType("CastleDefender.Game.UnitVoiceCue");

        Assert.That(loadoutType, Is.Not.Null);
        Assert.That(catalogType, Is.Not.Null);
        Assert.That(voiceLibraryType, Is.Not.Null);
        Assert.That(voiceCueType, Is.Not.Null);

        GameObject loadoutHost = new("LoadoutAudioCoverageHost");
        loadoutHost.SetActive(false);
        Component loadout = loadoutHost.AddComponent(loadoutType);

        try
        {
            MethodInfo tryResolvePreviewSfx = loadoutType.GetMethod("TryResolvePreviewSfx", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo buildPreviewVoiceUnit = loadoutType.GetMethod("BuildPreviewVoiceUnit", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo hasVoiceProfile = voiceLibraryType.GetMethod("HasVoiceProfile", BindingFlags.Public | BindingFlags.Static);
            MethodInfo hasGeneratedClips = voiceLibraryType.GetMethod("HasGeneratedClips", BindingFlags.Public | BindingFlags.Static);

            Assert.That(tryResolvePreviewSfx, Is.Not.Null);
            Assert.That(buildPreviewVoiceUnit, Is.Not.Null);
            Assert.That(hasVoiceProfile, Is.Not.Null);
            Assert.That(hasGeneratedClips, Is.Not.Null);

            List<string> failures = new();
            foreach (object unit in CollectRaceUnits(catalogType))
            {
                string displayName = GetStringProperty(unit, "DisplayName");
                string unitId = GetStringProperty(unit, "Id");
                bool isRequirement = string.Equals(GetEnumName(unit, "CardStyle"), "RequirementStep", StringComparison.Ordinal);
                bool isBuildingCard = GetPropertyValue(unit, "CardDisplay") != null;

                object[] previewSfxArgs = { unit, null, null };
                bool hasPreviewSfx = !isRequirement && (bool)tryResolvePreviewSfx.Invoke(loadout, previewSfxArgs);
                if (!isRequirement && !hasPreviewSfx)
                    failures.Add($"Missing preview SFX mapping for '{displayName}' ({unitId}).");

                if (isRequirement || isBuildingCard)
                    continue;

                if (!ShouldExpectVoice(unit))
                    continue;

                object previewUnit = buildPreviewVoiceUnit.Invoke(null, new[] { unit });
                if (previewUnit == null)
                {
                    failures.Add($"Preview voice unit was not created for '{displayName}' ({unitId}).");
                    continue;
                }

                if (!(bool)hasVoiceProfile.Invoke(null, new[] { previewUnit }))
                {
                    failures.Add($"Voice profile was not resolved for '{displayName}' ({unitId}).");
                    continue;
                }

                for (int i = 0; i < VoiceCueNames.Length; i++)
                {
                    object cue = Enum.Parse(voiceCueType, VoiceCueNames[i]);
                    bool hasClips = (bool)hasGeneratedClips.Invoke(null, new[] { previewUnit, cue });
                    if (!hasClips)
                        failures.Add($"Missing imported voice clips for '{displayName}' ({unitId}) cue '{VoiceCueNames[i]}'.");
                }
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(loadoutHost);
        }

        yield return null;
    }

    [UnityTest]
    public IEnumerator LoadoutPreviewVoice_Playback_Works_For_All_CurrentRaceUnits()
    {
        Type loadoutType = FindType("CastleDefender.UI.LoadoutPhaseManager");
        Type catalogType = FindType("CastleDefender.UI.RaceProgressionCatalog");
        Type audioManagerType = FindType("AudioManager");

        Assert.That(loadoutType, Is.Not.Null);
        Assert.That(catalogType, Is.Not.Null);
        Assert.That(audioManagerType, Is.Not.Null);

        GameObject audioListener = EnsureAudioListener();
        GameObject audioManagerHost = CreateAudioManager(audioManagerType);

        try
        {
            MethodInfo tryPlayPreviewVoice = loadoutType.GetMethod("TryPlayPreviewVoice", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(tryPlayPreviewVoice, Is.Not.Null);

            List<string> failures = new();
            foreach (object unit in CollectRaceUnits(catalogType))
            {
                bool isRequirement = string.Equals(GetEnumName(unit, "CardStyle"), "RequirementStep", StringComparison.Ordinal);
                bool isBuildingCard = GetPropertyValue(unit, "CardDisplay") != null;
                if (isRequirement || isBuildingCard || !ShouldExpectVoice(unit))
                    continue;

                bool played = (bool)tryPlayPreviewVoice.Invoke(null, new[] { unit });
                if (!played)
                {
                    failures.Add(
                        $"Preview voice playback failed for '{GetStringProperty(unit, "DisplayName")}' ({GetStringProperty(unit, "Id")}).");
                }

                yield return null;
            }

            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }
        finally
        {
            if (audioManagerHost != null)
                UnityEngine.Object.DestroyImmediate(audioManagerHost);
            if (audioListener != null)
                UnityEngine.Object.DestroyImmediate(audioListener);
        }
    }

    [UnityTest]
    public IEnumerator WaveRuntime_AudioMethods_Register_CombatSfx_And_UnitVoices_For_RepresentativeFamilies()
    {
        Type spawnerType = FindType("CastleDefender.Game.WaveSnapshotRuntimeSpawner");
        Type voiceCueType = FindType("CastleDefender.Game.UnitVoiceCue");
        Type sfxEnumType = FindType("AudioManager+SFX");
        Type audioManagerType = FindType("AudioManager");

        Assert.That(spawnerType, Is.Not.Null);
        Assert.That(voiceCueType, Is.Not.Null);
        Assert.That(sfxEnumType, Is.Not.Null);
        Assert.That(audioManagerType, Is.Not.Null);

        GameObject audioListener = EnsureAudioListener();
        GameObject audioManagerHost = CreateAudioManager(audioManagerType);
        GameObject spawnerHost = new("WaveRuntimeAudioValidation");

        try
        {
            Component spawner = spawnerHost.AddComponent(spawnerType);
            MethodInfo tryPlayCombatSfx = spawnerType.GetMethod("TryPlayCombatSfx", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo tryPlayUnitVoice = spawnerType.GetMethod("TryPlayUnitVoice", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo combatHistoryField = spawnerType.GetField("_lastCombatSfxAt", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo cueHistoryField = spawnerType.GetField("_lastUnitVoiceAtByCue", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo lastAnyVoiceField = spawnerType.GetField("_lastAnyUnitVoiceAt", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(tryPlayCombatSfx, Is.Not.Null);
            Assert.That(tryPlayUnitVoice, Is.Not.Null);
            Assert.That(combatHistoryField, Is.Not.Null);
            Assert.That(cueHistoryField, Is.Not.Null);
            Assert.That(lastAnyVoiceField, Is.Not.Null);

            IDictionary combatHistory = combatHistoryField.GetValue(spawner) as IDictionary;
            IDictionary cueHistory = cueHistoryField.GetValue(spawner) as IDictionary;

            Assert.That(combatHistory, Is.Not.Null);
            Assert.That(cueHistory, Is.Not.Null);

            float now = 1f;
            foreach (string sfxName in new[] { "FighterSlash", "ArcherShoot", "MageShoot", "BallistaShoot", "CannonShoot", "UnitDeath" })
            {
                object sfx = Enum.Parse(sfxEnumType, sfxName);
                tryPlayCombatSfx.Invoke(spawner, new object[] { sfx, 0.85f, now });

                Assert.That(combatHistory.Contains(sfx), Is.True, $"Combat SFX '{sfxName}' was not recorded by the runtime spawner.");
                Assert.That(Convert.ToSingle(combatHistory[sfx]), Is.EqualTo(now).Within(0.001f));
                now += 1f;
                yield return null;
            }

            foreach (MLUnit unit in BuildRepresentativeUnits())
            {
                for (int i = 0; i < VoiceCueNames.Length; i++)
                {
                    object cue = Enum.Parse(voiceCueType, VoiceCueNames[i]);
                    tryPlayUnitVoice.Invoke(spawner, new object[] { unit, cue, now, 0.8f, true });

                    Assert.That(
                        cueHistory.Contains(cue),
                        Is.True,
                        $"Runtime voice cue '{VoiceCueNames[i]}' was not recorded for '{unit.catalogUnitKey ?? unit.type ?? unit.id}'.");
                    Assert.That(Convert.ToSingle(cueHistory[cue]), Is.EqualTo(now).Within(0.001f));
                    Assert.That(Convert.ToSingle(lastAnyVoiceField.GetValue(spawner)), Is.EqualTo(now).Within(0.001f));

                    now += VoiceValidationSpacingSeconds;
                    yield return null;
                }
            }
        }
        finally
        {
            if (spawnerHost != null)
                UnityEngine.Object.DestroyImmediate(spawnerHost);
            if (audioManagerHost != null)
                UnityEngine.Object.DestroyImmediate(audioManagerHost);
            if (audioListener != null)
                UnityEngine.Object.DestroyImmediate(audioListener);
        }
    }

    static IEnumerable<object> CollectRaceUnits(Type catalogType)
    {
        string defaultRaceId = catalogType.GetProperty("DefaultRaceId", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string;
        MethodInfo getOrDefault = catalogType.GetMethod("GetOrDefault", BindingFlags.Public | BindingFlags.Static);
        Assert.That(getOrDefault, Is.Not.Null);

        object race = getOrDefault.Invoke(null, new object[] { defaultRaceId, "LoadoutUnitAudioRuntimeTests" });
        Assert.That(race, Is.Not.Null);

        Array lanes = GetPropertyValue(race, "Lanes") as Array;
        Assert.That(lanes, Is.Not.Null);

        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (object lane in lanes)
        {
            foreach (object unit in EnumerateUnitsFromLane(lane, "Units", seen))
                yield return unit;
            foreach (object unit in EnumerateUnitsFromLane(lane, "OutcomeUnits", seen))
                yield return unit;
        }
    }

    static IEnumerable<object> EnumerateUnitsFromLane(object lane, string propertyName, HashSet<string> seen)
    {
        Array units = GetPropertyValue(lane, propertyName) as Array;
        if (units == null)
            yield break;

        for (int i = 0; i < units.Length; i++)
        {
            object unit = units.GetValue(i);
            if (unit == null)
                continue;

            string unitId = GetStringProperty(unit, "Id");
            if (!string.IsNullOrWhiteSpace(unitId) && !seen.Add(unitId))
                continue;

            yield return unit;
        }
    }

    static GameObject CreateAudioManager(Type audioManagerType)
    {
        DestroyExistingObjectsOfType(audioManagerType);

        GameObject host = new("AudioManager_Test");
        Component audioManager = host.AddComponent(audioManagerType);
        AudioClip clip = AudioClip.Create("audio-validation", 22050, 1, 22050, false);

        for (int i = 0; i < AudioClipFieldNames.Length; i++)
        {
            FieldInfo field = audioManagerType.GetField(AudioClipFieldNames[i], BindingFlags.Instance | BindingFlags.Public);
            field?.SetValue(audioManager, clip);
        }

        return host;
    }

    static GameObject EnsureAudioListener()
    {
        AudioListener existing = UnityEngine.Object.FindFirstObjectByType<AudioListener>(FindObjectsInactive.Include);
        if (existing != null)
            return null;

        GameObject listener = new("AudioListener_Test");
        listener.AddComponent<AudioListener>();
        return listener;
    }

    static void DestroyExistingObjectsOfType(Type type)
    {
        if (type == null)
            return;

        UnityEngine.Object[] existing = Resources.FindObjectsOfTypeAll(type);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] is Component component && component.gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(component.gameObject);
                continue;
            }

            if (existing[i] != null)
                UnityEngine.Object.DestroyImmediate(existing[i]);
        }
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

    static object GetPropertyValue(object target, string propertyName)
    {
        return target?.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(target);
    }

    static string GetStringProperty(object target, string propertyName)
    {
        return GetPropertyValue(target, propertyName) as string;
    }

    static string GetEnumName(object target, string propertyName)
    {
        object value = GetPropertyValue(target, propertyName);
        return value?.ToString();
    }

    static bool ShouldExpectVoice(object unit)
    {
        string unitId = GetStringProperty(unit, "Id");
        return !string.IsNullOrWhiteSpace(unitId)
            && !unitId.EndsWith("_siege", StringComparison.OrdinalIgnoreCase);
    }

    static Type FindType(string fullName)
    {
        for (int i = 0; i < AppDomain.CurrentDomain.GetAssemblies().Length; i++)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies()[i];
            Type type = assembly.GetType(fullName, false);
            if (type != null)
                return type;
        }

        return null;
    }
}
