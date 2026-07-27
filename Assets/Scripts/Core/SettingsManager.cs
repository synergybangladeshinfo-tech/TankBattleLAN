using UnityEngine;

namespace TankBattle.Core
{
    /// <summary>
    /// Persists user settings via PlayerPrefs and applies them to the engine.
    /// Quality levels are applied manually (shadows / AA / pixel lights) so the
    /// game behaves identically regardless of the project's Quality asset setup.
    /// Runs before the first scene loads.
    /// </summary>
    public static class SettingsManager
    {
        const string KeyMusic = "tb_music_on";
        const string KeySfx = "tb_sfx_on";
        const string KeyQuality = "tb_quality"; // 0 = Low, 1 = Medium, 2 = High
        const string KeyName = "tb_player_name";
        const string KeyTankColor = "tb_tank_color";
        const string KeyTankStyle = "tb_tank_style";
        const string KeyTankPattern = "tb_tank_pattern";
        const string KeyVibration = "tb_vibration";
        const string KeyBotDiff = "tb_bot_difficulty";
        const string KeyHints = "tb_first_hints";
        const string KeyMusicVol = "tb_music_vol";
        const string KeySfxVol = "tb_sfx_vol";
        const string KeyShake = "tb_cam_shake";
        const string KeyAimAssist = "tb_aim_assist";
        const string KeyMinimap = "tb_show_minimap";
        const string KeyFps = "tb_show_fps";
        const string KeyFrameCap = "tb_frame_cap";  // 0 = 30, 1 = 60, 2 = uncapped
        const string KeyCloudId = "tb_cloud_project_id";
        const string KeyLastRoom = "tb_last_room_code";

