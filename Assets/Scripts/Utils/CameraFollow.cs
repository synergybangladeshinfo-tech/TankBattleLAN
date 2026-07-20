using UnityEngine;

namespace TankBattle.Utils
{
    /// <summary>
    /// Smooth third-person chase camera. Sits behind and above the local tank,
    /// following its yaw so pushing the joystick up always drives "into" the
    /// screen. Uses simple exponential smoothing - cheap and frame-rate safe.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] float distance = 9f;    // metres behind the tank
        [SerializeField] float height = 5.5f;    // metres above the tank
        [SerializeField] float positionLerp = 5f;
        [SerializeField] float lookHeight = 1.2f;

        Transform _target;

        public void SetTarget(Transform target)
        {
            _target = target;
            if (_target != null) SnapToTarget();
        }

        void SnapToTarget()
        {
            transform.position = DesiredPosition();
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }

        void LateUpdate()
        {
            if (_target == null) return;
            float t = 1f - Mathf.Exp(-positionLerp * Time.deltaTime); // fps-independent
            transform.position = Vector3.Lerp(transform.position, DesiredPosition(), t);
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }

        Vector3 DesiredPosition() =>
            _target.position - _target.forward * distance + Vector3.up * height;
    }
}
