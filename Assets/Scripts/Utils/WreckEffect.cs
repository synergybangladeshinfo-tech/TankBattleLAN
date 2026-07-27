using UnityEngine;

namespace TankBattle.Utils
{
    /// <summary>
    /// The "your tank just died" show, played locally on every client when the
    /// replicated IsDead flag flips. Nothing here is networked - each peer runs
    /// the same routine off the same event, which keeps it cheap and avoids
    /// sending physics over the wire.
    ///
    /// Sequence: the turret is blown clear with real physics, the burnt hull
    /// slumps and tilts, chunks of armour scatter, a scorch mark is burned into
    /// the ground and a column of black smoke rises for a few seconds.
    /// </summary>
    public static class WreckEffect
    {
        /// <summary>
        /// Build a wreck at the dead tank's position. 'tank' is only read from
        /// (to copy its meshes) - it is never modified.
        /// </summary>
        public static void Spawn(Transform tank, Color tint)
        {
            if (tank == null) return;

            Vector3 pos = tank.position;
            Quaternion rot = tank.rotation;

            // Ground scorch under the wreck.
            ImpactFx.Scorch(new Vector3(pos.x, pos.y + 0.03f, pos.z), Vector3.up, 5.5f);
            ImpactFx.Debris(pos + Vector3.up * 0.6f, Vector3.up, 10, 4.5f, 0.24f, tint * 0.45f);

            var root = new GameObject("TankWreck");
            root.transform.SetPositionAndRotation(pos, rot);
            Object.Destroy(root, 9f);

            // ---- burnt hull: a slumped, darkened copy of the tank body ----
            var hull = BuildChunk(root.transform, new Vector3(0f, 0.55f, 0f),
                new Vector3(2.1f, 0.75f, 3.1f), tint * 0.30f, addRigidbody: true);
            var hullRb = hull.GetComponent<Rigidbody>();
            hullRb.mass = 40f;
            // A shove sideways plus spin makes it settle at a wrecked angle.
            hullRb.AddForce(new Vector3(Random.Range(-1.4f, 1.4f), 2.6f,
                                        Random.Range(-1.4f, 1.4f)), ForceMode.VelocityChange);
            hullRb.AddTorque(new Vector3(Random.Range(-2.2f, 2.2f), Random.Range(-1f, 1f),
                                         Random.Range(-2.2f, 2.2f)), ForceMode.VelocityChange);

            // ---- turret: launched clear, tumbling ----
            var turret = BuildChunk(root.transform, new Vector3(0f, 1.15f, -0.05f),
                new Vector3(1.15f, 0.5f, 1.2f), tint * 0.45f, addRigidbody: true);
            var tRb = turret.GetComponent<Rigidbody>();
            tRb.mass = 8f;
            tRb.AddForce(new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(8f, 12f),
                                     Random.Range(-2.5f, 2.5f)), ForceMode.VelocityChange);
            tRb.AddTorque(Random.insideUnitSphere * 9f, ForceMode.VelocityChange);

            // Barrel welded to the flying turret so it tumbles as one piece.
            var barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "WreckBarrel";
            Object.Destroy(barrel.GetComponent<Collider>());
            barrel.transform.SetParent(turret.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0f, 0.85f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.16f, 0.6f, 0.16f);
            Paint(barrel, new Color(0.16f, 0.16f, 0.18f));

            // ---- fire flash then a rising smoke column ----
            SpawnSmoke(root.transform);
        }

        static GameObject BuildChunk(Transform parent, Vector3 localPos, Vector3 scale,
                                     Color color, bool addRigidbody)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "WreckChunk";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            Paint(go, color);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (addRigidbody)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
                // Detach so the pieces fly independently of the wreck root.
                go.transform.SetParent(null, true);
                Object.Destroy(go, 9f);
            }
            return go;
        }

        static void Paint(GameObject go, Color c)
        {
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mat = new Material(Shader.Find("Standard"));
            mat.color = c;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.08f);
            mr.sharedMaterial = mat;
        }

        /// <summary>Thick black smoke that climbs out of the wreck.</summary>
        static void SpawnSmoke(Transform parent)
        {
            var go = new GameObject("WreckSmoke");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.8f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = 6f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.10f, 0.10f, 0.10f, 0.85f),
                new Color(0.28f, 0.26f, 0.24f, 0.65f));
            main.gravityModifier = -0.06f;   // smoke rises
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 14f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)18) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.7f;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 0.45f);
            curve.AddKey(1f, 1f);
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (shader != null) renderer.material = new Material(shader);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            ps.Play();
        }
    }
}
