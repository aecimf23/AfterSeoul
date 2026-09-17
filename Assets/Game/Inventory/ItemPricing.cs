using System;
using AfterSeoul.Core;

namespace AfterSeoul.Inventory
{
    public static class ItemPricing
    {
        // Mainline GetAmmoPreparationCost prices ammo by stack; mobile inventory counts rounds.
        // Keep BasePrice intact for the separate PC transfer validation contract.
        public static double UnitValue(ItemDef item) => item == null ? 0 :
            item.Category == "Ammo" ? item.BasePrice / (double)Math.Max(1, item.MaxStack) : item.BasePrice;
    }
}
