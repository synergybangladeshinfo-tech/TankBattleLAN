using UnityEngine;

namespace TankBattle.Core
{
    /// <summary>Every on-screen control the player is allowed to move/resize.</summary>
    public enum ControlId
    {
        MoveStick = 0,
        AimStick = 1,
        Fire = 2,
        Dash = 3,
        Bomb = 4,
        Hover = 5
    }

    /// <summary>
    /// The player's custom control layout: where each button/stick sits, how big
    /// it is, plus global feel settings (opacity, handedness, aim sensitivity,
    /// auto-fire). Positions are stored in canvas reference space (1920x1080,
    /// origin at the bottom-left corner) so they are resolution independent -
    /// the CanvasScaler maps them onto any device.
    ///
    /// Everything persists through PlayerPrefs, so a layout survives app
    /// restarts. Three ready-made presets are provided for players who would
    /// rather not fiddle with it.
    /// </summary>
    public static class ControlLayout
    {
        public const int Count = 6;

        /// <summary>Canvas reference size the positions are expressed in.</summary>
        public static readonly Vector2 Reference = new Vector2(1920f, 1080f);

        /// <summary>UI labels, index-aligned with ControlId.</summary>
        public static readonly string[] Names =
        { "MOVE STICK", "AIM STICK", "FIRE", "DASH", "BOMB", "HOVER" };

        /// <summary>Base (unscaled) pixel size of each control.</summary>
        public static readonly float[] BaseSize =
        { 280f, 280f, 150f, 140f, 140f, 140f };

        // ---- persisted state (kept in memory, flushed to PlayerPrefs) ----

        static readonly Vector2[] _pos = new Vector2[Count];
        static readonly float[] _scale = new float[Count];
        static float _opacity = 0.6f;
        static float _sensitivity = 1f;
        static bool _leftHanded;
        static bool _autoFire = true;
        static bool _loaded;

        const string KeyLayout = "tb_ctrl_layout";
        const string KeyOpacity = "tb_ctrl_opacity";
        const string KeySens = "tb_ctrl_sens";
        const string KeyLeft = "tb_ctrl_lefthand";
        const string KeyAuto = "tb_ctrl_autofire";

        // ---- presets -------------------------------------------------------

        public static readonly string[] PresetNames = { "CLASSIC", "MINI MILITIA", "ONE HAND" };

        /// <summary>Preset positions [preset][control].</summary>
        static readonly Vector2[][] Presets =
        {
            // 0 - CLASSIC: sticks in the corners, action buttons in the middle.
            new[]
            {
                new Vector2(230f, 210f),    // move stick
                new Vector2(1690f, 210f),   // aim stick
                new Vector2(790f, 190f),    // fire
                new Vector2(960f, 190f),    // dash
                new Vector2(1125f, 190f),   // bomb
                new Vector2(960f, 350f)     // hover
            },
            // 1 - MINI MILITIA: move left, all actions clustered bottom-right.
            new[]
            {
                new Vector2(240f, 230f),
                new Vector2(1610f, 300f),
                new Vector2(1660f, 620f),
                new Vector2(1430f, 175f),
                new Vector2(1250f, 250f),
                new Vector2(1830f, 430f)
            },
            // 2 - ONE HAND: everything reachable with the right thumb.
            new[]
            {
                new Vector2(1560f, 200f),
                new Vector2(1560f, 200f),
                new Vector2(1810f, 430f),
                new Vector2(1810f, 620f),
                new Vector2(1600f, 640f),
                new Vector2(1360f, 560f)
            }
        };

        /// <summary>Per-preset scale multipliers (one-hand shrinks things a touch).</summary>
        static readonly float[] PresetScale = { 1f, 1f, 0.85f };

        // ---- api -----------------------------------------------------------

        public static float Opacity
        {
            get { EnsureLoaded(); return _opacity; }
            set { EnsureLoaded(); _opacity = Mathf.Clamp(value, 0.15f, 1f); }
        }

        /// <summary>Aim-stick turn speed multiplier (0.4 .. 2.5).</summary>
        public static float Sensitivity
        {
            get { EnsureLoaded(); return _sensitivity; }
            set { EnsureLoaded(); _sensitivity = Mathf.Clamp(value, 0.4f, 2.5f); }
        }

        /// <summary>Mirror the whole layout for left-handed players.</summary>
        public static bool LeftHanded
        {
            get { EnsureLoaded(); return _leftHanded; }
            set
            {
                EnsureLoaded();
                if (_leftHanded == value) return;
                _leftHanded = value;
                Mirror();
            }
        }