        public static bool MusicOn
        {
            get => PlayerPrefs.GetInt(KeyMusic, 1) == 1;
            set { PlayerPrefs.SetInt(KeyMusic, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        public static bool SfxOn
        {
            get => PlayerPrefs.GetInt(KeySfx, 1) == 1;
            set { PlayerPrefs.SetInt(KeySfx, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Music loudness, 0..1. Independent of the on/off switch.</summary>
        public static float MusicVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusicVol, 0.55f));
            set { PlayerPrefs.SetFloat(KeyMusicVol, Mathf.Clamp01(value)); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Sound-effect loudness, 0..1.</summary>
        public static float SfxVolume
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfxVol, 1f));
            set { PlayerPrefs.SetFloat(KeySfxVol, Mathf.Clamp01(value)); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Camera shake on firing and explosions.</summary>
        public static bool CameraShakeOn
        {
            get => PlayerPrefs.GetInt(KeyShake, 1) == 1;
            set { PlayerPrefs.SetInt(KeyShake, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Turret lock-on. Off = fully manual aiming.</summary>
        public static bool AimAssistOn
        {
            get => PlayerPrefs.GetInt(KeyAimAssist, 1) == 1;
            set { PlayerPrefs.SetInt(KeyAimAssist, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Show the corner minimap during a match.</summary>
        public static bool ShowMinimap
        {
            get => PlayerPrefs.GetInt(KeyMinimap, 1) == 1;
            set { PlayerPrefs.SetInt(KeyMinimap, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>Show a live FPS readout.</summary>
        public static bool ShowFps
        {
            get => PlayerPrefs.GetInt(KeyFps, 0) == 1;
            set { PlayerPrefs.SetInt(KeyFps, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>0 = 30 FPS (saves battery), 1 = 60 FPS, 2 = uncapped.</summary>
        public static int FrameCap
        {
            get => PlayerPrefs.GetInt(KeyFrameCap, 1);
            set
            {
                PlayerPrefs.SetInt(KeyFrameCap, Mathf.Clamp(value, 0, 2));
                PlayerPrefs.Save();
                ApplyFrameCap();
                OnChanged?.Invoke();
            }
        }

        static void ApplyFrameCap()
        {
            switch (PlayerPrefs.GetInt(KeyFrameCap, 1))
            {
                case 0: Application.targetFrameRate = 30; break;
                case 2: Application.targetFrameRate = -1; break;
                default: Application.targetFrameRate = 60; break;
            }
        }

        /// <summary>Phone vibration on firing, taking hits and dying.</summary>
        public static bool VibrationOn
        {
            get => PlayerPrefs.GetInt(KeyVibration, 1) == 1;
            set { PlayerPrefs.SetInt(KeyVibration, value ? 1 : 0); PlayerPrefs.Save(); OnChanged?.Invoke(); }
        }

        /// <summary>0 = Low, 1 = Medium, 2 = High.</summary>
        public static int Quality
        {
            get => PlayerPrefs.GetInt(KeyQuality, 1);
            set { PlayerPrefs.SetInt(KeyQuality, Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); ApplyQuality(); OnChanged?.Invoke(); }
        }

        public static string SavedPlayerName
        {
            get => PlayerPrefs.GetString(KeyName, "");
            set { PlayerPrefs.SetString(KeyName, value); PlayerPrefs.Save(); }
        }

        /// <summary>Garage: saved tank color index.</summary>
        public static int SavedTankColor
        {
            get => PlayerPrefs.GetInt(KeyTankColor, 0);
            set { PlayerPrefs.SetInt(KeyTankColor, value); PlayerPrefs.Save(); }
        }

        /// <summary>Garage: saved tank body style index.</summary>
        public static int SavedTankStyle
        {
            get => PlayerPrefs.GetInt(KeyTankStyle, 0);
            set { PlayerPrefs.SetInt(KeyTankStyle, value); PlayerPrefs.Save(); }
        }

        /// <summary>Garage: saved hull pattern index.</summary>
        public static int SavedTankPattern
        {
            get => PlayerPrefs.GetInt(KeyTankPattern, 0);
            set { PlayerPrefs.SetInt(KeyTankPattern, value); PlayerPrefs.Save(); }
        }

        /// <summary>Solo-mode AI difficulty (0 Easy / 1 Normal / 2 Hard).</summary>
        public static int SavedBotDifficulty
        {
            get => PlayerPrefs.GetInt(KeyBotDiff, 1);
            set { PlayerPrefs.SetInt(KeyBotDiff, Mathf.Clamp(value, 0, 2)); PlayerPrefs.Save(); }
        }

        /// <summary>True until the player has seen the one-time control hints.</summary>
        public static bool ShowFirstTimeHints
        {
            get => PlayerPrefs.GetInt(KeyHints, 1) == 1;
            set { PlayerPrefs.SetInt(KeyHints, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// Unity Cloud project id used for online (Relay) play. Normally baked
        /// into the build, but it can be pasted in the Online screen so online
        /// can be switched on without waiting for a new APK.
        /// </summary>
        public static string CloudProjectId
        {
            get => PlayerPrefs.GetString(KeyCloudId, "");
            set { PlayerPrefs.SetString(KeyCloudId, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Last room code typed on the Join Online screen.</summary>
        public static string LastRoomCode
        {
            get => PlayerPrefs.GetString(KeyLastRoom, "");
            set { PlayerPrefs.SetString(KeyLastRoom, value ?? ""); PlayerPrefs.Save(); }
        }

        /// <summary>Raised whenever any setting changes (AudioManager listens).</summary>
        public static System.Action OnChanged;

        /// <summary>
        /// Called automatically before the first scene loads.
        /// Locks the frame rate to 60 and applies the saved quality level.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            QualitySettings.vSyncCount = 0;     // targetFrameRate governs pacing
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            ApplyFrameCap();
            ApplyQuality();
        }

        static void ApplyQuality()
        {
            switch (PlayerPrefs.GetInt(KeyQuality, 1))
            {
                case 0: // Low - lowest cost for weak devices
                    QualitySettings.shadows = ShadowQuality.Disable;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.pixelLightCount = 1;
                    QualitySettings.lodBias = 0.7f;
                    break;
                case 1: // Medium
                    QualitySettings.shadows = ShadowQuality.HardOnly;
                    QualitySettings.shadowDistance = 40f;
                    QualitySettings.antiAliasing = 0;
                    QualitySettings.pixelLightCount = 2;
                    QualitySettings.lodBias = 1f;
                    break;
                default: // High
                    QualitySettings.shadows = ShadowQuality.All;
                    QualitySettings.shadowDistance = 60f;
                    QualitySettings.antiAliasing = 2;
                    QualitySettings.pixelLightCount = 4;
                    QualitySettings.lodBias = 1.5f;
                    break;
            }
        }
    }
}
