using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class RaidUsabilityTests
    {
        private IDataRegistry data;
        private GameSave save;
        [SetUp] public void Setup()
        {
            data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));
            save = new GameSave(); save.Player.EmployerNpcId = "HWANG";
            ExplorationSystem.PrepareStarter(save, data);
            Assert.IsTrue(ExplorationSystem.Start(save, data, "YONGSAN_MARKET", new[] { new ItemStack("AMO05", 40) }));
        }

        [Test] public void FullBagChoiceSurvivesReloadAndReplacementCannotDuplicateReward()
        {
            var run = save.Exploration;
            run.LootCapacity = 8;
            var items = data.AllItems.Where(i => i.Id != "MED05").Take(8).ToArray();
            foreach (var i in items) run.Loot.Add(new ItemStack(i.Id, 1));
            run.Phase = ExplorationPhase.LootChoice;
            run.LootOptions.Add(new ItemStack("MED05", 3));
            // Start may have generated unused choices; replace them explicitly.
            run.LootOptions = new List<ItemStack> { new ItemStack("MED05", 3) };
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, 0));
            Assert.AreEqual(1, run.PendingLoot.Count);
            Assert.IsFalse(ExplorationSystem.ResolvePendingLoot(save, true));
            var codec = new NewtonsoftJsonCodec(); save = codec.Deserialize<GameSave>(codec.Serialize(save));
            Assert.IsFalse(ExplorationSystem.ContinueEncounter(save));
            Assert.IsTrue(ExplorationSystem.DiscardLoot(save, items[0].Id));
            Assert.IsTrue(ExplorationSystem.ResolvePendingLoot(save, true));
            Assert.IsFalse(ExplorationSystem.ResolvePendingLoot(save, true));
            Assert.AreEqual(8, save.Exploration.Loot.Count);
            Assert.AreEqual(3, save.Exploration.Loot.Single(i => i.ItemId == "MED05").Count);
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(save));
        }

        [Test] public void FullBagMergesExistingTypeAndLeavingNewLootPreservesInventory()
        {
            var run = save.Exploration;
            foreach (var i in data.AllItems.Take(8)) run.Loot.Add(new ItemStack(i.Id, 1));
            string existing = run.Loot[0].ItemId;
            run.Phase = ExplorationPhase.LootChoice;
            run.LootOptions = new List<ItemStack> { new ItemStack(existing, 2) };
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save, 0));
            Assert.AreEqual(3, run.Loot[0].Count);
            Assert.IsEmpty(run.PendingLoot);
            run.PendingLoot.Add(new ItemStack("MED05", 2));
            Assert.IsTrue(ExplorationSystem.ResolvePendingLoot(save, false));
            Assert.AreEqual(8, run.Loot.Count);
            Assert.IsTrue(ExplorationSystem.ContinueEncounter(save));
        }

        [Test] public void GunFeedbackMatchesActualDamageAndCoverReportsReducedIncomingHit()
        {
            var run = save.Exploration;
            run.Phase = ExplorationPhase.Combat; run.Enemy = new ExplorationEnemy { Kind = "PMC", Name = "PMC", Hp = 500, MaxHp = 500 };
            Assert.IsTrue(ExplorationSystem.Attack(save, data, FireMode.Single));
            Assert.IsNotEmpty(run.PlayerFeedback);
            if (run.Enemy.Hp < 500) StringAssert.Contains("명중", run.PlayerFeedback);
            else StringAssert.Contains("빗나", run.PlayerFeedback);
            bool hit = false;
            for (int i=0;i<20 && !hit;i++) {
                run.Enemy.Action = EnemyAction.Aiming; run.Enemy.Remaining = .01;
                run.CoverCooldown = 0; save.Player.Hp = 100;
                Assert.IsTrue(ExplorationSystem.Cover(save));
                ExplorationSystem.Tick(save, data, .1);
                if (save.Player.Hp < 100) { hit = true; Assert.Less(100-save.Player.Hp, 6); StringAssert.Contains("80%", run.EnemyFeedback); }
            }
            Assert.IsTrue(hit);
        }

        [Test] public void EquippedBagAndRigIncreaseActualRaidCapacity()
        {
            var bag = data.AllItems.First(i => PlayerEquipment.SlotFor(i) == "Backpack" && i.GridSlots >= 4);
            var rig = data.AllItems.First(i => PlayerEquipment.SlotFor(i) == "TacticalRig" && i.GridSlots >= 8);
            save.Player.Equipment["Backpack"] = bag.Id; save.Player.Equipment["TacticalRig"] = rig.Id;
            Assert.AreEqual(8 + bag.GridSlots / 4 + rig.GridSlots / 8, RaidEquipment.LootCapacity(save, data));
        }
    }
}
