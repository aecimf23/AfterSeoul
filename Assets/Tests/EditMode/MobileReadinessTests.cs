using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using AfterSeoul.Unity.UI.Screens;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class MobileReadinessTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _host;
        private AppShell _shell;
        private GameSession _session;

        [SetUp]
        public void SetUp()
        {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var clock = new TestClock(DateTimeOffset.Parse("2026-09-16T00:00:00Z"));
            _session = new GameSession(new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), data, clock);
            _session.Boot();
            _session.ChooseEmployer("HWANG");
            _session.Save.WelcomePage = -1;
            _host = new GameObject("MobileReadiness");
            _host.SetActive(false);
            _shell = _host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Private).Invoke(_shell, new object[] { _session });
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_host);
            Tween.Clear();
        }

        private void Back()
        {
            var handler = typeof(AppShell).GetMethod("HandleBack", Private);
            Assert.IsNotNull(handler, "The shell needs one back action for device and keyboard input.");
            handler.Invoke(_shell, null);
        }

        [Test]
        public void CanvasModalKeepsItsControlsInsideTheSafeArea()
        {
            var canvas = _host.transform.GetChild(0);
            var modal = Ui.Modal("PhoneModal", canvas, "Settings", () => { }, out _);
            Assert.IsNotNull(modal.GetComponent<SafeArea>(), "A full-canvas modal must inset its panel from cutouts and gesture areas.");
        }

        [Test]
        public void ModalInsideSafeAreaIsNotInsetTwice()
        {
            var canvas = _host.transform.GetChild(0);
            var safe = Ui.Rect("PhoneSafe", canvas);
            safe.gameObject.AddComponent<SafeArea>();
            var modal = Ui.Modal("NestedModal", safe, "Detail", () => { }, out _);
            Assert.IsNull(modal.GetComponent<SafeArea>(), "A child of the existing safe area must retain its parent's layout bounds.");
        }

        [Test]
        public void BackClosesOnlyTheTopmostCancelableModal()
        {
            var canvas = _host.transform.GetChild(0);
            int lowerClosed = 0, upperClosed = 0;
            Ui.Modal("Lower", canvas, "Lower", () => lowerClosed++, out _);
            Ui.Modal("Upper", canvas, "Upper", () => upperClosed++, out _);
            Back();
            Assert.AreEqual(1, upperClosed);
            Assert.AreEqual(0, lowerClosed);
        }

        [Test]
        public void BackDoesNotReachBehindANonCancelableModal()
        {
            var canvas = _host.transform.GetChild(0);
            int lowerClosed = 0;
            Ui.Modal("Lower", canvas, "Lower", () => lowerClosed++, out _);
            Ui.Modal("BlockingConfirmation", canvas, "Confirm", null, out _);
            Back();
            Assert.AreEqual(0, lowerClosed, "Back must never activate an action behind a confirmation.");
        }

        [Test]
        public void BackFromAnotherTabReturnsHome()
        {
            typeof(AppShell).GetField("_enteredGame", Private).SetValue(_shell, true);
            _shell.Select(1);
            Assert.AreEqual(1, typeof(AppShell).GetField("_active", Private).GetValue(_shell));
            Back();
            Assert.AreEqual(0, typeof(AppShell).GetField("_active", Private).GetValue(_shell));
        }

        [Test]
        public void LosingFocusReleasesAnActiveHoldBeforeItsTimerAdvances()
        {
            var game = new HoldGame();
            game.Mount(Ui.Rect("HoldHost", _host.transform));
            game.Begin(0);
            game.Press();
            var factory = (FactoryScreen)((System.Collections.Generic.List<ScreenBase>)typeof(AppShell).GetField("_screens", Private).GetValue(_shell))[1];
            typeof(FactoryScreen).GetField("_game", Private).SetValue(factory, game);
            var pause = typeof(AppShell).GetMethod("OnApplicationPause", Private);
            Assert.IsNotNull(pause, "The shell must release active touch state when the app backgrounds.");
            pause.Invoke(_shell, new object[] { true });
            game.Tick(2f);
            Assert.IsFalse(game.Resolved, "A missing pointer-up after app switching must not force a failed craft.");
        }
    }
}
