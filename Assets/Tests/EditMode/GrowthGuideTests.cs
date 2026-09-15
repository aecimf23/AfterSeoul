using System;
using System.IO;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Scav;
using NUnit.Framework;
using UnityEngine;

namespace AfterSeoul.Tests
{
    public class GrowthGuideTests
    {
        [Test]
        public void AffordableArmedWorkerIsConsideredBeyondFirstRosterEntry()
        {
            var save = Save();
            save.ExploredMapIds.Add("MYEONGDONG");
            save.Scavs[0].Equipment.Clear();
            var ready = new ScavState { Uid = "ready", Status = ScavStatus.Idle };
            ready.Equipment[EquipSlot.Weapon] = "MEL01";
            save.Scavs.Add(ready);
            Assert.AreNotEqual(GrowthGoalKind.Equip, GrowthGuide.Current(save, _data).Kind);
            save.Scavs[0].Equipment[EquipSlot.Weapon] = "MEL01";
            save.Scavs[0].WagePerHour = 100000000;
            Assert.AreNotEqual(GrowthGoalKind.Earn, GrowthGuide.Current(save, _data).Kind);
        }

        [Test]
        public void ExploredRegionDoesNotReappearAfterRecentHistoryIsPruned()
        {
            var save = Save();
            var target = GrowthGuide.NextMap(save, _data);
            save.ExploredMapIds.Add(target.Id);
            save.Expeditions.Clear();
            Assert.AreNotEqual(target.Id, GrowthGuide.NextMap(save, _data)?.Id);
        }
        private IDataRegistry _data;
        [SetUp] public void Setup() =>
            _data = JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "Data", n)));

        private GameSave Save(string employer = "HWANG")
        {
            var save = new GameSave();
            save.Player.EmployerNpcId = employer;
            save.Player.Money = 50000;
            save.Orientation = new OrientationState { Stage = OrientationStage.Completed };
            var scav = new ScavState { Uid = "test", Status = ScavStatus.Idle };
            scav.Equipment[EquipSlot.Weapon] = "MEL01";
            save.Scavs.Add(scav);
            return save;
        }

        [TestCase("HWANG")]
        [TestCase("DR_CHOI")]
        [TestCase("YONGSAN_KIM")]
        public void SuggestedLockedMapDoesNotRequireAnotherEmployersTrust(string employer)
        {
            // Use actual employer ids through their data entries as well in the standalone suite.
            var save = Save(employer);
            var map = GrowthGuide.NextMap(save, _data);
            Assert.NotNull(map);
            if (map.Unlock.Type == "npcTrust") Assert.AreEqual(employer, map.Unlock.NpcId);
        }

        [Test]
        public void GearAlreadyOwnedIsNotSuggestedForRepurchase()
        {
            var save = Save();
            var goal = GrowthGuide.Current(save, _data);
            Assert.AreEqual(GrowthGoalKind.Equip, goal.Kind);
            save.Warehouse.Stacks.Add(new ItemStack { ItemId = goal.ItemId, Count = 1 });
            var owned = GrowthGuide.Current(save, _data);
            Assert.AreEqual(goal.ItemId, owned.ItemId);
            Assert.AreEqual(0, owned.Cost);
        }

        [Test]
        public void GuideIsReadOnlyAndWaitsForAnActualTrip()
        {
            var save = Save();
            save.Expeditions.Add(new ExpeditionState { MapId = "MYEONGDONG" });
            var codec = new NewtonsoftJsonCodec();
            string before = codec.Serialize(save);
            Assert.AreEqual(GrowthGoalKind.Wait, GrowthGuide.Current(save, _data).Kind);
            Assert.AreEqual(before, codec.Serialize(save));
        }

        [Test]
        public void InjuredRosterGetsRecoveryGuidance()
        {
            var save = Save();
            save.Scavs[0].Status = ScavStatus.Injured;
            Assert.AreEqual(GrowthGoalKind.Treat, GrowthGuide.Current(save, _data).Kind);
        }
    }
}
