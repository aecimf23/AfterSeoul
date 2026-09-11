using System;
using AfterSeoul.Core;

namespace AfterSeoul.Inventory
{
    /// <summary>
    /// 창고 물품 판매. 판매가 = <c>basePrice × balance.sellPriceRatio</c>.
    ///
    /// 본편 가격을 그대로 쓴다 (DATA_SCHEMA §3-1). 모바일 전용 가격표를 두면 본편 아이템이
    /// 바뀔 때마다 두 표를 맞춰야 하고, 전송 한도 계산이 두 가격 사이에서 흔들린다.
    /// </summary>
    public static class Market
    {
        public static long SellPrice(IDataRegistry data, string itemId)
        {
            var def = data.GetItem(itemId);
            if (def == null) return 0;
            return (long)Math.Floor(def.BasePrice * data.Balance.SellPriceRatio);
        }

        /// <summary>
        /// 판다. 수량이 모자라면 아무것도 하지 않고 false.
        /// 차감은 전부 아니면 무다 (ARCHITECTURE §8) — 절반만 팔리는 판매는 없다.
        /// </summary>
        public static bool TrySell(GameSave save, IDataRegistry data, string itemId, int count)
        {
            if (count <= 0) return false;

            long unit = SellPrice(data, itemId);
            if (unit <= 0) return false;   // 모르는 아이템이나 가격 0 은 받지 않는다

            if (!Warehouse.TryRemove(save.Warehouse, itemId, count)) return false;
            save.Player.Money += unit * count;
            return true;
        }
    }
}
