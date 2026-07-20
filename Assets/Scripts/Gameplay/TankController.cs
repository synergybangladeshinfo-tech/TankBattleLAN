using Unity.Netcode;
using UnityEngine;
using TankBattle.Core;
using TankBattle.UI;
using TankBattle.Utils;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Tank-style movement driven by the on-screen virtual joystick:
    ///   joystick Y = throttle (forward/backward)
    ///   joystick X = hull rotation
    /// Movement runs only on the owning client (owner-authoritative transform
    /// via ClientNetworkTransform) for zero-latency touch controls.
    /// Also applies the per-player color that the server assigns at spawn.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class TankController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] float moveSpeed = 7f;        // m/s forward
        [SerializeField] float reverseSpeed = 4f;     // m/s backward
        [SerializeField] float turnSpeed = 130f;      // degrees/s
        [SerializeField] float gravity = 25f;

        /// <summary>Color slot assigned by the server when the tank is spawned.</summary>
        public NetworkVariable<int> ColorIndex = new NetworkVariable<int>(-1);

        CharacterController _cc;
        TankHealth _health;
        float _verticalVelocity;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<TankHealth>();
        }

        public override void OnNetworkSpawn()
        {
            // Tint the tank with the owner's color (now and on late change).
            ApplyColor(ColorIndex.Value);
            ColorIndex.OnValueChanged += (_, v) => ApplyColor(v);

            if (IsOwner)
            {
                // Attach the scene camera and the HUD to the local tank.
                var cam = Object.FindFirstObjectByType<CameraFollow>();
                if (cam != null) cam.SetTarget(transform);
                if (HUDController.Instance != null) HUDController.Instance.BindLocalTank(this);
            }
        }

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;

            // Frozen while dead or after the match has ended.
            bool frozen = (_health != null && _health.IsDead.Value) ||
                          (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value);

            Vector2 input = frozen ? Vector2.zero : ReadInput();

            // Hull rotation.
            transform.Rotate(0f, input.x * turnSpeed * Time.deltaTime, 0f);

            // Throttle along the hull's forward axis.
            float speed = input.y >= 0f ? moveSpeed : reverseSpeed;
            Vector3 motion = transform.forward * (input.y * speed);

            // Simple gravity so tanks stick to ramps and the ground.
            if (_cc.isGrounded) _verticalVelocity = -1f;
            else _verticalVelocity -= gravity * Time.deltaTime;
            motion.y = _verticalVelocity;

            _cc.Move(motion * Time.deltaTime);
        }

        /// <summary>Joystick on device; WASD/arrows as an editor convenience.</summary>
        Vector2 ReadInput()
        {
            if (HUDController.Instance != null && HUDController.Instance.Joystick != null)
            {
                Vector2 j = HUDController.Instance.Joystick.Direction;
                if (j.sqrMagnitude > 0.01f) return j;
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            return new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
#else
            return Vector2.zero;
#endif
        }

        /// <summary>Tint every mesh except the health bar with the player color.</summary>
        void ApplyColor(int index)
        {
            if (index < 0) return;
            Color c = GameConstants.GetPlayerColor(index);
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<Billboard>() != null) continue; // skip health bar
                r.material.color = c;
            }
        }

        /// <summary>
        /// Owner-side teleport used for respawning. The CharacterController must
        /// be disabled while moving the transform or it will override the change.
        /// </summary>
        public void TeleportLocal(Vector3 position, Quaternion rotation)
        {
            _cc.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            _verticalVelocity = 0f;
            _cc.enabled = true;
        }
    }
}
