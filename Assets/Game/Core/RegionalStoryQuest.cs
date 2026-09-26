using System;
using System.Collections.Generic;
using AfterSeoul.Exploration;

namespace AfterSeoul.Core
{
    public sealed class RegionalStoryQuestDef
    {
        public string Map { get; }
        public string Npc { get; }
        public string Site { get; }
        public string ItemId { get; }

        public RegionalStoryQuestDef(string map, string npc, string site, string itemId)
        {
            Map = map;
            Npc = npc;
            Site = site;
            ItemId = itemId;
        }

        private string Key(int stage) => "AS_STORY_" + Map + (stage == 0 ? "_SCOUT" : "_RECOVER");
        public string Title(int stage) => Loc.Get(Key(stage) + "_TITLE");
        public string Offer(int stage) => Loc.Get(Key(stage) + "_OFFER");
        public string ReportLine(int stage) => Loc.Get(Key(stage) + "_REPORT");
    }

    [Serializable]
    public sealed class RegionalStoryQuestProgress
    {
        // 0: scout, 1: recover, 2: both reports completed.
        public int Stage;
        public bool Accepted, ReadyToReport;
        public string ExcludedRunId, ExcludedResultId;
        // A recovered quest item is reserved here before ordinary loot is stored.
        public string HeldItemId;
        public bool Completed => Stage >= 2;
    }

    public static class RegionalStoryQuest
    {
        public static RegionalStoryQuestProgress Progress(GameSave save, string map)
        {
            if (save?.RegionalStoryQuests == null || map == null) return null;
            save.RegionalStoryQuests.TryGetValue(map, out var progress);
            return progress;
        }

        public static bool IsAvailable(GameSave save, string map)
        {
            if (save?.FirstExplorationQuest?.Completed != true || RegionalStoryQuestCatalog.Find(map) == null ||
                ExplorationSystem.RouteLockReason(save, map) != null) return false;
            return map == "YONGSAN_MARKET" || RegionalExplorationQuest.Progress(save, map)?.Accepted == true;
        }

        public static bool CanOffer(GameSave save, string map)
        {
            var progress = Progress(save, map);
            return IsAvailable(save, map) && (progress == null || progress.Stage >= 0 && !progress.Completed && !progress.Accepted);
        }

        private static bool Busy(GameSave save) => save.Exploration != null &&
            (save.Exploration.Result == null || !save.Exploration.Result.Acknowledged);

        public static bool Accept(GameSave save, string map)
        {
            if (!CanOffer(save, map) || Busy(save)) return false;
            if (save.RegionalStoryQuests == null)
                save.RegionalStoryQuests = new Dictionary<string, RegionalStoryQuestProgress>();
            var progress = Progress(save, map);
            if (progress == null)
                save.RegionalStoryQuests[map] = progress = new RegionalStoryQuestProgress();
            progress.Accepted = true;
            progress.ReadyToReport = false;
            progress.HeldItemId = null;
            progress.ExcludedRunId = save.Exploration?.Uid;
            progress.ExcludedResultId = save.Exploration?.Result?.Id;
            return true;
        }

        public static void PrepareFirstRoute(GameSave save)
        {
            var run = save?.Exploration;
            var def = run == null ? null : RegionalStoryQuestCatalog.Find(run.MapId);
            var progress = def == null ? null : Progress(save, def.Map);
            if (progress?.Accepted != true || progress.ReadyToReport || progress.Completed) return;
            var site = RaidRegions.Find(def.Map, def.Site);
            if (site == null || site.Dangerous || run.Routes == null || run.Routes.Length == 0) return;
            run.Routes[0] = site.Name;
            run.RouteContainers[0] = site.Container;
            run.RouteDangerous[0] = site.Dangerous;
            run.RouteIndoors[0] = site.Indoors;
        }

        public static long MoneyReward(GameSave save, string map)
        {
            if (RegionalStoryQuestCatalog.Find(map) == null) return 0;
            int stage = Progress(save, map)?.Stage ?? 0;
            return stage == 0 ? 8000 : stage == 1 ? 12000 : 0;
        }

        public static long ExperienceReward(GameSave save, string map)
        {
            if (RegionalStoryQuestCatalog.Find(map) == null) return 0;
            int stage = Progress(save, map)?.Stage ?? 0;
            return stage == 0 ? 75 : stage == 1 ? 100 : 0;
        }

