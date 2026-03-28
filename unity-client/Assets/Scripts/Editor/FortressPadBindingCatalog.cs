using System;
using System.Collections.Generic;
using UnityEngine;
using CastleDefender.Game;

namespace CastleDefender.Editor
{
    public static class FortressPadBindingCatalog
    {
        public readonly struct RuntimePadSpec
        {
            public readonly string PadId;
            public readonly string BuildingType;
            public readonly string DisplayName;
            public readonly FortressPadAnchor.LaneColor LaneColor;
            public readonly string[] CandidateNames;
            public readonly int DuplicateIndex;

            public RuntimePadSpec(
                string padId,
                string buildingType,
                string displayName,
                FortressPadAnchor.LaneColor laneColor,
                string[] candidateNames,
                int duplicateIndex)
            {
                PadId = padId;
                BuildingType = buildingType;
                DisplayName = displayName;
                LaneColor = laneColor;
                CandidateNames = candidateNames ?? Array.Empty<string>();
                DuplicateIndex = duplicateIndex;
            }
        }

        sealed class LaneSpec
        {
            public string ScenePrefix;
            public FortressPadAnchor.LaneColor LaneColor;
        }

        static readonly LaneSpec[] LaneSpecs =
        {
            new() { ScenePrefix = "Red", LaneColor = FortressPadAnchor.LaneColor.Red },
            new() { ScenePrefix = "Yellow", LaneColor = FortressPadAnchor.LaneColor.Gold },
            new() { ScenePrefix = "Blue", LaneColor = FortressPadAnchor.LaneColor.Blue },
            new() { ScenePrefix = "Green", LaneColor = FortressPadAnchor.LaneColor.Green },
        };

        static readonly (string padId, string buildingType, string displayName, string objectSuffix)[] BranchPadBindings =
        {
            ("stable_pad", "stable", "Stable", "Stable"),
            ("workshop_pad", "workshop", "Workshop", "Workshop"),
            ("library_pad", "library", "Library", "Library"),
            ("market_pad", "market", "Market", "Market"),
            ("lumber_mill_pad", "lumber_mill", "Lumber Mill", "LumberMill"),
        };

        static readonly (string padId, string displayName, string objectSuffix, string alternateSuffix, string redOverrideSuffix, int duplicateIndex)[] WallPadBindings =
        {
            ("wall_front_left_01_pad", "Front Left Wall 01", "Wall_Front_Left_01_Lvl_1", null, null, 0),
            ("wall_front_left_02_pad", "Front Left Wall 02", "Wall_Front_Left_02_Lvl_1", null, null, 0),
            ("wall_front_left_03_pad", "Front Left Wall 03", "Wall_Front_Left_03_Lvl_1", null, null, 0),
            ("wall_front_left_04_pad", "Front Left Wall 04", "Wall_Front_Left_04_Lvl_1", null, null, 0),
            ("wall_front_left_06_pad", "Front Left Wall 06", "Wall_Front_Left_06_Lvl_1", null, null, 0),
            ("wall_front_left_07_pad", "Front Left Wall 07", "Wall_Front_Left_07_Lvl_1", null, null, 0),
            ("wall_front_right_01_pad", "Front Right Wall 01", "Wall_Front_Right_01_Lvl_1", null, null, 0),
            ("wall_front_right_02_pad", "Front Right Wall 02", "Wall_Front_Right_02_Lvl_1", null, null, 0),
            ("wall_back_left_01_pad", "Back Left Wall 01", "Wall_Back_Left_01_Lvl_1", null, null, 0),
            ("wall_back_left_02_pad", "Back Left Wall 02", "Wall_Back_Left_02_Lvl_1", null, null, 0),
            ("wall_back_left_04_pad", "Back Left Wall 04", "Wall_Back_Left_04_Lvl_1", null, null, 0),
            ("wall_back_left_05_pad", "Back Left Wall 05", "Wall_Back_Left_05_Lvl_1", null, null, 0),
            ("wall_back_right_01_pad", "Back Right Wall 01", "Wall_Back_Right_01_Lvl_1", null, null, 0),
            ("wall_back_right_02_pad", "Back Right Wall 02", "Wall_Back_Right_02_Lvl_1", null, null, 0),
            ("wall_right_side_03_pad", "Right Side Wall 03", "Wall_Right_Side_03_Lvl_1", null, "Wall_Right_Side_01_Lvl_1", 0),
            ("wall_right_side_04_a_pad", "Right Side Wall 04A", "Wall_Right_Side_04_Lvl_1", null, "Wall_Right_Side_02_Lvl_1", 0),
            ("wall_right_side_04_b_pad", "Right Side Wall 04B", "Wall_Right_Side_04_Lvl_1", "Wall_Right_Side_04_Lvl_1 (1)", "Wall_Right_Side_03_Lvl_1", 1),
            ("wall_right_side_05_pad", "Right Side Wall 05", "Wall_Right_Side_05_Lvl_1", null, "Wall_Right_Side_04_Lvl_1", 0),
            ("wall_right_side_06_pad", "Right Side Wall 06", "Wall_Right_Side_06_Lvl_1", null, "Wall_Right_Side_05_Lvl_1", 0),
            ("wall_right_side_07_pad", "Right Side Wall 07", "Wall_Right_Side_07_Lvl_1", null, "Wall_Right_Side_06_Lvl_1", 0),
        };

        static readonly (string padId, string displayName, string objectSuffix)[] GatePadBindings =
        {
            ("gate_front_pad", "Front Gate", "Gate_Front"),
            ("gate_left_pad", "Left Gate", "Gate_Left"),
            ("gate_right_pad", "Right Gate", "Gate_Right"),
            ("gate_rear_pad", "Rear Gate", "Gate_Rear"),
        };

