using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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
    /// v2: bigger 80x80 arenas for 16 players, procedural gradient skyboxes,
    /// distance fog, decorative scenery ring, 8 spawn points, 6 weapon-crate
    /// points and the King of the Hill zone in every map.
    /// </summary>
    public static class SceneBuilder
    {
        public const string SceneDir = "Assets/Scenes";

        enum MapTheme { Desert, Urban, Forest, Alien, Fort }

        /// <summary>Visual theme + obstacle layout for one map.</summary>
        class MapDef
        {
            public string SceneName, DisplayName;
            public MapTheme Theme;
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
                    SceneName = "Map01_Arena", DisplayName = "Open Arena", Theme = MapTheme.Desert,
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
                    SceneName = "Map02_Crossfire", DisplayName = "Crossfire", Theme = MapTheme.Urban,
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
                    SceneName = "Map03_Maze", DisplayName = "The Maze", Theme = MapTheme.Forest,
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
                    SceneName = "Map04_Pillars", DisplayName = "Pillar Field", Theme = MapTheme.Alien,
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
                    SceneName = "Map05_Fortress", DisplayName = "Fortress", Theme = MapTheme.Fort,
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
        const float ArenaHalf = 40f;   // 80 x 80 playfield for 16 players
        const float LayoutScale = 1.3f; // obstacle layouts were authored for 60x60

        static void BuildMap(MapDef d)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Per-map TEXTURED materials (procedural textures = huge visual jump).
            _ground = PrefabBuilder.CreateTexturedMaterial($"{d.SceneName}_Ground",
                d.Ground, GroundTexture(d.Theme), 9f);
            _wall = PrefabBuilder.CreateTexturedMaterial($"{d.SceneName}_Wall",
                d.Wall, WallTexture(d.Theme), 1f, WallNormal(d.Theme));
            _wall.mainTextureScale = new Vector2(18f, 1.2f);   // long perimeter walls
            _wall.SetTextureScale("_BumpMap", new Vector2(18f, 1.2f));
            _obstacle = PrefabBuilder.CreateTexturedMaterial($"{d.SceneName}_Obstacle",
                d.Obstacle, WallTexture(d.Theme), 1.6f, WallNormal(d.Theme));

            // Camera with the chase behaviour (targets the local tank at spawn).
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener),
                                       typeof(CameraFollow));
            camGo.tag = "MainCamera";
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.farClipPlane = 220f;
            camGo.transform.position = new Vector3(0f, 35f, -45f);
            camGo.transform.rotation = Quaternion.Euler(40f, 0f, 0f);

            // Procedural gradient skybox - far nicer than a flat color, and the
            // shader is included in the build because the material is an asset.
            var sky = CreateSkyboxMaterial(d);
            RenderSettings.skybox = sky;

            // Key light (warm sun) with strong soft shadows.
            var lightGo = new GameObject("Directional Light", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(1f, 0.96f, 0.88f); // warm sun
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
            light.shadowBias = 0.03f;
            light.shadowNormalBias = 0.4f;
            lightGo.transform.rotation = Quaternion.Euler(52f, -35f, 0f);

            // Cool rim/fill light from behind for depth and shape separation.
            var rimGo = new GameObject("Rim Light", typeof(Light));
            var rim = rimGo.GetComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = 0.45f;
            rim.color = new Color(0.55f, 0.65f, 0.9f); // cool sky bounce
            rim.shadows = LightShadows.None;
            rimGo.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(d.Sky, Color.white, 0.2f);
            RenderSettings.ambientEquatorColor = d.Ambient;
            RenderSettings.ambientGroundColor = Color.Lerp(d.Ground, Color.black, 0.4f);
            RenderSettings.reflectionIntensity = 1f;
            RenderSettings.sun = light;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Color.Lerp(d.Sky, Color.white, 0.15f);
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 160f;

            // Ground (80 x 80) + perimeter walls.
            var geometry = new GameObject("Geometry");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(geometry.transform, false);
            ground.transform.localScale = new Vector3(ArenaHalf / 5f, 1f, ArenaHalf / 5f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = _ground;
            ground.isStatic = true;

            _obstacleParent = geometry.transform;
            _layoutScale = 1f; // walls use absolute positions
            Box(d, "WallN", new Vector3(0f, 1.5f, ArenaHalf + 0.5f), new Vector3(ArenaHalf * 2f + 2f, 3f, 1f), _wall);
            Box(d, "WallS", new Vector3(0f, 1.5f, -ArenaHalf - 0.5f), new Vector3(ArenaHalf * 2f + 2f, 3f, 1f), _wall);
            Box(d, "WallE", new Vector3(ArenaHalf + 0.5f, 1.5f, 0f), new Vector3(1f, 3f, ArenaHalf * 2f + 2f), _wall);
            Box(d, "WallW", new Vector3(-ArenaHalf - 0.5f, 1.5f, 0f), new Vector3(1f, 3f, ArenaHalf * 2f + 2f), _wall);

            // Map-specific obstacles (scaled up into the bigger arena).
            _layoutScale = LayoutScale;
            d.BuildObstacles?.Invoke(d);
            _layoutScale = 1f;

            // Decorative scenery ring between the action and the walls,
            // plus themed props INSIDE the arena (trees, barrels, crystals...),
            // plus scattered grass, bushes and roofed hideouts.
            BuildScenery(d);
            BuildInteriorDecor(d);
            BuildFoliage(d);

            // Eight spawn points on a ring, all facing the centre.
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 33f;
                pos.y = 0.1f;
                var sp = new GameObject($"Spawn_{i}", typeof(SpawnPoint));
                sp.transform.position = pos;
                sp.transform.rotation = Quaternion.LookRotation(-pos.normalized);
            }

            // Six weapon-crate points (centre cross + two diagonals).
            Vector3[] pickupSpots =
            {
                new Vector3(14f, 0f, 0f), new Vector3(-14f, 0f, 0f),
                new Vector3(0f, 0f, 14f), new Vector3(0f, 0f, -14f),
                new Vector3(24f, 0f, 24f), new Vector3(-24f, 0f, -24f)
            };
            for (int i = 0; i < pickupSpots.Length; i++)
            {
                var pp = new GameObject($"Pickup_{i}", typeof(PickupPoint));
                pp.transform.position = pickupSpots[i];
            }

            // King of the Hill zone (auto-hidden in other modes).
            BuildKothZone(d);

            // Realtime reflection probe (rendered once at load) - makes metal
            // barrels/barrels and the shield bubble reflect the environment.
            var probeGo = new GameObject("ReflectionProbe");
            probeGo.transform.position = new Vector3(0f, 12f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
            probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
            probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.size = new Vector3(180f, 70f, 180f);
            probe.boxProjection = true;
            probe.cullingMask = ~0;

            // Floating dust motes for atmosphere.
            BuildAtmosphere(d);

            // Cinematic post-processing (bloom, colour grade, AO, vignette...).
            PostFXBuilder.ApplyToScene(cam);

            // Match logic (in-scene NetworkObject) + runtime-built HUD.
            var mm = new GameObject("MatchManager");
            mm.AddComponent<NetworkObject>();
            mm.AddComponent<MatchManager>();
            new GameObject("HUD").AddComponent<HUDController>();

            EditorSceneManager.SaveScene(scene, $"{SceneDir}/{d.SceneName}.unity");
        }

        // ------------------------------------------------------------- skies etc

        static Material CreateSkyboxMaterial(MapDef d)
        {
            string path = $"{PrefabBuilder.MaterialDir}/{d.SceneName}_Sky.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                existing = new Material(Shader.Find("Skybox/Procedural"));
                AssetDatabase.CreateAsset(existing, path);
            }
            existing.SetColor("_SkyTint", d.Sky);
            existing.SetColor("_GroundColor", Color.Lerp(d.Ground, Color.black, 0.35f));
            existing.SetFloat("_Exposure", 1.2f);
            existing.SetFloat("_AtmosphereThickness", 0.9f);
            existing.SetFloat("_SunSize", 0.05f);
            return existing;
        }

        static void BuildKothZone(MapDef d)
        {
            // Transparent glowing disc at the centre of the map.
            string path = $"{PrefabBuilder.MaterialDir}/KothZone.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = new Color(1f, 0.85f, 0.2f, 0.35f);

            var zone = new GameObject("KothZone", typeof(KothZone));
            zone.transform.position = new Vector3(0f, 0.03f, 0f);

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "ZoneDisc";
            disc.transform.SetParent(zone.transform, false);
            disc.transform.localScale = new Vector3(GameConstants.KothZoneRadius * 2f, 0.02f,
                                                    GameConstants.KothZoneRadius * 2f);
            Object.DestroyImmediate(disc.GetComponent<Collider>()); // never blocks anything
            var mr = disc.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>Rocks + corner towers around the edge - pure decoration with collision.</summary>
        static void BuildScenery(MapDef d)
        {
            var rockMat = PrefabBuilder.CreateTexturedMaterial($"{d.SceneName}_Rock",
                Color.Lerp(d.Obstacle, Color.black, 0.25f), TextureBuilder.StoneTile, 2f,
                TextureBuilder.StoneTileN);

            // Ring of rocks (deterministic pseudo-random sizes/offsets).
            for (int i = 0; i < 12; i++)
            {
                float ang = (i * 30f + 11f) * Mathf.Deg2Rad;
                float radius = 37.2f + ((i * 7) % 3) * 0.8f;
                Vector3 pos = new Vector3(Mathf.Sin(ang) * radius, 0f, Mathf.Cos(ang) * radius);
                float s = 1.6f + ((i * 13) % 5) * 0.5f;

                var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                rock.name = $"Rock_{i}";
                if (_obstacleParent != null) rock.transform.SetParent(_obstacleParent, false);
                rock.transform.position = new Vector3(pos.x, s * 0.35f, pos.z);
                rock.transform.localScale = new Vector3(s, s * 0.7f, s);
                rock.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);
                rock.GetComponent<MeshRenderer>().sharedMaterial = rockMat;
                rock.isStatic = true;
            }

            // Four corner watchtowers.
            foreach (var sx in new[] { -1f, 1f })
                foreach (var sz in new[] { -1f, 1f })
                {
                    var baseGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    baseGo.name = "TowerBase";
                    if (_obstacleParent != null) baseGo.transform.SetParent(_obstacleParent, false);
                    baseGo.transform.position = new Vector3(37f * sx, 3f, 37f * sz);
                    baseGo.transform.localScale = new Vector3(3f, 3f, 3f);
                    baseGo.GetComponent<MeshRenderer>().sharedMaterial = _wall;
                    baseGo.isStatic = true;

                    var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    top.name = "TowerTop";
                    if (_obstacleParent != null) top.transform.SetParent(_obstacleParent, false);
                    top.transform.position = new Vector3(37f * sx, 6.4f, 37f * sz);
                    top.transform.localScale = new Vector3(4.2f, 0.9f, 4.2f);
                    top.GetComponent<MeshRenderer>().sharedMaterial = _obstacle;
                    top.isStatic = true;
                    Object.DestroyImmediate(top.GetComponent<Collider>()); // out of reach anyway
                }
        }

        // ----------------------------------------------------- themed textures

        static Texture2D GroundTexture(MapTheme t) => t switch
        {
            MapTheme.Desert => TextureBuilder.Sand,
            MapTheme.Urban => TextureBuilder.Concrete,
            MapTheme.Forest => TextureBuilder.Grass,
            MapTheme.Alien => TextureBuilder.StoneTile,
            _ => TextureBuilder.Sand
        };

        static Texture2D WallTexture(MapTheme t) => t switch
        {
            MapTheme.Urban => TextureBuilder.Concrete,
            MapTheme.Alien => TextureBuilder.StoneTile,
            MapTheme.Forest => TextureBuilder.StoneTile,
            _ => TextureBuilder.Brick // desert + fort = brickwork
        };

        static Texture2D WallNormal(MapTheme t) => t switch
        {
            MapTheme.Urban => null,
            MapTheme.Alien => TextureBuilder.StoneTileN,
            MapTheme.Forest => TextureBuilder.StoneTileN,
            _ => TextureBuilder.BrickN
        };

        // ------------------------------------------------------ interior decor

        /// <summary>Eight themed prop spots inside the arena (clear of the zone,
        /// crate points and spawn ring) - cover + atmosphere in one.</summary>
        static void BuildInteriorDecor(MapDef d)
        {
            Vector3[] spots =
            {
                new Vector3(20f, 0f, 8f),  new Vector3(-20f, 0f, 8f),
                new Vector3(20f, 0f, -8f), new Vector3(-20f, 0f, -8f),
                new Vector3(8f, 0f, 20f),  new Vector3(-8f, 0f, 20f),
                new Vector3(8f, 0f, -20f), new Vector3(-8f, 0f, -20f)
            };

            var barrelMat = PrefabBuilder.CreateTexturedMaterial("Prop_Barrel",
                new Color(0.75f, 0.35f, 0.2f), TextureBuilder.MetalPlate, 1f, null, 0.4f, 0.5f);
            var trunkMat = PrefabBuilder.CreateTexturedMaterial("Prop_Trunk",
                new Color(0.55f, 0.4f, 0.28f), TextureBuilder.Planks, 1f);
            var leafMat = PrefabBuilder.CreateTexturedMaterial("Prop_Leaf",
                new Color(0.5f, 0.8f, 0.45f), TextureBuilder.Leaf, 2f);
            var barrierMat = PrefabBuilder.CreateTexturedMaterial("Prop_Barrier",
                new Color(0.85f, 0.85f, 0.88f), TextureBuilder.Concrete, 1f);

            for (int i = 0; i < spots.Length; i++)
            {
                Vector3 p = spots[i];
                switch (d.Theme)
                {
                    case MapTheme.Forest:
                        Tree(p, trunkMat, leafMat, 1f + (i % 3) * 0.25f);
                        break;
                    case MapTheme.Alien:
                        Crystal(p, i);
                        break;
                    case MapTheme.Urban:
                        if (i % 2 == 0) Barrier(p, barrierMat, i * 45f);
                        else Barrel(p, barrelMat);
                        break;
                    default: // Desert + Fort
                        if (i % 2 == 0) Barrel(p, barrelMat);
                        else Tree(p, trunkMat, leafMat, 0.8f); // sparse dry trees
                        break;
                }
            }
        }

        static void Barrel(Vector3 p, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Barrel";
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = new Vector3(p.x, 0.65f, p.z);
            go.transform.localScale = new Vector3(0.9f, 0.65f, 0.9f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        static void Barrier(Vector3 p, Material mat, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Barrier";
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = new Vector3(p.x, 0.55f, p.z);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(3.2f, 1.1f, 0.8f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        static void Tree(Vector3 p, Material trunk, Material leaf, float scale)
        {
            var root = new GameObject("Tree");
            if (_obstacleParent != null) root.transform.SetParent(_obstacleParent, false);
            root.transform.position = p;

            var t = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            t.name = "Trunk";
            t.transform.SetParent(root.transform, false);
            t.transform.localPosition = new Vector3(0f, 1.4f * scale, 0f);
            t.transform.localScale = new Vector3(0.45f * scale, 1.4f * scale, 0.45f * scale);
            t.GetComponent<MeshRenderer>().sharedMaterial = trunk;
            t.isStatic = true;

            // Two overlapping foliage spheres = fuller canopy.
            foreach (var (off, s) in new[]
            {
                (new Vector3(0f, 3.4f, 0f), 2.6f),
                (new Vector3(0.7f, 2.7f, 0.4f), 1.8f)
            })
            {
                var f = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                f.name = "Leaves";
                f.transform.SetParent(root.transform, false);
                f.transform.localPosition = off * scale;
                f.transform.localScale = Vector3.one * s * scale;
                Object.DestroyImmediate(f.GetComponent<Collider>()); // drive under canopy
                f.GetComponent<MeshRenderer>().sharedMaterial = leaf;
                f.isStatic = true;
            }
        }

        /// <summary>
        /// Scatter walk-through grass tufts + hide-in bushes across the arena,
        /// and drop a few roofed "hideout" nooks you can duck into. All greenery
        /// has no collision (bushes/grass) so tanks can drive through and hide;
        /// the hideout walls DO collide for real cover.
        /// </summary>
        static void BuildFoliage(MapDef d)
        {
            // Deterministic per-map so every device builds the identical arena.
            var prev = Random.state;
            Random.InitState(d.SceneName.GetHashCode());

            var grassMat = PrefabBuilder.CreateTexturedMaterial("Foliage_Grass",
                new Color(0.5f, 0.95f, 0.45f), TextureBuilder.Leaf, 1f);
            var bushMat = PrefabBuilder.CreateTexturedMaterial("Foliage_Bush",
                new Color(0.35f, 0.75f, 0.35f), TextureBuilder.Leaf, 2f);

            // Greener maps get denser grass; deserts get sparse tufts.
            int grassCount = d.Theme == MapTheme.Forest ? 130
                           : d.Theme == MapTheme.Fort || d.Theme == MapTheme.Urban ? 55 : 70;
            for (int i = 0; i < grassCount; i++)
            {
                Vector3 p = new Vector3(Random.Range(-35f, 35f), 0f, Random.Range(-35f, 35f));
                if (p.magnitude < 6f) continue; // keep the very centre clear
                GrassTuft(p, grassMat, Random.Range(0.7f, 1.5f));
            }

            // Bushes big enough to hide a tank inside (no collider = drive in).
            int bushCount = d.Theme == MapTheme.Forest ? 20 : 12;
            for (int i = 0; i < bushCount; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float rad = Random.Range(10f, 30f);
                Vector3 p = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                Bush(p, bushMat, Random.Range(2.2f, 3.4f));
            }

            // Three roofed hideouts (real cover you can shelter under).
            for (int i = 0; i < 3; i++)
            {
                float ang = (i * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Cos(ang) * 22f, 0f, Mathf.Sin(ang) * 22f);
                Hideout(p, i * 120f + 30f);
            }

            Random.state = prev;
        }

        static void GrassTuft(Vector3 p, Material mat, float scale)
        {
            var root = new GameObject("Grass");
            if (_obstacleParent != null) root.transform.SetParent(_obstacleParent, false);
            root.transform.position = p;
            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            // Two crossed quads = a cheap 3D-looking tuft.
            for (int q = 0; q < 2; q++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Blade";
                quad.transform.SetParent(root.transform, false);
                quad.transform.localPosition = new Vector3(0f, 0.5f * scale, 0f);
                quad.transform.localRotation = Quaternion.Euler(0f, q * 90f, 0f);
                quad.transform.localScale = new Vector3(1.4f * scale, 1.0f * scale, 1f);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                quad.isStatic = true;
            }
        }

        static void Bush(Vector3 p, Material mat, float scale)
        {
            var root = new GameObject("Bush");
            if (_obstacleParent != null) root.transform.SetParent(_obstacleParent, false);
            root.transform.position = p;

            // A clump of 3 overlapping spheres, no collider (hide inside).
            foreach (var off in new[]
            {
                new Vector3(0f, scale * 0.45f, 0f),
                new Vector3(scale * 0.35f, scale * 0.35f, scale * 0.2f),
                new Vector3(-scale * 0.3f, scale * 0.3f, -scale * 0.25f)
            })
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = "Leaves";
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = off;
                s.transform.localScale = Vector3.one * scale * Random.Range(0.8f, 1.1f);
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.GetComponent<MeshRenderer>().sharedMaterial = mat;
                s.isStatic = true;
            }
        }

        static void Hideout(Vector3 p, float yaw)
        {
            var root = new GameObject("Hideout");
            if (_obstacleParent != null) root.transform.SetParent(_obstacleParent, false);
            root.transform.position = p;
            root.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            // Three walls + a roof; the open side faces the centre.
            void Wall(Vector3 lp, Vector3 ls)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = "HideWall";
                w.transform.SetParent(root.transform, false);
                w.transform.localPosition = lp;
                w.transform.localScale = ls;
                w.GetComponent<MeshRenderer>().sharedMaterial = _wall;
                w.isStatic = true;
            }
            Wall(new Vector3(0f, 1.4f, -2.5f), new Vector3(5.5f, 2.8f, 0.5f)); // back
            Wall(new Vector3(-2.5f, 1.4f, 0f), new Vector3(0.5f, 2.8f, 5.5f)); // left
            Wall(new Vector3(2.5f, 1.4f, 0f), new Vector3(0.5f, 2.8f, 5.5f));  // right

            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "HideRoof";
            roof.transform.SetParent(root.transform, false);
            roof.transform.localPosition = new Vector3(0f, 2.9f, 0f);
            roof.transform.localScale = new Vector3(5.7f, 0.4f, 5.7f);
            roof.GetComponent<MeshRenderer>().sharedMaterial = _obstacle;
            roof.isStatic = true;
        }

        /// <summary>Slow floating dust motes drifting through the arena air.</summary>
        static void BuildAtmosphere(MapDef d)
        {
            string path = $"{PrefabBuilder.MaterialDir}/FX_Motes.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
                AssetDatabase.CreateAsset(mat, path);
            }

            var go = new GameObject("Atmosphere", typeof(ParticleSystem));
            go.transform.position = new Vector3(0f, 10f, 0f);
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 9f;
            main.startSpeed = 0.25f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
            main.startColor = new Color(1f, 0.98f, 0.9f, 0.5f);
            main.maxParticles = 240;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 26f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(78f, 22f, 78f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(0.15f);
            vel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.3f),
                        new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        static void Crystal(Vector3 p, int i)
        {
            // Glowing alien shards (emissive material = they light up at dusk).
            var mat = PrefabBuilder.CreateMaterial("Prop_Crystal",
                new Color(0.6f, 0.4f, 1f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.45f, 0.25f, 0.9f) * 1.4f);

            var root = new GameObject("Crystal");
            if (_obstacleParent != null) root.transform.SetParent(_obstacleParent, false);
            root.transform.position = p;

            foreach (var (yaw, tilt, h) in new[]
            {
                (i * 40f, 12f, 2.6f), (i * 40f + 140f, -18f, 1.7f)
            })
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                c.name = "Shard";
                c.transform.SetParent(root.transform, false);
                c.transform.localPosition = new Vector3(0f, h * 0.4f, 0f);
                c.transform.localRotation = Quaternion.Euler(tilt, yaw, 45f);
                c.transform.localScale = new Vector3(0.6f, h, 0.6f);
                c.GetComponent<MeshRenderer>().sharedMaterial = mat;
                c.isStatic = true;
            }
        }

        // -------------------------------------------------------------- helpers

        static Transform _obstacleParent;
        static float _layoutScale = 1f;

        static GameObject Box(MapDef d, string name, Vector3 pos, Vector3 scale, Material mat = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = new Vector3(pos.x * _layoutScale, pos.y, pos.z * _layoutScale);
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
            go.transform.position = new Vector3(pos.x * _layoutScale, pos.y, pos.z * _layoutScale);
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
