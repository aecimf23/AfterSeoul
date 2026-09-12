using System;
using System.Collections.Generic;
using AfterSeoul.Core;

namespace AfterSeoul.Expedition
{
    /// <summary>
    /// 실종자 구조 — <c>"김철수의 무전 신호가 다시 포착되었습니다."</c> (GDD §15).
    ///
    /// <para><b>왜 있는가.</b> GDD 는 "오래 살아남은 스캐브의 죽음·실종은 사건이 되어야 한다"고
    /// 적어 두었다. 그런데 지금 실종은 명단에서 이름이 회색이 되는 것으로 끝난다 — 그건 사건이
    /// 아니라 삭제다. 다시 데려올 수 있어야 잃는 것이 무게를 갖는다.</para>
    ///
    /// <para><b>사망과 실종을 가르는 것도 이것이다.</b> 둘 다 돌아오지 않는 건 같지만, 실종은
    /// 아직 끝나지 않았다. 헬멧이 사망을 실종으로 강등시키는 것(GDD §7)이 의미를 가지려면
    /// 실종에 남은 길이 있어야 한다.</para>
    ///
    /// <para><b>기회는 한 번이고 시한이 있다.</b> 영영 열려 있으면 급할 게 없어서 사건이 되지 않고,
    /// 반복해서 잡히면 실종이 그냥 지연이 된다. 놓치면 그걸로 끝이다.</para>
    /// </summary>
    public sealed class RescueSystem : ITimelineSystem
    {
        public string Name => "Rescue";

        /// <summary>실종 후 신호가 잡히기까지. 바로 잡히면 사고의 무게가 없고, 너무 늦으면 잊는다.</summary>
        public static readonly TimeSpan SignalDelay = TimeSpan.FromHours(6);

        /// <summary>신호가 유지되는 시간. 이 안에 보내야 한다.</summary>
        public static readonly TimeSpan SignalWindow = TimeSpan.FromHours(18);

        public IEnumerable<TimedEvent> CollectEvents(ResolveWindow window, ResolveContext ctx)
        {
            foreach (var scav in ctx.Save.Scavs)
            {
                if (scav.Status != ScavStatus.Missing) continue;
                if (scav.SignalAt != default) continue;          // 이미 잡혔다
                if (string.IsNullOrEmpty(scav.LostAtMapId)) continue;
                if (scav.LostAt == default) continue;

                var at = scav.LostAt + SignalDelay;
                if (at > window.To) continue;

                var captured = scav;
                yield return new TimedEvent(
                    at < window.From ? window.From : at,
                    EventOrder.ExpeditionReturn,
                    "signal:" + captured.Uid,
                    c => PickUpSignal(c, captured));
            }

            // 시한이 끝나는 순간도 사건이다.
            //
            // <b>이게 없던 동안 창은 닫히지 않았다.</b> ExpireStaleSignals 는 만들어져 있었지만
            // 부르는 코드가 테스트 말고 한 군데도 없었다 — 화면은 RemainingWindow 로 걸러서
            // 겉보기엔 맞았지만, 세이브 안에서는 신호가 영원히 살아 있었다. 시한이 있다는 것이
            // 규칙이 되려면 시각이 지날 때 실제로 무언가 일어나야 한다.
            foreach (var scav in ctx.Save.Scavs)
            {
                if (scav.Status != ScavStatus.Missing) continue;
                if (scav.SignalAt == default) continue;

                // 이미 데리러 간 사람은 시한이 지나도 놓지 않는다.
                //
                // <b>시한은 "언제까지 출발할 수 있는가"이지 "언제까지 돌아와야 하는가"가 아니다.</b>
                // 구조 파견은 20분에서 3시간이 걸리므로, 돌아오는 시각을 기준으로 잡으면
                // 시한 끝자락에 제대로 보낸 구조대가 도착하기도 전에 대상이 죽는다.
                if (RescueUnderway(ctx.Save, scav.Uid)) continue;

                var closes = scav.SignalAt + SignalWindow;
                if (closes > window.To) continue;

                var captured = scav;
                yield return new TimedEvent(
                    closes < window.From ? window.From : closes,
                    // 복귀보다 <b>뒤</b>에 둔다. 정확히 마감 시각에 구조대가 돌아왔다면
                    // 그건 늦은 게 아니라 맞춰 온 것이다 — 애매하면 플레이어에게 유리한 쪽으로.
                    EventOrder.FactoryOutput,
                    "signal:lost:" + captured.Uid,
                    _ => LetGo(captured));
            }
        }

        private static void PickUpSignal(ResolveContext ctx, ScavState scav)
        {
            if (scav.Status != ScavStatus.Missing || scav.SignalAt != default) return;

            scav.SignalAt = ctx.EventTime;
            ctx.Report.RescueSignals.Add(scav.Uid);
        }

        // ── 조회 ─────────────────────────────────────────────────

