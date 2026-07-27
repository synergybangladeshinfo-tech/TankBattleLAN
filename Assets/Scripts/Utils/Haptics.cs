using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Utils
{
    /// <summary>
    /// Phone vibration feedback, respecting the player's Settings toggle.
    ///
    /// Unity's cross-platform Handheld.Vibrate() is a single fixed-length buzz
    /// with no intensity control, so on Android we call the OS vibrator directly
    /// to get short taps for firing and longer rumbles for explosions. Any
    /// failure (emulator, no vibrator, permission missing) is swallowed - haptics
    /// are a nicety and must never break the game.
    /// </summary>
    public static class Haptics
    {
        /// <summary>Light tap - firing a shot.</summary>
        public static void Light() => Buzz(18);

        /// <summary>Medium - taking a hit.</summary>
        public static void Medium() => Buzz(45);

        /// <summary>Heavy - your tank is destroyed.</summary>
        public static void Heavy() => Buzz(140);

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static bool _looked;

        static AndroidJavaObject Vibrator()
        {
            if (_looked) return _vibrator;
            _looked = true;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                }
            }
            catch { _vibrator = null; }
            return _vibrator;
        }
#endif

        static void Buzz(long milliseconds)
        {
            if (!SettingsManager.VibrationOn) return;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var v = Vibrator();
                if (v == null) return;
                // API 26+ wants a VibrationEffect; older devices take a raw duration.
                if (GetApiLevel() >= 26)
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    {
                        var effect = effectClass.CallStatic<AndroidJavaObject>(
                            "createOneShot", milliseconds, -1 /* DEFAULT_AMPLITUDE */);
                        v.Call("vibrate", effect);
                    }
                }
                else
                {
                    v.Call("vibrate", milliseconds);
                }
            }
            catch { /* haptics are optional - never let them break play */ }
#else
            _ = milliseconds;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static int _apiLevel = -1;
        static int GetApiLevel()
        {
            if (_apiLevel > 0) return _apiLevel;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                    _apiLevel = version.GetStatic<int>("SDK_INT");
            }
            catch { _apiLevel = 21; }
            return _apiLevel;
        }
#endif
    }
}
