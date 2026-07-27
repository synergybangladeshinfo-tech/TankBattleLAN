using System.Collections.Generic;
using UnityEngine;

namespace TankBattle.Gameplay
{
    /// <summary>
    /// Optional support for real 3D tank models (e.g. downloaded from Meshy).
    ///
    /// HOW TO ADD A MODEL
    ///   1. Download the tank as .fbx or .glb.
    ///   2. Drop it into  Assets/Resources/TankModels/
    ///   3. That's it - the game finds it automatically and it shows up in the
    ///      Garage as an extra body style. No code or scene changes needed.
    ///
    /// The loader is deliberately forgiving: it re-scales whatever it finds to a
    /// sensible tank size, drops physics colliders (the CharacterController on
    /// the tank root already handles collision), looks for a child that seems to
    /// be a turret so aiming still works, and tints the largest material with
    /// the player's colour. If the folder is empty - which it is by default -
    /// nothing changes and the built-in procedural hulls are used exactly as
    /// before, so the game always builds and runs.
    /// </summary>
    public static class TankModelLibrary
    {
        public const string ResourceFolder = "TankModels";

        /// <summary>Target length (metres) every imported model is scaled to.</summary>
        const float TargetLength = 4.2f;

        static GameObject[] _prefabs;
        static string[] _names;

        /// <summary>Imported models, loaded once. Empty array when none exist.</summary>
        public static GameObject[] Prefabs
        {
            get { EnsureLoaded(); return _prefabs; }
        }

        /// <summary>Display names for the Garage, index-aligned with Prefabs.</summary>
        public static string[] Names
        {
            get { EnsureLoaded(); return _names; }
        }

        public static int Count => Prefabs.Length;
        public static bool HasModels => Count > 0;

        static void EnsureLoaded()
        {
            if (_prefabs != null) return;

            var found = Resources.LoadAll<GameObject>(ResourceFolder);
            if (found == null) found = new GameObject[0];

            // Stable alphabetical order so every device shows the same list.
            var list = new List<GameObject>(found);
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));

            _prefabs = list.ToArray();
            _names = new string[_prefabs.Length];
            for (int i = 0; i < _prefabs.Length; i++)
                _names[i] = Prettify(_prefabs[i].name);
        }

        /// <summary>"heavy_tank_01" -> "HEAVY TANK 01".</summary>
        static string Prettify(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "TANK";
            string s = raw.Replace('_', ' ').Replace('-', ' ').Trim();
            return s.ToUpperInvariant();
        }

        /// <summary>
        /// Instantiate model 'index' under 'parent', normalised for the game:
        /// scaled to tank size, sat on the ground, colliders stripped, and with
        /// the turret transform returned when one can be identified.
        /// Returns null if the index is out of range.
        /// </summary>
        public static GameObject Spawn(int index, Transform parent, out Transform turret)
        {
            turret = null;
            EnsureLoaded();
            if (index < 0 || index >= _prefabs.Length) return null;

            var go = Object.Instantiate(_prefabs[index], parent);
            go.name = $"ImportedHull_{index}";
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // --- strip colliders: the tank root owns collision ---
            foreach (var col in go.GetComponentsInChildren<Collider>(true))
                Object.Destroy(col);

            // Inherit the parent's layer. This matters for the Garage preview,
            // which renders one specific layer into a RenderTexture - without
            // it an imported model simply would not show up in the preview.
            SetLayerRecursively(go, parent.gameObject.layer);

            // --- scale to a consistent size and sit it on the ground ---
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);

                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (longest > 0.0001f)
                {
                    float scale = TargetLength / longest;
                    go.transform.localScale = Vector3.one * scale;

                    // Re-measure after scaling so we can drop it onto y = 0.
                    Bounds b2 = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++) b2.Encapsulate(renderers[i].bounds);
                    float bottomOffset = b2.min.y - parent.position.y;
                    go.transform.localPosition = new Vector3(0f, -bottomOffset, 0f);
                }
            }

            turret = FindTurret(go.transform);
            return go;
        }

        /// <summary>
        /// Best-effort turret detection: a child whose name mentions turret /
        /// tower / head / barrel / gun. Returns null when the model is a single
        /// welded mesh, in which case the tank just aims with its whole body.
        /// </summary>
        static Transform FindTurret(Transform root)
        {
            string[] hints = { "turret", "tower", "head", "cannon", "barrel", "gun", "weapon" };
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string n = t.name.ToLowerInvariant();
                for (int i = 0; i < hints.Length; i++)
                    if (n.Contains(hints[i])) return t;
            }
            return null;
        }

        static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        /// <summary>Tint every material on an imported model with the player colour.</summary>
        public static void Tint(GameObject model, Color color)
        {
            if (model == null) return;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.materials;              // instance copies
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    // Keep the model's own texture detail, just push it toward
                    // the team/player colour so tanks stay tellable apart.
                    if (mats[i].HasProperty("_Color"))
                        mats[i].color = Color.Lerp(mats[i].color, color, 0.65f);
                }
                r.materials = mats;
            }
        }
    }
}
