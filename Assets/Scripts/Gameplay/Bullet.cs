using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Server-authoritative projectile. The server moves it forward using a
    /// spherecast for hit detection (no physics dependencies, cheap on mobile);
    /// a plain NetworkTransform replicates the motion to clients.
    /// The weapon index is replicated so every client can style the projectile
    /// (size, color, trail). Rockets deal splash damage in a radius.
    /// In Team Battle, bullets fly straight through teammates.
    /// </summary>
    public class Bullet : NetworkBehaviour
    {
        [SerializeField] float radius = 0.15f;
        [SerializeField] float maxLifetime = 3f;

        /// <summary>Which weapon fired this (set by the server before Spawn).</summary>
        public NetworkVariable<int> WeaponIndex = new NetworkVariable<int>(0);

        // Server-side state; the server is the sole hit arbiter.
        ulong _shooterId;
        int _shooterTeam = -1;
        WeaponDef _def;
        float _dieAt;
        Vector3 _hitPoint, _hitNormal = Vector3.up;   // surface we struck, for FX
        Color _visualColor = Color.white;              // set on every peer at spawn

        /// <summary>Server: remember who fired for kill credit / team filtering.</summary>
        public void Init(ulong shooterId, int weaponIndex, int shooterTeam)
        {
            _shooterId = shooterId;
            _shooterTeam = shooterTeam;
            _def = Weapons.Get(weaponIndex);
            // Flame "bullets" burn out almost immediately, which is what turns
            // the flamethrower into a short cone instead of a machine gun.
            _dieAt = Time.time + (Weapons.IsShortRange(weaponIndex) ? 0.55f : maxLifetime);
        }

        public override void OnNetworkSpawn()
        {
            // Style the projectile on every peer (visuals only).
            var def = Weapons.Get(WeaponIndex.Value);
            var visual = transform.Find("Visual");
            if (visual != null)
            {
                visual.localScale = Vector3.one * def.BulletScale;
                var mr = visual.GetComponent<MeshRenderer>();
                if (mr != null) mr.material.color = def.BulletColor;
            }
            _visualColor = def.BulletColor;

            var trail = GetComponentInChildren<TrailRenderer>();
            if (trail != null)
            {
                trail.startColor = def.BulletColor;
                Color end = def.BulletColor; end.a = 0f;
                trail.endColor = end;
                trail.startWidth = def.BulletScale * 0.6f;
                trail.Clear();
            }
        }

        public override void OnNetworkDespawn()
        {
            AudioManager.Instance?.PlayHitAt(transform.position);
        }

        void Update()
        {
            if (!IsServer || !IsSpawned) return;

            if (Time.time >= _dieAt) { Explode(null); return; }

            float step = _def.Speed * Time.deltaTime;

            // Spherecast the path we are about to travel this frame.
            if (Physics.SphereCast(transform.position, radius, transform.forward,
                                   out RaycastHit hit, step, Physics.DefaultRaycastLayers,
                                   QueryTriggerInteraction.Ignore))
            {
                var health = hit.collider.GetComponentInParent<TankHealth>();
                if (health != null)
                {
                    // Fly through the shooter's own tank and dead tanks.
                    if (health.ActorId == _shooterId || health.IsDead.Value) { }
                    // Fly through teammates in Team Battle.
                    else if (IsTeammate(health)) { }
                    else
                    {
                        _hitPoint = hit.point; _hitNormal = hit.normal;
                        Explode(health);
                        return;
                    }
                }
                else
                {
                    // Shootable prop (crate / fuel barrel) or plain scenery.
                    var prop = hit.collider.GetComponentInParent<Destructible>();
                    if (prop != null) prop.TakeDamage(_def.Damage, _shooterId);

                    _hitPoint = hit.point; _hitNormal = hit.normal;
                    Explode(null); // wall / obstacle
                    return;
                }
            }

            transform.position += transform.forward * step;
        }

        bool IsTeammate(TankHealth victim)
        {
            if (_shooterTeam < 0) return false;
            var tc = victim.GetComponent<TankController>();
            return tc != null && tc.TeamIndex.Value == _shooterTeam;
        }

        /// <summary>Server: apply damage (direct + splash for rockets) and despawn.</summary>
        void Explode(TankHealth directHit)
        {
            if (directHit != null)
                directHit.TakeDamage(_def.Damage, _shooterId);

            // Rocket splash: hurt every enemy near the blast, with falloff.
            // Iterates the global tank registry so bots are included too.
            if (_def.SplashRadius > 0f)
            {
                for (int i = TankHealth.All.Count - 1; i >= 0; i--)
                {
                    var h = TankHealth.All[i];
                    if (h == null || h.IsDead.Value || h == directHit) continue;
                    if (h.ActorId == _shooterId) continue; // no self-damage (fairness)
                    if (IsTeammate(h)) continue;

                    float d = Vector3.Distance(h.transform.position, transform.position);
                    if (d > _def.SplashRadius) continue;

                    // 100% at the centre down to 35% at the edge.
                    float falloff = Mathf.Lerp(1f, 0.35f, d / _def.SplashRadius);
                    h.TakeDamage(Mathf.RoundToInt(_def.Damage * falloff), _shooterId);
                }
            }

            // Blasts shatter nearby props as well as tanks.
            if (_def.SplashRadius > 0f)
                Destructible.DamageInRadius(transform.position, _def.SplashRadius,
                                            _def.Damage, _shooterId);

            // Tell every client where to draw the mark before we disappear.
            Vector3 fxPoint = _hitPoint == Vector3.zero ? transform.position : _hitPoint;
            ImpactClientRpc(fxPoint, _hitNormal, _def.SplashRadius);

            if (IsSpawned) GetComponent<NetworkObject>().Despawn();
        }

        /// <summary>Every client draws the scorch mark, sparks and dust locally.</summary>
        [ClientRpc]
        void ImpactClientRpc(Vector3 point, Vector3 normal, float splash)
        {
            if (splash > 0f) TankBattle.Utils.ImpactFx.Explosion(point, splash);
            else TankBattle.Utils.ImpactFx.BulletHit(point, normal, _visualColor);
        }
    }
}
