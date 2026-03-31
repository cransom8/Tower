using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class BarracksLanePath : MonoBehaviour
    {
        public Transform spawnPoint;
        public List<Transform> markerTransforms = new List<Transform>();
        public bool drawGizmos = true;
        public Color gizmoColor = new Color(0.25f, 0.9f, 0.35f, 1f);

        public IReadOnlyList<Transform> Markers => markerTransforms;

        public int GetWaypointCount()
        {
            return (spawnPoint != null ? 1 : 0) + (markerTransforms != null ? markerTransforms.Count : 0);
        }

        public bool TryGetWaypoint(int index, out Transform waypoint)
        {
            waypoint = null;
            if (index < 0)
                return false;

            if (index == 0 && spawnPoint != null)
            {
                waypoint = spawnPoint;
                return true;
            }

            int markerIndex = spawnPoint != null ? index - 1 : index;
            if (markerTransforms == null || markerIndex < 0 || markerIndex >= markerTransforms.Count)
                return false;

            waypoint = markerTransforms[markerIndex];
            return waypoint != null;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos)
                return;

            var previous = spawnPoint != null ? spawnPoint : transform;
            if (previous == null)
                return;

            Gizmos.color = gizmoColor;
            Gizmos.DrawSphere(previous.position, 0.35f);

            if (markerTransforms == null)
                return;

            for (int i = 0; i < markerTransforms.Count; i++)
            {
                var marker = markerTransforms[i];
                if (marker == null)
                    continue;

                Gizmos.DrawSphere(marker.position, 0.25f);
                Gizmos.DrawLine(previous.position, marker.position);
                previous = marker;
            }
        }
    }
}
