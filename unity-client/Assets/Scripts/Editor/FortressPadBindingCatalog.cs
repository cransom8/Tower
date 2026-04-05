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

        static readonly (string side, int count)[] WallPadBindings =
        {
            ("Front", 8),
            ("Left", 8),
            ("Right", 8),
            ("Rear", 8),
        };

        static readonly string[] GatePadBindings =
        {
            "Front",
            "Left",
            "Right",
            "Rear",
        };

        static readonly (string side, int count)[] TowerPadBindings =
        {
            ("Front", 6),
            ("Left", 4),
            ("Right", 4),
            ("Rear", 6),
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
                    var section = WallPadBindings[i];
                    for (int index = 1; index <= section.count; index++)
                    {
                        results.Add(new RuntimePadSpec(
                            $"wall_{section.side.ToLowerInvariant()}_{index}_pad",
                            "wall",
                            $"{section.side} Wall {index}",
                            lane.LaneColor,
                            new[] { $"{lane.ScenePrefix}_Wall_{section.side}_{index}" },
                            0));
                    }
                }

                for (int i = 0; i < GatePadBindings.Length; i++)
                {
                    results.Add(new RuntimePadSpec(
                        $"gate_{GatePadBindings[i].ToLowerInvariant()}_pad",
                        "gate",
                        $"{GatePadBindings[i]} Gate",
                        lane.LaneColor,
                        new[] { $"{lane.ScenePrefix}_Gate_{GatePadBindings[i]}" },
                        0));
                }

                for (int i = 0; i < TowerPadBindings.Length; i++)
                {
                    var section = TowerPadBindings[i];
                    for (int index = 1; index <= section.count; index++)
                    {
                        results.Add(new RuntimePadSpec(
                            $"tower_{section.side.ToLowerInvariant()}_{index}_pad",
                            "turret",
                            $"{section.side} Tower {index}",
                            lane.LaneColor,
                            new[] { $"{lane.ScenePrefix}_Tower_{section.side}_{index}" },
                            0));
                    }
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
    }
}