        /// <summary>True = holding the aim stick also fires (Mini Militia feel).</summary>
        public static bool AutoFire
        {
            get { EnsureLoaded(); return _autoFire; }
            set { EnsureLoaded(); _autoFire = value; }
        }

        public static Vector2 GetPos(ControlId id)
        {
            EnsureLoaded();
            return _pos[(int)id];
        }

        public static void SetPos(ControlId id, Vector2 pos)
        {
            EnsureLoaded();
            int i = (int)id;
            float half = BaseSize[i] * _scale[i] * 0.5f;
            // Keep every control fully on screen.
            pos.x = Mathf.Clamp(pos.x, half, Reference.x - half);
            pos.y = Mathf.Clamp(pos.y, half, Reference.y - half);
            _pos[i] = pos;
        }

        public static float GetScale(ControlId id)
        {
            EnsureLoaded();
            return _scale[(int)id];
        }

        public static void SetScale(ControlId id, float scale)
        {
            EnsureLoaded();
            _scale[(int)id] = Mathf.Clamp(scale, 0.6f, 1.7f);
            SetPos(id, _pos[(int)id]); // re-clamp: a bigger button may now overflow
        }

        /// <summary>Final on-screen pixel size of a control (base * its scale).</summary>
        public static float SizeOf(ControlId id) => BaseSize[(int)id] * GetScale(id);

        /// <summary>Load one of the built-in presets (does not save on its own).</summary>
        public static void ApplyPreset(int index)
        {
            EnsureLoaded();
            index = Mathf.Clamp(index, 0, Presets.Length - 1);
            for (int i = 0; i < Count; i++)
            {
                _scale[i] = PresetScale[index];
                _pos[i] = Presets[index][i];
            }
            if (_leftHanded) Mirror();
        }

        public static void ResetDefaults()
        {
            EnsureLoaded();
            _opacity = 0.6f;
            _sensitivity = 1f;
            _autoFire = true;
            _leftHanded = false;
            ApplyPreset(0);
        }

        /// <summary>Flip every control across the vertical centre line.</summary>
        static void Mirror()
        {
            for (int i = 0; i < Count; i++)
                _pos[i] = new Vector2(Reference.x - _pos[i].x, _pos[i].y);
        }

        // ---- persistence ---------------------------------------------------

        public static void Save()
        {
            EnsureLoaded();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(_pos[i].x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(',')
                  .Append(_pos[i].y.ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(',')
                  .Append(_scale[i].ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
            PlayerPrefs.SetString(KeyLayout, sb.ToString());
            PlayerPrefs.SetFloat(KeyOpacity, _opacity);
            PlayerPrefs.SetFloat(KeySens, _sensitivity);
            PlayerPrefs.SetInt(KeyLeft, _leftHanded ? 1 : 0);
            PlayerPrefs.SetInt(KeyAuto, _autoFire ? 1 : 0);
            PlayerPrefs.Save();
        }

        static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;   // set first: ApplyPreset below calls back in

            // Defaults, then overwrite from prefs when a saved layout exists.
            for (int i = 0; i < Count; i++)
            {
                _scale[i] = 1f;
                _pos[i] = Presets[0][i];
            }
            _opacity = PlayerPrefs.GetFloat(KeyOpacity, 0.6f);
            _sensitivity = PlayerPrefs.GetFloat(KeySens, 1f);
            _leftHanded = PlayerPrefs.GetInt(KeyLeft, 0) == 1;
            _autoFire = PlayerPrefs.GetInt(KeyAuto, 1) == 1;

            string raw = PlayerPrefs.GetString(KeyLayout, "");
            if (string.IsNullOrEmpty(raw)) return;

            string[] parts = raw.Split('|');
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < Count && i < parts.Length; i++)
            {
                string[] f = parts[i].Split(',');
                if (f.Length < 3) continue;
                if (float.TryParse(f[0], System.Globalization.NumberStyles.Float, ci, out float x) &&
                    float.TryParse(f[1], System.Globalization.NumberStyles.Float, ci, out float y) &&
                    float.TryParse(f[2], System.Globalization.NumberStyles.Float, ci, out float s))
                {
                    _pos[i] = new Vector2(x, y);
                    _scale[i] = Mathf.Clamp(s, 0.6f, 1.7f);
                }
            }
        }
    }
}
