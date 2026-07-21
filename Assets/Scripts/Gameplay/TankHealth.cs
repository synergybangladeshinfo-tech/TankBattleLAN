using System.Collections;
using System.Collections.Generic;
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
    ///   a spawn point (unless the mode forbids it, e.g. out of lives in Last
    ///   Tank Standing). Because the tank transform is owner-authoritative, the
    ///   actual teleport is executed by the owner via a targeted ClientRpc.
    /// - Visuals (renderers, overhead bar, damage smoke, hit sparks, explosion)
    ///   react to the replicated variables on every client.
    /// </summary>
    public class TankHealth : NetworkBehaviour
    {
        [Header("Wired by the prefab builder")]
        public Transform healthBarFill;   // scaled/offset to show remaining health
        public Renderer healthBarFillRenderer;

        public NetworkVariable<int> Health = new NetworkVariable<int>(GameConstants.MaxHealth);
        public NetworkVariable<bool> IsDead = new NetworkVariable<bool>(false);

        /// <summary>True while a shield pickup is protecting this tank (invincible).</summary>
        public NetworkVariable<bool> Shielded = new NetworkVariable<bool>(false);

        /// <summary>Every spawned tank (players AND bots) - used for targeting/aim/splash.</summary>
        public static readonly List<TankHealth> All = new List<TankHealth>();

        Renderer[] _renderers;
        TankController _controller;
        ParticleSystem _smoke, _sparks, _explosion;
        Transform _shieldBubble;   // translucent sphere shown while shielded
        Coroutine _shieldRoutine;
        BotTank _bot;
        bool _botChecked;

        /// <summary>
        /// Identity used by scoring: the owning client for players, or the
        /// fake bot id for AI tanks (which are server-owned, so OwnerClientId
        /// alone could collide with the host player's id).
        /// </summary>
        public ulong ActorId
        {
            get
            {
                if (!_botChecked) { _bot = GetComponent<BotTank>(); _botChecked = true; }
                return _bot != null ? _bot.BotId : OwnerClientId;
            }
        }

        void Awake()
        {
            _controller = GetComponent<TankController>();

            // Particle children are wired by name (built into the prefab).
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.name == "SmokePS") _smoke = ps;
                else if (ps.name == "HitSparkPS") _sparks = ps;
                else if (ps.name == "ExplosionPS") _explosion = ps;
            }

            var bubble = transform.Find("ShieldBubble");
            if (bubble != null) { _shieldBubble = bubble; _shieldBubble.gameObject.SetActive(false); }
        }

        public override void OnNetworkSpawn()
        {
            if (!All.Contains(this)) All.Add(this);

            // Mesh renderers only: particles and the name tag manage themselves.
            _renderers = GetComponentsInChildren<MeshRenderer>(true);

            Health.OnValueChanged += OnHealthChanged;
            IsDead.OnValueChanged += OnDeadChanged;
            Shielded.OnValueChanged += OnShieldChanged;
            OnHealthChanged(GameConstants.MaxHealth, Health.Value);
            OnDeadChanged(false, IsDead.Value);
            OnShieldChanged(false, Shielded.Value);
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            Health.OnValueChanged -= OnHealthChanged;
            IsDead.OnValueChanged -= OnDeadChanged;
            Shielded.OnValueChanged -= OnShieldChanged;
        }

        // ---------------------------------------------------------------- server

        /// <summary>Server: grant (or refresh) the 2-minute invincibility shield.</summary>
        public void ServerGrantShield()
        {
            if (!IsServer) return;
            if (_shieldRoutine != null) StopCoroutine(_shieldRoutine);
            _shieldRoutine = StartCoroutine(ShieldRoutine());
        }

        IEnumerator ShieldRoutine()
        {
            Shielded.Value = true;
            yield return new WaitForSeconds(GameConstants.ShieldSeconds);
            Shielded.Value = false;
            _shieldRoutine = null;
        }

        /// <summary>Apply damage. Server only. attackerId gets kill credit.</summary>
        public void TakeDamage(int amount, ulong attackerId)
        {
            if (!IsServer || IsDead.Value) return;
            if (Shielded.Value) return; // invincible while shielded

            Health.Value = Mathf.Max(0, Health.Value - amount);
            if (Health.Value > 0) return;

            // Death.
            IsDead.Value = true;
            if (MatchManager.Instance != null)
                MatchManager.Instance.RegisterKill(attackerId, ActorId);
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(GameConstants.RespawnDelay);
            if (!IsSpawned) yield break; // owner disconnected meanwhile

            // Mode gate: in Last Tank Standing a player can run out of lives.
            if (MatchManager.Instance != null &&
                !MatchManager.Instance.AllowRespawn(ActorId))
                yield break; // stays dead (spectating until the match ends)

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

            // Losing the shield on death would be unfair mid-timer; keep it,
            // but a fresh spawn with full health + shield is fine.
        }

        [ClientRpc]
        void TeleportOwnerClientRpc(Vector3 pos, Quaternion rot, ClientRpcParams _ = default)
        {
            _controller.TeleportLocal(pos, rot);
        }

        // --------------------------------------------------------------- visuals

        void OnHealthChanged(int previous, int current)
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

            // Impact feedback on every client: sparks on damage, smoke when low.
            if (current < previous && current > 0 && _sparks != null) _sparks.Play();
            if (_smoke != null)
            {
                bool low = pct > 0f && pct <= 0.35f;
                if (low && !_smoke.isPlaying) _smoke.Play();
                else if (!low && _smoke.isPlaying)
                    _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            // Local HUD bar.
            if (IsOwner && HUDController.Instance != null)
                HUDController.Instance.SetHealth(pct);
        }

        void OnDeadChanged(bool _, bool dead)
        {
            // Hide/show the whole tank. Dead tanks are ignored by bullets.
            foreach (var r in _renderers)
                if (r != null) r.enabled = !dead;

            if (dead)
            {
                if (_explosion != null) _explosion.Play();
                if (_smoke != null) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                AudioManager.Instance?.PlayExplosionAt(transform.position);
                TankBattle.Utils.CameraFollow.Instance?.ShakeAt(transform.position, 0.7f);
            }

            if (IsOwner && HUDController.Instance != null)
            {
                if (dead) HUDController.Instance.ShowRespawnOverlay(GameConstants.RespawnDelay);
                else HUDController.Instance.HideRespawnOverlay();
            }
        }

        void OnShieldChanged(bool _, bool on)
        {
            if (_shieldBubble != null) _shieldBubble.gameObject.SetActive(on);
        }
    }
}
