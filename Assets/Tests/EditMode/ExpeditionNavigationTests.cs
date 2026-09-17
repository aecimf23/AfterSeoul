using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Scav;
using AfterSeoul.Unity.UI;
using AfterSeoul.Unity.UI.Screens;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class ExpeditionNavigationTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject host;
        GameSession session;
        ExpeditionScreen expedition;

        [SetUp] public void SetUp()
        {
            var clock = new TestClock(DateTimeOffset.Parse("2026-09-16T00:00:00Z"));
            var data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            session = new GameSession(new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), data, clock);
            session.Boot(); session.ChooseEmployer("HWANG"); session.Save.WelcomePage = -1;
            host = new GameObject("ExpeditionNavigationUi"); host.SetActive(false);
            var shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Private).Invoke(shell, new object[] { session });
            shell.SelectByName("탐색");
            expedition = ((List<ScreenBase>)typeof(AppShell).GetField("_screens", Private).GetValue(shell)).OfType<ExpeditionScreen>().Single();
        }

        [TearDown] public void TearDown() { UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }
        Button[] Buttons() => host.GetComponentsInChildren<Button>(true);
        Button Button(string name) => Buttons().Single(b => b.name == name);
        bool HasButton(string name) => Buttons().Any(b => b.name == name);
        bool HasObject(string name) => host.GetComponentsInChildren<Transform>(true).Any(t => t.name == name);
        void Click(string name)
        {
            var button = Button(name);
            Assert.IsTrue(button.interactable, name + " must be available");
            button.onClick.Invoke();
        }
        void AssertNoTeamOrDeparture()
        {
            Assert.IsFalse(Buttons().Any(b => b.name.StartsWith("Pick_") || b.name.StartsWith("Depart_") || b.name == "OrientationDepart"));
        }
        ScavState AddArmedScav(string uid = "sc_navigation")
        {
            var scav = new ScavState { Uid = uid, Name = uid, Search = 5, Combat = 5, Survival = 20,
                WagePerHour = 40000, Status = ScavStatus.Idle, HiredAt = session.Clock.UtcNow };
            scav.Equipment[EquipSlot.Weapon] = "MEL01";
            session.Save.Scavs.Add(scav);
            return scav;
        }
        [Test] public void OverviewOnlyShowsSurvivedRouteUnlocks()
        {
            CollectionAssert.AreEqual(new[]{"MapSelect_YONGSAN_MARKET"}, Buttons().Where(b=>b.name.StartsWith("MapSelect_")).Select(b=>b.name));
            AssertNoTeamOrDeparture();
            session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET"); expedition.Refresh();
            Assert.IsTrue(HasButton("MapSelect_GURO_FACTORY")); Assert.IsFalse(HasButton("MapSelect_MYEONGDONG"));
        }
        [Test] public void RegionDetailsOpenInModalAndTeamCompletionReturnsToDetails()
        {
            AddArmedScav(); Click("MapSelect_YONGSAN_MARKET"); Assert.IsTrue(HasObject("Map_YONGSAN_MARKET"));
            Click("EditMapTeam"); Assert.IsTrue(HasButton("Pick_sc_navigation"));
            Click("TeamReady"); Assert.IsTrue(HasObject("Map_YONGSAN_MARKET"));
        }
        [Test] public void NormalDepartureUsesSelectedTeamAndReturnsToMap()
        {
            var scav=AddArmedScav(); session.Save.Player.Money=1000000; session.Save.Orientation.Stage=OrientationStage.Completed;
            Click("MapSelect_YONGSAN_MARKET"); Assert.IsFalse(Button("Depart_YONGSAN_MARKET").interactable);
            Click("EditMapTeam"); Click("Pick_"+scav.Uid); Click("TeamReady");
            long money=session.Save.Player.Money; Click("Depart_YONGSAN_MARKET");
            var run=session.Save.Expeditions.Single(); Assert.AreEqual("YONGSAN_MARKET",run.MapId);
            CollectionAssert.AreEqual(new[]{scav.Uid},run.ScavUids); Assert.AreEqual(money-run.CostPaid,session.Save.Player.Money);
            AssertNoTeamOrDeparture();
        }
        [Test] public void FirstSupplyRemainsFreeWithoutShowingLockedMyeongdong()
        {
            var scav=AddArmedScav(); session.Save.Player.Money=0;
            Click("ChooseTeam"); Click("Pick_"+scav.Uid); Click("OrientationDepart");
            var run=session.Save.Expeditions.Single(); Assert.IsTrue(run.IsOrientation);
            Assert.AreEqual(TimeSpan.FromMinutes(3),run.ReturnsAt-run.DepartedAt); Assert.AreEqual(0,run.CostPaid);
            Assert.IsFalse(HasButton("MapSelect_MYEONGDONG")); AssertNoTeamOrDeparture();
        }
        [Test] public void ClosingRegionRemovesItsOpenDetails()
        {
            session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET"); expedition.Refresh(); Click("MapSelect_GURO_FACTORY");
            session.Save.SurvivedExplorationMapIds.Clear(); expedition.Refresh();
            Assert.IsFalse(HasObject("Map_GURO_FACTORY")); Assert.IsFalse(HasButton("MapSelect_GURO_FACTORY"));
        }
        [TestCase(ScavStatus.Working)] [TestCase(ScavStatus.OnExpedition)]
        public void BusyScavIsPrunedAndNotAutomaticallyReselected(ScavStatus status)
        {
            var scav=AddArmedScav(); session.Save.Player.Money=1000000;
            Click("MapSelect_YONGSAN_MARKET"); Click("EditMapTeam"); Click("Pick_"+scav.Uid); Click("TeamReady");
            Assert.IsTrue(Button("Depart_YONGSAN_MARKET").interactable);
            scav.Status=status; expedition.Refresh(); Assert.IsFalse(Button("Depart_YONGSAN_MARKET").interactable);
            scav.Status=ScavStatus.Idle; expedition.Refresh(); Assert.IsFalse(Button("Depart_YONGSAN_MARKET").interactable);
        }
        [Test] public void OperationsShowsOnlyActiveRunsAndUpdatesRemainingTime()
        {
            session.Save.Expeditions.Add(new ExpeditionState{Uid="active",MapId="YONGSAN_MARKET",DepartedAt=session.Clock.UtcNow,ReturnsAt=session.Clock.UtcNow.AddMinutes(20)});
            session.Save.Expeditions.Add(new ExpeditionState{Uid="done",MapId="YONGSAN_MARKET",Resolved=true});
            Click("Operations"); Assert.IsTrue(HasObject("Run0")); Assert.IsFalse(HasObject("Run1"));
            var label=host.GetComponentsInChildren<Text>(true).Single(t=>t.name=="Time"); string before=label.text;
            ((TestClock)session.Clock).Advance(TimeSpan.FromMinutes(2)); expedition.Tick(.1f); Assert.AreNotEqual(before,label.text);
        }
        [Test] public void RescueUsesPreparedTeamFromOperations()
        {
            var scav=AddArmedScav(); session.Save.Player.Money=1000000;
            session.Save.Scavs.Add(new ScavState{Uid="missing",Name="Missing",Status=ScavStatus.Missing,LostAtMapId="YONGSAN_MARKET",LostAt=session.Clock.UtcNow.AddHours(-7),SignalAt=session.Clock.UtcNow.AddHours(-1)});
            Click("ChooseTeam"); Click("Pick_"+scav.Uid); Click("TeamReady"); Click("Operations"); Click("RB_missing");
            Assert.AreEqual("missing",session.Save.Expeditions.Single().RescueScavUid);
        }
    }
}
