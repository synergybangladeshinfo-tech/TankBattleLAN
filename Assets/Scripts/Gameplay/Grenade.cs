using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Core;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// A thrown grenade: the server arcs it forward under gravity and detonates
    /// on a fuse timer or when it hits the ground / an obstacle, dealing splash
    /// damage to every enemy in the blast radius (shooter and teammates immune).
    /// A NetworkTransform replicates the arc to clients; the visual spins locally.
    /// </summary>
    public class Grenade : NetworkBehaviour
    {
        const float Gravity = 18f;
        const float Radius = 0.22f;

        Vector3 _vel;
        ulong _shooter;
        int _team = -1;
        float _blowAt;
        bool _blown;

        /// <summary>Server: set the thrower, team and initial velocity.</summary>
        public void Init(ulong shooter, int team, Vector3 velocity)
        {
            _shooter = shooter;
            _team = team;
            _vel = velocity;
            _blowAt = Time.time + GameConstants.GrenadeFuse;
        }

        void Update()
        {
            // Spin the mesh on every client for a bit of life.
            var visual = transform.Find("Visual");
            if (visual != null) visual.Rotate(180f * Time.deltaTime, 240f * Time.deltaTime, 0f);

            if (!IsServer || !IsSpawned) return;

            _vel.y -= Gravity * Time.deltaTime;
            Vector3 step = _vel * Time.deltaTime;
            float dist = step.magnitude;

            if (dist > 0.001f && Physics.SphereCast(transform.position, Radius, _vel.normalized,
                    out RaycastHit hit, dist + 0.05f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                // Hit the ground / a wall (ignore tanks - splash handles them).
                if (hit.collider.GetComponentInParent<TankHealth>() == null) { Explode(); return; }
            }

            transform.position += step;
            if (Time.time >= _blowAt) Explode();
        }

        void Explode()
        {
            if (_blown) return;
            _blown = true;

            for (int i = TankHealth.All.Count - 1; i >= 0; i--)
            {
                var h = TankHealth.All[i];
                if (h == null || h.IsDead.Value) continue;
                if (h.ActorId == _shooter) continue; // no self-harm

                if (_team >= 0)
                {
                    var tc = h.GetComponent<TankController>();
                    if (tc != null && tc.TeamIndex.Value == _team) continue; // teammates safe
                }

                float d = Vector3.Distance(h.transform.position, transform.position);
                if (d > GameConstants.GrenadeSplashRadius) continue;
                float falloff = Mathf.Lerp(1f, 0.3f, d / GameConstants.GrenadeSplashRadius);
                h.TakeDamage(Mathf.RoundToInt(GameConstants.GrenadeDamage * falloff), _shooter);
            }

            ExplodeClientRpc(transform.position);
            if (IsSpawned) GetComponent<NetworkObject>().Despawn();
        }

        [ClientRpc]
        void ExplodeClientRpc(Vector3 pos)
        {
            AudioManager.Instance?.PlayExplosionAt(pos);
            TankBattle.Utils.CameraFollow.Instance?.ShakeAt(pos, 0.6f);
        }
    }
}
