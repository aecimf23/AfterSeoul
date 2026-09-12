using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 복귀 알림 (GDD §31).
    ///
    /// <para>"무엇을 언제 알릴 것인가"는 게임 규칙이라 Core 에 있고, 그래서 실기기 없이
    /// 검증된다. 안드로이드 API 뒤에 숨겨 뒀다면 폰을 꺼내야만 확인할 수 있었을 것이다.</para>
    /// </summary>
    [TestFixture]
    public class NotificationPlanTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 10_000_000)
        {
            return new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
        }

        private static ScavState Add(GameSave save, string uid, string name)
        {
            var s = new ScavState
            {
                Uid = uid, Name = name, Search = 5, Combat = 4, Survival = 5,
                WagePerHour = 40000, Status = ScavStatus.Idle, HiredAt = T0,
            };
            s.Equipment[EquipSlot.Weapon] = "MEL01";
            save.Scavs.Add(s);
            return s;
        }

        [Test]
        public void Empty_WhenNothingIsPending()
        {
            Assert.AreEqual(0, NotificationPlan.Build(NewSave(), _data, T0).Count);
        }

        [Test]
        public void Expedition_IsScheduledAtItsReturnTime()
        {
            var save = NewSave();
            var scav = Add(save, "sc_1", "정대만");
            var exp = new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { scav.Uid }, T0);

            var plan = NotificationPlan.Build(save, _data, T0);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(exp.ReturnsAt, plan[0].At);
            StringAssert.Contains("정대만", plan[0].Body, "누가 돌아오는지 이름이 있어야 연다");
        }

        /// <summary>이미 정산된 파견은 알리지 않는다. 예약하면 즉시 울린다.</summary>
        [Test]
        public void ResolvedOrPastExpeditions_AreNotScheduled()
        {
            var save = NewSave();
            var scav = Add(save, "sc_1", "정대만");
            var exp = new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { scav.Uid }, T0);

            // 이미 복귀 시각이 지났다
            Assert.AreEqual(0, NotificationPlan.Build(save, _data, exp.ReturnsAt).Count);
            Assert.AreEqual(0, NotificationPlan.Build(save, _data, exp.ReturnsAt.AddHours(1)).Count);

            // 정산까지 끝났다
            exp.Resolved = true;
            Assert.AreEqual(0, NotificationPlan.Build(save, _data, T0).Count);
        }

        [Test]
        public void MultiScavTeam_NamesOneAndCountsTheRest()
        {
            var save = NewSave();
            var a = Add(save, "sc_a", "정대만");
            var b = Add(save, "sc_b", "김철수");
            new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { a.Uid, b.Uid }, T0);

            var body = NotificationPlan.Build(save, _data, T0)[0].Body;
            StringAssert.Contains("정대만", body);
            StringAssert.Contains("1명", body, "나머지 인원 수가 나와야 한다");
        }

        [Test]
        public void CraftJobs_AreScheduledToo()
        {
            var save = NewSave();
            save.Factory.Queue.Add(new CraftJob
            {
                RecipeId = "RCP_BOLT",
                StartedAt = T0,
                CompletesAt = T0.AddHours(1),
                Seed = 7,
            });

            var plan = NotificationPlan.Build(save, _data, T0);
            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(T0.AddHours(1), plan[0].At);
        }

        [Test]
        public void CollectedCraftJobs_AreNotScheduled()
        {
            var save = NewSave();
            save.Factory.Queue.Add(new CraftJob
            {
                RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = T0.AddHours(1), Collected = true,
            });

            Assert.AreEqual(0, NotificationPlan.Build(save, _data, T0).Count);
        }

        [Test]
        public void SortedByTime_SoTheCapCutsTheFurthestAway()
        {
            var save = NewSave();
            for (int i = 0; i < NotificationPlan.MaxScheduled + 4; i++)
            {
                save.Factory.Queue.Add(new CraftJob
                {
                    RecipeId = "RCP_BOLT",
                    StartedAt = T0,
                    // 뒤에서부터 넣어서 정렬이 실제로 동작하는지 본다
                    CompletesAt = T0.AddHours(NotificationPlan.MaxScheduled + 4 - i),
                });
            }

            var plan = NotificationPlan.Build(save, _data, T0);

            Assert.AreEqual(NotificationPlan.MaxScheduled, plan.Count, "상한을 넘으면 안 된다");
            for (int i = 1; i < plan.Count; i++)
                Assert.LessOrEqual(plan[i - 1].At, plan[i].At, "가까운 것부터여야 한다");
            Assert.AreEqual(T0.AddHours(1), plan[0].At, "잘려나가는 건 먼 미래의 일이어야 한다");
        }

        /// <summary>같은 상태면 같은 계획. 백그라운드로 갈 때마다 다시 짜도 흔들리지 않는다.</summary>
        [Test]
        public void SameState_GivesSamePlan()
        {
            var save = NewSave();
            var scav = Add(save, "sc_1", "정대만");
            new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { scav.Uid }, T0);
            save.Factory.Queue.Add(new CraftJob
            {
                RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = T0.AddMinutes(5),
            });

            string Describe(List<PlannedNotification> p)
            {
                var parts = new List<string>();
                foreach (var n in p) parts.Add($"{n.Key}@{n.At.ToUnixTimeSeconds()}|{n.Body}");
                return string.Join(";", parts.ToArray());
            }

            Assert.AreEqual(
                Describe(NotificationPlan.Build(save, _data, T0)),
                Describe(NotificationPlan.Build(save, _data, T0)));
        }

        /// <summary>키가 사건마다 달라야 예약을 구분할 수 있다.</summary>
        [Test]
        public void KeysAreUnique()
        {
            var save = NewSave();
            var a = Add(save, "sc_a", "정대만");
            var b = Add(save, "sc_b", "김철수");
            new ExpeditionSystem().Depart(save, _data, "MYEONGDONG", new[] { a.Uid }, T0);
            new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { b.Uid }, T0);

            var seen = new HashSet<string>();
            foreach (var n in NotificationPlan.Build(save, _data, T0))
                Assert.IsTrue(seen.Add(n.Key), $"키가 겹친다: {n.Key}");
            Assert.AreEqual(2, seen.Count);
        }

        // ── 묶기 ─────────────────────────────────────────────────

        /// <summary>
        /// 비슷한 때 끝나는 일은 한 번만 울린다.
        ///
        /// <para>파견 셋을 같이 보내면 거의 같은 시각에 돌아온다. 그때 진동이 세 번 울리면
        /// 사람은 내용을 읽는 게 아니라 알림을 꺼버린다.</para>
        /// </summary>
        [Test]
        public void ThingsFinishingTogether_RingOnce()
        {
            var save = NewSave();
            for (int i = 0; i < 3; i++)
                save.Factory.Queue.Add(new CraftJob
                {
                    RecipeId = "RCP_BOLT", StartedAt = T0,
                    CompletesAt = T0.AddHours(2).AddMinutes(i * 2),   // 2분 간격
                });

            var plan = NotificationPlan.Build(save, _data, T0);

            Assert.AreEqual(1, plan.Count, "2분 간격 셋이 따로 울리면 그건 알림이 아니라 소음이다");
            StringAssert.Contains("2건", plan[0].Body, "몇 건이 더 있는지 적혀야 한다");
        }

        /// <summary>묶은 알림은 <b>마지막 것</b>에 맞춘다. 먼저 울리면 안 끝난 일을 끝났다고 말하게 된다.</summary>
        [Test]
        public void AMergedNotification_WaitsForTheLastOne()
        {
            var save = NewSave();
            var last = T0.AddHours(2).AddMinutes(6);

            save.Factory.Queue.Add(new CraftJob
                { RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = T0.AddHours(2) });
            save.Factory.Queue.Add(new CraftJob
                { RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = last });

            var plan = NotificationPlan.Build(save, _data, T0);

            Assert.AreEqual(1, plan.Count);
            Assert.AreEqual(last, plan[0].At);
        }

        /// <summary>충분히 떨어져 있으면 그대로 둔다 — 묶는 것 자체가 목적이 아니다.</summary>
        [Test]
        public void ThingsFarApart_StayApart()
        {
            var save = NewSave();
            save.Factory.Queue.Add(new CraftJob
                { RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = T0.AddHours(2) });
            save.Factory.Queue.Add(new CraftJob
                { RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = T0.AddHours(5) });

            Assert.AreEqual(2, NotificationPlan.Build(save, _data, T0).Count);
        }

        // ── 새벽 ─────────────────────────────────────────────────

        /// <summary>
        /// 자는 동안에는 울리지 않는다. <b>미루는 것이지 버리는 것이 아니다</b> —
        /// 새벽에 끝난 일도 일어나면 알아야 한다.
        /// </summary>
        [Test]
        public void NothingRingsWhileYouAreAsleep()
        {
            var save = NewSave();

            // KST 새벽 3시에 끝나도록 잡는다 (T0 = KST 10:00).
            var threeAm = new DateTimeOffset(2026, 9, 12, 3, 0, 0, GameTime.GameZoneOffset);
            save.Factory.Queue.Add(new CraftJob
                { RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = threeAm.ToUniversalTime() });

            var plan = NotificationPlan.Build(save, _data, T0);

            Assert.AreEqual(1, plan.Count, "미뤄야지 버리면 안 된다");

            int hour = plan[0].At.ToOffset(GameTime.GameZoneOffset).Hour;
            Assert.AreEqual(NotificationPlan.QuietEndHourKst, hour, $"새벽에 울린다 (KST {hour}시)");
        }

        /// <summary>아침으로 민 것들이 한 시각에 뭉치므로, 민 뒤에 한 번 더 묶여야 한다.</summary>
        [Test]
        public void ThingsPushedToMorning_AreMergedAgain()
        {
            var save = NewSave();
            var night = new DateTimeOffset(2026, 9, 12, 2, 0, 0, GameTime.GameZoneOffset);

            for (int i = 0; i < 3; i++)
                save.Factory.Queue.Add(new CraftJob
                {
                    RecipeId = "RCP_BOLT", StartedAt = T0,
                    CompletesAt = night.AddHours(i).ToUniversalTime(),   // 새벽 2·3·4시
                });

            Assert.AreEqual(1, NotificationPlan.Build(save, _data, T0).Count,
                "아침 7시에 세 번 울리면 민 의미가 없다");
        }

        /// <summary>깨어 있는 시간에 끝나는 것은 건드리지 않는다.</summary>
        [Test]
        public void DaytimeNotifications_AreLeftAlone()
        {
            var save = NewSave();
            var noon = new DateTimeOffset(2026, 9, 12, 12, 0, 0, GameTime.GameZoneOffset);
            save.Factory.Queue.Add(new CraftJob
                { RecipeId = "RCP_BOLT", StartedAt = T0, CompletesAt = noon.ToUniversalTime() });

            Assert.AreEqual(noon.ToUniversalTime(), NotificationPlan.Build(save, _data, T0)[0].At);
        }

        // ── 무전 ─────────────────────────────────────────────────

        /// <summary>실종자 무전은 시한이 있다 — 알림 한 자리를 쓸 값어치가 있는 소식이다.</summary>
        [Test]
        public void AMissingScavSignal_IsWorthANotification()
        {
            var save = NewSave();
            save.Scavs.Add(new ScavState
            {
                Uid = "sc_lost", Name = "김철수", Status = ScavStatus.Missing,
                HiredAt = T0, LostAt = T0, LostAtMapId = "MYEONGDONG",
            });

            var plan = NotificationPlan.Build(save, _data, T0);

            Assert.AreEqual(1, plan.Count);
            StringAssert.Contains("김철수", plan[0].Body);
            Assert.AreEqual(T0 + RescueSystem.SignalDelay, plan[0].At);
        }

        [Test]
        public void AnAlreadyPickedUpSignal_IsNotScheduledAgain()
        {
            var save = NewSave();
            save.Scavs.Add(new ScavState
            {
                Uid = "sc_lost", Name = "김철수", Status = ScavStatus.Missing,
                HiredAt = T0, LostAt = T0, LostAtMapId = "MYEONGDONG",
                SignalAt = T0.AddHours(6),
            });

            Assert.AreEqual(0, NotificationPlan.Build(save, _data, T0).Count);
        }
    }
}
