using System.Collections.Generic;
using AfterSeoul.Core;
using AfterSeoul.Inventory;

namespace AfterSeoul.Exploration
{
    public static class PlayerEquipment
    {
        public static readonly string[] Slots = {"Weapon", "Melee", "Headwear", "BodyArmor", "Earpiece", "TacticalRig", "Backpack"};
        public static string SlotFor(ItemDef def)
        {
            if (def == null || !def.Equippable)
                return null;
            if (def.Slot == "Melee" || def.Slot == "MeleeWeapon" || (def.Id != null && def.Id.StartsWith("MEL")))
                return "Melee";
            if (def.EquipSlot == "Weapon" && CombatProfiles.For(def.Id) == null)
                return null;
            return Scav.EquipSlot.IsValid(def.EquipSlot) ? def.EquipSlot : null;
        }

        public static string Equipped(GameSave save, string slot)
        {
            string id;
            return save.Player.Equipment != null && save.Player.Equipment.TryGetValue(slot, out id) ? id : null;
        }

        public static bool TryEquip(GameSave save, IDataRegistry data, string itemId)
        {
            if (ExplorationSystem.IsActive(save))
                return false;
            var slot = SlotFor(data.GetItem(itemId));
            if (slot == null || Warehouse.CountOf(save.Warehouse, itemId) < 1)
                return false;
            var trial = new WarehouseState{Capacity = save.Warehouse.Capacity, BonusCapacity = save.Warehouse.BonusCapacity, Stacks = new List<ItemStack>(save.Warehouse.Stacks)};
            Warehouse.TryRemove(trial, itemId, 1);
            var old = Equipped(save, slot);
            if (old != null && Warehouse.TryAdd(trial, data, old, 1) > 0)
                return false;
            save.Warehouse.Stacks = trial.Stacks;
            if (save.Player.Equipment == null)
                save.Player.Equipment = new Dictionary<string, string>();
            save.Player.Equipment[slot] = itemId;
            return true;
        }

        public static bool TryUnequip(GameSave save, IDataRegistry data, string slot)
        {
            if (ExplorationSystem.IsActive(save))
                return false;
            var old = Equipped(save, slot);
            if (old == null || Warehouse.TryAdd(save.Warehouse, data, old, 1) > 0)
                return false;
            save.Player.Equipment.Remove(slot);
            return true;
        }
    }
}
