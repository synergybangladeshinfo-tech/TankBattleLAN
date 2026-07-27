using UnityEngine;
using TankBattle.Core;

namespace TankBattle.Audio
{
    /// <summary>
    /// Music + SFX player with fully PROCEDURAL audio: every clip is synthesized
    /// at startup (layered waves and filtered noise), so the project ships zero
    /// audio assets yet still has music and punchy effects - a distinct sound per
    /// weapon, pickups, countdown ticks and a two-layer explosion.
    ///
    /// Playback uses a small pool of 2D AudioSources with MANUAL distance
    /// attenuation and stereo panning instead of Unity's 3D rolloff. That matters:
    /// the chase camera sits ~9 m behind the tank, and Unity's default logarithmic
    /// 3D rolloff made your own gunfire almost silent on a phone speaker. Doing the
    /// falloff by hand keeps your own shots loud and punchy while distant fire
    /// still fades out naturally.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        /// <summary>44.1 kHz: 22 kHz made every effect sound muffled and cheap.</summary>
        const int SampleRate = 44100;

        /// <summary>Beyond this many metres a sound is inaudible.</summary>
        const float MaxAudibleDistance = 70f;

        /// <summary>Sounds closer than this play at full volume (your own tank).</summary>
        const float FullVolumeDistance = 14f;

        const int SfxVoices = 14;

        AudioSource _musicSource;
        AudioSource _uiSource;
        AudioSource[] _sfxPool;
        int _nextVoice;

        AudioClip _menuMusic, _battleMusic;
        AudioClip _hit, _explosion, _click, _victory, _pickup, _tick, _whiz;
        AudioClip[] _shots; // index-aligned with Weapons.Defs

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _musicSource = gameObject.AddComponent<AudioSource>();
            _musicSource.loop = true;
            _musicSource.playOnAwake = false;
            _musicSource.volume = 0.28f;   // sits under the effects
            _musicSource.spatialBlend = 0f;

            _uiSource = gameObject.AddComponent<AudioSource>();
            _uiSource.playOnAwake = false;
            _uiSource.spatialBlend = 0f;

            // Voice pool: plain 2D sources we drive ourselves.
            _sfxPool = new AudioSource[SfxVoices];
            for (int i = 0; i < SfxVoices; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;      // 2D - we do the falloff manually
                src.dopplerLevel = 0f;
                _sfxPool[i] = src;
            }

            GenerateClips();
            LoadRealMusic();
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
            if (clip == null) return;
            if (_musicSource.clip == clip && _musicSource.isPlaying) return;

            // Real recordings are already mastered loud; the synthesized loops
            // are quiet by design, so each gets its own level.
            bool real = clip == _menuMusic ? _menuIsReal
                      : clip == _battleMusic ? _battleIsReal : false;
            _musicSource.volume = real ? 0.42f : 0.28f;

