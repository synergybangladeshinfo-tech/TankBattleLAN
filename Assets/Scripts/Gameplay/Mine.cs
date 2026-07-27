using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Core;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Proximity mine dropped by the MINE weapon. Server-authoritative:
    /// the server arms it after a short delay, watches for an enemy stepping
    /// inside the trigger radius, then deals splash damage and despawns.
    /// Clients only see the blinking light and the explosion.
    /// </summary>
    public class Mine : NetworkBehaviour
    {
        /// <summary>Who planted it (never damages its owner's team).</summary>
        ulong _ownerActorId;
        int _ownerTeam = -1;

        float _armedAt;
        float _expireAt;
        bool _exploded;

        /// <summary>Blink state replicated implicitly - clients animate locally.</summary>
        Renderer _lightRenderer;
        ParticleSystem _explosion;

        void Awake()
        {
            var lightTf = transform.Find("Light");
            if (lightTf != null) _lightRenderer = lightTf.GetComponent<Renderer>();
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name == "ExplosionPS") { _explosion = ps; break; }
        }

        /// <summary>Server: call immediately after Instantiate, before Spawn.</summary>
        public void Init(ulong ownerActorId, int ownerTeam)
        {
            _ownerActorId = ownerActorId;
            _ownerTeam = ownerTeam;
        }

        public override void OnNetworkSpawn()
        {
            _armedAt = Time.time + GameConstants.MineArmDelay;
            _expireAt = Time.time + GameConstants.MineLifetime;
        }

        void Update()
        {
            // Everyone: pulse the little light so mines are spottable if you look.
            if (_lightRenderer != null)
            {
                bool armed = Time.time >= _armedAt;
                float blink = armed
                    ? Mathf.PingPong(Time.time * 3f, 1f)
                    : Mathf.PingPong(Time.time * 8f, 1f);   // fast blink while arming
                _lightRenderer.material.color =
                    Color.Lerp(new Color(0.35f, 0.05f, 0.05f),
                               armed ? new Color(1f, 0.2f, 0.15f) : new Color(1f, 0.85f, 0.2f),
                               blink);
            }

            if (!IsServer || _exploded) return;

            if (Time.time >= _expireAt) { Despawn(); return; }
            if (Time.time < _armedAt) return;

            // Armed: look for an enemy standing on it.
            for (int i = 0; i < TankHealth.All.Count; i++)
            {
                var h = TankHealth.All[i];
                if (h == null || h.IsDead.Value) continue;
                if (h.ActorId == _ownerActorId) continue;

                if (_ownerTeam >= 0)
                {
                    var tc = h.GetComponent<TankController>();
                    if (tc != null && tc.TeamIndex.Value == _ownerTeam) continue;
                }

                Vector3 to = h.transform.position - transform.position;
                to.y *= 0.6f; // a little vertical tolerance for ramps/platforms
                if (to.sqrMagnitude <= GameConstants.MineTriggerRadius *
                                       GameConstants.MineTriggerRadius)
                {
                    Explode();
                    return;
                }
            }
        }

        /// <summary>Server: blow up, damaging every enemy inside the splash radius.</summary>
        void Explode()
        {
            if (_exploded) return;
            _exploded = true;

            Vector3 centre = transform.position;
            for (int i = 0; i < TankHealth.All.Count; i++)
            {
                var h = TankHealth.All[i];
                if (h == null || h.IsDead.Value) continue;
                if (_ownerTeam >= 0 && h.ActorId != _ownerActorId)
                {
                    var tc = h.GetComponent<TankController>();
                    if (tc != null && tc.TeamIndex.Value == _ownerTeam) continue;
                }

                float d = Vector3.Distance(h.transform.position, centre);
                if (d > GameConstants.MineSplashRadius) continue;

                float falloff = Mathf.Lerp(1f, 0.4f, d / GameConstants.MineSplashRadius);
                h.TakeDamage(Mathf.RoundToInt(GameConstants.MineDamage * falloff), _ownerActorId);
            }

            BoomClientRpc(centre);
            Invoke(nameof(Despawn), 0.6f); // let the FX play before it disappears
        }

        [ClientRpc]
        void BoomClientRpc(Vector3 pos)
        {
            if (_explosion != null) _explosion.Play();
            AudioManager.Instance?.PlayExplosionAt(pos);
            TankBattle.Utils.CameraFollow.Instance?.ShakeAt(pos, 0.6f);

            // Hide the body immediately; the particles finish on their own.
            foreach (var r in GetComponentsInChildren<MeshRenderer>(true))
                r.enabled = false;
        }

        void Despawn()
        {
            if (IsServer && IsSpawned) NetworkObject.Despawn(true);
        }
    }
}
