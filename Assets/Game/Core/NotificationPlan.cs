using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>예약할 알림 하나.</summary>
    public struct PlannedNotification
    {
        /// <summary>무엇 때문에 뜨는 알림인지. 같은 사건이면 항상 같은 값이라 중복 예약을 걸러낼 수 있다.</summary>
        public string Key;

        public string Title;
        public string Body;
        public DateTimeOffset At;
    }

    /// <summary>
    /// 무엇을 언제 알릴지 정한다 (GDD §31 — 복귀 알림).
    ///
    /// <para><b>여기에 Unity 가 없다.</b> 알림을 실제로 거는 일은 플랫폼마다 다르지만,
    /// "무엇을 언제 알릴 것인가"는 게임 규칙이라 Core 에 있어야 테스트할 수 있다.
    /// 안드로이드 API 뒤에 숨어 있으면 실기기를 켜야만 확인이 된다.</para>
    ///
    /// <para><b>예약은 앱이 백그라운드로 갈 때 전부 다시 짠다.</b> 취소하고 새로 거는 쪽이
    /// 옳은 이유는, 파견을 하나 더 보내거나 제작을 취소했을 때 예전 예약을 개별로 찾아
    /// 고치는 코드가 반드시 어긋나기 때문이다. 전부 지우고 지금 상태로 다시 그리면
    /// 어긋날 자리가 없다.</para>
    /// </summary>
    public static class NotificationPlan
    {
        /// <summary>한 번에 예약할 최대 개수. 안드로이드는 무제한이 아니고, 많아야 잔소리가 된다.</summary>
        public const int MaxScheduled = 8;

        /// <summary>
        /// 지금 상태로 걸어야 할 알림 목록. 시각 순으로 정렬되어 있다.
        ///
        /// <para><paramref name="now"/> 이전에 끝나는 일은 넣지 않는다 — 이미 끝난 일을
        /// 알릴 이유가 없고, 예약하면 즉시 울린다.</para>
        /// </summary>
        public static List<PlannedNotification> Build(GameSave save, IDataRegistry data, DateTimeOffset now)
        {
            var list = new List<PlannedNotification>();
            if (save == null) return list;

            foreach (var exp in save.Expeditions)
            {
                if (exp.Resolved || exp.ReturnsAt <= now) continue;

                list.Add(new PlannedNotification
                {
                    Key = "expedition:" + exp.Uid,
                    Title = Loc.Text("복귀"),
                    Body = BuildExpeditionBody(save, exp),
                    At = exp.ReturnsAt,
                });
            }

            foreach (var job in save.Factory.Queue)
            {
                if (job.Collected || job.CompletesAt <= now) continue;

                var recipe = data != null ? data.GetRecipe(job.RecipeId) : null;
                string itemId = recipe != null ? recipe.OutputItemId : null;

                list.Add(new PlannedNotification
                {
                    Key = "craft:" + job.RecipeId + ":" + job.CompletesAt.ToUnixTimeSeconds(),
                    Title = Loc.Text("생산 완료"),
                    Body = string.IsNullOrEmpty(itemId)
                        ? Loc.Text("공장 작업이 끝났습니다.")
                        : Loc.Text("{0} 생산이 끝났습니다." , Loc.ItemName(itemId)),
                    At = job.CompletesAt,
                });
            }

            // 실종자 무전. 시한이 있어서 놓치면 그 사람은 영영 안 돌아온다 —
            // 알림 몫을 하나 쓸 값어치가 있는 소식이다 (GDD §15).
            foreach (var scav in save.Scavs)
            {
                if (scav.Status != ScavStatus.Missing) continue;
                if (scav.SignalAt != default || scav.LostAt == default) continue;

                var at = scav.LostAt + Expedition.RescueSystem.SignalDelay;
                if (at <= now) continue;

                list.Add(new PlannedNotification
                {
                    Key = "signal:" + scav.Uid,
                    Title = Loc.Text("무전 포착"),
                    Body = Loc.Text("{0} 의 신호가 다시 잡혔습니다. 데리러 갈 수 있습니다." , Loc.Text(scav.Name)),
                    At = at,
                });
            }

            // 치료 완료. 파견·제작과 같은 부류다 — 시간이 걸리고, 끝나면 다시 쓸 수 있게 되는 것.
            // 여기 없으면 회복한 사람이 명단에서 조용히 대기 상태가 되고, 그걸 알아채려면
            // 앱을 열어 인원 화면까지 들어가야 한다.
            foreach (var scav in save.Scavs)
            {
                if (scav.Status != ScavStatus.Treating) continue;
                if (scav.RecoversAt <= now) continue;

                list.Add(new PlannedNotification
                {
                    Key = "recover:" + scav.Uid,
                    Title = Loc.Text("복귀 가능"),
                    Body = Loc.Text("{0} 의 치료가 끝났습니다. 다시 내보낼 수 있습니다." , Loc.Text(scav.Name)),
                    At = scav.RecoversAt,
                });
            }

            Sort(list);
            list = Merge(list);
            list = ShiftOutOfQuietHours(list);

            if (list.Count > MaxScheduled) list.RemoveRange(MaxScheduled, list.Count - MaxScheduled);
            return list;
        }

        /// <summary>가까운 것부터. 상한에 걸려 잘려나가는 건 먼 미래의 일이어야 한다.</summary>
        private static void Sort(List<PlannedNotification> list) => list.Sort((a, b) =>
        {
            int c = a.At.CompareTo(b.At);
            return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
        });

        /// <summary>이 안에 몰린 알림은 하나로 묶는다. 셋이 연달아 울리면 그건 알림이 아니라 소음이다.</summary>
        public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(10);

        /// <summary>
        /// 붙어 있는 알림을 합친다.
        ///
        /// <para>파견 셋을 같이 보내면 거의 같은 시각에 돌아온다. 그때 진동이 세 번 울리면
        /// 사람은 내용을 읽는 게 아니라 알림을 끈다. <b>묶은 것의 시각은 마지막 것에 맞춘다</b> —
        /// 먼저 울리면 아직 안 끝난 일을 끝났다고 말하게 된다.</para>
        /// </summary>
        private static List<PlannedNotification> Merge(List<PlannedNotification> sorted)
        {
            var merged = new List<PlannedNotification>();

            int i = 0;
            while (i < sorted.Count)
            {
                int j = i + 1;
                while (j < sorted.Count && sorted[j].At - sorted[i].At <= MergeWindow) j++;

                int count = j - i;
                if (count == 1) { merged.Add(sorted[i]); i = j; continue; }

                merged.Add(new PlannedNotification
                {
                    // 묶인 개수를 키에 넣는다 — 구성이 바뀌면 키도 바뀌어야 예약이 갱신된다.
                    Key = "batch:" + sorted[i].Key + "+" + (count - 1),
                    Title = Loc.Text("정리할 것이 있습니다"),
                    Body = Loc.Text("{0} 그 밖에 {1}건이 더 끝났습니다." , sorted[i].Body, count - 1),
                    At = sorted[j - 1].At,
                });
                i = j;
            }

            return merged;
        }

        /// <summary>새벽에는 울리지 않는다. 시각은 KST 고정 — 기기 타임존을 따르면 사람마다 다르게 운다.</summary>
        public const int QuietStartHourKst = 0;
        public const int QuietEndHourKst = 7;

        /// <summary>
        /// 자는 동안 울릴 알림을 아침으로 민다.
        ///
        /// <para>미루는 것이지 버리는 것이 아니다 — 새벽에 끝난 일도 일어나면 알아야 한다.
        /// 아침으로 민 것들이 한 시각에 뭉치므로 <b>민 뒤에 한 번 더 묶는다.</b></para>
        /// </summary>
        private static List<PlannedNotification> ShiftOutOfQuietHours(List<PlannedNotification> list)
        {
            bool shifted = false;

            for (int i = 0; i < list.Count; i++)
            {
                var kst = list[i].At.ToOffset(GameTime.GameZoneOffset);
                if (kst.Hour >= QuietEndHourKst || kst.Hour < QuietStartHourKst) continue;

                var wake = new DateTimeOffset(
                    kst.Year, kst.Month, kst.Day,
                    QuietEndHourKst, 0, 0, GameTime.GameZoneOffset);

                var item = list[i];
                item.At = wake.ToUniversalTime();
                list[i] = item;
                shifted = true;
            }

            if (!shifted) return list;

            Sort(list);
            return Merge(list);
        }

        /// <summary>
        /// 누가 어디서 돌아오는지 적는다.
        ///
        /// <para>"파견이 완료되었습니다"로는 알림을 열 이유가 없다. 이름이 있어야
        /// 누가 돌아왔는지 궁금해서 연다 (GDD §15).</para>
        /// </summary>
        private static string BuildExpeditionBody(GameSave save, ExpeditionState exp)
        {
            string map = Loc.MapName(exp.MapId);

            var names = new List<string>();
            foreach (var uid in exp.ScavUids)
            {
                foreach (var s in save.Scavs)
                {
                    if (s.Uid != uid) continue;
                    names.Add(Loc.Text(s.Name));
                    break;
                }
            }

            if (names.Count == 0) return Loc.Text("{0} 파견이 돌아왔습니다." , map);
            if (names.Count == 1) return Loc.Text("{0} 이(가) {1} 에서 돌아왔습니다." , names[0], map);
            return Loc.Text("{0} 외 {1}명이 {2} 에서 돌아왔습니다." , names[0], names.Count - 1, map);
        }
    }
}
