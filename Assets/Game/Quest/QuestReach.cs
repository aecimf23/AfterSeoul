using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Inventory;

namespace AfterSeoul.Quest
{
    /// <summary>
    /// 이 의뢰를 <b>지금 실제로 깰 수 있는가</b>.
    ///
    /// <para>티어 숫자만으로 거르면 "레벨 1 에게 구로에서만 나오는 탄약 30발을 요구하는 의뢰"가
    /// 걸린다. 실제로 그랬다 — 탄약 의뢰가 티어 1 이었고, 탄약은 레벨 5 잠금인 구로에서만
    /// 나왔다. 플레이어에게는 그냥 못 깨는 칸 하나가 하루 종일 붙어 있는 것이다.</para>
    ///
    /// <para>그래서 티어가 아니라 <b>경로</b>를 본다. 요구 품목마다 "열려 있는 지역에서 나오는가 /
    /// 만들 수 있는가 / 살 수 있는가 / 이미 창고에 있는가"를 묻는다. 지역과 전리품 표가 바뀌어도
    /// 손으로 유지하는 대응표가 없으니 어긋날 자리가 없다.</para>
    /// </summary>
    public static class QuestReach
    {
        /// <summary>요구 품목을 전부 구할 수 있으면 true.</summary>
        public static bool IsAchievable(GameSave save, IDataRegistry data, QuestDef quest)
        {
            if (quest == null) return false;
            if (quest.Requires == null || quest.Requires.Length == 0) return true;

            var reachable = ReachableItems(save, data);

            foreach (var req in quest.Requires)
            {
                if (!string.IsNullOrEmpty(req.ItemId))
                {
                    if (!reachable.Contains(req.ItemId)) return false;
                }
                else if (!string.IsNullOrEmpty(req.Tag))
                {
                    if (!AnyWithTag(reachable, data, req.Tag)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 지금 손에 넣을 수 있는 아이템 전부.
        ///
        /// <para>순서가 중요하다: 먼저 열린 지역의 전리품과 상점·창고를 모으고,
        /// 그 다음 그것들로 만들 수 있는 것을 더한다. 제작은 한 단계만 따라간다 —
        /// 재료의 재료까지 쫓으면 순환을 막는 코드가 필요해지는데, 레시피가 그렇게
        /// 깊어질 계획이 없다.</para>
        /// </summary>
        public static HashSet<string> ReachableItems(GameSave save, IDataRegistry data)
        {
            var set = new HashSet<string>();

            // 1. 열려 있는 지역의 전리품
            foreach (var map in data.AllMaps)
            {
                if (!MapUnlock.IsUnlocked(save, map)) continue;

                var table = data.GetLootTable(map.LootTableId);
                if (table == null) continue;

                foreach (var e in table.Entries) set.Add(e.ItemId);
            }

            // 2. 상점에서 살 수 있는 것 (신뢰도로 열린 상인만)
            foreach (var offer in Shop.OffersFor(save, data)) set.Add(offer.ItemId);

            // 3. 이미 창고에 있는 것. 어제 주워 둔 것으로 오늘 의뢰를 채울 수 있다.
            foreach (var stack in Warehouse.Snapshot(save.Warehouse)) set.Add(stack.ItemId);

            // 4. 위의 것들로 만들 수 있는 것
            var crafted = new List<string>();
            foreach (var recipe in data.AllRecipes)
            {
                if (save.Factory.StationLevel < recipe.StationLevel) continue;

                bool haveAll = true;
                foreach (var input in recipe.Inputs)
                    if (!set.Contains(input.ItemId)) { haveAll = false; break; }

                if (haveAll && !string.IsNullOrEmpty(recipe.OutputItemId))
                    crafted.Add(recipe.OutputItemId);
            }
            foreach (var id in crafted) set.Add(id);

            return set;
        }

        private static bool AnyWithTag(HashSet<string> reachable, IDataRegistry data, string tag)
        {
            foreach (var id in reachable)
            {
                var def = data.GetItem(id);
                if (def == null || def.Tags == null) continue;

                foreach (var t in def.Tags)
                    if (t == tag) return true;
            }
            return false;
        }
    }
}
