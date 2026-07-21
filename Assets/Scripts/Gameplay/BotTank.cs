using Unity.Netcode;
using UnityEngine;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Server-side AI driver for solo mode. Added at runtime (before Spawn) to
    /// a normal tank instance; the bot tank is server-owned, so this component
    /// simply drives the CharacterController on the server and the transform
    /// replicates to clients like any other tank.
    ///
    /// Behaviour: hunt the nearest enemy tank, keep a comfortable range, avoid
    /// walls with a feeler raycast, fire with a small random aim error and a
    /// human-ish reaction cadence - challenging but always beatable.
    /// </summary>
    public class BotTank : MonoBehaviour
    {
        /// <summary>Fake client id used in the scoreboard (GameConstants.BotIdBase + n).</summary>
        public ulong BotId;

        /// <summary>Team in Team Battle, -1 otherwise.</summary>
        public int Team = -1;

        const float MoveSpeed = 6.0f;   // slightly slower than players
        const float TurnSpeed = 110f;
        const float FireRange = 30f;
        const float Gravity = 25f;

        CharacterController _cc;
        TankHealth _health;
        TankShooting _shooting;
        Transform _muzzle;

        TankHealth _target;
        float _nextThink, _nextFire, _nextWanderChange;
        float _wanderTurn;
        float _aimError;
        float _vy;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<TankHealth>();
            _shooting = GetComponent<TankShooting>();
            _muzzle = transform.Find("Muzzle");
            _aimError = Random.Range(2.5f, 5f); // per-bot personality
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (_cc == null || !_cc.enabled) return;
            if (_health != null && _health.IsDead.Value) return;
            if (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value) return;

            // Re-evaluate the target a couple of times per second.
            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + 0.4f;
                PickTarget();
            }

            float turnInput;
            float throttle;

            if (_target != null)
            {
                Vector3 to = _target.transform.position - transform.position;
                to.y = 0f;
                float dist = to.magnitude;
                float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);

                turnInput = Mathf.Clamp(angle / 25f, -1f, 1f);
                throttle = dist < 9f ? 0f                       // hold distance
                         : Mathf.Abs(angle) > 70f ? 0.25f       // turn first
                         : 0.9f;

                // Fire when roughly aligned, in range and with clear line of sight.
                if (dist < FireRange && Mathf.Abs(angle) < 6f &&
                    Time.time >= _nextFire && HasLineOfSight(_target))
                {
                    _nextFire = Time.time + Random.Range(0.8f, 1.4f);
                    _shooting.ServerFireOnce(BotId, Team,
                        Random.Range(-_aimError, _aimError));
                }
            }
            else
            {
                // No target: wander, changing direction now and then.
                if (Time.time >= _nextWanderChange)
                {
                    _nextWanderChange = Time.time + Random.Range(1.5f, 3.5f);
                    _wanderTurn = Random.Range(-0.6f, 0.6f);
                }
                turnInput = _wanderTurn;
                throttle = 0.7f;
            }

            // Wall feeler: if something solid (not a tank) is close ahead, veer off.
            if (Physics.Raycast(transform.position + Vector3.up * 0.8f, transform.forward,
                                out RaycastHit hit, 5f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<TankHealth>() == null)
            {
                turnInput = 1f;
                throttle = 0.5f;
            }

            transform.Rotate(0f, turnInput * TurnSpeed * Time.deltaTime, 0f);
            Vector3 motion = transform.forward * (throttle * MoveSpeed);
            if (_cc.isGrounded) _vy = -1f;
            else _vy -= Gravity * Time.deltaTime;
            motion.y = _vy;
            _cc.Move(motion * Time.deltaTime);
        }

        void PickTarget()
        {
            _target = null;
            float best = float.MaxValue;
            foreach (var h in TankHealth.All)
            {
                if (h == null || h == _health || h.IsDead.Value) continue;
                if (h.ActorId == BotId) continue;

                // Never hunt teammates in Team Battle.
                if (Team >= 0)
                {
                    var tc = h.GetComponent<TankController>();
                    if (tc != null && tc.TeamIndex.Value == Team) continue;
                }

                float d = (h.transform.position - transform.position).sqrMagnitude;
                if (d < best) { best = d; _target = h; }
            }
        }

        bool HasLineOfSight(TankHealth target)
        {
            if (_muzzle == null) return true;
            Vector3 from = _muzzle.position;
            Vector3 to = target.transform.position + Vector3.up * 0.8f;
            if (Physics.Raycast(from, (to - from).normalized, out RaycastHit hit,
                                FireRange, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<TankHealth>() == target;
            return false;
        }
    }
}
