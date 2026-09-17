using AfterSeoul.Core;
using AfterSeoul.Exploration;
using NUnit.Framework;

namespace AfterSeoul.Tests
{
    public class FirstExplorationQuestTests
    {
        static GameSave Save(string npc = "HWANG")
        {
            var save = new GameSave();
            save.Player.EmployerNpcId = npc;
            return save;
        }

        static void Raid(GameSave save, string map = "YONGSAN_MARKET")
        {
            save.Exploration = new ExplorationState { MapId = map, Phase = ExplorationPhase.Encounter };
        }

        static void Return(GameSave save, ExplorationOutcome outcome = ExplorationOutcome.Success)
        {
            save.Exploration.Phase = ExplorationPhase.Result;
            save.Exploration.Result = new ExplorationResult { MapId = save.Exploration.MapId, Outcome = outcome, Settled = true };
            FirstExplorationQuest.OnSuccessfulReturn(save);
        }

        [TestCase("HWANG", "Pocket")]
        [TestCase("DR_CHOI", "Medical")]
        [TestCase("YONGSAN_KIM", "Tool")]
        public void MatchingSalvageAndSurvivalPermitOneReportAfterResultAcknowledgment(string npc, string container)
        {
            var save = Save(npc);
            save.NpcTrust[npc] = 4;
            Assert.IsTrue(FirstExplorationQuest.IsPending(save));
            Assert.IsTrue(FirstExplorationQuest.Accept(save));
            Assert.IsFalse(FirstExplorationQuest.Accept(save));
            Assert.AreEqual(container, FirstExplorationQuest.RequiredContainer(save));
            Assert.IsFalse(FirstExplorationQuest.Report(save));
            Raid(save);
            FirstExplorationQuest.OnContainerLooted(save, container);
            Assert.IsTrue(save.Exploration.FirstQuestContainerSearched);
            Assert.IsFalse(save.FirstExplorationQuest.ReadyToReport);
            Return(save);
            Assert.IsTrue(save.FirstExplorationQuest.ReadyToReport);
            Assert.IsFalse(FirstExplorationQuest.Report(save));
            save.Exploration.Result.Acknowledged = true;
            Assert.IsTrue(FirstExplorationQuest.Report(save));
            Assert.AreEqual(25000, save.Player.Money);
            Assert.AreEqual(6, save.NpcTrust[npc]);
            Assert.AreEqual(0, save.Player.CharacterExp);
            Assert.IsEmpty(save.SurvivedExplorationMapIds);
            Assert.IsFalse(FirstExplorationQuest.IsPending(save));
            Assert.IsFalse(FirstExplorationQuest.Report(save));
            Assert.AreEqual(25000, save.Player.Money);
        }

        [Test] public void WrongContainerOrWrongMapDoesNotAdvanceQuest()
        {
            var save = Save("DR_CHOI");
            FirstExplorationQuest.Accept(save);
            Raid(save);
            FirstExplorationQuest.OnContainerLooted(save, "Tool");
            Assert.IsFalse(save.Exploration.FirstQuestContainerSearched);
            Return(save);
            Assert.IsFalse(save.FirstExplorationQuest.ReadyToReport);
            Raid(save, "GURO_FACTORY");
            FirstExplorationQuest.OnContainerLooted(save, "Medical");
            Return(save);
            Assert.IsFalse(save.FirstExplorationQuest.ReadyToReport);
        }

        [TestCase(ExplorationOutcome.Death)]
        [TestCase(ExplorationOutcome.Emergency)]
        [TestCase(ExplorationOutcome.Exhausted)]
        public void FailedReturnKeepsQuestAcceptedAndRequiresSearchOnRetry(ExplorationOutcome outcome)
        {
            var save = Save();
            FirstExplorationQuest.Accept(save);
            Raid(save);
            FirstExplorationQuest.OnContainerLooted(save, "Pocket");
            Return(save, outcome);
            Assert.IsFalse(save.FirstExplorationQuest.ReadyToReport);
            Assert.IsTrue(save.FirstExplorationQuest.Accepted);
            Raid(save);
            Assert.IsFalse(save.Exploration.FirstQuestContainerSearched);
            Return(save);
            Assert.IsFalse(save.FirstExplorationQuest.ReadyToReport);
            Raid(save);
            FirstExplorationQuest.OnContainerLooted(save, "Pocket");
            Return(save);
            Assert.IsTrue(save.FirstExplorationQuest.ReadyToReport);
        }

        [Test] public void AcceptanceRequiresChosenEmployerAndNoUnfinishedDirectExploration()
        {
            Assert.IsFalse(FirstExplorationQuest.Accept(Save(null)));
            Assert.IsFalse(FirstExplorationQuest.IsPending(Save("OTHER")));
            var save = Save();
            Raid(save);
            FirstExplorationQuest.OnContainerLooted(save, "Pocket");
            Assert.IsFalse(save.Exploration.FirstQuestContainerSearched);
            Assert.IsFalse(FirstExplorationQuest.Accept(save));
            Return(save);
            Assert.IsFalse(FirstExplorationQuest.Accept(save));
            save.Exploration.Result.Acknowledged = true;
            save.Expeditions.Add(new ExpeditionState { Resolved = false });
            Assert.IsTrue(FirstExplorationQuest.Accept(save));
        }

        [Test] public void ReloadPreservesSearchAndCompletionWithoutRepeatingReward()
        {
            var codec = new NewtonsoftJsonCodec();
            var save = Save("YONGSAN_KIM");
            FirstExplorationQuest.Accept(save);
            Raid(save);
            FirstExplorationQuest.OnContainerLooted(save, "Tool");
            save = codec.Deserialize<GameSave>(codec.Serialize(save));
            Return(save);
            save.Exploration.Result.Acknowledged = true;
            save = codec.Deserialize<GameSave>(codec.Serialize(save));
            Assert.IsTrue(FirstExplorationQuest.Report(save));
            save = codec.Deserialize<GameSave>(codec.Serialize(save));
            Assert.IsFalse(FirstExplorationQuest.Report(save));
            Assert.AreEqual(25000, save.Player.Money);
            Assert.AreEqual(2, save.NpcTrust["YONGSAN_KIM"]);
        }

        [Test] public void EachEmployerHasDistinctOfferObjectiveAndReport()
        {
            var titles = new System.Collections.Generic.HashSet<string>();
            var offers = new System.Collections.Generic.HashSet<string>();
            var objectives = new System.Collections.Generic.HashSet<string>();
            var reports = new System.Collections.Generic.HashSet<string>();
            foreach (var npc in new[] { "HWANG", "DR_CHOI", "YONGSAN_KIM" })
            {
                var save = Save(npc);
                titles.Add(FirstExplorationQuest.Title(save));
                offers.Add(FirstExplorationQuest.Offer(save));
                objectives.Add(FirstExplorationQuest.Objective(save));
                reports.Add(FirstExplorationQuest.ReportLine(save));
            }
            Assert.AreEqual(3, titles.Count);
            Assert.AreEqual(3, offers.Count);
            Assert.AreEqual(3, objectives.Count);
            Assert.AreEqual(3, reports.Count);
        }
    }
}
