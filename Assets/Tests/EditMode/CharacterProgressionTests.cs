using System;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Quest;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class CharacterProgressionTests
    {
        private IDataRegistry data;
        [SetUp] public void Setup() => data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));

        private GameSave Player()
        {
            var save = new GameSave();
            save.Player.EmployerNpcId = "HWANG";
            save.Player.CharacterExp = 195;
            save.Player.Exp = 1234;
            save.Player.Level = 7;
            return save;
        }

        [Test]
        public void SuccessfulExplorationAwardsCharacterExperienceOnceAndPersistsLevel()
        {
            var save = Player();
            ExplorationSystem.PrepareStarter(save, data);
            Assert.IsTrue(ExplorationSystem.Start(save, data, "YONGSAN_MARKET"));
            var run = save.Exploration;
            run.Phase = ExplorationPhase.Routes;
            run.AwaitingEntryChoice = false;
            run.NodeIndex = run.NodeCount - 1;
            Assert.IsTrue(ExplorationSystem.Extract(save, data));
            Assert.Greater(save.Player.CharacterExp, 195);
            Assert.Greater(save.Player.CharacterLevel, 1);
            Assert.AreEqual(save.Player.CharacterLevel, run.Result.CharacterLevel);
            long earned = save.Player.CharacterExp;
            Assert.IsFalse(ExplorationSystem.Extract(save, data));
            Assert.AreEqual(earned, save.Player.CharacterExp);
            var codec = new NewtonsoftJsonCodec();
            var restored = codec.Deserialize<GameSave>(codec.Serialize(save));
            Assert.AreEqual(earned, restored.Player.CharacterExp);
            Assert.AreEqual(save.Player.CharacterLevel, restored.Player.CharacterLevel);
            Assert.AreEqual(1234, save.Player.Exp);
            Assert.AreEqual(7, save.Player.Level);
        }

        [Test]
        public void FirstQuestReportRaisesCharacterLevelOnlyOnFirstCompletion()
        {
            var save = Player();
            Assert.IsTrue(FirstExplorationQuest.Accept(save));
            save.FirstExplorationQuest.ReadyToReport = true;
            Assert.IsTrue(FirstExplorationQuest.Report(save));
            Assert.Greater(save.Player.CharacterExp, 195);
            Assert.Greater(save.Player.CharacterLevel, 1);
            long earned = save.Player.CharacterExp;
            Assert.IsFalse(FirstExplorationQuest.Report(save));
            Assert.AreEqual(earned, save.Player.CharacterExp);
        }

        [Test]
        public void DailyDeliveryAwardsCharacterExperienceWithoutRepeatingReward()
        {
            var save = Player();
            var quest = data.GetQuestPool("DQP_HWANG").First(q => q.Requires.All(r => !string.IsNullOrEmpty(r.ItemId)) && q.RewardExp > 0);
            save.Quests.Active.Add(new ActiveQuest { QuestId = quest.Id });
            foreach (var item in quest.Requires) save.Warehouse.Stacks.Add(new ItemStack(item.ItemId, item.Count));
            var system = new DailyQuestSystem();
            Assert.IsTrue(system.TryDeliver(save, data, quest.Id));
            Assert.Greater(save.Player.CharacterExp, 195);
            Assert.Greater(save.Player.CharacterLevel, 1);
            Assert.AreEqual(1234 + quest.RewardExp, save.Player.Exp);
            long earned = save.Player.CharacterExp;
            Assert.IsFalse(system.TryDeliver(save, data, quest.Id));
            Assert.AreEqual(earned, save.Player.CharacterExp);
        }

        [Test]
        public void RegionalAndFollowupReportsAwardExperienceAtTheirOwnCompletion()
        {
            var save = Player();
            var progress = new RegionalQuestProgress { Accepted = true, ReadyToReport = true };
            save.RegionalExplorationQuests["GURO_FACTORY"] = progress;
            Assert.IsTrue(RegionalExplorationQuest.Report(save, "GURO_FACTORY"));
            Assert.Greater(save.Player.CharacterExp, 195);
            long regional = save.Player.CharacterExp;
            Assert.IsFalse(RegionalExplorationQuest.Report(save, "GURO_FACTORY"));
            Assert.AreEqual(regional, save.Player.CharacterExp);
            progress.Followup = new RegionalFollowupProgress { Accepted = true, ReadyToReport = true };
            Assert.IsTrue(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Assert.Greater(save.Player.CharacterExp, regional);
            long followup = save.Player.CharacterExp;
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Assert.AreEqual(followup, save.Player.CharacterExp);
        }

        [Test]
        public void InterruptedExplorationDoesNotAwardSurvivalExperience()
        {
            var save = Player();
            ExplorationSystem.PrepareStarter(save, data);
            Assert.IsTrue(ExplorationSystem.Start(save, data, "YONGSAN_MARKET"));
            Assert.IsTrue(ExplorationSystem.EmergencyReturn(save, data));
            Assert.AreEqual(195, save.Player.CharacterExp);
        }

        [Test]
        public void CurveHonorsBoundariesCapAndExistingLevels()
        {
            var balance = data.Balance;
            for (int level = 2; level <= CharacterProgression.Tuning(balance).Curve.MaxLevel; level++)
            {
                long boundary = CharacterProgression.ExpForLevel(level, balance);
                Assert.AreEqual(level - 1, CharacterProgression.LevelForExp(boundary - 1, balance));
                Assert.AreEqual(level, CharacterProgression.LevelForExp(boundary, balance));
            }
            var save = Player();
            save.Player.CharacterLevel = 7;
            CharacterProgression.Award(save, 10, balance);
            Assert.AreEqual(7, save.Player.CharacterLevel, "Existing levels must not decrease.");
            Assert.AreEqual(CharacterProgression.ExpForLevel(8, balance) - 205,
                CharacterProgression.ExpToNextLevel(save.Player, balance));
            Assert.AreEqual(0, CharacterProgression.ProgressInLevel(save.Player, balance));
            CharacterProgression.Award(save, long.MaxValue, balance);
            Assert.AreEqual(20, save.Player.CharacterLevel);
            Assert.AreEqual(0, CharacterProgression.ExpToNextLevel(save.Player, balance));
            Assert.AreEqual(1, CharacterProgression.ProgressInLevel(save.Player, balance));
            Assert.AreEqual(0, CharacterProgression.Award(save, 1, balance), "XP must not wrap at long.MaxValue.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FirstSurvivalAndQuestCreateAnEarlyLevelUp(bool intermediateExit)
        {
            var save = Player();
            save.Player.CharacterExp = 0;
            save.RngCounter = 85; // Earliest intermediate exit: three explored nodes.
            ExplorationSystem.PrepareStarter(save, data);
            FirstExplorationQuest.Accept(save);
            Assert.IsTrue(ExplorationSystem.Start(save, data, "YONGSAN_MARKET"));
            save.Exploration.Phase = ExplorationPhase.Routes;
            save.Exploration.AwaitingEntryChoice = false;
            save.Exploration.NodeIndex = intermediateExit ? save.Exploration.IntermediateExitIndex : save.Exploration.NodeCount - 1;
            save.Exploration.FirstQuestContainerSearched = true;
            ExplorationSystem.Extract(save, data);
            long raidExp = save.Exploration.Result.CharacterExpGained;
            Assert.AreEqual(CharacterProgression.SurvivalReward(save.Exploration.NodeIndex + 1, data.Balance), raidExp);
            ExplorationSystem.Acknowledge(save);
            Assert.IsTrue(FirstExplorationQuest.Report(save, data.Balance));
            Assert.AreEqual(raidExp + data.Balance.CharacterProgression.FirstQuestExp, save.Player.CharacterExp);
            Assert.AreEqual(2, save.Player.CharacterLevel);
        }
    }
}
