using System;
using System.Collections.Generic;
using UnityEngine;

namespace AfterSeoul.Unity
{
    /// <summary>Original UI cues, quiet base music, and generated minigame judgement tones.</summary>
    public static class Sfx
    {
        private const int SampleRate = 44100;
        private const string EffectsKey = "AfterSeoul.Audio.EffectsVolume";
        private const string MusicKey = "AfterSeoul.Audio.MusicVolume";
        private static AudioSource _source, _music, _workshop, _talk;
        private static AudioRuntime _owner;
        private static readonly List<AudioClip> Generated = new List<AudioClip>();
        private static AudioClip _perfect, _good, _edge, _miss, _step, _complete, _tap;
        private static AudioClip _confirm, _error, _buy, _loot;
        private static AudioClip _hammer, _workshopComplete, _workshopMiss, _npcBlip;
        private static AudioClip _baseMusic, _factoryMusic;
        private static bool _factorySelected;
        private static float _musicFade = 1f, _nextBlip;
        private static bool _enabled = true;

        public static bool Enabled
        {
            get => _enabled;
            set { _enabled = value; ApplyVolumes(); }
        }

        public static float EffectsVolume
        {
            get => ClampVolume(PlayerPrefs.GetFloat(EffectsKey, 0.55f));
            set { SaveVolume(EffectsKey, value); ApplyVolumes(); }
        }

        public static float MusicVolume
        {
            get => ClampVolume(PlayerPrefs.GetFloat(MusicKey, 0.18f));
            set { SaveVolume(MusicKey, value); ApplyVolumes(); }
        }

        public static bool EffectsMuted
        {
            get => PlayerPrefs.GetInt("AfterSeoul.Audio.EffectsMuted", 0) != 0;
            set { PlayerPrefs.SetInt("AfterSeoul.Audio.EffectsMuted", value ? 1 : 0); PlayerPrefs.Save(); ApplyVolumes(); }
        }
        public static bool MusicMuted
        {
            get => PlayerPrefs.GetInt("AfterSeoul.Audio.MusicMuted", 0) != 0;
            set { PlayerPrefs.SetInt("AfterSeoul.Audio.MusicMuted", value ? 1 : 0); PlayerPrefs.Save(); ApplyVolumes(); }
        }
        private static float ClampVolume(float value) => float.IsNaN(value) ? 0f : Mathf.Clamp01(value);

        private static void SaveVolume(string key, float value)
        {
            PlayerPrefs.SetFloat(key, ClampVolume(value));
            PlayerPrefs.Save();
        }

        private static void ApplyVolumes()
        {
            if (_source != null) { _source.volume = EffectsVolume; _source.mute = !Enabled || EffectsMuted; }
            if (_workshop != null) { _workshop.volume = EffectsVolume; _workshop.mute = !Enabled || EffectsMuted; }
            if (_talk != null) { _talk.volume = EffectsVolume; _talk.mute = !Enabled || EffectsMuted; }
            if (_music != null) { _music.volume = MusicVolume * _musicFade; _music.mute = !Enabled || MusicMuted; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            var previous = _owner;
            Release(previous);
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                DestroyOwned(previous.gameObject);
            }
            _enabled = true;
        }

        public static void Attach(GameObject host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (_owner != null) return;

            var audio = new GameObject("AfterSeoul Audio");
            audio.transform.SetParent(host.transform, false);
            _owner = audio.AddComponent<AudioRuntime>();
            _source = audio.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _music = audio.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;
            _music.loop = true;
            _music.priority = 192;
            _workshop = audio.AddComponent<AudioSource>();
            _talk = audio.AddComponent<AudioSource>();
            _workshop.playOnAwake = _talk.playOnAwake = false;
            _workshop.spatialBlend = _talk.spatialBlend = 0f;

            _perfect = Chord("sfx_perfect", new[] { 1320f, 1760f }, 0.16f, 0.55f);
            _good = Tone("sfx_good", 990f, 0.11f, 0.5f);
            _edge = Tone("sfx_edge", 660f, 0.10f, 0.45f);
            _miss = Noise("sfx_miss", 150f, 0.14f, 0.4f);
            _step = Tone("sfx_step", 520f, 0.07f, 0.4f, square: true);
            _complete = Resources.Load<AudioClip>("Audio/extract_success")
                ?? Arpeggio("sfx_complete", new[] { 880f, 1108f, 1320f }, 0.10f, 0.5f);
            _tap = Resources.Load<AudioClip>("Audio/ui_move")
                ?? Tone("sfx_tap", 420f, 0.04f, 0.3f, square: true);
            _confirm = Resources.Load<AudioClip>("Audio/ui_confirm") ?? _good;
            _error = Resources.Load<AudioClip>("Audio/ui_error") ?? _miss;
            _buy = Resources.Load<AudioClip>("Audio/ui_buy") ?? _perfect;
            _loot = Resources.Load<AudioClip>("Audio/loot_open") ?? _complete;
            _hammer = MetalHammer();
            _npcBlip = NpcTalkClip();
            _workshopComplete = Resources.Load<AudioClip>("Audio/reload_complete") ?? _complete;
            _workshopMiss = Resources.Load<AudioClip>("Audio/weapon_equip") ?? _miss;
            _baseMusic = Resources.Load<AudioClip>("Audio/Where_the_River_Bends");
            _factoryMusic = Resources.Load<AudioClip>("Audio/Cold_Iron_Floor") ?? _baseMusic;
            _music.clip = _factorySelected ? _factoryMusic : _baseMusic;
            _musicFade = 1f;
            _nextBlip = float.NegativeInfinity;
            ApplyVolumes();
            _owner.EnsureListener();
            if (Application.isPlaying && _music.clip != null) _music.Play();
        }

