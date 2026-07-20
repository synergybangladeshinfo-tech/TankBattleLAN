using Unity.Netcode;
using UnityEngine;
using TankBattle.Networking;
using TankBattle.UI;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Owner reads the fire button and asks the server (ServerRpc) to spawn a
    /// bullet. The server validates the fire rate and the tank's alive state,
    /// then spawns a server-authoritative bullet at the muzzle.
    /// </summary>
    public class TankShooting : NetworkBehaviour
    {
        [Header("Wired by the prefab builder")]
        public Transform muzzle;                     // bullet spawn point (barrel tip)

        [Header("Tuning")]
        [SerializeField] float fireInterval = 0.5f;  // seconds between shots

        float _nextLocalFire;   // owner-side cooldown (responsiveness)
        float _nextServerFire;  // server-side cooldown (authority)

        TankHealth _health;

        void Awake() => _health = GetComponent<TankHealth>();

        void Update()
        {
            if (!IsOwner || !IsSpawned) return;
            if (_health != null && _health.IsDead.Value) return;
            if (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value) return;

            if (!FirePressed() || Time.time < _nextLocalFire) return;

            _nextLocalFire = Time.time + fireInterval;
            FireServerRpc();
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

        [ServerRpc]
        void FireServerRpc()
        {
            // Server re-validates everything it cares about.
            if (Time.time < _nextServerFire) return;
            if (_health != null && _health.IsDead.Value) return;
            if (MatchManager.Instance != null && MatchManager.Instance.MatchEnded.Value) return;
            _nextServerFire = Time.time + fireInterval * 0.9f; // small tolerance for jitter

            var prefab = ConnectionManager.Instance != null ? ConnectionManager.Instance.BulletPrefab : null;
            if (prefab == null || muzzle == null) return;

            GameObject go = Instantiate(prefab, muzzle.position, muzzle.rotation);
            go.GetComponent<Bullet>().Init(OwnerClientId);
            go.GetComponent<NetworkObject>().Spawn(true); // destroyWithScene
        }
    }
}
