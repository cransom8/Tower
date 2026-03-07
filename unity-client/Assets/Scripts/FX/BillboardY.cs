using UnityEngine;

namespace CastleDefender.FX
{
    /// <summary>
    /// Rotates a flat visual to face the camera on the Y axis only.
    /// Keeps the sprite upright while matching camera yaw.
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
            toCam.y = 0f;
            if (toCam.sqrMagnitude < 0.0001f)
                return;

            // Quad forward needs inversion in this scene setup so art faces the camera.
            transform.rotation = Quaternion.LookRotation((-toCam).normalized, Vector3.up);
        }
    }
}
