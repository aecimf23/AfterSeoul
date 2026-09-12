using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Inventory
{
    /// <summary>
    /// 창고 조작. stateless — 상태는 전부 <see cref="WarehouseState"/> 에 있다.
    ///
    /// 본편은 그리드 인벤토리(<c>PlayerProfile.StashGrid</c>)지만 모바일은 스택 리스트다.
    /// 작은 화면에서 테트리스 인벤토리를 만지게 하는 건 최악의 UX 이고, 이 게임의
    /// 재미는 공간 퍼즐이 아니라 자원 배분에 있다. 용량은 "스택 종류 수"로만 제한한다.
    /// </summary>
    public static class Warehouse
    {
        /// <summary>
        /// 넣는다. 용량이 모자라면 들어간 만큼만 넣고 나머지 수량을 돌려준다.
        ///
        /// <b>전부 넣거나 전부 실패하는 방식을 쓰지 않는 이유:</b> 파견 복귀 전리품이
        /// 창고를 넘칠 때 "하나도 못 받음"은 플레이어에게 가혹하다. 들어갈 만큼 넣고
        /// 넘친 것은 리포트에 남겨 알린다.
        /// </summary>
        public static int TryAdd(WarehouseState w, IDataRegistry data, string itemId, int count)
        {
            if (count <= 0) return 0;

            var def = data.GetItem(itemId);
            int maxStack = def?.MaxStack ?? 1;
            if (maxStack < 1) maxStack = 1;

            int remaining = count;

            // 기존 스택의 빈자리부터 채운다.
            for (int i = 0; i < w.Stacks.Count && remaining > 0; i++)
            {
                if (w.Stacks[i].ItemId != itemId) continue;
                int room = maxStack - w.Stacks[i].Count;
                if (room <= 0) continue;

                int put = room < remaining ? room : remaining;
                var s = w.Stacks[i];
                s.Count += put;
                w.Stacks[i] = s;
                remaining -= put;
            }

            // 남으면 새 스택.
            while (remaining > 0 && w.Stacks.Count < w.TotalCapacity)
            {
                int put = maxStack < remaining ? maxStack : remaining;
                w.Stacks.Add(new ItemStack(itemId, put));
                remaining -= put;
            }

            return remaining; // 넘친 수량
        }

        /// <summary>
        /// 남은 스택 칸. 0 이면 새 종류를 더 넣을 수 없다.
        /// (같은 종류를 기존 스택에 더 쌓는 것은 칸이 없어도 된다.)
        /// </summary>
        public static int FreeSlots(WarehouseState w)
        {
            int free = w.TotalCapacity - w.Stacks.Count;
            return free > 0 ? free : 0;
        }

        /// <summary>보유 수량.</summary>
        public static int CountOf(WarehouseState w, string itemId)
        {
            int total = 0;
            foreach (var s in w.Stacks)
                if (s.ItemId == itemId) total += s.Count;
            return total;
        }

        /// <summary>
        /// 뺀다. 수량이 모자라면 <b>아무것도 빼지 않고</b> false.
        ///
        /// 넣기와 달리 부분 성공을 허용하지 않는 이유: 차감은 납품·발송·제작처럼
        /// 대가가 오가는 지점에서만 일어난다. 여기서 부분 성공을 허용하면
        /// "재료는 절반 사라졌는데 결과물은 없는" 상태가 만들어진다.
        /// </summary>
        public static bool TryRemove(WarehouseState w, string itemId, int count)
        {
            if (count <= 0) return true;
            if (CountOf(w, itemId) < count) return false;

            int remaining = count;
            for (int i = w.Stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                if (w.Stacks[i].ItemId != itemId) continue;

                var s = w.Stacks[i];
                int take = s.Count < remaining ? s.Count : remaining;
                s.Count -= take;
                remaining -= take;

                if (s.Count == 0) w.Stacks.RemoveAt(i);
                else w.Stacks[i] = s;
            }
            return true;
        }

        /// <summary>태그로 총 수량을 센다. 의뢰의 <c>tag</c> 조건 판정에 쓴다.</summary>
        public static int CountByTag(WarehouseState w, IDataRegistry data, string tag)
        {
            int total = 0;
            foreach (var s in w.Stacks)
            {
                var def = data.GetItem(s.ItemId);
                if (def == null) continue;
                foreach (var t in def.Tags)
                    if (t == tag) { total += s.Count; break; }
            }
            return total;
        }

        public static IReadOnlyList<ItemStack> Snapshot(WarehouseState w) => w.Stacks;
    }
}
