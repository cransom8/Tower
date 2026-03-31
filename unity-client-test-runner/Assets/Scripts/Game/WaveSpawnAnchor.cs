using System;
using System.Collections.Generic;
using UnityEngine;

namespace CastleDefender.Game
{
    [DisallowMultipleComponent]
    public sealed class WaveSpawnAnchor : MonoBehaviour
    {
        const string DefaultAnchorId = "mine_center";

        [Header("Identity")]
        [SerializeField] string anchorId = DefaultAnchorId;
        [SerializeField] Transform focusTransform;

        static readonly List<WaveSpawnAnchor> s_activeAnchors = new();

        public string AnchorId => NormalizeAnchorId(anchorId);
        public Transform FocusTransform => focusTransform != null ? focusTransform : transform;

        void OnEnable()
        {
            if (!s_activeAnchors.Contains(this))
                s_activeAnchors.Add(this);
        }

        void OnDisable()
        {
            s_activeAnchors.Remove(this);
        }

        void OnValidate()
        {
            anchorId = NormalizeAnchorId(anchorId);
        }

        public static WaveSpawnAnchor FindAnchor(string requestedAnchorId)
        {
            string normalizedId = NormalizeAnchorId(requestedAnchorId);
            for (int i = 0; i < s_activeAnchors.Count; i++)
            {
                var anchor = s_activeAnchors[i];
                if (anchor == null || !anchor.isActiveAndEnabled)
                    continue;

                if (string.Equals(anchor.AnchorId, normalizedId, StringComparison.OrdinalIgnoreCase))
                    return anchor;
            }

            return null;
        }

        public static string NormalizeAnchorId(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? DefaultAnchorId
                : value.Trim().ToLowerInvariant();
        }
    }
}
