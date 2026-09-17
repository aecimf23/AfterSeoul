using System;
using System.Collections.Generic;
using AfterSeoul.Exploration;

namespace AfterSeoul.Core
{
    [Serializable]
    public sealed class FirstExplorationQuestProgress
    {
        public bool Accepted, ReadyToReport, Completed;
        public string EmployerId;
    }

    public static class FirstExplorationQuest
    {
        static bool KnownEmployer(string id) => id == "HWANG" || id == "DR_CHOI" || id == "YONGSAN_KIM";
        static string Employer(GameSave save) => save?.FirstExplorationQuest?.Accepted == true
            ? save.FirstExplorationQuest.EmployerId : save?.Player?.EmployerNpcId;
        static bool Busy(GameSave save) => ExplorationSystem.IsActive(save)
            || (save.Exploration?.Result != null && !save.Exploration.Result.Acknowledged);
        static bool InProgress(GameSave save) => save?.FirstExplorationQuest?.Accepted == true
            && !save.FirstExplorationQuest.Completed && KnownEmployer(save.FirstExplorationQuest.EmployerId)
            && save.FirstExplorationQuest.EmployerId == save.Player?.EmployerNpcId;

        public static bool IsPending(GameSave save) => KnownEmployer(save?.Player?.EmployerNpcId)
            && save.FirstExplorationQuest?.Completed != true;

        public static bool Accept(GameSave save)
        {
            if (!IsPending(save) || save.FirstExplorationQuest?.Accepted == true || Busy(save)) return false;
            save.FirstExplorationQuest = new FirstExplorationQuestProgress
            {
                Accepted = true,
                EmployerId = save.Player.EmployerNpcId
            };
            return true;
        }

        public static string RequiredContainer(GameSave save)
        {
            switch (Employer(save))
            {
                case "HWANG": return "Pocket";
                case "DR_CHOI": return "Medical";
                case "YONGSAN_KIM": return "Tool";
                default: return null;
            }
        }

        // Called only after the player has actually claimed an item from a container.
        public static void OnContainerLooted(GameSave save, string kind)
        {
            if (!InProgress(save) || save.FirstExplorationQuest.ReadyToReport || !ExplorationSystem.IsActive(save)
                || save.Exploration.MapId != "YONGSAN_MARKET" || kind != RequiredContainer(save)) return;
            save.Exploration.FirstQuestContainerSearched = true;
        }

        public static void OnSuccessfulReturn(GameSave save)
        {
            if (!InProgress(save) || save.Exploration?.MapId != "YONGSAN_MARKET"
                || !save.Exploration.FirstQuestContainerSearched
                || save.Exploration.Result == null || save.Exploration.Result.Outcome != ExplorationOutcome.Success) return;
            save.FirstExplorationQuest.ReadyToReport = true;
        }

        public static bool Report(GameSave save)
        {
            if (!InProgress(save) || !save.FirstExplorationQuest.ReadyToReport || Busy(save)) return false;
            var quest = save.FirstExplorationQuest;
            quest.Completed = true;
            quest.ReadyToReport = false;
            save.Player.Money += 25000;
            if (save.NpcTrust == null) save.NpcTrust = new Dictionary<string, int>();
            save.NpcTrust.TryGetValue(quest.EmployerId, out var trust);
            save.NpcTrust[quest.EmployerId] = trust + 2;
            return true;
        }

        public static string Title(GameSave save)
        {
            switch (Employer(save))
            {
                case "DR_CHOI": return Loc.Text("주민들을 위한 첫 왕진 준비");
                case "YONGSAN_KIM": return Loc.Text("다시 쓸 수 있는 부품");
                default: return Loc.Text("쓸모를 증명하는 첫 보급");
            }
        }

        public static string Offer(GameSave save)
        {
            switch (Employer(save))
            {
                case "DR_CHOI": return Loc.Text("서울에 남으시기로 했군요. 이곳 주민들도 아직 도움이 필요해요. 용산 전자상가 주변을 살펴보고 의료 상자에 쓸 수 있는 물품이 남아 있는지 확인해 주시겠어요? 하나 골라 챙기시고, 필요하면 쓰셔도 돼요. 무사히 돌아오셔서 상황을 알려 주세요.");
                case "YONGSAN_KIM": return Loc.Text("남아서 버틸 거면 고쳐 쓸 줄도 알아야지. 용산 전자상가 공구 상자에 쓸 만한 재료가 남았는지 봐 줘. 하나 골라 챙겨 보고, 돌아와서 뭘 찾았는지 알려 줘. 물건보다 네가 무사히 돌아오는 게 먼저야.");
                default: return Loc.Text("서울에 남겠다고? 그럼 네 몫을 해라. 용산 전자상가의 포켓을 뒤져 쓸 만한 물자가 남았는지 확인하고 하나 골라 회수해라. 현장에서 필요하면 사용해도 된다. 교전은 목적이 아니다. 살아서 복귀한 뒤 수색 결과를 보고해. 쓸모는 그때 판단하겠다.");
            }
        }

        public static string Objective(GameSave save)
        {
            switch (Employer(save))
            {
                case "DR_CHOI": return Loc.Text("용산 전자상가 의료 상자 수색 → 물품 선택 → 생존 귀환 → 보건의 최씨에게 보고");
                case "YONGSAN_KIM": return Loc.Text("용산 전자상가 공구 상자 수색 → 물품 선택 → 생존 귀환 → 용산 김씨에게 보고");
                default: return Loc.Text("용산 전자상가 포켓 수색 → 물품 선택 → 생존 귀환 → 황 상사에게 보고");
            }
        }

        public static string NextAction(GameSave save)
        {
            if (save.FirstExplorationQuest?.Accepted != true) return Loc.Text("첫 의뢰를 받아보세요");
            if (save.FirstExplorationQuest.ReadyToReport) return Loc.Text("귀환 완료 · 의뢰 보고");
            if (ExplorationSystem.IsActive(save) && save.Exploration.FirstQuestContainerSearched)
                return Loc.Text("조사 완료 · 탈출구에서 귀환");
            return Loc.Text("용산 전자상가 · {0} 수색", LootContainers.Name(RequiredContainer(save)));
        }

        public static string ReportLine(GameSave save)
        {
            switch (Employer(save))
            {
                case "DR_CHOI": return Loc.Text("무사히 돌아오셔서 다행이에요. 살펴봐 주신 덕분에 주민들께 필요한 물자를 준비할 수 있겠어요. 이건 약속드린 보수예요. 오늘은 몸부터 돌보세요.");
                case "YONGSAN_KIM": return Loc.Text("그런 재료가 남아 있었다고? 좋아, 어디를 뒤져야 할지 알겠네. 물건 보는 눈은 있어. 수고비 받아. 다음에도 쓸 만한 게 보이면 알려 줘. 무리해서 욕심내지는 말고.");
                default: return Loc.Text("물자 수색 보고, 생존 복귀 확인. 첫 임무는 합격이다. 보수를 지급하겠다. 다음에도 이 원칙을 지켜라. 물자보다 네 복귀가 먼저다.");
            }
        }
    }
}
