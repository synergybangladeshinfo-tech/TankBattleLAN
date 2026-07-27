using Unity.Netcode;
using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Server-side AI driver for solo mode. Added at runtime (before Spawn) to a
    /// normal tank instance; the bot tank is server-owned, so this component
    /// simply drives the CharacterController on the server and the transform
    /// replicates to clients like any other tank.
    ///
    /// v2.8 brain: instead of always charging straight at you, a bot now runs a
    /// small state machine -
    ///   HUNT     close the distance to a chosen target
    ///   ENGAGE   hold a firing distance and strafe rather than stand still
    ///   FLANK    circle around when it cannot get a clear shot
    ///   RETREAT  break off and back away when badly hurt
    ///   WANDER   no target: roam the map
    /// Difficulty scales aim error, reaction time, fire rate, how far it can see
    /// and how bravely it pushes, so Easy is genuinely gentle and Hard actually
    /// hunts you down.
    /// </summary>
    public class BotTank : MonoBehaviour
    {
        /// <summary>Fake client id used in the scoreboard (GameConstants.BotIdBase + n).</summary>
        public ulong BotId;

        /// <summary>Team in Team Battle, -1 otherwise.</summary>
        public int Team = -1;

        enum State { Wander, Hunt, Engage, Flank, Retreat }

        const float Gravity = 25f;
        const float TurnSpeed = 130f;

        // ---- tuned per difficulty in Awake ----
        float _moveSpeed = 6.0f;
        float _fireRange = 42f;
        float _sightRange = 55f;
        float _aimError = 4f;
        float _reaction = 0.4f;      // seconds between re-thinks
        float _fireCadenceMin = 0.8f, _fireCadenceMax = 1.4f;
        float _retreatHealthPct = 0.25f;
        float _engageDistance = 16f;

        CharacterController _cc;
        TankHealth _health;
        TankShooting _shooting;
        Transform _muzzle;

        TankHealth _target;
        State _state = State.Wander;
        float _nextThink, _nextFire, _nextWanderChange, _nextStrafeFlip;
        float _wanderTurn;
        int _strafeDir = 1;
        float _vy;
        bool _hadLineOfSight;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<TankHealth>();
            _shooting = GetComponent<TankShooting>();
            _muzzle = transform.Find("Muzzle");
            ApplyDifficulty(GameSession.BotDifficulty);
        }

        /// <summary>0 = Easy, 1 = Normal, 2 = Hard.</summary>
        void ApplyDifficulty(int level)
        {
            switch (Mathf.Clamp(level, 0, 2))
            {
                case 0: // EASY - slow, poor aim, gives up early, keeps its distance
                    _moveSpeed = 4.6f;
                    _fireRange = 28f;
                    _sightRange = 34f;
                    _aimError = Random.Range(7f, 11f);
                    _reaction = 0.85f;
                    _fireCadenceMin = 1.6f; _fireCadenceMax = 2.6f;
                    _retreatHealthPct = 0.45f;
                    _engageDistance = 20f;
                    break;

                case 2: // HARD - fast, accurate, relentless, presses the advantage
                    _moveSpeed = 7.4f;
                    _fireRange = 50f;
                    _sightRange = 72f;
                    _aimError = Random.Range(0.8f, 2.2f);
                    _reaction = 0.18f;
                    _fireCadenceMin = 0.45f; _fireCadenceMax = 0.8f;
                    _retreatHealthPct = 0.12f;
                    _engageDistance = 13f;
                    break;

                default: // NORMAL
                    _moveSpeed = 6.0f;
                    _fireRange = 42f;
                    _sightRange = 55f;
                    _aimError = Random.Range(2.5f, 5f);
                    _reaction = 0.4f;
                    _fireCadenceMin = 0.8f; _fireCadenceMax = 1.4f;
                    _retreatHealthPct = 0.25f;
                    _engageDistance = 16f;
                    break;
            }
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;
            if (_cc == null || !_cc.enabled) return;
            if (_health != null && _health.IsDead.Value) return;

            var match = MatchManager.Instance;
            if (match != null && match.MatchEnded.Value) return;
            // Hold still during the 3-2-1 countdown like everyone else.
            if (match != null && !match.RoundLive) return;

            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + _reaction;
                PickTarget();
                _hadLineOfSight = _target != null && HasLineOfSight(_target);
                ChooseState();
            }

            float turnInput = 0f, throttle = 0f;
            RunState(ref turnInput, ref throttle);

            // Wall feeler: if something solid (not a tank) is close ahead, veer off.
            if (Physics.Raycast(transform.position + Vector3.up * 0.8f, transform.forward,
                                out RaycastHit hit, 5f, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore) &&
                hit.collider.GetComponentInParent<TankHealth>() == null)
            {
                // Steer around the obstacle rather than always turning the same
                // way, which is what used to make bots grind along walls.
                turnInput = _strafeDir;
                throttle = Mathf.Min(throttle, 0.45f);
            }

            transform.Rotate(0f, turnInput * TurnSpeed * Time.deltaTime, 0f);
            Vector3 motion = transform.forward * (throttle * _moveSpeed);
            if (_cc.isGrounded) _vy = -1f;
            else _vy -= Gravity * Time.deltaTime;
            motion.y = _vy;
            _cc.Move(motion * Time.deltaTime);
        }

        // --------------------------------------------------------------- brain

        void ChooseState()
        {
            if (_target == null) { _state = State.Wander; return; }

            float hpPct = _health != null
                ? _health.Health.Value / (float)GameConstants.MaxHealth : 1f;
            if (hpPct <= _retreatHealthPct) { _state = State.Retreat; return; }

            float dist = Vector3.Distance(transform.position, _target.transform.position);

            if (dist > _fireRange) _state = State.Hunt;
            else if (!_hadLineOfSight) _state = State.Flank;   // something is in the way
            else _state = State.Engage;
        }

        void RunState(ref float turnInput, ref float throttle)
        {
            if (Time.time >= _nextStrafeFlip)
            {
                _nextStrafeFlip = Time.time + Random.Range(1.4f, 3f);
                _strafeDir = Random.value < 0.5f ? -1 : 1;
            }

            switch (_state)
            {
                case State.Wander:
                    if (Time.time >= _nextWanderChange)
                    {
                        _nextWanderChange = Time.time + Random.Range(1.5f, 3.5f);
                        _wanderTurn = Random.Range(-0.6f, 0.6f);
                    }
                    turnInput = _wanderTurn;
                    throttle = 0.7f;
                    return;

                case State.Retreat:
                {
                    // Face away from the threat and run, still shooting behind us
                    // if we happen to line up.
                    Vector3 away = transform.position - _target.transform.position;
                    away.y = 0f;
                    float angleAway = Vector3.SignedAngle(transform.forward, away, Vector3.up);
                    turnInput = Mathf.Clamp(angleAway / 25f, -1f, 1f);
                    throttle = 1f;
                    TryFire();
                    return;
                }

                case State.Hunt:
                {
                    Vector3 to = _target.transform.position - transform.position;
                    to.y = 0f;
                    float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);
                    turnInput = Mathf.Clamp(angle / 25f, -1f, 1f);
                    throttle = Mathf.Abs(angle) > 70f ? 0.3f : 1f;
                    TryFire();
                    return;
                }

                case State.Flank:
                {
                    // No clear shot: swing around the target instead of nosing
                    // into the wall between us.
                    Vector3 to = _target.transform.position - transform.position;
                    to.y = 0f;
                    Vector3 orbit = Vector3.Cross(Vector3.up, to.normalized) * _strafeDir;
                    Vector3 desired = (to.normalized * 0.35f + orbit).normalized;
                    float angle = Vector3.SignedAngle(transform.forward, desired, Vector3.up);
                    turnInput = Mathf.Clamp(angle / 25f, -1f, 1f);
                    throttle = 0.9f;
                    TryFire();
                    return;
                }

                default: // Engage
                {
                    Vector3 to = _target.transform.position - transform.position;
                    to.y = 0f;
                    float dist = to.magnitude;
                    float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);

                    // Strafe sideways while shooting so it is not a sitting duck.
                    Vector3 orbit = Vector3.Cross(Vector3.up, to.normalized) * _strafeDir;
                    Vector3 desired = dist < _engageDistance * 0.7f
                        ? (-to.normalized * 0.5f + orbit).normalized   // too close, back off
                        : dist > _engageDistance * 1.3f
                            ? (to.normalized * 0.8f + orbit * 0.5f).normalized // close in
                            : orbit;                                   // hold and circle

                    float steerAngle = Vector3.SignedAngle(transform.forward, desired, Vector3.up);
                    turnInput = Mathf.Clamp(steerAngle / 30f, -1f, 1f);
                    throttle = 0.75f;

                    // Keep the nose roughly on target so shots actually land.
                    if (Mathf.Abs(angle) < 40f) turnInput = Mathf.Clamp(angle / 25f, -1f, 1f);

                    TryFire();
                    return;
                }
            }
        }

        void TryFire()
        {
            if (_target == null || _shooting == null) return;
            if (Time.time < _nextFire) return;

            Vector3 to = _target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            float angle = Vector3.SignedAngle(transform.forward, to, Vector3.up);

            if (dist > _fireRange) return;
            if (Mathf.Abs(angle) > 8f) return;
            if (!HasLineOfSight(_target)) return;

            _nextFire = Time.time + Random.Range(_fireCadenceMin, _fireCadenceMax);
            _shooting.ServerFireOnce(BotId, Team, Random.Range(-_aimError, _aimError));
        }

        void PickTarget()
        {
            _target = null;
            float best = _sightRange * _sightRange;
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
            if (_muzzle == null || target == null) return true;
            Vector3 from = _muzzle.position;
            Vector3 to = target.transform.position + Vector3.up * 0.8f;
            if (Physics.Raycast(from, (to - from).normalized, out RaycastHit hit,
                                _fireRange, Physics.DefaultRaycastLayers,
                                QueryTriggerInteraction.Ignore))
                return hit.collider.GetComponentInParent<TankHealth>() == target;
            return false;
        }
    }
}
