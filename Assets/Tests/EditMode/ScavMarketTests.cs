using System;
using System.Collections.Generic;
using NUnit.Framework;
using AfterSeoul.Core;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 고용 시장. 일일 의뢰와 같은 날짜 경계 시스템이라 같은 성질을 요구한다 —
    /// 같은 날은 같은 후보, 재정산해도 안 바뀜, 며칠 치가 밀려도 한 번에 처리.
    /// </summary>
    [TestFixture]
    public class ScavMarketTests
    {
        // KST 2026-09-11 10:00
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave(long money = 0)
        {
            return new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = money, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
        }

        private static OfflineResolver NewResolver(TestClock clock, ScavMarket market)
        {
            return new OfflineResolver(clock, new List<ITimelineSystem> { market });
        }

        // ── 후보 생성 ───────────────────────────────────────────

        [Test]
        public void FirstResolve_OffersCandidates()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            Assert.AreEqual(3, save.Market.Offers.Count, "첫 정산에 후보가 걸려야 한다");
            Assert.AreEqual("2026-09-11", save.Market.ActiveGameDate);

            foreach (var o in save.Market.Offers)
            {
                Assert.IsFalse(string.IsNullOrEmpty(o.OfferId));
                Assert.IsFalse(string.IsNullOrEmpty(o.Name));
                Assert.Greater(o.HireCost, 0);
                Assert.GreaterOrEqual(o.Search, 2, "능력치가 0 이면 안 된다");
                Assert.GreaterOrEqual(o.Combat, 2);
                Assert.GreaterOrEqual(o.Survival, 2);
                Assert.GreaterOrEqual(o.TraitIds.Count, 1);
            }
        }

        /// <summary>
        /// 비특기 능력치에 바닥이 있는가.
        ///
        /// <para>예전에는 탐색 1 짜리 전투형이 나올 수 있었다. 탐색은 회수량을 정하는 값이라
        /// 그런 스캐브는 25만원을 주고 사서 첫 파견에 빈손으로 돌아온다. 개성이 아니라 함정이다.
        /// 하루씩 넘기며 여러 날치 후보를 훑는다 — 한 번 뽑기로는 꼬리를 못 잡는다.</para>
        /// </summary>
        [Test]
        public void Offers_NeverDumpAStatIntoTheGround()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var market = new ScavMarket();
            int checkedCount = 0;

            for (int day = 0; day < 60; day++)
            {
                clock.Advance(TimeSpan.FromDays(1));
                NewResolver(clock, market).Resolve(save, _data);

                foreach (var o in save.Market.Offers)
                {
                    int min = System.Math.Min(o.Search, System.Math.Min(o.Combat, o.Survival));
                    int max = System.Math.Max(o.Search, System.Math.Max(o.Combat, o.Survival));

                    Assert.GreaterOrEqual(min, 2,
                        $"{o.Name}({o.Search}/{o.Combat}/{o.Survival}): 능력치 하나가 바닥까지 떨어졌다");
                    Assert.GreaterOrEqual(min * 6, o.StatTotal,
                        $"{o.Name}({o.Search}/{o.Combat}/{o.Survival}): 최저 능력치가 총합의 1/6 미만");
                    Assert.LessOrEqual(max * 5, o.StatTotal * 3,
                        $"{o.Name}({o.Search}/{o.Combat}/{o.Survival}): 특기가 총합의 60% 초과 — 나머지가 죽는다");
                    checkedCount++;
                }
            }

            Assert.Greater(checkedCount, 100, "표본이 너무 적어 검증이 의미 없다");
        }

        [Test]
        public void Offers_RespectTierStatRange()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            foreach (var o in save.Market.Offers)
            {
                ScavTierDef tier = null;
                foreach (var td in _data.ScavPool.Tiers)
                    if (td.Tier == o.Tier) { tier = td; break; }

                Assert.IsNotNull(tier, $"티어 {o.Tier} 정의가 없다");
                Assert.GreaterOrEqual(o.StatTotal, tier.StatTotalMin,
                    $"{o.Name}: 능력치 합 {o.StatTotal} 이 티어 하한 미만");
                Assert.LessOrEqual(o.StatTotal, tier.StatTotalMax,
                    $"{o.Name}: 능력치 합 {o.StatTotal} 이 티어 상한 초과");
            }
        }

        [Test]
        public void LowLevelPlayer_OnlySeesTierOne()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            save.Player.Level = 1;
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            foreach (var o in save.Market.Offers)
                Assert.AreEqual(1, o.Tier, "레벨 1 에게 상위 티어가 걸리면 안 된다");
        }

        [Test]
        public void Offers_DoNotDuplicateEmployedNames()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            // 풀의 이름 6개 중 4개를 이미 고용한 상태로 만든다.
            foreach (var name in new[] { "김철수", "박영호", "이순자", "정대만" })
                save.Scavs.Add(new ScavState { Uid = "sc_" + name, Name = name });

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, new ScavMarket()).Resolve(save, _data);

            foreach (var o in save.Market.Offers)
                Assert.IsFalse(o.Name == "김철수" || o.Name == "박영호"
                            || o.Name == "이순자" || o.Name == "정대만",
                    $"이미 고용한 이름이 후보로 다시 나왔다: {o.Name}");
        }

        // ── 날짜 경계 ───────────────────────────────────────────

        [Test]
        public void SameDay_OffersAreStable()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var market = new ScavMarket();
            var resolver = NewResolver(clock, market);

            clock.Advance(TimeSpan.FromMinutes(1));
            resolver.Resolve(save, _data);
            string first = Describe(save);

            clock.Advance(TimeSpan.FromHours(6));
            resolver.Resolve(save, _data);

            Assert.AreEqual(first, Describe(save), "같은 날에는 후보가 바뀌면 안 된다");
        }

        [Test]
        public void NextDay_OffersRefresh()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            var resolver = NewResolver(clock, new ScavMarket());

            clock.Advance(TimeSpan.FromMinutes(1));
            resolver.Resolve(save, _data);
            var firstIds = new List<string>();
            foreach (var o in save.Market.Offers) firstIds.Add(o.OfferId);

            clock.Advance(TimeSpan.FromDays(1));
            resolver.Resolve(save, _data);

            Assert.AreEqual("2026-09-12", save.Market.ActiveGameDate);
            foreach (var o in save.Market.Offers)
                Assert.IsFalse(firstIds.Contains(o.OfferId),
                    "날짜가 바뀌면 후보 id 도 새로 나와야 한다 (어제 id 가 되살아나면 안 됨)");
        }

        [Test]
        public void SameDayAndPlayer_IsDeterministic()
        {
            string a = RunFresh();
            string b = RunFresh();
            Assert.AreEqual(a, b, "같은 날·같은 플레이어는 항상 같은 후보여야 한다");
        }

        private string RunFresh()
        {
            var clock = new TestClock(T0);
            var save = NewSave();
            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, new ScavMarket()).Resolve(save, _data);
            return Describe(save);
        }

        [Test]
        public void DifferentPlayers_GetDifferentOffers()
        {
            var clock1 = new TestClock(T0);
            var s1 = NewSave();
            clock1.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock1, new ScavMarket()).Resolve(s1, _data);

            var clock2 = new TestClock(T0);
            var s2 = NewSave();
            s2.Player.CreatedAt = T0.AddSeconds(98765);   // 다른 시각에 시작한 플레이어
            clock2.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock2, new ScavMarket()).Resolve(s2, _data);

            Assert.AreNotEqual(Describe(s1), Describe(s2),
                "플레이어마다 후보가 달라야 한다 (시드에 생성 시각이 들어간다)");
        }

        // ── 고용 ────────────────────────────────────────────────

        [Test]
        public void Hire_DeductsMoneyAndAddsScav()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 5_000_000);
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var offer = save.Market.Offers[0];
            long before = save.Player.Money;

            var scav = market.TryHire(save, offer.OfferId, clock.UtcNow);

            Assert.IsNotNull(scav);
            Assert.AreEqual(before - offer.HireCost, save.Player.Money, "고용비가 차감돼야 한다");
            Assert.AreEqual(1, save.Scavs.Count);
            Assert.AreEqual(offer.Name, save.Scavs[0].Name);
            Assert.AreEqual(offer.Search, save.Scavs[0].Search);
            Assert.AreEqual(offer.WagePerHour, save.Scavs[0].WagePerHour);
            Assert.AreEqual(ScavStatus.Idle, save.Scavs[0].Status);
            Assert.IsTrue(offer.Hired);
        }

        [Test]
        public void Hire_TwiceOnSameOffer_Fails()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 5_000_000);
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var offer = save.Market.Offers[0];
            Assert.IsNotNull(market.TryHire(save, offer.OfferId, clock.UtcNow));

            long after = save.Player.Money;
            Assert.IsNull(market.TryHire(save, offer.OfferId, clock.UtcNow),
                "같은 후보를 두 번 고용하면 안 된다");
            Assert.AreEqual(after, save.Player.Money, "실패했으면 돈이 더 빠지면 안 된다");
            Assert.AreEqual(1, save.Scavs.Count);
        }

        [Test]
        public void Hire_WithoutMoney_ChangesNothing()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 1000);
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var offer = save.Market.Offers[0];
            Assert.IsNull(market.TryHire(save, offer.OfferId, clock.UtcNow));
            Assert.AreEqual(1000, save.Player.Money, "실패 시 차감이 없어야 한다");
            Assert.AreEqual(0, save.Scavs.Count);
            Assert.IsFalse(offer.Hired);
            Assert.IsNotNull(ScavMarket.HireBlockReason(save, offer), "사유를 알려줘야 한다");
        }

        [Test]
        public void HiredScav_SurvivesDayRollover()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 5_000_000);
            var market = new ScavMarket();
            var resolver = NewResolver(clock, market);

            clock.Advance(TimeSpan.FromMinutes(1));
            resolver.Resolve(save, _data);
            market.TryHire(save, save.Market.Offers[0].OfferId, clock.UtcNow);

            clock.Advance(TimeSpan.FromDays(2));
            resolver.Resolve(save, _data);

            Assert.AreEqual(1, save.Scavs.Count, "고용한 사람은 후보 갱신과 무관해야 한다");
            Assert.AreEqual(0, save.Market.Offers.FindAll(o => o.Hired).Count,
                "새 후보 목록에 고용 표시가 남아 있으면 안 된다");
        }

        [Test]
        public void Hire_GivesUniqueUids()
        {
            var clock = new TestClock(T0);
            var save = NewSave(money: 20_000_000);
            var market = new ScavMarket();

            clock.Advance(TimeSpan.FromMinutes(1));
            NewResolver(clock, market).Resolve(save, _data);

            var uids = new HashSet<string>();
            foreach (var offer in new List<ScavOffer>(save.Market.Offers))
            {
                var scav = market.TryHire(save, offer.OfferId, clock.UtcNow);
                if (scav == null) continue;
                Assert.IsTrue(uids.Add(scav.Uid), $"Uid 가 겹쳤다: {scav.Uid}");
            }
            Assert.GreaterOrEqual(uids.Count, 2, "여러 명을 고용할 수 있어야 한다");
        }

        // ── 세션 통합 ───────────────────────────────────────────

        [Test]
        public void Session_HireThenDepart()
        {
            var clock = new TestClock(T0);
            var saves = new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock);
            var session = new GameSession(saves, _data, clock);
            session.Boot();
            session.Save.Player.Money = 5_000_000;

            clock.Advance(TimeSpan.FromMinutes(1));
            session.Tick();

            Assert.Greater(session.Save.Market.Offers.Count, 0, "부팅 후 후보가 있어야 한다");

            var scav = session.Hire(session.Save.Market.Offers[0].OfferId);
            Assert.IsNotNull(scav, "세션 경유 고용이 돼야 한다");

            var exp = session.Depart("MYEONGDONG", new[] { scav.Uid });
            Assert.IsNotNull(exp, "고용한 스캐브를 바로 파견할 수 있어야 한다");
            Assert.AreEqual(ScavStatus.OnExpedition, scav.Status);

            // 복귀까지 돌린다 — Vertical Slice 의 빠진 토막이 이어지는지 확인.
            clock.Advance(TimeSpan.FromHours(2));
            var report = session.Tick();

            Assert.AreEqual(1, report.Expeditions.Count, "파견이 정산돼야 한다");
            Assert.AreNotEqual(ScavStatus.OnExpedition, scav.Status, "스캐브가 돌아와야 한다");
        }

        [Test]
        public void Session_HirePersistsAcrossRestart()
        {
            var clock = new TestClock(T0);
            var files = new MemoryFileStore();
            var codec = new NewtonsoftJsonCodec();

            var session = new GameSession(new SaveService(files, codec, clock), _data, clock);
            session.Boot();
            session.Save.Player.Money = 5_000_000;
            clock.Advance(TimeSpan.FromMinutes(1));
            session.Tick();

            var scav = session.Hire(session.Save.Market.Offers[0].OfferId);
            Assert.IsNotNull(scav);
            session.Suspend();

            // 앱을 껐다 켠다.
            var reopened = new GameSession(new SaveService(files, codec, clock), _data, clock);
            reopened.Boot();

            Assert.AreEqual(1, reopened.Save.Scavs.Count, "고용한 스캐브가 세이브에 남아야 한다");
            Assert.AreEqual(scav.Name, reopened.Save.Scavs[0].Name);
            Assert.AreEqual(scav.WagePerHour, reopened.Save.Scavs[0].WagePerHour);
            Assert.AreEqual(scav.TraitIds.Count, reopened.Save.Scavs[0].TraitIds.Count);
        }

        // ── 헬퍼 ────────────────────────────────────────────────

        private static string Describe(GameSave save)
        {
            var parts = new List<string>();
            foreach (var o in save.Market.Offers)
                parts.Add($"{o.Name}/{o.Tier}/{o.Search}-{o.Combat}-{o.Survival}/{string.Join("+", o.TraitIds)}");
            return string.Join(",", parts);
        }
    }
}
