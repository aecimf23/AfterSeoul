using AfterSeoul.Expedition;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;
using AfterSeoul.Scav;

namespace AfterSeoul.Core
{
    public enum GrowthGoalKind { Equip, Earn, Depart, Wait, Deliver, Collect, Treat, Hire, Explore }
    public sealed class GrowthGoal
    {
        public GrowthGoalKind Kind;
        public string ItemId, QuestId, MapId, ScavUid;
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

            if (regular && pending != null)
                return new GrowthGoal { Kind = GrowthGoalKind.Collect, QuestId = pending.Id };
            var departure = NextDeparture(save, data);
            if (departure != null) return departure;
            if (ExplorationSystem.IsActive(save))
                return new GrowthGoal { Kind = GrowthGoalKind.Wait, MapId = save.Exploration.MapId };
            var next = NextMap(save, data);
            return new GrowthGoal { Kind = GrowthGoalKind.Explore, MapId = next == null ? null : next.Id };
        }

        private static GrowthGoal NextDeparture(GameSave save, IDataRegistry data)
        {
            GrowthGoal affordable = null, cheapest = null;
            bool affordableFresh = false;
            foreach (var map in ExplorationSystem.OrderedMaps(data))
            {
                if (!MapUnlock.IsUnlocked(save, map)) continue;
                if (ExplorationSystem.IsActive(save) && save.Exploration.MapId == map.Id) continue;
                bool fresh = save.ExploredMapIds == null || !save.ExploredMapIds.Contains(map.Id);
                foreach (var worker in save.Scavs)
                {
                    if (worker.Status != ScavStatus.Idle || !Scav.Equipment.EffectsOf(worker, data).HasWeapon) continue;
                    var team = new[] { worker.Uid };
                    long cost = ExpeditionSystem.CostFor(save, data, map, team);
                    if (cheapest == null || cost < cheapest.Cost)
                        cheapest = new GrowthGoal { Kind = GrowthGoalKind.Earn, MapId = map.Id, ScavUid = worker.Uid, Cost = cost };
                    if (cost > save.Player.Money || ExpeditionSystem.DepartBlockReason(save, data, map.Id, team) != null) continue;
                    if (affordable == null || (fresh && !affordableFresh) ||
                        (fresh == affordableFresh && map.Id == affordable.MapId && cost < affordable.Cost))
                    {
                        affordable = new GrowthGoal { Kind = GrowthGoalKind.Depart, MapId = map.Id, ScavUid = worker.Uid, Cost = cost };
                        affordableFresh = fresh;
                    }
                }
            }
            if (affordable != null) return affordable;
            if (cheapest != null) cheapest.Cost -= save.Player.Money;
            return cheapest;
        }

        public static MapDef NextMap(GameSave save, IDataRegistry data)
        {
            foreach (var map in ExplorationSystem.OrderedMaps(data))
            {
                if ((save.SurvivedExplorationMapIds != null && save.SurvivedExplorationMapIds.Contains(map.Id)) ||
                    (save.ExploredMapIds != null && save.ExploredMapIds.Contains(map.Id)) ||
                    save.Expeditions.Exists(e => !e.IsOrientation && e.MapId == map.Id)) continue;
                return map;
            }
            return null;
        }
    }
}