            _musicSource.clip = clip;
            _musicSource.Play();
        }

        public void PlayClick() => PlayUi(_click, 0.55f);
        public void PlayVictory() => PlayUi(_victory, 0.85f);
        public void PlayCountdownTick() => PlayUi(_tick, 0.7f);

        /// <summary>Weapon-specific firing sound at a world position.</summary>
        public void PlayShootAt(Vector3 pos, int weaponIndex = 0)
        {
            if (_shots == null || _shots.Length == 0) return;
            if (weaponIndex < 0 || weaponIndex >= _shots.Length) weaponIndex = 0;

            // Slight pitch variation stops repeated shots sounding like a machine.
            PlayWorld(_shots[weaponIndex], pos, 1.0f, Random.Range(0.94f, 1.07f));
        }

        public void PlayHitAt(Vector3 pos) => PlayWorld(_hit, pos, 0.75f, Random.Range(0.9f, 1.1f));

        /// <summary>
        /// Enemy round passing close by. Deliberately NOT distance-attenuated
        /// the usual way - a near miss should be startling, so it plays loud
        /// and panned to whichever side it went past.
        /// </summary>
        public void PlayWhizAt(Vector3 pos)
        {
            if (!SettingsManager.SfxOn || _whiz == null || _sfxPool == null) return;
            var cam = Camera.main;
            float pan = 0f;
            if (cam != null)
                pan = Mathf.Clamp(Vector3.Dot((pos - cam.transform.position).normalized,
                                              cam.transform.right), -1f, 1f) * 0.9f;

            var src = _sfxPool[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _sfxPool.Length;
            src.Stop();
            src.clip = _whiz;
            src.pitch = Random.Range(0.9f, 1.2f);
            src.panStereo = pan;
            src.volume = 0.55f;
            src.Play();
        }
        public void PlayExplosionAt(Vector3 pos) => PlayWorld(_explosion, pos, 1.0f, Random.Range(0.9f, 1.05f));
        public void PlayPickupAt(Vector3 pos) => PlayWorld(_pickup, pos, 0.85f);

        void PlayUi(AudioClip clip, float volume)
        {
            if (!SettingsManager.SfxOn || clip == null) return;
            _uiSource.PlayOneShot(clip, volume);
        }

        /// <summary>
        /// Play a clip positioned in the world through the 2D voice pool, with
        /// hand-rolled distance attenuation and stereo panning relative to the
        /// listener (the player's camera).
        /// </summary>
        void PlayWorld(AudioClip clip, Vector3 pos, float volume, float pitch = 1f)
        {
            if (!SettingsManager.SfxOn || clip == null || _sfxPool == null) return;

            var cam = Camera.main;
            float attenuation = 1f;
            float pan = 0f;

            if (cam != null)
            {
                Vector3 toSound = pos - cam.transform.position;
                float dist = toSound.magnitude;
                if (dist > MaxAudibleDistance) return;   // too far to bother

                // Full volume up close, then a smooth curve out to silence.
                attenuation = dist <= FullVolumeDistance
                    ? 1f
                    : 1f - Mathf.SmoothStep(0f, 1f,
                        (dist - FullVolumeDistance) / (MaxAudibleDistance - FullVolumeDistance));

                // Pan toward whichever side of the screen the sound came from.
                pan = Mathf.Clamp(Vector3.Dot(toSound.normalized, cam.transform.right), -1f, 1f) * 0.75f;
            }

            var src = _sfxPool[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _sfxPool.Length;

            src.Stop();
            src.clip = clip;
            src.pitch = pitch;
            src.panStereo = pan;
            src.volume = Mathf.Clamp01(volume * attenuation);
            src.Play();
        }

        // ----------------------------------------------------------- real music

        /// <summary>
        /// Replace the synthesized menu/battle loops with real tracks if any are
        /// present in Assets/Resources/Music. Drop in "Menu.mp3" and/or
        /// "Battle.mp3" and they are used automatically; if a file is missing the
        /// generated chiptune loop stays, so the game always has music.
        /// </summary>
        void LoadRealMusic()
        {
            var menu = Resources.Load<AudioClip>("Music/Menu");
            if (menu != null) { _menuMusic = menu; _menuIsReal = true; }

            var battle = Resources.Load<AudioClip>("Music/Battle");
            if (battle != null) { _battleMusic = battle; _battleIsReal = true; }
        }

        bool _menuIsReal, _battleIsReal;

        // ------------------------------------------------------------- synthesis

        void GenerateClips()
        {
            _click = Synth("click", 0.05f, t =>
                Mathf.Sin(2f * Mathf.PI * 1500f * t) * Mathf.Exp(-t * 70f));

            _tick = Synth("tick", 0.08f, t =>
                Mathf.Sin(2f * Mathf.PI * 1100f * t) * Mathf.Exp(-t * 45f));

            // ---- weapon shots (index-aligned with WeaponType) ----
            // Cannon, MachineGun, Shotgun, Laser, Rocket, Sniper, Flamethrower, Mine
            _shots = new AudioClip[8];

            // 0 CANNON - hard transient, descending body, short tail. Big and dry.
            _shots[0] = Norm(Synth("shotCannon", 0.34f, t =>
            {
                float crack = (Random.value * 2f - 1f) * Mathf.Exp(-t * 220f);       // click
                float sweep = Mathf.Lerp(420f, 65f, Mathf.Clamp01(t / 0.20f));
                float body = Mathf.Sin(2f * Mathf.PI * sweep * t) * Mathf.Exp(-t * 13f);
                float tail = (Random.value * 2f - 1f) * Mathf.Exp(-t * 26f) * 0.45f;
                return crack * 0.9f + body * 0.85f + tail;
            }));

            // 1 MACHINE GUN - very short, sharp, dry snap.
            _shots[1] = Norm(Synth("shotMG", 0.10f, t =>
            {
                float crack = (Random.value * 2f - 1f) * Mathf.Exp(-t * 130f);
                float body = Mathf.Sin(2f * Mathf.PI * 520f * t) * Mathf.Exp(-t * 60f);
                return crack * 0.85f + body * 0.5f;
            }));

            // 2 SHOTGUN - wide low blast with a long noisy tail.
            _shots[2] = Norm(SynthFiltered("shotShotgun", 0.40f, 0.30f, t =>
            {
                float blast = (Random.value * 2f - 1f) * Mathf.Exp(-t * 11f);
                float thump = Mathf.Sin(2f * Mathf.PI * 95f * t) * Mathf.Exp(-t * 16f) * 0.7f;
                return blast + thump;
            }));

            // 3 LASER - rising sci-fi zap with a metallic ring.
            _shots[3] = Norm(Synth("shotLaser", 0.26f, t =>
            {
                float sweep = Mathf.Lerp(650f, 2100f, Mathf.Clamp01(t / 0.26f));
                float main = Mathf.Sin(2f * Mathf.PI * sweep * t);
                float ring = Mathf.Sin(2f * Mathf.PI * sweep * 1.5f * t) * 0.35f;
                return (main + ring) * Mathf.Exp(-t * 12f);
            }));

            // 4 ROCKET - launch whoosh over a low thump.
            _shots[4] = Norm(SynthFiltered("shotRocket", 0.55f, 0.16f, t =>
            {
                float swell = Mathf.Sin(Mathf.Clamp01(t / 0.55f) * Mathf.PI);
                float whoosh = (Random.value * 2f - 1f) * swell;
                float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 9f) * 0.9f;
                return whoosh + thump;
            }));

            // 5 SNIPER - brutal high crack plus a distant slap-back echo.
            _shots[5] = Norm(Synth("shotSniper", 0.55f, t =>
            {
                float crack = (Random.value * 2f - 1f) * Mathf.Exp(-t * 150f);
                float body = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(900f, 120f,
                    Mathf.Clamp01(t / 0.12f)) * t) * Mathf.Exp(-t * 20f);
                // slap-back: a quieter copy ~150 ms later
                float echo = 0f;
                if (t > 0.15f)
                {
                    float et = t - 0.15f;
                    echo = (Random.value * 2f - 1f) * Mathf.Exp(-et * 24f) * 0.35f;
                }
                return crack + body * 0.8f + echo;
            }));

            // 6 FLAMETHROWER - breathy filtered roar (fires very fast, so keep short).
            _shots[6] = Norm(SynthFiltered("shotFlame", 0.16f, 0.35f, t =>
            {
                float noise = (Random.value * 2f - 1f);
                float env = Mathf.Sin(Mathf.Clamp01(t / 0.16f) * Mathf.PI);
                return noise * env * 0.8f;
            }));

            // 7 MINE - mechanical clunk as it is planted.
            _shots[7] = Norm(Synth("shotMine", 0.20f, t =>
            {
                float clunk = Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 30f);
                float click = (Random.value * 2f - 1f) * Mathf.Exp(-t * 180f) * 0.7f;
                return clunk + click;
            }));

            // Near miss: a fast doppler-ish whoosh past your head.
            _whiz = Norm(Synth("whiz", 0.22f, t =>
            {
                float k = Mathf.Clamp01(t / 0.22f);
                // Sweep down in pitch as it passes, like a real fly-by.
                float freq = Mathf.Lerp(1500f, 420f, k);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * t);
                float air = (Random.value * 2f - 1f) * 0.45f;
                float env = Mathf.Sin(k * Mathf.PI);   // fade in AND out
                return (tone * 0.7f + air) * env;
            }));

            // Bullet impact: short metallic ping.
            _hit = Norm(Synth("hit", 0.14f, t =>
            {
                float ping = Mathf.Sin(2f * Mathf.PI * 950f * t) * Mathf.Exp(-t * 40f);
                float tick = (Random.value * 2f - 1f) * Mathf.Exp(-t * 160f) * 0.5f;
                return ping + tick;
            }));

            // Explosion: deep rumble + crackle.
            _explosion = Norm(SynthExplosion("explosion", 0.95f, 0.05f));

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

        /// <summary>
        /// Normalise a clip so its loudest sample sits near full scale. Without
        /// this the synthesized shots came out far quieter than the UI beeps.
        /// </summary>
        static AudioClip Norm(AudioClip clip, float target = 0.95f)
        {
            if (clip == null) return null;
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);

            float peak = 0f;
            for (int i = 0; i < data.Length; i++)
            {
                float a = data[i] < 0f ? -data[i] : data[i];
                if (a > peak) peak = a;
            }
            if (peak < 0.0001f) return clip;

            float gain = target / peak;
            for (int i = 0; i < data.Length; i++)
                data[i] = Mathf.Clamp(data[i] * gain, -1f, 1f);

            clip.SetData(data, 0);
            return clip;
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
        static AudioClip SynthExplosion(string name, float duration, float alpha)
        {
            int samples = Mathf.CeilToInt(duration * SampleRate);
            var data = new float[samples];
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)SampleRate;

                // Layer 1: low rumble (heavily low-passed noise, slow decay).
                float rumbleRaw = (Random.value * 2f - 1f) * Mathf.Exp(-t * 4.5f);
                lp += alpha * (rumbleRaw - lp);
                float rumble = lp * 3.2f;

                // Layer 2: crackle (sparse bright pops, fast decay).
                float crackle = 0f;
                if (Random.value < 0.05f)
                    crackle = (Random.value * 2f - 1f) * Mathf.Exp(-t * 8f) * 0.75f;

                // Layer 3: initial hard punch.
                float punch = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 11f) * 0.8f;

                data[i] = Mathf.Clamp(rumble + crackle + punch, -1f, 1f);
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
