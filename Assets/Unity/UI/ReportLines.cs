using System.Collections.Generic;
using AfterSeoul.Core;
using UnityEngine;

namespace AfterSeoul.Unity.UI
{
    /// <summary>
    /// <see cref="ResolveReport"/> 를 사람이 읽는 줄로 바꾼다.
    ///
    /// <para><b>왜 따로 뺐나:</b> 같은 보고를 두 곳이 그린다 — 돌아온 순간의 연출
    /// (<see cref="ReturnCutscene"/>) 과 홈의 "복귀 보고" 카드. 각자 문장을 만들면
    /// 반드시 갈라진다. 연출에만 새 줄을 하나 넣고 카드에는 빠뜨리는 식으로.
    /// 문장은 한 벌만 있고, 두 화면은 그걸 <b>어떻게 보여줄지만</b> 다르게 한다.</para>
    ///
    /// <para>여기는 세이브를 읽기만 한다. 보고서에 없는 걸 세이브에서 역산하지 않는다
    /// (ARCHITECTURE §9) — 이름을 uid 로 찾는 것만 예외다. 보고서에 이름까지 담으면
    /// 도메인이 표시 문자열을 들고 다니게 된다.</para>
    /// </summary>
    public static class ReportLines
    {
        public struct Line
        {
            public string Text;
            public Color Color;

            /// <summary>0 = 소식의 제목, 1 = 그 아래 딸린 내용.</summary>
            public int Indent;

            /// <summary>연출에서 소리를 낼 만한 줄인가. 전부 울리면 소음이 된다.</summary>
            public bool Notable;
        }

        public static List<Line> Build(ResolveReport report, GameSave save, IDataRegistry data)
        {
            var lines = new List<Line>();
            if (report == null || report.IsEmpty) return lines;

            if (report.ClockWentBackwards)
            {
                // 한 번이면 사정이 있었던 것이고, 반복되면 기기 설정 문제다.
                // 같은 문장을 계속 띄우면 "또 저러네"가 되고 정작 고칠 곳을 알려주지 못한다.
                Add(lines, report.ClockAnomalies >= 3
                        ? AfterSeoul.Core.Loc.Text("기기 시각이 자꾸 과거로 돌아갑니다. 설정에서 날짜·시간 자동 설정을 켜 주세요 — ") +
                          AfterSeoul.Core.Loc.Text("그동안은 이번처럼 진행이 한 번씩 건너뜁니다.")
                        : AfterSeoul.Core.Loc.Text("기기 시각이 과거로 설정되어 이번 진행은 건너뛰었습니다."),
                    Theme.Warn, 0, true);
                return lines;
            }

            var away = report.OfflineDuration;
            if (away.TotalMinutes >= 1)
                Add(lines, AwayText(away), Theme.TextFaint);

            foreach (var exp in report.Expeditions) AddExpedition(lines, exp, save);

            // 무전은 시한이 있다. 다른 소식에 섞여 스쳐 지나가면 그 사람은 영영 안 돌아온다 (GDD §15).
            foreach (var uid in report.RescueSignals)
                Add(lines,
                    AfterSeoul.Core.Loc.Text("{0} 의 무전 신호가 다시 포착되었습니다 — 탐색에서 구조대를 보낼 수 있습니다", ScavName(save, uid)),
                    Theme.Warn, 0, true);

            foreach (var craft in report.Crafts)
                Add(lines, craft.Count > 0
                        ? AfterSeoul.Core.Loc.Text("제작 완료: {0} ×{1} ({2})", Loc.ItemName(craft.OutputItemId), craft.Count, Theme.QualityLabel(craft.Quality))
                        : AfterSeoul.Core.Loc.Text("제작 실패: {0}", Loc.ItemName(craft.OutputItemId)),
                    Theme.QualityColor(craft.Quality), 0, true);

            if (report.LevelsGained > 0)
                Add(lines, AfterSeoul.Core.Loc.Text("레벨 {0} 달성", report.NewLevel) + UnlockedAt(report.NewLevel, data),
                    Theme.Accent, 0, true);

            if (report.DayRollovers > 0)
                Add(lines, report.DayRollovers == 1
                    ? AfterSeoul.Core.Loc.Text("새 의뢰가 도착했습니다")
                    : AfterSeoul.Core.Loc.Text("{0}일치 의뢰가 지나갔습니다", report.DayRollovers), Theme.Accent, 0, true);

            foreach (var over in report.Overflowed)
                Add(lines, AfterSeoul.Core.Loc.Text("창고가 가득 차 버려짐: {0} ×{1}", Loc.ItemName(over.ItemId), over.Count),
                    Theme.Danger, 0, true);

            return lines;
        }

