using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class PrologueUiTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject host;
        private GameSession session;
        private FailingFiles files;
        private TestClock clock;

        [SetUp] public void Setup()
        {
            host = new GameObject("PrologueTest", typeof(RectTransform));
            files = new FailingFiles();
            clock = new TestClock(new DateTimeOffset(2026, 9, 17, 1, 0, 0, TimeSpan.Zero));
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            session = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), data, clock);
            session.Boot();
        }

        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }

        private void Welcome(bool replay, Action done)
        {
            var type = typeof(AppShell).Assembly.GetType("AfterSeoul.Unity.UI.WelcomeBriefing");
            Activator.CreateInstance(type, Private, null, new object[] { host.transform, session, replay, done }, null);
        }

        private Button Button(string name) => host.GetComponentsInChildren<Button>(true).Single(b => b.name == name);

        [Test] public void AdvanceBackResumeAndSkipArePersisted()
        {
            bool done = false;
            Welcome(false, () => done = true);
            Assert.IsNotEmpty(host.GetComponentsInChildren<Image>().Where(i => i.name == "StoryArtwork" && i.sprite != null).ToArray());
            Assert.IsFalse(host.GetComponentsInChildren<Text>().Any(t => t.name == "Art"));
            Button("Next").onClick.Invoke();
            Assert.AreEqual(1, session.Save.WelcomePage);
            Button("Back").onClick.Invoke();
            Assert.AreEqual(0, session.Save.WelcomePage);
            Button("Next").onClick.Invoke();
            Ui.Clear(host.transform);
            session = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), session.Data, clock);
            session.Boot();
            Welcome(false, () => done = true);
            Assert.AreEqual("2 / 4", host.GetComponentsInChildren<Text>().Single(t => t.name == "Progress").text);
            Button("Skip").onClick.Invoke();
            Assert.IsTrue(done);
            Assert.AreEqual(-1, session.Save.WelcomePage);
            Assert.IsTrue(session.NeedsEmployerChoice);
        }

        [Test] public void FailedWritesKeepCurrentPageAndCannotClose()
        {
            bool done = false;
            Welcome(false, () => done = true);
            files.Fail = true;
            Button("Next").onClick.Invoke();
            Assert.AreEqual(0, session.Save.WelcomePage);
            Assert.AreEqual("1 / 4", host.GetComponentsInChildren<Text>().Single(t => t.name == "Progress").text);
            Button("Skip").onClick.Invoke();
            Assert.IsFalse(done);
            Assert.AreEqual(0, session.Save.WelcomePage);
            files.Fail = false;
            Button("Next").onClick.Invoke();
            files.Fail = true;
            Button("Back").onClick.Invoke();
            Assert.AreEqual(1, session.Save.WelcomePage);
        }

        [Test] public void ReplayBeforeEmployerChoiceDoesNotChangeSavedProgress()
        {
            session.Save.WelcomePage = 2;
            string before = new NewtonsoftJsonCodec().Serialize(session.Save);
            Welcome(true, () => { });
            Button("Next").onClick.Invoke();
            Button("Skip").onClick.Invoke();
            Assert.AreEqual(before, new NewtonsoftJsonCodec().Serialize(session.Save));
        }

        [TestCase(false, 0, true)]
        [TestCase(false, -1, false)]
        [TestCase(true, 0, false)]
        [TestCase(true, -1, false)]
        public void EntryOnlyShowsUnfinishedPrologueForUnchosenResident(bool chosen, int page, bool expected)
        {
            if (chosen) session.ChooseEmployer("HWANG");
            session.Save.WelcomePage = page;
            host.SetActive(false);
            var shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Private).Invoke(shell, new object[] { session });
            Assert.AreEqual(expected, typeof(AppShell).GetField("_welcome", Private).GetValue(shell) != null);
            Assert.AreEqual(!chosen && !expected, typeof(AppShell).GetField("_employerHost", Private).GetValue(shell) != null);
        }

        private sealed class FailingFiles : IFileStore
        {
            private readonly MemoryFileStore inner = new MemoryFileStore();
            public bool Fail;
            public bool Exists(string p) => inner.Exists(p);
            public string ReadAllText(string p) => inner.ReadAllText(p);
            public void WriteAllText(string p, string data) { if (Fail) throw new IOException("disk unavailable"); inner.WriteAllText(p, data); }
            public void Replace(string a, string b) => inner.Replace(a, b);
            public bool TryMove(string a, string b) => inner.TryMove(a, b);
        }
    }
}
