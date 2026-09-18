using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using NUnit.Framework;
using UnityEngine;
namespace AfterSeoul.Tests
{
 public class RaidEconomySimulationTests
 {
  [Test] public void CautiousStarterRunsCanSurviveAndPayForConsumedSupplies()
  {
   var data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
   var net=new List<long>();int survived=0,combat=0;
   for(uint seed=1;seed<=100;seed++) {
    var s=new GameSave{RngCounter=seed};s.Player.EmployerNpcId="HWANG";ExplorationSystem.PrepareStarter(s,data);s.FirstExplorationQuest.Completed=true;
    Warehouse.TryAdd(s.Warehouse,data,"FOOD02",3);
    var pack=new[]{new ItemStack("AMO05",50),new ItemStack("MED05",2),new ItemStack("FOOD01",2),new ItemStack("FOOD02",3)};
    long before=s.Warehouse.Stacks.Sum(x=>Market.SellPrice(s,data,x.ItemId)*x.Count);
    Assert.IsTrue(ExplorationSystem.Start(s,data,"YONGSAN_MARKET",pack));
    int budget=4000;
    while(ExplorationSystem.IsActive(s) && budget-->0) {
     var e=s.Exploration;
     if(e.UseRemaining>0){ExplorationSystem.Tick(s,data,.1);continue;}
     bool rest=e.Phase==ExplorationPhase.Routes || e.Phase==ExplorationPhase.Combat && (e.Enemy.Action==EnemyAction.Cover || e.Enemy.Action==EnemyAction.Reloading);
     if(rest && ((s.Player.Hp<55 && ExplorationSystem.Use(s,data,"MED05")) || (s.Player.Energy<30 && ExplorationSystem.Use(s,data,"FOOD01")) || (s.Player.Hydration<30 && ExplorationSystem.Use(s,data,"FOOD02"))))continue;
     switch(e.Phase) {
      case ExplorationPhase.Routes:
       if(ExplorationSystem.CanExtract(e)) Assert.IsTrue(ExplorationSystem.Extract(s,data));
       else if(!ExplorationSystem.Move(s,data,0)) ExplorationSystem.EmergencyReturn(s,data);
       break;
      case ExplorationPhase.Encounter:
       if(e.Enemy==null) ExplorationSystem.Choose(s,data,EncounterChoice.Search);
       else if(!ExplorationSystem.Choose(s,data,EncounterChoice.Avoid)) ExplorationSystem.Choose(s,data,EncounterChoice.Fight);
       if(e.Phase==ExplorationPhase.Combat)combat++;
       break;
      case ExplorationPhase.LootChoice: ExplorationSystem.ChooseLoot(s,0);break;
      case ExplorationPhase.EncounterResult:
       while(e.PendingLoot.Count>0)ExplorationSystem.ResolvePendingLoot(s,ExplorationSystem.CanCarry(e,e.PendingLoot[0].ItemId));
       Assert.IsTrue(ExplorationSystem.ContinueEncounter(s));break;
      case ExplorationPhase.Combat:
       if(e.Enemy.Action==EnemyAction.Grenade||e.Enemy.Action==EnemyAction.Rush)ExplorationSystem.Dodge(s);
       if(e.Enemy.Action==EnemyAction.Aiming && e.Enemy.Remaining<1.3)ExplorationSystem.Cover(s);
       if(e.Enemy.Action!=EnemyAction.Cover) {
        if(!ExplorationSystem.Attack(s,data,FireMode.Single) && ExplorationSystem.AmmoRemaining(s)==0)ExplorationSystem.Melee(s,data);
       }
       ExplorationSystem.Tick(s,data,.1);break;
     }
    }
    Assert.Greater(budget,0,"stalled seed "+seed);
    if(s.Exploration.Result.Outcome==ExplorationOutcome.Success)survived++;
    long after=s.Warehouse.Stacks.Sum(x=>Market.SellPrice(s,data,x.ItemId)*x.Count)+s.ExplorationOverflow.Sum(x=>Market.SellPrice(s,data,x.ItemId)*x.Count);
    // Charge replacement stock at shop prices instead of sale values (10% normal markup).
    long replacementMarkup=pack.Sum(x=>Math.Max(0,x.Count-Warehouse.CountOf(s.Warehouse,x.ItemId))*(RaidProgression.SupplyPrice(s,data,x.ItemId,1)-Market.SellPrice(s,data,x.ItemId)));
    net.Add(after-before-replacementMarkup);
   }
   net.Sort();TestContext.Out.WriteLine("Cautious Yongsan seeds=100 survived="+survived+" combat="+combat+" medianNet="+net[50]+" meanNet="+net.Average()+" p10="+net[10]+" p90="+net[90]);
   Assert.GreaterOrEqual(survived,80);Assert.Greater(net[50],0);
  }
 }
}
