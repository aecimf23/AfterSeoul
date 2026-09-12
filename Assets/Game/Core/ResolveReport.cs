using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 정산 중 시스템들이 공유하는 작업 컨텍스트.
    /// 세이브·데이터·리포트에 닿는 유일한 통로다.
    /// </summary>
    public sealed class ResolveContext
    {
        public readonly GameSave Save;
        public readonly IDataRegistry Data;
        public readonly ResolveReport Report;

        /// <summary>현재 적용 중인 사건의 시각. 사건 안에서 "지금"은 실제 현재가 아니다.</summary>
        public DateTimeOffset EventTime { get; internal set; }

        public ResolveContext(GameSave save, IDataRegistry data, ResolveReport report)
        {
            Save = save;
            Data = data;
            Report = report;
        }
    }

    /// <summary>
    /// 정산 결과. 홈 화면의 "복귀 보고서"가 이걸 그대로 그린다.
    ///
    /// UI 가 세이브를 뒤져서 "뭐가 바뀌었지?"를 역산하지 않게 하려고 존재한다.
    /// 역산하기 시작하면 UI 가 도메인 규칙을 중복 구현하게 된다.
    /// </summary>
    public sealed class ResolveReport
    {
        public DateTimeOffset From;
        public DateTimeOffset To;
        public TimeSpan OfflineDuration => To - From;

        /// <summary>기기 시계가 뒤로 가 있었다. 이번 구간은 진행 없음.</summary>
        public bool ClockWentBackwards;

        /// <summary>
        /// 지금까지 시계가 뒤로 간 횟수 (<c>GameSave.ClockAnomalyCount</c>).
        ///
        /// <para>한 번이면 타임존을 넘었거나 시각을 한 번 고친 것이다 — 안내 한 줄이면 된다.
        /// 계속 쌓이면 기기의 자동 시각 설정이 꺼져 있다는 뜻이고, 그때는 "무슨 일이 있었다"가
        /// 아니라 "무엇을 고쳐야 한다"를 말해야 한다. 그 판단에 쓰는 값이다.</para>
        /// </summary>
        public int ClockAnomalies;

        public readonly List<ExpeditionResult> Expeditions = new List<ExpeditionResult>();
        public readonly List<CraftResult> Crafts = new List<CraftResult>();
        public readonly List<ItemStack> ItemsGained = new List<ItemStack>();
        public readonly List<string> ScavCasualties = new List<string>();

        /// <summary>
        /// 무전이 다시 잡힌 실종자 (GDD §15). 화면이 이걸 보고 구조 임무를 띄운다.
        ///
        /// <para>시한이 있으므로 복귀 보고에서 놓치면 기회가 사라진다 — 그래서 다른 소식과
        /// 섞이지 않게 따로 담는다.</para>
        /// </summary>
        public readonly List<string> RescueSignals = new List<string>();

        /// <summary>일일 의뢰가 갱신된 횟수. 2 이상이면 며칠 만에 들어온 것.</summary>
        public int DayRollovers;

        /// <summary>창고가 가득 차서 버려진 것. 있으면 반드시 UI 에 알린다.</summary>
        public readonly List<ItemStack> Overflowed = new List<ItemStack>();

        /// <summary>이번 정산에서 오른 레벨 수. 0 이면 안 올랐다.</summary>
        public int LevelsGained;

        /// <summary>오른 뒤의 레벨. <see cref="LevelsGained"/> 이 0 이면 의미 없다.</summary>
        public int NewLevel;

        /// <summary>
        /// 알릴 것이 없는가. 화면은 이게 참이면 보고 자체를 띄우지 않는다.
        ///
        /// <para><b>시계 경고는 "아무 일도 없음"이 아니다.</b> 되돌림을 만나면 정산이 곧바로
        /// 돌아오므로 나머지 칸은 전부 비어 있고, 그래서 예전에는 이 값이 참이 되어
        /// <c>GameSession</c> 이 <c>Resolved</c> 를 쏘지 않았고 홈 화면도 카드를 감췄다.
        /// 결과적으로 <c>ReportLines</c> 에 적어 둔 "기기 시각이 과거로…" 문장은 <b>한 번도
        /// 화면에 닿을 수 없었다.</b> 게임이 멈춘 바로 그 상황에서 멈췄다는 말만 사라진 셈이다.</para>
        /// </summary>
        public bool IsEmpty =>
            !ClockWentBackwards &&      // 멈췄다는 사실 자체가 알릴 일이다
            Expeditions.Count == 0 && Crafts.Count == 0 &&
            DayRollovers == 0 && Overflowed.Count == 0 && LevelsGained == 0 &&
            RescueSignals.Count == 0;   // 무전은 시한이 있다. 조용히 지나가면 기회가 사라진다

        public void AddGain(string itemId, int count)
        {
            for (int i = 0; i < ItemsGained.Count; i++)
            {
                if (ItemsGained[i].ItemId == itemId)
                {
                    var s = ItemsGained[i];
                    s.Count += count;
                    ItemsGained[i] = s;
                    return;
                }
            }
            ItemsGained.Add(new ItemStack(itemId, count));
        }
    }

    public sealed class ExpeditionResult
    {
        public string ExpeditionUid;
        public string MapId;
        public DateTimeOffset ReturnedAt;
        public List<ItemStack> Loot = new List<ItemStack>();
        public bool HadCombat;
        public bool HadAccident;
        public List<string> InjuredScavUids = new List<string>();
        public List<string> LostScavUids = new List<string>();

        /// <summary>
        /// 돌아오지 못한 사람과 함께 사라진 장비의 아이템 id.
        /// 복귀 보고가 이걸 읽는다 — 사람만 적고 장비를 빼면, 다음에 창고를 열었을 때
        /// 방탄복이 왜 없는지 알 길이 없다.
        /// </summary>
        public List<string> LostGear = new List<string>();

        /// <summary>
        /// 이번 파견에서 일어난 일 (<c>expedition_events.json</c>). 아무 일도 없었으면 비어 있다.
        ///
        /// <para>보고서가 전리품 목록 하나뿐이면 기다린 보람이 숫자가 된다.
        /// 사건 한 줄이 "무슨 일이 있었나"를 만든다.</para>
        /// </summary>
        public string EventId;

        /// <summary>보낸 사람들이 그 일을 감당했는가. 같은 사건도 팀에 따라 다른 이야기가 된다.</summary>
        public bool EventPassed;

        /// <summary>
        /// 출발할 때 낸 파견비. <c>ExpeditionState.CostPaid</c> 를 그대로 옮긴 값이다.
        ///
        /// <para><b>왜 보고서에 넣나:</b> 이 값은 출발 시점에 이미 세이브에 적히고 있었는데
        /// <b>읽는 곳이 한 군데도 없었다</b> — 이 프로젝트에서 반복된 그 부류다.
        /// 그래서 플레이어는 "이번 파견이 남는 장사였나"를 끝내 알 수 없었다.
        /// 4인 팀이 1인보다 손해라는 것도 화면에서는 보이지 않았다.</para>
        /// </summary>
        public long CostPaid;

        /// <summary>
        /// 창고에 실제로 들어간 회수품의 판매가 합. <b>넘쳐서 버려진 것은 빠진다</b> —
        /// 못 받은 물건을 수익에 세면 순익이 거짓이 된다.
        /// </summary>
        public long LootValue;

        /// <summary>남은 것. 음수면 파견비도 못 건졌다는 뜻이고, 그건 알려줘야 한다.</summary>
        public long Net => LootValue - CostPaid;

        /// <summary>구조하러 간 실종자. 평범한 탐색이면 비어 있다 (GDD §15).</summary>
        public string RescueScavUid;

        /// <summary>데려왔는가. 실패하면 신호가 끊기고 그걸로 끝이다.</summary>
        public bool RescueSucceeded;
    }

    // (레벨업은 파견 하나에 딸린 것이 아니라 정산 전체의 결과라 ResolveReport 쪽에 있다)

    public sealed class CraftResult
    {
        public string RecipeId;
        public string OutputItemId;
        public int Count;
        public CraftQuality Quality;
        public DateTimeOffset CompletedAt;
    }

    public enum CraftQuality { Failed = 0, Normal = 1, Good = 2, Excellent = 3 }
}
