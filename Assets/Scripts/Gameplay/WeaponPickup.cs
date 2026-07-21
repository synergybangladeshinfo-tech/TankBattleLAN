using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Core;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// A floating weapon crate. The server spawns one at each PickupPoint,
    /// checks for nearby living tanks and grants the weapon on contact; the
    /// crate then despawns and MatchManager respawns a fresh (random) one at
    /// the same point after a delay. Spin/bob animation runs locally on every
    /// client - the crate never moves logically, so no NetworkTransform needed.
    /// </summary>
    public class WeaponPickup : NetworkBehaviour
    {
        [SerializeField] float takeRadius = 2.4f;   // metres to collect
        [SerializeField] float spinSpeed = 90f;     // deg/s visual spin
        [SerializeField] float bobHeight = 0.25f;   // metres visual bobbing

        /// <summary>Weapon this crate grants (index into Weapons.Defs).</summary>
        public NetworkVariable<int> Type = new NetworkVariable<int>(1);

        /// <summary>Server-only: which PickupPoint this crate belongs to.</summary>
        public int PointIndex;

        Vector3 _basePos;
        float _nextCheck;
        bool _taken;

        public override void OnNetworkSpawn()
        {
            _basePos = transform.position;

            // Tint the crate so players can read the contents from a distance.
            Color tint = Type.Value == GameConstants.ShieldPickupId
                ? GameConstants.ShieldColor
                : Weapons.Get(Type.Value).BulletColor;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>())
                mr.material.color = tint;
        }

        void Update()
        {
            // Visual spin + bob on every peer (deterministic, no sync needed).
            transform.rotation = Quaternion.Euler(0f, Time.time * spinSpeed % 360f, 0f);
            transform.position = _basePos +
                Vector3.up * (Mathf.Sin(Time.time * 2.2f) * bobHeight);

            // Server: cheap proximity poll (5x per second is plenty).
            if (!IsServer || !IsSpawned || _taken || Time.time < _nextCheck) return;
            _nextCheck = Time.time + 0.2f;

            foreach (var kv in NetworkManager.ConnectedClients)
            {
                var po = kv.Value.PlayerObject;
                if (po == null) continue;
                var health = po.GetComponent<TankHealth>();
                if (health == null || health.IsDead.Value) continue;
                if (Vector3.Distance(po.transform.position, _basePos) > takeRadius) continue;

                if (Type.Value == GameConstants.ShieldPickupId)
                {
                    // Shield crate: grant 2-minute invincibility.
                    health.ServerGrantShield();
                }
                else
                {
                    var shooting = po.GetComponent<TankShooting>();
                    if (shooting == null) continue;
                    var def = Weapons.Get(Type.Value);
                    shooting.ServerSetWeapon(Type.Value, def.Ammo);
                }

                _taken = true;
                TakenClientRpc(_basePos);
                if (MatchManager.Instance != null)
                    MatchManager.Instance.OnPickupTaken(PointIndex);
                GetComponent<NetworkObject>().Despawn();
                return;
            }
        }

        [ClientRpc]
        void TakenClientRpc(Vector3 pos)
        {
            AudioManager.Instance?.PlayPickupAt(pos);
        }
    }
}
