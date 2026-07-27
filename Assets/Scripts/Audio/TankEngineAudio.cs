using UnityEngine;
using TankBattle.Core;
using TankBattle.Gameplay;

namespace TankBattle.Audio
{
    /// <summary>
    /// Gives every tank an engine you can hear. Until now tanks slid around in
    /// total silence, which is the single biggest reason the game felt lifeless.
    ///
    /// Three looping layers, all synthesized at runtime (no audio assets):
    ///   idle    a low rumble that is always there while the tank is alive
    ///   drive   a higher harmonic that fades in and pitches up with throttle
    ///   track   metallic squeal that only appears while turning or moving fast
    ///
    /// It runs on EVERY peer and measures the tank's actual replicated movement,
    /// so enemy tanks rumble past you correctly without any extra networking.
    /// Volume falls off with distance from the camera by hand (same reasoning as
    /// AudioManager: Unity's 3D rolloff made everything inaudible on a phone).
    /// </summary>
    [RequireComponent(typeof(TankController))]
    public class TankEngineAudio : MonoBehaviour
    {
        const float MaxAudible = 55f;    // metres
        const float FullVolume = 10f;    // your own tank is always at full

        AudioSource _idle, _drive, _track;
        TankHealth _health;
        Vector3 _lastPos;
        float _lastYaw;
        float _speed01, _turn01;

        static AudioClip _idleClip, _driveClip, _trackClip;

        void Awake()
        {
            _health = GetComponent<TankHealth>();
            _lastPos = transform.position;
            _lastYaw = transform.eulerAngles.y;

            EnsureClips();

            _idle = MakeSource(_idleClip, 0.0f);
            _drive = MakeSource(_driveClip, 0.0f);
            _track = MakeSource(_trackClip, 0.0f);
        }

        AudioSource MakeSource(AudioClip clip, float volume)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = volume;
            src.spatialBlend = 0f;      // manual distance handling, see Update
            src.dopplerLevel = 0f;
            src.Play();
            return src;
        }

        void Update()
        {
            // --- measure how this tank is actually moving, on every peer ---
            Vector3 delta = transform.position - _lastPos;
            _lastPos = transform.position;
            delta.y = 0f;
            float speed = Time.deltaTime > 0.0001f ? delta.magnitude / Time.deltaTime : 0f;

            float yaw = transform.eulerAngles.y;
            float turnRate = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) /
                             Mathf.Max(0.0001f, Time.deltaTime);
            _lastYaw = yaw;

            // Smooth so the engine does not chatter frame to frame.
            _speed01 = Mathf.Lerp(_speed01, Mathf.Clamp01(speed / 9f),
                                  1f - Mathf.Exp(-8f * Time.deltaTime));
            _turn01 = Mathf.Lerp(_turn01, Mathf.Clamp01(turnRate / 90f),
                                 1f - Mathf.Exp(-8f * Time.deltaTime));

            bool dead = _health != null && _health.IsDead.Value;
            bool soundOn = SettingsManager.SfxOn;

            // --- distance attenuation, done by hand ---
            float atten = 1f;
            float pan = 0f;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 to = transform.position - cam.transform.position;
                float dist = to.magnitude;
                if (dist > MaxAudible) atten = 0f;
                else if (dist > FullVolume)
                    atten = 1f - Mathf.SmoothStep(0f, 1f,
                        (dist - FullVolume) / (MaxAudible - FullVolume));
                pan = Mathf.Clamp(Vector3.Dot(to.normalized, cam.transform.right), -1f, 1f) * 0.6f;
            }

            if (dead || !soundOn) atten = 0f;

            // --- idle: always running, slightly louder when stationary ---
            _idle.volume = atten * Mathf.Lerp(0.34f, 0.20f, _speed01);
            _idle.pitch = Mathf.Lerp(0.94f, 1.06f, _speed01);
            _idle.panStereo = pan;

            // --- drive: the "working hard" layer ---
            _drive.volume = atten * _speed01 * 0.42f;
            _drive.pitch = Mathf.Lerp(0.8f, 1.55f, _speed01);
            _drive.panStereo = pan;

            // --- track squeal: turning, or moving at speed ---
            float trackAmount = Mathf.Max(_turn01, _speed01 * 0.45f);
            _track.volume = atten * trackAmount * 0.22f;
            _track.pitch = Mathf.Lerp(0.85f, 1.25f, trackAmount);
            _track.panStereo = pan;
        }

        // --------------------------------------------------------- synthesis

        static void EnsureClips()
        {
            if (_idleClip != null) return;
            const int rate = 44100;

            // IDLE: slow diesel thump - a low saw plus its octave, with a
            // periodic amplitude wobble that reads as cylinders firing.
            _idleClip = Loop("engineIdle", rate, 1.0f, t =>
            {
                float thump = Mathf.Sin(2f * Mathf.PI * 34f * t);
                float saw = Mathf.Repeat(t * 68f, 1f) * 2f - 1f;
                float sub = Mathf.Sin(2f * Mathf.PI * 17f * t);
                float wobble = 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * 8.5f * t);
                return (thump * 0.5f + saw * 0.25f + sub * 0.4f) * wobble;
            });

            // DRIVE: brighter harmonic stack that sounds like it is under load.
            _driveClip = Loop("engineDrive", rate, 1.0f, t =>
            {
                float a = Mathf.Repeat(t * 96f, 1f) * 2f - 1f;      // saw
                float b = Mathf.Sin(2f * Mathf.PI * 192f * t) * 0.4f;
                float c = Mathf.Sin(2f * Mathf.PI * 288f * t) * 0.18f;
                float grit = (Mathf.PerlinNoise(t * 260f, 0f) - 0.5f) * 0.5f;
                return a * 0.55f + b + c + grit;
            });

            // TRACK: metallic band-passed noise with a rhythmic clank.
            _trackClip = Loop("trackSqueal", rate, 1.0f, t =>
            {
                float noise = Mathf.PerlinNoise(t * 1400f, 3.7f) - 0.5f;
                float ring = Mathf.Sin(2f * Mathf.PI * 640f * t) * 0.35f;
                float clank = Mathf.Repeat(t * 11f, 1f) < 0.12f ? 0.55f : 0f;
                return (noise * 1.6f + ring) * (0.5f + clank);
            });
        }

        /// <summary>
        /// Build a seamless looping clip. The final 20 ms is cross-faded into
        /// the start so the loop point is inaudible - without this you hear a
        /// click every second, which is worse than having no engine at all.
        /// </summary>
        static AudioClip Loop(string name, int rate, float seconds,
                              System.Func<float, float> gen)
        {
            int samples = Mathf.CeilToInt(seconds * rate);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
                data[i] = Mathf.Clamp(gen(i / (float)rate), -1f, 1f);

            int fade = Mathf.Min(rate / 50, samples / 4);   // ~20 ms
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                int tail = samples - fade + i;
                data[tail] = Mathf.Lerp(data[tail], data[i], k);
            }

            // Normalise so every layer sits at a predictable level.
            float peak = 0f;
            for (int i = 0; i < samples; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            if (peak > 0.001f)
                for (int i = 0; i < samples; i++) data[i] = data[i] / peak * 0.9f;

            var clip = AudioClip.Create(name, samples, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