        private static void AddExpedition(List<Line> lines, ExpeditionResult exp, GameSave save)
        {
            if (exp.IsOrientation)
            {
                Add(lines, AfterSeoul.Core.Loc.Text("[초도 보급] 전원 무사 복귀"), Theme.Safe, 0, true);
                Add(lines, AfterSeoul.Core.Loc.Text("보급 꾸러미 ×1 회수 · 기지에서 납품하면 50,000원"), Theme.Text, 1);
                Add(lines, AfterSeoul.Core.Loc.Text("임무 물자 별도 보관 · 파견비 없음 · 장비 보존"), Theme.TextFaint, 1);
                return;
            }
            Add(lines, AfterSeoul.Core.Loc.Text("[{0}] 탐색 완료", Loc.MapName(exp.MapId)), Theme.Info, 0, true);

            // 무슨 일이 있었는지가 먼저다. 목록만 있으면 기다린 보람이 숫자가 된다.
            // 키 조립은 ExpeditionEvents 에 맡긴다 — 여기서 다시 만들면 _PASS/_FAIL 규칙이
            // 두 군데가 되고, 한쪽만 고치는 날 조용히 어긋난다.
            string key = AfterSeoul.Expedition.ExpeditionEvents.LocaleKey(exp.EventId, exp.EventPassed);
            if (key != null && Loc.Has(key))
                Add(lines, Loc.Get(key), exp.EventPassed ? Theme.Safe : Theme.Warn, 1, true);

            foreach (var loot in exp.Loot)
                Add(lines, $"{Loc.ItemName(loot.ItemId)} ×{loot.Count}", Theme.Text, 1);
            if (exp.Loot.Count == 0)
                Add(lines, AfterSeoul.Core.Loc.Text("회수품 없음"), Theme.TextFaint, 1);

            // 남는 장사였나.
            //
            // 파견비는 출발할 때 이미 빠져나갔고 회수품은 창고에 쌓이기만 해서, 둘을 나란히
            // 두지 않으면 손익이 보이지 않는다. 넷을 보내면 비용은 네 배인데 회수량은 그만큼
            // 늘지 않는데도 화면에는 "많이 주워왔다"만 보였다. 판매가 기준이라 실제로 손에
            // 쥘 돈과는 다르므로 그 말을 같이 적는다.
            if (exp.CostPaid > 0 || exp.LootValue > 0)
            {
                long net = exp.Net;
                Add(lines,
                    AfterSeoul.Core.Loc.Text("파견비 {0} / 회수품 {1} (판매가) → ", Theme.Won(exp.CostPaid), Theme.Won(exp.LootValue)) +
                    (net >= 0 ? "+" : "−") + Theme.Won(net < 0 ? -net : net),
                    net >= 0 ? Theme.TextFaint : Theme.Warn, 1, net < 0);
            }

            if (exp.HadAccident)
                Add(lines, AfterSeoul.Core.Loc.Text("사고 발생"), Theme.Danger, 1, true);

            // 사람과 장비를 따로 적는다. 장비를 빼면 다음에 창고를 열었을 때
            // 방탄복이 왜 없는지 알 길이 없다.
            foreach (var uid in exp.LostScavUids)
                Add(lines, AfterSeoul.Core.Loc.Text("{0} 돌아오지 못함", ScavName(save, uid)), Theme.Danger, 1, true);
            foreach (var uid in exp.InjuredScavUids)
                Add(lines, AfterSeoul.Core.Loc.Text("{0} 부상", ScavName(save, uid)), Theme.Warn, 1, true);
            foreach (var itemId in exp.LostGear)
                Add(lines, AfterSeoul.Core.Loc.Text("장비 손실: {0}", Loc.ItemName(itemId)), Theme.Danger, 1);

            if (!string.IsNullOrEmpty(exp.RescueScavUid))
                Add(lines, exp.RescueSucceeded
                        ? AfterSeoul.Core.Loc.Text("{0} 를 데리고 나왔습니다. 상태는 좋지 않습니다.", ScavName(save, exp.RescueScavUid))
                        : AfterSeoul.Core.Loc.Text("{0} 를 찾지 못했습니다. 신호가 끊겼습니다.", ScavName(save, exp.RescueScavUid)),
                    exp.RescueSucceeded ? Theme.Safe : Theme.Danger, 1, true);
        }

