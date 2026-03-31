using System;
using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    static class FortressLaneResolver
    {
        public static string ResolveLaneKey(Transform source, FortressPadAnchor.LaneColor explicitLaneColor)
        {
            string inferred = InferLaneKeyFromHierarchy(source);
            if (!string.IsNullOrWhiteSpace(inferred))
                return inferred;

            return LaneColorToLaneKey(explicitLaneColor);
        }

        public static int ResolveLaneIndex(Transform source, FortressPadAnchor.LaneColor explicitLaneColor)
        {
            return LaneKeyToLaneIndex(ResolveLaneKey(source, explicitLaneColor));
        }

        public static bool MatchesLane(Transform source, FortressPadAnchor.LaneColor explicitLaneColor, string slotColor, int laneIndex)
        {
            string ownLaneKey = ResolveLaneKey(source, explicitLaneColor);
            if (string.IsNullOrWhiteSpace(ownLaneKey))
                return false;

            string laneKey = FortressPadAnchor.NormalizeLaneKey(slotColor);
            if (!string.IsNullOrWhiteSpace(laneKey))
                return string.Equals(ownLaneKey, laneKey, StringComparison.OrdinalIgnoreCase);

            int configuredLaneIndex = ResolveConfiguredLaneIndex(SnapshotApplier.Instance, ownLaneKey);
            if (configuredLaneIndex >= 0)
                return configuredLaneIndex == laneIndex;

            return string.Equals(
                ownLaneKey,
                FortressPadAnchor.LaneIndexToLaneKey(laneIndex),
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryResolveSnapshotLane(
            SnapshotApplier snapshotApplier,
            Transform source,
            FortressPadAnchor.LaneColor explicitLaneColor,
            out MLLaneSnap lane,
            out bool laneConfigured)
        {
            lane = null;
            laneConfigured = false;
            if (snapshotApplier == null)
                return false;

            string laneKey = ResolveLaneKey(source, explicitLaneColor);
            if (string.IsNullOrWhiteSpace(laneKey))
                return false;

            var lanes = snapshotApplier.LatestML?.lanes;
            if (lanes != null)
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    var candidate = lanes[i];
                    if (candidate == null || !MatchesLaneKey(candidate.slotColor, laneKey))
                        continue;

                    laneConfigured = true;
                    lane = candidate;
                    return true;
                }
            }

            int configuredLaneIndex = ResolveConfiguredLaneIndex(snapshotApplier, laneKey);
            laneConfigured = configuredLaneIndex >= 0;
            if (configuredLaneIndex < 0)
                return false;

            lane = snapshotApplier.GetLane(configuredLaneIndex);
            return lane != null;
        }

        public static bool IsLaneConfiguredInCurrentMatch(
            SnapshotApplier snapshotApplier,
            Transform source,
            FortressPadAnchor.LaneColor explicitLaneColor)
        {
            if (snapshotApplier == null)
                return false;

            string laneKey = ResolveLaneKey(source, explicitLaneColor);
            if (string.IsNullOrWhiteSpace(laneKey))
                return false;

            return ResolveConfiguredLaneIndex(snapshotApplier, laneKey) >= 0;
        }

        public static string LaneColorToLaneKey(FortressPadAnchor.LaneColor laneColor)
        {
            return laneColor switch
            {
                FortressPadAnchor.LaneColor.Red => "red",
                FortressPadAnchor.LaneColor.Gold => "yellow",
                FortressPadAnchor.LaneColor.Blue => "blue",
                FortressPadAnchor.LaneColor.Green => "green",
                _ => string.Empty,
            };
        }

        public static int LaneKeyToLaneIndex(string laneKey)
        {
            return FortressPadAnchor.NormalizeLaneKey(laneKey) switch
            {
                "red" => 0,
                "yellow" => 1,
                "blue" => 2,
                "green" => 3,
                _ => -1,
            };
        }

        static int ResolveConfiguredLaneIndex(SnapshotApplier snapshotApplier, string laneKey)
        {
            if (snapshotApplier == null || string.IsNullOrWhiteSpace(laneKey))
                return -1;

            var lanes = snapshotApplier.LatestML?.lanes;
            if (lanes != null)
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    var lane = lanes[i];
                    if (lane != null && MatchesLaneKey(lane.slotColor, laneKey))
                        return lane.laneIndex;
                }
            }

            var assignments = snapshotApplier.LatestMLMatchReady?.laneAssignments;
            if (assignments != null)
            {
                for (int i = 0; i < assignments.Length; i++)
                {
                    var assignment = assignments[i];
                    if (assignment != null && MatchesLaneKey(assignment.slotColor, laneKey))
                        return assignment.laneIndex;
                }
            }

            var topologySlots = snapshotApplier.CurrentBattlefieldTopology?.slotDefinitions;
            if (topologySlots != null)
            {
                for (int i = 0; i < topologySlots.Length; i++)
                {
                    var slot = topologySlots[i];
                    if (slot != null && MatchesLaneKey(slot.slotColor, laneKey))
                        return slot.laneIndex;
                }
            }

            var configSlots = snapshotApplier.LatestMLMatchConfig?.slotDefinitions;
            if (configSlots != null)
            {
                for (int i = 0; i < configSlots.Length; i++)
                {
                    var slot = configSlots[i];
                    if (slot != null && MatchesLaneKey(slot.slotColor, laneKey))
                        return slot.laneIndex;
                }
            }

            return -1;
        }

        static bool MatchesLaneKey(string slotColor, string expectedLaneKey)
        {
            return string.Equals(
                FortressPadAnchor.NormalizeLaneKey(slotColor),
                expectedLaneKey,
                StringComparison.OrdinalIgnoreCase);
        }

        static string InferLaneKeyFromHierarchy(Transform source)
        {
            for (var current = source; current != null; current = current.parent)
            {
                string laneKey = InferLaneKeyFromName(current.name);
                if (!string.IsNullOrWhiteSpace(laneKey))
                    return laneKey;
            }

            return string.Empty;
        }

        static string InferLaneKeyFromName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim().ToLowerInvariant();
            if (normalized.Contains("yellow") || normalized.Contains("gold"))
                return "yellow";
            if (normalized.Contains("green"))
                return "green";
            if (normalized.Contains("blue"))
                return "blue";
            if (normalized.Contains("red"))
                return "red";

            return string.Empty;
        }
    }
}
