using System;
using System.Collections.Generic;
using System.IO;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

// Compile alongside Assets/Game, not a reimplementation of the reward model.
public static class EconomySimulation
{
    public static void Main(string[] args)
    {
        var data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(args[0], name)));
        int runs = args.Length > 1 ? int.Parse(args[1]) : 3000;
        Console.WriteLine("map,tier,members,capacity,gear,runs,loot,cost,net,riskNet,riskNetPerScav,injured,lost,overflowValue");
        foreach (var map in data.Maps)
        foreach (var tier in data.ScavPool.Tiers)
        for (int members = 1; members <= 4; members++)
        foreach (int capacity in new[] {60, 9999})
        foreach (bool geared in new[] {false, true})
        {
            double loot = 0, cost = 0, risk = 0, injuries = 0, lost = 0, overflow = 0;
            for (int i = 1; i <= runs; i++)
            {
                var now = new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);
                var save = new GameSave { SavedAt = now, RngCounter = (uint)i };
                save.Player.Money = 100000000;
                save.Player.Level = 99;
                save.Player.CreatedAt = now;
                save.Warehouse.Capacity = capacity;
                if (map.Unlock != null && map.Unlock.NpcId != null) save.NpcTrust[map.Unlock.NpcId] = 100;
                var team = new List<string>();
                int stat = (int)Math.Round((tier.StatTotalMin + tier.StatTotalMax) / 6.0);
                for (int k = 0; k < members; k++)
                {
                    var s = new ScavState { Uid = "sc_" + k, Name = "probe", Tier = tier.Tier,
                        Search = stat, Survival = stat, Combat = stat, WagePerHour = tier.WagePerHour };
                    s.Equipment[EquipSlot.Weapon] = tier.StarterWeapon;
                    if (geared)
                    foreach (var slot in EquipSlot.All)
                    {
                        ItemDef best = null;
                        foreach (var item in data.Items)
                            if (item.Equippable && item.EquipSlot == slot &&
                                (best == null || Equipment.EffectRank(item) > Equipment.EffectRank(best))) best = item;
                        if (best != null) s.Equipment[slot] = best.Id;
                    }
                    save.Scavs.Add(s);
                    team.Add(s.Uid);
                }
                var system = new ExpeditionSystem();
                var exp = system.Depart(save, data, map.Id, team, now);
                if (exp == null) throw new Exception("Departure blocked: " + map.Id);
                var clock = new TestClock(exp.ReturnsAt);
                var report = new OfflineResolver(clock, new[] { system }).Resolve(save, data);
                var result = report.Expeditions[0];
                loot += result.LootValue;
                cost += result.CostPaid;
                double loss = result.InjuredScavUids.Count * Treatment.CostFor(save.Scavs[0], data)
                    + result.LostScavUids.Count * tier.HireCost;
                foreach (var itemId in result.LostGear) loss += data.GetItem(itemId).BasePrice;
                // Events may debit/credit money in addition to the departure fee.
                risk += result.LootValue + save.Player.Money - 100000000 - loss;
                injuries += result.InjuredScavUids.Count;
                lost += result.LostScavUids.Count;
                foreach (var stack in report.Overflowed) overflow += data.GetItem(stack.ItemId).BasePrice * stack.Count;
            }
            Console.WriteLine(string.Join(",", map.Id, tier.Tier, members, capacity, geared ? "full" : "starter", runs,
                Math.Round(loot / runs), Math.Round(cost / runs), Math.Round((loot-cost) / runs),
                Math.Round(risk / runs), Math.Round(risk / runs / members), injuries / runs, lost / runs, Math.Round(overflow / runs)));
        }
    }
}
