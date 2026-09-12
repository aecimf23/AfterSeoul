using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Scav
{
    /// <summary>
    /// 치료 완료를 시간 순서에 끼워 넣는다.
    ///
    /// <para><b>왜 타임라인인가:</b> 자는 동안에도 나아야 한다. 접속 중에만 회복하면
    /// "밤새 놔뒀는데 아침에도 부상"이 되고, 그건 방치형에서 있을 수 없는 일이다.
    /// <see cref="ITimelineSystem"/> 에 얹으면 오프라인 정산이 알아서 같은 순서로 적용한다
    /// (ARCHITECTURE §4).</para>
    ///
    /// <para><b>순서는 파견 복귀와 같은 단계에 둔다.</b> 치료가 끝난 그 시각에 파견을 보낼 수
    /// 있어야 하고, 애매할 때는 플레이어에게 유리한 쪽으로 정한다 — 날짜 경계보다 먼저 회복해야
    /// 그날의 의뢰에 이 사람을 쓸 수 있다.</para>
    /// </summary>
    public sealed class TreatmentSystem : ITimelineSystem
    {
        public string Name => "Treatment";

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            foreach (var scav in ctx.Save.Scavs)
            {
                if (scav.Status != ScavStatus.Treating) continue;
                if (scav.RecoversAt == default) continue;
                if (scav.RecoversAt > window.To) continue;

                var captured = scav;

                // 구간보다 앞선 시각이면 구간 시작으로 당긴다. 정산은 "이 구간 안의 일"만
                // 처리하므로, 예전에 끝났어야 할 치료를 버리면 영영 안 낫는다.
                var at = captured.RecoversAt < window.From ? window.From : captured.RecoversAt;

                yield return new TimedEvent(
                    at, EventOrder.ExpeditionReturn, "recover:" + captured.Uid,
                    c => Treatment.Recover(captured, c.EventTime));
            }
        }
    }
}
