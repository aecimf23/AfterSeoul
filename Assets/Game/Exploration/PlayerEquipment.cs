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
            if(slot=="Weapon" && ExplorationSystem.IsActive(save) && save.Exploration.RecoveryRun) return "WPN04";
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

        public static string EquipBlockReason(GameSave save, IDataRegistry data, string itemId)
        {
            if (ExplorationSystem.IsActive(save)) return "탐색을 마치고 돌아온 뒤 장비를 교체할 수 있습니다.";
            string slot = SlotFor(data.GetItem(itemId));
            if (slot == null) return "현재 착용할 수 없는 아이템입니다.";
            if (Warehouse.CountOf(save.Warehouse, itemId) < 1) return "창고에 남아 있는 장비가 없습니다.";
            var trial = new WarehouseState { Capacity = save.Warehouse.Capacity, BonusCapacity = save.Warehouse.BonusCapacity,
                Stacks = new List<ItemStack>(save.Warehouse.Stacks) };
            Warehouse.TryRemove(trial, itemId, 1);
            string old = Equipped(save, slot);
            return old != null && Warehouse.TryAdd(trial, data, old, 1) > 0
                ? "기존 장비를 돌려놓을 창고 공간이 부족합니다." : null;
        }

        public static string UnequipBlockReason(GameSave save, IDataRegistry data, string slot)
        {
            if (ExplorationSystem.IsActive(save)) return "탐색을 마치고 돌아온 뒤 장비를 해제할 수 있습니다.";
            string old = Equipped(save, slot);
            if (old == null) return "착용한 장비가 없습니다.";
            var trial = new WarehouseState { Capacity = save.Warehouse.Capacity, BonusCapacity = save.Warehouse.BonusCapacity,
                Stacks = new List<ItemStack>(save.Warehouse.Stacks) };
            return Warehouse.TryAdd(trial, data, old, 1) > 0 ? "장비를 돌려놓을 창고 공간이 부족합니다." : null;
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
