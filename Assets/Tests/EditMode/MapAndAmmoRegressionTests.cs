using System;
using System.IO;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Inventory;
using AfterSeoul.Exploration;
using AfterSeoul.Expedition;
namespace AfterSeoul.Tests {
 public class MapAndAmmoRegressionTests {
  JsonDataRegistry Load() => JsonDataRegistry.Load(n => File.ReadAllText(Path.Combine("Assets/StreamingAssets/Data", n)));
  [Test] public void AmmoPriceUsesRoundsAndSalePaysDisplayedTotal() {
   var d=Load(); var s=new GameSave(); var ammo=d.GetItem("AMO05");
   Assert.AreEqual(20,ItemPricing.UnitValue(ammo));
   long price=Market.SellPrice(s,d,ammo.Id); Assert.AreEqual((long)Math.Floor(20 * d.Balance.SellPriceRatio),price);
   Warehouse.TryAdd(s.Warehouse,d,ammo.Id,50); long before=s.Player.Money;
   Assert.IsTrue(Market.TrySell(s,d,ammo.Id,50)); Assert.AreEqual(before+price*50,s.Player.Money);
   Assert.AreEqual(d.GetItem("WPN04").BasePrice,ItemPricing.UnitValue(d.GetItem("WPN04")));
  }
  [Test] public void EntryIsSafeUntilRouteSelectedAcrossSeedsAndReload() {
   var d=Load();
   for(uint seed=1;seed<=100;seed++) {
    var s=new GameSave(); s.Player.Equipment["Melee"]="MEL01"; s.RngCounter=seed;
    Assert.IsTrue(ExplorationSystem.Start(s,d,"YONGSAN_MARKET"));
    var codec=new NewtonsoftJsonCodec(); s=codec.Deserialize<GameSave>(codec.Serialize(s));
    double hp=s.Player.Hp; ExplorationSystem.Tick(s,d,120);
    Assert.AreEqual(hp,s.Player.Hp); Assert.IsNull(s.Exploration.Enemy);
    Assert.AreEqual(ExplorationPhase.Routes,s.Exploration.Phase); Assert.IsTrue(s.Exploration.AwaitingEntryChoice);
    Assert.AreEqual(2,s.Exploration.Routes.Length); Assert.IsFalse(ExplorationSystem.CanExtract(s.Exploration));
    Assert.IsTrue(ExplorationSystem.Move(s,d,0)); Assert.IsFalse(s.Exploration.AwaitingEntryChoice); Assert.AreEqual(0,s.Exploration.NodeIndex);
   }
  }
  [Test] public void DispatchUsesSameUnlockSequenceAsDirectExploration() {
   var d=Load(); var s=new GameSave();
   foreach(var m in ExplorationSystem.OrderedMaps(d)) Assert.AreEqual(m.Id=="YONGSAN_MARKET",MapUnlock.IsUnlocked(s,m),m.Id);
   foreach(var id in ExplorationSystem.MainRoute) s.SurvivedExplorationMapIds.Add(id);
   foreach(var m in ExplorationSystem.OrderedMaps(d)) Assert.IsTrue(MapUnlock.IsUnlocked(s,m),m.Id);
  }
 }
}
