using UnityEditor;
using UnityEngine;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// One-click project generation. After Unity imports the scripts, run:
    ///   Tank Battle > 1. Generate Everything
    /// to create all materials, prefabs, the MainMenu scene, the five map
    /// scenes, the build-settings scene list and the Android player settings.
    /// Then (optionally) Tank Battle > 2. Build Android APK.
    ///
    /// Everything is idempotent - running it again simply regenerates assets.
    /// </summary>
    public static class TankBattleSetup
    {
        [MenuItem("Tank Battle/1. Generate Everything", priority = 0)]
        public static void GenerateEverything()
        {
            EnsureFolders();

            // 0. Procedural textures + the shared post-processing profile.
            TextureBuilder.GenerateAll();
            PostFXBuilder.BuildProfile();

            // 1. Prefabs (also creates the shared materials).
            var tank = PrefabBuilder.BuildTankPrefab();
            var bullet = PrefabBuilder.BuildBulletPrefab();
            var pickup = PrefabBuilder.BuildPickupPrefab();
            PrefabBuilder.BuildPreviewPrefab(); // Garage 3D preview (Resources)
            var netMgr = PrefabBuilder.BuildNetworkManagerPrefab(tank, bullet, pickup);
            Debug.Log("[TankBattle] Prefabs generated.");

            // 2. Scenes.
            SceneBuilder.BuildAllMaps();
            SceneBuilder.BuildMainMenuScene(netMgr); // last -> stays open
            SceneBuilder.RegisterScenesInBuildSettings();
            Debug.Log("[TankBattle] Scenes generated and registered in Build Settings.");

            // 3. App icon (drawn in code) + Android settings.
            IconBuilder.BuildAndAssignIcon();
            AndroidConfig.Apply();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[TankBattle] DONE. MainMenu scene is open - press Play to test, " +
                      "or run 'Tank Battle/2. Build Android APK'.");
        }

        [MenuItem("Tank Battle/2. Build Android APK", priority = 1)]
        public static void BuildApk() => AndroidConfig.BuildApk();

        [MenuItem("Tank Battle/Apply Android Settings Only", priority = 20)]
        public static void ApplyAndroidSettings() => AndroidConfig.Apply();

        static void EnsureFolders()
        {
            CreateFolder("Assets", "Prefabs");
            CreateFolder("Assets", "Materials");
            CreateFolder("Assets", "Scenes");
        }

        static void CreateFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}"))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