        /// <summary>지금 구조하러 갈 수 있는 사람들. 신호가 잡혔고 아직 시한이 남은 실종자.</summary>
        public static List<ScavState> Rescuable(GameSave save, DateTimeOffset now)
        {
            var list = new List<ScavState>();
            foreach (var scav in save.Scavs)
                if (IsRescuable(scav, now)) list.Add(scav);
            return list;
        }

        public static bool IsRescuable(ScavState scav, DateTimeOffset now) =>
            scav != null &&
            scav.Status == ScavStatus.Missing &&
            scav.SignalAt != default &&
            now >= scav.SignalAt &&
            now < scav.SignalAt + SignalWindow;

        /// <summary>신호가 끊기기까지 남은 시간. 안 잡혔거나 이미 끊겼으면 0.</summary>
        public static TimeSpan RemainingWindow(ScavState scav, DateTimeOffset now)
        {
            if (!IsRescuable(scav, now)) return TimeSpan.Zero;
            return scav.SignalAt + SignalWindow - now;
        }

        /// <summary>
        /// 데려올 수 있는가. 구조는 전투도 탐색도 아니고 <b>버티는 일</b>이라 생존으로 본다.
        /// 요구치는 지역 위험도에 비례한다 — 깊은 데서 잃었으면 그만큼 데리러 가기 어렵다.
        /// </summary>
        public static int RequiredSurvival(MapDef map) => map == null ? 6 : 4 + map.RiskLevel * 3;

        public static bool WouldSucceed(GameSave save, IDataRegistry data, MapDef map,
            IReadOnlyList<string> teamUids)
        {
            int survival = 0;
            foreach (var uid in teamUids)
            {
                var s = FindScav(save, uid);
                if (s != null) survival += s.Survival;
            }
            survival += Scav.Equipment.TeamEffectsOf(save, data, teamUids).SurvivalBonus;

            return survival >= RequiredSurvival(map);
        }

        /// <summary>
        /// 데려왔다. <b>멀쩡하게 돌려주지 않는다</b> — 부상으로 돌아온다.
        /// 사고 없이 되돌릴 수 있으면 실종은 그냥 지연이 된다.
        /// </summary>
        public static void BringHome(ScavState scav)
        {
            if (scav == null) return;

            scav.Status = ScavStatus.Injured;
            scav.SignalAt = default;
            scav.LostAt = default;
            scav.LostAtMapId = null;
        }

        /// <summary>
        /// 신호가 끊겼다. 여기서부터는 돌아오지 않는다.
        ///
        /// <para><b>이 함수는 <c>SignalAt</c> 만 지우고 있었다.</b> 그런데
        /// <see cref="CollectEvents"/> 는 "<c>SignalAt</c> 이 비어 있으면 아직 안 잡힌 것"으로
        /// 읽는다 — 그래서 구조에 실패하면 다음 정산에서 <b>같은 신호가 다시 잡혔다.</b>
        /// "기회는 한 번이다"라고 적힌 주석 바로 옆에서, 기회가 무한히 다시 열리고 있었다.</para>
        ///
        /// <para>그래서 끝을 상태로 남긴다. <c>Missing</c> 에서 <c>Dead</c> 로 간다 —
        /// 실종이 끝나는 방식은 돌아오거나 아니거나 둘뿐이고, 이쪽이 아닌 쪽이다.
        /// 상태가 바뀌면 <see cref="CollectEvents"/> 가 애초에 이 사람을 보지 않는다.</para>
        /// </summary>
        public static void LetGo(ScavState scav)
        {
            // 실종 상태인 사람에게만 해당한다. 구조가 성공해 이미 돌아온 사람에게 이게 닿으면
            // 살아 돌아온 사람을 죽이게 된다 — 시한 사건과 구조 복귀가 같은 정산 안에서
            // 만날 수 있으므로 방어가 함수 안에 있어야 한다.
            if (scav == null || scav.Status != ScavStatus.Missing) return;

            scav.Status = ScavStatus.Dead;
            scav.SignalAt = default;
            scav.LostAt = default;
            scav.LostAtMapId = null;
        }

        /// <summary>지금 이 사람을 데리러 간 구조 파견이 나가 있는가.</summary>
        public static bool RescueUnderway(GameSave save, string scavUid)
        {
            if (save == null || string.IsNullOrEmpty(scavUid)) return false;

            foreach (var exp in save.Expeditions)
                if (!exp.Resolved && exp.RescueScavUid == scavUid) return true;

            return false;
        }

        /// <summary>시한이 지난 신호를 정리한다. 화면이 "이미 끝난 기회"를 계속 보여주지 않게.</summary>
        public static int ExpireStaleSignals(GameSave save, DateTimeOffset now)
        {
            int expired = 0;
            foreach (var scav in save.Scavs)
            {
                if (scav.Status != ScavStatus.Missing || scav.SignalAt == default) continue;
                if (RescueUnderway(save, scav.Uid)) continue;
                if (now < scav.SignalAt + SignalWindow) continue;

                LetGo(scav);
                expired++;
            }
            return expired;
        }

        private static ScavState FindScav(GameSave save, string uid)
        {
            foreach (var s in save.Scavs) if (s.Uid == uid) return s;
            return null;
        }
    }
}
