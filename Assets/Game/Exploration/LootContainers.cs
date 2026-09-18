using System;
using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Exploration
{
    public static class LootContainers
    {
        public static readonly IReadOnlyList<string> Kinds = Array.AsReadOnly(new[] { "Tool", "Medical", "Weapon", "Ammo", "Pocket", "Safe" });
        public static string Name(string kind)
        {
            switch (kind) {
                case "Tool": return Loc.Text("공구상자");
                case "Medical": return Loc.Text("의료상자");
                case "Weapon": return Loc.Text("총기상자");
                case "Ammo": return Loc.Text("탄약상자");
                case "Safe": return Loc.Text("금고");
                default: return Loc.Text("포켓");
            }
        }

        public static string Hint(string kind)
        {
            switch (kind) {
                case "Tool": return Loc.Text("수리에 쓸 부품과 공구가 보입니다.");
                case "Medical": return Loc.Text("응급 처치에 쓸 의료 물자가 보입니다.");
                case "Weapon": return Loc.Text("보관된 무기와 장비 중 필요한 것을 골라 보세요.");
                case "Ammo": return Loc.Text("총기에 맞는 탄종인지 확인하고 챙기세요.");
                case "Safe": return Loc.Text("금고 안에 거래할 만한 귀중품이 남아 있습니다.");
                default: return Loc.Text("버려진 외투의 포켓에 작은 물자가 남아 있습니다.");
            }
        }

        public static List<ItemStack> RollChoices(IDataRegistry data, string kind, Func<int, int> roll, string map = null, int choices = 2)
        {
            var available = new List<string>();
            foreach (var item in data.AllItems)
                if (AvailableIn(item, kind)) available.Add(item.Id);
            // Stable ordering keeps a saved RNG state reproducible across registry implementations.
            available.Sort(StringComparer.Ordinal);
            // Minimal registries and older catalogs can still produce a useful find.
            if (available.Count == 0 && data.GetItem("JUNK03") != null) available.Add("JUNK03");
            var result = new List<ItemStack>();
            while (result.Count < choices && available.Count > 0) {
                int total = 0;
                foreach (string candidate in available) total += Weight(data.GetItem(candidate)) * (map == null ? 1 : RaidRegions.LootWeight(map, data.GetItem(candidate)));
                int ticket = roll(total), index = 0;
                while (index < available.Count - 1 && ticket >= Weight(data.GetItem(available[index])) * (map == null ? 1 : RaidRegions.LootWeight(map, data.GetItem(available[index])))) {
                    ticket -= Weight(data.GetItem(available[index])) * (map == null ? 1 : RaidRegions.LootWeight(map, data.GetItem(available[index]))); index++;
                }
                string id = available[index]; available.RemoveAt(index);
                var item = data.GetItem(id);
                int count = item.Category == "Ammo" ? 10 + roll(3) * 5 :
                    kind == "Tool" && item.BasePrice <= 5000 ? 2 + roll(3) : 1;
                result.Add(new ItemStack(id, count));
            }
            return result;
        }

        public static bool AvailableIn(ItemDef item, string kind)
        {
            if (item == null || item.SpawnWeight <= 0 || item.Id.StartsWith("UIJ_") || item.Id.StartsWith("QUEST_") ||
                item.Id.StartsWith("KEY") || item.Id.StartsWith("COSM_") || item.Id.StartsWith("CASE_")) return false;
            switch (kind) {
                case "Weapon": return item.Equippable && (item.EquipSlot != "Weapon" ||
                    item.Slot == "Melee" || CombatProfiles.For(item.Id) != null);
                case "Medical": return item.Category == "Medical" && CombatProfiles.ConsumableFor(item.Id) != null;
                case "Ammo": return item.Category == "Ammo" && item.Id.StartsWith("AMO");
                case "Safe": return Valuable(item.Id);
                case "Tool": return item.Category == "Junk" && item.Id.StartsWith("JUNK") &&
                    !Valuable(item.Id) && item.Id != "JUNK11" && item.Id != "JUNK12";
                default: return item.Category == "Food" && CombatProfiles.ConsumableFor(item.Id) != null ||
                    item.Id == "MED16" || item.Id == "MED05" || item.Id == "BAT01" || item.Id == "JUNK_LIGHTER";
            }
        }

        private static bool Valuable(string id) => id == "JUNK01" || id == "JUNK02" || id == "JUNK05" ||
            id == "JUNK06" || id == "JUNK13" || id == "JUNK14" || id == "JUNK39" || id == "JUNK40" ||
            id == "JUNK_FLASH" || id == "GOLDEN_ZIBBO";

        private static int Weight(ItemDef item)
        {
            // Preserve mainline scarcity, further reduce expensive jackpots for short mobile runs.
            int divisor = item.BasePrice >= 150000 ? 8 : item.BasePrice >= 60000 ? 4 : item.BasePrice >= 25000 ? 2 : 1;
            return Math.Max(1, Math.Min(80, item.SpawnWeight) / divisor);
        }
    }
}
