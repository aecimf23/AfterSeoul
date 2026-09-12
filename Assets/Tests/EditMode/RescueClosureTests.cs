using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Expedition;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 실종 구조의 <b>끝</b> (GDD §15 — "기회는 한 번이고 시한이 있다").
    ///
    /// <para><b>그 문장이 주석으로만 있었다.</b> <c>LetGo</c> 는 <c>SignalAt</c> 만 지웠는데,
    /// 신호를 거는 쪽은 "<c>SignalAt</c> 이 비어 있으면 아직 안 잡힌 것"으로 읽는다 —
    /// 그래서 <b>구조에 실패하면 다음 정산에서 같은 신호가 다시 잡혔다.</b>
    /// "기회는 한 번이다"라고 적힌 줄 바로 옆에서 기회가 무한히 다시 열리고 있었다.</para>
    ///
    /// <para>시한 쪽도 같았다. <c>ExpireStaleSignals</c> 는 만들어져 있었지만 부르는 코드가
    /// 테스트 말고 한 군데도 없었다 — 화면이 걸러줘서 겉보기엔 맞았을 뿐, 세이브 안에서
    /// 신호는 영원히 살아 있었다.</para>
    /// </summary>
    [TestFixture]
    public class RescueClosureTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private JsonDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        private static GameSave NewSave()
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = 10_000_000, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            save.Warehouse.Capacity = 500;
            return save;
        }

        /// <summary>무전이 잡힌 상태의 실종자를 만든다.</summary>
        private static ScavState Signalled(GameSave save, DateTimeOffset signalAt)
        {
            var scav = new ScavState
            {
                Uid = "lost1",
                Name = "김철수",
                Tier = 1,
                Search = 4, Combat = 4, Survival = 4,
                Status = ScavStatus.Missing,
                LostAt = signalAt - RescueSystem.SignalDelay,
                LostAtMapId = "MYEONGDONG",
                SignalAt = signalAt,
                HiredAt = T0,
            };
            save.Scavs.Add(scav);
            return scav;
        }

        private static OfflineResolver Resolver(TestClock clock) =>
            new OfflineResolver(clock, new List<ITimelineSystem> { new RescueSystem() });

        // ── 기회는 한 번 ────────────────────────────────────────

        [Test]
        public void FailedRescue_IsFinal()
        {
            var save = NewSave();
            var scav = Signalled(save, T0);

            RescueSystem.LetGo(scav);

            Assert.AreNotEqual(ScavStatus.Missing, scav.Status,
                "실종 상태로 남으면 다음 정산에서 신호가 다시 잡힌다");

            // 그리고 실제로 다시 안 잡히는지 정산을 돌려 확인한다.
            var clock = new TestClock(T0);
            clock.Advance(TimeSpan.FromDays(2));
            var report = Resolver(clock).Resolve(save, _data);

            Assert.IsEmpty(report.RescueSignals, "놓친 사람의 신호가 다시 잡혔다 — 기회가 무한이 된다");
            Assert.AreEqual(default(DateTimeOffset), scav.SignalAt);
        }

        [Test]
        public void SignalIsPickedUpExactlyOnce()
        {
            var save = NewSave();

            var scav = new ScavState
            {
                Uid = "lost1", Name = "김철수", Tier = 1,
                Search = 4, Combat = 4, Survival = 4,
                Status = ScavStatus.Missing,
                LostAt = T0,
                LostAtMapId = "MYEONGDONG",
                HiredAt = T0,
            };
            save.Scavs.Add(scav);

            var clock = new TestClock(T0);

            // 신호가 잡힐 때까지.
            clock.Advance(RescueSystem.SignalDelay + TimeSpan.FromMinutes(5));
            var first = Resolver(clock).Resolve(save, _data);
            Assert.AreEqual(1, first.RescueSignals.Count, "신호가 안 잡혔다");

            // 곧바로 또 돌려도 두 번 잡히면 안 된다.
            clock.Advance(TimeSpan.FromMinutes(5));
            var second = Resolver(clock).Resolve(save, _data);
            Assert.IsEmpty(second.RescueSignals);
        }

        // ── 시한 ────────────────────────────────────────────────

        [Test]
        public void Window_ActuallyCloses()
        {
            var save = NewSave();
            var scav = Signalled(save, T0);

            var clock = new TestClock(T0);
            clock.Advance(RescueSystem.SignalWindow + TimeSpan.FromHours(1));
            Resolver(clock).Resolve(save, _data);

            Assert.IsFalse(RescueSystem.IsRescuable(scav, clock.UtcNow));
            Assert.AreNotEqual(ScavStatus.Missing, scav.Status,
                "시한이 지났는데 세이브 안에서는 아직 실종 상태로 남아 있다");
        }

        [Test]
        public void Window_StaysOpenUntilItActuallyPasses()
        {
            var save = NewSave();
            var scav = Signalled(save, T0);

            var clock = new TestClock(T0);
            clock.Advance(RescueSystem.SignalWindow - TimeSpan.FromHours(1));
            Resolver(clock).Resolve(save, _data);

            Assert.AreEqual(ScavStatus.Missing, scav.Status, "아직 시한 안인데 놓아버렸다");
            Assert.IsTrue(RescueSystem.IsRescuable(scav, clock.UtcNow));
        }

        /// <summary>
        /// <b>시한은 "언제까지 출발할 수 있는가"이지 "언제까지 돌아와야 하는가"가 아니다.</b>
        ///
        /// <para>구조 파견은 20분에서 3시간이 걸린다. 돌아오는 시각을 기준으로 잡으면,
        /// 시한 끝자락에 제대로 보낸 구조대가 도착하기도 전에 대상이 죽는다 —
        /// 플레이어 입장에서는 아무 잘못도 안 했는데 실패하는 것이다.</para>
        /// </summary>
        [Test]
        public void RescueInFlight_IsNotKilledByTheClock()
        {
            var save = NewSave();
            var scav = Signalled(save, T0);

            // 시한이 끝나기 직전에 출발했고, 도착은 시한 뒤다.
            var departAt = T0 + RescueSystem.SignalWindow - TimeSpan.FromMinutes(10);
            save.Expeditions.Add(new ExpeditionState
            {
                Uid = "rescue1",
                MapId = "MYEONGDONG",
                ScavUids = new List<string> { "helper" },
                DepartedAt = departAt,
                ReturnsAt = departAt + TimeSpan.FromHours(2),
                RescueScavUid = scav.Uid,
                Seed = 7,
            });

            var clock = new TestClock(T0);
            clock.Advance(RescueSystem.SignalWindow + TimeSpan.FromMinutes(30));
            Resolver(clock).Resolve(save, _data);

            Assert.AreEqual(ScavStatus.Missing, scav.Status,
                "데리러 간 구조대가 도착하기 전에 대상을 놓아버렸다");
        }

        /// <summary>
        /// 시한 사건과 구조 복귀가 같은 정산 안에서 만날 수 있다.
        /// 살아 돌아온 사람에게 시한이 닿으면 되살아난 사람을 죽이게 된다.
        /// </summary>
        [Test]
        public void LetGo_DoesNothingToSomeoneAlreadyBroughtHome()
        {
            var save = NewSave();
            var scav = Signalled(save, T0);

            RescueSystem.BringHome(scav);
            Assert.AreEqual(ScavStatus.Injured, scav.Status);

            RescueSystem.LetGo(scav);

            Assert.AreEqual(ScavStatus.Injured, scav.Status,
                "구조에 성공해 돌아온 사람을 시한이 죽였다");
        }
    }
}
