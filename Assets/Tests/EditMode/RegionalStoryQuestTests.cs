using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public sealed class RegionalStoryQuestTests
    {
        private IDataRegistry _data;

        [SetUp]
        public void SetUp() => _data = JsonDataRegistry.Load(name =>
            File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", name)));

        private static GameSave Eligible(string map)
        {
            var save = new GameSave();
            save.Player.EmployerNpcId = "HWANG";
            save.FirstExplorationQuest.Completed = true;
            save.SurvivedExplorationMapIds.AddRange(ExplorationSystem.MainRoute);
            if (map != "YONGSAN_MARKET")
                save.RegionalExplorationQuests[map] = new RegionalQuestProgress { Accepted = true };
            return save;
        }

        private void StartAtStorySite(GameSave save, string map)
        {
            if (!save.ExplorationStarterPrepared) Assert.IsTrue(ExplorationSystem.PrepareStarter(save, _data));
            Assert.IsTrue(ExplorationSystem.Start(save, _data, map));
            Assert.AreEqual(RegionalStoryQuestCatalog.Find(map).Site, save.Exploration.Routes[0]);
            Assert.IsTrue(ExplorationSystem.Move(save, _data, 0));
            Assert.IsTrue(save.Exploration.StorySiteVisited);
        }

        private void ExtractAtFirstExit(GameSave save)
        {
            var run = save.Exploration;
            run.NodeIndex = 2;
            run.IntermediateExitIndex = 2;
            run.Phase = ExplorationPhase.Routes;
            run.AwaitingEntryChoice = false;
            Assert.IsTrue(ExplorationSystem.Extract(save, _data));
            Assert.AreEqual(ExplorationOutcome.Success, run.Result.Outcome);
        }

        [Test]
        public void AvailabilityRequiresFirstReportOpenRouteAndRegionalIntroduction()
        {
            var save = new GameSave();
            save.Player.EmployerNpcId = "HWANG";
            Assert.IsFalse(RegionalStoryQuest.CanOffer(save, "YONGSAN_MARKET"));
            save.FirstExplorationQuest.Completed = true;
            Assert.IsTrue(RegionalStoryQuest.CanOffer(save, "YONGSAN_MARKET"));
            Assert.IsFalse(RegionalStoryQuest.CanOffer(save, "GURO_FACTORY"));
            save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");
            Assert.IsFalse(RegionalStoryQuest.CanOffer(save, "GURO_FACTORY"));
            save.RegionalExplorationQuests["GURO_FACTORY"] = new RegionalQuestProgress { Accepted = true };
            Assert.IsTrue(RegionalStoryQuest.CanOffer(save, "GURO_FACTORY"));
            Assert.IsFalse(RegionalStoryQuest.CanOffer(save, "UIJEONGBU"));
        }

        [Test]
        public void EveryAcceptedStoryForcesItsActualSiteIntoTheFirstSafeRoute()
        {
            Assert.AreEqual(8, RegionalStoryQuestCatalog.All.Length);
            foreach (var def in RegionalStoryQuestCatalog.All)
            {
                var save = Eligible(def.Map);
                Assert.IsTrue(RegionalStoryQuest.Accept(save, def.Map));
                StartAtStorySite(save, def.Map);
            }
        }

        [Test]
        public void ScoutNeedsActualSiteVisitDepthAndSuccessfulReturnBeforeOneReport()
        {
            var save = Eligible("YONGSAN_MARKET");
            Assert.IsTrue(RegionalStoryQuest.Accept(save, "YONGSAN_MARKET"));
            StartAtStorySite(save, "YONGSAN_MARKET");
            Assert.IsTrue(ExplorationSystem.EmergencyReturn(save, _data));
            Assert.IsFalse(RegionalStoryQuest.Progress(save, "YONGSAN_MARKET").ReadyToReport);
            Assert.IsTrue(ExplorationSystem.Acknowledge(save));
            Assert.IsTrue(ExplorationSystem.Start(save, _data, "YONGSAN_MARKET"));
            Assert.IsTrue(ExplorationSystem.Move(save, _data, 1));
            Assert.IsFalse(save.Exploration.StorySiteVisited);
            ExtractAtFirstExit(save);
            Assert.IsFalse(RegionalStoryQuest.Progress(save, "YONGSAN_MARKET").ReadyToReport);
            Assert.IsTrue(ExplorationSystem.Acknowledge(save));
            StartAtStorySite(save, "YONGSAN_MARKET");
            ExtractAtFirstExit(save);
            var progress = RegionalStoryQuest.Progress(save, "YONGSAN_MARKET");
            Assert.IsTrue(progress.ReadyToReport);
            Assert.IsFalse(RegionalStoryQuest.Report(save, "YONGSAN_MARKET", _data), "Unacknowledged raid results must be confirmed first.");
            Assert.IsTrue(ExplorationSystem.Acknowledge(save));
            Assert.IsTrue(RegionalStoryQuest.Report(save, "YONGSAN_MARKET", _data));
            Assert.AreEqual(1, progress.Stage);
            Assert.AreEqual(8000, save.Player.Money);
            Assert.IsFalse(RegionalStoryQuest.Report(save, "YONGSAN_MARKET", _data));
        }

        [Test]
        public void RecoveryGuaranteesOneNormalChoiceAndReservesOnlyCarriedLoot()
        {
            const string map = "MYEONGDONG";
            var save = Eligible(map);
            save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress { Stage = 1 };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, map));
            StartAtStorySite(save, map);
            string itemId = RegionalStoryQuestCatalog.Find(map).ItemId;
            Assert.IsTrue(save.Exploration.LootOptions.Any(x => x.ItemId == itemId));
            int index = save.Exploration.LootOptions.FindIndex(x => x.ItemId == itemId);
            save.Exploration.Phase = ExplorationPhase.LootChoice;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, index));
            Assert.IsTrue(save.Exploration.StoryItemCollected);
            save.Warehouse.Capacity = 0;
            ExtractAtFirstExit(save);
            var progress = RegionalStoryQuest.Progress(save, map);
            Assert.IsTrue(progress.ReadyToReport);
            Assert.AreEqual(itemId, progress.HeldItemId);
            Assert.AreEqual(itemId, save.Exploration.Result.StoryQuestItemId);
            Assert.AreEqual(0, Warehouse.CountOf(save.Warehouse, itemId));
            Assert.IsFalse(save.ExplorationOverflow.Any(x => x.ItemId == itemId));
            Assert.IsTrue(ExplorationSystem.Acknowledge(save));
            Assert.IsTrue(RegionalStoryQuest.Report(save, map, _data));
            Assert.AreEqual(2, progress.Stage);
            Assert.IsNull(progress.HeldItemId);
            Assert.AreEqual(12000, save.Player.Money);
            Assert.IsFalse(RegionalStoryQuest.Report(save, map, _data));
        }

        [Test]
        public void RecoveryRejectsPackedSupplyWithoutARealSitePickup()
        {
            const string map = "NAMSAN_WOODS";
            var save = Eligible(map);
            save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress { Stage = 1 };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, map));
            Assert.IsTrue(ExplorationSystem.PrepareStarter(save, _data));
            string itemId = RegionalStoryQuestCatalog.Find(map).ItemId;
            save.Warehouse.Stacks.Add(new ItemStack(itemId, 1));
            Assert.IsTrue(ExplorationSystem.Start(save, _data, map, new[] { new ItemStack(itemId, 1) }));
            Assert.IsTrue(ExplorationSystem.Move(save, _data, 0));
            ExtractAtFirstExit(save);
            Assert.IsFalse(RegionalStoryQuest.Progress(save, map).ReadyToReport, "Packing the same item is not finding it at the site.");
            Assert.IsTrue(ExplorationSystem.Acknowledge(save));

        }

        [Test]
        public void DiscardingSiteItemThenFindingSameIdElsewhereDoesNotCompleteRecovery()
        {
            const string map = "YONGSAN_MARKET";
            var save = Eligible(map);
            save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress { Stage = 1 };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, map));
            StartAtStorySite(save, map);
            var run = save.Exploration;
            string itemId = RegionalStoryQuestCatalog.Find(map).ItemId;
            run.Phase = ExplorationPhase.LootChoice;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, run.LootOptions.FindIndex(x => x.ItemId == itemId)));
            Assert.IsTrue(run.StoryItemCollected);
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(save));
            Assert.IsTrue(ExplorationSystem.DiscardLoot(save, itemId));
            Assert.IsFalse(run.StoryItemCollected, "Discarding the last site-sourced item must clear its provenance.");
            run.Routes[0] = "1층 전자 매장";
            run.RouteContainers[0] = "Tool";
            Assert.IsTrue(ExplorationSystem.Move(save, _data, 0));
            Assert.AreEqual("1층 전자 매장", run.Location);
            run.LootOptions = new List<ItemStack> { new ItemStack(itemId, 1) };
            run.Phase = ExplorationPhase.LootChoice;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, 0));
            ExtractAtFirstExit(save);
            Assert.IsFalse(RegionalStoryQuest.Progress(save, map).ReadyToReport);
            Assert.IsNull(run.Result.StoryQuestItemId);
            Assert.AreEqual(1, Warehouse.CountOf(save.Warehouse, itemId));
        }

        [Test]
        public void ConsumingTheLastSiteFoodClearsItsProvenance()
        {
            const string map = "NAMSAN_WOODS";
            var save = Eligible(map);
            save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress { Stage = 1 };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, map));
            StartAtStorySite(save, map);
            var run = save.Exploration;
            string itemId = RegionalStoryQuestCatalog.Find(map).ItemId;
            run.Phase = ExplorationPhase.LootChoice;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, run.LootOptions.FindIndex(x => x.ItemId == itemId)));
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(save));
            Assert.IsTrue(ExplorationSystem.Use(save, _data, itemId));
            ExplorationSystem.Tick(save, _data, 30);
            Assert.IsFalse(run.StoryItemCollected);
            Assert.IsFalse(run.Loot.Any(x => x.ItemId == itemId));
        }

        [Test]
        public void DecliningPendingSiteItemClearsItsProvenance()
        {
            const string map = "YONGSAN_MARKET";
            var save = Eligible(map);
            save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress { Stage = 1 };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, map));
            StartAtStorySite(save, map);
            var run = save.Exploration;
            run.LootCapacity = 8;
            for (int i = 0; i < 8; i++) run.Loot.Add(new ItemStack("JUNK" + (i + 1).ToString("00"), 1));
            string itemId = RegionalStoryQuestCatalog.Find(map).ItemId;
            run.Phase = ExplorationPhase.LootChoice;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, run.LootOptions.FindIndex(x => x.ItemId == itemId)));
            Assert.IsTrue(run.StoryItemCollected);
            Assert.IsTrue(run.PendingLoot.Any(x => x.ItemId == itemId));
            Assert.IsTrue(ExplorationSystem.ResolvePendingLoot(save, false));
            Assert.IsFalse(run.StoryItemCollected);
            Assert.IsFalse(run.PendingLoot.Any(x => x.ItemId == itemId));
        }

        [Test]
        public void ResultAndHeldItemPersistWithoutRegrantingOnLaterRuns()
        {
            const string map = "GURO_FACTORY";
            var save = Eligible(map);
            save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress { Stage = 1 };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, map));
            StartAtStorySite(save, map);
            string itemId = RegionalStoryQuestCatalog.Find(map).ItemId;
            save.Exploration.Phase = ExplorationPhase.LootChoice;
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, save.Exploration.LootOptions.FindIndex(x => x.ItemId == itemId)));
            ExtractAtFirstExit(save);
            var codec = new NewtonsoftJsonCodec();
            save = codec.Deserialize<GameSave>(codec.Serialize(save));
            Assert.AreEqual(itemId, save.Exploration.Result.StoryQuestItemId);
            Assert.AreEqual(itemId, RegionalStoryQuest.Progress(save, map).HeldItemId);
            Assert.IsTrue(ExplorationSystem.Acknowledge(save));
            Assert.IsTrue(ExplorationSystem.Start(save, _data, map));
            Assert.IsTrue(ExplorationSystem.Move(save, _data, 0));
            ExtractAtFirstExit(save);
            Assert.AreEqual(itemId, RegionalStoryQuest.Progress(save, map).HeldItemId);
            Assert.IsNull(save.Exploration.Result.StoryQuestItemId);
        }

        [Test]
        public void AcceptExcludesTheAlreadyAcknowledgedResultAndLegacySaveCanStart()
        {
            var codec = new NewtonsoftJsonCodec();
            var save = codec.Deserialize<GameSave>("{\"FirstExplorationQuest\":{\"Completed\":true},\"Player\":{\"EmployerNpcId\":\"HWANG\"}}");
            Assert.IsNull(RegionalStoryQuest.Progress(save, "YONGSAN_MARKET"));
            save.Exploration = new ExplorationState {
                Uid = "old-run", MapId = "YONGSAN_MARKET", NodeIndex = 2, StorySiteVisited = true,
                Phase = ExplorationPhase.Result,
                Result = new ExplorationResult { Id = "old-result", Outcome = ExplorationOutcome.Success, Acknowledged = true }
            };
            Assert.IsTrue(RegionalStoryQuest.Accept(save, "YONGSAN_MARKET"));
            RegionalStoryQuest.OnSuccessfulReturn(save);
            Assert.IsFalse(RegionalStoryQuest.Progress(save, "YONGSAN_MARKET").ReadyToReport);
        }

        [Test]
        public void FailedReportSaveKeepsReservedItemAndRewardRetryPaysOnce()
        {
            const string map = "MYEONGDONG";
            var files = new RejectableFiles();
            var clock = new TestClock(DateTimeOffset.Parse("2026-09-18T00:00:00Z"));
            var service = new SaveService(files, new NewtonsoftJsonCodec(), clock);
            var session = new GameSession(service, _data, clock);
            session.Boot();
            session.Save.Player.Money = 0;
            session.Save.RegionalStoryQuests[map] = new RegionalStoryQuestProgress {
                Stage = 1, Accepted = true, ReadyToReport = true, HeldItemId = "JUNK34"
            };
            session.Commit();
            files.FailReplace = true;
            Assert.Throws<IOException>(() => session.ExecuteSavedAction(save => RegionalStoryQuest.Report(save, map, _data)));
            var progress = RegionalStoryQuest.Progress(session.Save, map);
            Assert.AreEqual(1, progress.Stage);
            Assert.AreEqual("JUNK34", progress.HeldItemId);
            Assert.AreEqual(0, session.Save.Player.Money);
            files.FailReplace = false;
            Assert.IsTrue(session.ExecuteSavedAction(save => RegionalStoryQuest.Report(save, map, _data)));
            Assert.AreEqual(12000, session.Save.Player.Money);
            Assert.IsNull(RegionalStoryQuest.Progress(session.Save, map).HeldItemId);
            var restored = new GameSession(service, _data, clock);
            restored.Boot();
            Assert.AreEqual(12000, restored.Save.Player.Money);
            Assert.IsFalse(RegionalStoryQuest.Report(restored.Save, map, _data));
        }

        private sealed class RejectableFiles : IFileStore
        {
            private readonly Dictionary<string, string> _files = new Dictionary<string, string>();
            public bool FailReplace;
            public bool Exists(string path) => _files.ContainsKey(path);
            public string ReadAllText(string path) => _files[path];
            public void WriteAllText(string path, string content) => _files[path] = content;
            public void Replace(string source, string destination)
            {
                if (FailReplace) throw new IOException("The test store rejected the save.");
                _files[destination] = _files[source];
                _files.Remove(source);
            }
            public bool TryMove(string source, string destination)
            {
                if (!_files.ContainsKey(source)) return false;
                _files[destination] = _files[source];
                _files.Remove(source);
                return true;
            }
        }
    }
}
