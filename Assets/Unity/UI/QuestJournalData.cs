using System.Collections.Generic;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using AfterSeoul.Quest;

namespace AfterSeoul.Unity.UI
{
    internal sealed class QuestJournalEntry
    {
        internal string Id, Title, Objective, Reward, Map, QuestId, Npc, Description;
        internal bool Followup, Daily, Accepted, Ready, Completed;
    }

    /// <summary>Read-only views of the existing quests; opening the journal never grants rewards.</summary>
    internal static class QuestJournalData
    {
        internal static List<QuestJournalEntry> Build(GameSession session)
        {
            var save = session.Save;
            var entries = new List<QuestJournalEntry>();
            if (string.IsNullOrEmpty(save.Player.EmployerNpcId)) return entries;
            bool firstDone = save.FirstExplorationQuest?.Completed == true;
            entries.Add(new QuestJournalEntry { Id = "main:first", Title = FirstExplorationQuest.Title(save),
                Map = "YONGSAN_MARKET", Npc = save.Player.EmployerNpcId, Accepted = save.FirstExplorationQuest?.Accepted == true,
                Ready = save.FirstExplorationQuest?.ReadyToReport == true, Completed = firstDone,
                Objective = FirstExplorationQuest.Objective(save) + "\n" + Loc.Text("지금 할 일 · ") + (firstDone ? Loc.Text("보고 완료") : FirstExplorationQuest.NextAction(save)),
                Description = Loc.Text("첫 정찰을 마치고 고용주에게 생존과 수색 결과를 보고하세요."),
                Reward = Loc.Text("25,000원 · 신뢰 +2") });
            if (firstDone) foreach (var map in RegionalExplorationQuest.Maps) {
                if (ExplorationSystem.RouteLockReason(save, map) != null) continue;
                var p = RegionalExplorationQuest.Progress(save, map);
                string next = p?.ReadyToReport == true ? Loc.Text("복귀 완료 · 보고하고 보상 받기")
                    : p?.Accepted != true ? Loc.Text("지역 담당자에게 소개받기") : Loc.Text("지역을 정찰하고 물자를 챙겨 생존 탈출");
                entries.Add(new QuestJournalEntry { Id = "main:" + map, Title = Loc.Text("{0} 정찰", Loc.MapName(map)), Map = map,
                    Npc = RegionalExplorationQuest.Npc(map), Accepted = p?.Accepted == true, Ready = p?.ReadyToReport == true, Completed = p?.Completed == true,
                    Description = Loc.Text("지역 담당자와 연락하고 다음 탐색 경로를 확보하세요."),
                    Objective = RegionalExplorationQuest.Objective(map) + "\n" + Loc.Text("지금 할 일 · ") + next,
                    Reward = Loc.Text("15,000원 · 신뢰 +1") });
            }
            if(firstDone) foreach(var map in RegionalExplorationQuest.Maps) {
                var followup=RegionalExplorationQuest.FollowupProgress(save,map);
                if(!RegionalExplorationQuest.CanOfferFollowup(save,map) && followup?.Accepted!=true) continue;
                entries.Add(new QuestJournalEntry {Id="followup:"+map,Followup=true,Map=map,Npc=RegionalExplorationQuest.Npc(map),
                    Title=RegionalExplorationQuest.FollowupTitle(save,map),Objective=RegionalExplorationQuest.FollowupObjective(save,map),
                    Description=RegionalExplorationQuest.FollowupOffer(save,map),Accepted=followup?.Accepted==true,Ready=followup?.ReadyToReport==true,
                    Reward=Loc.Text("의뢰 보상 · {0}원 / 신뢰 +1",RegionalExplorationQuest.FollowupReward(save,map).ToString("N0"))});
            }
            var pool = session.Data.GetQuestPool(Employers.QuestPoolId(session.Data, save.Player.EmployerNpcId));
            foreach (var active in save.Quests.Active) {
                var def = pool?.FirstOrDefault(q => q.Id == active.QuestId);
                if (def == null) continue;
                var lines = new List<string>();
                foreach (var req in def.Requires) {
                    string name = !string.IsNullOrEmpty(req.ItemId) ? ItemPresentation.Name(session.Data, req.ItemId) : Loc.Text("{0} 계열", Loc.Text(req.Tag));
                    int have = !string.IsNullOrEmpty(req.ItemId) ? Warehouse.CountOf(save.Warehouse, req.ItemId) : Warehouse.CountByTag(save.Warehouse, session.Data, req.Tag);
                    lines.Add(active.Delivered ? Loc.Text("{0} · 납품 완료", name) : Loc.Text("{0} · {1}/{2}개", name, have, req.Count));
                }
                string subject = def.Requires.Length > 0 && !string.IsNullOrEmpty(def.Requires[0].ItemId)
                    ? ItemPresentation.Name(session.Data, def.Requires[0].ItemId) : def.Requires.Length > 0 ? Loc.Text("{0} 계열", Loc.Text(def.Requires[0].Tag)) : Loc.Text("보급 물자");
                entries.Add(new QuestJournalEntry { Id = "daily:" + def.Id, QuestId = def.Id, Daily = true, Accepted = true,
                    Npc = save.Player.EmployerNpcId, Title = Loc.Text("{0} 납품", subject),
                    Description = Loc.Text("창고에 모은 물자를 납품하면 보상을 받습니다. 장착 중인 장비는 납품하지 않습니다."),
                    Objective = string.Join("\n", lines), Reward = Loc.Text("{0} · 신뢰 +{1} · 경험치 +{2}", Theme.Won(def.RewardMoney), def.RewardTrust, def.RewardExp),
                    Completed = active.Delivered, Ready = !active.Delivered && DailyQuestSystem.MeetsRequirements(save, session.Data, def) });
            }
            return entries;
        }

