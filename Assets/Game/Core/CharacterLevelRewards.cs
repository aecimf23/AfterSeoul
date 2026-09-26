using System;
using System.Collections.Generic;
using AfterSeoul.Inventory;

namespace AfterSeoul.Core
{
    /// <summary>A single character level's fixed, persisted reward.</summary>
    public sealed class CharacterLevelReward
    {
        public int Level { get; }
        public long Money { get; }
        public int WarehouseSlots { get; }
        public ItemStack[] Items { get; }

        internal CharacterLevelReward(int level, long money, int warehouseSlots, ItemStack[] items)
        {
            Level = level;
            Money = money;
            WarehouseSlots = warehouseSlots;
            Items = items;
        }
    }

    /// <summary>Grants earned levels once and tracks which saved receipts the player has seen.</summary>
    public static class CharacterLevelRewards
    {
        private const int FirstLevel = 2;
        private const int LastLevel = 20;

        public static CharacterLevelReward ForLevel(int level)
        {
            if (level < FirstLevel || level > LastLevel) return null;
            return new CharacterLevelReward(level, 10000L + 5000L * (level - FirstLevel),
                level % 5 == 0 ? 5 : 0,
                new[] { new ItemStack("MED05", 2), new ItemStack("FOOD01", 2), new ItemStack("FOOD02", 2) });
        }

        /// <summary>Already granted rewards awaiting acknowledgement in the saved UI.</summary>
        public static IReadOnlyList<CharacterLevelReward> Pending(GameSave save)
        {
            var rewards = new List<CharacterLevelReward>();
            if (save?.Player == null) return rewards;
            int last = Math.Min(LastLevel, save.Player.CharacterRewardedThrough);
            if (save.Player.CharacterRewardAcknowledgedThrough >= last) return rewards;
            int first = Math.Max(FirstLevel, save.Player.CharacterRewardAcknowledgedThrough + 1);
            for (int level = first; level <= last; level++) rewards.Add(ForLevel(level));
            return rewards;
        }

        /// <summary>The next level that can earn a reward, independent of unacknowledged receipts.</summary>
        public static CharacterLevelReward Next(GameSave save, BalanceDef balance)
        {
            if (save?.Player == null) return null;
            int cap = RewardCap(balance);
            if (save.Player.CharacterRewardedThrough >= cap) return null;
            int next = Math.Max(FirstLevel, save.Player.CharacterRewardedThrough + 1);
            return next <= cap ? ForLevel(next) : null;
        }

        /// <summary>Only a reward that has actually been granted may be acknowledged.</summary>
        public static bool Acknowledge(GameSave save, int throughLevel)
        {
            if (save?.Player == null || throughLevel < FirstLevel || throughLevel > LastLevel ||
                throughLevel <= save.Player.CharacterRewardAcknowledgedThrough ||
                throughLevel > save.Player.CharacterRewardedThrough) return false;
            save.Player.CharacterRewardAcknowledgedThrough = throughLevel;
            return true;
        }

        /// <summary>Grant every earned level, including levels present in older saves.</summary>
        public static int GrantEarned(GameSave save, IDataRegistry data)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            if (data == null) throw new ArgumentNullException(nameof(data));
            int target = Math.Min(Math.Max(1, save.Player.CharacterLevel), RewardCap(data.Balance));
            if (save.Player.CharacterRewardedThrough >= target) return 0;
            int first = Math.Max(FirstLevel, save.Player.CharacterRewardedThrough + 1);
            if (first > target) return 0;

            // Check the entire batch before mutating any game state. This keeps direct
            // Boot/Commit calls safe even when an imported save is near a numeric limit.
            long money = save.Player.Money;
            int capacity = save.Warehouse.Capacity;
            for (int level = first; level <= target; level++)
            {
                var reward = ForLevel(level);
                money = checked(money + reward.Money);
                capacity = checked(capacity + reward.WarehouseSlots);
            }
            checked { int totalCapacity = capacity + save.Warehouse.BonusCapacity; }

            save.Player.Money = money;
            save.Warehouse.Capacity = capacity;
            if (save.ExplorationOverflow == null) save.ExplorationOverflow = new List<ItemStack>();
            for (int level = first; level <= target; level++)
            {
                foreach (var item in ForLevel(level).Items)
                {
                    int overflow = Warehouse.TryAdd(save.Warehouse, data, item.ItemId, item.Count);
                    if (overflow > 0) save.ExplorationOverflow.Add(new ItemStack(item.ItemId, overflow));
                }
            }
            save.Player.CharacterRewardedThrough = target;
            return target - first + 1;
        }

        private static int RewardCap(BalanceDef balance) =>
            Math.Min(LastLevel, Math.Max(1, CharacterProgression.Tuning(balance).Curve.MaxLevel));
    }
}
