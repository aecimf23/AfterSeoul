using System;
using AfterSeoul.Core;

namespace AfterSeoul.Inventory
{
    /// <summary>
    /// 창고 물품 판매. 판매가 = <c>unitValue × balance.sellPriceRatio</c>.
    ///
    /// 본편 묶음 가격을 사용하되 탄약은 발당 가격으로 환산한다 (DATA_SCHEMA §3-1). 모바일 전용 가격표를 두면 본편 아이템이
    /// 바뀔 때마다 두 표를 맞춰야 하고, 전송 한도 계산이 두 가격 사이에서 흔들린다.
    /// </summary>
    public static class Market
    {
        /// <summary>고용주를 모르는 자리에서 쓰는 기본가. 보너스가 붙지 않는다.</summary>
        public static long SellPrice(IDataRegistry data, string itemId)
        {
            var def = data.GetItem(itemId);
            if (def == null) return 0;
            return (long)Math.Floor(ItemPricing.UnitValue(def) * data.Balance.SellPriceRatio);
        }

        /// <summary>
        /// 실제로 받는 값. 고용주 보너스가 붙는다 (GDD §4).
        ///
        /// <para><b>화면과 실제가 같은 함수를 써야 한다.</b> 목록에 적힌 값과 팔았을 때 들어온 값이
        /// 다르면, 그건 보너스가 아니라 버그로 읽힌다.</para>
        /// </summary>
        public static long SellPrice(GameSave save, IDataRegistry data, string itemId)
        {
            var def = data.GetItem(itemId);
            if (def == null) return 0;

            double ratio = data.Balance.SellPriceRatio + Employers.SellPriceBonus(save, data);
            return (long)Math.Floor(ItemPricing.UnitValue(def) * ratio);
        }

        /// <summary>
        /// 판다. 수량이 모자라면 아무것도 하지 않고 false.
        /// 차감은 전부 아니면 무다 (ARCHITECTURE §8) — 절반만 팔리는 판매는 없다.
        /// </summary>
        public static bool TrySell(GameSave save, IDataRegistry data, string itemId, int count)
        {
            if (count <= 0) return false;

            long unit = SellPrice(save, data, itemId);   // 고용주 보너스 포함
            if (unit <= 0) return false;   // 모르는 아이템이나 가격 0 은 받지 않는다

            if (!Warehouse.TryRemove(save.Warehouse, itemId, count)) return false;
            save.Player.Money += unit * count;
            return true;
        }
    }
}
