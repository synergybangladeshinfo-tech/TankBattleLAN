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
            public GameConstants.Weather Weather;
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
                    SceneName = "Map01_Arena", DisplayName = "Desert Ruins", Theme = MapTheme.Desert,
                    Weather = GameConstants.Weather.DustStorm,
                    Ground = new Color(0.76f, 0.70f, 0.50f), Wall = new Color(0.45f, 0.36f, 0.26f),
                    Obstacle = new Color(0.55f, 0.45f, 0.30f), Sky = new Color(0.55f, 0.75f, 0.95f),
                    Ambient = new Color(0.55f, 0.55f, 0.55f),
                    BuildObstacles = d =>
                    {
                        // OPEN DESERT: long sightlines broken by rolling dunes,
                        // a ruined outpost and a wrecked convoy. Almost no hard
                        // cover in the middle - this is the sniper's map.
                        for (int i = 0; i < 12; i++)
                        {
                            float a = i * 30f * Mathf.Deg2Rad;
                            float r = 22f + (i % 3) * 13f;
                            var dune = Sphere(d, "Dune",
                                new Vector3(Mathf.Cos(a) * r, -1.2f, Mathf.Sin(a) * r),
                                new Vector3(18f + (i % 4) * 6f, 5.5f, 14f + (i % 3) * 6f));
                            dune.transform.rotation = Quaternion.Euler(0f, i * 37f, 0f);
                        }

                        // Ruined outpost in the west - the only real hard cover.
                        var r1 = Box(d, "Ruin", new Vector3(-32f, 2.2f, 14f), new Vector3(22f, 4.4f, 1.6f));
                        r1.transform.rotation = Quaternion.Euler(0f, 18f, 0f);
                        var r2 = Box(d, "Ruin", new Vector3(-18f, 1.6f, -6f), new Vector3(16f, 3.2f, 1.6f));
                        r2.transform.rotation = Quaternion.Euler(0f, -52f, 0f);
                        var r3 = Box(d, "Ruin", new Vector3(-42f, 1.3f, -18f), new Vector3(14f, 2.6f, 1.6f));
                        r3.transform.rotation = Quaternion.Euler(0f, 74f, 0f);
                        Cylinder(d, "RuinColumn", new Vector3(-27f, 3.0f, -2f), new Vector3(2.0f, 3.0f, 2.0f));
                        Cylinder(d, "RuinColumn", new Vector3(-36f, 2.4f, 4f), new Vector3(2.0f, 2.4f, 2.0f));

                        // Wrecked convoy strung along a dry track to the east.
                        for (int i = 0; i < 6; i++)
                        {
                            var hulk = Box(d, "Wreck",
                                new Vector3(16f + i * 8f, 1.3f, 26f - i * 10f),
                                new Vector3(6.5f, 2.6f, 3.2f));
                            hulk.transform.rotation = Quaternion.Euler(0f, 20f + i * 34f, i % 2 == 0 ? 0f : 12f);
                        }

                        // Two lone rock spires you can circle for cover.
                        Cylinder(d, "Spire", new Vector3(10f, 5.5f, -26f), new Vector3(4.5f, 5.5f, 4.5f));
                        Cylinder(d, "Spire", new Vector3(-8f, 4.2f, 40f), new Vector3(3.6f, 4.2f, 3.6f));
                    }
                },
                new MapDef
                {
                    SceneName = "Map02_Crossfire", DisplayName = "City Block", Theme = MapTheme.Urban,
                    Weather = GameConstants.Weather.Night,
                    Ground = new Color(0.45f, 0.52f, 0.58f), Wall = new Color(0.25f, 0.30f, 0.36f),
                    Obstacle = new Color(0.32f, 0.40f, 0.50f), Sky = new Color(0.65f, 0.60f, 0.55f),
                    Ambient = new Color(0.50f, 0.50f, 0.55f),
                    BuildObstacles = d =>
                    {
                        // CITY BLOCK: a real street grid. Tall blocks form
                        // avenues and back alleys, so fighting is corner to
                        // corner instead of across an open field.
                        float[] cx = { -34f, -12f, 12f, 34f };
                        float[] cz = { -34f, -12f, 12f, 34f };
                        for (int i = 0; i < cx.Length; i++)
                            for (int j = 0; j < cz.Length; j++)
                            {
                                // Leave the very centre as a plaza.
                                if (Mathf.Abs(cx[i]) < 20f && Mathf.Abs(cz[j]) < 20f) continue;

                                float h = 4f + ((i * 3 + j * 5) % 4) * 2.2f;
                                Box(d, "Building", new Vector3(cx[i], h * 0.5f, cz[j]),
                                    new Vector3(13f, h, 13f));
                                // Doorway-height ledge so buildings read as buildings.
                                Box(d, "Ledge", new Vector3(cx[i], h + 0.35f, cz[j]),
                                    new Vector3(14.5f, 0.7f, 14.5f));
                            }

                        // Central plaza: a fountain ring plus low benches.
                        Cylinder(d, "Fountain", new Vector3(0f, 0.7f, 0f), new Vector3(5f, 0.7f, 5f));
                        Cylinder(d, "FountainTop", new Vector3(0f, 1.6f, 0f), new Vector3(1.6f, 1.6f, 1.6f));
                        foreach (var s in new[] { -1f, 1f })
                        {
                            Box(d, "Bench", new Vector3(9f * s, 0.5f, 0f), new Vector3(1.2f, 1f, 6f));
                            Box(d, "Bench", new Vector3(0f, 0.5f, 9f * s), new Vector3(6f, 1f, 1.2f));
                        }

                        // Sandbag barricades across the avenues.
                        foreach (var s in new[] { -1f, 1f })
                        {
                            Box(d, "Barricade", new Vector3(23f * s, 0.8f, 0f), new Vector3(1.6f, 1.6f, 9f));
                            Box(d, "Barricade", new Vector3(0f, 0.8f, 23f * s), new Vector3(9f, 1.6f, 1.6f));
                        }

                        // Overpass slab you can drive under, hover over.
                        Box(d, "Overpass", new Vector3(0f, 5.2f, -46f), new Vector3(30f, 0.8f, 6f));
                        Box(d, "OverpassLegL", new Vector3(-13f, 2.6f, -46f), new Vector3(1.6f, 5.2f, 5f));
                        Box(d, "OverpassLegR", new Vector3(13f, 2.6f, -46f), new Vector3(1.6f, 5.2f, 5f));
                    }
                },
                new MapDef
                {
                    SceneName = "Map03_Maze", DisplayName = "Deep Forest", Theme = MapTheme.Forest,
                    Weather = GameConstants.Weather.Rain,
                    Ground = new Color(0.40f, 0.55f, 0.35f), Wall = new Color(0.28f, 0.35f, 0.25f),
                    Obstacle = new Color(0.36f, 0.44f, 0.30f), Sky = new Color(0.60f, 0.80f, 0.70f),
                    Ambient = new Color(0.50f, 0.55f, 0.50f),
                    BuildObstacles = d =>
                    {
                        // DEEP FOREST: a rocky ridge splits the map in two, with
                        // only three ways through. Winding log walls make the
                        // flanks a genuine maze rather than a grid.
                        for (int i = 0; i < 11; i++)
                        {
                            float x = -55f + i * 11f;
                            // Gaps at three points so the ridge is passable.
                            if (i == 2 || i == 5 || i == 8) continue;
                            var rock = Sphere(d, "Ridge", new Vector3(x, 0.4f, 0f),
                                new Vector3(11f, 6.5f, 8f));
                            rock.transform.rotation = Quaternion.Euler(0f, i * 29f, 0f);
                        }

                        // Winding log walls in the north half.
                        var w1 = Box(d, "Logs", new Vector3(-26f, 1.4f, 20f), new Vector3(24f, 2.8f, 1.6f));
                        w1.transform.rotation = Quaternion.Euler(0f, 14f, 0f);
                        var w2 = Box(d, "Logs", new Vector3(-8f, 1.4f, 32f), new Vector3(1.6f, 2.8f, 18f));
                        w2.transform.rotation = Quaternion.Euler(0f, -22f, 0f);
                        var w3 = Box(d, "Logs", new Vector3(16f, 1.4f, 24f), new Vector3(26f, 2.8f, 1.6f));
                        w3.transform.rotation = Quaternion.Euler(0f, -9f, 0f);
                        var w4 = Box(d, "Logs", new Vector3(34f, 1.4f, 38f), new Vector3(1.6f, 2.8f, 22f));
                        w4.transform.rotation = Quaternion.Euler(0f, 16f, 0f);

                        // and the south half, mirrored but not identical.
                        var w5 = Box(d, "Logs", new Vector3(24f, 1.4f, -20f), new Vector3(26f, 2.8f, 1.6f));
                        w5.transform.rotation = Quaternion.Euler(0f, -13f, 0f);
                        var w6 = Box(d, "Logs", new Vector3(6f, 1.4f, -34f), new Vector3(1.6f, 2.8f, 20f));
                        w6.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
                        var w7 = Box(d, "Logs", new Vector3(-20f, 1.4f, -26f), new Vector3(22f, 2.8f, 1.6f));
                        w7.transform.rotation = Quaternion.Euler(0f, 11f, 0f);
                        var w8 = Box(d, "Logs", new Vector3(-38f, 1.4f, -40f), new Vector3(1.6f, 2.8f, 20f));

                        // Fallen trunks you can shelter behind.
                        for (int i = 0; i < 7; i++)
                        {
                            float a = i * 51f * Mathf.Deg2Rad;
                            var trunk = Cylinder(d, "FallenTrunk",
                                new Vector3(Mathf.Cos(a) * 42f, 0.8f, Mathf.Sin(a) * 42f),
                                new Vector3(1.5f, 5f, 1.5f));
                            trunk.transform.rotation = Quaternion.Euler(90f, i * 43f, 0f);
                        }
                    }
                },
                new MapDef
                {
                    SceneName = "Map04_Pillars", DisplayName = "Space Deck", Theme = MapTheme.Alien,
                    Weather = GameConstants.Weather.Clear,
                    Ground = new Color(0.35f, 0.33f, 0.40f), Wall = new Color(0.22f, 0.20f, 0.28f),
                    Obstacle = new Color(0.55f, 0.50f, 0.65f), Sky = new Color(0.30f, 0.25f, 0.45f),
                    Ambient = new Color(0.45f, 0.42f, 0.55f),
                    BuildObstacles = d =>
                    {
                        // SPACE PLATFORM: floating decks at different heights with
                        // gaps between them. The only map where HOVER genuinely
                        // pays off, because the high ground is not walkable.
                        var decks = new[]
                        {
                            new Vector4(  0f, 0.0f,   0f, 32f),  // x, top height, z, diameter
                            new Vector4( 36f, 3.0f,  24f, 22f),
                            new Vector4(-36f, 3.0f, -24f, 22f),
                            new Vector4( 32f, 5.5f, -32f, 18f),
                            new Vector4(-32f, 5.5f,  32f, 18f),
                            new Vector4(  0f, 7.5f,  50f, 16f),
                            new Vector4(  0f, 7.5f, -50f, 16f)
                        };
                        foreach (var deck in decks)
                        {
                            if (deck.y <= 0.01f) continue;  // centre deck is the ground itself
                            float half = (deck.y + 0.6f) * 0.5f;
                            Cylinder(d, "Deck", new Vector3(deck.x, deck.y - half, deck.z),
                                new Vector3(deck.w, half, deck.w));
                            // Guard-rail posts so the edge reads clearly.
                            for (int i = 0; i < 8; i++)
                            {
                                float a = i * 45f * Mathf.Deg2Rad;
                                Cylinder(d, "Rail",
                                    new Vector3(deck.x + Mathf.Cos(a) * deck.w * 0.43f,
                                                deck.y + 0.7f,
                                                deck.z + Mathf.Sin(a) * deck.w * 0.43f),
                                    new Vector3(0.5f, 0.7f, 0.5f));
                            }
                        }

                        // Ramps up to the two mid decks - everything higher needs hover.
                        var b1 = Box(d, "Ramp", new Vector3(24f, 1.5f, 16f), new Vector3(7f, 0.6f, 15f));
                        b1.transform.rotation = Quaternion.Euler(-12f, 34f, 0f);
                        var b2 = Box(d, "Ramp", new Vector3(-24f, 1.5f, -16f), new Vector3(7f, 0.6f, 15f));
                        b2.transform.rotation = Quaternion.Euler(-12f, 214f, 0f);

                        // Glowing monoliths clustered in the middle for cover.
                        for (int i = 0; i < 7; i++)
                        {
                            float a = i * 51.4f * Mathf.Deg2Rad;
                            var m = Box(d, "Monolith",
                                new Vector3(Mathf.Cos(a) * 12f, 3.5f, Mathf.Sin(a) * 12f),
                                new Vector3(3.0f, 7f, 3.0f));
                            m.transform.rotation = Quaternion.Euler(0f, i * 36f, 0f);
                        }

                        // Outer ring of thin spires - visual scale, light cover.
                        for (int i = 0; i < 10; i++)
                        {
                            float a = (i * 36f + 18f) * Mathf.Deg2Rad;
                            Cylinder(d, "Spire",
                                new Vector3(Mathf.Cos(a) * 58f, 6f, Mathf.Sin(a) * 58f),
                                new Vector3(2.2f, 6f, 2.2f));
                        }
                    }
                },
                new MapDef
                {
                    SceneName = "Map05_Fortress", DisplayName = "Fortress", Theme = MapTheme.Fort,
                    Weather = GameConstants.Weather.Night,
                    Ground = new Color(0.72f, 0.55f, 0.42f), Wall = new Color(0.48f, 0.32f, 0.24f),
                    Obstacle = new Color(0.58f, 0.42f, 0.30f), Sky = new Color(0.95f, 0.70f, 0.45f),
                    Ambient = new Color(0.60f, 0.50f, 0.45f),
                    BuildObstacles = d =>
                    {
                        // FORTRESS: an outer curtain wall with four gateways,
                        // corner towers, and a raised keep in the middle. Whoever
                        // holds the keep holds the map - so everyone fights for it.
                        const float R = 44f;      // curtain wall distance from centre
                        const float gate = 9f;    // half-width of each gateway
                        float seg = (R - gate) * 0.5f;          // length of one wall half
                        float off = gate + seg * 0.5f;          // its centre offset

                        foreach (var s in new[] { -1f, 1f })
                        {
                            // North / south curtain, split around a central gate.
                            Box(d, "Curtain", new Vector3(-off, 3f, R * s), new Vector3(seg, 6f, 2.6f));
                            Box(d, "Curtain", new Vector3(off, 3f, R * s), new Vector3(seg, 6f, 2.6f));
                            // East / west curtain.
                            Box(d, "Curtain", new Vector3(R * s, 3f, -off), new Vector3(2.6f, 6f, seg));
                            Box(d, "Curtain", new Vector3(R * s, 3f, off), new Vector3(2.6f, 6f, seg));
                        }

                        // Corner towers.
                        foreach (var sx in new[] { -1f, 1f })
                            foreach (var sz in new[] { -1f, 1f })
                            {
                                Cylinder(d, "Tower", new Vector3(R * sx, 4.5f, R * sz),
                                    new Vector3(9f, 4.5f, 9f));
                                Cylinder(d, "TowerCap", new Vector3(R * sx, 9.4f, R * sz),
                                    new Vector3(10.5f, 0.4f, 10.5f));
                            }

                        // Inner keep - a raised square you must climb via ramps.
                        Box(d, "Keep", new Vector3(0f, 2.2f, 0f), new Vector3(26f, 4.4f, 26f));
                        var rampN = Box(d, "Ramp", new Vector3(0f, 2.2f, 21f), new Vector3(10f, 0.7f, 16f));
                        rampN.transform.rotation = Quaternion.Euler(-16f, 0f, 0f);
                        var rampS = Box(d, "Ramp", new Vector3(0f, 2.2f, -21f), new Vector3(10f, 0.7f, 16f));
                        rampS.transform.rotation = Quaternion.Euler(16f, 0f, 0f);

                        // Battlements around the keep roof for cover up top.
                        for (int i = 0; i < 12; i++)
                        {
                            float t = i / 12f * Mathf.PI * 2f;
                            Box(d, "Merlon",
                                new Vector3(Mathf.Cos(t) * 11.5f, 5.4f, Mathf.Sin(t) * 11.5f),
                                new Vector3(2.4f, 2f, 2.4f));
                        }

                        // Courtyard buildings between the wall and the keep.
                        for (int i = 0; i < 8; i++)
                        {
                            float t = (i + 0.5f) / 8f * Mathf.PI * 2f;
                            var st = Box(d, "Stable",
                                new Vector3(Mathf.Cos(t) * 32f, 1.8f, Mathf.Sin(t) * 32f),
                                new Vector3(9f, 3.6f, 5.5f));
                            st.transform.rotation = Quaternion.Euler(0f, -t * Mathf.Rad2Deg, 0f);
                        }
                    }
                }
            };

            for (int i = 0; i < defs.Count; i++)
                BuildMap(defs[i]);
        }

        static Material _ground, _wall, _obstacle; // per-map, set in BuildMap
        // v2.7: the arena is now 140 x 140 - about THREE TIMES the old playable
        // area, so 16 tanks have somewhere to go and the cover actually matters.
        const float ArenaHalf = 70f;    // 140 x 140 playfield

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
            cam.farClipPlane = 400f;
            camGo.transform.position = new Vector3(0f, 55f, -78f);
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
            RenderSettings.fogStartDistance = 95f;
            RenderSettings.fogEndDistance = 290f;

            // Ground (140 x 140) + perimeter walls.
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

            // Map-specific obstacles. v3.1: every map is authored directly in
            // full 140 x 140 arena coordinates, so no rescaling is applied - the
            // five layouts are genuinely different shapes, not the same layout
            // stretched by different amounts.
            _layoutScale = 1f;
            d.BuildObstacles?.Invoke(d);

            // Decorative scenery ring between the action and the walls,
            // plus themed props INSIDE the arena (trees, barrels, crystals...),
            // plus scattered grass, bushes and roofed hideouts.
            BuildScenery(d);
            BuildInteriorDecor(d);
            BuildFoliage(d);
            BuildPlatforms(d);   // raised platforms + ramps (Mini-Militia vertical feel)

            // Spawn points on two rings (14 total) so 16 players never pile up
            // on top of each other in the much larger arena.
            int spawnIndex = 0;
            for (int i = 0; i < 8; i++)
            {
                float ang = i * 45f * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 60f;
                pos.y = 0.1f;
                var sp = new GameObject($"Spawn_{spawnIndex++:00}", typeof(SpawnPoint));
                sp.transform.position = pos;
                sp.transform.rotation = Quaternion.LookRotation(-pos.normalized);
            }
            for (int i = 0; i < 6; i++)
            {
                float ang = (i * 60f + 30f) * Mathf.Deg2Rad;
                Vector3 pos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * 34f;
                pos.y = 0.1f;
                var sp = new GameObject($"Spawn_{spawnIndex++:00}", typeof(SpawnPoint));
                sp.transform.position = pos;
                sp.transform.rotation = Quaternion.LookRotation(-pos.normalized);
            }

            // Fourteen weapon-crate points spread over the whole arena - with 3x
            // the ground to cover, six crates left most of the map empty.
            Vector3[] pickupSpots =
            {
                new Vector3( 20f, 0f,   0f), new Vector3(-20f, 0f,   0f),
                new Vector3(  0f, 0f,  20f), new Vector3(  0f, 0f, -20f),
                new Vector3( 38f, 0f,  38f), new Vector3(-38f, 0f, -38f),
                new Vector3( 38f, 0f, -38f), new Vector3(-38f, 0f,  38f),
                new Vector3( 56f, 0f,   0f), new Vector3(-56f, 0f,   0f),
                new Vector3(  0f, 0f,  56f), new Vector3(  0f, 0f, -56f),
                new Vector3( 28f, 0f, -12f), new Vector3(-28f, 0f,  12f)
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
            BuildWeather(d);   // night / rain / dust storm per map

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

            // v3.1: the perimeter silhouette is now themed. This used to be the
            // same rock ring + four watchtowers on every map, which is the main
            // reason all five arenas looked identical from the cockpit.
            for (int i = 0; i < 26; i++)
            {
                float ang = (i * 13.85f + 11f) * Mathf.Deg2Rad;
                float radius = 66.5f + ((i * 7) % 3) * 0.9f;
                Vector3 pos = new Vector3(Mathf.Sin(ang) * radius, 0f, Mathf.Cos(ang) * radius);
                float yaw = Mathf.Atan2(pos.x, pos.z) * Mathf.Rad2Deg;

                switch (d.Theme)
                {
                    case MapTheme.Desert:
                    {
                        // Big wind-shaped dunes, low and wide.
                        float s = 9f + ((i * 13) % 5) * 3f;
                        var dune = Prim(PrimitiveType.Sphere, "SkylineDune",
                            new Vector3(pos.x, -s * 0.16f, pos.z),
                            new Vector3(s, s * 0.42f, s * 0.8f), rockMat);
                        dune.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);
                        break;
                    }
                    case MapTheme.Urban:
                    {
                        // Distant tower blocks - a real skyline.
                        float h = 14f + ((i * 11) % 6) * 5f;
                        Prim(PrimitiveType.Cube, "SkylineTower",
                            new Vector3(pos.x, h * 0.5f, pos.z),
                            new Vector3(9f + (i % 3) * 3f, h, 9f + (i % 4) * 3f), _wall)
                            .transform.rotation = Quaternion.Euler(0f, yaw + (i % 5) * 7f, 0f);
                        break;
                    }
                    case MapTheme.Forest:
                    {
                        // A wall of tall conifers.
                        float h = 9f + ((i * 17) % 5) * 2.5f;
                        Prim(PrimitiveType.Cylinder, "TreeTrunk",
                            new Vector3(pos.x, h * 0.35f, pos.z),
                            new Vector3(1.4f, h * 0.35f, 1.4f), rockMat);
                        var crown = Prim(PrimitiveType.Sphere, "TreeCrown",
                            new Vector3(pos.x, h * 0.85f, pos.z),
                            new Vector3(7f, h * 0.85f, 7f), _obstacle);
                        Object.DestroyImmediate(crown.GetComponent<Collider>());
                        break;
                    }
                    case MapTheme.Alien:
                    {
                        // Tilted crystal shards leaning over the arena.
                        float h = 12f + ((i * 19) % 5) * 4f;
                        var shard = Prim(PrimitiveType.Cube, "Shard",
                            new Vector3(pos.x, h * 0.4f, pos.z),
                            new Vector3(3.5f, h, 3.5f), _obstacle);
                        shard.transform.rotation =
                            Quaternion.Euler((i % 2 == 0 ? 14f : -11f), yaw, (i % 3) * 6f);
                        break;
                    }
                    default: // Fort
                    {
                        // A continuous rampart with battlements.
                        Prim(PrimitiveType.Cube, "Rampart",
                            new Vector3(pos.x, 4f, pos.z), new Vector3(11f, 8f, 3f), _wall)
                            .transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        Prim(PrimitiveType.Cube, "RampartMerlon",
                            new Vector3(pos.x, 8.7f, pos.z), new Vector3(2.4f, 1.6f, 3.4f), _obstacle)
                            .transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        break;
                    }
                }
            }

            // Corner watchtowers only where a fortified map wants them.
            if (d.Theme == MapTheme.Fort || d.Theme == MapTheme.Urban)
                foreach (var sx in new[] { -1f, 1f })
                    foreach (var sz in new[] { -1f, 1f })
                    {
                        Prim(PrimitiveType.Cylinder, "TowerBase",
                            new Vector3(60f * sx, 3f, 60f * sz), new Vector3(3f, 3f, 3f), _wall);
                        var top = Prim(PrimitiveType.Cube, "TowerTop",
                            new Vector3(60f * sx, 6.4f, 60f * sz), new Vector3(4.2f, 0.9f, 4.2f), _obstacle);
                        Object.DestroyImmediate(top.GetComponent<Collider>()); // out of reach anyway
                    }
        }

        /// <summary>Create a primitive parented to the geometry root, in absolute
        /// world coordinates (scenery is never affected by the layout scale).</summary>
        static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
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
            // v3.1: prop spots follow each map's own layout instead of the same
            // fourteen positions everywhere - props used to sit inside walls on
            // some maps and made every arena read the same on others.
            Vector3[] spots = d.Theme switch
            {
                // Desert: props hug the ruins and the convoy track.
                MapTheme.Desert => new[]
                {
                    new Vector3(-30f, 0f,   4f), new Vector3(-38f, 0f,  -8f),
                    new Vector3(-22f, 0f,  22f), new Vector3( 22f, 0f,  16f),
                    new Vector3( 34f, 0f,   2f), new Vector3( 46f, 0f, -14f),
                    new Vector3(  4f, 0f, -40f), new Vector3(-12f, 0f, -46f),
                    new Vector3( 52f, 0f,  30f), new Vector3(-52f, 0f,  34f),
                    new Vector3( 30f, 0f,  50f), new Vector3(-30f, 0f, -52f),
                    new Vector3( 56f, 0f, -46f), new Vector3(-56f, 0f,  10f)
                },
                // Urban: props line the avenues, never inside a building.
                MapTheme.Urban => new[]
                {
                    new Vector3(  0f, 0f,  23f), new Vector3(  0f, 0f, -23f),
                    new Vector3( 23f, 0f,   0f), new Vector3(-23f, 0f,   0f),
                    new Vector3( 23f, 0f,  23f), new Vector3(-23f, 0f, -23f),
                    new Vector3( 23f, 0f, -23f), new Vector3(-23f, 0f,  23f),
                    new Vector3( 48f, 0f,  12f), new Vector3(-48f, 0f, -12f),
                    new Vector3( 12f, 0f,  48f), new Vector3(-12f, 0f, -48f),
                    new Vector3( 52f, 0f, -50f), new Vector3(-52f, 0f,  50f)
                },
                // Forest: trees crowd the open lanes either side of the ridge.
                MapTheme.Forest => new[]
                {
                    new Vector3(-44f, 0f,  12f), new Vector3(-16f, 0f,  12f),
                    new Vector3( 14f, 0f,  12f), new Vector3( 44f, 0f,  12f),
                    new Vector3(-44f, 0f, -12f), new Vector3(-16f, 0f, -12f),
                    new Vector3( 14f, 0f, -12f), new Vector3( 44f, 0f, -12f),
                    new Vector3(-52f, 0f,  46f), new Vector3( 52f, 0f,  46f),
                    new Vector3(-52f, 0f, -46f), new Vector3( 52f, 0f, -46f),
                    new Vector3(  0f, 0f,  56f), new Vector3(  0f, 0f, -56f)
                },
                // Alien: crystals grow in the gaps between the floating decks.
                MapTheme.Alien => new[]
                {
                    new Vector3( 20f, 0f, -12f), new Vector3(-20f, 0f,  12f),
                    new Vector3( 44f, 0f,   4f), new Vector3(-44f, 0f,  -4f),
                    new Vector3(  6f, 0f,  32f), new Vector3( -6f, 0f, -32f),
                    new Vector3( 50f, 0f,  44f), new Vector3(-50f, 0f, -44f),
                    new Vector3( 50f, 0f, -12f), new Vector3(-50f, 0f,  12f),
                    new Vector3( 18f, 0f,  60f), new Vector3(-18f, 0f, -60f),
                    new Vector3(-40f, 0f,  56f), new Vector3( 40f, 0f, -56f)
                },
                // Fort: barrels in the courtyard, supplies outside the gates.
                _ => new[]
                {
                    new Vector3( 20f, 0f,  36f), new Vector3(-20f, 0f,  36f),
                    new Vector3( 20f, 0f, -36f), new Vector3(-20f, 0f, -36f),
                    new Vector3( 36f, 0f,  20f), new Vector3(-36f, 0f,  20f),
                    new Vector3( 36f, 0f, -20f), new Vector3(-36f, 0f, -20f),
                    new Vector3(  0f, 0f,  56f), new Vector3(  0f, 0f, -56f),
                    new Vector3( 56f, 0f,   0f), new Vector3(-56f, 0f,   0f),
                    new Vector3( 52f, 0f,  52f), new Vector3(-52f, 0f, -52f)
                }
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

        /// <summary>
        /// Fuel barrel: cover you can hide behind, until someone shoots it and
        /// it takes the whole corner with it. These are in-scene NetworkObjects
        /// so the server decides when one blows and every client agrees.
        /// </summary>
        static void Barrel(Vector3 p, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ExplosiveBarrel";
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = new Vector3(p.x, 0.65f, p.z);
            go.transform.localScale = new Vector3(0.9f, 0.65f, 0.9f);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            // NOT static: it has to be able to disappear at runtime.

            go.AddComponent<NetworkObject>();
            var d = go.AddComponent<Destructible>();
            d.maxHealth = GameConstants.BarrelHealth;
            d.explosive = true;
            d.blastRadius = GameConstants.BarrelBlastRadius;
            d.blastDamage = GameConstants.BarrelBlastDamage;
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
            int grassCount = d.Theme == MapTheme.Forest ? 380
                           : d.Theme == MapTheme.Fort || d.Theme == MapTheme.Urban ? 160 : 210;
            for (int i = 0; i < grassCount; i++)
            {
                Vector3 p = new Vector3(Random.Range(-64f, 64f), 0f, Random.Range(-64f, 64f));
                if (p.magnitude < 6f) continue; // keep the very centre clear
                GrassTuft(p, grassMat, Random.Range(0.7f, 1.5f));
            }

            // Bushes big enough to hide a tank inside (no collider = drive in).
            int bushCount = d.Theme == MapTheme.Forest ? 58 : 34;
            for (int i = 0; i < bushCount; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float rad = Random.Range(10f, 62f);
                Vector3 p = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                Bush(p, bushMat, Random.Range(2.2f, 3.4f));
            }

            // Roofed hideouts - only on the maps that lack natural shelter.
            int hideCount = d.Theme switch
            {
                MapTheme.Desert => 6,
                MapTheme.Forest => 5,
                MapTheme.Fort => 3,
                _ => 0            // City and Space already have roofs and decks
            };
            for (int i = 0; i < hideCount; i++)
            {
                float deg = i * 51.4f + 30f;
                float ang = deg * Mathf.Deg2Rad;
                float rad = 38f + (i % 3) * 11f;   // pushed out of the centre fight
                Vector3 p = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                Hideout(p, deg);
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

        /// <summary>
        /// Raised platforms you can drive onto via ramps, plus low cover walls -
        /// gives the arena the vertical, cover-heavy feel of Mini Militia. Tanks
        /// climb the ramps with their CharacterController.
        /// </summary>
        static void BuildPlatforms(MapDef d)
        {
            var platMat = _wall;

            // v3.1: City, Space and Fortress build all of their verticality in
            // their own layouts (rooftops, decks, the keep), so dropping seven
            // identical generic platforms on top of them was exactly what made
            // every map feel the same. Only the two "open" maps get these.
            Vector3[] spots = d.Theme switch
            {
                MapTheme.Desert => new[]
                {
                    new Vector3( 40f, 0f,  40f),
                    new Vector3(-40f, 0f, -40f),
                    new Vector3(-46f, 0f,  30f),
                    new Vector3( 30f, 0f, -50f),
                    new Vector3(  0f, 0f,  56f)
                },
                MapTheme.Forest => new[]
                {
                    new Vector3( 52f, 0f,  26f),   // ranger platforms in the trees
                    new Vector3(-52f, 0f, -26f),
                    new Vector3( 26f, 0f, -52f),
                    new Vector3(-26f, 0f,  52f)
                },
                _ => new Vector3[0]
            };

            foreach (var spot in spots)
            {
                // Flat raised platform (top surface at y = 2).
                Box(d, "Platform", new Vector3(spot.x, 1.0f, spot.z),
                    new Vector3(9f, 2f, 9f), platMat);

                // Ramp leading up toward the arena centre.
                Vector3 toC = new Vector3(-spot.x, 0f, -spot.z).normalized;
                float yaw = Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg;
                Vector3 rampPos = spot + toC * 6.8f;
                var ramp = Box(d, "Ramp", new Vector3(rampPos.x, 1.0f, rampPos.z),
                    new Vector3(5f, 0.5f, 7f), _obstacle);
                ramp.transform.rotation = Quaternion.Euler(-20f, yaw, 0f);

                // A bit of cover on top of the platform.
                Box(d, "PlatformCover", new Vector3(spot.x, 2.6f, spot.z),
                    new Vector3(3f, 1.2f, 1f), _obstacle);
            }

            // Scattered low cover blocks around the middle.
            var prev = Random.state;
            Random.InitState(d.SceneName.GetHashCode() + 99);

            // Scattered low cover - sparse where the layout is already busy.
            int coverCount = d.Theme switch
            {
                MapTheme.Desert => 30,   // the open map needs the most filler
                MapTheme.Forest => 20,
                MapTheme.Urban => 12,
                MapTheme.Alien => 8,
                _ => 14
            };
            for (int i = 0; i < coverCount; i++)
            {
                Vector3 p = new Vector3(Random.Range(-58f, 58f), 0.6f, Random.Range(-58f, 58f));
                if (p.magnitude < 14f) continue;   // never block the centre fight
                var b = Box(d, "Cover", p, new Vector3(Random.Range(2f, 3.5f), 1.2f, Random.Range(1f, 1.6f)), _obstacle);
                b.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 180f), 0f);
            }
            Random.state = prev;
        }

        /// <summary>Slow floating dust motes drifting through the arena air.</summary>
        /// <summary>
        /// Per-map weather and time of day. Each arena gets its own mood so the
        /// five maps stop feeling like recolours of each other: a dust storm at
        /// dusk, a rain-soaked forest, two night maps and one clear day.
        /// Everything here is lighting + particles, so it costs almost nothing.
        /// </summary>
        static void BuildWeather(MapDef d)
        {
            var sun = Object.FindFirstObjectByType<Light>();

            switch (d.Weather)
            {
                case GameConstants.Weather.Night:
                {
                    // Cold moonlight, deep blue ambient, tight fog.
                    if (sun != null)
                    {
                        sun.intensity = 0.42f;
                        sun.color = new Color(0.62f, 0.72f, 1f);
                        sun.transform.rotation = Quaternion.Euler(28f, 200f, 0f);
                    }
                    RenderSettings.ambientSkyColor = new Color(0.10f, 0.13f, 0.22f);
                    RenderSettings.ambientEquatorColor = new Color(0.07f, 0.09f, 0.16f);
                    RenderSettings.ambientGroundColor = new Color(0.03f, 0.04f, 0.07f);
                    RenderSettings.fogColor = new Color(0.05f, 0.07f, 0.12f);
                    RenderSettings.fogStartDistance = 40f;
                    RenderSettings.fogEndDistance = 190f;

                    // A few warm lamps so the map is readable, not pitch black.
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * 60f * Mathf.Deg2Rad;
                        var lampGo = new GameObject($"NightLamp_{i}", typeof(Light));
                        lampGo.transform.position =
                            new Vector3(Mathf.Cos(ang) * 34f, 9f, Mathf.Sin(ang) * 34f);
                        var lamp = lampGo.GetComponent<Light>();
                        lamp.type = LightType.Point;
                        lamp.range = 42f;
                        lamp.intensity = 2.4f;
                        lamp.color = new Color(1f, 0.82f, 0.55f);
                        lamp.shadows = LightShadows.None;   // cheap on mobile
                    }
                    break;
                }

                case GameConstants.Weather.Rain:
                {
                    if (sun != null)
                    {
                        sun.intensity = 0.75f;
                        sun.color = new Color(0.78f, 0.82f, 0.9f);
                    }
                    RenderSettings.ambientSkyColor = new Color(0.30f, 0.34f, 0.38f);
                    RenderSettings.fogColor = new Color(0.42f, 0.46f, 0.50f);
                    RenderSettings.fogStartDistance = 30f;
                    RenderSettings.fogEndDistance = 165f;
                    MakeWeatherParticles("Rain",
                        colour: new Color(0.72f, 0.82f, 0.95f, 0.55f),
                        size: new Vector2(0.035f, 0.075f),
                        lifetime: 1.5f, rate: 950f, downSpeed: 26f,
                        sideDrift: 1.6f, stretched: true);
                    break;
                }

                case GameConstants.Weather.DustStorm:
                {
                    if (sun != null)
                    {
                        sun.intensity = 1.05f;
                        sun.color = new Color(1f, 0.80f, 0.55f);   // low orange sun
                        sun.transform.rotation = Quaternion.Euler(18f, 40f, 0f);
                    }
                    RenderSettings.ambientSkyColor = new Color(0.62f, 0.48f, 0.32f);
                    RenderSettings.ambientEquatorColor = new Color(0.48f, 0.38f, 0.26f);
                    RenderSettings.fogColor = new Color(0.72f, 0.58f, 0.38f);
                    RenderSettings.fogStartDistance = 25f;
                    RenderSettings.fogEndDistance = 150f;   // sand cuts the view down
                    MakeWeatherParticles("DustStorm",
                        colour: new Color(0.85f, 0.72f, 0.50f, 0.30f),
                        size: new Vector2(1.4f, 3.6f),
                        lifetime: 4.5f, rate: 130f, downSpeed: 1.2f,
                        sideDrift: 16f, stretched: false);
                    break;
                }
            }
        }

        /// <summary>One world-space particle volume covering the whole arena.</summary>
        static void MakeWeatherParticles(string name, Color colour, Vector2 size,
                                         float lifetime, float rate, float downSpeed,
                                         float sideDrift, bool stretched)
        {
            string path = $"{PrefabBuilder.MaterialDir}/FX_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
                AssetDatabase.CreateAsset(mat, path);
            }

            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.position = new Vector3(0f, 26f, 0f);
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = lifetime;
            main.startSpeed = downSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = colour;
            main.maxParticles = 1400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.rateOverTime = rate;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Cover the whole 140x140 arena from above.
            shape.scale = new Vector3(ArenaHalf * 2.2f, 2f, ArenaHalf * 2.2f);
            shape.rotation = new Vector3(90f, 0f, 0f);   // emit downward

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-sideDrift, sideDrift);
            vel.z = new ParticleSystem.MinMaxCurve(-sideDrift * 0.4f, sideDrift * 0.4f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            if (stretched)
            {
                // Rain reads much better as streaks than as dots.
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.velocityScale = 0.12f;
                renderer.lengthScale = 3.2f;
            }
        }

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

        static GameObject Sphere(MapDef d, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            if (_obstacleParent != null) go.transform.SetParent(_obstacleParent, false);
            go.transform.position = new Vector3(pos.x * _layoutScale, pos.y, pos.z * _layoutScale);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = _obstacle;
            go.isStatic = true;
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
