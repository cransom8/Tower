using UnityEngine;

namespace CastleDefender.Game
{
    /// Snapshot presenters do not run the original prefab-side combat/audio scripts,
    /// so we swallow legacy clip events here instead of spamming the console every frame.
    public sealed class SnapshotAnimationEventRelay : MonoBehaviour
    {
        public void FootL()
        {
        }

        public void FootR()
        {
        }

        public void Hit()
        {
        }
    }
}
