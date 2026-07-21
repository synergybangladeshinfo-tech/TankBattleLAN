using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Audio
{
    /// <summary>
    /// Music + SFX player with fully PROCEDURAL audio: every clip is synthesized
    /// at startup (layered waves and filtered noise), so the project ships zero
    /// audio assets yet still has background music and rich effects - including
    /// a distinct sound per weapon, pickups, countdown ticks and a two-layer
    /// explosion (low boom + crackle).
    /// Replace any clip with real assets later by assigning the fields.
    /// Persists across scenes; respects the sound settings.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        const int SampleRate = 22050;

        AudioSource _musicSource;
        AudioSource _uiSource;

        AudioClip _menuMusic, _battleMusic;
        AudioClip _hit, _explosion, _click, _victory, _pickup, _tick;
        AudioClip[] _shots; // index-aligned with Weapons.Defs

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.volume = 0.35f;

            _uiSource = gameObject.AddComponent<AudioSource>();
            _uiSource.playOnAwake = false;

            GenerateClips();
            SettingsManager.OnChanged += ApplySettings;
            ApplySettings();
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                SettingsManager.OnChanged -= ApplySettings;
            }
        }

        void ApplySettings()
        {
            _musicSource.mute = !SettingsManager.MusicOn;
        }

        // ------------------------------------------------------------------ play

        public void PlayMenuMusic() => PlayMusic(_menuMusic);
        public void PlayBattleMusic() => PlayMusic(_battleMusic);

        void PlayMusic(AudioClip clip)
        {
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;
            _musicSource.clip = clip;
            _musicSource.Play();
        }

        public void PlayClick() => PlayUi(_click, 0.5f);
        public void PlayVictory() => PlayUi(_victory, 0.8f);
        public void PlayCountdownTick() => PlayUi(_tick, 0.6f);

        /// <summary>Weapon-specific firing sound at a world position.</summary>
        public void PlayShootAt(Vector3 pos, int weaponIndex = 0)
        {
            if (_shots == null || _shots.Length == 0) return;
            if (weaponIndex < 0 || weaponIndex >= _shots.Length) weaponIndex = 0;
            PlayWorld(_shots[weaponIndex], pos, 0.7f);
        }

        public void PlayHitAt(Vector3 pos) => PlayWorld(_hit, pos, 0.6f);
        public void PlayExplosionAt(Vector3 pos) => PlayWorld(_explosion, pos, 0.9f);
        public void PlayPickupAt(Vector3 pos) => PlayWorld(_pickup, pos, 0.8f);

        void PlayUi(AudioClip clip, float volume)
        {
            if (!SettingsManager.SfxOn || clip == null) return;
            _uiSource.PlayOneShot(clip, volume);
        }

        void PlayWorld(AudioClip clip, Vector3 pos, float volume)
        {
            if (!SettingsManager.SfxOn || clip == null) return;
            // 2D-ish playback positioned in the world; cheap and predictable.
            AudioSource.PlayClipAtPoint(clip, pos, volume);
        }

        // ------------------------------------------------------------- synthesis

        void GenerateClips()
        {
            _click = Synth("click", 0.06f, t =>
                Mathf.Sin(2f * Mathf.PI * 1400f * t) * Mathf.Exp(-t * 60f));

            _tick = Synth("tick", 0.09f, t =>
                Mathf.Sin(2f * Mathf.PI * 1000f * t) * Mathf.Exp(-t * 40f));

            // Per-weapon shots: [Cannon, MachineGun, Shotgun, Laser, Rocket].
            _shots = new AudioClip[5];

            _shots[0] = Synth("shotCannon", 0.25f, t =>       // classic thump
            {
                float sweep = Mathf.Lerp(320f, 70f, t / 0.25f);
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * sweep * t));
                float noise = (Random.value * 2f - 1f) * 0.6f;
                return (square * 0.5f + noise * 0.5f) * Mathf.Exp(-t * 18f);
            });

            _shots[1] = Synth("shotMG", 0.09f, t =>           // short snappy tick
            {
                float noise = (Random.value * 2f - 1f);
                float tone = Mathf.Sin(2f * Mathf.PI * 480f * t);
                return (noise * 0.6f + tone * 0.4f) * Mathf.Exp(-t * 55f);
            });

            _shots[2] = SynthFiltered("shotShotgun", 0.30f, 0.25f, t => // wide boom
                (Random.value * 2f - 1f) * Mathf.Exp(-t * 14f));

            _shots[3] = Synth("shotLaser", 0.22f, t =>        // rising sci-fi zap
            {
                float sweep = Mathf.Lerp(700f, 1900f, t / 0.22f);
                return Mathf.Sin(2f * Mathf.PI * sweep * t) * Mathf.Exp(-t * 14f) * 0.8f;
            });

            _shots[4] = SynthFiltered("shotRocket", 0.5f, 0.12f, t =>   // whoosh
            {
                float noise = (Random.value * 2f - 1f);
                float env = Mathf.Sin(Mathf.Clamp01(t / 0.5f) * Mathf.PI); // swell
                return noise * env;
            });

            _hit = Synth("hit", 0.12f, t =>
                Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 45f));

            // Explosion: low-passed rumble + a crackle layer on top.
            _explosion = SynthLayered("explosion", 0.85f,
                (t, prevLp) => 0f, // handled inside SynthLayered
                0.06f);

            _pickup = SynthMelody("pickup",
                new float[] { 659.25f, 830.61f, 987.77f }, 0.07f, wave: 0);

            _victory = SynthMelody("victory",
                new float[] { 523.25f, 659.25f, 783.99f, 1046.5f, 783.99f, 1046.5f }, 0.15f, wave: 0);

            // Menu music: slow, soft arpeggio (sine).
            _menuMusic = SynthMelody("menuMusic", new float[]
            {
                261.63f, 329.63f, 392.00f, 329.63f, 293.66f, 349.23f, 440.00f, 349.23f,
                246.94f, 311.13f, 392.00f, 311.13f, 261.63f, 329.63f, 392.00f, 523.25f
            }, 0.30f, wave: 0);

            // Battle music: faster, punchier square-wave loop.
            _battleMusic = SynthMelody("battleMusic", new float[]
            {
                130.81f, 130.81f, 155.56f, 130.81f, 174.61f, 155.56f, 130.81f, 196.00f,
                130.81f, 130.81f, 155.56f, 130.81f, 116.54f, 123.47f, 130.81f, 98.00f
            }, 0.19f, wave: 1);
        }

        /// <summary>Create a clip from a time-domain generator function.</summary>
        static AudioClip Synth(string name, float duration, System.Func<float, float> gen)
        {
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
                data[i] = Mathf.Clamp(gen(i / (float)SampleRate), -1f, 1f);
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Same as Synth but with a one-pole low-pass filter (for rumble).</summary>
        static AudioClip SynthFiltered(string name, float duration, float alpha,
            System.Func<float, float> gen)
        {
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            float prev = 0f;
            for (int i = 0; i < samples; i++)
            {
                float raw = gen(i / (float)SampleRate);
                prev += alpha * (raw - prev); // low-pass
                data[i] = Mathf.Clamp(prev * 2.5f, -1f, 1f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Two-layer explosion: deep filtered boom + bright crackle.</summary>
        static AudioClip SynthLayered(string name, float duration,
            System.Func<float, float, float> _unused, float alpha)
        {
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;

                // Layer 1: low rumble (heavily low-passed noise, slow decay).
                float rumbleRaw = (Random.value * 2f - 1f) * Mathf.Exp(-t * 5f);
                lp += alpha * (rumbleRaw - lp);
                float rumble = lp * 3.0f;

                // Layer 2: crackle (sparse bright pops, fast decay).
                float crackle = 0f;
                if (Random.value < 0.06f)
                    crackle = (Random.value * 2f - 1f) * Mathf.Exp(-t * 9f) * 0.7f;

                data[i] = Mathf.Clamp(rumble + crackle, -1f, 1f);
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Simple sequenced melody. wave 0 = sine (soft), 1 = square (chippy).
        /// Each note gets a short attack/decay envelope to avoid clicks.
        /// </summary>
        static AudioClip SynthMelody(string name, float[] freqs, float noteLen, int wave)
        {
            int noteSamples = Mathf.CeilToInt(noteLen * SampleRate);
            int total = noteSamples * freqs.Length;
            var data = new float[total];
            for (int n = 0; n < freqs.Length; n++)
            {
                float f = freqs[n];
                for (int i = 0; i < noteSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float envAttack = Mathf.Clamp01(i / (SampleRate * 0.01f));
                    float envRelease = Mathf.Clamp01((noteSamples - i) / (SampleRate * 0.05f));
                    float phase = 2f * Mathf.PI * f * t;
                    float s = wave == 0
                        ? Mathf.Sin(phase)
                        : Mathf.Sign(Mathf.Sin(phase)) * 0.35f; // quieter square
                    data[n * noteSamples + i] = s * 0.5f * envAttack * envRelease;
                }
            }
            var clip = AudioClip.Create(name, total, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
