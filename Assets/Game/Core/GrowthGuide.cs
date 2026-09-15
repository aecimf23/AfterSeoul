using System.Collections.Generic;
using AfterSeoul.Expedition;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;
using AfterSeoul.Scav;

namespace AfterSeoul.Core
{
    public enum GrowthGoalKind { Equip, Earn, Depart, Wait, Deliver, Collect, Treat, Hire, Explore }
    public sealed class GrowthGoal
    {
        public GrowthGoalKind Kind;
        public string ItemId, QuestId, MapId;
        public long Cost;
    }

    // Guidance derives from real inventory/unlock rules; it never grants rewards or changes saves.
    public static class GrowthGuide
    {
        public static GrowthGoal Current(GameSave save, IDataRegistry data)
        {
            var pool = data.GetQuestPool(Employers.QuestPoolId(data, save.Player.EmployerNpcId));
            QuestDef pending = null;
            foreach (var active in save.Quests.Active)
            {
                if (active.Delivered || pool == null) continue;
                foreach (var q in pool)
                {
                    if (q.Id != active.QuestId) continue;
                    if (DailyQuestSystem.MeetsRequirements(save, data, q))
                        return new GrowthGoal { Kind = GrowthGoalKind.Deliver, QuestId = q.Id };
                    if (pending == null) pending = q;
                }
            }
            foreach (var exp in save.Expeditions)
                if (!exp.Resolved) return new GrowthGoal { Kind = GrowthGoalKind.Wait, MapId = exp.MapId };

            var idle = save.Scavs.Find(s => s.Status == ScavStatus.Idle);
            if (idle == null)
            {
                if (save.Scavs.Exists(s => s.Status == ScavStatus.Injured))
                    return new GrowthGoal { Kind = GrowthGoalKind.Treat };
                if (save.Scavs.Exists(s => s.Status == ScavStatus.Treating))
                    return new GrowthGoal { Kind = GrowthGoalKind.Wait };
                return new GrowthGoal { Kind = GrowthGoalKind.Hire };
            }

            // Prefer an armed, affordable idle worker rather than the first roster entry.
            ScavState ready = null;
            long cheapestWage = long.MaxValue;
            foreach (var worker in save.Scavs) {
                if (worker.Status != ScavStatus.Idle || !Scav.Equipment.EffectsOf(worker, data).HasWeapon) continue;
                long wage = ExpeditionSystem.CostFor(save, data, data.GetMap(Orientation.MapId), new[] { worker.Uid });
                if (wage < cheapestWage) { ready = worker; cheapestWage = wage; }
            }
            if (ready != null) idle = ready;
            bool regular = (save.ExploredMapIds != null && save.ExploredMapIds.Count > 0) ||
                save.Expeditions.Exists(e => !e.IsOrientation);
            if (!regular)
            {
                bool extra = false;
                foreach (var entry in idle.Equipment)
                    if (entry.Key != EquipSlot.Weapon && !string.IsNullOrEmpty(entry.Value)) extra = true;
                if (!extra)
                {
                    ShopOffer? cheapest = null;
                    foreach (var offer in Shop.OffersFor(save, data))
                    {
                        var item = data.GetItem(offer.ItemId);
                        if (item.EquipSlot == EquipSlot.Weapon) continue;
                        if (Warehouse.CountOf(save.Warehouse, item.Id) > 0)
                            return new GrowthGoal { Kind = GrowthGoalKind.Equip, ItemId = item.Id };
                        if (!cheapest.HasValue || offer.Price < cheapest.Value.Price) cheapest = offer;
                    }
                    if (cheapest.HasValue)
                        return new GrowthGoal { Kind = GrowthGoalKind.Equip, ItemId = cheapest.Value.ItemId, Cost = cheapest.Value.Price };
                }
            }
            if (!Scav.Equipment.EffectsOf(idle, data).HasWeapon)
                return new GrowthGoal { Kind = GrowthGoalKind.Equip };

            var entryMap = data.GetMap(Orientation.MapId);
            long cost = ExpeditionSystem.CostFor(save, data, entryMap, new[] { idle.Uid });
            if (save.Player.Money < cost)
                return new GrowthGoal { Kind = GrowthGoalKind.Earn, Cost = cost - save.Player.Money };
            if (!regular)
                return new GrowthGoal { Kind = GrowthGoalKind.Depart, MapId = Orientation.MapId, Cost = cost };
            if (pending != null)
                return new GrowthGoal { Kind = GrowthGoalKind.Collect, QuestId = pending.Id };
            var next = NextMap(save, data);
            return new GrowthGoal { Kind = GrowthGoalKind.Explore, MapId = next == null ? null : next.Id };
        }

        public static MapDef NextMap(GameSave save, IDataRegistry data)
        {
            var maps = new List<MapDef>();
            foreach (var map in data.AllMaps)
            {
                if (map.Id == Orientation.MapId || (save.ExploredMapIds != null && save.ExploredMapIds.Contains(map.Id)) || save.Expeditions.Exists(e => !e.IsOrientation && e.MapId == map.Id)) continue;
                var unlock = map.Unlock ?? UnlockDef.Default;
                // Trust earned from deliveries belongs to the chosen employer.
                if (unlock.Type == "npcTrust" && unlock.NpcId != save.Player.EmployerNpcId &&
                    !MapUnlock.IsUnlocked(save, map)) continue;
                maps.Add(map);
            }
            maps.Sort((a, b) => {
                int tier = a.Tier.CompareTo(b.Tier);
                if (tier != 0) return tier;
                bool ua = MapUnlock.IsUnlocked(save, a), ub = MapUnlock.IsUnlocked(save, b);
                if (ua != ub) return ua ? -1 : 1;
                return string.CompareOrdinal(a.Id, b.Id);
            });
            return maps.Count == 0 ? null : maps[0];
        }
    }
}