        internal static void Release(AudioRuntime owner)
        {
            if (!ReferenceEquals(_owner, owner)) return;
            if (_source != null) { _source.Stop(); _source.clip = null; }
            if (_music != null) { _music.Stop(); _music.clip = null; }
            if (_workshop != null) { _workshop.Stop(); _workshop.clip = null; }
            if (_talk != null) { _talk.Stop(); _talk.clip = null; }
            foreach (var clip in Generated) if (clip != null) DestroyOwned(clip);
            Generated.Clear();
            _source = _music = _workshop = _talk = null;
            _owner = null;
            _perfect = _good = _edge = _miss = _step = _complete = _tap = null;
            _confirm = _error = _buy = _loot = null;
            _hammer = _workshopComplete = _workshopMiss = _npcBlip = null;
            _baseMusic = _factoryMusic = null;
            _factorySelected = false;
            _musicFade = 1f;
            _nextBlip = float.NegativeInfinity;
        }

        private static void DestroyOwned(UnityEngine.Object item)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }

        public static void Perfect() => Play(_perfect);
        public static void Good() => Play(_good);
        public static void Edge() => Play(_edge);
        public static void Miss() => Play(_miss);
        public static void Step() => Play(_step);
        public static void Complete() => Play(_complete);
        public static void Tap() => Play(_tap);
        public static void Confirm() => Play(_confirm);
        public static void Error() => Play(_error);
        public static void Buy() => Play(_buy);
        public static void OpenLoot() => Play(_loot);

        public static void WorkshopHit(bool automatic = false)
            => PlayEffect(_workshop, _hammer, automatic ? 0.22f : 0.65f);

        public static void WorkshopMiss() => PlayEffect(_workshop, _workshopMiss, 0.4f);
        public static void WorkshopComplete() => PlayEffect(_workshop, _workshopComplete, 0.8f);

        public static void NpcBlip(string npcId)
        {
            if (!Enabled || EffectsMuted || _talk == null || _npcBlip == null || Time.unscaledTime < _nextBlip) return;
            _nextBlip = Time.unscaledTime + 0.1f;
            // Stable character colour without changing UI or workshop pitch.
            uint voice = 0;
            foreach (char c in npcId ?? string.Empty) voice = unchecked(voice * 31 + c);
            _talk.pitch = 0.9f + (voice % 5) * 0.05f;
            PlayEffect(_talk, _npcBlip, 0.32f);
        }

        public static void SetFactoryMusic(bool factory) => _factorySelected = factory;

        internal static void TickAudio(float delta)
        {
            if (_music == null || float.IsNaN(delta) || delta <= 0f) return;
            var target = _factorySelected ? _factoryMusic : _baseMusic;
            if (_music.clip != target)
            {
                _musicFade = Mathf.MoveTowards(_musicFade, 0f, delta / 0.45f);
                if (_musicFade <= 0f)
                {
                    _music.Stop();
                    _music.clip = target;
                    if (Application.isPlaying && target != null) _music.Play();
                }
            }
            else _musicFade = Mathf.MoveTowards(_musicFade, 1f, delta / 0.65f);
            _music.volume = MusicVolume * _musicFade;
        }

        private static void PlayEffect(AudioSource source, AudioClip clip, float gain)
        {
            if (!Enabled || EffectsMuted || source == null || clip == null || !Application.isPlaying) return;
            source.PlayOneShot(clip, gain);
        }

        public static void ForScore(double score)
        {
            if (score <= 0.0) Miss();
            else if (score >= 0.92) Perfect();
            else if (score >= 0.7) Good();
            else Edge();
        }

        private static void Play(AudioClip clip)
        {
            if (!Enabled || EffectsMuted || _source == null || clip == null || !Application.isPlaying) return;
            _source.PlayOneShot(clip);
        }

