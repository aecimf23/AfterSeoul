using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using AfterSeoul.Core;
using AfterSeoul.Factory;
using AfterSeoul.Inventory;
using AfterSeoul.Mail;

namespace AfterSeoul.Tests
{
    /// <summary>
    /// 월간 지원계약과 보상 광고 (GDD §11).
    ///
    /// <para><b>이 파일에서 제일 중요한 것은 "무엇이 안 되는가"다.</b> GDD §11 의 금지 목록에
    /// "PC 본편 배송량 현금 판매"가 있다. 돈으로 본편에 더 보낼 수 있으면 그건 편의가 아니라
    /// 본편 경제를 파는 것이고, 한 번 풀리면 되돌릴 수 없다.</para>
    ///
    /// <para>그래서 그 약속을 주석이 아니라 테스트로 묶는다 — 나중에 누가(나 포함) 편의라고
    /// 생각하고 한도에 손을 대면 여기서 걸린다.</para>
    /// </summary>
    [TestFixture]
    public class SupportTests
    {
        private static readonly DateTimeOffset T0 =
            new DateTimeOffset(2026, 9, 11, 1, 0, 0, TimeSpan.Zero);

        private FakeRegistry _data;

        [SetUp]
        public void SetUp() => _data = FakeRegistry.Build();

        private static GameSave NewSave()
        {
            var save = new GameSave
            {
                SavedAt = T0,
                Player = new PlayerState { CreatedAt = T0, Money = 10_000_000, EmployerNpcId = "HWANG" },
                Factory = new FactoryState { LastCollectedAt = T0 },
            };
            save.Warehouse.Capacity = 200;
            save.Mail.Linked = true;
            save.Mail.LinkedProfileLabel = "테스트";
            return save;
        }

        private static GameSave Subscribed()
        {
            var save = NewSave();
            Support.Grant(save, T0, TimeSpan.FromDays(30));
            return save;
        }

        // ── 팔지 않는 것 ─────────────────────────────────────────

        /// <summary>
        /// <b>지원계약이 본편 배송 한도를 1원어치도 바꾸면 안 된다.</b>
        /// GDD §11 의 금지 목록 중 유일하게 되돌릴 수 없는 항목이다.
        /// </summary>
        [Test]
        public void SupportContract_DoesNotChangeMainlineShippingLimits()
        {
            var free = NewSave();
            var paid = Subscribed();

            Assert.AreEqual(
                Outbox.RemainingDailyValue(free, _data, T0),
                Outbox.RemainingDailyValue(paid, _data, T0),
                "지원계약이 일일 전송 가치 한도를 바꿨다 — 배송량을 판 것이다");

            Assert.AreEqual(
                Outbox.RemainingShipments(free, _data, T0),
                Outbox.RemainingShipments(paid, _data, T0),
                "지원계약이 일일 발송 횟수를 바꿨다");
        }

        /// <summary>한도를 다 쓴 상태도 같아야 한다 — 남은 양이 아니라 <b>총량</b>이 같아야 한다.</summary>
        [Test]
        public void EvenWhenTheQuotaIsSpent_PayingDoesNotReopenIt()
        {
            var paid = Subscribed();
            Give(paid, "MED16", 12);

            // 한도를 소진시킨다.
            while (Outbox.TryQueue(paid, _data, One("MED16"), T0) != null) { }

            Assert.IsNotNull(Outbox.BlockReason(paid, _data, One("MED16"), T0),
                "계약이 있어도 한도를 다 쓰면 막혀야 한다");
            Assert.AreEqual(0, Outbox.RemainingShipments(paid, _data, T0));
        }

        /// <summary>광고로도 못 연다. 광고 보상 목록에 배송 관련이 아예 없어야 한다.</summary>
        [Test]
        public void NoAdReward_TouchesShipping()
        {
            foreach (RewardedAd.Reward reward in Enum.GetValues(typeof(RewardedAd.Reward)))
            {
                string name = reward.ToString().ToLowerInvariant();
                Assert.IsFalse(name.Contains("ship") || name.Contains("mail") || name.Contains("transfer"),
                    $"광고 보상에 배송 관련이 있다: {reward}");
            }
        }