        static readonly (string padId, string displayName, string objectSuffix)[] TurretPadBindings =
        {
            ("turret_front_left_pad", "Front Left Tower", "Tower_Front_Left_Lvl_1"),
            ("turret_front_left_05_pad", "Front Left Tower 05", "Tower_Front_Left_05_Lvl_1"),
            ("turret_front_right_pad", "Front Right Tower", "Tower_Front_Right_Lvl_1"),
            ("turret_core_03_pad", "Inner Tower 03", "Tower_lvl_3"),
            ("turret_core_04_pad", "Inner Tower 04", "Tower_lvl_4"),
            ("turret_core_05_pad", "Inner Tower 05", "Tower_lvl_5"),
            ("turret_front_gate_left_pad", "Front Gate Tower Left", "Tower_Front_Gate_Left_lvl_1"),
            ("turret_front_gate_right_pad", "Front Gate Tower Right", "Tower_Front_Gate_Right_lvl_1"),
            ("turret_back_left_03_pad", "Back Left Tower 03", "Tower_Back_Left_03_Lvl_1"),
            ("turret_back_left_06_pad", "Back Left Tower 06", "Tower_Back_Left_06_Lvl_1"),
            ("turret_back_left_07_pad", "Back Left Tower 07", "Tower_Back_Left_07_Lvl_1"),
            ("turret_back_right_03_pad", "Back Right Tower 03", "Tower_Back_Right_03_Lvl_1"),
            ("turret_rear_gate_left_pad", "Rear Gate Tower Left", "Tower_Rear_Gate_Left_lvl_1"),
            ("turret_rear_gate_right_pad", "Rear Gate Tower Right", "Tower_Rear_Gate_Right_lvl_1"),
            ("turret_right_side_05_pad", "Right Side Tower 05", "Tower_Right_Side_05_Lvl_1"),
            ("turret_right_side_06_pad", "Right Side Tower 06", "Tower_Right_Side_06_Lvl_1"),
            ("turret_right_side_07_pad", "Right Side Tower 07", "Tower_Right_Side_07_Lvl_1"),
        };

        public static void GetRuntimePadSpecs(List<RuntimePadSpec> results)
        {
            if (results == null)
                return;

            results.Clear();

            for (int laneIndex = 0; laneIndex < LaneSpecs.Length; laneIndex++)
            {
                var lane = LaneSpecs[laneIndex];

                for (int i = 0; i < BranchPadBindings.Length; i++)
                {
                    var spec = BranchPadBindings[i];
                    results.Add(new RuntimePadSpec(
                        spec.padId,
                        spec.buildingType,
                        spec.displayName,
                        lane.LaneColor,
                        new[] { $"{lane.ScenePrefix}_{spec.objectSuffix}" },
                        0));
                }

                for (int i = 0; i < WallPadBindings.Length; i++)
                {
                    var spec = WallPadBindings[i];
                    int duplicateIndex = !string.IsNullOrWhiteSpace(spec.alternateSuffix)
                        && !string.Equals(lane.ScenePrefix, "Red", StringComparison.Ordinal)
                        ? 0
                        : spec.duplicateIndex;

                    results.Add(new RuntimePadSpec(
                        spec.padId,
                        "wall",
                        spec.displayName,
                        lane.LaneColor,
                        BuildWallCandidateNames(lane.ScenePrefix, spec.objectSuffix, spec.alternateSuffix, spec.redOverrideSuffix),
                        duplicateIndex));
                }

                for (int i = 0; i < GatePadBindings.Length; i++)
                {
                    var spec = GatePadBindings[i];
                    results.Add(new RuntimePadSpec(
                        spec.padId,
                        "gate",
                        spec.displayName,
                        lane.LaneColor,
                        new[] { $"{lane.ScenePrefix}_{spec.objectSuffix}" },
                        0));
                }

                for (int i = 0; i < TurretPadBindings.Length; i++)
                {
                    var spec = TurretPadBindings[i];
                    results.Add(new RuntimePadSpec(
                        spec.padId,
                        "turret",
                        spec.displayName,
                        lane.LaneColor,
                        new[] { $"{lane.ScenePrefix}_{spec.objectSuffix}" },
                        0));
                }
            }
        }

        public static Vector3 ResolveMinimumColliderSize(string buildingType)
        {
            switch ((buildingType ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "wall":
                    return new Vector3(2.8f, 2.8f, 1.8f);
                case "gate":
                    return new Vector3(3.6f, 3.2f, 2.2f);
                case "turret":
                    return new Vector3(2.8f, 4.0f, 2.8f);
                default:
                    return new Vector3(2.2f, 2.4f, 2.2f);
            }
        }

        static string[] BuildCandidateNames(string lanePrefix, string objectSuffix, string alternateSuffix)
        {
            if (!string.IsNullOrWhiteSpace(alternateSuffix))
            {
                if (string.Equals(lanePrefix, "Red", StringComparison.Ordinal))
                    return new[] { $"{lanePrefix}_{objectSuffix}" };

                return new[]
                {
                    $"{lanePrefix}_{alternateSuffix}",
                    $"{lanePrefix}_{objectSuffix}",
                };
            }

            return new[]
            {
                $"{lanePrefix}_{objectSuffix}",
            };
        }

        static string[] BuildWallCandidateNames(string lanePrefix, string objectSuffix, string alternateSuffix, string redOverrideSuffix)
        {
            if (string.Equals(lanePrefix, "Red", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(redOverrideSuffix))
            {
                return new[]
                {
                    $"{lanePrefix}_{redOverrideSuffix}",
                };
            }

            return BuildCandidateNames(lanePrefix, objectSuffix, alternateSuffix);
        }
    }
}
