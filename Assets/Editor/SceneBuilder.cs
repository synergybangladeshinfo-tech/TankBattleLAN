using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using TankBattle.Audio;
using TankBattle.Core;
using TankBattle.Gameplay;
using TankBattle.Networking;
using TankBattle.UI;
using TankBattle.Utils;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Generates the MainMenu scene and the five low-poly map scenes entirely
    /// from primitives, then registers them all in the Android build settings.
    /// </summary>
    public static class SceneBuilder
    {
        public const string SceneDir = "Assets/Scenes";

        /// <summary>Visual theme + obstacle layout for one map.</summary>
        class MapDef
        {
            public string SceneName, DisplayName;
            public Color Ground, Wall, Obstacle, Sky, Ambient;
            public System.Action<MapDef> BuildObstacles;
        }

        // ------------------------------------------------------------ main menu

        public static void BuildMainMenuScene(GameObject networkManagerPrefab)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Camera (menu is pure UI; solid dark background).
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.10f);

            // Persistent network stack (NGO keeps it alive across scene loads).
            PrefabUtility.InstantiatePrefab(networkManagerPrefab);

            // Replicated lobby player list (in-scene NetworkObject).
            var lobby = new GameObject("LobbyState");
            lobby.AddComponent<NetworkObject>();
            lobby.AddComponent<LobbyState>();

            // Runtime-built menu UI + persistent audio.
            new GameObject("MainMenuUI").AddComponent<MainMenuUI>();
            new GameObject("AudioManager").AddComponent<AudioManager>();

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{GameConstants.MainMenuScene}.unity");
        }

        // ----------------------------------------------------------------- maps

        public static void BuildAllMaps()
        {
            var defs = new List<MapDef>
            {
                new MapDef
                {
                    SceneName = "Map01_Arena", DisplayName = "Open Arena",
                    Ground = new Color(0.76f, 0.70f, 0.50f), Wall = new Color(0.45f, 0.36f, 0.26f),
                    Obstacle = new Color(0.55f, 0.45f, 0.30f), Sky = new Color(0.55f, 0.75f, 0.95f),
                    Ambient = new Color(0.55f, 0.55f, 0.55f),
                    BuildObstacles = d =>
                    {
                        foreach (var sx in new[] { -1f, 1f })
                            foreach (var sz in new[] { -1f, 1f })
                                Box(d, "Crate", new Vector3(8f * sx, 1f, 8f * sz), new Vector3(4f, 2f, 4f));
                        Box(d, "WallE", new Vector3(16f, 1.25f, 0f), new Vector3(2f, 2.5f, 10f));
                        Box(d, "WallW", new Vector3(-16f, 1.25f, 0f), new Vector3(2f, 2.5f, 10f));
                    }
                },
                new MapDef
                {
                    SceneName = "Map02_Crossfire", DisplayName = "Crossfire",
                    Ground = new Color(0.45f, 0.52f, 0.58f), Wall = new Color(0.25f, 0.30f, 0.36f),
                    Obstacle = new Color(0.32f, 0.40f, 0.50f), Sky = new Color(0.65f, 0.60f, 0.55f),
                    Ambient = new Color(0.50f, 0.50f, 0.55f),
                    BuildObstacles = d =>
                    {
                        // Plus-shaped cover with an open centre.
                        Box(d, "N", new Vector3(0f, 1.5f, 7.5f), new Vector3(2f, 3f, 9f));
                        Box(d, "S", new Vector3(0f, 1.5f, -7.5f), new Vector3(2f, 3f, 9f));
                        Box(d, "E", new Vector3(7.5f, 1.5f, 0f), new Vector3(9f, 3f, 2f));
                        Box(d, "W", new Vector3(-7.5f, 1.5f, 0f), new Vector3(9f, 3f, 2f));
                        foreach (var sx in new[] { -1f, 1f })
                            foreach (var sz in new[] { -1f, 1f })
                            {
                                Box(d, "CornerA", new Vector3(18f * sx, 1f, 16f * sz), new Vector3(6f, 2f, 2f));
                                Box(d, "CornerB", new Vector3(16f * sx, 1f, 18f * sz), new Vector3(2f, 2f, 6f));
                            }
                    }
                },
                new MapDef
                {
                    SceneName = "Map03_Maze", DisplayName = "The Maze",
                    Ground = new Color(0.40f, 0.55f, 0.35f), Wall = new Color(0.28f, 0.35f, 0.25f),
                    Obstacle = new Color(0.36f, 0.44f, 0.30f), Sky = new Color(0.60f, 0.80f, 0.70f),
                    Ambient = new Color(0.50f, 0.55f, 0.50f),
                    BuildObstacles = d =>
                    {
                        Box(d, "M1", new Vector3(-13f, 1.5f, 10f), new Vector3(16f, 3f, 1.5f));
                        Box(d, "M2", new Vector3(13f, 1.5f, 10f), new Vector3(16f, 3f, 1.5f));
                        Box(d, "M3", new Vector3(-13f, 1.5f, -10f), new Vector3(16f, 3f, 1.5f));
                        Box(d, "M4", new Vector3(13f, 1.5f, -10f), new Vector3(16f, 3f, 1.5f));
                        Box(d, "M5", new Vector3(0f, 1.5f, 0f), new Vector3(1.5f, 3f, 12f));
                        Box(d, "M6", new Vector3(-20f, 1.5f, 0f), new Vector3(1.5f, 3f, 10f));
                        Box(d, "M7", new Vector3(20f, 1.5f, 0f), new Vector3(1.5f, 3f, 10f));
                        Box(d, "M8", new Vector3(-8f, 1.5f, 0f), new Vector3(8f, 3f, 1.5f));
                        Box(d, "M9", new Vector3(8f, 1.5f, 0f), new Vector3(8f, 3f, 1.5f));
                    }
                },
                new MapDef
                {
                    SceneName = "Map04_Pillars", DisplayName = "Pillar Field",
                    Ground = new Color(0.35f, 0.33f, 0.40f), Wall = new Color(0.22f, 0.20f, 0.28f),
                    Obstacle = new Color(0.55f, 0.50f, 0.65f), Sky = new Color(0.30f, 0.25f, 0.45f),
                    Ambient = new Color(0.45f, 0.42f, 0.55f),
                    BuildObstacles = d =>
                    {
                        float[] grid = { -16f, -8f, 0f, 8f, 16f };
                        foreach (var x in grid)
                            foreach (var z in grid)
                            {
                                // Keep the spawn corners clear.
                                if (Mathf.Abs(x) > 12f && Mathf.Abs(z) > 12f) continue;
                                Cylinder(d, "Pillar", new Vector3(x, 2f, z), new Vector3(2.4f, 2f, 2.4f));
                            }
                    }
                },
                new MapDef
                {
                    SceneName = "Map05_Fortress", DisplayName = "Fortress",
                    Ground = new Color(0.72f, 0.55f, 0.42f), Wall = new Color(0.48f, 0.32f, 0.24f),
                    Obstacle = new Color(0.58f, 0.42f, 0.30f), Sky = new Color(0.95f, 0.70f, 0.45f),
                    Ambient = new Color(0.60f, 0.50f, 0.45f),
                    BuildObstacles = d =>
                    {
                        // Central fort: four walls, each with a doorway gap.
                        foreach (var s in new[] { -1f, 1f })
                        {
                            Box(d, "FortNS_A", new Vector3(-6.5f, 1.5f, 10f * s), new Vector3(7f, 3f, 1.5f));
                            Box(d, "FortNS_B", new Vector3(6.5f, 1.5f, 10f * s), new Vector3(7f, 3f, 1.5f));
                            Box(d, "FortEW_A", new Vector3(10f * s, 1.5f, -6.5f), new Vector3(1.5f, 3f, 7f));
                            Box(d, "FortEW_B", new Vector3(10f * s, 1.5f, 6.5f), new Vector3(1.5f, 3f, 7f));
                        }
                        Cylinder(d, "Tower", new Vector3(0f, 2.5f, 0f), new Vector3(4f, 2.5f, 4f));
                        foreach (var sx in new[] { -1f, 1f })
                            foreach (var sz in new[] { -1f, 1f })
                            {
                                var bunker = Box(d, "Bunker", new Vector3(19f * sx, 1f, 19f * sz),
                                                 new Vector3(7f, 2f, 2f));
                                bunker.transform.rotation = Quaternion.Euler(0f, 45f * sx * sz, 0f);
                            }
                    }
                }
            };

            for (int i = 0; i < defs.Count; i++)
                BuildMap(defs[i]);
        }

        static Material _ground, _wall, _obstacle; // per-map, set in BuildMap

        static void BuildMap(MapDef d)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Per-map materials.
            _ground = PrefabBuilder.CreateMaterial($"{d.SceneName}_Ground", d.Ground);
            _wall = PrefabBuilder.CreateMaterial($"{d.SceneName}_Wall", d.Wall);
            _obstacle = PrefabBuilder.CreateMaterial($"{d.SceneName}_Obstacle", d.Obstacle);

            // Camera with the chase behaviour (targets the local tank at spawn).
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener),
                                       typeof(CameraFollow));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; // cheaper than a skybox
            cam.backgroundColor = d.Sky;
            cam.farClipPlane = 150f;
            camGo.transform.position = new Vector3(0f, 30f, -35f);
            camGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            // Light + flat ambient (no baking required).
            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft; // runtime quality setting can disable
            lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = d.Ambient;
            RenderSettings.fog = false;

            // Ground (60 x 60) + perimeter walls.
            var geometry = new GameObject("Geometry");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(geometry.transform, false);
            ground.transform.localScale = new Vector3(6f, 1f, 6f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = _ground;
            ground.isStatic = true;

            _obstacleParent = geometry.transform;
            Box(d, "WallN", new Vector3(0f, 1.5f, 30.5f), new Vector3(62f, 3f, 1f), _wall);
            Box(d, "WallS", new Vector3(0f, 1.5f, -30.5f), new Vector3(62f, 3f, 1f), _wall);
            Box(d, "WallE", new Vector3(30.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), _wall);
            Box(d, "WallW", new Vector3(-30.5f, 1.5f, 0f), new Vector3(1f, 3f, 62f), _wall);

            // Map-specific obstacles.
            d.BuildObstacles?.Invoke(d);

            // Four spawn points facing the centre.
            for (int i = 0; i < 4; i++)
            {
                float sx = (i % 2 == 0) ? -1f : 1f;
                float sz = (i < 2) ? -1f : 1f;
                var sp = new GameObject($"Spawn_{i}", typeof(SpawnPoint));
                sp.transform.position = new Vector3(24f * sx, 0.1f, 24f * sz);
                sp.transform.rotation = Quaternion.LookRotation(new Vector3(-sx, 0f, -sz));
            }

            // Match logic (in-scene NetworkObject) + runtime-built HUD.
            var mm = new GameObject("MatchManager");
            mm.AddComponent<NetworkObject>();
            mm.AddComponent<MatchManager>();
            new GameObject("HUD").AddComponent<HUDController>();

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{d.SceneName}.unity");
        }

        // -------------------------------------------------------------- helpers

        static Transform _obstacleParent;

        static GameObject Box(MapDef d, string name, Vector3 pos, Vector3 scale, Material mat = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat != null ? mat : _obstacle;
            go.isStatic = true; // static batching for mobile perf
            return go;
        }

        static GameObject Cylinder(MapDef d, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = _obstacle;
            go.isStatic = true;
            return go;
        }

        // -------------------------------------------------------- build settings

        public static void RegisterScenesInBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene($"{SceneDir}/{GameConstants.MainMenuScene}.unity", true)
            };
            foreach (var map in GameConstants.MapScenes)
                list.Add(new EditorBuildSettingsScene($"{SceneDir}/{map}.unity", true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
