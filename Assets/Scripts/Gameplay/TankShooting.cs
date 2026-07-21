using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Networking;
using TankBattle.UI;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Owner reads the fire button and asks the server (ServerRpc) to fire.
    /// The server validates fire rate / alive state / ammo, applies a small
    /// aim assist (bullets nudge toward an enemy that is almost in your sights
    /// - a big comfort win on touch controls), then spawns server-authoritative
    /// bullets. Weapon and ammo are NetworkVariables so the HUD and all clients
    /// stay in sync; pickups and Gun Game change them via ServerSetWeapon.
    /// AI bots reuse the same server fire path via ServerFireOnce.
    /// </summary>
    public class TankShooting : NetworkBehaviour
    {
        [Header("Wired by the prefab builder")]
        public Transform muzzle;                     // bullet spawn point (barrel tip)

        [Header("Aim assist")]
        [SerializeField] float assistRange = 35f;    // metres
        [SerializeField] float assistCone = 10f;     // degrees off-axis we still help

        /// <summary>Current weapon (index into Weapons.Defs) and shots left (-1 = infinite).</summary>
        public NetworkVariable<int> Weapon = new NetworkVariable<int>(0);
        public NetworkVariable<int> Ammo = new NetworkVariable<int>(-1);

        float _nextLocalFire;   // owner-side cooldown (responsiveness)
        float _nextServerFire;  // server-side cooldown (authority)

        TankHealth _health;
        ParticleSystem _muzzleFlash;

        void Awake()
        {
            _health = GetComponent<TankHealth>();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name == "MuzzleFlashPS") { _muzzleFlash = ps; break; }
        }

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            if (GetComponent<BotTank>() != null) return; // bots fire from BotTank
            if (_health != null && _health.IsDead.Value) return;
            if (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value) return;

            if (!FirePressed() || Time.time < _nextLocalFire) return;

            _nextLocalFire = Time.time + Weapons.Get(Weapon.Value).FireInterval;
            FireServerRpc();

            // Punchy recoil kick on the local camera.
            var def = Weapons.Get(Weapon.Value);
            float kick = def.SplashRadius > 0f ? 0.32f : 0.12f; // rockets kick harder
            TankBattle.Utils.CameraFollow.Instance?.Shake(kick);
        }

        bool FirePressed()
        {
            if (HUDController.Instance != null && HUDController.Instance.FireButton != null &&
                HUDController.Instance.FireButton.IsPressed) return true;
#if UNITY_EDITOR || UNITY_STANDALONE
            return Input.GetKey(KeyCode.Space); // editor testing convenience
#else
            return false;
#endif
        }

        // ---------------------------------------------------------------- server

        /// <summary>Server: hand this tank a weapon (pickups, Gun Game tiers).</summary>
        public void ServerSetWeapon(int weaponIndex, int ammo)
        {
            if (!IsServer) return;
            Weapon.Value = weaponIndex;
            Ammo.Value = ammo;
        }

        /// <summary>
        /// Server: fire immediately with an optional yaw error - the path AI
        /// bots use (their cooldown and aiming live in BotTank).
        /// </summary>
        public void ServerFireOnce(ulong actorId, int team, float yawErrorDeg = 0f)
        {
            if (!IsServer || !IsSpawned || muzzle == null) return;
            var prefab = ConnectionManager.Instance != null
                ? ConnectionManager.Instance.BulletPrefab : null;
            if (prefab == null) return;

            int weaponIndex = Weapon.Value;
            Quaternion rot = muzzle.rotation * Quaternion.Euler(0f, yawErrorDeg, 0f);
            SpawnBullets(prefab, weaponIndex, rot, actorId, team);
            FiredClientRpc(weaponIndex);
        }

        [ServerRpc]
        void FireServerRpc()
        {
            // Server re-validates everything it cares about.
            if (Time.time < _nextServerFire) return;
            if (_health != null && _health.IsDead.Value) return;
            if (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value) return;

            int weaponIndex = Weapon.Value;
            var def = Weapons.Get(weaponIndex);
            _nextServerFire = Time.time + def.FireInterval * 0.9f; // jitter tolerance

            var prefab = ConnectionManager.Instance != null ? ConnectionManager.Instance.BulletPrefab : null;
            if (prefab == null || muzzle == null) return;

            // Spend ammo; back to the standard cannon when it runs out.
            if (Ammo.Value > 0)
            {
                Ammo.Value--;
                if (Ammo.Value == 0)
                {
                    Weapon.Value = (int)WeaponType.Standard;
                    Ammo.Value = -1;
                }
            }

            ulong actorId = _health != null ? _health.ActorId : OwnerClientId;
            int shooterTeam = MatchManager.Instance != null
                ? MatchManager.Instance.GetTeam(actorId) : -1;

            Quaternion baseRot = AimAssist(muzzle.rotation, actorId, shooterTeam);
            SpawnBullets(prefab, weaponIndex, baseRot, actorId, shooterTeam);
            FiredClientRpc(weaponIndex);
        }

        /// <summary>Server: spawn the projectile(s) for one trigger pull.</summary>
        void SpawnBullets(GameObject prefab, int weaponIndex, Quaternion baseRot,
                          ulong actorId, int team)
        {
            var def = Weapons.Get(weaponIndex);
            for (int i = 0; i < Mathf.Max(1, def.Pellets); i++)
            {
                Quaternion rot = baseRot;
                if (def.SpreadDeg > 0f)
                    rot = baseRot * Quaternion.Euler(
                        Random.Range(-def.SpreadDeg, def.SpreadDeg) * 0.4f,
                        Random.Range(-def.SpreadDeg, def.SpreadDeg), 0f);

                GameObject go = Instantiate(prefab, muzzle.position, rot);
                var bullet = go.GetComponent<Bullet>();
                bullet.WeaponIndex.Value = weaponIndex;
                bullet.Init(actorId, weaponIndex, team);
                go.GetComponent<NetworkObject>().Spawn(true); // destroyWithScene
            }
        }

        /// <summary>
        /// Server: if an enemy is nearly in our sights, rotate the shot onto it.
        /// Horizontal only, small cone - helps aiming without feeling unfair.
        /// </summary>
        Quaternion AimAssist(Quaternion aim, ulong actorId, int shooterTeam)
        {
            Vector3 fwd = aim * Vector3.forward; fwd.y = 0f; fwd.Normalize();
            Transform best = null;
            float bestAngle = assistCone;

            foreach (var h in TankHealth.All)
            {
                if (h == null || h.IsDead.Value) continue;
                if (h.ActorId == actorId) continue;

                // Never assist onto teammates.
                if (shooterTeam >= 0)
                {
                    var tc = h.GetComponent<TankController>();
                    if (tc != null && tc.TeamIndex.Value == shooterTeam) continue;
                }

                Vector3 to = h.transform.position - muzzle.position;
                to.y = 0f;
                float dist = to.magnitude;
                if (dist > assistRange || dist < 2f) continue;

                float angle = Vector3.Angle(fwd, to);
                if (angle < bestAngle) { bestAngle = angle; best = h.transform; }
            }

            if (best == null) return aim;
            Vector3 dir = best.position - muzzle.position;
            dir.y = 0f;
            return Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        [ClientRpc]
        void FiredClientRpc(int weaponIndex)
        {
            if (_muzzleFlash != null) _muzzleFlash.Play();
            AudioManager.Instance?.PlayShootAt(muzzle != null ? muzzle.position : transform.position,
                                              weaponIndex);
        }
    }
}
