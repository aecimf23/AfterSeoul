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
    public class RaidDepthFeatureTests
    {
        private IDataRegistry data;
        private GameSave save;
        [SetUp] public void Setup() {
            data=JsonDataRegistry.Load(n=>File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"Data",n)));
            save=new GameSave();save.Player.EmployerNpcId="HWANG";ExplorationSystem.PrepareStarter(save,data);save.FirstExplorationQuest.Completed=true;
        }
        private ExplorationState Start() {Assert.IsTrue(ExplorationSystem.Start(save,data,"YONGSAN_MARKET"));return save.Exploration;}
        private ExplorationState Enemy(string type="Grenadier") {
            var e=Start();e.Phase=ExplorationPhase.Combat;e.Enemy=new ExplorationEnemy {Kind="PMC",Name="test",Archetype=type,Hp=100,MaxHp=100};return e;
        }
        [Test] public void RainOutdoorsConcealsPlayerAndContactPersistsWithoutTickDamage() {
            int humans=0;
            for(uint seed=1;seed<=90;seed++) {
                save.Exploration=null;save.Player.Energy=save.Player.Hydration=save.Player.Hp=100;save.RngCounter=seed;
                var e=Start();ExplorationSystem.Move(save,data,0);e.Phase=ExplorationPhase.Routes;e.Weather=ExplorationWeather.Rain;
                ExplorationSystem.Move(save,data,1);
                Assert.IsFalse(e.Indoors);Assert.IsTrue(e.Dangerous);
                if(e.Enemy==null) continue;
                humans++;Assert.IsFalse(e.Detected);Assert.IsTrue(e.Initiative);Assert.AreEqual(ExplorationPhase.Encounter,e.Phase);
                var codec=new NewtonsoftJsonCodec();save=codec.Deserialize<GameSave>(codec.Serialize(save));
                double hp=save.Player.Hp;ExplorationSystem.Tick(save,data,20);Assert.AreEqual(hp,save.Player.Hp);
                Assert.IsTrue(ExplorationSystem.Choose(save,data,EncounterChoice.Avoid));Assert.AreEqual(ExplorationPhase.LootChoice,save.Exploration.Phase);
            }
            Assert.Greater(humans,10);
        }
        [Test] public void YongsanEntranceContainersMatchSafeEntrancesAfterOnboarding()
        {
            var e=Start();CollectionAssert.AreEqual(new[]{"Tool","Pocket"},e.RouteContainers);
        }
        [Test] public void DangerousCombatVictoryOffersThreeChoicesWithoutAnExtraAutomaticReward()
        {
            var e=Enemy();e.Dangerous=true;e.Enemy.Hp=1;save.Player.Equipment["Melee"]="MEL01";
            e.LootOptions=LootContainers.RollChoices(data,"Weapon",n=>0,e.MapId,3);
            Assert.IsTrue(ExplorationSystem.Melee(save,data));Assert.AreEqual(ExplorationPhase.LootChoice,e.Phase);Assert.IsEmpty(e.Loot);
            Assert.IsTrue(ExplorationSystem.ChooseLoot(save,2));Assert.AreEqual(1,e.Loot.Count);Assert.AreEqual(1,e.EncounterLoot.Count);
            Assert.IsFalse(ExplorationSystem.ChooseLoot(save,1));
        }
        [Test] public void BuildingsRemoveFogAndRainModifiers() {
            var e=Start();e.Weather=ExplorationWeather.Fog;e.Indoors=true;
            Assert.AreEqual(ExplorationWeather.Clear,ExplorationSystem.EffectiveWeather(e));
            e.Indoors=false;Assert.AreEqual(ExplorationWeather.Fog,ExplorationSystem.EffectiveWeather(e));
        }
        [TestCase("Rifleman")][TestCase("Rusher")][TestCase("Sniper")][TestCase("Grenadier")]
        public void EnemyPatternsMixActionsAndGrenadesAreLimited(string type) {
            var e=Enemy(type);save.Player.Hp=1000;var actions=new HashSet<EnemyAction>();
            for(int n=0;n<600;n++){ExplorationSystem.Tick(save,data,.1);actions.Add(e.Enemy.Action);}
            Assert.IsTrue(actions.Contains(EnemyAction.Aiming));Assert.IsTrue(actions.Contains(EnemyAction.Reloading));
            Assert.LessOrEqual(e.Enemy.GrenadesThrown,2);
            if(type=="Grenadier") Assert.IsTrue(actions.Contains(EnemyAction.Grenade));
            if(type=="Rusher") Assert.IsTrue(actions.Contains(EnemyAction.Rush));
        }
        [TestCase(true)][TestCase(false)] public void GrenadeMustBeDodgedAndResponseSurvivesReload(bool dodge) {
            var e=Enemy();e.Enemy.Action=EnemyAction.Grenade;e.Enemy.Remaining=2.4;e.CoverRemaining=10;
            if(dodge) Assert.IsTrue(ExplorationSystem.Dodge(save));
            var codec=new NewtonsoftJsonCodec();save=codec.Deserialize<GameSave>(codec.Serialize(save));
            ExplorationSystem.Tick(save,data,2.45);
            Assert.AreEqual(dodge?100:68,save.Player.Hp,.001);
            StringAssert.Contains(dodge?"회피 성공":"수류탄 폭발",save.Exploration.EnemyFeedback);
        }
        [Test] public void FriendlyScavGiftIsGrantedOnceAndHostilityStartsTelegraphedCombat() {
            var e=Start();e.Phase=ExplorationPhase.Encounter;e.Enemy=new ExplorationEnemy{Kind="Scav"};e.ScavAttitude="Friendly";
            Assert.IsTrue(ExplorationSystem.Choose(save,data,EncounterChoice.Talk));
            Assert.IsTrue(ExplorationSystem.Choose(save,data,EncounterChoice.RequestAid));Assert.AreEqual(1,e.Loot.Count);
            Assert.IsFalse(ExplorationSystem.Choose(save,data,EncounterChoice.RequestAid));
            Assert.IsNotEmpty(e.EncounterNote);
            e.Phase=ExplorationPhase.Encounter;e.Enemy=new ExplorationEnemy{Kind="Scav"};e.ScavAttitude="Hostile";e.ConversationOpen=true;
            Assert.IsTrue(ExplorationSystem.Choose(save,data,EncounterChoice.RequestAid));Assert.AreEqual(ExplorationPhase.Combat,e.Phase);Assert.Greater(e.Enemy.Remaining,1.5);
        }
        [Test] public void ScavTradeNeedsOwnedFoodAndConsumesOnlyOne() {
            var e=Start();e.Phase=ExplorationPhase.Encounter;e.Enemy=new ExplorationEnemy{Kind="Scav"};e.ScavAttitude="Friendly";e.ConversationOpen=true;
            e.LoanSupplies.Add(new ItemStack("FOOD01",2));
            Assert.IsFalse(ExplorationSystem.Choose(save,data,EncounterChoice.Trade));
            e.Supplies.Add(new ItemStack("FOOD01",2));
            Assert.IsTrue(ExplorationSystem.Choose(save,data,EncounterChoice.Trade));
            Assert.AreEqual(1,e.Supplies[0].Count);Assert.AreEqual(2,e.LoanSupplies[0].Count);Assert.AreEqual("JUNK20",e.Loot[0].ItemId);
        }
        [Test] public void RegionLootHasDistinctMaterialBiasAndDangerousRoutesOfferThreeChoices() {
            int yongsan=0,guro=0;var random=new System.Random(872);
            for(int n=0;n<1000;n++) {
                yongsan+=LootContainers.RollChoices(data,"Tool",random.Next,"YONGSAN_MARKET").Count(x=>x.ItemId=="JUNK23");
                guro+=LootContainers.RollChoices(data,"Tool",random.Next,"GURO_FACTORY").Count(x=>x.ItemId=="JUNK23");
            }
            Assert.Greater(yongsan,guro*2);
            var e=Start();ExplorationSystem.Move(save,data,0);
            Assert.IsFalse(e.RouteDangerous[0]);Assert.IsTrue(e.RouteDangerous[1]);
            e.Phase=ExplorationPhase.Routes;ExplorationSystem.Move(save,data,1);Assert.AreEqual(3,e.LootOptions.Count);
        }
        [Test] public void FacilityCostsAreAtomicAndEffectsChangeMovementAndHealing() {
            save.Player.Money=20000;var p=RaidProgression.Next(save,"supplies");foreach(var c in p.Costs) Warehouse.TryAdd(save.Warehouse,data,c.ItemId,c.Count);
            Assert.IsTrue(RaidProgression.Upgrade(save,data,"supplies"));Assert.AreEqual(16000,save.Player.Money);
            Assert.IsFalse(RaidProgression.Upgrade(save,data,"supplies"));
            var e=Start();ExplorationSystem.Move(save,data,0);Assert.AreEqual(87,save.Player.Energy);Assert.AreEqual(89,save.Player.Hydration);
            e.Phase=ExplorationPhase.Routes;e.Supplies.Add(new ItemStack("MED05",1));save.RaidBase.Clinic=1;save.Player.Hp=20;
            Assert.IsTrue(ExplorationSystem.Use(save,data,"MED05"));ExplorationSystem.Tick(save,data,2.1);Assert.AreEqual(65,save.Player.Hp);
        }
        [Test] public void BarterCannotChargeForLockedMapOrFullWarehouse() {
            var offer=Array.Find(RaidProgression.Barters,x=>x.Id=="rifle");
            foreach(var c in offer.Costs) Warehouse.TryAdd(save.Warehouse,data,c.ItemId,c.Count);
            var codec=new NewtonsoftJsonCodec();string before=codec.Serialize(save);
            Assert.IsFalse(RaidProgression.Barter(save,data,"rifle"));Assert.AreEqual(before,codec.Serialize(save));
            save.SurvivedExplorationMapIds.Add("YONGSAN_MARKET");save.RaidBase.Workbench=1;
            Assert.IsTrue(RaidProgression.Barter(save,data,"rifle"));Assert.AreEqual(1,Warehouse.CountOf(save.Warehouse,"WPN01"));
            Assert.IsFalse(RaidProgression.Barter(save,data,"rifle"));
        }
        [Test] public void RecoveryLoanWorksWithoutEquipmentAndCannotBeSoldAfterReturning() {
            save.Player.Equipment.Clear();save.Warehouse.Stacks.Clear();save.Player.Money=0;
            Assert.IsTrue(RaidProgression.RequestRecovery(save));Assert.IsFalse(RaidProgression.RequestRecovery(save));
            var e=Start();Assert.IsTrue(e.RecoveryRun);Assert.AreEqual("WPN04",PlayerEquipment.Equipped(save,"Weapon"));Assert.AreEqual(50,ExplorationSystem.AmmoRemaining(save));
            e.Phase=ExplorationPhase.Combat;e.Enemy=new ExplorationEnemy{Hp=1000};
            Assert.IsTrue(ExplorationSystem.Attack(save,data,FireMode.Single));Assert.AreEqual(49,ExplorationSystem.AmmoRemaining(save));
            Assert.IsTrue(ExplorationSystem.EmergencyReturn(save,data));Assert.IsNull(PlayerEquipment.Equipped(save,"Weapon"));Assert.IsEmpty(save.Warehouse.Stacks);Assert.IsEmpty(save.ExplorationOverflow);Assert.IsEmpty(e.LoanSupplies);
        }
        [Test] public void ResupplyPricesUseAmmoUnitsAndCannotGenerateMoneyByReselling() {
            save.Player.Money=10000;
            long price=RaidProgression.SupplyPrice(save,data,"AMO05",30);
            Assert.Less(price,1000);Assert.Greater(price,Market.SellPrice(save,data,"AMO05")*30);
            int before=Warehouse.CountOf(save.Warehouse,"AMO05");
            Assert.IsTrue(RaidProgression.BuySupplies(save,data,"AMO05",30));Assert.AreEqual(before+30,Warehouse.CountOf(save.Warehouse,"AMO05"));Assert.AreEqual(10000-price,save.Player.Money);
            Assert.IsFalse(RaidProgression.BuySupplies(save,data,"WPN04",1));
        }
    }
}
