using AfterSeoul.Core;
using AfterSeoul.Exploration;
using NUnit.Framework;

namespace AfterSeoul.Tests
{
    public class RegionalFollowupQuestTests
    {
        static GameSave Introduced(string map = "GURO_FACTORY")
        {
            var save = new GameSave();
            save.SurvivedExplorationMapIds.AddRange(new[] { "YONGSAN_MARKET", "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS" });
            save.RegionalExplorationQuests[map] = new RegionalQuestProgress { Accepted = true, Completed = true };
            return save;
        }
        static void Return(GameSave save, string id, int node, int loot, ExplorationOutcome outcome = ExplorationOutcome.Success, string map = "GURO_FACTORY")
        {
            save.Exploration = new ExplorationState { Uid = id, MapId = map, NodeIndex = node, Result = new ExplorationResult { Id = id, Outcome = outcome } };
            if (loot > 0) save.Exploration.Loot.Add(new ItemStack("JUNK01", loot));
            RegionalExplorationQuest.OnSuccessfulReturn(save);
        }
        [TestCase("GURO_FACTORY")]
        [TestCase("HAN_RIVER")]
        [TestCase("NAMSAN_WOODS")]
        public void LegacyCompletedIntroductionCanOfferWithoutResetOrAutomaticAcceptance(string map)
        {
            var codec = new NewtonsoftJsonCodec();
            var save = codec.Deserialize<GameSave>("{\"SurvivedExplorationMapIds\":[\"YONGSAN_MARKET\",\"GURO_FACTORY\",\"HAN_RIVER\",\"NAMSAN_WOODS\"],\"RegionalExplorationQuests\":{\"" + map + "\":{\"Accepted\":true,\"ReadyToReport\":false,\"Completed\":true}}}");
            Assert.IsTrue(RegionalExplorationQuest.CanOfferFollowup(save, map));
            Assert.IsFalse(RegionalExplorationQuest.NeedsIntroduction(save, map));
            Assert.IsNull(RegionalExplorationQuest.FollowupProgress(save, map));
            Assert.IsTrue(RegionalExplorationQuest.AcceptFollowup(save, map));
            Assert.IsTrue(RegionalExplorationQuest.Progress(save, map).Completed);
            Assert.IsFalse(RegionalExplorationQuest.AcceptFollowup(save, map));
        }
        [TestCase(ExplorationOutcome.Death, 4, 2, "GURO_FACTORY", false)]
        [TestCase(ExplorationOutcome.Emergency, 4, 2, "GURO_FACTORY", false)]
        [TestCase(ExplorationOutcome.Exhausted, 4, 2, "GURO_FACTORY", false)]
        [TestCase(ExplorationOutcome.Success, 2, 2, "GURO_FACTORY", false)]
        [TestCase(ExplorationOutcome.Success, 4, 0, "GURO_FACTORY", false)]
        [TestCase(ExplorationOutcome.Success, 4, 2, "HAN_RIVER", false)]
        [TestCase(ExplorationOutcome.Success, 3, 1, "GURO_FACTORY", true)]
        public void AcceptedFollowupRequiresItsOwnRegionSurveyLootAndSuccessfulExtraction(ExplorationOutcome outcome, int node, int loot, string map, bool ready)
        {
            var save = Introduced();
            Assert.IsTrue(RegionalExplorationQuest.AcceptFollowup(save, "GURO_FACTORY"));
            Return(save, "new", node, loot, outcome, map);
            Assert.AreEqual(ready, RegionalExplorationQuest.FollowupProgress(save, "GURO_FACTORY").ReadyToReport);
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
        }
        [Test] public void TwoSeparateAcceptedRaidsAwardOnceAndSurviveSaveReload()
        {
            var save = Introduced();
            Return(save, "old", 5, 3);
            save.Exploration.Result.Acknowledged = true;
            Assert.IsTrue(RegionalExplorationQuest.AcceptFollowup(save, "GURO_FACTORY"));
            RegionalExplorationQuest.OnSuccessfulReturn(save);
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Return(save, "first", 3, 1);
            var readyCodec = new NewtonsoftJsonCodec();
            save = readyCodec.Deserialize<GameSave>(readyCodec.Serialize(save));
            save.Exploration.Result.Acknowledged = true;
            Assert.IsTrue(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Assert.AreEqual(20000, save.Player.Money);
            Assert.IsTrue(RegionalExplorationQuest.AcceptFollowup(save, "GURO_FACTORY"));
            var codec = new NewtonsoftJsonCodec();
            save = codec.Deserialize<GameSave>(codec.Serialize(save));
            RegionalExplorationQuest.OnSuccessfulReturn(save);
            Assert.IsFalse(RegionalExplorationQuest.FollowupProgress(save, "GURO_FACTORY").ReadyToReport);
            Return(save, "short", 3, 2);
            Assert.IsFalse(RegionalExplorationQuest.FollowupProgress(save, "GURO_FACTORY").ReadyToReport);
            Return(save, "empty", 4, 1);
            Assert.IsFalse(RegionalExplorationQuest.FollowupProgress(save, "GURO_FACTORY").ReadyToReport);
            Return(save, "second", 4, 2);
            save.Exploration.Result.Acknowledged = true;
            Assert.IsTrue(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
            Assert.IsFalse(RegionalExplorationQuest.AcceptFollowup(save, "GURO_FACTORY"));
            Assert.IsTrue(RegionalExplorationQuest.FollowupProgress(save, "GURO_FACTORY").Completed);
            Assert.IsTrue(RegionalExplorationQuest.Progress(save, "GURO_FACTORY").Completed);
            Assert.AreEqual(50000, save.Player.Money);
            Assert.AreEqual(2, save.NpcTrust["DONGDAEMUN_CHOI"]);
        }
        [Test] public void FollowupCannotBeAcceptedBeforeIntroductionOrDuringRaid()
        {
            Assert.IsFalse(RegionalExplorationQuest.AcceptFollowup(new GameSave(), "GURO_FACTORY"));
            Assert.IsFalse(RegionalExplorationQuest.CanOfferFollowup(Introduced("GANGNAM_STREETS"), "GANGNAM_STREETS"));
            var save = Introduced();
            save.Exploration = new ExplorationState { MapId = "GURO_FACTORY" };
            Assert.IsFalse(RegionalExplorationQuest.AcceptFollowup(save, "GURO_FACTORY"));
            Return(save, "unaccepted", 4, 2);
            save.Exploration.Result.Acknowledged = true;
            Assert.IsFalse(RegionalExplorationQuest.ReportFollowup(save, "GURO_FACTORY"));
        }
    }
}
