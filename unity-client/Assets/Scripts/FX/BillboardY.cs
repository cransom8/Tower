using UnityEngine;

namespace CastleDefender.FX
{
    /// <summary>
    /// Rotates a flat visual to fully face the camera.
    /// Used by world-space health bars so they stay readable from the gameplay camera.
    /// </summary>
    public class BillboardY : MonoBehaviour
    {
        Camera _cam;

        void LateUpdate()
        {
            if (_cam == null)
                _cam = Camera.main;
            if (_cam == null)
                return;

            Vector3 toCam = _cam.transform.position - transform.position;
            if (toCam.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation((-toCam).normalized, _cam.transform.up);
        }
    }
}
