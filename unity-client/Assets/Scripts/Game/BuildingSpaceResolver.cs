using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    static class BuildingSpaceResolver
    {
        const float PushOutEpsilon = 0.05f;
        const int MaxPushPasses = 4;

        static readonly List<FortressPadAnchor> s_anchorScratch = new();
        static readonly List<BarracksSiteView> s_barracksScratch = new();
        static readonly List<BoxCollider> s_blockerScratch = new();
        static int s_cachedFrame = -1;

        public static Vector3 ConstrainGroundPosition(Vector3 position, float radius, Vector3 preferredDirection)
        {
            if (radius <= 0f)
                return position;

            RebuildCacheIfNeeded();
            if (s_blockerScratch.Count == 0)
                return position;

            Vector3 resolved = position;
            for (int pass = 0; pass < MaxPushPasses; pass++)
            {
                bool changed = false;
                for (int i = 0; i < s_blockerScratch.Count; i++)
                {
                    var blocker = s_blockerScratch[i];
                    if (blocker == null || !blocker.gameObject.activeInHierarchy)
                        continue;

                    if (!TryPushOutOfBox(blocker, resolved, radius, preferredDirection, out Vector3 pushed))
                        continue;

                    resolved = pushed;
                    changed = true;
                }

                if (!changed)
                    break;
            }

            resolved.y = position.y;
            return resolved;
        }

        static void RebuildCacheIfNeeded()
        {
            if (Application.isPlaying && Time.frameCount == s_cachedFrame)
                return;

            s_cachedFrame = Application.isPlaying ? Time.frameCount : -1;
            s_blockerScratch.Clear();

            FortressPadAnchor.CollectAnchors(s_anchorScratch);
            for (int i = 0; i < s_anchorScratch.Count; i++)
            {
                var collider = s_anchorScratch[i]?.EnsureInteractionCollider() as BoxCollider;
                if (collider != null && collider.gameObject.activeInHierarchy)
                    s_blockerScratch.Add(collider);
            }

            BarracksSiteView.CollectSites(s_barracksScratch);
            for (int i = 0; i < s_barracksScratch.Count; i++)
            {
                var collider = s_barracksScratch[i]?.EnsureInteractionCollider() as BoxCollider;
                if (collider != null && collider.gameObject.activeInHierarchy)
                    s_blockerScratch.Add(collider);
            }
        }

        static bool TryPushOutOfBox(
            BoxCollider box,
            Vector3 position,
            float radius,
            Vector3 preferredDirection,
            out Vector3 pushed)
        {
            pushed = position;

            var boxTransform = box.transform;
            Vector3 local = boxTransform.InverseTransformPoint(position) - box.center;
            float halfX = Mathf.Max(0.01f, box.size.x * 0.5f + radius);
            float halfZ = Mathf.Max(0.01f, box.size.z * 0.5f + radius);

            if (Mathf.Abs(local.x) > halfX || Mathf.Abs(local.z) > halfZ)
                return false;

            float minX = Mathf.Min(halfX - local.x, halfX + local.x);
            float minZ = Mathf.Min(halfZ - local.z, halfZ + local.z);
            bool useX = minX <= minZ;

            Vector3 preferredLocal = boxTransform.InverseTransformDirection(preferredDirection);
            if (preferredLocal.sqrMagnitude > 0.0001f)
            {
                bool prefersX = Mathf.Abs(preferredLocal.x) >= Mathf.Abs(preferredLocal.z);
                float preferredDistance = prefersX ? minX : minZ;
                float otherDistance = prefersX ? minZ : minX;
                if (preferredDistance <= otherDistance + radius * 0.35f)
                    useX = prefersX;
            }

            if (useX)
            {
                float sign = Mathf.Abs(preferredLocal.x) > 0.001f ? Mathf.Sign(preferredLocal.x) : Mathf.Sign(local.x);
                if (Mathf.Abs(sign) < 0.001f)
                    sign = 1f;

                local.x = sign >= 0f ? halfX + PushOutEpsilon : -(halfX + PushOutEpsilon);
            }
            else
            {
                float sign = Mathf.Abs(preferredLocal.z) > 0.001f ? Mathf.Sign(preferredLocal.z) : Mathf.Sign(local.z);
                if (Mathf.Abs(sign) < 0.001f)
                    sign = 1f;

                local.z = sign >= 0f ? halfZ + PushOutEpsilon : -(halfZ + PushOutEpsilon);
            }

            pushed = boxTransform.TransformPoint(local + box.center);
            pushed.y = position.y;
            return true;
        }
    }
}
