using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Factory;

namespace AfterSeoul.Tests
{
    public class GuidedInteractionTests
    {
        [Test]
        public void PickingWorkChangesTheInstructionOnlyAfterStarting()
        {
            var data = FakeRegistry.Build();
            var save = new GameSave();
            Assert.AreEqual("pick_work", Tutorial.ActionKey(save, data));
            var recipe = Workbench.AvailableRecipes(save, data)[0];
            Assert.IsTrue(Workbench.TryStart(save, data, recipe.Id));
            Assert.AreEqual("play_work", Tutorial.ActionKey(save, data));
            Assert.AreEqual("공장", Tutorial.ActionTab(Tutorial.ActionKey(save, data)));
            var restored = new NewtonsoftJsonCodec().Deserialize<GameSave>(new NewtonsoftJsonCodec().Serialize(save));
            Assert.AreEqual("play_work", Tutorial.ActionKey(restored, data));
        }

        [Test]
        public void UnarmedStarterIsSentToEquipmentBeforeDeparture()
        {
            var data = FakeRegistry.Build();
            var save = new GameSave { Orientation = new OrientationState { Stage = OrientationStage.Pending } };
            save.Scavs.Add(new ScavState { Uid = "starter", Status = ScavStatus.Idle });
            Assert.AreEqual("equip_first", Tutorial.ActionKey(save, data));
            Assert.AreEqual("인원", Tutorial.ActionTab(Tutorial.ActionKey(save, data)));
        }

        [Test]
        public void ReturnedCargoIsTheNextActionAndCanOnlyBeRewardedOnce()
        {
            var data = FakeRegistry.Build();
            var save = new GameSave { Orientation = new OrientationState {
                Stage = OrientationStage.ReadyToDeliver, HasCargo = true } };
            Assert.AreEqual("deliver_first", Tutorial.ActionKey(save, data));
            Assert.IsTrue(Orientation.Deliver(save));
            Assert.IsFalse(Orientation.Deliver(save));
            Assert.AreEqual(Orientation.DeliveryReward, save.Player.Money);
            Assert.IsNull(Tutorial.ActionKey(save, data));
        }

        [Test]
        public void BuyingUpgradePreservesAnUnfinishedManualGameAndChargesOnce()
        {
            var data = FakeRegistry.Build();
            var save = new GameSave();
            var recipe = Workbench.AvailableRecipes(save, data)[0];
            Assert.IsTrue(Workbench.TryStart(save, data, recipe.Id));
            var bench = save.Factory.Workbench;
            bench.Scores.Add(.85);
            long cost = Station.UpgradeCost(save, data);
            save.Player.Money = cost;
            Assert.IsTrue(Station.TryUpgrade(save, data));
            Assert.AreSame(bench, save.Factory.Workbench);
            Assert.AreEqual(recipe.Id, bench.RecipeId);
            Assert.AreEqual(.85, bench.Scores[0]);
            Assert.AreEqual(0, save.Player.Money);
            Assert.IsFalse(Station.TryUpgrade(save, data));
            Assert.AreEqual(2, save.Factory.StationLevel);
        }
    }
}
