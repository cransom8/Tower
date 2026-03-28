using UnityEngine;

namespace CastleDefender.Game
{
    /// <summary>
    /// Scene anchor for shared gameplay-presentation dependencies.
    /// Live ML unit materialization happens through WaveSnapshotRuntimeSpawner;
    /// this root only carries shared presentation assets for the active battlefield.
    /// </summary>
    public sealed class GameplayPresentationRoot : MonoBehaviour
    {
        [Header("Unit prefab registry (key -> prefab)")]
        public UnitPrefabRegistry Registry;

        [Header("HP bar prefab (optional WorldSpace Canvas Image)")]
        public GameObject HpBarPrefab;

        public static GameplayPresentationRoot FindActive()
        {
            return Object.FindFirstObjectByType<GameplayPresentationRoot>();
        }

        public static UnitPrefabRegistry ResolveRegistry(UnitPrefabRegistry preferred = null)
        {
            if (preferred != null)
                return preferred;

            var root = FindActive();
            if (root != null && root.Registry != null)
                return root.Registry;

            var waveRuntime = Object.FindFirstObjectByType<WaveSnapshotRuntimeSpawner>();
            if (waveRuntime != null && waveRuntime.Registry != null)
                return waveRuntime.Registry;

            var tileGrid = Object.FindFirstObjectByType<TileGrid>();
            if (tileGrid != null && tileGrid.Registry != null)
                return tileGrid.Registry;

            return null;
        }

        public static GameObject ResolveHpBarPrefab(GameObject preferred = null)
        {
            if (preferred != null)
                return preferred;

            var root = FindActive();
            if (root != null && root.HpBarPrefab != null)
                return root.HpBarPrefab;

            var waveRuntime = Object.FindFirstObjectByType<WaveSnapshotRuntimeSpawner>();
            if (waveRuntime != null && waveRuntime.HpBarPrefab != null)
                return waveRuntime.HpBarPrefab;

            var tileGrid = Object.FindFirstObjectByType<TileGrid>();
            if (tileGrid != null && tileGrid.HpBarPrefab != null)
                return tileGrid.HpBarPrefab;

            return null;
        }
    }
}
