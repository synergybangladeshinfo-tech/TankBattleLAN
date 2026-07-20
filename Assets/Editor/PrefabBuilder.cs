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
    /// Builds all runtime prefabs (Tank, Bullet, NetworkManager) and the shared
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

        // ----------------------------------------------------------------- tank

        public static GameObject BuildTankPrefab()
        {
            var tankMat = CreateMaterial("Tank_Base", Color.white);
            var barBg = CreateMaterial("HealthBar_BG", new Color(0.1f, 0.1f, 0.1f), unlit: true);
            var barFill = CreateMaterial("HealthBar_Fill", Color.green, unlit: true);

            var root = new GameObject("Tank");
            try
            {
                // Physics body: a single capsule via CharacterController.
                var cc = root.AddComponent<CharacterController>();
                cc.center = new Vector3(0f, 0.8f, 0f);
                cc.radius = 0.8f;
                cc.height = 1.6f;

                // Low-poly hull from primitives (colliders stripped - the
                // CharacterController capsule is the sole hit volume).
                AddPart(root, PrimitiveType.Cube, "Body",
                    new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 0.55f, 2.1f), tankMat);
                AddPart(root, PrimitiveType.Cube, "TrackL",
                    new Vector3(-0.85f, 0.35f, 0f), new Vector3(0.4f, 0.5f, 2.3f), tankMat);
                AddPart(root, PrimitiveType.Cube, "TrackR",
                    new Vector3(0.85f, 0.35f, 0f), new Vector3(0.4f, 0.5f, 2.3f), tankMat);
                AddPart(root, PrimitiveType.Cube, "Turret",
                    new Vector3(0f, 1.05f, -0.1f), new Vector3(0.9f, 0.4f, 1.0f), tankMat);
                var barrel = AddPart(root, PrimitiveType.Cylinder, "Barrel",
                    new Vector3(0f, 1.05f, 0.95f), new Vector3(0.14f, 0.55f, 0.14f), tankMat);
                barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

                // Muzzle: bullet spawn point just past the barrel tip.
                var muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = new Vector3(0f, 1.05f, 1.8f);

                // Overhead health bar (billboarded quads).
                var barPivot = new GameObject("HealthBarPivot").transform;
                barPivot.SetParent(root.transform, false);
                barPivot.localPosition = new Vector3(0f, 2.2f, 0f);
                barPivot.localScale = new Vector3(1.6f, 0.22f, 1f);
                barPivot.gameObject.AddComponent<Billboard>();

                var bg = AddQuad(barPivot, "BG", new Vector3(0f, 0f, 0.02f), Vector3.one, barBg);
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

        // --------------------------------------------------------------- bullet

        public static GameObject BuildBulletPrefab()
        {
            var mat = CreateMaterial("Bullet", new Color(1f, 0.85f, 0.2f), unlit: true);

            var root = new GameObject("Bullet");
            try
            {
                // Visual only - hit detection is a server-side spherecast, and
                // having no collider means bullets never block each other.
                var visual = AddPart(root, PrimitiveType.Sphere, "Visual",
                    Vector3.zero, Vector3.one * 0.35f, mat);

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

        // ------------------------------------------------------- network manager

        public static GameObject BuildNetworkManagerPrefab(GameObject tankPrefab, GameObject bulletPrefab)
        {
            var root = new GameObject("NetworkManager");
            try
            {
                var nm = root.AddComponent<NetworkManager>();
                var utp = root.AddComponent<UnityTransport>();
                nm.NetworkConfig.NetworkTransport = utp;
                nm.NetworkConfig.EnableSceneManagement = true; // host drives map loads
                nm.NetworkConfig.ConnectionApproval = true;    // player cap + names
                nm.NetworkConfig.TickRate = 30;                // plenty for 4 tanks

                var cm = root.AddComponent<ConnectionManager>();
                root.AddComponent<LanDiscovery>();

                // Wire the private serialized prefab references.
                var so = new SerializedObject(cm);
                so.FindProperty("tankPrefab").objectReferenceValue = tankPrefab;
                so.FindProperty("bulletPrefab").objectReferenceValue = bulletPrefab;
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
    }
}
