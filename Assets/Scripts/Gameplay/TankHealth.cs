using System.Collections;
using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Core;
using TankBattle.UI;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Server-authoritative health, death and respawn.
    /// - Health/IsDead are NetworkVariables so every client sees them.
    /// - On death the server credits the kill, waits, then respawns the tank at
    ///   a spawn point. Because the tank transform is owner-authoritative, the
    ///   actual teleport is executed by the owner via a targeted ClientRpc.
    /// - Visuals (renderers + overhead health bar) react to the variables.
    /// </summary>
    public class TankHealth : NetworkBehaviour
    {
        [Header("Wired by the prefab builder")]
        public Transform healthBarFill;   // scaled/offset to show remaining health
        public Renderer healthBarFillRenderer;

        public NetworkVariable<int> Health = new NetworkVariable<int>(GameConstants.MaxHealth);
        public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(false);

        Renderer[] _renderers;
        TankController _controller;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _controller = GetComponent<TankController>();
        }

        public override void OnNetworkSpawn()
        {
            Health.OnValueChanged += OnHealthChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            OnHealthChanged(0, Health.Value);
            OnDeadChanged(false, IsDead.Value);
        }

        public override void OnNetworkDespawn()
        {
            Health.OnValueChanged -= OnHealthChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
        }

        // ---------------------------------------------------------------- server

        /// <summary>Apply damage. Server only. attackerId gets kill credit.</summary>
        public void TakeDamage(int amount, ulong attackerId)
        {
            if (!IsServer || IsDead.Value) return;

            Health.Value = Mathf.Max(0, Health.Value - amount);
            if (Health.Value > 0) return;

            // Death.
            IsDead.Value = true;
            if (MatchManager.Instance != null)
                MatchManager.Instance.RegisterKill(attackerId, OwnerClientId);
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(GameConstants.RespawnDelay);
            if (!IsSpawned) yield break; // owner disconnected meanwhile

            // Pick a spawn point and tell the OWNER to teleport itself there
            // (owner-authoritative transform), then bring the tank back to life.
            var (pos, rot) = MatchManager.Instance != null
                ? MatchManager.Instance.GetRespawnPoint()
                : (transform.position, transform.rotation);

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            TeleportOwnerClientRpc(pos, rot, target);

            Health.Value = GameConstants.MaxHealth;
            IsDead.Value = false;
        }

        [ClientRpc]
        void TeleportOwnerClientRpc(Vector3 pos, Quaternion rot, ClientRpcParams _ = default)
        {
            _controller.TeleportLocal(pos, rot);
        }

        // --------------------------------------------------------------- visuals

        void OnHealthChanged(int _, int current)
        {
            // Overhead bar: shrink toward the left edge, green -> red.
            float pct = (float)current / GameConstants.MaxHealth;
            if (healthBarFill != null)
            {
                var s = healthBarFill.localScale;
                healthBarFill.localScale = new Vector3(Mathf.Max(0.001f, pct), s.y, s.z);
                healthBarFill.localPosition = new Vector3(-(1f - pct) * 0.5f, 0f, 0f);
            }
            if (healthBarFillRenderer != null)
                healthBarFillRenderer.material.color = Color.Lerp(Color.red, Color.green, pct);

            // Local HUD bar.
            if (IsOwner && HUDController.Instance != null)
                HUDController.Instance.SetHealth(pct);
        }

        void OnDeadChanged(bool _, bool dead)
        {
            // Hide/show the whole tank. Dead tanks are ignored by bullets.
            foreach (var r in _renderers) r.enabled = !dead;

            if (dead)
                AudioManager.Instance?.PlayExplosionAt(transform.position);

            if (IsOwner && HUDController.Instance != null)
            {
                if (dead) HUDController.Instance.ShowRespawnOverlay(GameConstants.RespawnDelay);
                else HUDController.Instance.HideRespawnOverlay();
            }
        }
    }
}
