using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Inventory;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 첫 30분 안내 (GDD §16).
    ///
    /// <para>대본을 재생하지 않고 <b>지금 상태를 보고 다음 할 일을 되묻는</b> 방식이라,
    /// 여기서 볼 것은 "순서를 벗어난 플레이어에게도 말이 되는가"다. 저장된 단계를 하나씩
    /// 넘기는 방식이었다면 이 테스트들이 전부 깨졌을 자리들이다.</para>
    /// </summary>
    [TestFixture]
    public class TutorialTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 0)
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            save.Warehouse.Capacity = 100;
            return save;
        }

        [Test]
        public void EmptyHandedPlayer_IsToldToMakeSomething()
        {
            Assert.AreEqual(TutorialStep.MakeSomething, Tutorial.Current(NewSave(), _data));
        }

        [Test]
        public void WithStuffButNoMoney_IsToldToSell()
        {
            var save = NewSave();
            Warehouse.TryAdd(save.Warehouse, _data, "JUNK16", 3);

            Assert.AreEqual(TutorialStep.SellIt, Tutorial.Current(save, _data));
        }

        [Test]
        public void WithMoneyAndNoOne_IsToldToHire()
        {
            Assert.AreEqual(TutorialStep.HireScav, Tutorial.Current(NewSave(5_000_000), _data));
        }

        [Test]
        public void WithSomeoneIdle_IsToldToDepart()
        {
            var save = NewSave(5_000_000);
            save.Scavs.Add(Scav(ScavStatus.Idle));

            Assert.AreEqual(TutorialStep.Depart, Tutorial.Current(save, _data));
        }

        /// <summary>기다리는 중에 "사람을 고용하세요"가 떠 있으면 안내가 아니라 잔소리다.</summary>
        [Test]
        public void WhileWaiting_NothingElseIsSuggested()
        {
            var save = NewSave(5_000_000);
            save.Scavs.Add(Scav(ScavStatus.OnExpedition));
            save.Expeditions.Add(new ExpeditionState
            {
                Uid = "ex_1", MapId = "MYEONGDONG", DepartedAt = T0,
                ReturnsAt = T0.AddMinutes(20), Resolved = false,
            });

            Assert.AreEqual(TutorialStep.Wait, Tutorial.Current(save, _data));
        }

        [Test]
        public void WhenAQuestCanBeDelivered_ThatComesFirst()
        {
            var save = NewSave(5_000_000);
            save.Scavs.Add(Scav(ScavStatus.Idle));

            var quest = FirstQuest();
            save.Quests.Active.Add(new ActiveQuest { QuestId = quest.Id });
            foreach (var req in quest.Requires)
                if (!string.IsNullOrEmpty(req.ItemId))
                    Warehouse.TryAdd(save.Warehouse, _data, req.ItemId, req.Count);

            Assert.AreEqual(TutorialStep.Deliver, Tutorial.Current(save, _data));
        }

        /// <summary>한 바퀴 돌면 안내가 사라진다 — "건너뛰기"를 따로 만들 필요가 없는 이유다.</summary>
        [Test]
        public void AfterTheFirstDelivery_TheGuideGoesAway()
        {
            var save = NewSave();
            save.Quests.CompletedIds.Add("DQ_HWANG_BOLT_01");

            Assert.AreEqual(TutorialStep.Done, Tutorial.Current(save, _data));
            Assert.IsTrue(Tutorial.IsDone(save, _data));
        }

        /// <summary>
        /// 순서를 벗어나도 말이 돼야 한다. 안내를 무시하고 먼저 사람을 쓴 플레이어에게
        /// "물건을 만드세요"가 떠 있으면, 그 안내는 화면이 아니라 대본을 보고 있는 것이다.
        /// </summary>
        [Test]
        public void PlayingOutOfOrder_StillMakesSense()
        {
            var save = NewSave();
            save.Scavs.Add(Scav(ScavStatus.Idle));   // 돈도 물건도 없이 사람만 있다

            Assert.AreEqual(TutorialStep.Depart, Tutorial.Current(save, _data),
                "사람이 놀고 있으면 내보내라고 해야 한다");
        }

        /// <summary>
        /// 다친 사람만 남았으면 치료하라고 해야 한다.
        ///
        /// <para>이 테스트는 원래 <c>Done</c> 을 기대했다 — "다친 사람뿐이면 권할 게 아니라
        /// 기다릴 일"이라는 주석과 함께. 그런데 <b>기다려도 낫지 않았다.</b> 부상에서 나오는 길이
        /// 게임에 없었기 때문이다. 그래서 안내가 사라진 자리에서 초보자는 그대로 막혔고,
        /// 테스트는 그 막힌 상태를 정답이라고 못 박고 있었다.</para>
        /// </summary>
        [Test]
        public void WithOnlyInjuredPeople_ItPointsAtTreatment()
        {
            var save = NewSave(5_000_000);
            save.Scavs.Add(Scav(ScavStatus.Injured));

            Assert.AreEqual(TutorialStep.Treat, Tutorial.Current(save, _data));
            Assert.AreEqual("인원", Tutorial.TabOf(TutorialStep.Treat));
        }

        /// <summary>치료를 시작했으면 더 재촉하지 않는다. 기다리는 것도 한 수다.</summary>
        [Test]
        public void WhileRecovering_ItSaysToWait()
        {
            var save = NewSave(5_000_000);
            save.Scavs.Add(Scav(ScavStatus.Treating));

            Assert.AreEqual(TutorialStep.Recovering, Tutorial.Current(save, _data));
        }

        /// <summary>
        /// 사고는 순서대로 거치는 칸이 아니라 옆길이다. 번호를 붙이면 "5/8 까지 왔다"처럼 읽혀
        /// 진행도가 거짓이 된다.
        /// </summary>
        [Test]
        public void OffLoopSteps_AreNotNumbered()
        {
            Assert.AreEqual(0, Tutorial.IndexOf(TutorialStep.Treat));
            Assert.AreEqual(0, Tutorial.IndexOf(TutorialStep.Recovering));

            Assert.AreEqual(1, Tutorial.IndexOf(TutorialStep.MakeSomething));
            Assert.AreEqual(Tutorial.TotalSteps, Tutorial.IndexOf(TutorialStep.Deliver),
                "마지막 칸의 번호가 총 칸 수와 같아야 '6/6' 으로 끝난다");
        }

        [Test]
        public void EveryStep_HasAHintAndATab()
        {
            foreach (TutorialStep step in Enum.GetValues(typeof(TutorialStep)))
            {
                if (step == TutorialStep.Done) continue;

                Assert.IsNotEmpty(Tutorial.HintOf(step), step.ToString());
                Assert.IsNotEmpty(Tutorial.TabOf(step), step.ToString() + " 의 목적지");
            }
        }

        private static ScavState Scav(ScavStatus status) => new ScavState
        {
            Uid = "sc_1", Name = "김철수", Search = 4, Combat = 4, Survival = 4,
            Status = status, HiredAt = T0,
        };

        private QuestDef FirstQuest()
        {
            foreach (var q in _data.GetQuestPool("DQP_HWANG"))
            {
                bool simple = true;
                foreach (var req in q.Requires) if (string.IsNullOrEmpty(req.ItemId)) simple = false;
                if (simple) return q;
            }
            Assert.Fail("품목 조건만 있는 의뢰가 없다");
            return null;
        }
    }

    /// <summary>
    /// 실종자 구조 (GDD §15).
    ///
    /// <para>지키려는 것: <b>실종은 삭제가 아니라 사건이다.</b> 다시 데려올 수 있어야 잃는 것이
    /// 무게를 갖고, 헬멧이 사망을 실종으로 강등시키는 것(GDD §7)도 그제서야 의미가 생긴다.
    /// 동시에 <b>기회는 한 번이고 시한이 있다</b> — 아니면 실종은 그냥 지연이 된다.</para>
    /// </summary>
    [TestFixture]
    public class RescueTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave SaveWithMissing(DateTimeOffset lostAt)
        {
            var save = new GameSave
            {
                SavedAt = lostAt,
                Player = new PlayerState { CreatedAt = lostAt, Money = 5_000_000, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = lostAt },
            };
            save.Warehouse.Capacity = 100;
            save.Scavs.Add(new ScavState
            {
                Uid = "sc_lost", Name = "김철수", Search = 5, Combat = 5, Survival = 5,
                Status = ScavStatus.Missing, HiredAt = lostAt,
                LostAt = lostAt, LostAtMapId = "MYEONGDONG",
            });
            return save;
        }

        private ResolveReport Resolve(GameSave save, TestClock clock) =>
            new OfflineResolver(clock, new List<ITimelineSystem> { new RescueSystem() })
                .Resolve(save, _data);

        // ── 무전 ─────────────────────────────────────────────────

        [Test]
        public void TheSignal_TakesAWhileToComeBack()
        {
            var save = SaveWithMissing(T0);
            var clock = new TestClock(T0);

            clock.Advance(RescueSystem.SignalDelay - TimeSpan.FromMinutes(30));
            Resolve(save, clock);

            Assert.AreEqual(default(DateTimeOffset), save.Scavs[0].SignalAt,
                "실종되자마자 잡히면 사고의 무게가 없다");
            Assert.IsFalse(RescueSystem.IsRescuable(save.Scavs[0], clock.UtcNow));
        }

        [Test]
        public void AfterTheDelay_TheSignalIsPickedUp()
        {
            var save = SaveWithMissing(T0);
            var clock = new TestClock(T0);

            clock.Advance(RescueSystem.SignalDelay + TimeSpan.FromMinutes(5));
            var report = Resolve(save, clock);

            Assert.AreNotEqual(default(DateTimeOffset), save.Scavs[0].SignalAt);
            Assert.IsTrue(RescueSystem.IsRescuable(save.Scavs[0], clock.UtcNow));
            CollectionAssert.Contains(report.RescueSignals, "sc_lost");
            Assert.IsFalse(report.IsEmpty, "무전은 시한이 있다 — 빈 보고로 넘어가면 놓친다");
        }

        [Test]
        public void TheSignalIsPickedUpOnlyOnce()
        {
            var save = SaveWithMissing(T0);
            var clock = new TestClock(T0);

            clock.Advance(RescueSystem.SignalDelay + TimeSpan.FromMinutes(5));
            Resolve(save, clock);

            clock.Advance(TimeSpan.FromMinutes(30));
            Assert.AreEqual(0, Resolve(save, clock).RescueSignals.Count);
        }

        /// <summary>시한이 지나면 끝이다. 영영 열려 있으면 급할 게 없어서 사건이 되지 않는다.</summary>
        [Test]
        public void TheSignalExpires()
        {
            var save = SaveWithMissing(T0);
            var clock = new TestClock(T0);

            clock.Advance(RescueSystem.SignalDelay + TimeSpan.FromMinutes(5));
            Resolve(save, clock);

            clock.Advance(RescueSystem.SignalWindow);
            Assert.IsFalse(RescueSystem.IsRescuable(save.Scavs[0], clock.UtcNow));
            Assert.AreEqual(1, RescueSystem.ExpireStaleSignals(save, clock.UtcNow));
            Assert.AreEqual(0, RescueSystem.Rescuable(save, clock.UtcNow).Count);
        }

        [Test]
        public void DeadScavs_NeverSendASignal()
        {
            var save = SaveWithMissing(T0);
            save.Scavs[0].Status = ScavStatus.Dead;

            var clock = new TestClock(T0);
            clock.Advance(TimeSpan.FromDays(2));

            Assert.AreEqual(0, Resolve(save, clock).RescueSignals.Count,
                "죽은 사람은 안 돌아온다 — 그게 사망과 실종을 가르는 전부다");
        }

        // ── 데리러 가기 ──────────────────────────────────────────

        [Test]
        public void ARescue_BringsThemBackInjured()
        {
            var save = SaveWithMissing(T0);
            var clock = new TestClock(T0);
            clock.Advance(RescueSystem.SignalDelay + TimeSpan.FromMinutes(5));
            Resolve(save, clock);

            // 생존이 넉넉한 구조대.
            save.Scavs.Add(new ScavState
            {
                Uid = "sc_a", Name = "구조대", Search = 6, Combat = 6, Survival = 20,
                Status = ScavStatus.Idle, HiredAt = T0,
            });
            save.Scavs[1].Equipment["Weapon"] = "MEL01";

            var exp = new ExpeditionSystem().Depart(
                save, _data, "MYEONGDONG", new[] { "sc_a" }, clock.UtcNow, "sc_lost");
            Assert.IsNotNull(exp, "신호가 살아 있으면 보낼 수 있어야 한다");

            clock.Advance(TimeSpan.FromDays(1));
            var report = new OfflineResolver(clock,
                new List<ITimelineSystem> { new ExpeditionSystem() }).Resolve(save, _data);

            Assert.AreEqual(1, report.Expeditions.Count);
            Assert.AreEqual("sc_lost", report.Expeditions[0].RescueScavUid);
            Assert.IsTrue(report.Expeditions[0].RescueSucceeded);

            Assert.AreEqual(ScavStatus.Injured, save.Scavs[0].Status,
                "멀쩡하게 돌려주면 실종이 그냥 지연이 된다");
            Assert.IsNull(save.Scavs[0].LostAtMapId);
        }

        /// <summary>생존이 모자라면 못 데려온다. 그리고 신호가 끊긴다 — 기회는 한 번이다.</summary>
        [Test]
        public void AFailedRescue_EndsIt()
        {
            var save = SaveWithMissing(T0);
            var clock = new TestClock(T0);
            clock.Advance(RescueSystem.SignalDelay + TimeSpan.FromMinutes(5));
            Resolve(save, clock);

            save.Scavs.Add(new ScavState
            {
                Uid = "sc_a", Name = "약골", Search = 1, Combat = 1, Survival = 1,
                Status = ScavStatus.Idle, HiredAt = T0,
            });
            save.Scavs[1].Equipment["Weapon"] = "MEL01";

            new ExpeditionSystem().Depart(
                save, _data, "MYEONGDONG", new[] { "sc_a" }, clock.UtcNow, "sc_lost");

            clock.Advance(TimeSpan.FromDays(1));
            var report = new OfflineResolver(clock,
                new List<ITimelineSystem> { new ExpeditionSystem() }).Resolve(save, _data);

            Assert.IsFalse(report.Expeditions[0].RescueSucceeded);

            // 이 줄은 원래 Missing 을 기대했다. 그런데 Missing 으로 남겨두면 신호를 거는 쪽이
            // "SignalAt 이 비었으니 아직 안 잡힌 사람"으로 읽어서 <b>다음 정산에 같은 신호가
            // 다시 잡힌다</b> — 될 때까지 보낼 수 있게 된다. 끝은 상태로 남아야 한다.
            Assert.AreEqual(ScavStatus.Dead, save.Scavs[0].Status,
                "실패가 상태로 남지 않으면 신호가 다시 잡혀서 기회가 무한이 된다");

            Assert.IsFalse(RescueSystem.IsRescuable(save.Scavs[0], clock.UtcNow),
                "실패했으면 신호가 끊겨야 한다 — 될 때까지 보낼 수 있으면 시한이 없는 것이다");

            // 그리고 실제로 다시 안 잡히는지 확인한다. 상태만 보고 넘어가면
            // 같은 버그가 다른 경로로 돌아온다.
            clock.Advance(TimeSpan.FromDays(2));
            Assert.IsEmpty(Resolve(save, clock).RescueSignals,
                "놓친 사람의 신호가 다시 잡혔다");
        }

        [Test]
        public void CannotRescue_BeforeTheSignal()
        {
            var save = SaveWithMissing(T0);
            save.Scavs.Add(new ScavState
            {
                Uid = "sc_a", Name = "구조대", Search = 6, Combat = 6, Survival = 20,
                Status = ScavStatus.Idle, HiredAt = T0,
            });
            save.Scavs[1].Equipment["Weapon"] = "MEL01";

            Assert.IsNull(new ExpeditionSystem().Depart(
                save, _data, "MYEONGDONG", new[] { "sc_a" }, T0, "sc_lost"),
                "신호가 안 잡혔는데 데리러 갈 수는 없다");
        }

        /// <summary>깊은 데서 잃었으면 데리러 가기도 어렵다.</summary>
        [Test]
        public void DeeperMaps_AreHarderToRescueFrom()
        {
            MapDef low = null, high = null;
            foreach (var map in _data.AllMaps)
            {
                if (low == null || map.RiskLevel < low.RiskLevel) low = map;
                if (high == null || map.RiskLevel > high.RiskLevel) high = map;
            }
            if (low == null || high == null || low.RiskLevel == high.RiskLevel)
                Assert.Ignore("위험도가 갈리는 지역이 없다");

            Assert.Greater(RescueSystem.RequiredSurvival(high), RescueSystem.RequiredSurvival(low));
        }
    }
}
