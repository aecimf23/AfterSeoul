using System;
using System.Collections.Generic;
using AfterSeoul.Exploration;

namespace AfterSeoul.Core
{
    [Serializable] public sealed class RegionalQuestProgress
    {
        public bool Accepted, ReadyToReport, Completed;
        public RegionalFollowupProgress Followup;
    }

    [Serializable] public sealed class RegionalFollowupProgress
    {
        // Completed introductions retain their original flags in old and new saves.
        public int Stage;
        public bool Accepted, ReadyToReport;
        public string ExcludedRunId, ExcludedResultId;
        public bool Completed => Stage >= 2;
    }

    // Mobile introductions adapt the original traders' roles, not their entire PC quest chain.
    public static class RegionalExplorationQuest
    {
        public static readonly string[] Maps = { "GURO_FACTORY", "HAN_RIVER", "NAMSAN_WOODS", "GANGNAM_STREETS", "YONGSAN_BASE", "MYEONGDONG", "UIJEONGBU" };
        public static string Npc(string map)
        {
            switch (map) {
                case "GURO_FACTORY": return "DONGDAEMUN_CHOI";
                case "HAN_RIVER": return "DOKKAEBI";
                case "NAMSAN_WOODS": return "WILDMAN";
                case "GANGNAM_STREETS": return "HWANG";
                case "YONGSAN_BASE": case "UIJEONGBU": return "US_LIAISON";
                case "MYEONGDONG": return "BROKER";
                default: return null;
            }
        }
        public static string Introducer(GameSave save, string map)
        {
            switch (map) {
                case "HAN_RIVER": return "YONGSAN_KIM";
                case "NAMSAN_WOODS": return "HWANG";
                case "GANGNAM_STREETS": return "DOKKAEBI";
                case "YONGSAN_BASE": return "HWANG";
                case "MYEONGDONG": return "DOKKAEBI";
                case "UIJEONGBU": return "WILDMAN";
                default: return save.Player.EmployerNpcId;
            }
        }
        public static RegionalQuestProgress Progress(GameSave save, string map)
        {
            RegionalQuestProgress progress;
            return save.RegionalExplorationQuests != null && save.RegionalExplorationQuests.TryGetValue(map, out progress) ? progress : null;
        }
        public static bool NeedsIntroduction(GameSave save, string map) => Npc(map) != null && Progress(save, map)?.Accepted != true;
        static bool Busy(GameSave save) => save.Exploration != null && (save.Exploration.Result == null || !save.Exploration.Result.Acknowledged);
        public static bool Accept(GameSave save, string map)
        {
            if (!NeedsIntroduction(save, map) || Busy(save) || ExplorationSystem.RouteLockReason(save, map) != null) return false;
            if (save.RegionalExplorationQuests == null) save.RegionalExplorationQuests = new Dictionary<string, RegionalQuestProgress>();
            save.RegionalExplorationQuests[map] = new RegionalQuestProgress { Accepted = true };
            return true;
        }
        public static void OnSuccessfulReturn(GameSave save)
        {
            var run = save.Exploration;
            if (run == null) return;
            var progress = Progress(save, run.MapId);
            if (progress?.Accepted == true && !progress.Completed && run.Result?.Outcome == ExplorationOutcome.Success
                && run.NodeIndex >= 2 && run.Loot.Count > 0) progress.ReadyToReport = true;
            var followup = progress?.Followup;
            if (followup?.Accepted != true || followup.Completed || run.Result?.Outcome != ExplorationOutcome.Success
                || (!string.IsNullOrEmpty(followup.ExcludedRunId) && followup.ExcludedRunId == run.Uid)
                || (!string.IsNullOrEmpty(followup.ExcludedResultId) && followup.ExcludedResultId == run.Result.Id)) return;
            long quantity = 0;
            foreach (var item in run.Loot) if (item.Count > 0) quantity += item.Count;
            if (run.NodeIndex >= 3 + followup.Stage && quantity >= 1 + followup.Stage) followup.ReadyToReport = true;
        }
        public static bool Report(GameSave save, string map)
        {
            var progress = Progress(save, map);
            if (Busy(save) || progress?.ReadyToReport != true || progress.Completed) return false;
            progress.Completed = true; progress.ReadyToReport = false;
            save.Player.Money += 15000;
            if (save.NpcTrust == null) save.NpcTrust = new Dictionary<string, int>();
            save.NpcTrust.TryGetValue(Npc(map), out var trust); save.NpcTrust[Npc(map)] = trust + 1;
            return true;
        }
        public static RegionalFollowupProgress FollowupProgress(GameSave save, string map) => Progress(save, map)?.Followup;
        static bool HasFollowup(string map) => map == "GURO_FACTORY" || map == "HAN_RIVER" || map == "NAMSAN_WOODS";
        public static bool CanOfferFollowup(GameSave save, string map)
        {
            var progress = FollowupProgress(save, map);
            return HasFollowup(map) && Progress(save, map)?.Completed == true && progress?.Completed != true && progress?.Accepted != true;
        }
        public static bool AcceptFollowup(GameSave save, string map)
        {
            if (!CanOfferFollowup(save, map) || Busy(save) || ExplorationSystem.RouteLockReason(save, map) != null) return false;
            var introduction = Progress(save, map);
            if (introduction.Followup == null) introduction.Followup = new RegionalFollowupProgress();
            var progress = introduction.Followup;
            progress.Accepted = true;
            progress.ReadyToReport = false;
            progress.ExcludedRunId = save.Exploration?.Uid;
            progress.ExcludedResultId = save.Exploration?.Result?.Id;
            return true;
        }
        public static int FollowupReward(GameSave save, string map) => FollowupProgress(save, map)?.Stage >= 1 ? 30000 : 20000;
        public static bool ReportFollowup(GameSave save, string map)
        {
            var progress = FollowupProgress(save, map);
            if (!HasFollowup(map) || Busy(save) || progress?.Accepted != true || !progress.ReadyToReport || progress.Completed) return false;
            save.Player.Money += FollowupReward(save, map);
            if (save.NpcTrust == null) save.NpcTrust = new Dictionary<string, int>();
            save.NpcTrust.TryGetValue(Npc(map), out var trust); save.NpcTrust[Npc(map)] = trust + 1;
            progress.Stage++;
            progress.Accepted = false;
            progress.ReadyToReport = false;
            return true;
        }
        static bool SecondFollowup(GameSave save, string map) => FollowupProgress(save, map)?.Stage >= 1;
        public static string FollowupTitle(GameSave save, string map)
        {
            bool second = SecondFollowup(save, map);
            switch (map) {
                case "GURO_FACTORY": return Loc.Text(second ? "구로 거래선 · 다시 잇는 보급" : "구로 거래선 · 끊긴 운송로");
                case "HAN_RIVER": return Loc.Text(second ? "한강 밀수로 · 다음 화물의 길" : "한강 밀수로 · 사라진 화물");
                case "NAMSAN_WOODS": return Loc.Text(second ? "남산 생존선 · 돌아올 사람들" : "남산 생존선 · 숲속 우회로");
                default: return string.Empty;
            }
        }
        public static string FollowupObjective(GameSave save, string map) => SecondFollowup(save, map)
            ? Loc.Text("{0}에서 새 레이드 · 이동 4회 이상 · 물품 합계 2개 이상 지닌 채 생존 탈출 후 보고 (물품은 보관)", Loc.MapName(map))
            : Loc.Text("{0}에서 새 레이드 · 이동 3회 이상 · 물품 1개 이상 지닌 채 생존 탈출 후 보고 (물품은 보관)", Loc.MapName(map));
        public static string FollowupOffer(GameSave save, string map)
        {
            bool second = SecondFollowup(save, map);
            switch (map) {
                case "GURO_FACTORY": return Loc.Text(second
                    ? "자네가 확인한 길로 거래선을 다시 이으려 하오. 이번에는 공단 안쪽까지 살펴보고, 운반할 만한 물자를 두 개 이상 가져와 주시오. 어떤 물건인지는 상관없소. 회수품은 자네 몫이고, 나는 살아서 돌아온 보고에 값을 치르겠소."
                    : "첫 정찰은 잘했소. 하지만 예전 운송로가 어디까지 통하는지는 아직 모르오. 구로에서 지난번보다 한 걸음 더 들어가 길을 확인해 주시오. 물건 하나라도 들고 돌아올 수 있다면 거래를 다시 시작할 근거가 되겠소.");
                case "HAN_RIVER": return Loc.Text(second
                    ? "네 보고 덕에 다음 화물을 보낼 길이 보인다. 이번엔 한강 안쪽까지 돌아보고 쓸 만한 물건 두 개 이상 챙겨 와. 회수한 건 네가 가져. 난 그 길로 짐을 싣고도 살아 돌아올 수 있는지 확인하려는 거야."
                    : "사라진 화물 이야기는 기억하지? 네 첫 보고만으론 하역장까지 길이 이어지는지 부족해. 한강에서 조금 더 깊이 들어가 살피고 물건 하나라도 들고 와. 소문 말고 네 발로 확인한 길이 필요해.");
                case "NAMSAN_WOODS": return Loc.Text(second
                    ? "네가 찾은 우회로를 다른 생존자에게도 알려 주려 한다. 남산 안쪽까지 길을 확인하고 물자 두 개 이상을 지닌 채 돌아와라. 물건은 네가 써. 빈손으로 달아나는 길과 짐을 지고 돌아오는 길은 다르다."
                    : "남산에 남은 사람들이 다 큰길로 다닐 수 있는 건 아니다. 첫 정찰보다 더 들어가 우회할 만한 길을 살펴라. 쓸 수 있는 물건 하나도 챙겨 와. 살아 돌아온 다음에야 남에게 그 길을 알려 줄 수 있다.");
                default: return string.Empty;
            }
        }
        public static string FollowupReportLine(GameSave save, string map)
        {
            bool second = SecondFollowup(save, map);
            switch (map) {
                case "GURO_FACTORY": return Loc.Text(second ? "공단 안쪽에서도 짐을 지고 돌아왔구려. 이 보고로 거래선에 연락하겠소. 끊겼던 보급에 다시 기대를 걸어 볼 만하오. 약속한 보수를 받으시오." : "운송로가 아직 이어져 있다는 말이지. 좋소, 거래선에 전할 첫 근거가 생겼구려. 보수를 받아 가시오. 다음에는 실제로 짐을 나를 수 있을지 보겠소.");
                case "HAN_RIVER": return Loc.Text(second ? "좋아. 짐까지 챙겨서 돌아왔으니 다음 화물을 움직여 볼 만하겠네. 네 이름은 거래선에 제대로 기억시켜 두지. 약속한 몫이야." : "하역장 쪽으로 더 갈 수 있다는 거지? 이제 화물이 어디서 끊겼는지 범위를 좁힐 수 있겠다. 약속한 몫 받아. 다음 운송 전에 한 번 더 확인하자고.");
                case "NAMSAN_WOODS": return Loc.Text(second ? "짐을 지고도 살아 돌아왔군. 다른 사람들에게 전할 길이 하나 더 생겼다. 오늘 네가 한 일은 물건 몇 개보다 값지다. 보수를 받고 쉬어라." : "우회로를 직접 보고 돌아왔으면 됐다. 네 보고는 기억해 두겠다. 보수를 받아라. 다음에는 물자를 챙긴 사람도 지나갈 수 있을지 살펴보자.");
                default: return string.Empty;
            }
        }
        public static string Objective(string map) => Loc.Text("{0}에서 이동 2회 이상 · 물품 확보 · 생존 탈출 후 보고", Loc.MapName(map));
        public static string Introduction(string map)
        {
            switch (map) {
                case "GURO_FACTORY": return "용산에서 살아 돌아왔으니 다음 길을 알려 주지. 동대문 최씨는 장비를 손보며 구로를 오가는 거래선도 알고 있어. 내가 보냈다고 해. 먼저 그 사람 이야기를 들어 봐.";
                case "HAN_RIVER": return "탄약이 어디로 움직이는지 알려면 도깨비를 만나. 한강 세관의 밀수로를 아는 녀석이야. 말은 거칠어도 살아 돌아온 사람과의 약속은 지켜. 네 이름은 전해 둘게.";
                case "NAMSAN_WOODS": return "남산으로 갈 거면 와일드맨부터 만나. 산에서 살아남는 법은 그자가 가장 잘 안다. 내가 소개했다고 해. 지도를 믿기 전에 그 사람 말을 들어라.";
                case "GANGNAM_STREETS": return "강남까지 가겠다고? 그러면 황 상사한테 연락해 줄게. 그 양반은 허풍보다 직접 보고 돌아온 보고서를 쳐 줘. 빈말만 하면 바로 돌려보낼 거다.";
                case "YONGSAN_BASE": return "다음은 용산 기지다. 미군 연락책에게 네 이름을 전했다. 무장한 채 멋대로 경계를 넘지 말고, 먼저 연락을 받고 조사 범위를 확인해라.";
                case "MYEONGDONG": return "주요 경로는 전부 돌아봤군. 명동의 거래선을 찾으려면 장물아비에게 가 봐. 물건뿐 아니라 네가 아는 길에도 값을 매길 녀석이니 말은 골라서 해.";
                default: return "북쪽으로 갈 생각이면 미군 연락책과 먼저 이야기해라. 의정부에서 끊긴 보급 연락을 찾고 있다더군. 모르는 길에서는 돌아올 방법부터 정해 둬.";
            }
        }
        public static string Offer(string map)
        {
            switch (map) {
                case "GURO_FACTORY": return "소개받고 왔소? 나는 동대문 최씨요. 구로 폐공단을 가려면 옷부터 단단히 여미시오. 열린 통로와 노출된 길을 직접 확인하고, 쓸 만한 물건도 챙겨 오시오. 거래선에 소개할 사람인지 보겠소. 물건 때문에 목숨을 버리지는 마시오.";
                case "HAN_RIVER": return "아앙? 군인도 아니고, 거지 같은 옷차림의 약탈자가 한강 세관에 가겠다고? 그쪽에 굴러다니는 시체를 늘리긴 싫은데, 제대로 살아 돌아올 수는 있는 놈이냐? 내 밀수로에서 화물이 자꾸 사라져. 진입로와 하역장으로 이어지는 길을 살피고, 남은 물자도 확인해 와. 살아 돌아오면 네 이름 정도는 기억해 주지.";
                case "NAMSAN_WOODS": return "남산에 들어갈 거라면 총부터 믿지 마라. 잠깐 멈춰서 숲의 소리를 들어. 길이 어디서 끊기는지 살피고 쓸 수 있는 물자를 찾아 돌아와라. 싸움은 피할 수 있으면 피해. 돌아오지 못한 놈의 배낭은 아무 쓸모 없다.";
                case "GANGNAM_STREETS": return "강남 진입을 요청했나. 그럼 현장 판단부터 확인하겠다. 이동 가능한 통로와 우회로를 정찰하고 회수 가능한 물자를 확보해라. 적을 몇 명 잡았는지는 묻지 않겠다. 생환해서 네 눈으로 본 것만 보고해.";
                case "YONGSAN_BASE": return "소개는 받았습니다. 기지 주변 보급 통로를 확인할 인원이 필요합니다. 경계선과 우회 가능한 길을 조사하고 남은 물자를 확보하십시오. 교전 확대가 목적은 아닙니다. 안전하게 복귀해서 관측한 사실을 보고해 주십시오.";
                case "MYEONGDONG": return "명동까지 갈 길을 안다고? 좋아. 그 길이 오늘도 열려 있는지 확인해 와. 주변 물건 하나도 직접 가져오고. 말로만 들은 소문에는 값을 매기지 않아. 살아서 돌아온 네 이야기에 값을 주지.";
                default: return "의정부의 보급 연락이 끊겼습니다. 진입로와 귀환 가능한 통로를 직접 확인하고 현지 물자를 확보해 주십시오. 확인되지 않은 적 규모를 추측할 필요는 없습니다. 귀하의 생환과 관측 보고가 우선입니다.";
            }
        }
        public static string ReportLine(string map)
        {
            switch (Npc(map)) {
                case "DOKKAEBI": return "오, 제 발로 돌아왔네? 옷차림만 보고 얕봤더니 길은 볼 줄 아는구만. 네가 본 동선은 써먹을 데가 있겠다. 약속한 몫은 받아 가. 다음에도 유품 말고 네 얼굴을 보자고.";
                case "DONGDAEMUN_CHOI": return "직접 살피고 돌아왔구려. 그 정도면 내 이름을 걸고 소개해도 되겠소. 장비의 해진 곳도 살펴 두시오. 다음 길은 더 험할 테니.";
                case "WILDMAN": return "살아 돌아왔으면 가장 중요한 건 배운 거다. 오늘 본 길도 내일은 달라질 수 있어. 물부터 마시고, 다음에 나갈 길을 생각해라.";
                case "BROKER": return "직접 확인한 길에 물건까지. 이 정도면 값을 치를 만하지. 약속한 몫이야. 다음 거래에서도 네 발로 돌아와.";
                case "HWANG": return "생환 확인. 정찰 보고와 회수 기록은 접수했다. 이번 일은 제대로 처리했군. 보급비를 받아 가고 다음 출발 전에 장비부터 점검해라.";
                default: return "복귀 확인했습니다. 관측 보고는 보급 계획에 반영하겠습니다. 협조에 감사드립니다. 약속한 지원금을 수령하고 휴식하십시오.";
            }
        }
    }
}