        private static string AwayText(System.TimeSpan away)
        {
            if (away.TotalDays >= 1) return AfterSeoul.Core.Loc.Text("{0}일 {1}시간 만에 접속", (int)away.TotalDays, away.Hours);
            if (away.TotalHours >= 1) return AfterSeoul.Core.Loc.Text("{0}시간 {1}분 만에 접속", (int)away.TotalHours, away.Minutes);
            return AfterSeoul.Core.Loc.Text("{0}분 만에 접속", away.Minutes);
        }

        /// <summary>
        /// 이 레벨에서 새로 열린 것. 레벨업 줄에 붙인다.
        ///
        /// <para>숫자만 올라가면 무엇이 달라졌는지 알 수가 없다. 레벨이 실제로 막고 있는 건
        /// 지역·의뢰 티어·고용 티어 셋이므로, 그 경계에서만 말한다.</para>
        ///
        /// <para><b>지역은 데이터에서 뽑는다.</b> 원래 이 함수는 레벨 숫자를 손으로 적은
        /// switch 였고, 그래서 <c>expeditions.json</c> 이 강남을 3, 남산을 7 로 열어두는 동안
        /// 그 두 레벨에서는 "레벨 달성" 한 줄만 뜨고 지역이 열렸다는 말이 없었다.
        /// 이 프로젝트에서 세 번 이상 나온 그 부류다 — 값을 읽는 쪽과 정하는 쪽이 갈라진 것.
        /// 의뢰·고용 티어는 아직 코드 상수라 그대로 둔다.</para>
        /// </summary>
        public static string UnlockedAt(int level, IDataRegistry data)
        {
            var opened = new List<string>();

            if (data != null)
            {
                foreach (var map in data.AllMaps)
                {
                    if (map?.Unlock == null) continue;
                    if (map.Unlock.Type != "playerLevel" || map.Unlock.Value != level) continue;
                    opened.Add(Loc.MapName(map.Id));
                }
            }

            switch (level)
            {
                case 5: opened.Add(AfterSeoul.Core.Loc.Text("상위 의뢰·고용")); break;
                case 10: opened.Add(AfterSeoul.Core.Loc.Text("최상위 의뢰·고용")); break;
            }

            return opened.Count == 0 ? "" : " — " + string.Join(", ", opened.ToArray()) + AfterSeoul.Core.Loc.Text(" 개방");
        }

        /// <summary>
        /// uid 로 이름을 찾는다. 실종·사망한 사람도 세이브에는 남아 있어서 이름이 나온다 —
        /// 이름 없이 "스캐브 1명 상실"이라고만 적으면 그게 누구였는지 알 수가 없고,
        /// 그러면 상실이 사건이 되지 못한다 (GDD §15).
        /// </summary>
        public static string ScavName(GameSave save, string uid)
        {
            if (save == null) return AfterSeoul.Core.Loc.Text("스캐브");
            foreach (var s in save.Scavs)
                if (s.Uid == uid) return AfterSeoul.Core.Loc.Text(s.Name);
            return AfterSeoul.Core.Loc.Text("스캐브");
        }

        private static void Add(List<Line> lines, string text, Color color, int indent = 0, bool notable = false)
            => lines.Add(new Line { Text = text, Color = color, Indent = indent, Notable = notable });
    }
}
