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
    public class TabClarityTests
    {
        const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        GameObject host;AppShell shell;
        [SetUp] public void Setup()
        {
            var clock=new TestClock(DateTimeOffset.Parse("2026-09-29T00:00:00Z"));
            var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
            var session=new GameSession(new SaveService(new MemoryFileStore(),new NewtonsoftJsonCodec(),clock),data,clock);
            session.Boot();session.ChooseEmployer("HWANG");session.Save.WelcomePage=-1;session.Save.FactoryTutorialSeen=true;
            host=new GameObject("TabClarity");host.SetActive(false);shell=host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady",Hidden).Invoke(shell,new object[]{session});
        }
        [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(host);Tween.Clear();}
        Button Button(string id)=>host.GetComponentsInChildren<Button>(true).Last(b=>b.name==id);
        RectTransform Rect(string id)=>host.GetComponentsInChildren<RectTransform>(true).Last(r=>r.name==id);
        [Test] public void ExplorationSeparatesPlayerDepartureFromDispatch()
        {
            shell.SelectByName("탐색");
            Assert.IsTrue(Rect("DirectExplorationLanding").gameObject.activeSelf);
            Assert.IsFalse(Rect("DispatchLanding").gameObject.activeSelf);
            Button("ExploreDispatchTab").onClick.Invoke();
            Assert.IsTrue(Rect("DispatchLanding").gameObject.activeSelf);
            Assert.IsFalse(Rect("DirectExplorationLanding").gameObject.activeSelf);
            shell.SelectByName("탐색");Assert.IsTrue(Rect("DirectExplorationLanding").gameObject.activeSelf);
            Button("StartDirectExploration").onClick.Invoke();
            Assert.IsNotNull(host.GetComponentInChildren<ExplorationView>(true));
        }
        [Test] public void RapidTabChangesDoNotAccumulateHorizontalOffset()
        {
            shell.SelectByName("기지");shell.SelectByName("창고");shell.SelectByName("기지");
            Tween.Tick(3);
            Assert.AreEqual(0,Rect("Screen_기지").anchoredPosition.x,.01f);
        }
        [Test] public void WarehouseSeparatesStoredItemsFromEquippedSlots()
        {
            shell.SelectByName("창고");
            Assert.IsTrue(Rect("WarehouseInventory").gameObject.activeSelf);
            Assert.IsFalse(Rect("WarehouseEquipment").gameObject.activeSelf);
            Button("WarehouseEquipmentTab").onClick.Invoke();
            Assert.IsFalse(Rect("WarehouseInventory").gameObject.activeSelf);
            Assert.IsTrue(Rect("WarehouseEquipment").gameObject.activeSelf);
            shell.SelectByName("창고");Assert.IsTrue(Rect("WarehouseInventory").gameObject.activeSelf);
        }
        [Test] public void PersonnelDoesNotMixCandidatesWithOwnedRoster()
        {
            shell.SelectByName("인원");
            Assert.IsFalse(host.GetComponentsInChildren<Text>(true).Any(t=>t.name=="MarketHead"));
            Button("PersonnelRecruitTab").onClick.Invoke();
            Assert.IsTrue(host.GetComponentsInChildren<Text>(true).Any(t=>t.name=="MarketHead"));
            Assert.IsFalse(host.GetComponentsInChildren<Text>(true).Any(t=>t.name=="RosterHead"));
        }
    }
}
