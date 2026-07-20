using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Headless build entry point for CI (GitHub Actions / GameCI).
    /// Generates all scenes/prefabs first, then builds the Android APK to the
    /// path GameCI passes via -customBuildPath. Exits non-zero on failure so
    /// the CI job is marked failed.
    /// </summary>
    public static class CiBuild
    {
        public static void Build()
        {
            try
            {
                // 1. Generate prefabs, scenes, build settings, Android config.
                TankBattleSetup.GenerateEverything();

                // 2. Resolve output path (GameCI passes -customBuildPath).
                string path = GetArg("-customBuildPath");
                if (string.IsNullOrEmpty(path))
                    path = "build/Android/TankBattleLAN.apk";
                if (!path.EndsWith(".apk")) path = Path.Combine(path, "TankBattleLAN.apk");
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                // 3. Build.
                var scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled).Select(s => s.path).ToArray();
                var report = BuildPipeline.BuildPlayer(
                    scenes, path, BuildTarget.Android, BuildOptions.None);

                if (report.summary.result !=
                    UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    Debug.LogError("[CiBuild] Build failed.");
                    EditorApplication.Exit(1);
                    return;
                }
                Debug.Log($"[CiBuild] APK written to {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[CiBuild] Exception: {e}");
                EditorApplication.Exit(1);
            }
        }

        static string GetArg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
