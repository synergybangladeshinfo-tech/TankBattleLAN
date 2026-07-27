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
        float _zoom = 1f;         // 1 = normal, >1 = pulled back (sniper)
        float _zoomTarget = 1f;

        /// <summary>
        /// Pull the camera back (or return it). 1 = default chase distance,
        /// ~1.65 while sniping so the long shots are actually aimable.
        /// </summary>
        public void SetZoom(float multiplier) => _zoomTarget = Mathf.Clamp(multiplier, 0.7f, 2.5f);

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void SetTarget(Transform target)
        {
            _target = target;
            _watchUntil = 0f;          // a fresh target cancels any death cam
            _watchTarget = null;
            if (_target != null) SnapToTarget();
        }

        Transform _watchTarget;    // whoever killed you
        float _watchUntil;

        /// <summary>
        /// Point the camera at the tank that just killed you for a moment, then
        /// hand control back to your own tank. A cheap "so THAT is where it came
        /// from" moment without the cost of recording a full replay.
        /// </summary>
        public void WatchKiller(Transform killer, float seconds)
        {
            if (killer == null) return;
            _watchTarget = killer;
            _watchUntil = Time.time + seconds;
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
            // Death cam takes over briefly, then expires on its own.
            Transform focus = _target;
            if (_watchTarget != null && Time.time < _watchUntil) focus = _watchTarget;
            else if (_watchTarget != null) _watchTarget = null;

            if (focus == null) return;
            _activeFocus = focus;

            // Smooth zoom so switching to the sniper glides instead of snapping.
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-7f * Time.deltaTime));

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
            transform.LookAt(focus.position + Vector3.up * lookHeight);
        }

        Transform _activeFocus;

        Vector3 DesiredPosition()
        {
            var t = _activeFocus != null ? _activeFocus : _target;
            if (t == null) return transform.position;
            return t.position - t.forward * (distance * _zoom)
                              + Vector3.up * (height * Mathf.Lerp(1f, 1.35f, _zoom - 1f));
        }
    }
}
