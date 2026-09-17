using System;
using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    // Real registry, departure, inventory and resolver: no duplicated reward formula.
    public class TeamEconomyTests
    {
        private JsonDataRegistry _data;
        [SetUp] public void SetUp() => _data = JsonDataRegistry.Load(name =>
            File.ReadAllText(Path.Combine(UnityEngine.Application.streamingAssetsPath, "Data", name)));

        private struct Totals { public double Loot, RiskNet, Overflow; }

        private Totals Run(int tierNumber, int members, int capacity = 60, int runs = 1000)
        {
            var totals = new Totals();
            ScavTierDef tier = null;
            foreach (var candidate in _data.ScavPool.Tiers)
                if (candidate.Tier == tierNumber) tier = candidate;
            Assert.IsNotNull(tier);
            int stat = (int)Math.Round((tier.StatTotalMin + tier.StatTotalMax) / 6.0);
            for (uint seed = 1; seed <= runs; seed++)
            {
                var now = new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);
                var save = new GameSave { SavedAt = now, RngCounter = seed };
                save.Player.Money = 100000000;
                save.Player.Level = 99;
                save.SurvivedExplorationMapIds.AddRange(AfterSeoul.Exploration.ExplorationSystem.MainRoute);
                save.Player.CreatedAt = now;
                save.Warehouse.Capacity = capacity;
                var team = new List<string>();
                for (int i = 0; i < members; i++)
                {
                    var scav = new ScavState { Uid = "sc_" + i, Name = "probe", Tier = tierNumber,
                        Search = stat, Combat = stat, Survival = stat, WagePerHour = tier.WagePerHour };
                    foreach (var slot in EquipSlot.All)
                    {
                        ItemDef best = null;
                        foreach (var item in _data.Items)
                            if (item.Equippable && item.EquipSlot == slot &&
                                (best == null || Equipment.EffectRank(item) > Equipment.EffectRank(best))) best = item;
                        if (best != null) scav.Equipment[slot] = best.Id;
                    }
                    save.Scavs.Add(scav);
                    team.Add(scav.Uid);
                }
                var system = new ExpeditionSystem();
                var exp = system.Depart(save, _data, "GURO_FACTORY", team, now);
                Assert.IsNotNull(exp);
                var resolver = new OfflineResolver(new TestClock(exp.ReturnsAt), new[] { system });
                var report = resolver.Resolve(save, _data);
                Assert.AreEqual(1, report.Expeditions.Count);
                var result = report.Expeditions[0];
                double loss = result.InjuredScavUids.Count * Treatment.CostFor(save.Scavs[0], _data)
                    + result.LostScavUids.Count * tier.HireCost;
                foreach (var itemId in result.LostGear) loss += _data.GetItem(itemId).BasePrice;
                totals.Loot += result.LootValue;
                totals.RiskNet += result.LootValue + save.Player.Money - 100000000 - loss;
                foreach (var item in report.Overflowed) totals.Overflow += _data.GetItem(item.ItemId).BasePrice * item.Count;
                Assert.IsEmpty(resolver.Resolve(save, _data).Expeditions, "Return must settle only once");
            }
            return totals;
        }

        [Test] public void PairHasARealSafetyAndProfitNichePerDeployedScav()
        {
            var solo = Run(1, 1);
            var pair = Run(1, 2);
            Assert.Greater(pair.Loot, solo.Loot * 1.25, "Extra hands must actually recover more");
            Assert.Greater(pair.RiskNet / 2, solo.RiskNet * 1.10,
                "On hazardous Guro with valuable gear, teamwork must beat independent solo expeditions per scav");
            Assert.Less(pair.Loot / 2, solo.Loot, "Carrying returns should diminish per member");
        }

        [Test] public void SkilledSoloHasAProfitableUpgradeNiche()
        {
            var entry = Run(1, 1);
            var skilled = Run(2, 1);
            var expert = Run(3, 1);
            Assert.Greater(skilled.RiskNet, entry.RiskNet);
            Assert.Greater(expert.RiskNet, skilled.RiskNet);
        }

        [Test] public void RecoveryIsDeterministicAndCannotCreditOverflow()
        {
            var first = Run(3, 4, 60, 50);
            var replay = Run(3, 4, 60, 50);
            Assert.AreEqual(first.Loot, replay.Loot);
            Assert.AreEqual(first.RiskNet, replay.RiskNet);
            var full = Run(3, 4, 0, 50);
            Assert.AreEqual(0, full.Loot);
            Assert.Greater(full.Overflow, 0);
            Assert.Less(full.RiskNet, 0);
        }
    }
}
