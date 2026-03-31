using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [CreateAssetMenu(fileName = "BuildingVisualCatalog", menuName = "Castle Defender/Buildings/Visual Catalog")]
    public sealed class BuildingVisualCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class ChainEntry
        {
            public string key;
            public string buildingType;
            public string displayName;
            public int maxTier = 1;
            public GameObject prefab;
            public string[] cardIds = Array.Empty<string>();
            public string[] portraitResourcePaths = Array.Empty<string>();
        }

        [SerializeField] ChainEntry[] entries = Array.Empty<ChainEntry>();

        readonly Dictionary<string, ChainEntry> _entriesByBuildingType = new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _portraitPathByCardId = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ChainEntry> Entries => entries;

        void OnEnable()
        {
            RebuildLookup();
        }

        void OnValidate()
        {
            RebuildLookup();
        }

        public void ConfigureForEditor(ChainEntry[] configuredEntries)
        {
            entries = configuredEntries ?? Array.Empty<ChainEntry>();
            RebuildLookup();
        }

        public static BuildingVisualCatalog LoadGenerated()
        {
            return Resources.Load<BuildingVisualCatalog>("Generated/BuildingVisualCatalog");
        }

        public bool TryGetByBuildingType(string buildingType, out ChainEntry entry)
        {
            if (string.IsNullOrWhiteSpace(buildingType))
            {
                entry = null;
                return false;
            }

            RebuildLookup();
            return _entriesByBuildingType.TryGetValue(buildingType.Trim(), out entry);
        }

        public bool TryGetPortraitPath(string cardId, out string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(cardId))
            {
                resourcePath = null;
                return false;
            }

            RebuildLookup();
            return _portraitPathByCardId.TryGetValue(cardId.Trim(), out resourcePath);
        }

        void RebuildLookup()
        {
            _entriesByBuildingType.Clear();
            _portraitPathByCardId.Clear();

            if (entries == null)
                return;

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(entry.buildingType))
                    _entriesByBuildingType[entry.buildingType.Trim()] = entry;

                int linkCount = Mathf.Min(
                    entry.cardIds != null ? entry.cardIds.Length : 0,
                    entry.portraitResourcePaths != null ? entry.portraitResourcePaths.Length : 0);
                for (int linkIndex = 0; linkIndex < linkCount; linkIndex++)
                {
                    string cardId = entry.cardIds[linkIndex]?.Trim();
                    string path = entry.portraitResourcePaths[linkIndex]?.Trim();
                    if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(path))
                        continue;

                    _portraitPathByCardId[cardId] = path;
                }
            }
        }
    }
}
