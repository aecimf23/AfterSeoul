using System;
using System.Collections.Generic;
using AfterSeoul.Exploration;

namespace AfterSeoul.Core
{
    [Serializable] public sealed class RegionalQuestProgress
    {
        public bool Accepted, ReadyToReport, Completed;
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
            var progress = Progress(save, run.MapId);
            if (progress?.Accepted == true && !progress.Completed && run.Result?.Outcome == ExplorationOutcome.Success
                && run.NodeIndex >= 2 && run.Loot.Count > 0) progress.ReadyToReport = true;
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
