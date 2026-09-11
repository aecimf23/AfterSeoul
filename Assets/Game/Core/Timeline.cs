using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 오프라인 동안 일어난 사건 하나.
    ///
    /// 각 시스템은 "내 쪽에서 이 구간에 무슨 일이 일어났는가"를 이 목록으로 답하고,
    /// 실제 적용 순서는 <see cref="OfflineResolver"/> 가 결정한다.
    /// 시스템끼리 서로를 호출하지 않는다.
    /// </summary>
    public readonly struct TimedEvent
    {
        /// <summary>사건이 일어난 UTC 시각.</summary>
        public readonly DateTimeOffset At;

        /// <summary>같은 시각에 겹쳤을 때의 순서. 작을수록 먼저. <see cref="EventOrder"/> 참조.</summary>
        public readonly int Order;

        /// <summary>리포트/로그용 분류.</summary>
        public readonly string Kind;

        /// <summary>실제로 세이브를 변경하는 동작.</summary>
        public readonly Action<ResolveContext> Apply;

        public TimedEvent(DateTimeOffset at, int order, string kind, Action<ResolveContext> apply)
        {
            At = at;
            Order = order;
            Kind = kind;
            Apply = apply;
        }
    }

    /// <summary>
    /// 같은 시각에 겹친 사건의 처리 순서.
    ///
    /// 파견 복귀가 날짜 경계보다 먼저인 이유: 파견이 정확히 경계 시각에 복귀했다면
    /// 그 물자는 어제 의뢰가 만료되기 **전에** 창고에 들어와야 한다. 애매할 때는
    /// 플레이어에게 유리한 쪽으로 정한다.
    /// </summary>
    public static class EventOrder
    {
        public const int ExpeditionReturn = 0;
        public const int FactoryOutput = 1;
        public const int DayRollover = 2;
    }

    /// <summary>정산 구간. from 이후 ~ to 까지(둘 다 UTC).</summary>
    public readonly struct ResolveWindow
    {
        public readonly DateTimeOffset From;
        public readonly DateTimeOffset To;

        public ResolveWindow(DateTimeOffset from, DateTimeOffset to)
        {
            From = from;
            To = to;
        }

        public bool Contains(DateTimeOffset t) => t > From && t <= To;
        public TimeSpan Duration => To - From;
    }

    /// <summary>
    /// 오프라인 정산에 참여하는 시스템.
    ///
    /// 새 시스템이 늘어나도 <see cref="OfflineResolver"/> 는 손대지 않는다.
    /// 등록만 하면 시간 순서에 알아서 끼어든다.
    /// </summary>
    public interface ITimelineSystem
    {
        string Name { get; }

        /// <summary>
        /// 이 구간에 일어난 사건을 돌려준다.
        /// <b>여기서 세이브를 변경하면 안 된다.</b> 변경은 반드시 <see cref="TimedEvent.Apply"/> 안에서만.
        /// 수집과 적용을 분리해야 시간 순 정렬이 의미를 가진다.
        /// </summary>
        IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx);
    }
}
