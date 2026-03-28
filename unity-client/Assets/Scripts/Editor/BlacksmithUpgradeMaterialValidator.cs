#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CastleDefender.Game;
using CastleDefender.Net;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CastleDefender.Editor
{
    public static class BlacksmithUpgradeMaterialValidator
    {
        const string AutomatedMenuPath = "Castle Defender/Buildings/Run Blacksmith Upgrade Material Validation";
        const string MenuPath = "Castle Defender/Buildings/Validate Blacksmith Upgrade Visuals (Play Mode)";
        static readonly string[] LaneKeys = { "red", "gold", "blue", "green" };
        static readonly string[] LegacyBannerMaterialPrefixes =
        {
            "TT_Banner_red",
            "TT_Banner_blue_A",
            "TT_Banner_green_A",
            "TT_Banner_yellow",
        };

        const string StepKey = "CastleDefender.BlacksmithValidation.Step";
        const string ReportKey = "CastleDefender.BlacksmithValidation.Report";
        const string OverallValidKey = "CastleDefender.BlacksmithValidation.OverallValid";
        const string DeadlineKey = "CastleDefender.BlacksmithValidation.Deadline";
        const double StepTimeoutSeconds = 75d;

        enum AutomatedStep
        {
            None = 0,
            LoadBootstrap = 1,
            WaitForLogin = 2,
            WaitForLobby = 3,
            WaitForLoadout = 4,
            WaitForInitialGameMl = 5,
            WaitForPostGame = 6,
            WaitForReturnLobby = 7,
            WaitForReturnLoadout = 8,
            WaitForReloadedGameMl = 9,
            Completed = 10,
            Failed = 11,
        }

        static BlacksmithUpgradeMaterialValidator()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.update -= TickAutomatedValidation;
            EditorApplication.update += TickAutomatedValidation;
        }

        [MenuItem(AutomatedMenuPath)]
        public static void RunAutomatedValidation()
        {
            SessionState.SetInt(StepKey, (int)AutomatedStep.LoadBootstrap);
            SessionState.SetString(ReportKey, string.Empty);
            SessionState.SetBool(OverallValidKey, true);
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + StepTimeoutSeconds));

            string reportPath = GetReportPath();
            if (File.Exists(reportPath))
                File.Delete(reportPath);

            AppendReport("[BlacksmithVisualValidation] Starting automated runtime validation.");

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = true;
        }

        [MenuItem(MenuPath)]
        public static void ValidateInPlayMode()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[BlacksmithVisualValidation] Enter Play Mode in Game_ML before running this validator.");
                return;
            }

            var snapshotApplier = UnityEngine.Object.FindFirstObjectByType<SnapshotApplier>(FindObjectsInactive.Include);
            if (snapshotApplier == null)
            {
                Debug.LogError("[BlacksmithVisualValidation] SnapshotApplier was not found in the active play session.");
                return;
            }

            var anchor = UnityEngine.Object.FindObjectsByType<FortressPadAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    candidate.gameObject.scene == SceneManager.GetActiveScene() &&
                    candidate.AnchorLaneColor == FortressPadAnchor.LaneColor.Red &&
                    string.Equals(candidate.BuildingType, "blacksmith", StringComparison.OrdinalIgnoreCase));
            if (anchor == null)
            {
                Debug.LogError("[BlacksmithVisualValidation] Could not find the red-lane blacksmith pad in Game_ML.");
                return;
            }

            var initial = CaptureStage(snapshotApplier, anchor, tier: 1, label: "initial_load");
            var upgraded = CaptureStage(snapshotApplier, anchor, tier: 2, label: "after_upgrade");

            var report = new StringBuilder(512);
            report.AppendLine("[BlacksmithVisualValidation] Runtime report");
            report.AppendLine($"  initial_load valid={initial.valid} activeTier={initial.activeTier} generatedRoot={initial.generatedRootName}");
            report.AppendLine($"  initial_load materials={FormatMaterials(initial.materials)}");
            report.AppendLine($"  after_upgrade valid={upgraded.valid} activeTier={upgraded.activeTier} generatedRoot={upgraded.generatedRootName}");
            report.AppendLine($"  after_upgrade materials={FormatMaterials(upgraded.materials)}");

            if (!string.IsNullOrWhiteSpace(initial.failureReason))
                report.AppendLine($"  initial_load failure={initial.failureReason}");
            if (!string.IsNullOrWhiteSpace(upgraded.failureReason))
                report.AppendLine($"  after_upgrade failure={upgraded.failureReason}");

            if (initial.valid && upgraded.valid)
            {
                Debug.Log(report.ToString());
                return;
            }

            Debug.LogError(report.ToString());
        }

        static void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    if (GetAutomatedStep() != AutomatedStep.None)
                        ResetDeadline();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    var step = GetAutomatedStep();
                    if (step == AutomatedStep.Completed || step == AutomatedStep.Failed)
                    {
                        Debug.Log($"[BlacksmithVisualValidation] Wrote runtime report to '{GetReportPath()}'.");
                        SessionState.SetInt(StepKey, (int)AutomatedStep.None);
                    }
                    break;
            }
        }

        static void TickAutomatedValidation()
        {
            if (!EditorApplication.isPlaying)
                return;

            var step = GetAutomatedStep();
            if (step == AutomatedStep.None || step == AutomatedStep.Completed || step == AutomatedStep.Failed)
                return;

            try
            {
                if (HasTimedOut())
                {
                    FailAutomation($"Timed out while waiting for step '{step}'. Active scene='{SceneManager.GetActiveScene().name}'.");
                    return;
                }

                switch (step)
                {
                    case AutomatedStep.LoadBootstrap:
                        RemoteContentVerification.ClearFailureCounts();
                        RemoteContentVerification.ClearEvidence();
                        SceneManager.LoadScene("Bootstrap", LoadSceneMode.Single);
                        AppendReport("[BlacksmithVisualValidation] Loaded Bootstrap.");
                        AdvanceAutomation(AutomatedStep.WaitForLogin);
                        break;
                    case AutomatedStep.WaitForLogin:
                        if (!IsReadyForNextTransition("Login"))
                            return;

                        LoadingScreen.LoadScene("Lobby");
                        AppendReport("[BlacksmithVisualValidation] Login reached, loading Lobby.");
                        AdvanceAutomation(AutomatedStep.WaitForLobby);
                        break;
                    case AutomatedStep.WaitForLobby:
                        if (!IsReadyForNextTransition("Lobby"))
                            return;

                        LoadingScreen.LoadScene("Loadout");
                        AppendReport("[BlacksmithVisualValidation] Lobby reached, loading Loadout.");
                        AdvanceAutomation(AutomatedStep.WaitForLoadout);
                        break;
                    case AutomatedStep.WaitForLoadout:
                        if (!IsReadyForNextTransition("Loadout"))
                            return;

                        LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML", preloadEnvironment: true);
                        AppendReport("[BlacksmithVisualValidation] Loadout reached, loading Game_ML through remote content gate.");
                        AdvanceAutomation(AutomatedStep.WaitForInitialGameMl);
                        break;
                    case AutomatedStep.WaitForInitialGameMl:
                        if (!IsReadyForNextTransition("Game_ML"))
                            return;

                        CaptureAndAppendStage("initial_load", tier: 1);
                        CaptureAndAppendStage("after_upgrade", tier: 2);
                        LoadingScreen.LoadScene("PostGame");
                        AppendReport("[BlacksmithVisualValidation] Initial Game_ML validation complete, loading PostGame.");
                        AdvanceAutomation(AutomatedStep.WaitForPostGame);
                        break;
                    case AutomatedStep.WaitForPostGame:
                        if (!IsReadyForNextTransition("PostGame"))
                            return;

                        LoadingScreen.LoadScene("Lobby");
                        AppendReport("[BlacksmithVisualValidation] PostGame reached, returning to Lobby.");
                        AdvanceAutomation(AutomatedStep.WaitForReturnLobby);
                        break;
                    case AutomatedStep.WaitForReturnLobby:
                        if (!IsReadyForNextTransition("Lobby"))
                            return;

                        LoadingScreen.LoadScene("Loadout");
                        AppendReport("[BlacksmithVisualValidation] Lobby reached again, loading Loadout.");
                        AdvanceAutomation(AutomatedStep.WaitForReturnLoadout);
                        break;
                    case AutomatedStep.WaitForReturnLoadout:
                        if (!IsReadyForNextTransition("Loadout"))
                            return;

                        LoadingScreen.LoadSceneWithRemoteContentGate("Game_ML", preloadEnvironment: true);
                        AppendReport("[BlacksmithVisualValidation] Loadout reached again, reloading Game_ML through remote content gate.");
                        AdvanceAutomation(AutomatedStep.WaitForReloadedGameMl);
                        break;
                    case AutomatedStep.WaitForReloadedGameMl:
                        if (!IsReadyForNextTransition("Game_ML"))
                            return;

                        CaptureAndAppendStage("after_scene_reload", tier: 2);
                        CompleteAutomation();
                        break;
                }
            }
            catch (Exception ex)
            {
                FailAutomation($"Exception during automation step '{step}': {ex}");
            }
        }

        static ValidationStage CaptureStage(SnapshotApplier snapshotApplier, FortressPadAnchor anchor, int tier, string label)
        {
            snapshotApplier.DebugApplyMLSnapshot(BuildSnapshot(tier), myLaneIndex: 0, viewingLane: 0, totalLanes: LaneKeys.Length);
            anchor.BroadcastMessage("RefreshFromSnapshot", SendMessageOptions.DontRequireReceiver);

            var generatedRoot = anchor.GetComponentsInChildren<TieredBuildingVisual>(true)
                .FirstOrDefault(visual => visual != null && visual.transform.parent == anchor.transform);
            if (generatedRoot == null)
            {
                return ValidationStage.Fail(label, "No generated TieredBuildingVisual was spawned under the blacksmith pad.");
            }

            var renderers = generatedRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return ValidationStage.Fail(label, "The generated blacksmith visual has no renderers.");
            }

            var materials = new List<string>(renderers.Length * 2);
            bool sawBaseBuildingMaterial = false;
            bool sawTeamAccentMaterial = false;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer == null)
                    continue;

                var activeMaterials = renderer.materials;
                if (activeMaterials == null || activeMaterials.Length == 0)
                    continue;

                for (int materialIndex = 0; materialIndex < activeMaterials.Length; materialIndex++)
                {
                    var material = activeMaterials[materialIndex];
                    if (material == null)
                        return ValidationStage.Fail(label, $"Renderer '{renderer.name}' has a null runtime material.");

                    var shader = material.shader;
                    if (shader == null)
                        return ValidationStage.Fail(label, $"Material '{material.name}' on renderer '{renderer.name}' has no shader.");

                    if (!shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
                    {
                        return ValidationStage.Fail(
                            label,
                            $"Renderer '{renderer.name}' is using non-URP shader '{shader.name}' via material '{material.name}'.");
                    }

                    foreach (string legacyPrefix in LegacyBannerMaterialPrefixes)
                    {
                        if (material.name.StartsWith(legacyPrefix, StringComparison.Ordinal))
                        {
                            return ValidationStage.Fail(
                                label,
                                $"Legacy banner material '{material.name}' leaked into renderer '{renderer.name}'.");
                        }
                    }

                    if (material.name.StartsWith("TT_BuildingBase_Neutral", StringComparison.Ordinal))
                        sawBaseBuildingMaterial = true;
                    if (material.name.StartsWith("TT_BannerAccent_", StringComparison.Ordinal))
                        sawTeamAccentMaterial = true;

                    materials.Add($"{renderer.name}:{material.name}:{shader.name}");
                }
            }

            if (materials.Count == 0)
                return ValidationStage.Fail(label, "No runtime materials were collected from the generated blacksmith visual.");

            if (!sawBaseBuildingMaterial)
                return ValidationStage.Fail(label, "The URP building base material was not present on the generated blacksmith body.");

            if (tier >= 2 && !sawTeamAccentMaterial)
                return ValidationStage.Fail(label, "The upgraded blacksmith did not apply a URP team accent material.");

            return new ValidationStage
            {
                label = label,
                valid = true,
                activeTier = generatedRoot.CurrentTier,
                generatedRootName = generatedRoot.gameObject.name,
                materials = materials.ToArray(),
            };
        }

        static MLSnapshot BuildSnapshot(int blacksmithTier)
        {
            return new MLSnapshot
            {
                lanes = LaneKeys
                    .Select((laneKey, laneIndex) => BuildLane(laneKey, laneIndex, blacksmithTier))
                    .ToArray()
            };
        }

        static MLLaneSnap BuildLane(string laneKey, int laneIndex, int blacksmithTier)
        {
            var lane = new MLLaneSnap
            {
                laneIndex = laneIndex,
                slotColor = laneKey,
                team = laneKey,
                fortressPads = Array.Empty<MLFortressPad>(),
                barracksSites = Array.Empty<MLBarracksSite>(),
                barracksRoster = Array.Empty<MLBarracksRosterEntry>(),
                heroRoster = Array.Empty<MLHeroRosterEntry>(),
                upcomingWave = new MLUpcomingWave { entries = Array.Empty<MLUpcomingWaveEntry>() },
                upcomingWaveQueue = Array.Empty<MLUpcomingWave>(),
                path = Array.Empty<MLGridPos>(),
                units = Array.Empty<MLUnit>(),
                projectiles = Array.Empty<MLProjectile>(),
            };

            if (laneIndex == 0)
            {
                lane.fortressPads = new[]
                {
                    new MLFortressPad
                    {
                        padId = "blacksmith_pad",
                        buildingType = "blacksmith",
                        buildingName = "Blacksmith",
                        displayName = "Blacksmith",
                        tier = Mathf.Max(1, blacksmithTier),
                        maxTier = 3,
                        isBuilt = true,
                        canBuild = false,
                        canUpgrade = blacksmithTier < 3,
                        buildState = "built",
                        currentTierName = $"Tier {Mathf.Max(1, blacksmithTier)}",
                    }
                };
            }

            return lane;
        }

        static string FormatMaterials(string[] materials)
        {
            return materials == null || materials.Length == 0
                ? "<none>"
                : string.Join(", ", materials);
        }

        static void AppendStageReport(ValidationStage stage)
        {
            var line = new StringBuilder(256);
            line.Append("[BlacksmithVisualValidation] ");
            line.Append(stage.label);
            line.Append(" valid=");
            line.Append(stage.valid);
            line.Append(" activeTier=");
            line.Append(stage.activeTier);
            line.Append(" generatedRoot=");
            line.Append(stage.generatedRootName);
            line.Append(" materials=");
            line.Append(FormatMaterials(stage.materials));
            if (!string.IsNullOrWhiteSpace(stage.failureReason))
            {
                line.Append(" failure=");
                line.Append(stage.failureReason);
            }

            AppendReport(line.ToString());
            if (!stage.valid)
                SessionState.SetBool(OverallValidKey, false);
        }

        static void AppendReport(string line)
        {
            string current = SessionState.GetString(ReportKey, string.Empty);
            string updated = string.IsNullOrWhiteSpace(current) ? line : $"{current}{Environment.NewLine}{line}";
            SessionState.SetString(ReportKey, updated);
            File.WriteAllText(GetReportPath(), updated);
        }

        static string GetReportPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "blacksmith-material-validation.txt"));
        }

        static void CaptureAndAppendStage(string label, int tier)
        {
            var snapshotApplier = UnityEngine.Object.FindFirstObjectByType<SnapshotApplier>(FindObjectsInactive.Include);
            var anchor = UnityEngine.Object.FindObjectsByType<FortressPadAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    candidate.gameObject.scene == SceneManager.GetActiveScene() &&
                    candidate.AnchorLaneColor == FortressPadAnchor.LaneColor.Red &&
                    string.Equals(candidate.BuildingType, "blacksmith", StringComparison.OrdinalIgnoreCase));

            if (snapshotApplier == null || anchor == null)
            {
                AppendStageReport(ValidationStage.Fail(
                    label,
                    "SnapshotApplier or red-lane blacksmith anchor was not available in Game_ML."));
                return;
            }

            AppendStageReport(CaptureStage(snapshotApplier, anchor, tier, label));
        }

        static bool IsActiveScene(string sceneName)
        {
            var scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid()
                && scene.isLoaded
                && string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal);
        }

        static bool IsReadyForNextTransition(string sceneName)
        {
            return IsActiveScene(sceneName) && !LoadingScreen.IsTransitionInProgress;
        }

        static AutomatedStep GetAutomatedStep()
        {
            return (AutomatedStep)SessionState.GetInt(StepKey, 0);
        }

        static void AdvanceAutomation(AutomatedStep nextStep)
        {
            SessionState.SetInt(StepKey, (int)nextStep);
            ResetDeadline();
        }

        static void ResetDeadline()
        {
            SessionState.SetFloat(DeadlineKey, (float)(EditorApplication.timeSinceStartup + StepTimeoutSeconds));
        }

        static bool HasTimedOut()
        {
            return EditorApplication.timeSinceStartup > SessionState.GetFloat(DeadlineKey, 0f);
        }

        static void CompleteAutomation()
        {
            bool overallValid = SessionState.GetBool(OverallValidKey, true);
            AppendReport($"[BlacksmithVisualValidation] overall_valid={overallValid}");
            File.WriteAllText(GetReportPath(), SessionState.GetString(ReportKey, string.Empty));
            SessionState.SetInt(StepKey, (int)AutomatedStep.Completed);
            EditorApplication.isPlaying = false;
        }

        static void FailAutomation(string reason)
        {
            AppendReport($"[BlacksmithVisualValidation] overall_valid=false failure={reason}");
            SessionState.SetBool(OverallValidKey, false);
            File.WriteAllText(GetReportPath(), SessionState.GetString(ReportKey, string.Empty));
            SessionState.SetInt(StepKey, (int)AutomatedStep.Failed);
            EditorApplication.isPlaying = false;
        }

        struct ValidationStage
        {
            public string label;
            public bool valid;
            public int activeTier;
            public string generatedRootName;
            public string failureReason;
            public string[] materials;

            public static ValidationStage Fail(string label, string reason)
            {
                return new ValidationStage
                {
                    label = label,
                    valid = false,
                    activeTier = 0,
                    generatedRootName = string.Empty,
                    failureReason = reason,
                    materials = Array.Empty<string>(),
                };
            }
        }
    }
}
#endif
