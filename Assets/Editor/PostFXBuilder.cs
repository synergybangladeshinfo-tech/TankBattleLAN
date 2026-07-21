using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace TankBattle.EditorTools
{
    /// <summary>
    /// Builds the shared post-processing profile (Bloom glow, colour grading,
    /// ambient occlusion, vignette, chromatic aberration, grain) once, and adds
    /// a configured PostProcessLayer to a camera + a global PostProcessVolume to
    /// a scene. This is the single biggest visual upgrade - it gives the game a
    /// modern, cinematic, "glowy" look on top of the primitive geometry.
    ///
    /// Uses the built-in render pipeline's Post-Processing v2 package, wired
    /// entirely from editor code so no manual inspector setup is needed.
    /// </summary>
    public static class PostFXBuilder
    {
        const string ProfilePath = "Assets/PostFX/TankBattle_PostFX.asset";
        const string ResourcesPath =
            "Packages/com.unity.postprocessing/PostProcessResources.asset";

        static PostProcessProfile _profile;

        /// <summary>Create (or load) the shared profile. Called once per generation.</summary>
        public static PostProcessProfile BuildProfile()
        {
            if (!AssetDatabase.IsValidFolder("Assets/PostFX"))
                AssetDatabase.CreateFolder("Assets", "PostFX");

            var profile = AssetDatabase.LoadAssetAtPath<PostProcessProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<PostProcessProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                // Clear any previous settings so re-runs stay clean.
                for (int i = profile.settings.Count - 1; i >= 0; i--)
                    Object.DestroyImmediate(profile.settings[i], true);
                profile.settings.Clear();
            }

            // --- Bloom: soft glow on bright pixels (muzzle flashes, sky, metal) ---
            var bloom = profile.AddSettings<Bloom>();
            bloom.enabled.Override(true);
            bloom.intensity.Override(3.2f);
            bloom.threshold.Override(1.05f);
            bloom.softKnee.Override(0.6f);
            bloom.diffusion.Override(7f);
            bloom.fastMode.Override(true); // cheaper on mobile

            // --- Colour grading: warmer, punchier, filmic contrast ---
            var grading = profile.AddSettings<ColorGrading>();
            grading.enabled.Override(true);
            grading.gradingMode.Override(GradingMode.LowDefinitionRange);
            grading.temperature.Override(8f);
            grading.saturation.Override(14f);
            grading.contrast.Override(12f);
            grading.postExposure.Override(0.25f);

            // --- Ambient occlusion: soft contact shadows where shapes meet ---
            var ao = profile.AddSettings<AmbientOcclusion>();
            ao.enabled.Override(true);
            ao.mode.Override(AmbientOcclusionMode.ScalableAmbientObscurance);
            ao.intensity.Override(0.75f);
            ao.radius.Override(4f);

            // --- Vignette: gentle dark corners (cinematic framing) ---
            var vignette = profile.AddSettings<Vignette>();
            vignette.enabled.Override(true);
            vignette.intensity.Override(0.32f);
            vignette.smoothness.Override(0.4f);

            // --- Subtle film grain + chromatic aberration for realism ---
            var grain = profile.AddSettings<Grain>();
            grain.enabled.Override(true);
            grain.intensity.Override(0.12f);
            grain.size.Override(1.1f);

            var chroma = profile.AddSettings<ChromaticAberration>();
            chroma.enabled.Override(true);
            chroma.intensity.Override(0.12f);

            EditorUtility.SetDirty(profile);
            _profile = profile;
            return profile;
        }

        /// <summary>
        /// Attach the profile as a global volume and wire the camera to render
        /// it. Both go on the "TransparentFX" layer (index 1, always present)
        /// so the volume layer mask is deterministic without new layers.
        /// </summary>
        public static void ApplyToScene(Camera camera)
        {
            if (_profile == null) BuildProfile();

            camera.allowHDR = true;   // bloom needs HDR to look right
            camera.allowMSAA = true;

            int fxLayer = LayerMask.NameToLayer("TransparentFX"); // built-in, index 1
            if (fxLayer < 0) fxLayer = 0;

            // Global volume holding the profile.
            var volGo = new GameObject("PostFXVolume");
            volGo.layer = fxLayer;
            var vol = volGo.AddComponent<PostProcessVolume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = _profile;

            // Camera layer that renders the volume.
            var layer = camera.gameObject.AddComponent<PostProcessLayer>();
            var resources = AssetDatabase.LoadAssetAtPath<PostProcessResources>(ResourcesPath);
            if (resources != null) layer.Init(resources);
            layer.volumeTrigger = camera.transform;
            layer.volumeLayer = 1 << fxLayer;
            layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
        }
    }
}
