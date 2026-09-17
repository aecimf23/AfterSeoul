using System;
using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Exploration
{
    public enum ExplorationPhase
    {
        Encounter,
        Combat,
        Routes,
        Result,
        EncounterResult,
        LootChoice
    }

    public enum EncounterChoice
    {
        Fight,
        Avoid,
        Search,
        Leave
    }

    public enum FireMode
    {
        Single,
        Burst,
        Auto
    }

    public enum ExplorationWeather
    {
        Clear,
        Rain,
        Fog
    }

    public enum EnemyAction
    {
        Alert,
        Aiming,
        Firing,
        Cover,
        Reloading,
        Injured
    }

    public enum ExplorationOutcome
    {
        Success,
        Death,
        Exhausted,
        Emergency
    }

    [Serializable]
    public sealed class ExplorationState
    {
        public string Uid, MapId, Location, EncounterKind;
        public int NodeCount, NodeIndex, IntermediateExitIndex, Ammo, ShotsSinceReload;
        public uint RngState;
        public string[] Routes = new string[2];
        public string[] RouteContainers = new string[2];
        public string ContainerKind;
        public List<ItemStack> LootOptions = new List<ItemStack>();
        public bool FirstQuestContainerSearched;
        public bool AwaitingEntryChoice;
        public ExplorationWeather Weather;
        public ExplorationPhase Phase;
        public bool Detected, Paused, EncounterRewarded;
        public ExplorationEnemy Enemy;
        public List<ItemStack> Supplies = new List<ItemStack>(), Loot = new List<ItemStack>();
        public List<ItemStack> EncounterLoot = new List<ItemStack>();
        public double CoverRemaining, CoverCooldown, AttackCooldown, UseRemaining;
        public string PendingItemId;
        public ExplorationResult Result;
    }

    [Serializable]
    public sealed class ExplorationEnemy
    {
        public string Name, Kind, WeaponId;
        public double Hp = 70, MaxHp = 70, Remaining = 1.2;
        public EnemyAction Action = EnemyAction.Alert;
    }

    [Serializable]
    public sealed class ExplorationResult
    {
        public string Id, MapId, KillerName, KillerKind, KillerWeaponId, Cause;
        public int CharacterLevel;
        public ExplorationOutcome Outcome;
        public List<ItemStack> Items = new List<ItemStack>(), LostLoot = new List<ItemStack>();
        public bool Settled, Acknowledged;
    }
}
