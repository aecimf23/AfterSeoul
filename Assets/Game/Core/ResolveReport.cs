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

        /// <summary>기기 시계가 뒤로 가 있었다. 진행 없음.</summary>
        public bool ClockWentBackwards;

        public readonly List<ExpeditionResult> Expeditions = new List<ExpeditionResult>();
        public readonly List<CraftResult> Crafts = new List<CraftResult>();
        public readonly List<ItemStack> ItemsGained = new List<ItemStack>();
        public readonly List<string> ScavCasualties = new List<string>();

        /// <summary>일일 의뢰가 갱신된 횟수. 2 이상이면 며칠 만에 들어온 것.</summary>
        public int DayRollovers;

        /// <summary>창고가 가득 차서 버려진 것. 있으면 반드시 UI 에 알린다.</summary>
        public readonly List<ItemStack> Overflowed = new List<ItemStack>();

        public bool IsEmpty =>
            Expeditions.Count == 0 && Crafts.Count == 0 &&
            DayRollovers == 0 && Overflowed.Count == 0;

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
    }

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
