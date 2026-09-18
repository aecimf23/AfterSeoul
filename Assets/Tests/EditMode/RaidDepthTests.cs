using System;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class RaidDepthTests
    {
        private IDataRegistry data;
        [SetUp] public void Setup() { data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n))); }
        [Test] public void EveryHumanContactWaitsForAnExplicitChoiceBeforeCombat()
        {
            int contacts = 0;
            for (uint seed=1;seed<=60;seed++) {
                var save = new GameSave { RngCounter = seed }; save.Player.EmployerNpcId = "HWANG";
                ExplorationSystem.PrepareStarter(save,data); save.FirstExplorationQuest.Completed = true;
                Assert.IsTrue(ExplorationSystem.Start(save,data,"YONGSAN_MARKET"));
                Assert.IsTrue(ExplorationSystem.Move(save,data,0));
                if (save.Exploration.Enemy == null) continue;
                contacts++;
                Assert.AreEqual(ExplorationPhase.Encounter, save.Exploration.Phase, "seed " + seed);
                double hp=save.Player.Hp;
                ExplorationSystem.Tick(save,data,30);
                Assert.AreEqual(hp,save.Player.Hp);
                Assert.AreEqual(ExplorationPhase.Encounter,save.Exploration.Phase);
            }
            Assert.Greater(contacts,10);
        }
    }
}
