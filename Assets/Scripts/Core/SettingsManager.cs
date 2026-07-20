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

        /// <summary>Raised whenever any setting changes (AudioManager listens).</summary>
        public static System.Action OnChanged;

        /// <summary>
        /// Called automatically before the first scene loads.
        /// Locks the frame rate to 60 and applies the saved quality level.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            Application.targetFrameRate = 60;   // mobile: cap at 60 FPS
            QualitySettings.vSyncCount = 0;     // targetFrameRate governs pacing
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
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
