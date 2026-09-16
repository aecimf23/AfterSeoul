using System;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 월간 지원계약 (GDD §11).
    ///
    /// <para><b>철학이 먼저다.</b> 과금은 "불편함을 의도적으로 만든 뒤 제거하는 방식"이 아니라
    /// 운영 편의성 중심이다. 그래서 여기 있는 혜택은 전부 <b>이미 되는 일을 덜 귀찮게</b> 할 뿐,
    /// 안 되던 일을 되게 하지 않는다. 지역·NPC·성장은 건드리지 않는다.</para>
    ///
    /// <para><b>절대로 건드리지 않는 것: 본편 배송량.</b> GDD §11 의 금지 목록에
    /// "PC 본편 배송량 현금 판매"가 명시돼 있다. 돈으로 본편에 더 보낼 수 있으면 그건 편의가
    /// 아니라 본편 경제를 파는 것이다. 그래서 <c>Outbox</c> 는 이 클래스를 <b>참조하지 않고</b>,
    /// 그 사실을 <c>SupportTests</c> 가 검사한다 — 주석이 아니라 테스트로 묶어 둔다.</para>
    ///
    /// <para>결제 SDK 는 여기 없다. 이 클래스가 아는 것은 "계약이 살아 있는가" 하나뿐이고,
    /// 그걸 누가 어떻게 확인했는지는 플랫폼 쪽 일이다 (R1 과 같은 이유).</para>
    /// </summary>
    public static class Support
    {
        // ── 혜택 (전부 편의) ─────────────────────────────────────

        /// <summary>오프라인 생산 상한을 이만큼 늘린다. 12시간 → 24시간.</summary>
        public const int ExtraOfflineHours = 12;

        /// <summary>제작 큐 칸을 하나 더. 작업대 레벨을 대신하지는 않는다 — 얹을 뿐이다.</summary>
        public const int ExtraQueueSlots = 1;

        /// <summary>창고 칸.</summary>
        public const int ExtraWarehouseSlots = 40;

        public static bool IsActive(GameSave save, DateTimeOffset now) =>
            save != null && save.Support.ActiveUntil > now;

        /// <summary>남은 기간. 없으면 0.</summary>
        public static TimeSpan Remaining(GameSave save, DateTimeOffset now) =>
            IsActive(save, now) ? save.Support.ActiveUntil - now : TimeSpan.Zero;

        /// <summary>
        /// 계약을 반영한 오프라인 생산 상한.
        ///
        /// <para>상한을 없애지 않고 늘리기만 한다. 무제한이면 "일주일 뒤에 한 번 접속"이
        /// 최적이 되어 매일 접속할 이유가 사라지는데, 그건 돈 낸 사람에게도 나쁜 게임이다.</para>
        /// </summary>
        public static TimeSpan OfflineCap(GameSave save, IDataRegistry data, DateTimeOffset now)
        {
            var station = data != null && data.Balance != null ? data.Balance.Station : null;
            int hours = station != null && station.OfflineCapHours > 0 ? station.OfflineCapHours : 12;

            if (IsActive(save, now)) hours += ExtraOfflineHours;
            return TimeSpan.FromHours(hours);
        }

        public static int QueueCapacityBonus(GameSave save, DateTimeOffset now) =>
            IsActive(save, now) ? ExtraQueueSlots : 0;

        public static int WarehouseBonus(GameSave save, DateTimeOffset now) =>
            IsActive(save, now) ? ExtraWarehouseSlots : 0;

        /// <summary>
        /// 계약을 걸어 둔다. <b>결제 확인은 여기서 하지 않는다</b> —
        /// 플랫폼이 영수증을 검증한 뒤에 이 메서드를 부른다.
        /// </summary>
        public static void Grant(GameSave save, DateTimeOffset now, TimeSpan duration)
        {
            if (save == null || duration <= TimeSpan.Zero) return;

            // 남아 있으면 이어붙인다. 덮어쓰면 갱신할 때마다 남은 기간이 사라진다.
            var from = IsActive(save, now) ? save.Support.ActiveUntil : now;
            save.Support.ActiveUntil = from + duration;
        }

        /// <summary>
        /// 화면에 적을 혜택 목록. <b>여기 없는 것은 팔지 않는다.</b>
        /// 목록을 코드에 두는 이유는, 파는 것과 실제로 주는 것이 어긋나지 않게 하기 위해서다.
        /// </summary>
        public static string[] Benefits => new string[]
        {
            Loc.Text("오프라인 생산 시간 +12시간"),
            Loc.Text("제작 큐 +1칸"),
            Loc.Text("창고 +40칸"),
            Loc.Text("광고를 보지 않고 같은 보상 (하루 {0}회는 그대로)" , RewardedAd.MaxPerDay),
        };

        /// <summary>
        /// 지원계약으로도 <b>절대</b> 바뀌지 않는 것. 화면에 그대로 적는다 —
        /// 안 판다는 것을 분명히 하는 편이 나중에 의심받는 것보다 낫다.
        /// </summary>
        public static string[] NeverSold => new string[]
        {
            Loc.Text("본편 배송량 (돈으로 더 보낼 수 없습니다)"),
            Loc.Text("지역·고용주 접근"),
            Loc.Text("스캐브 부활"),
        };
    }

    /// <summary>
    /// 선택형 보상 광고 (GDD §11 — <b>강제 광고 금지</b>).
    ///
    /// <para>광고 SDK 는 여기 없다. 이 클래스가 정하는 것은 "무엇을 보상으로 줄 수 있는가"뿐이고,
    /// 그 목록에 본편 배송 관련이 없다는 것이 요점이다.</para>
    /// </summary>
    public static class RewardedAd
    {
        /// <summary>하루에 볼 수 있는 횟수. 무제한이면 광고가 곧 주 수입원이 된다.</summary>
        public const int MaxPerDay = 3;

        /// <summary>
        /// 광고 한 번의 보상으로 줄 수 있는 것.
        ///
        /// <para><b>본편 배송 한도는 여기 없고 앞으로도 없다</b> (GDD §11 금지 목록).
        /// 광고를 봐서 본편에 더 보낼 수 있으면, 그건 배송량을 파는 것과 같다.</para>
        /// </summary>
        public enum Reward
        {
            /// <summary>고용 시장을 한 번 더 굴린다.</summary>
            RerollHiringMarket,

            /// <summary>제작 큐의 대기 시간을 줄인다.</summary>
            SpeedUpCraft,

            /// <summary>부상자 회복을 앞당긴다.</summary>
            SpeedUpRecovery,
        }

        public static int RemainingToday(GameSave save, DateTimeOffset now)
        {
            string today = GameTime.GameDateOf(now).ToString();
            if (save.Support.AdGameDate != today) return MaxPerDay;

            int left = MaxPerDay - save.Support.AdsWatchedToday;
            return left < 0 ? 0 : left;
        }

        /// <summary>광고를 봤다고 기록한다. 실제 재생 여부는 플랫폼이 판단해서 부른다.</summary>
        public static bool TryConsume(GameSave save, DateTimeOffset now)
        {
            if (RemainingToday(save, now) <= 0) return false;

            string today = GameTime.GameDateOf(now).ToString();
            if (save.Support.AdGameDate != today)
            {
                save.Support.AdGameDate = today;
                save.Support.AdsWatchedToday = 0;
            }

            save.Support.AdsWatchedToday++;
            return true;
        }
    }
}
