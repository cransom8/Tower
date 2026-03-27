using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    public enum BarracksUnitRole
    {
        Frontline = 0,
        Ranged = 1,
        Support = 2,
        Siege = 3,
    }

    [DisallowMultipleComponent]
    public sealed class BarracksAutoSpawner : MonoBehaviour
    {
        [Serializable]
        public sealed class BarracksRosterEntry
        {
            public string displayName;
            public string unitTypeKey;
            public string skinKey;
            public BarracksUnitRole role;
            public int ownedCount;
            public string spawnedUnitName;
        }

        public BarracksLanePath barracksPath;
        public BattleTeam team = BattleTeam.Red;
        public List<BarracksRosterEntry> roster = new List<BarracksRosterEntry>();
        public float spawnIntervalSeconds = 15f;
        public float moveSpeed = 8f;
        public float formationLateralSpacing = 2.6f;
        public float formationRowSpacing = 3.4f;

        // Legacy single-unit prototype fields kept so existing prefab data deserializes cleanly.
        public string unitTypeKey;
        public string skinKey;
        public string spawnedUnitName;
        public int purchasedArcherCount;
        public bool legacyPrototypeMigrated = true;

        public IReadOnlyList<BarracksRosterEntry> Roster => roster;

        public bool HasPath()
        {
            return barracksPath != null && barracksPath.GetWaypointCount() > 0;
        }
    }
}