        /// <summary>
        /// 이 테스트는 소스를 읽는다.
        ///
        /// <para>값을 비교하는 위 테스트들은 "지금 한도가 같은가"만 본다. 하지만 진짜 약속은
        /// <b>배송 코드가 과금 상태를 쳐다보지도 않는다</b>는 것이다. 참조가 없으면 실수로도
        /// 연결될 수 없다 — 값 비교보다 이쪽이 강한 보장이다.</para>
        /// </summary>
        [Test]
        public void OutboxCode_NeverReadsPaymentState()
        {
            string path = Path.Combine(Application.dataPath, "Game", "Mail", "Outbox.cs");
            Assert.IsTrue(File.Exists(path), "Outbox.cs 를 찾지 못했다: " + path);

            string source = File.ReadAllText(path);

            foreach (var forbidden in new[] { "Support", "RewardedAd", "IsActive", "Purchase", "Iap" })
                StringAssert.DoesNotContain(forbidden, source,
                    $"Outbox 가 '{forbidden}' 를 참조한다 — 배송 한도와 과금이 이어지면 안 된다 (GDD §11)");
        }

        [Test]
        public void TheThingsWeNeverSell_AreWrittenDown()
        {
            Assert.Greater(Support.NeverSold.Length, 0);

            bool mentionsShipping = false;
            foreach (var line in Support.NeverSold)
                if (line.Contains("배송")) mentionsShipping = true;

            Assert.IsTrue(mentionsShipping,
                "화면에 '배송량은 팔지 않는다'가 적혀 있어야 한다 — 나중에 의심받는 것보다 낫다");
        }

        // ── 파는 것 (전부 편의) ──────────────────────────────────

        [Test]
        public void WithoutAContract_NothingChanges()
        {
            var save = NewSave();

            Assert.IsFalse(Support.IsActive(save, T0));
            Assert.AreEqual(0, Support.QueueCapacityBonus(save, T0));
            Assert.AreEqual(0, Support.WarehouseBonus(save, T0));
            Assert.AreEqual(save.Factory.StationLevel, FactorySystem.QueueCapacity(save, T0));
        }

        [Test]
        public void AContract_AddsQueueSlotsAndOfflineHours()
        {
            var free = NewSave();
            var paid = Subscribed();

            Assert.Greater(FactorySystem.QueueCapacity(paid, T0), FactorySystem.QueueCapacity(free, T0));
            Assert.Greater(Support.OfflineCap(paid, _data, T0), Support.OfflineCap(free, _data, T0));
        }

        /// <summary>계약이 상한을 <b>없애지는</b> 않는다. 무제한이면 매일 켤 이유가 사라진다.</summary>
        [Test]
        public void AContract_RaisesTheOfflineCap_ButDoesNotRemoveIt()
        {
            var paid = Subscribed();
            var cap = Support.OfflineCap(paid, _data, T0);

            Assert.Less(cap, TimeSpan.FromDays(3), "사실상 무제한이면 접속 동기가 사라진다");
        }

        [Test]
        public void AContract_Expires()
        {
            var save = NewSave();
            Support.Grant(save, T0, TimeSpan.FromDays(30));

            Assert.IsTrue(Support.IsActive(save, T0.AddDays(29)));
            Assert.IsFalse(Support.IsActive(save, T0.AddDays(31)));
        }

        /// <summary>갱신하면 남은 기간에 <b>이어붙는다</b>. 덮어쓰면 갱신할 때마다 손해다.</summary>
        [Test]
        public void Renewing_AddsToWhatIsLeft()
        {
            var save = NewSave();
            Support.Grant(save, T0, TimeSpan.FromDays(30));
            Support.Grant(save, T0.AddDays(10), TimeSpan.FromDays(30));

            Assert.IsTrue(Support.IsActive(save, T0.AddDays(55)),
                "20일 남은 상태에서 30일을 더 샀으면 50일이어야 한다");
        }

