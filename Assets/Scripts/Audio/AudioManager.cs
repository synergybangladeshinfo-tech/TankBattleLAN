using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Audio
{
    /// <summary>
    /// Music + SFX player with fully PROCEDURAL audio: every clip is synthesized
    /// at startup (square/sine waves and filtered noise), so the project ships
    /// zero audio assets yet still has background music and effects.
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
        AudioClip _shoot, _hit, _explosion, _click, _victory;

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

        public void PlayShootAt(Vector3 pos) => PlayWorld(_shoot, pos, 0.7f);
        public void PlayHitAt(Vector3 pos) => PlayWorld(_hit, pos, 0.6f);
        public void PlayExplosionAt(Vector3 pos) => PlayWorld(_explosion, pos, 0.9f);

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

            _shoot = Synth("shoot", 0.25f, t =>
            {
                float sweep = Mathf.Lerp(320f, 70f, t / 0.25f);            // falling pitch
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * sweep * t));
                float noise = (Random.value * 2f - 1f) * 0.6f;
                return (square * 0.5f + noise * 0.5f) * Mathf.Exp(-t * 18f);
            });

            _hit = Synth("hit", 0.12f, t =>
                Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 45f));

            // Explosion: decaying low-passed noise rumble.
            _explosion = SynthFiltered("explosion", 0.7f, 0.08f, t =>
                (Random.value * 2f - 1f) * Mathf.Exp(-t * 6f));

            _victory = SynthMelody("victory",
                new float[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.16f, wave: 0);

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
