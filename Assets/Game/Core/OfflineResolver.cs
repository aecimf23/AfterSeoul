using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 이 게임의 척추.
    ///
    /// <para>앱이 꺼져 있던 동안 일어난 일을 <b>시간 순서대로</b> 정산한다.
    /// 시스템별로 차례차례 돌리지 않고, 모든 시스템에서 사건을 모아 한 줄로 세운 뒤
    /// 앞에서부터 적용한다.</para>
    ///
    /// <para><b>왜 시스템 순서가 아니라 시간 순서인가:</b>
    /// 공장 완성이 10시, 파견 복귀가 11시, 날짜 경계가 자정이라면 실제로 일어난 순서는
    /// 그 순서다. 시스템별로 "공장 전부 → 파견 전부 → 의뢰 전부" 식으로 돌리면
    /// 어제 복귀한 물자가 오늘 의뢰에 잡히거나, 날짜가 두 번 넘어간 경우 중간 상태가
    /// 뭉개지는 식의 버그가 생긴다. 그 버그들은 전부 "며칠 만에 접속했을 때만" 나와서
    /// 재현이 거의 불가능하다. 정렬 한 번으로 그 부류 전체를 없앤다.</para>
    ///
    /// <para><b>멱등성:</b> 같은 세이브에 두 번 돌려도 결과가 같아야 한다.
    /// 모든 사건은 소비 표시(<c>Resolved</c>, <c>Collected</c>, <c>ActiveGameDate</c>)를
    /// 갖고, 수집 단계에서 이미 소비된 것은 내보내지 않는다.</para>
    /// </summary>
    public sealed class OfflineResolver
    {
        private readonly IClock _clock;
        private readonly IReadOnlyList<ITimelineSystem> _systems;

        /// <summary>
        /// 한 번에 정산할 수 있는 최대 구간. 이보다 오래 비웠으면 잘라낸다.
        /// 몇 달 만에 켰을 때 수천 개 사건을 만들어 프레임을 통째로 날리는 것을 막는다.
        /// </summary>
        public TimeSpan MaxCatchUp { get; set; } = TimeSpan.FromDays(30);

        /// <summary>
        /// 기준 시각(<c>SavedAt</c>)이 지금보다 앞서 있어도 되는 한계.
        ///
        /// <para>타임존 이동(최대 26시간 차)과 시각 보정을 덮을 만큼은 넉넉해야 하고,
        /// 기기 시각이 크게 틀어졌을 때 멈춰 있는 기간이 이 값을 넘지 않아야 한다.
        /// 하루 반으로 잡는다 — 둘 다 만족하는 가장 짧은 길이다.</para>
        /// </summary>
        public TimeSpan MaxFutureSkew { get; set; } = TimeSpan.FromHours(36);

        public OfflineResolver(IClock clock, IReadOnlyList<ITimelineSystem> systems)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
        }

        public ResolveReport Resolve(GameSave save, IDataRegistry data)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));

            var now = _clock.UtcNow;
            var report = new ResolveReport { From = save.SavedAt, To = now };
            var ctx = new ResolveContext(save, data, report);

            // ── 시계 되돌림 ─────────────────────────────────────────
            //
            // 처벌하지 않는다. 타임존 변경이나 기기 시각 보정 같은 정상적인 이유가 훨씬 많고,
            // 어차피 결과는 시드로 고정돼 있어서 시간을 앞당겨도 얻는 게 없다(§Rng 참조).
            // 되돌린 구간의 진행은 주지 않는다 — 뒤로 돌린 만큼 앞당겨 받는 일이 없어야 한다.
            //
            // <b>다만 기다림에는 끝이 있어야 한다.</b> <c>SavedAt</c> 은 정산이 <b>끝나야</b>
            // 갱신되므로, 예전에는 여기서 돌아가는 동안 그 값이 미래에 그대로 박혀 있었다.
            // 기기 시각이 과거로 <b>크게</b> 어긋나면 — 배터리가 빠져 2010년으로 돌아갔다든가,
            // 공장 초기화로 날짜가 리셋됐다든가 — 실제 시간이 그 미래를 따라잡을 때까지
            // 계속 여기서 되돌아왔다. 몇 년이다. 그동안 파견은 복귀하지 않고 제작은 끝나지 않고
            // 날짜도 넘어가지 않는다. 되돌림을 막으려다 <b>세이브를 잠가 버린</b> 셈이고,
            // 처벌하지 않는다는 말과도 어긋났다.
            //
            // 그래서 미래로 밀려날 수 있는 거리를 <see cref="MaxFutureSkew"/> 로 자른다.
            // 시간을 되돌려 창을 만들어 내는 짓은 여전히 안 된다 — 잘라낸 뒤에도 기준점은
            // <b>여전히 미래</b>라서, 시각을 되돌리는 것으로 얻어지는 정산 구간은 없다.
            // 바뀌는 것은 최악의 경우 멈춰 있는 기간이 "몇 년"에서 "하루"가 된다는 것뿐이다.
            if (now < save.SavedAt)
            {
                save.ClockAnomalyCount++;
                report.ClockWentBackwards = true;
                report.ClockAnomalies = save.ClockAnomalyCount;
                report.To = save.SavedAt;

                if (save.SavedAt - now > MaxFutureSkew) save.SavedAt = now + MaxFutureSkew;
                return report;
            }

            var from = save.SavedAt;
            if (now - from > MaxCatchUp)
            {
                from = now - MaxCatchUp;
                report.From = from;
            }

            var window = new ResolveWindow(from, now);

            // ── 1단계: 수집 (세이브를 건드리지 않는다) ─────────────────
            // seq 는 수집 순서. 시각과 Order 가 모두 같은 사건(예: 같은 순간에 복귀한
            // 파견 두 건)이 있을 때 정렬을 완전 순서로 만들어 준다. List.Sort 는 불안정
            // 정렬이라, 완전 순서를 주지 않으면 같은 세이브를 두 번 정산했을 때 결과가
            // 달라질 수 있다. 시스템 등록 순서와 각 시스템의 수집 순서가 결정론적이면
            // seq 도 결정론적이다.
            var events = new List<(TimedEvent Event, int Seq)>();
            int seq = 0;
            foreach (var system in _systems)
            {
                foreach (var e in system.CollectEvents(window, ctx))
                {
                    // 구간 밖 사건을 내보내는 시스템이 있으면 여기서 막는다.
                    if (e.At > window.To) continue;
                    events.Add((e, seq++));
                }
            }

            // ── 2단계: 시간 순 정렬 ────────────────────────────────
            events.Sort(CompareEvents);

            // ── 3단계: 순서대로 적용 ───────────────────────────────
            foreach (var (e, _) in events)
            {
                ctx.EventTime = e.At;
                e.Apply(ctx);
            }

            save.SavedAt = now;
            return report;
        }

        /// <summary>시각 → Order → 수집순서. 완전 순서이므로 정렬 안정성에 의존하지 않는다.</summary>
        private static int CompareEvents((TimedEvent Event, int Seq) a, (TimedEvent Event, int Seq) b)
        {
            int c = a.Event.At.CompareTo(b.Event.At);
            if (c != 0) return c;
            c = a.Event.Order.CompareTo(b.Event.Order);
            if (c != 0) return c;
            return a.Seq.CompareTo(b.Seq);
        }
    }
}
