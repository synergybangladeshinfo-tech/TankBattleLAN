using UnityEngine;

namespace TankBattle.Utils
{
    /// <summary>
    /// Keeps a world-space element (overhead health bar) facing the camera.
    /// </summary>
    public class Billboard : MonoBehaviour
    {
        Camera _cam;

        void LateUpdate()
        {
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;
            transform.rotation = _cam.transform.rotation;
        }
    }
}
