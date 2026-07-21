using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// The King of the Hill capture zone (centre of every map). The server
    /// scores players standing inside it (see MatchManager.TickKingOfTheHill);
    /// this component only handles the visual: a glowing pulsing ring that is
    /// hidden automatically in every other game mode.
    /// </summary>
    public class KothZone : MonoBehaviour
    {
        public static KothZone Instance { get; private set; }

        Renderer[] _renderers;
        bool _visibilityApplied;

        void Awake()
        {
            Instance = this;
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned) return;

            bool active = match.CurrentMode == GameMode.KingOfTheHill;
            if (!_visibilityApplied)
            {
                foreach (var r in _renderers) r.enabled = active;
                _visibilityApplied = true;
            }
            if (!active) return;

            // Gentle golden pulse so the zone reads clearly from anywhere.
            float pulse = 0.55f + Mathf.PingPong(Time.time * 0.5f, 0.3f);
            foreach (var r in _renderers)
            {
                var c = r.material.color;
                r.material.color = new Color(c.r, c.g, c.b, pulse * 0.5f);
            }
            float s = 1f + Mathf.Sin(Time.time * 2f) * 0.02f;
            transform.localScale = new Vector3(s, 1f, s);
        }
    }
}
