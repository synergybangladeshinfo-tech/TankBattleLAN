using Unity.Netcode;
using UnityEngine;
using TankBattle.UI;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Independent turret that auto-aims (lock-on) at the nearest enemy tank so
    /// the gun visibly rotates to track its target - separate from the hull, so
    /// you can drive one way while the cannon points another. The owner computes
    /// the aim yaw (relative to the hull) and replicates it; every client
    /// applies it to the turret pivot. Bullets fire from the turret's muzzle, so
    /// aiming the turret aims the shots. The HUD reads CurrentTarget to draw a
    /// target marker over the locked enemy.
    /// </summary>
    public class TurretAim : NetworkBehaviour
    {
        /// <summary>Turret yaw relative to the hull, in degrees (owner writes).</summary>
        public NetworkVariable<float> TurretYaw = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        [SerializeField] float range = 45f;       // lock-on range (metres)
        [SerializeField] float turnSpeed = 220f;  // turret slew (deg/s)

        Transform _pivot;
        TankHealth _health;
        float _appliedYaw;

        /// <summary>Owner-side: the enemy currently being tracked (for the HUD marker).</summary>
        public Transform CurrentTarget { get; private set; }

        void Awake()
        {
            _pivot = transform.Find("TurretPivot");
            _health = GetComponent<TankHealth>();
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (IsOwner) OwnerAim();

            // Every client applies the replicated yaw smoothly to the gun.
            _appliedYaw = Mathf.MoveTowardsAngle(_appliedYaw, TurretYaw.Value,
                                                 turnSpeed * Time.deltaTime);
            if (_pivot != null) _pivot.localRotation = Quaternion.Euler(0f, _appliedYaw, 0f);
        }

        void OwnerAim()
        {
            float desired;

            // MANUAL aim: the right stick (camera-relative) overrides auto-lock.
            Vector2 stick = ManualAim();
            if (stick.sqrMagnitude > 0.05f && Camera.main != null)
            {
                var cam = Camera.main.transform;
                Vector3 f = cam.forward; f.y = 0f; f.Normalize();
                Vector3 r = cam.right; r.y = 0f; r.Normalize();
                Vector3 dir = f * stick.y + r * stick.x;
                if (dir.sqrMagnitude > 0.001f)
                {
                    float worldYaw = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y;
                    desired = Mathf.DeltaAngle(transform.eulerAngles.y, worldYaw);
                    CurrentTarget = null; // hide the lock marker while aiming by hand
                    Commit(desired);
                    return;
                }
            }

            // AUTO-LOCK fallback: track the nearest enemy.
            CurrentTarget = FindTarget();
            if (CurrentTarget != null)
            {
                Vector3 to = CurrentTarget.position - transform.position;
                to.y = 0f;
                float worldYaw = Quaternion.LookRotation(to.normalized, Vector3.up).eulerAngles.y;
                desired = Mathf.DeltaAngle(transform.eulerAngles.y, worldYaw);
            }
            else desired = 0f; // rest pointing forward
            Commit(desired);
        }

        void Commit(float desired)
        {
            float next = Mathf.MoveTowardsAngle(TurretYaw.Value, desired, turnSpeed * Time.deltaTime);
            if (Mathf.Abs(Mathf.DeltaAngle(TurretYaw.Value, next)) > 0.05f)
                TurretYaw.Value = next;
        }

        /// <summary>Local player's right aim stick (bots have none).</summary>
        Vector2 ManualAim()
        {
            var bot = GetComponent<BotTank>();
            if (bot != null) return Vector2.zero;
            return HUDController.Instance != null ? HUDController.Instance.AimDirection : Vector2.zero;
        }

        Transform FindTarget()
        {
            if (_health != null && _health.IsDead.Value) return null;

            int myTeam = MatchManager.Instance != null
                ? MatchManager.Instance.GetTeam(_health != null ? _health.ActorId : OwnerClientId)
                : -1;

            Transform best = null;
            float bestSq = range * range;
            for (int i = 0; i < TankHealth.All.Count; i++)
            {
                var h = TankHealth.All[i];
                if (h == null || h == _health || h.IsDead.Value) continue;

                if (myTeam >= 0)
                {
                    var tc = h.GetComponent<TankController>();
                    if (tc != null && tc.TeamIndex.Value == myTeam) continue; // skip teammates
                }

                float sq = (h.transform.position - transform.position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = h.transform; }
            }
            return best;
        }
    }
}
