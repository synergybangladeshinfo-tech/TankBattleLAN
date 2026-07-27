using Unity.Netcode;
using UnityEngine;
using TankBattle.Audio;
using TankBattle.Core;
using TankBattle.Utils;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// A prop that can be shot to pieces: crates and barrels placed in the map.
    /// Explosive ones (fuel barrels) take everything nearby with them, which
    /// turns cover into a trap and gives you a way to flush out a camper.
    ///
    /// These are in-scene NetworkObjects: the server owns the health and decides
    /// when something breaks, then tells every client to play the break so the
    /// visuals match everywhere. Bullets and blasts call TakeDamage on the
    /// server only.
    /// </summary>
    public class Destructible : NetworkBehaviour
    {
        [Header("Set by the scene builder")]
        public int maxHealth = 40;

        /// <summary>Fuel barrels detonate and hurt everything in range.</summary>
        public bool explosive;

        public float blastRadius = 6f;
        public int blastDamage = 55;

        /// <summary>Replicated so late joiners see already-broken props.</summary>
        readonly NetworkVariable<bool> _broken = new NetworkVariable<bool>(false);

        int _health;
        Renderer[] _renderers;
        Collider[] _colliders;

        void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _colliders = GetComponentsInChildren<Collider>(true);
        }

        public override void OnNetworkSpawn()
        {
            _health = maxHealth;
            _broken.OnValueChanged += OnBrokenChanged;
            // A client joining mid-match must not see props that are already gone.
            if (_broken.Value) Hide();
        }

        public override void OnNetworkDespawn()
        {
            _broken.OnValueChanged -= OnBrokenChanged;
        }

        /// <summary>Server: damage this prop. attackerId gets credit for any kills.</summary>
        public void TakeDamage(int amount, ulong attackerId)
        {
            if (!IsServer || _broken.Value) return;

            _health -= amount;
            if (_health > 0) return;

            _broken.Value = true;

            if (explosive)
            {
                // Chain reaction: hurt tanks AND set off neighbouring barrels.
                Vector3 centre = transform.position;

                for (int i = 0; i < TankHealth.All.Count; i++)
                {
                    var h = TankHealth.All[i];
                    if (h == null || h.IsDead.Value) continue;
                    float d = Vector3.Distance(h.transform.position, centre);
                    if (d > blastRadius) continue;
                    float falloff = Mathf.Lerp(1f, 0.35f, d / blastRadius);
                    h.TakeDamage(Mathf.RoundToInt(blastDamage * falloff), attackerId);
                }

                var others = FindObjectsByType<Destructible>(FindObjectsSortMode.None);
                for (int i = 0; i < others.Length; i++)
                {
                    var o = others[i];
                    if (o == null || o == this || o._broken.Value || !o.explosive) continue;
                    if (Vector3.Distance(o.transform.position, centre) > blastRadius) continue;
                    // Small delay so a barrel field pops one after another.
                    o.Invoke(nameof(ChainDetonate), Random.Range(0.08f, 0.3f));
                }
            }

            BreakClientRpc(explosive);
        }

        void ChainDetonate()
        {
            if (IsServer && !_broken.Value) TakeDamage(9999, OwnerClientId);
        }

        void OnBrokenChanged(bool _, bool broken)
        {
            if (broken) Hide();
        }

        [ClientRpc]
        void BreakClientRpc(bool wasExplosive)
        {
            Vector3 p = transform.position;

            if (wasExplosive)
            {
                ImpactFx.Explosion(p, blastRadius * 0.5f);
                ImpactFx.Debris(p + Vector3.up * 0.5f, Vector3.up, 14, 6f, 0.2f,
                    new Color(0.35f, 0.18f, 0.08f));
                AudioManager.Instance?.PlayExplosionAt(p);
                CameraFollow.Instance?.ShakeAt(p, 0.8f);
            }
            else
            {
                // Splintered crate: lighter, browner chunks, no blast.
                ImpactFx.Debris(p + Vector3.up * 0.4f, Vector3.up, 9, 3.2f, 0.16f,
                    new Color(0.45f, 0.32f, 0.18f));
                AudioManager.Instance?.PlayHitAt(p);
                CameraFollow.Instance?.ShakeAt(p, 0.25f);
            }

            Hide();
        }

        /// <summary>Remove the prop from view and from collision.</summary>
        void Hide()
        {
            for (int i = 0; i < _renderers.Length; i++)
                if (_renderers[i] != null) _renderers[i].enabled = false;
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = false;
        }

        /// <summary>Server-side helper used by splash damage from other sources.</summary>
        public static void DamageInRadius(Vector3 centre, float radius, int damage, ulong attackerId)
        {
            var all = FindObjectsByType<Destructible>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d == null || d._broken.Value) continue;
                float dist = Vector3.Distance(d.transform.position, centre);
                if (dist > radius) continue;
                d.TakeDamage(Mathf.RoundToInt(damage * Mathf.Lerp(1f, 0.4f, dist / radius)),
                             attackerId);
            }
            _ = GameConstants.MaxHealth; // (keeps the Core using-directive meaningful)
        }
    }
}
