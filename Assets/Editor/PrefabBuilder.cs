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
    /// Builds all runtime prefabs (Tank with 3 body styles + particle effects,
    /// Bullet with trail, WeaponCrate, NetworkManager) and the shared materials
    /// from Unity primitives. Invoked by TankBattleSetup.
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

        // ----------------------------------------------------------------- tank

        public static GameObject BuildTankPrefab()
        {
            var tankMat = CreateMaterial("Tank_Base", Color.white);
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

                // ---- three swappable hull styles (TankController enables one) ----
                BuildStandardHull(NewHull(root, 0), tankMat);
                BuildHeavyHull(NewHull(root, 1), tankMat);
                BuildScoutHull(NewHull(root, 2), tankMat);

                // Muzzle: bullet spawn point just past every barrel tip.
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = new Vector3(0f, 1.05f, 1.9f);

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

        static GameObject NewHull(GameObject root, int index)
        {
            var hull = new GameObject($"Hull_{index}");
            hull.transform.SetParent(root.transform, false);
            hull.SetActive(index == 0); // Standard visible by default
            return hull;
        }

        /// <summary>Style 0 - STANDARD: the classic balanced silhouette.</summary>
        static void BuildStandardHull(GameObject h, Material mat)
        {
            AddPart(h, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 0.55f, 2.1f), mat);
            AddPart(h, PrimitiveType.Cube, "TrackL",
                new Vector3(-0.85f, 0.35f, 0f), new Vector3(0.4f, 0.5f, 2.3f), mat);
            AddPart(h, PrimitiveType.Cube, "TrackR",
                new Vector3(0.85f, 0.35f, 0f), new Vector3(0.4f, 0.5f, 2.3f), mat);
            AddPart(h, PrimitiveType.Cube, "Turret",
                new Vector3(0f, 1.05f, -0.1f), new Vector3(0.9f, 0.4f, 1.0f), mat);
            var barrel = AddPart(h, PrimitiveType.Cylinder, "Barrel",
                new Vector3(0f, 1.05f, 0.95f), new Vector3(0.14f, 0.55f, 0.14f), mat);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            AddPart(h, PrimitiveType.Cube, "MuzzleBrake",
                new Vector3(0f, 1.05f, 1.45f), new Vector3(0.22f, 0.22f, 0.18f), mat);
            var hatch = AddPart(h, PrimitiveType.Cylinder, "Hatch",
                new Vector3(-0.2f, 1.3f, -0.25f), new Vector3(0.3f, 0.05f, 0.3f), mat);
            hatch.transform.localRotation = Quaternion.identity;
        }

        /// <summary>Style 1 - HEAVY: wide armored brute with twin exhausts.</summary>
        static void BuildHeavyHull(GameObject h, Material mat)
        {
            AddPart(h, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.58f, 0f), new Vector3(1.8f, 0.65f, 2.2f), mat);
            AddPart(h, PrimitiveType.Cube, "TrackL",
                new Vector3(-1.0f, 0.38f, 0f), new Vector3(0.5f, 0.55f, 2.5f), mat);
            AddPart(h, PrimitiveType.Cube, "TrackR",
                new Vector3(1.0f, 0.38f, 0f), new Vector3(0.5f, 0.55f, 2.5f), mat);
            AddPart(h, PrimitiveType.Cube, "ArmorL",
                new Vector3(-1.0f, 0.75f, 0f), new Vector3(0.35f, 0.25f, 2.0f), mat);
            AddPart(h, PrimitiveType.Cube, "ArmorR",
                new Vector3(1.0f, 0.75f, 0f), new Vector3(0.35f, 0.25f, 2.0f), mat);
            AddPart(h, PrimitiveType.Cube, "Turret",
                new Vector3(0f, 1.12f, -0.15f), new Vector3(1.15f, 0.5f, 1.2f), mat);
            var barrel = AddPart(h, PrimitiveType.Cylinder, "Barrel",
                new Vector3(0f, 1.05f, 0.95f), new Vector3(0.2f, 0.55f, 0.2f), mat);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            AddPart(h, PrimitiveType.Cube, "MuzzleBrake",
                new Vector3(0f, 1.05f, 1.5f), new Vector3(0.3f, 0.3f, 0.25f), mat);
            var ex1 = AddPart(h, PrimitiveType.Cylinder, "ExhaustL",
                new Vector3(-0.5f, 1.0f, -1.05f), new Vector3(0.12f, 0.2f, 0.12f), mat);
            ex1.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var ex2 = AddPart(h, PrimitiveType.Cylinder, "ExhaustR",
                new Vector3(0.5f, 1.0f, -1.05f), new Vector3(0.12f, 0.2f, 0.12f), mat);
            ex2.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
        }

        /// <summary>Style 2 - SCOUT: slim, angular and fast-looking.</summary>
        static void BuildScoutHull(GameObject h, Material mat)
        {
            AddPart(h, PrimitiveType.Cube, "Body",
                new Vector3(0f, 0.52f, -0.1f), new Vector3(1.2f, 0.45f, 1.9f), mat);
            var nose = AddPart(h, PrimitiveType.Cube, "Nose",
                new Vector3(0f, 0.62f, 1.0f), new Vector3(1.0f, 0.35f, 0.7f), mat);
            nose.transform.localRotation = Quaternion.Euler(-18f, 0f, 0f);
            AddPart(h, PrimitiveType.Cube, "TrackL",
                new Vector3(-0.7f, 0.33f, 0f), new Vector3(0.32f, 0.45f, 2.2f), mat);
            AddPart(h, PrimitiveType.Cube, "TrackR",
                new Vector3(0.7f, 0.33f, 0f), new Vector3(0.32f, 0.45f, 2.2f), mat);
            AddPart(h, PrimitiveType.Cube, "Turret",
                new Vector3(0f, 0.98f, -0.25f), new Vector3(0.7f, 0.32f, 0.8f), mat);
            var barrel = AddPart(h, PrimitiveType.Cylinder, "Barrel",
                new Vector3(0f, 1.02f, 0.85f), new Vector3(0.1f, 0.75f, 0.1f), mat);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var antenna = AddPart(h, PrimitiveType.Cylinder, "Antenna",
                new Vector3(-0.35f, 1.55f, -0.6f), new Vector3(0.03f, 0.45f, 0.03f), mat);
            antenna.transform.localRotation = Quaternion.Euler(0f, 0f, 8f);
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
            var crateMat = CreateMaterial("Crate", Color.white); // tinted at runtime

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

        // ------------------------------------------------------- network manager

        public static GameObject BuildNetworkManagerPrefab(GameObject tankPrefab,
            GameObject bulletPrefab, GameObject pickupPrefab)
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
            bool cone, bool loop = false, float rate = 0f)
        {
            var go = new GameObject(name, typeof(ParticleSystem));
            go.transform.SetParent(parent.transform, false);
            var ps = go.GetComponent<ParticleSystem>();

            var main = ps.main;
            main.loop = loop;
            main.playOnAwake = false;
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

            var psStop = ps; // returned for callers that reposition/parent it
            return psStop;
        }
    }
}
