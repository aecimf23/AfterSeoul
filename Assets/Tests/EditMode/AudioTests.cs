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
            Assert.That(sources.Length, Is.EqualTo(2));
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
            Assert.That(_host.GetComponentsInChildren<AudioSource>().Length, Is.EqualTo(2));
        }
    }
}
