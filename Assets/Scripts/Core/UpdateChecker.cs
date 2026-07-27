using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace TankBattle.Core
{
    /// <summary>
    /// In-app update check.
    ///
    /// The problem this solves: every new version used to mean sending the APK
    /// to each friend by hand. Now the game checks a small version file on
    /// GitHub when the menu opens. If a newer build exists it shows an UPDATE
    /// banner, and one tap opens the download link - Android installs it over
    /// the top, keeping the player's name, garage and control layout.
    ///
    /// The check is a single ~200 byte request, runs in the background, and any
    /// failure (no internet, GitHub down) is silent: the game is fully playable
    /// offline and must never be blocked by it.
    /// </summary>
    public class UpdateChecker : MonoBehaviour
    {
        /// <summary>Raw version file in the repo. Small, cached by GitHub's CDN.</summary>
        const string VersionUrl =
            "https://raw.githubusercontent.com/synergybangladeshinfo-tech/TankBattleLAN/main/version.json";

        /// <summary>Fallback if the version file cannot be parsed for a URL.</summary>
        const string FallbackApkUrl =
            "https://github.com/synergybangladeshinfo-tech/TankBattleLAN/releases/latest/download/TankBattleLAN.apk";

        /// <summary>Version code of THIS build. Bumped alongside AndroidConfig.</summary>
        public const int InstalledVersionCode = 12;

        [System.Serializable]
        class VersionInfo
        {
            public int versionCode;
            public string version;
            public string apkUrl;
            public string notes;
        }

        /// <summary>True once a newer build has been found.</summary>
        public static bool UpdateAvailable { get; private set; }
        public static string LatestVersion { get; private set; } = "";
        public static string LatestNotes { get; private set; } = "";
        public static string DownloadUrl { get; private set; } = FallbackApkUrl;

        /// <summary>Raised on the main thread when an update is found.</summary>
        public static System.Action OnUpdateFound;

        /// <summary>Kick off a background check. Safe to call more than once.</summary>
        public static void Check(MonoBehaviour host)
        {
            if (host == null || UpdateAvailable) return;
            host.StartCoroutine(Run());
        }

        static IEnumerator Run()
        {
            // Cache-buster: GitHub's raw CDN holds files for a few minutes.
            string url = VersionUrl + "?t=" + System.DateTime.UtcNow.Ticks;

            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                    yield break;              // offline - stay quiet

                VersionInfo info = null;
                try { info = JsonUtility.FromJson<VersionInfo>(req.downloadHandler.text); }
                catch { yield break; }

                if (info == null || info.versionCode <= InstalledVersionCode)
                    yield break;              // already up to date

                UpdateAvailable = true;
                LatestVersion = string.IsNullOrEmpty(info.version) ? "new" : info.version;
                LatestNotes = info.notes ?? "";
                if (!string.IsNullOrEmpty(info.apkUrl)) DownloadUrl = info.apkUrl;

                OnUpdateFound?.Invoke();
            }
        }

        /// <summary>
        /// Open the APK link. Android downloads it in the browser and the player
        /// taps the notification to install over the existing app - no uninstall,
        /// no data loss. Nothing is downloaded silently, so the game needs no
        /// install permission.
        /// </summary>
        public static void OpenDownload()
        {
            Application.OpenURL(DownloadUrl);
        }
    }
}
