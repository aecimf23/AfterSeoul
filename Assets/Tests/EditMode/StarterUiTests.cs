using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Unity.UI;
using AfterSeoul.Unity.UI.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class StarterUiTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject host;
        AppShell shell;
        GameSession session;
        FactoryScreen factory;

        [SetUp] public void SetUp()
        {
            var clock = new TestClock(DateTimeOffset.Parse("2026-09-16T00:00:00Z"));
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
            session = new GameSession(new SaveService(new MemoryFileStore(),new NewtonsoftJsonCodec(),clock),data,clock);
            session.Boot(); session.ChooseEmployer("HWANG"); session.Save.WelcomePage = -1;
            session.Save.LearnedMinigames.AddRange(new[]{"signal","vault"});
            host = new GameObject("StarterUi"); host.SetActive(false);
            shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady",Private).Invoke(shell,new object[] { session });
            factory = ((System.Collections.Generic.List<ScreenBase>)typeof(AppShell).GetField("_screens",Private).GetValue(shell)).OfType<FactoryScreen>().Single();
            shell.SelectByName("공장");
            Button("FactoryEquipment").onClick.Invoke();
            Button("FactoryParts").onClick.Invoke();
        }
        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }
        Button Button(string name) => host.GetComponentsInChildren<Button>(true).First(b => b.name == name);
        T Field<T>(object target,string name) => (T)target.GetType().GetField(name,Private).GetValue(target);

        [Test] public void FreshFactory_HasAnActionablePracticeEntry()
        {
            Assert.IsTrue(Button("R_RCP_SALVAGE").interactable);
            Button("R_RCP_SALVAGE").onClick.Invoke();
            Assert.IsTrue(Button("Action").interactable, "The first visible workbench must have an enabled action");
            Button("Action").GetComponent<PressButton>().OnPointerDown(new PointerEventData(null));
            Assert.IsFalse(session.Save.Factory.Workbench.IsIdle);
            Assert.IsTrue(Field<bool>(factory,"_running"));
        }
        [Test] public void SelectingWork_ShowsPausedBoard_AndFocusesWorkbench()
        {
            Button("R_RCP_SALVAGE").onClick.Invoke();
            var game = Field<Minigame>(factory,"_game");
            Assert.IsNotNull(game, "Selecting a job must immediately show its game board");
            Assert.IsFalse(Field<bool>(factory,"_running"));
            factory.Tick(60);
            Assert.IsFalse(game.Resolved, "Preview must not run while reading instructions");
            Assert.IsNull(Button("Action").GetComponentInParent<ScrollRect>(true), "Workbench stays fixed above the parts list.");
            var input = Field<RectTransform>(factory,"_gameHost").GetComponent<CanvasGroup>();
            Assert.IsFalse(input.interactable, "Preview controls must be disabled until explicit start");
        }
        [Test] public void Signal_FirstHeldStartPress_MovesReceiverRight()
        {
            Button("R_RCP_SALVAGE").onClick.Invoke();
            var press = Button("Action").GetComponent<PressButton>();
            press.OnPointerDown(new PointerEventData(null));
            var signal = Field<DeliverySignal>(Field<Minigame>(factory,"_game"),"_signal");
            double before = signal.Cursor;
            factory.Tick(.1f);
            Assert.Greater(signal.Cursor,before,"Starting finger-down must also be held input");
            press.OnPointerUp(new PointerEventData(null));
            double held = signal.Cursor; factory.Tick(.1f);
            Assert.Less(signal.Cursor,held);
        }

        [Test] public void Vault_BoardControlsCompleteARealRun()
        {
            Button("R_RCP_VAULT").onClick.Invoke();
            Button("Action").GetComponent<PressButton>().OnPointerDown(new PointerEventData(null));
            Assert.IsFalse(Button("Action").interactable);
            Button("Sector0").onClick.Invoke();
            Button("Scan").onClick.Invoke();
            Button("Scan").onClick.Invoke();
            Assert.IsTrue(Button("Extract").interactable);
            Button("Extract").onClick.Invoke(); factory.Tick(1.2f);
            Assert.IsTrue(session.Save.Factory.Workbench.IsIdle);
            Assert.IsTrue(session.Save.Starter.PracticeCompleted);
        }
    }
}
