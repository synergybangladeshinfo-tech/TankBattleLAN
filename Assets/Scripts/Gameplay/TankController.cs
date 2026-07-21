using Unity.Collections;
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
    /// Also applies the color / body style / name assigned at spawn, and shows
    /// a floating name tag above every remote tank.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class TankController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] float moveSpeed = 7f;        // m/s forward (Standard style)
        [SerializeField] float reverseSpeed = 4f;     // m/s backward
        [SerializeField] float turnSpeed = 130f;      // degrees/s
        [SerializeField] float gravity = 25f;

        /// <summary>Identity assigned by the server when the tank is spawned.</summary>
        public NetworkVariable<int> ColorIndex = new NetworkVariable<int>(-1);
        public NetworkVariable<int> StyleIndex = new NetworkVariable<int>(0);
        public NetworkVariable<int> TeamIndex = new NetworkVariable<int>(-1);
        public NetworkVariable<FixedString32Bytes> PlayerName =
            new NetworkVariable<FixedString32Bytes>(new FixedString32Bytes(""));

        CharacterController _cc;
        TankHealth _health;
        float _verticalVelocity;
        TextMesh _nameTag;
        bool _localBound; // camera + HUD attached (retried until the scene is ready)
        bool _isBot;      // cached: AI-driven tank (server drives it, not input)

        ParticleSystem _dust;   // track dust, emission driven by movement speed
        Vector3 _lastDustPos;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<TankHealth>();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name == "DustPS") { _dust = ps; break; }
            _lastDustPos = transform.position;
        }

        /// <summary>
        /// Kick up dust while driving - runs on EVERY peer by measuring how far
        /// the (replicated) transform moved, so remote tanks smoke up too.
        /// </summary>
        void LateUpdate()
        {
            if (_dust == null) return;
            Vector3 delta = transform.position - _lastDustPos;
            _lastDustPos = transform.position;
            delta.y = 0f;
            float speed = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;

            var emission = _dust.emission;
            bool moving = speed > 1.2f && (_health == null || !_health.IsDead.Value);
            emission.rateOverTime = moving ? Mathf.Min(28f, speed * 3.5f) : 0f;
        }

        public override void OnNetworkSpawn()
        {
            _isBot = GetComponent<BotTank>() != null;
            ApplyStyle(StyleIndex.Value);
            ApplyColor(ColorIndex.Value);
            StyleIndex.OnValueChanged += (_, v) => { ApplyStyle(v); ApplyColor(ColorIndex.Value); };
            ColorIndex.OnValueChanged += (_, v) => ApplyColor(v);
            PlayerName.OnValueChanged += (_, v) => { if (_nameTag != null) _nameTag.text = v.ToString(); };

            if (IsOwner)
            {
                TryBindLocal();
            }
            else
            {
                CreateNameTag();
            }

            // Bots are server-owned, so the host would otherwise skip their tag.
            if (_isBot && _nameTag == null && IsOwner) CreateNameTag();
        }

        /// <summary>
        /// Attach the scene camera and HUD to the local tank. Retried from
        /// Update because on joining clients the tank can spawn a moment
        /// before the map scene (camera/HUD) has finished loading.
        /// </summary>
        void TryBindLocal()
        {
            if (_isBot) { _localBound = true; return; }

            var cam = Object.FindFirstObjectByType<CameraFollow>();
            if (cam != null) cam.SetTarget(transform);
            if (HUDController.Instance != null) HUDController.Instance.BindLocalTank(this);
            _localBound = cam != null && HUDController.Instance != null;
        }

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            if (_isBot) return; // AI drives bot tanks (see BotTank)

            // Late scene load on joiners: keep trying until camera + HUD exist.
            if (!_localBound) TryBindLocal();

            // Frozen while dead or after the match has ended.
            bool frozen = (_health != null && _health.IsDead.Value) ||
                          (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value);

            Vector2 input = frozen ? Vector2.zero : ReadInput();

            // Small, fair per-style speed differences (Heavy slower, Scout faster).
            Vector2 mult = GameConstants.TankStyleSpeed[
                Mathf.Clamp(StyleIndex.Value, 0, GameConstants.TankStyleSpeed.Length - 1)];

            // Hull rotation.
            transform.Rotate(0f, input.x * turnSpeed * mult.y * Time.deltaTime, 0f);

            // Throttle along the hull's forward axis.
            float speed = (input.y >= 0f ? moveSpeed : reverseSpeed) * mult.x;
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
                if (j.sqrMagnitude > 0.0001f) return j;
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            return new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
#else
            return Vector2.zero;
#endif
        }

        /// <summary>Enable only the hull matching the chosen body style.</summary>
        void ApplyStyle(int style)
        {
            for (int i = 0; i < GameConstants.TankStyleNames.Length; i++)
            {
                var hull = transform.Find($"Hull_{i}");
                if (hull != null) hull.gameObject.SetActive(i == style);
            }
        }

        /// <summary>
        /// Tint only the camo hull panels (material "Tank_Base") with the player
        /// color - tracks stay rubber-dark and steel parts stay metallic, which
        /// reads far better than tinting the whole tank.
        /// </summary>
        void ApplyColor(int index)
        {
            if (index < 0) return;
            Color c = GameConstants.GetPlayerColor(index);
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponentInParent<Billboard>() != null) continue; // bar / name tag
                var shared = r.sharedMaterial;
                if (shared == null || !shared.name.StartsWith("Tank_Base")) continue;
                r.material.color = c;
            }
            if (_nameTag != null) _nameTag.color = Color.Lerp(c, Color.white, 0.35f);
        }

        /// <summary>Floating name above remote tanks (own name would block the view).</summary>
        void CreateNameTag()
        {
            var go = new GameObject("NameTag");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 2.85f, 0f);
            go.AddComponent<Billboard>();

            _nameTag = go.AddComponent<TextMesh>();
            _nameTag.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _nameTag.GetComponent<MeshRenderer>().material = _nameTag.font.material;
            _nameTag.text = PlayerName.Value.ToString();
            _nameTag.fontSize = 48;
            _nameTag.characterSize = 0.045f;
            _nameTag.anchor = TextAnchor.MiddleCenter;
            _nameTag.alignment = TextAlignment.Center;
            _nameTag.color = Color.white;
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
