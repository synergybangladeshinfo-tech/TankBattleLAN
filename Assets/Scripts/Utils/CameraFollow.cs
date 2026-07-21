using UnityEngine;

namespace TankBattle.Utils
{
    /// <summary>
    /// Smooth third-person chase camera with cinematic shake. Sits behind and
    /// above the local tank, following its yaw so pushing the joystick up always
    /// drives "into" the screen. Uses frame-rate-independent exponential
    /// smoothing, and adds a decaying positional shake on firing / explosions
    /// (distance-scaled) for weight and impact.
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        public static CameraFollow Instance { get; private set; }

        [SerializeField] float distance = 9f;    // metres behind the tank
        [SerializeField] float height = 5.5f;    // metres above the tank
        [SerializeField] float positionLerp = 6f;
        [SerializeField] float lookHeight = 1.2f;

        Transform _target;
        float _shake;          // current shake magnitude (decays)
        Vector3 _shakeOffset;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetTarget(Transform target)
        {
            _target = target;
            if (_target != null) SnapToTarget();
        }

        /// <summary>Add a burst of camera shake (e.g. own weapon fire).</summary>
        public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

        /// <summary>Shake scaled by how close the source is to the camera.</summary>
        public void ShakeAt(Vector3 worldPos, float amount)
        {
            float d = Vector3.Distance(transform.position, worldPos);
            float falloff = Mathf.Clamp01(1f - d / 45f);
            if (falloff > 0f) Shake(amount * falloff);
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
            Vector3 basePos = Vector3.Lerp(transform.position - _shakeOffset, DesiredPosition(), t);

            // Decaying random shake offset.
            if (_shake > 0.001f)
            {
                _shakeOffset = new Vector3(
                    Random.value * 2f - 1f,
                    Random.value * 2f - 1f,
                    Random.value * 2f - 1f) * _shake;
                _shake = Mathf.Lerp(_shake, 0f, 1f - Mathf.Exp(-9f * Time.deltaTime));
            }
            else _shakeOffset = Vector3.zero;

            transform.position = basePos + _shakeOffset;
            transform.LookAt(_target.position + Vector3.up * lookHeight);
        }

        Vector3 DesiredPosition() =>
            _target.position - _target.forward * distance + Vector3.up * height;
    }
}
