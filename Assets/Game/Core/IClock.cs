using System;

namespace AfterSeoul.Core
{
    /// <summary>
    /// 이 게임에서 시간을 알 수 있는 유일한 통로.
    ///
    /// 규칙: <c>DateTime.UtcNow</c> / <c>DateTimeOffset.UtcNow</c> 를 직접 부르는 코드는
    /// 이 파일 바깥에 존재하면 안 된다. 비동기 게임이라 "앱을 닫은 사이에 무슨 일이
    /// 일어났는가"가 곧 게임 내용이고, 그걸 테스트하려면 시간을 마음대로 돌릴 수 있어야 한다.
    /// </summary>
    public interface IClock
    {
        DateTimeOffset UtcNow { get; }
    }

    /// <summary>실제 기기 시계. 프로덕션에서만 쓴다.</summary>
    public sealed class SystemClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    /// <summary>테스트용. 시간을 임의로 설정하고 밀어서 오프라인 시나리오를 재현한다.</summary>
    public sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; }

        public TestClock(DateTimeOffset start) => UtcNow = start;

        public void Advance(TimeSpan delta) => UtcNow += delta;

        /// <summary>시계 되돌림 치팅 시나리오를 만들기 위한 것. 프로덕션 경로에는 없다.</summary>
        public void Rewind(TimeSpan delta) => UtcNow -= delta;
    }
}