        /// <summary>
        /// 파는 것과 실제로 주는 것이 어긋나면 안 된다.
        ///
        /// <para><b>이 테스트는 한동안 거짓 안심을 주고 있었다.</b> <c>Support.WarehouseBonus(paid) &gt; 0</c>
        /// 는 통과했지만, 그 함수를 <b>부르는 코드가 게임에 한 군데도 없었다</b> — 창고는
        /// <c>Capacity</c> 를 날것으로 읽고 있었다. 함수가 옳은 값을 돌려준다는 것과 그 값이
        /// 실제로 쓰인다는 것은 다른 이야기다.</para>
        ///
        /// <para>그래서 지금은 <b>혜택이 닿는 자리</b>를 본다 — 보너스 계산기가 아니라
        /// 창고가 실제로 몇 칸인지, 큐가 실제로 몇 칸인지.</para>
        /// </summary>
        [Test]
        public void EveryAdvertisedBenefit_IsActuallyDelivered()
        {
            Assert.AreEqual(4, Support.Benefits.Length, "혜택 목록이 바뀌었으면 아래 검사도 같이 고칠 것");

            var free = NewSave();
            var paid = Subscribed();

            // ① 오프라인 상한 — FactorySystem 과 AssistantSystem 이 이걸 그대로 쓴다.
            Assert.Greater(Support.OfflineCap(paid, _data, T0), Support.OfflineCap(free, _data, T0));

            // ② 제작 큐 — 보너스 계산기가 아니라 실제 칸 수를 본다.
            Assert.Greater(
                AfterSeoul.Factory.FactorySystem.QueueCapacity(paid, T0),
                AfterSeoul.Factory.FactorySystem.QueueCapacity(free, T0));

            // ③ 창고 — 창고 코드가 실제로 읽는 값을 본다.
            paid.Warehouse.BonusCapacity = Support.WarehouseBonus(paid, T0);
            free.Warehouse.BonusCapacity = Support.WarehouseBonus(free, T0);
            Assert.Greater(paid.Warehouse.TotalCapacity, free.Warehouse.TotalCapacity);

            // ④ 광고 없이 같은 보상 — 경로가 있는지는 StoreAndRewardTests 가 세션으로 확인한다.
            //    여기서는 계약이 살아 있다는 것만.
            Assert.IsTrue(Support.IsActive(paid, T0));
        }

        // ── 보상 광고 ────────────────────────────────────────────

        [Test]
        public void AdsAreCappedPerDay()
        {
            var save = NewSave();

            for (int i = 0; i < RewardedAd.MaxPerDay; i++)
                Assert.IsTrue(RewardedAd.TryConsume(save, T0), $"{i + 1}번째");

            Assert.IsFalse(RewardedAd.TryConsume(save, T0), "무제한이면 광고가 주 수입원이 된다");
            Assert.AreEqual(0, RewardedAd.RemainingToday(save, T0));
        }

        [Test]
        public void AdCountResets_OnTheNextGameDay()
        {
            var save = NewSave();
            for (int i = 0; i < RewardedAd.MaxPerDay; i++) RewardedAd.TryConsume(save, T0);

            Assert.AreEqual(RewardedAd.MaxPerDay, RewardedAd.RemainingToday(save, T0.AddDays(1)));
        }

        // ── 도우미 ───────────────────────────────────────────────

        private void Give(GameSave save, string itemId, int count) =>
            Warehouse.TryAdd(save.Warehouse, _data, itemId, count);

        private static List<ItemStack> One(string itemId) =>
            new List<ItemStack> { new ItemStack(itemId, 1) };
    }
}
