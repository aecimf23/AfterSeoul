using System.Reflection;
using AfterSeoul.Unity;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class AudioTests
    {
        private GameObject _host;
        private GameObject _camera;
        private float _effects;
        private float _music;
        private bool _hadEffects, _hadMusic;
        private bool _enabled, _effectsMuted, _musicMuted;

        [SetUp]
        public void SetUp()
        {
            _hadEffects = PlayerPrefs.HasKey("AfterSeoul.Audio.EffectsVolume");
            _hadMusic = PlayerPrefs.HasKey("AfterSeoul.Audio.MusicVolume");
            _effects = PlayerPrefs.GetFloat("AfterSeoul.Audio.EffectsVolume");
            _music = PlayerPrefs.GetFloat("AfterSeoul.Audio.MusicVolume");
            _enabled = Sfx.Enabled;
            _effectsMuted = Sfx.EffectsMuted; _musicMuted = Sfx.MusicMuted;
            Sfx.EffectsMuted = false; Sfx.MusicMuted = false;
            _host = new GameObject("AudioTestHost");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_host);
            if (_camera != null) Object.DestroyImmediate(_camera);
            Restore("AfterSeoul.Audio.EffectsVolume", _hadEffects, _effects);
            Restore("AfterSeoul.Audio.MusicVolume", _hadMusic, _music);
            Sfx.Enabled = _enabled;
            Sfx.EffectsMuted = _effectsMuted; Sfx.MusicMuted = _musicMuted;
        }

        private static void Restore(string key, bool existed, float value)
        {
            if (existed) PlayerPrefs.SetFloat(key, value);
            else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }

        [Test]
        public void Attach_RepeatedCallsDoNotDuplicateSources_AndStreamsLoopingMusic()
        {
            Sfx.Attach(_host);
            Sfx.Attach(_host);
            var sources = _host.GetComponentsInChildren<AudioSource>();
            Assert.That(sources.Length, Is.EqualTo(4));
            var music = System.Array.Find(sources, source => source.loop);
            Assert.That(music, Is.Not.Null);
            Assert.That(music.clip, Is.Not.Null);
            Assert.That(music.clip.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
        }

        [Test]
        public void Attach_ReusesAnExistingListener()
        {
            _camera = new GameObject("ExistingAudioListener", typeof(AudioListener));
            var before = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
            Sfx.Attach(_host);
            Assert.That(Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length, Is.EqualTo(before));
        }

        [Test]
        public void Listener_FillsMissingSceneListener_ThenYieldsToSceneListener()
        {
            var existing = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
            var enabled = System.Array.ConvertAll(existing, listener => listener.enabled);
            try
            {
                foreach (var listener in existing) listener.enabled = false;
                Sfx.Attach(_host);
                var fallback = _host.GetComponentInChildren<AudioListener>();
                Assert.That(fallback, Is.Not.Null);
                Assert.That(fallback.enabled, Is.True);
                _camera = new GameObject("NewSceneListener", typeof(AudioListener));
                var runtime = _host.GetComponentInChildren<AudioRuntime>();
                typeof(AudioRuntime).GetMethod("EnsureListener", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(runtime, null);
                Assert.That(fallback.enabled, Is.False);
                Assert.That(_camera.GetComponent<AudioListener>().enabled, Is.True);
            }
            finally
            {
                for (int i = 0; i < existing.Length; i++)
                    if (existing[i] != null) existing[i].enabled = enabled[i];
            }
        }

        [Test]
        public void Volumes_ClampPersistAndUpdateAttachedSources_AndMuteBothChannels()
        {
            var effectsProperty = typeof(Sfx).GetProperty("EffectsVolume", BindingFlags.Public | BindingFlags.Static);
            var musicProperty = typeof(Sfx).GetProperty("MusicVolume", BindingFlags.Public | BindingFlags.Static);
            Assert.That(effectsProperty, Is.Not.Null, "Effects channel needs persisted volume control.");
            Assert.That(musicProperty, Is.Not.Null, "Music needs an independent persisted volume control.");
            Sfx.Attach(_host);
            effectsProperty.SetValue(null, 3f);
            musicProperty.SetValue(null, -2f);
            Assert.That(PlayerPrefs.GetFloat("AfterSeoul.Audio.EffectsVolume"), Is.EqualTo(1f));
            Assert.That(PlayerPrefs.GetFloat("AfterSeoul.Audio.MusicVolume"), Is.EqualTo(0f));
            foreach (var source in _host.GetComponentsInChildren<AudioSource>())
                Assert.That(source.volume, Is.EqualTo(source.loop ? 0f : 1f));
            Sfx.Enabled = false;
            foreach (var source in _host.GetComponentsInChildren<AudioSource>()) Assert.That(source.mute, Is.True);
            Sfx.Enabled = true;
            foreach (var source in _host.GetComponentsInChildren<AudioSource>()) Assert.That(source.mute, Is.False);
            musicProperty.SetValue(null, float.NaN);
            Assert.That(float.IsNaN((float)musicProperty.GetValue(null)), Is.False);
        }

        [Test]
        public void Teardown_ReleasesGeneratedClips_AndCanAttachAgain()
        {
            Sfx.Attach(_host);
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            var generated = System.Array.Find(clips, clip => clip.name == "sfx_perfect");
            Assert.That(generated, Is.Not.Null);
            Object.DestroyImmediate(_host);
            Assert.That(generated == null, Is.True, "Procedural clips must not leak between sessions.");
            _host = new GameObject("ReplacementAudioHost");
            Sfx.Attach(_host);
            Assert.That(_host.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(4));
        }

        [Test]
        public void FactoryMusic_FadesToIndustrialTrack_AndRepeatedSelectionKeepsItsPosition()
        {
            Sfx.Attach(_host);
            var music = System.Array.Find(_host.GetComponentsInChildren<AudioSource>(), source => source.loop);
            var original = music.clip;
            Sfx.SetFactoryMusic(true);
            Assert.That(music.clip, Is.SameAs(original), "Switch only after fading out.");
            Tick(1f);
            Assert.That(music.clip, Is.SameAs(Resources.Load<AudioClip>("Audio/Cold_Iron_Floor")));
            Assert.That(music.clip.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
            Tick(1f);
            music.timeSamples = 1000;
            Sfx.SetFactoryMusic(true);
            Tick(0.1f);
            Assert.That(music.timeSamples, Is.EqualTo(1000));
            Sfx.SetFactoryMusic(false);
            Tick(1f);
            Assert.That(music.clip, Is.SameAs(original));
        }

        [Test]
        public void FactoryMusic_CanSelectBeforeAttachment_AndResetsWithOwnerLifetime()
        {
            Sfx.SetFactoryMusic(true);
            Sfx.Attach(_host);
            var music = System.Array.Find(_host.GetComponentsInChildren<AudioSource>(), source => source.loop);
            Assert.That(music.clip.name, Is.EqualTo("Cold_Iron_Floor"));
            Object.DestroyImmediate(_host);
            _host = new GameObject("NextAudioHost");
            Sfx.Attach(_host);
            music = System.Array.Find(_host.GetComponentsInChildren<AudioSource>(), source => source.loop);
            Assert.That(music.clip.name, Is.EqualTo("Where_the_River_Bends"));
        }

        [Test]
        public void FactoryMusic_ReversingAnUnfinishedFadeKeepsCurrentClip()
        {
            Sfx.MusicVolume = 0.4f;
            Sfx.Attach(_host);
            var music = System.Array.Find(_host.GetComponentsInChildren<AudioSource>(), source => source.loop);
            var original = music.clip;
            Sfx.SetFactoryMusic(true);
            Tick(0.2f);
            Assert.That(music.volume, Is.LessThan(Sfx.MusicVolume));
            Sfx.SetFactoryMusic(false);
            Tick(1f);
            Assert.That(music.clip, Is.SameAs(original));
            Assert.That(music.volume, Is.EqualTo(Sfx.MusicVolume));
        }

        [Test]
        public void NpcBlip_ThrottlesBurstCalls_AndDoesNotRepitchOtherEffects()
        {
            Sfx.Enabled = true;
            Sfx.Attach(_host);
            Sfx.NpcBlip("b");
            var sources = _host.GetComponentsInChildren<AudioSource>();
            var pitched = System.Array.FindAll(sources, source => Mathf.Abs(source.pitch - 1f) > 0.001f);
            Assert.That(pitched.Length, Is.EqualTo(1), "Only the dedicated voice source changes pitch.");
            var voicePitch = pitched[0].pitch;
            Sfx.NpcBlip("x");
            Sfx.WorkshopHit();
            Sfx.WorkshopHit(true);
            Sfx.WorkshopMiss();
            Sfx.WorkshopComplete();
            Assert.That(pitched[0].pitch, Is.EqualTo(voicePitch), "Same-frame speech calls must not burst.");
            Assert.That(System.Array.FindAll(sources, source => Mathf.Abs(source.pitch - 1f) > 0.001f).Length, Is.EqualTo(1));
        }

        [Test]
        public void Attach_AppliesPreviouslyPersistedMutesToEveryChannel()
        {
            Sfx.Enabled = true;
            Sfx.EffectsMuted = true;
            Sfx.MusicMuted = true;
            Sfx.Attach(_host);
            foreach (var source in _host.GetComponentsInChildren<AudioSource>()) Assert.That(source.mute, Is.True);
        }

        [Test]
        public void EffectsMute_CoversWorkshopAndTalk_WhileMusicRemainsIndependent()
        {
            Sfx.Enabled = true;
            Sfx.Attach(_host);
            Sfx.EffectsMuted = true;
            foreach (var source in _host.GetComponentsInChildren<AudioSource>())
                Assert.That(source.mute, Is.EqualTo(!source.loop));
            Sfx.MusicMuted = true;
            Sfx.SetFactoryMusic(true);
            Tick(1f);
            foreach (var source in _host.GetComponentsInChildren<AudioSource>()) Assert.That(source.mute, Is.True);
        }

        [Test]
        public void WorkshopAndTalkClips_AreLoadedOrGenerated_AndGeneratedClipsAreReleased()
        {
            Sfx.Attach(_host);
            Assert.That(Resources.Load<AudioClip>("Audio/reload_complete"), Is.Not.Null);
            Assert.That(Resources.Load<AudioClip>("Audio/weapon_equip"), Is.Not.Null);
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            var hammer = System.Array.Find(clips, clip => clip.name == "sfx_workshop_hammer");
            var talk = System.Array.Find(clips, clip => clip.name == "SFX_NpcTalkBlip");
            Assert.That(hammer, Is.Not.Null);
            Assert.That(talk, Is.Not.Null);
            Assert.That(talk.frequency, Is.EqualTo(22050));
            Object.DestroyImmediate(_host);
            Assert.That(hammer == null && talk == null, Is.True);
        }

        private static void Tick(float delta) => typeof(Sfx)
            .GetMethod("TickAudio", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { delta });
    }
}
