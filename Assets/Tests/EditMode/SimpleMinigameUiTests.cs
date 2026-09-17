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
    public class SimpleMinigameUiTests
    {
        private GameObject _host;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        [SetUp] public void Setup() {
            _host = new GameObject("SimpleUi", typeof(RectTransform));
        }
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(_host); Tween.Clear(); }

        [Test] public void VaultUsesRecognizableBoxesInsteadOfCoordinatesOrRewardMultipliers() {
            var game = new VaultGame(); game.Mount((RectTransform)_host.transform); game.Begin(0);
            foreach (var label in _host.GetComponentsInChildren<Text>()) {
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(label.text, @"[A-D]-[1-4]"), label.text);
                Assert.IsFalse(label.text.Contains("×"), label.text);
            }
        }

        [Test] public void SignalHidesRewardDecisionsUntilThereIsSomethingToCollect() {
            var game = new SignalGame(); game.Mount((RectTransform)_host.transform); game.Begin(0);
            Assert.IsFalse(_host.GetComponentsInChildren<Button>().Any(b => b.name == "BankCargo" || b.name == "RiskCargo"));
        }

        [Test] public void FirstPlayWaitsForExplicitStartAndHelpCanBeReopenedWithoutRestarting() {
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            var clock = new TestClock(new DateTimeOffset(2026,9,17,1,0,0,TimeSpan.Zero));
            var session = new GameSession(new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock),data,clock);
            session.Boot(); session.ChooseEmployer("HWANG");
            _host.SetActive(false);
            var shell = _host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady",Private).Invoke(shell,new object[]{session});
            shell.Select(1);
            var screens = (System.Collections.IList)typeof(AppShell).GetField("_screens",Private).GetValue(shell);
            var factory = screens[1]; var type = factory.GetType();
            type.GetMethod("OnPick",Private).Invoke(factory,new object[]{"RCP_SALVAGE"});
            type.GetMethod("OnPress",Private).Invoke(factory,null);
            Assert.IsFalse((bool)type.GetField("_running",Private).GetValue(factory), "The game clock must wait for the tutorial.");
            var start = _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="StartMinigame");
            start.onClick.Invoke();
            CollectionAssert.Contains(session.Save.LearnedMinigames, "signal");
            var loaded = new NewtonsoftJsonCodec().Deserialize<GameSave>(new NewtonsoftJsonCodec().Serialize(session.Save));
            CollectionAssert.Contains(loaded.LearnedMinigames, "signal");
            var game = type.GetField("_game",Private).GetValue(factory);
            Assert.IsNotNull(game);
            var help = _host.GetComponentsInChildren<Button>(true).Single(b=>b.name=="GameHelp");
            help.onClick.Invoke();
            ((ScreenBase)factory).Tick(50);
            Assert.IsFalse(((Minigame)game).Resolved, "Help must pause the active game.");
            _host.GetComponentsInChildren<Button>(true).Last(b=>b.name=="StartMinigame").onClick.Invoke();
            Assert.AreSame(game,type.GetField("_game",Private).GetValue(factory));
        }
    }
}