        // ── 파형 만들기 ──────────────────────────────────────────

        private static AudioClip MetalHammer()
        {
            var samples = new float[(int)(SampleRate * 0.34f)];
            var random = new System.Random(7319);
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)SampleRate;
                float strike = (float)(random.NextDouble() * 2 - 1) * Mathf.Exp(-t * 190f) * 0.55f;
                float ring = Mathf.Sin(2f * Mathf.PI * 1640f * t) * Mathf.Exp(-t * 17f) * 0.32f
                    + Mathf.Sin(2f * Mathf.PI * 2713f * t) * Mathf.Exp(-t * 24f) * 0.19f
                    + Mathf.Sin(2f * Mathf.PI * 3971f * t) * Mathf.Exp(-t * 31f) * 0.11f;
                float body = Mathf.Sin(2f * Mathf.PI * 185f * t) * Mathf.Exp(-t * 48f) * 0.25f;
                samples[i] = Mathf.Clamp(strike + ring + body, -0.95f, 0.95f)
                    * Mathf.Clamp01(t / 0.001f) * Mathf.Clamp01((0.34f - t) / 0.02f);
            }
            return Bake("sfx_workshop_hammer", samples);
        }

        // Ported from EscapeFromSeoul AudioPlayer.CreateNpcTalkBlipClip; see docs/AUDIO_SOURCES.md.
        private static AudioClip NpcTalkClip()
        {
            const int rate = 22050;
            int count = Mathf.CeilToInt(rate * 0.06f);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float tone = Mathf.Sin(2f * Mathf.PI * 1450f * t) * 0.7f
                    + Mathf.Sin(2f * Mathf.PI * 1120f * t) * 0.3f;
                samples[i] = Mathf.Clamp(tone * Mathf.Exp(-t * 55f), -0.9f, 0.9f);
            }
            var clip = AudioClip.Create("SFX_NpcTalkBlip", count, 1, rate, false);
            clip.SetData(samples, 0);
            Generated.Add(clip);
            return clip;
        }

        /// <summary>
        /// 감쇠 포락선. 시작이 제일 크고 끝에서 0 이 된다.
        /// 이게 없으면 파형이 뚝 끊기면서 "틱" 하는 잡음이 같이 난다.
        /// </summary>
        private static float Envelope(int i, int total)
        {
            float t = (float)i / total;
            float attack = t < 0.02f ? t / 0.02f : 1f;      // 아주 짧은 상승
            float decay = (1f - t) * (1f - t);
            return attack * decay;
        }

        private static AudioClip Tone(string name, float hz, float seconds, float gain, bool square = false)
        {
            int n = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float phase = 2f * Mathf.PI * hz * i / SampleRate;
                float wave = square ? (Mathf.Sin(phase) >= 0f ? 1f : -1f) : Mathf.Sin(phase);
                data[i] = wave * Envelope(i, n) * gain;
            }
            return Bake(name, data);
        }

        private static AudioClip Chord(string name, float[] hz, float seconds, float gain)
        {
            int n = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float sum = 0f;
                foreach (var f in hz) sum += Mathf.Sin(2f * Mathf.PI * f * i / SampleRate);
                data[i] = sum / hz.Length * Envelope(i, n) * gain;
            }
            return Bake(name, data);
        }

        private static AudioClip Arpeggio(string name, float[] hz, float noteSeconds, float gain)
        {
            int per = Mathf.Max(1, (int)(SampleRate * noteSeconds));
            var data = new float[per * hz.Length];

            for (int note = 0; note < hz.Length; note++)
            {
                for (int i = 0; i < per; i++)
                {
                    float wave = Mathf.Sin(2f * Mathf.PI * hz[note] * i / SampleRate);
                    data[note * per + i] = wave * Envelope(i, per) * gain;
                }
            }
            return Bake(name, data);
        }

        /// <summary>
        /// 잡음 + 낮은 톤. 빗나갔을 때 쓴다 — 음정이 없어야 "틀렸다"로 들린다.
        /// 난수는 고정 시드로 만든다. 소리가 실행마다 달라질 이유가 없다.
        /// </summary>
        private static AudioClip Noise(string name, float hz, float seconds, float gain)
        {
            int n = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[n];
            var rng = new System.Random(1);

            for (int i = 0; i < n; i++)
            {
                float tone = Mathf.Sin(2f * Mathf.PI * hz * i / SampleRate);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                data[i] = (tone * 0.6f + noise * 0.4f) * Envelope(i, n) * gain;
            }
            return Bake(name, data);
        }

        private static AudioClip Bake(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            Generated.Add(clip);
            return clip;
        }
    }
}
