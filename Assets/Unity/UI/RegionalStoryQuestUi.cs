using System;
using System.Linq;
using AfterSeoul.Core;
using AfterSeoul.Exploration;
using AfterSeoul.Inventory;
using UnityEngine;
using UnityEngine.UI;

namespace AfterSeoul.Unity.UI
{
    internal static class StoryQuestPresentation
    {
        internal static string Objective(GameSession session, string map)
        {
            var def = RegionalStoryQuestCatalog.Find(map);
            if (def == null) return "";
            var progress = RegionalStoryQuest.Progress(session.Save, map);
            if ((progress?.Stage ?? 0) == 0)
                return Loc.Text("{0} · {1} 방문 → 탐색 지점 3곳 이상 → 생존 귀환 후 보고", Loc.MapName(map), Loc.Text(def.Site));
            var site = RaidRegions.Find(map, def.Site);
            return Loc.Text("{0} · {1}의 {2}에서 {3} 1개 회수 → 생존 귀환 → 보고 시 전달",
                Loc.MapName(map), Loc.Text(def.Site), LootContainers.Name(site.Container), ItemPresentation.Name(session.Data, def.ItemId));
        }

        internal static RegionalStoryQuestDef Active(GameSave save, string map)
        {
            var p = RegionalStoryQuest.Progress(save, map);
            return p?.Accepted == true && !p.Completed && !p.ReadyToReport ? RegionalStoryQuestCatalog.Find(map) : null;
        }

        internal static string Progress(GameSession session, string map)
        {
            var p = RegionalStoryQuest.Progress(session.Save, map);
            if (p?.Completed == true) return Loc.Text("지역 의뢰 2/2 완료");
            if (p?.ReadyToReport == true) return Loc.Text("복귀 완료 · 보고하고 보상 받기");
            int stage = p?.Stage ?? 0;
            if (p?.Accepted != true) return Loc.Text("지역 의뢰 {0}/2 · 담당자에게 의뢰 받기", stage + 1);
            var run = session.Save.Exploration;
            bool valid = ExplorationSystem.IsActive(session.Save) && run.MapId == map && run.Uid != p.ExcludedRunId;
            if (stage == 0)
                return Loc.Text("현장 확인 {0}/1 · 탐색 {1}/3 · 생존 귀환 필요", valid && run.StorySiteVisited ? 1 : 0, valid ? Math.Max(0, Math.Min(3, run.NodeIndex + 1)) : 0);
            var def = RegionalStoryQuestCatalog.Find(map);
            bool carried = valid && run.StoryItemCollected && run.Loot.Any(x => x.ItemId == def.ItemId && x.Count > 0);
            return Loc.Text("의뢰 물품 회수 {0}/1 · 사용하거나 버리지 말고 생존 귀환", carried ? 1 : 0);
        }

        internal static string Reward(GameSession session, string map) =>
            RegionalStoryQuest.Progress(session.Save, map)?.Completed == true ? Loc.Text("보상 수령 완료") :
            Loc.Text("의뢰 보상 · {0}원 / 신뢰 +1", RegionalStoryQuest.MoneyReward(session.Save, map).ToString("N0"))
                + "\n" + Loc.Text("캐릭터 경험치 +{0}", RegionalStoryQuest.ExperienceReward(session.Save, map));
    }

    public sealed partial class ExplorationView
    {
        private Text _storyLiveGoal;

        internal void FocusStoryQuest(string map)
        {
            if (HasRun || !_session.Save.ExplorationStarterPrepared || !RegionalStoryQuest.IsAvailable(_session.Save, map)) return;
            ShowStoryQuest(map);
        }

        private void ShowStoryQuest(string map)
        {
            var def = RegionalStoryQuestCatalog.Find(map);
            var p = RegionalStoryQuest.Progress(_session.Save, map);
            if (def == null || p?.Completed == true || !RegionalStoryQuest.IsAvailable(_session.Save, map)) return;
            bool report = p?.ReadyToReport == true;
            bool accept = p?.Accepted != true;
            int stage = p?.Stage ?? 0;
            string reward = StoryQuestPresentation.Reward(_session, map);
            OpenModal(Loc.TraderName(def.Npc), body => {
                GameArt.Portrait("StoryQuestPortrait", body, def.Npc, 210);
                Text(body, "StoryQuestTitle", def.Title(stage), 88, Theme.Accent, 33);
                Text(body, "StoryQuestOffer", report ? def.ReportLine(stage) : def.Offer(stage), 265, Theme.Text, 30);
                Text(body, "StoryQuestObjective", StoryQuestPresentation.Objective(_session, map), 150, Theme.Info, 26);
                Text(body, "StoryQuestProgress", StoryQuestPresentation.Progress(_session, map), 85, Theme.Info, 26);
                if (stage == 1)
                    Text(body, "StoryQuestStorage", Loc.Text("회수품 1개는 귀환 후 의뢰용으로 따로 보관하고, 보고할 때 전달합니다."), 95, Theme.TextDim, 26);
                Text(body, "StoryQuestReward", reward, 100, Theme.Safe, 28);
                Button(body, report ? "StoryQuestReport" : accept ? "StoryQuestAccept" : "StoryQuestPrepare",
                    report ? Loc.Text("보고 · 보상 받기") : accept ? Loc.Text("의뢰를 맡고 준비하기") : Loc.Text("목표 지역 출발 준비"), () => {
                        if (report || accept) {
                            if (!Command(s => {
                                bool changed = report ? RegionalStoryQuest.Report(s, map, _session.Data) : RegionalStoryQuest.Accept(s, map);
                                if (changed) s.TrackedQuestId = "story:" + map;
                                return changed;
                            }, false)) return;
                        }
                        CloseModal(); Render();
                        if (report) { Sfx.Complete(); ShowStoryReceipt(def, stage, reward); }
                        else ShowMapDetail(map);
                    }, accent: true);
            });
            PinModalAction(report ? "StoryQuestReport" : accept ? "StoryQuestAccept" : "StoryQuestPrepare");
        }

        private void ShowStoryReceipt(RegionalStoryQuestDef def, int reportedStage, string reward)
        {
            OpenModal(Loc.TraderName(def.Npc), body => {
                GameArt.Portrait("StoryReceiptPortrait", body, def.Npc, 210);
                Text(body, "StoryReceiptTitle", Loc.Text("완료 · ") + def.Title(reportedStage), 100, Theme.Accent, 33);
                Text(body, "StoryReceiptDialogue", def.ReportLine(reportedStage), 265, Theme.Text, 30);
                Text(body, "StoryQuestReceipt", Loc.Text("완료! 받은 보상 · {0}", reward), 150, Theme.Safe, 28);
                if (reportedStage == 0)
                    Text(body, "StoryNextTitle", Loc.Text("다음 추적 목표 · {0}", def.Title(1)), 100, Theme.Info, 28);
                Button(body, "StoryQuestNext", reportedStage == 0 ? Loc.Text("다음 목표 보기") : Loc.Text("목표 지역 출발 준비"), () => {
                    CloseModal();
                    if (reportedStage == 0) ShowStoryQuest(def.Map); else ShowMapDetail(def.Map);
                }, accent: true);
            });
            PinModalAction("StoryQuestNext");
        }
    }
}