        public static bool Report(GameSave save, string map, IDataRegistry data)
        {
            var def = RegionalStoryQuestCatalog.Find(map);
            var progress = Progress(save, map);
            if (def == null || data == null || progress?.Accepted != true || !progress.ReadyToReport ||
                progress.Stage < 0 || progress.Completed || Busy(save) ||
                (progress.Stage == 1 && progress.HeldItemId != def.ItemId)) return false;

            long money = checked(save.Player.Money + MoneyReward(save, map));
            int oldTrust = 0;
            if (save.NpcTrust != null) save.NpcTrust.TryGetValue(def.Npc, out oldTrust);
            int trust = checked(oldTrust + 1);
            long experience = ExperienceReward(save, map);
            save.Player.Money = money;
            CharacterProgression.Award(save, experience, data.Balance);
            if (save.NpcTrust == null) save.NpcTrust = new Dictionary<string, int>();
            save.NpcTrust[def.Npc] = trust;
            progress.Stage++;
            progress.Accepted = false;
            progress.ReadyToReport = false;
            progress.HeldItemId = null;
            return true;
        }

        // Called after the normal first/regional quest checks but before loot is stored.
        public static void OnSuccessfulReturn(GameSave save)
        {
            var run = save?.Exploration;
            var result = run?.Result;
            var def = run == null ? null : RegionalStoryQuestCatalog.Find(run.MapId);
            var progress = def == null ? null : Progress(save, def.Map);
            if (progress?.Accepted != true || progress.ReadyToReport || progress.Completed ||
                result?.Outcome != ExplorationOutcome.Success || !run.StorySiteVisited ||
                (!string.IsNullOrEmpty(progress.ExcludedRunId) && progress.ExcludedRunId == run.Uid) ||
                (!string.IsNullOrEmpty(progress.ExcludedResultId) && progress.ExcludedResultId == result.Id)) return;

            if (progress.Stage == 0)
            {
                if (run.NodeIndex >= 2) progress.ReadyToReport = true;
                return;
            }
            if (progress.Stage != 1 || !run.StoryItemCollected || run.Loot == null) return;
            for (int i = 0; i < run.Loot.Count; i++)
            {
                var item = run.Loot[i];
                if (item.ItemId != def.ItemId || item.Count <= 0) continue;
                if (item.Count == 1) run.Loot.RemoveAt(i);
                else run.Loot[i] = new ItemStack(item.ItemId, item.Count - 1);
                progress.HeldItemId = def.ItemId;
                progress.ReadyToReport = true;
                result.StoryQuestItemId = def.ItemId;
                return;
            }
        }

        // Enter is also called for the starting street: only an actual chosen route counts.
        public static void OnEnter(GameSave save, IDataRegistry data)
        {
            var run = save?.Exploration;
            var def = run == null ? null : RegionalStoryQuestCatalog.Find(run.MapId);
            var progress = def == null ? null : Progress(save, def.Map);
            if (progress?.Accepted != true || progress.ReadyToReport || progress.Completed ||
                run.NodeIndex < 0 || run.Location != def.Site) return;
            run.StorySiteVisited = true;
            if (progress.Stage != 1 || run.StoryItemOffered || run.LootOptions == null || data == null) return;
            var site = RaidRegions.Find(def.Map, def.Site);
            var item = data.GetItem(def.ItemId);
            if (site == null || !LootContainers.AvailableIn(item, site.Container) || run.LootOptions.Count == 0) return;
            int choice = run.LootOptions.FindIndex(x => x.ItemId == def.ItemId);
            if (choice < 0) choice = run.LootOptions.Count - 1;
            run.LootOptions[choice] = new ItemStack(def.ItemId, 1);
            run.StoryItemOffered = true;
        }

        public static void OnChosenLoot(GameSave save, string itemId)
        {
            var run = save?.Exploration;
            var def = run == null ? null : RegionalStoryQuestCatalog.Find(run.MapId);
            var progress = def == null ? null : Progress(save, def.Map);
            if (progress?.Accepted == true && !progress.ReadyToReport && progress.Stage == 1 &&
                run.StorySiteVisited && run.Location == def.Site && itemId == def.ItemId)
                run.StoryItemCollected = true;
        }

        /// <summary>
        /// A chosen story item is only evidence while at least one matching raid-loot
        /// unit remains. The game stacks equal IDs, so any remaining unit retains the
        /// mark; losing the entire stack clears it before later finds elsewhere.
        /// </summary>
        public static void ReconcileCarriedItem(GameSave save)
        {
            var run = save?.Exploration;
            if (run?.StoryItemCollected != true) return;
            var def = RegionalStoryQuestCatalog.Find(run.MapId);
            if (def == null) return;
            if (HasItem(run.Loot, def.ItemId) || HasItem(run.PendingLoot, def.ItemId)) return;
            run.StoryItemCollected = false;
        }

        private static bool HasItem(List<ItemStack> items, string itemId)
        {
            if (items == null) return false;
            foreach (var item in items)
                if (item.ItemId == itemId && item.Count > 0) return true;
            return false;
        }
    }
}
