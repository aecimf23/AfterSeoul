using System;
using AfterSeoul.Core;

namespace AfterSeoul.Exploration
{
    public static class RaidEquipment
    {
        public static int ExtraLootTypes(ItemDef item)
        {
            string slot = PlayerEquipment.SlotFor(item);
            return slot == "Backpack" ? Math.Max(0, item.GridSlots / 4) : slot == "TacticalRig" ? Math.Max(0, item.GridSlots / 8) : 0;
        }

        public static int LootCapacity(GameSave save, IDataRegistry data)
        {
            string bag = PlayerEquipment.Equipped(save, "Backpack"), rig = PlayerEquipment.Equipped(save, "TacticalRig");
            return 8 + ExtraLootTypes(bag == null ? null : data.GetItem(bag))
                + ExtraLootTypes(rig == null ? null : data.GetItem(rig));
        }

        public static double ArmorReduction(GameSave save, IDataRegistry data)
        {
            double reduction = 0;
            foreach (string slot in new[] { "Headwear", "BodyArmor" })
            {
                string id = PlayerEquipment.Equipped(save, slot);
                reduction += (id == null ? 0 : data.GetItem(id)?.ArmorClass ?? 0) * .035;
            }
            return Math.Min(.6, reduction);
        }
    }
}
