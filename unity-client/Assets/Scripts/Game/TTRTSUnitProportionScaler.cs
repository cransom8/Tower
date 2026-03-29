using UnityEngine;

namespace CastleDefender.Game
{
    public sealed class TTRTSUnitProportionScaler : MonoBehaviour
    {
        [SerializeField, Range(1f, 1.25f)] float spineStretch = 1.08f;
        [SerializeField, Range(1f, 1.2f)] float upperArmStretch = 1.07f;
        [SerializeField, Range(1f, 1.2f)] float forearmStretch = 1.06f;
        [SerializeField, Range(1f, 1.2f)] float thighStretch = 1.10f;
        [SerializeField, Range(1f, 1.2f)] float calfStretch = 1.08f;
        [SerializeField, Range(1f, 1.1f)] float neckStretch = 1.02f;

        bool _applied;

        void Awake() => ApplyIfNeeded();
        void OnEnable() => ApplyIfNeeded();

        void ApplyIfNeeded()
        {
            if (_applied)
                return;

            Stretch("Bip001 Spine", spineStretch);
            Stretch("Bip001 Neck", neckStretch);
            Stretch("Bip001 L UpperArm", upperArmStretch);
            Stretch("Bip001 R UpperArm", upperArmStretch);
            Stretch("Bip001 L Forearm", forearmStretch);
            Stretch("Bip001 R Forearm", forearmStretch);
            Stretch("Bip001 L Thigh", thighStretch);
            Stretch("Bip001 R Thigh", thighStretch);
            Stretch("Bip001 L Calf", calfStretch);
            Stretch("Bip001 R Calf", calfStretch);

            _applied = true;
        }

        void Stretch(string boneName, float axisStretch)
        {
            var bone = FindDeepChild(transform, boneName);
            if (bone == null)
                return;

            var scale = bone.localScale;
            scale.x *= axisStretch;
            bone.localScale = scale;
        }

        static Transform FindDeepChild(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName))
                return null;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var child = transforms[i];
                if (child != null && string.Equals(child.name, targetName, System.StringComparison.Ordinal))
                    return child;
            }

            return null;
        }
    }
}
