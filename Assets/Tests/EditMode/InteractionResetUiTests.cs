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
    public class InteractionResetUiTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host; AppShell shell; GameSession session;
        [SetUp] public void Setup()
        {
            var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
            var clock=new TestClock(DateTimeOffset.Parse("2026-09-16T00:00:00Z"));
            session=new GameSession(new SaveService(new MemoryFileStore(),new NewtonsoftJsonCodec(),clock),data,clock);
            session.Boot();session.ChooseEmployer("HWANG");session.Save.WelcomePage=-1;
            host=new GameObject("InteractionUI");host.SetActive(false);shell=host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady",Private).Invoke(shell,new object[]{session});
        }
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(host);Tween.Clear(); }
        Button Button(string name)=>host.GetComponentsInChildren<Button>(true).First(b=>b.name==name);
        void Tick(float delta)=>typeof(AppShell).GetMethod("TickGreeting",Private).Invoke(shell,new object[]{delta});
        void Settings()=>typeof(AppShell).GetMethod("OpenSettings",Private).Invoke(shell,null);
        [TestCase(1080f)] [TestCase(966f)] [TestCase(900f)]
        public void ReturnReportFitsNarrowPhoneAndSafeArea(float width)
        {
            var canvas = Ui.Rect("Phone", host.transform);
            canvas.sizeDelta = new Vector2(width, 2200);
            var report = new ResolveReport { To = DateTimeOffset.UtcNow };
            new ReturnCutscene(canvas, report, session, null);
            var root = (RectTransform)canvas.Find("ReturnCutscene");
            var safe = (RectTransform)root.Find("SafeArea");
            Assert.IsNotNull(safe);
            Assert.IsNotNull(safe.GetComponent<SafeArea>());
            safe.anchorMin = new Vector2(.04f, .03f);
            safe.anchorMax = new Vector2(.96f, .97f);
            var panel = (RectTransform)safe.Find("Panel");
            Assert.AreEqual(safe.rect.width - Theme.Gutter * 2, panel.rect.width, .1f);
            var corners = new Vector3[4]; panel.GetWorldCorners(corners);
            Assert.Greater(safe.InverseTransformPoint(corners[0]).x, safe.rect.xMin);
            Assert.Less(safe.InverseTransformPoint(corners[2]).x, safe.rect.xMax);
        }
        [Test] public void ModalInteriorDoesNotForwardClicksToDismissBackground()
        {
            var surface = new GameObject("TouchSurface", typeof(RectTransform));
            var events = new GameObject("TouchEvents", typeof(UnityEngine.EventSystems.EventSystem));
            try {
                int closed = 0;
                var modal = Ui.Modal("TouchModal", surface.transform, "test", () => closed++, out var body);
                var label = Ui.Label("Explanation", body, "test");
                var pointer = new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()) {
                    button = UnityEngine.EventSystems.PointerEventData.InputButton.Left
                };
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(label.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(0, closed, "Content clicks must stop before reaching the scrim.");
                var field = Ui.Rect("Name", body).gameObject.AddComponent<InputField>();
                UnityEngine.EventSystems.ExecuteEvents.ExecuteHierarchy(field.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(0, closed, "Input field clicks must not dismiss the modal.");
                UnityEngine.EventSystems.ExecuteEvents.Execute(modal.gameObject, pointer, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(1, closed, "The outside background must still dismiss an ordinary modal.");
            } finally { UnityEngine.Object.DestroyImmediate(surface); UnityEngine.Object.DestroyImmediate(events); }
        }
        [Test] public void HomeGuideDoesNotRepeatQuestOfferBeforeAcceptanceScreen()
        {
            typeof(AppShell).GetMethod("ShowStepPrompt",Private).Invoke(shell,new object[]{"direct_exploration"});
            var hint = host.GetComponentsInChildren<Text>(true).Single(t=>t.name=="NextAction");
            Assert.AreNotEqual(FirstExplorationQuest.Offer(session.Save), hint.text);
        }
        [Test] public void HeaderTapChangesDialogue_AndIdleAddsAnotherLine()
        {
            var portrait=host.GetComponentsInChildren<Image>(true).Single(i=>i.name=="EmployerPortrait");
            Assert.IsNotNull(portrait.sprite);
            Button("EmployerScene").onClick.Invoke(); for(int i=0;i<30;i++)Tick(.1f);
            var text=host.GetComponentsInChildren<Text>(true).Single(t=>t.name=="GreetingText");
            string first=text.text;Assert.IsNotEmpty(first);
            Button("EmployerScene").onClick.Invoke(); for(int i=0;i<30;i++)Tick(.1f);
            string second=text.text;Assert.AreNotEqual(first,second);
            for(int i=0;i<46;i++)Tick(1);
            Assert.AreNotEqual(second,text.text); Assert.IsTrue(text.transform.parent.gameObject.activeSelf);
        }
        [Test] public void CancelPreservesSave_ConfirmReplaysLaunch_ThenEmployerChoiceAndTutorial()
        {
            session.Save.Player.Money=456789;
            Settings();Button("ResetAccount").onClick.Invoke();Button("CancelAccountReset").onClick.Invoke();
            Assert.AreEqual(456789,session.Save.Player.Money);
            Settings();Button("ResetAccount").onClick.Invoke();Button("ConfirmAccountReset").onClick.Invoke();
            Assert.IsTrue(session.NeedsEmployerChoice);
            Assert.AreEqual(0,session.Save.Scavs.Count);
            Assert.AreEqual(1,host.transform.childCount);
            Button("LaunchPresentation").onClick.Invoke();
            Button("LaunchPresentation").onClick.Invoke();
            for(int i=0;i<4;i++) Button("Next").onClick.Invoke();
            Button("E_HWANG").onClick.Invoke();
            Button("QuestAction_main:first").onClick.Invoke();
            Assert.IsTrue(StarterSupport.Active(session.Save));
            Assert.IsTrue(Button("AcceptFirstQuest").gameObject.activeSelf);
            Assert.IsFalse(session.NeedsEmployerChoice);
        }
        [Test] public void AllWeaponAndMaterialSpritesResolveAndUseDistinctTiles()
        {
            var seen=new System.Collections.Generic.HashSet<Sprite>();
            foreach(var gun in AfterSeoul.Factory.ProductionWork.Guns(session.Data)) Assert.IsTrue(seen.Add(GameArt.Weapon(gun.WeaponId)));
            Assert.AreEqual(25,seen.Count);
            for(int tier=1;tier<=4;tier++)Assert.IsTrue(seen.Add(GameArt.Material(tier)));
            Assert.IsNotNull(GameArt.City()); Assert.IsNotNull(GameArt.EmptyBench());
        }
        [Test] public void ScreenLocalModalBlocksBothClickBanterAndIdleBanter()
        {
            var screen=host.GetComponentsInChildren<RectTransform>(true).Single(r=>r.name=="Screen_기지");
            var modal=Ui.Modal("NestedDialogue",screen,"test",null,out var body);
            Button("EmployerScene").onClick.Invoke();for(int i=0;i<90;i++)Tick(1);
            var text=host.GetComponentsInChildren<Text>(true).Single(t=>t.name=="GreetingText");
            Assert.AreEqual("",text.text);
            modal.gameObject.SetActive(false);
            Button("EmployerScene").onClick.Invoke();for(int i=0;i<8;i++)Tick(.2f);
            Assert.IsNotEmpty(text.text);
        }
    }
}
