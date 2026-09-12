using System;
using System.Collections.Generic;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 게임 날짜 하나. 시각 성분이 없는 날짜만 표현한다.
    ///
    /// <para><b>왜 <c>DateOnly</c> 를 쓰지 않는가:</b> <c>DateOnly</c> 는 .NET 6 에서 추가된
    /// 타입이라 Unity 의 .NET Standard 2.1 API 레벨에는 없다. Unity 에서 컴파일되지 않는다.
    /// <c>DateTime</c> 을 날짜 용도로 쓰면 시각 성분이 섞여 들어가는 사고가 나므로,
    /// 작은 전용 타입을 만든다.</para>
    /// </summary>
    [Serializable]
    public readonly struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        public readonly int Year;
        public readonly int Month;
        public readonly int Day;

        public GameDate(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public static GameDate FromDateTime(DateTime dt) => new GameDate(dt.Year, dt.Month, dt.Day);

        public DateTime ToDateTime() => new DateTime(Year, Month, Day, 0, 0, 0, DateTimeKind.Unspecified);

        /// <summary>
        /// 0001-01-01 을 1 로 하는 일련번호. 일일 의뢰 시드에 쓴다.
        /// 파이썬 참조 구현(<c>Tools/sim_model.py</c>)의 <c>date.toordinal()</c> 과 같은 값이어야 한다.
        /// </summary>
        public int DayNumber => (int)(ToDateTime().Ticks / TimeSpan.TicksPerDay) + 1;

        public GameDate AddDays(int days) => FromDateTime(ToDateTime().AddDays(days));

        /// <summary>세이브·로그 키. 항상 이 형식만 쓴다.</summary>
        public override string ToString() => $"{Year:0000}-{Month:00}-{Day:00}";

        public bool Equals(GameDate other) =>
            Year == other.Year && Month == other.Month && Day == other.Day;

        public override bool Equals(object obj) => obj is GameDate d && Equals(d);

        public override int GetHashCode() => (Year * 397 ^ Month) * 397 ^ Day;

        public int CompareTo(GameDate other) => ToDateTime().CompareTo(other.ToDateTime());

        public static bool operator ==(GameDate a, GameDate b) => a.Equals(b);
        public static bool operator !=(GameDate a, GameDate b) => !a.Equals(b);
    }

    /// <summary>
    /// 게임 날짜 경계 계산.
    ///
    /// 일일 의뢰는 UTC 자정이 아니라 **한국시간 새벽 5시**에 갱신된다.
    /// UTC 자정은 KST 오전 9시라서 출근길에 의뢰가 바뀌어 버리고,
    /// KST 자정은 밤에 플레이하던 사람의 의뢰를 눈앞에서 날려버린다.
    /// 새벽 5시는 거의 아무도 플레이하지 않는 시각이다.
    ///
    /// 기기 로컬 타임존을 쓰지 않는 이유: 해외 이용자나 여행 중인 이용자가
    /// 타임존을 넘나들면서 의뢰를 두 번 받을 수 있다. 게임 시간대는 하나로 고정한다.
    /// </summary>
    public static class GameTime
    {
        /// <summary>게임 표준시 = KST(UTC+9) 고정. 서머타임 없음.</summary>
        public static readonly TimeSpan GameZoneOffset = TimeSpan.FromHours(9);

        /// <summary>하루가 바뀌는 시각 (게임 표준시 기준).</summary>
        public const int DayBoundaryHour = 5;

        /// <summary>해당 시점이 속한 게임 날짜. 일일 의뢰 키로 쓴다.</summary>
        public static GameDate GameDateOf(DateTimeOffset utc)
        {
            var local = utc.ToOffset(GameZoneOffset);
            var shifted = local.AddHours(-DayBoundaryHour);
            return new GameDate(shifted.Year, shifted.Month, shifted.Day);
        }

        /// <summary>해당 게임 날짜가 시작되는 정확한 UTC 시각.</summary>
        public static DateTimeOffset StartOfGameDate(GameDate date)
        {
            var local = new DateTimeOffset(
                date.Year, date.Month, date.Day,
                DayBoundaryHour, 0, 0, GameZoneOffset);
            return local.ToUniversalTime();
        }

        /// <summary>
        /// <paramref name="from"/> 다음부터 <paramref name="to"/> 까지 사이에 지나간
        /// 날짜 경계들을 순서대로 돌려준다. 오프라인 정산에서 일일 갱신을
        /// "몇 번" 일으킬지 정하는 데 쓴다.
        /// </summary>
        public static IEnumerable<DateTimeOffset> DayBoundariesBetween(
            DateTimeOffset from, DateTimeOffset to)
        {
            if (to <= from) yield break;

            var cursor = StartOfGameDate(GameDateOf(from).AddDays(1));
            while (cursor <= to)
            {
                yield return cursor;
                cursor = StartOfGameDate(GameDateOf(cursor).AddDays(1));
            }
        }
    }
}
