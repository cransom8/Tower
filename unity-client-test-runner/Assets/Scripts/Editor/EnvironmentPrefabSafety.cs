using System;
using System.Collections.Generic;
using System.Linq;
using CastleDefender.Game;
using UnityEngine;

namespace CastleDefender.Editor
{
    static class EnvironmentPrefabSafety
    {
        static readonly string[] RequiredCriticalPadIds =
        {
            "town_core_pad",
            "barracks_pad",
            "archery_tower_pad",
            "wizard_tower_pad",
            "blacksmith_pad",
            "temple_pad",
        };

        public static void AssertValidCriticalEnvironmentRoot(GameObject root, string sourceLabel)
        {
            if (root == null)
                throw new InvalidOperationException($"Critical environment validation failed because '{sourceLabel}' was null.");

            var anchors = root.GetComponentsInChildren<FortressPadAnchor>(true);
            if (anchors.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to write critical environment from '{sourceLabel}' because it contains no FortressPadAnchor objects. " +
                    "This usually means a partial preview root or broken scene instance is about to overwrite GameEnvironment.prefab.");
            }

            var authoredPadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var anchor in anchors)
            {
                if (!string.IsNullOrWhiteSpace(anchor.PadId))
                    authoredPadIds.Add(anchor.PadId.Trim());
            }

            string[] missingPadIds = RequiredCriticalPadIds
                .Where(requiredPadId => !authoredPadIds.Contains(requiredPadId))
                .ToArray();

            if (missingPadIds.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Refusing to write critical environment from '{sourceLabel}' because it is missing required authored pads: " +
                    $"{string.Join(", ", missingPadIds)}. " +
                    $"Found {anchors.Length} FortressPadAnchor component(s) across {authoredPadIds.Count} unique pad id(s).");
            }
        }
    }
}
