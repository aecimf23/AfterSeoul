using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Unity;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class SettingsFlowTests
    {
        private string _theme;
        private bool _hadTheme;
        private bool _motion, _effectsMuted, _musicMuted;
        private float _effects, _music;
        private GameObject _host;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup()
        {
            _hadTheme = PlayerPrefs.HasKey("AfterSeoul.UI.Theme");
            _theme = PlayerPrefs.GetString("AfterSeoul.UI.Theme");
            _motion = PresentationSettings.ReducedMotion;
            _effectsMuted = Sfx.EffectsMuted; _musicMuted = Sfx.MusicMuted;
            _effects = Sfx.EffectsVolume; _music = Sfx.MusicVolume;
            Sfx.EffectsMuted = false; Sfx.MusicMuted = false;
            PresentationSettings.ReducedMotion = false;
            Tween.Clear();
            _host = new GameObject("SettingsFlow");
        }
        [TearDown] public void Cleanup()
        {
            UnityEngine.Object.DestroyImmediate(_host);
            if (_hadTheme) PlayerPrefs.SetString("AfterSeoul.UI.Theme", _theme); else PlayerPrefs.DeleteKey("AfterSeoul.UI.Theme");
            Theme.Reload();
            PresentationSettings.ReducedMotion = _motion;
            Sfx.EffectsMuted = _effectsMuted; Sfx.MusicMuted = _musicMuted;
            Sfx.EffectsVolume = _effects; Sfx.MusicVolume = _music;
            Tween.Clear();
        }
        [Test] public void ThemeControlsKeepCurrentScreenAndGameSave()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
            var clock = new TestClock(new DateTimeOffset(2026,9,15,1,0,0,TimeSpan.Zero));
            var session = new GameSession(new SaveService(new MemoryFileStore(),new NewtonsoftJsonCodec(),clock),data,clock);
            session.Boot(); session.ChooseEmployer("HWANG");
            _host.SetActive(false);
            var shell = _host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady",Private).Invoke(shell,new object[]{session});
            shell.Select(2); Tween.Tick(1);
            var before = new NewtonsoftJsonCodec().Serialize(session.Save);
            var canvas = _host.transform.GetChild(0);
            var screen = _host.GetComponentsInChildren<RectTransform>(true).First(r=>r.name=="Screen_탐색");
            foreach (var id in Theme.Ids)
            {
                typeof(AppShell).GetMethod("OpenSettings",Private).Invoke(shell,null);
                var option = _host.GetComponentsInChildren<Button>(true).First(b=>b.name=="ThemeOption_"+id);
                option.onClick.Invoke(); Tween.Tick(1);
                Assert.AreSame(canvas,_host.transform.GetChild(0));
                Assert.IsTrue(screen.gameObject.activeSelf);
                Assert.AreEqual(Theme.Bg,canvas.Find("Background").GetComponent<Image>().color);
                Assert.AreEqual(before,new NewtonsoftJsonCodec().Serialize(session.Save));
            }
            var screens = (System.Collections.Generic.List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(shell);
            var spy = new TickCounter(); screens[2] = spy;
            typeof(AppShell).GetMethod("Update",Private).Invoke(shell,null);
            Assert.AreEqual(0,spy.Ticks,"Settings must pause active crafting input/timer.");
            typeof(AppShell).GetMethod("CloseSettings",Private).Invoke(shell,null);
            typeof(AppShell).GetMethod("Update",Private).Invoke(shell,null);
            Assert.AreEqual(1,spy.Ticks);
            typeof(AppShell).GetMethod("OpenSettings",Private).Invoke(shell,null);
            var slider = _host.GetComponentsInChildren<Slider>(true)[0];
            slider.value=.37f;
            Assert.AreEqual(.37f,Sfx.EffectsVolume,.001f);
            var mute = slider.transform.parent.GetComponentsInChildren<Button>(true).First(b=>b.name=="Mute");
            mute.onClick.Invoke();
            Assert.IsTrue(Sfx.EffectsMuted);
            Assert.AreEqual(.37f,Sfx.EffectsVolume,.001f);
            var reset = _host.GetComponentsInChildren<Button>(true).First(b=>b.name=="ResetPreferences");
            reset.onClick.Invoke();
            Assert.AreEqual(.37f,Sfx.EffectsVolume,.001f,"First click asks for reset confirmation.");
            reset.onClick.Invoke();
            Assert.AreEqual(.55f,Sfx.EffectsVolume,.001f);
            Assert.AreEqual("night",Theme.Id);
            Assert.AreEqual(before,new NewtonsoftJsonCodec().Serialize(session.Save));
        }
        private sealed class TickCounter : ScreenBase
        {
            public int Ticks;
            public override string TabName => "Probe";
            protected override void Build() {}
            public override void Tick(float deltaTime) { Ticks++; }
        }
        [Test] public void ReducedMotionSkipsRealScreenSlide()
        {
            PresentationSettings.ReducedMotion = true;
            var rect = _host.AddComponent<RectTransform>();
            var home = new Vector2(20,40); rect.anchoredPosition = home;
            Tween.SlideIn(rect,new Vector2(48,0),.2f);
            Assert.AreEqual(home,rect.anchoredPosition);
        }
        [Test] public void ReducedMotionSettlesDecorationButKeepsGameTiming()
        {
            PresentationSettings.ReducedMotion = true;
            float decoration=0, gameplay=0;
            Tween.Play(_host,"alpha",2,t=>decoration=t);
            Tween.Play(_host,"game-timing",2,t=>gameplay=t,Tween.Ease.Linear);
            Assert.AreEqual(1,decoration); Assert.AreEqual(0,gameplay);
            Tween.Tick(1); Assert.AreEqual(.5f,gameplay,.001f);
            float pulse=0;
            Tween.Loop(_host,"breathe",1,t=>pulse=t);
            Assert.AreEqual(.5f,pulse,.001f);
        }
        [Test] public void MuteKeepsVolumeAndResetRestoresDefaults()
        {
            Sfx.Attach(_host);
            Sfx.MusicVolume=.43f; Sfx.MusicMuted=true;
            Assert.IsTrue(_host.GetComponentsInChildren<AudioSource>().First(s=>s.loop).mute);
            Assert.AreEqual(.43f,Sfx.MusicVolume,.001f);
            Sfx.MusicMuted=false;
            Assert.IsFalse(_host.GetComponentsInChildren<AudioSource>().First(s=>s.loop).mute);
            PresentationSettings.Reset();
            Assert.AreEqual(.18f,Sfx.MusicVolume,.001f);
            Assert.IsFalse(Sfx.MusicMuted);
        }
    }
}
