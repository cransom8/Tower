using UnityEngine;

namespace CastleDefender.Game
{
    public class UnitAnimationBinding : MonoBehaviour
    {
        [Header("Profile Override")]
        public UnitAnimationProfile profile;

        [Tooltip("Optional human-readable id for debugging before a concrete profile is assigned.")]
        public string profileId;

        [Header("Role Override")]
        public UnitAnimationAttackFamily attackFamilyOverride = UnitAnimationAttackFamily.Unspecified;

        [Header("Controller Overrides")]
        public RuntimeAnimatorController runtimeControllerOverride;
        public RuntimeAnimatorController portraitControllerOverride;
        public bool overrideExistingControllers;

        [Header("Animator Settings")]
        public bool applyRootMotion;
        [Min(0.05f)] public float animatorSpeedMultiplier = 1f;

        [TextArea(2, 5)]
        public string notes;
    }
}
