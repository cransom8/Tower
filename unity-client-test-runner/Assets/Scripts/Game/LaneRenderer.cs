using CastleDefender.Net;
using UnityEngine;

namespace CastleDefender.Game
{
    /// <summary>
    /// Lightweight scene anchor for shared unit presentation dependencies.
    /// Live ML unit materialization now happens only through WaveSnapshotRuntimeSpawner.
    /// </summary>
    public sealed class LaneRenderer : MonoBehaviour
    {
        [Header("Unit prefab registry (key -> prefab)")]
        public UnitPrefabRegistry Registry;

        [Header("HP bar prefab (optional WorldSpace Canvas Image)")]
        public GameObject HpBarPrefab;

        public static bool DebugLogCadence;

        // Compatibility hook for editor tools that still reflect this method.
        void OnSnapshot(MLSnapshot _)
        {
        }
    }
}
