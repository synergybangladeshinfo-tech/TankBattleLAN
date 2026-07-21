using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Applies all Android player/build settings for a production APK:
    /// IL2CPP + ARM64/ARMv7, landscape only, 60 FPS-friendly options, and the
    /// INTERNET permission (required by Android for ANY socket use, including
    /// pure-LAN UDP - the game never talks to the internet).
    /// </summary>
    public static class AndroidConfig
    {
        public static void Apply()
        {
            // Identity.
            PlayerSettings.productName = "Tank Battle LAN";
            PlayerSettings.companyName = "TankBattleLAN";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.tankbattlelan.game");
            PlayerSettings.bundleVersion = "2.0.0";
            PlayerSettings.Android.bundleVersionCode = 2;

            // Scripting: IL2CPP release for both mainstream ABIs.
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures =
                AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Android,
                Il2CppCompilerConfiguration.Release);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android,
                ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android,
                ManagedStrippingLevel.Low); // safe with reflection-free code

            // OS versions.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel23;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // Orientation: landscape both ways, no portrait.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // Networking permission: Android mandates INTERNET for any socket,
            // even when traffic never leaves the local network. No other
            // permissions are requested; there are no ads, logins or analytics.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.forceSDCardPermission = false;

            // Small perf wins for low-end devices.
            PlayerSettings.accelerometerFrequency = 0; // sensor not used

            // Plain APK output (not an app bundle).
            EditorUserBuildSettings.buildAppBundle = false;

            Debug.Log("[TankBattle] Android player settings applied.");
        }

        /// <summary>Builds Builds/TankBattleLAN.apk from the registered scenes.</summary>
        public static void BuildApk()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                        BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Debug.LogError("[TankBattle] Could not switch to the Android build target. " +
                                   "Install Android Build Support via Unity Hub.");
                    return;
                }
            }

            Apply();

            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0)
            {
                Debug.LogError("[TankBattle] No scenes in build settings. " +
                               "Run 'Tank Battle/1. Generate Everything' first.");
                return;
            }

            System.IO.Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(
                scenes, "Builds/TankBattleLAN.apk", BuildTarget.Android, BuildOptions.None);

            if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                Debug.Log($"[TankBattle] APK built: {report.summary.outputPath} " +
                          $"({report.summary.totalSize / (1024 * 1024)} MB)");
            else
                Debug.LogError("[TankBattle] Build failed - see the console for details.");
        }
    }
}