        internal static QuestJournalEntry Current(GameSession session)
        {
            var entries = Build(session);
            var tracked = entries.FirstOrDefault(e => e.Id == session.Save.TrackedQuestId && !e.Completed);
            if (tracked != null) return tracked;
            bool daily = session.Save.TrackedQuestId?.StartsWith("daily:") == true;
            return entries.Where(e => !e.Completed).OrderBy(e => e.Daily == daily ? 0 : 1).ThenBy(e => e.Ready ? 0 : 1).FirstOrDefault();
        }

        internal static string ActionLabel(GameSession session, QuestJournalEntry entry)
        {
            if (entry.Completed) return Loc.Text("보상 수령 완료");
            var run = session.Save.Exploration;
            if (run != null && (run.Result == null || !run.Result.Acknowledged))
                return run.Result == null ? Loc.Text("탐색 이어하기") : Loc.Text("귀환 결과 확인");
            if (entry.Ready) return entry.Daily ? Loc.Text("물자 납품 · 보상 받기") : Loc.Text("보고 · 보상 받기");
            return entry.Daily ? Loc.Text("추적하고 물자 준비") : entry.Accepted ? Loc.Text("목표 지역으로 가기") : Loc.Text("담당자 만나기");
        }

        internal static string RaidObjective(GameSession session, QuestJournalEntry entry)
        {
            if (!entry.Daily || session.Save.Exploration == null) return entry.Objective;
            var pool = session.Data.GetQuestPool(Employers.QuestPoolId(session.Data, session.Save.Player.EmployerNpcId));
            var def = pool?.FirstOrDefault(q => q.Id == entry.QuestId);
            if (def == null) return entry.Objective;
            var lines = new List<string>();
            foreach (var req in def.Requires) {
                bool byId = !string.IsNullOrEmpty(req.ItemId);
                int stored = byId ? Warehouse.CountOf(session.Save.Warehouse, req.ItemId) : Warehouse.CountByTag(session.Save.Warehouse, session.Data, req.Tag);
                int carried = 0;
                foreach (var item in session.Save.Exploration.Loot)
                    if (byId ? item.ItemId == req.ItemId : session.Data.GetItem(item.ItemId)?.Tags?.Contains(req.Tag) == true) carried += item.Count;
                string name = byId ? ItemPresentation.Name(session.Data, req.ItemId) : Loc.Text(req.Tag);
                lines.Add(Loc.Text("{0} · 창고 {1} + 전리품 {2} / 필요 {3}", name, stored, carried, req.Count));
            }
            return string.Join("\n", lines) + "\n" + Loc.Text("전리품은 생존 귀환 후 납품할 수 있습니다.");
        }
    }
}
