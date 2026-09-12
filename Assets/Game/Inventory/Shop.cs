using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Scav;

namespace AfterSeoul.Inventory
{
    /// <summary>상점에 걸린 물건 한 줄.</summary>
    public struct ShopOffer
    {
        public string ItemId;

        /// <summary>구매가. <c>basePrice × shop.priceMultiplier</c> 를 올림한 값.</summary>
        public long Price;

        /// <summary>대주는 상인. 화면에 "누구 물건인지"를 보여주려고 들고 있다.</summary>
        public string NpcId;
    }

    /// <summary>
    /// 장비 상점 (GDD §7).
    ///
    /// <para><b>황 상사가 중개한다.</b> 고용주 신뢰도가 오를수록 더 좋은 상인의 물건을 대준다.
    /// 상인을 직접 만나는 화면은 만들지 않는다 — MVP 는 고용주가 하나뿐이고(GDD §4),
    /// 상인마다 화면을 두면 창이 다섯 개가 되는데 그 안에 든 건 목록 하나씩이다.</para>
    ///
    /// <para><b>파는 물건은 <c>npcs.json</c> 의 본편 재고에서 온다.</b> 모바일에서 품목을
    /// 따로 적으면 본편과 다른 물건을 파는 같은 이름의 상인이 생긴다.</para>
    /// </summary>
    public static class Shop
    {
        /// <summary>
        /// 지금 살 수 있는 장비. 신뢰도로 잠긴 상인의 물건은 나오지 않는다.
        /// <paramref name="equipSlot"/> 을 주면 그 칸에 맞는 것만 거른다.
        /// </summary>
        public static List<ShopOffer> OffersFor(GameSave save, IDataRegistry data, string equipSlot = null)
        {
            var list = new List<ShopOffer>();
            var shop = data.Shop;
            if (shop == null || shop.Traders == null) return list;

            int trust = TrustOfEmployer(save);
            var seen = new HashSet<string>();

            foreach (var trader in shop.Traders)
            {
                if (trust < trader.RequiresTrust) continue;

                var npc = data.GetNpc(trader.NpcId);
                if (npc == null) continue;

                foreach (var itemId in npc.InventoryItemIds)
                {
                    var def = data.GetItem(itemId);
                    if (def == null || !def.Equippable) continue;
                    if (equipSlot != null && def.EquipSlot != equipSlot) continue;

                    // 같은 물건을 두 상인이 취급하면 싼 쪽 하나만 건다.
                    // 목록에 같은 이름이 두 번 뜨면 값이 다른 이유를 설명할 길이 없다.
                    if (!seen.Add(itemId)) continue;

                    list.Add(new ShopOffer
                    {
                        ItemId = itemId,
                        Price = PriceOf(def, shop),
                        NpcId = trader.NpcId,
                    });
                }
            }

            // 값이 아니라 효과 순으로 세운다. 값으로 세우면 "줄의 끝이 제일 좋은 물건"이라는
            // 인상을 주는데, 무기 칸에서는 그게 거짓이다 — 제일 비싼 무기가 950,000원짜리
            // 단검(최하 등급)이라서, 돈이 생긴 플레이어가 맨 아래를 사면 손해를 본다.
            // 약한 것에서 강한 것으로 가고, 효과가 같으면 싼 것을 앞에 둔다.
            list.Sort((a, b) =>
            {
                var da = data.GetItem(a.ItemId);
                var db = data.GetItem(b.ItemId);

                int bySlot = Equipment.SlotOrder(da.EquipSlot).CompareTo(Equipment.SlotOrder(db.EquipSlot));
                if (bySlot != 0) return bySlot;

                int byEffect = Equipment.EffectRank(da).CompareTo(Equipment.EffectRank(db));
                if (byEffect != 0) return byEffect;

                int byPrice = a.Price.CompareTo(b.Price);
                return byPrice != 0 ? byPrice : string.CompareOrdinal(a.ItemId, b.ItemId);
            });
            return list;
        }

        /// <summary>구매가. 올림해서 basePrice 보다 싸지는 일이 없게 한다.</summary>
        public static long PriceOf(ItemDef def, ShopDef shop)
        {
            double m = shop != null && shop.PriceMultiplier > 0 ? shop.PriceMultiplier : 1.0;
            return (long)System.Math.Ceiling(def.BasePrice * m);
        }

        /// <summary>
        /// 산다. 돈을 내고 창고에 넣는다. 창고가 꽉 차 있으면 사지 않는다 —
        /// 돈만 빠지고 물건이 안 들어오는 상태를 만들지 않는다.
        /// </summary>
        public static bool TryBuy(GameSave save, IDataRegistry data, string itemId)
        {
            if (BuyBlockReason(save, data, itemId) != null) return false;

            var def = data.GetItem(itemId);
            long price = PriceOf(def, data.Shop);

            // 창고를 먼저 시험한다. 넣지 못하면 돈은 건드리지 않는다.
            if (Warehouse.TryAdd(save.Warehouse, data, itemId, 1) > 0) return false;

            save.Player.Money -= price;
            return true;
        }

        /// <summary>살 수 없는 이유. 가능하면 null.</summary>
        public static string BuyBlockReason(GameSave save, IDataRegistry data, string itemId)
        {
            var def = data.GetItem(itemId);
            if (def == null) return "아이템 정보를 찾을 수 없습니다";
            if (!def.Equippable) return "상점에서 다루지 않는 물건입니다";

            if (!IsAvailable(save, data, itemId))
                return $"{def.EquipSlot} — 아직 구할 수 없습니다 (황 상사 신뢰도 부족)";

            long price = PriceOf(def, data.Shop);
            if (save.Player.Money < price)
                return $"자금 부족 — {price - save.Player.Money:N0}원 더 필요합니다";

            if (Warehouse.FreeSlots(save.Warehouse) <= 0)
                return "창고가 가득 찼습니다";

            return null;
        }

        private static bool IsAvailable(GameSave save, IDataRegistry data, string itemId)
        {
            int trust = TrustOfEmployer(save);
            foreach (var trader in data.Shop.Traders)
            {
                if (trust < trader.RequiresTrust) continue;
                var npc = data.GetNpc(trader.NpcId);
                if (npc == null) continue;
                foreach (var id in npc.InventoryItemIds)
                    if (id == itemId) return true;
            }
            return false;
        }

        /// <summary>
        /// 다음 상인이 열리기까지 남은 신뢰도. 전부 열렸으면 null.
        /// 화면이 "무엇을 더 하면 더 좋은 물건이 나오는지"를 말할 수 있게 한다.
        /// </summary>
        public static ShopTraderDef NextLockedTrader(GameSave save, IDataRegistry data)
        {
            int trust = TrustOfEmployer(save);
            ShopTraderDef next = null;

            foreach (var t in data.Shop.Traders)
            {
                if (trust >= t.RequiresTrust) continue;
                if (next == null || t.RequiresTrust < next.RequiresTrust) next = t;
            }
            return next;
        }

        public static int TrustOfEmployer(GameSave save)
        {
            if (save.NpcTrust == null || string.IsNullOrEmpty(save.Player.EmployerNpcId)) return 0;
            int v;
            return save.NpcTrust.TryGetValue(save.Player.EmployerNpcId, out v) ? v : 0;
        }
    }
}
