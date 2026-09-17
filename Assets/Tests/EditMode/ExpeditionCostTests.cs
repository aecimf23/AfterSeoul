using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Expedition;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 파견비가 팀 구성을 따라가는가, 그리고 사고가 팀을 통째로 지우지 않는가.
    ///
    /// <para>이 둘은 한 쌍이다. 정산은 생존·전투를 <i>합</i>으로 쓰고 회수량은 탐색의
    /// <i>평균</i>으로 쓰기 때문에, 비용이 고정이면 "항상 전원 파견"이 유일한 정답이 된다.
    /// 반대로 비용만 올리고 사고가 전원 피해로 남으면 아무도 팀을 짜지 않는다.</para>
    /// </summary>
    [TestFixture]
    public class ExpeditionCostTests
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

        private static ScavState Add(GameSave save, string uid, long wage, int survival = 4)
        {
            var s = new ScavState
            {
                Uid = uid, Name = uid, Level = 1, Tier = 1,
                Search = 5, Combat = 4, Survival = survival,
                WagePerHour = wage, Status = ScavStatus.Idle, HiredAt = T0,
            };
            s.Equipment[EquipSlot.Weapon] = "MEL01";
            save.Scavs.Add(s);
            return s;
        }

        private static OfflineResolver NewResolver(TestClock clock) =>
            new OfflineResolver(clock, new List<ITimelineSystem> { new ExpeditionSystem() });

        // ── 파견비 ───────────────────────────────────────────────

        /// <summary>
        /// 기준점: 기준 시급짜리 한 명을 보내면 <c>expeditions.json</c> 값 그대로.
        /// 데이터의 회수가치 대비 비율 검증이 이 지점에 걸려 있어서, 여기가 흔들리면
        /// 밸런스 데이터 전체의 뜻이 바뀐다.
        /// </summary>
        [Test]
        public void Cost_BaselineIsOneStandardWageScav()
        {
            var save = NewSave();
            var map = _data.GetMap("MYEONGDONG");
            Add(save, "sc_1", _data.Balance.BaseWagePerHour);

            Assert.AreEqual(
                map.BaseCostWage + map.BaseCostSupply,
                ExpeditionSystem.CostFor(save, _data, map, new[] { "sc_1" }));
            Assert.AreEqual(
                ExpeditionSystem.BaselineCost(map),
                ExpeditionSystem.CostFor(save, _data, map, new[] { "sc_1" }));
        }

        [Test]
        public void Cost_ScalesWithHeadCount()
        {
            var save = NewSave();
            var map = _data.GetMap("MYEONGDONG");
            long wage = _data.Balance.BaseWagePerHour;
            Add(save, "sc_1", wage);
            Add(save, "sc_2", wage);

            long one = ExpeditionSystem.CostFor(save, _data, map, new[] { "sc_1" });
            long two = ExpeditionSystem.CostFor(save, _data, map, new[] { "sc_1", "sc_2" });

            Assert.AreEqual(one * 2, two, "같은 시급 둘이면 정확히 두 배여야 한다");
        }

        [Test]
        public void Cost_ScalesWithWage()
        {
            var save = NewSave();
            var map = _data.GetMap("MYEONGDONG");   // 인건비 20,000 · 보급비 7,000
            long wage = _data.Balance.BaseWagePerHour;
            Add(save, "cheap", wage);
            Add(save, "pricey", wage * 3);

            long cheap = ExpeditionSystem.CostFor(save, _data, map, new[] { "cheap" });
            long pricey = ExpeditionSystem.CostFor(save, _data, map, new[] { "pricey" });

            // 인건비만 3배가 되고 보급비는 머릿수를 따라가므로 그대로다.
            Assert.AreEqual(map.BaseCostWage * 3 + map.BaseCostSupply, pricey);
            Assert.Greater(pricey, cheap, "비싼 스캐브를 보내면 더 비싸야 한다");
        }

        /// <summary>
        /// 없는 uid 를 섞어도 싸지지 않는다. 싸지면 "이상한 값을 넣으면 공짜"가 된다.
        /// </summary>
        [Test]
        public void Cost_UnknownUidDoesNotGoFree()
        {
            var save = NewSave();
            var map = _data.GetMap("MYEONGDONG");
            long baseline = ExpeditionSystem.BaselineCost(map);

            Assert.AreEqual(baseline,
                ExpeditionSystem.CostFor(save, _data, map, new[] { "nope" }));
        }

        [Test]
        public void Cost_EmptyTeamFallsBackToBaseline()
        {
            var save = NewSave();
            var map = _data.GetMap("MYEONGDONG");

            Assert.AreEqual(ExpeditionSystem.BaselineCost(map),
                ExpeditionSystem.CostFor(save, _data, map, new string[0]));
            Assert.AreEqual(ExpeditionSystem.BaselineCost(map),
                ExpeditionSystem.CostFor(save, _data, map, null));
        }

        [Test]
        public void Depart_ChargesExactlyWhatCostForSaid()
        {
            var save = NewSave();
            var map = _data.GetMap("GURO_FACTORY");
            long wage = _data.Balance.BaseWagePerHour;
            Add(save, "sc_1", wage);
            Add(save, "sc_2", wage * 2);

            var team = new[] { "sc_1", "sc_2" };
            long expected = ExpeditionSystem.CostFor(save, _data, map, team);
            long before = save.Player.Money;

            var exp = new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", team, T0);

            Assert.IsNotNull(exp);
            Assert.AreEqual(expected, exp.CostPaid);
            Assert.AreEqual(before - expected, save.Player.Money);
        }

        /// <summary>
        /// 한 명은 보낼 수 있지만 둘은 못 보내는 자금. 차단 판정이 팀 기준 비용을 봐야 한다.
        /// </summary>
        [Test]
        public void BlockReason_UsesTeamCostNotBaseline()
        {
            var map = _data.GetMap("MYEONGDONG");
            long wage = 40000;

            var save = NewSave(money: ExpeditionSystem.BaselineCost(map));
            Add(save, "sc_1", wage);
            Add(save, "sc_2", wage);

            Assert.IsNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { "sc_1" }),
                "한 명은 보낼 수 있어야 한다");
            Assert.IsNotNull(
                ExpeditionSystem.DepartBlockReason(save, _data, "MYEONGDONG", new[] { "sc_1", "sc_2" }),
                "둘이면 두 배라 자금이 모자라야 한다");
        }

        // ── 사고 ─────────────────────────────────────────────────

        /// <summary>
        /// 사고가 난 파견을 여러 번 돌려서 통계를 본다.
        /// 시드는 <c>RngCounter</c> 로 갈아 끼운다 — 출발 시점에 시드가 확정되는 구조라
        /// 세이브를 새로 만들 필요 없이 이것만 바꾸면 다른 결과가 나온다.
        /// </summary>
        private Stats RunMany(string mapId, int teamSize, int survival, int runs)
        {
            var st = new Stats();

            for (int i = 0; i < runs; i++)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.RngCounter = (uint)(i * 2 + 1);

                var team = new string[teamSize];
                for (int k = 0; k < teamSize; k++)
                {
                    team[k] = "sc_" + k;
                    Add(save, team[k], _data.Balance.BaseWagePerHour, survival);
                }

                var system = new ExpeditionSystem();
                var exp = system.Depart(save, _data, mapId, team, T0);
                Assert.IsNotNull(exp, "자금은 충분해야 한다");

                clock.Advance(TimeSpan.FromHours(6));
                var report = NewResolver(clock).Resolve(save, _data);

                Assert.AreEqual(1, report.Expeditions.Count);
                var r = report.Expeditions[0];

                st.Runs++;
                if (r.HadAccident) st.Accidents++;
                st.Injured += r.InjuredScavUids.Count;
                st.Lost += r.LostScavUids.Count;

                int hurt = r.InjuredScavUids.Count + r.LostScavUids.Count;
                if (r.HadAccident)
                {
                    Assert.Greater(hurt, 0, "사고가 났는데 아무도 안 다치면 사고가 아니다");
                    if (hurt < teamSize) st.PartialAccidents++;
                }

                foreach (var s in save.Scavs)
                    if (s.Status == ScavStatus.Dead) st.Dead++;
            }

            return st;
        }

        private sealed class Stats
        {
            public int Runs, Accidents, PartialAccidents, Injured, Lost, Dead;
            public double LossPerRun => (double)Lost / Runs;
        }

        /// <summary>
        /// <b>보고서가 손익을 말해야 한다.</b>
        ///
        /// <para>파견비는 출발할 때 <c>ExpeditionState.CostPaid</c> 에 적히고 있었는데
        /// <b>읽는 곳이 한 군데도 없었다</b>. 회수품은 창고에 쌓이기만 했다. 그래서 화면에는
        /// "무엇을 주워왔다"만 보이고 "남는 장사였나"는 끝내 보이지 않았다 — 넷을 보내면
        /// 비용이 네 배인데 회수량은 그만큼 늘지 않는다는 것도 플레이어는 알 수 없었다.
        /// 이 프로젝트에서 반복된 부류다: 값을 만드는 코드가 있다고 그 값이 읽히는 것은 아니다.</para>
        /// </summary>
        [Test]
        public void TheReport_CarriesWhatItCostAndWhatItBroughtBack()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            save.RngCounter = 11;
            Add(save, "sc_0", _data.Balance.BaseWagePerHour);
            Add(save, "sc_1", _data.Balance.BaseWagePerHour);

            var exp = new ExpeditionSystem()
                .Depart(save, _data, "GURO_FACTORY", new[] { "sc_0", "sc_1" }, T0);
            Assert.IsNotNull(exp);
            Assert.Greater(exp.CostPaid, 0, "파견비가 0 이면 검사할 게 없다");

            clock.Advance(TimeSpan.FromHours(6));
            var r = NewResolver(clock).Resolve(save, _data).Expeditions[0];

            Assert.AreEqual(exp.CostPaid, r.CostPaid, "보고서의 파견비가 실제로 낸 돈과 다르다");

            long expected = 0;
            foreach (var loot in r.Loot)
            {
                var def = _data.GetItem(loot.ItemId);
                expected += (long)System.Math.Floor(AfterSeoul.Inventory.ItemPricing.UnitValue(def) * loot.Count);
            }
            Assert.AreEqual(expected, r.LootValue,
                "보고서의 회수 가치가 실제 회수 목록과 맞지 않는다");
            Assert.AreEqual(r.LootValue - r.CostPaid, r.Net);
        }

        /// <summary>
        /// <b>넘쳐서 버려진 것은 수익이 아니다.</b> 창고가 가득 찬 채로 돌아오면 받은 것이
        /// 없는데도 "얼마어치 벌었다"가 뜨면, 그 화면은 거짓말을 하는 것이다.
        /// </summary>
        [Test]
        public void LootThatOverflowed_IsNotCountedAsEarnings()
        {
            int overflowedRuns = 0;

            for (uint seed = 1; seed <= 20; seed += 2)
            {
                var clock = new TestClock(T0);
                var save = NewSave();
                save.RngCounter = seed;
                save.Warehouse.Capacity = 0;   // 한 칸도 없다
                Add(save, "sc_0", _data.Balance.BaseWagePerHour);

                new ExpeditionSystem().Depart(save, _data, "GURO_FACTORY", new[] { "sc_0" }, T0);

                clock.Advance(TimeSpan.FromHours(6));
                var report = NewResolver(clock).Resolve(save, _data);
                var r = report.Expeditions[0];

                if (report.Overflowed.Count > 0) overflowedRuns++;

                Assert.AreEqual(0, r.Loot.Count, "칸이 없는데 받은 것이 있다");
                Assert.AreEqual(0, r.LootValue, "못 받은 물건을 수익으로 세면 안 된다");
                Assert.Less(r.Net, 0, "빈손으로 돌아왔으면 파견비만큼 손해다");
            }

            Assert.Greater(overflowedRuns, 0, "창고가 0칸인데 한 번도 안 넘쳤다 — 전제가 틀렸다");
        }

        /// <summary>
        /// 사고가 나도 팀 전원이 당하지는 않는다.
        ///
        /// <para>예전에는 사고 판정 한 번에 전원이 굴렀다. 그러면 인원을 늘릴수록 기대 손실이
        /// 사람 수만큼 커져서, 파견비까지 오른 지금은 팀을 짤 이유가 완전히 사라진다.</para>
        /// </summary>
        [Test]
        public void Accident_DoesNotAlwaysWipeTheWholeTeam()
        {
            var st = RunMany("GURO_FACTORY", teamSize: 3, survival: 6, runs: 200);

            Assert.Greater(st.Accidents, 0, "구로에서 200회면 사고가 나야 한다");
            Assert.Greater(st.PartialAccidents, 0,
                $"사고 {st.Accidents}회가 전부 전원 피해였다 — 연쇄가 완화되지 않았다");
        }

        /// <summary>
        /// 첫 스캐브를 데리고 놀 수 있을 만큼은 안전한가.
        ///
        /// <para>명동은 1티어 입문 지역이다. 여기서 파견 한 번에 10% 씩 사람이 사라지면
        /// 평균 10회 만에 25만원짜리 첫 고용이 증발하고, 애착이 쌓일 시간이 없다.</para>
        /// </summary>
        [Test]
        public void LossRate_AtEntryMapIsSurvivable()
        {
            var st = RunMany("MYEONGDONG", teamSize: 1, survival: 4, runs: 400);

            Assert.Less(st.LossPerRun, 0.08,
                $"명동 1인 파견 상실률 {st.LossPerRun:P1} — 입문 지역치고 너무 가혹하다");
            Assert.Greater(st.Injured + st.Lost, 0,
                "400회에 아무 일도 안 일어나면 위험이 없는 것이다");
        }

        /// <summary>
        /// 생존이 높아도 면역은 아니다. GDD §15 가 사건이 되길 바라는 죽음·실종이
        /// 영영 안 일어나면 애착의 반대편이 사라진다.
        /// </summary>
        [Test]
        public void HighSurvival_ReducesLossButNeverToZero()
        {
            var weak = RunMany("GURO_FACTORY", teamSize: 1, survival: 2, runs: 400);
            var tough = RunMany("GURO_FACTORY", teamSize: 1, survival: 12, runs: 400);

            Assert.Less(tough.LossPerRun, weak.LossPerRun,
                $"생존이 높은 쪽이 덜 잃어야 한다 (약 {weak.LossPerRun:P1} / 강 {tough.LossPerRun:P1})");
            Assert.Greater(tough.Lost, 0,
                "생존 12 가 400회 동안 한 번도 안 잃으면 사실상 면역이다");
        }
    }
}
