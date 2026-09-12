using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Scav;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 과금 경로가 <b>실제로 닿는가</b> (GDD §11).
    ///
    /// <para><b>왜 이 파일이 생겼나.</b> 지원계약과 보상 광고는 규칙도 화면 설명도 다 있었는데,
    /// <c>Support.Grant</c> 와 <c>RewardedAd.TryConsume</c> 을 부르는 코드가 테스트 말고는
    /// <b>한 군데도 없었다.</b> <c>RewardedAd.Reward</c> 세 값은 선언만 돼 있고 읽는 쪽이 0 곳이었다.
    /// 즉 게임 안에서는 계약이 절대 켜지지 않고, 광고를 봐도 아무 일이 일어나지 않았다 —
    /// 파는 물건을 벽에 적어두고 계산대를 안 만든 셈이다.</para>
    ///
    /// <para>그래서 여기서 검사하는 것은 값이 아니라 <b>경로</b>다: 세션을 통해 눌렀을 때
    /// 세이브가 실제로 바뀌는가.</para>
    /// </summary>
    [TestFixture]
    public class StoreAndRewardTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        // IDataRegistry 로 받는다 — AllRecipes 는 명시적 인터페이스 구현이라
        // 구체 타입(JsonDataRegistry)으로는 보이지 않는다.
        private IDataRegistry _data;

        [OneTimeSetUp]
        public void Load()
        {
            string dir = Path.Combine(Application.streamingAssetsPath, "Data");
            _data = JsonDataRegistry.Load(name => File.ReadAllText(Path.Combine(dir, name)));
        }

        private GameSession NewSession(out TestClock clock)
        {
            clock = new TestClock(T0);
            var session = new GameSession(
                new SaveService(new MemoryFileStore(), new NewtonsoftJsonCodec(), clock), _data, clock);
            session.Store = new DebugStore();
            session.Boot();

            // 부팅 직후의 정산 구간은 길이가 0 이라 아무 사건도 안 일어난다.
            // 1분을 흘려야 고용 시장이 첫 후보를 건다 — 다시 굴릴 대상이 생긴다.
            clock.Advance(TimeSpan.FromMinutes(1));
            session.Tick();

            return session;
        }

        // ── 지원계약 ────────────────────────────────────────────

        [Test]
        public void PurchaseSupport_ActuallyTurnsTheContractOn()
        {
            TestClock clock;
            var session = NewSession(out clock);

            Assert.IsFalse(Support.IsActive(session.Save, clock.UtcNow), "처음엔 계약이 없다");

            bool ok = false;
            session.PurchaseSupport((success, _) => ok = success);

            Assert.IsTrue(ok);
            Assert.IsTrue(Support.IsActive(session.Save, clock.UtcNow),
                "결제가 성공했는데 계약이 안 켜진다 — 계산대가 금고에 연결돼 있지 않다");
        }

        /// <summary>
        /// 계약의 값어치가 실제로 나타나는가. 켜졌다는 표시만 바뀌고 아무것도 안 달라지면
        /// 그건 파는 것이 아니라 속이는 것이다.
        /// </summary>
        [Test]
        public void SupportContract_ShowsUpWhereItWasPromised()
        {
            TestClock clock;
            var session = NewSession(out clock);

            int slotsBefore = AfterSeoul.Factory.FactorySystem.QueueCapacity(session.Save, clock.UtcNow);
            int spaceBefore = session.Save.Warehouse.TotalCapacity;

            session.PurchaseSupport((_, __) => { });

            Assert.AreEqual(slotsBefore + Support.ExtraQueueSlots,
                AfterSeoul.Factory.FactorySystem.QueueCapacity(session.Save, clock.UtcNow),
                "제작 큐 +1칸이 적용되지 않았다");

            // 이쪽이 특히 중요하다. Support.WarehouseBonus 는 있었지만 부르는 코드가 한 군데도
            // 없어서, 화면에 "창고 +40칸"이라고 적어 팔면서 실제로는 한 칸도 안 늘었다.
            Assert.AreEqual(spaceBefore + Support.ExtraWarehouseSlots,
                session.Save.Warehouse.TotalCapacity, "창고 +40칸이 적용되지 않았다");
        }

        /// <summary>
        /// 계약이 끝나면 혜택도 끝나야 한다. 늘어난 칸을 세이브에 담아 두면 만료 뒤에도 남아서,
        /// 돈을 안 내는 사람에게 혜택이 계속 간다.
        /// </summary>
        [Test]
        public void SupportBenefits_ExpireWithTheContract()
        {
            TestClock clock;
            var session = NewSession(out clock);

            int plain = session.Save.Warehouse.TotalCapacity;
            session.PurchaseSupport((_, __) => { });
            Assert.Greater(session.Save.Warehouse.TotalCapacity, plain);

            // DebugStore 는 30일짜리를 준다.
            clock.Advance(TimeSpan.FromDays(31));
            session.Tick();

            Assert.AreEqual(plain, session.Save.Warehouse.TotalCapacity,
                "계약이 끝났는데 칸이 그대로다");
        }

        /// <summary>
        /// <b>상점이 없으면 아무것도 주지 않는다.</b> 개발 편의로 "일단 되는 척"을 기본값에 넣어
        /// 두면 그게 스토어 빌드까지 따라가서 결제 없이 혜택이 들어간다.
        /// </summary>
        [Test]
        public void NullStore_GrantsNothingAndSaysWhy()
        {
            TestClock clock;
            var session = NewSession(out clock);
            session.Store = new NullStore();

            bool ok = true;
            string message = null;
            session.PurchaseSupport((success, m) => { ok = success; message = m; });

            Assert.IsFalse(ok);
            Assert.IsFalse(string.IsNullOrEmpty(message), "실패를 조용히 삼키면 안 된다");
            Assert.IsFalse(Support.IsActive(session.Save, clock.UtcNow));
        }

        // ── 보상 광고 ───────────────────────────────────────────

        /// <summary>
        /// <b>보상 세 종류가 전부 실제로 무언가를 바꾸는가.</b>
        ///
        /// <para>이 테스트가 없으면 열거형에 값을 하나 더 넣고 아무 데도 안 쓰는 일이 또 일어난다 —
        /// 이 프로젝트에서 이미 네 번 일어났다.</para>
        /// </summary>
        [Test]
        public void EveryRewardDoesSomething()
        {
            foreach (RewardedAd.Reward reward in Enum.GetValues(typeof(RewardedAd.Reward)))
            {
                TestClock clock;
                var session = NewSession(out clock);
                Prepare(session, clock);

                string before = Fingerprint(session, reward);

                bool ok = false;
                session.WatchAd(reward, (success, _) => ok = success);
                Assert.IsTrue(ok, reward + " 시청이 실패했다");

                Assert.AreNotEqual(before, Fingerprint(session, reward),
                    reward + " 를 받았는데 세이브가 그대로다 — 이름만 있는 보상이다");
            }
        }

        [Test]
        public void WatchAd_RespectsTheDailyLimit()
        {
            TestClock clock;
            var session = NewSession(out clock);

            for (int i = 0; i < RewardedAd.MaxPerDay; i++)
                session.WatchAd(RewardedAd.Reward.RerollHiringMarket, (_, __) => { });

            bool ok = true;
            session.WatchAd(RewardedAd.Reward.RerollHiringMarket, (success, _) => ok = success);

            Assert.IsFalse(ok, "무제한이면 광고가 곧 주 수입원이 된다");
        }

        /// <summary>
        /// 계약자는 광고 없이 같은 보상을 받는다 (GDD §11 — 운영 편의성 중심).
        ///
        /// <para><c>Support.Benefits</c> 에 그렇게 적혀 있었는데 그걸 구현한 분기가 없었다.
        /// 파는 물건의 설명이 코드와 어긋나면 그건 버그가 아니라 거짓말이다.</para>
        /// </summary>
        [Test]
        public void Contract_SkipsTheAdButKeepsTheDailyLimit()
        {
            TestClock clock;
            var session = NewSession(out clock);

            session.PurchaseSupport((_, __) => { });

            // 상점이 없어도 — 즉 광고를 틀 방법이 없어도 — 보상이 나와야 한다.
            session.Store = new NullStore();

            bool ok = false;
            session.WatchAd(RewardedAd.Reward.RerollHiringMarket, (success, _) => ok = success);

            Assert.IsTrue(ok, "계약 중인데 광고를 못 틀어서 보상이 막혔다");
            Assert.AreEqual(RewardedAd.MaxPerDay - 1,
                RewardedAd.RemainingToday(session.Save, clock.UtcNow),
                "하루 횟수까지 사라지면 그건 편의가 아니라 무제한 판매다");
        }

        /// <summary>광고를 안 봤으면 횟수도 안 깎여야 한다. 실패가 비용이 되면 안 된다.</summary>
        [Test]
        public void FailedAd_CostsNothing()
        {
            TestClock clock;
            var session = NewSession(out clock);
            session.Store = new NullStore();

            int before = RewardedAd.RemainingToday(session.Save, clock.UtcNow);
            session.WatchAd(RewardedAd.Reward.RerollHiringMarket, (_, __) => { });

            Assert.AreEqual(before, RewardedAd.RemainingToday(session.Save, clock.UtcNow));
        }

        // ── 고용 시장 다시 굴리기 ────────────────────────────────

        /// <summary>
        /// 날짜 시드만으로는 다시 굴릴 수가 없다 — 같은 날은 몇 번을 돌려도 같은 후보가 나오도록
        /// 만들어 뒀기 때문이다(리세마라 방지). 굴린 횟수를 섞어야 결과가 달라진다.
        /// </summary>
        [Test]
        public void Reroll_ChangesTheCandidates()
        {
            TestClock clock;
            var session = NewSession(out clock);

            string before = OfferNames(session.Save);
            Assert.IsFalse(string.IsNullOrEmpty(before), "후보가 없으면 굴릴 것도 없다");

            session.Hiring.Reroll(session.Save, _data, clock.UtcNow);

            Assert.AreNotEqual(before, OfferNames(session.Save), "다시 굴렸는데 그대로다");
        }

        /// <summary>굴린 뒤에도 결정론은 그대로여야 한다. 같은 상태에서 같은 결과가 나와야 한다.</summary>
        [Test]
        public void Reroll_StaysDeterministic()
        {
            TestClock a, b;
            var one = NewSession(out a);
            var two = NewSession(out b);

            // 두 세이브의 생성 시각이 같아야 시드가 같다. Boot 이 T0 로 만들어 준다.
            Assert.AreEqual(one.Save.Player.CreatedAt, two.Save.Player.CreatedAt);

            one.Hiring.Reroll(one.Save, _data, a.UtcNow);
            two.Hiring.Reroll(two.Save, _data, b.UtcNow);

            Assert.AreEqual(OfferNames(one.Save), OfferNames(two.Save));
        }

        /// <summary>
        /// 다시 굴린 판의 offerId 가 이전 판과 겹치면, 화면에 남아 있던 옛 버튼이
        /// 새 후보를 고용해 버린다.
        /// </summary>
        [Test]
        public void Reroll_GivesFreshOfferIds()
        {
            TestClock clock;
            var session = NewSession(out clock);

            var before = new HashSet<string>();
            foreach (var o in session.Save.Market.Offers) before.Add(o.OfferId);

            session.Hiring.Reroll(session.Save, _data, clock.UtcNow);

            foreach (var o in session.Save.Market.Offers)
                Assert.IsFalse(before.Contains(o.OfferId), "새 후보가 옛 id 를 물려받았다: " + o.OfferId);
        }

        [Test]
        public void Reroll_ResetsOnTheNextDay()
        {
            TestClock clock;
            var session = NewSession(out clock);

            session.Hiring.Reroll(session.Save, _data, clock.UtcNow);
            Assert.Greater(session.Save.Market.RerollCount, 0);

            clock.Advance(TimeSpan.FromDays(1));
            session.Tick();

            Assert.AreEqual(0, session.Save.Market.RerollCount,
                "안 돌려놓으면 광고를 한 번 본 사람은 그 뒤로 영원히 다른 시드를 쓰게 된다");
        }

        // ── 도구 ────────────────────────────────────────────────

        /// <summary>보상이 닿을 대상을 미리 만들어 둔다. 대상이 없으면 "없다"고만 답하고 끝난다.</summary>
        private void Prepare(GameSession session, TestClock clock)
        {
            var save = session.Save;
            save.Player.Money = 50_000_000;

            // 치료 중인 사람 하나.
            var scav = new ScavState
            {
                Uid = "hurt", Name = "치료중", Tier = 2,
                Search = 4, Combat = 4, Survival = 4,
                Status = ScavStatus.Injured, HiredAt = T0,
            };
            save.Scavs.Add(scav);
            Treatment.TryTreat(save, _data, scav, clock.UtcNow, useSupplies: false);

            // 제작 하나.
            foreach (var recipe in _data.AllRecipes)
            {
                save.Factory.Queue.Add(new CraftJob
                {
                    RecipeId = recipe.Id,
                    StartedAt = clock.UtcNow,
                    CompletesAt = clock.UtcNow.AddHours(4),
                    Seed = 1,
                });
                break;
            }
        }

        /// <summary>보상이 건드리는 자리만 찍어 본다. 전체를 비교하면 무관한 변화에도 통과한다.</summary>
        private static string Fingerprint(GameSession session, RewardedAd.Reward reward)
        {
            var save = session.Save;

            switch (reward)
            {
                case RewardedAd.Reward.RerollHiringMarket:
                    return OfferNames(save);

                case RewardedAd.Reward.SpeedUpCraft:
                {
                    var parts = new List<string>();
                    foreach (var job in save.Factory.Queue) parts.Add(job.CompletesAt.Ticks.ToString());
                    return string.Join(",", parts.ToArray());
                }

                default:
                {
                    var parts = new List<string>();
                    foreach (var s in save.Scavs) parts.Add(s.Uid + ":" + s.RecoversAt.Ticks);
                    return string.Join(",", parts.ToArray());
                }
            }
        }

        private static string OfferNames(GameSave save)
        {
            var names = new List<string>();
            foreach (var o in save.Market.Offers) names.Add(o.Name + "/" + o.Search + o.Combat + o.Survival);
            return string.Join(",", names.ToArray());
        }
    }
}
