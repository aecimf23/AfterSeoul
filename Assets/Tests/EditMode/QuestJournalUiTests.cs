using System;
using System.IO;
using System.Linq;
using System.Reflection;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using AfterSeoul.Unity.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Tests
{
    public class QuestJournalUiTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        GameObject host; AppShell shell; GameSession session; MemoryFileStore files; TestClock clock;
        [SetUp] public void Setup()
        {
            IDataRegistry data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            files = new MemoryFileStore(); clock = new TestClock(DateTimeOffset.Parse("2026-09-18T01:00:00Z"));
            session = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), data, clock);
            session.Boot(); session.ChooseEmployer("HWANG"); session.Save.WelcomePage = -1;
            host = new GameObject("QuestUi"); host.SetActive(false); shell = host.AddComponent<AppShell>();
            typeof(AppShell).GetMethod("OnReady", Hidden).Invoke(shell, new object[] { session });
        }
        [TearDown] public void Cleanup() { UnityEngine.Object.DestroyImmediate(host); Tween.Clear(); }
        Button Find(string name) => host.GetComponentsInChildren<Button>(true).Last(b => b.name == name);
        Text Label(string name) => host.GetComponentsInChildren<Text>(true).Last(t => t.name == name);
        void Open() => Find("QuestJournal").onClick.Invoke();

        [Test] public void HomeExposesMainObjectiveRewardAndDailyEntryWithoutExpandingDetails()
        {
            Assert.IsNotEmpty(Label("TrackedObjective").text);
            Assert.IsNotEmpty(Label("TrackedReward").text);
            Assert.IsTrue(Find("QuestJournal").gameObject.activeSelf);
            Open();
            Assert.IsNotNull(Find("MainQuests")); Assert.IsNotNull(Find("DailyQuests"));
            Assert.IsNotEmpty(Label("QuestObjective").text); Assert.IsNotEmpty(Label("QuestReward").text);
        }

        [Test] public void MainReportPaysOnceAndImmediatelyOffersNextUnlockedGoal()
        {
            FirstExplorationQuest.Accept(session.Save);
            session.Save.FirstExplorationQuest.ReadyToReport = true;
            session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            long before = session.Save.Player.Money;
            Open(); Find("QuestAction_main:first").onClick.Invoke();
            Assert.IsTrue(session.Save.FirstExplorationQuest.Completed);
            Assert.AreEqual(before + 25000, session.Save.Player.Money);
            Assert.IsNotEmpty(Label("QuestReceipt").text);
            Assert.IsTrue(Find("QuestAction_main:GURO_FACTORY").interactable);
            Assert.IsFalse(host.GetComponentsInChildren<Button>(true).Any(b => b.name == "QuestAction_main:first" && b.interactable));
            Assert.IsFalse(host.GetComponentsInChildren<Button>(true).Any(b => b.name == "QuestAction_main:HAN_RIVER"));
        }

        [Test] public void MergedFollowupAppearsInJournalAndReportsOnlyOnce()
        {
            session.Save.FirstExplorationQuest.Completed=true;
            session.Save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            RegionalExplorationQuest.Accept(session.Save,"GURO_FACTORY");
            RegionalExplorationQuest.Progress(session.Save,"GURO_FACTORY").Completed=true;
            Assert.IsTrue(RegionalExplorationQuest.AcceptFollowup(session.Save,"GURO_FACTORY"));
            RegionalExplorationQuest.FollowupProgress(session.Save,"GURO_FACTORY").ReadyToReport=true;
            long before=session.Save.Player.Money;
            Open();Find("QuestAction_followup:GURO_FACTORY").onClick.Invoke();
            Assert.AreEqual(before+20000,session.Save.Player.Money);
            Assert.AreEqual(1,RegionalExplorationQuest.FollowupProgress(session.Save,"GURO_FACTORY").Stage);
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(session.Save,"GURO_FACTORY"));
        }
        [Test] public void DailyDeliveryShowsCountsRewardsAndUpdatesNextGoal()
        {
            var active = session.Save.Quests.Active.First();
            var def = session.Data.GetQuestPool(Employers.QuestPoolId(session.Data, "HWANG")).First(q => q.Id == active.QuestId);
            session.Save.Warehouse.Capacity = 999;
            foreach (var req in def.Requires) {
                string id = !string.IsNullOrEmpty(req.ItemId) ? req.ItemId : session.Data.AllItems.First(i => i.Tags.Contains(req.Tag)).Id;
                Warehouse.TryAdd(session.Save.Warehouse, session.Data, id, req.Count);
            }
            Open(); Find("DailyQuests").onClick.Invoke();
            Assert.IsNotEmpty(Label("QuestObjective").text);
            var button = Find("QuestAction_daily:" + def.Id); Assert.IsTrue(button.interactable);
            long before = session.Save.Player.Money; button.onClick.Invoke();
            Assert.AreEqual(before + def.RewardMoney, session.Save.Player.Money);
            Assert.IsTrue(session.Save.Quests.Active.First(q => q.QuestId == def.Id).Delivered);
            Assert.IsNotEmpty(Label("QuestReceipt").text);
            Assert.IsFalse(Find("QuestAction_daily:" + def.Id).interactable);
            typeof(AppShell).GetMethod("ActOnQuest", Hidden).Invoke(shell, new object[] { "daily:" + def.Id });
            Assert.AreEqual(before + def.RewardMoney, session.Save.Player.Money, "Repeated taps cannot grant another reward.");
        }

        [Test] public void TrackingSurvivesReloadAndExpiredDailyGoalFallsBackToCurrentQuest()
        {
            string id = "daily:" + session.Save.Quests.Active.First().QuestId;
            Open(); Find("DailyQuests").onClick.Invoke(); Find("TrackQuest_" + id).onClick.Invoke();
            Assert.AreEqual(id, session.Save.TrackedQuestId);
            var reloaded = new GameSession(new SaveService(files, new NewtonsoftJsonCodec(), clock), session.Data, clock);
            reloaded.Boot(); Assert.AreEqual(id, reloaded.Save.TrackedQuestId);
            session.Save.Quests.Active.Clear(); shell.AfterAction();
            StringAssert.Contains(Loc.Text("메인"), Label("TrackedTitle").text);
        }

        [Test] public void LoginOverviewOpensOnceAndWaitsForSettingsToClose()
        {
            typeof(AppShell).GetMethod("OpenSettings", Hidden).Invoke(shell, null);
            typeof(AppShell).GetMethod("MaybeShowStepPrompt", Hidden).Invoke(shell, null);
            Assert.IsFalse(host.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "QuestJournalWindow"));
            typeof(AppShell).GetMethod("CloseSettings", Hidden).Invoke(shell, null);
            typeof(AppShell).GetMethod("MaybeShowStepPrompt", Hidden).Invoke(shell, null);
            Assert.IsNotNull(Find("MainQuests"));
            typeof(AppShell).GetMethod("CloseQuestJournal", Hidden).Invoke(shell, new object[] { false });
            typeof(AppShell).GetMethod("MaybeShowStepPrompt", Hidden).Invoke(shell, null);
            Assert.IsFalse(host.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "QuestJournalWindow"));
        }

        [Test] public void PendingRaidCanBeResumedEvenWhenTrackingDailyQuest()
        {
            ExplorationSystem.PrepareStarter(session.Save, session.Data);
            session.Save.ExplorationTutorialSeen = 15;
            Assert.IsTrue(ExplorationSystem.Start(session.Save, session.Data, "YONGSAN_MARKET"));
            session.Save.TrackedQuestId = "daily:" + session.Save.Quests.Active.First().QuestId;
            shell.AfterAction();
            Find("TrackedQuestAction").onClick.Invoke();
            Assert.IsNotNull(host.GetComponentInChildren<ExplorationView>(true));
            Assert.IsTrue(session.Save.Exploration.AwaitingEntryChoice);
        }
    }
}
