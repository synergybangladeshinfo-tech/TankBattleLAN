using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using TankBattle.Gameplay;
using TankBattle.Networking;
using TankBattle.Utils;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Builds all runtime prefabs (Tank with 3 body styles, textured materials
    /// and particle effects, Bullet with trail, WeaponCrate, NetworkManager,
    /// plus a visual-only TankPreview for the Garage screen) and the shared
    /// materials from Unity primitives. Invoked by TankBattleSetup.
    /// </summary>
    public static class PrefabBuilder
    {
        public const string PrefabDir = "Assets/Prefabs";
        public const string MaterialDir = "Assets/Materials";

        // ------------------------------------------------------------- materials

        public static Material CreateMaterial(string name, Color color, bool unlit = false)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) { existing.color = color; return existing; }

            var mat = new Material(Shader.Find(unlit ? "Unlit/Color" : "Standard"));
            mat.color = color;
            if (!unlit)
            {
                mat.SetFloat("_Glossiness", 0.15f); // matte low-poly look
                mat.SetFloat("_Metallic", 0f);
            }
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Standard material with an albedo texture (+ optional normal map).</summary>
        public static Material CreateTexturedMaterial(string name, Color color,
            Texture2D albedo, float tiling = 1f, Texture2D normal = null,
            float glossiness = 0.18f, float metallic = 0f)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            mat.mainTexture = albedo;
            mat.mainTextureScale = new Vector2(tiling, tiling);
            mat.SetFloat("_Glossiness", glossiness);
            mat.SetFloat("_Metallic", metallic);
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", normal);
                mat.SetTextureScale("_BumpMap", new Vector2(tiling, tiling));
                mat.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Particle material (saved as an asset so the shader ships in builds).</summary>
        static Material CreateFxMaterial(string name, string shaderName, Color tint)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find(shaderName));
            if (mat.HasProperty("_TintColor")) mat.SetColor("_TintColor", tint);
            else mat.color = tint;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>The three tank materials (camo hull / rubber tracks / steel parts).</summary>
        static (Material hull, Material dark, Material metal) TankMaterials()
        {
            var hull = CreateTexturedMaterial("Tank_Base", Color.white,
                TextureBuilder.Camo, 1f, null, 0.22f, 0.05f);
            var dark = CreateTexturedMaterial("Tank_Dark", new Color(0.85f, 0.85f, 0.85f),
                TextureBuilder.Track, 1.5f, null, 0.1f, 0f);
            var metal = CreateTexturedMaterial("Tank_Metal", new Color(0.82f, 0.84f, 0.88f),
                TextureBuilder.MetalPlate, 1f, null, 0.7f, 0.85f); // shinier, reflective
            return (hull, dark, metal);
        }

        // ----------------------------------------------------------------- tank

        public static GameObject BuildTankPrefab()
        {
            var (hullMat, darkMat, metalMat) = TankMaterials();
            var barBg = CreateMaterial("HealthBar_BG", new Color(0.1f, 0.1f, 0.1f), unlit: true);
            var barFill = CreateMaterial("HealthBar_Fill", Color.green, unlit: true);
            var fxAdd = CreateFxMaterial("FX_Additive", "Legacy Shaders/Particles/Additive",
                new Color(1f, 0.8f, 0.4f, 0.6f));
            var fxSmoke = CreateFxMaterial("FX_Smoke", "Legacy Shaders/Particles/Alpha Blended",
                new Color(0.25f, 0.25f, 0.25f, 0.55f));

            var root = new GameObject("Tank");
            try
            {
                // Physics body: a single capsule via CharacterController.
                var cc = root.AddComponent<CharacterController>();
                cc.center = new Vector3(0f, 0.8f, 0f);
                cc.radius = 0.8f;
                cc.height = 1.6f;
                cc.slopeLimit = 55f;    // climb the ramps
                cc.stepOffset = 0.6f;   // step onto low ledges/platforms

                // ---- three swappable hull styles (TankController enables one) ----
                BuildStandardHull(NewHull(root, 0), hullMat, darkMat, metalMat);
                BuildHeavyHull(NewHull(root, 1), hullMat, darkMat, metalMat);
                BuildScoutHull(NewHull(root, 2), hullMat, darkMat, metalMat);

                // ---- independent rotating turret (shared by all hull styles) ----
                var muzzle = BuildTurret(root, hullMat, metalMat);

                // ---- particle effects (played by gameplay scripts by name) ----
                var flash = AddParticles(root, "MuzzleFlashPS", fxAdd,
                    new Color(1f, 0.75f, 0.25f), burst: 14, life: 0.12f, speed: 6f,
                    size: 0.4f, cone: true);
                flash.transform.SetParent(muzzle, false);

                AddParticles(root, "HitSparkPS", fxAdd,
                    new Color(1f, 0.85f, 0.3f), burst: 18, life: 0.3f, speed: 5f,
                    size: 0.18f, cone: false).transform.localPosition = new Vector3(0f, 1f, 0f);

                var smoke = AddParticles(root, "SmokePS", fxSmoke,
                    new Color(0.2f, 0.2f, 0.2f, 0.6f), burst: 0, life: 1.2f, speed: 1.6f,
                    size: 0.8f, cone: false, loop: true, rate: 12f);
                smoke.transform.localPosition = new Vector3(0f, 1.35f, -0.4f);

                AddParticles(root, "ExplosionPS", fxAdd,
                    new Color(1f, 0.5f, 0.15f), burst: 48, life: 0.75f, speed: 9f,
                    size: 1.0f, cone: false).transform.localPosition = new Vector3(0f, 1f, 0f);

                // Track dust kicked up while driving (rate driven by TankController).
                var dust = AddParticles(root, "DustPS", fxSmoke,
                    new Color(0.75f, 0.68f, 0.55f, 0.35f), burst: 0, life: 0.9f, speed: 1.2f,
                    size: 0.7f, cone: false, loop: true, rate: 0f, autoPlay: true);
                dust.transform.localPosition = new Vector3(0f, 0.15f, -1.1f);

                // Shield bubble: translucent cyan sphere shown while invincible.
                var shieldMat = CreateFxMaterial("FX_Shield",
                    "Legacy Shaders/Transparent/Diffuse", new Color(0.25f, 0.85f, 1f, 0.35f));
                shieldMat.color = new Color(0.25f, 0.85f, 1f, 0.35f);
                var bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                bubble.name = "ShieldBubble";
                bubble.transform.SetParent(root.transform, false);
                bubble.transform.localPosition = new Vector3(0f, 1.0f, 0f);
                bubble.transform.localScale = Vector3.one * 3.4f;
                Object.DestroyImmediate(bubble.GetComponent<Collider>());
                var bmr = bubble.GetComponent<MeshRenderer>();
                bmr.sharedMaterial = shieldMat;
                bmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bmr.receiveShadows = false;
                bubble.SetActive(false); // toggled by TankHealth

                // Overhead health bar (billboarded quads).
                var barPivot = new GameObject("HealthBarPivot").transform;
                barPivot.SetParent(root.transform, false);
                barPivot.localPosition = new Vector3(0f, 2.2f, 0f);
                barPivot.localScale = new Vector3(1.6f, 0.22f, 1f);
                barPivot.gameObject.AddComponent<Billboard>();

                AddQuad(barPivot, "BG", new Vector3(0f, 0f, 0.02f), Vector3.one, barBg);
                var fill = AddQuad(barPivot, "Fill", Vector3.zero, new Vector3(1f, 0.75f, 1f), barFill);

                // Networking + gameplay components.
                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<ClientNetworkTransform>();
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.SyncRotAngleX = nt.SyncRotAngleZ = false; // yaw only
                nt.Interpolate = true;
                nt.PositionThreshold = 0.01f;
                nt.RotAngleThreshold = 0.5f;

                root.AddComponent<TankController>();

                // Engine rumble + rolling wheels: both measure the tank's real

                // replicated motion, so they work for players, remotes and bots.

                root.AddComponent<TankBattle.Audio.TankEngineAudio>();

                root.AddComponent<TrackAnimator>();
                root.AddComponent<TurretAim>();   // rotates the TurretPivot
                var health = root.AddComponent<TankHealth>();
                health.healthBarFill = fill.transform;
                health.healthBarFillRenderer = fill.GetComponent<MeshRenderer>();
                var shooting = root.AddComponent<TankShooting>();
                shooting.muzzle = muzzle;

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Tank.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// Visual-only tank (all three hulls, no scripts) placed in Resources
        /// so the Garage screen can show a live rotating 3D preview.
        /// </summary>
        public static void BuildPreviewPrefab()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var (hullMat, darkMat, metalMat) = TankMaterials();
            var root = new GameObject("TankPreview");
            try
            {
                BuildStandardHull(NewHull(root, 0), hullMat, darkMat, metalMat);
                BuildHeavyHull(NewHull(root, 1), hullMat, darkMat, metalMat);
                BuildScoutHull(NewHull(root, 2), hullMat, darkMat, metalMat);
                BuildTurret(root, hullMat, metalMat); // static gun for the preview
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/TankPreview.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static GameObject NewHull(GameObject root, int index)
        {
            var hull = new GameObject($"Hull_{index}");
            hull.transform.SetParent(root.transform, false);
            hull.SetActive(index == 0); // Standard visible by default
            return hull;
        }



        /// <summary>
        /// Shared rotating turret (cannon) on a pivot at the tank root, so it can
        /// yaw independently of the hull. Returns the muzzle transform (bullet
        /// spawn point) at the barrel tip.
        /// </summary>
        static Transform BuildTurret(GameObject root, Material hullMat, Material metalMat)
        {
            var pivot = new GameObject("TurretPivot").transform;
            pivot.SetParent(root.transform, false);
            pivot.localPosition = new Vector3(0f, 1.05f, -0.05f);

            // Turret box (camo, tinted with the player colour) + steel barrel.
            AddPart(pivot.gameObject, PrimitiveType.Cube, "Turret",
                new Vector3(0f, 0f, -0.05f), new Vector3(1.0f, 0.42f, 1.05f), hullMat);
            var barrel = AddPart(pivot.gameObject, PrimitiveType.Cylinder, "Barrel",
                new Vector3(0f, 0f, 0.95f), new Vector3(0.15f, 0.6f, 0.15f), metalMat);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            AddPart(pivot.gameObject, PrimitiveType.Cube, "MuzzleBrake",
                new Vector3(0f, 0f, 1.5f), new Vector3(0.24f, 0.24f, 0.2f), metalMat);
            AddPart(pivot.gameObject, PrimitiveType.Cylinder, "Hatch",
                new Vector3(-0.25f, 0.24f, -0.3f), new Vector3(0.3f, 0.05f, 0.3f), metalMat);

            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(pivot, false);
            muzzle.localPosition = new Vector3(0f, 0f, 1.9f);
            return muzzle;
        }

        /// <summary>
        /// Shared running gear: a row of road wheels plus a drive sprocket and
        /// idler at each end, mudguards and the track surface itself.
        /// TrackAnimator finds these by name ("Wheel*", "Sprocket*", "Idler*",
        /// "Track*") and spins/scrolls them from the tank's real movement, so a
        /// driving tank actually looks driven instead of sliding.
        /// </summary>
        static void BuildRunningGear(GameObject h, Material dark, Material metal,
                                     float halfWidth, float length, float wheelR,
                                     int wheelCount, float trackHeight)
        {
            for (int side = 0; side < 2; side++)
            {
                float sx = side == 0 ? -halfWidth : halfWidth;
                string tag = side == 0 ? "L" : "R";

                // Track surface: a thin slab either side. Its material is
                // instanced per tank so the UV scroll is independent.
                AddPart(h, PrimitiveType.Cube, "Track" + tag,
                    new Vector3(sx, trackHeight, 0f),
                    new Vector3(0.30f, wheelR * 2.05f, length), dark);

                // Road wheels between the sprocket and idler.
                float span = length * 0.5f - wheelR * 1.15f;
                for (int i = 0; i < wheelCount; i++)
                {
                    float t = wheelCount == 1 ? 0.5f : i / (float)(wheelCount - 1);
                    float z = Mathf.Lerp(-span, span, t);
                    var w = AddPart(h, PrimitiveType.Cylinder, "Wheel" + tag + i,
                        new Vector3(sx, wheelR, z),
                        new Vector3(wheelR * 1.85f, 0.09f, wheelR * 1.85f), metal);
                    // Lay the cylinder on its side so it rolls around local X.
                    w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }

                // Drive sprocket (rear) and idler (front) sit slightly higher.
                var spr = AddPart(h, PrimitiveType.Cylinder, "Sprocket" + tag,
                    new Vector3(sx, wheelR * 1.15f, -length * 0.5f + wheelR * 0.9f),
                    new Vector3(wheelR * 2.1f, 0.10f, wheelR * 2.1f), metal);
                spr.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                var idl = AddPart(h, PrimitiveType.Cylinder, "Idler" + tag,
                    new Vector3(sx, wheelR * 1.15f, length * 0.5f - wheelR * 0.9f),
                    new Vector3(wheelR * 1.95f, 0.10f, wheelR * 1.95f), metal);
                idl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                // Mudguard over the top run of the track.
                AddPart(h, PrimitiveType.Cube, "Fender" + tag,
                    new Vector3(sx, wheelR * 2.15f, 0f),
                    new Vector3(0.42f, 0.07f, length * 0.98f), dark);
            }
        }

        /// <summary>
        /// Stowage boxes, tow hooks and an exhaust - the small clutter that makes
        /// a box read as a real fighting vehicle.
        /// </summary>
        static void AddStowage(GameObject h, Material metal,
                               float halfWidth, float deckY, float rearZ)
        {
            AddPart(h, PrimitiveType.Cube, "StowageL",
                new Vector3(-halfWidth * 0.78f, deckY + 0.10f, rearZ + 0.32f),
                new Vector3(0.34f, 0.20f, 0.55f), metal);
            AddPart(h, PrimitiveType.Cube, "StowageR",
                new Vector3(halfWidth * 0.78f, deckY + 0.10f, rearZ + 0.32f),
                new Vector3(0.34f, 0.20f, 0.55f), metal);

            var ex = AddPart(h, PrimitiveType.Cylinder, "ExhaustPipe",
                new Vector3(halfWidth * 0.55f, deckY + 0.16f, rearZ + 0.05f),
                new Vector3(0.13f, 0.22f, 0.13f), metal);
            ex.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            AddPart(h, PrimitiveType.Cube, "TowHookL",
                new Vector3(-halfWidth * 0.45f, deckY - 0.28f, rearZ - 0.30f),
                new Vector3(0.14f, 0.12f, 0.18f), metal);
            AddPart(h, PrimitiveType.Cube, "TowHookR",
                new Vector3(halfWidth * 0.45f, deckY - 0.28f, rearZ - 0.30f),
                new Vector3(0.14f, 0.12f, 0.18f), metal);
        }

        /// <summary>Style 0 - STANDARD: the classic balanced silhouette.</summary>
        static void BuildStandardHull(GameObject h, Material hull, Material dark, Material metal)
        {
            // Lower hull tub.
            AddPart(h, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.55f, 0f), new Vector3(1.45f, 0.50f, 2.15f), hull);

            // Sloped glacis at the front - the classic tank silhouette cue.
            var glacis = AddPart(h, PrimitiveType.Cube, "Glacis",
                new Vector3(0f, 0.68f, 1.12f), new Vector3(1.42f, 0.55f, 0.20f), hull);
            glacis.transform.localRotation = Quaternion.Euler(-38f, 0f, 0f);

            // Upper deck.
            AddPart(h, PrimitiveType.Cube, "Deck",
                new Vector3(0f, 0.83f, -0.05f), new Vector3(1.30f, 0.16f, 1.95f), hull);

            // Rear plate, angled the other way.
            var rear = AddPart(h, PrimitiveType.Cube, "RearPlate",
                new Vector3(0f, 0.66f, -1.10f), new Vector3(1.35f, 0.48f, 0.18f), hull);
            rear.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);

            // Driver's periscope + headlights.
            AddPart(h, PrimitiveType.Cube, "Periscope",
                new Vector3(-0.35f, 0.94f, 0.72f), new Vector3(0.22f, 0.10f, 0.14f), metal);
            var hlL = AddPart(h, PrimitiveType.Cylinder, "HeadlightL",
                new Vector3(-0.52f, 0.90f, 1.03f), new Vector3(0.15f, 0.04f, 0.15f), metal);
            hlL.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var hlR = AddPart(h, PrimitiveType.Cylinder, "HeadlightR",
                new Vector3(0.52f, 0.90f, 1.03f), new Vector3(0.15f, 0.04f, 0.15f), metal);
            hlR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            BuildRunningGear(h, dark, metal, 0.85f, 2.35f, 0.24f, 5, 0.30f);
            AddStowage(h, metal, 0.85f, 0.83f, -0.75f);
        }

        /// <summary>Style 1 - HEAVY: wide, slab-sided, obviously armoured.</summary>
        static void BuildHeavyHull(GameObject h, Material hull, Material dark, Material metal)
        {
            AddPart(h, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.58f, 0f), new Vector3(1.75f, 0.62f, 2.25f), hull);

            var glacis = AddPart(h, PrimitiveType.Cube, "Glacis",
                new Vector3(0f, 0.74f, 1.16f), new Vector3(1.72f, 0.66f, 0.24f), hull);
            glacis.transform.localRotation = Quaternion.Euler(-32f, 0f, 0f);

            AddPart(h, PrimitiveType.Cube, "Deck",
                new Vector3(0f, 0.92f, -0.05f), new Vector3(1.58f, 0.18f, 2.05f), hull);

            // Bolt-on side skirts - the visual signature of the heavy.
            AddPart(h, PrimitiveType.Cube, "SkirtL",
                new Vector3(-1.02f, 0.80f, 0f), new Vector3(0.16f, 0.42f, 2.10f), hull);
            AddPart(h, PrimitiveType.Cube, "SkirtR",
                new Vector3(1.02f, 0.80f, 0f), new Vector3(0.16f, 0.42f, 2.10f), hull);

            // Spare track links bolted to the nose as extra armour.
            for (int i = 0; i < 3; i++)
                AddPart(h, PrimitiveType.Cube, "SpareLink" + i,
                    new Vector3(-0.38f + i * 0.38f, 0.60f, 1.28f),
                    new Vector3(0.30f, 0.16f, 0.10f), metal);

            var rear = AddPart(h, PrimitiveType.Cube, "RearPlate",
                new Vector3(0f, 0.70f, -1.16f), new Vector3(1.68f, 0.55f, 0.20f), hull);
            rear.transform.localRotation = Quaternion.Euler(18f, 0f, 0f);

            var ex1 = AddPart(h, PrimitiveType.Cylinder, "ExhaustL",
                new Vector3(-0.55f, 1.04f, -1.02f), new Vector3(0.14f, 0.22f, 0.14f), metal);
            ex1.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var ex2 = AddPart(h, PrimitiveType.Cylinder, "ExhaustR",
                new Vector3(0.55f, 1.04f, -1.02f), new Vector3(0.14f, 0.22f, 0.14f), metal);
            ex2.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);

            BuildRunningGear(h, dark, metal, 1.00f, 2.55f, 0.27f, 6, 0.32f);
            AddStowage(h, metal, 1.00f, 0.92f, -0.80f);
        }

        /// <summary>Style 2 - SCOUT: low, narrow and wedge-nosed.</summary>
        static void BuildScoutHull(GameObject h, Material hull, Material dark, Material metal)
        {
            AddPart(h, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.50f, -0.10f), new Vector3(1.15f, 0.40f, 1.95f), hull);

            var nose = AddPart(h, PrimitiveType.Cube, "Nose",
                new Vector3(0f, 0.60f, 1.00f), new Vector3(1.05f, 0.34f, 0.75f), hull);
            nose.transform.localRotation = Quaternion.Euler(-24f, 0f, 0f);

            AddPart(h, PrimitiveType.Cube, "Deck",
                new Vector3(0f, 0.72f, -0.15f), new Vector3(1.02f, 0.14f, 1.75f), hull);

            // Angled cheeks give it a fast, faceted look.
            var cheekL = AddPart(h, PrimitiveType.Cube, "CheekL",
                new Vector3(-0.56f, 0.62f, 0.30f), new Vector3(0.16f, 0.34f, 1.30f), hull);
            cheekL.transform.localRotation = Quaternion.Euler(0f, 0f, 22f);
            var cheekR = AddPart(h, PrimitiveType.Cube, "CheekR",
                new Vector3(0.56f, 0.62f, 0.30f), new Vector3(0.16f, 0.34f, 1.30f), hull);
            cheekR.transform.localRotation = Quaternion.Euler(0f, 0f, -22f);

            var antenna = AddPart(h, PrimitiveType.Cylinder, "Antenna",
                new Vector3(-0.34f, 1.00f, -0.62f), new Vector3(0.03f, 0.48f, 0.03f), metal);
            antenna.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);

            var hlL = AddPart(h, PrimitiveType.Cylinder, "HeadlightL",
                new Vector3(-0.40f, 0.80f, 1.24f), new Vector3(0.13f, 0.04f, 0.13f), metal);
            hlL.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var hlR = AddPart(h, PrimitiveType.Cylinder, "HeadlightR",
                new Vector3(0.40f, 0.80f, 1.24f), new Vector3(0.13f, 0.04f, 0.13f), metal);
            hlR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            BuildRunningGear(h, dark, metal, 0.70f, 2.20f, 0.21f, 4, 0.26f);
            AddStowage(h, metal, 0.70f, 0.72f, -0.70f);
        }

        // --------------------------------------------------------------- bullet

        public static GameObject BuildBulletPrefab()
        {
            var mat = CreateMaterial("Bullet", new Color(1f, 0.85f, 0.2f), unlit: true);
            var fxAdd = CreateFxMaterial("FX_Additive", "Legacy Shaders/Particles/Additive",
                new Color(1f, 0.8f, 0.4f, 0.6f));

            var root = new GameObject("Bullet");
            try
            {
                // Visual only - hit detection is a server-side spherecast, and
                // having no collider means bullets never block each other.
                AddPart(root, PrimitiveType.Sphere, "Visual",
                    Vector3.zero, Vector3.one * 0.35f, mat);

                // Glowing trail - makes every projectile easy to track.
                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = fxAdd;
                trail.time = 0.25f;
                trail.startWidth = 0.22f;
                trail.endWidth = 0f;
                trail.minVertexDistance = 0.15f;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;

                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<NetworkTransform>(); // server authoritative
                nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.Interpolate = true;
                nt.PositionThreshold = 0.01f;

                root.AddComponent<Bullet>();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Bullet.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // --------------------------------------------------------- weapon crate

        public static GameObject BuildPickupPrefab()
        {
            var crateMat = CreateTexturedMaterial("Crate", Color.white,
                TextureBuilder.Planks, 1f); // tinted per weapon at runtime

            var root = new GameObject("WeaponCrate");
            try
            {
                AddPart(root, PrimitiveType.Cube, "Crate",
                    Vector3.zero, new Vector3(1.1f, 1.1f, 1.1f), crateMat);
                AddPart(root, PrimitiveType.Cube, "CrateCore",
                    Vector3.zero, new Vector3(0.75f, 1.25f, 0.75f), crateMat);

                root.AddComponent<NetworkObject>();
                root.AddComponent<WeaponPickup>();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/WeaponCrate.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // -------------------------------------------------------------- grenade

        public static GameObject BuildGrenadePrefab()
        {
            var body = CreateMaterial("Grenade", new Color(0.22f, 0.45f, 0.2f));      // olive
            var band = CreateMaterial("GrenadeBand", new Color(0.85f, 0.72f, 0.18f)); // yellow

            var root = new GameObject("Grenade");
            try
            {
                AddPart(root, PrimitiveType.Sphere, "Visual", Vector3.zero, Vector3.one * 0.45f, body);
                AddPart(root, PrimitiveType.Cube, "Band", Vector3.zero,
                    new Vector3(0.5f, 0.14f, 0.5f), band);

                root.AddComponent<NetworkObject>();
                var nt = root.AddComponent<NetworkTransform>(); // server-authoritative arc
                nt.SyncRotAngleX = nt.SyncRotAngleY = nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.Interpolate = true;
                nt.PositionThreshold = 0.02f;
                root.AddComponent<Grenade>();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Grenade.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ------------------------------------------------------- network manager

        /// <summary>
        /// Proximity mine dropped by the MINE weapon: a squat dark disc with a
        /// small emissive "Light" child the Mine script pulses while it arms.
        /// </summary>
        public static GameObject BuildMinePrefab()
        {
            var body = CreateMaterial("Mine", new Color(0.16f, 0.17f, 0.19f));   // dark casing
            var lamp = CreateMaterial("MineLight", new Color(1f, 0.25f, 0.2f));  // warning light

            var root = new GameObject("Mine");
            try
            {
                AddPart(root, PrimitiveType.Cylinder, "Visual", Vector3.zero,
                    new Vector3(0.7f, 0.09f, 0.7f), body);
                AddPart(root, PrimitiveType.Sphere, "Light", new Vector3(0f, 0.12f, 0f),
                    Vector3.one * 0.22f, lamp);

                // Blast particles, reusing the shared explosion look.
                var boom = new GameObject("ExplosionPS");
                boom.transform.SetParent(root.transform, false);
                var ps = boom.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 0.5f;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = 0.55f;
                main.startSpeed = 9f;
                main.startSize = 0.7f;
                main.startColor = new Color(1f, 0.55f, 0.15f);
                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)26) });
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.3f;
                ps.Stop();

                root.AddComponent<NetworkObject>();
                root.AddComponent<Mine>();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/Mine.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        public static GameObject BuildNetworkManagerPrefab(GameObject tankPrefab,
            GameObject bulletPrefab, GameObject pickupPrefab, GameObject grenadePrefab,
            GameObject minePrefab)
        {
            var root = new GameObject("NetworkManager");
            try
            {
                var nm = root.AddComponent<NetworkManager>();
                var utp = root.AddComponent<UnityTransport>();
                nm.NetworkConfig.NetworkTransport = utp;
                nm.NetworkConfig.EnableSceneManagement = true; // host drives map loads
                nm.NetworkConfig.ConnectionApproval = true;    // player cap + names
                nm.NetworkConfig.TickRate = 30;                // fine for 16 tanks on LAN

                var cm = root.AddComponent<ConnectionManager>();
                root.AddComponent<LanDiscovery>();

                // Wire the private serialized prefab references.
                var so = new SerializedObject(cm);
                so.FindProperty("tankPrefab").objectReferenceValue = tankPrefab;
                so.FindProperty("bulletPrefab").objectReferenceValue = bulletPrefab;
                so.FindProperty("pickupPrefab").objectReferenceValue = pickupPrefab;
                so.FindProperty("grenadePrefab").objectReferenceValue = grenadePrefab;
                so.FindProperty("minePrefab").objectReferenceValue = minePrefab;
                so.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/NetworkManager.prefab");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // ---------------------------------------------------------------- utils

        /// <summary>Primitive child with its collider stripped.</summary>
        public static GameObject AddPart(GameObject parent, PrimitiveType type, string name,
            Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject AddQuad(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>One pre-configured burst/loop particle system child.</summary>
        static ParticleSystem AddParticles(GameObject parent, string name, Material mat,
            Color color, int burst, float life, float speed, float size,
            bool cone, bool loop = false, float rate = 0f, bool autoPlay = false)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent.transform, false);
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = loop;
            main.playOnAwake = autoPlay;
            main.startLifetime = life;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = Mathf.Max(burst * 2, 60);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            if (loop)
            {
                emission.rateOverTime = rate;
            }
            else
            {
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burst) });
            }

            var shape = ps.shape;
            if (cone)
            {
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 18f;
                shape.radius = 0.08f;
            }
            else
            {
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.4f;
            }

            // Fade out over lifetime for softer edges.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return ps;
        }
    }
}
