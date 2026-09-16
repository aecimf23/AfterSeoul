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
        void PrepareNormal(string mapId = "MYEONGDONG")
        {
            session.Save.Orientation.Stage = OrientationStage.Completed;
            session.Save.Player.Money = 1000000;
            expedition.Refresh();
            Click("SelectMap_" + mapId);
            Click("PrepareExpedition");
        }

        [Test] public void Overview_OnlyShowsUnlockedRegions_WithoutTeamOrDeparture()
        {
            AddArmedScav();
            session.Save.Player.Level = 1; session.Save.NpcTrust.Clear(); expedition.Refresh();
            CollectionAssert.AreEquivalent(new[] { "SelectMap_MYEONGDONG" },
                Buttons().Where(b => b.name.StartsWith("SelectMap_")).Select(b => b.name).ToArray());
            AssertNoTeamOrDeparture();
            Assert.IsFalse(HasButton("PrepareExpedition"));

            session.Save.Player.Level = 9;
            foreach (var map in ((JsonDataRegistry)session.Data).Maps)
                if (map.Unlock != null && !string.IsNullOrEmpty(map.Unlock.NpcId)) session.Save.NpcTrust[map.Unlock.NpcId] = 10;
            expedition.Refresh();
            CollectionAssert.AreEquivalent(new[] { "MYEONGDONG", "NAMSAN_WOODS", "GURO_FACTORY", "YONGSAN_MARKET", "GANGNAM_STREETS", "HAN_RIVER", "UIJEONGBU" },
                Buttons().Where(b => b.name.StartsWith("SelectMap_")).Select(b => b.name.Substring("SelectMap_".Length)).ToArray());
            AssertNoTeamOrDeparture();
        }

        [Test] public void RegionDetail_PrecedesTeamSelection_AndBackReturnsThroughEachStep()
        {
            AddArmedScav(); expedition.Refresh();
            Click("SelectMap_MYEONGDONG");
            Assert.IsTrue(HasObject("Map_MYEONGDONG"));
            Assert.IsTrue(HasButton("PrepareExpedition"));
            AssertNoTeamOrDeparture();
            Click("PrepareExpedition");
            Assert.IsTrue(HasButton("Pick_sc_navigation"));
            Assert.IsTrue(HasButton("OrientationDepart"));
            Assert.IsFalse(HasButton("Depart_MYEONGDONG"));
            Click("ExpeditionBack");
            Assert.IsTrue(HasObject("Map_MYEONGDONG"));
            Assert.IsTrue(HasButton("PrepareExpedition"));
            AssertNoTeamOrDeparture();
            Click("ExpeditionBack");
            Assert.IsTrue(HasButton("SelectMap_MYEONGDONG"));
            Assert.IsFalse(HasButton("PrepareExpedition"));
            AssertNoTeamOrDeparture();
        }

        [Test] public void NormalDeparture_UsesSelectedTeamAndRestoresRegionOverview()
        {
            var scav = AddArmedScav(); PrepareNormal();
            Assert.IsFalse(Button("Depart_MYEONGDONG").interactable);
            Assert.IsFalse(HasButton("OrientationDepart"));
            Click("Pick_" + scav.Uid);
            long money = session.Save.Player.Money;
            Click("Depart_MYEONGDONG");
            var run = session.Save.Expeditions.Single();
            Assert.AreEqual("MYEONGDONG", run.MapId);
            CollectionAssert.AreEqual(new[] { scav.Uid }, run.ScavUids);
            Assert.IsFalse(run.IsOrientation);
            Assert.Greater(run.CostPaid, 0);
            Assert.AreEqual(money - run.CostPaid, session.Save.Player.Money);
            Assert.AreEqual(ScavStatus.OnExpedition, scav.Status);
            Assert.IsTrue(HasButton("SelectMap_MYEONGDONG"));
            Assert.IsNotEmpty(Label("MapReturn_MYEONGDONG").text);
            AssertNoTeamOrDeparture();
        }

        [Test] public void FirstSupplyDeparture_RemainsFreeAndThreeMinutes()
        {
            var scav = AddArmedScav(); session.Save.Player.Money = 0; expedition.Refresh();
            Assert.AreEqual(OrientationStage.Pending, session.Save.Orientation.Stage);
            Click("SelectMap_MYEONGDONG"); Click("PrepareExpedition"); Click("Pick_" + scav.Uid);
            Assert.IsFalse(HasButton("Depart_MYEONGDONG"));
            Click("OrientationDepart");
            var run = session.Save.Expeditions.Single();
            Assert.IsTrue(run.IsOrientation);
            Assert.AreEqual(TimeSpan.FromMinutes(3), run.ReturnsAt - run.DepartedAt);
            Assert.AreEqual(0, run.CostPaid);
            Assert.AreEqual(0, session.Save.Player.Money);
            Assert.AreEqual(OrientationStage.Outbound, session.Save.Orientation.Stage);
            Assert.IsTrue(HasButton("SelectMap_MYEONGDONG"));
            AssertNoTeamOrDeparture();
        }

        [Test] public void SelectedRegionBecomingLocked_ResetsToOverview()
        {
            session.Save.Player.Level = 9; expedition.Refresh();
            Click("SelectMap_UIJEONGBU"); Click("PrepareExpedition");
            session.Save.Player.Level = 1; expedition.Refresh();
            Assert.IsTrue(HasButton("SelectMap_MYEONGDONG"));
            Assert.IsFalse(HasButton("SelectMap_UIJEONGBU"));
            Assert.IsFalse(HasButton("PrepareExpedition"));
            Assert.IsFalse(HasButton("ExpeditionBack"));
            AssertNoTeamOrDeparture();
        }

        [TestCase(ScavStatus.Working)]
        [TestCase(ScavStatus.OnExpedition)]
        public void SelectedScavLeavingIdle_IsPrunedAndNotReselectedOnReturn(ScavStatus status)
        {
            var scav = AddArmedScav(); PrepareNormal(); Click("Pick_" + scav.Uid);
            Assert.IsTrue(Button("Depart_MYEONGDONG").interactable);
            scav.Status = status; expedition.Refresh();
            Assert.IsFalse(HasButton("Pick_" + scav.Uid));
            Assert.IsFalse(Button("Depart_MYEONGDONG").interactable);
            scav.Status = ScavStatus.Idle; expedition.Refresh();
            Assert.IsTrue(HasButton("Pick_" + scav.Uid));
            Assert.IsFalse(Button("Depart_MYEONGDONG").interactable);
            Click("Pick_" + scav.Uid);
            Assert.IsTrue(Button("Depart_MYEONGDONG").interactable);
        }

        ExpeditionState AddRun(string uid, string map, int minutes, bool resolved = false)
        {
            var scav = AddArmedScav(uid);
            scav.Status = resolved ? ScavStatus.Idle : ScavStatus.OnExpedition;
            var run = new ExpeditionState { Uid = "run_" + uid, MapId = map,
                DepartedAt = session.Clock.UtcNow, ReturnsAt = session.Clock.UtcNow.AddMinutes(minutes),
                Resolved = resolved, ScavUids = new List<string> { uid } };
            session.Save.Expeditions.Add(run);
            return run;
        }
        Text Label(string name) => host.GetComponentsInChildren<Text>(true).Single(t => t.name == name);
        Transform Run(string name) => host.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);
        Text RunLabel(string run, string label) => Run(run).GetComponentsInChildren<Text>(true).Single(t => t.name == label);

        [Test] public void RegionReturnSummary_ShowsEarliestActiveReturnInKst_AndTicksRemainingTime()
        {
            AddRun("later", "MYEONGDONG", 20);
            var earliest = AddRun("earliest", "MYEONGDONG", 10);
            AddRun("resolved", "MYEONGDONG", 1, true);
            expedition.Refresh();
            string initial = Label("MapReturn_MYEONGDONG").text;
            StringAssert.Contains(Loc.Text("파견 {0}팀 · 복귀 {1}", 2, "09:10"), initial);
            StringAssert.DoesNotContain("09:01", initial);
            ((TestClock)session.Clock).Advance(TimeSpan.FromMinutes(2));
            expedition.Tick(.1f);
            string afterTick = Label("MapReturn_MYEONGDONG").text;
            StringAssert.Contains("09:10", afterTick);
            Assert.AreNotEqual(initial, afterTick, "The map's remaining time must advance without rebuilding the screen");
            earliest.Resolved = true; expedition.Refresh();
            StringAssert.Contains(Loc.Text("파견 {0}팀 · 복귀 {1}", 1, "09:20"), Label("MapReturn_MYEONGDONG").text);
        }

        [Test] public void RegionDetail_ShowsOnlyItsActiveTeams_WithReturnTimeAndLiveProgress()
        {
            AddRun("Alpha", "MYEONGDONG", 20);
            AddRun("Bravo", "MYEONGDONG", 40);
            AddRun("ResolvedCrew", "MYEONGDONG", 5, true);
            AddRun("OtherRegionCrew", "UIJEONGBU", 60);
            expedition.Refresh(); Click("SelectMap_MYEONGDONG");
            Assert.IsTrue(HasObject("Run0")); Assert.IsTrue(HasObject("Run1"));
            Assert.IsFalse(HasObject("Run2"));
            StringAssert.Contains("Alpha", RunLabel("Run0", "Map").text);
            StringAssert.Contains("Bravo", RunLabel("Run1", "Map").text);
            StringAssert.Contains("09:20", RunLabel("Run0", "ReturnAt").text);
            StringAssert.Contains("09:40", RunLabel("Run1", "ReturnAt").text);
            string initialProgress = RunLabel("Run0", "Progress").text;
            string initialRemaining = RunLabel("Run0", "Time").text;
            ((TestClock)session.Clock).Advance(TimeSpan.FromMinutes(10));
            expedition.Tick(.1f);
            Assert.AreNotEqual(initialRemaining, RunLabel("Run0", "Time").text);
            Assert.AreNotEqual(initialProgress, RunLabel("Run0", "Progress").text);
            StringAssert.Contains("50", RunLabel("Run0", "Progress").text);
            StringAssert.Contains("25", RunLabel("Run1", "Progress").text);
            StringAssert.Contains("09:20", RunLabel("Run0", "ReturnAt").text);
            Assert.IsTrue(HasObject("Map_MYEONGDONG"));
            AssertNoTeamOrDeparture();
        }
        [Test] public void Rescue_UsesRegionDetailAndTeamPreparationBeforeDispatch()
        {
            var rescuer = AddArmedScav();
            session.Save.Player.Money = 1000000;
            session.Save.Scavs.Add(new ScavState { Uid = "missing", Name = "Missing", Status = ScavStatus.Missing,
                LostAtMapId = "MYEONGDONG", LostAt = session.Clock.UtcNow.AddHours(-7), SignalAt = session.Clock.UtcNow.AddHours(-1) });
            expedition.Refresh();
            Assert.IsTrue(HasButton("SelectRescue_missing"));
            Assert.IsFalse(HasButton("RB_missing"));
            Click("SelectRescue_missing");
            Assert.IsTrue(HasObject("Map_MYEONGDONG"));
            AssertNoTeamOrDeparture();
            Click("PrepareExpedition");
            Assert.IsFalse(Button("RB_missing").interactable);
            Assert.IsFalse(HasButton("OrientationDepart"));
            Click("Pick_" + rescuer.Uid); Click("RB_missing");
            var run = session.Save.Expeditions.Single();
            Assert.AreEqual("missing", run.RescueScavUid);
            CollectionAssert.AreEqual(new[] { rescuer.Uid }, run.ScavUids);
            Assert.IsTrue(HasButton("SelectMap_MYEONGDONG"));
            AssertNoTeamOrDeparture();
        }
    }
}

