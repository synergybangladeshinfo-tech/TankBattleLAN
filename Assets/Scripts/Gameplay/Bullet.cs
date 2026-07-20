using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Core;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Server-authoritative projectile. The server moves it forward using a
    /// spherecast for hit detection (no physics dependencies, cheap on mobile);
    /// a plain NetworkTransform replicates the motion to clients.
    /// Clients play the shoot sound on spawn and a hit "pop" on despawn.
    /// </summary>
    public class Bullet : NetworkBehaviour
    {
        [SerializeField] float speed = 22f;
        [SerializeField] float maxLifetime = 3f;
        [SerializeField] float radius = 0.15f;

        ulong _shooterId;   // server-side only; server is the sole hit arbiter
        float _dieAt;

        /// <summary>Server: remember who fired for kill credit / self-hit filtering.</summary>
        public void Init(ulong shooterId)
        {
            _shooterId = shooterId;
            _dieAt = Time.time + maxLifetime;
        }

        public override void OnNetworkSpawn()
        {
            // Everyone (host included, exactly once) hears the shot at the muzzle.
            AudioManager.Instance?.PlayShootAt(transform.position);
        }

        public override void OnNetworkDespawn()
        {
            AudioManager.Instance?.PlayHitAt(transform.position);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;

            if (Time.time >= _dieAt) { Despawn(); return; }

            float step = speed * Time.deltaTime;

            // Spherecast the path we are about to travel this frame.
            if (Physics.SphereCast(transform.position, radius, transform.forward,
                                   out RaycastHit hit, step, Physics.DefaultRaycastLayers,
                                   QueryTriggerInteraction.Ignore))
            {
                var health = hit.collider.GetComponentInParent<TankHealth>();
                if (health != null)
                {
                    // Ignore the shooter's own tank and already-dead tanks.
                    if (health.OwnerClientId != _shooterId && !health.IsDead.Value)
                    {
                        health.TakeDamage(GameConstants.BulletDamage, _shooterId);
                        Despawn();
                        return;
                    }
                    // Fly through the shooter/dead tanks.
                }
                else
                {
                    Despawn(); // wall / obstacle
                    return;
                }
            }

            transform.position += transform.forward * step;
        }

        void Despawn()
        {
            if (IsSpawned) GetComponent<NetworkObject>().Despawn();
        }
    }
}
