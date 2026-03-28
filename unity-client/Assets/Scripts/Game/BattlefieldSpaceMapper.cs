using UnityEngine;

namespace CastleDefender.Game
{
    /// <summary>
    /// Authoritative lane-space mapper for the multilane battlefield.
    /// Runtime presentation, camera framing, and projectile systems should
    /// resolve lane geometry here instead of depending on TileGrid.
    /// </summary>
    public static class BattlefieldSpaceMapper
    {
        public const int LaneCount = 4;
        public const int LaneCols = 11;
        public const int LaneRows = 28;
        public const float TileW = 1f;
        public const float TileH = 1f;
        const float LaneTileSurfaceY = 2.54f;

        readonly struct LaneConfig
        {
            public readonly Vector3 origin;
            public readonly Vector3 colDir;
            public readonly Vector3 rowDir;

            public LaneConfig(Vector3 origin, Vector3 colDir, Vector3 rowDir)
            {
                this.origin = origin;
                this.colDir = colDir;
                this.rowDir = rowDir;
            }
        }

        static readonly LaneConfig[] LaneConfigs =
        {
            new(new Vector3( 14f, LaneTileSurfaceY, -5f), new Vector3( 0f, 0f,  1f), new Vector3( 1f, 0f,  0f)),
            new(new Vector3(-14f, LaneTileSurfaceY,  5f), new Vector3( 0f, 0f, -1f), new Vector3(-1f, 0f,  0f)),
            new(new Vector3( -5f, LaneTileSurfaceY,-14f), new Vector3( 1f, 0f,  0f), new Vector3( 0f, 0f, -1f)),
            new(new Vector3(  5f, LaneTileSurfaceY, 14f), new Vector3(-1f, 0f,  0f), new Vector3( 0f, 0f,  1f)),
        };

        static readonly Vector3[][] LanePathWaypoints =
        {
            new[] { new Vector3(  0f, 1f,  0f), new Vector3(  0f, 1f, 14f), new Vector3(-24f, 1f, 14f), new Vector3(-38f, 1f,  8f), new Vector3(-54f, 1f, 0f), new Vector3(-72f, 1f, 0f) },
            new[] { new Vector3(  0f, 1f,  0f), new Vector3(  0f, 1f,-14f), new Vector3(-24f, 1f,-14f), new Vector3(-38f, 1f, -8f), new Vector3(-54f, 1f, 0f), new Vector3(-72f, 1f, 0f) },
            new[] { new Vector3(  0f, 1f,  0f), new Vector3(  0f, 1f, 14f), new Vector3( 24f, 1f, 14f), new Vector3( 38f, 1f,  8f), new Vector3( 54f, 1f, 0f), new Vector3( 72f, 1f, 0f) },
            new[] { new Vector3(  0f, 1f,  0f), new Vector3(  0f, 1f,-14f), new Vector3( 24f, 1f,-14f), new Vector3( 38f, 1f, -8f), new Vector3( 54f, 1f, 0f), new Vector3( 72f, 1f, 0f) },
        };

        public static bool IsValidLaneIndex(int laneIndex)
        {
            return (uint)laneIndex < (uint)LaneConfigs.Length;
        }

        public static Vector3 TileToWorld(int laneIndex, int col, int row)
        {
            if (IsValidLaneIndex(laneIndex))
            {
                LaneConfig config = LaneConfigs[laneIndex];
                return config.origin + config.colDir * (col * TileW) + config.rowDir * (row * TileH);
            }

            return TileToWorld(col, row);
        }

        public static Vector3 TileToWorld(int laneIndex, float col, float row)
        {
            if (IsValidLaneIndex(laneIndex))
            {
                LaneConfig config = LaneConfigs[laneIndex];
                return config.origin + config.colDir * (col * TileW) + config.rowDir * (row * TileH);
            }

            return new Vector3(col * TileW, 0f, row * TileH);
        }

        public static Vector3 TileToWorld(int col, int row)
        {
            return new Vector3(col * TileW, 0f, row * TileH);
        }

        public static int GetBranchConfigIndex(string branchId)
        {
            switch (branchId)
            {
                case "left_branch_a": return 0;
                case "left_branch_b": return 1;
                case "right_branch_a": return 2;
                case "right_branch_b": return 3;
                default: return -1;
            }
        }

        public static Vector3 GetLaneForwardDir(int laneIndex)
        {
            return IsValidLaneIndex(laneIndex)
                ? LaneConfigs[laneIndex].rowDir
                : Vector3.forward;
        }

        public static Vector3 GetLaneLateralDir(int laneIndex)
        {
            return IsValidLaneIndex(laneIndex)
                ? LaneConfigs[laneIndex].colDir
                : Vector3.right;
        }

        public static Vector3[] GetLanePathWaypoints(int laneIndex)
        {
            if (IsValidLaneIndex(laneIndex))
                return LanePathWaypoints[laneIndex];

            return LanePathWaypoints[0];
        }

        public static Vector3 NormProgressToWorld(int laneIndex, float normProgress)
        {
            return SamplePolyline(GetLanePathWaypoints(laneIndex), Mathf.Clamp01(normProgress), 0);
        }

        public static Vector3 SuffixProgressToWorld(int laneIndex, float suffixProgress)
        {
            return SamplePolyline(GetLanePathWaypoints(laneIndex), Mathf.Clamp01(suffixProgress), 2);
        }

        public static bool TryWorldToTile(int laneIndex, Vector3 worldPos, out int col, out int row)
        {
            return TryWorldToTile(laneIndex, worldPos, LaneCols, LaneRows, out col, out row);
        }

        public static bool TryWorldToTile(int laneIndex, Vector3 worldPos, int cols, int rows, out int col, out int row)
        {
            col = -1;
            row = -1;
            if (!IsValidLaneIndex(laneIndex))
                return false;

            LaneConfig config = LaneConfigs[laneIndex];
            Vector3 local = worldPos - config.origin;
            col = Mathf.RoundToInt(Vector3.Dot(local, config.colDir) / TileW);
            row = Mathf.RoundToInt(Vector3.Dot(local, config.rowDir) / TileH);
            return col >= 0 && col < cols && row >= 0 && row < rows;
        }

        static Vector3 SamplePolyline(Vector3[] points, float normalizedProgress, int startIndex)
        {
            if (points == null || points.Length == 0)
                return Vector3.zero;

            int safeStartIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, points.Length - 1));
            if (safeStartIndex >= points.Length - 1)
                return points[points.Length - 1];

            float totalLength = 0f;
            for (int i = safeStartIndex; i < points.Length - 1; i++)
                totalLength += Vector3.Distance(points[i], points[i + 1]);

            float targetDistance = normalizedProgress * totalLength;
            float walkedDistance = 0f;

            for (int i = safeStartIndex; i < points.Length - 1; i++)
            {
                float segmentLength = Vector3.Distance(points[i], points[i + 1]);
                if (walkedDistance + segmentLength >= targetDistance)
                {
                    float segmentT = segmentLength > 0f ? (targetDistance - walkedDistance) / segmentLength : 0f;
                    return Vector3.Lerp(points[i], points[i + 1], segmentT);
                }

                walkedDistance += segmentLength;
            }

            return points[points.Length - 1];
        }
    }
}
