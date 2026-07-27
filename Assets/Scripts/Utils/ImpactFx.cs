using System.Collections.Generic;
using UnityEngine;

namespace TankBattle.Utils
{
    /// <summary>
    /// Client-side hit feedback: scorch decals, sparks, dust puffs and flying
    /// debris chunks. Everything is generated in code and pooled, so there are
    /// no art assets and no per-hit allocations.
    ///
    /// This is purely cosmetic - it never touches gameplay state - so it runs
    /// locally on every client off replicated events (a bullet impact RPC, a
    /// tank dying, a barrel exploding) instead of being networked itself.
    /// </summary>
    public static class ImpactFx
    {
        const int MaxDecals = 48;      // oldest decal is recycled beyond this
        const int MaxDebris = 90;

        static readonly List<GameObject> _decals = new List<GameObject>();
        static readonly List<Rigidbody> _debris = new List<Rigidbody>();
        static Transform _root;
        static Material _scorchMat, _debrisMat;
        static Mesh _quadMesh, _cubeMesh;

        static void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("~ImpactFx");
            Object.DontDestroyOnLoad(go);
            _root = go.transform;
        }

        /// <summary>Soft dark blob used for scorch marks and craters.</summary>
        static Material ScorchMaterial()
        {
            if (_scorchMat != null) return _scorchMat;

            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x - S * 0.5f) / (S * 0.5f);
                    float dy = (y - S * 0.5f) / (S * 0.5f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    // Ragged edge so scorch marks do not look like perfect circles.
                    float noise = Mathf.PerlinNoise(x * 0.28f, y * 0.28f) * 0.32f;
                    float a = Mathf.Clamp01(1f - (d + noise));
                    a = Mathf.Pow(a, 1.6f);
                    px[y * S + x] = new Color(0.05f, 0.045f, 0.04f, a * 0.9f);
                }
            tex.SetPixels32(px);
            tex.Apply();

            // Transparent unlit: decals must not react to lighting or they
            // "pop" against the surface they are lying on.
            var shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _scorchMat = new Material(shader) { mainTexture = tex };
            _scorchMat.color = Color.white;
            return _scorchMat;
        }

        static Material DebrisMaterial()
        {
            if (_debrisMat != null) return _debrisMat;
            var shader = Shader.Find("Standard");
            _debrisMat = new Material(shader);
            _debrisMat.color = new Color(0.22f, 0.20f, 0.18f);
            if (_debrisMat.HasProperty("_Glossiness")) _debrisMat.SetFloat("_Glossiness", 0.1f);
            return _debrisMat;
        }

        static Mesh QuadMesh()
        {
            if (_quadMesh != null) return _quadMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quadMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            return _quadMesh;
        }

        static Mesh CubeMesh()
        {
            if (_cubeMesh != null) return _cubeMesh;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(tmp);
            return _cubeMesh;
        }

        // ------------------------------------------------------------- decals

        /// <summary>
        /// Stick a scorch mark flat against a surface. 'normal' is the surface
        /// direction; pass Vector3.up for ground marks.
        /// </summary>
        public static void Scorch(Vector3 position, Vector3 normal, float size)
        {
            EnsureRoot();

            GameObject go;
            if (_decals.Count >= MaxDecals)
            {
                go = _decals[0];
                _decals.RemoveAt(0);
            }
            else
            {
                go = new GameObject("Scorch");
                go.transform.SetParent(_root, false);
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = QuadMesh();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ScorchMaterial();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // Lift very slightly off the surface to avoid z-fighting.
            go.transform.position = position + normal * 0.02f;
            go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up) *
                                    Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * size;
            go.SetActive(true);
            _decals.Add(go);
        }

        // ------------------------------------------------------------- debris

        /// <summary>Throw a handful of physics chunks out of an impact point.</summary>
        public static void Debris(Vector3 position, Vector3 normal, int count,
                                  float force, float scale, Color? tint = null)
        {
            EnsureRoot();
            for (int i = 0; i < count; i++)
            {
                Rigidbody rb;
                if (_debris.Count >= MaxDebris)
                {
                    rb = _debris[0];
                    _debris.RemoveAt(0);
                    rb.gameObject.SetActive(true);
                }
                else
                {
                    var go = new GameObject("Debris");
                    go.transform.SetParent(_root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = CubeMesh();
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = DebrisMaterial();
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.AddComponent<BoxCollider>();
                    rb = go.AddComponent<Rigidbody>();
                    rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                    go.AddComponent<DebrisFade>();
                }

                if (tint.HasValue)
                {
                    var mr = rb.GetComponent<MeshRenderer>();
                    mr.material.color = tint.Value;
                }

                float s = scale * Random.Range(0.5f, 1.35f);
                rb.transform.localScale = new Vector3(s, s * Random.Range(0.4f, 1f), s);
                rb.transform.position = position + Random.insideUnitSphere * 0.25f;
                rb.transform.rotation = Random.rotation;

                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Vector3 dir = (normal + Random.insideUnitSphere * 0.85f).normalized;
                rb.AddForce(dir * force * Random.Range(0.6f, 1.4f), ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * force * 0.35f, ForceMode.Impulse);

                rb.GetComponent<DebrisFade>().Restart(Random.Range(3.5f, 6f));
                _debris.Add(rb);
            }
        }

        // -------------------------------------------------------- combined fx

        /// <summary>Small bullet hit: spark debris + a little scorch mark.</summary>
        public static void BulletHit(Vector3 position, Vector3 normal, Color tint)
        {
            Scorch(position, normal, Random.Range(0.5f, 0.85f));
            Debris(position, normal, 4, 2.2f, 0.10f, tint * 0.6f);
        }

        /// <summary>Big blast: wide scorch, lots of chunks.</summary>
        public static void Explosion(Vector3 position, float radius)
        {
            Scorch(position + Vector3.up * 0.02f, Vector3.up, radius * 1.5f);
            Debris(position, Vector3.up, 12, 5.5f, 0.22f);
        }
    }

    /// <summary>Shrinks a debris chunk away, then parks it for reuse.</summary>
    public class DebrisFade : MonoBehaviour
    {
        float _dieAt;
        float _life;
        Vector3 _startScale;

        public void Restart(float life)
        {
            _life = life;
            _dieAt = Time.time + life;
            _startScale = transform.localScale;
            enabled = true;
        }

        void Update()
        {
            float left = _dieAt - Time.time;
            if (left <= 0f)
            {
                gameObject.SetActive(false);
                enabled = false;
                return;
            }
            // Only shrink over the final second so chunks sit around a while.
            if (left < 1f) transform.localScale = _startScale * Mathf.Clamp01(left);
            _ = _life;
        }
    }
}
